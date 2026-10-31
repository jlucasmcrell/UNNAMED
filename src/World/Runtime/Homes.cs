// UNNAMED World - a home of one's own at run time: claiming a plot, building it, furnishing it, and the chest that opens the shared stash
// (HSG-; GAPS_WORK_QUEUE #6, the fresh rebuild of 1G; owner ruling 2026-10-31 on storage points)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Housing;

namespace UNNAMED.World.Runtime;

/// <summary>Claim a plot: the price in coin buys the ground, and the house is built on it afterwards.</summary>
public sealed record ClaimHomeCommand(EntityId Actor, string HomeId) : GameCommand(Actor);

/// <summary>Build the house on a claimed plot, from the goods its definition names.</summary>
public sealed record BuildHomeCommand(EntityId Actor, string HomeId) : GameCommand(Actor);

/// <summary>Set a carried piece of furniture down in the home, at a place inside the plot.</summary>
public sealed record PlaceFurnitureCommand(EntityId Actor, string HomeId, EntityId Item, long XMm, long ZMm, int FacingMdeg = 0)
    : GameCommand(Actor);

/// <summary>Take a piece back up: it goes back into the pack where it stood.</summary>
public sealed record RemoveFurnitureCommand(EntityId Actor, string HomeId, int Index) : GameCommand(Actor);

/// <summary>A plot was claimed: whose, at what price, and when.</summary>
public sealed record HomeClaimed(string HomeId, long Coin, long Tick);

/// <summary>A house was built on a plot, from the goods it took.</summary>
public sealed record HomeBuilt(string HomeId, ImmutableArray<string> Materials, long Tick);

/// <summary>A piece was set down in a home, or taken back up.</summary>
public sealed record HomeFurnished(string HomeId, string ItemId, bool Placed, long Tick);

/// <summary>
/// Homes at run time (HSG-): the plots a character can claim and build on, the furniture they set down in them, and the chest that reaches their
/// one shared stash. The stash belongs to the economy's own system - a home's chest is a door to it, `StorageKind.Home`, and it opens once the
/// house is built. Nothing here keeps a chest of its own, on the owner's ruling that there is one stash and no silos.
/// </summary>
internal sealed class HomesSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly ImmutableSortedDictionary<string, HomeDefinition> _homes;

    public HomesSystem(SystemContext context, SliceOwner owner, EntityId player, ImmutableSortedDictionary<string, HomeDefinition> homes)
    {
        _context = context;
        _owner = owner;
        _player = player;
        _homes = homes;
    }

    private RuntimeState State => _context.State;

    /// <summary>Every plot the pack defines, by id.</summary>
    public ImmutableSortedDictionary<string, HomeDefinition> Definitions => _homes;

    /// <summary>The homes the character holds, by plot id.</summary>
    public ImmutableArray<HomeState> Held => State.Homes;

    public HomeState? Of(string homeId) => State.Homes.FirstOrDefault(h => h.HomeId == homeId);

    /// <summary>The storage point this home's chest answers to: the shared stash, behind a door that opens once the house is up.</summary>
    public StoragePoint? Storage(string homeId) => Of(homeId) is { } home && _homes.TryGetValue(homeId, out var definition)
        ? home.Storage(definition)
        : null;

    public string? Handle(ClaimHomeCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!_homes.TryGetValue(command.HomeId, out var definition))
            return $"there is no plot called {command.HomeId}";
        if (HomeRules.WhyNotClaim(definition, Of(command.HomeId)) is { } refused)
            return refused;

        if (NotAt(definition) is { } away)
            return away;

        // The claim is paid for before the plot changes hands, so a purse that cannot cover it leaves the world as it was.
        if (definition.PriceCoin > 0 && _context.Dispatch(new SpendCurrency((int)definition.PriceCoin)) is { } broke)
            return broke;

        Set(State.Homes.Add(Homes.Claim(definition)));
        _context.Events.Publish(new HomeClaimed(command.HomeId, definition.PriceCoin, tick));
        return null;
    }

    public string? Handle(BuildHomeCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!_homes.TryGetValue(command.HomeId, out var definition))
            return $"there is no plot called {command.HomeId}";
        var home = Of(command.HomeId);
        if (HomeRules.WhyNotBuild(home) is { } refused)
            return refused;

        if (NotAt(definition) is { } away)
            return away;

        // Every good the build needs must be carried, or the whole build is refused: a house is not raised four fifths of the way.
        var carried = State.Inventory
            .GroupBy(e => e.DefId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Count), StringComparer.Ordinal);
        foreach (var material in definition.Materials)
            if (carried.GetValueOrDefault(material.ItemId, 0) < material.Count)
                return $"building it takes {material.Count} of {material.ItemId}, and only {carried.GetValueOrDefault(material.ItemId, 0)} is carried";

        foreach (var material in definition.Materials)
            if (_context.Dispatch(new TakeCarried(material.ItemId, material.Count)) is { } shortBy)
                return shortBy;

        Set(State.Homes.Select(h => h.HomeId == command.HomeId ? Homes.Build(h) : h).ToImmutableArray());
        _context.Events.Publish(new HomeBuilt(command.HomeId, definition.Materials.Select(m => m.ItemId).ToImmutableArray(), tick));
        return null;
    }

    public string? Handle(PlaceFurnitureCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!_homes.TryGetValue(command.HomeId, out var definition))
            return $"there is no plot called {command.HomeId}";
        var home = Of(command.HomeId);
        if (home is null)
            return "that plot is not yours";
        if (NotAt(definition) is { } away)
            return away;
        if (State.Inventory.FirstOrDefault(e => e.ItemId == command.Item) is not { } entry)
            return $"{command.Item.Value} is not carried";

        var placement = new FurniturePlacement(entry.DefId, command.XMm, command.ZMm, HomeRules.NormaliseFacing(command.FacingMdeg));
        if (HomeRules.WhyNotPlace(definition, home, placement) is { } refused)
            return refused;
        if (_context.Dispatch(new TakeCarried(entry.DefId, 1)) is { } taken)
            return taken;

        Set(State.Homes.Select(h => h.HomeId == command.HomeId ? Homes.Place(h, placement) : h).ToImmutableArray());
        _context.Events.Publish(new HomeFurnished(command.HomeId, entry.DefId, true, tick));
        return null;
    }

    public string? Handle(RemoveFurnitureCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var home = Of(command.HomeId);
        if (home is null)
            return "that plot is not yours";
        if (HomeRules.WhyNotRemove(home, command.Index) is { } refused)
            return refused;

        var piece = home.Furniture[command.Index];
        // It goes back into the pack before it leaves the home, so a pack that cannot take it leaves the piece where it stands.
        if (_context.Dispatch(new GrantItem(piece.ItemId, 1, 0) { SourceRef = command.HomeId }) is { } refusedItem)
            return refusedItem;

        Set(State.Homes.Select(h => h.HomeId == command.HomeId ? Homes.Remove(h, command.Index) : h).ToImmutableArray());
        _context.Events.Publish(new HomeFurnished(command.HomeId, piece.ItemId, false, tick));
        return null;
    }

    /// <summary>How far from the plot's centre a body may stand and still work on it: the plot's own floor, and a step past it.</summary>
    private const long ReachBeyondPlotMm = 25_000;

    /// <summary>Why the character is not at this plot, or null: claiming and building are done standing on the ground itself.</summary>
    private string? NotAt(HomeDefinition definition)
    {
        long dx = State.Body.XMm - definition.XMm, dz = State.Body.ZMm - definition.ZMm;
        long reach = definition.RadiusMm + ReachBeyondPlotMm;
        return dx * dx + dz * dz <= reach * reach ? null : $"that plot is not here: {definition.Name} is not in reach";
    }

    private void Set(ImmutableArray<HomeState> homes) => State.SetHomes(_owner, homes);
}
