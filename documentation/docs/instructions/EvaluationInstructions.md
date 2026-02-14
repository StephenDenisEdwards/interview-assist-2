# Evaluation & Testing Instructions

This document describes all evaluation and testing capabilities available in the Interview-assist-transcription-detection-console application for measuring and improving question detection accuracy.

## Quick Reference

| Command | Description |
|---------|-------------|
| `--evaluate <session>` | Basic evaluation against LLM ground truth |
| `--compare <session>` | Compare all detection strategies |
| `--tune-threshold <session>` | Find optimal confidence threshold |
| `--regression <baseline> --data <session>` | Test for quality regressions |
| `--create-baseline <output> --data <session>` | Create baseline from session |
| `--dataset <file>` | Evaluate against curated dataset |
| `--generate-tests <seed>` | Generate synthetic test cases |
| `--analyze-errors <report>` | Analyze false positive patterns |

---

## Auto-Evaluation on Report Generation

When a report is generated via `--playback --headless` or `--analyze`, evaluation runs automatically against both LLM-extracted and human ground truth. This requires an OpenAI API key; if unavailable, auto-evaluation is skipped silently.

### What Happens

1. **LLM ground truth evaluation** — The session is evaluated against LLM-extracted ground truth (same as `--evaluate`). Results are saved to `evaluations/{session-id}.evaluation-llm.json`.

2. **Human ground truth seed** — If no human ground truth file exists at `evaluations/{session-id}.human-ground-truth.json`, one is seeded from the LLM evaluation's ground truth (no extra API call). This file is a JSON array of `ExtractedQuestion` objects and should be manually reviewed/curated. A placeholder `evaluation-human.json` is also created (to keep version numbers in sync) with `"status": "pending"`.

3. **Human ground truth evaluation** — On subsequent runs (when the seed file already exists), the session is evaluated against the human ground truth file. Results are saved to `evaluations/{session-id}.evaluation-human.json`.

### Output Files

```
evaluations/
├── session-2026-02-13-125207-58704.evaluation-llm.json      # LLM ground truth eval (versioned)
├── session-2026-02-13-125207-58704.human-ground-truth.json   # Human ground truth (seeded once, then curated)
└── session-2026-02-13-125207-58704.evaluation-human.json     # Human ground truth eval (versioned)
```

Evaluation files are versioned: if a file already exists, a `-v2`, `-v3`, etc. suffix is appended. The `human-ground-truth.json` file is created once and never overwritten — it is the curated input for human evaluation.

### Human Ground Truth Workflow

1. Run `--analyze` or `--playback --headless` — auto-seeds `human-ground-truth.json` and creates a placeholder `evaluation-human.json`
2. Open `evaluations/{session-id}.human-ground-truth.json` and review/edit the questions
3. Re-run `--analyze` or `--playback --headless` — the human evaluation will now run against your curated file
4. Compare `evaluation-llm.json` vs `evaluation-human.json` to assess ground truth quality

---

## Prerequisites

### API Keys

All evaluation modes require an OpenAI API key for ground truth extraction:

```bash
# Set via environment variable
export OPENAI_API_KEY=sk-...

# Or in appsettings.json
{
  "OpenAI": { "ApiKey": "sk-..." }
}
```

### Recording Sessions

Most evaluation commands require a recorded session file (`.jsonl`). Record sessions using the console app:

```bash
# Start the app (recording auto-starts if configured)
dotnet run --project Interview-assist-transcription-detection-console

# Or toggle recording with Ctrl+R
```

Sessions are saved to `recordings/session-{timestamp}.jsonl`. If `SaveAudio: true` is set in `Recording` config, a `.wav` file is also saved alongside each JSONL.

You can also re-transcribe a WAV recording through Deepgram (useful for testing different Deepgram settings or detection strategies):

```bash
dotnet run --project Interview-assist-transcription-detection-console -- --playback recordings/session.wav
```

WAV playback requires a Deepgram API key (the audio is sent to Deepgram for live transcription). JSONL playback does not.

---

## Basic Evaluation

Evaluate question detection accuracy against LLM-extracted ground truth.

### Command

```bash
dotnet run --project Interview-assist-transcription-detection-console -- \
  --evaluate recordings/session-2026-02-02-114135.jsonl \
  [--model gpt-4o] \
  [--output evaluations/report.json]
```

### Options

| Option | Description | Default |
|--------|-------------|---------|
| `--evaluate <file>` | Session file to evaluate | Required |
| `--model <model>` | Model for ground truth extraction | `gpt-4o` |
| `--output <file>` | Output file for detailed report | Auto-generated |

### Output

```
=== Metrics ===
Ground Truth:    43 questions
Detected:        111 unique questions

True Positives:  30
False Positives: 81
False Negatives: 13

Precision:       27.0%
Recall:          69.8%
F1 Score:        38.9%

=== Subtype Accuracy ===
Overall: 72.5%
Definition:  85.0%
HowTo:       77.8%
Compare:     60.0%

=== Error Pattern Analysis ===
Trailing intonation:     23 (28.4%)
I think statements:      18 (22.2%)
Relative clauses:        15 (18.5%)
...
```

### Understanding Metrics

| Metric | Meaning | Target |
|--------|---------|--------|
| **Precision** | % of detected questions that are correct | >60% |
| **Recall** | % of actual questions that were detected | >70% |
| **F1 Score** | Harmonic mean of precision/recall | >65% |

---

## Strategy Comparison

Compare Heuristic, LLM, and Parallel detection strategies on the same input.

### Command

```bash
dotnet run --project Interview-assist-transcription-detection-console -- \
  --compare recordings/session.jsonl \
  [--output evaluations/comparison.json]
```

### Output

```
Strategy    | Detected | TP  | FP  | FN  | Precision | Recall | F1     | Latency
------------|----------|-----|-----|-----|-----------|--------|--------|--------
Heuristic   |       89 |  28 |  61 |  15 |       31% |    65% |    42% |   12ms
LLM         |       52 |  35 |  17 |   8 |       67% |    81% |    74% |  450ms *
Parallel    |      111 |  30 |  81 |  13 |       27% |    70% |    39% |  455ms

* Best F1: LLM
```

### Strategy Characteristics

| Strategy | Speed | Cost | Accuracy | Best For |
|----------|-------|------|----------|----------|
| **Heuristic** | ~12ms | Free | ~42% F1 | Fast feedback, low cost |
| **LLM** | ~450ms | Per-call | ~74% F1 | Maximum accuracy |
| **Parallel** | ~455ms | Per-call | ~39% F1 | Best UX (immediate + corrections) |

---

## Threshold Tuning

Find the optimal confidence threshold for your use case.

### Command

```bash
dotnet run --project Interview-assist-transcription-detection-console -- \
  --tune-threshold recordings/session.jsonl \
  [--optimize f1|precision|recall]
```

### Options

| Option | Description | Default |
|--------|-------------|---------|
| `--tune-threshold <file>` | Session file to analyze | Required |
| `--optimize <target>` | Optimization target | `f1` |

### Output

```
Threshold | Detected | TP  | FP  | FN  | Precision | Recall | F1
----------|----------|-----|-----|-----|-----------|--------|------
0.30      |      145 |  38 |  107|   5 |       26% |    88% |   40%
0.40      |      111 |  30 |   81|  13 |       27% |    70% |   39%
0.50      |       89 |  28 |   61|  15 |       31% |    65% |   42%
0.60      |       67 |  26 |   41|  17 |       39% |    60% |   47% <-- Balanced
0.70      |       52 |  22 |   30|  21 |       42% |    51% |   46%
0.80      |       34 |  18 |   16|  25 |       53% |    42% |   47%
0.90      |       18 |  12 |    6|  31 |       67% |    28% |   39%

Optimal for F1: 0.60 (F1: 47%)
Recommendation: Lower threshold from 0.70 to 0.60
```

### Recommendations

- **Maximize F1**: Balance precision and recall
- **Maximize Precision**: Use higher threshold (fewer false alarms)
- **Maximize Recall**: Use lower threshold (catch more questions)

---

## Regression Testing

Detect quality degradation by comparing against a baseline.

### Creating a Baseline

```bash
dotnet run --project Interview-assist-transcription-detection-console -- \
  --create-baseline evaluations/baselines/baseline-v1.json \
  --data recordings/session.jsonl \
  [--version "1.0"]
```

This creates a baseline file with current metrics and minimum thresholds (5% below current).

### Running Regression Tests

```bash
dotnet run --project Interview-assist-transcription-detection-console -- \
  --regression evaluations/baselines/baseline-v1.json \
  --data recordings/session.jsonl
```

### Output

```
=== REGRESSION DETECTED ===

Metric     | Baseline | Current | Delta   | Min     | Status
-----------|----------|---------|---------|---------|--------
Precision  |    65.0% |   58.2% |  -6.8%  |   60.0% | FAIL
Recall     |    70.0% |   72.1% |  +2.1%  |   65.0% | PASS
F1         |    67.4% |   64.3% |  -3.1%  |   62.0% | FAIL

Exit code: 1 (regression detected)
```

### CI/CD Integration

```yaml
# Example GitHub Actions workflow
- name: Run regression test
  run: |
    dotnet run --project Interview-assist-transcription-detection-console -- \
      --regression evaluations/baselines/baseline-v1.json \
      --data evaluations/test-session.jsonl
```

Returns exit code 1 on regression, 0 on pass.

---

## Curated Dataset Evaluation

Evaluate against hand-labeled test cases for reproducible benchmarks.

### Dataset Format

Datasets are JSONL files with labeled examples:

```json
{"text": "What is dependency injection?", "type": "Question", "subtype": "Definition"}
{"text": "I think we should refactor this code.", "type": "Statement"}
{"text": "Show me the implementation.", "type": "Command"}
```

### Available Datasets

| File | Description | Items |
|------|-------------|-------|
| `evaluations/datasets/questions.jsonl` | Technical interview questions | 20 |
| `evaluations/datasets/statements.jsonl` | Non-questions (negatives) | 15 |
| `evaluations/datasets/edge-cases.jsonl` | Ambiguous/tricky cases | 15 |

### Command

```bash
dotnet run --project Interview-assist-transcription-detection-console -- \
  --dataset evaluations/datasets/questions.jsonl \
  [--mode Heuristic|Llm|Parallel]
```

### Output

```
=== Results ===
Type Accuracy:     85.0% (17/20)
Question F1:       88.9%
  Precision:       94.4%
  Recall:          85.0%
Subtype Accuracy:  70.0%

Confusion Matrix:
            | Predicted
Actual      | Question | Statement | Command
------------|----------|-----------|--------
Question    |       17 |         3 |       0
Statement   |        1 |        14 |       0
Command     |        0 |         0 |       5
```

### Creating Custom Datasets

1. Create a JSONL file with one item per line:

```json
{"text": "Your question or statement here", "type": "Question", "subtype": "Definition", "notes": "Optional notes"}
```

2. Valid types: `Question`, `Statement`, `Command`
3. Valid subtypes: `Definition`, `HowTo`, `Compare`, `Troubleshoot`, `Clarification`, `YesNo`, `Opinion`, `Rhetorical`

---

## Synthetic Test Generation

Generate test variations from seed questions.

### Seed File Format

Plain text file with one question per line:

```
What is dependency injection?
How do you implement the repository pattern?
Can you explain async/await?
```

### Command

```bash
dotnet run --project Interview-assist-transcription-detection-console -- \
  --generate-tests evaluations/datasets/seed-questions.txt \
  [--output evaluations/datasets/generated.jsonl]
```

### Generated Variations

| Variation | Example |
|-----------|---------|
| **Direct** | What is dependency injection? |
| **NoQuestionMark** | What is dependency injection |
| **Indirect** | I was wondering what is dependency injection? |
| **Embedded** | So basically, what is dependency injection? |
| **Softened** | Just curious, what is dependency injection? |
| **Imperative** | Explain dependency injection. |
| **Fragment** | What is dependency |
| **Statement** | I think dependency injection is interesting. |

### Output

```
=== Generate Synthetic Tests ===
Seed file: seed-questions.txt
Output: generated.jsonl

Generating test variations...
  Generated: 90 test cases
    Direct: 10
    Indirect: 30
    Embedded: 20
    NoQuestionMark: 10
    Fragment: 10
    Statement: 10
```

---

## Error Analysis

Analyze false positive patterns to identify systematic issues.

### Command

```bash
dotnet run --project Interview-assist-transcription-detection-console -- \
  --analyze-errors evaluations/session-evaluation.json
```

### Output

```
=== Error Pattern Analysis ===
Total False Positives: 81

Pattern Breakdown:
  Trailing intonation          23 (28.4%)
    Statements ending with filler words that sound like trailing questions
    Examples: "so that's how it works"

  I think statements           18 (22.2%)
    Opinion statements starting with 'I think...'
    Examples: "I think we should refactor this"

  Relative clauses             15 (18.5%)
    Fragments starting with relative pronouns
    Examples: "which gives you a great indication"

  Filler phrases               12 (14.8%)
    Utterances dominated by filler phrases

  Incomplete fragments          8 (9.9%)
    Very short fragments (1-4 words) without punctuation

Unclassified: 5 false positives

Confidence Distribution:
Bucket     | FP  | TP  | Precision
-----------|-----|-----|----------
0.4-0.5    |  45 |   5 |       10%
0.5-0.6    |  20 |  10 |       33%
0.6-0.7    |  10 |  10 |       50%
0.7-0.8    |   5 |   5 |       50%
0.8-0.9    |   1 |   0 |        0%
```

### Known Error Patterns

| Pattern | Description | Fix |
|---------|-------------|-----|
| Trailing intonation | "so that's how it works" | Add sentence completion check |
| I think statements | "I think we should..." | Filter opinion prefixes |
| Relative clauses | "which gives you..." | Check for main clause |
| Filler phrases | "you know what I mean" | Filter known fillers |
| Incomplete fragments | "What about the" | Minimum word count |

---

## Configuration Reference

### appsettings.json Evaluation Section

```json
{
  "Evaluation": {
    "Model": "gpt-4o",
    "MatchThreshold": 0.7,
    "DeduplicationThreshold": 0.8,
    "OutputFolder": "evaluations",
    "DatasetsFolder": "evaluations/datasets",
    "BaselinesFolder": "evaluations/baselines",
    "ThresholdRange": {
      "Min": 0.3,
      "Max": 0.95,
      "Step": 0.05
    }
  }
}
```

### Settings Reference

| Setting | Description | Default |
|---------|-------------|---------|
| `Model` | LLM for ground truth extraction | `gpt-4o` |
| `MatchThreshold` | Similarity for question matching | `0.7` |
| `DeduplicationThreshold` | Similarity for deduplication | `0.8` |
| `OutputFolder` | Default output directory | `evaluations` |
| `DatasetsFolder` | Curated datasets location | `evaluations/datasets` |
| `BaselinesFolder` | Baselines location | `evaluations/baselines` |

### Matching Algorithm

The evaluator uses a dual-strategy matching algorithm to compare detected questions against ground truth (`DetectionEvaluator.CalculateMatchSimilarity`):

1. **Levenshtein similarity** — Normalized edit distance, effective for minor text differences (casing, punctuation, minor wording).

2. **Word containment similarity** — Measures what fraction of the shorter text's content words appear in the longer text, after stripping pronouns. This handles **pronoun resolution**, where the detector makes questions self-contained by expanding pronouns.

The evaluator takes the **maximum** of both scores and compares against `MatchThreshold`.

**Example:** The ground truth extractor produces the raw transcript form `"How do you read values from it?"` while the detector resolves the pronoun to produce `"How do you read values from the appsettings.json file?"`. Levenshtein similarity is ~0.57 (below threshold), but word containment is ~0.86 (above threshold) because all non-pronoun words from the shorter text appear in the longer text.

Pronouns stripped during word containment: `it`, `its`, `they`, `them`, `their`, `theirs`, `this`, `that`, `these`, `those`, `he`, `she`, `him`, `her`, `his`, `hers`.

---

## Workflow Examples

### Initial Baseline Setup

```bash
# 1. Record a representative session
dotnet run --project Interview-assist-transcription-detection-console
# Press Ctrl+R to start recording, conduct test interview, Ctrl+Q to quit

# 2. Run initial evaluation
dotnet run -- --evaluate recordings/session.jsonl --output evaluations/initial-eval.json

# 3. Analyze errors
dotnet run -- --analyze-errors evaluations/initial-eval.json

# 4. Tune threshold
dotnet run -- --tune-threshold recordings/session.jsonl

# 5. Create baseline with optimal settings
dotnet run -- --create-baseline evaluations/baselines/v1.json --data recordings/session.jsonl
```

### Iterative Improvement Workflow

```bash
# 1. Make detection changes (code modifications)

# 2. Run regression test
dotnet run -- --regression evaluations/baselines/v1.json --data recordings/session.jsonl

# 3. If improved, update baseline
dotnet run -- --create-baseline evaluations/baselines/v2.json --data recordings/session.jsonl --version "2.0"

# 4. Compare strategies
dotnet run -- --compare recordings/session.jsonl
```

### Creating a Test Suite

```bash
# 1. Create seed questions file
echo "What is dependency injection?
How do you handle errors?
Can you explain SOLID principles?" > evaluations/datasets/seeds.txt

# 2. Generate synthetic tests
dotnet run -- --generate-tests evaluations/datasets/seeds.txt --output evaluations/datasets/generated.jsonl

# 3. Evaluate against generated tests
dotnet run -- --dataset evaluations/datasets/generated.jsonl
```

---

## Troubleshooting

### Common Issues

**"OpenAI API key required"**
- Set `OPENAI_API_KEY` environment variable
- Or add to appsettings.json under `Evaluation.ApiKey`

**"No events found in session file"**
- Verify the session file exists and is not empty
- Check file format is valid JSONL

**Low precision (many false positives)**
- Increase confidence threshold
- Run error analysis to identify patterns
- Consider using LLM mode instead of Heuristic

**Low recall (missing questions)**
- Decrease confidence threshold
- Check for question types not being detected
- Review missed questions in evaluation report

**Regression test fails unexpectedly**
- Compare baseline and current results
- Check if test data matches baseline conditions
- Consider updating baseline if intentional changes

---

## API Reference

### EvaluationResult

```csharp
public sealed record EvaluationResult
{
    public int TruePositives { get; init; }
    public int FalsePositives { get; init; }
    public int FalseNegatives { get; init; }
    public double Precision { get; }      // TP / (TP + FP)
    public double Recall { get; }         // TP / (TP + FN)
    public double F1Score { get; }        // 2 * P * R / (P + R)
    public IReadOnlyList<MatchedQuestion> Matches { get; init; }
    public IReadOnlyList<ExtractedQuestion> Missed { get; init; }
    public IReadOnlyList<DetectedQuestionInfo> FalseAlarms { get; init; }
}
```

### Programmatic Usage

```csharp
// Load session
var events = await SessionLoader.LoadAsync("session.jsonl");

// Extract transcript and detections
var transcript = TranscriptExtractor.ExtractFullTranscript(events);
var detected = TranscriptExtractor.ExtractDetectedQuestions(events);

// Get ground truth
using var extractor = new GroundTruthExtractor(apiKey, "gpt-4o");
var groundTruth = await extractor.ExtractQuestionsAsync(transcript);

// Evaluate
var evaluator = new DetectionEvaluator(matchThreshold: 0.7);
var result = evaluator.Evaluate(groundTruth, detected);

// Analyze errors
var errorAnalyzer = new ErrorAnalyzer();
var errors = errorAnalyzer.Analyze(result.FalseAlarms);
```
