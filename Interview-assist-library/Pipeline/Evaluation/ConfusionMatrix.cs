using System.Text;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Tracks classification predictions vs actuals for evaluation.
/// </summary>
public sealed class ConfusionMatrix
{
    private readonly Dictionary<(string Actual, string Predicted), int> _matrix = new();
    private readonly HashSet<string> _labels = new();

    /// <summary>
    /// The raw confusion matrix data.
    /// </summary>
    public IReadOnlyDictionary<(string Actual, string Predicted), int> Matrix => _matrix;

    /// <summary>
    /// All unique labels seen.
    /// </summary>
    public IReadOnlySet<string> Labels => _labels;

    /// <summary>
    /// Add a classification result.
    /// </summary>
    public void Add(string actual, string predicted)
    {
        var key = (actual, predicted);
        _matrix.TryGetValue(key, out var count);
        _matrix[key] = count + 1;

        _labels.Add(actual);
        _labels.Add(predicted);
    }

    /// <summary>
    /// Get count for a specific actual/predicted combination.
    /// </summary>
    public int GetCount(string actual, string predicted)
    {
        return _matrix.GetValueOrDefault((actual, predicted), 0);
    }

    /// <summary>
    /// Get total count of all entries.
    /// </summary>
    public int TotalCount => _matrix.Values.Sum();

    /// <summary>
    /// Get accuracy for a specific class (true positives / total actual).
    /// </summary>
    public double GetClassAccuracy(string label)
    {
        var truePositives = GetCount(label, label);
        var totalActual = _matrix
            .Where(kvp => kvp.Key.Actual == label)
            .Sum(kvp => kvp.Value);

        return totalActual > 0 ? (double)truePositives / totalActual : 0.0;
    }

    /// <summary>
    /// Get precision for a specific class (TP / (TP + FP)).
    /// </summary>
    public double GetPrecision(string label)
    {
        var truePositives = GetCount(label, label);
        var totalPredicted = _matrix
            .Where(kvp => kvp.Key.Predicted == label)
            .Sum(kvp => kvp.Value);

        return totalPredicted > 0 ? (double)truePositives / totalPredicted : 0.0;
    }

    /// <summary>
    /// Get recall for a specific class (TP / (TP + FN)).
    /// </summary>
    public double GetRecall(string label)
    {
        var truePositives = GetCount(label, label);
        var totalActual = _matrix
            .Where(kvp => kvp.Key.Actual == label)
            .Sum(kvp => kvp.Value);

        return totalActual > 0 ? (double)truePositives / totalActual : 0.0;
    }

    /// <summary>
    /// Get F1 score for a specific class.
    /// </summary>
    public double GetF1Score(string label)
    {
        var precision = GetPrecision(label);
        var recall = GetRecall(label);

        return precision + recall > 0
            ? 2 * precision * recall / (precision + recall)
            : 0.0;
    }

    /// <summary>
    /// Get overall accuracy (correct / total).
    /// </summary>
    public double OverallAccuracy
    {
        get
        {
            var correct = _labels.Sum(label => GetCount(label, label));
            var total = TotalCount;
            return total > 0 ? (double)correct / total : 0.0;
        }
    }

    /// <summary>
    /// Get per-class metrics.
    /// </summary>
    public IReadOnlyDictionary<string, ClassMetrics> GetPerClassMetrics()
    {
        return _labels.ToDictionary(
            label => label,
            label => new ClassMetrics(
                Label: label,
                TruePositives: GetCount(label, label),
                TotalActual: _matrix.Where(kvp => kvp.Key.Actual == label).Sum(kvp => kvp.Value),
                TotalPredicted: _matrix.Where(kvp => kvp.Key.Predicted == label).Sum(kvp => kvp.Value),
                Precision: GetPrecision(label),
                Recall: GetRecall(label),
                F1Score: GetF1Score(label)));
    }

    /// <summary>
    /// Format the confusion matrix as a string table.
    /// </summary>
    public string ToFormattedString()
    {
        var sb = new StringBuilder();
        var sortedLabels = _labels.OrderBy(l => l).ToList();

        // Header row
        var maxLabelLen = Math.Max(10, sortedLabels.Max(l => l.Length));
        sb.Append("".PadRight(maxLabelLen + 2));
        sb.Append("| Predicted".PadRight(maxLabelLen * sortedLabels.Count + sortedLabels.Count * 3));
        sb.AppendLine();

        sb.Append("Actual".PadRight(maxLabelLen + 2));
        foreach (var label in sortedLabels)
        {
            sb.Append($"| {label.PadRight(maxLabelLen)} ");
        }
        sb.AppendLine();

        // Separator
        sb.AppendLine(new string('-', maxLabelLen + 2 + (maxLabelLen + 3) * sortedLabels.Count));

        // Data rows
        foreach (var actualLabel in sortedLabels)
        {
            sb.Append(actualLabel.PadRight(maxLabelLen + 2));
            foreach (var predictedLabel in sortedLabels)
            {
                var count = GetCount(actualLabel, predictedLabel);
                sb.Append($"| {count.ToString().PadRight(maxLabelLen)} ");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// Metrics for a single class.
/// </summary>
public sealed record ClassMetrics(
    string Label,
    int TruePositives,
    int TotalActual,
    int TotalPredicted,
    double Precision,
    double Recall,
    double F1Score);
