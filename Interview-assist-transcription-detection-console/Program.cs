using InterviewAssist.Audio.Windows;
using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Pipeline.Detection;
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
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--playback" && i + 1 < args.Length)
            {
                playbackFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--help" || args[i] == "-h")
            {
                Console.WriteLine("Interview Assist - Transcription & Detection Console");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  dotnet run                         Normal mode (live audio)");
                Console.WriteLine("  dotnet run -- --playback <file>    Playback mode (from recorded session)");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --playback <file>    Play back a recorded session file (.jsonl)");
                Console.WriteLine("  --help, -h           Show this help message");
                Console.WriteLine();
                Console.WriteLine("Keyboard shortcuts in normal mode:");
                Console.WriteLine("  Ctrl+S    Stop transcription");
                Console.WriteLine("  Ctrl+R    Toggle recording (or use AutoStart in appsettings.json)");
                Console.WriteLine("  Ctrl+Q    Quit");
                return 0;
            }
        }

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // In playback mode, Deepgram API key is not required
        string? deepgramApiKey = null;
        if (playbackFile == null)
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
            intentDetectionOptions.Mode != IntentDetectionMode.Heuristic &&
            string.IsNullOrWhiteSpace(intentDetectionOptions.Llm.ApiKey))
        {
            Console.WriteLine($"Error: OpenAI API key required for {intentDetectionOptions.Mode} detection mode.");
            Console.WriteLine("Set OPENAI_API_KEY environment variable or add IntentDetection:Llm:ApiKey to appsettings.json.");
            Console.WriteLine("Alternatively, set Mode to \"Heuristic\" to use free regex-based detection.");
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
            Diarize = diarizeEnabled
        };

        // Load UI settings
        var uiConfig = configuration.GetSection("UI");
        var backgroundColorHex = uiConfig["BackgroundColor"] ?? "#1E1E1E";
        var intentColorHex = uiConfig["IntentColor"] ?? "#FFFF00";

        // Load recording settings
        var recordingConfig = configuration.GetSection("Recording");
        var recordingOptions = new RecordingOptions
        {
            Folder = recordingConfig["Folder"] ?? "recordings",
            FileNamePattern = recordingConfig["FileNamePattern"] ?? "session-{timestamp}.jsonl",
            AutoStart = recordingConfig.GetValue("AutoStart", false)
        };

        // Initialize Terminal.Gui
        Application.Init();

        try
        {
            // Create the main UI and run
            var app = new TranscriptionApp(
                source, sampleRate, deepgramOptions, diarizeEnabled, intentDetectionEnabled,
                intentDetectionOptions, backgroundColorHex, intentColorHex, recordingOptions, playbackFile);
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
            _ => IntentDetectionMode.Heuristic
        };

        var heuristicConfig = intentConfig.GetSection("Heuristic");
        var llmConfig = intentConfig.GetSection("Llm");

        // Get OpenAI API key for LLM modes
        string? openAiApiKey = null;
        if (mode != IntentDetectionMode.Heuristic)
        {
            openAiApiKey = GetFirstNonEmpty(
                llmConfig["ApiKey"],
                rootConfig["OpenAI:ApiKey"],
                Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
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
                DeduplicationWindowMs = llmConfig.GetValue("DeduplicationWindowMs", 30000)
            }
        };
    }
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
    private readonly string? _playbackFile;

    // UI elements
    private TextView _transcriptView = null!;
    private TextView _intentView = null!;
    private TextView _debugView = null!;
    private StatusItem _recordingStatusItem = null!;

    // State
    private readonly List<string> _debugLines = new();
    private readonly List<string> _intentLines = new();
    private int? _currentSpeaker;
    private CancellationTokenSource? _cts;
    private DeepgramTranscriptionService? _deepgramService;
    private UtteranceIntentPipeline? _intentPipeline;
    private ColorScheme? _intentColorScheme;
    private SessionRecorder? _recorder;
    private SessionPlayer? _player;
    private bool _isTranscribing;
    private StatusItem _transcriptionStatusItem = null!;
    private StatusItem _detectionModeStatusItem = null!;

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
        string? playbackFile)
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
        _playbackFile = playbackFile;
    }

    public async Task RunAsync()
    {
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

        _transcriptView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true
        };
        transcriptFrame.Add(_transcriptView);

        // Intent panel - Below transcript
        var intentFrame = new FrameView("Detected Intents")
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
        intentFrame.Add(_intentView);

        topContainer.Add(transcriptFrame, intentFrame);

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

        StatusBar statusBar;
        if (_playbackFile != null)
        {
            statusBar = new StatusBar(new StatusItem[]
            {
                new StatusItem(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop()),
                new StatusItem(Key.Null, "PLAYBACK MODE", null),
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
        Application.Top.Add(mainWindow, statusBar);

        // Start transcription or playback in background
        _cts = new CancellationTokenSource();
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
        _player?.Stop();
        if (_deepgramService != null)
        {
            await _deepgramService.StopAsync();
            await _deepgramService.DisposeAsync();
        }
        _intentPipeline?.Dispose();
    }

    private void ToggleRecording()
    {
        if (_recorder == null || _intentPipeline == null) return;

        if (_recorder.IsRecording)
        {
            _recorder.Stop();
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
        try
        {
            AddDebug($"Loading playback file: {_playbackFile}");

            _player = new SessionPlayer();
            _player.OnInfo += msg => Application.MainLoop?.Invoke(() => AddDebug($"[Playback] {msg}"));

            await _player.LoadAsync(_playbackFile!, ct);

            // Create intent pipeline with detection strategy
            var strategy = CreateDetectionStrategy();
            _intentPipeline = new UtteranceIntentPipeline(detectionStrategy: strategy);
            WireIntentPipelineEvents();

            var modeName = _intentPipeline.DetectionModeName;
            Application.MainLoop?.Invoke(() => _detectionModeStatusItem.Title = $"Mode: {modeName}");
            AddDebug($"Detection mode: {modeName}");

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
                            if (speaker != _currentSpeaker)
                            {
                                _currentSpeaker = speaker;
                                AddTranscript($"\n[Speaker {speaker}] ");
                            }
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

    private async Task StartTranscriptionAsync(CancellationToken ct)
    {
        try
        {
            _isTranscribing = true;
            Application.MainLoop?.Invoke(() => _transcriptionStatusItem.Title = "RUNNING");

            AddDebug("Starting audio capture...");

            var audio = new WindowsAudioCaptureService(_sampleRate, _audioSource);
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

        _deepgramService.OnStableText += args =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                // Handle speaker change for diarization
                if (_diarizeEnabled && args.Speaker.HasValue && args.Speaker != _currentSpeaker)
                {
                    _currentSpeaker = args.Speaker;
                    AddTranscript($"\n[Speaker {args.Speaker.Value}] ");
                }

                AddTranscript(args.Text + " ");

                // Feed to intent pipeline
                _intentPipeline?.ProcessAsrEvent(new AsrEvent
                {
                    Text = args.Text,
                    IsFinal = true,
                    SpeakerId = args.Speaker?.ToString()
                });
            });
        };

        _deepgramService.OnProvisionalText += args =>
        {
            // Feed provisional to intent pipeline for early detection (no display)
            if (!string.IsNullOrWhiteSpace(args.Text))
            {
                _intentPipeline?.ProcessAsrEvent(new AsrEvent
                {
                    Text = args.Text,
                    IsFinal = false,
                    SpeakerId = args.Speaker?.ToString()
                });
            }
        };

        _deepgramService.OnInfo += msg =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                // Signal utterance end to intent pipeline
                if (msg.Contains("Utterance end"))
                {
                    _intentPipeline?.SignalUtteranceEnd();
                }

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

                // Display detected questions in the intent list
                if (evt.Intent.Type == IntentType.Question)
                {
                    var subtypeLabel = evt.Intent.Subtype switch
                    {
                        IntentSubtype.Definition => "Definition",
                        IntentSubtype.HowTo => "How-To",
                        IntentSubtype.Compare => "Compare",
                        IntentSubtype.Troubleshoot => "Troubleshoot",
                        _ => "Question"
                    };

                    // Include speaker if available
                    var speakerPrefix = _currentSpeaker.HasValue
                        ? $"S{_currentSpeaker.Value} "
                        : "";

                    // Show confidence for quality evaluation
                    var confidence = evt.Intent.Confidence.ToString("F1");

                    // Add to intent list
                    AddIntent($"[{speakerPrefix}{subtypeLabel} {confidence}] {evt.Intent.SourceText}");
                }
            });
        };

        _intentPipeline.OnUtteranceFinal += evt =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                AddDebug($"[Utterance.final] {evt.Id}: \"{Truncate(evt.StableText, 40)}\" ({evt.CloseReason})");
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

                // Handle corrections that result in questions
                if (evt.CorrectedIntent.Type == IntentType.Question)
                {
                    var subtypeLabel = evt.CorrectedIntent.Subtype switch
                    {
                        IntentSubtype.Definition => "Definition",
                        IntentSubtype.HowTo => "How-To",
                        IntentSubtype.Compare => "Compare",
                        IntentSubtype.Troubleshoot => "Troubleshoot",
                        _ => "Question"
                    };

                    var prefix = evt.CorrectionType switch
                    {
                        IntentCorrectionType.Added => "[LLM+]",      // LLM found something heuristic missed
                        IntentCorrectionType.TypeChanged => "[LLM~]", // LLM corrected the type
                        _ => "[LLM]"
                    };

                    AddIntent($"{prefix} [{subtypeLabel}] {evt.CorrectedIntent.SourceText}");
                }
            });
        };
    }

    private void AddTranscript(string text)
    {
        // Append to existing text
        var existingText = _transcriptView.Text?.ToString() ?? "";
        _transcriptView.Text = existingText + text;

        // Auto-scroll to bottom
        _transcriptView.MoveEnd();
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
    }

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
            _ => new HeuristicIntentStrategy(_intentDetectionOptions.Heuristic)
        };
    }

    private LlmIntentStrategy CreateLlmStrategy()
    {
        var apiKey = _intentDetectionOptions.Llm.ApiKey
            ?? throw new InvalidOperationException("OpenAI API key is required for LLM detection mode. Set OPENAI_API_KEY environment variable.");

        var detector = new OpenAiIntentDetector(
            apiKey,
            _intentDetectionOptions.Llm.Model,
            _intentDetectionOptions.Llm.ConfidenceThreshold);

        return new LlmIntentStrategy(detector, _intentDetectionOptions.Llm);
    }

    private ParallelIntentStrategy CreateParallelStrategy()
    {
        var apiKey = _intentDetectionOptions.Llm.ApiKey
            ?? throw new InvalidOperationException("OpenAI API key is required for Parallel detection mode. Set OPENAI_API_KEY environment variable.");

        var llmDetector = new OpenAiIntentDetector(
            apiKey,
            _intentDetectionOptions.Llm.Model,
            _intentDetectionOptions.Llm.ConfidenceThreshold);

        return new ParallelIntentStrategy(
            llmDetector,
            _intentDetectionOptions.Heuristic,
            _intentDetectionOptions.Llm);
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
