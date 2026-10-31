using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>The authored pennants and restrung staves answer the same wind as the settlement's trees.</summary>
public partial class FestivalWind : Node
{
    private readonly List<(Node3D Node, Transform3D Base, bool Cloth)> _pieces = new();

    public override void _Ready()
    {
        WindField.Ensure(GetParent());
        foreach (var node in GetParent().FindChildren("*", nameof(Node3D), true, false).OfType<Node3D>()
                     .OrderBy(n => n.Name.ToString(), StringComparer.Ordinal))
        {
            string name = node.Name;
            if (name.StartsWith("HollowDayPennant_", StringComparison.Ordinal))
                _pieces.Add((node, node.Transform, true));
            else if (name.StartsWith("HollowDayStave_", StringComparison.Ordinal))
                _pieces.Add((node, node.Transform, false));
        }
        if (_pieces.Count == 0) SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (GetParent() is not Node3D { } holder || !holder.IsVisibleInTree()) return;
        double seconds = Time.GetTicksMsec() / 1000.0;
        for (int i = 0; i < _pieces.Count; i++)
        {
            var (node, rest, cloth) = _pieces[i];
            var localWind = node.GetParent<Node3D>().GlobalBasis.Inverse() * WindField.Direction;
            float pulse = (float)(Math.Sin(seconds * (cloth ? 1.7 : 1.1) + i * .47)
                + .22 * WindField.Gust * Math.Sin(seconds * 3.1 + i));
            float angle = (cloth ? .02f + .10f * WindField.Strength : .005f + .035f * WindField.Strength) * pulse;
            var axis = cloth ? Vector3.Right : new Vector3(-localWind.Z, 0, localWind.X).Normalized();
            if (axis.LengthSquared() < .001f) axis = Vector3.Right;
            node.Transform = new Transform3D(rest.Basis.Rotated(axis, angle), rest.Origin);
        }
    }
}
