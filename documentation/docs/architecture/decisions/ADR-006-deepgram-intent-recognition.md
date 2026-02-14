# ADR-006: Deepgram Intent Recognition via REST API

## Status

Superseded — code removed; evaluated and found unsuitable (0% F1 across all recordings)

## Context

The pipeline needs a mid-tier intent detection option between the free-but-inaccurate heuristic strategy and the accurate-but-expensive LLM strategy. Deepgram offers intent recognition through two surfaces:

1. **Streaming WebSocket** (`/v1/listen`) — supports transcription but does **not** support intent detection.
2. **REST Read endpoint** (`/v1/read`) — accepts text, returns detected intents with confidence scores.
3. **Deepgram SDK** (NuGet `Deepgram`) — official .NET SDK wrapping both endpoints.

Additionally, Deepgram's intent labels are dynamic verb phrases (e.g., "Find out difference between abstract class and interface") rather than a fixed taxonomy, requiring a mapping layer.

### Requirements

- Latency under 200ms per detection call
- No new NuGet dependencies (the project already avoids the Deepgram SDK for transcription)
- Support custom intents via `custom_intent` query parameters
- Reuse existing `ILlmIntentDetector` interface so the detector slots into `LlmIntentStrategy` infrastructure
- Map Deepgram's dynamic labels to the project's `IntentType`/`IntentSubtype` taxonomy

## Decision

Use Deepgram's `/v1/read` REST endpoint via raw `HttpClient`, implementing the `ILlmIntentDetector` interface. Keep the implementation in the codebase as an available strategy even though evaluation showed poor results for question detection — it may have value for other intent types (imperatives, topic classification) in the future.

### Implementation (`DeepgramIntentDetector`)

- Sends text as JSON POST to `https://api.deepgram.com/v1/read?intents=true&language=en`
- Parses `results.intents.segments[].intents[]` from the response
- Filters by configurable confidence threshold (default 0.3, lower than LLM's 0.7 because Deepgram scores differently)
- Maps dynamic verb-phrase labels to `IntentType`/`IntentSubtype` via keyword matching in `ClassifyIntent()`:
  - Question indicators: "ask", "question", "inquire", "wonder", "explain", "describe"
  - Information-seeking: "find out", "learn", "understand", "know", "clarify"
  - Imperative subtypes: "stop", "repeat", "continue", "generate"
  - Fallback: `IntentType.Statement`
- Infers question subtypes (Definition, HowTo, Compare, Troubleshoot) from label keywords
- Supports custom intents in "extended" (add to built-in) or "strict" (custom only) modes

### Why not the Deepgram SDK?

The project already uses raw `HttpClient` for Deepgram transcription (WebSocket in `DeepgramTranscriptionService`). Adding the SDK NuGet would introduce a dependency used for a single REST call. The `/v1/read` endpoint is a straightforward POST, making raw HTTP simpler than an SDK wrapper.

### Why not the streaming endpoint?

Deepgram's streaming WebSocket (`/v1/listen`) does not support the `intents` feature. Intent detection is only available on the `/v1/read` text analysis endpoint.

## Evaluation Results

Evaluated across 4 recorded interview sessions (10K–131K characters, 3–39 ground truth questions per session). Deepgram scored **0% F1 across all recordings** — 0 true positives, only false positives and false negatives.

### Results Summary

| Recording | Ground Truth | Deepgram Detected | TP | FP | FN | F1 |
|-----------|-------------|-------------------|----|----|-----|-----|
| session-162854 (10K) | 3 | 0 | 0 | 0 | 3 | 0% |
| session-155843 (99K) | 28 | 2 | 0 | 2 | 28 | 0% |
| session-163251 (131K) | 15 | 2 | 0 | 2 | 15 | 0% |
| session-114135 (86K) | 39 | 2 | 0 | 2 | 39 | 0% |

For comparison, the LLM strategy (gpt-4o-mini) scored 19–100% F1 on the same recordings.

### Root Cause Analysis

**Deepgram's intent recognition solves a different problem than question detection.** The `/v1/read` intent model is designed for **conversation routing** — classifying user requests in task-oriented systems (e.g., "I want to check my balance" → route to billing). It is not designed for **speech act classification** (determining whether an utterance is a question, statement, or command).

Specific findings from API testing:

1. **Intent labels are goal descriptions, not speech act classifications.** Deepgram returns verb phrases describing the speaker's objective:
   - "What is the difference between microservices and monolithic architecture?" → label: `"Find out difference between microservices and monolithic architecture"` (conf: 0.003)
   - "Could you clarify when to use each one?" → label: `"Understand difference between abstract classes and interfaces"` (conf: 0.448)
   - A statement like "We processed two million transactions per day" → label: `"Work on distributed payments system"` (conf: 0.003)

2. **Confidence scores are not calibrated for question detection.** Scores range from 0.001–0.7 regardless of whether the text is a question. An explicit question ("What challenges did you face?") received confidence 0.01, while a non-question statement received 0.003 — no meaningful separation.

3. **Custom intents provide marginal improvement.** Adding custom intents like "ask a question" and "seek explanation" occasionally boosted confidence for some texts (e.g., 0.448 for a clarification request) but not consistently enough to establish a reliable threshold.

4. **The keyword-based label classifier cannot compensate.** Even when Deepgram returns a label that passes the confidence threshold, the label often doesn't contain question-indicating keywords. Labels like "Show flex" (conf: 0.709) pass the threshold but are correctly classified as statements.

### Why the Strategy Was Removed

The implementation was originally retained because it was isolated, might be useful for future use cases, and served as a documented evaluation. It was removed because:
- Dead code with 0% effectiveness carried 6 permanently-failing unit tests
- Maintenance overhead on every refactoring (updating enum, options, DI wiring, factory methods)
- Misleading developers into thinking Deepgram detection is a viable mode
- This ADR itself serves as the documented evaluation — the code is not needed for that purpose

## Consequences

### Positive

- **No new dependencies**: Reuses existing `HttpClient` pattern
- **Pluggable**: Implements `ILlmIntentDetector`, slots into `LlmIntentStrategy` for buffering, rate-limiting, and deduplication
- **Custom intents**: Supports domain-specific intent hints via query parameters
- **Documented evaluation**: Concrete data showing why general-purpose intent recognition doesn't work for question detection

### Negative

- **Not effective for question detection**: 0% F1 across all evaluated recordings
- **Dynamic labels require mapping**: Deepgram returns free-form verb phrases, not a fixed taxonomy. The keyword-based `ClassifyIntent()` method misclassifies most labels
- **Confidence scores not meaningful for this use case**: No threshold separates questions from statements
- **No context window**: Unlike the LLM strategy, Deepgram analyzes each text segment independently without conversational context

### Configuration

```csharp
var options = new DeepgramDetectionOptions
{
    ConfidenceThreshold = 0.3,      // Lower than LLM due to different scoring
    CustomIntents = new List<string> { "ask a technical question", "request clarification" },
    CustomIntentMode = "extended",   // "extended" or "strict"
    TimeoutMs = 5000
};

var detector = new DeepgramIntentDetector(apiKey, options);
```
