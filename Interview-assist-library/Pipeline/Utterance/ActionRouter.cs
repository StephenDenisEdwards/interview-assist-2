using System.Collections.Concurrent;

namespace InterviewAssist.Library.Pipeline.Utterance;

/// <summary>
/// Routes intents to action handlers with debouncing and conflict resolution.
/// Thread-safe implementation using concurrent collections.
/// </summary>
public sealed class ActionRouter : IActionRouter
{
    private readonly PipelineOptions _options;
    private readonly CooldownConfig _cooldownConfig;
    private readonly Func<DateTime> _clock;

    private readonly ConcurrentDictionary<IntentSubtype, DateTime> _lastFiredTimes = new();
    private readonly ConcurrentDictionary<IntentSubtype, Action<DetectedIntent>> _handlers = new();

    // Conflict resolution state
    private readonly object _conflictLock = new();
    private PendingIntent? _pendingIntent;
    private DateTime _conflictWindowStart;

    public event Action<ActionEvent>? OnActionTriggered;

    public ActionRouter(
        PipelineOptions? options = null,
        CooldownConfig? cooldownConfig = null,
        Func<DateTime>? clock = null)
    {
        _options = options ?? PipelineOptions.Default;
        _cooldownConfig = cooldownConfig ?? CooldownConfig.Default;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public bool Route(DetectedIntent intent, string utteranceId)
    {
        if (intent.Type != IntentType.Imperative || intent.Subtype == null)
        {
            // Only route imperatives to actions
            // Questions and statements don't trigger actions here
            return false;
        }

        var subtype = intent.Subtype.Value;
        var now = _clock();

        // Check cooldown
        if (IsOnCooldown(subtype, now))
        {
            OnActionTriggered?.Invoke(new ActionEvent
            {
                ActionName = GetActionName(subtype),
                Intent = intent,
                UtteranceId = utteranceId,
                Timestamp = now,
                WasDebounced = true
            });
            return false;
        }

        // Conflict resolution: last-wins within window
        lock (_conflictLock)
        {
            if (_pendingIntent != null)
            {
                var timeSinceConflictStart = now - _conflictWindowStart;
                if (timeSinceConflictStart < _options.ConflictWindow)
                {
                    // Within conflict window - replace pending intent (last-wins)
                    _pendingIntent = new PendingIntent(intent, utteranceId, now);
                    return true; // Will be fired when window closes
                }
                else
                {
                    // Window expired - fire pending and continue with new
                    FirePendingIntent();
                }
            }
        }

        // Fire immediately and start conflict window for subsequent intents
        lock (_conflictLock)
        {
            _conflictWindowStart = now;
        }
        return FireIntent(intent, utteranceId, now);
    }

    /// <summary>
    /// Check and resolve any pending conflicts. Call periodically.
    /// </summary>
    public void CheckConflictWindow()
    {
        var now = _clock();

        lock (_conflictLock)
        {
            if (_pendingIntent != null)
            {
                var timeSinceConflictStart = now - _conflictWindowStart;
                if (timeSinceConflictStart >= _options.ConflictWindow)
                {
                    FirePendingIntent();
                }
            }
        }
    }

    public void RegisterHandler(IntentSubtype subtype, Action<DetectedIntent> handler)
    {
        _handlers[subtype] = handler;
    }

    public void Reset()
    {
        _lastFiredTimes.Clear();
        lock (_conflictLock)
        {
            _pendingIntent = null;
        }
    }

    private bool FireIntent(DetectedIntent intent, string utteranceId, DateTime now)
    {
        var subtype = intent.Subtype!.Value;

        // Update last fired time
        _lastFiredTimes[subtype] = now;

        // Invoke handler if registered
        if (_handlers.TryGetValue(subtype, out var handler))
        {
            try
            {
                handler(intent);
            }
            catch
            {
                // Don't let handler exceptions break the pipeline
            }
        }

        // Emit event
        OnActionTriggered?.Invoke(new ActionEvent
        {
            ActionName = GetActionName(subtype),
            Intent = intent,
            UtteranceId = utteranceId,
            Timestamp = now,
            WasDebounced = false
        });

        return true;
    }

    private void FirePendingIntent()
    {
        if (_pendingIntent == null) return;

        var pending = _pendingIntent;
        _pendingIntent = null;

        FireIntent(pending.Intent, pending.UtteranceId, pending.Timestamp);
    }

    private bool IsOnCooldown(IntentSubtype subtype, DateTime now)
    {
        if (!_lastFiredTimes.TryGetValue(subtype, out var lastFired))
            return false;

        var cooldown = _cooldownConfig.GetCooldown(subtype);
        var elapsed = now - lastFired;

        return elapsed < cooldown;
    }

    private static bool IsConflictProne(IntentSubtype subtype)
    {
        // These commands are likely to be corrected by the user
        return subtype is IntentSubtype.Stop
            or IntentSubtype.Continue
            or IntentSubtype.Repeat;
    }

    private static string GetActionName(IntentSubtype subtype)
    {
        return subtype switch
        {
            IntentSubtype.Stop => "stop",
            IntentSubtype.Repeat => "repeat",
            IntentSubtype.Continue => "continue",
            IntentSubtype.StartOver => "start_over",
            IntentSubtype.Generate => "generate_questions",
            _ => subtype.ToString().ToLowerInvariant()
        };
    }

    private sealed record PendingIntent(DetectedIntent Intent, string UtteranceId, DateTime Timestamp);
}
