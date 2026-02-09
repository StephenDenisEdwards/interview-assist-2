using System.Text;
using System.Text.Json;
using InterviewAssist.Library.Pipeline.Detection;
using InterviewAssist.Library.Pipeline.Utterance;

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

    public bool IsRecording => _isRecording;
    public string? CurrentFilePath { get; private set; }

    public event Action<string>? OnInfo;

    public SessionRecorder(UtteranceIntentPipeline pipeline)
    {
        _pipeline = pipeline;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
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
    private void OnAsrFinal(AsrEvent evt) => WriteAsrEvent(evt);

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
    private void OnUtteranceFinal(UtteranceEvent evt) => WriteUtteranceEvent(evt);

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
                SpeakerId = evt.SpeakerId
            }
        });
    }

    private void OnIntentCandidate(IntentEvent evt) => WriteIntentEvent(evt, isCandidate: true);
    private void OnIntentFinal(IntentEvent evt) => WriteIntentEvent(evt, isCandidate: false);

    private void WriteIntentEvent(IntentEvent evt, bool isCandidate)
    {
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
                    OriginalText = evt.Intent.OriginalText,
                    Slots = evt.Intent.Slots != null ? new IntentSlotsData
                    {
                        Topic = evt.Intent.Slots.Topic,
                        Count = evt.Intent.Slots.Count,
                        Reference = evt.Intent.Slots.Reference
                    } : null
                },
                UtteranceId = evt.UtteranceId,
                IsCandidate = isCandidate
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
                    OriginalText = evt.CorrectedIntent.OriginalText,
                    Slots = evt.CorrectedIntent.Slots != null ? new IntentSlotsData
                    {
                        Topic = evt.CorrectedIntent.Slots.Topic,
                        Count = evt.CorrectedIntent.Slots.Count,
                        Reference = evt.CorrectedIntent.Slots.Reference
                    } : null
                },
                CorrectionType = evt.CorrectionType.ToString()
            }
        });
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
