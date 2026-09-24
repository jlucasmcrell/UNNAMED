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
    private static readonly (double X, double Z)[] Route = { (56, 56), (55, 44) };
    private static readonly (double X, double Z)[] Inside = { (52.5, 44), (46, 44.6) };
    private static readonly (double X, double Z)[] ToTheForge = { (52.5, 44), (56.5, 43), (57, 35), (58.8, 34), (61.5, 34) };

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly string _profile;
    private int _frames;
    private int _waypoint;
    private int _inside;
    private bool _asked;
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

    public Smoke(GameSession session, PlayerController controller, CameraRig camera, string profile)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _profile = profile;
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
                        $"door {_controller.IsOpen("door.longhouse")}, inside {_inside}, taken {_taken}, read {_read}, cast {_cast}, worked {_worked}, " +
                        $"swung {_swung}, started {_started}, missed {_missed}");
        }

        if (_waypoint < Route.Length)
        {
            if (Walk(Route[_waypoint]))
                _waypoint++;
            return null;
        }

        if (!_controller.IsOpen("door.longhouse"))
        {
            if (!_asked && _controller.FocusOn(_camera) is { Kind: FocusKind.Door } door)
            {
                _controller.Interact(door.Key);
                _asked = true;
            }
            return null;
        }

        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        if (_inside < Inside.Length)
        {
            if (Walk(Inside[_inside]))
                _inside++;
            return null;
        }
        // Whoever's conversation hands over a book that teaches, and the replies that reach it: found in the data, not named.
        if (PathToABook(_session.Setup) is not { } path)
            return Fail("no conversation hands over a book that teaches");
        var speaker = simulation.Npcs.Single(n => n.Id == path.NpcId);
        if (!_talked)
        {
            // Up to within a hand's reach of them, from where the character stands.
            var body = _controller.Authoritative;
            var away = new Vector3(body.XMm - speaker.Body.XMm, 0, body.ZMm - speaker.Body.ZMm).Normalized() * 1.3f;
            if (!Walk((speaker.Body.XMm / 1000.0 + away.X, speaker.Body.ZMm / 1000.0 + away.Z)))
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
        if (PathToAQuest(_session.Setup) is not { } quest)
            return Fail("no conversation starts a quest");
        if (!simulation.Quests.Any(q => q.Id == quest.QuestId))
        {
            if (_forge < ToTheForge.Length)
            {
                // The way there passes a door: the nearest one opens when the character stands at it (the layout's, not named).
                if (_forge == ToTheForge.Length - 1 && !_forgeDoor)
                {
                    var here = _controller.Authoritative;
                    var door = _session.Setup.Layout.Doors.OrderBy(d => Math.Pow(d.ClosedFootprint.CenterXMm - here.XMm, 2)
                        + Math.Pow(d.ClosedFootprint.CenterZMm - here.ZMm, 2)).First();
                    if (!_controller.IsOpen(door.Key))
                        _controller.Interact(door.Key);
                    _forgeDoor = true;
                    return null;
                }
                if (Walk(ToTheForge[_forge]))
                    _forge++;
                return null;
            }
            var giver = simulation.Npcs.Single(n => n.Id == quest.NpcId);
            var at = _controller.Authoritative;
            var toward = new Vector3(at.XMm - giver.Body.XMm, 0, at.ZMm - giver.Body.ZMm).Normalized() * 1.3f;
            if (!Walk((giver.Body.XMm / 1000.0 + toward.X, giver.Body.ZMm / 1000.0 + toward.Z)))
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
        string quests = string.Join(";", before.Quests.Select(q => $"{q.Id}:{q.Status}:{string.Join(",", q.Objectives.Select(o => $"{o.Id}={o.Status}"))}"));
        _session.Save(SaveSlots.Quick);
        var loaded = _session.Load(SaveSlots.Quick);
        var after = _session.Simulation!;
        if (!loaded.IsComplete || after.StateDigest() != digest || after.WorldTick != tick
            || !after.Doors.Single(d => d.Site.Key == "door.longhouse").Open || after.Combat.Strain != strain || strain <= 0
            || heard == 0 || after.CaptureRecord().Conversations.Sum(c => c.Heard.Length) != heard
            || string.Join(";", after.Quests.Select(q => $"{q.Id}:{q.Status}:{string.Join(",", q.Objectives.Select(o => $"{o.Id}={o.Status}"))}")) != quests)
            return Fail($"the quicksave did not load back to the same state (digest {after.StateDigest()} vs {digest}, tick {after.WorldTick} vs {tick}, strain {after.Combat.Strain} vs {strain}, lines heard {heard})");

        GD.Print($"UNNAMED smoke: PASS - {_frames} frames, world tick {tick}, {after.Creatures.Length} creatures and {after.Npcs.Length} NPCs placed, " +
                 $"door opened, {PathToABook(_session.Setup)!.Value.NpcId} gave a book in {heard} lines, {_controller.Formulas().Length} formulas read " +
                 $"from it and one worked (+{_worked} Strain), a swing ran and missed, {quest.NpcId} gave {quest.QuestId} " +
                 $"({after.Quests.Single(q => q.Id == quest.QuestId).Objectives.Count(o => o.Status == Domain.Quests.ObjectiveStatus.Satisfied)} of its objectives done at once), " +
                 $"save/load digest {digest[..23]}... identical");
        Cleanup();
        return 0;
    }

    /// <summary>
    /// The first NPC whose conversation hands over a book that teaches, and the replies from its first line to the one that
    /// does - a walk through the dialogue data, so no NPC, line or book is named here.
    /// </summary>
    private static (string NpcId, string[] Replies)? PathToABook(World.Runtime.SimulationSetup setup)
    {
        foreach (var npc in setup.Social.Npcs.Values)
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
    /// The first NPC whose conversation starts a quest, the quest, and the replies from its first line to the one that starts it -
    /// a walk through the dialogue data, like <see cref="PathToABook"/>.
    /// </summary>
    private static (string NpcId, string QuestId, string[] Replies)? PathToAQuest(World.Runtime.SimulationSetup setup)
    {
        foreach (var npc in setup.Social.Npcs.Values)
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

    /// <summary>One frame of walking towards a point at a run; true once there.</summary>
    private bool Walk((double X, double Z) to)
    {
        var body = _controller.Authoritative;
        var direction = new Vector3((float)(to.X - body.XMm / 1000.0), 0, (float)(to.Z - body.ZMm / 1000.0));
        if (direction.Length() < 0.3f)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        _controller.SteerWorld(direction.Normalized(), Gait.Run, _camera);
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
        if (Directory.Exists(_profile))
            Directory.Delete(_profile, recursive: true);
    }
}
