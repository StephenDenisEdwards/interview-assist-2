using InterviewAssist.Library.Pipeline;

namespace InterviewAssist.Library.UnitTests.Pipeline;

public class QuestionQueueTests
{
    private static DetectedQuestion CreateQuestion(string text = "What is dependency injection?")
    {
        return new DetectedQuestion
        {
            Text = text,
            Confidence = 0.9,
            Type = QuestionType.Question
        };
    }

    [Fact]
    public void TryEnqueue_NewQuestion_ReturnsTrue()
    {
        // Arrange
        var queue = new QuestionQueue();
        var question = CreateQuestion();

        // Act
        var result = queue.TryEnqueue(question, "context");

        // Assert
        Assert.True(result);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryEnqueue_DuplicateQuestion_ReturnsFalse()
    {
        // Arrange
        var queue = new QuestionQueue();
        var question = CreateQuestion("What is async/await?");

        // Act
        queue.TryEnqueue(question, "context");
        var result = queue.TryEnqueue(CreateQuestion("What is async/await?"), "context");

        // Assert
        Assert.False(result);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryEnqueue_SameTextDifferentCase_TreatedAsDuplicate()
    {
        // Arrange
        var queue = new QuestionQueue();

        // Act
        queue.TryEnqueue(CreateQuestion("What is DI?"), "context");
        var result = queue.TryEnqueue(CreateQuestion("what is di?"), "context");

        // Assert
        Assert.False(result);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryEnqueue_SameTextWithWhitespace_TreatedAsDuplicate()
    {
        // Arrange
        var queue = new QuestionQueue();

        // Act
        queue.TryEnqueue(CreateQuestion("What is DI?"), "context");
        var result = queue.TryEnqueue(CreateQuestion("  What is DI?  "), "context");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryEnqueue_DifferentQuestions_BothAccepted()
    {
        // Arrange
        var queue = new QuestionQueue();

        // Act
        var result1 = queue.TryEnqueue(CreateQuestion("Question one?"), "context");
        var result2 = queue.TryEnqueue(CreateQuestion("Question two?"), "context");

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void TryEnqueue_AfterDispose_ReturnsFalse()
    {
        // Arrange
        var queue = new QuestionQueue();
        queue.Dispose();

        // Act
        var result = queue.TryEnqueue(CreateQuestion(), "context");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryEnqueue_ExceedMaxSize_DropsOldest()
    {
        // Arrange
        var queue = new QuestionQueue(maxSize: 2);

        // Act
        queue.TryEnqueue(CreateQuestion("First?"), "context");
        queue.TryEnqueue(CreateQuestion("Second?"), "context");
        queue.TryEnqueue(CreateQuestion("Third?"), "context");

        // Assert - queue should still have max 2 items (oldest dropped)
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsEnqueuedItems()
    {
        // Arrange
        var queue = new QuestionQueue();
        queue.TryEnqueue(CreateQuestion("First?"), "context1");
        queue.TryEnqueue(CreateQuestion("Second?"), "context2");
        queue.Complete();

        // Act
        var items = new List<QueuedQuestion>();
        await foreach (var item in queue.ReadAllAsync())
        {
            items.Add(item);
        }

        // Assert
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ReadAllAsync_WithCancellation_StopsReading()
    {
        // Arrange
        var queue = new QuestionQueue();
        queue.TryEnqueue(CreateQuestion(), "context");
        var cts = new CancellationTokenSource();

        // Act
        var items = new List<QueuedQuestion>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var item in queue.ReadAllAsync(cts.Token))
            {
                items.Add(item);
            }
        });

        await Task.Delay(100);
        cts.Cancel();

        // Assert - should not throw
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
    }

    [Fact]
    public void Complete_PreventsNewEnqueues()
    {
        // Arrange
        var queue = new QuestionQueue();
        queue.TryEnqueue(CreateQuestion("Before complete"), "context");

        // Act
        queue.Complete();
        var result = queue.TryEnqueue(CreateQuestion("After complete"), "context");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ClearDeduplicationCache_AllowsReenqueue()
    {
        // Arrange
        var queue = new QuestionQueue();
        queue.TryEnqueue(CreateQuestion("Same question?"), "context");

        // Verify duplicate is rejected
        Assert.False(queue.TryEnqueue(CreateQuestion("Same question?"), "context"));

        // Act
        queue.ClearDeduplicationCache();

        // Assert - same question can now be enqueued
        Assert.True(queue.TryEnqueue(CreateQuestion("Same question?"), "context"));
    }

    [Fact]
    public void TryEnqueue_SimilarQuestions_Deduplicated()
    {
        // Arrange - use default similarity threshold of 0.7
        var queue = new QuestionQueue(maxSize: 5, similarityThreshold: 0.7);

        // Act - add a question and then a similar one
        var result1 = queue.TryEnqueue(CreateQuestion("What is a lock statement in C#?"), "context");
        var result2 = queue.TryEnqueue(CreateQuestion("What is a lock statement?"), "context"); // Similar

        // Assert - first succeeds, similar one is deduplicated
        Assert.True(result1);
        Assert.False(result2);
    }

    [Fact]
    public void TryEnqueue_DifferentQuestions_NotDeduplicated()
    {
        // Arrange
        var queue = new QuestionQueue(maxSize: 5, similarityThreshold: 0.7);

        // Act - add completely different questions
        var result1 = queue.TryEnqueue(CreateQuestion("What is a lock statement?"), "context");
        var result2 = queue.TryEnqueue(CreateQuestion("How does garbage collection work?"), "context");

        // Assert - both succeed because they're different
        Assert.True(result1);
        Assert.True(result2);
    }

    [Fact]
    public void TryEnqueue_AfterSuppressionWindow_AllowsReenqueue()
    {
        // Arrange - very short suppression window for testing
        var queue = new QuestionQueue(maxSize: 5, similarityThreshold: 0.7, suppressionWindowMs: 1);

        // Act - add a question
        queue.TryEnqueue(CreateQuestion("What is async await?"), "context");

        // Wait for suppression window to expire
        Thread.Sleep(10);

        // Try to add the same question again
        var result = queue.TryEnqueue(CreateQuestion("What is async await?"), "context");

        // Assert - can re-add after suppression window expired
        Assert.True(result);
    }

    [Fact]
    public void QueuedQuestion_HasCorrectProperties()
    {
        // Arrange
        var queue = new QuestionQueue();
        var question = CreateQuestion("Test question?");
        var context = "Full context here";
        queue.TryEnqueue(question, context);
        queue.Complete();

        // Act
        QueuedQuestion? queued = null;
        var enumerator = queue.ReadAllAsync().GetAsyncEnumerator();
        if (enumerator.MoveNextAsync().AsTask().Result)
        {
            queued = enumerator.Current;
        }

        // Assert
        Assert.NotNull(queued);
        Assert.Equal("Test question?", queued.Question.Text);
        Assert.Equal(context, queued.FullContext);
        Assert.True(queued.EnqueuedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var queue = new QuestionQueue();

        // Act & Assert - should not throw
        queue.Dispose();
        queue.Dispose();
        queue.Dispose();
    }

    [Fact]
    public void Count_ReturnsApproximateQueueSize()
    {
        // Arrange
        var queue = new QuestionQueue();

        // Act & Assert
        Assert.Equal(0, queue.Count);

        queue.TryEnqueue(CreateQuestion("Q1?"), "c");
        Assert.Equal(1, queue.Count);

        queue.TryEnqueue(CreateQuestion("Q2?"), "c");
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void ThreadSafety_ConcurrentEnqueue_NoExceptions()
    {
        // Arrange
        var queue = new QuestionQueue(maxSize: 100);
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 50; j++)
                    {
                        queue.TryEnqueue(CreateQuestion($"Q{index}-{j}?"), "context");
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.Empty(exceptions);
    }
}
