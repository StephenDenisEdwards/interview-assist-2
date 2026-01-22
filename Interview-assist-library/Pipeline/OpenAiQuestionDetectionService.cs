using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// OpenAI GPT-4 based implementation of question detection.
/// Uses structured JSON output to identify questions and imperatives.
/// </summary>
public sealed class OpenAiQuestionDetectionService : IQuestionDetectionService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly double _confidenceThreshold;
    private readonly ILogger<OpenAiQuestionDetectionService> _logger;

    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    private const string SystemPrompt = """
        You are a question detection system analyzing interview transcripts.
        Your task is to identify questions or imperatives directed at the interviewee.

        For each detected item, provide:
        - text: The question or imperative, made SELF-CONTAINED (see critical rules below)
        - type: One of "question", "imperative", "clarification", or "follow_up"
        - confidence: A score from 0.0 to 1.0 indicating your confidence

        CRITICAL - Making questions self-contained:
        - Every question MUST make sense on its own without needing surrounding context
        - If a question contains pronouns (it, this, that, they, them) that refer to a subject mentioned earlier, RESOLVE the pronoun by including the subject
        - Examples of resolving references:
          * "When should we use it?" where "it" refers to "abstract class" → "When should we use an abstract class?"
          * "What are the advantages?" where context is about interfaces → "What are the advantages of using interfaces?"
          * "Can you explain that further?" where "that" refers to dependency injection → "Can you explain dependency injection further?"
        - Keep questions that are already self-contained as-is:
          * "Can you store different types in an array?" → Keep as-is
          * "What is a jagged array?" → Keep as-is
        - If you cannot determine what a pronoun refers to from the context, skip the question

        Detection guidelines:
        - Questions: Direct questions ending in ? or implied questions
        - Imperatives: Commands like "Explain...", "Describe...", "Tell me about...", "Walk me through..."
        - Clarifications: "Can you elaborate?", "What do you mean by...?"
        - Follow-ups: Questions that reference previous context (MUST resolve the reference!)

        Only include items you're confident are actual interview questions/imperatives.
        Ignore filler words, partial sentences, transcription artifacts, and rhetorical questions.
        Ignore meta-questions about how a video/tutorial is structured.

        Respond with a JSON object containing a "detected" array. If no questions found, return empty array.
        """;

    public OpenAiQuestionDetectionService(
        string apiKey,
        string model = "gpt-4o-mini",
        double confidenceThreshold = 0.7,
        ILogger<OpenAiQuestionDetectionService>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model;
        _confidenceThreshold = confidenceThreshold;
        _logger = logger ?? NullLogger<OpenAiQuestionDetectionService>.Instance;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<IReadOnlyList<DetectedQuestion>> DetectQuestionsAsync(
        string transcriptText,
        string? previousContext = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            return Array.Empty<DetectedQuestion>();
        }

        try
        {
            var userMessage = BuildUserMessage(transcriptText, previousContext);

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
                max_tokens = 1024
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("Sending detection request for {Length} chars of transcript", transcriptText.Length);

            using var response = await _http.PostAsync(ChatCompletionsUrl, content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseDetectionResponse(responseJson);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Question detection failed");
            return Array.Empty<DetectedQuestion>();
        }
    }

    private static string BuildUserMessage(string transcriptText, string? previousContext)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(previousContext))
        {
            sb.AppendLine("Previous context (for detecting follow-ups):");
            sb.AppendLine(previousContext);
            sb.AppendLine();
        }

        sb.AppendLine("Current transcript to analyze:");
        sb.AppendLine(transcriptText);

        return sb.ToString();
    }

    private IReadOnlyList<DetectedQuestion> ParseDetectionResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                _logger.LogWarning("No choices in detection response");
                return Array.Empty<DetectedQuestion>();
            }

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
            {
                _logger.LogWarning("No message content in detection response");
                return Array.Empty<DetectedQuestion>();
            }

            var contentText = content.GetString();
            if (string.IsNullOrWhiteSpace(contentText))
            {
                return Array.Empty<DetectedQuestion>();
            }

            using var contentDoc = JsonDocument.Parse(contentText);
            var contentRoot = contentDoc.RootElement;

            if (!contentRoot.TryGetProperty("detected", out var detected))
            {
                _logger.LogDebug("No 'detected' array in response");
                return Array.Empty<DetectedQuestion>();
            }

            var results = new List<DetectedQuestion>();

            foreach (var item in detected.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? ""
                    : "";

                var confidence = item.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetDouble()
                    : 0.0;

                var typeStr = item.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString() ?? "question"
                    : "question";

                if (string.IsNullOrWhiteSpace(text) || confidence < _confidenceThreshold)
                {
                    continue;
                }

                var questionType = typeStr.ToLowerInvariant() switch
                {
                    "imperative" => QuestionType.Imperative,
                    "clarification" => QuestionType.Clarification,
                    "follow_up" or "followup" => QuestionType.FollowUp,
                    _ => QuestionType.Question
                };

                results.Add(new DetectedQuestion
                {
                    Text = text,
                    Confidence = confidence,
                    Type = questionType
                });

                _logger.LogDebug("Detected {Type}: \"{Text}\" (confidence: {Confidence:F2})",
                    questionType, text.Length > 50 ? text[..50] + "..." : text, confidence);
            }

            _logger.LogInformation("Detected {Count} question(s) above threshold {Threshold}",
                results.Count, _confidenceThreshold);

            return results;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse detection response JSON");
            return Array.Empty<DetectedQuestion>();
        }
    }
}
