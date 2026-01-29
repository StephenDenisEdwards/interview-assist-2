namespace InterviewAssist.Library.Constants;

/// <summary>
/// Constants related to audio transcription processing.
/// </summary>
public static class TranscriptionConstants
{
    /// <summary>
    /// Minimum audio duration in seconds required by Whisper API.
    /// Audio shorter than this will be rejected with "audio_too_short" error.
    /// </summary>
    public const double MinAudioDurationSeconds = 0.1;

    /// <summary>
    /// Default RMS energy threshold for silence detection (0.0-1.0).
    /// Audio with RMS below this is considered silence.
    /// Value of 0.01 corresponds to approximately -40 dB.
    /// </summary>
    public const double DefaultSilenceThreshold = 0.01;

    /// <summary>
    /// Maximum consecutive identical words before text is considered a hallucination.
    /// </summary>
    public const int MaxConsecutiveRepetitions = 3;

    // Basic mode defaults

    /// <summary>
    /// Default minimum batch window in milliseconds for Basic mode.
    /// </summary>
    public const int DefaultBasicBatchMs = 3000;

    /// <summary>
    /// Default maximum batch window in milliseconds for Basic mode.
    /// </summary>
    public const int DefaultBasicMaxBatchMs = 6000;

    // Revision mode defaults

    /// <summary>
    /// Default overlap duration in milliseconds between batches for Revision mode.
    /// </summary>
    public const int DefaultRevisionOverlapMs = 1500;

    /// <summary>
    /// Default batch duration in milliseconds for Revision mode.
    /// </summary>
    public const int DefaultRevisionBatchMs = 2000;

    /// <summary>
    /// Default number of confirmations required for text to become stable in Revision mode.
    /// </summary>
    public const int DefaultRevisionAgreementCount = 2;

    /// <summary>
    /// Default Jaccard similarity threshold for text matching in Revision mode.
    /// </summary>
    public const double DefaultRevisionSimilarityThreshold = 0.85;

    // Streaming mode defaults

    /// <summary>
    /// Default minimum batch duration in milliseconds for Streaming mode.
    /// </summary>
    public const int DefaultStreamingMinBatchMs = 500;

    /// <summary>
    /// Default interval in milliseconds between hypothesis updates in Streaming mode.
    /// </summary>
    public const int DefaultStreamingUpdateIntervalMs = 250;

    /// <summary>
    /// Default number of unchanged iterations before text becomes stable in Streaming mode.
    /// </summary>
    public const int DefaultStreamingStabilityIterations = 3;

    /// <summary>
    /// Default timeout in milliseconds after which provisional text becomes stable in Streaming mode.
    /// </summary>
    public const int DefaultStreamingStabilityTimeoutMs = 2000;

    /// <summary>
    /// Default cooldown in milliseconds to prevent rapid provisional updates in Streaming mode.
    /// </summary>
    public const int DefaultStreamingFlickerCooldownMs = 100;

    // Context prompting defaults

    /// <summary>
    /// Default maximum characters of recent transcript to include in context prompt.
    /// </summary>
    public const int DefaultContextPromptMaxChars = 200;
}
