using System.Text.Json;
using UNNAMED.Domain.Items;

namespace UNNAMED.Content.Tests;

public class PlaytestTwoArmourContentTests
{
    [Fact]
    public void EveryBoundPieceHasCompleteStatsRepairStockCreditsAndBothNativeBodyContracts()
    {
        string root = RepoPaths.Root();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "P2_ARMOUR_MANIFEST.json")));
        var rows = manifest.RootElement.EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Length, rows.Select(r => r.GetProperty("item_id").GetString()).Distinct().Count());
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(root, "content"));
        Assert.Empty(loader.Errors);
        var catalog = ItemContent.BuildCatalog(loader);
        var merchants = ItemContent.BuildMerchants(loader);
        string credits = File.ReadAllText(Path.Combine(root, "content", "credits.yaml"));
        using var bindings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Presentation", "Art", "art_bindings.json")));
        var players = new[] { bindings.RootElement.GetProperty("people").GetProperty("player") }
            .Concat(bindings.RootElement.GetProperty("people_by_visual_option").EnumerateObject()
                .Where(o => o.Value.TryGetProperty("player", out var p) && p.TryGetProperty("equipment_outfits", out _))
                .Select(o => o.Value.GetProperty("player"))).ToArray();
        foreach (var row in rows)
        {
            string id = row.GetProperty("item_id").GetString()!;
            var item = catalog.Get(id);
            Assert.Equal("armor", item.Category);
            string yaml = File.ReadAllText(Path.Combine(root, "content", "items", "armor", id.Split('.')[^1] + ".yaml"));
            Assert.Matches(@"(?m)^material_class: (cloth|leather|mail|plate|chitin|exotic)\r?$", yaml);
            Assert.Equal(ItemClass.Ordinary, item.Class);
            Assert.Equal("common", item.Rarity);
            Assert.Equal(1, item.StackMax);
            Assert.InRange(item.ArmorValue, 1, 10);
            Assert.InRange(item.WeightGrams, 100, 8000);
            Assert.InRange(item.ValueBase, 1, 75);
            Assert.InRange(item.DurabilityMax, 100, 600);
            Assert.Equal(Enum.Parse<EquipSlot>(row.GetProperty("slot").GetString()!, true), item.Slot);
            Assert.Empty(item.Requirements.Attributes);
            Assert.Empty(item.Requirements.Skills);
            var repair = Assert.IsType<RepairSpec>(item.Repair);
            Assert.Contains(repair.StationKind, new[] { "anvil", "workbench" });
            Assert.InRange(repair.SkillMin, 1, 5);
            Assert.NotNull(catalog.Get(repair.MaterialId));
            Assert.Contains(merchants[row.GetProperty("merchant").GetString()!].Stock, s => s.ItemId == id && s.Count > 0);
            Assert.Equal(row.GetProperty("icon").GetString(), bindings.RootElement.GetProperty("icons").GetProperty(id).GetString());
            string art = row.GetProperty("art_id").GetString()!;
            foreach (var player in players)
            {
                Assert.Equal(art + "__humgen_creator_female", Assert.Single(player.GetProperty("equipment_outfits").GetProperty(id).EnumerateArray()).GetString());
                if (item.Slot == EquipSlot.Feet)
                    Assert.Equal(art + "_underlayer__humgen_creator_female", Assert.Single(player.GetProperty("equipment_outfits").GetProperty("under:" + id).EnumerateArray()).GetString());
            }
            var contracts = row.GetProperty("native_contracts").EnumerateArray().ToArray();
            Assert.Equal(new[] { "humgen_creator_female", "humgen_creator_male" }, contracts.Select(c => c.GetProperty("body").GetString()).Order());
            Assert.Equal(2, contracts.Select(c => c.GetProperty("body_sha256").GetString()).Distinct().Count());
            foreach (var contract in contracts)
            {
                Assert.Equal(0, contract.GetProperty("native_rest_error").GetDouble());
                Assert.Equal(64, contract.GetProperty("contract_sha256").GetString()!.Length);
                Assert.Contains(contract.GetProperty("identifier").GetString()!, credits);
            }
            Assert.Contains(row.GetProperty("author").GetString()!, credits);
            Assert.Contains(row.GetProperty("source_url").GetString()!, credits);
            if (row.TryGetProperty("upstream", out var upstream) && upstream.TryGetProperty("title", out _))
            {
                Assert.Contains(upstream.GetProperty("author").GetString()!, credits);
                Assert.Contains(upstream.GetProperty("source_url").GetString()!, credits);
                Assert.Contains(upstream.GetProperty("licence_url").GetString()!, credits);
            }
        }
    }
}
