// UNNAMED Presentation - Main's panels: built and wired, and the screens opened, closed and kept in reach (D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>The panels and the System Menu: built, bound and given their icons.</summary>
    private void BuildPanels(string profile)
    {
        _inventory = new InventoryPanel { Name = "Inventory" };
        _inventory.Bind(_session, _controller);
        AddChild(_inventory);
        _dialogue = new DialoguePanel { Name = "Dialogue" };
        _dialogue.Bind(_session);
        AddChild(_dialogue);
        _journal = new JournalPanel { Name = "Journal" };
        AddChild(_journal);
        _questDebug = new QuestDebugPanel { Name = "QuestDebug" };
        AddChild(_questDebug);
        _character = new CharacterPanel { Name = "Character" };
        _character.Bind(_session);
        AddChild(_character);
        _help = new HelpPanel { Name = "Help", DeveloperKeys = _developer, Files = $"Saves: {profile}\nThe log, to send with a problem report: {LogPath}" };
        GD.Print($"UNNAMED files: saves in {profile}; the log at {LogPath}");
        AddChild(_help);
        _effectsPanel = new EffectsPanel { Name = "Effects" };
        _effectsPanel.Bind(_session);
        AddChild(_effectsPanel);
        _companionsPanel = new CompanionsPanel { Name = "Companions" };
        _companionsPanel.Bind(_session, _controller);
        AddChild(_companionsPanel);
        _waystones = new WaystonePanel { Name = "Waystones" };
        _waystones.Bind(_session, _controller);
        AddChild(_waystones);
        _map = new MapPanel { Name = "Map" };
        _map.Bind(_session);
        AddChild(_map);
        _soundPanel = new Ui.SoundPanel { Name = "Sound" };
        AddChild(_soundPanel);
        _menu = new SystemMenuPanel
        {
            Name = "SystemMenu",
            Entries = MenuEntries(),
            Quit = () => GetTree().Quit(0),
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
        _character.UseIcons(icons);
        _effectsPanel.UseIcons(icons);
        _companionsPanel.UseIcons(icons);
        _waystones.UseIcons(icons);
        _map.UseIcons(icons);
        _menu.UseIcons(icons);
        _journal.UseIcons(icons);
        _help.UseIcons(icons);
        _saves = new SavesPanel { Name = "Saves" };
        _saves.Bind(_session);
        _saves.NewGame = () =>
        {
            _session.NewGame(DefaultCharacter);
            Started();
        };
        _saves.Load = save => LoadChosen(save.Slot, save.Copy, save);
        _saves.Quit = () => GetTree().Quit(0);
        _saves.Closed = _saves.Close;
        AddChild(_saves);
        _saves.UseIcons(icons);
    }

    /// <summary>
    /// The System Menu's rows, in each group's order: group, words, key, icon, what choosing it does, and optionally whether it can be
    /// chosen yet. A new screen is one row here.
    /// </summary>
    private SystemMenuPanel.Entry[] MenuEntries() => new SystemMenuPanel.Entry[]
    {
        new("You", "Character", "character", "panel.character", FromMenu(() => Show(_character))),
        new("You", "Inventory", "inventory", "panel.inventory", FromMenu(() => OpenInventory(null))),
        new("You", "Effects", null, "panel.effects", FromMenu(() => Show(_effectsPanel))),
        new("The world", "Journal", "journal", "panel.journal", FromMenu(() => Show(_journal))),
        new("The world", "Map", "map", "panel.map", FromMenu(() => Show(_map))),
        new("The world", "Companions", null, "panel.companions", FromMenu(() => Show(_companionsPanel))),
        new("System", "Save and load", "saves", "panel.saves", FromMenu(() => _saves.OpenInGame())),
        new("System", "Sound", null, "panel.sound", FromMenu(() => Show(_soundPanel))),
        new("System", "Controls and help", "help", "panel.help", FromMenu(_help.Toggle)),
    };

    /// <summary>
    /// A panel that takes the keys and the mouse is open: a conversation, or a management screen - the System Menu, the inventory (on the
    /// character, a container, a trader or a station), the character sheet, the journal, effects, companions, the controls or the saves
    /// list (the Phase-1 technical audit, L-27; the Field Instrument UI). While one is, no gameplay key reaches the world, and the world
    /// runs on (the owner's ruling: nothing pauses for a panel). The quest debugger is an overlay: it takes no keys.
    /// </summary>
    public bool Modal => _dialogue.Visible || Screen is not null;

    /// <summary>The management screen open, if any: one at a time.</summary>
    private CanvasLayer? Screen =>
        new CanvasLayer[] { _menu, _inventory, _character, _journal, _effectsPanel, _companionsPanel, _waystones, _map, _soundPanel, _help, _saves }.FirstOrDefault(s => s.Visible);

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
        foreach (var screen in new CanvasLayer[] { _menu, _character, _journal, _effectsPanel, _companionsPanel, _map, _soundPanel })
            screen.Visible = false;
    }

    /// <summary>
    /// A screen opened by name, for the harnesses that picture them (the UI shots, the playthrough): Escape is pressed on whatever is open;
    /// every other name opens its screen in place of the last, and "" closes them.
    /// </summary>
    private void ScreenByName(string name)
    {
        if (name != "escape")
            CloseScreens();
        switch (name)
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
            case "inventory":
                OpenInventory(null);
                break;
            case "journal":
                Show(_journal);
                break;
            case "effects":
                Show(_effectsPanel);
                break;
            case "companions":
                Show(_companionsPanel);
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
        }
    }

    /// <summary>The Waystone panel, at the stone the character stands at.</summary>
    private void Show(WaystonePanel panel, string stoneKey)
    {
        CloseScreens();
        panel.Open(stoneKey);
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
            $"Played {Span(_session.PlaytimeSeconds)}", saved);
    }

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
        if (!_inventory.Visible || _inventory.Anchor is not { } anchor)
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

    private void OpenInventory(string? container)
    {
        _inventory.Open(container);
    }

    private void OpenTrade(string npcId)
    {
        _inventory.OpenTrade(npcId);
    }

    private void OpenStation(string key)
    {
        _inventory.OpenAt(_session.Setup.Layout.Stations.Single(s => s.Key == key));
    }

    private void CloseInventory()
    {
        _inventory.Close();
    }
}
