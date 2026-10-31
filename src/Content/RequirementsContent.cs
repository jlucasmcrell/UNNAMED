// UNNAMED Content - the requires block on a technique (PRG-29), read in a file of its own so the techniques lane and this one do not share one
// No Godot references - content only

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Content;

/// <summary>
/// PRG-29: what a technique asks of a character - <c>attribute</c> (a name and a least value), <c>skill</c> (a level), <c>known</c> (another
/// definition), <c>equipment</c> (a carried or worn one), <c>body</c> (a people or lineage), <c>knowledge</c> (a record) or <c>standing</c> (with
/// whom, and how much). A kind the rule cannot answer is refused at load rather than never met in game.
/// </summary>
public static class RequirementsContent
{
    /// <summary>The requirements a technique's own <c>requires</c> block asks for; none when it has no block.</summary>
    public static ImmutableArray<TechniqueRequirement> TechniqueRequirementsOf(Dictionary<object, object> map, string id)
    {
        if (!map.ContainsKey("requires"))
            return ImmutableArray<TechniqueRequirement>.Empty;
        return CombatContent.Rows(map, "requires", id).Select(row =>
        {
            string kind = CombatContent.Text(row, "kind");
            TechniqueRequirementKind? parsed = kind switch
            {
                "skill" => TechniqueRequirementKind.Skill,
                "attribute" => TechniqueRequirementKind.Attribute,
                "known" => TechniqueRequirementKind.Known,
                "equipment" => TechniqueRequirementKind.Equipment,
                "body" => TechniqueRequirementKind.Body,
                "knowledge" => TechniqueRequirementKind.Knowledge,
                "standing" => TechniqueRequirementKind.Standing,
                _ => null,
            };
            if (parsed is not { } named)
                throw new FormatException($"{id}: requires kind '{kind}' is not one of skill, attribute, known, equipment, body, knowledge, standing");

            string? reference = kind switch
            {
                "skill" => row.GetValueOrDefault("skill_ref") as string,
                "known" => row.GetValueOrDefault("technique_ref") as string,
                "equipment" => row.GetValueOrDefault("item_ref") as string,
                "body" => row.GetValueOrDefault("people_ref") as string,
                "knowledge" => row.GetValueOrDefault("knowledge_ref") as string,
                "standing" => row.GetValueOrDefault("npc_ref") as string,
                _ => CombatContent.Text(row, "attribute"),
            };
            int min = row.ContainsKey("min") ? CombatContent.Int(row, "min") : 1;
            var requirement = new TechniqueRequirement(named, reference, min);
            if (requirement.Problem() is { } problem)
                throw new FormatException($"{id}: {problem}");
            return requirement;
        }).ToImmutableArray();
    }
}
