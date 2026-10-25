// UNNAMED Presentation - Main's panels: built and wired, and the screens opened, closed and kept in reach (D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private SettingsPanel _settings = null!;
    private CreditsPanel _credits = null!;
    private TitleScreen _title = null!;
    private FirstLaunchPanel _firstLaunch = null!;
    private SkillsPanel _skills = null!;

    /// <summary>The settings as last applied: a change applies only the parts it touched (a volume slider never restyles the interface).</summary>
    private ClientPrefs _applied = new();
    /// <summary>The panels and the System Menu: built, bound and given their icons.</summary>
    private void BuildPanels(string profile)
    {
        _inventory = new InventoryPanel { Name = "Inventory", Assign = AssignQuickSlot };
        _inventory.Bind(_session, _controller);
        _inventory.BindArt(_art,_bindings,()=> (_avatarModel,_avatarPose?.ArmSplayDegrees??0));
        AddChild(_inventory);
        _loot = new LootPanel { Name = "Loot" };
        _loot.Bind(_session);
        AddChild(_loot);
        _dialogue = new DialoguePanel { Name = "Dialogue" };
        _dialogue.Voice = _voice;
        _dialogue.Bind(_session);
        AddChild(_dialogue);
        _journal = new JournalPanel { Name = "Journal" };
        AddChild(_journal);
        _reading = new ReadingPanel { Name = "Reading" };
        _reading.Bind(_session);
        AddChild(_reading);
        _questDebug = new QuestDebugPanel { Name = "QuestDebug" };
        AddChild(_questDebug);
        _character = new CharacterPanel { Name = "Character", AllSkills = () => ShowSkills(null) };
        _character.Bind(_session);
        AddChild(_character);
        _skills = new SkillsPanel { Name = "Skills", Assign = AssignQuickSlot, NearStation = () => _inventory.OpenStation?.Kind };
        _skills.Bind(_session);
        AddChild(_skills);
        if (_hud.Bars is { } bars)
        {
            // The bars only ask: every change is a command, and the world keeps running while they are edited.
            bars.Assign = AssignActionSlot;
            bars.Use = UseActionSlot;
            bars.PageTo = page => _barPage = page;
        }
        _help = new HelpPanel { Name = "Help", DeveloperKeys = _developer, Hints = () => _session.Hints.Values, Files = $"Saves: {profile}\nThe log, to send with a problem report: {LogPath}" };
        GD.Print($"UNNAMED files: saves in {profile}; the log at {LogPath}");
        AddChild(_help);
        _effectsPanel = new EffectsPanel { Name = "Effects" };
        _effectsPanel.Bind(_session);
        AddChild(_effectsPanel);
        _reputation = new ReputationPanel { Name = "Reputation" };
        _reputation.Bind(_session);
        AddChild(_reputation);
        _awaySheet = new AwaySheet { Name = "Away", Dismissed = CloseScreens };
        AddChild(_awaySheet);
        _companionsPanel = new CompanionsPanel { Name = "Party" };
        _companionsPanel.Bind(_session, _controller);
        AddChild(_companionsPanel);
        _waystones = new WaystonePanel { Name = "Waystones", TravelRequested=TravelWithEffects };
        _waystones.Bind(_session, _controller);
        AddChild(_waystones);
        _noticeBoard = new NoticeBoardPanel { Name = "NoticeBoard" };
        _noticeBoard.Bind(_session);
        AddChild(_noticeBoard);
        _map = new MapPanel { Name = "Map" };
        _map.Bind(_session);
        AddChild(_map);
        _settings = new SettingsPanel { Name = "Settings" };
        AddChild(_settings);
        _credits = new CreditsPanel { Name = "Credits", Folder = NoticesFolder(), CreditsFile = Path.Combine(_session.Options.ContentRoot, "credits.yaml") };
        AddChild(_credits);
        BuildReportPanel();
        BuildPlayerScreens();
        _menu = new SystemMenuPanel
        {
            Name = "SystemMenu",
            Entries = MenuEntries(),
            Quit = Quit,
            Status = MenuStatus,
        };
        AddChild(_menu);
        var dial = _assets.Icon("ui.hud.compass");
        if (dial is null)
            _art.Coverage.Fallback("icon", "compass", "no compass dial in the icon manifest: a plain strip", "ui.hud.compass");
        else
            _art.Coverage.Resolved("icon", "compass", "ui.hud.compass");
        _hud.UseCompassDial(dial);
        var icons = new HudIcons(_assets, _bindings.Icons, _art.Coverage);
        _hud.UseIcons(icons);
        _inventory.UseIcons(icons);
        _loot.UseIcons(icons);
        _character.UseIcons(icons);
        _skills.UseIcons(icons);
        _effectsPanel.UseIcons(icons);
        _companionsPanel.UseIcons(icons);
        _waystones.UseIcons(icons);
        _map.UseIcons(icons);
        _menu.UseIcons(icons);
        _journal.UseIcons(icons);
        _help.UseIcons(icons);
        _saveIndicator = new SaveIndicator { Name = "SaveIndicator" };
        _saveIndicator.Bind(_session);
        AddChild(_saveIndicator);
        _saves = new SavesPanel { Name = "Saves" };
        _saves.Bind(_session);
        _saves.Load = save => LoadChosen(save.Slot, save.Copy, save);
        _saves.Closed = () => BackToTitle(_saves.Close);
        AddChild(_saves);
        _saves.UseIcons(icons);
        _settings.Closed = () => BackToTitle(CloseScreens);
        _credits.Closed = () => BackToTitle(CloseScreens);
        _title = new TitleScreen
        {
            Name = "Title", Version = $"Playtest · {BuildId()} · content {_session.Content.Version}",
            CrashNotice = _crashNotice, OpenLogs = OpenCrashFolder,
            Continue = _ => Continue(),
            NewGame = OpenCreator,
            Load = () => FromTitle(_saves.OpenStart),
            Settings = () => FromTitle(() => _settings.Open()),
            Credits = () => FromTitle(() => _credits.Visible = true),
            Quit = Quit,
        };
        _title.Bind(_session);
        AddChild(_title);
        BuildCreator(icons);
        _firstLaunch = new FirstLaunchPanel { Name = "FirstLaunch", Done = () => _title.Open() };
        AddChild(_firstLaunch);
    }

    /// <summary>
    /// The settings in force from the start: the keys (the defaults taken first, for Reset), the camera, the interface and, for a player's
    /// run, the picture once the world's sky and sun exist. Every later change applies the part it touched.
    /// </summary>
    private void UseSettings(bool applyPicture)
    {
        SettingsStore.CaptureDefaults();
        _applied = SettingsStore.Current;
        SettingsStore.ApplyBindings();
        _camera.Use(_applied.Profile.Camera);
        if (_applied.Profile.Interface != new InterfacePrefs { SeenHints = _applied.Profile.Interface.SeenHints })
            SettingsStore.ApplyInterface(GetTree());
        // Sky3D makes its sun as it enters the tree, after this frame: the picture's dials are applied once it has.
        if (applyPicture)
            Callable.From(() => SettingsStore.ApplyPicture(GetViewport())).CallDeferred();
        SettingsStore.Changed += SettingsChanged;
    }

    private void SettingsChanged()
    {
        var now = SettingsStore.Current;
        var was = _applied;
        _applied = now;
        if (now.Machine.WindowMode != was.Machine.WindowMode || now.Machine.Resolution != was.Machine.Resolution
            || now.Machine.Vsync != was.Machine.Vsync || now.Machine.FrameCap != was.Machine.FrameCap)
            SettingsStore.ApplyWindow();
        if (now.Machine.AntiAliasing != was.Machine.AntiAliasing || now.Machine.Upscaler != was.Machine.Upscaler
            || now.Machine.RenderScale != was.Machine.RenderScale || now.Machine.Brightness != was.Machine.Brightness || now.Machine.Dials != was.Machine.Dials)
            SettingsStore.ApplyPicture(GetViewport());
        if (now.Profile.Audio != was.Profile.Audio)
            SettingsStore.ApplyAudio();
        if (now.Profile.Controls != was.Profile.Controls)
            SettingsStore.ApplyBindings();
        if (now.Profile.Camera != was.Profile.Camera)
            _camera.Use(now.Profile.Camera);
        if (now.Profile.Interface with { SeenHints = was.Profile.Interface.SeenHints } != was.Profile.Interface)
            SettingsStore.ApplyInterface(GetTree());
    }

    /// <summary>
    /// The front end at launch: the first-launch screen the first time the game starts on this machine, then the title; after the opening,
    /// the title's entries come in under the name it left in place.
    /// </summary>
    private void OpenFrontEnd(bool reveal = false)
    {
        if (SettingsStore.Current.Profile.FirstLaunchDone && reveal)
            _title.Reveal();
        else if (SettingsStore.Current.Profile.FirstLaunchDone)
            _title.Open();
        else
            OpenFirstLaunch();
    }

    /// <summary>The first-launch screen, readable before anything is chosen: the interface sized for the window the game opened in.</summary>
    private void OpenFirstLaunch()
    {
        _title.Close();
        var prefs = SettingsStore.Current;
        SettingsStore.Set(prefs with { Profile = prefs.Profile with { Interface = prefs.Profile.Interface with { UiScale = SettingsStore.RecommendedUiScale() } } });
        _firstLaunch.Visible = true;
    }

    /// <summary>A world began: every front-end screen goes.</summary>
    private void CloseFrontEnd()
    {
        _title.Close();
        _firstLaunch.Visible = false;
        _saves.Close();
        _settings.Visible = false;
        _credits.Visible = false;
        CloseCreator();
    }

    /// <summary>A screen opened from the title: the title steps aside for it, and comes back when it closes.</summary>
    private void FromTitle(Action open)
    {
        _title.Close();
        open();
    }

    /// <summary>A screen opened from the title or the game closed: before a world runs the title returns; in the game, play.</summary>
    private void BackToTitle(Action close)
    {
        close();
        if (_session.Simulation is null)
            _title.Open();
    }

    /// <summary>Escape before a world runs: a screen opened from the title closes back to it; the title and the first launch stay.</summary>
    private void FrontEscape()
    {
        if (_settings.Capturing)
            return;
        if (_settings.Visible)
            BackToTitle(_settings.Close);
        else if (_credits.Visible)
            BackToTitle(() => _credits.Visible = false);
        else if (_saves.Visible)
            BackToTitle(_saves.Close);
        else if (_creator.Visible)
            BackToTitle(CloseCreator);
    }

    /// <summary>
    /// Quit, from the title or the System Menu: a running world is saved first, with no question (MMO rules: one live world, saved on the
    /// way out - DN7). The save is written in the background and finished before the game exits (<see cref="_ExitTree"/>).
    /// TODO(WP11): save to the character's live state once the live-state and backup split lands.
    /// </summary>
    private void Quit()
    {
        SaveOnTheWayOut();
        CaptureQuit.At(this, 0);
    }

    private void SaveOnTheWayOut()
    {
        if (_session?.Simulation is not null && !_scripted)
            _session.SaveInBackground(SaveSlots.Quick);
    }

    /// <summary>The window's close button quits as Quit does: the world is saved on the way out.</summary>
    public override void _Notification(int what)
    {
        // A capture harness has to know who ended the run. A close request from the window writes this line with its frame and clock; a
        // process killed from outside writes nothing at all, so the two are told apart by looking for it.
        if (what == NotificationWMCloseRequest)
        {
            GD.Print($"CAPTURE close request at frame {Engine.GetFramesDrawn()} ({DateTimeOffset.Now:O}); scripted={_scripted}");
            SaveOnTheWayOut();
        }
        else if (what == NotificationApplicationFocusOut)
            GD.Print($"CAPTURE focus out at frame {Engine.GetFramesDrawn()} ({DateTimeOffset.Now:O}); scripted={_scripted}");
    }

    /// <summary>
    /// The third-party notices Credits reads: beside an exported build (the release payload's <c>THIRD_PARTY_NOTICES</c>), and in the
    /// repository the payload's own folder.
    /// </summary>
    private static string NoticesFolder() => OS.HasFeature("template")
        ? Path.Combine(Home(), "THIRD_PARTY_NOTICES")
        : Path.Combine(Home(), "tools", "release", "payload", "THIRD_PARTY_NOTICES");

    /// <summary>
    /// The System Menu's rows, in each group's order: group, words, key, icon, what choosing it does, and optionally whether it can be
    /// chosen yet. A new screen is one row here.
    /// </summary>
    private SystemMenuPanel.Entry[] MenuEntries() => new SystemMenuPanel.Entry[]
    {
        new("You", "Character", "character", "panel.character", FromMenu(() => Show(_character))),
        new("You", "Skills", "skills", "panel.skills", FromMenu(() => ShowSkills(null))),
        new("You", "Inventory", "inventory", "panel.inventory", FromMenu(() => OpenInventory(null))),
        new("You", "Effects", null, "panel.effects", FromMenu(() => Show(_effectsPanel))),
        new("The world", "Journal", "journal", "panel.journal", FromMenu(() => Show(_journal))),
        new("The world", "Map", "map", "panel.map", FromMenu(() => Show(_map))),
        new("The world", "Party", "party", "panel.companions", FromMenu(() => ShowParty(null))),
        new("The world", "Chat", "chat", "panel.chat", FromMenu(() => _chat.Open())),
        new("The world", "Reputation", null, "panel.reputation", FromMenu(() => Show(_reputation))),
        new("System", "Save and load", "saves", "panel.saves", FromMenu(() => _saves.OpenInGame())),
        new("System", "Settings", null, "panel.settings", FromMenu(() => _settings.Open())),
        new("System", "Credits", null, "panel.credits", FromMenu(() => _credits.Visible = true)),
        new("System", "Controls and help", "help", "panel.help", FromMenu(_help.Toggle)),
        new("System", "Report a problem", "report_problem", "panel.help", FromMenu(OpenReport)),
    };

    /// <summary>
    /// A panel that takes the keys and the mouse is open: a conversation, or a management screen - the System Menu, the inventory (on the
    /// character, a container, a trader or a station), the character sheet, the journal, effects, the party, chat, a hiring sheet, the
    /// controls or the saves list (the Phase-1 technical audit, L-27; the Field Instrument UI). While one is, no gameplay key reaches the
    /// world, and the world runs on (the owner's ruling: nothing pauses for a panel). The quest debugger is an overlay: it takes no keys.
    /// </summary>
    public bool Modal => _waystoneTravelBusy || _dialogue.Visible || Screen is not null;

    /// <summary>The management screen open, if any: one at a time.</summary>
    private CanvasLayer? Screen =>
        new CanvasLayer[] { _menu, _inventory, _loot, _character, _skills, _journal, _reading, _effectsPanel, _companionsPanel, _reputation, _awaySheet, _waystones, _map, _settings, _credits, _help, _saves, _chat, _hire, _report }
            .FirstOrDefault(s => s.Visible);

    /// <summary>Close whatever management screen is open, back to play.</summary>
    private void CloseScreens()
    {
        if (_openedFromMenu is { } opened)
            UiKit.SheetOf(opened)?.CloseWord("Close");
        _openedFromMenu = null;
        CloseInventory();
        _saves.Close();
        if (_help.Visible)
            _help.Toggle();
        _waystones.Close();
        _settings.Close();
        _hire.Close();
        foreach (var screen in new CanvasLayer[] { _menu, _character, _skills, _journal, _effectsPanel, _companionsPanel, _reputation, _awaySheet, _map, _credits, _chat, _report })
            screen.Visible = false;
    }

    /// <summary>
    /// A screen opened by name, for the harnesses that picture them (the UI shots, the playthrough): Escape is pressed on whatever is open;
    /// every other name opens its screen in place of the last, and "" closes them. A bare "large_" or "text_" enlarges what is up (a
    /// conversation stays open).
    /// </summary>
    private void ScreenByName(string name)
    {
        if (name != "escape")
            CloseScreens();
        if (name != "compass")
            _compassPreview = false;
        // "large_<screen>": that screen at the largest text and interface scale, in high contrast; "text_<screen>" at the largest text in
        // high contrast, the interface scale as it was; "scale_<screen>": the interface at 200 % with the text as it was (in memory only: a harness
        // writes no preferences). Any other name puts them back.
        string? prefix = name.StartsWith("large_", StringComparison.Ordinal) ? "large_" : name.StartsWith("text_", StringComparison.Ordinal) ? "text_"
            : name.StartsWith("scale_", StringComparison.Ordinal) ? "scale_" : null;
        if (prefix != _enlarged)
        {
            if (_beforeLarge is not null)
                SettingsStore.Set(_beforeLarge);
            _beforeLarge = prefix is null ? null : SettingsStore.Current;
            if (_beforeLarge is { Profile.Interface: var ui })
                SettingsStore.Set(_beforeLarge with
                {
                    Profile = _beforeLarge.Profile with { Interface = ui with { UiScale = prefix is "large_" or "scale_" ? 2f : ui.UiScale, TextSize = prefix == "scale_" ? ui.TextSize : 2f, HighContrast = prefix != "scale_" || ui.HighContrast } },
                });
            _enlarged = prefix;
        }
        switch (prefix is null ? name : name[prefix.Length..])
        {
            case "menu_character":
                // A destination chosen on the System Menu, as its row's press chooses it.
                _menu.Visible = true;
                _menu.Choose("Character");
                break;
            case "escape":
                Escape();
                break;
            case "menu":
                _menu.Visible = true;
                break;
            case "character":
                Show(_character);
                break;
            case "skills":
                ShowSkills(null);
                break;
            case "skills_formulas":
                ShowSkills(SkillsPanel.Formulas);
                break;
            case "skills_recipes":
                ShowSkills(SkillsPanel.Recipes);
                break;
            case "skills_map":
                ShowSkills(SkillsPanel.Map);
                break;
            case "bars":
            case "bars_side":
                // The action bars, pictured with two further bars shown, a gear set kept and its switch on the main bar (a harness preview).
                BarsPreview(atSide: name == "bars_side");
                break;
            case "compass":
                // The compass with both marks, held on a picture (a preview: no quest points there).
                _compassPreview = true;
                _hud.SetMarker(35);
                _hud.SetNearestMarker(250);
                _hud.SetMarkerWords(CompassMarkerModel.Words(new CompassMark("Renn Vale", 120, 35), new CompassMark("Sel Arien", 40, 250)));
                break;
            case "subtitles":
                // Two captions as a bark and a spoken line show them (a preview: nobody speaks).
                _hud.Say("Renn Vale", "Traveller. A bed, a meal, directions. Start with whichever keeps you upright.", 30);
                _hud.Say("Kera Voss", "Mind the anvil, it bites.", 30);
                break;
            case "hud_stack":
            case "hud_stack_bars":
                // Everything that crowds the HUD at once: three captions, the tracked quest and notices, and with "_bars" two further bars (a preview: nobody speaks).
                if (name.EndsWith("_bars", StringComparison.Ordinal))
                    BarsPreview(atSide: false);
                _hud.Say("Renn Vale", "Traveller. A bed, a meal, directions. Start with whichever keeps you upright.", 60);
                _hud.Say("Kera Voss", "Mind the anvil, it bites.", 60);
                _hud.Say("Renn Vale", "The road in from the east is quiet this week; the boar in the wallow is not.", 60);
                // The notices wait a moment so they are laid out at the interface size this name has just set.
                // (the same words again while they show are one notice, so each picture's differ by a trailing space.)
                string room = new(' ', ++_stackShots);
                GetTree().CreateTimer(0.1).Timeout += () =>
                {
                    _hud.Toast("Discovered: Ashen Hollow Waystation" + room, 60);
                    _hud.Toast("Done: Follow the road down to the waystation." + room, 60);
                    _hud.Toast("Kept Field kit as gear set 1" + room, 60);
                };
                break;
            case "announce_skill":
                // The skill-up notice, pictured without waiting for a skill to rise (a preview: nothing is awarded).
                _hud.Toast("Skill: One-hand Blade 4", 3);
                break;
            case "inventory":
                OpenInventory(null);
                PackShotNotices();
                break;
            case "inventory_stash":
                OpenInventory("container.stash.waystation");
                PackShotNotices();
                break;
            case "inventory_station":
                OpenStation("station.hollow.workbench");
                PackShotNotices();
                break;
            case "inventory_trade":
                OpenTrade("npc.ashen_hollow.kera_voss");
                PackShotNotices();
                break;
            case "journal":
                Show(_journal);
                break;
            case "effects":
                Show(_effectsPanel);
                break;
            case "companions":
            case "party":
                ShowParty(null);
                break;
            case "chat":
                _chat.Open(ChatTab.All);
                break;
            case "chat_groups":
                _chat.Open(ChatTab.Groups);
                break;
            case { } hire when hire.StartsWith("hire:", StringComparison.Ordinal):
                // The hiring sheet on someone in reach, as interacting with them opens it.
                ShowHire(hire[5..]);
                break;
            case "reputation":
                Show(_reputation);
                break;
            case "away":
                // The sheet a login opens, pictured from the last login's log, or from a day's absence ending now (a preview: nothing passes).
                ShowAway(AwayPreview());
                break;
            case "help":
                _help.Toggle();
                break;
            case "saves":
                _saves.OpenInGame();
                break;
            case "announce":
                // The announcement as a level-up shows it, pictured without waiting a level (a preview: nothing is awarded).
                _hud.Announce("Level up", "Level 2", HudStyle.Ember, ("A point to spend on the character sheet", HelpPanel.Key("character")));
                break;
            case "map":
                Show(_map);
                break;
            case "credits":
                _credits.Visible = true;
                break;
            case "report":
                OpenReport();
                break;
            case "report_written":
                // A note written and the report written, as its button does: the zip it leaves is the harness's evidence (gate E2).
                OpenReport();
                _report.Submit("UI shots: the report as a tester writes it.");
                break;
            case not null when name.StartsWith("settings", StringComparison.Ordinal):
                // "settings" opens its first page, "settings_controls" that page.
                _settings.Open(name.Length > 9 ? name[9..] : "display");
                break;
        }
    }

    /// <summary>
    /// The harness's picture of the action bars: bars 2 and 3 shown (stacked above the main bar, or at the right edge), the worn set kept as
    /// gear set 1 and switched to from slot 9, and a few things put on the second bar - every change the way the player makes it, by command.
    /// </summary>
    private void BarsPreview(bool atSide)
    {
        var prefs = SettingsStore.Current;
        SettingsStore.Set(prefs with { Profile = prefs.Profile with { Interface = prefs.Profile.Interface with { BarsShown = 0b00011, BarsAtSide = atSide } } });
        if (_session.Simulation is not { } simulation)
            return;
        var player = simulation.PlayerId;
        if (simulation.GearSets.IsEmpty)
        {
            _session.Submit(new SaveGearSetCommand(player, 1, "Field kit"));
            _session.Submit(new AssignQuickSlotCommand(player, 8, UNNAMED.Domain.Progression.QuickBar.GearSetId(1)));
            int slot = 0;
            foreach (string id in simulation.QuickBar.OfType<string>().Where(i => !UNNAMED.Domain.Progression.QuickBar.TryGearSet(i, out _)).Take(3))
                _session.Submit(new AssignQuickSlotCommand(player, slot++, id, 1));
            foreach (string id in simulation.Player.Progression.Known.Keys.Where(k => _session.Setup.Techniques.ContainsKey(k) || _session.Setup.Magic.Formulas.ContainsKey(k)).Take(4))
                _session.Submit(new AssignQuickSlotCommand(player, slot++, id, 2));
        }
    }

    private int _stackShots;
    private ClientPrefs? _beforeLarge;
    private string? _enlarged;

    /// <summary>Every management screen, as the smoke opens them to prove the world runs under each.</summary>
    private static readonly string[] EveryScreen =
        { "menu", "character", "skills", "skills_formulas", "inventory", "journal", "effects", "party", "chat", "chat_groups", "reputation", "away", "map", "help", "saves",
          "settings", "settings_controls", "credits" };

    private int _noPauseScreen;
    private int _noPauseFrames;
    private long _noPauseFrom;

    private bool NoPauseDone => _noPauseScreen >= EveryScreen.Length;

    /// <summary>
    /// The smoke's no-pause check: each screen opened by name, then held open for ten frames, in which the world must have ticked, the
    /// tree must not be paused and time must run at its own rate - and the screen must still be the one open. Null while it runs, true
    /// when a screen passed, false (said in the log) when one stopped the world.
    /// </summary>
    private bool? NoPauseStep()
    {
        if (NoPauseDone)
            return null;
        string name = EveryScreen[_noPauseScreen];
        long tick = _session.Simulation!.WorldTick;
        if (_noPauseFrames++ == 0)
        {
            ScreenByName(name);
            _noPauseFrom = tick;
            return null;
        }
        if (_noPauseFrames <= 10)
            return null;
        bool open = Screen is not null;
        bool ran = tick > _noPauseFrom && !GetTree().Paused && Engine.TimeScale == 1.0;
        GD.Print($"UNNAMED no-pause: {name} open {open}, world tick {_noPauseFrom} -> {tick}, paused {GetTree().Paused}, time scale {Engine.TimeScale}");
        if (!open || !ran)
        {
            GD.PushError($"UNNAMED no-pause: FAIL - with {name} open the world {(open ? "stopped" : "was not shown: the screen did not open")}");
            return false;
        }
        ScreenByName("");
        _noPauseScreen++;
        _noPauseFrames = 0;
        return true;
    }

    private ulong _frontStarted;
    private int _frontStep;

    /// <summary>
    /// <c>--front-shots dir</c>: the front end as a player meets it, each screen pictured - the first-launch screen, the title, each
    /// Settings page, Credits and, with a save in <c>--profile</c>, Load - then a new game is started and saved (so a second run, on the
    /// same profile, pictures the title with Continue) and the run quits. A harness: it reads and writes no player settings.
    /// </summary>
    private void FrontShots(string directory)
    {
        // The window sized for the capture first (--capture-size), then a picture every second and a half once it has settled.
        if (_frontStarted == 0)
        {
            _frontStarted = Time.GetTicksMsec();
            SizeForCapture();
            OpenFirstLaunch();
        }
        if (Time.GetTicksMsec() < _frontStarted + 4000 + 1500 * (ulong)_frontStep)
            return;
        var steps = new (string Shot, Action? Then)[]
        {
            ("01_first_launch", () =>
            {
                _firstLaunch.Visible = false;
                // The recommended settings, as the first launch's own button sets them.
                SettingsStore.Set(new ClientPrefs { Profile = new ProfilePrefs { FirstLaunchDone = true, Interface = new InterfacePrefs { UiScale = SettingsStore.RecommendedUiScale() } } });
                _title.Open();
            }),
            ("02_title", () => FromTitle(() => _settings.Open("display"))),
            ("03_settings_display", () => _settings.Open("audio")),
            ("04_settings_audio", () => _settings.Open("controls")),
            ("05_settings_controls", () => _settings.Open("camera")),
            ("06_settings_camera", () => _settings.Open("interface")),
            // The largest text and a larger interface, as a player who needs them sees Settings and the title (in memory only: a harness
            // writes no preferences).
            ("07_settings_interface", () =>
            {
                SettingsStore.Set(SettingsStore.Current with
                {
                    Profile = SettingsStore.Current.Profile with { Interface = SettingsStore.Current.Profile.Interface with { UiScale = 1.25f, TextSize = 2f } },
                });
                _settings.Open("controls");
            }),
            ("07b_settings_controls_large_text", () => BackToTitle(_settings.Close)),
            ("07c_title_large_text", () =>
            {
                SettingsStore.Set(SettingsStore.Current with
                {
                    Profile = SettingsStore.Current.Profile with { Interface = new InterfacePrefs { UiScale = SettingsStore.RecommendedUiScale() } },
                });
                FromTitle(() => _credits.Visible = true);
            }),
            ("08_credits", () => { BackToTitle(() => _credits.Visible = false); if (!_session.SaveSlots().IsEmpty) FromTitle(_saves.OpenStart); }),
            ("09_load_when_saves_exist", () =>
            {
                BackToTitle(_saves.Close);
                _session.NewGame(DefaultCharacter);
                Started();
            }),
            ("10_in_game", () =>
            {
                _menu.Visible = true;
                _menu.Choose("Settings");
            }),
            ("11_in_game_settings", () =>
            {
                CloseScreens();
                _session.Save(SaveSlots.Quick);
            }),
        };
        if (_frontStep >= steps.Length)
        {
            GD.Print($"UNNAMED front shots written to {directory}");
            CaptureQuit.At(this, 0);
            return;
        }
        var (shot, then) = steps[_frontStep++];
        SaveScreenshot(directory, shot);
        then?.Invoke();
    }

    /// <summary>The Skills screen in place of whatever was open, on a row (a skill or a known view), or on the most practised skill.</summary>
    private void ShowSkills(string? select)
    {
        CloseScreens();
        _skills.Open(select);
    }

    /// <summary>A quick slot given something to use, or cleared: the command, as the Skills screen and the inventory ask for it (WP9).</summary>
    private void AssignQuickSlot(int slot, string? id) => AssignActionSlot(0, slot, id);

    /// <summary>A slot of any of the six bars given something to use, or cleared (the bars' drag and drop, and the right click).</summary>
    private void AssignActionSlot(int bar, int slot, string? id)
    {
        if (_session.Simulation is { } simulation)
            _session.Submit(new AssignQuickSlotCommand(simulation.PlayerId, slot, id, bar));
    }

    /// <summary>A bar's slot clicked: used as its key would use it.</summary>
    private void UseActionSlot(int bar, int slot)
    {
        if (_session.Simulation is { } simulation && simulation.ActionBars[bar][slot] is { } id)
            UseQuickSlot(slot, id, captured: true);
    }

    /// <summary>The Waystone panel, at the stone the character stands at.</summary>
    private void Show(WaystonePanel panel, string stoneKey)
    {
        CloseScreens();
        panel.Open(stoneKey);
    }

    /// <summary>The inn's notice board, at the board the character stands at (spec 15).</summary>
    private void Show(NoticeBoardPanel panel, string stationKey)
    {
        CloseScreens();
        panel.Open(stationKey);
    }

    /// <summary>Something written, open at the page just read (spec 41).</summary>
    private void Show(ReadingPanel panel, string entryId, int page)
    {
        CloseScreens();
        panel.Open(entryId, page);
    }

    /// <summary>Open a screen in place of whatever was open: a destination replaces the menu, and a screen's own key replaces another.</summary>
    private void Show(CanvasLayer screen)
    {
        CloseScreens();
        screen.Visible = true;
        if (screen == _character)
            _character.Refresh();
        if (screen == _journal)
            _journal.Refresh(_session);
    }

    /// <summary>
    /// A System Menu destination: opens its screen and remembers that the menu opened it. With <c>--menu-escape back</c> (the owner's open
    /// UX option, 08 §11.3) Escape then returns to the menu, and the screen's title bar says BACK; by default Escape closes it to play.
    /// </summary>
    private Action FromMenu(Action open) => () =>
    {
        open();
        _openedFromMenu = Screen;
        if (_escapeToMenu && _openedFromMenu is { } screen)
            UiKit.SheetOf(screen)?.CloseWord("Back");
    };

    /// <summary>The System Menu's strip: who the character is and how far along, the time played and the last save this session.</summary>
    private SystemMenuPanel.StatusLine? MenuStatus()
    {
        if (_session?.Simulation is not { } simulation)
            return null;
        var progression = simulation.Player.Progression;
        var rules = _session.Setup.Progression;
        bool capped = progression.Level >= rules.LevelCap;
        string saved = _lastSave is { } last
            ? $"{(last.Auto ? "Autosaved" : "Saved")} {Ago(_session.PlaytimeSeconds - last.CapturedAtPlaytime)}"
            : _loadedFrom is { } loaded ? $"Loaded {loaded}, not saved since" : "Not saved yet";
        return new SystemMenuPanel.StatusLine(simulation.Player.Name, progression.Level, capped, progression.LevelProgressXp,
            capped ? 0 : rules.Curve.ToReach(progression.Level + 1), progression.XpDebt, progression.UnspentAttributePoints,
            $"Played {Span(_session.PlaytimeSeconds)}", saved, DateLine(simulation));
    }

    /// <summary>The calendar line (WP7): the date in numbered tallies and the weather now, as the System Menu's strip and the map show it.</summary>
    private string DateLine(UNNAMED.World.Runtime.Simulation simulation) =>
        CalendarWords.Line(simulation.Calendar, _session.Setup.Calendar, simulation.Weather.State);

    /// <summary>A stretch of play in words: "12 min", "1 h 04 min".</summary>
    private static string Span(double seconds)
    {
        long minutes = (long)Math.Max(0, seconds) / 60;
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }

    private static string Ago(double seconds) => seconds < 60 ? "just now" : $"{Span(seconds)} ago";

    /// <summary>
    /// Escape outside a conversation: closes the screen open back to play (the accepted C4 semantics), and with nothing open opens the
    /// System Menu. With <c>--menu-escape back</c>, a screen the menu opened closes back to the menu instead.
    /// </summary>
    private void Escape()
    {
        var open = Screen;
        bool back = _escapeToMenu && open is not null && open != _menu && open == _openedFromMenu;
        if (open is not null)
            CloseScreens();
        if (open is null || back)
            _menu.Visible = true;
    }

    /// <summary>A screen's own key: closes it when it is the one open, otherwise opens it in place of any other.</summary>
    private void Toggle(CanvasLayer screen, Action open)
    {
        bool was = screen.Visible;
        CloseScreens();
        if (!was)
            open();
    }

    /// <summary>
    /// Whatever the panel was opened from - a container, a corpse, a station, a trader - it stays open only while the body is in reach
    /// of where that was (the owner's M6 playtest: the same rule for all of them), and a corpse searched bare closes it. The inventory
    /// opened with its own key has nowhere to be near, and stays.
    /// </summary>
    private void KeepContainerInReach()
    {
        if (_loot.Visible && _loot.Anchor is { } lootAt)
        {
            if (_session.Simulation!.Containers.All(c => c.Site.Key != _loot.OpenContainer)
                || Math.Sqrt(Math.Pow(lootAt.XMm - _controller.Authoritative.XMm, 2) + Math.Pow(lootAt.ZMm - _controller.Authoritative.ZMm, 2)) > _session.Setup.Items.Inventory.ReachMm)
                CloseInventory();
            return;
        }
        if (!_inventory.Visible || _inventory.Anchor is not { } anchor)
            return;
        // The pictures of the stash, station and trader screens open them from wherever the harness stands (they would close at once out of reach).
        if (_flags.Contains("--pack-shots") && _shots is not null)
            return;
        // A corpse searched bare is gone with what it held: the panel opened for it goes too, not back to the pack (the owner's first playtest).
        if (_inventory.OpenContainer is { } open && _session.Simulation!.Containers.All(c => c.Site.Key != open))
        {
            CloseInventory();
            return;
        }
        long reach = _session.Setup.Items.Inventory.ReachMm + (_inventory.OpenTrader is not null ? _session.Setup.Movement.BodyRadiusMm : 0);
        if (Math.Sqrt(Math.Pow(anchor.XMm - _controller.Authoritative.XMm, 2) + Math.Pow(anchor.ZMm - _controller.Authoritative.ZMm, 2)) > reach)
            CloseInventory();
    }

    /// <summary>The pack pictures show a hint and a toast over the open screen, as play does, so a layout that collapses them is seen.</summary>
    private void PackShotNotices()
    {
        _hintLine?.Show("The world went on while you were away, as it always will. The away log says what changed.");
        _hud.Toast("Skill: One-hand Blade 4", 6);
    }

    private void OpenInventory(string? container)
    {
        if (container is not null && _session.Simulation!.Containers.FirstOrDefault(c => c.Site.Key == container) is { Site.Stash: false })
            _loot.Open(container);
        else
            _inventory.Open(container);
    }

    private void OpenTrade(string npcId)
    {
        _inventory.OpenTrade(npcId);
    }

    private void OpenStation(string key)
    {
        var station = _session.Setup.Layout.Stations.Single(s => s.Key == key);
        if (station.Kind == NoticeBoardPanel.Kind)
            Show(_noticeBoard, key);
        else
            _inventory.OpenAt(station);
    }

    private void CloseInventory()
    {
        _loot.Close();
        _inventory.Close();
    }
}
