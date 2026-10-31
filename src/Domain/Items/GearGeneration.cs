using System.Collections.Immutable;

namespace UNNAMED.Domain.Items;

/// <summary>
/// D21 gear generation's authored pieces: a part is one component a gear family is assembled from (a blade, a guard, a grip), a maker is a workshop
/// that builds from a particular set of parts in a particular material palette with one small legible tendency, and a material is a crafting material
/// class with multipliers applied to the finished piece (canon <c>GEAR_GENERATION.md</c> §2).
/// </summary>
public sealed record PartDefinition(
    string Id,
    string Family,
    string Slot,
    string MeshRef,
    ImmutableArray<string> MaterialSlots,
    int TierMin,
    PartStats Stats,
    ImmutableArray<string> Tags)
{
    /// <summary>The item noun this part gives the piece it dominates (canon section 8: a leaf blade is an arming sword). Null for parts that name nothing.</summary>
    public string? Noun { get; init; }

    /// <summary>Which parts of its neighbours it legally fits against, from <c>compat.requires_tags_on</c>: slot to the tags that slot needs.</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<string>> RequiresTagsOn { get; init; } =
        ImmutableSortedDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal);
}

/// <summary>What a part adds to the family baseline, before material and quality (canon §2.2: small bounded deltas).</summary>
public sealed record PartStats(double DamageMult, long ReachAddMm, long WeightAddGrams, double SpeedMult, int DurabilityAdd)
{
    public static readonly PartStats None = new(1.0, 0, 0, 1.0, 0);
}

/// <summary>One part at one weight within its maker's set for a family and slot.</summary>
public sealed record WeightedPart(string PartId, int Weight);

/// <summary>One material at one weight within its maker's palette.</summary>
public sealed record WeightedMaterial(string MaterialId, int Weight);

/// <summary>How a maker's tendency bends the finished piece (§9 caps this).</summary>
public sealed record MakerStats(double WeightMult, double DurabilityMult, double SpeedMult, double DamageMult)
{
    public static readonly MakerStats None = new(1.0, 1.0, 1.0, 1.0);
}

/// <summary>A maker's one legible tendency: the closed enum canon allows, and the small stat bias it applies.</summary>
public sealed record MakerSignature(string Tendency, MakerStats StatBias)
{
    /// <summary>The closed list canon gives (canon §2.3). Anything else is a fallacy: makers do not invent tendencies.</summary>
    public static readonly ImmutableArray<string> Tendencies = ImmutableArray.Create(
        "heavy_and_durable", "light_and_quick", "reach", "balanced", "brittle_keen", "high_socket", "quiet", "loud", "warm_to_hand");
}

/// <summary>What a material does to the finished piece.</summary>
public sealed record MaterialMultipliers(double Damage, double Armor, double Weight, double Durability, double Value)
{
    public static readonly MaterialMultipliers None = new(1.0, 1.0, 1.0, 1.0, 1.0);
}

/// <summary>
/// A workshop: who makes gear, out of which parts, in which materials, and the one tendency its work is known by. A maker never grants a power bonus:
/// it biases weight, durability, reach, speed or sockets within the balance guard rails (canon §2.3, §9).
/// </summary>
public sealed record MakerDefinition(
    string Id,
    string PeopleId,
    string Era,
    string FitFamily,
    ImmutableSortedDictionary<string, ImmutableSortedDictionary<string, ImmutableArray<WeightedPart>>> PartSets,
    ImmutableArray<WeightedMaterial> MaterialPalette,
    MakerSignature Signature,
    ImmutableArray<int> TierRange,
    string NameGrammar,
    string TooltipLineKey)
{
    /// <summary>The written reason this maker's materials and tendencies are what they are (canon §2.3 <c>source_notes</c>).</summary>
    public ImmutableArray<string> SourceNotes { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>The authored words this maker's readable names are built from (canon section 8's maker adjective list, 3-6 words).</summary>
    public ImmutableArray<string> Words { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>The affix flavours this maker's loot leans to, by tag.</summary>
    public ImmutableArray<string> AffixPoolTags { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// A crafting material class - not a stack item: <c>mat.steel.kal_folded</c> names the class, <c>material_ref_item</c> names what crafting consumes
/// (canon §2.4).
/// </summary>
public sealed record MaterialDefinition(string Id, string Class, string RefItemId, MaterialMultipliers Mult, int TierMin)
{
    /// <summary>The material's word in a readable name (canon section 8: only notable materials appear, so plain iron has none).</summary>
    public string? Word { get; init; }

    /// <summary>The closed material classes canon allows.</summary>
    public static readonly ImmutableArray<string> Classes =
        ImmutableArray.Create("metal", "stone", "hide", "wood", "bone", "fibre", "exotic");
}

/// <summary>
/// The assembly block an item carries when it is built from parts (canon §2.5): the family, the slots its family has, and which maker may build it.
/// An item with no assembly block is a plain authored item, exactly as before.
/// </summary>
public sealed record AssemblySpec(string Family, ImmutableArray<string> Slots, string? MakerId, int PartTier)
{
    /// <summary>Whether the family has this slot at all.</summary>
    public bool HasSlot(string slot) => Slots.Contains(slot, StringComparer.Ordinal);
}

/// <summary>
/// What a built instance stores beyond its definition (canon §2.6): the parts it was assembled from, by slot; the material per part material slot; the
/// maker; the part tier it rolled at; and the affixes and sockets the roll gave it. A plain item has none of this.
/// </summary>
public sealed record GearBuild(
    string Family,
    string MakerId,
    int PartTier,
    ImmutableSortedDictionary<string, string> Parts,
    ImmutableSortedDictionary<string, string> Materials,
    ImmutableArray<string> Affixes,
    int Sockets)
{
    /// <summary>The name the build reads as (canon §8). Null when nothing named it.</summary>
    public string? Name { get; init; }
}

/// <summary>Everything D21's craft path needs to turn chosen parts into a build (G3): the parts, the makers and the materials.</summary>
public sealed record GearSetup(
    ImmutableSortedDictionary<string, PartDefinition> Parts,
    ImmutableSortedDictionary<string, MakerDefinition> Makers,
    ImmutableSortedDictionary<string, MaterialDefinition> Materials)
{
    public static readonly GearSetup Empty = new(
        ImmutableSortedDictionary.Create<string, PartDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, MakerDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, MaterialDefinition>(StringComparer.Ordinal));

    /// <summary>
    /// The build a craft makes: the player's picks where they name a part the maker offers for that slot, and the maker's first offering where they do
    /// not - so a Make before the picker is touched is still a whole piece.
    /// </summary>
    public GearBuild BuildOf(AssemblySpec assembly, ImmutableSortedDictionary<string, string>? chosen)
    {
        var maker = Makers[assembly.MakerId!];
        var picked = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (string slot in assembly.Slots)
        {
            var offered = maker.PartSets[assembly.Family][slot];
            if (chosen is not null && chosen.TryGetValue(slot, out string? want) && offered.Any(p => p.PartId == want))
                picked[slot] = want;
            else
                picked[slot] = offered[0].PartId;
        }
        var materials = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (string slot in picked.Keys)
        foreach (string materialSlot in Parts[picked[slot]].MaterialSlots)
            materials[materialSlot] = maker.MaterialPalette[0].MaterialId;
        return new GearBuild(assembly.Family, maker.Id, assembly.PartTier, picked.ToImmutable(), materials.ToImmutable(),
            ImmutableArray<string>.Empty, 0);
    }
}
