using InterviewAssist.Library.Context;

namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// Service for generating responses using Chat Completions API.
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// Generates a response to a question using Chat Completions API.
    /// Streams deltas via callback for real-time UI updates.
    /// </summary>
    /// <param name="question">The question to answer.</param>
    /// <param name="conversationContext">Recent conversation context for follow-up understanding.</param>
    /// <param name="contextChunks">Optional CV/job spec context chunks.</param>
    /// <param name="systemInstructions">Optional custom system instructions.</param>
    /// <param name="onDelta">Callback for streaming text deltas.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The complete response with answer and code.</returns>
    Task<ChatResponse> GenerateResponseAsync(
        string question,
        string? conversationContext,
        IReadOnlyList<ContextChunk>? contextChunks,
        string? systemInstructions,
        Action<string>? onDelta,
        CancellationToken ct = default);
}

/// <summary>
/// Response from the chat completion service.
/// </summary>
public record ChatResponse
{
    /// <summary>
    /// The explanation/answer text.
    /// </summary>
    public required string Answer { get; init; }

    /// <summary>
    /// The code example (C# console application).
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// The function name that was called (report_technical_response).
    /// </summary>
    public string FunctionName { get; init; } = "report_technical_response";

    /// <summary>
    /// Whether the response was generated via function call or text extraction.
    /// </summary>
    public bool WasFunctionCall { get; init; }
}
