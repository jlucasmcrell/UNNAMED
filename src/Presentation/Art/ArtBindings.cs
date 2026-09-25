// UNNAMED Presentation - what the game draws and plays for each thing the simulation has, by semantic asset ID
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// How a model is placed for the thing it stands for, always at its authored size (see <c>art_bindings.json</c>'s comment): a node's
/// <see cref="Spent"/> look beside its ready one, a station's <see cref="Beside"/> model at <see cref="BesideOffset"/> and its
/// <see cref="Light"/>, a building's lights inside it (<see cref="Lights"/>).
/// </summary>
public sealed record Placement(string? Model, string Fit = "none", string? Surface = null, string? Material = null, bool Hidden = false,
    bool HideReady = false, string? Beside = null, Vector3 Offset = default, string? Sound = null, string? Spent = null,
    Vector3 BesideOffset = default, LightSpec? Light = null, IReadOnlyList<LightSpec>? Lights = null,
    Vector3? SurfaceTint = null, Vector3? Glow = null, float GlowEnergy = 0);

/// <summary>
/// A light that is presentation data, not a coordinate in code: where it stands relative to what it lights (a station's feet, a building's
/// footprint centre on the ground), its colour, energy, range, and whether it casts shadows.
/// </summary>
public sealed record LightSpec(Vector3 At, Color Colour, float Energy, float Range, bool Shadows, float Size, float FogEnergy, string? Note = null)
{
    /// <summary>The light itself, placed relative to its parent.</summary>
    public OmniLight3D Build(string name) => new()
    {
        Name = name, Position = At, LightColor = Colour, LightEnergy = Energy, OmniRange = Range, OmniAttenuation = 1.2f, ShadowEnabled = Shadows,
        LightSize = Size, LightVolumetricFogEnergy = FogEnergy,
    };
}

public sealed record CreatureArt(string Model, IReadOnlyDictionary<string, string> Clips, string Voice, string Flesh,
    IReadOnlyDictionary<string, string> Steps, int StepsPerSound);

/// <summary>
/// A skinned person: the body, the hand bones a weapon rides on, the grip frame on each (in the bone's own space: where the hand closes
/// and which way a held weapon points - a rig without hand sockets gets them here, stated), and the clips by state.
/// </summary>
public sealed record PersonArt(string Model, string Hand, string? OffHand, IReadOnlyDictionary<string, string> Clips, Transform3D? Grip,
    Transform3D? OffGrip);

/// <summary>A weapon held in the hand: its model (held by its own grip socket), its family, and whether the off hand holds it.</summary>
public sealed record WeaponArt(string Model, string Family, bool OffHand);

public sealed record CellSound(string Bed, IReadOnlyList<string> Details, string Surface);

/// <summary>
/// The presentation's bindings (<c>res://Art/art_bindings.json</c>): game IDs to the asset pipeline's stable semantic IDs. Data, so no
/// content ID lives in the game's code; presentation only, so nothing here touches a rule, a save or the content hash.
/// </summary>
public sealed class ArtBindings
{
    /// <summary>Where the bindings ship: inside the project, so an exported build carries them in its pack.</summary>
    public const string ResourcePath = "res://Art/art_bindings.json";

    private readonly Dictionary<string, Placement> _structures = new(StringComparer.Ordinal);
    // Phase B's proofs: a structure's look while a --visual option has a value (the key, the value, the looks by structure ID).
    private readonly List<(string Key, string Value, Dictionary<string, Placement> Looks)> _optionStructures = new();
    private readonly List<(string Prefix, IReadOnlyList<string> Models, Placement Look)> _prefixes = new();
    private readonly List<(string Prefix, string Material)> _wallMaterials = new();
    private readonly Dictionary<string, Placement> _buildings = new(StringComparer.Ordinal);

    public static ArtBindings Empty { get; } = new();

    /// <summary>Assets that exist but are not fit to draw, with why (reported to the asset pipeline; the greybox stands in).</summary>
    public IReadOnlyDictionary<string, string> Withheld { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Terrain { get; private init; } = new Dictionary<string, string>();
    public string? Cliffs { get; private init; }
    public IReadOnlyDictionary<string, Placement> Doors { get; private init; } = new Dictionary<string, Placement>();
    public IReadOnlyDictionary<string, Placement> Containers { get; private init; } = new Dictionary<string, Placement>();
    public IReadOnlyDictionary<string, Placement> Nodes { get; private init; } = new Dictionary<string, Placement>();
    public IReadOnlyDictionary<string, Placement> Stations { get; private init; } = new Dictionary<string, Placement>();
    public Placement? GroundItem { get; private init; }
    public IReadOnlyDictionary<string, CreatureArt> Creatures { get; private init; } = new Dictionary<string, CreatureArt>();
    public IReadOnlyDictionary<string, PersonArt> People { get; private init; } = new Dictionary<string, PersonArt>();
    public IReadOnlyDictionary<string, WeaponArt> Weapons { get; private init; } = new Dictionary<string, WeaponArt>();
    public WeaponArt? CompanionWeapon { get; private init; }
    /// <summary>A shot's model by its kind ("arrow"), for <see cref="Greybox.ProjectilesView"/>; no entry, no entry loaded, greybox.</summary>
    public IReadOnlyDictionary<string, string> Projectiles { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Icons { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Effects { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, CellSound> CellSounds { get; private init; } = new Dictionary<string, CellSound>();
    public string InteriorSurface { get; private init; } = "wood";
    public IReadOnlyDictionary<string, string> FormulaSounds { get; private init; } = new Dictionary<string, string>();
    public string? ForgeStation { get; private init; }

    /// <summary>The look for a region structure by its ID (exact, then by prefix - a tree's model chosen by its ID), or null.</summary>
    public Placement? Structure(string id)
    {
        foreach (var (key, value, looks) in _optionStructures)
        {
            if (VisualOptions.All.GetValueOrDefault(key) == value && looks.TryGetValue(id, out var chosen))
                return chosen;
        }
        if (_structures.TryGetValue(id, out var look))
            return look;
        foreach (var (prefix, models, prefixLook) in _prefixes)
        {
            if (!id.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            uint hash = 2166136261;
            foreach (char c in id)
                hash = (hash ^ c) * 16777619;
            return prefixLook with { Model = models[(int)(hash % (uint)models.Count)] };
        }
        return null;
    }

    /// <summary>What a structure is made of, for the sound of an arrow striking it ("wood", "stone"), or null.</summary>
    public string? StructureMaterial(string id)
    {
        if (Structure(id)?.Material is { } material)
            return material;
        foreach (var (prefix, wall) in _wallMaterials)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal))
                return wall;
        }
        return null;
    }

    /// <summary>The prefixes of the buildings' walls (a longhouse's, a forge's): inside their bounds is indoors, for footsteps.</summary>
    public IEnumerable<string> BuildingPrefixes => _wallMaterials.Select(w => w.Prefix);

    /// <summary>A whole building's model by its walls' prefix: drawn over the walls' footprint, which stays the world's truth.</summary>
    public IReadOnlyDictionary<string, Placement> Buildings => _buildings;

    /// <summary>Every static model the bindings name (for the gallery).</summary>
    public IEnumerable<string> StaticModels()
    {
        foreach (var look in _structures.Values.Concat(_optionStructures.SelectMany(o => o.Looks.Values)).Concat(_buildings.Values).Concat(Doors.Values).Concat(Containers.Values).Concat(Nodes.Values).Concat(Stations.Values))
        {
            if (look.Model is { } model)
                yield return model;
            if (look.Beside is { } beside)
                yield return beside;
            if (look.Spent is { } spent)
                yield return spent;
        }
        foreach (var (_, models, _) in _prefixes)
        {
            foreach (string model in models)
                yield return model;
        }
        foreach (var weapon in Weapons.Values)
            yield return weapon.Model;
        foreach (string model in Projectiles.Values)
            yield return model;
        if (GroundItem?.Model is { } item)
            yield return item;
    }

    public static ArtBindings Load(string resourcePath)
    {
        if (!Godot.FileAccess.FileExists(resourcePath))
            return Empty;
        try
        {
            using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(resourcePath));
            var root = json.RootElement;
            var audio = root.TryGetProperty("audio", out var a) ? a : default;
            var bindings = new ArtBindings
            {
                Withheld = Strings(root, "withheld"),
                Terrain = Strings(root, "terrain"),
                Cliffs = Text(root, "cliffs"),
                Doors = Looks(root, "doors"),
                Containers = Looks(root, "containers"),
                Nodes = Looks(root, "nodes"),
                Stations = Looks(root, "stations"),
                GroundItem = root.TryGetProperty("ground_item", out var ground) ? Look(ground) : null,
                Creatures = root.TryGetProperty("creatures", out var creatures)
                    ? creatures.EnumerateObject().ToDictionary(c => c.Name, c => new CreatureArt(Text(c.Value, "model")!,
                        new[] { "idle", "walk", "run", "attack", "hit", "death" }.ToDictionary(s => s, s => $"{Text(c.Value, "clips")}.{s}"),
                        Text(c.Value, "voice") ?? string.Empty, Text(c.Value, "flesh") ?? "flesh", Strings(c.Value, "steps"),
                        c.Value.TryGetProperty("steps_per_sound", out var steps) ? steps.GetInt32() : 1), StringComparer.Ordinal)
                    : new Dictionary<string, CreatureArt>(),
                People = root.TryGetProperty("people", out var people)
                    ? people.EnumerateObject().ToDictionary(p => p.Name, p => new PersonArt(Text(p.Value, "model")!, Text(p.Value, "hand") ?? "hand_r",
                        Text(p.Value, "off_hand"), Strings(p.Value, "clips"), Grip(p.Value, "grip"), Grip(p.Value, "off_grip")), StringComparer.Ordinal)
                    : new Dictionary<string, PersonArt>(),
                Weapons = root.TryGetProperty("weapons", out var weapons)
                    ? weapons.EnumerateObject().ToDictionary(w => w.Name, w => Weapon(w.Value), StringComparer.Ordinal)
                    : new Dictionary<string, WeaponArt>(),
                CompanionWeapon = root.TryGetProperty("companion_weapon", out var companion) ? Weapon(companion) : null,
                Projectiles = root.TryGetProperty("projectiles", out var projectiles)
                    ? projectiles.EnumerateObject().Where(p => Text(p.Value, "model") is not null).ToDictionary(p => p.Name, p => Text(p.Value, "model")!, StringComparer.Ordinal)
                    : new Dictionary<string, string>(),
                Icons = Strings(root, "icons"),
                Effects = Strings(root, "effects"),
                CellSounds = audio.ValueKind == JsonValueKind.Object && audio.TryGetProperty("cells", out var cells)
                    ? cells.EnumerateObject().ToDictionary(c => c.Name, c => new CellSound(Text(c.Value, "bed")!,
                        c.Value.GetProperty("details").EnumerateArray().Select(d => d.GetString()!).ToList(), Text(c.Value, "surface") ?? "dirt"), StringComparer.Ordinal)
                    : new Dictionary<string, CellSound>(),
                InteriorSurface = Text(audio, "interior_surface") ?? "wood",
                FormulaSounds = Strings(audio, "formulas"),
                ForgeStation = Text(audio, "forge_station"),
            };
            if (root.TryGetProperty("structures", out var structures))
            {
                foreach (var structure in structures.EnumerateObject())
                    bindings._structures[structure.Name] = Look(structure.Value);
            }
            if (root.TryGetProperty("structures_by_visual_option", out var byOption))
            {
                foreach (var option in byOption.EnumerateObject())
                {
                    string[] kv = option.Name.Split('=', 2);
                    if (kv.Length == 2)
                        bindings._optionStructures.Add((kv[0], kv[1], option.Value.EnumerateObject().ToDictionary(o => o.Name, o => Look(o.Value), StringComparer.Ordinal)));
                }
            }
            if (root.TryGetProperty("structure_prefixes", out var prefixes))
            {
                foreach (var prefix in prefixes.EnumerateObject())
                    bindings._prefixes.Add((prefix.Name, prefix.Value.GetProperty("models").EnumerateArray().Select(m => m.GetString()!).ToList(), Look(prefix.Value)));
            }
            if (root.TryGetProperty("building_walls", out var walls))
            {
                foreach (var wall in walls.EnumerateObject())
                {
                    bindings._wallMaterials.Add((wall.Name, Text(wall.Value, "material") ?? "wood"));
                    var look = Look(wall.Value);
                    if (look.Model is not null || look.Lights is { Count: > 0 })
                        bindings._buildings[wall.Name] = look;
                }
            }
            return bindings;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or IndexOutOfRangeException
                                      or FormatException)
        {
            GD.PushWarning($"UNNAMED art: the bindings at {resourcePath} could not be read ({e.Message}); drawing greybox");
            return Empty;
        }
    }

    private static Placement Look(JsonElement e) => new(
        Text(e, "model"), Text(e, "fit") ?? "none", Text(e, "surface"), Text(e, "material"), Flag(e, "hidden"), Flag(e, "hide_ready"),
        Text(e, "beside"), e.TryGetProperty("offset", out var offset) ? Vector(offset) : default, Text(e, "sound"), Text(e, "spent"),
        e.TryGetProperty("beside_offset", out var besideOffset) ? Vector(besideOffset) : default,
        e.TryGetProperty("light", out var light) ? Light(light) : null,
        e.TryGetProperty("interior_light", out var lights)
            ? (lights.ValueKind == JsonValueKind.Array ? lights.EnumerateArray().Select(Light) : new[] { Light(lights) }).OfType<LightSpec>().ToList()
            : null,
        e.TryGetProperty("surface_tint", out var tint) ? Vector(tint) : null,
        e.TryGetProperty("glow", out var glow) ? Vector(glow) : null, Number(e, "glow_energy", 0));

    private static bool Flag(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>A light: "at" [x, y, z] metres from what it lights, "colour" [r, g, b], "energy", "range" metres, "shadows", "size"; null when malformed.</summary>
    private static LightSpec? Light(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("at", out var at) || !IsVector(at))
        {
            GD.PushWarning($"UNNAMED art: a light binding without its \"at\" [x, y, z] is left out: {e.GetRawText()}");
            return null;
        }
        var colour = e.TryGetProperty("colour", out var c) && IsVector(c) ? Vector(c) : Vector3.One;
        return new LightSpec(Vector(at), new Color(colour.X, colour.Y, colour.Z), Number(e, "energy", 1f), Number(e, "range", 5f), Flag(e, "shadows"),
            Number(e, "size", 0f), Number(e, "fog_energy", 1f), Text(e, "note"));
    }

    private static float Number(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d) && double.IsFinite(d) ? (float)d : fallback;

    private static bool IsVector(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3 && e.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number);

    private static WeaponArt Weapon(JsonElement e) => new(Text(e, "model")!, Text(e, "family") ?? "sword", Text(e, "hand") == "off_hand");

    private static Transform3D? Grip(JsonElement e, string name) =>
        e.TryGetProperty(name, out var grip) && grip.ValueKind == JsonValueKind.Object
            ? HeldWeapon.Frame(Vector(grip.GetProperty("at")), Vector(grip.GetProperty("primary")), Vector(grip.GetProperty("secondary")))
            : null;

    private static Vector3 Vector(JsonElement e) =>
        IsVector(e) ? new((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble()) : throw new FormatException($"not [x, y, z]: {e.GetRawText()}");

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Dictionary<string, string> Strings(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var map) && map.ValueKind == JsonValueKind.Object
            ? map.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static Dictionary<string, Placement> Looks(JsonElement e, string name) =>
        e.TryGetProperty(name, out var map) ? map.EnumerateObject().ToDictionary(p => p.Name, p => Look(p.Value), StringComparer.Ordinal)
            : new Dictionary<string, Placement>(StringComparer.Ordinal);
}
