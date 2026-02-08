using InterviewAssist.Library.Audio;
using NAudio.Wave;

namespace InterviewAssist.Audio.Windows;

/// <summary>
/// Records raw audio from <see cref="IAudioCaptureService"/> to a WAV file.
/// Audio is written as 16-bit mono PCM at the configured sample rate.
/// </summary>
public sealed class AudioFileRecorder : IDisposable
{
    private readonly IAudioCaptureService _audioService;
    private readonly int _sampleRate;
    private readonly object _lock = new();

    private WaveFileWriter? _writer;
    private bool _isRecording;

    public bool IsRecording => _isRecording;

    public AudioFileRecorder(IAudioCaptureService audioService, int sampleRate)
    {
        _audioService = audioService;
        _sampleRate = sampleRate;
    }

    /// <summary>
    /// Start recording audio to the specified WAV file path.
    /// </summary>
    public void Start(string filePath)
    {
        lock (_lock)
        {
            if (_isRecording) return;

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var format = new WaveFormat(_sampleRate, 16, 1);
            _writer = new WaveFileWriter(filePath, format);
            _isRecording = true;

            _audioService.OnAudioChunk += OnAudioChunk;
        }
    }

    /// <summary>
    /// Stop recording and finalize the WAV file.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRecording) return;

            _audioService.OnAudioChunk -= OnAudioChunk;
            _isRecording = false;

            _writer?.Dispose();
            _writer = null;
        }
    }

    private void OnAudioChunk(byte[] data)
    {
        lock (_lock)
        {
            _writer?.Write(data, 0, data.Length);
        }
    }

    public void Dispose() => Stop();
}
