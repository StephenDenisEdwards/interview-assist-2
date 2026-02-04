using System.Diagnostics;
using InterviewAssist.Library.Audio;
using Microsoft.Extensions.Logging;

namespace InterviewAssist.Audio.Termux;

/// <summary>
/// Termux implementation of IAudioCaptureService using termux-microphone-record.
/// Requires Termux:API app and termux-api package installed.
/// Only supports microphone capture; loopback falls back to microphone with a warning.
/// </summary>
public sealed class TermuxAudioCaptureService : IAudioCaptureService
{
    private readonly int _sampleRate;
    private readonly ILogger<TermuxAudioCaptureService>? _logger;
    private readonly int _chunkSize;

    private Process? _process;
    private Thread? _readThread;
    private volatile bool _isRecording;
    private AudioInputSource _currentSource;
    private bool _disposed;

    public event Action<byte[]>? OnAudioChunk;

    public TermuxAudioCaptureService(
        int sampleRate = 16000,
        AudioInputSource initialSource = AudioInputSource.Microphone,
        ILogger<TermuxAudioCaptureService>? logger = null)
    {
        _sampleRate = sampleRate;
        _logger = logger;
        _currentSource = initialSource;

        // Chunk size: ~100ms of 16-bit mono audio
        _chunkSize = (sampleRate * 2) / 10;

        if (initialSource == AudioInputSource.Loopback)
        {
            _logger?.LogWarning("Loopback audio capture is not supported on Termux. Falling back to Microphone.");
            _currentSource = AudioInputSource.Microphone;
        }
    }

    public void SetSource(AudioInputSource source)
    {
        if (source == AudioInputSource.Loopback)
        {
            _logger?.LogWarning("Loopback audio capture is not supported on Termux. Falling back to Microphone.");
            source = AudioInputSource.Microphone;
        }

        if (_currentSource == source)
            return;

        bool wasRecording = _isRecording;
        if (wasRecording)
            Stop();

        _currentSource = source;

        if (wasRecording)
            Start();
    }

    public AudioInputSource GetSource() => _currentSource;

    public void Start()
    {
        if (_isRecording)
            return;

        _logger?.LogInformation("Starting Termux audio capture at {SampleRate}Hz", _sampleRate);

        try
        {
            // termux-microphone-record -f - outputs PCM to stdout
            // -r: sample rate, -c: channels (1=mono), -l: limit (0=unlimited)
            // Try Termux path first (for PRoot), then fall back to PATH
            var termuxCmd = File.Exists("/data/data/com.termux/files/usr/bin/termux-microphone-record")
                ? "/data/data/com.termux/files/usr/bin/termux-microphone-record"
                : "termux-microphone-record";

            var psi = new ProcessStartInfo
            {
                FileName = termuxCmd,
                Arguments = $"-f - -r {_sampleRate} -c 1 -l 0",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = Process.Start(psi);
            if (_process == null)
            {
                throw new InvalidOperationException("Failed to start termux-microphone-record process");
            }

            _isRecording = true;

            // Start thread to read audio data
            _readThread = new Thread(ReadAudioLoop)
            {
                IsBackground = true,
                Name = "TermuxAudioCapture"
            };
            _readThread.Start();

            // Log any stderr output
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_process.HasExited)
                    {
                        var line = await _process.StandardError.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line))
                        {
                            _logger?.LogWarning("[termux-microphone-record] {Message}", line);
                        }
                    }
                }
                catch { }
            });

            _logger?.LogInformation("Termux audio capture started");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start Termux audio capture. Is termux-api installed?");
            _isRecording = false;
            throw;
        }
    }

    public void Stop()
    {
        if (!_isRecording)
            return;

        _logger?.LogInformation("Stopping Termux audio capture");
        _isRecording = false;

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error stopping termux-microphone-record process");
        }

        try
        {
            _readThread?.Join(2000);
        }
        catch { }

        _process?.Dispose();
        _process = null;
        _readThread = null;

        _logger?.LogInformation("Termux audio capture stopped");
    }

    private void ReadAudioLoop()
    {
        var buffer = new byte[_chunkSize];

        try
        {
            var stream = _process?.StandardOutput.BaseStream;
            if (stream == null)
                return;

            while (_isRecording && _process != null && !_process.HasExited)
            {
                int totalRead = 0;
                while (totalRead < buffer.Length && _isRecording)
                {
                    int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                    if (read == 0)
                        break;
                    totalRead += read;
                }

                if (totalRead > 0)
                {
                    var chunk = new byte[totalRead];
                    Array.Copy(buffer, chunk, totalRead);

                    try
                    {
                        OnAudioChunk?.Invoke(chunk);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error in OnAudioChunk handler");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_isRecording)
            {
                _logger?.LogError(ex, "Error reading audio from termux-microphone-record");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }
}
