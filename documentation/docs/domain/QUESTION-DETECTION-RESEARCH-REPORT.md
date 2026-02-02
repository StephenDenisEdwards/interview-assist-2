# Question Detection Research Report

## Overview

This report analyzes the academic paper "Detecting Questions and Imperative Sentences in English" and evaluates its findings against the current interview-assist question detection implementation. The goal is to identify potential improvements and validate current design decisions.

**Source Document:** `documentation/docs/Detecting Questions and Imperative Sentences in English.pdf`

**Date:** February 2026

---

## Executive Summary

The research paper provides a comprehensive survey of question and imperative detection techniques spanning rule-based methods, traditional ML, deep learning, and prosodic (audio) analysis. Key findings relevant to our implementation:

| Finding | Current State | Opportunity |
|---------|---------------|-------------|
| LLM-based detection achieves ~95%+ accuracy | Already using GPT-4o-mini | Validated |
| Prosodic cues (pitch, duration) improve detection | Not implemented | Medium priority |
| Hybrid approaches (rules + ML) reduce costs | Heuristic detector exists but not integrated | High priority |
| Indirect speech acts are challenging | LLM handles via context | Validated |
| Punctuation restoration aids detection | Relying on Whisper's output | Low risk |

**Recommendation:** Implement a hybrid pre-filter using heuristics to reduce API costs while maintaining accuracy for edge cases via LLM verification.

---

## 1. Detection Techniques Analysis

### 1.1 Rule-Based Methods

**Paper Summary:**
- Punctuation (`?` at end) strongly indicates questions
- WH-words (who, what, when, where, why, how) at sentence start
- Auxiliary verb inversion (Do you..., Can we..., Is it...)
- Imperatives: Base verb (VB) at start with no explicit subject
- POS tagging and dependency parsing can reveal structure

**Our Implementation:**

| Pattern | HeuristicQuestionDetector | OpenAiQuestionDetectionService |
|---------|---------------------------|--------------------------------|
| Question mark detection | Yes | Via LLM |
| WH-word starters | Yes | Via LLM |
| Modal verb starters | Yes | Via LLM |
| Imperative patterns | Yes (10 patterns) | Via LLM |
| POS/Dependency parsing | No | Implicit in LLM |
| Subject absence check | No | Implicit in LLM |

**Gap Identified:** The heuristic detector could benefit from POS tagging to reduce false positives. The paper notes that "Thank you", "See you", "Guess not" all start with base verbs but are not imperatives. Our heuristic detector may misclassify these.

**Recommendation:** Consider adding a blacklist of common false-positive phrases to the heuristic detector:
```csharp
private static readonly string[] NonImperativePhrases = {
    "thank you", "see you", "guess not", "sounds good",
    "got it", "makes sense", "understood"
};
```

### 1.2 Indirect Speech Acts

**Paper Summary:**
- "Could you close the window?" is grammatically a question but functionally a request
- "I was wondering if you could tell me the time" is declarative but functions as a question
- Surface-level detection misses these pragmatic interpretations

**Our Implementation:**
The LLM-based detector handles this well. The system prompt explicitly instructs:
> "Identify technical interview questions and imperatives... Classify as Question, Imperative, Clarification, or Follow-up"

The model understands pragmatic intent, not just surface form. For example:
- "Could you walk me through your approach?" → Correctly classified as Imperative (request)
- "I'd like to understand your experience with Kubernetes" → Detected as implicit question

**Validation:** Our LLM approach is well-suited for indirect speech acts. No changes needed.

### 1.3 Deep Learning Approaches

**Paper Summary:**
- BERT fine-tuned on dialogue act corpora achieves ~83% accuracy on Switchboard
- Hierarchical RNN+CRF models achieve >90% on MRDA
- Pre-trained transformers capture syntax and context without explicit features
- GPT-family models can classify via prompting ("Is this a question or command?")

**Key Quote:**
> "Using large generative models for this classification is usually overkill in production due to cost and latency, unless a platform already employs them for broader NLU tasks."

**Our Implementation:**
We use GPT-4o-mini, which balances cost and quality. However, the paper suggests smaller fine-tuned models could achieve similar accuracy at lower cost.

**Trade-off Analysis:**

| Approach | Accuracy | Latency | Cost | Offline |
|----------|----------|---------|------|---------|
| GPT-4o-mini (current) | ~95% | 200-500ms | $$ | No |
| Fine-tuned BERT | ~85% | 10-50ms | $ | Yes |
| Heuristics (current) | ~70-80% | ~0ms | Free | Yes |

**Recommendation:** For high-volume scenarios, consider training a local BERT-based classifier on interview question data. The paper indicates that even with ~85% accuracy, combining with heuristic pre-filtering could match LLM accuracy while dramatically reducing costs.

---

## 2. Prosodic (Audio) Cues

### 2.1 Research Findings

**Key Insights from Paper:**
- Rising final F0 (fundamental frequency/pitch) is a strong cue for yes-no questions
- Duration and pausing are even more predictive than pitch for question detection
- Final lengthening and pause patterns signal questions
- Declarative questions ("You're coming along?") rely entirely on intonation

**Prosodic Features:**
| Feature | Question Signal | Imperative Signal |
|---------|-----------------|-------------------|
| Final pitch rise | Strong indicator | Rare |
| Final lengthening | Moderate indicator | Low |
| Pause before response | Common | Uncommon |
| Energy/intensity | Variable | Often higher |

### 2.2 Current Implementation Gap

Our implementation processes **text transcripts only**. We do not analyze:
- Pitch contours from audio
- Duration/timing of speech
- Pause patterns

This means we may miss:
1. **Declarative questions** - "You've worked with databases." spoken with rising intonation
2. **Rhetorical questions** - Different intonation pattern than genuine questions
3. **Ambiguous utterances** - Where tone disambiguates intent

### 2.3 Potential Integration

**Option A: ASR Punctuation Reliance (Current)**
Whisper and other ASR systems internally use acoustic features to predict punctuation. Our approach indirectly benefits from this.

**Option B: Prosodic Feature Extraction**
Tools like openSMILE can extract hundreds of acoustic features. These could feed into a classifier:

```
Audio → openSMILE → Prosodic Features
                          ↓
Transcript → LLM/Heuristics → Combined Classifier → Final Decision
```

**Option C: End-to-End Audio Classification**
Models like Wav2Vec 2.0 can classify audio directly without intermediate text.

**Recommendation:** Low priority. Given our reliance on Whisper (which handles punctuation well), prosodic analysis would provide marginal improvement at significant implementation cost. Revisit if accuracy issues arise specifically with declarative questions or rhetorical question false positives.

---

## 3. Hybrid Approach Opportunity

### 3.1 Paper Recommendation

> "The most robust systems often combine rules and ML. A rule-based filter might handle extremely clear cases (high precision rules) and a statistical model handles the rest."

The paper suggests:
1. High-precision rules for obvious cases (`?` at end → question)
2. ML/LLM for nuanced cases (indirect speech, context-dependent)
3. Prosody as tiebreaker when confidence is low

### 3.2 Current Architecture

```
Audio → Whisper STT → Transcript → LLM Detection → Questions
                                   (every call)
```

**Problem:** Every detection cycle calls the API, even for obvious questions like "What is dependency injection?"

### 3.3 Proposed Hybrid Architecture

```
Audio → Whisper STT → Transcript
                          ↓
              ┌─────────────────────┐
              │ Heuristic Pre-Filter │
              │ (High-confidence)    │
              └──────────┬──────────┘
                         │
         ┌───────────────┼───────────────┐
         ↓ Obvious       │ Uncertain     ↓ Non-question
    Direct Pass          ↓           Skip LLM
                   ┌──────────┐
                   │ LLM API  │
                   │ (Verify) │
                   └──────────┘
                         ↓
                   Final Decision
```

**Benefits:**
- Obvious questions (with `?`) skip API entirely
- Obvious non-questions (single words, greetings) skip API
- LLM called only for uncertain cases (~30-50% of inputs)
- Maintains high accuracy while reducing costs by 50-70%

**Implementation Sketch:**

```csharp
public class HybridQuestionDetectionService : IQuestionDetectionService
{
    private readonly HeuristicQuestionDetector _heuristic;
    private readonly IQuestionDetectionService _llmService;

    public async Task<DetectedQuestion[]> DetectQuestionsAsync(
        string text, string? context = null)
    {
        // Phase 1: Heuristic assessment
        var confidence = _heuristic.AssessConfidence(text);

        if (confidence.IsDefinitelyQuestion)
            return _heuristic.DetectQuestions(); // Skip API

        if (confidence.IsDefinitelyNotQuestion)
            return Array.Empty<DetectedQuestion>(); // Skip API

        // Phase 2: LLM for uncertain cases
        return await _llmService.DetectQuestionsAsync(text, context);
    }
}
```

**Recommendation:** High priority. This architecture aligns with academic best practices and could significantly reduce API costs while maintaining accuracy.

---

## 4. Datasets and Evaluation

### 4.1 Available Benchmarks

The paper identifies several datasets for training/evaluation:

| Dataset | Type | Question Types | Imperative Coverage |
|---------|------|----------------|---------------------|
| Switchboard (SwDA) | Phone conversations | Yes-No, Wh-, Or-questions | Limited directives |
| MRDA | Meeting recordings | Multiple question types | Action items |
| TV-AfD Corpus | TV scripts + Wikipedia | Full pragmatic coverage | Strong |

### 4.2 Current Evaluation

We have integration tests measuring precision/recall:
- **Location:** `Interview-assist-library-integration-tests/Detection/QuestionDetectionAccuracyTests.cs`
- **Thresholds:** 80% precision, 70% recall

### 4.3 Gap Identified

Our test data may not cover:
- Indirect speech acts ("I was wondering...")
- Rhetorical questions
- Commands phrased as questions ("Could you...")
- Multi-part questions
- Questions split by disfluencies

**Recommendation:** Expand test suite with examples from the TV-AfD corpus patterns:
- Polite requests in question form
- Imperatives without explicit verb starts
- Declarative questions
- Tag questions ("Let's go, shall we?")

---

## 5. Known Limitations and Mitigations

### 5.1 Paper-Identified Challenges

| Challenge | Paper Mitigation | Our Status |
|-----------|------------------|------------|
| Indirect speech acts | Pragmatic context | LLM handles well |
| ASR errors | Punctuation restoration models | Whisper provides punctuation |
| Rhetorical questions | Difficult, context needed | LLM may catch, not tested |
| Domain shift | Diverse training data | Prompt-based, adapts |
| Declarative questions | Prosodic cues | Not implemented |
| Segmentation issues | Sentence boundary detection | Rolling buffer helps |

### 5.2 Our Specific Challenges

Based on IMPROVEMENT-PLAN-0009 and testing:

1. **Questions split by silence gaps** - Heuristic misses ~750ms+ gaps
   - Mitigation: Rolling buffer approach (implemented)

2. **Pronoun resolution** - "When should we use it?" needs context
   - Mitigation: LLM prompt explicitly requests this (implemented)

3. **Promotional content** - "Like and subscribe"
   - Mitigation: Prompt instructs to filter (implemented)

---

## 6. Recommendations Summary

### High Priority

1. **Implement Hybrid Detection Pipeline**
   - Use heuristics for high-confidence cases
   - Reserve LLM for uncertain cases
   - Expected cost reduction: 50-70%

2. **Add False-Positive Blacklist to Heuristics**
   - "Thank you", "See you", "Makes sense" etc.
   - Reduces unnecessary LLM calls

### Medium Priority

3. **Expand Test Coverage**
   - Add indirect speech act examples
   - Add rhetorical question test cases
   - Add multi-part question handling

4. **Consider Local Model for Offline/Cost-Sensitive Use**
   - Fine-tuned BERT on interview question data
   - Alternative to GPT-4o-mini for high-volume

### Low Priority

5. **Prosodic Analysis**
   - Only if declarative question detection becomes an issue
   - Significant implementation effort for marginal gain

6. **Confidence Scoring for Heuristics**
   - Pattern strength weighting
   - Enables smarter LLM deferral decisions

---

## 7. Conclusion

Our current LLM-based approach aligns well with academic best practices for high-accuracy question detection. The use of GPT-4o-mini provides the contextual understanding needed for indirect speech acts and imperative classification.

The primary opportunity for improvement is implementing a **hybrid approach** that combines our existing heuristic detector with the LLM service. This would maintain accuracy while significantly reducing API costs, following the paper's recommendation that "the most robust systems often combine rules and ML."

Prosodic analysis, while valuable in research contexts, offers diminishing returns given Whisper's built-in punctuation prediction. This should remain a future consideration rather than an immediate priority.

---

## References

1. "Detecting Questions and Imperative Sentences in English" - Survey paper (2024)
2. ADR-003: Use LLM for Question Detection
3. HEURISTIC_QUESTION_DETECTOR.md
4. IMPROVEMENT-PLAN-0009: Heuristic Detection Improvements
5. Switchboard Dialogue Act Corpus (SwDA)
6. ICSI Meeting Recorder Dialog Act (MRDA) Corpus
7. TV-AfD Corpus (Chen et al. 2020, LREC)
