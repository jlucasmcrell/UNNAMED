// Presentation selection only. Item ownership/equipment remains authoritative in the simulation.
namespace UNNAMED.Presentation.Art;

public readonly record struct WornOutfit(string? Chest = null, string? Head = null, string? Feet = null,
    string? Hands = null, string? Legs = null, string? Cloak = null)
{
    public string? LegacyVariant => Chest is not null && Head is not null ? $"{Chest}_cap" : Chest ?? Head;

    /// <summary>Permanent underlayers plus the actual worn items' modular garments; shared backing is loaded once.</summary>
    public IReadOnlyList<string> Garments(IReadOnlyList<string> permanent, IReadOnlyDictionary<string, string[]> equipment)
    {
        // A layered torso/legs set is one authored mesh; individual head, foot,
        // hand and cloak pieces remain independent equipment regions.
        if (equipment.TryGetValue($"set:{Chest}|{Legs}", out var set))
            return set.Concat(new[] { Feet, Head, Hands, Cloak }.OfType<string>()
                .SelectMany(id => equipment.GetValueOrDefault(id) ?? Array.Empty<string>()))
                .Distinct(StringComparer.Ordinal).ToArray();
        return permanent.Concat(new[] { Legs, Chest, Feet, Head, Hands, Cloak }.OfType<string>()
            .SelectMany(id => equipment.GetValueOrDefault(id) ?? Array.Empty<string>()))
            .Distinct(StringComparer.Ordinal).ToArray();
    }
}
