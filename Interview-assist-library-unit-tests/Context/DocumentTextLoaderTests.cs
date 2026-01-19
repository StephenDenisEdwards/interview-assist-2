using InterviewAssist.Library.Context;

namespace InterviewAssist.Library.UnitTests.Context;

public class DocumentTextLoaderTests
{
    private static string CreateTempTxtFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void LoadAllText_NullPath_ReturnsEmptyString()
    {
        // Act
        var result = DocumentTextLoader.LoadAllText(null);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void LoadAllText_EmptyPath_ReturnsEmptyString()
    {
        // Act
        var result = DocumentTextLoader.LoadAllText("");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void LoadAllText_WhitespacePath_ReturnsEmptyString()
    {
        // Act
        var result = DocumentTextLoader.LoadAllText("   ");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void LoadAllText_NonExistentFile_ReturnsEmptyString()
    {
        // Act
        var result = DocumentTextLoader.LoadAllText("/nonexistent/file.txt");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void LoadAllText_TxtFile_ReturnsContent()
    {
        // Arrange
        var content = "Hello, this is test content.";
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var result = DocumentTextLoader.LoadAllText(tempFile);

            // Assert
            Assert.Equal(content, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadAllText_TxtFileWithLineEndings_PreservesContent()
    {
        // Arrange
        var content = "Line one\r\nLine two\r\nLine three";
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var result = DocumentTextLoader.LoadAllText(tempFile);

            // Assert - txt files are read as-is (no normalization)
            Assert.Equal(content, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadAllText_UnsupportedExtension_ReturnsEmptyString()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xyz");
        try
        {
            File.WriteAllText(tempFile, "Some content");

            // Act
            var result = DocumentTextLoader.LoadAllText(tempFile);

            // Assert
            Assert.Equal(string.Empty, result);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadAllText_TxtExtensionCaseInsensitive_ReturnsContent()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.TXT");
        try
        {
            var content = "Content in TXT file";
            File.WriteAllText(tempFile, content);

            // Act
            var result = DocumentTextLoader.LoadAllText(tempFile);

            // Assert
            Assert.Equal(content, result);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadAllText_TxtWithMultipleSpaces_PreservesContent()
    {
        // Arrange
        var content = "Word1     Word2\t\tWord3";
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var result = DocumentTextLoader.LoadAllText(tempFile);

            // Assert - txt files are read as-is (no normalization)
            Assert.Equal(content, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadAllText_TxtWithExcessiveNewlines_PreservesContent()
    {
        // Arrange
        var content = "Paragraph 1\n\n\n\n\nParagraph 2";
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var result = DocumentTextLoader.LoadAllText(tempFile);

            // Assert - txt files are read as-is (no normalization)
            Assert.Equal(content, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadAllText_TxtWithLeadingTrailingWhitespace_PreservesContent()
    {
        // Arrange
        var content = "   \n\nActual content\n\n   ";
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var result = DocumentTextLoader.LoadAllText(tempFile);

            // Assert - txt files are read as-is (no normalization or trimming)
            Assert.Equal(content, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadAllText_EmptyTxtFile_ReturnsEmptyString()
    {
        // Arrange
        var tempFile = CreateTempTxtFile("");
        try
        {
            // Act
            var result = DocumentTextLoader.LoadAllText(tempFile);

            // Assert
            Assert.Equal(string.Empty, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
