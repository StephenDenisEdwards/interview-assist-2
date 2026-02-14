using System.Text.Json;

namespace InterviewAssist.Library.Utilities;

/// <summary>
/// Thread-safe, shared JSON serialization presets for the pipeline.
/// Each property auto-freezes on first use, making instances safe for concurrent access.
/// </summary>
public static class PipelineJsonOptions
{
    /// <summary>camelCase property names (default WriteIndented=false).</summary>
    public static JsonSerializerOptions CamelCase { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>camelCase property names with indented output.</summary>
    public static JsonSerializerOptions CamelCasePretty { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Case-insensitive property matching for deserialization.</summary>
    public static JsonSerializerOptions CaseInsensitive { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Compact output (WriteIndented=false, no naming policy).</summary>
    public static JsonSerializerOptions Compact { get; } = new()
    {
        WriteIndented = false
    };

    /// <summary>Pretty-printed output (WriteIndented=true, no naming policy).</summary>
    public static JsonSerializerOptions Pretty { get; } = new()
    {
        WriteIndented = true
    };
}
