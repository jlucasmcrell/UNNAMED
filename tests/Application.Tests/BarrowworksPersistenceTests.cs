// UNNAMED Application tests - the space survives a save (M8 D10, the interior-space save extension)
// A save taken inside the ring loads back inside it. Before the interior-space save extension the position was the only thing a save
// held about where a body was, and the ring's local metres sit inside the exterior's grid - so the Vestibule's
// (50, 84) loaded a body into the Ashen Hollow at the same metres, hundreds of metres from either entrance.

using UNNAMED.Application;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

public class BarrowworksPersistenceTests
{
    [Fact]
    public void LoadingAnActiveAttempt_RestagesOutsideCourt_WithoutRefillingThePlayer()
    {
        using var profile = new TempProfile();
        var options = new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        { RegionId = "region.barrowworks", Barrowworks = true, StartXMm = 47_000, StartZMm = 106_000 };
        using var session = GameSession.Boot(options);
        session.NewGame(Harness.Named("Wanderer"), seed: 42);
        for (int i = 0; i < 60; i++) session.Frame(session.TickSeconds);
        Assert.Contains(session.Simulation!.Creatures, c => c.DefId == "creature.construct.ring_custodian" && c.Hostile);
        var pools = session.Simulation.Player.Progression.Pools;
        var inventory = session.Simulation.Player.Inventory.ToArray();
        session.Save(SaveSlots.Quick);
        session.Load(SaveSlots.Quick);
        Assert.Equal(50_000, session.Simulation.Player.Body.XMm);
        Assert.Equal(84_000, session.Simulation.Player.Body.ZMm);
        Assert.Equal(pools, session.Simulation.Player.Progression.Pools);
        Assert.Equal(inventory, session.Simulation.Player.Inventory);
        var boss = Assert.Single(session.Simulation.Creatures.Where(c => c.DefId == "creature.construct.ring_custodian"));
        Assert.False(boss.Hostile);
        Assert.Equal(boss.MaxHealth, boss.Health);
        session.Save(SaveSlots.Quick);
        session.Load(SaveSlots.Quick);
        Assert.Equal(84_000, session.Simulation.Player.Body.ZMm);
    }
    private const string Threshold = "portal.barrowworks.threshold";

    /// <summary>
    /// The threshold portal's exterior anchor: beside Blackvein's spoil heaps. These are the portal's own region
    /// millimetres, which is the frame GameOptions.StartXMm takes and the frame the reach check measures in.
    /// </summary>
    private const long ThresholdXMm = 440_000L;
    private const long ThresholdZMm = 621_000L;

    /// <summary>
    /// A run with the gate on, opened AT the threshold. The start position is the mechanism P2 added for the
    /// capture harness, and it is the only way to begin a real session beside a portal rather than walking to it.
    /// </summary>
    private static GameOptions Options(TempProfile profile, bool atThreshold) =>
        new(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            LockProfile = true,
            RegionId = "region.starting_frontier",
            Barrowworks = true,
            StartXMm = atThreshold ? ThresholdXMm : null,
            StartZMm = atThreshold ? ThresholdZMm : null,
        };

    /// <summary>
    /// Take a save inside the ring, on this profile, and let the session go.
    /// </summary>
    /// <remarks>
    /// <c>Submit</c> only queues a command - it applies at the next tick boundary - so the frame is what actually
    /// carries the body through the portal. Without it the command is never drained and the body stays outside.
    /// </remarks>
    private static void SaveInsideTheRing(TempProfile profile)
    {
        using var session = GameSession.Boot(Options(profile, atThreshold: true));
        session.NewGame(Harness.Named("Wanderer"), seed: 42);
        Assert.True(session.Simulation!.PlayerInExterior);
        session.Submit(new EnterSpaceCommand(session.Simulation.PlayerId, Threshold));
        session.Frame(session.TickSeconds);
        Assert.False(session.Simulation.PlayerInExterior, "the body should be inside the ring after using the portal");
        session.Save(SaveSlots.Quick);
    }

    [Fact]
    public void ASaveTakenInsideTheRing_LoadsBackInsideIt()
    {
        using var profile = new TempProfile();
        SaveInsideTheRing(profile);

        // A second session on the SAME profile - a save lives in the profile's root, so a fresh one has nothing to
        // load. Without the interior-space save extension this loads the body into the Ashen Hollow at the ring's own metres.
        using var loaded = GameSession.Boot(Options(profile, atThreshold: false));
        var result = loaded.Load(SaveSlots.Quick);

        Assert.True(result.IsComplete, "the load should complete without losing a section");
        Assert.False(loaded.Simulation!.PlayerInExterior);
    }

    [Fact]
    public void TheSpaceIsInTheRecordNotOnlyInTheCoordinates()
    {
        using var profile = new TempProfile();
        SaveInsideTheRing(profile);

        // LoadResult.Player is the decoded record, so this reads the field that was written and read back rather
        // than the live simulation's own state.
        using var loaded = GameSession.Boot(Options(profile, atThreshold: false));
        var result = loaded.Load(SaveSlots.Quick);

        Assert.Equal("space.barrowworks", result.Player.SpaceId);
        Assert.Equal(Threshold, result.Player.SpaceTraversal);
    }

    [Fact]
    public void ARunWithTheGateOff_NeverCarriesASpaceAtAll()
    {
        using var profile = new TempProfile();
        using (var session = Harness.Boot(profile))
        {
            session.NewGame(Harness.Named("Wanderer"), seed: 42);

            // No portals exist, so the ring is unreachable and the space is never anything but the exterior - which
            // is what keeps the option-off playtest the playtest it was.
            Assert.True(session.Simulation!.PlayerInExterior);
            Assert.DoesNotContain(session.Setup.Layout.Portals.Portals, p => p.Space == UNNAMED.Domain.Spatial.SpaceId.Parse("space.barrowworks"));
            session.Save(SaveSlots.Quick);
        }

        using var loaded = Harness.Boot(profile);
        // The empty name is the exterior's, by design: an older save carries exactly this.
        Assert.Equal("", loaded.Load(SaveSlots.Quick).Player.SpaceId);
    }
}
