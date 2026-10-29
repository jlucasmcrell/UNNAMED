using System.Collections.Immutable;

namespace UNNAMED.Domain.Items;

/// <summary>
/// D21's resolution step: the one pure function that turns an assembly - a base template, the parts chosen or rolled for each of its slots, their
/// materials and the maker - into the numbers an assembled piece actually has (canon <c>GEAR_GENERATION.md</c> §3 step 5, and the guard rails in §9).
/// It is deliberately the same function for loot and for crafting: the roll only decides *which* parts, never how they add up.
/// </summary>
public static class GearResolver
{
    /// <summary>Canon §9.2: a maker's signature is one tendency with a bias of at most ±10% on at most three stats.</summary>
    public const double MakerBiasLimit = 0.10;

    /// <summary>Canon §9.1: a part's contribution is a small bounded delta, ±12% on any one stat.</summary>
    public const double PartDeltaLimit = 0.12;

    /// <summary>
    /// The band an assembled piece's stats must land inside, as multipliers of its template's own stats (canon §9.1's per-template envelope). A
    /// resolved piece outside it is clamped, and the fact that it had to be is reported rather than hidden.
    /// </summary>
    public sealed record Envelope(
        double DamageMin, double DamageMax, double Weight, double Reach, double Speed, double Durability, double Value)
    {
        /// <summary>The default envelope: what any legal assembly may do to a template before it stops being that template.</summary>
        public static readonly Envelope Default = new(1.30, 1.30, 1.45, 1.30, 1.20, 1.50, 2.20);
    }

    /// <summary>What an assembled piece resolves to, and whether the envelope had to clamp it.</summary>
    public sealed record ResolvedGear(
        int DamageMin,
        int DamageMax,
        long ReachMm,
        long WeightGrams,
        int AttackMs,
        int DrawMs,
        int Durability,
        int Armor,
        long Value,
        bool Clamped)
    {
        /// <summary>Which stats the envelope clamped, by name, for the content warning canon §9.1 asks to log.</summary>
        public ImmutableArray<string> ClampedStats { get; init; } = ImmutableArray<string>.Empty;
    }

    /// <summary>
    /// Resolve one assembly. Missing parts or materials are the caller's error (the loader refuses unreachable ones), so they contribute nothing here
    /// rather than throwing deep inside a loot roll.
    /// </summary>
    public static ResolvedGear Resolve(
        ItemDefinition item,
        GearBuild build,
        IReadOnlyDictionary<string, PartDefinition> parts,
        IReadOnlyDictionary<string, MaterialDefinition> materials,
        MakerDefinition? maker,
        Envelope? envelope = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(build);
        var band = envelope ?? Envelope.Default;

        double damage = 1.0, weight = 1.0, reach = 1.0, speed = 1.0, durabilityMult = 1.0, value = 1.0;
        long reachAdd = 0, weightAdd = 0;
        int durabilityAdd = 0;

        foreach (var (_, partId) in build.Parts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!parts.TryGetValue(partId, out var part))
                continue;
            var stats = part.Stats;
            damage *= Bounded(stats.DamageMult, PartDeltaLimit, 1.0);
            speed *= Bounded(stats.SpeedMult, PartDeltaLimit, 1.0);
            reach *= 1.0;
            reachAdd += stats.ReachAddMm;
            weightAdd += stats.WeightAddGrams;
            durabilityAdd += stats.DurabilityAdd;
        }

        foreach (var (_, materialId) in build.Materials.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!materials.TryGetValue(materialId, out var material))
                continue;
            damage *= material.Mult.Damage;
            weight *= material.Mult.Weight;
            durabilityMult *= material.Mult.Durability;
            value *= material.Mult.Value;
        }

        if (maker is not null)
        {
            var bias = maker.Signature.StatBias;
            weight *= Bounded(bias.WeightMult, MakerBiasLimit, 1.0);
            durabilityMult *= Bounded(bias.DurabilityMult, MakerBiasLimit, 1.0);
            speed *= Bounded(bias.SpeedMult, MakerBiasLimit, 1.0);
            damage *= Bounded(bias.DamageMult, MakerBiasLimit, 1.0);
        }

        var weapon = item.Weapon;
        int baseMin = weapon?.DamageMin ?? 0;
        int baseMax = weapon?.DamageMax ?? 0;
        long baseReach = weapon?.ReachMm ?? 0;
        int baseAttack = weapon?.AttackMs ?? 0;
        int baseDraw = weapon?.DrawMs ?? 0;
        int baseDurability = item.DurabilityMax;
        long baseWeight = item.WeightGrams;
        long baseValue = item.ValueBase;

        var clamped = ImmutableArray.CreateBuilder<string>();

        int damageMin = Clamp("damage_min", (int)Math.Round(baseMin * damage, MidpointRounding.AwayFromZero), baseMin, band.DamageMin, clamped);
        int damageMax = Clamp("damage_max", (int)Math.Round(baseMax * damage, MidpointRounding.AwayFromZero), baseMax, band.DamageMax, clamped);
        long reachMm = Clamp("reach", (long)Math.Round(baseReach * reach, MidpointRounding.AwayFromZero) + reachAdd, baseReach, band.Reach, clamped);
        long weightGrams = Clamp("weight", (long)Math.Round(baseWeight * weight, MidpointRounding.AwayFromZero) + weightAdd, baseWeight, band.Weight, clamped);
        int durability = Clamp("durability", (int)Math.Round(baseDurability * durabilityMult, MidpointRounding.AwayFromZero) + durabilityAdd,
            baseDurability, band.Durability, clamped);
        long resolvedValue = Clamp("value", (long)Math.Round(baseValue * value, MidpointRounding.AwayFromZero), baseValue, band.Value, clamped);

        // A quicker piece attacks (or draws) sooner: speed acts on the interval, and never past the envelope's own bound.
        double pace = Math.Clamp(speed, 1.0 / band.Speed, band.Speed);
        int attackMs = baseAttack == 0 ? 0 : (int)Math.Round(baseAttack / pace, MidpointRounding.AwayFromZero);
        int drawMs = baseDraw == 0 ? 0 : (int)Math.Round(baseDraw / pace, MidpointRounding.AwayFromZero);

        return new ResolvedGear(damageMin, damageMax, reachMm, weightGrams, attackMs, drawMs, durability, item.ArmorValue, resolvedValue,
            clamped.Count > 0)
        {
            ClampedStats = clamped.ToImmutable(),
        };
    }

    /// <summary>Keep a part's or maker's multiplier inside canon's bound of ±limit around 1.0.</summary>
    private static double Bounded(double multiplier, double limit, double fallback) =>
        double.IsFinite(multiplier) && multiplier > 0 ? Math.Clamp(multiplier, 1.0 - limit, 1.0 + limit) : fallback;

    /// <summary>Clamp a resolved stat to the template's envelope, and remember that it happened (canon §9.1).</summary>
    private static int Clamp(string stat, int value, int baseValue, double ceiling, ImmutableArray<string>.Builder clamped)
    {
        if (baseValue <= 0)
            return Math.Max(0, value);
        int high = (int)Math.Round(baseValue * ceiling, MidpointRounding.AwayFromZero);
        if (value <= high)
            return Math.Max(0, value);
        clamped.Add(stat);
        return high;
    }

    private static long Clamp(string stat, long value, long baseValue, double ceiling, ImmutableArray<string>.Builder clamped)
    {
        if (baseValue <= 0)
            return Math.Max(0, value);
        long high = (long)Math.Round(baseValue * ceiling, MidpointRounding.AwayFromZero);
        if (value <= high)
            return Math.Max(0, value);
        clamped.Add(stat);
        return high;
    }
}
