namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Utilities for comparing and analyzing transcript text for stability detection.
/// </summary>
public static class TranscriptionTextComparer
{
    /// <summary>
    /// Calculates the Jaccard similarity between two strings based on word tokens.
    /// </summary>
    /// <param name="text1">First text to compare.</param>
    /// <param name="text2">Second text to compare.</param>
    /// <returns>Similarity score from 0.0 (no overlap) to 1.0 (identical).</returns>
    public static double JaccardSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) && string.IsNullOrWhiteSpace(text2))
            return 1.0;

        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0.0;

        var words1 = Tokenize(text1);
        var words2 = Tokenize(text2);

        if (words1.Count == 0 && words2.Count == 0)
            return 1.0;

        if (words1.Count == 0 || words2.Count == 0)
            return 0.0;

        var intersection = words1.Intersect(words2, StringComparer.OrdinalIgnoreCase).Count();
        var union = words1.Union(words2, StringComparer.OrdinalIgnoreCase).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <summary>
    /// Finds the longest common prefix between two strings at word boundaries.
    /// </summary>
    /// <param name="text1">First text to compare.</param>
    /// <param name="text2">Second text to compare.</param>
    /// <returns>The common prefix string.</returns>
    public static string CommonPrefix(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return string.Empty;

        var words1 = Tokenize(text1);
        var words2 = Tokenize(text2);

        var commonWords = new List<string>();
        var minLength = Math.Min(words1.Count, words2.Count);

        for (int i = 0; i < minLength; i++)
        {
            if (words1[i].Equals(words2[i], StringComparison.OrdinalIgnoreCase))
            {
                commonWords.Add(words1[i]);
            }
            else
            {
                break;
            }
        }

        return string.Join(" ", commonWords);
    }

    /// <summary>
    /// Finds the longest common suffix between two strings at word boundaries.
    /// </summary>
    /// <param name="text1">First text to compare.</param>
    /// <param name="text2">Second text to compare.</param>
    /// <returns>The common suffix string.</returns>
    public static string CommonSuffix(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return string.Empty;

        var words1 = Tokenize(text1);
        var words2 = Tokenize(text2);

        var commonWords = new List<string>();
        var minLength = Math.Min(words1.Count, words2.Count);

        for (int i = 0; i < minLength; i++)
        {
            var idx1 = words1.Count - 1 - i;
            var idx2 = words2.Count - 1 - i;

            if (words1[idx1].Equals(words2[idx2], StringComparison.OrdinalIgnoreCase))
            {
                commonWords.Insert(0, words1[idx1]);
            }
            else
            {
                break;
            }
        }

        return string.Join(" ", commonWords);
    }

    /// <summary>
    /// Determines if two transcripts are similar enough to be considered matching.
    /// </summary>
    /// <param name="text1">First text to compare.</param>
    /// <param name="text2">Second text to compare.</param>
    /// <param name="threshold">Minimum Jaccard similarity required (0.0-1.0).</param>
    /// <returns>True if texts are considered matching.</returns>
    public static bool AreMatching(string text1, string text2, double threshold = 0.85)
    {
        return JaccardSimilarity(text1, text2) >= threshold;
    }

    /// <summary>
    /// Extracts the new portion of text2 that extends beyond text1.
    /// Useful for incremental stability tracking.
    /// </summary>
    /// <param name="stableText">The currently stable text.</param>
    /// <param name="newText">The new transcript to compare.</param>
    /// <returns>The portion of newText that extends beyond stableText, or null if no match.</returns>
    public static string? GetExtension(string stableText, string newText)
    {
        if (string.IsNullOrWhiteSpace(stableText))
            return newText?.Trim();

        if (string.IsNullOrWhiteSpace(newText))
            return null;

        var stableWords = Tokenize(stableText);
        var newWords = Tokenize(newText);

        // Check if newText starts with stableText
        if (newWords.Count < stableWords.Count)
            return null;

        for (int i = 0; i < stableWords.Count; i++)
        {
            if (!stableWords[i].Equals(newWords[i], StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // Return the extension
        if (newWords.Count == stableWords.Count)
            return string.Empty;

        var extensionWords = newWords.Skip(stableWords.Count);
        return string.Join(" ", extensionWords);
    }

    /// <summary>
    /// Finds the agreed-upon text across multiple transcripts.
    /// Returns the longest common prefix that appears in all transcripts.
    /// </summary>
    /// <param name="transcripts">Collection of transcripts to compare.</param>
    /// <returns>The text that all transcripts agree on.</returns>
    public static string FindAgreedText(IEnumerable<string> transcripts)
    {
        var list = transcripts.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        if (list.Count == 0)
            return string.Empty;

        if (list.Count == 1)
            return list[0].Trim();

        // Start with the first transcript and find common prefix with each subsequent one
        var agreed = list[0];
        for (int i = 1; i < list.Count; i++)
        {
            agreed = CommonPrefix(agreed, list[i]);
            if (string.IsNullOrWhiteSpace(agreed))
                break;
        }

        return agreed;
    }

    /// <summary>
    /// Normalizes text for comparison by trimming and normalizing whitespace.
    /// </summary>
    /// <param name="text">Text to normalize.</param>
    /// <returns>Normalized text.</returns>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(" ", Tokenize(text));
    }

    /// <summary>
    /// Tokenizes text into words, handling punctuation and whitespace.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        return text
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
