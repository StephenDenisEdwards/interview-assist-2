# IMPROVEMENT-PLAN-0013: Deepgram Intent Recognition Strategy

**Created:** 2026-02-06
**Status:** Completed — implementation works, but evaluation shows Deepgram is unsuitable for question detection
**Completed:** 2026-02-06

## Evaluation Outcome

**Deepgram scored 0% F1 across all 4 recorded interview sessions.** The strategy is retained in the codebase but is not recommended for question detection. See [ADR-006](../architecture/decisions/ADR-006-deepgram-intent-recognition.md) for the full root cause analysis.

The core finding: Deepgram's `/v1/read` intent recognition is a **conversation routing** model (designed for "I want to check my balance" → route to billing), not a **speech act classifier** (is this a question?). It returns verb-phrase goal descriptions with confidence scores that don't meaningfully separate questions from statements.

### Results Summary

| Strategy | Recording 1 (3Q) | Recording 2 (28Q) | Recording 3 (15Q) | Recording 4 (39Q) |
|----------|:-:|:-:|:-:|:-:|
| Heuristic | 40% F1 | 27% F1 | 12% F1 | 40% F1 |
| **LLM** | **100% F1** | **46% F1** | **19% F1** | **57% F1** |
| Parallel | 40% F1 | 27% F1 | 12% F1 | 40% F1 |
| Deepgram | 0% F1 | 0% F1 | 0% F1 | 0% F1 |

Full evaluation data: `evaluations/strategy-comparison-analysis.md`

## Implementation Notes

### Key Design Deviation from Plan

The plan originally called for a separate `DeepgramIntentStrategy` class (Phase 3). During implementation, we determined this was unnecessary — `LlmIntentStrategy` is already parameterized on `ILlmIntentDetector` and handles all buffering, rate limiting, deduplication, and trigger logic. The only thing that changes is the detector backend.

**Actual approach:** `DeepgramIntentDetector` implements `ILlmIntentDetector` and is passed to `LlmIntentStrategy`. No separate strategy class was created. This means Phase 3 was effectively merged into Phase 1.

### Streaming Limitation

Deepgram's intent recognition is **not available on the live streaming WebSocket API** (`wss://api.deepgram.com/v1/listen`). It is only available on the pre-recorded REST endpoint (`/v1/listen`) and the text analysis REST endpoint (`/v1/read`).

The workaround implemented here sends finalized utterance text to `/v1/read?intents=true` via `HttpClient`. This adds ~50-100ms latency per detection call but fits cleanly into the existing `LlmIntentStrategy` buffering pattern, which already handles trigger-based batching and rate limiting.

### SDK Decision

We chose raw `HttpClient` over the Deepgram .NET SDK to avoid adding a new NuGet dependency. The `/v1/read` REST call is simple enough (JSON POST, parse response) that the SDK adds no meaningful value. Phase 5 is therefore N/A.

### Confidence Threshold

The default confidence threshold was lowered from 0.7 to 0.3 after discovering that Deepgram's confidence scores are calibrated very differently from LLM scores. Even explicit questions receive scores of 0.003–0.45, while non-questions receive 0.002–0.02. The 0.3 threshold allows some detections through but there is no threshold that cleanly separates questions from non-questions.

### Files Created/Modified

| Action | File |
|--------|------|
| Created | `Pipeline/Detection/DeepgramDetectionOptions.cs` |
| Created | `Pipeline/Detection/DeepgramIntentDetector.cs` |
| Created | `unit-tests/Pipeline/Detection/DeepgramIntentDetectorTests.cs` (37 tests) |
| Modified | `Pipeline/Detection/IntentDetectionMode.cs` — added `Deepgram` enum value |
| Modified | `Pipeline/Detection/IntentDetectionOptions.cs` — added `Deepgram` property |
| Modified | `transcription-detection-console/Program.cs` — config loading, strategy factory, API key validation |
| Modified | `transcription-detection-console/appsettings.json` — added Deepgram detection config section |
| Modified | `Extensions/ServiceCollectionExtensions.cs` — added Deepgram case to DI factory |
| Modified | `Pipeline/Evaluation/StrategyComparer.cs` — added Deepgram to strategy comparison |
| Modified | `transcription-detection-console/EvaluationRunner.cs` — fixed `CompareAsync` call signature |
| Modified | `transcription-detection-android/EvaluationRunner.cs` — fixed `CompareAsync` call signature |
| Modified | `Interview-assist-library.csproj` — added `InternalsVisibleTo` for test project |

### Bug Fixes Discovered During Evaluation

Running evaluations with real-time pacing exposed several pre-existing race conditions:

1. **UtteranceBuilder.CheckTimeouts NullReferenceException** — timer thread accessed `_current` after it was nulled by another thread. Fixed by capturing `_current` in local variable.
2. **UtteranceBuilder.CloseUtterance NullReferenceException** — concurrent close calls. Fixed with `Interlocked.Exchange(ref _current, null)`.
3. **UtteranceBuilder.UpdateUtterance NullReferenceException** — timer thread nulled `_current` mid-method. Fixed by capturing `_current` once in `ProcessAsrEvent` and passing through all sub-methods.
4. **LlmIntentStrategy.Dispose ObjectDisposedException** — `_timeoutCts` already disposed when `Cancel()` called. Fixed with try/catch.
5. **ParallelIntentStrategy.Dispose ObjectDisposedException** — same issue, same fix.
6. **StrategyComparer not using real-time pacing** — async strategies (LLM, Deepgram) need real-time delays between events to allow their rate limiters and triggers to fire. Fixed by replaying events using `OffsetMs`-based delays.

## Goal

Add Deepgram's built-in Intent Recognition as a new `IIntentDetectionStrategy` option in the transcription-detection-console, providing a lower-latency, lower-cost alternative to the current LLM-based (GPT-4o-mini) intent detection.

## Implementation Plan

### Phase 1: Deepgram Intent Detector (Medium effort) — COMPLETE

- [x] Verify whether the Deepgram .NET SDK supports `/v1/read` with intents, or if we need raw `HttpClient` — Used raw `HttpClient` (no new dependency)
- [x] Create `DeepgramIntentDetector` class implementing `ILlmIntentDetector`
- [x] Handle request construction: text payload, `intents=true`, optional `custom_intent` params
- [x] Parse response: extract `segments[].intents[]`, map to `DetectedIntent` records
- [x] Add intent-to-`IntentType` mapping logic (custom intent labels + keyword fallback)
- [x] Add rate limiting — inherited via `LlmIntentStrategy` reuse
- [x] Add error handling and logging (HTTP errors, empty responses, timeouts)

### Phase 2: Configuration (Low effort) — COMPLETE

- [x] Create `DeepgramDetectionOptions` record
- [x] Add `Deepgram` value to `IntentDetectionMode` enum
- [x] Add `Deepgram` section to `IntentDetectionOptions`
- [x] Update `appsettings.json` with Deepgram intent configuration section

### Phase 3: Strategy Implementation — SKIPPED (merged into Phase 1)

- [x] ~~Create `DeepgramIntentStrategy`~~ — Reused `LlmIntentStrategy` directly

### Phase 4: Console App Integration (Low effort) — COMPLETE

- [x] Update `Program.cs` strategy factory to handle `IntentDetectionMode.Deepgram`
- [x] Reuse existing Deepgram API key
- [x] Map Deepgram intent labels into the existing UI format

### Phase 5: NuGet Package — N/A (used raw HttpClient)

### Phase 6: Parallel+Deepgram Hybrid — NOT PURSUED

Given the 0% F1 results, a hybrid Heuristic+Deepgram strategy would not provide value over Heuristic alone.

### Phase 7: Evaluation & Benchmarking — COMPLETE

- [x] Add `Deepgram` mode to `StrategyComparer` for side-by-side comparison
- [x] Run all four modes on recorded sessions using playback mode
- [x] Measure precision, recall, F1, latency
- [x] Document results in ADR-006 and strategy-comparison-analysis.md
- [x] Fix real-time pacing in StrategyComparer for accurate async strategy evaluation
- [x] Fix race conditions discovered during evaluation runs

## References

- [ADR-006: Deepgram Intent Recognition](../architecture/decisions/ADR-006-deepgram-intent-recognition.md) — Full evaluation findings and root cause analysis
- [Strategy Comparison Analysis](../../../../evaluations/strategy-comparison-analysis.md) — Detailed metrics across all recordings
- [Deepgram Intent Recognition Docs](https://developers.deepgram.com/docs/intent-recognition)
- [Deepgram Text Intent Recognition](https://developers.deepgram.com/docs/text-intention-recognition)
