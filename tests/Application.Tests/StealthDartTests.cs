using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S5's sleep dart and marksman rifle (STEALTH.md sections 7.4 and 8): the dart leaves what it hits asleep rather than dead, and the rifle carries further
/// and cracks louder than any bow.
/// </summary>
public class StealthDartTests
{
    private const string Dart = "item.weapon.sleep_dart";
    private const string Rifle = "item.weapon.marksman_rifle";
    private const string Wolf = "creature.beast.wolf_grey";

    private static Func<PlayerRecord, PlayerRecord> Wielding(string weapon, string ammo) => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId == weapon || e.DefId == ammo).ToList();
        if (!inventory.Any(e => e.DefId == weapon))
            inventory.Add(Arena.Stack(weapon, 1));
        if (!inventory.Any(e => e.DefId == ammo))
            inventory.Add(Arena.Stack(ammo, 8));
        var weaponId = inventory.First(e => e.DefId == weapon).ItemId;
        // The rifle and the dart ask for might or precision; a levelled character has both enough to hold them.
        var progression = r.Progression with
        {
            Allocation = r.Progression.Allocation
                .SetItem(CharacterAttribute.Might, 14)
                .SetItem(CharacterAttribute.Precision, 12),
        };
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, weaponId) }, r.Currency, r.Effects);
    };

    /// <summary>A dart into an unaware wolf leaves it asleep where it stood, and the noise it makes is a pipe's, not a bow's.</summary>
    [Fact]
    public void ADart_LeavesTheBodyAsleep_NotDead()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (470, 745), 180,
            new[] { (Wolf, 470.0, 745.0 - 6, "pack_hunter") }, Wielding(Dart, "item.consumable.sleep_coating"),
            startTick: 40_000, origin: (0, 0));
        arena.Tick(3);
        arena.Submit(new AttackCommand(arena.Player));
        for (int i = 0; i < 60 && arena.Creature().Alive && !arena.Creature().Asleep; i++)
            arena.Tick();
        var body = arena.Creature();
        Assert.True(body.Alive, "a dart does not kill");
        Assert.True(body.Asleep, "a dart puts the body to sleep where it stood");
        Assert.True(arena.Simulation.PlayerNoiseMm <= 3_100, $"the pipe is quiet ({arena.Simulation.PlayerNoiseMm} mm)");
    }

    /// <summary>The rifle's crack is nine metres - a street hears it - and its reach is longer than a bow's.</summary>
    [Fact]
    public void TheMarksmanRifle_CarriesFurtherAndCracksLouder()
    {
        var session = Harness.Boot(new TempProfile());
        var catalog = session.Setup.Items.Catalog;
        var rifle = catalog.Find(Rifle)!.Weapon!;
        var bow = catalog.Find("item.weapon.hunting_bow")!.Weapon!;
        Assert.Equal(9, rifle.ShotNoiseMm / 1_000);
        Assert.Equal(6, bow.ShotNoiseMm / 1_000);
        Assert.True(rifle.OnHitEffect is null, "a rifle wounds; the dart is what sleeps");
        Assert.Equal("effect.sleep", catalog.Find(Dart)!.Weapon!.OnHitEffect);
    }
}