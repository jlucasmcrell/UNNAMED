// UNNAMED Presentation - what the game draws and plays for each thing the simulation has, by semantic asset ID
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A default a record's own parameter list cannot name (a record's body is not in scope there), so the value lives in its own type.
/// See <see cref="LightSpec.DefaultAttenuation"/> for the measurement behind the number.
/// </summary>
public static class LightDefaults
{
    /// <summary>Godot's own default decay for <c>omni_attenuation</c>.</summary>
    public const float Attenuation = 1.2f;
}

/// <summary>
/// How a model is placed for the thing it stands for, always at its authored size (see <c>art_bindings.json</c>'s comment): a node's
/// <see cref="Spent"/> look beside its ready one, a station's <see cref="Beside"/> model at <see cref="BesideOffset"/> and its
/// <see cref="Light"/>, a building's lights inside it (<see cref="Lights"/>).
/// </summary>
public sealed record Placement(string? Model, string Fit = "none", string? Surface = null, string? Material = null, bool Hidden = false,
    bool HideReady = false, string? Beside = null, Vector3 Offset = default, string? Sound = null, string? Spent = null,
    Vector3 BesideOffset = default, LightSpec? Light = null, IReadOnlyList<LightSpec>? Lights = null,
    Vector3? SurfaceTint = null, Vector3? Glow = null, float GlowEnergy = 0, float Wind = 0,
    IReadOnlyList<(string Recipe, Vector3 At)>? Emitters = null, float YawSpread = 0, float LateS = 0, string? AnimationFlag = null, bool HideSetMark = false);

/// <summary>
/// A light that is presentation data, not a coordinate in code: where it stands relative to what it lights (a station's feet, a building's
/// footprint centre on the ground), its colour, energy, range, and whether it casts shadows.
/// </summary>
/// <remarks>
/// UNBLOCK FIX (not mine, declared): <c>Attenuation = DefaultAttenuation</c> in the parameter list below cannot resolve, because a record's
/// own members are not in scope in its parameter list. The constant moved to <see cref="LightDefaults"/> and every reference with it.
/// </remarks>
public sealed record LightSpec(Vector3 At, Color Colour, float Energy, float Range, bool Shadows, float Size, float FogEnergy,
    float Attenuation = LightDefaults.Attenuation, float Daylight = 0, string? Note = null)
{
    /// <summary>
    /// The decay a light gets when it does not state one: Godot's own default for <c>omni_attenuation</c>, and the
    /// curve every energy, range and offset in the shipped bindings was authored against.
    ///
    /// MEASURED, not assumed. `CraftingView.cs:170` builds a station's light with a bare <c>new OmniLight3D</c> and
    /// never touches the property; the forge light that code path produced reports <c>omni_attenuation 1.2</c> out
    /// of a real frame's own scene dump. So this is the engine's default, which means the 1.2 that
    /// <see cref="Build"/> used before this round was not a tuned choice - it was the default written down - and
    /// every hand-built light in the codebase (<c>VfxSheet</c>, <c>CraftingView</c>, <c>ProjectilesView</c>) is
    /// still on it today. The forced 2.0 applied to binding-driven lights only, which left the game running two
    /// curves at once. See docs/phase_b/INTERIORS_FOLLOWUP.md section 2 for what the interiors measured under each.
    /// </summary>
    public const float DefaultAttenuation = LightDefaults.Attenuation;

    /// <summary>The light itself, placed relative to its parent.</summary>
    /// <remarks>
    /// ATTENUATION IS DATA NOW, AND IT WAS NOT BEFORE. It was a literal in this method, and on the morning of
    /// 2026-09-29 an uncommitted edit changed that literal from 1.2 to 2.0 - correctly observing in its own comment
    /// that "the energies in bindings.json were authored against the flatter curve, so they want raising to match",
    /// and then not raising them. Every light in the game changed shape and not one authored number moved with it.
    ///
    /// WHY THE CURVE WAS THE THING TO PUT BACK, MEASURED RATHER THAN ARGUED. Godot's omni falloff is
    /// <c>(1 - (d/range)^4)^2 * d^-decay</c>, so the two curves cross at almost exactly 1 m and the ratio grows
    /// with distance: 1.00 at 1 m, 1.74 at 2 m, 2.41 at 3 m, 3.03 at 4 m, 3.62 at 5 m. Decay 2.0 is therefore
    /// BRIGHTER than 1.2 inside a metre and darker beyond it, which is the shape the judged frames actually have:
    /// the wall within a metre of the lamp sits at 0.350 frame luminance while the floor three metres away sits at
    /// 0.056, and the frame's 99th percentile is 0.335 - nothing in the room reaches a third of full scale. Raising
    /// the energy multiplies both ends, so it would blow the pool that is already correctly exposed to lift a floor
    /// that is dark because the curve does not reach it. Only the curve moves one end without the other, and only
    /// the curve restores the three values per light (energy, range, and the lamp's own height) that the 2.0 edit
    /// invalidated at once. See docs/phase_b/INTERIORS_FOLLOWUP.md section 2.
    ///
    /// <see cref="Energy"/> IS AUTHORED AGAINST <see cref="Attenuation"/>, which is why both are here: the pair is
    /// one decision, and a binding that changes one is authoring a different light.
    ///
    /// <see cref="Daylight"/> is how much of the light the sun takes away: 0 for a hearth or a lamp that burns all
    /// day, 1 for a street lantern that is lit at dusk. It is the same axis the fog's light colour and the night
    /// fill already run on (<see cref="Greybox.SkyView"/>), applied to the one kind of light that was still pinned
    /// to the moment the process booted.
    /// </remarks>
    public OmniLight3D Build(string name) => new()
    {
        Name = name, Position = At, LightColor = Colour, LightEnergy = Energy, OmniRange = Range, OmniAttenuation = Attenuation,
        ShadowEnabled = Shadows, LightSize = Size, LightVolumetricFogEnergy = FogEnergy,
    };
}

public sealed record CreatureArt(string Model, IReadOnlyDictionary<string, string> Clips, string Voice, string Flesh,
    IReadOnlyDictionary<string, string> Steps, int StepsPerSound)
{
    public IReadOnlyDictionary<string, float> Paces { get; init; } = new Dictionary<string, float>();
    /// <summary>How the species is drawn "plausible but wrong" over its scanned model (<see cref="OtherFauna"/>), or null as scanned.</summary>
    public FaunaLook? Fauna { get; init; }
}

/// <summary>
/// A skinned person: the body, the hand bones a weapon rides on, the grip frame on each (in the bone's own space: where the hand closes
/// and which way a held weapon points - a rig without hand sockets gets them here, stated), and the clips by state.
/// </summary>
public sealed record PersonArt(string Model, string Hand, string? OffHand, IReadOnlyDictionary<string, string> Clips, Transform3D? Grip,
    Transform3D? OffGrip)
{
    /// <summary>The pace (m/s) each gait clip was authored at, where it is not the game's own (walk 1.4, run 3.2, sprint 5, crouched 1.6).</summary>
    public IReadOnlyDictionary<string, float> Paces { get; init; } = new Dictionary<string, float>();

    /// <summary>
    /// Where each action clip's blow falls, as fractions of the clip: the end of the windup, the end of the active window and the end
    /// of the recovery - the simulation's phases are mapped onto these (Phase B remediation); a clip without them uses 0.45 / 0.7 / 1.
    /// </summary>
    public IReadOnlyDictionary<string, float[]> Phases { get; init; } = new Dictionary<string, float[]>();

    /// <summary>The garments worn over the body (rigged asset IDs, each with its <c>_garment.json</c>; <see cref="Outfit"/>), or none.</summary>
    public IReadOnlyList<string> Outfit { get; init; } = Array.Empty<string>();
    /// <summary>Optional item definition to modular garment IDs, over the permanent Outfit underlayers.</summary>
    public IReadOnlyDictionary<string, string[]> EquipmentOutfits { get; init; } = new Dictionary<string, string[]>();
}

/// <summary>A weapon held in the hand: its model (held by its own grip socket), its family, and whether the off hand holds it.</summary>
public sealed record WeaponArt(string Model, string Family, bool OffHand);

public sealed record CellSound(string Bed, IReadOnlyList<string> Details, string Surface);

/// <summary>A place with a music theme (a family stem such as <c>mus.settlement</c>) heard within its radius.</summary>
public sealed record MusicPlace(string Theme, float RadiusM);

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

    /// <summary>
    /// The id prefixes whose structures form a roofed room, each getting one slab over its own walls.
    /// </summary>
    /// <remarks>
    /// Defaults to the two the greybox has always roofed. A region that is properly under cover - the
    /// Barrowworks is workings cut into a hill - names its own rooms here, because `Roofs` used to carry those
    /// two names in code and no region could ask for a third.
    /// </remarks>
    public IReadOnlyList<string> RoofGroups { get; private set; } = new[] { "longhouse", "forge" };

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

    /// <summary>The bodies the character creator offers (<c>player_bodies</c>, WP2), each drawn on the player's binding.</summary>
    public PlayerBodies Bodies { get; private init; } = PlayerBodies.None;
    public IReadOnlyDictionary<string, WeaponArt> Weapons { get; private init; } = new Dictionary<string, WeaponArt>();
    public WeaponArt? CompanionWeapon { get; private init; }
    /// <summary>A shot's model by its kind ("arrow"), for <see cref="Greybox.ProjectilesView"/>; no entry, no entry loaded, greybox.</summary>
    public IReadOnlyDictionary<string, string> Projectiles { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Icons { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Effects { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, CellSound> CellSounds { get; private init; } = new Dictionary<string, CellSound>();
    public string InteriorSurface { get; private init; } = "wood";
    public IReadOnlyDictionary<string, string> FormulaSounds { get; private init; } = new Dictionary<string, string>();

    /// <summary>What a switch sounds like as it is set, by its key (the audio bindings' <c>switches</c>; Phase B VFX lane).</summary>
    public IReadOnlyDictionary<string, string> SwitchSounds { get; private init; } = new Dictionary<string, string>();
    public string? ForgeStation { get; private init; }

    /// <summary>The footstep surface of a worn path or road (<c>audio.path_surface</c>), heard on the path whatever its cell's ground; none, the cell's.</summary>
    public string? PathSurface { get; private init; }

    /// <summary>The bed heard inside a building (<c>audio.interior_bed</c>), in place of the cell's.</summary>
    public string? InteriorBed { get; private init; }

    /// <summary>What using a consumable sounds like, by item ID (<c>audio.consumables</c>: a family).</summary>
    public IReadOnlyDictionary<string, string> ConsumableSounds { get; private init; } = new Dictionary<string, string>();

    /// <summary>The places with music, by location ID (<c>audio.music.places</c>).</summary>
    public IReadOnlyDictionary<string, MusicPlace> MusicPlaces { get; private init; } = new Dictionary<string, MusicPlace>();

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
        => LoadWithOverlay(resourcePath, null);

    /// <summary>
    /// The bindings for a run, with a second file layered over the first. A region brings its own art
    /// without restating what it shares: the HUD, the icons, the effect sheets and the audio beds are
    /// the same everywhere, and only the models, materials and looks are the region's own. Objects merge
    /// key by key, recursively; anything else - a string, a number, an array - replaces what it lands on.
    /// A region's file therefore says only what it changes.
    /// </summary>
    public static ArtBindings LoadWithOverlay(string resourcePath, string? overlayPath)
    {
        if (overlayPath is null || !Godot.FileAccess.FileExists(overlayPath))
            return LoadOnly(resourcePath);
        if (!Godot.FileAccess.FileExists(resourcePath))
            return LoadOnly(overlayPath);
        try
        {
            using var baseJson = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(resourcePath));
            using var overlayJson = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(overlayPath));
            return FromJson(Merge(baseJson.RootElement, overlayJson.RootElement));
        }
        catch (JsonException e)
        {
            GD.PushError($"art bindings: overlay {overlayPath} over {resourcePath} does not parse ({e.Message}); using the base file alone");
            return LoadOnly(resourcePath);
        }
    }

    /// <summary>Deep-merges an overlay onto a base object, returning the merged document as text.</summary>
    private static string Merge(JsonElement baseElement, JsonElement overlayElement)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            MergeInto(writer, baseElement, overlayElement);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void MergeInto(Utf8JsonWriter writer, JsonElement baseElement, JsonElement overlayElement)
    {
        if (baseElement.ValueKind != JsonValueKind.Object || overlayElement.ValueKind != JsonValueKind.Object)
        {
            overlayElement.WriteTo(writer);
            return;
        }
        writer.WriteStartObject();
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in baseElement.EnumerateObject())
        {
            written.Add(property.Name);
            writer.WritePropertyName(property.Name);
            if (overlayElement.TryGetProperty(property.Name, out var over))
                MergeInto(writer, property.Value, over);
            else
                property.Value.WriteTo(writer);
        }
        foreach (var property in overlayElement.EnumerateObject())
        {
            if (written.Contains(property.Name))
                continue;
            writer.WritePropertyName(property.Name);
            property.Value.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static ArtBindings LoadOnly(string resourcePath)
    {
        if (!Godot.FileAccess.FileExists(resourcePath))
            return Empty;
        try
        {
            return FromJson(Godot.FileAccess.GetFileAsString(resourcePath));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or IndexOutOfRangeException
                                      or FormatException)
        {
            GD.PushWarning($"UNNAMED art: the bindings at {resourcePath} could not be read ({e.Message}); drawing greybox");
            return Empty;
        }
    }

    /// <summary>
    /// The bindings a document states. Reading and parsing are separate so a region's file can be merged
    /// into the shared one as text, before either is interpreted.
    /// </summary>
    private static ArtBindings FromJson(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
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
                        c.Value.TryGetProperty("clips", out var creatureClips) && creatureClips.ValueKind == JsonValueKind.Object
                            ? Strings(c.Value, "clips")
                            : new[] { "idle", "walk", "run", "attack", "hit", "death" }.ToDictionary(s => s, s => $"{Text(c.Value, "clips")}.{s}"),
                        Text(c.Value, "voice") ?? string.Empty, Text(c.Value, "flesh") ?? "flesh", Strings(c.Value, "steps"),
                        c.Value.TryGetProperty("steps_per_sound", out var steps) ? steps.GetInt32() : 1)
                        {
                            Fauna = FaunaLook.Parse(c.Value),
                            Paces = c.Value.TryGetProperty("paces", out var creaturePaces)
                                ? creaturePaces.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle()) : new Dictionary<string, float>(),
                        },
                        StringComparer.Ordinal)
                    : new Dictionary<string, CreatureArt>(),
                People = root.TryGetProperty("people", out var people)
                    ? people.EnumerateObject().ToDictionary(p => p.Name, p => OptionClips(root, p.Name, new PersonArt(Text(p.Value, "model")!,
                        Text(p.Value, "hand") ?? "hand_r", Text(p.Value, "off_hand"), Strings(p.Value, "clips"), Grip(p.Value, "grip"), Grip(p.Value, "off_grip"))
                        { Outfit = List(p.Value, "outfit") ?? Array.Empty<string>(), EquipmentOutfits = EquipmentOutfits(p.Value) }),
                        StringComparer.Ordinal)
                    : new Dictionary<string, PersonArt>(),
                Bodies = root.TryGetProperty("player_bodies", out var bodies) ? PlayerBodies.Read(bodies) : PlayerBodies.None,
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
                SwitchSounds = Strings(audio, "switches"),
                ForgeStation = Text(audio, "forge_station"),
                PathSurface = Text(audio, "path_surface"),
                InteriorBed = Text(audio, "interior_bed"),
                ConsumableSounds = Strings(audio, "consumables"),
                MusicPlaces = audio.ValueKind == JsonValueKind.Object && audio.TryGetProperty("music", out var music) && music.TryGetProperty("places", out var places)
                    ? places.EnumerateObject().ToDictionary(p => p.Name, p => new MusicPlace(Text(p.Value, "theme")!,
                        p.Value.TryGetProperty("radius_m", out var r) ? (float)r.GetDouble() : 60f), StringComparer.Ordinal)
                    : new Dictionary<string, MusicPlace>(),
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
            if (root.TryGetProperty("roof_groups", out var groups))
            {
                var named = groups.EnumerateArray().Select(g => g.GetString()!).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
                if (named.Count > 0)
                    bindings.RoofGroups = named;
            }
            return bindings;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or IndexOutOfRangeException
                                      or FormatException)
        {
            GD.PushWarning($"UNNAMED art: the bindings document could not be read ({e.Message}); drawing greybox");
            return Empty;
        }
    }

    /// <summary>
    /// A person's clips under the run's visual options (Phase B, B12: <c>people_clips_by_visual_option</c>): each named state's clip replaced,
    /// the rest kept, with the replacing clips' authored paces.
    /// </summary>
    private static PersonArt OptionClips(JsonElement root, string person, PersonArt look)
    {
        look = OptionModel(root, person, look);
        if (!root.TryGetProperty("people_clips_by_visual_option", out var byOption))
            return look;
        foreach (var option in byOption.EnumerateObject())
        {
            string[] kv = option.Name.Split('=', 2);
            if (kv.Length != 2 || VisualOptions.All.GetValueOrDefault(kv[0]) != kv[1] || !option.Value.TryGetProperty(person, out var over))
                continue;
            var clips = look.Clips.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
            foreach (var (state, clip) in Strings(over, "clips"))
                clips[state] = clip;
            var paces = over.TryGetProperty("paces", out var p) && p.ValueKind == JsonValueKind.Object
                ? p.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.Number).ToDictionary(x => x.Name, x => (float)x.Value.GetDouble(), StringComparer.Ordinal)
                : new Dictionary<string, float>(StringComparer.Ordinal);
            var phases = over.TryGetProperty("phases", out var ph) && ph.ValueKind == JsonValueKind.Object
                ? ph.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.Array && x.Value.GetArrayLength() == 3)
                    .ToDictionary(x => x.Name, x => x.Value.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray(), StringComparer.Ordinal)
                : new Dictionary<string, float[]>(StringComparer.Ordinal);
            look = look with { Clips = clips, Paces = paces, Phases = phases };
        }
        return look;
    }

    /// <summary>
    /// A person's model under the run's visual options (Phase B, character fidelity: <c>people_by_visual_option</c>): the model, and the hand
    /// bones and grips where the replacing model's rig names them differently.
    /// </summary>
    private static PersonArt OptionModel(JsonElement root, string person, PersonArt look)
    {
        if (!root.TryGetProperty("people_by_visual_option", out var byOption))
            return look;
        foreach (var option in byOption.EnumerateObject())
        {
            string[] kv = option.Name.Split('=', 2);
            if (kv.Length != 2 || VisualOptions.All.GetValueOrDefault(kv[0]) != kv[1] || !option.Value.TryGetProperty(person, out var over))
                continue;
            look = look with
            {
                Model = Text(over, "model") ?? look.Model,
                Hand = Text(over, "hand") ?? look.Hand,
                OffHand = Text(over, "off_hand") ?? look.OffHand,
                Grip = Grip(over, "grip") ?? look.Grip,
                OffGrip = Grip(over, "off_grip") ?? look.OffGrip,
                Outfit = List(over, "outfit") ?? look.Outfit,
                EquipmentOutfits = over.TryGetProperty("equipment_outfits", out _) ? EquipmentOutfits(over) : look.EquipmentOutfits,
            };
        }
        return look;
    }

    private static Dictionary<string, string[]> EquipmentOutfits(JsonElement e) =>
        e.TryGetProperty("equipment_outfits", out var outfits)
            ? outfits.EnumerateObject().ToDictionary(p => p.Name,
                p => p.Value.EnumerateArray().Select(v => v.GetString()!).ToArray(), StringComparer.Ordinal)
            : new Dictionary<string, string[]>(StringComparer.Ordinal);

    private static Placement Look(JsonElement e) => new(
        Text(e, "model"), Text(e, "fit") ?? "none", Text(e, "surface"), Text(e, "material"), Flag(e, "hidden"), Flag(e, "hide_ready"),
        Text(e, "beside"), e.TryGetProperty("offset", out var offset) ? Vector(offset) : default, Text(e, "sound"), Text(e, "spent"),
        e.TryGetProperty("beside_offset", out var besideOffset) ? Vector(besideOffset) : default,
        e.TryGetProperty("light", out var light) ? Light(light) : null,
        e.TryGetProperty("interior_light", out var lights)
            ? (lights.ValueKind == JsonValueKind.Array ? lights.EnumerateArray().Select(Light) : new[] { Light(lights) }).OfType<LightSpec>().ToList()
            : null,
        e.TryGetProperty("surface_tint", out var tint) ? Vector(tint) : null,
        e.TryGetProperty("glow", out var glow) ? Vector(glow) : null, Number(e, "glow_energy", 0), Number(e, "wind", 0),
        e.TryGetProperty("emitters", out var emitters) && emitters.ValueKind == JsonValueKind.Array
            ? emitters.EnumerateArray().Where(x => Text(x, "recipe") is not null && x.TryGetProperty("at", out _))
                .Select(x => (Text(x, "recipe")!, Vector(x.GetProperty("at")))).ToList()
            : null,
        Number(e, "yaw_spread", 0), Number(e, "late_s", 0), Text(e, "animation_flag"), Flag(e, "hide_set_mark"));

    private static bool Flag(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// A light: "at" [x, y, z] metres from what it lights, "colour" [r, g, b], "energy", "range" metres, "shadows",
    /// "size", and optionally "attenuation" (Godot's decay; defaults to <see cref="LightSpec.DefaultAttenuation"/>)
    /// and "daylight" (how much of it the sun takes away; defaults to 0, a light the sun does not touch); null when
    /// malformed.
    /// </summary>
    private static LightSpec? Light(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("at", out var at) || !IsVector(at))
        {
            GD.PushWarning($"UNNAMED art: a light binding without its \"at\" [x, y, z] is left out: {e.GetRawText()}");
            return null;
        }
        var colour = e.TryGetProperty("colour", out var c) && IsVector(c) ? Vector(c) : Vector3.One;
        float attenuation = Number(e, "attenuation", LightSpec.DefaultAttenuation);
        if (attenuation <= 0f || !float.IsFinite(attenuation))
        {
            // Decay 0 is a light that does not fall off with distance at all, which is never what a binding means;
            // it is a typo, and a silent one would light the whole cell. A refused value is reported, not obeyed.
            GD.PushWarning($"UNNAMED art: a light binding asks for attenuation {attenuation}, which is not a decay; using {LightSpec.DefaultAttenuation}");
            attenuation = LightSpec.DefaultAttenuation;
        }
        float daylight = Mathf.Clamp(Number(e, "daylight", 0f), 0f, 1f);
        return new LightSpec(Vector(at), new Color(colour.X, colour.Y, colour.Z), Number(e, "energy", 1f), Number(e, "range", 5f),
            Flag(e, "shadows"), Number(e, "size", 0f), Number(e, "fog_energy", 1f), attenuation, daylight, Text(e, "note"));
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

    private static string[]? List(JsonElement e, string name) =>
        e.TryGetProperty(name, out var list) ? list.EnumerateArray().Select(x => x.GetString()!).ToArray() : null;

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
