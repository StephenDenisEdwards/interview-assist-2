namespace InterviewAssist.Library.Health;

/// <summary>
/// Interface for health check implementations.
/// </summary>
public interface IHealthCheck
{
    /// <summary>
    /// Gets the name of this health check.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Performs the health check asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The health check result.</returns>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}
