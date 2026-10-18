// Presentation selection only. Item ownership/equipment remains authoritative in the simulation.
namespace UNNAMED.Presentation.Art;

public readonly record struct WornOutfit(string? Chest = null, string? Head = null, string? Feet = null,
    string? Hands = null, string? Legs = null, string? Cloak = null)
{
    public string? LegacyVariant => Chest is not null && Head is not null ? $"{Chest}_cap" : Chest ?? Head;

    /// <summary>Permanent underlayers plus the actual worn items' modular garments; shared backing is loaded once.</summary>
    public IReadOnlyList<string> Garments(IReadOnlyList<string> permanent, IReadOnlyDictionary<string, string[]> equipment) =>
        permanent.Concat(new[] { Legs, Chest, Feet, Head, Hands, Cloak }.OfType<string>()
            .SelectMany(id => equipment.GetValueOrDefault(id) ?? Array.Empty<string>()))
            .Distinct(StringComparer.Ordinal).ToArray();
}
