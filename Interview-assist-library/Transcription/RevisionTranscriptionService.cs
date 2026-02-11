using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Constants;
using InterviewAssist.Library.Pipeline;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Revision mode transcription service with overlapping windows and local agreement policy.
/// Text becomes stable after appearing consistently across multiple transcription passes.
/// </summary>
public sealed class RevisionTranscriptionService : IStreamingTranscriptionService
{
    private readonly IAudioCaptureService _audio;
    private readonly StreamingTranscriptionOptions _options;
    private readonly StringBuilder _stableTranscript = new();
    private readonly StringBuilder _provisionalTranscript = new();
    private readonly object _transcriptLock = new();
    private readonly List<string> _recentTranscripts = new();
    private readonly Queue<byte[]> _audioHistory = new();
    private CancellationTokenSource? _cts;
    private Channel<byte[]>? _audioChannel;
    private Action<byte[]>? _audioHandler;
    private Task? _transcribeTask;
    private int _started;
    private long _streamOffsetMs;
    private DateTime _lastStableTime = DateTime.UtcNow;
    private int _totalAudioBytesInHistory;

    public event Action<StableTextEventArgs>? OnStableText;
    public event Action<ProvisionalTextEventArgs>? OnProvisionalText;
    public event Action<HypothesisEventArgs>? OnFullHypothesis;
    public event Action<string>? OnInfo;
    public event Action<string>? OnWarning;
    public event Action<Exception>? OnError;

    /// <summary>
    /// Creates a new RevisionTranscriptionService.
    /// </summary>
    /// <param name="audioCaptureService">Audio capture service for input.</param>
    /// <param name="options">Transcription options.</param>
    public RevisionTranscriptionService(
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
        _recentTranscripts.Clear();
        _audioHistory.Clear();
        _totalAudioBytesInHistory = 0;

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
        OnInfo?.Invoke($"RevisionTranscriptionService active ({_audio.GetSource()})");

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
            try { if (_audio != null) _audio.OnAudioChunk -= _audioHandler; } catch { }
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
            return _provisionalTranscript.ToString();
        }
    }

    private async Task TranscriptionLoop(CancellationToken ct)
    {
        if (_audioChannel == null) return;
        var currentBatch = new MemoryStream();
        var lastBatchTime = Environment.TickCount64;
        var revisionOptions = _options.Revision;

        // Calculate byte sizes for timing
        int bytesPerMs = _options.SampleRate * 2 / 1000; // 16-bit audio = 2 bytes per sample
        int batchBytes = revisionOptions.BatchMs * bytesPerMs;
        int overlapBytes = revisionOptions.OverlapMs * bytesPerMs;

        try
        {
            await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                // Add to current batch
                await currentBatch.WriteAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false);

                // Add to audio history for overlapping
                _audioHistory.Enqueue(chunk);
                _totalAudioBytesInHistory += chunk.Length;

                // Trim history to keep only what we need for overlap
                int maxHistoryBytes = batchBytes + overlapBytes;
                while (_totalAudioBytesInHistory > maxHistoryBytes && _audioHistory.Count > 0)
                {
                    var removed = _audioHistory.Dequeue();
                    _totalAudioBytesInHistory -= removed.Length;
                }

                var elapsed = Environment.TickCount64 - lastBatchTime;

                if (elapsed >= revisionOptions.BatchMs)
                {
                    // Build overlapping window from history
                    var windowData = BuildOverlappingWindow(batchBytes, overlapBytes);

                    if (windowData.Length > 0)
                    {
                        await TranscribeAndReconcileAsync(windowData, elapsed, ct).ConfigureAwait(false);
                    }

                    currentBatch.SetLength(0);
                    lastBatchTime = Environment.TickCount64;
                }
            }

            // Final transcription
            if (currentBatch.Length > 0)
            {
                var finalData = currentBatch.ToArray();
                if (finalData.Length > 0)
                {
                    await TranscribeAndReconcileAsync(finalData, 0, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
        finally
        {
            currentBatch.Dispose();
        }
    }

    private byte[] BuildOverlappingWindow(int batchBytes, int overlapBytes)
    {
        // Combine audio history into overlapping window
        var windowSize = Math.Min(_totalAudioBytesInHistory, batchBytes + overlapBytes);
        if (windowSize == 0) return Array.Empty<byte>();

        using var ms = new MemoryStream();
        int bytesNeeded = windowSize;
        var historyArray = _audioHistory.ToArray();

        // Take from end of history (most recent first, but write in order)
        int startIdx = 0;
        int accumulated = _totalAudioBytesInHistory;

        // Find where to start to get the bytes we need
        for (int i = 0; i < historyArray.Length && accumulated > bytesNeeded; i++)
        {
            accumulated -= historyArray[i].Length;
            startIdx = i + 1;
        }

        // Write chunks
        for (int i = startIdx; i < historyArray.Length; i++)
        {
            ms.Write(historyArray[i], 0, historyArray[i].Length);
        }

        return ms.ToArray();
    }

    private async Task TranscribeAndReconcileAsync(byte[] audioData, long batchDurationMs, CancellationToken ct)
    {
        // Check minimum duration
        double durationSeconds = (double)audioData.Length / (_options.SampleRate * 2);
        if (durationSeconds < TranscriptionConstants.MinAudioDurationSeconds)
        {
            _streamOffsetMs += batchDurationMs;
            return;
        }

        // Check for silence
        if (_options.SilenceThreshold > 0 && IsSilence(audioData, _options.SilenceThreshold))
        {
            OnInfo?.Invoke("Silence detected, skipping transcription");
            _streamOffsetMs += batchDurationMs;
            return;
        }

        var wav = BuildWav(audioData, _options.SampleRate, 1, 16);
        try
        {
            var prompt = BuildPrompt();
            var newTranscript = await TranscribeAsync(wav, prompt, ct).ConfigureAwait(false);

            // Filter low-quality transcriptions
            if (TranscriptQualityFilter.IsLowQuality(newTranscript))
            {
                OnInfo?.Invoke("Low quality transcript filtered");
                _streamOffsetMs += batchDurationMs;
                return;
            }

            newTranscript = TranscriptQualityFilter.CleanTranscript(newTranscript);

            if (string.IsNullOrWhiteSpace(newTranscript))
            {
                _streamOffsetMs += batchDurationMs;
                return;
            }

            // Add to recent transcripts for agreement checking
            _recentTranscripts.Add(newTranscript);

            // Keep only the required number for agreement
            while (_recentTranscripts.Count > _options.Revision.AgreementCount)
            {
                _recentTranscripts.RemoveAt(0);
            }

            // Find agreed text across recent transcripts
            ReconcileTranscripts();

            _streamOffsetMs += batchDurationMs;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    private void ReconcileTranscripts()
    {
        var revisionOptions = _options.Revision;
        var now = DateTime.UtcNow;

        if (_recentTranscripts.Count < revisionOptions.AgreementCount)
        {
            // Not enough transcripts for agreement, treat latest as provisional
            if (_recentTranscripts.Count > 0)
            {
                var latestTranscript = _recentTranscripts[^1];
                var currentStable = GetStableTranscript();

                // Extract portion beyond current stable text
                var extension = TranscriptionTextComparer.GetExtension(currentStable, latestTranscript);

                lock (_transcriptLock)
                {
                    _provisionalTranscript.Clear();
                    if (!string.IsNullOrWhiteSpace(extension))
                    {
                        _provisionalTranscript.Append(extension);
                    }
                }

                EmitProvisionalAndHypothesis(now);
            }
            return;
        }

        // Find agreed text across all recent transcripts
        var agreedText = TranscriptionTextComparer.FindAgreedText(_recentTranscripts);
        var currentStableText = GetStableTranscript();

        // Find new stable portion (extension of current stable)
        var newStableExtension = TranscriptionTextComparer.GetExtension(currentStableText, agreedText);

        if (!string.IsNullOrWhiteSpace(newStableExtension))
        {
            // We have new stable text
            lock (_transcriptLock)
            {
                if (_stableTranscript.Length > 0)
                    _stableTranscript.Append(' ');
                _stableTranscript.Append(newStableExtension);
            }

            _lastStableTime = now;

            OnStableText?.Invoke(new StableTextEventArgs
            {
                Text = newStableExtension,
                StreamOffsetMs = _streamOffsetMs,
                Timestamp = now,
                ConfirmationCount = revisionOptions.AgreementCount
            });

            // Clear transcripts that contributed to this stable text
            _recentTranscripts.Clear();
        }

        // Update provisional text from latest transcript
        if (_recentTranscripts.Count > 0)
        {
            var latestTranscript = _recentTranscripts[^1];
            var updatedStable = GetStableTranscript();
            var provisional = TranscriptionTextComparer.GetExtension(updatedStable, latestTranscript);

            lock (_transcriptLock)
            {
                _provisionalTranscript.Clear();
                if (!string.IsNullOrWhiteSpace(provisional))
                {
                    _provisionalTranscript.Append(provisional);
                }
            }
        }
        else
        {
            lock (_transcriptLock)
            {
                _provisionalTranscript.Clear();
            }
        }

        EmitProvisionalAndHypothesis(now);
    }

    private void EmitProvisionalAndHypothesis(DateTime now)
    {
        var stableText = GetStableTranscript();
        var provisionalText = GetProvisionalTranscript();

        if (!string.IsNullOrWhiteSpace(provisionalText))
        {
            OnProvisionalText?.Invoke(new ProvisionalTextEventArgs
            {
                Text = provisionalText,
                StreamOffsetMs = _streamOffsetMs,
                Timestamp = now
            });
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
