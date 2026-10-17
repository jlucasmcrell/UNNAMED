// UNNAMED Presentation - the character sheet's words: the Call in a line, and the source label beside every known list's source line
// (UI_PROGRESSION_AUDIT items A and N; canon 26 §4.1)
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11)

using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Presentation.Ui;

/// <summary>The Call as the Character sheet says it: its name and tagline, its package in one line, and its contact.</summary>
public sealed record CallLine(string Name, string Tagline, string Package, string Contact);

public static class CharacterWords
{
    /// <summary>"Endurance +2 · Might +1 · Agility +1": a Call's attribute leaning, the largest first.</summary>
    public static string Leaning(CallDefinition call) =>
        string.Join(" · ", call.Package.AttributeBias.OrderByDescending(a => a.Value).ThenBy(a => ProgressionKeys.Key(a.Key), StringComparer.Ordinal)
            .Select(a => $"{Capital(ProgressionKeys.Key(a.Key))} +{a.Value}"));

    /// <summary>
    /// A Call's package in one line: what it leans toward and the two disciplines it starts. The sentence never says what the character
    /// "cannot" be: a Call grants nothing another character cannot reach (PROGRESSION.md §5).
    /// </summary>
    public static string Package(CallDefinition call, Func<string, string> name) =>
        $"Leans {Leaning(call)} · starts {string.Join(", ", call.Package.Skills.Select(s => $"{name(s.Key)} {s.Value}"))}";

    /// <summary>The Call line, or null for a character made before Calls existed or whose Call this content no longer defines.</summary>
    public static CallLine? Call(string? callId, IReadOnlyDictionary<string, CallDefinition> calls, Func<string, string> name) =>
        callId is not null && calls.TryGetValue(callId, out var call)
            ? new CallLine(name(call.Id), call.Tagline, Package(call, name), $"{name(call.Contact.NpcId)}, who already trusts you a little")
            : null;

    /// <summary>
    /// The nine-way source word (canon 26 §4.1) for how something came to be known. A starting package is upbringing and a Call, so Culture;
    /// a teacher, a text, a quest, study, a bench experiment and a find by doing are all Learned; a thing of an artefact is Gear.
    /// </summary>
    public static string SourceLabel(LearningSource source) => source switch
    {
        LearningSource.StartingPackage or LearningSource.Culture => "Culture",
        LearningSource.Artifact => "Gear",
        _ => "Learned",
    };

    /// <summary>Where something known was learned, in words: "taught by Kera Voss", "read in the Resonance Primer".</summary>
    public static string SourceLine(KnownTechnique learned, Func<string, string> name)
    {
        string? from = learned.SourceRef is { } source ? name(source) : null;
        return learned.Source switch
        {
            LearningSource.StartingPackage => "known from the start",
            LearningSource.Teacher => from is null ? "taught" : $"taught by {from}",
            LearningSource.Book => from is null ? "read" : $"read in {from}",
            LearningSource.Quest => from is null ? "from a quest" : $"from the quest {from}",
            LearningSource.Experiment => "worked out at the bench",
            LearningSource.Discovery => "found by doing",
            _ => ProgressionKeys.Key(learned.Source).Replace('_', ' ') + (from is null ? "" : $" ({from})"),
        };
    }

    /// <summary>The label and the source line together, as a known row shows them: "Learned: taught by Kera Voss".</summary>
    public static string Labelled(KnownTechnique learned, Func<string, string> name) => $"{SourceLabel(learned.Source)}: {SourceLine(learned, name)}";

    private static string Capital(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
