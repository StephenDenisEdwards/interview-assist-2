namespace InterviewAssist.Library.Utilities;

/// <summary>
/// Common string helper methods.
/// </summary>
public static class StringUtilities
{
    /// <summary>
    /// Returns the first non-null, non-empty, non-whitespace value from the provided strings,
    /// or null if none qualify.
    /// </summary>
    public static string? GetFirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
