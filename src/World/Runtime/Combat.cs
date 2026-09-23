// UNNAMED World - combat at run time: attacks, the guard, dodging, stamina, creature combatants, status effects and
// death (SYSTEMS.md S-06, S-07, S-11, S-12; COMBAT_DAMAGE_ARMOR_AND_DEATH.md; PROTOTYPE.md §5 steps 6-7; M3c)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Where creatures stand when a world starts (DATA_MODEL.md §4.17's spawner, M3c's subset). Respawn, population state
/// and its persistence are M3d's; until then every start places each site's full count.
/// </summary>
public sealed record SpawnSite(string Key, string CreatureId, int Count, long XMm, long ZMm, long RadiusMm);

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

public sealed record ItemUsed(EntityId Actor, string DefId, string EffectId, long Tick);

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

public sealed record CreatureView(EntityId Id, string DefId, Body Body, int Health, int MaxHealth, CombatPhase Phase, int PhaseTicksLeft,
    bool Hostile, bool Alive);

/// <summary>The player in combat: what they are doing, with what, and their pools.</summary>
public sealed record CombatView(CombatPhase Phase, int PhaseTicksLeft, string? AttackSource, bool Blocking, AttackProfile Weapon,
    int Health, int MaxHealth, int Stamina, int MaxStamina, ImmutableArray<ActiveEffect> Effects);

// ── state (internal) ────────────────────────────────────────────────────────

internal enum ActionKind
{
    Idle,
    Attack,
    Dodge,
    Staggered,
}

/// <summary>What a body is doing, from which tick. The phases follow from the start tick; nothing counts down.</summary>
internal sealed record ActionState(ActionKind Kind, long StartTick, AttackProfile? Attack, int DirXPermille, int DirZPermille, ImmutableHashSet<EntityId> Struck)
{
    public static ActionState Idle { get; } = new(ActionKind.Idle, 0, null, 0, 0, ImmutableHashSet<EntityId>.Empty);

    public static ActionState Begin(ActionKind kind, long tick, AttackProfile? attack = null, int dirX = 0, int dirZ = 0) =>
        new(kind, tick, attack, dirX, dirZ, ImmutableHashSet<EntityId>.Empty);

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
                if (elapsed <= constants.StaggerTicks)
                    return (CombatPhase.Staggered, (int)(constants.StaggerTicks - elapsed));
                break;
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
}

/// <summary>
/// A creature combatant. <see cref="Key"/> (<c>spawn#index</c>) is its stable name: its identity and every roll it makes
/// derive from it, so a replay meets the same wolves. Transient in M3c; M3d saves creature state.
/// </summary>
internal sealed record CreatureState(EntityId Id, string Key, string SpawnKey, CreatureDefinition Definition, long HomeXMm, long HomeZMm,
    Body Body, int Health, ActionState Action, long StaggerImmuneUntil, EntityId? Target, bool Alive);

// ── internal commands ───────────────────────────────────────────────────────

/// <summary>To <see cref="ProgressionSystem"/>: move the current pools, clamped to their derived maxima.</summary>
internal sealed record ChangePools(int Health, int Stamina) : InternalCommand;

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

/// <summary>To <see cref="CombatSystem"/>: the player respawned; the fight is over for everyone in it.</summary>
internal sealed record EndFight : InternalCommand;

/// <summary>To <see cref="InventorySystem"/>: spend carried items by definition (an arrow at release).</summary>
internal sealed record ConsumeItem(string DefId, int Count) : InternalCommand;

// ── systems ─────────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Combat"/> - the player's combat state and every creature combatant. The one damage
/// pipeline (S-12): every blow resolves through <see cref="CombatRules.Resolve"/> here, and every loss of health -
/// blows and effect ticks alike - is applied here, where death is noticed. Creatures act only in tier-A cells. Until
/// M3d brings perception, a creature knows only what it has felt: it turns on whoever wounds it, and gives up past its
/// leash. There is no shared threat table and no awareness at a distance (STEALTH_DETECTION_AND_THREAT.md §1, §6).
/// </summary>
internal sealed class CombatSystem
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

    /// <summary>World start: every spawn site's creatures, placed where they fit, from rolls keyed by their names.</summary>
    public void Populate()
    {
        var space = _context.Setup.Layout.Space;
        foreach (var site in Setup.Spawns)
        {
            var definition = Setup.Creatures[site.CreatureId];
            var cell = CellKey.OfWorld(site.XMm / 1000.0, site.ZMm / 1000.0);
            for (int i = 0; i < site.Count; i++)
            {
                string key = $"{site.Key}#{i}";
                var channel = RngChannel.Open(State.World.WorldSeed, cell, "spawn", key);
                var others = State.Creatures.Values.Select(c => (Blocker)new CircleBlocker(c.Key, c.Body.XMm, c.Body.ZMm, c.Definition.RadiusMm, 0)).ToList();
                (long x, long z) = (site.XMm, site.ZMm);
                for (uint sample = 0; sample < 32; sample += 2)
                {
                    double angle = channel.UInt64(sample) / SampleScale * 2 * Math.PI;
                    double distance = Math.Sqrt(channel.UInt64(sample + 1) / SampleScale) * site.RadiusMm;
                    long cx = site.XMm + (long)Math.Round(Math.Sin(angle) * distance), cz = site.ZMm + (long)Math.Round(Math.Cos(angle) * distance);
                    if (Kinematics.IsClear(cx, cz, definition.RadiusMm, space, others))
                    {
                        (x, z) = (cx, cz);
                        break;
                    }
                }
                int facing = (int)(channel.UInt64(40) % 360_000);
                var body = new Body(x, space.Terrain.HeightAtMm(x, z), z, facing);
                State.SetCreature(_owner, new CreatureState(CreatureIdentity(key), key, site.Key, definition, x, z, body, definition.MaxHealth,
                    ActionState.Idle, 0, null, true));
            }
        }
    }

    /// <summary>A creature's identity from its stable name: the same wolf is the same ID on every start.</summary>
    private static EntityId CreatureIdentity(string key)
    {
        using var h = new CanonicalHasher();
        return EntityId.Create(EntityKind.Creature, 1, h.Add("unnamed.creature/v1").Add(key).FinishBytes().AsSpan(0, 10));
    }

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
        Exert(C.DodgeStaminaCost, tick);
        State.SetPlayerCombat(_owner, State.PlayerCombat with { Action = ActionState.Begin(ActionKind.Dodge, tick, dirX: x, dirZ: z), Blocking = false });
        return null;
    }

    private string? Busy(ActionState action, long tick) => action.PhaseAt(tick, C).Phase switch
    {
        CombatPhase.Idle => State.PlayerCombat.Defeated ? "dead" : null,
        CombatPhase.Staggered => "staggered",
        CombatPhase.Dodge => "dodging",
        var phase => $"already attacking ({phase.ToString().ToLowerInvariant()})",
    };

    /// <summary>What the player attacks with: the main-hand weapon, or bare hands.</summary>
    public AttackProfile PlayerAttack()
    {
        if (State.Equipment.TryGetValue(EquipSlot.MainHand, out var id) && State.Inventory.FirstOrDefault(e => e.ItemId == id) is { } entry
            && _context.Setup.Items.Catalog.Find(entry.DefId)?.Weapon is { } weapon)
            return WeaponAttack(entry.DefId, weapon);
        return C.Unarmed;
    }

    /// <summary>A weapon's attack in ticks: a swing splits into windup, active and recovery; a bow draws, releases once, and nocks.</summary>
    private AttackProfile WeaponAttack(string defId, WeaponStats weapon)
    {
        int stamina = weapon.StaminaCost ?? C.DefaultStaminaCost;
        if (weapon.Ranged)
        {
            return new AttackProfile(defId, weapon.DamageMin, weapon.DamageMax, weapon.DamageType, C.RangedRangeMm,
                Math.Max(1, Ticks(weapon.DrawMs)), 1, C.BowRecoveryTicks, stamina)
            {
                Ranged = true,
                AmmoDefId = weapon.AmmoDefId,
                SkillId = weapon.SkillId,
            };
        }
        int total = Math.Max(3, Ticks(weapon.AttackMs));
        int windup = Math.Max(1, (int)Math.Round(total * C.WindupPercent / 100.0, MidpointRounding.AwayFromZero));
        int active = Math.Max(1, (int)Math.Round(total * C.ActivePercent / 100.0, MidpointRounding.AwayFromZero));
        return new AttackProfile(defId, weapon.DamageMin, weapon.DamageMax, weapon.DamageType, weapon.ReachMm, windup, active,
            Math.Max(0, total - windup - active), stamina) { SkillId = weapon.SkillId };
    }

    private int Ticks(int milliseconds) => (int)Math.Round((double)milliseconds / TickMs, MidpointRounding.AwayFromZero);

    // ── the tick ────────────────────────────────────────────────────────────

    public void Tick(long tick)
    {
        PlayerTick(tick);
        foreach (var key in State.Creatures.Keys.ToList())
            CreatureTick(key, tick);
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

    private void PlayerHits(CreatureState creature, AttackProfile attack, long tick)
    {
        var definition = creature.Definition;
        var defense = new DefenseProfile(definition.MaxHealth, WithBonus(definition.Armor, EffectRules.ArmorBonus(EffectsOf(creature.Id), Setup.Effects)),
            definition.Resistances, Blocking: false, Dodging: false);
        var result = CombatRules.Resolve(PlayerStrike(attack), defense, C, Random("player", creature.Key, creature.Body, tick));
        int health = Math.Max(0, creature.Health - result.Final);
        var action = creature.Action;
        long immune = creature.StaggerImmuneUntil;
        bool staggered = result.Staggered && health > 0 && tick >= immune;
        if (staggered)
        {
            action = ActionState.Begin(ActionKind.Staggered, tick);
            immune = tick + C.StaggerImmunityTicks;
        }
        // It felt the wound, so it knows who dealt it (M3c's whole perception; M3d replaces this with S-23's senses).
        var updated = creature with { Health = health, Action = action, StaggerImmuneUntil = immune, Target = _player };
        State.SetCreature(_owner, updated);
        State.SetPlayerCombat(_owner, State.PlayerCombat with { LastCombat = tick });
        _context.Events.Publish(new HitResolved(_player, "player", creature.Id, attack.Source, result.Region, result.Final,
            result.Critical, result.Blocked, result.Dodged, staggered, health, tick));
        if (result.EffectApplied && attack.OnHitEffect is { } effect && health > 0)
            _context.Dispatch(new ApplyEffect(creature.Id, effect));
        // Weapon skill only from effective contribution: a blow that wounded something that fights back (ROADMAP.md M3c).
        if (attack.SkillId is { } skill && result.Final > 0)
            _context.Dispatch(new PracticeSkill(new SkillPractice(skill, definition.Level, PracticeOutcome.Success, tick)));
        if (health == 0)
            Kill(updated, tick);
    }

    /// <summary>What the player brings to a blow: Might for physical blows, effects such as weakness, and skill passives.</summary>
    private Strike PlayerStrike(AttackProfile attack)
    {
        var rules = _context.Setup.Progression;
        double multiplier = EffectRules.DamageDealtMultiplier(EffectsOf(_player), Setup.Effects);
        if (DamageTypes.IsPhysical(attack.DamageType))
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

    private void Kill(CreatureState creature, long tick)
    {
        State.SetCreature(_owner, creature with { Health = 0, Alive = false, Action = ActionState.Idle, Target = null });
        _context.Dispatch(new ClearEffects(creature.Id));
        _context.Events.Publish(new CreatureKilled(creature.Id, creature.Definition.Id, _player, tick));
        var definition = creature.Definition;
        if (definition.XpValue > 0)
        {
            _context.Dispatch(new AwardExperience(new XpAward(XpSource.Combat, definition.XpValue, tick)
            {
                Kill = new KillContext(definition.Id, definition.Level, creature.SpawnKey),
            }));
        }
    }

    // ── creatures ───────────────────────────────────────────────────────────

    private void CreatureTick(EntityId id, long tick)
    {
        var creature = State.Creatures[id];
        if (!creature.Alive || TierOf(creature.Body) != SimulationTier.A)
            return;
        var (phase, _) = creature.Action.PhaseAt(tick, C);
        if (creature.Action.Kind != ActionKind.Idle && phase == CombatPhase.Idle)
            creature = creature with { Action = ActionState.Idle };

        var body = State.Body;
        var attack = creature.Definition.Attack;
        switch (phase)
        {
            case CombatPhase.Staggered or CombatPhase.Recovery:
                break;
            case CombatPhase.Windup:
                creature = creature with { Body = creature.Body with { FacingMdeg = CombatRules.FacingTowards(creature.Body.XMm, creature.Body.ZMm, body.XMm, body.ZMm) } };
                break;
            case CombatPhase.Active:
                creature = CreatureStrikes(creature, tick);
                break;
            default:
                creature = Decide(creature, tick);
                break;
        }
        State.SetCreature(_owner, creature);
    }

    /// <summary>At rest: chase and bite what wounded it, or give up past the leash and go home.</summary>
    private CreatureState Decide(CreatureState creature, long tick)
    {
        var body = State.Body;
        var definition = creature.Definition;
        if (creature.Target is not null && (State.PlayerCombat.Defeated || Distance(creature.HomeXMm, creature.HomeZMm, body.XMm, body.ZMm) > C.LeashMm))
            creature = creature with { Target = null };

        if (creature.Target is null)
        {
            if (Distance(creature.Body.XMm, creature.Body.ZMm, creature.HomeXMm, creature.HomeZMm) <= 1_000)
                return creature;
            return creature with { Body = Move(creature, creature.HomeXMm, creature.HomeZMm, Gait.Walk) };
        }

        long reach = definition.Attack.ReachMm + _context.Setup.Movement.BodyRadiusMm;
        double distance = Distance(creature.Body.XMm, creature.Body.ZMm, body.XMm, body.ZMm);
        if (distance <= reach - 100 && !Walled(creature.Body.XMm, creature.Body.ZMm, body.XMm, body.ZMm))
        {
            var facing = CombatRules.FacingTowards(creature.Body.XMm, creature.Body.ZMm, body.XMm, body.ZMm);
            _context.Events.Publish(new AttackStarted(creature.Id, definition.Attack.Source, definition.Attack.WindupTicks,
                definition.Attack.ActiveTicks, definition.Attack.RecoveryTicks, tick));
            return creature with { Body = creature.Body with { FacingMdeg = facing }, Action = ActionState.Begin(ActionKind.Attack, tick, definition.Attack) };
        }
        return creature with { Body = Move(creature, body.XMm, body.ZMm, Gait.Run) };
    }

    private Body Move(CreatureState creature, long toXMm, long toZMm, Gait gait)
    {
        var from = creature.Body;
        double dx = toXMm - from.XMm, dz = toZMm - from.ZMm;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return from;
        var intent = new MoveIntent((int)Math.Round(dx / length * 1000), (int)Math.Round(dz / length * 1000), gait,
            CombatRules.FacingTowards(from.XMm, from.ZMm, toXMm, toZMm));
        var rules = new MovementRules(creature.Definition.MoveSpeedMmPerSecond, 50, 100, creature.Definition.RadiusMm, 0);
        var others = new List<Blocker>(_context.ClosedDoors())
        {
            new CircleBlocker("player", State.Body.XMm, State.Body.ZMm, _context.Setup.Movement.BodyRadiusMm, 0),
        };
        others.AddRange(State.Creatures.Values.Where(c => c.Alive && c.Id != creature.Id)
            .Select(c => (Blocker)new CircleBlocker(c.Key, c.Body.XMm, c.Body.ZMm, c.Definition.RadiusMm, 0)));
        return Kinematics.Step(from, intent, rules, _context.Setup.Layout.Space, others, TickMs);
    }

    private CreatureState CreatureStrikes(CreatureState creature, long tick)
    {
        var action = creature.Action;
        var attack = action.Attack!;
        long elapsed = tick - action.StartTick;
        var combat = State.PlayerCombat;
        var body = State.Body;
        bool reaches = !action.Struck.Contains(_player) && !combat.Defeated
            && CombatRules.InFront(creature.Body.XMm, creature.Body.ZMm, creature.Body.FacingMdeg, body.XMm, body.ZMm,
                attack.ReachMm + _context.Setup.Movement.BodyRadiusMm, C.MeleeArcMdeg)
            && !Walled(creature.Body.XMm, creature.Body.ZMm, body.XMm, body.ZMm);
        if (!reaches)
        {
            if (action.Struck.IsEmpty && elapsed == attack.WindupTicks + attack.ActiveTicks)
                _context.Events.Publish(new AttackMissed(creature.Id, attack.Source, tick));
            return creature;
        }

        // The guard holds only against what is in front of it, and only while there is stamina behind it.
        bool guarding = combat.Blocking
            && CombatRules.InFront(body.XMm, body.ZMm, body.FacingMdeg, creature.Body.XMm, creature.Body.ZMm, long.MaxValue / 4, C.BlockArcMdeg);
        if (guarding && Stamina() < C.BlockStaminaPerHit)
        {
            guarding = false;
            combat = combat with { Blocking = false };
            State.SetPlayerCombat(_owner, combat);
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
        bool staggered = result.Staggered && health > 0 && StaggerPlayer(tick, force: false);
        _context.Events.Publish(new HitResolved(creature.Id, creature.Definition.Id, _player, attack.Source, result.Region, result.Final,
            result.Critical, result.Blocked, result.Dodged, staggered, health, tick));
        if (result.EffectApplied && attack.OnHitEffect is { } effect && health > 0)
            _context.Dispatch(new ApplyEffect(_player, effect));
        return creature with { Action = action with { Struck = action.Struck.Add(_player) } };
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

    private static ImmutableSortedDictionary<BodyRegion, int> WithBonus(ImmutableSortedDictionary<BodyRegion, int> armor, int bonus) =>
        bonus == 0 ? armor : Enum.GetValues<BodyRegion>().ToImmutableSortedDictionary(r => r, r => armor.GetValueOrDefault(r) + bonus);

    // ── health: the one place it falls ──────────────────────────────────────

    public string? Handle(Harm command, long tick)
    {
        if (command.Amount <= 0)
            return null;
        if (command.Target == _player)
        {
            int health = LosePlayerHealth(command.Amount, command.Source, command.Source, tick);
            _context.Events.Publish(new HealthChanged(_player, command.Source, -command.Amount, health, tick));
            return null;
        }
        if (State.Creatures.GetValueOrDefault(command.Target) is not { Alive: true } creature)
            return null;
        int left = Math.Max(0, creature.Health - command.Amount);
        var updated = creature with { Health = left };
        State.SetCreature(_owner, updated);
        _context.Events.Publish(new HealthChanged(creature.Id, command.Source, -command.Amount, left, tick));
        if (left == 0)
            Kill(updated, tick);   // in Phase 1 every effect on a creature is the player's doing
        return null;
    }

    public string? Handle(Heal command, long tick)
    {
        if (command.Amount <= 0)
            return null;
        if (command.Target == _player)
        {
            if (State.PlayerCombat.Defeated)
                return "the dead do not heal";
            _context.Dispatch(new ChangePools(command.Amount, 0));
            _context.Events.Publish(new HealthChanged(_player, command.Source, command.Amount, Health(), tick));
            return null;
        }
        if (State.Creatures.GetValueOrDefault(command.Target) is not { Alive: true } creature)
            return null;
        var healed = creature with { Health = Math.Min(creature.Definition.MaxHealth, creature.Health + command.Amount) };
        State.SetCreature(_owner, healed);
        _context.Events.Publish(new HealthChanged(creature.Id, command.Source, command.Amount, healed.Health, tick));
        return null;
    }

    public string? Handle(EndFight command)
    {
        State.SetPlayerCombat(_owner, PlayerCombat.Rested);
        foreach (var creature in State.Creatures.Values.Where(c => c.Target == _player).ToList())
            State.SetCreature(_owner, creature with { Target = null, Action = ActionState.Idle });
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
        if (staminaDelta != 0 || healthDelta != 0)
            _context.Dispatch(new ChangePools(healthDelta, staminaDelta));
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

    private SimulationTier TierOf(Body body) =>
        State.Tiers.GetValueOrDefault(CellKey.OfWorld(body.XMm / 1000.0, body.ZMm / 1000.0).ToString(), SimulationTier.D);

    private static double Distance(long x0, long z0, long x1, long z1)
    {
        double dx = x1 - x0, dz = z1 - z0;
        return Math.Sqrt(dx * dx + dz * dz);
    }

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
            MaxStamina(), EffectsOf(_player));
    }

    public ImmutableArray<CreatureView> Creatures() =>
        State.Creatures.Values.Select(c =>
        {
            var (phase, left) = c.Action.PhaseAt(State.WorldTick, C);
            return new CreatureView(c.Id, c.Definition.Id, c.Body, c.Health, c.Definition.MaxHealth, phase, left, c.Target is not null, c.Alive);
        }).ToImmutableArray();

    /// <summary>Living creatures' bodies, which the player cannot walk through.</summary>
    public IEnumerable<Blocker> CreatureBlockers() =>
        State.Creatures.Values.Where(c => c.Alive).Select(c => (Blocker)new CircleBlocker(c.Key, c.Body.XMm, c.Body.ZMm, c.Definition.RadiusMm, 0));
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
        var current = _context.State.Effects.GetValueOrDefault(command.Target, ImmutableArray<ActiveEffect>.Empty);
        var next = EffectRules.Apply(current, definition, tick);
        _context.State.SetEffects(_owner, command.Target, next);
        var applied = next.Single(e => e.EffectId == definition.Id);
        _context.Events.Publish(new EffectApplied(command.Target, definition.Id, applied.Stacks, applied.ExpiresTick, tick));
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
        if (_context.Setup.Combat.Effects.ContainsKey(_context.Setup.Combat.Constants.DeathEffect))
            _context.Dispatch(new ApplyEffect(_player, _context.Setup.Combat.Constants.DeathEffect));
        _context.Events.Publish(new PlayerRespawned(body, tick));
    }
}
