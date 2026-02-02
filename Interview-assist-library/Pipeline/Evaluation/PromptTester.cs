using InterviewAssist.Library.Pipeline.Recording;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Tests different LLM prompts for ground truth extraction and compares results.
/// </summary>
public sealed class PromptTester
{
    private readonly string _apiKey;
    private readonly double _matchThreshold;

    public PromptTester(string apiKey, double matchThreshold = 0.7)
    {
        _apiKey = apiKey;
        _matchThreshold = matchThreshold;
    }

    /// <summary>
    /// Test multiple prompt variants against the same transcript.
    /// </summary>
    public async Task<PromptTestResult> TestPromptsAsync(
        string transcript,
        IReadOnlyList<RecordedIntentEvent> detectedQuestions,
        IEnumerable<PromptVariant> variants,
        CancellationToken ct = default)
    {
        var results = new List<PromptVariantResult>();
        var evaluator = new DetectionEvaluator(_matchThreshold);

        foreach (var variant in variants)
        {
            ct.ThrowIfCancellationRequested();

            Console.WriteLine($"Testing prompt variant: {variant.Name}...");

            using var extractor = CreateExtractorWithPrompt(variant);
            var groundTruth = await extractor.ExtractQuestionsAsync(transcript, ct);

            var evalResult = evaluator.Evaluate(groundTruth, detectedQuestions);

            results.Add(new PromptVariantResult(
                VariantName: variant.Name,
                PromptDescription: variant.Description,
                GroundTruthCount: groundTruth.Count,
                EvaluationResult: evalResult));
        }

        // Find best variant for each metric
        var bestF1 = results.OrderByDescending(r => r.EvaluationResult.F1Score).First();
        var bestPrecision = results.OrderByDescending(r => r.EvaluationResult.Precision).First();
        var bestRecall = results.OrderByDescending(r => r.EvaluationResult.Recall).First();

        return new PromptTestResult(
            VariantResults: results,
            BestForF1: bestF1.VariantName,
            BestForPrecision: bestPrecision.VariantName,
            BestForRecall: bestRecall.VariantName,
            DetectedCount: detectedQuestions.Count);
    }

    /// <summary>
    /// Get default prompt variants for testing.
    /// </summary>
    public static IReadOnlyList<PromptVariant> GetDefaultVariants()
    {
        return new List<PromptVariant>
        {
            new PromptVariant(
                Name: "Default",
                Description: "Standard extraction prompt",
                SystemPrompt: null), // Use default

            new PromptVariant(
                Name: "Strict",
                Description: "Only clear, direct questions",
                SystemPrompt: """
                    You are a strict question extraction system. Extract ONLY clear, direct questions.

                    Include:
                    - Direct questions ending with question marks
                    - Clear "what/how/why/when/where/who" questions

                    DO NOT include:
                    - Indirect questions ("I wonder if...")
                    - Rhetorical questions
                    - Incomplete fragments
                    - Statements with questioning intonation

                    Be conservative - when in doubt, don't include it.

                    Respond with JSON: {"questions": [{"text": "...", "subtype": "...", "confidence": 0.9, "position": 0}]}
                    """),

            new PromptVariant(
                Name: "Inclusive",
                Description: "All forms of questions and requests",
                SystemPrompt: """
                    You are a comprehensive question extraction system. Extract ALL forms of questions and information requests.

                    Include:
                    - Direct questions with question marks
                    - Indirect questions ("I wonder if...", "I'm curious about...")
                    - Imperative requests for information ("Tell me about...", "Explain...")
                    - Embedded questions ("The question is whether...")
                    - Rhetorical questions (mark as rhetorical)
                    - Questions without question marks (common in speech)

                    Be inclusive - include anything that seeks information.

                    Respond with JSON: {"questions": [{"text": "...", "subtype": "...", "confidence": 0.9, "position": 0}]}
                    """),

            new PromptVariant(
                Name: "Technical",
                Description: "Focus on technical interview questions",
                SystemPrompt: """
                    You are extracting questions from a technical interview transcript.

                    Focus on:
                    - Technical definition questions ("What is X?")
                    - How-to questions about implementation
                    - Architecture and design questions
                    - Troubleshooting and debugging questions
                    - Experience and background questions

                    Ignore small talk and meta-conversation.

                    For each question, classify the subtype:
                    - Definition: "What is X?"
                    - HowTo: "How do you..."
                    - Compare: "What's the difference between..."
                    - Troubleshoot: "Why is X not working?"
                    - Experience: "Have you worked with..."

                    Respond with JSON: {"questions": [{"text": "...", "subtype": "...", "confidence": 0.9, "position": 0}]}
                    """)
        };
    }

    private GroundTruthExtractor CreateExtractorWithPrompt(PromptVariant variant)
    {
        // For now, we use the default extractor
        // A future enhancement could modify GroundTruthExtractor to accept custom prompts
        return new GroundTruthExtractor(_apiKey, "gpt-4o");
    }
}

/// <summary>
/// A prompt variant to test.
/// </summary>
public sealed record PromptVariant(
    string Name,
    string Description,
    string? SystemPrompt);

/// <summary>
/// Result for a single prompt variant.
/// </summary>
public sealed record PromptVariantResult(
    string VariantName,
    string PromptDescription,
    int GroundTruthCount,
    EvaluationResult EvaluationResult);

/// <summary>
/// Complete prompt test results.
/// </summary>
public sealed record PromptTestResult(
    IReadOnlyList<PromptVariantResult> VariantResults,
    string BestForF1,
    string BestForPrecision,
    string BestForRecall,
    int DetectedCount);
