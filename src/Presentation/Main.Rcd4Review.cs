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
        var trees = _hollow.FindChildren("*", "Node3D", true, false).OfType<Node3D>()
            .Where(n => n.Name.ToString().StartsWith("cw_birch_", StringComparison.Ordinal)
                || n.Name.ToString().StartsWith("tree_", StringComparison.Ordinal)).ToArray();
        File.WriteAllText(Path.Combine(directory,"tree_nodes.json"), JsonSerializer.Serialize(trees.Select(t => new {
            name=t.Name.ToString(), position=t.GlobalPosition.ToString(), bounds=Art.ArtGallery.Bounds(t).ToString() })));
        foreach (var tree in trees.OrderBy(t => t.GlobalPosition.DistanceSquaredTo(_avatar.GlobalPosition)).Take(2))
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
            ("old_late_before", new Vector3(680,23,805),new Vector3(650,18,765)),
            ("old_late_roots_before",new Vector3(666,5,779),new Vector3(650,3,765)),
            ("den_gorge_before",new Vector3(512,11,794),new Vector3(512,-5,830)),
            ("tavar_before",new Vector3(551,5,651),new Vector3(545,3.5f,642)) })
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
