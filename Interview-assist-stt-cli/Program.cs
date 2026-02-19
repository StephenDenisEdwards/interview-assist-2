using System.Text.Json;
using InterviewAssist.Audio.Windows;
using InterviewAssist.Library.Audio;
using InterviewAssist.Library.Transcription;

namespace InterviewAssist.SttCli;

public static class Program
{
    private sealed record StableChunk(
        long Seq,
        string Text,
        long StreamOffsetMs,
        DateTime Timestamp,
        int ConfirmationCount,
        int? Speaker);

    private sealed record CaptureResult(
        string Source,
        string? MicDeviceId,
        string? MicDeviceName,
        int DurationSeconds,
        int SampleRate,
        string Model,
        string Language,
        string StableTranscript,
        string ProvisionalTranscript,
        int StableChunkCount,
        IReadOnlyList<StableChunk> StableChunks,
        DateTime CapturedAtUtc);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }
            if (options.ListDevices)
            {
                var enumerator = new WindowsAudioDeviceEnumerator();
                var payload = new
                {
                    capture = enumerator.GetCaptureDevices(),
                    render = enumerator.GetRenderDevices()
                };
                var devicesJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(devicesJson);
                return 0;
            }

            var apiKey = Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("Error: DEEPGRAM_API_KEY is required.");
                return 1;
            }

            var source = ParseSource(options.Source);
            var audio = new WindowsAudioCaptureService(
                options.SampleRate,
                source,
                options.MicDeviceId,
                options.MicDeviceName);
            var deepgramOptions = new DeepgramOptions
            {
                ApiKey = apiKey,
                Model = options.Model,
                Language = options.Language,
                SampleRate = options.SampleRate,
                InterimResults = true,
                Punctuate = true,
                SmartFormat = true,
                EndpointingMs = options.EndpointingMs,
                UtteranceEndMs = options.UtteranceEndMs,
                Vad = true,
                Diarize = options.Diarize,
            };

            var chunks = new List<StableChunk>();
            long seq = 0;

            await using var service = new DeepgramTranscriptionService(audio, deepgramOptions);
            service.OnStableText += evt =>
            {
                var text = (evt.Text ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    return;
                }

                chunks.Add(new StableChunk(
                    Seq: Interlocked.Increment(ref seq),
                    Text: text,
                    StreamOffsetMs: evt.StreamOffsetMs,
                    Timestamp: evt.Timestamp,
                    ConfirmationCount: evt.ConfirmationCount,
                    Speaker: evt.Speaker));
            };

            using var cts = new CancellationTokenSource();
            var startTask = service.StartAsync(cts.Token);

            await Task.Delay(TimeSpan.FromSeconds(options.DurationSeconds), cts.Token);
            await service.StopAsync();

            try
            {
                cts.Cancel();
                await startTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }

            var result = new CaptureResult(
                Source: source.ToString(),
                MicDeviceId: options.MicDeviceId,
                MicDeviceName: options.MicDeviceName,
                DurationSeconds: options.DurationSeconds,
                SampleRate: options.SampleRate,
                Model: options.Model,
                Language: options.Language,
                StableTranscript: service.GetStableTranscript().Trim(),
                ProvisionalTranscript: service.GetProvisionalTranscript().Trim(),
                StableChunkCount: chunks.Count,
                StableChunks: chunks,
                CapturedAtUtc: DateTime.UtcNow);

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            if (!string.IsNullOrWhiteSpace(options.OutputFile))
            {
                var outputPath = Path.GetFullPath(options.OutputFile);
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(outputPath, json);
            }

            Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private sealed record CliOptions(
        int DurationSeconds,
        string Source,
        string? MicDeviceId,
        string? MicDeviceName,
        int SampleRate,
        string Model,
        string Language,
        int EndpointingMs,
        int UtteranceEndMs,
        bool Diarize,
        string? OutputFile,
        bool ListDevices,
        bool ShowHelp);

    private static CliOptions ParseArgs(string[] args)
    {
        var durationSeconds = 8;
        var source = "microphone";
        string? micDeviceId = null;
        string? micDeviceName = null;
        var sampleRate = 16000;
        var model = "nova-2";
        var language = "en";
        var endpointingMs = 300;
        var utteranceEndMs = 1000;
        var diarize = false;
        string? outputFile = null;
        var listDevices = false;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
            {
                showHelp = true;
                continue;
            }
            if (arg == "--list-devices")
            {
                listDevices = true;
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {arg}");
            }

            var value = args[++i];
            switch (arg)
            {
                case "--duration-seconds":
                    durationSeconds = Math.Max(1, int.Parse(value));
                    break;
                case "--source":
                    source = value;
                    break;
                case "--mic-device-id":
                    micDeviceId = value;
                    break;
                case "--mic-device-name":
                    micDeviceName = value;
                    break;
                case "--sample-rate":
                    sampleRate = Math.Max(8000, int.Parse(value));
                    break;
                case "--model":
                    model = value;
                    break;
                case "--language":
                    language = value;
                    break;
                case "--endpointing-ms":
                    endpointingMs = Math.Max(0, int.Parse(value));
                    break;
                case "--utterance-end-ms":
                    utteranceEndMs = Math.Max(0, int.Parse(value));
                    break;
                case "--diarize":
                    diarize = bool.Parse(value);
                    break;
                case "--output":
                    outputFile = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new CliOptions(
            DurationSeconds: durationSeconds,
            Source: source,
            MicDeviceId: micDeviceId,
            MicDeviceName: micDeviceName,
            SampleRate: sampleRate,
            Model: model,
            Language: language,
            EndpointingMs: endpointingMs,
            UtteranceEndMs: utteranceEndMs,
            Diarize: diarize,
            OutputFile: outputFile,
            ListDevices: listDevices,
            ShowHelp: showHelp);
    }

    private static AudioInputSource ParseSource(string source)
    {
        return source.Trim().ToLowerInvariant() switch
        {
            "mic" => AudioInputSource.Microphone,
            "microphone" => AudioInputSource.Microphone,
            "loopback" => AudioInputSource.Loopback,
            _ => throw new ArgumentException("Source must be 'microphone' (or 'mic') or 'loopback'."),
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Interview Assist STT CLI (Deepgram)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project Interview-assist-stt-cli -- --duration-seconds 8 --source microphone");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --duration-seconds <int>  Capture duration (default: 8)");
        Console.WriteLine("  --source <name>           microphone|mic|loopback (default: microphone)");
        Console.WriteLine("  --mic-device-id <id>      Specific capture endpoint id (microphone only)");
        Console.WriteLine("  --mic-device-name <name>  Capture device name contains match (microphone only)");
        Console.WriteLine("  --sample-rate <int>       Audio sample rate (default: 16000)");
        Console.WriteLine("  --model <name>            Deepgram model (default: nova-2)");
        Console.WriteLine("  --language <code>         Language code (default: en)");
        Console.WriteLine("  --endpointing-ms <int>    Endpointing ms (default: 300)");
        Console.WriteLine("  --utterance-end-ms <int>  Utterance end ms (default: 1000)");
        Console.WriteLine("  --diarize <true|false>    Enable speaker diarization (default: false)");
        Console.WriteLine("  --output <file>           Optional output JSON file path");
        Console.WriteLine("  --list-devices            List capture/render endpoints as JSON");
        Console.WriteLine("  --help|-h                 Show help");
    }
}
