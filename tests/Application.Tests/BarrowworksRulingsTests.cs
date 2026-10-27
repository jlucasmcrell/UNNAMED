// UNNAMED Application tests - the owner's Barrowworks rulings of 2026-10-23 (handoff OQ-16 and OQ-17)
// OQ-16: the headworks door takes BOTH the durable Custodian defeat and a fitting wedge, and the wedge stays fitted in
// the door rather than being spent at the saddles. OQ-17: leaving or dying resets the Custodian to full health, and
// only an actual defeat is durable.

using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

public class BarrowworksRulingsTests
{
    private const string Wedge = "item.tool.fitting_wedge";
    private const string Custodian = "creature.construct.ring_custodian";
    private const string Defeated = "world.barrowworks.ring_custodian_defeated";
    private const string Court = "location.barrowworks.r11_measuring_court";

    private static int Wedges(Simulation simulation) => simulation.Player.Inventory.Where(i => i.DefId == Wedge).Sum(i => i.Count);

    private static CellKey CourtCell(GameSession session)
    {
        var place = session.Setup.Layout.Locations.Single(l => l.Id == Court);
        return CellKey.OfInterior(session.Setup.Layout.SpaceId, place.XMm / 1000.0, place.ZMm / 1000.0);
    }

    /// <summary>A character on the court side of G3, with <paramref name="wedges"/> wedges and the defeat recorded or not.</summary>
    private static (Simulation Simulation, EventBus Bus) AtTheHeadworksDoor(GameSession session, int wedges, bool defeated)
    {
        var arena = Arena.OpenCreatures(session, session.Setup, (50, 117), 0, [], origin: (0, 0));
        var snapshot = arena.Simulation.World.TakeSnapshot();
        if (defeated)
        {
            var cell = CourtCell(session);
            var record = new CellDeltaRecord(cell.ToString(), arena.Simulation.World.Baseline(cell).Digest, ImmutableArray<string>.Empty,
                ImmutableArray.Create(new KeyValuePair<string, long>(Defeated, 1)), ImmutableArray<NodeHarvest>.Empty,
                ImmutableArray<KeyValuePair<string, int>>.Empty);
            snapshot = snapshot with { Cells = snapshot.Cells.Add(record).OrderBy(c => c.CellKey, StringComparer.Ordinal).ToImmutableArray() };
        }
        var world = WorldDelta.FromSnapshot(session.Generator, arena.Simulation.World.WorldSeed, new Registry(), snapshot, out var rejected);
        Assert.Empty(rejected);
        var player = arena.Simulation.CaptureRecord();
        if (wedges > 0)
            player = player.WithInventory(player.Inventory.Append(Arena.Stack(Wedge, wedges)));
        var bus = new EventBus();
        return (Simulation.Start(session.Setup, player.WithSpace(session.Setup.Layout.SpaceId.ToString(), null), world,
            arena.Simulation.WorldTick, bus), bus);
    }

    private static bool G3Open(Simulation simulation) => simulation.Doors.Single(d => d.Site.Key == "door.barrowworks.headworks").Open;

    private static string? Use((Simulation Simulation, EventBus Bus) run, string key)
    {
        string? refusal = null;
        void Rejected(CommandRejected r) => refusal = r.Reason;
        run.Bus.Subscribe<CommandRejected>(Rejected);
        run.Simulation.Enqueue(new InteractCommand(run.Simulation.PlayerId, key));
        run.Simulation.DrainCommands();
        run.Bus.Unsubscribe<CommandRejected>(Rejected);
        return refusal;
    }

    [Fact]
    public void TheHeadworksDoor_TakesTheDefeatAndAWedge_AndKeepsTheWedgeFitted()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.barrowworks", barrowworks: true);
        var door = session.Setup.Layout.Doors.Single(d => d.Key == "door.barrowworks.headworks");
        Assert.Equal(new SiteItem(Wedge, 1), door.Fitted);
        Assert.True(door.StaysOpen);

        // A wedge alone does not open it, and is not taken.
        var run = AtTheHeadworksDoor(session, wedges: 2, defeated: false);
        Assert.Equal(door.LockedText, Use(run, door.Key));
        Assert.False(G3Open(run.Simulation));
        Assert.Equal(2, Wedges(run.Simulation));

        // Nor does the defeat alone.
        run = AtTheHeadworksDoor(session, wedges: 0, defeated: true);
        Assert.Equal(door.LockedText, Use(run, door.Key));
        Assert.False(G3Open(run.Simulation));

        // Both: exactly one wedge leaves the pack and the door holds it open for good.
        run = AtTheHeadworksDoor(session, wedges: 2, defeated: true);
        Assert.Null(Use(run, door.Key));
        Assert.True(G3Open(run.Simulation));
        Assert.Equal(1, Wedges(run.Simulation));
        Assert.Null(Use(run, door.Key));
        Assert.True(G3Open(run.Simulation));
        Assert.Equal(1, Wedges(run.Simulation));
    }

    [Fact]
    public void TheSaddles_TakeNoSecondWedge()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.barrowworks", barrowworks: true);
        Assert.All(session.Setup.Layout.Switches.Where(s => s.Key.StartsWith("switch.barrowworks.headworks_", StringComparison.Ordinal)),
            s => Assert.Null(s.Consume));
        Assert.Single(session.Setup.Layout.Doors, d => d.Fitted is not null);
    }

    /// <summary>The ring in its own region, the character a step south of the Custodian in the court.</summary>
    private static Arena InTheCourt(GameSession session, SimulationSetup rules) =>
        Arena.OpenCreatures(session, rules, (50, 102), 0, [], keepSpawns: true, origin: (0, 0));

    private static CreatureView Boss(Arena arena) => arena.Simulation.Creatures.Single(c => c.DefId == Custodian);

    /// <summary>Strike until the Custodian has taken a wound, then stop.</summary>
    private static void Wound(Arena arena)
    {
        for (int i = 0; i < 400 && Boss(arena).Health == Boss(arena).MaxHealth; i++)
            arena.Fight(Boss(arena), 1);
        Assert.True(Boss(arena).Health < Boss(arena).MaxHealth, "the fight never landed a blow");
    }

    [Fact]
    public void Dying_ResetsTheWoundedCustodianToFullHealth_AndRecordsNoDefeat()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.barrowworks", barrowworks: true);
        var arena = InTheCourt(session, session.Setup);
        var died = arena.Record<PlayerDied>();
        var back = arena.Record<PlayerRespawned>();
        Wound(arena);

        // Stand and take it until the character falls, then wait out the death.
        for (int i = 0; i < 6000 && back.Count == 0; i++)
            arena.Tick();
        Assert.Single(died);
        Assert.Single(back);
        arena.Tick();

        // Respawned outside, so the ring's creatures are not in view: read the record the world keeps.
        Assert.True(arena.Simulation.PlayerInExterior);
        var boss = arena.Simulation.World.Creature("spawn.barrowworks.ring_custodian#0")!;
        Assert.Equal(CreatureCondition.Alive, boss.Condition);
        Assert.Equal(session.Setup.Combat.Creatures[Custodian].MaxHealth, boss.Health);
        Assert.NotEqual(CreatureMind.Engaged, boss.Mind);
        Assert.Equal(0, arena.Simulation.World.GetFlag(CourtCell(session), Defeated));
    }

    [Fact]
    public void LeavingTheCourt_ResetsTheWoundedCustodianToFullHealth()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.barrowworks", barrowworks: true);
        var arena = InTheCourt(session, session.Setup);
        Wound(arena);
        var recovery = session.Setup.Combat.Spawns.Single(s => s.Key == "spawn.barrowworks.ring_custodian").Recovery!;
        BarrowworksFrontierTests.Walk(arena, (x, z) => z < recovery.MinZMm - 2_000);
        arena.Tick(40);

        var boss = Boss(arena);
        Assert.Equal(boss.MaxHealth, boss.Health);
        Assert.Equal(0, arena.Simulation.World.GetFlag(CourtCell(session), Defeated));
    }

    [Fact]
    public void ADefeat_IsDurable_ThroughDeathAndReturn()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.barrowworks", barrowworks: true);
        // A one-blow Custodian: this checks what a defeat leaves behind, not the fight's balance.
        var rules = session.Setup with
        {
            Combat = session.Setup.Combat with
            {
                Creatures = session.Setup.Combat.Creatures.SetItem(Custodian, session.Setup.Combat.Creatures[Custodian] with { MaxHealth = 1 }),
            },
        };
        var arena = InTheCourt(session, rules);
        arena.Fight(Boss(arena), 400);
        Assert.False(Boss(arena).Alive);
        Assert.Equal(1, arena.Simulation.World.GetFlag(CourtCell(session), Defeated));

        arena.Tick((int)session.Setup.Clock.TicksPerDay);
        Assert.False(Boss(arena).Alive);
        Assert.Equal(1, arena.Simulation.World.GetFlag(CourtCell(session), Defeated));
    }

    /// <summary>
    /// The way out is held only by something in the ring that can still come for you. A wolf left fighting outside used
    /// to refuse every exit ("Not while something is fighting you"), however far off and in whatever space.
    /// </summary>
    [Fact]
    public void ACreatureFightingOutside_DoesNotHoldTheRingsExit()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, barrowworks: true);
        var arena = Arena.OpenCreatures(session, session.Setup, (440, 622), 0, [(Arena.Wolf, 440, 624.2, "pack_hunter")], origin: (0, 0));
        var wolf = arena.Creature();
        for (int i = 0; i < 200 && !arena.Creature().Hostile; i++)
        {
            arena.Face(arena.Creature());
            arena.Submit(new AttackCommand(arena.Player));
            arena.Tick();
        }
        Assert.True(arena.Simulation.Creatures.Single(c => c.Key == wolf.Key).Hostile, "the wolf never engaged");

        Assert.Null(arena.Submit(new EnterSpaceCommand(arena.Player, "portal.barrowworks.threshold")));
        Assert.False(arena.Simulation.PlayerInExterior);
        Assert.Null(arena.Submit(new LeaveSpaceCommand(arena.Player)));
        Assert.True(arena.Simulation.PlayerInExterior);
    }
}
