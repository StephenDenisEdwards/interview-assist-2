using System.Text;
using InterviewAssist.Library.Pipeline.Recording;

namespace InterviewAssist.Library.UnitTests.Pipeline.Recording;

public class SessionRecorderPositionTests
{
    /// <summary>
    /// Simulate a running transcript built from ASR finals:
    /// "Hello world " + "what is dependency injection " + "let me explain "
    /// Segments at offsets 1000, 3000, 5000ms.
    /// </summary>
    private static (StringBuilder Transcript, List<(int, int, long)> Segments) BuildTestTranscript()
    {
        var sb = new StringBuilder();
        var segments = new List<(int CharStart, int CharEnd, long OffsetMs)>();

        void AddSegment(string text, long offsetMs)
        {
            int start = sb.Length;
            sb.Append(text);
            int end = sb.Length;
            sb.Append(' ');
            segments.Add((start, end, offsetMs));
        }

        AddSegment("Hello world", 1000);
        AddSegment("what is dependency injection", 3000);
        AddSegment("let me explain", 5000);

        return (sb, segments);
    }

    [Fact]
    public void ExactMatch_ReturnsCorrectRange()
    {
        var (transcript, segments) = BuildTestTranscript();
        var timeRange = (StartMs: 2000L, EndMs: 4000L);

        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "what is dependency injection", null,
            timeRange, transcript, segments);

        // "what is dependency injection" starts at offset 12 (after "Hello world ")
        Assert.Equal(12, start);
        Assert.Equal(40, end);
    }

    [Fact]
    public void SourceText_PreferredOverOriginalText()
    {
        // Build a transcript with garbled text containing the clean question
        var sb = new StringBuilder();
        var segments = new List<(int CharStart, int CharEnd, long OffsetMs)>();

        void AddSegment(string text, long offsetMs)
        {
            int start2 = sb.Length;
            sb.Append(text);
            int end2 = sb.Length;
            sb.Append(' ');
            segments.Add((start2, end2, offsetMs));
        }

        // Garbled ASR: "Is Billy Is Billie Eilish about to lose her house"
        AddSegment("Is Billy Is Billie Eilish about to lose her house", 3000);

        var timeRange = (StartMs: 2000L, EndMs: 4000L);

        // sourceText is the clean reformulated question that appears as a substring
        // originalText is the full garbled text (also matches but gives a wider range)
        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "Is Billy Is Billie Eilish about to lose her house",
            "Is Billie Eilish about to lose her house",
            timeRange, sb, segments);

        // sourceText ("Is Billie Eilish about to lose her house") is tried first and matches
        // giving a narrower, more precise range than the full garbled text
        Assert.Equal(9, start);  // "Is Billie Eilish..." starts at offset 9 (after "Is Billy ")
        Assert.Equal(49, end);   // 9 + 40 = 49
    }

    [Fact]
    public void SourceTextFallsBackToOriginalText_WhenSourceNotInTranscript()
    {
        var (transcript, segments) = BuildTestTranscript();
        var timeRange = (StartMs: 2000L, EndMs: 4000L);

        // sourceText won't be found (LLM resolved pronouns), but originalText matches
        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "what is dependency injection", "What is DI?",
            timeRange, transcript, segments);

        // Falls back to originalText match
        Assert.Equal(12, start);
        Assert.Equal(40, end);
    }

    [Fact]
    public void SourceTextFallback_WhenOriginalTextNull()
    {
        var (transcript, segments) = BuildTestTranscript();
        var timeRange = (StartMs: 2000L, EndMs: 4000L);

        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            null, "what is dependency injection",
            timeRange, transcript, segments);

        Assert.Equal(12, start);
        Assert.Equal(40, end);
    }

    [Fact]
    public void CaseInsensitiveMatch()
    {
        var (transcript, segments) = BuildTestTranscript();
        var timeRange = (StartMs: 2000L, EndMs: 4000L);

        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "What Is Dependency Injection", null,
            timeRange, transcript, segments);

        Assert.Equal(12, start);
        Assert.Equal(40, end);
    }

    [Fact]
    public void PartialMatch_WithinRegion()
    {
        var (transcript, segments) = BuildTestTranscript();
        var timeRange = (StartMs: 2000L, EndMs: 4000L);

        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "dependency injection", null,
            timeRange, transcript, segments);

        // "dependency injection" starts at offset 20
        Assert.Equal(20, start);
        Assert.Equal(40, end);
    }

    [Fact]
    public void NoTextMatch_ReturnsFullRegion()
    {
        var (transcript, segments) = BuildTestTranscript();
        // Narrow time range: only segment at 3000ms is within [2500-2000, 3500+2000] = [500, 5500]
        // Actually ±2s window captures segments at 1000, 3000, 5000 — so use a very narrow range
        // that only catches the middle segment: [2500, 3500] → window [500, 5500] catches all three.
        // To isolate just the middle segment: startMs=3000, endMs=3000 → window [1000, 5000] still catches all.
        // Use startMs=3500, endMs=4000 → window [1500, 6000] catches 3000 and 5000.
        var timeRange = (StartMs: 3500L, EndMs: 4000L);

        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "completely unrelated text", "also unrelated",
            timeRange, transcript, segments);

        // Segments at 3000ms and 5000ms are within [1500, 6000]
        // Region spans from "what is dependency injection" (12) to end of "let me explain" (55)
        Assert.Equal(12, start);
        Assert.Equal(55, end);
    }

    [Fact]
    public void NoSegmentsInRange_ReturnsNull()
    {
        var (transcript, segments) = BuildTestTranscript();
        // Time range that doesn't overlap any segments (even with ±2s tolerance)
        var timeRange = (StartMs: 20000L, EndMs: 25000L);

        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "anything", null,
            timeRange, transcript, segments);

        Assert.Null(start);
        Assert.Null(end);
    }

    [Fact]
    public void ToleranceWindow_IncludesNearbySegments()
    {
        var (transcript, segments) = BuildTestTranscript();
        // Time range just outside the segment at 3000ms, but within ±2s tolerance
        var timeRange = (StartMs: 4500L, EndMs: 4800L);

        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "what is dependency injection", null,
            timeRange, transcript, segments);

        // Segment at 3000ms is within [4500-2000, 4800+2000] = [2500, 6800]
        // Segment at 5000ms is also within range
        Assert.Equal(12, start);
        Assert.Equal(40, end);
    }

    [Fact]
    public void MultipleSegments_SpansFullRegion_WhenNoTextMatch()
    {
        var (transcript, segments) = BuildTestTranscript();
        // Time range spanning all segments
        var timeRange = (StartMs: 0L, EndMs: 6000L);

        var (start, end) = SessionRecorder.ComputeTranscriptPosition(
            "nonexistent text", null,
            timeRange, transcript, segments);

        // All three segments are in range: covers full transcript
        Assert.Equal(0, start);
        Assert.Equal(55, end); // "let me explain" ends at 55
    }
}
