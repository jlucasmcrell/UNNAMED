using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

public class CombatWordsTests
{
    [Fact]
    public void CreatureMoveUsesItsOwnWordsAfterTheAttackersName()
    {
        var player = EntityId.NewId(EntityKind.Character);
        var alpha = EntityId.NewId(EntityKind.Creature);
        var hit = new HitResolved(alpha, "creature.beast.wolf_grey_alpha", player, "ability.creature.wolf_grey_alpha_rush",
            BodyRegion.Torso, 12, false, false, false, false, 40, 100);
        Assert.Equal("The Grey Alpha's rush hits your torso for 12",
            CombatWords.Hit(hit, player, "Grey Alpha", false, "", "Rush"));
    }

    [Fact]
    public void CompanionsBlowNamesTheWolf_AndOnlyAPlayerTargetSaysYourTorso()
    {
        var player = EntityId.NewId(EntityKind.Character);
        var tavar = EntityId.NewId(EntityKind.Npc);
        var wolf = EntityId.NewId(EntityKind.Creature);
        var hit = new HitResolved(tavar, "npc.tavar", wolf, "march_spear", BodyRegion.Torso, 12,
            false, false, false, false, 40, 100);
        string Words(HitResolved e) => CombatWords.Hit(e, player, "Tavar Orr", true, "Grey Wolf", "March Spear");
        Assert.Equal("Tavar Orr's march spear hits the Grey Wolf (torso) for 12", Words(hit));
        Assert.Equal("Tavar Orr's march spear hits your torso for 12", Words(hit with { Target = player }));
        Assert.Equal("The Grey Wolf dodges Tavar Orr's march spear", Words(hit with { Dodged = true }));
        Assert.Equal("You dodge Tavar Orr's march spear", Words(hit with { Target = player, Dodged = true }));
        Assert.Equal("You hit the Grey Wolf (torso) for 12", Words(hit with { Attacker = player }));
        Assert.Equal("The Grey Wolf dodges your march spear", Words(hit with { Attacker = player, Dodged = true }));
        Assert.Equal("The Grey Wolf's bite hits your torso for 12",
            CombatWords.Hit(hit with { Attacker = wolf, Target = player }, player, "Grey Wolf", false, "", "Bite"));
        Assert.Equal("The Grey Wolf's bite hits Tavar Orr (torso) for 12",
            CombatWords.Hit(hit with { Attacker = wolf, Target = tavar }, player, "Grey Wolf", false, "Tavar Orr", "Bite", true));
    }
}
