using System.Text.Json;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Content.Tests;

/// <summary>
/// ROADMAP M2c exit criteria (a) and (b), against the shipped config: the XP/hour of four scripted activity
/// profiles stays inside AG-6's band, and a six-hour farm at one spawn cluster saturates to 0.10-0.30x while
/// every other currency is untouched. With <c>UNNAMED_WRITE_PROGRESSION_REPORT=&lt;path&gt;</c> the same run writes
/// the M2c telemetry report (per-axis advancement rates, the axis correlation matrix of PROGRESSION.md §13.2).
/// </summary>
/// <remarks>
/// The per-event XP values below are the Novice-tier reference awards: no creature, quest or recipe content
/// carries XP values yet. They are the targets Phase-1 content is authored against, and these profiles become
/// content-driven when that content exists (M3b-M5).
/// </remarks>
public class ProgressionBalanceTests
{
    private const long TicksPerHour = 20 * 60 * 60;

    private static readonly ProgressionRules Rules =
        ProgressionContent.BuildRules(LoadGameContent());

    private static ContentLoader LoadGameContent()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    // Novice-tier reference awards (see remarks).
    private const long NewCell = 40, NewLocation = 150, QuestObjective = 300, FirstCraft = 120, SocialMilestone = 150;
    private static long KillXp(int creatureLevel) => 25L * creatureLevel;

    /// <summary>One scripted hour of an activity, as timed events. Every profile needs at most three source kinds (§3.3).</summary>
    private sealed record Profile(string Name, Func<int, long, IEnumerable<(long Offset, XpAward Award)>> Hour);

    private static readonly Profile[] Profiles =
    {
        new("explorer", (hour, start) =>
            Every(30, i => new XpAward(XpSource.Discovery, NewCell, 0))
            .Concat(Every(6, i => new XpAward(XpSource.Discovery, NewLocation, 0)))
            .Concat(Every(4, i => new XpAward(XpSource.QuestObjective, QuestObjective, 0)))
            .Concat(Every(10, i => Kill($"creature.explorer.species_{i % 5}", 3, $"cluster.explorer.{hour}.{i}")))),
        new("quester", (hour, start) =>
            Every(8, i => new XpAward(XpSource.QuestObjective, QuestObjective, 0))
            .Concat(Every(15, i => new XpAward(XpSource.Discovery, NewCell, 0)))
            .Concat(Every(4, i => new XpAward(XpSource.Social, SocialMilestone, 0)))),
        new("fighter", (hour, start) =>
            Every(44, i => Kill($"creature.fighter.species_{i % 4}", 3 + i % 3, $"cluster.fighter.{hour}.{i % 6}"))
            .Concat(Every(3, i => new XpAward(XpSource.QuestObjective, QuestObjective, 0)))),
        new("crafter", (hour, start) =>
            Every(6, i => new XpAward(XpSource.Production, FirstCraft, 0) { FirstKey = $"item.crafted.h{hour}_{i}" })
            .Concat(Every(6, i => new XpAward(XpSource.QuestObjective, QuestObjective, 0)))
            .Concat(Every(20, i => new XpAward(XpSource.Discovery, NewCell, 0)))),
    };

    private static XpAward Kill(string species, int level, string cluster) =>
        new(XpSource.Combat, KillXp(level), 0) { Kill = new KillContext(species, level, cluster) };

    /// <summary><paramref name="count"/> events spread evenly across one hour, in order.</summary>
    private static IEnumerable<(long, XpAward)> Every(int count, Func<int, XpAward> award) =>
        Enumerable.Range(0, count).Select(i => ((long)(i * TicksPerHour / count), award(i)));

    private sealed record Run(double XpPerHour, CharacterProgression Progression, List<Advancement> Advancements);

    /// <summary>Plays a profile for some hours at level 3 (a Novice-tier character) and measures XP per hour.</summary>
    private static Run Play(Profile profile, int hours, ProgressionRules rules)
    {
        var p = CharacterProgression.Empty with { Level = 3 };
        var advancements = new List<Advancement>();
        long total = 0;
        for (int hour = 0; hour < hours; hour++)
        {
            long start = hour * TicksPerHour;
            foreach (var (offset, award) in profile.Hour(hour, start).OrderBy(e => e.Offset))
            {
                var result = ProgressionEngine.Award(p, award with { Tick = start + offset }, rules);
                total += result.Awarded;
                advancements.AddRange(result.Advancements);
                p = result.Progression with { Level = 3, LevelProgressXp = 0 };   // hold the tier: rates, not levelling, are measured
            }
        }
        return new Run((double)total / hours, p, advancements);
    }

    public static TheoryData<string> ProfileNames()
    {
        var data = new TheoryData<string>();
        foreach (var profile in Profiles)
            data.Add(profile.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(ProfileNames))]
    public void EveryActivityProfile_StaysInsideTheAg6Band(string name)
    {
        // Exit (a): 0.6x-1.4x of the Novice tier's target rate (levels 1-10 in 4-6 hours of mixed play).
        var profile = Profiles.Single(p => p.Name == name);
        double target = Rules.TierTargets[0].TargetRatePerHour(Rules.Curve);

        var run = Play(profile, hours: 5, Rules);

        double ratio = run.XpPerHour / target;
        Assert.True(ratio is >= 0.6 and <= 1.4, $"{name}: {run.XpPerHour:F0} XP/h is {ratio:F2}x the {target:F0} XP/h tier target");
        Assert.True(run.Progression.LifetimeXp.Count <= 3, $"{name} needs {run.Progression.LifetimeXp.Count} source kinds; at most 3 may be required (§3.3)");
    }

    [Fact]
    public void ASixHourFarmAtOneCluster_Saturates_WhileEveryOtherCurrencyIsUntouched()
    {
        // Exit (b): one wolf every simulated minute at one spawn cluster, an even fight, for six hours.
        const int KillsPerHour = 60;
        var farmed = CharacterProgression.Empty with { Level = 3 };
        var unguarded = farmed;
        var noGuards = Rules with
        {
            Guards = Rules.Guards with { LevelBand = [new LevelBandRow(-999, 999, 1.0)], SpeciesDecay = 1.0, ClusterThreshold = int.MaxValue },
        };
        long saturatedXp = 0, baselineXp = 0;
        for (int k = 0; k < 6 * KillsPerHour; k++)
        {
            var award = new XpAward(XpSource.Combat, KillXp(3), k * TicksPerHour / KillsPerHour)
            {
                Kill = new KillContext("creature.beast.wolf_grey", 3, "pop.r_0_0.c_00_02.wolves"),
            };
            var guarded = ProgressionEngine.Award(farmed, award, Rules);
            var free = ProgressionEngine.Award(unguarded, award, noGuards);
            if (k >= KillsPerHour / 2)   // after the first half hour: saturation has set in
            {
                saturatedXp += guarded.Awarded;
                baselineXp += free.Awarded;
            }

            // AG-4: the guards touched level XP and their own memory, nothing else.
            Assert.Equal(farmed.Skills, guarded.Progression.Skills);
            Assert.Equal(farmed.Known, guarded.Progression.Known);
            Assert.Equal(farmed.Allocation, guarded.Progression.Allocation);
            Assert.Equal(farmed.Pools, guarded.Progression.Pools);

            // The weapon-skill practice of each fight is identical with and without the guards.
            var practice = new SkillPractice("skill.one_hand_blade", 20, PracticeOutcome.Success, award.Tick);
            farmed = ProgressionEngine.Practice(guarded.Progression with { Level = 3, LevelProgressXp = 0 }, practice, Rules).Progression;
            unguarded = ProgressionEngine.Practice(free.Progression with { Level = 3, LevelProgressXp = 0 }, practice, noGuards).Progression;
            Assert.Equal(unguarded.Skills["skill.one_hand_blade"], farmed.Skills["skill.one_hand_blade"]);
        }

        double ratio = (double)saturatedXp / baselineXp;
        Assert.True(ratio is >= 0.10 and < 0.30, $"after saturation the farm yields {ratio:F3}x the unguarded XP");
    }

    [Fact]
    public void TheTelemetryReport_RecordsPerAxisRates_AndNoPairMovesTogether()
    {
        // PROGRESSION.md §13.2: per-axis advancement events, their rates, and the correlation matrix. A varied
        // session - every profile an hour, with practice and learning between - must show no pair above r = 0.8,
        // except level and attributes: attribute points are the allocation of level-ups by design (§2).
        var session = VariedSession(hours: 12);
        var windows = session
            .GroupBy(a => a.Tick / (TicksPerHour / 6))   // ten-minute windows
            .OrderBy(g => g.Key)
            .Select(g => Enum.GetValues<Axis>().ToDictionary(axis => axis, axis => (double)g.Where(a => a.Axis == axis).Sum(a => a.Amount)))
            .ToList();
        var axes = Enum.GetValues<Axis>();
        var correlations = new Dictionary<string, double>();
        foreach (var a in axes)
            foreach (var b in axes.Where(b => b > a))
                correlations[$"{a}/{b}"] = Pearson(windows.Select(w => w[a]).ToArray(), windows.Select(w => w[b]).ToArray());

        Assert.All(correlations.Where(kv => kv.Key != "Level/Attributes"),
            kv => Assert.True(kv.Value <= 0.8, $"{kv.Key} correlate at r = {kv.Value:F2}"));
        Assert.True(session.Any(a => a.Axis == Axis.Attributes), "the session never levelled, so it proves nothing about attributes");

        if (Environment.GetEnvironmentVariable("UNNAMED_WRITE_PROGRESSION_REPORT") is { Length: > 0 } path)
        {
            double target = Rules.TierTargets[0].TargetRatePerHour(Rules.Curve);
            var report = new
            {
                milestone = "M2c",
                config = "content/config (config.progression, config.xp_curve, config.level_cap, config.time)",
                novice_target_xp_per_hour = Math.Round(target),
                profiles = Profiles.Select(p =>
                {
                    var run = Play(p, 5, Rules);
                    return new
                    {
                        profile = p.Name,
                        xp_per_hour = Math.Round(run.XpPerHour),
                        band_ratio = Math.Round(run.XpPerHour / target, 3),
                        source_kinds = run.Progression.LifetimeXp.Keys.Select(ProgressionKeys.Key).ToArray(),
                    };
                }).ToArray(),
                varied_session_hours = 12,
                per_axis_per_hour = axes.ToDictionary(
                    axis => axis.ToString(), axis => Math.Round(session.Where(a => a.Axis == axis).Sum(a => a.Amount) / 12.0, 1)),
                correlation_10_minute_windows = correlations.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 3)),
                cross_axis_conversion = "none: each axis advances only through its own currency type (NonConversionTests)",
            };
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>Every profile for an hour in turn, with skill practice and learning events woven through. The character levels freely.</summary>
    private static List<Advancement> VariedSession(int hours)
    {
        var p = CharacterProgression.Empty;
        var events = new List<Advancement>();
        string[] skills = { "skill.athletics", "skill.survival", "skill.one_hand_blade" };
        for (int hour = 0; hour < hours; hour++)
        {
            var profile = Profiles[hour % Profiles.Length];
            long start = hour * TicksPerHour;
            foreach (var (offset, award) in profile.Hour(hour, start).OrderBy(e => e.Offset))
            {
                var result = ProgressionEngine.Award(p, award with { Tick = start + offset }, Rules);
                events.AddRange(result.Advancements);
                p = result.Progression;
            }
            // Practice sessions in some hours but not others, on a different rhythm from the profiles.
            if (hour % 3 != 1)
            {
                string skill = skills[hour % skills.Length];
                for (int i = 0; i < 12; i++)
                {
                    var result = ProgressionEngine.Practice(p,
                        new SkillPractice(skill, ProgressionEngine.SkillLevel(p, skill) + 10, PracticeOutcome.Success, start + i * TicksPerHour / 12), Rules);
                    events.AddRange(result.Advancements);
                    p = result.Progression;
                }
            }
            // A lesson every few hours.
            if (hour % 4 == 2)
            {
                var result = ProgressionEngine.Learn(p, new TechniqueLearning($"ability.test.lesson_{hour}", LearningSource.Teacher, start + TicksPerHour / 2));
                events.AddRange(result.Advancements);
                p = result.Progression;
            }
        }
        return events;
    }

    private static double Pearson(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average();
        double cov = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
        double sx = Math.Sqrt(x.Sum(a => (a - mx) * (a - mx))), sy = Math.Sqrt(y.Sum(b => (b - my) * (b - my)));
        return sx == 0 || sy == 0 ? 0 : cov / (sx * sy);
    }
}
