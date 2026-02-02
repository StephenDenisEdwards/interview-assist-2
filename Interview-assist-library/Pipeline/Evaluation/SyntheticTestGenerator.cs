using System.Text.Json;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Generates synthetic test variations from seed questions.
/// </summary>
public sealed class SyntheticTestGenerator
{
    private static readonly string[] IndirectPrefixes =
    {
        "I was wondering ",
        "I'm curious about ",
        "Could you tell me ",
        "Do you know ",
        "Can you explain ",
        "Would you mind explaining ",
        "I'd like to know "
    };

    private static readonly string[] EmbeddedPrefixes =
    {
        "So basically, ",
        "The thing is, ",
        "I mean, ",
        "Well, ",
        "You see, "
    };

    private static readonly string[] SoftenerPrefixes =
    {
        "Just curious, ",
        "Quick question: ",
        "By the way, ",
        "One more thing - "
    };

    /// <summary>
    /// Generate variations of a base question.
    /// </summary>
    public IEnumerable<GeneratedTestCase> GenerateVariations(string baseQuestion, string? subtype = null)
    {
        var cleanQuestion = baseQuestion.Trim();
        var lowerQuestion = LowerFirst(cleanQuestion);

        // Original form
        yield return new GeneratedTestCase(
            Text: cleanQuestion,
            Type: "Question",
            Subtype: subtype,
            Variation: "Direct",
            BaseQuestion: baseQuestion);

        // Without question mark
        if (cleanQuestion.EndsWith('?'))
        {
            yield return new GeneratedTestCase(
                Text: cleanQuestion.TrimEnd('?'),
                Type: "Question",
                Subtype: subtype,
                Variation: "NoQuestionMark",
                BaseQuestion: baseQuestion);
        }

        // Indirect forms
        foreach (var prefix in IndirectPrefixes.Take(3))
        {
            yield return new GeneratedTestCase(
                Text: $"{prefix}{lowerQuestion}",
                Type: "Question",
                Subtype: subtype,
                Variation: "Indirect",
                BaseQuestion: baseQuestion);
        }

        // Embedded forms
        foreach (var prefix in EmbeddedPrefixes.Take(2))
        {
            yield return new GeneratedTestCase(
                Text: $"{prefix}{lowerQuestion}",
                Type: "Question",
                Subtype: subtype,
                Variation: "Embedded",
                BaseQuestion: baseQuestion);
        }

        // Softener forms
        foreach (var prefix in SoftenerPrefixes.Take(2))
        {
            yield return new GeneratedTestCase(
                Text: $"{prefix}{lowerQuestion}",
                Type: "Question",
                Subtype: subtype,
                Variation: "Softened",
                BaseQuestion: baseQuestion);
        }

        // Convert to imperative if it starts with common patterns
        var imperative = ConvertToImperative(cleanQuestion);
        if (imperative != null)
        {
            yield return new GeneratedTestCase(
                Text: imperative,
                Type: "Question",
                Subtype: subtype,
                Variation: "Imperative",
                BaseQuestion: baseQuestion);
        }
    }

    /// <summary>
    /// Generate negative test cases (non-questions) from a question.
    /// </summary>
    public IEnumerable<GeneratedTestCase> GenerateNegatives(string baseQuestion)
    {
        var cleanQuestion = baseQuestion.Trim().TrimEnd('?');

        // Fragment (incomplete)
        var words = cleanQuestion.Split(' ');
        if (words.Length > 3)
        {
            yield return new GeneratedTestCase(
                Text: string.Join(' ', words.Take(3)),
                Type: "Statement",
                Subtype: null,
                Variation: "Fragment",
                BaseQuestion: baseQuestion);
        }

        // Turn into statement
        var statement = ConvertToStatement(cleanQuestion);
        if (statement != null)
        {
            yield return new GeneratedTestCase(
                Text: statement,
                Type: "Statement",
                Subtype: null,
                Variation: "Statement",
                BaseQuestion: baseQuestion);
        }

        // Meta-reference
        yield return new GeneratedTestCase(
            Text: $"That's a good question about {ExtractTopic(cleanQuestion)}.",
            Type: "Statement",
            Subtype: null,
            Variation: "MetaReference",
            BaseQuestion: baseQuestion);
    }

    /// <summary>
    /// Load seed questions from a file and generate a complete test dataset.
    /// </summary>
    public async Task<IReadOnlyList<GeneratedTestCase>> GenerateFromSeedFileAsync(
        string seedFilePath,
        bool includeNegatives = true,
        CancellationToken ct = default)
    {
        var results = new List<GeneratedTestCase>();

        await foreach (var line in File.ReadLinesAsync(seedFilePath, ct))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//"))
                continue;

            var question = line.Trim();
            results.AddRange(GenerateVariations(question));

            if (includeNegatives)
            {
                results.AddRange(GenerateNegatives(question));
            }
        }

        return results;
    }

    /// <summary>
    /// Save generated test cases to a JSONL file.
    /// </summary>
    public static async Task SaveToFileAsync(
        string filePath,
        IEnumerable<GeneratedTestCase> testCases,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        await using var writer = new StreamWriter(filePath);
        foreach (var testCase in testCases)
        {
            var item = new DatasetItem
            {
                Text = testCase.Text,
                Type = testCase.Type,
                Subtype = testCase.Subtype,
                Notes = $"Generated: {testCase.Variation} from '{testCase.BaseQuestion[..Math.Min(30, testCase.BaseQuestion.Length)]}...'"
            };

            var json = JsonSerializer.Serialize(item, options);
            await writer.WriteLineAsync(json);
        }
    }

    private static string LowerFirst(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Handle question words that should stay capitalized
        var questionWords = new[] { "What", "How", "Why", "When", "Where", "Who", "Which" };
        if (questionWords.Any(w => text.StartsWith(w, StringComparison.OrdinalIgnoreCase)))
        {
            return text[..1].ToLower() + text[1..];
        }

        return text;
    }

    private static string? ConvertToImperative(string question)
    {
        // "What is X?" -> "Explain X"
        if (question.StartsWith("What is ", StringComparison.OrdinalIgnoreCase))
        {
            return "Explain " + question[8..].TrimEnd('?') + ".";
        }

        // "How do you X?" -> "Show me how to X"
        if (question.StartsWith("How do you ", StringComparison.OrdinalIgnoreCase))
        {
            return "Show me how to " + question[11..].TrimEnd('?') + ".";
        }

        // "Can you explain X?" -> "Explain X"
        if (question.StartsWith("Can you explain ", StringComparison.OrdinalIgnoreCase))
        {
            return "Explain " + question[16..].TrimEnd('?') + ".";
        }

        return null;
    }

    private static string? ConvertToStatement(string question)
    {
        // "What is X?" -> "X is interesting"
        if (question.StartsWith("What is ", StringComparison.OrdinalIgnoreCase))
        {
            var topic = question[8..].TrimEnd('?');
            return $"I think {topic} is interesting.";
        }

        // "How do you X?" -> "I X by..."
        if (question.StartsWith("How do you ", StringComparison.OrdinalIgnoreCase))
        {
            var action = question[11..].TrimEnd('?');
            return $"I usually {action} by following best practices.";
        }

        return null;
    }

    private static string ExtractTopic(string question)
    {
        // Simple extraction of likely topic
        var words = question.Split(' ');
        if (words.Length > 3)
        {
            return string.Join(' ', words.Skip(2).Take(3));
        }
        return "that topic";
    }
}

/// <summary>
/// A generated test case with metadata.
/// </summary>
public sealed record GeneratedTestCase(
    string Text,
    string Type,
    string? Subtype,
    string Variation,
    string BaseQuestion);
