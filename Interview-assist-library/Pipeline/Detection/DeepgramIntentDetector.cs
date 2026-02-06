using System.Text;
using System.Text.Json;
using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Deepgram-based intent detector using the /v1/read REST endpoint.
/// Implements ILlmIntentDetector so it can be used with LlmIntentStrategy.
/// </summary>
public sealed class DeepgramIntentDetector : ILlmIntentDetector
{
    private readonly HttpClient _http;
    private readonly double _confidenceThreshold;
    private readonly List<string> _customIntents;
    private readonly string _customIntentMode;

    private const string BaseUrl = "https://api.deepgram.com/v1/read";

    public DeepgramIntentDetector(string apiKey, DeepgramDetectionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey));

        options ??= new DeepgramDetectionOptions();
        _confidenceThreshold = options.ConfidenceThreshold;
        _customIntents = options.CustomIntents;
        _customIntentMode = options.CustomIntentMode;

        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs) };
        _http.DefaultRequestHeaders.Add("Authorization", $"Token {apiKey}");
    }

    /// <summary>
    /// Internal constructor for unit testing with a pre-configured HttpClient.
    /// </summary>
    internal DeepgramIntentDetector(HttpClient httpClient, DeepgramDetectionOptions? options = null)
    {
        options ??= new DeepgramDetectionOptions();
        _confidenceThreshold = options.ConfidenceThreshold;
        _customIntents = options.CustomIntents;
        _customIntentMode = options.CustomIntentMode;
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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
            var url = BuildRequestUrl();

            var requestBody = JsonSerializer.Serialize(new { text });
            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"[Deepgram Error] {response.StatusCode}: {error}");
                return Array.Empty<DetectedIntent>();
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse(responseJson, text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Deepgram Error] {ex.Message}");
            return Array.Empty<DetectedIntent>();
        }
    }

    private string BuildRequestUrl()
    {
        var sb = new StringBuilder(BaseUrl);
        sb.Append("?intents=true&language=en");

        foreach (var intent in _customIntents)
        {
            sb.Append("&custom_intent=");
            sb.Append(Uri.EscapeDataString(intent));
        }

        if (_customIntents.Count > 0)
        {
            sb.Append("&custom_intent_mode=");
            sb.Append(Uri.EscapeDataString(_customIntentMode));
        }

        return sb.ToString();
    }

    private IReadOnlyList<DetectedIntent> ParseResponse(string responseJson, string sourceText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results))
                return Array.Empty<DetectedIntent>();

            if (!results.TryGetProperty("intents", out var intents))
                return Array.Empty<DetectedIntent>();

            if (!intents.TryGetProperty("segments", out var segments))
                return Array.Empty<DetectedIntent>();

            var detectedIntents = new List<DetectedIntent>();

            foreach (var segment in segments.EnumerateArray())
            {
                var segmentText = segment.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? sourceText
                    : sourceText;

                if (!segment.TryGetProperty("intents", out var segmentIntents))
                    continue;

                foreach (var intent in segmentIntents.EnumerateArray())
                {
                    var label = intent.TryGetProperty("intent", out var intentProp)
                        ? intentProp.GetString() ?? ""
                        : "";

                    var confidence = intent.TryGetProperty("confidence_score", out var confProp)
                        ? confProp.GetDouble()
                        : 0.0;

                    if (string.IsNullOrWhiteSpace(label) || confidence < _confidenceThreshold)
                        continue;

                    var (intentType, subtype) = ClassifyIntent(label);

                    detectedIntents.Add(new DetectedIntent
                    {
                        Type = intentType,
                        Subtype = subtype,
                        Confidence = confidence,
                        SourceText = segmentText
                    });
                }
            }

            return detectedIntents;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[Deepgram Parse Error] {ex.Message}");
            return Array.Empty<DetectedIntent>();
        }
    }

    private static (IntentType Type, IntentSubtype? Subtype) ClassifyIntent(string label)
    {
        var lower = label.ToLowerInvariant();

        // Question-indicating intents (explicit question words)
        if (lower.Contains("ask") || lower.Contains("question") ||
            lower.Contains("inquire") || lower.Contains("wonder"))
        {
            var subtype = InferQuestionSubtype(lower);
            return (IntentType.Question, subtype);
        }

        // Imperative forms that are effectively questions (asking for information)
        if (lower.Contains("explain") || lower.Contains("describe") || lower.Contains("tell"))
        {
            return (IntentType.Question, InferQuestionSubtype(lower));
        }

        // Information-seeking intents (Deepgram often returns verb-phrase labels like
        // "Find out difference...", "Learn about...", "Understand...")
        if (lower.Contains("find out") || lower.Contains("learn") || lower.Contains("understand") ||
            lower.Contains("know") || lower.Contains("clarif") || lower.Contains("seek"))
        {
            return (IntentType.Question, InferQuestionSubtype(lower));
        }

        // Stop/cancel imperatives
        if (lower.Contains("stop") || lower.Contains("cancel") || lower.Contains("quit"))
        {
            return (IntentType.Imperative, IntentSubtype.Stop);
        }

        // Repeat imperatives
        if (lower.Contains("repeat") || lower.Contains("again"))
        {
            return (IntentType.Imperative, IntentSubtype.Repeat);
        }

        // Continue imperatives
        if (lower.Contains("continue") || lower.Contains("go on") || lower.Contains("next"))
        {
            return (IntentType.Imperative, IntentSubtype.Continue);
        }

        // Generate imperatives
        if (lower.Contains("generate") || lower.Contains("create") || lower.Contains("make"))
        {
            return (IntentType.Imperative, IntentSubtype.Generate);
        }

        // Fallback
        return (IntentType.Statement, null);
    }

    private static IntentSubtype? InferQuestionSubtype(string label)
    {
        if (label.Contains("define") || label.Contains("definition") || label.Contains("what is") || label.Contains("meaning"))
            return IntentSubtype.Definition;

        if (label.Contains("how") || label.Contains("steps") || label.Contains("process"))
            return IntentSubtype.HowTo;

        if (label.Contains("compare") || label.Contains("difference") || label.Contains("versus") || label.Contains(" vs "))
            return IntentSubtype.Compare;

        if (label.Contains("troubleshoot") || label.Contains("error") || label.Contains("fix") || label.Contains("problem"))
            return IntentSubtype.Troubleshoot;

        return null;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
