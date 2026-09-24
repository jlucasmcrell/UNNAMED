// UNNAMED Presentation - the heading compass (content bible §22; the owner's M6 playtest)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Heading only (content bible §22: "prefer a simple compass/heading cue over an omniscient minimap"): a dial that turns so its north
/// points north, the cardinal letters on it, a fixed mark for where the view faces, and the bearing in words and degrees. It shows no
/// place, creature, container or resource. The dial is the asset pipeline's <c>ui.hud.compass</c> when the workspace is present -
/// its face, cut out of the icon's painted background - and a drawn one otherwise.
/// </summary>
public partial class Compass : Control
{
    public const float Diameter = 84f;

    private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    private readonly TextureRect _dial = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
    private readonly Marks _marks = new();
    private readonly Label _bearing = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private float _heading = float.NaN;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Diameter, Diameter + 26);
        _dial.Size = new Vector2(Diameter, Diameter);
        _dial.PivotOffset = new Vector2(Diameter / 2, Diameter / 2);
        _dial.Material = new ShaderMaterial
        {
            Shader = new Shader
            {
                // The icon is painted on an opaque background: only the round face is shown.
                Code = "shader_type canvas_item;\nvoid fragment() {\n  vec4 c = texture(TEXTURE, UV);\n" +
                       "  c.a *= 1.0 - smoothstep(0.47, 0.5, distance(UV, vec2(0.5)));\n  COLOR = c;\n}\n",
            },
        };
        AddChild(_dial);
        _marks.Size = new Vector2(Diameter, Diameter);
        AddChild(_marks);
        _bearing.Position = new Vector2(-20, Diameter + 2);
        _bearing.Size = new Vector2(Diameter + 40, 22);
        _bearing.AddThemeFontSizeOverride("font_size", 17);
        _bearing.AddThemeColorOverride("font_outline_color", Colors.Black);
        _bearing.AddThemeConstantOverride("outline_size", 4);
        AddChild(_bearing);
    }

    /// <summary>Use the pipeline's dial: its face (the round, needle-up part of the icon) cut out of the picture around it.</summary>
    public void UseDial(Texture2D? icon)
    {
        if (icon?.GetImage() is not { } image)
            return;
        int size = image.GetWidth();
        // The face sits a little below the icon's middle, under the ring it hangs from: centre (0.5, 0.545), radius 0.335 of the width.
        int radius = (int)(size * 0.335f), x = size / 2 - radius, y = (int)(size * 0.545f) - radius;
        _dial.Texture = ImageTexture.CreateFromImage(image.GetRegion(new Rect2I(x, y, 2 * radius, 2 * radius)));
        _marks.DrawnDial = false;
    }

    /// <summary>Where the view faces, in degrees clockwise from north.</summary>
    public void SetHeading(float degrees)
    {
        degrees = Mathf.PosMod(degrees, 360f);
        if (!float.IsNaN(_heading) && Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(_heading), Mathf.DegToRad(degrees))) < 0.002f)
            return;
        _heading = degrees;
        float turn = -Mathf.DegToRad(degrees);
        _dial.Rotation = turn;
        _marks.Turn = turn;
        _marks.QueueRedraw();
        _bearing.Text = $"{Points[(int)Mathf.PosMod(Mathf.Round(degrees / 45f), 8)]}  {Mathf.RoundToInt(degrees) % 360:000}°";
    }

    /// <summary>The letters, the drawn dial when there is no art, and the fixed mark at the top.</summary>
    private sealed partial class Marks : Control
    {
        public float Turn { get; set; }

        public bool DrawnDial { get; set; } = true;

        public override void _Draw()
        {
            var centre = Size / 2;
            float radius = Diameter / 2;
            if (DrawnDial)
            {
                DrawCircle(centre, radius, new Color(0.12f, 0.11f, 0.1f, 0.85f));
                DrawArc(centre, radius - 1, 0, Mathf.Tau, 48, new Color(0.75f, 0.7f, 0.6f), 2);
                var north = centre + Vector2.Up.Rotated(Turn) * (radius * 0.7f);
                DrawLine(centre, north, new Color(0.85f, 0.25f, 0.2f), 3);
                DrawLine(centre, centre - (north - centre), new Color(0.8f, 0.8f, 0.8f), 3);
            }
            var font = ThemeDB.FallbackFont;
            string[] letters = { "N", "E", "S", "W" };
            for (int i = 0; i < 4; i++)
            {
                var at = centre + Vector2.Up.Rotated(Turn + i * Mathf.Pi / 2) * (radius * 0.72f);
                var colour = i == 0 ? new Color(1f, 0.45f, 0.35f) : new Color(0.95f, 0.92f, 0.85f);
                // Centred by its measured width: a fixed box narrower than the letter drops it (the W).
                float width = font.GetStringSize(letters[i], HorizontalAlignment.Left, -1, 15).X;
                DrawString(font, at + new Vector2(-width / 2, 6), letters[i], HorizontalAlignment.Left, -1, 15, colour);
            }
            // The view's mark: fixed at the top, where the camera faces.
            DrawColoredPolygon(new[] { centre + new Vector2(-6, -radius - 7), centre + new Vector2(6, -radius - 7), centre + new Vector2(0, -radius + 3) },
                new Color(0.95f, 0.85f, 0.4f));
        }
    }
}
