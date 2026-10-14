// UNNAMED Presentation - who travels with the character, and the order each is under (the Field Instrument UI, design record 06)
// Godot presentation only: its buttons go through the controller's commands; nothing here holds state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Companions;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Companions, from the System Menu: a STANDARD sheet with a card a companion - their state, name, what they are doing and how they
/// are, a health gauge, and the order as two segments (Follow me | Wait here) with the one given shown selected. Downed, a wound
/// callout counts down the time they have, live, and Help them up replaces the order. No coordinates: they are with you, or not.
/// </summary>
public partial class CompanionsPanel : CanvasLayer
{
    private readonly List<(string NpcId, HudView.Gauge Health, Label Words)> _live = new();
    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private PlayerController _controller = null!;
    private string? _drawn;

    public void Bind(GameSession session, PlayerController controller)
    {
        _session = session;
        _controller = controller;
    }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Companions", null);
        _sheet.OnShown(() =>
        {
            _drawn = null;
            Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
        });
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        var companions = simulation.Companions;
        string signature = string.Join("|", companions.Select(c => $"{c.NpcId}:{c.Order}:{c.Condition}:{c.Doing}"));
        if (signature != _drawn)
        {
            _drawn = signature;
            string? focused = UiKit.FocusedId(_sheet.Root);
            Build(simulation);
            if (!UiKit.Refocus(_sheet.Root, focused))
                UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
        }
        foreach (var (id, health, words) in _live)
        {
            if (companions.FirstOrDefault(c => c.NpcId == id) is not { } companion)
                continue;
            health.Set(companion.Health, companion.MaxHealth);
            words.Text = companion.FallsAtTick is { } falls
                ? $"Down - {Math.Max(0, falls - simulation.WorldTick) * _session.TickSeconds:0} s unless helped up"
                : $"{companion.Health} of {companion.MaxHealth}";
        }
    }

    private void Build(Simulation simulation)
    {
        _live.Clear();
        UiKit.Clear(_sheet.Body);
        if (simulation.Companions.IsEmpty)
        {
            _sheet.Body.AddChild(UiKit.EmptyState("Travelling alone", "No one travels with you yet.",
                "gives orders to a companion who is with you.", "companion_order"));
            return;
        }
        foreach (var companion in simulation.Companions)
        {
            bool downed = companion.Condition != CompanionCondition.Up;
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 8);
            var state = downed ? UiKit.Chip("Downed", HudStyle.Wound)
                : UiKit.Chip(companion.Order == CompanionOrder.Follow ? "Following" : "Waiting", HudStyle.Iron, HudStyle.Bone);
            state.CustomMinimumSize = new Vector2(96, 0);
            var head = UiKit.Row(state, companion.Name, $"{companion.Doing} · {companion.Standing}");
            card.AddChild(head.Root);

            var health = new HBoxContainer();
            health.AddThemeConstantOverride("separation", 12);
            health.AddChild(UiKit.Caption("Health"));
            var gauge = UiKit.Gauge(HudStyle.HealthFill, 360, 8);
            health.AddChild(gauge);
            var words = HudStyle.Text(HudStyle.Strong, 18, downed ? HudStyle.Wound : HudStyle.Bone);
            words.VerticalAlignment = VerticalAlignment.Center;
            health.AddChild(words);
            card.AddChild(health);
            _live.Add((companion.NpcId, gauge, words));

            var orders = new HBoxContainer();
            orders.AddThemeConstantOverride("separation", 8);
            string id = companion.NpcId;
            if (downed)
            {
                orders.AddChild(UiKit.Action("Help them up", () => _controller.Revive(id), UiKit.Kind.Primary, id: $"{id}/revive"));
                orders.AddChild(UiKit.Words("Reach them first: help is given hand to hand.", 16, HudStyle.Muted));
            }
            else
            {
                foreach (var (order, label) in new[] { (CompanionOrder.Follow, "Follow me"), (CompanionOrder.Wait, "Wait here") })
                {
                    bool given = companion.Order == order;
                    var button = UiKit.Action(label, () =>
                    {
                        if (!given)
                            _controller.Order(id, order);
                    }, id: $"{id}/{order}");
                    UiKit.Select(button, given);
                    orders.AddChild(button);
                }
                if (UiKit.KeyCap("companion_order") is { } cap)
                {
                    orders.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
                    orders.AddChild(cap);
                    orders.AddChild(UiKit.Words("switches every companion's order in play", 16, HudStyle.Muted));
                }
            }
            card.AddChild(orders);
            card.AddChild(HudStyle.Rule(8));
            _sheet.Body.AddChild(card);
        }
    }
}
