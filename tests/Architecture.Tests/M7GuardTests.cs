using System.Reflection;
using System.Text.RegularExpressions;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Architecture.Tests;

/// <summary>
/// M7's guards over navigation (M7 design §6, G3-G25, N-X1): the derived grid is never saved, never reached for its bytes and never
/// read for its work counts; no Godot navigation outside the spike; no system subscribes to events; the authority reads no clock.
/// </summary>
public class M7GuardTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }

    /// <summary>Every C# source under a directory of the repository, generated and build output left out.</summary>
    private static List<(string Path, string[] Lines)> Sources(string relative)
    {
        string root = Path.Combine(Root(), relative);
        char sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}.godot{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetRelativePath(Root(), f).Replace('\\', '/'), File.ReadAllLines(f)))
            .ToList();
    }

    private static IEnumerable<string> Hits(IEnumerable<(string Path, string[] Lines)> files, Regex pattern) =>
        from file in files
        from line in file.Lines.Select((text, i) => (text, i))
        where pattern.IsMatch(line.text)
        select $"{file.Path}:{line.i + 1}: {line.text.Trim()}";

    private static List<(string Path, string[] Lines)> NavDomainFiles() =>
        Sources("src/Domain/Spatial").Where(f => Path.GetFileName(f.Path).StartsWith("Nav", StringComparison.Ordinal)).ToList();

    // N-X1 (G16)
    [Fact]
    public void NavDomain_IsIntegerOnly()
    {
        var files = NavDomainFiles();
        Assert.True(files.Count >= 9, $"only {files.Count} Nav*.cs files found");
        var hits = Hits(files, new Regex(@"\bdouble\b|\bfloat\b|Math\.(Sqrt|Sin|Cos|Atan|Pow|Round)\b")).ToList();
        Assert.True(hits.Count == 0, "Floating point in navigation:\n" + string.Join("\n", hits));
    }

    // G3
    [Fact]
    public void PersistenceNeverReferencesTheNavigationGrid()
    {
        var hits = Hits(Sources("src/Persistence"), new Regex(@"\b(NavGrid|NavTile|NavScratch|NavCounters)\b")).ToList();
        Assert.True(hits.Count == 0, "Persistence names the navigation grid:\n" + string.Join("\n", hits));
    }

    // G4
    [Fact]
    public void PresentationUsesNoGodotNavigation_OutsideTheSpike()
    {
        var files = Sources("src/Presentation").Where(f => !f.Path.StartsWith("src/Presentation/Spike/", StringComparison.Ordinal));
        var hits = Hits(files, new Regex(@"\b(NavigationServer3D|NavigationAgent3D|NavigationRegion3D|NavigationMesh)\b")).ToList();
        Assert.True(hits.Count == 0, "Godot navigation outside the spike:\n" + string.Join("\n", hits));
    }

    // G5
    [Fact]
    public void SystemsNeverSubscribe()
    {
        var hits = Hits(Sources("src/World").Concat(Sources("src/Domain")), new Regex(@"\.Subscribe\s*[<(]")).ToList();
        Assert.True(hits.Count == 0, "A system subscribes to events:\n" + string.Join("\n", hits));
    }

    // G8
    [Fact]
    public void M7SystemsMintNoWallClockIds()
    {
        string[] systems = { "Building.cs", "BuildingRules.cs", "Navigation.cs", "Factions.cs", "Errands.cs" };
        var files = Sources("src/World/Runtime").Where(f => systems.Contains(Path.GetFileName(f.Path))).ToList();
        Assert.Contains(files, f => f.Path.EndsWith("/Navigation.cs", StringComparison.Ordinal));
        var hits = Hits(files, new Regex(@"NewId|Registry\.")).ToList();
        Assert.True(hits.Count == 0, "An M7 system mints or registers an ID itself:\n" + string.Join("\n", hits));
    }

    // G15
    [Fact]
    public void CountersAreNeverRead()
    {
        var authority = Sources("src/Domain").Concat(Sources("src/World")).ToList();
        bool IsSink(string path) => path.EndsWith("/NavCounters.cs", StringComparison.Ordinal);
        bool IsView(string path) => path == "src/World/Runtime/Navigation.cs";

        // The snapshot type is named only where it is defined and where the view carries it.
        var typeHits = Hits(authority.Where(f => !IsSink(f.Path) && !IsView(f.Path)), new Regex(@"\bNavCounters\b")).ToList();
        Assert.True(typeHits.Count == 0, "NavCounters is named outside its definition and the view:\n" + string.Join("\n", typeHits));
        // A snapshot is taken only to build the view.
        var snapshots = Hits(authority.Where(f => !IsSink(f.Path)), new Regex(@"\.Snapshot\(\)")).ToList();
        Assert.All(snapshots, hit => Assert.True(hit.StartsWith("src/World/Runtime/Navigation.cs:", StringComparison.Ordinal) && hit.Contains("View(", StringComparison.Ordinal), hit));
        Assert.Single(snapshots);
        // No count is read anywhere in the authority.
        var reads = Hits(authority.Where(f => !IsSink(f.Path)),
            new Regex(@"\.(FullBuilds|RectRebuilds|TilesRestamped|NodesRestamped|PlansByOutcome|MaxExpansionsOneQuery|EditChecks|EditRefusalsByRule|FloodNodes)\b")).ToList();
        Assert.True(reads.Count == 0, "A navigation count is read:\n" + string.Join("\n", reads));
        // The sink offers counting and the snapshot, and nothing a decision could read.
        var members = typeof(NavCounterSink).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.Name).ToList();
        Assert.All(members, name => Assert.True(name.StartsWith("Count", StringComparison.Ordinal) || name == nameof(NavCounterSink.Snapshot), name));
        Assert.Empty(typeof(NavCounterSink).GetProperties(BindingFlags.Public | BindingFlags.Instance));
    }

    // G24
    [Fact]
    public void M7Code_NamesItsComparers()
    {
        var files = NavDomainFiles().Concat(Sources("src/World/Runtime").Where(f => f.Path.EndsWith("/Navigation.cs", StringComparison.Ordinal)))
            .Concat(Sources("src/Content").Where(f => f.Path.EndsWith("/NavigationContent.cs", StringComparison.Ordinal))).ToList();
        var construction = new Regex(@"ToImmutableSortedDictionary\(|ImmutableSortedDictionary\.Create|ToImmutableSortedSet\(|ImmutableSortedSet\.Create|new SortedDictionary<string|new SortedSet<string|SortedDictionary<string[^>]*>\s+\w+\s*=\s*new\(|SortedSet<string>\s+\w+\s*=\s*new\(");
        var offenders = new List<string>();
        foreach (var (path, lines) in files)
        {
            string text = string.Join("\n", lines);
            foreach (Match match in construction.Matches(text))
            {
                int end = text.IndexOf(';', match.Index);
                string statement = text[match.Index..(end < 0 ? text.Length : end)];
                if (!statement.Contains("StringComparer.Ordinal", StringComparison.Ordinal))
                    offenders.Add($"{path}: {statement.Split('\n')[0].Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "A sorted collection without an ordinal comparer:\n" + string.Join("\n", offenders));
    }

    // G25
    [Fact]
    public void NavigationGridBytes_AreNeverUnwrapped()
    {
        var pattern = new Regex(@"ImmutableCollectionsMarshal");
        var presentation = Hits(Sources("src/Presentation"), pattern).ToList();
        Assert.True(presentation.Count == 0, "Presentation unwraps immutable arrays:\n" + string.Join("\n", presentation));
        var authority = Hits(Sources("src/Domain").Concat(Sources("src/World"))
            .Where(f => f.Path is not ("src/Domain/Spatial/NavTile.cs" or "src/Domain/Spatial/NavGrid.cs")), pattern).ToList();
        Assert.True(authority.Count == 0, "ImmutableCollectionsMarshal outside NavTile.cs and NavGrid.cs:\n" + string.Join("\n", authority));
    }

    // STOP S3
    [Fact]
    public void AuthorityNeverReadsAClock()
    {
        var clock = new Regex(@"\bStopwatch\b|DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.Now|DateTimeOffset\.UtcNow|Environment\.TickCount");
        var world = Hits(Sources("src/World"), clock).ToList();
        Assert.True(world.Count == 0, "The world reads a clock:\n" + string.Join("\n", world));

        // Domain: only EntityId.NewId, whose ULID carries the time by definition (D-04).
        var domain = Hits(Sources("src/Domain"), clock).ToList();
        Assert.All(domain, hit => Assert.StartsWith("src/Domain/EntityId.cs:", hit));
        Assert.Single(domain);

        // Application: only the manifest label a save is captured with, which nothing in the authority reads back.
        var application = Hits(Sources("src/Application"), clock).ToList();
        var captured = Assert.Single(application);
        Assert.StartsWith("src/Application/GameSession.cs:", captured);
        Assert.Contains("CapturedAt", captured);
    }
}
