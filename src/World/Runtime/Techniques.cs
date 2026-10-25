// UNNAMED World - techniques at run time: a known active technique, used with a weapon of its family (WP9; D11; SYSTEMS.md S-10)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Use a known weapon-bound technique. Reactions also fire from the base guard verb; deliberate and contextual techniques use this
/// command, including a second press to release a held draw.
/// </summary>
public sealed record TechniqueCommand(EntityId Actor, string TechniqueId) : GameCommand(Actor)
{
    public BodyRegion Region { get; init; } = BodyRegion.Torso;
}

/// <summary>A technique was begun with this weapon (its <see cref="AttackStarted"/> follows under the technique's name); ready again at <paramref name="ReadyTick"/>.</summary>
public sealed record TechniqueUsed(EntityId Actor, string TechniqueId, string WeaponDefId, long ReadyTick, long Tick);
public sealed record TechniqueDiscovered(string TechniqueId, long Tick);
public sealed record TechniqueHint(string TechniqueId, string Reason, long Tick);

// ── techniques: the player's blows, so the combat system's ─────────────────

internal sealed partial class CombatSystem
{
    private long _guardRaisedAt = long.MinValue;
    private readonly Dictionary<EntityId, (long Until, long ImmuneUntil)> _openings = new();
    private readonly Dictionary<EntityId, int> _guardStamina = new();
    private readonly Dictionary<EntityId, long> _guardRestoresAt = new();
    private readonly HashSet<string> _hintedTechniques = new(StringComparer.Ordinal);
    private long _steadySince;
    private int _steadyFacing;
    private bool _holdingDraw;
    private BodyRegion _aimRegion;

    private string? ReleaseDraw(long tick)
    {
        if (_context.ControlOn(_player) == ControlKind.Incapacitates) return "you cannot move";
        var action = State.PlayerCombat.Action;
        var attack = action.Attack!;
        if (action.PhaseAt(tick, C).Phase == CombatPhase.Windup && tick - action.StartTick < attack.WindupTicks)
            return "the bow is not fully drawn";
        _holdingDraw = false;
        bool steady = !_intent().IsMoving && tick - _steadySince >= Ticks(1_000) && State.Body.FacingMdeg == _steadyFacing;
        attack = attack with { PlacedRegion = steady && ReadyTechnique(TechniqueKind.SteadyDraw, PlayerAttack()) is not null ? _aimRegion : null };
        State.SetPlayerCombat(_owner, State.PlayerCombat with { Action = action with { Attack = attack, StartTick = tick - attack.WindupTicks } });
        return null;
    }

    private TechniqueDefinition? ReadyTechnique(TechniqueKind kind, AttackProfile weapon) => _context.Setup.Techniques.Values
        .FirstOrDefault(t => t.Kind == kind && t.SkillId == weapon.SkillId && (!t.RequiresDagger || StealthRules.IsDagger(weapon))
            && ProgressionEngine.Knows(State.Progression, t.Id) && _context.TechniqueRefusal(t) is null);

    private bool Open(CreatureState creature, long tick) => _openings.TryGetValue(creature.Id, out var opening) && tick < opening.Until;

    private bool KnowsAnatomy(CreatureState creature) => State.Lore.Any(page => _context.Setup.Lore.TryGetValue(page.EntryId, out var entry)
        && entry.Category == UNNAMED.Domain.Knowledge.LoreCategory.Creatures
        && entry.Pages.Any(p => p.Page == page.Page && p.Sources.Any(s => s.Ref == creature.Definition.Id)));

    private void OpenCreature(CreatureState creature, long tick, string source)
    {
        if (_openings.TryGetValue(creature.Id, out var opening) && tick < opening.ImmuneUntil) return;
        _openings[creature.Id] = (tick + Ticks(500), tick + Ticks(2_000));
        // Its owner cancels the blow through the existing stagger and immunity rule; combat owns only the opening window.
        _context.Dispatch(new WoundCreature(creature.Id, new HitResult(BodyRegion.Torso, 0, 0, false, false, false, true, false), source));
    }

    private void Discover(TechniqueDiscovery success, CreatureState opponent, long tick)
    {
        foreach (var technique in _context.Setup.Techniques.Values.Where(t => t.Discovery == success && t.SkillId == PlayerAttack().SkillId
                     && (!t.RequiresDagger || StealthRules.IsDagger(PlayerAttack())) && !ProgressionEngine.Knows(State.Progression, t.Id)))
        {
            int skill = ProgressionEngine.SkillLevel(State.Progression, technique.SkillId);
            if (opponent.Definition.Level <= skill - _context.Setup.Progression.Skills.DifficultyMargin) continue;
            if (_context.TechniqueRefusal(technique) is { } reason)
            {
                if (_hintedTechniques.Add(technique.Id)) _context.Events.Publish(new TechniqueHint(technique.Id, reason, tick));
                continue;
            }
            if (_context.Dispatch(new LearnTechnique(new TechniqueLearning(technique.Id, LearningSource.Discovery, tick)
                    { SourceRef = opponent.Definition.Id })) is not null) continue;
            _context.Dispatch(new PracticeSkill(new SkillPractice(technique.SkillId, opponent.Definition.Level, PracticeOutcome.Success, tick)
                { NoveltyKey = technique.Id }));
            _context.Events.Publish(new TechniqueDiscovered(technique.Id, tick));
        }
    }
    /// <summary>
    /// Begin a technique: the weapon's attack as the technique changes it, then the blow runs as any attack does - the same phases, the
    /// same pipeline, the same skill trained by what it wounds. Refused unknown, with no weapon of its family in hand, before its cooldown
    /// is out, without the stamina (or a bow's arrow), and while busy.
    /// </summary>
    public string? Handle(TechniqueCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!_context.Setup.Techniques.TryGetValue(command.TechniqueId, out var technique))
            return $"{command.TechniqueId} is not a technique this build knows";
        if (!ProgressionEngine.Knows(State.Progression, technique.Id))
            return $"{technique.Id} is not known";
        if (_context.TechniqueRefusal(technique) is { } requirements) return requirements;
        if (!Enum.IsDefined(command.Region)) return "unknown body region";
        if (_context.ControlOn(_player) != ControlKind.None) return "you cannot move";
        var combat = State.PlayerCombat;
        if (_holdingDraw && combat.Action.Attack?.Source == technique.Id) return ReleaseDraw(tick);
        if (Busy(combat.Action, tick) is { } busy)
            return busy;
        var weapon = PlayerAttack();
        if (weapon.SkillId != technique.SkillId)
            return $"{technique.Id} needs a {technique.SkillId} weapon in hand";
        if (technique.RequiresDagger && !StealthRules.IsDagger(weapon)) return $"{technique.Id} needs a dagger in hand";
        if (technique.Kind is TechniqueKind.Parry or TechniqueKind.SetAgainstCharge)
            return Handle(new BlockCommand(_player, true), tick);
        if (combat.TechniquesReadyAt.TryGetValue(technique.Id, out long ready) && tick < ready)
            return $"{technique.Id} is not ready ({ready - tick} ticks)";
        var attack = TechniqueRules.Profile(weapon, technique);
        if (technique.Kind is TechniqueKind.GapThrust or TechniqueKind.QuietBlade)
        {
            var target = State.Creatures.Values.Where(c => c.Alive && c.Site.Space == State.Space
                    && CombatRules.InFront(State.Body.XMm, State.Body.ZMm, State.Body.FacingMdeg, c.Body.XMm, c.Body.ZMm,
                        weapon.ReachMm + c.Definition.RadiusMm, C.MeleeArcMdeg)
                    && !Walled(State.Body.XMm, State.Body.ZMm, c.Body.XMm, c.Body.ZMm))
                .OrderBy(c => Math.Pow(c.Body.XMm - State.Body.XMm, 2) + Math.Pow(c.Body.ZMm - State.Body.ZMm, 2)).FirstOrDefault();
            if (target is null) return "nothing within reach";
            bool unaware = StealthRules.Unaware(target.Awareness, Setup.Awareness);
            if (technique.Kind == TechniqueKind.QuietBlade && (!unaware || !Behind(target.Body, State.Body))) return "needs an unaware target from behind";
            if (!unaware && !Open(target, tick) && target.Action.PhaseAt(tick, C).Phase != CombatPhase.Staggered) return "needs an opening";
            attack = attack with { PlacedRegion = command.Region, PlacedTarget = target.Id, PlacedAsAmbush = unaware };
        }
        if (technique.Kind == TechniqueKind.SteadyDraw)
        {
            _holdingDraw = true;
            _aimRegion = command.Region;
            _steadySince = tick + attack.WindupTicks;
            _steadyFacing = State.Body.FacingMdeg;
        }
        if (attack.Ranged && attack.AmmoDefId is { } ammo && !State.Inventory.Any(e => e.DefId == ammo))
            return $"no {ammo} to shoot";
        if (Stamina() < attack.StaminaCost)
            return $"too tired for {technique.Id}";
        Exert(attack.StaminaCost, tick);
        long readyAt = tick + technique.CooldownTicks;
        State.SetPlayerCombat(_owner, State.PlayerCombat with
        {
            Action = ActionState.Begin(ActionKind.Attack, tick, attack,
                dirX: (int)Math.Round(-Math.Sin(State.Body.FacingMdeg / 1000.0 * Math.PI / 180) * 1000),
                dirZ: (int)Math.Round(-Math.Cos(State.Body.FacingMdeg / 1000.0 * Math.PI / 180) * 1000)),
            Blocking = false,
            TechniquesReadyAt = State.PlayerCombat.TechniquesReadyAt.SetItem(technique.Id, readyAt),
        });
        _context.Events.Publish(new TechniqueUsed(_player, technique.Id, weapon.Source, readyAt, tick));
        _context.Events.Publish(new AttackStarted(_player, attack.Source, attack.WindupTicks, attack.ActiveTicks, attack.RecoveryTicks, tick));
        return null;
    }

    /// <summary>Ticks until a technique is ready again; 0 when it is ready now (the quick bar's cooldown sweep).</summary>
    public int TechniqueCooldown(string techniqueId, long tick) =>
        State.PlayerCombat.TechniquesReadyAt.TryGetValue(techniqueId, out long ready) && ready > tick ? (int)(ready - tick) : 0;
}
