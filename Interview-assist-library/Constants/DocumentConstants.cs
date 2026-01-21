namespace InterviewAssist.Library.Constants;

/// <summary>
/// Constants related to document processing and context loading.
/// </summary>
public static class DocumentConstants
{
    /// <summary>
    /// Default maximum total characters to include in context.
    /// </summary>
    public const int DefaultMaxContextChars = 40000;

    /// <summary>
    /// Default size of each context chunk in characters.
    /// </summary>
    public const int DefaultChunkSize = 1200;

    /// <summary>
    /// Default overlap between chunks in characters.
    /// </summary>
    public const int DefaultChunkOverlap = 150;

    /// <summary>
    /// Maximum characters for document preview sections.
    /// </summary>
    public const int PreviewMaxChars = 2000;
}
