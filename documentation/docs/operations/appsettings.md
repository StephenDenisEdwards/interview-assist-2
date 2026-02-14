# Application Settings Reference

This document describes all configuration options available in `appsettings.json` for the Interview Assist transcription-detection console application.

## Quick Reference

```json
{
  "UI": { "BackgroundColor": "#1E1E1E", "IntentColor": "#FFFF00" },
  "Logging": { "Folder": "logs" },
  "Recording": { "Folder": "recordings", "AutoStart": true, "SaveAudio": true },
  "Transcription": {
    "Mode": "Deepgram",
    "AudioSource": "Loopback",
    "Deepgram": { "Model": "nova-2", ... },
    "IntentDetection": {
      "Enabled": true,
      "Mode": "Llm",
      "Llm": { "Model": "gpt-4o-mini", ... }
    }
  },
  "Deepgram": { "ApiKey": "" },
  "QuestionDetection": { "Enabled": false, ... },
  "Reporting": { "Folder": "reports" },
  "Evaluation": { ... }
}
```

---

## UI Settings

```json
"UI": {
  "BackgroundColor": "#1E1E1E",
  "IntentColor": "#FFFF00"
}
```

### UI.BackgroundColor

**Type:** `string`
**Default:** `"#1E1E1E"`

Hex color code for the Terminal.Gui background.

### UI.IntentColor

**Type:** `string`
**Default:** `"#FFFF00"`

Hex color code for highlighting detected intents in the transcript view.

---

## Logging Settings

```json
"Logging": {
  "Folder": "logs"
}
```

### Logging.Folder

**Type:** `string`
**Default:** `"logs"`

Directory for application log files. Log files are named `session-YYYY-MM-DD-HHmmss-{pid}.log`.

---

## Recording Settings

```json
"Recording": {
  "Folder": "recordings",
  "FileNamePattern": "session-{timestamp}-{pid}.recording.jsonl",
  "AutoStart": true,
  "SaveAudio": true
}
```

### Recording.Folder

**Type:** `string`
**Default:** `"recordings"`

Directory where session JSONL recordings are saved.

### Recording.FileNamePattern

**Type:** `string`
**Default:** `"session-{timestamp}.recording.jsonl"`

Filename pattern for recordings. `{timestamp}` is replaced with `yyyy-MM-dd-HHmmss`. `{pid}` is replaced with the process ID.

### Recording.AutoStart

**Type:** `boolean`
**Default:** `false`

When enabled, recording begins automatically when the application starts.

### Recording.SaveAudio

**Type:** `boolean`
**Default:** `false`

When enabled, saves a WAV audio file alongside the JSONL recording. The WAV file uses the session ID with an `.audio.wav` suffix (e.g., `session-2026-02-10-171013-1234.audio.wav`).

---

## Transcription Settings

### Mode

**Type:** `string`
**Default:** `"Legacy"`
**Values:** `Legacy`, `Basic`, `Revision`, `Hypothesis`, `Deepgram`

Controls the transcription engine and stability tracking behavior.

| Mode | Description | Use Case |
|------|-------------|----------|
| `Legacy` | Traditional batched transcription with optional question detection. Uses `TimestampedTranscriptionService`. | General use, when question detection is needed |
| `Basic` | Streaming transcription where all text is immediately stable. Uses context prompting for continuity. | Simple applications, high-quality audio |
| `Revision` | Overlapping batches with local agreement policy. Text becomes stable after appearing consistently across multiple transcription passes. | High accuracy requirements |
| `Hypothesis` | Rapid hypothesis updates with stability tracking. Text becomes stable after remaining unchanged for N iterations or a timeout. | Lowest latency real-time display |
| `Deepgram` | Real-time streaming via Deepgram WebSocket API with utterance segmentation and intent detection pipeline. | Primary mode for live transcription with intent detection |

### AudioSource

**Type:** `string`
**Default:** `"Loopback"`
**Values:** `Loopback`, `Microphone`, `Mic`

Specifies the audio input source.

| Value | Description |
|-------|-------------|
| `Loopback` | Captures system audio output (what you hear through speakers/headphones) |
| `Microphone` / `Mic` | Captures audio from the default microphone input |

### SampleRate

**Type:** `integer`
**Default:** `16000`
**Unit:** Hz

Audio sample rate. Both Whisper and Deepgram expect 16kHz, so this is the recommended value. Higher values will be resampled.

### BatchMs

**Type:** `integer`
**Default:** `1500`
**Unit:** milliseconds

Batch interval for Legacy mode transcription. Lower values reduce latency but may decrease accuracy.

### IncludeWordTimestamps

**Type:** `boolean`
**Default:** `false`

When enabled, requests word-level timestamps from the Whisper API. Increases response size but provides precise timing for each word.

### Language

**Type:** `string`
**Default:** `"en"`

Language code for transcription (e.g., `en`, `es`, `fr`, `de`). Set to `null` or omit for automatic language detection.

### EnableContextPrompting

**Type:** `boolean`
**Default:** `true`

Enables context prompting, which provides recent stable text to the Whisper API to improve transcription continuity and reduce errors.

### ContextPromptMaxChars

**Type:** `integer`
**Default:** `200`

Maximum number of characters from recent transcript to include in the context prompt.

### VocabularyPrompt

**Type:** `string`
**Default:** `""`

Domain-specific vocabulary to improve recognition of technical terms. Comma-separated list of terms.

**Example:**
```json
"VocabularyPrompt": "C#, async, await, IEnumerable, Kubernetes, OAuth"
```

---

## Basic Mode Settings

Settings specific to `Mode: "Basic"`.

```json
"Basic": {
  "BatchMs": 3000,
  "MaxBatchMs": 6000
}
```

### Basic.BatchMs

**Type:** `integer`
**Default:** `3000`
**Unit:** milliseconds

Minimum batch window before transcription. Audio is accumulated until this threshold is reached or silence is detected.

### Basic.MaxBatchMs

**Type:** `integer`
**Default:** `6000`
**Unit:** milliseconds

Maximum batch window. Forces transcription even if no silence is detected, preventing excessive latency during continuous speech.

---

## Revision Mode Settings

Settings specific to `Mode: "Revision"`.

```json
"Revision": {
  "OverlapMs": 1500,
  "BatchMs": 2000,
  "AgreementCount": 2,
  "SimilarityThreshold": 0.85
}
```

### Revision.OverlapMs

**Type:** `integer`
**Default:** `1500`
**Unit:** milliseconds

Overlap duration between consecutive batches. Larger overlaps increase accuracy but also API costs.

### Revision.BatchMs

**Type:** `integer`
**Default:** `2000`
**Unit:** milliseconds

Batch duration for each transcription window.

### Revision.AgreementCount

**Type:** `integer`
**Default:** `2`

Number of times text must appear consistently across transcription passes to become stable. Higher values increase accuracy but also latency.

### Revision.SimilarityThreshold

**Type:** `double`
**Default:** `0.85`
**Range:** `0.0` - `1.0`

Minimum Jaccard similarity for text segments to be considered matching. Lower values are more permissive, higher values require near-exact matches.

---

## Hypothesis Mode Settings

Settings specific to `Mode: "Hypothesis"`.

```json
"Hypothesis": {
  "MinBatchMs": 500,
  "UpdateIntervalMs": 250,
  "StabilityIterations": 3,
  "StabilityTimeoutMs": 2000,
  "FlickerCooldownMs": 100
}
```

### Hypothesis.MinBatchMs

**Type:** `integer`
**Default:** `500`
**Unit:** milliseconds

Minimum batch duration before transcription. Ensures enough audio for meaningful transcription.

### Hypothesis.UpdateIntervalMs

**Type:** `integer`
**Default:** `250`
**Unit:** milliseconds

Interval between hypothesis updates. Lower values provide more responsive updates but increase API costs.

### Hypothesis.StabilityIterations

**Type:** `integer`
**Default:** `3`

Number of unchanged iterations before provisional text becomes stable. Prevents premature stabilization.

### Hypothesis.StabilityTimeoutMs

**Type:** `integer`
**Default:** `2000`
**Unit:** milliseconds

Timeout after which provisional text becomes stable regardless of iteration count. Ensures text eventually stabilizes during pauses.

### Hypothesis.FlickerCooldownMs

**Type:** `integer`
**Default:** `100`
**Unit:** milliseconds

Cooldown period to prevent rapid provisional text updates, reducing visual flickering in the UI.

---

## Deepgram Transcription Settings

Settings specific to `Mode: "Deepgram"`. Nested under `Transcription.Deepgram`.

```json
"Deepgram": {
  "Model": "nova-2",
  "InterimResults": true,
  "Punctuate": true,
  "SmartFormat": true,
  "EndpointingMs": 300,
  "UtteranceEndMs": 1000,
  "Keywords": "",
  "Vad": true,
  "Diarize": true
}
```

### Deepgram.Model

**Type:** `string`
**Default:** `"nova-2"`

Deepgram speech recognition model. Options include `nova-2`, `nova`, `enhanced`, `base`.

### Deepgram.InterimResults

**Type:** `boolean`
**Default:** `true`

Enables interim (partial) transcription results streamed before final results. Required for real-time display.

### Deepgram.Punctuate

**Type:** `boolean`
**Default:** `true`

Enables automatic punctuation in transcription output.

### Deepgram.SmartFormat

**Type:** `boolean`
**Default:** `true`

Enables smart formatting (e.g., converting numbers to digits, formatting dates).

### Deepgram.EndpointingMs

**Type:** `integer`
**Default:** `300`
**Unit:** milliseconds

Duration of silence before Deepgram considers a speech segment final. Lower values produce faster finalization but may split utterances.

### Deepgram.UtteranceEndMs

**Type:** `integer`
**Default:** `1000`
**Unit:** milliseconds

Duration of silence before Deepgram sends an `UtteranceEnd` signal, indicating a natural pause boundary.

### Deepgram.Keywords

**Type:** `string`
**Default:** `""`

Comma-separated keywords to boost recognition of specific terms.

### Deepgram.Vad

**Type:** `boolean`
**Default:** `true`

Enables Voice Activity Detection to filter out non-speech audio.

### Deepgram.Diarize

**Type:** `boolean`
**Default:** `false`

Enables speaker diarization to distinguish between different speakers.

---

## Intent Detection Settings

Settings for the utterance-intent detection pipeline. Nested under `Transcription.IntentDetection`. Active in `Deepgram` mode.

```json
"IntentDetection": {
  "Enabled": true,
  "Mode": "Llm",
  "Heuristic": { "MinConfidence": 0.4 },
  "Llm": {
    "Model": "gpt-4o-mini",
    "ConfidenceThreshold": 0.7,
    "SystemPromptFile": "system-prompt.txt",
    ...
  }
}
```

### IntentDetection.Enabled

**Type:** `boolean`
**Default:** `true`

Enables or disables the intent detection pipeline.

### IntentDetection.Mode

**Type:** `string`
**Default:** `"Heuristic"`
**Values:** `Heuristic`, `Llm`, `Parallel`

Detection strategy to use.

| Mode | Description |
|------|-------------|
| `Heuristic` | Pattern matching and linguistic rules. Fast and free. |
| `Llm` | LLM-based classification. More accurate but has API costs. |
| `Parallel` | Runs both Heuristic and LLM in parallel, merges results. |

### IntentDetection.Heuristic.MinConfidence

**Type:** `double`
**Default:** `0.4`
**Range:** `0.0` - `1.0`

Minimum confidence threshold for heuristic-based detections.

### IntentDetection.Llm.Model

**Type:** `string`
**Default:** `"gpt-4o-mini"`

OpenAI model for LLM-based intent classification.

### IntentDetection.Llm.ApiKey

**Type:** `string?`
**Default:** `null`

API key for LLM calls. Falls back to `OPENAI_API_KEY` environment variable if not set.

### IntentDetection.Llm.ConfidenceThreshold

**Type:** `double`
**Default:** `0.7`
**Range:** `0.0` - `1.0`

Minimum LLM confidence to accept a detection.

### IntentDetection.Llm.RateLimitMs

**Type:** `integer`
**Default:** `2000`
**Unit:** milliseconds

Minimum interval between LLM API calls.

### IntentDetection.Llm.BufferMaxChars

**Type:** `integer`
**Default:** `800`

Maximum characters in the unprocessed utterance buffer before a forced detection trigger.

### IntentDetection.Llm.ContextWindowChars

**Type:** `integer`
**Default:** `1500`

Maximum characters retained in the processed context window for pronoun resolution.

### IntentDetection.Llm.TriggerOnQuestionMark

**Type:** `boolean`
**Default:** `true`

Trigger LLM detection when a `?` appears in utterance text.

### IntentDetection.Llm.TriggerOnPause

**Type:** `boolean`
**Default:** `true`

Trigger LLM detection when a speech pause is signaled.

### IntentDetection.Llm.TriggerTimeoutMs

**Type:** `integer`
**Default:** `3000`
**Unit:** milliseconds

Trigger detection after this many milliseconds of inactivity.

### IntentDetection.Llm.EnablePreprocessing

**Type:** `boolean`
**Default:** `true`

Apply noise removal and technical term correction before sending text to LLM.

### IntentDetection.Llm.EnableDeduplication

**Type:** `boolean`
**Default:** `true`

Enable semantic fingerprint deduplication to prevent duplicate detections.

### IntentDetection.Llm.DeduplicationWindowMs

**Type:** `integer`
**Default:** `30000`
**Unit:** milliseconds

Time window for suppressing duplicate intent detections.

### IntentDetection.Llm.SystemPromptFile

**Type:** `string?`
**Default:** `null`

Path to a custom system prompt file for LLM intent detection. Relative paths are resolved from the application base directory. When `null`, uses the built-in default prompt. The file `system-prompt.txt` ships with the application and can be customized.

---

## Question Detection Settings (Legacy Mode)

Settings for automatic question detection. Only active in Legacy mode.

```json
"QuestionDetection": {
  "Enabled": false,
  "Method": "Llm",
  "Model": "gpt-4o-mini",
  "ConfidenceThreshold": 0.7,
  "DetectionIntervalMs": 2000,
  "MinBufferLength": 50,
  "DeduplicationWindowMs": 30000,
  "EnableTechnicalTermCorrection": true,
  "EnableNoiseFilter": true
}
```

### QuestionDetection.Enabled

**Type:** `boolean`
**Default:** `false`

Enables or disables automatic question detection.

### QuestionDetection.Method

**Type:** `string`
**Default:** `"Llm"`
**Values:** `Llm`, `Heuristic`

Detection method to use.

| Method | Description |
|--------|-------------|
| `Llm` | Uses an LLM (GPT) to semantically detect questions. More accurate but has API costs. |
| `Heuristic` | Uses pattern matching and linguistic rules. Faster and free but less accurate. |

### QuestionDetection.Model

**Type:** `string`
**Default:** `"gpt-4o-mini"`

OpenAI model to use for LLM-based question detection. Only applies when `Method` is `Llm`.

### QuestionDetection.ConfidenceThreshold

**Type:** `double`
**Default:** `0.7`
**Range:** `0.0` - `1.0`

Minimum confidence score for a detected question to be reported. Higher values reduce false positives but may miss some questions.

### QuestionDetection.DetectionIntervalMs

**Type:** `integer`
**Default:** `2000`
**Unit:** milliseconds

Interval between detection checks. Lower values detect questions faster but increase API costs.

### QuestionDetection.MinBufferLength

**Type:** `integer`
**Default:** `50`
**Unit:** characters

Minimum transcript buffer length before detection is attempted. Prevents detection on very short fragments.

### QuestionDetection.DeduplicationWindowMs

**Type:** `integer`
**Default:** `30000`
**Unit:** milliseconds

Time window for question deduplication. The same question won't be reported twice within this window.

### QuestionDetection.EnableTechnicalTermCorrection

**Type:** `boolean`
**Default:** `true`

Enables correction of common transcription errors for technical terms before question detection.

### QuestionDetection.EnableNoiseFilter

**Type:** `boolean`
**Default:** `true`

Filters out noise phrases (filler words, false starts) before question detection.

---

## Reporting Settings

```json
"Reporting": {
  "Folder": "reports"
}
```

### Reporting.Folder

**Type:** `string`
**Default:** `"reports"`

Directory for session report markdown files. Reports are generated by `--headless` and `--analyze` modes and include event distribution, intent detection results, latency statistics, and evaluation summaries.

---

## Evaluation Settings

Settings for the offline evaluation framework.

```json
"Evaluation": {
  "Model": "gpt-4o",
  "MatchThreshold": 0.7,
  "DeduplicationThreshold": 0.8,
  "OutputFolder": "evaluations",
  "DatasetsFolder": "evaluations/datasets",
  "BaselinesFolder": "evaluations/baselines",
  "ThresholdRange": { "Min": 0.3, "Max": 0.95, "Step": 0.05 },
  "PromptVariants": []
}
```

### Evaluation.Model

**Type:** `string`
**Default:** `"gpt-4o"`

OpenAI model used for evaluation matching.

### Evaluation.MatchThreshold

**Type:** `double`
**Default:** `0.7`

Similarity threshold for matching detected intents against ground truth.

### Evaluation.DeduplicationThreshold

**Type:** `double`
**Default:** `0.8`

Jaccard similarity threshold for deduplicating evaluation results.

### Evaluation.OutputFolder

**Type:** `string`
**Default:** `"evaluations"`

Directory for evaluation output files.

### Evaluation.GroundTruthFile

**Type:** `string?`
**Default:** `null`

Path to a human-labeled ground truth JSON file. When set, skips LLM extraction and loads ground truth from this file. Can also be specified via `--ground-truth <file>` on the command line. The file format is a JSON array of objects with `Text`, `Subtype`, `Confidence`, and `ApproximatePosition` fields.

### Evaluation.DatasetsFolder

**Type:** `string`
**Default:** `"evaluations/datasets"`

Directory containing evaluation dataset JSONL files.

### Evaluation.BaselinesFolder

**Type:** `string`
**Default:** `"evaluations/baselines"`

Directory containing baseline evaluation results for regression testing.

---

## Deepgram API Configuration

```json
"Deepgram": {
  "ApiKey": ""
}
```

### Deepgram.ApiKey

**Type:** `string`

Deepgram API key for transcription. Can also be set via the `DEEPGRAM_API_KEY` environment variable.

---

## Environment Variables

### OPENAI_API_KEY

**Required for LLM intent detection and evaluation.** OpenAI API key.

Can be set via:
- Environment variable: `OPENAI_API_KEY`
- appsettings.json: `"OpenAI": { "ApiKey": "sk-..." }`
- User secrets (recommended for development)

### DEEPGRAM_API_KEY

**Required for Deepgram transcription mode.** Deepgram API key.

Can be set via:
- Environment variable: `DEEPGRAM_API_KEY`
- appsettings.json: `"Deepgram": { "ApiKey": "..." }`
- User secrets (recommended for development)

---

## Command Line Options

### Playback & Analysis

```bash
# Replay a recorded JSONL session (no audio/API required)
--playback <file.jsonl>

# Re-transcribe a WAV recording via Deepgram
--playback <file.wav>

# Headless mode: no Terminal.Gui UI, outputs console summary + session report + auto-evaluation
--playback <file> --headless

# Generate a markdown report from an existing JSONL file (no playback) + auto-evaluation
--analyze <file.jsonl>
```

### Evaluation Options

```bash
# Use human-labeled ground truth instead of LLM extraction
--ground-truth <file.json>
```

### Audio & Transcription Overrides

```bash
# Audio source
--mic, -m              # Use microphone input
--loopback, -l         # Use system audio loopback

# Transcription mode
--mode <mode>          # Set mode: legacy, basic, revision, hypothesis, deepgram

# Legacy mode options
--batch, -b <ms>       # Batch interval in milliseconds
--lang <code>          # Language code

# Context prompting
--vocabulary, --vocab <terms>   # Technical vocabulary

# Question detection (Legacy mode only)
--detection, -d <method>        # Detection method: heuristic or llm
--detection-model <model>       # LLM model for detection
```

### Examples

```bash
# Live transcription with Deepgram and LLM intent detection
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj

# Replay a JSONL recording with full UI
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.jsonl

# Headless analysis of a WAV recording
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.wav --headless

# Generate report from existing session data
dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --analyze recordings/session.jsonl
```

### Output Locations

| Mode | Output |
|------|--------|
| Live transcription | Terminal.Gui UI, JSONL recording in `recordings/`, optional WAV file |
| `--playback` (interactive) | Terminal.Gui UI with replay |
| `--playback --headless` | Console summary + markdown report in `reports/` + auto-evaluation in `evaluations/` |
| `--analyze` | Markdown report in `reports/` + auto-evaluation in `evaluations/` |
