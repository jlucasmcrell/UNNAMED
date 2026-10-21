namespace UNNAMED.Presentation.Art;

// Shader LOD is measured from each plant; Godot's coarse visibility is measured from the batch centre.
// Triangle inequality requires these guards so no contributing plant is culled with its whole batch.
internal static class PlantLodBands
{
    internal static (float Begin, float End) Guard(float begin, float end, float radius, bool last) =>
        (Math.Max(0, begin - radius - Math.Min(1, begin * .1f)),
            end + radius + (last ? 0 : Math.Min(1, end * .1f)));
}
