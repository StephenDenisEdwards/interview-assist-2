using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Interface for LLM-based intent detection.
/// </summary>
public interface ILlmIntentDetector : IDisposable
{
    /// <summary>
    /// Detect intents in the given text using LLM.
    /// </summary>
    /// <param name="text">Text to analyze.</param>
    /// <param name="previousContext">Optional previous context for follow-up detection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of detected intents.</returns>
    Task<IReadOnlyList<DetectedIntent>> DetectIntentsAsync(
        string text,
        string? previousContext = null,
        CancellationToken ct = default);
}
