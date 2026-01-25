using InterviewAssist.Library.Context;

namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// Configuration options for the pipeline-based realtime API implementation.
/// </summary>
public record PipelineApiOptions
{
    /// <summary>
    /// OpenAI API key for all API calls (Whisper, GPT-4).
    /// </summary>
    public required string ApiKey { get; init; }

    // STT Configuration
    /// <summary>
    /// Transcription batch window in milliseconds. Default: 3000ms.
    /// </summary>
    public int TranscriptionBatchMs { get; init; } = 3000;

    /// <summary>
    /// Audio sample rate in Hz. Default: 16000 (matches AudioConstants.SampleRateHz).
    /// </summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary>
    /// RMS energy threshold for silence detection (0.0-1.0). Default: 0.01.
    /// Audio below this threshold is considered silence and not transcribed.
    /// Set to 0 to disable silence detection.
    /// </summary>
    public double SilenceEnergyThreshold { get; init; } = 0.01;

    /// <summary>
    /// Language code for Whisper transcription (e.g., "en", "es").
    /// Null for auto-detection. Specifying improves accuracy.
    /// </summary>
    public string? TranscriptionLanguage { get; init; } = "en";

    /// <summary>
    /// Optional vocabulary prompt to guide Whisper transcription.
    /// Include domain-specific terms like "C#, async, await, ConfigureAwait, Span".
    /// </summary>
    public string? TranscriptionPrompt { get; init; }

    /// <summary>
    /// Maximum transcription batch window in milliseconds. Default: 6000ms.
    /// Transcription flushes at this limit even without silence detection.
    /// Used with adaptive batching to cap latency while allowing speech boundaries.
    /// </summary>
    public int MaxTranscriptionBatchMs { get; init; } = 6000;

    // Question Detection Configuration
    /// <summary>
    /// Model to use for question detection. Default: gpt-4o-mini.
    /// </summary>
    public string DetectionModel { get; init; } = "gpt-4o-mini";

    /// <summary>
    /// Minimum confidence threshold for question detection (0.0-1.0). Default: 0.7.
    /// </summary>
    public double DetectionConfidenceThreshold { get; init; } = 0.7;

    /// <summary>
    /// Interval between detection checks in milliseconds. Default: 1500ms.
    /// </summary>
    public int DetectionIntervalMs { get; init; } = 1500;

    /// <summary>
    /// Rolling transcript buffer duration in seconds. Default: 30.
    /// </summary>
    public int TranscriptBufferSeconds { get; init; } = 30;

    // Response Generation Configuration
    /// <summary>
    /// Model to use for response generation. Default: gpt-4o.
    /// </summary>
    public string ResponseModel { get; init; } = "gpt-4o";

    /// <summary>
    /// Maximum tokens for response generation. Default: 2048.
    /// </summary>
    public int MaxResponseTokens { get; init; } = 2048;

    /// <summary>
    /// Temperature for response generation (0.0-2.0). Default: 0.3.
    /// </summary>
    public double Temperature { get; init; } = 0.3;

    // Context (same pattern as RealtimeApiOptions)
    /// <summary>
    /// Extra instructions/context preview for the assistant.
    /// </summary>
    public string? ExtraInstructions { get; init; }

    /// <summary>
    /// Context chunks (CV, job spec) for detailed reference.
    /// </summary>
    public IReadOnlyList<ContextChunk>? ContextChunks { get; init; }

    /// <summary>
    /// Custom system instructions. Uses default C# expert instructions if not set.
    /// </summary>
    public string? SystemInstructions { get; init; }

    // Queueing behavior
    /// <summary>
    /// Maximum number of questions that can be queued. Default: 5.
    /// </summary>
    public int MaxQueuedQuestions { get; init; } = 5;

    /// <summary>
    /// Whether to process multiple questions in parallel. Default: false.
    /// </summary>
    public bool AllowParallelResponses { get; init; } = false;

    // Deduplication settings
    /// <summary>
    /// Jaccard similarity threshold for question deduplication (0.0-1.0). Default: 0.7.
    /// Questions with similarity >= this threshold are considered duplicates.
    /// </summary>
    public double DeduplicationSimilarityThreshold { get; init; } = 0.7;

    /// <summary>
    /// Time window in milliseconds for question suppression. Default: 30000 (30 seconds).
    /// Similar questions within this window are considered duplicates.
    /// </summary>
    public int DeduplicationWindowMs { get; init; } = 30000;
}
