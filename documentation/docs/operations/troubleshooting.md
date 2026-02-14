# Troubleshooting

Common issues and their solutions when running Interview Assist.

## Audio Capture

### No audio detected / silence

**Symptoms:** Transcription panel stays empty, no ASR events in debug panel.

**Possible causes:**
- Wrong audio source selected. Check `Transcription:AudioSource` in `appsettings.json` — use `"Loopback"` for system audio or `"Microphone"` for mic input.
- No audio is playing on the system (loopback mode captures system output).
- Audio device not available. NAudio requires a valid Windows audio endpoint.

**Fix:** Verify audio is playing, check the audio source setting, and ensure Windows audio devices are enabled in Sound Settings.

### Audio capture crashes on startup

**Symptoms:** `NAudio.MmException` or `COMException` on launch.

**Possible causes:**
- No audio output device connected (loopback requires an active output device).
- Running in a headless/RDP session without audio drivers.

**Fix:** Ensure a physical or virtual audio output device is available. For remote sessions, install a virtual audio driver.

## Deepgram Connection

### "Deepgram API key not configured"

**Symptoms:** Error message on startup or empty transcription.

**Fix:** Set the `DEEPGRAM_API_KEY` environment variable or configure it in user secrets:
```bash
dotnet user-secrets set "Deepgram:ApiKey" "your-key"
```

### WebSocket connection failures

**Symptoms:** Repeated "Disconnected" / "Reconnecting" messages in the debug panel.

**Possible causes:**
- Invalid or expired API key.
- Network/firewall blocking WebSocket connections to `api.deepgram.com`.
- Deepgram service outage.

**Fix:** Verify your API key at [console.deepgram.com](https://console.deepgram.com). Check network connectivity. The app will automatically retry with exponential backoff.

### Rate limiting (HTTP 429)

**Symptoms:** `RateLimitExceeded` errors in debug panel, circuit breaker activating.

**Fix:** The built-in circuit breaker handles rate limits automatically with backoff. If persistent, check your Deepgram plan limits. See [Rate Limiting](rate-limiting.md) for details.

## OpenAI / Intent Detection

### "OpenAI API key not configured"

**Symptoms:** Intent detection disabled, no question highlights in transcript.

**Fix:** Set the `OPENAI_API_KEY` environment variable or configure it in user secrets:
```bash
dotnet user-secrets set "OpenAI:ApiKey" "your-key"
```

Intent detection is optional. The app works without it — you'll get transcription only.

### LLM detection returning no results

**Symptoms:** Transcription works but no intents detected.

**Possible causes:**
- Intent detection mode set to `Heuristic` (less accurate than `Llm`).
- Confidence threshold too high.
- Detection disabled in config.

**Fix:** Check `appsettings.json`:
```json
{
  "Transcription": {
    "IntentDetection": {
      "Enabled": true,
      "Mode": "Llm",
      "Llm": {
        "Model": "gpt-4o-mini",
        "ConfidenceThreshold": 0.3
      }
    }
  }
}
```

## Terminal.Gui / UI

### UI renders incorrectly or shows garbled characters

**Symptoms:** Broken borders, overlapping text, incorrect colors.

**Possible causes:**
- Terminal emulator doesn't support Unicode box-drawing characters.
- Console font doesn't include required glyphs.
- Terminal window too small.

**Fix:** Use Windows Terminal (recommended) instead of legacy `cmd.exe`. Set a font that supports Unicode (e.g., Cascadia Code, Consolas). Resize the window to at least 120x30.

### Application.Init() throws in tests or CI

**Symptoms:** `InvalidOperationException` when running code that calls `Application.Init()`.

**Cause:** Terminal.Gui requires an interactive console (PTY). It cannot run in non-interactive contexts like CI pipelines, unit tests, or headless containers.

**Fix:** Separate UI code from business logic. Test the parsing/processing code independently. See the existing unit tests for patterns that avoid `Application.Init()`.

## Playback Mode

### "File not found" when using --playback

**Symptoms:** Error referencing the JSONL file path.

**Fix:** Use the full path or a path relative to the working directory. The app runs from the project directory by default:
```bash
dotnet run --project Interview-assist-transcription-detection-console -- --playback recordings/session-2026-02-10-171013.recording.jsonl
```

### Playback runs but no events appear

**Symptoms:** Playback completes instantly with no output.

**Possible causes:**
- JSONL file is empty or contains only non-ASR events.
- File uses an incompatible format (not `RecordedEvent` JSONL).

**Fix:** Inspect the JSONL file to verify it contains `AsrPartial` / `AsrFinal` event types with valid data.

## Evaluation

### Auto-evaluation shows 0% metrics

**Symptoms:** All precision/recall/F1 scores are 0.

**Possible causes:**
- No ground truth file available for the session.
- Ground truth file has no matching questions.

**Fix:** Create a ground truth file using the annotation tools. See [Evaluation Instructions](../instructions/EvaluationInstructions.md).

### "OPENAI_API_KEY required for auto-evaluation"

**Symptoms:** `--analyze` mode skips LLM-based evaluation.

**Fix:** Auto-evaluation uses OpenAI to match detected intents against ground truth. Set the `OPENAI_API_KEY` environment variable.
