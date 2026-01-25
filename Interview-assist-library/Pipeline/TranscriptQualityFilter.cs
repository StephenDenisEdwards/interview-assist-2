using InterviewAssist.Library.Constants;

namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// Filters and cleans transcript text to remove low-quality transcription artifacts.
/// </summary>
public static class TranscriptQualityFilter
{
    /// <summary>
    /// Determines if transcript text is low-quality and should be filtered out.
    /// </summary>
    /// <param name="text">Transcript text to analyze.</param>
    /// <returns>True if the text should be filtered out.</returns>
    public static bool IsLowQuality(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        var trimmed = text.Trim();

        // Filter very short text with no meaningful content
        if (trimmed.Length < 5) return true;

        // Filter text with excessive repetition (hallucination indicator)
        if (HasExcessiveRepetition(trimmed)) return true;

        return false;
    }

    /// <summary>
    /// Detects excessive word repetition, a common Whisper hallucination pattern.
    /// </summary>
    /// <param name="text">Text to analyze.</param>
    /// <returns>True if excessive repetition is detected.</returns>
    public static bool HasExcessiveRepetition(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return false;

        int maxConsecutive = 1;
        int currentConsecutive = 1;

        for (int i = 1; i < words.Length; i++)
        {
            if (words[i].Equals(words[i - 1], StringComparison.OrdinalIgnoreCase))
            {
                currentConsecutive++;
                maxConsecutive = Math.Max(maxConsecutive, currentConsecutive);
            }
            else
            {
                currentConsecutive = 1;
            }
        }

        // 3+ consecutive identical words indicates hallucination
        return maxConsecutive >= TranscriptionConstants.MaxConsecutiveRepetitions;
    }

    /// <summary>
    /// Cleans transcript text by removing excessive repetitions while preserving
    /// legitimate repeated words (e.g., "very very good" becomes "very very good").
    /// </summary>
    /// <param name="text">Text to clean.</param>
    /// <returns>Cleaned text with repetitions reduced.</returns>
    public static string CleanTranscript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return string.Empty;

        var result = new List<string>();
        string? lastWord = null;
        int repeatCount = 0;

        foreach (var word in words)
        {
            if (lastWord != null && word.Equals(lastWord, StringComparison.OrdinalIgnoreCase))
            {
                repeatCount++;
                // Allow max 2 repetitions (e.g., "very very good" is OK)
                if (repeatCount < 2)
                {
                    result.Add(word);
                }
            }
            else
            {
                result.Add(word);
                repeatCount = 0;
            }
            lastWord = word;
        }

        return string.Join(" ", result);
    }
}
