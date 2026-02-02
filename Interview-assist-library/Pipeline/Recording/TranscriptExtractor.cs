using System.Text;

namespace InterviewAssist.Library.Pipeline.Recording;

/// <summary>
/// Extracts full transcript from recorded session events.
/// </summary>
public static class TranscriptExtractor
{
    /// <summary>
    /// Extract the complete transcript as a single string from recorded events.
    /// </summary>
    /// <param name="events">Recorded events from a session</param>
    /// <param name="includeSpeakers">Whether to include speaker labels for diarized sessions</param>
    /// <returns>The full transcript text</returns>
    public static string ExtractFullTranscript(IEnumerable<RecordedEvent> events, bool includeSpeakers = false)
    {
        var segments = ExtractSegments(events);

        if (!includeSpeakers)
        {
            return string.Join(" ", segments.Select(s => s.Text.Trim()));
        }

        var sb = new StringBuilder();
        string? currentSpeaker = null;

        foreach (var segment in segments)
        {
            if (segment.SpeakerId != currentSpeaker && segment.SpeakerId != null)
            {
                currentSpeaker = segment.SpeakerId;
                sb.AppendLine();
                sb.Append($"[Speaker {currentSpeaker}] ");
            }
            sb.Append(segment.Text.Trim());
            sb.Append(' ');
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Extract transcript as individual segments with metadata.
    /// </summary>
    /// <param name="events">Recorded events from a session</param>
    /// <returns>List of transcript segments in chronological order</returns>
    public static IReadOnlyList<TranscriptSegment> ExtractSegments(IEnumerable<RecordedEvent> events)
    {
        var segments = new List<TranscriptSegment>();

        var orderedEvents = events
            .OfType<RecordedUtteranceEvent>()
            .Where(e => e.Data.EventType == "Final" && !string.IsNullOrWhiteSpace(e.Data.StableText))
            .OrderBy(e => e.OffsetMs);

        foreach (var evt in orderedEvents)
        {
            segments.Add(new TranscriptSegment(
                Id: evt.Data.Id,
                Text: evt.Data.StableText,
                SpeakerId: evt.Data.SpeakerId,
                StartOffsetMs: evt.OffsetMs - evt.Data.DurationMs,
                EndOffsetMs: evt.OffsetMs
            ));
        }

        return segments;
    }

    /// <summary>
    /// Extract all detected questions from recorded events.
    /// </summary>
    /// <param name="events">Recorded events from a session</param>
    /// <param name="candidatesOnly">If true, only return events marked as candidates</param>
    /// <returns>List of intent events that are questions</returns>
    public static IReadOnlyList<RecordedIntentEvent> ExtractDetectedQuestions(
        IEnumerable<RecordedEvent> events,
        bool candidatesOnly = false)
    {
        return events
            .OfType<RecordedIntentEvent>()
            .Where(e => e.Data.Intent.Type == "Question")
            .Where(e => !candidatesOnly || e.Data.IsCandidate)
            .OrderBy(e => e.OffsetMs)
            .ToList();
    }

    /// <summary>
    /// Deduplicate questions by text similarity.
    /// </summary>
    /// <param name="questions">List of detected question events</param>
    /// <param name="similarityThreshold">Minimum similarity (0-1) to consider as duplicate</param>
    /// <returns>Deduplicated list of questions</returns>
    public static IReadOnlyList<RecordedIntentEvent> DeduplicateQuestions(
        IEnumerable<RecordedIntentEvent> questions,
        double similarityThreshold = 0.8)
    {
        var result = new List<RecordedIntentEvent>();

        foreach (var question in questions)
        {
            var isDuplicate = result.Any(existing =>
                CalculateSimilarity(existing.Data.Intent.SourceText, question.Data.Intent.SourceText) >= similarityThreshold);

            if (!isDuplicate)
            {
                result.Add(question);
            }
        }

        return result;
    }

    /// <summary>
    /// Calculate text similarity using normalized Levenshtein distance.
    /// </summary>
    public static double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2))
            return 1.0;
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0.0;

        // Normalize: lowercase and trim
        var s1 = text1.ToLowerInvariant().Trim();
        var s2 = text2.ToLowerInvariant().Trim();

        if (s1 == s2)
            return 1.0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);

        return 1.0 - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++)
            d[i, 0] = i;
        for (var j = 0; j <= n; j++)
            d[0, j] = j;

        for (var j = 1; j <= n; j++)
        {
            for (var i = 1; i <= m; i++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }
}

/// <summary>
/// A segment of transcript with metadata.
/// </summary>
public sealed record TranscriptSegment(
    string Id,
    string Text,
    string? SpeakerId,
    long StartOffsetMs,
    long EndOffsetMs);
