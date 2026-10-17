// UNNAMED Presentation - the creator's bodies: a body type and a build, and the model each one is drawn with (WP2; owner D3, OD15:20-21)
// Godot presentation only, and free of Godot itself so the tests can read it: the domain keeps only the preset ID (D-11)

using System.Text.Json;
using UNNAMED.Domain.Character;

namespace UNNAMED.Presentation.Art;

/// <summary>A body type the creator offers: its words, and its models by the player's model family, the first one installed drawn.</summary>
public sealed record BodyType(string Id, string Label, IReadOnlyDictionary<string, IReadOnlyList<string>> Models);

/// <summary>A build the creator offers: its words and how much broader (over 1) or narrower (under 1) the limbs and trunk are drawn.</summary>
public sealed record BodyBuild(string Id, string Label, float Girth);

/// <summary>
/// The bindings' <c>player_bodies</c>: the body types and builds the creator offers, whose pair is the body preset a character keeps
/// (<c>body.&lt;type&gt;.&lt;build&gt;</c>, e.g. <c>body.veth_f.lean</c>). <c>body.default</c> - every character made before the creator - is
/// <see cref="Default"/>. A type names its models per family: <c>production</c> is the cast the player plays (MPFB), <c>metahuman</c> the
/// <c>--visual player=metahuman_*</c> bodies. A build is drawn as a girth on its type's body (<c>Art/BuildShape.cs</c>).
/// </summary>
public sealed class PlayerBodies
{
    public const string Prefix = "body.";

    public static PlayerBodies None { get; } = new(Array.Empty<BodyType>(), Array.Empty<BodyBuild>(), null);

    public PlayerBodies(IReadOnlyList<BodyType> types, IReadOnlyList<BodyBuild> builds, string? fallback)
    {
        Types = types;
        Builds = builds;
        Default = fallback is not null && Parse(fallback) is not null ? fallback
            : types.Count > 0 && builds.Count > 0 ? Id(types[0], builds[builds.Count / 2]) : CharacterSpec.DefaultBodyPreset;
    }

    public IReadOnlyList<BodyType> Types { get; }

    public IReadOnlyList<BodyBuild> Builds { get; }

    /// <summary>The preset a character with no choice is drawn with, and the creator starts from.</summary>
    public string Default { get; }

    public bool IsEmpty => Types.Count == 0 || Builds.Count == 0;

    public static string Id(BodyType type, BodyBuild build) => $"{Prefix}{type.Id}.{build.Id}";

    /// <summary>The type and build a preset names, or null when it names none this file offers; <c>body.default</c> is <see cref="Default"/>.</summary>
    public (BodyType Type, BodyBuild Build)? Parse(string? presetId)
    {
        if (presetId == CharacterSpec.DefaultBodyPreset && Default != CharacterSpec.DefaultBodyPreset)
            presetId = Default;
        if (presetId is null || !presetId.StartsWith(Prefix, StringComparison.Ordinal))
            return null;
        string[] parts = presetId[Prefix.Length..].Split('.');
        if (parts.Length != 2)
            return null;
        var type = Types.FirstOrDefault(t => t.Id == parts[0]);
        var build = Builds.FirstOrDefault(b => b.Id == parts[1]);
        return type is null || build is null ? null : (type, build);
    }

    /// <summary>The type and build a preset is drawn with: its own, or the default's when this file does not offer it (an older save).</summary>
    public (BodyType Type, BodyBuild Build)? Resolve(string? presetId) => Parse(presetId) ?? Parse(Default);

    /// <summary>The model a type is drawn with in a family: the first of its models <paramref name="installed"/> says is there, or null.</summary>
    public static string? Model(BodyType type, string family, Func<string, bool> installed) =>
        type.Models.TryGetValue(family, out var models) ? models.FirstOrDefault(installed) : null;

    /// <summary>The model family a run's <c>--visual player=</c> value draws the player from.</summary>
    public static string Family(string? playerOption) =>
        playerOption is not null && playerOption.StartsWith("metahuman", StringComparison.Ordinal) ? "metahuman" : "production";

    /// <summary>The bindings' <c>player_bodies</c> object; <see cref="None"/> when it is missing or malformed, so the creator offers the default body only.</summary>
    public static PlayerBodies Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return None;
        var types = new List<BodyType>();
        if (element.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array)
        {
            foreach (var type in t.EnumerateArray())
            {
                if (Text(type, "id") is not { } id || Text(type, "label") is not { } label)
                    continue;
                var models = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                if (type.TryGetProperty("models", out var m) && m.ValueKind == JsonValueKind.Object)
                {
                    foreach (var family in m.EnumerateObject().Where(f => f.Value.ValueKind == JsonValueKind.Array))
                        models[family.Name] = family.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList();
                }
                types.Add(new BodyType(id, label, models));
            }
        }
        var builds = new List<BodyBuild>();
        if (element.TryGetProperty("builds", out var b) && b.ValueKind == JsonValueKind.Array)
        {
            foreach (var build in b.EnumerateArray())
            {
                if (Text(build, "id") is not { } id || Text(build, "label") is not { } label)
                    continue;
                float girth = build.TryGetProperty("girth", out var g) && g.ValueKind == JsonValueKind.Number ? (float)g.GetDouble() : 1f;
                builds.Add(new BodyBuild(id, label, Math.Clamp(girth, 0.85f, 1.15f)));
            }
        }
        return new PlayerBodies(types, builds, Text(element, "default"));
    }

    private static string? Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
