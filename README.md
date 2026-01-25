# Interview Assist

Real-time interview assistance application that captures audio, transcribes speech, and provides AI-powered responses during technical interviews.

## Prerequisites

- .NET 8.0 SDK or later
- Windows 10/11 (required for audio capture)
- OpenAI API key with access to Realtime API

## Quick Start

1. **Clone the repository**

2. **Set your OpenAI API key**

   ```bash
   # Option 1: Environment variable
   set OPENAI_API_KEY=sk-your-key-here

   # Option 2: User secrets (recommended for development)
   cd Interview-assist-transcription-console
   dotnet user-secrets set "OpenAI:ApiKey" "sk-your-key-here"
   ```

3. **Build the solution**

   ```bash
   dotnet build interview-assist-2.sln
   ```

4. **Run the transcription console**

   ```bash
   dotnet run --project Interview-assist-transcription-console
   ```

## Project Structure

| Project | Description |
|---------|-------------|
| `Interview-assist-library` | Core abstractions, OpenAI Realtime API, and Pipeline implementations |
| `interview-assist-audio-windows` | Windows-specific audio capture using NAudio |
| `Interview-assist-pipeline` | Pipeline-based STT + semantic question detection |
| `Interview-assist-transcription-console` | Console app for real-time transcription |
| `Interview-assist-pipeline-console` | Console app for pipeline mode |
| `Interview-assist-library-unit-tests` | xUnit unit tests |
| `Interview-assist-library-integration-tests` | Integration tests (requires API access) |

## Two Modes of Operation

### Realtime Mode (OpenAiRealtimeApi)

Uses OpenAI's native Realtime API via WebSocket for lowest latency:

- Sub-500ms response times
- Server-side voice activity detection (VAD)
- Turn-based conversation model
- Integrated audio responses

### Pipeline Mode (PipelineRealtimeApi)

Uses separate Whisper STT + GPT-4 Chat API for more control:

- Semantic question detection
- Queue-based processing for overlapping speech
- Configurable detection thresholds
- Text-only responses

See [ADR-004](documentation/docs/architecture/decisions/ADR-004-pipeline-vs-realtime.md) for detailed comparison.

## Configuration

Configuration is loaded from multiple sources (in order of precedence):

1. Environment variables
2. User secrets
3. `appsettings.json`

Key settings:

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "RealtimeModel": "gpt-4o-realtime-preview-2024-12-17",
    "TranscriptionModel": "whisper-1"
  }
}
```

## Testing

```bash
# Run all tests
dotnet test interview-assist-2.sln

# Run unit tests only
dotnet test Interview-assist-library-unit-tests

# Run integration tests (requires API key)
dotnet test Interview-assist-library-integration-tests
```

## Architecture

See the [Software Architecture Document](documentation/docs/architecture/SAD.md) for detailed architecture information.

### Key Interfaces

- **IRealtimeApi** - Main abstraction for real-time API interaction
- **IRealtimeSink** - Observer pattern for consuming API events
- **IAudioCaptureService** - Platform-specific audio capture abstraction

## License

Private repository - all rights reserved.
