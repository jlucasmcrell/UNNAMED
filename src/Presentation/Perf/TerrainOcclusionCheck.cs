using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Perf;

public partial class TerrainOcclusionCheck : Node3D
{
    public override void _Ready()
    {
        try
        {
            const int size = 512;
            var terrain = (Node3D)ClassDB.Instantiate("Terrain3D").AsGodotObject();
            AddChild(terrain);
            terrain.Set("region_size", 128);
            terrain.Set("vertex_spacing", 1f);
            terrain.Get("collision").AsGodotObject()!.Set("mode", 0);
            int lods = terrain.Get("mesh_lods").AsInt32();
            using var image = Image.CreateEmpty(size, size, false, Image.Format.Rf);
            var values = new float[size * size];
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                float h = (float)(Math.Sin(x * .05) * 8 + Math.Cos(z * .04) * 6);
                if (x == 260 && z == 264) h -= 40;
                if (x == 270 && z == 271) h += 60;
                values[z * size + x] = h;
                image.SetPixel(x, z, new Color(h, 0, 0));
            }
            terrain.Get("data").AsGodotObject()!.Call("import_images",
                new Godot.Collections.Array { image, default(Variant), default(Variant) }, Vector3.Zero, 0f, 1f);
            var grid = TerrainOcclusionGrid.Build(values, size, size, lods) ?? throw new Exception("No conservative grid");
            float At(Vector3 p)
            {
                float gx = (p.X - grid.Inset) / grid.Step, gz = (p.Z - grid.Inset) / grid.Step;
                int x = Math.Clamp((int)Math.Floor(gx), 0, grid.Columns - 2), z = Math.Clamp((int)Math.Floor(gz), 0, grid.Rows - 2);
                float u = gx - x, v = gz - z;
                float a = grid.Heights[z * grid.Columns + x], b = grid.Heights[z * grid.Columns + x + 1],
                    c = grid.Heights[(z + 1) * grid.Columns + x], d = grid.Heights[(z + 1) * grid.Columns + x + 1];
                return u + v <= 1 ? a + (b - a) * u + (c - a) * v : d + (c - d) * (1 - u) + (b - d) * (1 - v);
            }
            int checkedPoints = 0;
            float worst = float.NegativeInfinity;
            int nearest = (int)ClassDB.ClassGetIntegerConstant("Terrain3DData", "HEIGHT_FILTER_NEAREST");
            for (int lod = 0; lod < lods; lod++)
            {
                using var mesh = terrain.Call("bake_mesh", lod, nearest).AsGodotObject() as ArrayMesh
                    ?? throw new Exception($"No native baked LOD {lod}");
                using var arrays = mesh.SurfaceGetArrays(0);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
                    // Edge and interior probes, including the native LOD's diagonal.
                    foreach (var p in new[] { a, (a + b) / 2, (a + c) / 2, (a + b + c) / 3 })
                    {
                        if (p.X < grid.Inset || p.Z < grid.Inset || p.X > grid.Inset + (grid.Columns - 1) * grid.Step
                            || p.Z > grid.Inset + (grid.Rows - 1) * grid.Step) continue;
                        float overshoot = At(p) - p.Y;
                        worst = Math.Max(worst, overshoot); checkedPoints++;
                        if (overshoot > .001f) throw new Exception($"Occluder over native LOD {lod} at {p} by {overshoot}m");
                    }
                }
            }
            if (checkedPoints == 0) throw new Exception("No native samples checked");
            GD.Print($"TERRAIN OCCLUSION CHECK PASS: native LOD0-{lods - 1}, {checkedPoints} probes, worst overshoot {worst}m");
            terrain.Free();
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
