using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>A periodic level derived from the saved world tick, shared by water presentation, collision and navigation.</summary>
public sealed record TidalWater(long LowMm, long HighMm, long PeriodTicks, long PhaseTicks,
    ImmutableArray<TidalPassage> Passages)
{
    public long LevelAt(long tick)
    {
        long phase = ((tick % PeriodTicks + PhaseTicks % PeriodTicks) % PeriodTicks + PeriodTicks) % PeriodTicks;
        // Integer smoothstep on each half-cycle: no frame time, new saved state or floating point in authority.
        long half = PeriodTicks / 2;
        long u = (phase <= half ? phase : PeriodTicks - phase) * 10000 / half;
        long eased = u * u * (30000 - 2 * u) / 100000000;
        return LowMm + (HighMm - LowMm) * eased / 10000;
    }

    public bool Open(TidalPassage passage, long tick) => LevelAt(tick) <= passage.DryAtMm;
}

/// <summary>A flooded crossing; the ordinary raised alternative remains independent of this footprint.</summary>
public sealed record TidalPassage(string Key, Blocker Footprint, long DryAtMm);
