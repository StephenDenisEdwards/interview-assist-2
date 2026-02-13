using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InterviewAssist.Library.Pipeline.Recording;

/// <summary>
/// Generates markdown session reports from recorded events.
/// </summary>
public static class SessionReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Load recorded events from a JSONL file.
    /// </summary>
    public static async Task<IReadOnlyList<RecordedEvent>> LoadEventsAsync(
        string jsonlPath, CancellationToken ct = default)
    {
        var events = new List<RecordedEvent>();

        using var reader = new StreamReader(jsonlPath, Encoding.UTF8);
        string? line;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var evt = JsonSerializer.Deserialize<RecordedEvent>(line, JsonOptions);
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

    /// <summary>
    /// Parsed summary of a session log file.
    /// </summary>
    public sealed record LogInsights
    {
        public int TotalLines { get; init; }
        public int DeepgramConnections { get; init; }
        public int SpeechStarts { get; init; }
        public int UtteranceEnds { get; init; }
        public int EndpointingEvents { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = [];
        public IReadOnlyList<string> Errors { get; init; } = [];
    }

    /// <summary>
    /// Parse a log file and return a summary of operational events.
    /// </summary>
    public static LogInsights ParseLogFile(IReadOnlyList<string> logLines)
    {
        int connections = 0, speechStarts = 0, utteranceEnds = 0, endpointing = 0;
        var warnings = new List<string>();
        var errors = new List<string>();

        foreach (var line in logLines)
        {
            if (line.Contains("[Deepgram] Connected to Deepgram"))
                connections++;
            else if (line.Contains("[Deepgram] Speech started"))
                speechStarts++;
            else if (line.Contains("[Deepgram] Utterance end detected"))
                utteranceEnds++;
            else if (line.Contains("Speech final (endpointing)"))
                endpointing++;

            if (line.Contains("WARNING", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Warn", StringComparison.Ordinal))
                warnings.Add(line.TrimEnd());

            if (line.Contains("Error", StringComparison.Ordinal))
                errors.Add(line.TrimEnd());
        }

        return new LogInsights
        {
            TotalLines = logLines.Count,
            DeepgramConnections = connections,
            SpeechStarts = speechStarts,
            UtteranceEnds = utteranceEnds,
            EndpointingEvents = endpointing,
            Warnings = warnings,
            Errors = errors,
        };
    }

    /// <summary>
    /// Extract the session ID from any session-derived filename.
    /// Returns the session-YYYY-MM-DD-HHmmss[-PID] prefix, or null if the filename doesn't match.
    /// </summary>
    public static string? ExtractSessionId(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var match = Regex.Match(name, @"^(session-\d{4}-\d{2}-\d{2}-\d{6}(?:-\d+)?)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Find the associated log file for a JSONL session file.
    /// Tries new convention first (session-YYYY-MM-DD-HHmmss[-PID].log),
    /// then falls back to legacy format (transcription-detection-YYYYMMDD-HHmmss[-PID].log).
    /// </summary>
    public static string? ResolveLogFile(string jsonlPath, string logFolder = "logs")
    {
        var sessionId = ExtractSessionId(jsonlPath);
        if (sessionId == null) return null;

        // New convention: session-YYYY-MM-DD-HHmmss[-pid].log
        var newPath = Path.Combine(logFolder, $"{sessionId}.log");
        if (File.Exists(newPath)) return newPath;

        // Legacy: transcription-detection-YYYYMMDD-HHmmss[-pid].log
        var match = Regex.Match(sessionId, @"session-(\d{4})-(\d{2})-(\d{2})-(\d{6})(?:-(\d+))?");
        if (!match.Success) return null;
        var dateStr = $"{match.Groups[1].Value}{match.Groups[2].Value}{match.Groups[3].Value}";
        var timeStr = match.Groups[4].Value;
        var pid = match.Groups[5].Success ? match.Groups[5].Value : null;
        if (pid != null)
        {
            var legacyWithPid = Path.Combine(logFolder, $"transcription-detection-{dateStr}-{timeStr}-{pid}.log");
            if (File.Exists(legacyWithPid)) return legacyWithPid;
        }
        var legacyPath = Path.Combine(logFolder, $"transcription-detection-{dateStr}-{timeStr}.log");
        return File.Exists(legacyPath) ? legacyPath : null;
    }

    /// <summary>
    /// Get the report output path in the reports/ folder for a given JSONL path.
    /// </summary>
    public static string GetReportPath(string jsonlPath, string reportFolder = "reports")
    {
        var sessionId = ExtractSessionId(jsonlPath);
        var baseName = sessionId ?? Path.GetFileNameWithoutExtension(jsonlPath);
        return Path.Combine(reportFolder, baseName + ".report.md");
    }

    /// <summary>
    /// Generate a markdown report from recorded events.
    /// </summary>
    public static string GenerateMarkdown(
        IReadOnlyList<RecordedEvent> events,
        string? sourceFile = null,
        string? outputFile = null,
        string? logFile = null,
        TimeSpan? wallClockDuration = null,
        IReadOnlyList<string>? logLines = null)
    {
        var sb = new StringBuilder();

        var metadata = events.OfType<RecordedSessionMetadata>().FirstOrDefault();
        var asrEvents = events.OfType<RecordedAsrEvent>().ToList();
        var utteranceEndSignals = events.OfType<RecordedUtteranceEndSignal>().ToList();
        var utteranceEvents = events.OfType<RecordedUtteranceEvent>().ToList();
        var intentEvents = events.OfType<RecordedIntentEvent>().ToList();
        var correctionEvents = events.OfType<RecordedIntentCorrectionEvent>().ToList();
        var actionEvents = events.OfType<RecordedActionEvent>().ToList();

        // ── Session Overview ──
        sb.AppendLine("# Session Report");
        sb.AppendLine();

        if (sourceFile != null)
            sb.AppendLine($"- **Source:** `{sourceFile}`");
        if (outputFile != null)
            sb.AppendLine($"- **Output:** `{outputFile}`");
        if (logFile != null)
            sb.AppendLine($"- **Log:** `{logFile}`");
        if (metadata != null)
            sb.AppendLine($"- **Recorded:** {metadata.RecordedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");

        if (metadata?.Config != null)
        {
            var cfg = metadata.Config;
            if (cfg.DeepgramModel != null)
                sb.AppendLine($"- **Deepgram model:** {cfg.DeepgramModel}");
            if (cfg.IntentDetectionMode != null)
                sb.AppendLine($"- **Detection mode:** {cfg.IntentDetectionMode}");
            if (cfg.AudioSource != null)
                sb.AppendLine($"- **Audio source:** {cfg.AudioSource}");
        }

        var lastEvent = events.Where(e => e is not RecordedSessionMetadata).LastOrDefault();
        if (lastEvent != null)
        {
            var sessionDurationMs = lastEvent.OffsetMs;
            sb.AppendLine($"- **Session duration:** {FormatDuration(TimeSpan.FromMilliseconds(sessionDurationMs))}");
        }
        if (wallClockDuration != null)
            sb.AppendLine($"- **Wall clock:** {FormatDuration(wallClockDuration.Value)}");

        sb.AppendLine($"- **Total events:** {events.Count}");
        sb.AppendLine();

        // ── Event Distribution ──
        sb.AppendLine("## Event Distribution");
        sb.AppendLine();
        sb.AppendLine("| Type | Count |");
        sb.AppendLine("|------|------:|");
        if (asrEvents.Count > 0)
            sb.AppendLine($"| ASR | {asrEvents.Count} |");
        if (utteranceEndSignals.Count > 0)
            sb.AppendLine($"| UtteranceEndSignal | {utteranceEndSignals.Count} |");
        if (utteranceEvents.Count > 0)
            sb.AppendLine($"| Utterance | {utteranceEvents.Count} |");
        if (intentEvents.Count > 0)
            sb.AppendLine($"| Intent | {intentEvents.Count} |");
        if (correctionEvents.Count > 0)
            sb.AppendLine($"| IntentCorrection | {correctionEvents.Count} |");
        if (actionEvents.Count > 0)
            sb.AppendLine($"| Action | {actionEvents.Count} |");
        sb.AppendLine();

        // ── Utterance Analysis ──
        var finalUtterances = utteranceEvents
            .Where(e => e.Data.EventType == "Final")
            .ToList();

        if (finalUtterances.Count > 0)
        {
            sb.AppendLine("## Utterance Analysis");
            sb.AppendLine();

            // Close reason distribution
            var closeReasons = finalUtterances
                .GroupBy(e => e.Data.CloseReason ?? "unknown")
                .OrderByDescending(g => g.Count())
                .ToList();

            sb.AppendLine("### Close Reasons");
            sb.AppendLine();
            sb.AppendLine("| Reason | Count |");
            sb.AppendLine("|--------|------:|");
            foreach (var group in closeReasons)
                sb.AppendLine($"| {group.Key} | {group.Count()} |");
            sb.AppendLine();

            // Duration stats
            var durations = finalUtterances
                .Select(e => e.Data.DurationMs)
                .OrderBy(d => d)
                .ToList();

            sb.AppendLine("### Duration Statistics");
            sb.AppendLine();
            sb.AppendLine($"- **Count:** {durations.Count}");
            sb.AppendLine($"- **Min:** {durations.First()}ms");
            sb.AppendLine($"- **Median:** {GetPercentile(durations.Select(d => (double)d).ToList(), 50):F0}ms");
            sb.AppendLine($"- **Mean:** {durations.Average():F0}ms");
            sb.AppendLine($"- **Max:** {durations.Last()}ms");
            sb.AppendLine();
        }

        // ── Intent Detection Results ──
        var finalIntents = intentEvents.Where(e => !e.Data.IsCandidate).ToList();
        var candidateIntents = intentEvents.Where(e => e.Data.IsCandidate).ToList();

        if (intentEvents.Count > 0)
        {
            sb.AppendLine("## Intent Detection Results");
            sb.AppendLine();
            sb.AppendLine($"- **Candidates:** {candidateIntents.Count}");
            sb.AppendLine($"- **Finals:** {finalIntents.Count}");
            sb.AppendLine();

            if (finalIntents.Count > 0)
            {
                sb.AppendLine("### Final Intents");
                sb.AppendLine();

                for (int i = 0; i < finalIntents.Count; i++)
                {
                    var evt = finalIntents[i];
                    var intent = evt.Data.Intent;
                    sb.AppendLine($"**#{i + 1}** `{intent.Type}/{intent.Subtype}` — conf={intent.Confidence:F2}");
                    sb.AppendLine();
                    sb.AppendLine($"- **Source:** \"{Truncate(intent.SourceText, 120)}\"");

                    if (intent.OriginalText != null)
                    {
                        sb.AppendLine($"- **Original:** \"{Truncate(intent.OriginalText, 120)}\"");
                        var isMatch = string.Equals(
                            intent.SourceText.Trim(), intent.OriginalText.Trim(),
                            StringComparison.OrdinalIgnoreCase);
                        sb.AppendLine($"- **Match:** {(isMatch ? "YES" : "NO")}");
                    }

                    // Compute latency: find the closest preceding utterance Final for this utteranceId
                    var uttFinal = finalUtterances
                        .Where(u => u.Data.Id == evt.Data.UtteranceId)
                        .FirstOrDefault();
                    if (uttFinal != null)
                    {
                        var latencyMs = evt.OffsetMs - uttFinal.OffsetMs;
                        sb.AppendLine($"- **Latency:** {latencyMs}ms (from utterance close)");
                    }

                    sb.AppendLine();
                }
            }
        }

        // ── Intent Corrections ──
        if (correctionEvents.Count > 0)
        {
            sb.AppendLine("## Intent Corrections");
            sb.AppendLine();

            foreach (var evt in correctionEvents)
            {
                var data = evt.Data;
                sb.AppendLine($"- **{data.CorrectionType}** (utt={data.UtteranceId})");
                if (data.OriginalIntent != null)
                    sb.AppendLine($"  - Original: `{data.OriginalIntent.Type}/{data.OriginalIntent.Subtype}` conf={data.OriginalIntent.Confidence:F2}");
                sb.AppendLine($"  - Corrected: `{data.CorrectedIntent.Type}/{data.CorrectedIntent.Subtype}` conf={data.CorrectedIntent.Confidence:F2}");
                sb.AppendLine($"  - Source: \"{Truncate(data.CorrectedIntent.SourceText, 100)}\"");
            }

            sb.AppendLine();
        }

        // ── Action Events ──
        if (actionEvents.Count > 0)
        {
            sb.AppendLine("## Action Events");
            sb.AppendLine();
            sb.AppendLine("| Action | Utterance | Debounced | Offset |");
            sb.AppendLine("|--------|-----------|-----------|-------:|");
            foreach (var evt in actionEvents)
            {
                var d = evt.Data;
                sb.AppendLine($"| {d.ActionName} | {d.UtteranceId} | {(d.WasDebounced ? "Yes" : "No")} | {evt.OffsetMs}ms |");
            }
            sb.AppendLine();
        }

        // ── originalText Accuracy ──
        if (finalIntents.Count > 0)
        {
            var withOriginal = finalIntents.Where(e => e.Data.Intent.OriginalText != null).ToList();
            var exactMatches = withOriginal.Count(e =>
                string.Equals(
                    e.Data.Intent.SourceText.Trim(),
                    e.Data.Intent.OriginalText!.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            sb.AppendLine("## originalText Accuracy");
            sb.AppendLine();
            if (withOriginal.Count > 0)
            {
                var pct = 100.0 * exactMatches / withOriginal.Count;
                sb.AppendLine($"- **Exact matches:** {exactMatches}/{withOriginal.Count} ({pct:F1}%)");
            }
            else
            {
                sb.AppendLine("- No intents had originalText populated.");
            }
            sb.AppendLine();
        }

        // ── Latency Statistics ──
        if (finalIntents.Count > 0)
        {
            var latencies = new List<double>();

            foreach (var evt in finalIntents)
            {
                var uttFinal = finalUtterances
                    .Where(u => u.Data.Id == evt.Data.UtteranceId)
                    .FirstOrDefault();
                if (uttFinal != null)
                {
                    var latencyMs = evt.OffsetMs - uttFinal.OffsetMs;
                    if (latencyMs >= 0)
                        latencies.Add(latencyMs);
                }
            }

            if (latencies.Count > 0)
            {
                latencies.Sort();

                sb.AppendLine("## Latency Statistics");
                sb.AppendLine();
                sb.AppendLine($"- **Count:** {latencies.Count}");
                sb.AppendLine($"- **Min:** {latencies.First():F0}ms");
                sb.AppendLine($"- **Median:** {GetPercentile(latencies, 50):F0}ms");
                sb.AppendLine($"- **Mean:** {latencies.Average():F0}ms");
                sb.AppendLine($"- **Max:** {latencies.Last():F0}ms");
                sb.AppendLine($"- **P95:** {GetPercentile(latencies, 95):F0}ms");
                sb.AppendLine();
            }
        }

        // ── Log Insights ──
        if (logLines is { Count: > 0 })
        {
            var insights = ParseLogFile(logLines);

            sb.AppendLine("## Log Insights");
            sb.AppendLine();
            sb.AppendLine($"- **Log lines:** {insights.TotalLines}");
            sb.AppendLine();

            sb.AppendLine("### Deepgram Events");
            sb.AppendLine();
            sb.AppendLine($"- **Connections:** {insights.DeepgramConnections}");
            sb.AppendLine($"- **Speech starts:** {insights.SpeechStarts}");
            sb.AppendLine($"- **Utterance ends:** {insights.UtteranceEnds}");
            sb.AppendLine($"- **Endpointing:** {insights.EndpointingEvents}");
            sb.AppendLine();

            if (insights.Warnings.Count > 0)
            {
                sb.AppendLine($"### Warnings ({insights.Warnings.Count})");
                sb.AppendLine();
                foreach (var w in insights.Warnings)
                    sb.AppendLine($"- `{Truncate(w, 200)}`");
                sb.AppendLine();
            }

            if (insights.Errors.Count > 0)
            {
                sb.AppendLine($"### Errors ({insights.Errors.Count})");
                sb.AppendLine();
                foreach (var e in insights.Errors)
                    sb.AppendLine($"- `{Truncate(e, 200)}`");
                sb.AppendLine();
            }
        }

        // ── Transcript Segments ──
        var segments = TranscriptExtractor.ExtractSegments(events);
        if (segments.Count > 0)
        {
            sb.AppendLine("## Transcript");
            sb.AppendLine();
            sb.AppendLine($"{segments.Count} utterance segment(s).");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"*Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");

        return sb.ToString();
    }

    private static double GetPercentile(List<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var index = (percentile / 100.0) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        var fraction = index - lower;

        if (upper >= sortedValues.Count) upper = sortedValues.Count - 1;

        return sortedValues[lower] * (1 - fraction) + sortedValues[upper] * fraction;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalMinutes < 1)
            return $"{ts.TotalSeconds:F1}s";
        if (ts.TotalHours < 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }
}
