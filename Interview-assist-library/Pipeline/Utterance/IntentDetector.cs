using System.Text.RegularExpressions;

namespace InterviewAssist.Library.Pipeline.Utterance;

/// <summary>
/// Detects intent from utterance text using heuristic rules.
/// Two-stage: candidate detection (reversible) and final detection (commit).
/// </summary>
public sealed partial class IntentDetector : IIntentDetector
{
    // Imperative patterns (highest priority)
    [GeneratedRegex(@"^(stop|cancel|nevermind|never\s*mind|quit|exit|enough|that's\s*enough)", RegexOptions.IgnoreCase)]
    private static partial Regex StopPattern();

    [GeneratedRegex(@"^(repeat|say\s+(that|it)\s+again|what\s+did\s+you\s+say)", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatStartPattern();

    [GeneratedRegex(@"(repeat|say)\s+(the\s+)?(last|previous)", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatLastPattern();

    [GeneratedRegex(@"repeat\s+(number\s*|#)?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatNumberPattern();

    [GeneratedRegex(@"^(continue|go\s+on|next|proceed|keep\s+going|carry\s+on)", RegexOptions.IgnoreCase)]
    private static partial Regex ContinuePattern();

    [GeneratedRegex(@"(start\s+over|from\s+the\s+(beginning|start)|reset|begin\s+again)", RegexOptions.IgnoreCase)]
    private static partial Regex StartOverPattern();

    [GeneratedRegex(@"(generate|give\s+me|create|make|produce)\s+.*?(questions?|queries)", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratePattern();

    [GeneratedRegex(@"(\d+)\s*(questions?|queries)", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionCountPattern();

    // Polite imperative prefix
    [GeneratedRegex(@"^(please\s+|can\s+you\s+|could\s+you\s+|would\s+you\s+)", RegexOptions.IgnoreCase)]
    private static partial Regex PolitePrefix();

    // Question patterns
    [GeneratedRegex(@"^(what|why|how|when|where|who|which|whose)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WhWordPattern();

    [GeneratedRegex(@"^(is|are|was|were|do|does|did|can|could|would|should|have|has|will|shall|may|might)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AuxVerbPattern();

    [GeneratedRegex(@"\?$")]
    private static partial Regex QuestionMarkPattern();

    [GeneratedRegex(@"(do\s+you\s+know|can\s+you\s+tell\s+me|what's|what\s+is)", RegexOptions.IgnoreCase)]
    private static partial Regex KnowPattern();

    // Question subtype patterns
    [GeneratedRegex(@"(what\s+is|what\s+are|what\s+does|define|meaning\s+of|definition)", RegexOptions.IgnoreCase)]
    private static partial Regex DefinitionPattern();

    [GeneratedRegex(@"(how\s+do\s+I|how\s+can\s+I|how\s+to|how\s+would|steps\s+to)", RegexOptions.IgnoreCase)]
    private static partial Regex HowToPattern();

    [GeneratedRegex(@"(difference\s+between|compare|vs\.?|versus|compared\s+to)", RegexOptions.IgnoreCase)]
    private static partial Regex ComparePattern();

    [GeneratedRegex(@"(why\s+isn't|why\s+doesn't|why\s+won't|not\s+working|error|issue|problem|fail)", RegexOptions.IgnoreCase)]
    private static partial Regex TroubleshootPattern();

    // Topic extraction
    [GeneratedRegex(@"(?:what\s+is\s+(?:a|an|the)?\s*|define\s+|explain\s+)(.+?)(?:\?|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TopicExtractPattern();

    [GeneratedRegex(@"about\s+(.+?)(?:\?|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AboutTopicPattern();

    public DetectedIntent? DetectCandidate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Candidate detection uses same logic but may return null for low confidence
        var intent = DetectIntentCore(text, isCandidate: true);

        // Filter out low-confidence candidates
        if (intent != null && intent.Confidence < 0.3)
            return null;

        return intent;
    }

    public DetectedIntent DetectFinal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DetectedIntent
            {
                Type = IntentType.Other,
                Confidence = 0.0,
                SourceText = text ?? ""
            };
        }

        return DetectIntentCore(text, isCandidate: false) ?? new DetectedIntent
        {
            Type = IntentType.Statement,
            Confidence = 0.5,
            SourceText = text
        };
    }

    private DetectedIntent? DetectIntentCore(string text, bool isCandidate)
    {
        var normalizedText = NormalizeText(text);

        // 1. Check for imperatives first (highest priority)
        var imperative = DetectImperative(normalizedText, text);
        if (imperative != null)
            return imperative;

        // 2. Check for questions
        var question = DetectQuestion(normalizedText, text);
        if (question != null)
            return question;

        // 3. Default to statement (only for final, not candidate)
        if (!isCandidate)
        {
            return new DetectedIntent
            {
                Type = IntentType.Statement,
                Confidence = 0.4,
                SourceText = text
            };
        }

        return null;
    }

    private DetectedIntent? DetectImperative(string normalized, string original)
    {
        // Strip polite prefix for matching
        var textToMatch = normalized;
        var hadPolitePrefix = false;
        var politeMatch = PolitePrefix().Match(normalized);
        if (politeMatch.Success)
        {
            textToMatch = normalized[politeMatch.Length..].TrimStart();
            hadPolitePrefix = true;
        }

        // Stop/Cancel
        if (StopPattern().IsMatch(textToMatch))
        {
            return new DetectedIntent
            {
                Type = IntentType.Imperative,
                Subtype = IntentSubtype.Stop,
                Confidence = 0.95,
                SourceText = original
            };
        }

        // Repeat with number
        var repeatNumMatch = RepeatNumberPattern().Match(normalized);
        if (repeatNumMatch.Success)
        {
            var num = int.TryParse(repeatNumMatch.Groups[2].Value, out var n) ? n : (int?)null;
            return new DetectedIntent
            {
                Type = IntentType.Imperative,
                Subtype = IntentSubtype.Repeat,
                Confidence = 0.9,
                SourceText = original,
                Slots = new IntentSlots
                {
                    Reference = $"number {num}",
                    Count = num
                }
            };
        }

        // Repeat last/previous
        if (RepeatLastPattern().IsMatch(normalized))
        {
            return new DetectedIntent
            {
                Type = IntentType.Imperative,
                Subtype = IntentSubtype.Repeat,
                Confidence = 0.9,
                SourceText = original,
                Slots = new IntentSlots { Reference = "last" }
            };
        }

        // Repeat (general)
        if (RepeatStartPattern().IsMatch(textToMatch))
        {
            return new DetectedIntent
            {
                Type = IntentType.Imperative,
                Subtype = IntentSubtype.Repeat,
                Confidence = hadPolitePrefix ? 0.85 : 0.8,
                SourceText = original
            };
        }

        // Continue
        if (ContinuePattern().IsMatch(textToMatch))
        {
            return new DetectedIntent
            {
                Type = IntentType.Imperative,
                Subtype = IntentSubtype.Continue,
                Confidence = 0.85,
                SourceText = original
            };
        }

        // Start over
        if (StartOverPattern().IsMatch(normalized))
        {
            return new DetectedIntent
            {
                Type = IntentType.Imperative,
                Subtype = IntentSubtype.StartOver,
                Confidence = 0.9,
                SourceText = original
            };
        }

        // Generate questions
        if (GeneratePattern().IsMatch(normalized))
        {
            var countMatch = QuestionCountPattern().Match(normalized);
            var count = countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out var c) ? c : (int?)null;

            var topicMatch = AboutTopicPattern().Match(normalized);
            var topic = topicMatch.Success ? topicMatch.Groups[1].Value.Trim() : null;

            return new DetectedIntent
            {
                Type = IntentType.Imperative,
                Subtype = IntentSubtype.Generate,
                Confidence = 0.85,
                SourceText = original,
                Slots = new IntentSlots
                {
                    Count = count,
                    Topic = topic
                }
            };
        }

        return null;
    }

    private DetectedIntent? DetectQuestion(string normalized, string original)
    {
        var confidence = 0.0;
        IntentSubtype? subtype = null;

        // Question mark is strong signal
        if (QuestionMarkPattern().IsMatch(normalized))
        {
            confidence += 0.5;
        }

        // WH-word at start
        if (WhWordPattern().IsMatch(normalized))
        {
            confidence += 0.4;
        }

        // Auxiliary verb at start
        if (AuxVerbPattern().IsMatch(normalized))
        {
            confidence += 0.3;
        }

        // "Do you know" pattern
        if (KnowPattern().IsMatch(normalized))
        {
            confidence += 0.3;
        }

        // Compare request (often phrased as imperative but is really a question)
        if (ComparePattern().IsMatch(normalized))
        {
            confidence += 0.5;
        }

        // Error/problem statements are implicit help requests
        if (TroubleshootPattern().IsMatch(normalized))
        {
            confidence += 0.4;
        }

        // Not enough signals for a question
        if (confidence < 0.4)
            return null;

        // Determine subtype - check more specific patterns first
        // Note: HowTo must come before Troubleshoot because "How to fix this error" is HowTo, not Troubleshoot
        if (ComparePattern().IsMatch(normalized))
        {
            subtype = IntentSubtype.Compare;
        }
        else if (HowToPattern().IsMatch(normalized))
        {
            subtype = IntentSubtype.HowTo;
        }
        else if (TroubleshootPattern().IsMatch(normalized))
        {
            subtype = IntentSubtype.Troubleshoot;
        }
        else if (DefinitionPattern().IsMatch(normalized))
        {
            subtype = IntentSubtype.Definition;
        }

        // Extract topic
        string? topic = null;
        var topicMatch = TopicExtractPattern().Match(normalized);
        if (topicMatch.Success)
        {
            topic = topicMatch.Groups[1].Value.Trim().TrimEnd('?');
        }
        else
        {
            var aboutMatch = AboutTopicPattern().Match(normalized);
            if (aboutMatch.Success)
            {
                topic = aboutMatch.Groups[1].Value.Trim().TrimEnd('?');
            }
        }

        return new DetectedIntent
        {
            Type = IntentType.Question,
            Subtype = subtype,
            Confidence = Math.Min(confidence, 1.0),
            SourceText = original,
            Slots = new IntentSlots { Topic = topic }
        };
    }

    private static string NormalizeText(string text)
    {
        return text.Trim().ToLowerInvariant();
    }
}
