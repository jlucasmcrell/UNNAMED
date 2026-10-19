using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>A resolved blow names its actual target; named people never take a creature's article.</summary>
public static class CombatWords
{
    public static string Hit(HitResolved hit, EntityId player, string attacker, bool namedAttacker, string target, string source, bool namedTarget = false)
    {
        string region = BodyRegions.Key(hit.Region);
        string how = (hit.Critical ? ", critical" : "") + (hit.Blocked ? ", blocked" : "") + (hit.Staggered ? ", staggered" : "");
        string targetSubject = namedTarget ? target : $"The {target}";
        string targetObject = namedTarget ? target : $"the {target}";
        string attackerObject = namedAttacker ? attacker : $"the {attacker}";
        string weapon = source.ToLowerInvariant();
        if (hit.Attacker == player)
            return hit.Dodged ? $"{targetSubject} dodges your {weapon}" : $"You hit {targetObject} ({region}) for {hit.Damage}{how}";
        string who = namedAttacker ? attacker : $"The {attacker}";
        if (hit.Target == player)
            return hit.Dodged ? $"You dodge {attackerObject}'s {weapon}"
                : $"{who}'s {weapon} hits your {region} for {hit.Damage}{how}";
        return hit.Dodged ? $"{targetSubject} dodges {attackerObject}'s {weapon}"
            : $"{who}'s {weapon} hits {targetObject} ({region}) for {hit.Damage}{how}";
    }
}
