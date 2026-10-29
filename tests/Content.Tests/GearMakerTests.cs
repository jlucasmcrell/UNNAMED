using System.Collections.Immutable;
using UNNAMED.Domain.Items;

namespace UNNAMED.Content.Tests;

/// <summary>
/// D21 G2: the first makers and parts, loaded from the game's own content and held to what a generator needs - every slot of every family a maker
/// offers must be buildable, every assembled item must name a family and a maker that can build it, and a real assembly must resolve inside its
/// envelope (GEAR_GENERATION.md §2.2-§2.3, §3, §9).
/// </summary>
public class GearMakerTests
{
    private static ContentLoader Game() => Load(Path.Combine(RepoPaths.Root(), "content"));

    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    [Fact]
    public void TheGamesOwnMakers_AreWholeGarages_EverySlotOfEveryFamilyTheyOfferIsBuildable()
    {
        var loader = Game();
        var makers = GearContent.BuildMakers(loader);
        Assert.NotEmpty(makers);

        foreach (var maker in makers.Values)
        foreach (var (family, slots) in maker.PartSets)
        {
            Assert.True(GearContent.FamilySlots.ContainsKey(family), $"{maker.Id} offers family '{family}', which canon gives no slots");
            foreach (string slot in GearContent.FamilySlots[family])
                Assert.True(slots.ContainsKey(slot) && slots[slot].Length > 0,
                    $"{maker.Id} offers {family} but no {slot}: an assembly of that family could never be built");
        }
    }

    [Fact]
    public void EveryMaker_NamesPartsItCanActuallyUse_AndMaterialsThatExist()
    {
        var loader = Game();
        var parts = GearContent.BuildParts(loader);
        var materials = GearContent.BuildMaterials(loader);
        Assert.NotEmpty(parts);
        Assert.NotEmpty(materials);

        foreach (var maker in GearContent.BuildMakers(loader).Values)
        {
            foreach (var (family, slots) in maker.PartSets)
            foreach (var (slot, offered) in slots)
            foreach (var weighted in offered)
            {
                var part = parts[weighted.PartId];
                Assert.Equal(family, part.Family);
                Assert.Equal(slot, part.Slot);
                Assert.True(weighted.Weight > 0, $"{maker.Id} weights {weighted.PartId} at nothing");
            }
            foreach (var weighted in maker.MaterialPalette)
            {
                Assert.True(materials.ContainsKey(weighted.MaterialId), $"{maker.Id} names material {weighted.MaterialId}");
                Assert.True(weighted.Weight > 0);
            }
        }
    }

    [Fact]
    public void AnAssembledItem_NamesAFamilyAndAMakerThatBuildsIt()
    {
        var loader = Game();
        var makers = GearContent.BuildMakers(loader);
        var catalog = ItemContent.BuildCatalog(loader);
        var assembled = loader.GetByKind("item").Keys
            .Concat(loader.GetByKind("item.weapon").Keys)
            .Concat(loader.GetByKind("item.armor").Keys)
            .Select(catalog.Get)
            .Where(item => item.Assembly is not null).ToArray();
        Assert.NotEmpty(assembled);

        foreach (var item in assembled)
        {
            var assembly = item.Assembly!;
            Assert.True(makers.ContainsKey(assembly.MakerId!), $"{item.Id} names maker {assembly.MakerId}, which does not exist");
            var maker = makers[assembly.MakerId!];
            Assert.True(maker.PartSets.ContainsKey(assembly.Family),
                $"{item.Id} is a {assembly.Family} and {maker.Id} does not build {assembly.Family}");
            foreach (string slot in assembly.Slots)
                Assert.True(GearContent.FamilySlots[assembly.Family].Contains(slot, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void ARealAssemblyFromContent_ResolvesInsideItsEnvelope()
    {
        var loader = Game();
        var parts = GearContent.BuildParts(loader);
        var materials = GearContent.BuildMaterials(loader);
        var makers = GearContent.BuildMakers(loader);
        var catalog = ItemContent.BuildCatalog(loader);

        foreach (var item in loader.GetByKind("item").Keys
                     .Concat(loader.GetByKind("item.weapon").Keys)
                     .Concat(loader.GetByKind("item.armor").Keys)
                     .Select(catalog.Get)
                     .Where(i => i.Assembly is not null))
        {
            var maker = makers[item.Assembly!.MakerId!];
            var chosen = maker.PartSets[item.Assembly.Family]
                .ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value[0].PartId, StringComparer.Ordinal);
            var material = maker.MaterialPalette[0].MaterialId;
            var build = new GearBuild(item.Assembly.Family, maker.Id, item.Assembly.PartTier, chosen,
                ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal).Add("metal_main", material),
                ImmutableArray<string>.Empty, 0);

            var resolved = GearResolver.Resolve(item, build, parts, materials, maker);

            Assert.False(resolved.Clamped, $"{item.Id} built by {maker.Id} needed clamping: {string.Join(", ", resolved.ClampedStats)}");
            Assert.True(resolved.DamageMax > 0, $"{item.Id} resolved to no damage");
            Assert.True(resolved.AttackMs > 0 || resolved.DrawMs > 0, $"{item.Id} resolved to no swing and no draw");
            Assert.True(resolved.Durability > 0);
            Assert.True(resolved.Value > 0);
        }
    }

    [Fact]
    public void TheSevenMakers_ComeFromSevenOfCanonsPeoples_AndEachHasOneTendency()
    {
        var loader = Game();
        var makers = GearContent.BuildMakers(loader).Values.ToArray();

        Assert.True(makers.Length >= 7, $"only {makers.Length} makers are authored");
        Assert.Equal(makers.Length, makers.Select(m => m.PeopleId).Distinct(StringComparer.Ordinal).Count());
        foreach (var maker in makers)
        {
            Assert.Contains(maker.Signature.Tendency, MakerSignature.Tendencies);
            Assert.NotEmpty(maker.TooltipLineKey);
            Assert.True(maker.TierRange[0] >= 1);
        }
    }
}
