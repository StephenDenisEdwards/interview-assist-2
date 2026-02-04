using Android.Media;
using InterviewAssist.Library.Audio;
using Microsoft.Extensions.Logging;

namespace Interview_assist_audio_android;

/// <summary>
/// Android implementation of IAudioCaptureService using AudioRecord API.
/// Only supports microphone capture; loopback falls back to microphone with a warning.
/// </summary>
public sealed class AndroidAudioCaptureService : IAudioCaptureService
{
    private readonly int _sampleRate;
    private readonly ILogger<AndroidAudioCaptureService>? _logger;
    private readonly object _lock = new();

    private AudioRecord? _audioRecord;
    private Thread? _recordingThread;
    private volatile bool _isRecording;
    private AudioInputSource _currentSource;
    private int _bufferSize;
    private bool _disposed;

    private const ChannelIn Channel = ChannelIn.Mono;
    private const Encoding AudioEncoding = Encoding.Pcm16bit;

    public event Action<byte[]>? OnAudioChunk;

    public AndroidAudioCaptureService(
        int sampleRate = 16000,
        AudioInputSource initialSource = AudioInputSource.Microphone,
        ILogger<AndroidAudioCaptureService>? logger = null)
    {
        _sampleRate = sampleRate;
        _logger = logger;
        _currentSource = initialSource;

        if (initialSource == AudioInputSource.Loopback)
        {
            _logger?.LogWarning("Loopback audio capture is not supported on Android. Falling back to Microphone.");
            _currentSource = AudioInputSource.Microphone;
        }
    }

    public void SetSource(AudioInputSource source)
    {
        lock (_lock)
        {
            if (source == AudioInputSource.Loopback)
            {
                _logger?.LogWarning("Loopback audio capture is not supported on Android. Falling back to Microphone.");
                source = AudioInputSource.Microphone;
            }

            if (_currentSource == source)
            {
                return;
            }

            bool wasRecording = _isRecording;
            if (wasRecording)
            {
                StopInternal();
            }

            _currentSource = source;

            if (wasRecording)
            {
                StartInternal();
            }
        }
    }

    public AudioInputSource GetSource()
    {
        return _currentSource;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRecording)
            {
                return;
            }

            StartInternal();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRecording)
            {
                return;
            }

            StopInternal();
        }
    }

    private void StartInternal()
    {
        try
        {
            _bufferSize = AudioRecord.GetMinBufferSize(_sampleRate, Channel, AudioEncoding);
            if (_bufferSize <= 0)
            {
                _bufferSize = _sampleRate * 2; // Fallback: 1 second of 16-bit mono audio
                _logger?.LogWarning("GetMinBufferSize returned invalid value, using fallback buffer size: {BufferSize}", _bufferSize);
            }

            _audioRecord = new AudioRecord(
                AudioSource.Mic,
                _sampleRate,
                Channel,
                AudioEncoding,
                _bufferSize);

            if (_audioRecord.State != State.Initialized)
            {
                _logger?.LogError("Failed to initialize AudioRecord. State: {State}", _audioRecord.State);
                _audioRecord.Release();
                _audioRecord = null;
                throw new InvalidOperationException("Failed to initialize AudioRecord. Ensure RECORD_AUDIO permission is granted.");
            }

            _isRecording = true;
            _audioRecord.StartRecording();

            _recordingThread = new Thread(RecordingLoop)
            {
                IsBackground = true,
                Name = "AndroidAudioCapture"
            };
            _recordingThread.Start();

            _logger?.LogInformation("Started audio capture at {SampleRate}Hz", _sampleRate);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error starting audio capture");
            CleanupAudioRecord();
            throw;
        }
    }

    private void StopInternal()
    {
        _isRecording = false;

        try
        {
            _recordingThread?.Join(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error waiting for recording thread to stop");
        }

        _recordingThread = null;
        CleanupAudioRecord();

        _logger?.LogInformation("Stopped audio capture");
    }

    private void CleanupAudioRecord()
    {
        try
        {
            if (_audioRecord != null)
            {
                if (_audioRecord.RecordingState == RecordState.Recording)
                {
                    _audioRecord.Stop();
                }
                _audioRecord.Release();
                _audioRecord = null;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error cleaning up AudioRecord");
        }
    }

    private void RecordingLoop()
    {
        var buffer = new byte[_bufferSize];

        while (_isRecording)
        {
            try
            {
                if (_audioRecord == null)
                {
                    break;
                }

                int bytesRead = _audioRecord.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    var chunk = new byte[bytesRead];
                    Array.Copy(buffer, chunk, bytesRead);

                    try
                    {
                        OnAudioChunk?.Invoke(chunk);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error in OnAudioChunk handler");
                    }
                }
                else if (bytesRead < 0)
                {
                    _logger?.LogWarning("AudioRecord.Read returned error: {ErrorCode}", bytesRead);
                }
            }
            catch (Exception ex)
            {
                if (_isRecording)
                {
                    _logger?.LogError(ex, "Error in recording loop");
                }
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
