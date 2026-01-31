# IMPROVEMENT-PLAN-0007: Intent Detection Quality Improvements

**Created:** 2026-01-31
**Status:** Planning
**Priority:** High
**Affected Components:** Interview-assist-library (Pipeline/Utterance), Interview-assist-transcription-detection-console

## Problem Statement

Analysis of live transcription output revealed several quality issues with the intent detection pipeline. The current implementation produces excessive false positives, duplicate detections, and triggers on sentence fragments that are not actual questions.

### Observed Issues

#### 1. Duplicate Detections as Text Builds
The same question is detected multiple times as partial ASR events arrive:
```
>> [Question] how this technology is going to evolve to the point where
>> [Question] how this technology is going to evolve to the point where humanity is
>> [Question] how this technology is going to evolve to the point where humanity is how...
```

#### 2. False Positives on Relative Clauses
Subordinate clauses incorrectly detected as questions:
```
>> [Question] who are creating it,
>> [Question] who are creating it and blackmail
```
These are relative clauses (e.g., "people **who** are creating it"), not standalone questions.

#### 3. False Positives on Indirect/Reported Speech
```
>> [Compare] Because you told Faisal here before that you would compare this to the relative
```
The word "compare" triggers detection, but it's reported speech, not a comparison question.

#### 4. Premature Fragment Detections
Very short fragments detected before meaningful content:
```
>> [Question] What
>> [Question] What
>> [Question] What one question
```

#### 5. No Deduplication of Final Detections
Once a question is detected as final, it appears again when the utterance closes with slightly more text.

---

## Proposed Solutions

### Task 1: Detect Only on Final ASR Events
**Priority:** High
**Effort:** Low
**Files:** `UtteranceIntentPipeline.cs`, `Program.cs` (detection console)

Currently, intent detection runs on every partial ASR event, causing repeated detections as text builds up. Change to only run intent detection on final/stable text.

**Changes:**
- Modify pipeline to skip `DetectCandidate` on partial events for display purposes
- Only emit `OnIntentCandidate` for UI hints, not for transcript display
- `OnIntentFinal` should only fire on utterance close

---

### Task 2: Add Similarity-Based Deduplication
**Priority:** High
**Effort:** Medium
**Files:** `UtteranceIntentPipeline.cs` or new `IntentDeduplicator.cs`

Add deduplication to prevent showing the same question multiple times.

**Algorithm:**
- Maintain a sliding window of recently detected intents (last 30 seconds)
- Before emitting a new intent, check similarity against recent ones
- Skip if >80% similar (using Levenshtein ratio or token overlap)
- Clear window on speaker change or long silence

**New Interface:**
```csharp
public interface IIntentDeduplicator
{
    bool IsDuplicate(DetectedIntent intent);
    void RecordIntent(DetectedIntent intent);
    void Reset();
}
```

---

### Task 3: Improve Question Detection - Sentence Position Check
**Priority:** High
**Effort:** Medium
**Files:** `IntentDetector.cs`

Add validation that question words appear at utterance/sentence start, not mid-sentence.

**Changes:**
- Track sentence boundaries (periods, question marks, exclamation marks)
- Only match WH-words and auxiliary verbs at sentence-initial position
- Reject matches that follow a noun phrase (likely relative clauses)

**Heuristics to add:**
- "who/which/that" following a noun → relative clause, not question
- Question word after comma → likely subordinate clause
- Check for preceding context in the utterance

---

### Task 4: Add Minimum Length Threshold
**Priority:** Medium
**Effort:** Low
**Files:** `IntentDetector.cs`, `PipelineOptions.cs`

Require minimum word count before detecting intent.

**Changes:**
- Add `MinWordsForDetection` option (default: 4-5 words)
- Skip detection for fragments shorter than threshold
- Apply to both candidate and final detection

---

### Task 5: Improve Indirect Speech Detection
**Priority:** Medium
**Effort:** Medium
**Files:** `IntentDetector.cs`

Detect and filter out indirect/reported speech patterns.

**Patterns to detect:**
- "you said that...", "he told me...", "she asked whether..."
- "would compare", "would ask", "might question" (conditional)
- Quoted speech context

**Changes:**
- Add regex patterns for indirect speech markers
- Reduce confidence or skip detection when indirect speech detected
- Check for past tense reporting verbs before question words

---

### Task 6: Raise Confidence Threshold for Display
**Priority:** Low
**Effort:** Low
**Files:** `Program.cs` (both console apps), `appsettings.json`

Add configurable confidence threshold for displaying detected intents.

**Changes:**
- Add `IntentDetection:DisplayConfidenceThreshold` setting (default: 0.6)
- Only display intents above threshold in transcript
- Still log all detections in debug panel

---

### Task 7: Add Question Mark Requirement Option
**Priority:** Low
**Effort:** Low
**Files:** `IntentDetector.cs`, `PipelineOptions.cs`

Add option to require question mark for high-confidence question detection.

**Changes:**
- Add `RequireQuestionMark` option (default: false)
- When enabled, questions without `?` get reduced confidence
- Useful for cleaner output at cost of some recall

---

## Implementation Order

### Phase 1: Quick Wins (High Impact, Low Effort)
1. Task 1: Detect only on final ASR events
2. Task 4: Add minimum length threshold
3. Task 6: Raise confidence threshold for display

### Phase 2: Core Quality Improvements
4. Task 2: Add similarity-based deduplication
5. Task 3: Improve question detection - sentence position check

### Phase 3: Refinements
6. Task 5: Improve indirect speech detection
7. Task 7: Add question mark requirement option

---

## Success Criteria

After implementation, the following should be true:

1. **No duplicate detections** - Each unique question appears only once
2. **No fragment detections** - Short incomplete phrases are not detected
3. **Reduced false positives** - Relative clauses and indirect speech filtered out
4. **Configurable behavior** - Thresholds adjustable via appsettings.json
5. **Backward compatible** - Existing tests continue to pass

---

## Testing Plan

1. **Unit tests** for each new component (deduplicator, position checker)
2. **Update existing IntentDetector tests** with new edge cases
3. **Manual testing** with live transcription of interview/podcast content
4. **Before/after comparison** using the sample output from this analysis

---

## Notes

- The current heuristic-based approach has fundamental limitations
- Future consideration: LLM-based intent detection for higher accuracy
- Balance between precision (fewer false positives) and recall (catching all questions)
