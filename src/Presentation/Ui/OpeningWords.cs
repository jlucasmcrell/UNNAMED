// UNNAMED Presentation - the opening's words and their timing: one line of canon a shot, and the score's hit on the name
// No Godot or domain types: the words are tested on their own (Presentation.Tests links this file)

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// One line on screen, between two moments of the opening (seconds from its first frame). <see cref="Voice"/> names the recording a
/// narrator would say it with (vo/&lt;id&gt;.ogg under the asset workspace): said when one exists, timed to the words, and the words stand
/// alone when none does.
/// </summary>
public sealed record OpeningLine(double From, double Until, string Words, string Voice);

public static class OpeningWords
{
    /// <summary>The score's hit (mus.opening is cut so that its hit falls here): the name comes in over the start screen's own view.</summary>
    public const double Hit = 62;

    /// <summary>The name has settled into the title's place and the title's entries come in: the opening is over.</summary>
    public const double End = 67;

    /// <summary>
    /// Every line as the game's own pages have it (OPENING_AND_MUSIC_PROPOSAL.md option (a), MYTHOLOGY.md, content/lore), one a shot: the
    /// road in's journal entry under the night sky, Tavar's ruin at the Foldscar, Old Late's crown, Old Corra's line in the lodge book under
    /// the Needle, the waystone, and the Silence over the road east.
    /// </summary>
    public static readonly OpeningLine[] Lines =
    {
        new(2.2, 8.6, "You woke beneath a changing sky.", "opening.01"),
        new(12.0, 19.8, "Here the frontier stops behaving like a frontier.", "opening.02"),
        new(23.4, 29.8, "Its crown answers the wind - late, a breath behind the gust.", "opening.03"),
        new(32.6, 39.6, "The top hums. Leave it to hum.", "opening.04"),
        new(42.6, 48.8, "The stone answers a traveller's hand.", "opening.05"),
        new(51.4, 59.0, "Every people has a story about what came before. No two agree.", "opening.06"),
    };
}
