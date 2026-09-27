using System.Collections.Immutable;
using UNNAMED.Domain.Factions;

namespace UNNAMED.Domain.Tests.Factions;

/// <summary>The faction rules (M7 design §5.4): the ladder, learning an act once, the identity seam, the clamp, and the act log's eviction.</summary>
public class FactionRulesTests
{
    private const string Wolf = "creature.beast.wolf_grey";
    private const string Keepers = "faction.fixture.keepers";
    private const string Sel = "npc.fixture.sel";

    private static readonly FactionDefinition Approves = new(Keepers, "the keepers", "location.fixture",
        ImmutableArray.Create(new Reaction(ActKinds.CreatureKilled, Wolf, 100)), ImmutableArray<Relation>.Empty);

    private static FactionLedger WithWolves(int count)
    {
        var ledger = FactionLedger.Empty;
        for (int i = 0; i < count; i++)
            ledger = ledger.WithAct(new ActRecord(ledger.NextActSeq, ActKinds.CreatureKilled, Wolf, "r_0_0:c_00_00", 1_000, 2_000, 100 + i));
        return ledger;
    }

    private static (FactionLedger Ledger, Learned? Change) Tell(FactionLedger ledger, FactionDefinition faction, long act,
        string identity = Identities.Identified, long tick = 500) =>
        FactionRules.Learn(ledger, faction, act, KnowledgeSources.Reported, Sel, identity, tick, StandingLadder.Default);

    [Fact]
    public void StandingTierOf_FollowsTheLadder_AtEveryBoundary()
    {
        var ladder = StandingLadder.Default;
        var cases = new (int Points, string Tier)[]
        {
            (-1000, "anathema"), (-999, "outcast"), (-700, "outcast"), (-699, "despised"), (-450, "despised"), (-449, "disliked"),
            (-250, "disliked"), (-249, "wary"), (-100, "wary"), (-99, "neutral"), (0, "neutral"), (99, "neutral"), (100, "accepted"),
            (249, "accepted"), (250, "trusted"), (449, "trusted"), (450, "honoured"), (699, "honoured"), (700, "allied"), (999, "allied"),
            (1000, "exalted"),
        };
        foreach (var (points, tier) in cases)
            Assert.Equal(tier, ladder.StandingTierOf(points).Key);
        Assert.Equal(StandingLadder.Keys, ladder.Tiers.Select(t => (t.Key, t.Level)));
        Assert.Equal(1, StandingLadder.LevelOf("accepted"));
        Assert.Equal(-5, StandingLadder.LevelOf("anathema"));
        Assert.Throws<FormatException>(() => StandingLadder.LevelOf("hostile"));
    }

    [Fact]
    public void Learn_AppliesARowOnce_PerFactionPerAct()
    {
        var (once, first) = Tell(WithWolves(1), Approves, 1);
        Assert.Equal(new Learned(Keepers, 1, KnowledgeSources.Reported, Sel, Identities.Identified, false, 0, 100), first);
        Assert.Equal(100, FactionRules.PointsOf(once, Keepers));

        var (twice, second) = Tell(once, Approves, 1, tick: 900);
        Assert.Null(second);
        Assert.Equal(once, twice);
        Assert.Equal(new FactionKnowledge(Keepers, 1, Identities.Identified, KnowledgeSources.Reported, Sel, 500, 100), Assert.Single(twice.Knowledge));
    }

    [Fact]
    public void Learn_Unidentified_KnowsButMovesNothing()
    {
        var (ledger, change) = Tell(WithWolves(1), Approves, 1, Identities.Unidentified);

        Assert.Equal(new FactionKnowledge(Keepers, 1, Identities.Unidentified, KnowledgeSources.Reported, Sel, 500, 0), Assert.Single(ledger.Knowledge));
        Assert.Empty(ledger.Standing);
        Assert.Equal((0, 0), (change!.From, change.To));

        // Told unidentified again: no change.
        var (again, none) = Tell(ledger, Approves, 1, Identities.Unidentified, 600);
        Assert.Null(none);
        Assert.Equal(ledger, again);
    }

    [Fact]
    public void Learn_AReportUpgradesUnidentifiedExactlyOnce()
    {
        var (unknown, _) = Tell(WithWolves(1), Approves, 1, Identities.Unidentified);
        var (known, upgrade) = Tell(unknown, Approves, 1, Identities.Identified, 700);

        Assert.True(upgrade!.Upgraded);
        Assert.Equal((0, 100), (upgrade.From, upgrade.To));
        Assert.Equal(new FactionKnowledge(Keepers, 1, Identities.Identified, KnowledgeSources.Reported, Sel, 700, 100), Assert.Single(known.Knowledge));

        var (after, none) = Tell(known, Approves, 1, Identities.Identified, 800);
        Assert.Null(none);
        Assert.Equal(100, FactionRules.PointsOf(after, Keepers));
    }

    [Fact]
    public void Learn_WithoutARow_StoresNothing()
    {
        var indifferent = Approves with { Reactions = ImmutableArray.Create(new Reaction(ActKinds.SwitchSet, "world.lever.mill_gate", 100)) };
        var ledger = WithWolves(1);

        var (after, change) = Tell(ledger, indifferent, 1);
        Assert.Null(change);
        Assert.Equal(ledger, after);
        Assert.Empty(after.Knowledge);
    }

    [Fact]
    public void Learn_ClampsAtTheOrdinaryFloor_AndTheCap()
    {
        var harsh = Approves with { Reactions = ImmutableArray.Create(new Reaction(ActKinds.CreatureKilled, Wolf, -250)) };
        var ledger = WithWolves(1).WithPoints(Keepers, -900);
        var (floored, down) = Tell(ledger, harsh, 1);
        Assert.Equal((-900, -999), (down!.From, down.To));
        Assert.Equal(-99, Assert.Single(floored.Knowledge).Delta);   // what actually moved

        var (capped, up) = Tell(WithWolves(1).WithPoints(Keepers, 950), Approves, 1);
        Assert.Equal((950, 1000), (up!.From, up.To));
        Assert.Equal(50, Assert.Single(capped.Knowledge).Delta);
        Assert.Equal("exalted", StandingLadder.Default.StandingTierOf(FactionRules.PointsOf(capped, Keepers)).Key);
    }

    [Fact]
    public void Compact_EvictsTheOldestActFirst()
    {
        var (told, _) = Tell(WithWolves(3), Approves, 1);
        told = told.WithAct(new ActRecord(4, ActKinds.CreatureKilled, Wolf, "r_0_0:c_00_00", 1_000, 2_000, 900));

        var (kept, evicted) = FactionRules.Compact(told, 2);
        Assert.Equal(new long[] { 1, 2 }, evicted);
        Assert.Equal(new long[] { 3, 4 }, kept.Acts.Select(a => a.Seq));
        Assert.Empty(kept.Knowledge);                                 // act 1's row went with it
        Assert.Equal(100, FactionRules.PointsOf(kept, Keepers));      // standing is untouched
        Assert.Equal(5, kept.NextActSeq);                             // the sequence never goes back

        var (same, none) = FactionRules.Compact(kept, 2);
        Assert.Empty(none);
        Assert.Equal(kept, same);
    }
}
