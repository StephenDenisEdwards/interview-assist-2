namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Configuration options for Deepgram streaming transcription.
/// </summary>
public record DeepgramOptions
{
    /// <summary>
    /// Deepgram API key.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Model to use for transcription. Default: "nova-2".
    /// Options: "nova-2", "nova-2-general", "nova-2-meeting", "nova-2-phonecall", etc.
    /// </summary>
    public string Model { get; init; } = "nova-2";

    /// <summary>
    /// Language code for transcription. Default: "en".
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Audio sample rate in Hz. Default: 16000.
    /// </summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary>
    /// Audio encoding. Default: "linear16".
    /// </summary>
    public string Encoding { get; init; } = "linear16";

    /// <summary>
    /// Number of audio channels. Default: 1 (mono).
    /// </summary>
    public int Channels { get; init; } = 1;

    /// <summary>
    /// Enable interim results (is_final: false). Default: true.
    /// </summary>
    public bool InterimResults { get; init; } = true;

    /// <summary>
    /// Enable punctuation in transcription. Default: true.
    /// </summary>
    public bool Punctuate { get; init; } = true;

    /// <summary>
    /// Enable smart formatting (numbers, dates, etc.). Default: true.
    /// </summary>
    public bool SmartFormat { get; init; } = true;

    /// <summary>
    /// Endpointing timeout in milliseconds. Time of silence before finalizing.
    /// Default: 300ms. Set to 0 to disable.
    /// </summary>
    public int EndpointingMs { get; init; } = 300;

    /// <summary>
    /// Utterance end timeout in milliseconds. Gap after last word before utterance_end event.
    /// Default: 1000ms. Requires InterimResults to be true.
    /// </summary>
    public int UtteranceEndMs { get; init; } = 1000;

    /// <summary>
    /// Keywords to boost recognition for (comma-separated or array).
    /// Example: "C#, async, await, Kubernetes"
    /// </summary>
    public string? Keywords { get; init; }

    /// <summary>
    /// Enable Voice Activity Detection. Default: true.
    /// </summary>
    public bool Vad { get; init; } = true;

    /// <summary>
    /// WebSocket URL for Deepgram streaming API.
    /// </summary>
    public string WebSocketUrl { get; init; } = "wss://api.deepgram.com/v1/listen";

    /// <summary>
    /// Connection timeout in milliseconds. Default: 10000.
    /// </summary>
    public int ConnectTimeoutMs { get; init; } = 10000;

    /// <summary>
    /// Keep-alive interval in milliseconds. Default: 10000.
    /// </summary>
    public int KeepAliveIntervalMs { get; init; } = 10000;
}
