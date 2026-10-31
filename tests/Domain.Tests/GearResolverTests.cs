using System.Collections.Immutable;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D21 G1: <see cref="GearResolver"/> is the one place an assembly becomes numbers. These tests hold it to canon's guard rails
/// (GEAR_GENERATION.md §9): a plain assembly changes nothing, a part's contribution is bounded, a maker's signature is capped, and no legal assembly
/// of a template escapes that template's envelope.
/// </summary>
public class GearResolverTests
{
    private static ItemDefinition Sword(AssemblySpec? assembly = null) =>
        new("item.weapon.test_sword", "weapon", 1, 1200, 40, "common", false, false, EquipSlot.MainHand,
            Requirements.None,
            new WeaponStats(8, 12, "slash", 1000, false, "skill.one_hand_blade", null) { AttackMs = 900 },
            0)
        {
            Assembly = assembly ?? new AssemblySpec("sword", ImmutableArray.Create("blade", "guard", "grip", "pommel"), "maker.test", 1),
            DurabilityMax = 100,
        };

    private static PartDefinition Part(string id, string slot, PartStats? stats = null) =>
        new(id, "sword", slot, "mesh." + id, ImmutableArray.Create("metal_main"), 1, stats ?? PartStats.None, ImmutableArray.Create(slot));

    private static readonly ImmutableSortedDictionary<string, PartDefinition> Parts = new[]
    {
        Part("part.sword.blade.plain", "blade"),
        Part("part.sword.guard.plain", "guard"),
        Part("part.sword.grip.plain", "grip"),
        Part("part.sword.pommel.plain", "pommel"),
        Part("part.sword.blade.heavy", "blade", new PartStats(1.12, 0, 220, 0.92, 15)),
    }.ToImmutableSortedDictionary(p => p.Id, p => p, StringComparer.Ordinal);

    private static readonly ImmutableSortedDictionary<string, MaterialDefinition> Materials = new[]
    {
        new MaterialDefinition("mat.steel.plain", "metal", "item.material.steel_ingot", MaterialMultipliers.None, 1),
        new MaterialDefinition("mat.steel.folded", "metal", "item.material.steel_ingot",
            new MaterialMultipliers(1.12, 1.10, 1.0, 1.25, 1.8), 1),
    }.ToImmutableSortedDictionary(m => m.Id, m => m, StringComparer.Ordinal);

    private static MakerDefinition Maker(string tendency, MakerStats bias) =>
        new("maker.test", "people.veth", "medieval", "compact_broad",
            ImmutableSortedDictionary<string, ImmutableSortedDictionary<string, ImmutableArray<WeightedPart>>>.Empty,
            ImmutableArray<WeightedMaterial>.Empty, new MakerSignature(tendency, bias), ImmutableArray.Create(1, 5),
            "name.maker.test", "maker.test.line");

    private static GearBuild Build(params (string Slot, string PartId)[] parts) => new(
        "sword", "maker.test", 1,
        parts.ToImmutableSortedDictionary(p => p.Slot, p => p.PartId, StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal),
        ImmutableArray<string>.Empty, 0);

    private static readonly GearBuild Plain = Build(
        ("blade", "part.sword.blade.plain"), ("guard", "part.sword.guard.plain"),
        ("grip", "part.sword.grip.plain"), ("pommel", "part.sword.pommel.plain"));

    [Fact]
    public void APlainAssembly_IsTheTemplateItself()
    {
        var item = Sword();
        var resolved = GearResolver.Resolve(item, Plain, Parts, Materials, null);

        Assert.Equal(item.Weapon!.DamageMin, resolved.DamageMin);
        Assert.Equal(item.Weapon.DamageMax, resolved.DamageMax);
        Assert.Equal(item.Weapon.ReachMm, resolved.ReachMm);
        Assert.Equal(item.WeightGrams, resolved.WeightGrams);
        Assert.Equal(item.Weapon.AttackMs, resolved.AttackMs);
        Assert.Equal(item.DurabilityMax, resolved.Durability);
        Assert.Equal(item.ValueBase, resolved.Value);
        Assert.False(resolved.Clamped);
    }

    [Fact]
    public void APartsContribution_BendsThePieceWithinCanonsBounds()
    {
        var item = Sword();
        var heavy = Build(
            ("blade", "part.sword.blade.heavy"), ("guard", "part.sword.guard.plain"),
            ("grip", "part.sword.grip.plain"), ("pommel", "part.sword.pommel.plain"));
        var resolved = GearResolver.Resolve(item, heavy, Parts, Materials, null);

        Assert.True(resolved.DamageMax > item.Weapon!.DamageMax, "a keen blade does more damage");
        Assert.Equal(item.WeightGrams + 220, resolved.WeightGrams);
        Assert.True(resolved.AttackMs > item.Weapon.AttackMs, "a heavier blade is slower");
        Assert.Equal(item.DurabilityMax + 15, resolved.Durability);
        Assert.False(resolved.Clamped);
    }

    [Fact]
    public void AMaterialMultiplies_BeforeTheEnvelopeSpeaks()
    {
        var item = Sword();
        var withSteel = Plain with { Materials = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal).Add("metal_main", "mat.steel.folded") };
        var resolved = GearResolver.Resolve(item, withSteel, Parts, Materials, null);

        Assert.Equal(13, resolved.DamageMax);   // 12 damage x 1.12 steel
        Assert.Equal(125, resolved.Durability);
        Assert.Equal((long)Math.Round(item.ValueBase * 1.8, MidpointRounding.AwayFromZero), resolved.Value);
    }

    [Fact]
    public void AMakersSignature_IsCappedAtTenPercent_AndOneTendency()
    {
        // A maker written with an absurd bias: the resolver holds it to canon's cap rather than trusting content.
        var maker = Maker("heavy_and_durable", new MakerStats(1.60, 1.60, 1.60, 1.60));
        var item = Sword();
        var resolved = GearResolver.Resolve(item, Plain, Parts, Materials, maker);

        Assert.Equal((long)Math.Round(item.WeightGrams * 1.10, MidpointRounding.AwayFromZero), resolved.WeightGrams);
        Assert.Equal(110, resolved.Durability);
        Assert.Equal(item.Weapon!.DamageMax + 1, resolved.DamageMax);   // 12 * 1.10 = 13.2 -> 13
    }

    [Fact]
    public void NoLegalAssembly_EscapesItsEnvelope()
    {
        // Canon §9.7: every legal assembly of a template stays inside the envelope. Walk the whole matrix, not one example.
        var item = Sword();
        var maker = Maker("brittle_keen", new MakerStats(0.9, 0.9, 0.9, 1.1));
        var heavy = Maker("heavy_and_durable", new MakerStats(1.1, 1.1, 0.9, 0.9));
        string[] blades = { "part.sword.blade.plain", "part.sword.blade.heavy" };
        string[] materials = { "mat.steel.plain", "mat.steel.folded" };

        foreach (string blade in blades)
        foreach (string material in materials)
        foreach (var used in new[] { null, maker, heavy })
        {
            var build = Plain with
            {
                Parts = Plain.Parts.SetItem("blade", blade),
                Materials = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal).Add("metal_main", material),
            };
            var resolved = GearResolver.Resolve(item, build, Parts, Materials, used);

            Assert.InRange(resolved.DamageMax, 0, (int)Math.Round(item.Weapon!.DamageMax * GearResolver.Envelope.Default.DamageMax));
            Assert.InRange(resolved.WeightGrams, 0, (long)Math.Round(item.WeightGrams * GearResolver.Envelope.Default.Weight));
            Assert.InRange(resolved.Durability, 0, (int)Math.Round(item.DurabilityMax * GearResolver.Envelope.Default.Durability));
            Assert.InRange(resolved.Value, 0, (long)Math.Round(item.ValueBase * GearResolver.Envelope.Default.Value));
        }
    }

    [Fact]
    public void AnAssemblyThatWouldEscape_IsClampedAndSaysSo()
    {
        var item = Sword();
        var greedy = new GearResolver.Envelope(1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0);   // no assembly may improve on its template at all
        var build = Plain with { Materials = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal).Add("metal_main", "mat.steel.folded") };
        var resolved = GearResolver.Resolve(item, build, Parts, Materials, null, greedy);

        Assert.True(resolved.Clamped);
        Assert.Contains("damage_max", resolved.ClampedStats);
    }
}
