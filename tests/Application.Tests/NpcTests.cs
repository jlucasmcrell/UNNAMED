using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M4: the waystation's people over the game's own content - three named NPCs standing in the world, conversations as data
/// whose replies follow from world state and whose consequences are commands, what each NPC thinks of the character, the
/// continuity of both across a save, and trade with the smith.
/// </summary>
public class NpcTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Wares = "merchant.ashen_hollow.kera_voss";
    private const string Primer = "item.tome.resonance_primer";
    private const string BilletRecipe = "recipe.smithing.iron_billet";
    private const string SpearRecipe = "recipe.smithing.march_spear";
    private const string Spear = "item.weapon.march_spear";
    private const string Arrows = "item.ammo.arrow_rough";
    private const string Hide = "item.material.wolf_hide";

    // Where the character stands to speak to each (content/regions/ashen_hollow.yaml).
    private static readonly (double X, double Z) AtRenn = (48, 126.2);
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);
    private static readonly (double X, double Z) AtSel = (71, 123);

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change);

    private static PlayerRecord With(PlayerRecord r, long currency = 0, params InventoryEntry[] extra) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg, r.Discoveries,
            r.Equipment, currency, r.Effects, r.Relationships, r.Conversations);

    private static InventoryEntry Stack(string defId, int count, int quality = Quality.Standard) =>
        new(EntityId.NewId(EntityKind.Item), defId, count) { Quality = quality };

    private static string? Say(Arena arena, string reply) => arena.Submit(new ChooseCommand(arena.Player, reply));

    private static ConversationView Now(Arena arena) => arena.Simulation.Conversation ?? throw new InvalidOperationException("no conversation");

    private static string[] Replies(Arena arena) => Now(arena).Replies.Select(r => r.Id).ToArray();

    private static int Regard(Arena arena, string npc, string dimension) =>
        arena.Simulation.CaptureRecord().Relationships.FirstOrDefault(r => r.NpcId == npc && r.Dimension == dimension)?.Value ?? 0;

    private static int Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    // ── the people ──────────────────────────────────────────────────────────

    [Fact]
    public void TheWaystationsPeople_StandWhereTheRegionPutsThem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Tester", seed: 42);

        var npcs = simulation.Npcs.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        // The waystation's three, and Tavar, caught in the Foldscar (M6).
        Assert.Equal(new[] { Kera, Renn, Sel, "npc.ashen_hollow.tavar_orr" }, npcs.Select(n => n.Id));
        Assert.Equal(new[] { "Kera Voss", "Renn Vale", "Sel Arien", "Tavar Orr" }, npcs.Select(n => n.Name));
        Assert.All(npcs, n => Assert.False(n.Talking));
        Assert.Equal((61_600L, 139_600L, 300_000), (npcs[0].Body.XMm, npcs[0].Body.ZMm, npcs[0].Body.FacingMdeg));
        // A named NPC is the same instance in every world: identity is derived, never rolled (D-10).
        var again = session.NewGame("Tester", seed: 7);
        Assert.Equal(npcs.Select(n => n.InstanceId), again.Npcs.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => n.InstanceId));
        Assert.All(npcs, n => Assert.Equal(EntityKind.Npc, n.InstanceId.Kind));
        // They are solid: bodies walk round them.
        Assert.Contains(simulation.DynamicBlockers, b => b.Id == Kera);
    }

    // ── conversations ───────────────────────────────────────────────────────

    [Fact]
    public void AConversation_OpensWithinReach_AndItsRepliesFollowFromWorldState()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var far = At(session, AtKera);
        Assert.Equal("Renn Vale is out of reach", far.Submit(new TalkCommand(far.Player, Renn)));
        Assert.Equal("there is no one called npc.nobody here", far.Submit(new TalkCommand(far.Player, "npc.nobody")));
        Assert.Equal("not in a conversation", Say(far, "leave"));

        var arena = At(session, AtRenn);
        var lines = arena.Record<ConversationLine>();
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        Assert.Equal("greet", Now(arena).NodeId);
        Assert.Equal(new[] { "place", "work", "leave" }, Replies(arena));
        Assert.True(arena.Simulation.Npcs.Single(n => n.Id == Renn).Talking);
        Assert.Equal("'iron' is not a reply here", Say(arena, "iron"));
        Assert.Null(Say(arena, "place"));
        Assert.Null(Say(arena, "back"));
        // The greeting is spent: the conversation stands at its again-node, and carrying nothing, the character is offered
        // neither the iron nor the spear.
        Assert.Equal("again", Now(arena).NodeId);
        Assert.Equal(new[] { "place", "work", "leave" }, Replies(arena));
        Assert.Null(Say(arena, "leave"));
        Assert.Null(arena.Simulation.Conversation);
        Assert.Equal(new[] { "greet", "place", "again" }, lines.Select(l => l.NodeId));

        // Talking again opens past the spent greeting.
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        Assert.Equal("again", Now(arena).NodeId);

        // What the character carries changes what they may say.
        var bearer = At(session, AtRenn, r => With(r, 0, Stack("item.material.iron_ore", 2), Stack(Spear, 1)));
        Assert.Null(bearer.Submit(new TalkCommand(bearer.Player, Renn)));
        Assert.Null(Say(bearer, "leave"));
        Assert.Null(bearer.Submit(new TalkCommand(bearer.Player, Renn)));
        Assert.Equal(new[] { "place", "work", "iron", "spear", "leave" }, Replies(bearer));
        Assert.Null(Say(bearer, "iron"));
        Assert.Equal(5, Regard(bearer, Renn, "respect"));
        Assert.Null(Say(bearer, "back"));
        Assert.DoesNotContain("iron", Replies(bearer));   // said once
    }

    [Fact]
    public void Kera_TeachesTheForge_AsATeacher_AndOpensHerWares()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtKera);
        var learned = arena.Record<TechniqueLearned>();
        var opened = arena.Record<ServiceOpened>();
        var changed = arena.Record<RelationshipChanged>();

        Assert.Equal($"{BilletRecipe} is not known", arena.Submit(new CraftCommand(arena.Player, BilletRecipe)));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Null(Say(arena, "teach"));

        Assert.Equal(new[] { BilletRecipe, SpearRecipe }, learned.Select(l => l.DefinitionId));
        Assert.All(new[] { BilletRecipe, SpearRecipe }, id =>
        {
            var known = arena.Simulation.Player.Progression.Known[id];
            Assert.Equal((LearningSource.Teacher, Kera), (known.Source, known.SourceRef));
        });
        Assert.Equal(new RelationshipChanged(Kera, "trust", 0, 5, "asked_to_learn", arena.Simulation.WorldTick), Assert.Single(changed));
        Assert.Equal("lesson", Now(arena).NodeId);
        Assert.Null(Say(arena, "thanks"));
        Assert.DoesNotContain("teach", Replies(arena));   // taught once

        Assert.Null(Say(arena, "trade"));
        Assert.Equal((Kera, "trade"), (Assert.Single(opened).NpcId, Assert.Single(opened).Service));
        Assert.Null(arena.Simulation.Conversation);   // trade takes over from talk
    }

    [Fact]
    public void ASpearShownToKera_GetsTheWordItsQualityEarns_Once()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        string[] Offered(int quality)
        {
            var arena = At(session, AtKera, r => With(r, 0, Stack(Spear, 1, quality)));
            Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
            Assert.Null(Say(arena, "leave"));
            Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
            return Replies(arena);
        }

        Assert.Contains("show_fine", Offered(Quality.Fine));
        Assert.DoesNotContain("show_spear", Offered(Quality.Fine));
        Assert.Contains("show_spear", Offered(Quality.Crude));
        Assert.DoesNotContain("show_fine", Offered(Quality.Standard));

        var fine = At(session, AtKera, r => With(r, 0, Stack(Spear, 1, Quality.Fine)));
        Assert.Null(fine.Submit(new TalkCommand(fine.Player, Kera)));
        Assert.Null(Say(fine, "leave"));
        Assert.Null(fine.Submit(new TalkCommand(fine.Player, Kera)));
        Assert.Null(Say(fine, "show_fine"));
        Assert.Equal(("praised", 10), (Now(fine).NodeId, Regard(fine, Kera, "respect")));
        Assert.Null(Say(fine, "back"));
        Assert.DoesNotContain("show_fine", Replies(fine));
    }

    [Fact]
    public void Sel_LendsThePrimer_AndAReplyThatCannotGiveIt_IsRefusedWhole()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // A pack with no free stack: the primer cannot be taken, so the reply is refused and nothing else happens either.
        var full = At(session, AtSel, r => With(r, 0, Enumerable.Range(0, session.Setup.Items.Inventory.StackSlots - r.Inventory.Length)
            .Select(_ => Stack("item.trinket.wolf_fang", 1)).ToArray()));
        Assert.Null(full.Submit(new TalkCommand(full.Player, Sel)));
        Assert.Null(Say(full, "books"));
        Assert.StartsWith("no room", Say(full, "take"));
        Assert.Equal(("primer", 0, 0), (Now(full).NodeId, Regard(full, Sel, "trust"), Carried(full, Primer)));

        var arena = At(session, AtSel);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.Null(Say(arena, "books"));
        Assert.Null(Say(arena, "take"));
        Assert.Equal((1, 5), (Carried(arena, Primer), Regard(arena, Sel, "trust")));
        Assert.Equal("primer_given", Now(arena).NodeId);
        Assert.Null(Say(arena, "back"));
        Assert.Equal(new[] { "ruin", "strain", "tavar", "leave" }, Replies(arena));   // the primer is given once; now she will talk of strain
    }

    /// <summary>
    /// The Phase-1 technical audit, H-01: hearing Sel offer the primer is not getting it. Walking away from the offer, being refused it for a
    /// full pack and coming back, or a load while the offer is open, all leave it to be taken - once.
    /// </summary>
    [Fact]
    public void ThePrimer_OfferedAndLeft_IsStillThere_AndIsGivenOnce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // Heard, then walked away from (the leave command is Escape; walking off or dying ends it the same way).
        var left = At(session, AtSel);
        Assert.Null(left.Submit(new TalkCommand(left.Player, Sel)));
        Assert.Null(Say(left, "books"));
        Assert.Equal(new[] { "take" }, Replies(left));
        Assert.Null(left.Submit(new LeaveCommand(left.Player)));
        Assert.Null(left.Submit(new TalkCommand(left.Player, Sel)));
        Assert.Equal("again", Now(left).NodeId);
        Assert.Contains("books", Replies(left));
        Assert.DoesNotContain("strain", Replies(left));   // she speaks of what the primer says once it has been given
        Assert.Null(Say(left, "books"));
        Assert.Null(Say(left, "take"));
        Assert.Null(Say(left, "back"));
        Assert.Equal((1, 5), (Carried(left, Primer), Regard(left, Sel, "trust")));
        Assert.DoesNotContain("books", Replies(left));   // given once

        // Refused for a full pack, left to make room, then taken.
        var full = At(session, AtSel, r => With(r, 0, Enumerable.Range(0, session.Setup.Items.Inventory.StackSlots - r.Inventory.Length)
            .Select(_ => Stack("item.trinket.wolf_fang", 1)).ToArray()));
        Assert.Null(full.Submit(new TalkCommand(full.Player, Sel)));
        Assert.Null(Say(full, "books"));
        Assert.StartsWith("no room", Say(full, "take"));
        Assert.Null(full.Submit(new LeaveCommand(full.Player)));
        var fang = full.Simulation.Player.Inventory.First(e => e.DefId == "item.trinket.wolf_fang");
        Assert.Null(full.Submit(new MoveItemCommand(full.Player, fang.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        Assert.Null(full.Submit(new TalkCommand(full.Player, Sel)));
        Assert.Null(Say(full, "books"));
        Assert.Null(Say(full, "take"));
        Assert.Equal((1, 5), (Carried(full, Primer), Regard(full, Sel, "trust")));

        // Saved while the offer is on screen, and loaded: the open conversation is not saved, and the offer is still to be had.
        var open = At(session, AtSel);
        Assert.Null(open.Submit(new TalkCommand(open.Player, Sel)));
        Assert.Null(Say(open, "books"));
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("offered"), SaveDocuments.Capture(open.Simulation.World, open.Simulation.CaptureRecord(), session.Content,
            open.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(session.Setup, store.Load(SaveSlots.Manual("offered"), new LoadContext(session.Generator, session.Content, new Registry())));
        Assert.Null(loaded.Simulation.Conversation);
        Assert.Null(loaded.Submit(new TalkCommand(loaded.Player, Sel)));
        Assert.Null(Say(loaded, "books"));
        Assert.Null(Say(loaded, "take"));
        Assert.Equal((1, 5), (Carried(loaded, Primer), Regard(loaded, Sel, "trust")));
    }

    /// <summary>A save from before the fix that already carries the primer is not offered a second one.</summary>
    [Fact]
    public void APrimerAlreadyCarried_IsNotOfferedAgain()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtSel, r => With(r, 0, Stack(Primer, 1)).WithSocial(Array.Empty<RelationshipValue>(),
            new[] { new ConversationMemory("dialogue.ashen_hollow.sel_arien", ImmutableArray.Create("again", "greet", "primer")) }));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.Equal("again", Now(arena).NodeId);
        Assert.DoesNotContain("books", Replies(arena));
    }

    [Fact]
    public void WhatWasSaidAndThought_ContinuesAcrossASaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtSel);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Sel)));
        Assert.Null(Say(arena, "books"));
        Assert.Null(Say(arena, "take"));
        Assert.Null(Say(arena, "back"));
        Assert.Null(Say(arena, "leave"));
        arena.Tick(3);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("met"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var again = Arena.Resume(session.Setup, store.Load(SaveSlots.Manual("met"), new LoadContext(session.Generator, session.Content, new Registry())));

        Assert.Equal(arena.Simulation.StateDigest(), again.Simulation.StateDigest());
        var memory = Assert.Single(again.Simulation.CaptureRecord().Conversations);
        Assert.Equal("dialogue.ashen_hollow.sel_arien", memory.DialogueId);
        Assert.Equal(new[] { "again", "greet", "primer", "primer_given" }, memory.Heard.ToArray());
        Assert.Equal(5, Regard(again, Sel, "trust"));
        // Her greeting and her gift stay spent.
        Assert.Null(again.Submit(new TalkCommand(again.Player, Sel)));
        Assert.Equal("again", Now(again).NodeId);
        Assert.DoesNotContain("books", Replies(again));
    }

    [Fact]
    public void AFlagSetInConversation_LivesInTheSpeakersCell_WhereItsConditionReadsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A conversation of this test's own, given to Renn: he opens the longhouse door by setting its flag - which lives in
        // his cell - and only then is thanked. The content needs no such line; the consequence must still work.
        const string Door = "world.hollow.longhouse_door_open";
        var dialogue = new DialogueDefinition("dialogue.test.door", ImmutableArray.Create(Renn), "ask", new[]
        {
            new DialogueNode("ask", "The door?", ImmutableArray.Create(
                new DialogueChoice("open", "Open it for me.", ImmutableArray.Create<DialogueCondition>(new WorldStateCondition(Door, 0, 0)),
                    ImmutableArray.Create<DialogueConsequence>(new SetWorldFlagConsequence(Door, 1)), "ask"),
                new DialogueChoice("thanks", "Thank you.", ImmutableArray.Create<DialogueCondition>(new WorldStateCondition(Door, 1, 1)),
                    ImmutableArray<DialogueConsequence>.Empty, null)), null, false, null),
        }.ToImmutableSortedDictionary(n => n.Id, n => n, StringComparer.Ordinal));
        var social = session.Setup.Social;
        var rules = session.Setup with
        {
            Social = new SocialSetup(social.Npcs.SetItem(Renn, social.Npcs[Renn] with { DialogueId = dialogue.Id }), social.Dialogues.SetItem(dialogue.Id, dialogue)),
        };
        var arena = Arena.OpenCreatures(session, rules, AtRenn, 0, Array.Empty<(string, double, double, string)>());
        bool Open() => arena.Simulation.Doors.Single(d => d.Site.Key == "door.longhouse").Open;

        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        Assert.Equal(new[] { "open" }, Replies(arena));
        Assert.False(Open());
        Assert.Null(Say(arena, "open"));
        Assert.True(Open());
        Assert.Equal(new[] { "thanks" }, Replies(arena));
    }

    [Fact]
    public void WalkingAway_EndsTheConversation_AndTheSpeakerTurnsBack()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtKera);
        var ended = arena.Record<ConversationEnded>();
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        arena.Tick(20);
        var kera = arena.Simulation.Npcs.Single(n => n.Id == Kera);
        var body = arena.Simulation.Player.Body;
        Assert.Equal(Domain.Combat.CombatRules.FacingTowards(kera.Body.XMm, kera.Body.ZMm, body.XMm, body.ZMm), kera.Body.FacingMdeg);   // she faces who talks

        Assert.True(arena.WalkTo(55, 142.2));
        Assert.Equal(Kera, Assert.Single(ended).NpcId);
        Assert.Null(arena.Simulation.Conversation);
        arena.Tick(20);
        Assert.Equal(300_000, arena.Simulation.Npcs.Single(n => n.Id == Kera).Body.FacingMdeg);   // and back to her work
    }

    // ── trade ───────────────────────────────────────────────────────────────

    [Fact]
    public void BuyingAndSelling_MoveCoinAndGoodsExactly_AndRefuseCleanly()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var token = Stack("item.quest.halda_token", 1);
        var arena = At(session, AtKera, r => With(r, 25, Stack(Hide, 2), token));
        var wares = arena.Simulation.Wares(Kera)!;
        var arrows = wares.Wares.First(w => w.ItemId == Arrows);
        var vest = wares.Wares.Single(w => w.ItemId == "item.armor.hide_vest");
        Assert.Equal((60, 1L, 1, 30L), (wares.Wares.Where(w => w.ItemId == Arrows).Sum(w => w.Count), arrows.Price, vest.Count, vest.Price));
        Assert.Null(arena.Simulation.Wares(Renn));   // Renn does not trade

        var bought = arena.Record<ItemBought>();
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, arrows.Ref, 20)));
        Assert.Equal((5L, 20), (arena.Simulation.Player.Currency, Carried(arena, Arrows)));
        Assert.Equal(new ItemBought(Kera, Arrows, 20, 20, arena.Simulation.WorldTick), Assert.Single(bought));
        Assert.Equal(40, arena.Simulation.Wares(Kera)!.Wares.Where(w => w.ItemId == Arrows).Sum(w => w.Count));
        // Touched, her wares got their identities (the slot-promotion rule): a ware is named by what the view says now.
        vest = arena.Simulation.Wares(Kera)!.Wares.Single(w => w.ItemId == "item.armor.hide_vest");
        Assert.Equal("not enough coin: 30 asked, 5 carried", arena.Submit(new BuyCommand(arena.Player, Kera, vest.Ref, 1)));
        Assert.Equal("Kera Voss has no such ware", arena.Submit(new BuyCommand(arena.Player, Kera, "merchant.nothing#00", 1)));

        // She buys a hide at 40% of its value, and it joins her wares; a quest token she will not buy.
        var hide = arena.Simulation.Player.Inventory.Single(e => e.DefId == Hide);
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, hide.ItemId, 2)));
        long each = (long)Math.Floor(session.Setup.Items.Catalog.Get(Hide).ValueBase * 0.4);
        Assert.Equal((5 + 2 * each, 0), (arena.Simulation.Player.Currency, Carried(arena, Hide)));
        Assert.Contains(arena.Simulation.Wares(Kera)!.Wares, w => w.ItemId == Hide && w.Count == 2);
        Assert.Equal("Kera Voss does not buy that", arena.Submit(new SellCommand(arena.Player, Kera, token.ItemId, 1)));
        var meat = Stack("item.material.raw_meat", 1);   // worth 2: 40% of it is nothing
        var hungry = At(session, AtKera, r => With(r, 0, meat));
        Assert.Equal("Kera Voss would give nothing for that", hungry.Submit(new SellCommand(hungry.Player, Kera, meat.ItemId, 1)));

        // Her wares are no chest: nothing moves into or out of them but by trade.
        var wolfPack = arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Arrows);
        Assert.Contains("trader's wares", arena.Submit(new MoveItemCommand(arena.Player, wolfPack.Ref, ItemPlace.In(Wares), ItemPlace.Carried, 1)));
        var sword = arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.weapon.rusted_sword");
        Assert.Equal("unequip item.weapon.rusted_sword first", arena.Submit(new SellCommand(arena.Player, Kera, sword.ItemId, 1)));

        var far = At(session, AtRenn, r => With(r, 25));
        Assert.Equal("Kera Voss is out of reach", far.Submit(new BuyCommand(far.Player, Kera, arrows.Ref, 1)));
        Assert.Equal("Renn Vale does not trade", far.Submit(new BuyCommand(far.Player, Renn, arrows.Ref, 1)));
    }

    [Fact]
    public void ATradersWares_SurviveASaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera, r => With(r, 40, Stack(Hide, 1)));
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Arrows).Ref, 7)));
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, arena.Simulation.Player.Inventory.Single(e => e.DefId == Hide).ItemId, 1)));
        arena.Tick(2);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("traded"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var again = Arena.Resume(session.Setup, store.Load(SaveSlots.Manual("traded"), new LoadContext(session.Generator, session.Content, new Registry())));

        Assert.Equal(arena.Simulation.StateDigest(), again.Simulation.StateDigest());
        Assert.Equal(arena.Simulation.Wares(Kera)!.Wares.ToList(), again.Simulation.Wares(Kera)!.Wares.ToList());
        Assert.Equal(arena.Simulation.Player.Currency, again.Simulation.Player.Currency);
    }

    /// <summary>
    /// The Phase-1 technical audit, M-01: a fixed-stock trader bought out is out. Her wares stay an empty record rather than falling back to
    /// her authored stock, and stay empty across a save and load.
    /// </summary>
    [Fact]
    public void ATraderBoughtOut_StaysEmpty_AcrossASaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera, r => With(r, 1000));
        int stacks = arena.Simulation.Wares(Kera)!.Wares.Length;
        Assert.Equal(5, stacks);
        for (int i = 0; i < stacks; i++)
        {
            var ware = arena.Simulation.Wares(Kera)!.Wares[0];
            Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, ware.Ref, ware.Count)));
        }
        Assert.Empty(arena.Simulation.Wares(Kera)!.Wares);
        Assert.Empty(arena.Simulation.World.Container(Wares)!.Items);
        Assert.Equal((60, 1, 1), (Carried(arena, Arrows), Carried(arena, "item.armor.hide_vest"), Carried(arena, "item.armor.hide_cap")));
        arena.Tick(2);
        Assert.Empty(arena.Simulation.Wares(Kera)!.Wares);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("bought_out"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var again = Arena.Resume(session.Setup, store.Load(SaveSlots.Manual("bought_out"), new LoadContext(session.Generator, session.Content, new Registry())));

        Assert.Equal(arena.Simulation.StateDigest(), again.Simulation.StateDigest());
        Assert.Empty(again.Simulation.Wares(Kera)!.Wares);
        Assert.Empty(again.Simulation.World.Container(Wares)!.Items);
        // What she is sold afterwards joins her wares, as it always did.
        Assert.Null(again.Submit(new SellCommand(again.Player, Kera, again.Simulation.Player.Inventory.Single(e => e.DefId == "item.armor.hide_vest").ItemId, 1)));
        Assert.Equal("item.armor.hide_vest", Assert.Single(again.Simulation.Wares(Kera)!.Wares).ItemId);
    }
}
