using System.Text;
using System.Text.Json;
using InterviewAssist.TranscriptionConsole;

/// <summary>
/// LLM-based question detector using OpenAI GPT models.
/// Provides higher accuracy than heuristic detection but requires API calls.
/// </summary>
public class LlmQuestionDetector : IQuestionDetector, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly double _confidenceThreshold;
    private readonly int _detectionIntervalMs;
    private readonly StringBuilder _buffer = new();
    private readonly HashSet<string> _detectedQuestions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _detectedQuestionsLock = new();
    private DateTime _lastDetection = DateTime.MinValue;
    private const int MaxBufferLength = 2500;
    private const int MaxDetectedHistory = 50;
    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    private const string SystemPrompt = """
        You are a question detection system analyzing TECHNICAL INTERVIEW transcripts.
        Your task is to identify TECHNICAL questions or imperatives that test programming knowledge.

        For each detected item, provide:
        - text: The question or imperative, made SELF-CONTAINED (see rules below)
        - type: One of "Question", "Imperative", "Clarification", or "Follow-up"
        - confidence: A score from 0.0 to 1.0 indicating your confidence

        CRITICAL - Making questions self-contained:
        - Every question MUST make sense on its own without needing surrounding context
        - If a question contains pronouns (it, this, that, they, them) that refer to a technical subject, RESOLVE the pronoun
        - Examples:
          * "When should we use it?" where "it" = "abstract class" → "When should we use an abstract class?"
          * "What are the advantages?" where context = interfaces → "What are the advantages of using interfaces?"
          * "What is a jagged array?" → Keep as-is (already self-contained)
        - If you cannot determine what a pronoun refers to, skip the question

        MUST IGNORE (do NOT detect these):
        - Promotional content: "subscribe", "like", "comment", "visit my site/channel"
        - Meta/intro content: "what are we doing today", "welcome to", "in this video"
        - Conversational filler: "how are you", "what do you think", "anything else"
        - Transcription artifacts: repeated words like "you you you", partial sentences
        - Non-technical questions about the video/tutorial structure

        ONLY DETECT:
        - Technical interview questions about programming concepts
        - Imperatives like "Explain...", "Describe...", "Walk me through..."
        - Follow-up technical questions

        Respond with a JSON object: {"detected": [...]}
        If no technical questions found, return: {"detected": []}
        """;

    public LlmQuestionDetector(
        string apiKey,
        string model = "gpt-4o-mini",
        double confidenceThreshold = 0.7,
        int detectionIntervalMs = 1000)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model;
        _confidenceThreshold = confidenceThreshold;
        _detectionIntervalMs = detectionIntervalMs;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public string Name => $"LLM ({_model})";

    public void AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _buffer.Append(' ');
        _buffer.Append(text);

        // Trim buffer if too long
        if (_buffer.Length > MaxBufferLength)
        {
            var excess = _buffer.Length - MaxBufferLength;
            _buffer.Remove(0, excess);
        }
    }

    public async Task<List<DetectedQuestion>> DetectQuestionsAsync(CancellationToken ct = default)
    {
        var results = new List<DetectedQuestion>();

        // Rate limit detection calls
        var elapsed = DateTime.UtcNow - _lastDetection;
        if (elapsed.TotalMilliseconds < _detectionIntervalMs)
        {
            return results;
        }

        var text = _buffer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length < 15)
        {
            return results;
        }

        _lastDetection = DateTime.UtcNow;

        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = $"Analyze this transcript:\n\n{text}" }
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
                Console.Error.WriteLine($"[LLM Detection Error] {response.StatusCode}: {error}");
                return results;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var allDetected = ParseDetectionResponse(responseJson);

            // Filter out already-detected questions (thread-safe)
            lock (_detectedQuestionsLock)
            {
                foreach (var question in allDetected)
                {
                    var normalizedText = NormalizeQuestion(question.Text);

                    // Check if we've already detected this question (or very similar)
                    if (!IsAlreadyDetectedUnsafe(normalizedText))
                    {
                        results.Add(question);
                        _detectedQuestions.Add(normalizedText);

                        // Clear the portion of buffer containing this question
                        ClearDetectedFromBuffer(question.Text);
                    }
                }

                // Keep detected history bounded
                if (_detectedQuestions.Count > MaxDetectedHistory)
                {
                    // Remove oldest entries (HashSet doesn't preserve order, so just clear half)
                    var toKeep = _detectedQuestions.Skip(_detectedQuestions.Count / 2).ToList();
                    _detectedQuestions.Clear();
                    foreach (var q in toKeep)
                        _detectedQuestions.Add(q);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LLM Detection Error] {ex.Message}");
        }

        return results;
    }

    private static string NormalizeQuestion(string text)
    {
        // Normalize for comparison - lowercase, trim, remove extra spaces
        return string.Join(" ", text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Checks if a question was already detected. Must be called within _detectedQuestionsLock.
    /// </summary>
    private bool IsAlreadyDetectedUnsafe(string normalizedText)
    {
        // Exact match
        if (_detectedQuestions.Contains(normalizedText))
            return true;

        // Check for high similarity (questions that are mostly the same)
        foreach (var existing in _detectedQuestions)
        {
            if (IsSimilar(normalizedText, existing))
                return true;
        }

        return false;
    }

    private static bool IsSimilar(string a, string b)
    {
        // Very short strings - require exact match
        if (a.Length < 15 || b.Length < 15)
            return a == b;

        // Check if one contains the other
        if (a.Contains(b) || b.Contains(a))
            return true;

        // Word-based Jaccard similarity
        var wordsA = GetSignificantWords(a);
        var wordsB = GetSignificantWords(b);

        if (wordsA.Count == 0 || wordsB.Count == 0)
            return false;

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();

        var jaccard = (double)intersection / union;

        // If 60% or more of significant words overlap, consider similar
        return jaccard >= 0.6;
    }

    private static HashSet<string> GetSignificantWords(string text)
    {
        // Stop words to ignore when comparing
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
            "in", "on", "at", "to", "for", "of", "with", "by", "from", "as",
            "and", "or", "but", "if", "then", "else", "when", "where", "why", "how",
            "what", "which", "who", "whom", "this", "that", "these", "those",
            "do", "does", "did", "have", "has", "had", "can", "could", "would", "should",
            "will", "shall", "may", "might", "must", "you", "your", "we", "our", "me", "i"
        };

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', '?', '!', ',', ';', ':'))
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return words;
    }

    private void ClearDetectedFromBuffer(string questionText)
    {
        // Only remove the specific question text, not everything before it
        // This preserves context that may contain other undetected questions
        var bufferText = _buffer.ToString();
        var idx = bufferText.IndexOf(questionText, StringComparison.OrdinalIgnoreCase);

        if (idx >= 0)
        {
            // Remove only the question text itself
            _buffer.Remove(idx, questionText.Length);
        }
        // If exact match not found, the LLM cleaned up the text
        // Don't aggressively clear - the deduplication logic will prevent re-detection
    }

    private List<DetectedQuestion> ParseDetectionResponse(string responseJson)
    {
        var results = new List<DetectedQuestion>();

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return results;

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
                return results;

            var contentText = content.GetString();
            if (string.IsNullOrWhiteSpace(contentText))
                return results;

            using var contentDoc = JsonDocument.Parse(contentText);
            var contentRoot = contentDoc.RootElement;

            if (!contentRoot.TryGetProperty("detected", out var detected))
                return results;

            foreach (var item in detected.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? ""
                    : "";

                var confidence = item.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetDouble()
                    : 0.0;

                var type = item.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString() ?? "Question"
                    : "Question";

                if (!string.IsNullOrWhiteSpace(text) && confidence >= _confidenceThreshold)
                {
                    results.Add(new DetectedQuestion(text, type, confidence));
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parsing errors
        }

        return results;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
