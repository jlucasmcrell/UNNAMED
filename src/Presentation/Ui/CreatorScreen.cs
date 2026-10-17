// UNNAMED Presentation - the character creator: People, Body, Identity, Call and Confirm, plus Quick start (WP2's local part; PJ04 §3, PJ06 §3)
// Godot presentation only: it holds the draft being made and hands Main the spec to start a new game with; nothing here is game state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// New Game's creator, in the Field Instrument kit: the title's dark panel down the left with the step bar, the step and Back / Next,
/// and the body itself filling the rest - the game's own model, turning on a set of its own (<see cref="CreatorPreview"/>). The owner's
/// scope (D3): the Veth only; a body type and one of three builds; a name of 1-24 characters with Suggest; he, she or they; one of the
/// four Calls, each showing what it starts with. No face sliders. Every step has a value from the first frame, so the steps can be taken
/// in any order and going back loses nothing; Begin is refused, with the reason, only while the name cannot be kept.
/// </summary>
public partial class CreatorScreen : CanvasLayer
{
    private const float Width = 660;

    private static readonly (CreatorStep Step, string Name)[] Steps =
    {
        (CreatorStep.People, "People"), (CreatorStep.Body, "Body"), (CreatorStep.Identity, "Identity"), (CreatorStep.Call, "Call"),
        (CreatorStep.Confirm, "Confirm"),
    };

    private GameSession _session = null!;
    private ArtLibrary _art = ArtLibrary.Empty;
    private ArtBindings _bindings = ArtBindings.Empty;
    private HudIcons _icons = HudIcons.None;
    private readonly RandomNumberGenerator _random = new();
    private CreatorPreview _preview = null!;
    private HBoxContainer _stepBar = null!;
    private Label _stepTitle = null!;
    private VBoxContainer _body = null!;
    private ScrollContainer _scroll = null!;
    private Button _back = null!;
    private Button _next = null!;
    private Label _lightName = null!;
    private Label? _count;
    private Label? _problem;
    private CreatorDraft? _draft;
    private string? _shownBody;

    /// <summary>The spec chosen, to start the game with: Main hands it to <c>GameSession.NewGame</c>.</summary>
    public Action<CharacterSpec>? Begin { get; set; }

    /// <summary>Back out to the title; the draft is kept for this session (PJ04 §11).</summary>
    public Action? Leave { get; set; }

    /// <summary>Whether this profile has walked the road in once: only then is "Start at the Waystone" offered (PJ06 §8).</summary>
    public bool RoadWalked { get; set; }

    /// <summary>The skip as this profile last chose it; once offered it defaults on (PJ06 §8).</summary>
    public bool SkipRoad { get; set; } = true;

    /// <summary>The step on screen.</summary>
    public CreatorStep Step { get; private set; }

    /// <summary>What has been chosen so far.</summary>
    public CreatorDraft Draft => _draft ??= Fresh();

    public void Bind(GameSession session, ArtLibrary art, ArtBindings bindings, HudIcons icons)
    {
        _session = session;
        _art = art;
        _bindings = bindings;
        _icons = icons;
    }

    public override void _Ready()
    {
        Visible = false;
        Layer = 10;
        _random.Randomize();
        var root = new Control { Theme = UiKit.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _preview = new CreatorPreview { Name = "Preview" };
        root.AddChild(_preview);

        var panel = new PanelContainer { AnchorBottom = 1, OffsetRight = Width, MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(HudStyle.Soot, 0.97f),
            BorderColor = HudStyle.Iron, BorderWidthRight = 1,
            ContentMarginLeft = 56, ContentMarginRight = 48, ContentMarginTop = 56, ContentMarginBottom = 40,
        });
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        var title = HudStyle.Text(HudStyle.Caps, 44, HudStyle.Bone, caps: true);
        title.Text = "A new character";
        column.AddChild(title);
        column.AddChild(new UiKit.TickRule { CustomMinimumSize = new Vector2(0, 10) });
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
        _stepBar = new HBoxContainer();
        _stepBar.AddThemeConstantOverride("separation", 6);
        column.AddChild(_stepBar);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 22) });
        _stepTitle = HudStyle.Text(HudStyle.Caps, 28, HudStyle.Bone, caps: true);
        column.AddChild(_stepTitle);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        _scroll = UiKit.Scroll();
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 22);
        _scroll.AddChild(_body);
        column.AddChild(_scroll);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        _back = UiKit.Action("Back", Deferred(GoBack), id: "creator.back");
        footer.AddChild(_back);
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _next = UiKit.Action("Next", Deferred(GoNext), UiKit.Kind.Primary, id: "creator.next");
        _next.CustomMinimumSize = new Vector2(180, 48);
        footer.AddChild(_next);
        column.AddChild(footer);
        panel.AddChild(column);
        root.AddChild(panel);
        root.AddChild(ViewBar());
        AddChild(root);
    }

    /// <summary>
    /// The preview's own controls, at the foot of the body's side of the screen: turn, the three framings, the light. Buttons, so the
    /// keyboard reaches all of it; the mouse can also drag and wheel on the body itself.
    /// </summary>
    private Control ViewBar()
    {
        var bar = new HBoxContainer
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Begin, GrowVertical = Control.GrowDirection.Begin,
            OffsetRight = -40, OffsetBottom = -40,
        };
        bar.AddThemeConstantOverride("separation", 6);
        bar.AddChild(UiKit.Action("◀", () => _preview.Turn(-0.4f), compact: true, id: "creator.view.left"));
        bar.AddChild(UiKit.Action("▶", () => _preview.Turn(0.4f), compact: true, id: "creator.view.right"));
        bar.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        foreach (var (name, zoom) in CreatorPreview.Framings)
            bar.AddChild(UiKit.Action(name, () => _preview.Frame(zoom), compact: true, id: "creator.view." + name.ToLowerInvariant().Replace(' ', '_')));
        bar.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        var light = UiKit.Action("Light", () =>
        {
            _preview.UseLight(_preview.Light + 1);
            _lightName.Text = CreatorPreview.Lights[_preview.Light];
        }, compact: true, id: "creator.view.light");
        bar.AddChild(light);
        _lightName = UiKit.Caption(CreatorPreview.Lights[0], HudStyle.Ash);
        _lightName.CustomMinimumSize = new Vector2(150, 0);
        bar.AddChild(_lightName);
        return bar;
    }

    /// <summary>Open on the People step, with the draft kept from earlier this session or a fresh one.</summary>
    public void Open()
    {
        _preview.Bind(_art, _bindings);
        Visible = true;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
        if (!OfferedBodies().Any(b => b.Id == Draft.BodyPresetId && b.Installed))
            _draft = Draft with { BodyPresetId = DefaultBody() };
        _draft = Draft with { Start = RoadWalked && SkipRoad ? StartPoint.Waystone : StartPoint.Road };
        Go(CreatorStep.People);
    }

    public void Close() => Visible = false;

    /// <summary>Forget the draft: the next Open starts afresh (after Begin).</summary>
    public void Forget()
    {
        _draft = null;
        _shownBody = null;
        _preview.ResetView();
    }

    /// <summary>Show a step; focus goes to its first control (Begin on Confirm, PJ06 §3).</summary>
    public void Go(CreatorStep step)
    {
        Step = step;
        Render(null);
    }

    /// <summary>Use this draft, as a harness or Quick start does; the step on screen is drawn again.</summary>
    public void Use(CreatorDraft draft)
    {
        _draft = draft;
        Render(null);
    }

    /// <summary>Press Begin as a click would (the creator shots). False when the draft cannot begin.</summary>
    public bool PressBegin()
    {
        if (Draft.Problem is not null)
            return false;
        Begin?.Invoke(Draft.Spec());
        return true;
    }

    private void GoBack()
    {
        if (Step == CreatorStep.People)
            Leave?.Invoke();
        else
            Go(Step - 1);
    }

    private void GoNext()
    {
        if (Step == CreatorStep.Confirm)
            PressBegin();
        else
            Go(Step + 1);
    }

    private CreatorDraft Fresh() => CreatorDraft.Fresh(DefaultBody(),
        _session.Characters.Calls.ContainsKey(CreatorDraft.FirstCall) ? CreatorDraft.FirstCall : _session.Characters.Calls.Keys.FirstOrDefault(),
        StartPoint.Road);

    // ── bodies ──────────────────────────────────────────────────────────────

    /// <summary>One body the creator offers: its preset, its type and build, the model it is drawn with and whether this build has it.</summary>
    private sealed record Offered(string Id, BodyType Type, BodyBuild Build, string? Model, bool Installed);

    /// <summary>
    /// Every type and build the bindings name, each with the model it would be drawn with in this run's family. When none of them is
    /// installed (a run with no asset workspace) the game draws everyone as the greybox, and every body is offered on it.
    /// </summary>
    private List<Offered> OfferedBodies()
    {
        var bodies = _bindings.Bodies;
        string family = PlayerBodies.Family(VisualOptions.All.GetValueOrDefault("player"));
        bool Installed(string model) => _art.RiggedFile(model, model + "_rigged.glb") is not null;
        var offered = bodies.Types.SelectMany(t => bodies.Builds.Select(b =>
        {
            string? model = PlayerBodies.Model(t, family, Installed);
            return new Offered(PlayerBodies.Id(t, b), t, b, model, model is not null);
        })).ToList();
        if (offered.Count > 0 && offered.All(o => !o.Installed))
            offered = offered.Select(o => o with { Installed = true }).ToList();
        return offered;
    }

    private string DefaultBody()
    {
        var offered = OfferedBodies();
        return offered.FirstOrDefault(o => o.Id == _bindings.Bodies.Default && o.Installed)?.Id
               ?? offered.FirstOrDefault(o => o.Installed)?.Id
               ?? CharacterSpec.DefaultBodyPreset;
    }

    /// <summary>The body the draft names, drawn: loaded the first time, then only its build and outfit change.</summary>
    private void ShowBody()
    {
        var body = OfferedBodies().FirstOrDefault(o => o.Id == Draft.BodyPresetId);
        string? chest = Outfit(Draft.CallId);
        string shown = $"{body?.Model}|{body?.Build.Girth}|{chest}";
        if (shown == _shownBody)
            return;
        _shownBody = shown;
        _preview.Show(body?.Model, body?.Build.Girth ?? 1f, chest);
    }

    /// <summary>What the body wears for a Call's kit, by the game's own rule (Main): the chest item, "_cap" with a head item, else the head item.</summary>
    private string? Outfit(string? callId)
    {
        var lines = Kit(callId).Where(k => k.Equip).ToList();
        string? Worn(EquipSlot slot) => lines.FirstOrDefault(k => _session.Setup.Items.Catalog.Get(k.ItemId).Slot == slot)?.ItemId;
        string? chest = Worn(EquipSlot.Chest), head = Worn(EquipSlot.Head);
        return chest is not null && head is not null ? $"{chest}_cap" : chest ?? head;
    }

    /// <summary>Everything a new character carries: the common kit, then the Call's.</summary>
    private IEnumerable<StartingItem> Kit(string? callId) =>
        callId is not null && _session.Characters.Calls.TryGetValue(callId, out var call)
            ? _session.Setup.Items.StartingKit.Concat(call.Kit)
            : _session.Setup.Items.StartingKit;

    // ── the steps ───────────────────────────────────────────────────────────

    private void Render(string? focus)
    {
        _count = null;
        _problem = null;
        UiKit.Clear(_stepBar);
        foreach (var (step, name) in Steps)
        {
            var button = UiKit.Action($"{(int)step + 1}  {name}", Deferred(() => Go(step)), compact: true, id: "creator.step." + name.ToLowerInvariant());
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            UiKit.Select(button, step == Step);
            _stepBar.AddChild(button);
        }
        _stepTitle.Text = Steps[(int)Step].Name;
        UiKit.Clear(_body);
        switch (Step)
        {
            case CreatorStep.People:
                PeopleStep();
                break;
            case CreatorStep.Body:
                BodyStep();
                break;
            case CreatorStep.Identity:
                IdentityStep();
                break;
            case CreatorStep.Call:
                CallStep();
                break;
            default:
                ConfirmStep();
                break;
        }
        _back.Text = Step == CreatorStep.People ? "Title" : "Back";
        _next.Text = Step == CreatorStep.Confirm ? "Begin" : "Next";
        _next.Disabled = Step == CreatorStep.Confirm && Draft.Problem is not null;
        ShowBody();
        _scroll.ScrollVertical = 0;
        Callable.From(() =>
        {
            if (UiKit.Refocus(_body, focus))
                return;
            if (Step == CreatorStep.Confirm && !_next.Disabled)
                _next.GrabFocus();
            else
                (UiKit.FirstFocusable(_body) ?? _next).GrabFocus();
        }).CallDeferred();
    }

    /// <summary>Step 1: the one people the slice offers (the others are not shown, PJ04:51), and Quick start.</summary>
    private void PeopleStep()
    {
        var section = UiKit.Section("Lineage");
        foreach (var people in _session.Characters.PlayablePeoples)
        {
            var card = UiKit.ListRow("creator.people." + people.Id, Names.Of(_session, people.Id), "The touched lineage", height: 56);
            UiKit.Select(card.Button, people.Id == Draft.PeopleId);
            string id = people.Id;
            card.Button.Pressed += Deferred(() =>
            {
                _draft = Draft with { PeopleId = id };
                Render("creator.people." + id);
            });
            section.AddChild(card.Button);
            if (people.Id == "people.veth")
            {
                // RACES.md §1, in its own words and shortened: who they are, how their bodies differ, where they lean.
                section.AddChild(UiKit.Words("Ordinary humans, insofar as that word means anything here: their ancestors survived a Pachakuti. "
                    + "The majority, and the least examined.", 17, HudStyle.Ash));
                section.AddChild(Line("Body", "Slightly long forearms and shins, long fingers, unusually mobile joints, reflective irises."));
                section.AddChild(Line("Leans toward", "Melee and ranged."));
            }
        }
        _body.AddChild(section);

        var quick = UiKit.Section("Quick start");
        quick.AddChild(UiKit.Words("A ready body, the Wayfarer's Call and a Veth name to start from. You can change the name, then begin.", 17, HudStyle.Ash));
        quick.AddChild(UiKit.Action("Start from a preset", Deferred(QuickStart), id: "creator.quick"));
        _body.AddChild(quick);
    }

    /// <summary>Quick start (PJ04 §3.1): a whole character - the default body, the first Call and a suggested name - then Identity.</summary>
    private void QuickStart()
    {
        _draft = Draft with { BodyPresetId = DefaultBody(), CallId = Fresh().CallId, Name = SuggestedName() };
        Go(CreatorStep.Identity);
    }

    /// <summary>Step 2: a body type and a build (D3: a blend preset and 3 builds), on the model the game will draw.</summary>
    private void BodyStep()
    {
        var offered = OfferedBodies();
        var current = offered.FirstOrDefault(o => o.Id == Draft.BodyPresetId) ?? offered.FirstOrDefault();
        var types = UiKit.Section("Body type", "independent of pronouns");
        var typeRow = new HBoxContainer();
        typeRow.AddThemeConstantOverride("separation", 8);
        foreach (var type in _bindings.Bodies.Types)
        {
            var choice = offered.FirstOrDefault(o => o.Type == type && o.Build == current?.Build) ?? offered.First(o => o.Type == type);
            string id = "creator.type." + type.Id;
            var button = UiKit.Action(type.Label, Deferred(() =>
            {
                _draft = Draft with { BodyPresetId = choice.Id };
                Render(id);
            }), id: id);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.Disabled = !choice.Installed;
            if (!choice.Installed)
                button.TooltipText = "This body is not installed in this build.";
            UiKit.Select(button, current?.Type == type);
            typeRow.AddChild(button);
        }
        types.AddChild(typeRow);
        if (offered.Any(o => !o.Installed))
            types.AddChild(UiKit.Words("A body type that is greyed out is not installed in this build.", 16, HudStyle.Muted));
        _body.AddChild(types);

        var builds = UiKit.Section("Build");
        var buildRow = new HBoxContainer();
        buildRow.AddThemeConstantOverride("separation", 8);
        foreach (var build in _bindings.Bodies.Builds)
        {
            var choice = offered.FirstOrDefault(o => o.Type == current?.Type && o.Build == build);
            if (choice is null)
                continue;
            string id = "creator.build." + build.Id;
            var button = UiKit.Action(build.Label, Deferred(() =>
            {
                _draft = Draft with { BodyPresetId = choice.Id };
                Render(id);
            }), id: id);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.Disabled = !choice.Installed;
            UiKit.Select(button, current?.Build == build);
            buildRow.AddChild(button);
        }
        builds.AddChild(buildRow);
        _body.AddChild(builds);

        var randomise = UiKit.Action("Randomise", Deferred(() =>
        {
            var installed = OfferedBodies().Where(o => o.Installed).ToList();
            if (installed.Count > 0)
                _draft = Draft with { BodyPresetId = installed[_random.RandiRange(0, installed.Count - 1)].Id };
            Render("creator.randomise");
        }), id: "creator.randomise");
        var tools = new HBoxContainer();
        tools.AddChild(randomise);
        _body.AddChild(tools);
        _body.AddChild(UiKit.Words("Drag the body to turn it, and use the wheel to come closer. The buttons at the foot of the screen do the same.",
            16, HudStyle.Muted));
    }

    /// <summary>Step 3: the name (1-24 characters, Suggest from the Veth's own names) and pronouns (PJ04 §3.4).</summary>
    private void IdentityStep()
    {
        var name = UiKit.Section("Name");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var field = new LineEdit
        {
            Text = Draft.Name, PlaceholderText = "Your character's name", MaxLength = CharacterSpec.MaxNameLength,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44),
        };
        field.AddThemeFontSizeOverride("font_size", HudStyle.Sized(20));
        field.AddThemeStyleboxOverride("normal", UiKit.Box(HudStyle.Coal, HudStyle.Iron));
        field.AddThemeStyleboxOverride("focus", UiKit.Box(HudStyle.Coal, HudStyle.Ember));
        UiKit.Id(field, "creator.name");
        field.TextChanged += text =>
        {
            _draft = Draft with { Name = text };
            NameState();
        };
        field.TextSubmitted += _ => Deferred(GoNext)();
        row.AddChild(field);
        row.AddChild(UiKit.Action("Suggest", Deferred(() =>
        {
            _draft = Draft with { Name = SuggestedName() };
            Render("creator.suggest");
        }), id: "creator.suggest"));
        name.AddChild(row);
        var state = new HBoxContainer();
        _problem = UiKit.Words(string.Empty, 16, HudStyle.Wound);
        state.AddChild(_problem);
        _count = UiKit.Caption(string.Empty);
        state.AddChild(_count);
        name.AddChild(state);
        name.AddChild(UiKit.Words("A short given name and a land-or-work surname is the Veth way; any name is yours to choose.", 16, HudStyle.Muted));
        _body.AddChild(name);
        NameState();

        var pronouns = UiKit.Section("Pronouns", "how others will speak of you");
        var choices = new HBoxContainer();
        choices.AddThemeConstantOverride("separation", 8);
        foreach (var value in new[] { Pronouns.He, Pronouns.She, Pronouns.They })
        {
            string id = "creator.pronouns." + PronounKeys.Key(value);
            var button = UiKit.Action(CreatorDraft.Words(value), Deferred(() =>
            {
                _draft = Draft with { Pronouns = value };
                Render(id);
            }), id: id);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            UiKit.Select(button, Draft.Pronouns == value);
            choices.AddChild(button);
        }
        pronouns.AddChild(choices);
        pronouns.AddChild(UiKit.Words("Independent of the body. Today people mostly say \"you\"; the journal, rumours and talk about you will use these.", 16, HudStyle.Muted));
        _body.AddChild(pronouns);
    }

    /// <summary>The name's counter and its refusal, kept in place while the player types (the field keeps the focus).</summary>
    private void NameState()
    {
        if (_count is not null)
            _count.Text = Draft.Count;
        if (_problem is not null)
            _problem.Text = Draft.Name.Length == 0 ? string.Empty : Draft.Problem ?? string.Empty;
    }

    /// <summary>A name from the people's own list (canon-approved), never the one the field already holds.</summary>
    private string SuggestedName()
    {
        if (!_session.Characters.Peoples.TryGetValue(Draft.PeopleId, out var people) || people.Names.IsEmpty)
            return Draft.Name;
        string name = Draft.Name;
        for (int i = 0; i < 4 && name == Draft.Name; i++)
            name = people.Names.Pick(_random.Randi());
        return name;
    }

    /// <summary>Step 4: the four Calls, each with its package - its leaning, the two skills it starts, its kit and its contact (PJ05).</summary>
    private void CallStep()
    {
        var list = UiKit.Section("What you did before");
        foreach (var call in _session.Characters.Calls.Values.OrderBy(c => c.Id == CreatorDraft.FirstCall ? 0 : 1).ThenBy(c => Names.Of(_session, c.Id)))
        {
            string id = "creator.call." + call.Id;
            var row = UiKit.ListRow(id, Names.Of(_session, call.Id), Leaning(call), height: 48);
            if (call.Id == CreatorDraft.FirstCall)
                row.Right.AddChild(UiKit.Chip("First character", HudStyle.Iron, HudStyle.Ash));
            UiKit.Select(row.Button, Draft.CallId == call.Id);
            string callId = call.Id;
            row.Button.Pressed += Deferred(() =>
            {
                _draft = Draft with { CallId = callId };
                Render(id);
            });
            list.AddChild(row.Button);
        }
        _body.AddChild(list);
        if (Draft.CallId is { } chosen && _session.Characters.Calls.TryGetValue(chosen, out var picked))
            _body.AddChild(Package(picked));
    }

    /// <summary>A Call's attribute leaning, in words: "Endurance +2 · Might +1 · Agility +1".</summary>
    private static string Leaning(CallDefinition call) => CharacterWords.Leaning(call);

    private Control Package(CallDefinition call)
    {
        var section = UiKit.Section(Names.Of(_session, call.Id), call.Tagline);
        section.AddChild(UiKit.Words(call.Description, 17, HudStyle.Ash));
        section.AddChild(Line("Leans", Leaning(call)));
        section.AddChild(Line("Starts", string.Join(", ", call.Package.Skills.Select(s => $"{Names.Of(_session, s.Key)} {s.Value}"))));
        section.AddChild(Line("Contact", $"{Names.Of(_session, call.Contact.NpcId)}, who already trusts you a little"));
        section.AddChild(UiKit.Caption("Adds to what everyone carries"));
        section.AddChild(KitList(call.Kit));
        return section;
    }

    /// <summary>Step 5 (PJ06 §3): who they are, what they carry and know, the opening once the road has been walked, and Begin.</summary>
    private void ConfirmStep()
    {
        var draft = Draft;
        var body = OfferedBodies().FirstOrDefault(o => o.Id == draft.BodyPresetId);
        var who = UiKit.Section("Who");
        who.AddChild(UiKit.Stat("Name", draft.KeptName.Length > 0 ? draft.KeptName : "-", draft.Problem is null ? null : HudStyle.Wound));
        who.AddChild(UiKit.Stat("Pronouns", CreatorDraft.Words(draft.Pronouns)));
        who.AddChild(UiKit.Stat("People", Names.Of(_session, draft.PeopleId)));
        who.AddChild(UiKit.Stat("Body", body is null ? "-" : $"{body.Type.Label}, {body.Build.Label.ToLowerInvariant()} build"));
        if (draft.CallId is { } callId && _session.Characters.Calls.TryGetValue(callId, out var call))
        {
            who.AddChild(UiKit.Stat("Call", Names.Of(_session, callId)));
            who.AddChild(UiKit.Stat("Starts", string.Join(", ", call.Package.Skills.Select(s => $"{Names.Of(_session, s.Key)} {s.Value}")), size: 17));
        }
        _body.AddChild(who);
        if (draft.Problem is { } problem)
        {
            var fix = UiKit.Callout(HudStyle.Wound, problem);
            _body.AddChild(fix);
            _body.AddChild(UiKit.Action("Name your character", Deferred(() => Go(CreatorStep.Identity)), id: "creator.fix_name"));
        }

        var carries = UiKit.Section("Carries");
        carries.AddChild(KitList(Kit(draft.CallId)));
        carries.AddChild(UiKit.Words("Knows no formulas or recipes yet: people and books teach those.", 16, HudStyle.Muted));
        _body.AddChild(carries);

        if (RoadWalked)
        {
            var opening = UiKit.Section("Opening");
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            foreach (var (start, words) in new[] { (StartPoint.Road, "The road in"), (StartPoint.Waystone, "Start at the Waystone") })
            {
                string id = "creator.start." + start.ToString().ToLowerInvariant();
                var button = UiKit.Action(words, Deferred(() =>
                {
                    _draft = Draft with { Start = start };
                    SkipRoad = start == StartPoint.Waystone;
                    Render(id);
                }), id: id);
                button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                UiKit.Select(button, draft.Start == start);
                row.AddChild(button);
            }
            opening.AddChild(row);
            _body.AddChild(opening);
        }
    }

    /// <summary>Items as the HUD shows them: each one's icon and name, with a count over one.</summary>
    private Control KitList(IEnumerable<StartingItem> kit)
    {
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        foreach (var item in kit)
        {
            var row = UiKit.Row(UiKit.Icon(_icons, item.ItemId, 32), item.Count > 1 ? $"{Names.Of(_session, item.ItemId)} ×{item.Count}" : Names.Of(_session, item.ItemId),
                item.Equip ? "worn or in hand" : "", height: 36);
            list.AddChild(row.Root);
        }
        return list;
    }

    /// <summary>A muted caption and its words on one line.</summary>
    private static Control Line(string caption, string words)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var label = UiKit.Caption(caption);
        label.CustomMinimumSize = new Vector2(120, 0);
        label.VerticalAlignment = VerticalAlignment.Top;
        row.AddChild(label);
        row.AddChild(UiKit.Words(words, 17));
        return row;
    }

    private static string Capital(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>A press that runs after it is handled: what it does rebuilds the step that holds the button.</summary>
    private static Action Deferred(Action pressed) => () => Callable.From(pressed).CallDeferred();
}
