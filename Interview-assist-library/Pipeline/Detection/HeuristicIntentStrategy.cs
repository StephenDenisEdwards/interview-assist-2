using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Heuristic-based intent detection strategy using regex patterns.
/// Fast and free, but lower accuracy (~67% recall).
/// </summary>
public sealed class HeuristicIntentStrategy : IIntentDetectionStrategy
{
    private readonly IIntentDetector _detector;
    private readonly HeuristicDetectionOptions _options;

    public string ModeName => "Heuristic";

    public event Action<IntentEvent>? OnIntentDetected;
#pragma warning disable CS0067 // Event required by interface but not used in heuristic-only strategy
    public event Action<IntentCorrectionEvent>? OnIntentCorrected;
#pragma warning restore CS0067

    public HeuristicIntentStrategy(HeuristicDetectionOptions? options = null)
    {
        _detector = new IntentDetector();
        _options = options ?? new HeuristicDetectionOptions();
    }

    public Task ProcessUtteranceAsync(UtteranceEvent utterance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(utterance.StableText))
            return Task.CompletedTask;

        var intent = _detector.DetectFinal(utterance.StableText);

        // Filter by confidence threshold
        if (intent.Confidence < _options.MinConfidence)
        {
            intent = new DetectedIntent
            {
                Type = IntentType.Statement,
                Confidence = 0.4,
                SourceText = utterance.StableText
            };
        }

        var intentEvent = new IntentEvent
        {
            Intent = intent,
            UtteranceId = utterance.Id
        };

        OnIntentDetected?.Invoke(intentEvent);

        return Task.CompletedTask;
    }

    public void SignalPause()
    {
        // Heuristic strategy doesn't use pause signals
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
