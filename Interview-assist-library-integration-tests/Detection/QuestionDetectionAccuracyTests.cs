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

    /// <summary>
    /// Integration test for the C# tutorial video transcript from real console output.
    /// This tests detection against a real-world noisy transcript with multiple interview questions.
    /// </summary>
    //[Fact(Skip = "Run manually - requires API key")]
    [Fact]
    public async Task DetectQuestions_CSharpTutorialTranscript_ListsAllDetectedQuestions()
    {
        SkipIfNotConfigured();

        // This transcript is derived from real console output of the transcription app
        // watching a C# interview questions tutorial video series
        var transcript = """
            This is part one of C-Sharp interview questions video series. In this video,
            We'll answer this interview question. Can you store different types in an array in C sharp.
            So, let's answer this interview question. Can you store different types in an array in C sharp.
            And the answer is yes, if you create an object array. Let's understand that with an example.
            Here I have a console application. Now let's create an integer array. Now look at this.
            I can store an integer within this array, because that is an integer array.
            If I try to store a string, look at what is going to happen. We will get a compilation error.
            Look at that. We have an error cannot implicitly convert type string to int.
            So arrays are strongly typed. You can only store integer type within this array.
            But then if you want to store different data types, create an array of type object.
            Then look at this. I am able to store an integer. I am able to store a string.
            Because object type is the base type for all types in dot net.
            This is part 2 of C-Sharp interview questions video series. In this video we will answer
            this interview question, what is a jagged array. Let's understand this with an example.
            Let's say we have three employees. Mark, Matt, and John. Mark has three qualifications,
            bachelors, masters, and doctorate, whereas Matt has only one qualification.
            John has bachelors and masters. Now here different employees have different number of qualifications.
            So, I want a data structure where I can store these varying number of qualifications.
            Jagged Array is one of the choices. Now what is a jagged array? A jagged array is an array of arrays.
            This is part three of C sharp interview questions video series. In this video,
            we will answer this interview question, why and when should we use an abstract class.
            This interview question could also be asked in a slightly different way,
            give an example of where we could use an abstract class.
            Interfaces and abstract classes are complex subjects.
            This is part 4 of C-Sharp interview questions video series. In this video we will answer
            this interview question, what are the advantages of using interfaces.
            Before proceeding with this, I strongly recommend to watch these videos from the C-sharp tutorial.
            """;

        _output.WriteLine("=== C# Tutorial Transcript Question Detection Test ===\n");
        _output.WriteLine("Input Transcript:");
        _output.WriteLine(new string('-', 60));
        _output.WriteLine(transcript);
        _output.WriteLine(new string('-', 60));
        _output.WriteLine("");

        var sw = Stopwatch.StartNew();
        var results = await _fixture.DetectionService.DetectQuestionsAsync(transcript);
        sw.Stop();

        _output.WriteLine($"Detection completed in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Total detections: {results.Count}");
        _output.WriteLine("");

        _output.WriteLine("=== DETECTED QUESTIONS ===");
        _output.WriteLine("");

        var questionNumber = 1;
        foreach (var question in results.OrderByDescending(q => q.Confidence))
        {
            _output.WriteLine($"{questionNumber}. [{question.Type}] (Confidence: {question.Confidence:P0})");
            _output.WriteLine($"   \"{question.Text}\"");
            _output.WriteLine("");
            questionNumber++;
        }

        // Expected questions from this transcript (must be self-contained):
        // The LLM should resolve pronouns and references to make questions standalone
        var expectedQuestions = new[]
        {
            "store different types in an array",  // Already self-contained
            "jagged array",                        // "What is a jagged array?" - self-contained
            "abstract class",                      // "When should we use an abstract class?" - resolved from "it"
            "give an example",                     // Imperative - should include "abstract class"
            "advantages",                          // "What are the advantages of using interfaces?" - resolved
            "interfaces"                           // Should be mentioned in the advantages question
        };

        _output.WriteLine("=== EXPECTED QUESTIONS CHECK ===");
        _output.WriteLine("");

        foreach (var expected in expectedQuestions)
        {
            var found = results.Any(r => r.Text.Contains(expected, StringComparison.OrdinalIgnoreCase));
            var status = found ? "✓ FOUND" : "✗ MISSED";
            _output.WriteLine($"  {status}: \"{expected}\"");
        }

        _output.WriteLine("");

        // Assert we found most of the expected keywords in the self-contained questions
        var foundCount = expectedQuestions.Count(exp =>
            results.Any(r => r.Text.Contains(exp, StringComparison.OrdinalIgnoreCase)));

        _output.WriteLine($"Found {foundCount}/{expectedQuestions.Length} expected keywords in questions");

        // We expect at least 4 distinct questions to be detected
        Assert.True(results.Count >= 4, $"Expected at least 4 questions to be detected, but got {results.Count}");

        // Each question should be self-contained (contain its subject matter)
        Assert.True(foundCount >= 5, $"Expected at least 5 keyword matches in self-contained questions, but only found {foundCount}");
    }

    /// <summary>
    /// Tests each part of the C# tutorial video separately.
    /// </summary>
    [Theory(Skip = "Run manually - requires API key")]
    [InlineData("csharp_tutorial_arrays_part1")]
    [InlineData("csharp_tutorial_jagged_arrays_part2")]
    [InlineData("csharp_tutorial_abstract_class_part3")]
    [InlineData("csharp_tutorial_interfaces_part4")]
    [InlineData("csharp_tutorial_full_transcript")]
    public async Task DetectQuestions_CSharpTutorialParts_DetectsExpectedQuestions(string testCaseId)
    {
        SkipIfNotConfigured();

        var testCases = TestDataLoader.LoadContinuousTranscriptTestCases();
        var testCase = testCases.FirstOrDefault(tc => tc.Id == testCaseId);
        Assert.NotNull(testCase);

        _output.WriteLine($"=== Test Case: {testCase.Id} ===");
        _output.WriteLine($"Description: {testCase.Description}");
        _output.WriteLine("");
        _output.WriteLine($"Transcript: {testCase.Transcript}");
        _output.WriteLine("");

        var sw = Stopwatch.StartNew();
        var results = await _fixture.DetectionService.DetectQuestionsAsync(testCase.Transcript);
        sw.Stop();

        _output.WriteLine($"Latency: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine("");
        _output.WriteLine("Detected Questions:");

        foreach (var result in results)
        {
            _output.WriteLine($"  [{result.Type}] \"{result.Text}\" ({result.Confidence:P0})");
        }

        _output.WriteLine("");
        _output.WriteLine("Expected:");

        foreach (var expected in testCase.ExpectedDetections)
        {
            var found = results.Any(r =>
                r.Text.Contains(expected.TextContains, StringComparison.OrdinalIgnoreCase));
            var status = found ? "✓" : "✗";
            _output.WriteLine($"  {status} [{expected.Type}] contains \"{expected.TextContains}\"");
        }

        // Verify all expected detections were found
        foreach (var expected in testCase.ExpectedDetections)
        {
            Assert.Contains(results, r =>
                r.Text.Contains(expected.TextContains, StringComparison.OrdinalIgnoreCase));
        }
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
