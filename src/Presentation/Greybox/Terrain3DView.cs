// UNNAMED Presentation - the ground drawn by Terrain3D (MIT, TokisanGames, v1.0.2): a renderer fed one way from the domain's terrain grid
// Godot presentation only (D-11): heights go domain -> image -> Terrain3D, never back; its collision is off, and nothing reads it

using System.Text.Json.Nodes;
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
    private static readonly (string Id, float UvScale, Color Tint)[] DefaultLayers =
    {
        ("terrain_ph_grass_ground", 0.20f, Colors.White),                    // 0: meadow (the waystation)
        ("terrain_ph_forest_floor", 0.25f, new Color(0.56f, 0.53f, 0.51f)),  // 1: leaf litter (the Charwood)
        ("terrain_ph_rocky_trail", 0.25f, new Color(0.78f, 0.74f, 0.68f)), // 2: trodden paths, the Foldscar's broken ground
        ("terrain_ph_rock_ground", 0.32f, new Color(0.70f, 0.68f, 0.65f)), // 3: the quarry floor (tile ~3 m, was 0.20/~5 m: at a grazing angle across a large flat floor the detail resolved to nothing)
        ("terrain_ph_dark_rock_02", 0.26f, Colors.White),                    // 4: steep faces (slopes inside; the ravine and ridges outside), projected on steep slopes (was 0.10, the most stretched of the nine)
        // Phase B demo (composition): the ground under things, not only per cell.
        ("terrain_ph_forest_ground_04", 0.22f, new Color(0.78f, 0.74f, 0.70f)), // 5: leafy ground under the Charwood's crowns
        ("terrain_ph_brown_mud", 0.30f, new Color(0.92f, 0.86f, 0.78f)),     // 6: trodden earth by doors and walls; the Charwood drainage
        ("terrain_ph_gravel_stones", 0.30f, new Color(0.80f, 0.78f, 0.75f)), // 7: the quarry's spoil and loose gravel
        ("terrain_ph_aerial_grass_rock", 0.12f, Colors.White),              // 8: thin grass over rock, breaking up the meadow and the rims
    };

    /// <summary>
    /// The nine slots this region fills, or the default nine when its asset root names none.
    ///
    /// The COUNT and the ORDER are a contract, not content: the index constants below address the slots by
    /// position and the weight array is sized by them, so a region may change what fills a slot but never
    /// how many there are. A file that does not name exactly nine is refused with a reason rather than
    /// truncated - a short list would silently slide the meadow into the cliff slot.
    ///
    /// Resolved per call and threaded through the methods that need it, never held in a static field: the
    /// architecture rules forbid static mutable state, and two worlds in one process must not share it.
    /// </summary>
    private static (string Id, float UvScale, Color Tint)[] ResolveLayers(string? assetRoot, out string? why)
    {
        why = null;
        if (assetRoot is null)
            return DefaultLayers;
        string path = Path.Combine(assetRoot, "terrain_layers.json");
        if (!File.Exists(path))
            return DefaultLayers;
        try
        {
            var list = new List<(string Id, float UvScale, Color Tint)>();
            foreach (var entry in JsonNode.Parse(File.ReadAllText(path))!.AsArray())
                list.Add((entry!["id"]!.GetValue<string>(), entry["uv_scale"]!.GetValue<float>(),
                          Color.FromHtml(entry["tint"]!.GetValue<string>())));
            if (list.Count != DefaultLayers.Length)
            {
                why = $"{path} names {list.Count} terrain layers; a region needs exactly {DefaultLayers.Length}, "
                    + "because the slots are addressed by position";
                return DefaultLayers;
            }
            return list.ToArray();
        }
        catch (Exception e)
        {
            why = $"{path}: {e.Message}";
            return DefaultLayers;
        }
    }

    private const int ForestGround = 5, Earth = 6, Gravel = 7, GrassRock = 8;

    /// <summary>
    /// A region's own ground, from an optional <c>&lt;asset-root&gt;/terrain_cells.json</c>: one entry per cell, in the
    /// region's own <c>cells:</c> order, each naming the layer it stands on, the layer its patches show, and how
    /// strongly. Null (with a reason, when there was a file and it was wrong) means the region named none, and
    /// Ashen Hollow's own arrangement stands exactly as it was.
    /// </summary>
    private static CellPalette? ResolveCellPalette(string? assetRoot, out string? why)
    {
        why = null;
        if (assetRoot is null)
            return null;
        string path = Path.Combine(assetRoot, "terrain_cells.json");
        if (!File.Exists(path))
            return null;
        try
        {
            var cells = new List<CellColour>();
            foreach (var entry in JsonNode.Parse(File.ReadAllText(path))!["cells"]!.AsArray())
                cells.Add(new CellColour(
                    entry!["canopy"]!.GetValue<int>(), entry["base"]!.GetValue<int>(), entry["patch"]!.GetValue<int>(),
                    entry["mix_lo"]?.GetValue<float>() ?? 0f, entry["mix_hi"]?.GetValue<float>() ?? 1f,
                    entry["mix_scale"]?.GetValue<float>() ?? 1f, entry["canopy_scale"]?.GetValue<float>() ?? 0f));
            if (cells.Count != 4)
            {
                why = $"{path} names {cells.Count} cells; a region has four, one per entry in its own cells: list, and "
                    + "they are addressed by position";
                return null;
            }
            foreach (var cell in cells)
                foreach (int slot in new[] { cell.Base, cell.Patch, cell.Canopy })
                    if (slot < 0 || slot >= DefaultLayers.Length)
                    {
                        why = $"{path} names layer slot {slot}; a region has {DefaultLayers.Length}, addressed by position";
                        return null;
                    }
            return new CellPalette(cells.ToArray());
        }
        catch (Exception e)
        {
            why = $"{path}: {e.Message}";
            return null;
        }
    }

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
    private const int MinimumRegions = 4;          // Keep the original 512 m scenery apron for the two stand-alone towns.

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
        // The nine slots this region fills: its asset root may name its own, but the count and order are
        // the contract the blending below depends on. Resolved here and threaded down, never a static.
        var slots = ResolveLayers(art.Root, out string? layerWhy);
        var palette = ResolveCellPalette(art.Root, out string? paletteWhy);
        if (paletteWhy is not null)
            GD.PushWarning($"UNNAMED terrain cells: {paletteWhy}");
        if (palette is not null)
            GD.Print($"UNNAMED terrain cells: the region's own ground palette from {art.Root}");
        if (layerWhy is not null)
            GD.PushWarning($"UNNAMED terrain layers: {layerWhy}");
        var packed = PackedMaps(slots.Select(s => s.Id), art);
        var layers = new List<GodotObject>();
        for (int i = 0; i < slots.Length; i++)
        {
            var asset = TextureAsset(slots[i].Id, slots[i].UvScale, slots[i].Tint, art.Root, packed);
            if (asset is null)
            {
                why = $"terrain layer {slots[i].Id} is not in the asset workspace";
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
        // THE AUTOSHADER AND THE CONTROL MAP ARE TWO DIFFERENT ANSWERS TO THE SAME QUESTION, AND ONLY ONE OF THEM IS
        // BEING ASKED. Terrain3D's autoshader computes the layer blend from the SURFACE NORMAL -
        // `blend = clamp(1 + 2 * auto_slope * (normal.y - 1))`, which is 1 on the flat - and picks one base and one
        // overlay texture for the whole terrain. The region, meanwhile, bakes 1152 distinct control words into its
        // control map, with a non-zero blend on 64 percent of its texels. With auto_shader on, the map's blend is not
        // what the renderer uses: on flat ground the blend is 1 everywhere and one overlay layer covers the lot,
        // which is a single flat sheet of one texture across the entire moor. UNNAMED_TERRAIN_AUTOSHADER=0 turns it
        // off so the baked map drives the blending instead. It is an experiment with a stated hypothesis, not a fix.
        bool autoShader = OS.GetEnvironment("UNNAMED_TERRAIN_AUTOSHADER") != "0";
        material.Set("auto_shader", autoShader);
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
        int regions = Math.Max(MinimumRegions, (int)MathF.Ceiling(MathF.Max(ground.Region.End.X - origin.X,
            ground.Region.End.Y - origin.Y) / RegionSize));
        var (height, control, colour) = Images(grid, ground, origin, regions, detail ?? new Detail(Array.Empty<Vector3>(), Array.Empty<Rect2>(), Array.Empty<Vector2[]>()), slots, palette);
        // A debug affordance, not a game path: UNNAMED_DUMP_TERRAIN=<dir> writes the three images this renderer is
        // fed. They are the ONLY thing Terrain3D's clipmap has to read once its splat mips down, so when the ground
        // goes flat at distance the question "what colour is the map actually telling it" is answered by looking at
        // the map rather than by editing a shader and re-capturing. Born from three edits that measured inert
        // because they were made in files that are not in the frame.
        if (OS.GetEnvironment("UNNAMED_DUMP_TERRAIN") is { Length: > 0 } dumpDir)
        {
            DirAccess.MakeDirRecursiveAbsolute(dumpDir);
            height.SavePng(System.IO.Path.Combine(dumpDir, "terrain_height.png"));
            colour.SavePng(System.IO.Path.Combine(dumpDir, "terrain_colour.png"));
            // THE CONTROL MAP CANNOT BE SAVED AS A PNG DIRECTLY AND THE FIRST VERSION OF THIS DUMP LIED BECAUSE OF IT.
            // It is an Image.Format.Rf image whose 32-bit floats hold the control word's BITS (see the BitConverter
            // write above). PNG has no 32-bit float single-channel format, so Godot converts it to 8-bit greyscale -
            // and a control word such as 0x00400000 is a denormal float of about 5.9e-39, which converts to 0. Every
            // texel came out [0,0,0,255] and it was recorded as a hard fact that the terrain had one layer with no
            // blending, when what had actually happened was that the instrument destroyed the data on the way out.
            // The bytes are right, so take the raw bytes and put them in an RGBA8 image unchanged: four bytes per
            // texel either way, and the word is then legible as four channel values.
            var ctl = Image.CreateFromData(control.GetWidth(), control.GetHeight(), false, Image.Format.Rgba8, control.GetData());
            ctl.SavePng(System.IO.Path.Combine(dumpDir, "terrain_control.png"));
            // And say what is in it, so the next reader does not have to trust a picture of a bit pattern.
            byte[] raw = control.GetData();
            var words = new Dictionary<uint, int>();
            for (int i = 0; i + 3 < raw.Length; i += 4)
            {
                uint w = BitConverter.ToUInt32(raw, i);
                words[w] = words.GetValueOrDefault(w) + 1;
            }
            var top = words.OrderByDescending(kv => kv.Value).Take(6)
                .Select(kv => $"0x{kv.Key:X8}(base {(kv.Key >> 27) & 0x1F}, over {(kv.Key >> 22) & 0x1F}, blend {(kv.Key >> 14) & 0xFF})={kv.Value}");
            GD.Print($"UNNAMED terrain3d: dumped height, colour and control to {dumpDir}; " +
                     $"{words.Count} distinct control words over {raw.Length / 4} texels - {string.Join(", ", top)}");
            // And the test for the one line suspected of zeroing the blend everywhere. Strongest() reads:
            //     if (slope > 0.5f) { second = Cliff; blend = 0.55f * SmoothStep(0.5f, 1.6f, slope); }
            // If Slope sits marginally above 0.5 across the moor then that rule names dark rock as the overlay AND
            // sets the blend to about zero AND discards the patch mix that would have blended heather or litter into
            // the grass, all at once - which is what a control map with a zero blend byte on 99 percent of its texels
            // looks like. This prints the distribution so the question is settled by a number, not by reading the line.
            var bands = new int[21];
            for (int py = 0; py < height.GetHeight(); py++)
                for (int px = 0; px < height.GetWidth(); px++)
                    bands[Math.Clamp((int)(ground.Slope(origin.X + px, origin.Y + py) * 10f), 0, 20)]++;
            GD.Print("UNNAMED terrain3d: ground.Slope distribution over the whole grid, 0.1 bands: " +
                     string.Join(" ", Enumerable.Range(0, 21).Where(b => bands[b] > 0)
                         .Select(b => $"{b / 10f:0.0}-{(b + 1) / 10f:0.0}={bands[b]}")));
        }
        var data = (GodotObject)terrain.Get("data");
        data.Call("import_images", new Godot.Collections.Array { height, control, colour }, new Vector3(origin.X, 0, origin.Y), 0.0f, 1.0f);
        GD.Print($"UNNAMED terrain3d: {regions}x{regions} regions of {RegionSize} m from {origin}; " + Check(data, grid, ground));
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
    private static (Image Height, Image Control, Image Colour) Images(TerrainGrid grid, GroundField ground, Vector2 origin, int regions, Detail detail, (string Id, float UvScale, Color Tint)[] slots, CellPalette? palette)
    {
        var region = ground.Region;
        int size = RegionSize * regions;
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
                (c, tint) = Composed(ground, x, z, detail, slots, palette);
            }
            else
            {
                h = Outside(ground, x, z, noise);
                c = Encode(SlotLayer[1], Cliff, 0, auto: true);
                // MARK THE OUT-OF-REGION TERRAIN. Everything beyond the walkable region keeps the white tint set above
                // and this one constant control word, so it is one layer with no blend and a flat multiply - a flat
                // sheet by construction, and immune to the palette, the patch layers, the tint and the macro
                // variation, all of which are applied by ComposedRegion and only inside the region. The flat
                // mid-ground band has now survived six changes made inside the region, including two to these very
                // maps, so the question "is the band simply terrain that is not in the region" is worth an
                // unmistakable answer rather than a projection. UNNAMED_TERRAIN_MARKOUTSIDE=1 tints every outside
                // texel magenta: if the band turns magenta it is out-of-region ground, and if it stays green this
                // hypothesis is dead like the other six.
                if (OS.GetEnvironment("UNNAMED_TERRAIN_MARKOUTSIDE") == "1")
                    tint = new Color(1f, 0f, 1f, 0.5f);
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
    /// <summary>
    /// How a region's ground is made: which layer each of its four cells stands on, which layer shows through its
    /// patches, and how strongly. Ashen Hollow's own arrangement is the default; a region that names its own in
    /// <c>&lt;asset-root&gt;/terrain_cells.json</c> is read from there instead. What this replaces was four named cell
    /// roles - quarry, meadow, fold, wood - written into the presentation, which meant a second region could only
    /// ever inherit the first one's ground.
    /// </summary>
    private sealed record CellPalette(CellColour[] Cells);

    /// <summary>One cell's ground: the layer under a canopy, the layer on open ground, and the layer in its patches.</summary>
    private sealed record CellColour(int Canopy, int Base, int Patch, float MixLo, float MixHi, float MixScale, float CanopyScale);

    private static (uint Control, Color Tint) Composed(GroundField ground, float x, float z, Detail detail,
        (string Id, float UvScale, Color Tint)[] slots, CellPalette? palette)
        => palette is null ? ComposedHollow(ground, x, z, detail, slots) : ComposedRegion(ground, x, z, detail, slots, palette);

    private static (uint Control, Color Tint) ComposedHollow(GroundField ground, float x, float z, Detail detail, (string Id, float UvScale, Color Tint)[] slots)
    {
        var w = ground.CellWeights(x, z);
        // slots: (W,S) quarry, (W,N) waystation, (E,S) Foldscar, (E,N) Charwood
        float quarry = w.X, meadow = w.Y, fold = w.Z, wood = w.W;
        var weight = new float[slots.Length];
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
        // Colour: a slow warm/cool drift across the open ground, wet ground darker and glossier, the Foldscar's ash greyer.
        float drift = 1 + 0.07f * macro;
        var tint = new Color(drift * (1 + 0.03f * macro), drift, drift * (1 - 0.04f * macro), 0.5f);
        tint = tint.Lerp(new Color(0.72f, 0.70f, 0.68f, 0.38f), wet * 0.8f);
        tint = tint.Lerp(new Color(0.88f, 0.87f, 0.88f, 0.5f), fold * 0.5f);
        tint = tint.Lerp(new Color(0.86f, 0.84f, 0.80f, 0.5f), canopy * wood * 0.35f);
        return Strongest(ground, x, z, weight, tint);
    }

    /// <summary>
    /// A region's ground, from the palette it declares: its four cells, each standing on its own layer with its own
    /// layer showing through the patches. Everything that is true everywhere - leaf litter under a crown, trodden
    /// earth at a wall, mud along the drainage, the worn paths, the dark rock of a steep bank - is applied on top,
    /// the same way for every region.
    /// </summary>
    private static (uint Control, Color Tint) ComposedRegion(GroundField ground, float x, float z, Detail detail,
        (string Id, float UvScale, Color Tint)[] slots, CellPalette palette)
    {
        var w = ground.CellWeights(x, z);
        float[] cells = { w.X, w.Y, w.Z, w.W };
        var weight = new float[slots.Length];
        float patch = Patches.GetNoise2D(x, z);
        float macro = Macro.GetNoise2D(x, z);

        // The canopy first: it is the one thing that overrides a cell's own ground.
        float canopy = 0;
        foreach (var crown in detail.Crowns)
        {
            float d = new Vector2(x - crown.X, z - crown.Y).Length();
            canopy = MathF.Max(canopy, 1 - GroundField.SmoothStep(crown.Z * 0.55f, crown.Z * 1.15f, d));
        }

        for (int c = 0; c < cells.Length && c < palette.Cells.Length; c++)
        {
            if (cells[c] <= 0)
                continue;
            var colour = palette.Cells[c];
            float mix = colour.MixScale * GroundField.SmoothStep(colour.MixLo, colour.MixHi, patch);
            weight[colour.Canopy] += cells[c] * canopy * colour.CanopyScale;
            weight[colour.Base] += cells[c] * (1 - canopy * colour.CanopyScale) * (1 - mix);
            weight[colour.Patch] += cells[c] * (1 - canopy * colour.CanopyScale) * mix;
        }

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

        float path = ground.PathAt(x, z);
        weight[Trail] += 2.2f * GroundField.SmoothStep(0.08f, 0.6f, path);

        // Colour: the same slow drift, the wet ground darker, and the litter under a crown warmer.
        //
        // MEASURED INERT ON THE THING IT WAS WRITTEN FOR - do not assume otherwise. The flat mid-ground band was
        // chased to here on the reasoning that this baked tint is the ground's colour at every distance, and the
        // band's flattest row measured a luminance standard deviation of 0.03615 BEFORE this change and 0.03643
        // AFTER. No effect. So neither GroundField's fragment shader nor this tint is what makes that band flat, and
        // two edits have now been spent proving it. The change is kept because two scales and a straw drift is
        // better ground than one noise at +-7%, and this note is here so the next person starts from the
        // measurement rather than from the reasoning.
        // THE TINT IS A MULTIPLY AND AN RGBA8 IMAGE CLAMPS, WHICH IS WHY THE FIRST ATTEMPT AT THIS MEASURED INERT.
        // The map came out at mean (250.09, 248.61, 233.22) out of 255 with a standard deviation of 7.5 - a three
        // percent white multiply, i.e. no variation at all. The reason is this line: drift ran from 1 +- 0.13, so
        // most of the intended variation sat ABOVE 1.0 and was thrown away as white on the way into the byte image.
        // The baseline is lowered so the whole range fits under 1.0 and the variation actually lands in the map.
        // The control map is NOT the problem and neither is the sky: measured inside the walkable region, the
        // terrain carries 1152 distinct control words with a non-zero blend on 64 percent of its texels averaging
        // 63/255, so the layers blend properly. This tint is what the far clipmap has left once the splat mips down.
        float macro2 = Macro.GetNoise2D(x * 0.34f, z * 0.34f);
        float drift = 0.84f + 0.10f * macro + 0.06f * macro2;      // 0.68 .. 1.00, all of it inside the byte image
        var tint = new Color(drift * (1 + 0.05f * macro), drift, drift * (1 - 0.06f * macro), 0.5f);
        tint = tint.Lerp(new Color(0.98f, 0.94f, 0.74f, 0.5f), Math.Clamp((macro2 + 0.35f) * 0.9f, 0f, 0.45f));
        tint = tint.Lerp(new Color(0.72f, 0.70f, 0.68f, 0.38f), wet * 0.8f);
        tint = tint.Lerp(new Color(0.86f, 0.84f, 0.80f, 0.5f), canopy * 0.35f);
        return Strongest(ground, x, z, weight, tint);
    }

    /// <summary>The two strongest layers, with a steep bank taking the dark rock over whatever the ground is.</summary>
    private static (uint Control, Color Tint) Strongest(GroundField ground, float x, float z, float[] weight, Color tint)
    {
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
    /// Beyond the edge (scenery): the ravine on the west, north and east - a sheer drop from the edge to a floor about 25 m below the den lip, a gulf,
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
        float floor = -17f + n2 * 1.2f;
        float ravine;
        if (d < 2f)
            ravine = edge - d * 0.35f;                                      // a soft lip: the playable edge never meets the sheer drop
        else if (d < 8f)
            ravine = Mathf.Lerp(edge - 0.7f, floor, GroundField.SmoothStep(2f, 8f, d) * 0.7f + (d - 2f) / 6f * 0.3f);
        else if (d < 26f)
            ravine = floor;
        else if (d < 40f)
            ravine = Mathf.Lerp(floor, 14f + n * 8f, GroundField.SmoothStep(26f, 40f, d));
        else
            ravine = 14f + n * 8f + GroundField.SmoothStep(40f, 180f, d) * (26f + n * 18f);
        float cliff = d < 12f
            ? Mathf.Lerp(edge, edge + 20f + n * 5f, GroundField.SmoothStep(0f, 12f, d))
            : edge + 20f + n * 5f + GroundField.SmoothStep(12f, 180f, d) * (24f + n * 16f);
        return Mathf.Lerp(ravine, cliff, south) + n2 * 0.6f * GroundField.SmoothStep(0f, 2f, d);
    }

    /// <summary>Terrain3D's control word: base id bits 31-27, overlay 26-22, blend 21-14, hole bit 2, navigation bit 1, autoshader bit 0.</summary>
    private static uint Encode(int baseLayer, int overlay, int blend, bool auto) =>
        ((uint)(baseLayer & 0x1F) << 27) | ((uint)(overlay & 0x1F) << 22) | ((uint)(blend & 0xFF) << 14) | (auto ? 1u : 0u);

    private static Dictionary<string, ImageTexture> PackedMaps(IEnumerable<string> ids, ArtLibrary art)
    {
        var maps = new Dictionary<string, ImageTexture>(StringComparer.Ordinal);
        if (art.PackedTerrainCache is not { } cache || art.Root is null) return maps;
        // Native texture-array layers must have one format. A partial cache must never mix BC7 and RGBA8.
        foreach (string id in ids)
            foreach (string suffix in new[] { "_albedo_height.png", "_normal_rough.png" })
            {
                string path = Path.Combine(art.Root, "materials", id, id + suffix);
                if (cache.For(TextureCache.Key(Path.GetRelativePath(art.Root, path)), path,
                        () => Image.LoadFromFile(path), "data") is not ImageTexture texture)
                {
                    maps.Clear();
                    GD.Print("UNNAMED packed terrain cache incomplete: using the original RGBA8 layer set");
                    return maps;
                }
                maps[path] = texture;
            }
        GD.Print($"UNNAMED packed terrain cache: {maps.Count} full-resolution maps, {cache.CompressedBytes / 1048576.0:0.0} MB BC7");
        return maps;
    }

    private static GodotObject? TextureAsset(string id, float uvScale, Color tint, string? assetRoot, IReadOnlyDictionary<string, ImageTexture> packed)
    {
        if (assetRoot is null)
            return null;
        string folder = Path.Combine(assetRoot, "materials", id);
        var albedo = LoadTexture(Path.Combine(folder, id + "_albedo_height.png"), packed);
        var normal = LoadTexture(Path.Combine(folder, id + "_normal_rough.png"), packed);
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

    private static ImageTexture? LoadTexture(string path, IReadOnlyDictionary<string, ImageTexture> packed)
    {
        if (!File.Exists(path))
            return null;
        if (packed.TryGetValue(path, out var compressed)) return compressed;
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
            return null;
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
