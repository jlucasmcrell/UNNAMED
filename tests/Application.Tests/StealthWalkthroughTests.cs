using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The stealth loop end to end, headless, in the order a player meets it (D23, STEALTH.md): the dark and a quiet step keep a body hidden, a watch
/// that sees it goes from unaware to engaged and records that it was an eye, a dagger from behind ends an unaware body, and a thrown stone draws a
/// watch that never saw the character to the place it landed and starts a search there. The HUD's reading of these numbers is
/// <c>Presentation.Tests.StealthHudRulingTests</c>.
/// </summary>
public class StealthWalkthroughTests
{
    private const string Watch = "creature.humanoid.back_ledger_raider";
    private const string Dagger = "item.weapon.iron_dagger";
    private const long Day = 12_000;
    private const long Night = 40_000;

    /// <summary>A clear patch of the road (StealthSightTests uses it for the same reason).</summary>
    private const double RoadX = 470, RoadZ = 745;

    private static Func<PlayerRecord, PlayerRecord> Kit => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is Dagger).ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack(Dagger, 1));
        inventory.Add(Arena.Stack("item.misc.lure_stone", 3));
        var dagger = inventory.First(e => e.DefId == Dagger).ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg, r.Discoveries,
            new[] { KeyValuePair.Create(EquipSlot.MainHand, dagger) }, r.Currency, r.Effects);
    };

    private static double Metres(long ax, long az, long bx, long bz) => Math.Sqrt(Math.Pow(ax - bx, 2) + Math.Pow(az - bz, 2)) / 1000.0;

    /// <summary>The night is darker than the day on the same ground, and a crouch-walk is quieter than a walk, which is quieter than a sprint.</summary>
    [Fact]
    public void Sneaking_TheDarkHides_AndTheQuieterGaitIsHeardLess()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var none = Array.Empty<(string, double, double, string)>();
        var day = Arena.OpenCreatures(session, session.Setup, (RoadX, RoadZ), 0, none, Kit, startTick: Day, origin: (0, 0));
        var night = Arena.OpenCreatures(session, session.Setup, (RoadX, RoadZ), 0, none, Kit, startTick: Night, origin: (0, 0));
        day.Tick(2);
        night.Tick(2);
        Assert.True(night.Simulation.PlayerLightBand < day.Simulation.PlayerLightBand,
            $"the night has to be darker than the day here ({night.Simulation.PlayerLightBand} against {day.Simulation.PlayerLightBand})");
        Assert.Equal(0, night.Simulation.PlayerNoiseMm);

        long Ring(Gait gait, bool crouched)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, (RoadX, RoadZ), 0, none, Kit, startTick: Night, origin: (0, 0));
            arena.Tick(2);
            arena.Submit(new CrouchCommand(arena.Player, crouched));
            arena.Tick(2);
            arena.Submit(new MoveCommand(arena.Player, new MoveIntent(0, 1_000, gait, 0)));
            arena.Tick(3);
            return arena.Simulation.PlayerNoiseMm;
        }
        long crouch = Ring(Gait.Walk, crouched: true), walk = Ring(Gait.Walk, crouched: false), sprint = Ring(Gait.Sprint, crouched: false);
        Assert.True(crouch > 0, "a crouch-walk is still a sound");
        Assert.True(crouch < walk && walk < sprint, $"crouch {crouch} mm < walk {walk} mm < sprint {sprint} mm");
    }

    /// <summary>Six metres in front of a watch by day: its awareness climbs from nothing, it engages, and what it recorded is that it saw.</summary>
    [Fact]
    public void Detection_AWatchThatSeesTheCharacter_GoesFromUnawareToEngaged_AndRemembersItWasAnEye()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        double facing = Arena.OpenCreatures(session, session.Setup, (RoadX, RoadZ), 0, new[] { (Watch, RoadX, RoadZ, "sentinel") },
            null, startTick: Day, origin: (0, 0)).Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var arena = Arena.OpenCreatures(session, session.Setup, (RoadX + Math.Sin(facing) * 6, RoadZ + Math.Cos(facing) * 6),
            (int)Math.Round(facing * 180 / Math.PI) + 180, new[] { (Watch, RoadX, RoadZ, "sentinel") }, Kit, startTick: Day, origin: (0, 0));
        Assert.Equal(CreatureMind.Unaware, arena.Creature().Mind);
        Assert.Equal(0, arena.Creature().Awareness);

        var minds = new List<CreatureMind>();
        for (int i = 0; i < 40 && arena.Creature().Mind != CreatureMind.Engaged; i++)
        {
            arena.Tick(1);
            minds.Add(arena.Creature().Mind);
        }
        var watch = arena.Creature();
        Assert.Equal(CreatureMind.Engaged, watch.Mind);
        Assert.True(watch.Awareness > 0);
        Assert.Equal(NoticeKind.Sight, watch.LastNotice);
        Assert.Equal(1, arena.Simulation.StealthDetections());
    }

    /// <summary>From behind an unaware wolf with a dagger in hand, the takedown ends it - and nothing noticed the character first.</summary>
    [Fact]
    public void Takedown_FromBehindAnUnawareBody_EndsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (X: 120.0, Z: 80.0);
        double facing = Arena.OpenCreatures(session, session.Setup, (wolf.X, wolf.Z - 60), 0, new[] { (Arena.Wolf, wolf.X, wolf.Z, "pack_hunter") })
            .Simulation.Creatures.Single().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var arena = Arena.OpenCreatures(session, session.Setup, (wolf.X - Math.Sin(facing) * 1.2, wolf.Z - Math.Cos(facing) * 1.2),
            (int)Math.Round(facing * 180 / Math.PI), new[] { (Arena.Wolf, wolf.X, wolf.Z, "pack_hunter") }, Kit);
        arena.Tick(2);
        Assert.Equal(CreatureMind.Unaware, arena.Creature().Mind);

        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(2);
        Assert.False(arena.Simulation.Creatures.Single().Alive);
        Assert.Equal(0, arena.Simulation.StealthDetections());
    }

    /// <summary>
    /// The lure loop: at night a crouched character throws a stone down the open ground at (120, 60) (the stone test's own throw) towards a watch
    /// thirteen metres off. The watch never saw anyone; it heard the stone, knows the place it landed rather than where the character crouches, and
    /// its search starts there.
    /// </summary>
    [Fact]
    public void Lure_AThrownStoneDrawsTheWatchToWhereItLanded_AndTheSearchStartsThere()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 180, new[] { (Watch, 121.0, 47.0, "sentinel") }, Kit, startTick: Night);
        arena.Submit(new CrouchCommand(arena.Player, true));
        arena.Tick(2);
        Assert.Equal(CreatureMind.Unaware, arena.Creature().Mind);

        var body = arena.Simulation.Player.Body;
        var watchBody = arena.Creature().Body;
        var thrown = arena.Record<Thrown>();
        var stone = arena.Simulation.Player.Inventory.First(e => e.DefId == "item.misc.lure_stone");
        Assert.Null(arena.Submit(new ThrowCommand(arena.Player, stone.ItemId)));
        var landed = Assert.Single(thrown);
        double fromWatch = Metres(landed.ToXMm, landed.ToZMm, watchBody.XMm, watchBody.ZMm);
        double fromThrower = Metres(landed.ToXMm, landed.ToZMm, body.XMm, body.ZMm);
        Assert.True(fromWatch < 6 && fromThrower > 10, $"the stone lands by the watch: {fromWatch:0.0} m from it, {fromThrower:0.0} m from the thrower");

        arena.Tick(3);
        var heard = arena.Creature();
        Assert.NotEqual(CreatureMind.Unaware, heard.Mind);
        Assert.NotEqual(CreatureMind.Engaged, heard.Mind);
        Assert.Equal(NoticeKind.Sound, heard.LastNotice);
        Assert.True(Metres(heard.KnownXMm, heard.KnownZMm, landed.ToXMm, landed.ToZMm) < 1,
            $"it knows where the stone landed, not where the character is: {Metres(heard.KnownXMm, heard.KnownZMm, landed.ToXMm, landed.ToZMm):0.0} m off");

        var first = arena.Simulation.SearchPointOf(heard.Id, arena.Simulation.WorldTick, arena.Simulation.WorldTick);
        Assert.True(Metres(first.X, first.Z, landed.ToXMm, landed.ToZMm) <= 9.5,
            $"the search begins at the stone: {Metres(first.X, first.Z, landed.ToXMm, landed.ToZMm):0.0} m from it");
        Assert.Equal(0, arena.Simulation.StealthDetections());
    }
}
