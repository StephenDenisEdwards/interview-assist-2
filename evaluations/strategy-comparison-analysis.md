# Strategy Comparison Analysis

Generated: 2026-02-06

## What the Evaluation Does

The evaluation replays recorded interview sessions through each detection strategy and measures how well each one identifies questions. Here's the process:

1. **Ground truth extraction**: GPT-4o reads the full transcript and identifies all actual questions asked — this is the "answer key"
2. **Strategy replay**: Each ASR event from the recording is fed through the detection pipeline in real-time, and each strategy independently decides what's a question
3. **Matching**: Detected questions are compared against ground truth using text similarity to determine correct vs incorrect detections

## The Metrics

- **Detected**: Total number of items the strategy flagged as questions
- **TP (True Positives)**: Correctly identified questions — flagged as a question AND actually is one
- **FP (False Positives)**: False alarms — flagged as a question but isn't one
- **FN (False Negatives)**: Missed questions — actually a question but wasn't flagged
- **Precision**: Of everything flagged, what percentage was actually a question (`TP / (TP + FP)`)
- **Recall**: Of all real questions, what percentage was found (`TP / (TP + FN)`)
- **F1**: Harmonic mean of precision and recall — balances both into a single score

## Results

### Recording 1: session-162854 (10K chars, 3 ground truth questions)

| Strategy | Detected | TP | FP | FN | Precision | Recall | F1 |
|----------|----------|----|----|-----|-----------|--------|----|
| Heuristic | 7 | 2 | 5 | 1 | 29% | 67% | 40% |
| **LLM** | **3** | **3** | **0** | **0** | **100%** | **100%** | **100%** |
| Parallel | 7 | 2 | 5 | 1 | 29% | 67% | 40% |
| Deepgram | 0 | 0 | 0 | 3 | 0% | 0% | 0% |

### Recording 2: session-155843 (99K chars, 28 ground truth questions)

| Strategy | Detected | TP | FP | FN | Precision | Recall | F1 |
|----------|----------|----|----|-----|-----------|--------|----|
| Heuristic | 77 | 14 | 63 | 14 | 18% | 50% | 27% |
| **LLM** | **50** | **18** | **32** | **10** | **36%** | **64%** | **46%** |
| Parallel | 77 | 14 | 63 | 14 | 18% | 50% | 27% |
| Deepgram | 2 | 0 | 2 | 28 | 0% | 0% | 0% |

### Recording 3: session-163251 (131K chars, 15 ground truth questions)

| Strategy | Detected | TP | FP | FN | Precision | Recall | F1 |
|----------|----------|----|----|-----|-----------|--------|----|
| Heuristic | 103 | 7 | 96 | 8 | 7% | 47% | 12% |
| **LLM** | **37** | **5** | **32** | **10** | **14%** | **33%** | **19%** |
| Parallel | 103 | 7 | 96 | 8 | 7% | 47% | 12% |
| Deepgram | 2 | 0 | 2 | 15 | 0% | 0% | 0% |

### Recording 4: session-114135 (86K chars, 39 ground truth questions)

| Strategy | Detected | TP | FP | FN | Precision | Recall | F1 |
|----------|----------|----|----|-----|-----------|--------|----|
| Heuristic | 105 | 29 | 76 | 10 | 28% | 74% | 40% |
| **LLM** | **77** | **33** | **44** | **6** | **43%** | **85%** | **57%** |
| Parallel | 105 | 29 | 76 | 10 | 28% | 74% | 40% |
| Deepgram | 2 | 0 | 2 | 39 | 0% | 0% | 0% |

## Strategy Analysis

### LLM (OpenAI gpt-4o-mini) — Best across all recordings

| Recording | F1 | Precision | Recall |
|-----------|-----|-----------|--------|
| 162854 (short) | 100% | 100% | 100% |
| 155843 | 46% | 36% | 64% |
| 163251 | 19% | 14% | 33% |
| 114135 | 57% | 43% | 85% |

The LLM strategy consistently wins because it actually *understands* the text. It reads the buffered utterance and reasons about whether it contains a question. On the short recording it got a perfect score. On longer recordings, performance drops but it still outperforms everything else. The main weakness is false positives — it's aggressive about flagging things as questions.

### Heuristic — High recall, terrible precision

| Recording | F1 | Precision | Recall |
|-----------|-----|-----------|--------|
| 162854 | 40% | 29% | 67% |
| 155843 | 27% | 18% | 50% |
| 163251 | 12% | 7% | 47% |
| 114135 | 40% | 28% | 74% |

The heuristic strategy uses pattern matching (question marks, question words like "what", "how", "why"). It catches roughly half to three-quarters of real questions (decent recall), but generates massive numbers of false alarms — flagging 77-105 items when only 3-39 are actual questions. The 7% precision on recording 163251 means 93% of its detections were wrong.

### Parallel — Identical to Heuristic

Parallel runs both Heuristic and LLM simultaneously and merges results. In practice, its numbers are identical to Heuristic because the heuristic fires much faster (17ms vs 26-45s latency) and dominates the output. The LLM detections get deduplicated as duplicates of what the heuristic already found.

### Deepgram — Not functional for question detection

| Recording | F1 | Precision | Recall |
|-----------|-----|-----------|--------|
| All four | 0% | 0% | 0% |

Deepgram's `/v1/read` intent recognition is a general-purpose intent classifier, not a question detector. When given text like "What is the difference between microservices and monolithic architecture?", Deepgram returns labels like "Find out difference between microservices and monolithic architecture" with a confidence of 0.003. It describes *what the speaker is doing* as a verb phrase, not *whether they're asking a question*. The confidence scores are an order of magnitude lower than what you'd get from an LLM, and the labels don't reliably map to "this is a question."

## Practical Implications

1. **LLM is the clear winner** for question detection accuracy, but it comes with latency (26-45 seconds average) and API cost per detection call
2. **Heuristic is fast but noisy** — useful as a first-pass filter but generates too many false positives to use alone in production
3. **Parallel doesn't add value** in its current form — the heuristic results overwhelm the LLM results. It would need a smarter merging strategy (e.g., only emit when both agree, or use heuristic as a gate for LLM)
4. **Deepgram intent recognition is not designed for this use case** — it's meant for routing conversations ("book a flight", "cancel order") not detecting questions in free-form interview speech

## Notes

The ground truth itself varies between runs (GPT-4o found 3, 15, 28, or 39 questions depending on the run), which highlights that even the "answer key" is somewhat subjective — what counts as a meaningful interview question vs. a rhetorical question or conversational filler is a judgment call.
