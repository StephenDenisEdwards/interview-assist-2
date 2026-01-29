using System.Threading.Channels;
using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Realtime;
using InterviewAssist.Library.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// Pipeline-based implementation of IRealtimeApi that uses continuous STT,
/// semantic question detection, and GPT-4 Chat API for response generation.
///
/// Unlike the OpenAI Realtime API, this implementation:
/// - Never blocks on turn-based semantics
/// - Detects questions semantically rather than by silence
/// - Can queue and process multiple questions without waiting
/// </summary>
public sealed class PipelineRealtimeApi : IRealtimeApi
{
    private readonly IAudioCaptureService _audio;
    private readonly PipelineApiOptions _options;
    private readonly ILogger<PipelineRealtimeApi> _logger;

    // Internal services
    private readonly IQuestionDetectionService? _detector;
    private readonly IChatCompletionService _chat;
    private readonly TranscriptBuffer _transcriptBuffer;
    private readonly QuestionQueue _questionQueue;

    // Transcriber (reuse existing OpenAiMicTranscriber pattern)
    private OpenAiMicTranscriber? _transcriber;

    // State management
    private CancellationTokenSource? _cts;
    private Task? _detectionTask;
    private Task? _responseTask;
    private int _started;

    // Event dispatcher (shared with OpenAiRealtimeApi)
    private readonly EventDispatcher _eventDispatcher;

    // Track last detected position to avoid re-detecting same questions
    private DateTime _lastDetectionTimestamp = DateTime.MinValue;

    #region IRealtimeApi Events
    public event Action? OnConnected;
    public event Action? OnReady;
    public event Action? OnDisconnected;
    public event Action? OnReconnecting;
    public event Action<string>? OnInfo;
    public event Action<string>? OnWarning;
    public event Action<string>? OnDebug;
    public event Action<Exception>? OnError;
    public event Action<string>? OnUserTranscript;
    public event Action? OnSpeechStarted;
    public event Action? OnSpeechStopped;
    public event Action<string>? OnAssistantTextDelta;
    public event Action? OnAssistantTextDone;
    public event Action<string>? OnAssistantAudioTranscriptDelta;
    public event Action? OnAssistantAudioTranscriptDone;
    public event Action<string, string, string>? OnFunctionCallResponse;
    #endregion

    public bool IsConnected { get; private set; }

    /// <summary>
    /// Creates a new PipelineRealtimeApi instance.
    /// </summary>
    /// <param name="audioCaptureService">Audio capture service for microphone/loopback input.</param>
    /// <param name="options">Pipeline configuration options.</param>
    /// <param name="questionDetectionService">
    /// Optional question detection service. If null, question detection is disabled
    /// and only transcription will occur. Register via services.AddQuestionDetection()
    /// to enable detection.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public PipelineRealtimeApi(
        IAudioCaptureService audioCaptureService,
        PipelineApiOptions options,
        IQuestionDetectionService? questionDetectionService = null,
        ILogger<PipelineRealtimeApi>? logger = null)
    {
        _audio = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _detector = questionDetectionService; // null means detection disabled
        _logger = logger ?? NullLogger<PipelineRealtimeApi>.Instance;

        _logger.LogInformation("Initializing Pipeline Realtime API (detection: {DetectionEnabled})",
            _detector != null ? "enabled" : "disabled");

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("API key is required", nameof(options));

        _chat = new OpenAiChatCompletionService(
            options.ApiKey,
            options.ResponseModel,
            options.MaxResponseTokens,
            options.Temperature,
            _logger as ILogger<OpenAiChatCompletionService>);

        _transcriptBuffer = new TranscriptBuffer(options.TranscriptBufferSeconds);
        _questionQueue = new QuestionQueue(
            options.MaxQueuedQuestions,
            options.DeduplicationSimilarityThreshold,
            options.DeduplicationWindowMs);
        _eventDispatcher = new EventDispatcher(_logger);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("Pipeline API already started.");
        }

        _logger.LogInformation("Starting Pipeline Realtime API");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // Start event dispatcher
            _eventDispatcher.Start(_cts.Token);

            // Fire connected event (simulated - no actual WebSocket)
            IsConnected = true;
            SafeRaise(() => OnConnected?.Invoke());

            // Create and start transcriber
            _transcriber = new OpenAiMicTranscriber(
                _audio,
                _options.ApiKey,
                _options.SampleRate,
                _options.SilenceEnergyThreshold,
                _options.TranscriptionLanguage,
                _options.TranscriptionPrompt,
                _options.TranscriptionBatchMs,
                _options.MaxTranscriptionBatchMs);
            _transcriber.OnTranscript += HandleTranscript;
            _transcriber.OnInfo += msg => SafeRaise(() => OnInfo?.Invoke(msg));
            _transcriber.OnWarning += msg => SafeRaise(() => OnWarning?.Invoke(msg));
            _transcriber.OnError += ex => SafeRaise(() => OnError?.Invoke(ex));

            // Start detection and response loops only if detection service is registered
            if (_detector != null)
            {
                _detectionTask = DetectionLoopAsync(_cts.Token);
                _responseTask = ResponseLoopAsync(_cts.Token);
                SafeRaise(() => OnInfo?.Invoke("Pipeline mode: continuous transcription with question detection active"));
            }
            else
            {
                SafeRaise(() => OnInfo?.Invoke("Pipeline mode: continuous transcription active (question detection disabled)"));
            }
            SafeRaise(() => OnReady?.Invoke());

            // Start transcriber (blocks until cancelled)
            await _transcriber.StartAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            _logger.LogInformation("Pipeline API cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline API error");
            SafeRaise(() => OnError?.Invoke(ex));
            throw;
        }
        finally
        {
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        _logger.LogInformation("Stopping Pipeline Realtime API");

        try { _cts?.Cancel(); } catch { }

        await CleanupAsync().ConfigureAwait(false);

        SafeRaise(() => OnDisconnected?.Invoke());
    }

    public async Task SendTextAsync(string text, bool requestResponse = true, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _logger.LogDebug("Injecting text: {Text}", text.Length > 50 ? text[..50] + "..." : text);

        // Add to transcript buffer
        _transcriptBuffer.Add(text);

        // Fire user transcript event
        SafeRaise(() => OnUserTranscript?.Invoke(text));

        // If response requested, create a synthetic question and queue it
        if (requestResponse)
        {
            var question = new DetectedQuestion
            {
                Text = text,
                Confidence = 1.0,
                Type = QuestionType.Question
            };

            var context = _transcriptBuffer.GetAllText();
            _questionQueue.TryEnqueue(question, context);
        }

        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _questionQueue.Dispose();
    }

    #region Internal Pipeline Loops

    private void HandleTranscript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _logger.LogDebug("Transcript received: {Text}", text.Length > 100 ? text[..100] + "..." : text);

        // Filter low-quality transcription (hallucinations, very short text)
        if (TranscriptQualityFilter.IsLowQuality(text))
        {
            _logger.LogDebug("Filtered low-quality transcript: {Text}", text.Length > 50 ? text[..50] + "..." : text);
            return;
        }

        // Clean up repetitions
        var cleaned = TranscriptQualityFilter.CleanTranscript(text);
        if (string.IsNullOrWhiteSpace(cleaned)) return;

        // Add to buffer
        _transcriptBuffer.Add(cleaned);

        // Fire user transcript event
        SafeRaise(() => OnUserTranscript?.Invoke(cleaned));
    }

    private async Task DetectionLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Detection loop started (interval: {Interval}ms)", _options.DetectionIntervalMs);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_options.DetectionIntervalMs, ct).ConfigureAwait(false);

                try
                {
                    await DetectAndQueueQuestionsAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Detection iteration failed");
                    SafeRaise(() => OnWarning?.Invoke($"Detection error: {ex.Message}"));
                }
            }
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("Detection loop stopped");
    }

    private async Task DetectAndQueueQuestionsAsync(CancellationToken ct)
    {
        // Skip if detection is disabled
        if (_detector == null)
        {
            return;
        }

        // Get transcript since last detection
        var lastTimestamp = _lastDetectionTimestamp;
        var entries = _transcriptBuffer.GetRecentEntries(_options.TranscriptBufferSeconds);

        // Filter to entries after last detection
        var newEntries = entries.Where(e => e.Timestamp > lastTimestamp).ToList();
        if (newEntries.Count == 0)
        {
            return;
        }

        var textToAnalyze = string.Join(" ", newEntries.Select(e => e.Text));
        if (string.IsNullOrWhiteSpace(textToAnalyze))
        {
            return;
        }

        // Update timestamp before detection to avoid re-detecting
        _lastDetectionTimestamp = newEntries.Max(e => e.Timestamp);

        // Get previous context for follow-up detection
        var previousContext = entries
            .Where(e => e.Timestamp <= lastTimestamp)
            .OrderByDescending(e => e.Timestamp)
            .Take(5)
            .Select(e => e.Text);
        var contextText = string.Join(" ", previousContext);

        _logger.LogDebug("Analyzing {Length} chars for questions", textToAnalyze.Length);

        var detected = await _detector.DetectQuestionsAsync(textToAnalyze, contextText, ct)
            .ConfigureAwait(false);

        foreach (var question in detected)
        {
            SafeRaise(() => OnInfo?.Invoke($"Question detected ({question.Type}): {question.Text}"));

            var fullContext = _transcriptBuffer.GetAllText();
            if (_questionQueue.TryEnqueue(question, fullContext))
            {
                _logger.LogInformation("Queued question: {Text}", question.Text);
            }
            else
            {
                _logger.LogDebug("Question not queued (duplicate or full): {Text}", question.Text);
            }
        }
    }

    private async Task ResponseLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Response loop started");

        try
        {
            await foreach (var queued in _questionQueue.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await GenerateResponseAsync(queued, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Response generation failed for question: {Question}",
                        queued.Question.Text);
                    SafeRaise(() => OnError?.Invoke(ex));
                }
            }
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("Response loop stopped");
    }

    private async Task GenerateResponseAsync(QueuedQuestion queued, CancellationToken ct)
    {
        _logger.LogInformation("Generating response for: {Question}", queued.Question.Text);

        SafeRaise(() => OnInfo?.Invoke($"Generating response for: {queued.Question.Text}"));

        var response = await _chat.GenerateResponseAsync(
            queued.Question.Text,
            queued.FullContext,
            _options.ContextChunks,
            _options.SystemInstructions,
            delta => SafeRaise(() => OnAssistantTextDelta?.Invoke(delta)),
            ct).ConfigureAwait(false);

        SafeRaise(() => OnAssistantTextDone?.Invoke());

        _logger.LogDebug("Response generated: answer={AnswerLen} chars, code={CodeLen} chars",
            response.Answer.Length, response.Code.Length);

        SafeRaise(() => OnFunctionCallResponse?.Invoke(
            response.FunctionName,
            response.Answer,
            response.Code));
    }

    #endregion

    #region Cleanup

    private async Task CleanupAsync()
    {
        IsConnected = false;

        // Stop transcriber
        if (_transcriber != null)
        {
            _transcriber.OnTranscript -= HandleTranscript;
            try { await _transcriber.StopAsync().ConfigureAwait(false); } catch { }
            try { await _transcriber.DisposeAsync().ConfigureAwait(false); } catch { }
            _transcriber = null;
        }

        // Complete queue
        _questionQueue.Complete();

        // Wait for tasks
        if (_detectionTask != null)
        {
            try { await _detectionTask.ConfigureAwait(false); } catch { }
            _detectionTask = null;
        }

        if (_responseTask != null)
        {
            try { await _responseTask.ConfigureAwait(false); } catch { }
            _responseTask = null;
        }

        // Stop event dispatcher
        _eventDispatcher.Stop();

        // Cleanup CTS
        try { _cts?.Dispose(); } catch { }
        _cts = null;

        // Clear buffers
        _transcriptBuffer.Clear();
        _questionQueue.ClearDeduplicationCache();
        _lastDetectionTimestamp = DateTime.MinValue;
    }

    #endregion

    #region Event Dispatcher
    private void SafeRaise(Action action) => _eventDispatcher.Raise(action);
    #endregion
}
