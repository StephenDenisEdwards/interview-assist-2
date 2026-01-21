namespace InterviewAssist.Library.Constants;

/// <summary>
/// Constants related to queues and buffers.
/// </summary>
public static class QueueConstants
{
    /// <summary>
    /// Default maximum size for the question queue.
    /// </summary>
    public const int DefaultQuestionQueueSize = 5;

    /// <summary>
    /// Multiplier for deduplication hash set size limit.
    /// Hash set is cleared when it exceeds (maxSize * DeduplicationMultiplier).
    /// </summary>
    public const int DeduplicationMultiplier = 10;

    /// <summary>
    /// Default maximum age in seconds for transcript buffer entries.
    /// </summary>
    public const int DefaultTranscriptMaxAgeSeconds = 30;

    /// <summary>
    /// Maximum reconnection delay cap in milliseconds.
    /// </summary>
    public const int MaxReconnectDelayMs = 30000;
}
