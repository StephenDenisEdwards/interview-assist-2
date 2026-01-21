using System.Text.Json;
using Microsoft.Extensions.Configuration;
using InterviewAssist.Library.Pipeline;

namespace InterviewAssist.Library.IntegrationTests.Detection;

/// <summary>
/// Shared fixture for detection integration tests.
/// Handles API key loading and service instantiation.
/// </summary>
public class DetectionTestFixture : IDisposable
{
    public IQuestionDetectionService DetectionService { get; }
    public string ApiKey { get; }
    public string Model { get; }
    public double ConfidenceThreshold { get; }
    public bool IsConfigured { get; }

    public DetectionTestFixture()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<DetectionTestFixture>(optional: true)
            .Build();

        // Try multiple sources for API key
        ApiKey = config["OpenAI:ApiKey"]
            ?? config["OPENAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? string.Empty;

        Model = config["Detection:DefaultModel"] ?? "gpt-4o-mini";
        ConfidenceThreshold = double.TryParse(config["Detection:ConfidenceThreshold"], out var thresh)
            ? thresh
            : 0.7;

        IsConfigured = !string.IsNullOrWhiteSpace(ApiKey);

        if (IsConfigured)
        {
            DetectionService = new OpenAiQuestionDetectionService(
                ApiKey,
                Model,
                ConfidenceThreshold);
        }
        else
        {
            // Create a dummy service that will cause tests to skip
            DetectionService = null!;
        }
    }

    public void Dispose()
    {
        // HttpClient in the service will be disposed with the service
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Test case model for continuous transcript detection tests.
/// </summary>
public class ContinuousTranscriptTestCase
{
    public required string Id { get; init; }
    public string? Description { get; init; }
    public required string Transcript { get; init; }
    public string? PreviousContext { get; init; }
    public required List<ExpectedDetection> ExpectedDetections { get; init; }
    public List<string>? ShouldNotDetect { get; init; }
}

public class ExpectedDetection
{
    public required string TextContains { get; init; }
    public required string Type { get; init; }
    public double MinConfidence { get; init; } = 0.7;
}

/// <summary>
/// Test data loader for continuous transcript test cases.
/// </summary>
public static class TestDataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static List<ContinuousTranscriptTestCase> LoadContinuousTranscriptTestCases()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "ContinuousTranscriptTestCases.json");
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        var testCases = doc.RootElement.GetProperty("testCases");
        return JsonSerializer.Deserialize<List<ContinuousTranscriptTestCase>>(testCases.GetRawText(), JsonOptions)
            ?? new List<ContinuousTranscriptTestCase>();
    }
}

/// <summary>
/// Result of a single detection test for reporting.
/// </summary>
public class DetectionTestResult
{
    public required string TestCaseId { get; init; }
    public required string Transcript { get; init; }
    public required List<ExpectedDetection> Expected { get; init; }
    public required List<DetectedQuestion> Actual { get; init; }
    public required int TruePositives { get; init; }
    public required int FalseNegatives { get; init; }
    public required int FalsePositives { get; init; }
    public required TimeSpan Latency { get; init; }
    public required List<string> MissedDetections { get; init; }
    public required List<string> UnexpectedDetections { get; init; }
}

/// <summary>
/// Aggregate accuracy metrics across all test cases.
/// </summary>
public class AccuracyReport
{
    public int TotalTestCases { get; set; }
    public int TotalExpectedDetections { get; set; }
    public int TruePositives { get; set; }
    public int FalseNegatives { get; set; }
    public int FalsePositives { get; set; }

    public double Precision => TruePositives + FalsePositives == 0
        ? 1.0
        : (double)TruePositives / (TruePositives + FalsePositives);

    public double Recall => TruePositives + FalseNegatives == 0
        ? 1.0
        : (double)TruePositives / (TruePositives + FalseNegatives);

    public double F1Score => Precision + Recall == 0
        ? 0
        : 2 * (Precision * Recall) / (Precision + Recall);

    public TimeSpan TotalLatency { get; set; }
    public TimeSpan AverageLatency => TotalTestCases == 0
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(TotalLatency.TotalMilliseconds / TotalTestCases);

    public TimeSpan MinLatency { get; set; } = TimeSpan.MaxValue;
    public TimeSpan MaxLatency { get; set; } = TimeSpan.MinValue;

    public List<DetectionTestResult> Results { get; } = new();

    public override string ToString()
    {
        return $"""
            === Detection Accuracy Report ===
            Model: {Model}
            Confidence Threshold: {ConfidenceThreshold:F2}

            Test Cases: {TotalTestCases}
            Expected Detections: {TotalExpectedDetections}

            True Positives:  {TruePositives}
            False Negatives: {FalseNegatives} (missed)
            False Positives: {FalsePositives} (spurious)

            Precision: {Precision:P1}
            Recall:    {Recall:P1}
            F1 Score:  {F1Score:P1}

            === Latency ===
            Average: {AverageLatency.TotalMilliseconds:F0}ms
            Min:     {MinLatency.TotalMilliseconds:F0}ms
            Max:     {MaxLatency.TotalMilliseconds:F0}ms
            """;
    }

    public string Model { get; set; } = "";
    public double ConfidenceThreshold { get; set; }
}
