// UNNAMED Presentation - boot, the frame loop, and input (ARCHITECTURE.md §2, §8.1; D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Combat;
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
/// <c>--perf [--perf-out dir] [--perf-seconds n]</c> (the performance capture), <c>--spike</c> (the 2x2 km greybox).
/// </summary>
public partial class Main : Node3D
{
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
    private InventoryPanel _inventory = null!;
    private FrameStats? _stats;
    private PerfRun? _perf;
    private Smoke? _smoke;
    private UiShots? _shots;
    private string _perfOut = string.Empty;
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
        string profile = _flags.Contains("--smoke") || _flags.Contains("--perf") || _options.ContainsKey("--ui-shots")
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
        _session.NewGame("Wanderer");
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
        _inventory = new InventoryPanel { Name = "Inventory" };
        _inventory.Bind(_session);
        AddChild(_inventory);
        _controller = new PlayerController(_session);
        Subscribe();
        Resync();
        DefineInput();

        if (_flags.Contains("--smoke"))
        {
            _smoke = new Smoke(_session, _controller, _camera, profile);
        }
        else if (_options.TryGetValue("--ui-shots", out string? shots))
        {
            _shots = new UiShots(_session, _controller, _camera, _inventory, shots);
        }
        else if (_flags.Contains("--perf"))
        {
            _perfOut = _options.GetValueOrDefault("--perf-out", DefaultPerfOut("prototype"));
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);   // measure the headroom, not the refresh rate
            _stats = new FrameStats(GetViewport());
            _perf = new PerfRun(Seconds());
            _perf.SpawnProxies(this, _session.Setup.Layout);
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
                    GetTree().Quit(1);
                    return;
                case { } shot:
                    SaveScreenshot(_shots.Directory, shot);
                    break;
            }
        }
        else
        {
            ReadInput();
        }
        KeepContainerInReach();

        // The smoke runs one tick per frame, so it finishes in a fraction of real time.
        var frame = _session.Frame(_smoke is not null ? _session.TickSeconds : delta);
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
        if (_session?.Simulation is null || _perf is not null || _smoke is not null || _shots is not null)
            return;
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && !_inventory.Visible:
                _camera.Look(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _camera.Zoom(-0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _camera.Zoom(0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Input.MouseMode != Input.MouseModeEnum.Captured && !_inventory.Visible:
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
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured && !_inventory.Visible;
        bool holding = captured && Input.IsActionPressed("guard");
        bool swing = captured && Input.IsActionJustPressed("attack");
        _controller.Steer(_camera, stick, gait, faceCamera: holding || swing || combat.Phase == CombatPhase.Windup);
        if (swing)
            _controller.Attack();
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
            }
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
        if (Input.IsActionJustPressed("quicksave"))
            QuickSave();
        if (Input.IsActionJustPressed("quickload"))
            QuickLoad();
        if (Input.IsActionJustPressed("release_mouse"))
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>A container stays open only while it is in reach; one that is gone (an emptied corpse) closes its column.</summary>
    private void KeepContainerInReach()
    {
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
            { } item => $"[E] Pick up {_session.DisplayName(item.DefId)}",
        });

        var view = simulation.Player;
        var stats = view.Stats;
        var pools = view.Progression.Pools;
        _hud.SetStatus(
            $"{view.Name}   Level {view.Progression.Level}   XP {view.Progression.LevelProgressXp}/{_session.Setup.Progression.Curve.ToReach(view.Progression.Level + 1)}" +
            (view.Progression.XpDebt > 0 ? $"   debt {view.Progression.XpDebt}" : "") +
            $"\nHealth {combat.Health}/{combat.MaxHealth}   Stamina {combat.Stamina}/{combat.MaxStamina}" +
            $"   Focus {pools.Focus ?? stats.FocusMax}/{stats.FocusMax}   Strain {pools.Strain}/{stats.StrainTolerance}" +
            $"\n{_session.DisplayName(combat.Weapon.Source)}{(combat.Blocking ? " (guarding)" : "")}   Coin {view.Currency}   Armor {view.Armor}" +
            $"   Carrying {view.CarriedGrams / 1000.0:0.#}/{view.CarryLimitGrams / 1000.0:0.#} kg   [Tab] inventory");
        _hud.SetVitals(combat.Health, combat.MaxHealth, combat.Stamina, combat.MaxStamina);
        _hud.SetEffects(string.Join("   ", combat.Effects.Select(e =>
            $"{_session.DisplayName(e.EffectId)}{(e.Stacks > 1 ? $" x{e.Stacks}" : "")} {Math.Max(0, e.ExpiresTick - simulation.WorldTick) * _session.TickSeconds:0}s")));
        if (Target(simulation) is { } target)
            _hud.SetTarget(_session.DisplayName(target.DefId), target.Health, target.MaxHealth);
        else
            _hud.SetTarget(null, 0, 0);

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
        _session.Subscribe<LocationDiscovered>(e => _hud.Toast($"Discovered: {_session.DisplayName(e.LocationId)}"));
        _session.Subscribe<ExperienceGained>(e => _hud.Toast(e.LevelsGained > 0 ? $"+{e.Awarded} XP - level {e.Level}!" : $"+{e.Awarded} XP", 3));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is InteractCommand or MoveItemCommand or EquipCommand or UnequipCommand)
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
        SubscribeCombat();
    }

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
            if (e.Target == _session.Simulation!.PlayerId)
                _hud.Log($"{Name(e.Source)}: {(e.Delta > 0 ? "+" : "")}{e.Delta}");
        });
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
        _session.Subscribe<ItemUsed>(e => _hud.Toast($"Used {Name(e.DefId)}", 2));
        _session.Subscribe<ItemConsumed>(_ => _inventory.Refresh());
        _session.Subscribe<PlayerDied>(e =>
        {
            string recap = string.Join("\n", e.Recap.Select(r =>
                r.AttackerDefId == r.Source ? $"{Name(r.Source)}: {r.Damage}" : $"{Name(r.AttackerDefId)}, {Name(r.Source).ToLowerInvariant()}: {r.Damage}"));
            string killer = e.KillerDefId == e.Cause ? Name(e.Cause) : $"the {Name(e.KillerDefId)} ({Name(e.Cause).ToLowerInvariant()})";
            _hud.ShowDeath($"You died - killed by {killer}.\n\nThe last blows:\n{recap}\n\nXP debt +{e.DebtAdded} (nothing earned is lost). " +
                           "You wake at the outpost, weakened for a minute.");
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is AttackCommand or DodgeCommand or UseItemCommand && !e.Reason.StartsWith("already", StringComparison.Ordinal)
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
        int length = combat.Phase switch
        {
            CombatPhase.Windup => weapon.WindupTicks,
            CombatPhase.Active => weapon.ActiveTicks,
            CombatPhase.Recovery => combat.AttackSource is null ? constants.DodgeRecoveryTicks : weapon.RecoveryTicks,
            CombatPhase.Dodge => constants.DodgeTicks,
            CombatPhase.Staggered => constants.StaggerTicks,
            _ => 1,
        };
        float progress = (float)((length - combat.PhaseTicksLeft + alpha) / Math.Max(1, length));
        var held = weapon.Ranged ? Held.Bow : weapon.Source == "unarmed" ? Held.Nothing : Held.Sword;
        return new CombatStance(combat.Phase, progress, held, combat.Blocking);
    }

    /// <summary>After a new game or a load: the only moments presentation copies the whole state.</summary>
    private void Resync()
    {
        _controller.Resync();
        _creatures.Reset();
        foreach (var door in _session.Simulation!.Doors)
            _hollow.SetDoor(door.Site.Key, door.Open);
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
        };
        string summary = _stats!.Write(_perfOut, notes);
        GD.Print($"UNNAMED perf capture written to {_perfOut}\n{summary}");
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

    private void CloseInventory()
    {
        _inventory.Close();
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>A container read as words; a corpse is named for the creature it was.</summary>
    internal static string Describe(GameSession session, string key) =>
        session.Simulation?.Creatures.FirstOrDefault(c => c.CorpseKey == key) is { } dead ? $"{session.DisplayName(dead.DefId)} remains" : Describe(key);

    /// <summary>A door or container key read as words: <c>door.forge_shed</c> is the forge shed door.</summary>
    internal static string Describe(string key) => key switch
    {
        _ when key.StartsWith("door.", StringComparison.Ordinal) => key["door.".Length..].Replace('_', ' ') + " door",
        _ when key.StartsWith("container.", StringComparison.Ordinal) => key["container.".Length..].Replace('_', ' '),
        _ => key,
    };

    private void ParseArguments(string[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is "--perf-out" or "--perf-seconds" or "--ui-shots" && i + 1 < arguments.Length)
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
        Bind("quicksave", Key.F5);
        Bind("quickload", Key.F9);
        Bind("release_mouse", Key.Escape);
        Bind("inventory", Key.Tab, Key.I);
        Bind("dodge", Key.C);
        Bind("use", Key.H);
        foreach (var (action, button) in new[] { ("attack", MouseButton.Left), ("guard", MouseButton.Right) })
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
        }
    }
}
