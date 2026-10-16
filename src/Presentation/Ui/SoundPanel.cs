// UNNAMED Presentation - Sound, from the System Menu: the master, music and effects volumes (the Field Instrument UI)
// Godot presentation only (D-11): how loud the game is, kept for the next launch; nothing here is game state or part of a save

using Godot;
using UNNAMED.Presentation.Audio;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Three sliders - Master, Music, Effects - heard as they move and saved when they settle (<see cref="AudioMix"/>). Music at zero
/// silences the score without touching the world's sounds.
/// </summary>
public partial class SoundPanel : CanvasLayer
{
    private Sheet _sheet = null!;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Sound", null);
        _sheet.SetHint("How loud the world, the music and everything together are.");
        _sheet.OnShown(Build);
    }

    private void Build()
    {
        UiKit.Clear(_sheet.Body);
        var volumes = AudioMix.Saved();
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 16);
        HSlider? first = null;
        foreach (var (name, value, with) in new (string, float, Func<AudioMix.Volumes, float, AudioMix.Volumes>)[]
                 {
                     ("Master", volumes.Master, (v, x) => v with { Master = x }),
                     ("Music", volumes.Music, (v, x) => v with { Music = x }),
                     ("Effects", volumes.Effects, (v, x) => v with { Effects = x }),
                 })
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 16);
            var label = UiKit.Words(name);
            label.CustomMinimumSize = new Vector2(140, 0);
            var shown = UiKit.Caption($"{value * 100:0}%");
            shown.CustomMinimumSize = new Vector2(64, 0);
            var slider = UiKit.Id(new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(360, 32),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }, $"volume.{name.ToLowerInvariant()}");
            slider.ValueChanged += x =>
            {
                volumes = with(volumes, (float)x);
                AudioMix.Apply(volumes);
                shown.Text = $"{x * 100:0}%";
            };
            slider.DragEnded += _ => AudioMix.Save(volumes);
            slider.FocusExited += () => AudioMix.Save(volumes);
            first ??= slider;
            row.AddChild(label);
            row.AddChild(slider);
            row.AddChild(shown);
            list.AddChild(row);
        }
        _sheet.Body.AddChild(list);
        first?.GrabFocus();
    }
}
