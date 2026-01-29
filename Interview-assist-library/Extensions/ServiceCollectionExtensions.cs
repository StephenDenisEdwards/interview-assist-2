using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Constants;
using InterviewAssist.Library.Health;
using InterviewAssist.Library.Pipeline;
using InterviewAssist.Library.Realtime;
using InterviewAssist.Library.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
            // Get optional question detection service - null if not registered
            var detector = sp.GetService<IQuestionDetectionService>();
            var logger = sp.GetService<ILogger<PipelineRealtimeApi>>();
            return new PipelineRealtimeApi(audioService, opts, detector, logger);
        });

        return services;
    }

    /// <summary>
    /// Adds Interview Assist health checks to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInterviewAssistHealthChecks(this IServiceCollection services)
    {
        services.AddSingleton<IHealthCheck, RealtimeApiHealthCheck>();
        services.AddSingleton<HealthCheckService>();
        return services;
    }

    /// <summary>
    /// Adds question detection services to the pipeline.
    /// If this method is not called, question detection will be disabled.
    /// Must be called BEFORE AddInterviewAssistPipeline() to take effect.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure detection options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// // Enable detection with defaults
    /// services.AddQuestionDetection();
    ///
    /// // Enable detection with custom settings
    /// services.AddQuestionDetection(options => options
    ///     .WithModel("gpt-4o-mini")
    ///     .WithConfidenceThreshold(0.8));
    /// </example>
    public static IServiceCollection AddQuestionDetection(
        this IServiceCollection services,
        Action<QuestionDetectionOptionsBuilder>? configureOptions = null)
    {
        var builder = new QuestionDetectionOptionsBuilder();
        configureOptions?.Invoke(builder);
        var options = builder.Build();

        services.AddSingleton(options);
        services.AddSingleton<IQuestionDetectionService>(sp =>
        {
            var opts = sp.GetRequiredService<QuestionDetectionOptions>();
            return new OpenAiQuestionDetectionService(
                opts.ApiKey,
                opts.Model,
                opts.ConfidenceThreshold);
        });

        return services;
    }

    /// <summary>
    /// Adds streaming transcription services with mode-based stability tracking.
    /// Requires IAudioCaptureService to be registered separately (platform-specific).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the StreamingTranscriptionOptions using a builder.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// services.AddStreamingTranscription(options => options
    ///     .WithApiKey("your-api-key")
    ///     .WithMode(TranscriptionMode.Revision)
    ///     .WithContextPrompting(true, maxChars: 200, vocabulary: "C#, async"));
    /// </example>
    public static IServiceCollection AddStreamingTranscription(
        this IServiceCollection services,
        Action<StreamingTranscriptionOptionsBuilder> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        var builder = new StreamingTranscriptionOptionsBuilder();
        configureOptions(builder);
        var options = builder.Build();

        services.AddSingleton(options);
        services.TryAddSingleton<IStreamingTranscriptionService>(sp =>
        {
            var audioService = sp.GetRequiredService<IAudioCaptureService>();
            var opts = sp.GetRequiredService<StreamingTranscriptionOptions>();

            return opts.Mode switch
            {
                TranscriptionMode.Basic => new BasicTranscriptionService(audioService, opts),
                TranscriptionMode.Revision => new RevisionTranscriptionService(audioService, opts),
                TranscriptionMode.Streaming => new StreamingHypothesisService(audioService, opts),
                _ => throw new InvalidOperationException($"Unknown transcription mode: {opts.Mode}")
            };
        });

        return services;
    }
}

/// <summary>
/// Configuration options for question detection service.
/// </summary>
public record QuestionDetectionOptions
{
    /// <summary>
    /// OpenAI API key for detection API calls.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Model to use for question detection. Default: gpt-4o-mini.
    /// </summary>
    public string Model { get; init; } = "gpt-4o-mini";

    /// <summary>
    /// Minimum confidence threshold for question detection (0.0-1.0). Default: 0.7.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.7;
}

/// <summary>
/// Builder class for constructing QuestionDetectionOptions with a fluent API.
/// </summary>
public class QuestionDetectionOptionsBuilder
{
    private string _apiKey = string.Empty;
    private string _model = "gpt-4o-mini";
    private double _confidenceThreshold = 0.7;

    public QuestionDetectionOptionsBuilder WithApiKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    public QuestionDetectionOptionsBuilder WithModel(string model)
    {
        _model = model;
        return this;
    }

    public QuestionDetectionOptionsBuilder WithConfidenceThreshold(double threshold)
    {
        _confidenceThreshold = threshold;
        return this;
    }

    internal QuestionDetectionOptions Build()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("API key is required. Call WithApiKey() first.");

        return new QuestionDetectionOptions
        {
            ApiKey = _apiKey,
            Model = _model,
            ConfidenceThreshold = _confidenceThreshold
        };
    }
}

/// <summary>
/// Builder class for constructing RealtimeApiOptions with a fluent API.
/// </summary>
public class RealtimeApiOptionsBuilder
{
    private string _apiKey = string.Empty;
    private string _realtimeModel = ModelConstants.DefaultRealtimeModel;
    private string _transcriptionModel = ModelConstants.DefaultTranscriptionModel;
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
    private int _webSocketConnectTimeoutMs = 30000;
    private int _webSocketKeepAliveIntervalMs = 30000;
    private int _httpRequestTimeoutMs = 60000;

    public RealtimeApiOptionsBuilder WithApiKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    public RealtimeApiOptionsBuilder WithRealtimeModel(string model)
    {
        _realtimeModel = model;
        return this;
    }

    public RealtimeApiOptionsBuilder WithTranscriptionModel(string model)
    {
        _transcriptionModel = model;
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

    public RealtimeApiOptionsBuilder WithWebSocketConnectTimeout(int timeoutMs)
    {
        _webSocketConnectTimeoutMs = timeoutMs;
        return this;
    }

    public RealtimeApiOptionsBuilder WithWebSocketKeepAliveInterval(int intervalMs)
    {
        _webSocketKeepAliveIntervalMs = intervalMs;
        return this;
    }

    public RealtimeApiOptionsBuilder WithHttpRequestTimeout(int timeoutMs)
    {
        _httpRequestTimeoutMs = timeoutMs;
        return this;
    }

    public RealtimeApiOptionsBuilder WithTimeouts(int connectTimeoutMs = 30000, int keepAliveIntervalMs = 30000, int httpTimeoutMs = 60000)
    {
        _webSocketConnectTimeoutMs = connectTimeoutMs;
        _webSocketKeepAliveIntervalMs = keepAliveIntervalMs;
        _httpRequestTimeoutMs = httpTimeoutMs;
        return this;
    }

    internal RealtimeApiOptions Build()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("API key is required. Call WithApiKey() first.");

        return new RealtimeApiOptions
        {
            ApiKey = _apiKey,
            RealtimeModel = _realtimeModel,
            TranscriptionModel = _transcriptionModel,
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
            MaxReconnectDelayMs = _maxReconnectDelayMs,
            WebSocketConnectTimeoutMs = _webSocketConnectTimeoutMs,
            WebSocketKeepAliveIntervalMs = _webSocketKeepAliveIntervalMs,
            HttpRequestTimeoutMs = _httpRequestTimeoutMs
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
