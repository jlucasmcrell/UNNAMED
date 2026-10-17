using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP15a: the party over the game's own content (SYSTEMS_COMPLETENESS_PLAN §2.15) - the character plus three, companions and hirelings
/// sharing the slots (DN11); hiring on stated terms, with the risk of death said and accepted (DN9); wages from the purse each midnight,
/// a day's grace, then a lapse; contracts served out; dismissal with no refund; a hireling's death that lasts; porters, gatherers and
/// crafters at their work; and the party's share of the world going on while the character is away, the same however the absence is cut
/// (T-22). The region's townsfolk stand in for the hireable role residents WP12 brings: content gives none a <c>hireling</c> block yet.
/// </summary>
public class PartyTests
{
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Ore = "item.material.iron_ore";
    private const string Billet = "item.material.iron_ingot";
    private const string BilletRecipe = "recipe.smithing.iron_billet";
    private const string AshStand = "node.wood.ash_stand";

    /// <summary>Beside Kera at the smithy, within a hand's reach of her (content/regions/ashen_hollow.yaml).</summary>
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);

    /// <summary>The setup with these townsfolk hireable in every role, each with a spear.</summary>
    private static SimulationSetup Hireable(SimulationSetup setup, params string[] ids) => setup with
    {
        Social = setup.Social with
        {
            Npcs = ids.Aggregate(setup.Social.Npcs, (npcs, id) => npcs.SetItem(id, npcs[id] with
            {
                Hireling = new HirelingProfile(Enum.GetValues<HirelingRole>().ToImmutableArray(),
                    new CompanionProfile(60, "item.weapon.march_spear", ImmutableSortedDictionary<BodyRegion, int>.Empty)),
            })),
        },
    };

    private static PlayerRecord WithCoin(PlayerRecord r, long coin, params InventoryEntry[] extra) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, coin,
            r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions);

    private static PlayerRecord Taught(PlayerRecord r) =>
        r.WithProgression(r.Progression with
        {
            Known = r.Progression.Known.SetItem(BilletRecipe, new KnownTechnique(LearningSource.Teacher, Kera, 0)),
        });

    private static Arena At(GameSession session, long coin, Func<PlayerRecord, PlayerRecord>? change = null, long startTick = 0, params InventoryEntry[] extra) =>
        Arena.OpenCreatures(session, Hireable(session.Setup, Kera, Renn, Sel), AtKera, 0, Array.Empty<(string, double, double, string)>(),
            r => change?.Invoke(WithCoin(r, coin, extra)) ?? WithCoin(r, coin, extra), startTick: startTick);

    private static CompanionRecord Member(string npc, double x, double z, PartyMembership membership = PartyMembership.Hireling,
        HirelingRole role = HirelingRole.Guard, int days = 10, long wage = 8) =>
        new(npc, CompanionOrder.Follow, CompanionCondition.Up, Harness.InRegion(x, z).XMm, Harness.InRegion(x, z).ZMm, 0, membership == PartyMembership.Hireling ? 60 : 100)
        {
            Membership = membership,
            Contract = membership == PartyMembership.Hireling ? new HirelingContract(role, days, wage, PartyRules.WageOnly) : null,
        };

    /// <summary>The tick of the world's <paramref name="day"/>-th midnight (config.time: the clock reads 07:30 at tick 0).</summary>
    private static long Midnight(SimulationSetup setup, int day) => day * setup.Clock.TicksPerDay - setup.Clock.StartTicks;

    private static string? Hire(Arena arena, string npc, HirelingRole role, int days, bool acceptsRisk = true) =>
        arena.Submit(new HireCommand(arena.Player, npc, role, days, acceptsRisk));

    private static CompanionView Member(Arena arena, string npc) => arena.Simulation.Companions.Single(c => c.NpcId == npc);

    private static int Carried(Simulation simulation, string defId) => simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static (long XMm, long ZMm) Home(SimulationSetup setup, string npc) =>
        setup.Layout.Npcs.Single(n => n.NpcId == npc) is var site ? (site.XMm, site.ZMm) : default;

    [Fact]
    public void Hiring_StatesItsTerms_AndThatTheyCanDie_AndTakesTheFirstDaysWage()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 50);
        var hired = arena.Record<HirelingHired>();

        var terms = arena.Simulation.HireTerms(Kera, HirelingRole.Guard, 5);
        Assert.Equal(new HireTerms(Kera, HirelingRole.Guard, 5, 8, 40, PartyRules.WageOnly, CanDie: true, null), terms);   // config.party: a guard is 8 a day
        Assert.Equal("a hireling can die in your service: the hire needs that risk accepted", Hire(arena, Kera, HirelingRole.Guard, 5, acceptsRisk: false));
        Assert.Equal("Renn Vale is out of reach", Hire(arena, Renn, HirelingRole.Guard, 5));
        Assert.Equal("a contract runs 1 to 30 days", Hire(arena, Kera, HirelingRole.Guard, 31));
        Assert.Empty(arena.Simulation.Companions);
        Assert.Equal(50, arena.Simulation.Player.Currency);

        Assert.Null(Hire(arena, Kera, HirelingRole.Guard, 5));

        Assert.Equal(new HirelingHired(Kera, HirelingRole.Guard, 5, 8, CanDie: true, 0), Assert.Single(hired));
        Assert.Equal(42, arena.Simulation.Player.Currency);
        var her = Member(arena, Kera);
        Assert.Equal((PartyMembership.Hireling, CompanionStance.Aggressive, CompanionOrder.Follow, 60), (her.Membership, her.Stance, her.Order, her.MaxHealth));
        Assert.Equal(new HirelingContract(HirelingRole.Guard, 5, 8, PartyRules.WageOnly) { Purse = 8 }, her.Contract);
        Assert.Equal("Kera Voss is already with you", Hire(arena, Kera, HirelingRole.Guard, 5));
    }

    /// <summary>
    /// A hire by dialogue that cannot go through - here, a purse short of the first wage - refuses the reply: the conversation stays where
    /// it was, so the hiring line is not spent, and no one is hired. A conversation of this test's own, given to Kera.
    /// </summary>
    [Fact]
    public void AHireByDialogue_ThatIsRefused_RefusesTheReply()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var dialogue = new DialogueDefinition("dialogue.test.hire", ImmutableArray.Create(Kera), "ask", new[]
        {
            new DialogueNode("ask", "Need a guard?", ImmutableArray.Create(
                new DialogueChoice("hire", "Five days, then.", ImmutableArray<DialogueCondition>.Empty,
                    ImmutableArray.Create<DialogueConsequence>(new HireHirelingConsequence(HirelingRole.Guard, 5)), "hired")), null, false, null),
            new DialogueNode("hired", "Lead on.", ImmutableArray<DialogueChoice>.Empty, null, false, null),
        }.ToImmutableSortedDictionary(n => n.Id, n => n, StringComparer.Ordinal));
        var setup = Hireable(session.Setup, Kera);
        setup = setup with
        {
            Social = setup.Social with
            {
                Npcs = setup.Social.Npcs.SetItem(Kera, setup.Social.Npcs[Kera] with { DialogueId = dialogue.Id }),
                Dialogues = setup.Social.Dialogues.SetItem(dialogue.Id, dialogue),
            },
        };
        Arena Open(long coin) => Arena.OpenCreatures(session, setup, AtKera, 0, Array.Empty<(string, double, double, string)>(), r => WithCoin(r, coin));

        var poor = Open(5);
        Assert.Null(poor.Submit(new TalkCommand(poor.Player, Kera)));
        Assert.Equal("not enough coin: the first day's wage is 8, 5 carried", poor.Submit(new ChooseCommand(poor.Player, "hire")));
        Assert.Equal("ask", poor.Simulation.Conversation!.NodeId);
        Assert.Empty(poor.Simulation.Companions);

        var paid = Open(50);
        Assert.Null(paid.Submit(new TalkCommand(paid.Player, Kera)));
        Assert.Null(paid.Submit(new ChooseCommand(paid.Player, "hire")));
        Assert.Equal("hired", paid.Simulation.Conversation!.NodeId);
        Assert.Equal(PartyMembership.Hireling, Member(paid, Kera).Membership);
    }

    [Fact]
    public void ThePartyHoldsThree_CompanionsAndHirelingsAlike_AndASlotFreedCanBeFilled()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 50, r => r.WithCompanions(new[]
        {
            Member(Tavar, 62, 142, PartyMembership.Companion), Member(Renn, 59, 142), Member(Sel, 61, 138, role: HirelingRole.Porter, wage: 4),
        }));
        Assert.Equal(3, arena.Simulation.Companions.Length);

        Assert.Equal("your party is full (3 with you)", arena.Simulation.HireTerms(Kera, HirelingRole.Guard, 3).Refusal);
        Assert.Equal("your party is full (3 with you)", Hire(arena, Kera, HirelingRole.Guard, 3));

        Assert.Null(arena.Submit(new DismissCommand(arena.Player, Sel)));
        Assert.Null(Hire(arena, Kera, HirelingRole.Guard, 3));
        Assert.Equal(new[] { Kera, Renn, Tavar }, arena.Simulation.Companions.Select(c => c.NpcId).OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>Each midnight the next day's wage comes out of the purse; with too little in it, one day's grace, then the contract lapses.</summary>
    [Fact]
    public void Wages_ArePaidEachMidnight_AndUnpaid_TheContractLapsesAfterADaysGrace()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = session.Setup;
        long day = setup.Clock.TicksPerDay;
        var arena = At(session, 16, startTick: Midnight(setup, 1) - 5);
        var wages = arena.Record<HirelingWage>();
        var left = arena.Record<PartyMemberLeft>();
        Assert.Null(Hire(arena, Kera, HirelingRole.Guard, 10));
        Assert.Equal(8, arena.Simulation.Player.Currency);

        arena.Tick(10);   // midnight, played through
        Assert.Equal(new HirelingWage(Kera, 8, 0, Midnight(setup, 1)), Assert.Single(wages));
        Assert.Equal(0, arena.Simulation.Player.Currency);
        Assert.Equal((1, 0, 16L), (Member(arena, Kera).Contract!.DaysServed, Member(arena, Kera).Contract!.UnpaidDays, Member(arena, Kera).Contract!.Purse));

        arena.Simulation.Advance(day);   // the next midnight, away: nothing to pay with - the day's grace
        Assert.Equal((0L, 1), (wages[^1].Paid, wages[^1].UnpaidDays));
        Assert.Single(arena.Simulation.Companions);

        var log = arena.Simulation.Advance(day);   // and the one after: the contract lapses, and she goes home
        Assert.Equal(new PartyMemberLeft(Kera, "lapsed", Midnight(setup, 3)), Assert.Single(left));
        Assert.Empty(arena.Simulation.Companions);
        // Home is her routine's (WP12): at midnight she is asleep there, not at the anvil the region places her at.
        var bed = setup.NpcLife.RoutineOf(Kera)!.Anchors["home"];
        Assert.Equal("sleep", arena.Simulation.Lives.Single(l => l.Key == Kera).Activity);
        Assert.Equal((bed.XMm, bed.ZMm), (arena.Simulation.Npcs.Single(n => n.Id == Kera).Body.XMm, arena.Simulation.Npcs.Single(n => n.Id == Kera).Body.ZMm));
        Assert.Contains(new AdvanceLine(Midnight(setup, 3), PartyAdvanceKinds.Left, $"{Kera}|lapsed"), log.Lines);
    }

    [Fact]
    public void AContract_EndsWhenItsDaysAreServed_PaidForEachOfThem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 100);
        var left = arena.Record<PartyMemberLeft>();
        Assert.Null(Hire(arena, Kera, HirelingRole.Guard, 2));

        var log = arena.Simulation.Advance(Midnight(session.Setup, 2));

        Assert.Equal(new PartyMemberLeft(Kera, "ended", Midnight(session.Setup, 2)), Assert.Single(left));
        Assert.Equal(84, arena.Simulation.Player.Currency);   // two days, two wages: one at the hire, one at the first midnight
        Assert.Contains(new AdvanceLine(Midnight(session.Setup, 2), PartyAdvanceKinds.WagesPaid, Kera, 8), log.Lines);
        Assert.Empty(arena.Simulation.Companions);
    }

    [Fact]
    public void Dismissed_TheyGoHome_AndTheWageAlreadyPaidIsNotRefunded()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 50);
        var left = arena.Record<PartyMemberLeft>();
        Assert.Null(Hire(arena, Kera, HirelingRole.Guard, 5));
        arena.Tick(20);

        Assert.Null(arena.Submit(new DismissCommand(arena.Player, Kera)));

        Assert.Equal(new PartyMemberLeft(Kera, "dismissed", arena.Simulation.WorldTick), Assert.Single(left));
        Assert.Equal(42, arena.Simulation.Player.Currency);
        Assert.Empty(arena.Simulation.Companions);
        Assert.Equal(Home(session.Setup, Kera), (arena.Simulation.Npcs.Single(n => n.Id == Kera).Body.XMm, arena.Simulation.Npcs.Single(n => n.Id == Kera).Body.ZMm));
        Assert.Equal("Kera Voss is not with you", arena.Submit(new DismissCommand(arena.Player, Kera)));
    }

    /// <summary>DN9: a hireling downed with no hand in time dies - gone from the world, and still gone when the world starts again.</summary>
    [Fact]
    public void AHirelingWhoFalls_Dies_AndStaysDead()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var down = Member(Kera, 61, 141) with { Condition = CompanionCondition.Downed, Health = 0, DownedTick = 0 };
        var arena = At(session, 50, r => r.WithCompanions(new[] { down }));
        var died = arena.Record<HirelingDied>();
        int window = session.Setup.Social.Companions!.ReviveWindowTicks;
        // The same fall in a world whose registry the test holds: the dead hireling's instance goes with her.
        var registry = new Registry();
        var watched = Simulation.Start(arena.Simulation.Setup, arena.Simulation.CaptureRecord(), new WorldDelta(session.Generator, 42, registry), 0, new EventBus());
        var instance = watched.Npcs.Single(n => n.Id == Kera).InstanceId;
        Assert.True(registry.Exists(instance));

        arena.Tick(window + 1);
        for (int i = 0; i <= window; i++)
        {
            watched.DrainCommands();
            watched.Step();
        }
        Assert.DoesNotContain(watched.Npcs, n => n.Id == Kera);
        Assert.False(registry.Exists(instance));

        Assert.Equal(new HirelingDied(Kera, window), Assert.Single(died));
        Assert.Empty(arena.Simulation.Companions);
        Assert.DoesNotContain(arena.Simulation.Npcs, n => n.Id == Kera);
        var record = arena.Simulation.CaptureRecord();
        Assert.Equal(new[] { Kera }, record.Party.Fallen);

        var again = Simulation.Start(arena.Simulation.Setup, record, new WorldDelta(session.Generator, 42, new Registry()), arena.Simulation.WorldTick, new EventBus());
        Assert.DoesNotContain(again.Npcs, n => n.Id == Kera);
        Assert.Equal($"there is no one called {Kera} here", again.HireTerms(Kera, HirelingRole.Guard, 3).Refusal);
    }

    [Fact]
    public void APorter_AddsToWhatTheCharacterCarries_WhileFollowingOnTheirFeet()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 50);
        long alone = arena.Simulation.Player.CarryLimitGrams;
        Assert.Null(Hire(arena, Kera, HirelingRole.Porter, 3));
        Assert.Equal(CompanionStance.Passive, Member(arena, Kera).Stance);
        Assert.Equal(46, arena.Simulation.Player.Currency);   // a porter is 4 a day

        Assert.Equal(alone + 30_000, arena.Simulation.Player.CarryLimitGrams);
        Assert.Null(arena.Submit(new OrderCompanionCommand(arena.Player, Kera, CompanionOrder.Wait)));
        Assert.Equal(alone, arena.Simulation.Player.CarryLimitGrams);
        Assert.Null(arena.Submit(new OrderCompanionCommand(arena.Player, Kera, CompanionOrder.Follow)));
        Assert.Equal(alone + 30_000, arena.Simulation.Player.CarryLimitGrams);
    }

    /// <summary>A gatherer set to a node works it each day, the character away or not, and what they take comes to the character.</summary>
    [Fact]
    public void AGatherer_WorksTheirNodeEachDay_AndDeliversToTheCharacter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 100);
        var delivered = arena.Record<HirelingDelivered>();
        var stand = arena.Simulation.Nodes.Where(n => n.NodeDefId == AshStand).OrderBy(n => n.Key, StringComparer.Ordinal).First();
        var node = session.Setup.Crafting.Nodes[AshStand];
        Assert.Null(Hire(arena, Kera, HirelingRole.Gatherer, 10));
        Assert.Equal("a gatherer works a node, not a recipe", arena.Submit(new AssignWorkCommand(arena.Player, Kera, stand.Key, BilletRecipe)));
        Assert.Equal("there is no node 'node.nowhere'", arena.Submit(new AssignWorkCommand(arena.Player, Kera, "node.nowhere")));

        Assert.Null(arena.Submit(new AssignWorkCommand(arena.Player, Kera, stand.Key)));

        var her = Member(arena, Kera);
        Assert.Equal((CompanionOrder.Wait, stand.Key), (her.Order, her.Contract!.WorkSite));
        Assert.InRange(Math.Sqrt(Math.Pow(her.Body.XMm - stand.XMm, 2) + Math.Pow(her.Body.ZMm - stand.ZMm, 2)), 0, 3_000);
        Assert.Equal(0, Carried(arena.Simulation, node.ItemId));

        arena.Simulation.Advance(Midnight(session.Setup, 3));

        Assert.Equal(3, delivered.Count);
        Assert.All(delivered, d => Assert.InRange(d.Count, 3 * node.YieldMin, 3 * node.YieldMax));   // config.party: three harvests' worth a day
        Assert.Equal(delivered.Sum(d => d.Count), Carried(arena.Simulation, node.ItemId) + arena.Simulation.WorldItems.Where(i => i.DefId == node.ItemId).Sum(i => i.Count));

        // Told to follow, she leaves the work.
        Assert.Null(arena.Submit(new OrderCompanionCommand(arena.Player, Kera, CompanionOrder.Follow)));
        Assert.Null(Member(arena, Kera).Contract!.WorkSite);
    }

    /// <summary>A crafter set to a station works a recipe the character showed them, from the materials they were handed, a few times a day.</summary>
    /// <summary>
    /// A hireling keeps working, and is paid, while the character is away (§2.12, §2.15): through the abstract advance a login runs, each
    /// midnight brings in the gatherer's day of work and pays the next day's wage from the purse.
    /// </summary>
    [Fact]
    public void AHireling_KeepsWorking_AndIsPaid_WhileTheCharacterIsAway()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 100);
        var stand = arena.Simulation.Nodes.Where(n => n.NodeDefId == AshStand).OrderBy(n => n.Key, StringComparer.Ordinal).First();
        Assert.Null(Hire(arena, Kera, HirelingRole.Gatherer, 10));
        Assert.Null(arena.Submit(new AssignWorkCommand(arena.Player, Kera, stand.Key)));
        arena.Tick();
        long wage = Member(arena, Kera).Contract!.DailyWage;
        long coin = arena.Simulation.Player.Currency;

        var away = arena.Simulation.Advance(Midnight(session.Setup, 3) - arena.Simulation.WorldTick);

        var contract = Member(arena, Kera).Contract!;
        Assert.Equal((3, 0, 4 * wage), (contract.DaysServed, contract.UnpaidDays, contract.Purse));   // the first day at hire, then three midnights
        Assert.Equal(coin - 3 * wage, arena.Simulation.Player.Currency);
        Assert.Equal(3 * wage, away.Lines.Single(l => l.Kind == PartyAdvanceKinds.WagesPaid && l.Ref == Kera).Amount);
        Assert.Equal(3, away.Lines.Count(l => l.Kind == PartyAdvanceKinds.Delivered && l.Ref.StartsWith(Kera, StringComparison.Ordinal)));
        Assert.True(Carried(arena.Simulation, session.Setup.Crafting.Nodes[AshStand].ItemId) > 0);
    }

    [Fact]
    public void ACrafter_WorksTheirRecipe_FromWhatTheyWereHanded()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 100, Taught, 0, Arena.Stack(Ore, 5));
        Assert.Null(Hire(arena, Kera, HirelingRole.Crafter, 10));
        Assert.Equal("Renn Vale is not a crafter in your hire", arena.Submit(new SupplyHirelingCommand(arena.Player, Renn, Ore, 1)));
        Assert.Null(arena.Submit(new SupplyHirelingCommand(arena.Player, Kera, Ore, 5)));
        Assert.Equal(0, Carried(arena.Simulation, Ore));
        Assert.Equal(5, Member(arena, Kera).Contract!.Supply[Ore]);

        Assert.Equal($"{BilletRecipe} is not worked at a anvil", arena.Submit(new AssignWorkCommand(arena.Player, Kera, "station.forge_anvil", BilletRecipe)));
        Assert.Equal("a crafter needs a recipe to work", arena.Submit(new AssignWorkCommand(arena.Player, Kera, "station.forge_hearth")));
        Assert.Null(arena.Submit(new AssignWorkCommand(arena.Player, Kera, "station.forge_hearth", BilletRecipe)));

        arena.Simulation.Advance(Midnight(session.Setup, 1));
        Assert.Equal(2, Carried(arena.Simulation, Billet));   // config.party: two a day
        Assert.Equal(3, Member(arena, Kera).Contract!.Supply[Ore]);

        arena.Simulation.Advance(Midnight(session.Setup, 3) - arena.Simulation.WorldTick);
        Assert.Equal(5, Carried(arena.Simulation, Billet));
        Assert.Empty(Member(arena, Kera).Contract!.Supply);
    }

    [Fact]
    public void Tactics_AreSetPerMember_AndOnlyForThoseWithTheCharacter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, 50);
        var changed = arena.Record<TacticsChanged>();
        Assert.Null(Hire(arena, Kera, HirelingRole.Guard, 3));
        Assert.Equal(PartyTactics.Default, Member(arena, Kera).Tactics);

        var tactics = new PartyTactics(KeepToLeash: false, ProtectInjured: true, HoldHealing: true);
        Assert.Null(arena.Submit(new SetTacticsCommand(arena.Player, Kera, tactics)));
        Assert.Null(arena.Submit(new SetTacticsCommand(arena.Player, Kera, tactics)));

        Assert.Equal(tactics, Member(arena, Kera).Tactics);
        Assert.Equal(tactics, Assert.Single(changed).Tactics);
        Assert.Equal(tactics, arena.Simulation.CaptureRecord().Companions.Single().Tactics);
        Assert.Equal("Renn Vale is not with you", arena.Submit(new SetTacticsCommand(arena.Player, Renn, tactics)));
    }

    // ── T-22: the party while the character is away ─────────────────────────

    /// <summary>
    /// A party with something due in every part of it: a gatherer at the ash stand on a wage the purse runs out of, a guard downed who
    /// will die, and Tavar downed, to be gravely wounded and to recover.
    /// </summary>
    private static Arena Away(GameSession session)
    {
        var stand = session.Setup.Layout.Nodes.First(n => n.Name == "ash_stand");
        var cell = CellKey.OfWorld(stand.XMm / 1000.0, stand.ZMm / 1000.0);
        var gatherer = Member(Kera, 179, 137, role: HirelingRole.Gatherer, days: 20, wage: 5) with { Order = CompanionOrder.Wait };
        gatherer = gatherer with { Contract = gatherer.Contract! with { WorkSite = Keys.NodeKey(cell, stand.Name, 0) } };
        var guard = Member(Renn, 61, 141) with { Condition = CompanionCondition.Downed, Health = 0, DownedTick = 0 };
        var tavar = Member(Tavar, 62, 142, PartyMembership.Companion) with { Condition = CompanionCondition.Downed, Health = 0, DownedTick = 300 };
        return At(session, 30, r => r.WithCompanions(new[] { gatherer, guard, tavar }), 0);
    }

    /// <summary>Everything of the party and what it did that the character could see; item instance IDs aside, which every grant makes afresh.</summary>
    private static string Seen(Simulation simulation)
    {
        var r = simulation.CaptureRecord();
        return string.Join(" | ", simulation.WorldTick, r.Currency,
            string.Join(";", r.Inventory.GroupBy(e => (e.DefId, e.Quality)).OrderBy(g => g.Key.DefId, StringComparer.Ordinal).Select(g => $"{g.Key.DefId}:{g.Key.Quality}:{g.Sum(e => e.Count)}")),
            string.Join(";", simulation.WorldItems.GroupBy(i => i.DefId).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}:{g.Sum(i => i.Count)}")),
            string.Join(";", r.Companions.Select(c => $"{c.NpcId}:{c.Membership}:{c.Order}:{c.Condition}:{c.Health}:{c.XMm},{c.ZMm}:{c.Contract?.DaysServed}:{c.Contract?.UnpaidDays}:{c.Contract?.Purse}:{string.Join(",", c.Contract?.Supply.Select(s => $"{s.Key}x{s.Value}") ?? Array.Empty<string>())}")),
            string.Join(";", r.Party.Recovering.Select(x => $"{x.NpcId}:{x.UntilTick}")), string.Join(";", r.Party.Fallen),
            string.Join(";", simulation.Npcs.Select(n => $"{n.Id}:{n.Body.XMm},{n.Body.ZMm}")));
    }

    [Fact]
    public void T22_TheParty_TwelveDaysAtOnce_IsTheSameAsDayByDay_AndAsAnyOtherCut()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        long day = session.Setup.Clock.TicksPerDay, total = 12 * day;
        Arena Fresh() => Away(session);

        var once = Fresh();
        var daily = Fresh();
        var cut = Fresh();
        Assert.Equal(Seen(once.Simulation), Seen(daily.Simulation));

        var log = once.Simulation.Advance(total);
        for (int i = 0; i < 12; i++)
            daily.Simulation.Advance(day);
        foreach (long piece in new[] { 1L, 1_199, day - 1_200, 3, 2 * day + 17, day / 2, total - (1 + 1_199 + (day - 1_200) + 3 + 2 * day + 17 + day / 2) })
            cut.Simulation.Advance(piece);

        Assert.Equal(Seen(once.Simulation), Seen(daily.Simulation));
        Assert.Equal(Seen(once.Simulation), Seen(cut.Simulation));

        // And it did what was due: the guard died, Tavar was wounded and is well again, the purse ran dry and the gatherer's contract lapsed.
        var record = once.Simulation.CaptureRecord();
        Assert.Equal(new[] { Renn }, record.Party.Fallen);
        Assert.Empty(record.Party.Recovering);
        Assert.Contains(log.Lines, l => l.Kind == PartyAdvanceKinds.Wounded && l.Ref == Tavar);
        Assert.Contains(log.Lines, l => l.Kind == PartyAdvanceKinds.Recovered && l.Ref == Tavar);
        Assert.DoesNotContain(record.Companions, c => c.NpcId == Kera);
        Assert.Contains(log.Lines, l => l.Kind == PartyAdvanceKinds.Died && l.Ref == Renn);
        Assert.Contains(log.Lines, l => l.Kind == PartyAdvanceKinds.Left && l.Ref.StartsWith(Kera, StringComparison.Ordinal));
        Assert.True(Carried(once.Simulation, session.Setup.Crafting.Nodes[AshStand].ItemId) > 0);
    }
}
