using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace InterviewAssist.Library.Resilience;

/// <summary>
/// Provides Polly-based resilience policies for HTTP operations.
/// </summary>
public static class PollyPolicies
{
    /// <summary>
    /// Default maximum retry attempts.
    /// </summary>
    public const int DefaultMaxRetries = 3;

    /// <summary>
    /// Default base delay for exponential backoff in milliseconds.
    /// </summary>
    public const int DefaultBaseDelayMs = 1000;

    /// <summary>
    /// Default number of exceptions allowed before circuit breaker opens.
    /// </summary>
    public const int DefaultExceptionsBeforeBreaking = 5;

    /// <summary>
    /// Default duration for circuit breaker to stay open in seconds.
    /// </summary>
    public const int DefaultBreakDurationSeconds = 30;

    /// <summary>
    /// Creates a retry policy for transient HTTP errors with exponential backoff.
    /// Handles HTTP 429 (Too Many Requests), 5xx errors, and network exceptions.
    /// </summary>
    /// <param name="logger">Optional logger for policy events.</param>
    /// <param name="maxRetries">Maximum number of retry attempts.</param>
    /// <param name="baseDelayMs">Base delay for exponential backoff.</param>
    /// <returns>An async retry policy for HttpResponseMessage.</returns>
    public static AsyncRetryPolicy<HttpResponseMessage> GetTransientRetryPolicy(
        ILogger? logger = null,
        int maxRetries = DefaultMaxRetries,
        int baseDelayMs = DefaultBaseDelayMs)
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(IsTransientError)
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt =>
                {
                    // Exponential backoff: baseDelay * 2^(attempt-1) with jitter
                    var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, retryAttempt - 1));
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100));
                    return delay + jitter;
                },
                onRetry: (outcome, delay, retryAttempt, context) =>
                {
                    var message = outcome.Exception?.Message ?? $"Status: {outcome.Result?.StatusCode}";
                    logger?.LogWarning(
                        "Retry {Attempt}/{Max} after {Delay}ms. Reason: {Reason}",
                        retryAttempt, maxRetries, delay.TotalMilliseconds, message);
                });
    }

    /// <summary>
    /// Creates a retry policy for generic transient exceptions with exponential backoff.
    /// </summary>
    /// <typeparam name="TException">The exception type to handle.</typeparam>
    /// <param name="logger">Optional logger for policy events.</param>
    /// <param name="maxRetries">Maximum number of retry attempts.</param>
    /// <param name="baseDelayMs">Base delay for exponential backoff.</param>
    /// <returns>An async retry policy.</returns>
    public static AsyncRetryPolicy GetExceptionRetryPolicy<TException>(
        ILogger? logger = null,
        int maxRetries = DefaultMaxRetries,
        int baseDelayMs = DefaultBaseDelayMs) where TException : Exception
    {
        return Policy
            .Handle<TException>()
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt =>
                {
                    var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, retryAttempt - 1));
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100));
                    return delay + jitter;
                },
                onRetry: (exception, delay, retryAttempt, context) =>
                {
                    logger?.LogWarning(
                        exception,
                        "Retry {Attempt}/{Max} after {Delay}ms due to {ExceptionType}",
                        retryAttempt, maxRetries, delay.TotalMilliseconds, exception.GetType().Name);
                });
    }

    /// <summary>
    /// Creates a circuit breaker policy for HTTP operations.
    /// Opens the circuit after consecutive failures to prevent further requests.
    /// </summary>
    /// <param name="logger">Optional logger for policy events.</param>
    /// <param name="exceptionsBeforeBreaking">Number of exceptions before circuit opens.</param>
    /// <param name="breakDurationSeconds">Duration circuit stays open.</param>
    /// <returns>An async circuit breaker policy for HttpResponseMessage.</returns>
    public static AsyncCircuitBreakerPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        ILogger? logger = null,
        int exceptionsBeforeBreaking = DefaultExceptionsBeforeBreaking,
        int breakDurationSeconds = DefaultBreakDurationSeconds)
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(IsTransientError)
            .CircuitBreakerAsync(
                exceptionsBeforeBreaking,
                TimeSpan.FromSeconds(breakDurationSeconds),
                onBreak: (outcome, duration) =>
                {
                    var reason = outcome.Exception?.Message ?? $"Status: {outcome.Result?.StatusCode}";
                    logger?.LogWarning(
                        "Circuit breaker opened for {Duration}s. Reason: {Reason}",
                        duration.TotalSeconds, reason);
                },
                onReset: () =>
                {
                    logger?.LogInformation("Circuit breaker reset to closed state");
                },
                onHalfOpen: () =>
                {
                    logger?.LogInformation("Circuit breaker half-open, testing request");
                });
    }

    /// <summary>
    /// Creates a wrapped policy combining retry with circuit breaker.
    /// Retry is executed inside the circuit breaker.
    /// </summary>
    /// <param name="logger">Optional logger for policy events.</param>
    /// <param name="maxRetries">Maximum retry attempts.</param>
    /// <param name="baseDelayMs">Base delay for exponential backoff.</param>
    /// <param name="exceptionsBeforeBreaking">Exceptions before circuit opens.</param>
    /// <param name="breakDurationSeconds">Duration circuit stays open.</param>
    /// <returns>A wrapped async policy combining retry and circuit breaker.</returns>
    public static AsyncPolicy<HttpResponseMessage> GetResiliencePolicy(
        ILogger? logger = null,
        int maxRetries = DefaultMaxRetries,
        int baseDelayMs = DefaultBaseDelayMs,
        int exceptionsBeforeBreaking = DefaultExceptionsBeforeBreaking,
        int breakDurationSeconds = DefaultBreakDurationSeconds)
    {
        var retryPolicy = GetTransientRetryPolicy(logger, maxRetries, baseDelayMs);
        var circuitBreakerPolicy = GetCircuitBreakerPolicy(logger, exceptionsBeforeBreaking, breakDurationSeconds);

        // Wrap: Circuit breaker wraps retry (retry happens inside circuit)
        return Policy.WrapAsync(circuitBreakerPolicy, retryPolicy);
    }

    /// <summary>
    /// Determines if an HTTP response represents a transient error.
    /// </summary>
    private static bool IsTransientError(HttpResponseMessage response)
    {
        // Rate limit (429) or server errors (5xx)
        return response.StatusCode == HttpStatusCode.TooManyRequests ||
               (int)response.StatusCode >= 500;
    }
}
