namespace InterviewAssist.Library.Utilities;

/// <summary>
/// Loads system instructions from various sources with configurable priority.
/// </summary>
public static class SystemInstructionsLoader
{
    /// <summary>
    /// Default system instructions for the C# programming assistant.
    /// </summary>
    public const string DefaultSystemInstructions = """
        You are a C# programming expert assistant.

        MANDATORY BEHAVIOR:
        When calling report_technical_response, you MUST ALWAYS provide both parameters:
        1. answer - your explanation
        2. console_code - complete C# code

        NEVER call the function with only 'answer'. ALWAYS include 'console_code'.
        If no code is needed, set console_code to: "// No code example needed"

        The console_code must be a complete, runnable C# program with Main method.
        """;

    /// <summary>
    /// Loads system instructions using the following priority order:
    /// 1. SystemInstructionsFactory (if provided and returns non-empty)
    /// 2. SystemInstructionsFilePath (if provided and file exists)
    /// 3. SystemInstructions property (if provided)
    /// 4. Default instructions
    /// </summary>
    /// <param name="factory">Optional factory function to generate instructions.</param>
    /// <param name="filePath">Optional file path to load instructions from.</param>
    /// <param name="propertyValue">Optional direct instructions value.</param>
    /// <returns>The resolved system instructions.</returns>
    public static string Load(
        Func<string>? factory = null,
        string? filePath = null,
        string? propertyValue = null)
    {
        // Priority 1: Factory
        if (factory != null)
        {
            try
            {
                var result = factory();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }
            catch
            {
                // Fall through to next priority
            }
        }

        // Priority 2: File path
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var content = File.ReadAllText(filePath);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return content;
                    }
                }
            }
            catch
            {
                // Fall through to next priority
            }
        }

        // Priority 3: Property value
        if (!string.IsNullOrWhiteSpace(propertyValue))
        {
            return propertyValue;
        }

        // Priority 4: Default
        return DefaultSystemInstructions;
    }
}
