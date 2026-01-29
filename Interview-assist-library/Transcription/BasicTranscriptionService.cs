using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Constants;
using InterviewAssist.Library.Pipeline;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Basic transcription service where all text is immediately stable.
/// Uses context prompting to guide transcription with recent stable text.
/// </summary>
public sealed class BasicTranscriptionService : IStreamingTranscriptionService
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

    public event Action<StableTextEventArgs>? OnStableText;
    public event Action<ProvisionalTextEventArgs>? OnProvisionalText;
    public event Action<HypothesisEventArgs>? OnFullHypothesis;
    public event Action<string>? OnInfo;
    public event Action<string>? OnWarning;
    public event Action<Exception>? OnError;

    /// <summary>
    /// Creates a new BasicTranscriptionService.
    /// </summary>
    /// <param name="audioCaptureService">Audio capture service for input.</param>
    /// <param name="options">Transcription options.</param>
    public BasicTranscriptionService(
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

        _audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(16)
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
        OnInfo?.Invoke($"BasicTranscriptionService active ({_audio.GetSource()})");

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
        // Basic mode has no provisional text - everything is immediately stable
        return string.Empty;
    }

    private async Task TranscriptionLoop(CancellationToken ct)
    {
        if (_audioChannel == null) return;
        var buffer = new MemoryStream();
        var lastFlush = Environment.TickCount64;
        var batchOptions = _options.Basic;

        try
        {
            await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await buffer.WriteAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false);
                var elapsed = Environment.TickCount64 - lastFlush;

                // Adaptive batching: flush on speech boundary or max window
                bool minWindowReached = elapsed >= batchOptions.BatchMs;
                bool maxWindowReached = elapsed >= batchOptions.MaxBatchMs;
                bool recentChunkIsSilence = _options.SilenceThreshold > 0 && IsSilence(chunk, _options.SilenceThreshold);
                bool speechBoundary = minWindowReached && recentChunkIsSilence;

                if (speechBoundary || maxWindowReached)
                {
                    await FlushAndTranscribeAsync(buffer, elapsed, ct).ConfigureAwait(false);
                    lastFlush = Environment.TickCount64;
                }
            }

            // Final flush
            var finalElapsed = Environment.TickCount64 - lastFlush;
            await FlushAndTranscribeAsync(buffer, finalElapsed, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    private async Task FlushAndTranscribeAsync(MemoryStream pcmBuffer, long batchDurationMs, CancellationToken ct)
    {
        if (pcmBuffer.Length == 0) return;

        var pcmData = pcmBuffer.ToArray();
        pcmBuffer.SetLength(0);

        // Check minimum duration (Whisper requires >= 0.1 seconds)
        double durationSeconds = (double)pcmData.Length / (_options.SampleRate * 2);
        if (durationSeconds < TranscriptionConstants.MinAudioDurationSeconds)
        {
            OnWarning?.Invoke($"Audio too short ({durationSeconds:F3}s), skipping transcription");
            return;
        }

        // Check for silence
        if (_options.SilenceThreshold > 0 && IsSilence(pcmData, _options.SilenceThreshold))
        {
            OnInfo?.Invoke("Silence detected, skipping transcription");
            _streamOffsetMs += batchDurationMs;
            return;
        }

        var wav = BuildWav(pcmData, _options.SampleRate, 1, 16);
        try
        {
            var prompt = BuildPrompt();
            var text = await TranscribeAsync(wav, prompt, ct).ConfigureAwait(false);

            // Filter low-quality transcriptions
            if (TranscriptQualityFilter.IsLowQuality(text))
            {
                OnInfo?.Invoke("Low quality transcript filtered");
                _streamOffsetMs += batchDurationMs;
                return;
            }

            text = TranscriptQualityFilter.CleanTranscript(text);

            if (!string.IsNullOrWhiteSpace(text))
            {
                var now = DateTime.UtcNow;
                var stableArgs = new StableTextEventArgs
                {
                    Text = text,
                    StreamOffsetMs = _streamOffsetMs,
                    Timestamp = now,
                    ConfirmationCount = 1
                };

                lock (_transcriptLock)
                {
                    if (_stableTranscript.Length > 0)
                        _stableTranscript.Append(' ');
                    _stableTranscript.Append(text);
                }

                _lastStableTime = now;
                OnStableText?.Invoke(stableArgs);

                // Emit full hypothesis
                var fullText = GetStableTranscript();
                OnFullHypothesis?.Invoke(new HypothesisEventArgs
                {
                    FullText = fullText,
                    StableText = fullText,
                    ProvisionalText = string.Empty,
                    StabilityRatio = 1.0,
                    TimeSinceLastStable = TimeSpan.Zero
                });
            }

            _streamOffsetMs += batchDurationMs;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    private string BuildPrompt()
    {
        var parts = new List<string>();

        // Add vocabulary prompt if specified
        if (!string.IsNullOrWhiteSpace(_options.VocabularyPrompt))
        {
            parts.Add(_options.VocabularyPrompt);
        }

        // Add context from recent stable transcript
        if (_options.EnableContextPrompting)
        {
            var stableText = GetStableTranscript();
            if (!string.IsNullOrWhiteSpace(stableText))
            {
                // Take last N characters for context
                var contextLength = Math.Min(stableText.Length, _options.ContextPromptMaxChars);
                var context = stableText.Substring(stableText.Length - contextLength);

                // Try to start at a word boundary
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
