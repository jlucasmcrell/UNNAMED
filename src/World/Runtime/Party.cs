// UNNAMED World - the party at run time: hiring, contracts and wages, roles and work, stances and standing rules, dismissal, grave
// wounds and deaths, and the party's share of the world going on while the character is away
// (SYSTEMS_COMPLETENESS_PLAN §2.15, WP15a; DN9, DN11; the offline catch-up of §2.12)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>
/// Hire someone within a hand's reach for a role and so many days, at the role's wage - the first day's paid now. A hireling can die
/// (DN9): the hire is refused unless the character has been told so and accepts it (<see cref="AcceptsRisk"/>), which the hiring sheet
/// states with <see cref="Simulation.HireTerms"/>.
/// </summary>
public sealed record HireCommand(EntityId Actor, string NpcId, HirelingRole Role, int Days, bool AcceptsRisk) : GameCommand(Actor);

/// <summary>Send a party member away: they go home and live their own life. A hireling's wage already paid is not refunded.</summary>
public sealed record DismissCommand(EntityId Actor, string NpcId) : GameCommand(Actor);

/// <summary>How readily a party member fights: aggressive, defensive or passive.</summary>
public sealed record SetStanceCommand(EntityId Actor, string NpcId, CompanionStance Stance) : GameCommand(Actor);

/// <summary>A party member's standing rules.</summary>
public sealed record SetTacticsCommand(EntityId Actor, string NpcId, PartyTactics Tactics) : GameCommand(Actor);

/// <summary>
/// Set a hireling to work (WP15a): a gatherer at a node (its key), a crafter at a station (its key) on a recipe the character knows.
/// They wait there and work each day, the character away or not. <see cref="SiteKey"/> null: no work; they wait where they are.
/// </summary>
public sealed record AssignWorkCommand(EntityId Actor, string NpcId, string? SiteKey, string? RecipeId = null) : GameCommand(Actor);

/// <summary>Hand a crafter materials from the pack, within a hand's reach of them: what they work from.</summary>
public sealed record SupplyHirelingCommand(EntityId Actor, string NpcId, string ItemId, int Count) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>Someone was hired: on what terms, and that they were told they could die (DN9).</summary>
public sealed record HirelingHired(string NpcId, HirelingRole Role, int Days, long DailyWage, bool CanDie, long Tick);

/// <summary>Someone left the party alive: <c>dismissed</c>, or a contract <c>ended</c> (served in full) or <c>lapsed</c> (unpaid past the grace).</summary>
public sealed record PartyMemberLeft(string NpcId, string Reason, long Tick);

/// <summary>A hireling died in the character's service (DN9). They do not come back.</summary>
public sealed record HirelingDied(string NpcId, long Tick);

/// <summary>A companion fell and is gravely wounded (DN9): home, recovering until <see cref="UntilTick"/>.</summary>
public sealed record CompanionGravelyWounded(string NpcId, long UntilTick, long Tick);

/// <summary>A companion recovered from a grave wound: they can be asked along again, at home.</summary>
public sealed record CompanionRecovered(string NpcId, long Tick);

/// <summary>A hireling's wage, paid at midnight (<see cref="Paid"/>), or owed (<see cref="Paid"/> 0, <see cref="UnpaidDays"/> counting).</summary>
public sealed record HirelingWage(string NpcId, long Paid, int UnpaidDays, long Tick);

/// <summary>A gatherer's or a crafter's day of work, delivered to the character (into the pack, or at their feet when it will not go in).</summary>
public sealed record HirelingDelivered(string NpcId, string ItemId, int Count, long Tick);

public sealed record PartyStanceChanged(string NpcId, CompanionStance Stance, long Tick);

public sealed record TacticsChanged(string NpcId, PartyTactics Tactics, long Tick);

public sealed record WorkAssigned(string NpcId, string? SiteKey, string? RecipeId, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>
/// What hiring someone would mean, as the hiring sheet states it before the character agrees (DN9, DN11): the role, the days, the daily
/// wage and what the whole contract costs, the loot rule, and that the hireling can die. <see cref="Refusal"/> is why the hire would be
/// refused now, or null.
/// </summary>
public sealed record HireTerms(string NpcId, HirelingRole Role, int Days, long DailyWage, long Total, string LootRule, bool CanDie, string? Refusal);

/// <summary>A companion recovering at home from a grave wound, and the tick they are well again.</summary>
public sealed record RecoveryView(string NpcId, string Name, long UntilTick);

/// <summary>The kinds of <see cref="AdvanceLine"/> the party writes while the character is away. Presentation words them.</summary>
public static class PartyAdvanceKinds
{
    /// <summary>Wages paid to a hireling over the absence; <see cref="AdvanceLine.Amount"/> is the coin.</summary>
    public const string WagesPaid = "hireling.wages";

    /// <summary>A hireling's work delivered; <see cref="AdvanceLine.Ref"/> is <c>npc|item</c>, <see cref="AdvanceLine.Amount"/> the count.</summary>
    public const string Delivered = "hireling.delivered";

    /// <summary>A hireling left: <see cref="AdvanceLine.Ref"/> is <c>npc|reason</c> (ended or lapsed).</summary>
    public const string Left = "hireling.left";

    public const string Died = "hireling.died";

    public const string Wounded = "companion.wounded";

    public const string Recovered = "companion.recovered";
}

/// <summary>What porters add to what the character can carry: each on their feet and following adds their role's bonus (D4's laden band).</summary>
internal static class PartyCarry
{
    public static long BonusGrams(RuntimeState state, PartyRules? rules) =>
        rules is null || !rules.Roles.TryGetValue(HirelingRole.Porter, out var porter)
            ? 0
            : state.Companions.Values.Count(c => c.Contract is { Role: HirelingRole.Porter } && c.Condition == CompanionCondition.Up && c.Order == CompanionOrder.Follow)
              * porter.CarryBonusGrams;
}

// ── internal commands ───────────────────────────────────────────────────────

/// <summary>To <see cref="CompanionSystem"/>: the speaker hires on (a conversation's <c>hire_hireling</c>, whose reply states the risk).</summary>
internal sealed record Hire(string NpcId, HirelingRole Role, int Days) : InternalCommand;

/// <summary>To <see cref="CompanionSystem"/>: the speaker leaves the party (a conversation's <c>dismiss_companion</c>).</summary>
internal sealed record Dismiss(string NpcId) : InternalCommand;

/// <summary>To <see cref="NpcSystem"/>: an NPC is gone from the world - a hireling who died (DN9).</summary>
internal sealed record RemoveNpc(string NpcId) : InternalCommand;

// ── the system's party half ─────────────────────────────────────────────────

internal sealed partial class CompanionSystem
{
    private PartyRules? Rules => _context.Setup.Social.Party;

    /// <summary>Why someone cannot join the party now, or null: the party is full, or they are still recovering from a grave wound.</summary>
    private string? Joinable(NpcDefinition definition)
    {
        if (State.Recovering.ContainsKey(definition.Id))
            return $"{definition.Name} is still recovering";
        int most = Rules?.MaxMembers ?? int.MaxValue;
        return State.Companions.Count >= most ? $"your party is full ({most} with you)" : null;
    }

    // ── hiring ──────────────────────────────────────────────────────────────

    public string? Handle(HireCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        if (!_context.InTalkReach(npc.Body))
            return $"{npc.Definition.Name} is out of reach";
        if (!command.AcceptsRisk)
            return "a hireling can die in your service: the hire needs that risk accepted";
        return Hire(command.NpcId, command.Role, command.Days, tick);
    }

    /// <summary>A conversation's hire: its reply stated the risk (the content attests it), and the speaker is within reach.</summary>
    public string? Handle(Hire command, long tick) => Hire(command.NpcId, command.Role, command.Days, tick);

    /// <summary>The terms of a hire, and why it would be refused now (null: it would not).</summary>
    public HireTerms Terms(string npcId, HirelingRole role, int days)
    {
        long wage = Rules?.Roles.GetValueOrDefault(role)?.DailyWage ?? 0;
        return new HireTerms(npcId, role, days, wage, wage * Math.Max(0, days), PartyRules.WageOnly, CanDie: true, Refusal(npcId, role, days));
    }

    private string? Refusal(string npcId, HirelingRole role, int days)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc))
            return $"there is no one called {npcId} here";
        if (State.Companions.ContainsKey(npcId))
            return $"{npc.Definition.Name} is already with you";
        if (Rules is not { } rules || Tuning is null || npc.Definition.Hireling is not { } profile || !profile.Roles.Contains(role) || AttackOf(profile.Body) is null)
            return $"{npc.Definition.Name} does not hire on as a {PartyKeys.Key(role)}";
        if (days < rules.ContractDaysMin || days > rules.ContractDaysMax)
            return $"a contract runs {rules.ContractDaysMin} to {rules.ContractDaysMax} days";
        if (Joinable(npc.Definition) is { } refused)
            return refused;
        long wage = rules.Roles[role].DailyWage;
        return State.Currency < wage ? $"not enough coin: the first day's wage is {wage}, {State.Currency} carried" : null;
    }

    private string? Hire(string npcId, HirelingRole role, int days, long tick)
    {
        if (Refusal(npcId, role, days) is { } refused)
            return refused;
        var rules = Rules!;
        var terms = rules.Roles[role];
        var profile = State.Npcs[npcId].Definition.Hireling!.Body;
        if (_context.Dispatch(new SpendCurrency(terms.DailyWage)) is { } unpaid)
            return unpaid;
        State.SetCompanion(_owner, new CompanionState(npcId, profile, AttackOf(profile)!, CompanionOrder.Follow, CompanionCondition.Up, profile.MaxHealth)
        {
            Membership = PartyMembership.Hireling,
            Stance = terms.Stance,
            Contract = new HirelingContract(role, days, terms.DailyWage, PartyRules.WageOnly) { Purse = terms.DailyWage },
        });
        _context.Events.Publish(new HirelingHired(npcId, role, days, terms.DailyWage, CanDie: true, tick));
        _context.Events.Publish(new CompanionRecruited(npcId, tick));
        return null;
    }

    // ── leaving ─────────────────────────────────────────────────────────────

    public string? Handle(DismissCommand command, long tick) =>
        command.Actor != _player ? $"unknown actor {command.Actor}" : Dismiss(command.NpcId, tick);

    public string? Handle(Dismiss command, long tick) => Dismiss(command.NpcId, tick);

    private string? Dismiss(string npcId, long tick)
    {
        if (!State.Companions.TryGetValue(npcId, out var c))
            return $"{Name(npcId)} is not with you";
        if (c.Condition == CompanionCondition.Downed)
            return $"{Name(npcId)} is down";
        Leave(c, "dismissed", tick, null);
        return null;
    }

    /// <summary>Someone leaves the party alive: they go home, and live their own life (§2.15; the life simulation takes them up with WP12).</summary>
    private void Leave(CompanionState c, string reason, long tick, AdvanceLog? log)
    {
        State.RemoveCompanion(_owner, c.NpcId);
        if (State.Npcs.TryGetValue(c.NpcId, out var npc))
            Place(npc, Home(npc));
        _context.Events.Publish(new PartyMemberLeft(c.NpcId, reason, tick));
        log?.Add(new AdvanceLine(tick, PartyAdvanceKinds.Left, $"{c.NpcId}|{reason}"));
    }

    /// <summary>Where someone lives: their place in the region's layout.</summary>
    private Body Home(NpcState npc) =>
        new(npc.Site.XMm, _context.Setup.SpaceFor(_context.Setup.SpaceOfNpc(npc.Site.NpcId)).Terrain.HeightAtMm(npc.Site.XMm, npc.Site.ZMm), npc.Site.ZMm, npc.Site.FacingMdeg);

    /// <summary>How many days a grave wound keeps a companion at home: rolled from the world, who and when, so a replay or a load agrees.</summary>
    private long WoundDays(string npcId, long tick)
    {
        if (Rules is not { } rules)
            return 0;
        var channel = RngChannel.Open(State.World.WorldSeed, CellKey.OfWorld(0, 0), "party", $"wound:{npcId}@{tick}");
        return channel.Int(0, rules.GraveWoundDaysMin, rules.GraveWoundDaysMax + 1);
    }

    // ── stance, tactics, work ───────────────────────────────────────────────

    public string? Handle(SetStanceCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!State.Companions.TryGetValue(command.NpcId, out var c))
            return $"{Name(command.NpcId)} is not with you";
        if (!Enum.IsDefined(command.Stance))
            return "no such stance";
        if (c.Stance == command.Stance)
            return null;
        State.SetCompanion(_owner, c with { Stance = command.Stance, TargetKey = command.Stance == CompanionStance.Passive ? null : c.TargetKey });
        _context.Events.Publish(new PartyStanceChanged(command.NpcId, command.Stance, tick));
        return null;
    }

    public string? Handle(SetTacticsCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!State.Companions.TryGetValue(command.NpcId, out var c))
            return $"{Name(command.NpcId)} is not with you";
        if (command.Tactics is null)
            return "no tactics given";
        if (c.Tactics == command.Tactics)
            return null;
        State.SetCompanion(_owner, c with { Tactics = command.Tactics });
        _context.Events.Publish(new TacticsChanged(command.NpcId, command.Tactics, tick));
        return null;
    }

    public string? Handle(AssignWorkCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!State.Companions.TryGetValue(command.NpcId, out var c) || c.Contract is not { } contract)
            return $"{Name(command.NpcId)} is not in your hire";
        if (c.Condition == CompanionCondition.Downed)
            return $"{Name(command.NpcId)} is down";
        if (command.SiteKey is null)
        {
            State.SetCompanion(_owner, c with { Contract = contract with { WorkSite = null, RecipeId = null } });
            _context.Events.Publish(new WorkAssigned(command.NpcId, null, null, tick));
            return null;
        }
        (long X, long Z) site;
        switch (contract.Role)
        {
            case HirelingRole.Gatherer:
                if (Node(command.SiteKey) is not { } node)
                    return $"there is no node '{command.SiteKey}'";
                if (command.RecipeId is not null)
                    return "a gatherer works a node, not a recipe";
                site = (node.Site.XMm, node.Site.ZMm);
                break;
            case HirelingRole.Crafter:
                if (_context.Setup.Sites(l => l.Stations).FirstOrDefault(p => p.Site.Key == command.SiteKey) is not { Site: { } station })
                    return $"there is no station '{command.SiteKey}'";
                if (command.RecipeId is null || !_context.Setup.Crafting.Recipes.TryGetValue(command.RecipeId, out var recipe))
                    return "a crafter needs a recipe to work";
                if (recipe.StationKind != station.Kind)
                    return $"{command.RecipeId} is not worked at a {station.Kind}";
                if (!ProgressionEngine.Knows(State.Progression, recipe.Id))
                    return $"you do not know {recipe.Id} to show them";
                site = (station.XMm, station.ZMm);
                break;
            default:
                return $"a {PartyKeys.Key(contract.Role)} is not set to work at a place";
        }
        // They go and stand by the work (a walk there arrives with WP12's errand mover; until then they are put down beside it).
        var npc = State.Npcs[c.NpcId];
        var siteBody = new Body(site.X, 0, site.Z, npc.Body.FacingMdeg);
        var at = Ring(siteBody, 0, 1_500, Obstacles(c.NpcId)) ?? site;
        Place(npc, new Body(at.X, _context.Setup.SpaceFor(_context.Setup.SpaceOfNpc(npc.Site.NpcId)).Terrain.HeightAtMm(at.X, at.Z), at.Z, CombatRules.FacingTowards(at.X, at.Z, site.X, site.Z)));
        State.SetCompanion(_owner, c with
        {
            Order = CompanionOrder.Wait,
            Trail = ImmutableArray<TrailMark>.Empty,
            TargetKey = null,
            StuckTicks = 0,
            Contract = contract with { WorkSite = command.SiteKey, RecipeId = command.RecipeId },
        });
        _context.Events.Publish(new WorkAssigned(command.NpcId, command.SiteKey, command.RecipeId, tick));
        return null;
    }

    public string? Handle(SupplyHirelingCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!State.Companions.TryGetValue(command.NpcId, out var c) || c.Contract is not { Role: HirelingRole.Crafter } contract)
            return $"{Name(command.NpcId)} is not a crafter in your hire";
        if (!_context.InTalkReach(State.Npcs[c.NpcId].Body))
            return $"{Name(command.NpcId)} is out of reach";
        if (command.Count < 1)
            return "hand over at least one";
        if (_context.Dispatch(new ConsumeItem(command.ItemId, command.Count)) is { } refused)
            return refused;
        State.SetCompanion(_owner, c with
        {
            Contract = contract with { Supply = contract.Supply.SetItem(command.ItemId, contract.Supply.GetValueOrDefault(command.ItemId) + command.Count) },
        });
        return null;
    }

    private (string Key, CellKey Cell, NodeSite Site)? Node(string key) =>
        _context.Setup.Sites(l => l.Nodes).Select(pair =>
        {
            var (home, site) = pair;
            var cell = CellKey.OfInterior(home.SpaceId, site.XMm / 1000.0, site.ZMm / 1000.0);
            return (Key: Keys.NodeKey(cell, site.Name, 0), Cell: cell, Site: site);
        }).Where(n => n.Key == key).Cast<(string, CellKey, NodeSite)?>().FirstOrDefault();

    // ── the party's day ─────────────────────────────────────────────────────

    /// <summary>
    /// A midnight (WP15a), the same online and away: each hireling, in NPC order, delivers the day's work, serves the day, and is paid the
    /// next day's wage from the character's purse - or, with too little in it, owes it; past the grace, or with the days served, they leave.
    /// </summary>
    private void Midnight(long tick, AdvanceLog? log, Dictionary<string, long>? wages = null)
    {
        var rules = Rules;
        foreach (string id in State.Companions.Keys.ToList())
        {
            if (State.Companions[id] is not { Contract: { } contract } c)
                continue;
            if (contract.WorkSite is not null)
                c = Work(c, contract, tick, log);
            var (next, paid, ends) = PartyRulesOf.Midnight(c.Contract!, State.Currency, rules?.GraceDays ?? 0);
            if (paid > 0 && _context.Dispatch(new SpendCurrency(paid)) is not null)
                (next, paid, ends) = PartyRulesOf.Midnight(c.Contract!, 0, rules?.GraceDays ?? 0);
            c = c with { Contract = next };
            if (ends is null)
            {
                State.SetCompanion(_owner, c);
                _context.Events.Publish(new HirelingWage(id, paid, next.UnpaidDays, tick));
                if (wages is not null && paid > 0)
                    wages[id] = wages.GetValueOrDefault(id) + paid;
            }
            else
                Leave(c, ends, tick, log);
        }
    }

    /// <summary>A day's work at the site they were set to: a gatherer's harvests from their node, a crafter's recipe from their supply.</summary>
    private CompanionState Work(CompanionState c, HirelingContract contract, long tick, AdvanceLog? log)
    {
        var terms = Rules?.Roles.GetValueOrDefault(contract.Role);
        if (terms is null || c.Condition != CompanionCondition.Up)
            return c;
        long day = _context.Setup.Clock.Day(tick);
        switch (contract.Role)
        {
            case HirelingRole.Gatherer when Node(contract.WorkSite!) is { } node && _context.Setup.Crafting.Nodes.TryGetValue(node.Site.NodeDefId, out var definition):
            {
                var channel = RngChannel.Open(State.World.WorldSeed, node.Cell, "party", $"gather:{c.NpcId}@{day}");
                int count = 0;
                for (uint i = 0; i < terms.YieldsPerDay; i++)
                    count += channel.Int(i, definition.YieldMin, definition.YieldMax + 1);
                Deliver(c.NpcId, definition.ItemId, count, tick, log);
                return c;
            }
            case HirelingRole.Crafter when contract.RecipeId is { } recipeId && _context.Setup.Crafting.Recipes.TryGetValue(recipeId, out var recipe):
            {
                int times = recipe.Inputs.Aggregate(terms.CraftsPerDay, (most, input) => Math.Min(most, contract.Supply.GetValueOrDefault(input.ItemId) / Math.Max(1, input.Count)));
                if (times <= 0)
                    return c;
                var supply = contract.Supply;
                foreach (var input in recipe.Inputs)
                {
                    int left = supply[input.ItemId] - input.Count * times;
                    supply = left > 0 ? supply.SetItem(input.ItemId, left) : supply.Remove(input.ItemId);
                }
                Deliver(c.NpcId, recipe.OutputItemId, recipe.OutputCount * times, tick, log);
                return c with { Contract = contract with { Supply = supply } };
            }
            default:
                return c;
        }
    }

    /// <summary>Work delivered to the character: into the pack, or at their feet when it will not go in (the storage chest arrives with WP8).</summary>
    private void Deliver(string npcId, string itemId, int count, long tick, AdvanceLog? log)
    {
        if (count <= 0)
            return;
        _context.Dispatch(new GrantItem(itemId, count, Quality.Standard) { SourceRef = npcId });
        _context.Events.Publish(new HirelingDelivered(npcId, itemId, count, tick));
        log?.Add(new AdvanceLine(tick, PartyAdvanceKinds.Delivered, $"{npcId}|{itemId}", count));
    }

    /// <summary>Companions whose grave wound has healed by <paramref name="tick"/> are well again.</summary>
    private void Recover(long tick, AdvanceLog? log)
    {
        foreach (var (id, until) in State.Recovering.Where(r => r.Value <= tick).ToList())
        {
            State.SetRecovering(_owner, id, null);
            _context.Events.Publish(new CompanionRecovered(id, until));
            log?.Add(new AdvanceLine(until, PartyAdvanceKinds.Recovered, id));
        }
    }

    /// <summary>Party members downed with no hand, whose revive window has run out by <paramref name="tick"/>, fall - each at the tick theirs did.</summary>
    private void FallsDue(long tick, AdvanceLog? log)
    {
        var window = Tuning?.ReviveWindowTicks ?? 0;
        foreach (var c in State.Companions.Values.Where(c => c.Condition == CompanionCondition.Downed && c.DownedTick + window <= tick)
                     .OrderBy(c => c.DownedTick).ThenBy(c => c.NpcId, StringComparer.Ordinal).ToList())
        {
            long at = c.DownedTick + window;
            if (State.Npcs.TryGetValue(c.NpcId, out var npc))
                Fall(c, npc, at);
            else
                State.RemoveCompanion(_owner, c.NpcId);
            log?.Add(new AdvanceLine(at, c.Membership == PartyMembership.Hireling ? PartyAdvanceKinds.Died : PartyAdvanceKinds.Wounded, c.NpcId));
        }
    }

    /// <summary>
    /// The party while the character is away (§2.12, §2.15): stepped at the world's midnights, absolute ticks, so any split of an
    /// absence visits the same ones (T-22). Before each, those whose revive window ran out fall; at each, hirelings work and draw wages,
    /// or leave; after the last, the rest fall and the healed recover. Those on their feet mend as they would have, a little every second
    /// out of a fight - closed-form, so it splits too.
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        if (toTick <= fromTick)
            return;
        Mend(fromTick, toTick);
        var clock = _context.Setup.Clock;
        var wages = new Dictionary<string, long>(StringComparer.Ordinal);
        for (long day = clock.Day(fromTick) + 1; day <= clock.Day(toTick); day++)
        {
            long midnight = day * clock.TicksPerDay - clock.StartTicks;
            FallsDue(midnight, log);
            Midnight(midnight, log, wages);
        }
        FallsDue(toTick, log);
        Recover(toTick, log);
        foreach (var (id, paid) in wages)
            log.Add(new AdvanceLine(toTick, PartyAdvanceKinds.WagesPaid, id, paid));
    }

    /// <summary>Every second boundary in (from, to] out of a fight long enough mends a member on their feet: what <see cref="Mend(CompanionState, long)"/> does tick by tick.</summary>
    private void Mend(long fromTick, long toTick)
    {
        if (Tuning is not { } tuning || tuning.RegenPerSecond == 0)
            return;
        long perSecond = Math.Max(1, 1000 / TickMs);
        foreach (var c in State.Companions.Values.Where(c => c.Condition == CompanionCondition.Up && c.Health < c.Profile.MaxHealth).ToList())
        {
            long after = Math.Max(fromTick, c.LastCombatTick + tuning.RegenDelayTicks - 1);
            long seconds = toTick > after ? toTick / perSecond - after / perSecond : 0;
            if (seconds > 0)
                State.SetCompanion(_owner, c with { Health = (int)Math.Min(c.Profile.MaxHealth, c.Health + seconds * tuning.RegenPerSecond) });
        }
    }

    public ImmutableArray<RecoveryView> Recoveries() =>
        State.Recovering.Select(r => new RecoveryView(r.Key, _context.Setup.Social.Npcs.TryGetValue(r.Key, out var d) ? d.Name : r.Key, r.Value)).ToImmutableArray();
}
