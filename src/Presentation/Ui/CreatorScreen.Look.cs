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
    private HumGenCreatorData? _humgen;
    private HumGenCreatorData NativeData => _humgen ??= new(Godot.FileAccess.GetFileAsString(CataloguePath));
    private string? NativeBase(string sex) => NativeData.Available && _art.RiggedFile(NativeData.Base(sex), "creator_ready.json") is not null ? NativeData.Base(sex) : null;
    private bool NativeActive => Model?.StartsWith("humgen_creator_", StringComparison.Ordinal) == true;
    private Label? _pendingWords;

    /// <summary>
    /// An exported build offers only what it draws (public playtest gate A4): the baked bodies' own choices, no sliders a bake has not caught
    /// up with, and no people without a baked body. The editor keeps the whole catalogue for the pipeline's work (<c>--public-creator</c>
    /// shows it the exported way).
    /// </summary>
    private bool BakedOnly => !NativeActive && (OS.HasFeature("template") || OS.GetCmdlineUserArgs().Contains("--public-creator"));

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
    private CreatorDraft WithBody(string id)
    {
        if (!_session.Characters.Bodies.TryGetValue(id, out var body)) return Draft with { BodyPresetId = id, Appearance = null };
        string sex = body.Type.EndsWith("_f", StringComparison.Ordinal) ? "f" : "m";
        if (NativeBase(sex) is null) return Draft with { BodyPresetId = id, Appearance = id == Draft.BodyPresetId ? Draft.Appearance : null };
        var look = Draft.Appearance;
        if (look is null || !NativeData.HasFace(look.Face) || !look.Face!.StartsWith("face." + sex + ".", StringComparison.Ordinal))
            look = new CharacterAppearance(_catalogue.Race(Draft.PeopleId)?.MidStatureMm ?? 1750, NativeData.FirstFace(sex), null, null, null, null);
        return Draft with { BodyPresetId = id, Appearance = NativeData.WithBuild(look, _catalogue, Draft.PeopleId, sex, body.Build) };
    }

    /// <summary>The native face presets the FACE step offers a sex, in catalogue order (the gate proof browses them).</summary>
    public IReadOnlyList<string> NativeFaces(string sex) => _catalogue.Options(CreatorChoice.Face, sex).Select(o => o.Id).ToList();

    /// <summary>How many HumGen body presets the FACE/BODY steps offer this body's sex (the gate proof browses them).</summary>
    public int BodyPresetCount(string body) => NativeData.BodyPresets(body.Contains("_f.", StringComparison.Ordinal) ? "f" : "m").Count;

    /// <summary>Choose a HumGen body preset as the Body step's menu does (the gate proof).</summary>
    public void UseBodyPreset(int index) => Shape(look => NativeData.WithBodyPreset(look, _catalogue, Draft.PeopleId, Sex, index));

    /// <summary>The model drawn: the body type's, or the body baked as the chosen face of it.</summary>
    private string? Model => _catalogue.DrawnModel(OfferedBodies().FirstOrDefault(o => o.Id == Draft.BodyPresetId)?.Model, Draft.Appearance?.Face);

    /// <summary>The baked faces a body offers, by face id, its own first (the creator shots walk these).</summary>
    public IReadOnlyList<string?> FacesFor(string body) =>
        _catalogue.Faces(OfferedBodies(CharacterSpec.DefaultPeople).FirstOrDefault(o => o.Id == body)?.Model).Select(f => f.Look.Face).ToList();

    /// <summary>A body and one of its baked faces, its look otherwise as baked, as choosing them does (the creator shots).</summary>
    public void UseFace(string body, string? face)
    {
        _draft = WithBody(body);
        _draft = Draft with { Appearance = Look with { Face = face } };
        Render(null);
    }

    /// <summary>The model a character's body and look are drawn with (the game's player, as the creator showed it).</summary>
    public string? DrawnModel(string? model, CharacterAppearance? look) => (look?.Face is null || NativeData.HasFace(look.Face)
        ? NativeBase(look?.Face?.StartsWith("face.f.", StringComparison.Ordinal) == true || (look?.Face is null
            && (_catalogue.BakedFor(model)?.Sex == "f" || model?.EndsWith("_f", StringComparison.Ordinal) == true)) ? "f" : "m") : null)
        ?? _catalogue.DrawnModel(model, look?.Face);

    /// <summary>The body's sex as HumGen's content folders split it: the baked body's own, else the body type's (<c>…_f</c>).</summary>
    private string Sex => _catalogue.BakedFor(Model)?.Sex
        ?? (OfferedBodies().FirstOrDefault(o => o.Id == Draft.BodyPresetId)?.Body.Type.EndsWith("_f", StringComparison.Ordinal) == true ? "f" : "m");

    /// <summary>The look as it stands: the draft's, or the body's as baked.</summary>
    private CharacterAppearance Look => Draft.Appearance ?? (NativeActive
        ? new CharacterAppearance(_catalogue.Race(Draft.PeopleId)?.MidStatureMm ?? 1750, NativeData.FirstFace(Sex), null, null, null, null)
        : _catalogue.Start(Model, Draft.PeopleId));

    /// <summary>Change the look; a look back at exactly the body as baked is kept as none, so the draft says "as baked".</summary>
    private void Shape(Func<CharacterAppearance, CharacterAppearance> change)
    {
        var look = change(Look);
        // A people only ever holds the choices its canon allows, whoever the change came from (the owner's ruling, 2026-10-28).
        if (NativeActive) look = NativeData.Constrain(look, _catalogue, Draft.PeopleId);
        _draft = Draft with { Appearance = look.Equals(_catalogue.Start(Model, Draft.PeopleId)) ? null : look };
        PendingState();
        ShowBody();
    }

    // ── the steps ───────────────────────────────────────────────────────────

    /// <summary>Step 3: the face preset HumGen starts from, then age and skin, then the face's proportions.</summary>
    private void FaceStep()
    {
        _body.AddChild(PendingNote());
        if (BakedOnly && _catalogue.Faces(Model) is { Count: > 1 } faces)
        {
            _body.AddChild(BakedFaces(faces));
            return;
        }
        _body.AddChild(OptionSection(CreatorChoice.Face, "Face", "a HumGen starting face; choosing one resets the face sliders to it"));
        _body.AddChild(SliderSection("face", "Features", "age, skin and proportions"));
    }

    /// <summary>The faces baked for this body type, each its own body: choosing one draws that body, its head and hair.</summary>
    private Control BakedFaces(IReadOnlyList<BakedBody> faces)
    {
        var section = UiKit.Section("Face");
        string? worn = Look.Face;
        foreach (var face in faces)
        {
            string id = $"creator.face.{face.Model}";
            string label = face.FaceOf is null ? LookSwatches.BakedFace : LookSwatches.ShownNames.GetValueOrDefault(face.Look.Face ?? "", face.Model);
            var row = UiKit.ListRow(id, label, height: 40);
            UiKit.Select(row.Button, face.Look.Face == worn || (worn is null && face.FaceOf is null));
            row.Button.Pressed += Deferred(() =>
            {
                Shape(look => look with { Face = face.Look.Face });
                Render(id);
            });
            section.AddChild(row.Button);
        }
        return section;
    }

    /// <summary>
    /// A Kal's growth picker: the same tile grid as every other choice, over the styles the people actually grows, held in the
    /// look's own slider so a save carries it and Randomise and Reset move within it like any other choice.
    /// </summary>
    private Control GrowthSection(string heading, string slider, IReadOnlyList<string> styles)
    {
        var section = UiKit.Section(heading);
        var grid = Grid();
        int worn = Look.Sliders.GetValueOrDefault(slider);
        for (int at = 0; at < styles.Count; at++)
        {
            int index = at;
            grid.AddChild(OptionTile($"creator.{slider}.{index}", styles[index], null, index == worn,
                () => Shape(look => look with { Sliders = index == 0 ? look.Sliders.Remove(slider) : look.Sliders.SetItem(slider, index) })));
        }
        section.AddChild(grid);
        return section;
    }

    /// <summary>Step 4: head hair, facial hair where HumGen has it for this body, and the hair's colour. A Kal gets stone growths instead.</summary>
    private void HairStep()
    {
        _body.AddChild(PendingNote());
        // A Kal has no hair at all: the step offers the stone growths its head grows instead (owner's ruling, 2026-10-28).
        if (NativeActive && Draft.PeopleId == PeopleLooks.Kal)
        {
            _body.AddChild(GrowthSection("Stone growth", KalGrowth.StyleSlider, KalGrowth.Styles));
            _body.AddChild(GrowthSection("Jaw and chin", KalGrowth.JawSlider, KalGrowth.Jaw));
            return;
        }
        // Hairstyles baked on this body's rig and installed in this build, the body's own first; the row is only there when there is a choice.
        var styles = LookSwatches.HairStyles(Model).Select((s, i) => (s.Label, Index: i, s.Asset))
            .Where(s => s.Asset is null || _art.RiggedFile(s.Asset, s.Asset + "_rigged.glb") is not null).ToList();
        // The exported creator's one baked hair is the style row's first entry, so the list is only drawn when there is no row.
        if (!BakedOnly || styles.Count < 2)
            _body.AddChild(OptionSection(CreatorChoice.Hair, "Hair", "HumGen hair cards"));
        if (styles.Count > 1)
        {
            var section = UiKit.Section("Hair style");
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            int worn = Look.Sliders.TryGetValue(LookSwatches.StyleSlider, out int at) ? at : 0;
            foreach (var style in styles)
            {
                string id = $"creator.{LookSwatches.StyleSlider}.{style.Index}";
                var button = UiKit.Action(style.Label, Deferred(() =>
                {
                    Shape(look => look with { Sliders = style.Index == 0 ? look.Sliders.Remove(LookSwatches.StyleSlider) : look.Sliders.SetItem(LookSwatches.StyleSlider, style.Index) });
                    Render(id);
                }), compact: true, id: id);
                button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                UiKit.Select(button, style.Index == worn);
                row.AddChild(button);
            }
            section.AddChild(row);
            _body.AddChild(section);
        }
        if (!NativeActive)
            _body.AddChild(SwatchSection("Hair colour", LookSwatches.HairSlider, LookSwatches.Hair(Draft.PeopleId), new Color(.08f, .035f, .012f), multiply: false));
        if (Draft.PeopleId != PeopleLooks.Siann && _catalogue.Options(CreatorChoice.FacialHair, Sex).Count > 0)
            _body.AddChild(OptionSection(CreatorChoice.FacialHair, "Facial hair", "", none: "Clean-shaven"));
        // A people with no hair-colour control at all (a Siann's hair is white at every age) is not shown an empty one.
        if (!NativeActive || NativeData.Sliders(_catalogue, Draft.PeopleId, "hair", Sex).Count > 0)
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
        // Cloth colours the cloth that was dyed: the frontier outfit keeps its own, so the choice is not offered over it.
        if (_preview.WearsDyedCloth)
        {
            _body.AddChild(SwatchSection("Shirt", LookSwatches.ShirtSlider, LookSwatches.Shirts, Colors.Black, multiply: false));
            _body.AddChild(SwatchSection("Trousers", LookSwatches.TrouserSlider, LookSwatches.Trousers, Colors.Black, multiply: false));
        }
    }

    /// <summary>A Kal's stone, from patched to full (the skin shader's coverage); full stone is the default and saves as no choice.</summary>
    private Control StoneSection()
    {
        var section = UiKit.Section("Stone");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        int full = LookSwatches.KalStone.Length - 1;
        int worn = Look.Sliders.TryGetValue(LookSwatches.StoneSlider, out int at) ? at : full;
        for (int i = 0; i < LookSwatches.KalStone.Length; i++)
        {
            int level = i;
            string id = $"creator.{LookSwatches.StoneSlider}.{level}";
            var button = UiKit.Action(LookSwatches.KalStone[level].Label, Deferred(() =>
            {
                Shape(look => look with { Sliders = level == full ? look.Sliders.Remove(LookSwatches.StoneSlider) : look.Sliders.SetItem(LookSwatches.StoneSlider, level) });
                Render(id);
            }), compact: true, id: id);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            UiKit.Select(button, level == worn);
            row.AddChild(button);
        }
        section.AddChild(row);
        return section;
    }

    /// <summary>The look's lines on Confirm: height, then each choice by its name.</summary>
    private IEnumerable<(string Caption, string Words)> LookLines()
    {
        if (_catalogue.Baked.Length == 0)
            yield break;
        var look = Look;
        // No outfit or footwear chosen is what the body was baked in, so that is named rather than "none".
        string? Baked(CreatorChoice choice) => choice is CreatorChoice.Outfit or CreatorChoice.Footwear
            ? _catalogue.Options(choice, Sex).FirstOrDefault(o => _catalogue.IsBaked(choice, o.Id, Model))?.Id : null;
        // A piece baked with one body only (a face body's own hair) is named by its shown name.
        string Name(CreatorChoice choice, string? id, string none) => _catalogue.Option(choice, id ?? Baked(choice)) is { } option
                ? OptionName(choice, option.Id, option.Label) : id is not null && LookSwatches.ShownNames.TryGetValue(id, out var shown) ? shown : none;
        // The exported creator draws each body's own hair and beard (a chosen face is its own body), whatever the draft carries.
        var drawn = BakedOnly ? _catalogue.BakedFor(Model)?.Look : null;
        string? hairId = drawn is not null ? drawn.Hair : look.Hair, beardId = drawn is not null ? drawn.FacialHair : look.FacialHair;
        var styles = LookSwatches.HairStyles(Model);
        string hair = styles.Count > 0 && (look.Hair is null || drawn is not null)
            ? styles.ElementAtOrDefault(look.Sliders.TryGetValue(LookSwatches.StyleSlider, out int style) ? style : 0).Label ?? styles[0].Label
            : Name(CreatorChoice.Hair, hairId, "none");
        yield return ("Height", Metres(look.StatureMm));
        yield return ("Face", BakedOnly ? FaceName(look.Face) : Name(CreatorChoice.Face, look.Face, "the body's own"));
        yield return ("Hair", hair + (beardId is { } beard ? ", " + Name(CreatorChoice.FacialHair, beard, beard) : ""));
        yield return ("Outfit", $"{Name(CreatorChoice.Outfit, look.Outfit, "none")}, {Name(CreatorChoice.Footwear, look.Footwear, "barefoot").ToLowerInvariant()}");
    }

    // ── pending bake ────────────────────────────────────────────────────────

    /// <summary>What the preview is: exactly the character, or the baked body with these parts of the look still to bake.</summary>
    private Control PendingNote()
    {
        if (NativeActive) return new Control();
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
        if (NativeActive) return;
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
            var chip = Image.CreateEmpty(14, 14, false, Image.Format.Rgba8);
            chip.Fill(multiply ? new Color(Math.Min(1, baked.R * swatch.R), Math.Min(1, baked.G * swatch.G), Math.Min(1, baked.B * swatch.B))
                : new Color(Math.Min(1, baked.R + swatch.R), Math.Min(1, baked.G + swatch.G), Math.Min(1, baked.B + swatch.B)));
            button.Icon = ImageTexture.CreateFromImage(chip);
            UiKit.Select(button, index == chosen);
            grid.AddChild(button);
        }
        section.AddChild(grid);
        return section;
    }

    /// <summary>A panel's sliders (body, face or hair), one group at a time behind a row of group tabs.</summary>
    private Control SliderSection(string panel, string heading, string note)
    {
        var sliders = NativeActive ? NativeData.Sliders(_catalogue, Draft.PeopleId, panel, Sex).ToList()
            : _catalogue.Sliders.Where(s => s.Panel == panel).ToList();
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
        if (NativeActive)
            section.AddChild(UiKit.Action("Reset " + group, Deferred(() =>
            {
                Shape(l => NativeData.Reset(l, _catalogue, Draft.PeopleId, panel, group, Sex));
                Render(null);
            }), compact: true, id: "creator.reset." + panel + "." + group.ToLowerInvariant().Replace(' ', '_')));
        var look = Look;
        foreach (var slider in sliders.Where(s => s.Group == group))
        {
            var shown = slider;
            section.AddChild(SliderRow("creator.slider." + slider.Id, slider.Label, slider.Min, slider.Max, 1, NativeActive ? NativeData.Value(look, _catalogue, Draft.PeopleId, slider) : _catalogue.Value(look, slider),
                v => SliderWords(shown, v), v => Shape(l => _catalogue.WithSlider(l, shown, v))));
        }
        return section;
    }

    /// <summary>
    /// The native body presets as the same tile grid as every other picker, named in-world ("Body 2") rather than by HumGen's
    /// own human names, and pictured with the preset's own face preview (the owner's rulings, 2026-10-28).
    /// </summary>
    private Control NativeBodyPresets()
    {
        var section = UiKit.Section("Body shape");
        var presets = NativeData.BodyPresets(Sex);
        int chosen = Math.Clamp(Look.Sliders.GetValueOrDefault("look.body_preset"), 0, presets.Count - 1);
        _body.AddChild(BodyGrid(section, presets, chosen));
        return section;
    }

    private Control BodyGrid(Control section, IReadOnlyList<System.Text.Json.JsonElement> presets, int chosen)
    {
        var grid = Grid();
        for (int at = 0; at < presets.Count; at++)
        {
            int index = at;
            string id = presets[index].GetProperty("id").GetString()!;
            grid.AddChild(OptionTile($"creator.body.{id}", $"Body {index + 1}", BodyThumbnail(id), index == chosen,
                () => Shape(look => NativeData.WithBodyPreset(look, _catalogue, Draft.PeopleId, Sex, index))));
        }
        return grid;
    }

    /// <summary>The tile grid every picker is drawn in: three across, each tile big enough to judge, its name underneath.</summary>
    private static GridContainer Grid()
    {
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 10);
        return grid;
    }

    /// <summary>The native preview staged for a choice: the preset set's own thumbnail for a face, the part's own for a groom or a garment.</summary>
    private Texture2D? Thumbnail(CreatorChoice choice, string id)
    {
        if (!NativeActive) return null;
        string? file = choice switch
        {
            CreatorChoice.Face => _art.RiggedFile("humgen_creator_materials", id + "/thumbnail.jpg"),
            CreatorChoice.Hair or CreatorChoice.FacialHair or CreatorChoice.Outfit or CreatorChoice.Footwear =>
                _art.RiggedFile(HumGenCreatorData.Part(choice, Sex, id), "thumbnail.jpg"),
            _ => null,
        };
        return file is null ? null : _art.Texture(file, "color");
    }

    /// <summary>A body preset's preview is the same preset's face thumbnail: HumGen ships one model and one preview for both.</summary>
    private Texture2D? BodyThumbnail(string presetId) =>
        _art.RiggedFile("humgen_creator_materials", "face." + presetId["bodyshape.".Length..] + "/thumbnail.jpg") is { } file
            ? _art.Texture(file, "color")
            : null;

    /// <summary>One picker tile: the picture in a button, the name under it (never over it), the chosen one highlighted.</summary>
    private Control OptionTile(string id, string name, Texture2D? icon, bool selected, Action pressed)
    {
        var cell = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cell.AddThemeConstantOverride("separation", 4);
        var button = new Button
        {
            Icon = icon,
            ExpandIcon = true,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(0, 104 * HudStyle.TextScale),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        UiKit.Select(button, selected);
        cell.AddChild(button);
        var label = HudStyle.Text(HudStyle.Body, 15, selected ? HudStyle.Bone : HudStyle.Muted);
        label.Text = name;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cell.AddChild(label);
        button.Pressed += Deferred(() =>
        {
            pressed();
            Render(id);
        });
        return cell;
    }

    /// <summary>A list of choices of one kind for this body's sex, grouped behind tabs when it is long, each marked baked or pending bake.</summary>
    private Control OptionSection(CreatorChoice choice, string heading, string note, string? none = null)
    {
        var options = _catalogue.Options(choice, Sex);
        if (BakedOnly)
            options = options.Where(o => _catalogue.IsBaked(choice, o.Id, Model)).ToList();
        else if (NativeActive)
            // A native groom or garment is offered once its own export exists: the body keeps its
            // own until then, and a batch still running must not offer a choice it cannot draw.
            options = options.Where(o => Installed(choice, o.Id)).ToList();
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
        // A face preset is offered as an in-world choice: no ethnicity tabs and no HumGen name (the owner's ruling, 2026-10-28).
        if (options.Count > 12 && choice != CreatorChoice.Face)
        {
            string group = Group(key, options.Select(o => o.Group), _catalogue.Option(choice, chosen)?.Group);
            section.AddChild(GroupTabs(key, options.Select(o => o.Group).Distinct().ToList(), group));
            shown = options.Where(o => o.Group == group).ToList();
        }
        string kind = choice.ToString().ToLowerInvariant();
        var numbers = options.Select((option, at) => (option.Id, At: at)).ToDictionary(p => p.Id, p => p.At + 1);
        var grid = Grid();
        if (none is not null && (!BakedOnly || _catalogue.IsBaked(choice, null, Model)))
            grid.AddChild(OptionTile($"creator.{kind}.none", none, null, chosen is null, () => Choose(choice, null)));
        foreach (var option in shown)
        {
            string id = option.Id;
            // The number is the choice's place in the people's own list, so "Face 12" is the same face every time it is opened.
            string name = choice == CreatorChoice.Face && NativeActive ? $"Face {numbers[id]}" : OptionName(choice, id, option.Label);
            grid.AddChild(OptionTile($"creator.{kind}.{id}", name, Thumbnail(choice, id), chosen == id, () => Choose(choice, id)));
        }
        section.AddChild(grid);
        if (BakedOnly)
            section.AddChild(UiKit.Words(LaterWords, 16, HudStyle.Muted));
        return section;
    }

    /// <summary>Whether this build holds a native groom's or garment's own export (the modular export batch writes one file per part).</summary>
    private bool Installed(CreatorChoice choice, string id) =>
        choice is not (CreatorChoice.Hair or CreatorChoice.FacialHair or CreatorChoice.Outfit or CreatorChoice.Footwear)
        || _art.RiggedFile(HumGenCreatorData.Part(choice, Sex, id), "creator_part.json") is not null;

    /// <summary>The baked face's name on Confirm: "their own" for the body type's own face, else the face's shown name.</summary>
    private string FaceName(string? face) => _catalogue.Faces(Model).FirstOrDefault(f => f.Look.Face == face) is { FaceOf: not null } variant
        ? LookSwatches.ShownNames.GetValueOrDefault(face!, variant.Model) : "their own";

    /// <summary>A choice's name as the body is drawn: the frontier outfit in place of the baked one, the baked face and pieces by our own names.</summary>
    private string OptionName(CreatorChoice choice, string id, string label) =>
        choice == CreatorChoice.Outfit && _preview.WearsFrontier ? LookSwatches.FrontierOutfit
        : choice == CreatorChoice.Face && BakedOnly ? LookSwatches.BakedFace
        : LookSwatches.ShownNames.GetValueOrDefault(id, label);

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
