namespace InterviewAssist.Library.Realtime;

/// <summary>
/// Interface for receiving events from the Realtime API.
/// Implementations handle how events are displayed or processed.
/// </summary>
public interface IRealtimeSink : IDisposable
{
    /// <summary>Called when the WebSocket connection is established.</summary>
    void OnConnected();

    /// <summary>Called when the session is ready to receive input.</summary>
    void OnReady();

    /// <summary>Called when the connection is closed.</summary>
    void OnDisconnected();

    /// <summary>Called when attempting to reconnect after connection loss.</summary>
    void OnReconnecting(int attempt, int maxAttempts);

    /// <summary>Called for informational messages.</summary>
    void OnInfo(string message);

    /// <summary>Called for warning messages.</summary>
    void OnWarning(string message);

    /// <summary>Called for debug messages.</summary>
    void OnDebug(string message);

    /// <summary>Called when an error occurs.</summary>
    void OnError(Exception ex);

    /// <summary>Called when user speech is transcribed.</summary>
    void OnUserTranscript(string text);

    /// <summary>Called for each text delta from the assistant.</summary>
    void OnAssistantTextDelta(string delta);

    /// <summary>Called when assistant text response is complete.</summary>
    void OnAssistantTextDone();

    /// <summary>Called when a function call response is received with answer and code.</summary>
    void OnFunctionCallResponse(string functionName, string answer, string code);
}

/// <summary>
/// Extension methods for wiring an IRealtimeSink to an IRealtimeApi.
/// </summary>
public static class RealtimeSinkExtensions
{
    /// <summary>
    /// Wire all events from the API to the sink. Returns an IDisposable that unwires when disposed.
    /// </summary>
    public static IDisposable WireToApi(this IRealtimeSink sink, IRealtimeApi api)
    {
        return new RealtimeApiSinkWiring(api, sink);
    }

    private sealed class RealtimeApiSinkWiring : IDisposable
    {
        private readonly IRealtimeApi _api;
        private readonly IRealtimeSink _sink;
        private bool _disposed;

        private readonly Action _onConnected;
        private readonly Action _onReady;
        private readonly Action _onDisconnected;
        private readonly Action<string> _onInfo;
        private readonly Action<string> _onWarning;
        private readonly Action<string> _onDebug;
        private readonly Action<Exception> _onError;
        private readonly Action<string> _onUserTranscript;
        private readonly Action<string> _onAssistantTextDelta;
        private readonly Action _onAssistantTextDone;
        private readonly Action<string, string, string> _onFunctionCallResponse;

        public RealtimeApiSinkWiring(IRealtimeApi api, IRealtimeSink sink)
        {
            _api = api;
            _sink = sink;

            _onConnected = () => sink.OnConnected();
            _onReady = () => sink.OnReady();
            _onDisconnected = () => sink.OnDisconnected();
            _onInfo = msg => sink.OnInfo(msg);
            _onWarning = msg => sink.OnWarning(msg);
            _onDebug = msg => sink.OnDebug(msg);
            _onError = ex => sink.OnError(ex);
            _onUserTranscript = t => sink.OnUserTranscript(t);
            _onAssistantTextDelta = d => sink.OnAssistantTextDelta(d);
            _onAssistantTextDone = () => sink.OnAssistantTextDone();
            _onFunctionCallResponse = (fn, answer, code) => sink.OnFunctionCallResponse(fn, answer, code);

            api.OnConnected += _onConnected;
            api.OnReady += _onReady;
            api.OnDisconnected += _onDisconnected;
            api.OnInfo += _onInfo;
            api.OnWarning += _onWarning;
            api.OnDebug += _onDebug;
            api.OnError += _onError;
            api.OnUserTranscript += _onUserTranscript;
            api.OnAssistantTextDelta += _onAssistantTextDelta;
            api.OnAssistantTextDone += _onAssistantTextDone;
            api.OnFunctionCallResponse += _onFunctionCallResponse;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _api.OnConnected -= _onConnected;
            _api.OnReady -= _onReady;
            _api.OnDisconnected -= _onDisconnected;
            _api.OnInfo -= _onInfo;
            _api.OnWarning -= _onWarning;
            _api.OnDebug -= _onDebug;
            _api.OnError -= _onError;
            _api.OnUserTranscript -= _onUserTranscript;
            _api.OnAssistantTextDelta -= _onAssistantTextDelta;
            _api.OnAssistantTextDone -= _onAssistantTextDone;
            _api.OnFunctionCallResponse -= _onFunctionCallResponse;
        }
    }
}
