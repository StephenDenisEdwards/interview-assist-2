namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// Service for detecting questions and imperatives in transcript text.
/// </summary>
public interface IQuestionDetectionService
{
    /// <summary>
    /// Analyzes transcript text to detect questions or imperatives.
    /// </summary>
    /// <param name="transcriptText">The transcript text to analyze.</param>
    /// <param name="previousContext">Optional previous context for detecting follow-up questions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of detected questions with confidence scores.</returns>
    Task<IReadOnlyList<DetectedQuestion>> DetectQuestionsAsync(
        string transcriptText,
        string? previousContext = null,
        CancellationToken ct = default);
}

/// <summary>
/// A detected question or imperative from transcript analysis.
/// </summary>
public record DetectedQuestion
{
    /// <summary>
    /// The detected question or imperative text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Confidence score for the detection (0.0-1.0).
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// The type of detected item.
    /// </summary>
    public required QuestionType Type { get; init; }

    /// <summary>
    /// Unique identifier for deduplication.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the question was detected.
    /// </summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Types of detected questions/imperatives.
/// </summary>
public enum QuestionType
{
    /// <summary>
    /// Direct question (e.g., "What is dependency injection?")
    /// </summary>
    Question,

    /// <summary>
    /// Imperative statement (e.g., "Explain how async/await works")
    /// </summary>
    Imperative,

    /// <summary>
    /// Request for clarification (e.g., "Can you elaborate on that?")
    /// </summary>
    Clarification,

    /// <summary>
    /// Follow-up question based on previous context
    /// </summary>
    FollowUp
}
