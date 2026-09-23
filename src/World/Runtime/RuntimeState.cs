// UNNAMED World - authoritative runtime state and who may write it (ARCHITECTURE.md §5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// The typed slices of authoritative runtime state. Every slice has exactly one owning system, claimed at
/// composition; a write by any other system is refused (ARCHITECTURE.md §5 "every component key is claimed
/// by exactly one system").
/// </summary>
public enum StateSlice
{
    /// <summary><c>world_tick</c> (S-04, clock half).</summary>
    Clock,

    /// <summary>The player's position and facing (ARCHITECTURE.md §5: "Position (actors) - Movement system").</summary>
    PlayerBody,

    /// <summary>The progression record (level, attributes, skills, techniques, pools, guards).</summary>
    PlayerProgression,

    /// <summary>Discovered-location records (S-30's Phase-1 subset: discovery credit).</summary>
    Discoveries,

    /// <summary>Cell-level <c>world.*</c> flags in the sparse world delta.</summary>
    WorldFlags,

    /// <summary>Per-cell simulation tier (S-21). Transient: derived from position, never saved.</summary>
    CellTiers,
}

/// <summary>A system's proof of which slices it owns. Only composition creates one.</summary>
internal sealed class SliceOwner
{
    internal SliceOwner(string system, ImmutableHashSet<StateSlice> slices)
    {
        System = system;
        Slices = slices;
    }

    public string System { get; }
    public ImmutableHashSet<StateSlice> Slices { get; }
}

/// <summary>
/// The mutable state behind a <see cref="Simulation"/>. It holds records and makes no decisions: every write
/// names its owner, and a writer that does not own the slice is a bug that fails loudly.
/// </summary>
internal sealed class RuntimeState
{
    private readonly Dictionary<StateSlice, string> _owners = new();

    public RuntimeState(WorldDelta world, long worldTick, Body body, CharacterProgression progression, IEnumerable<DiscoveryRecord> discoveries)
    {
        World = world;
        WorldTick = worldTick;
        Body = body;
        Progression = progression;
        Discoveries = discoveries.ToImmutableSortedDictionary(d => d.LocationId, d => d, StringComparer.Ordinal);
    }

    public WorldDelta World { get; }
    public long WorldTick { get; private set; }
    public Body Body { get; private set; }
    public CharacterProgression Progression { get; private set; }
    public ImmutableSortedDictionary<string, DiscoveryRecord> Discoveries { get; private set; }
    public ImmutableSortedDictionary<string, SimulationTier> Tiers { get; private set; } =
        ImmutableSortedDictionary.Create<string, SimulationTier>(StringComparer.Ordinal);

    public IReadOnlyDictionary<StateSlice, string> Owners => _owners;

    public SliceOwner Claim(string system, params StateSlice[] slices)
    {
        foreach (var slice in slices)
        {
            if (_owners.TryGetValue(slice, out string? owner))
                throw new InvalidOperationException($"State slice {slice} is claimed by both {owner} and {system}");
            _owners[slice] = system;
        }
        return new SliceOwner(system, slices.ToImmutableHashSet());
    }

    /// <summary>Composition check: a slice nobody owns is a startup error, not a silent success.</summary>
    public void RequireEverySliceOwned()
    {
        var unowned = Enum.GetValues<StateSlice>().Where(s => !_owners.ContainsKey(s)).ToList();
        if (unowned.Count > 0)
            throw new InvalidOperationException("State slices without an owning system: " + string.Join(", ", unowned));
    }

    public void AdvanceClock(SliceOwner owner)
    {
        Require(owner, StateSlice.Clock);
        WorldTick++;
    }

    public void SetBody(SliceOwner owner, Body body)
    {
        Require(owner, StateSlice.PlayerBody);
        Body = body;
    }

    public void SetProgression(SliceOwner owner, CharacterProgression progression)
    {
        Require(owner, StateSlice.PlayerProgression);
        Progression = progression.Validate();
    }

    public void AddDiscovery(SliceOwner owner, DiscoveryRecord record)
    {
        Require(owner, StateSlice.Discoveries);
        if (Discoveries.ContainsKey(record.LocationId))
            throw new InvalidOperationException($"{record.LocationId} is already discovered");
        Discoveries = Discoveries.Add(record.LocationId, record);
    }

    public void SetFlag(SliceOwner owner, CellKey cell, string flagId, long value)
    {
        Require(owner, StateSlice.WorldFlags);
        World.SetFlag(cell, flagId, value);
    }

    public void SetTier(SliceOwner owner, string cellKey, SimulationTier tier)
    {
        Require(owner, StateSlice.CellTiers);
        Tiers = Tiers.SetItem(cellKey, tier);
    }

    private void Require(SliceOwner owner, StateSlice slice)
    {
        if (!owner.Slices.Contains(slice) || _owners.GetValueOrDefault(slice) != owner.System)
            throw new InvalidOperationException($"{owner.System} wrote {slice}, which {_owners.GetValueOrDefault(slice) ?? "no system"} owns");
    }
}
