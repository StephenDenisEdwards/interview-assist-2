using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.UnitTests.Pipeline.Utterance;

public class IntentDetectorTests
{
    private readonly IntentDetector _detector = new();

    #region Question Detection

    [Theory]
    [InlineData("What is a lock statement?")]
    [InlineData("What is a lock statement used for in C#?")]
    [InlineData("What does async mean?")]
    [InlineData("What's the difference between await and wait?")]
    public void DetectFinal_QuestionWithWhatIs_ReturnsQuestionType(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Question, result.Type);
        Assert.True(result.Confidence >= 0.4);
    }

    [Theory]
    [InlineData("How do I use dependency injection?")]
    [InlineData("How can I implement caching?")]
    [InlineData("How to fix this error?")]
    public void DetectFinal_HowToQuestion_ReturnsHowToSubtype(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Question, result.Type);
        Assert.Equal(IntentSubtype.HowTo, result.Subtype);
    }

    [Theory]
    [InlineData("What is the difference between class and struct?")]
    [InlineData("Compare async and sync methods")]
    [InlineData("Interface vs abstract class?")]
    public void DetectFinal_CompareQuestion_ReturnsCompareSubtype(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Question, result.Type);
        Assert.Equal(IntentSubtype.Compare, result.Subtype);
    }

    [Theory]
    [InlineData("Why isn't my code working?")]
    [InlineData("Why doesn't this compile?")]
    [InlineData("I'm getting an error with null reference")]
    public void DetectFinal_TroubleshootQuestion_ReturnsTroubleshootSubtype(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Question, result.Type);
        Assert.Equal(IntentSubtype.Troubleshoot, result.Subtype);
    }

    [Fact]
    public void DetectFinal_QuestionWithTopic_ExtractsTopic()
    {
        var result = _detector.DetectFinal("What is a lock statement in C#?");

        Assert.Equal(IntentType.Question, result.Type);
        Assert.NotNull(result.Slots.Topic);
        Assert.Contains("lock statement", result.Slots.Topic, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Imperative Detection

    [Theory]
    [InlineData("stop")]
    [InlineData("Stop")]
    [InlineData("cancel")]
    [InlineData("nevermind")]
    [InlineData("never mind")]
    public void DetectFinal_StopCommand_ReturnsStopImperative(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Imperative, result.Type);
        Assert.Equal(IntentSubtype.Stop, result.Subtype);
        Assert.True(result.Confidence >= 0.9);
    }

    [Theory]
    [InlineData("repeat")]
    [InlineData("say that again")]
    [InlineData("what did you say")]
    [InlineData("repeat the last one")]
    public void DetectFinal_RepeatCommand_ReturnsRepeatImperative(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Imperative, result.Type);
        Assert.Equal(IntentSubtype.Repeat, result.Subtype);
    }

    [Fact]
    public void DetectFinal_RepeatNumber_ExtractsReference()
    {
        var result = _detector.DetectFinal("repeat number 3");

        Assert.Equal(IntentType.Imperative, result.Type);
        Assert.Equal(IntentSubtype.Repeat, result.Subtype);
        Assert.Equal(3, result.Slots.Count);
        Assert.Equal("number 3", result.Slots.Reference);
    }

    [Theory]
    [InlineData("continue")]
    [InlineData("go on")]
    [InlineData("next")]
    [InlineData("proceed")]
    [InlineData("keep going")]
    public void DetectFinal_ContinueCommand_ReturnsContinueImperative(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Imperative, result.Type);
        Assert.Equal(IntentSubtype.Continue, result.Subtype);
    }

    [Theory]
    [InlineData("start over")]
    [InlineData("from the beginning")]
    [InlineData("reset")]
    public void DetectFinal_StartOverCommand_ReturnsStartOverImperative(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Imperative, result.Type);
        Assert.Equal(IntentSubtype.StartOver, result.Subtype);
    }

    [Fact]
    public void DetectFinal_GenerateQuestions_ReturnsGenerateImperative()
    {
        var result = _detector.DetectFinal("generate 20 questions about C#");

        Assert.Equal(IntentType.Imperative, result.Type);
        Assert.Equal(IntentSubtype.Generate, result.Subtype);
        Assert.Equal(20, result.Slots.Count);
    }

    #endregion

    #region Polite Imperatives

    [Theory]
    [InlineData("please stop")]
    [InlineData("can you stop")]
    [InlineData("could you repeat that")]
    [InlineData("would you continue")]
    public void DetectFinal_PoliteImperative_ReturnsImperative(string text)
    {
        var result = _detector.DetectFinal(text);

        Assert.Equal(IntentType.Imperative, result.Type);
    }

    [Fact]
    public void DetectFinal_CanYouRepeat_ReturnsRepeatNotQuestion()
    {
        // "Can you repeat" should be imperative, not question
        var result = _detector.DetectFinal("can you repeat that");

        Assert.Equal(IntentType.Imperative, result.Type);
        Assert.Equal(IntentSubtype.Repeat, result.Subtype);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void DetectFinal_EmptyText_ReturnsOther()
    {
        var result = _detector.DetectFinal("");

        Assert.Equal(IntentType.Other, result.Type);
    }

    [Fact]
    public void DetectFinal_Statement_ReturnsStatement()
    {
        var result = _detector.DetectFinal("The weather is nice today.");

        Assert.Equal(IntentType.Statement, result.Type);
    }

    [Fact]
    public void DetectCandidate_LowConfidence_ReturnsNull()
    {
        // A statement has low confidence for both question and imperative
        var result = _detector.DetectCandidate("maybe");

        Assert.Null(result);
    }

    [Fact]
    public void DetectCandidate_PartialQuestion_ReturnsCandidate()
    {
        // Even partial question "What is" should return a candidate
        var result = _detector.DetectCandidate("What is a lock");

        Assert.NotNull(result);
        Assert.Equal(IntentType.Question, result.Type);
    }

    #endregion

    #region Split Utterance Handling

    [Fact]
    public void DetectFinal_SplitQuestionAcrossChunks_DetectsCompleteQuestion()
    {
        // Simulating: first chunk "What is a lock statement"
        // then complete: "What is a lock statement used for in C#?"
        var result = _detector.DetectFinal("What is a lock statement used for in C#?");

        Assert.Equal(IntentType.Question, result.Type);
        Assert.True(result.Confidence >= 0.5); // Higher confidence with question mark
    }

    #endregion
}
