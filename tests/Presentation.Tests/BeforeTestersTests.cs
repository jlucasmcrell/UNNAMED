using UNNAMED.Domain.Quests;
using UNNAMED.Presentation.Audio;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>The "before outside testers" gaps: the keys and settings that persist, auto-run, subtitles, dialogue skip, the compass's two marks, first-launch controls, and words for colour.</summary>
public class BeforeTestersTests
{
    [Theory]
    [InlineData("src/Presentation/Art/art_bindings.json")]
    [InlineData("content/_art_bindings.json")]
    public void DefaultPlayerLayers_KeepTheAcceptedTrousersWhenNoLegArmourIsEquipped(string relativePath)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), relativePath)));
        int checkedPlayers = 0;
        void Check(System.Text.Json.JsonElement node)
        {
            if (node.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name == "player" && property.Value.TryGetProperty("equipment_outfits", out var outfits))
                {
                    foreach (string key in new[] { "set:|", "set:item.armor.hide_vest|", "set:item.armor.lamework_jack|" })
                        Assert.Contains("_trousers_set__", outfits.GetProperty(key)[0].GetString()!);
                    checkedPlayers++;
                }
                Check(property.Value);
            }
        }
        Check(document.RootElement);
        Assert.True(checkedPlayers >= 4, $"Only {checkedPlayers} player fits checked in {relativePath}");
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "content", "hints")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("the repository's content folder");
    }

    // ---- preferences ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void ANewInterfaceAndCameraSetting_RoundTripsThroughTheOneFile()
    {
        var prefs = new ClientPrefs
        {
            Profile = new ProfilePrefs
            {
                Interface = new InterfacePrefs
                {
                    UiScale = 2f, TextSize = 2f, SubtitleSize = 1.5, SubtitleBackground = SubtitleBackground.Solid,
                    HiddenNotices = (int)(NoticeKind.Items | NoticeKind.World), BarScales = new[] { 1.5f, 1f, 0.75f, 1f, 1f, 1f }, BarsShown = 0b00101,
                },
                Camera = new CameraPrefs { MotionBlur = true, Shake = 0, Bob = 0 },
            },
        };
        var back = ClientPrefs.FromJson(prefs.ToJson()).Profile;
        Assert.Equal(2f, back.Interface.UiScale);
        Assert.Equal(1.5, back.Interface.SubtitleSize);
        Assert.Equal(SubtitleBackground.Solid, back.Interface.SubtitleBackground);
        Assert.False(back.Interface.Shows(NoticeKind.Items));
        Assert.False(back.Interface.Shows(NoticeKind.World));
        Assert.True(back.Interface.Shows(NoticeKind.Party));
        Assert.True(back.Interface.Shows(NoticeKind.None));   // what is not of a kind is never hidden
        Assert.Equal(new[] { 1.5f, 1f, 0.75f, 1f, 1f, 1f }, back.Interface.BarScales);
        Assert.Equal(0b00101, back.Interface.BarsShown);
        Assert.True(back.Camera.MotionBlur);
        Assert.Equal(0f, back.Camera.Shake);
    }

    [Fact]
    public void TheDefaults_AreWhatTheOwnerAsked()
    {
        var ui = new InterfacePrefs();
        Assert.Equal(0, ui.BarsShown);          // every extra bar off until the player turns it on
        Assert.True(ui.Subtitles);
        Assert.Equal(0, ui.HiddenNotices);
        Assert.False(new CameraPrefs().MotionBlur);
        Assert.All(ui.BarScales, s => Assert.Equal(1f, s));
        Assert.Equal(6, ui.BarScales.Length);
    }

    [Fact]
    public void ValuesTheGameCannotTake_AreBroughtIn()
    {
        var ui = new InterfacePrefs
        {
            UiScale = 5f, SubtitleSize = 3, SubtitleBackground = (SubtitleBackground)9, HiddenNotices = -1, BarScales = new[] { 0.1f, 9f, float.NaN },
        }.Normalised();
        Assert.Equal(2f, ui.UiScale);
        Assert.Equal(1.25, ui.SubtitleSize);
        Assert.Equal(SubtitleBackground.Dim, ui.SubtitleBackground);
        Assert.Equal((int)NoticeKind.All, ui.HiddenNotices);
        Assert.Equal(new[] { 0.75f, 1.5f, 1f, 1f, 1f, 1f }, ui.BarScales);
        Assert.Equal(0.75f, new InterfacePrefs { UiScale = 0.1f }.Normalised().UiScale);
    }

    [Fact]
    public void AutoRunWasRenamed_AndAFileThatNamesTheOldActionIsReadAsTheNew()
    {
        var old = new ControlPrefs { Bindings = new() { ["autorun"] = new[] { "key:96" } } }.Normalised();
        Assert.False(old.Bindings.ContainsKey("autorun"));
        Assert.Equal(new[] { "key:96" }, old.Bindings["auto_run"]);
        var text = "{\"profile\":{\"controls\":{\"bindings\":{\"autorun\":[\"key:70\"]}}}}";
        Assert.Equal(new[] { "key:70" }, ClientPrefs.FromJson(text).Profile.Controls.Bindings["auto_run"]);
    }

    [Fact]
    public void SpaceSkipsInAConversationAndJumpsInPlay_TwoContexts_NoConflict()
    {
        var map = new Dictionary<string, string[]> { ["jump"] = new[] { "key:32" }, ["dialogue_skip"] = new[] { "key:32" }, ["reply_1"] = new[] { "key:49" } };
        Assert.Empty(Binding.Conflicts(map));
        map["interact"] = new[] { "key:32" };   // Space on a play action too: that IS a conflict
        Assert.Contains(Binding.Conflicts(map), c => c.Actions.Contains("jump") && c.Actions.Contains("interact"));
    }

    [Fact]
    public void TheControlsPreset_PutsTheFourMovementActionsOnWasdOrTheArrows_AndCustomLeavesThemBe()
    {
        var wasd = ControlPresets.Apply(new ControlPrefs(), "wasd");
        Assert.Equal(new[] { "key:87" }, wasd.Bindings["move_forward"]);
        Assert.Equal(new[] { "key:68" }, wasd.Bindings["move_right"]);
        Assert.Equal("wasd", ControlPresets.Of(wasd));
        var arrows = ControlPresets.Apply(wasd, "arrows");
        Assert.Equal(new[] { "key:4194320" }, arrows.Bindings["move_forward"]);
        Assert.Equal("arrows", ControlPresets.Of(arrows));
        Assert.Equal(arrows, ControlPresets.Apply(arrows, "custom"));
        Assert.Equal("custom", ControlPresets.Of(new ControlPrefs()));   // the defaults bind both: not one preset
        Assert.All(arrows.Bindings.Values.SelectMany(b => b), b => Assert.True(Binding.IsValid(b)));
        // Other bindings stay.
        var jumpy = ControlPresets.Apply(new ControlPrefs { Bindings = new() { ["jump"] = new[] { "key:70" } } }, "wasd");
        Assert.Equal(new[] { "key:70" }, jumpy.Bindings["jump"]);
    }

    // ---- auto-run -------------------------------------------------------------------------------------------------------------

    private static AutoRunFrame Frame(bool toggle = false, bool forward = false, bool back = false, bool downed = false, bool talking = false, double moved = 0.1) =>
        new(toggle, forward, back, downed, talking, 0.1, moved);

    [Fact]
    public void AutoRun_StartsOnTheKey_AndTheKeyAgainStopsIt()
    {
        var run = new AutoRunRules();
        Assert.Null(run.Step(Frame(toggle: true)));
        Assert.True(run.On);
        Assert.Equal("Autorun off", run.Step(Frame(toggle: true, moved: 0.6)));
        Assert.False(run.On);
    }

    [Theory]
    [InlineData(true, false)]   // forward cancels
    [InlineData(false, true)]   // back cancels
    public void AutoRun_IsCancelledByForwardOrBack(bool forward, bool back)
    {
        var run = new AutoRunRules();
        run.Step(Frame(toggle: true));
        Assert.Null(run.Step(Frame(moved: 0.6)));
        Assert.True(run.On);
        Assert.NotNull(run.Step(Frame(forward: forward, back: back, moved: 0.6)));
        Assert.False(run.On);
    }

    [Fact]
    public void AutoRun_StrafingAndSteeringKeepItRunning()
    {
        var run = new AutoRunRules();
        run.Step(Frame(toggle: true));
        for (int i = 0; i < 30; i++)
            Assert.Null(run.Step(Frame(moved: 0.55)));   // moving at ~5.5 m/s however the camera and the side keys turn
        Assert.True(run.On);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AutoRun_StopsOnDownedOrOnOpeningDialogue_AndWillNotStartThen(bool downed, bool talking)
    {
        var run = new AutoRunRules();
        run.Step(Frame(toggle: true));
        Assert.NotNull(run.Step(Frame(downed: downed, talking: talking, moved: 0.6)));
        Assert.False(run.On);
        run.Step(Frame(toggle: true, downed: downed, talking: talking));
        Assert.False(run.On);
    }

    [Fact]
    public void AutoRun_StopsWhenMovementIsBlocked_NotForAMomentsHesitation()
    {
        var run = new AutoRunRules();
        run.Step(Frame(toggle: true));
        for (int i = 0; i < 3; i++)
            Assert.Null(run.Step(Frame(moved: 0)));   // 0.3 s against something: not yet
        Assert.Null(run.Step(Frame(moved: 0.6)));      // it gave way: the count starts again
        string? stopped = null;
        for (int i = 0; i < 10 && stopped is null; i++)
            stopped = run.Step(Frame(moved: 0.01));
        Assert.Equal("Autorun off - something is in the way", stopped);
        Assert.False(run.On);
    }

    // ---- subtitles ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Subtitles_NameTheSpeaker_HoldLongEnoughToRead_AndKeepTheNewestThree()
    {
        var model = new SubtitleModel();
        model.Say("Renn Vale", "Traveller. A bed, a meal, directions.", 0);
        Assert.Equal("RENN VALE: Traveller. A bed, a meal, directions.", SubtitleModel.Words(model.Lines(0.1).Single()));
        Assert.Empty(model.Lines(SubtitleModel.Hold("Traveller. A bed, a meal, directions.") + 0.01));
        // Short words still hold the minimum; a long voiced line holds as long as it is spoken.
        Assert.Equal(SubtitleModel.MinSeconds, SubtitleModel.Hold("Hm."));
        Assert.Equal(12, SubtitleModel.Hold("Hm.", 12));

        model.Say("A", "one", 0);
        model.Say("B", "two", 0);
        model.Say("C", "three", 0);
        model.Say("D", "four", 0);
        Assert.Equal(new[] { "B", "C", "D" }, model.Lines(0.5).Select(l => l.Speaker));
    }

    [Fact]
    public void ARepeatedLine_IsOneCaption_KeptAlive_AndAnEmptyLineIsNone()
    {
        var model = new SubtitleModel();
        model.Say("Kera", "Mind the anvil.", 0);
        model.Say("Kera", "Mind the anvil.", 2);
        Assert.Single(model.Lines(2.1));
        Assert.Single(model.Lines(4.0));   // kept alive from tick 2, not 0
        model.Say("Kera", "   ", 5);
        Assert.Single(model.Lines(4.1));
        model.Clear();
        Assert.Empty(model.Lines(0));
        Assert.Equal("Just words", SubtitleModel.Words(new Caption("", "Just words", 1)));
    }

    [Fact]
    public void SubtitleSize_ScalesOverTheTextSize()
    {
        Assert.Equal(20, SubtitleModel.FontSize(20, 1, 1));
        Assert.Equal(25, SubtitleModel.FontSize(20, 1, 1.25));
        // 200 % text and the largest subtitles would be 80 px: held to what three wrapped lines can take of a third of the screen, however short the screen.
        Assert.Equal(46, SubtitleModel.FontSize(20, 2, 2));
        Assert.Equal(23, SubtitleModel.FontSize(20, 2, 2, screenHeight: 540));   // 200 % interface scale: a 540-line screen
        Assert.Equal(16, SubtitleModel.FontSize(20, 1, 1, screenHeight: 200));   // never under 16
        Assert.All(InterfacePrefs.SubtitleSizes, s => Assert.True(s >= 1));
    }

    [Theory]
    [InlineData(false, false, true)]    // unvoiced, subtitles off: words stay over the head, or they would be lost
    [InlineData(false, true, false)]    // unvoiced, subtitles on: the captions carry them
    [InlineData(true, false, false)]    // voiced, subtitles off: heard only
    [InlineData(true, true, false)]     // voiced, subtitles on: captions
    public void ABarksWords_StandOverTheHeadOnlyWhenNothingElseCarriesThem(bool voiced, bool subtitles, bool overhead) =>
        Assert.Equal(overhead, VoiceRules.ShowOverhead(voiced, subtitles));

    // ---- dialogue skip --------------------------------------------------------------------------------------------------------

    [Fact]
    public void ATapSkips_SilencesTheLine_AndTakesTheOnlyWayOnWhenItDecidesNothing()
    {
        var skip = new DialogueSkipRules();
        var tap = skip.Step(true, true, 0.016, replies: 1, loneIsPlain: true);
        Assert.True(tap.Hush);
        Assert.True(tap.Advance);
        skip.Step(false, false, 0.016, 1, true);
        // A lone reply that does something (a quest, a hire, an exchange) is never taken for the player; nor is a choice.
        Assert.Equal(new DialogueSkipRules.Result(true, false), skip.Step(true, true, 0.016, 1, loneIsPlain: false));
        skip.Step(false, false, 0.016, 1, true);
        Assert.Equal(new DialogueSkipRules.Result(true, false), skip.Step(true, true, 0.016, 3, loneIsPlain: false));
    }

    [Fact]
    public void HoldingFastForwards_AfterAShortHold_ThenEveryFewTenthsOfASecond_ButNeverPastAChoice()
    {
        var skip = new DialogueSkipRules();
        skip.Step(true, true, 0.016, 1, true);
        int advanced = 0;
        double clock = 0;
        for (; clock < 2.0; clock += 0.02)
        {
            var step = skip.Step(true, false, 0.02, 1, true);
            if (step.Advance)
            {
                advanced++;
                Assert.True(clock >= DialogueSkipRules.HoldSeconds);
            }
        }
        Assert.InRange(advanced, 7, 11);   // (2 - 0.4) / 0.18 turns
        // A choice on screen: held or not, nothing is taken.
        for (int i = 0; i < 100; i++)
            Assert.False(skip.Step(true, false, 0.02, replies: 2, loneIsPlain: false).Advance);
        // Letting go starts it over.
        skip.Step(false, false, 0.02, 1, true);
        Assert.False(skip.Step(true, false, 0.02, 1, true).Advance);
    }

    // ---- the compass ----------------------------------------------------------------------------------------------------------

    private static QuestMarker Marker(string quest, long x, long z, string label = "Somewhere") =>
        new(quest, "o_1", TargetKind.Npc, "npc.x", label, x, z);

    [Fact]
    public void TheCompass_MarksTheTrackedBeat_AndTheNearestOtherObjective_EachInWordsAndMetres()
    {
        var tracked = Marker("q.main", 0, 120_000, "Renn Vale");          // 120 m north
        var near = Marker("q.side", 40_000, 0, "Sel Arien");               // 40 m east
        var far = Marker("q.bounty", -300_000, 0, "The ridge");            // 300 m west
        string Name(QuestMarker m) => m.Label;
        var a = CompassMarkerModel.Mark(tracked, 0, 0, Name)!;
        Assert.Equal(0, a.BearingDegrees, 1);
        Assert.Equal(120, a.Metres, 1);
        var b = CompassMarkerModel.Nearest(new QuestMarker?[] { tracked, far, near, null }, tracked, 0, 0, Name)!;
        Assert.Equal("Sel Arien", b.Label);   // the tracked quest's own mark is not "the nearest other"
        Assert.Equal(90, b.BearingDegrees, 1);
        Assert.Equal("Quest: Renn Vale, 120 m\nNearest: Sel Arien, 40 m", CompassMarkerModel.Words(a, b));
        Assert.Equal("", CompassMarkerModel.Words(null, null));
        Assert.Equal("Nearest: Sel Arien, 40 m", CompassMarkerModel.Words(null, b));
    }

    [Fact]
    public void AMarkHidesOnceTheCharacterStandsAtIt_AndDistancesReadInKilometresFarOff()
    {
        Assert.Null(CompassMarkerModel.Mark(Marker("q", 1_000, 1_000), 0, 0, m => m.Label));
        Assert.Null(CompassMarkerModel.Mark(null, 0, 0, m => m.Label));
        Assert.Null(CompassMarkerModel.Nearest(new QuestMarker?[] { Marker("q", 100, 100) }, null, 0, 0, m => m.Label));
        Assert.Equal("1.2 km", CompassMarkerModel.Distance(1234));
        Assert.Equal("85 m", CompassMarkerModel.Distance(83));
    }

    // ---- words for colour, explainers -----------------------------------------------------------------------------------------

    [Fact]
    public void ARequirement_SaysItIsMetOrWhatYouHave_NotOnlyGreenAndRed()
    {
        Assert.Equal("10 - met", ColourWords.Needs(10, 12));
        Assert.Equal("10 - met", ColourWords.Needs(10, 10));
        Assert.Equal("10 - you have 7", ColourWords.Needs(10, 7));
    }

    [Fact]
    public void TheExplainers_AreThreeScreensOfPlainWords_WithNoTokensLeftIn()
    {
        Assert.Equal(new[] { "explainer.downed_death", "explainer.no_pause", "explainer.living_world" }, Explainers.All.Select(e => e.Id));
        foreach (var explainer in Explainers.All)
        {
            Assert.NotEmpty(explainer.Title);
            Assert.All(explainer.Paragraphs, p =>
            {
                Assert.InRange(p.Length, 20, Explainers.MaxParagraph);
                Assert.DoesNotContain('{', p);
            });
        }
        // The three rules the owner set: there is no pause, death costs only XP debt, the world goes on.
        Assert.Contains("does not stop the world", string.Join(" ", Explainers.All[1].Paragraphs));
        Assert.Contains("XP debt", string.Join(" ", Explainers.All[0].Paragraphs));
    }

    // ---- the hints name keys that exist ---------------------------------------------------------------------------------------

    [Fact]
    public void EveryKeyAHintNames_IsAnActionThePlayerCanFindInControls()
    {
        string settings = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Presentation", "Settings", "SettingsStore.cs"));
        string block = settings[settings.IndexOf("Remappable =", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("};", StringComparison.Ordinal)];
        var actions = System.Text.RegularExpressions.Regex.Matches(block, "\"([a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Contains("auto_run", actions);
        Assert.Contains("dialogue_skip", actions);
        Assert.DoesNotContain("autorun", actions);
        // The action bars' keys: paged with Ctrl, a modifier and a page key, all rebindable.
        Assert.Contains("bar_modifier", actions);

        var unknown = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "content", "hints"), "*.yaml"))
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), @"\{key:([a-z0-9_]+)\}"))
            {
                if (!actions.Contains(m.Groups[1].Value))
                    unknown.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            }
        }
        Assert.Empty(unknown);
    }

    [Fact]
    public void EveryMomentAHintWaitsOn_IsNamedFromSomewhereInThePresentation()
    {
        string src = Path.Combine(RepoRoot(), "src", "Presentation");
        string all = string.Join("\n", Directory.EnumerateFiles(src, "Main*.cs").Select(File.ReadAllText));
        foreach (var field in typeof(UNNAMED.Domain.Guidance.HintTriggers).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)))
        {
            // price_changed waits on the markets' presentation, which names it when prices move; the rest are named today.
            if (field.Name == nameof(UNNAMED.Domain.Guidance.HintTriggers.PriceChanged))
                continue;
            Assert.True(all.Contains($"HintTriggers.{field.Name}", StringComparison.Ordinal), $"nothing names the {field.Name} moment");
        }
    }
}
