// UNNAMED Domain - the identity of a world space (WORLD_ARCHITECTURE.md §8; M8 D10)
// No Godot references - pure C#

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// The identity of a space a coordinate belongs to. The exterior world is the default space; an interior
/// is its own, so that two equal local coordinates in different spaces can never collide.
/// </summary>
/// <remarks>
/// <para>
/// The M8 design is explicit that this is not a portal-scene concern: combat target membership,
/// projectiles, perception, inventory reach, stations, discoveries, companion orders, baseline identity
/// and serialization all derive a cell or a position, and an interior whose local origin sits inside the
/// exterior region grid would otherwise share that cell - and its deltas, its baseline hash and its
/// random stream - with real ground.
/// </para>
/// <para>
/// It lives in Domain rather than beside <c>CellKey</c> in World because authored content names a space:
/// a door, a barrier, a switch and a named location all carry one. Domain is the innermost layer that
/// World and Content can both see, and Domain must not reference World.
/// </para>
/// <para>
/// The default is the exterior and its name is empty, so every existing key, save file and parse is
/// unchanged.
/// </para>
/// </remarks>
public readonly record struct SpaceId
{
    /// <summary>The exterior world. The default, and the only space that existed before M8.</summary>
    public static readonly SpaceId Exterior = default;

    public SpaceId(string name) => Name = name ?? "";

    /// <summary>Canonical name, e.g. <c>space.barrowworks</c>; empty for the exterior.</summary>
    /// <remarks><c>default(SpaceId)</c> has a null Name because a struct's default runs no constructor, so
    /// every reader treats null as the empty exterior name rather than dereferencing it.</remarks>
    public string Name { get; }

    public bool IsExterior => string.IsNullOrEmpty(Name);

    // A parsed empty save value and default(SpaceId) name the same exterior.
    public bool Equals(SpaceId other) => string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

    /// <summary>Rejects a name that cannot round-trip through a save key, rather than writing a broken one.</summary>
    public static SpaceId Parse(string value)
    {
        if (value.Length != 0 && (value.Contains('/') || value.Contains(':') || value.Any(char.IsWhiteSpace)))
            throw new FormatException($"Not a space name: '{value}'");
        return new SpaceId(value);
    }

    /// <summary>The name, or the empty exterior name when this is <c>default</c>.</summary>
    public override string ToString() => Name ?? "";
}
