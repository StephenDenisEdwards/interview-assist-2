# LLM-Based Intent Detection for UtteranceIntentPipeline

**Created:** 2026-02-01
**Status:** Complete

## Executive Summary

Implement three configurable intent detection modes for the `UtteranceIntentPipeline`, allowing comparison via recorded session playback:

1. **Heuristic** - Current regex-based detection (fast, free, ~67% recall)
2. **LLM** - Pure LLM detection with buffer (accurate, ~95% recall, has cost)
3. **Parallel** - Both run simultaneously, LLM authoritative (best UX, highest cost)

## Detection Modes

### Mode 1: Heuristic Only (Current)

```
Utterance Final ──► IntentDetector ──► Emit Intent
                    (regex-based)
```

**Characteristics:**
- Latency: <1ms
- Cost: Free
- Accuracy: ~67% recall
- Fragmentation: Poor (misses split questions)

**Use case:** Offline, cost-sensitive, or as baseline comparison.

### Mode 2: LLM Only (with Buffer)

```
Utterance Final ──► Context Buffer ──► Trigger Check ──► LLM ──► Emit Intent
                    (accumulates)      (? or pause)      (authoritative)
```

**Characteristics:**
- Latency: 200-500ms
- Cost: ~$0.27/hour
- Accuracy: ~95% recall
- Fragmentation: Solved (buffer provides context)

**Key features:**
- Buffer accumulates 500-1000 chars of context
- LLM called on trigger conditions (question mark, silence, timeout)
- Pronouns resolved using buffer context
- No heuristic involvement in detection

**Use case:** Maximum accuracy, testing intent detection quality.

### Mode 3: Parallel (Heuristic + LLM)

```
Utterance Final
      │
      ├──► IntentDetector ──► Emit Intent (immediate, tentative)
      │    (heuristic)
      │
      └──► Context Buffer ──► LLM ──► Emit/Correct/Add
           (async)            (authoritative)
```

**Characteristics:**
- Latency: Immediate heuristic + 200-500ms LLM confirmation
- Cost: ~$0.27/hour
- Accuracy: ~95% recall
- UX: Fast feedback with corrections

**Key features:**
- Heuristic provides immediate response
- LLM runs asynchronously on all utterances
- LLM can:
  - **Confirm** heuristic detection
  - **Correct** heuristic false positives
  - **Add** detections heuristic missed
- New `OnIntentCorrected` event for updates

**Use case:** Production use - fast feedback with high accuracy.

## Architecture

### Interface Design

```csharp
/// <summary>
/// Strategy for intent detection in the pipeline.
/// </summary>
public interface IIntentDetectionStrategy
{
    /// <summary>Mode identifier for logging/debugging.</summary>
    string ModeName { get; }

    /// <summary>Process a finalized utterance.</summary>
    Task ProcessUtteranceAsync(UtteranceEvent utterance, CancellationToken ct);

    /// <summary>Signal that a speech pause was detected.</summary>
    void SignalPause();
}

/// <summary>Events emitted by detection strategies.</summary>
public interface IIntentDetectionEvents
{
    event Action<IntentEvent>? OnIntentDetected;
    event Action<IntentCorrectionEvent>? OnIntentCorrected;
}
```

### Strategy Implementations

```csharp
// Mode 1: Heuristic only
public class HeuristicIntentStrategy : IIntentDetectionStrategy
{
    private readonly IIntentDetector _detector;
    // Immediate detection, no async
}

// Mode 2: LLM only with buffer
public class LlmIntentStrategy : IIntentDetectionStrategy
{
    private readonly ILlmIntentDetector _llm;
    private readonly StringBuilder _buffer;
    // Buffer-based, rate-limited LLM calls
}

// Mode 3: Parallel heuristic + LLM
public class ParallelIntentStrategy : IIntentDetectionStrategy
{
    private readonly IIntentDetector _heuristic;
    private readonly ILlmIntentDetector _llm;
    private readonly StringBuilder _buffer;
    // Immediate heuristic + async LLM verification
}
```

### DI Registration

```csharp
public static class IntentDetectionServiceExtensions
{
    public static IServiceCollection AddIntentDetection(
        this IServiceCollection services,
        Action<IntentDetectionOptions> configure)
    {
        var options = new IntentDetectionOptions();
        configure(options);

        services.AddSingleton(options);

        // Register strategy based on mode
        switch (options.Mode)
        {
            case IntentDetectionMode.Heuristic:
                services.AddSingleton<IIntentDetectionStrategy, HeuristicIntentStrategy>();
                break;

            case IntentDetectionMode.Llm:
                services.AddSingleton<ILlmIntentDetector, OpenAiIntentDetector>();
                services.AddSingleton<IIntentDetectionStrategy, LlmIntentStrategy>();
                break;

            case IntentDetectionMode.Parallel:
                services.AddSingleton<ILlmIntentDetector, OpenAiIntentDetector>();
                services.AddSingleton<IIntentDetectionStrategy, ParallelIntentStrategy>();
                break;
        }

        return services;
    }
}
```

### Pipeline Integration

```csharp
public class UtteranceIntentPipeline
{
    private readonly IIntentDetectionStrategy _strategy;

    public UtteranceIntentPipeline(IIntentDetectionStrategy strategy)
    {
        _strategy = strategy;

        // Wire strategy events to pipeline events
        if (_strategy is IIntentDetectionEvents events)
        {
            events.OnIntentDetected += evt => OnIntentFinal?.Invoke(evt);
            events.OnIntentCorrected += evt => OnIntentCorrected?.Invoke(evt);
        }
    }

    // New event for corrections (Mode 3)
    public event Action<IntentCorrectionEvent>? OnIntentCorrected;
}
```

## Configuration

### appsettings.json Schema

```json
{
  "Transcription": {
    "IntentDetection": {
      "Enabled": true,
      "Mode": "Parallel",

      "Heuristic": {
        "MinConfidence": 0.4
      },

      "Llm": {
        "Model": "gpt-4o-mini",
        "ConfidenceThreshold": 0.7,
        "RateLimitMs": 2000,
        "BufferMaxChars": 800,
        "TriggerOnQuestionMark": true,
        "TriggerOnPause": true,
        "TriggerTimeoutMs": 3000,
        "EnablePreprocessing": true,
        "EnableDeduplication": true,
        "DeduplicationWindowMs": 30000
      }
    }
  }
}
```

### Options Classes

```csharp
public enum IntentDetectionMode
{
    Heuristic,  // Fast, free, lower accuracy
    Llm,        // Slower, cost, highest accuracy
    Parallel    // Fast + accurate, highest cost
}

public class IntentDetectionOptions
{
    public bool Enabled { get; set; } = true;
    public IntentDetectionMode Mode { get; set; } = IntentDetectionMode.Heuristic;
    public HeuristicOptions Heuristic { get; set; } = new();
    public LlmOptions Llm { get; set; } = new();
}

public class HeuristicOptions
{
    public double MinConfidence { get; set; } = 0.4;
}

public class LlmOptions
{
    public string Model { get; set; } = "gpt-4o-mini";
    public string? ApiKey { get; set; }
    public double ConfidenceThreshold { get; set; } = 0.7;
    public int RateLimitMs { get; set; } = 2000;
    public int BufferMaxChars { get; set; } = 800;
    public bool TriggerOnQuestionMark { get; set; } = true;
    public bool TriggerOnPause { get; set; } = true;
    public int TriggerTimeoutMs { get; set; } = 3000;
    public bool EnablePreprocessing { get; set; } = true;
    public bool EnableDeduplication { get; set; } = true;
    public int DeduplicationWindowMs { get; set; } = 30000;
}
```

## Implementation Plan

### Phase 1: Shared Infrastructure (Low effort)

- [x] Create `IntentDetectionMode` enum
- [x] Create `IntentDetectionOptions` and sub-options classes
- [x] Create `IIntentDetectionStrategy` interface
- [x] Create `IIntentDetectionEvents` interface (merged into IIntentDetectionStrategy)
- [x] Create `IntentCorrectionEvent` record
- [x] Extract shared utilities to `TranscriptionPreprocessor.cs`

**Files to create:**
```
Interview-assist-library/Pipeline/Detection/
├── IntentDetectionMode.cs
├── IntentDetectionOptions.cs
├── IIntentDetectionStrategy.cs
├── IntentCorrectionEvent.cs
└── TranscriptionPreprocessor.cs
```

### Phase 2: Heuristic Strategy (Low effort)

- [x] Create `HeuristicIntentStrategy` wrapping existing `IntentDetector`
- [x] Implement `IIntentDetectionStrategy` interface
- [ ] Add unit tests (deferred - existing IntentDetector tests cover functionality)

**Files to create:**
```
Interview-assist-library/Pipeline/Detection/
└── HeuristicIntentStrategy.cs
```

### Phase 3: LLM Infrastructure (Medium effort)

- [x] Create `ILlmIntentDetector` interface
- [x] Create `OpenAiIntentDetector` adapting existing code
- [x] Add buffer management with trigger conditions
- [x] Add rate limiting and deduplication
- [ ] Add unit tests (deferred - requires mocking OpenAI API)

**Files to create:**
```
Interview-assist-library/Pipeline/Detection/
├── ILlmIntentDetector.cs
└── OpenAiIntentDetector.cs
```

### Phase 4: LLM Strategy (Medium effort)

- [x] Create `LlmIntentStrategy` with buffer-based detection
- [x] Implement trigger conditions (?, pause, timeout)
- [x] Map LLM detections back to utterance IDs
- [ ] Add integration tests (deferred - requires live testing)

**Files to create:**
```
Interview-assist-library/Pipeline/Detection/
└── LlmIntentStrategy.cs
```

### Phase 5: Parallel Strategy (Medium effort)

- [x] Create `ParallelIntentStrategy` combining both
- [x] Implement async LLM verification
- [x] Implement intent correction logic
- [ ] Add integration tests (deferred - requires live testing)

**Files to create:**
```
Interview-assist-library/Pipeline/Detection/
└── ParallelIntentStrategy.cs
```

### Phase 6: DI Registration (Low effort)

- [x] Create `AddIntentDetection` method in `ServiceCollectionExtensions`
- [x] Register strategies based on configuration
- [x] Add OpenAI API key handling
- [x] Create `IntentDetectionOptionsBuilder` for fluent API

**Files to create:**
```
Interview-assist-library/Extensions/
└── IntentDetectionServiceExtensions.cs
```

### Phase 7: Pipeline Integration (Medium effort)

- [x] Update `UtteranceIntentPipeline` to use `IIntentDetectionStrategy`
- [x] Add `OnIntentCorrected` event
- [x] Maintain backward compatibility (strategy is optional)
- [x] Existing tests pass (258 tests)

**Files to modify:**
```
Interview-assist-library/Pipeline/Utterance/UtteranceIntentPipeline.cs
```

### Phase 8: Console Integration (Medium effort)

- [x] Update appsettings.json with new schema
- [x] Update Program.cs to read config and register strategy
- [x] Add mode indicator in UI (debug output shows mode name)
- [x] Handle intent corrections in display
- [ ] Update recording to capture mode used (deferred)

**Files to modify:**
```
Interview-assist-transcription-detection-console/
├── appsettings.json
└── Program.cs
```

### Phase 9: Testing & Validation (High effort)

- [ ] Create test fixtures from recorded sessions
- [ ] Run all three modes on same recording
- [ ] Measure and compare:
  - Precision (false positive rate)
  - Recall (detection rate)
  - Latency (time to detection)
  - Cost (API calls made)
- [ ] Document results

## Console App UX

### Status Bar Display

```
Ctrl+Q Quit | Ctrl+S Stop | Ctrl+R Record | REC | RUNNING | Mode: Parallel | Audio: Loopback | Diarize: True
```

### Intent Detection Output Format

#### Basic Format
```
[Prefix] [Subtype Confidence] Question text
```

#### Source Prefixes

| Prefix | Meaning |
|--------|---------|
| *(none)* | Detected by **heuristic** (regex-based, immediate) |
| `[LLM+]` | **Added** by LLM - heuristic missed it, LLM found it |
| `[LLM~]` | **Type changed** by LLM - heuristic said "Statement", LLM corrected to "Question" |

#### Speaker Prefix

| Prefix | Meaning |
|--------|---------|
| `S0`, `S1`, etc. | Speaker ID from diarization (only shown when diarization enabled) |

#### Question Subtypes

| Subtype | Examples |
|---------|----------|
| `Definition` | "What is X?", "Define X" |
| `How-To` | "How do I...", "How can I..." |
| `Compare` | "What's the difference between X and Y?" |
| `Troubleshoot` | "Why doesn't...", "Why is X not working?" |
| `Question` | Generic question (no specific subtype detected) |

#### Confidence Score

The number (e.g., `0.4`, `0.7`) indicates detection confidence:
- `0.4` - Low confidence (heuristic default)
- `0.5-0.7` - Medium confidence
- `0.8-1.0` - High confidence

#### Examples

```
Detected Intents:
────────────────────────────────────────
[S0 Question 0.4] how many people it would take to pay that?
[S1 Definition 0.5] What is dependency injection?
[LLM+] [Compare] What's the difference between abstract class and interface?
[LLM~] [How-To] How do you implement the singleton pattern?
```

- Line 1: Speaker 0, generic question, low confidence, detected by heuristic
- Line 2: Speaker 1, definition question, medium confidence, detected by heuristic
- Line 3: LLM found this question that heuristic missed
- Line 4: Heuristic thought it was a Statement, LLM corrected it to a Question

### Debug Output

```
[14:30:15.123] [Intent.candidate] Question/Definition conf=0.40
[14:30:15.456] [Intent.final] Question/Definition conf=0.40
[14:30:15.892] [Utterance.final] utt_0042: "What is dependency inject..." (SilenceGap)
[14:30:16.234] [Intent.corrected] Added: None → Question
[14:30:16.235] [Intent.corrected] TypeChanged: Statement → Question
```

## Comparison Matrix

| Aspect | Heuristic | LLM | Parallel |
|--------|-----------|-----|----------|
| **Latency (first response)** | <1ms | 200-500ms | <1ms |
| **Latency (authoritative)** | <1ms | 200-500ms | 200-500ms |
| **Cost/hour** | $0 | ~$0.27 | ~$0.27 |
| **Recall** | ~67% | ~95% | ~95% |
| **Precision** | Medium | High | High |
| **Fragmentation handling** | Poor | Good | Good |
| **Pronoun resolution** | No | Yes | Yes |
| **Offline capable** | Yes | No | Partial |
| **Corrections** | No | N/A | Yes |

## Success Criteria

1. All three modes work with recorded session playback
2. Configuration switches mode without code changes
3. Parallel mode shows corrections in UI
4. LLM modes achieve >90% recall on test recordings
5. Cost stays under $0.50/hour at default settings

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| LLM API failures | Fallback to heuristic-only |
| High latency | Timeout + skip, use heuristic result |
| Cost overrun | Rate limiting, mode toggle |
| Complex debugging | Detailed logging per mode |

## References

- ADR-003: LLM Question Detection
- Existing implementations:
  - `OpenAiQuestionDetectionService.cs`
  - `LlmQuestionDetector.cs`
  - `IntentDetector.cs`

---

## Implementation Summary

### Completed: 2026-02-01

### Files Created

| File | Purpose |
|------|---------|
| `Pipeline/Detection/IntentDetectionMode.cs` | Enum defining three detection modes |
| `Pipeline/Detection/IntentDetectionOptions.cs` | Configuration classes for all detection options |
| `Pipeline/Detection/IIntentDetectionStrategy.cs` | Strategy interface + IntentCorrectionEvent record |
| `Pipeline/Detection/TranscriptionPreprocessor.cs` | Shared preprocessing utilities |
| `Pipeline/Detection/HeuristicIntentStrategy.cs` | Mode 1: Wraps existing IntentDetector |
| `Pipeline/Detection/ILlmIntentDetector.cs` | Interface for LLM-based detection |
| `Pipeline/Detection/OpenAiIntentDetector.cs` | OpenAI GPT implementation |
| `Pipeline/Detection/LlmIntentStrategy.cs` | Mode 2: Buffer-based LLM detection |
| `Pipeline/Detection/ParallelIntentStrategy.cs` | Mode 3: Heuristic + async LLM verification |

### Files Modified

| File | Changes |
|------|---------|
| `Extensions/ServiceCollectionExtensions.cs` | Added `AddIntentDetection()` method and `IntentDetectionOptionsBuilder` |
| `Pipeline/Utterance/UtteranceIntentPipeline.cs` | Added optional strategy parameter, `OnIntentCorrected` event, `DetectionModeName` property |
| `Pipeline/Recording/RecordedEvent.cs` | Added `IntentDetectionMode` to `SessionConfig`, added `RecordedIntentCorrectionEvent` type |
| `Pipeline/Recording/SessionRecorder.cs` | Added subscription to `OnIntentCorrected` event for recording LLM corrections |
| `Interview-assist-transcription-detection-console/appsettings.json` | Added full IntentDetection configuration schema |
| `Interview-assist-transcription-detection-console/Program.cs` | Added strategy factory, options loading, correction event handling, API key validation, status bar mode display, confidence in intent labels |

### Build & Test Results

- **Build:** Succeeded with 35 warnings (pre-existing)
- **Unit Tests:** 258 passed, 0 failed
- **Breaking Changes:** None (strategy is optional, backward compatible)

### Usage

Change mode in `appsettings.json`:
```json
"IntentDetection": {
  "Mode": "Parallel"  // or "Heuristic", "Llm"
}
```

For LLM/Parallel modes, set `OPENAI_API_KEY` environment variable or add `Llm.ApiKey` in config.

### Console App Improvements

| Feature | Description |
|---------|-------------|
| **API key validation** | Upfront validation for LLM/Parallel modes before starting UI |
| **Status bar mode display** | Shows `Mode: {Heuristic|LLM|Parallel}` in status bar |
| **Confidence display** | Intent labels include confidence score (e.g., `[Question 0.4]`) |
| **LLM correction display** | Shows `[LLM+]` for added intents, `[LLM~]` for type-changed intents |
| **Recording captures mode** | Session recordings include `IntentDetectionMode` in metadata |
| **Intent auto-scroll** | Intent panel auto-scrolls to show latest detected intent |

### Remaining Work

- Integration tests with recorded sessions
- Performance/accuracy comparison across modes
