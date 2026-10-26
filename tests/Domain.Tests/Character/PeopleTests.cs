using System.Collections.Immutable;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Tests.Progression;

namespace UNNAMED.Domain.Tests.Character;

/// <summary>
/// The owner-approved race traits and abilities (RACE_ABILITIES_PROPOSAL.md, 2026-10-24) as rules: a people's aptitude is a rate and a
/// ceiling and nothing else, its biology moves the derived numbers, the Orenth crossing toll takes what it takes and never progress, a blink
/// steps aside but never through a wall, and Kal wings glide, flare and leap.
/// </summary>
public class PeopleTests
{
    private static readonly ProgressionRules Rules = TestRules.Default();

    private static readonly LearningAptitude Veth = new()
    {
        RateBands = ImmutableArray.Create(new RateBand(0, 150), new RateBand(30, 125), new RateBand(60, 100)),
        SkillPercent = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create(TestRules.Blade, 110) }),
        MasteryCeiling = 90,
        NoveltyPercent = 150,
    };

    private static readonly LearningAptitude Siann = new()
    {
        RateBands = ImmutableArray.Create(new RateBand(0, 70), new RateBand(30, 80), new RateBand(60, 110)),
        CommonCeiling = 65,
    };

    private static readonly LearningAptitude NoMagic = new()
    {
        SkillPercent = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create("skill.force", 0) }),
    };

    private static long Gained(LearningAptitude aptitude, string skill, int level, string? novelty = null)
    {
        var progression = CharacterProgression.Empty with
        {
            Skills = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create(skill, new SkillState(level, 0)) }),
        };
        return ProgressionEngine.Practice(progression, new SkillPractice(skill, level + 15, PracticeOutcome.Success, 0) { NoveltyKey = novelty }, Rules, aptitude).XpGained;
    }

    [Fact]
    public void Aptitude_IsARateByBand_AndALeaning_AndNothingMore()
    {
        long usual = Gained(LearningAptitude.Usual, TestRules.Athletics, 10);
        Assert.Equal(usual * 150 / 100, Gained(Veth, TestRules.Athletics, 10));
        Assert.Equal(usual * 125 / 100, Gained(Veth, TestRules.Athletics, 40));
        Assert.Equal(usual, Gained(Veth, TestRules.Athletics, 59 + 1));
        // The leaning multiplies the band: the Veth learn the blade at 150% x 110%.
        Assert.Equal((long)Math.Round(usual * 1.65, MidpointRounding.AwayFromZero), Gained(Veth, TestRules.Blade, 10));
        Assert.Equal(usual * 70 / 100, Gained(Siann, TestRules.Athletics, 10));
        // No affinity at all: no skill XP, and no novelty bonus either.
        Assert.Equal(0, Gained(NoMagic, "skill.force", 10, novelty: "spell.ember.bolt"));
    }

    [Fact]
    public void AFirstSuccess_TeachesTheVethHalfAsMuchAgain()
    {
        long plain = Gained(Veth, TestRules.Athletics, 10);
        Assert.Equal(plain + (long)Math.Round(Rules.Skills.NoveltyBonusXp * 1.5, MidpointRounding.AwayFromZero), Gained(Veth, TestRules.Athletics, 10, novelty: "ability.blade.power_strike"));
    }

    [Fact]
    public void TheSiann_ReachTheHigherCeiling_AndEveryoneElseStopsAtTheCommonOne()
    {
        var at = CharacterProgression.Empty with
        {
            Skills = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create(TestRules.Athletics, new SkillState(59, 0)) }),
        };
        var practice = new SkillPractice(TestRules.Athletics, 120, PracticeOutcome.Success, 0);
        var usual = at;
        var siann = at;
        for (int i = 0; i < 400; i++)
        {
            usual = ProgressionEngine.Practice(usual, practice, Rules).Progression;
            siann = ProgressionEngine.Practice(siann, practice, Rules, Siann).Progression;
        }
        Assert.Equal(60, ProgressionEngine.SkillLevel(usual, TestRules.Athletics));
        Assert.Equal(65, ProgressionEngine.SkillLevel(siann, TestRules.Athletics));
    }

    [Fact]
    public void APeoplesGrant_MayTakeAPointAway_NoOtherGrantMay()
    {
        var p = ProgressionEngine.Grant(CharacterProgression.Empty, new AttributeGrant(CharacterAttribute.Agility, -1, GrantSource.Race, "people.kal.agility"), Rules);
        Assert.Equal(9, ProgressionEngine.AttributeValue(p.Validate(), CharacterAttribute.Agility, Rules));
        Assert.Throws<ArgumentException>(() =>
            ProgressionEngine.Grant(CharacterProgression.Empty, new AttributeGrant(CharacterAttribute.Agility, -1, GrantSource.Quest, "quest.a"), Rules));
        Assert.Throws<ArgumentException>(() =>
            ProgressionEngine.Grant(CharacterProgression.Empty, new AttributeGrant(CharacterAttribute.Agility, 0, GrantSource.Race, "people.kal.agility"), Rules));
    }

    [Fact]
    public void ThePeoplesTraits_MoveHealthAndResonance_AndTheTollTakesHealthOnly()
    {
        var stats = new DerivedStats(100, 100, 80, 20, 30);
        var kal = new PeopleTraits { HealthPercent = 110, ResonancePercent = 50 };
        Assert.Equal(stats with { HealthMax = 110, Resonance = 10 }, PeopleRules.Derive(stats, kal));
        Assert.Equal(stats with { HealthMax = 97 }, PeopleRules.Derive(stats, PeopleTraits.None, tollTier: 3));
        Assert.Equal(stats with { HealthMax = 90 }, PeopleRules.Derive(stats, PeopleTraits.None, tollTier: 99));
        var resist = PeopleRules.Resistances(new PeopleTraits
        {
            DamageTakenPercent = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create("physical_slash", 85), KeyValuePair.Create("necrotic", 150) }),
        });
        Assert.Equal(0.15, resist["physical_slash"], 6);
        Assert.Equal(-0.5, resist["necrotic"], 6);
    }

    [Fact]
    public void TheToll_TakesOneThingATier_InTheCycle_NeverTheSameTwice_AndIsHidden()
    {
        var places = new[] { "location.a", "location.b" };
        var names = new[] { "npc.x" };
        var crossing = TollRules.Pay(Crossing.None, 99, places, names);
        Assert.Equal(0, crossing.Tier);
        Assert.Empty(crossing.Costs);
        crossing = TollRules.Pay(crossing, 1, places, names);
        Assert.Equal(new[] { TollKind.Scar }, crossing.Costs.Select(c => c.Kind));
        crossing = TollRules.Pay(crossing, 1000, places, names);
        Assert.Equal(TollRules.MaxTier, crossing.Tier);
        Assert.Equal(TollRules.MaxTier, crossing.Costs.Length);
        Assert.Equal(new[] { TollKind.Scar, TollKind.Memory, TollKind.Name, TollKind.Years, TollKind.Scar, TollKind.Memory, TollKind.Name },
            crossing.Costs.Take(7).Select(c => c.Kind));
        var taken = crossing.Costs.Select(c => c.Ref).OfType<string>().ToList();
        Assert.Equal(taken.Distinct().Count(), taken.Count);
        Assert.Equal(new[] { "location.a", "location.b", "npc.x" }, taken.Order(StringComparer.Ordinal));
        Assert.Null(crossing.Problem());
        Assert.Throws<ArgumentOutOfRangeException>(() => TollRules.Pay(crossing, -1, places, names));
        // The same crossings take the same things: a replay is the same character.
        Assert.Equal(crossing.Costs, TollRules.Pay(TollRules.Pay(Crossing.None, 100, places, names), 1000, places, names).Costs);
        Assert.DoesNotContain(TollRules.Ledger(crossing.Tier), c => char.IsDigit(c));
    }

    private static readonly MovementRules Moving = new(3200, 50, 160, 350, 1600);

    private static WalkSpace Flat(params Blocker[] blockers) =>
        new(0, 0, 40000, 40000, new TerrainGrid(0, 0, 40000, 2, 2, new long[] { 0, 0, 0, 0 }), blockers.ToImmutableArray());

    [Fact]
    public void ABlink_StepsUpToItsRange_PastABody_ButNeverThroughAWall()
    {
        var from = new Body(5000, 0, 5000, 90_000);
        var open = PeopleRules.BlinkTarget(from, 1000, 0, 8000, Moving, Flat(), Array.Empty<Blocker>(), Array.Empty<Blocker>());
        Assert.Equal(new Body(13000, 0, 5000, 90_000), open);

        var creature = new CircleBlocker("wolf", 8000, 5000, 600, 0);
        var past = PeopleRules.BlinkTarget(from, 1000, 0, 8000, Moving, Flat(), Array.Empty<Blocker>(), new Blocker[] { creature });
        Assert.Equal(13000, past!.XMm);

        var wall = new BoxBlocker("wall", 9000, 0, 9500, 10000, 3000);
        var stopped = PeopleRules.BlinkTarget(from, 1000, 0, 8000, Moving, Flat(wall), Array.Empty<Blocker>(), Array.Empty<Blocker>());
        Assert.InRange(stopped!.XMm, 5250, 9000 - 350);

        var door = new BoxBlocker("door", 5300, 4000, 5600, 6000, 2500);
        Assert.Null(PeopleRules.BlinkTarget(from, 1000, 0, 8000, Moving, Flat(), new Blocker[] { door }, Array.Empty<Blocker>()));
    }

    private static readonly MovementRules Winged = Moving with
    {
        Wings = new WingRules { LeapApexBonusMm = 500, LeapDistancePercent = 160, GlideSpeedMmPerSecond = 7000, GlideSinkMmPerSecond = 2500, FlareSinkMmPerSecond = 1000 },
    };

    private static readonly MoveIntent East = new(1000, 0, Gait.Sprint, 90_000);

    private static (Body Body, Posture Posture, int Ticks) Fly(Body body, Posture posture, MovementRules rules, WalkSpace space, MoveIntent intent)
    {
        int ticks = 0;
        while (posture.Airborne && ticks < 2000)
        {
            (body, posture) = Kinematics.Step(body, posture, intent, rules, space, Array.Empty<Blocker>(), 50);
            ticks++;
        }
        return (body, posture, ticks);
    }

    [Fact]
    public void AGapLeap_GoesHigherAndFurther_ThanAPlainJump()
    {
        var start = new Body(1000, 0, 5000, 90_000);
        var (plain, _, _) = Fly(start, new Posture(Stance.Standing, true, 0), Winged, Flat(), East);
        var (leap, landed, _) = Fly(start, new Posture(Stance.Standing, true, 0) { Wing = WingState.Leap }, Winged, Flat(), East);
        Assert.InRange((leap.XMm - start.XMm) / (double)(plain.XMm - start.XMm), 1.55, 1.65);
        Assert.Equal(WingState.Folded, landed.Wing);
        Assert.Equal(1650, Winged.Leaping().JumpApexMm);
    }

    [Fact]
    public void AGlide_HoldsItsHeightOverFallingGround_AndLandsWithWingsFolded()
    {
        // A slope that falls 20 m over 40 m eastward: a body following the ground would be at its foot in no time.
        var slope = new WalkSpace(0, 0, 40000, 40000, new TerrainGrid(0, 0, 40000, 2, 2, new long[] { 20000, 0, 20000, 0 }), ImmutableArray<Blocker>.Empty);
        var start = new Body(1000, slope.HeightAtMm(1000, 5000) + 1000, 5000, 90_000);
        var glide = new Posture(Stance.Standing, true, 0) { Wing = WingState.Glide, FeetMm = start.YMm };
        var (after, mid) = Kinematics.Step(start, glide, East, Winged, slope, Array.Empty<Blocker>(), 1000);
        Assert.Equal(start.YMm - 2500, after.YMm);
        Assert.Equal(start.XMm + 7000, after.XMm);
        Assert.True(mid.Aloft);
        Assert.True(after.YMm > slope.HeightAtMm(after.XMm, after.ZMm));

        var (landed, posture, ticks) = Fly(start, glide, Winged, slope, East);
        Assert.False(posture.Airborne);
        Assert.Equal(WingState.Folded, posture.Wing);
        Assert.Equal(slope.HeightAtMm(landed.XMm, landed.ZMm), landed.YMm);
        Assert.True(landed.XMm - start.XMm > 20000, $"a glide off a 20 m slope should carry over 20 m, carried {landed.XMm - start.XMm}");
        Assert.True(ticks > 40);
    }

    [Fact]
    public void AFlare_StopsGoingForward_AndComesDownSlowly()
    {
        var start = new Body(1000, 3000, 5000, 90_000);
        var flare = new Posture(Stance.Standing, true, 0) { Wing = WingState.Flare, FeetMm = 3000 };
        var (after, _) = Kinematics.Step(start, flare, East, Winged, Flat(), Array.Empty<Blocker>(), 1000);
        Assert.Equal(start.XMm, after.XMm);
        Assert.Equal(2000, after.YMm);
    }
}
