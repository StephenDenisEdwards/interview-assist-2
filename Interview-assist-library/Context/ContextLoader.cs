using System.Text;

namespace InterviewAssist.Library.Context;

/// <summary>
/// Loads and chunks context documents (CV, Job Spec) for use with AI APIs.
/// </summary>
public static class ContextLoader
{
    /// <summary>
    /// Builds context chunks and preview text from CV and Job Spec files.
    /// </summary>
    /// <param name="cvPath">Path to CV document.</param>
    /// <param name="jobSpecPath">Path to Job Spec document.</param>
    /// <param name="maxContextChars">Maximum total characters to include. Default: 40000.</param>
    /// <param name="chunkSize">Size of each chunk in characters. Default: 1200.</param>
    /// <param name="chunkOverlap">Overlap between chunks in characters. Default: 150.</param>
    /// <returns>Tuple of (extraInstructions preview, list of context chunks).</returns>
    public static (string ExtraInstructions, IReadOnlyList<ContextChunk> Chunks) BuildContext(
        string? cvPath,
        string? jobSpecPath,
        int maxContextChars = 40000,
        int chunkSize = 1200,
        int chunkOverlap = 150)
    {
        var chunks = new List<ContextChunk>();
        var previewSb = new StringBuilder();

        var cvText = DocumentTextLoader.LoadAllText(cvPath);
        var jsText = DocumentTextLoader.LoadAllText(jobSpecPath);

        if (!string.IsNullOrWhiteSpace(cvText))
        {
            previewSb.AppendLine("CV Preview:");
            previewSb.AppendLine(TakeFirstChars(cvText, 2000));
            previewSb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(jsText))
        {
            previewSb.AppendLine("Job Spec Preview:");
            previewSb.AppendLine(TakeFirstChars(jsText, 2000));
        }

        AddChunks(chunks, "CV", cvText, chunkSize, chunkOverlap, maxContextChars);
        AddChunks(chunks, "Job Spec", jsText, chunkSize, chunkOverlap, maxContextChars);

        return (previewSb.ToString().Trim(), chunks);
    }

    private static string TakeFirstChars(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var normalized = text.Replace("\r\n", "\n");
        return normalized.Length <= max ? normalized : normalized.Substring(0, max);
    }

    private static void AddChunks(
        List<ContextChunk> list,
        string label,
        string text,
        int chunkSize,
        int overlap,
        int maxTotalChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        text = text.Replace("\r\n", "\n");
        if (text.Length > maxTotalChars)
            text = text.Substring(0, maxTotalChars);

        int idx = 0;
        int count = 0;

        while (idx < text.Length)
        {
            int len = Math.Min(chunkSize, text.Length - idx);
            list.Add(new ContextChunk
            {
                Label = $"{label} ({++count})",
                Text = text.Substring(idx, len)
            });

            idx += (chunkSize - overlap);
            if (idx < 0 || (chunkSize - overlap) <= 0)
                break;
        }
    }
}
