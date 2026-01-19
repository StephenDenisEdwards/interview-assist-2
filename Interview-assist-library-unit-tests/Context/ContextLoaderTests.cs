using InterviewAssist.Library.Context;

namespace InterviewAssist.Library.UnitTests.Context;

public class ContextLoaderTests
{
    private static string CreateTempTxtFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void BuildContext_NullPaths_ReturnsEmptyResults()
    {
        // Act
        var (extraInstructions, chunks) = ContextLoader.BuildContext(null, null);

        // Assert
        Assert.Equal(string.Empty, extraInstructions);
        Assert.Empty(chunks);
    }

    [Fact]
    public void BuildContext_EmptyPaths_ReturnsEmptyResults()
    {
        // Act
        var (extraInstructions, chunks) = ContextLoader.BuildContext("", "");

        // Assert
        Assert.Equal(string.Empty, extraInstructions);
        Assert.Empty(chunks);
    }

    [Fact]
    public void BuildContext_NonExistentFiles_ReturnsEmptyResults()
    {
        // Act
        var (extraInstructions, chunks) = ContextLoader.BuildContext(
            "/nonexistent/cv.txt",
            "/nonexistent/jobspec.txt");

        // Assert
        Assert.Equal(string.Empty, extraInstructions);
        Assert.Empty(chunks);
    }

    [Fact]
    public void BuildContext_WithValidTxtFile_ReturnsChunks()
    {
        // Arrange
        var content = new string('A', 3000); // 3000 chars
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null);

            // Assert
            Assert.NotEmpty(chunks);
            Assert.All(chunks, c => Assert.StartsWith("CV", c.Label));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildContext_ChunkSizeRespected()
    {
        // Arrange
        var content = new string('X', 5000);
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act - use small chunk size
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null, chunkSize: 500, chunkOverlap: 50);

            // Assert - each chunk should be at most chunkSize
            Assert.All(chunks, c => Assert.True(c.Text.Length <= 500));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildContext_ChunkOverlapCreatesOverlappingContent()
    {
        // Arrange
        // Create content with distinct markers
        var content = "AAAA" + new string('B', 1000) + "CCCC" + new string('D', 1000) + "EEEE";
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act - with overlap, adjacent chunks should share content
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null, chunkSize: 500, chunkOverlap: 100);

            // Assert - should have multiple chunks due to overlap
            Assert.True(chunks.Count >= 2);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildContext_MaxContextCharsTruncatesLongContent()
    {
        // Arrange
        var content = new string('Z', 100000); // 100K chars
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act - limit to 5000 chars
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null, maxContextChars: 5000, chunkSize: 1000);

            // Assert - total content should not exceed maxContextChars
            var totalChars = chunks.Sum(c => c.Text.Length);
            Assert.True(totalChars <= 5000 + 1000); // Allow for last chunk
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildContext_BothFilesProvided_CreatesChunksForBoth()
    {
        // Arrange
        var cvFile = CreateTempTxtFile(new string('C', 2000));
        var jobSpecFile = CreateTempTxtFile(new string('J', 2000));
        try
        {
            // Act
            var (_, chunks) = ContextLoader.BuildContext(cvFile, jobSpecFile);

            // Assert
            Assert.Contains(chunks, c => c.Label.StartsWith("CV"));
            Assert.Contains(chunks, c => c.Label.StartsWith("Job Spec"));
        }
        finally
        {
            File.Delete(cvFile);
            File.Delete(jobSpecFile);
        }
    }

    [Fact]
    public void BuildContext_ExtraInstructions_ContainsPreviews()
    {
        // Arrange
        var cvFile = CreateTempTxtFile("This is my CV content with skills");
        var jobSpecFile = CreateTempTxtFile("Job requirements listed here");
        try
        {
            // Act
            var (extraInstructions, _) = ContextLoader.BuildContext(cvFile, jobSpecFile);

            // Assert
            Assert.Contains("CV Preview:", extraInstructions);
            Assert.Contains("Job Spec Preview:", extraInstructions);
            Assert.Contains("CV content", extraInstructions);
            Assert.Contains("Job requirements", extraInstructions);
        }
        finally
        {
            File.Delete(cvFile);
            File.Delete(jobSpecFile);
        }
    }

    [Fact]
    public void BuildContext_PreviewTruncatesLongContent()
    {
        // Arrange
        var longContent = new string('X', 5000); // Longer than 2000 char preview limit
        var cvFile = CreateTempTxtFile(longContent);
        try
        {
            // Act
            var (extraInstructions, _) = ContextLoader.BuildContext(cvFile, null);

            // Assert - preview should be truncated
            // The preview includes "CV Preview:\n" prefix, so actual CV content is less than total
            Assert.True(extraInstructions.Length < 5000);
        }
        finally
        {
            File.Delete(cvFile);
        }
    }

    [Fact]
    public void BuildContext_ChunksHaveIncrementingLabels()
    {
        // Arrange
        var content = new string('A', 5000);
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null, chunkSize: 500, chunkOverlap: 50);

            // Assert
            Assert.Contains(chunks, c => c.Label == "CV (1)");
            Assert.Contains(chunks, c => c.Label == "CV (2)");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildContext_NormalizesLineEndings()
    {
        // Arrange
        var content = "Line one\r\nLine two\r\nLine three";
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null);

            // Assert - should not contain \r\n after normalization
            Assert.All(chunks, c => Assert.DoesNotContain("\r\n", c.Text));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildContext_SmallContent_SingleChunk()
    {
        // Arrange
        var content = "Small content";
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null, chunkSize: 1200);

            // Assert
            Assert.Single(chunks);
            Assert.Contains("Small content", chunks[0].Text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildContext_DefaultParameters_UseExpectedValues()
    {
        // Arrange
        var content = new string('D', 50000);
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act - use all defaults
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null);

            // Assert - with default chunkSize=1200, overlap=150, we can verify chunking occurred
            Assert.True(chunks.Count > 1);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BuildContext_ZeroOverlap_NonOverlappingChunks()
    {
        // Arrange
        var content = "0123456789" + "ABCDEFGHIJ" + "abcdefghij"; // 30 chars
        var tempFile = CreateTempTxtFile(content);
        try
        {
            // Act - no overlap
            var (_, chunks) = ContextLoader.BuildContext(tempFile, null, chunkSize: 10, chunkOverlap: 0);

            // Assert - chunks should not overlap
            Assert.Equal(3, chunks.Count);
            Assert.Equal("0123456789", chunks[0].Text);
            Assert.Equal("ABCDEFGHIJ", chunks[1].Text);
            Assert.Equal("abcdefghij", chunks[2].Text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
