using System.Text.Json;
namespace UNNAMED.Presentation.Ui.Models;
public sealed record NamedCharacterLook(string Native, string Mpfb, bool? Hood = null, string? Humgen = null);
public static class NamedCharacterCast
{
    public static IReadOnlyDictionary<string, NamedCharacterLook> Read(string text)
    {
        using var json = JsonDocument.Parse(text);
        var rows = json.RootElement.EnumerateObject().ToDictionary(p => p.Name,
            p => new NamedCharacterLook(p.Value.GetProperty("native").GetString()!, p.Value.GetProperty("mpfb").GetString()!, p.Value.TryGetProperty("hood", out var hood) ? hood.GetBoolean() : null,
                p.Value.TryGetProperty("humgen", out var humgen) ? humgen.GetString() : null), StringComparer.Ordinal);
        if (rows.Values.Select(r => r.Native).Distinct(StringComparer.Ordinal).Count() != rows.Count
            || rows.Values.Select(r => r.Mpfb).Distinct(StringComparer.Ordinal).Count() != rows.Count
            || rows.Values.Where(r => r.Humgen is not null).Select(r => r.Humgen).Distinct(StringComparer.Ordinal).Count() != rows.Values.Count(r => r.Humgen is not null))
            throw new FormatException("Named NPCs must have unique identities in each cast");
        return rows;
    }
}
