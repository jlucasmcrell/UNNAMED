using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>The supported cobbled backfill between the native retaining walls and the coarse terrain edge.</summary>
internal static class StepsTerraces
{
    public static MeshInstance3D? Build(RegionLayout layout, ArtLibrary art)
    {
        if (layout.Id != "region.otherhome") return null;
        // The authored support fields start 10 cm inside the retaining wall's back.
        // Match those exact origins; the old pier supports and the stairs stay separate.
        var origins = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith("oh_riser_", StringComparison.Ordinal))
            .Select(b => (b.MinXMm, Z: b.MaxZMm - 100)).ToHashSet();
        var fills = layout.Space.WalkableSurfaces.Where(s => origins.Contains((s.OriginXMm, s.OriginZMm))).ToArray();
        if (fills.Length == 0 || art.Root is null) return null;
        // This is a packed Terrain3D layer (RGB/height, RGB/roughness), not
        // a world-material record with separate basecolor/normal/ORM maps.
        const string id = "ter_ph_cobblestone_floor_04";
        string folder = Path.Combine(art.Root, "materials", id);
        var albedo = art.Texture(Path.Combine(folder, id + "_albedo_height.png"), "color");
        var normal = art.Texture(Path.Combine(folder, id + "_normal_rough.png"), "normal");
        if (albedo is null || normal is null) return null;
        const float tileSizeM = 5f;
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var fill in fills)
        {
            void Vertex(int x, int z)
            {
                var point = new Vector3((fill.OriginXMm + x * fill.SpacingMm) / 1000f,
                    fill.HeightAt(x, z) / 1000f + .012f, (fill.OriginZMm + z * fill.SpacingMm) / 1000f);
                surface.SetNormal(Vector3.Up);
                surface.SetUV(new Vector2(point.X, point.Z) / tileSizeM);
                surface.AddVertex(point);
            }
            for (int z = 0; z < fill.Rows - 1; z++)
            for (int x = 0; x < fill.Columns - 1; x++)
            {
                Vertex(x,z); Vertex(x+1,z); Vertex(x+1,z+1);
                Vertex(x,z); Vertex(x+1,z+1); Vertex(x,z+1);
            }
        }
        // The coarse terrain slopes from each 6 m cut into the narrower native
        // boards. Face those exposed banks in the same native cobble, rather
        // than leaving raw stretched terrain between the boards and cheek stone.
        foreach (var stair in layout.Space.WalkableSurfaces.Where(s => s.Columns == 7 && s.Rows == 17
            && s.HeightsMm[0] != s.HeightsMm[^1]))
        {
            float centre = (stair.OriginXMm + stair.MaxXMm) / 2000f;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 Point(int x, int z)
                {
                    float px = centre + side * (1.4f + x * .25f), pz = stair.OriginZMm / 1000f + z * .5f;
                    float py = layout.Space.Terrain.HeightAtMm((long)MathF.Round(px * 1000), (long)MathF.Round(pz * 1000)) / 1000f;
                    return new Vector3(px, py + .025f, pz);
                }
                void Triangle(Vector3 a, Vector3 b, Vector3 c)
                {
                    var normal = (c-a).Cross(b-a).Normalized();
                    if (normal.Y < 0) { (b,c) = (c,b); normal = -normal; }
                    foreach (var point in new[] { a,b,c })
                    {
                        surface.SetNormal(normal); surface.SetUV(new Vector2(point.X,point.Z) / tileSizeM); surface.AddVertex(point);
                    }
                }
                for (int z = 0; z < 32; z++)
                for (int x = 0; x < 10; x++)
                {
                    var a=Point(x,z); var b=Point(x+1,z); var c=Point(x,z+1); var d=Point(x+1,z+1);
                    Triangle(a,b,d); Triangle(a,d,c);
                }
            }
        }
        surface.Index(); surface.GenerateTangents();
        var material = new StandardMaterial3D
        {
            AlbedoTexture = albedo, NormalEnabled = true, NormalTexture = normal, NormalScale = 1,
            Roughness = 1, RoughnessTexture = normal, RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Alpha,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        art.Coverage.Resolved("terrace_backfill", "region.otherhome", "Supported cobble continuous with the native retaining walls");
        return new MeshInstance3D { Name = "StepsTerraceBackfill", Mesh = surface.Commit(), MaterialOverride = material };
    }
}
