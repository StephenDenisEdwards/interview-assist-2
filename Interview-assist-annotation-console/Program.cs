using Microsoft.Extensions.Configuration;
using Terminal.Gui;

namespace InterviewAssist.AnnotationConsole;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? reviewFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--review" && i + 1 < args.Length)
            {
                reviewFile = args[i + 1];
                i++;
            }
            else if (args[i] is "--help" or "-h")
            {
                Console.WriteLine("Interview Assist - Ground Truth Annotation Tool");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  dotnet run -- --review <evaluation.json>   Review and annotate ground truth");
                Console.WriteLine("  dotnet run -- --help                       Show this help message");
                Console.WriteLine();
                Console.WriteLine("Keyboard shortcuts:");
                Console.WriteLine("  A          Accept current question");
                Console.WriteLine("  X          Reject current question");
                Console.WriteLine("  E          Edit question text");
                Console.WriteLine("  S          Set subtype");
                Console.WriteLine("  N          Next item");
                Console.WriteLine("  P          Previous item");
                Console.WriteLine("  M          Add missed question");
                Console.WriteLine("  F          Finalise and save validated output");
                Console.WriteLine("  Ctrl+Q     Quit without finalising");
                Console.WriteLine();
                Console.WriteLine("Session auto-saves after every decision to *-review.json.");
                Console.WriteLine("Re-running with the same file resumes from last position.");
                return 0;
            }
        }

        if (string.IsNullOrWhiteSpace(reviewFile))
        {
            Console.WriteLine("Error: --review <evaluation.json> is required.");
            Console.WriteLine("Run with --help for usage information.");
            return 1;
        }

        if (!File.Exists(reviewFile))
        {
            Console.WriteLine($"Error: File not found: {reviewFile}");
            return 1;
        }

        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var backgroundColorHex = configuration["UI:BackgroundColor"] ?? "#1E1E1E";

        // Load evaluation data
        Console.WriteLine($"Loading evaluation: {Path.GetFileName(reviewFile)}");
        EvaluationData evaluation;
        try
        {
            evaluation = await EvaluationLoader.LoadAsync(reviewFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading evaluation file: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  Ground truth items: {evaluation.GroundTruth.Count}");
        Console.WriteLine($"  Detected questions: {evaluation.DetectedQuestions.Count}");
        Console.WriteLine($"  Transcript length: {evaluation.FullTranscript.Length:N0} chars");

        // Create review session
        var session = new ReviewSession(evaluation, Path.GetFullPath(reviewFile));

        // Check for existing review session (resume)
        var resumed = await session.TryResumeAsync();
        if (resumed)
        {
            Console.WriteLine($"  Resumed from existing review session (position {session.CurrentIndex + 1})");
            Console.WriteLine($"  Progress: {session.AcceptedCount} accepted, {session.RejectedCount} rejected, " +
                            $"{session.ModifiedCount} modified, {session.PendingCount} pending");
        }

        Console.WriteLine();
        Console.WriteLine("Starting annotation UI...");

        // Initialize Terminal.Gui
        Application.Init();

        try
        {
            var app = new AnnotationApp(session, Path.GetFileName(reviewFile), backgroundColorHex);
            app.Run();
        }
        finally
        {
            Application.Shutdown();
        }

        return 0;
    }
}
