// UNNAMED Presentation - the conversation panel (PROTOTYPE.md §4.1 UI panels; SYSTEMS.md S-28; M4)
// Godot presentation only: every reply submits a command; nothing here changes state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The open conversation: who speaks, their line, and the replies on offer now - numbered, so keys 1 to 9 answer as well as
/// the mouse. It redraws from the simulation's conversation view whenever a line is spoken; it holds no state of its own. A line with
/// a recording is said aloud as it arrives (WP5), and its words show while subtitles are on; a line without one always shows them.
/// </summary>
public partial class DialoguePanel : CanvasLayer
{
    private readonly Label _speaker = new();
    private readonly Label _line = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(PanelWidth - 40, 0) };
    private readonly VBoxContainer _replies = new();
    private GameSession _session = null!;

    // The production HUD's look (visual option hud=production): the HUD's soot panel and type, the speaker in ember capitals over an
    // iron rule, and each reply a quiet row with its number on a key cap. The same replies, the same keys; classic stays as it was.
    private readonly bool _styled = VisualOptions.ProductionHud;

    public void Bind(GameSession session) => _session = session;

    /// <summary>Who says the lines aloud; none in a run without sound.</summary>
    public Audio.VoicePlayer? Voice { get; set; }

    /// <summary>The line last said (aloud, or found to have no recording), so a redraw of the same line does not say it again.</summary>
    private string? _said;

    /// <summary>The replies on screen, in order: what keys 1 to 9 answer.</summary>
    public IReadOnlyList<string> Replies { get; private set; } = Array.Empty<string>();

    private PanelContainer _panel = null!;

    /// <summary>Where the conversation stands while it is open, or null: the Learn column keeps clear of it.</summary>
    public Rect2? PanelRect => Visible ? _panel.GetGlobalRect() : null;

    public override void _Ready()
    {
        Visible = false;
        // Held to the bottom of the screen and grown upwards, so a long line or a long list of replies never runs off it (M-06).
        var panel = _panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0),
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = -(PanelWidth + 40), OffsetRight = -40, OffsetTop = -80, OffsetBottom = -80,
            GrowHorizontal = Control.GrowDirection.Begin, GrowVertical = Control.GrowDirection.Begin,
        };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        _speaker.AddThemeFontSizeOverride("font_size", 20);
        _line.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(_speaker);
        if (_styled)
            Style(panel, column);
        column.AddChild(_line);
        _replies.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _scroll.AddChild(_replies);
        column.AddChild(_scroll);
        panel.AddChild(column);
        AddChild(panel);
    }

    /// <summary>The replies scroll past this height (M-06): nine long ones stay on the screen.</summary>
    private const float RepliesHeight = 300;

    /// <summary>The panel stands at the right edge, so the speaker in the middle of the screen stays in view.</summary>
    private const float PanelWidth = 560;

    /// <summary>Whether the topic questions are showing in place of the top level; a new line starts at the top level again.</summary>
    private bool _asking;

    private string? _askingAt;

    /// <summary>
    /// Its foot as built, raised above whatever the HUD keeps beside it once it has stepped aside at an enlarged interface (health and stamina),
    /// so the conversation never covers them.
    /// </summary>
    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        float foot = _styled ? -72 : -80;
        if (GetTree().Root.FindChild("HudView", true, false) is HudView { Aside: true } hud)
        {
            float bottom = _panel.GetViewportRect().Size.Y;
            var across = _panel.GetGlobalRect();
            foreach (var rect in hud.Intrusions().Where(r => r.Position.X < across.End.X && r.End.X > across.Position.X))
                foot = Math.Min(foot, rect.Position.Y - 12 - bottom);
        }
        _panel.OffsetTop = _panel.OffsetBottom = foot;
    }

    private bool _voiced;

    private readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };

    /// <summary>
    /// Show the conversation as it stands now, or hide the panel when there is none. <paramref name="spoken"/>: a line was just said
    /// (arriving at it again says it again); any other redraw says a line only when it is new.
    /// </summary>
    public void Refresh(bool spoken = false)
    {
        if (_session.Simulation?.Conversation is not { } view)
        {
            if (_said is not null)
                Voice?.Hush();
            _said = null;
            Visible = false;
            Replies = Array.Empty<string>();
            return;
        }
        Visible = true;
        _speaker.Text = Names.Of(_session, view.NpcId);
        _line.Text = view.Text;
        if (spoken || view.LineId != _said)
        {
            _said = view.LineId;
            _voiced = Voice?.Say(view.LineId) ?? false;
        }
        _line.Visible = Audio.VoiceRules.ShowWords(_voiced, Settings.SettingsStore.Current.Profile.Interface.Subtitles);
        foreach (var child in _replies.GetChildren())
        {
            _replies.RemoveChild(child);   // now, so the list's height is its new replies'
            child.QueueFree();
        }
        string at = $"{view.DialogueId}/{view.NodeId}";
        if (at != _askingAt)
        {
            _askingAt = at;
            _asking = false;
        }
        var offered = view.Replies.Select(r => (r.Id, Choice: Choice(view, r.Id))).ToList();
        var importance = offered.ToDictionary(r => r.Id, r => Importance(view, r.Choice));
        DialogueMenu.Group Rank(string id) => importance[id];
        bool Read(string id) => offered.First(r => r.Id == id).Choice?.Next is { } next && _session.Simulation.DialogueVisited(view.DialogueId, next);
        int unread = offered.Count(r => Rank(r.Id) == DialogueMenu.Group.Topic && !Read(r.Id));
        var shown = (_asking ? DialogueMenu.Topics(offered, Rank, Read) : DialogueMenu.Top(offered, Rank, Read)).Select(id => id switch
        {
            DialogueMenu.Ask => new ReplyView(id, unread > 0 ? $"Ask about... ({unread} unread)" : "Ask about..."),
            DialogueMenu.Back => new ReplyView(id, "Back"),
            _ => view.Replies.First(r => r.Id == id),
        }).ToList();
        Replies = shown.Select(r => r.Id).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            var reply = shown[i];
            var button = new Button { Text = _styled ? reply.Text : $"{i + 1}. {reply.Text}", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
            button.AddThemeFontSizeOverride("font_size", 17);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (_styled)
            {
                StyleReply(button, i + 1);
                Tag(button, Choice(view, reply.Id));
                if (importance.GetValueOrDefault(reply.Id) is DialogueMenu.Group.QuestAdvance)
                    button.Text = "◆ " + button.Text;
            }
            string id = reply.Id;
            button.Pressed += () => Answer(id);
            _replies.AddChild(button);
        }
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(_replies.GetCombinedMinimumSize().Y, RepliesHeight));
    }

    // A reply may advance a quest merely by arriving at a line; it need not carry a consequence.
    private DialogueMenu.Group Importance(ConversationView view, DialogueChoice? choice)
    {
        var rank = DialogueMenu.Classify(choice);
        if (choice is null) return rank;
        foreach (var quest in _session.Simulation!.Quests.Where(q => q.Status == QuestStatus.Active))
        {
            var definition = _session.Setup.Quests.Quests[quest.Id];
            foreach (var objective in quest.Objectives.Where(o => o.Status == ObjectiveStatus.Active))
            {
                bool advances = definition.Objectives[objective.Id].Condition switch
                {
                    TalkTo talk => talk.DialogueId == view.DialogueId && choice.Next is { } next && talk.Nodes.Contains(next),
                    ChoiceGiven given => given.DialogueId == view.DialogueId && given.NodeId == view.NodeId && given.ChoiceId == choice.Id,
                    RelationshipValueObjective relation => choice.Consequences.OfType<RelationshipEventConsequence>().Any(c => c.NpcId == relation.NpcId && c.Dimension == relation.Dimension),
                    CompanionPresent companion => companion.NpcId == view.NpcId && choice.Consequences.Any(c => c is RecruitCompanionConsequence),
                    WorldStateObjective flag => choice.Consequences.OfType<SetWorldFlagConsequence>().Any(c => c.FlagId == flag.FlagId),
                    _ => false,
                };
                if (advances) return DialogueMenu.Group.QuestAdvance;
            }
        }
        if (rank == DialogueMenu.Group.Topic && choice.Next is { } target &&
            _session.Setup.Social.Dialogues[view.DialogueId].Nodes.TryGetValue(target, out var node) &&
            node.Choices.Any(c => c.Consequences.Any(effect => effect is StartQuestConsequence)))
            return DialogueMenu.Group.QuestOffer;
        return rank;
    }

    private void Style(PanelContainer panel, VBoxContainer column)
    {
        panel.Theme = HudStyle.Theme;
        panel.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(22, 16));
        panel.CustomMinimumSize = new Vector2(PanelWidth, 0);
        panel.OffsetLeft = -(PanelWidth + 40);
        panel.OffsetRight = -40;
        panel.OffsetTop = panel.OffsetBottom = -72;
        column.AddThemeConstantOverride("separation", 9);
        _speaker.Uppercase = true;
        _speaker.AddThemeFontOverride("font", HudStyle.Caps);
        // At their designed sizes times Settings' text size, and kept so a change of text size reaches them (UiKit.Restyle).
        _speaker.SetMeta(HudStyle.DesignedSize, 21);
        _speaker.AddThemeFontSizeOverride("font_size", HudStyle.Sized(21));
        _speaker.AddThemeColorOverride("font_color", HudStyle.Ember);
        _line.AddThemeFontOverride("font", HudStyle.Body);
        _line.SetMeta(HudStyle.DesignedSize, 19);
        _line.AddThemeFontSizeOverride("font_size", HudStyle.Sized(19));
        _line.AddThemeConstantOverride("line_spacing", 4);
        _line.CustomMinimumSize = new Vector2(PanelWidth - 44, 0);
        _replies.AddThemeConstantOverride("separation", 4);
        column.AddChild(HudStyle.Rule(2));
    }

    /// <summary>A reply row: its words in the HUD's type, its number on a key cap at the left, ember at the edge under the pointer.</summary>
    private static void StyleReply(Button button, int number)
    {
        button.AddThemeFontOverride("font", HudStyle.Body);
        button.SetMeta(HudStyle.DesignedSize, 18);   // so a change of text size reaches an open row (UiKit.Restyle)
        button.AddThemeFontSizeOverride("font_size", HudStyle.Sized(18));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_focus_color", "font_hover_pressed_color" })
            button.AddThemeColorOverride(state, HudStyle.Bone);
        button.AddThemeColorOverride("font_pressed_color", HudStyle.Ember);
        StyleBoxFlat Row(Color fill, Color edge) => new()
        {
            BgColor = fill, BorderColor = edge, BorderWidthLeft = 2,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ContentMarginLeft = 48, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        button.AddThemeStyleboxOverride("normal", Row(new Color(1, 1, 1, 0.035f), HudStyle.Iron));
        var hover = Row(new Color(HudStyle.Ember, 0.12f), HudStyle.Ember);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("hover_pressed", hover);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var key = new HudView.KeyCap($"{number}")
        {
            AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = 12, OffsetRight = 12, OffsetTop = 0, OffsetBottom = 0,
            GrowVertical = Control.GrowDirection.Both,
        };
        button.AddChild(key);
    }

    /// <summary>The content behind a reply on screen: what it asks and what it does.</summary>
    private DialogueChoice? Choice(ConversationView view, string replyId) =>
        _session.Setup.Social.Dialogues.TryGetValue(view.DialogueId, out var dialogue) && dialogue.Nodes.TryGetValue(view.NodeId, out var node)
            ? node.Choices.FirstOrDefault(c => c.Id == replyId)
            : null;

    /// <summary>
    /// What a reply will do, as chips at its right, read off its own consequences and conditions (the owner's second playtest: "no
    /// indication which dialog selection is to continue or complete a quest"): it starts a quest, it belongs to one under way, it hands
    /// something over or receives it, it opens trade, the speaker joins you.
    /// </summary>
    private void Tag(Button button, DialogueChoice? choice)
    {
        if (choice is null)
            return;
        var tags = new List<(string Text, Color Tone)>();
        foreach (var consequence in choice.Consequences)
        {
            switch (consequence)
            {
                case StartQuestConsequence:
                    tags.Add(("New quest", HudStyle.Ember));
                    break;
                case TransferItemConsequence { ToPlayer: false } give:
                    tags.Add(($"Give {give.Count} {Names.Of(_session, give.ItemId)}", HudStyle.Brass));
                    break;
                case TransferItemConsequence take:
                    tags.Add(($"Receive {Names.Of(_session, take.ItemId)}", HudStyle.Lichen));
                    break;
                case OpenServiceConsequence:
                    tags.Add(("Trade", HudStyle.IronBright));
                    break;
                case RecruitCompanionConsequence:
                    tags.Add(("Joins you", HudStyle.Lichen));
                    break;
                case HireHirelingConsequence hire:
                    // DN9: a hireling can die, and the hire says so before it is agreed.
                    tags.Add(($"Hire: {_session.Simulation?.Party?.Roles.GetValueOrDefault(hire.Role)?.DailyWage ?? 0} a day, {hire.Days} days - can die", HudStyle.Ember));
                    break;
                case DismissCompanionConsequence:
                    tags.Add(("Leaves you", HudStyle.Brass));
                    break;
                case TeachConsequence:
                    tags.Add(("Learn", HudStyle.Lichen));
                    break;
            }
        }
        if (!tags.Any(t => t.Text == "New quest") && choice.Conditions.Any(c => c is QuestStateCondition { Negated: false, State: "active" or "satisfied" }))
            tags.Insert(0, ("Quest", HudStyle.Brass));
        if (tags.Count == 0)
            return;
        var chips = new HBoxContainer
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -12, OffsetRight = -12,
            GrowHorizontal = Control.GrowDirection.Begin, GrowVertical = Control.GrowDirection.Both, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        chips.AddThemeConstantOverride("separation", 6);
        foreach (var (text, tone) in tags.Distinct())
            chips.AddChild(UiKit.Chip(text, tone));
        button.AddChild(chips);
        // The words stop short of the chips.
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            ((StyleBoxFlat)button.GetThemeStylebox(state)).ContentMarginRight = 24 + chips.GetCombinedMinimumSize().X;
    }

    private readonly Models.DialogueSkipRules _skip = new();

    /// <summary>
    /// The skip key, a frame of it (tap: silence the line and go on when there is one plain way on; held: fast-forward through lone replies).
    /// Never picks between replies, and never takes one that does something - a quest, a hire, an exchange.
    /// </summary>
    public void Skip(bool pressed, bool justPressed, double seconds)
    {
        if (_session.Simulation?.Conversation is not { } view)
            return;
        var lone = view.Replies.Length == 1 ? Choice(view, view.Replies[0].Id) : null;
        bool plain = view.Replies.Length == 1 && (lone is null || lone.Consequences.Length == 0);
        var step = _skip.Step(pressed, justPressed, seconds, view.Replies.Length, plain);
        if (step.Hush)
            Voice?.Hush();
        if (step.Advance)
            Answer(view.Replies[0].Id);
    }

    /// <summary>Give a reply by its number on screen (keys 1 to 9).</summary>
    public void AnswerNumber(int number)
    {
        if (number >= 1 && number <= Replies.Count)
            Answer(Replies[number - 1]);
    }

    public void Answer(string replyId)
    {
        if (replyId is DialogueMenu.Ask or DialogueMenu.Back)
        {
            _asking = replyId == DialogueMenu.Ask;
            Refresh();
            return;
        }
        _session.Submit(new ChooseCommand(_session.Simulation!.PlayerId, replyId));
    }

    public void Leave() => _session.Submit(new LeaveCommand(_session.Simulation!.PlayerId));
}
