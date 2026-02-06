namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Configuration options for intent detection.
/// </summary>
public class IntentDetectionOptions
{
    /// <summary>
    /// Whether intent detection is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Detection mode to use.
    /// </summary>
    public IntentDetectionMode Mode { get; set; } = IntentDetectionMode.Heuristic;

    /// <summary>
    /// Options for heuristic detection.
    /// </summary>
    public HeuristicDetectionOptions Heuristic { get; set; } = new();

    /// <summary>
    /// Options for LLM detection.
    /// </summary>
    public LlmDetectionOptions Llm { get; set; } = new();

    /// <summary>
    /// Options for Deepgram intent detection.
    /// </summary>
    public DeepgramDetectionOptions Deepgram { get; set; } = new();
}

/// <summary>
/// Options for heuristic-based detection.
/// </summary>
public class HeuristicDetectionOptions
{
    /// <summary>
    /// Minimum confidence threshold for detection.
    /// </summary>
    public double MinConfidence { get; set; } = 0.4;
}

/// <summary>
/// Options for LLM-based detection.
/// </summary>
public class LlmDetectionOptions
{
    /// <summary>
    /// OpenAI model to use.
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// OpenAI API key. If null, reads from OPENAI_API_KEY environment variable.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Minimum confidence threshold for LLM detections.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Minimum milliseconds between LLM API calls.
    /// </summary>
    public int RateLimitMs { get; set; } = 2000;

    /// <summary>
    /// Maximum characters to accumulate in context buffer.
    /// </summary>
    public int BufferMaxChars { get; set; } = 800;

    /// <summary>
    /// Trigger LLM detection when question mark is detected.
    /// </summary>
    public bool TriggerOnQuestionMark { get; set; } = true;

    /// <summary>
    /// Trigger LLM detection on speech pause.
    /// </summary>
    public bool TriggerOnPause { get; set; } = true;

    /// <summary>
    /// Trigger LLM detection after this many ms without new input.
    /// </summary>
    public int TriggerTimeoutMs { get; set; } = 3000;

    /// <summary>
    /// Enable preprocessing (noise removal, technical term correction).
    /// </summary>
    public bool EnablePreprocessing { get; set; } = true;

    /// <summary>
    /// Enable deduplication of detected intents.
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Time window for deduplication in milliseconds.
    /// </summary>
    public int DeduplicationWindowMs { get; set; } = 30000;
}
