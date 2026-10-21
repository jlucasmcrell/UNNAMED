// UNNAMED Presentation - what the top-centre plate says about the creature in front of the character (owner finding 24)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain.Creatures;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// One creature as the plate reads it: what it is, how hard it is, what it will do, and whether the fight is on. This is the whole answer
/// to finding 24 ("I cannot tell whether the Barrowback can or should be fought"), and the words are the domain's own
/// (<see cref="ThreatRules"/> / <see cref="TemperamentRules"/>) rather than a second vocabulary invented by the interface.
/// </summary>
public readonly record struct TargetPlate(string Name, int Level, int Health, int MaxHealth, Threat Threat, Temperament Temperament,
    bool Engaged, bool Huntable, string Status = "")
{
    /// <summary>The line under the name: level, the threat word, what it will do, and what is in it for the character.</summary>
    public string Meta
    {
        get
        {
            string mood = TemperamentRules.Word(Temperament);
            string extra = Huntable ? "can be hunted" : "nothing to take from it";
            return $"Level {Level} · {ThreatRules.Word(Threat)} · {mood} · {extra}";
        }
    }

    /// <summary>
    /// The health bar belongs to a fight, not to an introduction: it is shown once the creature is fighting the character, and also once it
    /// has been hit - from a bow at range it may not have noticed yet, and a wound the player cannot see the size of is the same complaint
    /// finding 24 made about full bars.
    /// </summary>
    public bool ShowBar => Engaged || Health < MaxHealth;
}

/// <summary>
/// The nameplate's colours, read off the threat band: grey for a fight with nothing in it, green when the character has the edge, brass
/// at even, orange when it has the worse of it, wound red when it is a bad idea, pale when it is not a fight at all. The rest of the plate
/// keeps the house tones (<see cref="HudStyle"/>).
/// </summary>
public static class TargetTone
{
    public static Color Of(Threat threat) => threat switch
    {
        Threat.Trivial => HudStyle.IronBright,
        Threat.Easy => new Color(0.56f, 0.74f, 0.5f),
        Threat.Even => HudStyle.Brass,
        Threat.Hard => new Color(0.9f, 0.58f, 0.28f),
        Threat.Deadly => HudStyle.HealthFill,
        _ => new Color(0.84f, 0.8f, 0.88f),
    };
}
