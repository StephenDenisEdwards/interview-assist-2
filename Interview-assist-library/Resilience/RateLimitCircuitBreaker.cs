using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InterviewAssist.Library.Resilience;

/// <summary>
/// A circuit breaker pattern implementation for handling rate limits.
/// States: Closed (normal operation) → Open (rate limited, blocked) → HalfOpen (testing) → Closed/Open
/// </summary>
public class RateLimitCircuitBreaker
{
    private readonly ILogger _logger;
    private readonly int _baseRecoveryDelayMs;
    private readonly int _maxRecoveryDelayMs;
    private readonly object _lock = new();

    private CircuitState _state = CircuitState.Closed;
    private int _consecutiveFailures;
    private DateTime _blockedUntil = DateTime.MinValue;
    private Timer? _recoveryTimer;

    public event Action? OnOpened;
    public event Action? OnHalfOpen;
    public event Action? OnClosed;
    public event Action<int>? OnRecoveryScheduled;

    public CircuitState State
    {
        get { lock (_lock) return _state; }
    }

    public bool IsOpen
    {
        get { lock (_lock) return _state == CircuitState.Open; }
    }

    public bool AllowRequest
    {
        get
        {
            lock (_lock)
            {
                return _state switch
                {
                    CircuitState.Closed => true,
                    CircuitState.HalfOpen => true,
                    CircuitState.Open => DateTime.UtcNow >= _blockedUntil,
                    _ => false
                };
            }
        }
    }

    public RateLimitCircuitBreaker(int baseRecoveryDelayMs = 5000, int maxRecoveryDelayMs = 30000, ILogger? logger = null)
    {
        _baseRecoveryDelayMs = baseRecoveryDelayMs;
        _maxRecoveryDelayMs = maxRecoveryDelayMs;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Records a rate limit hit, opening or extending the circuit breaker.
    /// </summary>
    public void RecordRateLimit()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            var delay = CalculateDelay();

            _state = CircuitState.Open;
            _blockedUntil = DateTime.UtcNow.AddMilliseconds(delay);

            _logger.LogWarning("Circuit breaker opened. Consecutive failures: {Failures}, Recovery in {Delay}ms",
                _consecutiveFailures, delay);

            ScheduleRecovery(delay);
            OnOpened?.Invoke();
            OnRecoveryScheduled?.Invoke(delay);
        }
    }

    /// <summary>
    /// Records a successful operation, closing the circuit if it was half-open.
    /// </summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _logger.LogInformation("Circuit breaker closed after successful recovery");
                _state = CircuitState.Closed;
                _consecutiveFailures = 0;
                OnClosed?.Invoke();
            }
        }
    }

    /// <summary>
    /// Manually resets the circuit breaker to closed state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _recoveryTimer?.Dispose();
            _recoveryTimer = null;
            _state = CircuitState.Closed;
            _consecutiveFailures = 0;
            _blockedUntil = DateTime.MinValue;
            _logger.LogInformation("Circuit breaker reset to closed state");
            OnClosed?.Invoke();
        }
    }

    private int CalculateDelay()
    {
        // Exponential backoff with cap: base * 2^(failures-1), capped at max
        var delay = _baseRecoveryDelayMs * (1 << Math.Min(_consecutiveFailures - 1, 5));
        return Math.Min(delay, _maxRecoveryDelayMs);
    }

    private void ScheduleRecovery(int delayMs)
    {
        _recoveryTimer?.Dispose();
        _recoveryTimer = new Timer(
            _ => TransitionToHalfOpen(),
            null,
            delayMs,
            Timeout.Infinite);
    }

    private void TransitionToHalfOpen()
    {
        lock (_lock)
        {
            if (_state == CircuitState.Open)
            {
                _logger.LogInformation("Circuit breaker transitioning to half-open");
                _state = CircuitState.HalfOpen;
                OnHalfOpen?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        _recoveryTimer?.Dispose();
        _recoveryTimer = null;
    }
}

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}
