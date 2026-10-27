using Godot;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Ui;

/// <summary>A queued, non-modal story card. Every control lets the mouse through to normal play.</summary>
public partial class QuestCards : CanvasLayer
{
    public QuestCardQueue Cards { get; } = new();
    private readonly CenterContainer _center = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly PanelContainer _panel = new() { Name = "QuestCard", MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly VBoxContainer _words = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private QuestCard? _drawn;

    public override void _Ready()
    {
        Layer = 6;
        _center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_center);
        _panel.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(30, 24));
        _panel.AddChild(_words);
        _words.AddThemeConstantOverride("separation", 14);
        _center.AddChild(_panel);
        _panel.Visible = false;
    }

    public void Refresh(double delta)
    {
        Cards.Advance(delta);
        _panel.Visible = Cards.Current is not null;
        if (Cards.Current is not { } card)
            return;
        _panel.Modulate = new Color(1, 1, 1, (float)Cards.Opacity);
        if (card == _drawn)
            return;
        _drawn = card;
        foreach (var child in _words.GetChildren()) { _words.RemoveChild(child); child.QueueFree(); }
        // Keep the story-sized type while fitting smaller windows and the player's UI scale.
        float width = Math.Min(card.Started ? 820 : 700, GetViewport().GetVisibleRect().Size.X - 80);
        _panel.CustomMinimumSize = new Vector2(width, 0);
        void Line(string text, Font font, int size, Color colour)
        {
            var label = HudStyle.Text(font, size, colour).Say(text);
            label.CustomMinimumSize = new Vector2(width - 60, 0);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            _words.AddChild(label);
        }
        Line(card.Started ? "QUEST ACCEPTED" : "NEXT", HudStyle.Caps, 18, HudStyle.Brass);
        Line(card.Title, HudStyle.Caps, card.Started ? 36 : 28, HudStyle.Bone);
        if (card.Started)
        {
            if (card.Giver.Length > 0) Line("From " + card.Giver, HudStyle.Body, 18, HudStyle.Brass);
            Line(card.Summary, HudStyle.Body, 23, HudStyle.Bone);
        }
        Line("Next: " + card.Next, HudStyle.Strong, 25, HudStyle.Bone);
        Line($"{HelpPanel.Key("journal")}: journal     {HelpPanel.Key("quest_track")}: track     Esc: dismiss", HudStyle.Body, 17, HudStyle.Ash);
    }
}
