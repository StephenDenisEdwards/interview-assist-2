using System.Text.Json.Serialization;

namespace InterviewAssist.Library.Pipeline.Recording;

/// <summary>
/// Base class for all recorded events in a session recording.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RecordedSessionMetadata), "SessionMetadata")]
[JsonDerivedType(typeof(RecordedAsrEvent), "AsrEvent")]
[JsonDerivedType(typeof(RecordedUtteranceEndSignal), "UtteranceEndSignal")]
[JsonDerivedType(typeof(RecordedUtteranceEvent), "UtteranceEvent")]
[JsonDerivedType(typeof(RecordedIntentEvent), "IntentEvent")]
[JsonDerivedType(typeof(RecordedIntentCorrectionEvent), "IntentCorrectionEvent")]
[JsonDerivedType(typeof(RecordedActionEvent), "ActionEvent")]
public abstract record RecordedEvent
{
    /// <summary>Milliseconds since recording started.</summary>
    public long OffsetMs { get; init; }
}

/// <summary>
/// Session metadata recorded at the start of each session.
/// </summary>
public sealed record RecordedSessionMetadata : RecordedEvent
{
    public string Version { get; init; } = "1.0";
    public DateTime RecordedAtUtc { get; init; }
    public SessionConfig Config { get; init; } = new();
}

/// <summary>
/// Configuration snapshot for the recording session.
/// </summary>
public sealed record SessionConfig
{
    public string? DeepgramModel { get; init; }
    public bool Diarize { get; init; }
    public bool IntentDetectionEnabled { get; init; }
    public string? IntentDetectionMode { get; init; }
    public string? AudioSource { get; init; }
    public int SampleRate { get; init; }
}

/// <summary>
/// Recorded ASR (Automatic Speech Recognition) event.
/// </summary>
public sealed record RecordedAsrEvent : RecordedEvent
{
    public AsrEventData Data { get; init; } = new();
}

/// <summary>
/// Data payload for ASR events.
/// </summary>
public sealed record AsrEventData
{
    public string Text { get; init; } = "";
    public bool IsFinal { get; init; }
    public string? SpeakerId { get; init; }
    public bool IsUtteranceEnd { get; init; }
}

/// <summary>
/// Signal that an utterance has ended (from Deepgram).
/// </summary>
public sealed record RecordedUtteranceEndSignal : RecordedEvent;

/// <summary>
/// Recorded utterance lifecycle event.
/// </summary>
public sealed record RecordedUtteranceEvent : RecordedEvent
{
    public UtteranceEventData Data { get; init; } = new();
}

/// <summary>
/// Data payload for utterance events.
/// </summary>
public sealed record UtteranceEventData
{
    public string Id { get; init; } = "";
    public string EventType { get; init; } = ""; // Open, Update, Final
    public string StableText { get; init; } = "";
    public string RawText { get; init; } = "";
    public long DurationMs { get; init; }
    public string? CloseReason { get; init; }
    public string? SpeakerId { get; init; }

    /// <summary>
    /// OffsetMs values of the IsFinal ASR events that composed this utterance.
    /// Only on Final events. Null for old recordings (backward compatible).
    /// </summary>
    public IReadOnlyList<long>? AsrFinalOffsetMs { get; init; }
}

/// <summary>
/// Recorded intent detection event.
/// </summary>
public sealed record RecordedIntentEvent : RecordedEvent
{
    public IntentEventData Data { get; init; } = new();
}

/// <summary>
/// Data payload for intent events.
/// </summary>
public sealed record IntentEventData
{
    public DetectedIntentData Intent { get; init; } = new();
    public string UtteranceId { get; init; } = "";
    public bool IsCandidate { get; init; }

    /// <summary>Character offset (inclusive) in the running transcript. Null for old recordings.</summary>
    public int? TranscriptCharStart { get; init; }
    /// <summary>Character offset (exclusive) in the running transcript. Null for old recordings.</summary>
    public int? TranscriptCharEnd { get; init; }
}

/// <summary>
/// Serializable representation of a detected intent.
/// </summary>
public sealed record DetectedIntentData
{
    public string Type { get; init; } = "";
    public string? Subtype { get; init; }
    public double Confidence { get; init; }
    public string SourceText { get; init; } = "";
    /// <summary>Verbatim excerpt from the transcript, before any pronoun resolution or cleanup.</summary>
    public string? OriginalText { get; init; }
    public IntentSlotsData? Slots { get; init; }
}

/// <summary>
/// Serializable representation of intent slots.
/// </summary>
public sealed record IntentSlotsData
{
    public string? Topic { get; init; }
    public int? Count { get; init; }
    public string? Reference { get; init; }
}

/// <summary>
/// Recorded action triggered event.
/// </summary>
public sealed record RecordedActionEvent : RecordedEvent
{
    public ActionEventData Data { get; init; } = new();
}

/// <summary>
/// Data payload for action events.
/// </summary>
public sealed record ActionEventData
{
    public string ActionName { get; init; } = "";
    public string UtteranceId { get; init; } = "";
    public bool WasDebounced { get; init; }
}

/// <summary>
/// Recorded intent correction event (LLM corrects/adds to heuristic detection).
/// </summary>
public sealed record RecordedIntentCorrectionEvent : RecordedEvent
{
    public IntentCorrectionEventData Data { get; init; } = new();
}

/// <summary>
/// Data payload for intent correction events.
/// </summary>
public sealed record IntentCorrectionEventData
{
    public string UtteranceId { get; init; } = "";
    public DetectedIntentData? OriginalIntent { get; init; }
    public DetectedIntentData CorrectedIntent { get; init; } = new();
    public string CorrectionType { get; init; } = ""; // Confirmed, TypeChanged, Added, Removed

    /// <summary>Character offset (inclusive) in the running transcript. Null for old recordings.</summary>
    public int? TranscriptCharStart { get; init; }
    /// <summary>Character offset (exclusive) in the running transcript. Null for old recordings.</summary>
    public int? TranscriptCharEnd { get; init; }
}
