using Godot;
using System.Text.Json;

namespace UNNAMED.Presentation;

public partial class Main
{
    // The gorge behind the wolf den (owner finding 28, spec 28): stills from the den, the lip, the ledge and the far side at each hour in
    // OTHERREACH_GORGE_HOURS (default noon and dusk), from a free camera. OTHERREACH_GORGE_VARIANTS adds diagnostic passes at the first
    // hour with the gorge's mist hidden (nomist) or the air's fog off (nofog) - labelled, and never the shipped look.
    private async Task RunGorgeStills(string directory, Camera3D camera)
    {
        Vector3 Eye(float x, float z) => new(x, _session.Setup.Layout.Space.Terrain.HeightAtMm((long)(x * 1000), (long)(z * 1000)) / 1000f + 1.7f, z);
        var views = new (string Name, Vector3 From, Vector3 To)[]
        {
            ("den_mouth", Eye(512, 772), new Vector3(512, -4, 850)),
            ("lip_east", Eye(535, 799.2f), new Vector3(512, -17, 825)),
            ("lip_out", Eye(540, 799.2f), new Vector3(540, -6, 870)),
            ("lip_down", Eye(540, 799.2f), new Vector3(538, -17, 812)),
            ("lip_west", Eye(486, 799.2f), new Vector3(520, -12, 832)),
            ("wide", new Vector3(548, 40, 770), new Vector3(520, -10, 840)),
            ("fall_from_lip", Eye(530, 799.2f), new Vector3(497, 4, 850)),
            ("near_face_from_far_side", new Vector3(540, 40, 864), new Vector3(540, -5, 801)),
            ("swallow", Eye(612, 799.2f), new Vector3(640, -26, 827)),
            ("ledge", new Vector3(541, Eye(541, 799.6f).Y - 7.5f, 805.5f), new Vector3(505, -12, 845)),
            ("far_wall_close", new Vector3(530, -18, 826), new Vector3(525, 4, 852)),
            ("top_down", new Vector3(545, 150, 815), new Vector3(545, -20, 826)),
        };
        string hours = OS.GetEnvironment("OTHERREACH_GORGE_HOURS") is { Length: > 0 } h ? h : "12,18.5";
        string variants = OS.GetEnvironment("OTHERREACH_GORGE_VARIANTS");
        var rows = new List<object>();
        async Task Shoot(string suffix)
        {
            foreach (var (name, from, to) in views)
            {
                camera.GlobalPosition = from; camera.LookAt(to);
                _shadowLods.Update(camera.GlobalPosition);
                await Rcd3Wait(1.2);
                string label = $"{name}_{suffix}";
                await Rcd3Shot(directory, label);
                var sun = GetTree().Root.FindChildren("*", nameof(DirectionalLight3D), true, false).OfType<DirectionalLight3D>()
                    .Where(l => l.Visible && l.LightEnergy > 0).OrderByDescending(l => l.LightEnergy).FirstOrDefault();
                rows.Add(new { label, eye = from.ToString(), target = to.ToString(), sky_hour = Greybox.SkyView.Hour,
                    sun_toward = sun is null ? null : (-sun.GlobalTransform.Basis.Z).ToString(), sun_energy = sun?.LightEnergy });
            }
        }
        foreach (string hour in hours.Split(','))
        {
            VisualOptions.Pin("hour", hour);
            await Rcd3Wait(3);
            await Shoot($"{hour.Replace('.', '_')}h");
        }
        if (string.IsNullOrEmpty(variants))
        {
            File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new { shots = rows }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        VisualOptions.Pin("hour", hours.Split(',')[0]);
        await Rcd3Wait(3);
        var mist = _hollow.FindChildren("*", nameof(GpuParticles3D), true, false).OfType<GpuParticles3D>()
            .Where(p => p.GlobalPosition.Z > Greybox.DenGorge.Edge && p.GlobalPosition.X is > 430f and < 710f).ToList();
        var air = Greybox.SkyView.Sky3DNode?.Environment;
        foreach (string variant in variants.Split(','))
        {
            foreach (var p in mist) p.Visible = variant != "nomist";
            if (air is not null)
            {
                air.FogEnabled = variant is not ("nofog" or "noenvfog");
                air.VolumetricFogEnabled = variant is not ("nofog" or "novol");
            }
            Greybox.SkyView.Sky3DNode?.Set("fog_enabled", variant is not ("nofog" or "nosky3d"));
            await Rcd3Wait(2);
            await Shoot(variant);
        }
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new { shots = rows, mist_emitters = mist.Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
