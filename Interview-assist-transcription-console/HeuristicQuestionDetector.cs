using System.Text;
using InterviewAssist.TranscriptionConsole;

/// <summary>
/// Simple heuristic-based question detector using punctuation and common patterns.
/// Maintains a rolling buffer to catch questions split across transcription batches.
/// </summary>
public class HeuristicQuestionDetector : IQuestionDetector
{
    private readonly StringBuilder _buffer = new();
    private readonly List<string> _processedSentences = new();
    private const int MaxBufferLength = 2000;

    private static readonly string[] QuestionStarters =
    {
        "what", "how", "why", "when", "where", "who", "which",
        "can", "could", "would", "should", "do", "does", "did",
        "is", "are", "was", "were", "have", "has", "had",
        "will", "won't", "wouldn't", "couldn't", "shouldn't"
    };

    private static readonly string[] ImperativeStarters =
    {
        "tell me", "explain", "describe", "walk me through",
        "give me", "show me", "help me understand",
        "talk about", "share", "elaborate"
    };

    public string Name => "Heuristic";

    public void AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _buffer.Append(' ');
        _buffer.Append(text);

        // Trim buffer if too long (keep recent text)
        if (_buffer.Length > MaxBufferLength)
        {
            var excess = _buffer.Length - MaxBufferLength;
            _buffer.Remove(0, excess);
        }
    }

    public Task<List<DetectedQuestion>> DetectQuestionsAsync(CancellationToken ct = default)
    {
        var results = DetectQuestions();
        return Task.FromResult(results);
    }

    public List<DetectedQuestion> DetectQuestions()
    {
        var results = new List<DetectedQuestion>();
        var text = _buffer.ToString();

        // Split into sentences (by . ? !)
        var sentences = SplitIntoSentences(text);

        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Skip if already processed
            if (_processedSentences.Contains(trimmed))
                continue;

            var detection = AnalyzeSentence(trimmed);
            if (detection != null)
            {
                results.Add(detection);
                _processedSentences.Add(trimmed);

                // Remove only the detected sentence from buffer, preserving surrounding context
                var idx = _buffer.ToString().IndexOf(trimmed, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    _buffer.Remove(idx, trimmed.Length);
                }
            }
        }

        // Keep processed list small
        while (_processedSentences.Count > 20)
            _processedSentences.RemoveAt(0);

        return results;
    }

    private static DetectedQuestion? AnalyzeSentence(string sentence)
    {
        var lower = sentence.ToLowerInvariant();
        var endsWithQuestion = sentence.TrimEnd().EndsWith('?');

        // Direct question with ?
        if (endsWithQuestion)
        {
            var type = DetermineQuestionType(lower);
            return new DetectedQuestion(sentence, type, 0.9);
        }

        // Check for imperative patterns (no ? needed)
        foreach (var starter in ImperativeStarters)
        {
            if (lower.StartsWith(starter))
            {
                return new DetectedQuestion(sentence, "Imperative", 0.85);
            }
        }

        // Check for question words without ? (common in speech)
        if (StartsWithQuestionWord(lower) && sentence.Length > 20)
        {
            // Likely a question even without ?
            var type = DetermineQuestionType(lower);
            return new DetectedQuestion(sentence, type + "?", 0.7);
        }

        return null;
    }

    private static bool StartsWithQuestionWord(string lower)
    {
        foreach (var starter in QuestionStarters)
        {
            if (lower.StartsWith(starter + " ") || lower.StartsWith(starter + ","))
                return true;
        }
        return false;
    }

    private static string DetermineQuestionType(string lower)
    {
        if (lower.StartsWith("can you elaborate") || lower.StartsWith("what do you mean"))
            return "Clarification";
        if (lower.StartsWith("and ") || lower.StartsWith("but ") || lower.StartsWith("also "))
            return "Follow-up";
        if (lower.StartsWith("how") || lower.StartsWith("what") || lower.StartsWith("why"))
            return "Question";
        return "Question";
    }

    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var current = new StringBuilder();

        foreach (var c in text)
        {
            current.Append(c);
            if (c == '?' || c == '.' || c == '!')
            {
                var sentence = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(sentence))
                    sentences.Add(sentence);
                current.Clear();
            }
        }

        // Don't include incomplete sentence (no terminator yet)
        return sentences;
    }
}
