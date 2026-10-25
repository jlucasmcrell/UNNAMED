// UNNAMED Persistence - one save folder per character (OD-14, accepted; PJ14:362)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using UNNAMED.Domain;

namespace UNNAMED.Persistence;

/// <summary>
/// What a character's folder says about whose saves it holds, written beside them so a list of characters needs no save decoded. It is a
/// label, never authority: the saves themselves say who the character is.
/// </summary>
public sealed record CharacterCard(
    [property: JsonPropertyName("character_id")] string CharacterId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("people")] string? People,
    [property: JsonPropertyName("call")] string? Call);

/// <summary>
/// Saves are kept per character (OD-14): <c>&lt;profile&gt;/characters/&lt;character instance ID&gt;/</c>, each folder a whole
/// <see cref="SaveStore"/> root with its own slots, backups and rotation. A new character never shares a slot with another, so starting
/// one puts no earlier save at risk. Saves written before folders existed stay where they are, at the profile root, and are listed as a
/// folder of their own; nothing is moved.
/// </summary>
public static class CharacterFolders
{
    public const string Directory = "characters";
    public const string CardFile = "character.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The folder that holds a character's saves.</summary>
    public static string PathOf(string profileRoot, EntityId character) =>
        character.Kind == EntityKind.Character
            ? Path.Combine(Path.GetFullPath(profileRoot), Directory, character.Value)
            : throw new ArgumentException($"Saves are kept per character, got {character}", nameof(character));

    /// <summary>The characters with a folder in the profile, by instance ID. Anything else under <c>characters/</c> is not a folder of saves.</summary>
    public static ImmutableArray<EntityId> List(string profileRoot)
    {
        string root = Path.Combine(Path.GetFullPath(profileRoot), Directory);
        if (!System.IO.Directory.Exists(root))
            return ImmutableArray<EntityId>.Empty;
        return System.IO.Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Select(name => EntityId.TryParse(name, out var id) && id.Kind == EntityKind.Character && id.Value == name ? id : null)
            .OfType<EntityId>()
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>The card a folder holds, or null when it has none or it cannot be read.</summary>
    public static CharacterCard? ReadCard(string folder)
    {
        try
        {
            return JsonSerializer.Deserialize<CharacterCard>(File.ReadAllBytes(Path.Combine(folder, CardFile)), Json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private const string DeletedPrefix = ".deleted-";

    /// <summary>
    /// Delete a character and every save in its folder (PJ10 §5.2). Atomic as the player sees it: the folder is first renamed out of the
    /// list in one move - from then on the character is gone, whatever happens - and only then removed. A removal cut short (a crash, a
    /// file held open) leaves a <c>.deleted-</c> folder that <see cref="SweepDeleted"/> finishes at the next boot; it is never listed, and
    /// never loaded. False when the character has no folder.
    /// </summary>
    public static bool Delete(string profileRoot, EntityId character)
    {
        string folder = PathOf(profileRoot, character);
        if (!System.IO.Directory.Exists(folder))
            return false;
        string gone = Path.Combine(Path.GetDirectoryName(folder)!, DeletedPrefix + character.Value + "-" + EntityId.NewId(EntityKind.WorldEvent).Ulid);
        System.IO.Directory.Move(folder, gone);
        TryRemove(gone);
        return true;
    }

    /// <summary>Finish deletions an earlier run began: every <c>.deleted-</c> folder goes. The folders it could not remove yet, it names.</summary>
    public static ImmutableArray<string> SweepDeleted(string profileRoot)
    {
        string root = Path.Combine(Path.GetFullPath(profileRoot), Directory);
        if (!System.IO.Directory.Exists(root))
            return ImmutableArray<string>.Empty;
        return System.IO.Directory.EnumerateDirectories(root, DeletedPrefix + "*").Where(gone => !TryRemove(gone)).ToImmutableArray();
    }

    private static bool TryRemove(string gone)
    {
        try
        {
            System.IO.Directory.Delete(gone, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;   // the next boot's sweep tries again; the character is already out of the list
        }
    }

    /// <summary>Write a folder's card: to a temporary file first, then moved over the old one, so a crash leaves one card or the other.</summary>
    public static void WriteCard(string folder, CharacterCard card)
    {
        System.IO.Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, CardFile);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(card, Json));
        File.Move(temporary, path, overwrite: true);
    }
}
