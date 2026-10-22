// UNNAMED Domain - pets: the animal tamed out of the wild or raised from a pup, the skills it learns by use, the orders it
// takes, and the party it joins (spec 43 and its owner rulings of 2026-10-21; docs/PETS_ANIMALS_AND_ECOLOGY.md)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Pets;

/// <summary>
/// What a pet is for (spec 43 ruling 3: a pet has its own skills, not a generic combat bonus). The role says which of its
/// skills it is kept for; the hound is a fighter that a patient handler can also use to draw a creature off.
/// </summary>
public enum PetRole
{
    Fighter,
    Support,
    Control,
    Scout,
}

/// <summary>
/// The orders a pet takes (spec 43 ruling 4: the tactical commands are the design goal, not a convenience). Four of them name
/// a place on the ground - the wait point, the guard post, where to look ahead from, and where the lure leads - and the other
/// two name nothing but a mark: follow the character, or take the one they struck.
/// </summary>
public enum PetOrder
{
    Follow,
    WaitHere,
    Guard,
    Scout,
    Pull,
    AttackTarget,
    LureToHere,
}

/// <summary>
/// A pet on its feet, or badly wounded (owner ruling 1: a pet at no health is <em>not</em> dead - it goes home, mends there,
/// and is collected again).
/// </summary>
public enum PetCondition
{
    Up,
    Wounded,
}

/// <summary>The two skills this slice gives a pet (spec 43's first slice: a fighting bite and a control lure).</summary>
public enum PetSkillKind
{
    Bite,
    Lure,
}

public static class PetKeys
{
    public static string Role(PetRole role) => role switch
    {
        PetRole.Fighter => "fighter",
        PetRole.Support => "support",
        PetRole.Control => "control",
        PetRole.Scout => "scout",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown pet role"),
    };

    public static PetRole? ParseRole(string key) => key switch
    {
        "fighter" => PetRole.Fighter,
        "support" => PetRole.Support,
        "control" => PetRole.Control,
        "scout" => PetRole.Scout,
        _ => null,
    };

    public static string Order(PetOrder order) => order switch
    {
        PetOrder.Follow => "follow",
        PetOrder.WaitHere => "wait_here",
        PetOrder.Guard => "guard",
        PetOrder.Scout => "scout",
        PetOrder.Pull => "pull",
        PetOrder.AttackTarget => "attack_target",
        PetOrder.LureToHere => "lure_to_here",
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown pet order"),
    };

    public static PetOrder? ParseOrder(string key) => key switch
    {
        "follow" => PetOrder.Follow,
        "wait_here" => PetOrder.WaitHere,
        "guard" => PetOrder.Guard,
        "scout" => PetOrder.Scout,
        "pull" => PetOrder.Pull,
        "attack_target" => PetOrder.AttackTarget,
        "lure_to_here" => PetOrder.LureToHere,
        _ => null,
    };

    public static string Condition(PetCondition condition) => condition switch
    {
        PetCondition.Up => "up",
        PetCondition.Wounded => "wounded",
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown pet condition"),
    };

    public static PetCondition? ParseCondition(string key) => key switch
    {
        "up" => PetCondition.Up,
        "wounded" => PetCondition.Wounded,
        _ => null,
    };

    public static string Skill(PetSkillKind skill) => skill switch
    {
        PetSkillKind.Bite => "bite",
        PetSkillKind.Lure => "lure",
        _ => throw new ArgumentOutOfRangeException(nameof(skill), skill, "Unknown pet skill"),
    };

    public static PetSkillKind? ParseSkill(string key) => key switch
    {
        "bite" => PetSkillKind.Bite,
        "lure" => PetSkillKind.Lure,
        _ => null,
    };
}

/// <summary>
/// A pet's own skill, which levels with use (ruling 3: pets level their own abilities, not a generic combat stat). A use is one
/// bite that landed or one lure that was taken; the levels' thresholds are content (<c>config.pets</c>).
/// </summary>
public sealed record PetSkill(PetSkillKind Kind, int Level, int Uses)
{
    /// <summary>The lowest level a skill has: it knows the trick or it does not, and this slice gives it to the ones that do.</summary>
    public const int FirstLevel = 1;

    /// <summary>The highest level its content thresholds describe - a pet's skill is its own, not the character's ladder.</summary>
    public const int MaxLevel = 4;

    public bool IsValid => Enum.IsDefined(Kind) && Level >= FirstLevel && Level <= MaxLevel && Uses >= 0;

    public override string ToString() => $"{PetKeys.Skill(Kind)} {Level} ({Uses})";
}

/// <summary>What one use of a skill is worth: the skill as it grows, and the level it reached if this use was the one.</summary>
public sealed record PetSkillPractice(PetSkill Skill, bool Levelled);

public static class PetSkills
{
    /// <summary>
    /// One use, against content's thresholds (<see cref="PetSkillRules.LevelUses"/>): the third use takes a skill to 2, the
    /// eighth to 3, the fifteenth to 4 - and a use past the last threshold stops counting, so the record cannot grow without bound.
    /// </summary>
    public static PetSkillPractice Practice(PetSkill skill, PetSkillRules rules)
    {
        int uses = skill.Uses + 1;
        int level = skill.Level;
        foreach (int threshold in rules.LevelUses)
        {
            if (uses >= threshold && level < PetSkill.MaxLevel)
                level++;
        }
        bool levelled = level != skill.Level;
        // Past the last threshold there is nothing left to learn: the count stays where the ladder ended.
        bool counted = uses <= rules.LevelUses.LastOrDefault(0);
        return new PetSkillPractice(skill with { Level = level, Uses = counted ? uses : skill.Uses }, levelled);
    }

    /// <summary>How much a pet's bite has learnt: a tenth more of its own blow per level past the first.</summary>
    public static double DamageMultiplier(PetSkill? bite) => bite is null ? 1.0 : 1 + 0.10 * (bite.Level - PetSkill.FirstLevel);

    /// <summary>The skills a pet of this species has: exactly the ones its content names, at their first level.</summary>
    public static ImmutableArray<PetSkill> Start(ImmutableArray<PetSkillKind> kinds) =>
        kinds.Select(k => new PetSkill(k, PetSkill.FirstLevel, 0)).ToImmutableArray();

    public static PetSkill? Of(ImmutableArray<PetSkill> skills, PetSkillKind kind) => skills.FirstOrDefault(s => s.Kind == kind);
}

/// <summary>
/// How taming works (spec 43's <c>Odds</c>): temperament, the Handling skill, the food offered, the creature's health, and whether
/// it ever noticed the character. Every number is content (<c>config.pets</c>), because the odds are the thing playtests tune.
/// </summary>
public sealed record PetTamingRules(
    int BasePercent,
    int PerHandlingLevel,
    int UnnoticedPercent,
    int WoundedPercent,
    int ContentPercent,
    int MaxPercent,
    int ChannelTicks,
    ImmutableSortedDictionary<string, int> TemperamentPercent)
{
    public string? Problem()
    {
        if (BasePercent < 1 || BasePercent > 100)
            return "base_percent is 1 to 100";
        if (PerHandlingLevel < 0 || MaxPercent is < 1 or > 100)
            return "per_handling_level is not negative and max_percent is 1 to 100";
        if (ChannelTicks < 1)
            return "channel_ticks is at least 1";
        return null;
    }
}

public static class PetTaming
{
    /// <summary>
    /// The chance a tamed attempt takes, in percent, before the roll: the base, the Handling skill's worth, what the approach and
    /// the offer add - it never saw the character, the food was the best there is, it is wounded and therefore slower to run - less
    /// what the creature's own level costs. Clamped to <see cref="PetTamingRules.MaxPercent"/> and at least a percent: a wild animal
    /// can always be reached by a patient handler with the right food.
    /// </summary>
    public static int Chance(PetTamingRules rules, int handlingLevel, int foodQuality, int creatureLevel, int playerLevel, int health,
        int maxHealth, bool unnoticed, string temperament)
    {
        int chance = rules.BasePercent + rules.PerHandlingLevel * Math.Max(0, handlingLevel) + foodQuality;
        if (unnoticed)
            chance += rules.UnnoticedPercent;
        if (maxHealth > 0 && health * 2 < maxHealth)
            chance += rules.WoundedPercent;
        if (creatureLevel > playerLevel)
            chance += rules.ContentPercent * (creatureLevel - playerLevel);
        chance += rules.TemperamentPercent.GetValueOrDefault(temperament, 0);
        return Math.Clamp(chance, 1, rules.MaxPercent);
    }
}

/// <summary>
/// The lure (spec 43's control role): a pet draws one creature off to a place the handler chose - an ambush, a corner away from
/// the rest of the pack. It is a working, not a blow: the creature is led, and a strong-willed one may not go.
/// </summary>
public sealed record PetLureRules(int RadiusMm, int TravelMm, int ResistPerLevel, int MinPercent, int ImmunityTicks)
{
    public string? Problem()
    {
        if (RadiusMm < 1 || TravelMm < 1 || ImmunityTicks < 0)
            return "lure radius_m and travel_m are positive and immunity_s is not negative";
        if (MinPercent is < 1 or > 100)
            return "min_percent is 1 to 100";
        return null;
    }
}

public static class PetLure
{
    /// <summary>
    /// The chance, in percent, that a creature follows the pet's lure to the point: the best a lure can do less
    /// <see cref="PetLureRules.ResistPerLevel"/> per level the creature has, and nothing below <see cref="PetLureRules.MinPercent"/> -
    /// a lure is never certain, and never hopeless.
    /// </summary>
    public static int Chance(PetLureRules rules, PetSkill? lure, int creatureLevel) =>
        Math.Clamp(100 - rules.ResistPerLevel * Math.Max(0, creatureLevel) + 10 * ((lure?.Level ?? PetSkill.FirstLevel) - PetSkill.FirstLevel),
            rules.MinPercent, 100);

    /// <summary>How far a creature of this level is led: the pet's travel, less a step for every level over the handler's own.</summary>
    public static int Travel(PetLureRules rules, int creatureLevel) => Math.Max(5_000, rules.TravelMm - 1_000 * Math.Max(0, creatureLevel - 2));
}

/// <summary>
/// A pet at no health goes home badly wounded (owner ruling 1): home for content's days, mending there, and then collected again.
/// It is not a death and never removes the pet - the record stays, its condition changes.
/// </summary>
public sealed record PetRetireRules(int WoundDaysMin, int WoundDaysMax, int RegenPerSecond, int RegenDelayTicks)
{
    public string? Problem() =>
        WoundDaysMin < 1 || WoundDaysMax < WoundDaysMin ? "wound_days are at least 1 and min <= max" : null;
}

public static class PetWounds
{
    /// <summary>How many days the pet is away, from the world's own random channel so the same save mends the same way.</summary>
    public static int Days(PetRetireRules rules, Func<uint, double> random) =>
        rules.WoundDaysMin + Math.Min(rules.WoundDaysMax - rules.WoundDaysMin,
            (int)Math.Floor(random(0) * (rules.WoundDaysMax - rules.WoundDaysMin + 1)));

    public static long RecoversAt(long tick, int days, long ticksPerDay) => tick + Math.Max(1, days) * ticksPerDay;

    /// <summary>How long until the pet is home and mended, in world ticks, for the HUD and the journal to say.</summary>
    public static bool Home(long tick, long recoversAtTick) => tick >= recoversAtTick;
}

/// <summary>
/// The party cap (owner ruling 2): several pets AND companions, any mix, up to <c>config.party</c>'s <c>max_members</c>. A pet and
/// a hireling take the same slot, which is the whole point of one number.
/// </summary>
public static class PetParty
{
    /// <summary>Whether one more may join: the companions and pets already with the character, against the cap.</summary>
    public static bool Room(int companions, int pets, int max) => companions + pets < max;

    /// <summary>Why not, in the party's own words - the same wording the party uses for a companion.</summary>
    public static string? Refusal(int companions, int pets, int max) =>
        Room(companions, pets, max) ? null : $"your party is full ({max} with you)";
}

/// <summary>
/// Encounter scaling (owner ruling 5: the game must stay challenging with a full party, and a good player wins by strategy, not
/// by numbers). A party is worth points - one per follower, and a quarter for each level it has - and what a spawner places
/// grows by content's percent per point, up to its cap. What it buys: more health in the same body, and a level or two, which the
/// nameplate shows and the reward honours. The count is left as authored, so the fight is harder rather than longer.
/// </summary>
public sealed record EncounterRules(int PercentPerPoint, int MaxPercent, double LevelPerPercent)
{
    public string? Problem() =>
        PercentPerPoint < 0 || MaxPercent < 100 ? "percent_per_point is not negative and max_percent is at least 100" : null;
}

/// <summary>What a scaled encounter is: its strength over the authored one, in percent, and the levels that buys.</summary>
public sealed record EncounterScale(int Percent, int Levels)
{
    /// <summary>An encounter at the authored numbers: the solo character's.</summary>
    public static readonly EncounterScale Authored = new(100, 0);

    public double HealthFactor => Percent / 100.0;

    public double DamageFactor => Percent / 100.0;
}

public static class Encounters
{
    /// <summary>
    /// A party's worth in points: the character alone is zero, and each pet or companion adds one plus a quarter for every level
    /// past the first. Deliberately linear - a bigger party means a harder fight, never an unlosable one.
    /// </summary>
    public static double Points(int followers, int otherLevels) => followers + 0.25 * Math.Max(0, otherLevels);

    /// <summary>The scale a party of this worth meets: the authored numbers at zero points, content's percent per point after.</summary>
    public static EncounterScale Of(double points, EncounterRules rules)
    {
        int percent = Math.Min(rules.MaxPercent, 100 + (int)Math.Round(rules.PercentPerPoint * Math.Max(0, points)));
        int levels = (int)Math.Floor((percent - 100) * rules.LevelPerPercent);
        return new EncounterScale(percent, Math.Max(0, levels));
    }
}

/// <summary>
/// The pet rules (<c>config.pets</c>): what taming costs and how likely it is, how its own skills grow, how far a lure reaches,
/// how long a wounded pet stays home, and what a party does to the encounters it meets. Provisional, every number of it.
/// </summary>
public sealed record PetRules(PetTamingRules Tame, PetSkillRules Skills, PetLureRules Lure, PetRetireRules Retire, EncounterRules Encounter)
{
    public string? Problem() => Tame.Problem() ?? Lure.Problem() ?? Retire.Problem() ?? Encounter.Problem()
        ?? (Skills.LevelUses.IsEmpty ? "skill level_uses is at least one threshold" : null);
}

/// <summary>The thresholds at which a pet's own skill reaches its next level.</summary>
public sealed record PetSkillRules(ImmutableArray<int> LevelUses)
{
    public static readonly PetSkillRules Default = new(ImmutableArray.Create(3, 8, 15));
}

/// <summary>
/// One species as the world plays it (DATA_MODEL.md's creature, with the pet half its content names): the creature it is a body of,
/// the role it fills, the skills it has, what it eats, and how a pup becomes the grown animal.
/// </summary>
public sealed record PetSpecies(
    string CreatureId,
    string Name,
    PetRole Role,
    ImmutableArray<PetSkillKind> Skills,
    ImmutableSortedDictionary<string, int> Foods,
    string Temperament,
    string? GrowsInto,
    int GrowDays,
    string HomeLocation)
{
    /// <summary>Whether the species eats this item at all - a wolf takes meat, and refuses roots however good they are.</summary>
    public bool Eats(string itemId) => Foods.ContainsKey(itemId);

    /// <summary>What one offer of this food is worth in the taming odds.</summary>
    public int Quality(string itemId) => Foods.GetValueOrDefault(itemId, 0);

    /// <summary>A young animal that becomes the grown one after its content's days: the pup raised at the settlement.</summary>
    public bool Young => GrowsInto is not null && GrowDays > 0;
}

/// <summary>What a pack of pets holds: the rules, and the species a creature id tames into.</summary>
public sealed record PetSetup(PetRules Rules, ImmutableSortedDictionary<string, PetSpecies> Species)
{
    public static readonly PetSetup None = new(
        new PetRules(new PetTamingRules(1, 0, 0, 0, 0, 100, 1, ImmutableSortedDictionary<string, int>.Empty),
            PetSkillRules.Default, new PetLureRules(1, 1, 0, 1, 0), new PetRetireRules(1, 1, 0, 0), new EncounterRules(0, 100, 0)),
        ImmutableSortedDictionary.Create<string, PetSpecies>(StringComparer.Ordinal));

    /// <summary>The pet half of a creature, or null when that creature cannot be tamed.</summary>
    public PetSpecies? Of(string creatureId) => Species.GetValueOrDefault(creatureId);
}

/// <summary>
/// A bond's numbers (owner ruling 1: a tamed animal "starts wary, obeys less"; a bond rises with feeding, care and time together). Above
/// <see cref="Sure"/> it does as it is told; below it, a hard order is a roll that a wary animal can lose.
/// </summary>
public static class PetBond
{
    /// <summary>Where a wild animal starts: it does not fight for a stranger.</summary>
    public const int Tamed = 20;

    /// <summary>A bond earned by feeding and days on the road: every order is obeyed.</summary>
    public const int Sure = 60;

    public const int Max = 100;

    /// <summary>The percent chance a pet of this bond takes a hard order: it starts wary, and ends certain.</summary>
    public static int Obey(int bond, PetOrder order)
    {
        int floor = order is PetOrder.WaitHere ? 40 : 25;
        return Math.Clamp(floor + (bond - Tamed) * (100 - floor) / Math.Max(1, Sure - Tamed), floor, 100);
    }
}
