// UNNAMED Presentation - Report a problem: the tester's words, the log, the settings, the save and a picture, in one local zip (public playtest gate E2)
// Godot-free: plain .NET file I/O, so the tests compile it as the game does. Local only: the zip is written to a folder and nothing is sent.

using System.IO.Compression;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>What one report carries: the tester's note, the build and the machine in words, the files to include and the screen as a PNG.</summary>
public sealed record ProblemReportParts(string Note, string Build, string Machine, IReadOnlyList<string> Files, byte[]? Screenshot);

/// <summary>
/// Writes <c>report_NNNN.zip</c> into the reports folder: <c>report.txt</c> (the note, the build, the machine), <c>screenshot.png</c>, and
/// every listed file that exists under its own name - the log Godot still has open is read shared. Numbered like the crash files, so the
/// wall clock stays the session's. The tester sends the zip themselves; the game never does.
/// </summary>
public static class ProblemReport
{
    private const string Prefix = "report_";

    /// <summary>The most reports kept: the oldest go first.</summary>
    public const int Kept = 20;

    public static string Write(string directory, ProblemReportParts parts)
    {
        Directory.CreateDirectory(directory);
        var numbers = Directory.EnumerateFiles(directory, Prefix + "*.zip")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(n => int.TryParse(n![Prefix.Length..], out int i) ? i : -1)
            .Where(i => i > 0)
            .ToList();
        int next = numbers.Count == 0 ? 1 : numbers.Max() + 1;
        string zip = Path.Combine(directory, $"{Prefix}{next:D4}.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var text = new StreamWriter(archive.CreateEntry("report.txt").Open()))
            {
                text.WriteLine("What happened, in the tester's words:");
                text.WriteLine(parts.Note.Trim().Length > 0 ? parts.Note.Trim() : "(nothing written)");
                text.WriteLine();
                text.WriteLine($"Build: {parts.Build}");
                text.WriteLine(parts.Machine);
            }
            if (parts.Screenshot is { Length: > 0 } png)
            {
                using var shot = archive.CreateEntry("screenshot.png", CompressionLevel.NoCompression).Open();
                shot.Write(png);
            }
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "report.txt", "screenshot.png" };
            foreach (string file in parts.Files.Where(File.Exists))
            {
                string name = Path.GetFileName(file);
                for (int n = 2; !named.Add(name); n++)
                    name = $"{Path.GetFileNameWithoutExtension(file)}_{n}{Path.GetExtension(file)}";
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var entry = archive.CreateEntry(name).Open();
                source.CopyTo(entry);
            }
        }
        foreach (int old in numbers.OrderBy(n => n).Take(Math.Max(0, numbers.Count + 1 - Kept)))
            File.Delete(Path.Combine(directory, $"{Prefix}{old:D4}.zip"));
        return zip;
    }
}
