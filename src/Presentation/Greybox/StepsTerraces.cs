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
        if (fills.Length == 0 || art.WorldMaps("ter_ph_cobblestone_floor_04") is not { } maps) return null;
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var fill in fills)
        {
            void Vertex(int x, int z)
            {
                var point = new Vector3((fill.OriginXMm + x * fill.SpacingMm) / 1000f,
                    fill.HeightAt(x, z) / 1000f + .012f, (fill.OriginZMm + z * fill.SpacingMm) / 1000f);
                surface.SetNormal(Vector3.Up);
                surface.SetUV(new Vector2(point.X, point.Z) / maps.TileSizeM);
                surface.AddVertex(point);
            }
            for (int z = 0; z < fill.Rows - 1; z++)
            for (int x = 0; x < fill.Columns - 1; x++)
            {
                Vertex(x,z); Vertex(x+1,z); Vertex(x+1,z+1);
                Vertex(x,z); Vertex(x+1,z+1); Vertex(x,z+1);
            }
        }
        surface.Index(); surface.GenerateTangents();
        var material = new StandardMaterial3D
        {
            AlbedoTexture = maps.Albedo, NormalEnabled = maps.Normal is not null, NormalTexture = maps.Normal, NormalScale = maps.NormalStrength,
            Roughness = 1, RoughnessTexture = maps.Orm, RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green,
            AOEnabled = maps.Orm is not null, AOTexture = maps.Orm, AOTextureChannel = BaseMaterial3D.TextureChannel.Red,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        art.Coverage.Resolved("terrace_backfill", "region.otherhome", "Supported cobble continuous with the native retaining walls");
        return new MeshInstance3D { Name = "StepsTerraceBackfill", Mesh = surface.Commit(), MaterialOverride = material };
    }
}
