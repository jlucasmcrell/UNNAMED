using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S7's sound dampeners (STEALTH.md section 10.2): what is worn takes a share off the step before the skill does, and two pads do not make a silent boot.
/// </summary>
public class StealthDampenerTests
{
    private const string Pads = "item.tool.dampener_pads";

    private static Func<PlayerRecord, PlayerRecord> Wearing(bool pads) => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId == Pads).ToList();
        var equipment = Array.Empty<KeyValuePair<EquipSlot, EntityId>>();
        if (pads)
        {
            if (inventory.Count == 0)
                inventory.Add(Arena.Stack(Pads, 1));
            equipment = new[] { KeyValuePair.Create(EquipSlot.Feet, inventory[0].ItemId) };
        }
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, equipment, r.Currency, r.Effects);
    };

    /// <summary>The same walk on the same ground, once barefoot and once in pads: the ring is a third smaller in the pads.</summary>
    [Fact]
    public void PadsTakeAShareOffTheStep()
    {
        long Ring(Func<PlayerRecord, PlayerRecord> kit)
        {
            using var profile = new TempProfile();
            var session = Harness.Boot(profile);
            var arena = Arena.OpenCreatures(session, session.Setup, (470, 745), 0, Array.Empty<(string, double, double, string)>(),
                kit, startTick: 12_000, origin: (0, 0));
            arena.Tick(5);
            arena.Submit(new MoveCommand(arena.Player, new MoveIntent(1_000, 0, Gait.Walk, 0)));
            arena.Tick(3);
            return arena.Simulation.PlayerNoiseMm;
        }

        long bare = Ring(Wearing(pads: false));
        long padded = Ring(Wearing(pads: true));
        Assert.True(bare > 0, "a walking body makes a sound");
        Assert.True(padded < bare, $"pads take a share off the step ({bare} mm to {padded} mm)");
        Assert.InRange(padded, (long)(bare * 0.55), (long)(bare * 0.75));
    }
}