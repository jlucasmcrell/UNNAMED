using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S0's shot on the HUD (STEALTH.md section 2.7, 7.8): the ring the player reads is the radius the ear is read by, so a shot - the loudest thing a
/// stealth player does - has to show up on it while it is in the air. Found by reading the proof route's own numbers: every beat of the first
/// good run reported 0 m through the shot, because the ring only ever knew about footsteps and swings.
/// </summary>
public class StealthShotRingTests
{
    private static Func<PlayerRecord, PlayerRecord> Archer() => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.hunting_bow" or "item.ammo.arrow_rough").ToList();
        if (!inventory.Any(e => e.DefId == "item.weapon.hunting_bow"))
            inventory.Add(Arena.Stack("item.weapon.hunting_bow", 1));
        if (!inventory.Any(e => e.DefId == "item.ammo.arrow_rough"))
            inventory.Add(Arena.Stack("item.ammo.arrow_rough", 12));
        var bow = inventory.First(e => e.DefId == "item.weapon.hunting_bow").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, bow) }, r.Currency, r.Effects);
    };

    /// <summary>Loose an arrow and the ring reads the bow's own twang, not nothing and not a swing's radius.</summary>
    [Fact]
    public void AShotInTheAir_ShowsOnTheRingAtTheBowsOwnRadius()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (470, 745), 0, Array.Empty<(string, double, double, string)>(),
            Archer(), startTick: 12_000, origin: (0, 0));
        arena.Tick(5);
        Assert.Equal(0, arena.Simulation.PlayerNoiseMm);
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        long loudest = 0;
        for (int i = 0; i < 40; i++)
        {
            arena.Tick(1);
            loudest = Math.Max(loudest, arena.Simulation.PlayerNoiseMm);
        }
        // The hunting bow's own shot_noise_m is 6 m.
        Assert.Equal(6_000, loudest);
    }
}