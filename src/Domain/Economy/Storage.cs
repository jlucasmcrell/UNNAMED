// UNNAMED Domain - the shared stash and the storage points that reach it (owner ruling 2026-10-31; GEN-04/05; ECO-)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Economy;

/// <summary>
/// A place the character's stash can be reached from (owner ruling, 2026-10-31): a bank in a town, the chest of a rented inn room, a camp, or a
/// built home. There is ONE stash per character - the ruling is explicit - and these are only the doors to it.
/// </summary>
public enum StorageKind
{
    /// <summary>A town bank: always open, and it may charge a fee, which is a coin sink the ledger writes down.</summary>
    Bank,

    /// <summary>The chest of a rented inn room: reachable only while the room is rented, and shut the moment the rent lapses.</summary>
    InnRoomChest,

    /// <summary>A camp the character has made: always reachable while it stands.</summary>
    Camp,

    /// <summary>A built home: the hook for housing, always reachable once the house is built.</summary>
    Home,
}

/// <summary>A storage point as the rules see it: its kind, and the facts that decide whether it opens.</summary>
public sealed record StoragePoint(StorageKind Kind, string Key, bool RoomRented = false, bool HomeBuilt = false, bool CampStanding = false);

/// <summary>
/// The rules of the stash (owner ruling 2026-10-31): which door is open, and what a bank charges for being a bank. The stash itself is one per
/// character, so nothing here is per place except the door and the fee.
/// </summary>
public static class StashRules
{
    /// <summary>The coin a bank charges for one deposit (a coin sink, written to the GEN-04 ledger). Nothing else charges.</summary>
    public const long BankDepositFee = 1;

    /// <summary>The coin a bank charges for one withdrawal. Nothing else charges.</summary>
    public const long BankWithdrawFee = 1;

    /// <summary>
    /// Whether this door opens. A bank and a camp do; an inn room only while the room is rented; a home only once it is built - which is the
    /// housing hook, honoured rather than pretended.
    /// </summary>
    public static bool Opens(StoragePoint point) => point.Kind switch
    {
        StorageKind.Bank => true,
        StorageKind.Camp => point.CampStanding,
        StorageKind.InnRoomChest => point.RoomRented,
        StorageKind.Home => point.HomeBuilt,
        _ => false,
    };

    /// <summary>The words for a door that will not open, in the player's own language: the reason a refusal reads out.</summary>
    public static string? WhyShut(StoragePoint point) => Opens(point) ? null : point.Kind switch
    {
        StorageKind.InnRoomChest => "the room is not rented: the chest is locked",
        StorageKind.Camp => "there is no camp here",
        StorageKind.Home => "no home is built here yet",
        _ => "the stash cannot be reached here",
    };

    /// <summary>What this door charges for one movement of goods or coin: a bank charges, a camp and a rented room and a home do not.</summary>
    public static long Fee(StoragePoint point, bool deposit) => point.Kind == StorageKind.Bank ? (deposit ? BankDepositFee : BankWithdrawFee) : 0;
}

/// <summary>
/// One character's stash: goods and coin, held in one place however many towns it can be reached from. It is deliberately small and plain - the
/// authoritative character purse and inventory stay where they are, and this is the store that outlives a town.
/// </summary>
public sealed class Stash
{
    private readonly Dictionary<string, long> _goods = new(StringComparer.Ordinal);

    /// <summary>Coin kept in the stash, apart from the purse the character carries.</summary>
    public long Coin { get; private set; }

    /// <summary>The goods in the stash, in a fixed order, as (definition id, count).</summary>
    public ImmutableArray<(string ItemId, long Count)> Goods => _goods
        .Where(e => e.Value > 0)
        .OrderBy(e => e.Key, StringComparer.Ordinal)
        .Select(e => (e.Key, e.Value))
        .ToImmutableArray();

    public long Count(string itemId) => _goods.GetValueOrDefault(itemId, 0);

    /// <summary>Put goods in. A count of nothing changes nothing.</summary>
    public void Put(string itemId, long count)
    {
        if (count <= 0 || string.IsNullOrWhiteSpace(itemId))
            return;
        _goods[itemId] = Count(itemId) + count;
    }

    /// <summary>Take goods out, refusing to take more than is there rather than going negative.</summary>
    public bool Take(string itemId, long count)
    {
        if (count <= 0)
            return true;
        long held = Count(itemId);
        if (held < count)
            return false;
        _goods[itemId] = held - count;
        return true;
    }

    public void PutCoin(long coin)
    {
        if (coin > 0)
            Coin += coin;
    }

    /// <summary>Take coin out, refusing to take more than is kept.</summary>
    public bool TakeCoin(long coin)
    {
        if (coin <= 0)
            return true;
        if (Coin < coin)
            return false;
        Coin -= coin;
        return true;
    }
}
