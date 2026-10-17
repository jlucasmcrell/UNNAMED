// UNNAMED Presentation - the hiring sheet at a hireable resident or adventurer: the terms stated before the character agrees, the risk
// accepted in so many words, and the trip they may be asking company for (WP12b, WP13, WP15a, WP16; DN9, DN11)
// Godot presentation only: Hire and Join submit commands; nothing here holds game state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Companions;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The hiring sheet, a STANDARD sheet opened at someone who hires on (or asks company for a trip): who they are; why they cannot be hired
/// now, if they cannot; the roles they hire on as, as segments, with what each does; the days, held to the contract's range; the terms -
/// the wage, the whole cost, the loot rule; the risk (DN9) with its own acceptance; then Hire, offered only once the risk is accepted and
/// nothing refuses it. A trip they ask company for sits below, with Join (no wage, a share) and Ask. It closes when the character walks
/// out of reach of them; once they are hired it says so and offers the Party screen.
/// </summary>
public partial class HirePanel : CanvasLayer
{
    private Sheet _sheet = null!;
    private GameSession? _session;
    private string? _key;
    private HirelingRole? _role;
    private int _days = HireModel.DefaultDays;
    private bool _risk;
    private string? _asked;
    private string _drawn = string.Empty;

    /// <summary>The Party screen, on someone: what the sheet offers once they are hired.</summary>
    public Action<string>? ShowParty { get; set; }

    /// <summary>Who the sheet is about, while it is open.</summary>
    public string? Key => Visible ? _key : null;

    public void Bind(GameSession session) => _session = session;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Hire", null);
        _sheet.OnShown(() =>
        {
            _drawn = string.Empty;
            Refresh();
            Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
        });
    }

    /// <summary>Open on someone: the first role they hire on as, a week, and the risk not yet accepted.</summary>
    public void Open(string key)
    {
        if (_key != key)
        {
            _role = null;
            _days = HireModel.DefaultDays;
            _asked = null;
        }
        _key = key;
        _risk = false;
        Visible = true;
    }

    public void Close() => Visible = false;

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        if (!Visible || _session?.Simulation is not { } simulation || _key is not { } key)
            return;
        var hire = simulation.Hiring.Hireability(key);
        var adventurer = simulation.Adventurers.FirstOrDefault(a => a.Key == key);
        var instance = simulation.Npcs.FirstOrDefault(n => n.Id == key)?.InstanceId ?? adventurer?.InstanceId;
        var offer = instance is null ? null : simulation.GroupOffers.FirstOrDefault(o => o.Npc == instance);
        bool hired = simulation.Companions.Any(c => c.NpcId == key);
        HireTerms? terms = _role is { } role && hire is not null ? simulation.HireTerms(key, role, _days) : null;
        string signature = $"{hire}|{hired}|{_role}|{_days}|{_risk}|{terms}|{offer}|{_asked}|{simulation.Player.Currency}|" +
                           $"{simulation.WorldTick / Math.Max(1, _session.Setup.Clock.TicksPerDay / 288)}";
        if (signature == _drawn)
            return;
        _drawn = signature;
        string? focused = UiKit.FocusedId(_sheet.Root);
        Draw(simulation, key, hire, adventurer, offer, hired);
        if (!UiKit.Refocus(_sheet.Root, focused))
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }

    private void Draw(Simulation simulation, string key, NpcHireView? hire, AdventurerView? adventurer, LfgOfferView? offer, bool hired)
    {
        UiKit.Clear(_sheet.Body);
        string name = Person(simulation, key);
        _sheet.Head(name, null);
        var rules = simulation.Party;
        var trip = offer is null ? null : ChatModel.Posts(new[] { offer }, simulation.WorldTick, simulation.Setup.Clock)[0];
        if (hired)
        {
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Lichen, $"{name} works for you now."));
            if (ShowParty is { } show)
                _sheet.Body.AddChild(UiKit.Action("See your party", () => show(key), UiKit.Kind.Primary, id: "hire/party"));
            return;
        }
        if (hire is null)
        {
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound, $"{name} is not here to ask."));
            return;
        }
        var sheet = HireModel.Sheet(name, hire, rules, adventurer, trip, id => Names.Of(_session!, id));
        _sheet.Body.AddChild(UiKit.Words(sheet.Who, 18, HudStyle.Ash));
        if (!sheet.Roles.IsEmpty)
            Terms(simulation, sheet);
        else if (sheet.Refusal is { } none)
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound, none));
        if (sheet.Trip is { } post && offer is not null)
            Trip(simulation, post, offer);
    }

    /// <summary>The roles, the days, the terms, the risk and Hire.</summary>
    private void Terms(Simulation simulation, HireSheet sheet)
    {
        var work = UiKit.Section("Work", "for a daily wage");
        if (sheet.Refusal is { } refused)
            work.AddChild(UiKit.Callout(HudStyle.Wound, refused));
        var roles = sheet.Roles.Select(r => r.Role).ToList();
        if (_role is not { } role || !roles.Contains(role))
            _role = role = roles[0];
        work.AddChild(UiKit.Segments("hire/role", sheet.Roles.Select(r => r.Word).ToList(), roles.IndexOf(role), i =>
        {
            _role = roles[i];
            _risk = false;
        }));
        work.AddChild(UiKit.Words(sheet.Roles.First(r => r.Role == role).Does, 17, HudStyle.Ash));

        _days = HireModel.Days(_days, sheet.MinDays, sheet.MaxDays);
        var days = new HBoxContainer();
        days.AddThemeConstantOverride("separation", 8);
        days.AddChild(UiKit.Caption("Days"));
        var fewer = UiKit.Action("−", () => _days = HireModel.Days(_days - 1, sheet.MinDays, sheet.MaxDays), compact: true, id: "hire/fewer");
        fewer.Disabled = _days <= sheet.MinDays;
        days.AddChild(fewer);
        var count = HudStyle.Text(HudStyle.Strong, 20, HudStyle.Bone);
        count.Text = $"{_days}";
        count.CustomMinimumSize = new Vector2(36, 0);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        count.VerticalAlignment = VerticalAlignment.Center;
        days.AddChild(count);
        var more = UiKit.Action("+", () => _days = HireModel.Days(_days + 1, sheet.MinDays, sheet.MaxDays), compact: true, id: "hire/more");
        more.Disabled = _days >= sheet.MaxDays;
        days.AddChild(more);
        foreach (int preset in new[] { 1, 7, 30 }.Where(d => d >= sheet.MinDays && d <= sheet.MaxDays))
            days.AddChild(UiKit.Action(preset == 1 ? "A day" : preset == 7 ? "A week" : $"{preset} days", () => _days = preset, compact: true, id: $"hire/days/{preset}"));
        work.AddChild(days);
        _sheet.Body.AddChild(work);

        var offer = HireModel.Offer(simulation.HireTerms(sheet.Key, role, _days));
        var terms = UiKit.Section("Terms");
        terms.AddChild(UiKit.Stat("Wage", offer.Wage, HudStyle.Brass, 18));
        terms.AddChild(UiKit.Stat("Contract", offer.Total, null, 18));
        terms.AddChild(UiKit.Stat("Coin carried", $"{simulation.Player.Currency}", null, 18));
        terms.AddChild(UiKit.Words(offer.Loot, 17, HudStyle.Ash));
        _sheet.Body.AddChild(terms);

        if (offer.Risk.Length > 0)
        {
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Ember, offer.Risk));
            _sheet.Body.AddChild(UiKit.Toggle("I accept that they can die in my service", _risk, () => _risk = !_risk, "hire/risk"));
        }
        var act = new HBoxContainer();
        act.AddThemeConstantOverride("separation", 12);
        var take = UiKit.Action($"Hire {sheet.Name}", () =>
        {
            if (offer.CanHire(_risk || offer.Risk.Length == 0))
                _session!.Submit(new HireCommand(simulation.PlayerId, sheet.Key, role, _days, AcceptsRisk: true));
        }, UiKit.Kind.Primary, id: "hire/take");
        take.Disabled = !offer.CanHire(_risk || offer.Risk.Length == 0);
        act.AddChild(take);
        string? why = offer.Refusal ?? (!_risk && offer.Risk.Length > 0 ? "Accept the risk first." : null);
        if (why is not null)
            act.AddChild(UiKit.Words(why, 16, offer.Refusal is null ? HudStyle.Muted : HudStyle.Wound));
        _sheet.Body.AddChild(act);
    }

    /// <summary>The trip they ask company for: what, where and when, Join (a share, no wage) and Ask.</summary>
    private void Trip(Simulation simulation, GroupPost post, LfgOfferView offer)
    {
        var trip = UiKit.Section("Setting out", "no wage: a share of what you find");
        trip.AddChild(UiKit.Words($"{post.What}, at {post.Where}. Meet at {post.Meet}; they leave at {post.Leaves}, {post.LeavesIn}.", 18));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        if (post.Joined)
        {
            row.AddChild(UiKit.Chip("You are going", HudStyle.Lichen));
        }
        else
        {
            var join = UiKit.Action("Join them", () => _session!.Submit(new JoinGroupCommand(simulation.PlayerId, offer.Npc)), UiKit.Kind.Primary, id: "hire/join");
            join.Disabled = !post.CanJoin;
            row.AddChild(join);
        }
        row.AddChild(UiKit.Action("Ask", () =>
        {
            var body = simulation.Player.Body;
            _asked = ChatModel.Ask(offer, simulation.WorldTick, simulation.Setup.Clock, body.XMm, body.ZMm);
        }, id: "hire/ask"));
        trip.AddChild(row);
        if (_asked is { } asked)
            trip.AddChild(UiKit.Callout(HudStyle.Iron, asked));
        _sheet.Body.AddChild(trip);
    }

    /// <summary>A person's name: theirs in the world, an adventurer's, or their key in words.</summary>
    private string Person(Simulation simulation, string key) =>
        simulation.Npcs.FirstOrDefault(n => n.Id == key)?.Name ?? simulation.Adventurers.FirstOrDefault(a => a.Key == key)?.Name ?? Names.Of(_session!, key);
}
