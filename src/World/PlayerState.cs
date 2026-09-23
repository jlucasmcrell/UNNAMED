// UNNAMED World - player state (PERSISTENCE.md §5.1)
// No Godot references - pure C#

using System.Buffers.Binary;
using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World;

/// <summary>An inventory stack held by the player, keyed by its item instance ID.</summary>
public sealed record InventoryEntry(EntityId ItemId, string DefId, int Count);

/// <summary>How a place became known (SYSTEMS.md S-30). Phase 1 discovers by visiting only; the rest arrive with S-30.</summary>
public enum DiscoveryMethod
{
    Sighted,
    Visited,
    Told,
    Purchased,
    Magical,
}

/// <summary>A discovered-location record (PERSISTENCE.md §5.1): which place, how, and on which world tick.</summary>
public sealed record DiscoveryRecord(string LocationId, DiscoveryMethod Method, long Tick);

public static class DiscoveryMethods
{
    public static string Key(DiscoveryMethod method) => method switch
    {
        DiscoveryMethod.Sighted => "sighted",
        DiscoveryMethod.Visited => "visited",
        DiscoveryMethod.Told => "told",
        DiscoveryMethod.Purchased => "purchased",
        DiscoveryMethod.Magical => "magical",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown discovery method"),
    };

    public static DiscoveryMethod Parse(string key)
    {
        foreach (var method in Enum.GetValues<DiscoveryMethod>())
        {
            if (Key(method) == key)
                return method;
        }
        throw new FormatException($"Unknown discovery method '{key}'");
    }
}

/// <summary>
/// The player character. Fully serialized, never a delta: a character is not regenerable, so every
/// field is authoritative and a round trip must preserve all of them (PERSISTENCE.md §5.1, T-01).
/// Position is in integer millimetres, so no floating point is persisted.
/// </summary>
public sealed record PlayerRecord
{
    public PlayerRecord(EntityId id, string name, long xMm, long yMm, long zMm, ulong appearanceSeed, IEnumerable<InventoryEntry> inventory,
        CharacterProgression? progression = null, int facingMdeg = 0, IEnumerable<DiscoveryRecord>? discoveries = null)
    {
        if (id.Kind != EntityKind.Character)
            throw new ArgumentException($"The player's instance ID must be a character ID, got {id}", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("The player needs a name", nameof(name));
        Id = id;
        Name = name;
        XMm = xMm;
        YMm = yMm;
        ZMm = zMm;
        AppearanceSeed = appearanceSeed;
        Inventory = inventory.OrderBy(e => e.ItemId.Value, StringComparer.Ordinal).ToImmutableArray();
        foreach (var entry in Inventory)
        {
            if (entry.ItemId.Kind != EntityKind.Item)
                throw new ArgumentException($"Inventory holds item instances, got {entry.ItemId}", nameof(inventory));
            if (!DefinitionId.IsValid(entry.DefId) || entry.Count <= 0)
                throw new ArgumentException($"Invalid inventory entry {entry}", nameof(inventory));
        }
        if (Inventory.Select(e => e.ItemId).Distinct().Count() != Inventory.Length)
            throw new ArgumentException("An item instance can be held only once", nameof(inventory));
        Progression = (progression ?? CharacterProgression.Empty).Validate();
        if (facingMdeg is < 0 or >= 360_000)
            throw new ArgumentOutOfRangeException(nameof(facingMdeg), facingMdeg, "Facing is in millidegrees, [0, 360000)");
        FacingMdeg = facingMdeg;
        Discoveries = (discoveries ?? Array.Empty<DiscoveryRecord>()).OrderBy(d => d.LocationId, StringComparer.Ordinal).ToImmutableArray();
        foreach (var discovery in Discoveries)
        {
            if (!DefinitionId.IsValid(discovery.LocationId) || !discovery.LocationId.StartsWith("location.", StringComparison.Ordinal)
                || !Enum.IsDefined(discovery.Method) || discovery.Tick < 0)
                throw new ArgumentException($"Invalid discovery record {discovery}", nameof(discoveries));
        }
        if (Discoveries.Select(d => d.LocationId).Distinct(StringComparer.Ordinal).Count() != Discoveries.Length)
            throw new ArgumentException("A location is discovered only once", nameof(discoveries));
    }

    /// <summary>
    /// The appearance seed a character gets when nothing chose one: derived from its ULID, so it is
    /// deterministic and unique per character. Schema 3 made the field required; the 2 -> 3 migration
    /// gives older saves exactly this value.
    /// </summary>
    public static ulong DerivedAppearanceSeed(EntityId id)
    {
        using var h = new CanonicalHasher();
        return BinaryPrimitives.ReadUInt64BigEndian(h.Add("unnamed.appearance-seed/v1").Add(id.Value).FinishBytes());
    }

    /// <summary>The same player holding a different inventory (the definition-ID pass rewrites stored IDs).</summary>
    public PlayerRecord WithInventory(IEnumerable<InventoryEntry> inventory) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, inventory, Progression, FacingMdeg, Discoveries);

    /// <summary>The same player with different progression (schema 4; the definition-ID pass rewrites its IDs too).</summary>
    public PlayerRecord WithProgression(CharacterProgression progression) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, progression, FacingMdeg, Discoveries);

    /// <summary>The same player with different discovery records (schema 5; the definition-ID pass rewrites their location IDs).</summary>
    public PlayerRecord WithDiscoveries(IEnumerable<DiscoveryRecord> discoveries) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, discoveries);

    public EntityId Id { get; }
    public string Name { get; }
    public long XMm { get; }
    public long YMm { get; }
    public long ZMm { get; }

    /// <summary>The character's appearance identity (PERSISTENCE.md §5.1: "identity ... appearance seed").</summary>
    public ulong AppearanceSeed { get; }

    public ImmutableArray<InventoryEntry> Inventory { get; }

    /// <summary>Level, attributes, skills, known techniques, pools and guards (PROGRESSION.md §3-§4). Schema 4.</summary>
    public CharacterProgression Progression { get; }

    /// <summary>Which way the character faces, in millidegrees from +Z towards +X (PROTOTYPE.md §7.4). Schema 5.</summary>
    public int FacingMdeg { get; }

    /// <summary>Discovered-location records, sorted by location ID (PERSISTENCE.md §5.1). Schema 5.</summary>
    public ImmutableArray<DiscoveryRecord> Discoveries { get; }

    /// <summary>Full-equality digest over every field (T-01: "no field silently defaulted").</summary>
    public string Digest
    {
        get
        {
            using var h = new CanonicalHasher();
            h.Add("unnamed.player/v4").Add(Id.Value).Add(Name).Add(XMm).Add(YMm).Add(ZMm).Add(FacingMdeg).Add(AppearanceSeed).Add(Inventory.Length);
            foreach (var e in Inventory)
                h.Add(e.ItemId.Value).Add(e.DefId).Add(e.Count);
            h.Add(Progression.Digest).Add(Discoveries.Length);
            foreach (var d in Discoveries)
                h.Add(d.LocationId).Add(DiscoveryMethods.Key(d.Method)).Add(d.Tick);
            return h.Finish();
        }
    }
}
