// UNNAMED Presentation - the creator's live preview: the body being made, turning on a quiet set under the light the player chooses (PJ04 §3.2, §6)
// Godot presentation only: it draws the gameplay model the character will be drawn with, and holds no game state (D-11)

using Godot;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The body on a set of its own (its own world, so the hollow behind the title neither lights it nor is lit by it), drawn at the window's
/// own pixels behind the creator's panel. Drag to turn it and the wheel to come closer; the creator's buttons do the same from the keys.
/// The figure is the game's own: the same model, the same materials, the same clips, the same build.
/// </summary>
public sealed partial class CreatorPreview : TextureRect
{
    /// <summary>The four lights PJ04 §3.2 names, in its order.</summary>
    public static readonly string[] Lights = { "Road morning", "Overcast", "Torchlit interior", "Night by lantern" };

    /// <summary>Where the camera can stand: the whole body, the upper body, the face (PJ04 §3.2's framing buttons), as a zoom from 0 to 1.</summary>
    public static readonly (string Name, float Zoom)[] Framings = { ("Full body", 0f), ("Upper body", 0.55f), ("Face", 1f) };

    private readonly SubViewport _viewport = new()
    {
        Name = "Set", OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        Size = new Vector2I(1920, 1080),
    };

    private readonly Node3D _turntable = new() { Name = "Turntable" };
    private readonly Camera3D _camera = new() { Name = "Camera", Projection = Camera3D.ProjectionType.Frustum, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 0.05f };
    // The key's shadows are soft (a sun of some angular size) and fall only on the set, so their maps resolve the face; the fill opens the
    // shadow side of the face from beside the camera.
    private readonly DirectionalLight3D _key = new()
    {
        Name = "Key", ShadowEnabled = true, LightAngularDistance = 4, DirectionalShadowMaxDistance = 12,
        DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
    };
    private readonly DirectionalLight3D _fill = new() { Name = "Fill" };
    private readonly OmniLight3D _rim = new() { Name = "Rim", OmniRange = 8 };
    private readonly OmniLight3D _lamp = new() { Name = "Lamp", OmniRange = 5, ShadowEnabled = true };
    private readonly ProceduralSkyMaterial _sky = new();
    private readonly MeshInstance3D _ground = new() { Name = "Ground", Mesh = new PlaneMesh { Size = new Vector2(120, 120) } };
    private readonly Godot.Environment _environment = new();
    private readonly Dictionary<string, Figure> _figures = new(StringComparer.Ordinal);
    private readonly Dictionary<Figure, BuildPose?> _poses = new();

    private ArtLibrary _art = ArtLibrary.Empty;
    private ArtBindings _bindings = ArtBindings.Empty;
    private Figure? _figure;
    private float _yaw;
    private float _zoom;
    private float _shownZoom;
    private float _eyes = Avatar.EyeHeight;
    private bool _dragging;
    private string _expression = "neutral";
    public void Expression(string mood)
    {
        _expression = mood;
        _figure?.Perform(null);
        _figure?.SetExpression(mood);
    }
    public IReadOnlyList<string> PerformanceClips => _figure?.PerformanceClips ?? Array.Empty<string>();
    public string PerformanceName(string clip) => _figure?.PerformanceName(clip) ?? clip;
    public void Perform(string? clip) => _figure?.Perform(clip);

    /// <summary>Whether the body shown wears cloth the shirt and trouser colours dye (not the frontier outfit, which keeps its own).</summary>
    public bool WearsDyedCloth => _figure is not null && PeopleAppearance.WearsDyedCloth(_figure);

    public bool WearsFrontier => _figure is not null && PeopleAppearance.WearsFrontier(_figure);

    /// <summary>The light in use, by its index in <see cref="Lights"/>.</summary>
    public int Light { get; private set; }

    /// <summary>A contained inventory preview uses its own bounds and a centred lens.</summary>
    public bool Centered {get;init;}

    public void Bind(ArtLibrary art, ArtBindings bindings)
    {
        _art = art;
        _bindings = bindings;
    }

    public override void _Ready()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        StretchMode = StretchModeEnum.Scale;
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_viewport);
        Texture = _viewport.GetTexture();

        _environment.BackgroundMode = Godot.Environment.BGMode.Sky;
        _environment.Sky = new Sky { SkyMaterial = _sky };
        _environment.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        _environment.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;
        // The world's tone mapper (HollowView), so a face in the creator is the face in the game: Filmic pushed the skin warm and red.
        _environment.TonemapMode = Godot.Environment.ToneMapper.Agx;
        _environment.SsaoEnabled = true;
        var stage = new Node3D { Name = "Stage" };
        stage.AddChild(new WorldEnvironment { Environment = _environment });
        _key.RotationDegrees = new Vector3(-38, -32, 0);
        stage.AddChild(_key);
        _fill.RotationDegrees = new Vector3(-12, 35, 0);
        stage.AddChild(_fill);
        _rim.Position = new Vector3(-1.4f, 2.1f, -1.8f);
        stage.AddChild(_rim);
        stage.AddChild(_lamp);
        // The ground the body stands on, running off into the set's haze: the feet meet something, the shadow falls somewhere, and
        // there is no edge to the world behind the body.
        stage.AddChild(_ground);
        // A haze that starts well behind the body, so the body itself is never veiled.
        _environment.FogEnabled = true;
        _environment.FogMode = Godot.Environment.FogModeEnum.Depth;
        _environment.FogDepthBegin = 9;
        _environment.FogDepthEnd = 55;
        _environment.FogDensity = 1;
        _environment.FogSkyAffect = 0;
        stage.AddChild(_turntable);
        stage.AddChild(_camera);
        _viewport.AddChild(stage);
        _camera.MakeCurrent();
        UseLight(0);
    }

    private string? _model;

    /// <summary>
    /// Draw this body: <paramref name="model"/> on the player's binding (null: the greybox mannequin, as the game would), its arms at the
    /// build's <paramref name="armSplay"/>, wearing <paramref name="worn"/>. A body already
    /// drawn once this session is shown again without loading.
    /// </summary>
    public void Show(string? model, string? people, float armSplay, WornOutfit worn,string? weapon=null)
    {
        string key = (model ?? "") + "|" + people;
        _model = model;
        if (!_figures.TryGetValue(key, out var figure))
        {
            figure = (Figure?)MHCharacters.MetaHumanFigure.Create(_art, _bindings, "player", model) ?? (model is not null && SkinnedFigure.Create(_art, _bindings, "player", model, people: people) is { } skinned ? skinned : new Avatar());
            figure.Name = model ?? "Greybox";
            if (figure is SkinnedFigure detailed) detailed.ForceLod0 = true;
            _turntable.AddChild(figure);
            _figures[key] = figure;
            _poses[figure] = model?.StartsWith("humgen_", StringComparison.Ordinal) != true && figure is SkinnedFigure body ? BuildPose.On(body.Skeleton) : null;
        }
        foreach (var other in _figures.Values)
            other.Visible = other == figure;
        _figure = figure;
        figure.SetExpression(_expression);
        if (_poses[figure] is { } pose)
            pose.ArmSplayDegrees = armSplay;
        figure.WearEquipment(worn);
        figure.Hold(weapon);
    }

    /// <summary>The draft's skin tone and hair colour on the body shown (<see cref="PeopleAppearance.ApplyLook"/>).</summary>
    public void Look(string? people, Domain.Character.CharacterAppearance? look)
    {
        if (_figure is not null)
            PeopleAppearance.ApplyLook(_figure, people, look, _art, _model);
    }

    /// <summary>Draw the outfit's own headwear or not: the creator hides it on the steps that are about the face and the hair.</summary>
    public void WearHeadwear(bool worn)
    {
        if (_figure is not null)
            HumGenCreatorAppearance.WearHeadwear(_figure, worn);
    }

    /// <summary>Turn the body by an angle, in radians.</summary>
    public void Turn(float radians) => _yaw = Mathf.Wrap(_yaw + radians, -Mathf.Pi, Mathf.Pi);

    /// <summary>Come closer (positive) or step back, as a share of the way from the whole body to the face.</summary>
    public void Zoom(float by) => _zoom = Mathf.Clamp(_zoom + by, 0, 1);

    /// <summary>Stand the camera at a framing (<see cref="Framings"/>).</summary>
    public void Frame(float zoom) => _zoom = Mathf.Clamp(zoom, 0, 1);

    /// <summary>Put the body back as it first stood: facing the camera, the whole body in view.</summary>
    public void ResetView()
    {
        _yaw = 0;
        _zoom = 0;
    }

    /// <summary>Light the set as one of <see cref="Lights"/>.</summary>
    public void UseLight(int index)
    {
        Light = ((index % Lights.Length) + Lights.Length) % Lights.Length;
        switch (Light)
        {
            case 0:   // the opening's light: late morning, light overcast (bible §28)
                Sky(new Color(0.36f, 0.48f, 0.66f), new Color(0.70f, 0.72f, 0.74f), new Color(0.20f, 0.18f, 0.16f), 0.9f);
                Key(new Color(1f, 0.94f, 0.84f), 2.1f);
                Fill(new Color(0.86f, 0.9f, 1f), 0.55f);
                Rim(new Color(0.75f, 0.82f, 1f), 0.9f);
                Lamp(null, 0);
                break;
            case 1:
                Sky(new Color(0.52f, 0.55f, 0.60f), new Color(0.70f, 0.71f, 0.72f), new Color(0.22f, 0.21f, 0.20f), 1.2f);
                Key(new Color(0.92f, 0.94f, 0.97f), 0.7f);
                Fill(new Color(0.92f, 0.94f, 0.97f), 0.25f);
                Rim(new Color(0.9f, 0.92f, 0.96f), 0.4f);
                Lamp(null, 0);
                break;
            case 2:
                Sky(new Color(0.06f, 0.05f, 0.04f), new Color(0.12f, 0.09f, 0.07f), new Color(0.05f, 0.04f, 0.03f), 0.35f);
                Key(new Color(0.9f, 0.85f, 0.8f), 0.25f);
                Fill(new Color(1f, 0.8f, 0.6f), 0.1f);
                Rim(new Color(1f, 0.55f, 0.25f), 0.5f);
                Lamp(new Vector3(1.1f, 1.7f, 1.0f), 4.5f);
                break;
            default:
                Sky(new Color(0.03f, 0.05f, 0.10f), new Color(0.08f, 0.10f, 0.16f), new Color(0.03f, 0.03f, 0.04f), 0.4f);
                Key(new Color(0.55f, 0.65f, 0.95f), 0.35f);
                Fill(new Color(0.55f, 0.65f, 0.95f), 0.1f);
                Rim(new Color(0.5f, 0.6f, 1f), 0.6f);
                Lamp(new Vector3(0.7f, 1.1f, 1.1f), 3.0f);
                break;
        }
    }

    private void Sky(Color top, Color horizon, Color ground, float ambient)
    {
        _sky.SkyTopColor = top;
        _sky.SkyHorizonColor = horizon;
        // Below the horizon the sky is the haze, so the ground fades into it with no line where the set ends.
        _sky.GroundBottomColor = horizon;
        _sky.GroundHorizonColor = horizon;
        _ground.MaterialOverride = new StandardMaterial3D { AlbedoColor = ground, Roughness = 1 };
        _environment.AmbientLightEnergy = ambient;
        _environment.FogLightColor = horizon;
    }

    private void Key(Color colour, float energy)
    {
        _key.LightColor = colour;
        _key.LightEnergy = energy;
    }

    private void Fill(Color colour, float energy)
    {
        _fill.LightColor = colour;
        _fill.LightEnergy = energy;
    }

    private void Rim(Color colour, float energy)
    {
        _rim.LightColor = colour;
        _rim.LightEnergy = energy;
    }

    private void Lamp(Vector3? at, float energy)
    {
        _lamp.Visible = at is not null;
        _lamp.Position = at ?? Vector3.Zero;
        _lamp.LightColor = new Color(1f, 0.66f, 0.36f);
        _lamp.LightEnergy = energy;
    }

    public override void _Process(double delta)
    {
        // The set is drawn only while the creator shows it.
        var mode = IsVisibleInTree() ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        var processing = mode == SubViewport.UpdateMode.Disabled ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        if (_viewport.ProcessMode != processing)
            _viewport.ProcessMode = processing;
        if (_viewport.RenderTargetUpdateMode != mode)
            _viewport.RenderTargetUpdateMode = mode;
        if (mode == SubViewport.UpdateMode.Disabled || _figure is null)
            return;
        // At the window's own pixels: the canvas is stretched, and a set drawn at canvas size would be blurred at 1440p.
        var window = Centered ? new Vector2I(Math.Max(1,(int)Size.X),Math.Max(1,(int)Size.Y)) : DisplayServer.WindowGetSize();
        if (window.X > 0 && window.Y > 0 && _viewport.Size != window)
            _viewport.Size = window;
        _figure.Pose(Vector3.Zero, 0, 0, delta);
        _turntable.Rotation = new Vector3(0, _yaw, 0);
        if (_figure.Head is { } head)
            _eyes = Mathf.Lerp(_eyes, head.Y - 0.06f, 0.2f);
        _shownZoom = Mathf.Lerp(_shownZoom, _zoom, 1f - Mathf.Exp(-10f * (float)delta));
        // The whole body (the hips' height, far enough for head to feet), the upper body, the face: eased between as one zoom.
        float scale = _eyes / Avatar.EyeHeight;
        float t = _shownZoom;
        float height = t < 0.55f ? Mathf.Lerp(0.95f, 1.38f, t / 0.55f) * scale : Mathf.Lerp(1.38f * scale, _eyes, (t - 0.55f) / 0.45f);
        // The face is framed from 1.6 m with a longer lens: at 0.85 m the perspective swelled the nose 13 % against the ears.
        float distance = t < 0.55f ? Mathf.Lerp(5.1f, 2.1f, t / 0.55f) : Mathf.Lerp(2.1f, 1.6f, (t - 0.55f) / 0.45f);
        float fov = t < 0.55f ? 30f : Mathf.Lerp(30f, 16f, (t - 0.55f) / 0.45f);
        _camera.Position = new Vector3(0, height + 0.04f * distance, distance);
        _camera.LookAt(new Vector3(0, height, 0), Vector3.Up);
        // The body stands right of the panel: the lens is shifted (the view stays square on the body), not the camera moved left of
        // it - that showed the figure from 9.5 degrees to its right, narrowing its shoulders.
        float aspect = (float)window.X / Math.Max(1, window.Y);
        float half = _camera.Near * Mathf.Tan(Mathf.DegToRad(fov) / 2f);
        _camera.Size = 2f * half;
        _camera.FrustumOffset = Centered ? Vector2.Zero : new Vector2(-2f * half * aspect * 0.17f, 0);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } press:
                _dragging = press.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Zoom(0.08f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Zoom(-0.08f);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                Turn(motion.Relative.X * 0.012f);
                AcceptEvent();
                break;
        }
    }
}
