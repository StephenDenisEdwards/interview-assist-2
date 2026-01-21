using System.Diagnostics;
using InterviewAssist.Library.IntegrationTests.Transcription;
using InterviewAssist.Pipeline;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace InterviewAssist.Library.IntegrationTests.Benchmarks;

/// <summary>
/// Latency benchmarks for TimestampedTranscriptionService.
/// Measures API response times across different audio durations and configurations.
/// </summary>
public class TranscriptionLatencyBenchmarks
{
    private readonly ITestOutputHelper _output;

    public TranscriptionLatencyBenchmarks(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task MeasureLatencyAcrossAudioDurations()
    {
        var service = CreateService();

        var durations = new[] { 1.0, 2.0, 3.0, 5.0, 10.0 };

        _output.WriteLine("=== Transcription Latency vs Audio Duration ===\n");
        _output.WriteLine($"{"Duration (s)",-15} {"Latency (ms)",-15} {"Realtime Factor",-15}");
        _output.WriteLine(new string('-', 50));

        var results = new List<(double Duration, long Latency)>();

        foreach (var duration in durations)
        {
            var audio = TestAudioGenerator.GenerateSineWave(16000, duration, 440);

            var sw = Stopwatch.StartNew();
            var result = await service.TranscribeAsync(audio);
            sw.Stop();

            var latency = result?.LatencyMs ?? sw.ElapsedMilliseconds;
            results.Add((duration, latency));

            var realtimeFactor = latency / 1000.0 / duration;
            _output.WriteLine($"{duration,-15:F1} {latency,-15} {realtimeFactor,-15:F2}x");
        }

        _output.WriteLine(new string('-', 50));

        // Calculate correlation between duration and latency
        var avgLatency = results.Average(r => r.Latency);
        var avgDuration = results.Average(r => r.Duration);
        _output.WriteLine($"\nAverage latency: {avgLatency:F0}ms");
        _output.WriteLine($"Average duration: {avgDuration:F1}s");
        _output.WriteLine($"Average realtime factor: {avgLatency / 1000.0 / avgDuration:F2}x");

        await service.DisposeAsync();
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task MeasureLatencyWithAndWithoutWordTimestamps()
    {
        var serviceWithoutWords = CreateService(new TimestampedTranscriptionOptions
        {
            IncludeWordTimestamps = false,
            Language = "en"
        });

        var serviceWithWords = CreateService(new TimestampedTranscriptionOptions
        {
            IncludeWordTimestamps = true,
            Language = "en"
        });

        _output.WriteLine("=== Word Timestamps Impact on Latency ===\n");

        var audio = TestAudioGenerator.GenerateSineWave(16000, 5.0, 440);
        const int iterations = 3;

        var withoutWordLatencies = new List<long>();
        var withWordLatencies = new List<long>();

        _output.WriteLine($"{"Iteration",-12} {"Without Words (ms)",-20} {"With Words (ms)",-20}");
        _output.WriteLine(new string('-', 55));

        for (int i = 1; i <= iterations; i++)
        {
            var result1 = await serviceWithoutWords.TranscribeAsync(audio);
            withoutWordLatencies.Add(result1?.LatencyMs ?? 0);

            var result2 = await serviceWithWords.TranscribeAsync(audio);
            withWordLatencies.Add(result2?.LatencyMs ?? 0);

            _output.WriteLine($"{i,-12} {result1?.LatencyMs ?? 0,-20} {result2?.LatencyMs ?? 0,-20}");
        }

        _output.WriteLine(new string('-', 55));
        _output.WriteLine($"{"Average",-12} {withoutWordLatencies.Average():F0,-20} {withWordLatencies.Average():F0,-20}");

        var overhead = withWordLatencies.Average() - withoutWordLatencies.Average();
        _output.WriteLine($"\nWord timestamp overhead: {overhead:F0}ms ({overhead / withoutWordLatencies.Average() * 100:F1}%)");

        await serviceWithoutWords.DisposeAsync();
        await serviceWithWords.DisposeAsync();
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task MeasureLatencyWithAndWithoutLanguageHint()
    {
        var serviceWithHint = CreateService(new TimestampedTranscriptionOptions
        {
            Language = "en"
        });

        var serviceWithoutHint = CreateService(new TimestampedTranscriptionOptions
        {
            Language = null
        });

        _output.WriteLine("=== Language Hint Impact on Latency ===\n");

        var audio = TestAudioGenerator.GenerateSineWave(16000, 5.0, 440);
        const int iterations = 3;

        var withHintLatencies = new List<long>();
        var withoutHintLatencies = new List<long>();

        _output.WriteLine($"{"Iteration",-12} {"With Hint (ms)",-20} {"Without Hint (ms)",-20}");
        _output.WriteLine(new string('-', 55));

        for (int i = 1; i <= iterations; i++)
        {
            var result1 = await serviceWithHint.TranscribeAsync(audio);
            withHintLatencies.Add(result1?.LatencyMs ?? 0);

            var result2 = await serviceWithoutHint.TranscribeAsync(audio);
            withoutHintLatencies.Add(result2?.LatencyMs ?? 0);

            _output.WriteLine($"{i,-12} {result1?.LatencyMs ?? 0,-20} {result2?.LatencyMs ?? 0,-20}");
        }

        _output.WriteLine(new string('-', 55));
        _output.WriteLine($"{"Average",-12} {withHintLatencies.Average():F0,-20} {withoutHintLatencies.Average():F0,-20}");

        var diff = withoutHintLatencies.Average() - withHintLatencies.Average();
        _output.WriteLine($"\nLanguage hint saves: {diff:F0}ms ({Math.Abs(diff) / withoutHintLatencies.Average() * 100:F1}%)");

        await serviceWithHint.DisposeAsync();
        await serviceWithoutHint.DisposeAsync();
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task MeasureConsistencyOver10Calls()
    {
        var service = CreateService();
        var audio = TestAudioGenerator.GenerateSineWave(16000, 3.0, 440);

        _output.WriteLine("=== Latency Consistency Over 10 Calls ===\n");
        _output.WriteLine($"Audio duration: 3.0s\n");

        var latencies = new List<long>();

        for (int i = 1; i <= 10; i++)
        {
            var result = await service.TranscribeAsync(audio);
            var latency = result?.LatencyMs ?? 0;
            latencies.Add(latency);
            _output.WriteLine($"Call {i,2}: {latency,5}ms");
        }

        _output.WriteLine(new string('-', 20));
        _output.WriteLine($"{"Average:",-10} {latencies.Average():F0}ms");
        _output.WriteLine($"{"Min:",-10} {latencies.Min()}ms");
        _output.WriteLine($"{"Max:",-10} {latencies.Max()}ms");
        _output.WriteLine($"{"Std Dev:",-10} {CalculateStdDev(latencies):F0}ms");
        _output.WriteLine($"{"P50:",-10} {Percentile(latencies, 50):F0}ms");
        _output.WriteLine($"{"P95:",-10} {Percentile(latencies, 95):F0}ms");
        _output.WriteLine($"{"P99:",-10} {Percentile(latencies, 99):F0}ms");

        await service.DisposeAsync();
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task MeasureColdStartVsWarmLatency()
    {
        _output.WriteLine("=== Cold Start vs Warm Latency ===\n");

        var audio = TestAudioGenerator.GenerateSineWave(16000, 3.0, 440);

        // Cold start - new service instance
        var coldService = CreateService();
        var coldResult = await coldService.TranscribeAsync(audio);
        var coldLatency = coldResult?.LatencyMs ?? 0;
        _output.WriteLine($"Cold start latency: {coldLatency}ms");

        // Warm calls - same service instance
        var warmLatencies = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            var result = await coldService.TranscribeAsync(audio);
            warmLatencies.Add(result?.LatencyMs ?? 0);
        }

        _output.WriteLine($"Warm avg latency:   {warmLatencies.Average():F0}ms");
        _output.WriteLine($"\nCold start overhead: {coldLatency - warmLatencies.Average():F0}ms");

        await coldService.DisposeAsync();
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task MeasureParallelTranscriptionLatency()
    {
        _output.WriteLine("=== Parallel vs Sequential Transcription ===\n");

        var audio = TestAudioGenerator.GenerateSineWave(16000, 2.0, 440);
        const int batchSize = 3;

        // Sequential
        var sequentialService = CreateService();
        var sequentialSw = Stopwatch.StartNew();
        for (int i = 0; i < batchSize; i++)
        {
            await sequentialService.TranscribeAsync(audio, streamOffsetSeconds: i * 2.0);
        }
        sequentialSw.Stop();
        await sequentialService.DisposeAsync();

        _output.WriteLine($"Sequential ({batchSize} calls): {sequentialSw.ElapsedMilliseconds}ms total");

        // Parallel
        var parallelServices = Enumerable.Range(0, batchSize)
            .Select(_ => CreateService())
            .ToList();

        var parallelSw = Stopwatch.StartNew();
        var tasks = parallelServices.Select((s, i) =>
            s.TranscribeAsync(audio, streamOffsetSeconds: i * 2.0));
        await Task.WhenAll(tasks);
        parallelSw.Stop();

        foreach (var s in parallelServices)
            await s.DisposeAsync();

        _output.WriteLine($"Parallel ({batchSize} calls):   {parallelSw.ElapsedMilliseconds}ms total");
        _output.WriteLine($"\nSpeedup: {(double)sequentialSw.ElapsedMilliseconds / parallelSw.ElapsedMilliseconds:F2}x");
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task GenerateLatencyReport()
    {
        _output.WriteLine("=== TRANSCRIPTION SERVICE LATENCY REPORT ===");
        _output.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

        var service = CreateService(new TimestampedTranscriptionOptions
        {
            Language = "en"
        });

        var testCases = new (string Name, double Duration)[]
        {
            ("1s audio", 1.0),
            ("2s audio", 2.0),
            ("3s audio", 3.0),
            ("5s audio", 5.0),
        };

        var reportRows = new List<TranscriptionLatencyResult>();

        foreach (var (name, duration) in testCases)
        {
            var audio = TestAudioGenerator.GenerateSineWave(16000, duration, 440);
            var latencies = new List<long>();

            // Run 5 iterations per test case
            for (int i = 0; i < 5; i++)
            {
                var result = await service.TranscribeAsync(audio);
                latencies.Add(result?.LatencyMs ?? 0);
            }

            reportRows.Add(new TranscriptionLatencyResult
            {
                TestName = name,
                AudioDurationSeconds = duration,
                Iterations = 5,
                AvgLatencyMs = latencies.Average(),
                MinLatencyMs = latencies.Min(),
                MaxLatencyMs = latencies.Max(),
                P50LatencyMs = Percentile(latencies, 50),
                P95LatencyMs = Percentile(latencies, 95),
                StdDevMs = CalculateStdDev(latencies)
            });
        }

        // Print table
        _output.WriteLine($"{"Test",-12} {"Duration",-10} {"Avg",-8} {"Min",-8} {"Max",-8} {"P50",-8} {"P95",-8} {"StdDev",-8} {"RTF",-6}");
        _output.WriteLine(new string('-', 85));

        foreach (var row in reportRows)
        {
            var rtf = row.AvgLatencyMs / 1000.0 / row.AudioDurationSeconds;
            _output.WriteLine($"{row.TestName,-12} {row.AudioDurationSeconds + "s",-10} {row.AvgLatencyMs:F0}ms{"",-3} {row.MinLatencyMs}ms{"",-3} {row.MaxLatencyMs}ms{"",-3} {row.P50LatencyMs:F0}ms{"",-3} {row.P95LatencyMs:F0}ms{"",-3} {row.StdDevMs:F0}ms{"",-3} {rtf:F2}x");
        }

        _output.WriteLine(new string('-', 85));

        // Summary statistics
        var overallAvg = reportRows.Average(r => r.AvgLatencyMs);
        var overallRtf = reportRows.Average(r => r.AvgLatencyMs / 1000.0 / r.AudioDurationSeconds);

        _output.WriteLine($"\nSummary:");
        _output.WriteLine($"  Average latency: {overallAvg:F0}ms");
        _output.WriteLine($"  Average realtime factor: {overallRtf:F2}x");
        _output.WriteLine($"  Note: RTF < 1.0 means faster than realtime");

        await service.DisposeAsync();
    }

    private static TimestampedTranscriptionService CreateService(TimestampedTranscriptionOptions? options = null)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<TranscriptionLatencyBenchmarks>(optional: true)
            .Build();

        var apiKey = config["OpenAI:ApiKey"]
            ?? config["OPENAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OpenAI API key not configured. Set OPENAI_API_KEY environment variable.");

        return new TimestampedTranscriptionService(apiKey, options);
    }

    private static double CalculateStdDev(List<long> values)
    {
        if (values.Count <= 1) return 0;
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / (values.Count - 1));
    }

    private static double Percentile(List<long> values, int percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, index)];
    }
}

/// <summary>
/// Latency result for reporting.
/// </summary>
internal class TranscriptionLatencyResult
{
    public required string TestName { get; init; }
    public required double AudioDurationSeconds { get; init; }
    public required int Iterations { get; init; }
    public required double AvgLatencyMs { get; init; }
    public required long MinLatencyMs { get; init; }
    public required long MaxLatencyMs { get; init; }
    public required double P50LatencyMs { get; init; }
    public required double P95LatencyMs { get; init; }
    public required double StdDevMs { get; init; }
}
