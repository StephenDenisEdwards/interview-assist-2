using InterviewAssist.Audio.Windows;
using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Pipeline.Detection;
using InterviewAssist.Library.Pipeline.Evaluation;
using InterviewAssist.Library.Pipeline.Recording;
using InterviewAssist.Library.Pipeline.Utterance;
using InterviewAssist.Library.Transcription;
using Microsoft.Extensions.Configuration;
using Terminal.Gui;

namespace InterviewAssist.TranscriptionDetectionConsole;

public partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Parse command-line arguments
        string? playbackFile = null;
        string? playbackMode = null;
        string? evaluateFile = null;
        string? evaluateOutput = null;
        string? evaluateModel = null;
        string? analyzeErrorsFile = null;
        string? tuneThresholdFile = null;
        string? optimizeTarget = null;
        string? compareFile = null;
        string? regressionBaseline = null;
        string? regressionData = null;
        string? createBaselineOutput = null;
        string? baselineVersion = null;
        string? datasetFile = null;
        string? generateTestsFile = null;
        string? analyzeSessionFile = null;
        string? groundTruthFile = null;
        bool headless = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--playback" && i + 1 < args.Length)
            {
                playbackFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--mode" && i + 1 < args.Length)
            {
                playbackMode = args[i + 1];
                i++;
            }
            else if (args[i] == "--evaluate" && i + 1 < args.Length)
            {
                evaluateFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--output" && i + 1 < args.Length)
            {
                evaluateOutput = args[i + 1];
                i++;
            }
            else if (args[i] == "--model" && i + 1 < args.Length)
            {
                evaluateModel = args[i + 1];
                i++;
            }
            else if (args[i] == "--analyze-errors" && i + 1 < args.Length)
            {
                analyzeErrorsFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--tune-threshold" && i + 1 < args.Length)
            {
                tuneThresholdFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--optimize" && i + 1 < args.Length)
            {
                optimizeTarget = args[i + 1];
                i++;
            }
            else if (args[i] == "--compare" && i + 1 < args.Length)
            {
                compareFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--regression" && i + 1 < args.Length)
            {
                regressionBaseline = args[i + 1];
                i++;
            }
            else if (args[i] == "--data" && i + 1 < args.Length)
            {
                regressionData = args[i + 1];
                i++;
            }
            else if (args[i] == "--create-baseline" && i + 1 < args.Length)
            {
                createBaselineOutput = args[i + 1];
                i++;
            }
            else if (args[i] == "--version" && i + 1 < args.Length)
            {
                baselineVersion = args[i + 1];
                i++;
            }
            else if (args[i] == "--dataset" && i + 1 < args.Length)
            {
                datasetFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--generate-tests" && i + 1 < args.Length)
            {
                generateTestsFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--analyze" && i + 1 < args.Length)
            {
                analyzeSessionFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--ground-truth" && i + 1 < args.Length)
            {
                groundTruthFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--headless")
            {
                headless = true;
            }
            else if (args[i] == "--help" || args[i] == "-h")
            {
                Console.WriteLine("Interview Assist - Transcription & Detection Console");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  dotnet run                                Normal mode (live audio)");
                Console.WriteLine("  dotnet run -- --playback <file>           Playback mode (from recorded session)");
                Console.WriteLine("  dotnet run -- --playback <file> --headless  Headless playback (no UI, console summary)");
                Console.WriteLine("  dotnet run -- --analyze <file>            Generate report from existing session JSONL");
                Console.WriteLine("  dotnet run -- --evaluate <file>           Evaluate question detection accuracy");
                Console.WriteLine("  dotnet run -- --compare <file>            Compare all detection strategies");
                Console.WriteLine("  dotnet run -- --tune-threshold <file>     Find optimal confidence threshold");
                Console.WriteLine("  dotnet run -- --regression <baseline>     Test for quality regressions");
                Console.WriteLine("  dotnet run -- --create-baseline <file>    Create baseline from session data");
                Console.WriteLine("  dotnet run -- --dataset <file>            Evaluate against curated dataset");
                Console.WriteLine("  dotnet run -- --generate-tests <seed>     Generate synthetic test cases");
                Console.WriteLine("  dotnet run -- --analyze-errors <file>     Analyze error patterns from report");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --playback <file>       Play back a recorded session (.jsonl or .wav)");
                Console.WriteLine("  --mode <mode>           Override detection mode for playback (Heuristic, Llm, Parallel)");
                Console.WriteLine("  --evaluate <file>       Evaluate detection against LLM-extracted ground truth");
                Console.WriteLine("  --compare <file>        Compare Heuristic, LLM, and Parallel strategies");
                Console.WriteLine("  --tune-threshold <file> Find optimal confidence threshold for detection");
                Console.WriteLine("  --optimize <target>     Optimization target: f1, precision, recall (default: f1)");
                Console.WriteLine("  --regression <baseline> Test against baseline for regressions");
                Console.WriteLine("  --data <file>           Session data file for regression test");
                Console.WriteLine("  --create-baseline <out> Create new baseline file");
                Console.WriteLine("  --version <ver>         Version string for baseline (default: 1.0)");
                Console.WriteLine("  --dataset <file>        Evaluate detection against curated dataset");
                Console.WriteLine("  --generate-tests <seed> Generate synthetic tests from seed file");
                Console.WriteLine("  --model <model>         Model for ground truth extraction (default: gpt-4o)");
                Console.WriteLine("  --ground-truth <file>   Use human-labeled ground truth JSON instead of LLM extraction");
                Console.WriteLine("  --output <file>         Output file for evaluation report (.json)");
                Console.WriteLine("  --analyze-errors <file> Analyze false positive patterns from evaluation report");
                Console.WriteLine("  --analyze <file>        Generate markdown report from existing session JSONL");
                Console.WriteLine("  --headless              Run playback without Terminal.Gui UI (headless mode)");
                Console.WriteLine("  --help, -h              Show this help message");
                Console.WriteLine();
                Console.WriteLine("Keyboard shortcuts in normal mode:");
                Console.WriteLine("  Ctrl+S    Stop transcription");
                Console.WriteLine("  Ctrl+R    Toggle recording (or use AutoStart in appsettings.json)");
                Console.WriteLine("  Ctrl+Q    Quit");
                Console.WriteLine();
                Console.WriteLine("Keyboard shortcuts in playback mode:");
                Console.WriteLine("  Space     Pause/resume WAV playback");
                Console.WriteLine("  Ctrl+Q    Quit");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  # Live transcription (Deepgram + LLM intent detection)");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj");
                Console.WriteLine();
                Console.WriteLine("  # Replay a JSONL recording with full UI");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.jsonl");
                Console.WriteLine();
                Console.WriteLine("  # Replay with detection mode override");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.jsonl --mode Llm");
                Console.WriteLine();
                Console.WriteLine("  # Re-transcribe a WAV recording via Deepgram");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.wav");
                Console.WriteLine();
                Console.WriteLine("  # Headless playback (console summary + markdown report)");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --playback recordings/session.jsonl --headless");
                Console.WriteLine();
                Console.WriteLine("  # Generate report from existing session data");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --analyze recordings/session.jsonl");
                Console.WriteLine();
                Console.WriteLine("  # Evaluate detection accuracy");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --evaluate recordings/session.jsonl --output report.json --model gpt-4o");
                Console.WriteLine();
                Console.WriteLine("  # Compare all detection strategies");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --compare recordings/session.jsonl");
                Console.WriteLine();
                Console.WriteLine("  # Find optimal confidence threshold");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --tune-threshold recordings/session.jsonl --optimize f1");
                Console.WriteLine();
                Console.WriteLine("  # Create a baseline and test for regressions");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --create-baseline baseline.json --data recordings/session.jsonl --version 1.0");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --regression baseline.json --data recordings/session.jsonl");
                Console.WriteLine();
                Console.WriteLine("  # Evaluate against curated dataset");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --dataset evaluations/datasets/questions.jsonl");
                Console.WriteLine();
                Console.WriteLine("  # Generate synthetic test cases");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --generate-tests seed.jsonl");
                Console.WriteLine();
                Console.WriteLine("  # Analyze error patterns from evaluation report");
                Console.WriteLine("  dotnet run --project Interview-assist-transcription-detection-console/Interview-assist-transcription-detection-console.csproj -- --analyze-errors evaluation-report.json");
                return 0;
            }
        }

        // Handle evaluate mode (non-interactive)
        if (evaluateFile != null)
        {
            return await RunEvaluationModeAsync(evaluateFile, evaluateOutput, evaluateModel, groundTruthFile);
        }

        // Handle analyze-errors mode (non-interactive)
        if (analyzeErrorsFile != null)
        {
            return await RunAnalyzeErrorsModeAsync(analyzeErrorsFile);
        }

        // Handle tune-threshold mode (non-interactive)
        if (tuneThresholdFile != null)
        {
            return await RunTuneThresholdModeAsync(tuneThresholdFile, optimizeTarget);
        }

        // Handle compare mode (non-interactive)
        if (compareFile != null)
        {
            return await RunCompareModeAsync(compareFile, evaluateOutput);
        }

        // Handle regression test mode (non-interactive)
        if (regressionBaseline != null)
        {
            if (string.IsNullOrEmpty(regressionData))
            {
                Console.WriteLine("Error: --data <file> is required with --regression");
                return 1;
            }
            return await RunRegressionTestModeAsync(regressionBaseline, regressionData);
        }

        // Handle create-baseline mode (non-interactive)
        if (createBaselineOutput != null)
        {
            if (string.IsNullOrEmpty(regressionData))
            {
                Console.WriteLine("Error: --data <file> is required with --create-baseline");
                return 1;
            }
            return await RunCreateBaselineModeAsync(regressionData, createBaselineOutput, baselineVersion ?? "1.0");
        }

        // Handle dataset evaluation mode (non-interactive)
        if (datasetFile != null)
        {
            return await RunDatasetEvaluationModeAsync(datasetFile, playbackMode);
        }

        // Handle generate-tests mode (non-interactive)
        if (generateTestsFile != null)
        {
            var output = evaluateOutput ?? Path.ChangeExtension(generateTestsFile, ".generated.jsonl");
            return await RunGenerateTestsModeAsync(generateTestsFile, output);
        }

        // Handle analyze mode (non-interactive)
        if (analyzeSessionFile != null)
        {
            return await RunAnalyzeSessionAsync(analyzeSessionFile);
        }

        // Handle headless playback mode (non-interactive)
        if (playbackFile != null && headless)
        {
            return await RunHeadlessPlaybackAsync(playbackFile, playbackMode);
        }

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // In JSONL playback mode, Deepgram API key is not required.
        // WAV playback still needs it (audio goes through Deepgram for transcription).
        var isWavPlayback = playbackFile != null
            && Path.GetExtension(playbackFile).Equals(".wav", StringComparison.OrdinalIgnoreCase);
        string? deepgramApiKey = null;
        if (playbackFile == null || isWavPlayback)
        {
            // Get Deepgram API key
            deepgramApiKey = GetFirstNonEmpty(
                configuration["Deepgram:ApiKey"],
                Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY"));

            if (string.IsNullOrWhiteSpace(deepgramApiKey))
            {
                Console.WriteLine("Error: Deepgram API key not configured.");
                Console.WriteLine("Set DEEPGRAM_API_KEY environment variable or add Deepgram:ApiKey to appsettings.json/user secrets.");
                return 1;
            }
        }

        // Load transcription settings
        var transcriptionConfig = configuration.GetSection("Transcription");
        var deepgramConfig = transcriptionConfig.GetSection("Deepgram");

        var audioSourceStr = transcriptionConfig["AudioSource"] ?? "Loopback";
        var source = audioSourceStr.Equals("mic", StringComparison.OrdinalIgnoreCase)
            || audioSourceStr.Equals("Microphone", StringComparison.OrdinalIgnoreCase)
            ? AudioInputSource.Microphone
            : AudioInputSource.Loopback;

        var sampleRate = transcriptionConfig.GetValue("SampleRate", 16000);
        var language = transcriptionConfig["Language"] ?? "en";
        var diarizeEnabled = deepgramConfig.GetValue("Diarize", false);
        var intentDetectionEnabled = transcriptionConfig.GetValue("IntentDetection:Enabled", true);

        // Load intent detection options
        var intentConfig = transcriptionConfig.GetSection("IntentDetection");
        var intentDetectionOptions = LoadIntentDetectionOptions(intentConfig, configuration);

        // Validate API key for LLM modes (before starting UI)
        if (playbackFile == null && intentDetectionEnabled &&
            intentDetectionOptions.Mode is IntentDetectionMode.Llm or IntentDetectionMode.Parallel &&
            string.IsNullOrWhiteSpace(intentDetectionOptions.Llm.ApiKey))
        {
            Console.WriteLine($"Error: OpenAI API key required for {intentDetectionOptions.Mode} detection mode.");
            Console.WriteLine("Set OPENAI_API_KEY environment variable or add IntentDetection:Llm:ApiKey to appsettings.json.");
            Console.WriteLine("Alternatively, set Mode to \"Heuristic\" to use free regex-based detection.");
            return 1;
        }

        // Validate API key for Deepgram detection mode
        if (playbackFile == null && intentDetectionEnabled &&
            intentDetectionOptions.Mode == IntentDetectionMode.Deepgram &&
            string.IsNullOrWhiteSpace(intentDetectionOptions.Deepgram.ApiKey))
        {
            Console.WriteLine("Error: Deepgram API key required for Deepgram detection mode.");
            Console.WriteLine("Set DEEPGRAM_API_KEY environment variable or add IntentDetection:Deepgram:ApiKey to appsettings.json.");
            Console.WriteLine("Alternatively, set Mode to \"Heuristic\" to use free regex-based detection.");
            return 1;
        }

        var deepgramOptions = new DeepgramOptions
        {
            ApiKey = deepgramApiKey!,
            Model = deepgramConfig["Model"] ?? "nova-2",
            Language = language,
            SampleRate = sampleRate,
            InterimResults = deepgramConfig.GetValue("InterimResults", true),
            Punctuate = deepgramConfig.GetValue("Punctuate", true),
            SmartFormat = deepgramConfig.GetValue("SmartFormat", true),
            EndpointingMs = deepgramConfig.GetValue("EndpointingMs", 300),
            UtteranceEndMs = deepgramConfig.GetValue("UtteranceEndMs", 1000),
            Keywords = deepgramConfig["Keywords"],
            Vad = deepgramConfig.GetValue("Vad", true),
            Diarize = diarizeEnabled
        };

        // Load UI settings
        var uiConfig = configuration.GetSection("UI");
        var backgroundColorHex = uiConfig["BackgroundColor"] ?? "#1E1E1E";
        var intentColorHex = uiConfig["IntentColor"] ?? "#FFFF00";

        // Load logging settings
        var loggingConfig = configuration.GetSection("Logging");
        var logFolder = loggingConfig["Folder"] ?? "logs";

        // Load recording settings
        var recordingConfig = configuration.GetSection("Recording");
        var recordingOptions = new RecordingOptions
        {
            Folder = recordingConfig["Folder"] ?? "recordings",
            FileNamePattern = recordingConfig["FileNamePattern"] ?? "session-{timestamp}-{pid}.recording.jsonl",
            AutoStart = recordingConfig.GetValue("AutoStart", false),
            SaveAudio = recordingConfig.GetValue("SaveAudio", false)
        };

        // Initialize Terminal.Gui
        Application.Init();

        try
        {
            // Create the main UI and run
            var app = new TranscriptionApp(
                source, sampleRate, deepgramOptions, diarizeEnabled, intentDetectionEnabled,
                intentDetectionOptions, backgroundColorHex, intentColorHex, recordingOptions, logFolder, playbackFile, playbackMode);
            await app.RunAsync();
        }
        finally
        {
            Application.Shutdown();
        }

        return 0;
    }

    private static string? GetFirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static IntentDetectionOptions LoadIntentDetectionOptions(
        IConfigurationSection intentConfig,
        IConfiguration rootConfig)
    {
        var modeStr = intentConfig["Mode"] ?? "Heuristic";
        var mode = modeStr.ToLowerInvariant() switch
        {
            "heuristic" => IntentDetectionMode.Heuristic,
            "llm" => IntentDetectionMode.Llm,
            "parallel" => IntentDetectionMode.Parallel,
            "deepgram" => IntentDetectionMode.Deepgram,
            _ => IntentDetectionMode.Heuristic
        };

        var heuristicConfig = intentConfig.GetSection("Heuristic");
        var llmConfig = intentConfig.GetSection("Llm");
        var deepgramDetectionConfig = intentConfig.GetSection("Deepgram");

        // Always load OpenAI API key (needed for LLM modes and playback of recordings made with LLM/Parallel)
        var openAiApiKey = GetFirstNonEmpty(
            llmConfig["ApiKey"],
            rootConfig["OpenAI:ApiKey"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        // Load Deepgram API key for detection (separate from transcription Deepgram key)
        var deepgramDetectionApiKey = GetFirstNonEmpty(
            deepgramDetectionConfig["ApiKey"],
            rootConfig["Deepgram:ApiKey"],
            Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY"));

        var customIntents = new List<string>();
        var customIntentsStr = deepgramDetectionConfig["CustomIntents"];
        if (!string.IsNullOrWhiteSpace(customIntentsStr))
        {
            customIntents.AddRange(customIntentsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return new IntentDetectionOptions
        {
            Enabled = intentConfig.GetValue("Enabled", true),
            Mode = mode,
            Heuristic = new HeuristicDetectionOptions
            {
                MinConfidence = heuristicConfig.GetValue("MinConfidence", 0.4)
            },
            Llm = new LlmDetectionOptions
            {
                ApiKey = openAiApiKey,
                Model = llmConfig["Model"] ?? "gpt-4o-mini",
                ConfidenceThreshold = llmConfig.GetValue("ConfidenceThreshold", 0.7),
                RateLimitMs = llmConfig.GetValue("RateLimitMs", 2000),
                BufferMaxChars = llmConfig.GetValue("BufferMaxChars", 800),
                TriggerOnQuestionMark = llmConfig.GetValue("TriggerOnQuestionMark", true),
                TriggerOnPause = llmConfig.GetValue("TriggerOnPause", true),
                TriggerTimeoutMs = llmConfig.GetValue("TriggerTimeoutMs", 3000),
                EnablePreprocessing = llmConfig.GetValue("EnablePreprocessing", true),
                EnableDeduplication = llmConfig.GetValue("EnableDeduplication", true),
                DeduplicationWindowMs = llmConfig.GetValue("DeduplicationWindowMs", 30000),
                ContextWindowChars = llmConfig.GetValue("ContextWindowChars", 1500),
                SystemPromptFile = llmConfig["SystemPromptFile"] ?? "system-prompt.txt"
            },
            Deepgram = new DeepgramDetectionOptions
            {
                ApiKey = deepgramDetectionApiKey,
                ConfidenceThreshold = deepgramDetectionConfig.GetValue("ConfidenceThreshold", 0.7),
                CustomIntents = customIntents,
                CustomIntentMode = deepgramDetectionConfig["CustomIntentMode"] ?? "extended",
                TimeoutMs = deepgramDetectionConfig.GetValue("TimeoutMs", 5000)
            }
        };
    }

    private static async Task<int> RunAnalyzeErrorsModeAsync(string reportFile)
    {
        var options = new EvaluationOptions();
        var runner = new EvaluationRunner(options);
        return await runner.AnalyzeErrorsAsync(reportFile);
    }

    private static async Task<int> RunTuneThresholdModeAsync(string sessionFile, string? targetStr)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // Get OpenAI API key
        var evaluationConfig = configuration.GetSection("Evaluation");
        var apiKey = GetFirstNonEmpty(
            evaluationConfig["ApiKey"],
            configuration["OpenAI:ApiKey"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        var options = new EvaluationOptions
        {
            ApiKey = apiKey,
            Model = evaluationConfig["Model"] ?? "gpt-4o",
            MatchThreshold = evaluationConfig.GetValue("MatchThreshold", 0.7),
            DeduplicationThreshold = evaluationConfig.GetValue("DeduplicationThreshold", 0.8)
        };

        var target = targetStr?.ToLowerInvariant() switch
        {
            "precision" => OptimizationTarget.Precision,
            "recall" => OptimizationTarget.Recall,
            "balanced" => OptimizationTarget.Balanced,
            _ => OptimizationTarget.F1
        };

        var runner = new EvaluationRunner(options);
        return await runner.TuneThresholdAsync(sessionFile, target);
    }

    private static async Task<int> RunCompareModeAsync(string sessionFile, string? outputFile)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // Get OpenAI API key
        var evaluationConfig = configuration.GetSection("Evaluation");
        var apiKey = GetFirstNonEmpty(
            evaluationConfig["ApiKey"],
            configuration["OpenAI:ApiKey"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        var options = new EvaluationOptions
        {
            ApiKey = apiKey,
            Model = evaluationConfig["Model"] ?? "gpt-4o",
            MatchThreshold = evaluationConfig.GetValue("MatchThreshold", 0.7),
            DeduplicationThreshold = evaluationConfig.GetValue("DeduplicationThreshold", 0.8)
        };

        // Load intent detection options for strategies
        var intentConfig = configuration.GetSection("Transcription:IntentDetection");
        var heuristicConfig = intentConfig.GetSection("Heuristic");
        var llmConfig = intentConfig.GetSection("Llm");

        var heuristicOptions = new HeuristicDetectionOptions
        {
            MinConfidence = heuristicConfig.GetValue("MinConfidence", 0.4)
        };

        var llmApiKey = GetFirstNonEmpty(
            llmConfig["ApiKey"],
            configuration["OpenAI:ApiKey"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        var llmOptions = new LlmDetectionOptions
        {
            ApiKey = llmApiKey,
            Model = llmConfig["Model"] ?? "gpt-4o-mini",
            ConfidenceThreshold = llmConfig.GetValue("ConfidenceThreshold", 0.7),
            RateLimitMs = llmConfig.GetValue("RateLimitMs", 2000),
            BufferMaxChars = llmConfig.GetValue("BufferMaxChars", 800),
            TriggerOnQuestionMark = llmConfig.GetValue("TriggerOnQuestionMark", true),
            TriggerOnPause = llmConfig.GetValue("TriggerOnPause", true),
            TriggerTimeoutMs = llmConfig.GetValue("TriggerTimeoutMs", 3000),
            EnablePreprocessing = llmConfig.GetValue("EnablePreprocessing", true),
            EnableDeduplication = llmConfig.GetValue("EnableDeduplication", true),
            DeduplicationWindowMs = llmConfig.GetValue("DeduplicationWindowMs", 30000),
            ContextWindowChars = llmConfig.GetValue("ContextWindowChars", 1500)
        };

        // Load Deepgram detection options
        var deepgramDetectionConfig = intentConfig.GetSection("Deepgram");
        var deepgramDetectionApiKey = GetFirstNonEmpty(
            deepgramDetectionConfig["ApiKey"],
            configuration["Deepgram:ApiKey"],
            Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY"));

        var customIntents = new List<string>();
        var customIntentsStr = deepgramDetectionConfig["CustomIntents"];
        if (!string.IsNullOrWhiteSpace(customIntentsStr))
        {
            customIntents.AddRange(customIntentsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var deepgramOptions = new DeepgramDetectionOptions
        {
            ApiKey = deepgramDetectionApiKey,
            ConfidenceThreshold = deepgramDetectionConfig.GetValue("ConfidenceThreshold", 0.7),
            CustomIntents = customIntents,
            CustomIntentMode = deepgramDetectionConfig["CustomIntentMode"] ?? "extended",
            TimeoutMs = deepgramDetectionConfig.GetValue("TimeoutMs", 5000)
        };

        var runner = new EvaluationRunner(options);
        return await runner.CompareStrategiesAsync(sessionFile, outputFile, heuristicOptions, llmOptions, deepgramOptions);
    }

    private static async Task<int> RunEvaluationModeAsync(string evaluateFile, string? outputFile, string? model, string? groundTruthFile = null)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // Get OpenAI API key
        var evaluationConfig = configuration.GetSection("Evaluation");
        var apiKey = GetFirstNonEmpty(
            evaluationConfig["ApiKey"],
            configuration["OpenAI:ApiKey"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        var options = new EvaluationOptions
        {
            ApiKey = apiKey,
            Model = model ?? evaluationConfig["Model"] ?? "gpt-4o",
            MatchThreshold = evaluationConfig.GetValue("MatchThreshold", 0.7),
            DeduplicationThreshold = evaluationConfig.GetValue("DeduplicationThreshold", 0.8),
            OutputFolder = evaluationConfig["OutputFolder"] ?? "evaluations",
            GroundTruthFile = groundTruthFile
        };

        // Default output file if not specified
        if (string.IsNullOrWhiteSpace(outputFile))
        {
            var sessionId = SessionReportGenerator.ExtractSessionId(evaluateFile)
                ?? Path.GetFileNameWithoutExtension(evaluateFile);
            outputFile = Path.Combine(options.OutputFolder, $"{sessionId}.evaluation.json");
        }

        // Avoid overwriting: append version number if file exists
        if (File.Exists(outputFile))
        {
            var dir = Path.GetDirectoryName(outputFile)!;
            var name = Path.GetFileName(outputFile);
            // Strip .json extension, then check for existing version suffix
            var baseName = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? name[..^5] : name;
            var version = 2;
            while (File.Exists(Path.Combine(dir, $"{baseName}-v{version}.json")))
                version++;
            outputFile = Path.Combine(dir, $"{baseName}-v{version}.json");
        }

        var runner = new EvaluationRunner(options);
        return await runner.RunAsync(evaluateFile, outputFile);
    }

    private static async Task<int> RunRegressionTestModeAsync(string baselineFile, string sessionFile)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // Get OpenAI API key
        var evaluationConfig = configuration.GetSection("Evaluation");
        var apiKey = GetFirstNonEmpty(
            evaluationConfig["ApiKey"],
            configuration["OpenAI:ApiKey"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        var options = new EvaluationOptions
        {
            ApiKey = apiKey,
            Model = evaluationConfig["Model"] ?? "gpt-4o",
            MatchThreshold = evaluationConfig.GetValue("MatchThreshold", 0.7),
            DeduplicationThreshold = evaluationConfig.GetValue("DeduplicationThreshold", 0.8)
        };

        var runner = new EvaluationRunner(options);
        return await runner.RunRegressionTestAsync(baselineFile, sessionFile);
    }

    private static async Task<int> RunCreateBaselineModeAsync(string sessionFile, string outputFile, string version)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // Get OpenAI API key
        var evaluationConfig = configuration.GetSection("Evaluation");
        var apiKey = GetFirstNonEmpty(
            evaluationConfig["ApiKey"],
            configuration["OpenAI:ApiKey"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        var options = new EvaluationOptions
        {
            ApiKey = apiKey,
            Model = evaluationConfig["Model"] ?? "gpt-4o",
            MatchThreshold = evaluationConfig.GetValue("MatchThreshold", 0.7),
            DeduplicationThreshold = evaluationConfig.GetValue("DeduplicationThreshold", 0.8)
        };

        var runner = new EvaluationRunner(options);
        return await runner.CreateBaselineAsync(sessionFile, outputFile, version);
    }

    private static async Task<int> RunDatasetEvaluationModeAsync(string datasetFile, string? modeOverride)
    {
        Console.WriteLine("=== Dataset Evaluation ===");
        Console.WriteLine($"Dataset: {Path.GetFileName(datasetFile)}");
        Console.WriteLine();

        // Load dataset
        Console.WriteLine("Loading dataset...");
        var dataset = await DatasetLoader.LoadAsync(datasetFile);
        Console.WriteLine($"  Total items: {dataset.Items.Count}");
        Console.WriteLine($"  Questions: {dataset.QuestionCount}");
        Console.WriteLine($"  Statements: {dataset.StatementCount}");
        Console.WriteLine($"  Commands: {dataset.CommandCount}");
        Console.WriteLine();

        // Validate
        var validation = DatasetLoader.Validate(dataset);
        if (!validation.IsValid)
        {
            Console.WriteLine("Validation issues:");
            foreach (var issue in validation.Issues.Take(5))
            {
                Console.WriteLine($"  - {issue}");
            }
            Console.WriteLine();
        }

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // Create strategy
        var intentConfig = configuration.GetSection("Transcription:IntentDetection");
        var heuristicConfig = intentConfig.GetSection("Heuristic");

        var heuristicOptions = new HeuristicDetectionOptions
        {
            MinConfidence = heuristicConfig.GetValue("MinConfidence", 0.4)
        };

        var strategy = new HeuristicIntentStrategy(heuristicOptions);

        Console.WriteLine("Evaluating with Heuristic strategy...");
        var evaluator = new DatasetEvaluator();
        var result = await evaluator.EvaluateAsync(dataset, strategy);

        // Print results
        Console.WriteLine();
        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Type Accuracy:     {result.TypeAccuracy:P1} ({result.CorrectType}/{result.TotalItems})");
        Console.WriteLine($"Question F1:       {result.QuestionF1:P1}");
        Console.WriteLine($"  Precision:       {result.QuestionPrecision:P1}");
        Console.WriteLine($"  Recall:          {result.QuestionRecall:P1}");
        Console.WriteLine($"Subtype Accuracy:  {result.SubtypeAccuracy:P1}");
        Console.WriteLine();

        Console.WriteLine("Confusion Matrix:");
        Console.WriteLine(result.ConfusionMatrix.ToFormattedString());

        // Show misclassifications
        var errors = result.ItemResults.Where(r => !r.TypeCorrect).Take(10).ToList();
        if (errors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Sample Misclassifications ({errors.Count} shown):");
            foreach (var err in errors)
            {
                Console.WriteLine($"  Expected: {err.ActualType}, Got: {err.PredictedType}");
                Console.WriteLine($"    \"{err.Text[..Math.Min(50, err.Text.Length)]}...\"");
            }
        }

        return 0;
    }

    private static async Task<int> RunGenerateTestsModeAsync(string seedFile, string outputFile)
    {
        Console.WriteLine("=== Generate Synthetic Tests ===");
        Console.WriteLine($"Seed file: {Path.GetFileName(seedFile)}");
        Console.WriteLine($"Output: {outputFile}");
        Console.WriteLine();

        var generator = new SyntheticTestGenerator();

        Console.WriteLine("Generating test variations...");
        var testCases = await generator.GenerateFromSeedFileAsync(seedFile, includeNegatives: true);

        Console.WriteLine($"  Generated: {testCases.Count} test cases");

        var byVariation = testCases.GroupBy(t => t.Variation).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (variation, count) in byVariation.OrderByDescending(kvp => kvp.Value))
        {
            Console.WriteLine($"    {variation}: {count}");
        }

        Console.WriteLine();
        Console.WriteLine("Saving to file...");
        await SyntheticTestGenerator.SaveToFileAsync(outputFile, testCases);

        Console.WriteLine($"Saved {testCases.Count} test cases to {outputFile}");

        return 0;
    }

    /// <summary>
    /// Creates an intent detection strategy from a mode string and options.
    /// Used by both TranscriptionApp and headless playback.
    /// </summary>
    internal static IIntentDetectionStrategy? CreateDetectionStrategyForMode(
        string? modeStr, IntentDetectionOptions options, Action<string>? log = null)
    {
        var mode = modeStr?.ToLowerInvariant() switch
        {
            "heuristic" => IntentDetectionMode.Heuristic,
            "llm" => IntentDetectionMode.Llm,
            "parallel" => IntentDetectionMode.Parallel,
            "deepgram" => IntentDetectionMode.Deepgram,
            _ => options.Mode
        };

        return mode switch
        {
            IntentDetectionMode.Heuristic => new HeuristicIntentStrategy(options.Heuristic),
            IntentDetectionMode.Llm => CreateLlmStrategyStatic(options, log),
            IntentDetectionMode.Parallel => CreateParallelStrategyStatic(options, log),
            IntentDetectionMode.Deepgram => CreateDeepgramStrategyStatic(options),
            _ => new HeuristicIntentStrategy(options.Heuristic)
        };
    }

    private static LlmIntentStrategy CreateLlmStrategyStatic(IntentDetectionOptions options, Action<string>? log = null)
    {
        var apiKey = options.Llm.ApiKey
            ?? throw new InvalidOperationException("OpenAI API key is required for LLM detection mode.");

        var systemPrompt = LoadSystemPromptStatic(options.Llm, log);

        var detector = new OpenAiIntentDetector(
            apiKey, options.Llm.Model, options.Llm.ConfidenceThreshold, systemPrompt);

        if (log != null)
            WireDetectorRequestLogging(detector, log);

        return new LlmIntentStrategy(detector, options.Llm);
    }

    private static ParallelIntentStrategy CreateParallelStrategyStatic(IntentDetectionOptions options, Action<string>? log = null)
    {
        var apiKey = options.Llm.ApiKey
            ?? throw new InvalidOperationException("OpenAI API key is required for Parallel detection mode.");

        var systemPrompt = LoadSystemPromptStatic(options.Llm, log);

        var llmDetector = new OpenAiIntentDetector(
            apiKey, options.Llm.Model, options.Llm.ConfidenceThreshold, systemPrompt);

        if (log != null)
            WireDetectorRequestLogging(llmDetector, log);

        return new ParallelIntentStrategy(llmDetector, options.Heuristic, options.Llm);
    }

    private static LlmIntentStrategy CreateDeepgramStrategyStatic(IntentDetectionOptions options)
    {
        var apiKey = options.Deepgram.ApiKey
            ?? throw new InvalidOperationException("Deepgram API key is required for Deepgram detection mode.");

        var detector = new DeepgramIntentDetector(apiKey, options.Deepgram);
        return new LlmIntentStrategy(detector, options.Llm);
    }

    private static void WireDetectorRequestLogging(OpenAiIntentDetector detector, Action<string> log)
    {
        log("═══ LLM System Prompt ═══");
        log(detector.SystemPrompt);
        log("═══ End System Prompt ═══");

        void LogYellow(string msg)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            log(msg);
            Console.ForegroundColor = prev;
        }

        detector.OnRequestSending += userMessage =>
        {
            LogYellow("─── LLM Request ───");
            LogYellow($"[User Message]\n{userMessage}");
        };

        detector.OnRequestCompleted += elapsedMs =>
        {
            LogYellow($"[LLM completed in {elapsedMs}ms]");
            LogYellow("─── End Request ───");
        };
    }

    private static string LoadSystemPromptStatic(LlmDetectionOptions options, Action<string>? log)
    {
        var text = options.LoadSystemPrompt();
        log?.Invoke($"Loaded system prompt from {options.SystemPromptFile} ({text.Length} chars)");
        return text;
    }

    private static async Task<int> RunHeadlessPlaybackAsync(string playbackFile, string? modeOverride)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // Load intent detection options
        var intentConfig = configuration.GetSection("Transcription:IntentDetection");
        var intentDetectionOptions = LoadIntentDetectionOptions(intentConfig, configuration);

        // Logging setup
        var loggingConfig = configuration.GetSection("Logging");
        var logFolder = loggingConfig["Folder"] ?? "logs";
        Directory.CreateDirectory(logFolder);
        var sessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        var sessionId = $"session-{sessionTimestamp}-{Environment.ProcessId}";
        var logFileName = Path.Combine(logFolder, $"{sessionId}.log");
        await using var logWriter = new StreamWriter(logFileName, append: false) { AutoFlush = true };

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Console.WriteLine(line);
            try { logWriter.WriteLine(line); } catch { }
        }

        // Recording setup
        var recordingConfig = configuration.GetSection("Recording");
        var recordingOptions = new RecordingOptions
        {
            Folder = recordingConfig["Folder"] ?? "recordings",
            FileNamePattern = recordingConfig["FileNamePattern"] ?? "session-{timestamp}-{pid}.recording.jsonl"
        };

        var extension = Path.GetExtension(playbackFile).ToLowerInvariant();
        var isWav = extension == ".wav";

        Log($"Headless playback: {playbackFile}");

        // Stats collectors
        var stats = new HeadlessPlaybackStats();

        if (isWav)
        {
            return await RunHeadlessWavPlaybackAsync(
                playbackFile, modeOverride, configuration, intentDetectionOptions,
                recordingOptions, logFileName, Log, sw, stats);
        }

        // ── JSONL playback ──
        var player = new SessionPlayer();
        player.OnInfo += msg => Log($"[Playback] {msg}");

        Log("Loading session...");
        await player.LoadAsync(playbackFile);
        stats.TotalEvents = player.TotalEvents;

        // Determine effective detection mode
        var recordedMode = player.SessionConfig?.IntentDetectionMode;
        var effectiveMode = modeOverride ?? recordedMode;

        // Check API key availability
        var requiresOpenAiKey = effectiveMode?.ToLowerInvariant() is "llm" or "parallel";
        var requiresDeepgramKey = effectiveMode?.ToLowerInvariant() is "deepgram";

        if (requiresOpenAiKey && string.IsNullOrWhiteSpace(intentDetectionOptions.Llm.ApiKey))
        {
            Log($"WARNING: {effectiveMode} mode requires OpenAI API key. Falling back to Heuristic mode.");
            effectiveMode = "Heuristic";
        }
        else if (requiresDeepgramKey && string.IsNullOrWhiteSpace(intentDetectionOptions.Deepgram.ApiKey))
        {
            Log($"WARNING: {effectiveMode} mode requires Deepgram API key. Falling back to Heuristic mode.");
            effectiveMode = "Heuristic";
        }

        var strategy = CreateDetectionStrategyForMode(effectiveMode, intentDetectionOptions, Log);
        using var pipeline = new UtteranceIntentPipeline(detectionStrategy: strategy);
        var modeName = pipeline.DetectionModeName;

        Log($"Detection mode: {modeName} (override={modeOverride ?? "none"}, recorded={recordedMode ?? "none"})");

        // Wire recorder
        var recorder = new SessionRecorder(pipeline);
        recorder.OnInfo += msg => Log($"[Recording] {msg}");
        var outputPath = recordingOptions.GenerateFilePath();
        var sessionConfig = new SessionConfig
        {
            IntentDetectionEnabled = true,
            IntentDetectionMode = modeName,
            AudioSource = "Playback"
        };
        recorder.Start(outputPath, sessionConfig);

        // Wire pipeline events for stats
        WireHeadlessPipelineEvents(pipeline, Log, stats);

        pipeline.OnUtteranceFinal += evt =>
        {
            if (!stats.IntentTimestamps.ContainsKey(evt.Id))
                stats.IntentTimestamps[evt.Id] = evt.StartTime;
        };

        stats.TotalEvents = player.TotalEvents;

        // Play
        Log($"Starting playback ({stats.TotalEvents} events)...");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            await player.PlayAsync(pipeline, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Playback cancelled.");
        }

        // Wait for in-flight LLM calls
        if (modeName is "LLM" or "Parallel")
        {
            Log("Waiting for in-flight LLM calls...");
            await Task.Delay(5000);
        }

        recorder.Stop();
        sw.Stop();

        // Auto-generate session report
        var reportEvents = await SessionReportGenerator.LoadEventsAsync(outputPath);
        string[]? logLines = File.Exists(logFileName) ? await ReadAllLinesSharedAsync(logFileName) : null;
        var report = SessionReportGenerator.GenerateMarkdown(reportEvents,
            sourceFile: playbackFile, outputFile: outputPath,
            logFile: logFileName, wallClockDuration: sw.Elapsed,
            logLines: logLines);
        var reportPath = SessionReportGenerator.GetReportPath(outputPath);
        Directory.CreateDirectory("reports");
        await File.WriteAllTextAsync(reportPath, report);
        Log($"Report: {reportPath}");

        PrintHeadlessSummary(playbackFile, outputPath, logFileName, modeName, sw.Elapsed, stats, reportPath);

        return 0;
    }

    private static async Task<int> RunHeadlessWavPlaybackAsync(
        string playbackFile, string? modeOverride,
        IConfiguration configuration, IntentDetectionOptions intentDetectionOptions,
        RecordingOptions recordingOptions, string logFileName,
        Action<string> log, System.Diagnostics.Stopwatch sw,
        HeadlessPlaybackStats stats)
    {
        // Load Deepgram settings
        var transcriptionConfig = configuration.GetSection("Transcription");
        var deepgramConfig = transcriptionConfig.GetSection("Deepgram");
        var sampleRate = transcriptionConfig.GetValue("SampleRate", 16000);
        var language = transcriptionConfig["Language"] ?? "en";

        var deepgramApiKey = GetFirstNonEmpty(
            configuration["Deepgram:ApiKey"],
            Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY"));

        if (string.IsNullOrWhiteSpace(deepgramApiKey))
        {
            Console.WriteLine("Error: Deepgram API key required for WAV playback.");
            Console.WriteLine("Set DEEPGRAM_API_KEY environment variable or add Deepgram:ApiKey to appsettings.json.");
            return 1;
        }

        var deepgramOptions = new DeepgramOptions
        {
            ApiKey = deepgramApiKey,
            Model = deepgramConfig["Model"] ?? "nova-2",
            Language = language,
            SampleRate = sampleRate,
            InterimResults = deepgramConfig.GetValue("InterimResults", true),
            Punctuate = deepgramConfig.GetValue("Punctuate", true),
            SmartFormat = deepgramConfig.GetValue("SmartFormat", true),
            EndpointingMs = deepgramConfig.GetValue("EndpointingMs", 300),
            UtteranceEndMs = deepgramConfig.GetValue("UtteranceEndMs", 1000),
            Keywords = deepgramConfig["Keywords"],
            Vad = deepgramConfig.GetValue("Vad", true),
            Diarize = deepgramConfig.GetValue("Diarize", false)
        };

        // Determine detection mode
        var effectiveMode = modeOverride;
        if (effectiveMode == null)
        {
            var modeStr = transcriptionConfig["IntentDetection:Mode"] ?? "Heuristic";
            effectiveMode = modeStr;
        }

        var requiresOpenAiKey = effectiveMode.ToLowerInvariant() is "llm" or "parallel";
        if (requiresOpenAiKey && string.IsNullOrWhiteSpace(intentDetectionOptions.Llm.ApiKey))
        {
            log($"WARNING: {effectiveMode} mode requires OpenAI API key. Falling back to Heuristic mode.");
            effectiveMode = "Heuristic";
        }

        var strategy = CreateDetectionStrategyForMode(effectiveMode, intentDetectionOptions, log);
        using var pipeline = new UtteranceIntentPipeline(detectionStrategy: strategy);
        var modeName = pipeline.DetectionModeName;

        log($"Detection mode: {modeName}");

        // Wire recorder
        var recorder = new SessionRecorder(pipeline);
        recorder.OnInfo += msg => log($"[Recording] {msg}");
        var outputPath = recordingOptions.GenerateFilePath();
        var sessionConfig = new SessionConfig
        {
            DeepgramModel = deepgramOptions.Model,
            IntentDetectionEnabled = true,
            IntentDetectionMode = modeName,
            AudioSource = "WAV Playback",
            SampleRate = sampleRate
        };
        recorder.Start(outputPath, sessionConfig);

        // Wire pipeline events for stats
        WireHeadlessPipelineEvents(pipeline, log, stats);

        pipeline.OnUtteranceFinal += evt =>
        {
            if (!stats.IntentTimestamps.ContainsKey(evt.Id))
                stats.IntentTimestamps[evt.Id] = evt.StartTime;
        };

        // Create audio source and Deepgram service
        var wavAudio = new WavFileAudioSource(playbackFile, sampleRate);
        var deepgramService = new DeepgramTranscriptionService(wavAudio, deepgramOptions);

        // Wire Deepgram events to pipeline
        // Note: provisionals are NOT forwarded — they cause duplicate utterances from
        // progressive refinements. Stables (from endpointing + PromoteProvisionalToStable)
        // already cover all confirmed text.
        var eventCount = 0;
        deepgramService.OnStableText += args =>
        {
            Interlocked.Increment(ref eventCount);
            pipeline.ProcessAsrEvent(new AsrEvent
            {
                Text = args.Text,
                IsFinal = true,
                SpeakerId = args.Speaker?.ToString()
            });
        };

        deepgramService.OnInfo += msg =>
        {
            if (msg.Contains("Utterance end"))
                pipeline.SignalUtteranceEnd();
            log($"[Deepgram] {msg}");
        };

        deepgramService.OnWarning += msg => log($"[Deepgram.Warn] {msg}");
        deepgramService.OnError += ex => log($"[Deepgram.Error] {ex.Message}");

        // Wait for playback to complete
        var playbackComplete = new TaskCompletionSource();
        wavAudio.OnPlaybackComplete += () =>
        {
            _ = Task.Run(async () =>
            {
                log("WAV playback complete, waiting for final results...");
                await Task.Delay(3000);
                playbackComplete.TrySetResult();
            });
        };

        log($"Starting WAV playback via Deepgram (model: {deepgramOptions.Model})...");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); playbackComplete.TrySetResult(); };

        try
        {
            // StartAsync runs until Deepgram connection closes or cancellation
            var deepgramTask = deepgramService.StartAsync(cts.Token);

            // Wait for playback to finish (with WAV complete callback)
            await playbackComplete.Task;

            // Stop Deepgram
            await deepgramService.StopAsync();
        }
        catch (OperationCanceledException)
        {
            log("WAV playback cancelled.");
        }

        // Wait for in-flight LLM calls
        if (modeName is "LLM" or "Parallel")
        {
            log("Waiting for in-flight LLM calls...");
            await Task.Delay(5000);
        }

        stats.TotalEvents = eventCount;

        recorder.Stop();
        await deepgramService.DisposeAsync();
        wavAudio.Dispose();
        sw.Stop();

        // Auto-generate session report
        var reportEvents = await SessionReportGenerator.LoadEventsAsync(outputPath);
        string[]? logLines = File.Exists(logFileName) ? await ReadAllLinesSharedAsync(logFileName) : null;
        var report = SessionReportGenerator.GenerateMarkdown(reportEvents,
            sourceFile: playbackFile, outputFile: outputPath,
            logFile: logFileName, wallClockDuration: sw.Elapsed,
            logLines: logLines);
        var reportPath = SessionReportGenerator.GetReportPath(outputPath);
        Directory.CreateDirectory("reports");
        await File.WriteAllTextAsync(reportPath, report);
        log($"Report: {reportPath}");

        PrintHeadlessSummary(playbackFile, outputPath, logFileName, modeName, sw.Elapsed, stats, reportPath);

        return 0;
    }

    private static void WireHeadlessPipelineEvents(
        UtteranceIntentPipeline pipeline, Action<string> log, HeadlessPlaybackStats stats)
    {
        pipeline.OnIntentCandidate += evt =>
        {
            Interlocked.Increment(ref stats.CandidateCount);
            log($"[Intent.candidate] {evt.Intent.Type}/{evt.Intent.Subtype} conf={evt.Intent.Confidence:F2} utt={evt.UtteranceId}");
        };

        pipeline.OnIntentFinal += evt =>
        {
            var latencyMs = 0L;
            if (stats.IntentTimestamps.TryGetValue(evt.UtteranceId, out var startTime))
                latencyMs = (long)(evt.Timestamp - startTime).TotalMilliseconds;

            var apiTimeMs = evt.ApiTimeMs;

            // Track Questions and Imperatives in stats
            if (evt.Intent.Type is IntentType.Question or IntentType.Imperative)
            {
                var index = stats.FinalIntents.Count + 1;
                stats.FinalIntents.Add((index, evt.UtteranceId, evt.Intent, latencyMs, apiTimeMs));
                log($"[Intent.final] #{index} {evt.Intent.Type}/{evt.Intent.Subtype} conf={evt.Intent.Confidence:F2} latency={latencyMs}ms (api={apiTimeMs}ms)");
            }
            else
            {
                log($"[Intent.final] {evt.Intent.Type}/{evt.Intent.Subtype} conf={evt.Intent.Confidence:F2} latency={latencyMs}ms (api={apiTimeMs}ms)");
            }
            log($"  Source: \"{evt.Intent.SourceText}\"");
            log($"  Original: \"{evt.Intent.OriginalText ?? "(not available)"}\"");
        };

        pipeline.OnIntentCorrected += evt =>
        {
            var latencyMs = 0L;
            if (stats.IntentTimestamps.TryGetValue(evt.UtteranceId, out var startTime))
                latencyMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            var apiTimeMs = evt.ApiTimeMs;

            // Track Questions and Imperatives in stats
            if (evt.CorrectedIntent.Type is IntentType.Question or IntentType.Imperative)
            {
                var index = stats.FinalIntents.Count + 1;
                stats.FinalIntents.Add((index, evt.UtteranceId, evt.CorrectedIntent, latencyMs, apiTimeMs));
                log($"[Intent.corrected] #{index} {evt.CorrectionType}: conf={evt.CorrectedIntent.Confidence:F2} latency={latencyMs}ms (api={apiTimeMs}ms)");
            }
            else
            {
                log($"[Intent.corrected] {evt.CorrectionType}: {evt.CorrectedIntent.Type}/{evt.CorrectedIntent.Subtype} conf={evt.CorrectedIntent.Confidence:F2} latency={latencyMs}ms (api={apiTimeMs}ms)");
            }
            log($"  Source: \"{evt.CorrectedIntent.SourceText}\"");
            log($"  Original: \"{evt.CorrectedIntent.OriginalText ?? "(not available)"}\"");
        };

        pipeline.OnActionTriggered += evt =>
        {
            log($"[Action] {evt.ActionName} (debounced={evt.WasDebounced})");
        };

        pipeline.OnUtteranceFinal += evt =>
        {
            log($"[Utterance.final] {evt.Id}: \"{Truncate(evt.StableText, 60)}\" ({evt.CloseReason})");
        };
    }

    private static async Task<int> RunAnalyzeSessionAsync(string sessionFile)
    {
        if (!File.Exists(sessionFile))
        {
            Console.WriteLine($"Error: File not found: {sessionFile}");
            return 1;
        }

        Console.WriteLine($"Analyzing: {sessionFile}");

        var events = await SessionReportGenerator.LoadEventsAsync(sessionFile);
        if (events.Count == 0)
        {
            Console.WriteLine("Error: No events found in file.");
            return 1;
        }

        var logPath = SessionReportGenerator.ResolveLogFile(sessionFile);
        string[]? logLines = logPath != null ? await File.ReadAllLinesAsync(logPath) : null;
        var report = SessionReportGenerator.GenerateMarkdown(events, sourceFile: sessionFile,
            logFile: logPath, logLines: logLines);
        var reportPath = SessionReportGenerator.GetReportPath(sessionFile);
        Directory.CreateDirectory("reports");
        await File.WriteAllTextAsync(reportPath, report);

        Console.WriteLine($"Report:    {reportPath}");
        Console.WriteLine($"Log:       {logPath ?? "(not found)"}");
        Console.WriteLine($"Events:    {events.Count}");

        var intents = events.OfType<RecordedIntentEvent>().Where(e => !e.Data.IsCandidate).ToList();
        var corrections = events.OfType<RecordedIntentCorrectionEvent>().ToList();
        Console.WriteLine($"Intents:   {intents.Count} final, {corrections.Count} corrections");

        return 0;
    }

    private static void PrintHeadlessSummary(
        string sourceFile, string outputFile, string logFile, string modeName,
        TimeSpan duration, HeadlessPlaybackStats stats, string? reportPath = null)
    {
        Console.WriteLine();
        Console.WriteLine("═══ Headless Playback Summary ═══");
        Console.WriteLine($"Source:    {sourceFile}");
        Console.WriteLine($"Output:    {outputFile}");
        Console.WriteLine($"Log:       {logFile}");
        if (reportPath != null)
            Console.WriteLine($"Report:    {reportPath}");
        Console.WriteLine($"Mode:      {modeName}");
        Console.WriteLine($"Duration:  {duration.TotalSeconds:F1}s ({stats.TotalEvents} events)");
        Console.WriteLine();

        Console.WriteLine("Intents Detected:");
        Console.WriteLine($"  Candidates: {stats.CandidateCount}");
        Console.WriteLine($"  Finals:     {stats.FinalIntents.Count}");
        Console.WriteLine();

        if (stats.FinalIntents.Count > 0)
        {
            Console.WriteLine("Final Intent Details:");
            var exactMatches = 0;

            foreach (var (index, uttId, intent, latencyMs, apiTimeMs) in stats.FinalIntents)
            {
                Console.WriteLine($"  #{index} [{uttId}] {intent.Subtype} conf={intent.Confidence:F2} latency={latencyMs}ms (api={apiTimeMs}ms)");
                Console.WriteLine($"     Source:   \"{Truncate(intent.SourceText, 80)}\"");
                Console.WriteLine($"     Original: \"{Truncate(intent.OriginalText ?? "(not available)", 80)}\"");

                var isMatch = intent.OriginalText != null &&
                    string.Equals(intent.SourceText.Trim(), intent.OriginalText.Trim(), StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"     Match: {(isMatch ? "YES" : "NO")}");
                if (isMatch) exactMatches++;
            }

            Console.WriteLine();
            var total = stats.FinalIntents.Count;
            Console.WriteLine($"originalText Accuracy: {exactMatches}/{total} ({(total > 0 ? 100.0 * exactMatches / total : 0):F1}%) exact match");
            Console.WriteLine();

            // Latency stats
            var latencies = stats.FinalIntents.Where(f => f.LatencyMs > 0).Select(f => f.LatencyMs).OrderBy(l => l).ToList();
            var apiTimes = stats.FinalIntents.Where(f => f.ApiTimeMs > 0).Select(f => f.ApiTimeMs).OrderBy(t => t).ToList();
            if (latencies.Count > 0)
            {
                Console.WriteLine("End-to-end Latency (utterance open -> intent detected):");
                Console.WriteLine($"  Min:    {latencies.First()}ms");
                Console.WriteLine($"  Median: {latencies[latencies.Count / 2]}ms");
                Console.WriteLine($"  Mean:   {(long)latencies.Average()}ms");
                Console.WriteLine($"  Max:    {latencies.Last()}ms");
            }
            if (apiTimes.Count > 0)
            {
                Console.WriteLine("LLM API Time (HTTP request only):");
                Console.WriteLine($"  Min:    {apiTimes.First()}ms");
                Console.WriteLine($"  Median: {apiTimes[apiTimes.Count / 2]}ms");
                Console.WriteLine($"  Mean:   {(long)apiTimes.Average()}ms");
                Console.WriteLine($"  Max:    {apiTimes.Last()}ms");
            }
            Console.WriteLine();
            Console.WriteLine("Note: End-to-end latency measures the full pipeline from when the utterance");
            Console.WriteLine("was first opened to when the intent was detected. This includes transcription,");
            Console.WriteLine("utterance assembly, silence gap detection, buffering, rate limiting, and the");
            Console.WriteLine("LLM API call. LLM API time is just the HTTP request to the model.");
        }

        Console.WriteLine("═══════════════════════════════════");
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }

    /// <summary>
    /// Reads all lines from a file using FileShare.ReadWrite so it can be read
    /// while another process (the log writer) still holds the file open.
    /// </summary>
    private static async Task<string[]> ReadAllLinesSharedAsync(string path)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is { } line)
            lines.Add(line);
        return lines.ToArray();
    }
}

/// <summary>
/// Mutable stats container for headless playback (avoids ref parameter issues in async methods).
/// </summary>
internal class HeadlessPlaybackStats
{
    public int CandidateCount;
    public int TotalEvents;
    public readonly List<(int Index, string UtteranceId, DetectedIntent Intent, long LatencyMs, long ApiTimeMs)> FinalIntents = new();
    public readonly Dictionary<string, DateTime> IntentTimestamps = new();
}

/// <summary>
/// Main application with split-screen Terminal.Gui UI.
/// </summary>
public class TranscriptionApp
{
    private readonly AudioInputSource _audioSource;
    private readonly int _sampleRate;
    private readonly DeepgramOptions _deepgramOptions;
    private readonly bool _diarizeEnabled;
    private readonly bool _intentDetectionEnabled;
    private readonly IntentDetectionOptions _intentDetectionOptions;
    private readonly string _backgroundColorHex;
    private readonly string _intentColorHex;
    private readonly RecordingOptions _recordingOptions;
    private readonly string _logFolder;
    private readonly string? _playbackFile;
    private readonly string? _playbackModeOverride;

    // UI elements
    private HighlightTextView _transcriptView = null!;
    private TextView _intentView = null!;
    private FrameView _intentFrame = null!;
    private TextView _debugView = null!;
    private StatusItem _recordingStatusItem = null!;

    // State
    private readonly List<string> _debugLines = new();
    private readonly List<string> _intentLines = new();
    private int _intentCount;
    private int _nextHighlightId = 1;
    private readonly Dictionary<string, (int Start, int End)> _utterancePositions = new();
    private readonly List<(string Id, int Start, int End, string Text)> _utterancePositionList = new();
    private readonly List<(string UtteranceId, string? SourceText, string? OriginalText)> _pendingHighlights = new();
    private int _transcriptCharCount;
    private int _utteranceSearchFrom;
    private int? _currentSpeaker;
    private CancellationTokenSource? _cts;
    private DeepgramTranscriptionService? _deepgramService;
    private UtteranceIntentPipeline? _intentPipeline;
    private ColorScheme? _intentColorScheme;
    private Terminal.Gui.Attribute _highlightAttr;
    private SessionRecorder? _recorder;
    private AudioFileRecorder? _audioRecorder;
    private SessionPlayer _player = new();
    private WavFileAudioSource? _wavAudio;
    private bool _isTranscribing;
    private StatusItem _transcriptionStatusItem = null!;
    private StatusItem _detectionModeStatusItem = null!;
    private StatusItem _playbackStatusItem = null!;
    private readonly object _pipelineGate = new();
    private StreamWriter? _debugLogWriter;

    public TranscriptionApp(
        AudioInputSource audioSource,
        int sampleRate,
        DeepgramOptions deepgramOptions,
        bool diarizeEnabled,
        bool intentDetectionEnabled,
        IntentDetectionOptions intentDetectionOptions,
        string backgroundColorHex,
        string intentColorHex,
        RecordingOptions recordingOptions,
        string logFolder,
        string? playbackFile,
        string? playbackModeOverride = null)
    {
        _audioSource = audioSource;
        _sampleRate = sampleRate;
        _deepgramOptions = deepgramOptions;
        _diarizeEnabled = diarizeEnabled;
        _intentDetectionEnabled = intentDetectionEnabled;
        _intentDetectionOptions = intentDetectionOptions;
        _backgroundColorHex = backgroundColorHex;
        _intentColorHex = intentColorHex;
        _recordingOptions = recordingOptions;
        _logFolder = logFolder;
        _playbackFile = playbackFile;
        _playbackModeOverride = playbackModeOverride;
    }

    public async Task RunAsync()
    {
        // Handle Ctrl+C and SIGTERM to ensure clean shutdown
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent immediate termination
            Application.MainLoop?.Invoke(() => Application.RequestStop());
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Application.MainLoop?.Invoke(() => Application.RequestStop());
        };

        // Open debug log file
        Directory.CreateDirectory(_logFolder);
        var sessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        var sessionId = $"session-{sessionTimestamp}-{Environment.ProcessId}";
        var logFileName = Path.Combine(_logFolder, $"{sessionId}.log");
        _debugLogWriter = new StreamWriter(logFileName, append: false) { AutoFlush = true };

        // Parse background color and create color scheme
        var backgroundColor = ParseHexColor(_backgroundColorHex);
        var foregroundColor = Color.White;

        var colorScheme = new ColorScheme
        {
            Normal = Terminal.Gui.Attribute.Make(foregroundColor, backgroundColor),
            Focus = Terminal.Gui.Attribute.Make(foregroundColor, backgroundColor),
            HotNormal = Terminal.Gui.Attribute.Make(Color.BrightYellow, backgroundColor),
            HotFocus = Terminal.Gui.Attribute.Make(Color.BrightYellow, backgroundColor),
            Disabled = Terminal.Gui.Attribute.Make(Color.Gray, backgroundColor)
        };

        // Parse intent color and create intent color scheme
        var intentColor = ParseHexColor(_intentColorHex);
        _intentColorScheme = new ColorScheme
        {
            Normal = Terminal.Gui.Attribute.Make(intentColor, backgroundColor),
            Focus = Terminal.Gui.Attribute.Make(intentColor, backgroundColor),
            HotNormal = Terminal.Gui.Attribute.Make(intentColor, backgroundColor),
            HotFocus = Terminal.Gui.Attribute.Make(intentColor, backgroundColor),
            Disabled = Terminal.Gui.Attribute.Make(intentColor, backgroundColor)
        };

        // Highlight attribute for transcript: colored background so highlights are clearly visible
        _highlightAttr = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightYellow);

        // Create the main window
        var windowTitle = _playbackFile != null
            ? $"Interview Assist - PLAYBACK: {Path.GetFileName(_playbackFile)}"
            : "Interview Assist - Transcription & Detection";
        var mainWindow = new Window(windowTitle)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = colorScheme
        };

        // Calculate layout: Top section 70% height, bottom 30%
        // Top section split 60/40 left/right

        // Top container for left/right split
        var topContainer = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(70)
        };

        // Top panel - Transcript
        var transcriptFrame = new FrameView("Transcript")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(70)
        };

        _transcriptView = new HighlightTextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            NormalAttribute = colorScheme.Normal
        };
        transcriptFrame.Add(_transcriptView);

        // Intent panel - Below transcript
        _intentFrame = new FrameView("Detected Intents (0)")
        {
            X = 0,
            Y = Pos.Bottom(transcriptFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _intentView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = _intentColorScheme,
            ReadOnly = true,
            WordWrap = true
        };
        _intentFrame.Add(_intentView);

        topContainer.Add(transcriptFrame, _intentFrame);

        // Bottom panel - Debug output
        var bottomFrame = new FrameView("Debug")
        {
            X = 0,
            Y = Pos.Bottom(topContainer),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _debugView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false
        };
        bottomFrame.Add(_debugView);

        // Status bar at very bottom - different for playback vs live mode
        _recordingStatusItem = new StatusItem(Key.Null, "", null);
        _transcriptionStatusItem = new StatusItem(Key.Null, "", null);
        _detectionModeStatusItem = new StatusItem(Key.Null, "", null);
        _playbackStatusItem = new StatusItem(Key.Null, "PLAYING", null);

        StatusBar statusBar;
        if (_playbackFile != null)
        {
            statusBar = new StatusBar(new StatusItem[]
            {
                new StatusItem(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop()),
                new StatusItem(Key.Null, "~Space~ Pause/Resume", null),
                _playbackStatusItem,
                _detectionModeStatusItem
            });
        }
        else
        {
            statusBar = new StatusBar(new StatusItem[]
            {
                new StatusItem(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop()),
                new StatusItem(Key.S | Key.CtrlMask, "~Ctrl+S~ Stop", StopTranscription),
                new StatusItem(Key.R | Key.CtrlMask, "~Ctrl+R~ Record", ToggleRecording),
                _recordingStatusItem,
                _transcriptionStatusItem,
                _detectionModeStatusItem,
                new StatusItem(Key.Null, $"Audio: {_audioSource} | Diarize: {_diarizeEnabled}", null)
            });
        }

        mainWindow.Add(topContainer, bottomFrame);

        // Handle Space for playback pause/resume at the window level
        if (_playbackFile != null)
        {
            mainWindow.KeyPress += e =>
            {
                if (e.KeyEvent.Key == Key.Space)
                {
                    TogglePlaybackPause();
                    e.Handled = true;
                }
            };
        }

        Application.Top.Add(mainWindow, statusBar);

        // Start transcription or playback in background
        _cts = new CancellationTokenSource();
        _player.OnInfo += msg => Application.MainLoop?.Invoke(() => AddDebug($"[Playback] {msg}"));
        if (_playbackFile != null)
        {
            _ = Task.Run(() => StartPlaybackAsync(_cts.Token));
        }
        else
        {
            _ = Task.Run(() => StartTranscriptionAsync(_cts.Token));
        }

        // Run the UI
        Application.Run();

        // Cleanup
        _cts.Cancel();
        _recorder?.Stop();
        _audioRecorder?.Stop();
        _audioRecorder?.Dispose();
        _player.Stop();
        if (_deepgramService != null)
        {
            await _deepgramService.StopAsync();
            await _deepgramService.DisposeAsync();
        }
        _intentPipeline?.Dispose();
        _debugLogWriter?.Dispose();
    }

    private void TogglePlaybackPause()
    {
        if (_wavAudio != null)
        {
            _wavAudio.TogglePause();
            _playbackStatusItem.Title = _wavAudio.IsPaused ? "PAUSED" : "PLAYING";
            AddDebug($"[Pause] {(_wavAudio.IsPaused ? "Paused" : "Resumed")}");
        }
        else
        {
            _player.TogglePause();
            _playbackStatusItem.Title = _player.IsPaused ? "PAUSED" : "PLAYING";
            AddDebug($"[Pause] {(_player.IsPaused ? "Paused" : "Resumed")}");
        }
    }

    private void ToggleRecording()
    {
        if (_recorder == null || _intentPipeline == null) return;

        if (_recorder.IsRecording)
        {
            _recorder.Stop();
            _audioRecorder?.Stop();
            _recordingStatusItem.Title = "";
            AddDebug("Recording stopped");
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        if (_recorder == null || _intentPipeline == null || _recorder.IsRecording) return;

        var filePath = _recordingOptions.GenerateFilePath();
        var config = new SessionConfig
        {
            DeepgramModel = _deepgramOptions.Model,
            Diarize = _diarizeEnabled,
            IntentDetectionEnabled = _intentDetectionEnabled,
            IntentDetectionMode = _intentPipeline?.DetectionModeName,
            AudioSource = _audioSource.ToString(),
            SampleRate = _sampleRate
        };
        _recorder.Start(filePath, config);

        // Start audio file recording alongside the JSONL recording
        if (_audioRecorder != null)
        {
            var wavSessionId = SessionReportGenerator.ExtractSessionId(filePath);
            var wavPath = wavSessionId != null
                ? Path.Combine(Path.GetDirectoryName(filePath)!, $"{wavSessionId}.audio.wav")
                : Path.ChangeExtension(filePath, ".wav");
            _audioRecorder.Start(wavPath);
            AddDebug($"Audio recording to: {wavPath}");
        }

        _recordingStatusItem.Title = "REC";
        AddDebug($"Recording to: {filePath}");
    }

    private void StopTranscription()
    {
        if (!_isTranscribing) return;

        AddDebug("Stopping transcription...");

        // Stop recording first if active
        if (_recorder?.IsRecording == true)
        {
            _recorder.Stop();
            _audioRecorder?.Stop();
            _recordingStatusItem.Title = "";
        }

        // Cancel transcription
        _cts?.Cancel();
        _isTranscribing = false;
        _transcriptionStatusItem.Title = "STOPPED";
        AddDebug("Transcription stopped. Press Ctrl+Q to quit.");
    }

    private async Task StartPlaybackAsync(CancellationToken ct)
    {
        var extension = Path.GetExtension(_playbackFile!).ToLowerInvariant();
        if (extension == ".wav")
        {
            await StartWavPlaybackAsync(ct);
        }
        else
        {
            await StartJsonlPlaybackAsync(ct);
        }
    }

    private async Task StartJsonlPlaybackAsync(CancellationToken ct)
    {
        try
        {
            AddDebug($"Loading playback file: {_playbackFile}");

            await _player.LoadAsync(_playbackFile!, ct);

            // Use command-line override, then recorded mode, then fall back to current settings
            var recordedMode = _player.SessionConfig?.IntentDetectionMode;
            var effectiveMode = _playbackModeOverride ?? recordedMode;

            // Check if API key is available for modes that require one
            var requiresOpenAiKey = effectiveMode?.ToLowerInvariant() is "llm" or "parallel";
            var requiresDeepgramKey = effectiveMode?.ToLowerInvariant() is "deepgram";
            var hasOpenAiKey = !string.IsNullOrWhiteSpace(_intentDetectionOptions.Llm.ApiKey);
            var hasDeepgramKey = !string.IsNullOrWhiteSpace(_intentDetectionOptions.Deepgram.ApiKey);

            if (requiresOpenAiKey && !hasOpenAiKey)
            {
                AddDebug($"WARNING: {effectiveMode} mode requires OpenAI API key. Falling back to Heuristic mode.");
                AddDebug("Set OPENAI_API_KEY environment variable to use LLM/Parallel detection during playback.");
                effectiveMode = "Heuristic";
            }
            else if (requiresDeepgramKey && !hasDeepgramKey)
            {
                AddDebug($"WARNING: {effectiveMode} mode requires Deepgram API key. Falling back to Heuristic mode.");
                AddDebug("Set DEEPGRAM_API_KEY environment variable to use Deepgram detection during playback.");
                effectiveMode = "Heuristic";
            }

            var strategy = CreateDetectionStrategyForMode(effectiveMode);
            _intentPipeline = new UtteranceIntentPipeline(detectionStrategy: strategy);
            WireIntentPipelineEvents();

            var modeName = _intentPipeline.DetectionModeName;
            Application.MainLoop?.Invoke(() => _detectionModeStatusItem.Title = $"Mode: {modeName}");

            if (_playbackModeOverride != null)
                AddDebug($"Detection mode: {modeName} (override from --mode, recorded: {recordedMode ?? "not specified"})");
            else
                AddDebug($"Detection mode: {modeName} (from recording: {recordedMode ?? "not specified"})");

            // Wire playback events for transcript display
            _player.OnEventPlayed += evt =>
            {
                if (evt is RecordedAsrEvent asr && asr.Data.IsFinal)
                {
                    Application.MainLoop?.Invoke(() =>
                    {
                        if (_diarizeEnabled && asr.Data.SpeakerId != null)
                        {
                            var speaker = int.TryParse(asr.Data.SpeakerId, out var s) ? s : 0;
                            _currentSpeaker = speaker;
                        }
                        AddTranscript(asr.Data.Text + " ");
                    });
                }
            };

            AddDebug($"Starting playback ({_player.TotalEvents} events)...");

            await _player.PlayAsync(_intentPipeline, ct);
        }
        catch (OperationCanceledException)
        {
            AddDebug("Playback cancelled.");
        }
        catch (Exception ex)
        {
            AddDebug($"ERROR: {ex.Message}");
        }
    }

    private async Task StartWavPlaybackAsync(CancellationToken ct)
    {
        try
        {
            AddDebug($"Starting WAV playback: {_playbackFile}");

            // Create file-based audio source
            var wavAudio = new WavFileAudioSource(_playbackFile!, _sampleRate);
            _wavAudio = wavAudio;

            // Create Deepgram transcription service with WAV audio source
            _deepgramService = new DeepgramTranscriptionService(wavAudio, _deepgramOptions);

            // Create intent pipeline
            if (_intentDetectionEnabled)
            {
                var strategy = CreateDetectionStrategy();
                _intentPipeline = new UtteranceIntentPipeline(detectionStrategy: strategy);
                WireIntentPipelineEvents();

                var modeName = _intentPipeline.DetectionModeName;
                Application.MainLoop?.Invoke(() => _detectionModeStatusItem.Title = $"Mode: {modeName}");
                AddDebug($"Intent detection enabled (mode: {modeName})");

                // Create session recorder (attached to pipeline)
                _recorder = new SessionRecorder(_intentPipeline);
                _recorder.OnInfo += msg => Application.MainLoop?.Invoke(() => AddDebug($"[Recording] {msg}"));

                if (_recordingOptions.AutoStart)
                {
                    Application.MainLoop?.Invoke(() =>
                    {
                        StartRecording();
                        AddDebug("Recording auto-started (AutoStart=true)");
                    });
                }
            }

            // Wire Deepgram events (same as live transcription)
            WireDeepgramEvents();

            // When WAV playback completes, wait for final results then stop
            wavAudio.OnPlaybackComplete += () =>
            {
                _ = Task.Run(async () =>
                {
                    AddDebug("WAV playback complete, waiting for final results...");
                    await Task.Delay(2000);

                    // Stop recording if active
                    if (_recorder?.IsRecording == true)
                    {
                        Application.MainLoop?.Invoke(() =>
                        {
                            _recorder.Stop();
                            _recordingStatusItem.Title = "";
                            AddDebug("Recording stopped (playback complete)");
                        });
                    }

                    AddDebug("Stopping Deepgram...");
                    await _deepgramService.StopAsync();
                    AddDebug("WAV playback session finished. Press Ctrl+Q to quit.");
                });
            };

            AddDebug($"Connecting to Deepgram (model: {_deepgramOptions.Model})...");

            await _deepgramService.StartAsync(ct);
        }
        catch (OperationCanceledException)
        {
            AddDebug("WAV playback cancelled.");
        }
        catch (Exception ex)
        {
            AddDebug($"ERROR: {ex.Message}");
        }
    }

    private async Task StartTranscriptionAsync(CancellationToken ct)
    {
        try
        {
            _isTranscribing = true;
            Application.MainLoop?.Invoke(() => _transcriptionStatusItem.Title = "RUNNING");

            AddDebug("Starting audio capture...");

            var audio = new WindowsAudioCaptureService(_sampleRate, _audioSource);

            // Create audio file recorder if configured
            if (_recordingOptions.SaveAudio)
            {
                _audioRecorder = new AudioFileRecorder(audio, _sampleRate);
            }

            _deepgramService = new DeepgramTranscriptionService(audio, _deepgramOptions);

            // Create intent pipeline if enabled
            if (_intentDetectionEnabled)
            {
                var strategy = CreateDetectionStrategy();
                _intentPipeline = new UtteranceIntentPipeline(detectionStrategy: strategy);
                WireIntentPipelineEvents();
                var modeName = _intentPipeline.DetectionModeName;
                Application.MainLoop?.Invoke(() => _detectionModeStatusItem.Title = $"Mode: {modeName}");
                AddDebug($"Intent detection enabled (mode: {modeName})");

                // Create session recorder (attached to pipeline)
                _recorder = new SessionRecorder(_intentPipeline);
                _recorder.OnInfo += msg => Application.MainLoop?.Invoke(() => AddDebug($"[Recording] {msg}"));

                // Auto-start recording if configured
                if (_recordingOptions.AutoStart)
                {
                    Application.MainLoop?.Invoke(() =>
                    {
                        StartRecording();
                        AddDebug("Recording auto-started (AutoStart=true)");
                    });
                }
                else
                {
                    AddDebug("Recording available (Ctrl+R to start)");
                }
            }

            WireDeepgramEvents();

            AddDebug($"Connecting to Deepgram (model: {_deepgramOptions.Model})...");

            await _deepgramService.StartAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _isTranscribing = false;
            Application.MainLoop?.Invoke(() => _transcriptionStatusItem.Title = "STOPPED");
            AddDebug("Transcription stopped.");
        }
        catch (Exception ex)
        {
            _isTranscribing = false;
            Application.MainLoop?.Invoke(() => _transcriptionStatusItem.Title = "ERROR");
            AddDebug($"ERROR: {ex.Message}");
        }
    }

    private void WireDeepgramEvents()
    {
        if (_deepgramService == null) return;

        // Note: provisionals are NOT forwarded to the pipeline — they cause duplicate
        // utterances from progressive refinements. Stables (from endpointing +
        // PromoteProvisionalToStable) already cover all confirmed text.
        _deepgramService.OnStableText += args =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                // Track speaker for intent panel display
                if (_diarizeEnabled && args.Speaker.HasValue)
                {
                    _currentSpeaker = args.Speaker;
                }

                AddTranscript(args.Text + " ");
            });

            // Feed to intent pipeline
            EnqueuePipelineEvent(pipeline => pipeline.ProcessAsrEvent(new AsrEvent
            {
                Text = args.Text,
                IsFinal = true,
                SpeakerId = args.Speaker?.ToString()
            }));
        };

        _deepgramService.OnInfo += msg =>
        {
            // Signal utterance end to intent pipeline
            if (msg.Contains("Utterance end"))
            {
                EnqueuePipelineEvent(pipeline => pipeline.SignalUtteranceEnd());
            }

            Application.MainLoop?.Invoke(() =>
            {
                AddDebug($"[Info] {msg}");
            });
        };

        _deepgramService.OnWarning += msg =>
        {
            Application.MainLoop?.Invoke(() => AddDebug($"[Warn] {msg}"));
        };

        _deepgramService.OnError += ex =>
        {
            Application.MainLoop?.Invoke(() => AddDebug($"[Error] {ex.Message}"));
        };
    }

    private void EnqueuePipelineEvent(Action<UtteranceIntentPipeline> action)
    {
        void Run()
        {
            var pipeline = _intentPipeline;
            if (pipeline == null) return;

            lock (_pipelineGate)
            {
                action(pipeline);
            }
        }

        if (Application.MainLoop != null)
        {
            Application.MainLoop.Invoke(Run);
        }
        else
        {
            Run();
        }
    }

    private void WireIntentPipelineEvents()
    {
        if (_intentPipeline == null) return;

        _intentPipeline.OnIntentCandidate += evt =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                AddDebug($"[Intent.candidate] {evt.Intent.Type}/{evt.Intent.Subtype} conf={evt.Intent.Confidence:F2}");
            });
        };

        _intentPipeline.OnIntentFinal += evt =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                AddDebug($"[Intent.final] {evt.Intent.Type}/{evt.Intent.Subtype} conf={evt.Intent.Confidence:F2}");

                // Display detected questions and imperatives in the intent list
                if (evt.Intent.Type is IntentType.Question or IntentType.Imperative)
                {
                    var subtypeLabel = FormatSubtypeLabel(evt.Intent.Subtype);

                    // Include speaker if available
                    var speakerPrefix = _currentSpeaker.HasValue
                        ? $"Speaker {_currentSpeaker.Value} | "
                        : "";

                    // Show confidence for quality evaluation
                    var confidenceLabel = FormatConfidenceLabel(evt.Intent.Confidence);

                    // Add to intent list with header and indented text fields
                    _intentCount++;
                    _intentFrame.Title = $"Detected Intents ({_intentCount})";
                    AddIntent($"[{speakerPrefix}{subtypeLabel} | {confidenceLabel}]");
                    AddIntent($"  Reformulated: {evt.Intent.SourceText}");
                    var originalText = evt.Intent.OriginalText;
                    AddIntent($"  Original:     {originalText ?? "(not available)"}");
                    AddIntent("");

                    AddTranscriptHighlight(evt.UtteranceId, evt.Intent.SourceText, evt.Intent.OriginalText);
                }
            });
        };

        _intentPipeline.OnUtteranceFinal += evt =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                // Record utterance position in transcript for highlight mapping
                var transcript = _transcriptView.Text;
                if (!string.IsNullOrEmpty(evt.StableText) && !string.IsNullOrEmpty(transcript))
                {
                    // Search forward from last known position (avoids matching earlier similar text)
                    var searchFrom = Math.Min(_utteranceSearchFrom, transcript.Length);
                    var idx = transcript.IndexOf(evt.StableText, searchFrom, StringComparison.OrdinalIgnoreCase);
                    // Fallback: search from beginning if forward scan failed
                    if (idx < 0)
                        idx = transcript.LastIndexOf(evt.StableText, StringComparison.OrdinalIgnoreCase);

                    if (idx >= 0)
                    {
                        var endPos = idx + evt.StableText.Length;
                        _utterancePositions[evt.Id] = (idx, endPos);
                        _utterancePositionList.Add((evt.Id, idx, endPos, evt.StableText));
                        _utteranceSearchFrom = endPos;
                        AddDebug($"[Position] {evt.Id}: [{idx}..{endPos}] \"{Truncate(evt.StableText, 40)}\"");
                    }
                    else
                    {
                        // Position not found — text may not be in transcript yet; pending highlights will retry
                    }
                }

                AddDebug($"[Utterance.final] {evt.Id}: \"{Truncate(evt.StableText, 40)}\" ({evt.CloseReason})");

                // Try to resolve pending highlights now that we have a new position
                ResolvePendingHighlights();
            });
        };

        _intentPipeline.OnActionTriggered += evt =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                AddDebug($"[Action] {evt.ActionName} (debounced={evt.WasDebounced})");
            });
        };

        _intentPipeline.OnIntentCorrected += evt =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                var originalType = evt.OriginalIntent?.Type.ToString() ?? "None";
                var correctedType = evt.CorrectedIntent.Type.ToString();
                AddDebug($"[Intent.corrected] {evt.CorrectionType}: {originalType} → {correctedType}");

                // Handle corrections that result in questions or imperatives
                if (evt.CorrectedIntent.Type is IntentType.Question or IntentType.Imperative)
                {
                    var subtypeLabel = FormatSubtypeLabel(evt.CorrectedIntent.Subtype);

                    var prefix = evt.CorrectionType switch
                    {
                        IntentCorrectionType.Added => "LLM added",
                        IntentCorrectionType.TypeChanged => "LLM reclassified",
                        _ => "LLM"
                    };

                    _intentCount++;
                    _intentFrame.Title = $"Detected Intents ({_intentCount})";
                    AddIntent($"[{prefix} | {subtypeLabel}]");
                    AddIntent($"  Reformulated: {evt.CorrectedIntent.SourceText}");
                    var corrOriginalText = evt.CorrectedIntent.OriginalText;
                    AddIntent($"  Original:     {corrOriginalText ?? "(not available)"}");
                    AddIntent("");

                    AddTranscriptHighlight(evt.UtteranceId, evt.CorrectedIntent.SourceText, evt.CorrectedIntent.OriginalText);
                }
            });
        };
    }

    private void AddTranscript(string text)
    {
        _transcriptView.AppendText(text);
        _transcriptCharCount += text.Length;
        if (_pendingHighlights.Count > 0)
            ResolvePendingHighlights();
    }

    private void AddTranscriptHighlight(string utteranceId, string? sourceText, string? originalText)
    {
        var transcript = _transcriptView.Text ?? "";

        int start = -1;
        int end = -1;
        string matchMethod = "";

        // First: try utterance position lookup by exact ID
        if (_utterancePositions.TryGetValue(utteranceId, out var range))
        {
            start = range.Start;
            end = range.End;
            matchMethod = "id-lookup";
        }

        // Fallback 2: find the best matching recorded utterance by text similarity
        if (start < 0 && !string.IsNullOrWhiteSpace(originalText))
        {
            var bestMatch = FindBestUtteranceMatch(originalText);
            if (bestMatch.HasValue)
            {
                start = bestMatch.Value.Start;
                end = bestMatch.Value.End;
                matchMethod = "utterance-text-match";
            }
        }

        // Fallback 3: direct text search with originalText then sourceText
        if (start < 0)
        {
            if (!string.IsNullOrWhiteSpace(originalText))
            {
                var idx = transcript.LastIndexOf(originalText, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) { start = idx; end = idx + originalText.Length; matchMethod = "originalText-search"; }
            }

            if (start < 0 && !string.IsNullOrWhiteSpace(sourceText))
            {
                var idx = transcript.LastIndexOf(sourceText, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) { start = idx; end = idx + sourceText.Length; matchMethod = "sourceText-search"; }
            }
        }

        if (start < 0)
        {
            // Text not in transcript yet — queue for later resolution
            _pendingHighlights.Add((utteranceId, sourceText, originalText));
            AddDebug($"[Highlight] Queued pending for {utteranceId} (text not in transcript yet)");
            return;
        }

        var highlightId = _nextHighlightId++;
        AddDebug($"[Highlight] Added #{highlightId} for {utteranceId} [{start}..{end}] via {matchMethod}");
        _transcriptView.AddHighlight(new HighlightRegion
        {
            Id = highlightId,
            Start = start,
            End = end,
            Attr = _highlightAttr
        });
    }

    private (int Start, int End)? FindBestUtteranceMatch(string intentText)
    {
        var intentWords = intentText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant().Trim('.', '?', '!', ','))
            .Where(w => w.Length > 2)
            .ToHashSet();

        if (intentWords.Count == 0) return null;

        (int Start, int End)? bestMatch = null;
        double bestScore = 0;

        foreach (var pos in _utterancePositionList)
        {
            var uttWords = pos.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLowerInvariant().Trim('.', '?', '!', ','))
                .Where(w => w.Length > 2)
                .ToHashSet();

            if (uttWords.Count == 0) continue;

            var overlap = intentWords.Intersect(uttWords).Count();
            var score = (double)overlap / Math.Max(intentWords.Count, uttWords.Count);

            if (score > bestScore && score >= 0.4)
            {
                bestScore = score;
                bestMatch = (pos.Start, pos.End);
            }
        }

        return bestMatch;
    }

    private void ResolvePendingHighlights()
    {
        if (_pendingHighlights.Count == 0) return;

        var resolved = new List<int>();
        for (int i = 0; i < _pendingHighlights.Count; i++)
        {
            var (utteranceId, sourceText, originalText) = _pendingHighlights[i];

            int start = -1;
            int end = -1;
            string matchMethod = "";

            // Try utterance position lookup by exact ID
            if (_utterancePositions.TryGetValue(utteranceId, out var range))
            {
                start = range.Start;
                end = range.End;
                matchMethod = "deferred-id-lookup";
            }

            // Try text similarity match against recorded utterances
            if (start < 0 && !string.IsNullOrWhiteSpace(originalText))
            {
                var bestMatch = FindBestUtteranceMatch(originalText);
                if (bestMatch.HasValue)
                {
                    start = bestMatch.Value.Start;
                    end = bestMatch.Value.End;
                    matchMethod = "deferred-utterance-text-match";
                }
            }

            // Try direct transcript search
            if (start < 0)
            {
                var transcript = _transcriptView.Text;
                if (!string.IsNullOrWhiteSpace(originalText))
                {
                    var idx = transcript.LastIndexOf(originalText, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0) { start = idx; end = idx + originalText.Length; matchMethod = "deferred-originalText-search"; }
                }
                if (start < 0 && !string.IsNullOrWhiteSpace(sourceText))
                {
                    var idx = transcript.LastIndexOf(sourceText, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0) { start = idx; end = idx + sourceText.Length; matchMethod = "deferred-sourceText-search"; }
                }
            }

            if (start >= 0)
            {
                var highlightId = _nextHighlightId++;
                AddDebug($"[Highlight] Resolved pending #{highlightId} for {utteranceId} [{start}..{end}] via {matchMethod}");
                _transcriptView.AddHighlight(new HighlightRegion
                {
                    Id = highlightId,
                    Start = start,
                    End = end,
                    Attr = _highlightAttr
                });
                resolved.Add(i);
            }
        }

        // Remove resolved items (reverse order to preserve indices)
        for (int i = resolved.Count - 1; i >= 0; i--)
            _pendingHighlights.RemoveAt(resolved[i]);
    }

    private void AddIntent(string intent)
    {
        _intentLines.Add(intent);

        _intentView.Text = string.Join("\n", _intentLines);

        // Auto-scroll to bottom
        _intentView.MoveEnd();
    }

    private void AddDebug(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] {message}";

        _debugLines.Add(line);

        // Keep only last 100 lines
        while (_debugLines.Count > 100)
        {
            _debugLines.RemoveAt(0);
        }

        _debugView.Text = string.Join("\n", _debugLines);

        // Auto-scroll to bottom
        _debugView.MoveEnd();

        // Write to log file
        try { _debugLogWriter?.WriteLine(line); } catch { /* ignore write errors */ }
    }

    private static string FormatSubtypeLabel(IntentSubtype? subtype) => subtype switch
    {
        IntentSubtype.Definition => "Asking for definition",
        IntentSubtype.HowTo => "Asking how-to",
        IntentSubtype.Compare => "Asking to compare",
        IntentSubtype.Troubleshoot => "Troubleshooting",
        IntentSubtype.Stop => "Stop command",
        IntentSubtype.Repeat => "Repeat request",
        IntentSubtype.Continue => "Continue request",
        IntentSubtype.StartOver => "Start over request",
        IntentSubtype.Generate => "Generate request",
        _ => "General question"
    };

    private static string FormatConfidenceLabel(double confidence) => confidence switch
    {
        >= 0.9 => $"High confidence ({confidence:F1})",
        >= 0.7 => $"Medium confidence ({confidence:F1})",
        _ => $"Low confidence ({confidence:F1})"
    };

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }

    private IIntentDetectionStrategy? CreateDetectionStrategy()
    {
        if (!_intentDetectionEnabled || !_intentDetectionOptions.Enabled)
            return null;

        return _intentDetectionOptions.Mode switch
        {
            IntentDetectionMode.Heuristic => new HeuristicIntentStrategy(_intentDetectionOptions.Heuristic),
            IntentDetectionMode.Llm => CreateLlmStrategy(),
            IntentDetectionMode.Parallel => CreateParallelStrategy(),
            IntentDetectionMode.Deepgram => CreateDeepgramStrategy(),
            _ => new HeuristicIntentStrategy(_intentDetectionOptions.Heuristic)
        };
    }

    private IIntentDetectionStrategy? CreateDetectionStrategyForMode(string? recordedMode)
    {
        // Parse the recorded mode string, fall back to current settings if not specified
        var mode = recordedMode?.ToLowerInvariant() switch
        {
            "heuristic" => IntentDetectionMode.Heuristic,
            "llm" => IntentDetectionMode.Llm,
            "parallel" => IntentDetectionMode.Parallel,
            "deepgram" => IntentDetectionMode.Deepgram,
            _ => _intentDetectionOptions.Mode // Fall back to current settings
        };

        return mode switch
        {
            IntentDetectionMode.Heuristic => new HeuristicIntentStrategy(_intentDetectionOptions.Heuristic),
            IntentDetectionMode.Llm => CreateLlmStrategy(),
            IntentDetectionMode.Parallel => CreateParallelStrategy(),
            IntentDetectionMode.Deepgram => CreateDeepgramStrategy(),
            _ => new HeuristicIntentStrategy(_intentDetectionOptions.Heuristic)
        };
    }

    private string LoadSystemPrompt()
    {
        var text = _intentDetectionOptions.Llm.LoadSystemPrompt();
        AddDebug($"[Config] Loaded system prompt from {_intentDetectionOptions.Llm.SystemPromptFile} ({text.Length} chars)");
        return text;
    }

    private LlmIntentStrategy CreateLlmStrategy()
    {
        var apiKey = _intentDetectionOptions.Llm.ApiKey
            ?? throw new InvalidOperationException("OpenAI API key is required for LLM detection mode. Set OPENAI_API_KEY environment variable.");

        var systemPrompt = LoadSystemPrompt();

        var detector = new OpenAiIntentDetector(
            apiKey,
            _intentDetectionOptions.Llm.Model,
            _intentDetectionOptions.Llm.ConfidenceThreshold,
            systemPrompt);

        WireLlmRequestLogging(detector);

        return new LlmIntentStrategy(detector, _intentDetectionOptions.Llm);
    }

    private ParallelIntentStrategy CreateParallelStrategy()
    {
        var apiKey = _intentDetectionOptions.Llm.ApiKey
            ?? throw new InvalidOperationException("OpenAI API key is required for Parallel detection mode. Set OPENAI_API_KEY environment variable.");

        var systemPrompt = LoadSystemPrompt();

        var llmDetector = new OpenAiIntentDetector(
            apiKey,
            _intentDetectionOptions.Llm.Model,
            _intentDetectionOptions.Llm.ConfidenceThreshold,
            systemPrompt);

        WireLlmRequestLogging(llmDetector);

        return new ParallelIntentStrategy(
            llmDetector,
            _intentDetectionOptions.Heuristic,
            _intentDetectionOptions.Llm);
    }

    private LlmIntentStrategy CreateDeepgramStrategy()
    {
        var apiKey = _intentDetectionOptions.Deepgram.ApiKey
            ?? throw new InvalidOperationException("Deepgram API key is required for Deepgram detection mode. Set DEEPGRAM_API_KEY environment variable.");

        var detector = new DeepgramIntentDetector(apiKey, _intentDetectionOptions.Deepgram);

        // Reuse LlmIntentStrategy with Deepgram as the detector backend.
        // LLM options control buffering, rate limiting, triggers, and deduplication.
        return new LlmIntentStrategy(detector, _intentDetectionOptions.Llm);
    }

    private void WireLlmRequestLogging(OpenAiIntentDetector detector)
    {
        // Log system prompt once to file
        try
        {
            _debugLogWriter?.WriteLine("═══ LLM System Prompt ═══");
            _debugLogWriter?.WriteLine(detector.SystemPrompt);
            _debugLogWriter?.WriteLine("═══ End System Prompt ═══");
            _debugLogWriter?.WriteLine();
        }
        catch { /* ignore write errors */ }

        detector.OnRequestSending += (userMessage) =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                AddDebug("─── LLM Request ───");
                AddDebug($"[User Message]\n{userMessage}");
            });
        };

        detector.OnRequestCompleted += (elapsedMs) =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                AddDebug($"[LLM completed in {elapsedMs}ms]");
                AddDebug("─── End Request ───");
            });
        };
    }

    private static Color ParseHexColor(string hex)
    {
        // Remove # prefix if present
        hex = hex.TrimStart('#');

        if (hex.Length != 6)
            return Color.Black;

        try
        {
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);

            // Terminal.Gui 1.x uses Color enum - map RGB to nearest color
            // For very dark colors (like #1E1E1E = 30,30,30), use Black
            // This is the best approximation in 16-color mode
            var brightness = (r + g + b) / 3;

            if (brightness < 50)
                return Color.Black;
            if (brightness < 100)
                return Color.DarkGray;
            if (brightness < 160)
                return Color.Gray;
            return Color.White;
        }
        catch
        {
            return Color.Black;
        }
    }
}
