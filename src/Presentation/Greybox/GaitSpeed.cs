// UNNAMED Presentation - a body's drawn speed for its stride, read from the simulation's own steps (Phase B remediation, H04)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// How fast a body is going, for choosing and pacing its gait clip, and where to draw it between the simulation's steps. The
/// simulation moves a body once per tick (20 Hz) while frames come faster: dividing each frame's movement by the frame time read a
/// spike on the frame the body stepped and zero on the frames between, and the clip flicked run - idle - idle - run (the Codex audit,
/// H04). Here the speed is each tick's step over the tick's length, eased, and the drawn position runs from the last step's start to
/// its end over one tick - a tick behind, like the player's own interpolation. A body blocked in place reads zero at once.
/// </summary>
public sealed class GaitSpeed
{
    private Vector3 _from;
    private Vector3 _to;
    private bool _started;
    private double _since;
    private float _speed;

    /// <summary>The eased speed (m/s) for the stride.</summary>
    public float Speed => _speed;

    /// <summary>
    /// A frame: the body's simulated position now, the frame's length and the tick's. Returns where to draw the body this frame.
    /// </summary>
    public Vector3 Sample(Vector3 position, double delta, double tickSeconds)
    {
        if (!_started || position.DistanceTo(_to) > 3f)
        {
            // First sight, or a teleport (a load, a respawn, a warp): no stride across it.
            _from = _to = position;
            _started = true;
            _since = 0;
            _speed = 0;
            return position;
        }
        _since += delta;
        float target;
        if (position != _to)
        {
            // A new step: its length over the time it took (at least one tick - two steps can land between frames).
            var step = new Vector2(position.X - _to.X, position.Z - _to.Z).Length();
            target = step / (float)Math.Max(_since, tickSeconds);
            _from = DrawnAt(tickSeconds);
            _to = position;
            _since = 0;
        }
        else
        {
            // No step yet this tick: the last step's speed holds until a tick has clearly passed without one, then it is a stop.
            target = _since > tickSeconds * 1.5 ? 0 : _speed;
        }
        // Eased over about a tenth of a second: a stop reads within a few frames, a single late tick does not flick the gait.
        float k = 1f - Mathf.Exp(-(float)delta / 0.1f);
        _speed = target == 0 && _since > tickSeconds * 3 ? 0 : Mathf.Lerp(_speed, Math.Min(target, 8f), k);
        return DrawnAt(tickSeconds);
    }

    private Vector3 DrawnAt(double tickSeconds) => _from.Lerp(_to, (float)Math.Clamp(_since / tickSeconds, 0, 1));
}
