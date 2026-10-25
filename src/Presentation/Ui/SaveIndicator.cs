// UNNAMED Presentation - the corner "Saving…" indicator, and a failed save said until one succeeds (PJ10 §6.2, §6.3.1; SYS-147)
// Godot presentation only: it shows what the session's saves are doing; nothing here holds game state (D-11)

using System.Collections.Immutable;
using Godot;
using UNNAMED.Application;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The lower right corner, above the HUD: "Saving…" while a save is written in the background (never longer than the write, but long
/// enough to be seen), and a failed save's headline - "Not saved: the disk is full." - until a later save is committed. The saves list
/// (L) has the whole sentence and its Copy report.
/// </summary>
public partial class SaveIndicator : CanvasLayer
{
    private readonly SaveIndicatorModel _model = new();
    private Label _label = null!;

    public override void _Ready()
    {
        Layer = 9;
        var root = new Control { Theme = UiKit.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _label = UiKit.Words(string.Empty, 17, HudStyle.Ash);
        _label.AutowrapMode = TextServer.AutowrapMode.Off;
        _label.HorizontalAlignment = HorizontalAlignment.Right;
        _label.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _label.GrowHorizontal = Control.GrowDirection.Begin;
        _label.GrowVertical = Control.GrowDirection.Begin;
        _label.OffsetRight = -32;
        _label.OffsetBottom = -150;
        _label.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_label);
        AddChild(root);
    }

    /// <summary>A frame: whether a save is being written, and the saves that finished on it.</summary>
    public void Show(bool saving, ImmutableArray<SaveOutcome> finished)
    {
        bool succeeded = !finished.IsDefaultOrEmpty && finished.Any(s => s.Failure is null);
        string? failed = !finished.IsDefaultOrEmpty && finished.LastOrDefault(s => s.Failure is not null) is { } failure
            ? failure.Trouble?.Headline ?? "Not saved."
            : null;
        _model.Update(Time.GetTicksMsec() / 1000.0, saving, succeeded, failed);
        _label.Text = _model.Text ?? string.Empty;
        _label.AddThemeColorOverride("font_color", _model.Warning ? HudStyle.Wound : HudStyle.Ash);
    }
}
