// UNNAMED Domain - what a technique asks of a character before it can be learned or used (PRG-29; canon 04:§2.1-2.3, PROGRESSION.md:254)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Combat;

/// <summary>
/// The kinds of thing a technique can ask for (PRG-29): the teacher-side gates the codebase already had, plus the four it did not - an
/// attribute, the equipment in hand, the body the character was born in, a knowledge record, and social standing. One record covers every
/// kind so that a technique learned from a book, a quest or a discovery is gated by the same words as one taught at a bench.
/// </summary>
public enum TechniqueRequirementKind
{
    /// <summary>A skill at least at a level (a band's floor, as teaching names it).</summary>
    Skill,

    /// <summary>An attribute at least at a value (Might, Agility, Precision... §4.1).</summary>
    Attribute,

    /// <summary>Another technique, formula or recipe already known.</summary>
    Known,

    /// <summary>A kind of thing the character must be carrying or wearing.</summary>
    Equipment,

    /// <summary>The people or lineage the character must be (a body cannot be argued with).</summary>
    Body,

    /// <summary>A knowledge record - something read, heard or worked out.</summary>
    Knowledge,

    /// <summary>Standing with someone: a relationship value at least <see cref="TechniqueRequirement.Min"/>.</summary>
    Standing,
}

/// <summary>One thing a technique asks for (PRG-29): what kind, which one, and how much.</summary>
public sealed record TechniqueRequirement(TechniqueRequirementKind Kind, string? Ref, int Min)
{
    /// <summary>The requirement's kind as content names it.</summary>
    public string Key => Kind.ToString().ToLowerInvariant();

    /// <summary>A requirement always says what it is asking about: an attribute with no name or a body with no id is a content error.</summary>
    public string? Problem() =>
        Kind is TechniqueRequirementKind.Skill or TechniqueRequirementKind.Attribute or TechniqueRequirementKind.Known
            or TechniqueRequirementKind.Equipment or TechniqueRequirementKind.Body or TechniqueRequirementKind.Knowledge
            or TechniqueRequirementKind.Standing
            ? (string.IsNullOrWhiteSpace(Ref) ? $"{Key} names what it requires" : null)
            : $"'{Key}' is not a requirement kind";
}

/// <summary>What the character is, as far as a requirement can ask: the facts, no rules.</summary>
public sealed record TechniqueCandidate(
    ImmutableSortedDictionary<string, int> Skills,
    ImmutableSortedDictionary<CharacterAttribute, int> Attributes,
    ImmutableSortedSet<string> Known,
    ImmutableSortedSet<string> Equipment,
    string Body,
    ImmutableSortedSet<string> Knowledge,
    ImmutableSortedDictionary<string, int> Standing)
{
    public static TechniqueCandidate Empty { get; } = new(
        ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<CharacterAttribute, int>(),
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal),
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal),
        "",
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal));
}

/// <summary>A requirement and whether it was met, with the words for why not - the "why can't I" read (PRG-35) gets them from here.</summary>
public sealed record TechniqueRequirementVerdict(TechniqueRequirement Requirement, bool Met, string Why);

/// <summary>
/// The rule (PRG-29): every requirement must be met, and the refusal says which one and in the player's own words. Deterministic and
/// side-effect free, so the Learn column, the quick bar and a test all read the same answer.
/// </summary>
public static class TechniqueRequirements
{
    /// <summary>Every requirement as met or not, in the order content wrote them.</summary>
    public static ImmutableArray<TechniqueRequirementVerdict> Check(ImmutableArray<TechniqueRequirement> requirements, TechniqueCandidate who) =>
        requirements.IsDefaultOrEmpty
            ? ImmutableArray<TechniqueRequirementVerdict>.Empty
            : requirements.Select(r => Verdict(r, who)).ToImmutableArray();

    /// <summary>Whether every requirement is met: the one question a learner or a user asks.</summary>
    public static bool Met(ImmutableArray<TechniqueRequirement> requirements, TechniqueCandidate who) =>
        Check(requirements, who).All(v => v.Met);

    /// <summary>The first requirement not met, with its reason; null when the character may have it.</summary>
    public static TechniqueRequirementVerdict? FirstUnmet(ImmutableArray<TechniqueRequirement> requirements, TechniqueCandidate who) =>
        Check(requirements, who).FirstOrDefault(v => !v.Met);

    private static TechniqueRequirementVerdict Verdict(TechniqueRequirement requirement, TechniqueCandidate who)
    {
        if (requirement.Problem() is { } problem)
            return new TechniqueRequirementVerdict(requirement, false, problem);

        switch (requirement.Kind)
        {
            case TechniqueRequirementKind.Skill:
                int level = who.Skills.GetValueOrDefault(requirement.Ref!, 0);
                return level >= requirement.Min
                    ? new TechniqueRequirementVerdict(requirement, true, "")
                    : new TechniqueRequirementVerdict(requirement, false, $"Short of {Name(requirement.Ref!)}: {level} of {requirement.Min}");

            case TechniqueRequirementKind.Attribute:
                var attribute = Enum.TryParse<CharacterAttribute>(requirement.Ref, ignoreCase: true, out var parsed) ? parsed : (CharacterAttribute?)null;
                if (attribute is not { } named)
                    return new TechniqueRequirementVerdict(requirement, false, $"'{requirement.Ref}' is not an attribute");
                int value = who.Attributes.GetValueOrDefault(named, 0);
                return value >= requirement.Min
                    ? new TechniqueRequirementVerdict(requirement, true, "")
                    : new TechniqueRequirementVerdict(requirement, false, $"Short of {named}: {value} of {requirement.Min}");

            case TechniqueRequirementKind.Known:
                return who.Known.Contains(requirement.Ref!)
                    ? new TechniqueRequirementVerdict(requirement, true, "")
                    : new TechniqueRequirementVerdict(requirement, false, $"Needs {Name(requirement.Ref!)} first");

            case TechniqueRequirementKind.Equipment:
                return who.Equipment.Contains(requirement.Ref!)
                    ? new TechniqueRequirementVerdict(requirement, true, "")
                    : new TechniqueRequirementVerdict(requirement, false, $"Needs {Name(requirement.Ref!)} in hand");

            case TechniqueRequirementKind.Body:
                return string.Equals(who.Body, requirement.Ref, StringComparison.Ordinal)
                    ? new TechniqueRequirementVerdict(requirement, true, "")
                    : new TechniqueRequirementVerdict(requirement, false, $"Body cannot: {Name(requirement.Ref!)} only");

            case TechniqueRequirementKind.Knowledge:
                return who.Knowledge.Contains(requirement.Ref!)
                    ? new TechniqueRequirementVerdict(requirement, true, "")
                    : new TechniqueRequirementVerdict(requirement, false, $"Needs the knowledge: {Name(requirement.Ref!)}");

            default:
                int standing = who.Standing.GetValueOrDefault(requirement.Ref!, 0);
                return standing >= requirement.Min
                    ? new TechniqueRequirementVerdict(requirement, true, "")
                    : new TechniqueRequirementVerdict(requirement, false, $"Not in standing with {Name(requirement.Ref!)}: {standing} of {requirement.Min}");
        }
    }

    /// <summary>The last part of a definition id, which is what a player is shown: <c>skill.one_hand_blade</c> reads "one hand blade".</summary>
    private static string Name(string id)
    {
        int dot = id.LastIndexOf('.');
        return (dot >= 0 && dot + 1 < id.Length ? id[(dot + 1)..] : id).Replace('_', ' ');
    }
}
