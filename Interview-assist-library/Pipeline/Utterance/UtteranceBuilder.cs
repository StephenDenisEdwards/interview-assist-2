namespace InterviewAssist.Library.Pipeline.Utterance;

/// <summary>
/// Segments streaming ASR events into coherent utterances.
/// </summary>
public sealed class UtteranceBuilder : IUtteranceBuilder
{
    private readonly PipelineOptions _options;
    private readonly IStabilizer _stabilizer;
    private readonly Func<DateTime> _clock;

    private UtteranceState? _current;
    private int _utteranceCounter;

    public event Action<UtteranceEvent>? OnUtteranceOpen;
    public event Action<UtteranceEvent>? OnUtteranceUpdate;
    public event Action<UtteranceEvent>? OnUtteranceFinal;

    public UtteranceBuilder(PipelineOptions? options = null, IStabilizer? stabilizer = null, Func<DateTime>? clock = null)
    {
        _options = options ?? PipelineOptions.Default;
        _stabilizer = stabilizer ?? new Stabilizer(_options);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public string? CurrentUtteranceId => _current?.Id;
    public bool HasActiveUtterance => _current != null;

    public void ProcessAsrEvent(AsrEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Text))
        {
            return;
        }

        var now = _clock();

        // Open new utterance if needed
        if (_current == null)
        {
            OpenUtterance(evt, now);
        }

        // Update utterance
        UpdateUtterance(evt, now);

        // Check for close conditions
        CheckCloseConditions(evt, now);
    }

    public void SignalUtteranceEnd()
    {
        if (_current != null)
        {
            CloseUtterance(UtteranceCloseReason.DeepgramSignal);
        }
    }

    public void ForceClose()
    {
        if (_current != null)
        {
            CloseUtterance(UtteranceCloseReason.Manual);
        }
    }

    public void CheckTimeouts()
    {
        if (_current == null) return;

        var now = _clock();
        var duration = now - _current.StartTime;

        // Max duration guard
        if (duration > _options.MaxUtteranceDuration)
        {
            CloseUtterance(UtteranceCloseReason.MaxDuration);
            return;
        }

        // Silence gap detection
        var silenceDuration = now - _current.LastActivityTime;
        if (silenceDuration > _options.SilenceGapThreshold)
        {
            CloseUtterance(UtteranceCloseReason.SilenceGap);
            return;
        }

        // Terminal punctuation + pause (check both flag and value due to potential race condition)
        var punctuationTime = _current.TerminalPunctuationTime;
        if (_current.HasTerminalPunctuation && punctuationTime.HasValue)
        {
            var pauseSincePunctuation = now - punctuationTime.Value;
            if (pauseSincePunctuation > _options.PunctuationPauseThreshold)
            {
                CloseUtterance(UtteranceCloseReason.TerminalPunctuation);
            }
        }
    }

    private void OpenUtterance(AsrEvent evt, DateTime now)
    {
        _utteranceCounter++;
        _stabilizer.Reset();

        _current = new UtteranceState
        {
            Id = $"utt_{_utteranceCounter:D4}",
            StartTime = now,
            LastActivityTime = now,
            SpeakerId = evt.SpeakerId
        };

        OnUtteranceOpen?.Invoke(new UtteranceEvent
        {
            Id = _current.Id,
            Type = UtteranceEventType.Open,
            StartTime = _current.StartTime,
            SpeakerId = _current.SpeakerId
        });
    }

    private void UpdateUtterance(AsrEvent evt, DateTime now)
    {
        if (_current == null) return;

        _current.LastActivityTime = now;
        _current.RawText = CombineText(_current.CommittedText, evt.Text);

        // Update stable text
        string stableText;
        if (evt.IsFinal)
        {
            // Commit final segment
            _stabilizer.CommitFinal(evt.Text);
            _current.CommittedText = CombineText(_current.CommittedText, evt.Text);
            stableText = _current.CommittedText;
        }
        else
        {
            // Process interim
            stableText = CombineText(
                _current.CommittedText,
                _stabilizer.AddHypothesis(evt.Text, evt.Words)
            );
        }

        _current.StableText = stableText;

        // Check for terminal punctuation
        CheckTerminalPunctuation(_current.RawText, now);

        // Max length guard
        if (_current.RawText.Length > _options.MaxUtteranceLength)
        {
            CloseUtterance(UtteranceCloseReason.MaxLength);
            return;
        }

        OnUtteranceUpdate?.Invoke(new UtteranceEvent
        {
            Id = _current.Id,
            Type = UtteranceEventType.Update,
            StartTime = _current.StartTime,
            StableText = _current.StableText,
            RawText = _current.RawText,
            Duration = now - _current.StartTime,
            SpeakerId = _current.SpeakerId
        });
    }

    private void CheckCloseConditions(AsrEvent evt, DateTime now)
    {
        if (_current == null) return;

        // Deepgram explicit utterance end
        if (evt.IsUtteranceEnd)
        {
            CloseUtterance(UtteranceCloseReason.DeepgramSignal);
        }
    }

    private void CloseUtterance(UtteranceCloseReason reason)
    {
        if (_current == null) return;

        var now = _clock();
        var finalText = string.IsNullOrWhiteSpace(_current.StableText)
            ? _current.RawText
            : _current.StableText;

        OnUtteranceFinal?.Invoke(new UtteranceEvent
        {
            Id = _current.Id,
            Type = UtteranceEventType.Final,
            StartTime = _current.StartTime,
            StableText = finalText,
            RawText = _current.RawText,
            Duration = now - _current.StartTime,
            CloseReason = reason,
            SpeakerId = _current.SpeakerId
        });

        _current = null;
        _stabilizer.Reset();
    }

    private void CheckTerminalPunctuation(string text, DateTime now)
    {
        if (_current == null) return;

        var trimmed = text.TrimEnd();
        if (trimmed.Length > 0)
        {
            var lastChar = trimmed[^1];
            if (lastChar is '.' or '?' or '!')
            {
                if (!_current.HasTerminalPunctuation)
                {
                    _current.HasTerminalPunctuation = true;
                    _current.TerminalPunctuationTime = now;
                }
            }
            else
            {
                // Punctuation was removed (hypothesis changed)
                _current.HasTerminalPunctuation = false;
                _current.TerminalPunctuationTime = null;
            }
        }
    }

    private static string CombineText(string committed, string addition)
    {
        if (string.IsNullOrWhiteSpace(committed))
            return addition.Trim();

        if (string.IsNullOrWhiteSpace(addition))
            return committed;

        // Avoid duplication - check if addition is continuation
        var commitWords = committed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var addWords = addition.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (commitWords.Length == 0) return addition.Trim();
        if (addWords.Length == 0) return committed;

        // Simple append with space
        return $"{committed.TrimEnd()} {addition.Trim()}";
    }

    private sealed class UtteranceState
    {
        public required string Id { get; init; }
        public required DateTime StartTime { get; init; }
        public DateTime LastActivityTime { get; set; }
        public string CommittedText { get; set; } = "";
        public string StableText { get; set; } = "";
        public string RawText { get; set; } = "";
        public bool HasTerminalPunctuation { get; set; }
        public DateTime? TerminalPunctuationTime { get; set; }
        public string? SpeakerId { get; init; }
    }
}
