using InterviewAssist.Audio.Windows;
using InterviewAssist.Library.Audio;
using InterviewAssist.Pipeline;
using Microsoft.Extensions.Configuration;

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>(optional: true)
    .Build();

// Get API key from configuration or environment
var apiKey = configuration["OpenAI:ApiKey"]
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Error: OpenAI API key not configured.");
    Console.WriteLine("Set OPENAI_API_KEY environment variable or add OpenAI:ApiKey to appsettings.json/user secrets.");
    return 1;
}

// Load pipeline settings
var pipelineConfig = configuration.GetSection("InterviewPipeline");

// Parse audio source (command line overrides config)
var audioSourceStr = pipelineConfig["AudioSource"] ?? "Loopback";
if (args.Length > 0)
{
    audioSourceStr = args[0];
}

var source = audioSourceStr.Equals("mic", StringComparison.OrdinalIgnoreCase)
    || audioSourceStr.Equals("Microphone", StringComparison.OrdinalIgnoreCase)
    ? AudioInputSource.Microphone
    : AudioInputSource.Loopback;

// Parse other settings with defaults
var sampleRate = pipelineConfig.GetValue("SampleRate", 24000);
var transcriptionBatchMs = pipelineConfig.GetValue("TranscriptionBatchMs", 3000);

// Load question detection settings
var detectionConfig = configuration.GetSection("QuestionDetection");
var detectionEnabled = detectionConfig.GetValue("Enabled", true);
var detectionModel = detectionConfig["Model"] ?? "gpt-4o-mini";
var detectionConfidence = detectionConfig.GetValue("ConfidenceThreshold", 0.7);

Console.WriteLine("=== Interview Pipeline Demo ===");
Console.WriteLine($"Audio source: {source}");
Console.WriteLine($"Sample rate: {sampleRate} Hz");
Console.WriteLine($"Question detection: {(detectionEnabled ? "enabled" : "disabled")}");
if (detectionEnabled)
{
    Console.WriteLine($"Detection model: {detectionModel}");
    Console.WriteLine($"Detection confidence: {detectionConfidence:P0}");
}
Console.WriteLine("Press Ctrl+C to stop");
Console.WriteLine();

// Create audio capture
var audio = new WindowsAudioCaptureService(sampleRate, source);

// Create question detector only if enabled
QuestionDetector? detector = null;
if (detectionEnabled)
{
    detector = new QuestionDetector(apiKey, detectionModel, detectionConfidence);
}

// Create pipeline with optional detector
await using var pipeline = new InterviewPipeline(
    audio,
    apiKey,
    sampleRate: sampleRate,
    transcriptionBatchMs: transcriptionBatchMs,
    questionDetector: detector);

// Wire up events
pipeline.OnInfo += msg =>
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"[Info] {msg}");
    Console.ResetColor();
};

pipeline.OnError += err =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[Error] {err}");
    Console.ResetColor();
};

pipeline.OnTranscript += text =>
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[Transcript] {text}");
    Console.ResetColor();
};

pipeline.OnQuestionDetected += q =>
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[Question Detected] Type: {q.Type}, Confidence: {q.Confidence:P0}");
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"  → {q.Text}");
    Console.ResetColor();
    Console.WriteLine();
};

// Handle Ctrl+C
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Start and wait
pipeline.Start(cts.Token);

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Expected
}

await pipeline.StopAsync();
Console.WriteLine("Done.");
return 0;

// Marker class for user secrets
public partial class Program { }
