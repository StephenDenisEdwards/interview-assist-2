using InterviewAssist.Library.Utilities;

namespace InterviewAssist.Library.Tests.Utilities;

public class SystemInstructionsLoaderTests
{
    #region Default Behavior
    [Fact]
    public void Load_WithNoParameters_ReturnsDefaultInstructions()
    {
        var result = SystemInstructionsLoader.Load();

        Assert.Equal(SystemInstructionsLoader.DefaultSystemInstructions, result);
    }

    [Fact]
    public void Load_WithAllNullParameters_ReturnsDefaultInstructions()
    {
        var result = SystemInstructionsLoader.Load(null, null, null);

        Assert.Equal(SystemInstructionsLoader.DefaultSystemInstructions, result);
    }
    #endregion

    #region Property Priority
    [Fact]
    public void Load_WithPropertyValue_ReturnsPropertyValue()
    {
        var customInstructions = "Custom instructions from property";

        var result = SystemInstructionsLoader.Load(
            factory: null,
            filePath: null,
            propertyValue: customInstructions);

        Assert.Equal(customInstructions, result);
    }

    [Fact]
    public void Load_WithEmptyPropertyValue_ReturnsDefault()
    {
        var result = SystemInstructionsLoader.Load(
            factory: null,
            filePath: null,
            propertyValue: "");

        Assert.Equal(SystemInstructionsLoader.DefaultSystemInstructions, result);
    }

    [Fact]
    public void Load_WithWhitespacePropertyValue_ReturnsDefault()
    {
        var result = SystemInstructionsLoader.Load(
            factory: null,
            filePath: null,
            propertyValue: "   ");

        Assert.Equal(SystemInstructionsLoader.DefaultSystemInstructions, result);
    }
    #endregion

    #region File Path Priority
    [Fact]
    public void Load_WithValidFilePath_ReturnsFileContent()
    {
        var tempFile = Path.GetTempFileName();
        var fileContent = "Instructions from file";
        File.WriteAllText(tempFile, fileContent);

        try
        {
            var result = SystemInstructionsLoader.Load(
                factory: null,
                filePath: tempFile,
                propertyValue: null);

            Assert.Equal(fileContent, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_WithFilePath_TakesPriorityOverProperty()
    {
        var tempFile = Path.GetTempFileName();
        var fileContent = "Instructions from file";
        File.WriteAllText(tempFile, fileContent);

        try
        {
            var result = SystemInstructionsLoader.Load(
                factory: null,
                filePath: tempFile,
                propertyValue: "Instructions from property");

            Assert.Equal(fileContent, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_WithNonExistentFilePath_FallsBackToProperty()
    {
        var propertyValue = "Fallback to property";

        var result = SystemInstructionsLoader.Load(
            factory: null,
            filePath: @"C:\nonexistent\path\instructions.txt",
            propertyValue: propertyValue);

        Assert.Equal(propertyValue, result);
    }

    [Fact]
    public void Load_WithNonExistentFilePath_FallsBackToDefault()
    {
        var result = SystemInstructionsLoader.Load(
            factory: null,
            filePath: @"C:\nonexistent\path\instructions.txt",
            propertyValue: null);

        Assert.Equal(SystemInstructionsLoader.DefaultSystemInstructions, result);
    }

    [Fact]
    public void Load_WithEmptyFile_FallsBackToProperty()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "");
        var propertyValue = "Fallback to property";

        try
        {
            var result = SystemInstructionsLoader.Load(
                factory: null,
                filePath: tempFile,
                propertyValue: propertyValue);

            Assert.Equal(propertyValue, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
    #endregion

    #region Factory Priority
    [Fact]
    public void Load_WithFactory_ReturnsFactoryResult()
    {
        var factoryResult = "Instructions from factory";

        var result = SystemInstructionsLoader.Load(
            factory: () => factoryResult,
            filePath: null,
            propertyValue: null);

        Assert.Equal(factoryResult, result);
    }

    [Fact]
    public void Load_WithFactory_TakesPriorityOverFilePath()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Instructions from file");
        var factoryResult = "Instructions from factory";

        try
        {
            var result = SystemInstructionsLoader.Load(
                factory: () => factoryResult,
                filePath: tempFile,
                propertyValue: null);

            Assert.Equal(factoryResult, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_WithFactory_TakesPriorityOverProperty()
    {
        var factoryResult = "Instructions from factory";

        var result = SystemInstructionsLoader.Load(
            factory: () => factoryResult,
            filePath: null,
            propertyValue: "Instructions from property");

        Assert.Equal(factoryResult, result);
    }

    [Fact]
    public void Load_WithFactoryReturningEmpty_FallsBackToFilePath()
    {
        var tempFile = Path.GetTempFileName();
        var fileContent = "Instructions from file";
        File.WriteAllText(tempFile, fileContent);

        try
        {
            var result = SystemInstructionsLoader.Load(
                factory: () => "",
                filePath: tempFile,
                propertyValue: null);

            Assert.Equal(fileContent, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_WithFactoryReturningWhitespace_FallsBackToProperty()
    {
        var propertyValue = "Instructions from property";

        var result = SystemInstructionsLoader.Load(
            factory: () => "   ",
            filePath: null,
            propertyValue: propertyValue);

        Assert.Equal(propertyValue, result);
    }

    [Fact]
    public void Load_WithThrowingFactory_FallsBackToFilePath()
    {
        var tempFile = Path.GetTempFileName();
        var fileContent = "Instructions from file";
        File.WriteAllText(tempFile, fileContent);

        try
        {
            var result = SystemInstructionsLoader.Load(
                factory: () => throw new InvalidOperationException("Factory error"),
                filePath: tempFile,
                propertyValue: null);

            Assert.Equal(fileContent, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_WithThrowingFactory_FallsBackToProperty()
    {
        var propertyValue = "Instructions from property";

        var result = SystemInstructionsLoader.Load(
            factory: () => throw new InvalidOperationException("Factory error"),
            filePath: null,
            propertyValue: propertyValue);

        Assert.Equal(propertyValue, result);
    }

    [Fact]
    public void Load_WithThrowingFactory_FallsBackToDefault()
    {
        var result = SystemInstructionsLoader.Load(
            factory: () => throw new InvalidOperationException("Factory error"),
            filePath: null,
            propertyValue: null);

        Assert.Equal(SystemInstructionsLoader.DefaultSystemInstructions, result);
    }
    #endregion

    #region Default Instructions Content
    [Fact]
    public void DefaultSystemInstructions_ContainsCSharpReference()
    {
        Assert.Contains("C#", SystemInstructionsLoader.DefaultSystemInstructions);
    }

    [Fact]
    public void DefaultSystemInstructions_ContainsFunctionCallInstructions()
    {
        Assert.Contains("report_technical_response", SystemInstructionsLoader.DefaultSystemInstructions);
    }

    [Fact]
    public void DefaultSystemInstructions_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(SystemInstructionsLoader.DefaultSystemInstructions));
    }
    #endregion
}
