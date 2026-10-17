// UNNAMED Domain - the party: who travels with the character and on what terms, how they fight, and what a hireling is paid
// (SYSTEMS_COMPLETENESS_PLAN §2.15, WP15a; EXT/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES; DN9, DN11)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Companions;

/// <summary>
/// Why someone is in the party (canon :6-10): a companion chose to travel with the character, a hireling works under contract, a
/// temporary ally shares a goal. Someone dismissed is not in the party at all: they live their own life.
/// </summary>
public enum PartyMembership
{
    Companion,
    Hireling,
    TemporaryAlly,
}

/// <summary>How readily a party member fights (§2.15): aggressive fights what hunts the character, defensive only what comes close, passive never.</summary>
public enum CompanionStance
{
    Aggressive,
    Defensive,
    Passive,
}

/// <summary>What a hireling is hired to do (§2.15): fight, carry, gather at a node, or work a station.</summary>
public enum HirelingRole
{
    Guard,
    Porter,
    Gatherer,
    Crafter,
}

/// <summary>
/// A party member's standing rules (§2.15's three for this bar): don't chase past the leash, go first for what threatens an injured
/// ally, and keep healing items for allies below 30%. The last has nothing to act on until party members carry items (WP15b); it is
/// kept, and saved, so the rule a player set is there when they do.
/// </summary>
public sealed record PartyTactics(bool KeepToLeash, bool ProtectInjured, bool HoldHealing)
{
    /// <summary>What a newcomer follows: the leash kept, nothing else asked of them - the companion's behaviour as built.</summary>
    public static readonly PartyTactics Default = new(true, false, false);

    /// <summary>An ally below this share of their health is injured, for <see cref="ProtectInjured"/> and <see cref="HoldHealing"/>.</summary>
    public const int InjuredPercent = 30;
}

/// <summary>What makes an NPC hireable (§2.15): the roles they take on, and the body they work and fight with.</summary>
public sealed record HirelingProfile(ImmutableArray<HirelingRole> Roles, CompanionProfile Body);

/// <summary>One role's terms (<c>config.party</c>; DN11: wages are content numbers per role).</summary>
/// <param name="DailyWage">Coin a day, paid in advance at each midnight from the character's purse into the hireling's.</param>
/// <param name="CarryBonusGrams">A porter on their feet and following adds this to what the character can carry.</param>
/// <param name="YieldsPerDay">A gatherer takes this many harvests' worth from their node each day.</param>
/// <param name="CraftsPerDay">A crafter works their recipe at most this many times a day, from what they were supplied.</param>
/// <param name="Stance">The stance a hireling of this role starts in.</param>
public sealed record RoleTerms(long DailyWage, long CarryBonusGrams, int YieldsPerDay, int CraftsPerDay, CompanionStance Stance);

/// <summary>
/// The party's rules (<c>config.party</c>; DN9, DN11 - provisional, so content rather than code): how many may travel with the
/// character, how long a hireling waits for unpaid wages, how long a gravely wounded companion is away recovering, the shortest and
/// longest contract, and each role's terms.
/// </summary>
public sealed record PartyRules(int MaxMembers, int GraceDays, int GraveWoundDaysMin, int GraveWoundDaysMax, int ContractDaysMin, int ContractDaysMax,
    ImmutableSortedDictionary<HirelingRole, RoleTerms> Roles)
{
    /// <summary>The loot rule this bar builds (§2.15: wage-only by default): what the party finds is the character's.</summary>
    public const string WageOnly = "wage_only";

    /// <summary>A problem with the numbers, or null.</summary>
    public string? Problem() =>
        MaxMembers < 1 ? "max_members is at least 1"
        : GraceDays < 0 ? "grace_days is never negative"
        : GraveWoundDaysMin < 1 || GraveWoundDaysMax < GraveWoundDaysMin ? "the grave wound lasts at least a day, and its longest is at least its shortest"
        : ContractDaysMin < 1 || ContractDaysMax < ContractDaysMin ? "a contract lasts at least a day, and its longest is at least its shortest"
        : Enum.GetValues<HirelingRole>().Where(r => !Roles.ContainsKey(r)).Select(PartyKeys.Key).FirstOrDefault() is { } missing
            ? $"roles names every role; {missing} is missing"
        : Roles.Values.Any(t => t.DailyWage < 0 || t.CarryBonusGrams < 0 || t.YieldsPerDay < 0 || t.CraftsPerDay < 0)
            ? "a role's wage, carry bonus, yields and crafts are never negative"
        : null;
}

/// <summary>
/// A hireling's contract (§2.15): the role, how many days, the daily wage and the loot rule agreed at hire - and how it stands now: the
/// days served, unpaid days, what they have earned, and the work they were set to.
/// </summary>
public sealed record HirelingContract(HirelingRole Role, int Days, long DailyWage, string LootRule)
{
    /// <summary>Midnights served since the hire.</summary>
    public int DaysServed { get; init; }

    /// <summary>Midnights in a row their wage could not be paid; past the grace, the contract lapses.</summary>
    public int UnpaidDays { get; init; }

    /// <summary>The coin paid them so far: theirs, to spend in the economy (WP14; until it lands, it is only held).</summary>
    public long Purse { get; init; }

    /// <summary>A gatherer's node key, or a crafter's station key: where they work. Null: no work set; they go with the party.</summary>
    public string? WorkSite { get; init; }

    /// <summary>A crafter's recipe.</summary>
    public string? RecipeId { get; init; }

    /// <summary>A crafter's materials, as the character handed them over: item definition and count, sorted.</summary>
    public ImmutableSortedDictionary<string, int> Supply { get; init; } = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);

    public int DaysLeft => Math.Max(0, Days - DaysServed);

    public bool Equals(HirelingContract? other) =>
        other is not null && Role == other.Role && Days == other.Days && DailyWage == other.DailyWage && LootRule == other.LootRule
        && DaysServed == other.DaysServed && UnpaidDays == other.UnpaidDays && Purse == other.Purse && WorkSite == other.WorkSite
        && RecipeId == other.RecipeId && Supply.SequenceEqual(other.Supply);

    public override int GetHashCode() => HashCode.Combine(Role, Days, DailyWage, DaysServed, Purse, WorkSite);
}

/// <summary>Pure party rules.</summary>
public static class PartyRulesOf
{
    /// <summary>
    /// One midnight of a contract: a day more served; then, with days left, the next day's wage - paid when the purse holds it, owed
    /// otherwise. What comes of it: <c>ended</c> (served in full), <c>lapsed</c> (unpaid past the grace), or null (the contract goes on).
    /// </summary>
    public static (HirelingContract Contract, long Paid, string? Ends) Midnight(HirelingContract contract, long purse, int graceDays)
    {
        var served = contract with { DaysServed = contract.DaysServed + 1 };
        if (served.DaysLeft == 0)
            return (served, 0, "ended");
        if (purse >= served.DailyWage)
            return (served with { UnpaidDays = 0, Purse = served.Purse + served.DailyWage }, served.DailyWage, null);
        var owed = served with { UnpaidDays = served.UnpaidDays + 1 };
        return (owed, 0, owed.UnpaidDays > graceDays ? "lapsed" : null);
    }

    /// <summary>Whether a member below <paramref name="health"/> of <paramref name="maxHealth"/> counts as injured.</summary>
    public static bool Injured(int health, int maxHealth) => maxHealth > 0 && health * 100 < maxHealth * PartyTactics.InjuredPercent;
}

/// <summary>Saved and displayed names are stable snake_case keys, never enum ordinals.</summary>
public static class PartyKeys
{
    public static string Key(PartyMembership membership) => membership switch
    {
        PartyMembership.Companion => "companion",
        PartyMembership.Hireling => "hireling",
        PartyMembership.TemporaryAlly => "temporary_ally",
        _ => throw new ArgumentOutOfRangeException(nameof(membership)),
    };

    public static string Key(CompanionStance stance) => stance switch
    {
        CompanionStance.Aggressive => "aggressive",
        CompanionStance.Defensive => "defensive",
        CompanionStance.Passive => "passive",
        _ => throw new ArgumentOutOfRangeException(nameof(stance)),
    };

    public static string Key(HirelingRole role) => role switch
    {
        HirelingRole.Guard => "guard",
        HirelingRole.Porter => "porter",
        HirelingRole.Gatherer => "gatherer",
        HirelingRole.Crafter => "crafter",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static PartyMembership? ParseMembership(string key) =>
        Enum.GetValues<PartyMembership>().Cast<PartyMembership?>().FirstOrDefault(m => Key(m!.Value) == key);

    public static CompanionStance? ParseStance(string key) =>
        Enum.GetValues<CompanionStance>().Cast<CompanionStance?>().FirstOrDefault(s => Key(s!.Value) == key);

    public static HirelingRole? ParseRole(string key) =>
        Enum.GetValues<HirelingRole>().Cast<HirelingRole?>().FirstOrDefault(r => Key(r!.Value) == key);
}
