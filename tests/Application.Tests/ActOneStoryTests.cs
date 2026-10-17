using UNNAMED.Domain;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>Content integration at schema 26: real commands, live routines, persistent choices. No game-system changes.</summary>
public class ActOneStoryTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Berrin = "npc.cairnwold.berrin_garth";

    // A save fixture moves the test observer between scenes; NPC bodies/time/quests remain those of the living world.
    // This does not add a player teleport route or disable NPC life in the game.
    private sealed class Scene : IDisposable
    {
        private readonly TempProfile profile = new();
        public GameSession Session { get; }
        public Arena Arena { get; private set; }
        public Simulation Sim => Arena.Simulation;
        public Scene(long away = 0, params InventoryEntry[] extra)
        {
            Session = Harness.Boot(profile);
            Arena = Arena.OpenCreatures(Session, Session.Setup, (30, 158), 90, Array.Empty<(string, double, double, string)>(),
                r => r.WithInventory(r.Inventory.Concat(extra)));
            if (away > 0) Sim.Advance(away);
        }
        public void At(long x, long z)
        {
            if (Sim.Conversation is not null) SayCommand(new LeaveCommand(Arena.Player));
            var r = Sim.CaptureRecord();
            var moved = new PlayerRecord(r.Id, r.Name, x, Sim.Setup.Layout.Space.Terrain.HeightAtMm(x, z), z,
                r.AppearanceSeed, r.Inventory, r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency,
                r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions)
            {
                Posture = r.Posture, Waystones = r.Waystones, ReturnPoint = r.ReturnPoint, RoadsWalked = r.RoadsWalked,
                Deeds = r.Deeds, Identity = r.Identity, Factions = r.Factions, Party = r.Party,
                QuickBar = r.QuickBar, QuickBarCleared = r.QuickBarCleared, PartySheets = r.PartySheets,
            };
            var store = new SaveStore(profile.Root);
            store.Save(SaveSlots.Manual("scene"), SaveDocuments.Capture(Sim.World, moved, Session.Content, Sim.WorldTick, 0));
            var setup = Sim.Setup;
            Arena = Arena.Resume(setup, store.Load(SaveSlots.Manual("scene"), new LoadContext(Session.Generator, Session.Content, new Registry())));
            Arena.Tick();
        }
        public void Place(string location)
        {
            var p = Sim.Setup.Layout.Locations.Single(l => l.Id == location);
            At(p.XMm, p.ZMm);
        }
        public void Talk(string npc)
        {
            var body = Sim.Npcs.Single(n => n.Id == npc).Body;
            At(body.XMm + 700, body.ZMm);
            SayCommand(new TalkCommand(Arena.Player, npc));
            if (Sim.Conversation!.NodeId == "greet")
            {
                SayCommand(new LeaveCommand(Arena.Player));
                SayCommand(new TalkCommand(Arena.Player, npc));
            }
        }
        public void SayCommand(GameCommand c) => Assert.Null(Arena.Submit(c));
        public void Say(string reply)
        {
            SayCommand(new ChooseCommand(Arena.Player, reply));
            Arena.Tick();
        }
        public QuestView Quest(string id) => Sim.Quests.Single(q => q.Id == id);
        public void Dispose() => profile.Dispose();
    }

    [Fact]
    public void FourRelics_AreReachableStartOnlyLoot_AndStayTakenAcrossSaveLoad()
    {
        using var s = new Scene();
        var opening = s.Sim.Setup.Layout.Opening!;
        var sites = s.Sim.Setup.Layout.Containers.Where(c => c.Key.StartsWith("container.story.") && !c.Key.EndsWith("lantern_ledger")).ToArray();
        Assert.Equal(4, sites.Length);
        foreach (var site in sites)
        {
            Assert.InRange(Math.Sqrt(Math.Pow(site.XMm - opening.XMm, 2) + Math.Pow(site.ZMm - opening.ZMm, 2)), 0, 150_000);
            Assert.True(Kinematics.IsClear(site.XMm, site.ZMm, s.Sim.Setup.Movement.BodyRadiusMm,
                s.Sim.Setup.Layout.Space, Array.Empty<Blocker>()), $"pickup obstructed: {site.Key}");
            s.At(site.XMm, site.ZMm);
            s.SayCommand(new TakeAllCommand(s.Arena.Player, site.Key));
            Assert.Empty(s.Sim.Containers.Single(c => c.Site.Key == site.Key).Items);
        }
        s.Place("location.outpost");
        Assert.All(sites, site => Assert.Empty(s.Sim.Containers.Single(c => c.Site.Key == site.Key).Items));
        Assert.Equal(4, s.Sim.Player.Inventory.Count(i => sites.Any(c => "item.story." + c.Key.Split('.').Last() == i.DefId)));
        foreach (var slug in new[] { "stopped_watch", "brass_key", "glass_marble", "unreadable_page" })
            Assert.Equal(0, s.Sim.Setup.Items.Catalog.Get("item.story." + slug).ValueBase);
    }

    [Theory]
    [InlineData("carry_wayhold", 1)]
    [InlineData("carry_survey", 2)]
    [InlineData("carry_cairnwold", 3)]
    public void Carrier_RequiresFieldCircuit_CanBeDeferred_ThenPersistsOnceWithMobileRenn(string reply, long expected)
    {
        using var s = new Scene(5 * 2_400); // 12:30: away from Renn's normal work site
        var work = s.Sim.Setup.Layout.Npcs.Single(n => n.NpcId == Renn);
        var body = s.Sim.Npcs.Single(n => n.Id == Renn).Body;
        Assert.NotEqual((work.XMm, work.ZMm), (body.XMm, body.ZMm));
        s.Talk(Renn);
        Assert.DoesNotContain(s.Sim.Conversation!.Replies, r => r.Id == "story_accounts");
        s.Say("story_begin_accounts");
        s.Place("location.road.lantern_shrine");
        s.Talk(Renn);
        s.Say("story_accounts");
        s.Say("defer");
        Assert.Equal(QuestStatus.Active, s.Quest("quest.main.three_accounts").Status);
        s.Say("story_accounts");
        s.Say(reply);
        s.Say("back");
        Assert.Equal(QuestStatus.Completed, s.Quest("quest.main.three_accounts").Status);
        var outpost = s.Sim.Setup.Layout.Locations.Single(l => l.Id == "location.outpost");
        long Read() => s.Sim.World.GetFlag(CellKey.OfWorld(outpost.XMm / 1000.0, outpost.ZMm / 1000.0), "world.main.carrier");
        Assert.Equal(expected, Read());
        Assert.DoesNotContain(s.Sim.Conversation!.Replies, r => r.Id is "story_accounts" or "story_begin_accounts");
        s.At(s.Sim.Player.Body.XMm, s.Sim.Player.Body.ZMm);
        Assert.Equal(expected, Read());
        Assert.DoesNotContain(s.Sim.Player.Inventory, i => i.DefId.StartsWith("item.story.letter_"));
    }

    [Fact]
    public void NoteAndLedger_UseExistingSpeakersWithoutConsumingOrDuplicatingItems()
    {
        using var s = new Scene();
        s.Talk(Renn);
        s.Say("story_note");
        Assert.Contains(s.Sim.Conversation!.Replies, r => r.Id == "remember");
        Assert.Contains(s.Sim.Conversation.Replies, r => r.Id == "uncertain");
        s.Place("location.cairnwold.lodge");
        Assert.Equal(QuestStatus.Completed, s.Quest("quest.main.folded_note").Status);
        s.Talk(Berrin);
        s.Say("story_note");
        Assert.Single(s.Sim.Player.Inventory.Where(i => i.DefId == "item.story.folded_note"));
        var cache = s.Sim.Setup.Layout.Containers.Single(c => c.Key == "container.story.lantern_ledger");
        s.At(cache.XMm, cache.ZMm);
        s.SayCommand(new TakeAllCommand(s.Arena.Player, cache.Key));
        s.Talk(Kera);
        s.Say("story_ledger");
        Assert.Equal(QuestStatus.Completed, s.Quest("quest.main.closed_seam").Status);
        Assert.Equal(QuestStatus.Completed, s.Quest("quest.story.lantern_ledger").Status);
        Assert.Single(s.Sim.Player.Inventory.Where(i => i.DefId == "item.story.lantern_ledger"));
        Assert.Empty(s.Sim.Containers.Single(c => c.Site.Key == cache.Key).Items);
        s.Talk(Sel);
        s.Say("story_order");
        Assert.DoesNotContain(s.Sim.Setup.Quests.Quests.Keys, q => q is "quest.main.readings_resumed" or "quest.story.lantern_codex" or "quest.story.lantern_foot");
    }

    [Fact]
    public void RoadEast_UsesTheRealRiteAndStone_AndNeverBindsAutomatically()
    {
        using var s = new Scene();
        s.Talk(Berrin);
        s.Say("rim_stone");
        var before = s.Sim.CaptureRecord().ReturnPoint;
        var stone = s.Sim.Setup.Layout.FindWaystone("waystone.rim")!;
        var (x, z) = Footprints.Center(stone.Body);
        s.At(x + 500, z);
        s.SayCommand(new TouchStoneCommand(s.Arena.Player, "waystone.rim"));
        s.Arena.Tick();
        Assert.Equal(QuestStatus.Completed, s.Quest("quest.main.road_east").Status);
        Assert.True(s.Sim.Waystones.Single(w => w.Site.Key == "waystone.rim").Known);
        Assert.Equal(before, s.Sim.CaptureRecord().ReturnPoint);
    }

    [Fact]
    public void WrongSide_ObservesTheExistingRescue_AndEarlyTrustCannotBeFarmed()
    {
        using var s = new Scene();
        s.Talk(Sel);
        s.Say("story_grazers");
        Assert.Equal(QuestStatus.Active, s.Quest("quest.main.wrong_side").Status);
        foreach (string key in new[] { "switch.stone_north", "switch.stone_southwest", "switch.stone_southeast", "switch.foldscar_heart" })
        {
            var site = s.Sim.Setup.Layout.Switches.Single(v => v.Key == key);
            var (x, z) = Footprints.Center(site.Body);
            s.At(x, z);
            s.SayCommand(new InteractCommand(s.Arena.Player, key));
            s.Arena.Tick();
        }
        const string tavar = "npc.ashen_hollow.tavar_orr";
        s.Talk(tavar);
        s.Say("story_afternoon");
        s.Say("first");
        long Trust() => s.Sim.CaptureRecord().Relationships.Single(r => r.NpcId == tavar && r.Dimension == "trust").Value;
        long first = Trust();
        Assert.True(first >= 5);
        Assert.Equal(QuestStatus.Completed, s.Quest("quest.main.wrong_side").Status);
        s.Say("back"); s.Say("story_afternoon");
        Assert.DoesNotContain(s.Sim.Conversation!.Replies, r => r.Id == "first");
        Assert.Equal("'first' is not a reply here", s.Arena.Submit(new ChooseCommand(s.Arena.Player, "first")));
        s.Say("later");
        Assert.Equal(first, Trust());
        s.Talk(Renn);   // his departure line cites Tavar's account, so it waits until it has been heard
        Assert.Contains(s.Sim.Conversation!.Replies, r => r.Id == "story_road_east");
    }

    [Fact]
    public void MossLights_ExtendsTheShippedHandIn_WithDaylightAndRoutineSafeRecordVisits()
    {
        using var s = new Scene(0, Arena.Stack("item.material.herb_ashbloom", 3));
        const string mara = "npc.cairnwold.mara_ling";
        s.Talk(mara);
        s.Say("ask"); s.Say("offer"); s.Say("back");
        s.Place("location.cairnwold.the_bog");
        s.Talk(mara);
        s.Say("bog_return"); s.Say("back");
        Assert.Equal(QuestStatus.Completed, s.Quest("quest.cairnwold.what_the_bog_kept").Status);
        s.Say("story_lights");
        s.Place("location.cairnwold.the_bog");
        s.Talk(mara); s.Say("story_waterline");
        s.Place("location.cairnwold.lodge");
        s.Talk(Berrin); s.Say("story_records");
        Assert.Equal(QuestStatus.Completed, s.Quest("quest.main.moss_lights").Status);
    }

    [Fact]
    public void LeakwildNames_RecordThePlayersWordsAtTheOutpost_NotTheWalkingSpeakersCell()
    {
        using var s = new Scene(5 * 2_400);
        foreach (string word in new[] { "robin", "dandelion" })
        {
            s.Talk(Sel); s.Say("story_species_" + word); s.Say("name");
            Assert.Equal(QuestStatus.Completed, s.Quest("quest.story.name_" + word).Status);
            var outpost = s.Sim.Setup.Layout.Locations.Single(l => l.Id == "location.outpost");
            Assert.Equal(1, s.Sim.World.GetFlag(CellKey.OfWorld(outpost.XMm / 1000.0, outpost.ZMm / 1000.0), "world.main.named_" + word));
        }
    }
}
