using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Pipeline;
using InterviewAssist.Library.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InterviewAssist.Library.Extensions;

/// <summary>
/// Extension methods for registering Interview Assist services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the core Interview Assist services using the OpenAI Realtime API.
    /// Requires IAudioCaptureService to be registered separately (platform-specific).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the RealtimeApiOptions using a builder.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// services.AddInterviewAssistCore(options => options
    ///     .WithApiKey("your-api-key")
    ///     .WithVoice("alloy")
    ///     .WithRateLimitRecovery(enabled: true));
    /// </example>
    public static IServiceCollection AddInterviewAssistCore(
        this IServiceCollection services,
        Action<RealtimeApiOptionsBuilder> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        var builder = new RealtimeApiOptionsBuilder();
        configureOptions(builder);
        var options = builder.Build();

        services.AddSingleton(options);
        services.TryAddSingleton<IRealtimeApi>(sp =>
        {
            var audioService = sp.GetRequiredService<IAudioCaptureService>();
            var opts = sp.GetRequiredService<RealtimeApiOptions>();
            return new OpenAiRealtimeApi(audioService, opts);
        });

        return services;
    }

    /// <summary>
    /// Adds the pipeline-based Interview Assist services using STT + GPT-4.
    /// Requires IAudioCaptureService to be registered separately (platform-specific).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the PipelineApiOptions using a builder.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// services.AddInterviewAssistPipeline(options => options
    ///     .WithApiKey("your-api-key")
    ///     .WithResponseModel("gpt-4o")
    ///     .WithTemperature(0.3));
    /// </example>
    public static IServiceCollection AddInterviewAssistPipeline(
        this IServiceCollection services,
        Action<PipelineApiOptionsBuilder> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        var builder = new PipelineApiOptionsBuilder();
        configureOptions(builder);
        var options = builder.Build();

        services.AddSingleton(options);
        services.TryAddSingleton<IRealtimeApi>(sp =>
        {
            var audioService = sp.GetRequiredService<IAudioCaptureService>();
            var opts = sp.GetRequiredService<PipelineApiOptions>();
            return new PipelineRealtimeApi(audioService, opts);
        });

        return services;
    }
}

/// <summary>
/// Builder class for constructing RealtimeApiOptions with a fluent API.
/// </summary>
public class RealtimeApiOptionsBuilder
{
    private string _apiKey = string.Empty;
    private string? _extraInstructions;
    private string? _systemInstructions;
    private string? _systemInstructionsFilePath;
    private Func<string>? _systemInstructionsFactory;
    private IReadOnlyList<Context.ContextChunk>? _contextChunks;
    private string _voice = "alloy";
    private double _vadThreshold = 0.5;
    private int _silenceDurationMs = 500;
    private int _prefixPaddingMs = 300;
    private int _maxInstructionChars = 4000;
    private bool _enableReconnection = true;
    private int _maxReconnectAttempts = 5;
    private int _reconnectBaseDelayMs = 1000;
    private bool _enableRateLimitRecovery = true;
    private int _rateLimitRecoveryDelayMs = 60000;
    private int _maxReconnectDelayMs = 30000;

    public RealtimeApiOptionsBuilder WithApiKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    public RealtimeApiOptionsBuilder WithExtraInstructions(string? extraInstructions)
    {
        _extraInstructions = extraInstructions;
        return this;
    }

    public RealtimeApiOptionsBuilder WithSystemInstructions(string? systemInstructions)
    {
        _systemInstructions = systemInstructions;
        return this;
    }

    public RealtimeApiOptionsBuilder WithSystemInstructionsFilePath(string? path)
    {
        _systemInstructionsFilePath = path;
        return this;
    }

    public RealtimeApiOptionsBuilder WithSystemInstructionsFactory(Func<string>? factory)
    {
        _systemInstructionsFactory = factory;
        return this;
    }

    public RealtimeApiOptionsBuilder WithContextChunks(IReadOnlyList<Context.ContextChunk>? chunks)
    {
        _contextChunks = chunks;
        return this;
    }

    public RealtimeApiOptionsBuilder WithVoice(string voice)
    {
        _voice = voice;
        return this;
    }

    public RealtimeApiOptionsBuilder WithVadThreshold(double threshold)
    {
        _vadThreshold = threshold;
        return this;
    }

    public RealtimeApiOptionsBuilder WithSilenceDurationMs(int ms)
    {
        _silenceDurationMs = ms;
        return this;
    }

    public RealtimeApiOptionsBuilder WithPrefixPaddingMs(int ms)
    {
        _prefixPaddingMs = ms;
        return this;
    }

    public RealtimeApiOptionsBuilder WithMaxInstructionChars(int chars)
    {
        _maxInstructionChars = chars;
        return this;
    }

    public RealtimeApiOptionsBuilder WithReconnection(bool enabled, int maxAttempts = 5, int baseDelayMs = 1000)
    {
        _enableReconnection = enabled;
        _maxReconnectAttempts = maxAttempts;
        _reconnectBaseDelayMs = baseDelayMs;
        return this;
    }

    public RealtimeApiOptionsBuilder WithRateLimitRecovery(bool enabled, int recoveryDelayMs = 60000, int maxDelayMs = 30000)
    {
        _enableRateLimitRecovery = enabled;
        _rateLimitRecoveryDelayMs = recoveryDelayMs;
        _maxReconnectDelayMs = maxDelayMs;
        return this;
    }

    internal RealtimeApiOptions Build()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("API key is required. Call WithApiKey() first.");

        return new RealtimeApiOptions
        {
            ApiKey = _apiKey,
            ExtraInstructions = _extraInstructions,
            SystemInstructions = _systemInstructions,
            SystemInstructionsFilePath = _systemInstructionsFilePath,
            SystemInstructionsFactory = _systemInstructionsFactory,
            ContextChunks = _contextChunks,
            Voice = _voice,
            VadThreshold = _vadThreshold,
            SilenceDurationMs = _silenceDurationMs,
            PrefixPaddingMs = _prefixPaddingMs,
            MaxInstructionChars = _maxInstructionChars,
            EnableReconnection = _enableReconnection,
            MaxReconnectAttempts = _maxReconnectAttempts,
            ReconnectBaseDelayMs = _reconnectBaseDelayMs,
            EnableRateLimitRecovery = _enableRateLimitRecovery,
            RateLimitRecoveryDelayMs = _rateLimitRecoveryDelayMs,
            MaxReconnectDelayMs = _maxReconnectDelayMs
        };
    }
}

/// <summary>
/// Builder class for constructing PipelineApiOptions with a fluent API.
/// </summary>
public class PipelineApiOptionsBuilder
{
    private string _apiKey = string.Empty;
    private int _transcriptionBatchMs = 3000;
    private int _sampleRate = 24000;
    private string _detectionModel = "gpt-4o-mini";
    private double _detectionConfidenceThreshold = 0.7;
    private int _detectionIntervalMs = 1500;
    private int _transcriptBufferSeconds = 30;
    private string _responseModel = "gpt-4o";
    private int _maxResponseTokens = 2048;
    private double _temperature = 0.3;
    private string? _extraInstructions;
    private IReadOnlyList<Context.ContextChunk>? _contextChunks;
    private string? _systemInstructions;
    private int _maxQueuedQuestions = 5;
    private bool _allowParallelResponses = false;

    public PipelineApiOptionsBuilder WithApiKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    public PipelineApiOptionsBuilder WithTranscriptionBatchMs(int ms)
    {
        _transcriptionBatchMs = ms;
        return this;
    }

    public PipelineApiOptionsBuilder WithSampleRate(int rate)
    {
        _sampleRate = rate;
        return this;
    }

    public PipelineApiOptionsBuilder WithDetectionModel(string model)
    {
        _detectionModel = model;
        return this;
    }

    public PipelineApiOptionsBuilder WithDetectionConfidenceThreshold(double threshold)
    {
        _detectionConfidenceThreshold = threshold;
        return this;
    }

    public PipelineApiOptionsBuilder WithDetectionIntervalMs(int ms)
    {
        _detectionIntervalMs = ms;
        return this;
    }

    public PipelineApiOptionsBuilder WithTranscriptBufferSeconds(int seconds)
    {
        _transcriptBufferSeconds = seconds;
        return this;
    }

    public PipelineApiOptionsBuilder WithResponseModel(string model)
    {
        _responseModel = model;
        return this;
    }

    public PipelineApiOptionsBuilder WithMaxResponseTokens(int tokens)
    {
        _maxResponseTokens = tokens;
        return this;
    }

    public PipelineApiOptionsBuilder WithTemperature(double temperature)
    {
        _temperature = temperature;
        return this;
    }

    public PipelineApiOptionsBuilder WithExtraInstructions(string? instructions)
    {
        _extraInstructions = instructions;
        return this;
    }

    public PipelineApiOptionsBuilder WithContextChunks(IReadOnlyList<Context.ContextChunk>? chunks)
    {
        _contextChunks = chunks;
        return this;
    }

    public PipelineApiOptionsBuilder WithSystemInstructions(string? instructions)
    {
        _systemInstructions = instructions;
        return this;
    }

    public PipelineApiOptionsBuilder WithQueueSettings(int maxQueued = 5, bool allowParallel = false)
    {
        _maxQueuedQuestions = maxQueued;
        _allowParallelResponses = allowParallel;
        return this;
    }

    internal PipelineApiOptions Build()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("API key is required. Call WithApiKey() first.");

        return new PipelineApiOptions
        {
            ApiKey = _apiKey,
            TranscriptionBatchMs = _transcriptionBatchMs,
            SampleRate = _sampleRate,
            DetectionModel = _detectionModel,
            DetectionConfidenceThreshold = _detectionConfidenceThreshold,
            DetectionIntervalMs = _detectionIntervalMs,
            TranscriptBufferSeconds = _transcriptBufferSeconds,
            ResponseModel = _responseModel,
            MaxResponseTokens = _maxResponseTokens,
            Temperature = _temperature,
            ExtraInstructions = _extraInstructions,
            ContextChunks = _contextChunks,
            SystemInstructions = _systemInstructions,
            MaxQueuedQuestions = _maxQueuedQuestions,
            AllowParallelResponses = _allowParallelResponses
        };
    }
}
