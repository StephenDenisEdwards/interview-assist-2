# Utterance-Intent Pipeline Design

## Overview

This document describes a clean event-driven architecture for transforming Deepgram streaming ASR results into detected intents and triggered actions. The pipeline prioritizes **low latency for UI feedback** while ensuring **high correctness for action triggering**.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        UTTERANCE-INTENT PIPELINE                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Deepgram WS ──► AsrEventSource ──► Stabilizer ──► UtteranceBuilder ──►    │
│                      │                   │               │                  │
│                      │                   │               │                  │
│               asr.partial          stable_text    utterance.update          │
│               asr.final                          utterance.final            │
│                                                        │                    │
│                                                        ▼                    │
│                                              IntentDetector ──►             │
│                                                   │      │                  │
│                                          intent.candidate  intent.final     │
│                                                              │              │
│                                                              ▼              │
│                                                       ActionRouter ──►      │
│                                                              │              │
│                                                      action.triggered       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Event Types

### ASR Events
| Event | Description | Payload |
|-------|-------------|---------|
| `asr.partial` | Interim transcription hypothesis | `AsrEvent { Text, Words?, IsFinal=false }` |
| `asr.final` | Confirmed transcription segment | `AsrEvent { Text, Words?, IsFinal=true }` |

### Utterance Events
| Event | Description | Payload |
|-------|-------------|---------|
| `utterance.open` | New utterance started | `UtteranceEvent { Id, StartTime }` |
| `utterance.update` | Utterance content updated | `UtteranceEvent { Id, StableText, RawText, Duration }` |
| `utterance.final` | Utterance closed/committed | `UtteranceEvent { Id, FinalText, CloseReason }` |

### Intent Events
| Event | Description | Payload |
|-------|-------------|---------|
| `intent.candidate` | Tentative intent (UI hint only) | `IntentEvent { Intent, IsCandidate=true }` |
| `intent.final` | Committed intent (action-ready) | `IntentEvent { Intent, IsCandidate=false }` |

### Action Events
| Event | Description | Payload |
|-------|-------------|---------|
| `action.triggered` | Action executed | `ActionEvent { Action, Intent, Timestamp }` |

## Component Architecture

### 1. AsrEventSource
**Responsibility:** Normalize Deepgram WebSocket messages into canonical `AsrEvent` records.

```
Input:  Raw Deepgram JSON (Results, UtteranceEnd, SpeechStarted, etc.)
Output: asr.partial | asr.final events
```

**Key behavior:**
- Maps `is_final: false` → `asr.partial`
- Maps `is_final: true` → `asr.final`
- Maps `UtteranceEnd` → signals boundary (passed to UtteranceBuilder)
- Enriches events with `receivedAtUtc` for latency tracking

### 2. Stabilizer
**Responsibility:** Compute monotonically-growing stable text from volatile interim hypotheses.

```
Input:  Sequence of asr.partial texts for current utterance
Output: StableText (longest common prefix across last N hypotheses)
```

**Algorithm: Longest Common Prefix (LCP)**
```
hypotheses[0] = "What is a"
hypotheses[1] = "What is a lock"
hypotheses[2] = "What is a lock statement"
─────────────────────────────────────
stable_text  = "What is a"  (LCP of all 3)
```

**Configuration:**
- `WindowSize`: Number of hypotheses to compare (default: 3)
- `MinConfidence`: Word-level confidence threshold (default: 0.6)
- `RequireRepetition`: Low-confidence tokens must appear twice (default: true)

**Monotonicity guarantee:** `stable_text` never shrinks within an utterance.

### 3. UtteranceBuilder
**Responsibility:** Segment ASR stream into coherent utterances based on boundaries.

```
Input:  asr.partial, asr.final, timing signals
Output: utterance.open, utterance.update, utterance.final
```

**State Machine:**
```
                    ┌──────────────────────┐
                    │                      │
                    ▼                      │
    ┌───────┐   first word   ┌─────────┐  │  update
    │ IDLE  │ ─────────────► │ ACTIVE  │ ─┘
    └───────┘                └─────────┘
        ▲                         │
        │                         │ boundary detected
        │    utterance.final      │
        └─────────────────────────┘
```

**Close conditions (priority order):**
1. **Deepgram `UtteranceEnd`** - Explicit signal from Deepgram
2. **Terminal punctuation + pause** - `.` `?` `!` followed by ≥300ms silence
3. **Silence gap** - No speech for ≥750ms (configurable)
4. **Max duration guard** - Hard limit at 12s to prevent runaway buffering

**Guardrails:**
- `MaxUtteranceDuration`: 12 seconds
- `MaxUtteranceLength`: 500 characters
- `SilenceGapMs`: 750ms default
- `PunctuationPauseMs`: 300ms after terminal punctuation

### 4. IntentDetector
**Responsibility:** Classify utterances into intent categories with extracted slots.

```
Input:  utterance.update (for candidates), utterance.final (for commits)
Output: intent.candidate, intent.final
```

**Two-Stage Detection:**

| Stage | Trigger | Purpose | Can Fire Actions? |
|-------|---------|---------|-------------------|
| **Candidate** | `utterance.update` | UI hints, pre-warming | NO |
| **Final** | `utterance.final` | Commit decision | YES |

**Intent Taxonomy:**
```
IntentType
├── Question
│   ├── Definition    ("What is X?")
│   ├── HowTo         ("How do I X?")
│   ├── Compare       ("What's the difference between X and Y?")
│   └── Troubleshoot  ("Why isn't X working?")
├── Imperative
│   ├── Stop          ("stop", "cancel", "nevermind")
│   ├── Repeat        ("repeat", "say that again", "repeat number 3")
│   ├── Continue      ("continue", "go on", "next")
│   ├── StartOver     ("start over", "from the beginning")
│   └── Generate      ("generate 20 questions", "give me questions about X")
├── Statement         (declarative, no action needed)
└── Other             (unclear or noise)
```

**Detection Rules (Priority Order):**

1. **Imperative detection** (highest priority for command verbs):
   ```
   STOP_PATTERNS    = /^(stop|cancel|nevermind|never mind|quit|exit)/i
   REPEAT_PATTERNS  = /^(repeat|say (that|it) again|what did you say)/i
                    | /(repeat|say) (the )?(last|previous)/i
                    | /repeat (number |#)?\d+/i
   CONTINUE_PATTERNS= /^(continue|go on|next|proceed|keep going)/i
   START_OVER       = /start over|from the (beginning|start)|reset/i
   GENERATE         = /generate|give me|create|make/.* /questions?/i
   ```

2. **Polite imperative detection:**
   ```
   /^(please |can you |could you |would you )(stop|repeat|continue|...)/i
   → Strip prefix, apply imperative rules
   ```

3. **Question detection:**
   ```
   WH_WORDS    = /^(what|why|how|when|where|who|which|whose)/i
   AUX_VERBS   = /^(is|are|was|were|do|does|did|can|could|would|should|have|has|will)/i
   QUESTION_MARK = /\?$/
   KNOW_PATTERN  = /do you know|can you tell me|what's|what is/i
   ```

4. **Subtype classification:**
   - Definition: `what is`, `what does X mean`, `define`
   - HowTo: `how do I`, `how can I`, `how to`
   - Compare: `difference between`, `compare`, `vs`, `versus`
   - Troubleshoot: `why isn't`, `why doesn't`, `not working`, `error`

**Slot Extraction:**
```csharp
public record IntentSlots
{
    public string? Topic { get; init; }      // "lock statement in C#"
    public int? Count { get; init; }         // 20 (from "generate 20 questions")
    public string? Reference { get; init; }  // "number 3", "last", "previous"
}
```

### 5. ActionRouter
**Responsibility:** Debounce and route final intents to action handlers.

```
Input:  intent.final
Output: action.triggered
```

**Debounce Configuration:**
| Intent | Cooldown | Rationale |
|--------|----------|-----------|
| Stop/Cancel | 0ms | Immediate, no debounce needed |
| Repeat | 1500ms | Prevent double-tap |
| Continue | 1500ms | Prevent rapid-fire |
| Generate | 5000ms | Expensive operation |

**Conflict Resolution: Last-Wins**
Within a 1.5s conflict window:
- "Stop. Actually continue" → `continue` wins
- "Repeat number 3. No, number 5" → `repeat #5` wins

**Thread Safety:**
- Uses `ConcurrentDictionary` for cooldown tracking
- Atomic intent replacement during conflict window

## Data Flow Example

```
Time   Deepgram Input              Pipeline Events                    UI State
────── ─────────────────────────── ────────────────────────────────── ──────────────────
0ms    partial: "What"             asr.partial                        "What"
                                   utterance.open(id=1)
                                   utterance.update(stable="")

150ms  partial: "What is"          asr.partial                        "What is"
                                   utterance.update(stable="What")

300ms  partial: "What is a"        asr.partial                        "What is a"
                                   utterance.update(stable="What is")

500ms  partial: "What is a lock"   asr.partial                        "What is a lock"
                                   utterance.update(stable="What is a")
                                   intent.candidate(question/definition)

700ms  final: "What is a lock"     asr.final                          "What is a lock"
       (is_final=true)

850ms  partial: "statement"        asr.partial                        "What is a lock statement"
                                   utterance.update(stable="What is a lock")

1100ms partial: "statement used"   asr.partial                        "What is a lock statement used"
                                   utterance.update(stable="What is a lock statement")

1400ms final: "statement used      asr.final                          Full text
              for in C#?"          utterance.update(stable=full)

2100ms [silence 700ms]             utterance.final(id=1)
                                   intent.final(question/definition,
                                     topic="lock statement in C#")
                                   action.triggered(if applicable)
```

## Configuration

```csharp
public record PipelineOptions
{
    // Stabilizer
    public int StabilizerWindowSize { get; init; } = 3;
    public double MinWordConfidence { get; init; } = 0.6;

    // Utterance Builder
    public TimeSpan SilenceGapThreshold { get; init; } = TimeSpan.FromMilliseconds(750);
    public TimeSpan PunctuationPauseThreshold { get; init; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan MaxUtteranceDuration { get; init; } = TimeSpan.FromSeconds(12);
    public int MaxUtteranceLength { get; init; } = 500;

    // Action Router
    public TimeSpan ConflictWindow { get; init; } = TimeSpan.FromMilliseconds(1500);
    public Dictionary<IntentType, TimeSpan> Cooldowns { get; init; } = new()
    {
        [IntentType.Stop] = TimeSpan.Zero,
        [IntentType.Repeat] = TimeSpan.FromMilliseconds(1500),
        [IntentType.Continue] = TimeSpan.FromMilliseconds(1500),
        [IntentType.Generate] = TimeSpan.FromMilliseconds(5000)
    };
}
```

## Thread Safety Model

| Component | Thread Safety | Notes |
|-----------|---------------|-------|
| AsrEventSource | Single-threaded | WebSocket receive loop |
| Stabilizer | Single-threaded | Owned by UtteranceBuilder |
| UtteranceBuilder | Single-threaded | Processes events sequentially |
| IntentDetector | Stateless | Thread-safe by design |
| ActionRouter | Thread-safe | Uses concurrent collections |

All cross-component communication via events. Components should not share mutable state.

## Error Handling

- **Malformed ASR:** Log warning, skip event
- **Stabilizer overflow:** Evict oldest hypotheses
- **Utterance timeout:** Force close with `CloseReason.MaxDuration`
- **Action handler exception:** Catch, log, continue (don't block pipeline)

## Metrics (Future)

- `asr_to_utterance_latency_ms` - Time from first word to utterance.final
- `intent_detection_accuracy` - Manual review sampling
- `action_debounce_rate` - % of intents dropped by debounce
- `stabilizer_lag_tokens` - Tokens behind raw text
