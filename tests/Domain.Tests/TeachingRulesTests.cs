using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;

namespace UNNAMED.Domain.Tests;

/// <summary>WP9: a teacher's gates asked of the world as they stand, and a technique's blow drawn from the weapon in hand.</summary>
public class TeachingRulesTests
{
    private const string Teacher = "npc.test.smith";

    private sealed class Facts : IDialogueFacts
    {
        public Dictionary<string, int> Skills { get; } = new(StringComparer.Ordinal);
        public int Trust { get; set; }
        public int Killed { get; set; }
        public long Flag { get; set; }
        public string Quest { get; set; } = "not_started";

        public bool Visited(string dialogueId, string nodeId) => false;
        public long WorldFlag(string flagId, string? locationId) => Flag;
        public int Carried(string itemId, int qualityMin) => 0;
        public int Relationship(string npcId, string dimension) => npcId == Teacher && dimension == "trust" ? Trust : 0;
        public int SkillLevel(string skillId) => Skills.GetValueOrDefault(skillId);
        public int Level => 1;
        public string QuestState(string questId, string? objectiveId) => Quest;
        public Companions.CompanionOrder? CompanionOrderOf(string npcId) => null;
        public int Deeds(DeedKind kind, string subjectId, string? npcId) => kind == DeedKind.Killed ? Killed : 0;
    }

    private static readonly TeachingOffer Lesson = new(TeachingKind.Technique, "ability.test.strike", ImmutableArray.Create<TeachingRequirement>(
        new SkillBandRequirement("skill.test_blade", "familiar", 15),
        new TrustRequirement(10),
        new PrerequisiteRequirement("ability.test.stance"),
        new DeedRequirement(new DeedCondition(DeedKind.Killed, "creature.test.wolf", 3, false)),
        new FlagRequirement(new WorldStateCondition("world_flag.test.open", 1, long.MaxValue)),
        new QuestRequirement(new QuestStateCondition("quest.test.first", null, "completed", false))), 25);

    [Fact]
    public void EveryGate_IsAskedOfTheWorld_WithWhatTheCharacterHasAgainstIt()
    {
        var facts = new Facts { Trust = 9, Killed = 2 };
        facts.Skills["skill.test_blade"] = 14;
        var none = TeachingRules.Check(Lesson, Teacher, facts, _ => false);
        Assert.All(none, c => Assert.False(c.Met));
        Assert.Equal(new long[] { 14, 9, 0, 2, 0, 0 }, none.Select(c => c.Have));

        facts.Skills["skill.test_blade"] = 15;
        facts.Trust = 10;
        facts.Killed = 3;
        facts.Flag = 1;
        facts.Quest = "completed";
        var all = TeachingRules.Check(Lesson, Teacher, facts, id => id == "ability.test.stance");
        Assert.All(all, c => Assert.True(c.Met));
        Assert.Equal(new[] { "skill", "trust", "prerequisite", "deed", "flag", "quest" }, all.Select(c => c.Requirement.Key));
    }

    [Fact]
    public void ATechnique_ChangesTheWeaponsOwnBlow_AndTrainsTheWeaponsSkill()
    {
        var sword = new AttackProfile("item.weapon.test_sword", 6, 9, "physical_slash", 1_600, 6, 3, 5, 8) { SkillId = "skill.test_blade" };
        var strike = new TechniqueDefinition("ability.test.strike", "skill.test_blade", 1.6, 1.5, 800, 22, 120)
        {
            OnHitEffect = "effect.test.bleeding",
            OnHitEffectPercent = 50,
        };

        var blow = TechniqueRules.Profile(sword, strike);

        Assert.Equal(("ability.test.strike", 10, 14, 2_400L, 9, 3, 5, 22), (blow.Source, blow.DamageMin, blow.DamageMax, blow.ReachMm, blow.WindupTicks,
            blow.ActiveTicks, blow.RecoveryTicks, blow.StaminaCost));
        Assert.Equal(("skill.test_blade", "effect.test.bleeding", 50, 120), (blow.SkillId, blow.OnHitEffect, blow.OnHitEffectPercent, blow.CooldownTicks));

        // A bow's technique holds the draw longer and spends its arrow as ever; its reach is the bow's.
        var bow = new AttackProfile("item.weapon.test_bow", 8, 12, "physical_pierce", 40_000, 16, 1, 6, 8)
        {
            Ranged = true,
            AmmoDefId = "item.ammo.test_arrow",
            SkillId = "skill.test_bow",
        };
        var shot = TechniqueRules.Profile(bow, new TechniqueDefinition("ability.test.shot", "skill.test_bow", 1.8, 1.6, 800, 15, 120));
        Assert.Equal((14, 22, 40_000L, 26, true, "item.ammo.test_arrow"), (shot.DamageMin, shot.DamageMax, shot.ReachMm, shot.WindupTicks, shot.Ranged, shot.AmmoDefId));
    }

    [Fact]
    public void ATechniqueOutOfRange_SaysWhy()
    {
        Assert.Null(new TechniqueDefinition("ability.test.a", "skill.test", 1.2, 1, 0, 10, 20).Problem());
        Assert.Contains("windup_multiplier", new TechniqueDefinition("ability.test.a", "skill.test", 1.2, 0.5, 0, 10, 20).Problem());
        Assert.Contains("multiplier_of_weapon", new TechniqueDefinition("ability.test.a", "skill.test", 0, 1, 0, 10, 20).Problem());
    }
}
