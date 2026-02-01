using System.Text;
using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.Pipeline.Detection;

/// <summary>
/// LLM-based intent detection strategy with context buffer.
/// Higher accuracy (~95% recall) but has latency and cost.
/// </summary>
public sealed class LlmIntentStrategy : IIntentDetectionStrategy
{
    private readonly ILlmIntentDetector _llm;
    private readonly LlmDetectionOptions _options;

    private readonly StringBuilder _buffer = new();
    private readonly List<PendingUtterance> _pendingUtterances = new();
    private readonly HashSet<string> _detectedFingerprints = new();
    private readonly Dictionary<string, DateTime> _detectionTimes = new();
    private readonly object _lock = new();

    private DateTime _lastDetection = DateTime.MinValue;
    private DateTime _lastBufferChange = DateTime.MinValue;
    private bool _hasTrigger;
    private CancellationTokenSource? _timeoutCts;

    public string ModeName => "LLM";

    public event Action<IntentEvent>? OnIntentDetected;
    public event Action<IntentCorrectionEvent>? OnIntentCorrected;

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

        lock (_lock)
        {
            // Add to buffer
            if (_buffer.Length > 0)
                _buffer.Append(' ');
            _buffer.Append(text);
            _lastBufferChange = DateTime.UtcNow;

            // Track pending utterance
            _pendingUtterances.Add(new PendingUtterance(utterance.Id, text, DateTime.UtcNow));

            // Trim buffer if too long
            if (_buffer.Length > _options.BufferMaxChars)
            {
                var excess = _buffer.Length - _options.BufferMaxChars;
                _buffer.Remove(0, excess);

                // Remove old pending utterances
                while (_pendingUtterances.Count > 0 &&
                       !_buffer.ToString().Contains(_pendingUtterances[0].Text))
                {
                    _pendingUtterances.RemoveAt(0);
                }
            }

            // Check for trigger conditions
            if (_options.TriggerOnQuestionMark && text.Contains('?'))
            {
                _hasTrigger = true;
            }
        }

        // Start timeout timer
        ResetTimeoutTimer(ct);

        // Try to detect if we have a trigger
        if (_hasTrigger)
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
                    if (_buffer.Length > 0)
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
        string bufferText;
        List<PendingUtterance> pending;

        lock (_lock)
        {
            if (!_hasTrigger)
                return;

            // Rate limiting
            var elapsed = DateTime.UtcNow - _lastDetection;
            if (elapsed.TotalMilliseconds < _options.RateLimitMs)
                return;

            if (_buffer.Length == 0)
                return;

            bufferText = _buffer.ToString();
            pending = _pendingUtterances.ToList();
            _hasTrigger = false;
            _lastDetection = DateTime.UtcNow;
        }

        // Call LLM
        var detectedIntents = await _llm.DetectIntentsAsync(bufferText, null, ct);

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

            // Find the best matching utterance ID
            var utteranceId = FindBestMatchingUtterance(intent.SourceText, pending);

            var intentEvent = new IntentEvent
            {
                Intent = intent,
                UtteranceId = utteranceId ?? pending.LastOrDefault()?.Id ?? "unknown"
            };

            OnIntentDetected?.Invoke(intentEvent);
        }

        // Clear processed utterances from buffer
        lock (_lock)
        {
            if (detectedIntents.Count > 0)
            {
                // Keep only recent unprocessed content
                var cutoff = DateTime.UtcNow.AddMilliseconds(-_options.RateLimitMs);
                _pendingUtterances.RemoveAll(p => p.AddedAt < cutoff);

                // Rebuild buffer from remaining utterances
                _buffer.Clear();
                foreach (var p in _pendingUtterances)
                {
                    if (_buffer.Length > 0)
                        _buffer.Append(' ');
                    _buffer.Append(p.Text);
                }
            }
        }
    }

    private string? FindBestMatchingUtterance(string intentText, List<PendingUtterance> pending)
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
        _timeoutCts?.Cancel();
        _timeoutCts?.Dispose();
    }

    private record PendingUtterance(string Id, string Text, DateTime AddedAt);
}
