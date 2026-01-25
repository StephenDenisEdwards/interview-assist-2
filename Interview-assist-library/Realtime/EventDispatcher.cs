using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InterviewAssist.Library.Realtime;

/// <summary>
/// Thread-safe event dispatcher that queues actions and executes them sequentially.
/// Protects internal loops from subscriber exceptions.
/// </summary>
internal sealed class EventDispatcher : IDisposable
{
    private readonly ILogger _logger;
    private Channel<Action>? _channel;
    private Task? _dispatchTask;
    private CancellationTokenSource? _cts;
    private int _disposed;

    public EventDispatcher(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Starts the event dispatcher with the specified cancellation token.
    /// </summary>
    public void Start(CancellationToken cancellationToken = default)
    {
        if (_channel != null)
        {
            throw new InvalidOperationException("EventDispatcher is already started.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _channel = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _dispatchTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var action in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Event handler exception");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
        }, _cts.Token);
    }

    /// <summary>
    /// Stops the event dispatcher and waits for pending events to complete.
    /// </summary>
    public void Stop()
    {
        try
        {
            _channel?.Writer.TryComplete();
        }
        catch
        {
            // Ignore completion errors
        }

        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Ignore cancellation errors
        }

        _channel = null;
        _dispatchTask = null;

        try
        {
            _cts?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }

        _cts = null;
    }

    /// <summary>
    /// Queues an action to be executed by the dispatcher.
    /// If the dispatcher is not running, executes the action directly.
    /// </summary>
    public void Raise(Action action)
    {
        try
        {
            if (_channel != null)
            {
                _channel.Writer.TryWrite(action);
                return;
            }

            // Fallback: execute directly if dispatcher not running
            action();
        }
        catch
        {
            // Protect caller from subscriber exceptions
        }
    }

    /// <summary>
    /// Gets whether the dispatcher is currently running.
    /// </summary>
    public bool IsRunning => _channel != null;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Stop();
    }
}
