using System.Text.Json;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Items;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

public class PlaytestTwoGearTests
{
    [Fact]
    public void KerasNewWeaponsCanBeBoughtEquippedAndSavedByAFirstHourCharacter()
    {
        const string kera = "npc.ashen_hollow.kera_voss";
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Harness.RepoRoot(), "docs", "P2_GEAR_MANIFEST.json")));
        string[] ids = manifest.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("item_id").GetString()!)
            .Where(id => id.StartsWith("item.weapon.", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(ids);
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var site = session.Setup.Sites(layout => layout.Npcs).Single(pair => pair.Site.NpcId == kera).Site;
        var probe = Arena.OpenCreatures(session, session.Setup, (site.XMm / 1000.0, site.ZMm / 1000.0), 0,
            Array.Empty<(string, double, double, string)>(), origin: Harness.Region);
        var vendor = probe.Simulation.Npcs.Single(npc => npc.Id == kera);
        // Approach the open counter side, rather than standing behind the smithy's blocker.
        var arena = Arena.OpenCreatures(session, session.Setup, (vendor.Body.XMm / 1000.0 - 1.3, vendor.Body.ZMm / 1000.0 + .7), 0,
            Array.Empty<(string, double, double, string)>(), r => new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm,
                r.AppearanceSeed, r.Inventory, r.Progression with
                {
                    Level = 2,
                    Allocation = r.Progression.Allocation.SetItem(CharacterAttribute.Might, 1),
                }, r.FacingMdeg, r.Discoveries, r.Equipment, 5000, r.Effects, r.Relationships, r.Conversations)
                { SpaceId = vendor.Space.ToString() },
            origin: Harness.Region);
        foreach (string id in ids)
        {
            var ware = arena.Simulation.Wares(kera)!.Wares.Single(ware => ware.ItemId == id);
            string? rejection = arena.Submit(new BuyCommand(arena.Player, kera, ware.Ref, 1));
            var actualVendor = arena.Simulation.Npcs.Single(npc => npc.Id == kera);
            var player = arena.Simulation.Player.Body;
            string walls = string.Join(",", session.Setup.SpaceFor(actualVendor.Space).Blockers
                .Where(wall => wall.Crosses(player.XMm, player.ZMm, actualVendor.Body.XMm, actualVendor.Body.ZMm)).Select(wall => wall.Id));
            Assert.True(rejection is null, $"{id}: {rejection}; player={player}, vendor={actualVendor.Body}, walls={walls}");
            var carried = arena.Simulation.Player.Inventory.Single(item => item.DefId == id);
            Assert.Null(arena.Submit(new EquipCommand(arena.Player, carried.ItemId)));
            Assert.Equal(carried.ItemId, arena.Simulation.Player.Equipment[EquipSlot.MainHand]);
        }
        var slot = SaveSlots.Manual("p2_gear");
        new SaveStore(profile.Root).Save(slot, SaveDocuments.Capture(arena.Simulation.World,
            arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0));
        Assert.True(session.Load(slot).IsComplete);
        var loaded = session.Simulation!.Player;
        Assert.All(ids, id => Assert.Contains(loaded.Inventory, item => item.DefId == id));
        Assert.Equal(ids[^1], loaded.Inventory.Single(item => item.ItemId == loaded.Equipment[EquipSlot.MainHand]).DefId);
    }
}
