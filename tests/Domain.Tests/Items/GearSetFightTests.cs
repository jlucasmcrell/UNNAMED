using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Tests.Items;

/// <summary>
/// The owner's 2026-10-18 rulings on gear sets and action bars: in a fight a switch changes the hands only and says the armour stays on;
/// the main bar keeps a layout per weapon family; and the party's "focus my target" tactic is a standing rule with a window.
/// </summary>
public class GearSetFightTests
{
    private static EntityId Item(string tag) => EntityId.NewId(EntityKind.Item);

    private static (GearSet Set, Dictionary<EquipSlot, EntityId> Worn, EntityId Sword, EntityId Bow, EntityId Helm, EntityId Mail) Fixture()
    {
        var sword = Item("sword");
        var bow = Item("bow");
        var helm = Item("helm");
        var mail = Item("mail");
        var set = new GearSet(1, "Archer", ImmutableArray.Create(
            new GearSetItem(EquipSlot.MainHand, bow, "item.weapon.hunting_bow"),
            new GearSetItem(EquipSlot.Head, helm, "item.armor.cap"),
            new GearSetItem(EquipSlot.Chest, mail, "item.armor.jerkin")));
        var worn = new Dictionary<EquipSlot, EntityId> { [EquipSlot.MainHand] = sword };
        return (set, worn, sword, bow, helm, mail);
    }

    [Fact]
    public void OutOfAFight_ASwitchChangesEverything()
    {
        var f = Fixture();
        var plan = GearSetRules.Plan(f.Set, f.Worn, _ => true, inAFight: false);
        Assert.Equal(new[] { EquipSlot.MainHand, EquipSlot.Head, EquipSlot.Chest }, plan.Equip.Select(i => i.Slot));
        Assert.False(plan.ArmourKept);
    }

    [Fact]
    public void InAFight_OnlyTheHandsChange_AndTheArmourIsSkippedNotQueued()
    {
        var f = Fixture();
        var plan = GearSetRules.Plan(f.Set, f.Worn, _ => true, inAFight: true);
        Assert.Equal(new[] { EquipSlot.MainHand }, plan.Equip.Select(i => i.Slot));
        Assert.Empty(plan.Unequip);
        Assert.True(plan.ArmourKept);
        Assert.Empty(plan.Missing);   // skipped is not missing: nothing is reported as not carried
    }

    [Fact]
    public void InAFight_ArmourWornThatTheSetLeavesBare_StaysOn()
    {
        var f = Fixture();
        f.Worn[EquipSlot.Chest] = f.Mail;
        f.Worn[EquipSlot.OffHand] = Item("shield");
        var set = new GearSet(2, "Bare hands, bare chest", ImmutableArray<GearSetItem>.Empty);
        var fight = GearSetRules.Plan(set, f.Worn, _ => true, inAFight: true);
        Assert.Equal(new[] { EquipSlot.MainHand, EquipSlot.OffHand }, fight.Unequip);   // hands first, the chest stays
        Assert.True(fight.ArmourKept);
        var calm = GearSetRules.Plan(set, f.Worn, _ => true, inAFight: false);
        Assert.Contains(EquipSlot.Chest, calm.Unequip);
        Assert.False(calm.ArmourKept);
    }

    [Fact]
    public void InAFight_ASetOfOnlyArmour_ChangesNothingInTheHands_AndSaysSo()
    {
        var f = Fixture();
        var armourOnly = new GearSet(3, "Mail", ImmutableArray.Create(new GearSetItem(EquipSlot.Chest, f.Mail, "item.armor.jerkin")));
        f.Worn[EquipSlot.MainHand] = f.Sword;
        var plan = GearSetRules.Plan(armourOnly, f.Worn, _ => true, inAFight: true);
        Assert.True(plan.ArmourKept);
        Assert.Contains(EquipSlot.MainHand, plan.Unequip);   // the set wears nothing in the hands, so the hands come bare
    }

    [Fact]
    public void TheHands_AreTheMainHandAndTheOffHand_AndNothingElse()
    {
        Assert.All(Enum.GetValues<EquipSlot>(), slot => Assert.Equal(slot is EquipSlot.MainHand or EquipSlot.OffHand, GearSetRules.ChangesInAFight(slot)));
        Assert.Equal("Armour stays on in a fight", GearSetRules.ArmourStays);
    }

    [Theory]
    [InlineData(false, null, "unarmed")]
    [InlineData(true, "skill.blades", "skill.blades")]
    [InlineData(true, null, "other")]
    [InlineData(true, "not a skill", "other")]
    public void AWeaponFamily_IsItsSkill_OrUnarmed_OrOther(bool armed, string? skill, string expected) =>
        Assert.Equal(expected, WeaponFamilies.Of(armed, skill));

    [Fact]
    public void FamilyNames_AreValidated()
    {
        Assert.True(WeaponFamilies.IsValid("unarmed"));
        Assert.True(WeaponFamilies.IsValid("skill.archery"));
        Assert.False(WeaponFamilies.IsValid("item.weapon.sword"));
        Assert.False(WeaponFamilies.IsValid(""));
    }

    [Fact]
    public void FocusMyTarget_IsOff_UnlessAsked_AndKeepsTheOtherRules()
    {
        Assert.False(PartyTactics.Default.FocusTarget);
        var focused = PartyTactics.Default with { FocusTarget = true };
        Assert.True(focused.KeepToLeash);
        Assert.Equal(200, PartyTactics.FocusWindowTicks);
    }
}
