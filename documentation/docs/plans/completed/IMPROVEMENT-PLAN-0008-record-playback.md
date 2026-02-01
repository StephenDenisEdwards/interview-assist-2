# Session Recording and Playback for Intent Detection Testing

**Created:** 2026-02-01
**Status:** Complete

## Overview

Add recording and playback functionality to capture pipeline events (ASR, Utterance, Intent) with precise timing, save to JSONL files, and replay them to test intent detection without live audio.

## JSONL Format

```jsonl
{"type":"SessionMetadata","offsetMs":0,"version":"1.0","recordedAtUtc":"2026-01-31T10:30:00Z","config":{...}}
{"type":"AsrEvent","offsetMs":125,"data":{"text":"Is there a","isFinal":false,"speakerId":"0"}}
{"type":"AsrEvent","offsetMs":340,"data":{"text":"Is there a risk","isFinal":true,"speakerId":"0"}}
{"type":"UtteranceEndSignal","offsetMs":1200}
{"type":"UtteranceEvent","offsetMs":1205,"data":{"id":"utt-001","eventType":"Final","stableText":"...","speakerId":"0"}}
{"type":"IntentEvent","offsetMs":1210,"data":{"intent":{"type":"Question","confidence":0.8},"utteranceId":"utt-001"}}
```

## New Files

### Interview-assist-library/Pipeline/Recording/

| File | Purpose |
|------|---------|
| `RecordedEvent.cs` | Polymorphic record types for all event types |
| `SessionRecorder.cs` | Subscribes to pipeline events, writes JSONL with timing |
| `SessionPlayer.cs` | Reads JSONL, feeds ASR events to pipeline at recorded timing |
| `RecordingOptions.cs` | Configuration record for folder/filename settings |

## Modified Files

| File | Changes |
|------|---------|
| `Interview-assist-transcription-detection-console/appsettings.json` | Add `Recording` section with Folder, FileNamePattern, AutoStart |
| `Interview-assist-transcription-detection-console/Program.cs` | Add --playback argument, Ctrl+R/S shortcuts, AutoStart, status indicators |

## appsettings.json Addition

```json
{
  "Recording": {
    "Folder": "recordings",
    "FileNamePattern": "session-{timestamp}.jsonl",
    "AutoStart": true
  }
}
```

## Console App Usage

```bash
# Normal mode (live audio) - recording auto-starts if AutoStart=true in appsettings
dotnet run --project Interview-assist-transcription-detection-console

# Playback mode (from recorded file)
dotnet run --project Interview-assist-transcription-detection-console -- --playback recordings/session-2026-01-31-103000.jsonl

# Show help
dotnet run --project Interview-assist-transcription-detection-console -- --help
```

### Keyboard Shortcuts (Live Mode)

| Key | Action |
|-----|--------|
| Ctrl+S | Stop transcription |
| Ctrl+R | Toggle recording (manual, when AutoStart=false) |
| Ctrl+Q | Quit |

## Key Design Decisions

1. **JSONL format** - One event per line, easy to append/parse/analyze with grep/jq
2. **Relative timestamps** - Milliseconds from session start for portability
3. **Record all events, replay only ASR** - Pipeline regenerates Utterance/Intent/Action events during playback, enabling testing of detection changes
4. **Realtime playback** - Respects timing delays between events to simulate actual session flow

## Implementation Phases

### Phase 1: Core Library Classes
- [x] Create `Interview-assist-library/Pipeline/Recording/` folder
- [x] Implement `RecordedEvent.cs` with polymorphic JSON serialization
- [x] Implement `SessionRecorder.cs` - hooks pipeline events, writes JSONL
- [x] Implement `SessionPlayer.cs` - loads JSONL, replays with timing
- [x] Implement `RecordingOptions.cs` - configuration for folder/filename

### Phase 2: Console App Integration
- [x] Add `Recording:Folder` to appsettings.json
- [x] Parse `--playback <file>` command-line argument
- [x] Add `--help` command-line argument
- [x] Add Ctrl+R toggle for recording (status bar shows "REC")
- [x] Implement playback mode (bypasses DeepgramTranscriptionService)

### Phase 3: Testing
- [ ] Add unit tests for serialization round-trip
- [ ] Add integration test: record -> playback -> verify same intents

## Verification

1. Set `AutoStart: true` in appsettings.json and run app - recording starts automatically
2. Speak some questions, then press Ctrl+S to stop transcription
3. Check `recordings/` folder for JSONL file
4. Run app with `--playback <file>` argument
5. Verify same intents appear in Detected Intents pane
6. Share JSONL file for analysis - human readable with timestamps

---

## Implementation Summary

### Files Created

| File | Lines | Description |
|------|-------|-------------|
| `Interview-assist-library/Pipeline/Recording/RecordedEvent.cs` | 127 | Polymorphic record types using System.Text.Json |
| `Interview-assist-library/Pipeline/Recording/SessionRecorder.cs` | 185 | Event subscription and JSONL writing |
| `Interview-assist-library/Pipeline/Recording/SessionPlayer.cs` | 135 | JSONL loading and realtime playback |
| `Interview-assist-library/Pipeline/Recording/RecordingOptions.cs` | 24 | Configuration with file path generation |

### Files Modified

| File | Changes |
|------|---------|
| `Interview-assist-transcription-detection-console/appsettings.json` | Added `Recording` section |
| `Interview-assist-transcription-detection-console/Program.cs` | Added command-line parsing, recording toggle, playback mode |

### Build Result

```
Build succeeded.
    29 Warning(s) (pre-existing)
    0 Error(s)
```

### Key Implementation Details

**RecordedEvent.cs:**
- Uses `[JsonPolymorphic]` and `[JsonDerivedType]` attributes for type discrimination
- Event types: SessionMetadata, AsrEvent, UtteranceEndSignal, UtteranceEvent, IntentEvent, ActionEvent
- All events have `OffsetMs` for timing relative to session start

**SessionRecorder.cs:**
- Subscribes to all 8 pipeline events (AsrPartial, AsrFinal, UtteranceOpen/Update/Final, IntentCandidate/Final, ActionTriggered)
- Uses lock for thread-safe file writing
- Flushes after each event for crash resilience
- Creates recording folder if it doesn't exist

**SessionPlayer.cs:**
- Loads all events into memory from JSONL
- Replays with `Task.Delay()` between events based on recorded timing
- Only dispatches input events (AsrEvent, UtteranceEndSignal) to pipeline
- Output events (Utterance, Intent, Action) are regenerated by pipeline

**Program.cs Changes:**
- `--playback <file>` argument for playback mode
- `--help` argument for usage information
- `Ctrl+R` toggles recording in live mode
- `Ctrl+S` stops transcription
- Status bar shows "REC" when recording, "RUNNING"/"STOPPED" for transcription state
- Window title shows "PLAYBACK: filename" in playback mode
- Playback mode skips Deepgram API key validation
- Auto-start recording when `AutoStart=true` in appsettings.json

**RecordingOptions.cs:**
- Added `AutoStart` property (default: false)
- When enabled, recording starts automatically when transcription begins
