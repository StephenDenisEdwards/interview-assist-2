using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// LLM-based intent detection strategy with sliding context window.
/// Separates unprocessed utterances (for classification) from processed context (for pronoun resolution).
/// </summary>
public sealed class LlmIntentStrategy : IIntentDetectionStrategy
{
    private readonly ILlmIntentDetector _llm;
    private readonly LlmDetectionOptions _options;

    private readonly List<TrackedUtterance> _unprocessedUtterances = new();
    private readonly List<TrackedUtterance> _contextWindow = new();
    private readonly HashSet<string> _detectedFingerprints = new();
    private readonly Dictionary<string, DateTime> _detectionTimes = new();
    private readonly object _lock = new();

    private DateTime _lastDetection = DateTime.MinValue;
    private DateTime _lastBufferChange = DateTime.MinValue;
    private bool _hasTrigger;
    private CancellationTokenSource? _timeoutCts;

    public string ModeName => "LLM";

    public event Action<IntentEvent>? OnIntentDetected;
#pragma warning disable CS0067 // Event required by interface but not used in LLM-only strategy
    public event Action<IntentCorrectionEvent>? OnIntentCorrected;
#pragma warning restore CS0067

    public LlmIntentStrategy(ILlmIntentDetector llm, LlmDetectionOptions? options = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _options = options ?? new LlmDetectionOptions();
    }

    public async Task ProcessUtteranceAsync(UtteranceEvent utterance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(utterance.StableText))
            return;

        var text = utterance.StableText;

        // Preprocess if enabled
        if (_options.EnablePreprocessing)
        {
            text = TranscriptionPreprocessor.Preprocess(text);
            if (string.IsNullOrWhiteSpace(text))
                return;
        }

        bool forceDetect = false;

        lock (_lock)
        {
            _unprocessedUtterances.Add(new TrackedUtterance(utterance.Id, text, DateTime.UtcNow));
            _lastBufferChange = DateTime.UtcNow;

            // Check for trigger conditions
            if (_options.TriggerOnQuestionMark && text.Contains('?'))
            {
                _hasTrigger = true;
            }

            // Force detection if unprocessed text exceeds buffer limit
            var unprocessedChars = GetTotalChars(_unprocessedUtterances);
            if (unprocessedChars > _options.BufferMaxChars)
            {
                _hasTrigger = true;
                forceDetect = true;
            }
        }

        // Start timeout timer
        ResetTimeoutTimer(ct);

        // Try to detect if we have a trigger
        if (_hasTrigger || forceDetect)
        {
            await TryDetectAsync(ct);
        }
    }

    public void SignalPause()
    {
        if (!_options.TriggerOnPause)
            return;

        lock (_lock)
        {
            _hasTrigger = true;
        }

        // Fire and forget detection on pause
        _ = Task.Run(async () =>
        {
            try
            {
                await TryDetectAsync(CancellationToken.None);
            }
            catch
            {
                // Ignore errors in fire-and-forget
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
                await Task.Delay(_options.TriggerTimeoutMs, cts.Token);

                lock (_lock)
                {
                    if (_unprocessedUtterances.Count > 0)
                        _hasTrigger = true;
                }

                await TryDetectAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timer was reset
            }
        }, cts.Token);
    }

    private async Task TryDetectAsync(CancellationToken ct)
    {
        string newText;
        string? contextText;
        List<TrackedUtterance> unprocessedSnapshot;

        lock (_lock)
        {
            if (!_hasTrigger)
                return;

            // Rate limiting
            var elapsed = DateTime.UtcNow - _lastDetection;
            if (elapsed.TotalMilliseconds < _options.RateLimitMs)
                return;

            if (_unprocessedUtterances.Count == 0)
                return;

            // Build context from already-processed utterances (labeled)
            contextText = _contextWindow.Count > 0
                ? TranscriptionPreprocessor.FormatLabeledUtterances(
                    _contextWindow.Select(u => (u.Id, u.Text)).ToList())
                : null;

            // Build new text from unprocessed utterances (labeled)
            newText = TranscriptionPreprocessor.FormatLabeledUtterances(
                _unprocessedUtterances.Select(u => (u.Id, u.Text)).ToList());
            unprocessedSnapshot = _unprocessedUtterances.ToList();

            _hasTrigger = false;
            _lastDetection = DateTime.UtcNow;
        }

        // Call LLM with new text and context
        var detectedIntents = await _llm.DetectIntentsAsync(newText, contextText, ct);

        // Process results
        foreach (var intent in detectedIntents)
        {
            // Deduplication
            if (_options.EnableDeduplication)
            {
                var fingerprint = TranscriptionPreprocessor.GetSemanticFingerprint(intent.SourceText);

                lock (_lock)
                {
                    // Check time-based suppression
                    if (_detectionTimes.TryGetValue(fingerprint, out var lastTime))
                    {
                        if ((DateTime.UtcNow - lastTime).TotalMilliseconds < _options.DeduplicationWindowMs)
                            continue;
                    }

                    // Check similarity to already detected
                    var isDuplicate = _detectedFingerprints.Any(existing =>
                        TranscriptionPreprocessor.IsSimilar(fingerprint, existing));

                    if (isDuplicate)
                        continue;

                    _detectedFingerprints.Add(fingerprint);
                    _detectionTimes[fingerprint] = DateTime.UtcNow;

                    // Cleanup old entries
                    CleanupOldDetections();
                }
            }

            // Prefer UtteranceId from LLM response for direct lookup; fall back to word-overlap
            var utteranceId = intent.UtteranceId != null
                ? unprocessedSnapshot.FirstOrDefault(u => u.Id == intent.UtteranceId)?.Id
                : null;
            utteranceId ??= FindBestMatchingUtterance(intent.SourceText, unprocessedSnapshot);

            // Override OriginalText with the matched utterance's text (direct from pipeline, not LLM)
            var matchedUtterance = unprocessedSnapshot.FirstOrDefault(u => u.Id == utteranceId);
            var intentWithOriginal = intent with
            {
                OriginalText = matchedUtterance?.Text ?? intent.OriginalText
            };

            var intentEvent = new IntentEvent
            {
                Intent = intentWithOriginal,
                UtteranceId = utteranceId ?? unprocessedSnapshot.LastOrDefault()?.Id ?? "unknown"
            };

            OnIntentDetected?.Invoke(intentEvent);
        }

        // Move unprocessed → context window, trim context
        lock (_lock)
        {
            // Move all unprocessed utterances to context window
            _contextWindow.AddRange(_unprocessedUtterances);
            _unprocessedUtterances.Clear();

            // Trim context window to ContextWindowChars limit (FIFO)
            TrimContextWindow();
        }
    }

    private void TrimContextWindow()
    {
        var totalChars = GetTotalChars(_contextWindow);
        while (_contextWindow.Count > 0 && totalChars > _options.ContextWindowChars)
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

    private string? FindBestMatchingUtterance(string intentText, List<TrackedUtterance> pending)
    {
        // Find utterance that best matches the detected intent text
        string? bestMatch = null;
        int bestScore = 0;

        foreach (var p in pending)
        {
            var words = TranscriptionPreprocessor.GetSignificantWords(intentText);
            var utteranceWords = TranscriptionPreprocessor.GetSignificantWords(p.Text);
            var overlap = words.Intersect(utteranceWords).Count();

            if (overlap > bestScore)
            {
                bestScore = overlap;
                bestMatch = p.Id;
            }
        }

        return bestMatch;
    }

    private void CleanupOldDetections()
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-_options.DeduplicationWindowMs * 2);
        var keysToRemove = _detectionTimes
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _detectionTimes.Remove(key);
            _detectedFingerprints.Remove(key);
        }

        // Keep fingerprints bounded
        while (_detectedFingerprints.Count > 50)
        {
            var oldest = _detectionTimes.OrderBy(kvp => kvp.Value).First().Key;
            _detectionTimes.Remove(oldest);
            _detectedFingerprints.Remove(oldest);
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

    private record TrackedUtterance(string Id, string Text, DateTime AddedAt);
}
