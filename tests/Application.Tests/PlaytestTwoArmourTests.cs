using System.Text.Json;
using UNNAMED.Domain.Items;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

public class PlaytestTwoArmourTests
{
    [Fact]
    public void BoundArmourCanBeBoughtWornSavedAndRemovedByAFirstHourCharacter()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Harness.RepoRoot(), "docs", "P2_ARMOUR_MANIFEST.json")));
        var rows = manifest.RootElement.EnumerateArray().ToArray();
        Assert.Equal(20, rows.Length);
        foreach (var group in rows.GroupBy(r => r.GetProperty("merchant").GetString()!))
        {
            string npcId = group.Key.Replace("merchant.", "npc.", StringComparison.Ordinal);
            using var profile = new TempProfile();
            using var session = Harness.Boot(profile);
            var site = session.Setup.Sites(layout => layout.Npcs).Single(pair => pair.Site.NpcId == npcId).Site;
            var probe = Arena.OpenCreatures(session, session.Setup, (site.XMm / 1000.0, site.ZMm / 1000.0), 0,
                Array.Empty<(string, double, double, string)>(), origin: Harness.Region);
            var vendor = probe.Simulation.Npcs.Single(npc => npc.Id == npcId);
            var arena = Arena.OpenCreatures(session, session.Setup,
                (vendor.Body.XMm / 1000.0 - 1.3, vendor.Body.ZMm / 1000.0 + .7), 0,
                Array.Empty<(string, double, double, string)>(),
                r => new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression,
                    r.FacingMdeg, r.Discoveries, r.Equipment, 5000, r.Effects, r.Relationships, r.Conversations)
                    { SpaceId = vendor.Space.ToString() }, origin: Harness.Region);
            var lastBySlot = new Dictionary<EquipSlot, string>();
            foreach (var row in group)
            {
                string id = row.GetProperty("item_id").GetString()!;
                var slot = Enum.Parse<EquipSlot>(row.GetProperty("slot").GetString()!, true);
                var ware = arena.Simulation.Wares(npcId)!.Wares.Single(w => w.ItemId == id);
                Assert.Null(arena.Submit(new BuyCommand(arena.Player, npcId, ware.Ref, 1)));
                var carried = arena.Simulation.Player.Inventory.Single(item => item.DefId == id);
                Assert.Null(arena.Submit(new EquipCommand(arena.Player, carried.ItemId)));
                Assert.Equal(carried.ItemId, arena.Simulation.Player.Equipment[slot]);
                Assert.Null(arena.Submit(new UnequipCommand(arena.Player, slot)));
                Assert.False(arena.Simulation.Player.Equipment.ContainsKey(slot));
                Assert.Null(arena.Submit(new EquipCommand(arena.Player, carried.ItemId)));
                lastBySlot[slot] = id;
            }
            var save = SaveSlots.Manual("p2_armour");
            new SaveStore(profile.Root).Save(save, SaveDocuments.Capture(arena.Simulation.World,
                arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0));
            Assert.True(session.Load(save).IsComplete);
            var loaded = session.Simulation!.Player;
            foreach (var (slot, id) in lastBySlot)
                Assert.Equal(id, loaded.Inventory.Single(item => item.ItemId == loaded.Equipment[slot]).DefId);
            Assert.All(group, row => Assert.Contains(loaded.Inventory, item => item.DefId == row.GetProperty("item_id").GetString()));
        }
    }
}
