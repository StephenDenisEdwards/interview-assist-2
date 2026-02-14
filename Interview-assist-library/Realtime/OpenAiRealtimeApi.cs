// CS0420: Interlocked operations on volatile fields are intentional - Interlocked provides its own barriers
// CS0067: OnDebug event is part of the IRealtimeApi interface but not used in this implementation
#pragma warning disable CS0420, CS0067

using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Constants;
using InterviewAssist.Library.Context;
using InterviewAssist.Library.Diagnostics;
using InterviewAssist.Library.Resilience;
using InterviewAssist.Library.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace InterviewAssist.Library.Realtime;

public class OpenAiRealtimeApi : IRealtimeApi
{
    private readonly RealtimeApiOptions _options;
    private readonly ILogger<OpenAiRealtimeApi> _logger;
    private readonly string _wsUrl;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _externalCts;
    private IAudioCaptureService _audio;

    // Serialize websocket sends
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // --- Turn finalization / buffer sizing ---
    private static int MinCommitBytes => AudioConstants.MinCommitBytes;
    private long _pendingAudioBytes;
    private volatile int _hasUncommittedAudio;

    // --- Response coordination ---
    private volatile int _responseActive;
    private volatile int _responseRequested;
    private readonly SemaphoreSlim _responseCoordinatorLock = new(1, 1);

    // Lifecycle
    private int _started;
    private Task? _receiveTask;
    private Task? _audioSendTask;

    // Audio backpressure
    private Channel<byte[]>? _audioChannel;
    private Action<byte[]>? _audioHandler;

    // Event dispatcher
    private readonly EventDispatcher _eventDispatcher;

    // Function call state
    private readonly object _funcSync = new();
    private Dictionary<string, StringBuilder> _functionCallBuffers = new();
    private Dictionary<string, string> _functionCallNames = new();
    private HashSet<string> _pendingFunctionParse = new();

    // Rate limiting and reconnection
    private volatile bool _quotaExhausted;
    private volatile bool _rateLimited;
    private RateLimitCircuitBreaker? _rateLimitCircuitBreaker;
    private int _reconnectAttempts;

    // Response timing for metrics
    private long _responseStartTimestamp;

    // Correlation ID for request tracing
    private string _correlationId = string.Empty;

    // Session state tracking for graceful shutdown
    private DateTime _sessionStartTime;
    private readonly Queue<string> _recentTranscripts = new();
    private readonly object _transcriptLock = new();

    // Serialization
    private static readonly JsonSerializerOptions s_jsonOptions = PipelineJsonOptions.Compact;
    private static readonly Regex s_codeBlockRegex = new(
        @"```(?:csharp|cs|c#)?\s*\n(.*?)\n```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    /// <summary>
    /// Gets the correlation ID for the current session.
    /// </summary>
    public string CorrelationId => _correlationId;

    #region Events
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
    public event Action<int>? OnBackpressure;
    #endregion

    #region Constructors
    public OpenAiRealtimeApi(IAudioCaptureService audioCaptureService, RealtimeApiOptions options)
        : this(audioCaptureService, options, NullLogger<OpenAiRealtimeApi>.Instance)
    {
    }

    public OpenAiRealtimeApi(IAudioCaptureService audioCaptureService, RealtimeApiOptions options, ILogger<OpenAiRealtimeApi> logger)
    {
        _audio = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<OpenAiRealtimeApi>.Instance;
        _wsUrl = ModelConstants.BuildRealtimeUrl(options.RealtimeModel);
        _eventDispatcher = new EventDispatcher(_logger);

        _logger.LogInformation("Initializing OpenAI Live Realtime API with model {Model}", options.RealtimeModel);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("API key is required", nameof(options));
    }

    // Legacy constructors for backward compatibility
    public OpenAiRealtimeApi(IAudioCaptureService audioCaptureService, string openAiApiKey)
        : this(audioCaptureService, new RealtimeApiOptions { ApiKey = openAiApiKey })
    {
    }

    public OpenAiRealtimeApi(IAudioCaptureService audioCaptureService, string openAiApiKey, string? extraInstructions)
        : this(audioCaptureService, new RealtimeApiOptions { ApiKey = openAiApiKey, ExtraInstructions = extraInstructions })
    {
    }

    public OpenAiRealtimeApi(IAudioCaptureService audioCaptureService, string openAiApiKey, IReadOnlyList<ContextChunk> contextChunks)
        : this(audioCaptureService, new RealtimeApiOptions { ApiKey = openAiApiKey, ContextChunks = contextChunks })
    {
    }

    public OpenAiRealtimeApi(IAudioCaptureService audioCaptureService, string openAiApiKey, string? extraInstructions, IReadOnlyList<ContextChunk> contextChunks)
        : this(audioCaptureService, new RealtimeApiOptions { ApiKey = openAiApiKey, ExtraInstructions = extraInstructions, ContextChunks = contextChunks })
    {
    }
    #endregion

    #region Public API
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("Realtime API already started.");
        }

        _correlationId = CorrelationContext.GenerateId();
        CorrelationContext.Set(_correlationId);
        _sessionStartTime = DateTime.UtcNow;

        _logger.LogInformation("[{CorrelationId}] Starting Realtime API session", _correlationId);

        _externalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _reconnectAttempts = 0;

        await ConnectAndRunAsync().ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        _logger.LogInformation("[{CorrelationId}] Stopping Realtime API", _correlationId);

        await CleanupConnectionAsync().ConfigureAwait(false);

        _rateLimitCircuitBreaker?.Dispose();
        _rateLimitCircuitBreaker = null;

        _quotaExhausted = false;
        _rateLimited = false;
        _reconnectAttempts = 0;

        SafeRaise(() => OnDisconnected?.Invoke());
    }

    public async Task SendTextAsync(string text, bool requestResponse = true, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_ws == null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("Realtime socket is not connected.");

        _logger.LogDebug("Sending text: {Text}", text.Length > 50 ? text[..50] + "..." : text);

        if (interrupt)
        {
            await SendMessage(new { type = "response.cancel" }).ConfigureAwait(false);
        }

        var itemCreate = new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role = "user",
                content = new object[] { new { type = "input_text", text } }
            }
        };

        await SendMessage(itemCreate).ConfigureAwait(false);
        SafeRaise(() => OnUserTranscript?.Invoke(text));

        if (requestResponse)
        {
            await SendMessage(new { type = "response.create" }).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
    #endregion

    #region Connection Management
    private async Task ConnectAndRunAsync()
    {
        while (_started == 1 && !(_externalCts?.IsCancellationRequested ?? true))
        {
            try
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(_externalCts!.Token);
                await ConnectAsync().ConfigureAwait(false);
                await RunSessionAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_externalCts?.IsCancellationRequested == true)
            {
                _logger.LogInformation("Realtime API cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{CorrelationId}] Connection error", _correlationId);
                SafeRaise(() => OnError?.Invoke(ex));

                if (!_options.EnableReconnection || _quotaExhausted)
                {
                    break;
                }

                if (_reconnectAttempts >= _options.MaxReconnectAttempts)
                {
                    _logger.LogError("Max reconnection attempts ({Max}) reached", _options.MaxReconnectAttempts);
                    SafeRaise(() => OnError?.Invoke(new InvalidOperationException($"Failed to reconnect after {_options.MaxReconnectAttempts} attempts")));
                    break;
                }

                await CleanupConnectionAsync().ConfigureAwait(false);
                await AttemptReconnectAsync().ConfigureAwait(false);
            }
        }

        await CleanupConnectionAsync().ConfigureAwait(false);
    }

    private async Task ConnectAsync()
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {_options.ApiKey}");
        _ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
        _ws.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(_options.WebSocketKeepAliveIntervalMs);

        _logger.LogInformation(
            "[{CorrelationId}] Connecting to OpenAI Realtime API at {Url} (timeout: {TimeoutMs}ms, keepAlive: {KeepAliveMs}ms)",
            _correlationId, _wsUrl, _options.WebSocketConnectTimeoutMs, _options.WebSocketKeepAliveIntervalMs);

        // Create a timeout-linked cancellation token for the connection attempt
        using var timeoutCts = new CancellationTokenSource(_options.WebSocketConnectTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts!.Token, timeoutCts.Token);

        try
        {
            await _ws.ConnectAsync(new Uri(_wsUrl), linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !_cts.IsCancellationRequested)
        {
            throw new TimeoutException($"WebSocket connection timed out after {_options.WebSocketConnectTimeoutMs}ms");
        }

        _logger.LogInformation("[{CorrelationId}] Connected successfully", _correlationId);

        InterviewAssistMetrics.RecordConnection();
        SafeRaise(() => OnConnected?.Invoke());
        _reconnectAttempts = 0;
    }

    private async Task RunSessionAsync()
    {
        _eventDispatcher.Start(_cts!.Token);

        _audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(AudioConstants.AudioChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _receiveTask = ReceiveResponsesLoop(_cts.Token);
        _audioSendTask = AudioSendLoop(_cts.Token);

        await SendSessionConfig().ConfigureAwait(false);

        if (_options.ContextChunks is { Count: > 0 })
        {
            await SendContextChunksAsync(_options.ContextChunks).ConfigureAwait(false);
        }

        SetupAudioInput();
        SafeRaise(() => OnReady?.Invoke());

        await Task.WhenAll(_receiveTask, _audioSendTask).ConfigureAwait(false);
    }

    private async Task AttemptReconnectAsync()
    {
        _reconnectAttempts++;
        InterviewAssistMetrics.RecordReconnection();
        var delay = _options.ReconnectBaseDelayMs * (int)Math.Pow(2, _reconnectAttempts - 1);
        delay = Math.Min(delay, QueueConstants.MaxReconnectDelayMs);

        _logger.LogWarning("[{CorrelationId}] Reconnection attempt {Attempt}/{Max} in {Delay}ms",
            _correlationId, _reconnectAttempts, _options.MaxReconnectAttempts, delay);

        SafeRaise(() => OnReconnecting?.Invoke());

        await Task.Delay(delay, _externalCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CleanupConnectionAsync()
    {
        InterviewAssistMetrics.RecordDisconnection();

        // Save session state if configured
        await SaveSessionStateAsync().ConfigureAwait(false);

        try { _audio?.Stop(); } catch { }

        if (_audioHandler != null)
        {
            try { if (_audio != null) _audio.OnAudioChunk -= _audioHandler; } catch { }
            _audioHandler = null;
        }

        try { _audioChannel?.Writer.TryComplete(); } catch { }
        try { _cts?.Cancel(); } catch { }

        if (_receiveTask != null)
        {
            try { await _receiveTask.ConfigureAwait(false); } catch { }
            _receiveTask = null;
        }

        if (_audioSendTask != null)
        {
            try { await _audioSendTask.ConfigureAwait(false); } catch { }
            _audioSendTask = null;
        }

        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                try { _ws.Dispose(); } catch { }
                _ws = null;
            }
        }

        try { _cts?.Dispose(); } catch { }
        _cts = null;

        lock (_funcSync)
        {
            _functionCallBuffers.Clear();
            _functionCallNames.Clear();
            _pendingFunctionParse.Clear();
        }

        Interlocked.Exchange(ref _pendingAudioBytes, 0);
        Volatile.Write(ref _hasUncommittedAudio, 0);
        Volatile.Write(ref _responseActive, 0);
        Volatile.Write(ref _responseRequested, 0);

        _eventDispatcher.Stop();
    }
    #endregion

    #region Session Configuration
    private async Task SendSessionConfig()
    {
        var baseInstructions = SystemInstructionsLoader.Load(
            _options.SystemInstructionsFactory,
            _options.SystemInstructionsFilePath,
            _options.SystemInstructions);
        var instr = baseInstructions.Replace("\r\n", "\\n").Replace("\n", "\\n");

        if (!string.IsNullOrWhiteSpace(_options.ExtraInstructions))
        {
            var sanitized = _options.ExtraInstructions.Replace("\r\n", "\n");
            if (sanitized.Length > _options.MaxInstructionChars)
            {
                sanitized = sanitized[.._options.MaxInstructionChars];
            }
            instr += "\\n\\nInterview context:\\n" + sanitized;
        }

        var config = new
        {
            type = "session.update",
            session = new
            {
                modalities = new[] { "text", "audio" },
                instructions = instr,
                voice = _options.Voice,
                input_audio_format = "pcm16",
                output_audio_format = "pcm16",
                input_audio_transcription = new { model = _options.TranscriptionModel },
                turn_detection = new
                {
                    type = "server_vad",
                    threshold = _options.VadThreshold,
                    prefix_padding_ms = _options.PrefixPaddingMs,
                    silence_duration_ms = _options.SilenceDurationMs
                },
                tools = new[]
                {
                    new
                    {
                        type = "function",
                        name = "report_technical_response",
                        description = "Answer programming questions. MUST include both 'answer' and 'console_code' parameters - never omit console_code.",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                answer = new { type = "string", description = "Explanation of the concept" },
                                console_code = new { type = "string", description = "Complete C# console application code. REQUIRED - must always be provided. Use '// No code needed' if not applicable." }
                            },
                            required = new[] { "answer", "console_code" }
                        }
                    }
                },
                tool_choice = "auto"
            }
        };

        _logger.LogDebug("Sending session configuration");
        await SendMessage(config).ConfigureAwait(false);
    }

    private async Task SendContextChunksAsync(IReadOnlyList<ContextChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            var itemCreate = new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "message",
                    role = "user",
                    content = new object[] { new { type = "input_text", text = $"[CONTEXT] {chunk.Label}\n{chunk.Text}" } }
                }
            };
            await SendMessage(itemCreate).ConfigureAwait(false);
        }

        _logger.LogInformation("Uploaded {Count} context chunk(s) to session", chunks.Count);
        SafeRaise(() => OnInfo?.Invoke($"Uploaded {chunks.Count} context chunk(s) to session."));
    }
    #endregion

    #region Audio Handling
    private void SetupAudioInput()
    {
        if (_audioChannel == null)
        {
            throw new InvalidOperationException("Audio channel not initialized.");
        }

        _audioHandler = bytes =>
        {
            if (_quotaExhausted || _rateLimited) return;

            // Check for backpressure before write
            var readerCount = _audioChannel.Reader.Count;
            var backpressureThreshold = AudioConstants.AudioChannelCapacity - 2;

            if (readerCount >= backpressureThreshold)
            {
                InterviewAssistMetrics.RecordBackpressureWarning();
                InterviewAssistMetrics.SetQueueDepth(readerCount);
                _logger.LogWarning("[{CorrelationId}] Audio backpressure warning: queue depth {Depth}/{Capacity}, chunk size {Size}",
                    _correlationId, readerCount, AudioConstants.AudioChannelCapacity, bytes.Length);
                SafeRaise(() => OnBackpressure?.Invoke(readerCount));
            }

            if (!_audioChannel.Writer.TryWrite(bytes))
            {
                InterviewAssistMetrics.RecordAudioChunkDropped();
                _logger.LogWarning("[{CorrelationId}] Audio queue full; dropping chunk", _correlationId);
                SafeRaise(() => OnWarning?.Invoke("Audio queue full; dropping audio chunk."));
            }
        };

        _audio.OnAudioChunk += _audioHandler;
        _audio.Start();

        _logger.LogInformation("Audio capture active ({Source})", _audio.GetSource());
        SafeRaise(() => OnInfo?.Invoke($"AudioCaptureService active ({_audio.GetSource()})"));
    }

    private async Task AudioSendLoop(CancellationToken ct)
    {
        if (_audioChannel == null) return;

        try
        {
            await foreach (var audioData in _audioChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (_quotaExhausted || _rateLimited) continue;

                Interlocked.Add(ref _pendingAudioBytes, audioData.Length);
                Volatile.Write(ref _hasUncommittedAudio, 1);
                var base64Audio = Convert.ToBase64String(audioData);
                var msg = new { type = "input_audio_buffer.append", audio = base64Audio };
                await SendMessage(msg).ConfigureAwait(false);
                InterviewAssistMetrics.RecordAudioChunkProcessed();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio send loop error");
            SafeRaise(() => OnError?.Invoke(ex));
        }
    }
    #endregion

    #region Message Sending
    private async Task SendMessage(object message)
    {
        try
        {
            if (_ws == null || _cts == null)
                throw new InvalidOperationException("Realtime socket is not connected.");

            await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                var json = JsonSerializer.Serialize(message, s_jsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message");
            SafeRaise(() => OnError?.Invoke(ex));
        }
    }
    #endregion

    #region Response Processing
    private async Task ReceiveResponsesLoop(CancellationToken ct)
    {
        var buffer = new byte[RealtimeConstants.WebSocketBufferSize];
        var messageBuilder = new StringBuilder();

        try
        {
            if (_ws == null) return;

            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogWarning("Socket closed: {Status} {Description}",
                        result.CloseStatus, result.CloseStatusDescription);
                    SafeRaise(() => OnWarning?.Invoke($"Socket closed: {result.CloseStatus} {result.CloseStatusDescription}"));
                    break;
                }

                var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                messageBuilder.Append(chunk);
                _logger.LogTrace("RAW: {Chunk}", chunk);

                if (result.EndOfMessage)
                {
                    ProcessResponse(messageBuilder.ToString());
                    messageBuilder.Clear();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Receive loop error");
            SafeRaise(() => OnError?.Invoke(ex));
        }
    }

    private void ProcessResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var eventType)) return;
            var type = eventType.GetString() ?? string.Empty;

            switch (type)
            {
                case "session.created":
                case "session.updated":
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    if (root.TryGetProperty("transcript", out var transcript))
                    {
                        var text = transcript.GetString() ?? string.Empty;
                        _logger.LogDebug("User transcript: {Text}", text);
                        TrackRecentTranscript(text);
                        SafeRaise(() => OnUserTranscript?.Invoke(text));
                    }
                    break;

                case "conversation.item.input_audio_transcription.failed":
                    HandleTranscriptionFailed(root);
                    break;

                case "input_audio_buffer.speech_started":
                    SafeRaise(() => OnSpeechStarted?.Invoke());
                    break;

                case "input_audio_buffer.speech_stopped":
                    SafeRaise(() => OnSpeechStopped?.Invoke());
                    FinalizeTurnAndMaybeRespond();
                    break;

                case "response.function_call_arguments.delta":
                    HandleFunctionCallDelta(root);
                    break;

                case "response.function_call_arguments.done":
                    HandleFunctionCallDone(root);
                    break;

                case "response.text.delta":
                    if (root.TryGetProperty("delta", out var textDelta))
                    {
                        SafeRaise(() => OnAssistantTextDelta?.Invoke(textDelta.GetString() ?? string.Empty));
                    }
                    break;

                case "response.text.done":
                    SafeRaise(() => OnAssistantTextDone?.Invoke());
                    break;

                case "response.audio_transcript.delta":
                    if (root.TryGetProperty("delta", out var audioDelta))
                    {
                        SafeRaise(() => OnAssistantAudioTranscriptDelta?.Invoke(audioDelta.GetString() ?? string.Empty));
                    }
                    break;

                case "response.audio_transcript.done":
                    SafeRaise(() => OnAssistantAudioTranscriptDone?.Invoke());
                    break;

                case "response.done":
                    HandleResponseDone(root);
                    break;

                case "error":
                    HandleErrorEvent(root);
                    break;

                case "response.audio.delta":
                case "input_audio_buffer.committed":
                case "input_audio_buffer.cleared":
                case "conversation.item.created":
                case "response.created":
                case "response.output_item.added":
                case "response.output_item.done":
                case "response.content_part.added":
                case "response.content_part.done":
                    break;

                default:
                    _logger.LogDebug("Unhandled event: {Type}", type);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error");
            SafeRaise(() => OnError?.Invoke(ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Response processing error");
            SafeRaise(() => OnError?.Invoke(ex));
        }
    }
    #endregion

    #region Response Coordination
    private void RequestResponse()
    {
        Volatile.Write(ref _responseRequested, 1);
        _ = RunResponseCoordinatorAsync();
    }

    private async Task RunResponseCoordinatorAsync()
    {
        if (!await _responseCoordinatorLock.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            if (Volatile.Read(ref _responseRequested) != 1) return;
            if (Volatile.Read(ref _responseActive) == 1) return;

            Volatile.Write(ref _responseRequested, 0);
            // Set active BEFORE sending to prevent race where another speech_stopped
            // triggers a second response.create before this one completes
            Volatile.Write(ref _responseActive, 1);
            Interlocked.Exchange(ref _responseStartTimestamp, Stopwatch.GetTimestamp());
            await SendMessage(new { type = "response.create" }).ConfigureAwait(false);
        }
        finally
        {
            _responseCoordinatorLock.Release();
        }
    }

    private void FinalizeTurnAndMaybeRespond()
    {
        _ = FinalizeTurnAndMaybeRespondAsync();
    }

    private async Task FinalizeTurnAndMaybeRespondAsync()
    {
        try
        {
            // Check if there's uncommitted audio to prevent committing an empty buffer
            if (Interlocked.Exchange(ref _hasUncommittedAudio, 0) == 0)
            {
                return;
            }

            var pending = Interlocked.Read(ref _pendingAudioBytes);
            if (pending <= 0) return;

            if (pending < MinCommitBytes)
            {
                int pad = (int)(MinCommitBytes - pending);
                if (pad > 0)
                {
                    var silence = new byte[pad];
                    var base64 = Convert.ToBase64String(silence);
                    await SendMessage(new { type = "input_audio_buffer.append", audio = base64 }).ConfigureAwait(false);
                    Interlocked.Add(ref _pendingAudioBytes, pad);
                }
            }

            await SendMessage(new { type = "input_audio_buffer.commit" }).ConfigureAwait(false);
            await SendMessage(new { type = "input_audio_buffer.clear" }).ConfigureAwait(false);
            Interlocked.Exchange(ref _pendingAudioBytes, 0);

            RequestResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Turn finalization error");
            SafeRaise(() => OnError?.Invoke(ex));
        }
    }
    #endregion

    #region Event Handlers
    private void HandleTranscriptionFailed(JsonElement root)
    {
        string msg = "";
        if (root.TryGetProperty("error", out var err))
        {
            msg = err.TryGetProperty("message", out var m) ? (m.GetString() ?? "transcription failed") : "transcription failed";
        }

        _logger.LogWarning("Transcription failed: {Message}", msg);
        SafeRaise(() => OnWarning?.Invoke($"Transcription failed: {msg}"));

        if (!string.IsNullOrEmpty(msg) && msg.Contains("429"))
        {
            HandleRateLimit("Transcription rate limit");
        }
    }

    private void HandleFunctionCallDelta(JsonElement root)
    {
        if (!root.TryGetProperty("call_id", out var callIdProp)) return;
        var callId = callIdProp.GetString() ?? string.Empty;
        if (string.IsNullOrEmpty(callId)) return;

        lock (_funcSync)
        {
            if (!_functionCallBuffers.ContainsKey(callId))
            {
                _functionCallBuffers[callId] = new StringBuilder();
            }

            if (root.TryGetProperty("delta", out var delta))
            {
                _functionCallBuffers[callId].Append(delta.GetString() ?? string.Empty);
            }

            if (root.TryGetProperty("name", out var name))
            {
                _functionCallNames[callId] = name.GetString() ?? string.Empty;
            }
        }
    }

    private void HandleFunctionCallDone(JsonElement root)
    {
        var doneCallId = root.TryGetProperty("call_id", out var callIdProp) ? callIdProp.GetString() ?? "" : "";
        var functionName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(functionName) && !string.IsNullOrEmpty(doneCallId))
        {
            lock (_funcSync)
            {
                _functionCallNames.TryGetValue(doneCallId, out functionName);
                functionName ??= "";
            }
        }

        string raw;
        lock (_funcSync)
        {
            if (string.IsNullOrEmpty(doneCallId) || !_functionCallBuffers.TryGetValue(doneCallId, out var sb))
                return;
            raw = sb.ToString();
        }

        if (string.IsNullOrWhiteSpace(raw)) return;

        if (!IsCompleteJson(raw))
        {
            _logger.LogWarning("Incomplete function args, queuing for retry");
            SafeRaise(() => OnWarning?.Invoke("Incomplete function args – waiting for remaining deltas..."));

            lock (_funcSync) { _pendingFunctionParse.Add(doneCallId); }

            _ = RetryParseFunctionArgsAsync(doneCallId, functionName);
        }
        else
        {
            try
            {
                ParseFunctionArgs(raw, functionName);
                SaveRawJsonToFile(functionName, Guid.NewGuid().ToString(), raw);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Function args parse error");
                SafeRaise(() => OnError?.Invoke(ex));
            }
            finally
            {
                lock (_funcSync)
                {
                    _functionCallBuffers.Remove(doneCallId);
                    _functionCallNames.Remove(doneCallId);
                }
            }
        }
    }

    private async Task RetryParseFunctionArgsAsync(string callId, string functionName)
    {
        try
        {
            await Task.Delay(RealtimeConstants.FunctionParseRetryDelayMs, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            string retry;
            lock (_funcSync)
            {
                if (!_functionCallBuffers.TryGetValue(callId, out var sb)) return;
                retry = sb.ToString();
            }

            if (string.IsNullOrEmpty(retry)) return;

            if (!IsCompleteJson(retry))
            {
                _logger.LogWarning("Still incomplete after retry, attempting repair");
                retry = retry.Trim();
                if (!retry.StartsWith("{")) retry = "{" + retry;
                if (!retry.EndsWith("}")) retry += "}";
            }

            ParseFunctionArgs(retry, functionName);
            SaveRawJsonToFile(functionName, Guid.NewGuid().ToString(), retry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry parse error");
            SafeRaise(() => OnError?.Invoke(ex));
        }
        finally
        {
            lock (_funcSync)
            {
                _pendingFunctionParse.Remove(callId);
                _functionCallBuffers.Remove(callId);
                _functionCallNames.Remove(callId);
            }
        }
    }

    private void HandleResponseDone(JsonElement root)
    {
        // Record response latency
        var startTimestamp = Interlocked.Read(ref _responseStartTimestamp);
        if (startTimestamp > 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            InterviewAssistMetrics.RecordResponseLatency(elapsed.TotalMilliseconds);
        }

        // Process any pending function parses
        FlushPendingFunctionParses();

        // Mark response as complete
        Volatile.Write(ref _responseActive, 0);

        // Check if we owe another response
        if (Volatile.Read(ref _responseRequested) != 0)
        {
            _ = RunResponseCoordinatorAsync();
        }

        // Check for failures
        if (root.TryGetProperty("response", out var resp))
        {
            var status = resp.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "" : "";
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                HandleResponseFailure(resp);
            }
        }
    }

    private void FlushPendingFunctionParses()
    {
        List<string> pending;
        lock (_funcSync)
        {
            if (_pendingFunctionParse.Count == 0) return;
            pending = new List<string>(_pendingFunctionParse);
        }

        foreach (var call in pending)
        {
            string txt, name;
            lock (_funcSync)
            {
                if (!_functionCallBuffers.TryGetValue(call, out var sb)) continue;
                txt = sb.ToString();
                name = _functionCallNames.TryGetValue(call, out var n) ? n : "";
            }

            if (string.IsNullOrEmpty(txt)) continue;

            try
            {
                var attempt = txt.Trim();
                if (!IsCompleteJson(attempt))
                {
                    if (!attempt.StartsWith("{")) attempt = "{" + attempt;
                    if (!attempt.EndsWith("}")) attempt += "}";
                }

                ParseFunctionArgs(attempt, name);
                SaveRawJsonToFile(name, Guid.NewGuid().ToString(), attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flush parse error");
                SafeRaise(() => OnError?.Invoke(ex));
            }
            finally
            {
                lock (_funcSync)
                {
                    _pendingFunctionParse.Remove(call);
                    _functionCallBuffers.Remove(call);
                    _functionCallNames.Remove(call);
                }
            }
        }
    }

    private void HandleResponseFailure(JsonElement resp)
    {
        if (!resp.TryGetProperty("status_details", out var details) ||
            !details.TryGetProperty("error", out var err))
            return;

        var code = err.TryGetProperty("code", out var codeProp) ? codeProp.GetString() ?? "" : "";
        var message = err.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "response failed" : "response failed";

        _logger.LogWarning("Response failed: code={Code}, message={Message}", code, message);
        SafeRaise(() => OnWarning?.Invoke($"Response failed code={code} message={message}"));

        if (string.Equals(code, "insufficient_quota", StringComparison.OrdinalIgnoreCase))
        {
            HandleQuotaExhausted(message);
        }
        else if (message.Contains("429") && !_quotaExhausted)
        {
            HandleRateLimit(message);
        }
    }

    private void HandleErrorEvent(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error))
        {
            SafeRaise(() => OnWarning?.Invoke("Error event without details."));
            return;
        }

        var type = error.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        var code = error.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "Unknown error" : "Unknown error";
        var param = error.TryGetProperty("param", out var p) ? p.GetString() ?? "" : "";

        _logger.LogWarning("[{CorrelationId}] API error: type={Type}, code={Code}, param={Param}, message={Message}",
            _correlationId, type, code, param, message);
        SafeRaise(() => OnWarning?.Invoke($"API error type={type} code={code} param={param} message={message}"));

        // Fatal errors
        if (string.Equals(type, "authentication_error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "permission_error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "model_not_found", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Fatal provider error: {Type}/{Code}. {Message}", type, code, message);
            SafeRaise(() => OnError?.Invoke(new InvalidOperationException($"Fatal provider error: {type}/{code}. {message}")));
            try { _audio?.Stop(); } catch { }
            _cts?.Cancel();
            return;
        }

        if (string.Equals(code, "insufficient_quota", StringComparison.OrdinalIgnoreCase))
        {
            HandleQuotaExhausted(message);
            return;
        }

        if (string.Equals(type, "rate_limit_error", StringComparison.OrdinalIgnoreCase) || message.Contains("429"))
        {
            HandleRateLimit(message);
            return;
        }

        // Non-fatal flow control errors
        if (string.Equals(code, "conversation_already_has_active_response", StringComparison.OrdinalIgnoreCase))
        {
            Volatile.Write(ref _responseRequested, 1);
            return;
        }

        if (string.Equals(code, "input_audio_buffer_commit_empty", StringComparison.OrdinalIgnoreCase))
        {
            return; // Ignore
        }

        if (string.Equals(type, "invalid_request_error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "unsupported_modalities", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "tool_validation_error", StringComparison.OrdinalIgnoreCase))
        {
            SafeRaise(() => OnError?.Invoke(new InvalidOperationException($"Request/feature error: {type}/{code}. {message} (param={param})")));
            return;
        }

        if (string.Equals(type, "server_error", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Transient server error - will attempt recovery");
            SafeRaise(() => OnWarning?.Invoke("Transient server_error – consider retry/backoff."));
        }
    }
    #endregion

    #region Rate Limiting and Quota
    private void HandleQuotaExhausted(string message)
    {
        if (_quotaExhausted) return;
        _quotaExhausted = true;

        InterviewAssistMetrics.RecordQuotaExhausted();
        _logger.LogError("[{CorrelationId}] Quota exhausted - stopping session", _correlationId);
        SafeRaise(() => OnWarning?.Invoke("Quota exhausted – stopping audio capture and cancelling session."));

        try { _audio?.Stop(); } catch { }
        SafeRaise(() => OnError?.Invoke(new InvalidOperationException($"OpenAI insufficient_quota: {message}")));
        _cts?.Cancel();
    }

    private void HandleRateLimit(string context)
    {
        if (_quotaExhausted || _rateLimited) return;

        InterviewAssistMetrics.RecordRateLimitHit();
        _logger.LogWarning("[{CorrelationId}] Rate limit hit: {Context}", _correlationId, context);
        SafeRaise(() => OnWarning?.Invoke($"Rate limit detected – pausing audio. ({context})"));

        _rateLimited = true;
        try { _audio?.Stop(); } catch { }

        if (_options.EnableRateLimitRecovery)
        {
            InitializeCircuitBreakerIfNeeded();
            _rateLimitCircuitBreaker!.RecordRateLimit();
        }
    }

    private void InitializeCircuitBreakerIfNeeded()
    {
        if (_rateLimitCircuitBreaker != null) return;

        _rateLimitCircuitBreaker = new RateLimitCircuitBreaker(
            _options.RateLimitRecoveryDelayMs,
            _options.MaxReconnectDelayMs,
            _logger);

        _rateLimitCircuitBreaker.OnHalfOpen += () => ResumeAfterRateLimit();
        _rateLimitCircuitBreaker.OnRecoveryScheduled += delay =>
            SafeRaise(() => OnInfo?.Invoke($"Audio will resume in {delay / 1000} seconds..."));
        _rateLimitCircuitBreaker.OnClosed += () =>
            _logger.LogInformation("Rate limit circuit breaker closed - normal operation resumed");
    }

    private void ResumeAfterRateLimit()
    {
        if (_quotaExhausted || _started == 0) return;

        _rateLimited = false;
        _logger.LogInformation("Resuming after rate limit");
        SafeRaise(() => OnInfo?.Invoke("Resuming audio capture after rate limit pause."));

        try
        {
            _audio?.Start();
            _rateLimitCircuitBreaker?.RecordSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume audio after rate limit");
            SafeRaise(() => OnError?.Invoke(ex));
        }
    }
    #endregion

    #region JSON Parsing
    private void ParseFunctionArgs(string json, string functionName)
    {
        var fixedJson = FixMalformedJson(json);
        JsonElement args;
        JsonDocument? argsDoc = null;

        try
        {
            argsDoc = JsonDocument.Parse(fixedJson);
            args = argsDoc.RootElement;
        }
        catch (Exception)
        {
            var repaired = JsonRepairUtility.Repair(json);
            if (!string.IsNullOrEmpty(repaired))
            {
                argsDoc = JsonDocument.Parse(repaired);
                args = argsDoc.RootElement;
            }
            else
            {
                argsDoc?.Dispose();
                throw;
            }
        }

        var answerText = args.TryGetProperty("answer", out var answer) ? answer.GetString() ?? "" : "";
        var codeText = args.TryGetProperty("console_code", out var code) ? code.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(codeText) && !string.IsNullOrWhiteSpace(answerText))
        {
            _logger.LogWarning("No console_code provided - extracting from answer");
            SafeRaise(() => OnWarning?.Invoke("No console_code provided - attempting to extract from answer."));
            var (_, extractedCode) = ExtractCodeFromText(answerText);
            codeText = extractedCode;
        }

        _logger.LogDebug("Function call response: {FunctionName}, answer length: {AnswerLen}, code length: {CodeLen}",
            functionName, answerText.Length, codeText.Length);

        SafeRaise(() => OnFunctionCallResponse?.Invoke(functionName ?? string.Empty, answerText, codeText));
        argsDoc?.Dispose();
    }

    private static string FixMalformedJson(string json)
    {
        var result = new StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped) { result.Append(c); escaped = false; continue; }
            if (c == '\\') { result.Append(c); escaped = true; continue; }
            if (c == '"') { result.Append(c); inString = !inString; continue; }

            if (inString)
            {
                result.Append(c switch
                {
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => c.ToString()
                });
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static bool IsCompleteJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        var trimmed = json.TrimEnd();
        bool inString = false;
        bool escaped = false;
        int depth = 0;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }

        return !inString && depth == 0 && trimmed.EndsWith("}");
    }

    private static (string explanation, string code) ExtractCodeFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("", "");
        var match = s_codeBlockRegex.Match(text);
        if (match.Success)
        {
            var code = match.Groups[1].Value.Trim();
            var explanation = s_codeBlockRegex.Replace(text, "\n[CODE EXTRACTED]\n").Trim();
            return (explanation, code);
        }
        return (text, "");
    }
    #endregion

    #region File Operations
    private void SaveRawJsonToFile(string functionName, string callId, string rawJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return;
            var safeFunc = string.IsNullOrWhiteSpace(functionName) ? "unknown" : MakeFileNameSafe(functionName);
            var safeCall = string.IsNullOrWhiteSpace(callId) ? "nocallid" : MakeFileNameSafe(callId);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
            var baseName = $"{safeFunc}_{safeCall}_{timestamp}";
            var dir = Path.Combine(AppContext.BaseDirectory, "function-args-logs");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, baseName + ".json");
            int suffix = 0;
            while (File.Exists(path))
            {
                suffix++;
                path = Path.Combine(dir, $"{baseName}_{suffix}.json");
            }

            File.WriteAllText(path, rawJson);
            _logger.LogDebug("Saved raw function args to: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save function args to file");
        }
    }

    private static string MakeFileNameSafe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
    #endregion

    #region Event Dispatcher
    private void SafeRaise(Action action) => _eventDispatcher.Raise(action);
    #endregion

    #region Session State Tracking
    private void TrackRecentTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;

        lock (_transcriptLock)
        {
            _recentTranscripts.Enqueue(transcript);

            // Keep only the most recent transcripts
            while (_recentTranscripts.Count > RealtimeConstants.MaxRecentTranscripts)
            {
                _recentTranscripts.Dequeue();
            }
        }
    }

    private async Task SaveSessionStateAsync()
    {
        if (_options.OnShutdownSaveState == null) return;

        try
        {
            IReadOnlyList<string> transcripts;
            lock (_transcriptLock)
            {
                transcripts = _recentTranscripts.ToList();
            }

            var sessionState = new SessionState(
                _correlationId,
                transcripts,
                _sessionStartTime,
                DateTime.UtcNow);

            _logger.LogInformation("[{CorrelationId}] Saving session state: {TranscriptCount} transcripts, duration {Duration}",
                _correlationId, transcripts.Count, sessionState.Duration);

            await _options.OnShutdownSaveState(sessionState).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Don't let state saving failure prevent shutdown
            _logger.LogError(ex, "[{CorrelationId}] Failed to save session state", _correlationId);
        }
    }
    #endregion
}
