# IMPROVEMENT-PLAN-0004: Transcription Quality & Question Detection Timing

**Created:** 2026-01-24
**Status:** Planned
**Priority:** High

## Problem Statement

The transcription console has two main issues:

1. **Poor Transcription Quality** - Loopback audio produces garbled/fragmented text even with clean source audio
2. **Question Detection Timing** - Questions are either detected too early (split across batches) or too late (batched at section breaks)

### Example Output Showing Issues

```
Of course here they are are in the beginning what is This is a lock statement used for instance... C-Sharp.
```

Should be: "What is a lock statement used for in C#?"

---

## Part A: Transcription Quality

### Root Causes

| Issue | Cause | Impact |
|-------|-------|--------|
| Fragmented sentences | 1500ms batch size too short | Cuts words/sentences mid-stream |
| Misheard technical terms | No language parameter | "C#" → "sea sharp", "see sharp" |
| Wrong vocabulary | No prompt guidance | Technical terms misheard |
| Hallucinations | Silence not filtered properly | "you you you..." artifacts |
| Audio quality | Loopback format issues | Resampling/stereo conversion problems |

### Tasks

#### Quick Wins (Config Only)

- [ ] **A1.** Set `Language = "en"` explicitly in `TimestampedTranscriptionOptions`
- [ ] **A2.** Increase `BatchMs` from 1500 to 3000-4000ms
- [ ] **A3.** Add vocabulary prompt to Whisper:
  ```
  "C#, async, await, IEnumerable, IQueryable, garbage collector,
   dependency injection, ConfigureAwait, lock statement, volatile,
   sealed, abstract, interface, delegate, expression tree"
  ```

#### Medium Effort

- [ ] **A4.** Add audio level normalization before transcription
- [ ] **A5.** Tune silence threshold specifically for loopback (currently 0.01 RMS)
- [ ] **A6.** Add audio quality metrics/logging to diagnose issues
- [ ] **A7.** Verify stereo→mono conversion is averaging channels correctly

#### Larger Changes

- [ ] **A8.** Implement adaptive batching based on speech boundaries (silence-delimited)
- [ ] **A9.** Evaluate alternative STT services (Deepgram, AssemblyAI) for real-time use
- [ ] **A10.** Consider local Whisper model (whisper-large-v3) for better accuracy

---

## Part B: Question Detection Timing

### Current Implementation

Two-phase detection was implemented:
- **Phase 1:** Quick local scan for `?` or imperative patterns → mark as candidate
- **Phase 2:** Confirm via speech pause signal OR stability timeout (800ms) OR buffer looks complete

### Problem

- Speech pauses only occur at section breaks (long gaps)
- Stability timeout keeps resetting with continuous speech
- `BufferLooksComplete()` heuristics not catching all cases
- Result: Questions batch up and appear all at once

### Tasks

- [ ] **B1.** Review and tune `BufferLooksComplete()` heuristics
- [ ] **B2.** Consider intra-batch silence detection (detect pauses within audio chunks)
- [ ] **B3.** Add "maximum pending time" - force detection after N seconds regardless
- [ ] **B4.** Experiment with shorter stability window (500ms instead of 800ms)
- [ ] **B5.** Consider hybrid approach: immediate detection for high-confidence complete questions

---

## Files Affected

| File | Changes |
|------|---------|
| `Interview-assist-transcription-console/Program.cs` | Config options |
| `Interview-assist-transcription-console/LlmQuestionDetector.cs` | Two-phase timing |
| `Interview-assist-pipeline/TimestampedTranscriptionService.cs` | Audio processing |
| `Interview-assist-pipeline/TranscriptModels.cs` | Options |

---

## Implementation Order

1. **Phase 1:** Quick wins A1-A3 (config changes only)
2. **Phase 2:** Test and evaluate improvement
3. **Phase 3:** Medium effort A4-A7 if needed
4. **Phase 4:** Question detection timing B1-B5
5. **Phase 5:** Larger changes A8-A10 if still insufficient

---

## Success Criteria

- [ ] Technical terms like "C#", "IEnumerable" transcribed correctly
- [ ] No "you you you" hallucinations
- [ ] Complete sentences, not fragments
- [ ] Questions detected within 2-3 seconds of being spoken
- [ ] No duplicate question detections
