// UNNAMED Content tests - the region's fauna: herds where they should be, a body to skin for every animal, and no spawn in a region the
// session does not simulate (the region-fauna pass).

using System.Collections.Immutable;
using System.Linq;
using UNNAMED.Content;
using Xunit;

namespace UNNAMED.Content.Tests;

public sealed class FaunaContentTests
{
    private static readonly string[] Fauna =
    [
        "creature.beast.doe", "creature.beast.buck", "creature.beast.elk", "creature.beast.moose",
        "creature.beast.rabbit", "creature.beast.red_fox", "creature.beast.duck", "creature.beast.crow",
        "creature.fish.trout",
    ];

    // The four regions the session actually simulates. A spawn in `Declared`-only ground is populated for the day that area is booted, and
    // is listed here so the exception stays explicit rather than turning this test into a formality.
    private static readonly string[] Simulated =
    [
        "region.starting_frontier", "region.barrowworks", "region.den_gorge_cave", "region.sedge_levels",
    ];

    private static readonly string[] DeclaredOnly = ["region.ashen_hollow"];

    private static ContentLoader Load()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        return loader;
    }

    [Fact]
    public void EveryAnimal_CanBeHuntedAndSkinned()
    {
        var loader = Load();
        foreach (string id in Fauna)
        {
            var creature = loader.GetByKind("creature")[id];
            Assert.NotNull(creature);
            string yaml = creature.YamlSource!;
            Assert.Contains("loot_table:", yaml);
            Assert.Contains("carcass_ref:", yaml);
        }
    }

    [Fact]
    public void EverySpawn_SitsInARegionTheSessionSimulates()
    {
        var loader = Load();
        foreach (var spawn in loader.GetByKind("spawn").Values)
        {
            Assert.Contains(Simulated.Concat(DeclaredOnly), region => spawn.YamlSource!.Contains($"region_ref: {region}"));
        }
    }

    [Fact]
    public void TheHerdingAnimals_StandInBands()
    {
        var loader = Load();
        var bands = loader.GetByKind("spawn").Values
            .Where(spawn => spawn.YamlSource!.Contains("creature.beast.elk") || spawn.YamlSource!.Contains("creature.beast.moose")
                || spawn.YamlSource!.Contains("creature.beast.doe"))
            .ToList();
        Assert.NotEmpty(bands);

        // A band is more than a pair: the herding spawns ask for at least three animals between their rows, whatever the split.
        foreach (var spawn in bands)
        {
            var counts = spawn.YamlSource!.Split("count: [").Skip(1)
                .Select(part => int.Parse(part.Split(',')[0]))
                .ToList();
            Assert.NotEmpty(counts);
            Assert.True(counts.Sum() >= 3, $"{spawn.Id} asks for only {counts.Sum()} animal(s)");
        }
    }

    [Fact]
    public void EverySimulatedArea_HasWildlife()
    {
        var loader = Load();
        foreach (string region in Simulated)
        {
            Assert.Contains(loader.GetByKind("spawn").Values,
                spawn => spawn.YamlSource!.Contains($"region_ref: {region}") && Fauna.Any(id => spawn.YamlSource!.Contains(id)));
        }
    }
}
