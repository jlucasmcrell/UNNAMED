using UNNAMED.Persistence;

// Blocking waits on purpose: an await resumes through the shared thread pool, the very thing these tests must not depend on.
#pragma warning disable xUnit1031

namespace UNNAMED.Application.Tests;

/// <summary>
/// The save lane (the owner's ruling on the M7 E8.5 AsyncSave finding): background saves run on one thread of the session's own, in the
/// order queued, one at a time; a save starts at once however busy the shared thread pool is; what a save throws is its task's fault
/// and nothing else's; closing lets the queued saves finish and leaves no thread behind.
/// </summary>
public class SaveLaneTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void RepeatedSaves_RunInTheOrderQueued_NeverTwoAtOnce()
    {
        using var lane = new SaveLane("test lane");
        var order = new List<int>();
        int running = 0, most = 0;
        var tasks = Enumerable.Range(0, 60).Select(i => lane.Enqueue(() =>
        {
            int now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            lock (order)
                order.Add(i);
            Thread.SpinWait(20_000);
            Interlocked.Decrement(ref running);
        })).ToList();

        Assert.True(Task.WaitAll(tasks.ToArray(), Patience));
        Assert.Equal(Enumerable.Range(0, 60), order);
        Assert.Equal(1, most);
    }

    [Fact]
    public void ASaveThatThrows_FaultsItsOwnTask_AndTheNextStillRuns()
    {
        using var lane = new SaveLane("test lane");
        var failed = lane.Enqueue(() => throw new IOException("the disk is full"));
        bool ran = false;
        var next = lane.Enqueue(() => ran = true);

        Assert.True(next.Wait(Patience));
        Assert.True(ran);
        Assert.Equal("the disk is full", Assert.Throws<AggregateException>(() => failed.Wait(Patience)).InnerException!.Message);
    }

    [Fact]
    public void Closing_WithSavesQueued_FinishesThem_ThenEndsTheThread_AndTakesNoMore()
    {
        var lane = new SaveLane("test lane");
        using var gate = new ManualResetEventSlim(false);
        var written = new List<int>();
        var held = lane.Enqueue(() => gate.Wait(Patience));
        var queued = Enumerable.Range(1, 3).Select(i => lane.Enqueue(() => { lock (written) written.Add(i); })).ToList();
        Assert.True(lane.IsRunning);

        // Closed from a thread of its own, not the shared pool, which a busy test run can starve.
        bool closed = false;
        var closer = new Thread(() => closed = lane.Close(Patience)) { IsBackground = true };
        closer.Start();
        Thread.Sleep(50);
        Assert.True(closer.IsAlive);   // the held save and the three behind it are still to run
        gate.Set();

        Assert.True(closer.Join(Patience) && closed);
        Assert.True(held.IsCompletedSuccessfully && queued.All(t => t.IsCompletedSuccessfully));
        Assert.Equal(new[] { 1, 2, 3 }, written);
        Assert.False(lane.IsRunning);
        Assert.IsType<ObjectDisposedException>(Record.Exception(() => { _ = lane.Enqueue(() => { }); }));
    }

    [Fact]
    public void Closing_WhileASaveRuns_LetsItFinish_AndTheThreadEnds()
    {
        var lane = new SaveLane("test lane");
        using var gate = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        var running = lane.Enqueue(() =>
        {
            started.Set();
            gate.Wait(Patience);
        });
        Assert.True(started.Wait(Patience));

        Assert.False(lane.Close(TimeSpan.FromMilliseconds(50)));   // still writing: not cut off
        gate.Set();
        Assert.True(running.Wait(Patience));
        Assert.True(SpinWait.SpinUntil(() => !lane.IsRunning, Patience), "the lane's thread outlived its last save");
    }

    [Fact]
    public void ALaneThatNeverSaved_HasNoThread()
    {
        var lane = new SaveLane("test lane");
        Assert.False(lane.IsRunning);
        Assert.True(lane.Close(TimeSpan.Zero));
    }

    [Fact]
    public void ALaneDroppedWithoutClosing_WritesWhatItWasGiven_AndItsThreadEnds()
    {
        var (thread, written) = SaveOnADroppedLane();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.True(thread.Join(Patience), "the dropped lane's thread was left waiting");
        Assert.True(written.IsCompletedSuccessfully);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (Thread Thread, Task Written) SaveOnADroppedLane()
    {
        var lane = new SaveLane("test lane");
        Thread? thread = null;
        var first = lane.Enqueue(() => thread = Thread.CurrentThread);
        Assert.True(first.Wait(Patience));
        var second = lane.Enqueue(() => Thread.Sleep(100));   // still queued or running when the lane is collected
        return (thread!, second);
    }

    [Fact]
    public void ASessionThatSavedInTheBackground_LeavesNoLaneThreadWhenDisposed()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Assert.False(session.SaveLaneRunning);
        session.NewGame("Wanderer", seed: 42);
        session.SaveInBackground(SaveSlots.Quick);
        session.SaveInBackground(SaveSlots.Manual("second"));
        Assert.True(session.SaveLaneRunning);

        session.Dispose();   // the two saves finish first, then the lane's thread ends
        Assert.False(session.SaveLaneRunning);
        using var next = Harness.Boot(profile);
        Assert.True(next.Load(SaveSlots.Quick).IsComplete);
        Assert.True(next.Load(SaveSlots.Manual("second")).IsComplete);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}

/// <summary>Runs alone, after every parallel test: it holds every thread of the shared pool for a moment, which would slow the others.</summary>
[CollectionDefinition(nameof(ThreadPoolSaturation), DisableParallelization = true)]
public class ThreadPoolSaturation
{
}

[Collection(nameof(ThreadPoolSaturation))]
public class SaveLaneUnderASaturatedPoolTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The finding the lane answers: with every thread of the shared pool held (and more work queued behind them), a save on the lane
    /// still begins at once - on the pool it waited up to 13 s just to start.
    /// </summary>
    [Fact]
    public void ASaveBegins_WhileTheSharedThreadPoolIsSaturated()
    {
        using var lane = new SaveLane("test lane");
        // Not disposed: blockers still queued when the test ends reach it afterwards, find it set, and return.
        var hold = new ManualResetEventSlim(false);
        int queued = 0, blocked = 0;
        void Block()
        {
            queued++;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Interlocked.Increment(ref blocked);
                hold.Wait(TimeSpan.FromSeconds(30));
            });
        }
        int Waiting() => queued - Volatile.Read(ref blocked);
        try
        {
            // However far the pool has grown earlier in the run, hold every thread it has and leave work queued behind them.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < Patience && queued < 4096)
            {
                for (int i = 0; i < 16; i++)
                    Block();
                Thread.Sleep(100);
                if (Waiting() >= 16)
                    break;
            }
            Assert.True(Waiting() >= 16,
                $"the pool was never saturated: {queued} queued, {blocked} running, {ThreadPool.ThreadCount} pool threads");

            clock.Restart();
            using var began = new ManualResetEventSlim(false);
            int waitingAtStart = -1;
            var save = lane.Enqueue(() =>
            {
                waitingAtStart = Waiting();
                began.Set();
            });
            Assert.True(began.Wait(TimeSpan.FromSeconds(1)),
                $"the save had not begun after {clock.ElapsedMilliseconds} ms ({Waiting()} pool items waiting)");
            Assert.True(save.Wait(Patience));
            Assert.True(waitingAtStart > 0, "the pool was no longer saturated when the save began");
        }
        finally
        {
            hold.Set();
        }
    }
}
