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
                var similarity = TranscriptExtractor.CalculateSimilarity(gt.Text, det.Text);

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
}
