using System;
using InterviewAssist.Library.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace InterviewAssist.Audio.Windows;

public sealed class WindowsAudioCaptureService : IAudioCaptureService
{
    private WaveInEvent? _waveIn;
    private WasapiLoopbackCapture? _loopback;
    public event Action<byte[]>? OnAudioChunk;
    private readonly int _sampleRate;
    private AudioInputSource _source;
    private bool _isStarted;

    public WindowsAudioCaptureService(int sampleRate = 16000, AudioInputSource initialSource = AudioInputSource.Microphone)
    {
        _sampleRate = sampleRate;
        _source = initialSource;
    }

    public void SetSource(AudioInputSource source)
    {
        if (_source == source) return;
        bool restart = _isStarted;
        StopInternal();
        _source = source;
        if (restart) Start();
    }

    public AudioInputSource GetSource() => _source;

    public void Start()
    {
        if (_isStarted) return;
        _isStarted = true;
        if (_source == AudioInputSource.Microphone)
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(_sampleRate, 16, 1),
                BufferMilliseconds = 100
            };
            _waveIn.DataAvailable += HandleMicData;
            _waveIn.StartRecording();
        }
        else
        {
            _loopback = new WasapiLoopbackCapture();
            _loopback.DataAvailable += HandleLoopbackData;
            _loopback.StartRecording();
        }
    }

    private void HandleMicData(object? sender, WaveInEventArgs e)
    {
        var chunk = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, chunk, e.BytesRecorded);
        OnAudioChunk?.Invoke(chunk);
    }

    private void HandleLoopbackData(object? sender, WaveInEventArgs e)
    {
        if (_loopback == null) return;
        var format = _loopback.WaveFormat;
        var converted = ConvertLoopbackBuffer(e.Buffer, e.BytesRecorded, format);
        if (converted.Length > 0)
        {
            OnAudioChunk?.Invoke(converted);
        }
    }

    private byte[] ConvertLoopbackBuffer(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        return AudioResampler.ResampleToMonoPcm16(buffer, bytesRecorded, format, _sampleRate);
    }

    public void Stop() => StopInternal();

    private void StopInternal()
    {
        if (!_isStarted) return;
        _isStarted = false;
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= HandleMicData;
            try { _waveIn.StopRecording(); } catch { }
            _waveIn.Dispose();
            _waveIn = null;
        }
        if (_loopback != null)
        {
            _loopback.DataAvailable -= HandleLoopbackData;
            try { _loopback.StopRecording(); } catch { }
            _loopback.Dispose();
            _loopback = null;
        }
    }

    public void Dispose() => StopInternal();
}
