using NAudio.Wave;

namespace InterviewAssist.Audio.Windows;

/// <summary>
/// Utility class for resampling audio to mono PCM16 format.
/// </summary>
public static class AudioResampler
{
    /// <summary>
    /// Resamples audio data to mono 16-bit PCM at the target sample rate.
    /// Supports IEEE Float 32-bit and PCM 16-bit input formats.
    /// </summary>
    /// <param name="buffer">Input audio buffer.</param>
    /// <param name="bytesRecorded">Number of valid bytes in the buffer.</param>
    /// <param name="format">Wave format of the input audio.</param>
    /// <param name="targetSampleRate">Target sample rate in Hz.</param>
    /// <returns>Resampled audio as 16-bit PCM mono bytes, or empty array on failure.</returns>
    public static byte[] ResampleToMonoPcm16(byte[] buffer, int bytesRecorded, WaveFormat format, int targetSampleRate)
    {
        if (bytesRecorded == 0) return Array.Empty<byte>();

        try
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                return ResampleFloat32ToMonoPcm16(buffer, bytesRecorded, format, targetSampleRate);
            }

            if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
            {
                return ResamplePcm16ToMonoPcm16(buffer, bytesRecorded, format, targetSampleRate);
            }
        }
        catch
        {
            // Silently handle conversion errors
        }

        return Array.Empty<byte>();
    }

    private static byte[] ResampleFloat32ToMonoPcm16(byte[] buffer, int bytesRecorded, WaveFormat format, int targetSampleRate)
    {
        int frameSize = 4 * format.Channels;
        int frames = bytesRecorded / frameSize;
        if (frames == 0) return Array.Empty<byte>();

        float[] floats = new float[frames * format.Channels];
        Buffer.BlockCopy(buffer, 0, floats, 0, frames * frameSize);

        return ResampleAndMixToMono(
            frames,
            format.Channels,
            format.SampleRate,
            targetSampleRate,
            (frameIndex, channel) => floats[frameIndex * format.Channels + channel]);
    }

    private static byte[] ResamplePcm16ToMonoPcm16(byte[] buffer, int bytesRecorded, WaveFormat format, int targetSampleRate)
    {
        int frameSize = 2 * format.Channels;
        int frames = bytesRecorded / frameSize;
        if (frames == 0) return Array.Empty<byte>();

        short[] samples = new short[frames * format.Channels];
        Buffer.BlockCopy(buffer, 0, samples, 0, frames * frameSize);

        return ResampleAndMixToMono(
            frames,
            format.Channels,
            format.SampleRate,
            targetSampleRate,
            (frameIndex, channel) => samples[frameIndex * format.Channels + channel] / 32768.0);
    }

    private static byte[] ResampleAndMixToMono(
        int inputFrames,
        int channels,
        int sourceSampleRate,
        int targetSampleRate,
        Func<int, int, double> getSample)
    {
        double resampleRatio = (double)sourceSampleRate / targetSampleRate;
        int outputFrames = (int)(inputFrames / resampleRatio);
        if (outputFrames <= 0) return Array.Empty<byte>();

        short[] outputPcm = new short[outputFrames];

        for (int i = 0; i < outputFrames; i++)
        {
            double srcIndex = i * resampleRatio;
            int srcBase = (int)srcIndex;
            double frac = srcIndex - srcBase;

            // Clamp to valid range for interpolation
            if (srcBase >= inputFrames - 1) srcBase = inputFrames - 2;
            if (srcBase < 0) srcBase = 0;

            // Mix all channels to mono and interpolate
            double mixed0 = 0;
            double mixed1 = 0;
            for (int ch = 0; ch < channels; ch++)
            {
                mixed0 += getSample(srcBase, ch);
                mixed1 += getSample(srcBase + 1, ch);
            }
            mixed0 /= channels;
            mixed1 /= channels;

            // Linear interpolation
            double sample = mixed0 + frac * (mixed1 - mixed0);

            // Clamp and convert to 16-bit PCM
            sample = Math.Max(-1.0, Math.Min(1.0, sample));
            outputPcm[i] = (short)(sample * 32767);
        }

        byte[] outputBytes = new byte[outputFrames * 2];
        Buffer.BlockCopy(outputPcm, 0, outputBytes, 0, outputBytes.Length);
        return outputBytes;
    }
}
