using System.Net.Http.Headers;
using System.Text.Json;
using InterviewAssist.Library.Audio;

namespace InterviewAssist.Pipeline;

/// <summary>
/// Continuously transcribes audio using the Whisper API.
/// Fires OnTranscript immediately when transcription is ready.
/// </summary>
public sealed class TranscriptionService : IAsyncDisposable
{
    private readonly IAudioCaptureService _audio;
    private readonly string _apiKey;
    private readonly int _sampleRate;
    private readonly int _batchMs;
    private readonly HttpClient _http;

    private CancellationTokenSource? _cts;
    private Task? _transcriptionTask;
    private MemoryStream? _audioBuffer;
    private long _lastFlushTicks;
    private int _started;

    public event Action<string>? OnTranscript;
    public event Action<string>? OnError;

    public TranscriptionService(
        IAudioCaptureService audioCapture,
        string apiKey,
        int sampleRate = 24000,
        int batchMs = 3000)
    {
        _audio = audioCapture ?? throw new ArgumentNullException(nameof(audioCapture));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _sampleRate = sampleRate;
        _batchMs = batchMs;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("Already started");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _audioBuffer = new MemoryStream();
        _lastFlushTicks = Environment.TickCount64;

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
            if (elapsed >= _batchMs && _audioBuffer.Length > 0)
            {
                var audioData = _audioBuffer.ToArray();
                _audioBuffer.SetLength(0);
                _lastFlushTicks = Environment.TickCount64;

                // Fire transcription in background, don't block audio capture
                _ = TranscribeAsync(audioData, _cts.Token);
            }
        }
    }

    private async Task TranscribeAsync(byte[] pcmData, CancellationToken ct)
    {
        try
        {
            var wav = BuildWav(pcmData);

            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wav);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "audio.wav");
            content.Add(new StringContent("whisper-1"), "model");
            content.Add(new StringContent("json"), "response_format");

            using var response = await _http.PostAsync(
                "https://api.openai.com/v1/audio/transcriptions",
                content,
                ct).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                OnError?.Invoke($"Whisper API error: {response.StatusCode} - {body}");
                return;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    OnTranscript?.Invoke(text);
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

    private byte[] BuildWav(byte[] pcm)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int byteRate = _sampleRate * 1 * 2; // mono, 16-bit
        short blockAlign = 2;

        bw.Write("RIFF"u8);
        bw.Write(36 + pcm.Length);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);           // subchunk size
        bw.Write((short)1);     // PCM format
        bw.Write((short)1);     // mono
        bw.Write(_sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)16);    // bits per sample
        bw.Write("data"u8);
        bw.Write(pcm.Length);
        bw.Write(pcm);

        return ms.ToArray();
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
            return;

        _audio.OnAudioChunk -= HandleAudioChunk;
        _audio.Stop();

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
