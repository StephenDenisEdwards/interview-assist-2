using System.Text;
using System.Text.Json;
using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// OpenAI-based intent detector using GPT models.
/// </summary>
public sealed class OpenAiIntentDetector : ILlmIntentDetector
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly double _confidenceThreshold;

    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    private const string SystemPrompt = """
        You are an intent detection system analyzing real-time speech transcripts.
        Detect questions, imperatives, and other intents directed at a listener.

        For each detected intent, provide:
        - text: The question/imperative, made SELF-CONTAINED (resolve pronouns using context)
        - type: "Question" | "Imperative" | "Statement"
        - subtype: For questions: "Definition" | "HowTo" | "Compare" | "Troubleshoot" | null
                   For imperatives: "Stop" | "Repeat" | "Continue" | "Generate" | null
        - confidence: 0.0 to 1.0

        CRITICAL - Self-contained text:
        - Every detected item MUST make sense on its own without surrounding context
        - Resolve pronouns (it, this, that, they) using previous context
        - Examples:
          * "When should we use it?" where "it" = "abstract class" → "When should we use an abstract class?"
          * "What are the advantages?" where context = interfaces → "What are the advantages of using interfaces?"
        - If you cannot determine what a pronoun refers to, set confidence < 0.5

        Detection rules:
        - Questions: Direct questions (?) or implied questions ("Do you know...", "Can you tell me...")
        - Imperatives: Commands like "Explain...", "Describe...", "Stop", "Repeat", "Tell me about..."
        - Ignore: Filler words, partial sentences, meta-commentary about videos/tutorials
        - Ignore: "subscribe", "like", "comment" type content

        Question subtypes:
        - Definition: "What is X?", "Define X", "What does X mean?"
        - HowTo: "How do I...", "How can I...", "Steps to..."
        - Compare: "Difference between X and Y", "X vs Y"
        - Troubleshoot: "Why doesn't...", "Error with...", "Not working..."

        Imperative subtypes:
        - Stop: "Stop", "Cancel", "Nevermind"
        - Repeat: "Repeat", "Say that again"
        - Continue: "Continue", "Go on", "Next"
        - Generate: "Generate questions about..."

        Respond with JSON: {"intents": [...]}
        If no intents found, return: {"intents": []}
        """;

    public OpenAiIntentDetector(string apiKey, string model = "gpt-4o-mini", double confidenceThreshold = 0.7)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey));

        _model = model;
        _confidenceThreshold = confidenceThreshold;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<IReadOnlyList<DetectedIntent>> DetectIntentsAsync(
        string text,
        string? previousContext = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<DetectedIntent>();

        try
        {
            var userMessage = BuildUserMessage(text, previousContext);

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

            using var response = await _http.PostAsync(ChatCompletionsUrl, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"[OpenAI Error] {response.StatusCode}: {error}");
                return Array.Empty<DetectedIntent>();
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse(responseJson);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OpenAI Error] {ex.Message}");
            return Array.Empty<DetectedIntent>();
        }
    }

    private static string BuildUserMessage(string text, string? previousContext)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(previousContext))
        {
            sb.AppendLine("Previous context (for resolving pronouns and follow-ups):");
            sb.AppendLine(previousContext);
            sb.AppendLine();
        }

        sb.AppendLine("Current transcript to analyze:");
        sb.AppendLine(text);

        return sb.ToString();
    }

    private IReadOnlyList<DetectedIntent> ParseResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return Array.Empty<DetectedIntent>();

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
                return Array.Empty<DetectedIntent>();

            var contentText = content.GetString();
            if (string.IsNullOrWhiteSpace(contentText))
                return Array.Empty<DetectedIntent>();

            using var contentDoc = JsonDocument.Parse(contentText);
            var contentRoot = contentDoc.RootElement;

            if (!contentRoot.TryGetProperty("intents", out var intents))
                return Array.Empty<DetectedIntent>();

            var results = new List<DetectedIntent>();

            foreach (var item in intents.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? ""
                    : "";

                var confidence = item.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetDouble()
                    : 0.0;

                var typeStr = item.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString() ?? "Statement"
                    : "Statement";

                var subtypeStr = item.TryGetProperty("subtype", out var subtypeProp)
                    ? subtypeProp.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(text) || confidence < _confidenceThreshold)
                    continue;

                var intentType = typeStr.ToLowerInvariant() switch
                {
                    "question" => IntentType.Question,
                    "imperative" => IntentType.Imperative,
                    _ => IntentType.Statement
                };

                IntentSubtype? subtype = subtypeStr?.ToLowerInvariant() switch
                {
                    "definition" => IntentSubtype.Definition,
                    "howto" => IntentSubtype.HowTo,
                    "compare" => IntentSubtype.Compare,
                    "troubleshoot" => IntentSubtype.Troubleshoot,
                    "stop" => IntentSubtype.Stop,
                    "repeat" => IntentSubtype.Repeat,
                    "continue" => IntentSubtype.Continue,
                    "generate" => IntentSubtype.Generate,
                    _ => null
                };

                results.Add(new DetectedIntent
                {
                    Type = intentType,
                    Subtype = subtype,
                    Confidence = confidence,
                    SourceText = text
                });
            }

            return results;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[OpenAI Parse Error] {ex.Message}");
            return Array.Empty<DetectedIntent>();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
