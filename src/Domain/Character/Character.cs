// UNNAMED Domain - who a new character is: the creator's spec, peoples, Calls and the Suggest name source
// (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.2, WP2; CHARACTER_CREATION_AND_LINEAGE.md)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Character;

/// <summary>How the game refers to the character. A choice of its own, never derived from the body or the name.</summary>
public enum Pronouns { He, She, They }

public static class PronounKeys
{
    public static string Key(Pronouns value) => value switch
    {
        Pronouns.He => "he",
        Pronouns.She => "she",
        Pronouns.They => "they",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static Pronouns? Parse(string? key) => key switch
    {
        "he" => Pronouns.He,
        "she" => Pronouns.She,
        "they" => Pronouns.They,
        _ => null,
    };
}

/// <summary>
/// Where a new character first stands (PJ06 §8): on the road below the north-west crest, walking in ("The Road In", the opening), or at
/// the Ashen Waystone, the veteran's skip. The world is the same either way: the road holds no required content.
/// </summary>
public enum StartPoint { Waystone, Road }

/// <summary>
/// Everything the creator decides about a new character. There are no World Rules or difficulty fields: every world runs on the same
/// rules (owner ruling 2026-10-17). <see cref="CallId"/> is null only for a character made before Calls existed.
/// </summary>
public sealed record CharacterSpec(string Name, Pronouns Pronouns, string PeopleId, string BodyPresetId, string? CallId)
{
    /// <summary>Where the character first stands. Not kept: once the world runs, the character is wherever they walked.</summary>
    public StartPoint Start { get; init; } = StartPoint.Waystone;

    public const int MaxNameLength = 24;

    /// <summary>The people every character belongs to until the creator offers another (only the Veth are in the slice).</summary>
    public const string DefaultPeople = "people.veth";

    /// <summary>The body preset of a character the creator did not shape. Presentation resolves preset IDs; the domain only keeps them.</summary>
    public const string DefaultBodyPreset = "body.default";

    /// <summary>The name a character has with nothing chosen: the slice's fixed acceptance character (content bible §14).</summary>
    public const string DefaultName = "Wanderer";

    /// <summary>A spec with only a name and a Call chosen: the people, body and pronouns everyone starts from.</summary>
    public static CharacterSpec Quick(string name, string? callId) => new(name, Pronouns.They, DefaultPeople, DefaultBodyPreset, callId);

    /// <summary>The name as it will be kept: trimmed, with runs of spaces collapsed.</summary>
    public static string Normalize(string name) => string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Why <paramref name="name"/> cannot be a character's name, or null when it can (1-24 characters, no control characters).</summary>
    public static string? NameProblem(string? name)
    {
        string normalized = Normalize(name ?? "");
        if (normalized.Length == 0)
            return "A name needs at least one character";
        if (normalized.Length > MaxNameLength)
            return $"A name is at most {MaxNameLength} characters";
        if (normalized.Any(char.IsControl))
            return "A name cannot hold control characters";
        return null;
    }

    /// <summary>This spec with its name normalized; throws naming the first field that is invalid.</summary>
    public CharacterSpec Validated()
    {
        if (NameProblem(Name) is { } problem)
            throw new ArgumentException(problem, nameof(Name));
        if (!Enum.IsDefined(Pronouns))
            throw new ArgumentException($"Unknown pronouns {Pronouns}", nameof(Pronouns));
        if (!IsIdOf(PeopleId, "people"))
            throw new ArgumentException($"'{PeopleId}' is not a people ID", nameof(PeopleId));
        if (!IsIdOf(BodyPresetId, "body"))
            throw new ArgumentException($"'{BodyPresetId}' is not a body preset ID", nameof(BodyPresetId));
        if (CallId is not null && !IsIdOf(CallId, "call"))
            throw new ArgumentException($"'{CallId}' is not a Call ID", nameof(CallId));
        if (!Enum.IsDefined(Start))
            throw new ArgumentException($"Unknown start {Start}", nameof(Start));
        return this with { Name = Normalize(Name) };
    }

    private static bool IsIdOf(string? id, string prefix) => DefinitionId.IsValid(id) && id!.StartsWith(prefix + ".", StringComparison.Ordinal);
}

/// <summary>
/// What a character is besides its name, kept on the player record (schema 17). A character made before the creator has the default
/// people and body, "they", and no Call.
/// </summary>
public sealed record CharacterIdentity(Pronouns Pronouns, string PeopleId, string BodyPresetId, string? CallId)
{
    public static readonly CharacterIdentity BeforeTheCreator =
        new(Pronouns.They, CharacterSpec.DefaultPeople, CharacterSpec.DefaultBodyPreset, null);

    public static CharacterIdentity Of(CharacterSpec spec) => new(spec.Pronouns, spec.PeopleId, spec.BodyPresetId, spec.CallId);
}

/// <summary>Who a Call puts the character in touch with: an NPC who already trusts them a little.</summary>
public sealed record CallContact(string NpcId, int Trust);

/// <summary>
/// A Call (accepted OD-C03): the starting package a character chooses. It biases attributes, starts two skills, adds kit to the common
/// starting kit and names a contact. It grants nothing another character cannot reach (PROGRESSION.md §5).
/// </summary>
public sealed record CallDefinition(
    string Id,
    StartingPackage Package,
    ImmutableArray<StartingItem> Kit,
    CallContact Contact)
{
    /// <summary>A Call's attribute bias, in points: every Call spends exactly this many (C11).</summary>
    public const int BiasPoints = 4;

    /// <summary>A Call's starting skills: exactly this many, each at <see cref="SkillLevel"/>.</summary>
    public const int SkillCount = 2;

    public const int SkillLevel = 15;
}

/// <summary>A people (race) a character can belong to, and the names its members are given.</summary>
public sealed record PeopleDefinition(string Id, bool Playable, NameSource Names);

/// <summary>
/// A people's names, as the canon lists give them. The creator's Suggest button draws from it, and so can anything else that needs a
/// name for a member of that people (the role residents and adventurers of later work): it never invents a name. Draws are pure
/// functions of the sample given, so a caller that must be deterministic passes an <c>RngChannel</c> sample.
/// </summary>
public sealed class NameSource
{
    public NameSource(IEnumerable<string> names)
    {
        Names = names.Select(CharacterSpec.Normalize).Distinct(StringComparer.Ordinal).ToImmutableArray();
        foreach (string name in Names)
        {
            if (CharacterSpec.NameProblem(name) is { } problem)
                throw new ArgumentException($"'{name}': {problem}", nameof(names));
        }
    }

    /// <summary>Every name, in the order the content lists them.</summary>
    public ImmutableArray<string> Names { get; }

    public bool IsEmpty => Names.IsEmpty;

    /// <summary>The name a sample picks.</summary>
    public string Pick(ulong sample) =>
        IsEmpty ? throw new InvalidOperationException("This people has no names to suggest") : Names[(int)(sample % (ulong)Names.Length)];

    /// <summary>
    /// The name a sample picks, passing over names already taken, or null when every name is: a settlement's residents never share one
    /// while the list lasts.
    /// </summary>
    public string? PickUnused(ulong sample, IReadOnlySet<string> taken)
    {
        if (IsEmpty)
            return null;
        int start = (int)(sample % (ulong)Names.Length);
        for (int i = 0; i < Names.Length; i++)
        {
            string name = Names[(start + i) % Names.Length];
            if (!taken.Contains(name))
                return name;
        }
        return null;
    }
}

/// <summary>The peoples and Calls content defines: what the creator offers.</summary>
public sealed record CharacterSetup(
    ImmutableSortedDictionary<string, PeopleDefinition> Peoples,
    ImmutableSortedDictionary<string, CallDefinition> Calls)
{
    public static readonly CharacterSetup None = new(
        ImmutableSortedDictionary.Create<string, PeopleDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, CallDefinition>(StringComparer.Ordinal));

    /// <summary>The peoples a player may choose, in ID order.</summary>
    public ImmutableArray<PeopleDefinition> PlayablePeoples => Peoples.Values.Where(p => p.Playable).ToImmutableArray();

    /// <summary>Throws when the spec names a people or Call this content does not define, or a people the player cannot choose.</summary>
    public void Check(CharacterSpec spec)
    {
        if (!Peoples.TryGetValue(spec.PeopleId, out var people) || !people.Playable)
            throw new ArgumentException($"{spec.PeopleId} is not a people a character can be made from", nameof(spec));
        if (spec.CallId is { } call && !Calls.ContainsKey(call))
            throw new ArgumentException($"{call} is not a defined Call", nameof(spec));
    }
}
