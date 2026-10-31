using UNNAMED.Content;

namespace UNNAMED.Application.Tests;

/// <summary>
/// A pack that already passed validation boots without validating again, and any change to its files validates it afresh. An empty
/// unknown directory is the probe: validation refuses it (DIR003), but it adds no file, so it leaves the pack's stamp unchanged.
/// </summary>
public class ValidatedContentCacheTests
{
    [Fact]
    public void APackThatPassed_IsNotValidatedAgain_UntilAFileOfItChanges()
    {
        string pack = Path.Combine(Path.GetTempPath(), "unnamed-pack-" + Guid.NewGuid().ToString("N"));
        string cache = pack + "-checked";
        using var profile = new TempProfile();
        try
        {
            CopyTree(Path.Combine(Harness.RepoRoot(), "content"), pack);
            GameSession Boot(string? checkedCache) => GameSession.Boot(new GameOptions(pack, profile.Root) { ValidatedContentCache = checkedCache });

            Boot(cache);
            Assert.Single(Directory.GetFiles(cache, "*.ok"));

            Directory.CreateDirectory(Path.Combine(pack, "stray"));
            Assert.Throws<ContentBootException>(() => Boot(null));
            Boot(cache);   // the same files: the answer is remembered

            File.WriteAllText(Path.Combine(pack, "stray", "note.txt"), "not content");
            Assert.Throws<ContentBootException>(() => Boot(cache));
        }
        finally
        {
            foreach (string dir in new[] { pack, cache })
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
        }
    }

    private static void CopyTree(string from, string to)
    {
        foreach (string dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }
}
