using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D23 S0's rules without a world (STEALTH.md §7.1, §7.2): what a shot is worth on a body that has not noticed it, where a shot can be
/// put and what the place is worth, the cap on any single shot, and the fact that a sound says what made it.
/// </summary>
public class MarksmanshipTests
{
    private static readonly CombatConstants Constants = new()
    {
        SneakCap = 6.0,
        RegionWeights = new Dictionary<BodyRegion, int> { [BodyRegion.Head] = 10, [BodyRegion.Torso] = 90 }.ToImmutableSortedDictionary(),
        RegionMultipliers = new Dictionary<BodyRegion, double> { [BodyRegion.Head] = 2.0, [BodyRegion.Torso] = 1.0 }.ToImmutableSortedDictionary(),
        HitZones = new Dictionary<BodyRegion, HitZone>
        {
            [BodyRegion.Head] = new(BodyRegion.Head, 2.0),
            [BodyRegion.Torso] = new(BodyRegion.Torso, 1.0),
            [BodyRegion.Limbs] = new(BodyRegion.Limbs, 0.75),
        }.ToImmutableSortedDictionary(),
    };

    private static AttackProfile Weapon(string source, double sneak, bool ranged) =>
        new(source, 9, 9, "physical_pierce", ranged ? 40_000 : 1_200, 18, 1, 6, 8) { Ranged = ranged, SneakMultiplier = sneak };

    private static CreatureDefinition Body(ImmutableSortedDictionary<BodyRegion, HitZone>? zones = null) =>
        new("creature.test.wolf", "beast", 1, 50, ImmutableSortedDictionary<BodyRegion, int>.Empty,
            ImmutableSortedDictionary<string, double>.Empty,
            new AttackProfile("ability.test.bite", 1, 1, "physical_slash", 900, 1, 1, 1, 0), 1_000, 450, 10)
        {
            HitZones = zones ?? ImmutableSortedDictionary<BodyRegion, HitZone>.Empty,
        };

    [Fact]
    public void ABowAndACrossbowCarryTheirOwnSneakMultiplier_AndOnlyOnABodyThatHasNotNoticed()
    {
        var bow = Weapon("item.weapon.hunting_bow", 2.0, ranged: true);
        var crossbow = Weapon("item.weapon.light_crossbow", 2.5, ranged: true);
        var thrown = Weapon("item.weapon.knife_brace", 2.0, ranged: true);
        Assert.Equal(2.0, StealthRules.SneakMultiplier(bow, unaware: true));
        Assert.Equal(2.5, StealthRules.SneakMultiplier(crossbow, unaware: true));
        Assert.Equal(2.0, StealthRules.SneakMultiplier(thrown, unaware: true));
        Assert.Equal(1.0, StealthRules.SneakMultiplier(crossbow, unaware: false));
        // A shot is never a dagger: the takedown's class stays the blade's.
        Assert.False(StealthRules.IsDagger(crossbow));
    }

    [Fact]
    public void ABodysOwnAnatomyIsWhatAPlacedShotIsWorth()
    {
        var wolf = Body();
        // No table of its own: the build's, where a head is worth twice the mass.
        Assert.Equal(2.0, WeakPointRules.Multiplier(wolf, Constants, BodyRegion.Head, fromFront: true));
        Assert.Equal(0.75, WeakPointRules.Multiplier(wolf, Constants, BodyRegion.Limbs, fromFront: true));

        // A boar's eye is at the front only: behind it there is skull.
        var boar = Body(new Dictionary<BodyRegion, HitZone> { [BodyRegion.Head] = new(BodyRegion.Head, 2.5, FrontOnly: true) }.ToImmutableSortedDictionary());
        Assert.Equal(2.5, WeakPointRules.Multiplier(boar, Constants, BodyRegion.Head, fromFront: true));
        Assert.Equal(1.0, WeakPointRules.Multiplier(boar, Constants, BodyRegion.Head, fromFront: false));

        // A zone the body does not offer is nothing: there is nowhere to have hit.
        var shell = Body(new Dictionary<BodyRegion, HitZone> { [BodyRegion.Torso] = new(BodyRegion.Torso, 1.0) }.ToImmutableSortedDictionary());
        Assert.Equal(1.0, WeakPointRules.Multiplier(shell, Constants, BodyRegion.Head, fromFront: true));
    }

    [Theory]
    [InlineData(2.0, 2.0, 4.0)]
    [InlineData(2.5, 2.0, 5.0)]
    [InlineData(3.0, 2.5, 6.0)]
    [InlineData(4.0, 2.0, 6.0)]
    public void NoSingleShotEarnsMoreThanTheCap(double sneak, double zone, double expected)
    {
        Assert.Equal(expected, WeakPointRules.Capped(sneak, zone, Constants), 3);
    }

    [Fact]
    public void AWeakPointShotPutsTheArrowInTheHead_AndAnOrdinaryBlowIsNotPlaced()
    {
        var bow = Weapon("item.weapon.hunting_bow", 2.0, ranged: true);
        var aimed = new TechniqueDefinition("ability.bow.weak_point_shot", "skill.bow", 1.15, 1.5, 0, 12, 40) { AimRegion = BodyRegion.Head };
        Assert.Equal(BodyRegion.Head, TechniqueRules.Aim(bow, aimed));
        // A swing has nowhere to place anything, whatever the technique says.
        var sword = Weapon("item.weapon.rusted_sword", 1.0, ranged: false);
        Assert.Null(TechniqueRules.Aim(sword, aimed));
    }

    [Fact]
    public void ASoundSaysWhatMadeIt_AndACallIsAlwaysACall()
    {
        Assert.Equal(SoundSource.Footfall, new Noise(0, 0, 3_000).HeardAs);
        Assert.Equal(SoundSource.Shot, new Noise(0, 0, 6_000) { Source = SoundSource.Shot }.HeardAs);
        Assert.Equal(SoundSource.Impact, new Noise(0, 0, 1_500) { Source = SoundSource.Impact }.HeardAs);
        Assert.Equal(SoundSource.Kill, new Noise(0, 0, 2_000) { Source = SoundSource.Kill }.HeardAs);
        // A call is a call whatever carries it, and stays the channel only its own kind answers.
        Assert.Equal(SoundSource.Call, new Noise(0, 0, 45_000) { Source = SoundSource.Shot, Call = true }.HeardAs);
        // A kill is a small sound by default: what a silent kill makes, and all it makes (§7.7).
        Assert.InRange(new NoiseRules(3_000, 8_000, 16_000, 10_000, 20_000, 45_000).KillMm, 1, 2_500);
    }
}
