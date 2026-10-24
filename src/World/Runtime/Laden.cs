// UNNAMED World - the laden band: a heavy pack costs stamina (WP3b; SYSTEMS_COMPLETENESS_PLAN §2.4 I9, D4)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Owns no state. Above the laden band (<c>config.inventory laden</c>, 80% of the carry limit) the pack weighs on the legs (D4): a sprint
/// drains more stamina than usual, and a dodge costs more. It adds to what combat already takes, through the pools' own command, and never
/// reaches into combat: a sprint is read from the movement intent and the combat phase, as combat reads it, and a dodge is refused without
/// its whole laden cost and charged the extra once combat has accepted it. Stamina still returns only once the legs rest, as ever. Nothing
/// heavier than the limit is ever taken up: that refusal is the inventory's and is unchanged. The extra drain's part-points carry over
/// laden-sprint ticks only, and are transient, as combat's own sprint remainder is.
/// </summary>
internal sealed class LadenSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly Func<MoveIntent> _intent;
    private readonly Func<long> _carried;
    private readonly Func<long> _limit;
    private long _sprintMilli;

    public LadenSystem(SystemContext context, Func<MoveIntent> intent, Func<long> carried, Func<long> limit)
    {
        _context = context;
        _intent = intent;
        _carried = carried;
        _limit = limit;
    }

    private RuntimeState State => _context.State;
    private LadenRules Rules => _context.Setup.Items.Inventory.Laden;
    private CombatConstants Combat => _context.Setup.Combat.Constants;

    /// <summary>Above the band now.</summary>
    public bool Laden => Rules.IsLaden(_carried(), _limit());

    /// <summary>Nothing is carried anywhere while the character is away: there is no sprint to drain (§2.4).</summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
    }

    /// <summary>After combat's tick: a laden sprint drains the extra share of a sprint's stamina for this tick.</summary>
    public void Tick(long tick)
    {
        var combat = State.PlayerCombat;
        var intent = _intent();
        if (combat.Defeated || combat.Blocking || !intent.IsMoving || intent.Gait != Gait.Sprint
            || combat.Action.PhaseAt(tick, Combat).Phase != CombatPhase.Idle || Stamina() <= 0 || !Laden)
            return;
        // Thousandths of a point per laden-sprint tick, carried over as combat carries its own sprint's.
        _sprintMilli += (long)Combat.SprintStaminaPerSecond * (Rules.SprintDrainPercent - 100) * _context.Setup.TickMilliseconds / 100;
        Spend((int)(_sprintMilli / 1000));
        _sprintMilli %= 1000;
    }

    /// <summary>
    /// A dodge: laden, refused without the whole laden cost, and once combat has accepted it charged the extra share over combat's own.
    /// </summary>
    public string? Dodge(Func<string?> dodge)
    {
        int extra = Laden ? (Combat.DodgeStaminaCost * Rules.DodgeCostPercent + 99) / 100 - Combat.DodgeStaminaCost : 0;
        if (extra > 0 && Stamina() is var stamina && stamina >= Combat.DodgeStaminaCost && stamina < Combat.DodgeStaminaCost + extra)
            return "too tired to dodge";
        if (dodge() is { } refused)
            return refused;
        Spend(extra);
        return null;
    }

    private void Spend(int points)
    {
        int spend = Math.Min(points, Stamina());
        if (spend > 0)
            _context.Dispatch(new ChangePools(0, -spend));
    }

    private int Stamina() =>
        State.Progression.Pools.Stamina
        ?? (int)_context.PlayerStats().StaminaMax;
}
