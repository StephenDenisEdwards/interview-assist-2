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
- **Interview-assist-pipeline-console**: Console app for pipeline mode
- **Interview-assist-library-unit-tests**: xUnit tests for the core library
- **Interview-assist-library-integration-tests**: Integration tests requiring API access

### Key Interfaces

**IRealtimeApi** (`Interview-assist-library/IRealtimeApi.cs`): Main abstraction for OpenAI Realtime API interaction. Exposes lifecycle events (OnConnected, OnReady, OnDisconnected), diagnostic events, and content events (OnUserTranscript, OnAssistantTextDelta, OnFunctionCallResponse).

**IRealtimeSink** (`Interview-assist-library/Realtime/IRealtimeSink.cs`): Observer pattern for consuming API events. Use `WireToApi()` extension to subscribe/unsubscribe. Implementations: `MauiRealtimeSink`, `ConsoleRealtimeSink`.

**IAudioCaptureService** (`Interview-assist-library/Audio/IAudioCaptureService.cs`): Platform-specific audio input abstraction. Two sources: Microphone (WaveInEvent) and Loopback (WasapiLoopbackCapture).

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

- `appsettings.json` in MAUI project for defaults
- Environment variable `OPENAI_API_KEY` for API authentication
- User secrets supported (see csproj UserSecretsId)
- `RealtimeApiOptions` record type configures: model, voice, VAD settings, reconnection behavior

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

## Documentation Maintenance

Architecture documentation lives in `documentation/docs/architecture/` and is visible in the VS solution under the `documentation` folder.

### When to Create an ADR

Create a new ADR (`ADR-XXX-short-title.md`) when:
- Adding a new external dependency or library
- Changing communication protocols or API integrations
- Modifying the audio processing pipeline
- Adding new platform-specific implementations
- Making significant changes to the concurrency model
- Choosing between multiple viable technical approaches

ADRs use the Nygard template (Status, Context, Decision, Consequences). See `documentation/docs/architecture/decisions/README.md` for the template.

### When to Update the SAD

Update `documentation/docs/architecture/SAD.md` when:
- Adding new projects to the solution
- Creating new key interfaces or abstractions
- Changing the building block structure
- Modifying runtime behavior (sequence diagrams)
- Adding new crosscutting concerns

### Adding Documentation to the Solution

When adding new documentation files, update `interview-assist-2.sln` to include them in the appropriate solution folder so they appear in Visual Studio's Solution Explorer.

### Improvement Plans

When planning multi-task improvements or refactoring work:

1. Create a numbered plan file: `documentation/docs/todo/IMPROVEMENT-PLAN-XXXX.md`
2. Use the next available number (e.g., 0001, 0002, 0003)
3. Add the file to the solution under the `todo` folder in `interview-assist-2.sln`
4. Structure the plan with:
   - Created date and status
   - Completed tasks (checked off)
   - Remaining tasks with priority, effort, and affected files
   - Implementation order/phases

When completing an improvement plan:

1. Add an **Implementation Summary** section at the end with:
   - Tables showing tasks completed per phase and files changed
   - Build and test results
   - List of new files created
2. Update the status to reflect completion
3. Move the file from `documentation/docs/todo/` to `documentation/docs/`
4. Update `interview-assist-2.sln`:
   - Remove the file from the `todo` solution folder
   - Add the file to the `todo-done` solution folder
