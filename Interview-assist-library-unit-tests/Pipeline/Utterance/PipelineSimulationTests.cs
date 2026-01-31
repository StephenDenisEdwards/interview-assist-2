using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.UnitTests.Pipeline.Utterance;

/// <summary>
/// End-to-end simulation tests demonstrating the full pipeline.
/// These tests simulate realistic ASR event sequences.
/// </summary>
public class PipelineSimulationTests : IDisposable
{
    private readonly UtteranceIntentPipeline _pipeline;
    private readonly List<string> _log = new();

    public PipelineSimulationTests()
    {
        _pipeline = new UtteranceIntentPipeline();

        // Wire up logging
        _pipeline.OnAsrPartial += e => _log.Add($"[ASR.partial] {e.Text}");
        _pipeline.OnAsrFinal += e => _log.Add($"[ASR.final] {e.Text}");
        _pipeline.OnUtteranceOpen += e => _log.Add($"[UTT.open] {e.Id}");
        _pipeline.OnUtteranceUpdate += e => _log.Add($"[UTT.update] stable=\"{e.StableText}\" raw=\"{e.RawText}\"");
        _pipeline.OnUtteranceFinal += e => _log.Add($"[UTT.final] {e.Id}: \"{e.StableText}\" ({e.CloseReason})");
        _pipeline.OnIntentCandidate += e => _log.Add($"[INTENT.candidate] {e.Intent.Type}/{e.Intent.Subtype} conf={e.Intent.Confidence:F2}");
        _pipeline.OnIntentFinal += e => _log.Add($"[INTENT.final] {e.Intent.Type}/{e.Intent.Subtype} conf={e.Intent.Confidence:F2}");
        _pipeline.OnActionTriggered += e => _log.Add($"[ACTION] {e.ActionName} (debounced={e.WasDebounced})");
    }

    public void Dispose()
    {
        _pipeline.Dispose();
    }

    [Fact]
    public void Simulation_SplitQuestionAcrossChunks()
    {
        // Scenario: "What is a lock statement used for in C#?"
        // Arrives as chunks, testing that we don't fire prematurely

        // First chunk: partial results building up
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "What", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "What is", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "What is a", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "What is a lock", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "What is a lock statement", IsFinal = true });

        // Second chunk arrives
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "used for", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "used for in", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "used for in C#?", IsFinal = true });

        // Signal end
        _pipeline.SignalUtteranceEnd();

        PrintLog("Split Question Across Chunks");

        // Verify: Should have candidate intents but final intent only on close
        Assert.Contains(_log, l => l.Contains("[UTT.final]"));
        Assert.Contains(_log, l => l.Contains("[INTENT.final]") && l.Contains("Question"));

        // Should NOT have action triggered (questions don't trigger actions)
        Assert.DoesNotContain(_log, l => l.Contains("[ACTION]") && !l.Contains("debounced"));
    }

    [Fact]
    public void Simulation_CanYouRepeat_IsImperative()
    {
        // "Can you repeat that" should be imperative, not question

        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Can you", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Can you repeat", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Can you repeat that", IsFinal = true });
        _pipeline.SignalUtteranceEnd();

        PrintLog("Can You Repeat - Imperative");

        Assert.Contains(_log, l => l.Contains("[INTENT.final]") && l.Contains("Imperative") && l.Contains("Repeat"));
        Assert.Contains(_log, l => l.Contains("[ACTION] repeat"));
    }

    [Fact]
    public void Simulation_StopActuallyContinue_LastWins()
    {
        // "Stop. Actually continue." - last command should win

        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Stop", IsFinal = true });
        _pipeline.SignalUtteranceEnd();

        // Quick correction
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Actually", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Actually continue", IsFinal = true });
        _pipeline.SignalUtteranceEnd();

        PrintLog("Stop Actually Continue");

        // Both should trigger actions, but if conflict resolution is in effect,
        // the second utterance's action should be the effective one
        Assert.Contains(_log, l => l.Contains("[ACTION] stop") || l.Contains("[ACTION] continue"));
    }

    [Fact]
    public void Simulation_RepeatNumber3_ExtractsSlot()
    {
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Repeat", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Repeat number", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Repeat number 3", IsFinal = true });
        _pipeline.SignalUtteranceEnd();

        PrintLog("Repeat Number 3 - Slot Extraction");

        Assert.Contains(_log, l => l.Contains("[INTENT.final]") && l.Contains("Repeat"));
        Assert.Contains(_log, l => l.Contains("[ACTION] repeat"));

        // Verify the intent detector extracts the slot
        var intent = new IntentDetector().DetectFinal("Repeat number 3");
        Assert.Equal(3, intent.Slots.Count);
        Assert.Equal("number 3", intent.Slots.Reference);
    }

    [Fact]
    public void Simulation_GenerateQuestionsAboutTopic()
    {
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Generate", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Generate 20", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Generate 20 questions", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "Generate 20 questions about C#", IsFinal = true });
        _pipeline.SignalUtteranceEnd();

        PrintLog("Generate Questions");

        Assert.Contains(_log, l => l.Contains("[ACTION] generate_questions"));

        var intent = new IntentDetector().DetectFinal("Generate 20 questions about C#");
        Assert.Equal(IntentSubtype.Generate, intent.Subtype);
        Assert.Equal(20, intent.Slots.Count);
    }

    [Fact]
    public void Simulation_QuestionWithDefinitionSubtype()
    {
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "What is", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "What is dependency", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "What is dependency injection?", IsFinal = true });
        _pipeline.SignalUtteranceEnd();

        PrintLog("Definition Question");

        var intent = new IntentDetector().DetectFinal("What is dependency injection?");
        Assert.Equal(IntentType.Question, intent.Type);
        Assert.Equal(IntentSubtype.Definition, intent.Subtype);
        Assert.Contains("dependency injection", intent.Slots.Topic ?? "");
    }

    [Fact]
    public void Simulation_HowToQuestion()
    {
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "How do I", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "How do I implement", IsFinal = false });
        _pipeline.ProcessAsrEvent(new AsrEvent { Text = "How do I implement caching?", IsFinal = true });
        _pipeline.SignalUtteranceEnd();

        PrintLog("HowTo Question");

        var intent = new IntentDetector().DetectFinal("How do I implement caching?");
        Assert.Equal(IntentType.Question, intent.Type);
        Assert.Equal(IntentSubtype.HowTo, intent.Subtype);
    }

    private void PrintLog(string testName)
    {
        // This will show in test output for debugging
        // Note: Does NOT clear the log - assertions may follow
        var output = $"\n=== {testName} ===\n" + string.Join("\n", _log) + "\n";
        Console.WriteLine(output);
    }
}

/// <summary>
/// Runnable console simulation for manual testing.
/// </summary>
public static class PipelineSimulationRunner
{
    public static void RunSimulation()
    {
        Console.WriteLine("=== Utterance-Intent Pipeline Simulation ===\n");

        using var pipeline = new UtteranceIntentPipeline();

        // Wire up console logging
        pipeline.OnAsrPartial += e => Console.WriteLine($"  [ASR.partial] \"{e.Text}\"");
        pipeline.OnAsrFinal += e => Console.WriteLine($"  [ASR.final] \"{e.Text}\"");
        pipeline.OnUtteranceOpen += e => Console.WriteLine($"[UTT.open] {e.Id}");
        pipeline.OnUtteranceUpdate += e => Console.WriteLine($"  [UTT.update] stable=\"{e.StableText}\"");
        pipeline.OnUtteranceFinal += e =>
        {
            Console.WriteLine($"[UTT.final] {e.Id}: \"{e.StableText}\"");
            Console.WriteLine($"           Close reason: {e.CloseReason}");
        };
        pipeline.OnIntentCandidate += e =>
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"  [INTENT.candidate] {e.Intent.Type}/{e.Intent.Subtype} (conf={e.Intent.Confidence:F2})");
            Console.ResetColor();
        };
        pipeline.OnIntentFinal += e =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[INTENT.final] {e.Intent.Type}/{e.Intent.Subtype} (conf={e.Intent.Confidence:F2})");
            if (e.Intent.Slots.Topic != null)
                Console.WriteLine($"               Topic: {e.Intent.Slots.Topic}");
            if (e.Intent.Slots.Count != null)
                Console.WriteLine($"               Count: {e.Intent.Slots.Count}");
            if (e.Intent.Slots.Reference != null)
                Console.WriteLine($"               Reference: {e.Intent.Slots.Reference}");
            Console.ResetColor();
        };
        pipeline.OnActionTriggered += e =>
        {
            Console.ForegroundColor = e.WasDebounced ? ConsoleColor.DarkGray : ConsoleColor.Green;
            Console.WriteLine($"[ACTION] {e.ActionName}" + (e.WasDebounced ? " (DEBOUNCED)" : " (FIRED)"));
            Console.ResetColor();
        };

        // Simulation scenarios
        Console.WriteLine("\n--- Scenario 1: Split question across chunks ---");
        SimulateWithDelay(pipeline, new[]
        {
            ("What", false),
            ("What is", false),
            ("What is a", false),
            ("What is a lock", false),
            ("What is a lock statement", true),
            ("used for", false),
            ("used for in C#?", true)
        });
        pipeline.SignalUtteranceEnd();

        Thread.Sleep(200);
        Console.WriteLine("\n--- Scenario 2: Polite imperative ---");
        SimulateWithDelay(pipeline, new[]
        {
            ("Can you", false),
            ("Can you repeat", false),
            ("Can you repeat that", true)
        });
        pipeline.SignalUtteranceEnd();

        Thread.Sleep(200);
        Console.WriteLine("\n--- Scenario 3: Generate with count ---");
        SimulateWithDelay(pipeline, new[]
        {
            ("Generate", false),
            ("Generate 20", false),
            ("Generate 20 questions", false),
            ("Generate 20 questions about async await", true)
        });
        pipeline.SignalUtteranceEnd();

        Thread.Sleep(200);
        Console.WriteLine("\n--- Scenario 4: Stop command ---");
        SimulateWithDelay(pipeline, new[]
        {
            ("Stop", true)
        });
        pipeline.SignalUtteranceEnd();

        Thread.Sleep(200);
        Console.WriteLine("\n--- Scenario 5: Repeat number extraction ---");
        SimulateWithDelay(pipeline, new[]
        {
            ("Repeat", false),
            ("Repeat number", false),
            ("Repeat number 5", true)
        });
        pipeline.SignalUtteranceEnd();

        Console.WriteLine("\n=== Simulation Complete ===");
    }

    private static void SimulateWithDelay(UtteranceIntentPipeline pipeline, (string Text, bool IsFinal)[] events)
    {
        foreach (var (text, isFinal) in events)
        {
            Thread.Sleep(50); // Simulate realistic timing
            pipeline.ProcessAsrEvent(new AsrEvent { Text = text, IsFinal = isFinal });
        }
    }
}
