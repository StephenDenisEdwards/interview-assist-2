using System.Diagnostics;
using InterviewAssist.Library.Pipeline;
using Xunit.Abstractions;

namespace InterviewAssist.Library.IntegrationTests.Detection;

/// <summary>
/// Integration tests for question/imperative detection accuracy.
/// Tests the OpenAI-based detection service against continuous conversation transcripts.
/// </summary>
public class QuestionDetectionAccuracyTests : IClassFixture<DetectionTestFixture>
{
    private readonly DetectionTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public QuestionDetectionAccuracyTests(DetectionTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact(Skip = "Run manually - requires API key")]
    public async Task RunFullAccuracyReport()
    {
        SkipIfNotConfigured();

        var testCases = TestDataLoader.LoadContinuousTranscriptTestCases();
        var report = new AccuracyReport
        {
            Model = _fixture.Model,
            ConfidenceThreshold = _fixture.ConfidenceThreshold
        };

        _output.WriteLine($"Running {testCases.Count} test cases...\n");

        foreach (var testCase in testCases)
        {
            var result = await RunSingleTestCase(testCase);
            report.Results.Add(result);

            report.TotalTestCases++;
            report.TotalExpectedDetections += testCase.ExpectedDetections.Count;
            report.TruePositives += result.TruePositives;
            report.FalseNegatives += result.FalseNegatives;
            report.FalsePositives += result.FalsePositives;
            report.TotalLatency += result.Latency;

            if (result.Latency < report.MinLatency) report.MinLatency = result.Latency;
            if (result.Latency > report.MaxLatency) report.MaxLatency = result.Latency;

            // Output individual result
            _output.WriteLine($"[{testCase.Id}] {result.Latency.TotalMilliseconds:F0}ms - TP:{result.TruePositives} FN:{result.FalseNegatives} FP:{result.FalsePositives}");

            if (result.MissedDetections.Count > 0)
            {
                _output.WriteLine($"  MISSED: {string.Join(", ", result.MissedDetections)}");
            }
            if (result.UnexpectedDetections.Count > 0)
            {
                _output.WriteLine($"  UNEXPECTED: {string.Join(", ", result.UnexpectedDetections)}");
            }
        }

        _output.WriteLine("\n" + report.ToString());

        // Assert minimum quality thresholds
        Assert.True(report.Precision >= 0.8, $"Precision {report.Precision:P1} below 80% threshold");
        Assert.True(report.Recall >= 0.7, $"Recall {report.Recall:P1} below 70% threshold");
    }

    [Theory(Skip = "Run manually - requires API key")]
    [MemberData(nameof(GetTestCaseIds))]
    public async Task DetectQuestions_IndividualTestCase(string testCaseId)
    {
        SkipIfNotConfigured();

        var testCases = TestDataLoader.LoadContinuousTranscriptTestCases();
        var testCase = testCases.FirstOrDefault(tc => tc.Id == testCaseId);
        Assert.NotNull(testCase);

        var result = await RunSingleTestCase(testCase);

        _output.WriteLine($"Transcript: {testCase.Transcript}");
        _output.WriteLine($"\nExpected ({testCase.ExpectedDetections.Count}):");
        foreach (var expected in testCase.ExpectedDetections)
        {
            _output.WriteLine($"  - [{expected.Type}] contains \"{expected.TextContains}\"");
        }

        _output.WriteLine($"\nActual ({result.Actual.Count}):");
        foreach (var actual in result.Actual)
        {
            _output.WriteLine($"  - [{actual.Type}] \"{actual.Text}\" (confidence: {actual.Confidence:F2})");
        }

        _output.WriteLine($"\nLatency: {result.Latency.TotalMilliseconds:F0}ms");
        _output.WriteLine($"TP: {result.TruePositives}, FN: {result.FalseNegatives}, FP: {result.FalsePositives}");

        if (result.MissedDetections.Count > 0)
        {
            _output.WriteLine($"\nMISSED: {string.Join(", ", result.MissedDetections)}");
        }

        // Individual test should have no false negatives
        Assert.Empty(result.MissedDetections);
    }

    [Fact(Skip = "Run manually - requires API key")]
    public async Task DetectQuestions_NoQuestionsInTranscript_ReturnsEmpty()
    {
        SkipIfNotConfigured();

        var transcript = "Alright, I think that covers everything on my end. Let me check my notes here. Yeah, I think we're good.";

        var sw = Stopwatch.StartNew();
        var results = await _fixture.DetectionService.DetectQuestionsAsync(transcript);
        sw.Stop();

        _output.WriteLine($"Transcript: {transcript}");
        _output.WriteLine($"Latency: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Detections: {results.Count}");

        foreach (var r in results)
        {
            _output.WriteLine($"  - [{r.Type}] \"{r.Text}\" ({r.Confidence:F2})");
        }

        Assert.Empty(results);
    }

    [Fact(Skip = "Run manually - requires API key")]
    public async Task DetectQuestions_MultipleQuestionsInTranscript_DetectsAll()
    {
        SkipIfNotConfigured();

        var transcript = "What's your experience with dependency injection? And how do you typically structure your applications? Do you follow any specific architectural patterns?";

        var sw = Stopwatch.StartNew();
        var results = await _fixture.DetectionService.DetectQuestionsAsync(transcript);
        sw.Stop();

        _output.WriteLine($"Transcript: {transcript}");
        _output.WriteLine($"Latency: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Detections: {results.Count}");

        foreach (var r in results)
        {
            _output.WriteLine($"  - [{r.Type}] \"{r.Text}\" ({r.Confidence:F2})");
        }

        Assert.True(results.Count >= 2, $"Expected at least 2 questions, got {results.Count}");
    }

    [Fact(Skip = "Run manually - requires API key")]
    public async Task DetectQuestions_ImperativeStatement_DetectsAsImperative()
    {
        SkipIfNotConfigured();

        var transcript = "Tell me about your experience with microservices architecture. Walk me through a project where you used it.";

        var sw = Stopwatch.StartNew();
        var results = await _fixture.DetectionService.DetectQuestionsAsync(transcript);
        sw.Stop();

        _output.WriteLine($"Transcript: {transcript}");
        _output.WriteLine($"Latency: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Detections: {results.Count}");

        foreach (var r in results)
        {
            _output.WriteLine($"  - [{r.Type}] \"{r.Text}\" ({r.Confidence:F2})");
        }

        Assert.Contains(results, r => r.Type == QuestionType.Imperative);
    }

    [Fact(Skip = "Run manually - requires API key")]
    public async Task DetectQuestions_NoisyTranscript_ExtractsQuestionDespiteNoise()
    {
        SkipIfNotConfigured();

        var transcript = "Um so like uh what would you say is your uh biggest weakness as a developer and um how are you working to improve on that";

        var sw = Stopwatch.StartNew();
        var results = await _fixture.DetectionService.DetectQuestionsAsync(transcript);
        sw.Stop();

        _output.WriteLine($"Transcript: {transcript}");
        _output.WriteLine($"Latency: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Detections: {results.Count}");

        foreach (var r in results)
        {
            _output.WriteLine($"  - [{r.Type}] \"{r.Text}\" ({r.Confidence:F2})");
        }

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Text.Contains("weakness", StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<object[]> GetTestCaseIds()
    {
        var testCases = TestDataLoader.LoadContinuousTranscriptTestCases();
        return testCases.Select(tc => new object[] { tc.Id });
    }

    private async Task<DetectionTestResult> RunSingleTestCase(ContinuousTranscriptTestCase testCase)
    {
        var sw = Stopwatch.StartNew();
        var actual = await _fixture.DetectionService.DetectQuestionsAsync(
            testCase.Transcript,
            testCase.PreviousContext);
        sw.Stop();

        var actualList = actual.ToList();
        var truePositives = 0;
        var matchedActual = new HashSet<int>();
        var missedDetections = new List<string>();

        // Check each expected detection
        foreach (var expected in testCase.ExpectedDetections)
        {
            var found = false;
            for (var i = 0; i < actualList.Count; i++)
            {
                if (matchedActual.Contains(i)) continue;

                var a = actualList[i];
                if (a.Text.Contains(expected.TextContains, StringComparison.OrdinalIgnoreCase) &&
                    a.Confidence >= expected.MinConfidence &&
                    MatchesType(a.Type, expected.Type))
                {
                    truePositives++;
                    matchedActual.Add(i);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                missedDetections.Add($"[{expected.Type}] \"{expected.TextContains}\"");
            }
        }

        // Check for false positives (unexpected detections)
        var unexpectedDetections = new List<string>();
        var shouldNotDetect = testCase.ShouldNotDetect ?? new List<string>();

        for (var i = 0; i < actualList.Count; i++)
        {
            if (matchedActual.Contains(i)) continue;

            var a = actualList[i];
            // Check if this matches something we explicitly shouldn't detect
            if (shouldNotDetect.Any(s => a.Text.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                unexpectedDetections.Add($"[{a.Type}] \"{a.Text}\"");
            }
        }

        var falseNegatives = testCase.ExpectedDetections.Count - truePositives;
        var falsePositives = unexpectedDetections.Count;

        return new DetectionTestResult
        {
            TestCaseId = testCase.Id,
            Transcript = testCase.Transcript,
            Expected = testCase.ExpectedDetections,
            Actual = actualList,
            TruePositives = truePositives,
            FalseNegatives = falseNegatives,
            FalsePositives = falsePositives,
            Latency = sw.Elapsed,
            MissedDetections = missedDetections,
            UnexpectedDetections = unexpectedDetections
        };
    }

    private static bool MatchesType(QuestionType actual, string expected)
    {
        return expected.ToLowerInvariant() switch
        {
            "question" => actual == QuestionType.Question,
            "imperative" => actual == QuestionType.Imperative,
            "clarification" => actual == QuestionType.Clarification,
            "followup" or "follow_up" => actual == QuestionType.FollowUp,
            _ => true // If type not specified, accept any
        };
    }

    private void SkipIfNotConfigured()
    {
        if (!_fixture.IsConfigured)
        {
            throw new SkipException("OpenAI API key not configured. Set OPENAI_API_KEY environment variable or configure in user secrets.");
        }
    }
}

/// <summary>
/// Exception to skip tests when API key is not configured.
/// </summary>
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
