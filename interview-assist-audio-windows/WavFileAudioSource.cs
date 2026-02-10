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
    private readonly object _pauseLock = new();

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _delayCts;
    private TaskCompletionSource? _pauseTcs;
    private Task? _readTask;
    private bool _isPaused;

    public event Action<byte[]>? OnAudioChunk;

    /// <summary>
    /// Fires when the WAV file has been fully read.
    /// </summary>
    public event Action? OnPlaybackComplete;

    public bool IsPaused => _isPaused;

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
        lock (_pauseLock)
        {
            _isPaused = false;
            _pauseTcs?.TrySetResult();
            _pauseTcs = null;
        }
        _cts?.Cancel();
        try { _readTask?.Wait(); } catch { }
        _cts?.Dispose();
        _cts = null;
        _readTask = null;
    }

    public void Pause()
    {
        lock (_pauseLock)
        {
            if (_isPaused) return;
            _isPaused = true;
            _pauseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _delayCts?.Cancel();
        }
    }

    public void Resume()
    {
        lock (_pauseLock)
        {
            if (!_isPaused) return;
            _isPaused = false;
            _pauseTcs?.TrySetResult();
            _pauseTcs = null;
        }
    }

    public void TogglePause()
    {
        if (_isPaused)
            Resume();
        else
            Pause();
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
            // Block while paused
            await WaitIfPausedAsync(ct);
            ct.ThrowIfCancellationRequested();

            var bytesRead = reader.Read(buffer, 0, chunkBytes);
            if (bytesRead == 0) break;

            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            OnAudioChunk?.Invoke(chunk);

            await DelayWithPauseAsync(_chunkDurationMs, ct);
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
            // Block while paused
            await WaitIfPausedAsync(ct);
            ct.ThrowIfCancellationRequested();

            var bytesRead = reader.Read(buffer, 0, sourceChunkBytes);
            if (bytesRead == 0) break;

            var resampled = AudioResampler.ResampleToMonoPcm16(buffer, bytesRead, sourceFormat, _sampleRate);
            if (resampled.Length > 0)
            {
                OnAudioChunk?.Invoke(resampled);
            }

            await DelayWithPauseAsync(_chunkDurationMs, ct);
        }
    }

    private async Task WaitIfPausedAsync(CancellationToken ct)
    {
        TaskCompletionSource? tcs;
        lock (_pauseLock) { tcs = _pauseTcs; }
        if (tcs != null)
        {
            using var reg = ct.Register(() => tcs.TrySetCanceled());
            await tcs.Task;
        }
    }

    private async Task DelayWithPauseAsync(int ms, CancellationToken ct)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_pauseLock)
        {
            _delayCts = delayCts;
            if (_pauseTcs != null) delayCts.Cancel();
        }
        try
        {
            await Task.Delay(ms, delayCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Interrupted by pause — not a real cancellation
        }
        finally
        {
            lock (_pauseLock) { _delayCts = null; }
        }

        // Re-check pause after delay
        await WaitIfPausedAsync(ct);
    }
}
