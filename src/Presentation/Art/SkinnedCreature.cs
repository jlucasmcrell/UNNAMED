// UNNAMED Presentation - a creature drawn with the asset library's rigged body and its six clips (the Phase-1 asset integration)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A creature as the asset pipeline made it: its rigged body and its six clips (idle, walk, run, attack, hit, death), chosen by what the
/// simulation says. An attack's clip is held at the point its phase has reached - the windup to 45%, the active window to 70%, the
/// recovery to the end - so the tell reads on the simulation's timing, never the clip's. The mark over it says what it knows.
/// </summary>
public sealed partial class SkinnedCreature : CreatureBody
{
    private SkinnedModel _model = null!;
    private readonly Label3D _mark = new()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 72, OutlineSize = 14, PixelSize = 0.004f, Visible = false,
    };
    private Vector3 _previous;
    private Vector3 _current;
    private float _yaw;
    private CombatPhase _phase = CombatPhase.Idle;
    private int _phaseTicks = 1;

    /// <summary>The bound creature's skinned body, or null when the bindings or the asset lack it (the greybox is drawn instead).</summary>
    public static SkinnedCreature? Create(ArtLibrary art, ArtBindings bindings, CreatureDefinition definition)
    {
        if (!bindings.Creatures.TryGetValue(definition.Id, out var look) || SkinnedModel.Create(art, look.Model, look.Clips) is not { } model)
            return null;
        var creature = new SkinnedCreature { _model = model };
        creature.AddChild(model);
        creature._mark.Position = new Vector3(0, ArtGallery.Bounds(model).End.Y + 0.4f, 0);
        creature.AddChild(creature._mark);
        return creature;
    }

    public override void Place(Body body)
    {
        _previous = _current = Feet(body);
        _yaw = Facing(body);
    }

    public override void Advance(Body body)
    {
        _previous = _current;
        _current = Feet(body);
    }

    public override void Pose(CreatureView creature, float alpha, double delta)
    {
        if (creature.Condition == CreatureCondition.Gone)
        {
            Visible = false;
            return;
        }
        Visible = true;
        Position = _previous.Lerp(_current, alpha);
        float speed = new Vector2(_current.X - _previous.X, _current.Z - _previous.Z).Length() / 0.05f;
        _yaw = Mathf.LerpAngle(_yaw, Facing(creature.Body), 1f - Mathf.Exp(-16f * (float)delta));
        Rotation = new Vector3(0, _yaw, 0);
        if (!creature.Alive)
        {
            _mark.Visible = false;
            _model.Play("death", 0.15f);
            return;
        }
        Mark(creature);
        if (creature.Phase != _phase)
        {
            _phase = creature.Phase;
            _phaseTicks = Math.Max(1, creature.PhaseTicksLeft + 1);
        }
        float t = 1f - (float)creature.PhaseTicksLeft / _phaseTicks;
        switch (creature.Phase)
        {
            case CombatPhase.Windup:
                _model.Hold("attack", 0.45f * t);
                return;
            case CombatPhase.Active:
                _model.Hold("attack", 0.45f + 0.25f * t);
                return;
            case CombatPhase.Recovery:
                _model.Hold("attack", 0.7f + 0.3f * t);
                return;
            case CombatPhase.Staggered:
                _model.Play("hit", 0.1f);
                return;
        }
        if (creature.Asleep)
            _model.Play("idle", 0.3f, 0.35f);
        else if (speed > 2.6f)
            _model.Play("run", 0.2f, Mathf.Clamp(speed / 5f, 0.7f, 1.5f));
        else if (speed > 0.3f)
            _model.Play("walk", 0.2f, Mathf.Clamp(speed / 1.6f, 0.6f, 1.6f));
        else
            _model.Play("idle", 0.3f);
    }

    private void Mark(CreatureView creature)
    {
        (string text, Color colour) = creature switch
        {
            { Asleep: true } => ("z", new Color(0.75f, 0.8f, 0.9f)),
            { Mind: CreatureMind.Engaged } => ("!", new Color(1f, 0.25f, 0.15f)),
            { Mind: CreatureMind.Suspicious or CreatureMind.Searching } => ("?", new Color(1f, 0.85f, 0.25f)),
            _ => ("", Colors.White),
        };
        _mark.Visible = text != "";
        _mark.Text = text;
        _mark.Modulate = colour;
    }

    private static Vector3 Feet(Body body) => HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);

    private static float Facing(Body body) => Mathf.DegToRad(body.FacingMdeg / 1000f);
}
