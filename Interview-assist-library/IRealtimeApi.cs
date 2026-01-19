using System;
using System.Threading;
using System.Threading.Tasks;

namespace InterviewAssist.Library.Realtime;

public interface IRealtimeApi : IAsyncDisposable
{
    // Lifecycle
    event Action? OnConnected;
    event Action? OnReady;
    event Action? OnDisconnected;
    event Action? OnReconnecting;

    // Diagnostics
    event Action<string>? OnInfo;
    event Action<string>? OnWarning;
    event Action<string>? OnDebug;
    event Action<Exception>? OnError;

    // Input/Output
    event Action<string>? OnUserTranscript;
    event Action? OnSpeechStarted;
    event Action? OnSpeechStopped;
    event Action<string>? OnAssistantTextDelta;
    event Action? OnAssistantTextDone;
    event Action<string>? OnAssistantAudioTranscriptDelta;
    event Action? OnAssistantAudioTranscriptDone;

    // Tool/function response
    event Action<string, string, string>? OnFunctionCallResponse;

    /// <summary>
    /// Start the realtime API connection and begin processing.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop the realtime API connection gracefully.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Send a text message to the assistant.
    /// </summary>
    Task SendTextAsync(string text, bool requestResponse = true, bool interrupt = false);

    /// <summary>
    /// Check if the API is currently connected and ready.
    /// </summary>
    bool IsConnected { get; }
}
