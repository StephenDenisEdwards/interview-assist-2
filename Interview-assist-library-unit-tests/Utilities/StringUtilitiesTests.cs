using InterviewAssist.Library.Utilities;

namespace InterviewAssist.Library.UnitTests.Utilities;

public class StringUtilitiesTests
{
    [Fact]
    public void GetFirstNonEmpty_ReturnsFirstNonWhitespace()
    {
        var result = StringUtilities.GetFirstNonEmpty(null, "", "  ", "hello", "world");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void GetFirstNonEmpty_ReturnsNull_WhenAllEmpty()
    {
        var result = StringUtilities.GetFirstNonEmpty(null, "", "  ", "\t");
        Assert.Null(result);
    }

    [Fact]
    public void GetFirstNonEmpty_ReturnsNull_WhenNoArgs()
    {
        var result = StringUtilities.GetFirstNonEmpty();
        Assert.Null(result);
    }

    [Fact]
    public void GetFirstNonEmpty_ReturnsSingleValue()
    {
        var result = StringUtilities.GetFirstNonEmpty("only");
        Assert.Equal("only", result);
    }

    [Fact]
    public void GetFirstNonEmpty_SkipsNullAndEmpty_ReturnsTrimmedValue()
    {
        var result = StringUtilities.GetFirstNonEmpty(null, "", "value");
        Assert.Equal("value", result);
    }
}
