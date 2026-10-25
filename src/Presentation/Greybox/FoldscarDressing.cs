// UNNAMED Presentation - the Foldscar's ground: the heart's missing half, the break's debris, and stones that do not quite rest (Phase B, B9; grand scale 2026-10-24)
// Godot presentation only (D-11): non-colliding dressing placed by a seeded rule; nothing here is read by the game

using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The Foldscar at the scale of its story (<c>--visual foldscar=proof</c>; GRAND_SCALE_PROPOSAL.md 2.1, GS-2). The heart is the lower 24 m
/// of a broken dressed monolith; its missing upper 34 m hangs upside down over it, the break not lining up, with violet fluorite grown out of
/// both faces of the break and a violet tear in the gap that bends what is seen through it and casts no light of its own. The break's debris
/// hangs round it where it stopped falling - slabs the size of a house down to fist-sized chips - turning slowly, as if not quite stopped;
/// more lies half-buried round the ring, the ground under it is cracked and glassed, and dust drifts upward. Everything resting on nothing
/// comes down when the heart is steadied (FoldscarRegister); the upper half does not. Nothing here collides, nothing is placed in the fold.
/// </summary>
public static class FoldscarDressing
{
    private const string Heart = "gs_foldscar_heart";                       // Frozen Norse SM_Giant_Column at 24 m
    private const string Fluorite = "gs_fold_fluorite";                     // a CC0 fluorite scan, 3 m as converted
    private static readonly string[] Shards = { "gs_foldscar_shard_a", "gs_foldscar_shard_b", "gs_foldscar_shard_c" };
    private const string Wreck = "gs_foldscar_slab_a";                      // an 18.9 m broken run of carved masonry
    // The rest of the break, never the same piece twice running: carved masonry (Frozen Norse, the stones' own kit) and scanned fractured
    // rock (Cosmos crags and boulders, Poly Haven stones), each in its own material, with the size range each reads well at.
    private static readonly (string Id, float Min, float Max, bool Carved)[] Debris =
    {
        ("gs_foldscar_shard_a", 0.7f, 1.8f, true), ("rock_cos_crag_a", 0.25f, 0.5f, false), ("gs_foldscar_shard_b", 0.7f, 1.8f, true),
        ("landmark_cos_foldscar_monolith_a", 0.8f, 2.2f, false), ("gs_foldscar_shard_c", 0.7f, 1.8f, true), ("rock_cos_rim_boulder_a", 0.5f, 1.4f, false),
        ("gs_quiet_stone_b", 0.18f, 0.35f, true), ("landmark_ph_quiet_stone_a", 0.8f, 2.0f, false), ("rock_cos_crag_b", 0.25f, 0.5f, false),
        ("rock_ph_ironstone_boulder_a", 0.6f, 1.6f, false), ("landmark_ph_quiet_stone_b", 0.8f, 2.0f, false), ("rock_cos_rim_boulder_b", 0.5f, 1.4f, false),
        ("landmark_ph_quiet_stone_c", 0.8f, 2.0f, false), ("rock_ph_beam_pillar_a", 0.6f, 1.4f, false),
    };
    private const string Pebble = "env_ph_boulder_01";
    private const float HeartHeight = 24f;
    private const float UpperScale = 1.4f;                                 // the missing 34 m: the half that broke off was the greater
    private const float Gap = 12f;                                         // between the break and the hanging half; the top stands at ~70 m

    /// <summary>
    /// Place the dressing; the number of pieces placed. <paramref name="register"/>, when given, receives what the heart's release moves:
    /// the hanging half (doubled with the heart), every piece resting on nothing with the height it would rest at and its slow turn, the
    /// tear, the motes.
    /// </summary>
    public static int Dress(Node3D parent, ArtLibrary art, GroundField ground, Vector2 heart, Vector2 fold, float foldRadius,
        FoldscarRegister? register = null)
    {
        var root = new Node3D { Name = "FoldscarDressing" };
        parent.AddChild(root);
        var random = new RandomNumberGenerator { Seed = 0xF01D5CA7 };
        // The heart's dark polished stone, satin rather than wet: without the onyx's own gloss map a carved normal under the noon sun read
        // as crumpled foil at close range.
        var onyx = art.WorldMaps("mat_acg_onyx015") is { } polished ? polished with { Orm = null } : null;
        var dark = new Color(0.09f, 0.09f, 0.12f);
        float floor = ground.Height(heart.X, heart.Y);
        float breakY = floor + HeartHeight;
        var resting = new List<(Node3D Piece, float Settled)>();
        int placed = 0;
        // The lower tiers keep the heart, the stones and the tear whole and thin only what is many: the debris, the motes, the sparks.
        float share = VisualOptions.Tier switch { "low" => 0.4f, "medium" => 0.7f, _ => 1f };
        int Many(int count) => Math.Max(1, (int)MathF.Round(count * share));

        Vector2? Spot(float from, float to)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float angle = random.RandfRange(0, Mathf.Tau), distance = random.RandfRange(from, to);
                var p = heart + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                if (p.DistanceTo(fold) < foldRadius + 1.5f || ground.PathAt(p.X, p.Y) > 0.15f || !ground.Region.HasPoint(p))
                    continue;
                return p;
            }
            return null;
        }

        void Wear(Node3D piece)
        {
            if (onyx is not null)
                Palette.Wear(piece, onyx, dark, Colors.Black, 0f);
        }

        Vector3 Axis() => new Vector3(random.RandfRange(-1, 1), random.RandfRange(-1, 1), random.RandfRange(-1, 1)).Normalized();

        // Fluorite grown out of a face of the break, pointing along it.
        void Crystal(Node3D at, Vector3 offset, float scale, Vector3 tilt)
        {
            if (art.ModelWithLods(Fluorite) is not { } crystal)
                return;
            crystal.Position = offset;
            crystal.RotationDegrees = tilt;
            crystal.Scale = Vector3.One * scale;
            Inward(crystal);
            at.AddChild(crystal);
            placed++;
        }

        // The fluorite shows the fold's violet in its own body and nowhere else (owner, 2026-10-24: the fold casts no light of its own): its
        // colour comes up faintly from within, and it is kept out of the global illumination so nothing round it is lit by it.
        static void Inward(Node3D crystal)
        {
            foreach (var mesh in crystal.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
            {
                mesh.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
                for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                {
                    if (mesh.GetActiveMaterial(s) is not StandardMaterial3D own)
                        continue;
                    var inner = (StandardMaterial3D)own.Duplicate();
                    inner.EmissionEnabled = true;
                    inner.Emission = new Color(0.55f, 0.28f, 1f);
                    inner.EmissionTexture = own.AlbedoTexture;
                    inner.EmissionEnergyMultiplier = 2.2f;
                    mesh.SetSurfaceOverrideMaterial(s, inner);
                }
            }
        }

        // The lower half is the structure the library draws; it wears the same stone as everything broken off it.
        if (parent is HollowView view && view.PlacedModel("rock_foldscar_heart") is { } lower)
            Wear(lower);

        // The missing half, upside down over the break and turned off it, so the two never line up.
        if (art.ModelWithLods(Heart) is { } upper)
        {
            upper.Scale = Vector3.One * UpperScale;
            upper.RotationDegrees = new Vector3(180, 0, 4);
            var hanging = new Node3D { Name = "HeartUpperHalf", Position = new Vector3(heart.X + 1.2f, breakY + Gap + HeartHeight * UpperScale, heart.Y + 0.8f),
                RotationDegrees = new Vector3(0, 25, 0) };
            hanging.AddChild(upper);
            root.AddChild(hanging);
            Wear(upper);
            register?.AddHanging(hanging);
            placed++;
            // Its broken face is its lowest; the crystal there hangs downward.
            Crystal(hanging, new Vector3(0.6f, -HeartHeight * UpperScale - 0.6f, -0.4f), 1.6f, new Vector3(170, 40, 8));
            Crystal(hanging, new Vector3(-1.8f, -HeartHeight * UpperScale + 0.4f, 1.2f), 1.1f, new Vector3(150, 200, -20));

            // The tear in the gap: what is seen through it bent and edged in violet, with a seam of violet down its middle. It lights nothing.
            var tear = new Node3D { Name = "HeartTear", Position = new Vector3(heart.X + 0.6f, breakY + Gap / 2, heart.Y + 0.4f) };
            var bend = new MeshInstance3D
            {
                Name = "Bend", Mesh = new QuadMesh { Size = new Vector2(14f, Gap + 4f) },
                MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = BendCode } },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            var seam = new MeshInstance3D
            {
                Name = "Seam", Mesh = new QuadMesh { Size = new Vector2(4f, Gap + 7f) },
                MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = SeamCode } },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            // A haze of violet hanging in the gap and spilling a little past it, glowing in itself as a mist does at dusk; it lights nothing.
            var haze = new MeshInstance3D
            {
                Name = "Haze", Mesh = new QuadMesh { Size = new Vector2(18f, Gap + 16f) },
                MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = HazeCode } },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            tear.AddChild(haze);
            tear.AddChild(bend);
            tear.AddChild(seam);
            root.AddChild(tear);
            register?.AddTear((ShaderMaterial)bend.MaterialOverride, (ShaderMaterial)seam.MaterialOverride, (ShaderMaterial)haze.MaterialOverride);
            // Sparks of the same violet, rising slowly out of the gap and drifting off it.
            var sparks = new GpuParticles3D
            {
                Name = "TearSparks", Amount = Many(260), Lifetime = 9, Preprocess = 9, Position = tear.Position,
                VisibilityAabb = new Aabb(new Vector3(-14, -12, -14), new Vector3(28, 40, 28)),
                ProcessMaterial = new ParticleProcessMaterial
                {
                    EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(3, Gap / 2, 3),
                    Gravity = new Vector3(0, 0.25f, 0), InitialVelocityMin = 0.2f, InitialVelocityMax = 0.9f, Direction = Vector3.Up, Spread = 70,
                    TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.8f, TurbulenceNoiseScale = 4, ScaleMin = 0.5f, ScaleMax = 1.4f,
                    ColorRamp = new GradientTexture1D { Gradient = new Gradient { Offsets = new[] { 0f, 0.15f, 0.7f, 1f },
                        Colors = new[] { new Color(1, 1, 1, 0), Colors.White, Colors.White, new Color(1, 1, 1, 0) } } },
                },
                DrawPass1 = new QuadMesh
                {
                    Size = new Vector2(0.16f, 0.16f),
                    Material = new StandardMaterial3D
                    {
                        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true, AlbedoColor = new Color(2.2f, 1.1f, 3.6f),
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    },
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            root.AddChild(sparks);
            register?.AddMotes(sparks);
        }
        // The lower half's broken face, at the top of the heart the structure draws.
        var breakFace = new Node3D { Name = "HeartBreak", Position = new Vector3(heart.X, breakY, heart.Y) };
        root.AddChild(breakFace);
        Crystal(breakFace, new Vector3(-0.8f, -0.9f, 0.3f), 1.7f, new Vector3(-8, 10, 6));
        Crystal(breakFace, new Vector3(1.6f, -1.4f, -1.1f), 1.1f, new Vector3(20, 130, -14));

        // The break's debris, stopped mid-fall round the heart: the biggest highest and furthest out, chips close in and low. Every piece
        // keeps its own stone; a carved one split along a face shows fluorite in the split.
        int next = 0;
        void Hang(string id, float scale, bool carved, float from, float to, float low, float high)
        {
            if (Spot(from, to) is not { } p || art.ModelWithLods(id) is not { } piece)
                return;
            piece.Position = new Vector3(p.X, floor + random.RandfRange(low, high), p.Y);
            piece.RotationDegrees = new Vector3(random.RandfRange(-80, 80), random.RandfRange(0, 360), random.RandfRange(-80, 80));
            piece.Scale = Vector3.One * scale;
            root.AddChild(piece);
            if (carved && random.Randf() < 0.4f)
                Crystal(piece, new Vector3(random.RandfRange(-0.4f, 0.4f), random.RandfRange(0.5f, 1.5f), random.RandfRange(-0.4f, 0.4f)),
                    random.RandfRange(0.25f, 0.45f), new Vector3(random.RandfRange(-30, 30), random.RandfRange(0, 360), random.RandfRange(-30, 30)));
            resting.Add((piece, ground.Height(p.X, p.Y) - 0.4f * scale));
            register?.AddDrift(piece, Axis(), random.RandfRange(0.4f, 1.6f) / Mathf.Max(scale, 0.6f));
            placed++;
        }
        void HangNext(float size, float from, float to, float low, float high)
        {
            var (id, min, max, carved) = Debris[next++ % Debris.Length];
            Hang(id, size * random.RandfRange(min, max), carved, from, to, low, high);
        }
        // Kept off the heart's own column, so its silhouette, the break and the tear stay legible from the road.
        foreach (var band in FoldscarRing.Wreck)
            Hang(Wreck, band.Size, true, band.From, band.To, band.Low, band.High);
        foreach (var band in FoldscarRing.Hanging)
            for (int i = 0; i < Many(band.Count); i++)
                HangNext(band.Size, band.From, band.To, band.Low, band.High);

        // Debris that came down long ago: lying round the ring, sunk by a third, a few with fluorite showing where they split.
        var rubble = FoldscarRing.Rubble;
        for (int i = 0; i < Many(rubble.Count); i++)
        {
            var (id, min, max, _) = Debris[(next + i * 5) % Debris.Length];
            if (Spot(rubble.From, rubble.To) is not { } p || art.ModelWithLods(id) is not { } piece)
                continue;
            // Rubble, not boulders: nothing on the ground collides, so nothing lying where people walk stands taller than a knee.
            float scale = random.RandfRange(min, max) * rubble.Size;
            piece.Position = new Vector3(p.X, ground.Height(p.X, p.Y) - 0.5f * scale, p.Y);
            piece.RotationDegrees = new Vector3(random.RandfRange(-25, 25), random.RandfRange(0, 360), random.RandfRange(-25, 25));
            piece.Scale = Vector3.One * scale;
            root.AddChild(piece);
            placed++;
            if (i % 3 == 1 && art.ModelWithLods(Fluorite) is { } crystal)
            {
                crystal.Position = piece.Position + new Vector3(0, 0.6f * scale, 0);
                crystal.RotationDegrees = new Vector3(random.RandfRange(-20, 20), random.RandfRange(0, 360), random.RandfRange(-20, 20));
                crystal.Scale = Vector3.One * 0.8f * scale;
                Inward(crystal);
                root.AddChild(crystal);
                placed++;
            }
        }
        var pebbles = FoldscarRing.Pebbles;
        for (int i = 0; i < pebbles.Count; i++)
        {
            if (Spot(pebbles.From, pebbles.To) is not { } p || art.ModelWithLods(Pebble) is not { } pebble)
                continue;
            float s = random.RandfRange(0.07f, 0.13f);
            float at = ground.Height(p.X, p.Y);
            pebble.Position = new Vector3(p.X, at + random.RandfRange(pebbles.Low, pebbles.High), p.Y);
            pebble.RotationDegrees = new Vector3(random.RandfRange(0, 360), random.RandfRange(0, 360), random.RandfRange(0, 360));
            pebble.Scale = Vector3.One * s;
            root.AddChild(pebble);
            placed++;
            // Resting: its lowest point on the ground, sunk a quarter of the way in (the turned box overstates how low it reaches).
            var box = new Transform3D(pebble.Basis, pebble.Position) * ArtGallery.Bounds(pebble);
            resting.Add((pebble, at + (pebble.Position.Y - box.Position.Y) * 0.75f));
        }
        register?.AddPebbles(resting);

        // The ground of the basin, cracked and glassed outward from the heart: a decal, so it lies on whatever the terrain is.
        var cracks = new NoiseTexture2D
        {
            Width = 2048, Height = 2048, Seamless = false, Normalize = true,
            Noise = new FastNoiseLite
            {
                NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.012f, Seed = 0x5CA7,
                CellularDistanceFunction = FastNoiseLite.CellularDistanceFunctionEnum.Euclidean,
                CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub,
                DomainWarpEnabled = true, DomainWarpAmplitude = 30f, DomainWarpFrequency = 0.01f,
            },
            ColorRamp = new Gradient { Offsets = new[] { 0f, 0.07f, 0.2f }, Colors = new[] { new Color(0.08f, 0.05f, 0.12f, 0.95f), new Color(0.2f, 0.17f, 0.24f, 0.55f), new Color(0.3f, 0.28f, 0.3f, 0f) } },
        };
        root.AddChild(new Decal
        {
            Name = "FoldscarScar", Size = new Vector3(2 * FoldscarRing.Radius, 24, 2 * FoldscarRing.Radius), Position = new Vector3(heart.X, floor, heart.Y), TextureAlbedo = cracks,
            AlbedoMix = 0.85f, UpperFade = 0.2f, LowerFade = 0.2f, CullMask = 1,
        });

        // Dust and seed fluff in the basin drifting upward, slowly; never glowing. A few motes carry the fold's violet.
        var motes = new GpuParticles3D
        {
            Name = "FoldscarMotes", Amount = Many(900), Lifetime = 18, Preprocess = 18, Position = new Vector3(heart.X, floor + 0.5f, heart.Y),
            VisibilityAabb = new Aabb(new Vector3(-45, -2, -45), new Vector3(90, 70, 90)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(FoldscarRing.Radius, 1, FoldscarRing.Radius),
                Gravity = new Vector3(0, 0.22f, 0), InitialVelocityMin = 0.1f, InitialVelocityMax = 0.4f, Direction = Vector3.Up, Spread = 35,
                TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.5f, TurbulenceNoiseScale = 7, ScaleMin = 0.6f, ScaleMax = 1.6f,
                ColorInitialRamp = new GradientTexture1D { Gradient = new Gradient { Offsets = new[] { 0f, 0.7f, 0.71f }, Colors = new[] { new Color(0.86f, 0.83f, 0.76f), new Color(0.86f, 0.83f, 0.76f), new Color(0.8f, 0.5f, 1.4f) } } },
            },
            DrawPass1 = new QuadMesh
            {
                Size = new Vector2(0.09f, 0.09f),
                Material = new StandardMaterial3D
                {
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                    VertexColorUseAsAlbedo = true, AlbedoColor = new Color(1, 1, 1, 0.7f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(motes);
        register?.AddMotes(motes);
        art.Coverage.Resolved("scenery", "foldscar_ground", $"{placed} pieces: the heart's hanging half, fluorite, debris stopped and fallen, stones resting on nothing");
        return placed;
    }

    /// <summary>
    /// The gap between the heart's halves: the world behind it bent and split a little by colour, its edges tinted violet. Unshaded and
    /// added to nothing - it changes what is seen through it and throws no light. <c>presence</c> takes it away when the heart is steadied.
    /// </summary>
    private const string BendCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float presence = 1.0;
void vertex() {
    // Face the camera about the vertical, like the seam it surrounds.
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], MODEL_MATRIX[1], vec4(normalize(cross(INV_VIEW_MATRIX[0].xyz, MODEL_MATRIX[1].xyz)), 0.0), MODEL_MATRIX[3]);
}
void fragment() {
    // Strongest at the seam, nothing at the edges: no outline of a shape, only the world bending towards the line.
    float x = (UV.x - 0.5) * 2.0;
    float ends = smoothstep(0.0, 0.25, UV.y) * smoothstep(1.0, 0.75, UV.y);
    float pull = (1.0 - smoothstep(0.0, 1.0, abs(x))) * ends * presence;
    vec2 bend = vec2(-sign(x) * 0.012 * pull + sin(TIME * 0.6 + UV.y * 11.0) * 0.002 * pull, 0.0);
    vec3 behind = textureLod(screen_tex, SCREEN_UV + bend, 0.0).rgb;
    ALBEDO = mix(behind, behind * vec3(0.8, 0.62, 1.15), pull * 0.6);
    ALPHA = smoothstep(0.0, 0.3, pull);
}
";

    /// <summary>
    /// The haze in the gap: a soft violet mist, densest round the seam and thinning to nothing at its edges, drifting slowly. Self-lit and
    /// added over what is behind it, as a glowing mist is seen; nothing is lit by it.
    /// </summary>
    private const string HazeCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform float presence = 1.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p);
    float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}
void vertex() {
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], MODEL_MATRIX[1], vec4(normalize(cross(INV_VIEW_MATRIX[0].xyz, MODEL_MATRIX[1].xyz)), 0.0), MODEL_MATRIX[3]);
}
void fragment() {
    vec2 c = (UV - 0.5) * vec2(2.0, 2.0);
    float body = exp(-c.x * c.x * 3.5) * smoothstep(1.0, 0.35, abs(c.y));
    float drift = 0.65 + 0.35 * vnoise(vec2(UV.x * 5.0 + TIME * 0.05, UV.y * 4.0 - TIME * 0.12));
    ALBEDO = vec3(0.55, 0.26, 1.0) * 1.4 * drift;
    ALPHA = body * drift * 0.55 * presence;
}
";

    /// <summary>
    /// The seam down the middle of the gap: a ragged violet line where the two halves of the view meet, always facing the eye, brightest at
    /// its core and frayed at its ends. It is the colour of what is seen there, not a lamp: nothing near it is lit by it.
    /// </summary>
    private const string SeamCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform float presence = 1.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p);
    float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}
void vertex() {
    // Face the camera about the vertical, like a line drawn on the air.
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], MODEL_MATRIX[1], vec4(normalize(cross(INV_VIEW_MATRIX[0].xyz, MODEL_MATRIX[1].xyz)), 0.0), MODEL_MATRIX[3]);
}
void fragment() {
    float wander = (vnoise(vec2(UV.y * 6.0, TIME * 0.35)) - 0.5) * 0.35 + (vnoise(vec2(UV.y * 23.0, TIME * 0.8)) - 0.5) * 0.12;
    float x = abs(UV.x - 0.5 - wander * 0.5) * 2.0;
    float core = exp(-x * x * 90.0);
    float glow = exp(-x * x * 5.0) * 0.5;
    float ends = smoothstep(0.0, 0.22, UV.y) * smoothstep(1.0, 0.78, UV.y);
    float fray = 0.75 + 0.25 * vnoise(vec2(UV.y * 40.0, TIME * 1.3));
    ALBEDO = (vec3(0.95, 0.8, 1.0) * core + vec3(0.55, 0.22, 1.0) * glow) * 7.0 * fray;
    ALPHA = clamp(core + glow, 0.0, 1.0) * ends * presence;
}
";
}
