// UNNAMED World - conversations as data: the dialogue system, its commands, events and views (SYSTEMS.md S-28; M4)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>Speak to an NPC within reach: their conversation opens where it stands for this character.</summary>
public sealed record TalkCommand(EntityId Actor, string NpcId) : GameCommand(Actor);

/// <summary>Give one of the replies the open conversation offers.</summary>
public sealed record ChooseCommand(EntityId Actor, string ReplyId) : GameCommand(Actor);

/// <summary>Walk away from the conversation.</summary>
public sealed record LeaveCommand(EntityId Actor) : GameCommand(Actor);

public sealed record ConversationStarted(string NpcId, string DialogueId, long Tick);

/// <summary>A line is spoken: the conversation stands at this node now.</summary>
public sealed record ConversationLine(string NpcId, string DialogueId, string NodeId, long Tick)
{
    public bool FirstVisit { get; init; }
}

public sealed record ReplyChosen(string NpcId, string DialogueId, string NodeId, string ReplyId, long Tick);

public sealed record ConversationEnded(string NpcId, long Tick);

/// <summary>An NPC opened a service to the player (Phase 1: trade).</summary>
public sealed record ServiceOpened(string NpcId, string Service, long Tick);

/// <summary>
/// The open conversation: the line as it is said now (the node's wording or a variant of it, E-6), its line ID (E-10), and the replies
/// offered now.
/// </summary>
public sealed record ConversationView(string NpcId, string DialogueId, string NodeId, string Text, ImmutableArray<ReplyView> Replies)
{
    public string LineId { get; init; } = LineIds.Node(DialogueId, NodeId);
}

public sealed record ReplyView(string Id, string Text);

/// <summary>The open conversation: with whom, which graph, at which node. Transient: a load starts outside any conversation.</summary>
internal sealed record Conversation(string NpcId, string DialogueId, string NodeId);

/// <summary>
/// Owns: <see cref="StateSlice.Conversations"/> - which lines of each conversation the character has heard (saved with the
/// player, S-28) and the conversation open now (transient). A conversation is data: the replies on offer follow from their
/// conditions over world state, and a reply's consequences are commands to the systems that own what they change - dialogue
/// never writes another system's state.
/// </summary>
internal sealed class DialogueSystem : IDialogueFacts
{
    /// <summary>The reply that carries a line without replies on - or, at a conversation's last line, ends it.</summary>
    public const string Continue = "continue";

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public DialogueSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private RuntimeState State => _context.State;
    private SocialSetup Setup => _context.Setup.Social;

    public string? Handle(TalkCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        if (npc.Definition.DialogueId is not { } dialogueId || !Setup.Dialogues.TryGetValue(dialogueId, out var dialogue))
            return $"{npc.Definition.Name} has nothing to say";
        if (State.Companions.TryGetValue(command.NpcId, out var companion) && companion.Condition == CompanionCondition.Downed)
            return $"{npc.Definition.Name} is down";
        if (!_context.InTalkReach(npc))
            return $"{npc.Definition.Name} is out of reach";
        if (State.Conversation is { } open)
            End(open, tick);
        if (DialogueRules.Arrive(dialogue, dialogue.Root, n => Visited(dialogue.Id, n)) is not { } node)
            return $"{npc.Definition.Name} has nothing more to say";
        _context.Events.Publish(new ConversationStarted(npc.Definition.Id, dialogue.Id, tick));
        Enter(npc.Definition.Id, dialogue, node, tick);
        return null;
    }

    public string? Handle(ChooseCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.Conversation is not { } open)
            return "not in a conversation";
        var dialogue = Setup.Dialogues[open.DialogueId];
        var node = dialogue.Nodes[open.NodeId];
        if (node.Choices.IsEmpty)
        {
            if (command.ReplyId != Continue)
                return $"'{command.ReplyId}' is not a reply here";
            _context.Events.Publish(new ReplyChosen(open.NpcId, open.DialogueId, open.NodeId, Continue, tick));
            GoTo(open, dialogue, node.Next, tick);
            return null;
        }
        var choice = node.Choices.FirstOrDefault(c => c.Id == command.ReplyId);
        if (choice is null || !DialogueRules.Offered(choice, dialogue.Id, this))
            return $"'{command.ReplyId}' is not a reply here";

        // An item changing hands, a hire and a lesson are the consequences that can be refused (a full pack, a purse short of the first
        // wage or the fee, a requirement unmet): one goes first, and a refusal refuses the reply before anything else happens. The content
        // lint allows one a reply.
        if (choice.Consequences.OfType<TransferItemConsequence>().FirstOrDefault() is { } transfer && Transfer(open.NpcId, transfer, tick) is { } refused)
            return refused;
        if (choice.Consequences.OfType<HireHirelingConsequence>().FirstOrDefault() is { } hire
            && _context.Dispatch(new Hire(open.NpcId, hire.Role, hire.Days)) is { } refusedHire)
            return refusedHire;
        if (choice.Consequences.OfType<TeachConsequence>().FirstOrDefault() is { } teach
            && _context.Dispatch(new Teach(open.NpcId, teach.DefinitionIds)) is { } refusedLesson)
            return refusedLesson;
        if (choice.Consequences.OfType<TakePassageConsequence>().FirstOrDefault() is { } passage
            && _context.Dispatch(new TakePassage(open.NpcId, passage)) is { } refusedPassage)
            return refusedPassage;

        foreach (var consequence in choice.Consequences)
            Apply(open, consequence, tick);
        _context.Events.Publish(new ReplyChosen(open.NpcId, open.DialogueId, open.NodeId, choice.Id, tick));
        GoTo(open, dialogue, choice.Next, tick);
        return null;
    }

    public string? Handle(LeaveCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.Conversation is not { } open)
            return "not in a conversation";
        End(open, tick);
        return null;
    }

    /// <summary>A conversation ends when the character dies or walks away.</summary>
    public void Tick(long tick)
    {
        if (State.Conversation is not { } open)
            return;
        if (State.PlayerCombat.Defeated || !State.Npcs.TryGetValue(open.NpcId, out var npc) || _context.DistanceToPlayer(npc.Body) > _context.TalkReachMm + 1_000)
            End(open, tick);
    }

    public ConversationView? View()
    {
        if (State.Conversation is not { } open)
            return null;
        var dialogue = Setup.Dialogues[open.DialogueId];
        var node = dialogue.Nodes[open.NodeId];
        var replies = node.Choices.IsEmpty
            ? ImmutableArray.Create(new ReplyView(Continue, node.Next is null ? "(leave)" : "(go on)"))
            : node.Choices.Where(c => DialogueRules.Offered(c, dialogue.Id, this)).Select(c => new ReplyView(c.Id, c.Text)).ToImmutableArray();
        var line = DialogueRules.Line(node, dialogue.Id, this);
        return new ConversationView(open.NpcId, open.DialogueId, open.NodeId, line.Text, replies) { LineId = line.LineId };
    }

    private void GoTo(Conversation open, DialogueDefinition dialogue, string? next, long tick)
    {
        if (next is null || DialogueRules.Arrive(dialogue, next, n => Visited(dialogue.Id, n)) is not { } node)
        {
            End(open, tick);
            return;
        }
        Enter(open.NpcId, dialogue, node, tick);
    }

    private void Enter(string npcId, DialogueDefinition dialogue, DialogueNode node, long tick)
    {
        bool first=!Visited(dialogue.Id,node.Id);
        State.SetConversation(_owner, new Conversation(npcId, dialogue.Id, node.Id));
        State.MarkVisited(_owner, dialogue.Id, node.Id);
        _context.Events.Publish(new ConversationLine(npcId, dialogue.Id, node.Id, tick) { FirstVisit=first });
    }

    private void End(Conversation open, long tick)
    {
        State.SetConversation(_owner, null);
        _context.Events.Publish(new ConversationEnded(open.NpcId, tick));
    }

    private string? Transfer(string npcId, TransferItemConsequence transfer, long tick)
    {
        if (transfer.ToPlayer)
            return _context.Dispatch(new ExchangeItems(ImmutableArray<StackTake>.Empty, transfer.ItemId, transfer.Count, Quality.Standard)
            {
                Origin = new NotableOrigin(Domain.Items.ProvenanceKind.Given, npcId),
            });
        var takes = ImmutableArray.CreateBuilder<StackTake>();
        int left = transfer.Count;
        int worst = Quality.Fine;
        foreach (var stack in State.Inventory.Where(e => e.DefId == transfer.ItemId && !State.Equipment.ContainsValue(e.ItemId))
                     .OrderBy(e => e.ItemId.Value, StringComparer.Ordinal))
        {
            int take = Math.Min(left, stack.Count);
            takes.Add(new StackTake(stack.ItemId, take));
            worst = Math.Min(worst, stack.Quality);
            left -= take;
            if (left == 0)
                break;
        }
        if (left > 0)
            return $"needs {transfer.Count} {transfer.ItemId}";
        if (_context.Dispatch(new ExchangeItems(takes.ToImmutable(), null, 0, Quality.Standard)) is { } refused)
            return refused;
        // Handed over: a quest waiting for this delivery counts it (M5).
        _context.Dispatch(new RecordDeed(new Deed(DeedKind.Delivered, transfer.ItemId, transfer.Count, worst, npcId, tick)));
        return null;
    }

    private void Apply(Conversation open, DialogueConsequence consequence, long tick)
    {
        switch (consequence)
        {
            case SetWorldFlagConsequence flag:
                _context.Dispatch(new SetWorldFlag(SpeakerCell(open.NpcId), flag.FlagId, flag.Value));
                break;
            case RelationshipEventConsequence relationship:
                _context.Dispatch(new ChangeRelationship(relationship.NpcId, relationship.Dimension, relationship.Delta, relationship.EventKey));
                break;
            case OpenServiceConsequence service:
                _context.Events.Publish(new ServiceOpened(open.NpcId, service.Service, tick));
                break;
            case StartQuestConsequence start:
                // A quest already started is not started again, and that is no failure of the reply.
                _context.Dispatch(new StartQuest(start.QuestId, open.NpcId));
                break;
            case RecruitCompanionConsequence recruit:
                _context.Dispatch(new Recruit(open.NpcId) { As = recruit.As });
                break;
            case DismissCompanionConsequence:
                _context.Dispatch(new Dismiss(open.NpcId));
                break;
            case OrderCompanionConsequence order:
                _context.Dispatch(new OrderCompanion(open.NpcId, order.Order));
                break;
            case ReportActConsequence report:
                _context.Dispatch(new ReportAct(report.Kind, report.Subject, open.NpcId));
                break;
            case RentRoomConsequence:
                _context.Dispatch(new RentRoom(open.NpcId));
                break;
            case TellLoreConsequence told:
                _context.Dispatch(new TellLore(_player, told.EntryId, open.DialogueId));
                break;
        }
    }

    private CellKey SpeakerCell(string npcId)
    {
        var body = State.Npcs[npcId].Body;
        return CellKey.OfWorld(body.XMm / 1000.0, body.ZMm / 1000.0);
    }

    // ── what conditions may ask ─────────────────────────────────────────────

    public bool Visited(string dialogueId, string nodeId) =>
        State.Conversations.TryGetValue(dialogueId, out var heard) && heard.Contains(nodeId);

    /// <summary>
    /// Dialogue reads and writes world flags in the speaker's cell - or reads them in a named place's (L-17). A place this region does not
    /// hold has nothing set there: the flag reads unset rather than ending the conversation.
    /// </summary>
    public long WorldFlag(string flagId, string? locationId) =>
        locationId is not null ? PlaceFlag(locationId, flagId)
        : State.Conversation is { } open ? State.World.GetFlag(SpeakerCell(open.NpcId), flagId) : 0;

    /// <summary>
    /// A place's flag, read in the space the place's own layout stands in. Kera's offer asks this for
    /// <c>location.barrowworks.intake_hall</c>, which lives in the ring - so with one layout it was never found and
    /// the offer was never shown anywhere (review 3, B6/B3e).
    /// </summary>
    private long PlaceFlag(string locationId, string flagId) =>
        _context.Setup.Sites(l => l.Locations).FirstOrDefault(p => p.Site.Id == locationId) is { Site: { } place, Layout: var layout }
            ? State.World.GetFlag(_context.CellOfSite(layout.SpaceId, place.XMm, place.ZMm), flagId)
            : 0;

    public int Carried(string itemId, int qualityMin) =>
        State.Inventory.Where(e => e.DefId == itemId && e.Quality >= qualityMin).Sum(e => e.Count);

    public int Relationship(string npcId, string dimension) => State.RelationshipOf(npcId, dimension);

    public int SkillLevel(string skillId) => ProgressionEngine.SkillLevel(State.Progression, skillId);

    public int Level => State.Progression.Level;

    public CompanionOrder? CompanionOrderOf(string npcId) => State.Companions.TryGetValue(npcId, out var companion) ? companion.Order : null;

    public int Deeds(DeedKind kind, string subjectId, string? npcId) => DeedSystem.Count(State, kind, subjectId, npcId);

    public int StandingLevel(string factionId) => _context.Setup.Factions.Ladder.StandingTierOf(State.StandingOf(factionId)).Level;

    public bool ActDone(string kind, string subject) => State.Factions.Acts.Any(a => a.Kind == kind && a.Subject == subject);

    public long? DeedLastTick(DeedKind kind, string subjectId, string? npcId) => DeedSystem.LastTick(State, kind, subjectId, npcId);

    public long Now => State.WorldTick;

    /// <summary>The character within a place's radius (WP15b); a place this region does not hold is never near.</summary>
    public bool Near(string locationId, long? radiusMm) =>
        _context.Setup.Layout.Locations.FirstOrDefault(l => l.Id == locationId) is { } place
        && Math.Sqrt(Math.Pow(State.Body.XMm - place.XMm, 2) + Math.Pow(State.Body.ZMm - place.ZMm, 2)) <= (radiusMm ?? place.DiscoveryRadiusMm);

    public string? People => _context.PlayerPeople;

    /// <summary>The character's own Call (D14), so a contact can recognise their own.</summary>
    public string? Call => _context.PlayerCall;

    public string QuestState(string questId, string? objectiveId)
    {
        if (!State.Quests.TryGetValue(questId, out var quest))
            return objectiveId is null ? QuestKeys.NotStarted : QuestKeys.NotReached;
        if (objectiveId is null)
            return QuestKeys.Key(quest.Status);
        return quest.Objective(objectiveId) is { } objective ? QuestKeys.Key(objective.Status) : QuestKeys.NotReached;
    }

    public bool QuestAvailable(string questId) =>
        _context.Setup.Quests.Quests.TryGetValue(questId, out var quest) && QuestRules.CanStart(quest, State.Quests.GetValueOrDefault(questId), State.WorldTick)
        && QuestRules.Confides(quest, npc => State.RelationshipOf(npc, "trust"));

    /// <summary>Whether each condition of a reply holds now, as it would in a conversation with <paramref name="npcId"/> (the quest debugger).</summary>
    public ImmutableArray<(DialogueCondition Condition, bool Holds)> Check(string npcId, string dialogueId, DialogueChoice choice)
    {
        var facts = new SpeakerFacts(this, npcId);
        return choice.Conditions.Select(c => (c, DialogueRules.Holds(c, dialogueId, facts))).ToImmutableArray();
    }

    /// <summary>The barks <paramref name="npcId"/> could say now: their conversation's barks whose conditions hold, with their line IDs.</summary>
    public ImmutableArray<SpokenLine> Barks(string npcId)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc) || npc.Definition.DialogueId is not { } dialogueId
            || !Setup.Dialogues.TryGetValue(dialogueId, out var dialogue))
            return ImmutableArray<SpokenLine>.Empty;
        var facts = new SpeakerFacts(this, npcId);
        return dialogue.Barks.Select((b, i) => (Bark: b, Line: new SpokenLine(LineIds.Bark(npcId, i + 1), b.Text)))
            .Where(b => b.Bark.Conditions.All(c => DialogueRules.Holds(c, dialogueId, facts))).Select(b => b.Line).ToImmutableArray();
    }

    /// <summary>What a conversation with <paramref name="npcId"/> would see: what a teacher's requirements are asked of (WP9).</summary>
    public IDialogueFacts FactsOf(string npcId) => new SpeakerFacts(this, npcId);

    /// <summary>
    /// The banter <paramref name="npcId"/> could say on the road now (WP15b): their conversation's banter lines whose conditions hold, with
    /// their line IDs (<c>banter.&lt;npc&gt;.&lt;n&gt;</c>).
    /// </summary>
    public ImmutableArray<SpokenLine> Banter(string npcId)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc) || npc.Definition.DialogueId is not { } dialogueId
            || !Setup.Dialogues.TryGetValue(dialogueId, out var dialogue) || dialogue.Banter.IsEmpty)
            return ImmutableArray<SpokenLine>.Empty;
        var facts = new SpeakerFacts(this, npcId);
        return dialogue.Banter.Select((b, i) => (Line: b, Spoken: new SpokenLine(LineIds.Banter(npcId, i + 1), b.Text)))
            .Where(b => b.Line.Conditions.All(c => DialogueRules.Holds(c, dialogueId, facts))).Select(b => b.Spoken).ToImmutableArray();
    }

    /// <summary>What a conversation with one NPC would see, without one open: flags in that NPC's cell.</summary>
    private sealed class SpeakerFacts : IDialogueFacts
    {
        private readonly DialogueSystem _system;
        private readonly string _npcId;

        public SpeakerFacts(DialogueSystem system, string npcId)
        {
            _system = system;
            _npcId = npcId;
        }

        public bool Visited(string dialogueId, string nodeId) => _system.Visited(dialogueId, nodeId);

        public long WorldFlag(string flagId, string? locationId) =>
            locationId is not null ? _system.PlaceFlag(locationId, flagId)
            : _system.State.Npcs.ContainsKey(_npcId) ? _system.State.World.GetFlag(_system.SpeakerCell(_npcId), flagId) : 0;

        public int Carried(string itemId, int qualityMin) => _system.Carried(itemId, qualityMin);

        public int Relationship(string npcId, string dimension) => _system.Relationship(npcId, dimension);

        public int SkillLevel(string skillId) => _system.SkillLevel(skillId);

        public int Level => _system.Level;

        public string QuestState(string questId, string? objectiveId) => _system.QuestState(questId, objectiveId);

        public bool QuestAvailable(string questId) => _system.QuestAvailable(questId);

        public CompanionOrder? CompanionOrderOf(string npcId) => _system.CompanionOrderOf(npcId);

        public int Deeds(DeedKind kind, string subjectId, string? npcId) => _system.Deeds(kind, subjectId, npcId);

        public int StandingLevel(string factionId) => _system.StandingLevel(factionId);

        public bool ActDone(string kind, string subject) => _system.ActDone(kind, subject);

        public long? DeedLastTick(DeedKind kind, string subjectId, string? npcId) => _system.DeedLastTick(kind, subjectId, npcId);

        public long Now => _system.Now;

        public bool Near(string locationId, long? radiusMm) => _system.Near(locationId, radiusMm);

        public string? People => _system.People;

    public string? Call => _system.Call;
    }
}
