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

/// <summary>
/// A placed strike: a blow or a shot aimed at a vital, rather than swung at whatever is nearest. COMBAT §19 (if an
/// appropriate weapon reaches an unprotected critical structure the result can be lethal regardless of level) and §32
/// (level never grants categorical immunity); STEALTH's unaware states decide when a strike can be placed at all. The
/// dagger takedown (<see cref="TakedownRules"/>) is the same idea at knife range - this is what the other classes have.
/// </summary>
public static class PlacedStrikeRules
{
    /// <summary>A body must be within a knife's reach to have its neck opened.</summary>
    public const long NeckMm = 700;

    /// <summary>A fist or a boot can reach the torso but never the head: the canon's vitals need a weapon that gets there.</summary>
    public const long TorsoMm = 500;

    /// <summary>A vital struck on a body that has noticed you is a grave wound, not a kill: the canon's x3.</summary>
    public const double AwareVitalMultiplier = 3.0;

    /// <summary>The vitals this game's three body regions can express: the head (brain, neck vessel) and the torso (heart).</summary>
    public static bool IsVital(BodyRegion region) => region is BodyRegion.Head or BodyRegion.Torso;

    /// <summary>Whether the weapon can actually reach that vital: a bow reaches a head across a field, a knife needs the neck, a fist reaches nothing vital.</summary>
    public static bool Reaches(AttackProfile attack, BodyRegion region) => region switch
    {
        BodyRegion.Head => attack.Ranged || attack.ReachMm >= NeckMm,
        BodyRegion.Torso => attack.Ranged || attack.ReachMm >= TorsoMm,
        _ => false,
    };

    /// <summary>
    /// A placed strike kills outright when the body has not noticed the striker, the vital is bare (armour on that region
    /// stops it - COMBAT's armour layers), the weapon reaches, and it is not a keeper (a boss is never taken by surprise,
    /// the same ruling <see cref="TakedownRules.Executes"/> encodes for the dagger).
    /// </summary>
    public static bool Lethal(AttackProfile attack, bool unaware, BodyRegion region, bool armouredRegion, bool boss) =>
        unaware && !boss && !armouredRegion && IsVital(region) && Reaches(attack, region);

    /// <summary>
    /// Where a region stops being vulnerable: fur and hide (a wolf's 1-2) are not protection against a placed strike into a
    /// brain or a heart, while real armour (a boar's bristles at 4, mail) is. The canon's clause is an *unprotected*
    /// critical structure, so the line sits between hide and armour rather than at zero.
    /// </summary>
    public const int ProtectedFrom = 3;

    /// <summary>Whether armour of that value on the aimed region is enough to stop a placed strike.</summary>
    public static bool Protects(int armorAtRegion) => armorAtRegion >= ProtectedFrom;

    /// <summary>The damage multiplier a placed strike earns: the x3 only on a vital, and only once the body is aware.</summary>
    public static double Multiplier(bool unaware, BodyRegion region) =>
        unaware || !IsVital(region) ? 1.0 : AwareVitalMultiplier;
}
