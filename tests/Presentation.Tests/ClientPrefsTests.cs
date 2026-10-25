using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Tests;

/// <summary>The player's preferences file (PJ03 §2, §8): it round-trips, absorbs the two old files once, and never holds a value the game cannot take.</summary>
public class ClientPrefsTests
{
    [Theory]
    [InlineData("people=humgen", "ultra", 8, "people=humgen,tier=medium")]
    [InlineData("people=humgen,tier=ultra", "ultra", 4, "people=humgen,tier=low")]
    [InlineData("people=humgen,tier=low", "ultra", 8, "people=humgen,tier=low")]
    [InlineData("people=humgen", "ultra", 24, "people=humgen")]
    public void FirstRunCapsTheEffectiveDefaultEvenWithoutATierToken(string spec, string defaultTier, double memoryGb, string expected) =>
        Assert.Equal(expected, new MachinePrefs().StartVisuals(spec, defaultTier, memoryGb));

    [Fact]
    public void FirstRunDoesNotOverrideSavedQualityOrGuessUnknownMemory()
    {
        Assert.Equal("people=humgen", new MachinePrefs { Tier = "ultra" }.StartVisuals("people=humgen,tier=low", "ultra", 4));
        Assert.Equal("people=humgen", new MachinePrefs().StartVisuals("people=humgen", "ultra", null));
        Assert.Null(new MachinePrefs().StartVisuals(null, "ultra", 4));
    }

    [Theory]
    [InlineData(AntiAliasing.Off)]
    [InlineData(AntiAliasing.Fxaa)]
    [InlineData(AntiAliasing.Taa)]
    [InlineData(AntiAliasing.Msaa2X)]
    [InlineData(AntiAliasing.Msaa4X)]
    [InlineData(AntiAliasing.Msaa4XTaa)]
    public void GraphicsChoicesSurviveSaveAndReload(AntiAliasing aa)
    {
        var prefs = new ClientPrefs { Machine = new MachinePrefs { AntiAliasing = aa, Upscaler = Upscaler.Bilinear } };
        var loaded = ClientPrefs.FromJson(prefs.ToJson());
        Assert.Equal(aa, loaded.Machine.AntiAliasing);
        Assert.Equal(Upscaler.Bilinear, loaded.Machine.Upscaler);
    }

    [Fact]
    public void OldPreferencesKeepTierAaAndInvalidNumericChoicesAreClamped()
    {
        Assert.Equal(AntiAliasing.Tier, ClientPrefs.FromJson("{\"machine\":{\"tier\":\"ultra\"}}").Machine.AntiAliasing);
        var machine = new MachinePrefs { AntiAliasing = (AntiAliasing)999, Upscaler = (Upscaler)999 }.Normalised();
        Assert.Equal(AntiAliasing.Tier, machine.AntiAliasing);
        Assert.Equal(Upscaler.Fsr, machine.Upscaler);
    }

    [Fact]
    public void FpsReadoutIsOptInAndPersistsInTheInterfacePreferences()
    {
        Assert.False(ClientPrefs.FromJson("{}").Profile.Interface.ShowFps);
        var prefs=new ClientPrefs();
        prefs=prefs with { Profile=prefs.Profile with { Interface=prefs.Profile.Interface with { ShowFps=true } } };
        Assert.True(ClientPrefs.FromJson(prefs.ToJson()).Profile.Interface.ShowFps);
    }

    [Fact]
    public void Preferences_RoundTripThroughTheirFile()
    {
        var prefs = new ClientPrefs
        {
            Machine = new MachinePrefs { WindowMode = WindowMode.Windowed, Resolution = "2560x1440", FrameCap = 144, Tier = "ultra",
                Dials = new GraphicsDials { Shadows = Dial.Low, PlantDensity = 0.5f } },
            Profile = new ProfilePrefs
            {
                Audio = new AudioPrefs { Ambience = 0.3f, Voice = 0 },
                Controls = new ControlPrefs
                {
                    Bindings = new() { ["jump"] = new[] { "key:70", "mouse:4" } },
                    Toggles = new() { ["sprint"] = true },
                },
                Camera = new CameraPrefs { Sensitivity = 1.5f, InvertY = true, Fov = 95 },
                Interface = new InterfacePrefs { ColourVision = ColourVision.Deuteranopia, Guidance = Guidance.Light, SeenHints = new[] { "hint.dodge" } },
                FirstLaunchDone = true,
            },
        };

        var back = ClientPrefs.FromJson(prefs.ToJson());

        Assert.Equal(prefs.Machine with { Dials = back.Machine.Dials }, back.Machine);
        Assert.Equal(prefs.Machine.Dials, back.Machine.Dials);
        Assert.Equal(prefs.Profile.Audio, back.Profile.Audio);
        Assert.Equal(prefs.Profile.Camera, back.Profile.Camera);
        Assert.Equal(new[] { "key:70", "mouse:4" }, back.Profile.Controls.Bindings["jump"]);
        Assert.True(back.Profile.Controls.IsToggle("sprint"));
        Assert.Equal(ColourVision.Deuteranopia, back.Profile.Interface.ColourVision);
        Assert.Equal(new[] { "hint.dodge" }, back.Profile.Interface.SeenHints);
        Assert.True(back.Profile.FirstLaunchDone);
        Assert.Contains("\"window_mode\": \"windowed\"", prefs.ToJson());
    }

    [Fact]
    public void AFileFromBeforeTheOpening_PlaysIt_AndCannotSkipIt()
    {
        var back = ClientPrefs.FromJson("{\"profile\": {\"first_launch_done\": true}}");

        Assert.True(back.Profile.PlayOpening);
        Assert.False(back.Profile.OpeningSeen);
        Assert.True(ClientPrefs.FromJson(new ClientPrefs { Profile = new ProfilePrefs { OpeningSeen = true } }.ToJson()).Profile.OpeningSeen);
    }

    [Fact]
    public void TheOldFiles_AreAbsorbed()
    {
        var prefs = ClientPrefs.Migrate("{\"Master\":0.4,\"Music\":0.0,\"Effects\":0.85}", "low\n");

        Assert.Equal(0.4f, prefs.Profile.Audio.Master);
        Assert.Equal(0f, prefs.Profile.Audio.Music);
        Assert.Equal(0.85f, prefs.Profile.Audio.Effects);
        Assert.Equal(1f, prefs.Profile.Audio.Ambience);
        Assert.Equal("low", prefs.Machine.Tier);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("not json", "phase_a")]
    [InlineData("[1, 2]", "")]
    public void UnreadableOldFiles_GiveTheDefaults(string? audio, string? tier)
    {
        var prefs = ClientPrefs.Migrate(audio, tier);

        Assert.Equal(new AudioPrefs(), prefs.Profile.Audio);
        Assert.Null(prefs.Machine.Tier);
    }

    [Fact]
    public void AHandEditedFile_IsClampedToWhatTheGameCanTake()
    {
        const string text = """
            { "machine": { "render_scale": 7, "frame_cap": 61, "brightness": -3, "tier": "phase_a", "resolution": "huge",
                           "dials": { "plant_density": 99, "shadows": "nonsense" } },
              "profile": { "audio": { "master": 4 }, "camera": { "fov": 300, "sensitivity": 0 },
                           "interface": { "ui_scale": 9, "text_size": 0.1, "notice_duration": 7 },
                           "controls": { "bindings": { "jump": ["key:70", "pedal:1", "key:70", "mouse:1", "key:71"] }, "toggles": { "jump": true } } } }
            """;
        var prefs = ClientPrefs.FromJson(text);

        // An unknown choice's name is not a preferences file: the whole file gives way to the defaults.
        Assert.Equal(new ClientPrefs().ToJson(), prefs.ToJson());

        prefs = ClientPrefs.FromJson(text.Replace("\"nonsense\"", "\"low\""));
        Assert.Equal(1f, prefs.Machine.RenderScale);
        Assert.Equal(0, prefs.Machine.FrameCap);
        Assert.Equal(0.6f, prefs.Machine.Brightness);
        Assert.Null(prefs.Machine.Tier);
        Assert.Equal("1920x1080", prefs.Machine.Resolution);
        Assert.Equal(1.5f, prefs.Machine.Dials.PlantDensity);
        Assert.Equal(1f, prefs.Profile.Audio.Master);
        Assert.Equal(110f, prefs.Profile.Camera.Fov);
        Assert.Equal(0.2f, prefs.Profile.Camera.Sensitivity);
        Assert.Equal(2f, prefs.Profile.Interface.UiScale);   // 200 % is the most (canon 26)
        Assert.Equal(1f, prefs.Profile.Interface.TextSize);
        Assert.Equal(1, prefs.Profile.Interface.NoticeDuration);
        Assert.Equal(new[] { "key:70", "mouse:1" }, prefs.Profile.Controls.Bindings["jump"]);
        Assert.Empty(prefs.Profile.Controls.Toggles);
    }

    [Fact]
    public void NotAPreferencesFile_GivesTheDefaults() =>
        Assert.Equal(new ClientPrefs().Profile.Camera, ClientPrefs.FromJson("{ broken").Profile.Camera);

    [Fact]
    public void Crouch_TogglesByDefault_AndTheOthersAreHeld()
    {
        var controls = new ControlPrefs();

        Assert.True(controls.IsToggle("crouch"));
        Assert.False(controls.IsToggle("sprint"));
        Assert.False(controls.IsToggle("guard"));
        Assert.False(controls.IsToggle("jump"));
    }

    [Fact]
    public void OneKeyOnTwoActions_InPlay_IsAConflict()
    {
        var map = new Dictionary<string, string[]>
        {
            ["jump"] = new[] { "key:32" }, ["dodge"] = new[] { "key:67", "key:32" }, ["interact"] = new[] { "key:69" },
        };

        var conflict = Assert.Single(Binding.Conflicts(map));
        Assert.Equal("key:32", conflict.Binding);
        Assert.Equal(new[] { "jump", "dodge" }, conflict.Actions);
        Assert.Equal(new[] { "jump" }, Binding.Holders(map, "dodge", "key:32"));
    }

    [Fact]
    public void ThePageKeys_ShareTheDigitsAndShiftWithSlotsAndSprint_ButTwoSlotsOnOneKeyConflict()
    {
        // Shift pages the bar with 1 to 6 held: the digit is a slot's key and a page's, Shift is the sprint and the page modifier - no conflicts.
        var map = new Dictionary<string, string[]>
        {
            ["quick_1"] = new[] { "key:49" }, ["bar_page_1"] = new[] { "key:49" }, ["sprint"] = new[] { "key:4194325" },
            ["bar_modifier"] = new[] { "key:4194325" }, ["bar_scroll_modifier"] = new[] { "key:4194328" }, ["gear_set_1"] = Array.Empty<string>(),
        };
        Assert.Empty(Binding.Conflicts(map));
        // But a slot moved onto another slot's key, or a page onto another page's, is one.
        map["quick_2"] = new[] { "key:49" };
        Assert.Equal(new[] { "quick_1", "quick_2" }, Assert.Single(Binding.Conflicts(map)).Actions);
        map["quick_2"] = new[] { "key:50" };
        map["bar_page_2"] = new[] { "key:49" };
        Assert.Equal(new[] { "bar_page_1", "bar_page_2" }, Assert.Single(Binding.Conflicts(map)).Actions);
    }

    [Fact]
    public void BarPrefs_RoundTrip_AndOnlyTheFiveFurtherBarsCanBeShown()
    {
        var prefs = new ClientPrefs { Profile = new ProfilePrefs { Interface = new InterfacePrefs { BarsLocked = true, BarsShown = 0b100101, BarsAtSide = true } } };
        var back = ClientPrefs.FromJson(prefs.ToJson()).Normalised().Profile.Interface;
        Assert.True(back.BarsLocked);
        Assert.True(back.BarsAtSide);
        Assert.Equal(0b00101, back.BarsShown);
    }

    [Fact]
    public void ThePageWraps_AroundTheSixBars()
    {
        Assert.Equal(5, UNNAMED.Presentation.Ui.ActionBarModel.Page(0, -1));
        Assert.Equal(0, UNNAMED.Presentation.Ui.ActionBarModel.Page(5, 1));
        Assert.Equal(3, UNNAMED.Presentation.Ui.ActionBarModel.Page(1, 2));
    }

    [Fact]
    public void TheNumberKeys_AnswerInAConversation_AndUseQuickSlotsOutsideOne()
    {
        // Two contexts, no conflict (PJ03 §7) - but Escape is live in both, so a reply on it would be one.
        var map = new Dictionary<string, string[]>
        {
            ["quick_1"] = new[] { "key:49" }, ["reply_1"] = new[] { "key:49" }, ["release_mouse"] = new[] { "key:4194305" },
        };
        Assert.Empty(Binding.Conflicts(map));

        map["reply_2"] = new[] { "key:4194305" };
        Assert.Equal("key:4194305", Assert.Single(Binding.Conflicts(map)).Binding);
        Assert.Empty(Binding.Holders(map, "reply_1", "key:49"));
    }

    [Theory]
    [InlineData("Vision")]
    [InlineData("Hearing")]
    [InlineData("Motion")]
    [InlineData("Motor and timing")]
    public void EveryComfortPreset_SaysWhatItChanged_AndChangesIt(string preset)
    {
        var before = new ClientPrefs();
        var (after, changed) = ComfortPresets.Apply(before, preset);

        Assert.NotEmpty(changed);
        Assert.NotEqual(before.ToJson(), after.ToJson());
        Assert.Equal(after.ToJson(), after.Normalised().ToJson());
    }

    [Fact]
    public void TheMotionPreset_TurnsEveryCameraEffectToZero()
    {
        var camera = ComfortPresets.Apply(new ClientPrefs(), "Motion").Prefs.Profile.Camera;

        Assert.Equal(0, camera.Shake);
        Assert.Equal(0, camera.Bob);
        Assert.True(camera.ReduceFlashes);
    }

    /// <summary>WP16: chat is on unless the player says otherwise, and the setting is kept like any other.</summary>
    [Fact]
    public void Chat_IsOnByDefault_AndKeepsWhatThePlayerChose()
    {
        Assert.Equal(ChatSetting.On, new ClientPrefs().Profile.Interface.Chat);
        var quiet = new ClientPrefs { Profile = new ProfilePrefs { Interface = new InterfacePrefs { Chat = ChatSetting.PlayRelevantOnly } } };
        Assert.Equal(ChatSetting.PlayRelevantOnly, ClientPrefs.FromJson(quiet.ToJson()).Profile.Interface.Chat);
        var odd = new ClientPrefs { Profile = new ProfilePrefs { Interface = new InterfacePrefs { Chat = (ChatSetting)9 } } };
        Assert.Equal(ChatSetting.On, odd.Normalised().Profile.Interface.Chat);
    }

    // ── WP-1E: the HUD's options and the camera's distance, shoulder and aim speed ──

    [Fact]
    public void TheHudAndCameraOptions_DefaultToTheGameAsBuilt()
    {
        var prefs = new ClientPrefs().Profile;
        Assert.True(prefs.Hud.Shows(HudElement.Tracker));
        Assert.Equal(EnemyCondition.WordsAndBar, prefs.Hud.EnemyCondition);
        Assert.False(prefs.Hud.DamageNumbers);   // canon: damage numbers are off by default
        Assert.Equal(3.5f, prefs.Camera.Distance);
        Assert.Equal(CameraShoulder.Right, prefs.Camera.Shoulder);
        Assert.Equal(1f, prefs.Camera.AimSensitivity);
    }

    [Fact]
    public void TheHudAndCameraOptions_RoundTrip()
    {
        var prefs = new ClientPrefs
        {
            Profile = new ProfilePrefs
            {
                Hud = new HudPrefs { EnemyCondition = EnemyCondition.Numbers, DamageNumbers = true }.With(HudElement.Tracker, false),
                Camera = new CameraPrefs { Distance = 5f, Shoulder = CameraShoulder.Center, AimSensitivity = 0.5f },
            },
        };
        var loaded = ClientPrefs.FromJson(prefs.ToJson()).Profile;
        Assert.False(loaded.Hud.Shows(HudElement.Tracker));
        Assert.Equal(EnemyCondition.Numbers, loaded.Hud.EnemyCondition);
        Assert.True(loaded.Hud.DamageNumbers);
        Assert.Equal((5f, CameraShoulder.Center, 0.5f), (loaded.Camera.Distance, loaded.Camera.Shoulder, loaded.Camera.AimSensitivity));
        Assert.True(loaded.Hud.With(HudElement.Tracker, true).Shows(HudElement.Tracker));
    }

    [Fact]
    public void AnOlderFile_WithoutTheNewOptions_ReadsAsTheDefaults_AndAHandEditedOneIsClamped()
    {
        var old = ClientPrefs.FromJson("""{ "profile": { "camera": { "fov": 90 } } }""").Profile;
        Assert.Equal(new HudPrefs().HiddenElements, old.Hud.HiddenElements);
        Assert.Equal(3.5f, old.Camera.Distance);

        var edited = ClientPrefs.FromJson("""
            { "profile": { "camera": { "distance": 40, "aim_sensitivity": 0 }, "hud": { "hidden_elements": 255 } } }
            """).Profile;
        Assert.Equal(CameraPrefs.MaxDistance, edited.Camera.Distance);
        Assert.Equal(0.2f, edited.Camera.AimSensitivity);
        Assert.Equal((int)HudElement.All, edited.Hud.HiddenElements);   // only the parts that exist can be hidden
        Assert.Equal(CameraPrefs.MinDistance, ClientPrefs.FromJson("""{ "profile": { "camera": { "distance": 0 } } }""").Profile.Camera.Distance);
    }
}
