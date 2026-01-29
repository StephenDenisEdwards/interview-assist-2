using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Constants;
using InterviewAssist.Library.Pipeline;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Streaming mode transcription service with rapid hypothesis updates and stability tracking.
/// Text becomes stable after remaining unchanged for N iterations or a timeout period.
/// </summary>
public sealed class StreamingHypothesisService : IStreamingTranscriptionService
{
    private readonly IAudioCaptureService _audio;
    private readonly StreamingTranscriptionOptions _options;
    private readonly StringBuilder _stableTranscript = new();
    private readonly object _transcriptLock = new();
    private CancellationTokenSource? _cts;
    private Channel<byte[]>? _audioChannel;
    private Action<byte[]>? _audioHandler;
    private Task? _transcribeTask;
    private int _started;
    private long _streamOffsetMs;
    private DateTime _lastStableTime = DateTime.UtcNow;

    // Stability tracking state
    private string _currentProvisional = string.Empty;
    private string _lastProvisionalEmitted = string.Empty;
    private int _provisionalUnchangedCount;
    private DateTime _provisionalFirstSeen = DateTime.UtcNow;
    private DateTime _lastProvisionalEmitTime = DateTime.MinValue;

    public event Action<StableTextEventArgs>? OnStableText;
    public event Action<ProvisionalTextEventArgs>? OnProvisionalText;
    public event Action<HypothesisEventArgs>? OnFullHypothesis;
    public event Action<string>? OnInfo;
    public event Action<string>? OnWarning;
    public event Action<Exception>? OnError;

    /// <summary>
    /// Creates a new StreamingHypothesisService.
    /// </summary>
    /// <param name="audioCaptureService">Audio capture service for input.</param>
    /// <param name="options">Transcription options.</param>
    public StreamingHypothesisService(
        IAudioCaptureService audioCaptureService,
        StreamingTranscriptionOptions options)
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
        _lastStableTime = DateTime.UtcNow;
        ResetProvisionalTracking();

        _audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _audioHandler = bytes =>
        {
            if (_audioChannel == null) return;
            if (!_audioChannel.Writer.TryWrite(bytes))
            {
                OnWarning?.Invoke("Audio queue full; dropping audio chunk.");
            }
        };

        _audio.OnAudioChunk += _audioHandler;
        _audio.Start();
        OnInfo?.Invoke($"StreamingHypothesisService active ({_audio.GetSource()})");

        _transcribeTask = TranscriptionLoop(_cts.Token);
        try
        {
            await _transcribeTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        try { _audio?.Stop(); } catch { }
        if (_audioHandler != null)
        {
            try { _audio.OnAudioChunk -= _audioHandler; } catch { }
            _audioHandler = null;
        }
        try { _audioChannel?.Writer.TryComplete(); } catch { }
        try { _cts?.Cancel(); } catch { }

        if (_transcribeTask != null)
        {
            try { await _transcribeTask.ConfigureAwait(false); } catch { }
            _transcribeTask = null;
        }

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
            return _currentProvisional;
        }
    }

    private void ResetProvisionalTracking()
    {
        _currentProvisional = string.Empty;
        _lastProvisionalEmitted = string.Empty;
        _provisionalUnchangedCount = 0;
        _provisionalFirstSeen = DateTime.UtcNow;
    }

    private async Task TranscriptionLoop(CancellationToken ct)
    {
        if (_audioChannel == null) return;
        var audioBuffer = new MemoryStream();
        var streamingOptions = _options.Streaming;
        var lastTranscribeTime = Environment.TickCount64;
        var lastUpdateTime = Environment.TickCount64;

        // Calculate minimum bytes for transcription
        int bytesPerMs = _options.SampleRate * 2 / 1000;
        int minBatchBytes = streamingOptions.MinBatchMs * bytesPerMs;

        try
        {
            await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await audioBuffer.WriteAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false);
                var now = Environment.TickCount64;

                // Check if it's time for an update
                bool timeSinceLastUpdate = (now - lastUpdateTime) >= streamingOptions.UpdateIntervalMs;
                bool hasEnoughAudio = audioBuffer.Length >= minBatchBytes;

                if (timeSinceLastUpdate && hasEnoughAudio)
                {
                    var audioData = audioBuffer.ToArray();
                    var batchDuration = now - lastTranscribeTime;

                    await TranscribeAndUpdateHypothesisAsync(audioData, batchDuration, ct).ConfigureAwait(false);

                    lastUpdateTime = now;

                    // Check for stability timeout on provisional text
                    CheckStabilityTimeout();
                }

                // Check for silence-based flush
                if (_options.SilenceThreshold > 0 && IsSilence(chunk, _options.SilenceThreshold))
                {
                    var elapsed = now - lastTranscribeTime;
                    if (elapsed >= streamingOptions.MinBatchMs && audioBuffer.Length >= minBatchBytes)
                    {
                        var audioData = audioBuffer.ToArray();
                        await TranscribeAndUpdateHypothesisAsync(audioData, elapsed, ct).ConfigureAwait(false);

                        // Silence boundary - promote any provisional to stable
                        PromoteProvisionalToStable();

                        audioBuffer.SetLength(0);
                        lastTranscribeTime = now;
                        lastUpdateTime = now;
                    }
                }
            }

            // Final transcription
            if (audioBuffer.Length > 0)
            {
                var finalData = audioBuffer.ToArray();
                var finalDuration = Environment.TickCount64 - lastTranscribeTime;
                await TranscribeAndUpdateHypothesisAsync(finalData, finalDuration, ct).ConfigureAwait(false);
                PromoteProvisionalToStable();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
        finally
        {
            audioBuffer.Dispose();
        }
    }

    private async Task TranscribeAndUpdateHypothesisAsync(byte[] audioData, long batchDurationMs, CancellationToken ct)
    {
        // Check minimum duration
        double durationSeconds = (double)audioData.Length / (_options.SampleRate * 2);
        if (durationSeconds < TranscriptionConstants.MinAudioDurationSeconds)
        {
            return;
        }

        // Check for silence (entire buffer)
        if (_options.SilenceThreshold > 0 && IsSilence(audioData, _options.SilenceThreshold))
        {
            return;
        }

        var wav = BuildWav(audioData, _options.SampleRate, 1, 16);
        try
        {
            var prompt = BuildPrompt();
            var fullTranscript = await TranscribeAsync(wav, prompt, ct).ConfigureAwait(false);

            // Filter low-quality transcriptions
            if (TranscriptQualityFilter.IsLowQuality(fullTranscript))
            {
                return;
            }

            fullTranscript = TranscriptQualityFilter.CleanTranscript(fullTranscript);

            if (string.IsNullOrWhiteSpace(fullTranscript))
            {
                return;
            }

            UpdateHypothesis(fullTranscript);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    private void UpdateHypothesis(string fullTranscript)
    {
        var now = DateTime.UtcNow;
        var stableText = GetStableTranscript();
        var streamingOptions = _options.Streaming;

        // Extract provisional portion (beyond stable text)
        var provisional = TranscriptionTextComparer.GetExtension(stableText, fullTranscript);
        provisional = provisional?.Trim() ?? string.Empty;

        // Check if provisional text has changed
        if (string.Equals(_currentProvisional, provisional, StringComparison.Ordinal))
        {
            // Unchanged - increment stability counter
            _provisionalUnchangedCount++;

            // Check if stable by iteration count
            if (_provisionalUnchangedCount >= streamingOptions.StabilityIterations)
            {
                PromoteProvisionalToStable();
                return;
            }
        }
        else
        {
            // Changed - reset stability tracking
            _currentProvisional = provisional;
            _provisionalUnchangedCount = 1;
            _provisionalFirstSeen = now;
        }

        // Emit provisional update with anti-flicker cooldown
        if (!string.IsNullOrWhiteSpace(provisional))
        {
            var timeSinceLastEmit = (now - _lastProvisionalEmitTime).TotalMilliseconds;
            bool cooldownElapsed = timeSinceLastEmit >= streamingOptions.FlickerCooldownMs;
            bool textChanged = !string.Equals(_lastProvisionalEmitted, provisional, StringComparison.Ordinal);

            if (cooldownElapsed && textChanged)
            {
                _lastProvisionalEmitted = provisional;
                _lastProvisionalEmitTime = now;

                OnProvisionalText?.Invoke(new ProvisionalTextEventArgs
                {
                    Text = provisional,
                    StreamOffsetMs = _streamOffsetMs,
                    Timestamp = now,
                    Confidence = CalculateConfidence()
                });
            }
        }

        EmitHypothesis(now);
    }

    private void CheckStabilityTimeout()
    {
        if (string.IsNullOrWhiteSpace(_currentProvisional))
            return;

        var elapsed = (DateTime.UtcNow - _provisionalFirstSeen).TotalMilliseconds;
        if (elapsed >= _options.Streaming.StabilityTimeoutMs)
        {
            PromoteProvisionalToStable();
        }
    }

    private void PromoteProvisionalToStable()
    {
        if (string.IsNullOrWhiteSpace(_currentProvisional))
            return;

        var now = DateTime.UtcNow;

        lock (_transcriptLock)
        {
            if (_stableTranscript.Length > 0)
                _stableTranscript.Append(' ');
            _stableTranscript.Append(_currentProvisional);
        }

        _lastStableTime = now;

        OnStableText?.Invoke(new StableTextEventArgs
        {
            Text = _currentProvisional,
            StreamOffsetMs = _streamOffsetMs,
            Timestamp = now,
            ConfirmationCount = _provisionalUnchangedCount
        });

        ResetProvisionalTracking();
        EmitHypothesis(now);
    }

    private double CalculateConfidence()
    {
        var streamingOptions = _options.Streaming;

        // Confidence based on unchanged iterations
        double iterationConfidence = (double)_provisionalUnchangedCount / streamingOptions.StabilityIterations;

        // Confidence based on time
        var elapsed = (DateTime.UtcNow - _provisionalFirstSeen).TotalMilliseconds;
        double timeConfidence = Math.Min(1.0, elapsed / streamingOptions.StabilityTimeoutMs);

        // Return the higher of the two
        return Math.Max(iterationConfidence, timeConfidence);
    }

    private void EmitHypothesis(DateTime now)
    {
        var stableText = GetStableTranscript();
        var provisionalText = GetProvisionalTranscript();

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
            TimeSinceLastStable = now - _lastStableTime
        });
    }

    private string BuildPrompt()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(_options.VocabularyPrompt))
        {
            parts.Add(_options.VocabularyPrompt);
        }

        if (_options.EnableContextPrompting)
        {
            var stableText = GetStableTranscript();
            if (!string.IsNullOrWhiteSpace(stableText))
            {
                var contextLength = Math.Min(stableText.Length, _options.ContextPromptMaxChars);
                var context = stableText.Substring(stableText.Length - contextLength);

                var spaceIdx = context.IndexOf(' ');
                if (spaceIdx > 0 && spaceIdx < context.Length - 10)
                {
                    context = context.Substring(spaceIdx + 1);
                }

                parts.Add(context);
            }
        }

        return parts.Count > 0 ? string.Join(" ", parts) : string.Empty;
    }

    private static bool IsSilence(byte[] pcmData, double threshold)
    {
        if (pcmData.Length < 2) return true;

        double sumSquares = 0;
        int sampleCount = pcmData.Length / 2;

        for (int i = 0; i < pcmData.Length - 1; i += 2)
        {
            short sample = BitConverter.ToInt16(pcmData, i);
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        return rms < threshold;
    }

    private static byte[] BuildWav(byte[] pcm, int sampleRate, short channels, short bitsPerSample)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + pcm.Length);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(pcm.Length);
        bw.Write(pcm);
        bw.Flush();
        return ms.ToArray();
    }

    private async Task<string> TranscribeAsync(byte[] wavData, string prompt, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(wavData);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("json"), "response_format");

        if (!string.IsNullOrWhiteSpace(_options.Language))
        {
            content.Add(new StringContent(_options.Language), "language");
        }

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            content.Add(new StringContent(prompt), "prompt");
        }

        using var resp = await http.PostAsync("https://api.openai.com/v1/audio/transcriptions", content, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Transcription failed: {(int)resp.StatusCode} {resp.ReasonPhrase} - {body}");
        }
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("text", out var textProp))
        {
            return textProp.GetString() ?? string.Empty;
        }
        return string.Empty;
    }
}
