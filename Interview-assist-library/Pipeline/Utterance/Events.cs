namespace InterviewAssist.Library.Pipeline.Utterance;

/// <summary>
/// Normalized ASR event from Deepgram or other speech recognition source.
/// </summary>
public sealed record AsrEvent
{
    public required string Text { get; init; }
    public bool IsFinal { get; init; }
    public string? SpeakerId { get; init; }
    public IReadOnlyList<AsrWord>? Words { get; init; }
    public DateTime ReceivedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsUtteranceEnd { get; init; }
}

/// <summary>
/// Word-level ASR result with timing and confidence.
/// </summary>
public sealed record AsrWord
{
    public required string Word { get; init; }
    public double Start { get; init; }
    public double End { get; init; }
    public double Confidence { get; init; }
    public int? Speaker { get; init; }
}

/// <summary>
/// Reason an utterance was closed.
/// </summary>
public enum UtteranceCloseReason
{
    /// <summary>Deepgram signaled utterance end.</summary>
    DeepgramSignal,

    /// <summary>Terminal punctuation followed by pause.</summary>
    TerminalPunctuation,

    /// <summary>Silence gap exceeded threshold.</summary>
    SilenceGap,

    /// <summary>Maximum duration guard triggered.</summary>
    MaxDuration,

    /// <summary>Maximum length guard triggered.</summary>
    MaxLength,

    /// <summary>Manually closed (e.g., user interrupt).</summary>
    Manual
}

/// <summary>
/// Event emitted when an utterance is opened, updated, or finalized.
/// </summary>
public sealed record UtteranceEvent
{
    public required string Id { get; init; }
    public required UtteranceEventType Type { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Stable (confirmed) text from the stabilizer.</summary>
    public string StableText { get; init; } = "";

    /// <summary>Raw (volatile) text including unconfirmed portions.</summary>
    public string RawText { get; init; } = "";

    /// <summary>Duration since utterance opened.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Reason for close (only set when Type is Final).</summary>
    public UtteranceCloseReason? CloseReason { get; init; }

    /// <summary>Speaker ID if diarization is enabled.</summary>
    public string? SpeakerId { get; init; }

    /// <summary>
    /// UTC timestamps of IsFinal ASR events committed into this utterance.
    /// Only populated on Final events. Used for downstream position mapping.
    /// </summary>
    public IReadOnlyList<DateTime>? CommittedAsrTimestamps { get; init; }
}

public enum UtteranceEventType
{
    Open,
    Update,
    Final
}

/// <summary>
/// Detected intent with classification and extracted slots.
/// </summary>
public sealed record DetectedIntent
{
    public required IntentType Type { get; init; }
    public IntentSubtype? Subtype { get; init; }
    public double Confidence { get; init; }
    public IntentSlots Slots { get; init; } = new();
    public required string SourceText { get; init; }
    /// <summary>Verbatim excerpt from the transcript, before any pronoun resolution or cleanup.</summary>
    public string? OriginalText { get; init; }
    /// <summary>ID of the utterance this intent was detected from (set by LLM when using labeled input).</summary>
    public string? UtteranceId { get; init; }
    public DateTime DetectedAtUtc { get; init; } = DateTime.UtcNow;
}

public enum IntentType
{
    Question,
    Imperative,
    Statement,
    Other
}

public enum IntentSubtype
{
    // Question subtypes
    Definition,
    HowTo,
    Compare,
    Troubleshoot,

    // Imperative subtypes
    Stop,
    Repeat,
    Continue,
    StartOver,
    Generate
}

/// <summary>
/// Extracted slots from intent detection.
/// </summary>
public sealed record IntentSlots
{
    /// <summary>Topic extracted from the utterance (e.g., "lock statement in C#").</summary>
    public string? Topic { get; init; }

    /// <summary>Count extracted (e.g., 20 from "generate 20 questions").</summary>
    public int? Count { get; init; }

    /// <summary>Reference extracted (e.g., "number 3", "last", "previous").</summary>
    public string? Reference { get; init; }
}

/// <summary>
/// Intent event (candidate or final).
/// </summary>
public sealed record IntentEvent
{
    public required DetectedIntent Intent { get; init; }
    public required string UtteranceId { get; init; }
    public bool IsCandidate { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Action triggered event.
/// </summary>
public sealed record ActionEvent
{
    public required string ActionName { get; init; }
    public required DetectedIntent Intent { get; init; }
    public required string UtteranceId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool WasDebounced { get; init; }
}
