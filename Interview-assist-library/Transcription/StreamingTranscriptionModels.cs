namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Transcription mode determining how stability is tracked.
/// </summary>
public enum TranscriptionMode
{
    /// <summary>
    /// Legacy mode using TimestampedTranscriptionService with optional question detection.
    /// </summary>
    Legacy,

    /// <summary>
    /// All transcribed text is immediately stable. Uses context prompting for continuity.
    /// </summary>
    Basic,

    /// <summary>
    /// Overlapping windows with local agreement policy. Text becomes stable after
    /// appearing consistently across multiple transcription passes.
    /// </summary>
    Revision,

    /// <summary>
    /// Rapid hypothesis updates with stability tracking. Text becomes stable after
    /// remaining unchanged for N iterations or a timeout period.
    /// </summary>
    Hypothesis
}

/// <summary>
/// Event args for text that has been confirmed as stable and will not change.
/// </summary>
public record StableTextEventArgs
{
    /// <summary>
    /// The confirmed stable text segment.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Offset in milliseconds from the start of the audio stream.
    /// </summary>
    public long StreamOffsetMs { get; init; }

    /// <summary>
    /// Timestamp when this text was confirmed as stable.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Number of times this text was confirmed (for Revision/Streaming modes).
    /// Always 1 for Basic mode.
    /// </summary>
    public int ConfirmationCount { get; init; } = 1;
}

/// <summary>
/// Event args for provisional text that may still change.
/// </summary>
public record ProvisionalTextEventArgs
{
    /// <summary>
    /// The provisional text that may be revised.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Confidence score for this provisional text (0.0-1.0), if available.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Offset in milliseconds from the start of the audio stream.
    /// </summary>
    public long StreamOffsetMs { get; init; }

    /// <summary>
    /// Timestamp of this provisional update.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event args providing full hypothesis context including both stable and provisional text.
/// </summary>
public record HypothesisEventArgs
{
    /// <summary>
    /// Complete transcript including both stable and provisional text.
    /// </summary>
    public required string FullText { get; init; }

    /// <summary>
    /// Text that has been confirmed as stable.
    /// </summary>
    public required string StableText { get; init; }

    /// <summary>
    /// Text that is provisional and may change.
    /// </summary>
    public required string ProvisionalText { get; init; }

    /// <summary>
    /// Ratio of stable text to total text (0.0-1.0).
    /// </summary>
    public double StabilityRatio { get; init; }

    /// <summary>
    /// Time since the last text became stable.
    /// </summary>
    public TimeSpan TimeSinceLastStable { get; init; }
}
