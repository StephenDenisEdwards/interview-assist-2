using InterviewAssist.Library.Pipeline.Detection;

namespace InterviewAssist.Library.Pipeline.Utterance;

/// <summary>
/// Orchestrates the full ASR → Utterance → Intent → Action pipeline.
/// Wires components together and manages event flow.
/// </summary>
public sealed class UtteranceIntentPipeline : IDisposable
{
    private readonly IUtteranceBuilder _utteranceBuilder;
    private readonly IIntentDetector _intentDetector;
    private readonly IActionRouter _actionRouter;
    private readonly IIntentDetectionStrategy? _detectionStrategy;
    private readonly Timer? _timeoutTimer;
    private readonly Timer? _conflictTimer;

    private bool _disposed;

    // Pipeline events for external consumers
    public event Action<AsrEvent>? OnAsrPartial;
    public event Action<AsrEvent>? OnAsrFinal;
    public event Action<UtteranceEvent>? OnUtteranceOpen;
    public event Action<UtteranceEvent>? OnUtteranceUpdate;
    public event Action<UtteranceEvent>? OnUtteranceFinal;
    public event Action<IntentEvent>? OnIntentCandidate;
    public event Action<IntentEvent>? OnIntentFinal;
    public event Action<ActionEvent>? OnActionTriggered;
    public event Action<IntentCorrectionEvent>? OnIntentCorrected;

    public UtteranceIntentPipeline(
        PipelineOptions? options = null,
        IUtteranceBuilder? utteranceBuilder = null,
        IIntentDetector? intentDetector = null,
        IActionRouter? actionRouter = null,
        IIntentDetectionStrategy? detectionStrategy = null)
    {
        options ??= PipelineOptions.Default;

        _utteranceBuilder = utteranceBuilder ?? new UtteranceBuilder(options);
        _intentDetector = intentDetector ?? new IntentDetector();
        _actionRouter = actionRouter ?? new ActionRouter(options);
        _detectionStrategy = detectionStrategy;

        // Wire internal events
        WireEvents();

        // Wire detection strategy events if provided
        if (_detectionStrategy != null)
        {
            WireDetectionStrategyEvents();
        }

        // Start timeout checking timer (100ms interval)
        _timeoutTimer = new Timer(_ => _utteranceBuilder.CheckTimeouts(), null, 100, 100);

        // Start conflict resolution timer (100ms interval)
        if (_actionRouter is ActionRouter router)
        {
            _conflictTimer = new Timer(_ => router.CheckConflictWindow(), null, 100, 100);
        }
    }

    /// <summary>
    /// The detection mode name (Heuristic, LLM, or Parallel).
    /// </summary>
    public string DetectionModeName => _detectionStrategy?.ModeName ?? "Heuristic";

    /// <summary>
    /// Process an incoming ASR event from Deepgram or other source.
    /// </summary>
    public void ProcessAsrEvent(AsrEvent evt)
    {
        if (_disposed) return;

        // Emit ASR event
        if (evt.IsFinal)
        {
            OnAsrFinal?.Invoke(evt);
        }
        else
        {
            OnAsrPartial?.Invoke(evt);
        }

        // Forward to utterance builder
        _utteranceBuilder.ProcessAsrEvent(evt);
    }

    /// <summary>
    /// Signal utterance end from Deepgram.
    /// </summary>
    public void SignalUtteranceEnd()
    {
        if (_disposed) return;
        _utteranceBuilder.SignalUtteranceEnd();
        _detectionStrategy?.SignalPause();
    }

    /// <summary>
    /// Force close current utterance (e.g., user interrupt).
    /// </summary>
    public void ForceClose()
    {
        if (_disposed) return;
        _utteranceBuilder.ForceClose();
    }

    /// <summary>
    /// Register an action handler for a specific intent subtype.
    /// </summary>
    public void RegisterActionHandler(IntentSubtype subtype, Action<DetectedIntent> handler)
    {
        _actionRouter.RegisterHandler(subtype, handler);
    }

    /// <summary>
    /// Access the underlying components for testing/customization.
    /// </summary>
    public IUtteranceBuilder UtteranceBuilder => _utteranceBuilder;
    public IIntentDetector IntentDetector => _intentDetector;
    public IActionRouter ActionRouter => _actionRouter;
    public IIntentDetectionStrategy? DetectionStrategy => _detectionStrategy;

    private void WireEvents()
    {
        // Utterance events
        _utteranceBuilder.OnUtteranceOpen += evt =>
        {
            OnUtteranceOpen?.Invoke(evt);
        };

        _utteranceBuilder.OnUtteranceUpdate += evt =>
        {
            OnUtteranceUpdate?.Invoke(evt);

            // Candidate intent detection on update (heuristic only - for immediate feedback)
            var candidate = _intentDetector.DetectCandidate(evt.StableText);
            if (candidate != null)
            {
                OnIntentCandidate?.Invoke(new IntentEvent
                {
                    Intent = candidate with { OriginalText = evt.StableText },
                    UtteranceId = evt.Id,
                    IsCandidate = true
                });
            }
        };

        _utteranceBuilder.OnUtteranceFinal += evt =>
        {
            OnUtteranceFinal?.Invoke(evt);

            // If using a detection strategy, delegate to it
            if (_detectionStrategy != null)
            {
                // Strategy will fire OnIntentDetected events asynchronously
                _ = _detectionStrategy.ProcessUtteranceAsync(evt);
            }
            else
            {
                // Legacy behavior: synchronous heuristic detection
                var finalIntent = _intentDetector.DetectFinal(evt.StableText);

                OnIntentFinal?.Invoke(new IntentEvent
                {
                    Intent = finalIntent,
                    UtteranceId = evt.Id,
                    IsCandidate = false
                });

                // Route to action if applicable
                _actionRouter.Route(finalIntent, evt.Id);
            }
        };

        // Action events
        _actionRouter.OnActionTriggered += evt =>
        {
            OnActionTriggered?.Invoke(evt);
        };
    }

    private void WireDetectionStrategyEvents()
    {
        if (_detectionStrategy == null) return;

        _detectionStrategy.OnIntentDetected += evt =>
        {
            OnIntentFinal?.Invoke(evt);

            // Route to action if applicable
            _actionRouter.Route(evt.Intent, evt.UtteranceId);
        };

        _detectionStrategy.OnIntentCorrected += evt =>
        {
            OnIntentCorrected?.Invoke(evt);
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timeoutTimer?.Dispose();
        _conflictTimer?.Dispose();
        _detectionStrategy?.Dispose();
    }
}
