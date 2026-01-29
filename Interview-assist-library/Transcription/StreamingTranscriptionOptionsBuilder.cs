using InterviewAssist.Library.Constants;

namespace InterviewAssist.Library.Transcription;

/// <summary>
/// Builder class for constructing StreamingTranscriptionOptions with a fluent API.
/// </summary>
public class StreamingTranscriptionOptionsBuilder
{
    private string _apiKey = string.Empty;
    private TranscriptionMode _mode = TranscriptionMode.Basic;
    private int _sampleRate = 24000;
    private string? _language;
    private double _silenceThreshold = TranscriptionConstants.DefaultSilenceThreshold;
    private bool _enableContextPrompting = true;
    private int _contextPromptMaxChars = 200;
    private string? _vocabularyPrompt;

    // Basic mode options
    private int _basicBatchMs = 3000;
    private int _basicMaxBatchMs = 6000;

    // Revision mode options
    private int _revisionOverlapMs = 1500;
    private int _revisionBatchMs = 2000;
    private int _revisionAgreementCount = 2;
    private double _revisionSimilarityThreshold = 0.85;

    // Streaming mode options
    private int _streamingMinBatchMs = 500;
    private int _streamingUpdateIntervalMs = 250;
    private int _streamingStabilityIterations = 3;
    private int _streamingStabilityTimeoutMs = 2000;
    private int _streamingFlickerCooldownMs = 100;

    /// <summary>
    /// Sets the OpenAI API key.
    /// </summary>
    public StreamingTranscriptionOptionsBuilder WithApiKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    /// <summary>
    /// Sets the transcription mode.
    /// </summary>
    public StreamingTranscriptionOptionsBuilder WithMode(TranscriptionMode mode)
    {
        _mode = mode;
        return this;
    }

    /// <summary>
    /// Sets the audio sample rate in Hz.
    /// </summary>
    public StreamingTranscriptionOptionsBuilder WithSampleRate(int sampleRate)
    {
        _sampleRate = sampleRate;
        return this;
    }

    /// <summary>
    /// Sets the language code for transcription.
    /// </summary>
    public StreamingTranscriptionOptionsBuilder WithLanguage(string? language)
    {
        _language = language;
        return this;
    }

    /// <summary>
    /// Sets the silence detection threshold (0.0-1.0). Set to 0 to disable.
    /// </summary>
    public StreamingTranscriptionOptionsBuilder WithSilenceThreshold(double threshold)
    {
        _silenceThreshold = threshold;
        return this;
    }

    /// <summary>
    /// Configures context prompting to improve transcription continuity.
    /// </summary>
    /// <param name="enabled">Whether to enable context prompting.</param>
    /// <param name="maxChars">Maximum characters of recent transcript to include.</param>
    /// <param name="vocabulary">Domain-specific vocabulary prompt.</param>
    public StreamingTranscriptionOptionsBuilder WithContextPrompting(
        bool enabled = true,
        int maxChars = 200,
        string? vocabulary = null)
    {
        _enableContextPrompting = enabled;
        _contextPromptMaxChars = maxChars;
        _vocabularyPrompt = vocabulary;
        return this;
    }

    /// <summary>
    /// Configures Basic mode options.
    /// </summary>
    /// <param name="batchMs">Minimum batch window in milliseconds.</param>
    /// <param name="maxBatchMs">Maximum batch window in milliseconds.</param>
    public StreamingTranscriptionOptionsBuilder WithBasicOptions(
        int batchMs = 3000,
        int maxBatchMs = 6000)
    {
        _basicBatchMs = batchMs;
        _basicMaxBatchMs = maxBatchMs;
        return this;
    }

    /// <summary>
    /// Configures Revision mode options.
    /// </summary>
    /// <param name="overlapMs">Overlap duration between batches.</param>
    /// <param name="batchMs">Batch duration for each window.</param>
    /// <param name="agreementCount">Number of confirmations required for stability.</param>
    /// <param name="similarityThreshold">Minimum similarity for text matching.</param>
    public StreamingTranscriptionOptionsBuilder WithRevisionOptions(
        int overlapMs = 1500,
        int batchMs = 2000,
        int agreementCount = 2,
        double similarityThreshold = 0.85)
    {
        _revisionOverlapMs = overlapMs;
        _revisionBatchMs = batchMs;
        _revisionAgreementCount = agreementCount;
        _revisionSimilarityThreshold = similarityThreshold;
        return this;
    }

    /// <summary>
    /// Configures Streaming mode options.
    /// </summary>
    /// <param name="minBatchMs">Minimum batch duration before transcription.</param>
    /// <param name="updateIntervalMs">Interval between hypothesis updates.</param>
    /// <param name="stabilityIterations">Unchanged iterations before stability.</param>
    /// <param name="stabilityTimeoutMs">Timeout before provisional becomes stable.</param>
    /// <param name="flickerCooldownMs">Cooldown to prevent rapid updates.</param>
    public StreamingTranscriptionOptionsBuilder WithStreamingOptions(
        int minBatchMs = 500,
        int updateIntervalMs = 250,
        int stabilityIterations = 3,
        int stabilityTimeoutMs = 2000,
        int flickerCooldownMs = 100)
    {
        _streamingMinBatchMs = minBatchMs;
        _streamingUpdateIntervalMs = updateIntervalMs;
        _streamingStabilityIterations = stabilityIterations;
        _streamingStabilityTimeoutMs = stabilityTimeoutMs;
        _streamingFlickerCooldownMs = flickerCooldownMs;
        return this;
    }

    /// <summary>
    /// Builds the StreamingTranscriptionOptions instance.
    /// </summary>
    /// <returns>Configured options.</returns>
    /// <exception cref="InvalidOperationException">If required fields are not set.</exception>
    public StreamingTranscriptionOptions Build()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("API key is required. Call WithApiKey() first.");

        return new StreamingTranscriptionOptions
        {
            ApiKey = _apiKey,
            Mode = _mode,
            SampleRate = _sampleRate,
            Language = _language,
            SilenceThreshold = _silenceThreshold,
            EnableContextPrompting = _enableContextPrompting,
            ContextPromptMaxChars = _contextPromptMaxChars,
            VocabularyPrompt = _vocabularyPrompt,
            Basic = new BasicModeOptions
            {
                BatchMs = _basicBatchMs,
                MaxBatchMs = _basicMaxBatchMs
            },
            Revision = new RevisionModeOptions
            {
                OverlapMs = _revisionOverlapMs,
                BatchMs = _revisionBatchMs,
                AgreementCount = _revisionAgreementCount,
                SimilarityThreshold = _revisionSimilarityThreshold
            },
            Streaming = new StreamingModeOptions
            {
                MinBatchMs = _streamingMinBatchMs,
                UpdateIntervalMs = _streamingUpdateIntervalMs,
                StabilityIterations = _streamingStabilityIterations,
                StabilityTimeoutMs = _streamingStabilityTimeoutMs,
                FlickerCooldownMs = _streamingFlickerCooldownMs
            }
        };
    }
}
