using System.Diagnostics.Metrics;
using System.Text;

namespace InterviewAssist.Library.Diagnostics;

/// <summary>
/// Exports Interview Assist metrics in Prometheus text format.
/// Uses MeterListener to collect metrics from the InterviewAssist.Library meter.
/// </summary>
public sealed class PrometheusMetricsExporter : IDisposable
{
    private readonly MeterListener _meterListener;
    private readonly Dictionary<string, double> _counterValues = new();
    private readonly Dictionary<string, double> _gaugeValues = new();
    private readonly Dictionary<string, List<double>> _histogramValues = new();
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new PrometheusMetricsExporter.
    /// </summary>
    public PrometheusMetricsExporter()
    {
        _meterListener = new MeterListener();

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == InterviewAssistMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _meterListener.SetMeasurementEventCallback<long>(OnMeasurementRecorded);
        _meterListener.SetMeasurementEventCallback<double>(OnMeasurementRecorded);

        _meterListener.Start();
    }

    private void OnMeasurementRecorded<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state) where T : struct
    {
        var value = Convert.ToDouble(measurement);
        var name = SanitizeMetricName(instrument.Name);

        lock (_lock)
        {
            if (instrument is Counter<long> or Counter<double>)
            {
                if (!_counterValues.ContainsKey(name))
                    _counterValues[name] = 0;
                _counterValues[name] += value;
            }
            else if (instrument is Histogram<long> or Histogram<double>)
            {
                if (!_histogramValues.ContainsKey(name))
                    _histogramValues[name] = new List<double>();
                _histogramValues[name].Add(value);
            }
            else
            {
                _gaugeValues[name] = value;
            }
        }
    }

    /// <summary>
    /// Gets the current metrics in Prometheus text exposition format.
    /// </summary>
    /// <returns>Metrics in Prometheus text format.</returns>
    public string GetPrometheusMetrics()
    {
        var sb = new StringBuilder();

        lock (_lock)
        {
            // Counters
            foreach (var (name, value) in _counterValues)
            {
                sb.AppendLine($"# TYPE {name} counter");
                sb.AppendLine($"{name} {value}");
            }

            // Gauges
            foreach (var (name, value) in _gaugeValues)
            {
                sb.AppendLine($"# TYPE {name} gauge");
                sb.AppendLine($"{name} {value}");
            }

            // Histograms (simplified - just sum and count)
            foreach (var (name, values) in _histogramValues)
            {
                if (values.Count == 0) continue;

                var sum = values.Sum();
                var count = values.Count;

                sb.AppendLine($"# TYPE {name} histogram");
                sb.AppendLine($"{name}_sum {sum}");
                sb.AppendLine($"{name}_count {count}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Resets all collected metrics to zero.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _counterValues.Clear();
            _gaugeValues.Clear();
            _histogramValues.Clear();
        }
    }

    /// <summary>
    /// Gets a snapshot of counter values.
    /// </summary>
    public IReadOnlyDictionary<string, double> GetCounters()
    {
        lock (_lock)
        {
            return new Dictionary<string, double>(_counterValues);
        }
    }

    /// <summary>
    /// Gets a snapshot of gauge values.
    /// </summary>
    public IReadOnlyDictionary<string, double> GetGauges()
    {
        lock (_lock)
        {
            return new Dictionary<string, double>(_gaugeValues);
        }
    }

    private static string SanitizeMetricName(string name)
    {
        // Prometheus metric names: [a-zA-Z_:][a-zA-Z0-9_:]*
        return name.Replace('.', '_').Replace('-', '_');
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _meterListener.Dispose();
    }
}
