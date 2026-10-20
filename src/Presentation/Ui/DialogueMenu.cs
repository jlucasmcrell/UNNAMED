// UNNAMED Presentation - how a conversation's replies are grouped on screen (the owner's playtest: ten options at once)
// Presentation only: it orders and folds the replies the simulation offers; which are offered is still the content's conditions

using System.Collections.Immutable;
using UNNAMED.Domain.Social;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// A long list of replies folds to the few that matter: quest lines first, then trade, lessons and hires, one "Ask about..." entry holding
/// the topic questions, then the closing line. A topic already asked is not offered by the content, so it is not listed here either.
/// </summary>
public static class DialogueMenu
{
    public const string Ask = "\u0001ask";
    public const string Back = "\u0001back";

    /// <summary>At most this many replies show flat; more fold their topics into the Ask entry.</summary>
    public const int FlatMost = 6;

    private enum Group { Quest, Trade, Learn, Service, Topic, Close }

    private static Group Classify(DialogueChoice? choice)
    {
        if (choice is null)
            return Group.Topic;
        var consequences = choice.Consequences;
        if (consequences.Any(c => c is StartQuestConsequence or TransferItemConsequence)
            || choice.Conditions.Any(c => c is QuestStateCondition { Negated: false, State: "active" or "satisfied" }))
            return Group.Quest;
        if (consequences.Any(c => c is OpenServiceConsequence))
            return Group.Trade;
        if (consequences.Any(c => c is TeachConsequence))
            return Group.Learn;
        if (consequences.Any(c => c is RecruitCompanionConsequence or HireHirelingConsequence or DismissCompanionConsequence))
            return Group.Service;
        return choice.Next is null && consequences.IsEmpty ? Group.Close : Group.Topic;
    }

    /// <summary>The reply ids on the top level, in order; the Ask entry stands where the topics were folded away.</summary>
    public static ImmutableArray<string> Top(IReadOnlyList<(string Id, DialogueChoice? Choice)> replies)
    {
        if (replies.Count <= FlatMost)
            return replies.Select(r => r.Id).ToImmutableArray();
        var grouped = replies.Select(r => (r.Id, Group: Classify(r.Choice))).ToList();
        var top = grouped.Where(r => r.Group is not (Group.Topic or Group.Close)).OrderBy(r => r.Group).Select(r => r.Id).ToList();
        if (grouped.Any(r => r.Group == Group.Topic))
            top.Add(Ask);
        top.AddRange(grouped.Where(r => r.Group == Group.Close).Select(r => r.Id));
        return top.ToImmutableArray();
    }

    /// <summary>The topic ids behind the Ask entry, in the content's order, then Back.</summary>
    public static ImmutableArray<string> Topics(IReadOnlyList<(string Id, DialogueChoice? Choice)> replies) =>
        replies.Where(r => Classify(r.Choice) == Group.Topic).Select(r => r.Id).Append(Back).ToImmutableArray();
}
