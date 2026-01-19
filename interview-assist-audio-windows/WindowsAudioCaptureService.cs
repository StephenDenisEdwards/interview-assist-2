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
        try
        {
            if (bytesRecorded == 0) return Array.Empty<byte>();
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                int frameSize = 4 * format.Channels;
                int frames = bytesRecorded / frameSize;
                if (frames == 0) return Array.Empty<byte>();
                float[] floats = new float[frames * format.Channels];
                Buffer.BlockCopy(buffer, 0, floats, 0, frames * frameSize);
                double resampleRatio = (double)format.SampleRate / _sampleRate;
                int outFrames = (int)(frames / resampleRatio);
                if (outFrames <= 0) return Array.Empty<byte>();
                short[] outPcm = new short[outFrames];
                for (int i = 0; i < outFrames; i++)
                {
                    double srcIndex = i * resampleRatio;
                    int srcBase = (int)srcIndex;
                    double frac = srcIndex - srcBase;
                    if (srcBase >= frames - 1) srcBase = frames - 2;
                    double mixed0 = 0;
                    double mixed1 = 0;
                    for (int ch = 0; ch < format.Channels; ch++)
                    {
                        mixed0 += floats[srcBase * format.Channels + ch];
                        mixed1 += floats[(srcBase + 1) * format.Channels + ch];
                    }
                    mixed0 /= format.Channels;
                    mixed1 /= format.Channels;
                    double sample = mixed0 + frac * (mixed1 - mixed0);
                    sample = Math.Max(-1.0, Math.Min(1.0, sample));
                    outPcm[i] = (short)(sample * 32767);
                }
                byte[] outBytes = new byte[outFrames * 2];
                Buffer.BlockCopy(outPcm, 0, outBytes, 0, outBytes.Length);
                return outBytes;
            }
            if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
            {
                int frameSize = 2 * format.Channels;
                int frames = bytesRecorded / frameSize;
                if (frames == 0) return Array.Empty<byte>();
                short[] samples = new short[frames * format.Channels];
                Buffer.BlockCopy(buffer, 0, samples, 0, frames * frameSize);
                double resampleRatio = (double)format.SampleRate / _sampleRate;
                int outFrames = (int)(frames / resampleRatio);
                short[] outPcm = new short[outFrames];
                for (int i = 0; i < outFrames; i++)
                {
                    double srcIndex = i * resampleRatio;
                    int srcBase = (int)srcIndex;
                    double frac = srcIndex - srcBase;
                    if (srcBase >= frames - 1) srcBase = frames - 2;
                    double mixed0 = 0;
                    double mixed1 = 0;
                    for (int ch = 0; ch < format.Channels; ch++)
                    {
                        mixed0 += samples[srcBase * format.Channels + ch] / 32768.0;
                        mixed1 += samples[(srcBase + 1) * format.Channels + ch] / 32768.0;
                    }
                    mixed0 /= format.Channels;
                    mixed1 /= format.Channels;
                    double sample = mixed0 + frac * (mixed1 - mixed0);
                    sample = Math.Max(-1.0, Math.Min(1.0, sample));
                    outPcm[i] = (short)(sample * 32767);
                }
                byte[] outBytes = new byte[outFrames * 2];
                Buffer.BlockCopy(outPcm, 0, outBytes, 0, outBytes.Length);
                return outBytes;
            }
        }
        catch
        {
        }
        return Array.Empty<byte>();
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
