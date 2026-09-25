// UNNAMED Presentation - the character sheet (PROTOTYPE.md §4.1 UI panels; the owner's M6 playtest)
// Godot presentation only: it reads views and submits one command; nothing here holds state (D-11)

using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Progression;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The character (K): who they are, their level and XP, the attributes Phase 1 puts in play and what a point in each changes, the values
/// derived from them, their skills, and what they know and where they learned it - only what the game has now (no archetype choice,
/// no soul, faction or future progression). A level-up's unspent point is spent here, on an attribute some derived value reads.
/// </summary>
public partial class CharacterPanel : CanvasLayer
{
    private readonly Label _text = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(760, 0) };
    private readonly HBoxContainer _spend = new();
    private GameSession? _session;
    private string _drawn = string.Empty;

    public void Bind(GameSession session) => _session = session;

    public override void _Ready()
    {
        Visible = false;
        var panel = new PanelContainer { Position = new Vector2(60, 140), CustomMinimumSize = new Vector2(800, 0) };
        var column = new VBoxContainer();
        _text.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(_text);
        _spend.AddThemeConstantOverride("separation", 12);
        column.AddChild(_spend);
        panel.AddChild(column);
        AddChild(panel);
    }

    public void Refresh()
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        var view = simulation.Player;
        string text = Render(_session, view);
        if (text == _drawn)
            return;
        _drawn = text;
        _text.Text = text;
        foreach (var child in _spend.GetChildren())
            child.QueueFree();
        if (view.Progression.UnspentAttributePoints <= 0)
            return;
        foreach (var attribute in _session.Setup.Progression.Derived.Live)
        {
            var button = new Button { Text = $"+1 {Label(attribute)}" };
            button.Pressed += () => _session.Submit(new SpendAttributeCommand(simulation.PlayerId, attribute));
            _spend.AddChild(button);
        }
    }

    /// <summary>The sheet as text: what the panel shows, and what a test can read.</summary>
    public static string Render(GameSession session, PlayerView view)
    {
        var rules = session.Setup.Progression;
        var progression = view.Progression;
        var stats = view.Stats;
        var text = new StringBuilder($"CHARACTER   [{HelpPanel.Key("character")}] close\n\n");
        text.Append(view.Name).Append(" - no archetype in Phase 1: every character starts from the same package\n");
        text.Append(progression.Level >= rules.LevelCap
            ? $"Level {progression.Level}, the highest there is"
            : $"Level {progression.Level}   XP {progression.LevelProgressXp} of {rules.Curve.ToReach(progression.Level + 1)} to level {progression.Level + 1}");
        if (progression.XpDebt > 0)
            text.Append($"   XP debt {progression.XpDebt} (repaid from the next XP earned)");
        text.Append("\n\nATTRIBUTES");
        if (progression.UnspentAttributePoints > 0)
            text.Append($"   {progression.UnspentAttributePoints} point{(progression.UnspentAttributePoints == 1 ? "" : "s")} to spend");
        text.Append('\n');
        var live = rules.Derived.Live;
        foreach (var attribute in live)
        {
            text.Append($"  {Label(attribute),-10} {ProgressionEngine.AttributeValue(progression, attribute, rules),3}   a point: {Effects(session, attribute)}\n");
        }
        var idle = ProgressionKeys.Attributes.Where(a => !live.Contains(a)).Select(Label).ToList();
        if (idle.Count > 0)
            text.Append("  ").Append(string.Join(", ", idle)).Append(" are not in play in Phase 1\n");
        text.Append("\nDERIVED\n");
        text.Append($"  Health {stats.HealthMax}   Stamina {stats.StaminaMax}   Focus {stats.FocusMax}   Resonance {stats.Resonance}   Strain tolerance {stats.StrainTolerance}\n");
        text.Append($"  Armor {view.Armor}   Carrying {view.CarriedGrams / 1000.0:0.#} of {view.CarryLimitGrams / 1000.0:0.#} kg\n");
        text.Append("\nSKILLS\n");
        if (progression.Skills.Count == 0)
            text.Append("  none yet - skills grow by use\n");
        foreach (var (id, skill) in progression.Skills)
            text.Append($"  {session.DisplayName(id),-18} {skill.Level,3}   {skill.ProgressXp} of {rules.Skills.ToNext(skill.Level)} XP to {skill.Level + 1}\n");
        text.Append("\nKNOWN\n");
        if (progression.Known.Count == 0)
            text.Append("  nothing yet - taught, read, found or worked out, never bought with points\n");
        foreach (var (id, known) in progression.Known.OrderBy(k => k.Value.Tick))
        {
            text.Append($"  {session.DisplayName(id)} - {ProgressionKeys.Key(known.Source).Replace('_', ' ')}");
            if (known.SourceRef is { } source)
                text.Append($" ({session.DisplayName(source)})");
            text.Append('\n');
        }
        return text.ToString();
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
        return string.Join(", ", parts);
    }
}
