using InterviewAssist.Library.Audio;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Web;

namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Deepgram streaming transcription service with native interim/final results support.
/// </summary>
public sealed class DeepgramTranscriptionService : IStreamingTranscriptionService
{
    private readonly IAudioCaptureService _audio;
    private readonly DeepgramOptions _options;
    private readonly StringBuilder _stableTranscript = new();
    private readonly object _transcriptLock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Channel<byte[]>? _audioChannel;
    private Action<byte[]>? _audioHandler;
    private Task? _receiveTask;
    private Task? _sendTask;
    private Task? _keepAliveTask;
    private int _started;
    private long _streamOffsetMs;

    // Track provisional text for revision detection
    private string _lastProvisionalText = string.Empty;
    private string _currentProvisionalText = string.Empty;

    public event Action<StableTextEventArgs>? OnStableText;
    public event Action<ProvisionalTextEventArgs>? OnProvisionalText;
    public event Action<HypothesisEventArgs>? OnFullHypothesis;
    public event Action<string>? OnInfo;
    public event Action<string>? OnWarning;
    public event Action<Exception>? OnError;

    public DeepgramTranscriptionService(
        IAudioCaptureService audioCaptureService,
        DeepgramOptions options)
    {
        _audio = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("Service already started.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _streamOffsetMs = 0;

        // Set up audio channel
        _audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        // Connect to Deepgram
        await ConnectWebSocketAsync(_cts.Token).ConfigureAwait(false);

        // Set up audio capture
        _audioHandler = bytes =>
        {
            if (_audioChannel?.Writer.TryWrite(bytes) == false)
            {
                OnWarning?.Invoke("Audio queue full; dropping chunk.");
            }
        };

        _audio.OnAudioChunk += _audioHandler;
        _audio.Start();

        OnInfo?.Invoke($"DeepgramTranscriptionService active ({_audio.GetSource()})");

        // Start background tasks
        _receiveTask = ReceiveLoopAsync(_cts.Token);
        _sendTask = SendLoopAsync(_cts.Token);
        _keepAliveTask = KeepAliveLoopAsync(_cts.Token);

        // Wait for cancellation
        try
        {
            await Task.WhenAll(_receiveTask, _sendTask, _keepAliveTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        // Stop audio capture
        try { _audio?.Stop(); } catch { }
        if (_audioHandler != null)
        {
            try { _audio.OnAudioChunk -= _audioHandler; } catch { }
            _audioHandler = null;
        }

        // Complete audio channel
        try { _audioChannel?.Writer.TryComplete(); } catch { }

        // Cancel tasks
        try { _cts?.Cancel(); } catch { }

        // Wait for tasks to complete
        var tasks = new List<Task>();
        if (_receiveTask != null) tasks.Add(_receiveTask);
        if (_sendTask != null) tasks.Add(_sendTask);
        if (_keepAliveTask != null) tasks.Add(_keepAliveTask);

        if (tasks.Count > 0)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
        }

        // Send close message and close WebSocket
        await CloseWebSocketAsync().ConfigureAwait(false);

        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    public string GetStableTranscript()
    {
        lock (_transcriptLock)
        {
            return _stableTranscript.ToString();
        }
    }

    public string GetProvisionalTranscript()
    {
        lock (_transcriptLock)
        {
            return _currentProvisionalText;
        }
    }

    private async Task ConnectWebSocketAsync(CancellationToken ct)
    {
        _ws = new ClientWebSocket();

        // Set authorization header
        _ws.Options.SetRequestHeader("Authorization", $"Token {_options.ApiKey}");

        // Build query string with options
        var queryParams = BuildQueryString();
        var uri = new Uri($"{_options.WebSocketUrl}?{queryParams}");

        OnInfo?.Invoke($"Connecting to Deepgram ({_options.Model})...");

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_options.ConnectTimeoutMs);

        try
        {
            await _ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
            OnInfo?.Invoke("Connected to Deepgram");
        }
        catch (Exception ex)
        {
            OnError?.Invoke(new InvalidOperationException($"Failed to connect to Deepgram: {ex.Message}", ex));
            throw;
        }
    }

    private string BuildQueryString()
    {
        var parameters = new List<string>
        {
            $"model={HttpUtility.UrlEncode(_options.Model)}",
            $"language={HttpUtility.UrlEncode(_options.Language)}",
            $"sample_rate={_options.SampleRate}",
            $"encoding={HttpUtility.UrlEncode(_options.Encoding)}",
            $"channels={_options.Channels}",
            $"interim_results={_options.InterimResults.ToString().ToLowerInvariant()}",
            $"punctuate={_options.Punctuate.ToString().ToLowerInvariant()}",
            $"smart_format={_options.SmartFormat.ToString().ToLowerInvariant()}",
            $"vad_events={_options.Vad.ToString().ToLowerInvariant()}"
        };

        if (_options.EndpointingMs > 0)
        {
            parameters.Add($"endpointing={_options.EndpointingMs}");
        }

        if (_options.UtteranceEndMs > 0 && _options.InterimResults)
        {
            parameters.Add($"utterance_end_ms={_options.UtteranceEndMs}");
        }

        if (_options.Diarize)
        {
            parameters.Add("diarize=true");
        }

        if (!string.IsNullOrWhiteSpace(_options.Keywords))
        {
            // Split keywords and add each one
            var keywords = _options.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var keyword in keywords)
            {
                parameters.Add($"keywords={HttpUtility.UrlEncode(keyword)}");
            }
        }

        return string.Join("&", parameters);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_ws == null) return;

        var buffer = new byte[8192];
        var messageBuffer = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                messageBuffer.SetLength(0);

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnInfo?.Invoke("Deepgram closed connection");
                        return;
                    }

                    await messageBuffer.WriteAsync(buffer.AsMemory(0, result.Count), ct).ConfigureAwait(false);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    ProcessMessage(json);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            OnWarning?.Invoke("Deepgram connection closed prematurely");
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
        finally
        {
            messageBuffer.Dispose();
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                // Results message (most common, doesn't always have "type")
                if (root.TryGetProperty("channel", out _))
                {
                    ProcessTranscriptionResult(root);
                }
                return;
            }

            var messageType = typeProp.GetString();

            switch (messageType)
            {
                case "Results":
                    ProcessTranscriptionResult(root);
                    break;

                case "Metadata":
                    if (root.TryGetProperty("request_id", out var reqId))
                    {
                        OnInfo?.Invoke($"Deepgram session: {reqId.GetString()}");
                    }
                    break;

                case "UtteranceEnd":
                    // Utterance ended - any pending provisional should be considered final
                    PromoteProvisionalToStable();
                    OnInfo?.Invoke("Utterance end detected");
                    break;

                case "SpeechStarted":
                    OnInfo?.Invoke("Speech started");
                    break;

                case "Error":
                    var errorMsg = root.TryGetProperty("message", out var msg)
                        ? msg.GetString()
                        : "Unknown Deepgram error";
                    OnError?.Invoke(new InvalidOperationException($"Deepgram error: {errorMsg}"));
                    break;
            }
        }
        catch (JsonException ex)
        {
            OnWarning?.Invoke($"Failed to parse Deepgram message: {ex.Message}");
        }
    }

    private void ProcessTranscriptionResult(JsonElement root)
    {
        // Check if this is a final or interim result
        var isFinal = root.TryGetProperty("is_final", out var finalProp) && finalProp.GetBoolean();
        var speechFinal = root.TryGetProperty("speech_final", out var speechProp) && speechProp.GetBoolean();

        // Get transcript text and speaker info
        string transcript = string.Empty;
        double confidence = 0;
        int? speaker = null;

        if (root.TryGetProperty("channel", out var channel) &&
            channel.TryGetProperty("alternatives", out var alternatives) &&
            alternatives.GetArrayLength() > 0)
        {
            var firstAlt = alternatives[0];
            transcript = firstAlt.TryGetProperty("transcript", out var transProp)
                ? transProp.GetString() ?? string.Empty
                : string.Empty;
            confidence = firstAlt.TryGetProperty("confidence", out var confProp)
                ? confProp.GetDouble()
                : 0;

            // Extract speaker from first word (diarization)
            if (firstAlt.TryGetProperty("words", out var words) && words.GetArrayLength() > 0)
            {
                var firstWord = words[0];
                if (firstWord.TryGetProperty("speaker", out var speakerProp))
                {
                    speaker = speakerProp.GetInt32();
                }
            }
        }

        // Skip empty transcripts
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return;
        }

        // Get timing info
        var startTime = root.TryGetProperty("start", out var startProp) ? startProp.GetDouble() : 0;
        var duration = root.TryGetProperty("duration", out var durProp) ? durProp.GetDouble() : 0;
        _streamOffsetMs = (long)((startTime + duration) * 1000);

        if (isFinal)
        {
            // Final result - add to stable transcript
            lock (_transcriptLock)
            {
                if (_stableTranscript.Length > 0 && !_stableTranscript.ToString().EndsWith(" "))
                {
                    _stableTranscript.Append(' ');
                }
                _stableTranscript.Append(transcript.Trim());
                _currentProvisionalText = string.Empty;
            }

            OnStableText?.Invoke(new StableTextEventArgs
            {
                Text = transcript.Trim(),
                StreamOffsetMs = _streamOffsetMs,
                Timestamp = DateTime.UtcNow,
                ConfirmationCount = 1,
                Speaker = speaker
            });

            _lastProvisionalText = string.Empty;
        }
        else
        {
            // Interim result - update provisional
            lock (_transcriptLock)
            {
                _currentProvisionalText = transcript.Trim();
            }

            OnProvisionalText?.Invoke(new ProvisionalTextEventArgs
            {
                Text = transcript.Trim(),
                Confidence = confidence,
                StreamOffsetMs = _streamOffsetMs,
                Timestamp = DateTime.UtcNow,
                Speaker = speaker
            });

            _lastProvisionalText = transcript.Trim();
        }

        // Emit full hypothesis
        EmitHypothesis();

        // If speech_final is true, this is a good breakpoint
        if (speechFinal)
        {
            OnInfo?.Invoke("Speech final (endpointing)");
        }
    }

    private void PromoteProvisionalToStable()
    {
        string provisional;
        lock (_transcriptLock)
        {
            provisional = _currentProvisionalText;
            if (string.IsNullOrWhiteSpace(provisional))
            {
                return;
            }

            if (_stableTranscript.Length > 0 && !_stableTranscript.ToString().EndsWith(" "))
            {
                _stableTranscript.Append(' ');
            }
            _stableTranscript.Append(provisional);
            _currentProvisionalText = string.Empty;
        }

        OnStableText?.Invoke(new StableTextEventArgs
        {
            Text = provisional,
            StreamOffsetMs = _streamOffsetMs,
            Timestamp = DateTime.UtcNow,
            ConfirmationCount = 1
        });

        _lastProvisionalText = string.Empty;
        EmitHypothesis();
    }

    private void EmitHypothesis()
    {
        string stableText, provisionalText;
        lock (_transcriptLock)
        {
            stableText = _stableTranscript.ToString();
            provisionalText = _currentProvisionalText;
        }

        var fullText = string.IsNullOrWhiteSpace(provisionalText)
            ? stableText
            : $"{stableText} {provisionalText}".Trim();

        var totalLength = fullText.Length;
        var stabilityRatio = totalLength > 0 ? (double)stableText.Length / totalLength : 1.0;

        OnFullHypothesis?.Invoke(new HypothesisEventArgs
        {
            FullText = fullText,
            StableText = stableText,
            ProvisionalText = provisionalText,
            StabilityRatio = stabilityRatio,
            TimeSinceLastStable = TimeSpan.Zero
        });
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        if (_ws == null || _audioChannel == null) return;

        try
        {
            await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (_ws.State != WebSocketState.Open)
                {
                    break;
                }

                await _sendLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(chunk),
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
                }
                finally
                {
                    _sendLock.Release();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    private async Task KeepAliveLoopAsync(CancellationToken ct)
    {
        if (_ws == null) return;

        var keepAliveJson = JsonSerializer.SerializeToUtf8Bytes(new DeepgramKeepAlive());

        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                await Task.Delay(_options.KeepAliveIntervalMs, ct).ConfigureAwait(false);

                if (_ws.State == WebSocketState.Open)
                {
                    await _sendLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await _ws.SendAsync(
                            new ArraySegment<byte>(keepAliveJson),
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnWarning?.Invoke($"Keep-alive error: {ex.Message}");
        }
    }

    private async Task CloseWebSocketAsync()
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            // Send close stream message
            var closeJson = JsonSerializer.SerializeToUtf8Bytes(new DeepgramCloseStream());
            await _sendLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(closeJson),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            // Close WebSocket gracefully
            await _ws.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Closing",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            _ws.Dispose();
            _ws = null;
        }
    }
}
