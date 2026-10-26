// UNNAMED Domain - what being of a people does: innate biology, inherited aptitude, racial abilities and the Orenth crossing toll
// (RACES.md "Biology, aptitude and culture are separate"; RACE_ABILITIES_PROPOSAL.md, owner-approved 2026-10-24)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Character;

/// <summary>
/// A people's innate biology and inherited aptitude: always on, never learned, the same for every member - player, companion, hireling,
/// adventurer (proposal §0.4). Percent fields are of the usual (100). A people grants nothing another cannot reach by other roads, and
/// every people's creation grant nets the same (§1).
/// </summary>
public sealed record PeopleTraits
{
    public static PeopleTraits None { get; } = new();

    /// <summary>Of the health the attributes give.</summary>
    public int HealthPercent { get; init; } = 100;

    /// <summary>What a blow of a damage type still does, after armour; a type not listed does all of it.</summary>
    public ImmutableSortedDictionary<string, int> DamageTakenPercent { get; init; } = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);

    public int SpeedPercent { get; init; } = 100;

    /// <summary>Added to the carry limit (negative takes away).</summary>
    public long CarryGrams { get; init; }

    /// <summary>Of the Resonance the attributes give. 0 is no magic at all: no working can be cast (Vaskaal, Ondrek).</summary>
    public int ResonancePercent { get; init; } = 100;

    /// <summary>Of the impact it takes to stagger the body: denser bodies need more.</summary>
    public int StaggerThresholdPercent { get; init; } = 100;

    /// <summary>The share of a struck region's armour the body's own physical blows pass, in points (Kal: structural read).</summary>
    public int ArmorIgnoredPercent { get; init; }

    /// <summary>Added to how high a ledge a jump climbs onto (Veth: supple).</summary>
    public long MantleBonusMm { get; init; }

    /// <summary>Health returns out of combat; false for a body that must be repaired (Constructed).</summary>
    public bool Regenerates { get; init; } = true;

    /// <summary>Of the noise the body makes moving (Ondrek: loud).</summary>
    public int NoisePercent { get; init; } = 100;

    /// <summary>The creation grant, by attribute; it may take a point away where canon names a weakness.</summary>
    public ImmutableSortedDictionary<CharacterAttribute, int> AttributeGrant { get; init; } = ImmutableSortedDictionary<CharacterAttribute, int>.Empty;

    /// <summary>Points of the creation grant the player places on an attribute of their choice (Veth).</summary>
    public int FreeAttributePoints { get; init; }

    /// <summary>Where the free points go when the player does not choose (a quick start).</summary>
    public CharacterAttribute FreeAttributeDefault { get; init; } = CharacterAttribute.Endurance;

    public LearningAptitude Learning { get; init; } = LearningAptitude.Usual;

    public bool CanCast => ResonancePercent > 0;

    /// <summary>The creation grant's net points: the same for every people.</summary>
    public int GrantTotal => AttributeGrant.Values.Sum() + FreeAttributePoints;

    public string? Problem() =>
        HealthPercent <= 0 ? "health_percent is positive"
        : DamageTakenPercent.Any(d => d.Value < 0 || !DamageTypes.All.Contains(d.Key)) ? "damage_taken_percent names damage types, each at least 0"
        : SpeedPercent <= 0 ? "speed_percent is positive"
        : ResonancePercent < 0 ? "resonance_percent is not negative"
        : StaggerThresholdPercent <= 0 ? "stagger_threshold_percent is positive"
        : ArmorIgnoredPercent is < 0 or > 100 ? "armor_ignored_percent is 0 to 100"
        : MantleBonusMm < 0 ? "mantle_bonus_m is not negative"
        : NoisePercent <= 0 ? "noise_percent is positive"
        : FreeAttributePoints < 0 ? "free attribute points are not negative"
        : AttributeGrant.Values.Any(v => v == 0) ? "an attribute grant is not 0"
        : Learning.Problem();
}

/// <summary>What the creator says about a people (RACES.md, shortened in its own words): never a number, those are the traits.</summary>
public sealed record PeopleCard(string Epithet, string Summary, string Body, string LeansToward, string TradeOff);

/// <summary>What a racial ability does. Passive ones are listed on the card and the sheet; their working is in the traits.</summary>
public enum PeopleAction
{
    Passive,
    Glide,
    Flare,
    Leap,
    Blink,
    WraithStep,
    Anchor,
    Recall,
    KindledRing,
    PerfectRecall,
    EtherealStep,
    SpectralSight,
    ModuleSwap,
    Optimise,
    LongSight,
    ThermalRead,
    Stonesense,
    MaterialMemory,
    BedrockStance,
}

/// <summary>
/// A racial ability (<c>ability</c> of <c>class: people</c>): innate biology, known by every member of its people from birth, never taught to
/// another. Costs are Stamina and Focus only - there is no mana - and cooldowns are sim time. <see cref="Toll"/> is what an Orenth crossing
/// adds to the hidden crossing toll (§3.4).
/// </summary>
public sealed record PeopleAbility(string Id, string PeopleId, string Name, string Description, PeopleAction Action)
{
    public int StaminaCost { get; init; }
    public int FocusCost { get; init; }

    /// <summary>Stamina a held ability drains each second (Glide).</summary>
    public int StaminaPerSecond { get; init; }

    public int CooldownTicks { get; init; }
    public int DurationTicks { get; init; }
    public long RangeMm { get; init; }

    /// <summary>A glide's forward speed; a leap's horizontal speed is <see cref="DistancePercent"/> of the gait's.</summary>
    public long SpeedMmPerSecond { get; init; }

    /// <summary>How fast a glide, a flare or a fall comes down.</summary>
    public long SinkMmPerSecond { get; init; }

    public long ApexBonusMm { get; init; }
    public int DistancePercent { get; init; } = 100;
    public int Toll { get; init; }

    /// <summary>For the duration, workings cost this percent of their Strain and take this percent of their cast time (Kindled Ring).</summary>
    public int StrainPercent { get; init; } = 100;
    public int CastPercent { get; init; } = 100;

    /// <summary>The effect the ability puts on its user for <see cref="DurationTicks"/>, if any.</summary>
    public string? EffectId { get; init; }

    /// <summary>Always on, never used from a key: a trait's working, or one presentation keeps (Perfect Recall's journal).</summary>
    public bool Passive => Action is PeopleAction.Passive or PeopleAction.PerfectRecall;

    public string? Problem() =>
        StaminaCost < 0 || FocusCost < 0 || StaminaPerSecond < 0 ? "costs are not negative"
        : CooldownTicks < 0 || DurationTicks < 0 ? "cooldown_s and duration_s are not negative"
        : RangeMm < 0 || SpeedMmPerSecond < 0 || SinkMmPerSecond < 0 || ApexBonusMm < 0 ? "distances and speeds are not negative"
        : DistancePercent <= 0 || StrainPercent < 0 || CastPercent <= 0 ? "percents are positive"
        : Toll < 0 ? "toll is not negative"
        : Action == PeopleAction.Blink && RangeMm <= 0 ? "a blink has a range"
        : Action is PeopleAction.Glide or PeopleAction.Flare && SinkMmPerSecond <= 0 ? "a glide or a flare sinks"
        : Action == PeopleAction.Glide && SpeedMmPerSecond <= 0 ? "a glide goes forward"
        : null;
}

/// <summary>One cost a crossing took (RACES.md §4; MYTHOLOGY.md "memory, years, a sense"). <see cref="Ref"/> is what was lost, by ID.</summary>
public enum TollKind { Scar, Memory, Name, Years }

public sealed record TollCost(int Tier, TollKind Kind, string? Ref);

/// <summary>Where an Orenth's planar anchor stands: a space (its name, empty for the exterior), a point in it, and the world tick it was set.</summary>
public sealed record PlanarAnchor(string Space, long XMm, long YMm, long ZMm, long PlacedTick);

/// <summary>
/// The Orenth crossing record (§3.4): the hidden toll, the costs it has taken at each tier, and the anchor. Kept on the character; never
/// shown as a number. Empty for every other people.
/// </summary>
public sealed record Crossing(long Toll, ImmutableArray<TollCost> Costs, PlanarAnchor? Anchor)
{
    public static Crossing None { get; } = new(0, ImmutableArray<TollCost>.Empty, null);

    public int Tier => TollRules.TierOf(Toll);

    public string? Problem() =>
        Toll < 0 ? "a negative toll"
        : Costs.Select(c => c.Tier).ToList() is var tiers && !tiers.SequenceEqual(Enumerable.Range(1, tiers.Count)) ? "toll costs are not one per tier, in order"
        : Costs.Length > Tier ? "more toll costs than tiers"
        : Costs.Any(c => !Enum.IsDefined(c.Kind) || c.Ref is { } r && !DefinitionId.IsValid(r)) ? "a toll cost names no valid kind or ID"
        : Anchor is { } a && (a.Space is null || a.PlacedTick < 0) ? "an anchor names no space or a negative tick"
        : null;
}

/// <summary>
/// The crossing toll as rules (owner-approved §3.4): every <see cref="PerTier"/> toll is a Debt tier, ten at most. Each tier takes one more
/// thing in the cycle scar, memory, name, years, and a percent of health, which is the only stat it ever touches. It never touches skill,
/// knowledge, level, attributes, reputation or quests.
/// </summary>
public static class TollRules
{
    public const int PerTier = 100;
    public const int MaxTier = 10;

    private static readonly ImmutableArray<TollKind> Cycle = ImmutableArray.Create(TollKind.Scar, TollKind.Memory, TollKind.Name, TollKind.Years);

    public static int TierOf(long toll) => (int)Math.Min(MaxTier, toll / PerTier);

    /// <summary>Of the health the body would otherwise have: one percent a tier, ten at most.</summary>
    public static int HealthPercent(int tier) => 100 - Math.Clamp(tier, 0, MaxTier);

    public static TollKind KindOf(int tier) => Cycle[(tier - 1) % Cycle.Length];

    /// <summary>
    /// The record after <paramref name="toll"/> more: a cost for each tier newly reached. A memory takes one of <paramref name="places"/> and a
    /// name one of <paramref name="names"/> (ordinal order, picked by the tier so a replay takes the same), none already taken; with none
    /// left, the tier still costs, with nothing named.
    /// </summary>
    public static Crossing Pay(Crossing crossing, int toll, IReadOnlyList<string> places, IReadOnlyList<string> names)
    {
        if (toll < 0)
            throw new ArgumentOutOfRangeException(nameof(toll), toll, "A crossing never gives back");
        var next = crossing with { Toll = crossing.Toll + toll };
        var costs = crossing.Costs.ToBuilder();
        for (int tier = crossing.Costs.Length + 1; tier <= next.Tier; tier++)
        {
            var kind = KindOf(tier);
            string? Pick(IReadOnlyList<string> from)
            {
                var left = from.Where(id => !costs.Any(c => c.Ref == id)).Order(StringComparer.Ordinal).ToList();
                return left.Count == 0 ? null : left[(tier * 7919) % left.Count];
            }
            costs.Add(new TollCost(tier, kind, kind switch
            {
                TollKind.Memory => Pick(places),
                TollKind.Name => Pick(names),
                _ => null,
            }));
        }
        return next with { Costs = costs.ToImmutable() };
    }

    /// <summary>How a ledger-keeper words a tier: never the number (MYTHOLOGY.md "ledger of costs").</summary>
    public static string Ledger(int tier) => tier switch
    {
        0 => "You have given nothing yet.",
        <= 2 => "You have given little.",
        <= 5 => "You have given more than most who still walk.",
        <= 8 => "You have given a great deal. Walk carefully.",
        _ => "You are nearly all given. There is little left to take.",
    };
}

/// <summary>What a body's people does to the numbers the shared rules give it.</summary>
public static class PeopleRules
{
    /// <summary>
    /// A racial ability now, for the bar (canon 26 §4.3): what it needs to be set up for (a recall, an anchor to cross back to), its Stamina
    /// and Focus, and its cooldown - in canon's order: the situation, then the pools, then the short wait.
    /// </summary>
    public static Availability Availability(PeopleAbility ability, AvailabilityContext c, bool anchorSet)
    {
        int wait = c.CooldownSeconds(ability.Id);
        var lines = ImmutableArray.CreateBuilder<RequirementLine>();
        if (ability.Action == PeopleAction.Recall)
            lines.Add(new RequirementLine("An anchor set", anchorSet ? "yes" : "none", anchorSet));
        if (ability.StaminaCost > 0)
            lines.Add(new RequirementLine($"{ability.StaminaCost} Stamina", $"{c.Stamina}", c.Stamina >= ability.StaminaCost));
        if (ability.FocusCost > 0)
            lines.Add(new RequirementLine($"{ability.FocusCost} Focus", $"{c.Focus}", c.Focus >= ability.FocusCost));
        lines.Add(new RequirementLine("Recovered from its last use", wait > 0 ? $"{wait} s to go" : "yes", wait <= 0));
        var all = lines.ToImmutable();
        if (ability.Action == PeopleAction.Recall && !anchorSet)
            return new(AvailabilityKind.NeedsSituation, ReasonText.NeedsSituationWord, ReasonText.Situation("an anchor set"), all);
        if (c.Stamina < ability.StaminaCost)
            return new(AvailabilityKind.ShortOf, ReasonText.ShortWord("Stamina"), ReasonText.Short("Stamina", c.Stamina, ability.StaminaCost), all);
        if (c.Focus < ability.FocusCost)
            return new(AvailabilityKind.ShortOf, ReasonText.ShortWord("Focus"), ReasonText.Short("Focus", c.Focus, ability.FocusCost), all);
        if (wait > 0)
            return new(AvailabilityKind.Recovering, ReasonText.RecoveringWord, ReasonText.Recovering(wait), all);
        return new(AvailabilityKind.Ready, ReasonText.Ready, null, all);
    }

    /// <summary>The derived pools and Resonance with the people's biology, and the toll's health cost, applied.</summary>
    public static DerivedStats Derive(DerivedStats stats, PeopleTraits traits, int tollTier = 0) => stats with
    {
        HealthMax = Math.Max(1, stats.HealthMax * traits.HealthPercent * TollRules.HealthPercent(tollTier) / 10_000),
        Resonance = stats.Resonance * traits.ResonancePercent / 100,
    };

    /// <summary>The resistances a body's people gives it, as <see cref="DefenseProfile.Resistances"/> reads them: 1 - taken.</summary>
    public static ImmutableSortedDictionary<string, double> Resistances(PeopleTraits traits) =>
        traits.DamageTakenPercent.ToImmutableSortedDictionary(d => d.Key, d => 1 - d.Value / 100.0, StringComparer.Ordinal);

    /// <summary>
    /// The movement rules for a body of this people: its speed, its mantle reach, and the leap, glide and flare its abilities give it
    /// (none for a people without them).
    /// </summary>
    public static MovementRules Movement(MovementRules rules, PeopleTraits traits, IEnumerable<PeopleAbility> abilities)
    {
        var moving = rules with
        {
            BaseSpeedMmPerSecond = rules.BaseSpeedMmPerSecond * traits.SpeedPercent / 100,
            MantleMm = rules.MantleMm > 0 ? rules.MantleMm + traits.MantleBonusMm : 0,
        };
        foreach (var ability in abilities)
        {
            moving = ability.Action switch
            {
                PeopleAction.Leap => moving with { Wings = moving.Wings with { LeapApexBonusMm = ability.ApexBonusMm, LeapDistancePercent = ability.DistancePercent } },
                PeopleAction.Glide => moving with { Wings = moving.Wings with { GlideSpeedMmPerSecond = ability.SpeedMmPerSecond, GlideSinkMmPerSecond = ability.SinkMmPerSecond } },
                PeopleAction.Flare => moving with { Wings = moving.Wings with { FlareSinkMmPerSecond = ability.SinkMmPerSecond } },
                _ => moving,
            };
        }
        return moving;
    }

    /// <summary>
    /// Where a blink of up to <paramref name="rangeMm"/> along a direction ends: the furthest point the body fits at, reached without passing
    /// a wall, a closed door (<paramref name="barriers"/>) or a structure taller than the jump clears - a blink steps aside, it does not pass
    /// through what stands (§3.4). Creatures and people (<paramref name="bodies"/>) are stepped past, never landed in. Null when there is
    /// nowhere to go.
    /// </summary>
    public static Body? BlinkTarget(Body from, int dirXPermille, int dirZPermille, long rangeMm, MovementRules rules, WalkSpace space,
        IReadOnlyList<Blocker> barriers, IReadOnlyList<Blocker> bodies)
    {
        double length = Math.Sqrt((double)dirXPermille * dirXPermille + (double)dirZPermille * dirZPermille);
        if (length == 0 || rangeMm <= 0)
            return null;
        double ux = dirXPermille / length, uz = dirZPermille / length;
        const long Step = 250;
        Body? best = null;
        for (long d = Step; d <= rangeMm; d += Step)
        {
            long x = from.XMm + (long)Math.Round(ux * d, MidpointRounding.AwayFromZero);
            long z = from.ZMm + (long)Math.Round(uz * d, MidpointRounding.AwayFromZero);
            if (space.Blockers.Any(b => b.HeightMm > rules.JumpApexMm && b.ClearanceMm == 0 && b.Crosses(from.XMm, from.ZMm, x, z))
                || barriers.Any(b => b.Crosses(from.XMm, from.ZMm, x, z)))
                break;
            if (Kinematics.IsClear(x, z, rules.BodyRadiusMm, space, barriers.Concat(bodies).ToList(), rules.StandHeightMm))
                best = new Body(x, space.HeightAtMm(x, z), z, from.FacingMdeg);
        }
        return best;
    }
}
