# Application Settings Reference

This document describes all configuration options available in `appsettings.json` for the Interview Assist transcription console application.

## Quick Reference

```json
{
  "Transcription": {
    "Mode": "Legacy",
    "AudioSource": "Loopback",
    "SampleRate": 16000,
    "BatchMs": 1500,
    "Language": "en",
    ...
  },
  "QuestionDetection": {
    "Enabled": false,
    ...
  }
}
```

---

## Transcription Settings

### Mode

**Type:** `string`
**Default:** `"Legacy"`
**Values:** `Legacy`, `Basic`, `Revision`, `Hypothesis`

Controls the transcription engine and stability tracking behavior.

| Mode | Description | Use Case |
|------|-------------|----------|
| `Legacy` | Traditional batched transcription with optional question detection. Uses `TimestampedTranscriptionService`. | General use, when question detection is needed |
| `Basic` | Streaming transcription where all text is immediately stable. Uses context prompting for continuity. | Simple applications, high-quality audio |
| `Revision` | Overlapping batches with local agreement policy. Text becomes stable after appearing consistently across multiple transcription passes. | High accuracy requirements |
| `Hypothesis` | Rapid hypothesis updates with stability tracking. Text becomes stable after remaining unchanged for N iterations or a timeout. | Lowest latency real-time display |

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

Audio sample rate. Whisper API expects 16kHz, so this is the recommended value. Higher values will be resampled.

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

## Question Detection Settings

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

## Environment Variables

### OPENAI_API_KEY

**Required.** OpenAI API key for transcription and question detection.

Can be set via:
- Environment variable: `OPENAI_API_KEY`
- appsettings.json: `"OpenAI": { "ApiKey": "sk-..." }`
- User secrets (recommended for development)

---

## Command Line Overrides

Configuration can be overridden via command line arguments:

```bash
# Audio source
--mic, -m              # Use microphone input
--loopback, -l         # Use system audio loopback

# Transcription mode
--mode <mode>          # Set mode: legacy, basic, revision, hypothesis

# Legacy mode options
--batch, -b <ms>       # Batch interval in milliseconds
--lang <code>          # Language code

# Context prompting
--vocabulary, --vocab <terms>   # Technical vocabulary

# Question detection (Legacy mode only)
--detection, -d <method>        # Detection method: heuristic or llm
--detection-model <model>       # LLM model for detection
```

**Examples:**

```bash
# Basic mode with microphone
Interview-assist-transcription-console --mode basic --mic

# Revision mode with vocabulary
Interview-assist-transcription-console --mode revision --vocab "C#, async"

# Hypothesis mode for real-time
Interview-assist-transcription-console --mode hypothesis

# Legacy mode with LLM question detection
Interview-assist-transcription-console --detection llm
```
