// UNNAMED Presentation - the creator's bodies drawn: each body preset's model per player family (WP2, WP17; owner D3, OD15:20-21)
// Godot presentation only, and free of Godot itself so the tests can read it: the domain keeps only the preset ID (D-11)

using System.Text.Json;
using UNNAMED.Domain.Character;

namespace UNNAMED.Presentation.Art;

/// <summary>A body type's models by the player's model family (content's <c>body.*</c> type: veth_m, veth_f), the first one installed drawn.</summary>
public sealed record BodyModels(string Type, IReadOnlyDictionary<string, IReadOnlyList<string>> Models);

/// <summary>
/// The bindings' <c>player_bodies</c>: what each body preset (content kind <c>body.*</c>, WP17) is drawn with. A type names its models per
/// family - <c>production</c> the cast the player plays (MPFB), <c>metahuman</c> the <c>--visual player=metahuman_*</c> bodies - and a build
/// is its own model, baked with its garments (tools/character_build): <c>&lt;model&gt;_&lt;build&gt;</c>, the base build drawn by the model
/// itself. A body whose model is not installed is not drawn: there is no stand-in.
/// </summary>
public sealed class PlayerBodies
{
    public static PlayerBodies None { get; } = new(Array.Empty<BodyModels>(), null, "average");

    public PlayerBodies(IReadOnlyList<BodyModels> types, string? fallback, string baseBuild)
    {
        Types = types;
        Default = fallback ?? CharacterSpec.DefaultBodyPreset;
        BaseBuild = baseBuild;
    }

    /// <summary>The types, in the order the bindings list them (the order the creator shows them).</summary>
    public IReadOnlyList<BodyModels> Types { get; }

    /// <summary>The preset a character with no choice is drawn with, and the creator starts from.</summary>
    public string Default { get; }

    /// <summary>The build each type's own model is (every other build's model carries its build ID).</summary>
    public string BaseBuild { get; }

    /// <summary>The model a body is drawn with in a family: the first of its type's models whose build model is installed, or null.</summary>
    public string? Model(BodyPreset body, string family, Func<string, bool> installed) =>
        Types.FirstOrDefault(t => t.Type == body.Type) is { } type && type.Models.TryGetValue(family, out var models)
            ? models.Select(m => body.Build == BaseBuild ? m : $"{m}_{body.Build}").FirstOrDefault(installed)
            : null;

    /// <summary>
    /// A garment's variant baked for <paramref name="body"/> (tools/character_build: authored once, baked per body as
    /// <c>&lt;garment&gt;__&lt;body&gt;</c>): <c>shirt__mh_veth_m</c> on mh_veth_m_heavy is <c>shirt__mh_veth_m_heavy</c>.
    /// </summary>
    public static string Garment(string garment, string body) =>
        garment.IndexOf("__", StringComparison.Ordinal) is var at && at > 0 ? $"{garment[..at]}__{body}" : garment;

    /// <summary>The model family a run's <c>--visual player=</c> value draws the player from.</summary>
    public static string Family(string? playerOption) =>
        playerOption is not null && playerOption.StartsWith("metahuman", StringComparison.Ordinal) ? "metahuman" : "production";

    /// <summary>The bindings' <c>player_bodies</c> object; <see cref="None"/> when it is missing or malformed.</summary>
    public static PlayerBodies Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return None;
        var types = new List<BodyModels>();
        if (element.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array)
        {
            foreach (var type in t.EnumerateArray())
            {
                if (Text(type, "id") is not { } id)
                    continue;
                var models = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                if (type.TryGetProperty("models", out var m) && m.ValueKind == JsonValueKind.Object)
                {
                    foreach (var family in m.EnumerateObject().Where(f => f.Value.ValueKind == JsonValueKind.Array))
                        models[family.Name] = family.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList();
                }
                types.Add(new BodyModels(id, models));
            }
        }
        return new PlayerBodies(types, Text(element, "default"), Text(element, "base_build") ?? "average");
    }

    private static string? Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
