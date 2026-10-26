// UNNAMED World - racial abilities at run time: the Orenth crossings and their toll, and the Siann's Kindled Ring
// (RACES.md §3-§4; RACE_ABILITIES_PROPOSAL.md §3.3-§3.4, owner-approved 2026-10-24). The Kal's wings are movement's.
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>Use a racial ability of the character's people (a quick-bar slot's <c>ability.people.*</c>). What triggers it is presentation's.</summary>
public sealed record PeopleAbilityCommand(EntityId Actor, string AbilityId) : GameCommand(Actor);

/// <summary>A racial ability was used; ready again at <paramref name="ReadyTick"/>.</summary>
public sealed record PeopleAbilityUsed(EntityId Actor, string AbilityId, long ReadyTick, long Tick);

/// <summary>An Orenth stepped sideways and out again: presentation draws the two-ended registration ghost.</summary>
public sealed record Blinked(EntityId Actor, Body From, Body To, long Tick);

/// <summary>An Orenth is half elsewhere until <paramref name="UntilTick"/>.</summary>
public sealed record WraithStepped(EntityId Actor, long UntilTick, long Tick);

public sealed record AnchorPlaced(EntityId Actor, Body At, long Tick);

/// <summary>A recall to the anchor began; it crosses at <paramref name="UntilTick"/> if nothing breaks it.</summary>
public sealed record RecallBegun(EntityId Actor, long UntilTick, long Tick);

public sealed record RecallBroken(EntityId Actor, string Reason, long Tick);

public sealed record Recalled(EntityId Actor, Body From, Body To, long Tick);

/// <summary>A crossing took something (§3.4). Never a number: presentation words what is gone, and only that.</summary>
public sealed record CrossingCostTaken(EntityId Actor, TollKind Kind, string? Ref, long Tick);

/// <summary>
/// Owns: <see cref="StateSlice.Crossing"/>. Uses the character's racial abilities: each is its people's own, paid in Stamina and Focus,
/// ready again after its cooldown (transient, as cooldowns are). An Orenth crossing - a blink, a wraith-step, a recall - adds to the hidden
/// toll, and each tier the toll reaches takes something for good (<see cref="TollRules"/>). The wings are the movement system's; passive
/// abilities are the traits'.
/// </summary>
internal sealed class PeopleSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly Func<MoveIntent> _intent;
    private readonly Dictionary<string, long> _readyAt = new(StringComparer.Ordinal);
    private (PeopleAbility Ability, long Until, Body From, int Health)? _recall;

    public PeopleSystem(SystemContext context, SliceOwner owner, EntityId player, Func<MoveIntent> intent)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _intent = intent;
    }

    private RuntimeState State => _context.State;

    public string? Handle(PeopleAbilityCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (_context.PlayerAbilities.FirstOrDefault(a => a.Id == command.AbilityId) is not { } ability)
            return $"{command.AbilityId} is not an ability of {State.PeopleId}";
        if (ability.Passive)
            return $"{ability.Name} is always with you";
        if (ability.Action is PeopleAction.Glide or PeopleAction.Flare or PeopleAction.Leap)
            return $"{ability.Name} is in the jump";
        if (State.PlayerCombat.Defeated || _context.ControlOn(_player) != ControlKind.None)
            return "cannot now";
        if (_readyAt.TryGetValue(ability.Id, out long ready) && tick < ready)
            return $"{ability.Name} is not ready ({ready - tick} ticks)";
        if (Pool(State.Progression.Pools.Stamina, _context.PlayerStats().StaminaMax) < ability.StaminaCost)
            return $"too tired for {ability.Name}";
        if (Pool(State.Progression.Pools.Focus, _context.PlayerStats().FocusMax) < ability.FocusCost)
            return $"not enough Focus for {ability.Name}";
        Func<string?> act = ability.Action switch
        {
            PeopleAction.Blink => Blink(ability, tick),
            PeopleAction.WraithStep => WithEffect(ability, tick, () => _context.Events.Publish(new WraithStepped(_player, tick + ability.DurationTicks, tick))),
            PeopleAction.KindledRing => WithEffect(ability, tick, () => { }),
            PeopleAction.Anchor => Anchor(tick),
            PeopleAction.Recall => Recall(ability, tick),
            _ => () => $"{ability.Name} is not usable yet",
        };
        if (act() is { } refused)
            return refused;
        if (ability.StaminaCost > 0 || ability.FocusCost > 0)
            _context.Dispatch(new ChangePools(0, -ability.StaminaCost) { Focus = -ability.FocusCost });
        _readyAt[ability.Id] = tick + ability.CooldownTicks;
        _context.Events.Publish(new PeopleAbilityUsed(_player, ability.Id, tick + ability.CooldownTicks, tick));
        // A recall's toll is paid when it crosses, not when it begins.
        if (ability.Action != PeopleAction.Recall)
            Pay(ability.Toll, tick);
        return null;
    }

    /// <summary>Ticks until an ability is ready again; 0 when it is (the quick bar's sweep).</summary>
    public int Cooldown(string abilityId, long tick) => _readyAt.TryGetValue(abilityId, out long ready) && ready > tick ? (int)(ready - tick) : 0;

    /// <summary>
    /// A recall in its stillness crosses when it is over, and breaks if the body moves, is wounded or falls before then. The anchor lapses a
    /// day after it was set.
    /// </summary>
    public void Tick(long tick)
    {
        if (State.Crossing.Anchor is { } anchor && Lapsed(anchor, tick))
            State.SetCrossing(_owner, State.Crossing with { Anchor = null });
        if (_recall is not { } recall)
            return;
        var body = State.Body;
        string? broken = State.PlayerCombat.Defeated ? "fallen"
            : body.XMm != recall.From.XMm || body.ZMm != recall.From.ZMm ? "moved"
            : Pool(State.Progression.Pools.Health, _context.PlayerStats().HealthMax) < recall.Health ? "wounded"
            : State.Crossing.Anchor is null ? "the anchor lapsed"
            : null;
        if (broken is not null)
        {
            _recall = null;
            _context.Events.Publish(new RecallBroken(_player, broken, tick));
            return;
        }
        if (tick < recall.Until)
            return;
        _recall = null;
        var to = State.Crossing.Anchor!;
        var at = new Body(to.XMm, to.YMm, to.ZMm, body.FacingMdeg);
        _context.Dispatch(new Relocate(at));
        _context.Events.Publish(new Recalled(_player, body, at, tick));
        Pay(recall.Ability.Toll, tick);
    }

    /// <summary>Up to the blink's range along where the body is going - or, standing, where it faces - past bodies, never through a wall.</summary>
    private Func<string?> Blink(PeopleAbility ability, long tick) => () =>
    {
        var body = State.Body;
        var intent = _intent();
        int dx = intent.IsMoving ? intent.DirXPermille : (int)Math.Round(Math.Sin(body.FacingMdeg / 1000.0 * Math.PI / 180) * 1000);
        int dz = intent.IsMoving ? intent.DirZPermille : (int)Math.Round(Math.Cos(body.FacingMdeg / 1000.0 * Math.PI / 180) * 1000);
        var to = PeopleRules.BlinkTarget(body, dx, dz, ability.RangeMm, _context.PlayerMovement, _context.Setup.SpaceFor(State.Space),
            _context.ClosedDoors(), _context.Bodies().ToList());
        if (to is null)
            return "nowhere to step to";
        _context.Dispatch(new Relocate(to));
        _context.Events.Publish(new Blinked(_player, body, to, tick));
        return null;
    };

    private Func<string?> WithEffect(PeopleAbility ability, long tick, Action then) => () =>
    {
        if (ability.EffectId is not { } effect)
            return $"{ability.Name} has no effect";
        _context.Dispatch(new ApplyEffect(_player, effect));
        then();
        return null;
    };

    /// <summary>Fix a point in this reality, here: one anchor at a time, so setting it again moves it.</summary>
    private Func<string?> Anchor(long tick) => () =>
    {
        var body = State.Body;
        State.SetCrossing(_owner, State.Crossing with { Anchor = new PlanarAnchor(State.Space.ToString(), body.XMm, body.YMm, body.ZMm, tick) });
        _context.Events.Publish(new AnchorPlaced(_player, body, tick));
        return null;
    };

    private Func<string?> Recall(PeopleAbility ability, long tick) => () =>
    {
        if (State.Crossing.Anchor is not { } anchor || Lapsed(anchor, tick))
            return "no anchor to cross back to";
        if (anchor.Space != State.Space.ToString())
            return "the anchor is in another place";
        var body = State.Body;
        if (Math.Sqrt(Math.Pow(anchor.XMm - body.XMm, 2) + Math.Pow(anchor.ZMm - body.ZMm, 2)) > ability.RangeMm)
            return "the anchor is too far";
        if (_recall is not null)
            return "already crossing";
        _recall = (ability, tick + ability.DurationTicks, body, Pool(State.Progression.Pools.Health, _context.PlayerStats().HealthMax));
        _context.Events.Publish(new RecallBegun(_player, tick + ability.DurationTicks, tick));
        return null;
    };

    private bool Lapsed(PlanarAnchor anchor, long tick) =>
        _context.PlayerAbilities.FirstOrDefault(a => a.Action == PeopleAction.Anchor) is { DurationTicks: > 0 } placed && tick - anchor.PlacedTick > placed.DurationTicks;

    /// <summary>
    /// The toll a crossing adds, and what each tier it reaches takes: a place from the map (only those discovered), a name from those the
    /// character has dealings with, or a mark or years that are only seen.
    /// </summary>
    private void Pay(int toll, long tick)
    {
        if (toll <= 0)
            return;
        var before = State.Crossing;
        var after = TollRules.Pay(before, toll, State.Discoveries.Keys.ToList(), State.Relationships.Keys.ToList());
        State.SetCrossing(_owner, after);
        foreach (var cost in after.Costs.Skip(before.Costs.Length))
            _context.Events.Publish(new CrossingCostTaken(_player, cost.Kind, cost.Ref, tick));
    }

    private static int Pool(int? current, long max) => current ?? (int)max;
}
