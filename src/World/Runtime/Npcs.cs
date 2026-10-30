// UNNAMED World - NPC bodies, the social setup and relationship bookkeeping (SYSTEMS.md S-24, S-26; M4; WP12)
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

    /// <summary>The party's size, wages and wounds (<c>config.party</c>; WP15a); null in a pack where nobody can be hired or join.</summary>
    public PartyRules? Party { get; init; }

    public static SocialSetup Empty { get; } = new(ImmutableSortedDictionary.Create<string, NpcDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, DialogueDefinition>(StringComparer.Ordinal));
}

/// <summary>What an NPC thinks of the player moved on one dimension, for a named reason (S-26: every change is attributed).</summary>
public sealed record RelationshipChanged(string NpcId, string Dimension, int From, int To, string EventKey, long Tick);

/// <summary>An NPC as presentation draws them: who, where, and whether the character is talking to them.</summary>
public sealed record NpcView(string Id, EntityId InstanceId, string Name, Body Body, bool Talking)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public SpaceId Space { get; init; }
    /// <summary>A companion lying downed (M6).</summary>
    public bool Downed { get; init; }

    /// <summary>What their routine has them doing (WP12; <see cref="NpcActivities"/>), for the activity idles; null for an NPC with no routine.</summary>
    public string? Activity { get; init; }

    /// <summary>A role resident (WP12, DN3) rather than a named NPC: presentation dresses them from the role's variants.</summary>
    public bool Resident { get; init; }

    /// <summary>The routine/adventurer's people, for appearance selection; derived, never saved as presentation state.</summary>
    public string? PeopleId { get; init; }

    /// <summary>The role they fill: a named NPC's, or a resident's slot's.</summary>
    public string? Role { get; init; }

    /// <summary>Whether they are walking now (WP12): presentation plays the walk.</summary>
    public bool Moving { get; init; }
}

/// <summary>An NPC's body as the simulation holds it: where they stand, which way they face, and the space they stand in. Saved with their life (WP12).</summary>
internal sealed record NpcState(NpcDefinition Definition, EntityId InstanceId, NpcSite Site, Body Body)
{
    public SpaceId Space { get; init; }
}

/// <summary>To <see cref="RelationshipSystem"/>: move what an NPC thinks of the player on one dimension, for a reason.</summary>
internal sealed record ChangeRelationship(string NpcId, string Dimension, int Delta, string EventKey) : InternalCommand;

/// <summary>To <see cref="RelationshipSystem"/>: someone new holds an NPC's place, and thinks nothing of the player yet.</summary>
internal sealed record ForgetRelationships(string NpcId) : InternalCommand;

/// <summary>
/// Owns: <see cref="StateSlice.Npcs"/> - the NPCs' bodies (S-24) - and <see cref="StateSlice.NpcLife"/> - their lives (WP12). Each named
/// NPC stands where the region puts them until their routine takes them elsewhere; role residents fill their slots. An NPC with a routine
/// walks their day on the navigation grid (<see cref="TickBodies"/>) and lives it hour by hour (<see cref="TickLives"/>), and while the
/// character is away lives it in the abstract (<see cref="Advance"/>). One with none stands where the region puts them, turned the way it
/// says, as before. Everyone turns to face whoever talks to them, and talking pauses their routine. Identity is derived (D-10). A
/// companion's body (M6) is moved by <see cref="CompanionSystem"/> through <see cref="PlaceNpc"/>.
/// </summary>
internal sealed partial class NpcSystem
{
    private const int TurnMdegPerTick = 18_000;   // 360 degrees a second at 20 Hz

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly WorldNavigation _navigation;

    private readonly Func<long, IReadOnlyList<TierObserver>> _observers;
    private readonly Func<MoveIntent> _playerIntent;

    public NpcSystem(SystemContext context, SliceOwner owner, WorldNavigation navigation, Func<long, IReadOnlyList<TierObserver>> observers,
        Func<MoveIntent> playerIntent)
    {
        _context = context;
        _owner = owner;
        _navigation = navigation;
        _observers = observers;
        _playerIntent = playerIntent;
    }

    private RuntimeState State => _context.State;

    /// <summary>A named NPC's instance ID: derived from their definition, so they are the same instance in every world and load.</summary>
    public static EntityId InstanceIdOf(string npcId)
    {
        using var h = new CanonicalHasher();
        return EntityId.Create(EntityKind.Npc, 1, h.Add("unnamed.npc/v1").Add(npcId).FinishBytes().AsSpan(0, 10));
    }

    /// <summary>A role resident's instance ID: derived from their slot and how many held it before them, so each arrival is someone new.</summary>
    public static EntityId ResidentIdOf(string slotId, int generation)
    {
        using var h = new CanonicalHasher();
        return EntityId.Create(EntityKind.Npc, 1, h.Add("unnamed.resident/v1").Add(slotId).Add(generation).FinishBytes().AsSpan(0, 10));
    }

    public void Populate()
    {
        // Every layout's NPCs, each on its OWN ground. The player's space is wrong here: Populate runs after the
        // space is adopted from a save, so a save made inside the ring put all eight exterior NPCs on the ring's
        // floor and buried them under the Ashen Hollow (review 3, B8).
        foreach (var (home, site) in _context.Setup.Sites(l => l.Npcs))
        {
            // A hireling who died in the character's service stays dead (DN9).
            if (!_context.Setup.Social.Npcs.TryGetValue(site.NpcId, out var definition) || State.Fallen.Contains(site.NpcId))
                continue;
            var id = InstanceIdOf(site.NpcId);
            if (!State.World.Registry.Exists(id))
                State.World.Registry.CreateEntity(DefinitionId.Parse(site.NpcId), id);
            long y = _context.Setup.SpaceFor(home.SpaceId).HeightAtMm(site.XMm, site.ZMm);
            State.SetNpc(_owner, new NpcState(definition, id, site, new Body(site.XMm, y, site.ZMm, site.FacingMdeg)) { Space = home.SpaceId });
        }
        PopulateLives();
    }

    /// <summary>A hireling died (DN9): they are gone from the world, their instance with them.</summary>
    public string? Handle(RemoveNpc command)
    {
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        State.RemoveNpc(_owner, command.NpcId);
        if (command.KeepIdentity)
            return null;
        if (State.World.Registry.Exists(npc.InstanceId))
            State.World.Registry.DestroyEntity(npc.InstanceId);
        // Their life ends with them: no needs, no pairs, no job output after. A resident leaves their slot to an arrival (DN4).
        if (State.World.NpcLife is { } life && life.Npcs.TryGetValue(command.NpcId, out var lived))
        {
            if (_context.Setup.NpcLife.Routines.TryGetValue(lived.RoutineId, out var slot) && slot.IsSlot)
            {
                ulong sample = RngChannel.Open(State.World.WorldSeed, RegionCell, "npc_life", $"arrival:{lived.RoutineId}#{lived.Generation + 1}").UInt64(0);
                State.SetNpcLife(_owner, NpcLifeEngine.Depart(life, _context.Setup.NpcLife.Routines, _context.Setup.NpcLife.Rules, command.NpcId,
                    _context.Setup.Clock.Day(command.Tick), sample, out _) ?? life with { Npcs = life.Npcs.Remove(command.NpcId) });
            }
            else
                State.SetNpcLife(_owner, life with { Npcs = life.Npcs.Remove(command.NpcId) });
        }
        Rehire(State.WorldTick);
        return null;
    }

    /// <summary>A body for someone the region does not place (WP13: an adventurer at their lodge), with their own instance.</summary>
    /// <summary>Where a body in the world is headed now (spec 15): its place at the inn, by the hour. The mover walks it there.</summary>
    public string? Handle(SiteNpc command)
    {
        if (!State.Npcs.TryGetValue(command.Key, out var npc))
            return $"{command.Key} has no body";
        if (npc.Site == command.Site)
            return null;
        State.SetNpc(_owner, npc with { Site = command.Site });
        return null;
    }
    public string? Handle(EmbodyNpc command)
    {
        if (State.Fallen.Contains(command.Definition.Id))
            return $"{command.Definition.Name} is dead";
        if (!State.World.Registry.Exists(command.InstanceId))
            State.World.Registry.CreateEntity(DefinitionId.Parse(command.Definition.Id), command.InstanceId);
        var site = command.Site;
        State.SetNpc(_owner, new NpcState(command.Definition, command.InstanceId, site, BodyAt(site.XMm, site.ZMm, site.FacingMdeg))
        {
            // The body is put where it stands, in the LAYOUT's own space id: the state's space may still be the exterior sentinel rather than
            // the region layout's id, and a view compares those ids directly - so a body embodied here carried a space no view matched, and
            // was drawn nowhere. An adventurer at the gate on their departure hour was in the simulation and absent from the gate (spec 15).
            Space = _context.Setup.LayoutFor(State.Space).SpaceId,
        });
        return null;
    }

    /// <summary>
    /// A retiring adventurer takes an open role slot in their settlement (§2.13): the first slot, by ID, whose arrival has not yet come is
    /// theirs - they arrive in it today, under their own name. Refused when none stands open.
    /// </summary>
    public string? Handle(SettleRetiree command)
    {
        long day = Clock.Day(command.Tick);
        if (State.World.NpcLife is not { } life
            || life.Vacancies.Values.Where(v => v.ArrivalDay > day && Life.Routines.TryGetValue(v.SlotId, out var r) && r.Settlement == command.Settlement)
                .OrderBy(v => v.SlotId, StringComparer.Ordinal).FirstOrDefault() is not { } vacancy)
            return $"no role slot stands open in {command.Settlement}";
        State.SetNpcLife(_owner, life with { Vacancies = life.Vacancies.SetItem(vacancy.SlotId, vacancy with { ArrivalDay = day, ArrivingName = command.Name }) });
        return null;
    }

    public string? Handle(PlaceNpc command)
    {
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        State.SetNpc(_owner, npc with { Body = command.Body, Space = command.Space ?? npc.Space });
        return null;
    }

    /// <summary>
    /// One tick: lives cross an hour if one began, bodies with a routine walk theirs (tier A here; B and C through their tier simulations),
    /// and everyone else - and anyone talking - turns: to the speaker, or back to the way their place faces.
    /// </summary>
    public void Tick(long tick)
    {
        TickLives(tick);
        TickBodies(tick, SimulationTier.A);
        WalkAdventurers(tick);
        string? talking = State.Conversation?.NpcId;
        var player = State.Body;
        foreach (var npc in State.Npcs.Values.Where(n => !State.Companions.ContainsKey(n.Definition.Id)))
        {
            bool lives = LifeOf(npc.Definition.Id) is not null;
            if (lives && npc.Definition.Id != talking)
                continue;
            int wanted = npc.Definition.Id == talking
                ? CombatRules.FacingTowards(npc.Body.XMm, npc.Body.ZMm, player.XMm, player.ZMm)
                : npc.Site.FacingMdeg;
            var turned = Turn(npc.Body, wanted);
            if (turned != npc.Body)
                Place(npc, turned);
        }
    }

    public ImmutableArray<NpcView> Views() =>
        State.Npcs.Values.Select(n =>
        {
            var life = LifeOf(n.Definition.Id);
            return new NpcView(n.Definition.Id, n.InstanceId, n.Definition.Name, n.Body, State.Conversation?.NpcId == n.Definition.Id)
            {
                Space = n.Space,
                Downed = State.Companions.TryGetValue(n.Definition.Id, out var companion) && companion.Condition == CompanionCondition.Downed,
                Activity = life?.Activity,
                Resident = _context.Setup.NpcLife.Routines.TryGetValue(n.Definition.Id, out var slot) && slot.IsSlot,
                PeopleId = slot?.PeopleId ?? State.World.Adventurers?.Agents.GetValueOrDefault(n.Definition.Id)?.PeopleId,
                Role = n.Definition.Role,
                Moving = life is { Route.Status: NavRouteStatus.Active, Condition: not NpcCondition.Hired } && !State.Companions.ContainsKey(n.Definition.Id),
            };
        }).ToImmutableArray();

    /// <summary>A body on the walkable surface, including bridges above the terrain.</summary>
    private Body BodyAt(long xMm, long zMm, int facingMdeg) =>
        new(xMm, _context.Setup.Layout.Space.HeightAtMm(xMm, zMm), zMm, facingMdeg);

    /// <summary>A body on the walkable surface of the space it stands in: an interior's watch stands on the interior's floor, not the outside's.</summary>
    private Body BodyAt(SpaceId space, long xMm, long zMm, int facingMdeg) =>
        new(xMm, _context.Setup.SpaceFor(space).HeightAtMm(xMm, zMm), zMm, facingMdeg);

    /// <summary>The body turned towards a facing by at most 18 degrees a tick, as NPCs turn.</summary>
    private static Body Turn(Body body, int towardMdeg)
    {
        int delta = (int)(((long)towardMdeg - body.FacingMdeg + 540_000) % 360_000) - 180_000;
        if (delta == 0)
            return body;
        return body with { FacingMdeg = (int)(((long)body.FacingMdeg + Math.Clamp(delta, -TurnMdegPerTick, TurnMdegPerTick) + 360_000) % 360_000) };
    }

    private void Place(NpcState npc, Body body)
    {
        if (body != npc.Body)
            State.SetNpc(_owner, npc with { Body = body });
    }
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

    /// <summary>A resident's slot taken by a newcomer (WP12): they meet the character as a stranger, whatever the one before thought (owner, 2026-10-17).</summary>
    public void Listen(SimulationEvents events) => events.Listen<NpcArrived>(e => _context.Dispatch(new ForgetRelationships(e.Key)));

    public string? Handle(ForgetRelationships command, long tick)
    {
        if (_context.State.Relationships.GetValueOrDefault(command.NpcId) is not { } values)
            return null;
        foreach (var (dimension, from) in values)
        {
            _context.State.SetRelationship(_owner, command.NpcId, dimension, 0);
            _context.Events.Publish(new RelationshipChanged(command.NpcId, dimension, from, 0, "newcomer", tick));
        }
        return null;
    }
}
