// UNNAMED Presentation - the compass's two marks: the tracked beat and the nearest other objective, and the words that name them
// Pure: no Godot, so the rules are tested directly

using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>A mark as the compass draws it: where it lies from the character, and what to call it.</summary>
public sealed record CompassMark(string Label, double Metres, float BearingDegrees);

/// <summary>
/// What the compass shows of the quests (the owner's doctrine: a compass, never a minimap): a diamond on the tracked beat, and a ring on the
/// nearest objective of any other active quest the character knows the place of. Each carries its name and distance in words, so neither
/// rests on shape or colour alone. A mark within <see cref="ArrivedM"/> is hidden - the character is there.
/// </summary>
public static class CompassMarkerModel
{
    public const double ArrivedM = 3.0;

    public static CompassMark? Mark(QuestMarker? marker, long fromXMm, long fromZMm, Func<QuestMarker, string> name)
    {
        if (marker is null)
            return null;
        double dx = marker.XMm - fromXMm, dz = marker.ZMm - fromZMm, metres = Math.Sqrt(dx * dx + dz * dz) / 1000.0;
        return metres < ArrivedM ? null : new CompassMark(name(marker), metres, (float)(Math.Atan2(dx, dz) * 180 / Math.PI));
    }

    /// <summary>The nearest of <paramref name="others"/> (the active quests other than the tracked one) to the character, or null.</summary>
    public static CompassMark? Nearest(IEnumerable<QuestMarker?> others, QuestMarker? tracked, long fromXMm, long fromZMm, Func<QuestMarker, string> name) =>
        others.OfType<QuestMarker>().Where(m => tracked is null || m.QuestId != tracked.QuestId)
            .Select(m => Mark(m, fromXMm, fromZMm, name)).OfType<CompassMark>().OrderBy(m => m.Metres).ThenBy(m => m.Label, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>"120 m", or "1.2 km" from a kilometre.</summary>
    public static string Distance(double metres) => metres >= 1000 ? $"{metres / 1000:0.0} km" : $"{Math.Round(metres / 5) * 5:0} m";

    /// <summary>The two lines under the compass: "Quest: Renn, 120 m" and "Nearest: Sel, 40 m"; empty when there is neither.</summary>
    public static string Words(CompassMark? tracked, CompassMark? nearest)
    {
        var lines = new List<string>();
        if (tracked is not null)
            lines.Add($"Quest: {tracked.Label}, {Distance(tracked.Metres)}");
        if (nearest is not null)
            lines.Add($"Nearest: {nearest.Label}, {Distance(nearest.Metres)}");
        return string.Join("\n", lines);
    }
}
