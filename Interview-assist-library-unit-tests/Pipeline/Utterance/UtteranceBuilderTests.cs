using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.UnitTests.Pipeline.Utterance;

public class UtteranceBuilderTests
{
    private DateTime _currentTime = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private UtteranceBuilder CreateBuilder(PipelineOptions? options = null)
    {
        return new UtteranceBuilder(options, clock: () => _currentTime);
    }

    private void AdvanceTime(TimeSpan duration)
    {
        _currentTime = _currentTime.Add(duration);
    }

    [Fact]
    public void ProcessAsrEvent_FirstWord_OpensUtterance()
    {
        var builder = CreateBuilder();
        UtteranceEvent? openEvent = null;
        builder.OnUtteranceOpen += e => openEvent = e;

        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello", IsFinal = false });

        Assert.NotNull(openEvent);
        Assert.Equal(UtteranceEventType.Open, openEvent.Type);
        Assert.True(builder.HasActiveUtterance);
    }

    [Fact]
    public void ProcessAsrEvent_Partial_EmitsUpdate()
    {
        var builder = CreateBuilder();
        var updates = new List<UtteranceEvent>();
        builder.OnUtteranceUpdate += e => updates.Add(e);

        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello", IsFinal = false });
        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello world", IsFinal = false });

        Assert.Equal(2, updates.Count);
        Assert.Equal("Hello world", updates[1].RawText);
    }

    [Fact]
    public void SignalUtteranceEnd_ClosesUtterance()
    {
        var builder = CreateBuilder();
        UtteranceEvent? finalEvent = null;
        builder.OnUtteranceFinal += e => finalEvent = e;

        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello", IsFinal = false });
        builder.SignalUtteranceEnd();

        Assert.NotNull(finalEvent);
        Assert.Equal(UtteranceEventType.Final, finalEvent.Type);
        Assert.Equal(UtteranceCloseReason.DeepgramSignal, finalEvent.CloseReason);
        Assert.False(builder.HasActiveUtterance);
    }

    [Fact]
    public void CheckTimeouts_SilenceGap_ClosesUtterance()
    {
        var options = new PipelineOptions
        {
            SilenceGapThreshold = TimeSpan.FromMilliseconds(750)
        };
        var builder = CreateBuilder(options);
        UtteranceEvent? finalEvent = null;
        builder.OnUtteranceFinal += e => finalEvent = e;

        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello", IsFinal = false });
        AdvanceTime(TimeSpan.FromMilliseconds(800));
        builder.CheckTimeouts();

        Assert.NotNull(finalEvent);
        Assert.Equal(UtteranceCloseReason.SilenceGap, finalEvent.CloseReason);
    }

    [Fact]
    public void CheckTimeouts_MaxDuration_ClosesUtterance()
    {
        var options = new PipelineOptions
        {
            MaxUtteranceDuration = TimeSpan.FromSeconds(12)
        };
        var builder = CreateBuilder(options);
        UtteranceEvent? finalEvent = null;
        builder.OnUtteranceFinal += e => finalEvent = e;

        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello", IsFinal = false });
        AdvanceTime(TimeSpan.FromSeconds(13));
        builder.CheckTimeouts();

        Assert.NotNull(finalEvent);
        Assert.Equal(UtteranceCloseReason.MaxDuration, finalEvent.CloseReason);
    }

    [Fact]
    public void CheckTimeouts_TerminalPunctuationWithPause_ClosesUtterance()
    {
        var options = new PipelineOptions
        {
            PunctuationPauseThreshold = TimeSpan.FromMilliseconds(300),
            SilenceGapThreshold = TimeSpan.FromMilliseconds(750) // Longer than punctuation
        };
        var builder = CreateBuilder(options);
        UtteranceEvent? finalEvent = null;
        builder.OnUtteranceFinal += e => finalEvent = e;

        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello.", IsFinal = false });
        AdvanceTime(TimeSpan.FromMilliseconds(350)); // Past punctuation threshold but before silence gap
        builder.CheckTimeouts();

        Assert.NotNull(finalEvent);
        Assert.Equal(UtteranceCloseReason.TerminalPunctuation, finalEvent.CloseReason);
    }

    [Fact]
    public void ProcessAsrEvent_MaxLength_ClosesUtterance()
    {
        var options = new PipelineOptions
        {
            MaxUtteranceLength = 20
        };
        var builder = CreateBuilder(options);
        UtteranceEvent? finalEvent = null;
        builder.OnUtteranceFinal += e => finalEvent = e;

        builder.ProcessAsrEvent(new AsrEvent { Text = "This is a very long text that exceeds the maximum length", IsFinal = false });

        Assert.NotNull(finalEvent);
        Assert.Equal(UtteranceCloseReason.MaxLength, finalEvent.CloseReason);
    }

    [Fact]
    public void ProcessAsrEvent_FinalSegment_CommitsToStable()
    {
        var builder = CreateBuilder();
        var updates = new List<UtteranceEvent>();
        builder.OnUtteranceUpdate += e => updates.Add(e);

        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello", IsFinal = false });
        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello world", IsFinal = true });

        var lastUpdate = updates.Last();
        Assert.Contains("Hello world", lastUpdate.RawText);
    }

    [Fact]
    public void ForceClose_ClosesWithManualReason()
    {
        var builder = CreateBuilder();
        UtteranceEvent? finalEvent = null;
        builder.OnUtteranceFinal += e => finalEvent = e;

        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello", IsFinal = false });
        builder.ForceClose();

        Assert.NotNull(finalEvent);
        Assert.Equal(UtteranceCloseReason.Manual, finalEvent.CloseReason);
    }

    [Fact]
    public void FinalEvent_CommittedAsrTimestamps_MatchesFinalAsrEvents()
    {
        var builder = CreateBuilder();
        UtteranceEvent? finalEvent = null;
        builder.OnUtteranceFinal += e => finalEvent = e;

        var time1 = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc);
        var time2 = new DateTime(2024, 1, 1, 12, 0, 3, DateTimeKind.Utc);

        // First final ASR event
        builder.ProcessAsrEvent(new AsrEvent { Text = "What is a lock", IsFinal = true, ReceivedAtUtc = time1 });

        // Second final ASR event
        builder.ProcessAsrEvent(new AsrEvent { Text = "statement in C#?", IsFinal = true, ReceivedAtUtc = time2 });

        // Close the utterance
        builder.SignalUtteranceEnd();

        Assert.NotNull(finalEvent);
        Assert.NotNull(finalEvent.CommittedAsrTimestamps);
        Assert.Equal(2, finalEvent.CommittedAsrTimestamps.Count);
        Assert.Equal(time1, finalEvent.CommittedAsrTimestamps[0]);
        Assert.Equal(time2, finalEvent.CommittedAsrTimestamps[1]);
    }

    [Fact]
    public void FinalEvent_NoFinals_CommittedAsrTimestampsIsNull()
    {
        var builder = CreateBuilder();
        UtteranceEvent? finalEvent = null;
        builder.OnUtteranceFinal += e => finalEvent = e;

        // Only partials, no finals committed
        builder.ProcessAsrEvent(new AsrEvent { Text = "Hello world", IsFinal = false });
        builder.SignalUtteranceEnd();

        Assert.NotNull(finalEvent);
        Assert.Null(finalEvent.CommittedAsrTimestamps);
    }

    [Fact]
    public void ProcessAsrEvent_SplitQuestion_BuildsCompleteUtterance()
    {
        // Simulates: "What is a lock statement" followed by "used for in C#?"
        var builder = CreateBuilder();
        var updates = new List<UtteranceEvent>();
        builder.OnUtteranceUpdate += e => updates.Add(e);

        builder.ProcessAsrEvent(new AsrEvent { Text = "What is", IsFinal = false });
        builder.ProcessAsrEvent(new AsrEvent { Text = "What is a", IsFinal = false });
        builder.ProcessAsrEvent(new AsrEvent { Text = "What is a lock", IsFinal = false });
        builder.ProcessAsrEvent(new AsrEvent { Text = "What is a lock statement", IsFinal = true });
        builder.ProcessAsrEvent(new AsrEvent { Text = "used for", IsFinal = false });
        builder.ProcessAsrEvent(new AsrEvent { Text = "used for in C#?", IsFinal = true });

        var lastUpdate = updates.Last();
        Assert.Contains("What is a lock statement", lastUpdate.RawText);
        Assert.Contains("used for in C#?", lastUpdate.RawText);
    }
}
