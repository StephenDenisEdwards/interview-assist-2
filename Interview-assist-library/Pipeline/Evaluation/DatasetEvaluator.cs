using InterviewAssist.Library.Pipeline.Detection;
using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Evaluates detection accuracy against curated datasets.
/// </summary>
public sealed class DatasetEvaluator
{
    /// <summary>
    /// Evaluate a detection strategy against a curated dataset.
    /// </summary>
    public async Task<DatasetEvaluationResult> EvaluateAsync(
        EvaluationDataset dataset,
        IIntentDetectionStrategy strategy,
        CancellationToken ct = default)
    {
        var results = new List<DatasetItemResult>();
        var confusionMatrix = new ConfusionMatrix();

        foreach (var item in dataset.Items)
        {
            ct.ThrowIfCancellationRequested();

            var result = await EvaluateItemAsync(item, strategy, ct);
            results.Add(result);

            confusionMatrix.Add(item.Type, result.PredictedType);
        }

        // Calculate metrics
        var typeAccuracy = confusionMatrix.OverallAccuracy;

        var questionPrecision = confusionMatrix.GetPrecision("Question");
        var questionRecall = confusionMatrix.GetRecall("Question");
        var questionF1 = confusionMatrix.GetF1Score("Question");

        // Subtype accuracy for questions
        var questionItems = results.Where(r => r.ActualType == "Question").ToList();
        var subtypeCorrect = questionItems.Count(r =>
            NormalizeSubtype(r.ActualSubtype) == NormalizeSubtype(r.PredictedSubtype));
        var subtypeAccuracy = questionItems.Count > 0
            ? (double)subtypeCorrect / questionItems.Count
            : 0.0;

        return new DatasetEvaluationResult(
            DatasetPath: dataset.FilePath,
            TotalItems: dataset.Items.Count,
            CorrectType: results.Count(r => r.TypeCorrect),
            TypeAccuracy: typeAccuracy,
            QuestionPrecision: questionPrecision,
            QuestionRecall: questionRecall,
            QuestionF1: questionF1,
            SubtypeAccuracy: subtypeAccuracy,
            ConfusionMatrix: confusionMatrix,
            ItemResults: results);
    }

    private static async Task<DatasetItemResult> EvaluateItemAsync(
        DatasetItem item,
        IIntentDetectionStrategy strategy,
        CancellationToken ct)
    {
        DetectedIntent? detected = null;

        try
        {
            // Create a simple pipeline to process the text
            using var pipeline = new UtteranceIntentPipeline(detectionStrategy: strategy);

            var tcs = new TaskCompletionSource<DetectedIntent?>();
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            pipeline.OnIntentFinal += evt =>
            {
                tcs.TrySetResult(evt.Intent);
            };

            // Simulate ASR event
            pipeline.ProcessAsrEvent(new AsrEvent
            {
                Text = item.Text,
                IsFinal = true
            });

            pipeline.SignalUtteranceEnd();

            // Wait for detection or timeout
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                detected = await tcs.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout - no detection
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error evaluating '{item.Text[..Math.Min(30, item.Text.Length)]}...': {ex.Message}");
        }

        var predictedType = detected?.Type.ToString() ?? "Statement";
        var predictedSubtype = detected?.Subtype?.ToString();
        var confidence = detected?.Confidence ?? 0.0;

        return new DatasetItemResult(
            Text: item.Text,
            ActualType: item.Type,
            ActualSubtype: item.Subtype,
            PredictedType: predictedType,
            PredictedSubtype: predictedSubtype,
            Confidence: confidence,
            TypeCorrect: item.Type == predictedType,
            SubtypeCorrect: NormalizeSubtype(item.Subtype) == NormalizeSubtype(predictedSubtype));
    }

    private static string? NormalizeSubtype(string? subtype)
    {
        if (string.IsNullOrEmpty(subtype))
            return null;

        return subtype.ToLowerInvariant() switch
        {
            "definition" => "Definition",
            "howto" or "how-to" => "HowTo",
            "compare" or "comparison" => "Compare",
            "troubleshoot" => "Troubleshoot",
            "clarification" => "Clarification",
            "yesno" or "yes/no" => "YesNo",
            "opinion" => "Opinion",
            _ => subtype
        };
    }
}

/// <summary>
/// Result of evaluating a single dataset item.
/// </summary>
public sealed record DatasetItemResult(
    string Text,
    string ActualType,
    string? ActualSubtype,
    string PredictedType,
    string? PredictedSubtype,
    double Confidence,
    bool TypeCorrect,
    bool SubtypeCorrect);

/// <summary>
/// Complete result of dataset evaluation.
/// </summary>
public sealed record DatasetEvaluationResult(
    string DatasetPath,
    int TotalItems,
    int CorrectType,
    double TypeAccuracy,
    double QuestionPrecision,
    double QuestionRecall,
    double QuestionF1,
    double SubtypeAccuracy,
    ConfusionMatrix ConfusionMatrix,
    IReadOnlyList<DatasetItemResult> ItemResults);
