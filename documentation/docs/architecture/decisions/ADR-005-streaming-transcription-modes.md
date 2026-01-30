# ADR-005: Streaming Transcription Modes

## Status

Accepted

## Context

The existing transcription approach batches audio and sends it to Whisper API, immediately treating all returned text as final. This creates two problems:

1. **Latency vs Accuracy Trade-off**: Longer batches produce more accurate transcription but higher latency; shorter batches reduce latency but increase transcription errors.

2. **Text Flickering**: When using short batches for low latency, the same word/phrase may be transcribed differently across consecutive batches, causing visible "flickering" in the UI.

Research into streaming transcription systems (WhisperStreaming, Gladia) revealed techniques for distinguishing "stable" text (confirmed, won't change) from "provisional" text (may be revised). This allows displaying provisional text immediately for low latency while only committing stable text.

### Requirements Driving This Decision

- Reduce perceived latency for real-time display
- Improve transcription accuracy through revision
- Support different trade-offs for different use cases
- Provide clear visual feedback about text stability

## Decision

Implement three streaming transcription modes behind a common `IStreamingTranscriptionService` interface:

### Mode 1: Basic

All text immediately treated as stable. Equivalent to existing behavior with added context prompting.

**Best for:** Applications where simplicity matters more than accuracy, or when using high-quality audio.

### Mode 2: Revision

Overlapping batches with local agreement policy. Text confirmed by N consecutive transcriptions becomes stable.

**Best for:** High-accuracy requirements where moderate latency is acceptable.

### Mode 3: Hypothesis

Rapid hypothesis updates with stability tracking. Text unchanged for N iterations OR timeout becomes stable.

**Best for:** Lowest-latency real-time display with acceptable accuracy.

## Implementation Details

### Interface Design

```csharp
public interface IStreamingTranscriptionService : IAsyncDisposable
{
    event Action<StableTextEventArgs>? OnStableText;      // Confirmed, won't change
    event Action<ProvisionalTextEventArgs>? OnProvisionalText; // May be revised
    event Action<HypothesisEventArgs>? OnFullHypothesis;  // Full context

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    string GetStableTranscript();
    string GetProvisionalTranscript();
}
```

### Revision Mode Algorithm

```
Audio Stream: |----A----|----B----|----C----|----D----|
Batch 1:      |========|
Batch 2:           |========|
Batch 3:                |========|
Overlap:           |====|    |====|
```

1. Maintain overlapping windows (default: 2s batches, 1.5s overlap)
2. Transcribe each position multiple times
3. Find longest common prefix across N consecutive transcripts
4. Text agreed by N iterations becomes stable
5. Remainder is provisional

### Hypothesis Mode Algorithm

1. Transcribe rapidly (default: every 250ms)
2. Track stability via iteration count + timeout
3. Text unchanged for N iterations OR timeout becomes stable
4. Apply anti-flickering cooldown to provisional updates

## Consequences

### Positive

- **Flexibility**: Choose the right mode for each use case
- **Improved Accuracy**: Revision mode catches Whisper's common errors
- **Lower Perceived Latency**: Provisional text displays immediately
- **Clear Stability Feedback**: UI can distinguish tentative vs confirmed text

### Negative

- **Complexity**: Three implementations to maintain
- **Higher API Costs**: Revision/Hypothesis modes make more API calls
- **Learning Curve**: Users must understand mode differences
- **Configuration Surface**: More options to tune

## Configuration

### Via appsettings.json

```json
{
  "Transcription": {
    "Mode": "Revision",
    "VocabularyPrompt": "C#, async, await, IEnumerable",

    "Basic": { "BatchMs": 3000, "MaxBatchMs": 6000 },

    "Revision": {
      "OverlapMs": 1500,
      "BatchMs": 2000,
      "AgreementCount": 2,
      "SimilarityThreshold": 0.85
    },

    "Hypothesis": {
      "MinBatchMs": 500,
      "UpdateIntervalMs": 250,
      "StabilityIterations": 3,
      "StabilityTimeoutMs": 2000
    }
  }
}
```

### Via DI

```csharp
services.AddStreamingTranscription(opts => opts
    .WithApiKey(apiKey)
    .WithMode(TranscriptionMode.Revision)
    .WithContextPrompting(true, maxChars: 200, vocabulary: "C#, async"));
```

### Via Command Line

```bash
# Legacy mode (default, with question detection)
Interview-assist-transcription-console --mode legacy

# Basic mode
Interview-assist-transcription-console --mode basic --mic

# Revision mode (recommended for accuracy)
Interview-assist-transcription-console --mode revision

# Hypothesis mode (lowest latency)
Interview-assist-transcription-console --mode hypothesis --vocab "C#, async"
```

## References

- [WhisperStreaming - UFAL GitHub](https://github.com/ufal/whisper_streaming) - Local agreement policy
- [Gladia Real-Time Transcription](https://www.gladia.io/blog/real-time-transcription-powered-by-whisper-asr) - Partial vs final results
