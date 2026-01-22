using InterviewAssist.Library.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace InterviewAssist.Library.Extensions;

/// <summary>
/// Extension methods for configuring structured logging with Serilog.
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Default console output template with correlation ID support.
    /// </summary>
    public const string DefaultConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Default file output template with full timestamp.
    /// </summary>
    public const string DefaultFileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Adds Interview Assist structured logging using Serilog.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to customize the Serilog configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInterviewAssistLogging(
        this IServiceCollection services,
        Action<LoggingOptions>? configure = null)
    {
        var options = new LoggingOptions();
        configure?.Invoke(options);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(options.MinimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "InterviewAssist");

        // Add correlation ID enricher
        loggerConfig.Enrich.With(new CorrelationIdEnricher());

        if (options.EnableConsole)
        {
            loggerConfig.WriteTo.Console(
                outputTemplate: options.ConsoleTemplate ?? DefaultConsoleTemplate,
                restrictedToMinimumLevel: options.ConsoleMinimumLevel ?? options.MinimumLevel);
        }

        if (options.EnableFile && !string.IsNullOrEmpty(options.FilePath))
        {
            loggerConfig.WriteTo.File(
                path: options.FilePath,
                outputTemplate: options.FileTemplate ?? DefaultFileTemplate,
                restrictedToMinimumLevel: options.FileMinimumLevel ?? options.MinimumLevel,
                rollingInterval: options.RollingInterval,
                retainedFileCountLimit: options.RetainedFileCountLimit,
                fileSizeLimitBytes: options.FileSizeLimitBytes);
        }

        var logger = loggerConfig.CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });

        return services;
    }

    /// <summary>
    /// Creates a Serilog logger with default Interview Assist configuration.
    /// Useful for bootstrapping before DI container is built.
    /// </summary>
    /// <param name="configure">Optional action to customize the configuration.</param>
    /// <returns>The configured Serilog logger.</returns>
    public static Serilog.ILogger CreateBootstrapLogger(Action<LoggingOptions>? configure = null)
    {
        var options = new LoggingOptions();
        configure?.Invoke(options);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(options.MinimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "InterviewAssist")
            .Enrich.With(new CorrelationIdEnricher());

        if (options.EnableConsole)
        {
            loggerConfig.WriteTo.Console(
                outputTemplate: options.ConsoleTemplate ?? DefaultConsoleTemplate,
                restrictedToMinimumLevel: options.ConsoleMinimumLevel ?? options.MinimumLevel);
        }

        return loggerConfig.CreateLogger();
    }
}

/// <summary>
/// Options for configuring Interview Assist logging.
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// Minimum log level. Default: Information.
    /// </summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    /// <summary>
    /// Enable console logging. Default: true.
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// Custom console output template. Uses default if null.
    /// </summary>
    public string? ConsoleTemplate { get; set; }

    /// <summary>
    /// Minimum level for console output. Uses MinimumLevel if null.
    /// </summary>
    public LogEventLevel? ConsoleMinimumLevel { get; set; }

    /// <summary>
    /// Enable file logging. Default: false.
    /// </summary>
    public bool EnableFile { get; set; }

    /// <summary>
    /// File path for file logging.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Custom file output template. Uses default if null.
    /// </summary>
    public string? FileTemplate { get; set; }

    /// <summary>
    /// Minimum level for file output. Uses MinimumLevel if null.
    /// </summary>
    public LogEventLevel? FileMinimumLevel { get; set; }

    /// <summary>
    /// Rolling interval for file logs. Default: Day.
    /// </summary>
    public RollingInterval RollingInterval { get; set; } = RollingInterval.Day;

    /// <summary>
    /// Number of log files to retain. Default: 7.
    /// </summary>
    public int RetainedFileCountLimit { get; set; } = 7;

    /// <summary>
    /// Maximum file size in bytes before rolling. Default: 10MB.
    /// </summary>
    public long? FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Serilog enricher that adds correlation ID to log events.
/// </summary>
internal class CorrelationIdEnricher : Serilog.Core.ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, Serilog.Core.ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = CorrelationContext.Current ?? "--------";
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));
    }
}
