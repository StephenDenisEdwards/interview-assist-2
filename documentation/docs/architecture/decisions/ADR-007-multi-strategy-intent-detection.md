# ADR-007: Multi-Strategy Intent Detection

## Status

Accepted (supersedes ADR-003)

## Context

ADR-003 established LLM-based question detection as the sole detection approach. As the system evolved, several problems emerged:

1. **Latency**: LLM detection takes 500–2000ms, too slow for interactive imperative commands like "stop" or "repeat"
2. **Cost**: Every utterance triggers an LLM API call, expensive at high transcription throughput
3. **No correction mechanism**: If the LLM misclassifies, there is no way to revise the result
4. **Single point of failure**: LLM API outage disables all detection

The codebase now has four detection implementations with different latency/cost/accuracy profiles. A unified abstraction is needed to make them interchangeable and composable.

### Evaluated Strategies

| Strategy | Latency | Cost | Recall | Key Trade-off |
|----------|---------|------|--------|---------------|
| Heuristic | <1ms | Free | ~67% | Fast but misses implicit questions and imperatives |
| LLM | 500–2000ms | High | ~95% | Accurate but slow and expensive |
| Deepgram | 50–100ms | Low | TBD | Mid-tier, dynamic labels need mapping |
| Parallel | <1ms + 500–2000ms | High | ~95% | Best UX: fast heuristic + async LLM correction |

## Decision

Introduce `IIntentDetectionStrategy` as a pluggable strategy interface, with four implementations selectable via `IntentDetectionMode` enum.

### Interface Design

```csharp
public interface IIntentDetectionStrategy : IDisposable
{
    string ModeName { get; }
    Task ProcessUtteranceAsync(UtteranceEvent utterance, CancellationToken ct = default);
    void SignalPause();
    event Action<IntentEvent>? OnIntentDetected;
    event Action<IntentCorrectionEvent>? OnIntentCorrected;
}
```

Key design choices:

- **Utterance-level granularity**: Strategies receive finalized `UtteranceEvent` objects (from `UtteranceBuilder`), not raw ASR text. This ensures consistent segmentation across strategies.
- **Pause signaling**: `SignalPause()` allows external signals (e.g., Deepgram utterance-end) to trigger batched detection in strategies that buffer text.
- **Correction events**: `OnIntentCorrected` enables the Parallel strategy to revise earlier heuristic results when LLM verification completes.

### Strategy Implementations

**1. HeuristicIntentStrategy**
- Uses `IntentDetector` (regex-based) for synchronous classification
- Fires `OnIntentDetected` immediately on `ProcessUtteranceAsync`
- Never fires `OnIntentCorrected`
- Configurable via `HeuristicDetectionOptions.MinConfidence`

**2. LlmIntentStrategy**
- Buffers utterances in a `StringBuilder` (max `BufferMaxChars`, default 2500)
- Triggers LLM detection on: question mark in text, pause signal, or timeout (`TriggerTimeoutMs`)
- Rate-limited (`RateLimitMs`, default 1500ms)
- Deduplicates via semantic fingerprinting with time-based suppression windows
- Maps utterance IDs to LLM results via word overlap scoring
- Accepts any `ILlmIntentDetector` (OpenAI `ChatGptIntentDetector` or `DeepgramIntentDetector`)

**3. ParallelIntentStrategy**
- Phase 1: Runs heuristic synchronously, emits `OnIntentDetected` immediately
- Phase 2: Buffers text for async LLM verification (same buffering as `LlmIntentStrategy`)
- Compares LLM results against heuristic emissions:
  - **Confirmed**: Same intent type → no action
  - **TypeChanged**: Different type → emits `OnIntentCorrected`
  - **Added**: LLM found intent heuristic missed → emits both events
  - **Removed**: Heuristic false positive (moderate confidence, LLM disagrees) → emits correction
- Tracks emitted intents per utterance ID for comparison

**4. DeepgramIntentDetector** (via LlmIntentStrategy)
- Implements `ILlmIntentDetector`, plugs into `LlmIntentStrategy`
- Uses Deepgram `/v1/read` REST endpoint (see ADR-006)
- Same buffering, rate-limiting, and deduplication as LLM mode

### Selection

```csharp
public enum IntentDetectionMode
{
    Heuristic,  // Fast, free, ~67% recall
    Llm,        // Accurate, expensive, ~95% recall
    Parallel,   // Best UX: fast + verified
    Deepgram    // Mid-tier: moderate speed and cost
}
```

## Consequences

### Positive

- **Pluggable**: Swap strategies without changing pipeline wiring
- **Composable**: Parallel strategy composes heuristic + any `ILlmIntentDetector`
- **Correction mechanism**: `OnIntentCorrected` allows UI to revise displayed results
- **Testable**: Each strategy is independently unit-testable
- **Extensible**: New strategies (e.g., local ML model) implement the same interface

### Negative

- **Four implementations to maintain**: Each has distinct buffering and triggering logic
- **Shared infrastructure duplication**: `LlmIntentStrategy` and `ParallelIntentStrategy` share similar buffer/deduplication code
- **Correction complexity**: Consumers must handle both `OnIntentDetected` and `OnIntentCorrected` events
- **Configuration surface**: Each strategy has its own options class (`HeuristicDetectionOptions`, `LlmDetectionOptions`, `DeepgramDetectionOptions`)

### Relationship to ADR-003

ADR-003 described a single `LlmQuestionDetector` with Jaccard deduplication and rate limiting. That functionality is now split across:

- `ILlmIntentDetector` — the raw detection call (OpenAI or Deepgram)
- `LlmIntentStrategy` — buffering, triggering, rate-limiting, deduplication
- `IIntentDetectionStrategy` — the unified pipeline interface

ADR-003's core decision (use LLM for semantic detection) remains valid as one of four strategies. This ADR supersedes it by documenting the full strategy pattern.
