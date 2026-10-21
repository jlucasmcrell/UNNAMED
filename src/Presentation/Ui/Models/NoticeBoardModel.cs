// UNNAMED Presentation - the notice board's view model: the contracts posted on it, and who is out on their day (spec 15, finding 15)
// Godot-free: Presentation.Tests links this file and tests the words on their own (D-11)

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// A contract on the board: what it is, the facts of it in one line (what it pays, where, who posted it), and - when someone else has it -
/// who took it. The standing rules are the board's header's, said once.
/// </summary>
public sealed record NoticeContract(string Title, string Facts, string? Taken, bool Open);

/// <summary>Someone out of the inn on their day: who, what for, and when the inn expects them back.</summary>
public sealed record NoticeAway(string Name, string Doing);

/// <summary>
/// One board, as the owner asked for it (finding 15): the contracts and bounties the hamlet is paying for, and who is out - the unmade bed
/// and the empty seat, written down. Everything here is read from the world's own views and the quests it already holds; nothing is cached
/// and nothing is decided here.
/// </summary>
public static class NoticeBoardModel
{
    /// <summary>
    /// The board's contracts, open ones first, then by title. Each carries the facts a body standing at the board wants: the coin, the place,
    /// who posted it - and who has it, when an adventurer has beaten the character to it.
    /// </summary>
    public static ImmutableArray<NoticeContract> Contracts(IEnumerable<BountyView> bounties, EntityId player,
        Func<string, QuestDefinition?> quest, Func<string, string> name) =>
        bounties.OrderBy(b => b.Standing)
            .ThenBy(b => b.Title, StringComparer.Ordinal)
            .Select(b => Contract(b, player, quest, name))
            .ToImmutableArray();

    private static NoticeContract Contract(BountyView bounty, EntityId player, Func<string, QuestDefinition?> quest, Func<string, string> name)
    {
        var definition = quest(bounty.QuestId);
        var facts = new List<string>();
        if (definition is { } held && held.Rewards.OfType<CurrencyReward>().Sum(r => r.Amount) is var coin and > 0)
            facts.Add($"{coin} coin");
        if (Where(definition) is { } where)
            facts.Add($"at {name(where)}");
        if (bounty.GiverId is { } giver)
            facts.Add($"posted by {name(giver)}");
        string? taken = bounty.Standing switch
        {
            BountyStanding.Held => "Yours",
            BountyStanding.Claimed => bounty.ClaimantId == player.Value ? "Done, and paid" : $"Taken by {bounty.ClaimantName}",
            _ => null,
        };
        return new NoticeContract(bounty.Title, string.Join(" - ", facts), taken, bounty.Standing == BountyStanding.Open);
    }

    /// <summary>The place a contract is worked at: the first objective that names one, in the quest's own order.</summary>
    private static string? Where(QuestDefinition? definition) =>
        definition?.Order.Select(o => QuestRules.Target(o.Condition))
            .Where(t => t is { Kind: TargetKind.Location, Ref.Length: > 0 })
            .Select(t => t!.Ref)
            .FirstOrDefault();

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
