using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The four side quests of the hollow and the wold, each played end to end from its giver's own lines: Sel's two accounts of one night,
/// Tavar's unremembered debt, Berrin's missing page and Tam's fox. Every step uses content that already stands - the people who live in
/// both villages, the lamp oil Kera sells, the fox at the granary wall - so nothing in the world is placed for a test. The observer moves
/// between scenes by the save fixture <see cref="ActOneStoryTests"/> uses, which carries the character's quests with it and leaves the
/// living world exactly as it found it.
/// </summary>
public class SideQuestTests
{
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Berrin = "npc.cairnwold.berrin_garth";
    private const string Tam = "npc.cairnwold.tam_crowden";

    private const string CountingTonight = "quest.ashen_hollow.counting_tonight";
    private const string FairPrice = "quest.ashen_hollow.a_fair_price_for_a_night";
    private const string APageOfWinter = "quest.cairnwold.a_page_of_winter";
    private const string OneHenShort = "quest.cairnwold.one_hen_short";

    private const string Oil = "item.material.lamp_oil";
    private const string Fox = "creature.beast.red_fox";
    private const string Roast = "item.consumable.roast_meat";
    private const string Spear = "item.weapon.march_spear";

    private static readonly (double X, double Z) AtTam = (941, 679.8);

    /// <summary>
    /// One character, one world, and the observer put down wherever the next conversation is. The fixture saves and reloads the same
    /// living world with the character moved, so time, routines and quest state cross between scenes and nothing is teleported in game.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        private readonly TempProfile profile = new();
        public GameSession Session { get; }
        public Arena Arena { get; private set; }
        public Simulation Sim => Arena.Simulation;

        public Scene(params InventoryEntry[] extra)
        {
            Session = Harness.Boot(profile);
            Arena = Arena.OpenCreatures(Session, Session.Setup, (30, 158), 90, Array.Empty<(string, double, double, string)>(),
                r => r.WithInventory(r.Inventory.Concat(extra)));
        }

        public void At(long x, long z)
        {
            if (Sim.Conversation is not null)
                SayCommand(new LeaveCommand(Arena.Player));
            var r = Sim.CaptureRecord();
            var moved = new PlayerRecord(r.Id, r.Name, x, Sim.Setup.Layout.Space.HeightAtMm(x, z), z,
                r.AppearanceSeed, r.Inventory, r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency,
                r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions)
            {
                Posture = r.Posture, Waystones = r.Waystones, ReturnPoint = r.ReturnPoint, RoadsWalked = r.RoadsWalked,
                Deeds = r.Deeds, Identity = r.Identity, Factions = r.Factions, Party = r.Party,
                QuickBar = r.QuickBar, QuickBarCleared = r.QuickBarCleared, PartySheets = r.PartySheets, Attack = r.Attack,
            };
            var store = new SaveStore(profile.Root);
            store.Save(SaveSlots.Manual("scene"), SaveDocuments.Capture(Sim.World, moved, Session.Content, Sim.WorldTick, 0));
            var setup = Sim.Setup;
            Arena = Arena.Resume(setup, store.Load(SaveSlots.Manual("scene"), new LoadContext(Session.Generator, Session.Content, new Registry())));
            Arena.Tick();
        }

        /// <summary>Stand beside someone and open their conversation past the greeting they say once.</summary>
        public void Talk(string npc)
        {
            var body = Sim.Npcs.Single(n => n.Id == npc).Body;
            if (npc == Renn) At(body.XMm, body.ZMm + 1_200); // the aisle, clear of the supper table
            else At(body.XMm + 700, body.ZMm);
            var refused = Arena.Submit(new TalkCommand(Arena.Player, npc));
            Assert.True(refused is null, $"{refused}: player={Sim.Player.Body}; speaker={Sim.Npcs.Single(n => n.Id == npc).Body}");
            if (Sim.Conversation!.NodeId == "greet")
            {
                SayCommand(new LeaveCommand(Arena.Player));
                SayCommand(new TalkCommand(Arena.Player, npc));
            }
        }

        public void SayCommand(GameCommand c) => Assert.Null(Arena.Submit(c));

        /// <summary>Put the observer at a place the content names, for the steps that are a walk rather than a conversation.</summary>
        public void Place(string location)
        {
            var at = Sim.Setup.Layout.Locations.Single(l => l.Id == location);
            At(at.XMm, at.ZMm);
        }

        public void Say(string reply)
        {
            SayCommand(new ChooseCommand(Arena.Player, reply));
            Arena.Tick();
        }

        public QuestStatus Status(string quest) => Sim.Quests.Single(q => q.Id == quest).Status;

        public int Carried(string defId) => Sim.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

        public void Dispose() => profile.Dispose();
    }

    private static Func<PlayerRecord, PlayerRecord> Armed(string weapon) => r =>
    {
        var inventory = r.Inventory.Any(e => e.DefId == weapon) ? r.Inventory : r.Inventory.Add(Arena.Stack(weapon, 1));
        var item = inventory.Single(e => e.DefId == weapon).ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression,
            r.FacingMdeg, r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, r.Currency, r.Effects);
    };

    [Fact]
    public void CountingTonight_PlaysEndToEnd_FromTwoAccountsToAMargin()
    {
        using var s = new Scene();

        s.Talk(Sel);
        s.Say("beds_offer");
        s.Say("take");
        Assert.Equal(QuestStatus.Active, s.Status(CountingTonight));

        s.Talk(Renn);
        s.Say("beds_count_ask");
        s.Say("back");

        s.Talk(Kera);
        s.Say("crew_ask");
        s.Say("back");

        s.Talk(Sel);
        s.Say("beds_back");
        s.Say("back");

        Assert.Equal(QuestStatus.Completed, s.Status(CountingTonight));
    }

    [Fact]
    public void FairPriceForANight_PlaysEndToEnd_WithTheOilLeftAtTheFold()
    {
        using var s = new Scene(Arena.Stack(Oil, 1));

        // Renn keeps the house and Tavar stops a breath short of the fold, so the debt is settled by walking rather than by talking:
        // the flask goes out to the Foldscar, and the objective is the walk and what is carried on it.
        s.Talk(Renn);
        s.Say("debt");
        s.Say("take");
        Assert.Equal(QuestStatus.Active, s.Status(FairPrice));
        Assert.Equal(1, s.Carried(Oil));

        s.Place("location.foldscar");

        s.Talk(Renn);
        s.Say("debt_back");
        s.Say("back");

        Assert.Equal(QuestStatus.Completed, s.Status(FairPrice));
    }

    [Fact]
    public void APageOfWinter_PlaysEndToEnd_WithTamsCountPuttingTheWinterBack()
    {
        using var s = new Scene();

        s.Talk(Berrin);
        s.Say("page");
        s.Say("take");
        Assert.Equal(QuestStatus.Active, s.Status(APageOfWinter));

        s.Talk(Tam);
        s.Say("the_page");
        s.Say("back");

        s.Talk(Berrin);
        s.Say("page_back");
        s.Say("back");

        Assert.Equal(QuestStatus.Completed, s.Status(APageOfWinter));
    }

    [Fact]
    public void OneHenShort_PlaysEndToEnd_WithTheFoxBehindTheGranary()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A fox at the granary wall is the region's own doing (spawn.cairnwold_fox); the test only puts it close enough that the fight
        // stays in the yard, so the walk back to Tam is a step and not a search.
        var arena = Arena.OpenCreatures(session, session.Setup, AtTam, 0,
            new[] { (Fox, AtTam.X + 2.5, AtTam.Z, "pack_hunter") }, Armed(Spear), origin: Harness.Region);

        void TalkTam()
        {
            string? refused = arena.Submit(new TalkCommand(arena.Player, Tam));
            Assert.True(refused is null, refused);
            if (arena.Simulation.Conversation!.NodeId == "greet")
            {
                Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
                Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tam)));
            }
        }

        TalkTam();
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "hen")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "take")));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));
        arena.Tick();
        Assert.Equal(QuestStatus.Active, arena.Simulation.Quests.Single(q => q.Id == OneHenShort).Status);

        var target = arena.Simulation.Creatures.First(c => c.Alive);
        arena.Fight(target, 900);
        Assert.False(arena.Simulation.Creatures.Single(c => c.Key == target.Key).Alive, "the fox is still up");

        TalkTam();
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "hen_back")));
        arena.Tick();

        Assert.Equal(QuestStatus.Completed, arena.Simulation.Quests.Single(q => q.Id == OneHenShort).Status);
        Assert.Equal(2, arena.Simulation.Player.Inventory.Where(e => e.DefId == Roast).Sum(e => e.Count));
    }
}
