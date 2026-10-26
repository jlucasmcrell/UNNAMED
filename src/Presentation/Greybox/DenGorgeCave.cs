// UNNAMED Presentation - the hollow under the wolf den: the cave's rock, its light and its dressing (spec 28)
// Godot presentation only (D-11): drawn from the interior's own layout (its heights and wall boxes); nothing here is read by the game

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The cave behind Halda's ledge. Its rock is the layout's own: where a wall box stands is rock, where the body can walk is floor, so
/// the shell is drawn from the same data the body collides with - a floor that curls up into the walls where the boxes start, and a
/// roof that comes down to meet it, rising with the distance from the nearest wall (low in the squeeze, high over the great chamber).
/// The ledge itself is the exterior's (DenGorge): only the cave behind the lip and the passage out to the ledge are drawn here.
/// </summary>
public static class DenGorgeCave
{
    public static readonly SpaceId Space = SpaceId.Parse("space.den_gorge_cave");

    public static bool Owns(RegionLayout layout) => layout.SpaceId == Space;

    /// <summary>Where the cave's own air begins: nearer the lip than this is the gorge's.</summary>
    public const float AirLineZ = 797f;

    /// <summary>The crack in the great chamber's roof under the den's notch, where the daylight and the pack's leavings come down.</summary>
    private static readonly Vector2 Crack = new(511.5f, 773.5f);

    private const float Step = 0.5f, Rise = 2.8f;

    /// <summary>
    /// The highest the roof may rise: a metre of rock under the lowest of the den's ground above the cave (5.4 m), so the hollow never
    /// reaches the frontier's surface - drawn from below, that surface is not rock.
    /// </summary>
    private const float Overhead = 4.4f;

    public static Node3D Build(RegionLayout layout, ArtLibrary art)
    {
        var root = new Node3D { Name = "DenGorgeCave" };
        var grid = layout.Space.Terrain;
        float x0 = grid.OriginXMm / 1000f, z0 = grid.OriginZMm / 1000f, spacing = grid.SpacingMm / 1000f;
        int cols = grid.Columns, rows = grid.Rows;
        var walls = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith("gorge_wall_", StringComparison.Ordinal)).ToArray();
        bool Walled(float x, float z) => walls.Any(b => x * 1000 >= b.MinXMm && x * 1000 <= b.MaxXMm && z * 1000 >= b.MinZMm && z * 1000 <= b.MaxZMm);
        // The floor: every grid point the body can reach from the spawn without crossing a wall.
        var floor = new bool[cols, rows];
        var queue = new Queue<(int I, int J)>();
        var spawn = ((int)MathF.Round((layout.Spawn.XMm / 1000f - x0) / spacing), (int)MathF.Round((layout.Spawn.ZMm / 1000f - z0) / spacing));
        queue.Enqueue(spawn);
        floor[spawn.Item1, spawn.Item2] = true;
        while (queue.TryDequeue(out var p))
        {
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int i = p.I + di, j = p.J + dj;
                if (i < 0 || j < 0 || i >= cols || j >= rows || floor[i, j] || Walled(x0 + i * spacing, z0 + j * spacing))
                    continue;
                floor[i, j] = true;
                queue.Enqueue((i, j));
            }
        }
        var rockEdge = new List<Vector2>();
        var floorEdge = new List<Vector2>();
        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
        {
            bool beside = false;
            for (int b = Math.Max(0, j - 1); b <= Math.Min(rows - 1, j + 1); b++)
                for (int a = Math.Max(0, i - 1); a <= Math.Min(cols - 1, i + 1); a++)
                    beside |= floor[a, b] != floor[i, j];
            if (beside)
                (floor[i, j] ? floorEdge : rockEdge).Add(new Vector2(x0 + i * spacing, z0 + j * spacing));
        }
        // How far into the floor (positive) or the rock (negative) a point is, in metres from the wall face.
        float Inside(float x, float z)
        {
            var p = new Vector2(x, z);
            float rock = rockEdge.Min(e => e.DistanceSquaredTo(p)), open = floorEdge.Min(e => e.DistanceSquaredTo(p));
            return (MathF.Sqrt(rock) - MathF.Sqrt(open)) * 0.5f;
        }
        var noise = new FastNoiseLite { Seed = 2828, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.35f };
        float Height(float x, float z) => grid.HeightAtMm((long)(x * 1000), (long)(z * 1000)) / 1000f;
        // Drawn: the cave behind the lip and the passage out through the face to the ledge; not the ledge, which the exterior draws.
        bool Drawn(float x, float z) => z < 800.6f || (z < 801.45f && x > 534f && x < 544f);
        int nx = (int)((cols - 1) * spacing / Step) + 1, nz = (int)((rows - 1) * spacing / Step) + 1;
        var inside = new float[nx, nz];
        var floorY = new float[nx, nz];
        var roofY = new float[nx, nz];
        for (int b = 0; b < nz; b++)
        for (int a = 0; a < nx; a++)
        {
            float x = x0 + a * Step, z = z0 + b * Step;
            float d = Inside(x, z), h = Height(x, z);
            float n = noise.GetNoise2D(x, z), n2 = noise.GetNoise2D(x * 2.3f + 40, z * 2.3f - 17);
            inside[a, b] = d;
            // In the rock the floor and the roof are one height, so the shell is closed wherever the walls stand.
            // The roof: rising from the wall, kept low through the face where the passage comes out under the lip, never above Overhead.
            float clear = Rise + 1.7f * Math.Clamp(d + 0.9f, 0f, 6f);
            clear = Mathf.Lerp(clear, MathF.Min(clear, 4.2f), GroundField.SmoothStep(794f, 799f, z));
            float vault = MathF.Max(h + 2.4f, MathF.Min(h + clear * (0.92f + 0.16f * n) + 0.25f * n2, Overhead));
            float rockTop = MathF.Min(h + Rise * (0.9f + 0.2f * n), vault);
            floorY[a, b] = Mathf.Lerp(h + 0.06f * n2, rockTop, GroundField.SmoothStep(0.25f, -0.9f, d));
            roofY[a, b] = Mathf.Lerp(rockTop, vault, GroundField.SmoothStep(-0.9f, 0.25f, d));
        }
        var stone = new SurfaceTool();
        var ground = new SurfaceTool();
        var roof = new SurfaceTool();
        stone.Begin(Mesh.PrimitiveType.Triangles);
        ground.Begin(Mesh.PrimitiveType.Triangles);
        roof.Begin(Mesh.PrimitiveType.Triangles);
        var roofFaces = new List<Vector3>();
        Vector3 F(int a, int b) => new(x0 + a * Step, floorY[a, b], z0 + b * Step);
        Vector3 R(int a, int b) => new(x0 + a * Step, roofY[a, b], z0 + b * Step);
        // The roof faces down: its normal from its own slope, pointing into the cave (not left to winding, which lit it as sky).
        Vector3 Down(int a, int b)
        {
            float dx = (roofY[Math.Min(a + 1, nx - 1), b] - roofY[Math.Max(a - 1, 0), b]) / (2f * Step);
            float dz = (roofY[a, Math.Min(b + 1, nz - 1)] - roofY[a, Math.Max(b - 1, 0)]) / (2f * Step);
            return new Vector3(dx, -1f, dz).Normalized();
        }
        for (int b = 0; b + 1 < nz; b++)
        for (int a = 0; a + 1 < nx; a++)
        {
            float x = x0 + (a + 0.5f) * Step, z = z0 + (b + 0.5f) * Step;
            float near = MathF.Max(MathF.Max(inside[a, b], inside[a + 1, b]), MathF.Max(inside[a, b + 1], inside[a + 1, b + 1]));
            if (near < -2.5f || !Drawn(x, z))
                continue;
            // Wound clockwise from above for the floor (its front faces up); the roof the other way (its front faces down).
            var (p00, p10, p01, p11) = (F(a, b), F(a + 1, b), F(a, b + 1), F(a + 1, b + 1));
            var tool = MathF.Min(MathF.Min(inside[a, b], inside[a + 1, b]), MathF.Min(inside[a, b + 1], inside[a + 1, b + 1])) > -0.2f ? ground : stone;
            foreach (var v in new[] { p00, p10, p11, p00, p11, p01 })
                tool.AddVertex(v);
            foreach (var (i, j) in new[] { (a, b), (a + 1, b + 1), (a + 1, b), (a, b), (a, b + 1), (a + 1, b + 1) })
            {
                var v = R(i, j);
                roof.SetNormal(Down(i, j));
                roof.AddVertex(v);
                roofFaces.Add(v);
            }
        }
        // The shell is the dark behind the rock that lines the walls (Dress): darker on the walls, darkest overhead.
        var stoneMaterial = Darkened(art.WorldMaterial("cw_dark_rock"), 0.7f);
        foreach (var (tool, material, name) in new[] { (stone, stoneMaterial, "walls"), (ground, art.WorldMaterial("cw_rock_ground") ?? stoneMaterial, "floor"),
                     (roof, Darkened(art.WorldMaterial("cw_dark_rock"), 0.42f), "roof") })
        {
            if (tool != roof)
                tool.GenerateNormals();
            root.AddChild(new MeshInstance3D { Name = name, Mesh = tool.Commit(), MaterialOverride = material });
        }
        // The roof stops the camera, as the wall boxes do: it never rises through the rock into the gorge's daylight.
        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(roofFaces.ToArray());
        var collider = new StaticBody3D { Name = "RoofCollider", CollisionLayer = 1, CollisionMask = 0 };
        collider.AddChild(new CollisionShape3D { Shape = shape });
        root.AddChild(collider);
        Light(root, Height);
        Dress(root, art, Height, Inside);
        Line(root, art, Height, rockEdge, floorEdge);
        return root;
    }

    private static Material Darkened(Material? world, float by)
    {
        if (world?.Duplicate() is not BaseMaterial3D rock)
            return new StandardMaterial3D { AlbedoColor = new Color(0.2f * by, 0.19f * by, 0.18f * by), Roughness = 0.95f };
        rock.AlbedoColor = new Color(by, by, by);
        return rock;
    }

    /// <summary>
    /// The cave's walls: scanned rock lining the line where the floor meets the rock, every three metres or so, each piece turned to face
    /// into the cave and stood just inside the wall the body stops at, tall enough that its top is lost in the roof.
    /// </summary>
    private static void Line(Node3D root, ArtLibrary art, Func<float, float, float> height, List<Vector2> rockEdge, List<Vector2> floorEdge)
    {
        var random = new RandomNumberGenerator { Seed = 0x5EED_D02D };
        var taken = new HashSet<(int, int)>();
        foreach (var rock in rockEdge)
        {
            if (rock.Y > 801.4f || !taken.Add(((int)MathF.Floor(rock.X / 3.2f), (int)MathF.Floor(rock.Y / 3.2f))))
                continue;
            var open = floorEdge.MinBy(f => f.DistanceSquaredTo(rock));
            var inward = (open - rock).Normalized();
            float s = random.RandfRange(0.42f, 0.6f);
            var front = rock + inward * 0.45f;
            var at = new Vector3(front.X - inward.X * 1.57f * s, height(open.X, open.Y) - 0.4f, front.Y - inward.Y * 1.57f * s);
            if (art.ModelWithLods("env_ph_mountainside") is not { } shell)
                continue;
            shell.Position = at;
            shell.RotationDegrees = new Vector3(random.RandfRange(-6, 4), Mathf.RadToDeg(MathF.Atan2(inward.X, inward.Y)) + random.RandfRange(-12, 12), random.RandfRange(-6, 6));
            shell.Scale = Vector3.One * s;
            Palette.Tinted(shell, Palette.Shifted($"{at.X:F1},{at.Z:F1}", new Color(0.72f, 0.70f, 0.68f)));
            root.AddChild(shell);
        }
        // Rock hanging from the great chamber's roof and the antechamber's.
        foreach (var (x, z, s) in new[] { (514f, 762f, 1.4f), (522f, 768f, 1.1f), (509f, 769f, 0.9f), (526f, 759f, 1.2f), (541f, 787f, 0.8f) })
        {
            if (art.ModelWithLods("rock_ph_moss_boulder_l") is not { } hanging)
                continue;
            hanging.Position = new Vector3(x, height(x, z) + 8.5f * (s > 1f ? 1f : 0.6f), z);
            hanging.RotationDegrees = new Vector3(180f + random.RandfRange(-15, 15), random.RandfRange(0, 360), random.RandfRange(-10, 10));
            hanging.Scale = Vector3.One * s;
            root.AddChild(hanging);
        }
    }

    /// <summary>
    /// The cave's light: the day coming down the crack into the great chamber, and a faint cold fill in each chamber so the rock reads
    /// without a torch - darker than the day by far, never black.
    /// </summary>
    private static void Light(Node3D root, Func<float, float, float> height)
    {
        float floor = height(Crack.X, Crack.Y);
        // The crack itself: a slit of daylight in the roof (the den's ground above is not opened; the light is what comes through).
        var slit = new MeshInstance3D
        {
            Name = "CrackSlit", Mesh = new QuadMesh { Size = new Vector2(0.55f, 3.2f) }, Position = new Vector3(Crack.X, Overhead - 0.25f, Crack.Y),
            RotationDegrees = new Vector3(90, 18, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.95f, 0.97f, 1f), EmissionEnabled = true,
                Emission = new Color(0.9f, 0.95f, 1f), EmissionEnergyMultiplier = 2.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        root.AddChild(slit);
        root.AddChild(new DenGorgeCaveDay { Name = "Daylight" });
        root.AddChild(new SpotLight3D
        {
            // Just under the crack, not in the rock above it: from there the roof would shadow everything it lights.
            Name = "CrackDaylight", Position = new Vector3(Crack.X, Overhead - 0.6f, Crack.Y), RotationDegrees = new Vector3(-90, 0, 0),
            LightColor = new Color(1f, 0.94f, 0.82f), LightEnergy = 12f, SpotRange = 16f, SpotAngle = 22f, SpotAttenuation = 0.8f,
            ShadowEnabled = true, LightVolumetricFogEnergy = 2.5f,
        });
        foreach (var (x, z, energy, range) in new[] { (540f, 787f, 1.8f, 11f), (512f, 762f, 2.8f, 15f), (525f, 767f, 2.8f, 15f), (507f, 770f, 1.6f, 10f), (528f, 758f, 1.6f, 10f),
                     (540.5f, 755.5f, 1.4f, 8f), (532f, 777f, 1.2f, 8f), (539.5f, 794f, 1.0f, 8f) })
            root.AddChild(new OmniLight3D
            {
                Name = $"Fill_{x:F0}_{z:F0}", Position = new Vector3(x, height(x, z) + 3f, z), LightColor = new Color(0.62f, 0.70f, 0.82f),
                LightEnergy = energy, OmniRange = range, OmniAttenuation = 0.8f, ShadowEnabled = false,
            });
        // The great chamber's own air: one broad, soft fill high under its vault, so the matriarch is a shape and not a shadow.
        root.AddChild(new OmniLight3D
        {
            Name = "ChamberFill", Position = new Vector3(518f, Overhead - 1.2f, 765f), LightColor = new Color(0.66f, 0.72f, 0.82f),
            LightEnergy = 1.6f, OmniRange = 24f, OmniAttenuation = 0.6f, ShadowEnabled = false,
        });
    }

    private static readonly string[] Stones = { "rock_ph_moss_boulder_l", "rock_ph_moss_boulder_m", "env_ph_boulder_01", "gorge_rock_face", "env_ph_namaqualand_boulder_02" };

    /// <summary>Broken stone along the walls, roots come through the antechamber's roof from the Charwood above, and what the crack lets fall.</summary>
    private static void Dress(Node3D root, ArtLibrary art, Func<float, float, float> height, Func<float, float, float> inside)
    {
        var random = new RandomNumberGenerator { Seed = 0x5EED_D02C };
        void Put(string id, Vector3 at, Vector3 turn, float scale)
        {
            if (art.ModelWithLods(id) is not { } model)
                return;
            model.Position = at;
            model.RotationDegrees = turn;
            model.Scale = Vector3.One * scale;
            Palette.Tinted(model, Palette.Shifted($"{at.X:F1},{at.Z:F1}", new Color(0.8f, 0.8f, 0.8f)));
            root.AddChild(model);
        }
        // Stone at the foot of the walls: tried along a grid, kept where the point is just out of the floor.
        for (float z = 745f; z < 800f; z += 2.1f)
        for (float x = 498f; x < 566f; x += 2.3f)
        {
            float jx = x + random.RandfRange(-0.8f, 0.8f), jz = z + random.RandfRange(-0.8f, 0.8f);
            float d = inside(jx, jz);
            if (d > -0.15f || d < -0.9f || random.Randf() < 0.45f)
                continue;
            Put(Stones[random.RandiRange(0, Stones.Length - 1)], new Vector3(jx, height(jx, jz) - 0.2f, jz),
                new Vector3(random.RandfRange(-15, 15), random.RandfRange(0, 360), random.RandfRange(-15, 15)), random.RandfRange(0.5f, 1.2f));
        }
        // Roots hanging through the antechamber's roof and the passage's.
        foreach (var (x, z) in new[] { (537.5f, 788f), (542.5f, 784.5f), (539f, 795f), (544f, 788.5f), (535.5f, 783.5f) })
            Put("gorge_root_cluster", new Vector3(x, height(x, z) + 4.6f, z), new Vector3(180f + random.RandfRange(-12, 12), random.RandfRange(0, 360), 0), random.RandfRange(0.9f, 1.3f));
        // Under the crack: a fallen limb and the stones that came with it.
        Put("flora_fallen_log", new Vector3(Crack.X + 1.2f, height(Crack.X, Crack.Y) - 0.1f, Crack.Y + 0.5f), new Vector3(4, 35, 6), 0.8f);
        for (int i = 0; i < 6; i++)
        {
            float x = Crack.X + random.RandfRange(-2.5f, 2.5f), z = Crack.Y + random.RandfRange(-2f, 2f);
            Put("rock_ph_moss_pebble_a", new Vector3(x, height(x, z), z), new Vector3(0, random.RandfRange(0, 360), 0), random.RandfRange(2f, 5f));
        }
        // The way on: silk across its throat, thick enough to stop a body, the dark going down behind it.
        var silk = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.86f, 0.86f, 0.82f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.6f, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        };
        for (int i = 0; i < 4; i++)
        {
            float x = 501.5f + i * 0.35f, z = 751.5f + i * 0.4f;
            root.AddChild(new MeshInstance3D
            {
                Name = $"Silk_{i}", Mesh = new QuadMesh { Size = new Vector2(3.4f, 3.2f) }, MaterialOverride = silk,
                Position = new Vector3(x, height(x, z) + 1.6f, z), RotationDegrees = new Vector3(random.RandfRange(-8, 8), 48f + random.RandfRange(-10, 10), random.RandfRange(-10, 10)),
            });
        }
    }
}

/// <summary>The crack's light follows the day: full at noon, gone at night, so the hollow's one window never shines in the dark.</summary>
public partial class DenGorgeCaveDay : Node
{
    public override void _Process(double delta)
    {
        float day = SkyView.DaylightLevel;
        if (GetParent().GetNodeOrNull<SpotLight3D>("CrackDaylight") is { } light)
            light.LightEnergy = 12f * day;
        if (GetParent().GetNodeOrNull<MeshInstance3D>("CrackSlit") is { } slit)
            slit.Visible = day > 0.05f;
    }
}
