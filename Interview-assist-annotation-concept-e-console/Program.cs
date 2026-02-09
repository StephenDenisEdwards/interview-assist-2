using System.Text;
using System.Text.Json;
using InterviewAssist.Library.Pipeline.Recording;
using Terminal.Gui;

namespace InterviewAssist.AnnotationConceptEConsole;

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
                Console.WriteLine("Interview Assist - Concept E Annotation Tool");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  dotnet run -- --recording <session.jsonl>   Annotate transcript from a recording");
                Console.WriteLine("  dotnet run -- --help                        Show this help message");
                Console.WriteLine();
                Console.WriteLine("Keyboard shortcuts:");
                Console.WriteLine("  Up/Down      Scroll transcript / navigate questions");
                Console.WriteLine("  PgUp/PgDn    Scroll by page");
                Console.WriteLine("  M            Start/cancel mark mode (drop selection anchor)");
                Console.WriteLine("  Arrows       Move cursor (extends selection when marking)");
                Console.WriteLine("  S            Confirm selection and add as question");
                Console.WriteLine("  Esc          Cancel mark mode");
                Console.WriteLine("  D            Delete selected question");
                Console.WriteLine("  T            Cycle question subtype");
                Console.WriteLine("  Tab          Switch focus between panels");
                Console.WriteLine("  Ctrl+S       Save annotations");
                Console.WriteLine("  Ctrl+Q       Quit");
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
        List<RecordedEvent> events;
        try
        {
            events = await LoadEventsAsync(recordingFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading recording: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  Loaded {events.Count} events");

        // Build transcript from final ASR events (non-overlapping, clean text)
        var transcript = BuildTranscriptFromAsrEvents(events);
        Console.WriteLine($"  Transcript length: {transcript.Length:N0} chars");

        if (string.IsNullOrWhiteSpace(transcript))
        {
            Console.WriteLine("Warning: No transcript content found in recording.");
        }

        // Extract and deduplicate detected questions
        var detectedQuestions = TranscriptExtractor.ExtractDetectedQuestions(events);
        var dedupedQuestions = TranscriptExtractor.DeduplicateQuestions(detectedQuestions);
        Console.WriteLine($"  Detected {detectedQuestions.Count} questions, {dedupedQuestions.Count} after dedup");

        // Map detected questions to character offsets by searching for their text
        var annotatedQuestions = MapQuestionsByTextSearch(dedupedQuestions, transcript);
        Console.WriteLine($"  Mapped {annotatedQuestions.Count} questions to transcript positions");

        Console.WriteLine();
        Console.WriteLine("Starting UI...");

        // Run Terminal.Gui
        Application.Init();
        try
        {
            var app = new ConceptEApp(
                transcript,
                annotatedQuestions,
                Path.GetFileName(recordingFile),
                recordingFile);
            app.Run();
        }
        finally
        {
            Application.Shutdown();
        }

        return 0;
    }

    /// <summary>
    /// Build transcript from final ASR events (Deepgram's committed segments).
    /// These are non-overlapping, unlike UtteranceEvent.Final which contains
    /// duplicated text due to aggressive utterance boundary detection.
    /// </summary>
    internal static string BuildTranscriptFromAsrEvents(List<RecordedEvent> events)
    {
        var finalTexts = events
            .OfType<RecordedAsrEvent>()
            .Where(e => e.Data.IsFinal && !string.IsNullOrWhiteSpace(e.Data.Text))
            .Select(e => e.Data.Text.Trim());

        return string.Join(" ", finalTexts);
    }

    /// <summary>
    /// Map detected questions to character offsets by searching for their SourceText
    /// in the transcript. Falls back to word-sequence matching when exact match fails
    /// (SourceText may span ASR segment boundaries with different spacing/punctuation).
    /// </summary>
    internal static List<AnnotatedQuestion> MapQuestionsByTextSearch(
        IReadOnlyList<RecordedIntentEvent> questions,
        string transcript)
    {
        var result = new List<AnnotatedQuestion>();
        int nextId = 1;
        var usedRanges = new List<(int Start, int End)>();
        var transcriptLower = transcript.ToLowerInvariant();

        foreach (var q in questions)
        {
            var sourceText = q.Data.Intent.SourceText;
            var originalText = q.Data.Intent.OriginalText;
            if (string.IsNullOrWhiteSpace(sourceText))
                continue;

            // Prefer OriginalText for matching (verbatim transcript excerpt)
            // Fall back to SourceText (self-contained, may be reformulated)
            var searchText = !string.IsNullOrWhiteSpace(originalText) ? originalText : sourceText;

            // Try exact match first
            var match = FindExactMatch(transcriptLower, searchText.ToLowerInvariant(), usedRanges);

            // Fall back to word-sequence match
            if (match == null)
                match = FindWordSequenceMatch(transcriptLower, searchText, usedRanges);

            // If OriginalText didn't match, try SourceText as fallback
            if (match == null && !string.IsNullOrWhiteSpace(originalText))
            {
                match = FindExactMatch(transcriptLower, sourceText.ToLowerInvariant(), usedRanges);
                if (match == null)
                    match = FindWordSequenceMatch(transcriptLower, sourceText, usedRanges);
            }

            if (match == null)
                continue;

            usedRanges.Add(match.Value);

            result.Add(new AnnotatedQuestion(
                Id: nextId++,
                Text: sourceText,
                Subtype: q.Data.Intent.Subtype,
                Confidence: q.Data.Intent.Confidence,
                TranscriptStartOffset: match.Value.Start,
                TranscriptEndOffset: match.Value.End,
                Source: QuestionSource.LlmDetected));
        }

        return result;
    }

    private static (int Start, int End)? FindExactMatch(
        string transcriptLower, string searchLower, List<(int Start, int End)> usedRanges)
    {
        int searchFrom = 0;
        while (searchFrom < transcriptLower.Length)
        {
            int idx = transcriptLower.IndexOf(searchLower, searchFrom, StringComparison.Ordinal);
            if (idx < 0) break;

            int end = idx + searchLower.Length;
            if (!usedRanges.Any(r => idx < r.End && end > r.Start))
                return (idx, end);

            searchFrom = idx + 1;
        }
        return null;
    }

    /// <summary>
    /// Find the shortest span in the transcript that contains all words from the
    /// source text in order. Handles cases where SourceText spans ASR segment
    /// boundaries and has different whitespace/punctuation than the ASR transcript.
    /// </summary>
    private static (int Start, int End)? FindWordSequenceMatch(
        string transcriptLower, string sourceText, List<(int Start, int End)> usedRanges)
    {
        // Extract words from the source text (letters/digits only, lowercase)
        var sourceWords = ExtractWords(sourceText.ToLowerInvariant());
        if (sourceWords.Count == 0) return null;

        // Extract words from transcript with their character positions
        var transcriptWords = ExtractWordsWithPositions(transcriptLower);
        if (transcriptWords.Count == 0) return null;

        // Slide through transcript words looking for the source word sequence
        for (int i = 0; i <= transcriptWords.Count - sourceWords.Count; i++)
        {
            bool matched = true;
            int lastMatchIdx = i;

            for (int j = 0; j < sourceWords.Count; j++)
            {
                // Allow a small gap (up to 2 extra transcript words) for inserted filler
                bool found = false;
                int maxLookahead = j == 0 ? 0 : 2;
                for (int k = 0; k <= maxLookahead; k++)
                {
                    int tIdx = lastMatchIdx + (j == 0 ? 0 : 1) + k;
                    if (tIdx >= transcriptWords.Count) break;
                    if (transcriptWords[tIdx].Word == sourceWords[j])
                    {
                        lastMatchIdx = tIdx;
                        found = true;
                        break;
                    }
                }
                if (!found) { matched = false; break; }
            }

            if (matched)
            {
                int start = transcriptWords[i].Start;
                int end = transcriptWords[lastMatchIdx].End;

                if (!usedRanges.Any(r => start < r.End && end > r.Start))
                    return (start, end);
            }
        }

        return null;
    }

    private static List<string> ExtractWords(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0)
            {
                words.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        return words;
    }

    private static List<(string Word, int Start, int End)> ExtractWordsWithPositions(string text)
    {
        var words = new List<(string Word, int Start, int End)>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                int start = i;
                while (i < text.Length && char.IsLetterOrDigit(text[i])) i++;
                words.Add((text.Substring(start, i - start), start, i));
            }
            else
            {
                i++;
            }
        }
        return words;
    }

    private static async Task<List<RecordedEvent>> LoadEventsAsync(string filePath)
    {
        var events = new List<RecordedEvent>();
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        using var reader = new StreamReader(filePath, Encoding.UTF8);
        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var evt = JsonSerializer.Deserialize<RecordedEvent>(line, jsonOptions);
                if (evt != null)
                    events.Add(evt);
            }
            catch (JsonException)
            {
                // Skip unparseable lines
            }
        }

        return events;
    }
}
