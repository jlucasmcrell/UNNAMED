// UNNAMED Presentation - the character sheet (PROTOTYPE.md §4.1 UI panels; the owner's M6 playtest; the Field Instrument UI)
// Godot presentation only: it reads views and submits one command; the points being weighed are presentation state only (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Progression;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The character (K), on a WIDE sheet in three columns that read left to right as what you choose, what it makes you, and what you
/// carry and know: the attributes Phase 1 puts in play and what a point in each changes; the derived values, what is on you; what is
/// worn, the skills most practised and what is known and where it was learned. Only what the game has now (no archetype, soul or
/// faction). A level-up's points are weighed here before they are spent (the owner's first playtest: "a chance to back out of a point
/// ... it will show you what changes under derived"): + and − move points between attributes, the derived column shows what they
/// would change, and only Spend submits the existing <see cref="SpendAttributeCommand"/> - one a point.
/// </summary>
public partial class CharacterPanel : CanvasLayer
{
    private const float Column = 352;

    private readonly Dictionary<CharacterAttribute, int> _weighing = new();
    private Sheet _sheet = null!;
    private GameSession? _session;
    private string _drawn = string.Empty;

    public void Bind(GameSession session) => _session = session;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Character", "character", UiKit.Wide);
        _sheet.OnShown(() =>
        {
            _drawn = string.Empty;
            Refresh();
            if (UiKit.FocusedId(_sheet.Root) is null)
                UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
        });
        VisibilityChanged += () =>
        {
            if (!Visible)
                _weighing.Clear();
        };
    }

    /// <summary>Redraw when anything shown has changed (it is asked every frame: the world runs while the sheet is open).</summary>
    public void Refresh()
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        var view = simulation.Player;
        int unspent = view.Progression.UnspentAttributePoints;
        // A point spent elsewhere, or a load, can leave fewer to weigh than are being weighed: weigh no more than there are.
        while (_weighing.Values.Sum() > unspent)
            _weighing[_weighing.First(kv => kv.Value > 0).Key]--;
        var progression = view.Progression;
        string signature = $"{progression.Level}|{progression.LevelProgressXp}|{progression.XpDebt}|{unspent}|" +
                           string.Join(",", ProgressionKeys.Attributes.Select(a => ProgressionEngine.AttributeValue(progression, a, _session.Setup.Progression))) + "|" +
                           string.Join(",", progression.Skills.Select(s => $"{s.Key}:{s.Value.Level}:{s.Value.ProgressXp}")) + $"|{progression.Known.Count}|" +
                           $"{view.Name}|{view.Stats}|{view.Armor}|{view.CarriedGrams}|{view.CarryLimitGrams}|" +
                           string.Join(",", view.Equipment.Select(kv => $"{kv.Key}:{kv.Value}")) + "|" +
                           string.Join(",", simulation.Combat.Effects.Select(e => $"{e.EffectId}x{e.Stacks}")) + $"|{simulation.Combat.Strained}|" +
                           string.Join(",", _weighing.Select(kv => $"{kv.Key}{kv.Value}"));
        if (signature == _drawn)
            return;
        _drawn = signature;
        string? focused = UiKit.FocusedId(_sheet.Root);
        UiKit.Clear(_sheet.Body);
        _sheet.Body.AddChild(Identity(view));
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        columns.AddChild(Attributes(view));
        columns.AddChild(Derived(simulation, view));
        columns.AddChild(Carried(view));
        _sheet.Body.AddChild(columns);
        if (focused is not null && !UiKit.Refocus(_sheet.Root, focused))
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }

    /// <summary>The band across the top: the name, the level, the XP gauge and its numbers, and what is owed or waiting.</summary>
    private Control Identity(PlayerView view)
    {
        var rules = _session!.Setup.Progression;
        var progression = view.Progression;
        var band = new VBoxContainer();
        band.AddThemeConstantOverride("separation", 6);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        var name = HudStyle.Text(HudStyle.Caps, 30, HudStyle.Bone, caps: true);
        name.Text = view.Name;
        top.AddChild(name);
        var level = UiKit.Caption($"Level {progression.Level}", HudStyle.Ember, 20);
        top.AddChild(level);
        if (progression.Level >= rules.LevelCap)
        {
            top.AddChild(UiKit.Caption("The highest level", HudStyle.Brass));
        }
        else
        {
            long toNext = rules.Curve.ToReach(progression.Level + 1);
            var gauge = UiKit.Gauge(HudStyle.XpFill, 320);
            gauge.Set(progression.LevelProgressXp, toNext);
            top.AddChild(gauge);
            top.AddChild(UiKit.Caption("XP"));
            var xp = HudStyle.Text(HudStyle.Strong, 18, HudStyle.Brass);
            xp.Text = $"{progression.LevelProgressXp} / {toNext}";
            xp.VerticalAlignment = VerticalAlignment.Center;
            top.AddChild(xp);
            top.AddChild(UiKit.Caption($"to level {progression.Level + 1}"));
        }
        var chips = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        chips.AddThemeConstantOverride("separation", 8);
        if (progression.XpDebt > 0)
            chips.AddChild(UiKit.Chip($"XP debt {progression.XpDebt}", HudStyle.Wound));
        if (progression.UnspentAttributePoints > 0)
            chips.AddChild(UiKit.Chip($"{progression.UnspentAttributePoints} {(progression.UnspentAttributePoints == 1 ? "point" : "points")} to spend", HudStyle.Ember));
        top.AddChild(chips);
        band.AddChild(top);
        string hint = "Every character starts from the same package; a level-up grants an attribute point.";
        if (progression.XpDebt > 0)
            hint += " The XP debt is repaid from the next XP earned.";
        band.AddChild(UiKit.Words(hint, 16, HudStyle.Muted));
        return band;
    }

    /// <summary>The attributes in play: each value, what a point in it changes, and - with points to spend - the means to weigh them.</summary>
    private Control Attributes(PlayerView view)
    {
        var rules = _session!.Setup.Progression;
        int unspent = view.Progression.UnspentAttributePoints;
        int weighed = _weighing.Values.Sum();
        var column = UiKit.Section("Attributes", unspent > 0 ? "weigh, then spend" : "a level-up grants a point");
        column.CustomMinimumSize = new Vector2(Column, 0);
        column.AddThemeConstantOverride("separation", 8);
        foreach (var attribute in rules.Derived.Live)
        {
            int value = ProgressionEngine.AttributeValue(view.Progression, attribute, rules);
            int more = _weighing.GetValueOrDefault(attribute);
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 36) };
            row.AddThemeConstantOverride("separation", 8);
            var label = UiKit.Caption(Label(attribute));
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);
            var shown = HudStyle.Text(HudStyle.Strong, 28, more > 0 ? HudStyle.Lichen : HudStyle.Bone);
            shown.Text = more > 0 ? $"{value + more}" : $"{value}";
            shown.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(shown);
            if (unspent > 0)
            {
                var less = UiKit.Action("−", () => Weigh(attribute, -1), compact: true, id: $"less/{attribute}");
                less.Disabled = more == 0;
                less.TooltipText = $"Take back a point from {Label(attribute)}";
                row.AddChild(less);
                var plus = UiKit.Action($"+1 {Label(attribute)}", () => Weigh(attribute, 1), UiKit.Kind.Primary, compact: true, id: $"more/{attribute}");
                plus.Disabled = weighed >= unspent;
                row.AddChild(plus);
            }
            column.AddChild(row);
            column.AddChild(UiKit.Words(Effects(_session, attribute).Replace(", ", " · "), 17, HudStyle.Ash));
        }
        if (unspent > 0)
        {
            var actions = new HBoxContainer();
            actions.AddThemeConstantOverride("separation", 8);
            var spend = UiKit.Action(weighed == 0 ? "Spend" : $"Spend {weighed} {(weighed == 1 ? "point" : "points")}", Spend, UiKit.Kind.Primary, id: "spend");
            spend.Disabled = weighed == 0;
            actions.AddChild(spend);
            var undo = UiKit.Action("Take them back", () =>
            {
                _weighing.Clear();
                Callable.From(Refresh).CallDeferred();
            }, id: "undo");
            undo.Disabled = weighed == 0;
            actions.AddChild(undo);
            column.AddChild(actions);
            column.AddChild(UiKit.Words(weighed == 0 ? "Nothing is spent until you press Spend."
                : $"{unspent - weighed} of {unspent} left to place. Nothing is spent until you press Spend.", 16, HudStyle.Muted));
        }
        var idle = ProgressionKeys.Attributes.Where(a => !rules.Derived.Live.Contains(a)).Select(Label).ToList();
        if (idle.Count > 0)
            column.AddChild(UiKit.Words($"Not in play yet: {string.Join(", ", idle)}.", 16, HudStyle.Muted));
        return column;
    }

    /// <summary>The derived values in an aligned grid in the pools' colours - with what the weighed points would add - then what is on you.</summary>
    private Control Derived(Simulation simulation, PlayerView view)
    {
        var derived = _session!.Setup.Progression.Derived;
        var stats = view.Stats;
        var column = UiKit.Section("Derived");
        column.CustomMinimumSize = new Vector2(Column, 0);
        column.AddThemeConstantOverride("separation", 6);
        long Gain(DerivedFormula formula) => _weighing.Sum(kv => formula.PerPoint.GetValueOrDefault(kv.Key) * kv.Value);
        foreach (var (label, value, formula, pool) in new (string, long, DerivedFormula?, Color?)[]
                 {
                     ("Health", stats.HealthMax, derived.HealthMax, HudStyle.HealthFill), ("Stamina", stats.StaminaMax, derived.StaminaMax, HudStyle.StaminaFill),
                     ("Focus", stats.FocusMax, derived.FocusMax, HudStyle.FocusFill), ("Resonance", stats.Resonance, derived.Resonance, null),
                     ("Strain tolerance", stats.StrainTolerance, derived.StrainTolerance, HudStyle.StrainFill), ("Armour", view.Armor, null, null),
                 })
        {
            long gain = formula is null ? 0 : Gain(formula);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new ColorRect { Color = pool ?? new Color(0, 0, 0, 0), CustomMinimumSize = new Vector2(4, 4), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            var stat = UiKit.Stat(label, gain > 0 ? $"{value + gain}" : $"{value}", gain > 0 ? HudStyle.Lichen : HudStyle.Bone);
            stat.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(stat);
            var more = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Lichen);
            more.Text = gain > 0 ? $"+{gain}" : string.Empty;
            more.CustomMinimumSize = new Vector2(40, 0);
            more.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(more);
            column.AddChild(row);
        }
        long carryGain = _weighing.GetValueOrDefault(CharacterAttribute.Might) * _session.Setup.Items.Inventory.CarryGramsPerMight;
        column.AddChild(UiKit.Stat("Carrying", $"{view.CarriedGrams / 1000.0:0.#} / {(view.CarryLimitGrams + carryGain) / 1000.0:0.#} kg",
            carryGain > 0 ? HudStyle.Lichen : HudStyle.Bone));
        var load = UiKit.Gauge(HudStyle.Ash, Column);
        load.Set(view.CarriedGrams, view.CarryLimitGrams + carryGain);
        column.AddChild(load);

        var on = UiKit.Section("On you");
        on.AddThemeConstantOverride("separation", 6);
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 4);
        foreach (var effect in simulation.Combat.Effects)
            chips.AddChild(UiKit.Chip($"{Names.Of(_session, effect.EffectId)}{(effect.Stacks > 1 ? $" ×{effect.Stacks}" : "")}", HudStyle.Iron, HudStyle.Bone));
        if (simulation.Combat.Strained)
            chips.AddChild(UiKit.Chip("Strained", HudStyle.Wound));
        if (chips.GetChildCount() > 0)
            on.AddChild(chips);
        else
            on.AddChild(UiKit.Quiet("Nothing: no wound, no draught, no working."));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        column.AddChild(on);
        return column;
    }

    /// <summary>What is worn (by slot, in words), the skills most practised, and the newest of what is known with where it was learned.</summary>
    private Control Carried(PlayerView view)
    {
        var session = _session!;
        var rules = session.Setup.Progression;
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(Column, 0) };
        column.AddThemeConstantOverride("separation", 16);

        var worn = UiKit.Section("Equipped");
        foreach (var (slot, item) in view.Equipment)
        {
            if (view.Inventory.FirstOrDefault(e => e.ItemId == item) is not { } entry)
                continue;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var label = UiKit.Caption(Names.Slot(slot));
            label.CustomMinimumSize = new Vector2(96, 0);
            row.AddChild(label);
            row.AddChild(UiKit.Words(Names.Item(session, entry.DefId, entry.Quality)));
            worn.AddChild(row);
        }
        if (view.Equipment.IsEmpty)
            worn.AddChild(UiKit.Quiet("Nothing - bare hands and what you stand in."));
        column.AddChild(worn);

        var practised = UiKit.Section("Practised", "skills grow by use");
        foreach (var (id, skill) in view.Progression.Skills.OrderByDescending(s => s.Value.Level).ThenBy(s => s.Key, StringComparer.Ordinal).Take(4))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(UiKit.Words(Names.Of(session, id), 18));
            var gauge = UiKit.Gauge(HudStyle.Brass, 72, 6);
            gauge.Set(skill.ProgressXp, rules.Skills.ToNext(skill.Level));
            row.AddChild(gauge);
            var level = HudStyle.Text(HudStyle.Strong, 20, HudStyle.Bone);
            level.Text = $"{skill.Level}";
            level.CustomMinimumSize = new Vector2(28, 0);
            level.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(level);
            practised.AddChild(row);
        }
        if (view.Progression.Skills.Count == 0)
            practised.AddChild(UiKit.Quiet("None yet - skills grow by use."));
        column.AddChild(practised);

        var known = UiKit.Section("Known");
        var newest = view.Progression.Known.OrderByDescending(k => k.Value.Tick).ToList();
        foreach (var (id, learned) in newest.Take(4))
        {
            known.AddChild(UiKit.Words(Names.Of(session, id), 18));
            string from = ProgressionKeys.Key(learned.Source).Replace('_', ' ') + (learned.SourceRef is { } source ? $" ({Names.Of(session, source)})" : "");
            known.AddChild(UiKit.Words(from, 16, HudStyle.Ash));
        }
        if (newest.Count > 4)
            known.AddChild(UiKit.Words($"and {newest.Count - 4} more", 16, HudStyle.Muted));
        if (newest.Count == 0)
            known.AddChild(UiKit.Quiet("Nothing yet - taught, read, found or worked out, never bought with points."));
        column.AddChild(known);
        return column;
    }

    /// <summary>Weigh a point onto an attribute, or take one back: nothing reaches the authority until Spend.</summary>
    private void Weigh(CharacterAttribute attribute, int by)
    {
        int now = _weighing.GetValueOrDefault(attribute) + by;
        if (now <= 0)
            _weighing.Remove(attribute);
        else
            _weighing[attribute] = now;
        // After the press has been handled: the redraw replaces the button that took it.
        Callable.From(Refresh).CallDeferred();
    }

    /// <summary>Spend what is weighed: the existing command, once a point, as the old +1 buttons sent it.</summary>
    private void Spend()
    {
        if (_session?.Simulation is not { } simulation)
            return;
        foreach (var (attribute, points) in _weighing)
        {
            for (int i = 0; i < points; i++)
                _session.Submit(new SpendAttributeCommand(simulation.PlayerId, attribute));
        }
        _weighing.Clear();
        Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
    }

    private static string Label(CharacterAttribute attribute)
    {
        string key = ProgressionKeys.Key(attribute);
        return char.ToUpperInvariant(key[0]) + key[1..];
    }

    /// <summary>What one more point in an attribute changes, read off the derived values' formulas and the combat rules.</summary>
    private static string Effects(GameSession session, CharacterAttribute attribute)
    {
        var derived = session.Setup.Progression.Derived;
        var parts = new List<string>();
        foreach (var (label, formula) in new[] { ("health", derived.HealthMax), ("stamina", derived.StaminaMax), ("focus", derived.FocusMax),
                     ("resonance", derived.Resonance), ("strain tolerance", derived.StrainTolerance) })
        {
            if (formula.PerPoint.TryGetValue(attribute, out long per) && per != 0)
                parts.Add($"{label} +{per}");
        }
        if (attribute == CharacterAttribute.Might)
        {
            parts.Add($"physical blows +{session.Setup.Combat.Constants.MightDamagePercentPerPoint:0.#}%");
            parts.Add($"carrying +{session.Setup.Items.Inventory.CarryGramsPerMight / 1000.0:0.#} kg");
        }
        return "a point: " + string.Join(", ", parts);
    }
}
