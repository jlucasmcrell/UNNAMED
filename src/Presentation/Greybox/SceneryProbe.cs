// UNNAMED Presentation - diagnostic only: report scenery whose height does not match the ground the renderer draws.
//
// Set OTHERREACH_SCENERY_PROBE=1 and the boot prints, for each scenery group, how many pieces stand more than a metre off the drawn
// surface and the worst of them with name, position and delta. That turns "boulders float in the sky" into a set of named pieces and the
// exact placement path that lifted them, instead of a guess about which of the four dressing passes is responsible.

using System.Globalization;
using Godot;

namespace UNNAMED.Presentation.Greybox;

public static class SceneryProbe
{
    private const float Tolerance = 1.0f;

    public static void Report(Node3D root, Func<float, float, float> drawn, string label)
    {
        if (OS.GetEnvironment("OTHERREACH_SCENERY_PROBE") != "1")
            return;
        var offenders = new List<(string Name, Vector3 At, float Off)>();
        Walk(root, drawn, label.Length == 0 ? "scenery" : label, offenders);
        offenders.Sort((a, b) => MathF.Abs(b.Off).CompareTo(MathF.Abs(a.Off)));
        GD.Print($"UNNAMED scenery probe [{label}]: {offenders.Count} piece(s) more than {Tolerance:0.0} m off the drawn ground");
        foreach (var (name, at, off) in offenders.Take(10))
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"UNNAMED scenery probe [{label}]: {name} at ({at.X:0.0}, {at.Z:0.0}) y {at.Y:0.00} is {off:+0.00;-0.00} m off"));
    }

    private static void Walk(Node node, Func<float, float, float> drawn, string label, List<(string, Vector3, float)> offenders)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Node3D piece && piece.GetChildCount() > 0)
            {
                var at = piece.GlobalPosition;
                float off = at.Y - drawn(at.X, at.Z);
                if (MathF.Abs(off) > Tolerance)
                    offenders.Add((piece.Name, at, off));
            }
            Walk(child, drawn, label, offenders);
        }
    }

    /// <summary>
    /// Scatter is drawn as multi-mesh instances, so there is no Node3D per plant or rock to walk. This reads the instance transforms themselves and
    /// reports the ones standing off the drawn ground - which is how a rock whose height came from another source is caught, since nothing owns it at
    /// its own position.
    /// </summary>
    public static void ReportInstances(Node root, Func<float, float, float> drawn, string label)
    {
        if (OS.GetEnvironment("OTHERREACH_SCENERY_PROBE") != "1")
            return;
        var off = new List<(string Name, Vector3 At, float Off)>();
        int total = 0;
        foreach (var node in Descendants(root))
        {
            if (node is not MultiMeshInstance3D multi || multi.Multimesh is not { } mesh)
                continue;
            for (int i = 0; i < mesh.InstanceCount; i++)
            {
                var at = multi.GlobalTransform * mesh.GetInstanceTransform(i).Origin;
                float delta = at.Y - drawn(at.X, at.Z);
                total++;
                if (MathF.Abs(delta) > Tolerance)
                    off.Add((multi.Name, at, delta));
            }
        }
        off.Sort((a, b) => MathF.Abs(b.Off).CompareTo(MathF.Abs(a.Off)));
        GD.Print($"UNNAMED scenery probe [{label}]: {off.Count} of {total} instances more than {Tolerance:0.0} m off the drawn ground");
        foreach (var (name, at, delta) in off.Take(10))
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"UNNAMED scenery probe [{label}]: {name} at ({at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}) is {delta:+0.00;-0.00} m off"));
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
        {
            foreach (var deeper in Descendants(child))
                yield return deeper;
        }
    }
}
