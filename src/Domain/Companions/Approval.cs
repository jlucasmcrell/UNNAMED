// UNNAMED Domain - how a party member regards the character: their values, how they react to deeds, whether they go along with what
// they are asked, and the party's own progression and gear (SYSTEMS_COMPLETENESS_PLAN §2.15, WP15b; EXT/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES
// :38-58, :74-92; DN9, DN10)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;

namespace UNNAMED.Domain.Companions;

/// <summary>
/// What a party member does when asked (canon :54-58): go along with it, go along with it after a moment's doubt, refuse it, or leave the
/// party. It is read from what they think of the character, dimension by dimension - never from one approval score.
/// </summary>
public enum Disposition
{
    Comply,
    Hesitate,
    Refuse,
    Leave,
}

/// <summary>One dimension past a bound: below <see cref="Below"/>, or above <see cref="Above"/> (exactly one is set).</summary>
public sealed record DimensionBound(string Dimension, int? Below, int? Above)
{
    public bool Holds(int value) => Below is { } below ? value < below : value > Above!.Value;
}

/// <summary>
/// The approval rules (<c>config.party</c>'s <c>approval</c>; WP15b, provisional): how loyalty grows with days on the road together and with
/// a wage paid, how it falls with a wage owed, and the bounds past which a member hesitates, refuses or leaves - any one bound past is
/// enough, and leaving is read first.
/// </summary>
public sealed record ApprovalRules(int LoyaltyPerDay, int LoyaltyWagePaid, int LoyaltyWageUnpaid, ImmutableArray<DimensionBound> Hesitate,
    ImmutableArray<DimensionBound> Refuse, ImmutableArray<DimensionBound> Leave)
{
    /// <summary>No growth, and no bound: everyone always complies. What a pack with no <c>approval</c> block has.</summary>
    public static readonly ApprovalRules None = new(0, 0, 0, ImmutableArray<DimensionBound>.Empty, ImmutableArray<DimensionBound>.Empty,
        ImmutableArray<DimensionBound>.Empty);

    public string? Problem() =>
        LoyaltyPerDay < 0 || LoyaltyWagePaid < 0 ? "loyalty_per_day and loyalty_wage_paid are never negative"
        : LoyaltyWageUnpaid > 0 ? "loyalty_wage_unpaid is never positive: an owed wage never makes anyone more loyal"
        : Hesitate.Concat(Refuse).Concat(Leave).FirstOrDefault(b => (b.Below is null) == (b.Above is null)) is { } odd
            ? $"a bound on {odd.Dimension} names exactly one of below and above"
        : null;
}

/// <summary>
/// One of a party member's values (WP15b): a deed of this kind, on one of these subjects (a creature, an item, a resource - resolved from
/// a content tag or named), optionally to one NPC, moves what they think of the character on some dimensions, and draws a remark.
/// </summary>
/// <param name="On">The deed that moves them.</param>
/// <param name="Subjects">The deed's subjects that count, sorted.</param>
/// <param name="NpcId">For a delivery: only one to this NPC counts. Null: any.</param>
/// <param name="Changes">The dimension changes, each 1 to 100 either way.</param>
/// <param name="Remark">What they say when it happens (said aloud, subtitled), or null.</param>
public sealed record ValueReaction(DeedKind On, ImmutableSortedSet<string> Subjects, string? NpcId, ImmutableSortedDictionary<string, int> Changes, string? Remark)
{
    public bool Matches(DeedKind kind, string subject, string? npcId) =>
        kind == On && Subjects.Contains(subject) && (NpcId is null || NpcId == npcId);

    public bool Equals(ValueReaction? other) =>
        other is not null && On == other.On && Subjects.SetEquals(other.Subjects) && NpcId == other.NpcId && Changes.SequenceEqual(other.Changes)
        && Remark == other.Remark;

    public override int GetHashCode() => HashCode.Combine(On, Subjects.Count, NpcId, Changes.Count, Remark);
}

/// <summary>The approval rules as pure functions.</summary>
public static class Approval
{
    /// <summary>Strong enough, either way, for the regard sentence to say it.</summary>
    public const int Marked = 40;

    /// <summary>What a member does when asked, from what they think of the character (<paramref name="value"/> reads one dimension).</summary>
    public static Disposition Of(Func<string, int> value, ApprovalRules rules) =>
        Past(rules.Leave, value) ? Disposition.Leave
        : Past(rules.Refuse, value) ? Disposition.Refuse
        : Past(rules.Hesitate, value) ? Disposition.Hesitate
        : Disposition.Comply;

    private static bool Past(ImmutableArray<DimensionBound> bounds, Func<string, int> value) => bounds.Any(b => b.Holds(value(b.Dimension)));

    /// <summary>
    /// "How Tavar regards you" (§2.15: no approval bar, a short sentence instead): what they feel strongly, the warm side first, then the
    /// cold side after a "but". With nothing marked either way, they have no strong view yet.
    /// </summary>
    public static string Regard(string name, Func<string, int> value)
    {
        var warm = new List<string>();
        var cold = new List<string>();
        void Read(string dimension, string high, string? low, bool highIsWarm = true)
        {
            int v = value(dimension);
            if (v >= Marked)
                (highIsWarm ? warm : cold).Add(high);
            else if (v <= -Marked && low is not null)
                (highIsWarm ? cold : warm).Add(low);
        }
        Read("trust", "trusts you", "does not trust you");
        Read("loyalty", "is loyal to you", "owes you nothing");
        Read("respect", "respects you", "thinks little of you");
        Read("affection", "is fond of you", "dislikes you");
        Read("fear", "is afraid of you", null, highIsWarm: false);
        Read("grudge", "holds a grudge against you", null, highIsWarm: false);
        if (warm.Count == 0 && cold.Count == 0)
            return $"{name} has no strong view of you yet.";
        string Join(List<string> parts) => parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        return warm.Count > 0 && cold.Count > 0 ? $"{name} {Join(warm)}, but {Join(cold)}."
            : $"{name} {Join(warm.Count > 0 ? warm : cold)}.";
    }

    /// <summary>
    /// A party member's health on their feet (WP15b, on the character's rules): their own, plus what their attributes add beyond an untrained
    /// body's - so a member who never spent a point stands as their content says.
    /// </summary>
    public static int MaxHealth(int profileHealth, CharacterProgression progression, ProgressionRules rules) =>
        (int)Math.Max(1, profileHealth + ProgressionEngine.Derive(progression, rules).HealthMax - ProgressionEngine.Derive(CharacterProgression.Empty, rules).HealthMax);
}

public static class ApprovalKeys
{
    public static string Key(Disposition disposition) => disposition switch
    {
        Disposition.Comply => "comply",
        Disposition.Hesitate => "hesitate",
        Disposition.Refuse => "refuse",
        Disposition.Leave => "leave",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };
}
