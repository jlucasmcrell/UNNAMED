// UNNAMED Domain - who did what to a boss, part by part, and what that earns (CMB-45; EPIC 534-535 #9, #10; E4/E5)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Combat;

/// <summary>
/// The parts a blow can land on. Today these are the body's regions, because CMB-39 (multi-part entities) has not landed; when it does, a
/// part id names a part and this becomes the fallback for a body that is one piece.
/// </summary>
public static class ContributionParts
{
    public const string Body = "body";
    public const string Head = "head";
    public const string Torso = "torso";
    public const string Limbs = "limbs";

    public static string Of(BodyRegion region) => region switch
    {
        BodyRegion.Head => Head,
        BodyRegion.Torso => Torso,
        _ => Limbs,
    };
}

/// <summary>One attacker's tally on one part of one encounter: what they contributed, and when they last did.</summary>
public sealed record PartContribution(string ActorId, string Part, int Weight, long LastTick)
{
    public PartContribution Add(int weight, long tick) => this with { Weight = Weight + weight, LastTick = tick };
}

/// <summary>One attacker's share of an encounter, in per mille, so the sum of every share is exactly 1000.</summary>
public sealed record ContributionShare(string ActorId, int Weight, int Permille)
{
    /// <summary>
    /// Whether this attacker qualified for the encounter's rewards (E4): a contribution under five per cent is an assist, not a claim, and
    /// the threshold is a rule rather than a number each encounter picks.
    /// </summary>
    public bool Qualified => Permille >= ContributionRules.QualifyingPermille;
}

/// <summary>
/// The ledger of one encounter (CMB-45): who has hit what, part by part, and - when it ends - how the credit divides. It is public
/// accounting, not a private score: the shares are what the world records and what an adventurer remembers.
/// </summary>
public sealed class ContributionLedger
{
    private readonly Dictionary<(string Actor, string Part), PartContribution> _entries = new();

    /// <summary>Which encounter this is: the creature's own key, so two Custodians are two ledgers only if they are two creatures.</summary>
    public ContributionLedger(string encounterKey) => EncounterKey = encounterKey;

    public string EncounterKey { get; }

    /// <summary>Every contribution so far, in a fixed order (part, then actor), so a save and a test read the same list.</summary>
    public ImmutableArray<PartContribution> Entries => _entries.Values
        .OrderBy(e => e.Part, StringComparer.Ordinal).ThenBy(e => e.ActorId, StringComparer.Ordinal)
        .ToImmutableArray();

    public int Total => _entries.Values.Sum(e => e.Weight);

    /// <summary>A blow landed: its weight is the damage, doubled on a weak point, and nothing at all for a blow that did no harm.</summary>
    public void Landed(string actorId, BodyRegion region, int damage, bool weakPoint, long tick)
    {
        if (damage <= 0)
            return;
        string part = ContributionParts.Of(region);
        int weight = ContributionRules.Weigh(damage, weakPoint);
        var key = (actorId, part);
        _entries[key] = _entries.TryGetValue(key, out var had) ? had.Add(weight, tick) : new PartContribution(actorId, part, weight, tick);
    }

    /// <summary>The shares, in per mille, in a fixed order: the largest first, ties broken by actor id so nothing is decided by luck.</summary>
    public ImmutableArray<ContributionShare> Shares() => ContributionRules.Split(
        _entries.Values.GroupBy(e => e.ActorId, StringComparer.Ordinal).Select(g => (g.Key, g.Sum(e => e.Weight))));

    /// <summary>The one attacker the encounter weights most heavily, or null before anyone has landed a blow. Ties go to the lower id.</summary>
    public string? Leading => Shares().FirstOrDefault()?.ActorId;
}

public static class ContributionRules
{
    /// <summary>A blow on a weak point counts twice: it is the one a boss fight is actually about (CMB-38's windows).</summary>
    public const int WeakPointPermille = 2_000;

    /// <summary>Five per cent: below this a contribution is an assist and does not qualify for the encounter's rewards (E4).</summary>
    public const int QualifyingPermille = 50;

    /// <summary>The weight of one landed blow.</summary>
    public static int Weigh(int damage, bool weakPoint) => Math.Max(0, damage) * (weakPoint ? WeakPointPermille : 1_000) / 1_000;

    /// <summary>
    /// Shares in per mille that always sum to exactly 1000, with the rounding remainder given to the largest contributor - the ordinary way
    /// to divide a purse. Order: largest weight first, ties by actor id, so the same ledger always divides the same way.
    /// </summary>
    public static ImmutableArray<ContributionShare> Split(IEnumerable<(string Actor, int Weight)> weights)
    {
        var list = weights.Where(w => w.Weight > 0)
            .OrderByDescending(w => w.Weight).ThenBy(w => w.Actor, StringComparer.Ordinal)
            .ToImmutableArray();
        int total = list.Sum(w => w.Weight);
        if (total == 0)
            return ImmutableArray<ContributionShare>.Empty;

        var shares = list.Select(w => (w.Actor, w.Weight, Permille: w.Weight * 1000 / total)).ToArray();
        int remainder = 1000 - shares.Sum(s => s.Permille);
        if (remainder != 0)
            shares[0].Permille += remainder;
        return shares.Select(s => new ContributionShare(s.Actor, s.Weight, s.Permille)).ToImmutableArray();
    }
}
