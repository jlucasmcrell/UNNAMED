using Godot;

namespace UNNAMED.Presentation.Greybox;

public partial class ScatterView
{
    // Pilot: combine only chunks in their original camera-distance band. No hidden LOD
    // vertices are submitted. Fixed 24 m tiles retain useful native frustum/occlusion culling.
    private readonly bool _drawBatchMode = System.Environment.GetEnvironmentVariable("UNNAMED_SCATTER_DRAW_BATCHES") == "1";
    private sealed class DrawChunk(Vector3 centre, float begin, float end, float[] buffer)
    {
        public readonly Vector3 Centre = centre;
        public readonly float BeginSquared = begin * begin, EndSquared = end * end;
        public readonly float[] Buffer = buffer;
        public bool Active;
    }
    private sealed class DrawBatch(MultiMeshInstance3D node)
    {
        public readonly MultiMeshInstance3D Node = node;
        public readonly List<DrawChunk> Chunks = new();
        public float[] Buffer = Array.Empty<float>();
    }
    private readonly Dictionary<(ulong Mesh, int X, int Z), DrawBatch> _drawBatches = new();

    private void AddDrawBatchBands(List<MultiMeshInstance3D> bands, List<(Transform3D T, Color C, int Form)> placed)
    {
        if (!_drawBatchMode || _residentMode || bands.Count == 0) return;
        var buffer = new float[placed.Count * 16];
        for (int i = 0; i < placed.Count; i++)
        {
            var (t, c, _) = placed[i]; int p = i * 16;
            buffer[p] = t.Basis.X.X; buffer[p + 1] = t.Basis.Y.X; buffer[p + 2] = t.Basis.Z.X; buffer[p + 3] = t.Origin.X;
            buffer[p + 4] = t.Basis.X.Y; buffer[p + 5] = t.Basis.Y.Y; buffer[p + 6] = t.Basis.Z.Y; buffer[p + 7] = t.Origin.Y;
            buffer[p + 8] = t.Basis.X.Z; buffer[p + 9] = t.Basis.Y.Z; buffer[p + 10] = t.Basis.Z.Z; buffer[p + 11] = t.Origin.Z;
            buffer[p + 12] = c.R; buffer[p + 13] = c.G; buffer[p + 14] = c.B; buffer[p + 15] = c.A;
        }
        foreach (var band in bands)
        {
            var centre = band.CustomAabb.GetCenter();
            var key = (band.Multimesh.Mesh.GetInstanceId(), (int)MathF.Floor(centre.X / 24), (int)MathF.Floor(centre.Z / 24));
            if (!_drawBatches.TryGetValue(key, out var batch))
            {
                var node = new MultiMeshInstance3D { Name = "DrawBatch_" + band.Name, Visible = false,
                    CastShadow = band.CastShadow, GIMode = band.GIMode, CustomAabb = band.CustomAabb,
                    Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                        UseCustomData = true, Mesh = band.Multimesh.Mesh } };
                AddChild(node);
                _drawBatches.Add(key, batch = new DrawBatch(node));
            }
            else batch.Node.CustomAabb = batch.Node.CustomAabb.Merge(band.CustomAabb);
            batch.Chunks.Add(new DrawChunk(centre, band.VisibilityRangeBegin, band.VisibilityRangeEnd, buffer));
            band.Visible = false;
        }
    }

    private void UpdateDrawBatches(Vector3 eye)
    {
        if (_drawBatches.Count == 0) return;
        var transform = GlobalTransform;
        foreach (var batch in _drawBatches.Values)
        {
            bool dirty = batch.Buffer.Length == 0;
            foreach (var chunk in batch.Chunks)
            {
                float distance = eye.DistanceSquaredTo(transform * chunk.Centre);
                bool active = distance >= chunk.BeginSquared && (chunk.EndSquared == 0 || distance < chunk.EndSquared);
                if (active != chunk.Active) { chunk.Active = active; dirty = true; }
            }
            if (!dirty) continue;
            if (batch.Buffer.Length == 0)
            {
                batch.Buffer = new float[batch.Chunks.Sum(c => c.Buffer.Length)];
                batch.Node.Multimesh.InstanceCount = batch.Buffer.Length / 16;
            }
            int length = 0;
            foreach (var chunk in batch.Chunks)
            {
                if (!chunk.Active) continue;
                chunk.Buffer.CopyTo(batch.Buffer, length);
                length += chunk.Buffer.Length;
            }
            if (length > 0) batch.Node.Multimesh.Buffer = batch.Buffer;
            batch.Node.Multimesh.VisibleInstanceCount = length / 16;
            batch.Node.Visible = length > 0;
        }
    }
}
