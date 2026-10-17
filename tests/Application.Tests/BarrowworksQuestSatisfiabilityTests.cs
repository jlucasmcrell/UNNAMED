// UNNAMED Application tests - every step of the Barrowworks is satisfiable by something that exists
// This is the check whose absence let three steps of the quest be unreachable without any suite noticing: the
// quest read world flags that no switch and no door could ever set, so "find the Blind Store" and "make the
// choice" could not be completed by any player, and nothing failed.

using System.Collections.Immutable;
using UNNAMED.Application;
using UNNAMED.Domain.Quests;
using UNNAMED.World;
using Xunit;

namespace UNNAMED.Application.Tests;

public class BarrowworksQuestSatisfiabilityTests
{
    private const string Quest = "quest.barrowworks.unfinished_measure";
    private const string Region = "region.barrowworks";

    /// <summary>
    /// Booted WITH the Barrowworks as the region. Its own layout is what these tests read - the starting frontier
    /// is where a player walks in from, and its switches are the Foldscar's.
    /// </summary>
    private static GameSession Boot(TempProfile profile) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            LockProfile = true,
            RegionId = Region,
            Barrowworks = true,
        });

    /// <summary>
    /// Every flag a <c>world_state</c> objective waits on must be set by something the region declares: a switch,
    /// or a door - which records its own flag when it swings. A flag nothing writes is a step nobody can pass.
    /// </summary>
    /// <remarks>
    /// No exemption. There was one, for <c>ring_custodian_defeated</c>, while nothing in the engine wrote a flag on
    /// a creature's death; WP4's <c>defeat_boss</c> writes it now, and <c>o_custodian</c> is such an objective
    /// rather than a <c>world_state</c> one - so this test no longer looks at that flag at all, and the exemption
    /// would have been dead code that hid a missing writer if the id ever drifted.
    /// </remarks>
    [Fact]
    public void EveryFlagTheQuestWaitsOn_IsSetBySomethingInTheRegion()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var layout = session.Setup.Layout;

        var written = layout.Switches.Select(s => s.FlagId)
            .Concat(layout.Doors.Select(d => d.FlagId))
            .ToImmutableHashSet(StringComparer.Ordinal);

        var waited = session.Setup.Quests.Quests[Quest].Order
            .Select(o => o.Condition).OfType<WorldStateObjective>()
            .Select(c => c.FlagId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.NotEmpty(waited);
        var orphaned = waited.Where(f => !written.Contains(f)).ToImmutableArray();
        Assert.True(orphaned.IsEmpty, $"the quest waits on flags nothing can set: {string.Join(", ", orphaned)}");
    }

    /// <summary>
    /// Every place an objective names must be a location the region has. An <c>explore_location</c> or
    /// <c>world_state</c> objective is answered by being in the place it names, so a missing place is a step that
    /// can never be reached however writable its flag is.
    /// </summary>
    [Fact]
    public void EveryPlaceTheQuestNames_IsALocationTheRegionHas()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var places = session.Setup.Layout.Locations.Select(l => l.Id).ToImmutableHashSet(StringComparer.Ordinal);

        var named = session.Setup.Quests.Quests[Quest].Order
            .Select(o => o.Condition)
            .SelectMany(c => c switch
            {
                WorldStateObjective w => new[] { w.LocationId },
                ExploreLocation e => new[] { e.LocationId },
                VisitLocation v => new[] { v.LocationId },
                _ => Array.Empty<string>(),
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.NotEmpty(named);
        var missing = named.Where(p => !places.Contains(p)).ToImmutableArray();
        Assert.True(missing.IsEmpty, $"the quest names places the region does not have: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The whole chain, from the design: every objective is reachable from the entry, and the chain arrives at the
    /// report. A break leaves later steps satisfiable in principle and unreachable in fact - which is the failure
    /// this file exists for.
    /// </summary>
    [Fact]
    public void TheObjectives_FormOneChainFromTheEntryToTheReport()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var quest = session.Setup.Quests.Quests[Quest];

        var reached = new HashSet<string>(StringComparer.Ordinal) { quest.Entry };
        var frontier = new Queue<string>([quest.Entry]);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (!quest.Objectives.TryGetValue(current, out var objective))
                continue;
            foreach (string next in objective.Next)
                if (reached.Add(next))
                    frontier.Enqueue(next);
        }

        var unreached = quest.Order.Select(o => o.Id).Where(id => !reached.Contains(id)).ToImmutableArray();
        Assert.True(unreached.IsEmpty, $"these objectives are unreachable from {quest.Entry}: {string.Join(", ", unreached)}");
        Assert.Contains("o_report", reached);
    }

    /// <summary>
    /// The ending: both answers to the headworks are reachable from <c>o_measure</c>, and both of their flags are
    /// writable - which is the thing that was actually missing.
    /// </summary>
    [Fact]
    public void BothAnswersToTheHeadworks_AreReachable_AndBothAreWritable()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var quest = session.Setup.Quests.Quests[Quest];

        Assert.Contains("o_preserve", quest.Objectives["o_measure"].Next);
        Assert.Contains("o_dismantle", quest.Objectives["o_measure"].Next);

        var written = session.Setup.Layout.Switches.Select(s => s.FlagId).ToImmutableHashSet(StringComparer.Ordinal);
        Assert.Contains("world.barrowworks.headworks_preserved", written);
        Assert.Contains("world.barrowworks.headworks_dismantled", written);
    }
}
