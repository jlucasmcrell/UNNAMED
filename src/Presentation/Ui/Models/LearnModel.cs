// UNNAMED Presentation - the Learn column: what a teacher offers now, each lesson's fee and its requirements as met and unmet chips, in the
// Skills page's reason grammar (WP9; SYSTEMS_COMPLETENESS_PLAN §2.3 C10)
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11)

using System.Collections.Immutable;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>Where a lesson stands: known already, ready to ask for, asked for in the conversation instead, or blocked by a requirement or the fee.</summary>
public enum LearnState { Known, Ready, InConversation, Blocked }

/// <summary>A requirement chip: its words and whether it is met.</summary>
public sealed record LearnChip(string Text, bool Met);

/// <summary>
/// One lesson as the Learn column lists it: what it teaches and what kind of thing that is, the fee, where it stands, the first reason it
/// cannot be asked for now (verbatim, muted under it), and every requirement as a chip.
/// </summary>
public sealed record LearnRow(string DefinitionId, string Name, string Kind, string Fee, LearnState State, string? Why, ImmutableArray<LearnChip> Chips)
{
    /// <summary>The Learn action is offered only for a lesson ready to ask for.</summary>
    public bool CanLearn => State == LearnState.Ready;

    /// <summary>What is taught, in a line: what a technique does and costs, what a recipe makes and needs, what a formula works. Null when nothing says.</summary>
    public string? Teaches { get; init; }

    /// <summary>The availability word, before committing (canon 26 §4.3, §5.2): Known, Ready, Ask them, or why not - "Needs skill", "Needs trust", "Builds on", "Short of coin".</summary>
    public string Word { get; init; } = "";
}

/// <summary>The column: whether the teacher teaches now (and why not), and the lessons - ready first, then blocked, then known.</summary>
public sealed record LearnColumn(string NpcId, string? Unavailable, ImmutableArray<LearnRow> Rows)
{
    public bool Teaching => Unavailable is null;
}

public static class LearnModel
{
    /// <summary>
    /// The column from a teacher's view. <paramref name="coin"/> is what the character carries (a fee they cannot pay blocks the lesson);
    /// <paramref name="name"/> names a definition, a skill or a quest. A teacher who does not teach now offers nothing to learn, but every
    /// lesson is still listed, so the player sees what will be on offer - except one still waiting on the world (an unmet flag), which the
    /// player has no way to ask after yet (the Barrowworks wedge where the ring cannot be reached).
    /// </summary>
    public static LearnColumn Column(TeachingView view, string teacher, long coin, Func<string, string> name, Func<string, string?>? teaches = null)
    {
        var rows = view.Lessons.Where(lesson => lesson.Known || !lesson.Requirements.Any(r => r is { Requirement: FlagRequirement, Met: false }))
            .Select(lesson => Row(lesson, view.Unavailable, teacher, coin, name, teaches))
            .OrderBy(r => r.State switch { LearnState.Ready => 0, LearnState.InConversation => 1, LearnState.Blocked => 2, _ => 3 })
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        return new LearnColumn(view.NpcId, view.Unavailable is null ? null : Sentence(teacher, view.Unavailable), rows);
    }

    public static LearnRow Row(LessonView lesson, string? unavailable, string teacher, long coin, Func<string, string> name, Func<string, string?>? teaches = null)
    {
        var chips = lesson.Requirements.Select(r => new LearnChip(Chip(r, teacher, name), r.Met)).ToImmutableArray();
        string fee = lesson.Fee <= 0 ? "No fee" : $"{lesson.Fee} coin";
        string? why = null;
        LearnState state;
        if (lesson.Known)
        {
            state = LearnState.Known;
        }
        else if (lesson.Requirements.FirstOrDefault(r => !r.Met) is { } unmet)
        {
            state = LearnState.Blocked;
            why = $"Not yet: {Chip(unmet, teacher, name).ToLowerInvariant()}.";
        }
        else if (lesson.Fee > coin)
        {
            state = LearnState.Blocked;
            why = $"Not yet: the fee is {lesson.Fee} coin and you have {coin}.";
        }
        else if (unavailable is not null)
        {
            state = LearnState.Blocked;
            why = Sentence(teacher, unavailable);
        }
        else if (lesson.ByDialogue)
        {
            state = LearnState.InConversation;
            why = $"Ask {teacher} for it in conversation.";
        }
        else
        {
            state = LearnState.Ready;
        }
        return new LearnRow(lesson.DefinitionId, name(lesson.DefinitionId), KindWord(lesson.Kind), fee, state, why, chips)
        {
            Teaches = teaches?.Invoke(lesson.DefinitionId),
            Word = Word(lesson, state, unavailable),
        };
    }

    /// <summary>The word a lesson carries: the first unmet requirement's kind, else the fee, else the teacher's hours.</summary>
    private static string Word(LessonView lesson, LearnState state, string? unavailable) => state switch
    {
        LearnState.Known => "Known",
        LearnState.Ready => "Ready",
        LearnState.InConversation => "Ask them",
        _ => lesson.Requirements.FirstOrDefault(r => !r.Met)?.Requirement switch
        {
            SkillBandRequirement => "Needs skill",
            TrustRequirement => "Needs trust",
            PrerequisiteRequirement => "Builds on",
            null => unavailable is not null ? "Not teaching now" : "Short of coin",
            _ => "Needs more",
        },
    };

    public static string KindWord(TeachingKind kind) => kind switch
    {
        TeachingKind.Recipe => "Recipe",
        TeachingKind.Technique => "Technique",
        TeachingKind.Spell => "Formula",
        TeachingKind.BuildingPiece => "Building piece",
        _ => kind.ToString(),
    };

    /// <summary>
    /// A requirement in words, as its chip says it: a skill at its band with what the character has ("Smithing: journeyman · you have 3"),
    /// the teacher's trust in words (never a number), something known first, a deed, a quest's state, or something in the world.
    /// </summary>
    public static string Chip(RequirementCheck check, string teacher, Func<string, string> name) => check.Requirement switch
    {
        SkillBandRequirement skill => $"{name(skill.SkillId)}: {skill.Band.ToLowerInvariant()} · you have {check.Have}",
        TrustRequirement => check.Met ? $"{teacher} trusts you enough" : $"{teacher} must trust you more",
        PrerequisiteRequirement first => check.Met ? $"Knows {name(first.DefinitionId)}" : $"Learn {name(first.DefinitionId)} first",
        DeedRequirement deed => $"{Deed(deed.Condition.Kind)} {name(deed.Condition.SubjectId)}" +
                                (deed.Condition.Count > 1 ? $" {deed.Condition.Count} times · you have {check.Have}" : ""),
        QuestRequirement quest => $"{name(quest.Condition.QuestId)}: {Quest(quest.Condition)}",
        FlagRequirement => check.Met ? "The world is as it must be" : "Something in the world must change first",
        _ => check.Requirement.Key,
    };

    private static string Deed(DeedKind kind) => kind switch
    {
        DeedKind.Crafted => "Make",
        DeedKind.Harvested => "Gather",
        DeedKind.Killed => "Kill",
        DeedKind.Delivered => "Deliver",
        DeedKind.Chose => "Choose",
        DeedKind.Travelled => "Travel to",
        _ => kind.ToString(),
    };

    private static string Quest(QuestStateCondition condition) =>
        (condition.Negated ? "not " : "") + condition.State switch
        {
            "active" => "under way",
            "completed" => "done",
            "failed" => "failed",
            "satisfied" => "ready to hand in",
            var other => Names.Words(other),
        };

    private static string Sentence(string teacher, string reason)
    {
        string said = reason.StartsWith(teacher, StringComparison.Ordinal) ? reason : $"{teacher} {reason}";
        return said.EndsWith('.') ? said : said + ".";
    }
}
