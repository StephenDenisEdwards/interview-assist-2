# ADR-005: Streaming Transcription Modes

## Status

Superseded - Deepgram selected as primary transcription provider

## Context

The existing transcription approach batches audio and sends it to Whisper API, immediately treating all returned text as final. This creates two problems:

1. **Latency vs Accuracy Trade-off**: Longer batches produce more accurate transcription but higher latency; shorter batches reduce latency but increase transcription errors.

2. **Text Flickering**: When using short batches for low latency, the same word/phrase may be transcribed differently across consecutive batches, causing visible "flickering" in the UI.

Research into streaming transcription systems (WhisperStreaming, Gladia) revealed techniques for distinguishing "stable" text (confirmed, won't change) from "provisional" text (may be revised). This allows displaying provisional text immediately for low latency while only committing stable text.

### Evaluated Approaches

After implementing and testing multiple approaches, the following were evaluated:

| Approach | Latency | Accuracy | Complexity | Cost |
|----------|---------|----------|------------|------|
| Whisper Basic | High | Good | Low | Medium |
| Whisper Revision | Medium | Very Good | High | High |
| Whisper Hypothesis | Low | Fair | High | Very High |
| **Deepgram Streaming** | **Very Low** | **Very Good** | **Low** | **Low** |

**Deepgram emerged as the clear winner** due to:
- **Native streaming support**: Built for real-time from the ground up, not retrofitted onto a batch API
- **Native interim/final results**: No need for complex local agreement algorithms
- **Lower latency**: Purpose-built WebSocket streaming with sub-300ms latency
- **Better accuracy**: Nova-2 model performs excellently on conversational speech
- **Simpler implementation**: Single WebSocket connection, no overlapping batch management
- **Lower cost**: Pay per audio duration, not per API call (critical for high-frequency hypothesis mode)
- **Built-in features**: VAD, endpointing, smart formatting, keyword boosting included

### Requirements Driving This Decision

- Reduce perceived latency for real-time display
- Improve transcription accuracy through revision
- Support different trade-offs for different use cases
- Provide clear visual feedback about text stability

## Decision

**Use Deepgram as the primary streaming transcription provider.** The Whisper-based modes are retained for scenarios where Deepgram is unavailable or when offline transcription is required.

All modes are implemented behind a common `IStreamingTranscriptionService` interface:

### Mode 1: Basic

All text immediately treated as stable. Equivalent to existing behavior with added context prompting.

**Best for:** Applications where simplicity matters more than accuracy, or when using high-quality audio.

### Mode 2: Revision

Overlapping batches with local agreement policy. Text confirmed by N consecutive transcriptions becomes stable.

**Best for:** High-accuracy requirements where moderate latency is acceptable.

### Mode 3: Hypothesis

Rapid hypothesis updates with stability tracking. Text unchanged for N iterations OR timeout becomes stable.

**Best for:** Lowest-latency real-time display with acceptable accuracy.

### Mode 4: Deepgram (Recommended)

Native streaming transcription via Deepgram's Nova-2 model over WebSocket. Provides true interim and final results without client-side agreement algorithms.

**Best for:** All real-time transcription use cases. Offers the best combination of latency, accuracy, and simplicity.

**Key Features:**
- **Native interim/final results**: Deepgram distinguishes `is_final: false` (interim) from `is_final: true` (final) natively
- **Endpointing**: Automatic speech boundary detection (configurable, default 300ms)
- **Utterance end detection**: Signals when a speaker has finished (configurable, default 1000ms)
- **VAD (Voice Activity Detection)**: Built-in, reduces unnecessary processing
- **Smart formatting**: Automatic formatting of numbers, dates, currencies
- **Keyword boosting**: Boost recognition of domain-specific terms
- **Speaker diarization**: Identifies and labels different speakers in the audio stream (optional)

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

### Deepgram Mode Architecture

```
Audio Input (Mic/Loopback via NAudio)
    → IAudioCaptureService
    → Bounded channel queue (64 capacity, DropOldest)
    → WebSocket send (raw PCM, 16kHz mono)
    → Deepgram API (wss://api.deepgram.com/v1/listen)
    → JSON responses with interim/final results
    → OnProvisionalText / OnStableText events
```

**Key Implementation Details:**
- Single WebSocket connection with keep-alive heartbeat (10s interval)
- Audio sent as raw binary PCM (no base64 encoding overhead)
- Three concurrent tasks: receive loop, send loop, keep-alive loop
- Graceful shutdown with `CloseStream` message before WebSocket close

**Message Types Handled:**
- `Results`: Transcription with `is_final` and `speech_final` flags
- `Metadata`: Session info including request ID
- `UtteranceEnd`: Promotes any pending provisional text to stable
- `SpeechStarted`: VAD detected speech beginning
- `Error`: API errors with descriptive messages

**Speaker Diarization:**
When enabled (`Diarize: true`), Deepgram identifies different speakers in the audio:
- Each word in the response includes a `speaker` field (0-based index)
- Speaker changes are detected and displayed with labels: `[Speaker 0]`, `[Speaker 1]`, etc.
- Console output uses color-coding to distinguish speakers visually
- Speaker info is included in `StableTextEventArgs.Speaker` and `ProvisionalTextEventArgs.Speaker`

## Consequences

### Positive

- **Flexibility**: Choose the right mode for each use case
- **Improved Accuracy**: Deepgram Nova-2 provides excellent accuracy; Revision mode catches Whisper's common errors
- **Lower Perceived Latency**: Provisional text displays immediately
- **Clear Stability Feedback**: UI can distinguish tentative vs confirmed text
- **Simplified Architecture (Deepgram)**: No complex overlapping batch management or local agreement algorithms
- **Cost Effective (Deepgram)**: Pay-per-minute pricing vs pay-per-request, significant savings for real-time use
- **Production Ready (Deepgram)**: Enterprise-grade streaming infrastructure with built-in features
- **Speaker Diarization (Deepgram)**: Built-in speaker identification for multi-party conversations

### Negative

- **Complexity**: Four implementations to maintain (though Deepgram is significantly simpler)
- **Higher API Costs**: Whisper Revision/Hypothesis modes make more API calls
- **Learning Curve**: Users must understand mode differences
- **Configuration Surface**: More options to tune
- **External Dependency (Deepgram)**: Requires Deepgram account and API key; no offline option

## Configuration

### Via appsettings.json

```json
{
  "Transcription": {
    "Mode": "Deepgram",
    "VocabularyPrompt": "C#, async, await, IEnumerable",

    "Deepgram": {
      "ApiKey": "your-deepgram-api-key",
      "Model": "nova-2",
      "Language": "en",
      "InterimResults": true,
      "Punctuate": true,
      "SmartFormat": true,
      "EndpointingMs": 300,
      "UtteranceEndMs": 1000,
      "Keywords": "C#, async, await, Kubernetes",
      "Vad": true,
      "Diarize": false
    },

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
# Deepgram mode (recommended - best latency and accuracy)
Interview-assist-transcription-console --mode deepgram
Interview-assist-transcription-console --mode deepgram --mic --keywords "C#, async, Kubernetes"

# Deepgram with speaker diarization (identifies different speakers)
Interview-assist-transcription-console --mode deepgram --diarize

# Legacy mode (with question detection)
Interview-assist-transcription-console --mode legacy

# Basic mode (Whisper)
Interview-assist-transcription-console --mode basic --mic

# Revision mode (Whisper, higher accuracy)
Interview-assist-transcription-console --mode revision

# Hypothesis mode (Whisper, lowest latency)
Interview-assist-transcription-console --mode hypothesis --vocab "C#, async"
```

## References

- [Deepgram Streaming API](https://developers.deepgram.com/docs/getting-started-with-live-streaming-audio) - Native streaming transcription
- [Deepgram Nova-2 Model](https://deepgram.com/learn/nova-2-speech-to-text-api) - Production speech-to-text model
- [Deepgram Diarization](https://developers.deepgram.com/docs/diarization) - Speaker identification in audio streams
- [WhisperStreaming - UFAL GitHub](https://github.com/ufal/whisper_streaming) - Local agreement policy
- [Gladia Real-Time Transcription](https://www.gladia.io/blog/real-time-transcription-powered-by-whisper-asr) - Partial vs final results
