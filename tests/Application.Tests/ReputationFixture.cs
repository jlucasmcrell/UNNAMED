using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The reputation proof's two settings (M7 design §5.6.4, §5.15). <see cref="Walk"/> is the P script: shipped content, dialogue and
/// gates, the armour killed in an arena and everything after it walked and said through public commands. <see cref="Fixture"/> is the F
/// rows' world: two fixture factions over the waystation's people, fixture dialogues that report wolf kills and a stone, sleeping
/// one-blow wolves, and Sel moved beside Kera so one spot reaches both.
/// </summary>
internal static class ReputationFixture
{
    public const string Armour = "creature.construct.animated_armour";
    public const string Heart = "world.foldscar.steadied";
    public const string Waystation = "faction.ashen_hollow.waystation";
    public const string Survey = "faction.ashen_hollow.survey";
    public const string Kera = "npc.ashen_hollow.kera_voss";
    public const string Sel = "npc.ashen_hollow.sel_arien";
    public const string Renn = "npc.ashen_hollow.renn_vale";
    public const string Tavar = "npc.ashen_hollow.tavar_orr";
    public const string KeraWares = "merchant.ashen_hollow.kera_voss";
    public const string Billet = "item.material.iron_ingot";

    // ── the P script ───────────────────────────────────────────────────────────

    private static readonly (double X, double Z)[] ToTheNorthStone = { (148.5, 78) };
    private static readonly (double X, double Z)[] ToTheSouthWestStone = { (135, 60), (123.5, 38) };
    private static readonly (double X, double Z)[] ToTheSouthEastStone = { (140, 62), (160, 65), (187, 65), (194, 45), (182.5, 31) };
    private static readonly (double X, double Z)[] ToTheHeart = { (194, 45), (187, 65), (160, 65), (153, 50.2) };
    private static readonly (double X, double Z)[] ToTavar = { (145, 43.3) };
    private static readonly (double X, double Z)[] HomeWithTavar = { (150, 45), (135, 70), (100, 100), (80, 118), (62, 130), (48, 137) };
    private static readonly (double X, double Z)[] ToTheSmithyDoor = { (51.8, 139), (51.8, 142) };
    private static readonly (double X, double Z)[] IntoTheSmithy = { (54.5, 142), (60.3, 140.3) };
    private static readonly (double X, double Z)[] OutToSel = { (54.5, 142), (51.8, 142), (51.8, 136), (60, 128), (71, 123) };

    /// <summary>What the P script saw, step by step: the faction events as they came, and the state at each checkpoint.</summary>
    public sealed class Run
    {
        public required GameSession Session { get; init; }
        public required Arena Arena { get; init; }

        /// <summary>The character as the script began: a replay starts from it, in the same arena.</summary>
        public required PlayerRecord Start { get; init; }

        public List<ActRecorded> Acts { get; } = new();
        public List<FactionLearned> Learned { get; } = new();
        public List<ReputationChanged> Changes { get; } = new();
        public List<RelationshipChanged> Regard { get; } = new();
        public Dictionary<string, (ImmutableArray<FactionView> Factions, ImmutableArray<ActView> Acts)> At { get; } = new();
        public Dictionary<string, string[]> Replies { get; } = new();
        public Dictionary<string, WaresView> Wares { get; } = new();
        public string? RawBilletBuyBeforeTelling { get; set; }
        public string? BilletBoughtAtP3 { get; set; }

        public int Points(string step, string faction) => At[step].Factions.Single(f => f.Id == faction).Points;

        public string Tier(string step, string faction) => At[step].Factions.Single(f => f.Id == faction).Tier;

        /// <summary>The same record of events, listening on another arena (a resumed or replayed one).</summary>
        public Run On(Arena arena) => Listen(new Run { Session = Session, Arena = arena, Start = Start });
    }

    /// <summary>The P script through step <paramref name="through"/> (1 to 5). Stops at the first failure with its reason.</summary>
    public static Run Walk(GameSession session, int through = 5)
    {
        var run = Begin(session);
        for (int step = 1; step <= through; step++)
            Step(run, step);
        return run;
    }

    /// <summary>
    /// The P script's arena (L8): the shipped armour as a sentinel at (120, 63), the character 1.8 m behind it along its facing with the
    /// march spear and 40 coin, seed 42; the arena's spawn list replaces the shipped spawns. <paramref name="start"/> replays a record.
    /// </summary>
    public static Run Begin(GameSession session, PlayerRecord? start = null)
    {
        var armour = (X: 120.0, Z: 63.0);
        double facing = FacingOf(session, armour);
        var behind = (X: armour.X - Math.Sin(facing) * 1.8, Z: armour.Z - Math.Cos(facing) * 1.8);
        var arena = Arena.OpenCreatures(session, session.Setup, behind, (int)Math.Round(facing * 180 / Math.PI + 360) % 360,
            new[] { (Armour, armour.X, armour.Z, "sentinel") }, r => start ?? ArmedWithCoin(r, "item.weapon.march_spear", 40));
        return Listen(new Run { Session = session, Arena = arena, Start = arena.Simulation.CaptureRecord() });
    }

    private static Run Listen(Run run)
    {
        run.Arena.Bus.Subscribe<ActRecorded>(run.Acts.Add);
        run.Arena.Bus.Subscribe<FactionLearned>(run.Learned.Add);
        run.Arena.Bus.Subscribe<ReputationChanged>(run.Changes.Add);
        run.Arena.Bus.Subscribe<RelationshipChanged>(run.Regard.Add);
        return run;
    }

    /// <summary>One step of the P script (§5.6.4).</summary>
    public static void Step(Run run, int step)
    {
        var arena = run.Arena;
        switch (step)
        {
            case 1:   // the kill; nobody knows
                arena.Fight(arena.Simulation.Creatures.Single(), 600, stayPut: true);
                Require(!arena.Simulation.Creatures.Single().Alive, "P1: the armour still stands");
                run.Wares["P1"] = arena.Simulation.Wares(Kera)!;
                break;
            case 2:   // the heart; Tavar found and left where he stands
                Travel(arena, "P2", (140, 80));
                Travel(arena, "P2", ToTheNorthStone);
                Work(arena, "P2", "switch.stone_north");
                Travel(arena, "P2", ToTheSouthWestStone);
                Work(arena, "P2", "switch.stone_southwest");
                Travel(arena, "P2", ToTheSouthEastStone);
                Work(arena, "P2", "switch.stone_southeast");
                Travel(arena, "P2", ToTheHeart);
                Work(arena, "P2", "switch.foldscar_heart");
                Travel(arena, "P2", ToTavar);
                Say(arena, "P2", new TalkCommand(arena.Player, Tavar));
                Say(arena, "P2", new ChooseCommand(arena.Player, "found"));
                arena.Submit(new LeaveCommand(arena.Player));
                break;
            case 3:   // Kera told of the armour; a billet bought
                Travel(arena, "P3", HomeWithTavar);
                Travel(arena, "P3", ToTheSmithyDoor);
                Work(arena, "P3", "door.forge_shed");
                Travel(arena, "P3", IntoTheSmithy);
                run.RawBilletBuyBeforeTelling = arena.Submit(new BuyCommand(arena.Player, Kera, UntouchedBilletRef(run.Session), 1));
                Say(arena, "P3", new TalkCommand(arena.Player, Kera));
                Say(arena, "P3", new ChooseCommand(arena.Player, "leave"));
                Say(arena, "P3", new TalkCommand(arena.Player, Kera));
                run.Replies["P3 Kera again"] = Offered(arena);
                Say(arena, "P3", new ChooseCommand(arena.Player, "armour"));
                Say(arena, "P3", new ChooseCommand(arena.Player, "back"));
                arena.Submit(new LeaveCommand(arena.Player));
                run.Wares["P3"] = arena.Simulation.Wares(Kera)!;
                var billet = run.Wares["P3"].Wares.FirstOrDefault(w => w.ItemId == Billet);
                Require(billet is not null, "P3: the billets are not on offer after Kera was told");
                run.BilletBoughtAtP3 = arena.Submit(new BuyCommand(arena.Player, Kera, billet!.Ref, 1));
                break;
            case 4:   // Sel told Tavar is back: the Survey learns of the heart
                Travel(arena, "P4", OutToSel);
                Say(arena, "P4", new TalkCommand(arena.Player, Sel));
                run.Replies["P4 Sel greet"] = Offered(arena);
                Say(arena, "P4", new ChooseCommand(arena.Player, "tavar_back"));
                Say(arena, "P4", new ChooseCommand(arena.Player, "back"));
                run.Replies["P4 Sel again"] = Offered(arena);
                break;
            case 5:   // Sel told of the armour: the Survey drops back to neutral, and the notes close
                if (arena.Simulation.Conversation is null)
                    Say(arena, "P5", new TalkCommand(arena.Player, Sel));
                Say(arena, "P5", new ChooseCommand(arena.Player, "armour"));
                Say(arena, "P5", new ChooseCommand(arena.Player, "back"));
                run.Replies["P5 Sel again"] = Offered(arena);
                arena.Submit(new LeaveCommand(arena.Player));
                break;
        }
        Checkpoint(run, $"P{step}");
    }

    /// <summary>A character with the weapon in hand and coin in the purse: the P script buys a billet at 20 coin.</summary>
    public static PlayerRecord ArmedWithCoin(PlayerRecord r, string weapon, long coin)
    {
        var inventory = r.Inventory.ToList();
        if (inventory.All(e => e.DefId != weapon))
            inventory.Add(Arena.Stack(weapon, 1));
        var item = inventory.Single(e => e.DefId == weapon).ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg, r.Discoveries,
            new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, coin, r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions)
        {
            Posture = r.Posture,
            Factions = r.Factions,
        };
    }

    /// <summary>
    /// Kera's billet ware ref while her wares are untouched (§5.7.2, #05): her stock split into stacks by each item's <c>stack_max</c>, in
    /// row order. Computed from the stock rows and the catalogue, never hard-coded.
    /// </summary>
    public static string UntouchedBilletRef(GameSession session)
    {
        var merchant = session.Setup.Items.Merchants[KeraWares];
        int index = 0;
        foreach (var row in merchant.Stock)
        {
            int stackMax = Math.Max(1, session.Setup.Items.Catalog.Get(row.ItemId).StackMax);
            if (row.ItemId == Billet)
                return $"{KeraWares}#{index:00}";
            index += (row.Count + stackMax - 1) / stackMax;
        }
        throw new InvalidOperationException("Kera stocks no billet");
    }

    private static double FacingOf(GameSession session, (double X, double Z) at) =>
        Arena.OpenCreatures(session, session.Setup, (at.X, at.Z - 60), 0, new[] { (Armour, at.X, at.Z, "sentinel") })
            .Simulation.Creatures.Single().Body.FacingMdeg / 1000.0 * Math.PI / 180;

    private static void Checkpoint(Run run, string step) => run.At[step] = (run.Arena.Simulation.Factions, run.Arena.Simulation.Acts);

    public static string[] Offered(Arena arena) => arena.Simulation.Conversation?.Replies.Select(r => r.Id).ToArray() ?? Array.Empty<string>();

    private static void Travel(Arena arena, string step, params (double X, double Z)[] path)
    {
        foreach (var (x, z) in path)
            Require(arena.WalkTo(x, z), $"{step}: the walk to ({x}, {z}) stopped at {arena.Simulation.Player.Body}");
    }

    private static void Work(Arena arena, string step, string key)
    {
        string? refused = arena.Submit(new InteractCommand(arena.Player, key));
        Require(refused is null, $"{step}: {key}: {refused}");
    }

    private static void Say(Arena arena, string step, GameCommand command)
    {
        string? refused = arena.Submit(command);
        Require(refused is null, $"{step}: {command}: {refused}");
    }

    private static void Require(bool holds, string why)
    {
        if (!holds)
            throw new InvalidOperationException(why);
    }

    // ── the F rows' world ──────────────────────────────────────────────────────

    public const string Keepers = "faction.fixture.keepers";
    public const string Delvers = "faction.fixture.delvers";
    public const string Watchers = "faction.fixture.watchers";
    public const string Wolf = "creature.beast.wolf_grey";
    public const string Stone = "world.foldscar.stone_north_aligned";

    /// <summary>Where the character acts from in the F rows: 1.48 m from Kera and from Sel's moved site.</summary>
    public static readonly (double X, double Z) Spot = (60.3, 140.3);

    /// <summary>
    /// The F rows' setup (§5.15): keepers (Renn, Kera) react +100 to a wolf kill; delvers (Sel) -100 to it and +100 to the north stone;
    /// relations as given (cordial both ways by default). Kera and Sel speak fixture dialogues whose root offers <c>wolf</c> (and Sel's
    /// <c>stone</c>) with no visited guard, so a report can be repeated. Wolves are one-blow sleepers. <paramref name="watchers"/> adds
    /// a third faction that reacts to the wolf and has no member (F12).
    /// </summary>
    public static SimulationSetup Fixture(GameSession session, int capacity = 256, string keepersToDelvers = "cordial", string delversToKeepers = "cordial",
        bool watchers = false, (double X, double Z, int FacingDeg)? selAt = null)
    {
        var setup = session.Setup;
        var keepers = new FactionDefinition(Keepers, "the keepers", "location.outpost",
            ImmutableArray.Create(new Reaction(ActKinds.CreatureKilled, Wolf, 100)), ImmutableArray.Create(new Relation(Delvers, keepersToDelvers)));
        var delvers = new FactionDefinition(Delvers, "the delvers", "location.outpost",
            ImmutableArray.Create(new Reaction(ActKinds.CreatureKilled, Wolf, -100), new Reaction(ActKinds.SwitchSet, Stone, 100)),
            ImmutableArray.Create(new Relation(Keepers, delversToKeepers)));
        var factions = new[] { keepers, delvers }.ToList();
        if (watchers)
            factions.Add(new FactionDefinition(Watchers, "the watchers", "location.outpost",
                ImmutableArray.Create(new Reaction(ActKinds.CreatureKilled, Wolf, 100)), ImmutableArray<Relation>.Empty));

        var npcs = setup.Social.Npcs
            .SetItem(Renn, setup.Social.Npcs[Renn] with { FactionId = Keepers })
            .SetItem(Kera, setup.Social.Npcs[Kera] with { FactionId = Keepers, DialogueId = "dialogue.fixture.kera" })
            .SetItem(Sel, setup.Social.Npcs[Sel] with { FactionId = Delvers, DialogueId = "dialogue.fixture.sel" });
        var dialogues = setup.Social.Dialogues
            .SetItem("dialogue.fixture.kera", Reporting("dialogue.fixture.kera", Kera, (ActKinds.CreatureKilled, Wolf, "wolf")))
            .SetItem("dialogue.fixture.sel", Reporting("dialogue.fixture.sel", Sel, (ActKinds.CreatureKilled, Wolf, "wolf"), (ActKinds.SwitchSet, Stone, "stone")));
        var sel = selAt ?? (59.0, 139.6, 90);
        var layout = setup.Layout with
        {
            Npcs = setup.Layout.Npcs.Select(n => n.NpcId == Sel ? n with { XMm = (long)(sel.X * 1000), ZMm = (long)(sel.Z * 1000), FacingMdeg = sel.FacingDeg * 1000 } : n)
                .ToImmutableArray(),
        };
        var creatures = setup.Combat.Creatures.SetItem(Wolf, setup.Combat.Creatures[Wolf] with { MaxHealth = 1 });
        return setup with
        {
            Layout = layout,
            Social = setup.Social with { Npcs = npcs, Dialogues = dialogues },
            Combat = setup.Combat with { Creatures = creatures },
            Factions = new FactionSetup(factions.ToImmutableSortedDictionary(f => f.Id, f => f, StringComparer.Ordinal), setup.Factions.Ladder, capacity),
        };
    }

    /// <summary>A fixture dialogue whose one node offers each report, and <c>leave</c>.</summary>
    private static DialogueDefinition Reporting(string id, string npc, params (string Kind, string Subject, string Reply)[] reports)
    {
        var choices = reports.Select(r => new DialogueChoice(r.Reply, $"About the {r.Reply}.",
                ImmutableArray.Create<DialogueCondition>(new ActDoneCondition(r.Kind, r.Subject)),
                ImmutableArray.Create<DialogueConsequence>(new ReportActConsequence(r.Kind, r.Subject)), "root"))
            .Append(new DialogueChoice("leave", "Goodbye.", ImmutableArray<DialogueCondition>.Empty, ImmutableArray<DialogueConsequence>.Empty, null))
            .ToImmutableArray();
        var root = new DialogueNode("root", "Well?", choices, null, false, null);
        return new DialogueDefinition(id, ImmutableArray.Create(npc), "root",
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create("root", root) }));
    }

    /// <summary>An F-row arena: the character at <see cref="Spot"/>, the given one-blow wolves asleep.</summary>
    public static Arena Open(GameSession session, SimulationSetup setup, IEnumerable<(double X, double Z)> wolves,
        Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, setup, Spot, 0, wolves.Select(w => (Wolf, w.X, w.Z, "sleeper")), change);

    /// <summary>Kill a wolf, then come back to the spot.</summary>
    public static void Kill(Arena arena, CreatureView wolf)
    {
        arena.Fight(wolf, 600);
        Require(!arena.Simulation.Creatures.Single(c => c.Key == wolf.Key).Alive, $"{wolf.Key} still lives");
        Require(arena.WalkTo(Spot.X, Spot.Z), $"the walk back to the spot stopped at {arena.Simulation.Player.Body}");
    }

    /// <summary>Tell an NPC of an act in their fixture dialogue: talk, choose the report, leave.</summary>
    public static void Tell(Arena arena, string npc, string reply)
    {
        Require(arena.Submit(new TalkCommand(arena.Player, npc)) is null, $"{npc} will not talk");
        string? refused = arena.Submit(new ChooseCommand(arena.Player, reply));
        Require(refused is null, $"{npc} {reply}: {refused}");
        arena.Submit(new LeaveCommand(arena.Player));
    }
}
