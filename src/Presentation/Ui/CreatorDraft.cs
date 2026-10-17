// UNNAMED Presentation - the character being made: what the creator has chosen so far, and the spec it makes (WP2's local part)
// Godot-free, so the tests compile it as the game does: no game state lives here, and nothing here is saved (D-11)

using UNNAMED.Domain.Character;

namespace UNNAMED.Presentation.Ui;

/// <summary>The creator's steps, in order (PJ04 §3.1 as the owner scoped it, D3: no Face step).</summary>
public enum CreatorStep { People, Body, Identity, Call, Confirm }

/// <summary>
/// What the creator holds while a character is made: every choice has a value from the first frame, so any step can be visited in any
/// order and nothing is lost going back (PJ04 §3.1). <see cref="Spec"/> is what Begin hands <c>GameSession.NewGame</c>; until the name
/// can be kept, <see cref="Problem"/> says why not and Begin is refused.
/// </summary>
public sealed record CreatorDraft(string Name, Pronouns Pronouns, string PeopleId, string BodyPresetId, string? CallId, StartPoint Start)
{
    /// <summary>The Call offered first and preselected for a first character (PJ05 §8.4): "A little of everything; a good first choice."</summary>
    public const string FirstCall = "call.wayfarer";

    /// <summary>A draft with nothing chosen yet but a name: the defaults every step starts from.</summary>
    public static CreatorDraft Fresh(string bodyPresetId, string? callId, StartPoint start) =>
        new(string.Empty, Pronouns.They, CharacterSpec.DefaultPeople, bodyPresetId, callId, start);

    /// <summary>Why this draft cannot begin, or null when it can: the name's rule, in the creator's words.</summary>
    public string? Problem => CharacterSpec.NameProblem(Name) is { } problem
        ? Name.Trim().Length == 0 ? "Give your character a name." : problem + "."
        : null;

    /// <summary>The name as it will be kept (trimmed, spaces collapsed).</summary>
    public string KeptName => CharacterSpec.Normalize(Name);

    /// <summary>How many characters the kept name has, against the limit, for the counter under the field.</summary>
    public string Count => $"{KeptName.Length} / {CharacterSpec.MaxNameLength}";

    /// <summary>The spec Begin starts the game with; throws as the domain does when the draft cannot begin.</summary>
    public CharacterSpec Spec() => new CharacterSpec(Name, Pronouns, PeopleId, BodyPresetId, CallId) { Start = Start }.Validated();

    /// <summary>A pronoun set in the creator's words: "he/him", "she/her", "they/them".</summary>
    public static string Words(Pronouns pronouns) => pronouns switch
    {
        Pronouns.He => "he/him",
        Pronouns.She => "she/her",
        _ => "they/them",
    };
}
