using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Strategy interface for intent detection in the pipeline.
/// Different implementations provide different trade-offs between
/// speed, accuracy, and cost.
/// </summary>
public interface IIntentDetectionStrategy : IDisposable
{
    /// <summary>
    /// Mode identifier for logging and debugging.
    /// </summary>
    string ModeName { get; }

    /// <summary>
    /// Process a finalized utterance for intent detection.
    /// </summary>
    /// <param name="utterance">The finalized utterance event.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ProcessUtteranceAsync(UtteranceEvent utterance, CancellationToken ct = default);

    /// <summary>
    /// Signal that a speech pause was detected.
    /// Some strategies use this to trigger detection.
    /// </summary>
    void SignalPause();

    /// <summary>
    /// Event fired when an intent is detected.
    /// </summary>
    event Action<IntentEvent>? OnIntentDetected;

    /// <summary>
    /// Event fired when a previously detected intent is corrected.
    /// Only used by strategies that perform verification (e.g., Parallel mode).
    /// </summary>
    event Action<IntentCorrectionEvent>? OnIntentCorrected;
}

/// <summary>
/// Event indicating a correction to a previously detected intent.
/// </summary>
public record IntentCorrectionEvent
{
    /// <summary>
    /// The utterance ID of the original detection.
    /// </summary>
    public required string UtteranceId { get; init; }

    /// <summary>
    /// The original intent that was detected (may be null if it was a miss).
    /// </summary>
    public DetectedIntent? OriginalIntent { get; init; }

    /// <summary>
    /// The corrected intent from LLM verification.
    /// </summary>
    public required DetectedIntent CorrectedIntent { get; init; }

    /// <summary>
    /// Type of correction made.
    /// </summary>
    public required IntentCorrectionType CorrectionType { get; init; }

    /// <summary>
    /// Timestamp of the correction.
    /// </summary>
    public DateTime CorrectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Type of intent correction.
/// </summary>
public enum IntentCorrectionType
{
    /// <summary>
    /// LLM confirmed the heuristic detection.
    /// </summary>
    Confirmed,

    /// <summary>
    /// LLM changed the intent type (e.g., Statement → Question).
    /// </summary>
    TypeChanged,

    /// <summary>
    /// LLM detected an intent that heuristic missed.
    /// </summary>
    Added,

    /// <summary>
    /// LLM determined the heuristic detection was a false positive.
    /// </summary>
    Removed
}
