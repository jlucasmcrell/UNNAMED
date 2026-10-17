// UNNAMED World - NPC bodies, the social setup and relationship bookkeeping (SYSTEMS.md S-24, S-26; M4)
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
    /// <summary>A companion lying downed (M6).</summary>
    public bool Downed { get; init; }
}

/// <summary>An NPC's body as the simulation holds it: where they stand and which way they face. Transient in Phase 1.</summary>
internal sealed record NpcState(NpcDefinition Definition, EntityId InstanceId, NpcSite Site, Body Body);

/// <summary>To <see cref="RelationshipSystem"/>: move what an NPC thinks of the player on one dimension, for a reason.</summary>
internal sealed record ChangeRelationship(string NpcId, string Dimension, int Delta, string EventKey) : InternalCommand;

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
            // A hireling who died in the character's service stays dead (DN9).
            if (!_context.Setup.Social.Npcs.TryGetValue(site.NpcId, out var definition) || State.Fallen.Contains(site.NpcId))
                continue;
            var id = InstanceIdOf(site.NpcId);
            if (!State.World.Registry.Exists(id))
                State.World.Registry.CreateEntity(DefinitionId.Parse(site.NpcId), id);
            long y = _context.Setup.Layout.Space.Terrain.HeightAtMm(site.XMm, site.ZMm);
            State.SetNpc(_owner, new NpcState(definition, id, site, new Body(site.XMm, y, site.ZMm, site.FacingMdeg)));
        }
    }

    /// <summary>A hireling died (DN9): they are gone from the world.</summary>
    public string? Handle(RemoveNpc command)
    {
        if (!State.Npcs.ContainsKey(command.NpcId))
            return $"there is no one called {command.NpcId} here";
        State.RemoveNpc(_owner, command.NpcId);
        return null;
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
