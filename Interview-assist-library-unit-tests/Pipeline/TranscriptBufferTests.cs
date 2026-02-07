using InterviewAssist.Library.Pipeline;

namespace InterviewAssist.Library.UnitTests.Pipeline;

public class TranscriptBufferTests
{
    [Fact]
    public void Add_SingleEntry_IncreasesCount()
    {
        // Arrange
        var buffer = new TranscriptBuffer();

        // Act
        buffer.Add("Hello world");

        // Assert
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void Add_MultipleEntries_TracksAllEntries()
    {
        // Arrange
        var buffer = new TranscriptBuffer();

        // Act
        buffer.Add("First");
        buffer.Add("Second");
        buffer.Add("Third");

        // Assert
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void Add_NullOrWhitespace_DoesNotAddEntry()
    {
        // Arrange
        var buffer = new TranscriptBuffer();

        // Act
        buffer.Add(null!);
        buffer.Add("");
        buffer.Add("   ");

        // Assert
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Add_TrimsWhitespace()
    {
        // Arrange
        var buffer = new TranscriptBuffer();

        // Act
        buffer.Add("  Hello world  ");

        // Assert
        Assert.Equal("Hello world", buffer.GetAllText());
    }

    [Fact]
    public void GetAllText_ReturnsSpaceSeparatedEntries()
    {
        // Arrange
        var buffer = new TranscriptBuffer();
        buffer.Add("Hello");
        buffer.Add("world");

        // Act
        var result = buffer.GetAllText();

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void GetAllText_EmptyBuffer_ReturnsEmptyString()
    {
        // Arrange
        var buffer = new TranscriptBuffer();

        // Act
        var result = buffer.GetAllText();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetRecentText_ReturnsOnlyRecentEntries()
    {
        // Arrange
        var buffer = new TranscriptBuffer();
        var now = DateTime.UtcNow;

        buffer.Add("Old entry", now.AddSeconds(-10));
        buffer.Add("Recent entry", now);

        // Act
        var result = buffer.GetRecentText(5);

        // Assert
        Assert.Equal("Recent entry", result);
    }

    [Fact]
    public void GetRecentText_AllEntriesRecent_ReturnsAll()
    {
        // Arrange
        var buffer = new TranscriptBuffer();
        var now = DateTime.UtcNow;

        buffer.Add("First", now.AddSeconds(-2));
        buffer.Add("Second", now.AddSeconds(-1));
        buffer.Add("Third", now);

        // Act
        var result = buffer.GetRecentText(10);

        // Assert
        Assert.Equal("First Second Third", result);
    }

    [Fact]
    public void GetRecentEntries_ReturnsEntriesWithinTimeWindow()
    {
        // Arrange
        var buffer = new TranscriptBuffer();
        var now = DateTime.UtcNow;

        buffer.Add("Old", now.AddSeconds(-20));
        buffer.Add("Recent1", now.AddSeconds(-3));
        buffer.Add("Recent2", now);

        // Act
        var entries = buffer.GetRecentEntries(10);

        // Assert
        Assert.Equal(2, entries.Count);
        Assert.Equal("Recent1", entries[0].Text);
        Assert.Equal("Recent2", entries[1].Text);
    }

    [Fact]
    public void GetLastEntryTimestamp_ReturnsLatestTimestamp()
    {
        // Arrange
        var buffer = new TranscriptBuffer();
        var now = DateTime.UtcNow;
        var earlier = now.AddSeconds(-10);

        buffer.Add("First", earlier);
        buffer.Add("Second", now);

        // Act
        var timestamp = buffer.GetLastEntryTimestamp();

        // Assert
        Assert.NotNull(timestamp);
        Assert.Equal(now, timestamp.Value);
    }

    [Fact]
    public void GetLastEntryTimestamp_EmptyBuffer_ReturnsNull()
    {
        // Arrange
        var buffer = new TranscriptBuffer();

        // Act
        var timestamp = buffer.GetLastEntryTimestamp();

        // Assert
        Assert.Null(timestamp);
    }

    [Fact]
    public void Prune_RemovesOldEntries()
    {
        // Arrange
        var buffer = new TranscriptBuffer(maxAgeSeconds: 5);
        var now = DateTime.UtcNow;

        buffer.Add("Old entry", now.AddSeconds(-10));
        buffer.Add("Recent entry", now);

        // Act
        buffer.Prune();

        // Assert
        Assert.Equal(1, buffer.Count);
        Assert.Equal("Recent entry", buffer.GetAllText());
    }

    [Fact]
    public void Add_AutomaticallyPrunesOldEntries()
    {
        // Arrange
        var buffer = new TranscriptBuffer(maxAgeSeconds: 5);
        var now = DateTime.UtcNow;

        buffer.Add("Old entry", now.AddSeconds(-10));

        // Act - adding a new entry triggers pruning
        buffer.Add("New entry", now);

        // Assert
        Assert.Equal(1, buffer.Count);
        Assert.Equal("New entry", buffer.GetAllText());
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        // Arrange
        var buffer = new TranscriptBuffer();
        buffer.Add("Entry 1");
        buffer.Add("Entry 2");

        // Act
        buffer.Clear();

        // Assert
        Assert.Equal(0, buffer.Count);
        Assert.Equal(string.Empty, buffer.GetAllText());
    }

    [Fact]
    public void Constructor_CustomMaxAge_UsesProvidedValue()
    {
        // Arrange
        var buffer = new TranscriptBuffer(maxAgeSeconds: 60);
        var now = DateTime.UtcNow;

        buffer.Add("Entry at 50s ago", now.AddSeconds(-50));
        buffer.Add("Recent entry", now);

        // Act
        buffer.Prune();

        // Assert - 50s ago is within 60s max age
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void GetRecentEntries_OrderedByTimestamp()
    {
        // Arrange
        var buffer = new TranscriptBuffer();
        var now = DateTime.UtcNow;

        // Add out of order
        buffer.Add("Third", now);
        buffer.Add("First", now.AddSeconds(-10));
        buffer.Add("Second", now.AddSeconds(-5));

        // Act
        var entries = buffer.GetRecentEntries(30);

        // Assert
        Assert.Equal(3, entries.Count);
        Assert.Equal("First", entries[0].Text);
        Assert.Equal("Second", entries[1].Text);
        Assert.Equal("Third", entries[2].Text);
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentAddAndRead_NoExceptions()
    {
        // Arrange
        var buffer = new TranscriptBuffer();
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act - concurrent adds
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 100; j++)
                    {
                        buffer.Add($"Entry {index}-{j}");
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }));
        }

        // Concurrent reads
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 100; j++)
                    {
                        _ = buffer.GetAllText();
                        _ = buffer.GetRecentText(10);
                        _ = buffer.Count;
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentPruneAndAdd_NoExceptions()
    {
        // Arrange
        var buffer = new TranscriptBuffer(maxAgeSeconds: 1);
        var exceptions = new List<Exception>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var tasks = new List<Task>();

        // Act
        tasks.Add(Task.Run(() =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    buffer.Add($"Entry {DateTime.UtcNow.Ticks}");
                    Thread.Sleep(10);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                lock (exceptions) { exceptions.Add(ex); }
            }
        }));

        tasks.Add(Task.Run(() =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    buffer.Prune();
                    Thread.Sleep(5);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                lock (exceptions) { exceptions.Add(ex); }
            }
        }));

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions);
    }
}
