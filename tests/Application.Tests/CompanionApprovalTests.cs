using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP15b over the game's own content (SYSTEMS_COMPLETENESS_PLAN §2.15): gear given or loaned in the same ten slots, and what comes back
/// when someone leaves or dies; a party member's level and skills from the fights and work they share; their values, touched by what the
/// character does; loyalty from days together and wages; going along, hesitating, refusing and leaving; banter on the road; a companion's
/// personal quest behind a trust gate; the life a member goes back to, behind an interface until WP12; and the party's regard and sheets
/// the same however an absence is cut (T-22).
/// </summary>
public class CompanionApprovalTests
{
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Cap = "item.armor.hide_cap";
    private const string Vest = "item.armor.hide_vest";
    private const string Bow = "item.weapon.hunting_bow";
    private const string Arrows = "item.ammo.arrow_rough";
    private const string Ash = "resource.wood.ash";

    /// <summary>Beside the ash stand (CraftingTests), clear of the waystation and the Foldscar.</summary>
    private static readonly (double X, double Z) AtStand = (180, 137);

    /// <summary>An open stretch east of the smithy, clear of everything (CompanionTests).</summary>
    private static readonly (double X, double Z) Open = (70, 152);

    private static CompanionRecord Joined(double x, double z, string npc = Tavar, CompanionOrder order = CompanionOrder.Follow, int health = 100) =>
        new(npc, order, CompanionCondition.Up, Harness.InRegion(x, z).XMm, Harness.InRegion(x, z).ZMm, 90_000, health);

    private static CompanionRecord Hired(string npc, double x, double z, HirelingRole role = HirelingRole.Guard, int days = 10) =>
        new(npc, CompanionOrder.Follow, CompanionCondition.Up, Harness.InRegion(x, z).XMm, Harness.InRegion(x, z).ZMm, 0, 60)
        {
            Membership = PartyMembership.Hireling,
            Contract = new HirelingContract(role, days, 8, PartyRules.WageOnly),
        };

    private static InventoryEntry Stack(string defId, int count = 1) => new(EntityId.NewId(EntityKind.Item), defId, count);

    private static PlayerRecord Carrying(PlayerRecord r, long coin, params InventoryEntry[] extra) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, coin,
            r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions) { PartySheets = r.PartySheets };

    private static PlayerRecord Regarded(PlayerRecord r, params (string Npc, string Dimension, int Value)[] values) =>
        r.WithSocial(values.Select(v => new RelationshipValue(v.Npc, v.Dimension, v.Value)), r.Conversations);

    /// <summary>The setup with these townsfolk hireable in every role, each with a spear (PartyTests).</summary>
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

    /// <summary>The setup with Tavar valuing the ash stand's wood: a harvest of it moves him on these dimensions.</summary>
    private static SimulationSetup ValuingAsh(SimulationSetup setup, params (string Dimension, int Delta)[] changes) => setup with
    {
        Social = setup.Social with
        {
            Npcs = setup.Social.Npcs.SetItem(Tavar, setup.Social.Npcs[Tavar] with
            {
                Values = ImmutableArray.Create(new ValueReaction(DeedKind.Harvested, ImmutableSortedSet.Create(StringComparer.Ordinal, Ash), null,
                    changes.ToImmutableSortedDictionary(c => c.Dimension, c => c.Delta, StringComparer.Ordinal), "Good wood, that.")),
            }),
        },
    };

    private static SimulationSetup Rules(SimulationSetup setup, Func<PartyRules, PartyRules> change) =>
        setup with { Social = setup.Social with { Party = change(setup.Social.Party!) } };

    private static Arena At(GameSession session, SimulationSetup setup, (double X, double Z) place, Func<PlayerRecord, PlayerRecord> change,
        IEnumerable<(string, double, double, string)>? creatures = null, long startTick = 0) =>
        Arena.OpenCreatures(session, setup, place, 90, creatures ?? Array.Empty<(string, double, double, string)>(), change, startTick: startTick);

    private static CompanionView Member(Arena arena, string npc = Tavar) => arena.Simulation.Companions.Single(c => c.NpcId == npc);

    private static int Regard(Simulation simulation, string npc, string dimension) =>
        simulation.CaptureRecord().Relationships.FirstOrDefault(r => r.NpcId == npc && r.Dimension == dimension)?.Value ?? 0;

    private static long Midnight(SimulationSetup setup, int day) => day * setup.Clock.TicksPerDay - setup.Clock.StartTicks;

    // ── gear ────────────────────────────────────────────────────────────────

    /// <summary>
    /// §2.15: the character gives (it becomes theirs) or loans (it comes back when they leave). What they wear is the armour they fight in; a
    /// gift is not asked back; a loan is, and comes back by itself when they are dismissed - with the identity it went with.
    /// </summary>
    [Fact]
    public void Gear_GivenOrLoaned_IsWornAndFoughtIn_ALoanComesBackWhenHeLeaves_AGiftStaysHis()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var cap = Stack(Cap);
        var vest = Stack(Vest);
        var arena = At(session, session.Setup, AtStand, r => Carrying(r, 0, cap, vest).WithCompanions(new[] { Joined(181.5, 137) }));
        var changed = arena.Record<PartyGearChanged>();

        Assert.Equal(new[] { (BodyRegion.Head, 0), (BodyRegion.Torso, 3), (BodyRegion.Limbs, 1) }, Member(arena).Armor.Select(a => (a.Key, a.Value)));
        Assert.Null(arena.Submit(new GiveGearCommand(arena.Player, Tavar, cap.ItemId, Loan: false)));
        Assert.Null(arena.Submit(new GiveGearCommand(arena.Player, Tavar, vest.ItemId, Loan: true)));

        Assert.Equal(new[] { (BodyRegion.Head, 3), (BodyRegion.Torso, 9), (BodyRegion.Limbs, 1) }, Member(arena).Armor.Select(a => (a.Key, a.Value)));
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.ItemId == cap.ItemId || e.ItemId == vest.ItemId);
        var sheet = arena.Simulation.PartySheet(Tavar)!;
        Assert.Equal(new[] { (EquipSlot.Head, Cap, false), (EquipSlot.Chest, Vest, true) }, sheet.Gear.Select(g => (g.Slot, g.DefId, g.Loaned)));
        Assert.Equal(new[] { (EquipSlot.Head, Cap, false), (EquipSlot.Chest, Vest, true) }, changed.Select(c => (c.Slot, c.ItemDefId!, c.Loaned)));
        Assert.Equal("Tavar Orr's item.armor.hide_cap was a gift: it is theirs", arena.Submit(new TakeBackGearCommand(arena.Player, Tavar, EquipSlot.Head)));
        Assert.Equal("Kera Voss is not with you", arena.Submit(new GiveGearCommand(arena.Player, Kera, cap.ItemId, Loan: true)));

        Assert.Null(arena.Submit(new DismissCommand(arena.Player, Tavar)));

        Assert.Contains(arena.Simulation.Player.Inventory, e => e.ItemId == vest.ItemId);   // the loan, back
        var kept = Assert.Single(arena.Simulation.CaptureRecord().PartySheets);
        Assert.Equal((Tavar, cap.ItemId), (kept.NpcId, Assert.Single(kept.Gear).Value.Item.ItemId));   // the gift, still his at home
        Assert.False(kept.Gear[EquipSlot.Head].Loaned);
    }

    /// <summary>A loan asked back comes back to the pack, and he fights in his own jerkin again.</summary>
    [Fact]
    public void ALoan_AskedBack_ComesBack_AndHisBodyIsHisOwnAgain()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var vest = Stack(Vest);
        var arena = At(session, session.Setup, AtStand, r => Carrying(r, 0, vest).WithCompanions(new[] { Joined(181.5, 137) }));

        Assert.Null(arena.Submit(new GiveGearCommand(arena.Player, Tavar, vest.ItemId, Loan: true)));
        Assert.Null(arena.Submit(new TakeBackGearCommand(arena.Player, Tavar, EquipSlot.Chest)));

        Assert.Contains(arena.Simulation.Player.Inventory, e => e.ItemId == vest.ItemId);
        Assert.Equal(3, Member(arena).Armor[BodyRegion.Torso]);
        Assert.Equal("Tavar Orr wears nothing in chest", arena.Submit(new TakeBackGearCommand(arena.Player, Tavar, EquipSlot.Chest)));
        Assert.EndsWith("is not carried", arena.Submit(new GiveGearCommand(arena.Player, Tavar, EntityId.NewId(EntityKind.Item), Loan: true)));
    }

    /// <summary>DN9 and §2.15: a hireling who dies drops their own gear where they fell; what they were loaned comes back to the character.</summary>
    [Fact]
    public void AHirelingWhoDies_DropsTheirOwnGearWhereTheyFell_AndTheLoanComesBack()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var cap = Stack(Cap);
        var vest = Stack(Vest);
        var down = Hired(Kera, 181.5, 137) with { Condition = CompanionCondition.Downed, Health = 0, DownedTick = 0 };
        var sheet = new PartySheet(Kera, CharacterProgression.Empty)
        {
            Gear = ImmutableSortedDictionary.CreateRange(new[]
            {
                KeyValuePair.Create(EquipSlot.Head, new PartyGear(cap, Loaned: false)),
                KeyValuePair.Create(EquipSlot.Chest, new PartyGear(vest, Loaned: true)),
            }),
        };
        var arena = At(session, Hireable(session.Setup, Kera), AtStand, r => r.WithCompanions(new[] { down }) with { PartySheets = ImmutableArray.Create(sheet) });

        arena.Tick(session.Setup.Social.Companions!.ReviveWindowTicks + 1);

        Assert.Contains(arena.Simulation.Player.Inventory, e => e.ItemId == vest.ItemId);
        var dropped = Assert.Single(arena.Simulation.WorldItems, i => i.Id == cap.ItemId);
        Assert.Equal((Harness.InRegion(181.5, 137).XMm, Harness.InRegion(181.5, 137).ZMm), (dropped.XMm, dropped.ZMm));
        Assert.Empty(arena.Simulation.CaptureRecord().PartySheets);
    }

    // ── progression ─────────────────────────────────────────────────────────

    /// <summary>§2.15: a fight he shares raises his level on the character's rules, and his spear-work his polearm skill - his kill, still not the character's.</summary>
    [Fact]
    public void AFightHeShares_RaisesHisLevelAndHisWeaponSkill()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, session.Setup, Open, r => Carrying(r, 0, Stack(Bow), Stack(Arrows, 10)).WithCompanions(new[] { Joined(72, 153) }),
            new[] { (Arena.Wolf, 76.0, 152.0, "pack_hunter") });
        var killed = arena.Record<CreatureKilled>();
        var gained = arena.Record<PartyExperienceGained>();

        var bow = arena.Simulation.Player.Inventory.Single(e => e.DefId == Bow).ItemId;
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, bow)));
        arena.Face(arena.Creature());
        arena.Tick();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 1_200 && killed.Count == 0; i++)
            arena.Tick();

        Assert.Single(killed);
        var xp = Assert.Single(gained);
        Assert.Equal(Tavar, xp.NpcId);
        Assert.True(xp.Awarded > 0);
        var sheet = arena.Simulation.PartySheet(Tavar)!;
        Assert.True(sheet.Level > 1 || sheet.LevelProgressXp > 0);
        Assert.True(sheet.Skills.GetValueOrDefault("skill.polearm") is { } polearm && (polearm.Level > 0 || polearm.ProgressXp > 0));
        Assert.Equal(0, arena.Simulation.Player.Progression.LifetimeXp.GetValueOrDefault(XpSource.Combat));
    }

    /// <summary>A level grants points the player spends for him; endurance adds health as it would to the character.</summary>
    [Fact]
    public void HisAttributePoints_AreSpentByThePlayer_AndEnduranceAddsHealth()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var sheet = new PartySheet(Tavar, ProgressionEngine.Create(session.Setup.Progression) with { Level = 2, UnspentAttributePoints = 1 });
        var arena = At(session, session.Setup, AtStand, r => r.WithCompanions(new[] { Joined(181.5, 137) }) with { PartySheets = ImmutableArray.Create(sheet) });

        Assert.Null(arena.Submit(new SpendPartyAttributeCommand(arena.Player, Tavar, CharacterAttribute.Endurance)));

        Assert.Equal((105, 105), (Member(arena).MaxHealth, Member(arena).Health));   // config.progression: endurance adds 5
        Assert.Equal(0, arena.Simulation.PartySheet(Tavar)!.UnspentPoints);
        Assert.Equal("Tavar Orr has no unspent attribute points", arena.Submit(new SpendPartyAttributeCommand(arena.Player, Tavar, CharacterAttribute.Might)));
    }

    // ── values, loyalty, going along ────────────────────────────────────────

    /// <summary>A deed done in his sight that touches one of his values moves him, and draws his remark; out of sight, nothing.</summary>
    [Fact]
    public void ADeedHeValues_DoneInHisSight_MovesHim_AndHeRemarksOnIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = ValuingAsh(session.Setup, ("respect", 3), ("trust", 1));
        var arena = At(session, setup, AtStand, r => r.WithCompanions(new[] { Joined(181.5, 137) }));
        var reacted = arena.Record<PartyMemberReacted>();
        string stand = arena.Simulation.Nodes.Single(n => n.Name == "ash_stand").Key;

        Assert.Null(arena.Submit(new GatherCommand(arena.Player, stand)));

        Assert.Equal((3, 1), (Regard(arena.Simulation, Tavar, "respect"), Regard(arena.Simulation, Tavar, "trust")));
        var reaction = Assert.Single(reacted);
        Assert.Equal(("reaction.ashen_hollow.tavar_orr.1", "Good wood, that."), (reaction.LineId, reaction.Remark));

        var away = At(session, setup, AtStand, r => r.WithCompanions(new[] { Joined(120, 137, order: CompanionOrder.Wait) }));
        Assert.Null(away.Submit(new GatherCommand(away.Player, stand)));
        Assert.Equal(0, Regard(away.Simulation, Tavar, "respect"));
    }

    /// <summary>
    /// Canon :54-58 - comply, hesitate, refuse, leave - read dimension by dimension: a grudge past the hesitate bound, he does it after a moment's
    /// doubt; past the refuse bound, he refuses; and when a deed pushes it past the leave bound, he goes home.
    /// </summary>
    [Fact]
    public void PastEachBound_HeHesitates_Refuses_OrLeaves()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = ValuingAsh(session.Setup, ("grudge", 20));
        var hesitated = new List<PartyMemberHesitated>();

        var doubtful = At(session, setup, AtStand, r => Regarded(r.WithCompanions(new[] { Joined(181.5, 137) }), (Tavar, "grudge", 50)));
        doubtful.Bus.Subscribe<PartyMemberHesitated>(hesitated.Add);
        Assert.Null(doubtful.Submit(new OrderCompanionCommand(doubtful.Player, Tavar, CompanionOrder.Wait)));
        Assert.Equal(CompanionOrder.Wait, Member(doubtful).Order);
        Assert.Equal((Tavar, "wait"), (Assert.Single(hesitated).NpcId, hesitated[0].Asked));

        var sullen = At(session, setup, AtStand, r => Regarded(r.WithCompanions(new[] { Joined(181.5, 137) }), (Tavar, "grudge", 75)));
        var left = sullen.Record<PartyMemberLeft>();
        Assert.Equal("Tavar Orr refuses", sullen.Submit(new OrderCompanionCommand(sullen.Player, Tavar, CompanionOrder.Wait)));
        Assert.Equal("Tavar Orr refuses", sullen.Submit(new SetStanceCommand(sullen.Player, Tavar, CompanionStance.Passive)));
        Assert.Equal(CompanionOrder.Follow, Member(sullen).Order);
        Assert.Equal(Disposition.Refuse, sullen.Simulation.PartySheet(Tavar)!.Disposition);
        Assert.Equal("Tavar Orr holds a grudge against you.", sullen.Simulation.PartySheet(Tavar)!.Regard);

        Assert.Null(sullen.Submit(new GatherCommand(sullen.Player, sullen.Simulation.Nodes.Single(n => n.Name == "ash_stand").Key)));   // grudge 95

        Assert.Equal((Tavar, "left"), (Assert.Single(left).NpcId, left[0].Reason));
        Assert.Empty(sullen.Simulation.Companions);
    }

    /// <summary>Loyalty grows a point a midnight on his feet in the party; a hireling's with each wage paid, and falls with each one owed.</summary>
    [Fact]
    public void Loyalty_GrowsWithDaysTogether_AndAHirelingsWithWagesPaid_AndFallsWithWagesOwed()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, Hireable(session.Setup, Kera), AtStand,
            r => Carrying(r, 16, Array.Empty<InventoryEntry>()).WithCompanions(new[] { Joined(181.5, 137), Hired(Kera, 182, 138) }));

        arena.Simulation.Advance(Midnight(session.Setup, 2));   // two midnights: 16 coin pays both wages

        Assert.Equal(2, Regard(arena.Simulation, Tavar, "loyalty"));
        Assert.Equal(2, Regard(arena.Simulation, Kera, "loyalty"));

        arena.Simulation.Advance(session.Setup.Clock.TicksPerDay);   // a third, with nothing in the purse: owed

        Assert.Equal(3, Regard(arena.Simulation, Tavar, "loyalty"));
        Assert.Equal(2 - 5, Regard(arena.Simulation, Kera, "loyalty"));
    }

    // ── banter ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Party banter: at a banter boundary, Tavar following on his feet says a line whose conditions hold - here, away from every place he
    /// remarks on, the one he says while following - and nothing more while the party keeps quiet. By chance, so never at chance 0.
    /// </summary>
    [Fact]
    public void Banter_AtABoundary_HeSaysALineWhoseConditionsHold_ThenKeepsQuiet()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var talkative = Rules(session.Setup, p => p with { Banter = new BanterRules(20, 100, 200) });
        var arena = At(session, talkative, AtStand, r => r.WithCompanions(new[] { Joined(181.5, 137) }));
        var said = arena.Record<BanterSpoken>();

        arena.Tick(100);

        var line = Assert.Single(said);
        Assert.Equal(("banter.ashen_hollow.tavar_orr.4", "If I fall behind, it's the mud. Not the other thing."), (line.LineId, line.Text));
        Assert.Equal(20, line.Tick);
        arena.Tick(120);
        Assert.Equal(2, said.Count);   // the quiet over, at the first boundary after it
        Assert.Equal(220, said[1].Tick);

        var silent = At(session, Rules(session.Setup, p => p with { Banter = new BanterRules(20, 0, 0) }), AtStand, r => r.WithCompanions(new[] { Joined(181.5, 137) }));
        var none = silent.Record<BanterSpoken>();
        silent.Tick(200);
        Assert.Empty(none);
    }

    // ── a personal quest ────────────────────────────────────────────────────

    /// <summary>
    /// The framework, with a quest of the test's own (no new lore): a companion's personal quest starts only once their trust reaches its
    /// "confides" gate; finished, its approval change moves what they think.
    /// </summary>
    [Fact]
    public void APersonalQuest_IsConfidedOnlyAtItsTrust_AndItsApprovalChangeMovesHim()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string QuestId = "quest.test.ledger", DialogueId = "dialogue.test.ledger";
        var quest = new QuestDefinition(QuestId, "The Ledger", "Hear him out.", Tavar, "o_hear",
            ImmutableArray.Create(new ObjectiveDefinition("o_hear", "Hear him out.", new TalkTo(Tavar, DialogueId, ImmutableArray.Create("told")),
                ImmutableArray<string>.Empty)),
            ImmutableArray<ObjectiveCondition>.Empty,
            ImmutableArray.Create<QuestReward>(new ApprovalChangeReward(ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
                new[] { KeyValuePair.Create("loyalty", 10), KeyValuePair.Create("trust", 5) }))))
        {
            CompanionId = Tavar,
            ConfidesAt = 30,
        };
        var dialogue = new DialogueDefinition(DialogueId, ImmutableArray.Create(Tavar), "ask", new[]
        {
            new DialogueNode("ask", "Something on your mind?", ImmutableArray.Create(
                new DialogueChoice("ledger", "Tell me about the ledger.",
                    ImmutableArray.Create<DialogueCondition>(new QuestStateCondition(QuestId, null, QuestKeys.Available, false)),
                    ImmutableArray.Create<DialogueConsequence>(new StartQuestConsequence(QuestId)), "told")), null, false, null),
            new DialogueNode("told", "Not much to tell. Yet.", ImmutableArray<DialogueChoice>.Empty, null, false, null),
        }.ToImmutableSortedDictionary(n => n.Id, n => n, StringComparer.Ordinal));
        var setup = session.Setup with
        {
            Social = session.Setup.Social with
            {
                Npcs = session.Setup.Social.Npcs.SetItem(Tavar, session.Setup.Social.Npcs[Tavar] with { DialogueId = DialogueId }),
                Dialogues = session.Setup.Social.Dialogues.SetItem(DialogueId, dialogue),
            },
            Quests = new QuestSetup(session.Setup.Quests.Quests.SetItem(QuestId, quest)),
        };

        var wary = At(session, setup, AtStand, r => Regarded(r.WithCompanions(new[] { Joined(181.5, 137) }), (Tavar, "trust", 29)));
        Assert.Null(wary.Submit(new TalkCommand(wary.Player, Tavar)));
        Assert.NotNull(wary.Submit(new ChooseCommand(wary.Player, "ledger")));   // not offered: he does not confide it yet
        Assert.DoesNotContain(wary.Simulation.CaptureRecord().Quests, q => q.QuestId == QuestId);

        var trusted = At(session, setup, AtStand, r => Regarded(r.WithCompanions(new[] { Joined(181.5, 137) }), (Tavar, "trust", 30)));
        Assert.Null(trusted.Submit(new TalkCommand(trusted.Player, Tavar)));
        Assert.Null(trusted.Submit(new ChooseCommand(trusted.Player, "ledger")));
        trusted.Tick(2);

        Assert.Equal(QuestStatus.Completed, trusted.Simulation.CaptureRecord().Quests.Single(q => q.QuestId == QuestId).Status);
        Assert.Equal((35, 10), (Regard(trusted.Simulation, Tavar, "trust"), Regard(trusted.Simulation, Tavar, "loyalty")));
    }

    // ── the life beyond the party ───────────────────────────────────────────

    private sealed class RecordingLife : IPartyLife
    {
        public List<PartyLifeEntry> TakenUp { get; } = new();
        public List<string> Released { get; } = new();

        public void TakeUp(PartyLifeEntry entry) => TakenUp.Add(entry);

        public void Release(string npcId, long tick) => Released.Add(npcId);
    }

    /// <summary>
    /// §2.15's persistent world, behind <see cref="IPartyLife"/> until WP12: someone dismissed is handed to the life simulation with their sheet,
    /// and someone who joins is let go of.
    /// </summary>
    [Fact]
    public void Dismissed_HeIsHandedToTheLifeSimulation_WithHisSheet_AndOneWhoJoinsIsLetGo()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var sheet = new PartySheet(Tavar, ProgressionEngine.Create(session.Setup.Progression) with { Level = 3 });
        var seed = At(session, session.Setup, AtStand, r => r.WithCompanions(new[] { Joined(181.5, 137) }) with { PartySheets = ImmutableArray.Create(sheet) });
        var life = new RecordingLife();
        var bus = new EventBus();
        var simulation = Simulation.Start(session.Setup, seed.Simulation.CaptureRecord(), new WorldDelta(session.Generator, 42, new Registry()), 0, bus, life);

        simulation.Enqueue(new DismissCommand(simulation.PlayerId, Tavar));
        simulation.DrainCommands();

        var entry = Assert.Single(life.TakenUp);
        Assert.Equal((Tavar, PartyMembership.Companion, "dismissed", 3), (entry.NpcId, entry.Membership, entry.Reason, entry.Sheet!.Progression.Level));

        Assert.Equal(3, simulation.PartySheet(Tavar)!.Level);   // his level kept, at home

        // Joining - here a hire, beside Kera at the smithy (PartyTests) - the life simulation lets them go. (Tavar's own home lies in the
        // Foldscar, beyond its barrier in this world.)
        var hiring = At(session, Hireable(session.Setup, Kera), (60.3, 140.3), r => Carrying(r, 50));
        var hired = Simulation.Start(hiring.Simulation.Setup, hiring.Simulation.CaptureRecord(), new WorldDelta(session.Generator, 42, new Registry()), 0, bus, life);
        hired.Enqueue(new HireCommand(hired.PlayerId, Kera, HirelingRole.Guard, 3, AcceptsRisk: true));
        hired.DrainCommands();

        Assert.Equal(new[] { Kera }, life.Released);
    }

    // ── T-22 ────────────────────────────────────────────────────────────────

    /// <summary>Everything of the party's regard and sheets: every relationship, and each sheet's progression and gear.</summary>
    private static string Seen(Simulation simulation)
    {
        var r = simulation.CaptureRecord();
        return string.Join(" | ", simulation.WorldTick, r.Currency,
            string.Join(";", r.Relationships.Select(v => $"{v.NpcId}:{v.Dimension}:{v.Value}")),
            string.Join(";", r.Companions.Select(c => $"{c.NpcId}:{c.Condition}:{c.Health}")),
            string.Join(";", r.PartySheets.Select(p => $"{p.NpcId}:{p.Progression.Digest}:{string.Join(",", p.Gear.Select(g => $"{g.Key}={g.Value.Item.DefId}"))}")),
            string.Join(";", r.Inventory.GroupBy(e => e.DefId).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}:{g.Sum(e => e.Count)}")));
    }

    /// <summary>
    /// The party's regard and sheets while the character is away, the same however the absence is cut: Tavar on his feet growing loyal, a
    /// gatherer paid and practising until the purse runs dry, and a guard down with a loan, who dies and gives it back.
    /// </summary>
    [Fact]
    public void T22_TheRegardAndTheSheets_TenDaysAtOnce_AreTheSameAsDayByDay_AndAsAnyOtherCut()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = Hireable(session.Setup, Kera, Renn);
        var stand = setup.Layout.Nodes.First(n => n.Name == "ash_stand");
        var cell = CellKey.OfWorld(stand.XMm / 1000.0, stand.ZMm / 1000.0);
        var gatherer = Hired(Kera, 179, 137, HirelingRole.Gatherer, 20) with { Order = CompanionOrder.Wait };
        gatherer = gatherer with { Contract = gatherer.Contract! with { WorkSite = Keys.NodeKey(cell, stand.Name, 0) } };
        var guard = Hired(Renn, 181, 138) with { Condition = CompanionCondition.Downed, Health = 0, DownedTick = 0 };
        var vest = Stack(Vest);
        Arena Fresh() => At(session, setup, AtStand, r => Carrying(r, 30).WithCompanions(new[] { Joined(181.5, 137), gatherer, guard }) with
        {
            PartySheets = ImmutableArray.Create(new PartySheet(Renn, CharacterProgression.Empty)
            {
                Gear = ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(EquipSlot.Chest, new PartyGear(vest, Loaned: true)) }),
            }),
        });
        long day = setup.Clock.TicksPerDay, total = 10 * day;

        var once = Fresh();
        var daily = Fresh();
        var cut = Fresh();
        Assert.Equal(Seen(once.Simulation), Seen(cut.Simulation));

        once.Simulation.Advance(total);
        for (int i = 0; i < 10; i++)
            daily.Simulation.Advance(day);
        foreach (long piece in new[] { 1L, day - 2, 3, 3 * day + 11, day / 3, total - (1 + (day - 2) + 3 + (3 * day + 11) + day / 3) })
            cut.Simulation.Advance(piece);

        Assert.Equal(Seen(once.Simulation), Seen(daily.Simulation));
        Assert.Equal(Seen(once.Simulation), Seen(cut.Simulation));

        // And it did what was due: Tavar ten days more loyal, the gatherer's skill grown, the guard dead and the loan back.
        Assert.Equal(10, Regard(once.Simulation, Tavar, "loyalty"));
        var record = once.Simulation.CaptureRecord();
        Assert.Contains(record.Inventory, e => e.ItemId == vest.ItemId);
        Assert.DoesNotContain(record.PartySheets, p => p.NpcId == Renn);
        Assert.Contains(record.PartySheets, p => p.NpcId == Kera && !p.Progression.Skills.IsEmpty);
    }
}
