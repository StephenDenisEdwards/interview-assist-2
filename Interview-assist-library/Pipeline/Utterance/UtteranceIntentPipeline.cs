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

    public UtteranceIntentPipeline(
        PipelineOptions? options = null,
        IUtteranceBuilder? utteranceBuilder = null,
        IIntentDetector? intentDetector = null,
        IActionRouter? actionRouter = null)
    {
        options ??= PipelineOptions.Default;

        _utteranceBuilder = utteranceBuilder ?? new UtteranceBuilder(options);
        _intentDetector = intentDetector ?? new IntentDetector();
        _actionRouter = actionRouter ?? new ActionRouter(options);

        // Wire internal events
        WireEvents();

        // Start timeout checking timer (100ms interval)
        _timeoutTimer = new Timer(_ => _utteranceBuilder.CheckTimeouts(), null, 100, 100);

        // Start conflict resolution timer (100ms interval)
        if (_actionRouter is ActionRouter router)
        {
            _conflictTimer = new Timer(_ => router.CheckConflictWindow(), null, 100, 100);
        }
    }

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

            // Candidate intent detection on update
            var candidate = _intentDetector.DetectCandidate(evt.StableText);
            if (candidate != null)
            {
                OnIntentCandidate?.Invoke(new IntentEvent
                {
                    Intent = candidate,
                    UtteranceId = evt.Id,
                    IsCandidate = true
                });
            }
        };

        _utteranceBuilder.OnUtteranceFinal += evt =>
        {
            OnUtteranceFinal?.Invoke(evt);

            // Final intent detection
            var finalIntent = _intentDetector.DetectFinal(evt.StableText);

            OnIntentFinal?.Invoke(new IntentEvent
            {
                Intent = finalIntent,
                UtteranceId = evt.Id,
                IsCandidate = false
            });

            // Route to action if applicable
            _actionRouter.Route(finalIntent, evt.Id);
        };

        // Action events
        _actionRouter.OnActionTriggered += evt =>
        {
            OnActionTriggered?.Invoke(evt);
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timeoutTimer?.Dispose();
        _conflictTimer?.Dispose();
    }
}
