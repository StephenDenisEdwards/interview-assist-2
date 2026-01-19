using InterviewAssist.Library.Utilities;
using System.Text.Json;

namespace InterviewAssist.Library.UnitTests.Utilities;

public class JsonRepairUtilityTests
{
    [Fact]
    public void Repair_ValidJsonWithAnswerAndConsoleCode_ReturnsOriginalValues()
    {
        // Arrange
        var input = """{"answer": "Hello world", "console_code": "print('hello')"}""";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Equal("Hello world", parsed.GetProperty("answer").GetString());
        Assert.Equal("print('hello')", parsed.GetProperty("console_code").GetString());
    }

    [Fact]
    public void Repair_ValidJsonWithOnlyAnswer_SetsConsoleCodeToNoCode()
    {
        // Arrange
        var input = """{"answer": "Just an answer"}""";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Equal("Just an answer", parsed.GetProperty("answer").GetString());
        Assert.Equal("no-code", parsed.GetProperty("console_code").GetString());
    }

    [Fact]
    public void Repair_ValidJsonWithEmptyConsoleCode_SetsConsoleCodeToNoCode()
    {
        // Arrange
        var input = """{"answer": "An answer", "console_code": ""}""";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Equal("An answer", parsed.GetProperty("answer").GetString());
        Assert.Equal("no-code", parsed.GetProperty("console_code").GetString());
    }

    [Fact]
    public void Repair_PlainText_WrapsInAnswerField()
    {
        // Arrange
        var input = "This is just plain text, not JSON";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Equal(input, parsed.GetProperty("answer").GetString());
        Assert.Equal("no-code", parsed.GetProperty("console_code").GetString());
    }

    [Fact]
    public void Repair_JsonWithUnescapedNewlinesInString_RepairsAndParses()
    {
        // Arrange - JSON with literal newline inside string value
        var input = "{\"answer\": \"Line one\nLine two\"}";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Contains("Line one", parsed.GetProperty("answer").GetString());
        Assert.Contains("Line two", parsed.GetProperty("answer").GetString());
    }

    [Fact]
    public void Repair_JsonWithDifferentPropertyName_UsesFirstStringProperty()
    {
        // Arrange
        var input = """{"response": "My response text"}""";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Equal("My response text", parsed.GetProperty("answer").GetString());
    }

    [Fact]
    public void Repair_EmptyObject_ReturnsRawAsAnswer()
    {
        // Arrange
        var input = "{}";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Equal("{}", parsed.GetProperty("answer").GetString());
    }

    [Fact]
    public void Repair_JsonArray_TreatsAsPlainText()
    {
        // Arrange
        var input = """["item1", "item2"]""";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Equal(input, parsed.GetProperty("answer").GetString());
    }

    [Fact]
    public void Repair_JsonWithUnterminatedString_ClosesStringAndParses()
    {
        // Arrange - unterminated string
        var input = "{\"answer\": \"unterminated string";

        // Act
        var result = JsonRepairUtility.Repair(input);

        // Assert - should not throw and should return valid JSON
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.NotNull(parsed.GetProperty("answer").GetString());
    }

    [Fact]
    public void Repair_JsonWithSpecialCharacters_PreservesCharacters()
    {
        // Arrange
        var input = """{"answer": "Special chars: \t tab and \\ backslash"}""";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        var answer = parsed.GetProperty("answer").GetString();
        Assert.Contains("\t", answer);
        Assert.Contains("\\", answer);
    }

    [Fact]
    public void Repair_NullAnswerProperty_UsesRawInput()
    {
        // Arrange
        var input = """{"answer": null, "console_code": "some code"}""";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert - when answer is null, should fall back to raw input
        Assert.NotNull(parsed.GetProperty("answer").GetString());
    }

    [Fact]
    public void Repair_MultipleStringProperties_UsesFirstOne()
    {
        // Arrange
        var input = """{"first": "First value", "second": "Second value"}""";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.Equal("First value", parsed.GetProperty("answer").GetString());
    }

    [Fact]
    public void Repair_JsonWithCarriageReturns_RemovesCarriageReturns()
    {
        // Arrange - JSON with \r\n inside string
        var input = "{\"answer\": \"Line one\r\nLine two\"}";

        // Act
        var result = JsonRepairUtility.Repair(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert - \r should be removed
        var answer = parsed.GetProperty("answer").GetString();
        Assert.DoesNotContain("\r", answer);
    }

    [Fact]
    public void Repair_OutputIsValidJson()
    {
        // Arrange
        var inputs = new[]
        {
            "plain text",
            """{"answer": "valid"}""",
            "{broken json",
            """{"nested": {"deep": "value"}}"""
        };

        // Act & Assert
        foreach (var input in inputs)
        {
            var result = JsonRepairUtility.Repair(input);

            // Should not throw
            var parsed = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(parsed.ValueKind == JsonValueKind.Object);
        }
    }
}
