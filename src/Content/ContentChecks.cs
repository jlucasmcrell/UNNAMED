// UNNAMED Content - reference, namespace and Phase-1 semantic checks (DATA_MODEL.md §4, §5; pre-M3b hardening)
// No Godot references - pure C#

using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace UNNAMED.Content;

/// <summary>
/// The validation `DATA_MODEL.md` §5 makes mandatory, as three explicit passes rather than a general rule language:
/// every reference at any depth resolves to a definition of the kind its field declares (XREF002-XREF005); core
/// content leaves the reserved mod namespace alone (MOD001); and the Phase-1 item, weapon, armor and creature schemas
/// (§4.1-§4.4) have their required fields, closed enums and sane ranges (SEM001-SEM003). Errors name file and line.
/// </summary>
public static class ContentChecks
{
    /// <summary>§5's field patterns, matched as a suffix so a role prefix works (<c>parent_quest_ref</c> is a quest).</summary>
    private static readonly Dictionary<string, string[]> ReferenceSuffixes = new(StringComparer.Ordinal)
    {
        ["item_ref"] = new[] { "item", "item.weapon", "item.armor" },
        ["creature_ref"] = new[] { "creature" },
        ["npc_ref"] = new[] { "npc" },
        ["spell_ref"] = new[] { "spell" },
        ["ability_ref"] = new[] { "ability" },
        ["effect_ref"] = new[] { "effect" },
        ["affix_ref"] = new[] { "affix" },
        ["recipe_ref"] = new[] { "recipe" },
        ["resource_ref"] = new[] { "resource" },
        ["species_ref"] = new[] { "species" },
        ["region_ref"] = new[] { "region" },
        ["set_ref"] = new[] { "set" },
        ["quest_ref"] = new[] { "quest" },
        ["dialogue_ref"] = new[] { "dialogue" },
        ["faction_ref"] = new[] { "faction" },
        ["loot_ref"] = new[] { "loot" },
        ["location_ref"] = new[] { "location" },
        ["fact_ref"] = new[] { "fact" },
        ["flag_ref"] = new[] { "world_flag" },
        ["schedule_ref"] = new[] { "schedule" },
        ["anchor_ref"] = new[] { "anchor" },
        ["merchant_ref"] = new[] { "merchant" },
        ["skill_ref"] = new[] { "skill" },   // skill joined the closed kind table at M2c (§4.21)
        ["node_ref"] = new[] { "node" },     // a region's authored node (M3f)
        ["piece_ref"] = new[] { "piece" },   // a building piece (M7)
    };

    /// <summary>§4 fields that are references although they predate the <c>_ref</c> naming convention.</summary>
    private static readonly Dictionary<string, string[]> NamedReferences = new(StringComparer.Ordinal)
    {
        ["loot_table"] = new[] { "loot" },
        ["attack_set"] = new[] { "ability" },
        ["moveset"] = new[] { "ability" },
        ["affix_pool"] = new[] { "affix" },
        ["domain"] = new[] { "skill" },   // a formula's magic-domain skill (§4.6)
        ["giver_ref"] = new[] { "npc" },  // a quest's giver (§4.11): a role name, not a kind
    };

    /// <summary>§4.11's closed reward kinds, and the content kind a <c>{kind, ref}</c> entry of each resolves against.</summary>
    private static readonly Dictionary<string, string[]?> RewardKinds = new(StringComparer.Ordinal)
    {
        ["xp"] = null, ["currency"] = null, ["reputation"] = null, ["relationship"] = null, ["title"] = null,
        ["property"] = null, ["permanent_ability"] = null, ["transformation"] = null,
        ["item"] = new[] { "item", "item.weapon", "item.armor" },
        ["spell"] = new[] { "spell" },
        ["ability"] = new[] { "ability" },
        ["recipe"] = new[] { "recipe" },
        ["world_flag"] = new[] { "world_flag" },
        ["companion"] = new[] { "npc" },
        ["access"] = new[] { "location" },
    };

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        foreach (var definition in loader.Definitions.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            CheckModNamespace(definition, errors);
            if (Parse(definition) is not { } root)
                continue;
            WalkReferences(definition, root, "", loader, errors);
            PhaseOneSchemas.Check(definition, root, errors);
        }
        return errors;
    }

    /// <summary>
    /// MODDING: <c>&lt;kind&gt;.mod.&lt;package_namespace&gt;.*</c> belongs to mods. Core content may not put a <c>mod</c>
    /// segment directly after any kind prefix its ID starts with (<c>item.mod.*</c>, <c>item.weapon.mod.*</c>).
    /// </summary>
    private static void CheckModNamespace(ContentEnvelope definition, List<ValidationError> errors)
    {
        foreach (var kind in ContentKindRegistry.Kinds.Values)
        {
            string prefix = kind.IdPrefix + ".";
            if (definition.Id.StartsWith(prefix, StringComparison.Ordinal)
                && definition.Id[prefix.Length..].Split('.')[0] == "mod")
            {
                errors.Add(Error("MOD001", definition,
                    $"{definition.Id} uses the reserved mod namespace '{kind.IdPrefix}.mod.*'; core content may not", 1));
                return;
            }
        }
    }

    private static void WalkReferences(ContentEnvelope definition, YamlNode node, string path, ContentLoader loader, List<ValidationError> errors)
    {
        switch (node)
        {
            case YamlMappingNode map:
            {
                // A reward or grant entry: { kind: <reward kind>, ref: <definition> } (§4.1 use.grants, §4.11).
                if (Scalar(map, "ref") is { } rewardRef && Scalar(map, "kind") is { } rewardKind)
                {
                    if (!RewardKinds.TryGetValue(rewardKind.Value!, out var targets))
                        errors.Add(Error("XREF005", definition, $"{Here(path, "kind")}: '{rewardKind.Value}' is not a reward kind (DATA_MODEL.md §4.11)", Line(rewardKind)));
                    else if (targets is not null)
                        Resolve(definition, rewardRef, Here(path, "ref"), targets, loader, errors);
                }
                foreach (var (keyNode, value) in map.Children)
                {
                    string key = (keyNode as YamlScalarNode)?.Value ?? string.Empty;
                    string where = Here(path, key);
                    if (TargetsOf(key) is { } kinds)
                    {
                        foreach (var reference in References(value))
                        {
                            if (reference is YamlScalarNode scalar)
                                Resolve(definition, scalar, where, kinds, loader, errors);
                            else
                                errors.Add(Error("XREF004", definition, $"{where}: a reference is a definition ID, not a {reference.NodeType}", Line(reference)));
                        }
                    }
                    else if (key.EndsWith("_ref", StringComparison.Ordinal) || key.EndsWith("_refs", StringComparison.Ordinal))
                    {
                        errors.Add(Error("XREF004", definition,
                            $"{where}: no reference field of this name in DATA_MODEL.md §5, so its target kind is unknown", Line(keyNode)));
                    }
                    else
                    {
                        WalkReferences(definition, value, where, loader, errors);
                    }
                }
                break;
            }
            case YamlSequenceNode list:
            {
                int i = 0;
                foreach (var child in list.Children)
                    WalkReferences(definition, child, $"{path}[{i++}]", loader, errors);
                break;
            }
        }
    }

    private static string[]? TargetsOf(string key)
    {
        if (NamedReferences.TryGetValue(key, out var named))
            return named;
        string single = key.EndsWith("_refs", StringComparison.Ordinal) ? key[..^1] : key;
        return ReferenceSuffixes
            .Where(kv => single == kv.Key || single.EndsWith("_" + kv.Key, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .FirstOrDefault();
    }

    /// <summary>A reference field holds one ID or a list of them.</summary>
    private static IEnumerable<YamlNode> References(YamlNode value) =>
        value is YamlSequenceNode list ? list.Children : new[] { value };

    private static void Resolve(ContentEnvelope definition, YamlScalarNode reference, string where, string[] kinds, ContentLoader loader,
        List<ValidationError> errors)
    {
        string id = reference.Value ?? string.Empty;
        if (!loader.Definitions.TryGetValue(id, out var target))
        {
            string renamed = loader.Aliases.TryGetValue(id, out string? current) ? $"; it was renamed to {current} - name the current ID" : "";
            errors.Add(Error("XREF003", definition, $"{where}: '{id}' is not defined{renamed}", Line(reference)));
        }
        else if (!kinds.Contains(target.Kind))
        {
            errors.Add(Error("XREF002", definition,
                $"{where}: '{id}' is a {target.Kind}, but this field takes {string.Join(" or ", kinds)} (DATA_MODEL.md §5)", Line(reference)));
        }
    }

    internal static YamlMappingNode? Parse(ContentEnvelope definition)
    {
        if (string.IsNullOrEmpty(definition.YamlSource))
            return null;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(definition.YamlSource));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;   // the loader has already reported the parse error
        }
    }

    internal static YamlScalarNode? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlScalarNode : null;

    internal static int Line(YamlNode node) => (int)node.Start.Line;

    internal static ValidationError Error(string code, ContentEnvelope definition, string message, int line) => new()
    {
        SeverityLevel = ValidationError.Severity.Error,
        Code = code,
        Message = message,
        FilePath = definition.SourceFile ?? string.Empty,
        LineNumber = line + definition.LineOffset,
        DefinitionId = definition.Id,
    };

    private static string Here(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";
}

/// <summary>
/// Required fields, closed enums and ranges for the Phase-1 schemas M3b and M3d author against (`DATA_MODEL.md`
/// §4.1-§4.4). Deliberately only these kinds: each later milestone adds its own when it creates the content.
/// </summary>
internal static class PhaseOneSchemas
{
    private static readonly string[] Categories = { "weapon", "armor", "consumable", "material", "tool", "book", "quest_item", "container", "currency", "misc" };
    private static readonly string[] Rarities = { "common", "uncommon", "rare", "epic", "legendary", "artifact" };
    private static readonly string[] DamageTypes = { "physical_slash", "physical_pierce", "physical_blunt", "fire", "frost", "shock", "arcane", "holy", "necrotic", "poison" };
    private static readonly string[] Hands = { "one", "two", "offhand" };
    private static readonly string[] Slots = { "head", "chest", "hands", "legs", "feet", "cloak", "ring", "amulet" };
    private static readonly string[] MaterialClasses = { "cloth", "leather", "mail", "plate", "chitin", "exotic" };
    private static readonly string[] Archetypes = { "predator", "prey", "scavenger", "humanoid", "undead", "construct", "spirit", "apex" };
    private static readonly string[] Attributes = { "might", "endurance", "agility", "precision", "will", "insight", "presence" };
    private static readonly string[] Tiers = { "A", "B", "C", "D" };

    public static void Check(ContentEnvelope definition, YamlMappingNode root, List<ValidationError> errors)
    {
        var fields = new Fields(definition, root, errors);
        switch (definition.Kind)
        {
            case "item" or "item.weapon" or "item.armor":
                CheckItem(definition.Kind, fields);
                break;
            case "creature":
                CheckCreature(fields);
                break;
        }
    }

    private static void CheckItem(string kind, Fields f)
    {
        string? category = f.OneOf("category", Categories, required: true);
        long? stack = f.Integer("stack_max", required: true, min: 1);
        f.Number("weight", required: true, min: 0);
        f.Integer("value_base", required: true, min: 0);
        f.OneOf("rarity", Rarities, required: true);
        f.Integer("durability_max", required: false, min: 1);
        f.Integer("enchant_sockets", required: false, min: 0);

        if (kind == "item.weapon")
        {
            f.Require(category is null or "weapon", "category", "a weapon's category is weapon");
            f.Require(stack is null or 1, "stack_max", "weapons never stack");
            f.Range("damage", required: true);
            f.OneOf("damage_type", DamageTypes, required: true);
            f.OneOf("hands", Hands, required: true);
            f.Number("attack_speed", required: false, min: 0.01);
            f.Number("reach", required: false, min: 0.01);
            f.Number("stamina_cost", required: false, min: 0);
            // skill_ref names the weapon-family skill when there is one; Phase 1 trains only one_hand_blade (PROTOTYPE.md §4.1).
        }
        else if (kind == "item.armor")
        {
            f.Require(category is null or "armor", "category", "armor's category is armor");
            f.Require(stack is null or 1, "stack_max", "armor never stacks");
            f.OneOf("slot", Slots, required: true);
            f.Number("armor_value", required: true, min: 0);
            f.OneOf("material_class", MaterialClasses, required: false);
            f.Number("movement_penalty", required: false, min: 0, max: 1);
            f.Number("stealth_penalty", required: false, min: 0, max: 1);
        }
    }

    private static void CheckCreature(Fields f)
    {
        f.Present("family", "a creature names its family");
        f.OneOf("archetype", Archetypes, required: true);
        f.Range("level_band", required: true);
        f.Number("pools.health", required: true, min: 1);
        f.Number("pools.stamina", required: false, min: 0);
        f.Number("pools.focus", required: false, min: 0);
        f.KeysOf("attributes", Attributes);
        f.OneOf("tier_hint", Tiers, required: false);
        f.Require(f.Text("tameable") is null or "false", "tameable", "taming is deferred: false everywhere in Phase 1-2");
    }

    /// <summary>Typed reads of one definition's fields that report what is wrong where it is wrong.</summary>
    private sealed class Fields
    {
        private readonly ContentEnvelope _definition;
        private readonly YamlMappingNode _root;
        private readonly List<ValidationError> _errors;

        public Fields(ContentEnvelope definition, YamlMappingNode root, List<ValidationError> errors)
        {
            _definition = definition;
            _root = root;
            _errors = errors;
        }

        /// <summary>A dotted path into nested maps: <c>pools.health</c>.</summary>
        public YamlNode? Node(string path)
        {
            YamlNode? node = _root;
            foreach (string key in path.Split('.'))
            {
                if (node is not YamlMappingNode map || !map.Children.TryGetValue(new YamlScalarNode(key), out node))
                    return null;
            }
            return node;
        }

        public string? Text(string path) => (Node(path) as YamlScalarNode)?.Value;

        public void Present(string path, string why)
        {
            if (Node(path) is null)
                Fail("SEM001", path, $"is required: {why}", _root);
        }

        public string? OneOf(string path, string[] allowed, bool required)
        {
            var node = Node(path);
            if (node is null)
            {
                if (required)
                    Fail("SEM001", path, "is required", _root);
                return null;
            }
            string? value = (node as YamlScalarNode)?.Value;
            if (value is null || !allowed.Contains(value))
            {
                Fail("SEM002", path, $"'{value}' is not one of {string.Join(", ", allowed)}", node);
                return null;
            }
            return value;
        }

        public long? Integer(string path, bool required, long min)
        {
            var node = Node(path);
            if (node is null)
            {
                if (required)
                    Fail("SEM001", path, "is required", _root);
                return null;
            }
            if (node is not YamlScalarNode { Value: { } text } || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                Fail("SEM003", path, "must be a whole number", node);
                return null;
            }
            if (value < min)
                Fail("SEM003", path, $"{value} is below the minimum {min}", node);
            return value;
        }

        public double? Number(string path, bool required, double min, double max = double.MaxValue)
        {
            var node = Node(path);
            if (node is null)
            {
                if (required)
                    Fail("SEM001", path, "is required", _root);
                return null;
            }
            if (node is not YamlScalarNode { Value: { } text } || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                Fail("SEM003", path, "must be a number", node);
                return null;
            }
            if (value < min || value > max)
                Fail("SEM003", path, $"{value} is outside [{min}, {(max == double.MaxValue ? "..." : max.ToString(CultureInfo.InvariantCulture))}]", node);
            return value;
        }

        /// <summary>A <c>[min, max]</c> pair of positive whole numbers with min &lt;= max (§5 "range sanity").</summary>
        public void Range(string path, bool required)
        {
            var node = Node(path);
            if (node is null)
            {
                if (required)
                    Fail("SEM001", path, "is required", _root);
                return;
            }
            var values = (node as YamlSequenceNode)?.Children.Select(c => (c as YamlScalarNode)?.Value).ToList();
            if (values is not { Count: 2 }
                || !long.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long low)
                || !long.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long high))
            {
                Fail("SEM003", path, "must be [min, max] whole numbers", node);
                return;
            }
            if (low < 1 || low > high)
                Fail("SEM003", path, $"[{low}, {high}] needs 1 <= min <= max", node);
        }

        public void KeysOf(string path, string[] allowed)
        {
            if (Node(path) is not YamlMappingNode map)
                return;
            foreach (var (key, value) in map.Children)
            {
                string name = (key as YamlScalarNode)?.Value ?? string.Empty;
                if (!allowed.Contains(name))
                    Fail("SEM002", $"{path}.{name}", $"is not one of the canonical attributes {string.Join(", ", allowed)}", key);
                else if (value is not YamlScalarNode { Value: { } text } || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long score) || score < 0)
                    Fail("SEM003", $"{path}.{name}", "must be a non-negative whole number", value);
            }
        }

        public void Require(bool ok, string path, string why)
        {
            if (!ok)
                Fail("SEM003", path, why, Node(path) ?? _root);
        }

        private void Fail(string code, string path, string message, YamlNode at) =>
            _errors.Add(ContentChecks.Error(code, _definition, $"{_definition.Id} {path} {message}", ContentChecks.Line(at)));
    }
}
