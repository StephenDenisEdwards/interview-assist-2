# Headless WAV Playback Report

**Date:** 2026-02-10
**Source:** `recordings/session-2026-02-09-155932.wav`
**Output JSONL:** `recordings/session-2026-02-10-164246.jsonl`
**Log:** `logs/transcription-detection-20260210-164246.log`
**Mode:** LLM (gpt-4o-mini via OpenAI)
**Deepgram model:** nova-2

---

## 1. Session Overview

| Metric | Value |
|---|---|
| Wall-clock duration | 6 min 59s (419.2s) |
| Audio playback duration | ~6 min 51s |
| Post-playback LLM drain | ~8s |
| Total JSONL events | 1,576 |
| Deepgram ASR finals | 99 |
| Utterance finals | 328 |
| Intent candidates | 14 |
| Intent finals | 4 |
| Action events | 4 |
| Errors/warnings | 0 |

### Session Config
```json
{
  "deepgramModel": "nova-2",
  "diarize": false,
  "intentDetectionEnabled": true,
  "intentDetectionMode": "LLM",
  "audioSource": "WAV Playback",
  "sampleRate": 16000
}
```

### Content Summary

Single-speaker British comedy/political commentary covering:
1. Billie Eilish and Grammy celebrities condemning ICE
2. A lawyer offering to evict Billie Eilish from her mansion (Tongva tribal land)
3. Native American and Aboriginal perspectives on immigration
4. Peter Dinklage reading an Amanda Gorman poem at the Grammys
5. Peter Dinklage's criticism of Snow White dwarf depictions
6. Sign-off with Patreon/UK tour promotion

---

## 2. Event Distribution

| Event Type | Count | % |
|---|---|---|
| UtteranceEvent (Open/Update/Final) | 1,102 | 69.9% |
| AsrEvent | 446 | 28.3% |
| IntentEvent | 23 | 1.5% |
| ActionEvent | 4 | 0.3% |
| SessionMetadata | 1 | 0.1% |

### Utterance Close Reasons

| Reason | Count | % |
|---|---|---|
| SilenceGap | 251 | 76.5% |
| TerminalPunctuation | 63 | 19.2% |
| DeepgramSignal | 14 | 4.3% |

Utterance rate is ~0.8/sec (1 every ~1.25s), reflecting aggressive incremental segmentation typical of continuous monologue speech.

---

## 3. Deepgram Transcription

- **Connection latency:** 499ms
- **Connection stability:** No disconnections, errors, or warnings
- **Speech started events:** 59
- **Speech final (endpointing):** 13
- **Utterance end signals:** 12

### Notable Audio Gaps

| Timestamp | Duration | Context |
|---|---|---|
| 16:43:08-12 | 4.3s | Natural pause between opening and continuation |
| 16:49:23-35 | 11.7s | End of spoken content before trailing noise |

---

## 4. Intent Detection Results

### Final Intents (Questions)

#### #1 - "Is Billie Eilish about to lose her house?"
| Field | Value |
|---|---|
| Utterance | utt_0003 |
| Confidence | 1.00 |
| Latency | 1,905ms |
| Source | "Is Billie Eilish about to lose her house?" |
| Original | "Is Billie Eilish about to lose her house?" |
| Match | **YES** |

The title question of the video. Detected quickly with perfect confidence and exact text match.

#### #2 - "If someone entered the Grammys illegally..."
| Field | Value |
|---|---|
| Utterance | utt_0026 |
| Subtype | Troubleshoot |
| Confidence | 0.90 |
| Latency | 7,636ms |
| Source | "If someone entered the Grammys illegally, do you think they would be waived then and offered a seat at the table and a glass of champagne?" |
| Original | "you entered the Grammys illegally, do you think you'd be waived" |
| Match | **NO** |

The LLM rewrote from second-person to conditional third-person. High latency — waited for significant context before promoting. Rhetorical question correctly identified.

#### #3 - "Where is Billie Eilish's mansion located?"
| Field | Value |
|---|---|
| Utterance | utt_0100 |
| Confidence | 0.90 |
| Latency | 12,596ms |
| Source | "Where is Billie Eilish's mansion located?" |
| Original | "Where Billy" |
| Match | **NO** |

Highest latency in the session. The LLM synthesized a complete question from a 2-word ASR fragment. This is a **borderline false positive** — the speaker was providing information about the mansion's location, not explicitly asking a question. The LLM inferred an implicit question from context.

#### #4 - "Did anybody ask the Native Americans..."
| Field | Value |
|---|---|
| Utterance | utt_0148 |
| Confidence | 0.90 |
| Latency | 1,709ms |
| Source | "Did anybody ask the Native Americans if they want more people to come over the border and occupy their land?" |
| Original | "the Native Americans if they want more people to come over the border and occupy their land?" |
| Match | **NO** |

Fast detection. The LLM prepended "Did anybody ask" to complete the fragment. Genuine rhetorical question correctly identified.

### Latency Statistics

| Stat | Value |
|---|---|
| Min | 1,709ms |
| Median | 4,771ms |
| Mean | 5,962ms |
| Max | 12,596ms |

### originalText Accuracy

**1/4 (25.0%) exact match**

Only intent #1 achieved exact match. Intents #2-#4 had partial original text due to utterance fragmentation — the `originalText` field captured only the utterance fragment visible at detection time, not the full question span.

---

## 5. Intent Candidates (Not Promoted)

14 candidates were emitted across 9 distinct utterance IDs. Notable patterns:

| Utterance | Confidence | Content | Why Not Promoted |
|---|---|---|---|
| utt_0063-0064 | 0.50 | "houses. They travel around in SUVs..." | Descriptive text about celebrities, not a question |
| utt_0226 | 0.40 | "when the labor and bitter anger of our neighbors..." | Poetry excerpt (Amanda Gorman poem) |
| utt_0244, utt_0247 | 0.40 | "when the blank night has so long stood." | Poetry excerpt |
| utt_0273 | 0.40 | "which bombed, when they're planning the remake..." | Narrative clause, not a question |

**Duplicate candidates:** 5 of 14 candidates were duplicates (same utterance, fired on successive stabilizer updates ~40-800ms apart).

---

## 6. False Positives (from JSONL analysis)

The JSONL file reveals additional intents not visible in the headless console summary (which only prints questions). These were detected as imperatives:

| Intent | Type | Confidence | Source Text | Issue |
|---|---|---|---|---|
| "Take a look at Billie Eilish" | Imperative/Continue | 1.00 | "Take a look at Billie Eilish." | Narration, not a command |
| "Hammer down the doors of the Grammys" | Imperative/Continue | 1.00 | "Hammer down the doors of the Grammys and see how people like it there." | Rhetoric/hyperbole |
| "Evict Billie Eilish from her home" | Imperative | 0.80 | "Evict Billie Eilish from her home following her Grammy stunt." | Quoting a news article |
| "Stop the mass immigration" | Imperative/Stop | 0.90 | "Stop the mass immigration." | Quoting Aboriginal viewpoint |
| "We don't want more people coming onto our land" | Imperative/Stop | 0.90 | "We don't want more people coming onto our land." | Quoting Aboriginal viewpoint |

All 5 are **false positives** where the LLM interpreted reported speech, quotes, or rhetoric as direct commands. The two Stop imperatives fired simultaneously at the same timestamp, triggering two `stop` action events.

---

## 7. Observations and Recommendations

### What Worked Well
- **Clean session:** Zero errors, warnings, or disconnections across the full 7-minute WAV playback
- **Question detection accuracy:** 3 of 4 question finals were genuine rhetorical questions from the content (#1, #2, #4)
- **Text reconstruction:** The LLM successfully reconstructed complete questions from partial ASR fragments (e.g., "Where Billy" -> "Where is Billie Eilish's mansion located?")
- **Fast detection:** Two of four question intents were detected in under 2 seconds

### Areas for Improvement

1. **Reported speech false positives:** The LLM struggles to distinguish direct commands from reported/quoted speech. Phrases like "Take a look at" (narration) and "Stop the mass immigration" (quoting someone) were classified as imperatives with high confidence. A context-awareness improvement (e.g., detecting "he said", "they say", quotation patterns) could help.

2. **Duplicate candidate emissions:** 36% of candidates (5/14) were duplicates for the same utterance. The heuristic candidate detector could debounce by checking if the utterance ID already had a recent candidate.

3. **originalText fragmentation:** Only 25% exact match rate. The `originalText` field captures only the utterance fragment at detection time, not the full question span. For long questions that span multiple ASR segments, this results in truncated originals.

4. **Simultaneous action firing:** Two `stop` actions fired at the same millisecond from different utterances. The action router should debounce same-type actions within a short window.

5. **Poetry/literary text noise:** Low-confidence candidates from the Amanda Gorman poem section suggest the LLM partially triggers on poetic constructions. None promoted to finals, so this is low severity.

6. **Implicit question synthesis (#3):** The LLM generated "Where is Billie Eilish's mansion located?" from narrative context where no question was actually asked. While creative, this is a false positive — the speaker was stating facts, not asking.

---

## 8. Raw Statistics

### Event Timeline (JSONL offsets)
- First event: 0ms
- Last event: 411,201ms
- Duration: 411.2 seconds

### Utterance Duration Statistics
| Stat | Value |
|---|---|
| Min | 0ms |
| Max | 2,259ms |
| Mean | 785ms |
| Median | 796ms |

### Intent Event Counts
| Category | Count |
|---|---|
| Intent candidates (Question) | 14 |
| Intent finals (Question) | 4 |
| Intent finals (Imperative) | 5 |
| Intent finals (total) | 9 |
| Action events (continue) | 2 |
| Action events (stop) | 2 |
