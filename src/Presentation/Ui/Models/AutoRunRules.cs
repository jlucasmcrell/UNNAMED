// UNNAMED Presentation - auto-run: when the toggle starts the character running forward and what stops it (the owner, 2026-10-18)
// Pure: no Godot, so the rules are tested directly

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>What auto-run needs to know about a frame.</summary>
/// <param name="Toggle">The auto-run key was pressed this frame.</param>
/// <param name="Forward">Forward was pressed (not held) this frame.</param>
/// <param name="Back">Back was pressed or is held.</param>
/// <param name="Downed">The character is downed or dead.</param>
/// <param name="Talking">A conversation, or a screen that takes the keys, is open.</param>
/// <param name="Seconds">How long the frame was.</param>
/// <param name="MovedM">How far the body moved over the frame, in metres.</param>
public readonly record struct AutoRunFrame(bool Toggle, bool Forward, bool Back, bool Downed, bool Talking, double Seconds, double MovedM);

/// <summary>
/// The standard toggle: the key starts running forward; forward or back, or the key again, cancels it; strafing and steering with the mouse
/// or the camera leave it running, and Shift still sprints. It stops of itself on death or downing, on opening a conversation, and when
/// movement is blocked (a wall, a sheer slope: almost no ground gained for <see cref="BlockedSeconds"/>).
/// </summary>
public sealed class AutoRunRules
{
    /// <summary>Running into something for this long stops it.</summary>
    public const double BlockedSeconds = 0.6;

    /// <summary>Gaining less than this much ground a second counts as blocked.</summary>
    public const double BlockedBelowMPerSecond = 0.4;

    private double _blocked;

    public bool On { get; private set; }

    /// <summary>Put the frame in. Returns what stopped it this frame (words for a toast), or null; <see cref="On"/> is the state after.</summary>
    public string? Step(AutoRunFrame frame)
    {
        if (!On)
        {
            if (frame.Toggle && !frame.Downed && !frame.Talking)
            {
                On = true;
                _blocked = 0;
            }
            return null;
        }
        string? stopped = frame.Downed ? "Autorun off - you are down"
            : frame.Talking ? "Autorun off"
            : frame.Toggle ? "Autorun off"
            : frame.Forward || frame.Back ? "Autorun off"
            : null;
        if (stopped is null && frame.Seconds > 0)
        {
            _blocked = frame.MovedM / frame.Seconds < BlockedBelowMPerSecond ? _blocked + frame.Seconds : 0;
            if (_blocked >= BlockedSeconds)
                stopped = "Autorun off - something is in the way";
        }
        if (stopped is not null)
            Stop();
        return stopped;
    }

    /// <summary>Off without a word: a door, a journey, a respawn.</summary>
    public void Stop()
    {
        On = false;
        _blocked = 0;
    }
}
