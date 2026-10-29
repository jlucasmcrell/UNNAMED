// UNNAMED Presentation - Main's player-facing screens for the party, hiring, teaching and chat; the context line under the prompt; the
// chat feed; and the outlines on what the character can work with (WP9, WP12b, WP13, WP15a/b, WP16; W12)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using UNNAMED.Domain.Progression;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>How near a node with something to take is outlined, and how many at most (the nearest).</summary>
    private const long GatherableOutlineMm = 12_000;
    private const int GatherableOutlines = 8;

    /// <summary>How long a line of chat stays in the HUD's feed, in game seconds of world ticks.</summary>
    private const double ChatFeedSeconds = 30;

    private ChatPanel _chat = null!;
    private HirePanel _hire = null!;
    private LearnSheet _learn = null!;
    private readonly Outlines _outlines = new();

    /// <summary>The chat window, the hiring sheet and the conversation's Learn column, built with the other panels.</summary>
    private void BuildPlayerScreens()
    {
        _chat = new ChatPanel { Name = "Chat" };
        _chat.Bind(_session);
        _chat.AvailableGestures = () => _avatar.PerformanceClips.Where(c => c.StartsWith("emote_", StringComparison.Ordinal)).ToArray();
        _chat.Gesture = clip => _avatar.Perform(clip);
        AddChild(_chat);
        _hire = new HirePanel { Name = "Hire", ShowParty = ShowParty };
        _hire.Bind(_session);
        AddChild(_hire);
        _learn = new LearnSheet { Name = "Learn", AskAboutWork = AskAboutWork, Conversation = () => _dialogue.PanelRect };
        _learn.Bind(_session);
        AddChild(_learn);
    }

    /// <summary>The Party screen in place of whatever was open, on someone or on whoever was chosen last.</summary>
    private void ShowParty(string? npcId)
    {
        CloseScreens();
        _companionsPanel.Open(npcId);
    }

    /// <summary>The hiring sheet on someone in reach, in place of whatever was open; offering it names the hiring hint's moment.</summary>
    private void ShowHire(string key)
    {
        CloseScreens();
        _hire.Open(key);
        Hint(Domain.Guidance.HintTriggers.HireOffered);
    }

    /// <summary>From a conversation's context sheet: the conversation is left, and the hiring sheet opens on the same person.</summary>
    private void AskAboutWork(string key)
    {
        _dialogue.Leave();
        ShowHire(key);
    }

    /// <summary>The hiring sheet stays open only while its person is within talking reach of the character, as a trader's does.</summary>
    private void KeepHireInReach()
    {
        if (_hire.Key is not { } key || _session.Simulation is not { } simulation)
            return;
        if (simulation.Companions.Any(c => c.NpcId == key))
            return;   // hired: the sheet says so wherever they stand
        var npc = simulation.Npcs.FirstOrDefault(n => n.Id == key);
        // The simulation's talking reach, and a body's breadth of slack as the trader's panel has, so a step back does not close it.
        long reach = _session.Setup.Items.Inventory.ReachMm + 2 * _session.Setup.Movement.BodyRadiusMm;
        if (npc is null || Math.Sqrt(Math.Pow(npc.Body.XMm - _controller.Authoritative.XMm, 2) + Math.Pow(npc.Body.ZMm - _controller.Authoritative.ZMm, 2)) > reach)
            _hire.Close();
    }

    /// <summary>A person's name now: theirs in the world, an adventurer's, or their key read as words.</summary>
    private string Person(string key) =>
        _session.Simulation?.Npcs.FirstOrDefault(n => n.Id == key)?.Name ?? _session.Simulation?.Adventurers.FirstOrDefault(a => a.Key == key)?.Name ?? Names.Of(_session, key);

    /// <summary>What the character knows of a person in front of them: a conversation, the party, hiring, teaching, a trip.</summary>
    private NpcFacts Facts(Simulation simulation, string key)
    {
        bool talks = _session.Setup.Social.Npcs.TryGetValue(key, out var definition) && definition.DialogueId is not null;
        var hire = simulation.Hiring.Hireability(key);
        var instance = simulation.Npcs.FirstOrDefault(n => n.Id == key)?.InstanceId;
        var offer = instance is null ? null : simulation.GroupOffers.FirstOrDefault(o => o.Npc == instance);
        var trip = offer is null ? null : ChatModel.Posts(new[] { offer }, simulation.WorldTick, simulation.Setup.Clock)[0];
        return new NpcFacts(Person(key), talks, DownedCompanion(key) is not null, simulation.Companions.Any(c => c.NpcId == key), hire,
            simulation.Teaching(key) is { Lessons.IsEmpty: false }, trip);
    }

    /// <summary>Interacting with a person: help them up, talk, see them in the party, or ask about work or their trip.</summary>
    private void Approach(string key)
    {
        var simulation = _session.Simulation!;
        switch (ContextModel.Approach(Facts(simulation, key)))
        {
            case NpcApproachKind.HelpUp:
                _controller.Revive(key);
                break;
            case NpcApproachKind.Party:
                ShowParty(key);
                break;
            case NpcApproachKind.Hire:
                ShowHire(key);
                break;
            default:
                _controller.Talk(key);
                break;
        }
    }

    /// <summary>The line of context under the prompt for what it is about, or null.</summary>
    private string? Context(Simulation simulation, Focus focus)
    {
        var player = simulation.Player;
        switch (focus.Kind)
        {
            case FocusKind.Npc:
                return ContextModel.Npc(Facts(simulation, focus.Key));
            case FocusKind.Node when _session.Setup.Crafting.Nodes.TryGetValue(focus.DefId, out var node):
                bool ready = simulation.Nodes.FirstOrDefault(n => n.Key == focus.Key) is { Ready: true };
                return ContextModel.Node(node, ready, ProgressionEngine.SkillLevel(player.Progression, node.SkillId), id => Names.Of(_session, id));
            case FocusKind.Container when simulation.Containers.FirstOrDefault(c => c.Site.Key == focus.Key) is { } container:
                return ContextModel.Container(container.Items.Length);
            case FocusKind.Item when _session.Setup.Items.Catalog.Find(focus.DefId) is { } item:
                long count = simulation.WorldItems.FirstOrDefault(i => i.Id.Value == focus.Key)?.Count ?? 1;
                return ContextModel.Item(item.WeightGrams * count, player.CarriedGrams, player.CarryLimitGrams, _session.Setup.Items.Inventory.Laden);
            case FocusKind.Station when focus.DefId == NoticeBoardPanel.Kind:
                return NoticeBoardModel.Hint(
                    NoticeBoardModel.Contracts(simulation.Bounties, simulation.PlayerId,
                        id => _session.Setup.Quests.Quests.GetValueOrDefault(id), id => Names.Of(_session, id)),
                    NoticeBoardModel.Away(simulation.Adventurers, id => Names.Of(_session, id)));
            case FocusKind.Station:
                var made = _session.Setup.Crafting.Recipes.Values.Where(r => r.StationKind == focus.DefId).ToList();
                return ContextModel.Station(made.Count(r => player.Progression.Known.ContainsKey(r.Id)), made.Count);
            default:
                return null;
        }
    }

    /// <summary>
    /// The outlines this frame: the thing the prompt is about (not a node with nothing to take), and the nodes near the character with
    /// something to gather. None while a screen or a conversation is up.
    /// </summary>
    private void DrawOutlines(Simulation simulation, Focus? focus)
    {
        var wanted = new List<(Godot.Node3D?, OutlineTone)>();
        if (!Modal)
        {
            string? focusNode = null;
            if (focus is not null)
            {
                bool isNode = focus.Kind == FocusKind.Node;
                var nodeView = isNode ? simulation.Nodes.FirstOrDefault(n => n.Key == focus.Key) : null;
                focusNode = nodeView?.Key;
                var root = focus.Kind switch
                {
                    FocusKind.Node => nodeView is null ? null : _crafting.Node(nodeView.Name),
                    FocusKind.Station => _crafting.Station(focus.Key),
                    FocusKind.Container or FocusKind.Item => _items.Shown(focus.Key),
                    FocusKind.Npc => _npcs.FigureOf(focus.Key),
                    _ => null,
                };
                wanted.Add((root, ContextModel.Focus(isNode, nodeView?.Ready ?? false)));
            }
            var body = _controller.Authoritative;
            var nodes = simulation.Nodes;
            foreach (string key in ContextModel.Gatherable(nodes, body.XMm, body.ZMm, GatherableOutlineMm, focusNode, GatherableOutlines))
                wanted.Add((_crafting.Node(nodes.First(n => n.Key == key).Name), OutlineTone.Gatherable));
        }
        _outlines.Show(wanted, SettingsStore.Current.Profile.Interface.HighContrast);
    }

    /// <summary>
    /// The HUD's chat feed: the last lines heard, as the chat setting lets them show, each for half a minute of world time; nothing while
    /// a screen is up (the Chat screen holds every line).
    /// </summary>
    private void DrawChatFeed(Simulation simulation)
    {
        var setting = SettingsStore.Current.Profile.Interface.Chat;
        if (Modal || setting == ChatSetting.Off)
        {
            _hud.SetChat(Array.Empty<string>());
            return;
        }
        long since = simulation.WorldTick - (long)(ChatFeedSeconds / _session.TickSeconds);
        var recent = simulation.Chat.Where(m => m.Tick >= since);
        var rows = ChatModel.Rows(recent, ChatTab.All, setting, simulation.ChatText,
            id => ChatPanel.Person(simulation, id), simulation.Setup.Clock, 4);
        _hud.SetChat(rows.Select(r => r.Speaker is null ? r.Text : $"{r.Speaker}: {r.Text}").ToList());
    }

    /// <summary>The party, hiring, teaching and offers in the log and the notices: what was refused, and what was agreed.</summary>
    private void SubscribeParty()
    {
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is HireCommand or DismissCommand or SetStanceCommand or SetTacticsCommand or GiveGearCommand or TakeBackGearCommand
                or SpendPartyAttributeCommand or LearnFromTeacherCommand or JoinGroupCommand)
                _hud.Toast(char.ToUpperInvariant(e.Reason[0]) + e.Reason[1..], 3);
        });
        _session.Subscribe<HirelingHired>(e =>
            _hud.Toast($"{Person(e.NpcId)} works for you: {PartyModel.RoleWord(e.Role).ToLowerInvariant()}, {e.Days} {(e.Days == 1 ? "day" : "days")} at {e.DailyWage} coin a day", 4));
        _session.Subscribe<PartyMemberLeft>(e => _hud.Log($"{Person(e.NpcId)} left your party ({e.Reason})"));
        _session.Subscribe<HirelingDied>(e => _hud.Toast($"{Person(e.NpcId)} died in your service", 6));
        _session.Subscribe<Taught>(e => _hud.Log($"{Person(e.NpcId)} taught you {Names.Of(_session, e.DefinitionId)}{(e.Fee > 0 ? $" for {e.Fee} coin" : "")}"));
        _session.Subscribe<LfgJoined>(e => _hud.Toast($"You said you would go with {e.NpcName}: be at {e.AnchorName} by {ChatModel.Clock(e.DepartTick, _session.Setup.Clock)}", 5));
        _session.Subscribe<LfgMissed>(e => _hud.Toast($"{e.NpcName} left without you", 4));
    }
}
