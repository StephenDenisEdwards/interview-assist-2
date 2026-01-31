namespace InterviewAssist.Library.Pipeline.Utterance;

/// <summary>
/// Source of normalized ASR events from a speech recognition provider.
/// </summary>
public interface IAsrEventSource
{
    /// <summary>Raised when a partial (interim) ASR result arrives.</summary>
    event Action<AsrEvent>? OnPartial;

    /// <summary>Raised when a final ASR result arrives.</summary>
    event Action<AsrEvent>? OnFinal;

    /// <summary>Raised when Deepgram signals utterance end.</summary>
    event Action? OnUtteranceEnd;
}

/// <summary>
/// Computes stable (confirmed) text from volatile interim hypotheses.
/// </summary>
public interface IStabilizer
{
    /// <summary>
    /// Add a new hypothesis and get the current stable text.
    /// </summary>
    /// <param name="hypothesis">The interim ASR text.</param>
    /// <param name="words">Optional word-level details with confidence.</param>
    /// <returns>The stable (longest common prefix) text.</returns>
    string AddHypothesis(string hypothesis, IReadOnlyList<AsrWord>? words = null);

    /// <summary>
    /// Commit final text and return the complete stable result.
    /// </summary>
    string CommitFinal(string finalText);

    /// <summary>
    /// Reset the stabilizer for a new utterance.
    /// </summary>
    void Reset();

    /// <summary>
    /// Current stable text.
    /// </summary>
    string StableText { get; }
}

/// <summary>
/// Builds coherent utterances from streaming ASR events.
/// </summary>
public interface IUtteranceBuilder
{
    /// <summary>Raised when a new utterance is opened.</summary>
    event Action<UtteranceEvent>? OnUtteranceOpen;

    /// <summary>Raised when an utterance is updated with new text.</summary>
    event Action<UtteranceEvent>? OnUtteranceUpdate;

    /// <summary>Raised when an utterance is finalized.</summary>
    event Action<UtteranceEvent>? OnUtteranceFinal;

    /// <summary>
    /// Process an incoming ASR event.
    /// </summary>
    void ProcessAsrEvent(AsrEvent evt);

    /// <summary>
    /// Signal from Deepgram that the current utterance should end.
    /// </summary>
    void SignalUtteranceEnd();

    /// <summary>
    /// Force close the current utterance (e.g., user interrupt).
    /// </summary>
    void ForceClose();

    /// <summary>
    /// Check for timeout-based utterance closure. Call periodically.
    /// </summary>
    void CheckTimeouts();

    /// <summary>
    /// Current utterance ID, or null if no active utterance.
    /// </summary>
    string? CurrentUtteranceId { get; }

    /// <summary>
    /// Whether there is an active utterance.
    /// </summary>
    bool HasActiveUtterance { get; }
}

/// <summary>
/// Detects intent from utterance text.
/// </summary>
public interface IIntentDetector
{
    /// <summary>
    /// Detect a candidate intent from partial/updating utterance.
    /// This is for UI hints only and should NOT trigger actions.
    /// </summary>
    DetectedIntent? DetectCandidate(string text);

    /// <summary>
    /// Detect final intent from completed utterance.
    /// This is the commit decision that can trigger actions.
    /// </summary>
    DetectedIntent DetectFinal(string text);
}

/// <summary>
/// Routes intents to actions with debouncing and conflict resolution.
/// </summary>
public interface IActionRouter
{
    /// <summary>Raised when an action is triggered.</summary>
    event Action<ActionEvent>? OnActionTriggered;

    /// <summary>
    /// Route a final intent to an action.
    /// </summary>
    /// <param name="intent">The detected intent.</param>
    /// <param name="utteranceId">The source utterance ID.</param>
    /// <returns>True if action was triggered, false if debounced.</returns>
    bool Route(DetectedIntent intent, string utteranceId);

    /// <summary>
    /// Register an action handler for an intent subtype.
    /// </summary>
    void RegisterHandler(IntentSubtype subtype, Action<DetectedIntent> handler);

    /// <summary>
    /// Clear cooldown state (for testing).
    /// </summary>
    void Reset();
}

/// <summary>
/// Configuration options for the utterance-intent pipeline.
/// </summary>
public sealed record PipelineOptions
{
    // Stabilizer options
    public int StabilizerWindowSize { get; init; } = 3;
    public double MinWordConfidence { get; init; } = 0.6;
    public bool RequireRepetitionForLowConfidence { get; init; } = true;

    // Utterance builder options
    public TimeSpan SilenceGapThreshold { get; init; } = TimeSpan.FromMilliseconds(750);
    public TimeSpan PunctuationPauseThreshold { get; init; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan MaxUtteranceDuration { get; init; } = TimeSpan.FromSeconds(12);
    public int MaxUtteranceLength { get; init; } = 500;

    // Action router options
    public TimeSpan ConflictWindow { get; init; } = TimeSpan.FromMilliseconds(1500);

    public static PipelineOptions Default => new();
}

/// <summary>
/// Cooldown configuration per intent subtype.
/// </summary>
public sealed record CooldownConfig
{
    public Dictionary<IntentSubtype, TimeSpan> Cooldowns { get; init; } = new()
    {
        [IntentSubtype.Stop] = TimeSpan.Zero,
        [IntentSubtype.Repeat] = TimeSpan.FromMilliseconds(1500),
        [IntentSubtype.Continue] = TimeSpan.FromMilliseconds(1500),
        [IntentSubtype.StartOver] = TimeSpan.FromMilliseconds(2000),
        [IntentSubtype.Generate] = TimeSpan.FromMilliseconds(5000)
    };

    public TimeSpan GetCooldown(IntentSubtype subtype)
    {
        return Cooldowns.TryGetValue(subtype, out var cooldown) ? cooldown : TimeSpan.FromMilliseconds(1000);
    }

    public static CooldownConfig Default => new();
}
