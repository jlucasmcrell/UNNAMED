// UNNAMED Domain - teachers: what an NPC teaches, on what terms, and whether the character meets them
// (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.3 C10, WP9; PROGRESSION.md §4.4; DATA_MODEL.md §4.5 role trainer)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Quests;

namespace UNNAMED.Domain.Social;

/// <summary>What a teacher can teach (C10): a recipe, a technique (an active <c>ability</c>), a formula (<c>spell</c>) or a building piece.</summary>
public enum TeachingKind
{
    Recipe,
    Technique,
    Spell,
    BuildingPiece,
}

/// <summary>The teaching kinds' keys in content, and the definition prefix each teaches.</summary>
public static class TeachingKeys
{
    public static readonly ImmutableArray<TeachingKind> All =
        ImmutableArray.Create(TeachingKind.Recipe, TeachingKind.Technique, TeachingKind.Spell, TeachingKind.BuildingPiece);

    public static string Key(TeachingKind kind) => kind switch
    {
        TeachingKind.Recipe => "recipe",
        TeachingKind.Technique => "technique",
        TeachingKind.Spell => "spell",
        TeachingKind.BuildingPiece => "building_piece",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown teaching kind"),
    };

    /// <summary>The content kind a teaching of this kind names: a technique is an <c>ability</c>, a building piece a <c>piece</c>.</summary>
    public static string ContentKind(TeachingKind kind) => kind switch
    {
        TeachingKind.Recipe => "recipe",
        TeachingKind.Technique => "ability",
        TeachingKind.Spell => "spell",
        TeachingKind.BuildingPiece => "piece",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown teaching kind"),
    };

    public static TeachingKind? Parse(string key) => All.Cast<TeachingKind?>().FirstOrDefault(k => Key(k!.Value) == key);
}

/// <summary>
/// One gate on a lesson (C12:263-274: threshold, trust, fee): a teacher teaches only a character who meets every one. None of them is
/// ever paid for or raised by the lesson - a fee buys access, never standing, and no teacher raises a skill (PROGRESSION.md:64, 223).
/// </summary>
public abstract record TeachingRequirement
{
    /// <summary>The requirement's kind as content and the Learn column's chips name it.</summary>
    public abstract string Key { get; }
}

/// <summary>A skill at least at a band (PROGRESSION.md §6's bands, named in <c>config.teaching</c>): <see cref="MinLevel"/> is the band's floor.</summary>
public sealed record SkillBandRequirement(string SkillId, string Band, int MinLevel) : TeachingRequirement
{
    public override string Key => "skill";
}

/// <summary>What the teacher thinks of the character: trust at least this.</summary>
public sealed record TrustRequirement(int Min) : TeachingRequirement
{
    public override string Key => "trust";
}

/// <summary>Something the character must know already: a technique, recipe, formula or building piece.</summary>
public sealed record PrerequisiteRequirement(string DefinitionId) : TeachingRequirement
{
    public override string Key => "prerequisite";
}

/// <summary>A deed done (E-7), as a dialogue's <c>deed</c> condition asks it.</summary>
public sealed record DeedRequirement(DeedCondition Condition) : TeachingRequirement
{
    public override string Key => "deed";
}

/// <summary>A world flag in the teacher's cell in range, as a dialogue's <c>world_state</c> condition asks it.</summary>
public sealed record FlagRequirement(WorldStateCondition Condition) : TeachingRequirement
{
    public override string Key => "flag";
}

/// <summary>A quest (or one of its objectives) in a state, as a dialogue's <c>quest_state</c> condition asks it.</summary>
public sealed record QuestRequirement(QuestStateCondition Condition) : TeachingRequirement
{
    public override string Key => "quest";
}

/// <summary>
/// One entry of an NPC's <c>teaches</c> block (C10): what it teaches, the gates, and the coin it costs - a sink the character pays into
/// the economy. <see cref="ByDialogue"/> marks a lesson given by a line of the teacher's conversation (a <c>teach</c> consequence) rather
/// than asked for from the Learn column: the conversation decides when it is offered.
/// </summary>
public sealed record TeachingOffer(TeachingKind Kind, string DefinitionId, ImmutableArray<TeachingRequirement> Requirements, long Fee)
{
    public bool ByDialogue { get; init; }
}

/// <summary>One requirement as it stands for the character now: met or not, and what they have against it (a level, trust, a count; 1 or 0).</summary>
public sealed record RequirementCheck(TeachingRequirement Requirement, bool Met, long Have);

/// <summary><c>teach</c> (WP9): the speaker teaches these entries of their <c>teaches</c> block - refused whole if any gate or the fee is not met.</summary>
public sealed record TeachConsequence(ImmutableArray<string> DefinitionIds) : DialogueConsequence
{
    public bool Equals(TeachConsequence? other) => other is not null && DefinitionIds.SequenceEqual(other.DefinitionIds);

    public override int GetHashCode() => DefinitionIds.Aggregate(17, (h, id) => h * 31 + id.GetHashCode());
}

public static class TeachingRules
{
    /// <summary>Every requirement of a lesson as it stands now, asked of the teacher's facts (their cell's flags, their trust).</summary>
    public static ImmutableArray<RequirementCheck> Check(TeachingOffer offer, string teacherId, IDialogueFacts facts, Func<string, bool> knows) =>
        offer.Requirements.Select(r => Check(r, teacherId, facts, knows)).ToImmutableArray();

    public static RequirementCheck Check(TeachingRequirement requirement, string teacherId, IDialogueFacts facts, Func<string, bool> knows)
    {
        switch (requirement)
        {
            case SkillBandRequirement skill:
            {
                int level = facts.SkillLevel(skill.SkillId);
                return new RequirementCheck(requirement, level >= skill.MinLevel, level);
            }
            case TrustRequirement trust:
            {
                int value = facts.Relationship(teacherId, "trust");
                return new RequirementCheck(requirement, value >= trust.Min, value);
            }
            case PrerequisiteRequirement prerequisite:
            {
                bool known = knows(prerequisite.DefinitionId);
                return new RequirementCheck(requirement, known, known ? 1 : 0);
            }
            case DeedRequirement deed:
            {
                int count = facts.Deeds(deed.Condition.Kind, deed.Condition.SubjectId, deed.Condition.NpcId);
                return new RequirementCheck(requirement, DialogueRules.Holds(deed.Condition, "", facts), count);
            }
            case FlagRequirement flag:
                return new RequirementCheck(requirement, DialogueRules.Holds(flag.Condition, "", facts), facts.WorldFlag(flag.Condition.FlagId, flag.Condition.LocationId));
            case QuestRequirement quest:
            {
                bool holds = DialogueRules.Holds(quest.Condition, "", facts);
                return new RequirementCheck(requirement, holds, holds ? 1 : 0);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(requirement), requirement, "Unknown teaching requirement");
        }
    }
}
