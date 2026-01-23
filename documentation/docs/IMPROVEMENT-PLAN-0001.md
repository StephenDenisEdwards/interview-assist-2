# Question Detection Improvement Plan

## Overview

Analysis of the console output revealed several issues with the current LLM-based question detection system. This plan addresses each issue with specific implementation recommendations.

---

## Issue 1: Repeated "you" Token Noise

### Problem
The transcription produces repeated tokens like `you you you you you...` during silence or background noise. These artifacts pollute the detection buffer and waste LLM API calls.

### Root Cause
- The upstream transcription service (Whisper) doesn't have effective Voice Activity Detection (VAD)
- No pre-filtering of transcription artifacts before adding to the detection buffer

### Solution: Add Transcription Artifact Filter

**File:** `LlmQuestionDetector.cs`

**Changes:**
1. Add a `IsTranscriptionNoise()` method that detects:
   - Repeated single words (e.g., "you you you", "the the the")
   - Very short repeated patterns (< 4 chars repeated 3+ times)
   - Common filler patterns ("um um", "uh uh", etc.)

2. Modify `AddText()` to filter artifacts before buffering:
```csharp
public void AddText(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return;

    // NEW: Filter transcription noise
    var cleaned = RemoveTranscriptionNoise(text);
    if (string.IsNullOrWhiteSpace(cleaned))
        return;

    _buffer.Append(' ');
    _buffer.Append(cleaned);
    // ... rest of method
}

private static string RemoveTranscriptionNoise(string text)
{
    // Detect repeated word patterns: "you you you" -> ""
    // Detect filler words: "um", "uh", "er"
    // Return cleaned text or empty if all noise
}
```

**Acceptance Criteria:**
- [ ] Input "you you you you" returns empty string
- [ ] Input "you you you Hello there" returns "Hello there"
- [ ] Input "um uh so what is" returns "so what is"
- [ ] Normal sentences pass through unchanged

---

## Issue 2: Duplicate Question Detection

### Problem
The same question is detected and displayed multiple times:
```
[Question 90%] What is the difference between ConfigureAwait(true) and ConfigureAwait(false)?
... (appears 4+ times)
```

### Root Cause
The current `IsSimilar()` method uses Jaccard similarity at 0.6 threshold, but:
1. Questions with the same intent but different wording slip through
2. The LLM reformulates questions slightly differently each time
3. The buffer isn't cleared effectively after detection

### Solution: Enhanced Deduplication

**File:** `LlmQuestionDetector.cs`

**Changes:**

1. **Use semantic fingerprinting instead of word overlap:**
```csharp
private static string GetSemanticFingerprint(string question)
{
    // Extract key technical terms (nouns, verbs)
    // Sort alphabetically
    // Return as fingerprint: "async configureawait difference"
}
```

2. **Increase Jaccard threshold to 0.7:**
```csharp
// Current: return jaccard >= 0.6;
return jaccard >= 0.7;
```

3. **Add time-based suppression window:**
```csharp
private readonly Dictionary<string, DateTime> _detectionTimes = new();
private const int SuppressionWindowMs = 30000; // 30 seconds

private bool IsRecentlyDetected(string fingerprint)
{
    if (_detectionTimes.TryGetValue(fingerprint, out var lastTime))
    {
        if ((DateTime.UtcNow - lastTime).TotalMilliseconds < SuppressionWindowMs)
            return true;
    }
    return false;
}
```

4. **Clear buffer more aggressively after successful detection:**
```csharp
private void ClearDetectedFromBuffer(string questionText)
{
    // Current: only removes exact match
    // NEW: Also remove surrounding context that led to this detection
    var bufferText = _buffer.ToString();

    // Find the question and remove 50 chars before it too
    // This prevents the same lead-up context from triggering re-detection
}
```

**Acceptance Criteria:**
- [ ] Same question asked twice within 30s only appears once
- [ ] "What is Span T?" and "What is Span<T>?" are considered duplicates
- [ ] Buffer is cleared effectively after detection

---

## Issue 3: Technical Term Transcription Errors

### Problem
Technical C#/.NET terms are misrecognized:
| Transcribed | Intended |
|-------------|----------|
| "Spanty" | "Span<T>" |
| "Quality Compare Tea" | "IEqualityComparer<T>" |
| "Sea shard" | "C#" |
| "tHashCode" | "GetHashCode" |

### Root Cause
The Whisper transcription model doesn't have programming vocabulary context.

### Solution: Post-Processing Technical Term Correction

**File:** `LlmQuestionDetector.cs` (or new `TechnicalTermCorrector.cs`)

**Changes:**

1. **Add a technical term replacement dictionary:**
```csharp
private static readonly Dictionary<string, string> TechnicalTermCorrections = new(StringComparer.OrdinalIgnoreCase)
{
    // Generic type patterns
    { "span t", "Span<T>" },
    { "spanty", "Span<T>" },
    { "span tea", "Span<T>" },
    { "list t", "List<T>" },
    { "i enumerable t", "IEnumerable<T>" },
    { "quality compare tea", "IEqualityComparer<T>" },
    { "equality comparer t", "IEqualityComparer<T>" },
    { "i comparable t", "IComparable<T>" },
    { "async local t", "AsyncLocal<T>" },

    // Language names
    { "sea sharp", "C#" },
    { "sea shard", "C#" },
    { "c shard", "C#" },
    { "c-sharp", "C#" },

    // Methods/Types
    { "thashcode", "GetHashCode" },
    { "gethashcode", "GetHashCode" },
    { "configure await", "ConfigureAwait" },
    { "configure a wait", "ConfigureAwait" },
    { "task when all", "Task.WhenAll" },
    { "task wait all", "Task.WaitAll" },
    { "gc collect", "GC.Collect" },
    { "gc select", "GC.Collect" },

    // Common misheard words
    { "a weight", "await" },
    { "a wake", "await" },
    { "new soft", "Newtonsoft" },
    { "jay son", "JSON" },
};
```

2. **Apply corrections before sending to LLM:**
```csharp
public void AddText(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return;

    var cleaned = RemoveTranscriptionNoise(text);
    if (string.IsNullOrWhiteSpace(cleaned))
        return;

    // NEW: Correct technical terms
    var corrected = CorrectTechnicalTerms(cleaned);

    _buffer.Append(' ');
    _buffer.Append(corrected);
}
```

**Acceptance Criteria:**
- [ ] "Spanty" in input becomes "Span<T>" in buffer
- [ ] "quality compare tea" becomes "IEqualityComparer<T>"
- [ ] Corrections are case-insensitive
- [ ] Normal text passes through unchanged

---

## Issue 4: Fragmented Sentence Detection

### Problem
Questions are detected on incomplete sentences because text arrives in chunks:
```
"How does the GC..."  -> [detected prematurely]
"select method work"  -> [detected again as fragment]
```

### Root Cause
Detection runs on every transcription callback, even mid-sentence.

### Solution: Sentence Boundary Awareness

**File:** `LlmQuestionDetector.cs`

**Changes:**

1. **Only run detection when buffer likely contains complete sentences:**
```csharp
public async Task<List<DetectedQuestion>> DetectQuestionsAsync(CancellationToken ct = default)
{
    // ... existing rate limiting ...

    var text = _buffer.ToString().Trim();
    if (string.IsNullOrWhiteSpace(text) || text.Length < 15)
        return results;

    // NEW: Check for sentence completeness
    if (!HasCompleteSentence(text))
        return results;

    // ... rest of method
}

private static bool HasCompleteSentence(string text)
{
    // Must have at least one sentence terminator
    // AND some minimum content after the last terminator is optional
    var lastTerminator = Math.Max(
        text.LastIndexOf('.'),
        Math.Max(text.LastIndexOf('?'), text.LastIndexOf('!')));

    return lastTerminator > 20; // At least one decent sentence
}
```

2. **Increase minimum buffer length before detection:**
```csharp
// Current: text.Length < 15
// NEW: Require more context
if (text.Length < 50)
    return results;
```

3. **Add debounce after new text arrives:**
```csharp
private DateTime _lastTextAddition = DateTime.MinValue;
private const int DebounceAfterTextMs = 500;

public void AddText(string text)
{
    // ... existing code ...
    _lastTextAddition = DateTime.UtcNow;
}

public async Task<List<DetectedQuestion>> DetectQuestionsAsync(...)
{
    // NEW: Wait for text to settle
    var sinceLastText = DateTime.UtcNow - _lastTextAddition;
    if (sinceLastText.TotalMilliseconds < DebounceAfterTextMs)
        return results;

    // ... rest of method
}
```

**Acceptance Criteria:**
- [ ] Partial sentences like "How does the GC" don't trigger detection
- [ ] Complete sentences "How does GC.Collect work?" are detected
- [ ] Detection waits 500ms after last text before analyzing

---

## Issue 5: Configuration Improvements

### Problem
Current defaults may not be optimal for all use cases.

### Solution: Add New Configuration Options

**File:** `appsettings.json`

```json
{
  "QuestionDetection": {
    "Method": "Llm",
    "Model": "gpt-4o-mini",
    "ConfidenceThreshold": 0.7,
    "DetectionIntervalMs": 2000,
    "MinBufferLength": 50,
    "DebounceMs": 500,
    "DeduplicationWindowMs": 30000,
    "EnableTechnicalTermCorrection": true,
    "EnableNoiseFilter": true
  }
}
```

**File:** `LlmQuestionDetector.cs`

Add constructor parameters for new options:
```csharp
public LlmQuestionDetector(
    string apiKey,
    string model = "gpt-4o-mini",
    double confidenceThreshold = 0.7,
    int detectionIntervalMs = 2000,
    int minBufferLength = 50,
    int debounceMs = 500,
    int deduplicationWindowMs = 30000,
    bool enableTechnicalTermCorrection = true,
    bool enableNoiseFilter = true)
```

---

## Implementation Order

| Priority | Issue | Effort | Impact |
|----------|-------|--------|--------|
| 1 | Issue 1: Noise Filter | Low | High |
| 2 | Issue 2: Deduplication | Medium | High |
| 3 | Issue 4: Sentence Boundaries | Low | Medium |
| 4 | Issue 3: Technical Terms | Medium | Medium |
| 5 | Issue 5: Configuration | Low | Low |

### Recommended Sequence

1. **Phase 1** - Quick wins (Issues 1 & 4)
   - Add noise filter to `AddText()`
   - Add sentence boundary check and debounce
   - Test with live audio

2. **Phase 2** - Deduplication (Issue 2)
   - Implement semantic fingerprinting
   - Add time-based suppression
   - Improve buffer clearing

3. **Phase 3** - Polish (Issues 3 & 5)
   - Add technical term dictionary
   - Expose new configuration options
   - Update Program.cs to wire options

---

## Testing Strategy

### Unit Tests
- `RemoveTranscriptionNoise_RepeatedWords_ReturnsEmpty()`
- `RemoveTranscriptionNoise_MixedContent_ReturnsCleanedText()`
- `CorrectTechnicalTerms_KnownTerms_Corrected()`
- `IsSimilar_SameQuestion_ReturnsTrue()`
- `HasCompleteSentence_PartialSentence_ReturnsFalse()`

### Integration Tests
- Record sample audio with known questions
- Verify each question detected exactly once
- Verify no "you you you" noise appears in output

### Manual Testing
- Run with `--detection llm` against YouTube C# interview videos
- Verify output quality matches expected behavior

---

## Files to Modify

| File | Changes |
|------|---------|
| `LlmQuestionDetector.cs` | Main implementation changes |
| `Program.cs` | Wire new configuration options |
| `appsettings.json` | Add new settings |
| `Interview-assist-library-unit-tests/` | Add unit tests |

---

## Notes

- The LLM prompt already instructs to ignore "you you you" but it still processes them (wastes API calls). Pre-filtering is more efficient.
- Consider adding metrics/logging to track detection quality over time.
- Future enhancement: Use embeddings for semantic similarity instead of Jaccard.
