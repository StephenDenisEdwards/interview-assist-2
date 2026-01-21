namespace InterviewAssist.Pipeline;

/// <summary>
/// A timestamped transcript segment suitable for video captioning.
/// </summary>
public sealed record TranscriptSegment
{
    /// <summary>
    /// The transcribed text for this segment.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Start time in seconds from the beginning of the audio chunk.
    /// </summary>
    public required double StartSeconds { get; init; }

    /// <summary>
    /// End time in seconds from the beginning of the audio chunk.
    /// </summary>
    public required double EndSeconds { get; init; }

    /// <summary>
    /// Duration of this segment in seconds.
    /// </summary>
    public double DurationSeconds => EndSeconds - StartSeconds;

    /// <summary>
    /// Word-level timing information (if requested).
    /// </summary>
    public IReadOnlyList<TranscriptWord>? Words { get; init; }

    /// <summary>
    /// Absolute timestamp when this segment was captured (wall clock time).
    /// Useful for correlating with video timeline.
    /// </summary>
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Offset from the start of the stream in seconds.
    /// This is the cumulative time from when transcription started.
    /// </summary>
    public double StreamOffsetSeconds { get; init; }
}

/// <summary>
/// Word-level timing for precise caption synchronization.
/// </summary>
public sealed record TranscriptWord
{
    /// <summary>
    /// The word text.
    /// </summary>
    public required string Word { get; init; }

    /// <summary>
    /// Start time in seconds from the beginning of the audio chunk.
    /// </summary>
    public required double StartSeconds { get; init; }

    /// <summary>
    /// End time in seconds from the beginning of the audio chunk.
    /// </summary>
    public required double EndSeconds { get; init; }
}

/// <summary>
/// Complete transcription result from a single API call.
/// </summary>
public sealed record TranscriptionResult
{
    /// <summary>
    /// The full transcribed text.
    /// </summary>
    public required string FullText { get; init; }

    /// <summary>
    /// Individual segments with timing.
    /// </summary>
    public required IReadOnlyList<TranscriptSegment> Segments { get; init; }

    /// <summary>
    /// Duration of the audio that was transcribed in seconds.
    /// </summary>
    public required double AudioDurationSeconds { get; init; }

    /// <summary>
    /// Time taken to transcribe in milliseconds.
    /// </summary>
    public required long LatencyMs { get; init; }

    /// <summary>
    /// The language detected or used.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Offset from the start of the stream in seconds.
    /// </summary>
    public double StreamOffsetSeconds { get; init; }
}

/// <summary>
/// Options for the timestamped transcription service.
/// </summary>
public sealed record TimestampedTranscriptionOptions
{
    /// <summary>
    /// Audio sample rate in Hz. Default: 16000 (Whisper's native rate).
    /// </summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary>
    /// Batch duration in milliseconds. How much audio to buffer before transcribing.
    /// Default: 5000ms (5 seconds). Shorter = lower latency, longer = better accuracy.
    /// </summary>
    public int BatchMs { get; init; } = 5000;

    /// <summary>
    /// Whether to include word-level timestamps. Increases response size.
    /// </summary>
    public bool IncludeWordTimestamps { get; init; } = false;

    /// <summary>
    /// Language code (e.g., "en", "es"). Null for auto-detection.
    /// Specifying language improves speed and accuracy.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Optional prompt to guide transcription style/vocabulary.
    /// </summary>
    public string? Prompt { get; init; }
}
