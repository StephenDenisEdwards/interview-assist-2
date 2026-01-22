using InterviewAssist.Library.Constants;
using InterviewAssist.Library.Context;

namespace InterviewAssist.Library.Realtime;

/// <summary>
/// Configuration options for the OpenAI Realtime API connection.
/// </summary>
public record RealtimeApiOptions
{
    /// <summary>
    /// OpenAI API key for authentication.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// The realtime model to use. Default: gpt-4o-realtime-preview-2024-12-17.
    /// </summary>
    public string RealtimeModel { get; init; } = ModelConstants.DefaultRealtimeModel;

    /// <summary>
    /// The transcription model to use for input audio. Default: whisper-1.
    /// </summary>
    public string TranscriptionModel { get; init; } = ModelConstants.DefaultTranscriptionModel;

    /// <summary>
    /// Additional instructions to include in the session configuration.
    /// </summary>
    public string? ExtraInstructions { get; init; }

    /// <summary>
    /// Context chunks to upload at session start (e.g., CV, job spec).
    /// </summary>
    public IReadOnlyList<ContextChunk>? ContextChunks { get; init; }

    /// <summary>
    /// Base system instructions for the assistant. If null, uses default C# expert instructions.
    /// </summary>
    public string? SystemInstructions { get; init; }

    /// <summary>
    /// Factory function to dynamically generate system instructions.
    /// Takes priority over SystemInstructionsFilePath and SystemInstructions.
    /// </summary>
    public Func<string>? SystemInstructionsFactory { get; init; }

    /// <summary>
    /// File path to load system instructions from.
    /// Takes priority over SystemInstructions property but not over SystemInstructionsFactory.
    /// </summary>
    public string? SystemInstructionsFilePath { get; init; }

    /// <summary>
    /// Voice to use for audio output. Default: "alloy".
    /// </summary>
    public string Voice { get; init; } = "alloy";

    /// <summary>
    /// VAD threshold for speech detection. Default: 0.5.
    /// </summary>
    public double VadThreshold { get; init; } = 0.5;

    /// <summary>
    /// Silence duration in milliseconds before turn ends. Default: 500.
    /// </summary>
    public int SilenceDurationMs { get; init; } = 500;

    /// <summary>
    /// Prefix padding in milliseconds for speech detection. Default: 300.
    /// </summary>
    public int PrefixPaddingMs { get; init; } = 300;

    /// <summary>
    /// Maximum characters for extra instructions. Default: 4000.
    /// </summary>
    public int MaxInstructionChars { get; init; } = 4000;

    /// <summary>
    /// Enable automatic reconnection on connection loss. Default: true.
    /// </summary>
    public bool EnableReconnection { get; init; } = true;

    /// <summary>
    /// Maximum reconnection attempts before giving up. Default: 5.
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>
    /// Base delay for exponential backoff in milliseconds. Default: 1000.
    /// </summary>
    public int ReconnectBaseDelayMs { get; init; } = 1000;

    /// <summary>
    /// Enable automatic rate limit recovery. Default: true.
    /// </summary>
    public bool EnableRateLimitRecovery { get; init; } = true;

    /// <summary>
    /// Delay before resuming after rate limit in milliseconds. Default: 60000 (1 minute).
    /// </summary>
    public int RateLimitRecoveryDelayMs { get; init; } = 60000;

    /// <summary>
    /// Maximum delay for reconnection/recovery in milliseconds. Default: 30000 (30 seconds).
    /// Used to cap exponential backoff.
    /// </summary>
    public int MaxReconnectDelayMs { get; init; } = 30000;

    /// <summary>
    /// WebSocket connection timeout in milliseconds. Default: 30000 (30 seconds).
    /// </summary>
    public int WebSocketConnectTimeoutMs { get; init; } = 30000;

    /// <summary>
    /// WebSocket keep-alive interval in milliseconds. Default: 30000 (30 seconds).
    /// </summary>
    public int WebSocketKeepAliveIntervalMs { get; init; } = 30000;

    /// <summary>
    /// HTTP request timeout in milliseconds for non-WebSocket operations. Default: 60000 (1 minute).
    /// </summary>
    public int HttpRequestTimeoutMs { get; init; } = 60000;

    /// <summary>
    /// Optional callback to save session state on shutdown.
    /// Called during graceful shutdown with the session state.
    /// </summary>
    public Func<SessionState, Task>? OnShutdownSaveState { get; init; }
}
