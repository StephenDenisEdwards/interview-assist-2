using System.Diagnostics.Metrics;
using NAudio.Wave;

namespace InterviewAssist.Audio.Windows;

/// <summary>
/// Provides diagnostics and metrics for audio resampling operations.
/// </summary>
public static class AudioResamplerDiagnostics
{
    private const string MeterName = "InterviewAssist.Audio.Windows";

    private static readonly Meter s_meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> s_conversionFailures = s_meter.CreateCounter<long>(
        name: "interview_assist.audio.conversion_failures",
        unit: "{failures}",
        description: "Number of audio conversion failures");

    private static readonly Counter<long> s_unsupportedFormats = s_meter.CreateCounter<long>(
        name: "interview_assist.audio.unsupported_formats",
        unit: "{occurrences}",
        description: "Number of unsupported audio format occurrences");

    /// <summary>
    /// Logs and records an audio conversion error.
    /// </summary>
    /// <param name="exception">The exception that occurred during conversion.</param>
    /// <param name="format">The wave format that was being processed.</param>
    /// <param name="bytesRecorded">The number of bytes in the buffer.</param>
    /// <param name="targetSampleRate">The target sample rate.</param>
    public static void LogConversionError(
        Exception exception,
        WaveFormat format,
        int bytesRecorded,
        int targetSampleRate)
    {
        s_conversionFailures.Add(1);

        // Log to debug output - consumers can subscribe to this via diagnostics
        System.Diagnostics.Debug.WriteLine(
            $"[AudioResampler] Conversion error: {exception.Message} | " +
            $"Format: {format.Encoding}, {format.BitsPerSample}bit, {format.SampleRate}Hz, {format.Channels}ch | " +
            $"BytesRecorded: {bytesRecorded}, TargetRate: {targetSampleRate}Hz");
    }

    /// <summary>
    /// Logs and records an unsupported audio format.
    /// </summary>
    /// <param name="format">The unsupported wave format.</param>
    public static void LogUnsupportedFormat(WaveFormat format)
    {
        s_unsupportedFormats.Add(1);

        System.Diagnostics.Debug.WriteLine(
            $"[AudioResampler] Unsupported format: {format.Encoding}, {format.BitsPerSample}bit, {format.SampleRate}Hz, {format.Channels}ch");
    }

    /// <summary>
    /// Records a conversion failure metric.
    /// </summary>
    public static void RecordConversionFailure() => s_conversionFailures.Add(1);

    /// <summary>
    /// Records an unsupported format metric.
    /// </summary>
    public static void RecordUnsupportedFormat() => s_unsupportedFormats.Add(1);
}
