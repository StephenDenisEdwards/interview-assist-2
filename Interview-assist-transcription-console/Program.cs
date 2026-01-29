using InterviewAssist.Audio.Windows;
using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Transcription;
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

// Load streaming transcription settings
var useStreaming = transcriptionConfig.GetValue("UseStreaming", false);
var streamingModeStr = transcriptionConfig["Mode"] ?? "Basic";
var streamingMode = streamingModeStr.ToLowerInvariant() switch
{
    "revision" => TranscriptionMode.Revision,
    "streaming" => TranscriptionMode.Streaming,
    _ => TranscriptionMode.Basic
};
var vocabularyPrompt = transcriptionConfig["VocabularyPrompt"];

// Load question detection settings from config
var detectionConfig = configuration.GetSection("QuestionDetection");
var detectionEnabled = detectionConfig.GetValue("Enabled", true);
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
        case "--streaming":
        case "-s":
            useStreaming = true;
            break;
        case "--mode":
            if (i + 1 < args.Length)
            {
                var modeArg = args[++i].ToLowerInvariant();
                streamingMode = modeArg switch
                {
                    "revision" => TranscriptionMode.Revision,
                    "streaming" => TranscriptionMode.Streaming,
                    _ => TranscriptionMode.Basic
                };
                useStreaming = true; // Implies streaming mode
            }
            break;
        case "--vocabulary":
        case "--vocab":
            if (i + 1 < args.Length)
                vocabularyPrompt = args[++i];
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
if (useStreaming)
{
    Console.WriteLine($"Mode: Streaming ({streamingMode}) | Audio: {source} | Rate: {sampleRate}Hz | Lang: {language ?? "auto"}");
}
else
{
    Console.WriteLine($"Mode: Legacy | Audio: {source} | Rate: {sampleRate}Hz | Batch: {batchMs}ms | Lang: {language ?? "auto"}");
}
if (detectionEnabled && !useStreaming)
{
    Console.WriteLine($"Question Detection: {detectionMethod}" + (detectionMethod == QuestionDetectionMethod.Llm ? $" ({detectionModel})" : ""));
}
else if (!useStreaming)
{
    Console.WriteLine("Question Detection: Disabled");
}
Console.WriteLine("Press Ctrl+C to stop");
Console.WriteLine(new string('-', 60));
Console.WriteLine();

// Create audio capture
var audio = new WindowsAudioCaptureService(sampleRate, source);

// Handle Ctrl+C
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("Stopping...");
    cts.Cancel();
};

if (useStreaming)
{
    // Build streaming transcription options
    var streamingOptionsBuilder = new StreamingTranscriptionOptionsBuilder()
        .WithApiKey(apiKey)
        .WithMode(streamingMode)
        .WithSampleRate(sampleRate)
        .WithLanguage(language)
        .WithContextPrompting(true, maxChars: 200, vocabulary: vocabularyPrompt);

    // Configure mode-specific options
    if (streamingMode == TranscriptionMode.Basic)
    {
        streamingOptionsBuilder.WithBasicOptions(batchMs, batchMs * 2);
    }

    var streamingOptions = streamingOptionsBuilder.Build();

    // Create the appropriate streaming service based on mode
    await using IStreamingTranscriptionService streamingService = streamingMode switch
    {
        TranscriptionMode.Revision => new RevisionTranscriptionService(audio, streamingOptions),
        TranscriptionMode.Streaming => new StreamingHypothesisService(audio, streamingOptions),
        _ => new BasicTranscriptionService(audio, streamingOptions)
    };

    // Track provisional text for display updates
    string lastProvisional = "";

    // Wire up streaming transcription events
    streamingService.OnStableText += args =>
    {
        // Clear provisional display if any
        if (!string.IsNullOrEmpty(lastProvisional))
        {
            // Backspace over provisional text
            Console.Write(new string('\b', lastProvisional.Length));
            Console.Write(new string(' ', lastProvisional.Length));
            Console.Write(new string('\b', lastProvisional.Length));
            lastProvisional = "";
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(args.Text);
        Console.Write(" ");
        Console.ResetColor();
    };

    streamingService.OnProvisionalText += args =>
    {
        if (string.IsNullOrWhiteSpace(args.Text)) return;

        // Clear previous provisional
        if (!string.IsNullOrEmpty(lastProvisional))
        {
            Console.Write(new string('\b', lastProvisional.Length));
            Console.Write(new string(' ', lastProvisional.Length));
            Console.Write(new string('\b', lastProvisional.Length));
        }

        // Display new provisional in different color
        lastProvisional = args.Text;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(args.Text);
        Console.ResetColor();
    };

    streamingService.OnInfo += msg =>
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"[Info] {msg}");
        Console.ResetColor();
    };

    streamingService.OnWarning += msg =>
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[Warn] {msg}");
        Console.ResetColor();
    };

    streamingService.OnError += ex =>
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Error] {ex.Message}");
        Console.ResetColor();
    };

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Listening for audio...");
    Console.ResetColor();
    Console.WriteLine();

    // Start and wait - StartAsync blocks until cancelled or stopped
    _ = Task.Run(async () =>
    {
        try
        {
            await streamingService.StartAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
    });

    try
    {
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // Expected on shutdown
    }

    await streamingService.StopAsync();

    Console.WriteLine();
    Console.WriteLine($"Final stable transcript: {streamingService.GetStableTranscript()}");
}
else
{
    // Legacy transcription mode
    // Create transcription options
    var options = new TimestampedTranscriptionOptions
    {
        SampleRate = sampleRate,
        BatchMs = batchMs,
        Language = language
    };

    // Create transcription service
    await using var transcription = new TimestampedTranscriptionService(audio, apiKey, options);

    // Create question detector based on configuration (only if enabled)
    IQuestionDetector? questionDetector = null;
    if (detectionEnabled)
    {
        questionDetector = detectionMethod switch
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
    }

    // Wire up events - streaming display with question detection
    transcription.OnTranscriptionResult += async result =>
    {
        foreach (var segment in result.Segments)
        {
            var text = segment.Text.Trim();

            // Always print transcript text first
            Console.Write(text);
            Console.Write(" ");

            // Skip detection if disabled
            if (questionDetector == null)
                continue;

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

    // Wire up speech pause signal for two-phase question detection (only if detection enabled)
    if (questionDetector != null)
    {
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
    }

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
    Console.WriteLine("Streaming Transcription Options:");
    Console.WriteLine("  --streaming, -s        Enable streaming transcription mode");
    Console.WriteLine("  --mode <mode>          Streaming mode: basic, revision, or streaming");
    Console.WriteLine("                         - basic: All text immediately stable (default)");
    Console.WriteLine("                         - revision: Overlapping batches with local agreement");
    Console.WriteLine("                         - streaming: Real-time hypothesis with stability tracking");
    Console.WriteLine("  --vocabulary <terms>   Technical vocabulary for context prompting");
    Console.WriteLine("                         (e.g., \"C#, async, await, IEnumerable\")");
    Console.WriteLine();
    Console.WriteLine("Question Detection Options (legacy mode only):");
    Console.WriteLine("  --detection, -d <method>  Detection method: heuristic (default) or llm");
    Console.WriteLine("  --detection-model <model> LLM model (default: gpt-4o-mini)");
    Console.WriteLine();
    Console.WriteLine("Other:");
    Console.WriteLine("  --help, -h             Show this help message");
    Console.WriteLine();
    Console.WriteLine("Environment:");
    Console.WriteLine("  OPENAI_API_KEY         OpenAI API key (required)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  # Basic streaming mode with microphone");
    Console.WriteLine("  Interview-assist-transcription-console --streaming --mic");
    Console.WriteLine();
    Console.WriteLine("  # Revision mode with vocabulary prompting");
    Console.WriteLine("  Interview-assist-transcription-console --mode revision --vocab \"C#, async\"");
    Console.WriteLine();
    Console.WriteLine("  # Legacy mode with LLM question detection");
    Console.WriteLine("  Interview-assist-transcription-console --detection llm");
}

// Marker class for user secrets
public partial class Program { }
