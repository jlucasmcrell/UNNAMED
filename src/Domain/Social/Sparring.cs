// UNNAMED Domain - NPC sparring at a settlement's training ground (GEN-07)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Social;

/// <summary>
/// A training ground (GEN-07): where a settlement's fighters drill, when they drill there, and the animation the drill is seen doing. The spot is
/// named from the ground the settlement already stands on - its barracks yard, or the cairnwold green - so a bout happens somewhere a player can
/// walk to rather than in a footnote.
/// </summary>
public sealed record SparSite(string Id, string SettlementId, string Name, long XMm, long ZMm, int FacingMdeg,
    int FromHour, int ToHour, int EveryDays, string AnimationId)
{
    /// <summary>Whether the ground is drilling on this day and hour: every so many days, from one hour to another.</summary>
    public bool Live(long day, int hour) => hour >= FromHour && hour < ToHour && EveryDays > 0 && day % EveryDays == 0;
}

/// <summary>One of the pair as a bout sees them: who, what they carry to it, and what stands between them and a blow.</summary>
public sealed record SparFighter(string Key, string Name, int Level, AttackProfile Attack, DefenseProfile Defense, CharacterProgression Progression);

/// <summary>
/// A bout fought out (GEN-07): the ground, the pair, every exchange the combat resolver gave them, the health each was left on, and who came off
/// the better of it. Nobody is taken below the floor, so a bout is never a death.
/// </summary>
public sealed record SparBout(SparSite Site, SparFighter First, SparFighter Second, long Tick, int Rounds,
    int FirstHealth, int SecondHealth, int FirstLanded, int SecondLanded)
{
    /// <summary>The one the bout took to their floor, or null when the exchanges ran out: whoever it is, they lost it.</summary>
    public string? Floored { get; init; }

    /// <summary>Who came off the better of it: whoever was not taken to their floor, or - when the exchanges ran out - whoever is
    /// left standing higher on their own pool. Two fighters of different builds are compared by the fraction of themselves they have
    /// left, never by raw points of health.</summary>
    public string WinnerKey => Floored == First.Key ? Second.Key
        : Floored == Second.Key ? First.Key
        : FirstFraction > SecondFraction ? First.Key
        : FirstFraction < SecondFraction ? Second.Key
        : string.CompareOrdinal(First.Key, Second.Key) <= 0 ? First.Key : Second.Key;

    public string LoserKey => WinnerKey == First.Key ? Second.Key : First.Key;

    private int FirstFraction => FirstHealth * 100 / Math.Max(1, First.Defense.MaxHealth);

    private int SecondFraction => SecondHealth * 100 / Math.Max(1, Second.Defense.MaxHealth);

    /// <summary>How close it was, in points of health: a bout decided on the last exchange says more about both of them.</summary>
    public int CloseBy => Math.Abs(FirstFraction - SecondFraction);
}

/// <summary>
/// Sparring (GEN-07): two fighters drill where their settlement trains, and the bout is the world's own fight seen at training weight - the same
/// <see cref="CombatRules"/> resolve every blow, the same level gap advantage, the same armour - stopped at a health floor so the loser walks away.
/// A bout leaves both better at what they practise, and it leaves them thinking more of each other: regard grows with the bout, and grows more
/// when it was close.
/// </summary>
public static class SparRules
{
    /// <summary>Of their health a fighter is stopped at: the bout ends here, and no blow is allowed below it.</summary>
    public const int HealthFloorPercent = 30;

    /// <summary>The most exchanges a bout runs, so a grind between two armoured fighters still ends.</summary>
    public const int ExchangeCap = 24;

    /// <summary>The skill a bout practises: the drill blade's own, which is what the settlements train with.</summary>
    public const string SkillId = "skill.one_hand_blade";

    /// <summary>A drill blade: training weight, so a bout teaches timing rather than breaking bones.</summary>
    public static AttackProfile DrillBlow => new("spar.drill", 2, 5, DamageTypes.All[0], 1_800, 8, 3, 10, 2);

    /// <summary>What a fighter of a level stands on in a bout: a drill pool that grows with what they have learned.</summary>
    public static int MaxHealth(int level) => 60 + 10 * Math.Max(1, level);

    /// <summary>The armour a drill gives nobody: a bout turns on timing and reach, and the shield is the defender's own blocking.</summary>
    public static DefenseProfile Defense(int level) => new(MaxHealth(level),
        Enum.GetValues<BodyRegion>().ToImmutableSortedDictionary(r => r, r => 0),
        ImmutableSortedDictionary.Create<string, double>(StringComparer.Ordinal), Blocking: false, Dodging: false);

    /// <summary>How much regard a bout leaves between the two: respect for the one who beat them, and for the one who made it hard.</summary>
    public static int RegardGain(SparBout bout, string key)
    {
        int respect = key == bout.WinnerKey ? 2 : 1;
        return bout.CloseBy <= 20 ? respect + 1 : respect;
    }

    /// <summary>
    /// Fight the bout out (GEN-07): each exchange both of them swing, and every blow is a real one through <see cref="CombatRules.Resolve"/> with
    /// the level gap between them - the same resolver the world's fights use, so a bout is not a second combat rule. Health stops at the floor.
    /// </summary>
    public static SparBout Fight(SparSite site, SparFighter first, SparFighter second, long tick, CombatConstants constants,
        Func<uint, double> random)
    {
        int firstFloor = Math.Max(1, first.Defense.MaxHealth * HealthFloorPercent / 100);
        int secondFloor = Math.Max(1, second.Defense.MaxHealth * HealthFloorPercent / 100);
        int firstHealth = first.Defense.MaxHealth, secondHealth = second.Defense.MaxHealth;
        int firstLanded = 0, secondLanded = 0, rounds = 0;
        string? floored = null;

        while (rounds < ExchangeCap && firstHealth > firstFloor && secondHealth > secondFloor)
        {
            rounds++;
            int firstBlow = Blow(first, second, constants, random);
            if (firstBlow > 0)
            {
                secondHealth = Math.Max(secondFloor, secondHealth - firstBlow);
                firstLanded++;
            }
            if (secondHealth <= secondFloor)
                break;
            int secondBlow = Blow(second, first, constants, random);
            if (secondBlow > 0)
            {
                firstHealth = Math.Max(firstFloor, firstHealth - secondBlow);
                secondLanded++;
            }
        }
        return new SparBout(site, first, second, tick, rounds, firstHealth, secondHealth, firstLanded, secondLanded) { Floored = floored };
    }

    /// <summary>One swing, resolved by the world's own strike resolver: the level gap between them, then the blow on the defence.</summary>
    private static int Blow(SparFighter attacker, SparFighter defender, CombatConstants constants, Func<uint, double> random)
    {
        var strike = CombatRules.LevelGap(new Strike(attacker.Attack, 1.0, constants.BaseCritPercent, 1.0), attacker.Level, defender.Level);
        return CombatRules.Resolve(strike, defender.Defense, constants, random).Final;
    }
}
