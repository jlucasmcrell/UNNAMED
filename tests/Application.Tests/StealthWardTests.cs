using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S7's ward (STEALTH.md section 10.4): a ward of still air shuts one sense. A wolf downwind that would otherwise smell the character finds nothing, and the
/// same wolf still sees them - it is a ward, not a cloak.
/// </summary>
public class StealthWardTests
{
    private const string Wolf = "creature.beast.wolf_grey";
    private const string Ward = "item.armor.stills_ward";

    private static Func<PlayerRecord, PlayerRecord> Wearing(bool ward) => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId == Ward).ToList();
        var equipment = Array.Empty<KeyValuePair<EquipSlot, EntityId>>();
        if (ward)
        {
            if (inventory.Count == 0)
                inventory.Add(Arena.Stack(Ward, 1));
            equipment = new[] { KeyValuePair.Create(EquipSlot.Amulet, inventory[0].ItemId) };
        }
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, equipment, r.Currency, r.Effects);
    };

    /// <summary>Downwind at dawn the wolf smells the character; the same walk with the ward on the throat, and it does not.</summary>
    [Fact]
    public void AWardOfStillAir_ShutsTheNose()
    {
        NoticeKind? Notice(Func<PlayerRecord, PlayerRecord> kit)
        {
            using var profile = new TempProfile();
            var session = Harness.Boot(profile);
            long wind = Arena.OpenCreatures(session, session.Setup, (470, 745), 0, Array.Empty<(string, double, double, string)>(),
                null, startTick: 40_000, origin: (0, 0)).Simulation.WindBearing();
            double rad = wind / 1000.0 * Math.PI / 180;
            var arena = Arena.OpenCreatures(session, session.Setup, (470, 745), 0,
                new[] { (Wolf, 470 + Math.Sin(rad) * 26, 745 + Math.Cos(rad) * 26, "pack_hunter") }, kit,
                startTick: 40_000, origin: (0, 0));
            arena.Tick(200);
            for (int i = 0; i < 120; i++)
            {
                arena.Tick(1);
                if (arena.Creature().Awareness > 0)
                    return arena.Creature().LastNotice;
            }
            return null;
        }

        Assert.NotNull(Notice(Wearing(ward: false)));
        Assert.Null(Notice(Wearing(ward: true)));
    }
}