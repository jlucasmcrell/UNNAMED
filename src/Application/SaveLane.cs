// UNNAMED Application - the lane background saves run on (the owner's ruling on the M7 E8.5 AsyncSave finding)
// No Godot references - pure C#

namespace UNNAMED.Application;

/// <summary>
/// The one lane a session's background saves run on: a single thread of its own, started with the first save and kept until the lane
/// closes, taking saves in the order they were queued, one at a time. A save therefore starts as soon as the one before it has finished,
/// however busy the shared thread pool is - on the pool, a save waited up to 13 s just to begin while other work held every pool thread.
/// Each save's outcome is its task: completed, or faulted with what it threw. Closing takes no more saves, lets the queued ones finish,
/// and ends the thread.
/// </summary>
public sealed class SaveLane : IDisposable
{
    // The thread holds only the worker, never the lane: a lane its owner drops without closing is collected, closes the worker, and the
    // thread ends once the saves it was given are written - a session never disposed leaves no thread behind.
    private readonly Worker _worker;

    public SaveLane(string name) => _worker = new Worker(name);

    ~SaveLane() => _worker.Close(TimeSpan.Zero);

    /// <summary>Whether the lane's thread is alive: from the first save until the lane closes and its queue is done.</summary>
    public bool IsRunning => _worker.IsRunning;

    /// <summary>Queue a save behind every one queued before it. Refused once the lane has closed.</summary>
    public Task Enqueue(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _worker.Enqueue(work, this);
    }

    /// <summary>
    /// Take no more saves, let the queued ones finish, and end the thread: true when it has ended within <paramref name="timeout"/>. A
    /// save still running at the timeout goes on to its end on the lane's thread, which then ends.
    /// </summary>
    public bool Close(TimeSpan timeout)
    {
        GC.SuppressFinalize(this);
        return _worker.Close(timeout);
    }

    public void Dispose() => Close(Timeout.InfiniteTimeSpan);

    private sealed class Worker
    {
        private readonly object _gate = new();
        private readonly Queue<(Action Work, TaskCompletionSource Done)> _queue = new();
        private readonly string _name;
        private Thread? _thread;
        private bool _closed;

        public Worker(string name) => _name = name;

        public bool IsRunning
        {
            get
            {
                lock (_gate)
                    return _thread is { IsAlive: true };
            }
        }

        public Task Enqueue(Action work, SaveLane lane)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closed, lane);
                _queue.Enqueue((work, done));
                if (_thread is null)
                {
                    // Background, so a process that exits without closing the lane is not held open by it; quitting waits for the saves.
                    _thread = new Thread(Run) { IsBackground = true, Name = _name };
                    _thread.Start();
                }
                else
                {
                    Monitor.Pulse(_gate);
                }
            }
            return done.Task;
        }

        private void Run()
        {
            while (true)
            {
                (Action Work, TaskCompletionSource Done) next;
                lock (_gate)
                {
                    while (_queue.Count == 0)
                    {
                        if (_closed)
                            return;
                        Monitor.Wait(_gate);
                    }
                    next = _queue.Dequeue();
                }
                try
                {
                    next.Work();
                    next.Done.SetResult();
                }
                catch (Exception e)
                {
                    next.Done.SetException(e);
                }
            }
        }

        public bool Close(TimeSpan timeout)
        {
            Thread? thread;
            lock (_gate)
            {
                _closed = true;
                Monitor.PulseAll(_gate);
                thread = _thread;
            }
            return thread is null || thread.Join(timeout);
        }
    }
}
