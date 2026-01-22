namespace InterviewAssist.Library.Health;

/// <summary>
/// Represents the health status of a component.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// The component is healthy and functioning normally.
    /// </summary>
    Healthy,

    /// <summary>
    /// The component is experiencing degraded performance but still functional.
    /// </summary>
    Degraded,

    /// <summary>
    /// The component is unhealthy and not functioning properly.
    /// </summary>
    Unhealthy
}
