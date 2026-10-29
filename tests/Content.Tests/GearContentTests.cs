using System.Collections.Immutable;
using UNNAMED.Domain.Items;

namespace UNNAMED.Content.Tests;

/// <summary>
/// D21 G0: the data model loads from the new kinds, refuses what canon forbids, and a plain item is unchanged for having the option of an assembly.
/// </summary>
public class GearContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A minimal content root written to a temporary directory, removed afterwards.</summary>
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-gear-" + Guid.NewGuid().ToString("N"));

        public Fixture Write(string relative, string yaml)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, yaml.Replace("\r\n", "\n"));
            return this;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private const string Blade = """
        id: part.sword.blade.leaf_long
        kind: part
        schema: 1
        display_key: part.sword.blade.leaf_long.name
        tags: [blade]
        family: sword
        slot: blade
        mesh_ref: mesh.part.sword.blade.leaf_long
        material_slots: [metal_main]
        tier_min: 1
        stat_contrib: { damage_mult: 1.00, reach_add: 0.05, weight_add: 0.1, speed_mult: 0.99, durability_add: 0 }
        compat: { requires_tags_on: { guard: [cross] } }
        """;

    private const string IngotMaterial = """
        id: mat.steel.kal_folded
        kind: material
        schema: 1
        display_key: mat.steel.kal_folded.name
        tags: [material]
        class: metal
        material_ref_item: item.material.steel_ingot
        mult: { damage: 1.12, weight: 1.00, durability: 1.25, value: 1.8 }
        tier_min: 1
        """;

    private const string Ingot = """
        id: item.material.steel_ingot
        kind: item
        schema: 1
        display_key: item.material.steel_ingot.name
        tags: [item]
        category: material
        stack_max: 99
        weight: 0.3
        value_base: 4
        """;

    private static string Maker(string tendency) => """
        id: maker.kal.vossgar_forge
        kind: maker
        schema: 1
        display_key: maker.kal.vossgar_forge.name
        tags: [maker]
        people_ref: people.kal
        era: medieval
        fit_family: compact_broad
        part_sets:
          sword: { blade: [{ part_ref: part.sword.blade.leaf_long, weight: 5 }] }
        material_palette: [{ material_ref: mat.steel.kal_folded, weight: 5 }]
        signature: { tendency: TENDENCY, stat_bias: { weight_mult: 1.08, durability_mult: 1.10 } }
        tier_range: [1, 5]
        name_grammar: name.maker.kal_vossgar
        tooltip_line_key: maker.kal.vossgar.line
        """.Replace("TENDENCY", tendency);

    private static string People() => """
        id: people.kal
        kind: people
        schema: 1
        display_key: people.kal.name
        tags: [people]
        name: Kal
        """;

    [Fact]
    public void APart_AMaker_AndAMaterial_Load_FromTheNewKinds()
    {
        using var fixture = new Fixture()
            .Write("people/kal.yaml", People())
            .Write("items/steel_ingot.yaml", Ingot)
            .Write("parts/sword_blade.yaml", Blade)
            .Write("materials/kal_folded.yaml", IngotMaterial)
            .Write("makers/vossgar.yaml", Maker("heavy_and_durable"));
        var loader = Load(fixture.Root);

        var part = GearContent.BuildParts(loader)["part.sword.blade.leaf_long"];
        Assert.Equal("sword", part.Family);
        Assert.Equal("blade", part.Slot);
        Assert.Equal(50, part.Stats.ReachAddMm);
        Assert.Equal(100, part.Stats.WeightAddGrams);
        Assert.Equal(new[] { "cross" }, part.RequiresTagsOn["guard"]);

        var material = GearContent.BuildMaterials(loader)["mat.steel.kal_folded"];
        Assert.Equal("metal", material.Class);
        Assert.Equal("item.material.steel_ingot", material.RefItemId);
        Assert.Equal(1.25, material.Mult.Durability);

        var maker = GearContent.BuildMakers(loader)["maker.kal.vossgar_forge"];
        Assert.Equal("heavy_and_durable", maker.Signature.Tendency);
        Assert.Equal(5, maker.PartSets["sword"]["blade"].Single().Weight);
        Assert.Equal(1.08, maker.Signature.StatBias.WeightMult);
    }

    [Fact]
    public void APartInASlotItsFamilyDoesNotHave_IsRefused()
    {
        using var fixture = new Fixture().Write("parts/wrong.yaml", Blade.Replace("slot: blade", "slot: haft"));
        var loader = Load(fixture.Root);

        var refusal = Assert.Throws<FormatException>(() => GearContent.BuildParts(loader));
        Assert.Contains("not a sword slot", refusal.Message);
    }

    [Fact]
    public void AMakerWithATendencyCanonDoesNotAllow_IsRefused()
    {
        using var fixture = new Fixture()
            .Write("people/kal.yaml", People())
            .Write("items/steel_ingot.yaml", Ingot)
            .Write("parts/sword_blade.yaml", Blade)
            .Write("materials/kal_folded.yaml", IngotMaterial)
            .Write("makers/vossgar.yaml", Maker("invisible_and_cursed"));
        var loader = Load(fixture.Root);

        var refusal = Assert.Throws<FormatException>(() => GearContent.BuildMakers(loader));
        Assert.Contains("tendency 'invisible_and_cursed'", refusal.Message);
    }

    [Fact]
    public void AnItemWithNoAssemblyBlock_HasNone_AndOneWithItHasItsFamilyAndSlots()
    {
        var plain = new ItemDefinition("item.test.plain", "weapon", 1, 1000, 10, "common", false, false, null,
            Requirements.None, null, 0);
        Assert.Null(plain.Assembly);

        var built = plain with { Assembly = new AssemblySpec("sword", GearContent.FamilySlots["sword"], "maker.kal.vossgar_forge", 2) };
        Assert.True(built.Assembly!.HasSlot("pommel"));
        Assert.False(built.Assembly.HasSlot("haft"));
    }
}
