// UNNAMED Presentation - the creator's people cards: who a people is, what their body does, how they learn, their racial abilities and the
// trade-off (RACES.md; RACE_ABILITIES_PROPOSAL.md, owner-approved 2026-10-24)
// Godot presentation only: content's card and traits, worded; the domain decides what a character of them is

using Godot;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Ui;

public partial class CreatorScreen
{
    /// <summary>
    /// A people's card, under its row: the chosen playable people's in full, with the free attribute point to place if it has one; a people
    /// not open yet (owner, 2026-10-24: greyed, with its card) in muted words, so the player sees what is coming.
    /// </summary>
    private void PeopleCard(VBoxContainer section, PeopleDefinition people, bool chosen)
    {
        var tone = chosen ? HudStyle.Ash : HudStyle.Muted;
        if (people.Card is { } card)
        {
            section.AddChild(UiKit.Words(card.Summary, chosen ? 17 : 15, tone));
            if (!chosen)
            {
                section.AddChild(UiKit.Words($"{card.LeansToward} {card.TradeOff}", 15, HudStyle.Muted));
                if (!people.Abilities.IsEmpty)
                    section.AddChild(UiKit.Words("Abilities: " + string.Join(", ", _session.Characters.AbilitiesOf(people.Id).Select(a => a.Name)), 15, HudStyle.Muted));
                return;
            }
            section.AddChild(Line("Body", card.Body));
            section.AddChild(Line("Leans toward", card.LeansToward));
        }
        if (!chosen)
            return;
        foreach (var (caption, words) in PeopleWords.Traits(people.Traits))
            section.AddChild(Line(caption, words));
        foreach (var ability in _session.Characters.AbilitiesOf(people.Id))
            section.AddChild(Line(ability.Name, ability.Description));
        if (people.Card is { } trade)
            section.AddChild(UiKit.Callout(HudStyle.Ember, trade.TradeOff));
        if (people.Traits.FreeAttributePoints > 0)
            section.AddChild(FreeAttributeRow(people.Traits));
    }

    /// <summary>Where the people's free point goes (Veth: +1 to any attribute), the people's default until the player picks.</summary>
    private Control FreeAttributeRow(PeopleTraits traits)
    {
        var box = new VBoxContainer();
        box.AddChild(UiKit.Caption($"Place +{traits.FreeAttributePoints}"));
        var live = _session.Setup.Progression.Derived.Live;
        box.AddChild(UiKit.Words($"In play today: {string.Join(", ", live.Select(a => Capital(ProgressionKeys.Key(a))))}. The others count from when they arrive.", 15, HudStyle.Muted));
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        var chosen = Draft.FreeAttribute ?? traits.FreeAttributeDefault;
        foreach (var attribute in ProgressionKeys.Attributes)
        {
            string key = ProgressionKeys.Key(attribute);
            var button = UiKit.Action(Capital(key), Deferred(() =>
            {
                _draft = Draft with { FreeAttribute = attribute };
                Render("creator.free." + key);
            }), id: "creator.free." + key);
            UiKit.Select(button, attribute == chosen);
            row.AddChild(button);
        }
        box.AddChild(row);
        return box;
    }
}
