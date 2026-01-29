namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Interface for streaming transcription services with stability-aware text tracking.
/// Emits events for stable (confirmed), provisional (may change), and full hypothesis updates.
/// </summary>
public interface IStreamingTranscriptionService : IAsyncDisposable
{
    /// <summary>
    /// Raised when text has been confirmed as stable and will not change.
    /// </summary>
    event Action<StableTextEventArgs>? OnStableText;

    /// <summary>
    /// Raised when provisional text is available that may still be revised.
    /// </summary>
    event Action<ProvisionalTextEventArgs>? OnProvisionalText;

    /// <summary>
    /// Raised with the full hypothesis including both stable and provisional text.
    /// </summary>
    event Action<HypothesisEventArgs>? OnFullHypothesis;

    /// <summary>
    /// Raised for informational messages.
    /// </summary>
    event Action<string>? OnInfo;

    /// <summary>
    /// Raised for warning messages.
    /// </summary>
    event Action<string>? OnWarning;

    /// <summary>
    /// Raised when an error occurs.
    /// </summary>
    event Action<Exception>? OnError;

    /// <summary>
    /// Starts the transcription service.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the transcription service.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Gets the accumulated stable transcript text.
    /// </summary>
    /// <returns>All stable text confirmed so far.</returns>
    string GetStableTranscript();

    /// <summary>
    /// Gets the current provisional transcript text.
    /// </summary>
    /// <returns>Current provisional text that may still change.</returns>
    string GetProvisionalTranscript();
}
