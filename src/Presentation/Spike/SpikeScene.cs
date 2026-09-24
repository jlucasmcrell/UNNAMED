// UNNAMED Presentation - the isolated 2x2 km greybox frame-budget spike (ROADMAP.md M3 RISK SPIKE; RISK_REGISTER.md RK-02)
// Godot presentation only. Explicitly NOT the playable prototype, and not the formal D-01 revisit gate in Phase 1.

using System.Diagnostics;
using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Perf;

namespace UNNAMED.Presentation.Spike;

/// <summary>
/// 2x2 km of untextured heightmap, a few hundred instanced proxies, a navmesh, and the D-06 tier scaffolding over the
/// region's 400 cells, measured along a scripted camera path: a third-person follow, a first-person walk, and a camera
/// weaving through a dense cluster (obstruction), with the tier-A actor cap of 60 animated stand-ins in view. No content,
/// no player character, no save round trip. Its only output is a frame-time capture (<see cref="FrameStats"/>).
/// </summary>
public partial class SpikeScene : Node3D
{
    private const float Size = 2000f;
    private const int Points = 401;              // 5 m spacing
    private const int ActorCap = 60;             // WORLD_ARCHITECTURE.md §6.2: tier A, at most 60 actors
    private const int Trees = 300;
    private const int Rocks = 150;
    private const int Tiles = 8;                 // the navmesh is baked as 8 x 8 tiles of 250 m, stitched at their borders
    private const string NavigationGroup = "spike_navigation";
    private static readonly TierRules Tiers = new(150_000, 600_000, 2_000_000, 10_000);

    private readonly string _out;
    private readonly (string Name, double Seconds)[] _segments;
    private readonly Camera3D _camera = new() { Name = "DebugCamera", Fov = 75, Near = 0.05f, Far = 4000 };
    private readonly SimulationTier[] _cellTiers = Enumerable.Repeat(SimulationTier.D, 400).ToArray();
    private readonly long[] _tierCounts = new long[4];
    private MultiMesh _actors = null!;
    private readonly Queue<NavigationRegion3D> _unbaked = new();
    private int _polygons;
    private FrameStats _stats = null!;
    private readonly Stopwatch _bake = new();
    private string _bakeResult = "not started";
    private int _segment = -1;
    private double _elapsed;
    private double _tickAccumulator;
    private long _tierSamples;
    private bool _shot;

    public SpikeScene(string outDirectory, double segmentSeconds)
    {
        _out = outDirectory;
        _segments = new[] { ("warmup", 5.0), ("third_person", segmentSeconds), ("first_person", segmentSeconds), ("obstruction", segmentSeconds) };
    }

    public override void _Ready()
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        AddChild(_camera);
        _camera.Current = true;
        BuildTerrain();
        BuildProxies();
        BuildActors();
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, 35, 0), ShadowEnabled = true });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                FogEnabled = true,
                FogDensity = 0.0008f,
            },
        });
        _stats = new FrameStats(GetViewport());
        _stats.Segment = "navmesh_bake";
        _bake.Start();
        _bakeResult = "baking";
        _unbaked.Dequeue().BakeNavigationMesh(onThread: true);
        GD.Print($"UNNAMED spike: 2x2 km greybox built; baking the navmesh as {Tiles * Tiles} tiles");
    }

    public override void _Process(double delta)
    {
        UpdateActors();
        UpdateTiers(delta);

        // Measure only once the navmesh exists; the bake itself is recorded as its own segment.
        if (_bake.IsRunning && _bake.Elapsed.TotalSeconds < 300)
        {
            FlyCamera(0);
            _stats.Record(delta);
            return;
        }
        if (_bake.IsRunning)
        {
            _bake.Stop();
            _bakeResult = "did not finish within 300 s";
        }

        if (_segment < 0 || _elapsed >= _segments[_segment].Seconds)
        {
            if (++_segment >= _segments.Length)
            {
                Finish();
                return;
            }
            _elapsed = 0;
            _shot = false;
            _stats.Segment = _segments[_segment].Name;
        }
        _elapsed += delta;
        FlyCamera(_elapsed);
        _stats.Record(delta);
        if (!_shot && _elapsed >= _segments[_segment].Seconds / 2)
        {
            Main.SaveScreenshot(GetViewport(), _out, _segments[_segment].Name);
            _stats.SkipNext();
            _shot = true;
        }
    }

    private static float Height(float x, float z) =>
        30f * Mathf.Sin(x / 170f) * Mathf.Cos(z / 210f) + 12f * Mathf.Sin((x + z) / 90f) + 4f * Mathf.Sin(x / 23f) * Mathf.Sin(z / 31f);

    private void BuildTerrain()
    {
        float spacing = Size / (Points - 1);
        var vertices = new Vector3[Points * Points];
        var normals = new Vector3[Points * Points];
        var heights = new float[Points * Points];
        for (int j = 0; j < Points; j++)
        for (int i = 0; i < Points; i++)
        {
            float x = i * spacing, z = j * spacing, y = Height(x, z);
            vertices[j * Points + i] = new Vector3(x, y, z);
            heights[j * Points + i] = y;
            var dx = new Vector3(2 * spacing, Height(x + spacing, z) - Height(x - spacing, z), 0);
            var dz = new Vector3(0, Height(x, z + spacing) - Height(x, z - spacing), 2 * spacing);
            normals[j * Points + i] = dz.Cross(dx).Normalized();
        }
        var indices = new int[(Points - 1) * (Points - 1) * 6];
        int k = 0;
        for (int j = 0; j < Points - 1; j++)
        for (int i = 0; i < Points - 1; i++)
        {
            int p00 = j * Points + i, p10 = p00 + 1, p01 = p00 + Points, p11 = p01 + 1;
            foreach (int index in new[] { p00, p10, p11, p00, p11, p01 })
                indices[k++] = index;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        // WORLD_ARCHITECTURE.md §11: baked per cell-sized tile and stitched at the borders. One 2 km bake overflows
        // Recast's region IDs; tiles are also what streaming will need.
        NavigationServer3D.MapSetCellSize(GetWorld3D().NavigationMap, 1f);
        NavigationServer3D.MapSetCellHeight(GetWorld3D().NavigationMap, 0.5f);
        float tile = Size / Tiles;
        for (int tx = 0; tx < Tiles; tx++)
        for (int tz = 0; tz < Tiles; tz++)
        {
            var region = new NavigationRegion3D
            {
                Name = $"Navigation_{tx}_{tz}",
                NavigationMesh = new NavigationMesh
                {
                    CellSize = 1f,
                    CellHeight = 0.5f,
                    AgentRadius = 1f,
                    AgentHeight = 2f,
                    AgentMaxClimb = 1f,
                    AgentMaxSlope = 40f,
                    BorderSize = 2f,
                    FilterBakingAabb = new Aabb(new Vector3(tx * tile, -200, tz * tile), new Vector3(tile, 400, tile)),
                    GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
                    GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.GroupsWithChildren,
                    GeometrySourceGroupName = NavigationGroup,
                },
            };
            region.BakeFinished += () => TileBaked(region);
            AddChild(region);
            _unbaked.Enqueue(region);
        }

        var terrain = new MeshInstance3D { Name = "Terrain", Mesh = mesh, MaterialOverride = Palette.Terrain };
        terrain.AddToGroup(NavigationGroup);
        var body = new StaticBody3D();
        var shape = new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = Points, MapDepth = Points, MapData = heights },
            // A height map shape is centred and one unit per cell: scale and shift it onto the mesh.
            Scale = new Vector3(spacing, 1, spacing),
            Position = new Vector3(Size / 2, 0, Size / 2),
        };
        body.AddChild(shape);
        terrain.AddChild(body);
        AddChild(terrain);
    }

    /// <summary>Tiles bake one after another; the capture starts when the last is done.</summary>
    private void TileBaked(NavigationRegion3D region)
    {
        _polygons += region.NavigationMesh.GetPolygonCount();
        if (_unbaked.TryDequeue(out var next))
        {
            next.BakeNavigationMesh(onThread: true);
            return;
        }
        _bake.Stop();
        _bakeResult = $"{Tiles * Tiles} tiles of {Size / Tiles:0} m in {_bake.Elapsed.TotalSeconds:0.0} s, {_polygons} polygons";
    }

    private void BuildProxies()
    {
        var random = new RandomNumberGenerator { Seed = 11 };
        AddChild(Scatter("Trunks", new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.4f, Height = 7 }, Palette.Trunk, Trees, random, 3.5f));
        random.Seed = 11;
        AddChild(Scatter("Canopies", new SphereMesh { Radius = 2.4f, Height = 3.6f }, Palette.Canopy, Trees, random, 7.8f));
        random.Seed = 23;
        AddChild(Scatter("Rocks", new BoxMesh { Size = new Vector3(3, 2, 2.5f) }, Palette.Rock, Rocks, random, 0.6f));
    }

    /// <summary>Proxies cluster round the camera's circuit, so they are in view rather than spread thin over 4 km².</summary>
    private static MultiMeshInstance3D Scatter(string name, Mesh mesh, Material material, int count, RandomNumberGenerator random, float lift)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = count };
        for (int i = 0; i < count; i++)
        {
            float angle = random.Randf() * Mathf.Tau;
            // A 30 m band along the camera's circuit, so the obstruction segment weaves between trunks and rocks.
            float radius = 485f + random.Randf() * 30f;
            float x = Size / 2 + Mathf.Cos(angle) * radius, z = Size / 2 + Mathf.Sin(angle) * radius;
            multi.SetInstanceTransform(i, new Transform3D(Basis.Identity.Rotated(Vector3.Up, random.Randf() * Mathf.Tau), new Vector3(x, Height(x, z) + lift, z)));
        }
        return new MultiMeshInstance3D { Name = name, Multimesh = multi, MaterialOverride = material };
    }

    private void BuildActors()
    {
        _actors = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.8f },
            InstanceCount = ActorCap,
        };
        AddChild(new MultiMeshInstance3D { Name = "Actors", Multimesh = _actors, MaterialOverride = Palette.Proxy });
    }

    private void UpdateActors()
    {
        double t = Time.GetTicksMsec() / 1000.0;
        for (int i = 0; i < ActorCap; i++)
        {
            float angle = i * Mathf.Tau / ActorCap + (float)(t * 0.02 * (1 + i % 3));
            float radius = 500f + (i % 5 - 2) * 8f + Mathf.Sin((float)t + i) * 2f;
            float x = Size / 2 + Mathf.Cos(angle) * radius, z = Size / 2 + Mathf.Sin(angle) * radius;
            _actors.SetInstanceTransform(i, new Transform3D(Basis.Identity.Rotated(Vector3.Up, -angle), new Vector3(x, Height(x, z) + 0.9f, z)));
        }
    }

    /// <summary>D-06 scaffolding: the domain's tier rules over all 400 cells at the 20 Hz tick, stepwise with hysteresis.</summary>
    private void UpdateTiers(double delta)
    {
        _tickAccumulator += delta;
        while (_tickAccumulator >= 0.05)
        {
            _tickAccumulator -= 0.05;
            var eye = _camera.GlobalPosition;
            for (int c = 0; c < 400; c++)
            {
                float minX = c / 20 * 100f, minZ = c % 20 * 100f;
                float dx = Mathf.Max(0, Mathf.Max(minX - eye.X, eye.X - (minX + 100))), dz = Mathf.Max(0, Mathf.Max(minZ - eye.Z, eye.Z - (minZ + 100)));
                _cellTiers[c] = Tiers.Next(_cellTiers[c], (long)(Mathf.Sqrt(dx * dx + dz * dz) * 1000));
                _tierCounts[(int)_cellTiers[c]]++;
            }
            _tierSamples++;
        }
    }

    private void FlyCamera(double t)
    {
        string segment = _segment < 0 ? "warmup" : _segments[_segment].Name;
        float speed = segment == "first_person" ? 5f : 7f;
        float angle = (float)(t * speed / 500.0);
        var centre = new Vector3(Size / 2, 0, Size / 2);
        var ahead = centre + new Vector3(Mathf.Cos(angle + 0.02f) * 500, 0, Mathf.Sin(angle + 0.02f) * 500);
        var here = centre + new Vector3(Mathf.Cos(angle) * 500, 0, Mathf.Sin(angle) * 500);
        ahead.Y = Height(ahead.X, ahead.Z) + 1.6f;
        here.Y = Height(here.X, here.Z) + 1.6f;
        var forward = (ahead - here).Normalized();
        Vector3 eye = segment switch
        {
            "first_person" => here,
            // Weaving through the cluster at shoulder height, close to trunks and actors: the camera-obstruction case.
            "obstruction" => here + new Vector3(Mathf.Sin((float)t * 1.3f) * 6f, 0.4f, Mathf.Cos((float)t * 1.1f) * 6f),
            _ => here - forward * 3.5f + new Vector3(0, 1.2f, 0) + forward.Cross(Vector3.Up) * 0.45f,
        };
        _camera.GlobalPosition = eye;
        _camera.LookAt(here + forward * 10f, Vector3.Up);
    }

    private void Finish()
    {
        double samples = Math.Max(1, _tierSamples);
        var notes = new Dictionary<string, string>
        {
            ["scene"] = "isolated 2x2 km greybox spike (ROADMAP M3 RISK SPIKE): NOT the playable prototype; NOT the formal D-01 revisit gate in Phase 1",
            ["terrain"] = $"{Points}x{Points} height map at {Size / (Points - 1):0.#} m spacing, untextured, {(Points - 1) * (Points - 1) * 2} triangles",
            ["proxies"] = $"{Trees} trees (trunk + canopy) and {Rocks} rocks, instanced; {ActorCap} animated actor stand-ins (the tier-A cap)",
            ["navmesh"] = _bakeResult,
            ["tiers"] = $"400 cells; mean per tick A {_tierCounts[0] / samples:0.0}, B {_tierCounts[1] / samples:0.0}, C {_tierCounts[2] / samples:0.0}, D {_tierCounts[3] / samples:0.0}",
            ["segments"] = string.Join(", ", _segments.Select(s => $"{s.Name} {s.Seconds:0} s")),
            ["vsync"] = "disabled for the capture",
            ["screenshots"] = "one per segment, halfway through; the frame after each is left out of the numbers (the capture stalls the GPU)",
        };
        string summary = _stats.Write(_out, notes);
        GD.Print($"UNNAMED spike capture written to {_out}\n{summary}");
        GetTree().Quit(0);
    }
}
