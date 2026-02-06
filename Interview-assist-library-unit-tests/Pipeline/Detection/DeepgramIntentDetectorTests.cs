using System.Net;
using System.Text;
using InterviewAssist.Library.Pipeline.Detection;
using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.UnitTests.Pipeline.Detection;

public class DeepgramIntentDetectorTests : IDisposable
{
    private readonly List<HttpRequestMessage> _capturedRequests = new();

    private DeepgramIntentDetector CreateDetector(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        DeepgramDetectionOptions? options = null)
    {
        var handler = new FakeHttpHandler(responseJson, statusCode, _capturedRequests);
        var httpClient = new HttpClient(handler);
        return new DeepgramIntentDetector(httpClient, options);
    }

    public void Dispose()
    {
        // Nothing to clean up; detectors are disposed per-test
    }

    #region Constructor Validation

    [Fact]
    public void Constructor_NullApiKey_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DeepgramIntentDetector(apiKey: null!));
    }

    [Fact]
    public void Constructor_EmptyApiKey_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DeepgramIntentDetector(""));
    }

    [Fact]
    public void Constructor_WhitespaceApiKey_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DeepgramIntentDetector("   "));
    }

    #endregion

    #region Empty / Null Input

    [Fact]
    public async Task DetectIntentsAsync_NullText_ReturnsEmpty()
    {
        using var detector = CreateDetector("{}");
        var result = await detector.DetectIntentsAsync(null!);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_EmptyText_ReturnsEmpty()
    {
        using var detector = CreateDetector("{}");
        var result = await detector.DetectIntentsAsync("");
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_WhitespaceText_ReturnsEmpty()
    {
        using var detector = CreateDetector("{}");
        var result = await detector.DetectIntentsAsync("   ");
        Assert.Empty(result);
    }

    #endregion

    #region Response Parsing

    [Fact]
    public async Task DetectIntentsAsync_ValidQuestionIntent_ReturnsQuestion()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "Can you explain polymorphism?",
                  "intents": [
                    {
                      "intent": "Ask about concept",
                      "confidence_score": 0.95
                    }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("Can you explain polymorphism?");

        Assert.Single(result);
        Assert.Equal(IntentType.Question, result[0].Type);
        Assert.Equal(0.95, result[0].Confidence);
        Assert.Equal("Can you explain polymorphism?", result[0].SourceText);
    }

    [Fact]
    public async Task DetectIntentsAsync_MultipleIntentsInSegment_ReturnsAll()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "Can you explain and describe the difference?",
                  "intents": [
                    { "intent": "Ask about concept", "confidence_score": 0.9 },
                    { "intent": "Request explanation", "confidence_score": 0.85 }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("Can you explain and describe the difference?");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task DetectIntentsAsync_MultipleSegments_ReturnsIntentsFromAll()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "What is a lock?",
                  "intents": [
                    { "intent": "Ask a question", "confidence_score": 0.9 }
                  ]
                },
                {
                  "text": "Stop the recording.",
                  "intents": [
                    { "intent": "Stop process", "confidence_score": 0.95 }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("What is a lock? Stop the recording.");

        Assert.Equal(2, result.Count);
        Assert.Equal(IntentType.Question, result[0].Type);
        Assert.Equal(IntentType.Imperative, result[1].Type);
        Assert.Equal(IntentSubtype.Stop, result[1].Subtype);
    }

    [Fact]
    public async Task DetectIntentsAsync_SegmentTextUsedAsSourceText()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "What is polymorphism?",
                  "intents": [
                    { "intent": "Ask about concept", "confidence_score": 0.9 }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("full input text here");

        Assert.Single(result);
        Assert.Equal("What is polymorphism?", result[0].SourceText);
    }

    [Fact]
    public async Task DetectIntentsAsync_MissingSegmentText_FallsBackToInputText()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "intents": [
                    { "intent": "Ask about concept", "confidence_score": 0.9 }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("original input");

        Assert.Single(result);
        Assert.Equal("original input", result[0].SourceText);
    }

    [Fact]
    public async Task DetectIntentsAsync_EmptySegments_ReturnsEmpty()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": []
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("some text");

        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_MissingResults_ReturnsEmpty()
    {
        using var detector = CreateDetector("{}");
        var result = await detector.DetectIntentsAsync("some text");
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_MissingIntentsProperty_ReturnsEmpty()
    {
        var json = """{ "results": {} }""";
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("some text");
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_MissingSegmentsProperty_ReturnsEmpty()
    {
        var json = """{ "results": { "intents": {} } }""";
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("some text");
        Assert.Empty(result);
    }

    #endregion

    #region Confidence Filtering

    [Fact]
    public async Task DetectIntentsAsync_BelowThreshold_FiltersOut()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "maybe a question",
                  "intents": [
                    { "intent": "Ask about concept", "confidence_score": 0.3 }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("maybe a question");

        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_CustomThreshold_Respected()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "What is this?",
                  "intents": [
                    { "intent": "Ask about concept", "confidence_score": 0.85 }
                  ]
                }
              ]
            }
          }
        }
        """;

        var options = new DeepgramDetectionOptions { ConfidenceThreshold = 0.9 };
        using var detector = CreateDetector(json, options: options);
        var result = await detector.DetectIntentsAsync("What is this?");

        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_MixedConfidence_FiltersCorrectly()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "Explain this concept",
                  "intents": [
                    { "intent": "Ask about concept", "confidence_score": 0.9 },
                    { "intent": "Request information", "confidence_score": 0.3 }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("Explain this concept");

        Assert.Single(result);
        Assert.Equal(0.9, result[0].Confidence);
    }

    #endregion

    #region Intent Classification (ClassifyIntent)

    [Theory]
    [InlineData("Ask about concept", IntentType.Question)]
    [InlineData("Ask a question", IntentType.Question)]
    [InlineData("Inquire about details", IntentType.Question)]
    [InlineData("Wonder about meaning", IntentType.Question)]
    [InlineData("Question the approach", IntentType.Question)]
    public async Task DetectIntentsAsync_QuestionKeywords_MapsToQuestion(string intentLabel, IntentType expectedType)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(expectedType, result[0].Type);
    }

    [Theory]
    [InlineData("Explain the concept", IntentType.Question)]
    [InlineData("Describe the process", IntentType.Question)]
    [InlineData("Tell me about it", IntentType.Question)]
    public async Task DetectIntentsAsync_ImperativeQuestionForms_MapsToQuestion(string intentLabel, IntentType expectedType)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(expectedType, result[0].Type);
    }

    [Theory]
    [InlineData("Stop process", IntentType.Imperative, IntentSubtype.Stop)]
    [InlineData("Cancel operation", IntentType.Imperative, IntentSubtype.Stop)]
    [InlineData("Quit application", IntentType.Imperative, IntentSubtype.Stop)]
    public async Task DetectIntentsAsync_StopKeywords_MapsToStopImperative(string intentLabel, IntentType expectedType, IntentSubtype expectedSubtype)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(expectedType, result[0].Type);
        Assert.Equal(expectedSubtype, result[0].Subtype);
    }

    [Theory]
    [InlineData("Repeat the answer", IntentType.Imperative, IntentSubtype.Repeat)]
    [InlineData("Say it again", IntentType.Imperative, IntentSubtype.Repeat)]
    public async Task DetectIntentsAsync_RepeatKeywords_MapsToRepeatImperative(string intentLabel, IntentType expectedType, IntentSubtype expectedSubtype)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(expectedType, result[0].Type);
        Assert.Equal(expectedSubtype, result[0].Subtype);
    }

    [Theory]
    [InlineData("Continue the discussion", IntentType.Imperative, IntentSubtype.Continue)]
    [InlineData("Go on with the topic", IntentType.Imperative, IntentSubtype.Continue)]
    [InlineData("Next question", IntentType.Imperative, IntentSubtype.Continue)]
    public async Task DetectIntentsAsync_ContinueKeywords_MapsToContinueImperative(string intentLabel, IntentType expectedType, IntentSubtype expectedSubtype)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(expectedType, result[0].Type);
        Assert.Equal(expectedSubtype, result[0].Subtype);
    }

    [Theory]
    [InlineData("Generate questions", IntentType.Imperative, IntentSubtype.Generate)]
    [InlineData("Create a summary", IntentType.Imperative, IntentSubtype.Generate)]
    [InlineData("Make a list", IntentType.Imperative, IntentSubtype.Generate)]
    public async Task DetectIntentsAsync_GenerateKeywords_MapsToGenerateImperative(string intentLabel, IntentType expectedType, IntentSubtype expectedSubtype)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(expectedType, result[0].Type);
        Assert.Equal(expectedSubtype, result[0].Subtype);
    }

    [Theory]
    [InlineData("Provide feedback")]
    [InlineData("Share opinion")]
    [InlineData("Agree with statement")]
    public async Task DetectIntentsAsync_UnrecognizedLabel_MapsToStatement(string intentLabel)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(IntentType.Statement, result[0].Type);
        Assert.Null(result[0].Subtype);
    }

    #endregion

    #region Question Subtype Inference

    [Theory]
    [InlineData("Ask for definition", IntentSubtype.Definition)]
    [InlineData("Ask about what is X", IntentSubtype.Definition)]
    [InlineData("Inquire about meaning", IntentSubtype.Definition)]
    public async Task DetectIntentsAsync_DefinitionKeywords_InfersDefinitionSubtype(string intentLabel, IntentSubtype expectedSubtype)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(IntentType.Question, result[0].Type);
        Assert.Equal(expectedSubtype, result[0].Subtype);
    }

    [Theory]
    [InlineData("Ask how to do something", IntentSubtype.HowTo)]
    [InlineData("Ask about steps", IntentSubtype.HowTo)]
    [InlineData("Ask about process", IntentSubtype.HowTo)]
    public async Task DetectIntentsAsync_HowToKeywords_InfersHowToSubtype(string intentLabel, IntentSubtype expectedSubtype)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(IntentType.Question, result[0].Type);
        Assert.Equal(expectedSubtype, result[0].Subtype);
    }

    [Theory]
    [InlineData("Ask to compare options", IntentSubtype.Compare)]
    [InlineData("Ask about difference", IntentSubtype.Compare)]
    [InlineData("Ask X versus Y", IntentSubtype.Compare)]
    public async Task DetectIntentsAsync_CompareKeywords_InfersCompareSubtype(string intentLabel, IntentSubtype expectedSubtype)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(IntentType.Question, result[0].Type);
        Assert.Equal(expectedSubtype, result[0].Subtype);
    }

    [Theory]
    [InlineData("Ask about error", IntentSubtype.Troubleshoot)]
    [InlineData("Ask how to fix issue", IntentSubtype.Troubleshoot)]
    [InlineData("Ask about problem", IntentSubtype.Troubleshoot)]
    [InlineData("Ask to troubleshoot", IntentSubtype.Troubleshoot)]
    public async Task DetectIntentsAsync_TroubleshootKeywords_InfersTroubleshootSubtype(string intentLabel, IntentSubtype expectedSubtype)
    {
        var json = BuildSingleIntentResponse(intentLabel, 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(IntentType.Question, result[0].Type);
        Assert.Equal(expectedSubtype, result[0].Subtype);
    }

    [Fact]
    public async Task DetectIntentsAsync_GenericAskIntent_NoSubtype()
    {
        var json = BuildSingleIntentResponse("Ask about topic", 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test input");

        Assert.Single(result);
        Assert.Equal(IntentType.Question, result[0].Type);
        Assert.Null(result[0].Subtype);
    }

    #endregion

    #region HTTP Error Handling

    [Fact]
    public async Task DetectIntentsAsync_HttpError_ReturnsEmpty()
    {
        using var detector = CreateDetector("Unauthorized", HttpStatusCode.Unauthorized);
        var result = await detector.DetectIntentsAsync("What is this?");
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_ServerError_ReturnsEmpty()
    {
        using var detector = CreateDetector("Internal Server Error", HttpStatusCode.InternalServerError);
        var result = await detector.DetectIntentsAsync("What is this?");
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_MalformedJson_ReturnsEmpty()
    {
        using var detector = CreateDetector("this is not json");
        var result = await detector.DetectIntentsAsync("What is this?");
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var detector = CreateDetector("{}");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => detector.DetectIntentsAsync("test", ct: cts.Token));
    }

    #endregion

    #region URL Construction

    [Fact]
    public async Task DetectIntentsAsync_NoCustomIntents_BaseUrlOnly()
    {
        using var detector = CreateDetector("{}");
        await detector.DetectIntentsAsync("test");

        Assert.Single(_capturedRequests);
        var url = _capturedRequests[0].RequestUri!.ToString();
        Assert.Contains("intents=true", url);
        Assert.Contains("language=en", url);
        Assert.DoesNotContain("custom_intent", url);
    }

    [Fact]
    public async Task DetectIntentsAsync_WithCustomIntents_IncludesInUrl()
    {
        var options = new DeepgramDetectionOptions
        {
            CustomIntents = new List<string> { "ask a question", "request clarification" },
            CustomIntentMode = "strict"
        };

        using var detector = CreateDetector("{}", options: options);
        await detector.DetectIntentsAsync("test");

        Assert.Single(_capturedRequests);
        var url = _capturedRequests[0].RequestUri!.ToString();
        Assert.Contains("custom_intent=ask%20a%20question", url);
        Assert.Contains("custom_intent=request%20clarification", url);
        Assert.Contains("custom_intent_mode=strict", url);
    }

    [Fact]
    public async Task DetectIntentsAsync_RequestBodyContainsText()
    {
        using var detector = CreateDetector("{}");
        await detector.DetectIntentsAsync("What is polymorphism?");

        Assert.Single(_capturedRequests);
        var body = await _capturedRequests[0].Content!.ReadAsStringAsync();
        Assert.Contains("What is polymorphism?", body);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task DetectIntentsAsync_EmptyIntentLabel_FiltersOut()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "test",
                  "intents": [
                    { "intent": "", "confidence_score": 0.9 },
                    { "intent": "Ask about concept", "confidence_score": 0.9 }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test");

        Assert.Single(result);
        Assert.Equal(IntentType.Question, result[0].Type);
    }

    [Fact]
    public async Task DetectIntentsAsync_SegmentWithNoIntents_Skipped()
    {
        var json = """
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "just a statement"
                },
                {
                  "text": "What is this?",
                  "intents": [
                    { "intent": "Ask about concept", "confidence_score": 0.9 }
                  ]
                }
              ]
            }
          }
        }
        """;

        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test");

        Assert.Single(result);
    }

    [Fact]
    public async Task DetectIntentsAsync_CaseInsensitiveClassification()
    {
        var json = BuildSingleIntentResponse("ASK ABOUT CONCEPT", 0.9);
        using var detector = CreateDetector(json);
        var result = await detector.DetectIntentsAsync("test");

        Assert.Single(result);
        Assert.Equal(IntentType.Question, result[0].Type);
    }

    #endregion

    #region Helpers

    private static string BuildSingleIntentResponse(string intentLabel, double confidence)
    {
        return $$"""
        {
          "results": {
            "intents": {
              "segments": [
                {
                  "text": "test input",
                  "intents": [
                    {
                      "intent": "{{intentLabel}}",
                      "confidence_score": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
                    }
                  ]
                }
              ]
            }
          }
        }
        """;
    }

    /// <summary>
    /// Fake HTTP handler that returns a canned response and captures requests.
    /// </summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;
        private readonly List<HttpRequestMessage> _capturedRequests;

        public FakeHttpHandler(string responseBody, HttpStatusCode statusCode, List<HttpRequestMessage> capturedRequests)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _capturedRequests = capturedRequests;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Clone the request content before it gets disposed
            var cloned = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                cloned.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            _capturedRequests.Add(cloned);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    #endregion
}
