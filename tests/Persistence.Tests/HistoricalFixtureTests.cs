using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Travel;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.M2Probe;
using UNNAMED.SaveTool;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>The committed historical fixtures (Fixtures/README.md) and the context they load under.</summary>
internal static class Fixtures
{
    public const string ContentVersion = "0.2.9";

    public static string Root { get; } = FindRoot();

    public static string Save(int schema) => Path.Combine(Root, $"v{schema}", SaveSlots.Quick);

    public static string Expected(int schema) => Path.Combine(Root, $"v{schema}", "expected.json");

    public static string ContentRoot => Path.Combine(Root, "content");

    public static string Profile => Path.Combine(Root, "worldgen_profile.json");

    public static ContentIdentity Content(string? contentRoot = null) =>
        SaveToolApp.ReadContent(contentRoot ?? ContentRoot, ContentVersion, TextWriter.Null)
        ?? throw new InvalidOperationException("The fixture content does not validate");

    public static LoadContext Context(Registry registry, ContentIdentity? content = null) =>
        new(new CellBaselineGenerator(WorldgenProfileFile.Read(Profile)), content ?? Content(), registry);

    /// <summary>A throwaway profile holding a copy of the fixture: tests never touch the committed bytes.</summary>
    public static TempProfile Copy(int schema)
    {
        var profile = new TempProfile();
        CopyDirectory(Save(schema), Path.Combine(profile.Root, SaveSlots.Quick));
        return profile;
    }

    public static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return Path.Combine(dir.FullName, "tests", "Persistence.Tests", "Fixtures");
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }
}

/// <summary>
/// M2b §11 and §19's CI proof: every schema version that has shipped has a committed fixture written by
/// that version's own writer, and every one loads - and migrates through the commit path - to its
/// expected current state, non-lossily.
/// </summary>
public class HistoricalFixtureTests
{
    public static TheoryData<int> ShippedSchemas()
    {
        var data = new TheoryData<int>();
        for (int schema = SaveFormat.OldestSupportedSchema; schema <= SaveFormat.SchemaVersion; schema++)
            data.Add(schema);
        return data;
    }

    /// <summary>The fixture policy, enforced: a schema bump without its fixture fails CI.</summary>
    [Fact]
    public void EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture()
    {
        var present = Directory.EnumerateDirectories(Fixtures.Root, "v*")
            .Select(d => int.Parse(Path.GetFileName(d)[1..], System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(v => v)
            .ToList();

        Assert.Equal(Enumerable.Range(SaveFormat.OldestSupportedSchema, SaveFormat.SchemaVersion - SaveFormat.OldestSupportedSchema + 1), present);
        foreach (int schema in present)
        {
            Assert.True(File.Exists(Fixtures.Expected(schema)), $"v{schema} has no expected.json");
            string manifest = File.ReadAllText(Path.Combine(Fixtures.Save(schema), SaveFormat.Manifest));
            Assert.Contains($"\"schema_version\": {schema},", manifest);
        }
    }

    [Theory]
    [MemberData(nameof(ShippedSchemas))]
    public void Fixture_LoadsToItsExpectedCurrentState(int schema)
    {
        using var profile = Fixtures.Copy(schema);
        var loaded = new SaveStore(profile.Root).Load(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.True(loaded.IsComplete, string.Join("; ", loaded.Report.Warnings.Concat(loaded.Report.Loss)));
        string rendered = CanonicalState.Render(loaded);
        if (Environment.GetEnvironmentVariable("UNNAMED_WRITE_FIXTURE_EXPECTATIONS") == "1")
            File.WriteAllText(Fixtures.Expected(schema), rendered);
        Assert.Equal(File.ReadAllText(Fixtures.Expected(schema)).Replace("\r\n", "\n"), rendered);

        // Stated independently of expected.json: the fixture world as README.md describes it.
        Assert.Contains(loaded.Player.Inventory, e => e.DefId == "item.potion.minor_healing" && e.Count == 3);   // renamed via _aliases.yaml
        Assert.Contains(loaded.Player.Inventory, e => e.DefId == "item.weapon.iron_sword" && e.Count == 1);
        Assert.Equal(1, loaded.World.GetFlag(CellKey.Parse("r_0_0:c_00_00"), "world.door.cellar_open"));
        Assert.True(loaded.World.IsHarvested(CellKey.Parse("r_0_0:c_00_03"), "node.r_0_0:c_00_03.iron_vein.00"));
        Assert.Equal(1, loaded.World.GetPopulationAlive(CellKey.Parse("r_0_0:c_00_05"), "pop.r_0_0.c_00_05.deer"));
        Assert.Equal(3, loaded.World.GetFlag(CellKey.Parse("r_0_0:c_00_07"), "world.lever.mill_gate"));
        Assert.False(loaded.World.Occupant("r_0_0:c_00_02.pop.r_0_0.c_00_02.wolves.00").Alive);   // tombstoned baseline entity
        var deer = loaded.World.Occupant("r_0_0:c_00_04.pop.r_0_0.c_00_04.deer.01");
        Assert.Equal((1_234, 5_678), (deer.XCm, deer.ZCm));

        // Schema 3's required field: written by v3, derived from the ULID for v1 and v2 (value from Reference/worldgen_v2_reference.py).
        Assert.Equal(0x32599743E39279EFUL, loaded.Player.AppearanceSeed);
        var created = loaded.World.CreatedIn(CellKey.Parse("r_0_0:c_00_06"));
        if (schema >= 3)
            Assert.Equal(("item.weapon.iron_sword", 500, 600), (Assert.Single(created).DefId, created[0].XCm, created[0].ZCm));
        else
            Assert.Empty(created);

        // Schema 4's progression record: written by v4 and renamed on load like the inventory; empty for v1-v3.
        var progression = loaded.Player.Progression;
        if (schema >= 4)
        {
            Assert.Equal(3, progression.Level);
            Assert.True(ProgressionEngine.Knows(progression, "spell.ember.bolt"));   // renamed via _aliases.yaml
            Assert.False(ProgressionEngine.Knows(progression, "spell.ember.firebolt"));
            Assert.Contains("item.potion.minor_healing", progression.ProductionFirsts);
            Assert.Equal(5, ProgressionEngine.SkillLevel(progression, "skill.one_hand_blade"));
        }
        else
        {
            Assert.Equal(CharacterProgression.Empty.Digest, progression.Digest);
        }

        // Schema 5's facing and discovery record: written by v5, the place renamed on load like everything else; absent before.
        if (schema >= 5)
        {
            Assert.Equal(123_456, loaded.Player.FacingMdeg);
            var discovery = Assert.Single(loaded.Player.Discoveries);
            Assert.Equal(("location.den_mouth", DiscoveryMethod.Visited, 3_000L), (discovery.LocationId, discovery.Method, discovery.Tick));
        }
        else
        {
            Assert.Equal(0, loaded.Player.FacingMdeg);
            Assert.Empty(loaded.Player.Discoveries);
        }

        // Schema 6's equipment, purse, dropped stack and changed container; the renamed potion reaches the world too.
        var dropped = loaded.World.CreatedIn(CellKey.Parse("r_0_0:c_00_08"));
        var chest = loaded.World.Container("container.fixture_chest");
        if (schema >= 6)
        {
            Assert.Equal(M2Fixtures.Historical.SwordId, loaded.Player.Equipment[Domain.Items.EquipSlot.MainHand]);
            Assert.Equal(40, loaded.Player.Currency);
            Assert.Equal(("item.potion.minor_healing", 3), (Assert.Single(dropped).DefId, dropped[0].Count));
            Assert.NotNull(chest);
            Assert.Equal(new[] { ("item.potion.minor_healing", 4), ("item.weapon.iron_sword", 1) },
                chest.Items.Select(i => (i.DefId, i.Count)).OrderBy(i => i.DefId, StringComparer.Ordinal));
        }
        else
        {
            Assert.Empty(loaded.Player.Equipment);
            Assert.Equal(0, loaded.Player.Currency);
            Assert.Empty(dropped);
            Assert.Null(chest);
        }

        // Schema 7's active effects: deadlines in world ticks, and the renamed effect renamed on load; none before.
        if (schema >= 7)
        {
            Assert.Equal(new[] { new ActiveEffect("effect.bleeding", 2, 5_100, 5_020), new ActiveEffect("effect.weakened", 1, 5_600, 4_801) },
                loaded.Player.Effects);
        }
        else
        {
            Assert.Empty(loaded.Player.Effects);
        }

        // Schema 8's creature records: where each is, how hurt, whether it lives; the renamed species renamed on load; the
        // half-searched corpse is a changed container like any other. None before.
        var creatures = loaded.World.CreaturesIn(CellKey.Parse("r_0_0:c_00_01")).Concat(loaded.World.CreaturesIn(CellKey.Parse("r_0_0:c_00_02"))).ToList();
        if (schema >= 8)
        {
            Assert.Equal(new[] { "spawn.fixture.den#0", "spawn.fixture.den#1", "spawn.fixture.ridge#0" }, creatures.Select(c => c.Key));
            Assert.Equal((CreatureCondition.Alive, 12_345L, 67_890L, 21), (creatures[0].Condition, creatures[0].XMm, creatures[0].ZMm, creatures[0].Health));
            Assert.Equal((CreatureCondition.Corpse, 4_800L), (creatures[1].Condition, creatures[1].DiedTick));
            Assert.Equal(("creature.beast.ash_ember_hound", CreatureCondition.Gone, 2, 30_000L),
                (creatures[2].DefId, creatures[2].Condition, creatures[2].Generation, creatures[2].RespawnTick));   // renamed via _aliases.yaml
            Assert.Equal("item.potion.minor_healing", Assert.Single(loaded.World.Container("corpse.fixture_den.m1_g0")!.Items).DefId);
            // Schema 14: what den#0's next ticks depend on - a charge, a stun, a stagger immunity. Before, none of it was kept.
            Assert.Equal(schema >= 14 ? (5_060L, 5_020L, (long?)4_995, 40) : (0L, 0L, null, 0),
                (creatures[0].NextChargeTick, creatures[0].StaggerImmuneUntil, creatures[0].StaggeredTick, creatures[0].StaggerLastsTicks));
        }
        else
        {
            Assert.Empty(creatures);
        }

        // Schema 14's sounds waiting to be heard: the howl, its kind renamed on load, and the blow; none before.
        Assert.Equal(schema >= 14
                ? new[] { new Noise(12_345, 67_890, 30_000, Call: true, CallerKind: "creature.beast.ash_ember_hound"), new Noise(14_000, 66_000, 12_000) }
                : Array.Empty<Noise>(),
            loaded.World.Noises);

        // Schema 11's quests: the errand half done and renamed on load, the cull finished with its count; none before.
        if (schema >= 11)
        {
            Assert.Equal(new[] { "quest.fixture.cull", "quest.fixture.wardens_errand" }, loaded.Player.Quests.Select(q => q.QuestId));
            var cull = loaded.Player.Quests[0];
            Assert.Equal((QuestStatus.Completed, 3_500L, "o_cull"), (cull.Status, cull.EndedTick!.Value, cull.EndedBy));
            Assert.Equal(3, Assert.Single(cull.Objectives).Progress);
            Assert.Equal(new[] { ("o_ask", ObjectiveStatus.Satisfied), ("o_den", ObjectiveStatus.Active) },
                loaded.Player.Quests[1].Objectives.Select(o => (o.Id, o.Status)));
        }
        else
        {
            Assert.Empty(loaded.Player.Quests);
        }

        // Schema 12's companion: the warden, following Aelin mid-trail, renamed on load; none before.
        if (schema >= 12)
        {
            var warden = Assert.Single(loaded.Player.Companions);
            Assert.Equal(("npc.fixture.warden_sera", CompanionOrder.Follow, CompanionCondition.Up, 64), (warden.NpcId, warden.Order, warden.Condition, warden.Health));
            Assert.Equal((148_750L, -41_500L, 45_000, 2, 4_950L), (warden.XMm, warden.ZMm, warden.FacingMdeg, warden.StuckTicks, warden.LastCombatTick));
            Assert.Equal(new[] { new TrailMark(149_250, -41_000), new TrailMark(149_750, -40_750), new TrailMark(150_250, -40_125) }, warden.Trail);
            // Schema 21's party (WP15a): the warden is in Aelin's hire, a crafter at the hearth, the draughts handed over renamed on load;
            // before it, everyone in the party was a companion, aggressive and keeping to the leash.
            Assert.Equal(schema >= 21
                    ? (PartyMembership.Hireling, CompanionStance.Defensive, new PartyTactics(false, true, false, FocusTarget: schema >= 34), new HirelingContract(HirelingRole.Crafter, 10, 7, PartyRules.WageOnly)
                    {
                        DaysServed = 3, UnpaidDays = 1, Purse = 21, WorkSite = "station.fixture_hearth", RecipeId = "recipe.alchemy.salve_minor",
                        Supply = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal).Add("item.potion.minor_healing", 2),
                    })
                    : (PartyMembership.Companion, CompanionStance.Aggressive, PartyTactics.Default, (HirelingContract?)null),
                (warden.Membership, warden.Stance, warden.Tactics, warden.Contract));
        }
        else
        {
            Assert.Empty(loaded.Player.Companions);
        }

        // Schema 13's posture (the owner's M6 playtest): Aelin crouched, on the ground; before it, standing.
        Assert.Equal(schema >= 13 ? new Posture(Stance.Crouched, false, 0) : Posture.Grounded, loaded.Player.Posture);

        // Schema 15's Waystone travel: two stones known - one touched, one woken and chosen to return to - and two stretches of road
        // walked; before it, no stone known, none chosen (the region's spawn), no road on the map.
        Assert.Equal(schema >= 15
                ? new[] { new WaystoneRecord("waystone.fixture.cairn", "region.fixture_vale", WaystoneMethod.Touched, 4_100), new WaystoneRecord("waystone.fixture.ford", "region.fixture_vale", WaystoneMethod.Woken, 4_500) }
                : Array.Empty<WaystoneRecord>(),
            loaded.Player.Waystones);
        Assert.Equal(schema >= 15 ? "waystone.fixture.ford" : null, loaded.Player.ReturnPoint);
        Assert.Equal(schema >= 15 ? new[] { "road.fixture.track#0", "road.fixture.track#1" } : Array.Empty<string>(), loaded.Player.RoadsWalked);

        // Schema 16's deeds (WP5, E-7): three hounds killed, two draughts handed to the warden, a sword crafted - every name renamed to
        // today's; before it, nothing counted.
        Assert.Equal(schema >= 16
                ? new[]
                {
                    new DeedTally(DeedKind.Crafted, "item.weapon.iron_sword", null, 1, 3_900),
                    new DeedTally(DeedKind.Delivered, "item.potion.minor_healing", "npc.fixture.warden_sera", 2, 4_200),
                    new DeedTally(DeedKind.Killed, "creature.beast.ash_ember_hound", null, 3, 4_800),
                }
                : Array.Empty<DeedTally>(),
            loaded.Player.Deeds);
        // Schema 17's identity (the character creator): Aelin is "she", a Veth of a chosen build, of the Scholar's Call; before it, the
        // character the game always made - "they", a Veth of the default body, with no Call - and her name kept.
        // Schema 39's appearance (the HumGen creator): her stature, face, hair, boots and two sliders; before it, none - the preset as baked.
        Assert.Equal(schema >= 17
                ? new CharacterIdentity(Pronouns.She, "people.veth", "body.build_slender", "call.scholar") { Appearance = schema >= 39 ? M2Fixtures.Appearance : null }
                : new CharacterIdentity(Pronouns.They, "people.veth", "body.default", null),
            loaded.Player.Identity);
        Assert.Equal("Aelin", loaded.Player.Name);
        // Schema 41's World Event Director (WP 1H): a trader on the road, a migration that passed through; before it, none.
        if (schema >= 41)
        {
            var director = loaded.World.Director!;
            Assert.Equal(130, director.SettledHour);
            Assert.Equal(("event.travelling_trader", 126L, 134L), (Assert.Single(director.Active).DefId, director.Active[0].StartHour, director.Active[0].EndHour));
            Assert.Equal(("event.creature_migration", "passed_through"), (Assert.Single(director.History).DefId, director.History[0].Outcome));
        }
        else
        {
            Assert.Null(loaded.World.Director);
        }
        // Schema 21's ledger: no one recovering or fallen - the fixture pack has one NPC, and the warden is in the party.
        Assert.Equal(PartyLedger.Empty, loaded.Player.Party);
        // Schema 23's quick bar (WP9): the firebolt on key 4 and the draught cleared off the bar, both under their current names; before
        // it, an empty bar with nothing cleared, which what she carries and knows fills once she is in the world.
        // Schema 32 widened it to twelve slots - an older bar keeps its eight where they were - and the writer put a gear-set switch on key 9.
        Assert.Equal(schema >= 23 ? new string?[] { null, null, null, "spell.ember.bolt", null, null, null, null, schema >= 32 ? "gearset.set_1" : null, null, null, null } : new string?[12],
            loaded.Player.QuickBar);
        // Schema 32's further action bars and gear sets: the second bar holds the draught and the bolt, the sword is kept as an outfit; before it, five empty bars, no set.
        Assert.Equal(schema >= 32
                ? new string?[] { "item.potion.minor_healing", null, null, null, null, null, null, null, null, null, null, "spell.ember.bolt" }
                : new string?[12], loaded.Player.ExtraBars[0]);
        Assert.All(loaded.Player.ExtraBars.Skip(1), bar => Assert.All(bar, slot => Assert.Null(slot)));
        if (schema >= 32)
        {
            var set = Assert.Single(loaded.Player.GearSets);
            Assert.Equal((1, "Sword and board"), (set.Index, set.Name));
            Assert.Equal(new GearSetItem(EquipSlot.MainHand, M2Fixtures.Historical.SwordId, "item.weapon.iron_sword"), Assert.Single(set.Items));
        }
        else
        {
            Assert.Empty(loaded.Player.GearSets);
        }
        Assert.Equal(schema >= 23 ? new[] { "item.potion.minor_healing" } : Array.Empty<string>(), loaded.Player.QuickBarCleared);
        // Schema 18's persistent clock (WP11): saved at a known moment after a login that caught up a game day and a quarter; before it, the
        // moment the save was taken is its build_timestamp, to the second, and no login had caught anything up.
        if (schema >= 18)
        {
            Assert.Equal("2026-10-17T09:30:00.250Z", loaded.Manifest.SavedUtc);
            Assert.Equal(72_000, loaded.Manifest.WorldTimeAdvancedTicks);
            Assert.Equal(new[] { ("time.passed", 72_000L), ("time.days", 1L) }, loaded.Manifest.AwayLog.Select(l => (l.Kind, l.Amount)));
        }
        else
        {
            Assert.Equal(DateTimeOffset.Parse(loaded.Manifest.BuildTimestamp, System.Globalization.CultureInfo.InvariantCulture), loaded.Manifest.SavedAt);
            Assert.Empty(loaded.Manifest.AwayLog);
        }
        // Schema 19's notable records (WP3a): the carried sword was given as the cull's reward and has grown a little familiar; the chest's
        // blade is a masterwork with its maker's mark. Before it, no stack carried a record, and none was masterwork.
        var carried = loaded.Player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId);
        Assert.Equal(schema >= 19 ? new NotableRecord("Iron Sword", ProvenanceKind.Given, "quest.fixture.cull", 3_500) { Familiarity = 2, Uses = 1 } : null,
            carried.Notable);
        if (chest is not null)
        {
            var blade = chest.Items.Single(i => i.DefId == "item.weapon.iron_sword");
            Assert.Equal(schema >= 19 ? (2, new NotableRecord("Iron Sword", ProvenanceKind.Crafted, "recipe.fixture.sword", 2_000) { Maker = "Aelin" }) : (0, null),
                (blade.Quality, blade.Notable));
        }
        // Schema 24's wear (WP3b): the carried sword has taken 40 points, still fine; the chest's masterwork blade 5. Before it, nothing was worn.
        Assert.Equal(schema >= 24 ? new ItemWear(40, 1) : null, carried.Wear);
        if (chest is not null)
            Assert.Equal(schema >= 24 ? new ItemWear(5, 2) : null, chest.Items.Single(i => i.DefId == "item.weapon.iron_sword").Wear);
        // Schema 40's enhancements (WP 1B, spec 47): the carried sword has a fire edge set and a slot cracked; the chest's blade two runes. Before
        // it, nothing was enhanced.
        Assert.Equal(schema >= 40 ? new[] { "item.material.cinder_sac" } : null, carried.Enhancements?.Filled.ToArray());
        Assert.Equal(schema >= 40 ? (1, 3) : (0, 0), (carried.Enhancements?.Cracked ?? 0, carried.Enhancements?.Attempts ?? 0));
        if (chest is not null)
            Assert.Equal(schema >= 40 ? new[] { "item.rune.keen", "item.trinket.wolf_fang" } : null,
                chest.Items.Single(i => i.DefId == "item.weapon.iron_sword").Enhancements?.Filled.ToArray());
        // Schema 22's economy (WP14): the vale's market on day 3 with four draughts on the road, and its trader's purse. Before it, a save
        // held no economy: the markets start at their targets when the world does.
        var economy = loaded.World.Economy;
        if (schema >= 22)
        {
            Assert.NotNull(economy);
            Assert.Equal((3L, 14L, 3L), (economy!.Day, economy.Markets["market.fixture.vale"].Stock["item.potion.minor_healing"],
                economy.Markets["market.fixture.vale"].SoldByCharacter["item.potion.minor_healing"]));
            Assert.Equal(4, Assert.Single(economy.Shipments).Count);
            Assert.Equal(new UNNAMED.Domain.Economy.MerchantState("merchant.fixture.trader", 75, 2, 1, true), Assert.Single(loaded.World.Merchants));
        }
        else
        {
            Assert.Null(economy);
            Assert.Empty(loaded.World.Merchants);
        }

        // Schema 26's NPC life (WP12): the warden walking to her work, the porter injured, the carter's slot awaiting its next resident.
        // Before it, a save held no lives: the NPCs begin their routines when the world starts. A meal under way at schema 26-28 is time
        // about their day at 29 (owner, 2026-10-17: no needs, meals or visits), and the pairs' last meetings are gone.
        var life = loaded.World.NpcLife;
        if (schema >= 26)
        {
            Assert.NotNull(life);
            Assert.Equal(80, life!.Hour);
            var warden = life.Npcs["npc.fixture.warden"];
            Assert.Equal((NpcActivities.Work, 148_750L, -41_500L, NavRouteStatus.Active, 2), (warden.Activity, warden.XMm, warden.ZMm, warden.Route.Status, warden.Route.Corners.Length));
            var porter = life.Npcs["routine.fixture.porter"];
            Assert.Equal(("Aldo Renning", 1, NpcCondition.Injured, 6_000L), (porter.Name, porter.Generation, porter.Condition, porter.ConditionUntil));
            // Schema 29 (WP13): the carter's slot is promised to a retiring adventurer.
            Assert.Equal(new NpcVacancy("routine.fixture.carter", 1, 5, 1) { ArrivingName = schema >= 29 ? "Wyn Carrow" : null }, Assert.Single(life.Vacancies).Value);
            Assert.Equal(schema >= 29 ? NpcActivities.Sleep : NpcActivities.Idle, porter.Activity);
            // Schema 28 (WP12b): a resident hired by the party, their routine suspended, and one away on a trip. Before it, neither existed.
            if (schema >= 28)
            {
                var hunter = life.Npcs["routine.fixture.hunter"];
                Assert.Equal(("Mira Tolle", NpcCondition.Hired, 0L, NpcActivities.Idle), (hunter.Name, hunter.Condition, hunter.ConditionUntil, hunter.Activity));
                Assert.Equal(NpcActivities.Away, life.Npcs["routine.fixture.trapper"].Activity);
            }
            else
            {
                Assert.Equal(2, life.Npcs.Count);
            }
        }
        else
        {
            Assert.Null(life);
        }

        // Schema 29's adventurers (WP13): Bram out after the den's wolves with Kester, asking for company; Kester injured; the third slot
        // awaiting someone new; both bodies standing at the lodge. Before it, a save held none: each settlement's slots fill when the world starts.
        var adventurers = loaded.World.Adventurers;
        if (schema >= 29)
        {
            Assert.NotNull(adventurers);
            Assert.Equal(3, adventurers!.Day);
            var bram = adventurers.Agents["npc.adventurer.fixture_vale.slot_1_g0"];
            Assert.Equal(("Bram Hallow", 3, 42L, AdventurerGoals.Hunt, 2, 5_000L), (bram.Name, bram.Level, bram.Coin, bram.Plan.Goal, bram.Plan.Group.Length, bram.Plan.PostTick));
            Assert.Equal(("item.weapon.iron_sword", 1), (bram.Gear[UNNAMED.Domain.Items.EquipSlot.MainHand].DefId, bram.Gear[UNNAMED.Domain.Items.EquipSlot.MainHand].Quality));
            Assert.Equal(2, bram.Pack["item.potion.minor_healing"]);
            Assert.Equal((AdventurerCondition.Injured, 5L), (adventurers.Agents["npc.adventurer.fixture_vale.slot_2_g0"].Condition, adventurers.Agents["npc.adventurer.fixture_vale.slot_2_g0"].InjuredUntilDay));
            Assert.Equal(new AdventurerVacancy("fixture_vale.slot_3", "fixture_vale", 2, 6, 1), Assert.Single(adventurers.Vacancies).Value);
            // Bram's body lingers at the lodge in sight though he is out; a load stands it there again.
            Assert.Equal(new AdventurerBody("Bram Hallow", 150_000, -38_000, 270_000), adventurers.Bodies["npc.adventurer.fixture_vale.slot_1_g0"]);
            Assert.Equal(2, adventurers.Bodies.Count);
        }
        else
        {
            Assert.Null(adventurers);
        }

        // Schema 20's faction ledger (WP6): one ash hound killed at the ridge and told to the warden, so the keepers - the wardens, when it
        // was written - regard Aelin at +100; every name renamed to today's. Before it, no act and every faction neutral.
        Assert.Equal(schema >= 20
                ? new FactionLedger(2,
                    ImmutableArray.Create(new ActRecord(1, ActKinds.CreatureKilled, "creature.beast.ash_ember_hound", "r_0_0:c_00_02", 21_500, 4_250, 4_800)),
                    ImmutableArray.Create(new FactionKnowledge("faction.fixture.keepers", 1, Identities.Identified, KnowledgeSources.Reported,
                        "npc.fixture.warden_sera", 4_900, 100)),
                    ImmutableArray.Create(new FactionStanding("faction.fixture.keepers", 100)))
                : FactionLedger.Empty,
            loaded.Player.Factions);

        // Schema 25's party sheets (WP15b): the warden at level 2, one-hand blade 4, wearing a sword on loan - worn a little - and keeping a
        // draught of her own, renamed on load with her. Before it, no party member had grown, worn or kept anything.
        if (schema >= 25)
        {
            var sheet = Assert.Single(loaded.Player.PartySheets);
            Assert.Equal(("npc.fixture.warden_sera", 2, 40L, 4), (sheet.NpcId, sheet.Progression.Level, sheet.Progression.LevelProgressXp,
                sheet.Progression.Skills["skill.one_hand_blade"].Level));
            Assert.Equal(("item.weapon.iron_sword", true, new ItemWear(12, 0)),
                (sheet.Gear[EquipSlot.MainHand].Item.DefId, sheet.Gear[EquipSlot.MainHand].Loaned, sheet.Gear[EquipSlot.MainHand].Item.Wear));
            Assert.Equal("item.potion.minor_healing", Assert.Single(sheet.Belongings).DefId);
        }
        else
        {
            Assert.Empty(loaded.Player.PartySheets);
        }

        // Schema 27's attack under way (the soak's W4-SAVE-01): a swing of the sword begun at tick 4995 that has struck the first den wolf.
        // Before it, a load started every attack over.
        if (schema >= 27)
        {
            var attack = loaded.Player.Attack!;
            Assert.Equal(("item.weapon.iron_sword", 4_995L), (attack.Source, attack.StartTick));
            Assert.Equal(UNNAMED.Domain.EntityId.Create(UNNAMED.Domain.EntityKind.Creature, 1_700_000_000_101, new byte[] { 7, 7, 7, 7, 7, 7, 7, 7, 7, 1 }), Assert.Single(attack.Struck));
        }
        else
        {
            Assert.Null(loaded.Player.Attack);
        }

        Assert.Equal(SaveFormat.SchemaVersion - schema, loaded.Report.Steps.Count);
        // v4 names the potion twice - held, and first produced - and the report counts each occurrence; v21 a third time, in the
        // warden's contract supply; v23 the potion and the firebolt once more, on the quick bar; v25 the potion and the warden once more, on
        // her party sheet.
        var aliases = schema switch
        {
            >= 25 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound x4", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "faction.fixture.wardens -> faction.fixture.keepers x2",
                "item.potion.healing_draught -> item.potion.minor_healing x9", "location.wolf_den -> location.den_mouth",
                "npc.fixture.warden -> npc.fixture.warden_sera x6", "quest.fixture.errand -> quest.fixture.wardens_errand",
                "spell.ember.firebolt -> spell.ember.bolt x2",
            },
            >= 23 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound x4", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "faction.fixture.wardens -> faction.fixture.keepers x2",
                "item.potion.healing_draught -> item.potion.minor_healing x8", "location.wolf_den -> location.den_mouth",
                "npc.fixture.warden -> npc.fixture.warden_sera x5", "quest.fixture.errand -> quest.fixture.wardens_errand",
                "spell.ember.firebolt -> spell.ember.bolt x2",
            },
            >= 21 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound x4", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "faction.fixture.wardens -> faction.fixture.keepers x2",
                "item.potion.healing_draught -> item.potion.minor_healing x7", "location.wolf_den -> location.den_mouth",
                "npc.fixture.warden -> npc.fixture.warden_sera x5", "quest.fixture.errand -> quest.fixture.wardens_errand",
                "spell.ember.firebolt -> spell.ember.bolt",
            },
            >= 20 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound x4", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "faction.fixture.wardens -> faction.fixture.keepers x2",
                "item.potion.healing_draught -> item.potion.minor_healing x6", "location.wolf_den -> location.den_mouth",
                "npc.fixture.warden -> npc.fixture.warden_sera x5", "quest.fixture.errand -> quest.fixture.wardens_errand",
                "spell.ember.firebolt -> spell.ember.bolt",
            },
            >= 16 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound x3", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "item.potion.healing_draught -> item.potion.minor_healing x6",
                "location.wolf_den -> location.den_mouth", "npc.fixture.warden -> npc.fixture.warden_sera x4",
                "quest.fixture.errand -> quest.fixture.wardens_errand", "spell.ember.firebolt -> spell.ember.bolt",
            },
            >= 14 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound x2", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "item.potion.healing_draught -> item.potion.minor_healing x5",
                "location.wolf_den -> location.den_mouth", "npc.fixture.warden -> npc.fixture.warden_sera x3",
                "quest.fixture.errand -> quest.fixture.wardens_errand", "spell.ember.firebolt -> spell.ember.bolt",
            },
            >= 12 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "item.potion.healing_draught -> item.potion.minor_healing x5",
                "location.wolf_den -> location.den_mouth", "npc.fixture.warden -> npc.fixture.warden_sera x3",
                "quest.fixture.errand -> quest.fixture.wardens_errand", "spell.ember.firebolt -> spell.ember.bolt",
            },
            11 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "item.potion.healing_draught -> item.potion.minor_healing x5",
                "location.wolf_den -> location.den_mouth", "npc.fixture.warden -> npc.fixture.warden_sera x2",
                "quest.fixture.errand -> quest.fixture.wardens_errand", "spell.ember.firebolt -> spell.ember.bolt",
            },
            10 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound", "dialogue.fixture.warden -> dialogue.fixture.warden_sera",
                "effect.weakness -> effect.weakened", "item.potion.healing_draught -> item.potion.minor_healing x5",
                "location.wolf_den -> location.den_mouth", "npc.fixture.warden -> npc.fixture.warden_sera x2",
                "spell.ember.firebolt -> spell.ember.bolt",
            },
            >= 8 => new[]
            {
                "creature.beast.ash_hound -> creature.beast.ash_ember_hound", "effect.weakness -> effect.weakened",
                "item.potion.healing_draught -> item.potion.minor_healing x5", "location.wolf_den -> location.den_mouth",
                "spell.ember.firebolt -> spell.ember.bolt",
            },
            7 => new[]
            {
                "effect.weakness -> effect.weakened",
                "item.potion.healing_draught -> item.potion.minor_healing x4", "location.wolf_den -> location.den_mouth",
                "spell.ember.firebolt -> spell.ember.bolt",
            },
            6 => new[]
            {
                "item.potion.healing_draught -> item.potion.minor_healing x4", "location.wolf_den -> location.den_mouth",
                "spell.ember.firebolt -> spell.ember.bolt",
            },
            5 => new[]
            {
                "item.potion.healing_draught -> item.potion.minor_healing x2", "location.wolf_den -> location.den_mouth",
                "spell.ember.firebolt -> spell.ember.bolt",
            },
            4 => new[] { "item.potion.healing_draught -> item.potion.minor_healing x2", "spell.ember.firebolt -> spell.ember.bolt" },
            _ => new[] { "item.potion.healing_draught -> item.potion.minor_healing" },
        };
        Assert.Equal(aliases, loaded.Report.Aliases);
        Assert.Equal(schema switch { >= 8 => 10, >= 6 => 9, >= 3 => 7, _ => 6 }, loaded.Report.CellsMatched);
        Assert.Empty(loaded.Report.Loss);
    }

    [Theory]
    [MemberData(nameof(ShippedSchemas))]
    public void Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState(int schema)
    {
        using var profile = Fixtures.Copy(schema);
        var store = new SaveStore(profile.Root);

        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));
        Assert.Equal(MigrationResult.Ready, report.Result);

        var reloaded = store.Load(SaveSlots.Quick, Fixtures.Context(new Registry()));
        Assert.Equal(MigrationResult.UpToDate, reloaded.Report.Result);
        Assert.Equal(File.ReadAllText(Fixtures.Expected(schema)).Replace("\r\n", "\n"), CanonicalState.Render(reloaded));
        Assert.Equal(SaveFormat.SchemaVersion, reloaded.Manifest.SchemaVersion);
        Assert.Equal(Fixtures.Content().Hash, reloaded.Manifest.ContentHash);

        // The original is kept, byte for byte, when a schema migration displaced it.
        if (schema < SaveFormat.SchemaVersion)
        {
            string original = Assert.Single(store.PreMigrationBackups(SaveSlots.Quick));
            Assert.Equal($"pre_migration_{schema}_quick", Path.GetFileName(original));
            foreach (string file in Directory.EnumerateFiles(Fixtures.Save(schema)))
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(original, Path.GetFileName(file))));
        }
    }
}
