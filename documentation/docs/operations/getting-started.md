# Getting Started

This guide walks you through setting up and running Interview Assist for the first time.

## Prerequisites

- .NET 8.0 SDK or later
- Windows 10/11 (required for NAudio audio capture)
- A Deepgram API key (for transcription)
- An OpenAI API key (for LLM-based intent detection)

## 1. Clone and Build

```bash
git clone <repository-url>
cd interview-assist-2
dotnet build interview-assist-2.sln
```

Verify the build succeeds with no errors before continuing.

## 2. Configure API Keys

API keys can be set via environment variables or user secrets.

### Option A: Environment Variables

```bash
set DEEPGRAM_API_KEY=your-deepgram-key
set OPENAI_API_KEY=your-openai-key
```

### Option B: User Secrets (Recommended for Development)

```bash
cd Interview-assist-transcription-detection-console
dotnet user-secrets set "Deepgram:ApiKey" "your-deepgram-key"
dotnet user-secrets set "OpenAI:ApiKey" "your-openai-key"
```

User secrets are stored outside the repository and won't be accidentally committed.

## 3. First Run (Live Transcription)

```bash
dotnet run --project Interview-assist-transcription-detection-console
```

This launches the Terminal.Gui interface with:
- Live audio capture from your default loopback device
- Real-time Deepgram transcription
- LLM-based intent detection (if OpenAI key is configured)
- Automatic session recording to `recordings/`

### Switching Audio Source

By default, the app captures system audio (loopback). To use the microphone instead, edit `appsettings.json`:

```json
{
  "Transcription": {
    "AudioSource": "Microphone"
  }
}
```

## 4. Playback Mode (No Audio/API Required for Transcription)

If you have a recorded session (`.jsonl` file), you can replay it without any audio device or Deepgram key:

```bash
dotnet run --project Interview-assist-transcription-detection-console -- --playback recordings/session-2026-02-10-171013.recording.jsonl
```

This replays the session events through the Terminal.Gui interface with real-time pacing.

## 5. Headless Mode

For automated processing without the UI:

```bash
dotnet run --project Interview-assist-transcription-detection-console -- --playback recordings/session.jsonl --headless
```

Headless mode outputs a console summary and generates a session report in `reports/`.

## 6. Run Tests

```bash
# Unit tests (no API keys required)
dotnet test Interview-assist-library-unit-tests

# Integration tests (requires API keys)
dotnet test Interview-assist-library-integration-tests

# All tests
dotnet test interview-assist-2.sln
```

## Next Steps

- [Configuration Reference](appsettings.md) - Full list of all settings
- [Architecture Overview](../architecture/SAD.md) - System design and components
- [Evaluation & Testing](../instructions/EvaluationInstructions.md) - Running evaluations and benchmarks
