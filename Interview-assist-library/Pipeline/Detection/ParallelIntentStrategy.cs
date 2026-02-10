using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// Parallel intent detection: heuristic for immediate response, LLM for verification.
/// Best UX with fast feedback and corrections from LLM.
/// </summary>
public sealed class ParallelIntentStrategy : IIntentDetectionStrategy
{
    private readonly IIntentDetector _heuristic;
    private readonly ILlmIntentDetector _llm;
    private readonly HeuristicDetectionOptions _heuristicOptions;
    private readonly LlmDetectionOptions _llmOptions;

    private readonly List<TrackedUtterance> _unprocessedUtterances = new();
    private readonly List<TrackedUtterance> _contextWindow = new();
    private readonly Dictionary<string, EmittedIntent> _emittedIntents = new();
    private readonly HashSet<string> _detectedFingerprints = new();
    private readonly Dictionary<string, DateTime> _detectionTimes = new();
    private readonly object _lock = new();

    private DateTime _lastLlmDetection = DateTime.MinValue;
    private bool _hasTrigger;
    private CancellationTokenSource? _timeoutCts;

    public string ModeName => "Parallel";

    public event Action<IntentEvent>? OnIntentDetected;
    public event Action<IntentCorrectionEvent>? OnIntentCorrected;

    public ParallelIntentStrategy(
        ILlmIntentDetector llm,
        HeuristicDetectionOptions? heuristicOptions = null,
        LlmDetectionOptions? llmOptions = null)
    {
        _heuristic = new IntentDetector();
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _heuristicOptions = heuristicOptions ?? new HeuristicDetectionOptions();
        _llmOptions = llmOptions ?? new LlmDetectionOptions();
    }

    public async Task ProcessUtteranceAsync(UtteranceEvent utterance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(utterance.StableText))
            return;

        // Phase 1: Immediate heuristic detection
        var heuristicIntent = _heuristic.DetectFinal(utterance.StableText);

        if (heuristicIntent.Confidence >= _heuristicOptions.MinConfidence)
        {
            var intentEvent = new IntentEvent
            {
                Intent = heuristicIntent,
                UtteranceId = utterance.Id
            };

            lock (_lock)
            {
                _emittedIntents[utterance.Id] = new EmittedIntent(
                    utterance.Id,
                    heuristicIntent,
                    DateTime.UtcNow,
                    IsFromHeuristic: true);
            }

            OnIntentDetected?.Invoke(intentEvent);
        }

        // Phase 2: Queue for LLM verification
        var text = utterance.StableText;
        if (_llmOptions.EnablePreprocessing)
        {
            text = TranscriptionPreprocessor.Preprocess(text);
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        bool forceDetect = false;

        lock (_lock)
        {
            _unprocessedUtterances.Add(new TrackedUtterance(
                utterance.Id,
                text,
                DateTime.UtcNow,
                heuristicIntent));

            // Check for trigger
            if (_llmOptions.TriggerOnQuestionMark && text.Contains('?'))
            {
                _hasTrigger = true;
            }

            // Force detection if unprocessed text exceeds buffer limit
            var unprocessedChars = GetTotalChars(_unprocessedUtterances);
            if (unprocessedChars > _llmOptions.BufferMaxChars)
            {
                _hasTrigger = true;
                forceDetect = true;
            }
        }

        // Start timeout timer
        ResetTimeoutTimer(ct);

        // Try LLM detection if triggered
        if (_hasTrigger || forceDetect)
        {
            await TryLlmDetectAsync(ct);
        }
    }

    public void SignalPause()
    {
        if (!_llmOptions.TriggerOnPause)
            return;

        lock (_lock)
        {
            _hasTrigger = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await TryLlmDetectAsync(CancellationToken.None);
            }
            catch
            {
                // Ignore
            }
        });
    }

    private void ResetTimeoutTimer(CancellationToken ct)
    {
        _timeoutCts?.Cancel();
        _timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var cts = _timeoutCts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_llmOptions.TriggerTimeoutMs, cts.Token);

                lock (_lock)
                {
                    if (_unprocessedUtterances.Count > 0)
                        _hasTrigger = true;
                }

                await TryLlmDetectAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timer reset
            }
        }, cts.Token);
    }

    private async Task TryLlmDetectAsync(CancellationToken ct)
    {
        string newText;
        string? contextText;
        List<TrackedUtterance> unprocessedSnapshot;

        lock (_lock)
        {
            if (!_hasTrigger)
                return;

            var elapsed = DateTime.UtcNow - _lastLlmDetection;
            if (elapsed.TotalMilliseconds < _llmOptions.RateLimitMs)
                return;

            if (_unprocessedUtterances.Count == 0)
                return;

            // Build context from already-processed utterances
            contextText = _contextWindow.Count > 0
                ? string.Join(" ", _contextWindow.Select(u => u.Text))
                : null;

            // Build new text from unprocessed utterances
            newText = string.Join(" ", _unprocessedUtterances.Select(u => u.Text));
            unprocessedSnapshot = _unprocessedUtterances.ToList();

            _hasTrigger = false;
            _lastLlmDetection = DateTime.UtcNow;
        }

        // Call LLM with new text and context
        var llmIntents = await _llm.DetectIntentsAsync(newText, contextText, ct);

        // Process LLM results and compare with heuristic
        ProcessLlmResults(llmIntents, unprocessedSnapshot);

        // Move unprocessed → context window, trim context
        lock (_lock)
        {
            _contextWindow.AddRange(_unprocessedUtterances);
            _unprocessedUtterances.Clear();

            TrimContextWindow();
        }
    }

    private void ProcessLlmResults(IReadOnlyList<DetectedIntent> llmIntents, List<TrackedUtterance> pending)
    {
        var matchedUtterances = new HashSet<string>();

        foreach (var llmIntent in llmIntents)
        {
            // Deduplication check
            if (_llmOptions.EnableDeduplication)
            {
                var fingerprint = TranscriptionPreprocessor.GetSemanticFingerprint(llmIntent.SourceText);

                lock (_lock)
                {
                    if (_detectionTimes.TryGetValue(fingerprint, out var lastTime))
                    {
                        if ((DateTime.UtcNow - lastTime).TotalMilliseconds < _llmOptions.DeduplicationWindowMs)
                            continue;
                    }

                    var isDuplicate = _detectedFingerprints.Any(existing =>
                        TranscriptionPreprocessor.IsSimilar(fingerprint, existing));

                    if (isDuplicate)
                        continue;

                    _detectedFingerprints.Add(fingerprint);
                    _detectionTimes[fingerprint] = DateTime.UtcNow;
                    CleanupOldDetections();
                }
            }

            // Find matching utterance
            var matchedUtterance = FindBestMatchingUtterance(llmIntent.SourceText, pending);
            var utteranceId = matchedUtterance?.Id ?? pending.LastOrDefault()?.Id ?? "unknown";

            // Override OriginalText with the matched utterance's text (direct from pipeline, not LLM)
            var llmIntentWithOriginal = llmIntent with
            {
                OriginalText = matchedUtterance?.Text ?? llmIntent.OriginalText
            };

            if (matchedUtterance != null)
                matchedUtterances.Add(matchedUtterance.Id);

            // Check if heuristic already emitted an intent for this utterance
            EmittedIntent? previousEmission;
            lock (_lock)
            {
                _emittedIntents.TryGetValue(utteranceId, out previousEmission);
            }

            if (previousEmission != null)
            {
                // Compare LLM result with heuristic result
                var correctionType = DetermineCorrectionType(previousEmission.Intent, llmIntentWithOriginal);

                if (correctionType == IntentCorrectionType.Confirmed)
                {
                    // LLM confirmed heuristic - no action needed, just log
                    continue;
                }

                // Emit correction
                OnIntentCorrected?.Invoke(new IntentCorrectionEvent
                {
                    UtteranceId = utteranceId,
                    OriginalIntent = previousEmission.Intent,
                    CorrectedIntent = llmIntentWithOriginal,
                    CorrectionType = correctionType
                });
            }
            else
            {
                // LLM found something heuristic missed
                var intentEvent = new IntentEvent
                {
                    Intent = llmIntentWithOriginal,
                    UtteranceId = utteranceId
                };

                OnIntentDetected?.Invoke(intentEvent);

                // Also emit as correction (Added type)
                OnIntentCorrected?.Invoke(new IntentCorrectionEvent
                {
                    UtteranceId = utteranceId,
                    OriginalIntent = null,
                    CorrectedIntent = llmIntentWithOriginal,
                    CorrectionType = IntentCorrectionType.Added
                });
            }
        }

        // Check for heuristic detections that LLM didn't confirm (potential false positives)
        foreach (var p in pending.Where(p => !matchedUtterances.Contains(p.Id)))
        {
            EmittedIntent? emission;
            lock (_lock)
            {
                _emittedIntents.TryGetValue(p.Id, out emission);
            }

            if (emission != null &&
                emission.IsFromHeuristic &&
                emission.Intent.Type != IntentType.Statement &&
                emission.Intent.Confidence < 0.9)
            {
                // Heuristic detected something with moderate confidence that LLM didn't confirm
                // This could be a false positive, but we don't remove it to avoid confusion
                // Just note it in corrections
                OnIntentCorrected?.Invoke(new IntentCorrectionEvent
                {
                    UtteranceId = p.Id,
                    OriginalIntent = emission.Intent,
                    CorrectedIntent = new DetectedIntent
                    {
                        Type = IntentType.Statement,
                        Confidence = 0.5,
                        SourceText = emission.Intent.SourceText,
                        OriginalText = emission.Intent.OriginalText
                    },
                    CorrectionType = IntentCorrectionType.Removed
                });
            }
        }
    }

    private static IntentCorrectionType DetermineCorrectionType(DetectedIntent original, DetectedIntent corrected)
    {
        // Same type = confirmed
        if (original.Type == corrected.Type)
            return IntentCorrectionType.Confirmed;

        // Different type = type changed
        return IntentCorrectionType.TypeChanged;
    }

    private TrackedUtterance? FindBestMatchingUtterance(string intentText, List<TrackedUtterance> pending)
    {
        TrackedUtterance? bestMatch = null;
        int bestScore = 0;

        var intentWords = TranscriptionPreprocessor.GetSignificantWords(intentText);

        foreach (var p in pending)
        {
            var utteranceWords = TranscriptionPreprocessor.GetSignificantWords(p.Text);
            var overlap = intentWords.Intersect(utteranceWords).Count();

            if (overlap > bestScore)
            {
                bestScore = overlap;
                bestMatch = p;
            }
        }

        return bestMatch;
    }

    private void TrimContextWindow()
    {
        var totalChars = GetTotalChars(_contextWindow);
        while (_contextWindow.Count > 0 && totalChars > _llmOptions.ContextWindowChars)
        {
            totalChars -= _contextWindow[0].Text.Length;
            _contextWindow.RemoveAt(0);
        }
    }

    private static int GetTotalChars(List<TrackedUtterance> utterances)
    {
        var total = 0;
        foreach (var u in utterances)
            total += u.Text.Length;
        return total;
    }

    private void CleanupOldDetections()
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-_llmOptions.DeduplicationWindowMs * 2);

        var keysToRemove = _detectionTimes
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _detectionTimes.Remove(key);
            _detectedFingerprints.Remove(key);
        }

        // Also cleanup emitted intents
        var emittedToRemove = _emittedIntents
            .Where(kvp => kvp.Value.EmittedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in emittedToRemove)
        {
            _emittedIntents.Remove(key);
        }
    }

    public void Dispose()
    {
        try
        {
            _timeoutCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by ResetTimeoutTimer
        }
        _timeoutCts?.Dispose();
    }

    private record TrackedUtterance(string Id, string Text, DateTime AddedAt, DetectedIntent HeuristicIntent);
    private record EmittedIntent(string UtteranceId, DetectedIntent Intent, DateTime EmittedAt, bool IsFromHeuristic);
}
