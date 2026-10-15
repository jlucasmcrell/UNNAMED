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
        if (!bindings.Creatures.TryGetValue(definition.Id, out var look) || SkinnedModel.Create(art, look.Model, look.Clips, definition.Id) is not { } model)
            return null;
        // A clip that records the pace it is drawn at is played against that; the rest were retimed to the measured paces below.
        float Pace(string state, float measured) => (float)(art.Info(look.Clips.GetValueOrDefault(state) ?? "")?.AuthoredSpeed ?? measured);
        var creature = new SkinnedCreature { _model = model, _walkPace = Pace("walk", WalkPace), _runPace = Pace("run", RunPace) };
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
        // THE PACES THE CLIPS ARE ACTUALLY AUTHORED AT, MEASURED - NOT ASSUMED (creature fidelity round, 2026-09-29).
        // These two divisors are what a gait clip's implied ground speed is divided by to get its playback scale, so a
        // wrong divisor skates the feet by exactly the ratio it is wrong by. The clips carry `root_motion: false`, which
        // makes that speed decidable: while a foot is planted it is fixed to the ground, so its backward travel relative
        // to the root over its contact time IS the speed the clip is drawn for. Measured over the bound species by
        // tools/asset_pipeline/_creature_motion_measure.py - walk 1.86-2.00 m/s across the wolf, husk and armour; run
        // 6.05-6.27 across the wolf, hound and boar. The old 1.6 and 5.0 were guesses, and against them five of the six
        // species skated 12-25% at their own declared move_speed. A blind VIDEO review of the wolf walk - the same
        // animal, the same camera, the same floor, differing only in playback scale - scored the authored pace 9/10 on
        // foot sliding ("paws plant firmly") and the old assumed pace 2/10 ("each paw contacts the ground and
        // immediately glides forward instead of planting in world space", at 0:04-0:05), and asked for exactly this fix.
        // THE SPIDER IS DELIBERATELY LEFT ALONE: eight legs whose stance phases this measurement could not separate
        // (0.17-6.94 m/s spread), so it has no verified pace and an unverified constant is worse than a known gap.
        // THE CLAMP FLOOR IS 0.6, NOT 0.7, AND THAT IS A MEASURED CONSEQUENCE OF THE PACE ABOVE, NOT A PREFERENCE.
        // The floor exists to stop a near-stationary creature cycling its legs absurdly slowly, and at 0.7 it bound on
        // exactly one species: the boar's declared 4.0 m/s over a 6.054 m/s run clip is 0.656, below the floor, so the
        // clamp held a 5.9% residual skate while the wolf, hound, husk and armour all came in within 3%. The floor's job
        // is to bound degenerate speeds, not to decide a real one, so 0.6 clears the boar without loosening anything
        // that matters: run only plays above 2.6 m/s, where 2.6/6.1 = 0.426 is still floored. Nothing else moves -
        // measured, every other in-play species computes above 0.73, so only the boar is affected.
        // Small animals keep their own clips' paces (a hen walks at 0.29 m/s): their run starts halfway between the two.
        else if (speed > Math.Min(2.6f, (_walkPace + _runPace) / 2))
            _model.Play("run", 0.2f, Mathf.Clamp(speed / _runPace, 0.6f, 1.5f));
        else if (speed > Math.Min(0.3f, _walkPace / 2))
            _model.Play("walk", 0.2f, Mathf.Clamp(speed / _walkPace, 0.6f, 1.6f));
        else
            _model.Play("idle", 0.3f);
    }

    /// <summary>The measured paces of the retimed gait clips (see <see cref="Pose"/>).</summary>
    private const float WalkPace = 1.88f, RunPace = 6.1f;

    private float _walkPace = WalkPace;
    private float _runPace = RunPace;

    private void Mark(CreatureView creature)
    {
        (string text, Color colour) = creature switch
        {
            // Wildlife and the grazers carry no marks unless they turn and fight.
            { Peaceful: true, Mind: not CreatureMind.Engaged } => ("", Colors.White),
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
