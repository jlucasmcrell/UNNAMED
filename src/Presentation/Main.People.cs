// UNNAMED Presentation - a character's people in play: the Kal wings held from the jump key, the racial abilities' words, and the wings drawn
// (RACE_ABILITIES_PROPOSAL.md §3, owner-approved 2026-10-24)
// Godot presentation only: it submits the wing and ability commands and says what the simulation did (D-11)

using Godot;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>How long into a jump the key must still be held before the wings spread: a tap stays a jump.</summary>
    private const int WingHoldMs = 200;

    private bool _wingsHeld;
    private WingState? _wingsDrawn;

    /// <summary>
    /// The jump key held past the jump spreads the wings (Kal: Wing Glide); let go aloft, they fold and the body drops. Sent once each way, and
    /// only for a body whose people has wings.
    /// </summary>
    private void Wings(bool held)
    {
        var simulation = _session.Simulation!;
        if (!simulation.PlayerMovement.Wings.CanGlide)
            return;
        var posture = simulation.Posture;
        bool spread = held && (posture.Aloft || posture.Airborne && posture.AirMs >= WingHoldMs);
        if (spread == _wingsHeld)
            return;
        if (spread && posture.Aloft)
        {
            _wingsHeld = true;
            return;
        }
        _wingsHeld = spread;
        if (spread || posture.Aloft)
            _controller.Glide(spread);
    }

    /// <summary>
    /// The wings drawn as the posture holds them, through the figure's own wing rig when its people has one (PeopleAppearance attaches it as
    /// the skeleton's <c>KalWings</c> child; its states are Folded, Glide, Flare, Leap). A drop is drawn folded.
    /// </summary>
    private void DrawWings(Posture posture)
    {
        var state = posture.Airborne ? posture.Wing : WingState.Folded;
        if (state == _wingsDrawn || _avatar is not Art.SkinnedFigure figure || figure.Skeleton.GetNodeOrNull("KalWings") is not { } wings)
            return;
        _wingsDrawn = state;
        wings.Call("SetState", state switch
        {
            WingState.Glide => 1,
            WingState.Flare => 2,
            WingState.Leap => 3,
            _ => 0,
        });
    }

    private void SubscribePeople()
    {
        _session.Subscribe<AnchorPlaced>(_ => Notice(NoticeKind.Progress, "Anchor set", 2));
        _session.Subscribe<RecallBegun>(_ => _hud.Toast("Crossing back - hold still", 3));
        _session.Subscribe<RecallBroken>(e => _hud.Toast($"The crossing slipped: {e.Reason}", 3));
        // What a crossing took (§3.4), in words and never a number: the toll stays hidden, its cost does not.
        _session.Subscribe<CrossingCostTaken>(e => _hud.Toast(e.Kind switch
        {
            TollKind.Memory => e.Ref is { } place ? $"The way to {Names.Of(_session, place)} has gone from you." : "Something you knew is gone.",
            TollKind.Name => e.Ref is { } who ? $"You cannot remember who {Names.Of(_session, who)} was." : "A name has gone from you.",
            TollKind.Years => "You feel older than you did this morning.",
            _ => "Something in you no longer quite resolves.",
        }, 6));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is PeopleAbilityCommand)
                _hud.Toast(e.Reason, 2);
            else if (e.Command is GlideCommand && e.Reason.StartsWith("too tired", StringComparison.Ordinal))
                _hud.Toast("Too tired to hold the wings", 2);
        });
    }
}
