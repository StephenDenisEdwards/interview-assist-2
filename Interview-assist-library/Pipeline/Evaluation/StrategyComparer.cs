using System.Diagnostics;
using InterviewAssist.Library.Pipeline.Detection;
using InterviewAssist.Library.Pipeline.Recording;
using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Compares different detection strategies on the same input data.
/// </summary>
public sealed class StrategyComparer
{
    private readonly EvaluationOptions _options;

    public StrategyComparer(EvaluationOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Compare all strategies on the given session data.
    /// </summary>
    public async Task<StrategyComparisonResult> CompareAsync(
        IReadOnlyList<RecordedEvent> events,
        IReadOnlyList<ExtractedQuestion> groundTruth,
        HeuristicDetectionOptions? heuristicOptions = null,
        LlmDetectionOptions? llmOptions = null,
        DeepgramDetectionOptions? deepgramOptions = null,
        CancellationToken ct = default)
    {
        var results = new List<StrategyResult>();

        // Extract ASR events for replay
        var asrEvents = events
            .OfType<RecordedAsrEvent>()
            .OrderBy(e => e.OffsetMs)
            .ToList();

        // Test Heuristic strategy
        var heuristicResult = await TestStrategyAsync(
            "Heuristic",
            () => new HeuristicIntentStrategy(heuristicOptions ?? new HeuristicDetectionOptions()),
            asrEvents,
            groundTruth,
            ct);
        results.Add(heuristicResult);

        // Test LLM strategy (if API key available)
        if (!string.IsNullOrWhiteSpace(llmOptions?.ApiKey))
        {
            var llmResult = await TestStrategyAsync(
                "LLM",
                () => CreateLlmStrategy(llmOptions),
                asrEvents,
                groundTruth,
                ct);
            results.Add(llmResult);

            // Test Parallel strategy
            var parallelResult = await TestStrategyAsync(
                "Parallel",
                () => CreateParallelStrategy(heuristicOptions ?? new HeuristicDetectionOptions(), llmOptions),
                asrEvents,
                groundTruth,
                ct);
            results.Add(parallelResult);
        }

        // Test Deepgram strategy (if API key available)
        if (!string.IsNullOrWhiteSpace(deepgramOptions?.ApiKey))
        {
            var deepgramResult = await TestStrategyAsync(
                "Deepgram",
                () => CreateDeepgramStrategy(deepgramOptions, llmOptions ?? new LlmDetectionOptions()),
                asrEvents,
                groundTruth,
                ct);
            results.Add(deepgramResult);
        }

        // Determine best strategy for each metric
        var bestF1 = results.OrderByDescending(r => r.F1Score).First();
        var bestPrecision = results.OrderByDescending(r => r.Precision).First();
        var bestRecall = results.OrderByDescending(r => r.Recall).First();

        return new StrategyComparisonResult(
            Results: results,
            BestForF1: bestF1.StrategyName,
            BestForPrecision: bestPrecision.StrategyName,
            BestForRecall: bestRecall.StrategyName,
            GroundTruthCount: groundTruth.Count);
    }

    private async Task<StrategyResult> TestStrategyAsync(
        string strategyName,
        Func<IIntentDetectionStrategy> strategyFactory,
        IReadOnlyList<RecordedAsrEvent> asrEvents,
        IReadOnlyList<ExtractedQuestion> groundTruth,
        CancellationToken ct)
    {
        var detections = new List<DetectedIntent>();
        var latencies = new List<double>();
        var sw = Stopwatch.StartNew();

        using var strategy = strategyFactory();
        var pipeline = new UtteranceIntentPipeline(detectionStrategy: strategy);

        var detectionSw = new Stopwatch();

        pipeline.OnIntentFinal += evt =>
        {
            if (evt.Intent.Type == IntentType.Question)
            {
                detections.Add(evt.Intent);
                if (detectionSw.IsRunning)
                {
                    latencies.Add(detectionSw.ElapsedMilliseconds);
                    detectionSw.Restart();
                }
            }
        };

        // Replay ASR events with real-time pacing so async strategies
        // (LLM, Deepgram) have time for their rate limiters and triggers to fire.
        detectionSw.Start();
        long previousOffsetMs = 0;

        foreach (var asr in asrEvents)
        {
            ct.ThrowIfCancellationRequested();

            // Simulate real-time gaps between events
            var delayMs = asr.OffsetMs - previousOffsetMs;
            if (delayMs > 0)
            {
                // Cap individual delays to avoid excessively long waits
                var cappedDelay = Math.Min(delayMs, 5000);
                await Task.Delay((int)cappedDelay, ct);
            }
            previousOffsetMs = asr.OffsetMs;

            pipeline.ProcessAsrEvent(new AsrEvent
            {
                Text = asr.Data.Text,
                IsFinal = asr.Data.IsFinal,
                SpeakerId = asr.Data.SpeakerId
            });

            if (asr.Data.IsFinal)
            {
                pipeline.SignalUtteranceEnd();
            }
        }

        // Wait for any pending async detections to complete
        await Task.Delay(4000, ct);

        sw.Stop();

        // Convert to RecordedIntentEvent for evaluation
        var recordedDetections = detections.Select((d, i) => new RecordedIntentEvent
        {
            OffsetMs = i * 100,
            Data = new IntentEventData
            {
                Intent = new DetectedIntentData
                {
                    Type = d.Type.ToString(),
                    Subtype = d.Subtype?.ToString(),
                    Confidence = d.Confidence,
                    SourceText = d.SourceText,
                    OriginalText = d.OriginalText
                },
                UtteranceId = $"u{i}",
                IsCandidate = false
            }
        }).ToList();

        // Deduplicate and evaluate
        var deduplicated = TranscriptExtractor.DeduplicateQuestions(recordedDetections, _options.DeduplicationThreshold);
        var evaluator = new DetectionEvaluator(_options.MatchThreshold);
        var evalResult = evaluator.Evaluate(groundTruth, deduplicated);

        pipeline.Dispose();

        return new StrategyResult(
            StrategyName: strategyName,
            DetectionCount: deduplicated.Count,
            TruePositives: evalResult.TruePositives,
            FalsePositives: evalResult.FalsePositives,
            FalseNegatives: evalResult.FalseNegatives,
            Precision: evalResult.Precision,
            Recall: evalResult.Recall,
            F1Score: evalResult.F1Score,
            AverageLatencyMs: latencies.Count > 0 ? latencies.Average() : 0,
            TotalProcessingMs: sw.ElapsedMilliseconds);
    }

    private static LlmIntentStrategy CreateLlmStrategy(LlmDetectionOptions options)
    {
        var detector = new OpenAiIntentDetector(
            options.ApiKey!,
            options.Model,
            options.ConfidenceThreshold);

        return new LlmIntentStrategy(detector, options);
    }

    private static ParallelIntentStrategy CreateParallelStrategy(
        HeuristicDetectionOptions heuristicOptions,
        LlmDetectionOptions llmOptions)
    {
        var detector = new OpenAiIntentDetector(
            llmOptions.ApiKey!,
            llmOptions.Model,
            llmOptions.ConfidenceThreshold);

        return new ParallelIntentStrategy(detector, heuristicOptions, llmOptions);
    }

    private static LlmIntentStrategy CreateDeepgramStrategy(
        DeepgramDetectionOptions deepgramOptions,
        LlmDetectionOptions llmOptions)
    {
        var detector = new DeepgramIntentDetector(
            deepgramOptions.ApiKey!,
            deepgramOptions);

        return new LlmIntentStrategy(detector, llmOptions);
    }
}

/// <summary>
/// Result for a single strategy test.
/// </summary>
public sealed record StrategyResult(
    string StrategyName,
    int DetectionCount,
    int TruePositives,
    int FalsePositives,
    int FalseNegatives,
    double Precision,
    double Recall,
    double F1Score,
    double AverageLatencyMs,
    long TotalProcessingMs);

/// <summary>
/// Complete strategy comparison results.
/// </summary>
public sealed record StrategyComparisonResult(
    IReadOnlyList<StrategyResult> Results,
    string BestForF1,
    string BestForPrecision,
    string BestForRecall,
    int GroundTruthCount);
