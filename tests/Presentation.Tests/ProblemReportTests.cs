using System.IO.Compression;
using UNNAMED.Presentation.Diagnostics;

namespace UNNAMED.Presentation.Tests;

/// <summary>Report a problem (public playtest gate E2): one numbered local zip with the note, the build, the machine, the picture and the files.</summary>
public sealed class ProblemReportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "unnamed-report-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void AReport_CarriesTheNote_TheBuild_ThePicture_AndTheFilesThatExist()
    {
        Directory.CreateDirectory(_folder);
        string log = Path.Combine(_folder, "godot.log");
        File.WriteAllText(log, "the log");
        string other = Path.Combine(_folder, "sub", "godot.log");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "a second file of the same name");
        // Godot keeps its log open for writing while the game runs: the report reads it shared.
        using var open = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        string zip = ProblemReport.Write(Path.Combine(_folder, "reports"),
            new ProblemReportParts("The reply list went blank at Kera's.", "d9 · 6131fc64", "Graphics: test", new[] { log, other, Path.Combine(_folder, "missing.json") }, new byte[] { 1, 2, 3 }));

        Assert.Equal(Path.Combine(_folder, "reports", "report_0001.zip"), zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "godot.log", "godot_2.log", "report.txt", "screenshot.png" }, archive.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
        string text = new StreamReader(archive.GetEntry("report.txt")!.Open()).ReadToEnd();
        Assert.Contains("The reply list went blank at Kera's.", text);
        Assert.Contains("Build: d9 · 6131fc64", text);
        Assert.Contains("Graphics: test", text);
        Assert.Equal("the log", new StreamReader(archive.GetEntry("godot.log")!.Open()).ReadToEnd());
    }

    [Fact]
    public void OnlyTheNewestReports_AreKept()
    {
        for (int i = 0; i < ProblemReport.Kept + 2; i++)
            ProblemReport.Write(_folder, new ProblemReportParts("", "test", "", Array.Empty<string>(), null));

        var kept = Directory.EnumerateFiles(_folder, "report_*.zip").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(ProblemReport.Kept, kept.Count);
        Assert.Equal("report_0003.zip", kept[0]);
    }
}
