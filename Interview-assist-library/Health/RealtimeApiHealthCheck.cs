using System.Diagnostics;
using InterviewAssist.Library.Realtime;

namespace InterviewAssist.Library.Health;

/// <summary>
/// Health check for the OpenAI Realtime API connection.
/// </summary>
public class RealtimeApiHealthCheck : IHealthCheck
{
    private readonly IRealtimeApi _realtimeApi;

    /// <summary>
    /// Creates a new instance of RealtimeApiHealthCheck.
    /// </summary>
    /// <param name="realtimeApi">The realtime API instance to check.</param>
    public RealtimeApiHealthCheck(IRealtimeApi realtimeApi)
    {
        _realtimeApi = realtimeApi ?? throw new ArgumentNullException(nameof(realtimeApi));
    }

    /// <inheritdoc />
    public string Name => "RealtimeApi";

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var data = new Dictionary<string, object>
        {
            ["IsConnected"] = _realtimeApi.IsConnected
        };

        // Add correlation ID if available
        if (_realtimeApi is OpenAiRealtimeApi openAiApi && !string.IsNullOrEmpty(openAiApi.CorrelationId))
        {
            data["CorrelationId"] = openAiApi.CorrelationId;
        }

        sw.Stop();

        if (_realtimeApi.IsConnected)
        {
            return Task.FromResult(new HealthCheckResult(
                Name,
                HealthStatus.Healthy,
                "WebSocket connection is active",
                data,
                sw.Elapsed));
        }

        return Task.FromResult(new HealthCheckResult(
            Name,
            HealthStatus.Unhealthy,
            "WebSocket connection is not active",
            data,
            sw.Elapsed));
    }
}
