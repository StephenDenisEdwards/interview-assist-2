namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Shared utilities for preprocessing transcription text before detection.
/// </summary>
public static class TranscriptionPreprocessor
{
    /// <summary>
    /// Dictionary of common misheard programming terms from speech-to-text.
    /// </summary>
    private static readonly Dictionary<string, string> TechnicalTermCorrections = new(StringComparer.OrdinalIgnoreCase)
    {
        // Generic type patterns
        { "span t", "Span<T>" },
        { "spanty", "Span<T>" },
        { "span tea", "Span<T>" },
        { "list t", "List<T>" },
        { "list tea", "List<T>" },
        { "i enumerable t", "IEnumerable<T>" },
        { "i enumerable tea", "IEnumerable<T>" },
        { "quality compare tea", "IEqualityComparer<T>" },
        { "equality comparer t", "IEqualityComparer<T>" },
        { "equality comparer tea", "IEqualityComparer<T>" },
        { "i comparable t", "IComparable<T>" },
        { "i comparable tea", "IComparable<T>" },
        { "async local t", "AsyncLocal<T>" },
        { "async local tea", "AsyncLocal<T>" },
        { "dictionary t", "Dictionary<TKey, TValue>" },
        { "func t", "Func<T>" },
        { "action t", "Action<T>" },

        // Language names
        { "sea sharp", "C#" },
        { "sea shard", "C#" },
        { "c shard", "C#" },
        { "c-sharp", "C#" },
        { "see sharp", "C#" },
        { "f sharp", "F#" },
        { "f-sharp", "F#" },

        // Methods/Types
        { "thashcode", "GetHashCode" },
        { "gethashcode", "GetHashCode" },
        { "t hash code", "GetHashCode" },
        { "configure await", "ConfigureAwait" },
        { "configure a wait", "ConfigureAwait" },
        { "task when all", "Task.WhenAll" },
        { "task wait all", "Task.WaitAll" },
        { "gc collect", "GC.Collect" },
        { "gc select", "GC.Collect" },
        { "g c collect", "GC.Collect" },
        { "i disposable", "IDisposable" },
        { "eye disposable", "IDisposable" },
        { "i async disposable", "IAsyncDisposable" },

        // Common misheard words
        { "a weight", "await" },
        { "a wake", "await" },
        { "a wait", "await" },
        { "new soft", "Newtonsoft" },
        { "newton soft", "Newtonsoft" },
        { "jay son", "JSON" },
        { "jason", "JSON" },
        { "link", "LINQ" },
        { "ef core", "EF Core" },
        { "e f core", "EF Core" },
        { "entity framework", "Entity Framework" },
        { "asp net", "ASP.NET" },
        { "asp.net", "ASP.NET" },
        { "dot net", ".NET" },
        { "dotnet", ".NET" },

        // Async patterns
        { "async await", "async/await" },
        { "a sink a weight", "async/await" },
        { "value task", "ValueTask" },
        { "value task t", "ValueTask<T>" },

        // Memory/performance terms
        { "stack alec", "stackalloc" },
        { "stack alloc", "stackalloc" },
        { "memory t", "Memory<T>" },
        { "read only span", "ReadOnlySpan<T>" },
        { "read only memory", "ReadOnlyMemory<T>" },
    };

    /// <summary>
    /// Filler words to remove from transcription.
    /// </summary>
    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "um", "uh", "er", "ah", "hmm", "hm", "mm", "mhm", "erm"
    };

    /// <summary>
    /// Stop words to ignore when comparing questions.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "in", "on", "at", "to", "for", "of", "with", "by", "from", "as",
        "and", "or", "but", "if", "then", "else", "when", "where", "why", "how",
        "what", "which", "who", "whom", "this", "that", "these", "those",
        "do", "does", "did", "have", "has", "had", "can", "could", "would", "should",
        "will", "shall", "may", "might", "must", "you", "your", "we", "our", "me", "i"
    };

    /// <summary>
    /// Removes transcription noise like repeated words and filler words.
    /// </summary>
    public static string RemoveNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return string.Empty;

        var result = new List<string>();
        string? lastWord = null;
        int repeatCount = 0;

        foreach (var word in words)
        {
            var cleanWord = word.Trim('.', ',', '!', '?', ';', ':');

            // Skip filler words
            if (FillerWords.Contains(cleanWord))
                continue;

            // Track repetition
            if (string.Equals(cleanWord, lastWord, StringComparison.OrdinalIgnoreCase))
            {
                repeatCount++;
                // Allow up to 2 repetitions (for emphasis), skip 3+
                if (repeatCount >= 3)
                    continue;
            }
            else
            {
                lastWord = cleanWord;
                repeatCount = 1;
            }

            result.Add(word);
        }

        if (result.Count == 0)
            return string.Empty;

        // Check if the result is mostly a single repeated word
        var uniqueWords = result
            .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?'))
            .Where(w => w.Length > 1)
            .Distinct()
            .ToList();

        // If only 1 unique word and it's very short, likely noise
        if (uniqueWords.Count <= 1 && result.Count > 2)
        {
            var singleWord = uniqueWords.FirstOrDefault();
            if (singleWord != null && singleWord.Length <= 4)
                return string.Empty;
        }

        return string.Join(' ', result);
    }

    /// <summary>
    /// Corrects commonly misheard technical programming terms.
    /// </summary>
    public static string CorrectTechnicalTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = text;

        // Apply corrections - longer patterns first to avoid partial matches
        var sortedCorrections = TechnicalTermCorrections
            .OrderByDescending(kvp => kvp.Key.Length)
            .ToList();

        foreach (var (pattern, replacement) in sortedCorrections)
        {
            var index = result.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                result = result.Remove(index, pattern.Length).Insert(index, replacement);
                index = result.IndexOf(pattern, index + replacement.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies all preprocessing steps to text.
    /// </summary>
    public static string Preprocess(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var result = RemoveNoise(text);
        if (string.IsNullOrWhiteSpace(result))
            return string.Empty;

        return CorrectTechnicalTerms(result);
    }

    /// <summary>
    /// Extracts significant words from text for comparison.
    /// </summary>
    public static HashSet<string> GetSignificantWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', '?', '!', ',', ';', ':').ToLowerInvariant())
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return words;
    }

    /// <summary>
    /// Gets a semantic fingerprint for deduplication.
    /// </summary>
    public static string GetSemanticFingerprint(string text)
    {
        var significant = GetSignificantWords(text);
        var sorted = significant.OrderBy(w => w, StringComparer.OrdinalIgnoreCase);
        return string.Join(" ", sorted);
    }

    /// <summary>
    /// Checks if two texts are semantically similar using Jaccard similarity.
    /// </summary>
    public static bool IsSimilar(string a, string b, double threshold = 0.7)
    {
        // Very short strings - require exact match
        if (a.Length < 15 || b.Length < 15)
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // Check if one contains the other
        if (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
            b.Contains(a, StringComparison.OrdinalIgnoreCase))
            return true;

        // Word-based Jaccard similarity
        var wordsA = GetSignificantWords(a);
        var wordsB = GetSignificantWords(b);

        if (wordsA.Count == 0 || wordsB.Count == 0)
            return false;

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();

        var jaccard = (double)intersection / union;
        return jaccard >= threshold;
    }

    /// <summary>
    /// Normalizes text for comparison.
    /// </summary>
    public static string Normalize(string text)
    {
        return string.Join(" ", text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
