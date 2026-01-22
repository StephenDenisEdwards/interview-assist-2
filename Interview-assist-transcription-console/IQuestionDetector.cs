namespace InterviewAssist.TranscriptionConsole;

/// <summary>
/// Interface for question detection in the transcription console.
/// </summary>
public interface IQuestionDetector
{
    /// <summary>
    /// Adds text to the detector's buffer for analysis.
    /// </summary>
    /// <param name="text">The transcribed text to add.</param>
    void AddText(string text);

    /// <summary>
    /// Detects questions from the buffered text.
    /// </summary>
    /// <returns>List of detected questions.</returns>
    Task<List<DetectedQuestion>> DetectQuestionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the name of this detector for display purposes.
    /// </summary>
    string Name { get; }
}

/// <summary>
/// Represents a detected question.
/// </summary>
public record DetectedQuestion(string Text, string Type, double Confidence = 1.0);

/// <summary>
/// Available question detection methods.
/// </summary>
public enum QuestionDetectionMethod
{
    /// <summary>
    /// Pattern-based heuristic detection (offline, fast, ~70-80% accuracy).
    /// </summary>
    Heuristic,

    /// <summary>
    /// LLM-based detection using OpenAI GPT (online, ~95% accuracy).
    /// </summary>
    Llm
}
