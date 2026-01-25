using InterviewAssist.Library.Constants;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// Thread-safe queue for detected questions awaiting response generation.
/// Uses bounded channel to prevent unbounded memory growth.
/// Implements Jaccard similarity deduplication with time-based suppression.
/// </summary>
public sealed class QuestionQueue : IDisposable
{
    private readonly Channel<QueuedQuestion> _channel;
    private readonly Dictionary<string, DateTime> _processedQuestions = new();
    private readonly object _deduplicationLock = new();
    private readonly int _maxSize;
    private readonly double _similarityThreshold;
    private readonly int _suppressionWindowMs;
    private bool _disposed;

    /// <summary>
    /// Creates a new question queue with the specified configuration.
    /// </summary>
    /// <param name="maxSize">Maximum number of queued questions. Oldest dropped when full.</param>
    /// <param name="similarityThreshold">Jaccard similarity threshold (0.0-1.0). Default: 0.7.</param>
    /// <param name="suppressionWindowMs">Time window for suppression in ms. Default: 30000.</param>
    public QuestionQueue(
        int maxSize = QueueConstants.DefaultQuestionQueueSize,
        double similarityThreshold = 0.7,
        int suppressionWindowMs = 30000)
    {
        _maxSize = maxSize;
        _similarityThreshold = similarityThreshold;
        _suppressionWindowMs = suppressionWindowMs;
        _channel = Channel.CreateBounded<QueuedQuestion>(new BoundedChannelOptions(maxSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <summary>
    /// Attempts to enqueue a detected question for processing.
    /// Returns false if the question is similar to a recent one or queue is complete.
    /// </summary>
    public bool TryEnqueue(DetectedQuestion question, string context)
    {
        if (_disposed) return false;

        var normalizedText = NormalizeForComparison(question.Text);

        lock (_deduplicationLock)
        {
            // Expire old entries
            ExpireOldEntries();

            // Check for similar questions
            foreach (var existingText in _processedQuestions.Keys)
            {
                if (IsSimilar(normalizedText, existingText))
                {
                    return false;
                }
            }

            // Add to processed set
            _processedQuestions[normalizedText] = DateTime.UtcNow;
        }

        var queued = new QueuedQuestion
        {
            Question = question,
            FullContext = context,
            EnqueuedAt = DateTime.UtcNow
        };

        return _channel.Writer.TryWrite(queued);
    }

    /// <summary>
    /// Reads all queued questions as they become available.
    /// </summary>
    public IAsyncEnumerable<QueuedQuestion> ReadAllAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }

    /// <summary>
    /// Gets the approximate number of items currently in the queue.
    /// </summary>
    public int Count => _channel.Reader.Count;

    /// <summary>
    /// Marks the queue as complete - no more items can be added.
    /// </summary>
    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    /// <summary>
    /// Clears the deduplication cache, allowing previously seen questions to be re-queued.
    /// </summary>
    public void ClearDeduplicationCache()
    {
        lock (_deduplicationLock)
        {
            _processedQuestions.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Complete();
    }

    /// <summary>
    /// Normalizes text for comparison by removing punctuation and normalizing whitespace.
    /// </summary>
    private static string NormalizeForComparison(string text)
    {
        // Remove punctuation, lowercase, normalize whitespace
        var normalized = text.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[^\w\s]", "");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }

    /// <summary>
    /// Calculates Jaccard similarity between two texts based on word sets.
    /// </summary>
    private bool IsSimilar(string text1, string text2)
    {
        var words1 = new HashSet<string>(text1.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var words2 = new HashSet<string>(text2.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (words1.Count == 0 || words2.Count == 0) return false;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        double jaccard = (double)intersection / union;
        return jaccard >= _similarityThreshold;
    }

    /// <summary>
    /// Removes entries older than the suppression window.
    /// </summary>
    private void ExpireOldEntries()
    {
        var expiredKeys = _processedQuestions
            .Where(kvp => (DateTime.UtcNow - kvp.Value).TotalMilliseconds > _suppressionWindowMs)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _processedQuestions.Remove(key);
        }
    }
}

/// <summary>
/// A question queued for response generation.
/// </summary>
public record QueuedQuestion
{
    /// <summary>
    /// The detected question.
    /// </summary>
    public required DetectedQuestion Question { get; init; }

    /// <summary>
    /// Full transcript context at time of detection (for follow-up understanding).
    /// </summary>
    public required string FullContext { get; init; }

    /// <summary>
    /// When the question was added to the queue.
    /// </summary>
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}
