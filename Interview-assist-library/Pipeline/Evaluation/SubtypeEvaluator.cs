namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Evaluates the accuracy of intent subtype classification.
/// </summary>
public sealed class SubtypeEvaluator
{
    /// <summary>
    /// Evaluate subtype accuracy from matched questions.
    /// </summary>
    public SubtypeEvaluationResult Evaluate(IReadOnlyList<MatchedQuestion> matches)
    {
        var subtypeResults = new Dictionary<string, SubtypeAccuracyMetric>();

        // Group by ground truth subtype
        var byGroundTruth = matches
            .Where(m => !string.IsNullOrEmpty(m.GroundTruth.Subtype))
            .GroupBy(m => m.GroundTruth.Subtype!);

        foreach (var group in byGroundTruth)
        {
            var subtype = group.Key;
            var total = group.Count();
            var correct = group.Count(m =>
                NormalizeSubtype(m.Detected.Subtype) == NormalizeSubtype(subtype));

            var partialCorrect = group.Count(m =>
                IsPartialMatch(m.Detected.Subtype, subtype));

            subtypeResults[subtype] = new SubtypeAccuracyMetric(
                Subtype: subtype,
                TotalCount: total,
                CorrectCount: correct,
                PartialMatchCount: partialCorrect,
                Accuracy: total > 0 ? (double)correct / total : 0.0,
                PartialAccuracy: total > 0 ? (double)partialCorrect / total : 0.0);
        }

        // Calculate overall subtype accuracy
        var totalWithSubtype = matches.Count(m => !string.IsNullOrEmpty(m.GroundTruth.Subtype));
        var totalCorrect = subtypeResults.Values.Sum(s => s.CorrectCount);
        var overallAccuracy = totalWithSubtype > 0 ? (double)totalCorrect / totalWithSubtype : 0.0;

        // Build confusion matrix for subtypes
        var confusionMatrix = BuildSubtypeConfusionMatrix(matches);

        return new SubtypeEvaluationResult(
            SubtypeMetrics: subtypeResults,
            OverallAccuracy: overallAccuracy,
            TotalWithSubtype: totalWithSubtype,
            TotalCorrect: totalCorrect,
            ConfusionMatrix: confusionMatrix);
    }

    /// <summary>
    /// Get detailed breakdown of subtype misclassifications.
    /// </summary>
    public IReadOnlyList<SubtypeMisclassification> GetMisclassifications(IReadOnlyList<MatchedQuestion> matches)
    {
        return matches
            .Where(m => !string.IsNullOrEmpty(m.GroundTruth.Subtype))
            .Where(m => NormalizeSubtype(m.Detected.Subtype) != NormalizeSubtype(m.GroundTruth.Subtype))
            .Select(m => new SubtypeMisclassification(
                QuestionText: m.GroundTruth.Text,
                ExpectedSubtype: m.GroundTruth.Subtype!,
                ActualSubtype: m.Detected.Subtype ?? "None",
                Confidence: m.Detected.Confidence))
            .ToList();
    }

    private static ConfusionMatrix BuildSubtypeConfusionMatrix(IReadOnlyList<MatchedQuestion> matches)
    {
        var matrix = new ConfusionMatrix();

        foreach (var match in matches)
        {
            var actual = NormalizeSubtype(match.GroundTruth.Subtype) ?? "Unknown";
            var predicted = NormalizeSubtype(match.Detected.Subtype) ?? "Unknown";
            matrix.Add(actual, predicted);
        }

        return matrix;
    }

    private static string? NormalizeSubtype(string? subtype)
    {
        if (string.IsNullOrEmpty(subtype))
            return null;

        return subtype.ToLowerInvariant() switch
        {
            "definition" => "Definition",
            "def" => "Definition",
            "howto" => "HowTo",
            "how-to" => "HowTo",
            "how to" => "HowTo",
            "compare" => "Compare",
            "comparison" => "Compare",
            "troubleshoot" => "Troubleshoot",
            "debug" => "Troubleshoot",
            "fix" => "Troubleshoot",
            "rhetorical" => "Rhetorical",
            "clarification" => "Clarification",
            "clarify" => "Clarification",
            "yesno" => "YesNo",
            "yes/no" => "YesNo",
            "opinion" => "Opinion",
            _ => subtype
        };
    }

    private static bool IsPartialMatch(string? detected, string? groundTruth)
    {
        if (string.IsNullOrEmpty(detected) || string.IsNullOrEmpty(groundTruth))
            return false;

        var normalizedDetected = NormalizeSubtype(detected);
        var normalizedGroundTruth = NormalizeSubtype(groundTruth);

        if (normalizedDetected == normalizedGroundTruth)
            return true;

        // Define related subtypes
        var relatedGroups = new[]
        {
            new[] { "Definition", "Clarification" },
            new[] { "HowTo", "Troubleshoot" },
            new[] { "Compare", "Opinion" }
        };

        foreach (var group in relatedGroups)
        {
            if (group.Contains(normalizedDetected) && group.Contains(normalizedGroundTruth))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Accuracy metrics for a specific subtype.
/// </summary>
public sealed record SubtypeAccuracyMetric(
    string Subtype,
    int TotalCount,
    int CorrectCount,
    int PartialMatchCount,
    double Accuracy,
    double PartialAccuracy);

/// <summary>
/// Complete subtype evaluation result.
/// </summary>
public sealed record SubtypeEvaluationResult(
    IReadOnlyDictionary<string, SubtypeAccuracyMetric> SubtypeMetrics,
    double OverallAccuracy,
    int TotalWithSubtype,
    int TotalCorrect,
    ConfusionMatrix ConfusionMatrix);

/// <summary>
/// Details of a subtype misclassification.
/// </summary>
public sealed record SubtypeMisclassification(
    string QuestionText,
    string ExpectedSubtype,
    string ActualSubtype,
    double Confidence);
