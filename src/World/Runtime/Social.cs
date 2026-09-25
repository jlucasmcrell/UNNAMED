// UNNAMED World - NPCs, conversations, relationships and trade at run time (SYSTEMS.md S-24, S-26, S-28; M4)
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

/// <summary>The NPCs and their conversations, built from content at boot (M4). Where each stands is the region's.</summary>
public sealed record SocialSetup(ImmutableSortedDictionary<string, NpcDefinition> Npcs, ImmutableSortedDictionary<string, DialogueDefinition> Dialogues)
{
    /// <summary>How companions behave (M6); null in a pack where nobody can join.</summary>
    public CompanionTuning? Companions { get; init; }

    public static SocialSetup Empty { get; } = new(ImmutableSortedDictionary.Create<string, NpcDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, DialogueDefinition>(StringComparer.Ordinal));
}

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Speak to an NPC within reach: their conversation opens where it stands for this character.</summary>
public sealed record TalkCommand(EntityId Actor, string NpcId) : GameCommand(Actor);

/// <summary>Give one of the replies the open conversation offers.</summary>
public sealed record ChooseCommand(EntityId Actor, string ReplyId) : GameCommand(Actor);

/// <summary>Walk away from the conversation.</summary>
public sealed record LeaveCommand(EntityId Actor) : GameCommand(Actor);

/// <summary>Buy from a trader within reach: one of their wares (by its <see cref="WareView.Ref"/>), at their price.</summary>
public sealed record BuyCommand(EntityId Actor, string NpcId, string Ware, int Count) : GameCommand(Actor);

/// <summary>Sell a carried stack to a trader within reach who buys that kind of thing.</summary>
public sealed record SellCommand(EntityId Actor, string NpcId, EntityId Item, int Count) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

public sealed record ConversationStarted(string NpcId, string DialogueId, long Tick);

/// <summary>A line is spoken: the conversation stands at this node now.</summary>
public sealed record ConversationLine(string NpcId, string DialogueId, string NodeId, long Tick);

public sealed record ReplyChosen(string NpcId, string DialogueId, string NodeId, string ReplyId, long Tick);

public sealed record ConversationEnded(string NpcId, long Tick);

/// <summary>An NPC opened a service to the player (Phase 1: trade).</summary>
public sealed record ServiceOpened(string NpcId, string Service, long Tick);

/// <summary>What an NPC thinks of the player moved on one dimension, for a named reason (S-26: every change is attributed).</summary>
public sealed record RelationshipChanged(string NpcId, string Dimension, int From, int To, string EventKey, long Tick);

public sealed record ItemBought(string NpcId, string ItemId, int Count, long Price, long Tick);

public sealed record ItemSold(string NpcId, string ItemId, int Count, long Price, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>An NPC as presentation draws them: who, where, and whether the character is talking to them.</summary>
public sealed record NpcView(string Id, EntityId InstanceId, string Name, Body Body, bool Talking)
{
    /// <summary>A companion lying downed (M6).</summary>
    public bool Downed { get; init; }
}

/// <summary>The open conversation: the line, and the replies offered now.</summary>
public sealed record ConversationView(string NpcId, string DialogueId, string NodeId, string Text, ImmutableArray<ReplyView> Replies);

public sealed record ReplyView(string Id, string Text);

/// <summary>A trader's wares as they stand, each at the price they ask for one.</summary>
public sealed record WaresView(string NpcId, string MerchantId, ImmutableArray<WareView> Wares);

/// <summary>A ware: <see cref="Ref"/> is what a <see cref="BuyCommand"/> names it by.</summary>
public sealed record WareView(string Ref, string ItemId, int Count, int Quality, long Price);

// ── state and internal commands ─────────────────────────────────────────────

/// <summary>An NPC's body as the simulation holds it: where they stand and which way they face. Transient in Phase 1.</summary>
internal sealed record NpcState(NpcDefinition Definition, EntityId InstanceId, NpcSite Site, Body Body);

/// <summary>The open conversation: with whom, which graph, at which node. Transient: a load starts outside any conversation.</summary>
internal sealed record Conversation(string NpcId, string DialogueId, string NodeId);

/// <summary>To <see cref="RelationshipSystem"/>: move what an NPC thinks of the player on one dimension, for a reason.</summary>
internal sealed record ChangeRelationship(string NpcId, string Dimension, int Delta, string EventKey) : InternalCommand;

// ── systems ─────────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Npcs"/> - the NPCs' bodies (S-24). Each named NPC stands where the region puts them, turned
/// the way the region says, and turns to face whoever talks to them. Phase 1 has no schedules (the vertical slice) and no
/// simulation tiers for NPCs: the whole population is simulated in full. Identity is derived from the NPC's ID (D-10). A
/// companion's body (M6) is moved by <see cref="CompanionSystem"/> through <see cref="PlaceNpc"/> and saved with the companion;
/// everyone else stands where the region puts them, so nothing about their bodies needs saving.
/// </summary>
internal sealed class NpcSystem
{
    private const int TurnMdegPerTick = 18_000;   // 360 degrees a second at 20 Hz

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public NpcSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    private RuntimeState State => _context.State;

    /// <summary>A named NPC's instance ID: derived from their definition, so they are the same instance in every world and load.</summary>
    public static EntityId InstanceIdOf(string npcId)
    {
        using var h = new CanonicalHasher();
        return EntityId.Create(EntityKind.Npc, 1, h.Add("unnamed.npc/v1").Add(npcId).FinishBytes().AsSpan(0, 10));
    }

    public void Populate()
    {
        foreach (var site in _context.Setup.Layout.Npcs)
        {
            if (!_context.Setup.Social.Npcs.TryGetValue(site.NpcId, out var definition))
                continue;
            var id = InstanceIdOf(site.NpcId);
            if (!State.World.Registry.Exists(id))
                State.World.Registry.CreateEntity(DefinitionId.Parse(site.NpcId), id);
            long y = _context.Setup.Layout.Space.Terrain.HeightAtMm(site.XMm, site.ZMm);
            State.SetNpc(_owner, new NpcState(definition, id, site, new Body(site.XMm, y, site.ZMm, site.FacingMdeg)));
        }
    }

    public string? Handle(PlaceNpc command)
    {
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        State.SetNpc(_owner, npc with { Body = command.Body });
        return null;
    }

    public void Tick(long tick)
    {
        string? talking = State.Conversation?.NpcId;
        var player = State.Body;
        foreach (var npc in State.Npcs.Values.Where(n => !State.Companions.ContainsKey(n.Definition.Id)))
        {
            int wanted = npc.Definition.Id == talking
                ? CombatRules.FacingTowards(npc.Body.XMm, npc.Body.ZMm, player.XMm, player.ZMm)
                : npc.Site.FacingMdeg;
            int delta = (int)(((long)wanted - npc.Body.FacingMdeg + 540_000) % 360_000) - 180_000;
            if (delta == 0)
                continue;
            int facing = (int)(((long)npc.Body.FacingMdeg + Math.Clamp(delta, -TurnMdegPerTick, TurnMdegPerTick) + 360_000) % 360_000);
            State.SetNpc(_owner, npc with { Body = npc.Body with { FacingMdeg = facing } });
        }
    }

    public ImmutableArray<NpcView> Views() =>
        State.Npcs.Values.Select(n => new NpcView(n.Definition.Id, n.InstanceId, n.Definition.Name, n.Body, State.Conversation?.NpcId == n.Definition.Id)
        {
            Downed = State.Companions.TryGetValue(n.Definition.Id, out var companion) && companion.Condition == CompanionCondition.Downed,
        }).ToImmutableArray();
}

/// <summary>
/// Owns: <see cref="StateSlice.Relationships"/> - what each NPC thinks of the player, per dimension (S-26), saved with the
/// player. Every change names its reason; each value is held in [-100, 100].
/// </summary>
internal sealed class RelationshipSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public RelationshipSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    public string? Handle(ChangeRelationship command, long tick)
    {
        if (!Relationships.IsDimension(command.Dimension))
            return $"'{command.Dimension}' is not a relationship dimension";
        int from = _context.State.RelationshipOf(command.NpcId, command.Dimension);
        int to = Relationships.Clamp(from + command.Delta);
        _context.State.SetRelationship(_owner, command.NpcId, command.Dimension, to);
        _context.Events.Publish(new RelationshipChanged(command.NpcId, command.Dimension, from, to, command.EventKey, tick));
        return null;
    }
}

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
        if (!_context.InTalkReach(npc.Body))
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

        // An item changing hands is the one consequence that can be refused (a full pack, say): it goes first, and a
        // refusal refuses the reply before anything else happens. The content lint allows one a reply.
        if (choice.Consequences.OfType<TransferItemConsequence>().FirstOrDefault() is { } transfer && Transfer(open.NpcId, transfer, tick) is { } refused)
            return refused;
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
        return new ConversationView(open.NpcId, open.DialogueId, open.NodeId, node.Text, replies);
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
        State.SetConversation(_owner, new Conversation(npcId, dialogue.Id, node.Id));
        State.MarkVisited(_owner, dialogue.Id, node.Id);
        _context.Events.Publish(new ConversationLine(npcId, dialogue.Id, node.Id, tick));
    }

    private void End(Conversation open, long tick)
    {
        State.SetConversation(_owner, null);
        _context.Events.Publish(new ConversationEnded(open.NpcId, tick));
    }

    private string? Transfer(string npcId, TransferItemConsequence transfer, long tick)
    {
        if (transfer.ToPlayer)
            return _context.Dispatch(new ExchangeItems(ImmutableArray<StackTake>.Empty, transfer.ItemId, transfer.Count, Quality.Standard));
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
            case GiveRecipeConsequence recipe:
                // Already known - from another teacher, say - is no failure: there is simply nothing new to learn.
                _context.Dispatch(new LearnTechnique(new TechniqueLearning(recipe.RecipeId, LearningSource.Teacher, tick) { SourceRef = open.NpcId }));
                break;
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
            case RecruitCompanionConsequence:
                _context.Dispatch(new Recruit(open.NpcId));
                break;
            case OrderCompanionConsequence order:
                _context.Dispatch(new OrderCompanion(open.NpcId, order.Order));
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

    /// <summary>Dialogue reads and writes world flags in the speaker's cell - or reads them in a named place's (L-17).</summary>
    public long WorldFlag(string flagId, string? locationId) =>
        locationId is not null ? State.World.GetFlag(CellOf(locationId), flagId)
        : State.Conversation is { } open ? State.World.GetFlag(SpeakerCell(open.NpcId), flagId) : 0;

    private CellKey CellOf(string locationId) =>
        _context.Setup.Layout.Locations.FirstOrDefault(l => l.Id == locationId) is { } place
            ? CellKey.OfWorld(place.XMm / 1000.0, place.ZMm / 1000.0)
            : throw new InvalidOperationException($"{locationId} is not a place in this region");

    public int Carried(string itemId, int qualityMin) =>
        State.Inventory.Where(e => e.DefId == itemId && e.Quality >= qualityMin).Sum(e => e.Count);

    public int Relationship(string npcId, string dimension) => State.RelationshipOf(npcId, dimension);

    public int SkillLevel(string skillId) => ProgressionEngine.SkillLevel(State.Progression, skillId);

    public int Level => State.Progression.Level;

    public CompanionOrder? CompanionOrderOf(string npcId) => State.Companions.TryGetValue(npcId, out var companion) ? companion.Order : null;

    int IDialogueFacts.StandingLevel(string factionId) => _context.Setup.Factions.Ladder.StandingTierOf(State.StandingOf(factionId)).Level;

    bool IDialogueFacts.ActDone(string kind, string subject) => State.Factions.Acts.Any(a => a.Kind == kind && a.Subject == subject);

    public string QuestState(string questId, string? objectiveId)
    {
        if (!State.Quests.TryGetValue(questId, out var quest))
            return objectiveId is null ? QuestKeys.NotStarted : QuestKeys.NotReached;
        if (objectiveId is null)
            return QuestKeys.Key(quest.Status);
        return quest.Objective(objectiveId) is { } objective ? QuestKeys.Key(objective.Status) : QuestKeys.NotReached;
    }

    /// <summary>Whether each condition of a reply holds now, as it would in a conversation with <paramref name="npcId"/> (the quest debugger).</summary>
    public ImmutableArray<(DialogueCondition Condition, bool Holds)> Check(string npcId, string dialogueId, DialogueChoice choice)
    {
        var facts = new SpeakerFacts(this, npcId);
        return choice.Conditions.Select(c => (c, DialogueRules.Holds(c, dialogueId, facts))).ToImmutableArray();
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
            locationId is not null ? _system.State.World.GetFlag(_system.CellOf(locationId), flagId)
            : _system.State.Npcs.ContainsKey(_npcId) ? _system.State.World.GetFlag(_system.SpeakerCell(_npcId), flagId) : 0;

        public int Carried(string itemId, int qualityMin) => _system.Carried(itemId, qualityMin);

        public int Relationship(string npcId, string dimension) => _system.Relationship(npcId, dimension);

        public int SkillLevel(string skillId) => _system.SkillLevel(skillId);

        public int Level => _system.Level;

        public string QuestState(string questId, string? objectiveId) => _system.QuestState(questId, objectiveId);

        public CompanionOrder? CompanionOrderOf(string npcId) => _system.CompanionOrderOf(npcId);

        int IDialogueFacts.StandingLevel(string factionId) => ((IDialogueFacts)_system).StandingLevel(factionId);

        bool IDialogueFacts.ActDone(string kind, string subject) => ((IDialogueFacts)_system).ActDone(kind, subject);
    }
}

/// <summary>
/// Owns no state. Trade with an NPC who offers it (M3b's economy stub, opened by M4's NPCs): a trader's wares are a container
/// at the trader - their authored stock until the first sale, then a changed container like any other - so buying and selling
/// are one move of a stack and the coin the other way, all or nothing. A trader asks each ware's value times their bias, and
/// pays a fraction of value for what their trade takes; what they buy joins their wares. Their purse is bottomless.
/// </summary>
internal sealed class TradeSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly Func<ContainerSite, ContainerView> _contents;

    public TradeSystem(SystemContext context, EntityId player, Func<ContainerSite, ContainerView> contents)
    {
        _context = context;
        _player = player;
        _contents = contents;
    }

    private RuntimeState State => _context.State;
    private ItemSetup Items => _context.Setup.Items;

    public string? Handle(BuyCommand command, long tick)
    {
        if (Trader(command.Actor, command.NpcId, out var npc, out var merchant) is { } refused)
            return refused;
        var ware = _contents(_context.WaresOf(npc)!).Items.FirstOrDefault(i => i.Ref == command.Ware);
        if (ware is null)
            return $"{npc.Definition.Name} has no such ware";
        if (command.Count < 1 || command.Count > ware.Count)
            return $"{npc.Definition.Name} has {ware.Count} of it";
        long price = AskFor(merchant, ware.DefId) * command.Count;
        if (price > State.Currency)
            return $"not enough coin: {price} asked, {State.Currency} carried";
        if (_context.Dispatch(new Trade(new MoveItemCommand(_player, ware.Ref, ItemPlace.In(merchant.Id), ItemPlace.Carried, command.Count), -price)) is { } failed)
            return failed;
        _context.Events.Publish(new ItemBought(npc.Definition.Id, ware.DefId, command.Count, price, tick));
        return null;
    }

    public string? Handle(SellCommand command, long tick)
    {
        if (Trader(command.Actor, command.NpcId, out var npc, out var merchant) is { } refused)
            return refused;
        if (State.Inventory.FirstOrDefault(e => e.ItemId == command.Item) is not { } entry)
            return $"{command.Item} is not carried";
        if (command.Count < 1 || command.Count > entry.Count)
            return $"only {entry.Count} carried";
        if (Items.Pricing.SellPrice(Items.Catalog.Get(entry.DefId), merchant) is not { } each)
            return $"{npc.Definition.Name} does not buy that";
        if (each == 0)
            return $"{npc.Definition.Name} would give nothing for that";
        long price = each * command.Count;
        if (_context.Dispatch(new Trade(new MoveItemCommand(_player, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.In(merchant.Id), command.Count), price)) is { } failed)
            return failed;
        _context.Events.Publish(new ItemSold(npc.Definition.Id, entry.DefId, command.Count, price, tick));
        return null;
    }

    /// <summary>A trader's wares as they stand, at their price; null when this NPC does not trade.</summary>
    public WaresView? View(string npcId)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc) || _context.WaresOf(npc) is not { } site || !Items.Merchants.TryGetValue(site.Key, out var merchant))
            return null;
        return new WaresView(npcId, merchant.Id, _contents(site).Items
            .Select(i => new WareView(i.Ref, i.DefId, i.Count, i.Quality, AskFor(merchant, i.DefId)))
            .ToImmutableArray());
    }

    /// <summary>What a trader asks for one: its value times their bias for it (1 for anything they did not stock).</summary>
    private long AskFor(Merchant merchant, string itemId) =>
        Items.Pricing.BuyPrice(Items.Catalog.Get(itemId), merchant.Stock.FirstOrDefault(s => s.ItemId == itemId) ?? new MerchantStock(itemId, 0, 1.0));

    private string? Trader(EntityId actor, string npcId, out NpcState npc, out Merchant merchant)
    {
        npc = null!;
        merchant = null!;
        if (actor != _player)
            return $"unknown actor {actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (!State.Npcs.TryGetValue(npcId, out var found))
            return $"there is no one called {npcId} here";
        npc = found;
        if (!npc.Definition.Offers(NpcServices.Trade) || npc.Definition.MerchantId is not { } merchantId || !Items.Merchants.TryGetValue(merchantId, out var stock))
            return $"{npc.Definition.Name} does not trade";
        merchant = stock;
        return !_context.InTalkReach(npc.Body) ? $"{npc.Definition.Name} is out of reach" : null;
    }
}
