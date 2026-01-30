# IMPROVEMENT-PLAN-0006: Hypothesis Mode Redesign with Streaming-Native APIs

**Created:** 2025-01-30
**Status:** Planning

## Problem Statement

The current Hypothesis mode implementation using repeated Whisper batch API calls is fundamentally broken. This document captures the findings and proposes two alternative implementation approaches.

---

## Findings

### Why Whisper Batch API Doesn't Work for Hypothesis Mode

The original Hypothesis mode design assumed:

| Assumption | Reality |
|------------|---------|
| Whisper returns consistent results for same audio | Whisper is non-deterministic; same audio produces different punctuation, wording |
| Repeated transcription of same audio stabilizes | Each call returns different text, stability threshold never reached |
| Growing audio buffer improves context | Longer buffers cause Whisper to hallucinate (counting numbers, random words) |
| String comparison detects stability | "Hello" vs "Hello." vs "Hello," are different strings but same content |

### Observed Failures

1. **Buffer growth without stability**: Audio buffer grows from 0.5s → 1.7s+ because text never matches 3 times
2. **Hallucination with long buffers**: "PEWDS", "2 3 4 5 6 7 8 9 10...", "Fresh Vodka" - garbage unrelated to audio
3. **Aggressive silence promotion**: Any silent chunk promotes whatever provisional text exists
4. **No relationship between transcriptions**: After buffer clear, new transcription is unrelated to previous stable text

### The Correct Mental Model

**Core insight**: Speech is temporally ambiguous. Meaning often depends on later words.

Example:
```
Audio: "What is a lock statement used for in C sharp"

At T=1s: "What is a lock"
         └── Is "lock" a noun? verb? part of compound?

At T=2s: "What is a lock statement used"
         └── Is this a question? Is "used" the main verb?

At T=3s: "What is a lock statement used for in C sharp"
         └── NOW we know: question, "lock statement" = compound noun, C# context
```

**Corrections are not a bug—they're the feature.** The system should revise earlier guesses as context clarifies meaning.

**Stability isn't about repetition—it's about trailing context.** Text becomes stable not because an API returned it N times, but because we've heard enough subsequent audio to resolve ambiguity.

---

## Research: APIs with Native Interim/Final Support

### Comparison Table

| API | Model | Latency | Pricing | Key Features |
|-----|-------|---------|---------|--------------|
| **Deepgram** | `is_final: true/false` | ~150ms | $0.0077/min | Endpointing, utterance detection, keyword boosting |
| **OpenAI Realtime** | `delta` + `completed` events | ~150-300ms | ~$0.06/min | VAD built-in, already in project |
| **Google Cloud STT** | `interimResults` param | ~300ms | $0.016/min | 10MB stream limit |
| **AssemblyAI** | Partial + Final | ~300ms | $0.015/min | WebSocket, multilingual |
| **Azure Speech** | Intermediate results | ~200ms | $0.016/min | Real-time + batch modes |

### Deepgram Details

- **Interim Results**: Preliminary transcripts marked `is_final: false`, finalized with `is_final: true`
- **Endpointing**: Detects pauses, returns `speech_final: true` when speaker stops
- **Utterance End**: Analyzes interim/final results to identify gaps after last finalized word
- **Latency**: First interim results within ~150ms
- **Documentation**: https://developers.deepgram.com/docs/interim-results

### OpenAI Realtime API Details

- **Models**: `gpt-4o-transcribe`, `gpt-4o-mini-transcribe` support true streaming deltas
- **Events**: `conversation.item.input_audio_transcription.delta` (interim), `.completed` (final)
- **VAD**: Built-in voice activity detection, controls when transcription begins
- **Mode**: Supports transcription-only mode (no response generation)
- **Documentation**: https://platform.openai.com/docs/guides/realtime-transcription
- **Note**: Already used in project's `OpenAiRealtimeApi.cs`

---

## Option A: Deepgram Implementation

### Rationale

- Purpose-built for real-time transcription with interim/final model
- Cheapest option (~$0.0077/min vs ~$0.06/min for OpenAI Realtime)
- Explicit `is_final` flag matches our mental model exactly
- Configurable endpointing for fine-tuning pause detection
- Well-documented WebSocket API

### Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│ Audio Capture   │────▶│ Deepgram WebSocket│────▶│ Event Handler   │
│ (existing)      │     │ Connection        │     │                 │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                │                         │
                                ▼                         ▼
                        ┌──────────────────┐     ┌─────────────────┐
                        │ interim result   │     │ OnProvisional   │
                        │ is_final: false  │     │ OnStableText    │
                        └──────────────────┘     │ OnRevision      │
                        ┌──────────────────┐     └─────────────────┘
                        │ final result     │
                        │ is_final: true   │
                        └──────────────────┘
```

### New Files

| File | Purpose |
|------|---------|
| `Interview-assist-library/Transcription/DeepgramTranscriptionService.cs` | Main service implementation |
| `Interview-assist-library/Transcription/DeepgramOptions.cs` | Configuration (API key, model, endpointing) |
| `Interview-assist-library/Transcription/DeepgramModels.cs` | Response DTOs for WebSocket messages |

### Modified Files

| File | Changes |
|------|---------|
| `ServiceCollectionExtensions.cs` | Add `AddDeepgramTranscription()` method |
| `StreamingTranscriptionModels.cs` | Add `OnRevision` event args for corrections |
| `appsettings.json` | Add Deepgram configuration section |

### Tasks

#### Phase 1: Foundation
- [ ] Add Deepgram NuGet package or implement WebSocket client
- [ ] Create `DeepgramOptions.cs` with API key, model, endpointing settings
- [ ] Create `DeepgramModels.cs` with WebSocket message DTOs

#### Phase 2: Core Implementation
- [ ] Create `DeepgramTranscriptionService.cs` implementing `IStreamingTranscriptionService`
- [ ] Implement WebSocket connection lifecycle (connect, reconnect, disconnect)
- [ ] Implement audio streaming to Deepgram
- [ ] Parse interim/final results from WebSocket messages

#### Phase 3: Event Mapping
- [ ] Map `is_final: false` → `OnProvisionalText`
- [ ] Map `is_final: true` → `OnStableText`
- [ ] Implement revision detection (when final differs from last interim)
- [ ] Add `OnRevision` event for corrections

#### Phase 4: Integration
- [ ] Add `TranscriptionMode.Deepgram` enum value
- [ ] Update `ServiceCollectionExtensions.cs`
- [ ] Update console app for testing
- [ ] Add Deepgram API key to configuration

#### Phase 5: Testing & Documentation
- [ ] Create unit tests with mocked WebSocket
- [ ] Create integration tests with real API
- [ ] Update ADR-005 or create ADR-006
- [ ] Update appsettings.md

### Configuration

```json
{
  "Transcription": {
    "Mode": "Deepgram",
    "Deepgram": {
      "ApiKey": "...",
      "Model": "nova-2",
      "Language": "en",
      "InterimResults": true,
      "Endpointing": 300,
      "UtteranceEndMs": 1000,
      "SmartFormat": true,
      "Punctuate": true
    }
  }
}
```

---

## Option B: OpenAI Realtime API Implementation

### Rationale

- Already used in project (`OpenAiRealtimeApi.cs`)
- No new API dependency
- `gpt-4o-transcribe` supports true streaming deltas
- VAD built-in, handles voice activity detection automatically
- Transcription-only mode available (no response generation overhead)

### Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│ Audio Capture   │────▶│ OpenAI Realtime  │────▶│ Event Handler   │
│ (existing)      │     │ WebSocket        │     │                 │
└─────────────────┘     │ (existing)       │     └─────────────────┘
                        └──────────────────┘              │
                                │                         ▼
                                ▼                 ┌─────────────────┐
                        ┌──────────────────────┐  │ OnProvisional   │
                        │ transcription.delta  │  │ OnStableText    │
                        │ transcription.done   │  │ OnRevision      │
                        └──────────────────────┘  └─────────────────┘
```

### Approach

Refactor existing `OpenAiRealtimeApi.cs` or create new service that:
1. Uses transcription-only mode (disables response generation)
2. Uses `gpt-4o-transcribe` or `gpt-4o-mini-transcribe` model
3. Listens to `conversation.item.input_audio_transcription.delta` for interim results
4. Listens to `conversation.item.input_audio_transcription.completed` for final results

### New Files

| File | Purpose |
|------|---------|
| `Interview-assist-library/Transcription/RealtimeTranscriptionService.cs` | Transcription-only wrapper around Realtime API |

### Modified Files

| File | Changes |
|------|---------|
| `OpenAiRealtimeApi.cs` | Extract transcription event handling (or reuse) |
| `RealtimeApiOptions.cs` | Add transcription-only mode flag |
| `ServiceCollectionExtensions.cs` | Add registration for transcription-only mode |

### Tasks

#### Phase 1: Analysis
- [ ] Review existing `OpenAiRealtimeApi.cs` for transcription event handling
- [ ] Identify what can be reused vs needs new implementation
- [ ] Determine if wrapper or refactor is better approach

#### Phase 2: Core Implementation
- [ ] Create `RealtimeTranscriptionService.cs` or refactor existing
- [ ] Configure session for transcription-only mode
- [ ] Use `gpt-4o-transcribe` model for streaming deltas
- [ ] Handle `transcription.delta` events for interim results
- [ ] Handle `transcription.completed` events for final results

#### Phase 3: Event Mapping
- [ ] Map delta events → `OnProvisionalText`
- [ ] Map completed events → `OnStableText`
- [ ] Track previous deltas to detect revisions
- [ ] Add `OnRevision` event for corrections

#### Phase 4: Integration
- [ ] Add `TranscriptionMode.RealtimeApi` enum value
- [ ] Update `ServiceCollectionExtensions.cs`
- [ ] Update console app for testing
- [ ] Ensure existing OpenAI API key works

#### Phase 5: Testing & Documentation
- [ ] Create unit tests
- [ ] Create integration tests
- [ ] Update documentation

### Configuration

```json
{
  "Transcription": {
    "Mode": "RealtimeApi",
    "RealtimeApi": {
      "Model": "gpt-4o-transcribe",
      "TranscriptionOnly": true,
      "VadEnabled": true,
      "VadThreshold": 0.5,
      "SilenceDurationMs": 500
    }
  }
}
```

---

## Comparison: Option A vs Option B

| Aspect | Option A (Deepgram) | Option B (OpenAI Realtime) |
|--------|---------------------|---------------------------|
| **Cost** | ~$0.0077/min | ~$0.06/min |
| **New dependency** | Yes (new API) | No (already in project) |
| **Implementation effort** | Higher (new WebSocket client) | Lower (refactor existing) |
| **Interim/Final model** | Explicit `is_final` flag | Delta + completed events |
| **Endpointing control** | Configurable (ms) | VAD-based |
| **Accuracy** | Nova-2 model, highly optimized | GPT-4o transcribe |
| **Latency** | ~150ms | ~150-300ms |

### Recommendation

**Start with Option B** (OpenAI Realtime API) because:
1. No new API dependency
2. Lower implementation effort (code already exists)
3. Faster path to working solution

**Consider Option A** (Deepgram) if:
1. Cost becomes a concern (8x cheaper)
2. More control over endpointing is needed
3. OpenAI Realtime API has limitations

---

## Event Model Update

Regardless of which option is chosen, the event model should be updated:

```csharp
public interface IStreamingTranscriptionService : IAsyncDisposable
{
    // Existing events
    event Action<StableTextEventArgs>? OnStableText;
    event Action<ProvisionalTextEventArgs>? OnProvisionalText;
    event Action<HypothesisEventArgs>? OnFullHypothesis;

    // New event for corrections
    event Action<RevisionEventArgs>? OnRevision;

    event Action<string>? OnInfo;
    event Action<string>? OnWarning;
    event Action<Exception>? OnError;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    string GetStableTranscript();
    string GetProvisionalTranscript();
}

/// <summary>
/// Event args for when previously emitted text is corrected.
/// </summary>
public record RevisionEventArgs
{
    /// <summary>
    /// The text that was previously emitted (to be replaced).
    /// </summary>
    public required string OriginalText { get; init; }

    /// <summary>
    /// The corrected text.
    /// </summary>
    public required string CorrectedText { get; init; }

    /// <summary>
    /// Position/offset where the revision starts.
    /// </summary>
    public int StartOffset { get; init; }

    /// <summary>
    /// Timestamp of the revision.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
```

---

## References

- [Deepgram Interim Results Documentation](https://developers.deepgram.com/docs/interim-results)
- [Deepgram Endpointing Documentation](https://developers.deepgram.com/docs/endpointing)
- [OpenAI Realtime API Transcription Guide](https://platform.openai.com/docs/guides/realtime-transcription)
- [OpenAI Realtime API Reference](https://platform.openai.com/docs/api-reference/realtime)
- [Speech-to-Text API Pricing Comparison 2025](https://deepgram.com/learn/speech-to-text-api-pricing-breakdown-2025)

---

# Appendix A: Research - Live TV Broadcast Captioning Technology

## How Live TV Captioning Works

Live television broadcasts (news, sports, award shows) use a combination of **human stenographers** and **AI-assisted technology** for real-time captioning.

### Human Stenographers (CART Captioners)

[Real-time captioners](https://www.ncicap.org/captioning) use specialized **stenotype machines** (chorded keyboards with 22 keys) to transcribe speech at 200-300+ words per minute with 98%+ accuracy.

**How it works:**
1. Stenographer receives audio feed from broadcast
2. Types using phonetic shorthand on stenotype machine
3. [Computer-Aided Transcription (CAT) software](https://www.stenograph.com/catalyst-captioning-bcs) converts shorthand to text instantly
4. Text is sent to a **caption encoder** at the TV station
5. Encoder embeds captions into video signal on [Line 21](https://www.3playmedia.com/blog/demystifying-caption-encoder-workflows/) (CEA-608/708 standard)

**Why humans are still required:**
- [FCC regulations](https://verbit.ai/media/broadcast-captioning-101/) mandate high accuracy for broadcast TV
- AI alone cannot meet regulatory standards for live content
- Human captioners adapt to accents, technical jargon, crosstalk

### Caption Encoders (EEG Technology)

[EEG Enterprises](https://www.eegent.com/) (now part of AI-Media) has been the industry standard for broadcast caption encoding since 1981.

**Physical Encoders (iCap Encode Pro/HD492):**
- 1RU rack-mounted hardware at TV stations
- Receives caption text from remote stenographers via IP
- Embeds captions into SDI video signal
- Supports CEA-708 (USA) and OP-47 (Europe/Australia) standards

**Virtual Encoders (iCap Falcon):**
- Cloud-hosted for streaming platforms (YouTube, Facebook, Vimeo)
- No physical hardware required
- Used for online events and OTT content

### Hybrid AI + Human Approach

Modern providers like [Verbit](https://verbit.ai/solutions-real-time/) and [AI-Media](https://www.ai-media.tv/) combine:
- **AI/ASR** for initial transcription draft
- **Human editors** for real-time correction and quality assurance
- **Specialized dictionaries** for technical terms, names, jargon

This hybrid approach is used for major events like the Olympics, Super Bowl, and World Cup.

---

# Appendix B: Research - Self-Hosted / Open Source Options

## Option C: Self-Hosted Whisper Streaming

### Faster-Whisper

[Faster-Whisper](https://github.com/SYSTRAN/faster-whisper) is a reimplementation of OpenAI's Whisper using CTranslate2, providing:
- **4x faster** than original Whisper with same accuracy
- **8-bit quantization** support for CPU and GPU
- Lower memory usage

**Limitations:** Still batch-based, requires wrapper for streaming.

### Whisper-Streaming (UFAL)

[Whisper-Streaming](https://github.com/ufal/whisper_streaming) implements real-time transcription using:
- **Local agreement policy** with self-adaptive latency
- **~3.3 seconds latency** on average (NVIDIA A40 GPU)
- Uses faster-whisper as backend

```python
from whisper_online import *

asr = FasterWhisperASR("en", "large-v2")
online = OnlineASRProcessor(asr)

while audio_has_not_ended:
    chunk = receive_audio_chunk()
    online.insert_audio_chunk(chunk)
    partial_result = online.process_iter()
    print(partial_result)  # Interim result
```

**Note:** Being superseded by SimulStreaming in 2025.

### WhisperLive

[WhisperLive](https://pypi.org/project/whisper-live/) provides:
- WebSocket server for real-time transcription
- TensorRT acceleration support
- Client connections share single model instance

### Hardware Requirements

| Model | VRAM Required | Speed |
|-------|---------------|-------|
| Whisper tiny | ~1GB | Fastest |
| Whisper base | ~1GB | Fast |
| Whisper small | ~2GB | Good |
| Whisper medium | ~5GB | Slower |
| Whisper large-v3 | ~8GB+ | Slowest, most accurate |

---

## Option D: Vosk (Lightweight, Offline)

[Vosk](https://www.videosdk.live/developer-hub/stt/vosk-speech-recognition) is optimized for:
- **Offline operation** (no internet required)
- **Low-resource devices** (Raspberry Pi, embedded systems)
- **Real-time streaming** via WebSocket/gRPC API

**Key Features:**
- 20+ languages supported
- Models as small as **50MB**
- Native streaming API (unlike Whisper)
- Can run as server with multi-client support

**Best for:** Privacy-sensitive applications, edge devices, low-latency requirements where accuracy can be traded for speed.

---

## Option E: NVIDIA NeMo Canary

[NVIDIA NeMo](https://docs.nvidia.com/nemo-framework/user-guide/24.09/nemotoolkit/asr/intro.html) provides state-of-the-art ASR models:

### Canary-1B

- **Outperforms Whisper-large-v3** (6.67% average WER)
- 25 languages supported
- [Chunked/streaming inference](https://docs.nvidia.com/nemo-framework/user-guide/latest/nemotoolkit/asr/streaming_decoding/canary_chunked_and_streaming_decoding.html) supported
- **CC-BY-4.0 license** (commercial use allowed)

### Nemotron-Speech-Streaming-En-0.6B

[Purpose-built for real-time streaming](https://huggingface.co/nvidia/nemotron-speech-streaming-en-0.6b):
- Configurable chunk sizes: 80ms, 160ms, 560ms, 1120ms
- **Cache-aware architecture** - processes only new audio chunks
- Native punctuation and capitalization
- Designed for voice assistants, live captioning, conversational AI

### Canary-Qwen-2.5B (Latest)

[State-of-the-art English ASR](https://huggingface.co/nvidia/canary-qwen-2.5b):
- 2.5 billion parameters
- 418 RTFx (real-time factor)
- Dual mode: ASR-only or full LLM capabilities
- Can summarize/answer questions about transcripts

**Deployment:** Available as NIM endpoint via NVIDIA Riva, or self-hosted with NeMo Framework.

---

## Self-Hosted Options Comparison

| Solution | Streaming | Latency | Accuracy | Hardware | Complexity |
|----------|-----------|---------|----------|----------|------------|
| **Whisper-Streaming** | Wrapper | ~3.3s | Excellent | GPU (8GB+) | Medium |
| **WhisperLive** | Native | ~1-2s | Excellent | GPU (8GB+) | Low |
| **Vosk** | Native | ~200ms | Good | CPU only | Low |
| **NeMo Canary** | Native | ~500ms | State-of-art | GPU (8GB+) | High |
| **Nemotron-Streaming** | Native | ~80-160ms | Excellent | GPU | High |

### Recommendation for Self-Hosted

**For simplicity:** Vosk (low resource, native streaming, good accuracy)

**For quality:** NeMo Nemotron-Speech-Streaming (purpose-built for real-time, best latency)

**For Whisper compatibility:** WhisperLive with faster-whisper backend

---

## Additional References

- [NCRA - What is Captioning](https://www.ncra.org/home/the-profession/Captioning)
- [Stenograph CATalyst BCS](https://www.stenograph.com/catalyst-captioning-bcs)
- [3Play Media - Caption Encoder Workflows](https://www.3playmedia.com/blog/demystifying-caption-encoder-workflows/)
- [Top Open Source STT Options 2025](https://www.assemblyai.com/blog/top-open-source-stt-options-for-voice-applications)
- [Whisper Streaming Paper](https://arxiv.org/html/2307.14743)
- [NVIDIA NeMo Canary Blog](https://developer.nvidia.com/blog/new-standard-for-speech-recognition-and-translation-from-the-nvidia-nemo-canary-model/)
- [Vosk Speech Recognition Guide](https://www.videosdk.live/developer-hub/stt/vosk-speech-recognition)
