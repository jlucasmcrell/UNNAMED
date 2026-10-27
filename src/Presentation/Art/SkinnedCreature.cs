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
    public string? ClipState => _model.Current;
    private readonly Label3D _mark = new()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 72, OutlineSize = 14, PixelSize = 0.004f, Visible = false,
    };
    private Vector3 _previous;
    private Vector3 _current;
    private float _yaw;
    private CombatPhase _phase = CombatPhase.Idle;
    private int _phaseTicks = 1;
    private string? _attackSource;
    private string _chosenAttack = "attack";
    private long _attackTick = -1;
    private HashSet<string> _clips = new(StringComparer.Ordinal);
    private readonly List<(BaseMaterial3D Material, float Energy)> _otherfire = new();

    /// <summary>The bound creature's skinned body, or null when the bindings or the asset lack it (the greybox is drawn instead).</summary>
    public static SkinnedCreature? Create(ArtLibrary art, ArtBindings bindings, CreatureDefinition definition, string individual)
    {
        if (!bindings.Creatures.TryGetValue(definition.Id, out var look) || SkinnedModel.Create(art, look.Model, look.Clips, definition.Id) is not { } model)
            return null;
        foreach (var (state, native) in look.NativeStates) model.Alias(state, native);
        // A clip that records the pace it is drawn at is played against that; the rest were retimed to the measured paces below.
        float Pace(string state, float measured) => look.Paces.TryGetValue(state, out float pace) && pace > 0
            ? pace : (float)(art.Info(look.Clips.GetValueOrDefault(state) ?? "")?.AuthoredSpeed ?? measured);
        // A bigger animal of the kind takes longer strides: its clips' paces grow with it.
        float size = OtherFauna.Apply(model, definition.Id, look.Fauna, individual);
        var creature = new SkinnedCreature { _model = model, _walkPace = Pace("walk", WalkPace) * size, _runPace = Pace("run", RunPace) * size,
            _clips = look.Clips.Keys.Concat(look.NativeStates.Keys).ToHashSet(StringComparer.Ordinal) };
        creature.AddChild(model);
        if (look.WeaponItem is { } item)
        {
            var weapon = bindings.Weapons.GetValueOrDefault(item) ?? throw new InvalidDataException("Creature weapon unbound: " + item);
            var fingers = BodyModifiers.Grip(model.Skeleton, "hand_r");
            if (fingers is not null) fingers.Closed = 1;
            var grip = SkinnedFigure.SocketGrip(model, "hand_r", fingers) ?? throw new InvalidDataException("Creature hand has no grip: " + look.Model);
            var hand = model.Attach("hand_r") ?? throw new InvalidDataException("Creature hand missing: " + look.Model);
            hand.AddChild(HeldWeapon.Mount(art, weapon.Model, grip) ?? throw new InvalidDataException("Creature weapon missing: " + item));
        }
        // A body that burns with Otherfire (its materials named otherfire_*) gets its own copies, so its blow can flare them alone.
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>().Where(m => m.Mesh is not null))
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                if (mesh.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D { EmissionEnabled: true } fire && fire.ResourceName.StartsWith("otherfire", StringComparison.Ordinal))
                {
                    var own = (BaseMaterial3D)fire.Duplicate();
                    mesh.SetSurfaceOverrideMaterial(i, own);
                    creature._otherfire.Add((own, own.EmissionEnergyMultiplier));
                }
        creature._mark.Position = new Vector3(0, ArtGallery.Bounds(model).End.Y * size + 0.4f, 0);
        creature.AddChild(creature._mark);
        return creature;
    }

    public override void Place(Body body)
    {
        _previous = _current = Feet(body);
        _yaw = Facing(body);
    }

    /// <summary>
    /// The party's mark on a kept animal: a red band round the neck, riding the neck bone so it follows the head, sized to the neck it actually
    /// has - the neck's radius plus a centimetre, the band two centimetres wide - and emissive enough to read at portrait distance, which a
    /// dark band on dark fur did not (measured: invisible in the still at 3 m). Not a life ring, and not a shader.
    /// </summary>
    public void WearCollar()
    {
        int neck = BoneNames.Find(_model.Skeleton, "neck");
        if (neck < 0)
            neck = BoneNames.Find(_model.Skeleton, "head");
        if (neck < 0)
            return;
        int head = BoneNames.Find(_model.Skeleton, "head");
        Vector3 at = _model.Skeleton.GetBoneGlobalRest(neck).Origin;
        double length = head >= 0 ? at.DistanceTo(_model.Skeleton.GetBoneGlobalRest(head).Origin) : 0.18;
        float radius = Mathf.Max(0.03f, (float)length * 0.25f) + 0.02f;
        if (_model.Attach(_model.Skeleton.GetBoneName(neck)) is not { } bone)
            return;
        bone.AddChild(new MeshInstance3D
        {
            Name = "PetCollar",
            Mesh = new TorusMesh { InnerRadius = radius, OuterRadius = radius + 0.030f, Rings = 24, RingSegments = 10 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1.0f, 0.07f, 0.05f),
                Roughness = 0.4f,
                EmissionEnabled = true,
                Emission = new Color(1.0f, 0.05f, 0.03f),
                EmissionEnergyMultiplier = 1.8f,
            },
            Position = new Vector3(0, (float)length * 0.5f, 0),
        });
    }

    public override void Advance(Body body)
    {
        _previous = _current;
        _current = Feet(body);
    }

    internal void PrewarmMarks(Action draw)
    {
        // Label3D text changes are deferred, so temporarily assigning "!" then restoring it never drew a glyph.
        // Prime the actual fill/outline atlas directly, without changing what this creature communicates.
        var fonts = (_mark.Font ?? ThemeDB.FallbackFont).GetRids();
        using var fontArray = (Godot.Collections.Array)fonts;
        var server = TextServerManager.GetPrimaryInterface();
        foreach (var font in fonts)
            foreach (int outline in new[] { 0, _mark.OutlineSize })
                foreach (char mark in "!?")
                    server.FontRenderRange(font, new Vector2I(_mark.FontSize, outline), mark, mark);
        ForceUpdateTransform();
        foreach (string state in _clips)
        {
            _model.Hold(state, 0.5f, 0);
            _model.Skeleton.Advance(0);
            draw();
        }
        _model.Play("idle", 0, restart: true);
        _model.Skeleton.Advance(0);
        // Shape the real 3D glyph material after Label3D's deferred update, then retain it invisibly with this space.
        var glyph = (Label3D)_mark.Duplicate();
        glyph.Name = "PreparedAlarmGlyphs";
        glyph.Text = "!?";
        glyph.Visible = false;
        glyph.Layers = 1u << 19;
        AddChild(glyph);
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(glyph) || !IsInsideTree() || GetViewport().GetCamera3D() is not { } camera) return;
            uint mask = camera.CullMask;
            try
            {
                camera.CullMask = mask | glyph.Layers;
                glyph.GlobalPosition = camera.GlobalPosition - camera.GlobalBasis.Z * 2;
                glyph.Visible = true;
                camera.ForceUpdateTransform();
                glyph.ForceUpdateTransform();
                for (int i = 0; i < 3; i++) RenderingServer.ForceDraw(false, 1.0 / 60);
            }
            finally
            {
                glyph.Visible = false;
                camera.CullMask = mask;
            }
        }).CallDeferred();
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
        // The telegraph of a body that burns: the fire swells through the windup, holds through the blow, and goes out when it falls.
        float charge = 1f - (float)creature.PhaseTicksLeft / Math.Max(1, creature.PhaseDurationTicks);
        float flare = !creature.Alive ? 0f : creature.Phase switch { CombatPhase.Windup => 1f + 4f * charge, CombatPhase.Active => 5f, _ => 1f };
        foreach (var (material, energy) in _otherfire)
            material.EmissionEnergyMultiplier = energy * flare;
        if (!creature.Alive)
        {
            _mark.Visible = false;
            _model.Play("death", 0.15f);
            return;
        }
        Mark(creature);
        if (creature.Paralysed)
        {
            _model.Hold("idle", .25f, 0);
            return;
        }
        if (creature.Guarding)
        {
            _model.Play("block", .08f);
            return;
        }
        if (creature.Phase != _phase || creature.AttackSource != _attackSource || creature.AttackTick != _attackTick)
        {
            if (creature.AttackSource is { } move && (creature.Phase == CombatPhase.Windup || creature.AttackTick != _attackTick))
            {
                var variants = _clips.Where(c => c == move || c.StartsWith(move + "_", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
                _chosenAttack = variants.Length > 0 ? variants[creature.AttackVariant % variants.Length] : "attack";
            }
            _phase = creature.Phase;
            _attackTick = creature.AttackTick;
            _attackSource = creature.AttackSource;
            _phaseTicks = creature.PhaseDurationTicks > 0 ? creature.PhaseDurationTicks : Math.Max(1, creature.PhaseTicksLeft + 1);
        }
        string attackClip = _chosenAttack;
        float t = 1f - (float)creature.PhaseTicksLeft / _phaseTicks;
        switch (creature.Phase)
        {
            case CombatPhase.Windup:
                _model.Hold(attackClip, 0.45f * t);
                return;
            case CombatPhase.Active:
                _model.Hold(attackClip, 0.45f + 0.25f * t);
                return;
            case CombatPhase.Recovery:
                _model.Hold(attackClip, 0.7f + 0.3f * t);
                return;
            case CombatPhase.Staggered:
                _model.Play("hit", 0.1f);
                return;
            case CombatPhase.Dodge:
                _model.Play("dodge", .08f);
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
