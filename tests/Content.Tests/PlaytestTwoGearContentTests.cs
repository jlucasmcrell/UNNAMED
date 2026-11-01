using System.Text.Json;
using UNNAMED.Domain.Items;

namespace UNNAMED.Content.Tests;

public class PlaytestTwoGearContentTests
{
    [Fact]
    public void TheBatchHasDistinctFirstHourGear_WithRepairIconsAndAReachableShopSource()
    {
        string root = RepoPaths.Root();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "P2_GEAR_MANIFEST.json")));
        var rows = manifest.RootElement.EnumerateArray().ToArray();
        var ids = rows.Select(r => r.GetProperty("item_id").GetString()!).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(ids.Count(id => id.StartsWith("item.weapon.", StringComparison.Ordinal)) >= 15);
        Assert.All(ids, id => Assert.StartsWith("item.weapon.", id));
        Assert.True(rows.Select(r => r.GetProperty("era").GetString()).Distinct().Count() >= 3);

        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(root, "content"));
        Assert.Empty(loader.Errors);
        var catalog = ItemContent.BuildCatalog(loader);
        var merchants = ItemContent.BuildMerchants(loader);
        using var bindings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Presentation", "Art", "art_bindings.json")));

        var bow = catalog.Get("item.weapon.carved_old_bow");
        var hunting = catalog.Get("item.weapon.hunting_bow");
        var haldas = catalog.Get("item.weapon.haldas_bow");
        Assert.True(bow.WeightGrams < hunting.WeightGrams && bow.ValueBase < hunting.ValueBase);
        Assert.True(bow.Weapon!.DrawMs > hunting.Weapon!.DrawMs);
        Assert.True(bow.Weapon.DamageMin < hunting.Weapon.DamageMin);
        Assert.True(bow.Weapon.DamageMax > hunting.Weapon.DamageMax);
        Assert.True(haldas.Weapon!.DrawMs < hunting.Weapon.DrawMs);

        foreach (var row in rows)
        {
            string id = row.GetProperty("item_id").GetString()!;
            var item = catalog.Get(id);
            Assert.Equal(ItemClass.Ordinary, item.Class);
            Assert.Equal("common", item.Rarity);
            Assert.Equal(1, item.StackMax);
            Assert.InRange(item.ValueBase, 1, 75);
            Assert.InRange(item.WeightGrams, 1, 8000);
            Assert.InRange(item.DurabilityMax, 1, 600);
            Assert.NotNull(item.Repair);
            Assert.False(string.IsNullOrWhiteSpace(item.Repair!.StationKind));
            Assert.False(string.IsNullOrWhiteSpace(item.Repair.SkillId));
            Assert.InRange(item.Repair.SkillMin, 0, 5);
            Assert.NotNull(catalog.Get(item.Repair.MaterialId));
            Assert.All(item.Requirements.Attributes.Values, minimum => Assert.InRange(minimum, 0, 11));
            Assert.All(item.Requirements.Skills.Values, minimum => Assert.InRange(minimum, 0, 5));
            string merchant = row.GetProperty("merchant").GetString()!;
            Assert.Contains(merchants[merchant].Stock, stock => stock.ItemId == id && stock.Count > 0);
            Assert.Equal(row.GetProperty("icon").GetString(), bindings.RootElement.GetProperty("icons").GetProperty(id).GetString());
            var weapon = Assert.IsType<WeaponStats>(item.Weapon);
            Assert.Contains(weapon.DamageType, new[] { "physical_slash", "physical_pierce", "physical_blunt" });
            Assert.InRange(weapon.DamageMin, 1, 12);
            Assert.InRange(weapon.DamageMax, weapon.DamageMin, 13);
            Assert.Equal(row.GetProperty("art_id").GetString(), bindings.RootElement.GetProperty("weapons").GetProperty(id).GetProperty("model").GetString());
            if (weapon.DrawMs > 0)
            {
                Assert.True(weapon.TwoHanded);
                Assert.Equal("item.ammo.arrow_rough", weapon.AmmoDefId);
            }
            else
            {
                Assert.InRange(weapon.ReachMm, 1000, 2500);
                Assert.InRange(weapon.AttackMs, 450, 1300);
            }
        }
    }
}
