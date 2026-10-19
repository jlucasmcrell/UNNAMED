using System.Collections.Immutable;
using System.Reflection;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Application.Tests.Acceptance.AcceptanceReport;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests.Acceptance;

/// <summary>
/// The playtest acceptance checklist (<c>PLAYTEST_ACCEPTANCE_CHECKLIST.md</c>) walked headless: a new character on The Road In, Renn, the waystation's
/// people, the first trader, the first station craft, the first fight and its loot, equipping and a gear set, the stash, the road to Cairnwold,
/// a save and reload, a log-off and log-on (the away sheet), and the downed and recovery. Every step writes its checklist id and PASS or FAIL to
/// <see cref="AcceptanceReport.Path"/>. Nothing here changes the game: it drives the same commands the game's input does, and moves the character
/// between places by saving the world with the character moved and loading it, as <c>ActOneStoryTests</c> does (the one place the walk itself is
/// not driven is between the settlement's buildings; the walk from the road to the waystone is).
/// </summary>
public partial class AcceptanceWalkthroughTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Ore = "item.material.iron_ore";
    private const string Billet = "item.material.iron_ingot";
    private const string BilletRecipe = "recipe.smithing.iron_billet";
    private static readonly DateTimeOffset Saved = new(2026, 10, 18, 18, 0, 0, TimeSpan.Zero);

    private sealed class Run : IDisposable
    {
        private readonly TempProfile _profile = new();
        public DateTimeOffset Now = Saved;
        public GameSession Session { get; }
        public Simulation Sim => Session.Simulation!;
        public List<CommandRejected> Rejections { get; } = new();

        public Run()
        {
            string root = Environment.GetEnvironmentVariable("UNNAMED_ACCEPTANCE_GAME_ROOT") ?? Harness.RepoRoot();
            Session = GameSession.Boot(new GameOptions(System.IO.Path.Combine(root, "content"), _profile.Root) { Clock = () => Now });
            Session.Subscribe<CommandRejected>(Rejections.Add);
        }

        public string SaveFolder => _profile.Root;

        /// <summary>Submit and run one frame; the refusal's reason, or null when the command was taken.</summary>
        public string? Do(GameCommand command)
        {
            int before = Rejections.Count;
            Session.Submit(command);
            Session.Frame(Session.TickSeconds);
            return Rejections.Count > before ? Rejections[^1].Reason : null;
        }

        public void Ticks(int ticks) => Harness.Ticks(Session, ticks);

        public GameCommand Cmd(Func<EntityId, GameCommand> make) => make(Sim.PlayerId);

        /// <summary>Stand the character at a place in the hollow's metres: the world saved with the character moved, and loaded (the same world, tick and state).</summary>
        public void Relocate(double hollowX, double hollowZ, int facingDeg = 0)
        {
            if (Sim.Conversation is not null)
                Do(new LeaveCommand(Sim.PlayerId));
            var (x, z) = Harness.InRegion(hollowX, hollowZ);
            MoveTo(x, z, facingDeg);
        }

        /// <summary>Move the character, and - as a reward or a find would - give coin or things (a harness's own hand, never a command).</summary>
        public void MoveTo(long xMm, long zMm, int facingDeg = 0, long giveCoin = 0, params InventoryEntry[] give)
        {
            var r = Sim.CaptureRecord();
            var moved = new PlayerRecord(r.Id, r.Name, xMm, Sim.Setup.Layout.Space.Terrain.HeightAtMm(xMm, zMm), zMm, r.AppearanceSeed, r.Inventory.Concat(give), r.Progression,
                facingDeg * 1000 % 360_000, r.Discoveries, r.Equipment, r.Currency + giveCoin, r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions);
            foreach (var property in typeof(PlayerRecord).GetProperties().Where(p => p.SetMethod is not null && p.GetIndexParameters().Length == 0
                && p.Name is not nameof(PlayerRecord.FacingMdeg)))
                property.SetValue(moved, property.GetValue(r));
            var store = new SaveStore(Session.SaveFolder);
            store.Save(SaveSlots.Manual("walk"), SaveDocuments.Capture(Sim.World, moved, Session.Content, Sim.WorldTick, Session.WorldTimeAdvancedTicks));
            Session.Load(SaveSlots.Manual("walk"));
            store.Delete(SaveSlots.Manual("walk"));   // a harness's save is not one for Continue to find
        }

        /// <summary>Give coin or things where the character stands.</summary>
        public void Give(long coin, params InventoryEntry[] items)
        {
            var body = Sim.Player.Body;
            MoveTo(body.XMm, body.ZMm, body.FacingMdeg / 1000, coin, items);
        }

        public void StandBeside(string npcId)
        {
            var body = Sim.Npcs.Single(n => n.Id == npcId).Body;
            if (Sim.Conversation is not null)
                Do(new LeaveCommand(Sim.PlayerId));
            MoveTo(body.XMm + 700, body.ZMm);
        }

        public void Dispose() => _profile.Dispose();
    }

    private readonly List<string> _ran = new();

    /// <summary>A checklist step, recorded in the report; this test fails on the failures of the steps it ran, and only those.</summary>
    private void Step(string id, Func<string> check)
    {
        _ran.Add(id);
        AcceptanceReport.Step(id, check);
    }

    private IReadOnlyList<string> OwnFailures() => Failures.Where(f => _ran.Any(id => f.StartsWith($"[§{id}] ", StringComparison.Ordinal))).ToList();

    private static CharacterSpec Spec(string name = "  Wenna   Lind ", string call = "call.wayfarer") =>
        new(name, Pronouns.She, "people.veth", "body.veth_f.lean", call) { Start = StartPoint.Road };

    private static int Carried(Simulation sim, string defId) => sim.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static QuestView Quest(Simulation sim, string id) => sim.Quests.Single(q => q.Id == id);

    private static void Equal<T>(T expected, T actual, string what) =>
        Assert.True(EqualityComparer<T>.Default.Equals(expected, actual), $"{what}: expected {expected}, saw {actual}");

    private static void That(bool condition, string what) => Assert.True(condition, what);

    [Fact]
    public void TheMainFlow_WalkedFromANewCharacterToTheReloadedLogin()
    {
        using var run = new Run();
        var session = run.Session;

        // ── §1 the creator's data and the new character ────────────────────────────
        Step("1.1", () =>
        {
            var bodies = session.Characters.PlayablePeoples.SelectMany(p => session.Characters.OfferedBodies(p.Id)).ToList();
            That(bodies.Count > 0, "the creator offers no bodies");
            using var profile = new TempProfile();
            var other = Harness.Boot(profile);
            foreach (var body in bodies)
            {
                var sim = other.NewGame(new CharacterSpec("Body Check", Pronouns.They, "people.veth", body.Id, "call.wayfarer"), seed: 1);
                Equal(body.Id, sim.Player.Identity.BodyPresetId, "body of the new character");
            }
            return $"{bodies.Count} offered bodies each start a character";
        });
        Step("1.4", () =>
        {
            var calls = session.Characters.Calls.Keys.ToList();
            That(calls.Count == 4, $"{calls.Count} calls offered, expected 4");
            foreach (var id in calls)
            {
                string name = session.DisplayName(id);
                That(name.Length > 0 && name != id, $"{id} has no display name");
                var call = session.Characters.Calls[id];
                That(!string.IsNullOrWhiteSpace(call.Tagline) && !string.IsNullOrWhiteSpace(call.Description), $"{id} lacks a tagline or a description");
            }
            return "4 calls, each with a name, a tagline and a description";
        });

        string? wolfCorpse = null;
        int awayNpcMoved = 0;
        bool awayDateChanged = false;
        UNNAMED.World.Runtime.Simulation? simulation = null;
        Step("1.5", () =>
        {
            simulation = session.NewGame(Spec(), seed: 42);
            Equal("Wenna Lind", simulation.Player.Name, "name kept (spaces tidied only)");
            return "the name is kept as typed, spaces tidied";
        });
        if (simulation is null)
        {
            Blocked("1.6", "no new character"); Write();
            Assert.Fail("the new game did not start");
            return;
        }

        Step("1.6", () =>
        {
            Equal(session.Setup.Layout.Opening, simulation.Player.Body, "the character's place");
            Equal(QuestStatus.Active, Quest(simulation, "quest.main.the_road_in").Status, "The Road In");
            return "the character stands on the road at the opening, The Road In is active";
        });

        // ── §2 the opening ─────────────────────────────────────────────────────────
        Step("2.1", () =>
        {
            var road = Quest(run.Sim, "quest.main.the_road_in");
            Equal(ObjectiveStatus.Active, road.Objectives.Single(o => o.Id == "o_arrive").Status, "arrive objective");
            That(road.Objectives.Single(o => o.Id == "o_arrive").Description.Length > 10, "the arrival has no words");
            That(run.Sim.TrackedQuest() == "quest.main.the_road_in", "the opening is not the tracked quest");
            return "an arrival objective with words, tracked from the first tick";
        });
        string firstWords = "";
        Step("2.2", () =>
        {
            firstWords = Quest(run.Sim, "quest.main.the_road_in").Objectives.Single(o => o.Id == "o_arrive").Description;
            var spawn = session.Setup.Layout.Spawn;
            That(Harness.WalkTo(session, spawn.XMm, spawn.ZMm, maxTicks: 1500), "the road to the waystone could not be walked");
            run.Ticks(3);
            var road = Quest(run.Sim, "quest.main.the_road_in");
            Equal(ObjectiveStatus.Satisfied, road.Objectives.Single(o => o.Id == "o_arrive").Status, "arrive objective after the walk");
            var keeper = road.Objectives.Single(o => o.Id == "o_keeper");
            Equal(ObjectiveStatus.Active, keeper.Status, "keeper objective");
            That(keeper.Description != firstWords && keeper.Description.Contains("Renn"), "the next objective names Renn in words");
            return "walked the road; the objective updated to \"" + keeper.Description[..28] + "...\"";
        });

        Step("2.6", () =>
        {
            var said = new List<string>();
            foreach (var (npc, label) in new[] { (Renn, "Renn Vale"), (Kera, "Kera Voss"), (Sel, "Sel Arien") })
            {
                run.StandBeside(npc);
                Equal(null, run.Do(new TalkCommand(run.Sim.PlayerId, npc)), $"talking to {label}");
                int replies = 0;
                for (int i = 0; i < 6 && run.Sim.Conversation is { } view; i++)
                {
                    That(view.Replies.Length > 0, $"{label}'s node '{view.NodeId}' offers no reply");
                    That(view.Replies.All(r => !string.IsNullOrWhiteSpace(r.Text)), $"{label}'s node '{view.NodeId}' has a reply with no words");
                    var next = view.Replies.FirstOrDefault(r => r.Id is not ("leave" or "trade" or "teach" or "back")) ?? view.Replies[0];
                    if (next.Id is "leave")
                        break;
                    Equal(null, run.Do(new ChooseCommand(run.Sim.PlayerId, next.Id)), $"{label}: reply '{next.Id}'");
                    replies++;
                }
                if (run.Sim.Conversation is not null)
                    Equal(null, run.Do(new LeaveCommand(run.Sim.PlayerId)), $"leaving {label}");
                That(run.Sim.Conversation is null, $"{label}'s conversation did not end");
                said.Add($"{label} {replies}");
            }
            Equal(ObjectiveStatus.Satisfied, Quest(run.Sim, "quest.main.the_road_in").Objectives.Single(o => o.Id == "o_keeper").Status, "talk-to-Renn objective");
            return "talked through three keepers (" + string.Join(", ", said) + " replies), none dead-ended, each left cleanly; the keeper objective is satisfied";
        });

        // ── §11 the first trader: sell, see the price first, buy ───────────────────
        Step("11.2", () =>
        {
            run.StandBeside(Kera);
            const string meat = "item.consumable.roast_meat";
            long? offer = run.Sim.Offer(Kera, meat);
            That(offer is > 0, "Kera shows no price for roast meat before the sale");
            long coin = run.Sim.CaptureRecord().Currency;
            var stack = run.Sim.Player.Inventory.First(e => e.DefId == meat);
            Equal(null, run.Do(new SellCommand(run.Sim.PlayerId, Kera, stack.ItemId, 1)), "selling one roast meat");
            Equal(coin + offer!.Value, run.Sim.CaptureRecord().Currency, "coin after the sale (the price shown first)");
            return $"the offer ({offer}) was shown before the sale and is what was paid";
        });
        Step("11.1", () =>
        {
            var ware = run.Sim.Wares(Kera)!.Wares.First(w => w.ItemId == "item.consumable.salve_minor");
            run.Give(ware.Price + 5);
            run.StandBeside(Kera);
            long coin = run.Sim.CaptureRecord().Currency;
            int salves = Carried(run.Sim, "item.consumable.salve_minor");
            var shown = run.Sim.Wares(Kera)!.Wares.First(w => w.ItemId == "item.consumable.salve_minor");
            Equal(null, run.Do(new BuyCommand(run.Sim.PlayerId, Kera, shown.Ref, 1)), "buying a salve");
            Equal(coin - shown.Price, run.Sim.CaptureRecord().Currency, "coin after the purchase (the price shown)");
            Equal(salves + 1, Carried(run.Sim, "item.consumable.salve_minor"), "salves carried");
            return $"bought a salve for the {shown.Price} shown: coin down by that, one more carried";
        });
        Step("11.5", () =>
        {
            run.StandBeside(Kera);
            var ware = run.Sim.Wares(Kera)!.Wares.First(w => w.ItemId == "item.weapon.hunting_bow");
            long coin = run.Sim.CaptureRecord().Currency;
            That(coin < ware.Price, $"the character has {coin}, the bow costs {ware.Price}: the check needs a poorer character");
            string? why = run.Do(new BuyCommand(run.Sim.PlayerId, Kera, ware.Ref, 1));
            That(why is not null && why.Length > 4, "buying past the purse was not refused with a reason");
            That(run.Sim.CaptureRecord().Currency == coin, "coin changed on a refused purchase");
            return $"refused with \"{why}\"; coin untouched ({coin} of {ware.Price})";
        });

        // ── §6 / §4 the forge: taught, gathered, crafted ───────────────────────────
        Step("6.3", () =>
        {
            run.StandBeside(Kera);
            Equal(null, run.Do(new TalkCommand(run.Sim.PlayerId, Kera)), "talking to Kera");
            string reply = run.Sim.Conversation!.Replies.First(r => r.Id == "teach").Id;
            Equal(null, run.Do(new ChooseCommand(run.Sim.PlayerId, reply)), "asking Kera to teach the forge");
            Equal(null, run.Do(new LeaveCommand(run.Sim.PlayerId)), "leaving");
            That(run.Sim.Player.Progression.Known.ContainsKey(BilletRecipe), "the billet recipe is not known after the lesson");
            That(run.Sim.Player.Progression.Known.ContainsKey("recipe.smithing.march_spear"), "the spear recipe is not known after the lesson");
            Equal(QuestStatus.Active, Quest(run.Sim, "quest.ashen_hollow.iron_under_ash").Status, "Iron Under Ash");
            return "Kera taught the billet and the spear; Iron Under Ash began";
        });
        Step("4.1", () =>
        {
            var seam = run.Sim.Nodes.Single(n => n.Name == "iron_seam");
            run.Relocate(28, 44.1);
            int before = Carried(run.Sim, Ore);
            for (int i = 0; i < 8 && Carried(run.Sim, Ore) < 2; i++)
            {
                string? refused = run.Do(new GatherCommand(run.Sim.PlayerId, seam.Key));
                if (refused is not null)
                    break;
                run.Ticks(30);
            }
            int got = Carried(run.Sim, Ore) - before;
            That(got >= 1, $"gathering at the seam yielded {got} ore");
            return $"struck the seam and carried {got} ore";
        });
        Step("4.2", () =>
        {
            run.Relocate(59.5, 143);
            int ore = Carried(run.Sim, Ore), billets = Carried(run.Sim, Billet);
            Equal(null, run.Do(new CraftCommand(run.Sim.PlayerId, BilletRecipe)), "smelting a billet");
            run.Ticks(5);
            Equal(ore - 1, Carried(run.Sim, Ore), "ore after the smelt");
            Equal(billets + 1, Carried(run.Sim, Billet), "billets after the smelt");
            return "the hearth took one ore and made one iron billet";
        });
        Step("4.3", () =>
        {
            while (Carried(run.Sim, Ore) > 0 && run.Do(new CraftCommand(run.Sim.PlayerId, BilletRecipe)) is null)
                run.Ticks(5);
            That(Carried(run.Sim, Ore) == 0, "could not spend the ore to set up the refusal");
            string? why = run.Do(new CraftCommand(run.Sim.PlayerId, BilletRecipe));
            That(why is not null && why.Contains(Ore, StringComparison.Ordinal), $"the refusal does not name the missing ore: {why ?? "(none)"}");
            return $"refused with \"{why}\"";
        });

        // ── §3 the first fight and its loot ────────────────────────────────────────
        Step("3.1", () =>
        {
            var wolf = run.Sim.Creatures.First(c => c.Key == "spawn.hollow.east_pack#0");
            (double X, double Z) at = (wolf.Body.XMm / 1000.0 - Harness.Hollow.X - 3.0, wolf.Body.ZMm / 1000.0 - Harness.Hollow.Z);
            run.Relocate(at.X, at.Z, 90);
            var killed = new List<CreatureKilled>();
            session.Subscribe<CreatureKilled>(killed.Add);
            var hits = new List<HitResolved>();
            session.Subscribe<HitResolved>(hits.Add);
            for (int i = 0; i < 1500 && killed.Count == 0; i++)
            {
                var sim = run.Sim;
                var creature = sim.Creatures.First(c => c.Key == wolf.Key);
                if (!creature.Alive || sim.Combat.Defeated)
                    break;
                var body = sim.Player.Body;
                double dx = creature.Body.XMm - body.XMm, dz = creature.Body.ZMm - body.ZMm, distance = Math.Sqrt(dx * dx + dz * dz);
                int facing = CombatRules.FacingTowards(body.XMm, body.ZMm, creature.Body.XMm, creature.Body.ZMm);
                bool inReach = distance <= sim.Combat.Weapon.ReachMm + 300;
                session.Submit(new MoveCommand(sim.PlayerId, inReach ? MoveIntent.Idle(facing)
                    : new MoveIntent((int)Math.Round(dx / distance * 1000), (int)Math.Round(dz / distance * 1000), Gait.Run, facing)));
                if (inReach && sim.Combat.Phase == CombatPhase.Idle)
                    session.Submit(new AttackCommand(sim.PlayerId));
                session.Frame(session.TickSeconds);
            }
            That(killed.Count == 1 && killed[0].DefId == "creature.beast.wolf_grey", "the wolf was not killed with the starting weapon in 75 s");
            That(hits.Any(h => h.Attacker == run.Sim.PlayerId && h.Damage > 0), "the starting weapon never connected");
            wolfCorpse = wolf.CorpseKey;
            return $"the rusted sword killed a grey wolf ({hits.Count(h => h.Attacker == run.Sim.PlayerId && h.Damage > 0)} blows landed)";
        });
        Step("3.1b", () =>
        {
            That(wolfCorpse is not null, "no kill, so no corpse");
            var corpse = run.Sim.Containers.SingleOrDefault(c => c.Site.Key == wolfCorpse);
            That(corpse is not null, "the wolf left no corpse to search");
            int before = run.Sim.Player.Inventory.Sum(i => i.Count);
            Equal(null, run.Do(new TakeAllCommand(run.Sim.PlayerId, wolfCorpse!)), "taking the carcass's goods");
            int after = run.Sim.Player.Inventory.Sum(i => i.Count);
            That(after > before || corpse!.Items.Length == 0, "the loot did not arrive in the pack");
            return $"searched the carcass: {after - before} things taken";
        });

        // ── equip and a gear set (the checklist does not number these: §90) ─────────
        Step("90.1", () =>
        {
            run.Give(0, new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.weapon.hunting_bow", 1));
            var sim = run.Sim;
            var worn = sim.Player.Equipment;
            var bow = sim.Player.Inventory.First(e => e.DefId == "item.weapon.hunting_bow").ItemId;
            run.Ticks(60);
            Equal(null, run.Do(new SaveGearSetCommand(sim.PlayerId, 1, "Blade and vest")), "keeping the worn kit as a gear set");
            Equal(null, run.Do(new EquipCommand(sim.PlayerId, bow)), "equipping the hunting bow");
            Equal(bow, run.Sim.Player.Equipment[EquipSlot.MainHand], "main hand after equipping the bow");
            Equal(null, run.Do(new SwapGearSetCommand(sim.PlayerId, 1)), "switching back to the kept set");
            bool hurried = run.Sim.GearSwap is { InCombat: true };
            for (int i = 0; i < 400 && run.Sim.GearSwap is not null; i++)
                run.Ticks(1);
            That(worn.Select(kv => (kv.Key, kv.Value)).SequenceEqual(run.Sim.Player.Equipment.Select(kv => (kv.Key, kv.Value))), "what is worn after the switch is not what the set kept");
            Equal(null, run.Sim.GearSwap, "the switch finished");
            return "equipped the bow, kept a set, and the switch put the blade and vest back on" + (hurried ? " (the slower in-fight switch: a fight had just ended)" : " after its two seconds");
        });
        Step("90.2", () =>
        {
            run.Relocate(40, 138);
            var billet = run.Sim.Player.Inventory.First(e => e.DefId == Billet);
            Equal(null, run.Do(new MoveItemCommand(run.Sim.PlayerId, billet.ItemId.Value, ItemPlace.Carried, ItemPlace.In("container.stash.waystation"), 1)), "putting a billet in the stash");
            var stash = run.Sim.Containers.Single(c => c.Site.Key == "container.stash.waystation");
            That(stash.Items.Any(i => i.DefId == Billet), "the billet is not in the stash");
            var held = stash.Items.First(i => i.DefId == Billet);
            Equal(null, run.Do(new MoveItemCommand(run.Sim.PlayerId, held.Ref, ItemPlace.In("container.stash.waystation"), ItemPlace.Carried, 1)), "taking it back");
            That(Carried(run.Sim, Billet) >= billet.Count, "the billet did not come back to the pack");
            return "a billet went into the waystation stash and came back to the pack";
        });

        // ── §12 the stones: Cairnwold's stone woken, a journey made ────────────────
        Step("12.4", () =>
        {
            run.Relocate(28.9, 158.0);   // beside the Ashen Waystone (region 428.9, 758.0)
            Equal(null, run.Do(new TouchStoneCommand(run.Sim.PlayerId, "waystone.ashen")), "touching the Ashen Waystone");
            run.MoveTo(959_000, 644_000);   // Berrin Garth's door, in the region's metres
            run.StandBeside("npc.cairnwold.berrin_garth");
            Equal(null, run.Do(new TalkCommand(run.Sim.PlayerId, "npc.cairnwold.berrin_garth")), "talking to Berrin");
            Equal(null, run.Do(new ChooseCommand(run.Sim.PlayerId, "rim_stone")), "asking for the rite");
            run.Do(new LeaveCommand(run.Sim.PlayerId));
            run.MoveTo(880_600 + 500, 702_500);
            Equal(null, run.Do(new TouchStoneCommand(run.Sim.PlayerId, "waystone.rim")), "touching the Rim Stone");
            run.Relocate(28.9, 158.0);
            var option = run.Sim.TravelOptions("waystone.ashen").Single(o => o.ToKey == "waystone.rim");
            That(option.Blocked is null, $"the journey to the Rim Stone is blocked: {option.Blocked}");
            long before = run.Sim.WorldTick;
            Equal(null, run.Do(new TravelCommand(run.Sim.PlayerId, "waystone.ashen", "waystone.rim")), "travelling by the stones");
            run.Ticks(2);
            var body = run.Sim.Player.Body;
            var rim = run.Sim.Setup.Layout.FindWaystone("waystone.rim")!;
            var (rx, rz) = Footprints.Center(rim.Body);
            That(Math.Sqrt(Math.Pow(body.XMm - rx, 2) + Math.Pow(body.ZMm - rz, 2)) < 6_000, "the character did not arrive by the Rim Stone");
            long passed = run.Sim.WorldTick - before;
            Equal(3L, passed, "world ticks over the journey (the three frames run, no more)");
            return $"arrived at the Rim Stone above Cairnwold; the world clock moved only the {passed} frames run (the stones skip no time, DN8); the journey's walking cost of {option.Cost.WalkTicks} ticks is shown, not spent";
        });
        // ── §16 save and reload ────────────────────────────────────────────────────
        Step("16.1", () =>
        {
            session.Save(SaveSlots.Quick);
            var before = run.Sim.CaptureRecord();
            string digest = run.Sim.StateDigest();
            run.Ticks(30);   // the world moves on after the save
            session.Load(SaveSlots.Quick);
            var after = run.Sim.CaptureRecord();
            Equal(digest, run.Sim.StateDigest(), "the world's digest after the reload");
            Equal((before.XMm, before.ZMm, before.Currency), (after.XMm, after.ZMm, after.Currency), "position and coin after the reload");
            Equal(before.Inventory.Select(i => (i.DefId, i.Count)).Order().ToArray().Length, after.Inventory.Select(i => (i.DefId, i.Count)).Order().ToArray().Length, "pack entries");
            Equal(before.Quests.Select(q => (q.QuestId, q.Status)).Order().ToArray().Length, after.Quests.Select(q => (q.QuestId, q.Status)).Order().ToArray().Length, "quests");
            Equal(before.Equipment.Count, after.Equipment.Count, "worn items");
            That(after.Progression.Known.ContainsKey(BilletRecipe), "the lesson was forgotten");
            Equal(before.GearSets.Length, after.GearSets.Length, "gear sets");
            return "saved, the world ran on 30 ticks, reloaded: the world's digest, position, coin, pack, quests and kit are as saved";
        });

        // ── §14 log off, log on: the away sheet ────────────────────────────────────
        Step("14.3", () =>
        {
            session.Save(SaveSlots.Quick);
            run.Now = Saved.AddHours(1);   // a game day and a quarter
            var npcBefore = run.Sim.Npcs.ToDictionary(n => n.Id, n => (n.Body.XMm, n.Body.ZMm));
            var dateBefore = run.Sim.Calendar;
            var login = session.Continue();
            That(login.Away.Lines.Count > 0, "the away sheet is empty");
            That(login.Away.Lines.Any(l => l.Kind == AdvanceKinds.TimePassed && l.Amount > 0), "the away sheet does not say time passed");
            That(login.CatchUp.HonouredTicks == 72_000, $"an hour away honoured {login.CatchUp.HonouredTicks} ticks, expected 72 000");
            awayNpcMoved = run.Sim.Npcs.Count(n => npcBefore.TryGetValue(n.Id, out var was) && was != (n.Body.XMm, n.Body.ZMm));
            awayDateChanged = run.Sim.Calendar != dateBefore;
            return $"logged on an hour later: {login.Away.Lines.Count} away lines, {login.CatchUp.HonouredTicks} ticks honoured";
        });
        Step("14.4", () =>
        {
            That(awayDateChanged, "the calendar did not move while away");
            That(awayNpcMoved > 0, "no one moved while the character was away");
            That(run.Sim.Weather.State.ToString().Length > 0, "no weather after the absence");
            return $"the world moved: the calendar advanced and {awayNpcMoved} people stand elsewhere";
        });

        Write();
        Assert.Empty(OwnFailures());
    }
}
