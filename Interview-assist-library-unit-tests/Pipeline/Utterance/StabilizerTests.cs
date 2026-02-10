using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.UnitTests.Pipeline.Utterance;

public class StabilizerTests
{
    [Fact]
    public void AddHypothesis_SingleHypothesis_ReturnsEmptyStable()
    {
        var stabilizer = new Stabilizer();

        var result = stabilizer.AddHypothesis("What is");

        Assert.Equal("", result);
    }

    [Fact]
    public void AddHypothesis_MultipleHypotheses_ReturnsLongestCommonPrefix()
    {
        var stabilizer = new Stabilizer();

        stabilizer.AddHypothesis("What is");
        stabilizer.AddHypothesis("What is a");
        var result = stabilizer.AddHypothesis("What is a lock");

        Assert.Equal("What is", result);
    }

    [Fact]
    public void AddHypothesis_GrowingHypotheses_StableTextGrowsMonotonically()
    {
        var stabilizer = new Stabilizer();

        stabilizer.AddHypothesis("What");
        stabilizer.AddHypothesis("What is");
        stabilizer.AddHypothesis("What is a");
        Assert.Equal("What", stabilizer.StableText);

        stabilizer.AddHypothesis("What is a lock");
        stabilizer.AddHypothesis("What is a lock statement");
        stabilizer.AddHypothesis("What is a lock statement used");
        Assert.StartsWith("What is a", stabilizer.StableText);
    }

    [Fact]
    public void AddHypothesis_StableTextNeverShrinks()
    {
        var stabilizer = new Stabilizer();

        stabilizer.AddHypothesis("What is");
        stabilizer.AddHypothesis("What is a");
        stabilizer.AddHypothesis("What is a lock");

        var stableAfterGrowth = stabilizer.StableText;

        // Simulate a revision that would shrink stable text
        stabilizer.AddHypothesis("What is");
        stabilizer.AddHypothesis("What is a");

        Assert.True(stabilizer.StableText.Length >= stableAfterGrowth.Length);
    }

    [Fact]
    public void CommitFinal_ReturnsCommittedText_AndClearsStable()
    {
        var stabilizer = new Stabilizer();

        stabilizer.AddHypothesis("What is");
        stabilizer.AddHypothesis("What is a");

        var result = stabilizer.CommitFinal("What is a lock statement");

        // CommitFinal returns the committed text
        Assert.Equal("What is a lock statement", result);
        // StableText is cleared so next segment starts fresh
        Assert.Equal("", stabilizer.StableText);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var stabilizer = new Stabilizer();

        stabilizer.AddHypothesis("What is");
        stabilizer.AddHypothesis("What is a");
        stabilizer.AddHypothesis("What is a lock");

        stabilizer.Reset();

        Assert.Equal("", stabilizer.StableText);
    }

    [Fact]
    public void CommitFinal_ClearsHypothesisWindow_NextAddHypothesisStartsFresh()
    {
        var stabilizer = new Stabilizer();

        // Build up some stable text and commit
        stabilizer.AddHypothesis("Hello world");
        stabilizer.AddHypothesis("Hello world today");
        stabilizer.CommitFinal("Hello world today");

        // After commit, adding a single hypothesis should return "" (fresh start),
        // not the stale "Hello world today"
        var result = stabilizer.AddHypothesis("about");
        Assert.Equal("", result);

        // Two hypotheses should compute a new LCP from scratch
        var result2 = stabilizer.AddHypothesis("about something");
        Assert.Equal("about", result2);
    }

    [Fact]
    public void AddHypothesis_WithWordConfidence_FiltersLowConfidenceWords()
    {
        var options = new PipelineOptions
        {
            MinWordConfidence = 0.6,
            RequireRepetitionForLowConfidence = true
        };
        var stabilizer = new Stabilizer(options);

        var words = new List<AsrWord>
        {
            new() { Word = "What", Confidence = 0.95 },
            new() { Word = "is", Confidence = 0.9 },
            new() { Word = "a", Confidence = 0.3 }, // Low confidence
            new() { Word = "lock", Confidence = 0.4 } // Low confidence
        };

        stabilizer.AddHypothesis("What is a lock", words);
        stabilizer.AddHypothesis("What is a lock statement", words);
        var result = stabilizer.AddHypothesis("What is a lock statement used", words);

        // Should stop at low-confidence word unless repeated
        Assert.StartsWith("What is", result);
    }
}
