using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;

namespace UNNAMED.Domain.Items;

/// <summary>
/// D21's readable names (canon <c>GEAR_GENERATION.md</c> §8): assembled from authored word lists, never generated syllables, and never longer than four
/// words. <c>{Quality word?} {Maker adjective} {Material word?} {Item noun}</c>, where the noun comes from the part that dominates the piece.
/// </summary>
public static class GearNaming
{
    /// <summary>The quality word, only for fine and above (canon §8: <c>common</c> has none).</summary>
    public static string? QualityWord(int quality) => quality switch
    {
        >= Quality.Masterwork => "Masterwork",
        Quality.Fine => "Fine-wrought",
        _ => null,
    };

    /// <summary>The slots whose part names the piece: a sword is named for its blade, an axe for its head.</summary>
    public static readonly ImmutableArray<string> DominantSlots = ImmutableArray.Create("blade", "head", "face", "cap", "limbs");

    /// <summary>
    /// The name a build reads as. Deterministic: the same build always reads the same, and a build with no words for it falls back to the template's
    /// own name so no piece is ever nameless.
    /// </summary>
    public static string NameOf(
        ItemDefinition item,
        GearBuild build,
        IReadOnlyDictionary<string, PartDefinition> parts,
        IReadOnlyDictionary<string, MaterialDefinition> materials,
        MakerDefinition? maker)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(build);

        var words = new List<string>();
        if (QualityWord(build.PartTier) is { } qualityWord && build.Affixes.Length > 0)
            words.Add(qualityWord);

        if (maker is { Words.Length: > 0 })
            words.Add(maker.Words[Math.Abs(build.MakerId.Length + build.PartTier) % maker.Words.Length]);

        if (NotableMaterial(build, materials) is { } materialWord)
            words.Add(materialWord);

        words.Add(NounOf(item, build, parts));

        return string.Join(" ", words);
    }

    /// <summary>The item noun: the dominant part's own noun, or the template's name when its parts name nothing.</summary>
    public static string NounOf(ItemDefinition item, GearBuild build, IReadOnlyDictionary<string, PartDefinition> parts)
    {
        foreach (string slot in DominantSlots)
            if (build.Parts.TryGetValue(slot, out string? partId) && parts.TryGetValue(partId, out var part) && part.Noun is { } noun)
                return noun;
        return item.Name ?? item.Id[(item.Id.LastIndexOf('.') + 1)..].Replace('_', ' ');
    }

    /// <summary>
    /// The one material worth naming: the first notable one in the build, since canon names only what a buyer would ask about (steel, bronze), and a
    /// plain iron or ash piece goes unnamed.
    /// </summary>
    public static string? NotableMaterial(GearBuild build, IReadOnlyDictionary<string, MaterialDefinition> materials)
    {
        foreach (var (_, materialId) in build.Materials.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (materials.TryGetValue(materialId, out var material) && material.Word is { } word)
                return word;
        return null;
    }

    /// <summary>The tooltip's lines for a build: what it was made from, who made it, and the maker's own sentence about their work.</summary>
    public static ImmutableArray<string> TooltipLines(
        GearBuild build,
        IReadOnlyDictionary<string, PartDefinition> parts,
        IReadOnlyDictionary<string, MaterialDefinition> materials,
        MakerDefinition? maker)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        var made = build.Parts
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => parts.TryGetValue(kv.Value, out var part) ? $"{kv.Key}: {part.Noun ?? kv.Value[(kv.Value.LastIndexOf('.') + 1)..].Replace('_', ' ')}" : kv.Key)
            .ToList();
        if (made.Count > 0)
            lines.Add("Assembled from " + string.Join(", ", made));
        if (NotableMaterial(build, materials) is { } materialWord)
            lines.Add($"Finished in {materialWord.ToLowerInvariant()}");
        if (maker is not null)
        {
            string workshop = maker.Words.Length > 0 ? maker.Words[0] : maker.Id[(maker.Id.IndexOf('.') + 1)..].Replace('_', ' ');
            lines.Add(maker.PeopleId is { } people
                ? $"Made by {workshop} of {people[(people.IndexOf('.') + 1)..]}"
                : $"Made by {workshop}");
            lines.Add(maker.Signature.Tendency.Replace('_', ' '));
        }
        return lines.ToImmutable();
    }
}
