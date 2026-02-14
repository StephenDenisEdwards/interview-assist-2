using System.Text.Json;
using InterviewAssist.Library.Utilities;

namespace InterviewAssist.Library.Pipeline.Evaluation;

/// <summary>
/// Loads and validates curated evaluation datasets.
/// </summary>
public sealed class DatasetLoader
{
    /// <summary>
    /// Load a dataset from a JSONL file.
    /// </summary>
    public static async Task<EvaluationDataset> LoadAsync(string filePath, CancellationToken ct = default)
    {
        var items = new List<DatasetItem>();
        var lineNumber = 0;

        await foreach (var line in File.ReadLinesAsync(filePath, ct))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//"))
                continue;

            try
            {
                var item = JsonSerializer.Deserialize<DatasetItem>(line, PipelineJsonOptions.CaseInsensitive);

                if (item != null && !string.IsNullOrWhiteSpace(item.Text))
                {
                    items.Add(item);
                }
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Warning: Failed to parse line {lineNumber}: {ex.Message}");
            }
        }

        return new EvaluationDataset(
            FilePath: filePath,
            Items: items,
            QuestionCount: items.Count(i => i.Type == "Question"),
            StatementCount: items.Count(i => i.Type == "Statement"),
            CommandCount: items.Count(i => i.Type == "Command"));
    }

    /// <summary>
    /// Load multiple datasets and merge them.
    /// </summary>
    public static async Task<EvaluationDataset> LoadMultipleAsync(
        IEnumerable<string> filePaths,
        CancellationToken ct = default)
    {
        var allItems = new List<DatasetItem>();

        foreach (var path in filePaths)
        {
            var dataset = await LoadAsync(path, ct);
            allItems.AddRange(dataset.Items);
        }

        return new EvaluationDataset(
            FilePath: "merged",
            Items: allItems,
            QuestionCount: allItems.Count(i => i.Type == "Question"),
            StatementCount: allItems.Count(i => i.Type == "Statement"),
            CommandCount: allItems.Count(i => i.Type == "Command"));
    }

    /// <summary>
    /// Validate a dataset for completeness and consistency.
    /// </summary>
    public static DatasetValidationResult Validate(EvaluationDataset dataset)
    {
        var issues = new List<string>();
        var validTypes = new[] { "Question", "Statement", "Command" };

        for (var i = 0; i < dataset.Items.Count; i++)
        {
            var item = dataset.Items[i];

            if (string.IsNullOrWhiteSpace(item.Text))
            {
                issues.Add($"Item {i + 1}: Empty text");
            }

            if (string.IsNullOrWhiteSpace(item.Type))
            {
                issues.Add($"Item {i + 1}: Missing type");
            }
            else if (!validTypes.Contains(item.Type))
            {
                issues.Add($"Item {i + 1}: Invalid type '{item.Type}'");
            }
        }

        // Check for duplicates
        var duplicates = dataset.Items
            .GroupBy(i => i.Text.ToLowerInvariant().Trim())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var dup in duplicates)
        {
            issues.Add($"Duplicate text: \"{dup[..Math.Min(50, dup.Length)]}...\"");
        }

        return new DatasetValidationResult(
            IsValid: issues.Count == 0,
            Issues: issues,
            TotalItems: dataset.Items.Count,
            UniqueItems: dataset.Items.Select(i => i.Text.ToLowerInvariant().Trim()).Distinct().Count());
    }
}

/// <summary>
/// A single item in the evaluation dataset.
/// </summary>
public sealed class DatasetItem
{
    public string Text { get; init; } = "";
    public string Type { get; init; } = ""; // Question, Statement, Command
    public string? Subtype { get; init; }
    public string? Context { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// A loaded evaluation dataset.
/// </summary>
public sealed record EvaluationDataset(
    string FilePath,
    IReadOnlyList<DatasetItem> Items,
    int QuestionCount,
    int StatementCount,
    int CommandCount);

/// <summary>
/// Result of dataset validation.
/// </summary>
public sealed record DatasetValidationResult(
    bool IsValid,
    IReadOnlyList<string> Issues,
    int TotalItems,
    int UniqueItems);
