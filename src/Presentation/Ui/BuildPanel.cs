// UNNAMED Presentation - the build panel (M7 design §8.4; re-ported onto humgen-main by WP-1G)
// Godot presentation only: it shows build mode's words, and decides nothing (D-11)

using Godot;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The build panel (M7 design §8.4): the pieces with their keys, names and costs, the choice marked, the turn and the keys; under it the
/// ghost's verdict and the target's line. The verdict is toned and also says itself in words, so colour is never the only signal.
/// </summary>
public partial class BuildPanel : PanelContainer
{
    private readonly Label _panel = new() { Name = "Pieces" };
    private readonly Label _status = new() { Name = "Status", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) };
    private readonly Label _target = new() { Name = "Target", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) };

    public override void _Ready()
    {
        AnchorLeft = AnchorRight = 1;
        AnchorTop = AnchorBottom = 0.5f;
        OffsetLeft = -420;
        OffsetRight = -20;
        OffsetTop = -160;
        GrowVertical = GrowDirection.Both;
        MouseFilter = MouseFilterEnum.Ignore;
        var column = new VBoxContainer { Name = "Column" };
        column.AddChild(_panel);
        column.AddChild(_status);
        column.AddChild(_target);
        AddChild(column);
    }

    /// <summary>What build mode says now; hidden when it is not on.</summary>
    public void Show(string? panel, string? status, string? target, BuildTone tone)
    {
        Visible = panel is not null;
        if (panel is null)
            return;
        _panel.Text = panel;
        _status.Text = status ?? string.Empty;
        _status.Modulate = tone switch
        {
            BuildTone.Allowed => new Color(0.55f, 0.95f, 0.60f),
            BuildTone.Refused => new Color(1.00f, 0.55f, 0.50f),
            BuildTone.Unchecked => new Color(1.00f, 0.85f, 0.45f),
            _ => Colors.White,
        };
        _target.Text = target ?? string.Empty;
        _target.Visible = target is not null;
    }

    /// <summary>The words shown, for a scripted run.</summary>
    public string Shown => Visible ? $"{_panel.Text}\n{_status.Text}\n{_target.Text}" : string.Empty;
}
