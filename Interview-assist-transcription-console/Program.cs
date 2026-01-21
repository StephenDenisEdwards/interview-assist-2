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

// Load settings from config (command line args override)
var transcriptionConfig = configuration.GetSection("Transcription");

var audioSourceStr = transcriptionConfig["AudioSource"] ?? "Loopback";
var source = audioSourceStr.Equals("mic", StringComparison.OrdinalIgnoreCase)
    || audioSourceStr.Equals("Microphone", StringComparison.OrdinalIgnoreCase)
    ? AudioInputSource.Microphone
    : AudioInputSource.Loopback;

var sampleRate = transcriptionConfig.GetValue("SampleRate", 16000);
var batchMs = transcriptionConfig.GetValue("BatchMs", 1500);
var language = transcriptionConfig["Language"];

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
        case "--help":
        case "-h":
            PrintUsage();
            return 0;
    }
}

Console.WriteLine("=== Real-time Transcription ===");
Console.WriteLine($"Audio: {source} | Rate: {sampleRate}Hz | Batch: {batchMs}ms | Lang: {language ?? "auto"}");
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

// Wire up events - streaming display
transcription.OnTranscriptionResult += result =>
{
    // Just output the text continuously
    foreach (var segment in result.Segments)
    {
        Console.Write(segment.Text);
        Console.Write(" ");
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
Console.WriteLine();
Console.WriteLine("Done.");
return 0;

static void PrintUsage()
{
    Console.WriteLine("Real-time Transcription Demo");
    Console.WriteLine();
    Console.WriteLine("Usage: Interview-assist-transcription-console [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --mic, -m          Use microphone input (default: loopback)");
    Console.WriteLine("  --loopback, -l     Use system audio loopback");
    Console.WriteLine("  --batch, -b <ms>   Batch interval in ms (default: 1500, lower = faster)");
    Console.WriteLine("  --lang <code>      Language code (e.g., en, es) for transcription");
    Console.WriteLine("  --help, -h         Show this help message");
    Console.WriteLine();
    Console.WriteLine("Environment:");
    Console.WriteLine("  OPENAI_API_KEY     OpenAI API key (required)");
}

// Marker class for user secrets
public partial class Program { }
