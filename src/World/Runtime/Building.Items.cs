// UNNAMED World - what a placed chest asks of the items system (M7 design §4.13-§4.14; re-ported onto humgen-main by WP-1G)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;

namespace UNNAMED.World.Runtime;

/// <summary>
/// To <see cref="InventorySystem"/>: a chest was placed; its record starts, empty, under the identity its piece derives - so it never reads
/// as a loot table, and the same commands make the same chest.
/// </summary>
internal sealed record OpenPieceChest(string Key) : InternalCommand;

/// <summary>To <see cref="InventorySystem"/>: a chest was destroyed; what it held falls where it stood, each stack keeping its identity.</summary>
internal sealed record SpillContainer(string Key) : InternalCommand;

internal sealed partial class InventorySystem
{
    public string? Handle(OpenPieceChest command)
    {
        if (State.World.Container(command.Key) is not null)
            return null;
        if (_context.FindContainer(command.Key) is not { } site)
            return $"there is no container '{command.Key}'";
        if (!EntityId.TryParse("pce_" + command.Key["container.pce_".Length..].ToUpperInvariant(), out var piece))
            return $"there is no container '{command.Key}'";
        var chest = State.World.Registry.CreateEntity(DefinitionId.Parse(command.Key), SystemContext.PieceChestId(piece)).InstanceId;
        var cell = _context.CellOfSite(_context.ContainerSpace(site), site.XMm, site.ZMm);
        State.SetContainer(_owner, new ContainerRecord(site.Key, chest, cell.ToString(), ImmutableArray<ContainerItem>.Empty));
        return null;
    }

    public string? Handle(SpillContainer command)
    {
        if (State.World.Container(command.Key) is not { } record || _context.FindContainer(command.Key) is not { } site)
            return $"there is no container '{command.Key}'";
        var cell = _context.CellOfSite(_context.ContainerSpace(site), site.XMm, site.ZMm);
        var (minX, minZ) = CellOrigin(cell);
        foreach (var item in record.Items)
            State.PlaceItem(_owner, cell, item.ItemId, item.DefId, item.Count, (int)((site.XMm - minX) / 10), (int)((site.ZMm - minZ) / 10),
                item.Quality, item.Notable, item.Wear);
        State.ReleaseContainer(_owner, command.Key);
        return null;
    }
}
