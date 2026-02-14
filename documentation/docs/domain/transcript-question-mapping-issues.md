# Transcript-to-Question Mapping Issues

## Summary

When the annotation console maps LLM-detected questions back to their positions in the raw transcript, some questions cannot be mapped. This document describes the two root causes and their impact.

## Problem

The annotation console (Concept E) builds a transcript from ASR final events and then tries to find each detected question's `SourceText` within that transcript. In testing with real recordings, 2 out of 6 detected questions failed to map. The root causes are independent issues in different parts of the pipeline.

## Root Cause 1: UtteranceBuilder Text Duplication

**Location:** `Interview-assist-library/Pipeline/Utterance/UtteranceBuilder.cs`, `CombineText()` method (line 234).

**Description:** `UtteranceBuilder` accumulates ASR segments into utterances. Each time a new ASR segment arrives, `CombineText()` appends it to the existing committed text. The method has a comment "Avoid duplication - check if addition is continuation" but the actual implementation does a simple append:

```csharp
private static string CombineText(string committed, string addition)
{
    // ... null checks ...
    // Simple append with space
    return $"{committed.TrimEnd()} {addition.Trim()}";
}
```

This means if Deepgram sends overlapping segments (common when utterance boundary detection is aggressive), the same words appear multiple times in the utterance's `StableText`.

**Impact:** When the LLM receives duplicated text as input, its `SourceText` output may contain the duplicated phrasing, which won't match the clean ASR-based transcript.

**Example from real recording:**
- Deepgram ASR sent segment: `"do you know the difference between"`
- UtteranceBuilder accumulated: `"Do you know the difference between do you know the difference between value types and reference"`
- LLM SourceText based on duplicated input: `"Do you know the difference between do you know the difference between value types and reference types in c sharp?"`
- Clean transcript (from ASR finals): `"do you know the difference between value types and reference types in c sharp"`

The SourceText contains the duplication and cannot be found in the clean transcript.

## Root Cause 2: LLM Reformulation of SourceText

**Location:** All three LLM prompt implementations:
- `Interview-assist-library/Pipeline/Detection/OpenAiIntentDetector.cs` (lines 18-57)
- `Interview-assist-library/Pipeline/OpenAiQuestionDetectionService.cs` (lines 22-54)
- `Interview-assist-pipeline/QuestionDetector.cs` (lines 26-68)

**Description:** All three LLM prompts instruct the model to make detected questions "SELF-CONTAINED" by resolving pronouns and cleaning up text. The `SourceText` field in `DetectedIntent` therefore contains **LLM-generated text**, not a verbatim excerpt from the transcript. Key prompt instructions:

> CRITICAL - Self-contained text:
> - Every detected item MUST make sense on its own without surrounding context
> - Resolve pronouns (it, this, that, they) using previous context

The prompts also instruct to "clean up" questions, reconstruct partial questions, and remove prefixes.

**Impact:** Even when the original transcript text is clean, the LLM may:
1. Insert words to resolve pronouns (e.g., adding "an abstract class" where the speaker said "it")
2. Rephrase declarative fragments as interrogative sentences
3. Add question marks, restructure sentence order, or change word forms

These changes mean the reformulated `SourceText` does not appear verbatim in the transcript.

**Example from real recording:**
- Transcript text: `"what do they both have do they have the same base class"`
- LLM SourceText: `"Do value types and reference types have the same base class in C#?"`

The LLM resolved "they" to "value types and reference types", added "in C#" for context, and restructured the sentence. This reformulated text cannot be found in the transcript.

## Impact on Annotation Console

The annotation console uses two strategies to map questions to transcript positions:

1. **Exact match** — case-insensitive substring search
2. **Word-sequence match** — finds the shortest span containing all source words in order, with tolerance for small gaps

Both strategies fail when the SourceText contains:
- Duplicated words from UtteranceBuilder (extra words that don't exist in the clean transcript)
- Inserted words from LLM reformulation (words the speaker never said)

Unmapped questions appear in the question list but have no corresponding highlight in the transcript, reducing the tool's usefulness for ground truth annotation.

## Recommended Fixes

### For Root Cause 1 (UtteranceBuilder Duplication)

Implement proper overlap detection in `CombineText()`. When appending a new ASR segment, check if the beginning of the new segment overlaps with the end of the committed text and merge accordingly. This is tracked as a known issue but is a non-trivial fix due to the variety of overlap patterns Deepgram produces.

### For Root Cause 2 (LLM Reformulation)

Add a separate `originalText` field to the LLM response schema that contains the verbatim transcript excerpt, alongside the existing self-contained `text` field. This allows:
- The annotation console to use `originalText` for transcript position mapping
- Downstream consumers (answer generation LLM) to use whichever version is more appropriate
- Ground truth evaluation to compare against the actual transcript text

See the prompt and data model changes needed:
- `DetectedIntent.SourceText` remains the self-contained version
- New `DetectedIntent.OriginalText` contains the verbatim excerpt
- All three LLM prompts updated to request both fields
- `DetectedIntentData` (recording format) updated to persist `originalText`
