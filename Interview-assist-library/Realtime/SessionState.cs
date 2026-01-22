namespace InterviewAssist.Library.Realtime;

/// <summary>
/// Represents the state of a realtime API session at shutdown.
/// Can be used for session persistence or recovery.
/// </summary>
/// <param name="CorrelationId">The correlation ID of the session.</param>
/// <param name="RecentTranscripts">Recent transcripts from the session.</param>
/// <param name="SessionStart">When the session started.</param>
/// <param name="SessionEnd">When the session ended.</param>
public record SessionState(
    string CorrelationId,
    IReadOnlyList<string> RecentTranscripts,
    DateTime SessionStart,
    DateTime SessionEnd)
{
    /// <summary>
    /// Gets the duration of the session.
    /// </summary>
    public TimeSpan Duration => SessionEnd - SessionStart;

    /// <summary>
    /// Gets whether the session has any transcripts.
    /// </summary>
    public bool HasTranscripts => RecentTranscripts.Count > 0;
}
