# IMPROVEMENT-PLAN-0012: Advanced Evaluation & Testing Capabilities

**Status:** Completed
**Created:** 2026-02-02
**Completed:** 2026-02-02

## Summary

Extend the evaluation capabilities of Interview-assist-transcription-detection-console to enable comprehensive testing, regression detection, and iterative improvement of question/command detection accuracy.

## Motivation

Current evaluation (IMPROVEMENT-PLAN-0011) provides basic precision/recall/F1 metrics but has limitations:

- **Low precision (27%)** with 81 false positives out of 111 detections
- **Single ground truth source** - GPT-4o extraction may introduce bias
- **No strategy comparison** - Cannot easily compare Heuristic vs LLM vs Parallel
- **No regression detection** - Quality degradation not automatically caught
- **Limited error analysis** - False positive patterns not identified

## Goals

1. Identify and fix false positive patterns
2. Enable data-driven threshold tuning
3. Support A/B testing of detection strategies
4. Create reproducible benchmark suite
5. Prevent quality regression during development

## Implementation Phases

### Phase 1: Confusion Matrix & Error Analysis

Add detailed classification breakdown and false positive pattern analysis.

#### 1.1 ConfusionMatrix Class
**File:** `Interview-assist-library/Pipeline/Evaluation/ConfusionMatrix.cs`

```csharp
public class ConfusionMatrix
{
    // Track predictions vs actual for each intent type
    public Dictionary<(string Actual, string Predicted), int> Matrix { get; }

    public void Add(string actual, string predicted);
    public string ToFormattedString();
    public double GetAccuracy(string intentType);
}
```

#### 1.2 ErrorAnalyzer Class
**File:** `Interview-assist-library/Pipeline/Evaluation/ErrorAnalyzer.cs`

Analyzes false positive patterns:
- Trailing intonation patterns ("so that's how it works")
- "I think..." statement patterns
- Partial/incomplete questions
- Filler phrase patterns ("you know what I mean")
- Relative clause misdetections ("which gives you...")

#### 1.3 Update EvaluationResult
**File:** `Interview-assist-library/Pipeline/Evaluation/EvaluationModels.cs`

Add:
- `ConfusionMatrix` property
- `ErrorPatterns` list with frequency counts
- `SubtypeAccuracy` dictionary

### Phase 2: Per-Strategy Comparison Mode

#### 2.1 StrategyComparer Class
**File:** `Interview-assist-library/Pipeline/Evaluation/StrategyComparer.cs`

Runs all detection strategies on same input and compares results:
- Heuristic only
- LLM only
- Parallel mode

Returns comparative metrics table.

#### 2.2 Add --compare Flag
**File:** `Interview-assist-transcription-detection-console/Program.cs`

```bash
dotnet run -- --compare recordings/session.jsonl [--output comparison.json]
```

Output format:
```
Strategy      | Precision | Recall | F1    | Avg Latency
--------------|-----------|--------|-------|------------
Heuristic     | 45%       | 67%    | 54%   | 12ms
LLM           | 72%       | 89%    | 80%   | 450ms
Parallel      | 27%       | 70%    | 39%   | 455ms
```

### Phase 3: Threshold Tuning Mode

#### 3.1 ThresholdTuner Class
**File:** `Interview-assist-library/Pipeline/Evaluation/ThresholdTuner.cs`

Tests multiple confidence thresholds and finds optimal value:
- Sweeps from 0.3 to 0.95 in 0.05 increments
- Calculates metrics at each threshold
- Identifies optimal threshold for F1, precision, or recall

#### 3.2 Add --tune-threshold Flag
**File:** `Interview-assist-transcription-detection-console/Program.cs`

```bash
dotnet run -- --tune-threshold recordings/session.jsonl [--optimize f1|precision|recall]
```

Output:
```
Confidence Threshold Tuning
===========================
Threshold | Precision | Recall | F1     | Detections
----------|-----------|--------|--------|------------
0.40      | 27.0%     | 69.8%  | 38.9%  | 111
0.50      | 35.2%     | 65.1%  | 45.7%  | 89
0.60      | 48.5%     | 58.1%  | 52.9%  | 67
0.70      | 62.3%     | 51.2%  | 56.2%  | 52  ← Current
0.80      | 78.9%     | 38.5%  | 51.7%  | 34
0.90      | 88.2%     | 23.3%  | 36.9%  | 18

Optimal for F1: 0.60 (F1=52.9%)
Recommendation: Lower threshold from 0.70 to 0.60
```

### Phase 4: Curated Ground Truth Datasets

#### 4.1 Dataset Format
**Location:** `evaluations/datasets/`

JSONL format with hand-labeled examples:
```json
{"text": "What is dependency injection?", "type": "Question", "subtype": "Definition"}
{"text": "How do you implement the repository pattern?", "type": "Question", "subtype": "HowTo"}
{"text": "I think we should refactor this code.", "type": "Statement", "subtype": null}
{"text": "Can you show me the implementation?", "type": "Command", "subtype": "ShowCode"}
```

#### 4.2 DatasetLoader Class
**File:** `Interview-assist-library/Pipeline/Evaluation/DatasetLoader.cs`

Loads and validates curated datasets for evaluation.

#### 4.3 DatasetEvaluator Class
**File:** `Interview-assist-library/Pipeline/Evaluation/DatasetEvaluator.cs`

Evaluates detection against curated datasets:
- Runs each sample through detection
- Compares predicted vs labeled type/subtype
- Generates per-category accuracy metrics

#### 4.4 Add --dataset Flag
**File:** `Interview-assist-transcription-detection-console/Program.cs`

```bash
dotnet run -- --dataset evaluations/datasets/interview-questions.jsonl [--mode Heuristic]
```

### Phase 5: Regression Testing Mode

#### 5.1 Baseline Format
**File:** `evaluations/baselines/baseline-v1.json`

Stores expected metrics for regression comparison:
```json
{
  "version": "1.0",
  "createdAt": "2026-02-02T14:00:00Z",
  "metrics": {
    "precision": 0.65,
    "recall": 0.70,
    "f1": 0.67
  },
  "thresholds": {
    "precisionMin": 0.60,
    "recallMin": 0.65,
    "f1Min": 0.62
  }
}
```

#### 5.2 RegressionTester Class
**File:** `Interview-assist-library/Pipeline/Evaluation/RegressionTester.cs`

Compares current evaluation against baseline:
- Loads baseline metrics
- Runs evaluation on test data
- Flags regressions if metrics drop below thresholds
- Returns pass/fail with detailed diff

#### 5.3 Add --regression Flag
**File:** `Interview-assist-transcription-detection-console/Program.cs`

```bash
dotnet run -- --regression evaluations/baselines/baseline-v1.json --data recordings/session.jsonl
```

Output on regression:
```
REGRESSION DETECTED
===================
Metric     | Baseline | Current | Delta  | Status
-----------|----------|---------|--------|--------
Precision  | 65.0%    | 58.2%   | -6.8%  | FAIL (min: 60%)
Recall     | 70.0%    | 72.1%   | +2.1%  | PASS
F1         | 67.4%    | 64.3%   | -3.1%  | FAIL (min: 62%)

Exit code: 1 (regression detected)
```

#### 5.4 Add --create-baseline Flag

```bash
dotnet run -- --create-baseline evaluations/baselines/baseline-v2.json --data recordings/session.jsonl
```

### Phase 6: Intent Subtype Accuracy

#### 6.1 SubtypeEvaluator Class
**File:** `Interview-assist-library/Pipeline/Evaluation/SubtypeEvaluator.cs`

Measures accuracy of subtype classification:
- Definition, HowTo, Compare, YesNo, Opinion, etc.
- Per-subtype precision/recall

#### 6.2 Update Evaluation Output

Add subtype breakdown to evaluation reports:
```
Subtype Accuracy
================
Subtype     | Correct | Total | Accuracy
------------|---------|-------|----------
Definition  | 17      | 20    | 85.0%
HowTo       | 7       | 9     | 77.8%
Compare     | 3       | 5     | 60.0%
YesNo       | 11      | 12    | 91.7%
Opinion     | 4       | 7     | 57.1%
```

### Phase 7: Latency Benchmarking

#### 7.1 LatencyTracker Class
**File:** `Interview-assist-library/Pipeline/Evaluation/LatencyTracker.cs`

Records detection latency metrics:
- Time from utterance completion to detection event
- Per-strategy latency statistics (min, max, avg, p95)

#### 7.2 Update Recording Format

Add timing data to IntentEvent:
```json
{
  "type": "IntentEvent",
  "offsetMs": 1250,
  "data": {
    "intent": { ... },
    "latencyMs": 45,
    "strategy": "Heuristic"
  }
}
```

### Phase 8: Synthetic Test Generation

#### 8.1 SyntheticTestGenerator Class
**File:** `Interview-assist-library/Pipeline/Evaluation/SyntheticTestGenerator.cs`

Generates test variations:
```csharp
public IEnumerable<TestCase> GenerateVariations(string baseQuestion)
{
    yield return new("Direct", baseQuestion);
    yield return new("Indirect", $"I was wondering {Lower(baseQuestion)}");
    yield return new("Embedded", $"So basically, {baseQuestion}");
    yield return new("NoMark", baseQuestion.TrimEnd('?'));
    yield return new("Soft", $"Could you tell me {Lower(baseQuestion)}");
}
```

#### 8.2 Add --generate-tests Flag

```bash
dotnet run -- --generate-tests evaluations/datasets/seed-questions.txt --output evaluations/datasets/generated.jsonl
```

### Phase 9: Prompt A/B Testing

#### 9.1 PromptVariant Configuration

Add to appsettings.json:
```json
{
  "Evaluation": {
    "PromptVariants": [
      { "name": "default", "file": null },
      { "name": "strict", "file": "prompts/strict-detection.txt" },
      { "name": "contextual", "file": "prompts/contextual-detection.txt" }
    ]
  }
}
```

#### 9.2 PromptTester Class
**File:** `Interview-assist-library/Pipeline/Evaluation/PromptTester.cs`

Tests different LLM prompts and compares results.

#### 9.3 Add --test-prompts Flag

```bash
dotnet run -- --test-prompts recordings/session.jsonl
```

### Phase 10: Unified Evaluation Report

#### 10.1 ComprehensiveReport Class
**File:** `Interview-assist-library/Pipeline/Evaluation/ComprehensiveReport.cs`

Generates full HTML/JSON report combining all metrics:
- Summary metrics
- Confusion matrix visualization
- Error pattern analysis
- Strategy comparison
- Threshold analysis
- Subtype breakdown
- Latency statistics
- Regression status

## Files to Create

| File | Description |
|------|-------------|
| `Pipeline/Evaluation/ConfusionMatrix.cs` | Classification matrix |
| `Pipeline/Evaluation/ErrorAnalyzer.cs` | False positive pattern detection |
| `Pipeline/Evaluation/StrategyComparer.cs` | Multi-strategy comparison |
| `Pipeline/Evaluation/ThresholdTuner.cs` | Confidence threshold optimization |
| `Pipeline/Evaluation/DatasetLoader.cs` | Curated dataset loading |
| `Pipeline/Evaluation/DatasetEvaluator.cs` | Dataset-based evaluation |
| `Pipeline/Evaluation/RegressionTester.cs` | Baseline comparison |
| `Pipeline/Evaluation/SubtypeEvaluator.cs` | Subtype accuracy metrics |
| `Pipeline/Evaluation/LatencyTracker.cs` | Timing measurements |
| `Pipeline/Evaluation/SyntheticTestGenerator.cs` | Test case generation |
| `Pipeline/Evaluation/PromptTester.cs` | Prompt A/B testing |
| `Pipeline/Evaluation/ComprehensiveReport.cs` | Full report generation |
| `evaluations/datasets/questions.jsonl` | Curated question dataset |
| `evaluations/datasets/statements.jsonl` | Curated non-question dataset |
| `evaluations/datasets/edge-cases.jsonl` | Ambiguous cases dataset |
| `evaluations/baselines/baseline-v1.json` | Initial baseline metrics |

## Files to Modify

| File | Changes |
|------|---------|
| `Program.cs` | Add --compare, --tune-threshold, --dataset, --regression, --create-baseline, --generate-tests, --test-prompts, --analyze-errors flags |
| `EvaluationRunner.cs` | Integrate new evaluation components |
| `EvaluationModels.cs` | Add new result types |
| `appsettings.json` | Add new configuration sections |
| `RecordedEvent.cs` | Add latency tracking to IntentEvent |

## Configuration Updates

```json
{
  "Evaluation": {
    "Model": "gpt-4o",
    "MatchThreshold": 0.7,
    "DeduplicationThreshold": 0.8,
    "OutputFolder": "evaluations",
    "DatasetsFolder": "evaluations/datasets",
    "BaselinesFolder": "evaluations/baselines",
    "ThresholdRange": { "min": 0.3, "max": 0.95, "step": 0.05 },
    "PromptVariants": []
  }
}
```

## Command Reference

| Command | Description |
|---------|-------------|
| `--evaluate <file>` | Basic evaluation (existing) |
| `--compare <file>` | Compare all strategies |
| `--tune-threshold <file>` | Find optimal threshold |
| `--dataset <file>` | Evaluate against curated dataset |
| `--regression <baseline>` | Check for regressions |
| `--create-baseline <file>` | Create new baseline |
| `--generate-tests <seed>` | Generate synthetic tests |
| `--test-prompts <file>` | A/B test detection prompts |
| `--analyze-errors <report>` | Analyze false positive patterns |

## Success Criteria

1. **Precision improvement** from 27% to >60%
2. **Regression detection** catches quality drops automatically
3. **Strategy comparison** identifies best approach per use case
4. **Threshold tuning** provides data-driven configuration
5. **Curated datasets** enable reproducible benchmarks

## Execution Order

1. Phase 1: Confusion Matrix & Error Analysis (foundation for understanding issues)
2. Phase 3: Threshold Tuning (quick win for precision improvement)
3. Phase 2: Strategy Comparison (identify best baseline)
4. Phase 6: Subtype Accuracy (detailed classification metrics)
5. Phase 5: Regression Testing (protect quality going forward)
6. Phase 4: Curated Datasets (long-term benchmark foundation)
7. Phase 7: Latency Benchmarking (performance metrics)
8. Phase 8: Synthetic Generation (expand test coverage)
9. Phase 9: Prompt A/B Testing (optimize LLM detection)
10. Phase 10: Comprehensive Report (unified output)
