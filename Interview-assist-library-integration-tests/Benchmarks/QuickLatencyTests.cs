using System.Diagnostics;
using InterviewAssist.Library.Pipeline;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace InterviewAssist.Library.IntegrationTests.Benchmarks;

/// <summary>
/// Quick latency tests that can be run via xUnit.
/// For detailed benchmarks, use BenchmarkDotNet via the command line.
/// </summary>
public class QuickLatencyTests
{
    private readonly ITestOutputHelper _output;

    public QuickLatencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task MeasureLatencyAcrossTranscriptSizes()
    {
        var service = CreateService();

        var testCases = new (string Name, string Transcript)[]
        {
            ("Short (27 chars)", "What is dependency injection?"),
            ("Medium (200 chars)", "Okay great, thanks for joining us today. So, um, let me just pull up your resume here. Right, so I see you've been working with .NET for a few years now. What got you interested in software development?"),
            ("Long (350 chars)", "Alright, moving on to the technical portion. What's your experience with dependency injection? And how do you typically structure your applications? Do you follow any specific architectural patterns? I'm also curious about your testing philosophy - do you write unit tests first or after implementation? And what coverage targets do you aim for?"),
            ("Noisy (180 chars)", "Um so like uh what would you say is your uh biggest weakness as a developer and um how are you working to improve on that you know like what are you doing to get better"),
            ("No questions (150 chars)", "Alright, I think that covers everything on my end. Let me check my notes here. Yeah, I think we're good. Great job so far on the interview.")
        };

        _output.WriteLine("=== Latency Measurement ===\n");
        _output.WriteLine($"{"Test Case",-25} {"Chars",6} {"Latency",10} {"Detected",8}");
        _output.WriteLine(new string('-', 55));

        var latencies = new List<long>();

        foreach (var (name, transcript) in testCases)
        {
            var sw = Stopwatch.StartNew();
            var results = await service.DetectQuestionsAsync(transcript);
            sw.Stop();

            latencies.Add(sw.ElapsedMilliseconds);
            _output.WriteLine($"{name,-25} {transcript.Length,6} {sw.ElapsedMilliseconds + "ms",10} {results.Count,8}");
        }

        _output.WriteLine(new string('-', 55));
        _output.WriteLine($"{"Average",-25} {"",6} {latencies.Average():F0}ms");
        _output.WriteLine($"{"Min",-25} {"",6} {latencies.Min()}ms");
        _output.WriteLine($"{"Max",-25} {"",6} {latencies.Max()}ms");
        _output.WriteLine($"{"Std Dev",-25} {"",6} {CalculateStdDev(latencies):F0}ms");
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task MeasureLatencyOver10Calls()
    {
        var service = CreateService();
        var transcript = "What's your experience with dependency injection? And how do you typically structure your applications?";

        _output.WriteLine("=== Latency Over 10 Sequential Calls ===\n");
        _output.WriteLine($"Transcript: {transcript}\n");

        var latencies = new List<long>();

        for (var i = 1; i <= 10; i++)
        {
            var sw = Stopwatch.StartNew();
            var results = await service.DetectQuestionsAsync(transcript);
            sw.Stop();

            latencies.Add(sw.ElapsedMilliseconds);
            _output.WriteLine($"Call {i,2}: {sw.ElapsedMilliseconds,5}ms - {results.Count} detections");
        }

        _output.WriteLine($"\n{"Average:",-10} {latencies.Average():F0}ms");
        _output.WriteLine($"{"Min:",-10} {latencies.Min()}ms");
        _output.WriteLine($"{"Max:",-10} {latencies.Max()}ms");
        _output.WriteLine($"{"Std Dev:",-10} {CalculateStdDev(latencies):F0}ms");
        _output.WriteLine($"{"P50:",-10} {Percentile(latencies, 50):F0}ms");
        _output.WriteLine($"{"P95:",-10} {Percentile(latencies, 95):F0}ms");
    }

    [Fact(Skip = "Run manually - requires API key and makes API calls")]
    public async Task CompareModels()
    {
        var models = new[] { "gpt-4o-mini", "gpt-4o" };
        var transcript = "What's your experience with dependency injection? And how do you typically structure your applications? Do you follow any specific architectural patterns?";

        _output.WriteLine("=== Model Comparison ===\n");
        _output.WriteLine($"Transcript ({transcript.Length} chars): {transcript}\n");

        foreach (var model in models)
        {
            try
            {
                var service = CreateService(model);
                var latencies = new List<long>();
                var detectionCounts = new List<int>();

                for (var i = 0; i < 3; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var results = await service.DetectQuestionsAsync(transcript);
                    sw.Stop();

                    latencies.Add(sw.ElapsedMilliseconds);
                    detectionCounts.Add(results.Count);
                }

                _output.WriteLine($"Model: {model}");
                _output.WriteLine($"  Avg Latency: {latencies.Average():F0}ms");
                _output.WriteLine($"  Detections:  {detectionCounts.Average():F1} (avg over 3 calls)");
                _output.WriteLine("");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Model: {model} - FAILED: {ex.Message}\n");
            }
        }
    }

    private static IQuestionDetectionService CreateService(string? model = null)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<QuickLatencyTests>(optional: true)
            .Build();

        var apiKey = config["OpenAI:ApiKey"]
            ?? config["OPENAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OpenAI API key not configured. Set OPENAI_API_KEY environment variable.");

        model ??= config["Detection:DefaultModel"] ?? "gpt-4o-mini";

        return new OpenAiQuestionDetectionService(apiKey, model, 0.7);
    }

    private static double CalculateStdDev(List<long> values)
    {
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / values.Count);
    }

    private static double Percentile(List<long> values, int percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, index)];
    }
}
