// UNNAMED Presentation - the arenas' paint: the chalked circle and the measured floor, laid on the ground where each
// boss's court already is (D19). Paint, not architecture: nothing here blocks, and the boundary a fight reads is still
// the court the boss record carries.

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// One boss arena, drawn. The court's extent decides the mark's size, and the boss decides which mark: the Custodian's
/// measured floor for the machine that reads measures, the chalked circle for everything that fights in a ring.
/// </summary>
public static class ArenaMarks
{
    private const string Chalk = "arena_chalk_ring";
    private const string Measure = "arena_measure_floor";

    /// <summary>Paint only authored sporting/measured courts, not the recovery bounds of wild encounters.</summary>
    public static void Build(Node3D view, ArtLibrary art, IEnumerable<SpawnSite> spawns, RegionLayout layout)
    {
        foreach (var spawn in spawns)
        {
            if (spawn.Recovery is not { } court || spawn.Space != layout.SpaceId)
                continue;
            bool measured = spawn.Key.Contains("custodian", StringComparison.Ordinal);
            if (!court.Forfeit && !measured)
                continue;
            string asset = measured ? Measure : Chalk;
            string relative = Path.Combine("ready", asset, asset + ".glb");
            // Paint is a complete first-party alternative, not withheld art.
            // Still let the library report a native mesh that exists but fails to load.
            bool delivered = new[] { art.Root, ArtLibrary.FallbackRoot }
                .Any(root => root is not null && File.Exists(Path.Combine(root, relative)));
            var mark = delivered ? art.Model(asset) : null;
            // The ring is authored 19.2 m across and the floor 26 x 20 m; a wider court is drawn wider, never below a
            // third of the authored size, so a small court still reads as a ring rather than a disc.
            float width = (court.MaxXMm - court.MinXMm) / 1000f;
            float depth = (court.MaxZMm - court.MinZMm) / 1000f;
            float across = measured ? 26f : 19.2f;
            float deep = measured ? 20f : 19.2f;
            float x = (court.MinXMm + court.MaxXMm) / 2000f;
            float z = (court.MinZMm + court.MaxZMm) / 2000f;
            float ground = layout.Space.Terrain.HeightAtMm((long)(x * 1000), (long)(z * 1000)) / 1000f;
            if (mark is null)
            {
                // First-party floor paint also works on Terrain3D. A court remains
                // readable when the optional older arena mesh is not in the art pack.
                mark = new MeshInstance3D
                {
                    Mesh = BossEffects.Ground(new Vector3(x, ground, z), 0, Math.Min(width, depth) * .48f, 360, 0, .035f,
                        (px, pz) => layout.Space.Terrain.HeightAtMm((long)(px * 1000), (long)(pz * 1000)) / 1000f),
                    MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = PaintShader } },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                };
                ((ShaderMaterial)((MeshInstance3D)mark).MaterialOverride!).SetShaderParameter("measured", measured);
            }
            else
            {
                mark.Scale = new Vector3(Mathf.Max(width / across, 0.33f), 1f, Mathf.Max(depth / deep, 0.33f));
                mark.Position = new Vector3(x, ground + 0.012f, z);
            }
            mark.Name = "Arena:" + spawn.Key;
            view.AddChild(mark);
        }
    }

    private const string PaintShader = """
        shader_type spatial;
        render_mode cull_disabled, depth_draw_never;
        uniform bool measured = false;
        void fragment() {
            float rough = .65+.35*sin(UV.x*1900.0+sin(UV.y*600.0)*2.0);
            float rim = (1.0-smoothstep(.008,.018,abs(UV.y-.97)))*step(.13,fract(UV.x*32.0));
            float ticks = step(.975,fract(UV.x*48.0))*step(.78,UV.y);
            float inner = 1.0-smoothstep(.003,.009,abs(UV.y-.72));
            ALBEDO = vec3(.66,.60,.47);
            ROUGHNESS = 1.0;
            ALPHA = max(rim,measured ? max(ticks,inner*.5) : 0.0)*rough*.7;
        }
        """;
}
