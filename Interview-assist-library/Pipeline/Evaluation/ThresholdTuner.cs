using InterviewAssist.Library.Pipeline.Recording;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Tests different confidence thresholds to find optimal detection settings.
/// </summary>
public sealed class ThresholdTuner
{
    private readonly double _minThreshold;
    private readonly double _maxThreshold;
    private readonly double _step;

    public ThresholdTuner(double minThreshold = 0.3, double maxThreshold = 0.95, double step = 0.05)
    {
        _minThreshold = minThreshold;
        _maxThreshold = maxThreshold;
        _step = step;
    }

    /// <summary>
    /// Test different confidence thresholds and return metrics for each.
    /// </summary>
    public ThresholdTuningResult Tune(
        IReadOnlyList<ExtractedQuestion> groundTruth,
        IReadOnlyList<RecordedIntentEvent> allDetected,
        double matchThreshold = 0.7)
    {
        var results = new List<ThresholdResult>();
        var evaluator = new DetectionEvaluator(matchThreshold);

        for (var threshold = _minThreshold; threshold <= _maxThreshold; threshold += _step)
        {
            // Filter detections by confidence threshold
            var filtered = allDetected
                .Where(d => d.Data.Intent.Confidence >= threshold)
                .ToList();

            // Deduplicate
            var deduplicated = TranscriptExtractor.DeduplicateQuestions(filtered, 0.8);

            // Evaluate
            var result = evaluator.Evaluate(groundTruth, deduplicated);

            results.Add(new ThresholdResult(
                Threshold: Math.Round(threshold, 2),
                DetectionCount: deduplicated.Count,
                TruePositives: result.TruePositives,
                FalsePositives: result.FalsePositives,
                FalseNegatives: result.FalseNegatives,
                Precision: result.Precision,
                Recall: result.Recall,
                F1Score: result.F1Score));
        }

        // Find optimal thresholds for each metric
        var optimalF1 = results.OrderByDescending(r => r.F1Score).First();
        var optimalPrecision = results.OrderByDescending(r => r.Precision).First();
        var optimalRecall = results.OrderByDescending(r => r.Recall).First();

        // Calculate balanced threshold (maximize F1 but with min 50% precision)
        var balanced = results
            .Where(r => r.Precision >= 0.5)
            .OrderByDescending(r => r.F1Score)
            .FirstOrDefault() ?? optimalF1;

        return new ThresholdTuningResult(
            Results: results,
            OptimalForF1: optimalF1,
            OptimalForPrecision: optimalPrecision,
            OptimalForRecall: optimalRecall,
            BalancedOptimal: balanced,
            CurrentThreshold: 0.7); // Default current
    }

    /// <summary>
    /// Generate recommendations based on tuning results.
    /// </summary>
    public static IReadOnlyList<string> GenerateRecommendations(ThresholdTuningResult result, double currentThreshold)
    {
        var recommendations = new List<string>();

        // Compare current to optimal
        var current = result.Results.FirstOrDefault(r => Math.Abs(r.Threshold - currentThreshold) < 0.01);
        if (current == null)
        {
            recommendations.Add($"Current threshold {currentThreshold:F2} was not in the tested range.");
            return recommendations;
        }

        var optimal = result.OptimalForF1;

        if (Math.Abs(current.F1Score - optimal.F1Score) < 0.01)
        {
            recommendations.Add("Current threshold is already optimal for F1 score.");
        }
        else if (optimal.F1Score > current.F1Score)
        {
            var improvement = (optimal.F1Score - current.F1Score) * 100;
            recommendations.Add($"Changing threshold from {currentThreshold:F2} to {optimal.Threshold:F2} could improve F1 by {improvement:F1}%.");
        }

        // Check for precision/recall tradeoff
        if (current.Precision < 0.5)
        {
            var highPrecision = result.Results
                .Where(r => r.Precision >= 0.6)
                .OrderByDescending(r => r.F1Score)
                .FirstOrDefault();

            if (highPrecision != null)
            {
                recommendations.Add($"For better precision (60%+), consider threshold {highPrecision.Threshold:F2} (Precision: {highPrecision.Precision:P0}, Recall: {highPrecision.Recall:P0}).");
            }
        }

        // Check for high recall scenarios
        if (current.Recall < 0.7)
        {
            var highRecall = result.Results
                .Where(r => r.Recall >= 0.8)
                .OrderByDescending(r => r.Precision)
                .FirstOrDefault();

            if (highRecall != null)
            {
                recommendations.Add($"For better recall (80%+), consider threshold {highRecall.Threshold:F2} (Recall: {highRecall.Recall:P0}, Precision: {highRecall.Precision:P0}).");
            }
        }

        return recommendations;
    }
}

/// <summary>
/// Result for a single threshold value.
/// </summary>
public sealed record ThresholdResult(
    double Threshold,
    int DetectionCount,
    int TruePositives,
    int FalsePositives,
    int FalseNegatives,
    double Precision,
    double Recall,
    double F1Score);

/// <summary>
/// Complete threshold tuning results.
/// </summary>
public sealed record ThresholdTuningResult(
    IReadOnlyList<ThresholdResult> Results,
    ThresholdResult OptimalForF1,
    ThresholdResult OptimalForPrecision,
    ThresholdResult OptimalForRecall,
    ThresholdResult BalancedOptimal,
    double CurrentThreshold);

/// <summary>
/// Optimization target for threshold tuning.
/// </summary>
public enum OptimizationTarget
{
    F1,
    Precision,
    Recall,
    Balanced
}
