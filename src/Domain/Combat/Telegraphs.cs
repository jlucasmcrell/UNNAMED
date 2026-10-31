// UNNAMED Domain - the telegraph palettes a colour-blind player can choose (GEN-10): amber and orange in place of the red-orange warning
// No Godot references - pure C#

namespace UNNAMED.Domain.Combat;

/// <summary>Which telegraph colours are drawn (GEN-10): the authored ones, or an amber or orange set for eyes that lose a red-orange warning.</summary>
public enum TelegraphPalette
{
    Default,
    Amber,
    Orange,
}

/// <summary>What a telegraph is telling the player, which is what picks its colour.</summary>
public enum TelegraphRole
{
    /// <summary>A blow is coming at the character: the one thing worth crossing the screen to say.</summary>
    EnemyBlow,

    /// <summary>The character's own window is open - a parry waiting, a dodge still carrying them.</summary>
    YourWindow,

    /// <summary>The Other: violet in every palette, because it is a register rather than a warning (owner ruling).</summary>
    Other,
}

/// <summary>A colour as authored, so the palette is testable without a renderer.</summary>
public readonly record struct TelegraphColour(byte R, byte G, byte B)
{
    /// <summary>Whether two colours are the same ink.</summary>
    public bool Is(TelegraphColour other) => R == other.R && G == other.G && B == other.B;
}

/// <summary>
/// The telegraph colours, by palette and role (GEN-10). The amber and orange sets exist because the drawn warning is a red-orange, and a
/// red-green colour-blind player can lose it against the ember HUD. Both alternatives stay warm for the enemy's blow and shift the player's own
/// window to a blue the confusion preserves, so the two are never a hue apart in a way a dichromat cannot read.
///
/// <b>Violet is the Other's and nothing else's.</b> Every palette answers <see cref="TelegraphRole.Other"/> with the same violet - the colour
/// that stands against the Other's appearances - so no accessibility choice re-tints a register the world uses to mean one thing.
/// </summary>
public static class Telegraphs
{
    /// <summary>The violet the Other is drawn in, the whole interface's own strain violet, in every palette.</summary>
    public static readonly TelegraphColour Other = new(138, 97, 179);

    /// <summary>The colour a role's telegraph is drawn in, in the palette the player chose.</summary>
    public static TelegraphColour Of(TelegraphPalette palette, TelegraphRole role) => role switch
    {
        TelegraphRole.Other => Other,
        TelegraphRole.EnemyBlow => palette switch
        {
            TelegraphPalette.Amber => new(255, 176, 46),
            TelegraphPalette.Orange => new(255, 140, 26),
            _ => new(232, 76, 42),
        },
        TelegraphRole.YourWindow => palette switch
        {
            TelegraphPalette.Amber => new(127, 212, 255),
            TelegraphPalette.Orange => new(95, 200, 240),
            _ => new(110, 200, 122),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "no such telegraph role"),
    };

    /// <summary>Whether the palette is the authored one, which draws the Other's violet alongside its own two colours.</summary>
    public static bool IsAuthored(TelegraphPalette palette) => palette == TelegraphPalette.Default;
}
