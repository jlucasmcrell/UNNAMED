// UNNAMED Presentation - Main's HUD: what it shows each frame, from the simulation's state (D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using System.Collections.Immutable;
using Godot;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>The HUD and the open panels, from the frame's state.</summary>
    private void DrawHud(Simulation simulation, CombatView combat, Posture posture, double alpha, double delta)
    {
        _hud.SetHeading(PlayerController.FacingOf(_camera.GroundForward) / 1000f);
        bool aiming = Aim(simulation, combat);
        _hud.SetCrosshair(_camera.IsFirstPerson && !aiming);

        var focus = _controller.FocusOn(_camera);
        GuidanceFrame(simulation, combat, focus is not null);
        DrawOutlines(simulation, focus);
        DrawChatFeed(simulation);
        _hud.SetConversing(_dialogue.Visible);
        // No prompt under a screen: nothing behind a sheet can be worked, and the hint line sits where the prompt was.
        _hud.SetPrompt(Screen is not null ? null : focus switch
        {
            null => null,
            { Kind: FocusKind.Door } door when _controller.IsOpen(door.Key)
                && simulation.ActiveLayout.Doors.Any(d => d.Key == door.Key && d.StaysOpen)
                => $"The {Names.Place(_session, door.Key, door.DefId)} is latched open.",
            { Kind: FocusKind.Door } door => $"[{HelpPanel.Key("interact")}] {(_controller.IsOpen(door.Key) ? "Close" : "Open")} the {Names.Place(_session, door.Key, door.DefId)}",
            { Kind: FocusKind.Container } container => container.DefId == container.Key
                ? $"[{HelpPanel.Key("interact")}] Open the {Names.Place(_session, container.Key, container.DefId)}"
                : $"[{HelpPanel.Key("interact")}] Search the {Names.Of(_session, container.DefId)}",
            { Kind: FocusKind.Node } node => NodePrompt(simulation, node),
            { Kind: FocusKind.Station } station => $"[{HelpPanel.Key("interact")}] Work at the {Names.Place(_session, station.Key, station.DefId)}",
            { Kind: FocusKind.Npc } npc when DownedCompanion(npc.Key) is not null => $"[{HelpPanel.Key("interact")}] {ContextModel.Prompt(Facts(simulation, npc.Key))}",
            { Kind: FocusKind.Npc } npc => simulation.Conversation?.NpcId == npc.Key ? null : $"[{HelpPanel.Key("interact")}] {ContextModel.Prompt(Facts(simulation, npc.Key))}",
            { Kind: FocusKind.Switch } site => simulation.ActiveLayout.FindSwitch(site.Key) is { } s ? $"[{HelpPanel.Key("interact")}] {s.Verb} the {s.Name}" : null,
            { Kind: FocusKind.Portal } portal => $"[{HelpPanel.Key("interact")}] {(simulation.PlayerInExterior ? "Enter the Barrowworks" : "Return to the frontier")}",
            { Kind: FocusKind.Waystone } stone => simulation.Waystones.First(w => w.Site.Key == stone.Key) is var w && w.Known
                ? $"[{HelpPanel.Key("interact")}] The {w.Site.Name}: Waystones you know"
                : $"[{HelpPanel.Key("interact")}] Touch the stone",
            { Kind: FocusKind.Barrier } barrier => simulation.ActiveLayout.Barriers.First(b => b.Key == barrier.Key).Prompt,
            { } item => $"[{HelpPanel.Key("interact")}] Pick up {Names.Item(_session, item.DefId, item.Quality)}",
        }, focus is null || focus.Kind == FocusKind.Npc && simulation.Conversation?.NpcId == focus.Key ? null : Context(simulation, focus));

        var view = simulation.Player;
        var stats = view.Stats;
        var pools = view.Progression.Pools;
        _hud.SetStatus(
            $"{view.Name}   Level {view.Progression.Level}" +
            (view.Progression.Level >= _session.Setup.Progression.LevelCap ? " (the highest)"
                : $"   XP {view.Progression.LevelProgressXp}/{_session.Setup.Progression.Curve.ToReach(view.Progression.Level + 1)}") +
            (view.Progression.XpDebt > 0 ? $"   debt {view.Progression.XpDebt}" : "") +
            (view.Progression.UnspentAttributePoints > 0 ? $"   [{HelpPanel.Key("character")}] a point to spend" : "") +
            (posture.Stance == UNNAMED.Domain.Spatial.Stance.Crouched ? "   Crouched" : "") +
            $"\nHealth {combat.Health}/{combat.MaxHealth}   Stamina {combat.Stamina}/{combat.MaxStamina}" +
            $"   Focus {pools.Focus ?? stats.FocusMax}/{stats.FocusMax}   Strain {pools.Strain}/{stats.StrainTolerance}   Resonance {stats.Resonance}" +
            $"\n{Names.Item(_session, combat.Weapon.Source, Wielded(view)?.Quality ?? 0)}{(combat.Blocking ? " (guarding)" : "")}   Coin {view.Currency}   Armor {view.Armor}" +
            $"   Carrying {view.CarriedGrams / 1000.0:0.#}/{view.CarryLimitGrams / 1000.0:0.#} kg   [{HelpPanel.Key("inventory")}] inventory");
        _hud.SetVitals(combat.Health, combat.MaxHealth, combat.Stamina, combat.MaxStamina);
        _hud.SetMagicPools(combat.Focus, combat.MaxFocus, combat.Strain, combat.StrainTolerance, combat.Strained);
        var bar = simulation.QuickBar;
        var onBar = Enumerable.Range(0, bar.Length).Where(i => bar[i] is { } id && _session.Setup.Magic.Formulas.ContainsKey(id)).ToList();
        _hud.SetMagic((combat.Casting is { } casting ? $"Casting {Names.Of(_session, casting)}\n" : "") +
            string.Join("   ", onBar.Select(i => $"[{HelpPanel.Key(QuickKeys[i])}] {Names.Of(_session, bar[i]!)} {_session.Setup.Magic.Formulas[bar[i]!].FocusCost}F")) +
            (combat.Strained ? "   Strained" : ""), onBar.Select(i => bar[i]!).ToList());
        _hud.SetEffects(string.Join("   ", combat.Effects.Select(e =>
            $"{Names.Of(_session, e.EffectId)}{(e.Stacks > 1 ? $" x{e.Stacks}" : "")} {Math.Max(0, e.ExpiresTick - simulation.WorldTick) * _session.TickSeconds:0}s")),
            combat.Effects.Select(e => e.EffectId).Concat(combat.Strained ? new[] { "strained" } : Array.Empty<string>()).ToList());
        bool capped = view.Progression.Level >= _session.Setup.Progression.LevelCap;
        _hud.SetSheet(new HudSheet(view.Name, view.Progression.Level, capped, view.Progression.LevelProgressXp,
            capped ? 0 : _session.Setup.Progression.Curve.ToReach(view.Progression.Level + 1), view.Progression.XpDebt,
            view.Progression.UnspentAttributePoints > 0 ? HelpPanel.Key("character") : null, posture.Stance == UNNAMED.Domain.Spatial.Stance.Crouched,
            pools.Focus ?? stats.FocusMax, stats.FocusMax, pools.Strain, stats.StrainTolerance, stats.Resonance,
            Names.Item(_session, combat.Weapon.Source, Wielded(view)?.Quality ?? 0), combat.Weapon.Source, combat.Blocking, view.Currency, view.Armor,
            view.CarriedGrams / 1000.0, view.CarryLimitGrams / 1000.0, HelpPanel.Key("inventory"),
            combat.Casting is { } worked ? Names.Of(_session, worked) : null, combat.Strained));
        DrawActionBars(simulation, combat.Casting);
        if (Target(simulation) is { } target)
            _hud.SetTarget(Names.Of(_session, target.DefId), target.Health, target.MaxHealth);
        else
            _hud.SetTarget(null, 0, 0);
        var quests = simulation.Quests;
        string? tracked = simulation.TrackedQuest(_journal.Tracked);
        _hud.SetTracker(JournalPanel.Tracker(quests, tracked));
        _hud.SetMarker(Bearing(tracked is null ? null : simulation.Marker(tracked)));
        _hud.SetCompanions(string.Join("\n", simulation.Companions.Select(c =>
            $"{c.Name} - {c.Doing}, {c.Standing}" +
            (c.FallsAtTick is { } falls ? $" - falls in {Math.Max(0, falls - simulation.WorldTick) * _session.TickSeconds:0} s unless helped up" : "") +
            (c.Condition == CompanionCondition.Up ? $"   [{HelpPanel.Key("companion_order")}] {(c.Order == CompanionOrder.Follow ? "wait" : "follow")}" : ""))),
            simulation.Companions.Select(c => c.Condition != CompanionCondition.Up ? "downed" : c.Order == CompanionOrder.Follow ? "follow" : "wait").FirstOrDefault());
        _journal.Refresh(_session);
        _questDebug.Refresh(_session, delta);
        _character.Refresh();
        _skills.Refresh();

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
                "\ndiscovered:\n" + string.Join("\n", view.Discoveries.Select(d => $"  {Names.Of(_session, d.LocationId)} (tick {d.Tick})")));
        }
    }

    /// <summary>The compass's mark (WP4): the bearing from the character to the tracked beat, hidden once they stand at it.</summary>
    private float? Bearing(QuestMarker? marker)
    {
        if (marker is null)
            return null;
        var body = _controller.Authoritative;
        double dx = marker.XMm - body.XMm, dz = marker.ZMm - body.ZMm;
        return dx * dx + dz * dz < 3_000.0 * 3_000.0 ? null : (float)(Math.Atan2(dx, dz) * 180 / Math.PI);
    }

    /// <summary>
    /// The aiming reticle (the owner's M6 playtest): while a bow is drawn or held aimed, or a thrown working's tell runs - and always in
    /// first person with a bow - a ring on the point the simulation says the shot would stop at, projected from the shoulder's height.
    /// With the debug overlay on, a line from the bow to that point; never otherwise. True while aiming.
    /// </summary>
    private bool Aim(Simulation simulation, CombatView combat)
    {
        long range = 0;
        bool free = Input.MouseMode == Input.MouseModeEnum.Captured && !Modal;
        if (combat.Casting is { } casting && _session.Setup.Magic.Formulas.TryGetValue(casting, out var formula) && formula.Targeting == UNNAMED.Domain.Magic.Targeting.Projectile
            && combat.Phase is CombatPhase.Windup)
            range = formula.Blow!.ReachMm;
        else if (combat.Weapon.Ranged && (combat.Phase is CombatPhase.Windup || _camera.IsFirstPerson || free && Input.IsActionPressed("guard")))
            range = combat.Weapon.ReachMm;
        if (range == 0)
        {
            _hud.SetReticle(null, false);
            _projectiles.ShowAimLine(null, null);
            return false;
        }
        var body = simulation.Player.Body;
        var terrain = _session.Setup.Layout.Space.Terrain;
        // Drawn, the shot goes along the body's own facing - every turn of the camera is sent, and the release holds the facing of the
        // last tick of the draw - so the ring shows where it will stop (the Phase-1 technical audit, L-13). Not yet drawn, the camera's.
        int facing = combat.Phase == CombatPhase.Windup ? body.FacingMdeg : PlayerController.FacingOf(_camera.GroundForward);
        var (x, z, onCreature) = simulation.Aim(facing, range);
        float height = onCreature ? 0.7f : 1.4f;
        var point = new Vector3(x / 1000f, terrain.HeightAtMm(x, z) / 1000f + height, z / 1000f);
        var camera = _camera.Camera;
        _hud.SetReticle(camera.IsPositionBehind(point) ? null : camera.UnprojectPosition(point), onCreature);
        _projectiles.ShowAimLine(_hud.DebugVisible ? new Vector3(body.XMm / 1000f, body.YMm / 1000f + 1.4f, body.ZMm / 1000f) : null, _hud.DebugVisible ? point : null);
        return true;
    }

    /// <summary>A node's prompt: what it gives now, or why it gives nothing.</summary>
    private string NodePrompt(Simulation simulation, Focus focus)
    {
        string name = Names.Of(_session, focus.DefId);
        if (simulation.Nodes.FirstOrDefault(n => n.Key == focus.Key) is { Ready: true })
            return $"[{HelpPanel.Key("interact")}] Gather from the {name}";
        return _session.Setup.Crafting.Nodes[focus.DefId].Respawn == Respawn.None
            ? $"The {name} is worked out"
            : $"The {name} has nothing to take until it grows back";
    }

    /// <summary>The carried entry in the main hand, if any.</summary>
    private static InventoryEntry? Wielded(PlayerView view) =>
        view.Equipment.TryGetValue(EquipSlot.MainHand, out var id) ? view.Inventory.FirstOrDefault(e => e.ItemId == id) : null;

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

    /// <summary>
    /// The action bars (WP9; the owner's request for bars that never cap what a character can use): the page the main row shows, and every
    /// bar the player has shown, each slot as the one availability evaluator reads it - a formula's Focus, a technique's Stamina and wait, an
    /// item's count, a gear set's missing pieces.
    /// </summary>
    /// <summary>A key as a slot's corner shows it: the punctuation keys as themselves, not their names.</summary>
    private static string KeyWord(string key) => key switch { "Minus" => "-", "Equal" => "=", "Quoteleft" => "~", _ => key };

    private void DrawActionBars(Simulation simulation, string? casting)
    {
        if (_hud.Bars is not { } bars)
            return;
        string? station = _inventory.OpenStation?.Kind;
        var views = Enumerable.Range(0, UNNAMED.Domain.Progression.QuickBar.Bars).Select(bar =>
            ActionBarModel.Slots(simulation, bar, key => Names.Of(_session, key),
                (b, slot) => b == _barPage ? KeyWord(HelpPanel.Key(QuickKeys[slot])) : "", station, casting)).ToImmutableArray();
        bars.Set(views, _barPage);
    }
}
