// UNNAMED World - teachers at run time: lessons asked for from the Learn column or given by a line of conversation, on the teacher's
// terms, paid in coin (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.3 C10; WP9; PROGRESSION.md §4.4)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;

using UNNAMED.Domain.Combat;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Ask the NPC the character is talking to for one lesson of their <c>teaches</c> block (the Learn column of the conversation's context
/// sheet, WP9). A lesson their conversation gives (<see cref="TeachingOffer.ByDialogue"/>) is asked for there instead.
/// </summary>
public sealed record LearnFromTeacherCommand(EntityId Actor, string NpcId, string DefinitionId) : GameCommand(Actor);

/// <summary>A teacher taught the character one entry of their block, for this fee (a learning event from a teacher follows as <see cref="TechniqueLearned"/>).</summary>
public sealed record Taught(string NpcId, string DefinitionId, TeachingKind Kind, long Fee, long Tick);

/// <summary>
/// What a teacher offers now, for the Learn column: whether they teach at this hour and place (and why not), and each lesson with its
/// fee, its requirements as they stand, and whether it is known already.
/// </summary>
public sealed record TeachingView(string NpcId, string? Unavailable, ImmutableArray<LessonView> Lessons)
{
    public bool Available => Unavailable is null;
}

/// <summary>One lesson as the Learn column lists it: met and unmet requirement chips, the fee, and whether the conversation gives it.</summary>
public sealed record LessonView(TeachingKind Kind, string DefinitionId, long Fee, bool Known, bool ByDialogue, ImmutableArray<RequirementCheck> Requirements)
{
    /// <summary>The technique's own requirements (PRG-29), each met or not, with the words for why not.</summary>
    public ImmutableArray<TechniqueRequirementVerdict> Technique { get; init; } = ImmutableArray<TechniqueRequirementVerdict>.Empty;

    public bool Met => Requirements.All(r => r.Met) && Technique.All(t => t.Met);
}

/// <summary>To <see cref="TeachingSystem"/>: a <c>teach</c> consequence - the speaker teaches these entries of their block, all or nothing.</summary>
internal sealed record Teach(string NpcId, ImmutableArray<string> DefinitionIds) : InternalCommand;

/// <summary>
/// When and where a teacher teaches (C10: at their work anchor, in their work hours, §2.13). The NPC life simulation (WP12) answers it;
/// a teacher with no routine teaches wherever and whenever they are spoken to.
/// </summary>
internal interface ITeachingHours
{
    /// <summary>Why this NPC is not teaching at this tick (away from their work anchor, out of work hours); null when they are.</summary>
    string? Unavailable(string npcId, long tick);
}

/// <summary>
/// Owns no state: a lesson changes only what the character knows (<see cref="ProgressionSystem"/>'s, by a learning event from a teacher)
/// and their coin (the inventory's, spent; into the teacher's purse when they keep one, WP14). Every gate - the teacher's hours, each
/// requirement, the fee - is checked before anything changes, so a lesson is given whole or refused whole. No lesson, fee or teacher
/// ever raises a skill (PROGRESSION.md:64, 223): a technique is taught at the skill the character already has.
/// </summary>
internal sealed class TeachingSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly Func<string, IDialogueFacts> _facts;
    private readonly ITeachingHours _hours;

    public TeachingSystem(SystemContext context, EntityId player, Func<string, IDialogueFacts> facts, ITeachingHours hours)
    {
        _context = context;
        _player = player;
        _facts = facts;
        _hours = hours;
    }

    private RuntimeState State => _context.State;

    public string? Handle(LearnFromTeacherCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        if (State.Conversation?.NpcId != command.NpcId)
            return $"not talking to {npc.Definition.Name}";
        if (npc.Definition.Teaches.FirstOrDefault(o => o.DefinitionId == command.DefinitionId) is not { } offer)
            return $"{npc.Definition.Name} does not teach {command.DefinitionId}";
        if (offer.ByDialogue)
            return $"{npc.Definition.Name} teaches {command.DefinitionId} when asked in conversation";
        if (Knows(offer.DefinitionId))
            return $"{offer.DefinitionId} is already known";
        return Lesson(command.NpcId, ImmutableArray.Create(command.DefinitionId), tick);
    }

    /// <summary>A <c>teach</c> consequence. What is known already is simply not taught again, and is no refusal.</summary>
    public string? Handle(Teach command, long tick) => Lesson(command.NpcId, command.DefinitionIds, tick);

    private string? Lesson(string npcId, ImmutableArray<string> ids, long tick)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc))
            return $"there is no one called {npcId} here";
        var teacher = npc.Definition;
        var offers = new List<TeachingOffer>();
        foreach (string id in ids)
        {
            if (teacher.Teaches.FirstOrDefault(o => o.DefinitionId == id) is not { } offer)
                return $"{teacher.Name} does not teach {id}";
            if (!Knows(id))
                offers.Add(offer);
        }
        if (offers.Count == 0)
            return null;
        if (_hours.Unavailable(npcId, tick) is { } away)
            return $"{teacher.Name} is not teaching now: {away}";
        var facts = _facts(npcId);
        foreach (var offer in offers)
        {
            if (TeachingRules.Check(offer, npcId, facts, Knows).FirstOrDefault(c => !c.Met) is { } unmet)
                return $"{teacher.Name} will not teach {offer.DefinitionId} yet: {Reason(unmet, teacher)}";
            // PRG-29: the technique's own record gates the lesson too, whatever the teacher thinks of the character.
            if (TechniqueRequirements.FirstUnmet(TechniqueCandidates.RequirementsOf(_context.Setup, offer.DefinitionId),
                    TechniqueCandidates.Of(State, _context.Setup)) is { } short_)
                return $"{teacher.Name} will not teach {offer.DefinitionId} yet: {short_.Why}";
        }
        long fee = offers.Sum(o => o.Fee);
        if (State.Currency < fee)
            return $"not enough coin: {fee} asked, {State.Currency} carried";
        if (fee > 0)
        {
            if (_context.Dispatch(new SpendCurrency(fee)) is { } refused)
                return refused;
            // A fee feeds the teacher's purse when they keep one (WP14); otherwise it simply leaves the character's.
            if (teacher.MerchantId is { } merchant)
                _context.Dispatch(new PayMerchant(merchant, fee, ByCharacter: false));
        }
        foreach (var offer in offers)
        {
            _context.Dispatch(new LearnTechnique(new TechniqueLearning(offer.DefinitionId, LearningSource.Teacher, tick) { SourceRef = npcId }));
            _context.Events.Publish(new Taught(npcId, offer.DefinitionId, offer.Kind, offer.Fee, tick));
        }
        return null;
    }

    /// <summary>What an NPC teaches now, for the Learn column; null when they teach nothing.</summary>
    public TeachingView? View(string npcId, long tick)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc) || npc.Definition.Teaches.IsEmpty)
            return null;
        var facts = _facts(npcId);
        return new TeachingView(npcId, _hours.Unavailable(npcId, tick), npc.Definition.Teaches
            .Select(o => new LessonView(o.Kind, o.DefinitionId, o.Fee, Knows(o.DefinitionId), o.ByDialogue, TeachingRules.Check(o, npcId, facts, Knows))
            {
                // PRG-29: the technique's own record is a chip like any other, so the Learn column says why a lesson is shut.
                Technique = TechniqueRequirements.Check(
                    TechniqueCandidates.RequirementsOf(_context.Setup, o.DefinitionId), TechniqueCandidates.Of(State, _context.Setup)),
            })
            .ToImmutableArray());
    }

    /// <summary>A lesson has no clock of its own: nothing about teaching changes while the character is away.</summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
    }

    private bool Knows(string definitionId) => ProgressionEngine.Knows(State.Progression, definitionId);

    /// <summary>The unmet requirement in words, with what the character has against it (the Learn column words its chips itself).</summary>
    private static string Reason(RequirementCheck check, NpcDefinition teacher) => check.Requirement switch
    {
        SkillBandRequirement skill => $"needs {skill.SkillId} at {skill.Band} ({skill.MinLevel}), has {check.Have}",
        TrustRequirement trust => $"needs trust {trust.Min} with {teacher.Name}, has {check.Have}",
        PrerequisiteRequirement prerequisite => $"needs {prerequisite.DefinitionId} known first",
        DeedRequirement deed => $"needs {DeedKeys.Key(deed.Condition.Kind)} {deed.Condition.SubjectId} {deed.Condition.Count} times, has {check.Have}",
        FlagRequirement flag => $"needs {flag.Condition.FlagId} set",
        QuestRequirement quest => $"needs {quest.Condition.QuestId}{(quest.Condition.ObjectiveId is { } o ? $" {o}" : "")} {(quest.Condition.Negated ? "not " : "")}{quest.Condition.State}",
        _ => check.Requirement.Key,
    };
}
