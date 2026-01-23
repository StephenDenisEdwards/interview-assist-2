namespace InterviewAssist.Library.UnitTests.TranscriptionConsole;

public class LlmQuestionDetectorTests
{
    #region RemoveTranscriptionNoise Tests

    [Theory]
    [InlineData("you you you you", "you you")] // Keeps max 2 repetitions
    [InlineData("the the the the", "the the")] // Keeps max 2 repetitions
    [InlineData("um um um", "")] // Filler words are completely removed
    public void RemoveTranscriptionNoise_RepeatedWords_ReducesToMaxTwo(string input, string expected)
    {
        // Act
        var result = LlmQuestionDetector.RemoveTranscriptionNoise(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("you you you you Hello there", "you you Hello there")] // Keeps max 2 "you", then "Hello there"
    [InlineData("um uh er so what is the difference", "so what is the difference")] // filler words removed
    [InlineData("er er er the question is", "the question is")] // er is a filler, all removed
    public void RemoveTranscriptionNoise_MixedContent_ReturnsCleanedText(string input, string expected)
    {
        // Act
        var result = LlmQuestionDetector.RemoveTranscriptionNoise(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("What is the difference between abstract and interface?")]
    [InlineData("Explain how async await works in C#")]
    [InlineData("How does garbage collection work?")]
    public void RemoveTranscriptionNoise_NormalSentences_PassThroughUnchanged(string input)
    {
        // Act
        var result = LlmQuestionDetector.RemoveTranscriptionNoise(input);

        // Assert
        Assert.Equal(input, result);
    }

    [Theory]
    [InlineData("um")]
    [InlineData("uh")]
    [InlineData("er")]
    [InlineData("hmm")]
    [InlineData("um uh er")]
    public void RemoveTranscriptionNoise_FillerWordsOnly_ReturnsEmpty(string input)
    {
        // Act
        var result = LlmQuestionDetector.RemoveTranscriptionNoise(input);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RemoveTranscriptionNoise_NullOrWhitespace_ReturnsEmpty()
    {
        // Act & Assert
        Assert.Equal(string.Empty, LlmQuestionDetector.RemoveTranscriptionNoise(null!));
        Assert.Equal(string.Empty, LlmQuestionDetector.RemoveTranscriptionNoise(""));
        Assert.Equal(string.Empty, LlmQuestionDetector.RemoveTranscriptionNoise("   "));
    }

    [Theory]
    [InlineData("yes yes", "yes yes")] // 2 repetitions allowed
    [InlineData("no no no no", "no no")] // 4+ reduced to 2
    public void RemoveTranscriptionNoise_TwoRepetitionsAllowed_ExcessRemoved(string input, string expected)
    {
        // Act
        var result = LlmQuestionDetector.RemoveTranscriptionNoise(input);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region CorrectTechnicalTerms Tests

    [Theory]
    [InlineData("What is spanty?", "What is Span<T>?")]
    [InlineData("Explain span t in detail", "Explain Span<T> in detail")]
    [InlineData("How does span tea work", "How does Span<T> work")]
    public void CorrectTechnicalTerms_SpanT_CorrectsToProperly(string input, string expected)
    {
        // Act
        var result = LlmQuestionDetector.CorrectTechnicalTerms(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("What is sea sharp?", "What is C#?")]
    [InlineData("How do you write sea shard code?", "How do you write C# code?")]
    [InlineData("Explain c-sharp features", "Explain C# features")]
    public void CorrectTechnicalTerms_CSharp_CorrectsToProperly(string input, string expected)
    {
        // Act
        var result = LlmQuestionDetector.CorrectTechnicalTerms(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("What is quality compare tea?", "What is IEqualityComparer<T>?")]
    [InlineData("Implement equality comparer t", "Implement IEqualityComparer<T>")]
    public void CorrectTechnicalTerms_IEqualityComparer_CorrectsToProperly(string input, string expected)
    {
        // Act
        var result = LlmQuestionDetector.CorrectTechnicalTerms(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("When to use configure await?", "When to use ConfigureAwait?")]
    [InlineData("What does configure a wait false do?", "What does ConfigureAwait false do?")]
    public void CorrectTechnicalTerms_ConfigureAwait_CorrectsToProperly(string input, string expected)
    {
        // Act
        var result = LlmQuestionDetector.CorrectTechnicalTerms(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("What is a normal question without tech terms?")]
    [InlineData("How does inheritance work?")]
    [InlineData("Explain polymorphism.")]
    public void CorrectTechnicalTerms_NoKnownTerms_PassThroughUnchanged(string input)
    {
        // Act
        var result = LlmQuestionDetector.CorrectTechnicalTerms(input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void CorrectTechnicalTerms_CaseInsensitive_CorrectsToProperly()
    {
        // Arrange
        var inputs = new[] { "SPANTY", "Spanty", "spanty", "SpAnTy" };

        // Act & Assert
        foreach (var input in inputs)
        {
            var result = LlmQuestionDetector.CorrectTechnicalTerms(input);
            Assert.Equal("Span<T>", result);
        }
    }

    #endregion

    #region HasCompleteSentence Tests

    [Theory]
    [InlineData("What is the difference?", true)]
    [InlineData("This is a complete sentence.", true)]
    [InlineData("Explain how this works!", true)]
    [InlineData("Is this a question? Yes it is.", true)]
    public void HasCompleteSentence_WithTerminator_ReturnsTrue(string input, bool expected)
    {
        // Act
        var result = LlmQuestionDetector.HasCompleteSentence(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("How does the GC")]
    [InlineData("What is")]
    [InlineData("Explain")]
    [InlineData("")]
    public void HasCompleteSentence_WithoutTerminator_ReturnsFalse(string input)
    {
        // Act
        var result = LlmQuestionDetector.HasCompleteSentence(input);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("OK.")] // Only 3 chars to terminator
    [InlineData("Hi!")] // Only 3 chars to terminator
    [InlineData("A?")] // Only 2 chars to terminator
    public void HasCompleteSentence_VeryShortSentence_ReturnsFalse(string input)
    {
        // Act
        var result = LlmQuestionDetector.HasCompleteSentence(input);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetSemanticFingerprint Tests

    [Fact]
    public void GetSemanticFingerprint_ExtractsSignificantWords_SortedAlphabetically()
    {
        // Arrange
        var input = "what is the difference between async and await";

        // Act
        var result = LlmQuestionDetector.GetSemanticFingerprint(input);

        // Assert - should contain significant words, sorted
        Assert.Contains("async", result);
        Assert.Contains("await", result);
        Assert.Contains("difference", result);
        // Stop words should be excluded
        Assert.DoesNotContain("the", result.Split(' '));
        Assert.DoesNotContain("is", result.Split(' '));
        Assert.DoesNotContain("what", result.Split(' '));
    }

    [Fact]
    public void GetSemanticFingerprint_SimilarQuestions_ProduceSameFingerprint()
    {
        // Arrange - same question with different word order/phrasing
        var q1 = "what is the difference between async and await";
        var q2 = "the difference between await and async what is";

        // Act
        var fp1 = LlmQuestionDetector.GetSemanticFingerprint(q1);
        var fp2 = LlmQuestionDetector.GetSemanticFingerprint(q2);

        // Assert - fingerprints should be identical (same significant words, sorted)
        Assert.Equal(fp1, fp2);
    }

    #endregion
}
