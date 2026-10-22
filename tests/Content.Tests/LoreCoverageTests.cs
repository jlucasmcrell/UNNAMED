using System.IO;
using System.Linq;
using UNNAMED.Content;
using YamlDotNet.Serialization;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Spec 41 pass 2 Â§6: every lore reference resolves, every placed book or inscription names an entry that exists, and the coverage report
/// says what canon holds against what the player can read. The index is checked hard: a status of covered must name a real entry.
/// </summary>
public class LoreCoverageTests
{
    private static ContentLoader Load()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        return loader;
    }

    [Fact]
    public void EveryLoreReference_Resolves()
    {
        // LoreContent.Build(check: true) refuses a page whose source names nothing the pack has, so building it is the check.
        var entries = LoreContent.Build(Load(), check: true);
        Assert.NotEmpty(entries);
        foreach (var entry in entries.Values)
        {
            Assert.NotEmpty(entry.Pages);
            Assert.Equal(Enumerable.Range(1, entry.PageCount), entry.Pages.Select(p => p.Page));
            foreach (var page in entry.Pages)
                Assert.NotEmpty(page.Sources);
        }
    }

    [Fact]
    public void EveryPlacedReadable_NamesAnEntryThatExists()
    {
        var loader = Load();
        var entries = LoreContent.Build(loader);
        int readables = 0;
        foreach (var region in loader.GetByKind("region").Values)
        {
            var map = new DeserializerBuilder().Build().Deserialize<System.Collections.Generic.Dictionary<object, object>>(region.YamlSource) ?? new();
            if (map.GetValueOrDefault("readables") is not System.Collections.Generic.List<object> rows)
                continue;
            foreach (var row in rows.OfType<System.Collections.Generic.Dictionary<object, object>>())
            {
                readables++;
                string key = row.GetValueOrDefault("key") as string ?? "";
                string entry = row.GetValueOrDefault("lore_ref") as string ?? "";
                Assert.True(entries.ContainsKey(entry), $"{key} in {region.Id} names '{entry}', which is not a lore entry");
                int page = int.TryParse(row.GetValueOrDefault("page") as string, out int number) ? number : 0;
                if (page > 0)
                    Assert.NotNull(entries[entry].PageAt(page));
                // A page opened with E is a page its entry declares as inspected at that key, so the journal and the world agree.
                if (page > 0)
                    Assert.Contains(entries[entry].Pages, p => p.Sources.Any(s => s.Kind == UNNAMED.Domain.Knowledge.LoreSourceKind.Inspected && s.Ref == key));
            }
        }
        Assert.True(readables > 0, "no region places a readable");
    }

    [Fact]
    public void TheIndex_AgreesWithTheEntries()
    {
        var loader = Load();
        var coverage = LoreCoverageReport.Read(Path.Combine(RepoPaths.Root(), "content"), LoreContent.Build(loader));
        Assert.True(coverage.Problems.IsEmpty, string.Join("; ", coverage.Problems));
        Assert.True(coverage.Total > 0, "the canon index lists no concepts");
        Assert.Equal(coverage.Total, coverage.Covered + coverage.Partly + coverage.Missing);
    }
}
