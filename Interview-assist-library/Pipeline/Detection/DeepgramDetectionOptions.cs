namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Configuration options for Deepgram intent detection via /v1/read endpoint.
/// </summary>
public class DeepgramDetectionOptions
{
    /// <summary>
    /// Deepgram API key. If null, reads from DEEPGRAM_API_KEY environment variable.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Minimum confidence threshold for Deepgram intent detections.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Custom intents to include in the Deepgram request.
    /// These are verb phrases like "ask a question", "request clarification".
    /// </summary>
    public List<string> CustomIntents { get; set; } = new();

    /// <summary>
    /// Custom intent mode: "extended" (add to built-in) or "strict" (custom only).
    /// </summary>
    public string CustomIntentMode { get; set; } = "extended";

    /// <summary>
    /// HTTP request timeout in milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;
}
