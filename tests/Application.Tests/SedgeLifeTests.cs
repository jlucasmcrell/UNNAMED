using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Crafting;
using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using System.Text.Json;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

public sealed class SedgeLifeTests
{
    private static PlayerRecord OnDeck(GameSession session, PlayerRecord r, long coin = 0, bool wear = false) =>
        new(r.Id, r.Name, r.XMm, session.Setup.Layout.Space.HeightAtMm(r.XMm,r.ZMm), r.ZMm, r.AppearanceSeed,
            r.Inventory.Select(i => wear && i.DefId=="item.weapon.rusted_sword"
                ? i with { Wear = new ItemWear(100,Quality.Standard) } : i).ToImmutableArray(), r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, coin, r.Effects) { Identity = r.Identity };
    [Fact]
    public void RaiderLinksTwoCloseCutsIntoAHeavyFinisher()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile,"region.sedge_levels");
        var arena=Arena.OpenCreatures(session,session.Setup,(5135,1266),180,
            new[] { ("creature.humanoid.back_ledger_raider",5135.0,1265.0,"sentinel") },origin:Harness.Region);
        var swings=arena.Record<AttackStarted>();
        Assert.Null(arena.Submit(new BlockCommand(arena.Player,true)));
        for (int i=0;i<400;i++) arena.Tick();
        var moves=swings.Where(e=>e.Attacker!=arena.Player).Select(e=>e.Source).ToArray();
        Assert.True(Enumerable.Range(0,Math.Max(0,moves.Length-2)).Any(i=>
            moves[i]=="ability.creature.back_ledger_raider_light" && moves[i+1]==moves[i]
            && moves[i+2]=="ability.creature.back_ledger_raider_heavy"),string.Join(",",moves));
    }

    [Fact]
    public void LocalFerryRefusesAnEmptyPurseAndCarriesThePlayerBothWaysWithoutSkippingTime()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, "region.sedge_levels");
        var arena = Arena.OpenCreatures(session, session.Setup, (4998.5,1098.5), 0,
            Array.Empty<(string,double,double,string)>(), r => OnDeck(session,r,4), origin: Harness.Region);
        var journeys = arena.Record<PassageTaken>();
        Assert.Null(arena.Submit(new TalkCommand(arena.Player,"npc.sedge.tess_ferry")));
        Assert.Contains("high water",arena.Submit(new ChooseCommand(arena.Player,"passage"))!);
        Assert.Equal(4,arena.Simulation.Player.Currency);
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        arena.Simulation.Advance(14400);
        long before = arena.Simulation.WorldTick;
        void Cross(string npc)
        {
            Assert.Null(arena.Submit(new TalkCommand(arena.Player,"npc.sedge."+npc)));
            Assert.Null(arena.Submit(new ChooseCommand(arena.Player,"passage")));
        }
        Cross("tess_ferry");
        Assert.Equal((4755.5,1343.0),arena.At);
        Assert.Equal(before,arena.Simulation.WorldTick);
        Assert.Equal(2,arena.Simulation.Player.Currency);
        Cross("iren_orr");
        Assert.Equal((4998.5,1098.5),arena.At);
        Assert.Equal(before,arena.Simulation.WorldTick);
        Assert.Equal(0,arena.Simulation.Player.Currency);
        Assert.Equal(2,journeys.Count);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player,"npc.sedge.tess_ferry")));
        Assert.NotNull(arena.Submit(new ChooseCommand(arena.Player,"passage")));
        Assert.Equal((4998.5,1098.5),arena.At);
        Assert.Equal(2,journeys.Count);
    }

    [Fact]
    public void CreaturesSpawnOnTheSupportedWalkRatherThanTheTerrainBelowIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, "region.sedge_levels");
        var arena = Arena.OpenCreatures(session, session.Setup, (5000, 710), 0,
            new[] { ("creature.beast.sedge_eel_serpent", 4930.0, 710.0, "ambusher") }, origin: Harness.Region);
        var body = Assert.Single(arena.Simulation.Creatures).Body;
        Assert.Equal(session.Setup.Layout.Space.HeightAtMm(body.XMm, body.ZMm), body.YMm);
        Assert.True(body.YMm > session.Setup.Layout.Space.Terrain.HeightAtMm(body.XMm, body.ZMm) + 2000);
    }

    [Fact]
    public void RaiderReactsToSeenWindupsWithAGuardOrPhysicalDodge()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, "region.sedge_levels");
        const string raider = "creature.humanoid.back_ledger_raider";
        var original = session.Setup.Combat.Creatures[raider];
        AttackProfile Gentle(AttackProfile attack) => attack with { DamageMin = 1, DamageMax = 1 };
        // Long-lived practice partners isolate reactions from death and stamina
        // exhaustion; the encounter's shipped health/damage remain unchanged.
        var definition = original with { MaxHealth = 100_000, Attack = Gentle(original.Attack),
            Charge = Gentle(original.Charge!), CloseAttack = Gentle(original.CloseAttack!) };
        var rules = session.Setup with { Combat = session.Setup.Combat with {
            Creatures = session.Setup.Combat.Creatures.SetItem(raider, definition),
            Constants = session.Setup.Combat.Constants with { StaminaRegenPerSecond = 10000, StaminaRegenDelayTicks = 0 } } };
        var arena = Arena.OpenCreatures(session, rules, (5150, 1252), 180,
            new[] { (raider, 5150.0, 1250.0, "sentinel") }, origin: Harness.Region);
        bool guard = false, dodge = false;
        for (int i = 0; i < 1200; i++)
        {
            if (i % 20 == 0) arena.Submit(new AttackCommand(arena.Player));
            arena.Tick();
            var enemy = Assert.Single(arena.Simulation.Creatures);
            guard |= enemy.Guarding;
            dodge |= enemy.Phase == CombatPhase.Dodge;
        }
        Assert.True(guard, "No visible windup caused a guard");
        Assert.True(dodge, "No visible windup caused a dodge");
    }

    [Fact]
    public void TollpilesOffersStockRepairAndPaidRestWithoutSkippingTime()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, "region.sedge_levels");
        var arena = Arena.OpenCreatures(session, session.Setup, (4990.5, 1080), 270,
            Array.Empty<(string, double, double, string)>(), r => OnDeck(session,r,100,wear:true), origin: Harness.Region);
        foreach (var npc in arena.Simulation.Npcs)
            Assert.Equal(session.Setup.Layout.Space.HeightAtMm(npc.Body.XMm, npc.Body.ZMm), npc.Body.YMm);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, "npc.sedge.nessa_reed")));
        Assert.Contains(arena.Simulation.Conversation!.Replies, r => r.Id == "trade");
        Assert.Contains(arena.Simulation.Conversation!.Replies, r => r.Id == "rest");
        var slept = arena.Record<RoomRented>();
        long before = arena.Simulation.WorldTick;
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "rest")));
        Assert.Equal("sedge_tollpiles", Assert.Single(slept).Settlement);
        Assert.Equal(95, arena.Simulation.Player.Currency);
        Assert.Equal(before, arena.Simulation.WorldTick);
        if (arena.Simulation.Conversation is not null)
            Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        var food = arena.Simulation.Wares("npc.sedge.nessa_reed")!.Wares.First(w => w.ItemId == "item.consumable.roast_meat");
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, "npc.sedge.nessa_reed", food.Ref, 1)));
        Assert.Contains(arena.Simulation.Player.Inventory, i => i.DefId == "item.consumable.roast_meat");
        foreach (var (x, z) in new[] { (4995.0, 1080.0), (5000.0, 1080.0), (5000.0, 1089.0), (5005.0, 1089.0), (5009.5, 1089.0) })
            Assert.True(arena.WalkTo(x, z), $"Could not walk between the two civic service landings at {x},{z}");
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, "npc.sedge.oren_pin")));
        Assert.Contains(arena.Simulation.Conversation!.Replies, r => r.Id == "repair");
        var sword=arena.Simulation.Player.Inventory.Single(i=>i.DefId=="item.weapon.rusted_sword");
        var quote=arena.Simulation.RepairQuote("npc.sedge.oren_pin",sword.ItemId)!;
        Assert.NotNull(quote);
        long coin=arena.Simulation.Player.Currency;
        Assert.Null(arena.Submit(new MerchantRepairCommand(arena.Player,"npc.sedge.oren_pin",sword.ItemId)));
        var condition=arena.Simulation.Player.Inventory.Single(i=>i.ItemId==sword.ItemId).Wear;
        Assert.True(condition is null || condition.Points==0);
        Assert.Equal(coin-quote.Fee,arena.Simulation.Player.Currency);
    }

    [Fact]
    public void SurveyCanBeOfferedAgainAfterDecliningAndStartsAtItsBrief()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, "region.sedge_levels");
        var arena = Arena.OpenCreatures(session, session.Setup, (5000, 1072), 0,
            Array.Empty<(string, double, double, string)>(), origin: Harness.Region);
        const string clerk = "npc.sedge.sella_tally";
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, clerk)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "leave")));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, clerk)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "survey")));
        arena.Tick();
        var quest = Assert.Single(arena.Simulation.Quests, q => q.Id == "quest.sedge.the_kept_crossing");
        Assert.Equal(QuestStatus.Active, quest.Status);
        Assert.Contains(quest.Objectives, o => o.Id == "o_cuts" && o.Status == ObjectiveStatus.Active);
    }

    [Fact]
    public void CrewQuestWalksGathersDeliversAndRecordsEitherReport()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, "region.sedge_levels");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Harness.RepoRoot(), "content/sedge_gate2_route.json")));
        var route = json.RootElement.GetProperty("points").EnumerateArray().Select(p => (X:p[0].GetDouble(), Z:p[1].GetDouble())).ToArray();
        foreach (string report in new[] { "public", "private" })
        {
            var arena = Arena.OpenCreatures(session, session.Setup, (5000, 1072), 0,
                Array.Empty<(string, double, double, string)>(), r => OnDeck(session,r), origin: Harness.Region);
            var choices = arena.Record<ChoiceMade>();
            void Walk(double x, double z) => Assert.True(arena.WalkTo(x,z), $"{report}: stopped at {arena.At}, wanted {x},{z}");
            void Say(string npc, params string[] replies)
            {
                Assert.Null(arena.Submit(new TalkCommand(arena.Player, "npc.sedge."+npc)));
                foreach (string reply in replies) Assert.Null(arena.Submit(new ChooseCommand(arena.Player, reply)));
                if (arena.Simulation.Conversation is not null) Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
                arena.Tick();
            }
            Say("sella_tally", "survey", "go");
            for (int i=163;i<=341;i++) Walk(route[i].X,route[i].Z);
            Walk(4799,882);
            var wood = arena.Simulation.Nodes.Single(n=>n.Name=="sedge_tidewashed_spar");
            Assert.Null(arena.Submit(new GatherCommand(arena.Player,wood.Key))); arena.Tick();
            Walk(4802,877);
            for (int i=341;i>=163;i--) Walk(route[i].X,route[i].Z);
            Walk(5000,1089); Walk(5005,1089); Walk(5009.5,1089);
            Say("oren_pin","crew","leave");
            Say("oren_pin","deliver","leave");
            Assert.Equal(4,arena.Simulation.Player.Inventory.Where(i=>i.DefId=="item.material.timber").Sum(i=>i.Count));
            Walk(5005,1089);Walk(5000,1089);
            for (int i=159;i<=265;i++) Walk(route[i].X,route[i].Z);
            foreach (double z in new double[] {600,500,400,300,200,100,80}) Walk(5000,z);
            Walk(5000,40); Walk(5006,40); Walk(5010,40); Walk(5016,40);
            Walk(5010,40); Walk(5006,40); Walk(5000,40);
            foreach (double z in new double[] {100,200,300,400,500,600,685}) Walk(5000,z);
            for (int i=265;i>=163;i--) Walk(route[i].X,route[i].Z);
            Walk(5000,1072);
            Say("sella_tally",report+"_report","leave");
            Assert.Equal(QuestStatus.Completed,arena.Simulation.Quests.Single(q=>q.Id=="quest.sedge.the_kept_crossing").Status);
            Assert.Equal(report=="public"?1:2,Assert.Single(choices).Value);
        }
    }
}
