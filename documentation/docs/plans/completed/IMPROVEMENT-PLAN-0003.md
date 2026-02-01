# IMPROVEMENT-PLAN-0003: Transcription Pipeline Quality Improvements

**Created:** 2026-01-24
**Status:** Completed

## Overview

Fix six issues identified in the pipeline transcription console output that impact transcription quality and question detection accuracy.

## Issues Addressed

| # | Issue | Root Cause | Solution |
|---|-------|------------|----------|
| 1 | Audio too short error | No minimum duration check before Whisper API | Added duration validation (>= 0.1s) |
| 2 | "you you you..." hallucinations | No silence/VAD detection | Added RMS energy-based silence detection |
| 3 | Misheard words ("lock stamen") | No language parameter to Whisper | Added configurable language and prompt params |
| 4 | False positive questions | Detector trusts garbled input | Added transcript quality filter with repetition detection |
| 5 | Fragmented sentences | Fixed 3s window ignores speech boundaries | Implemented adaptive batching with silence-aware flush |
| 6 | Duplicate detections | Exact hash matching only | Replaced with Jaccard similarity + time-based suppression |

---

## Implementation Phases

### Phase 1: Critical Fixes (Issues 1 + 2)

- [x] Create `TranscriptionConstants.cs` with min duration and silence threshold constants
- [x] Add `IsSilence()` method to `OpenAiMicTranscriber.cs` using RMS energy calculation
- [x] Add duration validation in `FlushAndTranscribeAsync()`
- [x] Add `SilenceEnergyThreshold` configuration to `PipelineApiOptions.cs`
- [x] Update `PipelineRealtimeApi.cs` to pass options to transcriber

### Phase 2: Quality Improvements (Issues 3 + 4)

- [x] Add `_language` and `_prompt` fields to `OpenAiMicTranscriber.cs`
- [x] Update `TranscribeAsync()` to pass language and prompt to Whisper API
- [x] Create `TranscriptQualityFilter.cs` with `IsLowQuality()` and `CleanTranscript()` methods
- [x] Integrate quality filter in `PipelineRealtimeApi.HandleTranscript()`
- [x] Add `TranscriptionLanguage` and `TranscriptionPrompt` to `PipelineApiOptions.cs`

### Phase 3: Enhanced Batching (Issue 5)

- [x] Add `_windowMs` and `_maxWindowMs` fields to `OpenAiMicTranscriber.cs`
- [x] Update `TranscriptionLoop()` to use silence-aware adaptive batching
- [x] Add `MaxTranscriptionBatchMs` configuration to `PipelineApiOptions.cs`

### Phase 4: Improved Deduplication (Issue 6)

- [x] Replace `HashSet<string>` with `Dictionary<string, DateTime>` in `QuestionQueue.cs`
- [x] Add `NormalizeForComparison()` method for text normalization
- [x] Add `IsSimilar()` method using Jaccard similarity
- [x] Add `ExpireOldEntries()` for time-based suppression
- [x] Add `DeduplicationSimilarityThreshold` and `DeduplicationWindowMs` to `PipelineApiOptions.cs`
- [x] Update tests for new deduplication behavior

---

## Implementation Summary

### Tasks Completed

| Phase | Tasks | Description |
|-------|-------|-------------|
| Phase 1 | 5 | Minimum duration validation, RMS silence detection, constants file |
| Phase 2 | 5 | Language/prompt params, quality filter, repetition cleaning |
| Phase 3 | 3 | Adaptive batching with min/max windows |
| Phase 4 | 6 | Jaccard similarity, time-based suppression, test updates |

### Files Changed

| File | Changes |
|------|---------|
| `Interview-assist-library/Transcription/OpenAiMicTranscriber.cs` | Added silence detection, duration validation, language/prompt params, adaptive batching (windowMs, maxWindowMs) |
| `Interview-assist-library/Pipeline/PipelineApiOptions.cs` | Added 6 new configuration properties |
| `Interview-assist-library/Pipeline/PipelineRealtimeApi.cs` | Integrated quality filter in HandleTranscript, passes new options to transcriber and queue |
| `Interview-assist-library/Pipeline/QuestionQueue.cs` | Replaced hash-based dedup with Jaccard similarity + time-based suppression |
| `Interview-assist-library-unit-tests/Pipeline/QuestionQueueTests.cs` | Updated tests for new deduplication behavior (+2 new tests) |

### New Files Created

| File | Purpose |
|------|---------|
| `Interview-assist-library/Constants/TranscriptionConstants.cs` | Constants for min duration (0.1s), silence threshold (0.01), max repetitions (3) |
| `Interview-assist-library/Pipeline/TranscriptQualityFilter.cs` | Static class with IsLowQuality(), HasExcessiveRepetition(), CleanTranscript() |

### New Configuration Options

```csharp
// PipelineApiOptions.cs additions:

// Silence/VAD detection
public double SilenceEnergyThreshold { get; init; } = 0.01;

// Transcription quality
public string? TranscriptionLanguage { get; init; } = "en";
public string? TranscriptionPrompt { get; init; }

// Adaptive batching
public int MaxTranscriptionBatchMs { get; init; } = 6000;

// Deduplication
public double DeduplicationSimilarityThreshold { get; init; } = 0.7;
public int DeduplicationWindowMs { get; init; } = 30000;
```

### Build and Test Results

```
Build: Succeeded (0 errors, 25 warnings - all pre-existing)
Tests: 146 passed, 0 failed
```

### Expected Improvements

1. **No "audio too short" errors** - Duration validated before API call
2. **No hallucinations on silence** - RMS silence detection skips quiet audio
3. **Better transcription accuracy** - Language hint improves Whisper recognition
4. **Filtered garbage text** - Quality filter catches "you you you..." patterns
5. **Complete sentences** - Adaptive batching flushes at speech boundaries
6. **Single detection per question** - Jaccard similarity prevents near-duplicates
