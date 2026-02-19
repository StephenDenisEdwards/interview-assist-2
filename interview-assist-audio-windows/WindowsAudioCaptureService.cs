using System;
using System.Linq;
using InterviewAssist.Library.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace InterviewAssist.Audio.Windows;

public sealed class WindowsAudioCaptureService : IAudioCaptureService
{
    private WasapiCapture? _waveIn;
    private WasapiLoopbackCapture? _loopback;
    public event Action<byte[]>? OnAudioChunk;
    private readonly int _sampleRate;
    private readonly string? _micDeviceId;
    private readonly string? _micDeviceName;
    private AudioInputSource _source;
    private bool _isStarted;

    public WindowsAudioCaptureService(
        int sampleRate = 16000,
        AudioInputSource initialSource = AudioInputSource.Microphone,
        string? micDeviceId = null,
        string? micDeviceName = null)
    {
        _sampleRate = sampleRate;
        _source = initialSource;
        _micDeviceId = string.IsNullOrWhiteSpace(micDeviceId) ? null : micDeviceId.Trim();
        _micDeviceName = string.IsNullOrWhiteSpace(micDeviceName) ? null : micDeviceName.Trim();
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
            var captureDevice = ResolveMicrophoneDevice();
            _waveIn = new WasapiCapture(captureDevice);
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
        if (_waveIn == null) return;
        var format = _waveIn.WaveFormat;
        var converted = ConvertLoopbackBuffer(e.Buffer, e.BytesRecorded, format);
        if (converted.Length > 0)
        {
            OnAudioChunk?.Invoke(converted);
        }
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

    private MMDevice ResolveMicrophoneDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(_micDeviceId))
        {
            return enumerator.GetDevice(_micDeviceId);
        }

        if (!string.IsNullOrWhiteSpace(_micDeviceName))
        {
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var matched = captureDevices.FirstOrDefault(d =>
                d.FriendlyName.Contains(_micDeviceName, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }

            throw new InvalidOperationException(
                $"No active capture device matched mic-device-name '{_micDeviceName}'.");
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
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
