namespace InterviewAssist.Library.Pipeline.Recording;

/// <summary>
/// Configuration options for session recording.
/// </summary>
public sealed record RecordingOptions
{
    /// <summary>Folder path for session recordings.</summary>
    public string Folder { get; init; } = "recordings";

    /// <summary>
    /// File name pattern. {timestamp} will be replaced with the recording start time.
    /// </summary>
    public string FileNamePattern { get; init; } = "session-{timestamp}.recording.jsonl";

    /// <summary>
    /// Whether to automatically start recording when the session begins.
    /// </summary>
    public bool AutoStart { get; init; } = false;

    /// <summary>
    /// Whether to save captured audio as a WAV file alongside the JSONL recording.
    /// </summary>
    public bool SaveAudio { get; init; } = false;

    /// <summary>
    /// Generate a file path for a new recording.
    /// </summary>
    public string GenerateFilePath()
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        var fileName = FileNamePattern
            .Replace("{timestamp}", timestamp)
            .Replace("{pid}", Environment.ProcessId.ToString());
        return VersionedPath(Path.Combine(Folder, fileName));
    }

    /// <summary>
    /// Generate a recording file path derived from a playback source file.
    /// For .recording.jsonl sources, reuses the session ID and appends a version.
    /// For .wav sources, derives the session ID and produces a .recording.jsonl.
    /// </summary>
    public string GeneratePlaybackFilePath(string sourceFile)
    {
        var sessionId = SessionReportGenerator.ExtractSessionId(sourceFile);
        if (sessionId != null)
        {
            return VersionedPath(Path.Combine(Folder, $"{sessionId}.recording.jsonl"));
        }

        // Fallback: use source filename with .recording.jsonl extension
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(sourceFile))
            ?? Path.GetFileNameWithoutExtension(sourceFile);
        return VersionedPath(Path.Combine(Folder, $"{baseName}.recording.jsonl"));
    }

    private static string VersionedPath(string filePath)
    {
        if (!File.Exists(filePath))
            return filePath;

        var dir = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(fileName); // .jsonl
        var nameWithoutExt = fileName[..^ext.Length];
        // Handle double extensions like .recording.jsonl
        var secondExt = Path.GetExtension(nameWithoutExt);
        if (!string.IsNullOrEmpty(secondExt))
        {
            nameWithoutExt = nameWithoutExt[..^secondExt.Length];
            ext = secondExt + ext; // .recording.jsonl
        }
        var version = 2;
        while (File.Exists(Path.Combine(dir, $"{nameWithoutExt}-v{version}{ext}")))
            version++;
        return Path.Combine(dir, $"{nameWithoutExt}-v{version}{ext}");
    }
}
