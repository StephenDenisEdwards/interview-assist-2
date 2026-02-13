# Software Architecture Document (SAD)

**Project:** Interview Assist
**Version:** 1.0
**Last Updated:** 2026-02-11
**Template:** arc42 lite

---

## 1. Introduction and Goals

### 1.1 Purpose

Interview Assist is a real-time interview assistance application that captures audio from microphone and system loopback, transcribes speech using Deepgram's streaming API (or OpenAI's Realtime API in legacy mode), detects intents/questions via LLM classification, and provides AI-powered responses to help users during technical interviews.

### 1.2 Quality Goals

| Priority | Quality Goal | Description |
|----------|--------------|-------------|
| 1 | Low Latency | Real-time transcription with minimal delay (<500ms) |
| 2 | Reliability | Graceful handling of connection drops with auto-reconnect |
| 3 | Extensibility | Platform-agnostic core with pluggable audio capture |
| 4 | Testability | Core logic separated from platform concerns |

### 1.3 Stakeholders

| Role | Expectations |
|------|--------------|
| End User | Accurate transcription, helpful AI suggestions |
| Developer | Clean interfaces, easy to extend and test |

---

## 2. Constraints

### 2.1 Technical Constraints

| Constraint | Rationale |
|------------|-----------|
| .NET 8.0+ runtime | Modern C# features, cross-platform potential |
| Windows-only audio capture | NAudio library dependency for low-level audio APIs |
| Deepgram API dependency | Streaming WebSocket API provides primary transcription |
| OpenAI API dependency | LLM intent classification and legacy Realtime API mode |
| WebSocket protocol | Required by Deepgram streaming and OpenAI Realtime API |

### 2.2 Organizational Constraints

| Constraint | Rationale |
|------------|-----------|
| Single developer | Prefer simplicity over abstraction |
| No cloud deployment | Desktop-only application |

---

## 3. Context and Scope

### 3.1 Business Context

```mermaid
C4Context
    title System Context Diagram

    Person(user, "User", "Interview candidate using the app")
    System(app, "Interview Assist", "Real-time transcription and AI assistance")
    System_Ext(openai, "OpenAI Realtime API", "Speech-to-text and AI responses")
    System_Ext(audio, "Audio Sources", "Microphone / System Loopback")

    Rel(user, app, "Views transcription, receives suggestions")
    Rel(app, openai, "WebSocket: Audio chunks, receives transcripts")
    Rel(audio, app, "PCM audio stream")
```

### 3.2 Technical Context

| Interface | Protocol | Data Format |
|-----------|----------|-------------|
| Deepgram Streaming API | WebSocket (wss://) | JSON messages, raw PCM audio |
| OpenAI Realtime API | WebSocket (wss://) | JSON messages, Base64 audio |
| OpenAI Chat API | HTTPS REST | JSON (intent classification) |
| Audio Capture | NAudio events | 16kHz mono PCM16 |
| UI Events | .NET events | Strongly-typed delegates |
| Terminal.Gui | TUI framework | In-process rendering |

---

## 4. Solution Strategy

### 4.1 Key Decisions

| Decision | Approach | See ADR |
|----------|----------|---------|
| API Communication | WebSocket with OpenAI Realtime API | [ADR-001](decisions/ADR-001-realtime-api-websocket.md) |
| Audio Capture | NAudio library for Windows | [ADR-002](decisions/ADR-002-audio-capture-naudio.md) |
| Question Detection | LLM-based detection using GPT models | [ADR-003](decisions/ADR-003-question-detection-llm.md) |
| Streaming Transcription | Multi-mode stability tracking | [ADR-005](decisions/ADR-005-streaming-transcription-modes.md) |
| Intent Detection | Multi-strategy (Heuristic + LLM + Deepgram) | [ADR-007](decisions/ADR-007-multi-strategy-intent-detection.md) |
| Utterance Segmentation | Streaming ASR to coherent utterances | [ADR-008](decisions/ADR-008-utterance-segmentation.md) |

### 4.2 Technology Stack

| Layer | Technology |
|-------|------------|
| Core Library | .NET 8.0, System.Net.WebSockets |
| Audio Capture | NAudio (Windows-specific) |
| Transcription | Deepgram WebSocket streaming (primary), Whisper STT (legacy) |
| Intent Detection | OpenAI Chat API (GPT-4o-mini / GPT-4o) |
| Console UI | Terminal.Gui (TUI framework) |
| Testing | xUnit, Moq |

---

## 5. Building Block View

### 5.1 Level 1: Solution Structure

```mermaid
graph TB
    subgraph UI["UI Layer"]
        DetectionConsole[Interview-assist-transcription-detection-console]
        TransConsole[Interview-assist-transcription-console]
        PipelineConsole[Interview-assist-pipeline-console]
        AnnotationConsole[Interview-assist-annotation-console]
    end

    subgraph Core["Core Library"]
        Lib[Interview-assist-library]
        Pipeline[Interview-assist-pipeline]
    end

    subgraph Platform["Platform-Specific"]
        Audio[interview-assist-audio-windows]
    end

    subgraph Tests["Test Projects"]
        Unit[Interview-assist-library-unit-tests]
        Integration[Interview-assist-library-integration-tests]
    end

    DetectionConsole --> Lib
    DetectionConsole --> Audio
    TransConsole --> Lib
    TransConsole --> Audio
    PipelineConsole --> Pipeline
    PipelineConsole --> Audio
    Pipeline --> Lib
    Lib -.-> Audio
    Unit --> Lib
    Integration --> Lib
```

### 5.2 Level 2: Core Library Components

```mermaid
graph TB
    subgraph InterviewAssistLibrary["Interview-assist-library"]
        IRealtimeApi["IRealtimeApi<br/>(Interface)"]
        OpenAiRealtimeApi["OpenAiRealtimeApi<br/>(Implementation)"]
        IRealtimeSink["IRealtimeSink<br/>(Observer Interface)"]
        IAudioCaptureService["IAudioCaptureService<br/>(Interface)"]
        RealtimeApiOptions["RealtimeApiOptions<br/>(Configuration)"]
    end

    OpenAiRealtimeApi -->|implements| IRealtimeApi
    OpenAiRealtimeApi -->|uses| IAudioCaptureService
    IRealtimeSink -->|subscribes to| IRealtimeApi
```

### 5.3 Key Interfaces

#### IRealtimeApi

Main abstraction for OpenAI Realtime API interaction.

```csharp
public interface IRealtimeApi : IAsyncDisposable
{
    // Lifecycle events
    event Action? OnConnected;
    event Action? OnReady;
    event Action? OnDisconnected;
    event Action? OnReconnecting;

    // Content events
    event Action<string>? OnUserTranscript;
    event Action<string>? OnAssistantTextDelta;
    event Action<string, string, string>? OnFunctionCallResponse;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    Task SendTextAsync(string text, bool requestResponse = true, bool interrupt = false);
    bool IsConnected { get; }
}
```

#### IRealtimeSink

Observer pattern for consuming API events with automatic wiring.

```csharp
public interface IRealtimeSink : IDisposable
{
    void OnConnected();
    void OnReady();
    void OnDisconnected();
    void OnUserTranscript(string text);
    void OnAssistantTextDelta(string delta);
    // ... additional event handlers
}

// Usage: var wiring = sink.WireToApi(api);
```

#### IAudioCaptureService

Platform-specific audio input abstraction.

```csharp
public interface IAudioCaptureService : IDisposable
{
    event Action<byte[]>? OnAudioChunk;
    void SetSource(AudioInputSource source);  // Microphone or Loopback
    void Start();
    void Stop();
}
```

---

## 6. Runtime View

### 6.1 Audio Processing Pipeline

```mermaid
sequenceDiagram
    participant Audio as Audio Source
    participant NAudio as NAudio Capture
    participant Buffer as Audio Buffer
    participant Channel as Bounded Channel
    participant WS as WebSocket
    participant OpenAI as OpenAI API

    Audio->>NAudio: Raw audio samples
    NAudio->>Buffer: Resample to 16kHz mono
    Buffer->>Buffer: Accumulate min 100ms
    Buffer->>Channel: OnAudioChunk (8 capacity, DropOldest)
    Channel->>WS: Base64 encode + send
    WS->>OpenAI: input_audio_buffer.append
    OpenAI-->>WS: conversation.item.input_audio_transcription.completed
    WS-->>NAudio: OnUserTranscript event
```

### 6.2 Connection Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Disconnected
    Disconnected --> Connecting: StartAsync()
    Connecting --> Connected: WebSocket opened
    Connected --> Ready: session.created received
    Ready --> Ready: Processing audio/responses
    Ready --> Reconnecting: Connection lost
    Reconnecting --> Connecting: Retry attempt
    Reconnecting --> Disconnected: Max retries exceeded
    Ready --> Disconnected: StopAsync()
    Connected --> Disconnected: Error
```

---

## 7. Crosscutting Concepts

### 7.1 Concurrency Model

| Component | Strategy |
|-----------|----------|
| WebSocket Receive | Dedicated background Task |
| Audio Send | Dedicated background Task with bounded Channel |
| Event Dispatch | Channel-based async queue (protects from subscriber exceptions) |
| UI Updates | `MainThread.BeginInvokeOnMainThread()` |

### 7.2 Error Handling

- WebSocket errors trigger reconnection with exponential backoff
- Audio capture errors are logged but don't crash the app
- Subscriber exceptions are caught by the event dispatcher

### 7.3 Configuration

| Source | Purpose |
|--------|---------|
| `appsettings.json` | Default settings |
| Environment variables | `OPENAI_API_KEY` |
| User secrets | Development credentials |
| `RealtimeApiOptions` | Runtime configuration for Realtime API mode |
| `PipelineApiOptions` | Runtime configuration for Pipeline mode |
| `QuestionDetectionOptions` | Question detection settings (when enabled) |

### 7.4 Utterance-Intent Pipeline (Deepgram Mode)

When `Mode: "Deepgram"` is selected, the application uses a streaming pipeline architecture:

```
Deepgram WS → AsrEventSource → Stabilizer → UtteranceBuilder → IntentDetector → ActionRouter
```

See [DESIGN-utterance-intent-pipeline.md](../design/DESIGN-utterance-intent-pipeline.md) for detailed design.

### 7.5 Session Recording, Playback & Reporting

The application supports recording, playback, and analysis of sessions:

| Component | Purpose |
|-----------|---------|
| `SessionRecorder` | Records all pipeline events (ASR, utterances, intents, actions) to JSONL |
| `AudioFileRecorder` | Optionally saves raw audio to WAV alongside JSONL |
| `SessionPlayer` | Replays JSONL recordings with original timing for UI review |
| `WavFileAudioSource` | Re-transcribes WAV files via Deepgram for testing |
| `SessionReportGenerator` | Generates markdown reports from JSONL data |

**Playback modes:**
- `--playback <file.jsonl>` — Replays recorded events in Terminal.Gui UI
- `--playback <file.wav>` — Re-transcribes audio via Deepgram with UI
- `--playback <file> --headless` — Non-interactive, outputs console summary + report + auto-evaluation
- `--analyze <file.jsonl>` — Generates markdown report without playback + auto-evaluation

Reports are saved to the `reports/` folder as `{session-name}.report.md` and include event distribution, utterance analysis, intent detection results, latency statistics, and log insights.

**Auto-evaluation:** When a report is generated (via `--headless` or `--analyze`), evaluation automatically runs against both LLM-extracted and human ground truth. If no human ground truth file exists for the session, one is auto-generated from LLM extraction as a seed for later human editing. Evaluation outputs are saved to the `evaluations/` folder. Requires an OpenAI API key; skipped silently if unavailable.

### 7.6 Question Detection (Legacy Mode, Optional)

Question detection is an **optional feature** controlled via dependency injection. If `IQuestionDetectionService` is not registered, detection is disabled and only transcription occurs.

#### Configuration (appsettings.json)

```json
{
  "QuestionDetection": {
    "Enabled": true,
    "Model": "gpt-4o-mini",
    "ConfidenceThreshold": 0.7
  }
}
```

#### DI Registration

```csharp
// Enable question detection (call BEFORE AddInterviewAssistPipeline)
if (config.GetValue<bool>("QuestionDetection:Enabled", true))
{
    services.AddQuestionDetection(opts => opts
        .WithApiKey(apiKey)
        .WithModel("gpt-4o-mini")
        .WithConfidenceThreshold(0.7));
}

// Add pipeline (picks up detection service if registered)
services.AddInterviewAssistPipeline(opts => opts.WithApiKey(apiKey));
```

#### Console Apps (without full DI)

```csharp
// Create detector only if enabled
QuestionDetector? detector = detectionEnabled
    ? new QuestionDetector(apiKey, model, confidence)
    : null;

// Pass to pipeline (null = detection disabled)
var pipeline = new InterviewPipeline(audio, apiKey, questionDetector: detector);
```

#### Key Interfaces

| Interface | Purpose |
|-----------|---------|
| `IQuestionDetectionService` | Detects questions in transcript text |
| `QuestionDetectionOptions` | Configuration for detection (Model, ConfidenceThreshold) |
| `QuestionDetectionOptionsBuilder` | Fluent builder for configuration |

### 7.5 Streaming Transcription (Optional)

Streaming transcription provides stability-aware text tracking with three modes that trade off latency vs accuracy. See [ADR-005](decisions/ADR-005-streaming-transcription-modes.md) for design rationale.

#### Modes

| Mode | Latency | Accuracy | Use Case |
|------|---------|----------|----------|
| **Basic** | Low | Standard | Simple applications, high-quality audio |
| **Revision** | Medium | High | Accuracy-critical, acceptable delay |
| **Streaming** | Lowest | Good | Real-time display, acceptable errors |

#### Switching Modes

**Command Line:**

```bash
# Basic mode (all text immediately stable)
Interview-assist-transcription-console --streaming --mode basic

# Revision mode (overlapping batches, local agreement)
Interview-assist-transcription-console --mode revision

# Streaming mode (rapid hypothesis, stability tracking)
Interview-assist-transcription-console --mode streaming

# With vocabulary prompting (improves technical term accuracy)
Interview-assist-transcription-console --mode revision --vocab "C#, async, await"

# With microphone input
Interview-assist-transcription-console --mode revision --mic
```

**appsettings.json:**

```json
{
  "Transcription": {
    "UseStreaming": true,
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
      "StabilityTimeoutMs": 2000,
      "FlickerCooldownMs": 100
    }
  }
}
```

**DI Registration:**

```csharp
services.AddStreamingTranscription(opts => opts
    .WithApiKey(apiKey)
    .WithMode(TranscriptionMode.Revision)
    .WithContextPrompting(true, maxChars: 200, vocabulary: "C#, async")
    .WithRevisionOptions(overlapMs: 1500, agreementCount: 2));
```

#### Key Interfaces

| Interface | Purpose |
|-----------|---------|
| `IStreamingTranscriptionService` | Stability-aware transcription with events |
| `StreamingTranscriptionOptions` | Configuration for all modes |
| `StreamingTranscriptionOptionsBuilder` | Fluent builder for configuration |
| `StableTextEventArgs` | Confirmed text that won't change |
| `ProvisionalTextEventArgs` | Text that may be revised |
| `HypothesisEventArgs` | Full hypothesis with stability ratio |

#### Event Flow

```
Audio → Whisper API → TranscriptionTextComparer
                            ↓
              ┌─────────────┴─────────────┐
              ↓                           ↓
        OnStableText              OnProvisionalText
        (white text)               (gray text)
              ↓                           ↓
              └─────────────┬─────────────┘
                            ↓
                    OnFullHypothesis
                  (stable + provisional)
```

---

## 8. Architecture Decisions

See the [decisions/](decisions/) directory for Architecture Decision Records (ADRs):

| ADR | Title | Status |
|-----|-------|--------|
| [ADR-001](decisions/ADR-001-realtime-api-websocket.md) | Realtime API via WebSocket | Accepted |
| [ADR-002](decisions/ADR-002-audio-capture-naudio.md) | Audio Capture with NAudio | Accepted |
| [ADR-003](decisions/ADR-003-question-detection-llm.md) | LLM-based Question Detection | Superseded by ADR-007 |
| [ADR-004](decisions/ADR-004-pipeline-vs-realtime.md) | Pipeline vs Realtime Implementation | Accepted |
| [ADR-005](decisions/ADR-005-streaming-transcription-modes.md) | Streaming Transcription Modes | Superseded |
| [ADR-006](decisions/ADR-006-deepgram-intent-recognition.md) | Deepgram Intent Recognition | Evaluated (unsuitable) |
| [ADR-007](decisions/ADR-007-multi-strategy-intent-detection.md) | Multi-Strategy Intent Detection | Accepted |
| [ADR-008](decisions/ADR-008-utterance-segmentation.md) | Utterance Segmentation from Streaming ASR | Accepted |

---

## 9. Risks and Technical Debt

| Risk/Debt | Impact | Mitigation |
|-----------|--------|------------|
| Windows-only audio | Limits platform reach | IAudioCaptureService abstraction allows future implementations |
| Deepgram API dependency | Transcription requires Deepgram | JSONL playback mode works offline; WAV files can be re-transcribed |
| OpenAI API dependency | LLM intent detection requires OpenAI | Heuristic detection mode works without API; configurable model |
| No offline mode | Live transcription requires internet | Playback and analysis modes work offline with recorded data |

---

## 10. Glossary

| Term | Definition |
|------|------------|
| Loopback | System audio capture (what you hear through speakers) |
| VAD | Voice Activity Detection - detects when user is speaking |
| Realtime API | OpenAI's WebSocket-based API for real-time audio/text |
| Sink | Observer that receives and handles events from a source |
| PCM | Pulse Code Modulation - raw audio format |
