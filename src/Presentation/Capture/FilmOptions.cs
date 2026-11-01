using System.Globalization;
using System.Numerics;
using UNNAMED.Domain.Character;

namespace UNNAMED.Presentation.Capture;

public static class FilmOptions
{
    public static CharacterSpec Character(string? csv, CharacterSpec fallback)
    {
        if (string.IsNullOrWhiteSpace(csv)) return fallback;
        var fields = csv.Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length != 7 || fields.Any(string.IsNullOrWhiteSpace)
            || !Enum.TryParse<Pronouns>(fields[3], out var pronouns) || !Enum.IsDefined(pronouns)
            || !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stature) || stature <= 0)
            throw new ArgumentException("OTHERREACH_MKT_CHARACTER requires Name,people,body,He/She/They,face,hair,stature_mm (seven fields).");
        return new CharacterSpec(fields[0], pronouns, fields[1], fields[2], null)
            { Appearance = new CharacterAppearance(stature, fields[4], fields[5], null, null, null) };
    }

    /// <summary>Choose once per shot; reuse that up axis so crossing a vertical threshold cannot snap the roll.</summary>
    public static Vector3 CameraUp(Vector3 direction, Vector3? chosen = null)
    {
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 1e-10f)
            throw new ArgumentException("Film eye and aim must be distinct finite points.");
        var forward = Vector3.Normalize(direction);
        var up = chosen ?? (MathF.Abs(forward.Y) > .99f ? -Vector3.UnitZ : Vector3.UnitY);
        if (!float.IsFinite(up.LengthSquared()) || up.LengthSquared() < 1e-10f
            || Vector3.Cross(forward, Vector3.Normalize(up)).LengthSquared() < 1e-8f)
            throw new ArgumentException("Film direction is parallel to its up axis; author a nonparallel camera.up vector for this shot.");
        return Vector3.Normalize(up);
    }
}
