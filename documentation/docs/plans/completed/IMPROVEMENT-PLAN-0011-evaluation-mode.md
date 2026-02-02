# IMPROVEMENT-PLAN-0011: Post-Processing Evaluation Mode

**Status:** Completed
**Completed:** 2026-02-02

## Summary

Add an evaluation mode to the Interview-assist-transcription-detection-console app that compares real-time question detection against LLM-extracted ground truth from the complete transcript.

## Motivation

- Need to measure accuracy of real-time question detection
- Current recordings contain all data needed for evaluation
- Automated evaluation enables iterative improvement of detection algorithms

## Design Decisions

1. **Matching algorithm**: Fuzzy string matching (Levenshtein distance) - fast, no API calls
2. **Deduplication**: Unique questions only - deduplicate by text similarity before comparison
3. **Ground truth model**: GPT-4o - most capable for accurate question extraction

## Implementation

### Phase 1: Core Library Classes

#### 1.1 TranscriptExtractor
**File:** `Interview-assist-library/Pipeline/Recording/TranscriptExtractor.cs`

Extracts full transcript from JSONL by filtering `UtteranceEvent` where `eventType == "Final"` and concatenating `stableText` values. Also provides:
- `ExtractSegments()` - Returns transcript as individual segments with metadata
- `ExtractDetectedQuestions()` - Extracts all Question-type IntentEvents
- `DeduplicateQuestions()` - Removes duplicate detections using Levenshtein distance
- `CalculateSimilarity()` - Normalized Levenshtein distance for fuzzy matching

#### 1.2 GroundTruthExtractor
**File:** `Interview-assist-library/Pipeline/Evaluation/GroundTruthExtractor.cs`

Sends full transcript to GPT-4o to extract all questions with their approximate positions. Uses JSON response format with low temperature (0.1) for consistency.

#### 1.3 DetectionEvaluator
**File:** `Interview-assist-library/Pipeline/Evaluation/DetectionEvaluator.cs`

Compares LLM-extracted questions with detected questions using fuzzy matching. Calculates precision, recall, and F1 score. Returns detailed breakdown of matches, missed questions, and false alarms.

#### 1.4 EvaluationModels
**File:** `Interview-assist-library/Pipeline/Evaluation/EvaluationModels.cs`

Data types:
- `ExtractedQuestion` - Ground truth question from LLM
- `EvaluationResult` - Metrics and detailed breakdown
- `MatchedQuestion` - Paired ground truth and detected question
- `DetectedQuestionInfo` - Simplified detected question for reporting
- `EvaluationOptions` - Configuration settings

### Phase 2: Console Integration

#### 2.1 Add --evaluate mode
**File:** `Interview-assist-transcription-detection-console/Program.cs`

```
dotnet run -- --evaluate <session.jsonl> [--model gpt-4o] [--output report.json]
```

Added argument parsing for `--evaluate`, `--model`, and `--output` flags.

#### 2.2 EvaluationRunner
**File:** `Interview-assist-transcription-detection-console/EvaluationRunner.cs`

Orchestrates evaluation workflow:
1. Load JSONL session file
2. Extract full transcript using TranscriptExtractor
3. Extract and deduplicate detected questions
4. Call GroundTruthExtractor for LLM-extracted questions
5. Run DetectionEvaluator comparison
6. Output results to console and optional JSON file

## Files Created

| File | Description |
|------|-------------|
| `Interview-assist-library/Pipeline/Recording/TranscriptExtractor.cs` | Extract transcript from JSONL |
| `Interview-assist-library/Pipeline/Evaluation/GroundTruthExtractor.cs` | LLM-based question extraction |
| `Interview-assist-library/Pipeline/Evaluation/DetectionEvaluator.cs` | Comparison and metrics |
| `Interview-assist-library/Pipeline/Evaluation/EvaluationModels.cs` | Result data types |
| `Interview-assist-transcription-detection-console/EvaluationRunner.cs` | Evaluation orchestration |

## Files Modified

| File | Change |
|------|--------|
| `Interview-assist-transcription-detection-console/Program.cs` | Add --evaluate, --model, --output argument handling |
| `Interview-assist-transcription-detection-console/appsettings.json` | Add Evaluation config section |

## Usage

```bash
# Basic evaluation
dotnet run --project Interview-assist-transcription-detection-console -- --evaluate recordings/session-2026-02-02-114135.jsonl

# With custom model and output
dotnet run --project Interview-assist-transcription-detection-console -- --evaluate recordings/session.jsonl --model gpt-4o-mini --output results.json
```

## Configuration

Added to `appsettings.json`:
```json
{
  "Evaluation": {
    "Model": "gpt-4o",
    "MatchThreshold": 0.7,
    "DeduplicationThreshold": 0.8,
    "OutputFolder": "evaluations"
  }
}
```

## Verification Results

Tested with `session-2026-02-02-114135.jsonl`:

```
=== Question Detection Evaluation ===
Session: session-2026-02-02-114135.jsonl

Transcript: 86,950 characters, 1,458 utterances
Ground Truth (LLM): 41 questions
Detected: 111 unique questions (after deduplication)

=== Metrics ===
True Positives:  29
False Positives: 82
False Negatives: 12

Precision:       26.1%
Recall:          70.7%
F1 Score:        38.2%
```

### Key Findings

1. **Low precision (26.1%)** - Too many false positives from:
   - Incomplete sentence fragments ("Which gives you a great indication of his...")
   - Relative clauses starting with "which/when/where"
   - Low confidence detections (0.4) that aren't actual questions

2. **Decent recall (70.7%)** - The system catches most actual questions

3. **Technical questions matched well** - Questions like "What is the purpose of Span of Tea?" and "How do you create a custom attribute in C Sharp?" matched at 100%

### Recommendations for Detection Improvement

Based on evaluation results:
- Increase minimum confidence threshold (currently 0.4 produces many false positives)
- Add fragment filtering (reject detections without terminal punctuation)
- Improve handling of "which/when/where" relative clauses
- Consider utterance duration (very short fragments are rarely complete questions)
