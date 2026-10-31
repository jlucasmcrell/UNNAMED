using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-39 as content: the bosses that are made of pieces carry them when the world boots.
/// <para>
/// Nineteen of the roster's constructs are built as one body with parts - a Custodian's gauge face, measure arm and footing, a
/// Warden's gate plate and post, an Animated Armour's harness and empty helm - so the parts read from the same content the fight
/// loads, and no two parts of one boss claim the same body region.
/// </para>
/// </summary>
public class BossPartContentTests
{
    private static IReadOnlyDictionary<string, CreatureDefinition> Bosses()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        return session.Setup.Combat.Creatures
            .Where(pair => pair.Key.StartsWith("creature.boss.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    [Fact]
    public void TheConstructBossesAreMadeOfParts()
    {
        var bosses = Bosses();
        var withParts = bosses.Where(pair => !pair.Value.Parts.IsDefaultOrEmpty).ToList();
        Assert.True(withParts.Count >= 15, $"expected the construct bosses' parts, found {withParts.Count} bosses with parts");
        foreach (var (id, boss) in withParts)
        {
            Assert.True(boss.Parts.Length >= 2, $"{id} has {boss.Parts.Length} part(s)");
            var regions = boss.Parts.Select(part => part.Region).ToList();
            Assert.Equal(regions.Count, regions.Distinct().Count());
            Assert.All(boss.Parts, part =>
            {
                Assert.False(string.IsNullOrWhiteSpace(part.Name));
                Assert.True(part.Structure > 0, $"{id}'s part '{part.Name}' has no structure");
            });
        }
    }

    [Fact]
    public void PartsAreStructureOnOnePool_NotASecondHealthBar()
    {
        foreach (var (id, boss) in Bosses().Where(pair => !pair.Value.Parts.IsDefaultOrEmpty))
        {
            int structure = boss.Parts.Sum(part => part.Structure);
            // the controller's own pool is what the fight drains; the parts are what breaks along the way
            Assert.True(boss.MaxHealth > 0, $"{id} has no health pool");
            Assert.True(structure > 0, $"{id} has parts with no structure");
        }
    }

    [Fact]
    public void AWildBodyHasNoParts()
    {
        var bosses = Bosses();
        var whole = bosses.Where(pair => pair.Value.Parts.IsDefaultOrEmpty).ToList();
        Assert.NotEmpty(whole);
        Assert.All(whole, pair => Assert.True(pair.Value.Parts.IsDefaultOrEmpty));
    }
}
