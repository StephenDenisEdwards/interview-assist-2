namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Intent detection mode determining which strategy to use.
/// </summary>
public enum IntentDetectionMode
{
    /// <summary>
    /// Fast regex-based heuristic detection only.
    /// Free, low latency, ~67% recall.
    /// </summary>
    Heuristic,

    /// <summary>
    /// LLM-based detection with context buffer.
    /// Higher latency, has cost, ~95% recall.
    /// </summary>
    Llm,

    /// <summary>
    /// Parallel: Heuristic for immediate response, LLM for verification.
    /// Best UX with corrections, highest cost.
    /// </summary>
    Parallel,

    /// <summary>
    /// Deepgram intent recognition via /v1/read REST endpoint.
    /// Reuses LLM strategy buffering with Deepgram as the detector backend.
    /// </summary>
    Deepgram
}
