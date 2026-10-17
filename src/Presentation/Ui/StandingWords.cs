// UNNAMED Presentation - how a faction regards the character, as far as the character has been told (WP6; SYSTEMS_COMPLETENESS_PLAN §2.7)
// No Godot types: the words are tested on their own (Presentation.Tests links this file)

using System.Collections.Immutable;
using UNNAMED.Domain.Factions;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// One faction as the Reputation sheet tells it: its name, where it sits and who speaks for it, its regard in words (never points: standing
/// is report-only, and the character knows only what the faction's members have let them know), the tier's word for the chip, and what the
/// faction has heard of the character's acts.
/// </summary>
public sealed record FactionReport(string Id, string Name, string About, bool Known, string Regard, string Word, int Level, ImmutableArray<string> Heard);

/// <summary>
/// The faction views in words (PJ07: "a faction learns of an act only when you tell one of its members"). A faction that has heard nothing
/// of the character, and whose regard has never moved, says so; once it has, its regard is told as its members would put it. No number
/// reaches the sheet.
/// </summary>
public static class StandingWords
{
    /// <summary>The sheet's sentence while no faction knows the character at all.</summary>
    public const string NoneKnow = "A faction learns of an act only when you tell one of its members.";

    /// <summary>Every faction, in the views' order. <paramref name="name"/> turns an ID into its display name (presentation's <c>Names.Of</c>).</summary>
    public static ImmutableArray<FactionReport> Of(IReadOnlyList<FactionView> factions, IReadOnlyList<ActView> acts, Func<string, string> name,
        Func<string, string> place) =>
        factions.Select(f => Of(f, factions, acts, name, place)).ToImmutableArray();

    public static FactionReport Of(FactionView faction, IReadOnlyList<FactionView> all, IReadOnlyList<ActView> acts, Func<string, string> name,
        Func<string, string> place)
    {
        var heard = acts.SelectMany(a => a.Known.Where(k => k.Knower == faction.Id).Select(k => Heard(a, k, name))).ToImmutableArray();
        bool known = faction.Points != 0 || !heard.IsEmpty;
        return new FactionReport(faction.Id, Capitalised(faction.Name.Length > 0 ? faction.Name : name(faction.Id)), About(faction, all, name, place),
            known, known ? Regard(faction.Tier) : "They have heard nothing of you.", known ? Capitalised(faction.Tier) : "Not heard",
            known ? faction.Level : 0, heard);
    }

    /// <summary>A tier as the faction's members would say it, never as a number.</summary>
    public static string Regard(string tier) => tier switch
    {
        "exalted" => "They hold you above all others.",
        "allied" => "They count you an ally.",
        "honoured" => "They speak of you with honour.",
        "trusted" => "They trust you.",
        "accepted" => "They accept you.",
        "neutral" => "They know of you, and think nothing of you either way.",
        "wary" => "They are wary of you.",
        "disliked" => "They dislike you.",
        "despised" => "They despise you.",
        "outcast" => "They have cast you out.",
        "anathema" => "Your name is a curse among them.",
        _ => $"They hold you {Names.Words(tier)}.",
    };

    /// <summary>One thing a faction has heard: who told it, and of what.</summary>
    public static string Heard(ActView act, KnowledgeView known, Func<string, string> name)
    {
        string deed = Deed(act, name);
        if (known.Identity == Identities.Unidentified)
            return $"They have heard that someone {deed}, not knowing it was you.";
        return known.Source == KnowledgeSources.Reported && known.Via is { } via
            ? $"You told {name(via)} that you {deed}."
            : $"They know that you {deed}.";
    }

    /// <summary>An act in words: "killed a ridge wolf", or for an act without words of its own, what it was and what it touched.</summary>
    private static string Deed(ActView act, Func<string, string> name) => act.Kind switch
    {
        ActKinds.CreatureKilled => $"killed {Article(Lower(name(act.Subject)))}",
        ActKinds.SwitchSet => $"saw to {Lower(name(act.Subject))}",
        _ => $"{Names.Words(act.Kind)}: {Lower(name(act.Subject))}",
    };

    /// <summary>Seat, members and regards in words: "At the outpost · Sel Arien · cordial to the Wayhold".</summary>
    private static string About(FactionView faction, IReadOnlyList<FactionView> all, Func<string, string> name, Func<string, string> place)
    {
        var parts = new List<string> { "At " + place(faction.SeatLocationId) };
        if (faction.Members.Length > 0)
            parts.Add(string.Join(", ", faction.Members.Select(name)));
        parts.AddRange(faction.Relations.Select(r =>
            $"{r.Attitude} to {(all.FirstOrDefault(f => f.Id == r.FactionId) is { Name.Length: > 0 } other ? other.Name : name(r.FactionId))}"));
        return string.Join(" · ", parts);
    }

    private static string Article(string noun) => noun.Length == 0 || noun.StartsWith("the ", StringComparison.Ordinal) ? noun
        : ("aeiou".Contains(noun[0]) ? "an " : "a ") + noun;

    private static string Lower(string name) =>
        name.Length > 1 && char.IsUpper(name[0]) && char.IsLower(name[1]) && !name.Skip(1).Any(char.IsUpper) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
