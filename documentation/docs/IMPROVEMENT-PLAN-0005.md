# IMPROVEMENT-PLAN-0005: Streaming Transcription Services

**Created:** 2026-01-29
**Status:** Planned

## Overview

Implement three streaming transcription services with DI-based mode selection to improve transcription quality through incremental revision and hypothesis updates.

### Modes

- **Basic**: Current approach + context prompting (all text immediately stable)
- **Revision**: Overlapping batches with local agreement policy
- **Streaming**: Real-time hypothesis updates with stability tracking

All modes emit rich events: `OnStableText`, `OnProvisionalText`, `OnFullHypothesis`

---

## Tasks

### Phase 1: Foundation

- [ ] Create `StreamingTranscriptionModels.cs` - Event args records + `TranscriptionMode` enum
- [ ] Create `IStreamingTranscriptionService.cs` - Interface with stability-aware events
- [ ] Create `StreamingTranscriptionOptions.cs` - Configuration record for all modes
- [ ] Create `StreamingTranscriptionOptionsBuilder.cs` - Fluent builder for options

### Phase 2: Utilities + Basic Mode

- [ ] Create `TranscriptionTextComparer.cs` - Shared utilities (Jaccard similarity, common prefix)
- [ ] Create `BasicTranscriptionService.cs` - Basic mode implementation
- [ ] Update `TranscriptionConstants.cs` - Add default values for new options

### Phase 3: Revision Mode

- [ ] Create `RevisionTranscriptionService.cs` - Overlapping windows + local agreement

### Phase 4: Streaming Mode

- [ ] Create `StreamingHypothesisService.cs` - Rapid hypothesis + flickering mitigation

### Phase 5: Integration

- [ ] Update `ServiceCollectionExtensions.cs` - Add `AddStreamingTranscription()` method
- [ ] Update console apps for testing
- [ ] Create unit tests for each service
- [ ] Update documentation (ADR, SAD)

---

## File Structure

### New Files (Interview-assist-library/Transcription/)

| File | Purpose |
|------|---------|
| `IStreamingTranscriptionService.cs` | Interface with stability-aware events |
| `StreamingTranscriptionModels.cs` | Event args records + `TranscriptionMode` enum |
| `StreamingTranscriptionOptions.cs` | Configuration record for all modes |
| `StreamingTranscriptionOptionsBuilder.cs` | Fluent builder for options |
| `BasicTranscriptionService.cs` | Basic mode implementation |
| `RevisionTranscriptionService.cs` | Revision mode with overlapping windows |
| `StreamingHypothesisService.cs` | Streaming mode with rapid hypothesis |
| `TranscriptionTextComparer.cs` | Shared utilities (Jaccard similarity, common prefix) |

### Modified Files

| File | Changes |
|------|---------|
| `ServiceCollectionExtensions.cs` | Add `AddStreamingTranscription()` method |
| `TranscriptionConstants.cs` | Add default values for new options |

---

## Interface Design

```csharp
public interface IStreamingTranscriptionService : IAsyncDisposable
{
    event Action<StableTextEventArgs>? OnStableText;      // Confirmed text
    event Action<ProvisionalTextEventArgs>? OnProvisionalText; // May change
    event Action<HypothesisEventArgs>? OnFullHypothesis;  // Full context
    event Action<string>? OnInfo;
    event Action<string>? OnWarning;
    event Action<Exception>? OnError;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    string GetStableTranscript();
    string GetProvisionalTranscript();
}
```

### Event Args

```csharp
public record StableTextEventArgs
{
    public required string Text { get; init; }
    public long StreamOffsetMs { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int ConfirmationCount { get; init; } = 1;
}

public record ProvisionalTextEventArgs
{
    public required string Text { get; init; }
    public double? Confidence { get; init; }
    public long StreamOffsetMs { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public record HypothesisEventArgs
{
    public required string FullText { get; init; }
    public required string StableText { get; init; }
    public required string ProvisionalText { get; init; }
    public double StabilityRatio { get; init; }
    public TimeSpan TimeSinceLastStable { get; init; }
}

public enum TranscriptionMode { Basic, Revision, Streaming }
```

---

## Configuration (appsettings.json)

```json
{
  "Transcription": {
    "Mode": "Revision",
    "Language": "en",
    "SilenceThreshold": 0.01,
    "EnableContextPrompting": true,
    "ContextPromptMaxChars": 200,
    "VocabularyPrompt": "C#, async, await, IEnumerable, ConfigureAwait",

    "Basic": { "BatchMs": 3000, "MaxBatchMs": 6000 },

    "Revision": {
      "OverlapMs": 1500,
      "BatchMs": 2000,
      "AgreementCount": 2,
      "SimilarityThreshold": 0.85
    },

    "Streaming": {
      "MinBatchMs": 500,
      "UpdateIntervalMs": 250,
      "StabilityIterations": 3,
      "StabilityTimeoutMs": 2000,
      "FlickerCooldownMs": 100
    }
  }
}
```

---

## Key Algorithms

### Basic Mode
1. Batch audio (3-6s window with silence detection)
2. Build context prompt from recent stable text + vocabulary
3. Call Whisper API with prompt
4. Filter via `TranscriptQualityFilter`
5. Emit all text as stable immediately

### Revision Mode
1. Maintain overlapping windows (2s batches, 1.5s overlap)
2. Transcribe each position multiple times
3. Find longest common prefix across N consecutive transcripts
4. Text agreed by N iterations becomes stable
5. Remainder is provisional

```
Audio Stream: |----A----|----B----|----C----|----D----|
Batch 1:      |========|
Batch 2:           |========|
Batch 3:                |========|
Overlap:           |====|    |====|
```

### Streaming Mode
1. Transcribe rapidly (every 250ms)
2. Track stability via iteration count + timeout
3. Text unchanged for N iterations OR timeout becomes stable
4. Apply anti-flickering cooldown to provisional updates

---

## DI Registration

```csharp
// Enable streaming transcription with mode selection
services.AddStreamingTranscription(opts => opts
    .WithApiKey(apiKey)
    .WithMode(TranscriptionMode.Revision)
    .WithContextPrompting(true, maxChars: 200, vocabulary: "C#, async")
    .WithRevisionOptions(overlapMs: 1500, agreementCount: 2));
```

---

## Critical Files to Reference

- `OpenAiMicTranscriber.cs` - Batching, WAV building, Whisper API calls
- `TranscriptBuffer.cs` - Thread-safe buffer pattern
- `TranscriptQualityFilter.cs` - Hallucination filtering
- `ServiceCollectionExtensions.cs` - DI patterns

---

## Research References

- [WhisperStreaming - UFAL GitHub](https://github.com/ufal/whisper_streaming) - Local agreement policy
- [Flickering Reduction - IEEE](https://ieeexplore.ieee.org/document/10023016) - Stability techniques
- [Gladia Real-Time Transcription](https://www.gladia.io/blog/real-time-transcription-powered-by-whisper-asr) - Partial vs final results

---

## Verification

1. **Build**: `dotnet build interview-assist-2.sln`
2. **Unit Tests**: Test each service in isolation
3. **Manual Test**: Run pipeline-console with each mode
4. **Compare Modes**: Same audio, different modes, observe stability behavior
