using InterviewAssist.Audio.Windows;
using InterviewAssist.Library.Audio;
using InterviewAssist.Pipeline;
using InterviewAssist.TranscriptionConsole;
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

// Load transcription settings from config
var transcriptionConfig = configuration.GetSection("Transcription");

var audioSourceStr = transcriptionConfig["AudioSource"] ?? "Loopback";
var source = audioSourceStr.Equals("mic", StringComparison.OrdinalIgnoreCase)
    || audioSourceStr.Equals("Microphone", StringComparison.OrdinalIgnoreCase)
    ? AudioInputSource.Microphone
    : AudioInputSource.Loopback;

var sampleRate = transcriptionConfig.GetValue("SampleRate", 16000);
var batchMs = transcriptionConfig.GetValue("BatchMs", 1500);
var language = transcriptionConfig["Language"];

// Load question detection settings from config
var detectionConfig = configuration.GetSection("QuestionDetection");
var detectionMethodStr = detectionConfig["Method"] ?? "Heuristic";
var detectionMethod = detectionMethodStr.Equals("llm", StringComparison.OrdinalIgnoreCase)
    ? QuestionDetectionMethod.Llm
    : QuestionDetectionMethod.Heuristic;
var detectionModel = detectionConfig["Model"] ?? "gpt-4o-mini";
var confidenceThreshold = detectionConfig.GetValue("ConfidenceThreshold", 0.7);
var detectionIntervalMs = detectionConfig.GetValue("DetectionIntervalMs", 2000);
var minBufferLength = detectionConfig.GetValue("MinBufferLength", 50);
var deduplicationWindowMs = detectionConfig.GetValue("DeduplicationWindowMs", 30000);
var enableTechnicalTermCorrection = detectionConfig.GetValue("EnableTechnicalTermCorrection", true);
var enableNoiseFilter = detectionConfig.GetValue("EnableNoiseFilter", true);

// Parse command line arguments (override config)
for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--mic":
        case "-m":
            source = AudioInputSource.Microphone;
            break;
        case "--loopback":
        case "-l":
            source = AudioInputSource.Loopback;
            break;
        case "--batch":
        case "-b":
            if (i + 1 < args.Length && int.TryParse(args[++i], out var b))
                batchMs = b;
            break;
        case "--language":
        case "--lang":
            if (i + 1 < args.Length)
                language = args[++i];
            break;
        case "--detection":
        case "-d":
            if (i + 1 < args.Length)
            {
                var methodArg = args[++i].ToLowerInvariant();
                detectionMethod = methodArg switch
                {
                    "llm" or "gpt" or "ai" => QuestionDetectionMethod.Llm,
                    _ => QuestionDetectionMethod.Heuristic
                };
            }
            break;
        case "--detection-model":
            if (i + 1 < args.Length)
                detectionModel = args[++i];
            break;
        case "--help":
        case "-h":
            PrintUsage();
            return 0;
    }
}

Console.WriteLine("=== Real-time Transcription ===");
Console.WriteLine($"Audio: {source} | Rate: {sampleRate}Hz | Batch: {batchMs}ms | Lang: {language ?? "auto"}");
Console.WriteLine($"Question Detection: {detectionMethod}" + (detectionMethod == QuestionDetectionMethod.Llm ? $" ({detectionModel})" : ""));
Console.WriteLine("Press Ctrl+C to stop");
Console.WriteLine(new string('-', 60));
Console.WriteLine();

// Create audio capture
var audio = new WindowsAudioCaptureService(sampleRate, source);

// Create transcription options
var options = new TimestampedTranscriptionOptions
{
    SampleRate = sampleRate,
    BatchMs = batchMs,
    Language = language
};

// Create transcription service
await using var transcription = new TimestampedTranscriptionService(audio, apiKey, options);

// Create question detector based on configuration
IQuestionDetector questionDetector = detectionMethod switch
{
    QuestionDetectionMethod.Llm => new LlmQuestionDetector(
        apiKey,
        detectionModel,
        confidenceThreshold,
        detectionIntervalMs,
        minBufferLength,
        deduplicationWindowMs,
        enableTechnicalTermCorrection,
        enableNoiseFilter),
    _ => new HeuristicQuestionDetector()
};

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"Using {questionDetector.Name} question detection");
Console.ResetColor();
Console.WriteLine();

// Wire up events - streaming display with question detection
transcription.OnTranscriptionResult += async result =>
{
    foreach (var segment in result.Segments)
    {
        var text = segment.Text.Trim();

        // Always print transcript text first
        Console.Write(text);
        Console.Write(" ");

        // Add to detector buffer
        questionDetector.AddText(text);

        // Check for questions
        var detected = await questionDetector.DetectQuestionsAsync();

        // Display detected questions (separate from transcript flow)
        if (detected.Count > 0)
        {
            foreach (var question in detected)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{question.Type}");
                if (detectionMethod == QuestionDetectionMethod.Llm)
                {
                    Console.Write($" {question.Confidence:P0}");
                }
                Console.Write("] ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(question.Text);
                Console.ResetColor();
            }
        }
    }
};

transcription.OnSegment += segment =>
{
    // Individual segment callback - not used in streaming mode
};

transcription.OnError += error =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[Error] {error}");
    Console.ResetColor();
};

// Wire up speech pause signal for two-phase question detection
transcription.OnSpeechPause += async () =>
{
    questionDetector.SignalSpeechPause();

    // Check for questions now since no OnTranscriptionResult will fire during silence
    var detected = await questionDetector.DetectQuestionsAsync();
    if (detected.Count > 0)
    {
        foreach (var question in detected)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"[{question.Type}");
            if (detectionMethod == QuestionDetectionMethod.Llm)
            {
                Console.Write($" {question.Confidence:P0}");
            }
            Console.Write("] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(question.Text);
            Console.ResetColor();
        }
    }
};

// Handle Ctrl+C
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("Stopping...");
    cts.Cancel();
};

// Start transcription
transcription.Start(cts.Token);

Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("Listening for audio...");
Console.ResetColor();
Console.WriteLine();

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Expected on shutdown
}

await transcription.StopAsync();

// Cleanup
if (questionDetector is IDisposable disposable)
{
    disposable.Dispose();
}

Console.WriteLine();
Console.WriteLine("Done.");
return 0;

static void PrintUsage()
{
    Console.WriteLine("Real-time Transcription Demo");
    Console.WriteLine();
    Console.WriteLine("Usage: Interview-assist-transcription-console [options]");
    Console.WriteLine();
    Console.WriteLine("Audio Options:");
    Console.WriteLine("  --mic, -m              Use microphone input (default: loopback)");
    Console.WriteLine("  --loopback, -l         Use system audio loopback");
    Console.WriteLine("  --batch, -b <ms>       Batch interval in ms (default: 1500, lower = faster)");
    Console.WriteLine("  --lang <code>          Language code (e.g., en, es) for transcription");
    Console.WriteLine();
    Console.WriteLine("Question Detection Options:");
    Console.WriteLine("  --detection, -d <method>  Detection method: heuristic (default) or llm");
    Console.WriteLine("  --detection-model <model> LLM model (default: gpt-4o-mini)");
    Console.WriteLine();
    Console.WriteLine("Other:");
    Console.WriteLine("  --help, -h             Show this help message");
    Console.WriteLine();
    Console.WriteLine("Environment:");
    Console.WriteLine("  OPENAI_API_KEY         OpenAI API key (required)");
    Console.WriteLine();
    Console.WriteLine("Configuration:");
    Console.WriteLine("  Settings can also be configured in appsettings.json:");
    Console.WriteLine("  {");
    Console.WriteLine("    \"QuestionDetection\": {");
    Console.WriteLine("      \"Method\": \"Llm\",        // or \"Heuristic\"");
    Console.WriteLine("      \"Model\": \"gpt-4o-mini\",");
    Console.WriteLine("      \"ConfidenceThreshold\": 0.7,");
    Console.WriteLine("      \"DetectionIntervalMs\": 2000,");
    Console.WriteLine("      \"MinBufferLength\": 50,");
    Console.WriteLine("      \"DeduplicationWindowMs\": 30000,");
    Console.WriteLine("      \"EnableTechnicalTermCorrection\": true,");
    Console.WriteLine("      \"EnableNoiseFilter\": true");
    Console.WriteLine("    }");
    Console.WriteLine("  }");
}

// Marker class for user secrets
public partial class Program { }
