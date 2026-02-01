# Improving Question Detection in Streaming Transcription

**Created:** 2026-02-01
**Status:** Planned

## Problem Statement

The current heuristic question detector misses questions due to utterance fragmentation. When silence gaps (~750ms) split sentences, the detector sees incomplete text and makes decisions before the question mark arrives.

### Example

```
What the detector sees:              What the full sentence is:
─────────────────────────────────    ─────────────────────────────
"Have you really thought"            "Have you really thought about
[750ms silence - utterance ends]      this?"
"about this? Have you"
[750ms silence - utterance ends]
```

The detector classifies "Have you really thought" as a Statement because no `?` is present.

## Detection Rate Analysis

From a recorded session with 9 actual questions:

| # | Question | Detected? | Issue |
|---|----------|-----------|-------|
| 1 | "Have you really thought about this?" | No | Split before `?` |
| 2 | "Have you thought about putting your company's name on this poster?" | Partial | Missing beginning |
| 3 | "Do you understand the impact this is gonna have on a normal person looking at that?" | Partial | Missing beginning |
| 4 | "Who do you think Optimus would be a better surgeon than the best surgeons?" | Yes | 0.9 confidence |
| 5 | "How long for that?" | Yes | 0.9 confidence |
| 6 | "If it's four or five years, who cares?" | No | Rhetorical, embedded |
| 7 | "Where does this fit in the broader context?" | Yes | 0.5 confidence |
| 8 | "What the hell is going on?" | Yes | Combined with #7 |
| 9 | "And does this fit somewhere into the broader conflict of geopolitics?" | Yes | 0.5 confidence |

**Result:** 6/9 detected (67% recall), 3 missed due to fragmentation

## Root Causes

1. **Utterance fragmentation** - Silence gaps split sentences before `?` arrives
2. **No look-back mechanism** - Can't reconsider previous utterances when new context arrives
3. **Weak interrogative word detection** - "Have you", "Do you" not weighted heavily enough without `?`
4. **No cross-utterance context** - Each utterance evaluated in isolation

## Potential Solutions

### Option 1: Stronger Interrogative Patterns (Low effort)
Increase confidence for sentences starting with:
- "Have you...", "Do you...", "Did you..."
- "Is this...", "Are you...", "Was it..."
- "Can you...", "Could you...", "Would you..."

Even without `?`, these patterns strongly suggest questions.

**Pros:** Simple to implement, no architectural changes
**Cons:** May increase false positives

### Option 2: Look-back Window (Medium effort)
When a `?` is detected at the start of an utterance, look back at the previous utterance and re-evaluate as a combined question.

```csharp
// Pseudo-code
if (currentText.TrimStart().StartsWith("?") || previousUtterance.EndedRecently)
{
    var combined = previousUtterance.Text + " " + currentText;
    ReEvaluateAsQuestion(combined, previousUtteranceId);
}
```

**Pros:** Catches split questions accurately
**Cons:** Requires tracking previous utterances, delayed detection

### Option 3: Longer Silence Threshold (Configuration)
Increase `EndpointingMs` or `UtteranceEndMs` in Deepgram settings to reduce fragmentation.

Current settings:
```json
"EndpointingMs": 300,
"UtteranceEndMs": 1000
```

**Pros:** Simple configuration change
**Cons:** Delays all detection, not just questions

### Option 4: LLM-based Detection (High effort)
Use an LLM to evaluate utterances with broader context.

**Pros:** Best accuracy, understands semantics
**Cons:** Latency, cost, requires API calls

### Option 5: Hybrid Approach (Recommended)
Combine Options 1 and 2:
1. Strengthen interrogative pattern detection (immediate)
2. Add look-back when `?` detected at utterance boundary (catches splits)

## Implementation Plan

### Phase 1: Strengthen Interrogative Patterns
- [ ] Add interrogative prefix patterns to `IntentDetector`
- [ ] Increase confidence for "Have you", "Do you", "What", "How", "Where", "Why", "Who" patterns
- [ ] Add unit tests for these patterns

### Phase 2: Look-back Window
- [ ] Track last N utterances in `UtteranceIntentPipeline`
- [ ] Detect `?` at start of new utterance
- [ ] Re-emit corrected intent for previous utterance
- [ ] Add "QuestionCorrected" event type

### Phase 3: Testing
- [ ] Use recorded sessions for regression testing
- [ ] Measure precision/recall before and after changes
- [ ] Tune confidence thresholds

## Files to Modify

| File | Changes |
|------|---------|
| `IntentDetector.cs` | Add interrogative prefix patterns |
| `UtteranceIntentPipeline.cs` | Add look-back window logic |
| `IntentDetectorTests.cs` | Add tests for new patterns |

## Success Criteria

- Detect 8/9 questions from test recording (89%+ recall)
- Maintain precision (no significant increase in false positives)
- No added latency for non-question utterances
