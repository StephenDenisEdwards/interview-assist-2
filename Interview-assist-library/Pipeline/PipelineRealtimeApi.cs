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
    private readonly IQuestionDetectionService _detector;
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

    // Event dispatcher (same pattern as OpenAiRealtimeApi)
    private Channel<Action>? _eventChannel;
    private Task? _eventTask;

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

    public PipelineRealtimeApi(
        IAudioCaptureService audioCaptureService,
        PipelineApiOptions options,
        ILogger<PipelineRealtimeApi>? logger = null)
    {
        _audio = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<PipelineRealtimeApi>.Instance;

        _logger.LogInformation("Initializing Pipeline Realtime API");


		if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("API key is required", nameof(options));

        // Create internal services
        _detector = new OpenAiQuestionDetectionService(
            options.ApiKey,
            options.DetectionModel,
            options.DetectionConfidenceThreshold,
            _logger as ILogger<OpenAiQuestionDetectionService>);

        _chat = new OpenAiChatCompletionService(
            options.ApiKey,
            options.ResponseModel,
            options.MaxResponseTokens,
            options.Temperature,
            _logger as ILogger<OpenAiChatCompletionService>);

        _transcriptBuffer = new TranscriptBuffer(options.TranscriptBufferSeconds);
        _questionQueue = new QuestionQueue(options.MaxQueuedQuestions);
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
            StartEventDispatcher(_cts.Token);

            // Fire connected event (simulated - no actual WebSocket)
            IsConnected = true;
            SafeRaise(() => OnConnected?.Invoke());

            // Create and start transcriber
            _transcriber = new OpenAiMicTranscriber(_audio, _options.ApiKey, _options.SampleRate);
            _transcriber.OnTranscript += HandleTranscript;
            _transcriber.OnInfo += msg => SafeRaise(() => OnInfo?.Invoke(msg));
            _transcriber.OnWarning += msg => SafeRaise(() => OnWarning?.Invoke(msg));
            _transcriber.OnError += ex => SafeRaise(() => OnError?.Invoke(ex));

            // Start detection and response loops
            _detectionTask = DetectionLoopAsync(_cts.Token);
            _responseTask = ResponseLoopAsync(_cts.Token);

            SafeRaise(() => OnInfo?.Invoke("Pipeline mode: continuous transcription active"));
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

        // Add to buffer
        _transcriptBuffer.Add(text);

        // Fire user transcript event
        SafeRaise(() => OnUserTranscript?.Invoke(text));
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
        StopEventDispatcher();

        // Cleanup CTS
        try { _cts?.Dispose(); } catch { }
        _cts = null;

        // Clear buffers
        _transcriptBuffer.Clear();
        _questionQueue.ClearDeduplicationCache();
        _lastDetectionTimestamp = DateTime.MinValue;
    }

    #endregion

    #region Event Dispatcher (same pattern as OpenAiRealtimeApi)

    private void SafeRaise(Action action)
    {
        try
        {
            if (_eventChannel != null)
            {
                _eventChannel.Writer.TryWrite(action);
                return;
            }
            action();
        }
        catch
        {
            // Protect internal loops from subscriber exceptions
        }
    }

    private void StartEventDispatcher(CancellationToken ct)
    {
        _eventChannel = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _eventTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var action in _eventChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try { action(); }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Event handler exception");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private void StopEventDispatcher()
    {
        try { _eventChannel?.Writer.TryComplete(); } catch { }
        _eventChannel = null;
        _eventTask = null;
    }

    #endregion
}
