// UNNAMED Domain - stealth as the player's own competence, and the assassin's two instruments (STEALTH_DETECTION_AND_THREAT.md
// §12, §13, §14, §20; PROGRESSION.md §6; owner findings 26 and 27)
// No Godot references - pure C#

using UNNAMED.Domain.Creatures;

namespace UNNAMED.Domain.Combat;

/// <summary>
/// The stealth skill's effect on information, and nothing else (§20: stealth works by preventing or manipulating information, never by a
/// hidden meter). Its ranks do not add damage: they make less noise and leave less for a creature to notice.
/// </summary>
public static class StealthRules
{
    /// <summary>Levels at which a rank of stealth is reached: 1, 4, 8 (PROGRESSION.md's common bands).</summary>
    public static int Rank(int level) => level >= 8 ? 3 : level >= 4 ? 2 : level >= 1 ? 1 : 0;

    /// <summary>
    /// How much of a sound the character still makes, by rank: −15 %, −30 %, −45 % of the effective radius of every footfall and swing
    /// (<c>noise_m</c>). A rank-3 walk is quieter than everyone else's crouch-and-freeze.
    /// </summary>
    public static double NoiseFactor(int level) => 1 - 0.15 * Rank(level);

    /// <summary>How fast awareness rises when the character is seen, by rank: −10 % per rank. It never reaches zero.</summary>
    public static double AwarenessFactor(int level) => 1 - 0.10 * Rank(level);

    /// <summary>Whether a creature has not noticed the character yet: the doc's threshold, not a flag (§3).</summary>
    public static bool Unaware(int awareness, AwarenessRules rules) => awareness < rules.Suspicious;

    /// <summary>
    /// The multiplier a blow gets when its target has not noticed the attacker (§12): the weapon's own sneak multiplier - the dagger
    /// class's reason to exist - and nothing else. Applied before armour, so armour still decides (§13).
    /// </summary>
    public static double SneakMultiplier(AttackProfile attack, bool unaware) => unaware ? Math.Max(1.0, attack.SneakMultiplier) : 1.0;

    /// <summary>Whether a weapon is of the dagger class, which is what a takedown requires.</summary>
    public static bool IsDagger(AttackProfile attack) => attack.SneakMultiplier > 1.0 && !attack.Ranged;
}

/// <summary>The outcome of a takedown attempt: a body is killed outright, or it is not and now knows about the knife.</summary>
public sealed record TakedownResult(HitResult Hit, BodyRegion Region, bool Killed);

/// <summary>
/// A qualifying rear dagger ambush executes an unaware non-boss at Even threat or below (owner RC d5 ruling).
/// Other attempts retain the armour/vital result. Both resolve through the one combat resolver.
/// </summary>
public static class TakedownRules
{
    // "Even" uses the same threat band as the plate: the reviewed level-2 wolf vs level-1 Ada is Even.
    public static bool Executes(bool dagger, bool unaware, bool fromBehind, Threat threat, bool boss) =>
        dagger && unaware && fromBehind && threat <= Threat.Even && !boss;
    /// <summary>The vital a takedown aims at: the head from behind, the torso from inside reach (COMBAT §19's weak points).</summary>
    public static BodyRegion Vital(bool fromBehind) => fromBehind ? BodyRegion.Head : BodyRegion.Torso;

    /// <summary>
    /// Resolve the attempt. <paramref name="fromBehind"/> chooses the vital (the head from behind, the torso from inside reach), and
    /// <paramref name="health"/> is the target's remaining health, so a body that survives the vital is left standing with the wound and turns
    /// on the attacker.
    /// </summary>
    public static TakedownResult Resolve(Strike strike, DefenseProfile defense, CombatConstants constants, int health, bool fromBehind,
        Func<uint, double> random)
    {
        var region = Vital(fromBehind);
        var hit = CombatRules.Resolve(strike with { ForcedRegion = region }, defense, constants, random);
        return new TakedownResult(hit, region, hit.Final > 0 && hit.Final >= health);
    }
}
