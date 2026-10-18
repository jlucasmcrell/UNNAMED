// UNNAMED Presentation - dialogue skip and fast-forward: what the skip key does in a conversation
// Pure: no Godot, so the rules are tested directly

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>
/// The skip key in a conversation. A tap silences a line being said and takes the way on when there is only one - a lone reply that decides
/// nothing. Held, it fast-forwards: after <see cref="HoldSeconds"/> it takes the lone reply again every <see cref="RepeatSeconds"/>. It never
/// chooses between replies and never takes a reply that does something (a quest, a hire, an exchange): those wait for the player.
/// </summary>
public sealed class DialogueSkipRules
{
    public const double HoldSeconds = 0.4;
    public const double RepeatSeconds = 0.18;

    private double _held, _since;

    /// <summary>What a frame of the key does.</summary>
    /// <param name="Hush">Silence the voice now: a tap, or the first frame a hold takes.</param>
    /// <param name="Advance">Take the lone reply now.</param>
    public readonly record struct Result(bool Hush, bool Advance);

    /// <param name="pressed">The key is down.</param>
    /// <param name="justPressed">It went down this frame.</param>
    /// <param name="replies">How many replies are on offer.</param>
    /// <param name="loneIsPlain">There is exactly one, and it does nothing but go on.</param>
    public Result Step(bool pressed, bool justPressed, double seconds, int replies, bool loneIsPlain)
    {
        if (!pressed)
        {
            _held = _since = 0;
            return default;
        }
        bool plain = replies == 1 && loneIsPlain;
        if (justPressed)
        {
            _held = _since = 0;
            return new Result(true, plain);
        }
        _held += seconds;
        if (_held < HoldSeconds)
            return default;
        _since += seconds;
        if (_since < RepeatSeconds)
            return default;
        _since = 0;
        return new Result(true, plain);
    }
}
