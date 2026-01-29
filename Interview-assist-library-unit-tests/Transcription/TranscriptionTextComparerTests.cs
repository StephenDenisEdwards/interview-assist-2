using InterviewAssist.Library.Transcription;

namespace InterviewAssist.Library.UnitTests.Transcription;

public class TranscriptionTextComparerTests
{
    #region JaccardSimilarity Tests

    [Fact]
    public void JaccardSimilarity_IdenticalTexts_ReturnsOne()
    {
        // Arrange
        var text1 = "Hello world how are you";
        var text2 = "Hello world how are you";

        // Act
        var result = TranscriptionTextComparer.JaccardSimilarity(text1, text2);

        // Assert
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void JaccardSimilarity_CompletelyDifferent_ReturnsZero()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "Foo bar baz";

        // Act
        var result = TranscriptionTextComparer.JaccardSimilarity(text1, text2);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void JaccardSimilarity_PartialOverlap_ReturnsCorrectValue()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "Hello there";
        // Intersection: {Hello} = 1
        // Union: {Hello, world, there} = 3
        // Expected: 1/3 ≈ 0.333

        // Act
        var result = TranscriptionTextComparer.JaccardSimilarity(text1, text2);

        // Assert
        Assert.Equal(1.0 / 3.0, result, precision: 3);
    }

    [Fact]
    public void JaccardSimilarity_BothEmpty_ReturnsOne()
    {
        // Arrange
        var text1 = "";
        var text2 = "";

        // Act
        var result = TranscriptionTextComparer.JaccardSimilarity(text1, text2);

        // Assert
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void JaccardSimilarity_OneEmpty_ReturnsZero()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "";

        // Act
        var result = TranscriptionTextComparer.JaccardSimilarity(text1, text2);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void JaccardSimilarity_CaseInsensitive()
    {
        // Arrange
        var text1 = "Hello World";
        var text2 = "hello world";

        // Act
        var result = TranscriptionTextComparer.JaccardSimilarity(text1, text2);

        // Assert
        Assert.Equal(1.0, result);
    }

    #endregion

    #region CommonPrefix Tests

    [Fact]
    public void CommonPrefix_IdenticalTexts_ReturnsFullText()
    {
        // Arrange
        var text1 = "Hello world how are you";
        var text2 = "Hello world how are you";

        // Act
        var result = TranscriptionTextComparer.CommonPrefix(text1, text2);

        // Assert
        Assert.Equal("Hello world how are you", result);
    }

    [Fact]
    public void CommonPrefix_SharedStart_ReturnsCommonPart()
    {
        // Arrange
        var text1 = "Hello world how are you";
        var text2 = "Hello world what is up";

        // Act
        var result = TranscriptionTextComparer.CommonPrefix(text1, text2);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void CommonPrefix_NoCommonStart_ReturnsEmpty()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "Goodbye world";

        // Act
        var result = TranscriptionTextComparer.CommonPrefix(text1, text2);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CommonPrefix_OneEmpty_ReturnsEmpty()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "";

        // Act
        var result = TranscriptionTextComparer.CommonPrefix(text1, text2);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CommonPrefix_CaseInsensitive()
    {
        // Arrange
        var text1 = "Hello World";
        var text2 = "HELLO world goodbye";

        // Act
        var result = TranscriptionTextComparer.CommonPrefix(text1, text2);

        // Assert
        Assert.Equal("Hello World", result);
    }

    #endregion

    #region CommonSuffix Tests

    [Fact]
    public void CommonSuffix_IdenticalTexts_ReturnsFullText()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "Hello world";

        // Act
        var result = TranscriptionTextComparer.CommonSuffix(text1, text2);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void CommonSuffix_SharedEnd_ReturnsCommonPart()
    {
        // Arrange
        var text1 = "Hello world today";
        var text2 = "Goodbye world today";

        // Act
        var result = TranscriptionTextComparer.CommonSuffix(text1, text2);

        // Assert
        Assert.Equal("world today", result);
    }

    [Fact]
    public void CommonSuffix_NoCommonEnd_ReturnsEmpty()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "Goodbye there";

        // Act
        var result = TranscriptionTextComparer.CommonSuffix(text1, text2);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region AreMatching Tests

    [Fact]
    public void AreMatching_HighSimilarity_ReturnsTrue()
    {
        // Arrange
        var text1 = "Hello world how are you today";
        var text2 = "Hello world how are you";

        // Act
        var result = TranscriptionTextComparer.AreMatching(text1, text2, threshold: 0.7);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreMatching_LowSimilarity_ReturnsFalse()
    {
        // Arrange
        var text1 = "Hello world";
        var text2 = "Goodbye there friend";

        // Act
        var result = TranscriptionTextComparer.AreMatching(text1, text2, threshold: 0.5);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreMatching_ExactlyAtThreshold_ReturnsTrue()
    {
        // Arrange - 50% overlap
        var text1 = "Hello world";
        var text2 = "Hello there";
        // Jaccard = 1/3

        // Act
        var result = TranscriptionTextComparer.AreMatching(text1, text2, threshold: 1.0 / 3.0);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetExtension Tests

    [Fact]
    public void GetExtension_NewTextExtendsStable_ReturnsExtension()
    {
        // Arrange
        var stable = "Hello world";
        var newText = "Hello world how are you";

        // Act
        var result = TranscriptionTextComparer.GetExtension(stable, newText);

        // Assert
        Assert.Equal("how are you", result);
    }

    [Fact]
    public void GetExtension_NewTextSameAsStable_ReturnsEmpty()
    {
        // Arrange
        var stable = "Hello world";
        var newText = "Hello world";

        // Act
        var result = TranscriptionTextComparer.GetExtension(stable, newText);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetExtension_NewTextDoesNotStartWithStable_ReturnsNull()
    {
        // Arrange
        var stable = "Hello world";
        var newText = "Goodbye world how are you";

        // Act
        var result = TranscriptionTextComparer.GetExtension(stable, newText);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetExtension_StableEmpty_ReturnsFullNewText()
    {
        // Arrange
        var stable = "";
        var newText = "Hello world";

        // Act
        var result = TranscriptionTextComparer.GetExtension(stable, newText);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void GetExtension_NewTextShorterThanStable_ReturnsNull()
    {
        // Arrange
        var stable = "Hello world how are you";
        var newText = "Hello world";

        // Act
        var result = TranscriptionTextComparer.GetExtension(stable, newText);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region FindAgreedText Tests

    [Fact]
    public void FindAgreedText_AllIdentical_ReturnsFullText()
    {
        // Arrange
        var transcripts = new[] { "Hello world", "Hello world", "Hello world" };

        // Act
        var result = TranscriptionTextComparer.FindAgreedText(transcripts);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void FindAgreedText_CommonPrefix_ReturnsPrefix()
    {
        // Arrange
        var transcripts = new[]
        {
            "Hello world how are you",
            "Hello world what is up",
            "Hello world nice day"
        };

        // Act
        var result = TranscriptionTextComparer.FindAgreedText(transcripts);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void FindAgreedText_NoCommonPrefix_ReturnsEmpty()
    {
        // Arrange
        var transcripts = new[] { "Hello world", "Goodbye world", "Hey there" };

        // Act
        var result = TranscriptionTextComparer.FindAgreedText(transcripts);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FindAgreedText_SingleTranscript_ReturnsFullText()
    {
        // Arrange
        var transcripts = new[] { "Hello world" };

        // Act
        var result = TranscriptionTextComparer.FindAgreedText(transcripts);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void FindAgreedText_EmptyCollection_ReturnsEmpty()
    {
        // Arrange
        var transcripts = Array.Empty<string>();

        // Act
        var result = TranscriptionTextComparer.FindAgreedText(transcripts);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region Normalize Tests

    [Fact]
    public void Normalize_ExtraWhitespace_NormalizesToSingleSpaces()
    {
        // Arrange
        var text = "  Hello   world  how   are  you  ";

        // Act
        var result = TranscriptionTextComparer.Normalize(text);

        // Assert
        Assert.Equal("Hello world how are you", result);
    }

    [Fact]
    public void Normalize_EmptyString_ReturnsEmpty()
    {
        // Arrange
        var text = "";

        // Act
        var result = TranscriptionTextComparer.Normalize(text);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_OnlyWhitespace_ReturnsEmpty()
    {
        // Arrange
        var text = "   \t\n  ";

        // Act
        var result = TranscriptionTextComparer.Normalize(text);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion
}
