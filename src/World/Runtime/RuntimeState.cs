// UNNAMED World - authoritative runtime state and who may write it (ARCHITECTURE.md §5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// The typed slices of authoritative runtime state. Every slice has exactly one owning system, claimed at
/// composition; a write by any other system is refused (ARCHITECTURE.md §5 "every component key is claimed
/// by exactly one system").
/// </summary>
public enum StateSlice
{
    /// <summary><c>world_tick</c> (S-04, clock half).</summary>
    Clock,

    /// <summary>The player's position and facing (ARCHITECTURE.md §5: "Position (actors) - Movement system").</summary>
    PlayerBody,

    /// <summary>
    /// Which space the player is in, and the portal they came in by (M8 D10). Owned with <see cref="PlayerBody"/> by the
    /// space system: where a body is includes which space it is in, and the two are never written apart.
    /// </summary>
    PlayerSpace,

    /// <summary>The progression record (level, attributes, skills, techniques, pools, guards).</summary>
    PlayerProgression,

    /// <summary>Discovered-location records (S-30's Phase-1 subset: discovery credit), and the stretches of road walked (schema 15).</summary>
    Discoveries,

    /// <summary>Cell-level <c>world.*</c> flags in the sparse world delta.</summary>
    WorldFlags,

    /// <summary>Per-cell simulation tier (S-21). Transient: derived from position, never saved.</summary>
    CellTiers,

    /// <summary>The player's carried stacks and purse (S-14: "sole writer of item placement").</summary>
    PlayerInventory,

    /// <summary>The player's equipment slots (S-15).</summary>
    PlayerEquipment,

    /// <summary>Items lying in the world and the contents of changed world containers (S-14).</summary>
    WorldItems,

    /// <summary>The player's combat state: attacking, guarding, dodging, staggered (S-12). Transient: a load starts at rest.</summary>
    Combat,

    /// <summary>Spawners' creatures and their records in the world delta (S-23, S-31; M3d). A creature's mind is transient.</summary>
    Creatures,

    /// <summary>Status effects on every combatant (S-11). The player's are saved (schema 7).</summary>
    Effects,

    /// <summary>The harvest records of the region's resource nodes in the world delta (S-19, PERSISTENCE.md §5.5; M3f).</summary>
    Nodes,

    /// <summary>The named NPCs' bodies (S-24; M4). Transient in Phase 1: identity is derived and nothing moves them.</summary>
    Npcs,

    /// <summary>What each NPC thinks of the player, per dimension (S-26; M4). Saved with the player (schema 10).</summary>
    Relationships,

    /// <summary>The lines of each conversation the player has heard (saved, schema 10), and the conversation open now (S-28; M4).</summary>
    Conversations,

    /// <summary>Every quest the player has started, with its objectives (S-29; M5). Saved with the player (schema 11).</summary>
    Quests,

    /// <summary>The companions the player has recruited (S-25; M6). Saved with the player (schema 12); a companion's body is an NPC's.</summary>
    Companions,

    /// <summary>The Waystones the player knows and the one they chose to return to (S-38). Saved with the player (schema 15).</summary>
    Waystones,
}

/// <summary>A system's proof of which slices it owns. Only composition creates one.</summary>
internal sealed class SliceOwner
{
    internal SliceOwner(string system, ImmutableHashSet<StateSlice> slices)
    {
        System = system;
        Slices = slices;
    }

    public string System { get; }
    public ImmutableHashSet<StateSlice> Slices { get; }
}

/// <summary>
/// The mutable state behind a <see cref="Simulation"/>. It holds records and makes no decisions: every write
/// names its owner, and a writer that does not own the slice is a bug that fails loudly.
/// </summary>
internal sealed class RuntimeState
{
    private readonly Dictionary<StateSlice, string> _owners = new();

    public RuntimeState(WorldDelta world, long worldTick, Body body, PlayerRecord player, SpaceId startingSpace = default)
    {
        World = world;
        WorldTick = worldTick;
        Body = body;
        // The space this session opens in is the region's own (M8 D10). A region that is part of the world is
        // the exterior, which is the default; booting an interior region - as a harness or a survey does -
        // opens in that interior. Without this the body stands in the interior's metres while the simulation
        // believes it is outside, which is the collision the space identity exists to prevent, in reverse.
        Space = startingSpace;
        Posture = player.Posture;
        Progression = player.Progression;
        Discoveries = player.Discoveries.ToImmutableSortedDictionary(d => d.LocationId, d => d, StringComparer.Ordinal);
        Inventory = player.Inventory;
        Equipment = player.Equipment;
        Currency = player.Currency;
        Relationships = player.Relationships
            .GroupBy(r => r.NpcId, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(g => g.Key, g => g.ToImmutableSortedDictionary(r => r.Dimension, r => r.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
        Conversations = player.Conversations.ToImmutableSortedDictionary(c => c.DialogueId, c => c.Heard.ToImmutableSortedSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Quests = player.Quests.ToImmutableSortedDictionary(q => q.QuestId, q => q, StringComparer.Ordinal);
        RoadsWalked = player.RoadsWalked.ToImmutableSortedSet(StringComparer.Ordinal);
        KnownStones = player.Waystones.ToImmutableSortedDictionary(w => w.StoneKey, w => w, StringComparer.Ordinal);
        ReturnPoint = player.ReturnPoint;
    }

    public WorldDelta World { get; }
    public long WorldTick { get; private set; }
    public Body Body { get; private set; }
    public Posture Posture { get; private set; }
    public CharacterProgression Progression { get; private set; }
    public ImmutableSortedDictionary<string, DiscoveryRecord> Discoveries { get; private set; }
    public ImmutableSortedSet<string> RoadsWalked { get; private set; }
    public ImmutableSortedDictionary<string, WaystoneRecord> KnownStones { get; private set; }
    public string? ReturnPoint { get; private set; }
    public ImmutableSortedDictionary<string, SimulationTier> Tiers { get; private set; } =
        ImmutableSortedDictionary.Create<string, SimulationTier>(StringComparer.Ordinal);
    public ImmutableArray<InventoryEntry> Inventory { get; private set; }
    public ImmutableSortedDictionary<EquipSlot, EntityId> Equipment { get; private set; }
    public long Currency { get; private set; }
    public PlayerCombat PlayerCombat { get; private set; } = PlayerCombat.Rested;
    public ImmutableSortedDictionary<string, CreatureState> Creatures { get; private set; } =
        ImmutableSortedDictionary.Create<string, CreatureState>(StringComparer.Ordinal);
    public ImmutableSortedDictionary<EntityId, ImmutableArray<ActiveEffect>> Effects { get; private set; } =
        ImmutableSortedDictionary<EntityId, ImmutableArray<ActiveEffect>>.Empty;
    public ImmutableSortedDictionary<string, NpcState> Npcs { get; private set; } =
        ImmutableSortedDictionary.Create<string, NpcState>(StringComparer.Ordinal);
    public ImmutableSortedDictionary<string, ImmutableSortedDictionary<string, int>> Relationships { get; private set; }
    public ImmutableSortedDictionary<string, ImmutableSortedSet<string>> Conversations { get; private set; }
    public Conversation? Conversation { get; private set; }
    public ImmutableSortedDictionary<string, QuestState> Quests { get; private set; }
    public ImmutableSortedDictionary<string, CompanionState> Companions { get; private set; } =
        ImmutableSortedDictionary.Create<string, CompanionState>(StringComparer.Ordinal);

    /// <summary>The space the player is in. The exterior until the space system moves them (M8 D10).</summary>
    public SpaceId Space { get; private set; } = SpaceId.Exterior;

    /// <summary>The portal the player came in by, so leaving returns them to the anchor they used.</summary>
    public SpaceTraversal Traversal { get; private set; } = SpaceTraversal.None;

    public IReadOnlyDictionary<StateSlice, string> Owners => _owners;

    public SliceOwner Claim(string system, params StateSlice[] slices)
    {
        foreach (var slice in slices)
        {
            if (_owners.TryGetValue(slice, out string? owner))
                throw new InvalidOperationException($"State slice {slice} is claimed by both {owner} and {system}");
            _owners[slice] = system;
        }
        return new SliceOwner(system, slices.ToImmutableHashSet());
    }

    /// <summary>Composition check: a slice nobody owns is a startup error, not a silent success.</summary>
    public void RequireEverySliceOwned()
    {
        var unowned = Enum.GetValues<StateSlice>().Where(s => !_owners.ContainsKey(s)).ToList();
        if (unowned.Count > 0)
            throw new InvalidOperationException("State slices without an owning system: " + string.Join(", ", unowned));
    }

    public void AdvanceClock(SliceOwner owner)
    {
        Require(owner, StateSlice.Clock);
        WorldTick++;
    }

    public void SetBody(SliceOwner owner, Body body)
    {
        Require(owner, StateSlice.PlayerBody);
        Body = body;
    }

    /// <summary>Standing or crouched, and in the air or not: part of the body's slice (the owner's M6 playtest).</summary>
    public void SetPosture(SliceOwner owner, Posture posture)
    {
        Require(owner, StateSlice.PlayerBody);
        Posture = posture;
    }

    /// <summary>
    /// Move the player into a space and say which portal they came in by; the exterior with
    /// <see cref="SpaceTraversal.None"/> is going back outside.
    /// </summary>
    /// <remarks>
    /// The invariant is one-way: <em>holding a traversal implies being inside</em>. The exterior can never hold
    /// one - there is nothing out there to have come in by - but a space may be held with none, because a body
    /// can be inside without having entered through a portal: a harness or a survey opens an interior region
    /// directly, and a save can be written inside. Such a body can look around and cannot leave, which
    /// <see cref="SpaceSystem.Leave"/> refuses by name rather than by throwing.
    /// </remarks>
    public void SetSpace(SliceOwner owner, SpaceId space, SpaceTraversal traversal)
    {
        Require(owner, StateSlice.PlayerSpace);
        if (space.IsExterior && !traversal.IsNone)
            throw new InvalidOperationException("Left to the exterior while still holding a portal traversal");
        Space = space;
        Traversal = traversal;
    }

    public void SetProgression(SliceOwner owner, CharacterProgression progression)
    {
        Require(owner, StateSlice.PlayerProgression);
        Progression = progression.Validate();
    }

    public void AddDiscovery(SliceOwner owner, DiscoveryRecord record)
    {
        Require(owner, StateSlice.Discoveries);
        if (Discoveries.ContainsKey(record.LocationId))
            throw new InvalidOperationException($"{record.LocationId} is already discovered");
        Discoveries = Discoveries.Add(record.LocationId, record);
    }

    public void WalkRoad(SliceOwner owner, string stretch)
    {
        Require(owner, StateSlice.Discoveries);
        RoadsWalked = RoadsWalked.Add(stretch);
    }

    public void KnowStone(SliceOwner owner, WaystoneRecord record)
    {
        Require(owner, StateSlice.Waystones);
        if (KnownStones.ContainsKey(record.StoneKey))
            throw new InvalidOperationException($"{record.StoneKey} is already known");
        KnownStones = KnownStones.Add(record.StoneKey, record);
    }

    public void SetReturnPoint(SliceOwner owner, string stoneKey)
    {
        Require(owner, StateSlice.Waystones);
        ReturnPoint = KnownStones.ContainsKey(stoneKey) ? stoneKey : throw new InvalidOperationException($"{stoneKey} is not a known stone");
    }

    /// <summary>World time passing at once (Waystone travel): the clock moves on by whole ticks.</summary>
    public void AdvanceClock(SliceOwner owner, long ticks)
    {
        Require(owner, StateSlice.Clock);
        WorldTick += ticks >= 0 ? ticks : throw new InvalidOperationException("The clock never runs backwards");
    }

    public void SetFlag(SliceOwner owner, CellKey cell, string flagId, long value)
    {
        Require(owner, StateSlice.WorldFlags);
        World.SetFlag(cell, flagId, value);
    }

    public void HarvestNode(SliceOwner owner, CellKey cell, string nodeKey, long tick)
    {
        Require(owner, StateSlice.Nodes);
        World.HarvestNode(cell, nodeKey, tick);
    }

    public void SetTier(SliceOwner owner, string cellKey, SimulationTier tier)
    {
        Require(owner, StateSlice.CellTiers);
        Tiers = Tiers.SetItem(cellKey, tier);
    }

    public void SetInventory(SliceOwner owner, IEnumerable<InventoryEntry> inventory)
    {
        Require(owner, StateSlice.PlayerInventory);
        Inventory = inventory.OrderBy(e => e.ItemId.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    public void SetCurrency(SliceOwner owner, long currency)
    {
        Require(owner, StateSlice.PlayerInventory);
        Currency = currency >= 0 ? currency : throw new InvalidOperationException("The purse cannot go negative");
    }

    public void SetEquipment(SliceOwner owner, ImmutableSortedDictionary<EquipSlot, EntityId> equipment)
    {
        Require(owner, StateSlice.PlayerEquipment);
        Equipment = equipment;
    }

    public void PlaceItem(SliceOwner owner, CellKey cell, EntityId itemId, string defId, int count, int xCm, int zCm, int quality = 0)
    {
        Require(owner, StateSlice.WorldItems);
        World.PlaceItem(cell, itemId, defId, count, xCm, zCm, quality);
    }

    public CreatedEntityRecord TakeItem(SliceOwner owner, EntityId itemId)
    {
        Require(owner, StateSlice.WorldItems);
        return World.TakeCreated(itemId);
    }

    public void SetContainer(SliceOwner owner, ContainerRecord record)
    {
        Require(owner, StateSlice.WorldItems);
        World.SetContainer(record);
    }

    public void SetPlayerCombat(SliceOwner owner, PlayerCombat combat)
    {
        Require(owner, StateSlice.Combat);
        PlayerCombat = combat;
    }

    public void SetCreature(SliceOwner owner, CreatureState creature)
    {
        Require(owner, StateSlice.Creatures);
        Creatures = Creatures.SetItem(creature.Key, creature);
    }

    public void SetCreatureRecord(SliceOwner owner, CreatureRecord record)
    {
        Require(owner, StateSlice.Creatures);
        World.SetCreature(record);
    }

    public void RemoveCreatureRecord(SliceOwner owner, string key)
    {
        Require(owner, StateSlice.Creatures);
        World.RemoveCreature(key);
    }

    /// <summary>The sounds the next tick's creatures hear (schema 14).</summary>
    public void SetNoises(SliceOwner owner, ImmutableArray<Noise> noises)
    {
        Require(owner, StateSlice.Creatures);
        World.SetNoises(noises);
    }

    public void RemoveContainer(SliceOwner owner, string key)
    {
        Require(owner, StateSlice.WorldItems);
        World.RemoveContainer(key);
    }

    public void SetEffects(SliceOwner owner, EntityId body, ImmutableArray<ActiveEffect> effects)
    {
        Require(owner, StateSlice.Effects);
        Effects = effects.IsEmpty ? Effects.Remove(body) : Effects.SetItem(body, EffectRules.Sorted(effects));
    }

    public void SetNpc(SliceOwner owner, NpcState npc)
    {
        Require(owner, StateSlice.Npcs);
        Npcs = Npcs.SetItem(npc.Definition.Id, npc);
    }

    public int RelationshipOf(string npcId, string dimension) =>
        Relationships.TryGetValue(npcId, out var values) ? values.GetValueOrDefault(dimension) : 0;

    /// <summary>A value of 0 is the absence of a relationship on that dimension and is not kept.</summary>
    public void SetRelationship(SliceOwner owner, string npcId, string dimension, int value)
    {
        Require(owner, StateSlice.Relationships);
        var values = (Relationships.GetValueOrDefault(npcId) ?? ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal));
        values = value == 0 ? values.Remove(dimension) : values.SetItem(dimension, value);
        Relationships = values.IsEmpty ? Relationships.Remove(npcId) : Relationships.SetItem(npcId, values);
    }

    public void MarkVisited(SliceOwner owner, string dialogueId, string nodeId)
    {
        Require(owner, StateSlice.Conversations);
        var heard = Conversations.GetValueOrDefault(dialogueId) ?? ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
        Conversations = Conversations.SetItem(dialogueId, heard.Add(nodeId));
    }

    public void SetConversation(SliceOwner owner, Conversation? conversation)
    {
        Require(owner, StateSlice.Conversations);
        Conversation = conversation;
    }

    public void SetQuest(SliceOwner owner, QuestState quest)
    {
        Require(owner, StateSlice.Quests);
        Quests = Quests.SetItem(quest.QuestId, quest);
    }

    public void SetCompanion(SliceOwner owner, CompanionState companion)
    {
        Require(owner, StateSlice.Companions);
        Companions = Companions.SetItem(companion.NpcId, companion);
    }

    private void Require(SliceOwner owner, StateSlice slice)
    {
        if (!owner.Slices.Contains(slice) || _owners.GetValueOrDefault(slice) != owner.System)
            throw new InvalidOperationException($"{owner.System} wrote {slice}, which {_owners.GetValueOrDefault(slice) ?? "no system"} owns");
    }
}
