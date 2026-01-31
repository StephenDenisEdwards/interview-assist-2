using InterviewAssist.Library.Pipeline.Utterance;

namespace InterviewAssist.Library.UnitTests.Pipeline.Utterance;

public class ActionRouterTests
{
    private DateTime _currentTime = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private ActionRouter CreateRouter(PipelineOptions? options = null, CooldownConfig? cooldowns = null)
    {
        return new ActionRouter(options, cooldowns, () => _currentTime);
    }

    private void AdvanceTime(TimeSpan duration)
    {
        _currentTime = _currentTime.Add(duration);
    }

    private static DetectedIntent CreateIntent(IntentSubtype subtype, string text = "test")
    {
        return new DetectedIntent
        {
            Type = IntentType.Imperative,
            Subtype = subtype,
            Confidence = 0.9,
            SourceText = text
        };
    }

    [Fact]
    public void Route_ImperativeIntent_TriggersAction()
    {
        var router = CreateRouter();
        ActionEvent? triggeredEvent = null;
        router.OnActionTriggered += e => triggeredEvent = e;

        var result = router.Route(CreateIntent(IntentSubtype.Stop), "utt_001");

        Assert.True(result);
        Assert.NotNull(triggeredEvent);
        Assert.Equal("stop", triggeredEvent.ActionName);
        Assert.False(triggeredEvent.WasDebounced);
    }

    [Fact]
    public void Route_QuestionIntent_DoesNotTriggerAction()
    {
        var router = CreateRouter();
        var triggered = false;
        router.OnActionTriggered += _ => triggered = true;

        var result = router.Route(new DetectedIntent
        {
            Type = IntentType.Question,
            Confidence = 0.9,
            SourceText = "What is this?"
        }, "utt_001");

        Assert.False(result);
        Assert.False(triggered);
    }

    [Fact]
    public void Route_WithinCooldown_Debounces()
    {
        var cooldowns = new CooldownConfig
        {
            Cooldowns = new Dictionary<IntentSubtype, TimeSpan>
            {
                [IntentSubtype.Repeat] = TimeSpan.FromMilliseconds(1500)
            }
        };
        var router = CreateRouter(cooldowns: cooldowns);
        var events = new List<ActionEvent>();
        router.OnActionTriggered += e => events.Add(e);

        router.Route(CreateIntent(IntentSubtype.Repeat), "utt_001");
        AdvanceTime(TimeSpan.FromMilliseconds(500)); // Still within cooldown
        router.Route(CreateIntent(IntentSubtype.Repeat), "utt_002");

        Assert.Equal(2, events.Count);
        Assert.False(events[0].WasDebounced);
        Assert.True(events[1].WasDebounced);
    }

    [Fact]
    public void Route_AfterCooldown_Fires()
    {
        var cooldowns = new CooldownConfig
        {
            Cooldowns = new Dictionary<IntentSubtype, TimeSpan>
            {
                [IntentSubtype.Repeat] = TimeSpan.FromMilliseconds(1500)
            }
        };
        var router = CreateRouter(cooldowns: cooldowns);
        var events = new List<ActionEvent>();
        router.OnActionTriggered += e => events.Add(e);

        router.Route(CreateIntent(IntentSubtype.Repeat), "utt_001");
        AdvanceTime(TimeSpan.FromMilliseconds(1600)); // Past cooldown
        router.Route(CreateIntent(IntentSubtype.Repeat), "utt_002");

        Assert.Equal(2, events.Count);
        Assert.False(events[0].WasDebounced);
        Assert.False(events[1].WasDebounced);
    }

    [Fact]
    public void Route_StopCommand_HasZeroCooldown()
    {
        var router = CreateRouter();
        var events = new List<ActionEvent>();
        router.OnActionTriggered += e => events.Add(e);

        router.Route(CreateIntent(IntentSubtype.Stop), "utt_001");
        router.Route(CreateIntent(IntentSubtype.Stop), "utt_002");
        router.Route(CreateIntent(IntentSubtype.Stop), "utt_003");

        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.False(e.WasDebounced));
    }

    [Fact]
    public void Route_ConflictResolution_LastWins()
    {
        var options = new PipelineOptions
        {
            ConflictWindow = TimeSpan.FromMilliseconds(1500)
        };
        var router = CreateRouter(options);
        var events = new List<ActionEvent>();
        router.OnActionTriggered += e => events.Add(e);

        // "Stop" then quickly "Continue" within conflict window
        router.Route(CreateIntent(IntentSubtype.Stop, "Stop"), "utt_001");
        AdvanceTime(TimeSpan.FromMilliseconds(500));
        router.Route(CreateIntent(IntentSubtype.Continue, "Actually continue"), "utt_002");

        // Advance past conflict window to resolve
        AdvanceTime(TimeSpan.FromMilliseconds(1100));
        router.CheckConflictWindow();

        // Last one (Continue) should win
        var nonDebouncedEvents = events.Where(e => !e.WasDebounced).ToList();
        Assert.Contains(nonDebouncedEvents, e => e.ActionName == "continue");
    }

    [Fact]
    public void Route_RepeatWithNumber_IncludesSlots()
    {
        var router = CreateRouter();
        ActionEvent? triggeredEvent = null;
        router.OnActionTriggered += e => triggeredEvent = e;

        var intent = new DetectedIntent
        {
            Type = IntentType.Imperative,
            Subtype = IntentSubtype.Repeat,
            Confidence = 0.9,
            SourceText = "repeat number 3",
            Slots = new IntentSlots
            {
                Reference = "number 3",
                Count = 3
            }
        };

        router.Route(intent, "utt_001");

        Assert.NotNull(triggeredEvent);
        Assert.Equal(3, triggeredEvent.Intent.Slots.Count);
        Assert.Equal("number 3", triggeredEvent.Intent.Slots.Reference);
    }

    [Fact]
    public void RegisterHandler_InvokesOnRoute()
    {
        var router = CreateRouter();
        DetectedIntent? handledIntent = null;
        router.RegisterHandler(IntentSubtype.Generate, i => handledIntent = i);

        router.Route(CreateIntent(IntentSubtype.Generate), "utt_001");

        Assert.NotNull(handledIntent);
        Assert.Equal(IntentSubtype.Generate, handledIntent.Subtype);
    }

    [Fact]
    public void Reset_ClearsCooldowns()
    {
        var cooldowns = new CooldownConfig
        {
            Cooldowns = new Dictionary<IntentSubtype, TimeSpan>
            {
                [IntentSubtype.Repeat] = TimeSpan.FromMilliseconds(1500)
            }
        };
        var router = CreateRouter(cooldowns: cooldowns);
        var events = new List<ActionEvent>();
        router.OnActionTriggered += e => events.Add(e);

        router.Route(CreateIntent(IntentSubtype.Repeat), "utt_001");
        router.Reset();
        router.Route(CreateIntent(IntentSubtype.Repeat), "utt_002");

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.False(e.WasDebounced));
    }
}
