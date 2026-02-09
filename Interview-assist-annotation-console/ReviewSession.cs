using System.Text.Json;
using InterviewAssist.Library.Pipeline.Recording;

namespace InterviewAssist.AnnotationConsole;

public enum ReviewDecision { Pending, Accept, Reject, Modify }

public sealed record HighlightRegion(int StartIndex, int Length, int ReviewItemIndex);

public sealed class ReviewItem
{
    public required string Text { get; set; }
    public string? Subtype { get; set; }
    public double Confidence { get; set; }
    public ReviewDecision Decision { get; set; } = ReviewDecision.Pending;
    public string? ModifiedText { get; set; }
    public string? ModifiedSubtype { get; set; }
    public string Source { get; set; } = "llm";
}

public sealed class ReviewSession
{
    private readonly EvaluationData _evaluation;
    private readonly string _evaluationFilePath;
    private readonly List<ReviewItem> _items = new();
    private readonly List<HighlightRegion> _highlights = new();

    public IReadOnlyList<ReviewItem> Items => _items;
    public IReadOnlyList<HighlightRegion> Highlights => _highlights;
    public string FullTranscript => _evaluation.FullTranscript;
    public int CurrentIndex { get; private set; }
    public int Count => _items.Count;

    public int AcceptedCount => _items.Count(i => i.Decision == ReviewDecision.Accept);
    public int RejectedCount => _items.Count(i => i.Decision == ReviewDecision.Reject);
    public int ModifiedCount => _items.Count(i => i.Decision == ReviewDecision.Modify);
    public int PendingCount => _items.Count(i => i.Decision == ReviewDecision.Pending);

    public ReviewSession(EvaluationData evaluation, string evaluationFilePath)
    {
        _evaluation = evaluation;
        _evaluationFilePath = evaluationFilePath;

        foreach (var gt in evaluation.GroundTruth)
        {
            _items.Add(new ReviewItem
            {
                Text = gt.Text,
                Subtype = gt.Subtype,
                Confidence = gt.Confidence
            });
        }

        ComputeHighlights();
    }

    private void ComputeHighlights()
    {
        _highlights.Clear();
        var transcript = _evaluation.FullTranscript;
        if (string.IsNullOrEmpty(transcript))
            return;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var text = item.ModifiedText ?? item.Text;

            // Try exact case-insensitive match first
            var pos = transcript.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (pos >= 0)
            {
                _highlights.Add(new HighlightRegion(pos, text.Length, i));
                continue;
            }

            // Fallback to Levenshtein sliding window
            var bestPos = FindBestFuzzyMatch(transcript, text);
            if (bestPos >= 0)
            {
                _highlights.Add(new HighlightRegion(bestPos, text.Length, i));
            }
            // Items that can't be located still appear in the review list but without a highlight
        }
    }

    private static int FindBestFuzzyMatch(string transcript, string query)
    {
        if (string.IsNullOrEmpty(query) || query.Length > transcript.Length)
            return -1;

        var windowSize = query.Length;
        double bestSimilarity = 0;
        int bestPos = -1;
        const double minSimilarity = 0.6;

        // Step through transcript in chunks for performance
        var step = Math.Max(1, windowSize / 4);
        for (int i = 0; i <= transcript.Length - windowSize; i += step)
        {
            var window = transcript.Substring(i, windowSize);
            var similarity = TranscriptExtractor.CalculateSimilarity(window, query);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestPos = i;
            }
        }

        // Refine around best position
        if (bestPos >= 0 && step > 1)
        {
            var refineStart = Math.Max(0, bestPos - step);
            var refineEnd = Math.Min(transcript.Length - windowSize, bestPos + step);
            for (int i = refineStart; i <= refineEnd; i++)
            {
                var window = transcript.Substring(i, windowSize);
                var similarity = TranscriptExtractor.CalculateSimilarity(window, query);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestPos = i;
                }
            }
        }

        return bestSimilarity >= minSimilarity ? bestPos : -1;
    }

    public void NavigateNext()
    {
        if (_items.Count > 0)
            CurrentIndex = Math.Min(CurrentIndex + 1, _items.Count - 1);
    }

    public void NavigatePrevious()
    {
        CurrentIndex = Math.Max(CurrentIndex - 1, 0);
    }

    public void NavigateTo(int index)
    {
        if (index >= 0 && index < _items.Count)
            CurrentIndex = index;
    }

    public void Accept()
    {
        if (CurrentIndex < _items.Count)
            _items[CurrentIndex].Decision = ReviewDecision.Accept;
    }

    public void Reject()
    {
        if (CurrentIndex < _items.Count)
            _items[CurrentIndex].Decision = ReviewDecision.Reject;
    }

    public void ModifyText(string newText)
    {
        if (CurrentIndex < _items.Count)
        {
            _items[CurrentIndex].Decision = ReviewDecision.Modify;
            _items[CurrentIndex].ModifiedText = newText;
            ComputeHighlights();
        }
    }

    public void SetSubtype(string? subtype)
    {
        if (CurrentIndex < _items.Count)
        {
            _items[CurrentIndex].ModifiedSubtype = subtype;
            if (_items[CurrentIndex].Decision == ReviewDecision.Pending)
                _items[CurrentIndex].Decision = ReviewDecision.Modify;
        }
    }

    public void AddMissedQuestion(string text, string? subtype)
    {
        _items.Add(new ReviewItem
        {
            Text = text,
            Subtype = subtype,
            Decision = ReviewDecision.Accept,
            Source = "human-added"
        });
        ComputeHighlights();
        CurrentIndex = _items.Count - 1;
    }

    public string GetReviewFilePath()
    {
        var dir = Path.GetDirectoryName(_evaluationFilePath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(_evaluationFilePath);
        return Path.Combine(dir, $"{baseName}-review.json");
    }

    public string GetValidatedFilePath()
    {
        var dir = Path.GetDirectoryName(_evaluationFilePath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(_evaluationFilePath);
        return Path.Combine(dir, $"{baseName}-validated.json");
    }

    public async Task SaveSessionAsync()
    {
        var sessionData = new ReviewSessionData
        {
            EvaluationFile = _evaluationFilePath,
            CurrentIndex = CurrentIndex,
            Items = _items.Select(i => new ReviewItemData
            {
                Text = i.Text,
                Subtype = i.Subtype,
                Confidence = i.Confidence,
                Decision = i.Decision.ToString(),
                ModifiedText = i.ModifiedText,
                ModifiedSubtype = i.ModifiedSubtype,
                Source = i.Source
            }).ToList()
        };

        var json = JsonSerializer.Serialize(sessionData, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetReviewFilePath(), json);
    }

    public async Task<bool> TryResumeAsync()
    {
        var reviewPath = GetReviewFilePath();
        if (!File.Exists(reviewPath))
            return false;

        var json = await File.ReadAllTextAsync(reviewPath);
        var sessionData = JsonSerializer.Deserialize<ReviewSessionData>(json);
        if (sessionData == null)
            return false;

        // Restore decisions and modifications
        for (int i = 0; i < Math.Min(sessionData.Items.Count, _items.Count); i++)
        {
            var saved = sessionData.Items[i];
            _items[i].Decision = Enum.TryParse<ReviewDecision>(saved.Decision, out var d) ? d : ReviewDecision.Pending;
            _items[i].ModifiedText = saved.ModifiedText;
            _items[i].ModifiedSubtype = saved.ModifiedSubtype;
            _items[i].Source = saved.Source ?? "llm";
        }

        // Restore human-added items beyond original ground truth count
        for (int i = _items.Count; i < sessionData.Items.Count; i++)
        {
            var saved = sessionData.Items[i];
            _items.Add(new ReviewItem
            {
                Text = saved.Text,
                Subtype = saved.Subtype,
                Confidence = saved.Confidence,
                Decision = Enum.TryParse<ReviewDecision>(saved.Decision, out var d) ? d : ReviewDecision.Pending,
                ModifiedText = saved.ModifiedText,
                ModifiedSubtype = saved.ModifiedSubtype,
                Source = saved.Source ?? "human-added"
            });
        }

        CurrentIndex = Math.Min(sessionData.CurrentIndex, _items.Count - 1);
        ComputeHighlights();
        return true;
    }

    public async Task FinaliseAsync()
    {
        var validated = new List<ValidatedQuestion>();

        foreach (var item in _items)
        {
            if (item.Decision == ReviewDecision.Reject)
                continue;

            var source = item.Source switch
            {
                "human-added" => "human-added",
                _ => item.Decision switch
                {
                    ReviewDecision.Accept => "llm-accepted",
                    ReviewDecision.Modify => "llm-modified",
                    _ => "llm-pending"
                }
            };

            validated.Add(new ValidatedQuestion
            {
                Text = item.ModifiedText ?? item.Text,
                Subtype = item.ModifiedSubtype ?? item.Subtype,
                Confidence = item.Confidence,
                Source = source
            });
        }

        var output = new ValidatedOutput
        {
            GeneratedAt = DateTime.UtcNow,
            EvaluationFile = Path.GetFileName(_evaluationFilePath),
            TotalReviewed = _items.Count,
            Accepted = AcceptedCount,
            Rejected = RejectedCount,
            Modified = ModifiedCount,
            Questions = validated
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetValidatedFilePath(), json);
    }
}

internal sealed class ReviewSessionData
{
    public string EvaluationFile { get; set; } = "";
    public int CurrentIndex { get; set; }
    public List<ReviewItemData> Items { get; set; } = new();
}

internal sealed class ReviewItemData
{
    public string Text { get; set; } = "";
    public string? Subtype { get; set; }
    public double Confidence { get; set; }
    public string Decision { get; set; } = "Pending";
    public string? ModifiedText { get; set; }
    public string? ModifiedSubtype { get; set; }
    public string? Source { get; set; }
}

internal sealed class ValidatedQuestion
{
    public string Text { get; set; } = "";
    public string? Subtype { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = "";
}

internal sealed class ValidatedOutput
{
    public DateTime GeneratedAt { get; set; }
    public string EvaluationFile { get; set; } = "";
    public int TotalReviewed { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Modified { get; set; }
    public List<ValidatedQuestion> Questions { get; set; } = new();
}
