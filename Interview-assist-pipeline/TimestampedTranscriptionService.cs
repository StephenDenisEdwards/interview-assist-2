using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Constants;

namespace InterviewAssist.Pipeline;

/// <summary>
/// Transcription service that provides timestamped segments suitable for video captioning.
/// Uses OpenAI Whisper API with verbose_json format for timing information.
/// </summary>
public sealed class TimestampedTranscriptionService : IAsyncDisposable
{
    private readonly IAudioCaptureService? _audio;
    private readonly string _apiKey;
    private readonly TimestampedTranscriptionOptions _options;
    private readonly HttpClient _http;

    private CancellationTokenSource? _cts;
    private Task? _transcriptionTask;
    private MemoryStream? _audioBuffer;
    private long _lastFlushTicks;
    private double _streamOffsetSeconds;
    private int _started;

    /// <summary>
    /// Fired when a new transcription result is available with timestamped segments.
    /// </summary>
    public event Action<TranscriptionResult>? OnTranscriptionResult;

    /// <summary>
    /// Fired for each individual segment as it's transcribed.
    /// Useful for real-time caption display.
    /// </summary>
    public event Action<TranscriptSegment>? OnSegment;

    /// <summary>
    /// Fired when an error occurs.
    /// </summary>
    public event Action<string>? OnError;

    /// <summary>
    /// Fired when a speech pause is detected (silence in audio batch).
    /// Useful for triggering downstream processing that waits for natural speech boundaries.
    /// </summary>
    public event Action? OnSpeechPause;

    /// <summary>
    /// Creates a transcription service with audio capture for real-time streaming.
    /// </summary>
    public TimestampedTranscriptionService(
        IAudioCaptureService audioCapture,
        string apiKey,
        TimestampedTranscriptionOptions? options = null)
    {
        _audio = audioCapture ?? throw new ArgumentNullException(nameof(audioCapture));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _options = options ?? new TimestampedTranscriptionOptions();

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>
    /// Creates a transcription service for direct API calls (no audio capture).
    /// Use TranscribeAsync directly with audio data.
    /// </summary>
    public TimestampedTranscriptionService(string apiKey, TimestampedTranscriptionOptions? options = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _options = options ?? new TimestampedTranscriptionOptions();
        _audio = null;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>
    /// Starts real-time transcription from the audio capture source.
    /// </summary>
    public void Start(CancellationToken cancellationToken = default)
    {
        if (_audio == null)
            throw new InvalidOperationException("No audio capture service configured. Use TranscribeAsync for direct API calls.");

        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("Already started");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _audioBuffer = new MemoryStream();
        _lastFlushTicks = Environment.TickCount64;
        _streamOffsetSeconds = 0;

        _audio.OnAudioChunk += HandleAudioChunk;
        _audio.Start();

        _transcriptionTask = Task.CompletedTask;
    }

    private void HandleAudioChunk(byte[] chunk)
    {
        if (_audioBuffer == null || _cts == null || _cts.IsCancellationRequested)
            return;

        lock (_audioBuffer)
        {
            _audioBuffer.Write(chunk, 0, chunk.Length);

            var elapsed = Environment.TickCount64 - _lastFlushTicks;
            if (elapsed >= _options.BatchMs && _audioBuffer.Length > 0)
            {
                var audioData = _audioBuffer.ToArray();
                _audioBuffer.SetLength(0);
                _lastFlushTicks = Environment.TickCount64;

                var currentOffset = _streamOffsetSeconds;
                // Calculate duration of this chunk: bytes / (sample_rate * channels * bytes_per_sample)
                var chunkDuration = (double)audioData.Length / (_options.SampleRate * 1 * 2);
                _streamOffsetSeconds += chunkDuration;

                // Fire transcription in background
                _ = TranscribeAndNotifyAsync(audioData, currentOffset, _cts.Token);
            }
        }
    }

    private async Task TranscribeAndNotifyAsync(byte[] pcmData, double streamOffset, CancellationToken ct)
    {
        try
        {
            // Check minimum duration (Whisper requires >= 0.1 seconds)
            double durationSeconds = (double)pcmData.Length / (_options.SampleRate * 2);
            if (durationSeconds < TranscriptionConstants.MinAudioDurationSeconds)
            {
                // Skip - audio too short
                return;
            }

            // Check for silence (skip if below threshold)
            if (_options.SilenceThreshold > 0 && IsSilence(pcmData, _options.SilenceThreshold))
            {
                // Signal speech pause for downstream consumers
                OnSpeechPause?.Invoke();
                return;
            }

            var result = await TranscribeAsync(pcmData, streamOffset, ct).ConfigureAwait(false);
            if (result != null)
            {
                // Filter out hallucination patterns (excessive repetition)
                if (HasExcessiveRepetition(result.FullText))
                {
                    return;
                }

                OnTranscriptionResult?.Invoke(result);

                foreach (var segment in result.Segments)
                {
                    OnSegment?.Invoke(segment);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Transcription error: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines if audio data is silence based on RMS energy.
    /// </summary>
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

    /// <summary>
    /// Detects excessive word repetition (Whisper hallucination pattern).
    /// </summary>
    private static bool HasExcessiveRepetition(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return false;

        int maxConsecutive = 1;
        int currentConsecutive = 1;

        for (int i = 1; i < words.Length; i++)
        {
            if (words[i].Equals(words[i - 1], StringComparison.OrdinalIgnoreCase))
            {
                currentConsecutive++;
                maxConsecutive = Math.Max(maxConsecutive, currentConsecutive);
            }
            else
            {
                currentConsecutive = 1;
            }
        }

        return maxConsecutive >= TranscriptionConstants.MaxConsecutiveRepetitions;
    }

    /// <summary>
    /// Transcribes raw PCM audio data directly.
    /// </summary>
    /// <param name="pcmData">16-bit mono PCM audio data at the configured sample rate.</param>
    /// <param name="streamOffsetSeconds">Offset from stream start for absolute timing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Transcription result with timestamped segments, or null if empty.</returns>
    public async Task<TranscriptionResult?> TranscribeAsync(
        byte[] pcmData,
        double streamOffsetSeconds = 0,
        CancellationToken ct = default)
    {
        if (pcmData.Length == 0)
            return null;

        var sw = Stopwatch.StartNew();

        var wav = BuildWav(pcmData);
        var audioDuration = (double)pcmData.Length / (_options.SampleRate * 1 * 2);

        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(wav);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("verbose_json"), "response_format");

        if (_options.IncludeWordTimestamps)
        {
            content.Add(new StringContent("word"), "timestamp_granularities[]");
        }
        content.Add(new StringContent("segment"), "timestamp_granularities[]");

        if (!string.IsNullOrWhiteSpace(_options.Language))
        {
            content.Add(new StringContent(_options.Language), "language");
        }

        if (!string.IsNullOrWhiteSpace(_options.Prompt))
        {
            content.Add(new StringContent(_options.Prompt), "prompt");
        }

        using var response = await _http.PostAsync(
            "https://api.openai.com/v1/audio/transcriptions",
            content,
            ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            OnError?.Invoke($"Whisper API error: {response.StatusCode} - {body}");
            return null;
        }

        return ParseVerboseJsonResponse(body, streamOffsetSeconds, audioDuration, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Transcribes a WAV file directly.
    /// </summary>
    public async Task<TranscriptionResult?> TranscribeFileAsync(
        string filePath,
        double streamOffsetSeconds = 0,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var fileBytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        var fileName = Path.GetFileName(filePath);

        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(fileBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", fileName);
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("verbose_json"), "response_format");

        if (_options.IncludeWordTimestamps)
        {
            content.Add(new StringContent("word"), "timestamp_granularities[]");
        }
        content.Add(new StringContent("segment"), "timestamp_granularities[]");

        if (!string.IsNullOrWhiteSpace(_options.Language))
        {
            content.Add(new StringContent(_options.Language), "language");
        }

        if (!string.IsNullOrWhiteSpace(_options.Prompt))
        {
            content.Add(new StringContent(_options.Prompt), "prompt");
        }

        using var response = await _http.PostAsync(
            "https://api.openai.com/v1/audio/transcriptions",
            content,
            ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            OnError?.Invoke($"Whisper API error: {response.StatusCode} - {body}");
            return null;
        }

        // Estimate audio duration from file - will be refined by API response
        var estimatedDuration = 0.0;
        return ParseVerboseJsonResponse(body, streamOffsetSeconds, estimatedDuration, sw.ElapsedMilliseconds);
    }

    private TranscriptionResult? ParseVerboseJsonResponse(
        string json,
        double streamOffset,
        double audioDuration,
        long latencyMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var fullText = root.TryGetProperty("text", out var textProp)
                ? textProp.GetString() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(fullText))
                return null;

            var language = root.TryGetProperty("language", out var langProp)
                ? langProp.GetString()
                : null;

            // Get actual duration from API response if available
            if (root.TryGetProperty("duration", out var durationProp))
            {
                audioDuration = durationProp.GetDouble();
            }

            var segments = new List<TranscriptSegment>();

            if (root.TryGetProperty("segments", out var segmentsArray))
            {
                foreach (var seg in segmentsArray.EnumerateArray())
                {
                    var segText = seg.TryGetProperty("text", out var segTextProp)
                        ? segTextProp.GetString()?.Trim() ?? ""
                        : "";

                    if (string.IsNullOrWhiteSpace(segText))
                        continue;

                    var start = seg.TryGetProperty("start", out var startProp)
                        ? startProp.GetDouble()
                        : 0;

                    var end = seg.TryGetProperty("end", out var endProp)
                        ? endProp.GetDouble()
                        : 0;

                    List<TranscriptWord>? words = null;

                    if (_options.IncludeWordTimestamps && seg.TryGetProperty("words", out var wordsArray))
                    {
                        words = new List<TranscriptWord>();
                        foreach (var w in wordsArray.EnumerateArray())
                        {
                            var word = w.TryGetProperty("word", out var wordProp)
                                ? wordProp.GetString() ?? ""
                                : "";

                            var wordStart = w.TryGetProperty("start", out var wStartProp)
                                ? wStartProp.GetDouble()
                                : 0;

                            var wordEnd = w.TryGetProperty("end", out var wEndProp)
                                ? wEndProp.GetDouble()
                                : 0;

                            if (!string.IsNullOrWhiteSpace(word))
                            {
                                words.Add(new TranscriptWord
                                {
                                    Word = word.Trim(),
                                    StartSeconds = wordStart,
                                    EndSeconds = wordEnd
                                });
                            }
                        }
                    }

                    segments.Add(new TranscriptSegment
                    {
                        Text = segText,
                        StartSeconds = start,
                        EndSeconds = end,
                        Words = words,
                        StreamOffsetSeconds = streamOffset + start
                    });
                }
            }

            // If no segments returned, create one from the full text
            if (segments.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
            {
                segments.Add(new TranscriptSegment
                {
                    Text = fullText.Trim(),
                    StartSeconds = 0,
                    EndSeconds = audioDuration,
                    StreamOffsetSeconds = streamOffset
                });
            }

            // Handle word-level timestamps at root level (newer API versions)
            if (_options.IncludeWordTimestamps &&
                segments.Count > 0 &&
                segments[0].Words == null &&
                root.TryGetProperty("words", out var rootWordsArray))
            {
                var allWords = new List<TranscriptWord>();
                foreach (var w in rootWordsArray.EnumerateArray())
                {
                    var word = w.TryGetProperty("word", out var wordProp)
                        ? wordProp.GetString() ?? ""
                        : "";

                    var wordStart = w.TryGetProperty("start", out var wStartProp)
                        ? wStartProp.GetDouble()
                        : 0;

                    var wordEnd = w.TryGetProperty("end", out var wEndProp)
                        ? wEndProp.GetDouble()
                        : 0;

                    if (!string.IsNullOrWhiteSpace(word))
                    {
                        allWords.Add(new TranscriptWord
                        {
                            Word = word.Trim(),
                            StartSeconds = wordStart,
                            EndSeconds = wordEnd
                        });
                    }
                }

                // Distribute words to segments based on timing
                foreach (var segment in segments)
                {
                    var segmentWords = allWords
                        .Where(w => w.StartSeconds >= segment.StartSeconds && w.EndSeconds <= segment.EndSeconds)
                        .ToList();

                    if (segmentWords.Count > 0)
                    {
                        // Create new segment with words (records are immutable)
                        var index = segments.IndexOf(segment);
                        segments[index] = segment with { Words = segmentWords };
                    }
                }
            }

            return new TranscriptionResult
            {
                FullText = fullText.Trim(),
                Segments = segments,
                AudioDurationSeconds = audioDuration,
                LatencyMs = latencyMs,
                Language = language,
                StreamOffsetSeconds = streamOffset
            };
        }
        catch (JsonException ex)
        {
            OnError?.Invoke($"Failed to parse transcription response: {ex.Message}");
            return null;
        }
    }

    private byte[] BuildWav(byte[] pcm)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int byteRate = _options.SampleRate * 1 * 2; // mono, 16-bit
        short blockAlign = 2;

        bw.Write("RIFF"u8);
        bw.Write(36 + pcm.Length);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);                    // subchunk size
        bw.Write((short)1);              // PCM format
        bw.Write((short)1);              // mono
        bw.Write(_options.SampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)16);             // bits per sample
        bw.Write("data"u8);
        bw.Write(pcm.Length);
        bw.Write(pcm);

        return ms.ToArray();
    }

    /// <summary>
    /// Stops real-time transcription.
    /// </summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
            return;

        if (_audio != null)
        {
            _audio.OnAudioChunk -= HandleAudioChunk;
            _audio.Stop();
        }

        _cts?.Cancel();

        if (_transcriptionTask != null)
        {
            try { await _transcriptionTask.ConfigureAwait(false); }
            catch { }
        }

        _cts?.Dispose();
        _cts = null;

        _audioBuffer?.Dispose();
        _audioBuffer = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}
