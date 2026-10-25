// UNNAMED Presentation - greybox materials
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>Flat greybox materials, one per kind of thing, so the hollow reads at a glance.</summary>
public static class Palette
{
    public static StandardMaterial3D Terrain { get; } = Flat(new Color(0.36f, 0.42f, 0.27f), roughness: 1f);
    public static StandardMaterial3D Wood { get; } = Flat(new Color(0.42f, 0.30f, 0.20f));
    public static StandardMaterial3D Roof { get; } = Flat(new Color(0.30f, 0.22f, 0.16f));
    public static StandardMaterial3D Door { get; } = Flat(new Color(0.55f, 0.22f, 0.16f));
    public static StandardMaterial3D Rock { get; } = Flat(new Color(0.45f, 0.45f, 0.47f));
    public static StandardMaterial3D Trunk { get; } = Flat(new Color(0.33f, 0.24f, 0.17f));
    public static StandardMaterial3D Canopy { get; } = Flat(new Color(0.20f, 0.33f, 0.18f));
    public static StandardMaterial3D Cliff { get; } = Flat(new Color(0.32f, 0.30f, 0.29f));
    public static StandardMaterial3D Skin { get; } = Flat(new Color(0.78f, 0.63f, 0.52f));
    public static StandardMaterial3D Cloth { get; } = Flat(new Color(0.28f, 0.33f, 0.45f));
    public static StandardMaterial3D Leather { get; } = Flat(new Color(0.35f, 0.25f, 0.18f));
    public static StandardMaterial3D Proxy { get; } = Flat(new Color(0.55f, 0.52f, 0.45f));
    public static StandardMaterial3D Metal { get; } = Flat(new Color(0.62f, 0.60f, 0.56f), roughness: 0.4f);
    public static StandardMaterial3D Ore { get; } = Flat(new Color(0.55f, 0.32f, 0.22f), roughness: 0.6f);
    public static StandardMaterial3D WorkedOut { get; } = Flat(new Color(0.40f, 0.38f, 0.37f));
    public static StandardMaterial3D AshBark { get; } = Flat(new Color(0.78f, 0.74f, 0.64f));
    public static StandardMaterial3D Leaves { get; } = Flat(new Color(0.36f, 0.48f, 0.26f));
    public static StandardMaterial3D Hearth { get; } = Flat(new Color(0.38f, 0.34f, 0.32f));
    public static StandardMaterial3D Iron { get; } = Flat(new Color(0.18f, 0.18f, 0.20f), roughness: 0.35f);
    public static StandardMaterial3D Shaft { get; } = Flat(new Color(0.60f, 0.50f, 0.36f));

    /// <summary>An arrow's fletching: pale, so an arrow stuck in bark or earth can be found by eye.</summary>
    public static StandardMaterial3D Fletching { get; } = Flat(new Color(0.92f, 0.90f, 0.84f));

    public static StandardMaterial3D Embers { get; } = new()
    {
        AlbedoColor = new Color(1f, 0.45f, 0.1f),
        EmissionEnabled = true,
        Emission = new Color(1f, 0.35f, 0.05f),
    };

    /// <summary>
    /// The fold that holds Tavar (M6, content bible §8: "keep the weirdness restrained - slight image doubling; delayed shadow;
    /// displaced audio; inconsistent reflections"): a refractive shimmer over the world behind it (two drifting noise fields warp the
    /// screen sample, and a second, more-displaced ghost sample is blended faintly under the first for the doubling), a faint violet
    /// fresnel rim, and drifting motes; its own alpha and colour fade out approaching the ground and the top of its footprint so it
    /// reads as a standing effect rather than a hard-edged debug volume. Unshaded: it draws its own screen sample, not lit geometry.
    /// </summary>
    public static ShaderMaterial FoldscarBarrier { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_draw_opaque, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec3 edge_colour : source_color = vec3(0.62, 0.42, 0.95);
uniform vec3 core_colour : source_color = vec3(0.22, 0.15, 0.38);
uniform float distortion_amount : hint_range(0.0, 0.05) = 0.014;
uniform float scroll_speed = 0.045;
uniform float top_fade : hint_range(0.0, 1.0) = 0.60;
// The Phase B VFX lane's release (FoldscarRegister): the doubled view closing into register, then the whole thinning away.
uniform float in_register = 0.0;
uniform float presence = 1.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void fragment() {
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.2);

    vec2 uv_a = UV * vec2(2.4, 1.6) + vec2(TIME * scroll_speed, -TIME * scroll_speed * 0.5);
    vec2 uv_b = UV * vec2(3.6, 2.4) + vec2(-TIME * scroll_speed * 0.6, TIME * scroll_speed * 0.35);
    float shimmer = vnoise(uv_a * 5.0) * 0.6 + vnoise(uv_b * 8.0) * 0.4;

    vec2 offset = (vec2(vnoise(uv_a * 6.0 + 2.0), vnoise(uv_b * 7.0 + 9.0)) - 0.5) * distortion_amount;
    vec3 behind = textureLod(screen_tex, SCREEN_UV + offset, 0.0).rgb;
    // A second, more-displaced sample ghosted faintly under the first: the bible's ""slight image doubling"".
    vec3 ghost = textureLod(screen_tex, SCREEN_UV + (offset * 2.6 + vec2(0.006, -0.004)) * (1.0 - in_register), 1.5).rgb;

    float bottom_fade = smoothstep(0.0, 0.10, UV.y);
    float top_alpha = 1.0 - smoothstep(top_fade, 1.0, UV.y);
    float mask = bottom_fade * top_alpha;

    vec2 mote_uv = UV * vec2(7.0, 11.0) + vec2(shimmer * 0.4, -TIME * 0.085);
    float motes = pow(clamp(vnoise(mote_uv) - 0.55, 0.0, 1.0) * 2.2, 3.0);

    vec3 tint = mix(core_colour, edge_colour, clamp(fresnel + shimmer * 0.2, 0.0, 1.0));
    vec3 warped = mix(behind, ghost, 0.35);
    vec3 colour = mix(warped, tint, 0.35 + fresnel * 0.45) + edge_colour * motes * 1.4;

    ALBEDO = colour;
    ALPHA = clamp(0.22 + fresnel * 0.5 + shimmer * 0.1 + motes * 0.5, 0.0, 0.92) * mask * presence;
}
",
        },
    };

    /// <summary>
    /// The fold as B0.6 draws it (<c>--visual foldscar=proof</c>): the bible's restrained weirdness and nothing else - the world behind
    /// seen a little wrong (a slow warp, and the same view doubled a hair to one side), strongest where the fold faces the eye and thinning
    /// toward its silhouette; a ragged, drifting top, a thin ground-contact shimmer, and a restrained cool boundary make the standing
    /// fold readable at player distance. Refraction uses the opaque screen copy; its boundary and slow motes remain visible over sky.
    /// </summary>
    public static ShaderMaterial FoldscarFold { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_back, unshaded, depth_draw_never, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float height_m = 2.6;
uniform float warp_amount = 0.010;
uniform float doubling = 0.010;
uniform float reaction = 0.0;
uniform float proximity = 0.0;
// The Phase B VFX lane's release (FoldscarRegister): the doubled view closing into register, then the whole thinning away.
uniform float in_register = 0.0;
uniform float presence = 1.0;
varying vec3 local;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void vertex() {
    local = VERTEX;
}

void fragment() {
    float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float body = smoothstep(0.08, 0.75, facing);
    float around = atan(local.z, local.x);
    float h = local.y / height_m + 0.5;
    float ragged = vnoise(vec2(around * 2.5 + TIME * 0.03, TIME * 0.07));
    float top = 1.0 - smoothstep(0.45 + 0.3 * ragged, 0.98, h);
    float foot = smoothstep(0.0, 0.12, h);

    vec2 p = vec2(around * 1.7, h * 2.2);
    vec2 warp = (vec2(vnoise(p * 3.0 + vec2(TIME * 0.05, 0.0)), vnoise(p * 3.0 + vec2(7.3, -TIME * 0.04))) - 0.5) * warp_amount * body * (1.0 - 0.7 * in_register);
    vec4 behind = textureLod(screen_tex, SCREEN_UV + warp, 0.0);
    // The doubling: the same view again, a hair to one side and drifting slowly back and forth.
    vec2 side = vec2(doubling * (0.6 + 0.4 * sin(TIME * 0.21)), doubling * 0.15) * (1.0 - in_register);
    vec4 ghost = textureLod(screen_tex, SCREEN_UV + warp * 1.6 + side * body, 0.0);
    // What reaches the frame after the screen copy (a fading instance: the scatter's far chunks, a figure at its range) is not in it -
    // the copy holds nothing there (alpha 0). Draw only over what the copy holds, so those stay as they were drawn.
    float held = smoothstep(0.4, 0.9, behind.a);
    vec3 colour = mix(behind.rgb, mix(behind.rgb, ghost.rgb, 0.42), smoothstep(0.4, 0.9, ghost.a));
    colour *= mix(vec3(1.0), vec3(0.94, 0.93, 1.03), body);

    // Drifting motes are soft particles (FoldWard); thresholded cell noise here
    // made hard rectangular light patches on the cylinder at close range.

    // Keep refraction on the opaque screen copy, but let the boundary read over sky and transparent foliage too.
    float edge = pow(1.0 - facing, 2.2);
    float contact = 1.0 - smoothstep(0.01, 0.035, h);
    // Violet: the colour of the Rift tear, kept for whatever bends reality (owner, 2026-10-24).
    colour = mix(colour, vec3(0.62, 0.50, 0.86), clamp(edge * 0.65 + contact * 0.55, 0.0, 0.7));
    float pulse = .75 + .25 * sin(TIME * 2.5 - h * 5.0);
    float bands = pow(.5 + .5 * sin(h * 28.0 - TIME * .7 + ragged * 3.0), 12.0);
    vec3 light = vec3(.58,.3,1.0) * (edge * .9 + contact * 2.0 + bands * .45) * (pulse + reaction * 1.8 + proximity * .25);
    ALBEDO = colour + light;
    EMISSION = light * 3.0;
    ALPHA = clamp(body * top * foot * held * .14 + (edge * .55 * foot + contact * .7 + bands * .2 * foot) * top + reaction * .12 * top * foot, 0.0, .75) * presence;
}
",
        },
    };

    /// <summary>
    /// A model wearing a world material over its own relief (Phase B, B0.6): the material's colour, roughness and metal laid triplanar in
    /// the world at its tile size, the model's baked normal and occlusion keeping its carving, and a faint cold light kept in the deepest
    /// grooves, breathing slowly - worked stone whose purpose is not obvious, not a lamp. With <paramref name="edgeWear"/>, the relief's
    /// edges (where its baked normal turns hardest) are chipped back to bright metal wherever the material's alpha lets them.
    /// </summary>
    public static void Wear(Node3D model, Art.WorldMaps maps, Color tint, Color groove, float glow, float edgeWear = 0)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                var own = mesh.GetActiveMaterial(s) as BaseMaterial3D;
                var worn = new ShaderMaterial { Shader = _wornShader ??= new Shader { Code = WornCode } };
                worn.SetShaderParameter("surface_albedo", maps.Albedo);
                worn.SetShaderParameter("surface_orm", maps.Orm);
                worn.SetShaderParameter("has_surface_orm", maps.Orm is not null);
                worn.SetShaderParameter("tile_m", maps.TileSizeM);
                worn.SetShaderParameter("relief_normal", own?.NormalTexture);
                worn.SetShaderParameter("has_relief_normal", own is { NormalEnabled: true, NormalTexture: not null });
                worn.SetShaderParameter("relief_ao", own?.AOTexture);
                worn.SetShaderParameter("has_relief_ao", own is { AOEnabled: true, AOTexture: not null });
                worn.SetShaderParameter("groove_colour", new Vector3(groove.R, groove.G, groove.B));
                worn.SetShaderParameter("tint", new Vector3(tint.R, tint.G, tint.B));
                worn.SetShaderParameter("groove_glow", glow);
                worn.SetShaderParameter("edge_wear", edgeWear);
                mesh.SetSurfaceOverrideMaterial(s, worn);
            }
        }
    }

    /// <summary>
    /// Weathered carved stone over a model's own carving (grand scale): its own colour lifted by <paramref name="lift"/> (the power its
    /// source material raised the colour to - Unreal packs store their stone dark), roughened to matte and never metal, with
    /// <paramref name="stone"/>'s grain and lichen laid triplanar in the world over it as detail round that material's own mean, a slow
    /// variation in value the size of a house so a 60 m figure is not one flat tone, and <paramref name="moss"/> grown in patches on the
    /// faces that look up. The model's own normal and occlusion keep the chisel work.
    /// </summary>
    public static void Weathered(Node3D model, Art.WorldMaps stone, Art.WorldMaps moss, float lift = 0.5f)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                if (mesh.GetActiveMaterial(s) is not BaseMaterial3D own)
                    continue;
                var weathered = new ShaderMaterial { Shader = _weatheredShader ??= new Shader { Code = WeatheredCode } };
                weathered.SetShaderParameter("own_albedo", own.AlbedoTexture);
                weathered.SetShaderParameter("own_tint", new Vector3(own.AlbedoColor.R, own.AlbedoColor.G, own.AlbedoColor.B));
                weathered.SetShaderParameter("own_normal", own.NormalTexture);
                weathered.SetShaderParameter("has_own_normal", own is { NormalEnabled: true, NormalTexture: not null });
                weathered.SetShaderParameter("own_ao", own.AOTexture);
                weathered.SetShaderParameter("has_own_ao", own is { AOEnabled: true, AOTexture: not null });
                weathered.SetShaderParameter("own_rough", own.RoughnessTexture);
                weathered.SetShaderParameter("has_own_rough", own.RoughnessTexture is not null);
                weathered.SetShaderParameter("lift", lift);
                weathered.SetShaderParameter("stone_albedo", stone.Albedo);
                weathered.SetShaderParameter("stone_tile_m", stone.TileSizeM);
                weathered.SetShaderParameter("moss_albedo", moss.Albedo);
                weathered.SetShaderParameter("moss_tile_m", moss.TileSizeM);
                mesh.SetSurfaceOverrideMaterial(s, weathered);
            }
        }
    }

    private static Shader? _weatheredShader;
    private static Shader? _wornShader;
    private static Shader? _treeShader;
    private static Shader? _leafShader;

    /// <summary>
    /// A tree on the wind (Phase B, B4): each surface's own material rebuilt as <see cref="WindField.TreeShader"/> - its colour, normal and
    /// roughness maps, its leaves still alpha-tested - swaying by the model's height and <paramref name="stiffness"/>.
    /// <paramref name="tint"/> multiplies the base colour, which is how a PLACED TREE gets the per-instance variation
    /// it never had: see <see cref="Shifted"/>. The tint's alpha must stay 1 - the shader multiplies ALPHA by it too,
    /// and a leaf whose alpha drops crosses the alpha-scissor threshold and disappears.
    /// </summary>
    public static void Windblown(Node3D model, float stiffness, Color tint)
    {
        float height = Math.Max(1f, Art.ArtGallery.Bounds(model).Size.Y / Math.Max(0.01f, model.Scale.Y));
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                if (mesh.GetActiveMaterial(s) is not BaseMaterial3D own)
                    continue;
                bool foliage = own.Transparency is BaseMaterial3D.TransparencyEnum.AlphaScissor or BaseMaterial3D.TransparencyEnum.AlphaHash;
                // White source factors can move their exact tint to instance data without changing
                // source_color conversion. Keep the original product for non-white source factors.
                bool shared = own.AlbedoColor == Colors.White
                    && System.Environment.GetEnvironmentVariable("UNNAMED_TREE_MATERIALS") != "0";
                var tree = shared && own.HasMeta("otherreach_wind_white_material")
                    ? own.GetMeta("otherreach_wind_white_material").AsGodotObject() as ShaderMaterial : null;
                if (tree is null)
                {
                    var shader = foliage
                        ? _leafShader ??= new Shader { Code = Otherworld.Variant(WindField.TreeShader.Replace("cull_back", "cull_disabled")) }
                        : _treeShader ??= new Shader { Code = Otherworld.Variant(WindField.TreeShader) };
                    tree = new ShaderMaterial { Shader = shader };
                    tree.SetShaderParameter("albedo_tex", own.AlbedoTexture);
                    tree.SetShaderParameter("has_albedo", own.AlbedoTexture is not null);
                    tree.SetShaderParameter("albedo_colour", shared ? Colors.White : own.AlbedoColor * tint);
                    tree.SetShaderParameter("use_instance_tint", shared);
                    tree.SetShaderParameter("normal_tex", own.NormalTexture);
                    tree.SetShaderParameter("has_normal", own is { NormalEnabled: true, NormalTexture: not null });
                    tree.SetShaderParameter("roughness_tex", own.RoughnessTexture);
                    tree.SetShaderParameter("has_roughness", own.RoughnessTexture is not null);
                    tree.SetShaderParameter("roughness_value", own.Roughness);
                    tree.SetShaderParameter("foliage", foliage);
                    tree.SetShaderParameter("alpha_cut", own.AlphaScissorThreshold);
                    if (shared) own.SetMeta("otherreach_wind_white_material", tree);
                }
                mesh.SetSurfaceOverrideMaterial(s, tree);
            }
            mesh.SetInstanceShaderParameter("tree_tint", tint);
            mesh.SetInstanceShaderParameter("tree_height", height);
            mesh.SetInstanceShaderParameter("stiffness", stiffness);
            mesh.SetInstanceShaderParameter("ow_late", 0f);
        }
    }

    /// <summary>A <see cref="Windblown"/> tree's sway set <paramref name="seconds"/> behind the world's wind (the tree shader's <c>ow_late</c>).</summary>
    public static void Late(Node3D model, float seconds)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            mesh.SetInstanceShaderParameter("ow_late", seconds);
        }
    }

    /// <summary>
    /// The per-instance value and warmth shift a placement gets from its OWN ID - the colour half of the owner's
    /// "samey duplicates" complaint, in one place so every path that varies a placement varies it the same way.
    ///
    /// Deterministic on purpose: the same world must look the same on every run and in every capture, so it is the
    /// placement's own id and never a frame counter or a shared RNG. The numbers are the ones the surface path has
    /// used since 2026-09-27 (a value shift of +-18 %, a warm/cool lean of +-6 %). <see cref="Wear"/> applies it to a
    /// structure wearing a world material; <see cref="Windblown"/> applies it to a tree, WHICH HAD NO PER-INSTANCE
    /// COLOUR AT ALL until this was extracted - 145 placed trees drawn from twelve models, every birch canopy exactly
    /// the same yellow, while the 240 boulders beside them varied.
    /// </summary>
    public static Color Shifted(string id, Color tint)
    {
        uint hash = 2166136261u;
        foreach (char ch in id)
            hash = (hash ^ ch) * 16777619u;
        var variation = new RandomNumberGenerator { Seed = hash };
        float value = 1f + variation.RandfRange(-0.18f, 0.18f);
        float warm = variation.RandfRange(-0.06f, 0.06f);
        return new Color(
            Mathf.Clamp(tint.R * value * (1f + warm), 0f, 8f),
            Mathf.Clamp(tint.G * value, 0f, 8f),
            Mathf.Clamp(tint.B * value * (1f - warm), 0f, 8f));
    }

    /// <summary>
    /// The per-instance shift applied to a model's OWN materials, without replacing them - for scenery that is placed
    /// directly rather than bound in the region's `structures`, so it can never carry a `surface` and never reached
    /// <see cref="Wear"/> or <see cref="Windblown"/>.
    ///
    /// WHY IT DUPLICATES. A `MeshInstance3D` has no per-instance colour; `use_instance_color` is a MultiMesh feature,
    /// and these are individually placed `Node3D`s. So each surface's material is duplicated and its AlbedoColor
    /// multiplied - the scan's own base colour, normal and roughness maps are kept, which is the whole reason these
    /// Poly Haven pieces were chosen, and only the value and warm/cool lean change. `RavineDressing` rings the region
    /// with 304 rock pieces - 99 east, 100 north, 89 west and 16 on the south cliff - from ONE cliff model and TWO
    /// boulder models, and before this every one of them was the same colour as every other.
    /// </summary>
    public static void Tinted(Node3D model, Color tint)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                if (mesh.GetActiveMaterial(s) is not BaseMaterial3D own)
                    continue;
                var copy = (BaseMaterial3D)own.Duplicate();
                copy.AlbedoColor = own.AlbedoColor * tint;
                mesh.SetSurfaceOverrideMaterial(s, copy);
            }
        }
    }

    private const string WeatheredCode = @"
shader_type spatial;
uniform sampler2D own_albedo : source_color, filter_linear_mipmap_anisotropic;
uniform vec3 own_tint = vec3(1.0);
uniform sampler2D own_normal : hint_normal, filter_linear_mipmap_anisotropic;
uniform bool has_own_normal = false;
uniform sampler2D own_ao : hint_default_white, filter_linear_mipmap_anisotropic;
uniform bool has_own_ao = false;
uniform sampler2D own_rough : hint_default_white, filter_linear_mipmap_anisotropic;
uniform bool has_own_rough = false;
uniform float lift = 0.5;
uniform sampler2D stone_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float stone_tile_m = 2.0;
uniform sampler2D moss_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float moss_tile_m = 2.0;
varying vec3 world_pos;
varying vec3 world_normal;

vec3 triplanar(sampler2D tex, vec3 p, vec3 n, float tile) {
    vec3 w = pow(abs(n), vec3(4.0));
    w /= (w.x + w.y + w.z);
    return texture(tex, p.zy / tile).rgb * w.x + texture(tex, p.xz / tile).rgb * w.y + texture(tex, p.xy / tile).rgb * w.z;
}

float hash(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }

float vnoise(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) { return 0.55 * vnoise(p) + 0.3 * vnoise(p * 2.03 + 11.0) + 0.15 * vnoise(p * 4.1 + 23.0); }

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec3 own = pow(texture(own_albedo, UV).rgb * own_tint, vec3(lift));
    // The stone's grain round its own mean (the smallest mip), greyed a little so the lichen rock's rust does not tint the statue.
    vec3 grain = triplanar(stone_albedo, world_pos, world_normal, stone_tile_m) / max(textureLod(stone_albedo, vec2(0.5), 12.0).rgb, vec3(0.02));
    float grain_value = dot(grain, vec3(0.3, 0.59, 0.11));
    vec3 albedo = own * mix(vec3(1.0), mix(vec3(grain_value), grain, 0.35), 0.7) * (0.8 + 0.4 * fbm(world_pos / 9.0));
    float up = smoothstep(0.3, 0.8, world_normal.y);
    float tuft = smoothstep(0.42, 0.62, fbm(world_pos / 2.6 + vec3(7.0)));
    float mossed = up * tuft * 0.85;
    vec3 moss = triplanar(moss_albedo, world_pos, world_normal, moss_tile_m) * vec3(0.62, 0.66, 0.42);
    ALBEDO = mix(albedo, moss, mossed);
    float ao = has_own_ao ? texture(own_ao, UV).r : 1.0;
    float rough = has_own_rough ? texture(own_rough, UV).g : 0.8;
    ROUGHNESS = mix(clamp(rough + 0.25, 0.72, 1.0), 0.95, mossed);
    METALLIC = 0.0;
    AO = ao;
    if (has_own_normal) {
        NORMAL_MAP = texture(own_normal, UV).rgb;
    }
}
";

    private const string WornCode = @"
shader_type spatial;
uniform sampler2D surface_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D surface_orm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_surface_orm = false;
uniform float tile_m = 1.2;
uniform sampler2D relief_normal : hint_normal, filter_linear_mipmap_anisotropic;
uniform bool has_relief_normal = false;
uniform sampler2D relief_ao : hint_default_white, filter_linear_mipmap_anisotropic;
uniform bool has_relief_ao = false;
uniform vec3 groove_colour = vec3(0.45, 0.55, 1.0);
uniform vec3 tint = vec3(1.0);
uniform float groove_glow = 0.5;
uniform float edge_wear = 0.0;
varying vec3 world_pos;
varying vec3 world_normal;

vec4 triplanar(sampler2D tex, vec3 p, vec3 n) {
    vec3 w = pow(abs(n), vec3(4.0));
    w /= (w.x + w.y + w.z);
    return texture(tex, p.zy / tile_m) * w.x + texture(tex, p.xz / tile_m) * w.y + texture(tex, p.xy / tile_m) * w.z;
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec4 surface = triplanar(surface_albedo, world_pos, world_normal);
    vec3 albedo = surface.rgb;
    vec3 orm = has_surface_orm ? triplanar(surface_orm, world_pos, world_normal).rgb : vec3(1.0, 0.6, 0.0);
    if (edge_wear > 0.0 && has_relief_normal) {
        vec3 relief = texture(relief_normal, UV).rgb * 2.0 - 1.0;
        float chipped = smoothstep(0.12, 0.45, 1.0 - relief.z) * smoothstep(0.6, 0.2, surface.a) * edge_wear;
        albedo = mix(albedo, albedo * 2.6 + vec3(0.05), chipped);
        orm.gb = mix(orm.gb, vec2(0.3, 0.9), chipped);
    }
    float ao = has_relief_ao ? texture(relief_ao, UV).r : 1.0;
    ALBEDO = albedo * tint * mix(0.45, 1.0, ao);
    // A polished stone, not a mirror: the carving reads through the highlights.
    ROUGHNESS = clamp(orm.g, 0.22, 1.0);
    METALLIC = orm.b;
    AO = ao * orm.r;
    if (has_relief_normal) {
        NORMAL_MAP = texture(relief_normal, UV).rgb;
    }
    // The deepest tenth of the carving (its baked occlusion under about 0.75) holds the light.
    float groove = 1.0 - smoothstep(0.62, 0.8, ao);
    EMISSION = groove_colour * groove * groove_glow * (0.7 + 0.3 * sin(TIME * 0.55));
}
";

    /// <summary>A Quiet Stone turned into line (M6): a pale band round its top.</summary>
    public static StandardMaterial3D Aligned { get; } = new()
    {
        AlbedoColor = new Color(0.85f, 0.88f, 0.95f),
        EmissionEnabled = true,
        Emission = new Color(0.35f, 0.38f, 0.48f),
    };

    /// <summary>
    /// A Quiet Stone drawn from the asset library, turned into line (Phase A): a pale rim of light round the stone's own silhouette - a
    /// shell grown a centimetre off its surface, brightest where the surface turns away from the eye - leaving its face its own.
    /// </summary>
    public static ShaderMaterial AlignedGlow { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled;
uniform vec3 glow : source_color = vec3(0.70, 0.80, 1.0);
uniform float strength = 0.8;
varying vec3 world;
void vertex() {
    VERTEX += NORMAL * 0.012;
    world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.5);
    // A set stone is lit through, not only edged: a faint fill with bands of light climbing it (asset overhaul 2026-10-14).
    float band = smoothstep(0.7, 1.0, 0.5 + 0.5 * sin(world.y * 5.0 - TIME * 2.4 + sin(world.x * 2.3 + world.z * 1.7) * 2.0));
    ALBEDO = glow * strength * (rim + 0.18 + 0.45 * band);
}
",
        },
    };

    /// <summary>
    /// The stream (presentation only - the full water system is later): animated fake-normal ripples (two scrolling wave fields turned
    /// into a bump via a finite-difference slope, so a plain low-poly plane still shows motion), a shallow/deep colour gradient and a
    /// soft edge where it meets the banks (both read from the depth prepass: the gap between the water surface and whatever the camera
    /// sees behind it), a fresnel sky tint at grazing angles, and roughness that varies a little with the ripple so the sun's specular
    /// sparkles rather than sitting as one flat highlight.
    /// </summary>
    public static ShaderMaterial StreamWater { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;
uniform vec3 shallow_colour : source_color = vec3(0.18, 0.36, 0.34);
uniform vec3 deep_colour : source_color = vec3(0.035, 0.09, 0.13);
uniform float depth_fade_m : hint_range(0.05, 4.0) = 0.9;
uniform float edge_fade_m : hint_range(0.005, 1.0) = 0.12;
uniform float ripple_speed = 0.6;
uniform float ripple_scale = 2.4;
uniform float ripple_strength : hint_range(0.0, 0.2) = 0.035;
uniform float roughness_base : hint_range(0.0, 1.0) = 0.12;

varying vec2 world_xz;

void vertex() {
    world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

float wave_h(vec2 p) {
    float a = sin((p.x * 1.00 + p.y * 0.35 + TIME * ripple_speed) * ripple_scale);
    float b = sin((p.x * -0.60 + p.y * 0.80 - TIME * ripple_speed * 0.8) * ripple_scale * 1.7);
    return a * 0.6 + b * 0.4;
}

void fragment() {
    float e = 0.05;
    float h0 = wave_h(world_xz);
    float slope_x = (wave_h(world_xz + vec2(e, 0.0)) - h0) / e;
    float slope_z = (wave_h(world_xz + vec2(0.0, e)) - h0) / e;
    vec3 world_normal = normalize(vec3(-slope_x * ripple_strength, 1.0, -slope_z * ripple_strength));
    NORMAL = normalize((VIEW_MATRIX * vec4(world_normal, 0.0)).xyz);

    float raw_depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
    vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
    vec4 view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
    view.xyz /= view.w;
    float scene_depth = -view.z;
    float surface_depth = -VERTEX.z;
    float water_depth = max(scene_depth - surface_depth, 0.0);

    float depth_t = clamp(water_depth / depth_fade_m, 0.0, 1.0);
    vec3 tint = mix(shallow_colour, deep_colour, depth_t);
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
    tint = mix(tint, vec3(0.75, 0.82, 0.85), fresnel * 0.35);
    float edge = clamp(water_depth / edge_fade_m, 0.0, 1.0);

    ALBEDO = tint;
    ALPHA = mix(0.35, 0.88, depth_t) * edge;
    ROUGHNESS = clamp(roughness_base + abs(slope_x + slope_z) * 0.6, 0.03, 0.5);
    SPECULAR = 0.5;
}
",
        },
    };

    private static StandardMaterial3D Flat(Color color, float roughness = 0.9f) => new() { AlbedoColor = color, Roughness = roughness };
}
