// UNNAMED Presentation - boot, the frame loop, and input (ARCHITECTURE.md §2, §8.1; D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Perf;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Spike;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// The presentation shell. Each frame: read input and submit commands, advance the session by the frame's time,
/// then draw - the body where the last tick put it plus the frame's share of the next, the camera behind or inside it.
/// Run modes come after <c>--</c> on the command line: <c>--smoke</c> (headless boot and save round trip),
/// <c>--perf [--perf-out dir] [--perf-seconds n]</c> (the performance capture), <c>--spike</c> (the 2x2 km greybox),
/// <c>--ui-shots dir</c>, and <c>--playthrough dir</c> then <c>--playthrough-verify dir</c> (M6: the acceptance run, and its relaunch).
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Input actions for the formula keys, 4 to 6.</summary>
    private static readonly string[] CastKeys = { "cast_1", "cast_2", "cast_3" };

    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private GameSession _session = null!;
    private HollowView _hollow = null!;
    private Avatar _avatar = null!;
    private CameraRig _camera = null!;
    private PlayerController _controller = null!;
    private Hud _hud = null!;
    private ItemsView _items = null!;
    private CreaturesView _creatures = null!;
    private CraftingView _crafting = null!;
    private NpcsView _npcs = null!;
    private InventoryPanel _inventory = null!;
    private DialoguePanel _dialogue = null!;
    private JournalPanel _journal = null!;
    private QuestDebugPanel _questDebug = null!;
    private FrameStats? _stats;
    private PerfRun? _perf;
    private Smoke? _smoke;
    private UiShots? _shots;
    private Playthrough? _play;
    private string _perfOut = string.Empty;
    private int _perfStruck, _perfDied;
    private Vector3 _lastFeet;
    private double _lastAlpha;

    public override void _Ready()
    {
        ParseArguments(OS.GetCmdlineUserArgs());
        if (_flags.Contains("--spike"))
        {
            AddChild(new SpikeScene(_options.GetValueOrDefault("--perf-out", DefaultPerfOut("spike")), Seconds()));
            return;
        }

        string contentRoot = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "content"));
        string? playthrough = _options.GetValueOrDefault("--playthrough") ?? _options.GetValueOrDefault("--playthrough-verify");
        string profile = playthrough is not null ? Path.Combine(Path.GetFullPath(playthrough), "profile")
            : _flags.Contains("--smoke") || _flags.Contains("--perf") || _options.ContainsKey("--ui-shots")
            ? Path.Combine(OS.GetUserDataDir(), "scratch", $"run-{System.Environment.ProcessId}")
            : Path.Combine(OS.GetUserDataDir(), "saves", "default");
        try
        {
            _session = GameSession.Boot(new GameOptions(contentRoot, profile));
        }
        catch (ContentBootException e)
        {
            GD.PushError(e.Message);   // ARCHITECTURE.md §8.1: refuse to start, naming every file
            GetTree().Quit(2);
            return;
        }
        // The acceptance playthrough plays one fixed world, so it is the same run every time (M6).
        _session.NewGame("Wanderer", _options.ContainsKey("--playthrough") ? Playthrough.Seed : 0);
        if (_options.ContainsKey("--playthrough-verify"))
            _session.Load(SaveSlots.Manual(Playthrough.Slot));
        GD.Print($"UNNAMED boot: content {_session.Content.Version} ({_session.Content.Hash[..19]}...), region {_session.Setup.Layout.Id}, " +
                 $"{_session.Setup.Layout.CellKeys.Length} cells, seed {WorldSeed.Format(_session.Simulation!.World.WorldSeed)}");

        _hollow = new HollowView { Name = "Hollow" };
        AddChild(_hollow);
        _hollow.Build(_session.Setup.Layout);
        _avatar = new Avatar { Name = "Player" };
        AddChild(_avatar);
        _camera = new CameraRig { Name = "CameraRig" };
        AddChild(_camera);
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        _items = new ItemsView { Name = "Items" };
        AddChild(_items);
        _items.BuildContainers(_session.Setup.Layout);
        _creatures = new CreaturesView { Name = "Creatures" };
        AddChild(_creatures);
        _crafting = new CraftingView { Name = "Crafting" };
        AddChild(_crafting);
        _crafting.Build(_session.Setup.Layout);
        _npcs = new NpcsView { Name = "Npcs" };
        AddChild(_npcs);
        _controller = new PlayerController(_session);
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
        Subscribe();
        Resync();
        DefineInput();

        if (_flags.Contains("--smoke"))
        {
            _smoke = new Smoke(_session, _controller, _camera, profile);
        }
        else if (_options.TryGetValue("--ui-shots", out string? shots))
        {
            _shots = new UiShots(_session, _controller, _camera, _inventory, _dialogue, _journal, _questDebug, shots);
        }
        else if (playthrough is not null)
        {
            // One tick a frame, at the tick rate: the run is the same every time, and plays in real time (toasts and all).
            Engine.MaxFps = (int)Math.Round(1 / _session.TickSeconds);
            _play = new Playthrough(_session, _controller, _camera, _dialogue, Path.GetFullPath(playthrough), _options.ContainsKey("--playthrough-verify"));
        }
        else if (_flags.Contains("--perf"))
        {
            _perfOut = _options.GetValueOrDefault("--perf-out", DefaultPerfOut("prototype"));
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);   // measure the headroom, not the refresh rate
            _stats = new FrameStats(GetViewport());
            _perf = new PerfRun(Seconds());
            _perf.SpawnProxies(this, _session.Setup.Layout);
            _session.Subscribe<HitResolved>(e => _perfStruck += e.Target == _session.Simulation!.PlayerId ? 1 : 0);
            _session.Subscribe<PlayerDied>(_ => _perfDied++);
        }
        else if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Process(double delta)
    {
        if (_session?.Simulation is null)
            return;

        if (_perf is not null)
        {
            if (!_perf.Update(delta, _controller, _camera, _stats!, _session.Setup.Layout))
            {
                FinishPerf();
                return;
            }
        }
        else if (_smoke is not null)
        {
            if (_smoke.Update() is { } code)
            {
                GetTree().Quit(code);
                return;
            }
        }
        else if (_shots is not null)
        {
            switch (_shots.Update())
            {
                case "done":
                    GD.Print($"UNNAMED ui shots written to {_shots.Directory}");
                    GetTree().Quit(0);
                    return;
                case "failed":
                    SaveScreenshot(_shots.Directory, "failed");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_shots.Directory, shot);
                    break;
            }
        }
        else if (_play is not null)
        {
            switch (_play.Update())
            {
                case "done":
                    GetTree().Quit(0);
                    return;
                case "failed":
                    SaveScreenshot(_play.Directory, "failed");
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_play.Directory, shot);
                    break;
            }
        }
        else
        {
            ReadInput();
        }
        KeepContainerInReach();

        // The smoke and the playthrough run one tick per frame: the smoke finishes in a fraction of real time, and the playthrough is
        // the same run every time, whatever the frame rate.
        var frame = _session.Frame(_smoke is not null || _play is not null ? _session.TickSeconds : delta);
        if (frame.AutosavedTo is { } slot)
            _hud.Toast($"Autosaved ({slot})", 2);
        Draw(frame.Alpha, delta);
        _stats?.Record(delta);
        if (_perf is { ScreenshotDue: true })
        {
            SaveScreenshot(_perfOut, _perf.SegmentName);
            _stats!.SkipNext();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_session?.Simulation is null || _perf is not null || _smoke is not null || _shots is not null || _play is not null)
            return;
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && !_inventory.Visible && !_dialogue.Visible:
                _camera.Look(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _camera.Zoom(-0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _camera.Zoom(0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Input.MouseMode != Input.MouseModeEnum.Captured && !_inventory.Visible
                && !_dialogue.Visible:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
        }
    }

    private void ReadInput()
    {
        var stick = new Vector2(
            Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
            Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back"));
        var gait = Input.IsActionPressed("sprint") ? Gait.Sprint : Input.IsActionPressed("walk") ? Gait.Walk : Gait.Run;

        // Combat: the left button swings or shoots, the right holds a guard (or aims a bow), C dodges, H uses a salve.
        // A swing, a guard and an aimed bow all go where the camera looks.
        var combat = _session.Simulation!.Combat;
        // In a conversation the number keys answer, and Escape walks away (M4).
        if (_dialogue.Visible)
        {
            for (int n = 1; n <= 9; n++)
            {
                if (Input.IsActionJustPressed($"reply_{n}"))
                    _dialogue.AnswerNumber(n);
            }
            if (Input.IsActionJustPressed("release_mouse"))
                _dialogue.Leave();
        }
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured && !_inventory.Visible && !_dialogue.Visible;
        bool holding = captured && Input.IsActionPressed("guard");
        bool swing = captured && Input.IsActionJustPressed("attack");
        int slot = captured ? Array.FindIndex(CastKeys, key => Input.IsActionJustPressed(key)) : -1;
        _controller.Steer(_camera, stick, gait, faceCamera: holding || swing || slot >= 0 || combat.Phase == CombatPhase.Windup);
        if (swing)
            _controller.Attack();
        if (slot >= 0)
        {
            // A working goes where the camera looks, like a swing (M3e).
            var hotbar = _controller.Formulas();
            if (slot < hotbar.Length)
                _controller.Cast(hotbar[slot]);
            else
                _hud.Toast("No formula known for that key", 2);
        }
        bool guard = holding && !combat.Weapon.Ranged;
        if (guard != combat.Blocking && (!guard || combat.Phase == CombatPhase.Idle))
            _controller.Guard(guard);
        if (captured && Input.IsActionJustPressed("dodge"))
        {
            var wish = stick.LengthSquared() > 0.01f ? stick.Normalized() : Vector2.Zero;
            _controller.Dodge(_camera.GroundForward * wish.Y + _camera.GroundRight * wish.X);
        }
        if (Input.IsActionJustPressed("use") && !_controller.UseConsumable())
            _hud.Toast("Nothing to use", 2);

        if (Input.IsActionJustPressed("interact") && _controller.FocusOn(_camera) is { } focus)
        {
            switch (focus.Kind)
            {
                case FocusKind.Door:
                    _controller.Interact(focus.Key);
                    break;
                case FocusKind.Container:
                    OpenInventory(focus.Key);
                    break;
                case FocusKind.Item:
                    _controller.PickUp(focus.Key);
                    break;
                case FocusKind.Node:
                    _controller.Gather(focus.Key);
                    break;
                case FocusKind.Station:
                    OpenStation(focus.Key);
                    break;
                case FocusKind.Npc:
                    if (DownedCompanion(focus.Key) is not null)
                        _controller.Revive(focus.Key);
                    else
                        _controller.Talk(focus.Key);
                    break;
                case FocusKind.Switch:
                    _controller.Interact(focus.Key);
                    break;
            }
        }
        if (Input.IsActionJustPressed("companion_order"))
        {
            // One key for every companion on their feet: those following wait, those waiting follow.
            var up = _session.Simulation!.Companions.Where(c => c.Condition == CompanionCondition.Up).ToList();
            if (up.Count == 0)
                _hud.Toast("No one is with you", 2);
            foreach (var companion in up)
                _controller.Order(companion.NpcId, companion.Order == CompanionOrder.Follow ? CompanionOrder.Wait : CompanionOrder.Follow);
        }
        if (Input.IsActionJustPressed("inventory"))
        {
            if (_inventory.Visible)
                CloseInventory();
            else
                OpenInventory(null);
        }
        if (Input.IsActionJustPressed("jump"))
            _avatar.Hop();
        if (Input.IsActionJustPressed("first_person"))
            _camera.ToggleFirstPerson();
        if (Input.IsActionJustPressed("shoulder_swap"))
            _camera.SwapShoulder();
        if (Input.IsActionJustPressed("debug_overlay"))
            _hud.DebugVisible = _hollow.DebugVisible = !_hud.DebugVisible;
        if (Input.IsActionJustPressed("journal"))
            _journal.Visible = !_journal.Visible;
        if (Input.IsActionJustPressed("quest_debug"))
        {
            _questDebug.Visible = !_questDebug.Visible;
            _questDebug.Refresh(_session, 0, now: true);
        }
        if (Input.IsActionJustPressed("quicksave"))
            QuickSave();
        if (Input.IsActionJustPressed("quickload"))
            QuickLoad();
        if (Input.IsActionJustPressed("release_mouse") && !_dialogue.Visible)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>
    /// A container or a station stays open only while it is in reach; a container that is gone (an emptied corpse) closes
    /// its column.
    /// </summary>
    private void KeepContainerInReach()
    {
        if (_inventory.OpenTrader is { } trader)
        {
            if (_session.Simulation!.Npcs.FirstOrDefault(n => n.Id == trader) is not { } npc
                || Math.Sqrt(Math.Pow(npc.Body.XMm - _controller.Authoritative.XMm, 2) + Math.Pow(npc.Body.ZMm - _controller.Authoritative.ZMm, 2))
                > _session.Setup.Items.Inventory.ReachMm + _session.Setup.Movement.BodyRadiusMm)
                CloseInventory();
            return;
        }
        if (_inventory.OpenStation is { } station)
        {
            if (Math.Sqrt(Math.Pow(station.XMm - _controller.Authoritative.XMm, 2) + Math.Pow(station.ZMm - _controller.Authoritative.ZMm, 2))
                > _session.Setup.Items.Inventory.ReachMm)
                CloseInventory();
            return;
        }
        if (_inventory.OpenContainer is not { } open)
            return;
        if (_session.Simulation!.Containers.FirstOrDefault(c => c.Site.Key == open)?.Site is not { } site)
            _inventory.Open(null);
        else if (Math.Sqrt(Math.Pow(site.XMm - _controller.Authoritative.XMm, 2) + Math.Pow(site.ZMm - _controller.Authoritative.ZMm, 2))
                 > _session.Setup.Items.Inventory.ReachMm)
            CloseInventory();
    }

    private void Draw(double alpha, double delta)
    {
        var predicted = _controller.Predict(alpha);
        var feet = HollowView.ToGodot(predicted.XMm, predicted.YMm, predicted.ZMm);
        float speed = delta > 0 ? new Vector2(feet.X - _lastFeet.X, feet.Z - _lastFeet.Z).Length() / (float)delta : 0;
        // A tick boundary snaps the prediction back to zero progress; speed across it would read as a stumble.
        if (alpha < _lastAlpha)
            speed = _controller.Intent.IsMoving ? (float)_session.Setup.Movement.SpeedMmPerSecond(_controller.Intent.Gait) / 1000f : 0;
        _lastFeet = feet;
        _lastAlpha = alpha;

        var simulation = _session.Simulation!;
        var combat = simulation.Combat;
        _avatar.SetStance(Stance(combat, alpha));
        _avatar.Pose(feet, PlayerController.FacingRadians(predicted.FacingMdeg), Math.Min(speed, 8f), delta);
        _creatures.Draw(simulation, alpha, delta);
        _crafting.Refresh(simulation.Nodes);
        _npcs.Draw(simulation, delta);
        _camera.Follow(_shots?.Viewpoint ?? _avatar.Position, delta);
        _avatar.SetFirstPerson(_camera.EffectiveDistance < 0.4f);
        _hud.SetCrosshair(_camera.IsFirstPerson);

        _hud.SetPrompt(_controller.FocusOn(_camera) switch
        {
            null => null,
            { Kind: FocusKind.Door } door => $"[E] {(_controller.IsOpen(door.Key) ? "Close" : "Open")} the {Describe(door.Key)}",
            { Kind: FocusKind.Container } container => container.DefId == container.Key
                ? $"[E] Open the {Describe(container.Key)}"
                : $"[E] Search the {_session.DisplayName(container.DefId)}",
            { Kind: FocusKind.Node } node => NodePrompt(simulation, node),
            { Kind: FocusKind.Station } station => $"[E] Work at the {Describe(station.Key)}",
            { Kind: FocusKind.Npc } npc when DownedCompanion(npc.Key) is not null => $"[E] Help {_session.DisplayName(npc.Key)} up",
            { Kind: FocusKind.Npc } npc => simulation.Conversation?.NpcId == npc.Key ? null : $"[E] Talk to {_session.DisplayName(npc.Key)}",
            { Kind: FocusKind.Switch } site => _session.Setup.Layout.FindSwitch(site.Key) is { } s ? $"[E] {s.Verb} the {s.Name}" : null,
            { Kind: FocusKind.Barrier } barrier => _session.Setup.Layout.Barriers.First(b => b.Key == barrier.Key).Prompt,
            { } item => $"[E] Pick up {ItemName(_session, item.DefId, item.Quality)}",
        });

        var view = simulation.Player;
        var stats = view.Stats;
        var pools = view.Progression.Pools;
        _hud.SetStatus(
            $"{view.Name}   Level {view.Progression.Level}   XP {view.Progression.LevelProgressXp}/{_session.Setup.Progression.Curve.ToReach(view.Progression.Level + 1)}" +
            (view.Progression.XpDebt > 0 ? $"   debt {view.Progression.XpDebt}" : "") +
            $"\nHealth {combat.Health}/{combat.MaxHealth}   Stamina {combat.Stamina}/{combat.MaxStamina}" +
            $"   Focus {pools.Focus ?? stats.FocusMax}/{stats.FocusMax}   Strain {pools.Strain}/{stats.StrainTolerance}   Resonance {stats.Resonance}" +
            $"\n{ItemName(_session, combat.Weapon.Source, Wielded(view)?.Quality ?? 0)}{(combat.Blocking ? " (guarding)" : "")}   Coin {view.Currency}   Armor {view.Armor}" +
            $"   Carrying {view.CarriedGrams / 1000.0:0.#}/{view.CarryLimitGrams / 1000.0:0.#} kg   [Tab] inventory");
        _hud.SetVitals(combat.Health, combat.MaxHealth, combat.Stamina, combat.MaxStamina);
        _hud.SetMagicPools(combat.Focus, combat.MaxFocus, combat.Strain, combat.StrainTolerance, combat.Strained);
        var formulas = _controller.Formulas();
        _hud.SetMagic((combat.Casting is { } casting ? $"Casting {_session.DisplayName(casting)}\n" : "") +
            string.Join("   ", formulas.Select((f, i) => $"[{i + 4}] {_session.DisplayName(f)} {_session.Setup.Magic.Formulas[f].FocusCost}F")) +
            (combat.Strained ? "   Strained" : ""));
        _hud.SetEffects(string.Join("   ", combat.Effects.Select(e =>
            $"{_session.DisplayName(e.EffectId)}{(e.Stacks > 1 ? $" x{e.Stacks}" : "")} {Math.Max(0, e.ExpiresTick - simulation.WorldTick) * _session.TickSeconds:0}s")));
        if (Target(simulation) is { } target)
            _hud.SetTarget(_session.DisplayName(target.DefId), target.Health, target.MaxHealth);
        else
            _hud.SetTarget(null, 0, 0);
        var quests = simulation.Quests;
        _hud.SetTracker(JournalPanel.Tracker(quests));
        _hud.SetCompanions(string.Join("\n", simulation.Companions.Select(c =>
            $"{c.Name} - {c.Doing}, {c.Standing}" +
            (c.FallsAtTick is { } falls ? $" - falls in {Math.Max(0, falls - simulation.WorldTick) * _session.TickSeconds:0} s unless helped up" : "") +
            (c.Condition == CompanionCondition.Up ? $"   [G] {(c.Order == CompanionOrder.Follow ? "wait" : "follow")}" : ""))));
        _journal.Refresh(_session);
        _questDebug.Refresh(_session, delta);

        if (_hud.DebugVisible)
        {
            var body = _controller.Authoritative;
            _hud.SetDebug(
                $"{Engine.GetFramesPerSecond()} fps   {delta * 1000:0.0} ms\n" +
                $"tick {simulation.WorldTick}   alpha {alpha:0.00}   pending {simulation.PendingCommands}\n" +
                $"body ({body.XMm / 1000.0:0.00}, {body.YMm / 1000.0:0.00}, {body.ZMm / 1000.0:0.00}) facing {body.FacingMdeg / 1000.0:0.0}\n" +
                $"cell {CellKey.OfWorld(body.XMm / 1000.0, body.ZMm / 1000.0)}\n" +
                $"camera {(_camera.IsFirstPerson ? "first person" : "third person")} {_camera.EffectiveDistance:0.00}/{_camera.TargetDistance:0.00} m, {_camera.Side}\n" +
                "tiers:\n" + string.Join("\n", simulation.CellTiers.Select(kv => $"  {kv.Key}  {kv.Value}")) +
                "\ndiscovered:\n" + string.Join("\n", view.Discoveries.Select(d => $"  {_session.DisplayName(d.LocationId)} (tick {d.Tick})")));
        }
    }

    private void Subscribe()
    {
        _session.Subscribe<BodyMoved>(_controller.OnBodyMoved);
        _session.Subscribe<DoorToggled>(e =>
        {
            _controller.OnDoorToggled(e);
            _hollow.SetDoor(e.DoorKey, e.Open);
        });
        // Switches and barriers (M6): what a switch did, and every flag change shown as it now stands.
        _session.Subscribe<SwitchSet>(e =>
        {
            if (_session.Setup.Layout.FindSwitch(e.SwitchKey) is { } site)
                _hud.Toast(site.DoneText, 6);
        });
        _session.Subscribe<WorldFlagChanged>(_ => _hollow.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers));
        _session.Subscribe<LocationDiscovered>(e => _hud.Toast($"Discovered: {_session.DisplayName(e.LocationId)}"));
        // Companions (M6): joined, told, downed, helped up, fallen back to the Waystone.
        _session.Subscribe<CompanionRecruited>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)} joins you"));
        _session.Subscribe<CompanionOrdered>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)}: {(e.Order == CompanionOrder.Follow ? "following" : "waiting")}", 2));
        _session.Subscribe<CompanionDowned>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)} is down - reach them and press E", 6));
        _session.Subscribe<CompanionRevived>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)} is back on their feet"));
        _session.Subscribe<CompanionFell>(e => _hud.Toast($"{_session.DisplayName(e.NpcId)} fell, and will be waiting at the Ashen Waystone", 6));
        _session.Subscribe<ExperienceGained>(e => _hud.Toast(e.LevelsGained > 0 ? $"+{e.Awarded} XP - level {e.Level}!" : $"+{e.Awarded} XP", 3));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is InteractCommand or MoveItemCommand or EquipCommand or UnequipCommand or GatherCommand or CraftCommand)
                _hud.Toast(e.Reason, 3);
        });
        _session.Subscribe<ItemMoved>(e =>
        {
            _inventory.Refresh();
            if (e.From.Kind == PlaceKind.Ground || e.To.Kind == PlaceKind.Ground)
                _items.Refresh(_session.Simulation!);
        });
        _session.Subscribe<ItemEquipped>(_ => _inventory.Refresh());
        _session.Subscribe<ItemUnequipped>(_ => _inventory.Refresh());
        _session.Subscribe<NodeGathered>(e =>
        {
            _inventory.Refresh();
            string spent = !e.Spent ? ""
                : _session.Setup.Crafting.Nodes[e.NodeDefId].Respawn == Respawn.None ? $" - the {_session.DisplayName(e.NodeDefId)} is worked out"
                : $" - nothing more until the {_session.DisplayName(e.NodeDefId)} grows back";
            _hud.Toast($"+{e.Count} {_session.DisplayName(e.ItemId)}{spent}", 3);
        });
        _session.Subscribe<ItemCrafted>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Made {ItemName(_session, e.ItemId, e.Quality)}{(e.Count > 1 ? $" x{e.Count}" : "")}", 3);
        });
        SubscribeSocial();
        SubscribeQuests();
        SubscribeCombat();
    }

    /// <summary>Quests (M5): a toast when one starts, moves or ends; what it pays in the log. The tracker and journal redraw each frame.</summary>
    private void SubscribeQuests()
    {
        var quests = _session.Setup.Quests.Quests;
        string Title(string id) => quests.TryGetValue(id, out var q) ? q.Title : id;
        string Step(string quest, string objective) =>
            quests.TryGetValue(quest, out var q) && q.Objectives.TryGetValue(objective, out var o) ? o.Description : objective;
        _session.Subscribe<QuestStarted>(e => _hud.Toast($"New quest: {Title(e.QuestId)}", 5));
        _session.Subscribe<ObjectiveSatisfied>(e =>
        {
            if (_session.Simulation!.Quests.FirstOrDefault(q => q.Id == e.QuestId)?.Status == Domain.Quests.QuestStatus.Active)
                _hud.Toast($"Done: {Step(e.QuestId, e.ObjectiveId)}", 3);
        });
        _session.Subscribe<QuestCompleted>(e => _hud.Toast($"Quest complete: {Title(e.QuestId)}", 6));
        _session.Subscribe<QuestFailed>(e => _hud.Toast($"Quest failed: {Title(e.QuestId)}", 6));
        // XP, a technique and a relationship already speak for themselves (their own toast or log line); coin and goods do not.
        _session.Subscribe<RewardGranted>(e =>
        {
            if (e.Kind == "currency")
                _hud.Log($"{Title(e.QuestId)}: +{e.Amount} coin");
            else if (e.Kind == "item" && e.Ref is { } item)
                _hud.Log($"{Title(e.QuestId)}: {_session.DisplayName(item)}{(e.Amount > 1 ? $" x{e.Amount}" : "")}");
        });
    }

    /// <summary>The waystation's people (M4): conversations on their panel, trade on the inventory's, and what they think in the log.</summary>
    private void SubscribeSocial()
    {
        _session.Subscribe<ConversationLine>(_ =>
        {
            _dialogue.Refresh();
            if (DisplayServer.GetName() != "headless")
                Input.MouseMode = Input.MouseModeEnum.Visible;
        });
        _session.Subscribe<ConversationEnded>(_ =>
        {
            _dialogue.Refresh();
            if (!_inventory.Visible && DisplayServer.GetName() != "headless")
                Input.MouseMode = Input.MouseModeEnum.Captured;
        });
        _session.Subscribe<ServiceOpened>(e =>
        {
            if (e.Service == Domain.Social.NpcServices.Trade)
                OpenTrade(e.NpcId);
        });
        _session.Subscribe<ItemBought>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Bought {_session.DisplayName(e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<ItemSold>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Sold {_session.DisplayName(e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<RelationshipChanged>(e =>
            _hud.Log($"{_session.DisplayName(e.NpcId)}: {e.Dimension} {(e.To >= e.From ? "+" : "")}{e.To - e.From}"));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is TalkCommand or ChooseCommand or BuyCommand or SellCommand)
                _hud.Toast(e.Reason, 3);
        });
    }

    /// <summary>A node's prompt: what it gives now, or why it gives nothing.</summary>
    private string NodePrompt(Simulation simulation, Focus focus)
    {
        string name = _session.DisplayName(focus.DefId);
        if (simulation.Nodes.FirstOrDefault(n => n.Key == focus.Key) is { Ready: true })
            return $"[E] Gather from the {name}";
        return _session.Setup.Crafting.Nodes[focus.DefId].Respawn == Respawn.None
            ? $"The {name} is worked out"
            : $"The {name} has nothing to take until it grows back";
    }

    /// <summary>The carried entry in the main hand, if any.</summary>
    private static InventoryEntry? Wielded(PlayerView view) =>
        view.Equipment.TryGetValue(EquipSlot.MainHand, out var id) ? view.Inventory.FirstOrDefault(e => e.ItemId == id) : null;

    /// <summary>Combat reads in words as well as poses: every blow, effect, kill and death goes to the log (ROADMAP.md M3c).</summary>
    private void SubscribeCombat()
    {
        string Name(string id) => _session.DisplayName(id);
        _session.Subscribe<HitResolved>(e =>
        {
            var player = _session.Simulation!.PlayerId;
            string how = (e.Critical ? ", critical" : "") + (e.Blocked ? ", blocked" : "") + (e.Staggered ? ", staggered" : "");
            if (e.Attacker == player)
                _hud.Log($"You hit the {Name(CreatureDef(e.Target))} ({BodyRegions.Key(e.Region)}) for {e.Damage}{how}");
            else if (e.Dodged)
                _hud.Log($"You dodge the {Name(e.AttackerDefId)}'s {Name(e.Source).ToLowerInvariant()}");
            else
                _hud.Log($"The {Name(e.AttackerDefId)}'s {Name(e.Source).ToLowerInvariant()} hits your {BodyRegions.Key(e.Region)} for {e.Damage}{how}");
        });
        _session.Subscribe<AttackMissed>(e =>
        {
            if (e.Attacker == _session.Simulation!.PlayerId)
                _hud.Log(e.Source == "item.weapon.hunting_bow" ? "Your arrow finds nothing" : "Your swing finds nothing");
        });
        _session.Subscribe<HealthChanged>(e =>
        {
            if (e.Target == _session.Simulation!.PlayerId && e.Source != "strain")
                _hud.Log($"{Name(e.Source)}: {(e.Delta > 0 ? "+" : "")}{e.Delta}");
        });
        _session.Subscribe<CastCompleted>(e => _hud.Log($"You work {Name(e.FormulaId)} (+{e.Strain} Strain)"));
        _session.Subscribe<CastFizzled>(e => _hud.Log($"Your {Name(e.FormulaId)} fizzles (+{e.Strain} Strain)"));
        _session.Subscribe<CastInterrupted>(e => _hud.Log($"Your {Name(e.FormulaId)} is broken off ({e.Reason})"));
        _session.Subscribe<StrainBacklash>(e => _hud.Log($"Strain backlash: -{e.Damage} health"));
        _session.Subscribe<TechniqueLearned>(e => _hud.Toast($"Learned {Name(e.DefinitionId)}", 4));
        _session.Subscribe<EffectApplied>(e =>
        {
            if (e.Target == _session.Simulation!.PlayerId)
                _hud.Log($"You are {Name(e.EffectId).ToLowerInvariant()}{(e.Stacks > 1 ? $" (x{e.Stacks})" : "")}");
        });
        _session.Subscribe<GuardBroken>(_ => _hud.Toast("Guard broken - no stamina behind it", 2));
        _session.Subscribe<CreatureKilled>(e => _hud.Log($"The {Name(e.DefId)} dies"));
        _session.Subscribe<SkillPracticed>(e =>
        {
            if (e.Xp > 0)
                _hud.Log($"{Name(e.SkillId)} +{e.Xp} XP (level {e.Level})");
        });
        _session.Subscribe<ItemUsed>(e => _hud.Toast(e.EffectId is null ? $"Read {Name(e.DefId)}" : $"Used {Name(e.DefId)}", 2));
        _session.Subscribe<ItemConsumed>(_ => _inventory.Refresh());
        _session.Subscribe<PlayerDied>(e =>
        {
            string recap = string.Join("\n", e.Recap.Select(r =>
                r.AttackerDefId == r.Source ? $"{Name(r.Source)}: {r.Damage}" : $"{Name(r.AttackerDefId)}, {Name(r.Source).ToLowerInvariant()}: {r.Damage}"));
            string killer = e.KillerDefId == e.Cause ? Name(e.Cause) : $"the {Name(e.KillerDefId)} ({Name(e.Cause).ToLowerInvariant()})";
            _hud.ShowDeath($"You died - killed by {killer}.\n\nThe last blows:\n{recap}\n\nXP debt +{e.DebtAdded} (nothing earned is lost). " +
                           "You return at the Ashen Waystone, weakened for a minute.");   // content bible §18
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is AttackCommand or DodgeCommand or UseItemCommand or CastCommand && !e.Reason.StartsWith("already", StringComparison.Ordinal)
                && e.Reason is not ("staggered" or "dodging" or "dead"))
                _hud.Toast(e.Reason, 2);
        });
    }

    private string CreatureDef(Domain.EntityId id) =>
        _session.Simulation!.Creatures.FirstOrDefault(c => c.Id == id)?.DefId ?? "creature";

    /// <summary>The creature to show: the nearest one hunting the character, or the nearest living one the camera faces.</summary>
    private CreatureView? Target(Simulation simulation)
    {
        var body = _controller.Authoritative;
        double Distance(CreatureView c) => Math.Sqrt(Math.Pow(c.Body.XMm - body.XMm, 2) + Math.Pow(c.Body.ZMm - body.ZMm, 2));
        var living = simulation.Creatures.Where(c => c.Alive).ToList();
        var hunting = living.Where(c => c.Hostile && Distance(c) < 30_000).OrderBy(Distance).FirstOrDefault();
        if (hunting is not null)
            return hunting;
        return living.Where(c => Distance(c) < 20_000)
            .Where(c => new Vector3(c.Body.XMm - body.XMm, 0, c.Body.ZMm - body.ZMm).Normalized().Dot(_camera.GroundForward) > 0.85f)
            .OrderBy(Distance).FirstOrDefault();
    }

    /// <summary>How far the body is through its combat phase, from the simulation's own phase lengths, for the pose.</summary>
    private CombatStance Stance(CombatView combat, double alpha)
    {
        var constants = _session.Setup.Combat.Constants;
        var weapon = combat.Weapon;
        var working = combat.Casting is { } casting ? _session.Setup.Magic.Formulas[casting] : null;
        int length = combat.Phase switch
        {
            CombatPhase.Windup when working is not null => working.CastTicks,
            CombatPhase.Active when working is not null => 1,
            CombatPhase.Recovery when working is not null => working.RecoveryTicks,
            CombatPhase.Windup => weapon.WindupTicks,
            CombatPhase.Active => weapon.ActiveTicks,
            CombatPhase.Recovery => combat.AttackSource is null ? constants.DodgeRecoveryTicks : weapon.RecoveryTicks,
            CombatPhase.Dodge => constants.DodgeTicks,
            CombatPhase.Staggered => constants.StaggerTicks,
            _ => 1,
        };
        float progress = (float)((length - combat.PhaseTicksLeft + alpha) / Math.Max(1, length));
        var held = weapon.Ranged ? Held.Bow
            : weapon.Source == "unarmed" ? Held.Nothing
            : _session.Setup.Items.Catalog.Find(weapon.Source)?.Weapon?.TwoHanded == true ? Held.Spear   // the greybox draws a two-hander as a spear
            : Held.Sword;
        return new CombatStance(combat.Phase, progress, held, combat.Blocking, working is not null);
    }

    /// <summary>After a new game or a load: the only moments presentation copies the whole state.</summary>
    private void Resync()
    {
        _controller.Resync();
        _creatures.Reset();
        foreach (var door in _session.Simulation!.Doors)
            _hollow.SetDoor(door.Site.Key, door.Open);
        _hollow.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers);
        _items.Refresh(_session.Simulation!);
        _inventory.Refresh();
        var body = _controller.Authoritative;
        _camera.Yaw = PlayerController.FacingRadians(body.FacingMdeg) + Mathf.Pi;
        _lastFeet = HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);
    }

    private void QuickSave()
    {
        try
        {
            _session.Save(SaveSlots.Quick);
            _hud.Toast("Saved");
        }
        catch (SaveException e)
        {
            _hud.Toast($"Save failed: {e.Message}");
        }
    }

    private void QuickLoad()
    {
        try
        {
            var result = _session.Load(SaveSlots.Quick);
            Resync();
            _hud.Toast(result.IsComplete ? "Loaded" : "Loaded, with losses - see the log");
            foreach (string problem in result.Report.Loss.Concat(result.Report.Warnings))
                GD.PushWarning(problem);
        }
        catch (SaveCorruptionException e)
        {
            _hud.Toast(e.BackupGenerations.IsEmpty ? "The save is corrupt" : "The save is corrupt; a backup exists");
        }
        catch (SaveException e)
        {
            _hud.Toast($"Load failed: {e.Message}");
        }
    }

    private void FinishPerf()
    {
        var notes = new Dictionary<string, string>
        {
            ["scene"] = "PROTOTYPE greybox: Ashen Hollow, 200 m x 200 m, 4 cells",
            ["script"] = _perf!.Summary,
            ["vsync"] = "disabled for the capture, so frame times show headroom rather than the refresh rate",
            ["screenshots"] = "one per segment, halfway through; the frame after each is left out of the numbers (the capture stalls the GPU)",
            ["gate"] = "owner ruling 2026-09-23: sustained 60 FPS at 1080p on RAZER's RTX 4070 Ti with OBS, H3 and other GPU workloads stopped",
            ["route"] = $"{_perf.Progress}; the character was struck {_perfStruck} times and died {_perfDied} times (a clean capture: 0 and 0)",
        };
        string summary = _stats!.Write(_perfOut, notes);
        GD.Print($"UNNAMED perf capture written to {_perfOut}\n{summary}");
        if (_perfStruck + _perfDied > 0)
            GD.PushWarning($"UNNAMED perf: a creature reached the character ({_perfStruck} blows, {_perfDied} deaths) - the capture measured a fight");
        GetTree().Quit(0);
    }

    /// <summary>Keep a picture of what a capture segment was measuring, next to its numbers.</summary>
    public static void SaveScreenshot(Viewport viewport, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        viewport.GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
    }

    private void SaveScreenshot(string directory, string name) => SaveScreenshot(GetViewport(), directory, name);

    private void OpenInventory(string? container)
    {
        _inventory.Open(container);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void OpenTrade(string npcId)
    {
        _inventory.OpenTrade(npcId);
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void OpenStation(string key)
    {
        _inventory.OpenAt(_session.Setup.Layout.Stations.Single(s => s.Key == key));
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void CloseInventory()
    {
        _inventory.Close();
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>The companion lying downed as this NPC, or null (M6).</summary>
    private CompanionView? DownedCompanion(string npcId) =>
        _session.Simulation!.Companions.FirstOrDefault(c => c.NpcId == npcId && c.Condition == CompanionCondition.Downed);

    /// <summary>A container read as words; a corpse is named for the creature it was.</summary>
    internal static string Describe(GameSession session, string key) =>
        session.Simulation?.Creatures.FirstOrDefault(c => c.CorpseKey == key) is { } dead ? $"{session.DisplayName(dead.DefId)} remains" : Describe(key);

    /// <summary>A door, container or station key read as words: <c>door.forge_shed</c> is the forge shed door.</summary>
    internal static string Describe(string key) => key switch
    {
        _ when key.StartsWith("door.", StringComparison.Ordinal) => key["door.".Length..].Replace('_', ' ') + " door",
        _ when key.StartsWith("container.", StringComparison.Ordinal) => key["container.".Length..].Replace('_', ' '),
        _ when key.StartsWith("station.", StringComparison.Ordinal) => key["station.".Length..].Replace('_', ' '),
        _ => key,
    };

    /// <summary>An item's name with its quality (M3f): a fine or crude one says so; a standard one is just itself.</summary>
    internal static string ItemName(GameSession session, string defId, int quality) => quality switch
    {
        Quality.Fine => "Fine " + session.DisplayName(defId),
        Quality.Crude => "Crude " + session.DisplayName(defId),
        _ => session.DisplayName(defId),
    };

    private void ParseArguments(string[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is "--perf-out" or "--perf-seconds" or "--ui-shots" or "--playthrough" or "--playthrough-verify" && i + 1 < arguments.Length)
                _options[arguments[i]] = arguments[++i];
            else
                _flags.Add(arguments[i]);
        }
    }

    private double Seconds() =>
        double.TryParse(_options.GetValueOrDefault("--perf-seconds"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double seconds) ? seconds : 75;

    private static string DefaultPerfOut(string name) =>
        Path.Combine(OS.GetUserDataDir(), "perf", $"{name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

    private static void DefineInput()
    {
        void Bind(string action, params Key[] keys)
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            foreach (var key in keys)
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
        Bind("move_forward", Key.W, Key.Up);
        Bind("move_back", Key.S, Key.Down);
        Bind("move_left", Key.A, Key.Left);
        Bind("move_right", Key.D, Key.Right);
        Bind("sprint", Key.Shift);
        Bind("walk", Key.Ctrl);
        Bind("interact", Key.E);
        Bind("jump", Key.Space);
        Bind("first_person", Key.V);
        Bind("shoulder_swap", Key.Q);
        Bind("debug_overlay", Key.F3);
        Bind("quest_debug", Key.F4);
        Bind("journal", Key.J);
        Bind("quicksave", Key.F5);
        Bind("quickload", Key.F9);
        Bind("release_mouse", Key.Escape);
        Bind("inventory", Key.Tab, Key.I);
        Bind("dodge", Key.C);
        Bind("use", Key.H);
        Bind("companion_order", Key.G);   // follow or wait (content bible §9: no radial menu)
        for (int i = 0; i < CastKeys.Length; i++)
            Bind(CastKeys[i], Key.Key4 + i);   // the content bible's hotbar: 4 to 6 are the formulas
        for (int n = 1; n <= 9; n++)
            Bind($"reply_{n}", Key.Key1 + n - 1);   // in a conversation, the number keys answer
        foreach (var (action, button) in new[] { ("attack", MouseButton.Left), ("guard", MouseButton.Right) })
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
        }
    }
}
