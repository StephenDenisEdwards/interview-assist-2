namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// Rolling buffer of transcript entries with timestamps for maintaining conversation context.
/// Thread-safe for concurrent access from transcription and detection loops.
/// </summary>
public sealed class TranscriptBuffer
{
    private readonly int _maxAgeSeconds;
    private readonly List<TranscriptEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new transcript buffer with the specified maximum age.
    /// </summary>
    /// <param name="maxAgeSeconds">Maximum age of entries before they are pruned.</param>
    public TranscriptBuffer(int maxAgeSeconds = 30)
    {
        _maxAgeSeconds = maxAgeSeconds;
    }

    /// <summary>
    /// Adds a transcript entry with the current timestamp.
    /// </summary>
    public void Add(string text)
    {
        Add(text, DateTime.UtcNow);
    }

    /// <summary>
    /// Adds a transcript entry with a specific timestamp.
    /// </summary>
    public void Add(string text, DateTime timestamp)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            _entries.Add(new TranscriptEntry(text.Trim(), timestamp));
            PruneUnlocked();
        }
    }

    /// <summary>
    /// Gets all transcript text from the last N seconds, concatenated with spaces.
    /// </summary>
    public string GetRecentText(int lookbackSeconds)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-lookbackSeconds);
            var recentEntries = _entries
                .Where(e => e.Timestamp >= cutoff)
                .OrderBy(e => e.Timestamp)
                .Select(e => e.Text);

            return string.Join(" ", recentEntries);
        }
    }

    /// <summary>
    /// Gets all transcript text in the buffer, concatenated with spaces.
    /// </summary>
    public string GetAllText()
    {
        lock (_lock)
        {
            var orderedEntries = _entries
                .OrderBy(e => e.Timestamp)
                .Select(e => e.Text);

            return string.Join(" ", orderedEntries);
        }
    }

    /// <summary>
    /// Gets transcript entries from the last N seconds.
    /// </summary>
    public IReadOnlyList<TranscriptEntry> GetRecentEntries(int lookbackSeconds)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-lookbackSeconds);
            return _entries
                .Where(e => e.Timestamp >= cutoff)
                .OrderBy(e => e.Timestamp)
                .ToList();
        }
    }

    /// <summary>
    /// Gets the timestamp of the most recent entry, or null if buffer is empty.
    /// </summary>
    public DateTime? GetLastEntryTimestamp()
    {
        lock (_lock)
        {
            return _entries.Count > 0
                ? _entries.Max(e => e.Timestamp)
                : null;
        }
    }

    /// <summary>
    /// Removes entries older than the maximum age.
    /// </summary>
    public void Prune()
    {
        lock (_lock)
        {
            PruneUnlocked();
        }
    }

    /// <summary>
    /// Clears all entries from the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    /// <summary>
    /// Gets the number of entries in the buffer.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    private void PruneUnlocked()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_maxAgeSeconds);
        _entries.RemoveAll(e => e.Timestamp < cutoff);
    }
}

/// <summary>
/// A single transcript entry with text and timestamp.
/// </summary>
public record TranscriptEntry(string Text, DateTime Timestamp);
