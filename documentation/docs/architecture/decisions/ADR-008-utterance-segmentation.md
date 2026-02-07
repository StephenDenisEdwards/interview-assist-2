# ADR-008: Utterance Segmentation from Streaming ASR

## Status

Accepted

## Context

Streaming ASR (Deepgram) produces a continuous flow of interim and final text events. These raw events are not suitable for intent detection because:

1. **Interim results are unstable**: The same word position may be transcribed differently across consecutive events, causing flickering
2. **No natural boundaries**: ASR events arrive per-phrase, not per-utterance — a single question may span multiple events
3. **Intent detectors need complete text**: Both heuristic regex and LLM classification work better on coherent, complete segments

The system needs a component that converts the raw ASR stream into discrete, coherent utterances with stable text suitable for downstream processing.

### Requirements

- Distinguish stable (committed) text from provisional (may change) text
- Segment continuous speech into logical utterance units
- Support multiple close conditions (silence, punctuation, Deepgram signals, max duration)
- Produce events compatible with `IIntentDetectionStrategy.ProcessUtteranceAsync()`

## Decision

Implement a two-stage pipeline: **Stabilizer** → **UtteranceBuilder**.

### Stage 1: Stabilizer

The `Stabilizer` computes stable text from interim ASR hypotheses using a Longest Common Prefix (LCP) algorithm.

```
Hypothesis t=0: "What is the difference"
Hypothesis t=1: "What is the different"       ← "different" replaces "difference"
Hypothesis t=2: "What is the difference between"
                 ^^^^^^^^^^^^^^^^^^^^^^^^
                 LCP across window = stable
```

**Algorithm:**
- Maintains a sliding window of recent hypotheses (`Queue<HypothesisEntry>`)
- Computes LCP across all hypotheses in the window
- Stable text can only grow monotonically (never shrinks)
- `CommitFinal()` accepts Deepgram final results and resolves conflicts with existing stable text
- Optional word-level confidence filtering (`MinWordConfidence`, `RequireRepetitionForLowConfidence`)

**Configuration (`PipelineOptions`):**
- `StabilizerWindowSize` — number of hypotheses to compare (default: 3)
- `MinWordConfidence` — threshold for accepting low-confidence words
- `RequireRepetitionForLowConfidence` — require word to appear in multiple hypotheses

### Stage 2: UtteranceBuilder

The `UtteranceBuilder` segments the stabilized stream into discrete utterances with a state machine:

```
[No Utterance] → Open → Update* → Final
                  ↑                   |
                  └───────────────────┘
```

**Lifecycle Events:**
- `OnUtteranceOpen` — new utterance started (first ASR event after silence)
- `OnUtteranceUpdate` — utterance updated with new stable/raw text
- `OnUtteranceFinal` — utterance closed, final text emitted

**Close Conditions (checked in `CheckTimeouts()` and `CheckCloseConditions()`):**

| Condition | Default Threshold | Trigger |
|-----------|-------------------|---------|
| Silence gap | 750ms | No ASR activity for threshold duration |
| Terminal punctuation + pause | 300ms | `.`, `?`, or `!` followed by pause |
| Max duration | 12 seconds | Hard cap prevents runaway utterances |
| Max length | chars limit | Text length guard |
| Deepgram utterance-end | signal | Deepgram's native `UtteranceEnd` message |
| Manual | explicit call | `ForceClose()` for cleanup |

**Text Handling:**
- `CommittedText` — accumulated Deepgram final results
- `StableText` — committed + stabilizer output from interims
- `RawText` — latest full text including unstable interims
- `OnUtteranceFinal` uses `StableText` when available, falls back to `RawText`

**Thread Safety:**
- `CloseUtterance` uses `Interlocked.Exchange` to prevent double-close from concurrent timer and signal threads
- `CheckTimeouts()` captures `_current` locally before null check

### Data Flow

```
Deepgram ASR Events (interim + final)
    → Stabilizer (LCP → stable text)
    → UtteranceBuilder (segmentation → open/update/final events)
    → IIntentDetectionStrategy (intent classification)
    → ActionRouter (imperative routing)
```

## Consequences

### Positive

- **Stable input for detection**: Intent strategies receive coherent, non-flickering text
- **Multiple close heuristics**: Handles diverse speech patterns (pauses, punctuation, rapid-fire)
- **Deepgram integration**: Honors Deepgram's native `UtteranceEnd` and `is_final` signals
- **Testable**: Both `Stabilizer` and `UtteranceBuilder` accept injectable clocks for deterministic testing
- **Speaker tracking**: `SpeakerId` propagated from ASR events through utterance lifecycle

### Negative

- **Latency overhead**: Stabilizer adds delay proportional to window size before text is confirmed
- **Close condition tuning**: Default thresholds may not suit all speech patterns; five overlapping conditions can interact unexpectedly
- **No cross-utterance context**: Each utterance is independent; a question split across two utterances may be missed
- **Single-threaded assumption**: `UtteranceBuilder` is designed for single-producer use; concurrent `ProcessAsrEvent` calls are not safe (though `CloseUtterance` is thread-safe)

### Configuration

```csharp
var options = new PipelineOptions
{
    // Stabilizer
    StabilizerWindowSize = 3,
    MinWordConfidence = 0.5,

    // Utterance builder
    SilenceGapThreshold = TimeSpan.FromMilliseconds(750),
    PunctuationPauseThreshold = TimeSpan.FromMilliseconds(300),
    MaxUtteranceDuration = TimeSpan.FromSeconds(12),

    // Action router
    ConflictWindow = TimeSpan.FromMilliseconds(1500)
};

var stabilizer = new Stabilizer(options);
var builder = new UtteranceBuilder(options, stabilizer);
```
