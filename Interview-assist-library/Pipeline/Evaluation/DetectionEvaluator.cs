using InterviewAssist.Library.Pipeline.Recording;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Evaluates real-time question detection against LLM-extracted ground truth.
/// </summary>
public sealed class DetectionEvaluator
{
    private readonly double _matchThreshold;

    public DetectionEvaluator(double matchThreshold = 0.7)
    {
        _matchThreshold = matchThreshold;
    }

    /// <summary>
    /// Evaluate detected questions against ground truth.
    /// </summary>
    public EvaluationResult Evaluate(
        IReadOnlyList<ExtractedQuestion> groundTruth,
        IReadOnlyList<RecordedIntentEvent> detectedQuestions)
    {
        var detected = detectedQuestions
            .Select(q => new DetectedQuestionInfo(
                Text: q.Data.Intent.SourceText,
                Subtype: q.Data.Intent.Subtype,
                Confidence: q.Data.Intent.Confidence,
                UtteranceId: q.Data.UtteranceId,
                OffsetMs: q.OffsetMs))
            .ToList();

        var matches = new List<MatchedQuestion>();
        var matchedGroundTruth = new HashSet<int>();
        var matchedDetected = new HashSet<int>();

        // Find matches using fuzzy string matching
        for (var gtIdx = 0; gtIdx < groundTruth.Count; gtIdx++)
        {
            var gt = groundTruth[gtIdx];
            var bestMatchIdx = -1;
            var bestSimilarity = 0.0;

            for (var detIdx = 0; detIdx < detected.Count; detIdx++)
            {
                if (matchedDetected.Contains(detIdx))
                    continue;

                var det = detected[detIdx];
                var similarity = CalculateMatchSimilarity(gt.Text, det.Text);

                if (similarity >= _matchThreshold && similarity > bestSimilarity)
                {
                    bestMatchIdx = detIdx;
                    bestSimilarity = similarity;
                }
            }

            if (bestMatchIdx >= 0)
            {
                matches.Add(new MatchedQuestion(
                    GroundTruth: gt,
                    Detected: detected[bestMatchIdx],
                    SimilarityScore: bestSimilarity));
                matchedGroundTruth.Add(gtIdx);
                matchedDetected.Add(bestMatchIdx);
            }
        }

        // Calculate missed (false negatives) - ground truth not matched
        var missed = groundTruth
            .Where((_, idx) => !matchedGroundTruth.Contains(idx))
            .ToList();

        // Calculate false alarms (false positives) - detected but not in ground truth
        var falseAlarms = detected
            .Where((_, idx) => !matchedDetected.Contains(idx))
            .ToList();

        return new EvaluationResult
        {
            TruePositives = matches.Count,
            FalsePositives = falseAlarms.Count,
            FalseNegatives = missed.Count,
            Matches = matches,
            Missed = missed,
            FalseAlarms = falseAlarms
        };
    }

    /// <summary>
    /// Calculates match similarity using the best of Levenshtein similarity and
    /// word containment. Word containment handles pronoun resolution where the
    /// detector expands "it" → "the appsettings.json file" making the detected
    /// text longer but semantically equivalent.
    /// </summary>
    internal static double CalculateMatchSimilarity(string text1, string text2)
    {
        var levenshtein = TranscriptExtractor.CalculateSimilarity(text1, text2);
        var containment = WordContainmentSimilarity(text1, text2);
        return Math.Max(levenshtein, containment);
    }

    private static readonly char[] WordSeparators = [' ', '\t', ',', '.', '?', '!', '\'', '"', '(', ')'];
    private static readonly HashSet<string> Pronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "it", "its", "they", "them", "their", "theirs",
        "this", "that", "these", "those",
        "he", "she", "him", "her", "his", "hers"
    };

    /// <summary>
    /// Measures what fraction of the shorter text's non-pronoun words appear in the
    /// longer text. High containment means the detected text is a pronoun-resolved
    /// expansion of the ground truth (or vice versa).
    /// </summary>
    private static double WordContainmentSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0.0;

        var words1 = GetContentWords(text1);
        var words2 = GetContentWords(text2);

        if (words1.Count == 0 || words2.Count == 0)
            return 0.0;

        // Check containment of the shorter set's words in the longer set
        var shorter = words1.Count <= words2.Count ? words1 : words2;
        var longer = words1.Count <= words2.Count ? words2 : words1;

        var matched = shorter.Count(w => longer.Contains(w));
        return (double)matched / shorter.Count;
    }

    private static HashSet<string> GetContentWords(string text)
    {
        return text.ToLowerInvariant()
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Pronouns.Contains(w))
            .ToHashSet();
    }
}
