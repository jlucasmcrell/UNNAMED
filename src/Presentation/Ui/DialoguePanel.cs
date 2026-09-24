// UNNAMED Presentation - the conversation panel (PROTOTYPE.md §4.1 UI panels; SYSTEMS.md S-28; M4)
// Godot presentation only: every reply submits a command; nothing here changes state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The open conversation: who speaks, their line, and the replies on offer now - numbered, so keys 1 to 9 answer as well as
/// the mouse. It redraws from the simulation's conversation view whenever a line is spoken; it holds no state of its own.
/// </summary>
public partial class DialoguePanel : CanvasLayer
{
    private readonly Label _speaker = new();
    private readonly Label _line = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(760, 0) };
    private readonly VBoxContainer _replies = new();
    private GameSession _session = null!;

    public void Bind(GameSession session) => _session = session;

    /// <summary>The replies on screen, in order: what keys 1 to 9 answer.</summary>
    public IReadOnlyList<string> Replies { get; private set; } = Array.Empty<string>();

    public override void _Ready()
    {
        Visible = false;
        var panel = new PanelContainer { Position = new Vector2(560, 640), CustomMinimumSize = new Vector2(800, 0) };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        _speaker.AddThemeFontSizeOverride("font_size", 20);
        _line.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(_speaker);
        column.AddChild(_line);
        column.AddChild(_replies);
        panel.AddChild(column);
        AddChild(panel);
    }

    /// <summary>Show the conversation as it stands now, or hide the panel when there is none.</summary>
    public void Refresh()
    {
        if (_session.Simulation?.Conversation is not { } view)
        {
            Visible = false;
            Replies = Array.Empty<string>();
            return;
        }
        Visible = true;
        _speaker.Text = _session.DisplayName(view.NpcId);
        _line.Text = view.Text;
        foreach (var child in _replies.GetChildren())
            child.QueueFree();
        Replies = view.Replies.Select(r => r.Id).ToList();
        for (int i = 0; i < view.Replies.Length; i++)
        {
            var reply = view.Replies[i];
            var button = new Button { Text = $"{i + 1}. {reply.Text}", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
            button.AddThemeFontSizeOverride("font_size", 17);
            string id = reply.Id;
            button.Pressed += () => Answer(id);
            _replies.AddChild(button);
        }
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
