// UNNAMED Presentation - the chat window: the channels on tabs, the owner's chatter setting, offers to set out together with Join and Ask,
// and the disclosure of AI-written lines (WP16)
// Godot presentation only: Join submits the command; Ask and the setting change nothing in the world (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain;
using UNNAMED.Presentation.Settings;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Chat (T), a STANDARD sheet: tabs for every channel and the group board, the chatter setting as three segments (All, Play-relevant
/// only, Off - the same setting as Settings, Interface), then the rows newest last: the time, the channel, who said it and the words,
/// an AI chip on a line written with AI. An offer to set out together carries Join and Ask on its row and on the board; Ask shows the
/// offer's facts to the character alone. The disclosure stays under the title. The world runs on under it, as under every screen.
/// </summary>
public partial class ChatPanel : CanvasLayer
{
    private const int Most = 120;

    private Sheet _sheet = null!;
    private GameSession? _session;
    private ChatTab _tab = ChatTab.All;
    private string _drawn = string.Empty;
    /// <summary>What Ask showed, by who asked about: kept while the screen lives, never saved and never said into the world.</summary>
    private readonly Dictionary<EntityId, string> _asked = new();

    public void Bind(GameSession session) => _session = session;

    public void UseIcons(HudIcons icons) => _drawn = string.Empty;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Chat", "chat");
        _sheet.OnShown(() =>
        {
            _drawn = string.Empty;
            Refresh();
            Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
        });
    }

    /// <summary>Open on a tab (the group board from an offer's notice), or where it was left.</summary>
    public void Open(ChatTab? tab = null)
    {
        if (tab is { } chosen)
            _tab = chosen;
        Visible = true;
    }

    public override void _Process(double delta) => Refresh();

    /// <summary>Redraw when what is shown changed: a message heard, an offer opened or answered, the tab or the setting.</summary>
    public void Refresh()
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        var setting = SettingsStore.Current.Profile.Interface.Chat;
        var heard = simulation.Chat;
        var offers = simulation.GroupOffers;
        string signature = $"{_tab}|{setting}|{heard.Length}|{(heard.IsEmpty ? 0 : heard[^1].Tick)}|" +
                           string.Join(",", offers.Select(o => $"{o.Npc}:{o.Joined}:{o.DepartTick > simulation.WorldTick}")) + "|" + string.Join(",", _asked.Keys) +
                           $"|{simulation.WorldTick / (_session.Setup.Clock.TicksPerDay / 288)}";   // the board's "leaves in" moves every five game minutes
        if (signature == _drawn)
            return;
        _drawn = signature;
        string? focused = UiKit.FocusedId(_sheet.Root);
        Draw(simulation, setting);
        if (!UiKit.Refocus(_sheet.Root, focused))
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }

    private void Draw(Simulation simulation, ChatSetting setting)
    {
        UiKit.Clear(_sheet.Body);
        var tabs = Enum.GetValues<ChatTab>();
        _sheet.Body.AddChild(UiKit.Segments("chat/tab", tabs.Select(ChatModel.TabWord).ToList(), Array.IndexOf(tabs, _tab), i =>
        {
            _tab = tabs[i];
            _drawn = string.Empty;
        }, compact: true));
        var settings = Enum.GetValues<ChatSetting>();
        var mode = new HBoxContainer();
        mode.AddThemeConstantOverride("separation", 12);
        mode.AddChild(UiKit.Caption("Show"));
        mode.AddChild(UiKit.Segments("chat/mode", settings.Select(ChatModel.ModeWords).ToList(), Array.IndexOf(settings, setting), i =>
        {
            var prefs = SettingsStore.Current;
            SettingsStore.Set(prefs with { Profile = prefs.Profile with { Interface = prefs.Profile.Interface with { Chat = settings[i] } } });
            _drawn = string.Empty;
        }, compact: true));
        _sheet.Body.AddChild(mode);
        _sheet.Body.AddChild(HudStyle.Rule(4));
        if (_tab == ChatTab.Groups)
            DrawBoard(simulation);
        else
            DrawRows(simulation, setting);
    }

    private void DrawRows(Simulation simulation, ChatSetting setting)
    {
        if (setting == ChatSetting.Off)
        {
            _sheet.Body.AddChild(UiKit.EmptyState("Chat is off", "Nothing said around you is shown. Choose All or Play-relevant only above, or in Settings, Interface.",
                "The Groups tab still lists who is asking for company."));
            return;
        }
        var offers = simulation.GroupOffers;
        var rows = ChatModel.Rows(simulation.Chat, _tab, setting, simulation.ChatText, key => simulation.Setup.Chat.Line(key), id => Person(simulation, id),
            simulation.Setup.Clock, Most);
        if (rows.IsEmpty)
        {
            _sheet.Body.AddChild(UiKit.EmptyState("Nothing heard yet", _tab == ChatTab.All
                ? "Talk carries a short way, news across the region, and offers to set out together to everyone near a town."
                : $"Nothing said on {ChatModel.TabWord(_tab)} has reached you yet."));
            return;
        }
        foreach (var row in rows)
            _sheet.Body.AddChild(Line(simulation, row, ChatModel.Offer(row, offers)));
        Callable.From(() => { if (_sheet.Scroll is { } scroll) scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue; }).CallDeferred();
    }

    /// <summary>One message: time and channel at the left, who and what in the middle, the AI chip, and an open offer's Join and Ask.</summary>
    private Control Line(Simulation simulation, ChatRow row, LfgOfferView? offer)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 10);
        var time = UiKit.Caption(row.Time);
        time.CustomMinimumSize = new Vector2(52, 0);
        time.VerticalAlignment = VerticalAlignment.Top;
        line.AddChild(time);
        var channel = UiKit.Chip(row.ChannelWord, row.Channel == ChatChannel.System ? HudStyle.Brass : HudStyle.Iron, HudStyle.Ash);
        channel.CustomMinimumSize = new Vector2(76, 0);
        channel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        line.AddChild(channel);
        var words = UiKit.Words(row.Speaker is null ? row.Text : $"{row.Speaker}: {row.Text}", 17, row.Channel == ChatChannel.System ? HudStyle.Ash : HudStyle.Bone);
        line.AddChild(words);
        column.AddChild(line);
        if (offer is not null)
            column.AddChild(Actions(simulation, offer, indent: 148));
        return column;
    }

    /// <summary>The group board: every open offer, soonest first, with Join and Ask; what Ask showed under it.</summary>
    private void DrawBoard(Simulation simulation)
    {
        var offers = simulation.GroupOffers;
        var posts = ChatModel.Posts(offers, simulation.WorldTick, simulation.Setup.Clock);
        if (posts.IsEmpty)
        {
            _sheet.Body.AddChild(UiKit.EmptyState("No one is asking for company", "When someone sets out on a hunt, a bounty or an errand and would take company, their offer shows here and on World."));
            return;
        }
        foreach (var post in posts)
        {
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 6);
            var head = UiKit.Row(null, post.Who, $"{post.What}, at {post.Where} · meet at {post.Meet} · leaves {post.Leaves}, {post.LeavesIn}");
            if (post.Joined)
                head.Root.AddChild(UiKit.Chip("You are going", HudStyle.Lichen));
            card.AddChild(head.Root);
            if (offers.FirstOrDefault(o => o.Npc == post.Npc) is { } offer)
                card.AddChild(Actions(simulation, offer, indent: 0));
            card.AddChild(HudStyle.Rule(6));
            _sheet.Body.AddChild(card);
        }
    }

    /// <summary>Join (the command: the character will meet them) and Ask (the offer's facts, shown here alone); then what Ask showed.</summary>
    private Control Actions(Simulation simulation, LfgOfferView offer, float indent)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(indent, 0) });
        var post = ChatModel.Posts(new[] { offer }, simulation.WorldTick, simulation.Setup.Clock)[0];
        string id = offer.Npc.ToString();
        if (post.Joined)
        {
            row.AddChild(UiKit.Words($"You said you would go: be at {post.Meet} by {post.Leaves}.", 16, HudStyle.Lichen));
        }
        else
        {
            var join = UiKit.Action("Join", () => _session!.Submit(new JoinGroupCommand(simulation.PlayerId, offer.Npc)), UiKit.Kind.Primary, compact: true, id: $"{id}/join");
            join.Disabled = !post.CanJoin;
            row.AddChild(join);
        }
        row.AddChild(UiKit.Action("Ask", () =>
        {
            var body = simulation.Player.Body;
            _asked[offer.Npc] = ChatModel.Ask(offer, simulation.WorldTick, simulation.Setup.Clock, body.XMm, body.ZMm);
            _drawn = string.Empty;
        }, compact: true, id: $"{id}/ask"));
        if (post.Refusal is { } refused)
            row.AddChild(UiKit.Words(char.ToUpperInvariant(refused[0]) + refused[1..] + ".", 16, HudStyle.Muted));
        column.AddChild(row);
        if (_asked.TryGetValue(offer.Npc, out var asked))
        {
            var answer = new MarginContainer();
            answer.AddThemeConstantOverride("margin_left", (int)indent);
            answer.AddChild(UiKit.Callout(HudStyle.Iron, asked));
            column.AddChild(answer);
        }
        return column;
    }

    /// <summary>Who an instance is now: a person in the world, an adventurer, or no one known (the line's own name stands then).</summary>
    public static string? Person(Simulation simulation, EntityId id) =>
        simulation.Npcs.FirstOrDefault(n => n.InstanceId == id)?.Name ?? simulation.Adventurers.FirstOrDefault(a => a.InstanceId == id)?.Name;
}
