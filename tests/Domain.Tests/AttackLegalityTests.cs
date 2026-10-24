using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

public class AttackLegalityTests
{
    [Fact]
    public void APlayerCanBeEitherAttackerOrTargetWhenTheContextAllowsIt()
    {
        var first = EntityId.NewId(EntityKind.Character);
        var second = EntityId.NewId(EntityKind.Character);
        var context = new AttackContext(true, true, true);
        Assert.True(CombatRules.CanAttemptAttack(first, second, context));
        Assert.True(CombatRules.CanAttemptAttack(second, first, context));
    }

    [Fact]
    public void AnActorCannotAttackItself()
    {
        var body = EntityId.NewId(EntityKind.Character);
        Assert.False(CombatRules.CanAttemptAttack(body, body, new(true, true, true)));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void DeadParticipantsAndDisabledCombatRefuseAttack(bool attackerAlive, bool targetAlive, bool enabled)
    {
        Assert.False(CombatRules.CanAttemptAttack(EntityId.NewId(EntityKind.Character), EntityId.NewId(EntityKind.Character),
            new(attackerAlive, targetAlive, enabled)));
    }
}
