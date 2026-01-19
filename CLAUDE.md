# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build entire solution
dotnet build interview-assist.sln

# Build specific project
dotnet build interview-assist-maui-desktop/interview-assist-maui-desktop.csproj

# Run MAUI desktop app (Windows)
dotnet run --project interview-assist-maui-desktop/interview-assist-maui-desktop.csproj -f net10.0-windows10.0.19041.0

# Run console app
dotnet run --project interview-assist-console-windows/interview-assist-console-windows.csproj
```

## Test Commands

```bash
# Run all tests
dotnet test interview-assist.sln

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
- **interview-assist-maui-desktop**: MAUI desktop UI application (net10.0-windows)
- **interview-assist-console-windows**: Console-based CLI application
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
