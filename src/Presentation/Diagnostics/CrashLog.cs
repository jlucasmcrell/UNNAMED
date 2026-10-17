// UNNAMED Presentation - the crash log: an unhandled exception written to a local file, and said at the next launch (WP10, §2.1 R2)
// Godot-free: plain .NET file I/O, so the tests compile it as the game does. Local only: nothing here, or anywhere, sends anything.

using System.Text;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>What the last run left behind when it closed unexpectedly: the log it wrote and the kind of failure.</summary>
public sealed record CrashNotice(string LogFile, string What);

/// <summary>
/// Local-only crash hooks (accepted 2026-10-17: no upload, telemetry or update check, ever). An exception nothing caught - on any thread
/// (<see cref="AppDomain.UnhandledException"/>) or in a task nobody awaited (<see cref="TaskScheduler.UnobservedTaskException"/>) - is
/// written to <c>crash_N.txt</c> in the log folder, and a marker names it. The next launch takes the marker (<see cref="TakeNotice"/>)
/// and the title says the game closed unexpectedly, with the way to the folder. The files are numbered rather than dated: the wall
/// clock is read only by the session. The files and the marker are the only trace; nothing leaves the machine.
/// </summary>
public sealed class CrashLog : IDisposable
{
    public const string MarkerName = "crash_marker.txt";
    private const string Prefix = "crash_";

    /// <summary>The most crash files kept: the oldest go first.</summary>
    public const int Kept = 10;

    private readonly object _gate = new();
    private bool _installed;

    public CrashLog(string directory) => Directory = directory;

    public string Directory { get; }

    /// <summary>What the build is, for the head of each file.</summary>
    public string Build { get; init; } = string.Empty;

    public void Install()
    {
        if (_installed)
            return;
        AppDomain.CurrentDomain.UnhandledException += Unhandled;
        TaskScheduler.UnobservedTaskException += Unobserved;
        _installed = true;
    }

    public void Dispose()
    {
        if (!_installed)
            return;
        AppDomain.CurrentDomain.UnhandledException -= Unhandled;
        TaskScheduler.UnobservedTaskException -= Unobserved;
        _installed = false;
    }

    private void Unhandled(object? sender, UnhandledExceptionEventArgs e) =>
        Write(e.ExceptionObject as Exception, e.IsTerminating ? "unhandled exception (terminating)" : "unhandled exception", e.ExceptionObject);

    private void Unobserved(object? sender, UnobservedTaskExceptionEventArgs e) => Write(e.Exception, "unobserved task exception", null);

    /// <summary>
    /// Write one crash: its file, then the marker naming it, keeping no more than <see cref="Kept"/> files. Never throws - a crash
    /// handler that fails must not hide the crash it was writing. Returns the file written, or null when nothing could be.
    /// </summary>
    public string? Write(Exception? exception, string what, object? raw = null)
    {
        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var numbers = Numbers();
                int next = numbers.Count == 0 ? 1 : numbers.Max() + 1;
                string file = Path.Combine(Directory, $"{Prefix}{next:D4}.txt");
                var text = new StringBuilder()
                    .AppendLine("Otherreach closed unexpectedly.")
                    .AppendLine($"What: {what}")
                    .AppendLine(Build.Length > 0 ? $"Build: {Build}" : "Build: unknown")
                    .AppendLine($"Runtime: {Environment.Version} on {Environment.OSVersion}")
                    .AppendLine()
                    .AppendLine(exception?.ToString() ?? raw?.ToString() ?? "(no exception object)");
                File.WriteAllText(file, text.ToString());
                File.WriteAllText(Path.Combine(Directory, MarkerName), $"{Path.GetFileName(file)}\n{what}\n");
                foreach (int old in numbers.OrderBy(n => n).Take(Math.Max(0, numbers.Count + 1 - Kept)))
                    File.Delete(Path.Combine(Directory, $"{Prefix}{old:D4}.txt"));
                return file;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>The notice the last run left, taken once: the marker is removed, so the next launch after this one says nothing.</summary>
    public static CrashNotice? TakeNotice(string directory)
    {
        string marker = Path.Combine(directory, MarkerName);
        try
        {
            if (!File.Exists(marker))
                return null;
            string[] lines = File.ReadAllLines(marker);
            File.Delete(marker);
            string name = lines.Length > 0 ? Path.GetFileName(lines[0].Trim()) : string.Empty;
            return new CrashNotice(Path.Combine(directory, name), lines.Length > 1 ? lines[1].Trim() : "unknown");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private List<int> Numbers() =>
        System.IO.Directory.EnumerateFiles(Directory, Prefix + "*.txt")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(n => int.TryParse(n![Prefix.Length..], out int i) ? i : -1)
            .Where(i => i > 0)
            .ToList();
}
