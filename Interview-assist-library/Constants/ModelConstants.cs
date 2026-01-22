namespace InterviewAssist.Library.Constants;

/// <summary>
/// Constants for OpenAI model versions and API endpoints.
/// Centralizes model configuration to simplify version updates.
/// </summary>
public static class ModelConstants
{
    /// <summary>
    /// Default realtime model for the OpenAI Realtime API.
    /// </summary>
    public const string DefaultRealtimeModel = "gpt-4o-realtime-preview-2024-12-17";

    /// <summary>
    /// Default transcription model for Whisper STT.
    /// </summary>
    public const string DefaultTranscriptionModel = "whisper-1";

    /// <summary>
    /// Base URL for the OpenAI Realtime WebSocket API.
    /// </summary>
    public const string RealtimeApiBaseUrl = "wss://api.openai.com/v1/realtime";

    /// <summary>
    /// Base URL for the OpenAI REST API.
    /// </summary>
    public const string RestApiBaseUrl = "https://api.openai.com/v1";

    /// <summary>
    /// Builds the full WebSocket URL for the Realtime API.
    /// </summary>
    /// <param name="model">The model to use.</param>
    /// <returns>The full WebSocket URL with model query parameter.</returns>
    public static string BuildRealtimeUrl(string model) =>
        $"{RealtimeApiBaseUrl}?model={model}";
}
