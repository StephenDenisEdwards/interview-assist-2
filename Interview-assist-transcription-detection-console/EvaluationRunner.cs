using System.Text.Json;
using InterviewAssist.Library.Pipeline.Detection;
using InterviewAssist.Library.Pipeline.Evaluation;
using InterviewAssist.Library.Pipeline.Recording;

namespace InterviewAssist.TranscriptionDetectionConsole;

/// <summary>
/// Runs evaluation of question detection against LLM-extracted ground truth.
/// </summary>
public sealed class EvaluationRunner
{
    private readonly EvaluationOptions _options;
    private readonly ErrorAnalyzer _errorAnalyzer = new();
    private readonly SubtypeEvaluator _subtypeEvaluator = new();

    public EvaluationRunner(EvaluationOptions options)
    {
        _options = options;
    }

    public async Task<int> RunAsync(string sessionFile, string? outputFile, CancellationToken ct = default)
    {
        Console.WriteLine("=== Question Detection Evaluation ===");
        Console.WriteLine($"Session: {Path.GetFileName(sessionFile)}");
        Console.WriteLine();

        // Load session events
        Console.WriteLine("Loading session...");
        var events = await LoadEventsAsync(sessionFile, ct);
        if (events.Count == 0)
        {
            Console.WriteLine("Error: No events found in session file.");
            return 1;
        }

        // Extract session metadata
        var metadata = events.OfType<RecordedSessionMetadata>().FirstOrDefault();
        if (metadata != null)
        {
            Console.WriteLine($"  Recorded: {metadata.RecordedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"  Model: {metadata.Config.DeepgramModel}");
            Console.WriteLine($"  Detection mode: {metadata.Config.IntentDetectionMode}");
        }
        Console.WriteLine();

        // Extract transcript
        Console.WriteLine("Extracting transcript...");
        var transcript = TranscriptExtractor.ExtractFullTranscript(events);
        var segments = TranscriptExtractor.ExtractSegments(events);
        Console.WriteLine($"  Characters: {transcript.Length:N0}");
        Console.WriteLine($"  Utterances: {segments.Count}");
        Console.WriteLine();

        // Extract detected questions
        Console.WriteLine("Extracting detected questions...");
        var allDetected = TranscriptExtractor.ExtractDetectedQuestions(events, candidatesOnly: false);
        var deduplicated = TranscriptExtractor.DeduplicateQuestions(allDetected, _options.DeduplicationThreshold);
        Console.WriteLine($"  Total detection events: {allDetected.Count}");
        Console.WriteLine($"  Unique questions: {deduplicated.Count}");
        Console.WriteLine();

        // Extract ground truth using LLM
        Console.WriteLine($"Extracting ground truth using {_options.Model}...");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            Console.WriteLine("Error: OpenAI API key required for ground truth extraction.");
            Console.WriteLine("Set OPENAI_API_KEY environment variable or add Evaluation:ApiKey to appsettings.json.");
            return 1;
        }

        using var extractor = new GroundTruthExtractor(_options.ApiKey, _options.Model);
        var groundTruthResult = await extractor.ExtractQuestionsWithRawAsync(transcript, ct);
        var groundTruth = groundTruthResult.Questions;
        Console.WriteLine($"  Questions found: {groundTruth.Count}");
        Console.WriteLine();

        // Evaluate
        Console.WriteLine("Evaluating...");
        var evaluator = new DetectionEvaluator(_options.MatchThreshold);
        var result = evaluator.Evaluate(groundTruth, deduplicated);

        // Run error analysis
        Console.WriteLine("Analyzing error patterns...");
        var errorAnalysis = _errorAnalyzer.Analyze(result.FalseAlarms);
        var missedAnalysis = _errorAnalyzer.AnalyzeMissed(result.Missed);

        // Get true positive detected questions for confidence analysis
        var tpDetected = result.Matches.Select(m => m.Detected).ToList();
        var confidenceBuckets = _errorAnalyzer.AnalyzeByConfidence(result.FalseAlarms, tpDetected);

        // Evaluate subtype accuracy
        Console.WriteLine("Evaluating subtype accuracy...");
        var subtypeResult = _subtypeEvaluator.Evaluate(result.Matches);
        var misclassifications = _subtypeEvaluator.GetMisclassifications(result.Matches);

        // Create enhanced result
        var enhancedResult = result with
        {
            ErrorAnalysis = errorAnalysis,
            MissedAnalysis = missedAnalysis,
            SubtypeMetrics = subtypeResult.SubtypeMetrics.ToDictionary(
                kvp => kvp.Key,
                kvp => new SubtypeMetrics(kvp.Key, kvp.Value.CorrectCount, kvp.Value.TotalCount, kvp.Value.Accuracy))
        };

        // Output results
        PrintResults(enhancedResult, groundTruth.Count, deduplicated.Count);
        PrintSubtypeResults(subtypeResult, misclassifications);
        PrintErrorAnalysis(errorAnalysis, confidenceBuckets);
        PrintMissedAnalysis(missedAnalysis);
        PrintGroundTruthRaw(groundTruthResult.RawLlmResponse);

        // Save to file if requested
        if (!string.IsNullOrWhiteSpace(outputFile))
        {
            await SaveResultsAsync(outputFile, sessionFile, transcript, groundTruth, deduplicated,
                enhancedResult, errorAnalysis, missedAnalysis, confidenceBuckets, subtypeResult, ct);
            Console.WriteLine();
            Console.WriteLine($"Results saved to: {outputFile}");
        }

        return 0;
    }

    /// <summary>
    /// Run error analysis on an existing evaluation report file.
    /// </summary>
    public async Task<int> AnalyzeErrorsAsync(string reportFile, CancellationToken ct = default)
    {
        Console.WriteLine("=== Error Pattern Analysis ===");
        Console.WriteLine($"Report: {Path.GetFileName(reportFile)}");
        Console.WriteLine();

        // Load existing report
        var json = await File.ReadAllTextAsync(reportFile, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Extract false alarms
        var falseAlarms = new List<DetectedQuestionInfo>();
        if (root.TryGetProperty("FalseAlarms", out var faArray))
        {
            foreach (var fa in faArray.EnumerateArray())
            {
                var text = fa.TryGetProperty("Text", out var textProp) ? textProp.GetString() ?? "" : "";
                var confidence = fa.TryGetProperty("Confidence", out var confProp) ? confProp.GetDouble() : 0.5;
                falseAlarms.Add(new DetectedQuestionInfo(text, null, confidence, "", 0));
            }
        }

        if (falseAlarms.Count == 0)
        {
            Console.WriteLine("No false alarms found in report.");
            return 0;
        }

        // Analyze
        var analysis = _errorAnalyzer.Analyze(falseAlarms);

        // Get true positives for confidence analysis
        var truePositives = new List<DetectedQuestionInfo>();
        if (root.TryGetProperty("Matches", out var matchArray))
        {
            foreach (var match in matchArray.EnumerateArray())
            {
                var detected = match.TryGetProperty("Detected", out var detProp) ? detProp.GetString() ?? "" : "";
                truePositives.Add(new DetectedQuestionInfo(detected, null, 0.8, "", 0));
            }
        }

        var confidenceBuckets = _errorAnalyzer.AnalyzeByConfidence(falseAlarms, truePositives);

        PrintErrorAnalysis(analysis, confidenceBuckets);

        return 0;
    }

    private static void PrintSubtypeResults(SubtypeEvaluationResult result, IReadOnlyList<SubtypeMisclassification> misclassifications)
    {
        if (result.TotalWithSubtype == 0) return;

        Console.WriteLine();
        Console.WriteLine("=== Subtype Accuracy ===");
        Console.WriteLine($"Overall: {result.OverallAccuracy:P1} ({result.TotalCorrect}/{result.TotalWithSubtype})");
        Console.WriteLine();

        Console.WriteLine("Subtype      | Correct | Total | Accuracy");
        Console.WriteLine("-------------|---------|-------|----------");

        foreach (var (subtype, metric) in result.SubtypeMetrics.OrderByDescending(kvp => kvp.Value.TotalCount))
        {
            Console.WriteLine($"{subtype,-12} | {metric.CorrectCount,7} | {metric.TotalCount,5} | {metric.Accuracy,8:P0}");
        }

        if (misclassifications.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Misclassifications ({misclassifications.Count}):");
            foreach (var m in misclassifications.Take(5))
            {
                Console.WriteLine($"  Expected: {m.ExpectedSubtype}, Got: {m.ActualSubtype}");
                Console.WriteLine($"    \"{Truncate(m.QuestionText, 50)}\"");
            }
        }
    }

    private static void PrintErrorAnalysis(ErrorAnalysisResult analysis, IReadOnlyList<ConfidenceBucket> buckets)
    {
        Console.WriteLine();
        Console.WriteLine("=== Error Pattern Analysis ===");
        Console.WriteLine($"Total False Positives: {analysis.TotalFalsePositives}");
        Console.WriteLine();

        if (analysis.PatternCounts.Count > 0)
        {
            Console.WriteLine("Pattern Breakdown:");
            foreach (var pattern in analysis.PatternCounts)
            {
                var pct = analysis.TotalFalsePositives > 0
                    ? (double)pattern.Count / analysis.TotalFalsePositives * 100
                    : 0;
                Console.WriteLine($"  {pattern.Pattern.Name,-25} {pattern.Count,4} ({pct:F1}%)");
                Console.WriteLine($"    {pattern.Pattern.Description}");
                if (pattern.Examples.Count > 0)
                {
                    Console.WriteLine($"    Examples: \"{Truncate(pattern.Examples[0], 50)}\"");
                }
                Console.WriteLine();
            }
        }

        if (analysis.UnclassifiedCount > 0)
        {
            Console.WriteLine($"Unclassified: {analysis.UnclassifiedCount} false positives");
            foreach (var example in analysis.Unclassified.Take(5))
            {
                Console.WriteLine($"  - \"{Truncate(example, 60)}\"");
            }
            Console.WriteLine();
        }

        Console.WriteLine("Confidence Distribution:");
        Console.WriteLine("Bucket     | FP  | TP  | Precision");
        Console.WriteLine("-----------|-----|-----|----------");
        foreach (var bucket in buckets.Where(b => b.Total > 0))
        {
            Console.WriteLine($"{bucket.LowerBound:F1}-{bucket.UpperBound:F1}    | {bucket.FalsePositives,3} | {bucket.TruePositives,3} | {bucket.Precision:P0}");
        }
    }

    private static void PrintMissedAnalysis(MissedQuestionAnalysis analysis)
    {
        if (analysis.Total == 0) return;

        Console.WriteLine();
        Console.WriteLine("=== Missed Questions Analysis ===");
        Console.WriteLine($"Total Missed: {analysis.Total}");
        Console.WriteLine();

        if (analysis.BySubtype.Count > 0)
        {
            Console.WriteLine("By Subtype:");
            foreach (var (subtype, count) in analysis.BySubtype.OrderByDescending(kvp => kvp.Value))
            {
                Console.WriteLine($"  {subtype,-15} {count}");
            }
            Console.WriteLine();
        }

        if (analysis.NoQuestionMark.Count > 0)
        {
            Console.WriteLine($"Without question mark ({analysis.NoQuestionMark.Count}):");
            foreach (var q in analysis.NoQuestionMark.Take(3))
            {
                Console.WriteLine($"  - \"{Truncate(q, 60)}\"");
            }
            Console.WriteLine();
        }

        if (analysis.EmbeddedQuestions.Count > 0)
        {
            Console.WriteLine($"Embedded/Indirect questions ({analysis.EmbeddedQuestions.Count}):");
            foreach (var q in analysis.EmbeddedQuestions.Take(3))
            {
                Console.WriteLine($"  - \"{Truncate(q, 60)}\"");
            }
            Console.WriteLine();
        }

        if (analysis.ShortQuestions.Count > 0)
        {
            Console.WriteLine($"Short questions <30 chars ({analysis.ShortQuestions.Count}):");
            foreach (var q in analysis.ShortQuestions.Take(3))
            {
                Console.WriteLine($"  - \"{q}\"");
            }
        }
    }

    private static void PrintGroundTruthRaw(string rawLlmResponse)
    {
        if (string.IsNullOrWhiteSpace(rawLlmResponse))
            return;

        Console.WriteLine();
        Console.WriteLine("=== Raw Ground Truth LLM Response ===");

        // Pretty-print if the response is valid JSON, otherwise print as-is
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawLlmResponse);
            var formatted = System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(formatted);
        }
        catch (System.Text.Json.JsonException)
        {
            Console.WriteLine(rawLlmResponse);
        }
    }

    /// <summary>
    /// Run threshold tuning to find optimal confidence settings.
    /// </summary>
    public async Task<int> TuneThresholdAsync(
        string sessionFile,
        OptimizationTarget target = OptimizationTarget.F1,
        CancellationToken ct = default)
    {
        Console.WriteLine("=== Confidence Threshold Tuning ===");
        Console.WriteLine($"Session: {Path.GetFileName(sessionFile)}");
        Console.WriteLine($"Optimization target: {target}");
        Console.WriteLine();

        // Load session events
        Console.WriteLine("Loading session...");
        var events = await LoadEventsAsync(sessionFile, ct);
        if (events.Count == 0)
        {
            Console.WriteLine("Error: No events found in session file.");
            return 1;
        }

        // Extract transcript
        Console.WriteLine("Extracting transcript...");
        var transcript = TranscriptExtractor.ExtractFullTranscript(events);
        Console.WriteLine($"  Characters: {transcript.Length:N0}");

        // Extract ALL detected questions (no threshold filtering)
        Console.WriteLine("Extracting detected questions...");
        var allDetected = TranscriptExtractor.ExtractDetectedQuestions(events, candidatesOnly: false);
        Console.WriteLine($"  Total detection events: {allDetected.Count}");
        Console.WriteLine();

        // Extract ground truth using LLM
        Console.WriteLine($"Extracting ground truth using {_options.Model}...");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            Console.WriteLine("Error: OpenAI API key required for ground truth extraction.");
            return 1;
        }

        using var extractor = new GroundTruthExtractor(_options.ApiKey, _options.Model);
        var groundTruth = await extractor.ExtractQuestionsAsync(transcript, ct);
        Console.WriteLine($"  Questions found: {groundTruth.Count}");
        Console.WriteLine();

        // Run threshold tuning
        Console.WriteLine("Testing confidence thresholds...");
        var tuner = new ThresholdTuner(0.3, 0.95, 0.05);
        var result = tuner.Tune(groundTruth, allDetected, _options.MatchThreshold);

        // Print results
        PrintThresholdResults(result);

        // Print recommendations
        var currentThreshold = _options.MatchThreshold;
        var recommendations = ThresholdTuner.GenerateRecommendations(result, currentThreshold);

        if (recommendations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("=== Recommendations ===");
            foreach (var rec in recommendations)
            {
                Console.WriteLine($"  - {rec}");
            }
        }

        return 0;
    }

    private static void PrintThresholdResults(ThresholdTuningResult result)
    {
        Console.WriteLine();
        Console.WriteLine("Threshold | Detected | TP  | FP  | FN  | Precision | Recall | F1");
        Console.WriteLine("----------|----------|-----|-----|-----|-----------|--------|------");

        foreach (var r in result.Results)
        {
            var marker = "";
            if (r.Threshold == result.OptimalForF1.Threshold)
                marker = " <-- Best F1";
            else if (r.Threshold == result.BalancedOptimal.Threshold && result.BalancedOptimal.Threshold != result.OptimalForF1.Threshold)
                marker = " <-- Balanced";

            Console.WriteLine(
                $"{r.Threshold:F2}      | {r.DetectionCount,8} | {r.TruePositives,3} | {r.FalsePositives,3} | {r.FalseNegatives,3} | {r.Precision,9:P0} | {r.Recall,6:P0} | {r.F1Score:P0}{marker}");
        }

        Console.WriteLine();
        Console.WriteLine($"Optimal for F1:        {result.OptimalForF1.Threshold:F2} (F1: {result.OptimalForF1.F1Score:P1})");
        Console.WriteLine($"Optimal for Precision: {result.OptimalForPrecision.Threshold:F2} (Precision: {result.OptimalForPrecision.Precision:P1})");
        Console.WriteLine($"Optimal for Recall:    {result.OptimalForRecall.Threshold:F2} (Recall: {result.OptimalForRecall.Recall:P1})");
        Console.WriteLine($"Balanced (F1 + P>=50%): {result.BalancedOptimal.Threshold:F2} (F1: {result.BalancedOptimal.F1Score:P1}, P: {result.BalancedOptimal.Precision:P1})");
    }

    /// <summary>
    /// Compare all detection strategies on the same session data.
    /// </summary>
    public async Task<int> CompareStrategiesAsync(
        string sessionFile,
        string? outputFile,
        HeuristicDetectionOptions? heuristicOptions,
        LlmDetectionOptions? llmOptions,
        DeepgramDetectionOptions? deepgramOptions = null,
        CancellationToken ct = default)
    {
        Console.WriteLine("=== Strategy Comparison ===");
        Console.WriteLine($"Session: {Path.GetFileName(sessionFile)}");
        Console.WriteLine();

        // Load session events
        Console.WriteLine("Loading session...");
        var events = await LoadEventsAsync(sessionFile, ct);
        if (events.Count == 0)
        {
            Console.WriteLine("Error: No events found in session file.");
            return 1;
        }

        // Extract transcript for ground truth
        Console.WriteLine("Extracting transcript...");
        var transcript = TranscriptExtractor.ExtractFullTranscript(events);
        Console.WriteLine($"  Characters: {transcript.Length:N0}");

        // Extract ground truth using LLM
        Console.WriteLine($"Extracting ground truth using {_options.Model}...");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            Console.WriteLine("Error: OpenAI API key required for ground truth extraction.");
            return 1;
        }

        using var extractor = new GroundTruthExtractor(_options.ApiKey, _options.Model);
        var groundTruthResult = await extractor.ExtractQuestionsWithRawAsync(transcript, ct);
        var groundTruth = groundTruthResult.Questions;
        Console.WriteLine($"  Questions found: {groundTruth.Count}");
        Console.WriteLine();

        // Run strategy comparison
        Console.WriteLine("Comparing strategies...");
        var comparer = new StrategyComparer(_options);
        var result = await comparer.CompareAsync(events, groundTruth, heuristicOptions, llmOptions, deepgramOptions, ct);

        // Print results
        PrintComparisonResults(result);
        PrintGroundTruthRaw(groundTruthResult.RawLlmResponse);

        // Save to file if requested
        if (!string.IsNullOrWhiteSpace(outputFile))
        {
            await SaveComparisonResultsAsync(outputFile, sessionFile, result, ct);
            Console.WriteLine();
            Console.WriteLine($"Results saved to: {outputFile}");
        }

        return 0;
    }

    private static void PrintComparisonResults(StrategyComparisonResult result)
    {
        Console.WriteLine();
        Console.WriteLine($"Ground Truth: {result.GroundTruthCount} questions");
        Console.WriteLine();
        Console.WriteLine("Strategy    | Detected | TP  | FP  | FN  | Precision | Recall | F1     | Latency");
        Console.WriteLine("------------|----------|-----|-----|-----|-----------|--------|--------|--------");

        foreach (var r in result.Results)
        {
            var marker = "";
            if (r.StrategyName == result.BestForF1)
                marker = " *";

            Console.WriteLine(
                $"{r.StrategyName,-11} | {r.DetectionCount,8} | {r.TruePositives,3} | {r.FalsePositives,3} | {r.FalseNegatives,3} | {r.Precision,9:P0} | {r.Recall,6:P0} | {r.F1Score,6:P0} | {r.AverageLatencyMs,5:F0}ms{marker}");
        }

        Console.WriteLine();
        Console.WriteLine($"* Best F1: {result.BestForF1}");
        Console.WriteLine($"  Best Precision: {result.BestForPrecision}");
        Console.WriteLine($"  Best Recall: {result.BestForRecall}");
    }

    private static async Task SaveComparisonResultsAsync(
        string outputFile,
        string sessionFile,
        StrategyComparisonResult result,
        CancellationToken ct)
    {
        var report = new
        {
            GeneratedAt = DateTime.UtcNow,
            SessionFile = Path.GetFileName(sessionFile),
            GroundTruthCount = result.GroundTruthCount,
            Strategies = result.Results.Select(r => new
            {
                r.StrategyName,
                r.DetectionCount,
                r.TruePositives,
                r.FalsePositives,
                r.FalseNegatives,
                r.Precision,
                r.Recall,
                r.F1Score,
                r.AverageLatencyMs,
                r.TotalProcessingMs
            }),
            BestForF1 = result.BestForF1,
            BestForPrecision = result.BestForPrecision,
            BestForRecall = result.BestForRecall
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        var directory = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(outputFile, json, ct);
    }

    private static async Task<IReadOnlyList<RecordedEvent>> LoadEventsAsync(string filePath, CancellationToken ct)
    {
        var events = new List<RecordedEvent>();
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        await foreach (var line in File.ReadLinesAsync(filePath, ct))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var evt = JsonSerializer.Deserialize<RecordedEvent>(line, jsonOptions);
                if (evt != null)
                    events.Add(evt);
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return events;
    }

    private static void PrintResults(EvaluationResult result, int groundTruthCount, int detectedCount)
    {
        Console.WriteLine();
        Console.WriteLine("=== Metrics ===");
        Console.WriteLine($"Ground Truth:    {groundTruthCount} questions");
        Console.WriteLine($"Detected:        {detectedCount} unique questions");
        Console.WriteLine();
        Console.WriteLine($"True Positives:  {result.TruePositives}");
        Console.WriteLine($"False Positives: {result.FalsePositives}");
        Console.WriteLine($"False Negatives: {result.FalseNegatives}");
        Console.WriteLine();
        Console.WriteLine($"Precision:       {result.Precision:P1}");
        Console.WriteLine($"Recall:          {result.Recall:P1}");
        Console.WriteLine($"F1 Score:        {result.F1Score:P1}");

        if (result.Matches.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"=== Matched Questions ({result.Matches.Count}) ===");
            foreach (var match in result.Matches)
            {
                Console.WriteLine($"  [GT] \"{match.GroundTruth.Text}\"");
                Console.WriteLine($"  [DT] \"{match.Detected.Text}\" (match: {match.SimilarityScore:P0})");
                Console.WriteLine();
            }
        }

        if (result.Missed.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"=== MISSED - Questions/Imperatives Not Detected ({result.Missed.Count}) ===");
            foreach (var missed in result.Missed)
            {
                var subtype = missed.Subtype != null ? $" [{missed.Subtype}]" : "";
                Console.WriteLine($"  - \"{missed.Text}\"{subtype}");
            }
        }

        if (result.FalseAlarms.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"=== False Alarms ({result.FalseAlarms.Count}) ===");
            foreach (var fa in result.FalseAlarms)
            {
                Console.WriteLine($"  - \"{Truncate(fa.Text, 80)}\" (conf: {fa.Confidence:F1})");
            }
        }
    }

    private static async Task SaveResultsAsync(
        string outputFile,
        string sessionFile,
        string transcript,
        IReadOnlyList<ExtractedQuestion> groundTruth,
        IReadOnlyList<RecordedIntentEvent> detected,
        EvaluationResult result,
        ErrorAnalysisResult errorAnalysis,
        MissedQuestionAnalysis missedAnalysis,
        IReadOnlyList<ConfidenceBucket> confidenceBuckets,
        SubtypeEvaluationResult subtypeResult,
        CancellationToken ct)
    {
        var report = new
        {
            GeneratedAt = DateTime.UtcNow,
            SessionFile = Path.GetFileName(sessionFile),
            TranscriptLength = transcript.Length,
            Metrics = new
            {
                result.TruePositives,
                result.FalsePositives,
                result.FalseNegatives,
                result.Precision,
                result.Recall,
                result.F1Score
            },
            GroundTruth = groundTruth.Select(q => new { q.Text, q.Subtype, q.Confidence }),
            DetectedQuestions = detected.Select(q => new
            {
                q.Data.Intent.SourceText,
                q.Data.Intent.Subtype,
                q.Data.Intent.Confidence,
                q.Data.UtteranceId
            }),
            Matches = result.Matches.Select(m => new
            {
                GroundTruth = m.GroundTruth.Text,
                GroundTruthSubtype = m.GroundTruth.Subtype,
                Detected = m.Detected.Text,
                DetectedSubtype = m.Detected.Subtype,
                m.SimilarityScore
            }),
            Missed = result.Missed.Select(q => q.Text),
            FalseAlarms = result.FalseAlarms.Select(q => new { q.Text, q.Confidence }),
            SubtypeAccuracy = new
            {
                subtypeResult.OverallAccuracy,
                subtypeResult.TotalWithSubtype,
                subtypeResult.TotalCorrect,
                BySubtype = subtypeResult.SubtypeMetrics.Select(kvp => new
                {
                    kvp.Key,
                    kvp.Value.CorrectCount,
                    kvp.Value.TotalCount,
                    kvp.Value.Accuracy
                })
            },
            ErrorAnalysis = new
            {
                errorAnalysis.TotalFalsePositives,
                Patterns = errorAnalysis.PatternCounts.Select(p => new
                {
                    p.Pattern.Name,
                    p.Pattern.Description,
                    p.Count,
                    p.Examples
                }),
                errorAnalysis.UnclassifiedCount,
                UnclassifiedExamples = errorAnalysis.Unclassified.Take(10)
            },
            MissedAnalysis = new
            {
                missedAnalysis.Total,
                missedAnalysis.BySubtype,
                missedAnalysis.ShortQuestions,
                missedAnalysis.NoQuestionMark,
                missedAnalysis.EmbeddedQuestions
            },
            ConfidenceDistribution = confidenceBuckets.Select(b => new
            {
                Range = $"{b.LowerBound:F1}-{b.UpperBound:F1}",
                b.FalsePositives,
                b.TruePositives,
                b.Total,
                b.Precision
            }),
            FullTranscript = transcript
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        var directory = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(outputFile, json, ct);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }

    /// <summary>
    /// Run regression test against a baseline.
    /// </summary>
    public async Task<int> RunRegressionTestAsync(
        string baselineFile,
        string sessionFile,
        CancellationToken ct = default)
    {
        Console.WriteLine("=== Regression Test ===");
        Console.WriteLine($"Baseline: {Path.GetFileName(baselineFile)}");
        Console.WriteLine($"Session: {Path.GetFileName(sessionFile)}");
        Console.WriteLine();

        // Load baseline
        Console.WriteLine("Loading baseline...");
        var baseline = await RegressionTester.LoadBaselineAsync(baselineFile, ct);
        Console.WriteLine($"  Version: {baseline.Version}");
        Console.WriteLine($"  Created: {baseline.CreatedAt:yyyy-MM-dd HH:mm}");
        Console.WriteLine();

        // Run evaluation on current data
        Console.WriteLine("Running evaluation...");
        var events = await LoadEventsAsync(sessionFile, ct);
        var transcript = TranscriptExtractor.ExtractFullTranscript(events);
        var allDetected = TranscriptExtractor.ExtractDetectedQuestions(events, candidatesOnly: false);
        var deduplicated = TranscriptExtractor.DeduplicateQuestions(allDetected, _options.DeduplicationThreshold);

        // Extract ground truth
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            Console.WriteLine("Error: OpenAI API key required for ground truth extraction.");
            return 1;
        }

        using var extractor = new GroundTruthExtractor(_options.ApiKey, _options.Model);
        var groundTruthResult = await extractor.ExtractQuestionsWithRawAsync(transcript, ct);
        var groundTruth = groundTruthResult.Questions;

        var evaluator = new DetectionEvaluator(_options.MatchThreshold);
        var result = evaluator.Evaluate(groundTruth, deduplicated);

        // Run regression test
        var tester = new RegressionTester();
        var testResult = tester.Test(baseline, result);

        // Print results
        PrintRegressionResults(testResult);
        PrintGroundTruthRaw(groundTruthResult.RawLlmResponse);

        return testResult.OverallStatus == RegressionStatus.Failed ? 1 : 0;
    }

    /// <summary>
    /// Create a new baseline from session data.
    /// </summary>
    public async Task<int> CreateBaselineAsync(
        string sessionFile,
        string outputFile,
        string version,
        CancellationToken ct = default)
    {
        Console.WriteLine("=== Create Baseline ===");
        Console.WriteLine($"Session: {Path.GetFileName(sessionFile)}");
        Console.WriteLine($"Output: {outputFile}");
        Console.WriteLine();

        // Run evaluation
        Console.WriteLine("Running evaluation...");
        var events = await LoadEventsAsync(sessionFile, ct);
        var transcript = TranscriptExtractor.ExtractFullTranscript(events);
        var allDetected = TranscriptExtractor.ExtractDetectedQuestions(events, candidatesOnly: false);
        var deduplicated = TranscriptExtractor.DeduplicateQuestions(allDetected, _options.DeduplicationThreshold);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            Console.WriteLine("Error: OpenAI API key required for ground truth extraction.");
            return 1;
        }

        using var extractor = new GroundTruthExtractor(_options.ApiKey, _options.Model);
        var groundTruth = await extractor.ExtractQuestionsAsync(transcript, ct);

        var evaluator = new DetectionEvaluator(_options.MatchThreshold);
        var result = evaluator.Evaluate(groundTruth, deduplicated);

        // Create baseline
        var baseline = RegressionTester.CreateBaseline(version, result);
        baseline = new Baseline
        {
            Version = baseline.Version,
            CreatedAt = baseline.CreatedAt,
            Metrics = baseline.Metrics,
            Thresholds = baseline.Thresholds,
            TestDataFile = Path.GetFileName(sessionFile)
        };

        await RegressionTester.SaveBaselineAsync(outputFile, baseline, ct);

        Console.WriteLine();
        Console.WriteLine($"Baseline created: {outputFile}");
        Console.WriteLine($"  Precision: {baseline.Metrics.Precision:P1} (min: {baseline.Thresholds.PrecisionMin:P1})");
        Console.WriteLine($"  Recall: {baseline.Metrics.Recall:P1} (min: {baseline.Thresholds.RecallMin:P1})");
        Console.WriteLine($"  F1 Score: {baseline.Metrics.F1Score:P1} (min: {baseline.Thresholds.F1Min:P1})");

        return 0;
    }

    private static void PrintRegressionResults(RegressionTestResult result)
    {
        Console.WriteLine();

        var statusSymbol = result.OverallStatus switch
        {
            RegressionStatus.Passed => "PASS",
            RegressionStatus.Improved => "IMPROVED",
            RegressionStatus.Failed => "REGRESSION DETECTED",
            _ => "UNKNOWN"
        };

        Console.WriteLine($"=== {statusSymbol} ===");
        Console.WriteLine();

        Console.WriteLine("Metric     | Baseline | Current | Delta   | Min     | Status");
        Console.WriteLine("-----------|----------|---------|---------|---------|--------");

        foreach (var c in result.Comparisons)
        {
            var statusStr = c.Status switch
            {
                ComparisonStatus.Regression => "FAIL",
                ComparisonStatus.Improvement => "BETTER",
                _ => "OK"
            };

            var deltaStr = c.Delta >= 0 ? $"+{c.Delta:P1}" : $"{c.Delta:P1}";

            Console.WriteLine(
                $"{c.MetricName,-10} | {c.BaselineValue,8:P1} | {c.CurrentValue,7:P1} | {deltaStr,7} | {c.MinThreshold,7:P1} | {statusStr}");
        }

        Console.WriteLine();

        if (result.OverallStatus == RegressionStatus.Failed)
        {
            Console.WriteLine("Exit code: 1 (regression detected)");
        }
        else if (result.OverallStatus == RegressionStatus.Improved)
        {
            Console.WriteLine("Consider updating baseline to capture improvements.");
        }
    }
}
