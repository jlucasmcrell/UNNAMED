// UNNAMED Presentation - how a line of text becomes mouth shapes: one viseme per letter, a digraph as one sound.

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The letter-to-viseme mapping a spoken line uses when it has no recording to time it, kept apart from the face driver so it can be
/// read and tested on its own. A digraph is one shape ("th" is a TH and its H is silent, "sh" and "ch" are one), a doubled vowel is one
/// long vowel, and a word or sentence end closes the mouth instead of opening it.
/// </summary>
public static class Speech
{
    /// <summary>
    /// The viseme and jaw one letter of a line stands for, with the letter after it read where a digraph is one sound: "th" is a TH and
    /// its H is silent, "sh" and "ch" are one shape, "ng" is an N, and a doubled vowel is one long vowel. A space, a newline and a
    /// punctuation mark close the mouth instead of opening it.
    /// </summary>
    public static (string Shape, float Jaw) Shape(char before, char c, char next)
    {
        if (char.IsWhiteSpace(c) || c is '.' or ',' or '!' or '?' or '-' or ';' or ':' or '"' or '\'' or '\u2014')
            return ("viseme_sil", 0f);
        switch (c)
        {
            case 't' when next == 'h': return ("viseme_TH", 0.12f);
            case 's' when next == 'h': return ("viseme_CH", 0.12f);
            case 'c' when next == 'h': return ("viseme_CH", 0.12f);
            case 'n' when next == 'g': return ("viseme_nn", 0.15f);
            case 'h' when before is 't' or 's' or 'c': return ("viseme_sil", 0f);   // the second half of a digraph the letter before it said
            case 'h' when next == 'h': return ("viseme_sil", 0f);
            case 'a': return ("viseme_aa", next == 'a' ? 0.45f : 0.55f);
            case 'e': return ("viseme_E", next == 'e' ? 0.3f : 0.35f);
            case 'i' or 'y': return ("viseme_I", 0.25f);
            case 'o': return ("viseme_O", next == 'o' ? 0.4f : 0.45f);
            case 'u' or 'w': return ("viseme_U", 0.25f);
            case 'p' or 'b' or 'm': return ("viseme_PP", 0f);
            case 'f' or 'v': return ("viseme_FF", 0.08f);
            case 't' or 'd': return ("viseme_DD", 0.15f);
            case 'k' or 'g' or 'c' or 'q': return ("viseme_kk", 0.2f);
            case 'j': return ("viseme_CH", 0.12f);
            case 's' or 'z' or 'x': return ("viseme_SS", 0.08f);
            case 'n' or 'l': return ("viseme_nn", 0.15f);
            case 'r': return ("viseme_RR", 0.18f);
            case 'h': return ("viseme_aa", 0.2f);
            default: return ("viseme_sil", 0f);
        }
    }

}
