# Codebase Refactoring Report

**Created:** 2026-02-14
**Status:** Report (no implementation)
**Scope:** Full codebase review across all projects

## Executive Summary

A thorough codebase analysis identified refactoring opportunities organized into five areas. The most impactful changes target the 3,038-line `Program.cs` and duplicated code across intent detection strategies. A phased roadmap prioritizes high-ROI extractions that reduce duplication without architectural risk.

---

## 1. Program.cs (3,038 lines)

**File:** `Interview-assist-transcription-detection-console/Program.cs`

This is the largest file in the codebase and contains the most concentrated duplication.

### 1.1 Duplicated ConfigurationBuilder Blocks

**9 occurrences** of nearly identical configuration setup at lines 293, 507, 544, 628, 654, 1024, 1053, 1107, 1293.

Each follows the same pattern:
```csharp
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();
```

**Recommendation:** Extract a `BuildConfiguration()` helper method, called once and passed to each mode method.

### 1.2 API Key Resolution Duplicated 20 Times

**20 occurrences** of API key resolution across the file:
- `OPENAI_API_KEY`: 13 occurrences (lines 447, 519, 556, 579, 639, 1036, 1065, etc.)
- `DEEPGRAM_API_KEY`: 7 occurrences (lines 310, 315, 356, 453, 602, 1459, 1464)

All use the same `GetFirstNonEmpty()` pattern checking config section, root config, and environment variable.

**Recommendation:** Extract `ResolveOpenAiApiKey(IConfiguration)` and `ResolveDeepgramApiKey(IConfiguration)` methods, or a general `ResolveApiKey(IConfiguration, string sectionKey, string envVar)`.

### 1.3 IntentDetectionOptions Built Identically

**9 references** to `IntentDetectionOptions` construction. The main construction block (lines 462-494) builds nested `Heuristic`, `Llm`, and `Deepgram` sub-options. This is already partially addressed by `LoadIntentDetectionOptions()` but several call sites still construct options independently.

**Recommendation:** Ensure all paths go through `LoadIntentDetectionOptions()` with a shared configuration instance.

### 1.4 Main Method is 398 Lines

`Main()` spans lines 15-413 and handles CLI argument parsing (lines 17-218), mode routing (lines 220-289), and configuration/execution (lines 291-412).

**Recommendation:** Extract into:
- `ParseCommandLineArgs(string[] args)` returning an options record
- `DispatchMode(ParsedArgs args)` routing to the correct Run method

### 1.5 Headless Methods Share 80% Setup

Three headless methods (lines 1288-1689) share significant logic:
- `RunHeadlessPlaybackAsync` (lines 1288-1441)
- `RunHeadlessWavPlaybackAsync` (lines 1444-1620)
- `WireHeadlessPipelineEvents` (lines 1625-1689)

Both playback methods create configuration, load intent detection options, create `UtteranceIntentPipeline`, create `SessionRecorder`/`SessionConfig`, wire events, generate reports, and support auto-evaluation.

**Recommendation:** Extract `SetupHeadlessEnvironment()` returning a context object with pipeline, recorder, and config. The two methods then differ only in their event source (JSONL replay vs. Deepgram WAV transcription).

### 1.6 Magic Strings

**18 unique config key strings** used via bracket notation (`["ApiKey"]`, `["Mode"]`, `["Model"]`, etc.) plus hardcoded defaults for model names (`"nova-2"`, `"gpt-4o-mini"`), file extensions, directory names, color hex codes, and numeric thresholds.

**Recommendation:** Extract to a `ConfigKeys` static class and a `Defaults` static class.

---

## 2. Interview-assist-library Pipeline

### 2.1 LlmIntentStrategy / ParallelIntentStrategy Share ~137 Lines

**Files:**
- `Interview-assist-library/Pipeline/Detection/LlmIntentStrategy.cs` (353 lines)
- `Interview-assist-library/Pipeline/Detection/ParallelIntentStrategy.cs` (477 lines)

**7 duplicated methods:**

| Method | Lines Each | Duplication |
|--------|-----------|-------------|
| `DeduplicateProgressiveRefinements` | ~13 | Identical |
| `TrimContextWindow` | ~8 | Identical |
| `GetTotalChars` | ~6 | Identical |
| `FindBestMatchingUtterance` | ~20 | Nearly identical |
| `CleanupOldDetections` | ~22 | Similar logic |
| `ResetTimeoutTimer` | ~25 | Identical |
| `SignalPause` | ~19 | Nearly identical |

**Recommendation:** Extract an `LlmDetectionBase` abstract class containing the shared methods. `LlmIntentStrategy` becomes a thin subclass, while `ParallelIntentStrategy` adds its heuristic/LLM comparison logic (`ProcessLlmResults`, ~138 lines).

### 2.2 JsonSerializerOptions Created Inconsistently

**9 files** create `JsonSerializerOptions` with 3 different patterns:

| Pattern | Files |
|---------|-------|
| `PropertyNamingPolicy = CamelCase` | SessionReportGenerator, SessionPlayer, SessionRecorder, ComprehensiveReport |
| `PropertyNameCaseInsensitive = true` | DatasetLoader, RegressionTester |
| `WriteIndented = true` | ComprehensiveReport, RegressionTester, JsonRepairUtility |

**Recommendation:** Create a `PipelineJsonOptions` static class with pre-built instances:
- `PipelineJsonOptions.CamelCase` for JSONL serialization/deserialization
- `PipelineJsonOptions.PrettyPrint` for human-readable output
- `PipelineJsonOptions.Flexible` for case-insensitive parsing

### 2.3 Lock Contention (102 lock Statements)

**102 `lock()` statements** across 15 files. Highest contention:

| File | Count |
|------|-------|
| OpenAiRealtimeApi.cs | 13 |
| ParallelIntentStrategy.cs | 9 |
| TranscriptBuffer.cs | 8 |
| SessionPlayer.cs | 7 |
| RateLimitCircuitBreaker.cs | 7 |
| DeepgramTranscriptionService.cs | 6 |
| RevisionTranscriptionService.cs | 6 |
| LatencyTracker.cs | 6 |

**Recommendation:** Review high-contention files for opportunities to use `ConcurrentDictionary`, `ConcurrentQueue`, `Interlocked`, or `ReaderWriterLockSlim` where appropriate. Not all locks need replacing, but files with 7+ locks warrant examination.

### 2.4 Console.Error.WriteLine Instead of ILogger

**12 `Console.Error.WriteLine` calls** across 5 library files:

| File | Count | Context |
|------|-------|---------|
| DeepgramIntentDetector.cs | 3 | HTTP errors, JSON parse failures |
| OpenAiIntentDetector.cs | 3 | HTTP errors, JSON parse failures |
| GroundTruthExtractor.cs | 3 | HTTP errors, JSON parse failures |
| DatasetEvaluator.cs | 1 | Evaluation error |
| DatasetLoader.cs | 1 | JSON parse warning |

All follow a `[ComponentName Error]` prefix pattern.

**Recommendation:** Accept `ILogger` (or the existing `Action<string> log` callback pattern used elsewhere) and replace `Console.Error.WriteLine`. This is lower priority since the existing pattern is consistent and these are error paths.

### 2.5 HttpClient Creation Without Factory

**9 `HttpClient` creations** across 7 files with inconsistent patterns:
- **5 instance fields** (created once, reused): OpenAiChatCompletionService, DeepgramIntentDetector, OpenAiIntentDetector, OpenAiQuestionDetectionService, GroundTruthExtractor
- **4 local scoped** (created per-operation with `using`): BasicTranscriptionService, OpenAiMicTranscriber, RevisionTranscriptionService, StreamingHypothesisService

Issues: no `HttpClientFactory` usage (socket exhaustion risk), inconsistent timeout configuration, mixed auth patterns (Bearer vs. Token header).

**Recommendation:** For instance-field clients, consider `IHttpClientFactory` registration. For scoped clients, the `using` pattern is acceptable. Standardize timeout configuration.

---

## 3. Cross-Project Duplication

### 3.1 JSONL Event Loading Duplicated 5 Times

**5 implementations** of the same `LoadEventsAsync` pattern (read file, skip empty lines, deserialize `RecordedEvent`, handle `JsonException`):

| Location | JsonSerializerOptions |
|----------|----------------------|
| `Interview-assist-annotation-console/Program.cs:124` | CamelCase |
| `Interview-assist-annotation-concept-e-console/Program.cs:420` | CamelCase |
| `Interview-assist-transcription-detection-console/EvaluationRunner.cs:582` | CaseInsensitive |
| `Interview-assist-library/Pipeline/Recording/SessionPlayer.cs:60` | CamelCase |
| `Interview-assist-library/Pipeline/Recording/SessionReportGenerator.cs:34` | CamelCase |

**Recommendation:** `SessionReportGenerator.LoadEventsAsync()` already exists as a public static method in the library. The annotation consoles and EvaluationRunner should use it instead of duplicating. The one CaseInsensitive variant in EvaluationRunner should be tested with CamelCase to confirm compatibility.

### 3.2 GetFirstNonEmpty Copied 5 Times

**5 independent implementations** of the identical `GetFirstNonEmpty(params string?[])` method:

1. `Interview-assist-transcription-detection-console/Program.cs`
2. `Interview-assist-transcription-console/Program.cs:9-16`
3. `Interview-assist-library-integration-tests/Detection/DetectionTestFixture.cs`
4. `Interview-assist-library-integration-tests/Transcription/TranscriptionTestFixture.cs`
5. (Inline usages in other locations)

**Recommendation:** Add `StringUtilities.GetFirstNonEmpty()` to `Interview-assist-library` and reference it everywhere. This is a minimal change with zero risk.

### 3.3 Two Incompatible Question Detection Abstractions

Two distinct approaches coexist:

**IQuestionDetectionService** (library):
- Returns `DetectedQuestion` with `Text`, `Confidence`, `QuestionType` (Question/Imperative/Clarification/FollowUp)
- Focuses on semantic question detection with pronoun resolution
- Used by `OpenAiQuestionDetectionService`

**ILlmIntentDetector** (library, pipeline):
- Returns `IntentResult` with subtypes (Definition/HowTo/Compare/Troubleshoot/Rhetorical/Clarification)
- Lower-level intent classification with confidence filtering
- Used by `DeepgramIntentDetector`, `OpenAiIntentDetector`

**Recommendation:** These serve different purposes and may not need unification. However, documenting the distinction and when to use each would prevent confusion. If consolidation is desired, `IQuestionDetectionService` could be deprecated in favor of the pipeline-based approach.

### 3.4 Two Annotation Consoles Share Structural Code

- `Interview-assist-annotation-console`: 2 C# files, simple read-only transcript viewer
- `Interview-assist-annotation-concept-e-console`: 4 C# files (379-line ConceptEApp, 407-line HighlightTextView, 330-line QuestionListView), full annotation editor

Shared patterns: JSONL loading, CLI argument parsing, `TranscriptExtractor` usage, Terminal.Gui initialization.

**Recommendation:** The concept-e console is a superset. If the simple viewer is no longer needed, it can be retired. If both are needed, extract a shared `AnnotationBootstrapper` with JSONL loading and app initialization.

---

## 4. EvaluationRunner.cs (909 lines)

**File:** `Interview-assist-transcription-detection-console/EvaluationRunner.cs`

### 4.1 Mixed Responsibilities

18 methods mixing evaluation logic, console printing, and file I/O:

**Public methods (7):** `RunAsync`, `AnalyzeErrorsAsync`, `TuneThresholdAsync`, `CompareStrategiesAsync`, `RunRegressionTestAsync`, `CreateBaselineAsync`, constructor

**Print methods (6):** `PrintResults`, `PrintSubtypeResults`, `PrintErrorAnalysis`, `PrintMissedAnalysis`, `PrintThresholdResults`, `PrintComparisonResults`, `PrintRegressionResults`, `PrintGroundTruthRaw`

**Data methods:** `LoadEventsAsync`, `LoadGroundTruthFileAsync`, `SaveResultsAsync`, `SaveComparisonResultsAsync`, `Truncate`

**Recommendation:** Extract `EvaluationReportPrinter` for all Print* methods (~300 lines). This cleanly separates evaluation logic from presentation and makes the printer testable independently.

---

## 5. Priority Roadmap

### Phase 1: High ROI, Low Risk

These changes reduce duplication significantly with minimal architectural risk. Each can be done independently.

| Change | Est. Lines Saved | Risk | Files Touched |
|--------|-----------------|------|---------------|
| Extract `BuildConfiguration()` in Program.cs | ~50 | Low | 1 |
| Extract `ResolveApiKey()` helpers | ~80 | Low | 1 |
| Centralize `PipelineJsonOptions` | ~40 | Low | 9 (library) |
| Move `GetFirstNonEmpty` to library | ~30 | None | 5 |
| Use `SessionReportGenerator.LoadEventsAsync` everywhere | ~60 | Low | 3 |

**Total Phase 1: ~260 lines of duplication removed**

### Phase 2: Medium ROI, Medium Risk

These require more careful testing but address significant structural duplication.

| Change | Est. Lines Saved | Risk | Files Touched |
|--------|-----------------|------|---------------|
| Extract `LlmDetectionBase` from strategies | ~137 | Medium | 2-3 (library) |
| Extract `EvaluationReportPrinter` | ~300 | Medium | 1-2 (console) |
| Extract `SetupHeadlessEnvironment` | ~100 | Medium | 1 (Program.cs) |

**Total Phase 2: ~537 lines of structural improvement**

### Phase 3: Lower Priority, Higher Risk

These are larger changes that may affect behavior or require broader testing.

| Change | Risk | Notes |
|--------|------|-------|
| Decompose Program.cs Main/arg parsing | Medium | Requires careful testing of all CLI modes |
| Consolidate annotation consoles | Medium | May affect user workflows |
| Unify question detection abstractions | High | Affects pipeline behavior |
| Migrate to HttpClientFactory | Medium | Requires DI changes |
| Review lock contention patterns | High | Concurrency changes need thorough testing |
| Extract config key constants | Low | Large but mechanical change |

---

## Appendix: File Size Summary

| File | Lines | Primary Concern |
|------|-------|-----------------|
| Program.cs (detection console) | 3,038 | Configuration/API key duplication |
| EvaluationRunner.cs | 909 | Mixed evaluation/printing |
| ParallelIntentStrategy.cs | 477 | Code shared with LlmIntentStrategy |
| HighlightTextView.cs | 407 | Annotation-specific (no sharing concern) |
| ConceptEApp.cs | 379 | Annotation-specific |
| LlmIntentStrategy.cs | 353 | Code shared with ParallelIntentStrategy |
| QuestionListView.cs | 330 | Annotation-specific |
