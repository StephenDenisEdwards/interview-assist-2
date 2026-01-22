namespace InterviewAssist.Library.Diagnostics;

/// <summary>
/// Provides correlation ID context for distributed tracing and logging.
/// Uses AsyncLocal to flow context across async operations.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> s_correlationId = new();

    /// <summary>
    /// Gets the current correlation ID, or null if not set.
    /// </summary>
    public static string? Current => s_correlationId.Value;

    /// <summary>
    /// Gets the current correlation ID, or creates a new one if not set.
    /// </summary>
    /// <returns>The current or newly generated correlation ID.</returns>
    public static string GetOrCreate()
    {
        if (string.IsNullOrEmpty(s_correlationId.Value))
        {
            s_correlationId.Value = GenerateId();
        }
        return s_correlationId.Value;
    }

    /// <summary>
    /// Sets the correlation ID for the current async context.
    /// </summary>
    /// <param name="correlationId">The correlation ID to set.</param>
    public static void Set(string correlationId)
    {
        s_correlationId.Value = correlationId;
    }

    /// <summary>
    /// Clears the correlation ID for the current async context.
    /// </summary>
    public static void Clear()
    {
        s_correlationId.Value = null;
    }

    /// <summary>
    /// Creates a new scope with a correlation ID. The previous context is restored when disposed.
    /// </summary>
    /// <param name="correlationId">Optional correlation ID. If null, a new ID is generated.</param>
    /// <returns>An IDisposable that restores the previous context when disposed.</returns>
    public static IDisposable BeginScope(string? correlationId = null)
    {
        return new CorrelationScope(correlationId ?? GenerateId());
    }

    /// <summary>
    /// Generates an 8-character correlation ID.
    /// </summary>
    /// <returns>A new correlation ID.</returns>
    public static string GenerateId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    private sealed class CorrelationScope : IDisposable
    {
        private readonly string? _previousCorrelationId;
        private bool _disposed;

        public CorrelationScope(string correlationId)
        {
            _previousCorrelationId = s_correlationId.Value;
            s_correlationId.Value = correlationId;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            s_correlationId.Value = _previousCorrelationId;
        }
    }
}
