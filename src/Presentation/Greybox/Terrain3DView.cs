// UNNAMED Presentation - the ground drawn by Terrain3D (MIT, TokisanGames, v1.0.2): a renderer fed one way from the domain's terrain grid
// Godot presentation only (D-11): heights go domain -> image -> Terrain3D, never back; its collision is off, and nothing reads it

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual terrain=terrain3d</c> (Phase B, B0.1). Terrain3D draws the ground as a clipmap: LOD, a texture-array splat with height
/// blending, de-tiling and macro variation. Inside the walkable region every vertex (1 m apart) is the domain's own height at that
/// point, so the drawn ground is the ground the body stands on: the domain's 5 m quads are split on their (0,0)-(1,1) diagonal, which
/// passes through the 1 m vertices. Beyond the edge, where movement never goes, the same renderer draws scenery landforms from a
/// seeded noise - the ravine's sheer drop, its floor and far face, and ridges behind - in place of the Phase-A boxes.
/// <para>What it never does: own a height the game reads (the domain's <see cref="TerrainGrid"/> stays the truth, and the camera keeps
/// its own colliders), generate collision (its collision mode is DISABLED), or place anything the simulation sees.</para>
/// </summary>
public static class Terrain3DView
{
    // The five proof layers (B0.1), packed by tools/asset_pipeline/_pack_terrain_texture.py from the external depot (Poly Haven, CC0).
    // Each with a tint over its colour (B3): the Charwood's leaf litter darkened and greyed towards ash - the photographed floor is a
    // light autumn tan that reads pale under the overcast exposure.
    private static readonly (string Id, float UvScale, Color Tint)[] Layers =
    {
        ("terrain_ph_grass_ground", 0.20f, Colors.White),                    // 0: meadow (the waystation)
        ("terrain_ph_forest_floor", 0.25f, new Color(0.56f, 0.53f, 0.51f)),  // 1: leaf litter (the Charwood)
        ("terrain_ph_rocky_trail", 0.25f, Colors.White),                     // 2: trodden paths, the Foldscar's broken ground
        ("terrain_ph_rock_ground", 0.20f, Colors.White),                     // 3: the quarry floor
        ("terrain_ph_dark_rock_02", 0.10f, Colors.White),                    // 4: steep faces (slopes inside; the ravine and ridges outside), projected on steep slopes
        // Phase B demo (composition): the ground under things, not only per cell.
        ("terrain_ph_forest_ground_04", 0.22f, new Color(0.78f, 0.74f, 0.70f)), // 5: leafy ground under the Charwood's crowns
        ("terrain_ph_brown_mud", 0.30f, new Color(0.92f, 0.86f, 0.78f)),     // 6: trodden earth by doors and walls; the Charwood drainage
        ("terrain_ph_gravel_stones", 0.30f, new Color(0.86f, 0.84f, 0.82f)), // 7: the quarry's spoil and loose gravel
        ("terrain_ph_aerial_grass_rock", 0.12f, Colors.White),              // 8: thin grass over rock, breaking up the meadow and the rims
    };

    private const int ForestGround = 5, Earth = 6, Gravel = 7, GrassRock = 8;

    /// <summary>
    /// What the ground lies under and beside (Phase B demo): tree crowns (x, z, radius in metres), building footprints, and the drainage
    /// lines, so the ground reads as a place - leaf litter under the trees with grass in the clearings, trodden earth round the buildings,
    /// mud along the water - not one material per cell. Presentation only: drawn from the layout the simulation already has.
    /// </summary>
    public sealed record Detail(IReadOnlyList<Vector3> Crowns, IReadOnlyList<Rect2> Buildings, IReadOnlyList<Vector2[]> Drainage);

    /// <summary>The Charwood's shallow drainage (content bible section 6: the stream path to the Foldscar).</summary>
    public static readonly Vector2[] CharwoodStream =
        { new(190, 195), new(170, 165), new(150, 132), new(132, 108), new(126, 96) };

    private const int Cliff = 4, Trail = 2;
    private const int RegionSize = 128;             // metres at 1 m a vertex; the imported area is whole regions, aligned to them
    private const int Regions = 4;                  // 4 x 4 regions: 512 m from (-128, -128), the 200 m region inside it

    /// <summary>The ground slots' layers, in <see cref="GroundField.Cells"/> order: (W,S) quarry, (W,N) waystation, (E,S) Foldscar, (E,N) Charwood.</summary>
    private static readonly int[] SlotLayer = { 3, 0, 2, 1 };

    /// <summary>Terrain3D under <paramref name="parent"/>, or null (and why) when the extension or a layer is unavailable.</summary>
    public static Node3D? Build(Node3D parent, TerrainGrid grid, GroundField ground, ArtLibrary art, out string? why, Detail? detail = null)
    {
        why = null;
        if (!ClassDB.ClassExists("Terrain3D"))
        {
            why = "the Terrain3D extension is not loaded";
            return null;
        }
        var layers = new List<GodotObject>();
        for (int i = 0; i < Layers.Length; i++)
        {
            var asset = TextureAsset(Layers[i].Id, Layers[i].UvScale, Layers[i].Tint, art.Root);
            if (asset is null)
            {
                why = $"terrain layer {Layers[i].Id} is not in the asset workspace";
                return null;
            }
            layers.Add(asset);
        }

        var terrain = (Node3D)ClassDB.Instantiate("Terrain3D").AsGodotObject();
        terrain.Name = "Terrain3D";
        parent.AddChild(terrain);
        terrain.Set("region_size", RegionSize);
        terrain.Set("vertex_spacing", 1.0f);
        ((GodotObject)terrain.Get("collision")).Set("mode", 0);           // DISABLED: the domain is the ground's truth

        var assets = ClassDB.Instantiate("Terrain3DAssets").AsGodotObject();
        for (int i = 0; i < layers.Count; i++)
            assets.Call("set_texture", i, layers[i]);
        terrain.Set("assets", assets);

        var material = (GodotObject)terrain.Get("material");
        material.Set("world_background", 0);                              // NONE: nothing beyond the imported regions
        material.Set("auto_shader", true);
        // Terrain3D's autoshader (its embedded shader, read): blend = clamp(1 + 2 auto_slope (normal.y - 1)), 1 on the flat, falling with
        // steepness - so the overlay is the flat ground and the base the steep one; at 3, about 20 degrees is half rock, 30 mostly rock.
        material.Call("set_shader_param", "auto_base_texture", Cliff);
        material.Call("set_shader_param", "auto_overlay_texture", SlotLayer[1]);
        material.Call("set_shader_param", "auto_slope", 3.0f);
        material.Call("set_shader_param", "blend_sharpness", 0.87f);
        material.Call("set_shader_param", "enable_projection", true);      // faces steeper than ~37 degrees sample from the side
        material.Call("set_shader_param", "projection_threshold", 0.8f);
        material.Call("set_shader_param", "enable_macro_variation", true);

        var origin = Origin(ground);
        var (height, control, colour) = Images(grid, ground, origin, detail ?? new Detail(Array.Empty<Vector3>(), Array.Empty<Rect2>(), Array.Empty<Vector2[]>()));
        var data = (GodotObject)terrain.Get("data");
        data.Call("import_images", new Godot.Collections.Array { height, control, colour }, new Vector3(origin.X, 0, origin.Y), 0.0f, 1.0f);
        GD.Print($"UNNAMED terrain3d: {Regions}x{Regions} regions of {RegionSize} m from {origin}; " + Check(data, grid, ground));
        // B0.2: real rock over the north ravine, on the same scenery surface this renderer draws (never read back from it).
        int rocks = RavineDressing.Dress(parent, art, ground.Region, (x, z) => SceneryHeight(ground, x, z), art.Coverage);
        GD.Print($"UNNAMED terrain3d: {rocks} rock pieces dress the north ravine");
        // B0.8: a river on the ravine floor, for the water proof.
        if (WaterView.BuildRavineRiver(parent, ground.Region, (x, z) => SceneryHeight(ground, x, z), out string? waterWhy) is not null)
            art.Coverage.Resolved("water", "ravine_river", $"a river on the north ravine floor ({VisualOptions.Water})");
        else if (waterWhy is not null)
            art.Coverage.Fallback("water", "ravine_river", waterWhy);
        return terrain;
    }

    /// <summary>The imported area's corner: whole regions, one region beyond the walkable region's south-west corner.</summary>
    private static Vector2 Origin(GroundField ground) =>
        new(MathF.Floor(ground.Region.Position.X / RegionSize) * RegionSize - RegionSize, MathF.Floor(ground.Region.Position.Y / RegionSize) * RegionSize - RegionSize);

    /// <summary>
    /// A harness's proof, not a game path: Terrain3D's heights sampled back at every 1 m point of the walkable region against the domain's.
    /// Nothing the game decides ever reads Terrain3D.
    /// </summary>
    private static string Check(GodotObject data, TerrainGrid grid, GroundField ground)
    {
        double worst = 0;
        int n = 0;
        for (float z = ground.Region.Position.Y; z <= ground.Region.End.Y; z += 1f)
        for (float x = ground.Region.Position.X; x <= ground.Region.End.X; x += 1f)
        {
            double drawn = (float)data.Call("get_height", new Vector3(x, 0, z));
            double truth = grid.HeightAtMm((long)(x * 1000), (long)(z * 1000)) / 1000.0;
            worst = Math.Max(worst, Math.Abs(drawn - truth));
            n++;
        }
        return $"height check at {n} walkable 1 m points: worst |terrain3d - domain| = {worst * 1000:0.0} mm";
    }

    /// <summary>
    /// The height image (metres, FORMAT_RF), control image (Terrain3D's uint32 per texel, bit-exact) and colour map (albedo tint and a
    /// roughness offset, neutral 1,1,1,0.5) at 1 m.
    /// </summary>
    private static (Image Height, Image Control, Image Colour) Images(TerrainGrid grid, GroundField ground, Vector2 origin, Detail detail)
    {
        var region = ground.Region;
        int size = RegionSize * Regions;
        var heights = new byte[size * size * 4];
        var controls = new byte[size * size * 4];
        var colours = new byte[size * size * 4];
        var noise = Noise;
        for (int py = 0; py < size; py++)
        for (int px = 0; px < size; px++)
        {
            float x = origin.X + px, z = origin.Y + py;
            bool inside = x >= region.Position.X && x <= region.End.X && z >= region.Position.Y && z <= region.End.Y;
            float h;
            uint c;
            Color tint = new(1, 1, 1, 0.5f);
            if (inside)
            {
                h = grid.HeightAtMm((long)MathF.Round(x * 1000), (long)MathF.Round(z * 1000)) / 1000f;
                (c, tint) = Composed(ground, x, z, detail);
            }
            else
            {
                h = Outside(ground, x, z, noise);
                c = Encode(SlotLayer[1], Cliff, 0, auto: true);
            }
            int at = (py * size + px) * 4;
            BitConverter.TryWriteBytes(heights.AsSpan(at, 4), h);
            BitConverter.TryWriteBytes(controls.AsSpan(at, 4), c);    // the control word's bits as they are: never through a float
            colours[at] = (byte)(Math.Clamp(tint.R, 0, 1) * 255);
            colours[at + 1] = (byte)(Math.Clamp(tint.G, 0, 1) * 255);
            colours[at + 2] = (byte)(Math.Clamp(tint.B, 0, 1) * 255);
            colours[at + 3] = (byte)(Math.Clamp(tint.A, 0, 1) * 255);
        }
        return (Image.CreateFromData(size, size, false, Image.Format.Rf, heights), Image.CreateFromData(size, size, false, Image.Format.Rf, controls),
            Image.CreateFromData(size, size, false, Image.Format.Rgba8, colours));
    }

    private static readonly FastNoiseLite Patches = new() { Seed = 9127, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.045f,
        FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3 };
    private static readonly FastNoiseLite Macro = new() { Seed = 7741, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.012f };

    /// <summary>
    /// A walkable texel's two layers and colour: the cells' own grounds as before (<see cref="Inside"/>), then what the texel lies under
    /// or beside - leaf litter under the crowns and grass in the Charwood's clearings, trodden earth round the buildings, mud along the
    /// drainage, gravel spoil in the quarry, thin grass over rock breaking up the meadow - weighed together and the two strongest drawn.
    /// The colour map carries a slow tint so no meadow is one flat green, darker wet ground by the water, and the Foldscar's ash.
    /// </summary>
    private static (uint Control, Color Tint) Composed(GroundField ground, float x, float z, Detail detail)
    {
        var w = ground.CellWeights(x, z);
        // slots: (W,S) quarry, (W,N) waystation, (E,S) Foldscar, (E,N) Charwood
        float quarry = w.X, meadow = w.Y, fold = w.Z, wood = w.W;
        var weight = new float[Layers.Length];
        float patch = Patches.GetNoise2D(x, z);                 // -1..1, clumps a few metres across
        float macro = Macro.GetNoise2D(x, z);

        // The meadow: grass, with thin-grass-over-rock patches and the odd bare scar.
        weight[0] += meadow * (1 - 0.7f * GroundField.SmoothStep(0.25f, 0.65f, patch));
        weight[GrassRock] += meadow * 0.7f * GroundField.SmoothStep(0.25f, 0.65f, patch);

        // The Charwood: leaf litter under each crown, fading out at its edge; grass in the clearings between.
        float canopy = 0;
        foreach (var crown in detail.Crowns)
        {
            float d = new Vector2(x - crown.X, z - crown.Y).Length();
            canopy = MathF.Max(canopy, 1 - GroundField.SmoothStep(crown.Z * 0.55f, crown.Z * 1.15f, d));
        }
        weight[ForestGround] += wood * canopy;
        weight[1] += wood * (1 - canopy) * (0.55f + 0.3f * GroundField.SmoothStep(-0.3f, 0.4f, patch));
        weight[0] += wood * (1 - canopy) * 0.45f * (1 - GroundField.SmoothStep(-0.3f, 0.4f, patch));
        // Crowns just over a cell edge still drop their litter on the meadow.
        weight[ForestGround] += meadow * canopy * 0.6f;

        // The quarry: rock ground with gravel spoil in drifts.
        weight[3] += quarry * (1 - 0.6f * GroundField.SmoothStep(0.0f, 0.5f, patch));
        weight[Gravel] += quarry * 0.6f * GroundField.SmoothStep(0.0f, 0.5f, patch);

        // The Foldscar: broken trodden ground with rock showing through.
        weight[2] += fold * (1 - 0.45f * GroundField.SmoothStep(0.1f, 0.6f, patch));
        weight[3] += fold * 0.45f * GroundField.SmoothStep(0.1f, 0.6f, patch);

        // Round the buildings: trodden earth, strongest at the walls.
        float nearWall = 0;
        foreach (var b in detail.Buildings)
        {
            float dx = MathF.Max(MathF.Max(b.Position.X - x, x - b.End.X), 0), dz = MathF.Max(MathF.Max(b.Position.Y - z, z - b.End.Y), 0);
            nearWall = MathF.Max(nearWall, 1 - GroundField.SmoothStep(1.0f, 4.5f + 1.5f * patch, MathF.Sqrt(dx * dx + dz * dz)));
        }
        weight[Earth] += nearWall * 1.4f;

        // The drainage: mud in a ragged band.
        float wet = 0;
        foreach (var line in detail.Drainage)
            wet = MathF.Max(wet, 1 - GroundField.SmoothStep(0.8f, 3.2f + patch, Distance(line, x, z)));
        weight[Earth] += wet * 1.6f;

        // The worn paths and steep banks keep their Phase-B rules (overrides, blended from nothing at their thresholds).
        float path = ground.PathAt(x, z);
        weight[Trail] += 2.2f * GroundField.SmoothStep(0.08f, 0.6f, path);
        int first = 0;
        for (int i = 1; i < weight.Length; i++)
            if (weight[i] > weight[first])
                first = i;
        int second = first == 0 ? 1 : 0;
        for (int i = 0; i < weight.Length; i++)
            if (i != first && weight[i] > weight[second])
                second = i;
        float blend = weight[second] / MathF.Max(weight[first] + weight[second], 1e-4f);
        // A steep bank takes the dark rock only halfway, over whatever the ground is (the Phase-B rule: at full strength it read as a black
        // stain on the quarry's pale gravel).
        float slope = ground.Slope(x, z);
        if (slope > 0.5f)
        {
            second = Cliff;
            blend = 0.55f * GroundField.SmoothStep(0.5f, 1.6f, slope);
        }

        // Colour: a slow warm/cool drift across the open ground, wet ground darker and glossier, the Foldscar's ash greyer.
        float drift = 1 + 0.07f * macro;
        var tint = new Color(drift * (1 + 0.03f * macro), drift, drift * (1 - 0.04f * macro), 0.5f);
        tint = tint.Lerp(new Color(0.72f, 0.70f, 0.68f, 0.38f), wet * 0.8f);
        tint = tint.Lerp(new Color(0.88f, 0.87f, 0.88f, 0.5f), fold * 0.5f);
        tint = tint.Lerp(new Color(0.86f, 0.84f, 0.80f, 0.5f), canopy * wood * 0.35f);
        return (Encode(first, second, (int)MathF.Round(Math.Clamp(blend, 0f, 1f) * 255f), auto: false), tint);
    }

    private static float Distance(Vector2[] line, float x, float z)
    {
        var p = new Vector2(x, z);
        float best = float.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            var a = line[i];
            var ab = line[i + 1] - a;
            float t = Math.Clamp((p - a).Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-4f), 0, 1);
            best = MathF.Min(best, (a + ab * t - p).Length());
        }
        return best;
    }

    private static readonly FastNoiseLite Noise = new() { Seed = 4271, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.012f,
        FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 5 };

    /// <summary>The scenery surface at a point beyond the edge (the same function the height image is built from); inside, the domain's.</summary>
    public static float SceneryHeight(GroundField ground, float x, float z)
    {
        var r = ground.Region;
        bool inside = x >= r.Position.X && x <= r.End.X && z >= r.Position.Y && z <= r.End.Y;
        return inside ? ground.Height(x, z) : Outside(ground, x, z, Noise);
    }

    /// <summary>Inside the region: the dominant ground slot as the base, the next as the overlay, then the worn path, then a steep face.</summary>
    private static uint Inside(GroundField ground, float x, float z)
    {
        var w = ground.CellWeights(x, z);
        float[] weights = { w.X, w.Y, w.Z, w.W };
        int first = 0;
        for (int i = 1; i < 4; i++)
            if (weights[i] > weights[first])
                first = i;
        int second = first == 0 ? 1 : 0;
        for (int i = 0; i < 4; i++)
            if (i != first && weights[i] > weights[second])
                second = i;
        int baseLayer = SlotLayer[first], overlay = SlotLayer[second];
        float blend = weights[second] / MathF.Max(weights[first] + weights[second], 1e-4f);
        // An override (the worn path, a steep face) takes the overlay with a blend that starts from nothing at its threshold: switching the
        // overlay at the blend the neighbouring ground already had drew a hard, jagged edge between vertices a metre apart.
        float path = ground.PathAt(x, z);
        if (path > 0.08f)
        {
            overlay = Trail;
            blend = GroundField.SmoothStep(0.08f, 0.6f, path);
        }
        // Inside the hollow a steep bank takes the dark rock only halfway: the scenery's cliff rock at full strength read as a black stain
        // on the quarry's pale gravel.
        float slope = ground.Slope(x, z);
        if (slope > 0.5f)
        {
            overlay = Cliff;
            blend = 0.55f * GroundField.SmoothStep(0.5f, 1.6f, slope);
        }
        return Encode(baseLayer, overlay, (int)MathF.Round(Math.Clamp(blend, 0f, 1f) * 255f), auto: false);
    }

    /// <summary>
    /// Beyond the edge (scenery): the ravine on the west, north and east - a sheer drop from the edge to a floor about 32 m down, a gulf,
    /// then a far face rising to ridges - and to the south a cliff rising from the edge to hills. Continuous with the domain at the edge.
    /// </summary>
    private static float Outside(GroundField ground, float x, float z, FastNoiseLite noise)
    {
        var region = ground.Region;
        float cx = Math.Clamp(x, region.Position.X, region.End.X), cz = Math.Clamp(z, region.Position.Y, region.End.Y);
        float edge = ground.Height(cx, cz);
        float d = MathF.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
        float n = noise.GetNoise2D(x, z);                                   // -1..1
        float n2 = noise.GetNoise2D(x * 3.1f + 500, z * 3.1f - 300);
        // The south side (z below the region) rises; the other three fall into the ravine. Corners blend by direction.
        float south = z < region.Position.Y ? GroundField.SmoothStep(0.2f, 0.8f, (region.Position.Y - z) / MathF.Max(d, 1e-3f)) : 0f;
        float ravine;
        if (d < 2f)
            ravine = edge - d * 0.35f;                                      // a soft lip: the playable edge never meets the sheer drop
        else if (d < 8f)
            ravine = Mathf.Lerp(edge - 0.7f, -30f, GroundField.SmoothStep(2f, 8f, d) * 0.7f + (d - 2f) / 6f * 0.3f);
        else if (d < 26f)
            ravine = -31f + n2 * 1.2f;
        else if (d < 40f)
            ravine = Mathf.Lerp(-31f, 14f + n * 8f, GroundField.SmoothStep(26f, 40f, d));
        else
            ravine = 14f + n * 8f + GroundField.SmoothStep(40f, 180f, d) * (26f + n * 18f);
        float cliff = d < 12f
            ? Mathf.Lerp(edge, edge + 20f + n * 5f, GroundField.SmoothStep(0f, 12f, d))
            : edge + 20f + n * 5f + GroundField.SmoothStep(12f, 180f, d) * (24f + n * 16f);
        return Mathf.Lerp(ravine, cliff, south) + n2 * 0.6f;
    }

    /// <summary>Terrain3D's control word: base id bits 31-27, overlay 26-22, blend 21-14, hole bit 2, navigation bit 1, autoshader bit 0.</summary>
    private static uint Encode(int baseLayer, int overlay, int blend, bool auto) =>
        ((uint)(baseLayer & 0x1F) << 27) | ((uint)(overlay & 0x1F) << 22) | ((uint)(blend & 0xFF) << 14) | (auto ? 1u : 0u);

    private static GodotObject? TextureAsset(string id, float uvScale, Color tint, string? assetRoot)
    {
        if (assetRoot is null)
            return null;
        string folder = Path.Combine(assetRoot, "materials", id);
        var albedo = LoadTexture(Path.Combine(folder, id + "_albedo_height.png"));
        var normal = LoadTexture(Path.Combine(folder, id + "_normal_rough.png"));
        if (albedo is null || normal is null)
            return null;
        var asset = ClassDB.Instantiate("Terrain3DTextureAsset").AsGodotObject();
        asset.Set("name", id);
        asset.Set("albedo_texture", albedo);
        asset.Set("normal_texture", normal);
        asset.Set("uv_scale", uvScale);
        asset.Set("albedo_color", tint);
        asset.Set("detiling_rotation", 0.12f);
        asset.Set("detiling_shift", 0.25f);
        return asset;
    }

    private static ImageTexture? LoadTexture(string path)
    {
        if (!File.Exists(path))
            return null;
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
            return null;
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
