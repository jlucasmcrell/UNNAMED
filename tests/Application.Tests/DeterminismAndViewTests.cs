using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The command path's two guarantees: a session is a pure function of its commands and the tick boundaries they applied
/// at (PROTOTYPE.md C3), and a view that listens to everything changes nothing (D-11, §6.3's view-subscription test).
/// </summary>
public class DeterminismAndViewTests
{
    /// <summary>
    /// 200 commands delivered the way presentation delivers them: queued between frames of uneven length, then applied
    /// at whatever tick boundary the frame reaches. Moves in every direction and gait, doors in and out of reach, and
    /// commands that must be refused.
    /// </summary>
    private static void PlayScript(GameSession session, int seed)
    {
        var random = new Random(seed);
        var simulation = session.Simulation!;
        for (int i = 0; i < 200; i++)
        {
            GameCommand command = random.Next(100) switch
            {
                < 70 => new MoveCommand(simulation.PlayerId, Harness.Toward(random.Next(-1000, 1001), random.Next(-1000, 1001), (Gait)random.Next(3))),
                < 80 => new MoveCommand(simulation.PlayerId, MoveIntent.Idle(random.Next(MoveIntent.FullTurnMdeg))),
                < 90 => new InteractCommand(simulation.PlayerId, "door.longhouse"),
                < 95 => new InteractCommand(simulation.PlayerId, "door.forge_shed"),
                _ => new InteractCommand(simulation.PlayerId, "door.nowhere"),
            };
            session.Submit(command);
            for (int frames = random.Next(1, 4); frames > 0; frames--)
                session.Frame(0.008 + random.NextDouble() * 0.05);
        }
    }

    /// <summary>A new world, walked to the longhouse door and saved, so two sessions can start from the identical state.</summary>
    private static void SaveStart(TempProfile profile)
    {
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        // Start at the longhouse door so the script's door commands land in reach part of the time.
        Assert.True(Harness.WalkPath(session, (56, 56), (55, 44)));
        session.Save(SaveSlots.Manual("start"));
    }

    [Fact]
    public void AScripted200CommandSession_ThroughFrames_EqualsItsReplayThroughTheCommandBus()
    {
        using var profile = new TempProfile();
        SaveStart(profile);

        var played = Harness.Boot(profile);
        played.Load(SaveSlots.Manual("start"));
        PlayScript(played, seed: 7);
        var log = played.Simulation!.CommandLog;
        Assert.Equal(200, log.Count(e => e.Tick >= 0));
        Assert.Contains(log, e => e.RejectedReason is not null);
        Assert.Contains(log, e => e.Command is InteractCommand && e.RejectedReason is null);

        var replay = Harness.Boot(profile);
        replay.Load(SaveSlots.Manual("start"));
        var simulation = replay.Simulation!;
        foreach (var entry in log)
        {
            while (simulation.WorldTick < entry.Tick)
                simulation.Step();
            simulation.Enqueue(entry.Command);
            simulation.DrainCommands();
        }
        while (simulation.WorldTick < played.Simulation.WorldTick)
            simulation.Step();

        Assert.Equal(played.Simulation.StateDigest(), simulation.StateDigest());
        Assert.Equal(log.Select(e => (e.Tick, e.RejectedReason)), simulation.CommandLog.Select(e => (e.Tick, e.RejectedReason)));
    }

    [Fact]
    public void AViewThatListensToEverything_SeesExactlyWhatHappened_AndWritesNothing()
    {
        using var profile = new TempProfile();
        SaveStart(profile);

        // Control: the same session with nobody listening.
        var control = Harness.Boot(profile);
        control.Load(SaveSlots.Manual("start"));
        PlayScript(control, seed: 11);

        var session = Harness.Boot(profile);
        session.Load(SaveSlots.Manual("start"));
        var start = session.Simulation!.Player.Body;
        var knownAtStart = session.Simulation.Player.Discoveries.Select(d => d.LocationId).ToHashSet();
        long xpAtStart = session.Simulation.Player.Progression.LifetimeXp.Values.Sum();
        var moved = Harness.Record<BodyMoved>(session);
        var rejected = Harness.Record<CommandRejected>(session);
        var toggled = Harness.Record<DoorToggled>(session);
        var flags = Harness.Record<WorldFlagChanged>(session);
        var discovered = Harness.Record<LocationDiscovered>(session);
        var xp = Harness.Record<ExperienceGained>(session);
        // A view that tries its hardest to write: everything it receives is an immutable copy.
        session.Subscribe<BodyMoved>(e => _ = e with { To = e.From });
        PlayScript(session, seed: 11);
        var simulation = session.Simulation!;
        var log = simulation.CommandLog;

        Assert.Equal(control.Simulation!.StateDigest(), simulation.StateDigest());

        // Rejections: one event per refused command, in order, with the logged reason.
        Assert.Equal(log.Where(e => e.RejectedReason is not null).Select(e => (e.Command, e.RejectedReason)),
            rejected.Select(e => (e.Command, (string?)e.Reason)));
        // Doors: one toggle per accepted interaction, each with its flag change.
        Assert.Equal(log.Count(e => e.Command is InteractCommand && e.RejectedReason is null), toggled.Count);
        Assert.Equal(toggled.Count, flags.Count);
        Assert.Equal(simulation.Doors.Where(d => d.Open).Select(d => d.Site.Key).OrderBy(k => k),
            toggled.GroupBy(t => t.DoorKey).Where(g => g.Last().Open).Select(g => g.Key).OrderBy(k => k));
        // Movement: an unbroken chain from where the body started to where it is.
        Assert.NotEmpty(moved);
        Assert.Equal(start, moved[0].From);
        Assert.All(moved.Zip(moved.Skip(1)), pair => Assert.Equal(pair.First.To, pair.Second.From));
        Assert.Equal(simulation.Player.Body, moved[^1].To);
        // Discoveries and the XP they earned.
        Assert.Equal(simulation.Player.Discoveries.Select(d => d.LocationId).Where(id => !knownAtStart.Contains(id)).Order(),
            discovered.Select(d => d.LocationId).Order());
        Assert.Equal(simulation.Player.Progression.LifetimeXp.Values.Sum() - xpAtStart, xp.Sum(e => e.Awarded));
    }

    [Fact]
    public void SavingIsNotAnEvent_AndLoadingPublishesNothing()
    {
        using var profile = new TempProfile();
        SaveStart(profile);
        var session = Harness.Boot(profile);
        var anything = new List<object>();
        session.Subscribe<BodyMoved>(anything.Add);
        session.Subscribe<CellTierChanged>(anything.Add);
        session.Subscribe<LocationDiscovered>(anything.Add);

        session.Load(SaveSlots.Manual("start"));
        session.Save(SaveSlots.Quick);

        Assert.Empty(anything);
    }
}
