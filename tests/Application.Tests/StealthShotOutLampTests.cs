using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S1's "shot out" end to end (STEALTH.md section 2.2): somewhere in the real region a lamp must be shootable from a clear line, or the mechanic is only a
/// command. The test walks bearings around the yard lamp, fires, and requires that at least one of them puts it out.
/// </summary>
public class StealthShotOutLampTests
{
    private const string Lamp = "light.waystation_yard";
    private static readonly (long XMm, long ZMm) LampAt = (447_000, 730_000);

    private static Func<PlayerRecord, PlayerRecord> Archer() => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.light_crossbow" or "item.ammo.bolt_iron").ToList();
        if (!inventory.Any(e => e.DefId == "item.weapon.light_crossbow"))
            inventory.Add(Arena.Stack("item.weapon.light_crossbow", 1));
        if (!inventory.Any(e => e.DefId == "item.ammo.bolt_iron"))
            inventory.Add(Arena.Stack("item.ammo.bolt_iron", 8));
        var bow = inventory.First(e => e.DefId == "item.weapon.light_crossbow").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, bow) }, r.Currency, r.Effects);
    };

    [Fact]
    public void ALampInTheRegion_CanBeShotOut_FromAClearLine()
    {
        var tried = new List<string>();
        for (int bearing = 0; bearing < 16; bearing++)
        {
            double rad = bearing * Math.PI / 8;
            double px = (LampAt.XMm - Math.Sin(rad) * 5_000) / 1000.0, pz = (LampAt.ZMm - Math.Cos(rad) * 5_000) / 1000.0;
            using var profile = new TempProfile();
            var session = Harness.Boot(profile);
            var arena = Arena.OpenCreatures(session, session.Setup, (px, pz), (int)(bearing * 22.5), Array.Empty<(string, double, double, string)>(),
                Archer(), startTick: 40_000, origin: (0, 0));
            arena.Tick(3);
            arena.Submit(new AttackCommand(arena.Player));
            for (int i = 0; i < 60 && !arena.Simulation.IsLightDoused(Lamp); i++)
                arena.Tick();
            if (arena.Simulation.IsLightDoused(Lamp))
            {
                // The lane it was lighting has to go darker with it, or the lamp going out would be a cosmetic fact.
                Assert.True(arena.Simulation.PlayerLitPermille < 900, "a doused lamp cannot leave the lane at full day");
                return;
            }
            var body = arena.Simulation.Player.Body;
            tried.Add($"{bearing * 22.5:0}deg from ({body.XMm / 1000.0:0},{body.ZMm / 1000.0:0})");
        }
        Assert.Fail($"no bearing put the yard lamp out; shots came from: {string.Join("; ", tried)}");
    }
}