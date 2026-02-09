using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly int _minBufferLength;
    private readonly int _deduplicationWindowMs;
    private readonly bool _enableTechnicalTermCorrection;
    private readonly bool _enableNoiseFilter;
    private readonly StringBuilder _buffer = new();
    private readonly HashSet<string> _detectedQuestions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _detectionTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _detectedQuestionsLock = new();
    private DateTime _lastDetection = DateTime.MinValue;
    private DateTime _lastBufferChange = DateTime.MinValue;
    private bool _hasPendingCandidate;
    private bool _confirmedByPause;
    private readonly int _stabilityWindowMs;
    private const int MaxBufferLength = 2500;
    private const int MaxDetectedHistory = 50;
    private const double JaccardThreshold = 0.7;
    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    /// <summary>
    /// Dictionary of common misheard programming terms from speech-to-text.
    /// Keys are case-insensitive patterns, values are the correct technical terms.
    /// </summary>
    private static readonly Dictionary<string, string> TechnicalTermCorrections = new(StringComparer.OrdinalIgnoreCase)
    {
        // Generic type patterns
        { "span t", "Span<T>" },
        { "spanty", "Span<T>" },
        { "span tea", "Span<T>" },
        { "list t", "List<T>" },
        { "list tea", "List<T>" },
        { "i enumerable t", "IEnumerable<T>" },
        { "i enumerable tea", "IEnumerable<T>" },
        { "quality compare tea", "IEqualityComparer<T>" },
        { "equality comparer t", "IEqualityComparer<T>" },
        { "equality comparer tea", "IEqualityComparer<T>" },
        { "i comparable t", "IComparable<T>" },
        { "i comparable tea", "IComparable<T>" },
        { "async local t", "AsyncLocal<T>" },
        { "async local tea", "AsyncLocal<T>" },
        { "dictionary t", "Dictionary<TKey, TValue>" },
        { "func t", "Func<T>" },
        { "action t", "Action<T>" },

        // Language names
        { "sea sharp", "C#" },
        { "sea shard", "C#" },
        { "c shard", "C#" },
        { "c-sharp", "C#" },
        { "see sharp", "C#" },
        { "f sharp", "F#" },
        { "f-sharp", "F#" },

        // Methods/Types
        { "thashcode", "GetHashCode" },
        { "gethashcode", "GetHashCode" },
        { "t hash code", "GetHashCode" },
        { "configure await", "ConfigureAwait" },
        { "configure a wait", "ConfigureAwait" },
        { "task when all", "Task.WhenAll" },
        { "task wait all", "Task.WaitAll" },
        { "gc collect", "GC.Collect" },
        { "gc select", "GC.Collect" },
        { "g c collect", "GC.Collect" },
        { "i disposable", "IDisposable" },
        { "eye disposable", "IDisposable" },
        { "i async disposable", "IAsyncDisposable" },

        // Common misheard words
        { "a weight", "await" },
        { "a wake", "await" },
        { "a wait", "await" },
        { "new soft", "Newtonsoft" },
        { "newton soft", "Newtonsoft" },
        { "jay son", "JSON" },
        { "jason", "JSON" },
        { "link", "LINQ" },
        { "ef core", "EF Core" },
        { "e f core", "EF Core" },
        { "entity framework", "Entity Framework" },
        { "asp net", "ASP.NET" },
        { "asp.net", "ASP.NET" },
        { "dot net", ".NET" },
        { "dotnet", ".NET" },

        // Async patterns
        { "async await", "async/await" },
        { "a sink a weight", "async/await" },
        { "value task", "ValueTask" },
        { "value task t", "ValueTask<T>" },

        // Memory/performance terms
        { "stack alec", "stackalloc" },
        { "stack alloc", "stackalloc" },
        { "memory t", "Memory<T>" },
        { "read only span", "ReadOnlySpan<T>" },
        { "read only memory", "ReadOnlyMemory<T>" },
    };

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
        int detectionIntervalMs = 2000,
        int minBufferLength = 50,
        int deduplicationWindowMs = 30000,
        bool enableTechnicalTermCorrection = true,
        bool enableNoiseFilter = true,
        int stabilityWindowMs = 800)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model;
        _confidenceThreshold = confidenceThreshold;
        _detectionIntervalMs = detectionIntervalMs;
        _minBufferLength = minBufferLength;
        _deduplicationWindowMs = deduplicationWindowMs;
        _enableTechnicalTermCorrection = enableTechnicalTermCorrection;
        _enableNoiseFilter = enableNoiseFilter;
        _stabilityWindowMs = stabilityWindowMs;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public string Name => $"LLM ({_model})";

    public void AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var processed = text;

        // Filter transcription noise (if enabled)
        if (_enableNoiseFilter)
        {
            processed = RemoveTranscriptionNoise(processed);
            if (string.IsNullOrWhiteSpace(processed))
                return;
        }

        // Correct technical terms that are commonly misheard (if enabled)
        if (_enableTechnicalTermCorrection)
        {
            processed = CorrectTechnicalTerms(processed);
        }

        _buffer.Append(' ');
        _buffer.Append(processed);
        _lastBufferChange = DateTime.UtcNow;

        // Phase 1: Quick check for potential question candidates
        if (HasPotentialQuestion(processed))
        {
            _hasPendingCandidate = true;
        }

        // Trim buffer if too long
        if (_buffer.Length > MaxBufferLength)
        {
            var excess = _buffer.Length - MaxBufferLength;
            _buffer.Remove(0, excess);
        }
    }

    /// <summary>
    /// Quick local check for potential question markers.
    /// This is Phase 1 of two-phase detection - just flags candidates, doesn't confirm.
    /// </summary>
    private static bool HasPotentialQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Check for question mark
        if (text.Contains('?'))
            return true;

        // Check for imperative patterns (case-insensitive)
        var lowerText = text.ToLowerInvariant();
        var imperativeStarts = new[]
        {
            "explain ", "describe ", "walk me through", "tell me about",
            "what is ", "what are ", "how do ", "how does ", "how would ",
            "why do ", "why does ", "when should ", "can you ",
            "could you ", "give me an example"
        };

        return imperativeStarts.Any(pattern => lowerText.Contains(pattern));
    }

    /// <summary>
    /// Signals that a speech pause was detected.
    /// This confirms any pending candidates for detection.
    /// </summary>
    public void SignalSpeechPause()
    {
        if (_hasPendingCandidate)
        {
            _confirmedByPause = true;
        }
    }

    /// <summary>
    /// Checks if the buffer looks complete - ends with sentence terminator
    /// and no trailing lowercase continuation that suggests more is coming.
    /// </summary>
    private bool BufferLooksComplete()
    {
        var text = _buffer.ToString().TrimEnd();
        if (string.IsNullOrEmpty(text))
            return false;

        // Must end with terminal punctuation
        var lastChar = text[^1];
        if (lastChar != '.' && lastChar != '?' && lastChar != '!')
            return false;

        // Find the last terminator position
        var lastTerminator = text.Length - 1;

        // Check if there's meaningful content before the terminator
        if (lastTerminator < 15)
            return false;

        // Look at what's after the last question mark specifically
        // (we care most about ? since that's our primary signal)
        var lastQuestion = text.LastIndexOf('?');
        if (lastQuestion >= 0 && lastQuestion < text.Length - 1)
        {
            var afterQuestion = text.Substring(lastQuestion + 1).Trim();
            if (!string.IsNullOrEmpty(afterQuestion))
            {
                // There's text after the last ?
                // If it starts lowercase, it's likely a continuation - NOT complete
                if (char.IsLower(afterQuestion[0]))
                    return false;

                // Check for common continuation words
                var firstWord = afterQuestion.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.ToLowerInvariant()?.TrimEnd('.', ',', '?', '!');

                var continuationWords = new[] { "and", "or", "in", "for", "to", "with", "used", "like", "such" };
                if (firstWord != null && continuationWords.Contains(firstWord))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Removes transcription noise like repeated words and filler words.
    /// </summary>
    internal static string RemoveTranscriptionNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Filler words to remove
        var fillerWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "um", "uh", "er", "ah", "hmm", "hm", "mm", "mhm", "erm"
        };

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return string.Empty;

        // Detect repeated word patterns (same word 3+ times in a row)
        var result = new List<string>();
        string? lastWord = null;
        int repeatCount = 0;

        foreach (var word in words)
        {
            var cleanWord = word.Trim('.', ',', '!', '?', ';', ':');

            // Skip filler words
            if (fillerWords.Contains(cleanWord))
                continue;

            // Track repetition
            if (string.Equals(cleanWord, lastWord, StringComparison.OrdinalIgnoreCase))
            {
                repeatCount++;
                // Allow up to 2 repetitions (for emphasis), skip 3+
                if (repeatCount >= 3)
                    continue;
            }
            else
            {
                lastWord = cleanWord;
                repeatCount = 1;
            }

            result.Add(word);
        }

        // If all words were filtered out, return empty
        if (result.Count == 0)
            return string.Empty;

        // Check if the result is mostly a single repeated word
        var uniqueWords = result.Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?'))
            .Where(w => w.Length > 1)
            .Distinct()
            .ToList();

        // If only 1 unique word and it's very short, likely noise
        if (uniqueWords.Count <= 1 && result.Count > 2)
        {
            var singleWord = uniqueWords.FirstOrDefault();
            if (singleWord != null && singleWord.Length <= 4)
                return string.Empty;
        }

        return string.Join(' ', result);
    }

    /// <summary>
    /// Corrects commonly misheard technical programming terms.
    /// </summary>
    internal static string CorrectTechnicalTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = text;

        // Apply corrections - longer patterns first to avoid partial matches
        var sortedCorrections = TechnicalTermCorrections
            .OrderByDescending(kvp => kvp.Key.Length)
            .ToList();

        foreach (var (pattern, replacement) in sortedCorrections)
        {
            // Case-insensitive replacement
            var index = result.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                result = result.Remove(index, pattern.Length).Insert(index, replacement);
                // Continue searching after the replacement
                index = result.IndexOf(pattern, index + replacement.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if the text contains at least one complete sentence that isn't likely to continue.
    /// Prevents detection on partial/fragmented transcriptions.
    /// </summary>
    internal static bool HasCompleteSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Find the last sentence terminator (. ? !)
        var lastPeriod = text.LastIndexOf('.');
        var lastQuestion = text.LastIndexOf('?');
        var lastExclamation = text.LastIndexOf('!');

        var lastTerminator = Math.Max(lastPeriod, Math.Max(lastQuestion, lastExclamation));

        // Must have at least one sentence with meaningful content (> 20 chars to terminator)
        if (lastTerminator <= 20)
            return false;

        return true;
    }

    public async Task<List<DetectedQuestion>> DetectQuestionsAsync(CancellationToken ct = default)
    {
        var results = new List<DetectedQuestion>();

        // Phase 2: Check if we should confirm pending candidates
        // Confirmation happens via:
        // (1) speech pause signal - immediate
        // (2) stability timeout - fallback
        // (3) buffer looks complete - immediate (ends with terminator, no trailing lowercase)
        var timeSinceLastChange = (DateTime.UtcNow - _lastBufferChange).TotalMilliseconds;
        var confirmedByStability = _hasPendingCandidate && timeSinceLastChange >= _stabilityWindowMs;
        var confirmedByComplete = _hasPendingCandidate && BufferLooksComplete();

        if (!_confirmedByPause && !confirmedByStability && !confirmedByComplete)
        {
            // Not confirmed yet - wait for pause, stability, or completion
            return results;
        }

        // Reset confirmation state
        _hasPendingCandidate = false;
        _confirmedByPause = false;

        // Rate limit detection calls
        var elapsed = DateTime.UtcNow - _lastDetection;
        if (elapsed.TotalMilliseconds < _detectionIntervalMs)
        {
            return results;
        }

        var text = _buffer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length < _minBufferLength)
        {
            return results;
        }

        // Check for sentence completeness before processing
        if (!HasCompleteSentence(text))
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
                max_completion_tokens = 1024
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

                        // Record detection time for time-based suppression
                        var fingerprint = GetSemanticFingerprint(normalizedText);
                        RecordDetectionTime(fingerprint);

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
        var fingerprint = GetSemanticFingerprint(normalizedText);

        // Check time-based suppression first
        if (IsRecentlyDetected(fingerprint))
            return true;

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

    /// <summary>
    /// Extracts a semantic fingerprint from a question by identifying key technical terms.
    /// </summary>
    internal static string GetSemanticFingerprint(string question)
    {
        var significant = GetSignificantWords(question);
        var sorted = significant.OrderBy(w => w, StringComparer.OrdinalIgnoreCase);
        return string.Join(" ", sorted);
    }

    /// <summary>
    /// Checks if a fingerprint was detected within the suppression window.
    /// Must be called within _detectedQuestionsLock.
    /// </summary>
    private bool IsRecentlyDetected(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return false;

        if (_detectionTimes.TryGetValue(fingerprint, out var lastTime))
        {
            if ((DateTime.UtcNow - lastTime).TotalMilliseconds < _deduplicationWindowMs)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Records the detection time for a fingerprint.
    /// Must be called within _detectedQuestionsLock.
    /// </summary>
    private void RecordDetectionTime(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return;

        _detectionTimes[fingerprint] = DateTime.UtcNow;

        // Clean up old entries (older than deduplication window)
        var cutoff = DateTime.UtcNow.AddMilliseconds(-_deduplicationWindowMs * 2);
        var keysToRemove = _detectionTimes
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _detectionTimes.Remove(key);
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

        // Use configurable threshold for similarity
        return jaccard >= JaccardThreshold;
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
        var bufferText = _buffer.ToString();
        var idx = bufferText.IndexOf(questionText, StringComparison.OrdinalIgnoreCase);

        if (idx >= 0)
        {
            // Remove the question text plus some context before it
            // This prevents the same lead-up context from triggering re-detection
            const int contextPadding = 50;
            var startIdx = Math.Max(0, idx - contextPadding);
            var endIdx = Math.Min(bufferText.Length, idx + questionText.Length);
            var removeLength = endIdx - startIdx;

            _buffer.Remove(startIdx, removeLength);
        }
        else
        {
            // If exact match not found, the LLM may have cleaned up the text
            // Try fuzzy matching using significant words
            var questionWords = GetSignificantWords(questionText.ToLowerInvariant());
            if (questionWords.Count >= 3)
            {
                // Find and remove any substring that contains most of these words
                var bufferLower = bufferText.ToLowerInvariant();
                foreach (var word in questionWords.Take(3))
                {
                    var wordIdx = bufferLower.IndexOf(word, StringComparison.Ordinal);
                    if (wordIdx >= 0)
                    {
                        // Found a key word - remove surrounding context
                        var startIdx = Math.Max(0, wordIdx - 30);
                        var removeLength = Math.Min(100, bufferText.Length - startIdx);
                        _buffer.Remove(startIdx, removeLength);
                        break;
                    }
                }
            }
        }
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
