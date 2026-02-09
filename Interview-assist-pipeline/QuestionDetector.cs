using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace InterviewAssist.Pipeline;

public enum QuestionType
{
    Question,
    Imperative,
    Clarification,
    FollowUp
}

public sealed record DetectedQuestion(
    string Text,
    QuestionType Type,
    double Confidence,
    string? OriginalText = null);

/// <summary>
/// Analyzes transcript text for questions and imperatives using GPT.
/// Call AnalyzeAsync immediately when a transcript arrives.
/// </summary>
public sealed class QuestionDetector : IDisposable
{
    private const string SystemPrompt = """
        You analyze interview transcripts to detect questions or imperatives that a job candidate needs to answer.

        IMPORTANT: The transcript is from an interviewer speaking. Extract any questions they ask, even if:
        - Introduced with "The question is...", "The last question was...", "I'd like to ask..."
        - Phrased as statements that expect an answer
        - Split across sentences or partially transcribed

        Detect:
        - Direct questions (e.g., "How would you handle...?", "What is your experience with...?")
        - Imperatives requiring a response: "Explain...", "Describe...", "Tell me about...", "Walk me through..."
        - Clarifications: "Can you elaborate?", "What do you mean by...?"
        - Follow-ups: "And how does that...?", "What about...?"

        CRITICAL - Two text fields:
        - original_text: Copy the EXACT words from the transcript. Do not modify, clean up, or rephrase.
        - text: Make the question SELF-CONTAINED by resolving pronouns and adding context.
          Also clean up the text field: remove prefixes like "The question is", reconstruct partial questions.
        - If the question is already self-contained, both fields will be the same.
        - Examples:
          * "When should we use it?" where "it" refers to "abstract class":
            original_text: "When should we use it?"
            text: "When should we use an abstract class?"
          * "What are the advantages?" where context is about interfaces:
            original_text: "What are the advantages?"
            text: "What are the advantages of using interfaces?"
          * "Can you explain that?" where "that" refers to CQRS:
            original_text: "Can you explain that?"
            text: "Can you explain CQRS?"
        - Keep questions that are already self-contained as-is (both fields identical)
        - If you cannot determine what a pronoun refers to, skip the question
        - Keep technical terms even if transcribed oddly (e.g., "blazer" likely means "Blazor")

        Respond with JSON:
        {
          "detected": [
            {
              "original_text": "the exact words from the transcript",
              "text": "the question, cleaned up, complete, and SELF-CONTAINED",
              "type": "question|imperative|clarification|follow_up",
              "confidence": 0.0-1.0
            }
          ]
        }

        If no questions/imperatives found, return: {"detected": []}
        Only include items with confidence >= 0.6.
        """;

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly double _minConfidence;

    public event Action<DetectedQuestion>? OnQuestionDetected;
    public event Action<string>? OnError;

    public QuestionDetector(
        string apiKey,
        string model = "gpt-4o-mini",
        double minConfidence = 0.7)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey));

        _model = model;
        _minConfidence = minConfidence;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Analyzes transcript text immediately and fires OnQuestionDetected for each found.
    /// </summary>
    public async Task AnalyzeAsync(string transcript, string? recentContext = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return;

        try
        {
            var userMessage = BuildUserMessage(transcript, recentContext);

            var requestBody = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = userMessage }
                },
                response_format = new { type = "json_object" },
                temperature = 0.1,
                max_completion_tokens = 512
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                content,
                ct).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                OnError?.Invoke($"Detection API error: {response.StatusCode} - {body}");
                return;
            }

            var questions = ParseResponse(body);
            foreach (var q in questions)
            {
                if (q.Confidence >= _minConfidence)
                {
                    OnQuestionDetected?.Invoke(q);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Detection error: {ex.Message}");
        }
    }

    private static string BuildUserMessage(string transcript, string? recentContext)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(recentContext))
        {
            sb.AppendLine("Recent context:");
            sb.AppendLine(recentContext);
            sb.AppendLine();
        }

        sb.AppendLine("New transcript to analyze:");
        sb.AppendLine(transcript);

        return sb.ToString();
    }

    private static List<DetectedQuestion> ParseResponse(string responseJson)
    {
        var result = new List<DetectedQuestion>();

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return result;

            var messageContent = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(messageContent))
                return result;

            using var contentDoc = JsonDocument.Parse(messageContent);
            if (!contentDoc.RootElement.TryGetProperty("detected", out var detected))
                return result;

            foreach (var item in detected.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var t) ? t.GetString() : null;
                var originalText = item.TryGetProperty("original_text", out var ot) ? ot.GetString() : null;
                var typeStr = item.TryGetProperty("type", out var tp) ? tp.GetString() : "question";
                var confidence = item.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var type = typeStr?.ToLowerInvariant() switch
                {
                    "imperative" => QuestionType.Imperative,
                    "clarification" => QuestionType.Clarification,
                    "follow_up" or "followup" => QuestionType.FollowUp,
                    _ => QuestionType.Question
                };

                result.Add(new DetectedQuestion(text, type, confidence, originalText));
            }
        }
        catch
        {
            // Parsing failed, return empty
        }

        return result;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
