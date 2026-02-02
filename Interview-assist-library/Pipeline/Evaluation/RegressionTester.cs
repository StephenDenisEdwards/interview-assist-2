using System.Text.Json;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Tests for quality regressions by comparing against baseline metrics.
/// </summary>
public sealed class RegressionTester
{
    /// <summary>
    /// Load a baseline from file.
    /// </summary>
    public static async Task<Baseline> LoadBaselineAsync(string filePath, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        return JsonSerializer.Deserialize<Baseline>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse baseline file");
    }

    /// <summary>
    /// Save a baseline to file.
    /// </summary>
    public static async Task SaveBaselineAsync(string filePath, Baseline baseline, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true });

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(filePath, json, ct);
    }

    /// <summary>
    /// Create a baseline from evaluation results.
    /// </summary>
    public static Baseline CreateBaseline(
        string version,
        EvaluationResult result,
        double precisionThreshold = 0.05,
        double recallThreshold = 0.05,
        double f1Threshold = 0.05)
    {
        return new Baseline
        {
            Version = version,
            CreatedAt = DateTime.UtcNow,
            Metrics = new BaselineMetrics
            {
                Precision = result.Precision,
                Recall = result.Recall,
                F1Score = result.F1Score,
                TruePositives = result.TruePositives,
                FalsePositives = result.FalsePositives,
                FalseNegatives = result.FalseNegatives
            },
            Thresholds = new RegressionThresholds
            {
                PrecisionMin = Math.Max(0, result.Precision - precisionThreshold),
                RecallMin = Math.Max(0, result.Recall - recallThreshold),
                F1Min = Math.Max(0, result.F1Score - f1Threshold)
            }
        };
    }

    /// <summary>
    /// Test current results against baseline for regressions.
    /// </summary>
    public RegressionTestResult Test(Baseline baseline, EvaluationResult current)
    {
        var comparisons = new List<MetricComparison>
        {
            CompareMetric("Precision", baseline.Metrics.Precision, current.Precision, baseline.Thresholds.PrecisionMin),
            CompareMetric("Recall", baseline.Metrics.Recall, current.Recall, baseline.Thresholds.RecallMin),
            CompareMetric("F1 Score", baseline.Metrics.F1Score, current.F1Score, baseline.Thresholds.F1Min)
        };

        var hasRegression = comparisons.Any(c => c.Status == ComparisonStatus.Regression);
        var hasImprovement = comparisons.Any(c => c.Status == ComparisonStatus.Improvement);

        var overallStatus = hasRegression ? RegressionStatus.Failed :
                            hasImprovement ? RegressionStatus.Improved :
                            RegressionStatus.Passed;

        return new RegressionTestResult(
            BaselineVersion: baseline.Version,
            BaselineCreatedAt: baseline.CreatedAt,
            TestedAt: DateTime.UtcNow,
            OverallStatus: overallStatus,
            Comparisons: comparisons,
            BaselineMetrics: baseline.Metrics,
            CurrentMetrics: new BaselineMetrics
            {
                Precision = current.Precision,
                Recall = current.Recall,
                F1Score = current.F1Score,
                TruePositives = current.TruePositives,
                FalsePositives = current.FalsePositives,
                FalseNegatives = current.FalseNegatives
            });
    }

    private static MetricComparison CompareMetric(string name, double baseline, double current, double minThreshold)
    {
        var delta = current - baseline;
        var deltaPercent = baseline > 0 ? delta / baseline * 100 : 0;

        var status = current < minThreshold ? ComparisonStatus.Regression :
                     delta > 0.02 ? ComparisonStatus.Improvement :
                     ComparisonStatus.Unchanged;

        return new MetricComparison(
            MetricName: name,
            BaselineValue: baseline,
            CurrentValue: current,
            Delta: delta,
            DeltaPercent: deltaPercent,
            MinThreshold: minThreshold,
            Status: status);
    }
}

/// <summary>
/// Baseline metrics for regression testing.
/// </summary>
public sealed class Baseline
{
    public string Version { get; init; } = "1.0";
    public DateTime CreatedAt { get; init; }
    public BaselineMetrics Metrics { get; init; } = new();
    public RegressionThresholds Thresholds { get; init; } = new();
    public string? Description { get; init; }
    public string? TestDataFile { get; init; }
}

/// <summary>
/// Metric values in the baseline.
/// </summary>
public sealed class BaselineMetrics
{
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1Score { get; init; }
    public int TruePositives { get; init; }
    public int FalsePositives { get; init; }
    public int FalseNegatives { get; init; }
}

/// <summary>
/// Minimum thresholds for regression detection.
/// </summary>
public sealed class RegressionThresholds
{
    public double PrecisionMin { get; init; }
    public double RecallMin { get; init; }
    public double F1Min { get; init; }
}

/// <summary>
/// Result of a regression test.
/// </summary>
public sealed record RegressionTestResult(
    string BaselineVersion,
    DateTime BaselineCreatedAt,
    DateTime TestedAt,
    RegressionStatus OverallStatus,
    IReadOnlyList<MetricComparison> Comparisons,
    BaselineMetrics BaselineMetrics,
    BaselineMetrics CurrentMetrics);

/// <summary>
/// Comparison of a single metric.
/// </summary>
public sealed record MetricComparison(
    string MetricName,
    double BaselineValue,
    double CurrentValue,
    double Delta,
    double DeltaPercent,
    double MinThreshold,
    ComparisonStatus Status);

/// <summary>
/// Overall regression test status.
/// </summary>
public enum RegressionStatus
{
    Passed,
    Failed,
    Improved
}

/// <summary>
/// Status of a single metric comparison.
/// </summary>
public enum ComparisonStatus
{
    Unchanged,
    Improvement,
    Regression
}
