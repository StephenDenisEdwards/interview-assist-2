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
}
