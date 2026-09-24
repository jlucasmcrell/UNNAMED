// UNNAMED World - magic at run time: casting a known formula for Focus and Strain, its tell, its release, and what breaks
// it (SYSTEMS.md S-13; MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md; PROGRESSION.md §7; M3e)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

/// <summary>The magic a simulation runs under, built from content at boot.</summary>
public sealed record MagicSetup(MagicConstants Constants, ImmutableSortedDictionary<string, FormulaDefinition> Formulas)
{
    /// <summary>What reading a book teaches: item definition ID to the formulas it grants (DATA_MODEL.md §4.1 <c>use.grants</c>).</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<string>> Teaches { get; init; } =
        ImmutableSortedDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal);

    public static MagicSetup Empty { get; } = new(new MagicConstants(), ImmutableSortedDictionary.Create<string, FormulaDefinition>(StringComparer.Ordinal));
}

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Cast a known formula: a self formula works on the caster, a projectile along the caster's facing.</summary>
public sealed record CastCommand(EntityId Actor, string FormulaId) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>A working began: its Focus is spent, and its tell runs for the cast time.</summary>
public sealed record CastStarted(EntityId Caster, string FormulaId, int CastTicks, long Tick);

/// <summary>A working was released and took hold; it cost this much Strain.</summary>
public sealed record CastCompleted(EntityId Caster, string FormulaId, int Strain, long Tick);

/// <summary>A working was released and failed: its Focus and Strain are spent for nothing.</summary>
public sealed record CastFizzled(EntityId Caster, string FormulaId, int Strain, long Tick);

/// <summary>A working was broken off in its tell - by a wound, or by a dodge - and its Focus lost.</summary>
public sealed record CastInterrupted(EntityId Caster, string FormulaId, string Reason, long Tick);

/// <summary>A working pushed Strain past tolerance, and the excess was paid in health.</summary>
public sealed record StrainBacklash(EntityId Caster, int Damage, long Tick);

/// <summary>A technique, formula or recipe became known (a learning event, PROGRESSION.md §4.4).</summary>
public sealed record TechniqueLearned(string DefinitionId, string Source, long Tick);

// ── internal commands ───────────────────────────────────────────────────────

/// <summary>To <see cref="ProgressionSystem"/>: a learning event.</summary>
internal sealed record LearnTechnique(TechniqueLearning Learning) : InternalCommand;

/// <summary>To <see cref="StatusEffectSystem"/>: lift one effect (a mending that stops a bleed).</summary>
internal sealed record RemoveEffect(EntityId Target, string EffectId) : InternalCommand;

// ── casting: the player's action, so the combat system's ───────────────────

internal sealed partial class CombatSystem
{
    private MagicConstants M => _context.Setup.Magic.Constants;

    /// <summary>
    /// Begin a working. Every known formula can be cast; above one's skill it costs more Strain and may fizzle, and past
    /// one's tolerance it costs health - a risk, never a lock (MAGIC "Unsafe casting", "No spell-slot limit").
    /// </summary>
    public string? Handle(CastCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!_context.Setup.Magic.Formulas.TryGetValue(command.FormulaId, out var formula))
            return $"{command.FormulaId} is not a formula this build knows";
        if (!ProgressionEngine.Knows(State.Progression, formula.Id))
            return $"{formula.Id} is not known";
        if (Busy(State.PlayerCombat.Action, tick) is { } busy)
            return busy;
        if (Focus() < formula.FocusCost)
            return "not enough Focus";
        _context.Dispatch(new ChangePools(0, 0) { Focus = -formula.FocusCost });
        State.SetPlayerCombat(_owner, State.PlayerCombat with
        {
            Action = ActionState.Begin(ActionKind.Cast, tick) with { Formula = formula },
            Blocking = false,
            LastCast = tick,
        });
        _context.Events.Publish(new CastStarted(_player, formula.Id, formula.CastTicks, tick));
        return null;
    }

    /// <summary>
    /// The tell is over: the working takes its Strain - past tolerance, the excess in health - then fizzles or takes hold.
    /// A projectile resolves through the one damage pipeline; a self formula lifts and puts on its effects. Its domain
    /// learns only from a working that mattered: a bolt that wounded, or a working cast in the thick of a fight.
    /// </summary>
    private void Release(FormulaDefinition formula, long tick)
    {
        int skill = ProgressionEngine.SkillLevel(State.Progression, formula.DomainSkillId);
        int cost = MagicRules.StrainCost(formula, skill, M);
        int before = Strain();
        var (strain, backlash) = MagicRules.Settle(before, cost, StrainTolerance(), M);
        if (strain != before)
            _context.Dispatch(new ChangePools(0, 0) { Strain = strain - before });
        bool challenged = tick - State.PlayerCombat.LastCombat < C.OutOfCombatTicks;
        State.SetPlayerCombat(_owner, State.PlayerCombat with { LastCast = tick });

        if (Random("player", formula.Id, State.Body, tick)(0) * 100 < MagicRules.FizzlePercent(formula, skill, M))
        {
            _context.Events.Publish(new CastFizzled(_player, formula.Id, cost, tick));
            if (challenged)
                Practise(formula, PracticeOutcome.Failure, tick);
        }
        else
        {
            if (formula.Targeting == Targeting.Projectile)
            {
                var target = Loose(formula.Id, formula.Blow!.ReachMm, tick);
                if (target is not null)
                    PlayerHits(target, formula.Blow, tick);
                else
                    _context.Events.Publish(new AttackMissed(_player, formula.Id, tick));
            }
            else
            {
                foreach (string effect in formula.Removes)
                    _context.Dispatch(new RemoveEffect(_player, effect));
                foreach (string effect in formula.Applies)
                    _context.Dispatch(new ApplyEffect(_player, effect));
                if (challenged)
                    Practise(formula, PracticeOutcome.Success, tick);
            }
            _context.Events.Publish(new CastCompleted(_player, formula.Id, cost, tick));
        }
        if (backlash > 0)
        {
            _context.Events.Publish(new StrainBacklash(_player, backlash, tick));
            _context.Dispatch(new Harm(_player, "strain", backlash));
        }
    }

    private void Practise(FormulaDefinition formula, PracticeOutcome outcome, long tick) =>
        _context.Dispatch(new PracticeSkill(new SkillPractice(formula.DomainSkillId, formula.Complexity, outcome, tick)
        {
            NoveltyKey = outcome == PracticeOutcome.Success ? formula.Id : null,
        }));

    /// <summary>A working still in its tell is broken off: its Focus is gone and nothing else happens.</summary>
    private void BreakCast(ActionState was, string reason, long tick)
    {
        if (was.Kind == ActionKind.Cast && was.PhaseAt(tick, C).Phase == CombatPhase.Windup)
            _context.Events.Publish(new CastInterrupted(_player, was.Formula!.Id, reason, tick));
    }

    public int MaxFocus() => (int)Stats().FocusMax;

    public int Focus() => State.Progression.Pools.Focus ?? MaxFocus();

    public int Strain() => State.Progression.Pools.Strain;

    public int StrainTolerance() => (int)Stats().StrainTolerance;
}
