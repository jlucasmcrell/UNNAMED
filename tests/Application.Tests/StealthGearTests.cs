using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S7's cloak and watchglass (STEALTH.md sections 10.1 and 10.3): a cloak takes a share of the body off the eye, and a held device doubles how far the
/// wearer reads what has noticed them. Both are worn, not carried, and the best piece sets each number.
/// </summary>
public class StealthGearTests
{
    private const string Cloak = "item.armor.veil_cloak";
    private const string Watchglass = "item.tool.watchglass";

    private static Func<PlayerRecord, PlayerRecord> Wearing(string defId, EquipSlot slot) => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId == defId).ToList();
        var equipment = Array.Empty<KeyValuePair<EquipSlot, EntityId>>();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack(defId, 1));
        equipment = new[] { KeyValuePair.Create(slot, inventory[0].ItemId) };
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, equipment, r.Currency, r.Effects);
    };

    /// <summary>A cloak in daylight cuts the visibility factor by a fifth.</summary>
    [Fact]
    public void AVeilCloakTakesAShareOffTheBody()
    {
        double Visible(Func<PlayerRecord, PlayerRecord> kit)
        {
            using var profile = new TempProfile();
            var session = Harness.Boot(profile);
            var arena = Arena.OpenCreatures(session, session.Setup, (470, 745), 0, new[] { ("creature.beast.wolf_grey", 470.0, 762.0, "pack_hunter") },
                kit, startTick: 12_000, origin: (0, 0));
            arena.Tick(3);
            return arena.Simulation.PlayerVisibilityTo(arena.Creature().Id);
        }

        double bare = Visible(r => r);
        double cloaked = Visible(Wearing(Cloak, EquipSlot.Cloak));
        Assert.True(bare > 0, "a body in daylight is visible");
        Assert.True(cloaked < bare, $"the cloak has to cut it ({bare:0.00} to {cloaked:0.00})");
        Assert.InRange(cloaked, bare * 0.75, bare * 0.85);
    }

    /// <summary>A watchglass doubles the range at which the wearer reads what has noticed them.</summary>
    [Fact]
    public void AWatchglassDoublesTheDetectionRange()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (470, 745), 0, Array.Empty<(string, double, double, string)>(),
            Wearing(Watchglass, EquipSlot.OffHand), startTick: 12_000, origin: (0, 0));
        arena.Tick(3);
        // Thirty metres of the eye's own reach, plus the glass's thirty.
        Assert.Equal(60_000, arena.Simulation.PlayerDetectionRangeMm());
    }
}