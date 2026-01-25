namespace InterviewAssist.Library.Constants;

/// <summary>
/// Constants for the Realtime API implementation.
/// </summary>
public static class RealtimeConstants
{
    /// <summary>
    /// Maximum number of recent transcripts to keep for session state.
    /// </summary>
    public const int MaxRecentTranscripts = 50;

    /// <summary>
    /// WebSocket receive buffer size in bytes (64KB).
    /// </summary>
    public const int WebSocketBufferSize = 64 * 1024;

    /// <summary>
    /// Delay before retrying to parse incomplete function call arguments.
    /// </summary>
    public const int FunctionParseRetryDelayMs = 600;
}
