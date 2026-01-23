# ADR-002: Use NAudio for Windows Audio Capture

## Status

Accepted

## Context

The application needs to capture audio from two sources:
1. **Microphone** - User's voice input
2. **System loopback** - Audio playing through speakers (e.g., interviewer's voice in a video call)

Options considered:

1. **NAudio** - Mature .NET library for Windows audio APIs (WaveIn, WASAPI)
2. **CSCore** - Alternative .NET audio library
3. **Platform Invoke (P/Invoke)** - Direct Windows API calls
4. **Cross-platform libraries** (PortAudio bindings) - Platform-agnostic approach

Requirements:
- Capture from microphone with minimal latency
- Capture system loopback (WASAPI exclusive)
- Resample to 16kHz mono PCM (OpenAI Realtime API requirement)
- Event-driven architecture for streaming

## Decision

Use NAudio for Windows audio capture with the following components:

- `WaveInEvent` for microphone capture
- `WasapiLoopbackCapture` for system audio loopback
- `WaveFormat` resampling to 16kHz mono PCM16

The implementation:
- Wraps NAudio in `IAudioCaptureService` abstraction
- Raises `OnAudioChunk` events with raw PCM bytes
- Buffers to minimum 100ms chunks before emitting
- Applies silence padding if insufficient data

## Consequences

### Positive

- **Mature and stable**: NAudio has been maintained since 2007
- **WASAPI support**: Full access to Windows audio stack including loopback
- **Well-documented**: Extensive documentation and community support
- **NuGet availability**: Easy to integrate via package manager
- **Resampling built-in**: Can convert between formats without external tools

### Negative

- **Windows-only**: Limits the application to Windows platform
- **Large dependency**: NAudio includes many features we don't use
- **COM interop**: WASAPI uses COM which can have threading issues
- **No cross-platform path**: Would need different implementation for Mac/Linux

### Mitigation

The `IAudioCaptureService` abstraction allows future implementations for other platforms without changing the core library or consumers.

```
interview-assist-audio-windows/  <- Current NAudio implementation
interview-assist-audio-macos/    <- Future: AVFoundation implementation
interview-assist-audio-linux/    <- Future: PulseAudio implementation
```
