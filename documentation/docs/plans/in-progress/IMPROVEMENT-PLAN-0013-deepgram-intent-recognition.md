# IMPROVEMENT-PLAN-0013: Deepgram Intent Recognition Strategy

**Created:** 2026-02-06
**Status:** Completed (Phases 1-4, 7 partial)
**Completed:** 2026-02-06

## Implementation Notes

### Key Design Deviation from Plan

The plan originally called for a separate `DeepgramIntentStrategy` class (Phase 3). During implementation, we determined this was unnecessary — `LlmIntentStrategy` is already parameterized on `ILlmIntentDetector` and handles all buffering, rate limiting, deduplication, and trigger logic. The only thing that changes is the detector backend.

**Actual approach:** `DeepgramIntentDetector` implements `ILlmIntentDetector` and is passed to `LlmIntentStrategy`. No separate strategy class was created. This means Phase 3 was effectively merged into Phase 1.

### Streaming Limitation

Deepgram's intent recognition is **not available on the live streaming WebSocket API** (`wss://api.deepgram.com/v1/listen`). It is only available on the pre-recorded REST endpoint (`/v1/listen`) and the text analysis REST endpoint (`/v1/read`).

The workaround implemented here sends finalized utterance text to `/v1/read?intents=true` via `HttpClient`. This adds ~50-100ms latency per detection call but fits cleanly into the existing `LlmIntentStrategy` buffering pattern, which already handles trigger-based batching and rate limiting.

### SDK Decision

We chose raw `HttpClient` over the Deepgram .NET SDK to avoid adding a new NuGet dependency. The `/v1/read` REST call is simple enough (JSON POST, parse response) that the SDK adds no meaningful value. Phase 5 is therefore N/A.

### Files Created/Modified

| Action | File |
|--------|------|
| Created | `Pipeline/Detection/DeepgramDetectionOptions.cs` |
| Created | `Pipeline/Detection/DeepgramIntentDetector.cs` |
| Modified | `Pipeline/Detection/IntentDetectionMode.cs` — added `Deepgram` enum value |
| Modified | `Pipeline/Detection/IntentDetectionOptions.cs` — added `Deepgram` property |
| Modified | `transcription-detection-console/Program.cs` — config loading, strategy factory, API key validation |
| Modified | `transcription-detection-console/appsettings.json` — added Deepgram detection config section |
| Modified | `Extensions/ServiceCollectionExtensions.cs` — added Deepgram case to DI factory |
| Modified | `Pipeline/Evaluation/StrategyComparer.cs` — added Deepgram to strategy comparison |
| Modified | `transcription-detection-console/EvaluationRunner.cs` — fixed `CompareAsync` call signature |
| Modified | `transcription-detection-android/EvaluationRunner.cs` — fixed `CompareAsync` call signature |

## Goal

Add Deepgram's built-in Intent Recognition as a new `IIntentDetectionStrategy` option in the transcription-detection-console, providing a lower-latency, lower-cost alternative to the current LLM-based (GPT-4o-mini) intent detection.

## Research Summary

### What Deepgram Intent Recognition Is

Deepgram offers an **Audio Intelligence** feature called Intent Recognition, powered by their proprietary **Task-Specific Language Model (TSLM)** -- a small model fine-tuned on 60,000+ domain-specific conversations. Unlike traditional intent classifiers with fixed intent lists, the TSLM dynamically generates intent labels in verb form (e.g., "Ask about concept", "Request clarification") with confidence scores.

### API Endpoints

| Endpoint | Path | Intent Support |
|----------|------|----------------|
| Pre-recorded Audio (REST) | `POST /v1/listen?intents=true` | Yes |
| Text Analysis (REST) | `POST /v1/read?intents=true` | Yes |
| Live/Streaming (WebSocket) | `wss://api.deepgram.com/v1/listen` | **No** |

### Critical Constraint: No Streaming Support

**Intent recognition does NOT work on Deepgram's live/streaming WebSocket API.** This is the most important finding. Since our pipeline streams live audio via WebSocket for transcription, we cannot get intents from the streaming connection directly.

**Workaround:** Use the `/v1/read` (text analysis) REST endpoint. When the pipeline receives finalized utterance text from the streaming STT, send that text to `/v1/read?intents=true` for intent analysis. This adds ~50-100ms latency per utterance but fits cleanly into the existing `IIntentDetectionStrategy` pattern.

### Request Format

```bash
# Text analysis (our planned approach)
curl -X POST \
  -H 'Authorization: Token DEEPGRAM_API_KEY' \
  -H 'Content-Type: application/json' \
  -d '{"text": "Can you explain polymorphism in object-oriented programming?"}' \
  'https://api.deepgram.com/v1/read?intents=true&language=en'
```

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `intents` | `boolean` | Yes | Set to `true` to enable |
| `language` | `string` | No | Defaults to `en`. **English only.** |
| `custom_intent` | `string` | No | Custom intent to detect. Up to 100 values. |
| `custom_intent_mode` | `string` | No | `"strict"` = only custom intents; `"extended"` = auto + custom |

### Response Format

```json
{
  "results": {
    "intents": {
      "segments": [
        {
          "text": "Can you explain polymorphism?",
          "start_word": 0,
          "end_word": 4,
          "intents": [
            {
              "intent": "Ask about concept",
              "confidence_score": 0.975
            }
          ]
        }
      ]
    }
  }
}
```

### .NET SDK Support

The official `Deepgram` NuGet package (v6.6.1, targets .NET 8.0) supports intent recognition on the REST client.

**Relevant SDK types** (namespace `Deepgram.Models.Listen.v1.REST`):

```csharp
// Request options
public class PreRecordedSchema
{
    public bool? Intents { get; set; }
    public List<string>? CustomIntent { get; set; }
    public string? CustomIntentMode { get; set; }
}

// Response models
public class IntentGroup { public IReadOnlyList<Segment>? Segments { get; set; } }
public class Segment
{
    public string? Text { get; set; }
    public int? StartWord { get; set; }
    public int? EndWord { get; set; }
    public IReadOnlyList<Intent>? Intents { get; set; }
}
public class Intent
{
    public string? Intention { get; set; }       // e.g. "Ask about concept"
    public double? ConfidenceScore { get; set; }  // 0.0 - 1.0
}
```

**Note:** The SDK does NOT expose intent recognition on the `ListenWebSocketClient`, consistent with the API limitation. For the `/v1/read` text endpoint, we may need to use `HttpClient` directly since the SDK's read client may not expose intents on text input. This needs verification during implementation.

### Pricing

Included with all Deepgram plans (no add-on). Token-based under Audio Intelligence:

| Plan | Input Tokens | Output Tokens |
|------|-------------|---------------|
| Pay As You Go | $0.0003 / 1K tokens | $0.0006 / 1K tokens |
| Growth | $0.00024 / 1K tokens | $0.00048 / 1K tokens |

Starts with $200 free credit. Concurrency limit of 10 Audio Intelligence requests on Pay-As-You-Go/Growth.

### Limitations

| Limitation | Impact |
|-----------|--------|
| **English only** | Fine for our use case |
| **No streaming** | Mitigated by using `/v1/read` text endpoint on finalized utterances |
| **Dynamic labels** | Intent labels vary across calls; mitigated by `custom_intent` + `strict` mode |
| **Short utterances** | 1-2 word phrases yield low confidence; similar to LLM strategy |
| **10 concurrent requests** | Must rate-limit; pattern already exists in `LlmIntentStrategy` |
| **150K token limit per request** | Not a concern for single utterances |

### Comparison with Current Strategies

| Dimension | Deepgram `/v1/read` | LLM (GPT-4o-mini) | Heuristic |
|-----------|---------------------|---------------------|-----------|
| Latency | ~50-100ms | ~500-2000ms | <1ms |
| Cost | ~$0.0003/1K tokens | ~$0.15/1M tokens | Free |
| Label quality | Dynamic verb phrases (or fixed custom) | Structured JSON with subtypes | Regex patterns |
| Accuracy | Unknown (needs benchmarking) | ~95% recall | ~67% recall |
| Concurrency | 10 cap (lower tiers) | Higher limits | Unlimited |
| Offline | No | No | Yes |

## Architecture

### Integration Approach: Text-Based Post-Processing

```
Deepgram WebSocket (streaming STT)
    |
    v
UtteranceBuilder (segments on silence/punctuation)
    |
    v
UtteranceEvent (final)
    |
    v
DeepgramIntentStrategy (new)
    |
    v
POST /v1/read?intents=true  (REST, ~50-100ms)
    |
    v
Map Deepgram intents -> IntentEvent
    |
    v
OnIntentDetected event
```

This reuses the same Deepgram API key already configured for transcription. The strategy plugs into the existing `IIntentDetectionStrategy` interface with zero changes to the pipeline.

### Custom Intents for Interview Context

To get deterministic, relevant labels, configure custom intents tailored to interview scenarios:

```json
{
  "CustomIntents": [
    "Ask technical question",
    "Ask about experience",
    "Request clarification",
    "Request example",
    "Ask follow-up question",
    "Request explanation",
    "Ask about architecture",
    "Ask about tradeoffs"
  ],
  "CustomIntentMode": "extended"
}
```

Using `"extended"` mode returns both auto-detected and custom intents, giving us the best of both worlds.

### Intent Mapping

Deepgram returns verb-phrase intents. We need to map these to our existing `IntentType`/`IntentSubtype` model:

| Deepgram Intent | IntentType | IntentSubtype |
|-----------------|------------|---------------|
| "Ask technical question" | Question | Definition |
| "Ask about experience" | Question | Question (generic) |
| "Request clarification" | Question | Question (generic) |
| "Request example" | Question | HowTo |
| "Ask about architecture" | Question | Definition |
| "Ask about tradeoffs" | Question | Compare |
| (other auto-detected) | Mapped by keyword heuristic | Best-effort |

For auto-detected intents (non-custom), apply a simple keyword mapping: intents containing "ask", "question", "explain" map to `Question`; intents containing "stop", "cancel" map to `Imperative`; everything else maps to `Statement`.

## Implementation Plan

### Phase 1: Deepgram Intent Detector (Medium effort)

Create the low-level detector that calls the `/v1/read` endpoint.

- [x] Verify whether the Deepgram .NET SDK supports `/v1/read` with intents, or if we need raw `HttpClient` — Used raw `HttpClient` (no new dependency)
- [x] Create `DeepgramIntentDetector` class implementing `ILlmIntentDetector`
- [x] Handle request construction: text payload, `intents=true`, optional `custom_intent` params
- [x] Parse response: extract `segments[].intents[]`, map to `DetectedIntent` records
- [x] Add intent-to-`IntentType` mapping logic (custom intent labels + keyword fallback)
- [x] Add rate limiting (reuse pattern from `LlmIntentStrategy`, respect 10-concurrency cap) — inherited via `LlmIntentStrategy` reuse
- [x] Add error handling and logging (HTTP errors, empty responses, timeouts)

**Files to create:**
```
Interview-assist-library/Pipeline/Detection/DeepgramIntentDetector.cs
```

### Phase 2: Configuration (Low effort)

Add configuration options for the Deepgram intent strategy.

- [x] Create `DeepgramDetectionOptions` record with: `ConfidenceThreshold`, `CustomIntents`, `CustomIntentMode`, `RateLimitMs`, `TimeoutMs`
- [x] Add `Deepgram` value to `IntentDetectionMode` enum
- [x] Add `Deepgram` section to `IntentDetectionOptions`
- [x] Update `appsettings.json` with Deepgram intent configuration section

**Files to create:**
```
Interview-assist-library/Pipeline/Detection/DeepgramDetectionOptions.cs
```

**Files to modify:**
```
Interview-assist-library/Pipeline/Detection/IntentDetectionMode.cs
Interview-assist-library/Pipeline/Detection/IntentDetectionOptions.cs
Interview-assist-transcription-detection-console/appsettings.json
```

### Phase 3: Strategy Implementation (Medium effort)

Create the strategy that plugs into the pipeline.

- [x] ~~Create `DeepgramIntentStrategy` implementing `IIntentDetectionStrategy`~~ — **Skipped:** Reused `LlmIntentStrategy` directly (see Implementation Notes above)
- [x] On `ProcessUtteranceAsync`: extract text, call `DeepgramIntentDetector`, fire `OnIntentDetected` — inherited via `LlmIntentStrategy`
- [x] Add utterance text buffering — inherited via `LlmIntentStrategy`
- [x] Add deduplication — inherited via `LlmIntentStrategy`
- [x] Implement `SignalPause()` trigger — inherited via `LlmIntentStrategy`

**Files to create:**
```
Interview-assist-library/Pipeline/Detection/DeepgramIntentStrategy.cs
```

### Phase 4: Console App Integration (Low effort)

Wire the new strategy into the transcription-detection-console.

- [x] Update `Program.cs` strategy factory to handle `IntentDetectionMode.Deepgram`
- [x] Reuse existing Deepgram API key (no new key needed)
- [x] Add status bar display for Deepgram mode — inherited (mode name comes from `LlmIntentStrategy.ModeName`)
- [x] Map Deepgram intent labels into the existing UI format — handled in `DeepgramIntentDetector.ClassifyIntent()`

**Files to modify:**
```
Interview-assist-transcription-detection-console/Program.cs
```

### Phase 5: NuGet Package (Low effort, if needed)

- [x] ~~If SDK supports `/v1/read` with intents: `dotnet add package Deepgram` to Interview-assist-library~~ — **N/A:** Used raw `HttpClient`, no new dependency added
- [x] If SDK doesn't support it: use existing `HttpClient` (no new dependency) — **Done**

**Files to modify (potentially):**
```
Interview-assist-library/Interview-assist-library.csproj
```

### Phase 6: Optional - Parallel+Deepgram Hybrid (Medium effort, deferred)

A variant of the Parallel strategy that uses Deepgram instead of GPT-4o-mini for verification.

- [ ] Create `ParallelDeepgramIntentStrategy` (or parameterize existing `ParallelIntentStrategy`)
- [ ] Heuristic for immediate feedback + Deepgram `/v1/read` for verification
- [ ] Lower cost and lower latency than Heuristic+LLM parallel mode

**This phase is optional and can be deferred until after Phase 5 benchmarking.**

### Phase 7: Evaluation & Benchmarking (Medium effort)

Use existing evaluation infrastructure to measure the new strategy.

- [x] Add `Deepgram` mode to `StrategyComparer` for side-by-side comparison
- [ ] Run all four modes on recorded sessions using playback mode
- [ ] Measure precision, recall, F1, latency, and cost
- [ ] Compare Deepgram custom intents vs auto-detected intents
- [ ] Document results and update this plan

## Files Summary

| Action | File | Phase |
|--------|------|-------|
| Create | `Pipeline/Detection/DeepgramIntentDetector.cs` | 1 |
| Create | `Pipeline/Detection/DeepgramDetectionOptions.cs` | 2 |
| Modify | `Pipeline/Detection/IntentDetectionMode.cs` | 2 |
| Modify | `Pipeline/Detection/IntentDetectionOptions.cs` | 2 |
| Create | `Pipeline/Detection/DeepgramIntentStrategy.cs` | 3 |
| Modify | `transcription-detection-console/Program.cs` | 4 |
| Modify | `transcription-detection-console/appsettings.json` | 4 |
| Modify (maybe) | `Interview-assist-library.csproj` | 5 |

## Configuration Schema

```json
{
  "Transcription": {
    "IntentDetection": {
      "Mode": "Deepgram",

      "Deepgram": {
        "ConfidenceThreshold": 0.7,
        "RateLimitMs": 500,
        "TimeoutMs": 5000,
        "BufferMaxChars": 800,
        "TriggerOnQuestionMark": true,
        "TriggerOnPause": true,
        "TriggerTimeoutMs": 3000,
        "EnableDeduplication": true,
        "DeduplicationWindowMs": 30000,
        "CustomIntents": [
          "Ask technical question",
          "Ask about experience",
          "Request clarification",
          "Request example",
          "Ask follow-up question",
          "Request explanation"
        ],
        "CustomIntentMode": "extended"
      }
    }
  }
}
```

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Deepgram `/v1/read` adds too much latency | Low | Medium | ~50-100ms expected; still 5-20x faster than LLM |
| Dynamic intent labels are inconsistent | Medium | Medium | Use `custom_intent` with `strict` or `extended` mode |
| 10-concurrency cap hit during fast speech | Low | Low | Rate limiting already patterned in `LlmIntentStrategy` |
| Intent accuracy lower than LLM | Medium | Medium | Benchmarking in Phase 7 will quantify; keep LLM as option |
| Deepgram .NET SDK doesn't support `/v1/read` intents | Medium | Low | Fall back to raw `HttpClient` (simple REST call) |
| Deepgram API changes or deprecation | Low | High | Abstract behind `ILlmIntentDetector`; swappable |

## Success Criteria

1. `DeepgramIntentStrategy` works in the transcription-detection-console with `Mode: "Deepgram"`
2. Uses the same Deepgram API key already configured for transcription
3. Custom intents return consistent, relevant labels for interview questions
4. Latency is measurably lower than LLM mode (~50-100ms vs ~500-2000ms)
5. Existing evaluation/playback modes can benchmark the new strategy
6. No changes required to `UtteranceIntentPipeline` (clean strategy plug-in)

## References

- [Deepgram Intent Recognition Docs](https://developers.deepgram.com/docs/intent-recognition)
- [Deepgram Text Intent Recognition](https://developers.deepgram.com/docs/text-intention-recognition)
- [Deepgram Feature Overview Matrix](https://developers.deepgram.com/docs/stt-intelligence-feature-overview)
- [Deepgram .NET SDK](https://github.com/deepgram/deepgram-dotnet-sdk)
- [Deepgram NuGet Package](https://www.nuget.org/packages/Deepgram)
- IMPROVEMENT-PLAN-0010: LLM-Based Intent Detection (existing architecture)
- Existing domain doc: `documentation/docs/domain/Deepgram-overview.md`
