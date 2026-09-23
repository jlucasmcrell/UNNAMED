namespace UNNAMED.Content.Tests;

/// <summary>The pre-M3b hardening: references at any depth, target kinds, the mod namespace, Phase-1 semantics.</summary>
public class ContentChecksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "unnamed-checks-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private const string Skill = "id: skill.one_hand_blade\nkind: skill\nschema: 1\ndisplay_key: skill.one_hand_blade.name\ntags: [skill]\nfamily: combat\n";

    private const string Sword = """
        id: item.weapon.rusted_sword
        kind: item.weapon
        schema: 1
        display_key: item.weapon.rusted_sword.name
        tags: [weapon]
        category: weapon
        stack_max: 1
        weight: 3.0
        value_base: 20
        rarity: common
        damage: [7, 7]
        damage_type: physical_slash
        hands: one
        reach: 1.8
        attack_speed: 1.43
        skill_ref: skill.one_hand_blade
        """;

    private void Write(string relativePath, string yaml)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml.Replace("\r\n", "\n") + "\n");
    }

    private IReadOnlyList<ValidationError> Lint(params (string Path, string Yaml)[] files)
    {
        foreach (var (path, yaml) in files)
            Write(path, yaml);
        var loader = new ContentLoader();
        loader.LoadAll(_root);
        return loader.Errors.ToList();
    }

    private static string Tome(string grants) => $$"""
        id: item.tome.ember_primer
        kind: item
        schema: 1
        display_key: item.tome.ember_primer.name
        tags: [book]
        category: book
        stack_max: 1
        weight: 1.0
        value_base: 120
        rarity: uncommon
        use:
          consume: true
          grants:
        {{grants}}
        """;

    [Fact]
    public void AValidWeapon_AndItsSkill_PassEverything() =>
        Assert.Empty(Lint(("skills/one_hand_blade.yaml", Skill), ("items/weapon/rusted_sword.yaml", Sword)));

    [Fact]
    public void ANestedDanglingReference_IsCaught_WithItsLine()
    {
        var errors = Lint(("items/tome/ember_primer.yaml", Tome("    - { kind: spell, ref: spell.ember.bolt }")));

        var error = Assert.Single(errors, e => e.Code == "XREF003");
        Assert.Contains("use.grants[0].ref", error.Message);
        Assert.Contains("spell.ember.bolt", error.Message);
        Assert.Equal(14, error.LineNumber);
    }

    [Fact]
    public void AReferenceToTheWrongKind_IsCaught_EvenThoughTheIdExists()
    {
        var errors = Lint(("skills/one_hand_blade.yaml", Skill),
            ("items/tome/ember_primer.yaml", Tome("    - { kind: spell, ref: skill.one_hand_blade }")));

        Assert.Contains("is a skill, but this field takes spell", Assert.Single(errors, e => e.Code == "XREF002").Message);
    }

    [Fact]
    public void AnUnknownRewardKind_IsCaught() =>
        Assert.Contains(Lint(("items/tome/ember_primer.yaml", Tome("    - { kind: scripted, ref: item.tome.ember_primer }"))), e => e.Code == "XREF005");

    [Fact]
    public void ReferenceFields_AreFoundAtAnyDepth_InListsOfMaps_AndByRolePrefix()
    {
        string sword = Sword + "\nupgrades:\n  - { note: nested, parts: [ { item_ref: item.material.missing_ingot } ] }\n  - { parent_quest_ref: quest.missing.chain }\n";
        var errors = Lint(("skills/one_hand_blade.yaml", Skill), ("items/weapon/rusted_sword.yaml", sword));

        var dangling = errors.Where(e => e.Code == "XREF003").Select(e => e.Message).ToList();
        Assert.Contains(dangling, m => m.StartsWith("upgrades[0].parts[0].item_ref", StringComparison.Ordinal));
        Assert.Contains(dangling, m => m.StartsWith("upgrades[1].parent_quest_ref", StringComparison.Ordinal));
    }

    [Fact]
    public void AnItemRef_AcceptsWeaponsAndArmor_ButARefsListChecksEveryEntry()
    {
        string sword = Sword + "\ncounts_as_refs: []\nsalvage: { item_refs: [item.weapon.rusted_sword, item.material.missing] }\n";
        var errors = Lint(("skills/one_hand_blade.yaml", Skill), ("items/weapon/rusted_sword.yaml", sword));

        Assert.Contains(errors, e => e.Code == "XREF004" && e.Message.StartsWith("counts_as_refs", StringComparison.Ordinal));
        var dangling = Assert.Single(errors, e => e.Code == "XREF003");
        Assert.Contains("item.material.missing", dangling.Message);
    }

    [Fact]
    public void AReferenceFieldWithNoKnownTarget_IsCaught() =>
        Assert.Contains(Lint(("skills/one_hand_blade.yaml", Skill), ("items/weapon/rusted_sword.yaml", Sword + "\nstation_ref: station.forge_shed\n")),
            e => e.Code == "XREF004" && e.Message.StartsWith("station_ref", StringComparison.Ordinal));

    [Fact]
    public void ARenamedIdInContent_IsDangling_AndTheMessageNamesTheCurrentId()
    {
        Write("_aliases.yaml", "aliases:\n  spell.ember.firebolt: spell.ember.bolt\n");
        var errors = Lint(
            ("spells/ember/bolt.yaml", "id: spell.ember.bolt\nkind: spell\nschema: 1\ndisplay_key: spell.ember.bolt.name\ntags: [spell]\n"),
            ("items/tome/ember_primer.yaml", Tome("    - { kind: spell, ref: spell.ember.firebolt }")));

        Assert.Contains("renamed to spell.ember.bolt", Assert.Single(errors, e => e.Code == "XREF003").Message);
    }

    [Theory]
    [InlineData("items/mod/x.yaml", "item.mod.acme.lantern", "item")]
    [InlineData("items/weapon/mod.yaml", "item.weapon.mod.acme.blade", "item.weapon")]
    [InlineData("creatures/mod.yaml", "creature.mod.acme.drake", "creature")]
    public void TheModNamespace_IsReservedForMods(string path, string id, string kind) =>
        Assert.Contains(Lint((path, $"id: {id}\nkind: {kind}\nschema: 1\ndisplay_key: {id}.name\ntags: [x]\n")), e => e.Code == "MOD001");

    [Fact]
    public void ASegmentThatMerelyStartsWithMod_IsNotReserved()
    {
        var errors = Lint(("skills/one_hand_blade.yaml", Skill), ("items/weapon/rusted_sword.yaml", Sword.Replace("item.weapon.rusted_sword", "item.weapon.modest_blade")));
        Assert.DoesNotContain(errors, e => e.Code == "MOD001");
    }

    [Theory]
    [InlineData("stack_max: 1\n", "stack_max: 0\n", "SEM003", "stack_max")]
    [InlineData("damage: [7, 7]\n", "damage: [11, 7]\n", "SEM003", "damage")]
    [InlineData("rarity: common\n", "rarity: shiny\n", "SEM002", "rarity")]
    [InlineData("hands: one\n", "hands: three\n", "SEM002", "hands")]
    [InlineData("category: weapon\n", "category: armor\n", "SEM003", "category")]
    [InlineData("weight: 3.0\n", "weight: -1\n", "SEM003", "weight")]
    public void WeaponSemantics_AreChecked(string find, string replace, string code, string field)
    {
        // Normalised first: a raw string literal carries the line endings of the checkout.
        string sword = (Sword + "\n").Replace("\r\n", "\n");
        Assert.Contains(find, sword);
        var errors = Lint(("skills/one_hand_blade.yaml", Skill), ("items/weapon/rusted_sword.yaml", sword.Replace(find, replace)));
        Assert.Contains(errors, e => e.Code == code && e.Message.Contains($" {field} ", StringComparison.Ordinal));
    }

    [Fact]
    public void ArmorSemantics_AreChecked()
    {
        const string cap = "id: item.armor.hide_cap\nkind: item.armor\nschema: 1\ndisplay_key: item.armor.hide_cap.name\ntags: [armor]\n" +
                           "category: armor\nstack_max: 1\nweight: 1\nvalue_base: 12\nrarity: common\nslot: crown\narmor_value: 3\nmovement_penalty: 2\n";
        var errors = Lint(("items/armor/hide_cap.yaml", cap));

        Assert.Contains(errors, e => e.Code == "SEM002" && e.Message.Contains(" slot ", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == "SEM003" && e.Message.Contains(" movement_penalty ", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatureSemantics_AreChecked()
    {
        const string wolf = "id: creature.beast.wolf_grey\nkind: creature\nschema: 1\ndisplay_key: creature.beast.wolf_grey.name\ntags: [creature]\n" +
                            "family: beast\narchetype: predator\nlevel_band: [2, 2]\npools: { health: 30 }\nattributes: { might: 8, luck: 3 }\ntameable: true\n";
        var errors = Lint(("creatures/beast/wolf_grey.yaml", wolf));

        Assert.Contains(errors, e => e.Code == "SEM002" && e.Message.Contains("attributes.luck", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == "SEM003" && e.Message.Contains(" tameable ", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Code == "SEM001");
    }
}
