using Godot;

namespace UNNAMED.Presentation.Greybox;

public partial class ScatterView
{
    // Explicit experiment only. The default remains the existing whole-region renderer.
    private bool _residentMode = System.Environment.GetEnvironmentVariable("UNNAMED_SCATTER_RESIDENCY") == "1";
    private sealed record ResidentGrid(float[] X, float[] Z, float Chunk, float Reach);
    private sealed record ResidentChunk(Node[] Nodes, int Placements);
    private readonly List<ResidentGrid> _residentGrids = new();
    private readonly Dictionary<(int Kind, int X, int Z), ResidentChunk> _residents = new();
    private readonly Queue<(int Kind, int X, int Z)> _pending = new();
    private Vector2 _residencySpawn;
    private Vector2? _plannedFocus;
    public bool ResidencyEnabled => _residentMode;
    public bool ResidencyReady => _pending.Count == 0;
    public double WorstChunkMs { get; private set; }

    private void BuildResidents(ulong seed)
    {
        _residents.Clear();
        _pending.Clear();
        _residentGrids.Clear();
        _plannedFocus = null;
        WorstChunkMs = 0;
        var region = _ground.Region;
        foreach (var kind in _kinds)
        {
            float chunk = kind.Rules.ChunkM ?? _rules.ChunkM;
            // Preserve the baseline's repeated float addition, including non-integral chunk sizes.
            var xs = new List<float>();
            var zs = new List<float>();
            for (float x = region.Position.X; x < region.End.X; x += chunk) xs.Add(x);
            for (float z = region.Position.Y; z < region.End.Y; z += chunk) zs.Add(z);
            float radius = kind.Meshes.Select(m => m.GetAabb().Size.Length()).DefaultIfEmpty(0).Max();
            float tallest = 1 + kind.Rules.Tall.Values.DefaultIfEmpty(0).Max();
            _residentGrids.Add(new ResidentGrid(xs.ToArray(), zs.ToArray(), chunk,
                kind.Rules.FadeM + radius * kind.Rules.SizeMax * tallest));
            _counts[kind.Rules.Name] = 0;
        }
        PlanResidents(_residencySpawn);
        while (_pending.TryDequeue(out var key)) AddResident(seed, key);
    }

    public override void _Process(double delta)
    {
        if (!_residentMode || _builtFor is not { } seed || GetViewport().GetCamera3D() is not { } camera)
            return;
        var focus = new Vector2(camera.GlobalPosition.X, camera.GlobalPosition.Z);
        if (_plannedFocus is null || focus.DistanceSquaredTo(_plannedFocus.Value) >= 16)
            PlanResidents(focus);
        // Budget is checked between chunks: one dense chunk can exceed 2 ms. Report that cost honestly.
        ulong started = Time.GetTicksUsec();
        while (Time.GetTicksUsec() - started < 2000 && _pending.TryDequeue(out var key))
            AddResident(seed, key);
    }

    // Audit cameras teleport. Finish their neighborhood before the harness starts measuring/capturing.
    internal void PrepareResidencyForCapture(Vector3 eye)
    {
        if (!_residentMode || _builtFor is not { } seed) return;
        PlanResidents(new Vector2(eye.X, eye.Z));
        while (_pending.TryDequeue(out var key)) AddResident(seed, key);
    }

    // Frozen same-session audit only: rebuild the identical seed under each renderer.
    internal void SetResidencyModeForCapture(bool enabled)
    {
        if (_residentMode == enabled || _builtFor is not { } seed) return;
        _residentMode = enabled;
        _builtFor = null;
        Build(seed);
    }

    private void PlanResidents(Vector2 focus)
    {
        _plannedFocus = focus;
        _pending.Clear();
        foreach (var key in _residents.Keys.ToArray())
        {
            var grid = _residentGrids[key.Kind];
            if (DistanceSquared(grid, key.X, key.Z, focus) <= MathF.Pow(grid.Reach + 48, 2)) continue;
            var chunk = _residents[key];
            foreach (var node in chunk.Nodes) { RemoveChild(node); node.QueueFree(); }
            _counts[_kinds[key.Kind].Rules.Name] -= chunk.Placements;
            _residents.Remove(key);
        }
        var missing = new List<((int Kind, int X, int Z) Key, float Distance)>();
        for (int k = 0; k < _residentGrids.Count; k++)
        {
            var grid = _residentGrids[k];
            float radius = grid.Reach + 16;
            int x0 = Math.Clamp((int)MathF.Floor((focus.X - radius - grid.X[0]) / grid.Chunk), 0, grid.X.Length - 1);
            int x1 = Math.Clamp((int)MathF.Floor((focus.X + radius - grid.X[0]) / grid.Chunk), 0, grid.X.Length - 1);
            int z0 = Math.Clamp((int)MathF.Floor((focus.Y - radius - grid.Z[0]) / grid.Chunk), 0, grid.Z.Length - 1);
            int z1 = Math.Clamp((int)MathF.Floor((focus.Y + radius - grid.Z[0]) / grid.Chunk), 0, grid.Z.Length - 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                var key = (k, x, z);
                float distance = DistanceSquared(grid, x, z, focus);
                if (!_residents.ContainsKey(key) && distance <= radius * radius) missing.Add((key, distance));
            }
        }
        foreach (var item in missing.OrderBy(m => m.Distance).ThenBy(m => m.Key.Kind).ThenBy(m => m.Key.X).ThenBy(m => m.Key.Z))
            _pending.Enqueue(item.Key);
    }

    private static float DistanceSquared(ResidentGrid grid, int x, int z, Vector2 point)
    {
        float dx = point.X - Math.Clamp(point.X, grid.X[x], grid.X[x] + grid.Chunk);
        float dz = point.Y - Math.Clamp(point.Y, grid.Z[z], grid.Z[z] + grid.Chunk);
        return dx * dx + dz * dz;
    }

    private void AddResident(ulong seed, (int Kind, int X, int Z) key)
    {
        if (_residents.ContainsKey(key)) return;
        var grid = _residentGrids[key.Kind];
        int before = GetChildCount();
        ulong started = Time.GetTicksUsec();
        int count = BuildChunk(seed, _kinds[key.Kind], key.X, key.Z, grid.X[key.X], grid.Z[key.Z]);
        WorstChunkMs = Math.Max(WorstChunkMs, (Time.GetTicksUsec() - started) / 1000.0);
        var nodes = Enumerable.Range(before, GetChildCount() - before).Select(i => GetChild(i)).ToArray();
        _residents.Add(key, new ResidentChunk(nodes, count));
        _counts[_kinds[key.Kind].Rules.Name] += count;
    }
}
