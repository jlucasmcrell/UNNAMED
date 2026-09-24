// UNNAMED World - combat at run time: attacks, the guard, dodging, stamina, status effects and death
// (SYSTEMS.md S-06, S-07, S-11, S-12; COMBAT_DAMAGE_ARMOR_AND_DEATH.md; PROTOTYPE.md §5 steps 6-7; M3c, M3d)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>A skill threshold that changes a stat (DATA_MODEL.md §4.21 <c>passives</c>): from a level, the stat multiplies.</summary>
public sealed record SkillPassive(string SkillId, int FromLevel, string Stat, double Multiplier);

/// <summary>The combat rules a simulation runs under, built from content at boot.</summary>
public sealed record CombatSetup(
    CombatConstants Constants,
    ImmutableSortedDictionary<string, EffectDefinition> Effects,
    ImmutableSortedDictionary<string, CreatureDefinition> Creatures,
    ImmutableArray<SpawnSite> Spawns)
{
    /// <summary>What using a consumable applies: item definition ID to effect ID (the salve's mending).</summary>
    public ImmutableSortedDictionary<string, string> UseEffects { get; init; } = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

    public ImmutableArray<SkillPassive> Passives { get; init; } = ImmutableArray<SkillPassive>.Empty;

    /// <summary>The behaviour roles creatures play (M3d).</summary>
    public ImmutableSortedDictionary<string, CreatureRole> Roles { get; init; } = ImmutableSortedDictionary.Create<string, CreatureRole>(StringComparer.Ordinal);

    public AwarenessRules Awareness { get; init; } = new(30, 40, 200, 10, 60, 80, 120);

    public NoiseRules Noise { get; init; } = new(3_000, 8_000, 16_000, 10_000, 20_000, 45_000);

    /// <summary>How long a corpse lies before it is gone, looted or not.</summary>
    public long CorpseDecayTicks { get; init; } = 12_000;

    public int CorpseStackSlots { get; init; } = 8;

    public static CombatSetup Empty { get; } = new(new CombatConstants(),
        ImmutableSortedDictionary.Create<string, EffectDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, CreatureDefinition>(StringComparer.Ordinal), ImmutableArray<SpawnSite>.Empty);
}

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Attack with what the main hand holds, or bare-handed: a swing through the front arc, or a drawn shot along the facing.</summary>
public sealed record AttackCommand(EntityId Actor) : GameCommand(Actor);

/// <summary>Raise or lower the guard. A raised guard takes blows from the front for stamina and slows the feet to a walk.</summary>
public sealed record BlockCommand(EntityId Actor, bool Raised) : GameCommand(Actor);

/// <summary>Dodge along a world-space direction (per mille, like <see cref="MoveIntent"/>); no direction dodges backwards.</summary>
public sealed record DodgeCommand(EntityId Actor, int DirXPermille, int DirZPermille) : GameCommand(Actor);

/// <summary>Use a carried consumable (the salve).</summary>
public sealed record UseItemCommand(EntityId Actor, EntityId Item) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>An attack began. Presentation times its clip to these phases; the simulation never reads a clip.</summary>
public sealed record AttackStarted(EntityId Attacker, string Source, int WindupTicks, int ActiveTicks, int RecoveryTicks, long Tick);

/// <summary>An attack's window closed without touching anything: out of reach, out of the arc, or stopped by a wall.</summary>
public sealed record AttackMissed(EntityId Attacker, string Source, long Tick);

/// <summary>A blow landed, or was dodged: where, how hard, and what it left.</summary>
public sealed record HitResolved(
    EntityId Attacker, string AttackerDefId, EntityId Target, string Source, BodyRegion Region, int Damage,
    bool Critical, bool Blocked, bool Dodged, bool Staggered, int HealthAfter, long Tick);

/// <summary>Health changed outside a blow: an effect's tick, a salve.</summary>
public sealed record HealthChanged(EntityId Target, string Source, int Delta, int HealthAfter, long Tick);

/// <summary>A blow met a raised guard with no stamina behind it: the guard drops and the body staggers.</summary>
public sealed record GuardBroken(EntityId Actor, long Tick);

public sealed record EffectApplied(EntityId Target, string EffectId, int Stacks, long ExpiresTick, long Tick);

public sealed record EffectExpired(EntityId Target, string EffectId, long Tick);

public sealed record CreatureKilled(EntityId Creature, string DefId, EntityId Killer, long Tick);

/// <summary>One of the blows before a death, for the recap: who, with what, how much.</summary>
public sealed record DeathRecapLine(string AttackerDefId, string Source, int Damage, long Tick);

/// <summary>
/// The player died. <see cref="KillerDefId"/> and <see cref="Cause"/> are the last blow's; <see cref="Recap"/> is the
/// last few, so a tester can name what killed them (ROADMAP.md M3c). The penalty is XP debt, never lost XP (AG-8).
/// </summary>
public sealed record PlayerDied(string KillerDefId, string Cause, ImmutableArray<DeathRecapLine> Recap, long DebtAdded, long Tick);

public sealed record PlayerRespawned(Body Body, long Tick);

/// <summary>A use of a skill: the XP it earned, which the difficulty gate may make zero (PROGRESSION.md §4.2).</summary>
public sealed record SkillPracticed(string SkillId, long Xp, int Level, long Tick);

/// <summary>A carried item was used: a consumable's effect (the salve's mending), or none for a book that taught.</summary>
public sealed record ItemUsed(EntityId Actor, string DefId, string? EffectId, long Tick);

public sealed record ItemConsumed(EntityId Actor, string DefId, int Count, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

public enum CombatPhase
{
    Idle,
    Windup,
    Active,
    Recovery,
    Dodge,
    Staggered,
}

/// <summary>The player in combat: what they are doing, with what, and their pools.</summary>
public sealed record CombatView(CombatPhase Phase, int PhaseTicksLeft, string? AttackSource, bool Blocking, AttackProfile Weapon,
    int Health, int MaxHealth, int Stamina, int MaxStamina, ImmutableArray<ActiveEffect> Effects)
{
    public int Focus { get; init; }

    public int MaxFocus { get; init; }

    public int Strain { get; init; }

    public int StrainTolerance { get; init; }

    /// <summary>Strain at or past the Strained share of tolerance: the next working may cost health.</summary>
    public bool Strained { get; init; }

    /// <summary>The formula being cast, while the working runs.</summary>
    public string? Casting { get; init; }
}

// ── state (internal) ────────────────────────────────────────────────────────

internal enum ActionKind
{
    Idle,
    Attack,
    Dodge,
    Staggered,

    /// <summary>A committed run: windup, then straight on until it hits, runs its distance, or meets something solid.</summary>
    Charge,

    /// <summary>A working (M3e): the cast time is its tell, then it is released, then the body recovers.</summary>
    Cast,
}

/// <summary>What a body is doing, from which tick. The phases follow from the start tick; nothing counts down.</summary>
internal sealed record ActionState(ActionKind Kind, long StartTick, AttackProfile? Attack, int DirXPermille, int DirZPermille, ImmutableHashSet<EntityId> Struck)
{
    public static ActionState Idle { get; } = new(ActionKind.Idle, 0, null, 0, 0, ImmutableHashSet<EntityId>.Empty);

    public static ActionState Begin(ActionKind kind, long tick, AttackProfile? attack = null, int dirX = 0, int dirZ = 0) =>
        new(kind, tick, attack, dirX, dirZ, ImmutableHashSet<EntityId>.Empty);

    /// <summary>A charge's run ended on this tick (0 while it runs); recovery counts from here.</summary>
    public long EndedTick { get; init; }

    /// <summary>A stagger that lasts longer than the usual (a charger stunned against a wall); 0 takes the usual.</summary>
    public int LastsTicks { get; init; }

    /// <summary>The formula a cast is working.</summary>
    public FormulaDefinition? Formula { get; init; }

    /// <summary>The phase at a tick. Tick <c>StartTick + 1</c> is the action's first.</summary>
    public (CombatPhase Phase, int TicksLeft) PhaseAt(long tick, CombatConstants constants)
    {
        long elapsed = tick - StartTick;
        switch (Kind)
        {
            case ActionKind.Attack:
            {
                var a = Attack!;
                if (elapsed <= a.WindupTicks)
                    return (CombatPhase.Windup, (int)(a.WindupTicks - elapsed));
                if (elapsed <= a.WindupTicks + a.ActiveTicks)
                    return (CombatPhase.Active, (int)(a.WindupTicks + a.ActiveTicks - elapsed));
                if (elapsed <= a.TotalTicks)
                    return (CombatPhase.Recovery, (int)(a.TotalTicks - elapsed));
                break;
            }
            case ActionKind.Dodge:
                if (elapsed <= constants.DodgeTicks)
                    return (CombatPhase.Dodge, (int)(constants.DodgeTicks - elapsed));
                if (elapsed <= constants.DodgeTicks + constants.DodgeRecoveryTicks)
                    return (CombatPhase.Recovery, (int)(constants.DodgeTicks + constants.DodgeRecoveryTicks - elapsed));
                break;
            case ActionKind.Staggered:
            {
                int lasts = LastsTicks > 0 ? LastsTicks : constants.StaggerTicks;
                if (elapsed <= lasts)
                    return (CombatPhase.Staggered, (int)(lasts - elapsed));
                break;
            }
            case ActionKind.Charge:
            {
                var a = Attack!;
                if (elapsed <= a.WindupTicks)
                    return (CombatPhase.Windup, (int)(a.WindupTicks - elapsed));
                if (EndedTick == 0)
                    return (CombatPhase.Active, 0);
                if (tick - EndedTick <= a.RecoveryTicks)
                    return (CombatPhase.Recovery, (int)(a.RecoveryTicks - (tick - EndedTick)));
                break;
            }
            case ActionKind.Cast:
            {
                var f = Formula!;
                if (elapsed <= f.CastTicks)
                    return (CombatPhase.Windup, (int)(f.CastTicks - elapsed));
                if (elapsed == f.CastTicks + 1)
                    return (CombatPhase.Active, 0);
                if (elapsed <= f.CastTicks + 1 + f.RecoveryTicks)
                    return (CombatPhase.Recovery, (int)(f.CastTicks + 1 + f.RecoveryTicks - elapsed));
                break;
            }
        }
        return (CombatPhase.Idle, 0);
    }
}

/// <summary>The player's transient combat state. Never saved: a load starts at rest, with pools and effects from the save.</summary>
internal sealed record PlayerCombat(ActionState Action, bool Blocking, long StaggerImmuneUntil, long LastExertion, long LastCombat,
    ImmutableArray<DeathRecapLine> Recent)
{
    public const int RecapLength = 5;

    public static PlayerCombat Rested { get; } = new(ActionState.Idle, false, 0, -1_000_000, -1_000_000, ImmutableArray<DeathRecapLine>.Empty);

    /// <summary>Health reached zero; the death system settles it at the end of the tick.</summary>
    public bool Defeated { get; init; }

    /// <summary>Thousandths of a point accrued by sprinting, regeneration and recovery, so integer pools move smoothly.</summary>
    public int SprintMilli { get; init; }
    public int StaminaMilli { get; init; }
    public int HealthMilli { get; init; }
    public int FocusMilli { get; init; }
    public int StrainMilli { get; init; }

    /// <summary>The last working begun or released: Focus and Strain return only after a pause from it.</summary>
    public long LastCast { get; init; } = -1_000_000;
}

// ── internal commands ───────────────────────────────────────────────────────

/// <summary>To <see cref="ProgressionSystem"/>: move the current pools, clamped to their derived maxima (Strain to tolerance).</summary>
internal sealed record ChangePools(int Health, int Stamina) : InternalCommand
{
    public int Focus { get; init; }

    public int Strain { get; init; }
}

/// <summary>To <see cref="ProgressionSystem"/>: one use of a skill.</summary>
internal sealed record PracticeSkill(SkillPractice Practice) : InternalCommand;

/// <summary>To <see cref="ProgressionSystem"/>: a death - the XP debt (AG-8), and pools refilled for the respawn.</summary>
internal sealed record RecordDeath : InternalCommand;

/// <summary>To <see cref="MovementSystem"/>: put the body somewhere (a respawn).</summary>
internal sealed record Relocate(Body Body) : InternalCommand;

/// <summary>To <see cref="StatusEffectSystem"/>.</summary>
internal sealed record ApplyEffect(EntityId Target, string EffectId) : InternalCommand;

/// <summary>To <see cref="StatusEffectSystem"/>.</summary>
internal sealed record ClearEffects(EntityId Target) : InternalCommand;

/// <summary>To <see cref="CombatSystem"/>, the one place health falls: harm that is not a blow (an effect's tick).</summary>
internal sealed record Harm(EntityId Target, string Source, int Amount) : InternalCommand;

/// <summary>To <see cref="CombatSystem"/>.</summary>
internal sealed record Heal(EntityId Target, string Source, int Amount) : InternalCommand;

/// <summary>To <see cref="CombatSystem"/>: the player respawned; their fight is over.</summary>
internal sealed record EndFight : InternalCommand;

/// <summary>To <see cref="InventorySystem"/>: spend carried items by definition (an arrow at release).</summary>
internal sealed record ConsumeItem(string DefId, int Count) : InternalCommand;

// ── systems ─────────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Combat"/> - the player's combat state. The one damage pipeline (S-12): every blow, the
/// player's and every creature's, resolves here through <see cref="CombatRules.Resolve"/>, and each body's owner applies
/// what it did - the player's pools through the progression system, a creature's health through the creature system.
/// Effect ticks enter the same way. A blow needs reach, the front arc and no wall between; timing decides when it lands.
/// </summary>
internal sealed partial class CombatSystem
{
    private const double SampleScale = 18446744073709551616.0;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly Func<MoveIntent> _intent;

    public CombatSystem(SystemContext context, SliceOwner owner, EntityId player, Func<MoveIntent> intent)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _intent = intent;
    }

    private CombatSetup Setup => _context.Setup.Combat;
    private CombatConstants C => Setup.Constants;
    private RuntimeState State => _context.State;
    private int TickMs => _context.Setup.TickMilliseconds;

    // ── the player's commands ───────────────────────────────────────────────

    public string? Handle(AttackCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var combat = State.PlayerCombat;
        if (Busy(combat.Action, tick) is { } busy)
            return busy;
        var attack = PlayerAttack();
        if (attack.Ranged && attack.AmmoDefId is { } ammo && !State.Inventory.Any(e => e.DefId == ammo))
            return $"no {ammo} to shoot";
        if (Stamina() < attack.StaminaCost)
            return "too tired to attack";
        Exert(attack.StaminaCost, tick);
        State.SetPlayerCombat(_owner, State.PlayerCombat with
        {
            Action = ActionState.Begin(ActionKind.Attack, tick, attack),
            Blocking = false,
            LastCombat = tick,
        });
        _context.Events.Publish(new AttackStarted(_player, attack.Source, attack.WindupTicks, attack.ActiveTicks, attack.RecoveryTicks, tick));
        return null;
    }

    public string? Handle(BlockCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var combat = State.PlayerCombat;
        if (!command.Raised)
        {
            if (combat.Blocking)
                State.SetPlayerCombat(_owner, combat with { Blocking = false });
            return null;
        }
        if (Busy(combat.Action, tick) is { } busy)
            return busy;
        if (PlayerAttack().Ranged)
            return "a bow cannot guard";
        State.SetPlayerCombat(_owner, combat with { Blocking = true });
        return null;
    }

    public string? Handle(DodgeCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var combat = State.PlayerCombat;
        var (phase, _) = combat.Action.PhaseAt(tick, C);
        // A windup can still be abandoned; a committed blow and a stagger cannot.
        if (phase is not (CombatPhase.Idle or CombatPhase.Windup))
            return phase == CombatPhase.Staggered ? "staggered" : $"cannot dodge while {phase.ToString().ToLowerInvariant()}";
        if (Stamina() < C.DodgeStaminaCost)
            return "too tired to dodge";
        double dx = command.DirXPermille, dz = command.DirZPermille;
        if (dx == 0 && dz == 0)
        {
            double facing = State.Body.FacingMdeg / 1000.0 * Math.PI / 180;
            (dx, dz) = (-Math.Sin(facing), -Math.Cos(facing));
        }
        double length = Math.Sqrt(dx * dx + dz * dz);
        int x = (int)Math.Round(dx / length * 1000), z = (int)Math.Round(dz / length * 1000);
        BreakCast(combat.Action, "dodged", tick);
        Exert(C.DodgeStaminaCost, tick);
        State.SetPlayerCombat(_owner, State.PlayerCombat with { Action = ActionState.Begin(ActionKind.Dodge, tick, dirX: x, dirZ: z), Blocking = false });
        return null;
    }

    private string? Busy(ActionState action, long tick) => action.PhaseAt(tick, C).Phase switch
    {
        CombatPhase.Idle => State.PlayerCombat.Defeated ? "dead" : null,
        CombatPhase.Staggered => "staggered",
        CombatPhase.Dodge => "dodging",
        var phase => $"already {(action.Kind == ActionKind.Cast ? "casting" : "attacking")} ({phase.ToString().ToLowerInvariant()})",
    };

    /// <summary>What the player attacks with: the main-hand weapon, or bare hands.</summary>
    public AttackProfile PlayerAttack()
    {
        if (State.Equipment.TryGetValue(EquipSlot.MainHand, out var id) && State.Inventory.FirstOrDefault(e => e.ItemId == id) is { } entry
            && _context.Setup.Items.Catalog.Find(entry.DefId)?.Weapon is { } weapon)
        {
            // A weapon's quality is its own instance's (M3f): a fine one hits harder than its definition says, a crude one softer.
            var attack = WeaponAttack(entry.DefId, weapon);
            int step = entry.Quality * _context.Setup.Crafting.Constants.WeaponDamagePerStep;
            return step == 0 ? attack : attack with { DamageMin = Math.Max(1, attack.DamageMin + step), DamageMax = Math.Max(1, attack.DamageMax + step) };
        }
        return C.Unarmed;
    }

    private AttackProfile WeaponAttack(string defId, WeaponStats weapon) => AttackOf(defId, weapon, C, TickMs);

    /// <summary>
    /// A weapon's attack in ticks: a swing splits into windup, active and recovery; a bow draws, releases once, and nocks. The player's
    /// and a companion's weapons (M6) are timed alike.
    /// </summary>
    internal static AttackProfile AttackOf(string defId, WeaponStats weapon, CombatConstants constants, int tickMs)
    {
        int Ticks(int milliseconds) => (int)Math.Round((double)milliseconds / tickMs, MidpointRounding.AwayFromZero);
        int stamina = weapon.StaminaCost ?? constants.DefaultStaminaCost;
        if (weapon.Ranged)
        {
            return new AttackProfile(defId, weapon.DamageMin, weapon.DamageMax, weapon.DamageType, constants.RangedRangeMm,
                Math.Max(1, Ticks(weapon.DrawMs)), 1, constants.BowRecoveryTicks, stamina)
            {
                Ranged = true,
                AmmoDefId = weapon.AmmoDefId,
                SkillId = weapon.SkillId,
            };
        }
        int total = Math.Max(3, Ticks(weapon.AttackMs));
        int windup = Math.Max(1, (int)Math.Round(total * constants.WindupPercent / 100.0, MidpointRounding.AwayFromZero));
        int active = Math.Max(1, (int)Math.Round(total * constants.ActivePercent / 100.0, MidpointRounding.AwayFromZero));
        return new AttackProfile(defId, weapon.DamageMin, weapon.DamageMax, weapon.DamageType, weapon.ReachMm, windup, active,
            Math.Max(0, total - windup - active), stamina) { SkillId = weapon.SkillId };
    }

    private int Ticks(int milliseconds) => (int)Math.Round((double)milliseconds / TickMs, MidpointRounding.AwayFromZero);

    // ── the tick ────────────────────────────────────────────────────────────

    public void Tick(long tick)
    {
        PlayerTick(tick);
        Vitals(tick);
    }

    private void PlayerTick(long tick)
    {
        var combat = State.PlayerCombat;
        var action = combat.Action;
        if (action.Kind == ActionKind.Idle || combat.Defeated)
            return;
        var (phase, _) = action.PhaseAt(tick, C);
        if (phase == CombatPhase.Idle)
        {
            State.SetPlayerCombat(_owner, combat with { Action = ActionState.Idle });
            return;
        }
        if (action.Kind == ActionKind.Cast)
        {
            if (phase == CombatPhase.Active)
                Release(action.Formula!, tick);
            return;
        }
        if (action.Kind != ActionKind.Attack || phase != CombatPhase.Active)
            return;

        var attack = action.Attack!;
        long elapsed = tick - action.StartTick;
        var body = State.Body;
        if (attack.Ranged)
        {
            if (elapsed != attack.WindupTicks + 1)
                return;
            if (attack.AmmoDefId is { } ammo && _context.Dispatch(new ConsumeItem(ammo, 1)) is not null)
            {
                _context.Events.Publish(new AttackMissed(_player, attack.Source, tick));
                return;
            }
            if (RangedTarget(body, attack.ReachMm) is { } target)
                PlayerHits(target, attack, tick);
            else
                _context.Events.Publish(new AttackMissed(_player, attack.Source, tick));
            return;
        }

        var struck = action.Struck;
        foreach (var creature in State.Creatures.Values.Where(c => c.Alive && !struck.Contains(c.Id)).ToList())
        {
            if (!CombatRules.InFront(body.XMm, body.ZMm, body.FacingMdeg, creature.Body.XMm, creature.Body.ZMm,
                    attack.ReachMm + creature.Definition.RadiusMm, C.MeleeArcMdeg)
                || Walled(body.XMm, body.ZMm, creature.Body.XMm, creature.Body.ZMm))
                continue;
            struck = struck.Add(creature.Id);
            PlayerHits(creature, attack, tick);
        }
        State.SetPlayerCombat(_owner, State.PlayerCombat with { Action = State.PlayerCombat.Action with { Struck = struck } });
        if (struck.IsEmpty && elapsed == attack.WindupTicks + attack.ActiveTicks)
            _context.Events.Publish(new AttackMissed(_player, attack.Source, tick));
    }

    /// <summary>The first living creature along the facing within range whose body the line meets before any wall does.</summary>
    private CreatureState? RangedTarget(Body from, long rangeMm)
    {
        double facing = from.FacingMdeg / 1000.0 * Math.PI / 180;
        double dx = Math.Sin(facing), dz = Math.Cos(facing);
        CreatureState? best = null;
        double bestAlong = double.MaxValue;
        foreach (var creature in State.Creatures.Values.Where(c => c.Alive))
        {
            double ox = creature.Body.XMm - from.XMm, oz = creature.Body.ZMm - from.ZMm;
            double along = ox * dx + oz * dz;
            double across = Math.Abs(ox * dz - oz * dx);
            if (along <= 0 || along > rangeMm || across > creature.Definition.RadiusMm || along >= bestAlong)
                continue;
            if (Walled(from.XMm, from.ZMm, from.XMm + dx * along, from.ZMm + dz * along))
                continue;
            best = creature;
            bestAlong = along;
        }
        return best;
    }

    private bool Walled(double x0, double z0, double x1, double z1) =>
        _context.Setup.Layout.Space.Blockers.Concat(_context.ClosedDoors()).Any(b => b.Crosses(x0, z0, x1, z1));

    /// <summary>The player's blow on a creature: resolved here, applied by the creature's owner.</summary>
    private void PlayerHits(CreatureState creature, AttackProfile attack, long tick)
    {
        var definition = creature.Definition;
        var defense = new DefenseProfile(definition.MaxHealth, WithBonus(definition.Armor, EffectRules.ArmorBonus(EffectsOf(creature.Id), Setup.Effects)),
            definition.Resistances, Blocking: false, Dodging: false);
        var strike = PlayerStrike(attack);
        // A weak point open from behind is where a blow from behind lands (COMBAT §19): position, not luck.
        if (definition.WeakPoint is { FromBehind: true } weak && Behind(creature.Body, State.Body))
            strike = strike with { ForcedRegion = weak.Region };
        var result = CombatRules.Resolve(strike, defense, C, Random("player", creature.Key, creature.Body, tick));
        State.SetPlayerCombat(_owner, State.PlayerCombat with { LastCombat = tick });
        _context.Dispatch(new WoundCreature(creature.Id, result, attack.Source));
        bool alive = State.Creatures.Values.Any(c => c.Id == creature.Id && c.Alive);
        if (result.EffectApplied && attack.OnHitEffect is { } effect && alive)
            _context.Dispatch(new ApplyEffect(creature.Id, effect));
        // Weapon skill only from effective contribution: a blow that wounded something that fights back (ROADMAP.md M3c).
        if (attack.SkillId is { } skill && result.Final > 0)
        {
            // A formula's first wounding working is also its novelty (PROGRESSION.md §4.2).
            _context.Dispatch(new PracticeSkill(new SkillPractice(skill, definition.Level, PracticeOutcome.Success, tick)
            {
                NoveltyKey = attack.Magic ? attack.Source : null,
            }));
        }
    }

    /// <summary>True when the attacker stands behind the body: more than 110 degrees off its facing.</summary>
    internal static bool Behind(Body body, Body attacker)
    {
        double dx = attacker.XMm - body.XMm, dz = attacker.ZMm - body.ZMm;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return false;
        double facing = body.FacingMdeg / 1000.0 * Math.PI / 180;
        return (dx * Math.Sin(facing) + dz * Math.Cos(facing)) / length < Math.Cos(110 * Math.PI / 180);
    }

    /// <summary>What the player brings to a blow: Might for physical blows, effects such as weakness, and skill passives.</summary>
    private Strike PlayerStrike(AttackProfile attack)
    {
        var rules = _context.Setup.Progression;
        double multiplier = EffectRules.DamageDealtMultiplier(EffectsOf(_player), Setup.Effects);
        if (attack.Magic)
            multiplier *= MagicRules.ResonanceMultiplier(Stats().Resonance, M);
        else if (DamageTypes.IsPhysical(attack.DamageType))
            multiplier *= CombatRules.MightMultiplier(ProgressionEngine.AttributeValue(State.Progression, CharacterAttribute.Might, rules), rules.AttributeBase, C);
        double stagger = 1.0;
        foreach (var passive in Setup.Passives)
        {
            if (passive.Stat == "stat.stagger_power" && passive.SkillId == attack.SkillId
                && ProgressionEngine.SkillLevel(State.Progression, passive.SkillId) >= passive.FromLevel)
                stagger *= passive.Multiplier;
        }
        return new Strike(attack, multiplier, C.BaseCritPercent, stagger);
    }

    /// <summary>
    /// A creature's attack reached the player inside its active window (the creature system checked reach, arc and walls).
    /// The guard holds only against what is in front of it, and only while there is stamina behind it.
    /// </summary>
    public string? Handle(CreatureStrike command, long tick)
    {
        if (State.Creatures.Values.FirstOrDefault(c => c.Id == command.Attacker) is not { Alive: true } creature)
            return "no such attacker";
        var combat = State.PlayerCombat;
        if (combat.Defeated)
            return "dead";
        var attack = command.Attack;
        var body = State.Body;
        bool guarding = combat.Blocking
            && CombatRules.InFront(body.XMm, body.ZMm, body.FacingMdeg, creature.Body.XMm, creature.Body.ZMm, long.MaxValue / 4, C.BlockArcMdeg);
        if (guarding && Stamina() < C.BlockStaminaPerHit)
        {
            guarding = false;
            State.SetPlayerCombat(_owner, combat with { Blocking = false });
            _context.Events.Publish(new GuardBroken(_player, tick));
            StaggerPlayer(tick, force: true);
            combat = State.PlayerCombat;
        }
        bool dodging = combat.Action.Kind == ActionKind.Dodge && combat.Action.PhaseAt(tick, C).Phase == CombatPhase.Dodge;
        var defense = new DefenseProfile(MaxHealth(), PlayerArmor(), ImmutableSortedDictionary.Create<string, double>(StringComparer.Ordinal), guarding, dodging);
        var strike = new Strike(attack, EffectRules.DamageDealtMultiplier(EffectsOf(creature.Id), Setup.Effects), C.BaseCritPercent, 1.0);
        var result = CombatRules.Resolve(strike, defense, C, Random(creature.Key, "player", body, tick));

        if (result.Blocked)
            Exert(C.BlockStaminaPerHit, tick);
        State.SetPlayerCombat(_owner, State.PlayerCombat with { LastCombat = tick });
        int health = result.Final > 0 ? LosePlayerHealth(result.Final, creature.Definition.Id, attack.Source, tick) : Health();
        // Concentration under damage: a wound breaks a working still in its tell.
        if (result.Final > 0 && combat.Action.Kind == ActionKind.Cast && combat.Action.PhaseAt(tick, C).Phase == CombatPhase.Windup)
        {
            BreakCast(combat.Action, "wounded", tick);
            State.SetPlayerCombat(_owner, State.PlayerCombat with { Action = ActionState.Idle });
        }
        bool knocked = attack.ForcesStagger && !result.Blocked && !result.Dodged;
        bool staggered = (result.Staggered || knocked) && health > 0 && StaggerPlayer(tick, force: false);
        _context.Events.Publish(new HitResolved(creature.Id, creature.Definition.Id, _player, attack.Source, result.Region, result.Final,
            result.Critical, result.Blocked, result.Dodged, staggered, health, tick));
        if (result.EffectApplied && attack.OnHitEffect is { } effect && health > 0)
            _context.Dispatch(new ApplyEffect(_player, effect));
        return null;
    }

    private bool StaggerPlayer(long tick, bool force)
    {
        var combat = State.PlayerCombat;
        if (!force && tick < combat.StaggerImmuneUntil)
            return false;
        State.SetPlayerCombat(_owner, combat with
        {
            Action = ActionState.Begin(ActionKind.Staggered, tick),
            Blocking = false,
            StaggerImmuneUntil = tick + C.StaggerImmunityTicks,
        });
        return true;
    }

    /// <summary>The player's armor on each region, from what is worn there (coverage, COMBAT §11), plus effects such as oakskin.</summary>
    private ImmutableSortedDictionary<BodyRegion, int> PlayerArmor()
    {
        int Worn(params EquipSlot[] slots) => slots.Sum(slot =>
            State.Equipment.TryGetValue(slot, out var id) && State.Inventory.FirstOrDefault(e => e.ItemId == id) is { } entry
                ? _context.Setup.Items.Catalog.Find(entry.DefId)?.ArmorValue ?? 0
                : 0);
        var armor = ImmutableSortedDictionary.CreateRange(new[]
        {
            KeyValuePair.Create(BodyRegion.Head, Worn(EquipSlot.Head)),
            KeyValuePair.Create(BodyRegion.Torso, Worn(EquipSlot.Chest, EquipSlot.Cloak)),
            KeyValuePair.Create(BodyRegion.Limbs, Worn(EquipSlot.Hands, EquipSlot.Legs, EquipSlot.Feet)),
        });
        return WithBonus(armor, EffectRules.ArmorBonus(EffectsOf(_player), Setup.Effects));
    }

    internal static ImmutableSortedDictionary<BodyRegion, int> WithBonus(ImmutableSortedDictionary<BodyRegion, int> armor, int bonus) =>
        bonus == 0 ? armor : Enum.GetValues<BodyRegion>().ToImmutableSortedDictionary(r => r, r => armor.GetValueOrDefault(r) + bonus);

    // ── harm that is not a blow ─────────────────────────────────────────────

    public string? Handle(Harm command, long tick)
    {
        if (command.Amount <= 0)
            return null;
        if (command.Target != _player)
            return _context.Dispatch(new HarmCreature(command.Target, command.Source, command.Amount));
        int health = LosePlayerHealth(command.Amount, command.Source, command.Source, tick);
        _context.Events.Publish(new HealthChanged(_player, command.Source, -command.Amount, health, tick));
        return null;
    }

    public string? Handle(Heal command, long tick)
    {
        if (command.Amount <= 0)
            return null;
        if (command.Target != _player)
            return _context.Dispatch(new HealCreature(command.Target, command.Source, command.Amount));
        if (State.PlayerCombat.Defeated)
            return "the dead do not heal";
        _context.Dispatch(new ChangePools(command.Amount, 0));
        _context.Events.Publish(new HealthChanged(_player, command.Source, command.Amount, Health(), tick));
        return null;
    }

    public string? Handle(EndFight command)
    {
        State.SetPlayerCombat(_owner, PlayerCombat.Rested);
        return null;
    }

    private int LosePlayerHealth(int amount, string attackerDefId, string source, long tick)
    {
        var combat = State.PlayerCombat;
        if (combat.Defeated)
            return 0;
        if (amount > 0)
            _context.Dispatch(new ChangePools(-amount, 0));
        int health = Health();
        var recent = combat.Recent.Add(new DeathRecapLine(attackerDefId, source, amount, tick));
        if (recent.Length > PlayerCombat.RecapLength)
            recent = recent.RemoveRange(0, recent.Length - PlayerCombat.RecapLength);
        State.SetPlayerCombat(_owner, State.PlayerCombat with { Recent = recent, LastCombat = tick, Defeated = health == 0 });
        return health;
    }

    // ── stamina and recovery ────────────────────────────────────────────────

    private void Exert(int stamina, long tick)
    {
        if (stamina > 0)
            _context.Dispatch(new ChangePools(0, -stamina));
        State.SetPlayerCombat(_owner, State.PlayerCombat with { LastExertion = tick });
    }

    /// <summary>
    /// Every tick: sprinting spends stamina; after a pause it returns, slowed by effects and stopped by a raised guard;
    /// out of combat, health slowly returns.
    /// </summary>
    private void Vitals(long tick)
    {
        var combat = State.PlayerCombat;
        if (combat.Defeated)
            return;
        int stamina = Stamina(), maxStamina = MaxStamina(), health = Health(), maxHealth = MaxHealth();
        var intent = _intent();
        var (phase, _) = combat.Action.PhaseAt(tick, C);
        int staminaDelta = 0, healthDelta = 0;
        var next = combat;
        if (intent.IsMoving && intent.Gait == Gait.Sprint && phase == CombatPhase.Idle && !combat.Blocking)
        {
            // Holding a sprint is exertion even on an empty pool: nothing returns until the legs rest.
            int milli = stamina > 0 ? combat.SprintMilli + C.SprintStaminaPerSecond * TickMs : 0;
            staminaDelta -= milli / 1000;
            next = next with { SprintMilli = milli % 1000, LastExertion = tick };
        }
        else if (!combat.Blocking && stamina < maxStamina && tick - combat.LastExertion >= C.StaminaRegenDelayTicks)
        {
            double rate = C.StaminaRegenPerSecond * EffectRules.StaminaRegenMultiplier(EffectsOf(_player), Setup.Effects);
            int milli = combat.StaminaMilli + (int)Math.Round(rate * TickMs, MidpointRounding.AwayFromZero);
            staminaDelta += milli / 1000;
            next = next with { StaminaMilli = milli % 1000 };
        }
        if (health < maxHealth && tick - combat.LastCombat >= C.OutOfCombatTicks)
        {
            int milli = combat.HealthMilli + C.HealthRegenPerSecond * TickMs;
            healthDelta += milli / 1000;
            next = next with { HealthMilli = milli % 1000 };
        }
        // After a pause from the last working, Focus returns and Strain ebbs.
        int focusDelta = 0, strainDelta = 0;
        if (Focus() < MaxFocus() && tick - combat.LastCast >= M.FocusRegenDelayTicks)
        {
            int milli = combat.FocusMilli + M.FocusRegenPerSecond * TickMs;
            focusDelta += milli / 1000;
            next = next with { FocusMilli = milli % 1000 };
        }
        if (Strain() > 0 && tick - combat.LastCast >= M.StrainRecoveryDelayTicks)
        {
            int milli = combat.StrainMilli + M.StrainRecoveryPerSecond * TickMs;
            strainDelta -= milli / 1000;
            next = next with { StrainMilli = milli % 1000 };
        }
        if (staminaDelta != 0 || healthDelta != 0 || focusDelta != 0 || strainDelta != 0)
            _context.Dispatch(new ChangePools(healthDelta, staminaDelta) { Focus = focusDelta, Strain = strainDelta });
        if (next != combat)
            State.SetPlayerCombat(_owner, next);
    }

    // ── reads ───────────────────────────────────────────────────────────────

    private DerivedStats Stats() => ProgressionEngine.Derive(State.Progression, _context.Setup.Progression);

    public int MaxHealth() => (int)Stats().HealthMax;

    public int MaxStamina() => (int)Stats().StaminaMax;

    public int Health() => State.Progression.Pools.Health ?? MaxHealth();

    public int Stamina() => State.Progression.Pools.Stamina ?? MaxStamina();

    private ImmutableArray<ActiveEffect> EffectsOf(EntityId body) => State.Effects.GetValueOrDefault(body, ImmutableArray<ActiveEffect>.Empty);

    /// <summary>The rolls of one blow, keyed by who struck whom on which tick: the same on a replay and after a load.</summary>
    private Func<uint, double> Random(string attacker, string target, Body at, long tick)
    {
        var channel = RngChannel.Open(State.World.WorldSeed, CellKey.OfWorld(at.XMm / 1000.0, at.ZMm / 1000.0), "combat", $"{attacker}>{target}@{tick}");
        return sample => channel.UInt64(sample) / SampleScale;
    }

    public CombatView View()
    {
        var combat = State.PlayerCombat;
        var (phase, left) = combat.Action.PhaseAt(State.WorldTick, C);
        return new CombatView(phase, left, combat.Action.Attack?.Source, combat.Blocking, PlayerAttack(), Health(), MaxHealth(), Stamina(),
            MaxStamina(), EffectsOf(_player))
        {
            Focus = Focus(),
            MaxFocus = MaxFocus(),
            Strain = Strain(),
            StrainTolerance = StrainTolerance(),
            Strained = MagicRules.Strained(Strain(), StrainTolerance(), M),
            Casting = combat.Action.Kind == ActionKind.Cast && phase != CombatPhase.Idle ? combat.Action.Formula!.Id : null,
        };
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.Effects"/> - the status effects on every combatant (S-11). Applies and refreshes them, ticks
/// their damage and healing into <see cref="CombatSystem"/>, and expires them. The player's are saved (schema 7).
/// </summary>
internal sealed class StatusEffectSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public StatusEffectSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    private IReadOnlyDictionary<string, EffectDefinition> Definitions => _context.Setup.Combat.Effects;

    /// <summary>A loaded character's effects resume where the save left them: their deadlines are world ticks.</summary>
    public void Seed(EntityId body, ImmutableArray<ActiveEffect> effects)
    {
        if (!effects.IsEmpty)
            _context.State.SetEffects(_owner, body, effects);
    }

    public string? Handle(ApplyEffect command, long tick)
    {
        if (!Definitions.TryGetValue(command.EffectId, out var definition))
            return $"{command.EffectId} is not an effect this build knows";
        // DATA_MODEL.md §4.8 immunity_tags: a bloodless body does not bleed.
        if (_context.State.Creatures.Values.FirstOrDefault(c => c.Id == command.Target) is { } creature && definition.ImmuneTags.Overlaps(creature.Definition.Tags))
            return $"{creature.Definition.Id} is immune to {definition.Id}";
        var current = _context.State.Effects.GetValueOrDefault(command.Target, ImmutableArray<ActiveEffect>.Empty);
        var next = EffectRules.Apply(current, definition, tick);
        _context.State.SetEffects(_owner, command.Target, next);
        var applied = next.Single(e => e.EffectId == definition.Id);
        _context.Events.Publish(new EffectApplied(command.Target, definition.Id, applied.Stacks, applied.ExpiresTick, tick));
        return null;
    }

    /// <summary>Lift one effect: a mending that stops a bleed (M3e).</summary>
    public string? Handle(RemoveEffect command, long tick)
    {
        var current = _context.State.Effects.GetValueOrDefault(command.Target, ImmutableArray<ActiveEffect>.Empty);
        if (!current.Any(e => e.EffectId == command.EffectId))
            return null;
        _context.State.SetEffects(_owner, command.Target, current.RemoveAll(e => e.EffectId == command.EffectId));
        _context.Events.Publish(new EffectExpired(command.Target, command.EffectId, tick));
        return null;
    }

    public string? Handle(ClearEffects command, long tick)
    {
        var current = _context.State.Effects.GetValueOrDefault(command.Target, ImmutableArray<ActiveEffect>.Empty);
        if (current.IsEmpty)
            return null;
        _context.State.SetEffects(_owner, command.Target, ImmutableArray<ActiveEffect>.Empty);
        foreach (var effect in current)
            _context.Events.Publish(new EffectExpired(command.Target, effect.EffectId, tick));
        return null;
    }

    public void Tick(long tick)
    {
        foreach (var target in _context.State.Effects.Keys.ToList())
        {
            var kept = new List<ActiveEffect>();
            foreach (var effect in _context.State.Effects.GetValueOrDefault(target, ImmutableArray<ActiveEffect>.Empty))
            {
                var definition = Definitions.GetValueOrDefault(effect.EffectId);
                var current = effect;
                if (definition is { TickIntervalTicks: > 0 } && current.NextTickAt <= tick && current.NextTickAt <= current.ExpiresTick)
                {
                    if (definition.DamagePerTick > 0)
                        _context.Dispatch(new Harm(target, definition.Id, definition.DamagePerTick * current.Stacks));
                    if (definition.HealPerTick > 0)
                        _context.Dispatch(new Heal(target, definition.Id, definition.HealPerTick * current.Stacks));
                    current = current with { NextTickAt = current.NextTickAt + definition.TickIntervalTicks };
                }
                if (definition is null || current.ExpiresTick <= tick)
                {
                    _context.Events.Publish(new EffectExpired(target, effect.EffectId, tick));
                    continue;
                }
                kept.Add(current);
            }
            // A tick's harm can end a creature (its effects cleared) or the player (the death system clears theirs).
            if (_context.State.Effects.ContainsKey(target))
                _context.State.SetEffects(_owner, target, kept.ToImmutableArray());
        }
    }
}

/// <summary>
/// Owns no state. Settles a death at the end of the tick it happened in (PROTOTYPE.md §5 step 7): XP debt, never lost
/// XP (AG-8); the effects of the fight cleared; respawn at the outpost; a spell of weakness. It asks each owner in turn.
/// </summary>
internal sealed class DeathSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;

    public DeathSystem(SystemContext context, EntityId player)
    {
        _context = context;
        _player = player;
    }

    public void Tick(long tick)
    {
        var state = _context.State;
        var combat = state.PlayerCombat;
        if (!combat.Defeated)
            return;
        var last = combat.Recent.LastOrDefault();
        long debtBefore = state.Progression.XpDebt;
        _context.Dispatch(new RecordDeath());
        _context.Events.Publish(new PlayerDied(last?.AttackerDefId ?? "unknown", last?.Source ?? "unknown", combat.Recent,
            state.Progression.XpDebt - debtBefore, tick));
        _context.Dispatch(new ClearEffects(_player));
        var spawn = _context.Setup.Layout.Spawn;
        var body = new Body(spawn.XMm, spawn.YMm, spawn.ZMm, spawn.FacingMdeg);
        _context.Dispatch(new Relocate(body));
        _context.Dispatch(new EndFight());
        _context.Dispatch(new ForgetPlayer());
        if (_context.Setup.Combat.Effects.ContainsKey(_context.Setup.Combat.Constants.DeathEffect))
            _context.Dispatch(new ApplyEffect(_player, _context.Setup.Combat.Constants.DeathEffect));
        _context.Events.Publish(new PlayerRespawned(body, tick));
    }
}
