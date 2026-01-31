using System.Text.Json.Serialization;

namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Deepgram streaming transcription response.
/// </summary>
public record DeepgramResponse
{
    /// <summary>
    /// Response type: "Results", "Metadata", "UtteranceEnd", etc.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Channel index for multi-channel audio.
    /// </summary>
    [JsonPropertyName("channel_index")]
    public int[]? ChannelIndex { get; init; }

    /// <summary>
    /// Duration of audio processed so far in seconds.
    /// </summary>
    [JsonPropertyName("duration")]
    public double Duration { get; init; }

    /// <summary>
    /// Start time of this result in seconds.
    /// </summary>
    [JsonPropertyName("start")]
    public double Start { get; init; }

    /// <summary>
    /// Whether this is a final result (true) or interim (false).
    /// </summary>
    [JsonPropertyName("is_final")]
    public bool IsFinal { get; init; }

    /// <summary>
    /// Whether speech has ended (for endpointing).
    /// </summary>
    [JsonPropertyName("speech_final")]
    public bool SpeechFinal { get; init; }

    /// <summary>
    /// Channel results containing alternatives.
    /// </summary>
    [JsonPropertyName("channel")]
    public DeepgramChannel? Channel { get; init; }

    /// <summary>
    /// Metadata about the stream.
    /// </summary>
    [JsonPropertyName("metadata")]
    public DeepgramMetadata? Metadata { get; init; }
}

/// <summary>
/// Channel containing transcription alternatives.
/// </summary>
public record DeepgramChannel
{
    /// <summary>
    /// Alternative transcriptions, ordered by confidence.
    /// </summary>
    [JsonPropertyName("alternatives")]
    public DeepgramAlternative[]? Alternatives { get; init; }
}

/// <summary>
/// A single transcription alternative.
/// </summary>
public record DeepgramAlternative
{
    /// <summary>
    /// The transcribed text.
    /// </summary>
    [JsonPropertyName("transcript")]
    public string? Transcript { get; init; }

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>
    /// Word-level details.
    /// </summary>
    [JsonPropertyName("words")]
    public DeepgramWord[]? Words { get; init; }
}

/// <summary>
/// Word-level transcription details.
/// </summary>
public record DeepgramWord
{
    /// <summary>
    /// The word text.
    /// </summary>
    [JsonPropertyName("word")]
    public string? Word { get; init; }

    /// <summary>
    /// Start time in seconds.
    /// </summary>
    [JsonPropertyName("start")]
    public double Start { get; init; }

    /// <summary>
    /// End time in seconds.
    /// </summary>
    [JsonPropertyName("end")]
    public double End { get; init; }

    /// <summary>
    /// Confidence score for this word.
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>
    /// Whether this word was punctuated.
    /// </summary>
    [JsonPropertyName("punctuated_word")]
    public string? PunctuatedWord { get; init; }

    /// <summary>
    /// Speaker identifier (0-based index) when diarization is enabled.
    /// </summary>
    [JsonPropertyName("speaker")]
    public int? Speaker { get; init; }
}

/// <summary>
/// Metadata about the Deepgram stream.
/// </summary>
public record DeepgramMetadata
{
    /// <summary>
    /// Unique request ID.
    /// </summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>
    /// Model used for transcription.
    /// </summary>
    [JsonPropertyName("model_info")]
    public DeepgramModelInfo? ModelInfo { get; init; }

    /// <summary>
    /// SHA256 hash of the audio.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }
}

/// <summary>
/// Information about the model used.
/// </summary>
public record DeepgramModelInfo
{
    /// <summary>
    /// Model name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Model version.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>
    /// Model architecture.
    /// </summary>
    [JsonPropertyName("arch")]
    public string? Arch { get; init; }
}

/// <summary>
/// Utterance end event indicating a pause in speech.
/// </summary>
public record DeepgramUtteranceEnd
{
    /// <summary>
    /// Event type (should be "UtteranceEnd").
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Channel index.
    /// </summary>
    [JsonPropertyName("channel")]
    public int[]? Channel { get; init; }

    /// <summary>
    /// Last word end time in seconds.
    /// </summary>
    [JsonPropertyName("last_word_end")]
    public double LastWordEnd { get; init; }
}

/// <summary>
/// Keep-alive message to maintain WebSocket connection.
/// </summary>
public record DeepgramKeepAlive
{
    /// <summary>
    /// Message type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "KeepAlive";
}

/// <summary>
/// Close stream message.
/// </summary>
public record DeepgramCloseStream
{
    /// <summary>
    /// Message type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "CloseStream";
}
