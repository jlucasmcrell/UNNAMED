using Godot;
using System.Text.Json;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in scenery A/B. NPC drawing is suppressed only in this labelled diagnostic;
    // the normal world, terrain, materials, owner settings and gameplay pacing are retained.
    private async Task RunRcd4WorldReview(string directory, Camera3D camera)
    {
        SetProcess(false);
        var rows = new List<object>();
        Vector3 At(float x,float rise,float z) => new(x,
            _session.Setup.Layout.Space.Terrain.HeightAtMm((long)(x*1000),(long)(z*1000))/1000f+rise,z);
        var trees = _hollow.FindChildren("*", "Node3D", true, false).OfType<Node3D>()
            .Where(n => n.Name.ToString().StartsWith("cw_birch_", StringComparison.Ordinal)
                || n.Name.ToString().StartsWith("tree_", StringComparison.Ordinal)).ToArray();
        File.WriteAllText(Path.Combine(directory,"tree_nodes.json"), JsonSerializer.Serialize(trees.Select(t => new {
            name=t.Name.ToString(), position=t.GlobalPosition.ToString(), bounds=Art.ArtGallery.Bounds(t).ToString() })));
        if (System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_TREE_SHAPES") == "1")
        {
            var bands = _scatter.FindChildren("saplings_*", "MultiMeshInstance3D", true, false).OfType<MultiMeshInstance3D>().ToArray();
            // Fix the world instance, independent of a changed LOD chain's shared bounds.
            var reference = new Vector3(422.56113f,8.494504f,756.07996f);
            var plant = bands.Where(m => m.Name.ToString().EndsWith("_lod0",StringComparison.Ordinal))
                .SelectMany(m => Enumerable.Range(0,m.Multimesh.InstanceCount)
                    .Select(i => m.GlobalTransform*m.Multimesh.GetInstanceTransform(i).Origin))
                .OrderBy(p => p.DistanceSquaredTo(reference)).First();
            if (plant.DistanceTo(reference) > .01f) throw new InvalidOperationException("Sapling fixture world instance changed");
            foreach (float bias in new[] { 1f, 1024f })
            {
                foreach (var band in bands) band.LodBias=bias;
                foreach (float distance in new[] { 5f,12f,22f,42f })
                {
                    var aim=plant+Vector3.Up;
                    camera.GlobalPosition=aim+new Vector3(distance*.3f,.6f,distance*.954f);camera.LookAt(aim);
                    _shadowLods.Update(camera.GlobalPosition);
                    await Rcd3Wait(.4);
                    string name=$"sapling_bias{bias:0}_{distance:0}m";
                    await Rcd3Shot(directory,name);
                    rows.Add(new { name,plant=plant.ToString(),eye=camera.GlobalPosition.ToString(),bias,
                        note="Actual scatter plant; only automatic mesh LOD bias changes; authored distance bands retained" });
                }
            }
        }
        foreach (var tree in trees.OrderBy(t => t.GlobalPosition.DistanceSquaredTo(_avatar.GlobalPosition))
            .Take(System.Environment.GetEnvironmentVariable("OTHERREACH_RCD4_TREE_SHAPES") == "1" ? 0 : 2))
        {
            var bounds = Art.ArtGallery.Bounds(tree);
            var aim = tree.ToGlobal(bounds.GetCenter());
            var meshes = tree.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
            var ends = meshes.Select(m => m.VisibilityRangeEnd).Where(x => x > 0).Distinct().Order().ToArray();
            foreach (bool fade in new[] { false, true })
            {
                _shadowLods.Unwatch();
                foreach (var mesh in meshes)
                {
                    mesh.VisibilityRangeFadeMode = fade ? GeometryInstance3D.VisibilityRangeFadeModeEnum.Self
                        : GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
                    mesh.VisibilityRangeBeginMargin = fade ? mesh.VisibilityRangeBegin * .1f : 0;
                    mesh.VisibilityRangeEndMargin = fade ? mesh.VisibilityRangeEnd * .1f : 0;
                }
                _shadowLods.Watch(this);
                foreach (float end in ends)
                    foreach (float offset in new[] { -2f, 0f, 2f })
                    {
                        float distance = end + offset;
                        camera.GlobalPosition = aim + new Vector3(distance * .3f, 2, distance * .954f);
                        camera.LookAt(aim);
                        _shadowLods.Update(camera.GlobalPosition);
                        await Rcd3Wait(.35);
                        string name = $"{tree.Name}_{(fade ? "fade" : "hard")}_{distance:0.0}";
                        await Rcd3Shot(directory,name);
                        rows.Add(new { name, distance, eye=camera.GlobalPosition.ToString(), target=aim.ToString(),
                            bands=meshes.Select(m=>new { name=m.Name.ToString(), begin=m.VisibilityRangeBegin, end=m.VisibilityRangeEnd,
                                fade=m.VisibilityRangeFadeMode.ToString(), shadow=m.CastShadow.ToString(), gi=m.GIMode.ToString() }).ToArray() });
                    }
            }
        }
        foreach (var shot in new[] {
            ("old_late_before", At(680,23,790),At(650,25,765)),
            ("old_late_roots_before",At(666,2,779),At(650,2,765)),
            ("den_gorge_before",At(535,2,799.5f),At(512,-20,824)),
            ("tavar_before",At(551,2,651),At(545,1,642)) })
        {
            camera.GlobalPosition=shot.Item2;camera.LookAt(shot.Item3);
            _shadowLods.Update(camera.GlobalPosition);
            await Rcd3Wait(.5);await Rcd3Shot(directory,shot.Item1);
            rows.Add(new { name=shot.Item1, eye=shot.Item2.ToString(), target=shot.Item3.ToString(), npc_suppressed_fixture=true });
        }
        File.WriteAllText(Path.Combine(directory,"world_review.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(directory,"complete.json"),"{\"fixture\":\"Scenery A/B; NPC presentation suppressed; no world state writes\"}");
    }
}
