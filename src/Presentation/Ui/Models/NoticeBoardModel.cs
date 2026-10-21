// UNNAMED Presentation - the notice board's view model: the contracts posted on it, and who is out on their day (spec 15, finding 15)
// Godot-free: Presentation.Tests links this file and tests the words on their own (D-11)

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Social;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>A contract on the board: what it is, and how it stands in the board's own words.</summary>
public sealed record NoticeContract(string Title, string Standing, bool Open);

/// <summary>Someone out of the inn on their day: who, what for, and when the inn expects them back.</summary>
public sealed record NoticeAway(string Name, string Doing);

/// <summary>
/// One board, as the owner asked for it (finding 15): the contracts and bounties the hamlet is paying for, and who is out - the unmade bed
/// and the empty seat, written down. Everything here is read from the world's own views; nothing is cached and nothing is decided here.
/// </summary>
public static class NoticeBoardModel
{
    /// <summary>
    /// The board's contracts, open ones first, then by title: what it is and how it stands (yours, taken by whom, done, or open). One taken
    /// by another adventurer stays up - that is how the town reads that it has been taken - and drops off only when the world posts it again.
    /// </summary>
    public static ImmutableArray<NoticeContract> Contracts(IEnumerable<BountyView> bounties, EntityId player) =>
        bounties.OrderBy(b => b.Standing)
            .ThenBy(b => b.Title, StringComparer.Ordinal)
            .Select(b => new NoticeContract(b.Title, Standing(b, player), b.Standing == BountyStanding.Open))
            .ToImmutableArray();

    /// <summary>How a contract stands, in the words the board uses.</summary>
    public static string Standing(BountyView bounty, EntityId player) => bounty.Standing switch
    {
        BountyStanding.Held => "Yours: finish it, and it is yours to be paid for",
        BountyStanding.Claimed => bounty.ClaimantId == player.Value ? "Done, and paid" : $"Taken by {bounty.ClaimantName}",
        _ => "Open: first to finish it is paid",
    };

    /// <summary>
    /// Who is out: everyone of the town's adventurers who is neither in the inn nor with the character, and what they went out for.
    /// <paramref name="place"/> words a place ID (a town on the road), so the board says where rather than an ID.
    /// </summary>
    public static ImmutableArray<NoticeAway> Away(IEnumerable<AdventurerView> adventurers, Func<string, string> place) =>
        adventurers.Where(a => a.Party is null && !a.InTown)
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => new NoticeAway(a.Name, Doing(a, place)))
            .ToImmutableArray();

    /// <summary>What one who is out went out for, in a line.</summary>
    private static string Doing(AdventurerView adventurer, Func<string, string> place) => adventurer.Goal switch
    {
        AdventurerGoals.Bounty => "out on a bounty, and not back till it is finished",
        AdventurerGoals.Travel => adventurer.GoalRef is { } to ? $"on the road to {place(to)}" : "on the road",
        _ => "out hunting, back before dark",
    };

    /// <summary>The line under the prompt: how much work is posted, and how many the inn is short of.</summary>
    public static string Hint(IReadOnlyCollection<NoticeContract> contracts, IReadOnlyCollection<NoticeAway> away)
    {
        int open = contracts.Count(c => c.Open);
        string work = open == 0 ? "nothing open" : open == 1 ? "1 contract open" : $"{open} contracts open";
        string outWord = away.Count == 0 ? "nobody out" : away.Count == 1 ? "1 out on their day" : $"{away.Count} out on their day";
        return $"{work} - {outWord}";
    }
}
