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
    private readonly string _systemPrompt;

    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    // The system prompt must be provided via the systemPrompt constructor parameter,
    // typically loaded from a file (e.g. system-prompt.txt) configured via LlmDetectionOptions.SystemPromptFile.
    // See Interview-assist-transcription-detection-console/system-prompt.txt for the current prompt.

    /// <summary>
    /// Fires before each API call with the user message content.
    /// </summary>
    public event Action<string>? OnRequestSending;

    /// <summary>
    /// Fires after each API call completes with the elapsed time in milliseconds.
    /// </summary>
    public event Action<long>? OnRequestCompleted;

    /// <summary>
    /// The system prompt in use.
    /// </summary>
    public string SystemPrompt => _systemPrompt;

    public OpenAiIntentDetector(string apiKey, string model, double confidenceThreshold, string systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey));
        if (string.IsNullOrWhiteSpace(systemPrompt))
            throw new ArgumentException("A system prompt is required. Load one from a file via LlmDetectionOptions.SystemPromptFile.", nameof(systemPrompt));

        _model = model;
        _confidenceThreshold = confidenceThreshold;
        _systemPrompt = systemPrompt;
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

            OnRequestSending?.Invoke(userMessage);

            var requestBody = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = _systemPrompt },
                    new { role = "user", content = userMessage }
                },
                response_format = new { type = "json_object" },
                temperature = 0.1,
                max_completion_tokens = 1024
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _http.PostAsync(ChatCompletionsUrl, content, ct).ConfigureAwait(false);
            stopwatch.Stop();
            OnRequestCompleted?.Invoke(stopwatch.ElapsedMilliseconds);

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

                var originalText = item.TryGetProperty("original_text", out var origProp)
                    ? origProp.GetString()
                    : null;

                var utteranceId = item.TryGetProperty("utterance_id", out var uttIdProp)
                    ? uttIdProp.GetString()
                    : null;

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
                    SourceText = text,
                    OriginalText = originalText,
                    UtteranceId = utteranceId
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
