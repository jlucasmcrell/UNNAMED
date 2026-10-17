// UNNAMED World - techniques at run time: a known active technique, used with a weapon of its family (WP9; D11; SYSTEMS.md S-10)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Use a known technique (D11: one active technique per weapon family): the weapon in hand makes the technique's blow, if it is of the
/// technique's family. What triggers it - a quick-bar slot - is presentation's (WP9, local).
/// </summary>
public sealed record TechniqueCommand(EntityId Actor, string TechniqueId) : GameCommand(Actor);

/// <summary>A technique was begun with this weapon (its <see cref="AttackStarted"/> follows under the technique's name); ready again at <paramref name="ReadyTick"/>.</summary>
public sealed record TechniqueUsed(EntityId Actor, string TechniqueId, string WeaponDefId, long ReadyTick, long Tick);

// ── techniques: the player's blows, so the combat system's ─────────────────

internal sealed partial class CombatSystem
{
    /// <summary>
    /// Begin a technique: the weapon's attack as the technique changes it, then the blow runs as any attack does - the same phases, the
    /// same pipeline, the same skill trained by what it wounds. Refused unknown, with no weapon of its family in hand, before its cooldown
    /// is out, without the stamina (or a bow's arrow), and while busy.
    /// </summary>
    public string? Handle(TechniqueCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!_context.Setup.Techniques.TryGetValue(command.TechniqueId, out var technique))
            return $"{command.TechniqueId} is not a technique this build knows";
        if (!ProgressionEngine.Knows(State.Progression, technique.Id))
            return $"{technique.Id} is not known";
        var combat = State.PlayerCombat;
        if (Busy(combat.Action, tick) is { } busy)
            return busy;
        var weapon = PlayerAttack();
        if (weapon.SkillId != technique.SkillId)
            return $"{technique.Id} needs a {technique.SkillId} weapon in hand";
        if (combat.TechniquesReadyAt.TryGetValue(technique.Id, out long ready) && tick < ready)
            return $"{technique.Id} is not ready ({ready - tick} ticks)";
        var attack = TechniqueRules.Profile(weapon, technique);
        if (attack.Ranged && attack.AmmoDefId is { } ammo && !State.Inventory.Any(e => e.DefId == ammo))
            return $"no {ammo} to shoot";
        if (Stamina() < attack.StaminaCost)
            return $"too tired for {technique.Id}";
        Exert(attack.StaminaCost, tick);
        long readyAt = tick + technique.CooldownTicks;
        State.SetPlayerCombat(_owner, State.PlayerCombat with
        {
            Action = ActionState.Begin(ActionKind.Attack, tick, attack),
            Blocking = false,
            TechniquesReadyAt = State.PlayerCombat.TechniquesReadyAt.SetItem(technique.Id, readyAt),
        });
        _context.Events.Publish(new TechniqueUsed(_player, technique.Id, weapon.Source, readyAt, tick));
        _context.Events.Publish(new AttackStarted(_player, attack.Source, attack.WindupTicks, attack.ActiveTicks, attack.RecoveryTicks, tick));
        return null;
    }

    /// <summary>Ticks until a technique is ready again; 0 when it is ready now (the quick bar's cooldown sweep).</summary>
    public int TechniqueCooldown(string techniqueId, long tick) =>
        State.PlayerCombat.TechniquesReadyAt.TryGetValue(techniqueId, out long ready) && ready > tick ? (int)(ready - tick) : 0;
}
