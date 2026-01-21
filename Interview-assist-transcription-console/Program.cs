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

// Parse command line arguments
var source = AudioInputSource.Loopback;
var batchMs = 5000;
var includeWords = false;
string? language = null;

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
        case "--words":
        case "-w":
            includeWords = true;
            break;
        case "--language":
        case "--lang":
            if (i + 1 < args.Length)
                language = args[++i];
            break;
        case "--help":
        case "-h":
            PrintUsage();
            return 0;
    }
}

Console.WriteLine("=== Timestamped Transcription Demo ===");
Console.WriteLine($"Audio source: {source}");
Console.WriteLine($"Batch interval: {batchMs}ms");
Console.WriteLine($"Word timestamps: {(includeWords ? "enabled" : "disabled")}");
Console.WriteLine($"Language: {language ?? "auto-detect"}");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop");
Console.WriteLine(new string('-', 50));
Console.WriteLine();

// Create audio capture at 16kHz (Whisper's native sample rate)
const int sampleRate = 16000;
var audio = new WindowsAudioCaptureService(sampleRate, source);

// Create transcription options
var options = new TimestampedTranscriptionOptions
{
    SampleRate = sampleRate,
    BatchMs = batchMs,
    IncludeWordTimestamps = includeWords,
    Language = language
};

// Create transcription service
await using var transcription = new TimestampedTranscriptionService(audio, apiKey, options);

// Wire up events
transcription.OnTranscriptionResult += result =>
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Transcription (latency: {result.LatencyMs}ms, duration: {result.AudioDurationSeconds:F1}s)");
    Console.ResetColor();

    foreach (var segment in result.Segments)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"  [{FormatTime(segment.StreamOffsetSeconds)} - {FormatTime(segment.StreamOffsetSeconds + segment.DurationSeconds)}] ");
        Console.ResetColor();
        Console.WriteLine(segment.Text);

        if (includeWords && segment.Words != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            foreach (var word in segment.Words)
            {
                Console.Write($"    {word.Word} [{FormatTime(word.StartSeconds)}-{FormatTime(word.EndSeconds)}]");
            }
            Console.WriteLine();
            Console.ResetColor();
        }
    }
    Console.WriteLine();
};

transcription.OnSegment += segment =>
{
    // Individual segment callback - useful for real-time caption display
    // Already handled in OnTranscriptionResult for this demo
};

transcription.OnError += error =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[Error] {error}");
    Console.ResetColor();
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
Console.WriteLine("Done.");
return 0;

static string FormatTime(double seconds)
{
    var ts = TimeSpan.FromSeconds(seconds);
    return ts.TotalHours >= 1
        ? ts.ToString(@"h\:mm\:ss\.f")
        : ts.ToString(@"m\:ss\.f");
}

static void PrintUsage()
{
    Console.WriteLine("Timestamped Transcription Demo");
    Console.WriteLine();
    Console.WriteLine("Usage: Interview-assist-transcription-console [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --mic, -m          Use microphone input (default: loopback)");
    Console.WriteLine("  --loopback, -l     Use system audio loopback");
    Console.WriteLine("  --batch, -b <ms>   Batch interval in milliseconds (default: 5000)");
    Console.WriteLine("  --words, -w        Include word-level timestamps");
    Console.WriteLine("  --lang <code>      Language code (e.g., en, es) for transcription");
    Console.WriteLine("  --help, -h         Show this help message");
    Console.WriteLine();
    Console.WriteLine("Environment:");
    Console.WriteLine("  OPENAI_API_KEY     OpenAI API key (required)");
}

// Marker class for user secrets
public partial class Program { }
