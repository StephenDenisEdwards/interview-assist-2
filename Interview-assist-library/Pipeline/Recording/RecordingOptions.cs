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
        return Path.Combine(Folder, fileName);
    }
}
