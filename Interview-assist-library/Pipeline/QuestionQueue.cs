using InterviewAssist.Library.Constants;
using System.Threading.Channels;

namespace InterviewAssist.Library.Pipeline;

/// <summary>
/// Thread-safe queue for detected questions awaiting response generation.
/// Uses bounded channel to prevent unbounded memory growth.
/// </summary>
public sealed class QuestionQueue : IDisposable
{
    private readonly Channel<QueuedQuestion> _channel;
    private readonly HashSet<string> _processedQuestionHashes = new();
    private readonly object _deduplicationLock = new();
    private readonly int _maxSize;
    private bool _disposed;

    /// <summary>
    /// Creates a new question queue with the specified maximum size.
    /// </summary>
    /// <param name="maxSize">Maximum number of queued questions. Oldest dropped when full.</param>
    public QuestionQueue(int maxSize = QueueConstants.DefaultQuestionQueueSize)
    {
        _maxSize = maxSize;
        _channel = Channel.CreateBounded<QueuedQuestion>(new BoundedChannelOptions(maxSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <summary>
    /// Attempts to enqueue a detected question for processing.
    /// Returns false if the question is a duplicate or queue is complete.
    /// </summary>
    public bool TryEnqueue(DetectedQuestion question, string context)
    {
        if (_disposed) return false;

        // Deduplicate by question text hash
        var hash = ComputeHash(question.Text);
        lock (_deduplicationLock)
        {
            if (_processedQuestionHashes.Contains(hash))
            {
                return false;
            }
            _processedQuestionHashes.Add(hash);

            // Limit hash set size to prevent unbounded growth
            if (_processedQuestionHashes.Count > _maxSize * QueueConstants.DeduplicationMultiplier)
            {
                _processedQuestionHashes.Clear();
            }
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
            _processedQuestionHashes.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Complete();
    }

    private static string ComputeHash(string text)
    {
        // Simple normalized hash for deduplication
        var normalized = text.ToLowerInvariant().Trim();
        return normalized.GetHashCode().ToString();
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
