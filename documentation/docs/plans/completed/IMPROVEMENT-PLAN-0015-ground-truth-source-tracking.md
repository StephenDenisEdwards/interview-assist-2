# IMPROVEMENT-PLAN-0015: Ground Truth Source Tracking & Human Ground Truth File Support

**Status:** Completed
**Created:** 2026-02-12
**Completed:** 2026-02-12

## Summary

Add source tracking to ground truth data in evaluation reports and JSON output, and support loading human-labeled ground truth from a file instead of always extracting via LLM.

## Motivation

- Evaluation reports and JSON output did not indicate how the ground truth was generated (e.g., which LLM model, or whether it was human-labeled).
- Ground truth was always extracted via LLM — there was no way to supply a human-labeled file.
- Human-labeled ground truth is needed for reliable evaluation baselines that are not subject to LLM variability.
- Users who curate ground truth from previous LLM evaluations (editing the `GroundTruth` array in the output JSON) had no way to feed those corrections back in.

## Goals

1. Track and display the source of ground truth (LLM model name or human-labeled) in both console output and JSON reports.
2. Support a `--ground-truth <file>` CLI argument to bypass LLM extraction and load a human-labeled JSON file.
3. Maintain backward compatibility — existing evaluation workflows continue to work unchanged.

## Implementation

### 1. New `GroundTruthSource` Record

**File:** `Interview-assist-library/Pipeline/Evaluation/EvaluationModels.cs`

```csharp
public sealed record GroundTruthSource(string Method, string? Model);
```

- `Method`: `"LLM"` or `"HumanLabeled"`
- `Model`: e.g. `"gpt-4o"` for LLM extraction, `null` for human-labeled

### 2. Updated `GroundTruthResult`

**File:** `Interview-assist-library/Pipeline/Evaluation/EvaluationModels.cs`

Added third positional parameter `Source`:

```csharp
public sealed record GroundTruthResult(
    IReadOnlyList<ExtractedQuestion> Questions,
    string RawLlmResponse,
    GroundTruthSource Source);
```

### 3. `GroundTruthFile` Option

**File:** `Interview-assist-library/Pipeline/Evaluation/EvaluationModels.cs`

Added to `EvaluationOptions`:

```csharp
public string? GroundTruthFile { get; init; }
```

### 4. `GroundTruthExtractor` Source Population

**File:** `Interview-assist-library/Pipeline/Evaluation/GroundTruthExtractor.cs`

All `GroundTruthResult` construction sites now pass `new GroundTruthSource("LLM", _model)`.

### 5. CLI Argument

**File:** `Interview-assist-transcription-detection-console/Program.cs`

New `--ground-truth <file>` argument parsed and passed to `EvaluationOptions.GroundTruthFile`. Help text updated.

### 6. `EvaluationRunner` Branching

**File:** `Interview-assist-transcription-detection-console/EvaluationRunner.cs`

- `RunAsync` branches on `_options.GroundTruthFile`:
  - If set: loads human-labeled JSON via `LoadGroundTruthFileAsync`, no LLM API call
  - If not set: existing LLM extraction path (unchanged)
- New `LoadGroundTruthFileAsync` method reads a JSON array of `ExtractedQuestion` objects
- `PrintResults` displays source line: `Ground Truth Source: HumanLabeled` or `Ground Truth Source: LLM (gpt-4o)`
- `SaveResultsAsync` includes `GroundTruthSource` object with `Method` and `Model` in JSON output

## Human Ground Truth File Format

JSON array of objects matching the `GroundTruth` array shape in evaluation output:

```json
[
  { "Text": "Is Billie Eilish about to lose her house?", "Subtype": "Clarification", "Confidence": 1.0, "ApproximatePosition": 0 },
  { "Text": "Another question?", "Subtype": null, "Confidence": 1.0, "ApproximatePosition": 0 }
]
```

Users can extract and edit the `GroundTruth` array from a previous LLM-generated evaluation JSON to create a human-labeled ground truth file.

## Files Modified

| File | Changes |
|------|---------|
| `Interview-assist-library/Pipeline/Evaluation/EvaluationModels.cs` | Added `GroundTruthSource` record, updated `GroundTruthResult`, added `GroundTruthFile` to `EvaluationOptions` |
| `Interview-assist-library/Pipeline/Evaluation/GroundTruthExtractor.cs` | All `GroundTruthResult` constructions now include `GroundTruthSource` |
| `Interview-assist-transcription-detection-console/Program.cs` | Added `--ground-truth` CLI arg, updated help text, passed to options |
| `Interview-assist-transcription-detection-console/EvaluationRunner.cs` | Added file/LLM branching, `LoadGroundTruthFileAsync`, source in console + JSON output |

## Usage

```bash
# LLM-extracted ground truth (existing behavior, now with source tracking)
dotnet run --project Interview-assist-transcription-detection-console -- \
  --evaluate recordings/session.jsonl --output evaluations/report.json

# Human-labeled ground truth (skips LLM, no API key needed for ground truth)
dotnet run --project Interview-assist-transcription-detection-console -- \
  --evaluate recordings/session.jsonl --ground-truth evaluations/human-gt.json --output evaluations/report.json
```

## Verification

1. `dotnet build interview-assist-2.sln` — 0 errors, 0 warnings
2. `dotnet test Interview-assist-library-unit-tests` — no regressions (same 6 pre-existing DeepgramIntentDetector failures)
3. LLM mode: JSON output contains `"GroundTruthSource": { "Method": "LLM", "Model": "gpt-4o" }`
4. Human file mode: JSON output contains `"GroundTruthSource": { "Method": "HumanLabeled", "Model": null }`, no LLM API call made
