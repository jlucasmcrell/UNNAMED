// UNNAMED Presentation - Main's input: the key map, and each frame's keys and mouse turned into commands (D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_session?.Simulation is null || _scripted)
            return;
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && !Modal:
                _camera.Look(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when !Modal:
                _camera.Zoom(-0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when !Modal:
                _camera.Zoom(0.35f);
                break;
        }
    }

    /// <summary>
    /// Who has the mouse, decided once a frame from what is open - never set here and there (L-27): the game's while nothing modal is open,
    /// the pointer's otherwise. A harness run and a headless one leave it alone.
    /// </summary>
    private void UpdateMouse()
    {
        if (_scripted || DisplayServer.GetName() == "headless")
            return;
        var wanted = Modal ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (Input.MouseMode != wanted)
            Input.MouseMode = wanted;
    }

    private void ReadInput()
    {
        var combat = _session.Simulation!.Combat;
        bool modal = Modal;
        bool looting = _inventory.Visible && _inventory.Anchor is not null && _inventory.OpenStation is null &&
                       _inventory.OpenTrader is null && !_dialogue.Visible;

        // A conversation's keys (the number keys answer, Escape walks away - M4); then Escape and the screens' own keys (the Field
        // Instrument UI): Escape closes the screen open back to play, and with nothing open it opens the System Menu. A screen's key
        // opens it in place of any other, and closes it again.
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
        else if (Input.IsActionJustPressed("release_mouse"))
        {
            Escape();
        }
        else
        {
            if (Input.IsActionJustPressed("inventory"))
                Toggle(_inventory, () => OpenInventory(null));
            if (Input.IsActionJustPressed("character"))
                Toggle(_character, () => Show(_character));
            if (Input.IsActionJustPressed("journal"))
                Toggle(_journal, () => Show(_journal));
            if (Input.IsActionJustPressed("map"))
                Toggle(_map, () => Show(_map));
            if (Input.IsActionJustPressed("help"))
                Toggle(_help, _help.Toggle);
            if (Input.IsActionJustPressed("saves"))
                Toggle(_saves, () => _saves.OpenInGame());
        }
        if (Input.IsActionJustPressed("take_all") && _inventory.Visible)
            _inventory.TakeAll();
        if (looting && Input.IsActionJustPressed("interact"))
            CloseInventory();
        if (_developer && Input.IsActionJustPressed("debug_overlay"))
            _hud.DebugVisible = _hollow.DebugVisible = !_hud.DebugVisible;
        if (_developer && Input.IsActionJustPressed("quest_debug"))
        {
            _questDebug.Visible = !_questDebug.Visible;
            _questDebug.Refresh(_session, 0, now: true);
        }
        if (modal)
        {
            _autorun = false;
            // Loot stays within reach: walking is allowed, while other panels and gameplay actions remain blocked.
            var lootStick = looting && _inventory.Visible ? new Vector2(
                Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
                Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back")) : Vector2.Zero;
            var lootGait = Input.IsActionPressed("sprint") ? Gait.Sprint : Input.IsActionPressed("walk") ? Gait.Walk : Gait.Run;
            _controller.Steer(_camera, lootStick, lootGait);
            if (combat.Blocking)
                _controller.Guard(false);
            return;
        }

        var stick = new Vector2(
            Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
            Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back"));
        var gait = Input.IsActionPressed("sprint") ? Gait.Sprint : Input.IsActionPressed("walk") ? Gait.Walk : Gait.Run;
        // Autorun: the key turns it on and off, and stepping back turns it off; while on, the character goes forward, still steered by
        // the camera and the side keys, at whatever gait is held.
        if (Input.IsActionJustPressed("autorun"))
        {
            _autorun = !_autorun;
            _hud.Toast(_autorun ? $"Autorun - [{HelpPanel.Key("autorun")}] or a step back stops it" : "Autorun off", 2);
        }
        if (Input.IsActionPressed("move_back"))
            _autorun = false;
        if (_autorun)
            stick = new Vector2(stick.X, 1);

        // Combat: the left button swings or shoots, the right holds a guard (or aims a bow), C dodges, H uses a salve.
        // A swing, a guard and an aimed bow all go where the camera looks.
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
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
        for (int q = 0; q < QuickKeys.Length; q++)
        {
            if (Input.IsActionJustPressed(QuickKeys[q]) && !_controller.UseQuick(q))
                _hud.Toast($"Quick slot {q + 1} is empty - it fills with what you carry to use", 2);
        }

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
                case FocusKind.Waystone:
                    if (_session.Simulation!.Waystones.Any(w => w.Site.Key == focus.Key && w.Known))
                        Show(_waystones, focus.Key);
                    else
                        _controller.TouchStone(focus.Key);
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
        if (Input.IsActionJustPressed("jump"))
            _controller.Jump();
        if (Input.IsActionJustPressed("crouch"))
            _controller.Crouch(_session.Simulation!.Posture.Stance != UNNAMED.Domain.Spatial.Stance.Crouched);
        if (Input.IsActionJustPressed("first_person"))
            _camera.ToggleFirstPerson();
        if (Input.IsActionJustPressed("shoulder_swap"))
            _camera.SwapShoulder();
        if (Input.IsActionJustPressed("quicksave"))
            QuickSave();
        if (Input.IsActionJustPressed("quickload"))
            QuickLoad();
    }

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
        Bind("crouch", Key.X);            // a toggle (the owner's M6 playtest); C stays the dodge
        Bind("character", Key.K);
        Bind("help", Key.F1);
        Bind("take_all", Key.R);          // in a container's panel
        Bind("first_person", Key.V);
        Bind("shoulder_swap", Key.Q);
        Bind("debug_overlay", Key.F3);
        Bind("quest_debug", Key.F4);
        Bind("journal", Key.J);
        Bind("map", Key.M);
        Bind("autorun", Key.Quoteleft);   // the owner's second-play brief §23: autorun on tilde
        Bind("quicksave", Key.F5);
        Bind("quickload", Key.F9);
        Bind("saves", Key.L);
        Bind("release_mouse", Key.Escape);
        Bind("inventory", Key.Tab, Key.I);
        Bind("dodge", Key.C);
        Bind("use", Key.H);
        Bind("companion_order", Key.G);   // follow or wait (content bible §9: no radial menu)
        for (int i = 0; i < CastKeys.Length; i++)
            Bind(CastKeys[i], Key.Key4 + i);   // the content bible's hotbar: 4 to 6 are the formulas
        for (int n = 1; n <= 9; n++)
            Bind($"reply_{n}", Key.Key1 + n - 1);   // in a conversation, the number keys answer
        for (int i = 0; i < QuickKeys.Length; i++)
            Bind(QuickKeys[i], Key.Key1 + i);   // outside one, 1 to 3 are the quick slots (the owner's first playtest)
        foreach (var (action, button) in new[] { ("attack", MouseButton.Left), ("guard", MouseButton.Right) })
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
        }
    }
}
