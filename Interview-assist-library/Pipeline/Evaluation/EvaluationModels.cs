namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// A question extracted by LLM from the full transcript (ground truth).
/// </summary>
public sealed record ExtractedQuestion(
    string Text,
    string? Subtype,
    double Confidence,
    int ApproximatePosition);

/// <summary>
/// Result of comparing detected questions against ground truth.
/// </summary>
public sealed record EvaluationResult
{
    public required int TruePositives { get; init; }
    public required int FalsePositives { get; init; }
    public required int FalseNegatives { get; init; }

    public double Precision => TruePositives + FalsePositives > 0
        ? (double)TruePositives / (TruePositives + FalsePositives)
        : 0.0;

    public double Recall => TruePositives + FalseNegatives > 0
        ? (double)TruePositives / (TruePositives + FalseNegatives)
        : 0.0;

    public double F1Score => Precision + Recall > 0
        ? 2 * Precision * Recall / (Precision + Recall)
        : 0.0;

    public required IReadOnlyList<MatchedQuestion> Matches { get; init; }
    public required IReadOnlyList<ExtractedQuestion> Missed { get; init; }
    public required IReadOnlyList<DetectedQuestionInfo> FalseAlarms { get; init; }

    /// <summary>
    /// Confusion matrix for type classification (optional, populated by extended evaluation).
    /// </summary>
    public ConfusionMatrix? TypeConfusionMatrix { get; init; }

    /// <summary>
    /// Error pattern analysis for false positives (optional).
    /// </summary>
    public ErrorAnalysisResult? ErrorAnalysis { get; init; }

    /// <summary>
    /// Analysis of missed questions (optional).
    /// </summary>
    public MissedQuestionAnalysis? MissedAnalysis { get; init; }

    /// <summary>
    /// Per-subtype accuracy metrics (optional).
    /// </summary>
    public IReadOnlyDictionary<string, SubtypeMetrics>? SubtypeMetrics { get; init; }
}

/// <summary>
/// Metrics for a specific question subtype.
/// </summary>
public sealed record SubtypeMetrics(
    string Subtype,
    int CorrectCount,
    int TotalCount,
    double Accuracy);

/// <summary>
/// A matched pair of ground truth and detected question.
/// </summary>
public sealed record MatchedQuestion(
    ExtractedQuestion GroundTruth,
    DetectedQuestionInfo Detected,
    double SimilarityScore);

/// <summary>
/// Simplified info about a detected question for reporting.
/// </summary>
public sealed record DetectedQuestionInfo(
    string Text,
    string? Subtype,
    double Confidence,
    string UtteranceId,
    long OffsetMs);

/// <summary>
/// Configuration for evaluation.
/// </summary>
public sealed record EvaluationOptions
{
    /// <summary>
    /// OpenAI API key for ground truth extraction.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Model to use for ground truth extraction (default: gpt-4o).
    /// </summary>
    public string Model { get; init; } = "gpt-4o";

    /// <summary>
    /// Minimum similarity score (0-1) to consider a match (default: 0.7).
    /// </summary>
    public double MatchThreshold { get; init; } = 0.7;

    /// <summary>
    /// Minimum similarity to consider questions as duplicates (default: 0.8).
    /// </summary>
    public double DeduplicationThreshold { get; init; } = 0.8;

    /// <summary>
    /// Output folder for evaluation reports.
    /// </summary>
    public string OutputFolder { get; init; } = "evaluations";
}
