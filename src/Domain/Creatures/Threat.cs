// UNNAMED Domain - what a player can read about a creature before deciding whether to fight it (owner finding 24)
// No Godot references - pure C#

namespace UNNAMED.Domain.Creatures;

/// <summary>
/// How dangerous a creature is to this character, from the gap between its level and the character's.
/// <para>
/// The game already treats that gap as the measure of a fight: <c>guards.level_band</c> in config/progression.yaml scales what a kill is
/// worth by <c>creature_level - player_level</c> (AG-1). This is the same gap read the other way round, for the player instead of for the
/// reward, because the owner's complaint was that he could not tell whether a Barrowback can or should be fought (finding 24).
/// </para>
/// <para>
/// The bands are deliberately narrower than AG-1's: AG-1's rows are sixteen levels wide at the bottom, which is right for a reward
/// multiplier and useless as a readout on content that spans levels 1 to 5. These cut where this content actually differs - a grey wolf
/// (2) beside a level-1 character is Even, a Barrowback (5) is Deadly, and that is the question the player is asking.
/// </para>
/// </summary>
public enum Threat
{
    /// <summary>Four or more levels below: no fight in it.</summary>
    Trivial,
    Easy,
    Even,
    Hard,
    Deadly,
    /// <summary>Seven or more levels above: leave it alone.</summary>
    FarAbove,
}

/// <summary>What a creature does when it notices the player, in the words a nameplate shows. (Named Temperament because Domain.Companions already owns a Disposition.)</summary>
public enum Temperament
{
    /// <summary>It never starts a fight (wildlife, the great grazers).</summary>
    Passive,

    /// <summary>It leaves you alone until it is struck, and then its own kind answer (a herd defending as one).</summary>
    Neutral,

    /// <summary>It will start the fight, whether or not it has noticed you yet.</summary>
    Hostile,
}

public static class ThreatRules
{
    /// <summary>How this creature reads against a character of <paramref name="playerLevel"/>.</summary>
    public static Threat Of(int creatureLevel, int playerLevel)
    {
        int gap = creatureLevel - playerLevel;
        return gap switch
        {
            <= -4 => Threat.Trivial,
            <= -2 => Threat.Easy,
            <= 1 => Threat.Even,
            <= 3 => Threat.Hard,
            <= 6 => Threat.Deadly,
            _ => Threat.FarAbove,
        };
    }

    /// <summary>The word shown beside the creature's name.</summary>
    public static string Word(Threat threat) => threat switch
    {
        Threat.Trivial => "Trivial",
        Threat.Easy => "Easy",
        Threat.Even => "Even",
        Threat.Hard => "Hard",
        Threat.Deadly => "Deadly",
        _ => "Far above",
    };
}

public static class TemperamentRules
{
    /// <summary>
    /// What it does when it sees the player. <paramref name="engaged"/> is the runtime's own state (a creature fighting the character
    /// now); the other two are the role's, so a herd that answers its kind's call reads as Neutral and not as Passive.
    /// </summary>
    public static Temperament Of(bool peaceful, bool answersCalls, bool engaged)
    {
        if (engaged)
            return Temperament.Hostile;
        if (!peaceful)
            return Temperament.Hostile;
        return answersCalls ? Temperament.Neutral : Temperament.Passive;
    }

    /// <summary>The word shown beside the name: passive and neutral say why, because that is the useful part.</summary>
    public static string Word(Temperament temperament) => temperament switch
    {
        Temperament.Passive => "Passive",
        Temperament.Neutral => "Neutral",
        _ => "Hostile",
    };
}
