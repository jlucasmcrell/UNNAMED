// UNNAMED Presentation - the headless boot smoke (PROTOTYPE.md §6.3 "Boot smoke")
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --headless --path src/Presentation -- --smoke</c>: the project boots, content loads, the world is built and
/// ticks, the player walks to the longhouse through the real command path and opens its door; (M4) walks in to whoever's
/// conversation hands over a book that teaches, talks until it is given, and (M3e) reads it and works the first self formula
/// it taught (M5: after walking to whoever's conversation starts a quest - found in the data - and taking it); swings the sword
/// at nothing (M3c: the attack runs its phases and misses); and a quicksave loads back to the identical state, the conversation
/// remembered and the quest where it was. Exit code 0 on success, 1 on failure; the scratch save profile is removed either way.
/// </summary>
public sealed class Smoke
{
    // CAIRNWOLD'S OWN LAYOUT, DERIVED FROM THE REGION AND NOT TYPED BY HAND.
    //
    // These three arrays were the M6 DEMO's coordinates: its longhouse door stood at (51.6-52, 127.2-128.8) and
    // its forge shed at (53-53.4, 141.2-142.8). The generated region puts its village at (60, 60) instead, so the
    // route walked 75 m north of the village to empty ground, found no door there, and timed out at tick 3000 -
    // which is the red smoke test, and the thing the owner watched and read as the character stuck against a
    // building. Nothing was wrong with the walking; the waypoints pointed at a village that is not there.
    //
    // Rewriting them by hand does not work and was tried: of eleven hand-picked waypoints, FOUR stood inside
    // buildings with 0.00 m of clearance, and a waypoint inside a building is the exact failure being fixed.
    // They are derived instead by tools/asset_pipeline/cairnwold/smoke_waypoints.py, which reads the same region
    // file the game loads, takes each door's own box centre, finds the nearest open stand in front of it, and
    // walks the straight line between them nudging every point into cleared ground.
    //
    // A DOOR STAND IS JUDGED BY THE OPPOSITE RULE TO EVERY OTHER WAYPOINT, and that cost a run to learn: the
    // general rule wants 1.6 m of open ground around a point, and at a door that rule pushes the stand AWAY from
    // the building the door is set in - the first derivation produced stands 2.03 m from the door centre, out of
    // the 1.6 m interact_reach_m in config/base_speeds.yaml, so the character walked the entire route, arrived,
    // and then stood there unable to open anything. Open-ground waypoints are now held to 1.0 m of clearance and
    // door stands to being inside the reach with only the 0.45 m a 0.35 m body needs. Both are asserted when the
    // arrays are generated; none of these numbers was typed by hand.
    //
    // ONE LEG CHANGED MEANING. The demo's second leg walked "in through the door and out again" because its
    // longhouse was assembled from thin wall runs and had an interior. Cairnwold's houses are SOLID boxes - the
    // door is on the face of a solid volume - so there is no inside to walk into. The leg now crosses the village
    // green and returns to the door, which still exercises doors, collision and open-ground movement.
    // LAST TWO WAYPOINTS MOVED: they were (52.9, 43.1) and (48.5, 46.7), which is open ground no longer - the
    // communal lodge now stands on x 37.0-55.0 by z 38.8-46.8, and both points are inside it. The walk pressed
    // against the lodge's east wall at (55.4, 43.1) for the rest of the run and the harness timed out at tick
    // 3000 with `doors open 0 of 3`, having never finished the route — so the door step never ran at all.
    //
    // The leg's own note says what it is for: "crosses the village green and returns to the door". The green is
    // now east of the lodge rather than where the lodge is, so the leg goes into the open entry ground and comes
    // back to the lodge door from the side the door faces (+X, east). (55.8, 43.2) is 1.22 m from the door centre
    // at (54.8, 42.5), inside the 1.6 m interaction reach, and 0.8 m clear of the wall for a 0.35 m body.
    private static readonly (double X, double Z)[] Route = { (64.5, 28.9), (64.2, 33.4), (64.0, 38.0), (58.8, 40.9), (61.0, 41.5), (55.8, 43.2) };
    // Inside and ToTheForge have the same problem as Route did, and for the same reason: the lodge is on the
    // ground they were authored over. (43.7, 40.6), (54.0, 40.6) and (43.7, 40.6) are all inside x 37.0-55.0 by
    // z 38.8-46.8, and (48.5, 46.7) - the junction where the green leg hands over to the forge leg - sits in the
    // lodge's north wall run (z 46.4-46.8). The run stalled 0.65 m short of it at (48.50, 46.05) for eight
    // seconds with `inside 3` and never reached the forge at all.
    //
    // Both legs now use the open entry ground EAST of the lodge, which is the side the door faces. The green leg
    // still does what its note says - crosses the green, comes back, and ends at the door - and the forge leg
    // starts from that same open ground rather than from inside a wall.
    private static readonly (double X, double Z)[] Inside = { (60.5, 41.5), (56.5, 44.8), (60.5, 41.5), (55.8, 43.2) };
    private static readonly (double X, double Z)[] ToTheForge = { (60.0, 41.0), (61.0, 38.0), (73.0, 50.0), (78.0, 51.0), (77.0, 60.0), (75.0, 64.0), (82.0, 76.0), (81.8, 78.5) };

    // THE SAME THREE LEGS FOR THE DEMO, WHICH IS THE OTHER REGION THIS SHARED HARNESS HAS TO WALK.
    //
    // The arrays above were the DEMO's until this round, and replacing them with Cairnwold's fixed Cairnwold and
    // broke Ashen Hollow - `--smoke --region region.ashen_hollow` began timing out at (63.6, 36.2), pressed against
    // a wall that only exists in the other region. ONE HARNESS, TWO REGIONS, ONE SET OF COORDINATES is a test that
    // can only ever be green in one world, and the owner's ruling is explicit that the two are to be merged on
    // their merits rather than one forced over the other.
    //
    // These three are the demo's own, which the harness carried and passed with before Cairnwold was generated:
    // the M6 layout's waystone-to-lodge walk, its in-and-out leg (the demo's longhouse really is built from thin
    // wall runs, so it HAS an inside, unlike Cairnwold's solid houses) and its way to the forge shed. They are kept
    // rather than re-derived because they are correct for the region they were written for - now the hollow inside
    // region.starting_frontier, so they are placed where that region has it (Playthrough.Hollow).
    //
    // THE PROPER FIX IS TO DERIVE BOTH AT RUNTIME - `_session.Setup.Layout.Doors` already exposes each door's
    // footprint in whatever region is loaded, and tools/asset_pipeline/cairnwold/smoke_waypoints.py already proves
    // the derivation works. Until then, this keeps both regions green, which is what the shared harness is for.
    private static readonly (double X, double Z)[] DemoRoute = Playthrough.Hollow((44, 138), (54.5, 134), (53, 128));
    private static readonly (double X, double Z)[] DemoInside = Playthrough.Hollow((50.5, 128), (44, 128.6), (50.5, 128), (53, 128));
    // THE ARRAY ENDS BESIDE THE SHED, NOT BEHIND IT. Its last point was (54.5, 142), which is on the FAR SIDE of
    // the forge shed - the shed is a solid box at x 53-53.4, z 141.2-142.8, so reaching that point from (51.8, 142)
    // means walking round it, and Walk steers in a straight line. Nothing caught this before because the door step
    // used to fire one waypoint early and the last point was never walked at all; correcting that step to open the
    // door ON ARRIVAL made the walk real and the character pressed against the shed at (52.7, 142) for 3000 ticks.
    // (51.8, 142) is 1.2 m from the shed's west face - inside the 1.6 m interaction reach - so the door still opens.
    private static readonly (double X, double Z)[] DemoToTheForge = Playthrough.Hollow((60, 134), (51.8, 136), (51.8, 142));

    /// <summary>Whether the run's region is the generated Cairnwold, whose geography these legs were derived for.</summary>
    private static bool IsCairnwold(string regionId) => regionId.Contains("cairnwold", StringComparison.OrdinalIgnoreCase);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly string _profile;
    private int _frames;
    private readonly (double X, double Z)[] _route;
    private readonly (double X, double Z)[] _insideLeg;
    private readonly (double X, double Z)[] _forgeLeg;
    private int _waypoint;
    private int _inside;
    private (double X, double Z) _lastAt = (double.NaN, double.NaN);
    private readonly List<(double X, double Z)> _path = new();
    private int _steerFrames;
    private int _trail;
    private int _openDoors = -1;
    private bool _asked;
    private bool _forgeAsked;
    private bool _talked;
    private bool _taken;
    private bool _read;
    private bool _cast;
    private int _worked = -1;
    private bool _swung;
    private bool _started;
    private bool _missed;
    private int _forge;
    private bool _forgeDoor;
    private bool _questAsked;

    public Smoke(GameSession session, PlayerController controller, CameraRig camera, string profile, string region)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _profile = profile;
        // WHICH REGION'S GEOGRAPHY TO WALK. Chosen here, once, so the legs are fixed for the run.
        bool cairnwold = IsCairnwold(region);
        _route = cairnwold ? Route : DemoRoute;
        _insideLeg = cairnwold ? Inside : DemoInside;
        // BACK THE WAY IT CAME FIRST. The book's giver is met wherever their day has them (WP12), and Walk steers in a straight line: from
        // where the character stood beside Sel at her house, the line to the forge leg's first point ran into a wall, and the run pressed
        // against it at (451.25, 731.25) until the budget ran out. The line from the inside leg's last point to the giver was walked already,
        // so walking it back is clear, and from there the forge leg is the one every passing run walked.
        _forgeLeg = _insideLeg.TakeLast(1).Concat(cairnwold ? ToTheForge : DemoToTheForge).ToArray();
        GD.Print($"UNNAMED smoke: walking {(cairnwold ? "Cairnwold's" : "the demo's")} waypoints for region {region}");
        _session.Subscribe<AttackStarted>(e => _started |= e.Attacker == _session.Simulation!.PlayerId);
        _session.Subscribe<AttackMissed>(e => _missed |= e.Attacker == _session.Simulation!.PlayerId);
        _session.Subscribe<CastCompleted>(e => _worked = e.Strain);
        _session.Subscribe<CastFizzled>(_ => _cast = false);
    }

    /// <summary>Advance one frame; returns the exit code when the smoke is over.</summary>
    public int? Update()
    {
        if (++_frames > 3000)
        {
            var at = _controller.Authoritative;
            return Fail($"timed out at tick {_session.Simulation!.WorldTick} at ({at.XMm / 1000.0:0.0}, {at.ZMm / 1000.0:0.0}): route {_waypoint}, " +
                        $"doors open {_session.Simulation!.Doors.Count(d => d.Open)} of {_session.Simulation.Doors.Length}, inside {_inside}, taken {_taken}, read {_read}, cast {_cast}, worked {_worked}, " +
                        $"swung {_swung}, started {_started}, missed {_missed}");
        }

        // A DOOR-STATE WATCH, because the run ends with no door open and the only place that opens one is the forge
        // leg - so something closes it again and a snapshot at the end cannot say what. Every CHANGE is reported with
        // the frame and the leg indices, which turns "it is shut at the end" into a timeline: opened here, shut
        // there, and whatever ran in between is the answer. `Interact` toggles a door, so a leg that interacts more
        // than once opens and shuts it again, and the forge loop interacts every frame until its latch takes.
        if (_session.Simulation is { } sim)
        {
            int open = sim.Doors.Count(d => d.Open);
            if (open != _openDoors)
            {
                GD.PushWarning($"UNNAMED smoke: door watch - {open} of {sim.Doors.Length} open at frame {_frames} " +
                               $"(route {_waypoint}, inside {_inside}, forge {_forge}/{_forgeLeg.Length}, latched {_forgeDoor})");
                _openDoors = open;
            }
        }

        if (_waypoint < _route.Length)
        {
            if (Walk(_route[_waypoint]))
                _waypoint++;
            return null;
        }

        // NO DOOR IS NAMED HERE ANY MORE. This stood on `door.longhouse`, which Cairnwold no longer declares - the
        // region has no longhouse, and the orphan door site was removed once it was measured as drawing a lone leaf
        // in open ground. Naming it made the smoke die on `.Single(...)` before it could print a verdict, which is a
        // crash rather than a failure, and the file already says what to do instead: derive it. The forge leg below
        // opens the nearest door on the layout's own terms ("the layout's, not named"), so this step only has to get
        // out of the way - it used to block on a door nothing stands near any more.

        // OPEN THE DOOR THE ROUTE ENDS AT, IF IT ENDS AT ONE - AND DO NOT BLOCK IF IT DOES NOT.
        //
        // This step was deleted earlier in this stretch on the reasoning that Cairnwold's route no longer ends at a
        // door, so nothing was in reach and it could only hang. Deleting it BROKE THE DEMO REGION, which this same
        // harness walks: there the route ends at the longhouse door and the inside leg walks THROUGH that doorway, so
        // with nothing to open it the character pressed against the closed leaf at (52.35, 128.01) - 1.85 m from its
        // waypoint, net 0.02 m over two seconds - and timed out. The trail detector said so on the first sample.
        //
        // So it opens a door when one is in reach and marks itself done when one is not - which is correct in both
        // regions rather than tuned for one. `_asked` guards the ask either way: Interact TOGGLES, and a loop that
        // asks twice shuts the door it just opened.
        if (!_asked)
        {
            if (_controller.FocusOn(_camera) is { Kind: FocusKind.Door } door)
            {
                _controller.Interact(door.Key);
                _asked = true;
                return null;    // let the command land before anything reads the state
            }
            _asked = true;      // no door in reach: this route does not end at one, and the walk continues
        }

        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        if (_inside < _insideLeg.Length)
        {
            if (Walk(_insideLeg[_inside]))
                _inside++;
            return null;
        }
        // Whoever's conversation hands over a book that teaches, and the replies that reach it: found in the data, not named.
        if (PathToABook(_session.Setup, _session.Simulation!) is not { } path)
            return Fail("no conversation hands over a book that teaches");
        var speaker = simulation.Npcs.Single(n => n.Id == path.NpcId);
        if (!_talked)
        {
            // Up to within a hand's reach of them, from where the character stands: round walls and in through doors.
            if (!_controller.InTalkReach(speaker) && !WalkTo(speaker))
                return null;
            _controller.Talk(path.NpcId);
            foreach (string reply in path.Replies)
                _session.Submit(new ChooseCommand(simulation.PlayerId, reply));
            _talked = true;
            return null;
        }
        if (!_taken)
        {
            if (!simulation.Player.Inventory.Any(e => magic.Teaches.ContainsKey(e.DefId)))
                return null;
            _session.Submit(new LeaveCommand(simulation.PlayerId));
            _taken = true;
            return null;
        }
        // M5: whoever's conversation starts a quest, and the replies that reach it - found in the data - then the quest taken.
        if (PathToAQuest(_session.Setup, _session.Simulation!) is not { } quest)
            return Fail("no conversation starts a quest");
        if (!simulation.Quests.Any(q => q.Id == quest.QuestId))
        {
            if (_forge < _forgeLeg.Length)
            {
                if (Walk(_forgeLeg[_forge]))
                    _forge++;
                return null;
            }
            // THE DOOR OPENS ON ARRIVAL, NOT ON THE WAY - and this is what every "timed out 2.6 m short" run was.
            // The door step stood INSIDE the walking loop on `_forge == _forgeLeg.Length - 1`, which is the waypoint
            // being walked TO and not the one just reached. So from the moment the character set off for the last
            // stand it took this branch every frame and returned before Walk was called at all: it stopped 2.6 m
            // short of the smithy door and spent the rest of the run trying to focus a door out of its reach.
            //
            // That also explains why the trail detector never fired once. It reports a body that is not moving or
            // not closing, and this body was doing neither wrong - it had stopped being ASKED to move. A harness can
            // fail a walk by steering it badly or by never steering it at all, and the instrumentation for the first
            // is blind to the second.
            if (!_forgeDoor)
            {
                // ITS OWN FLAG, NOT THE ROUTE STEP'S. Both steps guard their ask with `_asked`, and the route step
                // now sets it in Cairnwold too - where the route ends on the village green and there is no door in
                // reach, so it marks itself done and carries on. Sharing the flag meant the forge step then found
                // `_asked` already true, never interacted, and left the run standing at the smithy door with `doors
                // open 0 of 2` until it timed out. Two steps that each ask once need one flag each; the demo was
                // passing while Cairnwold failed, which is exactly the shape a shared flag produces.
                if (!_forgeAsked)
                {
                    if (_controller.FocusOn(_camera) is { Kind: FocusKind.Door } focused)
                    {
                        _controller.Interact(focused.Key);
                        _forgeAsked = true;
                        return null;
                    }
                    return null;   // nothing in reach yet - the route stops AT this door, so keep looking
                }
                _forgeDoor = _session.Simulation!.Doors.Any(d => d.Open);
                return null;
            }
            var giver = simulation.Npcs.Single(n => n.Id == quest.NpcId);
            if (!_controller.InTalkReach(giver) && !WalkTo(giver))
                return null;
            if (_questAsked)
                return Fail($"{quest.NpcId}'s replies {string.Join(", ", quest.Replies)} did not start {quest.QuestId}");
            _controller.Talk(quest.NpcId);
            foreach (string reply in quest.Replies)
                _session.Submit(new ChooseCommand(simulation.PlayerId, reply));
            _session.Submit(new LeaveCommand(simulation.PlayerId));
            _questAsked = true;
            return null;
        }
        if (simulation.Quests.Single(q => q.Id == quest.QuestId).Status != Domain.Quests.QuestStatus.Active)
            return Fail($"{quest.QuestId} was not active once started");

        if (!_read)
        {
            if (simulation.Player.Inventory.FirstOrDefault(e => magic.Teaches.ContainsKey(e.DefId)) is not { } carried)
                return Fail("the book was not taken");
            _session.Submit(new UseItemCommand(simulation.PlayerId, carried.ItemId));
            _read = true;
            return null;
        }
        if (_worked < 0)
        {
            // A novice's working may fizzle; then it is worked again once the body is free.
            var formulas = _controller.Formulas();
            if (formulas.Length == 0)
                return Fail("reading the book taught nothing");
            if (!_cast && simulation.Combat.Phase == CombatPhase.Idle)
            {
                _controller.Cast(formulas.First(f => magic.Formulas[f].Targeting == Targeting.Self));
                _cast = true;
            }
            return null;
        }

        if (!_swung)
        {
            int spawned = _session.Setup.Combat.Spawns.Sum(s => s.Members.Length);
            if (simulation.Creatures.Count(c => c.Alive) != spawned)
                return Fail($"expected the {spawned} creatures the spawners place, found {simulation.Creatures.Count(c => c.Alive)} alive");
            if (simulation.Combat.Phase != CombatPhase.Idle)
                return null;
            _controller.Attack();
            _swung = true;
            return null;
        }
        if (!_started || !_missed)
            return null;

        var before = _session.Simulation!;
        string digest = before.StateDigest();
        long tick = before.WorldTick;
        int strain = before.Combat.Strain;
        int heard = before.CaptureRecord().Conversations.Sum(c => c.Heard.Length);
        // Measured, not assumed: the door clause below used to SAY "one was open before the save" without ever having
        // looked, so a run that had simply closed it again read exactly like a save/load fault.
        bool doorBefore = before.Doors.Any(d => d.Open);
        string quests = string.Join(";", before.Quests.Select(q => $"{q.Id}:{q.Status}:{string.Join(",", q.Objectives.Select(o => $"{o.Id}={o.Status}"))}"));
        _session.Save(SaveSlots.Quick);
        var loaded = _session.Load(SaveSlots.Quick);
        var after = _session.Simulation!;
        // EACH CLAUSE IS NAMED WHEN IT FAILS. The message used to report only the digest, the tick, the strain and the
        // lines heard - so when it fired with all four IDENTICAL it said "did not load back to the same state" and
        // gave no way to tell which of the other clauses had actually failed. A pass/fail gate that cannot say why it
        // failed costs a whole run to diagnose, and this one already had.
        var wrong = new List<string>();
        if (!loaded.IsComplete)
            wrong.Add("the load did not complete");
        if (after.StateDigest() != digest)
            wrong.Add($"digest {after.StateDigest()} vs {digest}");
        if (after.WorldTick != tick)
            wrong.Add($"tick {after.WorldTick} vs {tick}");
        if (!after.Doors.Any(d => d.Open))
            wrong.Add(doorBefore
                ? $"a door was open before the save and NONE is open after the load ({after.Doors.Length} doors) - the state did not round-trip"
                : $"no door is open, before the save or after the load ({after.Doors.Length} doors) - the leg never opened one");
        if (after.Combat.Strain != strain || strain <= 0)
            wrong.Add($"strain {after.Combat.Strain} vs {strain}");
        if (heard == 0)
            wrong.Add("no dialogue line was heard before the save");
        if (after.CaptureRecord().Conversations.Sum(c => c.Heard.Length) != heard)
            wrong.Add($"lines heard {after.CaptureRecord().Conversations.Sum(c => c.Heard.Length)} vs {heard}");
        string questsAfter = string.Join(";", after.Quests.Select(q => $"{q.Id}:{q.Status}:{string.Join(",", q.Objectives.Select(o => $"{o.Id}={o.Status}"))}"));
        if (questsAfter != quests)
            wrong.Add($"quests {questsAfter} vs {quests}");
        if (wrong.Count > 0)
            return Fail("the quicksave did not load back to the same state: " + string.Join("; ", wrong));

        GD.Print($"UNNAMED smoke: PASS - {_frames} frames, world tick {tick}, {after.Creatures.Length} creatures and {after.Npcs.Length} NPCs placed, " +
                 $"door opened, {PathToABook(_session.Setup, _session.Simulation!)!.Value.NpcId} gave a book in {heard} lines, {_controller.Formulas().Length} formulas read " +
                 $"from it and one worked (+{_worked} Strain), a swing ran and missed, {quest.NpcId} gave {quest.QuestId} " +
                 $"({after.Quests.Single(q => q.Id == quest.QuestId).Objectives.Count(o => o.Status == Domain.Quests.ObjectiveStatus.Satisfied)} of its objectives done at once), " +
                 $"save/load digest {digest[..23]}... identical");
        Cleanup();
        return 0;
    }

    /// <summary>
    /// The first NPC whose conversation hands over a book that teaches, and the replies from its first line to the one that
    /// does - a walk through the dialogue data, so no NPC, line or book is named here.
    ///
    /// NEAREST FIRST, AND THAT IS A FIX RATHER THAN A TIDY-UP. This walked `setup.Social.Npcs.Values` in dictionary
    /// order and took the first match, which is the CONTENT LOADER'S FILE ORDER and has nothing to do with where
    /// anybody is standing. `Walk` below steers in a straight line with no obstacle avoidance at all - the same
    /// limitation `Showcase.ToSel` was written to work around, and the reason its own comment records the owner
    /// watching a character run into a house and press against it. So a first match across the village is a walk
    /// that cannot arrive, and the run burns its 3000-frame budget against a wall.
    ///
    /// It worked in the demo by luck: the book-giver there happens to be the one standing nearest the end of the
    /// route. Placing Cairnwold's own cast moved the book to Berrin Garth, who keeps the lodge, and the run died at
    /// the lodge door with `read False, started False`. Ordering by distance makes the harness indifferent to which
    /// region's content it is walking and to the order the loader happened to read the files in.
    /// </summary>
    private (string NpcId, string[] Replies)? PathToABook(World.Runtime.SimulationSetup setup,
        World.Runtime.Simulation simulation)
    {
        foreach (var npc in NearestFirst(setup, simulation))
        {
            if (npc.DialogueId is not { } id || !setup.Social.Dialogues.TryGetValue(id, out var dialogue))
                continue;
            var queue = new Queue<(string Node, string[] Path)>();
            queue.Enqueue((dialogue.Root, Array.Empty<string>()));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (queue.TryDequeue(out var at))
            {
                if (!seen.Add(at.Node))
                    continue;
                foreach (var choice in dialogue.Nodes[at.Node].Choices)
                {
                    var path = at.Path.Append(choice.Id).ToArray();
                    if (choice.Consequences.OfType<TransferItemConsequence>().Any(t => t.ToPlayer && setup.Magic.Teaches.ContainsKey(t.ItemId)))
                        return (npc.Id, path);
                    if (choice.Next is { } next)
                        queue.Enqueue((next, path));
                }
            }
        }
        return null;
    }

    /// <summary>
    /// The content's NPCs, closest to the character first, so a leg of the walk is as short as the region allows.
    /// </summary>
    private IEnumerable<UNNAMED.Domain.Social.NpcDefinition> NearestFirst(World.Runtime.SimulationSetup setup,
        World.Runtime.Simulation simulation)
    {
        // The same body `Walk` steers, so "nearest" means nearest to where the character actually is.
        var body = _controller.Authoritative;
        return setup.Social.Npcs.Values.OrderBy(npc =>
        {
            var live = simulation.Npcs.FirstOrDefault(n => n.Id == npc.Id);
            if (live is null)
                return double.MaxValue;
            double dx = live.Body.XMm / 1000.0 - body.XMm / 1000.0;
            double dz = live.Body.ZMm / 1000.0 - body.ZMm / 1000.0;
            return dx * dx + dz * dz;
        });
    }

    /// <summary>
    /// The first NPC whose conversation starts a quest, the quest, and the replies from its first line to the one that starts it -
    /// a walk through the dialogue data, like <see cref="PathToABook"/>.
    /// </summary>
    private (string NpcId, string QuestId, string[] Replies)? PathToAQuest(World.Runtime.SimulationSetup setup,
        World.Runtime.Simulation simulation)
    {
        foreach (var npc in NearestFirst(setup, simulation))
        {
            if (npc.DialogueId is not { } id || !setup.Social.Dialogues.TryGetValue(id, out var dialogue))
                continue;
            var queue = new Queue<(string Node, string[] Path)>();
            queue.Enqueue((dialogue.Root, Array.Empty<string>()));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (queue.TryDequeue(out var at))
            {
                if (!seen.Add(at.Node))
                    continue;
                foreach (var choice in dialogue.Nodes[at.Node].Choices)
                {
                    var path = at.Path.Append(choice.Id).ToArray();
                    if (choice.Consequences.OfType<StartQuestConsequence>().FirstOrDefault() is { } start)
                        return (npc.Id, start.QuestId, path);
                    if (choice.Next is { } next)
                        queue.Enqueue((next, path));
                }
            }
        }
        return null;
    }

    private (double X, double Z)[]? _way;
    private int _wayAt;
    private string? _wayTo;
    private Body _wayFrom = new(0, 0, 0, 0);

    /// <summary>
    /// One frame of walking to within a hand's reach of someone (Playthrough.WayTo: round walls, in through open doors); true once there.
    /// Planned once, and again only if they move.
    /// </summary>
    private bool WalkTo(NpcView npc)
    {
        if (_way is null || _wayTo != npc.Id || Math.Abs(_wayFrom.XMm - npc.Body.XMm) + Math.Abs(_wayFrom.ZMm - npc.Body.ZMm) > 1_000)
            (_way, _wayAt, _wayTo, _wayFrom) = (Playthrough.WayTo(_session.Simulation!, _controller.Authoritative, npc.Body), 0, npc.Id, npc.Body);
        if (_wayAt < _way.Length && Walk(_way[_wayAt]))
            _wayAt++;
        return _wayAt >= _way.Length;
    }

    /// <summary>One frame of walking towards a point at a run; true once there.</summary>
    private bool Walk((double X, double Z) to)
    {
        var body = _controller.Authoritative;
        var direction = new Vector3((float)(to.X - body.XMm / 1000.0), 0, (float)(to.Z - body.ZMm / 1000.0));
        // A STALL IS THE ONE FAILURE THE TICK BUDGET CANNOT EXPLAIN, so it says so when it happens rather than at the
        // timeout. A run pressed against something for 3000 ticks and a run that was merely slow both end as "timed
        // out at tick 3000", and the difference is the whole diagnosis.
        //
        // IT IS MEASURED AS NO NET PROGRESS, NOT AS NO MOVEMENT, and the first version got that wrong: a body that
        // must move at least 0.02 m every 120 frames never tripped a "has not moved" test, and the run went on to
        // time out at the same spot - so what looked like a body pressed against a wall was a body OSCILLATING, and
        // the test for one could not see the other. This samples every two seconds and reports the net displacement
        // since the last sample, which is zero for a circle and zero for a jitter and non-zero for anything closing.
        double bx = body.XMm / 1000.0, bz = body.ZMm / 1000.0;
        _lastAt = (bx, bz);
        // THE PATH, NOT JUST ITS ENDPOINTS. A circle, a jitter and a push into something invisible all have the same
        // net displacement over two seconds and completely different causes, so a stall that reports only where the
        // body ended up cannot be told apart from the others. Seven samples two thirds of a second apart are kept and
        // printed when a stall is found, which is the difference between "it did not move" and "it moved in a loop".
        if (++_steerFrames % 20 == 0)
        {
            _path.Add((bx, bz));
            if (_path.Count > 7)
                _path.RemoveAt(0);
        }
        if (_steerFrames % 120 == 0)
        {
            double net = _path.Count < 2
                ? 9
                : Math.Sqrt((_path[^1].X - _path[0].X) * (_path[^1].X - _path[0].X) + (_path[^1].Z - _path[0].Z) * (_path[^1].Z - _path[0].Z));
            double short_by = Math.Sqrt((to.X - bx) * (to.X - bx) + (to.Z - bz) * (to.Z - bz));
            // TWO FAILURES, NOT ONE, AND THE FIRST VERSION OF THIS ONLY SAW THE SECOND. A body that stops and a body
            // that keeps moving without ever closing both fail Walk, and they report the same thing - "timed out,
            // still N m short". Reporting only net < 0.25 m found the stalls and printed NOTHING for the run that
            // orbits, because an orbit covers ground every two seconds and its net displacement never goes small.
            // So either condition reports: not moving, OR still far away while covering almost no ground.
            bool stalled = net < 0.25;
            bool milling = short_by > 0.4 && net < 1.5;
            if ((stalled || milling) && _trail < 12)
            {
                GD.PushWarning($"UNNAMED smoke: trail {_trail} {(stalled ? "STALLED" : "MILLING")}, {short_by:0.00} m from " +
                               $"({to.X:0.00}, {to.Z:0.00}), net {net:0.00} m, path " +
                               string.Join(" -> ", _path.Select(p => $"({p.X:0.00},{p.Z:0.00})")));
                _trail++;
            }
            _steerFrames = 0;
        }
        if (direction.Length() < 0.3f)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        // ARRIVAL NEEDS A SLOWER GAIT, NOT JUST A DIRECTION. This steered at Gait.Run the whole way, including the
        // last metre. A body moving at running speed with a finite turn rate cannot converge on a point a metre away
        // - it orbits it - and that is what two separate stalls measured this pass look like: clear geometry
        // underneath (the deriver proves 0.85 m and 0.54 m of clearance against a 0.35 m body, and the probe finds
        // nothing declared within 2.79 m of the forge leg's last stride), a body that keeps moving, and a target it
        // never closes on. Walk returns true only inside 0.3 m, so an orbit is a permanent failure. Inside three
        // metres the approach is a walk.
        _controller.SteerWorld(direction.Normalized(), direction.Length() < 3f ? Gait.Walk : Gait.Run, _camera);
        return false;
    }

    private int Fail(string reason)
    {
        GD.PushError($"UNNAMED smoke: FAIL - {reason}");
        Cleanup();
        return 1;
    }

    private void Cleanup()
    {
        _session.Dispose();   // the session holds the profile (M-07) until it lets go
        if (Directory.Exists(_profile))
            Directory.Delete(_profile, recursive: true);
    }
}
