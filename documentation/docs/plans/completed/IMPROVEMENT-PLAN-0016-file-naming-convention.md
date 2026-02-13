# IMPROVEMENT-PLAN-0015: Unified File Naming Convention

**Status:** Completed
**Created:** 2026-02-11

## Problem

The application generates multiple output files per session (recordings, logs, evaluations, reports) that are currently scattered across separate directories with inconsistent naming conventions. If files were consolidated or viewed together, related files would not sort adjacently.

### Current Naming Patterns

| File Type | Current Pattern | Folder | Example |
|-----------|----------------|--------|---------|
| Recording (JSONL) | `session-{yyyy-MM-dd-HHmmss}.jsonl` | `recordings/` | `session-2026-02-10-171013.jsonl` |
| Audio (WAV) | `session-{yyyy-MM-dd-HHmmss}.wav` | `recordings/` | `session-2026-02-10-171013.wav` |
| Log | `transcription-detection-{yyyyMMdd-HHmmss}.log` | `logs/` | `transcription-detection-20260210-171013.log` |
| Evaluation | `{baseName}-evaluation.json` | `evaluations/` | `session-2026-02-10-171013-evaluation.json` |
| Comparison | User-specified or `compare-{HHmmss}.json` | `evaluations/` | `compare-171013.json` |
| Report | `{baseName}.report.md` | `reports/` | `session-2026-02-10-171013.report.md` |
| Baseline | User-specified | User-specified | `baseline.json` |

### Issues

1. **Inconsistent timestamp formats** — JSONL uses `yyyy-MM-dd-HHmmss` (dashes in date), logs use `yyyyMMdd-HHmmss` (no dashes). This breaks the `SessionReportGenerator.ResolveLogFile()` mapping which must regex-transform between formats.

2. **Inconsistent prefixes** — Recordings use `session-`, logs use `transcription-detection-`. Related files from the same session do not share a common prefix and will not sort together alphabetically.

3. **Inconsistent suffix conventions** — Evaluations use `-evaluation.json` (hyphenated suffix), reports use `.report.md` (dotted suffix). No clear pattern for secondary file types.

4. **Compare files lose session identity** — Historical compare files like `compare-114135.json` contain only the time portion, losing the date and the `session-` prefix entirely.

5. **No convention for baselines** — Baselines are user-specified with no guidance, making them impossible to correlate with their source session.

## Proposed Convention

### Session ID

All files derived from a single session share a **session ID** that forms the sort key:

```
session-YYYY-MM-DD-HHmmss
```

This is the existing recording timestamp and does not change.

### Naming Pattern

```
session-YYYY-MM-DD-HHmmss.{purpose}.{ext}
```

Where `{purpose}` is a short label identifying the file type, and `{ext}` is the format extension.

### Proposed File Names

| File Type | Proposed Pattern | Example |
|-----------|-----------------|---------|
| Recording | `session-2026-02-10-171013.recording.jsonl` | Primary session data |
| Audio | `session-2026-02-10-171013.audio.wav` | Raw audio capture |
| Log | `session-2026-02-10-171013.log` | Application log |
| Report | `session-2026-02-10-171013.report.md` | Session analysis report |
| Evaluation | `session-2026-02-10-171013.evaluation.json` | Detection accuracy evaluation |
| Comparison | `session-2026-02-10-171013.comparison.json` | Multi-strategy comparison |
| Baseline | `session-2026-02-10-171013.baseline.json` | Regression testing baseline |
| Baseline (versioned) | `session-2026-02-10-171013.baseline-v2.0.json` | Versioned baseline |

### Sorting Behaviour

With this convention, all files from the same session sort together in any directory listing:

```
session-2026-02-10-171013.audio.wav
session-2026-02-10-171013.baseline.json
session-2026-02-10-171013.comparison.json
session-2026-02-10-171013.evaluation.json
session-2026-02-10-171013.log
session-2026-02-10-171013.recording.jsonl
session-2026-02-10-171013.report.md

session-2026-02-11-093045.audio.wav
session-2026-02-11-093045.evaluation.json
session-2026-02-11-093045.log
session-2026-02-11-093045.recording.jsonl
session-2026-02-11-093045.report.md
```

Sessions sort chronologically, and within each session, file types sort alphabetically by purpose.

### Evaluation Datasets (No Change)

Static evaluation datasets are not session-derived and keep their current location and naming:

```
evaluations/datasets/questions.jsonl
evaluations/datasets/edge-cases.jsonl
evaluations/datasets/statements.jsonl
```

### Headless Playback Outputs

When headless mode replays an existing session, the derived files use the **source session's ID**, not a new timestamp:

```
# Input
session-2026-02-10-171013.recording.jsonl

# Headless outputs (same session ID, new purpose labels)
session-2026-02-10-171013.log            # new log from this run
session-2026-02-10-171013.report.md      # generated report
```

If headless mode re-transcribes a WAV and produces a new JSONL, the new JSONL uses a fresh session ID (since it represents a new session).

## Implementation Steps

### Step 1: Update RecordingOptions default pattern

Change `FileNamePattern` from `"session-{timestamp}.jsonl"` to `"session-{timestamp}.recording.jsonl"`.

**Files:** `Interview-assist-library/Pipeline/Recording/RecordingOptions.cs`

### Step 2: Update WAV file derivation

Change `Path.ChangeExtension(filePath, ".wav")` to derive `.audio.wav` from the session base name.

**Files:** `Interview-assist-transcription-detection-console/Program.cs`

### Step 3: Update log file naming

Change `$"transcription-detection-{DateTime.Now:yyyyMMdd-HHmmss}.log"` to derive the log name from the session ID: `$"session-{timestamp}.log"`.

**Files:** `Interview-assist-transcription-detection-console/Program.cs` (two locations: headless and UI mode)

### Step 4: Update SessionReportGenerator.ResolveLogFile

Replace the regex-based timestamp transformation with a simple base name match, since the log file now shares the session prefix.

**Files:** `Interview-assist-library/Pipeline/Recording/SessionReportGenerator.cs`

### Step 5: Update SessionReportGenerator.GetReportPath

Already uses `{baseName}.report.md` — verify it handles the new `.recording.jsonl` double extension correctly (strip `.recording` too).

**Files:** `Interview-assist-library/Pipeline/Recording/SessionReportGenerator.cs`

### Step 6: Update evaluation output naming

Change `$"{baseName}-evaluation.json"` to `$"{sessionId}.evaluation.json"`, extracting the session ID from the base name.

**Files:** `Interview-assist-transcription-detection-console/EvaluationRunner.cs`, `Program.cs`

### Step 7: Update comparison output naming

When no `--output` is specified, derive the default as `{sessionId}.comparison.json`.

**Files:** `Interview-assist-transcription-detection-console/EvaluationRunner.cs`, `Program.cs`

### Step 8: Add session ID extraction helper

Create a shared utility method to extract the session ID (`session-YYYY-MM-DD-HHmmss`) from any filename following the convention:

```csharp
public static string? ExtractSessionId(string filePath)
{
    var name = Path.GetFileName(filePath);
    var match = Regex.Match(name, @"^(session-\d{4}-\d{2}-\d{2}-\d{6})");
    return match.Success ? match.Groups[1].Value : null;
}
```

**Files:** New method in `SessionReportGenerator.cs` or a shared utility class.

### Step 9: Update appsettings.md and CLAUDE.md

Update the documentation to reflect the new naming convention and examples.

**Files:** `documentation/docs/operations/appsettings.md`, `CLAUDE.md`

### Step 10: Update --help examples

Update the `--help` output examples to use the new file naming pattern.

**Files:** `Interview-assist-transcription-detection-console/Program.cs`

## Migration

No migration of existing files is required. The convention applies to newly generated files only. Old files with the previous naming will still load correctly since playback and analysis accept arbitrary file paths.

## Design Decisions

- **Dot-separated purpose label** (not hyphen-separated) — avoids ambiguity with the date components in the session ID. `session-2026-02-10-171013.evaluation.json` reads more clearly than `session-2026-02-10-171013-evaluation.json`.
- **`.recording.jsonl` instead of just `.jsonl`** — makes the purpose explicit when files are outside their home directory. A bare `.jsonl` could be a dataset, ground truth, or recording.
- **Session ID as the sort key** — the `session-YYYY-MM-DD-HHmmss` prefix guarantees chronological ordering and groups all related files together regardless of directory structure.
- **No folder restructuring** — files can remain in their current folders (`recordings/`, `logs/`, `reports/`, `evaluations/`) for backwards compatibility, or can be placed in a single `sessions/` folder if desired. The naming convention works either way.
