# Heuristic Question Detector

This document describes the `HeuristicQuestionDetector` class, a lightweight, offline question detection mechanism for real-time transcription scenarios.

## Overview

The `HeuristicQuestionDetector` provides fast, local question detection using pattern matching and punctuation analysis. Unlike the AI-based `OpenAiQuestionDetectionService` in the pipeline, this detector requires no API calls and operates with zero latency.

**Location:** `Interview-assist-transcription-console/HeuristicQuestionDetector.cs`

## Purpose

In real-time transcription scenarios, questions often arrive split across multiple transcription batches. For example:

```
Batch 1: "So tell me about your experience"
Batch 2: "with distributed systems?"
```

The heuristic detector solves this by:
1. Maintaining a rolling text buffer
2. Waiting for sentence terminators (`.` `?` `!`)
3. Analyzing complete sentences for question patterns

## Architecture

```
Audio Stream
     │
     ▼
┌─────────────────────┐
│ TranscriptionService│ (Whisper STT)
└──────────┬──────────┘
           │ OnTranscriptionResult
           ▼
┌─────────────────────┐
│ Rolling Buffer      │ ◄── AddText()
│ (max 500 chars)     │
└──────────┬──────────┘
           │ DetectQuestions()
           ▼
┌─────────────────────┐
│ Sentence Splitter   │ (by . ? !)
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│ Pattern Analysis    │
│ - Question marks    │
│ - Question starters │
│ - Imperative phrases│
└──────────┬──────────┘
           │
           ▼
    DetectedQuestion[]
```

## Detection Patterns

### 1. Direct Questions (with `?`)

Any sentence ending with `?` is classified as a question:

```
"What is dependency injection?" → Question
"Can you explain async/await?"  → Question
```

### 2. Question Word Starters

Sentences starting with these words (even without `?`) are detected if longer than 20 characters:

| Category | Words |
|----------|-------|
| **WH-words** | what, how, why, when, where, who, which |
| **Modal verbs** | can, could, would, should |
| **Auxiliaries** | do, does, did, is, are, was, were, have, has, had |
| **Future/Negative** | will, won't, wouldn't, couldn't, shouldn't |

Example:
```
"How would you handle a race condition" → Question? (no ? in speech)
```

### 3. Imperative Patterns

Commands that implicitly request information:

| Pattern | Example |
|---------|---------|
| `tell me` | "Tell me about your background" |
| `explain` | "Explain the singleton pattern" |
| `describe` | "Describe your ideal architecture" |
| `walk me through` | "Walk me through your approach" |
| `give me` | "Give me an example of polymorphism" |
| `show me` | "Show me how you'd refactor this" |
| `help me understand` | "Help me understand your decision" |
| `talk about` | "Talk about your testing strategy" |
| `share` | "Share your experience with microservices" |
| `elaborate` | "Elaborate on that point" |

## Question Type Classification

Detected questions are categorized:

| Type | Trigger |
|------|---------|
| `Clarification` | Starts with "can you elaborate" or "what do you mean" |
| `Follow-up` | Starts with "and ", "but ", or "also " |
| `Imperative` | Matches imperative starter patterns |
| `Question` | Default for WH-questions |
| `Question?` | Question word detected but no `?` punctuation |

## Usage

### Basic Integration

```csharp
var questionDetector = new HeuristicQuestionDetector();

transcription.OnTranscriptionResult += result =>
{
    foreach (var segment in result.Segments)
    {
        // Feed text to rolling buffer
        questionDetector.AddText(segment.Text);

        // Check for complete questions
        var detected = questionDetector.DetectQuestions();

        foreach (var question in detected)
        {
            Console.WriteLine($"[{question.Type}] {question.Text}");
        }
    }
};
```

### DetectedQuestion Record

```csharp
public record DetectedQuestion(string Text, string Type);
```

## Internal Mechanics

### Rolling Buffer

- **Max Size:** 500 characters
- **Overflow Handling:** Oldest text is trimmed when buffer exceeds limit
- **Purpose:** Captures questions split across transcription batches

### Sentence Processing

1. Buffer is split by sentence terminators (`. ? !`)
2. Each complete sentence is analyzed once
3. Processed sentences are tracked to avoid duplicates
4. Up to 20 recent sentences are remembered

### Buffer Management

```
Initial: "Tell me about"
Add:     " your experience with"
Add:     " databases?"
Buffer:  "Tell me about your experience with databases?"
         ↓ DetectQuestions()
Result:  DetectedQuestion("Tell me about your experience with databases?", "Imperative")
Buffer:  "" (cleared after detection)
```

## Comparison: Heuristic vs AI Detection

| Aspect | HeuristicQuestionDetector | OpenAiQuestionDetectionService |
|--------|---------------------------|--------------------------------|
| **Latency** | ~0ms | 200-500ms (API call) |
| **Cost** | Free | API tokens |
| **Accuracy** | ~70-80% | ~95%+ |
| **Context Awareness** | None | Full conversation context |
| **Confidence Scores** | No | Yes |
| **Complex Questions** | May miss | Handles well |
| **Sarcasm/Rhetorical** | Cannot detect | Can detect |
| **Offline** | Yes | No |

## When to Use

**Use Heuristic Detector when:**
- Low latency is critical
- Cost must be minimized
- Basic question highlighting is sufficient
- Running offline/disconnected
- Building transcription-only tools

**Use AI Detection when:**
- High accuracy is required
- Context-aware detection needed
- Confidence thresholds matter
- Distinguishing question types precisely
- Interview assistance with response generation

## Limitations

1. **No Context Awareness**
   - Cannot understand conversation flow
   - May flag rhetorical questions
   - Cannot detect implicit questions

2. **Punctuation Dependency**
   - Relies on transcription including punctuation
   - Some STT models omit `?` in speech
   - Incomplete sentences stay buffered

3. **Language Support**
   - Patterns are English-only
   - Question words hardcoded
   - No internationalization

4. **False Positives**
   - "Can you believe this weather?" (rhetorical)
   - "I wonder what time it is." (not directed)
   - Quoted questions in statements

5. **False Negatives**
   - "You've worked with Kubernetes." (implicit question)
   - Complex nested questions
   - Questions without standard markers

## Configuration

Currently, the detector has hardcoded values. Future enhancements could include:

```csharp
// Potential future API
var options = new HeuristicDetectorOptions
{
    MaxBufferLength = 500,
    MinQuestionLength = 20,
    MaxProcessedSentences = 20,
    CustomQuestionStarters = new[] { "imagine" },
    CustomImperatives = new[] { "consider" }
};
var detector = new HeuristicQuestionDetector(options);
```

## Example Output

```
=== Real-time Transcription ===
Audio: Loopback | Rate: 16000Hz | Batch: 1500ms | Lang: auto
Listening for audio...

So today we're going to talk about your experience
[Question] What projects have you worked on recently?

I worked on a microservices platform last year
[Follow-up] And how did you handle service discovery?

We used Consul for that
[Imperative] Tell me more about the challenges you faced.

The main challenge was network partitions
[Clarification] Can you elaborate on how you handled those?
```

## Related Components

- `TimestampedTranscriptionService` - Provides transcription segments
- `OpenAiQuestionDetectionService` - AI-based alternative (pipeline)
- `TranscriptBuffer` - Similar rolling buffer concept in pipeline

## Future Improvements

1. **Configurable Patterns** - Allow custom question/imperative lists
2. **Language Support** - Add patterns for other languages
3. **Confidence Scoring** - Heuristic confidence based on pattern strength
4. **ML Hybrid** - Local lightweight model for edge cases
5. **Integration** - Move to shared library for reuse
