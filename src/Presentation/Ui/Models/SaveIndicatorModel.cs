// UNNAMED Presentation - the corner "Saving…" indicator and the save failure that stays (PJ10 §6.2, §6.3.1; SYS-147)
// Pure: no Godot, so the rules are tested directly

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>
/// What the save corner says on a frame. While a save is written in the background it says "Saving…", for at least
/// <see cref="MinimumShownSeconds"/> so a fast save is still seen. A save that failed is said there until a later save succeeds: unlike a
/// toast it does not fade, because the world is not being kept until it clears. Nothing is said otherwise.
/// </summary>
public sealed class SaveIndicatorModel
{
    public const string Saving = "Saving…";

    /// <summary>A save faster than this is still shown this long, so the player sees that it happened.</summary>
    public const double MinimumShownSeconds = 0.8;

    private double _shownUntil = double.NegativeInfinity;
    private bool _wasSaving;

    /// <summary>The failure being said, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>What the corner shows now: the failure first, then "Saving…", or null for nothing.</summary>
    public string? Text { get; private set; }

    /// <summary>True while <see cref="Text"/> is a failure: drawn in the warning colour.</summary>
    public bool Warning => Text is not null && Text == Failure;

    /// <param name="now">Seconds on any steady clock.</param>
    /// <param name="saving">A save is being written now.</param>
    /// <param name="succeeded">A save finished and was committed on this frame.</param>
    /// <param name="failed">The headline of a save that failed on this frame, or null.</param>
    public void Update(double now, bool saving, bool succeeded, string? failed)
    {
        if (saving && !_wasSaving)
            _shownUntil = now + MinimumShownSeconds;
        _wasSaving = saving;
        if (succeeded)
            Failure = null;
        if (failed is not null)
            Failure = failed;
        Text = Failure ?? (saving || now < _shownUntil ? Saving : null);
    }
}
