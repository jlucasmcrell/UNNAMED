// UNNAMED Presentation - the three explainers a new player must be told, because Otherreach does not behave like the games they know
// Pure: no Godot, so the words are tested directly. The words are ONBOARDING_CONTENT.md §4 (DeepSeek's draft, PROPOSED); the Help screen shows them.

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>One explainer: an ID, a title and its paragraphs.</summary>
public sealed record Explainer(string Id, string Title, string[] Paragraphs);

public static class Explainers
{
    public static readonly Explainer[] All =
    {
        new("explainer.downed_death", "When you go down", new[]
        {
            "A fight does not end when your health does. You go down instead: on the ground, still alive, and still in the world. Anyone standing near - a companion, another player - can reach you and help you up.",
            "If nobody does, you die. That is the only way to die in Otherreach: unhelped.",
            "Death costs you XP debt, never XP already earned. The debt is paid out of what you earn next, so death slows you and never takes anything back. You return at your return point, which a Waystone you know can become.",
            "Nothing you are carrying is lost. Nothing is taken from another player. Death is expensive, slow and quiet, and it is the whole of the penalty.",
        }),
        new("explainer.no_pause", "Nothing stops", new[]
        {
            "Opening a sheet, a conversation or a menu does not stop the world. It was never going to: other people are in it.",
            "If you want to read something closely, go somewhere safe first and watch the road while you do it. The game will not do you the discourtesy of pretending that danger waited for you to close a window.",
            "This is also why the settings screen is worth setting up once. You will not get a quiet minute to look at it later.",
        }),
        new("explainer.living_world", "The world goes on without you", new[]
        {
            "Time passes whether or not you are watching. Log off for an hour and the road has an hour in it: prices moved, work finished, adventurers set out and came back, and somebody did something you would have wanted to see.",
            "You are not the only one moving. Residents have days. Adventurers have plans, drawn from the world's own seed, and they keep them. A town you emptied will not fill again because you left and returned; it fills because people come, which takes time that is not yours.",
            "When you return, the away sheet tells you what changed. Read it. It is the closest thing this world has to an apology for going on without you, and it is not sorry at all.",
            "This is also why nothing important is on a timer that waits for you. An Othertide does not hold. A seasonal event does not retime to when you arrived. If you want to see something, you have to be there for it.",
        }),
    };

    /// <summary>The longest an explainer's paragraph may be, so each reads on one screen of the Help sheet.</summary>
    public const int MaxParagraph = 420;
}
