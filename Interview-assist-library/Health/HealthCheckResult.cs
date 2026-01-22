namespace InterviewAssist.Library.Health;

/// <summary>
/// Represents the result of a health check.
/// </summary>
/// <param name="Name">The name of the health check.</param>
/// <param name="Status">The health status.</param>
/// <param name="Description">Optional description of the status.</param>
/// <param name="Data">Additional diagnostic data.</param>
/// <param name="Duration">Time taken to perform the health check.</param>
public record HealthCheckResult(
    string Name,
    HealthStatus Status,
    string? Description = null,
    IReadOnlyDictionary<string, object>? Data = null,
    TimeSpan? Duration = null)
{
    /// <summary>
    /// Creates a healthy result.
    /// </summary>
    public static HealthCheckResult Healthy(string name, string? description = null, IReadOnlyDictionary<string, object>? data = null)
        => new(name, HealthStatus.Healthy, description, data);

    /// <summary>
    /// Creates a degraded result.
    /// </summary>
    public static HealthCheckResult Degraded(string name, string? description = null, IReadOnlyDictionary<string, object>? data = null)
        => new(name, HealthStatus.Degraded, description, data);

    /// <summary>
    /// Creates an unhealthy result.
    /// </summary>
    public static HealthCheckResult Unhealthy(string name, string? description = null, IReadOnlyDictionary<string, object>? data = null)
        => new(name, HealthStatus.Unhealthy, description, data);
}
