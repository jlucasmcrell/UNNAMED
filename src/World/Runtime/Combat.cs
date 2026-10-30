// UNNAMED World - combat at run time: attacks, the guard, dodging, stamina, status effects, downed and death
// (SYSTEMS.md S-06, S-07, S-11, S-12; COMBAT_DAMAGE_ARMOR_AND_DEATH.md; PROTOTYPE.md §5 steps 6-7; M3c, M3d; OD-13 = B, WP10)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
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

    /// <summary>How light, cover and posture make a body visible (STEALTH §2): what an observer's sight is scaled by, and why.</summary>
    public LightRules Light { get; init; } = new();

    /// <summary>When it is night for the creatures that rest by night or by day; null when no role keeps hours.</summary>
    public CreatureHours? Hours { get; init; }

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

/// <summary>
/// A shot left the character (the owner's M6 playtest): an arrow or a thrown working, along the facing, from the body to where it
/// stopped - the creature it struck, the first wall, or the end of its range. The blow resolved at release; presentation draws the flight.
/// </summary>
public sealed record ShotLoosed(EntityId Attacker, string Source, long FromXMm, long FromZMm, long ToXMm, long ToZMm, EntityId? Target, long Tick);

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

public sealed record CreatureKilled(EntityId Creature, string DefId, EntityId Killer, long Tick)
{
    /// <summary>The physical attacker; Killer is the owning player for their pets and party (owner ruling 2026-10-24).</summary>
    public EntityId DirectAttacker { get; init; } = Killer;
}

/// <summary>One of the blows before a death, for the recap: who, with what, how much.</summary>
public sealed record DeathRecapLine(string AttackerDefId, string Source, int Damage, long Tick);

/// <summary>
/// The player died. <see cref="KillerDefId"/> and <see cref="Cause"/> are the last blow's; <see cref="Recap"/> is the
/// last few, so a tester can name what killed them (ROADMAP.md M3c). The penalty is XP debt, never lost XP (AG-8).
/// </summary>
public sealed record PlayerDied(string KillerDefId, string Cause, ImmutableArray<DeathRecapLine> Recap, long DebtAdded, long Tick, long RespawnTick);

public sealed record PlayerRespawned(Body Body, long Tick);

/// <summary>
/// The player was brought to nothing with a hand near (OD-13 = B): <see cref="HandNpcId"/>, up and close, will help them up at
/// <see cref="RevivedAtTick"/> unless they fall or leave first. <see cref="KillerDefId"/> and <see cref="Cause"/> are the last blow's.
/// </summary>
public sealed record PlayerDowned(string KillerDefId, string Cause, string HandNpcId, long Tick, long RevivedAtTick);

/// <summary>A companion or hireling helped the downed player up, with <see cref="Health"/>.</summary>
public sealed record PlayerRevived(string NpcId, int Health, long Tick);

/// <summary>A use of a skill: the XP it earned, which the difficulty gate may make zero (PROGRESSION.md §4.2), and the levels it gained.</summary>
public sealed record SkillPracticed(string SkillId, long Xp, int Level, int LevelsGained, long Tick);

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
    /// <summary>Acting, guarding, or within the existing out-of-combat delay after an exchange of blows.</summary>
    public bool InCombat { get; init; }

    public int Focus { get; init; }

    public int MaxFocus { get; init; }

    public int Strain { get; init; }

    public int StrainTolerance { get; init; }

    /// <summary>Strain at or past the Strained share of tolerance: the next working may cost health.</summary>
    public bool Strained { get; init; }

    /// <summary>The formula being cast, while the working runs.</summary>
    public string? Casting { get; init; }

    /// <summary>The character lies dead, until the respawn.</summary>
    public bool Defeated { get; init; }

    /// <summary>Ticks until the respawn (the fade to black closes on it); zero when alive, and while downed.</summary>
    public int RespawnTicksLeft { get; init; }

    /// <summary>Down but not dead (OD-13 = B): <see cref="Defeated"/> is true as well, so nothing the character does goes through.</summary>
    public bool Downed { get; init; }

    /// <summary>While downed: ticks until the hand near them helps them up.</summary>
    public int DownedTicksLeft { get; init; }
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

    /// <summary>
    /// The phase at a tick. Tick <c>StartTick + 1</c> is the action's first and <c>StartTick + TotalTicks</c> its last (Recovery, 0 left);
    /// the tick an action is taken is not one of its own, so actions asked for back to back come round every <c>TotalTicks + 1</c> - the
    /// rusted sword's 14 ticks of swing, one every 15 (the Phase-1 technical audit, L-25: documented, not changed, for the owner to rule).
    /// </summary>
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

/// <summary>
/// The player's transient combat state. Saved only in the attack under way (schema 27): a load starts at rest, or in that attack, with pools
/// and effects from the save - fallen, at none (DN15).
/// </summary>
internal sealed record PlayerCombat(ActionState Action, bool Blocking, long StaggerImmuneUntil, long LastExertion, long LastCombat,
    ImmutableArray<DeathRecapLine> Recent)
{
    public const int RecapLength = 5;

    public static PlayerCombat Rested { get; } = new(ActionState.Idle, false, 0, -1_000_000, -1_000_000, ImmutableArray<DeathRecapLine>.Empty);

    /// <summary>Health reached zero; the death system settles it at the end of the tick, then waits out the death before the respawn.</summary>
    public bool Defeated { get; init; }

    /// <summary>The tick the death ends in a respawn; zero until the death system has settled it.</summary>
    public long RespawnAt { get; init; }

    /// <summary>
    /// While downed (OD-13 = B): the tick a hand helps the character up; zero when not downed. <see cref="Defeated"/> stays true while
    /// it runs, so every system that refuses the dead refuses the downed too, and creatures leave them be.
    /// </summary>
    public long DownedUntil { get; init; }

    /// <summary>A death no hand can turn into a downing: one loaded from a save made while downed (DN15).</summary>
    public bool PastHelp { get; init; }

    /// <summary>Thousandths of a point accrued by sprinting, regeneration and recovery, so integer pools move smoothly.</summary>
    public int SprintMilli { get; init; }
    public int StaminaMilli { get; init; }
    public int HealthMilli { get; init; }
    public int FocusMilli { get; init; }
    public int StrainMilli { get; init; }

    /// <summary>Thousandths of a point spent swimming: the sprint's own accumulator, for the water (D20).</summary>
    public int SwimMilli { get; init; }

    /// <summary>The last working begun or released: Focus and Strain return only after a pause from it.</summary>
    public long LastCast { get; init; } = -1_000_000;

    /// <summary>The tick each technique used comes ready again (WP9). Transient, as cooldowns are (SYSTEMS.md S-10): a load starts them ready.</summary>
    public ImmutableSortedDictionary<string, long> TechniquesReadyAt { get; init; } = ImmutableSortedDictionary.Create<string, long>(StringComparer.Ordinal);
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

/// <summary>To <see cref="CombatSystem"/>: a death is settled; the player lies down until this tick.</summary>
internal sealed record AwaitRespawn(long At) : InternalCommand;

/// <summary>To <see cref="CombatSystem"/>: the player lies downed until <see cref="Until"/>; zero helps them up.</summary>
internal sealed record LieDowned(long Until) : InternalCommand;

/// <summary>To <see cref="InventorySystem"/>: spend carried items by definition (an arrow at release).</summary>
internal sealed record ConsumeItem(string DefId, int Count) : InternalCommand;

/// <summary>
/// Throw something that makes a noise where it lands (STEALTH §3.2): a stone, a bottle, a coin. The landing point is the lure - an observer
/// drawn by it walks to <em>there</em>, never to the character - and the ground it lands on decides how far the sound carries (§3.1).
/// </summary>
public sealed record ThrowCommand(EntityId Actor, EntityId Item, long DirXPermille = 0, long DirZPermille = 0) : GameCommand(Actor);

/// <summary>Something was thrown, from the body to the place it came down: presentation draws the arc, and the ear was given a place to look.</summary>
public sealed record Thrown(EntityId Actor, string DefId, long FromXMm, long FromZMm, long ToXMm, long ToZMm, long Tick);

/// <summary>A body was put down without a wound: it sleeps where it stood, and wakes fighting if anything wounds it (STEALTH §5.2).</summary>
public sealed record Subdued(EntityId Actor, EntityId Target, string DefId, long Tick);

/// <summary>A body was carried into cover: presentation moves the corpse, and the ground it lies on stops telling anyone anything.</summary>
public sealed record BodyHidden(EntityId Actor, EntityId Target, string DefId, string CoverKey, long Tick);

/// <summary>A body pulled a step along the ground: where it was, where it is, and who is doing the pulling (STEALTH §6.2).</summary>
public sealed record BodyDragged(EntityId Actor, EntityId Target, string DefId, long XMm, long ZMm, long Tick);

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
        // A paralysed or sleeping body does not swing (STEALTH §14's control effects reach the character too).
        if (_context.ControlOn(_player) == ControlKind.Incapacitates)
            return "you cannot move";
        var attack = PlayerAttack();
        if (attack.Ranged && attack.AmmoDefId is { } ammo && !State.Inventory.Any(e => e.DefId == ammo))
            return $"no {ammo} to shoot";
        if (Stamina() < attack.StaminaCost)
            return "too tired to attack";
        Exert(attack.StaminaCost, tick);
        // A swing is not a fight: only a blow landed or taken marks the character as in one - or a swing at the air before each working
        // would make every working a challenged one (the Phase-1 technical audit, L-10).
        State.SetPlayerCombat(_owner, State.PlayerCombat with
        {
            Action = ActionState.Begin(ActionKind.Attack, tick, attack),
            Blocking = false,
        });
        _context.Events.Publish(new AttackStarted(_player, attack.Source, attack.WindupTicks, attack.ActiveTicks, attack.RecoveryTicks, tick));
        return null;
    }

    /// <summary>
    /// Throw a stone or a bottle where the character is looking (STEALTH §3.2). What it buys is a noise at the landing spot: the ground
    /// decides how far that carries, and every observer drawn by it walks to the stone, never to the thrower. One is spent whether it lands
    /// in the open or against a wall.
    /// </summary>
    public string? Handle(ThrowCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var entry = State.Inventory.FirstOrDefault(e => e.ItemId == command.Item);
        if (entry is null)
            return "nothing to throw";
        if (_context.Setup.Items.Catalog.Find(entry.DefId) is not { ThrowNoiseMm: > 0 } item)
            return $"{entry.DefId} is not a thing to throw";
        if (_context.ControlOn(_player) == ControlKind.Incapacitates)
            return "you cannot move";
        var body = State.Body;
        // Where the character is looking, or the direction a caller gave: the arm is the build's range, the item's own overrides it, and a wall
        // stops the throw where it stands.
        double facing = body.FacingMdeg / 1000.0 * Math.PI / 180;
        bool aimed = command.DirXPermille != 0 || command.DirZPermille != 0;
        double dirX = aimed ? command.DirXPermille / 1000.0 : Math.Sin(facing);
        double dirZ = aimed ? command.DirZPermille / 1000.0 : Math.Cos(facing);
        double length = Math.Sqrt(dirX * dirX + dirZ * dirZ);
        if (length < 0.001)
            return "throw at something";
        dirX /= length;
        dirZ /= length;
        long range = item.ThrowRangeMm > 0 ? item.ThrowRangeMm : 12_000;
        long landingX = body.XMm, landingZ = body.ZMm;
        for (long step = 1_000; step <= range; step += 1_000)
        {
            long x = body.XMm + (long)Math.Round(dirX * step), z = body.ZMm + (long)Math.Round(dirZ * step);
            if (_context.Walled(body.XMm, body.ZMm, x, z))
                break;
            landingX = x;
            landingZ = z;
        }
        _context.Dispatch(new ConsumeItem(entry.DefId, 1));
        // The ground it lands on decides how far the sound goes (§3.1), and it is heard as a device rather than a blow.
        long radius = Math.Max(1, (long)Math.Round(
            item.ThrowNoiseMm * UNNAMED.Domain.Spatial.Surfaces.Factor(
                UNNAMED.Domain.Spatial.Surfaces.At(_context.Setup.LayoutFor(State.Space).Surfaces, landingX, landingZ)),
            MidpointRounding.AwayFromZero));
        _context.Dispatch(new RaiseNoise(new UNNAMED.Domain.Creatures.Noise(landingX, landingZ, radius)
        {
            Source = UNNAMED.Domain.Creatures.SoundSource.Device,
        }));
        _context.Events.Publish(new Thrown(_player, entry.DefId, body.XMm, body.ZMm, landingX, landingZ, tick));
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
        // The dash is a standing body's, on the ground (the owner's M6 playtest).
        if (State.Posture.Airborne)
            return "cannot dodge in the air";
        if (State.Posture.Stance == Stance.Crouched)
            return "cannot dodge while crouched";
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
        CombatPhase.Idle => State.PlayerCombat.DownedUntil > 0 ? "downed" : State.PlayerCombat.Defeated ? "dead" : null,
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
                // A shot at a target that has not noticed it is the weapon's own sneak blow, as a blade's is (STEALTH §7.1).
                SneakMultiplier = weapon.SneakMultiplier,
            };
        }
        int total = Math.Max(3, Ticks(weapon.AttackMs));
        int windup = Math.Max(1, (int)Math.Round(total * constants.WindupPercent / 100.0, MidpointRounding.AwayFromZero));
        int active = Math.Max(1, (int)Math.Round(total * constants.ActivePercent / 100.0, MidpointRounding.AwayFromZero));
        return new AttackProfile(defId, weapon.DamageMin, weapon.DamageMax, weapon.DamageType, weapon.ReachMm, windup, active,
            Math.Max(0, total - windup - active), stamina) { SkillId = weapon.SkillId, SneakMultiplier = weapon.SneakMultiplier };
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
            if (Loose(attack.Source, attack.ReachMm, tick) is { } target)
                PlayerHits(target, attack, tick);
            else
                _context.Events.Publish(new AttackMissed(_player, attack.Source, tick));
            return;
        }

        var struck = action.Struck;
        foreach (var creature in State.Creatures.Values.Where(c => c.Alive && c.Site.Space == State.Space && !struck.Contains(c.Id)).ToList())
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

    /// <summary>
    /// The weapon in the main hand, or null bare-handed. The attack's <c>Source</c> is a technique's id while a technique is in flight,
    /// so a weapon's own numbers (its noise, its family) are read from the hand rather than from the blow.
    /// </summary>
    private WeaponStats? WeaponInHand() =>
        State.Equipment.TryGetValue(EquipSlot.MainHand, out var id)
        && State.Inventory.FirstOrDefault(e => e.ItemId == id) is { } entry
            ? _context.Setup.Items.Catalog.Find(entry.DefId)?.Weapon
            : null;

    /// <summary>
    /// Where a shot is heard (STEALTH §7.8): at the shooter, as far as the weapon's own crack carries - never the melee swing radius a
    /// bow never earned.
    /// </summary>
    internal long ShotNoiseMm() => WeaponInHand() is { Ranged: true, ShotNoiseMm: > 0 } weapon ? weapon.ShotNoiseMm : Setup.Noise.ShotMm;

    /// <summary>The place a technique puts its shot (STEALTH §7.2), or null for an ordinary blow.</summary>
    private BodyRegion? AimOf(AttackProfile attack) =>
        attack.Ranged && _context.Setup.Techniques.TryGetValue(attack.Source, out var technique) ? technique.AimRegion : null;

    /// <summary>
    /// A shot leaves the body along its facing: where it stops is published for presentation, and whom it strikes returned. The sound of
    /// it is the creature system's to make (STEALTH §7.8) - it owns the ear - so combat only says what happened, at the shooter and at
    /// the place it came down.
    /// </summary>
    private CreatureState? Loose(string source, long rangeMm, long tick)
    {
        var body = State.Body;
        var (x, z, target) = Trace(body, rangeMm);
        _context.Events.Publish(new ShotLoosed(_player, source, body.XMm, body.ZMm, x, z, target?.Id, tick));
        _context.Dispatch(new RaiseNoise(new Noise(body.XMm, body.ZMm, ShotNoiseMm()) { Source = SoundSource.Shot }));
        _context.Dispatch(new RaiseNoise(new Noise(x, z, target is null ? Setup.Noise.ImpactSolidMm : Setup.Noise.ImpactFleshMm)
            { Source = SoundSource.Impact }));
        return target;
    }

    /// <summary>
    /// Where a shot along the body's facing stops: on the first creature it meets, else at the first wall, else at the end of its range.
    /// Read-only, so presentation can place the aiming reticle on the same point (the owner's M6 playtest).
    /// </summary>
    public (long XMm, long ZMm, CreatureState? Target) Trace(Body from, long rangeMm)
    {
        if (RangedTarget(from, rangeMm) is { } target)
            return (target.Body.XMm, target.Body.ZMm, target);
        double facing = from.FacingMdeg / 1000.0 * Math.PI / 180;
        double dx = Math.Sin(facing), dz = Math.Cos(facing);
        double reach = rangeMm;
        if (Walled(from.XMm, from.ZMm, from.XMm + dx * reach, from.ZMm + dz * reach))
        {
            // The line crosses a wall somewhere short of the range: halve the gap down to a centimetre.
            double clear = 0;
            while (reach - clear > 10)
            {
                double mid = (clear + reach) / 2;
                if (Walled(from.XMm, from.ZMm, from.XMm + dx * mid, from.ZMm + dz * mid))
                    reach = mid;
                else
                    clear = mid;
            }
            reach = clear;
        }
        return ((long)Math.Round(from.XMm + dx * reach, MidpointRounding.AwayFromZero), (long)Math.Round(from.ZMm + dz * reach, MidpointRounding.AwayFromZero), null);
    }

    /// <summary>The first living creature along the facing within range whose body the line meets before any wall does.</summary>
    private CreatureState? RangedTarget(Body from, long rangeMm)
    {
        double facing = from.FacingMdeg / 1000.0 * Math.PI / 180;
        double dx = Math.Sin(facing), dz = Math.Cos(facing);
        CreatureState? best = null;
        double bestAlong = double.MaxValue;
        foreach (var creature in State.Creatures.Values.Where(c => c.Alive && c.Site.Space == State.Space))
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
        _context.Setup.SpaceFor(State.Space).Blockers.Concat(_context.ClosedDoors()).Where(b => State.Space.IsExterior || b.ClearanceMm < 1800).Any(b => b.Crosses(x0, z0, x1, z1));

    /// <summary>The player's blow on a creature: resolved here, applied by the creature's owner.</summary>
    private void PlayerHits(CreatureState creature, AttackProfile attack, long tick)
    {
        if (!CombatRules.CanAttemptAttack(_player, creature.Id,
                new(!State.PlayerCombat.Defeated, creature.Alive, creature.Site.Space == State.Space))) return;
        var definition = creature.Definition;
        var defense = new DefenseProfile(definition.MaxHealth, WithBonus(definition.Armor, EffectRules.ArmorBonus(EffectsOf(creature.Id), Setup.Effects)),
            definition.Resistances, Blocking: false, Dodging: false);
        var strike = CombatRules.LevelGap(PlacedStrike(creature, attack, SneakStrike(creature, attack, PlayerStrike(attack))),
            State.Progression.Level, definition.Level + creature.LevelBonus);
        // A weak point open from behind is where a blow from behind lands (COMBAT §19): position, not luck.
        if (definition.WeakPoint is { FromBehind: true } weak && Behind(creature.Body, State.Body))
            strike = strike with { ForcedRegion = weak.Region };
        var result = CombatRules.Resolve(strike, defense, C, Random("player", creature.Key, creature.Body, tick));
        State.SetPlayerCombat(_owner, State.PlayerCombat with { LastCombat = tick });
        _context.Dispatch(new WoundCreature(creature.Id, result, attack.Source));
        bool alive = State.Creatures.Values.Any(c => c.Id == creature.Id && c.Alive);
        if (result.EffectApplied && attack.OnHitEffect is { } effect && alive)
            _context.Dispatch(new ApplyEffect(creature.Id, effect));
        // A coated blade puts its poison into what it wounds (owner finding 27): the coating is an effect on the character that names the
        // effect it delivers, so alchemy adds content rather than a second damage path.
        if (result.Final > 0 && alive && Coating() is { } poison)
            _context.Dispatch(new ApplyEffect(creature.Id, poison));
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

    /// <summary>
    /// The strike a blow resolves as against this creature: a target that has not noticed the character takes the weapon's sneak multiplier
    /// (STEALTH §12 - the dagger class's own number), applied before armour so that armour still decides. A target that has noticed the
    /// character takes the ordinary blow. A ranged blow is aimed, and what the aim is worth is the body's own anatomy (§7.2); the two
    /// together are one shot and come to no more than <c>sneak_cap</c> (§7.1).
    /// </summary>
    private Strike SneakStrike(CreatureState creature, AttackProfile attack, Strike strike) =>
        ScaleShot(creature, attack, strike, StealthRules.SneakMultiplier(attack, StealthRules.Unaware(creature.Awareness, Setup.Awareness)));

    /// <summary>A shot placed where the shooter aimed it, and the zone's own worth (STEALTH §7.2).</summary>
    private Strike PlacedStrike(CreatureState creature, AttackProfile attack, Strike strike) =>
        AimOf(attack) is { } region
            ? strike with { ForcedRegion = region }
            : strike;

    /// <summary>The one multiplier a single blow earns, capped (STEALTH §7.1). A swing earns only its sneak multiplier; a shot earns
    /// that and the zone's, because a shot at a body that has not moved is a shot at a body whose gaps are open.</summary>
    private Strike ScaleShot(CreatureState creature, AttackProfile attack, Strike strike, double sneak)
    {
        double zone = AimOf(attack) is { } region
            ? WeakPointRules.Multiplier(creature.Definition, C, region, !Behind(creature.Body, State.Body))
            : 1.0;
        double total = attack.Ranged ? WeakPointRules.Capped(sneak, zone, C) : sneak;
        return total == 1.0 ? strike : strike with { DamageMultiplier = strike.DamageMultiplier * total };
    }

    /// <summary>What the character's coated weapon delivers on a wound, or null when nothing is coated (owner finding 27).</summary>
    private string? Coating() =>
        EffectsOf(_player).Select(e => Setup.Effects.GetValueOrDefault(e.EffectId)?.AppliesOnHit).FirstOrDefault(e => e is not null);

    /// <summary>
    /// A takedown (STEALTH §12-§14; owner finding 27): a dagger, a body that has not noticed the character, and a vital aimed at - the head
    /// from behind, the torso from inside reach. The owner's RC d5 ruling executes a rear ambush at Even threat or below, except bosses.
    /// Higher-level and frontal attempts retain the normal vital/armour outcome through the same combat resolver.
    /// </summary>
    public string? Handle(TakedownCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (Busy(State.PlayerCombat.Action, tick) is { } busy)
            return busy;
        var attack = PlayerAttack();
        if (!StealthRules.IsDagger(attack))
            return "a takedown needs a dagger";
        var body = State.Body;
        // Inside reach: the blade's length, a step and the target's own body on the level - and from a ledge, as far as the drop is deep, because
        // a body that comes down on someone reaches them from further up (STEALTH §5.3).
        var nearest = State.Creatures.Values
            .Where(c => c.Alive && c.Site.Space == State.Space)
            .Select(c => (Creature: c, Distance: Math.Sqrt(Math.Pow(c.Body.XMm - body.XMm, 2) + Math.Pow(c.Body.ZMm - body.ZMm, 2))))
            .Where(t => TakedownRules.InReach(attack.ReachMm + 1_500 + t.Creature.Definition.RadiusMm, body.YMm, t.Creature.Body.YMm,
                (long)t.Distance))
            .OrderBy(t => t.Distance)
            .Select(t => (CreatureState?)t.Creature)
            .FirstOrDefault();
        if (nearest is not { } creature)
            return "nothing within reach of the blade";
        if (!CombatRules.CanAttemptAttack(_player, creature.Id,
                new(!State.PlayerCombat.Defeated, creature.Alive, creature.Site.Space == State.Space))) return "attack is not permitted";
        if (!StealthRules.Unaware(creature.Awareness, Setup.Awareness) && !EffectsOf(creature.Id).Any(e => e.EffectId == "effect.sleep"))
            return "it has noticed you";
        var definition = creature.Definition;
        // A choke is not a blade (STEALTH §5.2): the body goes down asleep where it stands and wakes fighting if anything wounds it. Same reach,
        // same unaware target - only what happens at the end of it changes, which is what a route wants where a corpse would raise the street.
        if (command.NonLethal)
        {
            _context.Dispatch(new ApplyEffect(creature.Id, "effect.sleep"));
            _context.Dispatch(new PracticeSkill(new SkillPractice("skill.stealth", definition.Level, PracticeOutcome.Success, tick)));
            _context.Events.Publish(new Subdued(_player, creature.Id, definition.Id, tick));
            State.SetPlayerCombat(_owner, State.PlayerCombat with { LastCombat = tick });
            return null;
        }
        var defense = new DefenseProfile(definition.MaxHealth, WithBonus(definition.Armor, EffectRules.ArmorBonus(EffectsOf(creature.Id), Setup.Effects)),
            definition.Resistances, Blocking: false, Dodging: false);
        var fromBehind = Behind(creature.Body, body);
        bool execution = TakedownRules.Executes(true, true, fromBehind,
            ThreatRules.Of(definition.Level, State.Progression.Level), definition.Tags.Contains("boss") || creature.Site.Recovery is not null);
        var outcome = TakedownRules.Resolve(CombatRules.LevelGap(PlayerStrike(attack) with { Execution = execution },State.Progression.Level,definition.Level+creature.LevelBonus), defense, C, creature.Health, fromBehind,
            Random("takedown", creature.Key, creature.Body, tick));
        State.SetPlayerCombat(_owner, State.PlayerCombat with { LastCombat = tick });
        _context.Dispatch(new WoundCreature(creature.Id, outcome.Hit, attack.Source));
        // A takedown is the skill's own practice: the killing of an unaware body is what stealth is for (finding 26: it levels with use).
        _context.Dispatch(new PracticeSkill(new SkillPractice("skill.stealth", definition.Level, PracticeOutcome.Success, tick)));
        return null;
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
        return new Strike(attack, multiplier, C.BaseCritPercent, stagger) { ArmorIgnoredPercent = _context.PlayerTraits.ArmorIgnoredPercent };
    }

    /// <summary>The player's people's resistances; phased, soul and phase damage do half again as much.</summary>
    private ImmutableSortedDictionary<string, double> PlayerResistances(bool phased)
    {
        var resistances = PeopleRules.Resistances(_context.PlayerTraits);
        return phased
            ? resistances.SetItem("necrotic", resistances.GetValueOrDefault("necrotic") - 0.5).SetItem("arcane", resistances.GetValueOrDefault("arcane") - 0.5)
            : resistances;
    }

    /// <summary>
    /// A creature's attack reached the player inside its active window (the creature system checked reach, arc and walls).
    /// The guard holds only against what is in front of it, and only while there is stamina behind it.
    /// </summary>
    public string? Handle(CreatureStrike command, long tick)
    {
        if (State.Creatures.Values.FirstOrDefault(c => c.Id == command.Attacker) is not { Alive: true } creature)
            return "no such attacker";
        if (creature.Site.Space != State.Space)
            return "attacker is in another space";
        var combat = State.PlayerCombat;
        if (combat.Defeated)
            return "dead";
        if (!CombatRules.CanAttemptAttack(creature.Id, _player,
                new(creature.Alive, !combat.Defeated, creature.Site.Space == State.Space))) return "attack is not permitted";
        var attack = command.Attack;
        var body = State.Body;
        // A guard takes only what it can take - a physical blow - so only such a blow can break it (the Phase-1 technical audit, L-21).
        bool guarding = combat.Blocking && DamageTypes.IsPhysical(attack.DamageType)
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
        // Wraith-step (Orenth): a physical blow passes through a body half elsewhere; soul and phase damage find it all the more.
        bool phased = _context.Phased();
        var defense = new DefenseProfile(MaxHealth(), PlayerArmor(), PlayerResistances(phased), guarding,
            dodging || phased && DamageTypes.IsPhysical(attack.DamageType))
        {
            StaggerThresholdPercent = _context.PlayerTraits.StaggerThresholdPercent,
        };
        var strike = CombatRules.LevelGap(new Strike(attack, EffectRules.DamageDealtMultiplier(EffectsOf(creature.Id), Setup.Effects), C.BaseCritPercent, 1.0), creature.Definition.Level + creature.LevelBonus, State.Progression.Level);
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
        bool knocked = attack.ForcesStagger && result.Final > 0 && !result.Blocked && !result.Dodged;
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
        // A piece's quality moves its armour as a weapon's moves its damage (WP3a); a shield in the off hand guards the body.
        int Worn(params EquipSlot[] slots) => slots.Sum(slot =>
            State.Equipment.TryGetValue(slot, out var id) && State.Inventory.FirstOrDefault(e => e.ItemId == id) is { } entry
                && _context.Setup.Items.Catalog.Find(entry.DefId) is { } item
                ? EquipmentRules.ArmorOf(item, entry.Quality, _context.Setup.Crafting.Constants.ArmorPerStep)
                : 0);
        var armor = ImmutableSortedDictionary.CreateRange(new[]
        {
            KeyValuePair.Create(BodyRegion.Head, Worn(EquipSlot.Head)),
            KeyValuePair.Create(BodyRegion.Torso, Worn(EquipSlot.Chest, EquipSlot.Cloak, EquipSlot.OffHand)),
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
        // Reported as it lands (the Phase-1 technical audit, L-26): no more than the health there was, and nothing once down.
        if (State.PlayerCombat.Defeated)
            return null;
        int before = Health();
        int health = LosePlayerHealth(command.Amount, command.Source, command.Source, tick);
        _context.Events.Publish(new HealthChanged(_player, command.Source, health - before, health, tick));
        return null;
    }

    public string? Handle(Heal command, long tick)
    {
        if (command.Amount <= 0)
            return null;
        if (command.Target != _player)
            return _context.Dispatch(new HealCreature(command.Target, command.Source, command.Amount));
        if (State.PlayerCombat.Defeated)
            return State.PlayerCombat.DownedUntil > 0 ? "the downed are helped up, not healed" : "the dead do not heal";
        int before = Health();
        _context.Dispatch(new ChangePools(command.Amount, 0));
        if (Health() != before)
            _context.Events.Publish(new HealthChanged(_player, command.Source, Health() - before, Health(), tick));
        return null;
    }

    public string? Handle(EndFight command)
    {
        State.SetPlayerCombat(_owner, PlayerCombat.Rested);
        return null;
    }

    public string? Handle(AwaitRespawn command)
    {
        State.SetPlayerCombat(_owner, State.PlayerCombat with { RespawnAt = command.At, DownedUntil = 0 });
        return null;
    }

    /// <summary>
    /// The combat a loaded character starts in. One saved at no health was saved downed, and under MMO rules that save is the death
    /// (DN15): they start fallen and past help, and the death system settles it on the first tick as it settles any other.
    /// </summary>
    public void Seed(PlayerRecord player)
    {
        if (player.Progression.Pools.Health == 0)
            State.SetPlayerCombat(_owner, PlayerCombat.Rested with { Defeated = true, PastHelp = true });
        else if (player.Attack is { } saved && Resumed(saved) is { } attack)
            State.SetPlayerCombat(_owner, PlayerCombat.Rested with
            {
                Action = ActionState.Begin(ActionKind.Attack, saved.StartTick, attack) with { Struck = saved.Struck.ToImmutableHashSet() },
            });
    }

    /// <summary>The attack under way as a save keeps it (schema 27), so a save mid-windup loads mid-windup; null when there is none.</summary>
    public AttackRecord? AttackUnderWay()
    {
        var action = State.PlayerCombat.Action;
        return action.Kind == ActionKind.Attack && action.PhaseAt(State.WorldTick, C).Phase != CombatPhase.Idle
            ? new AttackRecord(action.Attack!.Source, action.StartTick, action.Struck.ToImmutableArray())
            : null;
    }

    /// <summary>The blow a saved attack makes again: the weapon in hand's, or a technique's with it; null when neither makes it now.</summary>
    private AttackProfile? Resumed(AttackRecord saved)
    {
        var weapon = PlayerAttack();
        if (saved.Source == weapon.Source)
            return weapon;
        return _context.Setup.Techniques.TryGetValue(saved.Source, out var technique) && technique.SkillId == weapon.SkillId
            ? TechniqueRules.Profile(weapon, technique)
            : null;
    }

    /// <summary>The health a downed character stands up with: the revive share of their maximum, at least one.</summary>
    internal static int RevivedHealth(long maxHealth, CombatConstants constants) =>
        Math.Max(1, (int)(maxHealth * constants.DownedRevivePercent / 100));

    public string? Handle(LieDowned command, long tick)
    {
        var combat = State.PlayerCombat;
        if (!combat.Defeated || combat.RespawnAt != 0)
            return "not down";
        // Helped up: the fight goes on from here, so health returns only after the usual pause.
        State.SetPlayerCombat(_owner, command.Until > 0
            ? combat with { DownedUntil = command.Until, Action = ActionState.Idle, Blocking = false }
            : combat with { Defeated = false, DownedUntil = 0, LastCombat = tick, Action = ActionState.Idle, HealthMilli = 0 });
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
        if (State.Swimming)
        {
            // Swimming is exertion: the body holds itself up, and the wind it spends is half a sprint's own rate (config.damage_constants
            // stamina.sprint_per_s). A pool that runs out is the movement system's to bring ashore.
            int milli = stamina > 0 ? combat.SwimMilli + C.SprintStaminaPerSecond * TickMs / 2 : 0;
            staminaDelta -= milli / 1000;
            next = next with { SwimMilli = milli % 1000, LastExertion = tick };
        }
        else if (intent.IsMoving && intent.Gait == Gait.Sprint && phase == CombatPhase.Idle && !combat.Blocking)
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
            int milli = combat.HealthMilli + C.HealthRegenPerSecond * TickMs * (State.CampRestAt is not null ? 3 : 1);
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

    private DerivedStats Stats() => _context.PlayerStats();

    public int MaxHealth() => (int)Stats().HealthMax;

    public int MaxStamina() => (int)Stats().StaminaMax;

    public int Health() => State.Progression.Pools.Health ?? MaxHealth();

    public int Stamina() => State.Progression.Pools.Stamina ?? MaxStamina();

    private ImmutableArray<ActiveEffect> EffectsOf(EntityId body) => State.Effects.GetValueOrDefault(body, ImmutableArray<ActiveEffect>.Empty);

    /// <summary>The rolls of one blow, keyed by who struck whom on which tick: the same on a replay and after a load.</summary>
    private Func<uint, double> Random(string attacker, string target, Body at, long tick)
    {
        var channel = RngChannel.Open(State.World.WorldSeed, _context.CellOf(at), "combat", $"{attacker}>{target}@{tick}");
        return sample => channel.UInt64(sample) / SampleScale;
    }

    public CombatView View()
    {
        var combat = State.PlayerCombat;
        var (phase, left) = combat.Action.PhaseAt(State.WorldTick, C);
        return new CombatView(phase, left, combat.Action.Attack?.Source, combat.Blocking, PlayerAttack(), Health(), MaxHealth(), Stamina(),
            MaxStamina(), EffectsOf(_player))
        {
            InCombat = phase != CombatPhase.Idle || combat.Blocking || State.WorldTick - combat.LastCombat < C.OutOfCombatTicks,
            Focus = Focus(),
            MaxFocus = MaxFocus(),
            Strain = Strain(),
            StrainTolerance = StrainTolerance(),
            Strained = MagicRules.Strained(Strain(), StrainTolerance(), M),
            Casting = combat.Action.Kind == ActionKind.Cast && phase != CombatPhase.Idle ? combat.Action.Formula!.Id : null,
            Defeated = combat.Defeated,
            RespawnTicksLeft = combat.Defeated && combat.RespawnAt > 0 ? (int)Math.Max(0, combat.RespawnAt - State.WorldTick) : 0,
            Downed = combat.DownedUntil > 0,
            DownedTicksLeft = combat.DownedUntil > 0 ? (int)Math.Max(0, combat.DownedUntil - State.WorldTick) : 0,
        };
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.Effects"/> - the status effects on every combatant (S-11). Applies and refreshes them, ticks
/// their damage and healing into <see cref="CombatSystem"/>, and expires them. The player's are saved (schema 7). While the character is
/// away they only expire (WP11): closed-form, nothing ticks through the absence.
/// </summary>
internal sealed class StatusEffectSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public StatusEffectSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    /// <summary>
    /// Every effect whose deadline fell inside the absence is gone; one still running keeps its deadline, and its next pulse is the first
    /// of its own beats after the absence - the beats are fixed from when it was applied, so however the absence is split they land alike.
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        foreach (var target in _context.State.Effects.Keys.ToList())
        {
            var kept = new List<ActiveEffect>();
            foreach (var effect in _context.State.Effects[target])
            {
                var definition = Definitions.GetValueOrDefault(effect.EffectId);
                if (definition is null || effect.ExpiresTick <= toTick)
                {
                    _context.Events.Publish(new EffectExpired(target, effect.EffectId, toTick));
                    if (target == _player)
                        log.Add(new AdvanceLine(Math.Max(fromTick, Math.Min(effect.ExpiresTick, toTick)), AdvanceKinds.EffectExpired, effect.EffectId));
                    continue;
                }
                var current = effect;
                if (definition.TickIntervalTicks > 0 && current.NextTickAt <= toTick)
                {
                    long beats = (toTick - current.NextTickAt) / definition.TickIntervalTicks + 1;
                    current = current with { NextTickAt = current.NextTickAt + beats * definition.TickIntervalTicks };
                }
                kept.Add(current);
            }
            _context.State.SetEffects(_owner, target, kept.ToImmutableArray());
        }
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
        // A blade has one coating: replacing it must not leave delivery dependent on effect ordering.
        if (definition.AppliesOnHit is not null)
        {
            foreach (var old in current.Where(e => e.EffectId != definition.Id && Definitions.GetValueOrDefault(e.EffectId)?.AppliesOnHit is not null))
                _context.Events.Publish(new EffectExpired(command.Target, old.EffectId, tick));
            current = current.RemoveAll(e => e.EffectId != definition.Id && Definitions.GetValueOrDefault(e.EffectId)?.AppliesOnHit is not null);
        }
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
/// XP (AG-8); the effects of the fight cleared; respawn at the return point (the region's spawn until one is chosen); a spell of weakness. It asks each owner in turn.
/// With a hand near - a companion or hireling up within <see cref="CombatConstants.DownedHandWithinMm"/> (OD-13 = B) - the character is
/// downed first: they lie <see cref="CombatConstants.DownedTicks"/> and the hand helps them up. Should the last hand fall or go before
/// then, the death goes on as it would have. A death is never downed twice: once settled, it runs to the respawn. Nor is one loaded from a
/// save made while downed (DN15): it is past help.
/// </summary>
internal sealed class DeathSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly Func<Body> _returnPoint;
    private readonly Action _leaveSpace;

    public DeathSystem(SystemContext context, EntityId player, Func<Body> returnPoint, Action leaveSpace)
    {
        _context = context;
        _player = player;
        _returnPoint = returnPoint;
        _leaveSpace = leaveSpace;
    }

    public void Tick(long tick)
    {
        var state = _context.State;
        var combat = state.PlayerCombat;
        if (!combat.Defeated)
            return;
        if (combat.RespawnAt == 0)
        {
            var last = combat.Recent.LastOrDefault();
            var constants = _context.Setup.Combat.Constants;
            string? hand = Hand(state, constants.DownedHandWithinMm);
            if (combat.DownedUntil == 0 && !combat.PastHelp && hand is not null)
            {
                long until = tick + constants.DownedTicks;
                _context.Dispatch(new LieDowned(until));
                _context.Events.Publish(new PlayerDowned(last?.AttackerDefId ?? "unknown", last?.Source ?? "unknown", hand, tick, until));
                return;
            }
            if (combat.DownedUntil > 0 && hand is not null)
            {
                if (tick < combat.DownedUntil)
                    return;
                int health = CombatSystem.RevivedHealth(_context.PlayerStats().HealthMax, constants);
                _context.Dispatch(new ChangePools(health, 0));
                _context.Dispatch(new LieDowned(0));
                _context.Events.Publish(new PlayerRevived(hand, state.Progression.Pools.Health ?? health, tick));
                return;
            }
            long debtBefore = state.Progression.XpDebt;
            long respawnAt = tick + _context.Setup.Combat.Constants.DeathTicks;
            _context.Dispatch(new RecordDeath());
            _context.Events.Publish(new PlayerDied(last?.AttackerDefId ?? "unknown", last?.Source ?? "unknown", combat.Recent,
                state.Progression.XpDebt - debtBefore, tick, respawnAt));
            _context.Dispatch(new ClearEffects(_player));
            if (_context.Setup.Combat.Effects.ContainsKey(_context.Setup.Combat.Constants.DeathEffect))
                _context.Dispatch(new ApplyEffect(_player, _context.Setup.Combat.Constants.DeathEffect));
            _context.Dispatch(new AwaitRespawn(respawnAt));
            return;
        }
        if (tick < combat.RespawnAt)
            return;
        // Back at the stone the character chose to return to, or the region's spawn while they have chosen none
        // (S-38). OUT OF THE SPACE FIRST: a return point is a place in the REGION's space, so a death inside an
        // interior has to come out of it, or the body would stand at the stone's metres while the simulation
        // still called it inside and every cell it touched would be the wrong cell. It belongs here and not on the
        // death above, because the body does not move until it respawns.
        _leaveSpace();
        var body = _returnPoint();
        _context.Dispatch(new Relocate(body));
        _context.Dispatch(new EndFight());
        _context.Dispatch(new ForgetPlayer());
        _context.Events.Publish(new PlayerRespawned(body, tick));
    }

    /// <summary>The party member who can help the character up: up, and near enough; the nearest, then by ID. Null when there is none.</summary>
    private static string? Hand(RuntimeState state, long withinMm)
    {
        var at = state.Body;
        return state.Companions.Values
            .Where(c => c.Condition == CompanionCondition.Up && state.Npcs.ContainsKey(c.NpcId))
            .Select(c => (c.NpcId, Distance: Math.Sqrt(Math.Pow(state.Npcs[c.NpcId].Body.XMm - at.XMm, 2) + Math.Pow(state.Npcs[c.NpcId].Body.ZMm - at.ZMm, 2))))
            .Where(c => c.Distance <= withinMm)
            .OrderBy(c => c.Distance).ThenBy(c => c.NpcId, StringComparer.Ordinal)
            .Select(c => c.NpcId)
            .FirstOrDefault();
    }
}
