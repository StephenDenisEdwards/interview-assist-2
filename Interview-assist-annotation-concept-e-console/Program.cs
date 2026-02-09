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

        // Build transcript from final ASR events with timing info
        var (transcript, asrSegments) = BuildTranscriptWithTiming(events);
        Console.WriteLine($"  Transcript length: {transcript.Length:N0} chars, {asrSegments.Count} ASR segments");

        if (string.IsNullOrWhiteSpace(transcript))
        {
            Console.WriteLine("Warning: No transcript content found in recording.");
        }

        // Build utterance time ranges for time-based mapping
        var utteranceTimeRanges = BuildUtteranceTimeRanges(events);
        Console.WriteLine($"  Found {utteranceTimeRanges.Count} utterance time ranges");

        // Extract final detected questions (exclude candidates — preliminary heuristic guesses),
        // apply LLM corrections, then deduplicate by utteranceId
        var allQuestions = TranscriptExtractor.ExtractDetectedQuestions(events);
        var detectedQuestions = allQuestions.Where(q => !q.Data.IsCandidate).ToList();
        Console.WriteLine($"  Detected {allQuestions.Count} total questions, {detectedQuestions.Count} final (excluded {allQuestions.Count - detectedQuestions.Count} candidates)");
        var correctedQuestions = ApplyIntentCorrections(detectedQuestions, events);
        Console.WriteLine($"  After LLM corrections: {correctedQuestions.Count} questions");
        // Deduplicate by utteranceId — keep the last (highest confidence) per utterance
        var dedupedQuestions = correctedQuestions
            .GroupBy(q => q.Data.UtteranceId)
            .Select(g => g.OrderByDescending(q => q.Data.Intent.Confidence).First())
            .OrderBy(q => q.OffsetMs)
            .ToList();
        Console.WriteLine($"  After dedup: {dedupedQuestions.Count} questions");

        // Map detected questions to transcript positions using time correlation
        var mapDiagnostics = new List<string>();
        var annotatedQuestions = MapQuestionsByTime(dedupedQuestions, asrSegments, utteranceTimeRanges, mapDiagnostics);
        Console.WriteLine($"  Mapped {annotatedQuestions.Count} of {dedupedQuestions.Count} questions to transcript positions");

        // Write diagnostics to a log file (Terminal.Gui overwrites the console)
        var logLines = new List<string>
        {
            $"Recording: {Path.GetFileName(recordingFile)}",
            $"Events: {events.Count}",
            $"Transcript: {transcript.Length:N0} chars, {asrSegments.Count} ASR segments",
            $"Utterance time ranges: {utteranceTimeRanges.Count}",
            $"Questions: {allQuestions.Count} total, {detectedQuestions.Count} final ({allQuestions.Count - detectedQuestions.Count} candidates excluded)",
            $"After LLM corrections: {correctedQuestions.Count}",
            $"After dedup: {dedupedQuestions.Count}",
            $"Mapped: {annotatedQuestions.Count} of {dedupedQuestions.Count}",
            "",
            "=== Mapping Details ==="
        };
        logLines.AddRange(mapDiagnostics);
        var logPath = Path.ChangeExtension(recordingFile, null) + "-load.log";
        File.WriteAllText(logPath, string.Join(Environment.NewLine, logLines));

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

    internal record AsrSegmentInfo(int CharStart, int CharEnd, long OffsetMs);

    /// <summary>
    /// Build transcript from final ASR events and track each segment's character
    /// offsets and delivery time. ASR finals are non-overlapping committed segments.
    /// </summary>
    internal static (string Transcript, List<AsrSegmentInfo> Segments) BuildTranscriptWithTiming(
        List<RecordedEvent> events)
    {
        var sb = new StringBuilder();
        var segments = new List<AsrSegmentInfo>();

        foreach (var asrEvent in events.OfType<RecordedAsrEvent>()
            .Where(e => e.Data.IsFinal && !string.IsNullOrWhiteSpace(e.Data.Text)))
        {
            var text = asrEvent.Data.Text.Trim();
            int charStart = sb.Length;
            sb.Append(text);
            int charEnd = sb.Length;
            sb.Append(' ');

            segments.Add(new AsrSegmentInfo(charStart, charEnd, asrEvent.OffsetMs));
        }

        return (sb.ToString(), segments);
    }

    /// <summary>
    /// Build a lookup of utterance time ranges from Final utterance events.
    /// Each utterance's speech spans from (offsetMs - durationMs) to offsetMs.
    /// </summary>
    internal static Dictionary<string, (long StartMs, long EndMs)> BuildUtteranceTimeRanges(
        List<RecordedEvent> events)
    {
        var ranges = new Dictionary<string, (long StartMs, long EndMs)>();

        foreach (var utt in events.OfType<RecordedUtteranceEvent>()
            .Where(e => e.Data.EventType == "Final"))
        {
            long endMs = utt.OffsetMs;
            long startMs = endMs - utt.Data.DurationMs;
            ranges[utt.Data.Id] = (startMs, endMs);
        }

        return ranges;
    }

    /// <summary>
    /// Apply LLM intent corrections to the base detected questions.
    /// - "Removed": drop the question (LLM says it's a false positive)
    /// - "TypeChanged": replace the intent data if the corrected type is still Question
    /// - "Added": insert a new question the heuristic missed
    /// - "Confirmed": no change needed
    /// </summary>
    internal static IReadOnlyList<RecordedIntentEvent> ApplyIntentCorrections(
        IReadOnlyList<RecordedIntentEvent> baseQuestions,
        List<RecordedEvent> allEvents)
    {
        var corrections = allEvents
            .OfType<RecordedIntentCorrectionEvent>()
            .ToList();

        if (corrections.Count == 0)
            return baseQuestions;

        // Index base questions by utteranceId for matching against corrections
        var questionsByUtterance = baseQuestions
            .GroupBy(q => q.Data.UtteranceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Track which utteranceIds have been removed
        var removedUtteranceIds = new HashSet<string>();

        // Track corrections that update existing questions
        var replacements = new Dictionary<string, DetectedIntentData>();

        // Track added questions (LLM found questions heuristic missed)
        var addedQuestions = new List<RecordedIntentCorrectionEvent>();

        foreach (var correction in corrections)
        {
            var uttId = correction.Data.UtteranceId;

            switch (correction.Data.CorrectionType)
            {
                case "Removed":
                    removedUtteranceIds.Add(uttId);
                    break;

                case "TypeChanged":
                    if (correction.Data.CorrectedIntent.Type == "Question")
                        replacements[uttId] = correction.Data.CorrectedIntent;
                    else
                        removedUtteranceIds.Add(uttId); // reclassified away from Question
                    break;

                case "Added":
                    if (correction.Data.CorrectedIntent.Type == "Question")
                        addedQuestions.Add(correction);
                    break;

                // "Confirmed" — no action needed
            }
        }

        // Build the corrected list
        var result = new List<RecordedIntentEvent>();

        foreach (var q in baseQuestions)
        {
            if (removedUtteranceIds.Contains(q.Data.UtteranceId))
                continue;

            if (replacements.TryGetValue(q.Data.UtteranceId, out var replacement))
            {
                // Replace the intent data but keep the original event's timing/utteranceId
                result.Add(q with
                {
                    Data = q.Data with { Intent = replacement }
                });
            }
            else
            {
                result.Add(q);
            }
        }

        // Add LLM-discovered questions
        foreach (var added in addedQuestions)
        {
            result.Add(new RecordedIntentEvent
            {
                OffsetMs = added.OffsetMs,
                Data = new IntentEventData
                {
                    Intent = added.Data.CorrectedIntent,
                    UtteranceId = added.Data.UtteranceId,
                    IsCandidate = false
                }
            });
        }

        return result.OrderBy(q => q.OffsetMs).ToList();
    }

    /// <summary>
    /// Map detected questions to transcript character offsets using time correlation.
    /// Each question's utteranceId is looked up to get a time range, then ASR segments
    /// whose delivery time falls within that range determine the character offsets.
    /// </summary>
    internal static List<AnnotatedQuestion> MapQuestionsByTime(
        IReadOnlyList<RecordedIntentEvent> questions,
        List<AsrSegmentInfo> asrSegments,
        Dictionary<string, (long StartMs, long EndMs)> utteranceTimeRanges,
        List<string>? diagnostics = null)
    {
        var result = new List<AnnotatedQuestion>();
        int nextId = 1;

        foreach (var q in questions)
        {
            var sourceText = q.Data.Intent.SourceText;
            if (string.IsNullOrWhiteSpace(sourceText))
                continue;

            var label = sourceText[..Math.Min(50, sourceText.Length)];

            // Look up the utterance time range for this question
            if (!utteranceTimeRanges.TryGetValue(q.Data.UtteranceId, out var timeRange))
            {
                diagnostics?.Add($"SKIP [{q.Data.UtteranceId}] '{label}' — no utterance time range found");
                continue;
            }

            diagnostics?.Add($"MAP  [{q.Data.UtteranceId}] '{label}' — utterance range: {timeRange.StartMs}-{timeRange.EndMs}ms");

            // Find ASR segments whose delivery time falls within the utterance window
            var matching = asrSegments
                .Where(s => s.OffsetMs >= timeRange.StartMs && s.OffsetMs <= timeRange.EndMs)
                .ToList();

            diagnostics?.Add($"     Exact match: {matching.Count} ASR segments");

            // If no exact match, progressively widen the window
            if (matching.Count == 0)
            {
                foreach (var margin in new[] { 500, 1500 })
                {
                    matching = asrSegments
                        .Where(s => s.OffsetMs >= timeRange.StartMs - margin && s.OffsetMs <= timeRange.EndMs + margin)
                        .ToList();
                    diagnostics?.Add($"     Widened ±{margin}ms: {matching.Count} ASR segments");
                    if (matching.Count > 0) break;
                }
            }

            if (matching.Count == 0)
            {
                // Find nearest ASR segment for debugging
                var nearest = asrSegments
                    .OrderBy(s => Math.Min(Math.Abs(s.OffsetMs - timeRange.StartMs), Math.Abs(s.OffsetMs - timeRange.EndMs)))
                    .FirstOrDefault();
                diagnostics?.Add($"     FAILED — nearest ASR at {nearest?.OffsetMs}ms (gap: {(nearest != null ? Math.Min(Math.Abs(nearest.OffsetMs - timeRange.StartMs), Math.Abs(nearest.OffsetMs - timeRange.EndMs)) : -1)}ms)");
                continue;
            }

            int charStart = matching.First().CharStart;
            int charEnd = matching.Last().CharEnd;

            result.Add(new AnnotatedQuestion(
                Id: nextId++,
                Text: sourceText,
                OriginalText: q.Data.Intent.OriginalText,
                Subtype: q.Data.Intent.Subtype,
                Confidence: q.Data.Intent.Confidence,
                TranscriptStartOffset: charStart,
                TranscriptEndOffset: charEnd,
                Source: QuestionSource.LlmDetected));
        }

        return result;
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
