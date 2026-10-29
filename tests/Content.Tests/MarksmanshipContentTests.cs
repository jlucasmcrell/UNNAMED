using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;

namespace UNNAMED.Content.Tests;

/// <summary>
/// D23 S0 from content (STEALTH.md §7.1, §7.2, §7.8): the ranged weapons a distance kill is made with carry the numbers that make it one -
/// their own sneak multiplier, their own shot noise and ammunition - and a body's anatomy authors where a placed shot is worth taking.
/// </summary>
public class MarksmanshipContentTests
{
    private static ContentLoader Game()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    [Theory]
    [InlineData("item.weapon.hunting_bow", 2.0, 6)]
    [InlineData("item.weapon.haldas_bow", 2.0, 6)]
    [InlineData("item.weapon.light_crossbow", 2.5, 10)]
    [InlineData("item.weapon.knife_brace", 2.0, 3)]
    public void EveryRangedWeaponCarriesItsOwnSneakMultiplier_AndItsOwnShot(string id, double sneak, double shotMetres)
    {
        var weapon = Assert.IsType<WeaponStats>(ItemContent.BuildCatalog(Game()).Find(id)?.Weapon);
        Assert.True(weapon.Ranged, $"{id} is a ranged weapon");
        Assert.Equal(sneak, weapon.SneakMultiplier, 3);
        Assert.Equal((long)(shotMetres * 1000), weapon.ShotNoiseMm);
        Assert.NotNull(weapon.AmmoDefId);
    }

    [Fact]
    public void EveryRangedWeaponsAmmunitionExists_AndIsNotTheWeaponItself()
    {
        var items = ItemContent.BuildCatalog(Game());
        foreach (var weapon in items.Definitions.Values.Where(i => i.Weapon is { Ranged: true }))
        {
            string ammo = weapon.Weapon!.AmmoDefId!;
            Assert.True(items.Find(ammo) is not null, $"{weapon.Id} names {ammo} as its ammunition, and there is no such item");
            Assert.NotEqual(weapon.Id, ammo);
        }
    }

    [Fact]
    public void TheDaggerClassSwingsQuietly_AndTheBowDoesNotSwingAtAll()
    {
        var items = ItemContent.BuildCatalog(Game());
        Assert.Equal(2_000, items.Find("item.weapon.iron_dagger")!.Weapon!.SwingNoiseMm);
        Assert.Equal(0, items.Find("item.weapon.hunting_bow")!.Weapon!.SwingNoiseMm);
    }

    [Fact]
    public void ABodyThatAuthorsItsOwnAnatomyIsReadAsAuthored()
    {
        var creatures = CombatContent.BuildCreatures(Game(), tickMs: 50);

        // A boar's eye is at the front: 2.5 from in front, nothing from behind a skull.
        var boar = creatures["creature.beast.bristleback_boar"].HitZones[BodyRegion.Head];
        Assert.Equal(2.5, boar.Multiplier, 3);
        Assert.True(boar.FrontOnly);

        // A spider's abdomen, not its head: the mass is the shot.
        var spider = creatures["creature.beast.cave_hunting_spider"].HitZones;
        Assert.True(spider[BodyRegion.Torso].Multiplier > spider[BodyRegion.Head].Multiplier);

        // The vermin that carry no table of their own take the build's: a head is worth twice the mass there.
        Assert.True(creatures["creature.beast.wolf_grey"].HitZones.IsEmpty);
    }

    [Fact]
    public void TheWeakPointShotIsATechniqueOfTheBow_ThatPutsItsShotInTheHead()
    {
        var techniques = TeachingContent.BuildTechniques(Game());
        var shot = techniques["ability.bow.weak_point_shot"];
        Assert.Equal("skill.bow", shot.SkillId);
        Assert.Equal(BodyRegion.Head, shot.AimRegion);
        Assert.True(shot.DamageMultiplier > 1);
    }
}
