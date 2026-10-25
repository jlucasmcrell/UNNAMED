// UNNAMED Presentation - the opening at launch: a flight over the frontier from the last of the night into the start's morning, to the name
// Godot presentation only (D-11): a camera, a few lines of canon and the sky's clock; the world behind it is the one the start screen draws

using Godot;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The owner's opening (2026-10-24: "very in-your-face and grand and exciting - not just a panel off to the side"): full screen, letterboxed,
/// no HUD, before anything is chosen. Six shots through the real world while the sky runs from the night's galaxy and aurora to the
/// morning sun under the ring - the sky, the Foldscar's broken heart and its violet tear, Old Late, the Needle against the sunrise, the
/// Ashen Waystone waking cyan, then the road east - each with one line of canon, and on the score's hit the name over the start screen's
/// own view, which then simply carries on as the title. The shots are placed from the landmarks the world built (<see cref="Places"/>),
/// so the flight follows the world rather than numbers of its own. Skippable with any key once it has been seen through.
/// </summary>
public partial class OpeningCinematic : Node
{
    /// <summary>What the shots are framed on: the ground, and where the world put each landmark (feet and height, metres).</summary>
    public sealed record Places(Func<float, float, float> Ground, Vector3 Heart, Vector3 OldLate, float OldLateHeight, Vector3 Needle,
        float NeedleHeight, Vector3 Stone, float StoneHeight);

    private sealed record Shot(double From, double Until, Vector3[] Eyes, Vector3[] Looks, float FovFrom, float FovTo);

    private const double Hit = OpeningWords.Hit, End = OpeningWords.End;

    // The night the opening starts in and the morning it ends in (SkyView.ClockAtWorldStart, the start screen's hour).
    private const float NightHour = 5.0f;

    private const double DipSeconds = 0.3;

    // The sun clears the horizon (and SkyView takes its night fill away) inside the black dip at the cut to the Needle: the sky steps over
    // that much of the morning there, so the change of lights lands on a black frame (it cost one 128 ms frame at Ultra in the open).
    private const double Dawn = 31;
    private const float DawnHours = 0.35f;

    private readonly Places _at;
    private readonly Shot[] _shots;
    private readonly Camera3D _camera = new() { Name = "OpeningCamera", Far = 4000 };

    // The night has to read at once on any screen: the opening's own camera is exposed up at the start and back to the world's own by
    // the time the sky turns (the world's light and the title's camera are untouched).
    private readonly CameraAttributesPractical _exposure = new();
    private const float NightExposure = 2.6f;
    private const double ExposedUntil = 24;
    private CanvasLayer _layer = null!;
    private ColorRect _black = null!, _flash = null!, _top = null!, _bottom = null!;
    private Label _words = null!;
    private Control _name = null!;
    private int _line = -1;
    private double _clock = -1;
    private readonly OpeningClock _time = new();
    private int _warm;
    private bool _hit, _leaving;
    private double _leave;

    /// <summary>The first viewing plays through; after it any key skips.</summary>
    public bool Skippable { get; init; }

    /// <summary>Warmed under the loading screen (the opening draws nothing until then): ready to play from its first frame.</summary>
    public Action? Warmed { get; set; }

    /// <summary>Held on its first frame, warmed (a window recorder starting).</summary>
    public bool Holding { get; set; }

    /// <summary>The warm-up has begun: what is drawn now is under the opening's black (the caller builds the title there, so it opens without a hitch).</summary>
    public Action? Warming { get; set; }

    /// <summary>The first frame drawn: the score starts with it.</summary>
    public Action? Started { get; set; }

    /// <summary>A line's narration, by its voice id: true when a recording said it.</summary>
    public Func<string, bool>? Say { get; set; }

    /// <summary>The waystone in shot wakes (a pulse each time it is called).</summary>
    public Action? Pulse { get; set; }

    /// <summary>The hit: the name, over the start screen's own view, which the caller puts in place.</summary>
    public Action? Named { get; set; }

    /// <summary>Over (played through, or skipped): the title's entries come in.</summary>
    public Action<bool>? Done { get; set; }

    public OpeningCinematic(Places at)
    {
        _at = at;
        _shots = Shots(at);
    }

    /// <summary>A point <paramref name="height"/> metres over the ground at (x, z).</summary>
    private static Vector3 Over(Places at, float x, float z, float height) => new(x, at.Ground(x, z) + height, z);

    /// <summary>A point round <paramref name="centre"/>: <paramref name="bearing"/> degrees from east toward north, so many metres out and up.</summary>
    private static Vector3 Round(Places at, Vector3 centre, float bearing, float distance, float height)
    {
        float b = Mathf.DegToRad(bearing);
        return Over(at, centre.X + Mathf.Cos(b) * distance, centre.Z + Mathf.Sin(b) * distance, height);
    }

    private static Shot[] Shots(Places at)
    {
        var heart = at.Heart;
        var tear = heart + new Vector3(0, 30, 0);
        var stone = at.Stone;
        float s = at.StoneHeight;
        var road = Over(at, stone.X + 20, stone.Z - 8, 0);
        return new[]
        {
            // The sky: high over the hollow's rim, the last of the night - the galaxy and the aurora in the north (-Z, where the world's
            // pole stands: CelestialView) - tipping down across the land to the Foldscar.
            new Shot(0, 10, new[] { Over(at, stone.X + 38, stone.Z + 20, 34), Over(at, stone.X + 56, stone.Z + 12, 42) },
                new[] { Over(at, stone.X + 80, stone.Z - 300, 230), Over(at, stone.X + 110, stone.Z - 200, 110), Over(at, heart.X, heart.Z, 40) }, 56, 52),
            // The Foldscar: low at the basin's edge, looking up into its broken heart against the first light in the east - the 70 m of it
            // more than the frame holds, the tear in the gap at the frame's heart - closing in.
            new Shot(10, 22, new[] { Round(at, heart, 160, 92, 5), Round(at, heart, 178, 80, 4), Round(at, heart, 196, 68, 3) },
                new[] { tear + new Vector3(0, 8, 0), tear + new Vector3(0, 6, 0), tear + new Vector3(0, 4, 0) }, 54, 50),
            // Old Late, the kneeling stone giant: low at its knee, looking up past its sword to the crown slab that answers the wind late.
            new Shot(22, 31, new[] { Round(at, at.OldLate, -70, 50, 2), Round(at, at.OldLate, -80, 44, 2.5f), Round(at, at.OldLate, -92, 40, 4) },
                new[] { at.OldLate + new Vector3(0, at.OldLateHeight * 0.45f, 0), at.OldLate + new Vector3(0, at.OldLateHeight * 0.85f, 0) }, 62, 56),
            // The whole Needle over the Spill, the sun coming up beside it.
            new Shot(31, 41, new[] { Round(at, at.Needle, -165, 235, 30), Round(at, at.Needle, -150, 205, 26) },
                new[] { at.Needle + new Vector3(0, at.NeedleHeight * 0.45f, 0), at.Needle + new Vector3(0, at.NeedleHeight * 0.5f, 0) }, 40, 36),
            // The Ashen Waystone, close, waking: framed on the stone as drawn (2.4 m when this was shot; grand scale raises it).
            new Shot(41, 50, new[] { Round(at, stone, -28, 2.8f * s, 0.6f * s), Round(at, stone, -48, 2.3f * s, 0.7f * s), Round(at, stone, -68, 2.1f * s, 0.8f * s) },
                new[] { stone + new Vector3(0, 0.54f * s, 0), stone + new Vector3(0, 0.5f * s, 0) }, 44, 40),
            // The road east: up from the stone and out over the frontier toward the morning, faster and faster to the hit.
            new Shot(50, Hit, new[] { Over(at, road.X, road.Z, 16), Over(at, road.X + 30, road.Z - 10, 30), Over(at, road.X + 80, road.Z - 28, 60) },
                new[] { Over(at, road.X + 90, road.Z - 40, 6), Over(at, road.X + 260, road.Z - 70, 20), Over(at, at.Needle.X, at.Needle.Z, 40) }, 54, 62),
        };
    }

    public override void _Ready()
    {
        _camera.Attributes = _exposure;
        AddChild(_camera);
        _camera.MakeCurrent();
        StartCamera.Track(this, _camera);
        _layer = new CanvasLayer { Layer = 30, Visible = false };
        AddChild(_layer);
        // The title's theme, so the name it hands over is drawn exactly as the title draws it.
        var root = new Control { Theme = UiKit.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(root);
        // The bars: a 2.39:1 frame on a 16:9 screen is a ninth of the height above and below.
        _top = Bar(root, top: true);
        _bottom = Bar(root, top: false);
        _words = HudStyle.Text(HudStyle.Body, 34, HudStyle.Bone);
        _words.HorizontalAlignment = HorizontalAlignment.Center;
        _words.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _words.AddThemeConstantOverride("shadow_offset_x", 0);
        _words.AddThemeConstantOverride("shadow_offset_y", 2);
        _words.AddThemeConstantOverride("shadow_outline_size", 10);
        _words.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _words.AnchorTop = 0.74f;
        _words.AnchorBottom = 0.84f;
        _words.Modulate = new Color(1, 1, 1, 0);
        root.AddChild(_words);
        _name = TitleScreen.Wordmark();
        _name.Modulate = new Color(1, 1, 1, 0);
        root.AddChild(_name);
        _flash = new ColorRect { Color = new Color(1f, 0.97f, 0.9f, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_flash);
        _black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        _black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_black);
        Pose(0);
    }

    private static ColorRect Bar(Control root, bool top)
    {
        var bar = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (top)
            bar.AnchorBottom = 1f / 9;
        else
            bar.AnchorTop = 8f / 9;
        root.AddChild(bar);
        return bar;
    }

    // The warm-up, under the loading screen: every shot at its start, its middles and its end, at the hour it is seen at, two frames each -
    // so every material, the sky's night and morning and the tear's shader are built before the first frame anyone sees.
    private static readonly double[] WarmAt = { 0, 0.34, 0.67, 0.99 };

    public override void _Process(double delta)
    {
        if (_clock < 0)
        {
            int poses = _shots.Length * WarmAt.Length;
            if (_warm == 0)
                Warming?.Invoke();
            if (_warm < poses * 2)
            {
                var shot = _shots[_warm / 2 / WarmAt.Length];
                Pose(shot.From + (shot.Until - shot.From) * WarmAt[_warm / 2 % WarmAt.Length]);
                _warm++;
                return;
            }
            if (_warm == poses * 2)
            {
                _warm++;
                Pose(0);
                _layer.Visible = true;
                Warmed?.Invoke();
                return;
            }
            if (Holding)
                return;
            // The first frame after the warm-up: the opening starts with it, and so does its score.
            _time.Start(Now);
            Started?.Invoke();
            GetViewport().GuiReleaseFocus();
            if (DisplayServer.GetName() != "headless")
                Input.MouseMode = Input.MouseModeEnum.Hidden;
        }
        // Where the score is, not the sum of the frames: a slow machine drops frames and stays on the score (OpeningClock).
        var reached = _time.Advance(Now);
        _clock = _time.Seconds;
        if (_leaving)
        {
            _leave += delta;
            _black.Color = new Color(0, 0, 0, (float)Math.Min(1, _leave / 0.4));
            if (_leave >= 0.45)
                Finish(skipped: true);
            return;
        }
        if (_clock < Hit)
            Pose(_clock);
        bool over = false;
        foreach (var (cue, index) in reached)
        {
            switch (cue)
            {
                case OpeningCue.Line:
                    Say?.Invoke(OpeningWords.Lines[index].Voice);
                    break;
                case OpeningCue.Pulse:
                    Pulse?.Invoke();
                    break;
                case OpeningCue.Hit:
                    _hit = true;
                    Named?.Invoke();
                    break;
                case OpeningCue.End:
                    over = true;
                    break;
            }
        }
        Overlay(_clock);
        if (over)
            Finish(skipped: false);
    }

    /// <summary>Seconds on the engine's steady clock.</summary>
    private static double Now => Time.GetTicksUsec() / 1_000_000.0;

    /// <summary>The camera and the sky at a moment before the hit.</summary>
    private void Pose(double t)
    {
        var shot = _shots.LastOrDefault(s => s.From <= t) ?? _shots[0];
        float u = (float)Math.Clamp((t - shot.From) / (shot.Until - shot.From), 0, 1);
        // Never quite still: a gentle ease at each end of a shot, the first shot's start and the last's run into the hit eased the most.
        float eased = shot == _shots[^1] ? u * u * (1.4f - 0.4f * u) : Mathf.Lerp(u, u * u * (3 - 2 * u), shot == _shots[0] ? 0.8f : 0.35f);
        var eye = Along(shot.Eyes, eased);
        var look = Along(shot.Looks, eased);
        _camera.Fov = Mathf.Lerp(shot.FovFrom, shot.FovTo, eased);
        _camera.LookAtFromPosition(eye, look, Vector3.Up);
        float exposed = (float)Math.Clamp(t / ExposedUntil, 0, 1);
        _exposure.ExposureMultiplier = Mathf.Lerp(NightExposure, 1, exposed * exposed * (3 - 2 * exposed));
        float day = (float)Math.Clamp(t / Hit, 0, 1);
        float dawn = (float)Math.Clamp((t - (Dawn - DipSeconds)) / (2 * DipSeconds), 0, 1);
        SkyView.Show(Mathf.Lerp(NightHour, SkyView.ClockAtWorldStart - DawnHours, day * day * (3 - 2 * day)) + DawnHours * dawn);
    }

    /// <summary>Catmull-Rom through the keys (two keys: a straight line), at 0-1 along them.</summary>
    private static Vector3 Along(Vector3[] keys, float u)
    {
        if (keys.Length == 1)
            return keys[0];
        float span = u * (keys.Length - 1);
        int i = Math.Min((int)span, keys.Length - 2);
        float f = span - i;
        var p0 = keys[Math.Max(i - 1, 0)];
        var p3 = keys[Math.Min(i + 2, keys.Length - 1)];
        return keys[i].CubicInterpolate(keys[i + 1], p0, p3, f);
    }

    private void Overlay(double t)
    {
        // Black at the start, then a short dip at every cut between shots.
        float black = (float)Math.Clamp(1 - (t - 0.2) / 1.2, 0, 1);
        foreach (var shot in _shots.Skip(1))
            black = Math.Max(black, (float)Math.Clamp(1 - Math.Abs(t - shot.From) / DipSeconds, 0, 1));
        _black.Color = new Color(0, 0, 0, t < Hit ? black : 0);
        Words(t);
        // The hit: a flash, the name in, slightly large, settling; the bars open and the name rises to the title's place.
        double since = t - Hit;
        _flash.Color = new Color(_flash.Color, since < 0 ? (float)Math.Clamp((t - (Hit - 0.5)) / 0.5, 0, 1) * 0.55f
            : (float)Math.Clamp(0.9 * (1 - since / 1.3), 0, 1));
        if (since < 0)
            return;
        // The bars are gone before the name rises, so it never crosses them.
        float rise = (float)Math.Clamp((since - 2.7) / 1.8, 0, 1);
        rise = rise * rise * (3 - 2 * rise);
        float settle = (float)Math.Clamp(since / 0.9, 0, 1);
        float scale = Mathf.Lerp(1.22f, 1.0f, 1 - (1 - settle) * (1 - settle) * (1 - settle));
        _name.Modulate = new Color(1, 1, 1, (float)Math.Clamp(since / 0.12, 0, 1));
        _name.PivotOffset = _name.Size / 2;
        _name.Scale = new Vector2(scale, scale) * Mathf.Lerp(TitleScreen.RevealScale, 1, rise);
        float height = _name.GetParentControl().Size.Y;
        _name.Position = new Vector2(_name.Position.X, (TitleScreen.MarkTop + Mathf.Lerp(TitleScreen.RevealDrop, 0, rise)) * height);
        float bars = (float)Math.Clamp((since - 1.2) / 1.4, 0, 1);
        _top.Position = new Vector2(0, -_top.Size.Y * bars * bars);
        _bottom.Position = new Vector2(0, height * 8 / 9 + _bottom.Size.Y * bars * bars);
    }

    private void Words(double t)
    {
        int line = Array.FindIndex(OpeningWords.Lines, l => t >= l.From && t < l.Until);
        if (line != _line && line >= 0)
            _words.Text = OpeningWords.Lines[line].Words;
        _line = line;
        if (line < 0)
        {
            _words.Modulate = new Color(1, 1, 1, 0);
            return;
        }
        var l = OpeningWords.Lines[line];
        float fade = (float)Math.Min(Math.Clamp((t - l.From) / 0.9, 0, 1), Math.Clamp((l.Until - t) / 0.9, 0, 1));
        _words.Modulate = new Color(1, 1, 1, fade);
    }

    public override void _Input(InputEvent @event)
    {
        if (!Skippable || _clock < 0 || _leaving || _clock >= End || !@event.IsPressed() || @event.IsEcho())
            return;
        if (@event is not (InputEventKey or InputEventMouseButton or InputEventJoypadButton))
            return;
        GetViewport().SetInputAsHandled();
        _leaving = true;
    }

    /// <summary>Leave immediately when another front-end or world takes ownership.</summary>
    public void Dismiss()
    {
        SetProcess(false);
        SetProcessInput(false);
        if (_layer is not null) _layer.Visible = false;
        _camera.Current = false;
        SkyView.Show(SkyView.ClockAtWorldStart);
        QueueFree();
    }

    private void Finish(bool skipped)
    {
        if (IsQueuedForDeletion())
            return;
        // Skipped before the hit: the start screen's view goes in place first.
        if (!_hit)
        {
            _hit = true;
            Named?.Invoke();
        }
        Dismiss();
        Done?.Invoke(skipped);
    }
}
