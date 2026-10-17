using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.M2Probe;

/// <summary>
/// Worlds shared by the M2 tests and the probe process, so a save made in one process can be checked
/// against the same world built in another. Everything here is deterministic - including the player's
/// IDs - so separately built worlds are comparable digest-for-digest.
/// </summary>
public static class M2Fixtures
{
    public const ulong Seed = 0x5C1A9E7B4D2F0083;
    public const string ContentVersion = "0.1.0";
    public const string Slot = SaveSlots.Quick;

    public static readonly string ContentHash = "sha256:" + string.Concat(Enumerable.Repeat("ab", 32));

    /// <summary>Every definition ID the fixture worlds reference: flags, creatures, items.</summary>
    public static readonly IReadOnlyList<string> DefinitionIds = new[]
    {
        "creature.beast.deer", "creature.beast.wolf_grey",
        "item.potion.heal_dangling", "item.weapon.iron_sword",
        "world.door.barn_open", "world.door.cellar_open", "world.lever.mill_gate",
    };

    /// <summary>ROADMAP M2's exit world: ten cells.</summary>
    public static readonly CellKey[] TenCells =
        Enumerable.Range(0, 10).Select(i => new CellKey(new RegionKey(0, 0), 0, i)).ToArray();

    public static GenerationProfile Profile(int wolfTarget = 5) => new(
        new[]
        {
            new NodeRule("iron_vein", "resource.ore.iron_vein", 2, 4),
            new NodeRule("silverleaf", "resource.herb.silverleaf", 0, 3),
        },
        new[]
        {
            new PopulationRule("wolves", "creature.beast.wolf_grey", wolfTarget, 2, 7),
            new PopulationRule("deer", "creature.beast.deer", 3, 1, 4),
        },
        new TerrainRule(12_000, 800, 11));

    public static CellBaselineGenerator Generator(int wolfTarget = 5) => new(Profile(wolfTarget));

    public static ContentIdentity Content(string? contentHash = null) =>
        new(ContentVersion, contentHash ?? ContentHash, DefinitionIds);

    public static LoadContext Context(Registry registry, ContentIdentity? content = null, int wolfTarget = 5) =>
        new(Generator(wolfTarget), content ?? Content(), registry);

    public static readonly EntityId PlayerId =
        EntityId.Create(EntityKind.Character, 1_700_000_000_000, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

    public static PlayerRecord Player(string name = "Aelin") => new(
        PlayerId, name, 150_250, 12_000, -40_125, PlayerRecord.DerivedAppearanceSeed(PlayerId),
        new[]
        {
            new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_001, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 1 }),
                "item.weapon.iron_sword", 1),
            new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_002, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 2 }),
                "item.potion.heal_dangling", 3),
        });

    /// <summary>The "before" world for the kill test: two cells changed.</summary>
    public static WorldDelta OldWorld(Registry registry, int wolfTarget = 5)
    {
        var world = new WorldDelta(Generator(wolfTarget), Seed, registry);
        world.SetFlag(TenCells[0], "world.door.cellar_open", 1);
        world.HarvestNode(TenCells[3], world.Baseline(TenCells[3]).Nodes[0].NodeKey, tick: 1_000);
        return world;
    }

    /// <summary>The "after" world: the old changes plus two more cells.</summary>
    public static WorldDelta NewWorld(Registry registry)
    {
        var world = OldWorld(registry);
        world.SetPopulationAlive(TenCells[5], $"pop.r_0_0.c_00_05.deer", 1);
        world.SetFlag(TenCells[7], "world.lever.mill_gate", 3);
        return world;
    }

    /// <summary>
    /// The historical-fixture world (tests/Persistence.Tests/Fixtures/README.md): the same logical world
    /// at every schema version, written by that version's own writer, with fixture content 0.1.0.
    /// </summary>
    public static class Historical
    {
        /// <summary>The pack the v1-v3 fixtures were written with (Fixtures/content-0.1.0).</summary>
        public const string ContentVersion = "0.1.0";
        public const string ContentHash = "sha256:7f522a30f46119bbe25e50c29a9db0a4ff21be31b080dc7a5eba0f225379353d";

        /// <summary>
        /// The pack the current fixture is written with (Fixtures/content-0.1.6: 0.1.5 plus the two quests the player's quest
        /// records name, and the warden's replies that start them). 0.1.5 is 0.1.4 plus the NPC and the conversation the
        /// relationship and conversation records name; 0.1.4 is 0.1.3 plus the creature a creature record names; 0.1.3 is
        /// 0.1.2 plus the two effects the player's record names; 0.1.2 is 0.1.1 plus a region, the place the discovery record
        /// names, and the movement and tier config a region needs.
        /// </summary>
        public const string WriterContentVersion = "0.1.6";
        public const string WriterContentHash = "sha256:37c6725ef94b6c176f111d461690bc4c06b9ac6217d93ad834cf2a10a3e2b5b7";

        public static PlayerRecord Player() => new(
            PlayerId, "Aelin", 150_250, 12_000, -40_125, PlayerRecord.DerivedAppearanceSeed(PlayerId),
            new[]
            {
                // Schema 9: the sword is a fine one (quality lands on the instance).
                new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_001, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 1 }),
                    "item.weapon.iron_sword", 1)
                {
                    Quality = 1,
                    // Schema 19 (WP3a): the sword carries a notable record - given as the cull's reward, grown a little familiar.
                    Notable = new NotableRecord("Iron Sword", ProvenanceKind.Given, "quest.fixture.cull", 3_500) { Familiarity = 2, Uses = 1 },
                    // Schema 24 (WP3b): it has taken 40 points of wear - not yet worn out, so still fine.
                    Wear = new ItemWear(40, 1),
                },
                new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_002, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 2 }),
                    "item.potion.healing_draught", 3),
            },
            Progression(),
            facingMdeg: 123_456,
            // Schema 5. Written with content 0.1.2, which calls the place location.wolf_den; the current pack renames it.
            discoveries: new[] { new DiscoveryRecord("location.wolf_den", DiscoveryMethod.Visited, 3_000) },
            // Schema 6: the sword in the main hand, and a purse.
            equipment: new[] { KeyValuePair.Create(EquipSlot.MainHand, SwordId) },
            currency: 40,
            // Schema 7: two effects mid-course. Written with content 0.1.3, which calls the second effect.weakness; the
            // current pack renames it.
            effects: new[] { new ActiveEffect("effect.bleeding", 2, 5_100, 5_020), new ActiveEffect("effect.weakness", 1, 5_600, 4_801) },
            // Schema 10: what the warden thinks of Aelin, both ways, and the lines of the warden's conversation heard. Written
            // with content 0.1.5, which calls them npc.fixture.warden and dialogue.fixture.warden; the current pack renames both.
            relationships: new[] { new RelationshipValue("npc.fixture.warden", "respect", -3), new RelationshipValue("npc.fixture.warden", "trust", 12) },
            conversations: new[] { new ConversationMemory("dialogue.fixture.warden", ImmutableArray.Create("greet", "rumour")) },
            // Schema 11: the warden's errand half done - asked, the den not yet found - and the cull finished, its three wolves
            // counted. Written with content 0.1.6, which calls the errand quest.fixture.errand; the current pack renames it.
            quests: new[]
            {
                new QuestState("quest.fixture.errand", QuestStatus.Active, 4_000, null, null, ImmutableArray.Create(
                    new ObjectiveState("o_ask", ObjectiveStatus.Satisfied, 4_000, 4_001, 0),
                    new ObjectiveState("o_den", ObjectiveStatus.Active, 4_001, null, 0))),
                new QuestState("quest.fixture.cull", QuestStatus.Completed, 2_000, 3_500, "o_cull", ImmutableArray.Create(
                    new ObjectiveState("o_cull", ObjectiveStatus.Satisfied, 2_000, 3_500, 3))),
            },
            // Schema 12: the warden has joined Aelin and is following, hurt, two ticks without headway, three marks of the trail
            // still ahead. Written with content 0.1.6, which calls the warden npc.fixture.warden; the current pack renames them.
            companions: new[]
            {
                new CompanionRecord("npc.fixture.warden", CompanionOrder.Follow, CompanionCondition.Up, 148_750, -41_500, 45_000, 64)
                {
                    StuckTicks = 2,
                    LastCombatTick = 4_950,
                    Trail = ImmutableArray.Create(new TrailMark(149_250, -41_000), new TrailMark(149_750, -40_750), new TrailMark(150_250, -40_125)),
                    // Schema 21 (WP15a): the warden is in Aelin's hire, a crafter on a ten-day contract at 7 a day - three days served, the
                    // last one unpaid, 21 coin earned - set to the salve at the fixture hearth with two healing draughts handed over; defensive,
                    // off the leash, protecting the injured. The draught is under the name it had then.
                    Membership = PartyMembership.Hireling,
                    Stance = CompanionStance.Defensive,
                    Tactics = new PartyTactics(KeepToLeash: false, ProtectInjured: true, HoldHealing: false),
                    Contract = new HirelingContract(HirelingRole.Crafter, 10, 7, PartyRules.WageOnly)
                    {
                        DaysServed = 3,
                        UnpaidDays = 1,
                        Purse = 21,
                        WorkSite = "station.fixture_hearth",
                        RecipeId = "recipe.alchemy.salve_minor",
                        Supply = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal).Add("item.potion.healing_draught", 2),
                    },
                },
            })
        {
            // Schema 13 (the owner's M6 playtest): Aelin is crouched, on the ground.
            Posture = new Posture(Stance.Crouched, Airborne: false, AirMs: 0),
            // Schema 15 (Waystone travel): two stones known - one touched awake, one woken - the woken one chosen as the return point,
            // and two stretches of a road walked.
            Waystones = ImmutableArray.Create(new WaystoneRecord("waystone.fixture.cairn", "region.fixture_vale", WaystoneMethod.Touched, 4_100),
                new WaystoneRecord("waystone.fixture.ford", "region.fixture_vale", WaystoneMethod.Woken, 4_500)),
            ReturnPoint = "waystone.fixture.ford",
            RoadsWalked = ImmutableArray.Create("road.fixture.track#0", "road.fixture.track#1"),
            // Schema 16 (WP5, E-7): three ash hounds killed, two healing draughts handed to the warden, a sword crafted - the hound, the
            // draught and the warden under the names they had then.
            Deeds = ImmutableArray.Create(new DeedTally(DeedKind.Killed, "creature.beast.ash_hound", null, 3, 4_800),
                new DeedTally(DeedKind.Delivered, "item.potion.healing_draught", "npc.fixture.warden", 2, 4_200),
                new DeedTally(DeedKind.Crafted, "item.weapon.iron_sword", null, 1, 3_900)),
            // Schema 17 (the character creator): Aelin is "she", a Veth of a chosen build, of the Scholar's Call.
            Identity = new CharacterIdentity(Pronouns.She, "people.veth", "body.build_slender", "call.scholar"),
            // Schema 20 (WP6): Aelin killed an ash hound at the ridge and told the warden, so the wardens know it of her and regard her
            // at +100; the act log counts on from 2. The hound, the warden and the wardens under the names they had then.
            Factions = new FactionLedger(2,
                ImmutableArray.Create(new ActRecord(1, ActKinds.CreatureKilled, "creature.beast.ash_hound", "r_0_0:c_00_02", 21_500, 4_250, 4_800)),
                ImmutableArray.Create(new FactionKnowledge("faction.fixture.wardens", 1, Identities.Identified, KnowledgeSources.Reported,
                    "npc.fixture.warden", 4_900, 100)),
                ImmutableArray.Create(new FactionStanding("faction.fixture.wardens", 100))),
            // Schema 23 (the quick bar, WP9): the firebolt on key 4, and the draught taken off key 1 on purpose - both renamed by the
            // current pack, so the renames must reach the bar and what was cleared off it.
            QuickBar = ImmutableArray.Create<string?>(null, null, null, "spell.ember.firebolt", null, null, null, null),
            QuickBarCleared = ImmutableArray.Create("item.potion.healing_draught"),
            // Schema 25 (WP15b): the warden's sheet - level 2 with 40 XP toward 3, one-hand blade 4 - wearing a sword Aelin loaned her in
            // the main hand, and keeping a healing draught of her own. The draught under the name it had then.
            PartySheets = ImmutableArray.Create(new PartySheet("npc.fixture.warden", CharacterProgression.Empty with
            {
                Level = 2,
                LevelProgressXp = 40,
                Skills = ImmutableSortedDictionary.Create<string, SkillState>(StringComparer.Ordinal).Add("skill.one_hand_blade", new SkillState(4, 6)),
            })
            {
                Gear = ImmutableSortedDictionary.Create<EquipSlot, PartyGear>().Add(EquipSlot.MainHand, new PartyGear(
                    new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_005, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 5 }), "item.weapon.iron_sword", 1)
                    {
                        Wear = new ItemWear(12, 0),   // schema 24's wear goes with a piece a party member wears
                    },
                    Loaned: true)),
                Belongings = ImmutableArray.Create(new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_006, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 6 }),
                    "item.potion.healing_draught", 1)),
            }),
            // Schema 27 (the soak's W4-SAVE-01): a swing of the sword in hand under way, begun at tick 4 995, that has struck the first
            // den wolf already.
            Attack = new AttackRecord("item.weapon.iron_sword", 4_995, ImmutableArray.Create(Creature(1))),
        };

        public static readonly EntityId SwordId = EntityId.Create(EntityKind.Item, 1_700_000_000_001, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 1 });

        /// <summary>
        /// Progression in every field (schema 4). Written with content 0.1.1, so it names the formula
        /// <c>spell.ember.firebolt</c> and the potion <c>item.potion.healing_draught</c>; the current fixture pack renames
        /// both, and the load must carry the rename into the knowledge record and the production record.
        /// </summary>
        public static CharacterProgression Progression() => new()
        {
            Level = 3,
            LevelProgressXp = 120,
            XpDebt = 35,
            LifetimeXp = ImmutableSortedDictionary.CreateRange(new[]
            {
                KeyValuePair.Create(XpSource.Discovery, 400L),
                KeyValuePair.Create(XpSource.Combat, 395L),
                KeyValuePair.Create(XpSource.Production, 100L),
            }),
            Allocation = ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(CharacterAttribute.Might, 1) }),
            UnspentAttributePoints = 1,
            Grants = ImmutableArray.Create(new AttributeGrant(CharacterAttribute.Endurance, 1, GrantSource.Quest, "quest.fixture.rescue")),
            Skills = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
            {
                KeyValuePair.Create("skill.athletics", new SkillState(2, 0)),
                KeyValuePair.Create("skill.one_hand_blade", new SkillState(5, 12)),
            }),
            Known = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
            {
                KeyValuePair.Create("recipe.alchemy.salve_minor", new KnownTechnique(LearningSource.Book, "item.book.alchemy_primer", 2_400)),
                KeyValuePair.Create("spell.ember.firebolt", new KnownTechnique(LearningSource.Teacher, "npc_01HF7YAT0T0000000000000001", 1_200)),
            }),
            ProductionFirsts = ImmutableSortedSet.Create(StringComparer.Ordinal, "item.potion.healing_draught"),
            NoveltyFirsts = ImmutableSortedSet.Create(StringComparer.Ordinal, "recipe.alchemy.salve_minor"),
            Pools = new PoolState(87, null, 40, 6),
            Guards = new XpGuardState(
                ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create("creature.beast.wolf_grey", new SpeciesDay(0, 3)) }),
                ImmutableSortedSet.Create(StringComparer.Ordinal, "creature.beast.deer", "creature.beast.wolf_grey"),
                ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
                {
                    KeyValuePair.Create("pop.r_0_0.c_00_02.wolves", ImmutableArray.Create(4_000L, 4_200L, 4_500L)),
                })),
        };

        public static WorldDelta World(Registry registry)
        {
            var world = new WorldDelta(Generator(), Seed, registry);
            var cells = TenCells;
            world.SetFlag(cells[0], "world.door.cellar_open", 1);
            world.HarvestNode(cells[3], world.Baseline(cells[3]).Nodes[0].NodeKey, tick: 1_000);
            world.SetPopulationAlive(cells[5], "pop.r_0_0.c_00_05.deer", 1);
            world.SetFlag(cells[7], "world.lever.mill_gate", 3);
            var wolves = world.Baseline(cells[2]).Populations.Single(p => p.PopulationId.EndsWith(".wolves", StringComparison.Ordinal));
            world.KillOccupant(wolves.Slots[0].SlotKey);
            var deer = world.Baseline(cells[4]).Populations.Single(p => p.PopulationId.EndsWith(".deer", StringComparison.Ordinal));
            world.MoveOccupant(deer.Slots[1].SlotKey, 1_234, 5_678);
            // A created persistent instance. Schema 3 is the first that can record one, so the v1 and v2
            // fixtures, written before it, have none.
            world.PlaceCreated(cells[6], "item.weapon.iron_sword", 500, 600);
            // Schema 6: a dropped stack keeps its count, and a changed world container holds its whole contents.
            // Both name the potion by its 0.1.x ID, so the load renames it in the world as well as in the inventory.
            var stack = registry.CreateEntity(DefinitionId.Parse("item.potion.healing_draught")).InstanceId;
            world.PlaceItem(TenCells[8], stack, "item.potion.healing_draught", 3, 700, 800, quality: 1);   // schema 9: fine
            var chest = registry.CreateEntity(DefinitionId.Parse("container.fixture_chest"), EntityKind.Container).InstanceId;
            var blade = registry.CreateEntity(DefinitionId.Parse("item.weapon.iron_sword")).InstanceId;
            var potions = registry.CreateEntity(DefinitionId.Parse("item.potion.healing_draught")).InstanceId;
            world.SetContainer(new ContainerRecord("container.fixture_chest", chest, TenCells[9].ToString(), ImmutableArray.Create(
                // Schema 19 (WP3a): a masterwork blade in the chest, with its maker's mark.
                new ContainerItem(blade, "item.weapon.iron_sword", 1)
                {
                    Quality = 2,
                    Notable = new NotableRecord("Iron Sword", ProvenanceKind.Crafted, "recipe.fixture.sword", 2_000) { Maker = "Aelin" },
                    // Schema 24 (WP3b): a little worn where it lies.
                    Wear = new ItemWear(5, 2),
                },
                new ContainerItem(potions, "item.potion.healing_draught", 4) { Quality = -1 })));   // schema 9: crude
            // Schema 8: spawners' creatures that left their baseline - one alive, moved and wounded; one a corpse half
            // searched (its body is a changed container); one gone, of a renamed species, two generations on and due back.
            world.SetCreature(new CreatureRecord("spawn.fixture.den#0", "creature.beast.wolf_grey", Creature(1), TenCells[1].ToString(), 0,
                CreatureCondition.Alive, 12_345, 67_890, 90_000, 21, 0, 0)
            {
                // It lost sight of its target at tick 4990 and is searching where it last saw it.
                Mind = CreatureMind.Searching, Awareness = 45, Knows = true, KnownXMm = 13_000, KnownZMm = 70_000, LastSeenTick = 4_990,
                SearchUntil = 5_110, HasCalled = true,
                // Schema 14: what its next ticks depend on - a charge to wait out, a stun it is in (40 ticks from 4995), and a stagger
                // it is immune to until 5020.
                NextChargeTick = 5_060, StaggerImmuneUntil = 5_020, StaggeredTick = 4_995, StaggerLastsTicks = 40,
            });
            // Schema 14: two sounds the next tick hears - a howl of the renamed species (the load renames its kind), and a blow.
            world.SetNoises(ImmutableArray.Create(
                new Noise(12_345, 67_890, 30_000, Call: true, CallerKind: "creature.beast.ash_hound"),
                new Noise(14_000, 66_000, 12_000)));
            world.SetCreature(new CreatureRecord("spawn.fixture.den#1", "creature.beast.wolf_grey", Creature(2), TenCells[1].ToString(), 0,
                CreatureCondition.Corpse, 14_000, 66_000, 180_000, 0, 4_800, 0));
            var corpse = registry.CreateEntity(DefinitionId.Parse("corpse.fixture_den.m1_g0"), EntityKind.Container).InstanceId;
            var meat = registry.CreateEntity(DefinitionId.Parse("item.potion.healing_draught")).InstanceId;
            world.SetContainer(new ContainerRecord("corpse.fixture_den.m1_g0", corpse, TenCells[1].ToString(), ImmutableArray.Create(
                new ContainerItem(meat, "item.potion.healing_draught", 1))));
            world.SetCreature(new CreatureRecord("spawn.fixture.ridge#0", "creature.beast.ash_hound", Creature(3), TenCells[2].ToString(), 2,
                CreatureCondition.Gone, 20_000, 30_000, 0, 0, 4_900, 30_000));
            // Schema 22 (WP14): the vale's market on day 3, short of draughts and remembering three the character sold it, with four more
            // on the road from the ford; its trader's purse, restocked on day 2 and shelf drawn on day 1, traded with.
            world.SetEconomy(new EconomyState(3,
                ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
                {
                    KeyValuePair.Create("market.fixture.vale", new MarketState("market.fixture.vale",
                        ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
                        {
                            KeyValuePair.Create("item.potion.minor_healing", 14L), KeyValuePair.Create("item.weapon.iron_sword", 2L),
                        }),
                        ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create("item.potion.minor_healing", 3L) }))),
                }),
                ImmutableArray.Create(new Shipment("road.fixture.track", "market.fixture.ford", "market.fixture.vale", "item.potion.minor_healing", 4, 4))));
            world.SetMerchant(new MerchantState("merchant.fixture.trader", 75, 2, 1, Traded: true));
            // Schema 26 (WP12): the lives at game hour 80 - the warden walking to her work two corners on, a little hungry; the vale's porter,
            // the slot's second resident, arrived on day 2, eating with the warden's company remembered; the carter's slot empty since day 1,
            // its next resident due on day 5; the warden and the porter last met at hour 70.
            world.SetNpcLife(new NpcLifeState(80,
                ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
                {
                    KeyValuePair.Create("npc.fixture.warden", new NpcLifeRecord("npc.fixture.warden", "routine.fixture.warden", new NpcNeeds(1_250, 400, 900, 0),
                        NpcActivities.Work, 148_750, -41_500, 45_000)
                    {
                        Cell = TenCells[0].ToString(), StuckTicks = 2,
                        Route = NavRoute.Active(new NavPoint(152_000, -40_000), ImmutableArray.Create(new NavPoint(150_000, -41_000), new NavPoint(152_000, -40_000)),
                            4_990, 0x1122334455667788UL, new NavRect(128_000, -62_000, 172_999, -20_001), false),
                    }),
                    KeyValuePair.Create("routine.fixture.porter", new NpcLifeRecord("routine.fixture.porter", "routine.fixture.porter", new NpcNeeds(2_100, 0, 300, 1_000),
                        NpcActivities.Eat, 12_000, 34_000, 180_000)
                    {
                        Cell = TenCells[0].ToString(), Name = "Aldo Renning", Generation = 1, ArrivedDay = 2, With = null,
                        Condition = NpcCondition.Injured, ConditionUntil = 6_000,
                    }),
                }),
                ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
                {
                    KeyValuePair.Create("routine.fixture.carter", new NpcVacancy("routine.fixture.carter", 1, 5, 1)),
                }),
                ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create("npc.fixture.warden|routine.fixture.porter", 70L) })));
            return world;
        }

        private static EntityId Creature(byte n) => EntityId.Create(EntityKind.Creature, 1_700_000_000_100 + n, new byte[] { 7, 7, 7, 7, 7, 7, 7, 7, 7, n });

        public const string CurrentContentVersion = "0.2.9";
        public const string CurrentContentHash = "sha256:c4892c92c6e2fd41f08ea5da5ff27914a035183a2503c6e3f0c8c360cb846b75";

        /// <summary>
        /// Fixtures/content (0.2.0) as a content identity, for the probe, which does not load content
        /// files. A test pins this mirror to the real pack.
        /// </summary>
        public static ContentIdentity CurrentContent() => new(
            CurrentContentVersion, CurrentContentHash,
            new[]
            {
                "config.base_speeds", "config.factions", "config.simulation_tiers",
                "creature.beast.ash_ember_hound", "creature.beast.deer", "creature.beast.wolf_grey", "dialogue.fixture.warden_sera",
                "effect.bleeding", "effect.weakened", "faction.fixture.keepers",
                "item.potion.minor_healing",
                "item.weapon.iron_sword",
                "location.den_mouth", "npc.fixture.warden_sera", "quest.fixture.cull", "quest.fixture.wardens_errand", "recipe.alchemy.salve_minor",
                "region.fixture_vale", "skill.athletics",
                "skill.one_hand_blade",
                "spell.ember.bolt", "world.door.cellar_open", "world.lever.mill_gate",
            },
            new Dictionary<string, string>
            {
                ["item.potion.healing_draught"] = "item.potion.minor_healing",
                ["spell.ember.firebolt"] = "spell.ember.bolt",
                ["location.wolf_den"] = "location.den_mouth",
                ["effect.weakness"] = "effect.weakened",
                ["creature.beast.ash_hound"] = "creature.beast.ash_ember_hound",
                ["npc.fixture.warden"] = "npc.fixture.warden_sera",
                ["dialogue.fixture.warden"] = "dialogue.fixture.warden_sera",
                ["quest.fixture.errand"] = "quest.fixture.wardens_errand",
                ["faction.fixture.wardens"] = "faction.fixture.keepers",
            });

        public static LoadContext Context(Registry registry) => new(Generator(), CurrentContent(), registry);

        /// <summary>
        /// Write the fixture with this build's writer. From schema 18 it was saved at a known moment, after a login that caught up a game day
        /// and a quarter (Fixtures/README.md).
        /// </summary>
        public static void Write(string profileRoot) =>
            new SaveStore(profileRoot).Save(SaveSlots.Quick, SaveDocuments.Capture(
                World(new Registry()), Player(), new ContentIdentity(WriterContentVersion, WriterContentHash, Array.Empty<string>()),
                5_000, playtimeSeconds: 321.5, worldTimeAdvancedTicks: 72_000) with
            {
                CapturedAt = new DateTimeOffset(2026, 10, 17, 9, 30, 0, 250, TimeSpan.Zero),
                SavedUtc = new DateTimeOffset(2026, 10, 17, 9, 30, 0, 250, TimeSpan.Zero),
                AwayLog = ImmutableArray.Create(new AdvanceLine(4_000, AdvanceKinds.TimePassed, string.Empty, 72_000),
                    new AdvanceLine(4_000, AdvanceKinds.DaysPassed, string.Empty, 1)),
            });
    }

    public static SaveDocument Document(WorldDelta world, PlayerRecord? player = null, long tick = 5_000, ContentIdentity? content = null) =>
        SaveDocuments.Capture(world, player ?? Player(), content ?? Content(), tick, playtimeSeconds: 321.5);

    /// <summary>A digest of the effective state of all ten cells - equal exactly when the worlds are equal.</summary>
    public static string WorldDigest(WorldDelta world)
    {
        using var h = new CanonicalHasher();
        foreach (var cell in TenCells)
            h.Add(world.EffectiveCellDigest(cell));
        return h.Finish();
    }
}
