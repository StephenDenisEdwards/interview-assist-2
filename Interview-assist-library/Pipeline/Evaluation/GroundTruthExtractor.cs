using System.Text;
using System.Text.Json;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Extracts ground truth questions from a transcript using GPT-4o.
/// </summary>
public sealed class GroundTruthExtractor : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;

    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    private const string SystemPrompt = """
        You are a question extraction system. Your task is to identify ALL questions in a transcript.

        For each question found, provide:
        - text: The exact question text as it appears (or slightly cleaned up for clarity)
        - subtype: "Definition" | "HowTo" | "Compare" | "Troubleshoot" | "Rhetorical" | "Clarification" | null
        - confidence: How confident you are this is a genuine question (0.0 to 1.0)
        - position: Approximate character position in the transcript where the question appears

        Question types to detect:
        - Direct questions with question marks
        - Implied questions ("I wonder if...", "Do you know...")
        - Interview questions ("Tell me about...", "Can you explain...")
        - Rhetorical questions (still count them, mark subtype as "Rhetorical")

        DO NOT include:
        - Incomplete sentence fragments
        - Statements that don't seek information
        - Filler phrases

        Be thorough - extract EVERY question, even if they seem similar.
        Questions from all speakers should be included.

        Respond with JSON: {"questions": [...]}
        If no questions found: {"questions": []}
        """;

    public GroundTruthExtractor(string apiKey, string model = "gpt-4o")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey));

        _model = model;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    /// <summary>
    /// Extract all questions from the full transcript.
    /// </summary>
    public async Task<IReadOnlyList<ExtractedQuestion>> ExtractQuestionsAsync(
        string fullTranscript,
        CancellationToken ct = default)
    {
        var result = await ExtractQuestionsWithRawAsync(fullTranscript, ct);
        return result.Questions;
    }

    /// <summary>
    /// Extract all questions from the full transcript, returning both parsed questions and the raw LLM response.
    /// </summary>
    public async Task<GroundTruthResult> ExtractQuestionsWithRawAsync(
        string fullTranscript,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullTranscript))
            return new GroundTruthResult(Array.Empty<ExtractedQuestion>(), string.Empty);

        try
        {
            var userMessage = $"Extract all questions from this transcript:\n\n{fullTranscript}";

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
                max_completion_tokens = 4096
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(ChatCompletionsUrl, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"[GroundTruth Error] {response.StatusCode}: {error}");
                return new GroundTruthResult(Array.Empty<ExtractedQuestion>(), string.Empty);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var rawContent = ExtractContentFromResponse(responseJson);
            var questions = ParseResponse(responseJson);
            return new GroundTruthResult(questions, rawContent);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GroundTruth Error] {ex.Message}");
            return new GroundTruthResult(Array.Empty<ExtractedQuestion>(), string.Empty);
        }
    }

    private static string ExtractContentFromResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return string.Empty;

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
                return string.Empty;

            return content.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<ExtractedQuestion> ParseResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return Array.Empty<ExtractedQuestion>();

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
                return Array.Empty<ExtractedQuestion>();

            var contentText = content.GetString();
            if (string.IsNullOrWhiteSpace(contentText))
                return Array.Empty<ExtractedQuestion>();

            using var contentDoc = JsonDocument.Parse(contentText);
            var contentRoot = contentDoc.RootElement;

            if (!contentRoot.TryGetProperty("questions", out var questions))
                return Array.Empty<ExtractedQuestion>();

            var results = new List<ExtractedQuestion>();

            foreach (var item in questions.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? ""
                    : "";

                var confidence = item.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetDouble()
                    : 0.8;

                var subtype = item.TryGetProperty("subtype", out var subtypeProp)
                    ? subtypeProp.GetString()
                    : null;

                var position = item.TryGetProperty("position", out var posProp)
                    ? posProp.GetInt32()
                    : 0;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    results.Add(new ExtractedQuestion(
                        Text: text,
                        Subtype: subtype,
                        Confidence: confidence,
                        ApproximatePosition: position));
                }
            }

            return results;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[GroundTruth Parse Error] {ex.Message}");
            return Array.Empty<ExtractedQuestion>();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
