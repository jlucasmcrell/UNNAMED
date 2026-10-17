// UNNAMED Presentation - what a landed blow throws off the body it lands on: blood, sparks, bone dust, embers (asset overhaul 2026-10-14)
// Godot presentation only (D-11): fired from the simulation's own HitResolved, never from an animation guessing at contact

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A blow the simulation has resolved (not dodged) draws the recipes the effects map gives its material - <c>hit.flesh</c>,
/// <c>hit.metal</c>, <c>hit.bone</c>, <c>hit.ember</c>, or <c>hit.blocked</c> when a guard took it - at the struck body, thrown along
/// the blow (each recipe's local +Z). The material is the recipes' <c>hit_materials</c> by creature definition; anything not listed,
/// and the character, is flesh. Nothing draws without the recipes (vfx=recipes) or their textures.
/// </summary>
public partial class HitImpacts : Node3D
{
    private ParticleRecipes? _recipes;
    private readonly Dictionary<string, string> _materials = new(StringComparer.Ordinal);

    public void Bind(string? assetRoot)
    {
        _recipes = VisualOptions.Recipes ? ParticleRecipes.Load(assetRoot) : null;
        if (_recipes is null || !Godot.FileAccess.FileExists(ParticleRecipes.ResourcePath))
            return;
        using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ParticleRecipes.ResourcePath));
        if (json.RootElement.TryGetProperty("hit_materials", out var materials))
        {
            foreach (var m in materials.EnumerateObject().Where(m => m.Value.ValueKind == JsonValueKind.String))
                _materials[m.Name] = m.Value.GetString()!;
        }
    }

    /// <summary>Prepare these same recipes/textures at portal entry, owned by the interior's lifetime.</summary>
    public void Prepare(Node3D parent, Vector3 at)
    {
        if (_recipes is null) return;
        foreach (string effect in new[] { "hit.flesh", "hit.metal", "hit.blocked" })
            foreach (string name in _recipes.For(effect))
                if (_recipes.Build(name) is { } burst)
                {
                    parent.AddChild(burst);
                    burst.GlobalPosition = at;
                    ParticleRecipes.Fire(burst);
                }
    }

    /// <summary>A blow on <paramref name="defId"/> (null: the character) at <paramref name="at"/>, from <paramref name="from"/>.</summary>
    public void Hit(string? defId, Vector3 at, Vector3 from, bool blocked)
    {
        if (_recipes is null)
            return;
        string effect = blocked ? "hit.blocked" : $"hit.{(defId is not null && _materials.TryGetValue(defId, out var m) ? m : "flesh")}";
        var names = _recipes.For(effect);
        if (names.Count == 0)
            return;
        var along = at - from;
        along.Y = 0;
        var node = new Node3D { Name = "Hit" };
        AddChild(node);
        node.GlobalPosition = at;
        if (along.LengthSquared() > 0.0001f)
            node.GlobalBasis = Basis.LookingAt(-along.Normalized(), Vector3.Up);
        foreach (string name in names)
        {
            if (_recipes.Build(name) is not { } burst)
                continue;
            node.AddChild(burst);
            ParticleRecipes.Fire(burst);
        }
        GetTree().CreateTimer(_recipes.Lifetime(names) + 0.2).Timeout += node.QueueFree;
    }
}
