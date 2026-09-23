// UNNAMED Persistence - registered baseline transitions (M2b §7)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.World;

namespace UNNAMED.Persistence;

/// <summary>
/// A registered, tested move of saved deltas from one generator contract to another: the only way a
/// changed cell's delta reaches a changed baseline. It is keyed by the exact fingerprints, so it can
/// apply only to the transition it was written for. A cell whose baseline changed with no matching
/// transition refuses to load.
/// </summary>
/// <param name="Name">What changed, for the migration report.</param>
/// <param name="FromFingerprint">The <c>worldgen_fingerprint</c> the save was written with.</param>
/// <param name="ToFingerprint">The running generator's fingerprint.</param>
/// <param name="DropVanishedTargets">
/// Whether a record whose target no longer exists (a node, population or slot the new baseline does
/// not generate) may be dropped as declared loss. When false, such a record blocks the load.
/// </param>
public sealed record BaselineTransition(string Name, string FromFingerprint, string ToFingerprint, bool DropVanishedTargets = false);

/// <summary>
/// The conservative semantic rebase (M2b §7). A record is carried onto the new baseline only when
/// its target keeps a stable semantic identity there: the same node key, the same population with
/// the stored count inside its budget, the same slot of the same family. Values are carried, never
/// recomputed. Everything else is a vanished target: loss if the transition declares it, a blocker
/// otherwise.
/// </summary>
internal static class SemanticRebase
{
    public static (ImmutableArray<CellDeltaRecord> Cells, ImmutableArray<EntityDeltaRecord> Entities) Apply(
        BaselineTransition transition,
        IReadOnlySet<string> cellsToRebase,
        ImmutableArray<CellDeltaRecord> cells,
        ImmutableArray<EntityDeltaRecord> entities,
        Func<CellKey, CellBaseline> baseline,
        MigrationReport report)
    {
        void Vanished(string what)
        {
            if (transition.DropVanishedTargets)
                report.Loss.Add($"{what} (transition '{transition.Name}')");
            else
                report.Blockers.Add($"{what}, and transition '{transition.Name}' does not allow dropping it");
        }

        var rebasedCells = ImmutableArray.CreateBuilder<CellDeltaRecord>();
        foreach (var record in cells)
        {
            if (!cellsToRebase.Contains(record.CellKey))
            {
                rebasedCells.Add(record);
                continue;
            }
            var target = baseline(CellKey.Parse(record.CellKey));
            var nodes = record.HarvestedNodes.Where(n =>
            {
                bool kept = target.FindNode(n.NodeKey) is not null;
                if (!kept)
                    Vanished($"{record.CellKey}: harvested node '{n.NodeKey}' is not in the new baseline");
                return kept;
            }).ToImmutableArray();
            var populations = record.PopulationAlive.Where(p =>
            {
                var population = target.FindPopulation(p.Key);
                bool kept = population is not null && p.Value >= 0 && p.Value <= population.Max;
                if (!kept)
                    Vanished($"{record.CellKey}: population '{p.Key}' alive={p.Value} does not fit the new baseline");
                return kept;
            }).ToImmutableArray();
            rebasedCells.Add(record with { BaselineHash = target.Digest, HarvestedNodes = nodes, PopulationAlive = populations });
        }

        var rebasedEntities = ImmutableArray.CreateBuilder<EntityDeltaRecord>();
        foreach (var record in entities)
        {
            string host = Sections.SectionCodec.HostCell(record.SlotKey);
            if (!cellsToRebase.Contains(host))
            {
                rebasedEntities.Add(record);
                continue;
            }
            var target = baseline(CellKey.Parse(host));
            var slot = target.FindSlot(record.SlotKey);
            if (slot is null || slot.FamilyDefId != record.DefId)
            {
                Vanished($"entity {record.InstanceId}: slot '{record.SlotKey}' ({record.DefId}) is not in the new baseline");
                continue;
            }
            rebasedEntities.Add(record with { BaselineHash = target.Digest });
        }

        foreach (string cell in cellsToRebase.OrderBy(c => c, StringComparer.Ordinal))
            report.CellsRebased.Add($"{cell} ({transition.Name})");
        return (rebasedCells.ToImmutable(), rebasedEntities.ToImmutable());
    }
}
