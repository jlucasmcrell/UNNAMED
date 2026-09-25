using UNNAMED.Persistence;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Phase-1 technical audit, P-01: a save is taken on the frame - an immutable snapshot at a tick boundary - and encoded, written,
/// hashed and verified on a worker, through the same atomic sequence, one after another in the order taken. What happens when a second
/// autosave falls due while one is written, when a quicksave is asked for during an autosave, when a background save fails, and when
/// the game quits with one in flight - each held at a step of its commit by a hook, the way a slow disk would hold it.
/// </summary>
public class AsyncSaveTests
{
    /// <summary>A commit held at a step until the test lets it go, counting how many commits reached that step.</summary>
    private sealed class Held : IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(false);
        private readonly SaveStep _at;
        private int _reached;

        public Held(SaveStep at) => _at = at;

        public int Reached => Volatile.Read(ref _reached);

        public Action<SaveStep> Hook => step =>
        {
            if (step != _at)
                return;
            Interlocked.Increment(ref _reached);
            Assert.True(_gate.Wait(TimeSpan.FromSeconds(30)), "the test never let the save go");
        };

        public void WaitReached(int count)
        {
            for (int i = 0; i < 500 && Reached < count; i++)
                Thread.Sleep(10);
            Assert.Equal(count, Reached);
        }

        public void Release() => _gate.Set();

        public void Dispose()
        {
            _gate.Set();
            _gate.Dispose();
        }
    }

    private static GameSession Boot(TempProfile profile, Action<SaveStep> hook) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root) { SaveStepHook = hook });

    /// <summary>Frames of a quarter second until the playtime reaches <paramref name="seconds"/>; the world tick of the frame that got there.</summary>
    private static long PlayTo(GameSession session, double seconds, List<SaveOutcome>? outcomes = null)
    {
        while (session.PlaytimeSeconds < seconds)
        {
            var saves = session.Frame(GameSession.MaxFrameSeconds).Saves;
            outcomes?.AddRange(saves);
        }
        return session.Simulation!.WorldTick;
    }

    [Fact]
    public void AnAutosave_IsWrittenOffTheFrame_AndHoldsTheWorldAsItWasTaken()
    {
        using var profile = new TempProfile();
        using var held = new Held(SaveStep.StagingWritten);
        using var session = Boot(profile, held.Hook);
        session.NewGame("Wanderer", seed: 42);

        long taken = PlayTo(session, 300);   // the autosave is taken at the end of this frame
        held.WaitReached(1);
        // The frame goes on while the save is held mid-commit: it was not written on the frame's thread.
        var outcomes = new List<SaveOutcome>();
        long later = PlayTo(session, 305, outcomes);
        Assert.True(later > taken);
        Assert.Empty(outcomes);

        held.Release();
        Assert.True(session.WaitForSaves(TimeSpan.FromSeconds(10)));
        outcomes.AddRange(session.Frame(0).Saves);
        var autosave = Assert.Single(outcomes);
        Assert.Equal((SaveSlots.Auto(1), true, 300.0, (string?)null), (autosave.Slot, autosave.Auto, autosave.CapturedAtPlaytime, autosave.Failure));
        Assert.Equal(taken, session.Load(SaveSlots.Auto(1)).Manifest.WorldTick);   // the world as it was taken, not as it went on
    }

    [Fact]
    public void ASecondAutosaveDue_WhileOneIsBeingWritten_IsNotStarted_AndTheNextCountsFromTheOneWritten()
    {
        using var profile = new TempProfile();
        using var held = new Held(SaveStep.StagingWritten);
        using var session = Boot(profile, held.Hook);
        session.NewGame("Wanderer", seed: 42);

        PlayTo(session, 300);
        held.WaitReached(1);
        var outcomes = new List<SaveOutcome>();
        PlayTo(session, 650, outcomes);   // a second would have fallen due at 600
        Thread.Sleep(100);
        Assert.Equal(1, held.Reached);
        Assert.Empty(outcomes);

        held.Release();
        session.WaitForSaves(TimeSpan.FromSeconds(10));
        // The one written is reported; the next was due 300 s after it was taken, so it is taken on that same frame, at 650.
        outcomes.AddRange(session.Frame(0).Saves);
        session.WaitForSaves(TimeSpan.FromSeconds(10));
        outcomes.AddRange(session.Frame(0).Saves);
        Assert.Equal(new[] { (SaveSlots.Auto(1), 300.0), (SaveSlots.Auto(2), 650.0) }, outcomes.Select(o => (o.Slot, o.CapturedAtPlaytime)));
        Assert.All(outcomes, o => Assert.Null(o.Failure));
    }

    [Fact]
    public void AQuicksave_AskedForDuringAnAutosave_WaitsItsTurn_AndHoldsTheLaterWorld()
    {
        using var profile = new TempProfile();
        using var held = new Held(SaveStep.StagingWritten);
        using var session = Boot(profile, held.Hook);
        session.NewGame("Wanderer", seed: 42);

        long autosaved = PlayTo(session, 300);
        held.WaitReached(1);
        long quicksaved = PlayTo(session, 302);
        session.SaveInBackground(SaveSlots.Quick);   // taken now, while the autosave is held
        Thread.Sleep(100);
        Assert.Equal(1, held.Reached);   // it waits: one writer, in the order taken

        held.Release();
        session.WaitForSaves(TimeSpan.FromSeconds(10));
        var outcomes = session.Frame(0).Saves;
        Assert.Equal(new[] { (SaveSlots.Auto(1), true), (SaveSlots.Quick, false) }, outcomes.Select(o => (o.Slot, o.Auto)));
        Assert.All(outcomes, o => Assert.Null(o.Failure));
        Assert.Equal(2, held.Reached);
        Assert.Equal(autosaved, session.Load(SaveSlots.Auto(1)).Manifest.WorldTick);
        Assert.Equal(quicksaved, session.Load(SaveSlots.Quick).Manifest.WorldTick);
    }

    [Fact]
    public void ABackgroundSaveThatFails_IsAnOutcome_NotAnException_AndThePreviousSaveStands()
    {
        using var profile = new TempProfile();
        bool failing = false;
        using var session = Boot(profile, step =>
        {
            if (failing && step == SaveStep.IntegrityRootWritten)
                throw new IOException("the disk is full");
        });
        session.NewGame("Wanderer", seed: 42);
        PlayTo(session, 10);
        session.Save(SaveSlots.Quick);
        long first = session.Simulation!.WorldTick;

        failing = true;
        PlayTo(session, 20);
        session.SaveInBackground(SaveSlots.Quick);
        session.WaitForSaves(TimeSpan.FromSeconds(10));
        var outcome = Assert.Single(session.Frame(GameSession.MaxFrameSeconds).Saves);

        Assert.Equal((SaveSlots.Quick, false, "the disk is full"), (outcome.Slot, outcome.Auto, outcome.Failure));
        failing = false;
        new SaveStore(profile.Root).RecoverInterruptedCommits();   // what the next boot does with what the failed commit left
        Assert.Equal(first, session.Load(SaveSlots.Quick).Manifest.WorldTick);
    }

    [Fact]
    public void QuittingWithASaveInFlight_FinishesIt_AndTheNextBootLoadsIt()
    {
        using var profile = new TempProfile();
        using var held = new Held(SaveStep.StagingPromoted);
        long taken;
        var session = Boot(profile, held.Hook);
        session.NewGame("Wanderer", seed: 42);
        PlayTo(session, 30);
        taken = session.Simulation!.WorldTick;
        session.SaveInBackground(SaveSlots.Quick);
        held.WaitReached(1);

        Assert.False(session.WaitForSaves(TimeSpan.FromMilliseconds(50)));   // still being written
        using (new Timer(_ => held.Release(), null, 200, Timeout.Infinite))
            session.Dispose();   // quitting: waits for it

        using var next = Harness.Boot(profile);
        Assert.Equal(taken, next.StartChoice().Continue!.WorldTick);
        Assert.True(next.Load(SaveSlots.Quick).IsComplete);
    }
}
