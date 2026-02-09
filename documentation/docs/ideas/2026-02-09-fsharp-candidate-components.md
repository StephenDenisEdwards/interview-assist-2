# F# Candidate Components

**Date:** 2026-02-09
**Status:** Idea

## Summary

An analysis of components in the codebase that exhibit functional programming patterns and could benefit from being written in F#. The codebase already uses immutable records, pure functions, and async/await extensively, making it well-suited for incremental F# adoption.

## Tier 1: Strongest Candidates

### Intent Detection (`IntentDetector.cs`)

The single best candidate. A large if-else/regex classification engine mapping text to intent types. F# discriminated unions and pattern matching would dramatically simplify it:

```fsharp
type IntentType =
  | Question of QuestionSubtype
  | Imperative of ImperativeSubtype
  | Statement
  | Other

and QuestionSubtype = Definition | HowTo | Compare | Troubleshoot
and ImperativeSubtype = Stop | Repeat | Continue | StartOver | Generate
```

The current C# uses `IntentType` enum + `IntentSubtype` enum + nullable `DetectedIntent` record -- a textbook case for discriminated unions with exhaustive matching.

### Text Transformation Pipeline (`TranscriptionPreprocessor.cs`)

Already a chain of pure functions (`RemoveNoise -> CorrectTechnicalTerms`). F#'s pipe operator makes this natural:

```fsharp
let preprocess text =
  text |> removeNoise |> correctTechnicalTerms
```

Immutable lookup dictionaries (TechnicalTermCorrections, FillerWords, StopWords) and pure helper functions (`GetSignificantWords`, `IsSimilar`, `GetSemanticFingerprint`, `Normalize`) are all ideal F# territory.

### Audio Resampling (`AudioResampler.cs`)

Pure function: `byte[] * WaveFormat -> byte[]`. Already takes a higher-order function (`Func<int, int, double> getSample`). This is functional code in C# syntax.

### Event Types (`Events.cs`)

The `AsrEvent`, `UtteranceEvent`, `IntentEvent`, `ActionEvent` sealed record hierarchy maps directly to F# discriminated unions -- more concise and with compiler-enforced exhaustive matching.

## Tier 2: High Priority

### Evaluation Logic (`DatasetEvaluator.cs`)

Classic map-fold pattern: map dataset items through an evaluation function, fold results into a confusion matrix, compute accuracy/precision/recall/F1 metrics. Natural `List.map |> List.fold` in F#.

### LLM Intent Strategies (`LlmIntentStrategy.cs`, `ParallelIntentStrategy.cs`)

Detection pipelines that transform utterances through classification stages with confidence scoring and deduplication. Pure transformation logic within async wrappers.

### JSON Response Parsing (`OpenAiChatCompletionService.cs`)

Nested null-checks and property lookups on `JsonDocument` would be cleaner with `Result<'a, 'e>` and pattern matching:

```fsharp
let parseResponse (json: string) : Result<ChatResponse, ParseError> =
  match JsonDocument.Parse json with
  | Ok doc ->
    match (doc.RootElement.GetProperty "answer", doc.RootElement.GetProperty "code") with
    | (Ok answer, Ok code) -> Ok { answer = answer; code = code }
    | _ -> Error ParseError
  | Error e -> Error (JsonError e)
```

## Tier 3: Good Candidates

### Configuration Builders (`ServiceCollectionExtensions.cs`)

4 builder classes with 100+ fluent methods total. F# replaces all of this with record update syntax:

```fsharp
let opts = { defaults with apiKey = "key"; voice = "alloy" }
```

### Async Pipeline Orchestration (`PipelineRealtimeApi.cs`)

Already uses `await foreach` over async enumerables -- maps naturally to F# async computation expressions.

### Context Loading (`ContextLoader.cs`)

Document loading and chunking pipeline. Pure function: `(cvPath, jobSpecPath) -> (preview, chunks)`. Natural fit for function composition and pattern matching on optional document paths.

## What Should Stay in C#

- **Audio capture** (`WindowsAudioCaptureService`) -- NAudio/COM interop is better in C#
- **UI code** (`Terminal.Gui` console apps) -- framework bindings are C#-native
- **DI wiring** (`ServiceCollectionExtensions`) -- Microsoft.Extensions.DI is C#-first
- **WebSocket management** -- low-level I/O with platform interop

## Key Patterns That F# Improves

| Pattern | Current C# | F# Advantage |
|---------|-----------|--------------|
| Pipeline composition | Method chaining | `\|>` operator, more readable |
| Discriminated unions | Enum + nullable record | Type-safe, exhaustive matching |
| Option types | `null` checks | `Option<'a>` with pattern matching |
| Error handling | Exceptions or null | `Result<'a, 'e>` for explicit error types |
| Data transformation | LINQ + loops | `List.map`, `List.fold`, sequences |
| Async coordination | Channels + events | Async workflows, task computation expressions |
| Immutability | `record` with `init` | Records are immutable by default |
| Builder patterns | Fluent API chains | Record update syntax + function composition |

## Suggested Migration Path

1. **Phase 1:** Create F# library with pure functions (TranscriptionPreprocessor, AudioResampler, ContextLoader)
2. **Phase 2:** Implement discriminated unions for all Intent types (Events.cs)
3. **Phase 3:** Refactor detection logic (IntentDetector, strategies) to F#
4. **Phase 4:** Rewrite builders as F# record types with update syntax
5. **Phase 5:** Create F# async pipeline orchestration (PipelineRealtimeApi)

C#/F# interop in .NET is seamless -- F# libraries are consumed from C# like any other assembly.
