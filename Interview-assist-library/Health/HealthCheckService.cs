using System.Diagnostics;

namespace InterviewAssist.Library.Health;

/// <summary>
/// Service that aggregates multiple health checks.
/// </summary>
public class HealthCheckService
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;

    /// <summary>
    /// Creates a new instance of HealthCheckService.
    /// </summary>
    /// <param name="healthChecks">The health checks to aggregate.</param>
    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks)
    {
        _healthChecks = healthChecks ?? throw new ArgumentNullException(nameof(healthChecks));
    }

    /// <summary>
    /// Runs all health checks and returns the aggregated results.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated health check results.</returns>
    public async Task<HealthCheckReport> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<HealthCheckResult>();

        foreach (var check in _healthChecks)
        {
            try
            {
                var result = await check.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
                results.Add(result);
            }
            catch (Exception ex)
            {
                results.Add(new HealthCheckResult(
                    check.Name,
                    HealthStatus.Unhealthy,
                    $"Health check threw exception: {ex.Message}",
                    new Dictionary<string, object>
                    {
                        ["ExceptionType"] = ex.GetType().Name,
                        ["ExceptionMessage"] = ex.Message
                    }));
            }
        }

        sw.Stop();

        var overallStatus = DetermineOverallStatus(results);

        return new HealthCheckReport(overallStatus, results, sw.Elapsed);
    }

    private static HealthStatus DetermineOverallStatus(IReadOnlyList<HealthCheckResult> results)
    {
        if (results.Count == 0)
            return HealthStatus.Healthy;

        if (results.Any(r => r.Status == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;

        if (results.Any(r => r.Status == HealthStatus.Degraded))
            return HealthStatus.Degraded;

        return HealthStatus.Healthy;
    }
}

/// <summary>
/// Represents an aggregated health check report.
/// </summary>
/// <param name="Status">The overall health status.</param>
/// <param name="Results">Individual health check results.</param>
/// <param name="TotalDuration">Total time taken for all checks.</param>
public record HealthCheckReport(
    HealthStatus Status,
    IReadOnlyList<HealthCheckResult> Results,
    TimeSpan TotalDuration);
