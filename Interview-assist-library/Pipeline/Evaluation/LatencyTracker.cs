using System.Diagnostics;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Tracks and analyzes detection latency metrics.
/// </summary>
public sealed class LatencyTracker
{
    private readonly List<LatencyMeasurement> _measurements = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lock = new();

    /// <summary>
    /// All recorded measurements.
    /// </summary>
    public IReadOnlyList<LatencyMeasurement> Measurements
    {
        get
        {
            lock (_lock)
            {
                return _measurements.ToList();
            }
        }
    }

    /// <summary>
    /// Start tracking from a reference point (e.g., utterance end).
    /// </summary>
    public void StartMeasurement()
    {
        _stopwatch.Restart();
    }

    /// <summary>
    /// Record a detection event with latency.
    /// </summary>
    public void RecordDetection(string strategy, string intentType, double confidence)
    {
        lock (_lock)
        {
            _measurements.Add(new LatencyMeasurement(
                Strategy: strategy,
                IntentType: intentType,
                Confidence: confidence,
                LatencyMs: _stopwatch.ElapsedMilliseconds,
                RecordedAt: DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Record a manual measurement.
    /// </summary>
    public void AddMeasurement(LatencyMeasurement measurement)
    {
        lock (_lock)
        {
            _measurements.Add(measurement);
        }
    }

    /// <summary>
    /// Calculate statistics for all measurements.
    /// </summary>
    public LatencyStatistics GetStatistics()
    {
        lock (_lock)
        {
            if (_measurements.Count == 0)
            {
                return new LatencyStatistics(
                    Count: 0,
                    MinMs: 0,
                    MaxMs: 0,
                    AverageMs: 0,
                    MedianMs: 0,
                    P95Ms: 0,
                    P99Ms: 0,
                    StandardDeviationMs: 0,
                    ByStrategy: new Dictionary<string, StrategyLatencyStats>());
            }

            var latencies = _measurements.Select(m => m.LatencyMs).OrderBy(l => l).ToList();

            var byStrategy = _measurements
                .GroupBy(m => m.Strategy)
                .ToDictionary(
                    g => g.Key,
                    g => CalculateStrategyStats(g.ToList()));

            return new LatencyStatistics(
                Count: _measurements.Count,
                MinMs: latencies.First(),
                MaxMs: latencies.Last(),
                AverageMs: latencies.Average(),
                MedianMs: GetPercentile(latencies, 50),
                P95Ms: GetPercentile(latencies, 95),
                P99Ms: GetPercentile(latencies, 99),
                StandardDeviationMs: CalculateStdDev(latencies),
                ByStrategy: byStrategy);
        }
    }

    /// <summary>
    /// Get statistics for a specific strategy.
    /// </summary>
    public StrategyLatencyStats? GetStrategyStatistics(string strategy)
    {
        lock (_lock)
        {
            var strategyMeasurements = _measurements.Where(m => m.Strategy == strategy).ToList();
            if (strategyMeasurements.Count == 0)
                return null;

            return CalculateStrategyStats(strategyMeasurements);
        }
    }

    /// <summary>
    /// Clear all measurements.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _measurements.Clear();
            _stopwatch.Reset();
        }
    }

    /// <summary>
    /// Extract latency data from recorded events.
    /// </summary>
    public static LatencyStatistics CalculateFromEvents(
        IEnumerable<(long utteranceEndMs, long detectionMs, string strategy)> events)
    {
        var measurements = events
            .Select(e => new LatencyMeasurement(
                Strategy: e.strategy,
                IntentType: "Question",
                Confidence: 0,
                LatencyMs: e.detectionMs - e.utteranceEndMs,
                RecordedAt: DateTime.UtcNow))
            .Where(m => m.LatencyMs >= 0)
            .ToList();

        var tracker = new LatencyTracker();
        foreach (var m in measurements)
        {
            tracker.AddMeasurement(m);
        }

        return tracker.GetStatistics();
    }

    private static StrategyLatencyStats CalculateStrategyStats(List<LatencyMeasurement> measurements)
    {
        var latencies = measurements.Select(m => m.LatencyMs).OrderBy(l => l).ToList();

        return new StrategyLatencyStats(
            Count: measurements.Count,
            MinMs: latencies.First(),
            MaxMs: latencies.Last(),
            AverageMs: latencies.Average(),
            MedianMs: GetPercentile(latencies, 50),
            P95Ms: GetPercentile(latencies, 95));
    }

    private static double GetPercentile(List<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var index = (percentile / 100.0) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        var fraction = index - lower;

        if (upper >= sortedValues.Count) upper = sortedValues.Count - 1;

        return sortedValues[lower] * (1 - fraction) + sortedValues[upper] * fraction;
    }

    private static double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2) return 0;

        var avg = values.Average();
        var sumSquaredDiff = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumSquaredDiff / (values.Count - 1));
    }
}

/// <summary>
/// A single latency measurement.
/// </summary>
public sealed record LatencyMeasurement(
    string Strategy,
    string IntentType,
    double Confidence,
    double LatencyMs,
    DateTime RecordedAt);

/// <summary>
/// Overall latency statistics.
/// </summary>
public sealed record LatencyStatistics(
    int Count,
    double MinMs,
    double MaxMs,
    double AverageMs,
    double MedianMs,
    double P95Ms,
    double P99Ms,
    double StandardDeviationMs,
    IReadOnlyDictionary<string, StrategyLatencyStats> ByStrategy);

/// <summary>
/// Latency statistics for a specific strategy.
/// </summary>
public sealed record StrategyLatencyStats(
    int Count,
    double MinMs,
    double MaxMs,
    double AverageMs,
    double MedianMs,
    double P95Ms);
