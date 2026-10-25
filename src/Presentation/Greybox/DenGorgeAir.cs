// UNNAMED Presentation - the light the gorge behind the den gives itself (spec 28)
// Godot presentation only (D-11): local ambient for the gorge's shaded walls, following the day

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The gorge's walls face away from the sun for most of the day, and a real gorge lights its shaded side with what its sunlit rock and
/// its river throw back. The environment's ambient is the open moor's, so here the gorge gets its own: reflection probes along it, each
/// adding a cool sky-and-stone fill to everything inside its box (and giving the river its own reflections), scaled with the daylight so
/// the gorge is never brighter than the day and is dark at night.
/// </summary>
public partial class DenGorgeAir : Node3D
{
    private const float Fill = 1.1f;
    private readonly List<ReflectionProbe> _probes = new();

    public static DenGorgeAir Build(Func<float, float, float> height)
    {
        var air = new DenGorgeAir { Name = "DenGorgeAir" };
        foreach (float x in new[] { 470f, 560f, 650f })
        {
            float floor = height(x, DenGorge.Edge + 27f);
            var probe = new ReflectionProbe
            {
                Name = $"Bounce_{x:F0}", Position = new Vector3(x, floor + 22f, DenGorge.Edge + 30f), Size = new Vector3(96f, 64f, 64f),
                AmbientMode = ReflectionProbe.AmbientModeEnum.Color, AmbientColor = new Color(0.60f, 0.65f, 0.72f), AmbientColorEnergy = Fill,
                UpdateMode = ReflectionProbe.UpdateModeEnum.Once, BoxProjection = true, Intensity = 0.8f, BlendDistance = 6f, MaxDistance = 220f,
            };
            air.AddChild(probe);
            air._probes.Add(probe);
        }
        return air;
    }

    public override void _Process(double delta)
    {
        float energy = Fill * SkyView.DaylightLevel;
        DenGorge.FallWater.SetShaderParameter("sky_light", 0.3f * SkyView.DaylightLevel);
        foreach (var probe in _probes)
            if (Math.Abs(probe.AmbientColorEnergy - energy) > 0.01f)
                probe.AmbientColorEnergy = energy;
    }
}
