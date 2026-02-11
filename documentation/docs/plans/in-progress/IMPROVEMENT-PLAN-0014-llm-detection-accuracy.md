# LLM Intent Detection Accuracy Improvements

**Created:** 2026-02-07
**Status:** In Progress
**Focus:** Concentrate on the LLM detection strategy to improve F1 from 55.5% average

## Executive Summary

The LLM intent detection strategy (gpt-4o-mini) achieves 100% F1 on short recordings but degrades to 19% on longer ones. The root cause is the 800-character fixed buffer fragmenting context. This plan addresses that and other precision issues through a series of targeted improvements.

## Current Metrics (Baseline)

| Recording | Ground Truth | LLM F1 | Precision | Recall |
|-----------|-------------|--------|-----------|--------|
| Short (10K chars) | 3 questions | 100% | 100% | 100% |
| Medium (99K chars) | 28 questions | 46% | 36% | 64% |
| Long (131K chars) | 15 questions | 19% | 14% | 33% |
| Longer (86K chars) | 39 questions | 57% | 43% | 85% |
| **Average** | | **55.5%** | | |

## Improvement Steps

### Step 1: Sliding Context Window (High Impact, Medium Effort)

Replace the fixed 800-char `StringBuilder` buffer in `LlmIntentStrategy` with a sliding window of utterances. The key changes:

- Track utterances as a list, not concatenated text
- Maintain a read-only context window of older utterances for pronoun resolution
- Only ask the LLM to classify the newest (unprocessed) utterances
- Overlap windows so no utterance falls on a boundary
- Pass previous context via the existing `previousContext` parameter on `ILlmIntentDetector`

**Target:** Fix precision degradation on long recordings. The LLM should never re-classify already-processed utterances.

### Step 2: Inject ErrorAnalyzer Patterns into LLM Prompt (Low Effort)

The `ErrorAnalyzer` identifies 12 false positive patterns. Add explicit negative examples to the system prompt in `OpenAiIntentDetector`:
- Relative clauses ("which gives you...")
- Filler phrases ("you know what I mean")
- Self-corrections ("wait, actually...")
- Trailing intonation statements
- Code references

**Target:** Reduce false positives from the most common error categories.

### Step 3: Human-Labeled Ground Truth Dataset (Medium Effort)

Current ground truth is GPT-4o-extracted (circular bias). Create a hand-labeled dataset:
- Label 3-4 existing recordings with question boundaries
- Store as structured JSON alongside JSONL recordings
- Update `DatasetEvaluator` to load human labels
- Enable reliable regression testing

**Target:** Trustworthy metrics for optimizing against.

### Step 4: Two-Pass Classification (Medium Effort)

Split the current single-pass detect+classify into:
- Pass 1: "Is there a question in this text? Yes/No" (cheap, high recall)
- Pass 2 (only on Yes): Full extraction with type/subtype/confidence

**Target:** Reduce false positives on ambiguous statements.

### Step 5: Rhetorical Question Filtering (Low Effort)

Post-filter common rhetorical questions that appear in interview transcripts:
- "you know what I mean?"
- "right?"
- "does that make sense?"
- "okay?"

**Target:** Eliminate an entire class of false positives.

### Step 6: Buffer Size Experiment (Low Effort)

Quick experiment: increase `BufferMaxChars` from 800 to 2000-3000 and measure. Can validate whether larger context alone helps before the full sliding window.

### Step 7: Model Upgrade Experiment (Low Effort)

Test `gpt-4o` instead of `gpt-4o-mini`. The `ILlmIntentDetector` abstraction makes this a config change.

### Step 8: Threshold Tuning (Low Effort)

Apply `ThresholdTuner` results. Analyze whether a single threshold works or if adaptive thresholding is needed per-recording characteristics.

## Progress Tracker

| Step | Description | Status | Notes |
|------|-------------|--------|-------|
| 1 | Sliding Context Window | Done | Fully implemented in `LlmIntentStrategy.cs` with utterance lists and context window separation |
| 2 | Error Patterns in Prompt | Not Started | `ErrorAnalyzer` exists but patterns not yet injected into LLM prompt |
| 3 | Human-Labeled Ground Truth | Not Started | No human-labeled JSON files created yet |
| 4 | Two-Pass Classification | Not Started | Still single-pass in `OpenAiIntentDetector` |
| 5 | Rhetorical Question Filter | Not Started | No post-filter implemented |
| 6 | Buffer Size Experiment | Not Started | Infrastructure is configurable (`BufferMaxChars`), default still 800 |
| 7 | Model Upgrade (gpt-4o) | Not Started | Latency test exists in `QuickLatencyTests.cs`, not deployed |
| 8 | Threshold Tuning | Not Started | `ThresholdTuner` tool built, results not applied |

**Last updated:** 2026-02-11

## Implementation Priority

| Priority | Step | Description |
|----------|------|-------------|
| 1 | Step 1 | Sliding context window |
| 2 | Step 2 | Error patterns in prompt |
| 3 | Step 3 | Human-labeled dataset |
| 4 | Step 4 | Two-pass classification |
| 5 | Step 5 | Rhetorical question filter |
| 6 | Step 6 | Buffer size experiment |
| 7 | Step 7 | Model upgrade experiment |
| 8 | Step 8 | Threshold tuning |

## Success Criteria

- Average F1 > 70% across all test recordings
- No recording scores below 40% F1
- Precision > 50% on longest recordings
- No regression on short recordings (maintain 100%)

## References

- ADR-007: Multi-Strategy Intent Detection
- IMPROVEMENT-PLAN-0010: LLM Intent Detection (completed)
- IMPROVEMENT-PLAN-0012: Advanced Evaluation (completed)
- Strategy Comparison Analysis (evaluations/)
