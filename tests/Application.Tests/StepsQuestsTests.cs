// UNNAMED Application tests - the Salt Steps' four side quests, each played end to end in region.otherhome: the Long Survey's ledger,
// the Exchange's amended tariff, the Concord's sighting, and the Assize's chain.

using UNNAMED.Domain;
using UNNAMED.Domain.Quests;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Four new side quests at the Steps (steps-side-quests), for a character of levels 3-6: one reading with a pack in it, one round of the
/// district's institutions, one sighting taken at the harbour mouth, and one chain mended so the Assize's line holds again. Every quest
/// starts from its giver's own line and ends with that giver, and each test drives the whole of it, quest state and all.
///
/// The district is six terraces joined by stairs, and the simulation's walker goes in a straight line: crossing it on foot through
/// <see cref="Arena.WalkTo"/> stops at the first terrace wall, so travelling between terraces here is done the way the acceptance
/// walkthrough does it - the character is written into a save standing elsewhere and the save is loaded, which is also what proves the
/// quest's state survives a save. (The missing piece is the climb/vault content the area dossier asks for; noted as an owner question.)
/// </summary>
public class StepsQuestsTests
{
    private const string Archivist = "npc.otherhome.survey_archivist";
    private const string Clerk = "npc.otherhome.exchange_clerk";
    private const string Sergeant = "npc.otherhome.assize_sergeant";
    private const string Smith = "npc.otherhome.harbour_smith";
    private const string Geometer = "npc.otherhome.concord_geometer";
    private const string Porter = "npc.otherhome.porter";

    private const string LedgerStops = "quest.otherhome.ledger_stops";
    private const string WeekIsTheFee = "quest.otherhome.week_is_the_fee";
    private const string WhatTheGroundDid = "quest.otherhome.what_the_ground_did";
    private const string HeldByTheChain = "quest.otherhome.held_by_the_chain";

    private static readonly (double X, double Z) Cistern = (1532, 684);
    private static readonly (double X, double Z) Ring = (1312, 594);
    private static readonly (double X, double Z) Quay = (1482, 640);

    /// <summary>A run of one test: the session, and an arena that follows whatever save is loaded.</summary>
    private sealed class Run : IDisposable
    {
        private readonly TempProfile _profile;
        private readonly SaveStore _store;

        public Run(TempProfile profile, Func<PlayerRecord, PlayerRecord>? change = null)
        {
            _profile = profile;
            Session = Harness.Boot(profile, "region.otherhome");
            _store = new SaveStore(Session.SaveFolder);
            Opened = Arena.OpenCreatures(Session, Session.Setup, Quay, 0, Array.Empty<(string, double, double, string)>(), change,
                origin: Harness.Region);
        }

        private Run(TempProfile profile, GameSession session, Arena arena)
        {
            _profile = profile;
            Session = session;
            _store = new SaveStore(Session.SaveFolder);
            Opened = arena;
        }

        public static Run On(TempProfile profile, GameSession session, Arena arena) => new(profile, session, arena);

        public GameSession Session { get; private set; }
        public Arena Opened { get; private set; }
        public Simulation Sim => Opened.Simulation;

        /// <summary>The copy helper the other quest tests use: the same character, carrying more.</summary>
        public static PlayerRecord Carrying(PlayerRecord r, params InventoryEntry[] items) =>
            r.WithInventory(r.Inventory.AddRange(items));

        /// <summary>Stand beside the NPC's own body, by a save: the world written with the character moved and read back.</summary>
        public void At(string npcId)
        {
            var body = Sim.Npcs.Single(n => n.Id == npcId).Body;
            At((body.XMm / 1000.0, body.ZMm / 1000.0));
        }

        public void At((double X, double Z) place)
        {
            var record = Sim.CaptureRecord();
            // The arena's origin is the region's own, so a place in region metres is the record's millimetres.
            var (x, z) = ((long)Math.Round(place.X * 1000), (long)Math.Round(place.Z * 1000));
            var moved = new PlayerRecord(record.Id, record.Name, x, Sim.ActiveLayout.Space.Terrain.HeightAtMm(x, z), z,
                record.AppearanceSeed, record.Inventory, record.Progression, record.FacingMdeg, record.Discoveries, record.Equipment,
                record.Currency, record.Effects, record.Relationships, record.Conversations, record.Quests, record.Companions)
            {
                Posture = record.Posture,
                Waystones = record.Waystones,
                ReturnPoint = record.ReturnPoint,
            };
            _store.Save(SaveSlots.Manual("steps"), SaveDocuments.Capture(Sim.World, moved, Session.Content, Sim.WorldTick,
                Session.WorldTimeAdvancedTicks));
            Opened = Arena.Resume(Session.Setup, _store.Load(SaveSlots.Manual("steps"),
                new LoadContext(Session.Generator, Session.Content, new EntityRegistry.EntityRegistry())));
            Sim.Enqueue(new LeaveCommand(Sim.PlayerId));
            Sim.DrainCommands();
            Opened.Tick(1);
        }

        /// <summary>Within the simulation's own talk reach of the NPC as it stands now.</summary>
        public bool Near(string npcId)
        {
            var (them, me) = (Sim.Npcs.Single(n => n.Id == npcId).Body, Sim.Player.Body);
            return Math.Sqrt(Math.Pow(them.XMm - me.XMm, 2) + Math.Pow(them.ZMm - me.ZMm, 2)) <= 1800;
        }

        /// <summary>Stand beside the NPC: the save places the body, then the walker closes whatever metre the NPC's day moved.</summary>
        public void StandBeside(string npcId)
        {
            for (int i = 0; i < 4 && !Near(npcId); i++)
                At(npcId);
            Assert.True(Near(npcId), $"never stood beside {npcId}: stopped at {Sim.Player.Body}");
        }

        public void Say(string npcId, params string[] replies)
        {
            StandBeside(npcId);
            string? refused = Submit(new TalkCommand(Sim.PlayerId, npcId));
            Assert.True(refused is null, "talking to " + npcId + ": " + refused);
            foreach (string reply in replies)
            {
                var offered = Sim.Conversation!.Replies.Select(r => r.Id).ToList();
                Assert.True(offered.Contains(reply), $"{npcId} offered {string.Join(", ", offered)}, not '{reply}'");
                Assert.Null(Submit(new ChooseCommand(Sim.PlayerId, reply)));
            }
            if (Sim.Conversation is not null)
                Assert.Null(Submit(new LeaveCommand(Sim.PlayerId)));
            Opened.Tick(1);
        }

        /// <summary>Run the tick loop until the command path has taken the command: the arena's own submit, without its walkers.</summary>
        public string? Submit(GameCommand command) => Opened.Submit(command);

        public QuestStatus Status(string quest) => Sim.Quests.Single(q => q.Id == quest).Status;

        /// <summary>Give things where the character stands, by the harness's own hand (a save rebuilt with them).</summary>
        public void Give(params InventoryEntry[] items)
        {
            var record = Sim.CaptureRecord();
            var richer = Run.Carrying(record, items);
            var body = record;
            _store.Save(SaveSlots.Manual("give"), SaveDocuments.Capture(Sim.World, richer, Session.Content, Sim.WorldTick,
                Session.WorldTimeAdvancedTicks));
            Opened = Arena.Resume(Session.Setup, _store.Load(SaveSlots.Manual("give"),
                new LoadContext(Session.Generator, Session.Content, new EntityRegistry.EntityRegistry())));
            Opened.Tick(1);
        }

        /// <summary>The quest as the journal holds it, objectives by name, for a failure that says what is still open.</summary>
        public string Objectives(string quest)
        {
            var held = Sim.Quests.Single(q => q.Id == quest);
            return string.Join(", ", held.Objectives.Select(o => o.Id + "=" + o.Status)) + " (quest " + held.Status + ")";
        }

        public void Completed(string quest) =>
            Assert.True(Status(quest) == QuestStatus.Completed, quest + ": " + Objectives(quest));

        public void Dispose() => _store.Delete(SaveSlots.Manual("steps"));
    }

    [Fact]
    public void TheLedgerStops_PlaysEndToEnd_FromTheArchivistsOwnLine()
    {
        using var profile = new TempProfile();
        using var run = new Run(profile);

        run.Say(Archivist, "cistern", "go");
        Assert.Equal(QuestStatus.Active, run.Status(LedgerStops));

        // The pack broken: a hide off one of them, which is the evidence the Survey files with the page.
        run.Give(new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.material.wolf_hide", 1));
        run.Opened.Tick(2);

        // The court, for the visit and the reading at its threshold.
        run.At(Cistern);
        run.Opened.Tick(2);

        run.At(Archivist);
        run.Say(Archivist, "report", "back");

        run.Completed(LedgerStops);
    }

    [Fact]
    public void NothingHereIsHidden_PlaysEndToEnd_AcrossTheExchangesThreeInstitutions()
    {
        using var profile = new TempProfile();
        using var run = new Run(profile);

        run.At(Clerk);
        run.Say(Clerk, "tariff", "take");
        Assert.Equal(QuestStatus.Active, run.Status(WeekIsTheFee));

        run.At(Sergeant);
        run.Say(Sergeant, "witness", "back");

        run.At(Porter);
        run.Say(Porter, "landing", "back");

        run.At(Clerk);
        run.Say(Clerk, "report", "back");

        run.Completed(WeekIsTheFee);
    }

    [Fact]
    public void WhatTheGroundDid_PlaysEndToEnd_WithASightingTakenAtTheMouth()
    {
        using var profile = new TempProfile();
        using var run = new Run(profile);

        run.At(Geometer);
        run.Say(Geometer, "reading", "take");
        Assert.Equal(QuestStatus.Active, run.Status(WhatTheGroundDid));

        run.At(Ring);
        run.Opened.Tick(2);

        run.At(Geometer);
        run.Say(Geometer, "report", "back");

        run.Completed(WhatTheGroundDid);
    }

    [Fact]
    public void HeldByTheChain_PlaysEndToEnd_FromTwoBilletsToAWalkedLine()
    {
        using var profile = new TempProfile();
        // The two billets the district's own trade gives you: bought at the Exchange, or carried in from the frontier's forge.
        using var run = new Run(profile, r => Run.Carrying(r, new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.material.iron_ingot", 2)));

        run.At(Sergeant);
        run.Say(Sergeant, "chain", "take");
        Assert.Equal(QuestStatus.Active, run.Status(HeldByTheChain));

        run.At(Smith);
        run.Say(Smith, "chain_fit", "give");
        Assert.DoesNotContain(run.Sim.Player.Inventory, e => e.DefId == "item.material.iron_ingot");

        run.At(Ring);
        run.Opened.Tick(2);

        run.At(Sergeant);
        run.Say(Sergeant, "report", "back");

        run.Completed(HeldByTheChain);
    }
}
