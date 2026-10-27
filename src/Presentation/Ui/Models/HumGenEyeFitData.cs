using System.Numerics;
using System.Text.Json;

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>The paid native eye fitter's landmarks over retained body/face keys.</summary>
public sealed class HumGenEyeFitData
{
    private sealed record Delta(float Default, Vector3 Width, Vector3 Left, Vector3 Right);
    private readonly Dictionary<string, Delta> _keys = new(StringComparer.Ordinal);
    private readonly Vector3 _width;
    private readonly float _size;
    public HumGenEyeFitData(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        static Vector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        _width = Vector(root.GetProperty("width_default"));
        _size = root.GetProperty("size_default").GetSingle();
        foreach (var property in root.GetProperty("keys").EnumerateObject())
        {
            var value = property.Value;
            var tails = value.GetProperty("tails");
            _keys[property.Name] = new(value.GetProperty("default").GetSingle(), Vector(value.GetProperty("width_delta")),
                Vector(tails.GetProperty("LeftEye")), Vector(tails.GetProperty("RightEye")));
        }
    }

    public (float Radius, Vector3 Left, Vector3 Right) Fit(IReadOnlyDictionary<string, float> weights)
    {
        var width = _width;
        var left = Vector3.Zero;
        var right = Vector3.Zero;
        foreach (var (key, delta) in _keys)
        {
            float value = weights.GetValueOrDefault(key) - delta.Default;
            width += delta.Width * value;
            left += delta.Left * value;
            right += delta.Right * value;
        }
        return (94f * width.Length() - 2f - _size, left, right);
    }
}
