using System.Text;
using System.Text.Json;

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
    /// Generate a markdown report from recorded events.
    /// </summary>
    public static string GenerateMarkdown(
        IReadOnlyList<RecordedEvent> events,
        string? sourceFile = null,
        string? outputFile = null,
        string? logFile = null,
        TimeSpan? wallClockDuration = null)
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
