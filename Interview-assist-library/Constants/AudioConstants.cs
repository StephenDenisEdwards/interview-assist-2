namespace InterviewAssist.Library.Constants;

/// <summary>
/// Constants related to audio processing and capture.
/// </summary>
public static class AudioConstants
{
    /// <summary>
    /// Audio sample rate in Hz for OpenAI Realtime API.
    /// </summary>
    public const int SampleRateHz = 16000;

    /// <summary>
    /// Bytes per audio sample (16-bit PCM = 2 bytes).
    /// </summary>
    public const int BytesPerSample = 2;

    /// <summary>
    /// Number of audio channels (mono = 1).
    /// </summary>
    public const int Channels = 1;

    /// <summary>
    /// Minimum audio buffer duration in milliseconds before committing.
    /// </summary>
    public const int MinCommitMs = 100;

    /// <summary>
    /// Capacity of the audio channel queue.
    /// Uses DropOldest policy when full.
    /// </summary>
    public const int AudioChannelCapacity = 8;

    /// <summary>
    /// Calculates the minimum number of bytes required before committing audio.
    /// </summary>
    public static int MinCommitBytes => (SampleRateHz * BytesPerSample * Channels * MinCommitMs) / 1000;
}
