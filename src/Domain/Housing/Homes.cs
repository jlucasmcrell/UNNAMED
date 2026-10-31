// UNNAMED Domain - a home of one's own: the plot, the furniture that goes in it, and the chest that reaches the shared stash
// (HSG-; GAPS_WORK_QUEUE #6, the rebuild of 1G; owner ruling 2026-10-31 on storage points)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Economy;

namespace UNNAMED.Domain.Housing;

/// <summary>One thing a home is built from: an item the character must bring, and how many of it.</summary>
public sealed record HomeMaterial(string ItemId, int Count);

/// <summary>
/// A home as the content defines it: where the plot is, what it costs to claim, what building it takes, and the key its chest answers to. The
/// plot is claimed, then built; nothing else about a home is content - the furniture is whatever the character carries and sets down.
/// </summary>
public sealed record HomeDefinition(
    string Id,
    string RegionId,
    long XMm,
    long ZMm,
    long PriceCoin,
    string Name)
{
    /// <summary>The goods building it takes. Empty means the plot comes built once it is claimed.</summary>
    public ImmutableArray<HomeMaterial> Materials { get; init; } = ImmutableArray<HomeMaterial>.Empty;

    /// <summary>How far round the plot a piece of furniture may stand, in millimetres.</summary>
    public long RadiusMm { get; init; } = 8_000;

    /// <summary>The key this home's chest answers to, and the key its storage point is known by.</summary>
    public string ChestKey => $"{Id}.chest";
}

/// <summary>A piece of furniture standing in a home: what it is, where it stands, and which way it faces.</summary>
public sealed record FurniturePlacement(string ItemId, long XMm, long ZMm, int FacingMdeg);

/// <summary>A home the character holds: which plot, whether it is built yet, and what stands in it.</summary>
public sealed record HomeState(string HomeId, bool Built, ImmutableArray<FurniturePlacement> Furniture)
{
    public static HomeState Claimed(string homeId) => new(homeId, false, ImmutableArray<FurniturePlacement>.Empty);

    /// <summary>Where this home's chest stands: the plot itself, so a body must be at the plot to open it.</summary>
    public StoragePoint Storage(HomeDefinition definition) =>
        new(StorageKind.Home, definition.ChestKey, HomeBuilt: Built);
}

/// <summary>
/// The rules of a home (HSG-): what claiming and building take, what may be set down and where, and what the door of its chest needs to open.
/// Nothing here is saved - the pouch of coin and the goods are the character's, and the rules only say what they must be.
/// </summary>
public static class HomeRules
{
    /// <summary>How many pieces may stand in one home: a house, not a warehouse.</summary>
    public const int MaxFurniture = 24;

    /// <summary>
    /// The closest two pieces may stand, in millimetres: everything a home holds takes up its own floor. Furniture is set down by hand rather than
    /// stacked, so two things inside this distance are one thing placed twice.
    /// </summary>
    public const long ClearanceMm = 900;

    /// <summary>The most a placed piece may be turned: zero and 359 are the same wall.</summary>
    public static int NormaliseFacing(int facingMdeg) => ((facingMdeg % 360) + 360) % 360;

    /// <summary>Why this home cannot be claimed, or null. A home already claimed cannot be claimed again.</summary>
    public static string? WhyNotClaim(HomeDefinition definition, HomeState? held) =>
        held is not null ? $"the plot at {definition.Name} is already yours" : null;

    /// <summary>Why this home cannot be built, or null: it must be claimed, and not already built.</summary>
    public static string? WhyNotBuild(HomeState? held) =>
        held is null ? "that plot is not yours to build on"
        : held.Built ? "the house is already built"
        : null;

    /// <summary>
    /// Why a piece cannot be set down here, or null: it must be inside the plot, clear of what already stands there, and the home must have room.
    /// </summary>
    public static string? WhyNotPlace(HomeDefinition definition, HomeState home, FurniturePlacement placement)
    {
        if (!home.Built)
            return "the house is not built yet";
        if (home.Furniture.Length >= MaxFurniture)
            return $"a home holds at most {MaxFurniture} pieces";
        long dx = placement.XMm - definition.XMm, dz = placement.ZMm - definition.ZMm;
        if (dx * dx + dz * dz > definition.RadiusMm * definition.RadiusMm)
            return $"that is outside the plot at {definition.Name}";
        if (home.Furniture.Any(f => Within(f, placement)))
            return "something already stands there";
        return null;
    }

    /// <summary>Why a piece cannot be taken up, or null.</summary>
    public static string? WhyNotRemove(HomeState home, int index) =>
        index < 0 || index >= home.Furniture.Length ? "there is no such piece in this home" : null;

    /// <summary>Whether two placements are closer than the clearance a home keeps between what stands in it.</summary>
    public static bool Within(FurniturePlacement a, FurniturePlacement b)
    {
        long dx = a.XMm - b.XMm, dz = a.ZMm - b.ZMm;
        return dx * dx + dz * dz < ClearanceMm * ClearanceMm;
    }
}

/// <summary>
/// A home's transitions, as pure functions: the runtime spends the coin and takes the goods, then asks these for the state that follows, so what a
/// home becomes is testable without a world.
/// </summary>
public static class Homes
{
    /// <summary>The plot is claimed: held, not yet built.</summary>
    public static HomeState Claim(HomeDefinition definition) => HomeState.Claimed(definition.Id);

    /// <summary>The house is built, keeping whatever was already standing on the plot.</summary>
    public static HomeState Build(HomeState home) => home with { Built = true };

    /// <summary>A piece is set down, in the order pieces are placed (so the list a home shows is the order it was furnished in).</summary>
    public static HomeState Place(HomeState home, FurniturePlacement placement) =>
        home with { Furniture = home.Furniture.Add(placement with { FacingMdeg = HomeRules.NormaliseFacing(placement.FacingMdeg) }) };

    /// <summary>A piece is taken up again: the furniture after it keeps its order.</summary>
    public static HomeState Remove(HomeState home, int index) => home with { Furniture = home.Furniture.RemoveAt(index) };

    /// <summary>Whether the character may open this home's chest: only once the house is built.</summary>
    public static bool ChestOpens(HomeState? home, HomeDefinition definition) =>
        home is not null && StashRules.Opens(home.Storage(definition));
}
