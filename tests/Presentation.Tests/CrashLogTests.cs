using UNNAMED.Presentation.Diagnostics;

namespace UNNAMED.Presentation.Tests;

/// <summary>WP10's local crash hooks (§2.1 R2): a crash is written to a numbered local file and a marker; the next launch takes the notice once.</summary>
public sealed class CrashLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "unnamed-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void ACrash_IsWritten_AndTheNextLaunchIsTold_Once()
    {
        var log = new CrashLog(_folder) { Build = "test" };
        string? file = log.Write(new InvalidOperationException("the tick fell over"), "unhandled exception (terminating)");

        Assert.Equal(Path.Combine(_folder, "crash_0001.txt"), file);
        string text = File.ReadAllText(file!);
        Assert.Contains("Otherreach closed unexpectedly.", text);
        Assert.Contains("the tick fell over", text);
        Assert.Contains("Build: test", text);

        var notice = CrashLog.TakeNotice(_folder);
        Assert.Equal(new CrashNotice(file!, "unhandled exception (terminating)"), notice);
        Assert.Null(CrashLog.TakeNotice(_folder));   // said once
        Assert.True(File.Exists(file));              // the log itself stays
    }

    [Fact]
    public void NoMarker_NoNotice()
    {
        Assert.Null(CrashLog.TakeNotice(_folder));
    }

    [Fact]
    public void OnlyTheNewestCrashes_AreKept_AndTheMarkerNamesTheLast()
    {
        var log = new CrashLog(_folder);
        for (int i = 0; i < CrashLog.Kept + 3; i++)
            log.Write(new Exception($"crash {i}"), "unhandled exception");

        var kept = Directory.EnumerateFiles(_folder, "crash_*.txt").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(CrashLog.Kept, kept.Count(n => n != CrashLog.MarkerName));
        Assert.Equal("crash_0004.txt", kept.First(n => n != CrashLog.MarkerName));
        Assert.Equal(Path.Combine(_folder, $"crash_{CrashLog.Kept + 3:D4}.txt"), CrashLog.TakeNotice(_folder)!.LogFile);
    }

    [Fact]
    public void Installing_HooksOnce_AndDisposingUnhooks()
    {
        using var log = new CrashLog(_folder);
        log.Install();
        log.Install();
        log.Dispose();
        log.Dispose();
        Assert.False(Directory.Exists(_folder));   // nothing written without a crash
    }

    [Fact]
    public void AFolderThatCannotBeWritten_IsNoThrow()
    {
        string blocked = Path.Combine(_folder, "file");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(blocked, "a file where the folder should be");
        Assert.Null(new CrashLog(blocked).Write(new Exception("x"), "unhandled exception"));
    }
}
