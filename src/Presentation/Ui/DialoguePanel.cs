// UNNAMED Presentation - the conversation panel (PROTOTYPE.md §4.1 UI panels; SYSTEMS.md S-28; M4)
// Godot presentation only: every reply submits a command; nothing here changes state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Social;
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
    private readonly Label _line = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(760, 0) };
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

    public override void _Ready()
    {
        Visible = false;
        // Held to the bottom of the screen and grown upwards, so a long line or a long list of replies never runs off it (M-06).
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(800, 0),
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = -400, OffsetRight = 400, OffsetTop = -80, OffsetBottom = -80,
            GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Begin,
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
    private const float RepliesHeight = 420;

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
        Replies = view.Replies.Select(r => r.Id).ToList();
        for (int i = 0; i < view.Replies.Length; i++)
        {
            var reply = view.Replies[i];
            var button = new Button { Text = _styled ? reply.Text : $"{i + 1}. {reply.Text}", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
            button.AddThemeFontSizeOverride("font_size", 17);
            if (_styled)
            {
                StyleReply(button, i + 1);
                Tag(button, Choice(view, reply.Id));
            }
            string id = reply.Id;
            button.Pressed += () => Answer(id);
            _replies.AddChild(button);
        }
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(_replies.GetCombinedMinimumSize().Y, RepliesHeight));
    }

    private void Style(PanelContainer panel, VBoxContainer column)
    {
        panel.Theme = HudStyle.Theme;
        panel.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(22, 16));
        panel.CustomMinimumSize = new Vector2(820, 0);
        panel.OffsetLeft = -410;
        panel.OffsetRight = 410;
        panel.OffsetTop = panel.OffsetBottom = -72;
        column.AddThemeConstantOverride("separation", 9);
        _speaker.Uppercase = true;
        _speaker.AddThemeFontOverride("font", HudStyle.Caps);
        _speaker.AddThemeFontSizeOverride("font_size", 21);
        _speaker.AddThemeColorOverride("font_color", HudStyle.Ember);
        _line.AddThemeFontOverride("font", HudStyle.Body);
        _line.AddThemeFontSizeOverride("font_size", 19);
        _line.AddThemeConstantOverride("line_spacing", 4);
        _line.CustomMinimumSize = new Vector2(776, 0);
        _replies.AddThemeConstantOverride("separation", 4);
        column.AddChild(HudStyle.Rule(2));
    }

    /// <summary>A reply row: its words in the HUD's type, its number on a key cap at the left, ember at the edge under the pointer.</summary>
    private static void StyleReply(Button button, int number)
    {
        button.AddThemeFontOverride("font", HudStyle.Body);
        button.AddThemeFontSizeOverride("font_size", 18);
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
                case GiveRecipeConsequence:
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

    /// <summary>Give a reply by its number on screen (keys 1 to 9).</summary>
    public void AnswerNumber(int number)
    {
        if (number >= 1 && number <= Replies.Count)
            Answer(Replies[number - 1]);
    }

    public void Answer(string replyId) => _session.Submit(new ChooseCommand(_session.Simulation!.PlayerId, replyId));

    public void Leave() => _session.Submit(new LeaveCommand(_session.Simulation!.PlayerId));
}
