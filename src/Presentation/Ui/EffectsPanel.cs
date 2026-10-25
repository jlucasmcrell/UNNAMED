// UNNAMED Presentation - what is on the character: wounds, draughts, workings (the Field Instrument UI, design record 06)
// Godot presentation only: it reads the combat view; nothing here holds state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Effects, from the System Menu: a STANDARD sheet with Strained first as a wound callout, then a row an effect - harm or aid, its name
/// and stacks, what it does, and a time gauge with the seconds left that run down in place, which is the screen's proof that the world
/// is still running. Seconds, never ticks.
/// </summary>
public partial class EffectsPanel : CanvasLayer
{
    private readonly List<(string EffectId, HudView.Gauge Gauge, Label Left)> _timers = new();
    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private HudIcons _icons = HudIcons.None;
    private string? _drawn;

    public void Bind(GameSession session) => _session = session;

    /// <summary>The icon set: each effect's tile (the HUD's), and the screen's own.</summary>
    public void UseIcons(HudIcons icons)
    {
        _icons = icons;
        _sheet.SetIcon(icons.Binds("panel.effects") ? icons.For("panel.effects") : null);
        _drawn = null;
    }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Effects", null);
        _sheet.SetHint("What is on you.");
        _sheet.OnShown(() => _drawn = null);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        var combat = simulation.Combat;
        string signature = string.Join(",", combat.Effects.Select(e => $"{e.EffectId}x{e.Stacks}")) + combat.Strained;
        if (signature != _drawn)
        {
            _drawn = signature;
            Build(combat.Effects, combat.Strained);
        }
        foreach (var (id, gauge, left) in _timers)
        {
            if (combat.Effects.FirstOrDefault(e => e.EffectId == id) is not { } effect)
                continue;
            // Gear's worn effects (spec 47) never run out: they last while the piece is worn.
            if (effect.ExpiresTick == long.MaxValue)
            {
                gauge.Set(1, 1);
                left.Text = "worn";
                continue;
            }
            long ticks = Math.Max(0, effect.ExpiresTick - simulation.WorldTick);
            int whole = _session.Setup.Combat.Effects.TryGetValue(id, out var definition) ? definition.DurationTicks : (int)ticks;
            gauge.Set(ticks, Math.Max(1, whole));
            left.Text = $"{ticks * _session.TickSeconds:0.0} s";
        }
    }

    private void Build(IReadOnlyList<ActiveEffect> effects, bool strained)
    {
        _timers.Clear();
        UiKit.Clear(_sheet.Body);
        if (strained)
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound,
                "Strained: the last casting outran your tolerance, and the overflow is coming for your health."));
        if (effects.Count == 0 && !strained)
        {
            _sheet.Body.AddChild(UiKit.EmptyState("Nothing on you", "Nothing is on you: no wound, no draught, no working.",
                "Wounds, draughts and workings appear here with the time they have left."));
            return;
        }
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        foreach (var effect in effects)
        {
            var definition = _session.Setup.Combat.Effects.GetValueOrDefault(effect.EffectId);
            bool harm = definition is { DamagePerTick: > 0 } || definition is { DamageDealtMultiplier: < 1 } || definition is { StaminaRegenMultiplier: < 1 };
            var chip = UiKit.Chip(harm ? "Harm" : "Aid", harm ? HudStyle.Wound : HudStyle.Lichen);
            chip.CustomMinimumSize = new Vector2(64, 0);
            string stacks = effect.Stacks > 1 || definition is { MaxStacks: > 1 } ? $"  ×{effect.Stacks} of {definition?.MaxStacks ?? effect.Stacks}" : "";
            // The effect's tile at the left (02 §7: an empty frame when none is bound), the harm or aid chip with its time at the right.
            var row = UiKit.Row(UiKit.Icon(_icons, effect.EffectId, 36), Names.Of(_session, effect.EffectId) + stacks, Does(definition));
            if (definition is not null)
                row.Secondary.AddThemeColorOverride("font_color", harm ? HudStyle.Wound : HudStyle.Ash);
            row.Root.AddChild(chip);
            var gauge = UiKit.Gauge(HudStyle.Brass, 140, 6);
            row.Root.AddChild(gauge);
            var left = HudStyle.Text(HudStyle.Strong, 20, HudStyle.Brass);
            left.CustomMinimumSize = new Vector2(72, 0);
            left.HorizontalAlignment = HorizontalAlignment.Right;
            left.VerticalAlignment = VerticalAlignment.Center;
            row.Root.AddChild(left);
            _timers.Add((effect.EffectId, gauge, left));
            list.AddChild(row.Root);
        }
        _sheet.Body.AddChild(list);
    }

    /// <summary>What an effect does, from its definition, in seconds: "4 harm every 1 s", "heals 3 every 1 s", "armour +2".</summary>
    private string Does(EffectDefinition? effect)
    {
        if (effect is null)
            return string.Empty;
        string every = $"every {effect.TickIntervalTicks * _session.TickSeconds:0.#} s";
        var parts = new List<string>();
        if (effect.DamagePerTick > 0)
            parts.Add($"{effect.DamagePerTick} {Names.Words(effect.DamageType)} harm {every}");
        if (effect.HealPerTick > 0)
            parts.Add($"heals {effect.HealPerTick} {every}");
        if (effect.ArmorBonus != 0)
            parts.Add($"armour {effect.ArmorBonus:+#;-#}");
        if (Math.Abs(effect.DamageDealtMultiplier - 1) > 0.001)
            parts.Add($"blows {(effect.DamageDealtMultiplier - 1) * 100:+0;-0}%");
        if (Math.Abs(effect.StaminaRegenMultiplier - 1) > 0.001)
            parts.Add($"stamina returns {(effect.StaminaRegenMultiplier - 1) * 100:+0;-0}%");
        return $"{Names.Words(effect.Category)} · " + (parts.Count == 0 ? "no change to the body" : string.Join(", ", parts));
    }
}
