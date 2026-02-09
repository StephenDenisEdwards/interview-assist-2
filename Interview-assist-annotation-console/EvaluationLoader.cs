using System.Text.Json;

namespace InterviewAssist.AnnotationConsole;

public sealed record GroundTruthItem(string Text, string? Subtype, double Confidence);

public sealed record DetectedQuestionItem(string SourceText, string? Subtype, double Confidence, string? UtteranceId);

public sealed record MatchItem(
    string GroundTruth,
    string? GroundTruthSubtype,
    string Detected,
    string? DetectedSubtype,
    double SimilarityScore);

public sealed record EvaluationData(
    string FullTranscript,
    IReadOnlyList<GroundTruthItem> GroundTruth,
    IReadOnlyList<DetectedQuestionItem> DetectedQuestions,
    IReadOnlyList<MatchItem> Matches,
    IReadOnlyList<string> Missed);

public static class EvaluationLoader
{
    public static async Task<EvaluationData> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var fullTranscript = root.TryGetProperty("FullTranscript", out var ftProp)
            ? ftProp.GetString() ?? ""
            : "";

        var groundTruth = new List<GroundTruthItem>();
        if (root.TryGetProperty("GroundTruth", out var gtArray))
        {
            foreach (var item in gtArray.EnumerateArray())
            {
                var text = item.TryGetProperty("Text", out var t) ? t.GetString() ?? "" : "";
                var subtype = item.TryGetProperty("Subtype", out var s) && s.ValueKind != JsonValueKind.Null
                    ? s.GetString()
                    : null;
                var confidence = item.TryGetProperty("Confidence", out var c) ? c.GetDouble() : 0.0;
                groundTruth.Add(new GroundTruthItem(text, subtype, confidence));
            }
        }

        var detectedQuestions = new List<DetectedQuestionItem>();
        if (root.TryGetProperty("DetectedQuestions", out var dqArray))
        {
            foreach (var item in dqArray.EnumerateArray())
            {
                var sourceText = item.TryGetProperty("SourceText", out var st) ? st.GetString() ?? "" : "";
                var subtype = item.TryGetProperty("Subtype", out var s) && s.ValueKind != JsonValueKind.Null
                    ? s.GetString()
                    : null;
                var confidence = item.TryGetProperty("Confidence", out var c) ? c.GetDouble() : 0.0;
                var utteranceId = item.TryGetProperty("UtteranceId", out var u) && u.ValueKind != JsonValueKind.Null
                    ? u.GetString()
                    : null;
                detectedQuestions.Add(new DetectedQuestionItem(sourceText, subtype, confidence, utteranceId));
            }
        }

        var matches = new List<MatchItem>();
        if (root.TryGetProperty("Matches", out var mArray))
        {
            foreach (var item in mArray.EnumerateArray())
            {
                var gt = item.TryGetProperty("GroundTruth", out var g) ? g.GetString() ?? "" : "";
                var gtSubtype = item.TryGetProperty("GroundTruthSubtype", out var gs) && gs.ValueKind != JsonValueKind.Null
                    ? gs.GetString()
                    : null;
                var detected = item.TryGetProperty("Detected", out var d) ? d.GetString() ?? "" : "";
                var detectedSubtype = item.TryGetProperty("DetectedSubtype", out var ds) && ds.ValueKind != JsonValueKind.Null
                    ? ds.GetString()
                    : null;
                var score = item.TryGetProperty("SimilarityScore", out var ss) ? ss.GetDouble() : 0.0;
                matches.Add(new MatchItem(gt, gtSubtype, detected, detectedSubtype, score));
            }
        }

        var missed = new List<string>();
        if (root.TryGetProperty("Missed", out var missedArray))
        {
            foreach (var item in missedArray.EnumerateArray())
            {
                var text = item.GetString();
                if (text != null)
                    missed.Add(text);
            }
        }

        return new EvaluationData(fullTranscript, groundTruth, detectedQuestions, matches, missed);
    }
}
