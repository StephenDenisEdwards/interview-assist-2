using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace InterviewAssist.Library.Context;

/// <summary>
/// Loads text content from various document formats (txt, docx, pdf).
/// </summary>
public static class DocumentTextLoader
{
    /// <summary>
    /// Loads all text from a document file.
    /// </summary>
    /// <param name="path">Path to the document file.</param>
    /// <returns>Extracted text content, or empty string if file doesn't exist or format is unsupported.</returns>
    public static string LoadAllText(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (!File.Exists(path)) return string.Empty;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".txt" => File.ReadAllText(path),
            ".docx" => ReadDocx(path),
            ".pdf" => ReadPdf(path),
            _ => string.Empty
        };
    }

    private static string ReadDocx(string path)
    {
        using var fs = File.OpenRead(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        var entry = zip.GetEntry("word/document.xml");
        if (entry == null) return string.Empty;

        using var s = entry.Open();
        using var reader = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var xml = reader.ReadToEnd();

        // Convert paragraph and tab markers to text, then strip tags.
        xml = xml.Replace("</w:p>", "\n").Replace("<w:tab/>", " ");
        var text = Regex.Replace(xml, "<[^>]+>", " ");
        return Normalize(text);
    }

    private static string ReadPdf(string path)
    {
        var sb = new StringBuilder(64 * 1024);
        using var doc = PdfDocument.Open(path);
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return Normalize(sb.ToString());
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = text.Replace("\r\n", "\n");
        text = Regex.Replace(text, "[ \t]+", " ");
        text = Regex.Replace(text, "\n{3,}", "\n\n");
        return text.Trim();
    }
}
