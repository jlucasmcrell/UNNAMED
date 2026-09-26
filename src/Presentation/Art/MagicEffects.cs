// UNNAMED Presentation - the formulas' effects from the asset pipeline: the cast charge, what a working leaves on the body, the Strain overlay
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The effect manifest's flipbooks (<c>manifests/magic_vfx.json</c>), bound through the art bindings' <c>effects</c>: the cast charge in
/// the casting hand from a working's windup to its release; on the body, for as long as the simulation's effect lasts, whatever a working
/// left there (the Brace Ward's shell, the Mending Thread's restore) - a looping book loops, one that plays once then fades; and the Strain
/// overlay over the screen at the manifest's curve, pulsing as Strain nears the tolerance. Without the workspace nothing here draws and
/// the greybox tells stand (the glow in the hand, the Strain bar). When the run draws with the particle recipes (Phase B), the recipes'
/// effects map stands particles and formula forms (<see cref="FormulaForm"/>) in for a flipbook: the ward's lattice, the mending's
/// threads, the cast's glyph.
/// </summary>
public partial class MagicEffects : Node3D
{
    private const string CastCharge = "cast_charge";
    private const string Strain = "strain";
    private const float BodyEffectSize = 2.1f;
    private const float BodyEffectHeight = 0.95f;

    private readonly Dictionary<string, (MeshInstance3D Quad, Flipbook Book, double Since)> _onBody = new(StringComparer.Ordinal);
    // Phase B remediation: particle recipes (and formula forms) standing in for a body effect's flipbook, when the run draws with recipes.
    private readonly Dictionary<string, (Node3D Holder, float Lifetime)> _recipesOnBody = new(StringComparer.Ordinal);
    private ParticleRecipes? _recipes;
    private readonly TextureRect _overlay = new()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        Visible = false,
    };
    private AssetCatalog _assets = AssetCatalog.Empty;
    private ArtBindings _bindings = ArtBindings.Empty;
    private MeshInstance3D? _charge;
    private Flipbook? _chargeBook;
    private double _chargeSince;
    private double _clock;
    private CastGlyph? _glyph;
    private bool _glyphForm;

    public void Bind(AssetCatalog assets, ArtBindings bindings, ArtCoverage? coverage = null)
    {
        _assets = assets;
        _bindings = bindings;
        _coverage = coverage;
        _chargeBook = Book(CastCharge);
        _recipes = VisualOptions.Recipes ? ParticleRecipes.Load(assets.Root) : null;
        _glyphForm = _bindings.Effects.TryGetValue(CastCharge, out string? charge) && _recipes?.For(charge).Contains("form:cast_glyph") == true;
        if (_glyphForm)
            _coverage?.Resolved("effect_recipe", CastCharge, "form:cast_glyph");
    }

    private ArtCoverage? _coverage;

    /// <summary>Whether the cast charge is drawn (the greybox glow then steps aside).</summary>
    public bool HasCastCharge => _chargeBook is not null || _glyphForm;

    public override void _Ready()
    {
        var layer = new CanvasLayer { Layer = 0, Name = "StrainOverlay" };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (Book(Strain) is { } overlay)
            _overlay.Texture = overlay.Atlas;
        layer.AddChild(_overlay);
        AddChild(layer);
    }

    /// <summary>
    /// This frame's workings on <paramref name="body"/>: the formula being cast and the combat phase, the effects on the body by their IDs
    /// with the seconds each has left, and Strain as a share of the tolerance.
    /// </summary>
    public void Draw(Figure body, string? casting, CombatPhase phase, IEnumerable<(string Id, double Left)> effects, double strain, double delta)
    {
        _clock += delta;
        DrawCharge(body, casting, casting is not null && phase == CombatPhase.Windup);
        DrawOnBody(body, effects.ToList());
        DrawStrain(strain);
    }

    /// <summary>A blow landed on the body from <paramref name="from"/>: the forms on it answer (the ward's shell rings where it was struck).</summary>
    public void Struck(Vector3 from)
    {
        foreach (var (holder, _) in _recipesOnBody.Values)
        {
            foreach (var form in holder.GetChildren().OfType<FormulaForm>())
                form.Struck(from);
        }
    }

    private void DrawCharge(Figure body, string? casting, bool gathering)
    {
        if (_glyphForm)
        {
            DrawGlyph(body, casting, gathering);
            return;
        }
        if (_chargeBook is not { } book || !gathering || body.CastPoint is not { } hand)
        {
            if (_charge is not null)
                _charge.Visible = false;
            return;
        }
        if (_charge is null)
        {
            _charge = ProjectilesView.Quad("CastCharge", book, 0.55f);
            AddChild(_charge);
        }
        if (!_charge.Visible)
        {
            _charge.Visible = true;
            _chargeSince = _clock;
        }
        _charge.GlobalPosition = hand;
        // Once through, then held on its last frame until the working is released.
        ProjectilesView.Frame(_charge, book, Math.Min(book.Frames - 1, (int)((_clock - _chargeSince) * book.Fps)));
    }

    /// <summary>
    /// The cast glyph before the casting hand while the formula gathers, in its formula's ink and polygon (the recipes' <c>formulas</c>);
    /// on the release - or the working broken off - it flashes once and goes.
    /// </summary>
    private void DrawGlyph(Figure body, string? casting, bool gathering)
    {
        if (gathering && body.CastPoint is { } hand)
        {
            if (_glyph is null)
            {
                _glyph = new CastGlyph();
                var (ink, sides) = _recipes!.Formula(casting!);
                _glyph.Ink = ink;
                _glyph.Sides = sides;
                AddChild(_glyph);
            }
            var forward = body.GlobalTransform.Basis.Z with { Y = 0 };
            forward = forward.LengthSquared() > 0.0001f ? forward.Normalized() : Vector3.Forward;
            _glyph.GlobalPosition = hand + forward * 0.22f;
            _glyph.GlobalBasis = Basis.LookingAt(forward, Vector3.Up);
            return;
        }
        if (_glyph is not { } glyph)
            return;
        _glyph = null;
        Release(glyph, glyph.End());
    }

    /// <summary>Free a form once its ending has played.</summary>
    private void Release(Node node, double after) => GetTree().CreateTimer(after + 0.05).Timeout += () =>
    {
        if (IsInstanceValid(node))
            node.QueueFree();
    };

    private void DrawOnBody(Figure body, IReadOnlyList<(string Id, double Left)> effects)
    {
        var active = effects.Where(e => _bindings.Effects.ContainsKey(e.Id)).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string gone in _onBody.Keys.Where(k => !active.Contains(k)).ToList())
        {
            _onBody[gone].Quad.QueueFree();
            _onBody.Remove(gone);
        }
        foreach (string gone in _recipesOnBody.Keys.Where(k => !active.Contains(k)).ToList())
        {
            // The working has ended: no new particles, the forms play their endings, and the last particles finish their lives before
            // the emitters go.
            var (holder, lifetime) = _recipesOnBody[gone];
            _recipesOnBody.Remove(gone);
            foreach (var particles in holder.GetChildren().OfType<GpuParticles3D>())
                particles.Emitting = false;
            double ending = holder.GetChildren().OfType<FormulaForm>().Select(f => f.End()).DefaultIfEmpty(0).Max();
            Release(holder, Math.Max(lifetime + 0.15, ending));
        }
        foreach (var (effect, left) in effects.Where(e => active.Contains(e.Id)))
        {
            if (RecipesOnBody(effect) is { } recipes)
            {
                recipes.GlobalPosition = body.GlobalPosition;
                foreach (var form in recipes.GetChildren().OfType<FormulaForm>())
                    form.SecondsLeft = left;
                continue;
            }
            if (!_onBody.TryGetValue(effect, out var shown))
            {
                if (Book(effect) is not { } book)
                    continue;
                var quad = ProjectilesView.Quad(effect.Replace('.', '_'), book, BodyEffectSize);
                AddChild(quad);
                shown = _onBody[effect] = (quad, book, _clock);
            }
            shown.Quad.GlobalPosition = body.GlobalPosition + new Vector3(0, BodyEffectHeight, 0);
            var b = shown.Book;
            int frame = (int)((_clock - shown.Since) * b.Fps);
            ProjectilesView.Frame(shown.Quad, b, b.Loop ? frame % b.Frames : Math.Min(frame, b.Frames - 1));
            if (!b.Loop && frame >= b.Frames && b.TtlSeconds is { } ttl)
            {
                // Played once: the last frame fades out over what is left of the book's lifetime.
                double played = b.Frames / b.Fps;
                float fading = (float)Math.Clamp(1 - (_clock - shown.Since - played) / Math.Max(0.01, ttl - played), 0, 1);
                var material = (StandardMaterial3D)shown.Quad.MaterialOverride;
                material.AlbedoColor = material.AlbedoColor with { A = fading };
            }
        }
    }

    /// <summary>The recipes and forms standing in for a body effect's flipbook (at the body's feet), built the first time; null when none do.</summary>
    private Node3D? RecipesOnBody(string effect)
    {
        if (_recipesOnBody.TryGetValue(effect, out var shown))
            return shown.Holder;
        if (_recipes is null || !_bindings.Effects.TryGetValue(effect, out string? id) || _recipes.For(id) is not { Count: > 0 } names)
            return null;
        var holder = new Node3D { Name = effect.Replace('.', '_') + "_recipes" };
        foreach (string name in names)
        {
            if (FormulaForm.Create(name) is { } form)
                holder.AddChild(form);
            else if (_recipes.Build(name) is { } particles)
                holder.AddChild(particles);
        }
        if (holder.GetChildCount() == 0)
        {
            holder.Free();
            return null;
        }
        AddChild(holder);
        _recipesOnBody[effect] = (holder, _recipes.Lifetime(names));
        if (_asked.Add(effect))
            _coverage?.Resolved("effect_recipe", effect, string.Join(" + ", names));
        return holder;
    }

    private void DrawStrain(double strain)
    {
        var curve = _assets.StrainOverlay;
        if (_overlay.Texture is null || curve.Count == 0)
            return;
        double alpha = curve[0].Alpha;
        for (int i = 1; i < curve.Count; i++)
        {
            if (strain <= curve[i].Strain)
            {
                var (s0, a0) = curve[i - 1];
                var (s1, a1) = curve[i];
                alpha = a0 + (a1 - a0) * Math.Clamp((strain - s0) / Math.Max(1e-6, s1 - s0), 0, 1);
                break;
            }
            alpha = curve[i].Alpha;
        }
        // The manifest's pulse: none below 0.65 of tolerance, growing to its full rate's swing at the tolerance.
        double pulse = strain > 0.65 ? Math.Min(1, (strain - 0.65) / 0.35) * 0.25 * Math.Sin(_clock * Mathf.Tau * _assets.StrainPulseHz) : 0;
        float shown = (float)Math.Clamp(alpha * (1 + pulse), 0, 1);
        _overlay.Visible = shown > 0.001f;
        _overlay.Modulate = new Color(1, 1, 1, shown);
    }

    /// <summary>An effect's flipbook by its binding key, recorded in the coverage report the first time it is asked for.</summary>
    private Flipbook? Book(string key)
    {
        var book = _bindings.Effects.TryGetValue(key, out string? id) ? _assets.Effect(id) : null;
        if (_asked.Add(key))
        {
            if (book is not null)
                _coverage?.Resolved("effect", key, id!);
            else
                _coverage?.Fallback("effect", key, id is null ? "no effect bound: the greybox tell" : "its flipbook is not in the effect manifest, or would not load: the greybox tell", id);
        }
        return book;
    }

    private readonly HashSet<string> _asked = new(StringComparer.Ordinal);
}
