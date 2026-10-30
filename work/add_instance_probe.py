"""Add a multi-mesh instance probe: scatter has no Node3D per rock, so the node walk cannot see it."""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Greybox\SceneryProbe.cs')
text = PATH.read_text(encoding='utf-8')

ADDITION = '''
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
'''

if 'ReportInstances' in text:
    print('instance probe already present')
else:
    text = text.rstrip()
    assert text.endswith('}')
    PATH.write_text(text[:-1].rstrip() + '\n' + ADDITION, encoding='utf-8')
    print('instance probe added')
