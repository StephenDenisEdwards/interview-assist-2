# Interview Assist

Real-time interview assistance application that captures audio, transcribes speech via Deepgram, and detects questions using LLM-based intent classification.

## Prerequisites

- .NET 8.0 SDK or later
- Windows 10/11 (required for audio capture)
- Deepgram API key (transcription)
- OpenAI API key (intent detection, optional)

## Quick Start

```bash
# Build
dotnet build interview-assist-2.sln

# Configure API keys
set DEEPGRAM_API_KEY=your-deepgram-key
set OPENAI_API_KEY=your-openai-key

# Run live transcription with Terminal.Gui UI
dotnet run --project Interview-assist-transcription-detection-console

# Replay a recorded session (no audio/API required)
dotnet run --project Interview-assist-transcription-detection-console -- --playback recordings/session.jsonl

# Run tests
dotnet test interview-assist-2.sln
```

For detailed setup instructions, see the [Getting Started Guide](documentation/docs/operations/getting-started.md).

## Project Structure

| Project | Description |
|---------|-------------|
| `Interview-assist-library` | Core abstractions, intent detection pipeline, recording/playback, evaluation framework |
| `interview-assist-audio-windows` | Windows-specific audio capture using NAudio |
| `Interview-assist-transcription-detection-console` | Main console app with Terminal.Gui UI, playback, headless mode, and reporting |
| `Interview-assist-annotation-concept-e-console` | Ground truth annotation tool with interactive text selection |
| `Interview-assist-library-unit-tests` | xUnit unit tests |
| `Interview-assist-library-integration-tests` | Integration tests (requires API access) |

## Documentation

See the [Documentation Index](documentation/docs/index.md) for the full documentation hub, including:

- [Architecture Overview (SAD)](documentation/docs/architecture/SAD.md)
- [Configuration Reference](documentation/docs/operations/appsettings.md)
- [API Reference](documentation/docs/design/api-reference.md)
- [Evaluation & Testing Guide](documentation/docs/instructions/EvaluationInstructions.md)
- [Troubleshooting](documentation/docs/operations/troubleshooting.md)

## License

Private repository - all rights reserved.
