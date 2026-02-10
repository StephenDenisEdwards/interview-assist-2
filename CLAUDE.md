# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build entire solution
dotnet build interview-assist-2.sln

# Build specific project
dotnet build Interview-assist-library/Interview-assist-library.csproj

# Run transcription console app
dotnet run --project Interview-assist-transcription-console/Interview-assist-transcription-console.csproj

# Run pipeline console app
dotnet run --project Interview-assist-pipeline-console/Interview-assist-pipeline-console.csproj

# Run transcription-detection console app
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj

# Run transcription-detection console app in playback mode (JSONL replay, no audio/API required)
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.jsonl

# Run transcription-detection console app in WAV playback mode (re-transcribes via Deepgram)
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.wav

# Run headless playback (no Terminal.Gui UI, outputs JSONL + console summary)
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.jsonl --headless

# Run headless WAV playback (re-transcribes via Deepgram, no UI)
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.wav --headless
```

## Test Commands

```bash
# Run all tests
dotnet test interview-assist-2.sln

# Run unit tests only
dotnet test Interview-assist-library-unit-tests/Interview-assist-library-unit-tests.csproj

# Run integration tests only
dotnet test Interview-assist-library-integration-tests/Interview-assist-library-integration-tests.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~TestMethodName"
```

## Architecture Overview

This is a real-time interview assistance application that captures audio and integrates with OpenAI's Realtime API for live transcription and AI-powered responses.

### Project Structure

- **Interview-assist-library**: Core abstractions and OpenAI Realtime API implementation (net8.0)
- **interview-assist-audio-windows**: Windows-specific audio capture using NAudio (net8.0)
- **Interview-assist-pipeline**: Pipeline-based STT + semantic question detection (net8.0)
- **Interview-assist-transcription-console**: Console app for transcription testing
- **Interview-assist-transcription-detection-console**: Console app with Terminal.Gui UI, supports playback mode
- **Interview-assist-pipeline-console**: Console app for pipeline mode
- **Interview-assist-library-unit-tests**: xUnit tests for the core library
- **Interview-assist-library-integration-tests**: Integration tests requiring API access

### Key Interfaces

**IRealtimeApi** (`Interview-assist-library/IRealtimeApi.cs`): Main abstraction for OpenAI Realtime API interaction. Exposes lifecycle events (OnConnected, OnReady, OnDisconnected), diagnostic events, and content events (OnUserTranscript, OnAssistantTextDelta, OnFunctionCallResponse).

**IRealtimeSink** (`Interview-assist-library/Realtime/IRealtimeSink.cs`): Observer pattern for consuming API events. Use `WireToApi()` extension to subscribe/unsubscribe. Implementations: `MauiRealtimeSink`, `ConsoleRealtimeSink`.

**IAudioCaptureService** (`Interview-assist-library/Audio/IAudioCaptureService.cs`): Platform-specific audio input abstraction. Implementation: `WindowsAudioCaptureService` (NAudio, supports Microphone and Loopback).

### Audio Processing Pipeline

```
Audio Input (Mic/Loopback)
    → NAudio capture (WaveInEvent or WasapiLoopbackCapture)
    → Resample to 16kHz mono PCM
    → OnAudioChunk event
    → Bounded channel queue (8 capacity, DropOldest)
    → Base64 encode
    → WebSocket send to OpenAI
```

Audio is buffered to minimum 100ms chunks before sending. Silence padding applied if insufficient data.

### Configuration

- `appsettings.json` in console projects for defaults
- Environment variable `OPENAI_API_KEY` for API authentication
- User secrets supported (see csproj UserSecretsId)
- `RealtimeApiOptions` record type configures: model, voice, VAD settings, reconnection behavior
- `PipelineApiOptions` record type configures: transcription, response generation settings
- `QuestionDetectionOptions` record type configures: detection model, confidence threshold

### Question Detection (Optional)

Question detection is controlled via dependency injection. If `IQuestionDetectionService` is not registered, detection is disabled and only transcription occurs.

**appsettings.json:**
```json
{
  "QuestionDetection": {
    "Enabled": true,
    "Model": "gpt-4o-mini",
    "ConfidenceThreshold": 0.7
  }
}
```

**DI Registration:**
```csharp
// Enable detection (call BEFORE AddInterviewAssistPipeline)
if (config.GetValue<bool>("QuestionDetection:Enabled", true))
{
    services.AddQuestionDetection(opts => opts
        .WithApiKey(apiKey)
        .WithModel("gpt-4o-mini"));
}

services.AddInterviewAssistPipeline(opts => opts.WithApiKey(apiKey));
```

**Console apps (without DI):**
```csharp
QuestionDetector? detector = detectionEnabled
    ? new QuestionDetector(apiKey, model, confidence)
    : null;

var pipeline = new InterviewPipeline(audio, apiKey, questionDetector: detector);
```

### Concurrency Model

- Receive loop: Dedicated task for WebSocket reading
- Audio send loop: Dedicated task for buffered audio transmission
- Event dispatcher: Channels-based async queue protects from subscriber exceptions
- UI updates via `MainThread.BeginInvokeOnMainThread()`

## Code Style Guidelines

- Use async/await end-to-end
- No logic in controllers
- Prefer immutability
- Follow existing architectural patterns
- Prefer small, reviewable changes
- Do not introduce new dependencies unless asked

## Documentation

Documentation lives in `documentation/docs/` and is organized by purpose. See [documentation/docs/index.md](documentation/docs/index.md) for navigation.

### Documentation Structure

```
documentation/docs/
├── index.md              # Navigation index
├── architecture/         # SAD and ADRs
│   ├── SAD.md
│   └── decisions/
├── design/               # Design documents
├── domain/               # Domain knowledge (Deepgram, question detection)
├── operations/           # Configuration and operational guides
└── plans/
    ├── in-progress/      # Active improvement plans
    └── completed/        # Finished improvement plans
```

### Quick Reference

| Document Type | Location | When to Create/Update |
|--------------|----------|----------------------|
| Architecture Decision Record | `architecture/decisions/ADR-XXX-*.md` | New dependency, protocol change, significant design choice |
| Software Architecture Document | `architecture/SAD.md` | New projects, interfaces, building blocks |
| Design Document | `design/DESIGN-*.md` | Complex feature designs, pipelines |
| Domain Knowledge | `domain/` | External service integrations, algorithms |
| Operations Guide | `operations/` | Configuration, deployment, rate limiting |
| Improvement Plan | `plans/in-progress/IMPROVEMENT-PLAN-XXXX.md` | Multi-task refactoring or feature work |

### Detailed Guidelines

For comprehensive documentation guidelines including templates and examples, see:
- [.github/instructions/documentation.instructions.md](.github/instructions/documentation.instructions.md)

### Solution Integration

All documentation files must be added to `interview-assist-2.sln` under the appropriate solution folder to appear in Visual Studio's Solution Explorer.
