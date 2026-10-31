// UNNAMED World - the character as a technique requirement reads them (PRG-29)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

/// <summary>
/// What the character is, gathered once for the requirement record (PRG-29): attributes and skills as the progression rules value them, what is
/// known, what is in hand or worn, the body the character was born in, the knowledge records they hold, and standing with anyone.
/// </summary>
internal static class TechniqueCandidates
{
    public static TechniqueCandidate Of(RuntimeState state, SimulationSetup setup)
    {
        var rules = setup.Progression;
        var attributes = ImmutableSortedDictionary.CreateBuilder<CharacterAttribute, int>();
        foreach (CharacterAttribute attribute in Enum.GetValues<CharacterAttribute>())
            attributes[attribute] = ProgressionEngine.AttributeValue(state.Progression, attribute, rules);

        var skills = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (string skill in state.Progression.Skills.Keys)
            skills[skill] = ProgressionEngine.SkillLevel(state.Progression, skill);

        return new TechniqueCandidate(
            Skills: skills.ToImmutable(),
            Attributes: attributes.ToImmutable(),
            Known: state.Progression.Known.Keys.ToImmutableSortedSet(StringComparer.Ordinal),
            // In hand and worn are equipment slots holding item instances; a requirement asks what kind of thing it is.
            Equipment: state.Equipment.Values
                .Select(instanceId => state.Inventory.FirstOrDefault(e => e.ItemId == instanceId).DefId)
                .Where(defId => defId is not null)
                .Select(defId => defId!)
                .ToImmutableSortedSet(StringComparer.Ordinal),
            Body: state.PeopleId,
            Knowledge: state.Progression.NoveltyFirsts,
            Standing: state.Relationships.ToImmutableSortedDictionary(e => e.Key, e => e.Value.GetValueOrDefault("trust"), StringComparer.Ordinal));
    }

    /// <summary>The requirements one definition asks for, or none when it has no entry (an unknown id asks nothing rather than failing).</summary>
    public static ImmutableArray<TechniqueRequirement> RequirementsOf(SimulationSetup setup, string definitionId) =>
        setup.Techniques.TryGetValue(definitionId, out var technique) ? technique.Requirements : ImmutableArray<TechniqueRequirement>.Empty;
}
