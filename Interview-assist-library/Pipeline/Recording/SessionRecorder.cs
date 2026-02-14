using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using InterviewAssist.Library.Pipeline.Detection;
using InterviewAssist.Library.Pipeline.Utterance;
using InterviewAssist.Library.Utilities;

namespace InterviewAssist.Library.Pipeline.Recording;

/// <summary>
/// Records pipeline events to a JSONL file for later playback.
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    private readonly UtteranceIntentPipeline _pipeline;
    private readonly JsonSerializerOptions _jsonOptions;

    private StreamWriter? _writer;
    private DateTime _startTime;
    private bool _isRecording;
    private readonly object _lock = new();

    // Running transcript state for computing intent positions
    private readonly StringBuilder _runningTranscript = new();
    private readonly List<(int CharStart, int CharEnd, long OffsetMs)> _transcriptSegments = new();
    private readonly ConcurrentDictionary<string, (long StartMs, long EndMs)> _utteranceTimeRanges = new();

    public bool IsRecording => _isRecording;
    public string? CurrentFilePath { get; private set; }

    public event Action<string>? OnInfo;

    public SessionRecorder(UtteranceIntentPipeline pipeline)
    {
        _pipeline = pipeline;
        _jsonOptions = PipelineJsonOptions.CamelCase;
    }

    /// <summary>
    /// Start recording to the specified file path.
    /// </summary>
    public void Start(string filePath, SessionConfig config)
    {
        lock (_lock)
        {
            if (_isRecording) return;

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
            _startTime = DateTime.UtcNow;
            _isRecording = true;
            CurrentFilePath = filePath;

            _runningTranscript.Clear();
            _transcriptSegments.Clear();
            _utteranceTimeRanges.Clear();

            // Write metadata as first line
            WriteEvent(new RecordedSessionMetadata
            {
                OffsetMs = 0,
                RecordedAtUtc = _startTime,
                Config = config
            });

            // Subscribe to pipeline events
            WireEvents();

            OnInfo?.Invoke($"Recording started: {Path.GetFileName(filePath)}");
        }
    }

    /// <summary>
    /// Stop recording and close the file.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRecording) return;

            UnwireEvents();
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            _isRecording = false;

            OnInfo?.Invoke($"Recording stopped: {Path.GetFileName(CurrentFilePath)}");
            CurrentFilePath = null;
        }
    }

    private void WireEvents()
    {
        _pipeline.OnAsrPartial += OnAsrPartial;
        _pipeline.OnAsrFinal += OnAsrFinal;
        _pipeline.OnUtteranceOpen += OnUtteranceOpen;
        _pipeline.OnUtteranceUpdate += OnUtteranceUpdate;
        _pipeline.OnUtteranceFinal += OnUtteranceFinal;
        _pipeline.OnIntentCandidate += OnIntentCandidate;
        _pipeline.OnIntentFinal += OnIntentFinal;
        _pipeline.OnIntentCorrected += OnIntentCorrected;
        _pipeline.OnActionTriggered += OnActionTriggered;
    }

    private void UnwireEvents()
    {
        _pipeline.OnAsrPartial -= OnAsrPartial;
        _pipeline.OnAsrFinal -= OnAsrFinal;
        _pipeline.OnUtteranceOpen -= OnUtteranceOpen;
        _pipeline.OnUtteranceUpdate -= OnUtteranceUpdate;
        _pipeline.OnUtteranceFinal -= OnUtteranceFinal;
        _pipeline.OnIntentCandidate -= OnIntentCandidate;
        _pipeline.OnIntentFinal -= OnIntentFinal;
        _pipeline.OnIntentCorrected -= OnIntentCorrected;
        _pipeline.OnActionTriggered -= OnActionTriggered;
    }

    private void OnAsrPartial(AsrEvent evt) => WriteAsrEvent(evt);

    private void OnAsrFinal(AsrEvent evt)
    {
        WriteAsrEvent(evt);

        if (evt.IsFinal && !string.IsNullOrWhiteSpace(evt.Text))
        {
            var text = evt.Text.Trim();
            int charStart = _runningTranscript.Length;
            _runningTranscript.Append(text);
            int charEnd = _runningTranscript.Length;
            _runningTranscript.Append(' ');

            _transcriptSegments.Add((charStart, charEnd, GetOffsetMs()));
        }
    }

    private void WriteAsrEvent(AsrEvent evt)
    {
        // Record utterance end signal separately
        if (evt.IsUtteranceEnd)
        {
            WriteEvent(new RecordedUtteranceEndSignal
            {
                OffsetMs = GetOffsetMs()
            });
        }

        WriteEvent(new RecordedAsrEvent
        {
            OffsetMs = GetOffsetMs(),
            Data = new AsrEventData
            {
                Text = evt.Text,
                IsFinal = evt.IsFinal,
                SpeakerId = evt.SpeakerId,
                IsUtteranceEnd = evt.IsUtteranceEnd
            }
        });
    }

    private void OnUtteranceOpen(UtteranceEvent evt) => WriteUtteranceEvent(evt);
    private void OnUtteranceUpdate(UtteranceEvent evt) => WriteUtteranceEvent(evt);

    private void OnUtteranceFinal(UtteranceEvent evt)
    {
        WriteUtteranceEvent(evt);

        if (evt.Type == UtteranceEventType.Final)
        {
            long endMs = GetOffsetMs();
            long startMs = endMs - (long)evt.Duration.TotalMilliseconds;
            _utteranceTimeRanges[evt.Id] = (startMs, endMs);
        }
    }

    private void WriteUtteranceEvent(UtteranceEvent evt)
    {
        WriteEvent(new RecordedUtteranceEvent
        {
            OffsetMs = GetOffsetMs(),
            Data = new UtteranceEventData
            {
                Id = evt.Id,
                EventType = evt.Type.ToString(),
                StableText = evt.StableText,
                RawText = evt.RawText,
                DurationMs = (long)evt.Duration.TotalMilliseconds,
                CloseReason = evt.CloseReason?.ToString(),
                SpeakerId = evt.SpeakerId,
                AsrFinalOffsetMs = evt.CommittedAsrTimestamps?.Select(
                    ts => (long)(ts - _startTime).TotalMilliseconds).ToList()
            }
        });
    }

    private void OnIntentCandidate(IntentEvent evt) => WriteIntentEvent(evt, isCandidate: true);
    private void OnIntentFinal(IntentEvent evt) => WriteIntentEvent(evt, isCandidate: false);

    private void WriteIntentEvent(IntentEvent evt, bool isCandidate)
    {
        var (charStart, charEnd) = ComputeTranscriptPosition(
            evt.Intent.OriginalText, evt.Intent.SourceText, evt.UtteranceId);

        // Use transcript substring as OriginalText when position is known
        string? transcriptOriginal = (charStart != null && charEnd != null)
            ? _runningTranscript.ToString(charStart.Value, charEnd.Value - charStart.Value)
            : null;

        WriteEvent(new RecordedIntentEvent
        {
            OffsetMs = GetOffsetMs(),
            Data = new IntentEventData
            {
                Intent = new DetectedIntentData
                {
                    Type = evt.Intent.Type.ToString(),
                    Subtype = evt.Intent.Subtype?.ToString(),
                    Confidence = evt.Intent.Confidence,
                    SourceText = evt.Intent.SourceText,
                    OriginalText = transcriptOriginal ?? evt.Intent.OriginalText,
                    Slots = evt.Intent.Slots != null ? new IntentSlotsData
                    {
                        Topic = evt.Intent.Slots.Topic,
                        Count = evt.Intent.Slots.Count,
                        Reference = evt.Intent.Slots.Reference
                    } : null
                },
                UtteranceId = evt.UtteranceId,
                IsCandidate = isCandidate,
                TranscriptCharStart = charStart,
                TranscriptCharEnd = charEnd
            }
        });
    }

    private void OnActionTriggered(ActionEvent evt)
    {
        WriteEvent(new RecordedActionEvent
        {
            OffsetMs = GetOffsetMs(),
            Data = new ActionEventData
            {
                ActionName = evt.ActionName,
                UtteranceId = evt.UtteranceId,
                WasDebounced = evt.WasDebounced
            }
        });
    }

    private void OnIntentCorrected(IntentCorrectionEvent evt)
    {
        var (charStart, charEnd) = ComputeTranscriptPosition(
            evt.CorrectedIntent.OriginalText, evt.CorrectedIntent.SourceText, evt.UtteranceId);

        // Use transcript substring as OriginalText when position is known
        string? transcriptOriginal = (charStart != null && charEnd != null)
            ? _runningTranscript.ToString(charStart.Value, charEnd.Value - charStart.Value)
            : null;

        WriteEvent(new RecordedIntentCorrectionEvent
        {
            OffsetMs = GetOffsetMs(),
            Data = new IntentCorrectionEventData
            {
                UtteranceId = evt.UtteranceId,
                OriginalIntent = evt.OriginalIntent != null ? new DetectedIntentData
                {
                    Type = evt.OriginalIntent.Type.ToString(),
                    Subtype = evt.OriginalIntent.Subtype?.ToString(),
                    Confidence = evt.OriginalIntent.Confidence,
                    SourceText = evt.OriginalIntent.SourceText,
                    OriginalText = evt.OriginalIntent.OriginalText,
                    Slots = evt.OriginalIntent.Slots != null ? new IntentSlotsData
                    {
                        Topic = evt.OriginalIntent.Slots.Topic,
                        Count = evt.OriginalIntent.Slots.Count,
                        Reference = evt.OriginalIntent.Slots.Reference
                    } : null
                } : null,
                CorrectedIntent = new DetectedIntentData
                {
                    Type = evt.CorrectedIntent.Type.ToString(),
                    Subtype = evt.CorrectedIntent.Subtype?.ToString(),
                    Confidence = evt.CorrectedIntent.Confidence,
                    SourceText = evt.CorrectedIntent.SourceText,
                    OriginalText = transcriptOriginal ?? evt.CorrectedIntent.OriginalText,
                    Slots = evt.CorrectedIntent.Slots != null ? new IntentSlotsData
                    {
                        Topic = evt.CorrectedIntent.Slots.Topic,
                        Count = evt.CorrectedIntent.Slots.Count,
                        Reference = evt.CorrectedIntent.Slots.Reference
                    } : null
                },
                CorrectionType = evt.CorrectionType.ToString(),
                TranscriptCharStart = charStart,
                TranscriptCharEnd = charEnd
            }
        });
    }

    private (int? Start, int? End) ComputeTranscriptPosition(
        string? originalText, string? sourceText, string utteranceId)
    {
        if (_utteranceTimeRanges.TryGetValue(utteranceId, out var timeRange))
        {
            var result = ComputeTranscriptPosition(
                originalText, sourceText, timeRange,
                _runningTranscript, _transcriptSegments);
            if (result.Start != null)
                return result;
        }

        // Fallback: search the entire transcript
        return SearchFullTranscript(originalText, sourceText);
    }

    private (int? Start, int? End) SearchFullTranscript(string? originalText, string? sourceText)
    {
        if (_runningTranscript.Length == 0)
            return (null, null);

        var transcript = _runningTranscript.ToString();

        foreach (var searchText in new[] { sourceText, originalText })
        {
            if (string.IsNullOrWhiteSpace(searchText))
                continue;

            int idx = transcript.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return (idx, idx + searchText.Length);
        }

        return (null, null);
    }

    /// <summary>
    /// Compute the character range in a running transcript that corresponds to an intent's source text.
    /// Uses the utterance's time range to narrow down to a bounded window of ASR segments,
    /// then searches for the original/source text within that window.
    /// </summary>
    internal static (int? Start, int? End) ComputeTranscriptPosition(
        string? originalText, string? sourceText,
        (long StartMs, long EndMs) utteranceTimeRange,
        StringBuilder runningTranscript,
        List<(int CharStart, int CharEnd, long OffsetMs)> transcriptSegments)
    {
        // Find ASR segments within the utterance's time range (±2s tolerance)
        long windowStart = utteranceTimeRange.StartMs - 2000;
        long windowEnd = utteranceTimeRange.EndMs + 2000;

        var matchingSegments = transcriptSegments
            .Where(s => s.OffsetMs >= windowStart && s.OffsetMs <= windowEnd)
            .ToList();

        if (matchingSegments.Count == 0)
            return (null, null);

        int regionStart = matchingSegments.First().CharStart;
        int regionEnd = matchingSegments.Last().CharEnd;
        var region = runningTranscript.ToString(regionStart, regionEnd - regionStart);

        // Try sourceText first (clean LLM-reformulated text gives a precise match),
        // then originalText as fallback (for cases where LLM resolved pronouns)
        foreach (var searchText in new[] { sourceText, originalText })
        {
            if (string.IsNullOrWhiteSpace(searchText))
                continue;

            int idx = region.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return (regionStart + idx, regionStart + idx + searchText.Length);
        }

        // Text not found in region — return the full utterance region
        return (regionStart, regionEnd);
    }

    private long GetOffsetMs() => (long)(DateTime.UtcNow - _startTime).TotalMilliseconds;

    private void WriteEvent(RecordedEvent evt)
    {
        lock (_lock)
        {
            if (_writer == null) return;
            // Serialize as base type to include polymorphic type discriminator
            var json = JsonSerializer.Serialize<RecordedEvent>(evt, _jsonOptions);
            _writer.WriteLine(json);
            _writer.Flush(); // Ensure events are written immediately
        }
    }

    public void Dispose() => Stop();
}
