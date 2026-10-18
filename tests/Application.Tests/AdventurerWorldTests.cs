using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP13: NPC adventurers over the game's own content (SYSTEMS_COMPLETENESS_PLAN §2.13) - four in each town, living their days by the canon
/// goal chain and the fixed outcome table (DN2), taking competitive bounties first, culling creatures, trading at the markets, levelling on
/// the character's curve, dying and being replaced (DN4, DN5), hired with their level and gear, grouping up from LFG posts, and the world
/// going on while the character is away (T-22).
/// </summary>
public class AdventurerWorldTests
{
    private const long Day = 57_600;
    private const long Hour = Day / 24;

    private readonly ITestOutputHelper _output;

    public AdventurerWorldTests(ITestOutputHelper output) => _output = output;

    private static Arena AtSpawn(GameSession session, Func<PlayerRecord, PlayerRecord>? change = null, ulong seed = 42) =>
        Arena.OpenCreatures(session, session.Setup, (30, 158), 90, Array.Empty<(string, double, double, string)>(), change, seed: seed, keepSpawns: true);

    /// <summary>Everything adventurers touch, beyond the persistent digest: their views, the boards, the creatures and the markets.</summary>
    private static string Seen(Simulation simulation) =>
        simulation.StateDigest()
        + "|" + string.Join(";", simulation.Adventurers.Select(a => $"{a.Key}:{a.Name}:{a.At}:{a.Level}:{a.Coin}:{a.Condition}:{a.Goal}:{a.GoalRef}:{a.InTown}:{string.Join(",", a.Gear)}"))
        + "|" + string.Join(";", simulation.Bounties.Select(b => $"{b.QuestId}:{b.Standing}:{b.ClaimantId}:{b.PostedAgainTick}"))
        + "|" + string.Join(";", simulation.Creatures.Select(c => $"{c.Key}:{c.Generation}:{c.Condition}"))
        + "|" + string.Join(";", simulation.Markets.SelectMany(m => m.Goods.Select(g => $"{m.MarketId}/{g.ItemId}:{g.Stock}")))
        + "|" + string.Join(";", simulation.Npcs.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => $"{n.Id}:{n.Name}:{n.Body}"));

    private const string Wolves = "quest.bounty.road_wolves";

    /// <summary>A place in region metres, a hand's reach from the Ashen lodge's first body (config.adventurers: the lodge at (460, 752), facing_deg 0).</summary>
    private static readonly (double X, double Z) AtLodge = (457.6, 750.8);

    internal static Arena AtTheLodge(GameSession session, long coin = 0, ulong seed = 42) =>
        Arena.OpenCreatures(session, session.Setup, AtLodge, 90, Array.Empty<(string, double, double, string)>(),
            r => new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, coin),
            seed: seed, keepSpawns: true, origin: Harness.Region);

    /// <summary>
    /// What adventurers make of a world, without the character (whose instance IDs differ from arena to arena): every adventurer, every NPC
    /// life, the creatures, the markets and purses, and the boards.
    /// </summary>
    internal static string World(Simulation simulation, bool bodies = true)
    {
        using var h = new CanonicalHasher();
        simulation.World.Adventurers!.AddTo(h);
        simulation.World.NpcLife?.AddTo(h);
        foreach (var c in simulation.Creatures)
            h.Add(c.Key).Add(c.Generation).Add((int)c.Condition).Add(c.Id.Value);
        foreach (var m in simulation.Markets)
            foreach (var g in m.Goods)
                h.Add(m.MarketId).Add(g.ItemId).Add(g.Stock);
        foreach (var merchant in simulation.World.Merchants)
            h.Add(merchant.MerchantId).Add(merchant.Purse);
        foreach (var b in simulation.Bounties)
            h.Add(b.QuestId).Add((int)b.Standing).Add(b.ClaimantId ?? "-").Add(b.PostedAgainTick ?? -1);
        foreach (var n in simulation.Npcs.Where(n => bodies).OrderBy(n => n.Id, StringComparer.Ordinal))
            h.Add(n.Id).Add(n.Name).Add(n.Body.XMm).Add(n.Body.ZMm);
        return h.Finish();
    }

    [Fact]
    public void TheRegion_KeepsFourAdventurersInEachTown_NamedFromTheirPeople_AtLevelsOneToFive()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = AtSpawn(session).Simulation;
        var adventurers = simulation.Adventurers;
        Assert.Equal(8, adventurers.Length);
        Assert.Equal(4, adventurers.Count(a => a.Settlement == "ashen_hollow"));
        Assert.Equal(4, adventurers.Count(a => a.Settlement == "cairnwold"));
        var names = session.Setup.Character.Peoples["people.veth"].Names.Names;
        Assert.All(adventurers, a => Assert.Contains(a.Name, names));
        Assert.Equal(8, adventurers.Select(a => a.Name).Distinct().Count());
        Assert.DoesNotContain(adventurers, a => simulation.Lives.Any(l => l.Name == a.Name));
        Assert.All(adventurers, a => Assert.InRange(a.Level, 1, 5));
        Assert.All(adventurers, a => Assert.Contains("item.weapon.rusted_sword", a.Gear));
        // Each stands at their lodge, a body the world knows, and can be hired (WP15a).
        Assert.All(adventurers, a => Assert.True(a.InTown));
        var bodies = simulation.Npcs.Where(n => n.Role == "adventurer").ToList();
        Assert.Equal(8, bodies.Count);
        Assert.All(bodies, b => Assert.True(simulation.World.Registry.Exists(b.InstanceId)));
        Assert.Equal(adventurers.Select(a => a.InstanceId).OrderBy(i => i.Value), bodies.Select(b => b.InstanceId).OrderBy(i => i.Value));
    }

    [Fact]
    public void AnAbsence_InAnyPieces_LeavesTheSameWorld()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        long total = 9 * Day + 4_321;
        var once = AtSpawn(session).Simulation;
        once.Advance(total);
        foreach (var pieces in new[] { new long[] { 1, total - 1 }, new long[] { Day + 17, 0, 3 * Day, total - 4 * Day - 17 }, new long[] { Hour, Day - Hour, Day, Day, total - 3 * Day } })
        {
            var chunked = AtSpawn(session).Simulation;
            foreach (long piece in pieces)
                chunked.Advance(piece);
            Assert.Equal(World(once), World(chunked));
        }
        // Something happened: the boards, the hunts, the markets.
        Assert.Contains(once.Bounties, b => b.Standing == BountyStanding.Claimed && once.Adventurers.Any(a => a.InstanceId.Value == b.ClaimantId)
                                            || once.World.Adventurers!.Agents.Values.Any(a => a.BountiesClaimed > 0));
        Assert.Contains(once.World.Adventurers!.Agents.Values, a => a.Kills > 0);
    }

    [Fact]
    public void TheWorldRunning_AndTheWorldAway_LiveTheSameDays()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var away = AtSpawn(session).Simulation;
        var running = AtSpawn(session).Simulation;
        // Both away until just before the third midnight; then one steps over it tick by tick while the other is away over it.
        long third = 3 * Day - away.Setup.Clock.StartTicks;
        away.Advance(third - 20);
        running.Advance(third - 20);
        away.Advance(40);
        for (int t = 0; t < 40; t++)
            running.Step();
        // The adventurers' own lives are the same either way; what is out of sight of a running world (creatures it simulates in tier A) may
        // differ, so only their lives are compared.
        using var a = new CanonicalHasher();
        using var b = new CanonicalHasher();
        // Bodies too are what a running world's sight leaves standing (they come and go only out of it), so not compared.
        (away.World.Adventurers! with { Bodies = ImmutableSortedDictionary<string, AdventurerBody>.Empty }).AddTo(a);
        (running.World.Adventurers! with { Bodies = ImmutableSortedDictionary<string, AdventurerBody>.Empty }).AddTo(b);
        Assert.Equal(a.Finish(), b.Finish());
    }

    [Fact]
    public void TheyTakeCompetitiveBounties_FirstClaimantWins_AndTheBoardNamesThem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtSpawn(session);
        var claims = arena.Record<BountyClaimed>();
        for (int d = 0; d < 12 && claims.Count == 0; d++)
            arena.Simulation.Advance(Day);
        Assert.NotEmpty(claims);
        var claim = claims[0];
        var board = arena.Simulation.Bounties.Single(b => b.QuestId == claim.QuestId);
        Assert.True(session.Setup.Quests.Quests[claim.QuestId].Competitive);
        var claimant = arena.Simulation.Adventurers.SingleOrDefault(a => a.InstanceId.Value == claim.ClaimantId);
        if (board.Standing == BountyStanding.Claimed && board.ClaimantId == claim.ClaimantId && claimant is not null)
            Assert.Equal(claimant.Name, board.ClaimantName);
        // A board's bounty is claimed once until it is posted again.
        Assert.Equal(claims.Count, claims.Select(c => (c.QuestId, c.Tick)).Distinct().Count());
    }

    [Fact]
    public void Hired_TheyKeepTheirLevelAndGear_AndTakeThemBackWhenDismissed()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtTheLodge(session, coin: 50);
        var adventurer = arena.Simulation.Adventurers.Single(a => a.Key == "npc.adventurer.ashen_hollow.slot_1_g0");
        var hireability = arena.Record<HireabilityChanged>();
        Assert.True(arena.Simulation.Hiring.Hireability(adventurer.Key)!.Hireable);
        Assert.Equal(new[] { HirelingRole.Guard, HirelingRole.Porter }, arena.Simulation.Hiring.Hireability(adventurer.Key)!.HiresAs);
        Assert.Null(arena.Submit(new HireCommand(arena.Player, adventurer.Key, HirelingRole.Guard, 3, AcceptsRisk: true)));
        Assert.False(arena.Simulation.Hiring.Hireability(adventurer.Key)!.Hireable);
        arena.Simulation.Step();
        Assert.Contains(hireability, h => h.Key == adventurer.Key && !h.Hireable);

        var member = arena.Simulation.Companions.Single(c => c.NpcId == adventurer.Key);
        Assert.Equal(PartyMembership.Hireling, member.Membership);
        var sheet = arena.Simulation.PartySheet(adventurer.Key)!;
        Assert.Equal(adventurer.Level, sheet.Level);
        Assert.Equal(adventurer.Gear.OrderBy(g => g), sheet.Gear.Select(g => g.DefId).OrderBy(g => g));
        Assert.All(sheet.Gear, g => Assert.False(g.Loaned));
        Assert.Equal("item.weapon.rusted_sword", member.WeaponId);
        var hired = arena.Simulation.Adventurers.Single(a => a.Key == adventurer.Key);
        Assert.Equal("hireling", hired.Party);
        Assert.False(hired.InTown);

        // While with the party their own life waits: a midnight plans nothing for them.
        arena.Simulation.Advance(Day);
        Assert.Equal(AdventurerGoals.Rest, arena.Simulation.Adventurers.Single(a => a.Key == adventurer.Key).Goal);

        Assert.Null(arena.Submit(new DismissCommand(arena.Player, adventurer.Key)));
        var back = arena.Simulation.Adventurers.Single(a => a.Key == adventurer.Key);
        Assert.Null(back.Party);
        Assert.Equal(adventurer.Gear.OrderBy(g => g), back.Gear.OrderBy(g => g));
        Assert.True(back.Level >= adventurer.Level);
        Assert.Null(arena.Simulation.PartySheet(adventurer.Key));
        Assert.DoesNotContain(arena.Simulation.Companions, c => c.NpcId == adventurer.Key);
    }

    [Fact]
    public void AHiredAdventurerWhoFalls_Dies_AndSomeoneNewTakesTheSlot()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtTheLodge(session, coin: 50);
        const string key = "npc.adventurer.ashen_hollow.slot_1_g0";
        Assert.Null(arena.Submit(new HireCommand(arena.Player, key, HirelingRole.Guard, 3, AcceptsRisk: true)));
        var died = arena.Record<AdventurerDied>();
        // Downed, and left with no hand past the revive window (DN9).
        arena.Simulation.Submit(new CompanionStruck(arena.Simulation.Creatures.First(c => c.Alive).Id, key,
            new UNNAMED.Domain.Combat.AttackProfile("test", 10_000, 10_000, UNNAMED.Domain.Combat.DamageTypes.All[0], 1_000, 1, 1, 1, 1)));
        for (int i = 0; i < session.Setup.Social.Companions!.ReviveWindowTicks + 2; i++)
            arena.Simulation.Step();
        Assert.Single(died);
        Assert.DoesNotContain(arena.Simulation.Adventurers, a => a.Key == key);
        Assert.True(arena.Simulation.World.Adventurers!.Vacancies.ContainsKey("ashen_hollow.slot_1"));
        arena.Simulation.Advance(8 * Day);
        Assert.Contains(arena.Simulation.Adventurers, a => a.Key == "npc.adventurer.ashen_hollow.slot_1_g1");
        Assert.Equal(8, arena.Simulation.Adventurers.Length);
    }

    [Fact]
    public void TheyAskForCompany_AndAnAnsweredOffer_GoesWithTheCharacter_UntilMidnight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtTheLodge(session);
        var sim = arena.Simulation;
        // Day by day until someone at the Ashen lodge asks for company.
        SettingOut? offer = null;
        var posts = arena.Record<SettingOut>();
        for (int d = 0; d < 20 && offer is null; d++)
        {
            long midnight = (sim.Setup.Clock.Day(sim.WorldTick) + 1) * Day - sim.Setup.Clock.StartTicks;
            sim.Advance(midnight - sim.WorldTick + 1);
            var asking = sim.World.Adventurers!.Agents.Values.FirstOrDefault(a => a.At == "ashen_hollow" && a.Plan.AsksCompany && a.Plan.Day == sim.Setup.Clock.Day(sim.WorldTick));
            if (asking is null)
                continue;
            sim.Advance(asking.Plan.PostTick - sim.WorldTick - 1);
            for (int i = 0; i < 4 && posts.Count == 0; i++)
                sim.Step();
            offer = posts.FirstOrDefault(p => p.NpcName == asking.Name);
        }
        Assert.NotNull(offer);
        Assert.True(offer!.DepartTick - offer.Tick >= 20 * Hour / 60);   // at least 20 game minutes ahead (the WP12 contract)
        Assert.Contains(sim.GroupOffers, o => o.Npc == offer.Npc);
        Assert.Null(arena.Submit(new JoinGroupCommand(arena.Player, offer.Npc)));
        var formed = arena.Record<AllyGroupFormed>();
        while (sim.WorldTick < offer.DepartTick)
            sim.Step();
        Assert.Single(formed);
        var key = sim.Adventurers.Single(a => a.InstanceId == offer.Npc).Key;
        Assert.Equal(PartyMembership.TemporaryAlly, sim.Companions.Single(c => c.NpcId == key).Membership);
        Assert.Equal("temporary_ally", sim.Adventurers.Single(a => a.Key == key).Party);

        // The day's company ends at midnight: they part and go on with their own life.
        long next = (sim.Setup.Clock.Day(sim.WorldTick) + 1) * Day - sim.Setup.Clock.StartTicks;
        sim.Advance(next - sim.WorldTick);
        Assert.DoesNotContain(sim.Companions, c => c.NpcId == key);
        Assert.Null(sim.Adventurers.Single(a => a.Key == key).Party);
    }

    [Fact]
    public void OneHiringAnswer_ForResidentsAndAdventurers_AndOnlyTheAdventurersRaiseTheirChanges()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtTheLodge(session, coin: 50);
        const string key = "npc.adventurer.ashen_hollow.slot_1_g0";
        Assert.True(arena.Simulation.IsHireable(key));
        Assert.Equal(new[] { "adventurer" }, arena.Simulation.Hiring.Hireability(key)!.Roles);
        var changes = arena.Record<HireabilityChanged>();
        Assert.Null(arena.Submit(new HireCommand(arena.Player, key, HirelingRole.Guard, 3, AcceptsRisk: true)));
        arena.Simulation.Step();
        Assert.Single(changes, c => c.Key == key);
        Assert.False(arena.Simulation.IsHireable(key));
        Assert.Equal("is with you", arena.Simulation.Hiring.Hireability(key)!.Refusal);
    }

    [Fact]
    public void AnAdventurerOutOnTheirDay_CannotBeHired_ThoughTheirBodyStandsInSight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtTheLodge(session, coin: 50);
        var sim = arena.Simulation;
        AdventurerRecord? gone = null;
        for (int d = 0; d < 20 && gone is null; d++)
        {
            long midnight = (sim.Setup.Clock.Day(sim.WorldTick) + 1) * Day - sim.Setup.Clock.StartTicks;
            sim.Advance(midnight - sim.WorldTick + 1);
            var leaving = sim.World.Adventurers!.Agents.Values.FirstOrDefault(a => a.At == "ashen_hollow" && a.Party is null
                && AdventurerGoals.Out(a.Plan.Goal) && a.Plan.Day == sim.Setup.Clock.Day(sim.WorldTick));
            if (leaving is null)
                continue;
            sim.Advance(leaving.Plan.DepartTick - sim.WorldTick + 1);
            if (sim.Npcs.Any(n => n.Id == leaving.Key))
                gone = leaving;
        }
        Assert.NotNull(gone);
        Assert.False(sim.IsHireable(gone!.Key));
        Assert.Equal("is out", sim.Hiring.Hireability(gone.Key)!.Refusal);
        Assert.Equal($"{gone.Name} is out", arena.Submit(new HireCommand(arena.Player, gone.Key, HirelingRole.Guard, 3, AcceptsRisk: true)));
    }

    [Fact]
    public void AnAbsence_WithAHiredAdventurerWhoseContractEnds_InAnyPieces_LeavesTheSameWorld()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string key = "npc.adventurer.ashen_hollow.slot_1_g0";
        Simulation Hired()
        {
            var arena = AtTheLodge(session, coin: 200);
            Assert.Null(arena.Submit(new HireCommand(arena.Player, key, HirelingRole.Guard, 2, AcceptsRisk: true)));
            return arena.Simulation;
        }
        long total = 7 * Day + 1_234;
        var once = Hired();
        once.Advance(total);
        Assert.DoesNotContain(once.Companions, c => c.NpcId == key);
        foreach (var pieces in new[] { new long[] { Day + 17, total - Day - 17 }, new long[] { 3 * Day - 5, 10, total - 3 * Day - 5 } })
        {
            var chunked = Hired();
            foreach (long piece in pieces)
                chunked.Advance(piece);
            Assert.Equal(World(once, bodies: false), World(chunked, bodies: false));
        }
    }

    [Fact]
    public void AnAbsence_WithTheCharacterAtTheCairnwoldLodge_InAnyPieces_LeavesTheSameBodies()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Simulation Stood(bool tick)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, (session.Setup.Layout.Spawn.XMm / 1000.0, session.Setup.Layout.Spawn.ZMm / 1000.0), 0,
                Array.Empty<(string, double, double, string)>(), null, seed: 42, keepSpawns: true, origin: (0, 0));
            var lodge = session.Setup.Adventurers!.Settlements["cairnwold"];
            long x = lodge.LodgeXMm - 1_500, z = lodge.LodgeZMm - 1_500;
            Assert.Null(arena.Simulation.Submit(new Relocate(new Body(x, arena.Simulation.Setup.Layout.Space.Terrain.HeightAtMm(x, z), z, 0))));
            if (tick)
                arena.Tick(10);
            return arena.Simulation;
        }
        foreach (bool tick in new[] { false, true })
            foreach (long tail in new[] { 4_321L, 9 * Hour + 77, 12 * Hour, 15 * Hour + 5, 19 * Hour, 22 * Hour + 900 })
            {
                long total = 6 * Day + tail;
                var once = Stood(tick);
                once.Advance(total);
                foreach (var pieces in new[]
                         {
                             new long[] { 1, total - 1 }, new long[] { Hour, total - Hour }, new long[] { Day / 2, Day, total - Day - Day / 2 },
                             new long[] { Day + 17, 0, Day - 5, total - 2 * Day - 12 }, new long[] { Hour, Hour, Hour, Hour, total - 4 * Hour },
                         })
                {
                    var chunked = Stood(tick);
                    foreach (long piece in pieces)
                        chunked.Advance(piece);
                    Assert.True(World(once) == World(chunked), $"tick {tick} tail {tail} pieces {string.Join(',', pieces)}{Environment.NewLine}once:    {Bodies(once)}{Environment.NewLine}chunked: {Bodies(chunked)}");
                }
            }
    }

    private static string Bodies(Simulation s) => string.Join(" ", s.World.Adventurers!.Bodies.Select(b => $"{b.Key}@{b.Value.XMm}")) + " | " + string.Join(" ", s.World.Adventurers!.Agents.Values.Select(a => $"{a.Key}:{a.Plan.Goal}/{a.Condition}"));
}
