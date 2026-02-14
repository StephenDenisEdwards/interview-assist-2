using System.Text;
using System.Text.Json;
using InterviewAssist.Library.Utilities;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Generates comprehensive evaluation reports combining all metrics.
/// </summary>
public sealed class ComprehensiveReport
{
    /// <summary>
    /// Generate a full report from evaluation components.
    /// </summary>
    public static ComprehensiveReportData Generate(
        string sessionFile,
        EvaluationResult evaluation,
        ErrorAnalysisResult? errorAnalysis = null,
        MissedQuestionAnalysis? missedAnalysis = null,
        SubtypeEvaluationResult? subtypeResult = null,
        ThresholdTuningResult? thresholdResult = null,
        StrategyComparisonResult? strategyComparison = null,
        LatencyStatistics? latencyStats = null,
        RegressionTestResult? regressionResult = null)
    {
        return new ComprehensiveReportData
        {
            GeneratedAt = DateTime.UtcNow,
            SessionFile = Path.GetFileName(sessionFile),
            Summary = GenerateSummary(evaluation, regressionResult),
            Metrics = new MetricsSummary
            {
                TruePositives = evaluation.TruePositives,
                FalsePositives = evaluation.FalsePositives,
                FalseNegatives = evaluation.FalseNegatives,
                Precision = evaluation.Precision,
                Recall = evaluation.Recall,
                F1Score = evaluation.F1Score
            },
            ErrorAnalysis = errorAnalysis,
            MissedAnalysis = missedAnalysis,
            SubtypeAccuracy = subtypeResult,
            ThresholdTuning = thresholdResult,
            StrategyComparison = strategyComparison,
            LatencyStats = latencyStats,
            RegressionTest = regressionResult
        };
    }

    /// <summary>
    /// Export report to JSON file.
    /// </summary>
    public static async Task ExportJsonAsync(
        ComprehensiveReportData report,
        string filePath,
        CancellationToken ct = default)
    {
        var options = PipelineJsonOptions.CamelCasePretty;

        var json = JsonSerializer.Serialize(report, options);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(filePath, json, ct);
    }

    /// <summary>
    /// Generate a text summary for console output.
    /// </summary>
    public static string GenerateTextSummary(ComprehensiveReportData report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("                 COMPREHENSIVE EVALUATION REPORT                ");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Session: {report.SessionFile}");
        sb.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Summary
        sb.AppendLine("─── SUMMARY ───────────────────────────────────────────────────");
        sb.AppendLine(report.Summary);
        sb.AppendLine();

        // Core Metrics
        sb.AppendLine("─── CORE METRICS ──────────────────────────────────────────────");
        sb.AppendLine($"  Precision:       {report.Metrics.Precision:P1}");
        sb.AppendLine($"  Recall:          {report.Metrics.Recall:P1}");
        sb.AppendLine($"  F1 Score:        {report.Metrics.F1Score:P1}");
        sb.AppendLine($"  True Positives:  {report.Metrics.TruePositives}");
        sb.AppendLine($"  False Positives: {report.Metrics.FalsePositives}");
        sb.AppendLine($"  False Negatives: {report.Metrics.FalseNegatives}");
        sb.AppendLine();

        // Subtype Accuracy
        if (report.SubtypeAccuracy != null)
        {
            sb.AppendLine("─── SUBTYPE ACCURACY ──────────────────────────────────────────");
            sb.AppendLine($"  Overall: {report.SubtypeAccuracy.OverallAccuracy:P1}");
            foreach (var (subtype, metric) in report.SubtypeAccuracy.SubtypeMetrics)
            {
                sb.AppendLine($"    {subtype,-12}: {metric.Accuracy:P0} ({metric.CorrectCount}/{metric.TotalCount})");
            }
            sb.AppendLine();
        }

        // Error Analysis
        if (report.ErrorAnalysis != null && report.ErrorAnalysis.PatternCounts.Count > 0)
        {
            sb.AppendLine("─── ERROR PATTERNS ────────────────────────────────────────────");
            foreach (var pattern in report.ErrorAnalysis.PatternCounts.Take(5))
            {
                sb.AppendLine($"  {pattern.Pattern.Name,-25}: {pattern.Count}");
            }
            sb.AppendLine();
        }

        // Threshold Recommendation
        if (report.ThresholdTuning != null)
        {
            sb.AppendLine("─── THRESHOLD RECOMMENDATION ──────────────────────────────────");
            sb.AppendLine($"  Optimal for F1: {report.ThresholdTuning.OptimalForF1.Threshold:F2}");
            sb.AppendLine($"    F1: {report.ThresholdTuning.OptimalForF1.F1Score:P1}");
            sb.AppendLine($"    Precision: {report.ThresholdTuning.OptimalForF1.Precision:P1}");
            sb.AppendLine($"    Recall: {report.ThresholdTuning.OptimalForF1.Recall:P1}");
            sb.AppendLine();
        }

        // Strategy Comparison
        if (report.StrategyComparison != null)
        {
            sb.AppendLine("─── STRATEGY COMPARISON ───────────────────────────────────────");
            foreach (var strategy in report.StrategyComparison.Results)
            {
                var marker = strategy.StrategyName == report.StrategyComparison.BestForF1 ? " *" : "";
                sb.AppendLine($"  {strategy.StrategyName,-10}: F1={strategy.F1Score:P0}, P={strategy.Precision:P0}, R={strategy.Recall:P0}{marker}");
            }
            sb.AppendLine();
        }

        // Latency
        if (report.LatencyStats != null && report.LatencyStats.Count > 0)
        {
            sb.AppendLine("─── LATENCY STATISTICS ────────────────────────────────────────");
            sb.AppendLine($"  Average: {report.LatencyStats.AverageMs:F0}ms");
            sb.AppendLine($"  Median:  {report.LatencyStats.MedianMs:F0}ms");
            sb.AppendLine($"  P95:     {report.LatencyStats.P95Ms:F0}ms");
            sb.AppendLine();
        }

        // Regression Status
        if (report.RegressionTest != null)
        {
            sb.AppendLine("─── REGRESSION STATUS ─────────────────────────────────────────");
            var status = report.RegressionTest.OverallStatus switch
            {
                RegressionStatus.Passed => "PASSED",
                RegressionStatus.Failed => "FAILED",
                RegressionStatus.Improved => "IMPROVED",
                _ => "UNKNOWN"
            };
            sb.AppendLine($"  Status: {status}");
            sb.AppendLine($"  Baseline: {report.RegressionTest.BaselineVersion}");
            sb.AppendLine();
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    private static string GenerateSummary(EvaluationResult evaluation, RegressionTestResult? regression)
    {
        var sb = new StringBuilder();

        // Overall quality assessment
        var quality = evaluation.F1Score switch
        {
            >= 0.8 => "Excellent",
            >= 0.6 => "Good",
            >= 0.4 => "Fair",
            _ => "Needs Improvement"
        };

        sb.AppendLine($"Overall Quality: {quality} (F1: {evaluation.F1Score:P1})");

        // Key issues
        if (evaluation.Precision < 0.5)
        {
            sb.AppendLine($"Issue: Low precision ({evaluation.Precision:P1}) - too many false positives");
        }

        if (evaluation.Recall < 0.5)
        {
            sb.AppendLine($"Issue: Low recall ({evaluation.Recall:P1}) - missing too many questions");
        }

        // Regression status
        if (regression != null)
        {
            var regStatus = regression.OverallStatus switch
            {
                RegressionStatus.Failed => "REGRESSION DETECTED",
                RegressionStatus.Improved => "Quality improved",
                _ => "No regression"
            };
            sb.AppendLine($"Regression: {regStatus}");
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Data for a comprehensive report.
/// </summary>
public sealed class ComprehensiveReportData
{
    public DateTime GeneratedAt { get; init; }
    public string SessionFile { get; init; } = "";
    public string Summary { get; init; } = "";
    public MetricsSummary Metrics { get; init; } = new();
    public ErrorAnalysisResult? ErrorAnalysis { get; init; }
    public MissedQuestionAnalysis? MissedAnalysis { get; init; }
    public SubtypeEvaluationResult? SubtypeAccuracy { get; init; }
    public ThresholdTuningResult? ThresholdTuning { get; init; }
    public StrategyComparisonResult? StrategyComparison { get; init; }
    public LatencyStatistics? LatencyStats { get; init; }
    public RegressionTestResult? RegressionTest { get; init; }
}

/// <summary>
/// Summary of core metrics.
/// </summary>
public sealed class MetricsSummary
{
    public int TruePositives { get; init; }
    public int FalsePositives { get; init; }
    public int FalseNegatives { get; init; }
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1Score { get; init; }
}
