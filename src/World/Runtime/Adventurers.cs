// UNNAMED World - NPC adventurers in the running world: their days at midnight, what those days do to the boards, the creatures and the
// markets, their bodies at the lodge, asking for company, being hired and grouping up, and the world going on while away (WP13)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>How adventurers live in the region played (<c>config.adventurers</c>; WP13): the rules, the settlements, the roads, how they hire on.</summary>
public sealed record AdventurerSetup(AdventurerRules Rules, ImmutableSortedDictionary<string, AdventurerSettlement> Settlements, ImmutableArray<AdventurerRoad> Roads,
    HirelingProfile Hire)
{
    /// <summary>Every definition's display name (creatures, places), for the words a SettingOut carries.</summary>
    public ImmutableSortedDictionary<string, string> Names { get; init; } = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);
}

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>Someone new took up an adventurer's slot (DN5): their key, name, settlement and level.</summary>
public sealed record AdventurerArrived(string Key, string Name, string Settlement, int Level, long Tick);

/// <summary>An adventurer died out at a ground (DN2: by the outcome table); their gear is gone with them, and their slot waits for an arrival.</summary>
public sealed record AdventurerDied(string Key, string Name, string Where, long Tick);

/// <summary>An adventurer came back hurt and rests at the lodge until <see cref="UntilDay"/>.</summary>
public sealed record AdventurerInjured(string Key, string Name, long UntilDay, long Tick);

/// <summary>An adventurer retired (§2.13): into a role slot of their settlement when one stood open (<see cref="Settled"/>), or out of the region.</summary>
public sealed record AdventurerRetired(string Key, string Name, string Settlement, bool Settled, long Tick);

/// <summary>An adventurer reached a new level on the character's own curve.</summary>
public sealed record AdventurerLevelled(string Key, string Name, int Level, long Tick);

/// <summary>An adventurer bought a piece of the ladder, or commissioned it from the smith.</summary>
public sealed record AdventurerGeared(string Key, string Name, string ItemDefId, bool Commissioned, long Tick);

/// <summary>An adventurer walked the road to another settlement.</summary>
public sealed record AdventurerTravelled(string Key, string Name, string From, string To, long Tick);

/// <summary>An adventurer's day out culled creatures at a ground (they come back by their spawner's timer).</summary>
public sealed record AdventurerHunted(string Key, string Name, string SpawnKey, long Kills, long Tick);

/// <summary>A creature was taken off a ground out of sight by an adventurer's day (WP13): gone, and back by its spawner's timer.</summary>
public sealed record CreatureCulled(EntityId Creature, string Key, long Tick);

/// <summary>The character shared a bounty's purse with an adventurer who went with them (the LFG "the purse splits").</summary>
public sealed record AdventurerShare(string Key, string QuestId, long Coin, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>An adventurer as presentation and the away sheet read them.</summary>
public sealed record AdventurerView(string Key, EntityId InstanceId, string Name, string Settlement, string At, int Level, long Coin, AdventurerCondition Condition,
    string Goal, string? GoalRef, string? Party, bool InTown, long Kills, int BountiesClaimed, int RiskPercent, ImmutableArray<string> Gear);

/// <summary>The kinds of <see cref="AdvanceLine"/> adventurers write while the character is away: town news and the boards.</summary>
public static class AdventurerAdvanceKinds
{
    /// <summary>A bounty taken off a board; <see cref="AdvanceLine.Ref"/> is <c>quest|adventurer</c>, the amount its purse.</summary>
    public const string Claimed = "adventurer.claimed";

    public const string Died = "adventurer.died";

    public const string Arrived = "adventurer.arrived";

    public const string Retired = "adventurer.retired";
}

// ── internal commands ───────────────────────────────────────────────────────

/// <summary>To <see cref="QuestSystem"/>: another actor finished a competitive bounty first (WP4's claim, from inside the world).</summary>
/// The tick is the midnight the day was resolved at: during a catch-up the world's own tick is already the absence's end.
internal sealed record ClaimBounty(EntityId Claimant, string QuestId, long Tick) : InternalCommand;

/// <summary>To <see cref="CreatureSystem"/>: an adventurer's day took so many of a spawner's creatures, out of sight.</summary>
internal sealed record CullCreatures(string SpawnKey, int Count, long Tick) : InternalCommand;

/// <summary>To <see cref="PartySheetSystem"/>: someone joins with a life of their own - their sheet is theirs as they come (WP13: level and gear kept).</summary>
internal sealed record AdoptSheet(PartySheet Sheet) : InternalCommand;

/// <summary>To <see cref="PartySheetSystem"/>: someone with a life of their own left the party and took their sheet back into it.</summary>
internal sealed record ReleaseSheet(string NpcId) : InternalCommand;

/// <summary>To <see cref="NpcSystem"/>: a body for someone the region does not place - an adventurer at their lodge.</summary>
internal sealed record EmbodyNpc(NpcDefinition Definition, EntityId InstanceId, NpcSite Site) : InternalCommand;

/// <summary>To <see cref="NpcSystem"/>: a retiring adventurer takes an open role slot in their settlement, if one stands open (§2.13).</summary>
internal sealed record SettleRetiree(string Settlement, string Name, long Tick) : InternalCommand;

/// <summary>
/// To <see cref="AdventurerSystem"/>: an adventurer joined the party (as <see cref="Membership"/>) or left it, fell, or was wounded, at
/// <paramref name="Tick"/> - the party's tick, which in an absence is the midnight it happened at, not the end of the absence.
/// </summary>
internal sealed record AdventurerPartyChanged(string Key, string Change, long Tick) : InternalCommand
{
    public const string Joined = "joined", Left = "left", Died = "died", Wounded = "wounded";

    public PartyMembership Membership { get; init; }

    /// <summary>When a wound heals.</summary>
    public long UntilTick { get; init; }
}

/// <summary>To <see cref="AdventurerSystem"/>: the character finished a bounty they set out on with an adventurer: the purse splits.</summary>
internal sealed record ShareBounty(string QuestId) : InternalCommand;

/// <summary>
/// What adventurers trade at the markets on a calendar day (WP13): goods each way and coin to merchants. The markets apply a day's before they
/// settle it. Adventurers live their midnights before the markets settle them - in the running world and in the catch-up alike - so a day's
/// flows are always there when its market asks, and asked once; nothing of them outlives the tick, so nothing of them is saved.
/// </summary>
internal interface IAgentTrade
{
    IEnumerable<(string MarketId, string ItemId, long Delta)> Goods(long day);

    IEnumerable<(string MerchantId, long Coin)> Coin(long day);
}

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Adventurers"/> - every adventurer's life, and the slots awaiting arrivals (WP13, §2.13). Their days run at the
/// world's midnights by <see cref="AdventurerEngine"/>, the same step while the world runs and while the character is away, on absolute day
/// boundaries (T-22). What a day does elsewhere goes by command: a claim to the quests, culls to the creatures, retirements to NPC life,
/// trade to the markets (<see cref="IAgentTrade"/>). Their bodies stand at the lodge while they are in town - made and taken away only out of
/// sight (spawn suppression) - and are the NPC system's. They ask for company through SettingOut (<see cref="ISettingOutSource"/>), go with
/// the character as temporary allies when answered, and hire on through the party's hiring (WP15a) with their level and gear; while with the
/// party their own life waits, and they take it up again, with what they gained, when they leave.
/// </summary>
internal sealed class AdventurerSystem : IAbstractAdvance, IAgentTrade, ISettingOutSource, INpcHireability
{
    /// <summary>How far apart bodies stand at a lodge.</summary>
    private const long LodgeSpacingMm = 1_200;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;
    private readonly SortedDictionary<long, List<(string MarketId, string ItemId, long Delta)>> _goods = new();
    private readonly SortedDictionary<long, List<(string MerchantId, long Coin)>> _coin = new();
    private ImmutableSortedDictionary<string, AdventurerGround>? _grounds;
    private ImmutableArray<AdventurerBounty>? _bounties;

    /// <summary>The last hireability answer raised for each adventurer: events only, not state.</summary>
    private readonly Dictionary<string, bool> _hireable = new(StringComparer.Ordinal);

    public AdventurerSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private RuntimeState State => _context.State;
    private AdventurerSetup? Setup => _context.Setup.Adventurers;
    private WorldClock Clock => _context.Setup.Clock;
    private AdventurerState? Current => State.World.Adventurers;

    /// <summary>An adventurer's instance ID: derived from their key, so they are the same instance on every load.</summary>
    public static EntityId InstanceIdOf(string key)
    {
        using var h = new CanonicalHasher();
        return EntityId.Create(EntityKind.Npc, 1, h.Add("unnamed.adventurer/v1").Add(key).FinishBytes().AsSpan(0, 10));
    }

    /// <summary>A gear piece's identity: its owner and how many they had acquired before it.</summary>
    public static EntityId GearIdOf(string key, int serial)
    {
        using var h = new CanonicalHasher();
        return EntityId.Create(EntityKind.Item, 1, h.Add("unnamed.adventurer-gear/v1").Add(key).Add(serial).FinishBytes().AsSpan(0, 10));
    }

    /// <summary>What the party, the boards and the LFG channel tell adventurers, each answered with a command.</summary>
    public void Listen(SimulationEvents events)
    {
        events.Listen<CompanionRecruited>(e =>
        {
            if (Agent(e.NpcId) is not null && State.Companions.TryGetValue(e.NpcId, out var member))
                _context.Dispatch(new AdventurerPartyChanged(e.NpcId, AdventurerPartyChanged.Joined, e.Tick) { Membership = member.Membership });
        });
        events.Listen<PartyMemberLeft>(e =>
        {
            if (Agent(e.NpcId) is not null)
                _context.Dispatch(new AdventurerPartyChanged(e.NpcId, AdventurerPartyChanged.Left, e.Tick));
        });
        events.Listen<HirelingDied>(e =>
        {
            if (Agent(e.NpcId) is not null)
                _context.Dispatch(new AdventurerPartyChanged(e.NpcId, AdventurerPartyChanged.Died, e.Tick));
        });
        events.Listen<CompanionGravelyWounded>(e =>
        {
            if (Agent(e.NpcId) is not null)
                _context.Dispatch(new AdventurerPartyChanged(e.NpcId, AdventurerPartyChanged.Wounded, e.Tick) { UntilTick = e.UntilTick });
        });
        // An answered offer: the group sets out, the adventurer with the character as a temporary ally (WP16's group; its life is ours).
        events.Listen<AllyGroupFormed>(e =>
        {
            if (AgentByInstance(e.Group.Leader) is { } a)
                _context.Dispatch(new Recruit(a.Key) { As = PartyMembership.TemporaryAlly });
        });
        // The character finished the bounty a group set out on, and was paid for it (a competitive bounty's claim comes after its rewards).
        events.Listen<BountyClaimed>(e =>
        {
            if (e.ClaimantId == _player.Value
                && Current?.Agents.Values.Any(a => a.Party == PartyKeys.Key(PartyMembership.TemporaryAlly) && a.Plan.Ref == e.QuestId) == true)
                _context.Dispatch(new ShareBounty(e.QuestId));
        });
    }

    // ── populating, ticking, advancing ──────────────────────────────────────

    /// <summary>
    /// The adventurers the world delta holds, carried onto what content defines now, or a new world's; then the bodies of those in town or
    /// with the party. A pack with no adventurers holds none.
    /// </summary>
    public void Populate()
    {
        if (Setup is not { } setup)
        {
            if (Current is not null)
                State.SetAdventurers(_owner, AdventurerState.Empty(Clock.Day(State.WorldTick)));
            return;
        }
        long today = Clock.Day(State.WorldTick);
        var (state, _) = AdventurerEngine.Reconcile(Current, setup.Rules, Day(Current?.Day ?? today));
        // A hireling who died in the character's service stays dead (DN9); the slot waits for its arrival.
        foreach (var fallen in state.Agents.Keys.Where(State.Fallen.Contains).ToList())
            state = AdventurerEngine.Remove(state, fallen, setup.Rules, Day(state.Day));
        State.SetAdventurers(_owner, state);
        Bodies(State.WorldTick, force: true);
    }

    /// <summary>One tick: a midnight crossed lives the day; then bodies come and go at the lodge, out of sight only.</summary>
    public void Tick(long tick)
    {
        if (Current is { } state && Clock.Day(tick) > state.Day)
            LiveTo(Clock.Day(tick), null);
        Bodies(tick, force: false);
        Hireabilities(tick);
    }

    /// <summary>
    /// The world going on while the character was away (WP11): every midnight of the absence in turn, by the same step the running world
    /// takes (T-22). It runs before the creatures, NPC life and the markets in the advance, so each sees what the adventurers did as it settles.
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        if (Current is not { } state || Clock.Day(toTick) <= state.Day)
            return;
        LiveTo(Clock.Day(toTick), log);
        Bodies(toTick, force: false);
    }

    private void LiveTo(long day, AdvanceLog? log)
    {
        while (Current is { } state && state.Day < day && Setup is { } setup)
        {
            long d = state.Day + 1;
            long midnight = d * Clock.TicksPerDay - Clock.StartTicks;
            // A temporary ally's day with the character is over at midnight: they part, and go on with their own life. One lying downed
            // stays with the party until helped up or fallen.
            foreach (var ally in state.Agents.Values.Where(a => a.Party == PartyKeys.Key(PartyMembership.TemporaryAlly)).ToList())
                _context.Dispatch(new Dismiss(ally.Key));
            var (next, happenings) = AdventurerEngine.Midnight(Current!, setup.Rules, Day(d));
            State.SetAdventurers(_owner, next);
            Carry(state, happenings, midnight, log);
        }
    }

    /// <summary>What a midnight did, carried to its owners by command, published, and logged for the away sheet.</summary>
    private void Carry(AdventurerState before, ImmutableArray<AdventurerHappening> happenings, long tick, AdvanceLog? log)
    {
        var setup = Setup!;
        long day = Clock.Day(tick);
        string NameOf(string key) => Current!.Agents.GetValueOrDefault(key)?.Name ?? before.Agents.GetValueOrDefault(key)?.Name ?? key;
        foreach (var h in happenings)
        {
            switch (h.Kind)
            {
                case AdventurerEngine.Arrived:
                    var a = Current!.Agents[h.Key];
                    _context.Events.Publish(new AdventurerArrived(h.Key, a.Name, a.Settlement, a.Level, tick));
                    log?.Add(new AdvanceLine(tick, AdventurerAdvanceKinds.Arrived, h.Key));
                    break;
                case AdventurerEngine.Died:
                    _context.Events.Publish(new AdventurerDied(h.Key, NameOf(h.Key), h.Ref ?? "", tick));
                    log?.Add(new AdvanceLine(tick, AdventurerAdvanceKinds.Died, h.Key));
                    break;
                case AdventurerEngine.Injured:
                    _context.Events.Publish(new AdventurerInjured(h.Key, NameOf(h.Key), day + h.Amount, tick));
                    break;
                case AdventurerEngine.Retired:
                    bool settled = _context.Dispatch(new SettleRetiree(h.Settlement!, h.Ref!, tick)) is null;
                    _context.Events.Publish(new AdventurerRetired(h.Key, NameOf(h.Key), h.Settlement!, settled, tick));
                    log?.Add(new AdvanceLine(tick, AdventurerAdvanceKinds.Retired, h.Key));
                    break;
                case AdventurerEngine.Claimed:
                    if (_context.Dispatch(new ClaimBounty(InstanceIdOf(h.Key), h.Ref!, tick)) is null)
                        log?.Add(new AdvanceLine(tick, AdventurerAdvanceKinds.Claimed, $"{h.Ref}|{h.Key}", h.Coin));
                    break;
                case AdventurerEngine.Culled:
                    _context.Dispatch(new CullCreatures(h.Ref!, (int)h.Amount, tick));
                    _context.Events.Publish(new AdventurerHunted(h.Key, NameOf(h.Key), h.Ref!, h.Amount, tick));
                    break;
                case AdventurerEngine.Sold when h.Market is { } sold:
                    Flow(day, sold, h.Ref!, h.Amount);
                    break;
                case AdventurerEngine.Consumed:
                    Flow(day, h.Market!, h.Ref!, -h.Amount);
                    break;
                case AdventurerEngine.Bought:
                    Flow(day, h.Market!, h.Ref!, -h.Amount);
                    if (SellerOf(h.Market!, h.Ref!) is { } seller)
                        Pay(day, seller, h.Coin);
                    if (setup.Rules.GearLadder.Contains(h.Ref!))
                        _context.Events.Publish(new AdventurerGeared(h.Key, NameOf(h.Key), h.Ref!, false, tick));
                    break;
                case AdventurerEngine.Commissioned:
                    if (h.Settlement is { } smith)
                        Pay(day, smith, h.Coin);
                    _context.Events.Publish(new AdventurerGeared(h.Key, NameOf(h.Key), h.Ref!, true, tick));
                    break;
                case AdventurerEngine.Travelled:
                    _context.Events.Publish(new AdventurerTravelled(h.Key, NameOf(h.Key), h.Settlement ?? "", h.Ref!, tick));
                    break;
                case AdventurerEngine.LevelUp:
                    _context.Events.Publish(new AdventurerLevelled(h.Key, NameOf(h.Key), (int)h.Amount, tick));
                    break;
            }
        }
    }

    /// <summary>The merchant of a market who sells a good, or its first merchant; null when it has none.</summary>
    private string? SellerOf(string marketId, string itemId)
    {
        var economy = _context.Setup.Economy;
        if (!economy.Markets.TryGetValue(marketId, out var market))
            return null;
        return market.Merchants.FirstOrDefault(m => economy.Merchants.TryGetValue(m, out var e) && e.Goods.Contains(itemId)) ?? market.Merchants.FirstOrDefault();
    }

    private void Flow(long day, string marketId, string itemId, long delta)
    {
        if (!_goods.TryGetValue(day, out var list))
            _goods[day] = list = new List<(string, string, long)>();
        list.Add((marketId, itemId, delta));
    }

    private void Pay(long day, string merchantId, long coin)
    {
        if (coin <= 0)
            return;
        if (!_coin.TryGetValue(day, out var list))
            _coin[day] = list = new List<(string, long)>();
        list.Add((merchantId, coin));
    }

    /// <summary>A day's goods, given once: the market settling the day takes them.</summary>
    public IEnumerable<(string MarketId, string ItemId, long Delta)> Goods(long day) => Drain(_goods, day);

    public IEnumerable<(string MerchantId, long Coin)> Coin(long day) => Drain(_coin, day);

    private static List<T> Drain<T>(SortedDictionary<long, List<T>> flows, long day)
    {
        // Anything older than the day asked is a day no market keeps: it leaves with this one.
        var due = flows.Keys.Where(d => d <= day).ToList();
        var result = flows.TryGetValue(day, out var list) ? list : new List<T>();
        foreach (long d in due)
            flows.Remove(d);
        return result;
    }

    // ── the day's reading of the world ──────────────────────────────────────

    /// <summary>What a midnight reads: the day, its draws (one channel a day, each adventurer's sample by a stable hash of key and use), content.</summary>
    private AdventurerDay Day(long day)
    {
        var setup = Setup!;
        long tick = Math.Max(0, day * Clock.TicksPerDay - Clock.StartTicks);
        var channel = RngChannel.Open(State.World.WorldSeed, RegionCell, "adventurers", $"day@{day}");
        var catalog = _context.Setup.Items.Catalog;
        var economy = _context.Setup.Economy;
        return new AdventurerDay(day, tick, Clock.TicksPerDay / Clock.HoursPerDay, use => channel.UInt64(KeyHash(use)))
        {
            Settlements = setup.Settlements,
            Roads = setup.Roads,
            Grounds = Grounds,
            Bounties = Bounties,
            CanClaim = questId => _context.Setup.Quests.Quests.TryGetValue(questId, out var quest)
                                  && QuestRules.CanClaim(quest, State.Quests.GetValueOrDefault(questId), tick),
            Item = catalog.Find,
            Keeps = (settlement, item) => setup.Settlements.TryGetValue(settlement, out var s) && economy.Markets.TryGetValue(s.MarketId, out var m)
                                          && m.Goods.ContainsKey(item),
            Recipe = item => _context.Setup.Crafting.Recipes.Values.Where(r => r.OutputItemId == item).OrderBy(r => r.Id, StringComparer.Ordinal)
                .Select(r => (ImmutableArray<(string, int)>?)r.Inputs.Select(i => (i.ItemId, i.Count)).ToImmutableArray()).FirstOrDefault(),
            Loot = Loot,
            Yields = (spawn, item) => Grounds.ContainsKey(spawn) && MembersOf(spawn).Any(c => c.LootTableId is { } t && Holds(t, item, 0)),
            NameFor = NameFor,
            // Spawn suppression is the bodies' (Bodies: none made in sight); the arrival itself is never held back.
            CanArrive = _ => true,
            Progression = _context.Setup.Progression,
            KillXp = creature => _context.Setup.Combat.Creatures.TryGetValue(creature, out var c) ? c.XpValue : 0,
            GearId = GearIdOf,
        };
    }

    /// <summary>The hunting grounds the settlements name that the region places: each spawner's first creature, its highest level and its size.</summary>
    private ImmutableSortedDictionary<string, AdventurerGround> Grounds => _grounds ??= Setup!.Settlements.Values
        .SelectMany(s => s.Grounds.Select(g => (Settlement: s.Id, Site: _context.Setup.Combat.Spawns.FirstOrDefault(site => site.Key == g))))
        .Where(g => g.Site is { Members.Length: > 0 } && g.Site.Members.All(m => _context.Setup.Combat.Creatures.ContainsKey(m.CreatureId)))
        .GroupBy(g => g.Site!.Key, StringComparer.Ordinal)
        .Select(g => g.First())
        .ToImmutableSortedDictionary(g => g.Site!.Key, g => new AdventurerGround(g.Site!.Key, g.Settlement, g.Site.Members[0].CreatureId,
            g.Site.Members.Max(m => _context.Setup.Combat.Creatures[m.CreatureId].Level), g.Site.Members.Length), StringComparer.Ordinal);

    private IEnumerable<Domain.Combat.CreatureDefinition> MembersOf(string spawnKey) =>
        _context.Setup.Combat.Spawns.First(s => s.Key == spawnKey).Members.Select(m => _context.Setup.Combat.Creatures[m.CreatureId]);

    /// <summary>
    /// The competitive bounties adventurers read (WP4's <c>competitive: true</c>): a single kill or delivery objective, posted at the
    /// settlement whose life its giver lives in, paying its currency and XP.
    /// </summary>
    private ImmutableArray<AdventurerBounty> Bounties => _bounties ??= _context.Setup.Quests.Quests.Values
        .Where(q => q.Competitive && q.Order.Length == 1 && q.GiverId is not null)
        .Select(q =>
        {
            string? settlement = _context.Setup.NpcLife.RoutineOf(q.GiverId!)?.Settlement;
            long coin = q.Rewards.OfType<CurrencyReward>().Sum(r => r.Amount), xp = q.Rewards.OfType<XpReward>().Sum(r => r.Amount);
            return (settlement, q.Order[0].Condition) switch
            {
                ({ } s, KillCreature kill) => new AdventurerBounty(q.Id, q.Title, s, kill.CreatureId, null, kill.Count, coin, xp),
                ({ } s, DeliverItem deliver) => new AdventurerBounty(q.Id, q.Title, s, null, deliver.ItemId, deliver.Count, coin, xp),
                _ => null,
            };
        })
        .OfType<AdventurerBounty>().OrderBy(b => b.QuestId, StringComparer.Ordinal).ToImmutableArray();

    /// <summary>One kill's loot at a ground: a member drawn by the sample, its loot table rolled on a channel keyed by the sample.</summary>
    private ImmutableArray<(string ItemId, int Count)> Loot(string spawnKey, ulong sample)
    {
        var members = MembersOf(spawnKey).ToList();
        var creature = members[(int)(sample % (ulong)members.Count)];
        var tables = _context.Setup.Items.LootTables;
        if (creature.LootTableId is not { } tableId || !tables.TryGetValue(tableId, out var table))
            return ImmutableArray<(string, int)>.Empty;
        var channel = RngChannel.Open(State.World.WorldSeed, RegionCell, "adventurers", $"loot:{sample}");
        return LootRoller.Roll(table, tables, i => channel.UInt64(i) / 18446744073709551616.0).Select(d => (d.ItemId, d.Count)).ToImmutableArray();
    }

    private bool Holds(string tableId, string itemId, int depth) =>
        depth <= 8 && _context.Setup.Items.LootTables.TryGetValue(tableId, out var table)
        && table.Weighted.Concat(table.Independent).Concat(table.Guaranteed)
            .Any(e => e.ItemId == itemId || (e.TableId is { } nested && Holds(nested, itemId, depth + 1)));

    /// <summary>
    /// A newcomer's name (WP2's Suggest name source): the first playable people's name a sample picks, passing over names any adventurer or
    /// resident already carries while the list lasts. Never invented.
    /// </summary>
    private (string Name, string? People)? NameFor(string key, int generation, IReadOnlySet<string> taken)
    {
        if (_context.Setup.Character.Peoples.Values.Where(p => p.Playable && !p.Names.IsEmpty).OrderBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault() is not { } people)
            return null;
        var all = taken.Concat((State.World.NpcLife?.Npcs.Values ?? Enumerable.Empty<NpcLifeRecord>()).Select(n => n.Name).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        ulong sample = RngChannel.Open(State.World.WorldSeed, RegionCell, "adventurers", $"name:{key}").UInt64(0);
        return (people.Names.PickUnused(sample, all) ?? people.Names.Pick(sample), people.Id);
    }

    private CellKey RegionCell => new(CellKey.Parse(_context.Setup.Layout.CellKeys[0]).Region, 0, 0);

    private SimulationTier TierAt(long xMm, long zMm) =>
        State.Tiers.GetValueOrDefault(CellKey.OfWorld(xMm / 1000.0, zMm / 1000.0).ToString(), SimulationTier.D);

    private static uint KeyHash(string key)
    {
        uint h = 2_166_136_261;
        foreach (char c in key)
            h = (h ^ c) * 16_777_619;
        return h;
    }

    // ── bodies at the lodge ─────────────────────────────────────────────────

    /// <summary>Whether an adventurer stands at their lodge now: injured, resting, or not yet left for the day's work.</summary>
    private bool InTown(AdventurerRecord a, long tick) =>
        a.Condition == AdventurerCondition.Injured || !AdventurerGoals.Out(a.Plan.Goal) || a.Plan.Day != Clock.Day(tick) || tick < a.Plan.DepartTick;

    /// <summary>
    /// Bodies come and go: an adventurer in town or with the party has one, at their lodge; one out, dead or retired has none. A body is made
    /// or taken away only where the character cannot see it (tier A never); at a load (<paramref name="force"/>) every one that should be is made.
    /// </summary>
    private void Bodies(long tick, bool force)
    {
        if (Current is not { } state || Setup is not { } setup)
            return;
        foreach (var a in state.Agents.Values)
        {
            bool wanted = a.Party is not null || InTown(a, tick);
            bool has = State.Npcs.ContainsKey(a.Key);
            if (wanted == has || State.Companions.ContainsKey(a.Key) || !setup.Settlements.TryGetValue(a.At, out var town))
                continue;
            if (wanted)
            {
                if (State.Fallen.Contains(a.Key) || (!force && TierAt(town.LodgeXMm, town.LodgeZMm) == SimulationTier.A))
                    continue;
                _context.Dispatch(new EmbodyNpc(Definition(a), InstanceIdOf(a.Key), SiteOf(a, town)));
            }
            else if (TierAt(State.Npcs[a.Key].Body.XMm, State.Npcs[a.Key].Body.ZMm) != SimulationTier.A)
                _context.Dispatch(new RemoveNpc(a.Key, tick) { KeepIdentity = true });
        }
        // Those no longer living their lives here - dead, retired - leave their body only out of sight.
        foreach (var npc in State.Npcs.Values.Where(n => n.Definition.Role == Role && !state.Agents.ContainsKey(n.Definition.Id)
                     && !State.Companions.ContainsKey(n.Definition.Id)).ToList())
        {
            if (TierAt(npc.Body.XMm, npc.Body.ZMm) != SimulationTier.A)
                _context.Dispatch(new RemoveNpc(npc.Definition.Id, tick));
        }
    }

    public const string Role = "adventurer";

    /// <summary>An adventurer as an NPC: no services, no dialogue, hireable on the config's roles with the config's body, and able to go along as an ally.</summary>
    private NpcDefinition Definition(AdventurerRecord a) =>
        new(a.Key, a.Name, Role, ImmutableArray<string>.Empty, null, null) { Hireling = Setup!.Hire, Companion = Setup.Hire.Body };

    /// <summary>Their place at the lodge: in a row across it, by slot.</summary>
    private static NpcSite SiteOf(AdventurerRecord a, AdventurerSettlement town)
    {
        int number = int.TryParse(a.Slot[(a.Slot.LastIndexOf('_') + 1)..], out int n) ? n : 1;
        long offset = ((number - 1) % 6 - 2) * LodgeSpacingMm;
        double facing = town.LodgeFacingMdeg / 1000.0 * Math.PI / 180;
        long x = town.LodgeXMm + (long)Math.Round(Math.Cos(facing) * offset), z = town.LodgeZMm - (long)Math.Round(Math.Sin(facing) * offset);
        return new NpcSite(a.Key, x, z, town.LodgeFacingMdeg);
    }

    // ── the party ───────────────────────────────────────────────────────────

    public string? Handle(AdventurerPartyChanged command)
    {
        if (Current is not { } state || Agent(command.Key) is not { } a || Setup is not { } setup)
            return $"no adventurer called {command.Key}";
        switch (command.Change)
        {
            case AdventurerPartyChanged.Joined:
                // Their sheet is theirs as they are (§2.15: adventurers keep their level and gear when hired), and their own life waits.
                var gear = a.Gear.ToImmutableSortedDictionary(g => g.Key, g => new PartyGear(new InventoryEntry(g.Value.ItemId, g.Value.DefId, 1) { Quality = g.Value.Quality }, false));
                _context.Dispatch(new AdoptSheet(new PartySheet(a.Key, a.Progression) { Gear = gear }));
                _context.Dispatch(new RefitMember(a.Key));
                Set(a with
                {
                    Party = PartyKeys.Key(command.Membership),
                    Gear = ImmutableSortedDictionary<EquipSlot, AdventurerGear>.Empty,
                    Plan = new AdventurerPlan(Clock.Day(command.Tick), AdventurerGoals.Rest, command.Membership == PartyMembership.TemporaryAlly ? a.Plan.Ref : null),
                });
                return null;
            case AdventurerPartyChanged.Left:
            case AdventurerPartyChanged.Wounded:
                a = Restored(a) with { Party = null, Plan = AdventurerPlan.Resting(Clock.Day(command.Tick)) };
                if (command.Change == AdventurerPartyChanged.Wounded)
                    a = a with { Condition = AdventurerCondition.Injured, InjuredUntilDay = Clock.Day(command.UntilTick) + 1 };
                _context.Dispatch(new ReleaseSheet(a.Key));
                Set(a);
                return null;
            case AdventurerPartyChanged.Died:
                // The slot waits from the day they fell: their arrival is counted from it.
                State.SetAdventurers(_owner, AdventurerEngine.Remove(state, a.Key, setup.Rules, Day(Clock.Day(command.Tick))));
                _context.Events.Publish(new AdventurerDied(a.Key, a.Name, "party", command.Tick));
                return null;
            default:
                return $"no such change '{command.Change}'";
        }
    }

    /// <summary>What they take back from the party: their sheet's level and skills, what they wear that is theirs, and their own things.</summary>
    private AdventurerRecord Restored(AdventurerRecord a)
    {
        if (!State.PartySheets.TryGetValue(a.Key, out var sheet))
            return a;
        var gear = sheet.Gear.Where(g => !g.Value.Loaned)
            .ToImmutableSortedDictionary(g => g.Key, g => new AdventurerGear(g.Value.Item.ItemId, g.Value.Item.DefId, g.Value.Item.Quality));
        var pack = a.Pack;
        foreach (var item in sheet.Belongings)
            pack = pack.SetItem(item.DefId, pack.GetValueOrDefault(item.DefId) + item.Count);
        return a with { Progression = AdventurerEngine.Spend(sheet.Progression), Gear = gear, Pack = pack };
    }

    /// <summary>The character finished the bounty they set out on together: half its purse goes to the adventurer, and the group parts.</summary>
    public string? Handle(ShareBounty command, long tick)
    {
        if (Current?.Agents.Values.FirstOrDefault(a => a.Party == PartyKeys.Key(PartyMembership.TemporaryAlly) && a.Plan.Ref == command.QuestId) is not { } a
            || !_context.Setup.Quests.Quests.TryGetValue(command.QuestId, out var quest))
            return null;
        long half = quest.Rewards.OfType<CurrencyReward>().Sum(r => r.Amount) / 2;
        if (half > 0 && _context.Dispatch(new SpendCurrency(half)) is null)
        {
            Set(a with { Coin = a.Coin + half, Plan = a.Plan with { Ref = null } });
            _context.Events.Publish(new AdventurerShare(a.Key, command.QuestId, half, tick));
        }
        _context.Dispatch(new Dismiss(a.Key));
        return null;
    }

    // ── hireability (WP12b's contract) ──────────────────────────────────────

    /// <summary>
    /// Why an adventurer cannot be hired now, worded after their name as WP12b's refusals are, or null: they hire on while in town, on their
    /// feet, with a body, and not with the party.
    /// </summary>
    private string? Refusal(AdventurerRecord a) =>
        a.Party is not null ? "is with you"
        : a.Condition == AdventurerCondition.Injured ? "is resting an injury"
        : !InTown(a, State.WorldTick) ? "is out"
        : !State.Npcs.ContainsKey(a.Key) ? "is not here"
        : null;

    /// <summary>Why an adventurer cannot be hired now, for the party's hire (as <see cref="NpcSystem.HireRefusal"/> is); null for anyone else.</summary>
    public string? HireRefusal(string key) => Agent(key) is { } a ? Refusal(a) : null;

    public bool IsHireable(string key) => Agent(key) is { } a && Refusal(a) is null;

    public NpcHireView? Hireability(string key) =>
        Agent(key) is { } a
            ? new NpcHireView(key, Refusal(a) is null, Refusal(a), ImmutableArray.Create(Role), Setup?.Hire.Roles ?? ImmutableArray<HirelingRole>.Empty)
            : null;

    /// <summary>Raise <see cref="HireabilityChanged"/> for every adventurer whose answer changed since the last tick, and forget those gone.</summary>
    private void Hireabilities(long tick)
    {
        if (Current is not { } state)
            return;
        foreach (var a in state.Agents.Values)
        {
            string? refusal = Refusal(a);
            if (_hireable.TryGetValue(a.Key, out bool was) && was == (refusal is null))
                continue;
            _hireable[a.Key] = refusal is null;
            _context.Events.Publish(new HireabilityChanged(a.Key, refusal is null, refusal, tick));
        }
        foreach (string gone in _hireable.Keys.Where(k => !state.Agents.ContainsKey(k)).ToList())
        {
            _hireable.Remove(gone);
            _context.Events.Publish(new HireabilityChanged(gone, false, "gone", tick));
        }
    }

    // ── reads ───────────────────────────────────────────────────────────────

    private AdventurerRecord? Agent(string key) => Current?.Agents.GetValueOrDefault(key);

    private AdventurerRecord? AgentByInstance(EntityId id) => Current?.Agents.Values.FirstOrDefault(a => InstanceIdOf(a.Key) == id);

    /// <summary>An adventurer's name by their instance ID - a claimant on a board - or null for anyone else.</summary>
    public string? NameOf(string instanceId) => Current?.Agents.Values.FirstOrDefault(a => InstanceIdOf(a.Key).Value == instanceId)?.Name;

    public ImmutableArray<AdventurerView> Views()
    {
        if (Current is not { } state)
            return ImmutableArray<AdventurerView>.Empty;
        long tick = State.WorldTick;
        return state.Agents.Values.Select(a =>
        {
            var gear = (a.Party is not null && State.PartySheets.TryGetValue(a.Key, out var sheet)
                ? sheet.Gear.Values.Select(g => g.Item.DefId)
                : a.Gear.Values.Select(g => g.DefId)).ToImmutableArray();
            return new AdventurerView(a.Key, InstanceIdOf(a.Key), a.Name, a.Settlement, a.At,
                a.Party is not null && State.PartySheets.TryGetValue(a.Key, out var s) ? s.Progression.Level : a.Level, a.Coin, a.Condition,
                a.Plan.Goal, a.Plan.Ref, a.Party, a.Party is null && InTown(a, tick), a.Kills, a.BountiesClaimed, a.RiskPercent, gear);
        }).ToImmutableArray();
    }

    /// <summary>
    /// The SettingOuts adventurers post (the WP12 contract, read by tick): each one asking for company today whose post falls in
    /// (<paramref name="fromTick"/>, <paramref name="toTick"/>], waiting at their lodge until they leave. A pure read of the world.
    /// </summary>
    public IEnumerable<SettingOut> Between(long fromTick, long toTick)
    {
        if (Current is not { } state || Setup is not { } setup)
            yield break;
        foreach (var a in state.Agents.Values.Where(a => a.Party is null && a.Condition == AdventurerCondition.Active && a.Plan.AsksCompany
                                                         && a.Plan.PostTick > fromTick && a.Plan.PostTick <= toTick).OrderBy(a => a.Plan.PostTick).ThenBy(a => a.Key, StringComparer.Ordinal))
        {
            if (!setup.Settlements.TryGetValue(a.At, out var town))
                continue;
            var (purpose, purposeName, destination) = Words(a);
            string anchor = town.LocationId ?? town.Id;
            yield return new SettingOut(InstanceIdOf(a.Key), a.Name, purpose, purposeName, destination, anchor, setup.Names.GetValueOrDefault(anchor, town.Id),
                town.LodgeXMm, town.LodgeZMm, a.Plan.DepartTick, a.Plan.PostTick);
        }
    }

    /// <summary>A day out in words: the purpose (a bounty's title, or the creature), and the place nearest the ground.</summary>
    private (string Purpose, string PurposeName, string Destination) Words(AdventurerRecord a)
    {
        var names = Setup!.Names;
        AdventurerGround? ground = null;
        string purpose = LfgPurposes.Hunt, purposeName;
        if (a.Plan.Goal == AdventurerGoals.Bounty && Bounties.FirstOrDefault(b => b.QuestId == a.Plan.Ref) is { } bounty)
        {
            purpose = LfgPurposes.Bounty;
            purposeName = bounty.Title;
            ground = Grounds.Values.FirstOrDefault(g => g.Settlement == bounty.Settlement && g.CreatureId == bounty.CreatureId);
        }
        else
        {
            ground = a.Plan.Ref is { } key ? Grounds.GetValueOrDefault(key) : null;
            purposeName = ground is null ? "game" : names.GetValueOrDefault(ground.CreatureId, ground.CreatureId);
        }
        string destination = "the wilds";
        if (ground is not null && _context.Setup.Combat.Spawns.FirstOrDefault(s => s.Key == ground.SpawnKey) is { } site
            && _context.Setup.Layout.Locations.OrderBy(l => (l.XMm - site.XMm) * (l.XMm - site.XMm) + (l.ZMm - site.ZMm) * (l.ZMm - site.ZMm))
                .ThenBy(l => l.Id, StringComparer.Ordinal).FirstOrDefault() is { } place)
            destination = names.GetValueOrDefault(place.Id, place.Id);
        return (purpose, purposeName, destination);
    }

    private void Set(AdventurerRecord a)
    {
        if (Current is { } state)
            State.SetAdventurers(_owner, state with { Agents = state.Agents.SetItem(a.Key, a) });
    }
}

/// <summary>One answer to who can be hired (WP12b's contract): the adventurers for their own, NPC life for everyone else.</summary>
internal sealed class CombinedHireability : INpcHireability
{
    private readonly AdventurerSystem _adventurers;
    private readonly INpcHireability _residents;

    public CombinedHireability(AdventurerSystem adventurers, INpcHireability residents)
    {
        _adventurers = adventurers;
        _residents = residents;
    }

    public bool IsHireable(string key) => Hireability(key)?.Hireable ?? false;

    public NpcHireView? Hireability(string key) => _adventurers.Hireability(key) ?? _residents.Hireability(key);
}

/// <summary>Setting-outs from more than one source, in tick order: the NPC life simulation's and the adventurers'.</summary>
internal sealed class CombinedSettingOuts : ISettingOutSource
{
    private readonly ImmutableArray<ISettingOutSource> _sources;

    public CombinedSettingOuts(params ISettingOutSource[] sources) => _sources = sources.ToImmutableArray();

    public IEnumerable<SettingOut> Between(long fromTick, long toTick) =>
        _sources.SelectMany(s => s.Between(fromTick, toTick)).OrderBy(s => s.Tick).ThenBy(s => s.Npc).ToList();
}
