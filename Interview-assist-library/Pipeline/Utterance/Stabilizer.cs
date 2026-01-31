namespace InterviewAssist.Library.Pipeline.Utterance;

/// <summary>
/// Computes stable text using Longest Common Prefix across recent hypotheses.
/// Ensures monotonic growth of stable text within an utterance.
/// </summary>
public sealed class Stabilizer : IStabilizer
{
    private readonly int _windowSize;
    private readonly double _minConfidence;
    private readonly bool _requireRepetition;
    private readonly Queue<HypothesisEntry> _hypotheses = new();
    private string _stableText = "";
    private string _committedText = "";

    public Stabilizer(PipelineOptions? options = null)
    {
        options ??= PipelineOptions.Default;
        _windowSize = options.StabilizerWindowSize;
        _minConfidence = options.MinWordConfidence;
        _requireRepetition = options.RequireRepetitionForLowConfidence;
    }

    public string StableText => _stableText;

    public string AddHypothesis(string hypothesis, IReadOnlyList<AsrWord>? words = null)
    {
        if (string.IsNullOrWhiteSpace(hypothesis))
        {
            return _stableText;
        }

        // Add to window
        _hypotheses.Enqueue(new HypothesisEntry(hypothesis, words));

        // Evict old entries
        while (_hypotheses.Count > _windowSize)
        {
            _hypotheses.Dequeue();
        }

        // Compute LCP if we have enough hypotheses
        if (_hypotheses.Count >= 2)
        {
            var lcp = ComputeLongestCommonPrefix(_hypotheses.Select(h => h.Text).ToList());

            // Apply confidence filtering if words are available
            if (words != null && _minConfidence > 0)
            {
                lcp = FilterByConfidence(lcp, words);
            }

            // Monotonicity: stable text can only grow
            if (lcp.Length > _stableText.Length && lcp.StartsWith(_stableText))
            {
                _stableText = lcp;
            }
        }

        return _stableText;
    }

    public string CommitFinal(string finalText)
    {
        // Final text is authoritative
        _committedText = finalText;

        // Update stable to match final if it's an extension
        if (finalText.StartsWith(_stableText))
        {
            _stableText = finalText;
        }
        else if (_stableText.StartsWith(finalText))
        {
            // Final is subset of stable (rare, but handle gracefully)
            _stableText = finalText;
        }
        else
        {
            // Divergence - trust final
            _stableText = finalText;
        }

        return _stableText;
    }

    public void Reset()
    {
        _hypotheses.Clear();
        _stableText = "";
        _committedText = "";
    }

    private static string ComputeLongestCommonPrefix(IList<string> strings)
    {
        if (strings.Count == 0) return "";
        if (strings.Count == 1) return strings[0];

        var minLength = strings.Min(s => s.Length);
        var prefixLength = 0;

        for (int i = 0; i < minLength; i++)
        {
            var c = strings[0][i];
            if (strings.All(s => s[i] == c))
            {
                prefixLength = i + 1;
            }
            else
            {
                break;
            }
        }

        // Trim to last word boundary to avoid partial words
        var prefix = strings[0][..prefixLength];
        var lastSpace = prefix.LastIndexOf(' ');

        if (lastSpace > 0 && prefixLength < strings[0].Length)
        {
            // Only trim if we're not at the end and there's a partial word
            var nextCharInOriginal = strings[0].Length > prefixLength ? strings[0][prefixLength] : ' ';
            if (nextCharInOriginal != ' ')
            {
                prefix = prefix[..(lastSpace + 1)].TrimEnd();
            }
        }

        return prefix;
    }

    private string FilterByConfidence(string text, IReadOnlyList<AsrWord> words)
    {
        // Build a confidence map for words
        var wordConfidences = new Dictionary<string, (double Confidence, int Count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in words)
        {
            var key = word.Word.ToLowerInvariant();
            if (wordConfidences.TryGetValue(key, out var existing))
            {
                wordConfidences[key] = (Math.Max(existing.Confidence, word.Confidence), existing.Count + 1);
            }
            else
            {
                wordConfidences[key] = (word.Confidence, 1);
            }
        }

        // Filter text by confidence
        var textWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filteredWords = new List<string>();

        foreach (var word in textWords)
        {
            var cleanWord = word.TrimEnd('.', ',', '?', '!', ';', ':');
            if (wordConfidences.TryGetValue(cleanWord.ToLowerInvariant(), out var info))
            {
                // Include if high confidence OR repeated (if repetition required)
                if (info.Confidence >= _minConfidence ||
                    (_requireRepetition && info.Count >= 2))
                {
                    filteredWords.Add(word);
                }
                else
                {
                    // Stop at first low-confidence word
                    break;
                }
            }
            else
            {
                // Word not in confidence map, include it
                filteredWords.Add(word);
            }
        }

        return string.Join(" ", filteredWords);
    }

    private sealed record HypothesisEntry(string Text, IReadOnlyList<AsrWord>? Words);
}
