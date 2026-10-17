// UNNAMED Presentation - what is in front of the character, in one line under the prompt; what interacting with a person does; and which
// things the world outlines (the gatherable affordance, W12; the owner's second play: "no indication when something can be gathered from")
// Godot-free: Presentation.Tests links this file and tests the rules on their own (D-11)

using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>What interacting with a person does: help them up, talk, see them in the party, or ask them about work or their trip.</summary>
public enum NpcApproachKind { HelpUp, Talk, Party, Hire }

/// <summary>An outline's kind: none, the thing the prompt is about, or a node with something to gather near the character.</summary>
public enum OutlineTone { None, Focus, Gatherable }

/// <summary>What the character knows of a person in front of them, for the approach and the context line.</summary>
public sealed record NpcFacts(string Name, bool HasConversation, bool DownedInParty, bool InParty, NpcHireView? Hire, bool Teaches, GroupPost? Trip);

public static class ContextModel
{
    /// <summary>
    /// What interacting with a person does: a downed party member is helped up; someone with a conversation is talked to (the hire and the
    /// Learn column sit beside it); a party member with none opens the Party screen; anyone else who hires on, or asks company for a
    /// trip, opens the hiring sheet. Someone with none of these is still talked to, and says they have nothing to say.
    /// </summary>
    public static NpcApproachKind Approach(NpcFacts npc) =>
        npc.DownedInParty ? NpcApproachKind.HelpUp
        : npc.HasConversation ? NpcApproachKind.Talk
        : npc.InParty ? NpcApproachKind.Party
        : npc.Hire is { HiresAs.IsEmpty: false } || npc.Trip is not null ? NpcApproachKind.Hire
        : NpcApproachKind.Talk;

    /// <summary>The prompt's words for a person, after the key: "Help Tavar up", "Talk to Kera", "Ask Bram about work".</summary>
    public static string Prompt(NpcFacts npc) => Approach(npc) switch
    {
        NpcApproachKind.HelpUp => $"Help {npc.Name} up",
        NpcApproachKind.Party => $"{npc.Name}: your party",
        NpcApproachKind.Hire => npc.Hire is { HiresAs.IsEmpty: false } ? $"Ask {npc.Name} about work" : $"Ask {npc.Name} about the trip",
        _ => $"Talk to {npc.Name}",
    };

    /// <summary>
    /// The context line under a person's prompt, or null when there is nothing to add: in the party; asking company for a trip (and when
    /// they leave); hireable now, or why not; teaching.
    /// </summary>
    public static string? Npc(NpcFacts npc)
    {
        var parts = new List<string>();
        if (npc.InParty)
            parts.Add("With you");
        if (npc.Trip is { } trip)
            parts.Add(trip.Joined ? $"Expects you at {trip.Meet} by {trip.Leaves}" : $"Asking for company: leaves {trip.Meet} at {trip.Leaves}");
        if (!npc.InParty && npc.Hire is { HiresAs.IsEmpty: false } hire)
            parts.Add(hire.Hireable ? $"Hires on as {string.Join(", ", hire.HiresAs.Distinct().Select(r => PartyModel.RoleWord(r).ToLowerInvariant()))}"
                : $"Not hiring now: {Reason(npc.Name, hire.Refusal)}");
        if (npc.Teaches)
            parts.Add("Teaches");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// The context line under a node's prompt: ready, what it gives and the skill it trains, with "untrained" when the character has none
    /// of it yet; spent, whether it grows back.
    /// </summary>
    public static string Node(NodeDefinition node, bool ready, int skillLevel, Func<string, string> name)
    {
        if (!ready)
            return node.Respawn == Respawn.None ? "Gone for good" : "Grows back in time";
        return $"Gives {name(node.ItemId)} · {name(node.SkillId)} {(skillLevel > 0 ? skillLevel.ToString() : "(untrained)")}";
    }

    /// <summary>The context line under a container's prompt: what it holds, in a count.</summary>
    public static string Container(int stacks) => stacks switch
    {
        0 => "Empty",
        1 => "Holds one thing",
        _ => $"Holds {stacks} things",
    };

    /// <summary>
    /// The context line under a thing on the ground: its weight, and what taking it up would do to the load - fine, laden (D4) or too heavy
    /// to take at all (the inventory's refusal at the limit).
    /// </summary>
    public static string Item(long weightGrams, long carriedGrams, long limitGrams, LadenRules rules)
    {
        long after = carriedGrams + weightGrams;
        string weight = LoadWords.Kg(weightGrams);
        return after > limitGrams ? $"{weight} · too heavy to take: you are carrying all you can"
            : LoadWords.Band(after, limitGrams, rules) == LoadBand.Light ? weight
            : $"{weight} · you would be laden";
    }

    /// <summary>The context line under a station's prompt: how many of the things made there the character knows.</summary>
    public static string Station(int known, int total) =>
        total == 0 ? "Nothing is made here yet" : known == 0 ? $"You know none of the {total} things made here" : $"You know {known} of the {total} things made here";

    /// <summary>
    /// The outline on the thing the prompt is about: a node with nothing to gather is not outlined (the prompt says why); anything else the
    /// prompt offers is.
    /// </summary>
    public static OutlineTone Focus(bool isNode, bool nodeReady) => isNode && !nodeReady ? OutlineTone.None : OutlineTone.Focus;

    /// <summary>
    /// The nodes outlined as gatherable around the character: those with something to take, within <paramref name="radiusMm"/>, nearest
    /// first, at most <paramref name="most"/>; the one the prompt is about is outlined as the focus instead.
    /// </summary>
    public static IReadOnlyList<string> Gatherable(IEnumerable<NodeView> nodes, long xMm, long zMm, long radiusMm, string? focusKey, int most) =>
        nodes.Where(n => n.Ready && n.Key != focusKey)
            .Select(n => (n.Key, Square: (double)(n.XMm - xMm) * (n.XMm - xMm) + (double)(n.ZMm - zMm) * (n.ZMm - zMm)))
            .Where(n => n.Square <= (double)radiusMm * radiusMm)
            .OrderBy(n => n.Square).ThenBy(n => n.Key, StringComparer.Ordinal)
            .Take(most)
            .Select(n => n.Key)
            .ToList();

    private static string Reason(string name, string? refusal)
    {
        string why = refusal ?? "busy";
        return why.StartsWith(name + " ", StringComparison.Ordinal) ? why[(name.Length + 1)..] : why;
    }
}
