using System.Text.RegularExpressions;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Analyzes false positive patterns to identify systematic detection errors.
/// </summary>
public sealed class ErrorAnalyzer
{
    private static readonly ErrorPattern[] KnownPatterns =
    {
        new("Trailing intonation",
            @"^[^?]*\b(so|and|but|like|well|right|okay)\s*$",
            "Statements ending with filler words that sound like trailing questions"),

        new("I think statements",
            @"^I\s+(think|believe|feel|guess|suppose|assume)\b",
            "Opinion statements starting with 'I think...'"),

        new("Relative clauses",
            @"^(which|that|who|where|when)\s+",
            "Fragments starting with relative pronouns"),

        new("Filler phrases",
            @"\b(you know|I mean|like|basically|actually|honestly)\b.*$",
            "Utterances dominated by filler phrases"),

        new("Incomplete fragments",
            @"^[A-Z]?[a-z]+(\s+[a-z]+){0,3}$",
            "Very short fragments (1-4 words) without punctuation"),

        new("Lists and enumerations",
            @"(first|second|third|next|then|also|another)\b.*$",
            "List items or enumerations"),

        new("Indirect speech",
            @"\b(he|she|they)\s+(said|asked|wondered|mentioned)\b",
            "Reported speech about someone else's question"),

        new("Exclamations",
            @"^(wow|oh|ah|whoa|man|dude|nice|cool|great)\b",
            "Exclamatory expressions"),

        new("Self-corrections",
            @"\b(I mean|sorry|wait|no|actually)\b.{0,20}$",
            "Self-corrections or restarts"),

        new("Conditional statements",
            @"^if\s+.+,?\s*(then\s+)?[^?]*$",
            "If-then statements without question marks"),

        new("Technical jargon fragments",
            @"^[A-Z][a-zA-Z]+\s+(class|method|function|interface|type)\b",
            "Technical term fragments"),

        new("Code references",
            @"\b(dot|equals|null|void|return|public|private)\b",
            "Code-like terminology"),
    };

    /// <summary>
    /// Analyze false positives to identify common error patterns.
    /// </summary>
    public ErrorAnalysisResult Analyze(IEnumerable<DetectedQuestionInfo> falsePositives)
    {
        var fpList = falsePositives.ToList();
        var patternMatches = new Dictionary<string, List<string>>();
        var unclassified = new List<string>();

        foreach (var fp in fpList)
        {
            var text = fp.Text.Trim();
            var matched = false;

            foreach (var pattern in KnownPatterns)
            {
                if (Regex.IsMatch(text, pattern.Regex, RegexOptions.IgnoreCase))
                {
                    if (!patternMatches.ContainsKey(pattern.Name))
                        patternMatches[pattern.Name] = new List<string>();

                    patternMatches[pattern.Name].Add(text);
                    matched = true;
                    break; // Only count in first matching pattern
                }
            }

            if (!matched)
            {
                unclassified.Add(text);
            }
        }

        var patternCounts = patternMatches
            .Select(kvp => new ErrorPatternCount(
                Pattern: KnownPatterns.First(p => p.Name == kvp.Key),
                Count: kvp.Value.Count,
                Examples: kvp.Value.Take(3).ToList()))
            .OrderByDescending(p => p.Count)
            .ToList();

        return new ErrorAnalysisResult(
            TotalFalsePositives: fpList.Count,
            PatternCounts: patternCounts,
            Unclassified: unclassified,
            UnclassifiedCount: unclassified.Count);
    }

    /// <summary>
    /// Analyze by confidence level to see where errors concentrate.
    /// </summary>
    public IReadOnlyList<ConfidenceBucket> AnalyzeByConfidence(
        IEnumerable<DetectedQuestionInfo> falsePositives,
        IEnumerable<DetectedQuestionInfo> truePositives,
        double bucketSize = 0.1)
    {
        var fpList = falsePositives.ToList();
        var tpList = truePositives.ToList();
        var buckets = new List<ConfidenceBucket>();

        for (var threshold = 0.0; threshold < 1.0; threshold += bucketSize)
        {
            var lower = threshold;
            var upper = threshold + bucketSize;

            var fpInBucket = fpList.Count(fp => fp.Confidence >= lower && fp.Confidence < upper);
            var tpInBucket = tpList.Count(tp => tp.Confidence >= lower && tp.Confidence < upper);

            buckets.Add(new ConfidenceBucket(
                LowerBound: lower,
                UpperBound: upper,
                FalsePositives: fpInBucket,
                TruePositives: tpInBucket,
                Total: fpInBucket + tpInBucket,
                Precision: tpInBucket + fpInBucket > 0
                    ? (double)tpInBucket / (tpInBucket + fpInBucket)
                    : 0.0));
        }

        return buckets;
    }

    /// <summary>
    /// Analyze false negatives (missed questions) for patterns.
    /// </summary>
    public MissedQuestionAnalysis AnalyzeMissed(IEnumerable<ExtractedQuestion> missed)
    {
        var missedList = missed.ToList();
        var bySubtype = missedList
            .GroupBy(m => m.Subtype ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        var shortQuestions = missedList.Where(m => m.Text.Length < 30).ToList();
        var longQuestions = missedList.Where(m => m.Text.Length >= 100).ToList();
        var noQuestionMark = missedList.Where(m => !m.Text.Contains('?')).ToList();
        var embedded = missedList.Where(m =>
            m.Text.Contains(" if ") ||
            m.Text.Contains(" whether ") ||
            m.Text.StartsWith("Tell me", StringComparison.OrdinalIgnoreCase) ||
            m.Text.StartsWith("Can you", StringComparison.OrdinalIgnoreCase)).ToList();

        return new MissedQuestionAnalysis(
            Total: missedList.Count,
            BySubtype: bySubtype,
            ShortQuestions: shortQuestions.Select(q => q.Text).ToList(),
            LongQuestions: longQuestions.Select(q => q.Text).ToList(),
            NoQuestionMark: noQuestionMark.Select(q => q.Text).ToList(),
            EmbeddedQuestions: embedded.Select(q => q.Text).ToList());
    }
}

/// <summary>
/// A known error pattern with regex and description.
/// </summary>
public sealed record ErrorPattern(
    string Name,
    string Regex,
    string Description);

/// <summary>
/// Count of false positives matching a pattern.
/// </summary>
public sealed record ErrorPatternCount(
    ErrorPattern Pattern,
    int Count,
    IReadOnlyList<string> Examples);

/// <summary>
/// Result of error pattern analysis.
/// </summary>
public sealed record ErrorAnalysisResult(
    int TotalFalsePositives,
    IReadOnlyList<ErrorPatternCount> PatternCounts,
    IReadOnlyList<string> Unclassified,
    int UnclassifiedCount);

/// <summary>
/// Analysis of errors by confidence bucket.
/// </summary>
public sealed record ConfidenceBucket(
    double LowerBound,
    double UpperBound,
    int FalsePositives,
    int TruePositives,
    int Total,
    double Precision);

/// <summary>
/// Analysis of missed questions (false negatives).
/// </summary>
public sealed record MissedQuestionAnalysis(
    int Total,
    IReadOnlyDictionary<string, int> BySubtype,
    IReadOnlyList<string> ShortQuestions,
    IReadOnlyList<string> LongQuestions,
    IReadOnlyList<string> NoQuestionMark,
    IReadOnlyList<string> EmbeddedQuestions);
