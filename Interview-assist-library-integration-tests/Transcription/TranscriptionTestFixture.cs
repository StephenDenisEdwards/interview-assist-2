using Microsoft.Extensions.Configuration;
using InterviewAssist.Pipeline;

namespace InterviewAssist.Library.IntegrationTests.Transcription;

/// <summary>
/// Shared fixture for transcription integration tests.
/// </summary>
public class TranscriptionTestFixture : IDisposable
{
    public string ApiKey { get; }
    public bool IsConfigured { get; }

    public TranscriptionTestFixture()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<TranscriptionTestFixture>(optional: true)
            .Build();

        ApiKey = GetFirstNonEmpty(
	                 config["OpenAI:ApiKey"],
	                 config["OPENAI_API_KEY"],
	                 Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                 ?? string.Empty;

		// ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
		IsConfigured = !string.IsNullOrWhiteSpace(ApiKey);
    }

    private static string? GetFirstNonEmpty(params string?[] values)
    {
	    foreach (var value in values)
	    {
		    if (!string.IsNullOrWhiteSpace(value))
			    return value;
	    }
	    return null;
    }

	/// <summary>
	/// Creates a transcription service with default options.
	/// </summary>
	public TimestampedTranscriptionService CreateService(TimestampedTranscriptionOptions? options = null)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("OpenAI API key not configured");

        return new TimestampedTranscriptionService(ApiKey, options);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Helper for generating test audio data.
/// </summary>
public static class TestAudioGenerator
{
    /// <summary>
    /// Generates a sine wave tone as 16-bit mono PCM.
    /// Useful for testing the audio pipeline without speech.
    /// </summary>
    public static byte[] GenerateSineWave(int sampleRate, double durationSeconds, double frequencyHz = 440)
    {
        var sampleCount = (int)(sampleRate * durationSeconds);
        var pcm = new byte[sampleCount * 2]; // 16-bit = 2 bytes per sample

        for (var i = 0; i < sampleCount; i++)
        {
            var t = (double)i / sampleRate;
            var sample = (short)(Math.Sin(2 * Math.PI * frequencyHz * t) * 16000);
            pcm[i * 2] = (byte)(sample & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return pcm;
    }

    /// <summary>
    /// Generates silence as 16-bit mono PCM.
    /// </summary>
    public static byte[] GenerateSilence(int sampleRate, double durationSeconds)
    {
        var sampleCount = (int)(sampleRate * durationSeconds);
        return new byte[sampleCount * 2]; // All zeros = silence
    }

    /// <summary>
    /// Creates a WAV file from PCM data.
    /// </summary>
    public static byte[] CreateWavFile(byte[] pcm, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int byteRate = sampleRate * 1 * 2;
        short blockAlign = 2;

        bw.Write("RIFF"u8);
        bw.Write(36 + pcm.Length);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(pcm.Length);
        bw.Write(pcm);

        return ms.ToArray();
    }

    /// <summary>
    /// Saves PCM data as a WAV file.
    /// </summary>
    public static string SaveAsWavFile(byte[] pcm, int sampleRate, string? path = null)
    {
        path ??= Path.Combine(Path.GetTempPath(), $"test_audio_{Guid.NewGuid():N}.wav");
        var wav = CreateWavFile(pcm, sampleRate);
        File.WriteAllBytes(path, wav);
        return path;
    }
}

/// <summary>
/// Transcription test result for reporting.
/// </summary>
public class TranscriptionTestResult
{
    public required string TestName { get; init; }
    public required double AudioDurationSeconds { get; init; }
    public required long LatencyMs { get; init; }
    public required int SegmentCount { get; init; }
    public required int WordCount { get; init; }
    public required string TranscribedText { get; init; }
    public bool HasTimestamps { get; init; }
    public bool HasWordTimestamps { get; init; }
    public double? FirstSegmentStart { get; init; }
    public double? LastSegmentEnd { get; init; }

    public double LatencyRatio => AudioDurationSeconds > 0
        ? LatencyMs / 1000.0 / AudioDurationSeconds
        : 0;

    public override string ToString()
    {
        return $"{TestName}: {LatencyMs}ms for {AudioDurationSeconds:F1}s audio ({LatencyRatio:F2}x realtime), {SegmentCount} segments, {WordCount} words";
    }
}
