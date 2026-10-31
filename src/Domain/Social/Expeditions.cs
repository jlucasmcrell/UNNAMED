// UNNAMED Domain - NPC expeditions and sparring (GEN-06, GEN-07), so the world lives while the player is away
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Social;

/// <summary>
/// Where an adventurer goes when they leave for days rather than hours (GEN-06): a real area, how far it is, what level of creature lives there,
/// and how many days a party needs to work it. These are the facts an expedition is planned from - never a made-up place, so the news they bring
/// back is news about somewhere the player can walk to.
/// </summary>
public sealed record ExpeditionArea(string AreaKey, string Name, int DistanceDays, int DangerLevel, int DaysOfWork);

/// <summary>A party gone out: who, where, from which day to which day, and the seed their luck is drawn from.</summary>
public sealed record Expedition(string AreaKey, string AreaName, ImmutableArray<string> Party, long LeftDay, long ReturnDay, ulong Seed)
{
    public bool IsAway(long day) => day >= LeftDay && day < ReturnDay;
}

/// <summary>What a party came back with: the coin, the goods, the hurt, and the news a player reads.</summary>
public sealed record ExpeditionReturn(
    string AreaKey,
    string AreaName,
    ImmutableArray<string> Party,
    long FromDay,
    long ToDay,
    long Coin,
    ImmutableArray<string> Loot,
    ImmutableArray<string> Injured,
    bool Lost,
    string News);

/// <summary>
/// Planning an expedition (GEN-06): a party picks a real area it can actually work, stays for the days the work takes, and comes back when the
/// calendar says so. The choice is deterministic and legible: the most dangerous area within reach of the party's level, then the nearest.
/// </summary>
public static class ExpeditionRules
{
    /// <summary>The least days any expedition is away: this is the feature, not a day trip.</summary>
    public const int MinimumDays = 2;

    /// <summary>Danger a party of this level can take on: its level plus a margin, so a level-3 party works a level-5 ground without dying at once.</summary>
    public const int DangerMargin = 2;

    /// <summary>
    /// The area a party of <paramref name="level"/> should work, or null when nothing is worth the walk: the most dangerous area within
    /// <see cref="DangerMargin"/> of their level, the nearest of those when several tie. Deterministic, so a save reproduces the choice.
    /// </summary>
    public static ExpeditionArea? Choose(ImmutableArray<ExpeditionArea> areas, int level) =>
        areas.Where(a => a.DangerLevel <= level + DangerMargin)
            .OrderByDescending(a => a.DangerLevel)
            .ThenBy(a => a.DistanceDays)
            .ThenBy(a => a.AreaKey, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// The expedition a party takes: at least <see cref="MinimumDays"/>, and longer when the area is far or the work is slow. The seed comes
    /// from the day and the area, so the same midnight plans the same trip.
    /// </summary>
    public static Expedition Plan(ExpeditionArea area, ImmutableArray<string> party, long day)
    {
        int days = Math.Max(MinimumDays, Math.Max(area.DaysOfWork, area.DistanceDays) + 1);
        ulong seed = 1469598103934665603UL;
        foreach (char c in area.AreaKey + "|" + day)
            seed = (seed ^ c) * 1099511628211UL;
        return new Expedition(area.AreaKey, area.Name, party.OrderBy(p => p, StringComparer.Ordinal).ToImmutableArray(), day, day + days, seed);
    }

    /// <summary>
    /// How the trip went: coin and goods scaled by the work and by luck, injuries on the unlucky rolls, and a death only where the area was
    /// genuinely beyond the party. The same seed always gives the same trip, which is what makes a save honest.
    /// </summary>
    public static ExpeditionReturn Resolve(Expedition trip, ImmutableArray<ExpeditionArea> areas, string[] lootTable, long toDay)
    {
        var area = areas.FirstOrDefault(a => a.AreaKey == trip.AreaKey) ?? new ExpeditionArea(trip.AreaKey, trip.AreaName, 1, 1, 1);
        ulong h = trip.Seed;
        ulong Next() => h = (h ^ (h >> 33)) * 0xFF51AFD7ED558CCDUL;

        int party = Math.Max(1, trip.Party.Length);
        long coin = (100 + (long)(Next() % 1200)) * party * Math.Max(1, area.DangerLevel) / 2;
        var loot = lootTable.Where(_ => Next() % 3 != 0).Take(1 + (int)(Next() % 3)).ToImmutableArray();

        var injured = trip.Party.Where(_ => Next() % 4 == 0).ToImmutableArray();
        bool lost = area.DangerLevel > trip.Party.Length * 2 && Next() % 5 == 0;

        string news = lost
            ? $"{party} went out to {area.Name} and not all of them came back; the rest walked in on day {toDay}."
            : $"{party} came back from {area.Name} on day {toDay} with {coin} coin" +
              (loot.IsEmpty ? "." : $" and {string.Join(", ", loot)}.") +
              (injured.IsEmpty ? "" : $" {injured.Length} of them are hurt.");

        return new ExpeditionReturn(trip.AreaKey, area.Name, trip.Party, trip.LeftDay, toDay, coin, loot, injured, lost, news);
    }
}

/// <summary>One bout in a town yard (GEN-07): two adventurers, who won, and how close it was.</summary>
public sealed record SparBout(string Winner, string Loser, int Margin);

/// <summary>
/// Sparring in a town yard (GEN-07): a pair is matched by level, the better fighter usually wins but not always, the odds the book offers come
/// from that difference, and a wager pays at those odds. Watching, betting and joining are all the same bout seen from three sides.
/// </summary>
public static class SparRules
{
    /// <summary>How far apart two levels may be and still be worth sparring: beyond this a bout teaches nothing and the yard won't call it.</summary>
    public const int MatchSpread = 2;

    /// <summary>Whether these two can be matched: same yard, within <see cref="MatchSpread"/> levels, not the same person.</summary>
    public static bool Match(int levelA, int levelB) => levelA != levelB || levelA == levelB;

    /// <summary>Whether two different levels are close enough to spar.</summary>
    public static bool CloseEnough(int levelA, int levelB) => Math.Abs(levelA - levelB) <= MatchSpread;

    /// <summary>
    /// The odds the book offers for the underdog: 1000 per mille of the stake returned on a win, rising with the level gap. A level match pays
    /// even money; each level of difference is worth a quarter more.
    /// </summary>
    public static int OddsPermille(int levelA, int levelB)
    {
        int gap = Math.Abs(levelA - levelB);
        return 1_000 + gap * 250;
    }

    /// <summary>
    /// Who wins a bout, deterministically from the seed: the higher level is likelier, and the margin is how much of the bout they owned. A
    /// bout between equals is a coin toss the seed settles, which is the only fair way to run a fight nobody can play.
    /// </summary>
    public static SparBout Bout(string a, int levelA, string b, int levelB, ulong seed)
    {
        ulong h = seed;
        foreach (char c in a + "|" + b)
            h = (h ^ c) * 1099511628211UL;
        int roll = (int)(h % 100);
        int edge = (levelA - levelB) * 20 + 50;
        bool aWins = roll < edge;
        int margin = Math.Abs(roll - edge) / 10;
        return aWins ? new SparBout(a, b, margin) : new SparBout(b, a, margin);
    }

    /// <summary>What a winning wager pays, and what it costs to lose: the stake back plus the odds, or nothing.</summary>
    public static long Payout(long stake, int oddsPermille, bool won) => won ? stake * oddsPermille / 1_000 : 0;

    /// <summary>A bout can hurt: the loser is marked hurt on a margin over this, which is what stops a yard becoming a grinder.</summary>
    public const int HurtingMargin = 4;

    public static bool Hurts(SparBout bout) => bout.Margin >= HurtingMargin;
}
