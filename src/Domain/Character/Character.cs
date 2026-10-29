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

    /// <summary>What the creator shaped beyond the body preset, or null for the preset exactly as it was baked.</summary>
    public CharacterAppearance? Appearance { get; init; }

    /// <summary>
    /// Where the character was raised (CHARACTER_CREATION_AND_LINEAGE.md §4): separate from lineage, it decides where the character starts.
    /// </summary>
    public string OriginId { get; init; } = DefaultOrigin;

    /// <summary>Where a people's free creation point goes (Veth: +1 to any attribute); null is the people's default, and a people with none ignores it.</summary>
    public CharacterAttribute? FreeAttribute { get; init; }

    public const int MaxNameLength = 24;

    /// <summary>The origin every character has until content offers another: the frontier road into Ashen Hollow (owner, 2026-10-24).</summary>
    public const string DefaultOrigin = "origin.frontier_road";

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
        if (Appearance?.Problem() is { } appearance)
            throw new ArgumentException(appearance, nameof(Appearance));
        if (!IsIdOf(OriginId, "origin"))
            throw new ArgumentException($"'{OriginId}' is not an origin ID", nameof(OriginId));
        if (FreeAttribute is { } free && !Enum.IsDefined(free))
            throw new ArgumentException($"Unknown attribute {free}", nameof(FreeAttribute));
        return this with { Name = Normalize(Name) };
    }

    internal static bool IsIdOf(string? id, string prefix) => DefinitionId.IsValid(id) && id!.StartsWith(prefix + ".", StringComparison.Ordinal);
}

/// <summary>
/// What a character is besides its name, kept on the player record (schema 17). A character made before the creator has the default
/// people and body, "they", and no Call.
/// </summary>
public sealed record CharacterIdentity(Pronouns Pronouns, string PeopleId, string BodyPresetId, string? CallId)
{
    public static readonly CharacterIdentity BeforeTheCreator =
        new(Pronouns.They, CharacterSpec.DefaultPeople, CharacterSpec.DefaultBodyPreset, null);

    /// <summary>What the creator shaped beyond the body preset (schema 39), or null for the preset as baked.</summary>
    public CharacterAppearance? Appearance { get; init; }

    /// <summary>Where the character was raised (schema 40). A character made before origins has the frontier road.</summary>
    public string OriginId { get; init; } = CharacterSpec.DefaultOrigin;

    public static CharacterIdentity Of(CharacterSpec spec) =>
        new(spec.Pronouns, spec.PeopleId, spec.BodyPresetId, spec.CallId) { Appearance = spec.Appearance, OriginId = spec.OriginId };
}

/// <summary>
/// A character's look as the HumGen pipeline makes it (schema 39): the stature every scale derives from (RACE_SCALE_SPEC.md §1), the
/// face preset, head and facial hair, outfit and footwear chosen by ID, and each shaping slider moved, in the catalogue's integer units.
/// The domain only keeps the IDs and numbers; presentation resolves them, and a body whose combination has not been baked is drawn as
/// its preset until a bake fills it in. Null choices keep the preset's own.
/// </summary>
public sealed record CharacterAppearance(int StatureMm, string? Face, string? Hair, string? FacialHair, string? Outfit, string? Footwear)
{
    /// <summary>The statures any character can have, the full canon range with margin (RACES.md: 1.40 m Siann to 2.60 m Ondrek).</summary>
    public const int MinStatureMm = 1000, MaxStatureMm = 3000;

    /// <summary>Every slider moved, by ID, in the catalogue's units.</summary>
    public ImmutableSortedDictionary<string, int> Sliders { get; init; } = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);

    /// <summary>Why this appearance cannot be kept, or null when it can.</summary>
    public string? Problem() =>
        StatureMm is < MinStatureMm or > MaxStatureMm ? $"A stature of {StatureMm} mm is outside {MinStatureMm}-{MaxStatureMm} mm"
        : new[] { (Face, "face"), (Hair, "hair"), (FacialHair, "facehair"), (Outfit, "outfit"), (Footwear, "footwear") }
            .FirstOrDefault(c => c.Item1 is not null && !CharacterSpec.IsIdOf(c.Item1, c.Item2)) is { Item1: { } bad, Item2: var kind } ? $"'{bad}' is not a {kind} ID"
        : Sliders.Keys.FirstOrDefault(k => !DefinitionId.IsValid(k)) is { } slider ? $"'{slider}' is not a slider ID"
        : null;

    public bool Equals(CharacterAppearance? other) =>
        other is not null && (StatureMm, Face, Hair, FacialHair, Outfit, Footwear) == (other.StatureMm, other.Face, other.Hair, other.FacialHair, other.Outfit, other.Footwear)
        && Sliders.SequenceEqual(other.Sliders);

    public override int GetHashCode() => HashCode.Combine(StatureMm, Face, Hair, FacialHair, Outfit, Footwear, Sliders.Count);
}

/// <summary>Who a Call puts the character in touch with: an NPC who already trusts them a little.</summary>
public sealed record CallContact(string NpcId, int Trust);

/// <summary>
/// A Call (accepted OD-C03): the starting package a character chooses. It biases attributes, starts two skills, adds kit to the common
/// starting kit and names a contact. It grants nothing another character cannot reach (PROGRESSION.md §5).
/// </summary>
public sealed record CallDefinition(
    string Id,
    string Tagline,
    string Description,
    StartingPackage Package,
    ImmutableArray<StartingItem> Kit,
    CallContact Contact)
{
    /// <summary>A Call's attribute bias, in points: every Call spends exactly this many (C11).</summary>
    public const int BiasPoints = 4;

    /// <summary>A Call's starting skills: exactly this many, each at <see cref="SkillLevel"/>.</summary>
    public const int SkillCount = 2;

    public const int SkillLevel = 15;
    /// <summary>
    /// The line a Call's own contact says when they meet somebody of that Call (C11): the recognition that makes a Call a life rather than a
    /// list of starting numbers. Null when a Call names no contact line.
    /// </summary>
    public CallRecognition? Recognition { get; init; }
}

/// <summary>A people (race) a character can belong to, and the names its members are given.</summary>
public sealed record PeopleDefinition(string Id, bool Playable, NameSource Names)
{
    /// <summary>Its innate biology and inherited aptitude (RACE_ABILITIES_PROPOSAL.md): every member's, player or not.</summary>
    public PeopleTraits Traits { get; init; } = PeopleTraits.None;

    /// <summary>What the creator says of it; null for a people content gives no card.</summary>
    public PeopleCard? Card { get; init; }

    /// <summary>Its racial abilities (<c>ability</c> of <c>class: people</c>), by ID, in content order.</summary>
    public ImmutableArray<string> Abilities { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Its heritage: the keepsakes a new character of the people carries over the common kit and the Call's (owner, 2026-10-23). Heritage is
    /// identity, never power.
    /// </summary>
    public ImmutableArray<StartingItem> Heritage { get; init; } = ImmutableArray<StartingItem>.Empty;
}

/// <summary>
/// Where a character was raised (<c>config.origins</c>; CHARACTER_CREATION_AND_LINEAGE.md §4): what the creator calls it, and where a new
/// character of it starts - <see cref="StartLocationId"/>, a named place, or with none the region's road in. Today the only origin is the
/// frontier road; a start town is an origin naming its place.
/// </summary>
public sealed record OriginDefinition(string Id, string Name, string Description)
{
    public string? StartLocationId { get; init; }
}

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

/// <summary>A joint's rest-pose offset for a body, in millimetres in the joint's parent space.</summary>
public sealed record BoneOffset(int X, int Y, int Z);

/// <summary>
/// A body a character can be made with (content kind <c>body.*</c>, WP17): its people, its type (the creator's Masculine / Feminine, independent
/// of pronouns) and build, the tape measurements of its model and how its skeleton differs from the shared one - rest offsets per joint,
/// and the arm splay its build plays every clip with so the arms swing clear of its own sides. A character's body is fixed at creation
/// (PJ04:259-260); the models are baked per body, so nothing about it changes at runtime. <see cref="Offered"/> is false for a build the
/// clipping gate has not passed: content keeps it, the creator does not offer it.
/// </summary>
public sealed record BodyPreset(
    string Id,
    string PeopleId,
    string Type,
    string TypeLabel,
    string Build,
    string BuildLabel,
    int Order,
    bool Offered,
    int ArmSplayDegrees,
    ImmutableSortedDictionary<string, int> MeasurementsMm,
    ImmutableSortedDictionary<string, BoneOffset> RestOffsetsMm);

/// <summary>The peoples, Calls and bodies content defines: what the creator offers.</summary>
public sealed record CharacterSetup(
    ImmutableSortedDictionary<string, PeopleDefinition> Peoples,
    ImmutableSortedDictionary<string, CallDefinition> Calls)
{
    public static readonly CharacterSetup None = new(
        ImmutableSortedDictionary.Create<string, PeopleDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, CallDefinition>(StringComparer.Ordinal));

    /// <summary>Every body preset by ID; empty for a pack with none (test fixtures), where any <c>body.*</c> ID is accepted.</summary>
    public ImmutableSortedDictionary<string, BodyPreset> Bodies { get; init; } = ImmutableSortedDictionary.Create<string, BodyPreset>(StringComparer.Ordinal);

    /// <summary>Every racial ability by ID (<c>ability</c> of <c>class: people</c>).</summary>
    public ImmutableSortedDictionary<string, PeopleAbility> Abilities { get; init; } = ImmutableSortedDictionary.Create<string, PeopleAbility>(StringComparer.Ordinal);

    /// <summary>The origins the creator offers; empty for a pack with none, where only the default origin is accepted.</summary>
    public ImmutableSortedDictionary<string, OriginDefinition> Origins { get; init; } = ImmutableSortedDictionary.Create<string, OriginDefinition>(StringComparer.Ordinal);

    /// <summary>A people's traits; a people content does not define has none.</summary>
    public PeopleTraits TraitsOf(string? peopleId) =>
        peopleId is not null && Peoples.TryGetValue(peopleId, out var people) ? people.Traits : PeopleTraits.None;

    /// <summary>A people's racial abilities, in content order.</summary>
    public ImmutableArray<PeopleAbility> AbilitiesOf(string? peopleId) =>
        peopleId is not null && Peoples.TryGetValue(peopleId, out var people)
            ? people.Abilities.Select(id => Abilities[id]).ToImmutableArray()
            : ImmutableArray<PeopleAbility>.Empty;

    /// <summary>The ability of this people that does <paramref name="action"/>, or null.</summary>
    public PeopleAbility? AbilityOf(string? peopleId, PeopleAction action) => AbilitiesOf(peopleId).FirstOrDefault(a => a.Action == action);

    /// <summary>
    /// The creation grant a character of this spec's people takes (§1): the people's fixed grant, plus its free points on the attribute
    /// the spec chose (or the people's default). Each line is its own one-time <see cref="GrantSource.Race"/> grant.
    /// </summary>
    public ImmutableArray<AttributeGrant> RaceGrants(CharacterSpec spec)
    {
        var traits = TraitsOf(spec.PeopleId);
        var points = traits.AttributeGrant.ToBuilder();
        if (traits.FreeAttributePoints > 0 && (spec.FreeAttribute ?? traits.FreeAttributeDefault) is var free)
            points[free] = points.GetValueOrDefault(free) + traits.FreeAttributePoints;
        return points.Where(p => p.Value != 0)
            .Select(p => new AttributeGrant(p.Key, p.Value, GrantSource.Race, $"{spec.PeopleId}.{ProgressionKeys.Key(p.Key)}"))
            .ToImmutableArray();
    }

    /// <summary>The peoples a player may choose, in ID order.</summary>
    public ImmutableArray<PeopleDefinition> PlayablePeoples => Peoples.Values.Where(p => p.Playable).ToImmutableArray();

    /// <summary>The bodies the creator offers for a people, by type and then build order.</summary>
    public ImmutableArray<BodyPreset> OfferedBodies(string peopleId) =>
        Bodies.Values.Where(b => b.Offered && b.PeopleId == peopleId).OrderBy(b => b.Type, StringComparer.Ordinal).ThenBy(b => b.Order).ToImmutableArray();

    /// <summary>
    /// Throws when the spec names a people or Call this content does not define, a people the player cannot choose, or a body this content
    /// does not offer that people. <see cref="CharacterSpec.DefaultBodyPreset"/> is always accepted.
    /// </summary>
    public void Check(CharacterSpec spec)
    {
        if (!Peoples.TryGetValue(spec.PeopleId, out var people) || !people.Playable)
            throw new ArgumentException($"{spec.PeopleId} is not a people a character can be made from", nameof(spec));
        if (spec.CallId is { } call && !Calls.ContainsKey(call))
            throw new ArgumentException($"{call} is not a defined Call", nameof(spec));
        if (Origins.Count > 0 ? !Origins.ContainsKey(spec.OriginId) : spec.OriginId != CharacterSpec.DefaultOrigin)
            throw new ArgumentException($"{spec.OriginId} is not a defined origin", nameof(spec));

        if (Bodies.Count > 0 && spec.BodyPresetId != CharacterSpec.DefaultBodyPreset
            && (!Bodies.TryGetValue(spec.BodyPresetId, out var body) || !body.Offered || body.PeopleId != spec.PeopleId))
            throw new ArgumentException($"{spec.BodyPresetId} is not a body a {spec.PeopleId} character can be made with", nameof(spec));
    }
}

/// <summary>What a Call's contact says when they recognise the character's Call, and who says it.</summary>
public sealed record CallRecognition(string NpcId, string Line);
