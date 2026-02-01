using InterviewAssist.Audio.Windows;
using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Pipeline.Utterance;
using InterviewAssist.Library.Transcription;
using Microsoft.Extensions.Configuration;
using Terminal.Gui;

namespace InterviewAssist.TranscriptionDetectionConsole;

public partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        // Get Deepgram API key
        var deepgramApiKey = GetFirstNonEmpty(
            configuration["Deepgram:ApiKey"],
            Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY"));

        if (string.IsNullOrWhiteSpace(deepgramApiKey))
        {
            Console.WriteLine("Error: Deepgram API key not configured.");
            Console.WriteLine("Set DEEPGRAM_API_KEY environment variable or add Deepgram:ApiKey to appsettings.json/user secrets.");
            return 1;
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

        // Initialize Terminal.Gui
        Application.Init();

        try
        {
            // Create the main UI and run
            var app = new TranscriptionApp(source, sampleRate, deepgramOptions, diarizeEnabled, intentDetectionEnabled, backgroundColorHex, intentColorHex);
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
    private readonly string _backgroundColorHex;
    private readonly string _intentColorHex;

    // UI elements
    private TextView _transcriptView = null!;
    private Label _intentLabel = null!;
    private TextView _debugView = null!;

    // State
    private readonly List<string> _debugLines = new();
    private readonly List<string> _intentLines = new();
    private int? _currentSpeaker;
    private CancellationTokenSource? _cts;
    private DeepgramTranscriptionService? _deepgramService;
    private UtteranceIntentPipeline? _intentPipeline;
    private ColorScheme? _intentColorScheme;

    public TranscriptionApp(
        AudioInputSource audioSource,
        int sampleRate,
        DeepgramOptions deepgramOptions,
        bool diarizeEnabled,
        bool intentDetectionEnabled,
        string backgroundColorHex,
        string intentColorHex)
    {
        _audioSource = audioSource;
        _sampleRate = sampleRate;
        _deepgramOptions = deepgramOptions;
        _diarizeEnabled = diarizeEnabled;
        _intentDetectionEnabled = intentDetectionEnabled;
        _backgroundColorHex = backgroundColorHex;
        _intentColorHex = intentColorHex;
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
        var mainWindow = new Window("Interview Assist - Transcription & Detection")
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

        _intentLabel = new Label()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = _intentColorScheme,
            Text = ""
        };
        intentFrame.Add(_intentLabel);

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

        // Status bar at very bottom
        var statusBar = new StatusBar(new StatusItem[]
        {
            new StatusItem(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop()),
            new StatusItem(Key.Null, $"Audio: {_audioSource} | Diarize: {_diarizeEnabled} | Intent: {_intentDetectionEnabled}", null)
        });

        mainWindow.Add(topContainer, bottomFrame);
        Application.Top.Add(mainWindow, statusBar);

        // Start transcription in background
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => StartTranscriptionAsync(_cts.Token));

        // Run the UI
        Application.Run();

        // Cleanup
        _cts.Cancel();
        if (_deepgramService != null)
        {
            await _deepgramService.StopAsync();
            await _deepgramService.DisposeAsync();
        }
        _intentPipeline?.Dispose();
    }

    private async Task StartTranscriptionAsync(CancellationToken ct)
    {
        try
        {
            AddDebug("Starting audio capture...");

            var audio = new WindowsAudioCaptureService(_sampleRate, _audioSource);
            _deepgramService = new DeepgramTranscriptionService(audio, _deepgramOptions);

            // Create intent pipeline if enabled
            if (_intentDetectionEnabled)
            {
                _intentPipeline = new UtteranceIntentPipeline();
                WireIntentPipelineEvents();
                AddDebug("Intent detection enabled");
            }

            WireDeepgramEvents();

            AddDebug($"Connecting to Deepgram (model: {_deepgramOptions.Model})...");

            await _deepgramService.StartAsync(ct);
        }
        catch (OperationCanceledException)
        {
            AddDebug("Transcription stopped.");
        }
        catch (Exception ex)
        {
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
                        ? $"Speaker {_currentSpeaker.Value} "
                        : "";

                    var intentMarker = $"[{speakerPrefix}{subtypeLabel}]";

                    // Add to intent list
                    AddIntent($"{intentMarker} {evt.Intent.SourceText}");
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

        _intentLabel.Text = string.Join("\n", _intentLines);
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
