using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using InterviewAssist.Library.Pipeline;
using Microsoft.Extensions.Configuration;

namespace InterviewAssist.Library.IntegrationTests.Benchmarks;

/// <summary>
/// Benchmarks for question detection latency.
/// Run with: dotnet run -c Release --project Interview-assist-library-integration-tests -- --filter "*DetectionBenchmarks*"
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class DetectionBenchmarks
{
    private IQuestionDetectionService _service = null!;

    // Test transcripts of varying complexity
    private const string ShortSimple = "What is dependency injection?";

    private const string MediumWithFiller = "Okay great, thanks for joining us today. So, um, let me just pull up your resume here. Right, so I see you've been working with .NET for a few years now. What got you interested in software development in the first place?";

    private const string LongMultiQuestion = "Alright, moving on to the technical portion. What's your experience with dependency injection? And how do you typically structure your applications? Do you follow any specific architectural patterns? I'm also curious about your testing philosophy - do you write unit tests first or after implementation?";

    private const string NoisyTranscript = "Um so like uh what would you say is your uh biggest weakness as a developer and um how are you working to improve on that you know like what are you doing to get better";

    private const string NoQuestions = "Alright, I think that covers everything on my end. Let me check my notes here. Yeah, I think we're good. My colleague Sarah will be joining us in a moment for the next part. Great job so far.";

    [GlobalSetup]
    public void Setup()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<DetectionBenchmarks>(optional: true)
            .Build();

        var apiKey = config["OpenAI:ApiKey"]
            ?? config["OPENAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OpenAI API key not configured");

        var model = config["Detection:DefaultModel"] ?? "gpt-4o-mini";

        _service = new OpenAiQuestionDetectionService(apiKey, model, 0.7);
    }

    [Benchmark(Baseline = true, Description = "Short simple question")]
    public async Task<int> ShortSimpleQuestion()
    {
        var result = await _service.DetectQuestionsAsync(ShortSimple);
        return result.Count;
    }

    [Benchmark(Description = "Medium transcript with filler")]
    public async Task<int> MediumTranscriptWithFiller()
    {
        var result = await _service.DetectQuestionsAsync(MediumWithFiller);
        return result.Count;
    }

    [Benchmark(Description = "Long multi-question transcript")]
    public async Task<int> LongMultiQuestionTranscript()
    {
        var result = await _service.DetectQuestionsAsync(LongMultiQuestion);
        return result.Count;
    }

    [Benchmark(Description = "Noisy/disfluent transcript")]
    public async Task<int> NoisyDisfluent()
    {
        var result = await _service.DetectQuestionsAsync(NoisyTranscript);
        return result.Count;
    }

    [Benchmark(Description = "No questions (negative case)")]
    public async Task<int> NoQuestionsNegativeCase()
    {
        var result = await _service.DetectQuestionsAsync(NoQuestions);
        return result.Count;
    }
}

/// <summary>
/// Custom benchmark configuration for API-bound tests.
/// Uses fewer iterations since we're measuring network latency, not CPU.
/// </summary>
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(1)      // Single warmup (API auth)
            .WithIterationCount(5)   // 5 iterations per benchmark
            .WithInvocationCount(1)  // 1 API call per iteration
            .WithUnrollFactor(1));
    }
}

/// <summary>
/// Helper for running benchmarks from command line.
/// Run with: dotnet run -c Release -- --filter "*DetectionBenchmarks*"
/// </summary>
public static class BenchmarkHelper
{
    public static void RunBenchmarks(string[] args)
    {
        var summary = BenchmarkDotNet.Running.BenchmarkRunner.Run<DetectionBenchmarks>(args: args);
        Console.WriteLine(summary);
    }
}
