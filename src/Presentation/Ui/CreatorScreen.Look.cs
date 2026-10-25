// UNNAMED Presentation - the creator's look: height and body shape, Face, Hair and Outfit, as the HumGen pipeline can make them
// Godot presentation only: the choices are humgen_creator.json's (Ui/Models/CreatorCatalogue); the draft keeps them by ID and number, and a
// combination no baked body has is said to be pending bake, never drawn as if it were there (the preview stays on the baked body)

using Godot;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Ui;

public partial class CreatorScreen
{
    private const string CataloguePath = "res://Art/humgen_creator.json";

    /// <summary>A section's long lists show one group at a time: the group chosen in each, by section.</summary>
    private readonly Dictionary<string, string> _groups = new(StringComparer.Ordinal);

    private CreatorCatalogue _catalogue = CreatorCatalogue.Empty;
    private Label? _pendingWords;

    /// <summary>
    /// An exported build offers only what it draws (public playtest gate A4): the baked bodies' own choices, no sliders a bake has not caught
    /// up with, and no people without a baked body. The editor keeps the whole catalogue for the pipeline's work.
    /// </summary>
    private static bool BakedOnly => OS.HasFeature("template");

    /// <summary>What a player is told in place of the choices this build cannot draw yet.</summary>
    private const string LaterWords = "More looks come in later playtests.";

    private static CreatorCatalogue ReadCatalogue()
    {
        if (!Godot.FileAccess.FileExists(CataloguePath))
        {
            GD.PushError($"UNNAMED creator: {CataloguePath} is missing; the look steps offer nothing");
            return CreatorCatalogue.Empty;
        }
        return CreatorCatalogue.Read(Godot.FileAccess.GetFileAsString(CataloguePath));
    }

    /// <summary>The draft with this body: its look starts again from that body as baked (a look is per body, its options per sex).</summary>
    private CreatorDraft WithBody(string id) => Draft with { BodyPresetId = id, Appearance = id == Draft.BodyPresetId ? Draft.Appearance : null };

    private string? Model => OfferedBodies().FirstOrDefault(o => o.Id == Draft.BodyPresetId)?.Model;

    /// <summary>The body's sex as HumGen's content folders split it: the baked body's own, else the body type's (<c>…_f</c>).</summary>
    private string Sex => _catalogue.BakedFor(Model)?.Sex
        ?? (OfferedBodies().FirstOrDefault(o => o.Id == Draft.BodyPresetId)?.Body.Type.EndsWith("_f", StringComparison.Ordinal) == true ? "f" : "m");

    /// <summary>The look as it stands: the draft's, or the body's as baked.</summary>
    private CharacterAppearance Look => Draft.Appearance ?? _catalogue.Start(Model, Draft.PeopleId);

    /// <summary>Change the look; a look back at exactly the body as baked is kept as none, so the draft says "as baked".</summary>
    private void Shape(Func<CharacterAppearance, CharacterAppearance> change)
    {
        var look = change(Look);
        _draft = Draft with { Appearance = look.Equals(_catalogue.Start(Model, Draft.PeopleId)) ? null : look };
        PendingState();
    }

    // ── the steps ───────────────────────────────────────────────────────────

    /// <summary>Step 3: the face preset HumGen starts from, then age and skin, then the face's proportions.</summary>
    private void FaceStep()
    {
        _body.AddChild(PendingNote());
        _body.AddChild(OptionSection(CreatorChoice.Face, "Face", "a HumGen starting face; choosing one resets the face sliders to it"));
        _body.AddChild(SliderSection("face", "Features", "age, skin and proportions"));
    }

    /// <summary>Step 4: head hair, facial hair where HumGen has it for this body, and the hair's colour.</summary>
    private void HairStep()
    {
        _body.AddChild(PendingNote());
        _body.AddChild(OptionSection(CreatorChoice.Hair, "Hair", "HumGen hair cards"));
        _body.AddChild(SwatchSection("Hair colour", LookSwatches.HairSlider, LookSwatches.Hair(Draft.PeopleId), new Color(.08f, .035f, .012f), multiply: false));
        if (_catalogue.Options(CreatorChoice.FacialHair, Sex).Count > 0)
            _body.AddChild(OptionSection(CreatorChoice.FacialHair, "Facial hair", "", none: "Clean-shaven"));
        _body.AddChild(SliderSection("hair", "Colour", ""));
    }

    /// <summary>Step 5: the outfit and the footwear, with the gear fit family the people's frame takes (RACE_SCALE_SPEC.md §8).</summary>
    private void OutfitStep()
    {
        _body.AddChild(PendingNote());
        if (_catalogue.Race(Draft.PeopleId) is { } race && !BakedOnly)
            _body.AddChild(UiKit.Words($"Fit family: {race.FitFamily.Replace('_', ' ')}. What you start in is fitted to this body; gear found later is fitted by crafters.", 16, HudStyle.Muted));
        _body.AddChild(OptionSection(CreatorChoice.Outfit, "Outfit", "fitted by HumGen"));
        _body.AddChild(OptionSection(CreatorChoice.Footwear, "Footwear", ""));
    }

    /// <summary>The look's lines on Confirm: height, then each choice by its name.</summary>
    private IEnumerable<(string Caption, string Words)> LookLines()
    {
        if (_catalogue.Baked.Length == 0)
            yield break;
        var look = Look;
        string Name(CreatorChoice choice, string? id, string none) => _catalogue.Option(choice, id)?.Label ?? none;
        yield return ("Height", Metres(look.StatureMm));
        yield return ("Face", Name(CreatorChoice.Face, look.Face, "the body's own"));
        yield return ("Hair", Name(CreatorChoice.Hair, look.Hair, "none") + (look.FacialHair is { } beard ? ", " + Name(CreatorChoice.FacialHair, beard, beard) : ""));
        yield return ("Outfit", $"{Name(CreatorChoice.Outfit, look.Outfit, "none")}, {Name(CreatorChoice.Footwear, look.Footwear, "barefoot").ToLowerInvariant()}");
    }

    // ── pending bake ────────────────────────────────────────────────────────

    /// <summary>What the preview is: exactly the character, or the baked body with these parts of the look still to bake.</summary>
    private Control PendingNote()
    {
        if (BakedOnly)
            return new Control();
        _pendingWords = UiKit.Words(string.Empty, 16, HudStyle.Ember);
        _pendingWords.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UiKit.Id(_pendingWords, "creator.pending");
        PendingState();
        return _pendingWords;
    }

    /// <summary>The pending note, kept in place while a slider is dragged (the slider keeps the focus).</summary>
    private void PendingState()
    {
        if (_pendingWords is null)
            return;
        var pending = _catalogue.Pending(Draft.Appearance, Model, Draft.PeopleId);
        _pendingWords.Text = pending.Count == 0
            ? "As baked: the preview is exactly this character."
            : _catalogue.BakedFor(Model) is null
                ? $"Pending bake: {string.Join(", ", pending)}. This build draws no baked HumGen body here, so the preview is a stand-in; your choices are kept with the character for the bake."
                : $"Pending bake: {string.Join(", ", pending)}. The preview shows the baked body; your choices are kept with the character for the bake.";
        _pendingWords.AddThemeColorOverride("font_color", pending.Count == 0 ? HudStyle.Lichen : HudStyle.Ember);
    }

    private static PanelContainer PendingChip() => UiKit.Chip("Pending bake", HudStyle.Ember);

    private static PanelContainer BakedChip() => UiKit.Chip("Baked", HudStyle.Lichen);

    /// <summary>A people the creator cannot offer yet, with why: not playable in this build's content, or its body not made by HumGen as shipped.</summary>
    private Control PendingRace(CreatorRace race)
    {
        var row = UiKit.ListRow("creator.people." + race.People, race.Label, $"{race.Epithet} · {Metres(race.MinStatureMm)}-{Metres(race.MaxStatureMm)}",
            HudStyle.Muted, height: 48);
        row.Button.Disabled = true;
        if (BakedOnly)
        {
            row.Button.TooltipText = "Comes in a later playtest.";
            row.Right.AddChild(UiKit.Chip("Later playtest", HudStyle.Iron, HudStyle.Ash));
            return row.Button;
        }
        row.Button.TooltipText = race.HumgenBody ? "Not playable in this build." : "Its body needs its own morphology before HumGen can bake it.";
        row.Right.AddChild(race.HumgenBody ? UiKit.Chip("Not playable yet", HudStyle.Iron, HudStyle.Ash) : PendingChip());
        return row.Button;
    }

    // ── sections ────────────────────────────────────────────────────────────

    /// <summary>Height over the people's canon stature range (RACES.md), in centimetres; every scale derives from it (RACE_SCALE_SPEC.md §1).</summary>
    private Control HeightSection()
    {
        if (BakedOnly)
            return UiKit.Words($"Height {Metres(Look.StatureMm)}. {LaterWords}", 16, HudStyle.Muted);
        var race = _catalogue.Race(Draft.PeopleId);
        int min = race?.MinStatureMm ?? CharacterAppearance.MinStatureMm, max = race?.MaxStatureMm ?? CharacterAppearance.MaxStatureMm;
        var section = UiKit.Section("Height", race is null ? "" : $"{race.Label}: {Metres(min)}-{Metres(max)}");
        section.AddChild(SliderRow("creator.height", "Height", min, max, 10, Math.Clamp(Look.StatureMm, min, max), Metres,
            v => Shape(look => look with { StatureMm = v })));
        return section;
    }

    /// <summary>
    /// A row of tones (<see cref="LookSwatches"/>), each a chip of the colour it gives over <paramref name="baked"/> (the body's baked colour,
    /// scaled for a skin tone, added to for hair) and its name; the choice is kept as the slider's index, the first one as no slider at all.
    /// </summary>
    private Control SwatchSection(string heading, string slider, IReadOnlyList<Swatch> swatches, Color baked, bool multiply)
    {
        var section = UiKit.Section(heading);
        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        int chosen = Look.Sliders.TryGetValue(slider, out int at) ? at : 0;
        for (int i = 0; i < swatches.Count; i++)
        {
            var swatch = swatches[i];
            int index = i;
            string id = $"creator.{slider}.{i}";
            var button = UiKit.Action(swatch.Label, Deferred(() =>
            {
                Shape(look => look with { Sliders = index == 0 ? look.Sliders.Remove(slider) : look.Sliders.SetItem(slider, index) });
                Render(id);
            }), compact: true, id: id);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var chip = new ColorRect
            {
                Color = multiply ? new Color(Math.Min(1, baked.R * swatch.R), Math.Min(1, baked.G * swatch.G), Math.Min(1, baked.B * swatch.B))
                    : new Color(Math.Min(1, baked.R + swatch.R), Math.Min(1, baked.G + swatch.G), Math.Min(1, baked.B + swatch.B)),
                CustomMinimumSize = new Vector2(14, 14), MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            chip.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
            chip.Position = new Vector2(6, -7);
            button.AddChild(chip);
            UiKit.Select(button, index == chosen);
            grid.AddChild(button);
        }
        section.AddChild(grid);
        return section;
    }

    /// <summary>A panel's sliders (body, face or hair), one group at a time behind a row of group tabs.</summary>
    private Control SliderSection(string panel, string heading, string note)
    {
        var sliders = _catalogue.Sliders.Where(s => s.Panel == panel).ToList();
        if (BakedOnly)
            return new Control();
        var section = UiKit.Section(heading, note);
        if (sliders.Count == 0)
        {
            section.AddChild(UiKit.Words("The HumGen catalogue is not in this build.", 16, HudStyle.Muted));
            return section;
        }
        string key = "sliders." + panel;
        string group = Group(key, sliders.Select(s => s.Group), null);
        section.AddChild(GroupTabs(key, sliders.Select(s => s.Group).Distinct().ToList(), group));
        var look = Look;
        foreach (var slider in sliders.Where(s => s.Group == group))
        {
            var shown = slider;
            section.AddChild(SliderRow("creator.slider." + slider.Id, slider.Label, slider.Min, slider.Max, 1, _catalogue.Value(look, slider),
                v => SliderWords(shown, v), v => Shape(l => _catalogue.WithSlider(l, shown, v))));
        }
        return section;
    }

    /// <summary>A list of choices of one kind for this body's sex, grouped behind tabs when it is long, each marked baked or pending bake.</summary>
    private Control OptionSection(CreatorChoice choice, string heading, string note, string? none = null)
    {
        var options = _catalogue.Options(choice, Sex);
        if (BakedOnly)
            options = options.Where(o => _catalogue.IsBaked(choice, o.Id, Model)).ToList();
        var section = UiKit.Section(heading, BakedOnly ? "" : note);
        if (options.Count == 0)
        {
            section.AddChild(UiKit.Words(BakedOnly ? LaterWords : "The HumGen catalogue is not in this build.", 16, HudStyle.Muted));
            return section;
        }
        string? chosen = choice switch
        {
            CreatorChoice.Face => Look.Face,
            CreatorChoice.Hair => Look.Hair,
            CreatorChoice.FacialHair => Look.FacialHair,
            CreatorChoice.Outfit => Look.Outfit,
            _ => Look.Footwear,
        };
        string key = "options." + choice;
        var shown = options;
        if (options.Count > 12)
        {
            string group = Group(key, options.Select(o => o.Group), _catalogue.Option(choice, chosen)?.Group);
            section.AddChild(GroupTabs(key, options.Select(o => o.Group).Distinct().ToList(), group));
            shown = options.Where(o => o.Group == group).ToList();
        }
        string kind = choice.ToString().ToLowerInvariant();
        if (none is not null && (!BakedOnly || _catalogue.IsBaked(choice, null, Model)))
            section.AddChild(OptionRow($"creator.{kind}.none", none, chosen is null, _catalogue.IsBaked(choice, null, Model), () => Choose(choice, null)));
        foreach (var option in shown)
        {
            string id = option.Id;
            section.AddChild(OptionRow($"creator.{kind}.{id}", option.Label, chosen == id,
                _catalogue.IsBaked(choice, id, Model), () => Choose(choice, id)));
        }
        if (BakedOnly)
            section.AddChild(UiKit.Words(LaterWords, 16, HudStyle.Muted));
        return section;
    }

    private Control OptionRow(string id, string name, bool selected, bool baked, Action pressed)
    {
        var row = UiKit.ListRow(id, name, height: 40);
        if (!BakedOnly)
            row.Right.AddChild(baked ? BakedChip() : PendingChip());
        UiKit.Select(row.Button, selected);
        row.Button.Pressed += Deferred(() =>
        {
            pressed();
            Render(id);
        });
        return row.Button;
    }

    private void Choose(CreatorChoice choice, string? id) => Shape(look => choice switch
    {
        CreatorChoice.Face => _catalogue.WithFace(look, id!),
        CreatorChoice.Hair => look with { Hair = id },
        CreatorChoice.FacialHair => look with { FacialHair = id },
        CreatorChoice.Outfit => look with { Outfit = id },
        _ => look with { Footwear = id },
    });

    /// <summary>The group a section shows: the one last chosen there, else the chosen option's, else the first.</summary>
    private string Group(string key, IEnumerable<string> groups, string? chosen)
    {
        var all = groups.Distinct().ToList();
        if (_groups.TryGetValue(key, out var kept) && all.Contains(kept))
            return kept;
        return chosen is not null && all.Contains(chosen) ? chosen : all[0];
    }

    private Control GroupTabs(string key, IReadOnlyList<string> groups, string chosen)
    {
        var tabs = new HFlowContainer();
        tabs.AddThemeConstantOverride("h_separation", 6);
        tabs.AddThemeConstantOverride("v_separation", 6);
        foreach (string group in groups)
        {
            string id = $"creator.{key}.{group.ToLowerInvariant().Replace(' ', '_')}";
            var tab = UiKit.Action(group, Deferred(() =>
            {
                _groups[key] = group;
                Render(id);
            }), compact: true, id: id);
            UiKit.Select(tab, group == chosen);
            tabs.AddChild(tab);
        }
        return tabs;
    }

    /// <summary>A labelled slider with its value beside it; dragging changes the draft without drawing the step again.</summary>
    private static Control SliderRow(string id, string name, int min, int max, int step, int value, Func<int, string> shown, Action<int> set)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var label = UiKit.Caption(name);
        label.CustomMinimumSize = new Vector2(190, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(label);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value, FocusMode = Control.FocusModeEnum.All,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 24),
        };
        UiKit.Id(slider, id);
        row.AddChild(slider);
        var words = HudStyle.Text(HudStyle.Strong, 17, HudStyle.Bone);
        words.Text = shown(value);
        words.CustomMinimumSize = new Vector2(72, 0);
        words.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(words);
        slider.ValueChanged += v =>
        {
            words.Text = shown((int)v);
            set((int)v);
        };
        return row;
    }

    /// <summary>A slider's value in HumGen's own terms: years for age, else the key's value.</summary>
    private static string SliderWords(CreatorSlider slider, int value) =>
        slider.Humgen == "age" ? $"{value} yr" : (value * slider.Scale).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static string Metres(int mm) => (mm / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m";
}
