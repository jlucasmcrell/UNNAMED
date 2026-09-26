using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M7's persisted player state through the runtime's copy sites (M7 design §7.6): a companion's route through both companion copies,
/// and the faction ledger through the runtime's seed and <c>CaptureRecord</c>.
/// </summary>
public class M7PersistedStateTests
{
    private const string Tavar = "npc.ashen_hollow.tavar_orr";

    private static readonly NavRoute Route = NavRoute.Active(new NavPoint(52_000, 150_000),
        ImmutableArray.Create(new NavPoint(60_000, 151_000), new NavPoint(52_000, 150_000)), 3, 0x1122334455667788,
        new NavRect(30_000, 130_000, 90_000, 170_000), false);

    private static readonly FactionLedger Ledger = new(3,
        ImmutableArray.Create(
            new ActRecord(1, ActKinds.CreatureKilled, "creature.construct.animated_armour", "r_0_0:c_00_00", 63_300, 33_400, 900),
            new ActRecord(2, ActKinds.SwitchSet, "world.foldscar.steadied", "r_0_0:c_01_00", 153_000, 48_000, 1_200)),
        ImmutableArray.Create(
            new FactionKnowledge("faction.ashen_hollow.survey", 1, Identities.Identified, KnowledgeSources.Reported, "npc.ashen_hollow.sel_arien", 1_000, -100),
            new FactionKnowledge("faction.ashen_hollow.waystation", 1, Identities.Identified, KnowledgeSources.Reported, "npc.ashen_hollow.kera_voss", 950, 100)),
        ImmutableArray.Create(new FactionStanding("faction.ashen_hollow.survey", -100), new FactionStanding("faction.ashen_hollow.waystation", 100)));

    [Fact]
    public void ACompanionRoute_SurvivesPopulateAndCapture()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var tavar = new CompanionRecord(Tavar, CompanionOrder.Follow, CompanionCondition.Up, 60_000, 151_000, 90_000, 100) { Route = Route };
        var arena = Arena.OpenCreatures(session, session.Setup, (52, 150), 90, Array.Empty<(string, double, double, string)>(),
            r => r.WithCompanions(new[] { tavar }));

        var captured = Assert.Single(arena.Simulation.CaptureRecord().Companions);
        Assert.Equal(Route, captured.Route);
    }

    [Fact]
    public void TheFactionLedger_SurvivesStartAndCapture()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (52, 150), 90, Array.Empty<(string, double, double, string)>(),
            r => r with { Factions = Ledger });

        Assert.Equal(Ledger, arena.Simulation.CaptureRecord().Factions);
    }
}
