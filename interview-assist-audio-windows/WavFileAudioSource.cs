using InterviewAssist.Library.Audio;
using NAudio.Wave;

namespace InterviewAssist.Audio.Windows;

/// <summary>
/// File-based audio source that reads a WAV file in real-time-paced chunks,
/// firing OnAudioChunk at the same rate as live audio capture.
/// </summary>
public sealed class WavFileAudioSource : IAudioCaptureService
{
    private readonly string _filePath;
    private readonly int _sampleRate;
    private readonly int _chunkDurationMs;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    public event Action<byte[]>? OnAudioChunk;

    /// <summary>
    /// Fires when the WAV file has been fully read.
    /// </summary>
    public event Action? OnPlaybackComplete;

    public WavFileAudioSource(string filePath, int sampleRate, int chunkDurationMs = 100)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _sampleRate = sampleRate;
        _chunkDurationMs = chunkDurationMs;
    }

    public void Start()
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _readTask?.Wait(); } catch { }
        _cts?.Dispose();
        _cts = null;
        _readTask = null;
    }

    public void SetSource(AudioInputSource source)
    {
        // No-op for file-based source
    }

    public AudioInputSource GetSource() => AudioInputSource.Microphone;

    public void Dispose() => Stop();

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        // Target output format: 16kHz mono 16-bit PCM
        // Chunk size = sampleRate * 2 bytes * chunkDurationMs / 1000
        var chunkBytes = _sampleRate * 2 * _chunkDurationMs / 1000;

        using var reader = new WaveFileReader(_filePath);
        var sourceFormat = reader.WaveFormat;

        // Check if we need resampling
        var needsResample = sourceFormat.SampleRate != _sampleRate
                            || sourceFormat.Channels != 1
                            || sourceFormat.BitsPerSample != 16
                            || sourceFormat.Encoding != WaveFormatEncoding.Pcm;

        if (needsResample)
        {
            // Read in source-format chunks, then resample each chunk
            await ReadWithResampleAsync(reader, sourceFormat, chunkBytes, ct);
        }
        else
        {
            // Already in target format — read directly
            await ReadDirectAsync(reader, chunkBytes, ct);
        }

        if (!ct.IsCancellationRequested)
        {
            OnPlaybackComplete?.Invoke();
        }
    }

    private async Task ReadDirectAsync(WaveFileReader reader, int chunkBytes, CancellationToken ct)
    {
        var buffer = new byte[chunkBytes];

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = reader.Read(buffer, 0, chunkBytes);
            if (bytesRead == 0) break;

            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            OnAudioChunk?.Invoke(chunk);

            await Task.Delay(_chunkDurationMs, ct).ConfigureAwait(false);
        }
    }

    private async Task ReadWithResampleAsync(
        WaveFileReader reader, WaveFormat sourceFormat, int targetChunkBytes, CancellationToken ct)
    {
        // Calculate how many source bytes produce one chunk of target audio
        var sourceBytesPerSecond = sourceFormat.AverageBytesPerSecond;
        var sourceChunkBytes = sourceBytesPerSecond * _chunkDurationMs / 1000;
        var buffer = new byte[sourceChunkBytes];

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = reader.Read(buffer, 0, sourceChunkBytes);
            if (bytesRead == 0) break;

            var resampled = AudioResampler.ResampleToMonoPcm16(buffer, bytesRead, sourceFormat, _sampleRate);
            if (resampled.Length > 0)
            {
                OnAudioChunk?.Invoke(resampled);
            }

            await Task.Delay(_chunkDurationMs, ct).ConfigureAwait(false);
        }
    }
}
