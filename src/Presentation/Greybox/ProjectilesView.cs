// UNNAMED Presentation - shots in flight: arrows and thrown workings (the owner's M6 playtest)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Draws each shot the simulation published (<c>ShotLoosed</c>) along the path it resolved: an arrow that flies to where it stopped and
/// stays stuck there when it struck no creature, so the player can see where a shot went; a working that travels and bursts where it
/// stops. A working's art is the pipeline's flipbook named for it (<c>vfx.&lt;domain.name&gt;_travel</c> and <c>_impact</c>) when the asset
/// workspace has one; otherwise, and for arrows, which have no art yet, greybox. The blow itself already landed at release.
/// </summary>
public partial class ProjectilesView : Node3D
{
    public const float ArrowSpeed = 55f;
    private const float WorkingSpeed = 38f;
    private const double StuckSeconds = 10;

    /// <summary>From the arrow's centre back to its point, less the bite it takes: a stopped arrow's point is in what it hit.</summary>
    private const float PointAhead = 0.455f - 0.12f;

    private const float TrailLength = 3f;
    private const float TrailWidth = 0.06f;
    private const double TrailFade = 0.2;

    private sealed class Flight
    {
        public required Node3D Node { get; init; }
        public required Vector3 From { get; init; }
        public required Vector3 To { get; init; }
        public required double Duration { get; init; }
        public required bool Working { get; init; }
        public required bool Struck { get; init; }
        public string? EffectStem { get; init; }
        public double Elapsed { get; set; }
    }

    private readonly List<Flight> _flying = new();
    private readonly List<(Node3D Node, double Until, Action<double>? Animate)> _lingering = new();
    private readonly List<(Vector3 Tail, Vector3 Head, double Until)> _fadingTrails = new();
    private readonly MeshInstance3D _aimLine = new() { Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private readonly MeshInstance3D _trails = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private AssetCatalog _assets = AssetCatalog.Empty;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;
    private double _clock;
    private double? _lastBurst;

    /// <summary>How far the newest arrow (or working) in the air has flown, in metres; null when none is. For the delta shots.</summary>
    public float? Travelled(bool working) => _flying.LastOrDefault(f => f.Working == working) is { } flight ? flight.From.DistanceTo(flight.Node.Position) : null;

    /// <summary>Seconds since the last working burst; null before the first. For the delta shots.</summary>
    public double? SinceBurst => _clock - _lastBurst;

    public override void _Ready()
    {
        _aimLine.Mesh = new ImmediateMesh();
        _aimLine.MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 0.9f, 0.2f), NoDepthTest = true };
        AddChild(_aimLine);
        _trails.Mesh = new ImmediateMesh();
        _trails.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(_trails);
    }

    public void Bind(AssetCatalog assets, Art.ArtLibrary art, Art.ArtBindings bindings, Art.ArtCoverage? coverage = null)
    {
        _assets = assets;
        _recipes = VisualOptions.Recipes ? Art.ParticleRecipes.Load(assets.Root) : null;
        _art = art;
        _bindings = bindings;
        _coverage = coverage;
    }

    private Art.ArtCoverage? _coverage;

    // Phase B (B11): the particle recipes that stand in for the flat flipbooks, when the run draws with them.
    private Art.ParticleRecipes? _recipes;

    /// <summary>The recipes standing in for an effect, built; empty when the run draws flipbooks or none stand in.</summary>
    private List<GpuParticles3D> Recipes(string effect)
    {
        var built = new List<GpuParticles3D>();
        if (_recipes is null)
            return built;
        foreach (string name in _recipes.For(effect))
        {
            if (_recipes.Build(name) is { } particles)
                built.Add(particles);
        }
        if (built.Count > 0 || HasForms(effect))
            _coverage?.Resolved("effect_recipe", effect, string.Join(" + ", _recipes.For(effect)));
        return built;
    }

    private bool HasForms(string effect) => _recipes?.For(effect).Any(n => n.StartsWith(Art.FormulaForm.Prefix, StringComparison.Ordinal)) == true;

    /// <summary>The formula forms the recipes name for an effect (the bolt's core, its pressure front), built.</summary>
    private List<Art.FormulaForm> Forms(string effect) =>
        _recipes is null ? new() : _recipes.For(effect).Select(Art.FormulaForm.Create).OfType<Art.FormulaForm>().ToList();

    /// <summary>A shot's flipbook, recorded in the coverage report (resolved, or the greybox that stands in for it).</summary>
    private Flipbook? Book(string? stem, string moment, string greybox)
    {
        var book = stem is null ? null : _assets.Effect($"{stem}_{moment}");
        string key = $"{stem ?? "working"}_{moment}";
        if (book is not null)
            _coverage?.Resolved("effect", key, key);
        else
            _coverage?.Fallback("effect", key, $"no {moment} flipbook in the effect manifest: {greybox}", stem is null ? null : key);
        return book;
    }

    /// <summary>Where a shot came to rest as drawn - a working's burst, an arrow in a wall or the ground, or in a creature (struck) - for its sound.</summary>
    public Action<Vector3, bool, bool>? Arrived { get; set; }

    /// <summary>
    /// A shot from <paramref name="from"/> to <paramref name="to"/>: an arrow, or a working (<paramref name="effectStem"/> names its
    /// flipbooks). <paramref name="struck"/>: it ended on a creature.
    /// </summary>
    public void Loose(Vector3 from, Vector3 to, bool working, bool struck, string? effectStem)
    {
        var node = working ? Working(effectStem) : Arrow();
        AddChild(node);
        Face(node, from, to);
        if (!working && from.DistanceTo(to) > PointAhead)
            to -= (to - from).Normalized() * PointAhead;
        float length = from.DistanceTo(to);
        _flying.Add(new Flight
        {
            Node = node, From = from, To = to, Duration = Math.Max(0.05, length / (working ? WorkingSpeed : ArrowSpeed)),
            Working = working, Struck = struck, EffectStem = effectStem,
        });
    }

    /// <summary>The developer's aim line (F3 only): from the bow to where the reticle is.</summary>
    public void ShowAimLine(Vector3? from, Vector3? to)
    {
        _aimLine.Visible = from is not null && to is not null;
        if (!_aimLine.Visible)
            return;
        var mesh = (ImmediateMesh)_aimLine.Mesh;
        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        mesh.SurfaceAddVertex(from!.Value);
        mesh.SurfaceAddVertex(to!.Value);
        mesh.SurfaceEnd();
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        for (int i = _flying.Count - 1; i >= 0; i--)
        {
            var flight = _flying[i];
            flight.Elapsed += delta;
            float t = (float)Math.Min(1, flight.Elapsed / flight.Duration);
            flight.Node.Position = flight.From.Lerp(flight.To, t);
            if (flight.Working)
                Animate(flight.Node, flight.Elapsed);
            if (t < 1)
                continue;
            _flying.RemoveAt(i);
            Arrive(flight);
        }
        for (int i = _lingering.Count - 1; i >= 0; i--)
        {
            var (node, until, animate) = _lingering[i];
            animate?.Invoke(until - _clock);
            if (_clock < until)
                continue;
            node.QueueFree();
            _lingering.RemoveAt(i);
        }
        DrawTrails();
    }

    private void Arrive(Flight flight)
    {
        Arrived?.Invoke(flight.To, flight.Working, flight.Struck);
        if (flight.Working)
        {
            foreach (var trail in flight.Node.GetChildren().OfType<GpuParticles3D>().ToList())
            {
                var at = trail.GlobalTransform;
                flight.Node.RemoveChild(trail);
                AddChild(trail);
                trail.GlobalTransform = at;
                trail.Emitting = false;
                _lingering.Add((trail, _clock + trail.Lifetime + 0.1, null));
            }
            flight.Node.QueueFree();
            // A little short of where it stopped, so the burst, which faces the camera, is not half inside the wall it struck.
            var along = (flight.To - flight.From).Normalized();
            Burst(flight.To - along * 0.4f, flight.EffectStem, along);
            if (flight.Node.HasMeta("streak"))
                _fadingStreaks.Add((WorkingTail(flight, flight.To), flight.To - along * 0.4f, _clock + TrailFade));
            return;
        }
        _fadingTrails.Add((Tail(flight, flight.To), flight.To, _clock + TrailFade));
        if (flight.Struck)
        {
            // Into the creature: gone, with a spark where it went in.
            flight.Node.QueueFree();
            Spark(flight.To);
            return;
        }
        // Stuck where it stopped - in a wall, or in the ground at the end of its range - until it fades.
        _lingering.Add((flight.Node, _clock + StuckSeconds, null));
    }

    private static Vector3 Tail(Flight flight, Vector3 head) =>
        head - (flight.To - flight.From).Normalized() * Math.Min(TrailLength, flight.From.DistanceTo(head));

    private const float WorkingStreakLength = 2.6f;
    private const float WorkingStreakWidth = 0.09f;
    private static readonly Color WorkingStreak = new(0.9f, 0.95f, 1f);
    // The wash round it a deeper blue than the day, so the white core reads against bright ground and sky.
    private static readonly Color WorkingWash = new(0.36f, 0.52f, 0.92f);
    private readonly List<(Vector3 Tail, Vector3 Head, double Until)> _fadingStreaks = new();

    private static Vector3 WorkingTail(Flight flight, Vector3 head) =>
        head - (flight.To - flight.From).Normalized() * Math.Min(WorkingStreakLength, flight.From.DistanceTo(head));

    /// <summary>
    /// A pale streak behind each arrow in flight, turned to face the camera, fading off once the arrow stops: from behind the shoulder an
    /// arrow flies straight away from the eye and is a speck without it. It is drawn only while a loosed arrow is in the air.
    /// </summary>
    private void DrawTrails()
    {
        var mesh = (ImmediateMesh)_trails.Mesh;
        mesh.ClearSurfaces();
        var streaks = new List<(Vector3 Tail, Vector3 Head, float Alpha, float Width, Color Colour)>();
        var arrow = new Color(1f, 0.97f, 0.88f);
        foreach (var flight in _flying.Where(f => !f.Working))
            streaks.Add((Tail(flight, flight.Node.Position), flight.Node.Position, 1f, TrailWidth, arrow));
        _fadingTrails.RemoveAll(t => t.Until <= _clock);
        foreach (var (tail, head, until) in _fadingTrails)
            streaks.Add((tail, head, (float)((until - _clock) / TrailFade), TrailWidth, arrow));
        // A working's streak (Phase B VFX lane): wider, cold, longer - its path through the air, drawn to where it burst.
        // A fine bright core inside a wide faint wash.
        void Streak(Vector3 tail, Vector3 head, float alpha)
        {
            streaks.Add((tail, head, 0.3f * alpha, WorkingStreakWidth * 2.2f, WorkingWash));
            streaks.Add((tail, head, 0.95f * alpha, WorkingStreakWidth * 0.55f, WorkingStreak));
        }
        foreach (var flight in _flying.Where(f => f.Working && f.Node.HasMeta("streak")))
            Streak(WorkingTail(flight, flight.Node.Position), flight.Node.Position, 1f);
        _fadingStreaks.RemoveAll(t => t.Until <= _clock);
        foreach (var (tail, head, until) in _fadingStreaks)
            Streak(tail, head, (float)((until - _clock) / TrailFade));
        if (GetViewport().GetCamera3D() is not { } camera)
            return;
        var eye = ToLocal(camera.GlobalPosition);
        streaks.RemoveAll(s => s.Tail.DistanceSquaredTo(s.Head) < 0.0001f);
        if (streaks.Count == 0)
            return;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        foreach (var (tail, head, alpha, width, tint) in streaks)
        {
            var side = (head - tail).Cross(eye - head).Normalized() * (width / 2);
            var bright = tint with { A = 0.95f * alpha };
            var clear = tint with { A = 0f };
            foreach (var (colour, at) in new[] { (clear, tail - side), (clear, tail + side), (bright, head + side), (clear, tail - side), (bright, head + side), (bright, head - side) })
            {
                mesh.SurfaceSetColor(colour);
                mesh.SurfaceAddVertex(at);
            }
        }
        mesh.SurfaceEnd();
    }

    private static void Face(Node3D node, Vector3 from, Vector3 to)
    {
        node.Position = from;
        if (from.DistanceSquaredTo(to) > 0.0001f)
            node.LookAtFromPosition(from, to, Mathf.Abs((to - from).Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

    /// <summary>The pipeline's arrow model (<c>art_bindings.json</c>'s <c>projectiles.arrow</c>), already pointing along -Z with its
    /// fletching at +Z (see <c>tools/asset_pipeline/_procgen_ammo.py</c>'s axis convention); a greybox shaft, head and fletches when it
    /// is not bound or will not load.</summary>
    private Node3D Arrow()
    {
        string? modelId = _bindings.Projectiles.GetValueOrDefault("arrow");
        if (modelId is not null && _art.Model(modelId) is { } model)
        {
            _coverage?.Resolved("projectile", "arrow", modelId);
            model.Name = "Arrow";
            return model;
        }
        _coverage?.Fallback("projectile", "arrow", modelId is null ? "no arrow binding: a greybox shaft, head and fletches"
            : $"{_art.Why(modelId) ?? "not drawn"}: a greybox shaft, head and fletches", modelId);
        var arrow = new Node3D { Name = "Arrow" };
        var shaft = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.75f },
            MaterialOverride = Palette.Shaft,
            RotationDegrees = new Vector3(90, 0, 0),
        };
        arrow.AddChild(shaft);
        arrow.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.028f, Height = 0.09f },
            MaterialOverride = Palette.Metal,
            RotationDegrees = new Vector3(-90, 0, 0),
            Position = new Vector3(0, 0, -0.41f),
        });
        for (int i = 0; i < 3; i++)
        {
            arrow.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.004f, 0.06f, 0.14f) },
                MaterialOverride = Palette.Fletching,
                Position = new Vector3(0, 0, 0.3f),
                RotationDegrees = new Vector3(0, 0, i * 120),
            });
        }
        return arrow;
    }

    /// <summary>A thrown working: the pipeline's travelling flipbook on a quad that faces the camera, or a glowing sphere; and its light.</summary>
    private Node3D Working(string? stem)
    {
        var node = new Node3D { Name = "Working" };
        if (stem is not null && Forms($"{stem}_travel") is { Count: > 0 } forms)
        {
            // Phase B VFX lane: the formula's own shape in flight (its core and the ring of air round it), the recipe's particles behind
            // it, and the streak DrawTrails lays along its path.
            foreach (var form in forms)
                node.AddChild(form);
            foreach (var particles in Recipes($"{stem}_travel"))
            {
                particles.Name = "Trail";
                node.AddChild(particles);
            }
            node.SetMeta("streak", true);
        }
        else if (Book(stem, "travel", "a glowing sphere") is { } book)
        {
            var trail = Recipes($"{stem}_travel");
            node.AddChild(Quad("Book", book, trail.Count > 0 ? 0.45f : 0.9f));
            node.SetMeta("book", $"{stem}_travel");
            foreach (var particles in trail)
            {
                particles.Name = "Trail";
                node.AddChild(particles);
            }
        }
        else
        {
            node.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f },
                MaterialOverride = new StandardMaterial3D { EmissionEnabled = true, Emission = new Color(0.55f, 0.75f, 1f), EmissionEnergyMultiplier = 3, AlbedoColor = new Color(0.7f, 0.85f, 1f) },
            });
        }
        node.AddChild(new OmniLight3D { LightColor = new Color(0.6f, 0.78f, 1f), LightEnergy = 1.6f, OmniRange = 4.5f });
        return node;
    }

    /// <summary>
    /// The working bursts where it stopped (<paramref name="along"/>: the way it flew): the recipes' pressure front and particles, the
    /// impact flipbook played once, or a swelling flash - and its light, fading.
    /// </summary>
    private void Burst(Vector3 at, string? stem, Vector3 along)
    {
        _lastBurst = _clock;
        var node = new Node3D { Position = at };
        AddChild(node);
        var light = new OmniLight3D { LightColor = new Color(0.6f, 0.78f, 1f), LightEnergy = 3f, OmniRange = 6f };
        node.AddChild(light);
        var forms = stem is null ? new List<Art.FormulaForm>() : Forms($"{stem}_impact");
        var bursts = stem is null ? new List<GpuParticles3D>() : Recipes($"{stem}_impact");
        if (stem is not null && (bursts.Count > 0 || forms.Count > 0))
        {
            foreach (var form in forms)
            {
                node.AddChild(form);
                if (form is Art.BoltShock shock)
                    shock.Along = along;
            }
            foreach (var burst in bursts)
            {
                node.AddChild(burst);
                Art.ParticleRecipes.Fire(burst);
            }
            double life = Math.Max(_recipes!.Lifetime(_recipes.For($"{stem}_impact")), forms.Count > 0 ? Art.BoltShock.Seconds : 0) + 0.2;
            // The light: a hard flash that falls away fast, not a glow that lingers.
            _lingering.Add((node, _clock + life, left => light.LightEnergy = (float)(3 * Math.Pow(Math.Max(0, left / life), 3))));
            return;
        }
        if (Book(stem, "impact", "a swelling flash") is { } book)
        {
            var quad = Quad("Book", book, 1.6f);
            node.AddChild(quad);
            double ttl = book.TtlSeconds ?? book.Frames / book.Fps;
            double start = _clock;
            _lingering.Add((node, _clock + ttl, left =>
            {
                Frame(quad, book, Math.Min(book.Frames - 1, (int)((_clock - start) * book.Fps)));
                light.LightEnergy = (float)Math.Max(0, 3 * left / ttl);
            }));
            return;
        }
        var flash = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.2f, Height = 0.4f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.7f, 0.85f, 1f, 0.8f),
            },
        };
        node.AddChild(flash);
        const double seconds = 0.35;
        _lingering.Add((node, _clock + seconds, left =>
        {
            float t = 1 - (float)(left / seconds);
            flash.Scale = Vector3.One * (1 + 5 * t);
            ((StandardMaterial3D)flash.MaterialOverride).AlbedoColor = new Color(0.7f, 0.85f, 1f, 0.8f * (1 - t));
            light.LightEnergy = 3 * (1 - t);
        }));
    }

    /// <summary>An arrow going into a creature: a short white spark.</summary>
    private void Spark(Vector3 at)
    {
        if (Recipes("arrow_strike") is { Count: > 0 } sparks)
        {
            var node = new Node3D { Position = at };
            AddChild(node);
            foreach (var burst in sparks)
            {
                node.AddChild(burst);
                Art.ParticleRecipes.Fire(burst);
            }
            _lingering.Add((node, _clock + _recipes!.Lifetime(_recipes.For("arrow_strike")) + 0.2, null));
            return;
        }
        _coverage?.Fallback("effect", "arrow_strike", "no strike flipbook: a white sphere for an eighth of a second");
        var spark = new MeshInstance3D
        {
            Position = at,
            Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 0.95f, 0.8f) },
        };
        AddChild(spark);
        _lingering.Add((spark, _clock + 0.12, null));
    }

    /// <summary>A camera-facing quad showing a flipbook's first frame (shared with the formulas' effects).</summary>
    public static MeshInstance3D Quad(string name, Flipbook book, float size)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = book.Additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            AlbedoTexture = book.Atlas,
            AlbedoColor = book.Tint * Mathf.Max(1f, book.Emission),
            Uv1Scale = new Vector3(1f / book.Columns, 1f / book.Rows, 1),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var quad = new MeshInstance3D { Name = name, Mesh = new QuadMesh { Size = new Vector2(size, size) }, MaterialOverride = material };
        quad.SetMeta("columns", book.Columns);
        quad.SetMeta("rows", book.Rows);
        Frame(quad, book, 0);
        return quad;
    }

    public static void Frame(MeshInstance3D quad, Flipbook book, int frame)
    {
        var material = (StandardMaterial3D)quad.MaterialOverride;
        material.Uv1Offset = new Vector3((float)(frame % book.Columns) / book.Columns, (float)(frame / book.Columns) / book.Rows, 0);
    }

    /// <summary>Play a travelling working's flipbook, looping at its rate.</summary>
    private void Animate(Node3D node, double elapsed)
    {
        if (node.GetNodeOrNull<MeshInstance3D>("Book") is not { } quad || node.GetMeta("book").AsString() is not { } id || _assets.Effect(id) is not { } book)
            return;
        int frame = (int)(elapsed * book.Fps);
        Frame(quad, book, book.Loop ? frame % book.Frames : Math.Min(frame, book.Frames - 1));
    }
}
