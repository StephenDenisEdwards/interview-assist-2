using InterviewAssist.Library.Audio;

namespace InterviewAssist.Pipeline;

/// <summary>
/// Orchestrates the interview pipeline:
/// Audio → Transcription → Question Detection
///
/// Uses a sliding window of transcripts for detection to catch
/// questions that span multiple transcription batches.
/// </summary>
public sealed class InterviewPipeline : IAsyncDisposable
{
    private readonly TranscriptionService _transcription;
    private readonly QuestionDetector? _detector;
    private readonly List<string> _transcriptWindow = new();
    private readonly List<string> _detectedQuestions = new();
    private readonly int _windowSize;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private int _started;

    public event Action<string>? OnTranscript;
    public event Action<DetectedQuestion>? OnQuestionDetected;
    public event Action<string>? OnError;
    public event Action<string>? OnInfo;

    /// <summary>
    /// Creates a new InterviewPipeline with optional question detection.
    /// </summary>
    /// <param name="audioCapture">Audio capture service.</param>
    /// <param name="apiKey">OpenAI API key for transcription.</param>
    /// <param name="sampleRate">Audio sample rate in Hz.</param>
    /// <param name="transcriptionBatchMs">Transcription batch interval in milliseconds.</param>
    /// <param name="questionDetector">
    /// Optional question detector. If null, question detection is disabled
    /// and only transcription will occur.
    /// </param>
    /// <param name="windowSize">Number of transcripts to keep in sliding window for detection.</param>
    public InterviewPipeline(
        IAudioCaptureService audioCapture,
        string apiKey,
        int sampleRate = 24000,
        int transcriptionBatchMs = 3000,
        QuestionDetector? questionDetector = null,
        int windowSize = 6)
    {
        _windowSize = windowSize;
        _detector = questionDetector;

        _transcription = new TranscriptionService(audioCapture, apiKey, sampleRate, transcriptionBatchMs);

        // Wire up detector events if present
        if (_detector != null)
        {
            _detector.OnQuestionDetected += HandleDetectedQuestion;
            _detector.OnError += err => OnError?.Invoke($"[Detection] {err}");
        }

        // Wire transcription events
        _transcription.OnTranscript += HandleTranscript;
        _transcription.OnError += err => OnError?.Invoke($"[Transcription] {err}");
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("Pipeline already started");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        OnInfo?.Invoke("Starting pipeline...");
        _transcription.Start(_cts.Token);
        OnInfo?.Invoke("Pipeline running. Listening for audio...");
    }

    private void HandleTranscript(string text)
    {
        // Fire transcript event
        OnTranscript?.Invoke(text);

        // Skip detection if no detector registered
        if (_detector == null)
        {
            return;
        }

        // Build sliding window
        string windowText;
        lock (_lock)
        {
            _transcriptWindow.Add(text);
            if (_transcriptWindow.Count > _windowSize)
            {
                _transcriptWindow.RemoveAt(0);
            }

            // Combine all transcripts in window for analysis
            windowText = string.Join(" ", _transcriptWindow);
        }

        // Analyze the full window for questions
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _ = _detector.AnalyzeAsync(windowText, null, _cts.Token);
        }
    }

    private void HandleDetectedQuestion(DetectedQuestion question)
    {
        var normalized = NormalizeText(question.Text);

        // Skip very short questions (likely noise)
        if (normalized.Length < 15)
            return;

        lock (_lock)
        {
            // Check if this question is a subset of an existing one (skip - existing is more complete)
            foreach (var existing in _detectedQuestions)
            {
                if (existing.Contains(normalized))
                {
                    // Already have a more complete version
                    return;
                }
            }

            // Check if this question is a superset of an existing one (replace)
            for (int i = _detectedQuestions.Count - 1; i >= 0; i--)
            {
                if (normalized.Contains(_detectedQuestions[i]))
                {
                    // New question is more complete - remove old one
                    _detectedQuestions.RemoveAt(i);
                }
            }

            // Add the new question
            _detectedQuestions.Add(normalized);

            // Limit cache size
            if (_detectedQuestions.Count > 50)
            {
                _detectedQuestions.RemoveAt(0);
            }
        }

        // Forward to user
        OnQuestionDetected?.Invoke(question);
    }

    private static string NormalizeText(string text)
    {
        // Normalize: lowercase, remove punctuation, collapse whitespace
        var normalized = text.ToLowerInvariant();
        var chars = normalized.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        normalized = new string(chars);
        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized;
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
            return;

        OnInfo?.Invoke("Stopping pipeline...");

        _cts?.Cancel();
        await _transcription.StopAsync().ConfigureAwait(false);

        _cts?.Dispose();
        _cts = null;

        lock (_lock)
        {
            _transcriptWindow.Clear();
            _detectedQuestions.Clear();
        }

        OnInfo?.Invoke("Pipeline stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _transcription.DisposeAsync().ConfigureAwait(false);
        _detector?.Dispose();
    }
}
