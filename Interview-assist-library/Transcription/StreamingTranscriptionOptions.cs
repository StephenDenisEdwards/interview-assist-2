using InterviewAssist.Library.Constants;

namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Configuration options for streaming transcription services.
/// </summary>
public record StreamingTranscriptionOptions
{
    /// <summary>
    /// OpenAI API key for transcription requests.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Transcription mode determining stability tracking behavior.
    /// </summary>
    public TranscriptionMode Mode { get; init; } = TranscriptionMode.Basic;

    /// <summary>
    /// Audio sample rate in Hz. Default: 24000.
    /// </summary>
    public int SampleRate { get; init; } = 24000;

    /// <summary>
    /// Language code for transcription (e.g., "en"). Null for auto-detection.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// RMS threshold for silence detection (0.0-1.0). Default: 0.01. Set to 0 to disable.
    /// </summary>
    public double SilenceThreshold { get; init; } = TranscriptionConstants.DefaultSilenceThreshold;

    /// <summary>
    /// Enable context prompting to guide transcription with recent stable text.
    /// </summary>
    public bool EnableContextPrompting { get; init; } = true;

    /// <summary>
    /// Maximum characters of recent transcript to include in context prompt.
    /// </summary>
    public int ContextPromptMaxChars { get; init; } = 200;

    /// <summary>
    /// Vocabulary prompt with domain-specific terms to improve recognition.
    /// </summary>
    public string? VocabularyPrompt { get; init; }

    /// <summary>
    /// Options specific to Basic mode.
    /// </summary>
    public BasicModeOptions Basic { get; init; } = new();

    /// <summary>
    /// Options specific to Revision mode.
    /// </summary>
    public RevisionModeOptions Revision { get; init; } = new();

    /// <summary>
    /// Options specific to Hypothesis mode.
    /// </summary>
    public HypothesisModeOptions Hypothesis { get; init; } = new();
}

/// <summary>
/// Options for Basic transcription mode.
/// </summary>
public record BasicModeOptions
{
    /// <summary>
    /// Minimum batch window in milliseconds before transcription. Default: 3000.
    /// </summary>
    public int BatchMs { get; init; } = 3000;

    /// <summary>
    /// Maximum batch window in milliseconds. Forces flush even without silence. Default: 6000.
    /// </summary>
    public int MaxBatchMs { get; init; } = 6000;
}

/// <summary>
/// Options for Revision transcription mode.
/// </summary>
public record RevisionModeOptions
{
    /// <summary>
    /// Overlap duration in milliseconds between consecutive batches. Default: 1500.
    /// </summary>
    public int OverlapMs { get; init; } = 1500;

    /// <summary>
    /// Batch duration in milliseconds for each transcription window. Default: 2000.
    /// </summary>
    public int BatchMs { get; init; } = 2000;

    /// <summary>
    /// Number of times text must appear consistently to become stable. Default: 2.
    /// </summary>
    public int AgreementCount { get; init; } = 2;

    /// <summary>
    /// Minimum Jaccard similarity for text to be considered matching (0.0-1.0). Default: 0.85.
    /// </summary>
    public double SimilarityThreshold { get; init; } = 0.85;
}

/// <summary>
/// Options for Hypothesis transcription mode.
/// </summary>
public record HypothesisModeOptions
{
    /// <summary>
    /// Minimum batch duration in milliseconds before transcription. Default: 500.
    /// </summary>
    public int MinBatchMs { get; init; } = 500;

    /// <summary>
    /// Interval in milliseconds between hypothesis updates. Default: 250.
    /// </summary>
    public int UpdateIntervalMs { get; init; } = 250;

    /// <summary>
    /// Number of unchanged iterations before text becomes stable. Default: 3.
    /// </summary>
    public int StabilityIterations { get; init; } = 3;

    /// <summary>
    /// Timeout in milliseconds after which provisional text becomes stable. Default: 2000.
    /// </summary>
    public int StabilityTimeoutMs { get; init; } = 2000;

    /// <summary>
    /// Cooldown in milliseconds to prevent rapid provisional updates. Default: 100.
    /// </summary>
    public int FlickerCooldownMs { get; init; } = 100;
}
