using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D21 G4: a roll picks the maker's parts and material deterministically, and the piece it makes reads as a name and a tooltip a player can understand
/// (GEAR_GENERATION.md §3 steps 2-4, §8).
/// </summary>
public class GearNamingAndRollTests
{
    private static readonly ImmutableSortedDictionary<string, PartDefinition> Parts = new[]
    {
        new PartDefinition("part.sword.blade.armed", "sword", "blade", "mesh.a", ImmutableArray.Create("metal_main"), 1, PartStats.None,
            ImmutableArray.Create("blade")) { Noun = "arming sword" },
        new PartDefinition("part.sword.blade.leaf", "sword", "blade", "mesh.b", ImmutableArray.Create("metal_main"), 2, PartStats.None,
            ImmutableArray.Create("blade")) { Noun = "longsword" },
        new PartDefinition("part.sword.guard.cross", "sword", "guard", "mesh.c", ImmutableArray.Create("metal_guard"), 1, PartStats.None,
            ImmutableArray.Create("guard")),
    }.ToImmutableSortedDictionary(p => p.Id, p => p, StringComparer.Ordinal);

    private static readonly ImmutableSortedDictionary<string, MaterialDefinition> Materials = new[]
    {
        new MaterialDefinition("mat.iron.plain", "metal", "item.material.iron_ingot", MaterialMultipliers.None, 1),
        new MaterialDefinition("mat.iron.charcoal", "metal", "item.material.iron_ingot",
            new MaterialMultipliers(1.1, 1.05, 1.0, 1.15, 1.6), 1) { Word = "Charcoal Steel" },
    }.ToImmutableSortedDictionary(m => m.Id, m => m, StringComparer.Ordinal);

    private static MakerDefinition Maker() => new("maker.kal.vossgar_forge", "people.kal", "medieval", "compact_broad",
        new Dictionary<string, ImmutableSortedDictionary<string, ImmutableArray<WeightedPart>>>(StringComparer.Ordinal)
        {
            ["sword"] = new Dictionary<string, ImmutableArray<WeightedPart>>(StringComparer.Ordinal)
            {
                ["blade"] = ImmutableArray.Create(new WeightedPart("part.sword.blade.armed", 5), new WeightedPart("part.sword.blade.leaf", 2)),
                ["guard"] = ImmutableArray.Create(new WeightedPart("part.sword.guard.cross", 5)),
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        ImmutableArray.Create(new WeightedMaterial("mat.iron.plain", 3), new WeightedMaterial("mat.iron.charcoal", 5)),
        new MakerSignature("heavy_and_durable", MakerStats.None), ImmutableArray.Create(1, 5), "name.maker.kal_vossgar", "maker.kal.vossgar.line")
    {
        Words = ImmutableArray.Create("Vossgar", "Basalt-hafted"),
    };

    private static ItemDefinition Sword() => new("item.weapon.test_sword", "weapon", 1, 1200, 40, "common", false, false, EquipSlot.MainHand,
        Requirements.None, new WeaponStats(8, 12, "slash", 1000, false, "skill.one_hand_blade", null) { AttackMs = 900 }, 0)
    {
        Name = "Sword",
        Assembly = new AssemblySpec("sword", ImmutableArray.Create("blade", "guard"), "maker.kal.vossgar_forge", 1),
    };

    [Fact]
    public void ARollsPick_AreDeterministic_AndFollowTheMakersWeights()
    {
        var maker = Maker();
        double[] samples = { 0.1, 0.1, 0.1 };

        var first = GearRoll.Roll(Sword(), maker, 1, Parts, Materials, n => samples[n % samples.Length])!;
        var again = GearRoll.Roll(Sword(), maker, 1, Parts, Materials, n => samples[n % samples.Length])!;
        Assert.Equal(first.Parts, again.Parts);
        Assert.Equal(first.Materials, again.Materials);

        // A low sample lands in the maker's common blade; a high one in the rarer leaf (weight 2 of 7).
        Assert.Equal("part.sword.blade.armed", first.Parts["blade"]);
        var high = GearRoll.Roll(Sword(), maker, 2, Parts, Materials, n => n switch { 0 => 0.95, _ => 0.2 })!;
        Assert.Equal("part.sword.blade.leaf", high.Parts["blade"]);
        // The palette weighs plain iron 3 and charcoal steel 5 of 8, so a sample of 0.1 finishes it in plain iron.
        Assert.Equal("mat.iron.plain", first.Materials["metal_main"]);
    }

    [Fact]
    public void ARolledTier_KeepsOutPartsAboveIt()
    {
        var maker = Maker();
        // The leaf blade is a tier-2 part: a tier-1 roll may not have it, however the sample falls.
        var lowTier = GearRoll.Roll(Sword(), maker, 1, Parts, Materials, n => n == 0 ? 0.99 : 0.1)!;
        Assert.Equal("part.sword.blade.armed", lowTier.Parts["blade"]);
        Assert.Equal(1, lowTier.PartTier);
    }

    [Fact]
    public void AName_ReadsAsCanonSectionEightSays_MakerThenMaterialThenThePartsNoun()
    {
        var build = new GearBuild("sword", "maker.kal.vossgar_forge", 1,
            ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal)
                .Add("blade", "part.sword.blade.armed").Add("guard", "part.sword.guard.cross"),
            ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal).Add("metal_main", "mat.iron.charcoal"),
            ImmutableArray<string>.Empty, 0);

        string name = GearNaming.NameOf(Sword(), build, Parts, Materials, Maker());

        Assert.Equal("Vossgar Charcoal Steel arming sword", name);
        Assert.DoesNotContain("+", name);
        Assert.DoesNotContain(":", name);
        Assert.True(name.Split(' ').Length <= 5, $"'{name}' is longer than canon's four words plus an article");
    }

    [Fact]
    public void ABuildWithNoWordsForIt_StillHasAName_AndTheTooltipSaysWhatItIsMadeOf()
    {
        var build = new GearBuild("sword", "maker.unknown", 1,
            ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal).Add("blade", "part.sword.blade.armed"),
            ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal).Add("metal_main", "mat.iron.plain"),
            ImmutableArray<string>.Empty, 0);

        string name = GearNaming.NameOf(Sword(), build, Parts, Materials, null);
        Assert.Equal("arming sword", name);

        var lines = GearNaming.TooltipLines(build, Parts, Materials, Maker());
        Assert.Contains(lines, l => l.Contains("Assembled from", StringComparison.Ordinal) && l.Contains("arming sword", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Vossgar", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("heavy and durable", StringComparison.Ordinal));
    }

    [Fact]
    public void TheQualityWord_AppearsOnlyForFineAndAbove()
    {
        Assert.Null(GearNaming.QualityWord(Quality.Standard));
        Assert.Equal("Fine-wrought", GearNaming.QualityWord(Quality.Fine));
        Assert.Equal("Masterwork", GearNaming.QualityWord(Quality.Masterwork));
    }
}
