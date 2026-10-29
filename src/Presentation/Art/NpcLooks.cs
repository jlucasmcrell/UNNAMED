// UNNAMED Presentation - every NPC's look: the cast's authored record, and the residents' generated one (F1 Gate C)
// Godot-free: plain values, so the tests read the same generator the game draws with

using System.Collections.Immutable;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Social;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.World;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// Every NPC's look (F1 Gate C). A named cast member wears the record their own file authors, held to their people's ranges; a role
/// slot's residents wear one generated from the slot's own identity - its settlement, its id, its arrival and the person's people and
/// sex - and never from the world seed, because a face is part of who someone is: the same save, and another world, give the same person
/// the same face. The faces a town's slots wear are assigned rather than drawn, so no two residents of one town share one.
/// </summary>
public static class NpcLooks
{
    /// <summary>
    /// The stride an arrival moves a town's faces by. Not a multiple of 83 (or of either sex's count of them), so the newcomer in a slot
    /// differs from the one before, and the town's other slots stay distinct from each other.
    /// </summary>
    private const int GenerationStep = 37;

    /// <summary>The footwear a resident is drawn in, dyed per person (<see cref="LookSwatches.Shirts"/> and trousers).</summary>
    private const string ResidentFootwear = "footwear.outdoor.garden_boot";

    /// <summary>
    /// The wardrobe a role wears. The HumGen wardrobe is modern day-wear, so each role takes the set whose silhouette reads as its
    /// trade: the rugged outdoor set for the people who work outside, the work smock for the cook and the smith, the long coat for a
    /// steward and the scholar's robe for the scholar. The role is the slot id's last segment (`routine.<town>.<role>`).
    /// </summary>
    public static string RoleOutfit(string role,string? sex=null) => role switch
    {
        // A frontier waystation wears period garb selected for each trade.
        // Modern clothing belongs to Otherborn arrivals, which these towns do not have.
        "guard" => "outfit.period.guard",
        "scholar" => "outfit.period.robe",
        "steward" or "quartermaster" or "guide" => "outfit.period.coat",
        "cook" => "outfit.period.apron",
        "smith" or "craftsperson" => "outfit.period.smith_apron",
        "hunter" or "trapper" => "outfit.period.leathers",
        "villager" when sex=="f" => "outfit.period.dress",
        _ => "outfit.period.tunic",
    };

    /// <summary>The role a slot id names, which is what its wardrobe is chosen from.</summary>
    public static string RoleOf(string slotId) => slotId[(slotId.LastIndexOf('.') + 1)..];

    /// <summary>The channel every generated look is addressed by: the settlement, the slot and the arrival, and nothing else.</summary>
    private const string Channel = "npc_looks";

    /// <summary>
    /// One resident's look: <paramref name="position"/> is their slot's place in their town's roster, in ordinal order, which is what
    /// assigns the face (<see cref="Residents"/>, and the caller that owns the roster).
    /// </summary>
    public static CharacterAppearance Resident(string slotId, string settlement, int generation, string people, string sex,
        CreatorCatalogue catalogue, HumGenCreatorData data, int position)
    {
        var faces = catalogue.Options(CreatorChoice.Face, sex);
        if (faces.Count == 0)
            throw new InvalidOperationException($"the catalogue offers no face to a '{sex}' resident");
        var channel = RngChannel.Open(0, default, Channel, $"{settlement}/{slotId}#{generation}");
        var race = catalogue.Race(people);
        // Use only garments offered for this sex by the installed catalogue.
        string outfit = RoleOutfit(RoleOf(slotId),sex);
        if (catalogue.Options(CreatorChoice.Outfit, sex).All(option => option.Id != outfit))
            outfit = "outfit.period.tunic";
        var look = data.Randomize(new CharacterAppearance(race?.MidStatureMm ?? 1750, null, null, null, outfit, ResidentFootwear),
            catalogue, people, sex, channel.UInt64(0));
        // The face is assigned: a town's slots take consecutive presets, and adding a constant to a position is a bijection, so a town of
        // at most a dozen slots gives no two of them one preset (41 faces for a woman, 42 for a man).
        string face = faces[(int)((TownOffset(settlement) + (uint)position + (uint)generation * GenerationStep) % (uint)faces.Count)].Id;
        look = catalogue.WithFace(look, face);
        // A Siann's hair is long (RACES.md) as well as platinum-white under the shader, so a Siann draws from the long styles only; a Kal's
        // is taken away again by the people's own rule below, and the rest of the village wears whatever the draw gives them.
        var styles = catalogue.Options(CreatorChoice.Hair, sex)
            .Where(s => people != PeopleLooks.Siann || s.Id.StartsWith("hair.long.", StringComparison.Ordinal)).ToList();
        var beards = catalogue.Options(CreatorChoice.FacialHair, sex);
        string style = styles[channel.Int(1, 0, styles.Count)].Id;
        look = look with
        {
            Hair = style,
            // Most of a village is clean-shaven; the people's own rule takes the beard away where nobody wears one.
            FacialHair = beards.Count == 0 || channel.Int(2, 0, 3) > 0 ? null : beards[channel.Int(3, 0, beards.Count)].Id,
        };
        // An age of their own, inside the band their people allows: WithFace cleared the preset's, so the preset's own age would return.
        var age = catalogue.Sliders.First(s => s.Id == "face.age");
        if (data.Allowed(people, age, sex) is { } band)
            look = look with { Sliders = look.Sliders.SetItem(age.Id, channel.Int(4, band.Min, band.Max + 1)) };
        // Their own earth dye: without it every resident fell to the swatch default and a town read as one uniform.
        look = look with
        {
            Sliders = look.Sliders
                .SetItem(LookSwatches.ShirtSlider, channel.Int(5, 0, LookSwatches.Shirts.Length))
                .SetItem(LookSwatches.TrouserSlider, channel.Int(6, 0, LookSwatches.Trousers.Length)),
        };
        return data.Constrain(look, catalogue, people);
    }

    /// <summary>
    /// A town's roster: one look per slot, in ordinal order, so the face assignment is a function of the roster alone. The slots name the
    /// people and sex each slot's residents come from (content's <c>people_ref</c> and <c>sex</c>).
    /// </summary>
    public static ImmutableDictionary<string, CharacterAppearance> Residents(IEnumerable<(string SlotId, string People, string Sex)> slots,
        string settlement, CreatorCatalogue catalogue, HumGenCreatorData data) =>
        slots.OrderBy(s => s.SlotId, StringComparer.Ordinal)
            .Select((slot, i) => (slot.SlotId, Look: Resident(slot.SlotId, settlement, 0, slot.People, slot.Sex, catalogue, data, i)))
            .ToImmutableDictionary(s => s.SlotId, s => s.Look, StringComparer.Ordinal);

    /// <summary>
    /// The look one NPC wears: the cast member's authored record, held to their people (already constrained where it was authored), or a
    /// role slot's generated one. Null for a person with no people to draw them by.
    /// </summary>
    public static CharacterAppearance? Of(NpcDefinition definition, string settlement, int generation, string? people, string? sex,
        CreatorCatalogue catalogue, HumGenCreatorData data, int position)
    {
        if (definition.Appearance is { } authored)
            return people is null ? authored : data.Constrain(authored, catalogue, people);
        return people is null || sex is null ? null : Resident(definition.Id, settlement, generation, people, sex, catalogue, data, position);
    }

    /// <summary>FNV-1a over a settlement's ID, as the repository's other stable selections hash: a town's own offset into the presets.</summary>
    private static uint TownOffset(string settlement)
    {
        uint hash = 2_166_136_261;
        foreach (char c in settlement)
            hash = (hash ^ c) * 16_777_619;
        return hash;
    }
}
