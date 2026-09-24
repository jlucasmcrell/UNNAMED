// UNNAMED Presentation - the aiming reticle (CAMERA_PERSPECTIVE_AND_PRESENTATION.md §11; the owner's M6 playtest)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Where a drawn shot or a thrown working will go: a ring drawn on the point the simulation says the shot would stop (a creature, a
/// wall, or the end of its range), so in third person, off the shoulder, it sits where the arrow will land rather than at the middle of
/// the screen. It turns red over a creature. No line is drawn to it.
/// </summary>
public partial class Reticle : Control
{
    private bool _onCreature;

    public override void _Ready()
    {
        Size = new Vector2(28, 28);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    /// <summary>Show the ring at a point on the screen, or hide it (null).</summary>
    public void Aim(Vector2? point, bool onCreature)
    {
        Visible = point is not null;
        if (point is not { } at)
            return;
        Position = at - Size / 2;
        if (onCreature != _onCreature)
        {
            _onCreature = onCreature;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var centre = Size / 2;
        var colour = _onCreature ? new Color(1f, 0.35f, 0.3f) : new Color(0.95f, 0.95f, 0.9f);
        DrawArc(centre, 9, 0, Mathf.Tau, 32, Colors.Black, 4);
        DrawArc(centre, 9, 0, Mathf.Tau, 32, colour, 2);
        DrawCircle(centre, 2.5f, Colors.Black);
        DrawCircle(centre, 1.6f, colour);
    }
}
