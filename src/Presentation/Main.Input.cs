// UNNAMED Presentation - Main's input: the key map, and each frame's keys and mouse turned into commands (D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_session?.Simulation is null || _scripted || _mhReviewDirectory is not null)
            return;
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && !Modal:
                _camera.Look(motion.Relative);
                break;
            // The scroll modifier (Alt) and the wheel page the main bar instead of zooming.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when !Modal && Input.IsActionPressed("bar_scroll_modifier"):
                _barPage = ActionBarModel.Page(_barPage, -1);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when !Modal && Input.IsActionPressed("bar_scroll_modifier"):
                _barPage = ActionBarModel.Page(_barPage, 1);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when !Modal && Input.MouseMode == Input.MouseModeEnum.Captured:
                _camera.Zoom(-0.35f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when !Modal && Input.MouseMode == Input.MouseModeEnum.Captured:
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
        if (_mhReviewDirectory is not null)
        {
            if (Input.MouseMode == Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Visible;
            return;
        }
        if (_scripted || DisplayServer.GetName() == "headless")
            return;
        var wanted = Modal || Input.IsActionPressed("free_cursor") ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (Input.MouseMode != wanted)
            Input.MouseMode = wanted;
    }

    /// <summary>The toggleable actions pressed on (Settings, Controls: hold or toggle), until pressed again.</summary>
    private readonly Dictionary<string, bool> _toggled = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether an action that can be held or toggled is on this frame: held down, or - set to press on, press off - pressed once and not
    /// yet pressed again. Call it once a frame for each action, since a toggle reads the press.
    /// </summary>
    private bool Engaged(string action)
    {
        if (!SettingsStore.Current.Profile.Controls.IsToggle(action))
            return Input.IsActionPressed(action);
        if (Input.IsActionJustPressed(action))
            _toggled[action] = !_toggled.GetValueOrDefault(action);
        return _toggled.GetValueOrDefault(action);
    }

    /// <summary>The gait the keys ask for: sprint over walk over the run.</summary>
    private Gait ReadGait()
    {
        bool sprint = Engaged("sprint"), walk = Engaged("walk");
        return sprint ? Gait.Sprint : walk ? Gait.Walk : Gait.Run;
    }

    /// <summary>Before a world runs (the title and the screens it opens): only Escape, back to the title.</summary>
    private void ReadFrontInput()
    {
        if (Input.IsActionJustPressed("release_mouse"))
            FrontEscape();
    }

    private void ReadInput()
    {
        // A key being bound in Settings is the binding: it opens no screen and moves no one.
        if (_settings.Capturing || _rcd3Driving)
            return;
        var combat = _session.Simulation!.Combat;
        bool modal = Modal;
        bool looting = _loot.Visible || _inventory.Visible && _inventory.Anchor is not null && _inventory.OpenStation is null &&
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
            _dialogue.Skip(Input.IsActionPressed("dialogue_skip"), Input.IsActionJustPressed("dialogue_skip"), GetProcessDeltaTime());
            if (Input.IsActionJustPressed("release_mouse"))
                _dialogue.Leave();
        }
        else if (Input.IsActionJustPressed("release_mouse"))
        {
            Escape();
        }
        else if (!_journal.Typing)   // a note being written takes its letters, not the screens' keys (WP4)
        {
            if (Input.IsActionJustPressed("inventory"))
                Toggle(_inventory, () => OpenInventory(null));
            if (Input.IsActionJustPressed("character"))
                Toggle(_character, () => Show(_character));
            if (Input.IsActionJustPressed("skills"))
                Toggle(_skills, () => ShowSkills(null));
            if (Input.IsActionJustPressed("journal"))
            {
                // A landmark banner still standing offers its page: the journal opens on it (spec 41 §2).
                string? offered = (_hud.ActiveLandmarkTitle is { } title
                    ? _session.Setup.Lore.FirstOrDefault(entry => entry.Value.Title == title).Key
                    : null) ?? _loreOffered;
                if (offered is not null)
                {
                    _journal.OpenLore(offered);
                    _loreOffered = null;
                }
                Toggle(_journal, () => Show(_journal));
            }
            if (Input.IsActionJustPressed("map"))
                Toggle(_map, () => Show(_map));
            if (Input.IsActionJustPressed("chat"))
                Toggle(_chat, () => _chat.Open());
            if (Input.IsActionJustPressed("party"))
                Toggle(_companionsPanel, () => ShowParty(null));
            if (Input.IsActionJustPressed("help"))
                Toggle(_help, _help.Toggle);
            if (Input.IsActionJustPressed("saves"))
                Toggle(_saves, () => _saves.OpenInGame());
        }
        if (Input.IsActionJustPressed("take_all") && _loot.Visible)
            _loot.TakeAll();
        else if (Input.IsActionJustPressed("take_all") && _inventory.Visible)
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
            var lootStick = looting ? new Vector2(
                Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
                Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back")) : Vector2.Zero;
            _controller.Steer(_camera, lootStick, ReadGait());
            if (combat.Blocking)
                _controller.Guard(false);
            return;
        }

        // ENTERING OR LEAVING A SPACE STOPS IT. A portal puts the body down somewhere it did not walk to, and a
        // character who keeps going from there walks into whatever happens to be in front of them - the far room's
        // wall on the way in, or straight back through the door on the way out (M8 D10).
        bool outside = _session.Simulation?.PlayerInExterior ?? true;
        if (outside != _wasOutside)
        {
            _wasOutside = outside;
            // The scene follows the body: the ring is shown inside and the frontier's world hidden, and back on the
            // way out (review 3, B6 - nothing used to react to a space change at all).
            ShowSpace(outside);
            if (_autorun)
            {
                _autorun = false;
                _hud.Toast("Autorun off - you went through a door", 2);
            }
        }

        var stick = new Vector2(
            Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
            Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back"));
        var gait = ReadGait();
        if (HeldByTheOpening(stick) || _rope is not null)
        {
            _controller.Steer(_camera, Vector2.Zero, gait);
            return;
        }
        // A sprint pressed on ends where the running does.
        if (stick == Vector2.Zero && !_autorun)
            _toggled["sprint"] = false;
        // Auto-run (the owner, 2026-10-18): the key starts it; forward or back, or the key again, cancels it; strafing and the camera leave it
        // running, and the gait held (Shift) still applies. Death or downing, a conversation and a blocked way stop it (AutoRunRules).
        var at = _controller.Authoritative;
        double moved = _autoRunFrom is { } from ? Math.Sqrt(Math.Pow(at.XMm - from.X, 2) + Math.Pow(at.ZMm - from.Z, 2)) / 1000.0 : 0;
        _autoRunFrom = (at.XMm, at.ZMm);
        bool wasOn = _autoRun.On;
        string? stopped = _autoRun.Step(new Ui.Models.AutoRunFrame(Input.IsActionJustPressed("auto_run"), Input.IsActionJustPressed("move_forward"),
            Input.IsActionPressed("move_back"), _session.Simulation!.Combat.Defeated, false, GetProcessDeltaTime(), moved));
        if (stopped is not null)
            _hud.Toast(stopped, 2);
        else if (_autoRun.On && !wasOn)
            _hud.Toast($"Autorun - [{HelpPanel.Key("auto_run")}], forward or back stops it", 2);
        if (_autorun)
            stick = new Vector2(stick.X, 1);

        // Combat: the left button swings or shoots, the right holds a guard (or aims a bow), C dodges, H uses a salve.
        // A swing, a guard and an aimed bow all go where the camera looks.
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        bool holding = captured && Engaged("guard");
        bool swing = captured && Input.IsActionJustPressed("attack");
        // The quick bar (WP9): a slot's key uses what is on it - an item, or a formula or technique worked where the camera looks, like a swing (M3e).
        // The page keys first: the page modifier held with 1 to 6 pages the main bar, and the digit is then not a slot's.
        int page = Input.IsActionPressed("bar_modifier") ? Array.FindIndex(BarPageKeys, key => Input.IsActionJustPressed(key)) : -1;
        if (page >= 0)
            _barPage = page;
        int slot = page >= 0 ? -1 : Array.FindIndex(QuickKeys, key => Input.IsActionJustPressed(key));
        string? quick = slot >= 0 ? _session.Simulation!.ActionBars[_barPage][slot] : null;
        int gear = Array.FindIndex(GearSetKeys, key => Input.IsActionJustPressed(key));
        if (gear >= 0)
            _session.Submit(new SwapGearSetCommand(_session.Simulation!.PlayerId, gear + 1));
        bool working = captured && quick is not null && (_session.Setup.Magic.Formulas.ContainsKey(quick) || _session.Setup.Techniques.ContainsKey(quick));
        _controller.Steer(_camera, stick, gait, faceCamera: _avatar is MHCharacters.MetaHumanFigure || VisualOptions.ComparisonCast is not null || holding || swing || working || combat.Phase == CombatPhase.Windup);
        if (swing)
            _controller.Attack();
        if (slot >= 0)
            UseQuickSlot(slot, quick, captured);
        bool guard = holding && !combat.Weapon.Ranged;
        if (guard != combat.Blocking && (!guard || combat.Phase == CombatPhase.Idle))
            _controller.Guard(guard);
        if (captured && Input.IsActionJustPressed("dodge"))
        {
            var wish = stick.LengthSquared() > 0.01f ? stick.Normalized() : Vector2.Zero;
            _controller.Dodge(_camera.GroundForward * wish.Y + _camera.GroundRight * wish.X);
        }
        if (captured && Input.IsActionJustPressed("takedown"))
            _controller.Takedown();
        if (Input.IsActionJustPressed("use") && !_controller.UseConsumable())
            _hud.Toast("Nothing to use", 2);

        if (Input.IsActionJustPressed("interact") && _controller.FocusOn(_camera) is { } focus)
        {
            switch (focus.Kind)
            {
                case FocusKind.Door:
                case FocusKind.Portal:
                    _controller.Interact(focus.Key);
                    break;
                case FocusKind.Container when _session.Simulation!.ActiveLayout.Containers.Any(c => PlayerController.IsPickup(c) && c.Key == focus.Key):
                    _session.Submit(new TakeAllCommand(_session.Simulation!.PlayerId, focus.Key));
                    _avatar.Interact();
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
                    Approach(focus.Key);
                    break;
                case FocusKind.Switch:
                case FocusKind.Readable:
                    _controller.Interact(focus.Key);
                    break;
                case FocusKind.Waystone:
                    if (_session.Simulation!.Waystones.Any(w => w.Site.Key == focus.Key && w.Known))
                    {
                        WaystoneResponse(focus.Key, true);
                        _soundEvents.WaystoneUse(focus.Key);
                        Show(_waystones, focus.Key);
                    }
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
        // The animals' orders (spec 43 ruling 4): a key opens the six, a number gives one, and the ground marker shows the place the
        // ground-named ones would use. It comes before the quick slots, so a number means the order while the list is up.
        if (_petOrdersOpen)
            ReadPetOrderKeys();
        else if (Input.IsActionJustPressed("pet_orders"))
            OpenPetOrders();
        if (Input.IsActionJustPressed("jump"))
            _controller.Jump();
        // Crouch presses on and off by default (the owner's M6 playtest); held, the body stays down only while the key is.
        bool crouched = _session.Simulation!.Posture.Stance == UNNAMED.Domain.Spatial.Stance.Crouched;
        if (SettingsStore.Current.Profile.Controls.IsToggle("crouch") ? Input.IsActionJustPressed("crouch") : Input.IsActionPressed("crouch") != crouched)
            _controller.Crouch(!crouched);
        if (Input.IsActionJustPressed("first_person"))
            _camera.ToggleFirstPerson();
        if (Input.IsActionJustPressed("shoulder_swap"))
            _camera.SwapShoulder();
        if (Input.IsActionJustPressed("quicksave"))
            QuickSave();
    }

    /// <summary>
    /// What a quick slot's key does: an item is used from its first carried stack, a formula or technique worked (only with the mouse in
    /// the game, so it goes where the camera looks), and an empty slot or an item none of which is left says why nothing happened.
    /// </summary>
    private void UseQuickSlot(int slot, string? id, bool captured)
    {
        if (id is null)
            _hud.Toast($"Slot {HelpPanel.Key(QuickKeys[slot])} on bar {_barPage + 1} is empty - put something on it from Skills [{HelpPanel.Key("skills")}] or the inventory", 3);
        else if (UNNAMED.Domain.Progression.QuickBar.TryGearSet(id, out int set))
            _session.Submit(new SwapGearSetCommand(_session.Simulation!.PlayerId, set));
        else if (_session.Setup.Magic.Formulas.ContainsKey(id))
        {
            if (captured)
                _controller.Cast(id);
        }
        else if (_session.Setup.Techniques.ContainsKey(id))
        {
            if (captured)
                _controller.Technique(id);
        }
        else if (id.StartsWith("item.", StringComparison.Ordinal) && !_controller.UseItem(id))
            _hud.Toast($"No {Names.Of(_session, id)} left", 2);
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
        Bind("crouch", Key.X);            // a toggle by default (the owner's M6 playtest; Settings can make it a hold); C stays the dodge
        Bind("character", Key.K);
        Bind("skills", Key.P);
        Bind("help", Key.F1);
        Bind("take_all", Key.R);          // in a container's panel
        Bind("first_person", Key.V);
        Bind("shoulder_swap", Key.Q);
        Bind("debug_overlay", Key.F3);
        Bind("quest_debug", Key.F4);
        Bind("journal", Key.J);
        Bind("map", Key.M);
        Bind("chat", Key.T);
        Bind("party", Key.O);
        Bind("auto_run", Key.Quoteleft);   // the owner's brief Â§23 and ruling 2026-10-18: auto-run on the tilde key
        Bind("quicksave", Key.F5);
        Bind("saves", Key.L);
        Bind("release_mouse", Key.Escape);
        Bind("free_cursor", Key.Z); // Hold only; Alt remains action-bar scrolling.
        Bind("dialogue_skip", Key.Space, Key.Enter);   // in a conversation: silence the line and go on (held: fast-forward); Space jumps in play
        Bind("inventory", Key.Tab, Key.I);
        Bind("dodge", Key.C);
        Bind("takedown", Key.F);          // a knife into a body that has not noticed the character (STEALTH Â§12)
        Bind("use", Key.H);
        Bind("companion_order", Key.G);   // follow or wait (content bible §9: no radial menu)
        Bind("pet_orders", Key.Y);        // the animals' six orders, with the ground marker the ground-named ones use (spec 43 ruling 4)
        for (int n = 1; n <= 9; n++)
            Bind($"reply_{n}", Key.Key1 + n - 1);   // in a conversation, the number keys answer
        // Outside a conversation the digits are the main bar's slots (WP9; PJ09): 1 to 9, 0, - and =.
        Key[] slotKeys = { Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9, Key.Key0, Key.Minus, Key.Equal };
        for (int i = 0; i < QuickKeys.Length; i++)
            Bind(QuickKeys[i], slotKeys[i]);
        // Ctrl and 1 to 6 page the bar (Shift stays sprint only); Alt and the wheel do the same, and so do the main bar's own arrows. Gear sets have no default key.
        for (int i = 0; i < BarPageKeys.Length; i++)
            Bind(BarPageKeys[i], Key.Key1 + i);
        Bind("bar_modifier", Key.Ctrl);   // Ctrl and 1 to 6 page the main bar (the owner, 2026-10-18); Shift stays sprint only
        Bind("bar_scroll_modifier", Key.Alt);
        foreach (string action in GearSetKeys)
            Bind(action);
        foreach (var (action, button) in new[] { ("attack", MouseButton.Left), ("guard", MouseButton.Right) })
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
        }
    }
}

