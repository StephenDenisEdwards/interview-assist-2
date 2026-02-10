using InterviewAssist.Library.Pipeline.Detection;
using InterviewAssist.Library.Pipeline.Utterance;
using Moq;

namespace InterviewAssist.Library.UnitTests.Pipeline.Detection;

public class LlmIntentStrategyTests : IDisposable
{
    private readonly Mock<ILlmIntentDetector> _mockLlm = new();
    private readonly List<(string Text, string? Context)> _llmCalls = new();

    private LlmIntentStrategy CreateStrategy(LlmDetectionOptions? options = null)
    {
        var opts = options ?? new LlmDetectionOptions
        {
            TriggerOnQuestionMark = true,
            TriggerOnPause = false,
            RateLimitMs = 0,
            BufferMaxChars = 800,
            ContextWindowChars = 1500,
            EnablePreprocessing = false,
            EnableDeduplication = false
        };

        return new LlmIntentStrategy(_mockLlm.Object, opts);
    }

    private UtteranceEvent MakeUtterance(string text, string? id = null)
    {
        return new UtteranceEvent
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Type = UtteranceEventType.Final,
            StableText = text
        };
    }

    private void SetupLlmReturns(params DetectedIntent[] intents)
    {
        _mockLlm
            .Setup(x => x.DetectIntentsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((text, ctx, _) => _llmCalls.Add((text, ctx)))
            .ReturnsAsync((IReadOnlyList<DetectedIntent>)intents.ToList());
    }

    private void SetupLlmSequence(params IReadOnlyList<DetectedIntent>[] responses)
    {
        var callIndex = 0;
        _mockLlm
            .Setup(x => x.DetectIntentsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, CancellationToken>((text, ctx, _) =>
            {
                _llmCalls.Add((text, ctx));
                var idx = callIndex++;
                return Task.FromResult(idx < responses.Length ? responses[idx] : (IReadOnlyList<DetectedIntent>)new List<DetectedIntent>());
            });
    }

    public void Dispose()
    {
        // Nothing to clean up
    }

    #region Context Passing

    [Fact]
    public async Task FirstDetection_PassesNullContext()
    {
        SetupLlmReturns(new DetectedIntent
        {
            Type = IntentType.Question,
            Confidence = 0.9,
            SourceText = "What is polymorphism?"
        });

        using var strategy = CreateStrategy();
        await strategy.ProcessUtteranceAsync(MakeUtterance("What is polymorphism?"));

        Assert.Single(_llmCalls);
        Assert.Null(_llmCalls[0].Context);
    }

    [Fact]
    public async Task SecondDetection_PassesPreviousTextAsContext()
    {
        // First call returns a question
        var firstResponse = new List<DetectedIntent>
        {
            new() { Type = IntentType.Question, Confidence = 0.9, SourceText = "What is polymorphism?" }
        };
        // Second call returns a question
        var secondResponse = new List<DetectedIntent>
        {
            new() { Type = IntentType.Question, Confidence = 0.9, SourceText = "Can you explain that further?" }
        };

        SetupLlmSequence(firstResponse, secondResponse);

        using var strategy = CreateStrategy();

        // First utterance triggers detection (has ?)
        await strategy.ProcessUtteranceAsync(MakeUtterance("What is polymorphism?"));

        // Second utterance triggers detection (has ?)
        await strategy.ProcessUtteranceAsync(MakeUtterance("Can you explain that further?"));

        Assert.Equal(2, _llmCalls.Count);
        Assert.Null(_llmCalls[0].Context);
        Assert.NotNull(_llmCalls[1].Context);
        Assert.Contains("What is polymorphism?", _llmCalls[1].Context);
    }

    [Fact]
    public async Task ThirdDetection_ContextContainsBothPreviousUtterances()
    {
        var response = new List<DetectedIntent>
        {
            new() { Type = IntentType.Question, Confidence = 0.9, SourceText = "test" }
        };

        SetupLlmSequence(response, response, response);

        using var strategy = CreateStrategy();

        await strategy.ProcessUtteranceAsync(MakeUtterance("First utterance?"));
        await strategy.ProcessUtteranceAsync(MakeUtterance("Second utterance?"));
        await strategy.ProcessUtteranceAsync(MakeUtterance("Third utterance?"));

        Assert.Equal(3, _llmCalls.Count);
        // Third call should have both first and second as context
        Assert.NotNull(_llmCalls[2].Context);
        Assert.Contains("First utterance?", _llmCalls[2].Context);
        Assert.Contains("Second utterance?", _llmCalls[2].Context);
    }

    #endregion

    #region No Re-processing

    [Fact]
    public async Task AfterDetection_OnlyNewUtterancesInClassificationText()
    {
        var response = new List<DetectedIntent>
        {
            new() { Type = IntentType.Question, Confidence = 0.9, SourceText = "test" }
        };

        SetupLlmSequence(response, response);

        using var strategy = CreateStrategy();

        await strategy.ProcessUtteranceAsync(MakeUtterance("What is polymorphism?"));
        await strategy.ProcessUtteranceAsync(MakeUtterance("How does it work?"));

        Assert.Equal(2, _llmCalls.Count);

        // First call: only first utterance text (with label prefix)
        Assert.Contains("What is polymorphism?", _llmCalls[0].Text);
        Assert.DoesNotContain("How does it work?", _llmCalls[0].Text);

        // Second call: only new utterance text (not re-processing the first)
        Assert.Contains("How does it work?", _llmCalls[1].Text);
        // The first utterance should be in context, not in the classification text
        Assert.DoesNotContain("What is polymorphism?", _llmCalls[1].Text);
    }

    [Fact]
    public async Task MultipleUnprocessedUtterances_AllIncludedInClassificationText()
    {
        var response = new List<DetectedIntent>
        {
            new() { Type = IntentType.Question, Confidence = 0.9, SourceText = "test" }
        };

        SetupLlmSequence(response, response);

        var opts = new LlmDetectionOptions
        {
            TriggerOnQuestionMark = true,
            TriggerOnPause = false,
            RateLimitMs = 0,
            BufferMaxChars = 800,
            ContextWindowChars = 1500,
            EnablePreprocessing = false,
            EnableDeduplication = false,
            // Disable timeout to prevent spurious triggers
            TriggerTimeoutMs = 60000
        };

        using var strategy = CreateStrategy(opts);

        // First utterance has no question mark - won't trigger
        await strategy.ProcessUtteranceAsync(MakeUtterance("Here is some context"));
        Assert.Empty(_llmCalls);

        // Second utterance has question mark - triggers with both utterances
        await strategy.ProcessUtteranceAsync(MakeUtterance("What is this?"));

        Assert.Single(_llmCalls);
        Assert.Contains("Here is some context", _llmCalls[0].Text);
        Assert.Contains("What is this?", _llmCalls[0].Text);
    }

    #endregion

    #region Context Window Trimming

    [Fact]
    public async Task ContextWindow_TrimsOldUtterancesWhenExceedingLimit()
    {
        var response = new List<DetectedIntent>
        {
            new() { Type = IntentType.Question, Confidence = 0.9, SourceText = "test" }
        };

        SetupLlmSequence(response, response, response);

        // Use a very small context window
        var opts = new LlmDetectionOptions
        {
            TriggerOnQuestionMark = true,
            TriggerOnPause = false,
            RateLimitMs = 0,
            BufferMaxChars = 800,
            ContextWindowChars = 30,  // Very small - will force trimming
            EnablePreprocessing = false,
            EnableDeduplication = false
        };

        using var strategy = CreateStrategy(opts);

        // First: 25 chars
        await strategy.ProcessUtteranceAsync(MakeUtterance("This is the first text?"));
        // Second: 26 chars - after this, context is "This is the first text?" (23 chars)
        await strategy.ProcessUtteranceAsync(MakeUtterance("This is the second text?"));

        Assert.Equal(2, _llmCalls.Count);
        // Context from first call should include the first utterance
        Assert.Contains("This is the first text?", _llmCalls[1].Context!);

        // Third: context should be trimmed since first+second > 30 chars
        await strategy.ProcessUtteranceAsync(MakeUtterance("Third?"));

        Assert.Equal(3, _llmCalls.Count);
        // The context should have been trimmed - first utterance should be gone
        // Only "This is the second text?" (24 chars) should remain
        Assert.DoesNotContain("This is the first text?", _llmCalls[2].Context ?? "");
        Assert.Contains("This is the second text?", _llmCalls[2].Context!);
    }

    #endregion

    #region Forced Trigger on Buffer Overflow

    [Fact]
    public async Task UnprocessedExceedsBufferMax_ForcesDetection()
    {
        var response = new List<DetectedIntent>
        {
            new() { Type = IntentType.Statement, Confidence = 0.5, SourceText = "test" }
        };

        SetupLlmSequence(response);

        var opts = new LlmDetectionOptions
        {
            TriggerOnQuestionMark = false,  // No question mark trigger
            TriggerOnPause = false,
            RateLimitMs = 0,
            BufferMaxChars = 50,  // Small buffer
            ContextWindowChars = 1500,
            EnablePreprocessing = false,
            EnableDeduplication = false,
            TriggerTimeoutMs = 60000  // Long timeout so it doesn't fire
        };

        using var strategy = CreateStrategy(opts);

        // No trigger conditions, but accumulate past BufferMaxChars
        await strategy.ProcessUtteranceAsync(MakeUtterance("This is some text without any trigger conditions at all"));

        // Should have been forced to detect because text > 50 chars
        Assert.Single(_llmCalls);
    }

    [Fact]
    public async Task UnprocessedBelowBufferMax_NoForcedDetection()
    {
        SetupLlmReturns();

        var opts = new LlmDetectionOptions
        {
            TriggerOnQuestionMark = false,
            TriggerOnPause = false,
            RateLimitMs = 0,
            BufferMaxChars = 800,
            ContextWindowChars = 1500,
            EnablePreprocessing = false,
            EnableDeduplication = false,
            TriggerTimeoutMs = 60000
        };

        using var strategy = CreateStrategy(opts);

        // Short text, no triggers
        await strategy.ProcessUtteranceAsync(MakeUtterance("Short text"));

        Assert.Empty(_llmCalls);
    }

    #endregion

    #region Intent Events

    [Fact]
    public async Task DetectedIntent_FiresOnIntentDetectedEvent()
    {
        SetupLlmReturns(new DetectedIntent
        {
            Type = IntentType.Question,
            Confidence = 0.9,
            SourceText = "What is this?"
        });

        var detectedEvents = new List<IntentEvent>();
        using var strategy = CreateStrategy();
        strategy.OnIntentDetected += e => detectedEvents.Add(e);

        await strategy.ProcessUtteranceAsync(MakeUtterance("What is this?", "utt-1"));

        Assert.Single(detectedEvents);
        Assert.Equal("utt-1", detectedEvents[0].UtteranceId);
        Assert.Equal(IntentType.Question, detectedEvents[0].Intent.Type);
    }

    #endregion

    #region UtteranceId Lookup

    [Fact]
    public async Task UtteranceId_DirectLookup_UsesMatchedUtterance()
    {
        SetupLlmReturns(new DetectedIntent
        {
            Type = IntentType.Question,
            Confidence = 0.9,
            SourceText = "What is polymorphism?",
            UtteranceId = "utt-42"
        });

        var detectedEvents = new List<IntentEvent>();
        using var strategy = CreateStrategy();
        strategy.OnIntentDetected += e => detectedEvents.Add(e);

        await strategy.ProcessUtteranceAsync(MakeUtterance("What is polymorphism?", "utt-42"));

        Assert.Single(detectedEvents);
        Assert.Equal("utt-42", detectedEvents[0].UtteranceId);
        Assert.Equal("What is polymorphism?", detectedEvents[0].Intent.OriginalText);
    }

    [Fact]
    public async Task UtteranceId_NullFallback_UsesWordOverlap()
    {
        SetupLlmReturns(new DetectedIntent
        {
            Type = IntentType.Question,
            Confidence = 0.9,
            SourceText = "What is polymorphism?",
            UtteranceId = null  // LLM didn't provide utterance_id
        });

        var detectedEvents = new List<IntentEvent>();
        using var strategy = CreateStrategy();
        strategy.OnIntentDetected += e => detectedEvents.Add(e);

        await strategy.ProcessUtteranceAsync(MakeUtterance("What is polymorphism?", "utt-99"));

        Assert.Single(detectedEvents);
        // Should fall back to word-overlap matching
        Assert.Equal("utt-99", detectedEvents[0].UtteranceId);
    }

    [Fact]
    public async Task UtteranceId_InvalidId_FallsBackToWordOverlap()
    {
        SetupLlmReturns(new DetectedIntent
        {
            Type = IntentType.Question,
            Confidence = 0.9,
            SourceText = "What is polymorphism?",
            UtteranceId = "nonexistent-id"  // ID doesn't match any utterance
        });

        var detectedEvents = new List<IntentEvent>();
        using var strategy = CreateStrategy();
        strategy.OnIntentDetected += e => detectedEvents.Add(e);

        await strategy.ProcessUtteranceAsync(MakeUtterance("What is polymorphism?", "utt-real"));

        Assert.Single(detectedEvents);
        // Should fall back to word-overlap since "nonexistent-id" doesn't match
        Assert.Equal("utt-real", detectedEvents[0].UtteranceId);
    }

    #endregion

    #region Sliding Window Moves Utterances After Detection

    [Fact]
    public async Task AfterDetection_UnprocessedListIsCleared()
    {
        var response = new List<DetectedIntent>
        {
            new() { Type = IntentType.Question, Confidence = 0.9, SourceText = "test" }
        };
        var emptyResponse = new List<DetectedIntent>();

        SetupLlmSequence(response, emptyResponse);

        using var strategy = CreateStrategy();

        // Trigger detection
        await strategy.ProcessUtteranceAsync(MakeUtterance("What is this?"));
        Assert.Single(_llmCalls);

        // Signal pause to trigger again - should have empty unprocessed
        // (pause won't trigger because we disabled it)
        // Instead, add another utterance with ? to trigger
        await strategy.ProcessUtteranceAsync(MakeUtterance("Another question?"));

        Assert.Equal(2, _llmCalls.Count);
        // Second call text should only contain the new utterance (with label prefix)
        Assert.Contains("Another question?", _llmCalls[1].Text);
        Assert.DoesNotContain("What is this?", _llmCalls[1].Text);
    }

    #endregion
}
