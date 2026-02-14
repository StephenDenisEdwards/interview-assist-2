using InterviewAssist.Library.Pipeline.Recording;
using Terminal.Gui;

namespace InterviewAssist.AnnotationConsole;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? recordingFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--recording" && i + 1 < args.Length)
            {
                recordingFile = args[i + 1];
                i++;
            }
            else if (args[i] is "--help" or "-h")
            {
                Console.WriteLine("Interview Assist - Annotation Tool");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  dotnet run -- --recording <session.jsonl>   View transcript from a recording");
                Console.WriteLine("  dotnet run -- --help                        Show this help message");
                Console.WriteLine();
                Console.WriteLine("Keyboard shortcuts:");
                Console.WriteLine("  Up/Down    Scroll transcript");
                Console.WriteLine("  PgUp/PgDn  Scroll by page");
                Console.WriteLine("  Home/End   Jump to start/end");
                Console.WriteLine("  Ctrl+Q     Quit");
                return 0;
            }
        }

        if (string.IsNullOrWhiteSpace(recordingFile))
        {
            Console.WriteLine("Error: --recording <session.jsonl> is required.");
            Console.WriteLine("Run with --help for usage information.");
            return 1;
        }

        if (!File.Exists(recordingFile))
        {
            Console.WriteLine($"Error: File not found: {recordingFile}");
            return 1;
        }

        // Load events from JSONL
        Console.WriteLine($"Loading recording: {Path.GetFileName(recordingFile)}");
        IReadOnlyList<RecordedEvent> events;
        try
        {
            events = await SessionReportGenerator.LoadEventsAsync(recordingFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading recording: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  Loaded {events.Count} events");

        // Extract transcript from final ASR events (Deepgram's committed segments).
        // These are non-overlapping, unlike UtteranceEvent.Final which contains
        // duplicated text due to aggressive utterance boundary detection.
        var transcript = ExtractTranscriptFromFinalAsrEvents(events);
        Console.WriteLine($"  Transcript length: {transcript.Length:N0} chars");

        if (string.IsNullOrWhiteSpace(transcript))
        {
            Console.WriteLine("Warning: No transcript content found in recording.");
        }

        Console.WriteLine();
        Console.WriteLine("Starting UI...");

        // Run Terminal.Gui
        Application.Init();
        try
        {
            var app = new AnnotationApp(transcript, Path.GetFileName(recordingFile));
            app.Run();
        }
        finally
        {
            Application.Shutdown();
        }

        return 0;
    }

    private static string ExtractTranscriptFromFinalAsrEvents(IReadOnlyList<RecordedEvent> events)
    {
        var finalTexts = events
            .OfType<RecordedAsrEvent>()
            .Where(e => e.Data.IsFinal && !string.IsNullOrWhiteSpace(e.Data.Text))
            .Select(e => e.Data.Text.Trim());

        return string.Join(" ", finalTexts);
    }

}
