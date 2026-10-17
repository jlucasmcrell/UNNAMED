// UNNAMED Presentation - the Party screen: who travels with the character - companions, hirelings and allies - on what terms, under what
// orders, in what gear, and how they regard the character, in words (the Field Instrument UI, design record 06; WP15a, WP15b)
// Godot presentation only: its buttons go through the controller's and the session's commands; nothing here holds state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Party (O), a DENSE list + detail sheet. The list: each member with their order (or downed) tile, name, kind and what they are doing;
/// then who is recovering at home and the groups the character set out with. The detail, for the chosen member: their state and a live
/// health gauge (Help them up while downed, with the seconds they have); orders as segments - follow or wait, the stance, the three
/// tactics; a hireling's contract, wage and what is owed; how they regard the character and whether they go along with what they are
/// asked, as sentences - never an approval bar or a number; their level and points to spend; their gear, with what is loaned, Take back,
/// and what the character could hand them (Loan, Give); and Dismiss, asked twice. No coordinates: they are with you, or not.
/// </summary>
public partial class CompanionsPanel : CanvasLayer
{
    private const double ConfirmSeconds = 3;

    private readonly List<(string NpcId, HudView.Gauge Health, Label Words)> _live = new();
    private readonly Dictionary<string, Button> _rows = new(StringComparer.Ordinal);
    private Sheet _sheet = null!;
    private ListDetail _panes = null!;
    private GameSession _session = null!;
    private PlayerController _controller = null!;
    private HudIcons _icons = HudIcons.None;
    private string? _drawn;
    private string? _selected;
    private string? _confirm;
    private double _confirmLeft;

    /// <summary>The icon set: each member's order (or downed) tile, as the HUD's companion strip shows it, and the screen's own.</summary>
    public void UseIcons(HudIcons icons)
    {
        _icons = icons;
        _sheet.SetIcon(icons.Binds("panel.companions") ? icons.For("panel.companions") : null);
        _drawn = null;
    }

    public void Bind(GameSession session, PlayerController controller)
    {
        _session = session;
        _controller = controller;
    }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Party", "party", UiKit.Wide, scrolls: false);
        _sheet.Shape(UiKit.Wide, dense: true);
        _panes = UiKit.ListDetail();
        _sheet.Body.AddChild(_panes.Root);
        _sheet.OnShown(() =>
        {
            _drawn = null;
            _confirm = null;
            Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
        });
    }

    /// <summary>Open on a member (from a person in the world), or on whoever was chosen last.</summary>
    public void Open(string? npcId = null)
    {
        if (npcId is not null)
            _selected = npcId;
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        if (_confirm is not null && (_confirmLeft -= delta) <= 0)
        {
            _confirm = null;
            _drawn = null;
        }
        var companions = simulation.Companions;
        if (_selected is null || companions.All(c => c.NpcId != _selected))
            _selected = companions.FirstOrDefault()?.NpcId;
        var sheet = _selected is null ? null : simulation.PartySheet(_selected);
        string signature = string.Join("|", companions.Select(c => $"{c.NpcId}:{c.Order}:{c.Condition}:{c.Doing}:{c.Stance}:{c.Tactics}:{c.Contract}:{c.WeaponId}")) +
                           $"|{_selected}|{_confirm}|{sheet?.Level}:{sheet?.LevelProgressXp}:{sheet?.UnspentPoints}:{sheet?.Regard}:{sheet?.Disposition}:" +
                           string.Join(",", sheet?.Gear.Select(g => $"{g.Slot}{g.Item}{g.Loaned}") ?? Array.Empty<string>()) + "|" +
                           string.Join(",", simulation.Player.Inventory.Select(e => $"{e.ItemId}{e.Count}")) + "|" + string.Join(",", simulation.Player.Equipment.Values) + "|" +
                           string.Join(",", simulation.Recovering.Select(r => $"{r.NpcId}{(r.UntilTick - simulation.WorldTick) / Math.Max(1, _session.Setup.Clock.TicksPerDay)}")) + "|" +
                           string.Join(",", simulation.AllyGroups.Select(g => g.GroupKey));
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
        _rows.Clear();
        UiKit.Clear(_panes.List);
        UiKit.Clear(_panes.Detail);
        var rules = simulation.Party;
        _sheet.SetHint(PartyModel.Count(simulation.Companions.Length, rules));
        foreach (var member in simulation.Companions)
        {
            bool downed = member.Condition != CompanionCondition.Up;
            string tile = downed ? "downed" : member.Order == CompanionOrder.Follow ? "follow" : "wait";
            string id = member.NpcId;
            var row = UiKit.ListRow(id, member.Name, $"{PartyModel.Kind(member.Membership, member.Contract?.Role)} · {member.Doing}", height: 52,
                left: UiKit.Icon(_icons, tile, 32));
            if (downed)
                row.Right.AddChild(UiKit.Chip("Downed", HudStyle.Wound));
            else if (member.Contract is { UnpaidDays: > 0 })
                row.Right.AddChild(UiKit.Chip("Owed", HudStyle.Brass));
            row.Button.Pressed += () =>
            {
                _selected = id;
                _confirm = null;
                _drawn = null;
            };
            UiKit.Select(row.Button, id == _selected);
            _rows[id] = row.Button;
            _panes.List.AddChild(row.Button);
        }
        if (simulation.Companions.IsEmpty)
            _panes.List.AddChild(UiKit.Quiet("No one travels with you."));
        if (!simulation.Recovering.IsEmpty)
        {
            var away = UiKit.Section("Recovering");
            foreach (var recovery in simulation.Recovering)
                away.AddChild(UiKit.Words(PartyModel.Recovering(recovery, simulation.WorldTick, _session.Setup.Clock.TicksPerDay), 16, HudStyle.Ash));
            _panes.List.AddChild(away);
        }
        if (!simulation.AllyGroups.IsEmpty)
        {
            var groups = UiKit.Section("Set out together");
            foreach (var group in simulation.AllyGroups)
                groups.AddChild(UiKit.Words(PartyModel.Group(group, ChatPanel.Person(simulation, group.Leader) ?? "them"), 16, HudStyle.Ash));
            _panes.List.AddChild(groups);
        }

        if (simulation.Companions.FirstOrDefault(c => c.NpcId == _selected) is not { } chosen)
        {
            _panes.Detail.AddChild(UiKit.EmptyState("Travelling alone", "No one travels with you yet. Companions join through what you say to them; others hire on for a wage, or go along for a share when they ask for company.",
                "opens chat, where offers to set out together are posted.", "chat"));
            return;
        }
        Detail(simulation, chosen, PartyModel.Card(chosen, simulation.PartySheet(chosen.NpcId), rules, (def, q) => Names.Item(_session, def, q)));
    }

    private void Detail(Simulation simulation, CompanionView member, PartyCard card)
    {
        string id = card.NpcId;
        var head = UiKit.Row(null, card.Name, $"{card.Kind} · {card.Doing} · {card.Standing}");
        head.Root.AddChild(card.Downed ? UiKit.Chip("Downed", HudStyle.Wound)
            : UiKit.Chip(card.Order == CompanionOrder.Follow ? "Following" : "Waiting", HudStyle.Iron, HudStyle.Bone));
        _panes.Detail.AddChild(head.Root);

        var health = new HBoxContainer();
        health.AddThemeConstantOverride("separation", 12);
        health.AddChild(UiKit.Caption("Health"));
        var gauge = UiKit.Gauge(HudStyle.HealthFill, 360, 8);
        health.AddChild(gauge);
        var words = HudStyle.Text(HudStyle.Strong, 18, card.Downed ? HudStyle.Wound : HudStyle.Bone);
        words.VerticalAlignment = VerticalAlignment.Center;
        health.AddChild(words);
        _panes.Detail.AddChild(health);
        _live.Add((id, gauge, words));

        // Orders: follow or wait, the stance, the tactics - or, downed, only the hand up.
        var orders = UiKit.Section("Orders");
        if (card.Downed)
        {
            var up = new HBoxContainer();
            up.AddThemeConstantOverride("separation", 8);
            up.AddChild(UiKit.Action("Help them up", () => _controller.Revive(id), UiKit.Kind.Primary, id: $"{id}/revive"));
            up.AddChild(UiKit.Words("Reach them first: help is given hand to hand.", 16, HudStyle.Muted));
            orders.AddChild(up);
        }
        else
        {
            var given = new[] { CompanionOrder.Follow, CompanionOrder.Wait };
            orders.AddChild(UiKit.Segments($"{id}/order", new[] { "Follow me", "Wait here" }, Array.IndexOf(given, card.Order), i => _controller.Order(id, given[i])));
            var stances = Enum.GetValues<CompanionStance>();
            orders.AddChild(UiKit.Segments($"{id}/stance", stances.Select(PartyModel.StanceWord).ToList(), Array.IndexOf(stances, card.Stance),
                i => Submit(new SetStanceCommand(simulation.PlayerId, id, stances[i]))));
            var tactics = new HFlowContainer();
            tactics.AddThemeConstantOverride("h_separation", 8);
            tactics.AddThemeConstantOverride("v_separation", 6);
            foreach (var (key, said, on) in PartyModel.Tactics(card.Tactics))
                tactics.AddChild(UiKit.Toggle(said, on, () => Submit(new SetTacticsCommand(simulation.PlayerId, id, PartyModel.Toggled(card.Tactics, key))), $"{id}/tactic/{key}"));
            orders.AddChild(tactics);
            if (UiKit.KeyCap("companion_order") is { } cap)
            {
                var hint = new HBoxContainer();
                hint.AddThemeConstantOverride("separation", 8);
                hint.AddChild(cap);
                hint.AddChild(UiKit.Words("switches every companion between follow and wait in play", 16, HudStyle.Muted));
                orders.AddChild(hint);
            }
        }
        _panes.Detail.AddChild(orders);

        // A hireling's terms: the contract, the wage, and what is owed.
        if (card.Hired)
        {
            var contract = UiKit.Section("Contract", "wage only: what the party finds is yours");
            contract.AddChild(UiKit.Stat("Terms", card.Contract ?? "", null, 18));
            contract.AddChild(UiKit.Stat("Wage", card.Wage ?? "", HudStyle.Brass, 18));
            if (card.Owed is { } owed)
                contract.AddChild(UiKit.Callout(HudStyle.Ember, owed));
            _panes.Detail.AddChild(contract);
        }

        // How they regard the character, and whether they go along: sentences, never a bar or a number (§2.15).
        var regard = UiKit.Section("Regard");
        regard.AddChild(UiKit.Words(card.Regard, 18));
        regard.AddChild(UiKit.Words(card.Goes, 17, UiKit.Tone(card.GoesTone)));
        _panes.Detail.AddChild(regard);

        if (card.Level is { } level)
        {
            var growth = UiKit.Section("Level", "they grow by the fights they share");
            growth.AddChild(UiKit.Words(level, 18));
            if (card.UnspentPoints > 0 && !card.Downed)
            {
                var points = new HFlowContainer();
                points.AddThemeConstantOverride("h_separation", 8);
                points.AddThemeConstantOverride("v_separation", 6);
                foreach (var attribute in ProgressionKeys.Attributes)
                {
                    string word = ProgressionKeys.Key(attribute);
                    points.AddChild(UiKit.Action($"+1 {char.ToUpperInvariant(word[0])}{word[1..]}",
                        () => Submit(new SpendPartyAttributeCommand(simulation.PlayerId, id, attribute)), compact: true, id: $"{id}/spend/{word}"));
                }
                growth.AddChild(points);
            }
            _panes.Detail.AddChild(growth);
        }

        Gear(simulation, card);

        // Dismiss: asked twice, the second within a few seconds; a hireling's contract ends with it.
        var leave = new HBoxContainer();
        leave.AddThemeConstantOverride("separation", 12);
        bool asked = _confirm == id;
        var dismiss = UiKit.Action(asked ? "Press again to dismiss" : card.Hired ? "Dismiss: end the contract" : "Dismiss", () =>
        {
            if (_confirm == id)
            {
                _confirm = null;
                Submit(new DismissCommand(simulation.PlayerId, id));
            }
            else
            {
                _confirm = id;
                _confirmLeft = ConfirmSeconds;
            }
            _drawn = null;
        }, UiKit.Kind.Destructive, id: $"{id}/dismiss");
        dismiss.Disabled = !card.CanDismiss;
        leave.AddChild(dismiss);
        leave.AddChild(UiKit.Words(card.Dismiss ?? "They go home to their own life, and keep what is theirs; what you loaned them comes back.", 16, HudStyle.Muted));
        _panes.Detail.AddChild(leave);
    }

    /// <summary>What they wear, Take back on each piece; then what in the pack they could wear, to Loan or Give.</summary>
    private void Gear(Simulation simulation, PartyCard card)
    {
        string id = card.NpcId;
        var gear = UiKit.Section("Gear", "loaned comes back when they leave");
        foreach (var piece in card.Gear)
        {
            var row = UiKit.Row(UiKit.Chip(piece.SlotWord, HudStyle.Iron, HudStyle.Bone), piece.Item, piece.Whose);
            if (piece.Loaned && !card.Downed)
            {
                var slot = piece.Slot;
                row.Root.AddChild(UiKit.Action("Take back", () => Submit(new TakeBackGearCommand(simulation.PlayerId, id, slot)), compact: true, id: $"{id}/back/{slot}"));
            }
            gear.AddChild(row.Root);
        }
        if (card.Gear.IsEmpty)
            gear.AddChild(UiKit.Quiet("Only their own: nothing handed to them."));
        var player = simulation.Player;
        var catalog = simulation.Setup.Items.Catalog;
        var spare = player.Inventory.Where(e => !player.Equipment.ContainsValue(e.ItemId) && catalog.Find(e.DefId)?.Slot is not null).ToList();
        if (spare.Count > 0 && !card.Downed)
        {
            gear.AddChild(UiKit.Caption("Hand them from your pack"));
            foreach (var entry in spare)
            {
                var item = entry.ItemId;
                var definition = catalog.Find(entry.DefId)!;
                var row = UiKit.Row(UiKit.Chip(PartyModel.SlotWord(definition.Slot!.Value), HudStyle.Iron, HudStyle.Ash), Names.Item(_session, entry.DefId, entry.Quality),
                    WearWords.Line(entry.Wear, definition.DurabilityMax) ?? "");
                if (WearWords.Chip(entry.Wear, definition.DurabilityMax) is { } worn)
                    row.Root.AddChild(UiKit.Chip(worn));
                row.Root.AddChild(UiKit.Action("Loan", () => Submit(new GiveGearCommand(simulation.PlayerId, id, item, Loan: true)), compact: true, id: $"{id}/loan/{item}"));
                row.Root.AddChild(UiKit.Action("Give", () => Submit(new GiveGearCommand(simulation.PlayerId, id, item, Loan: false)), compact: true, id: $"{id}/give/{item}"));
                gear.AddChild(row.Root);
            }
        }
        _panes.Detail.AddChild(gear);
    }

    private void Submit(GameCommand command) => _session.Submit(command);
}
