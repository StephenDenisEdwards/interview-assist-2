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
    /// Audio sample rate in Hz. Default: 24000.
    /// </summary>
    public int SampleRate { get; init; } = 24000;

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
}
