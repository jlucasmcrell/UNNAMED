// UNNAMED Content - magic from content: the formulas, the tuning of casting, and what books teach
// (DATA_MODEL.md §4.1 use.grants, §4.6, §4.19; PROGRESSION.md §7; M3e)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Magic;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the simulation's <see cref="MagicSetup"/> and lints what the reference and semantic passes cannot see (MAG001):
/// every formula works a magic-domain skill, costs Focus and Strain and nothing else (there is no mana), and uses only the
/// targeting and payload Phase 1 builds; <c>config.magic</c> is in range; a book's grants are formulas, and it is read once.
/// </summary>
public static class MagicContent
{
    private static readonly string[] SelfPayloads = { "apply_effect", "remove_effect" };

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        if (!loader.Definitions.ContainsKey("config.time"))
            return errors;
        int tickMs;
        try
        {
            tickMs = WorldContent.TickMilliseconds(loader);
        }
        catch (Exception e) when (e is FormatException or KeyNotFoundException)
        {
            return errors;   // WLD008 reports a broken config.time
        }
        var constants = new MagicConstants();
        Try(() => constants = BuildConstants(loader, tickMs), "config.magic", errors);
        var formulas = ImmutableSortedDictionary.Create<string, FormulaDefinition>(StringComparer.Ordinal);
        Try(() => formulas = BuildFormulas(loader, tickMs, constants), "spells", errors);
        Try(() => BuildTeaches(loader, formulas), "items", errors);
        return errors;
    }

    public static MagicSetup Build(ContentLoader loader)
    {
        int tickMs = WorldContent.TickMilliseconds(loader);
        var constants = BuildConstants(loader, tickMs);
        var formulas = BuildFormulas(loader, tickMs, constants);
        return new MagicSetup(constants, formulas) { Teaches = BuildTeaches(loader, formulas) };
    }

    /// <summary><c>config.magic</c>: how Focus returns and Strain ebbs, what past-tolerance costs, and what skill and Resonance change.</summary>
    public static MagicConstants BuildConstants(ContentLoader loader, int tickMs)
    {
        if (!loader.Definitions.ContainsKey("config.magic"))
            return new MagicConstants();
        var map = Config(loader, "config.magic");
        var focus = Map(map, "focus");
        var strain = Map(map, "strain");
        var skill = Map(map, "skill");
        var resonance = Map(map, "resonance");
        var casting = Map(map, "casting");
        var constants = new MagicConstants
        {
            FocusRegenPerSecond = Int(focus, "regen_per_s"),
            FocusRegenDelayTicks = ToTicks(Number(focus, "regen_delay_s"), tickMs),
            StrainRecoveryPerSecond = Int(strain, "recovery_per_s"),
            StrainRecoveryDelayTicks = ToTicks(Number(strain, "recovery_delay_s"), tickMs),
            StrainedPercent = Int(strain, "strained_percent"),
            BacklashPerPoint = Int(strain, "backlash_per_point"),
            StrainPercentPerPoint = Int(skill, "strain_percent_per_point"),
            MinStrainPercent = Int(skill, "min_strain_percent"),
            FizzlePercentPerPoint = Int(skill, "fizzle_percent_per_point"),
            ResonanceReference = Int(resonance, "reference"),
            ResonanceDamagePercentPerPoint = Number(resonance, "damage_percent_per_point"),
            RecoveryTicks = ToTicks(Number(casting, "recovery_s"), tickMs),
        };
        if (constants.FocusRegenPerSecond < 0 || constants.StrainRecoveryPerSecond <= 0 || constants.StrainedPercent is <= 0 or > 100
            || constants.BacklashPerPoint < 1 || constants.StrainPercentPerPoint < 0 || constants.MinStrainPercent is < 0 or > 100
            || constants.FizzlePercentPerPoint is < 0 or > 100 || constants.ResonanceReference < 0 || constants.ResonanceDamagePercentPerPoint < 0)
            throw new FormatException("config.magic holds a value out of range (Strain must ebb and past tolerance must cost health; percentages in [0, 100])");
        return constants;
    }

    /// <summary>
    /// The formulas (<c>kind: spell</c>): a domain skill of the magic family, a complexity, costs in Focus and Strain, a cast
    /// time, and a payload - a projectile's one <c>damage</c>, or a self formula's <c>apply_effect</c> and <c>remove_effect</c>.
    /// </summary>
    public static ImmutableSortedDictionary<string, FormulaDefinition> BuildFormulas(ContentLoader loader, int tickMs, MagicConstants constants)
    {
        var skills = loader.GetByKind("skill");
        var effects = loader.GetByKind("effect");
        return loader.GetByKind("spell").Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            string id = definition.Id;
            string domain = Text(map, "domain");
            if (!skills.TryGetValue(domain, out var skill) || Read(skill.YamlSource).GetValueOrDefault("family") as string != "magic")
                throw new FormatException($"{id}: domain {domain} is not a magic-domain skill (a skill with family: magic)");
            var cost = Map(map, "cost");
            if (cost.Keys.OfType<string>().Any(k => k is not ("focus" or "strain")))
                throw new FormatException($"{id}: a formula costs focus and strain - there is no mana - and Phase 1 builds no contextual costs");
            var targeting = Text(map, "targeting") switch
            {
                "self" => Targeting.Self,
                "projectile" => Targeting.Projectile,
                var other => throw new FormatException($"{id}: targeting '{other}' is not built in Phase 1 (self, projectile)"),
            };
            int castTicks = Math.Max(1, ToTicks(Number(map, "cast_time_s"), tickMs));
            var formula = new FormulaDefinition(id, domain, Int(map, "complexity"), Int(cost, "focus"), Int(cost, "strain"), castTicks,
                constants.RecoveryTicks, targeting);
            if (formula.Complexity < 0 || formula.FocusCost < 0 || formula.StrainCost < 0)
                throw new FormatException($"{id}: complexity and costs are not negative");

            AttackProfile? blow = null;
            var applies = new List<string>();
            var removes = new List<string>();
            foreach (var row in Rows(map, "payload", id))
            {
                string type = Text(row, "type");
                if (targeting == Targeting.Projectile && type != "damage" || targeting == Targeting.Self && !SelfPayloads.Contains(type))
                {
                    throw new FormatException($"{id}: payload type '{type}' is not built for a {Text(map, "targeting")} formula in Phase 1 " +
                                              "(a projectile's one damage; a self formula's apply_effect and remove_effect)");
                }
                if (type == "damage")
                {
                    if (blow is not null)
                        throw new FormatException($"{id}: a projectile carries one damage entry");
                    var amount = List(row, "amount");
                    blow = new AttackProfile(id, IntOf(amount, 0, "amount"), IntOf(amount, 1, "amount"), Damage(Text(row, "damage_type")),
                        Mm(map, "range_m"), castTicks, 1, constants.RecoveryTicks, 0)
                    {
                        Ranged = true,
                        SkillId = domain,
                        Magic = true,
                    };
                    if (blow.DamageMin < 1 || blow.DamageMin > blow.DamageMax || blow.ReachMm <= 0)
                        throw new FormatException($"{id}: damage is [min, max] with 1 <= min <= max, and range_m is positive");
                    continue;
                }
                string effect = Text(row, "effect_ref");
                if (!effects.ContainsKey(effect))
                    throw new FormatException($"{id}: {effect} is not an effect");
                (type == "apply_effect" ? applies : removes).Add(effect);
            }
            if (targeting == Targeting.Projectile && blow is null)
                throw new FormatException($"{id}: a projectile needs its damage");
            if (targeting == Targeting.Self && applies.Count + removes.Count == 0)
                throw new FormatException($"{id}: a self formula puts on or lifts at least one effect");
            return formula with { Blow = blow, Applies = applies.ToImmutableArray(), Removes = removes.ToImmutableArray() };
        }).ToImmutableSortedDictionary(f => f.Id, f => f, StringComparer.Ordinal);
    }

    /// <summary>What reading a book teaches: <c>use: { consume: true, grants: [{ kind: spell, ref }] }</c> (DATA_MODEL.md §4.1).</summary>
    public static ImmutableSortedDictionary<string, ImmutableArray<string>> BuildTeaches(ContentLoader loader,
        ImmutableSortedDictionary<string, FormulaDefinition> formulas) =>
        loader.GetByKind("item").Values
            .Select(d => (d.Id, Use: Read(d.YamlSource).GetValueOrDefault("use") as Dictionary<object, object>))
            .Where(d => d.Use?.ContainsKey("grants") == true)
            .ToImmutableSortedDictionary(d => d.Id, d =>
            {
                if (d.Use!.GetValueOrDefault("consume") as string != "true")
                    throw new FormatException($"{d.Id}: a book that teaches is read once (use.consume: true)");
                if (d.Use.ContainsKey("teach_requires"))
                    throw new FormatException($"{d.Id}: teach_requires is not built in Phase 1");
                return Rows(d.Use, "grants", d.Id).Select(grant =>
                {
                    if (Text(grant, "kind") != "spell")
                        throw new FormatException($"{d.Id}: Phase 1 books teach formulas (grants of kind spell)");
                    string formula = Text(grant, "ref");
                    return formulas.ContainsKey(formula) ? formula : throw new FormatException($"{d.Id}: {formula} is not a formula");
                }).ToImmutableArray();
            }, StringComparer.Ordinal);

    private static void Try(Action build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "MAG001", Message = $"{what}: {e.Message}" });
        }
    }
}
