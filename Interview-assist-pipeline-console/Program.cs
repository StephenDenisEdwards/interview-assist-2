using InterviewAssist.Audio.Windows;
using InterviewAssist.Library.Audio;
using InterviewAssist.Pipeline;

// Get API key from environment
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Error: OPENAI_API_KEY environment variable not set");
    return 1;
}

// Parse command line for audio source
var source = AudioInputSource.Loopback;
if (args.Length > 0 && args[0].Equals("mic", StringComparison.OrdinalIgnoreCase))
{
    source = AudioInputSource.Microphone;
}

Console.WriteLine("=== Interview Pipeline Demo ===");
Console.WriteLine($"Audio source: {source}");
Console.WriteLine("Press Ctrl+C to stop");
Console.WriteLine();

// Create audio capture
var audio = new WindowsAudioCaptureService(24000, source);

// Create pipeline
await using var pipeline = new InterviewPipeline(
    audio,
    apiKey,
    sampleRate: 24000,
    transcriptionBatchMs: 3000,
    detectionModel: "gpt-4o-mini",
    detectionConfidence: 0.7);

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
