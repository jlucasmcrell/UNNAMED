// UNNAMED Domain - simulation tiers (D-06, WORLD_ARCHITECTURE.md §5.1, §6, §7.4)
// No Godot references - pure C#

namespace UNNAMED.Domain.Spatial;

/// <summary>A: full simulation. B: simplified regional. C: abstract schedule and economy. D: stored state only.</summary>
public enum SimulationTier
{
    A,
    B,
    C,
    D,
}

/// <summary>
/// Tier radii from <c>config.simulation_tiers</c> (WORLD_ARCHITECTURE.md §6.2: "single configuration values in one
/// place"). A cell's distance is from the player to the nearest point of the cell's square. Hysteresis keeps a cell
/// sitting on a boundary from thrashing (§7.4).
/// </summary>
public sealed record TierRules(long FullRadiusMm, long RegionalRadiusMm, long AbstractRadiusMm, long HysteresisMm)
{
    /// <summary>
    /// The tier a cell should hold, given the tier it holds now. Transitions are stepwise (§5.1): one step per
    /// evaluation, A -> B -> C -> D and back, never A -> D. A step towards A needs the cell to be inside the
    /// inner radius by the hysteresis margin; a step away needs it outside by the same margin.
    /// </summary>
    public SimulationTier Next(SimulationTier current, long distanceMm)
    {
        var target = Target(current, distanceMm);
        return target < current ? current - 1 : target > current ? current + 1 : current;
    }

    private SimulationTier Target(SimulationTier current, long distanceMm)
    {
        SimulationTier target = SimulationTier.D;
        foreach (var (tier, radius) in new[] { (SimulationTier.A, FullRadiusMm), (SimulationTier.B, RegionalRadiusMm), (SimulationTier.C, AbstractRadiusMm) })
        {
            long edge = current <= tier ? radius + HysteresisMm : radius - HysteresisMm;
            if (distanceMm <= edge)
            {
                target = tier;
                break;
            }
        }
        return target;
    }
}
