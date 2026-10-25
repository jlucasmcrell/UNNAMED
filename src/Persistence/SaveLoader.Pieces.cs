// UNNAMED Persistence - placed pieces in the definition-ID pass (schema 44; M7 design §4.18, re-ported by WP-1G)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.World;

namespace UNNAMED.Persistence;

internal static partial class SaveLoader
{
    /// <summary>
    /// Placed pieces through the definition-ID pass: a renamed piece takes its new ID; one whose definition was removed is dropped as a
    /// loss, and its chest's stacks are put on the ground where the piece stood, keeping their identities (the container pass has resolved
    /// them), so nothing carried in it is lost with it.
    /// </summary>
    private static (ImmutableArray<PieceRecord> Pieces, ImmutableArray<CreatedEntityRecord> Spilled) ResolvePieces(ImmutableArray<PieceRecord> saved,
        ref ImmutableArray<ContainerRecord> containers, Func<string, string, string?> resolve, MigrationReport report)
    {
        var pieces = ImmutableArray.CreateBuilder<PieceRecord>();
        var spilled = ImmutableArray.CreateBuilder<CreatedEntityRecord>();
        foreach (var piece in saved)
        {
            if (resolve(piece.DefId, $"piece {piece.InstanceId} in cell {piece.HostCell}") is { } id)
            {
                pieces.Add(piece with { DefId = id });
                continue;
            }
            string key = "container." + piece.InstanceId.Value.ToLowerInvariant();
            if (containers.FirstOrDefault(c => c.Key == key) is not { } chest)
                continue;
            containers = containers.Remove(chest);
            int xCm = (int)WorldMath.FloorMod(piece.XMm / 10, WorldMath.CellSizeCm), zCm = (int)WorldMath.FloorMod(piece.ZMm / 10, WorldMath.CellSizeCm);
            spilled.AddRange(chest.Items.Select(item => new CreatedEntityRecord(item.ItemId, item.DefId, piece.HostCell, xCm, zCm, piece.BaselineHash)
            {
                Count = item.Count, Quality = item.Quality, Notable = item.Notable, Wear = item.Wear,
            }));
            if (!chest.Items.IsEmpty)
                report.Warnings.Add($"{chest.Items.Length} stacks from the chest of piece {piece.InstanceId} were put on the ground");
        }
        return (pieces.ToImmutable(), spilled.ToImmutable());
    }
}
