// UNNAMED Content - teachers and techniques from content: an NPC's teaches block, the player's active techniques, and the lint that
// everything learnable can be learned somewhere (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.3 C5, C10; WP9; P-7, C01:172)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Social;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the player's active techniques and an NPC's <c>teaches</c> entries, and lints what the other passes cannot see (TCH001): an
/// active technique works one weapon family's skill and changes its blow within range; a <c>teaches</c> entry names a definition of its
/// kind, a known skill band, and a fee that is never negative; and every recipe, technique, formula and building piece has at least one
/// findable source - a teacher, a book, a quest's reward, an experiment, or a starting package (P-7: "nobody meets every teacher", so
/// nothing may hang on a source that does not exist).
/// </summary>
public static class TeachingContent
{
    /// <summary>The ability classes built: a creature's attack (M3c) and the player's active technique (WP9). The rest of §4.7's arrive with their systems.</summary>
    private static readonly string[] AbilityClasses = { "creature", "active", "people" };

    private static readonly string[] EntryFields = { "kind", "recipe_ref", "ability_ref", "spell_ref", "piece_ref", "fee", "by_dialogue", "requires" };

    private static readonly string[] RequirementFields = { "skill", "trust", "prerequisite", "deed", "flag", "quest" };

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        if (!loader.Definitions.ContainsKey("config.time"))
            return errors;
        Try(() => BuildTechniques(loader), "abilities", errors);
        Try(() => BuildSkillBands(loader), "config.teaching", errors);
        // The sources are read through the other kinds' builders: while any of them is refused, its own lint says why, and this one waits.
        if (errors.Count == 0 && !loader.Errors.Any(e => e.SeverityLevel == ValidationError.Severity.Error))
            Try(() => CheckSources(loader), "sources", errors);
        return errors;
    }

    /// <summary>
    /// <c>config.teaching</c>'s <c>skill_bands</c>: each band's name and the skill level it starts at (PROGRESSION.md §6's table). A pack
    /// with none has no bands, and a requirement naming one is refused.
    /// </summary>
    public static ImmutableSortedDictionary<string, int> BuildSkillBands(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.teaching"))
            return ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);
        var bands = Map(Config(loader, "config.teaching"), "skill_bands");
        var built = bands.ToImmutableSortedDictionary(kv => kv.Key as string ?? "", kv => Int(bands, kv.Key as string ?? ""), StringComparer.Ordinal);
        if (built.Values.Any(level => level < 0))
            throw new FormatException("config.teaching: a skill band starts at a level of 0 or more");
        return built;
    }

    /// <summary>
    /// The player's active techniques (<c>ability</c> of <c>class: active</c>, DATA_MODEL.md §4.7): a <c>discipline</c> (a combat skill:
    /// the weapon family that makes it), <c>cost.stamina</c>, <c>cooldown_s</c>, optionally <c>windup_multiplier</c> and <c>reach_bonus_m</c>,
    /// and a <c>payload</c> of one <c>damage</c> entry's <c>multiplier_of_weapon</c> and optionally an <c>apply_effect</c> with a <c>chance</c>.
    /// </summary>
    public static ImmutableSortedDictionary<string, TechniqueDefinition> BuildTechniques(ContentLoader loader)
    {
        int tickMs = WorldContent.TickMilliseconds(loader);
        var techniques = ImmutableSortedDictionary.CreateBuilder<string, TechniqueDefinition>(StringComparer.Ordinal);
        foreach (var definition in loader.GetByKind("ability").Values.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            var map = Read(definition.YamlSource);
            string id = definition.Id;
            string kind = Text(map, "class");
            if (!AbilityClasses.Contains(kind))
                throw new FormatException($"{id}: class '{kind}' is not built ({string.Join(", ", AbilityClasses)})");
            if (kind != "active")
                continue;
            string skill = Text(map, "discipline");
            if (!loader.Definitions.TryGetValue(skill, out var skillDefinition) || skillDefinition.Kind != "skill"
                || Read(skillDefinition.YamlSource).GetValueOrDefault("family") as string != "combat")
                throw new FormatException($"{id}: discipline {skill} is not a combat skill - an active technique is a weapon family's");
            var payload = Rows(map, "payload", id).ToList();
            var damage = payload.Where(p => p.GetValueOrDefault("type") as string == "damage").ToList();
            if (damage.Count != 1)
                throw new FormatException($"{id}: payload has exactly one damage entry (multiplier_of_weapon)");
            var effects = payload.Where(p => p.GetValueOrDefault("type") as string == "apply_effect").ToList();
            if (effects.Count > 1 || payload.Count != damage.Count + effects.Count)
                throw new FormatException($"{id}: payload is one damage entry and at most one apply_effect");
            var effect = effects.FirstOrDefault();
            var cost = map.GetValueOrDefault("cost") as Dictionary<object, object> ?? throw new FormatException($"{id}: cost.stamina is required");
            if (cost.Keys.Any(k => k as string != "stamina"))
                throw new FormatException($"{id}: a technique costs stamina and nothing else");
            var technique = new TechniqueDefinition(id, skill, Number(damage[0], "multiplier_of_weapon"),
                map.ContainsKey("windup_multiplier") ? Number(map, "windup_multiplier") : 1.0,
                map.ContainsKey("reach_bonus_m") ? Mm(map, "reach_bonus_m") : 0, Int(cost, "stamina"), ToTicks(Number(map, "cooldown_s"), tickMs))
            {
                OnHitEffect = effect is null ? null : Text(effect, "effect_ref"),
                OnHitEffectPercent = effect is null ? 0 : (int)Math.Round(Number(effect, "chance") * 100, MidpointRounding.AwayFromZero),
                AimRegion = map.ContainsKey("aim_region") ? BodyRegions.Parse(Text(map, "aim_region")) : null,
            };
            if (technique.OnHitEffect is { } applied && !loader.GetByKind("effect").ContainsKey(applied))
                throw new FormatException($"{id}: {applied} is not an effect");
            if (technique.Problem() is { } problem)
                throw new FormatException($"{id}: {problem}");
            techniques.Add(id, technique);
        }
        return techniques.ToImmutable();
    }

    /// <summary>
    /// An NPC's <c>teaches</c> block (C10): entries of <c>kind</c> (recipe, technique, spell, building_piece) naming their definition by
    /// the matching <c>recipe_ref</c>, <c>ability_ref</c>, <c>spell_ref</c> or <c>piece_ref</c>; a coin <c>fee</c> (0 when absent);
    /// <c>by_dialogue: true</c> when a line of the NPC's conversation gives the lesson; and <c>requires</c> - a <c>skill</c>
    /// (<c>{skill_ref, band}</c>), <c>trust</c> (the least the teacher's trust must be), a <c>prerequisite</c> already known, a
    /// <c>deed</c>, a <c>flag</c> or a <c>quest</c> (as the dialogue conditions of those names ask them).
    /// </summary>
    internal static ImmutableArray<TeachingOffer> Offers(string npcId, Dictionary<object, object> npc, ContentLoader loader)
    {
        if (!npc.ContainsKey("teaches"))
            return ImmutableArray<TeachingOffer>.Empty;
        var bands = BuildSkillBands(loader);
        var offers = Rows(npc, "teaches", npcId).Select((row, i) => Offer($"{npcId} teaches[{i}]", row, loader, bands)).ToImmutableArray();
        if (offers.GroupBy(o => o.DefinitionId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new FormatException($"{npcId}: teaches {twice.Key} twice");
        return offers;
    }

    private static TeachingOffer Offer(string at, Dictionary<object, object> row, ContentLoader loader, ImmutableSortedDictionary<string, int> bands)
    {
        if (row.Keys.OfType<string>().FirstOrDefault(k => !EntryFields.Contains(k)) is { } unknown)
            throw new FormatException($"{at}: '{unknown}' is not a field of a teaches entry ({string.Join(", ", EntryFields)})");
        string key = Text(row, "kind");
        var kind = TeachingKeys.Parse(key)
                   ?? throw new FormatException($"{at}: kind is {string.Join(", ", TeachingKeys.All.Select(TeachingKeys.Key))}, not '{key}'");
        string contentKind = TeachingKeys.ContentKind(kind);
        string field = $"{contentKind}_ref";
        if (row.Keys.OfType<string>().FirstOrDefault(k => k.EndsWith("_ref", StringComparison.Ordinal) && k != field) is { } other)
            throw new FormatException($"{at}: a {key} entry names its definition by {field}, not {other}");
        string id = Text(row, field);
        if (!loader.Definitions.TryGetValue(id, out var definition) || definition.Kind != contentKind)
            throw new FormatException($"{at}: {id} is not a defined {contentKind}");
        if (kind == TeachingKind.Technique && Read(definition.YamlSource).GetValueOrDefault("class") as string != "active")
            throw new FormatException($"{at}: {id} is not an active technique (class: active) - a creature's attack is never taught");
        long fee = row.TryGetValue("fee", out var feeValue)
            ? long.TryParse(feeValue as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) && parsed >= 0
                ? parsed
                : throw new FormatException($"{at}: fee is a whole number of coin, 0 or more")
            : 0;
        return new TeachingOffer(kind, id, Requirements(at, row, loader, bands), fee)
        {
            ByDialogue = row.GetValueOrDefault("by_dialogue") as string == "true",
        };
    }

    private static ImmutableArray<TeachingRequirement> Requirements(string at, Dictionary<object, object> row, ContentLoader loader,
        ImmutableSortedDictionary<string, int> bands)
    {
        if (!row.ContainsKey("requires"))
            return ImmutableArray<TeachingRequirement>.Empty;
        var map = Map(row, "requires");
        if (map.Keys.OfType<string>().FirstOrDefault(k => !RequirementFields.Contains(k)) is { } unknown)
            throw new FormatException($"{at}: '{unknown}' is not a requirement ({string.Join(", ", RequirementFields)})");
        var requirements = ImmutableArray.CreateBuilder<TeachingRequirement>();
        if (map.ContainsKey("skill"))
        {
            var skill = Map(map, "skill");
            string skillId = Text(skill, "skill_ref");
            if (!loader.GetByKind("skill").ContainsKey(skillId))
                throw new FormatException($"{at}: {skillId} is not a skill");
            string band = Text(skill, "band");
            if (!bands.TryGetValue(band, out int floor))
                throw new FormatException($"{at}: '{band}' is not a skill band config.teaching names ({string.Join(", ", bands.Keys)})");
            requirements.Add(new SkillBandRequirement(skillId, band, floor));
        }
        if (map.ContainsKey("trust"))
        {
            int trust = Int(map, "trust");
            if (trust is < Relationships.Min or > Relationships.Max)
                throw new FormatException($"{at}: trust is from {Relationships.Min} to {Relationships.Max}");
            requirements.Add(new TrustRequirement(trust));
        }
        if (map.ContainsKey("prerequisite"))
        {
            string known = Text(map, "prerequisite");
            if (!Domain.Progression.ProgressionKeys.IsTechniqueId(known) || !loader.Definitions.ContainsKey(known))
                throw new FormatException($"{at}: prerequisite {known} is not a defined technique, recipe, formula or building piece");
            requirements.Add(new PrerequisiteRequirement(known));
        }
        if (map.ContainsKey("deed"))
            requirements.Add(new DeedRequirement(SocialContent.Deed($"{at} requires.deed", Map(map, "deed"), loader, negated: false)));
        if (map.ContainsKey("flag"))
            requirements.Add(new FlagRequirement(SocialContent.WorldState($"{at} requires.flag", Map(map, "flag"), loader)));
        if (map.ContainsKey("quest"))
        {
            var quest = Map(map, "quest");
            requirements.Add(new QuestRequirement(SocialContent.QuestState($"{at} requires.quest", quest, loader, quest.GetValueOrDefault("not") as string == "true")));
        }
        return requirements.ToImmutable();
    }

    /// <summary>
    /// P-7 (C01:172): every recipe, active technique, formula and building piece has at least one findable source - an NPC's
    /// <c>teaches</c> block, a book's <c>use.grants</c>, a quest's reward, a recipe's <c>discovery.experiment</c>, or the starting package
    /// (the default's, or a Call's).
    /// </summary>
    private static object CheckSources(ContentLoader loader)
    {
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in SocialContent.BuildNpcs(loader).Values)
            sources.UnionWith(npc.Teaches.Select(o => o.DefinitionId));
        foreach (var item in loader.GetByKind("item").Values.Concat(loader.GetByKind("item.weapon").Values).Concat(loader.GetByKind("item.armor").Values))
        {
            if (Read(item.YamlSource).GetValueOrDefault("use") is Dictionary<object, object> use && use.ContainsKey("grants"))
                sources.UnionWith(Rows(use, "grants", item.Id).Select(g => g.GetValueOrDefault("ref") as string).OfType<string>());
        }
        foreach (var recipe in loader.GetByKind("recipe").Values)
        {
            if (Read(recipe.YamlSource).GetValueOrDefault("discovery") is Dictionary<object, object> discovery && discovery.GetValueOrDefault("experiment") as string == "true")
                sources.Add(recipe.Id);
        }
        foreach (var quest in QuestContent.BuildQuests(loader).Values)
            sources.UnionWith(quest.Rewards.OfType<Domain.Quests.TechniqueReward>().Select(r => r.DefinitionId));
        sources.UnionWith(ProgressionContent.BuildRules(loader).StartingPackage.Techniques);
        foreach (var call in CharacterContent.BuildCalls(loader).Values)
            sources.UnionWith(call.Package.Techniques);

        var learnable = loader.GetByKind("recipe").Keys
            .Concat(loader.GetByKind("spell").Keys)
            .Concat(loader.GetByKind("piece").Keys)
            .Concat(loader.GetByKind("ability").Values.Where(a => Read(a.YamlSource).GetValueOrDefault("class") as string == "active").Select(a => a.Id))
            .OrderBy(id => id, StringComparer.Ordinal);
        var orphans = learnable.Where(id => !sources.Contains(id)).ToList();
        if (orphans.Count > 0)
            throw new FormatException($"no findable source - no teacher, book, quest reward, experiment or starting package - teaches {string.Join(", ", orphans)}");
        return sources;
    }

    private static void Try(Func<object> build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "TCH001", Message = $"{what}: {e.Message}" });
        }
    }
}
