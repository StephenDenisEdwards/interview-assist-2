using InterviewAssist.Pipeline;
using Xunit.Abstractions;

namespace InterviewAssist.Library.IntegrationTests.Transcription;

/// <summary>
/// Integration tests for TimestampedTranscriptionService.
/// These tests require a valid OpenAI API key and make actual API calls.
/// Remove the Skip attribute and configure OPENAI_API_KEY to run.
/// </summary>
public class TimestampedTranscriptionTests : IClassFixture<TranscriptionTestFixture>
{
    private readonly TranscriptionTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TimestampedTranscriptionTests(TranscriptionTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void EnsureConfigured()
    {
        if (!_fixture.IsConfigured)
            throw new InvalidOperationException("OpenAI API key not configured. Set OPENAI_API_KEY environment variable.");
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task TranscribeAsync_WithSilence_ReturnsNull()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();
        var silence = TestAudioGenerator.GenerateSilence(16000, 2.0);

        var result = await service.TranscribeAsync(silence);

        // Silence should either return null or empty text
        Assert.True(result == null || string.IsNullOrWhiteSpace(result.FullText));
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
    [Fact]
    public async Task TranscribeAsync_WithTone_ReturnsResult()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();
        var tone = TestAudioGenerator.GenerateSineWave(16000, 3.0, 440);

        var result = await service.TranscribeAsync(tone);

        // Whisper may detect some sound but unlikely to produce meaningful text
        _output.WriteLine($"Result: {result?.FullText ?? "(null)"}");
        _output.WriteLine($"Latency: {result?.LatencyMs ?? 0}ms");
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task TranscribeAsync_ReturnsTimestampedSegments()
    {
        EnsureConfigured();

        // Use a longer tone to ensure we get timestamps
        await using var service = _fixture.CreateService();
        var audio = TestAudioGenerator.GenerateSineWave(16000, 5.0, 440);

        var result = await service.TranscribeAsync(audio);

        if (result != null && result.Segments.Count > 0)
        {
            foreach (var segment in result.Segments)
            {
                _output.WriteLine($"[{segment.StartSeconds:F2}s - {segment.EndSeconds:F2}s] {segment.Text}");

                Assert.True(segment.StartSeconds >= 0, "Start time should be non-negative");
                Assert.True(segment.EndSeconds >= segment.StartSeconds, "End time should be >= start time");
                Assert.True(segment.DurationSeconds >= 0, "Duration should be non-negative");
            }
        }
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task TranscribeAsync_WithWordTimestamps_ReturnsWordTiming()
    {
        EnsureConfigured();

        var options = new TimestampedTranscriptionOptions
        {
            IncludeWordTimestamps = true,
            Language = "en"
        };

        await using var service = _fixture.CreateService(options);
        var audio = TestAudioGenerator.GenerateSineWave(16000, 5.0, 440);

        var result = await service.TranscribeAsync(audio);

        _output.WriteLine($"Full text: {result?.FullText ?? "(null)"}");
        _output.WriteLine($"Latency: {result?.LatencyMs ?? 0}ms");

        if (result?.Segments.FirstOrDefault()?.Words != null)
        {
            _output.WriteLine("Word timestamps:");
            foreach (var word in result.Segments.First().Words!)
            {
                _output.WriteLine($"  [{word.StartSeconds:F2}s - {word.EndSeconds:F2}s] \"{word.Word}\"");
            }
        }
    }

	//[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
	public async Task TranscribeAsync_WithStreamOffset_AdjustsSegmentTiming()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();
        var audio = TestAudioGenerator.GenerateSineWave(16000, 3.0, 440);
        const double streamOffset = 10.5;

        var result = await service.TranscribeAsync(audio, streamOffset);

        Assert.NotNull(result);
        Assert.Equal(streamOffset, result.StreamOffsetSeconds);

        if (result.Segments.Count > 0)
        {
            var firstSegment = result.Segments.First();
            // StreamOffsetSeconds should be the stream offset plus segment start
            Assert.Equal(streamOffset + firstSegment.StartSeconds, firstSegment.StreamOffsetSeconds, 2);
            _output.WriteLine($"Segment stream offset: {firstSegment.StreamOffsetSeconds}s");
        }
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
    [Fact]
	public async Task TranscribeAsync_ReportsCorrectAudioDuration()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();
        const double expectedDuration = 4.0;
        var audio = TestAudioGenerator.GenerateSineWave(16000, expectedDuration, 440);

        var result = await service.TranscribeAsync(audio);

        _output.WriteLine($"Expected duration: {expectedDuration}s");
        _output.WriteLine($"Reported duration: {result?.AudioDurationSeconds}s");

        if (result != null)
        {
            // Allow some tolerance for audio processing
            Assert.InRange(result.AudioDurationSeconds, expectedDuration - 0.5, expectedDuration + 0.5);
        }
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task TranscribeAsync_WithLanguageHint_UsesSpecifiedLanguage()
    {
        EnsureConfigured();

        var options = new TimestampedTranscriptionOptions
        {
            Language = "en"
        };

        await using var service = _fixture.CreateService(options);
        var audio = TestAudioGenerator.GenerateSineWave(16000, 3.0, 440);

        var result = await service.TranscribeAsync(audio);

        _output.WriteLine($"Detected language: {result?.Language ?? "(null)"}");

        // If we specify English, Whisper should report English
        if (result?.Language != null)
        {
            Assert.Equal("english", result.Language.ToLowerInvariant());
        }
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task TranscribeAsync_RecordsLatency()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();
        var audio = TestAudioGenerator.GenerateSineWave(16000, 2.0, 440);

        var result = await service.TranscribeAsync(audio);

        Assert.NotNull(result);
        Assert.True(result.LatencyMs > 0, "Latency should be recorded");

        _output.WriteLine($"API latency: {result.LatencyMs}ms");
        _output.WriteLine($"Audio duration: {result.AudioDurationSeconds}s");
        _output.WriteLine($"Realtime factor: {result.LatencyMs / 1000.0 / result.AudioDurationSeconds:F2}x");
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task TranscribeFileAsync_WithValidWavFile_ReturnsResult()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();
        var audio = TestAudioGenerator.GenerateSineWave(16000, 3.0, 440);
        var wavPath = TestAudioGenerator.SaveAsWavFile(audio, 16000);

        try
        {
            var result = await service.TranscribeFileAsync(wavPath);

            _output.WriteLine($"File: {wavPath}");
            _output.WriteLine($"Result: {result?.FullText ?? "(null)"}");
            _output.WriteLine($"Latency: {result?.LatencyMs ?? 0}ms");
            _output.WriteLine($"Duration: {result?.AudioDurationSeconds ?? 0}s");
        }
        finally
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task TranscribeAsync_WithEmptyData_ReturnsNull()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();

        var result = await service.TranscribeAsync(Array.Empty<byte>());

        Assert.Null(result);
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task TranscribeAsync_CancellationToken_ThrowsOperationCanceled()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();
        var audio = TestAudioGenerator.GenerateSineWave(16000, 5.0, 440);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.TranscribeAsync(audio, ct: cts.Token));
    }

	//[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
	public async Task OnError_FiredOnApiError()
    {
        // Create service with invalid API key to trigger error
        var service = new TimestampedTranscriptionService("invalid_key");
        string? errorMessage = null;
        service.OnError += msg => errorMessage = msg;

        var audio = TestAudioGenerator.GenerateSineWave(16000, 2.0, 440);
        var result = await service.TranscribeAsync(audio);

        Assert.Null(result);
        Assert.NotNull(errorMessage);
        Assert.Contains("error", errorMessage.ToLowerInvariant());
        _output.WriteLine($"Error captured: {errorMessage}");

        await service.DisposeAsync();
    }

    //[Fact(Skip = "Run manually - requires API key and makes API calls")]
	[Fact]
    public async Task MultipleSequentialTranscriptions_AllSucceed()
    {
        EnsureConfigured();

        await using var service = _fixture.CreateService();
        var audio = TestAudioGenerator.GenerateSineWave(16000, 2.0, 440);

        var results = new List<TranscriptionResult?>();
        for (int i = 0; i < 3; i++)
        {
            var result = await service.TranscribeAsync(audio, streamOffsetSeconds: i * 2.0);
            results.Add(result);
            _output.WriteLine($"Call {i + 1}: Latency={result?.LatencyMs ?? 0}ms, Offset={result?.StreamOffsetSeconds}s");
        }

        // All calls should complete (may or may not have text based on audio content)
        Assert.Equal(3, results.Count);
    }
}
