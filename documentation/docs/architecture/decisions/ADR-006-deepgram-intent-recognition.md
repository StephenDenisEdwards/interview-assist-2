# ADR-006: Deepgram Intent Recognition via REST API

## Status

Accepted

## Context

The pipeline needs a mid-tier intent detection option between the free-but-inaccurate heuristic strategy (~67% recall) and the accurate-but-expensive LLM strategy (~95% recall, 500–2000ms latency). Deepgram offers intent recognition through two surfaces:

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

Use Deepgram's `/v1/read` REST endpoint via raw `HttpClient`, implementing the `ILlmIntentDetector` interface.

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

## Consequences

### Positive

- **Mid-tier latency**: ~50–100ms per call, 5–20x faster than LLM-based detection
- **Lower cost**: Deepgram text analysis pricing is cheaper than GPT-4o-mini token costs
- **No new dependencies**: Reuses existing `HttpClient` pattern
- **Pluggable**: Implements `ILlmIntentDetector`, slots into `LlmIntentStrategy` for buffering, rate-limiting, and deduplication
- **Custom intents**: Supports domain-specific intent hints via query parameters

### Negative

- **Dynamic labels require mapping**: Deepgram returns free-form verb phrases, not a fixed taxonomy. The keyword-based `ClassifyIntent()` method may misclassify novel labels
- **Lower confidence scores**: Deepgram's confidence distribution differs from LLM scores, requiring a lower threshold (0.3 vs 0.7)
- **No context window**: Unlike the LLM strategy, Deepgram analyzes each text segment independently without conversational context
- **Rate limiting**: Deepgram enforces a 10-request concurrency limit; must be managed via the `LlmIntentStrategy` rate limiter
- **HTTP timeout risk**: 5-second default timeout; network issues produce empty results rather than errors

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
