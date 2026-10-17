// UNNAMED Presentation - the conversation's context sheet: the Learn column of what this teacher offers, and the way to ask someone in
// conversation about work (WP9, C10; WP15a)
// Godot presentation only: Learn submits the command; nothing here holds game state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Beside an open conversation, at the left of the screen in the Field Instrument's soot: LEARN, with what this teacher offers - each
/// lesson's name, kind and fee, a KNOWN / READY / IN CONVERSATION / NOT YET chip, its requirements as met (lichen) and unmet (wound)
/// chips, the first reason it cannot be asked for now in muted words, and Learn on a ready one. A teacher out of hours or away from
/// their work says so above the list, which still shows what will be on offer. Someone who hires on has "Ask about work" here, which
/// leaves the conversation for the hiring sheet. Non-modal in itself: the conversation it stands beside takes the keys, and nothing pauses.
/// </summary>
public partial class LearnSheet : CanvasLayer
{
    private const float Width = 440, MinRoom = 200;

    private PanelContainer _panel = null!;
    private VBoxContainer _body = null!;
    private GameSession? _session;
    private string _drawn = string.Empty;

    /// <summary>Leave the conversation for the hiring sheet, on someone.</summary>
    public Action<string>? AskAboutWork { get; set; }

    /// <summary>Where the conversation stands, or null: the column ends above it where they would meet.</summary>
    public Func<Rect2?>? Conversation { get; set; }

    public void Bind(GameSession session) => _session = session;

    public override void _Ready()
    {
        Layer = 2;
        Visible = false;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiKit.Theme };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel = new PanelContainer
        {
            AnchorLeft = 0, AnchorRight = 0, AnchorTop = 0, AnchorBottom = 0,
            OffsetLeft = HudView.Margin, OffsetRight = HudView.Margin + Width,
            CustomMinimumSize = new Vector2(Width, 0),
        };
        _panel.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(18, 14));
        var scroll = UiKit.Scroll();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_body);
        _panel.AddChild(scroll);
        root.AddChild(_panel);
        AddChild(root);
    }

    public override void _Process(double delta)
    {
        Refresh();
        if (Visible)
            Place();
    }

    /// <summary>
    /// Vertically centred on 600 px of the screen, and ending above the conversation where an enlarged interface brings the two together
    /// (the column scrolls); hidden when that leaves it too little room.
    /// </summary>
    private void Place()
    {
        var canvas = _panel.GetParentControl().Size;
        float top = Math.Max(16, canvas.Y / 2 - 300), bottom = Math.Min(canvas.Y - 16, canvas.Y / 2 + 300);
        if (Conversation?.Invoke() is { } talk && talk.Position.X < _panel.GetGlobalRect().End.X)
        {
            // Only an enlarged interface brings them together, and the HUD's plate has stepped aside: the column may rise to the top.
            top = 16;
            bottom = Math.Min(bottom, talk.Position.Y - 12);
        }
        // With too little room left above an enlarged conversation, the column stands down: the reply's LEARN tag still says what is taught.
        _panel.Visible = bottom - top >= MinRoom;
        _panel.OffsetTop = top;
        _panel.OffsetBottom = Math.Max(top + MinRoom, bottom);
    }

    /// <summary>Shown while a conversation is open with someone who teaches or hires on; redrawn when what it shows changed.</summary>
    public void Refresh()
    {
        if (_session?.Simulation is not { } simulation || simulation.Conversation is not { } talk)
        {
            Visible = false;
            _drawn = string.Empty;
            return;
        }
        string npc = talk.NpcId;
        var teaching = simulation.Teaching(npc);
        var hire = simulation.Hiring.Hireability(npc);
        bool lessons = teaching is { Lessons.IsEmpty: false };
        bool hires = hire is { HiresAs.IsEmpty: false } && simulation.Companions.All(c => c.NpcId != npc);
        Visible = lessons || hires;
        if (!Visible)
            return;
        string signature = $"{npc}|{teaching?.Unavailable}|{string.Join(",", teaching?.Lessons.Select(l => $"{l.DefinitionId}{l.Known}{l.Met}") ?? Array.Empty<string>())}|" +
                           $"{simulation.Player.Currency}|{hire}";
        if (signature == _drawn)
            return;
        _drawn = signature;
        UiKit.Clear(_body);
        string teacher = simulation.Npcs.FirstOrDefault(n => n.Id == npc)?.Name ?? Names.Of(_session, npc);
        if (lessons)
            Learn(simulation, LearnModel.Column(teaching!, teacher, simulation.Player.Currency, id => Names.Of(_session, id), Teaches));
        if (hires && AskAboutWork is { } ask)
        {
            var work = UiKit.Section("Work");
            work.AddChild(UiKit.Words(hire!.Hireable ? $"{teacher} hires on for a wage." : HireModel.Sentence(teacher, hire.Refusal ?? "is not hiring now"), 17,
                hire.Hireable ? HudStyle.Ash : HudStyle.Muted));
            work.AddChild(UiKit.Action("Ask about work", () => ask(npc), id: "learn/work"));
            _body.AddChild(work);
        }
    }

    /// <summary>What a lesson teaches, from the definition it names: the same line the Skills page gives the technique, formula or recipe once known.</summary>
    private string? Teaches(string id)
    {
        var setup = _session!.Setup;
        string Name(string key) => Names.Of(_session, key);
        if (setup.Techniques.TryGetValue(id, out var technique))
            return UNNAMED.Domain.Progression.AvailabilityRules.TechniqueSummary(technique, Name);
        if (setup.Magic.Formulas.TryGetValue(id, out var formula))
            return $"{Name(formula.DomainSkillId)} · complexity {formula.Complexity} · {formula.FocusCost} Focus";
        if (setup.Crafting.Recipes.TryGetValue(id, out var recipe))
            return $"Makes {Name(recipe.OutputItemId)} at {UNNAMED.Domain.Progression.ReasonText.A(Name(recipe.StationKind))} from " +
                   string.Join(", ", recipe.Inputs.Select(i => $"{i.Count} × {Name(i.ItemId)}"));
        return null;
    }

    private void Learn(Simulation simulation, LearnColumn column)
    {
        var section = UiKit.Section("Learn", "a fee buys the lesson, never the skill");
        if (column.Unavailable is { } away)
            section.AddChild(UiKit.Callout(HudStyle.Brass, away));
        foreach (var row in column.Rows)
        {
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 4);
            var head = UiKit.Row(null, row.Name, $"{row.Kind} · {row.Fee}", row.State == LearnState.Known ? HudStyle.Ash : HudStyle.Bone);
            head.Root.AddChild(row.State switch
            {
                LearnState.Known => UiKit.Chip(row.Word, HudStyle.Iron, HudStyle.Ash),
                LearnState.Ready => UiKit.Chip(row.Word, HudStyle.Ember),
                LearnState.InConversation => UiKit.Chip(row.Word, HudStyle.Brass),
                _ => UiKit.Chip(row.Word, HudStyle.Iron, HudStyle.Muted),
            });
            card.AddChild(head.Root);
            if (row.Teaches is { } teaches && row.State != LearnState.Known)
                card.AddChild(UiKit.Words(teaches, 16, HudStyle.Ash));
            if (row.State != LearnState.Known && !row.Chips.IsEmpty)
            {
                var chips = new HFlowContainer();
                chips.AddThemeConstantOverride("h_separation", 6);
                chips.AddThemeConstantOverride("v_separation", 4);
                foreach (var chip in row.Chips)
                    chips.AddChild(chip.Met ? UiKit.Chip(chip.Text, HudStyle.Lichen) : UiKit.Chip(chip.Text, HudStyle.Wound));
                card.AddChild(chips);
            }
            if (row.Why is { } why)
                card.AddChild(UiKit.Words(why, 16, HudStyle.Muted));
            if (row.CanLearn)
            {
                string id = row.DefinitionId;
                card.AddChild(UiKit.Action(row.Fee == "No fee" ? "Learn" : $"Learn for {row.Fee}", () =>
                    _session!.Submit(new LearnFromTeacherCommand(simulation.PlayerId, column.NpcId, id)), UiKit.Kind.Primary, compact: true, id: $"learn/{id}"));
            }
            card.AddChild(HudStyle.Rule(4));
            section.AddChild(card);
        }
        _body.AddChild(section);
    }
}
