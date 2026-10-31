// UNNAMED World - one stash per character, behind the doors the owner ruled (2026-10-31; GEN-04/05)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Economy;

namespace UNNAMED.World.Runtime;

/// <summary>
/// To the stash: put goods and coin away at a storage point. The facts of the point travel with the command, because the door decides whether it
/// opens and the caller is the one who knows what kind of room it is standing in.
/// </summary>
public sealed record DepositToStashCommand(EntityId Actor, string StationKey, StorageKind Kind, bool RoomRented, bool HomeBuilt,
    bool CampStanding, EntityId? ItemId, long Count, long Coin) : GameCommand(Actor);

/// <summary>From the stash: take goods and coin out at a storage point, under the same door and the same fee.</summary>
public sealed record WithdrawFromStashCommand(EntityId Actor, string StationKey, StorageKind Kind, bool RoomRented, bool HomeBuilt,
    bool CampStanding, string? ItemId, long Count, long Coin) : GameCommand(Actor);

/// <summary>
/// Owns the character's one stash (owner ruling, 2026-10-31): goods and coin held once, reachable from any town's bank, a camp, a rented inn
/// room's chest, or a built home. A bank charges a fee for each movement, and that fee is a coin sink written into the GEN-04 ledger - which is
/// what makes it the ledger's business and not a hidden tax.
///
/// The stash is deliberately in memory on this branch: the object is the seam a save slice will hold, and the ruling's own behaviour (one
/// stash, many doors, a locked room, a bank's fee) is what is tested here.
/// </summary>
internal sealed class StashSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;

    public StashSystem(SystemContext context, EntityId player)
    {
        _context = context;
        _player = player;
    }

    private RuntimeState State => _context.State;

    /// <summary>The one stash: the same goods whichever door they went in by.</summary>
    public Stash Stash { get; } = new();

    public string? Handle(DepositToStashCommand command)
    {
        var point = Point(command.StationKey, command.Kind, command.RoomRented, command.HomeBuilt, command.CampStanding);
        if (StashRules.WhyShut(point) is { } shut)
            return shut;

        long goods = command.ItemId is null ? 0 : command.Count;
        if (goods <= 0 && command.Coin <= 0)
            return "there is nothing to put away";

        if (command.ItemId is { } itemId && goods > 0)
        {
            if (State.Inventory.FirstOrDefault(e => e.ItemId == itemId) is not { } entry)
                return $"{itemId.Value} is not carried";
            if (entry.Count < goods)
                return $"only {entry.Count} carried";
        }

        // A bank charges for the movement, and the fee is paid before anything moves, so a refused fee moves nothing.
        long fee = StashRules.Fee(point, deposit: true);
        if (fee > 0 && _context.Dispatch(new SpendCurrency(fee) { Reason = CoinReasons.Bank }) is { } feeRefused)
            return $"the bank's fee of {fee}: {feeRefused}";

        if (command.ItemId is { } moved && goods > 0)
        {
            // Read what the thing IS while the pack still holds it, then let the pack give it up by count: the stash is the character's own
            // store, not a container standing in a town, so it is keyed by the definition and the next stack joins the same pile.
            string defId = State.Inventory.First(e => e.ItemId == moved).DefId;
            if (_context.Dispatch(new TakeCarried(defId, (int)goods)) is { } refusedGoods)
                return refusedGoods;
            Stash.Put(defId, goods);
        }

        if (command.Coin > 0)
        {
            if (_context.Dispatch(new SpendCurrency((int)command.Coin) { Reason = CoinReasons.Bank }) is { } coinRefused)
                return coinRefused;
            Stash.PutCoin(command.Coin);
        }
        return null;
    }

    public string? Handle(WithdrawFromStashCommand command)
    {
        var point = Point(command.StationKey, command.Kind, command.RoomRented, command.HomeBuilt, command.CampStanding);
        if (StashRules.WhyShut(point) is { } shut)
            return shut;

        if (command.ItemId is null && command.Coin <= 0)
            return "there is nothing to take out";
        if (command.ItemId is { } wanted && !Stash.Take(wanted, command.Count))
            return $"the stash holds {Stash.Count(wanted)} of {wanted}";
        if (command.Coin > 0 && !Stash.TakeCoin(command.Coin))
            return $"the stash holds {Stash.Coin} coin";

        long fee = StashRules.Fee(point, deposit: false);
        if (fee > 0 && _context.Dispatch(new SpendCurrency(fee) { Reason = CoinReasons.Bank }) is { } feeRefused)
            return $"the bank's fee of {fee}: {feeRefused}";

        if (command.ItemId is { } taken && command.Count > 0
            && _context.Dispatch(new GrantItem(taken, (int)command.Count, 0) { SourceRef = command.StationKey }) is { } movedOut)
            return movedOut;
        if (command.Coin > 0 && _context.Dispatch(new AddCurrency((int)command.Coin) { Reason = CoinReasons.Bank }) is { } coinIn)
            return coinIn;
        return null;
    }

    private static StoragePoint Point(string key, StorageKind kind, bool rented, bool built, bool camp) =>
        new(kind, key, RoomRented: rented, HomeBuilt: built, CampStanding: camp);
}
