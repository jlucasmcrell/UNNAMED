// UNNAMED Domain - a people's inherited aptitude: how fast and how far its members learn (RACES.md "Biology, aptitude and culture are
// separate"; RACE_ABILITIES_PROPOSAL.md §2, owner-approved 2026-10-24)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Progression;

/// <summary>From skill level <see cref="From"/> up, use teaches at <see cref="Percent"/> of the usual rate.</summary>
public sealed record RateBand(int From, int Percent);

/// <summary>
/// How a body learns (§13.1's "modify rate" and a ceiling): a rate by skill band, a percent per skill where the people leans toward or
/// away from it, the common ceiling its competence stops at, and the share of a first success's novelty bonus. Only ever a rate and a
/// ceiling: aptitude never grants skill. <see cref="Usual"/> is the rules' own, every character before peoples had traits.
/// </summary>
public sealed record LearningAptitude
{
    public static LearningAptitude Usual { get; } = new();

    /// <summary>Lowest first; the first starts at 0. Empty is 100% at every level.</summary>
    public ImmutableArray<RateBand> RateBands { get; init; } = ImmutableArray<RateBand>.Empty;

    /// <summary>Per skill ID: a leaning (above 100), a biological limit (below 100), or none (0, the skill cannot be learned at all).</summary>
    public ImmutableSortedDictionary<string, int> SkillPercent { get; init; } = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);

    /// <summary>Where competence stops short of a mastery (§4.3); null is the rules' common ceiling.</summary>
    public int? CommonCeiling { get; init; }

    /// <summary>Where a designated mastery stops (§4.3, M12); null is 100. Kept for when designations arrive.</summary>
    public int? MasteryCeiling { get; init; }

    public int NoveltyPercent { get; init; } = 100;

    /// <summary>The rate, in percent, use of <paramref name="skillId"/> teaches at from <paramref name="level"/>.</summary>
    public int RatePercent(string skillId, int level)
    {
        int band = RateBands.LastOrDefault(b => b.From <= level)?.Percent ?? 100;
        return band * SkillPercent.GetValueOrDefault(skillId, 100) / 100;
    }

    public int Ceiling(SkillModel model) => CommonCeiling ?? model.CommonCeiling;

    public string? Problem() =>
        !RateBands.IsEmpty && RateBands[0].From != 0 ? "the first rate band starts at skill 0"
        : RateBands.Zip(RateBands.Skip(1)).Any(p => p.Second.From <= p.First.From) ? "rate bands rise"
        : RateBands.Any(b => b.Percent <= 0) ? "a rate band's percent is positive"
        : SkillPercent.Values.Any(p => p < 0) ? "a skill percent is not negative"
        : CommonCeiling is < 1 or > 100 ? "the common ceiling is 1 to 100"
        : MasteryCeiling is { } mastery && (mastery > 100 || mastery < (CommonCeiling ?? 0)) ? "the mastery ceiling is at least the common ceiling and at most 100"
        : NoveltyPercent < 0 ? "the novelty percent is not negative"
        : null;
}
