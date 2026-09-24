// UNNAMED World - player state (PERSISTENCE.md §5.1)
// No Godot references - pure C#

using System.Buffers.Binary;
using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;

namespace UNNAMED.World;

/// <summary>An inventory stack held by the player, keyed by its item instance ID.</summary>
public sealed record InventoryEntry(EntityId ItemId, string DefId, int Count)
{
    /// <summary>The stack's quality (M3f, schema 9): crude -1, standard 0, fine +1. It belongs to the instance, never the definition.</summary>
    public int Quality { get; init; }
}

/// <summary>How a place became known (SYSTEMS.md S-30). Phase 1 discovers by visiting only; the rest arrive with S-30.</summary>
public enum DiscoveryMethod
{
    Sighted,
    Visited,
    Told,
    Purchased,
    Magical,
}

/// <summary>A discovered-location record (PERSISTENCE.md §5.1): which place, how, and on which world tick.</summary>
public sealed record DiscoveryRecord(string LocationId, DiscoveryMethod Method, long Tick);

/// <summary>What one NPC thinks of the player on one dimension (SYSTEMS.md S-26; schema 10). A value of 0 is never stored.</summary>
public sealed record RelationshipValue(string NpcId, string Dimension, int Value);

/// <summary>The lines of one conversation the player has heard, sorted (SYSTEMS.md S-28; schema 10): what keeps a one-time line spent.</summary>
public sealed record ConversationMemory(string DialogueId, ImmutableArray<string> Heard);

public static class DiscoveryMethods
{
    public static string Key(DiscoveryMethod method) => method switch
    {
        DiscoveryMethod.Sighted => "sighted",
        DiscoveryMethod.Visited => "visited",
        DiscoveryMethod.Told => "told",
        DiscoveryMethod.Purchased => "purchased",
        DiscoveryMethod.Magical => "magical",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown discovery method"),
    };

    public static DiscoveryMethod Parse(string key)
    {
        foreach (var method in Enum.GetValues<DiscoveryMethod>())
        {
            if (Key(method) == key)
                return method;
        }
        throw new FormatException($"Unknown discovery method '{key}'");
    }
}

/// <summary>
/// The player character. Fully serialized, never a delta: a character is not regenerable, so every
/// field is authoritative and a round trip must preserve all of them (PERSISTENCE.md §5.1, T-01).
/// Position is in integer millimetres, so no floating point is persisted.
/// </summary>
public sealed record PlayerRecord
{
    public PlayerRecord(EntityId id, string name, long xMm, long yMm, long zMm, ulong appearanceSeed, IEnumerable<InventoryEntry> inventory,
        CharacterProgression? progression = null, int facingMdeg = 0, IEnumerable<DiscoveryRecord>? discoveries = null,
        IEnumerable<KeyValuePair<EquipSlot, EntityId>>? equipment = null, long currency = 0, IEnumerable<ActiveEffect>? effects = null,
        IEnumerable<RelationshipValue>? relationships = null, IEnumerable<ConversationMemory>? conversations = null,
        IEnumerable<QuestState>? quests = null)
    {
        if (id.Kind != EntityKind.Character)
            throw new ArgumentException($"The player's instance ID must be a character ID, got {id}", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("The player needs a name", nameof(name));
        Id = id;
        Name = name;
        XMm = xMm;
        YMm = yMm;
        ZMm = zMm;
        AppearanceSeed = appearanceSeed;
        Inventory = inventory.OrderBy(e => e.ItemId.Value, StringComparer.Ordinal).ToImmutableArray();
        foreach (var entry in Inventory)
        {
            if (entry.ItemId.Kind != EntityKind.Item)
                throw new ArgumentException($"Inventory holds item instances, got {entry.ItemId}", nameof(inventory));
            if (!DefinitionId.IsValid(entry.DefId) || entry.Count <= 0)
                throw new ArgumentException($"Invalid inventory entry {entry}", nameof(inventory));
        }
        if (Inventory.Select(e => e.ItemId).Distinct().Count() != Inventory.Length)
            throw new ArgumentException("An item instance can be held only once", nameof(inventory));
        Progression = (progression ?? CharacterProgression.Empty).Validate();
        if (facingMdeg is < 0 or >= 360_000)
            throw new ArgumentOutOfRangeException(nameof(facingMdeg), facingMdeg, "Facing is in millidegrees, [0, 360000)");
        FacingMdeg = facingMdeg;
        Discoveries = (discoveries ?? Array.Empty<DiscoveryRecord>()).OrderBy(d => d.LocationId, StringComparer.Ordinal).ToImmutableArray();
        foreach (var discovery in Discoveries)
        {
            if (!DefinitionId.IsValid(discovery.LocationId) || !discovery.LocationId.StartsWith("location.", StringComparison.Ordinal)
                || !Enum.IsDefined(discovery.Method) || discovery.Tick < 0)
                throw new ArgumentException($"Invalid discovery record {discovery}", nameof(discoveries));
        }
        if (Discoveries.Select(d => d.LocationId).Distinct(StringComparer.Ordinal).Count() != Discoveries.Length)
            throw new ArgumentException("A location is discovered only once", nameof(discoveries));
        Equipment = (equipment ?? Array.Empty<KeyValuePair<EquipSlot, EntityId>>()).ToImmutableSortedDictionary();
        var held = Inventory.Select(e => e.ItemId).ToHashSet();
        foreach (var (slot, item) in Equipment)
        {
            if (!held.Contains(item))
                throw new ArgumentException($"The {EquipSlots.Key(slot)} slot holds {item}, which is not in the inventory", nameof(equipment));
        }
        if (Equipment.Values.Distinct().Count() != Equipment.Count)
            throw new ArgumentException("One item fills two equipment slots", nameof(equipment));
        if (currency < 0)
            throw new ArgumentOutOfRangeException(nameof(currency), currency, "Currency is never negative");
        Currency = currency;
        Effects = EffectRules.Sorted(effects ?? Array.Empty<ActiveEffect>());
        foreach (var effect in Effects)
        {
            if (!DefinitionId.IsValid(effect.EffectId) || !effect.EffectId.StartsWith("effect.", StringComparison.Ordinal)
                || effect.Stacks < 1 || effect.ExpiresTick < 0 || effect.NextTickAt < 0)
                throw new ArgumentException($"Invalid active effect {effect}", nameof(effects));
        }
        if (Effects.Select(e => e.EffectId).Distinct(StringComparer.Ordinal).Count() != Effects.Length)
            throw new ArgumentException("An effect is active only once; stacks count repeats", nameof(effects));
        Relationships = (relationships ?? Array.Empty<RelationshipValue>())
            .OrderBy(r => r.NpcId, StringComparer.Ordinal).ThenBy(r => r.Dimension, StringComparer.Ordinal).ToImmutableArray();
        foreach (var r in Relationships)
        {
            if (!DefinitionId.IsValid(r.NpcId) || !r.NpcId.StartsWith("npc.", StringComparison.Ordinal) || !Domain.Social.Relationships.IsDimension(r.Dimension)
                || r.Value == 0 || r.Value != Domain.Social.Relationships.Clamp(r.Value))
                throw new ArgumentException($"Invalid relationship value {r}", nameof(relationships));
        }
        if (Relationships.Select(r => (r.NpcId, r.Dimension)).Distinct().Count() != Relationships.Length)
            throw new ArgumentException("An NPC holds one value per dimension", nameof(relationships));
        Conversations = (conversations ?? Array.Empty<ConversationMemory>())
            .Select(c => c with { Heard = c.Heard.OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray() })
            .OrderBy(c => c.DialogueId, StringComparer.Ordinal).ToImmutableArray();
        foreach (var c in Conversations)
        {
            if (!DefinitionId.IsValid(c.DialogueId) || !c.DialogueId.StartsWith("dialogue.", StringComparison.Ordinal) || c.Heard.IsEmpty
                || c.Heard.Any(string.IsNullOrWhiteSpace) || c.Heard.Distinct(StringComparer.Ordinal).Count() != c.Heard.Length)
                throw new ArgumentException($"Invalid conversation memory for {c.DialogueId}", nameof(conversations));
        }
        if (Conversations.Select(c => c.DialogueId).Distinct(StringComparer.Ordinal).Count() != Conversations.Length)
            throw new ArgumentException("A conversation is remembered once", nameof(conversations));
        Quests = (quests ?? Array.Empty<QuestState>())
            .Select(q => q with { Objectives = q.Objectives.OrderBy(o => o.Id, StringComparer.Ordinal).ToImmutableArray() })
            .OrderBy(q => q.QuestId, StringComparer.Ordinal).ToImmutableArray();
        foreach (var q in Quests)
        {
            bool ended = q.Status != QuestStatus.Active;
            if (!DefinitionId.IsValid(q.QuestId) || !q.QuestId.StartsWith("quest.", StringComparison.Ordinal) || !Enum.IsDefined(q.Status)
                || q.StartedTick < 0 || ended != q.EndedTick.HasValue || ended == string.IsNullOrEmpty(q.EndedBy) || q.EndedTick < q.StartedTick
                || q.Objectives.IsEmpty)
                throw new ArgumentException($"Invalid quest state for {q.QuestId}", nameof(quests));
            foreach (var o in q.Objectives)
            {
                bool over = o.Status != ObjectiveStatus.Active;
                if (string.IsNullOrWhiteSpace(o.Id) || !Enum.IsDefined(o.Status) || o.ActivatedTick < q.StartedTick || over != o.EndedTick.HasValue
                    || o.EndedTick < o.ActivatedTick || o.Progress < 0 || (!over && ended))
                    throw new ArgumentException($"Invalid objective state {o.Id} of {q.QuestId}", nameof(quests));
            }
            if (q.Objectives.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != q.Objectives.Length)
                throw new ArgumentException($"An objective of {q.QuestId} is recorded once", nameof(quests));
        }
        if (Quests.Select(q => q.QuestId).Distinct(StringComparer.Ordinal).Count() != Quests.Length)
            throw new ArgumentException("A quest is recorded once", nameof(quests));
    }

    /// <summary>
    /// The appearance seed a character gets when nothing chose one: derived from its ULID, so it is
    /// deterministic and unique per character. Schema 3 made the field required; the 2 -> 3 migration
    /// gives older saves exactly this value.
    /// </summary>
    public static ulong DerivedAppearanceSeed(EntityId id)
    {
        using var h = new CanonicalHasher();
        return BinaryPrimitives.ReadUInt64BigEndian(h.Add("unnamed.appearance-seed/v1").Add(id.Value).FinishBytes());
    }

    /// <summary>The same player holding a different inventory (the definition-ID pass rewrites stored IDs).</summary>
    public PlayerRecord WithInventory(IEnumerable<InventoryEntry> inventory)
    {
        var entries = inventory.ToList();
        var held = entries.Select(e => e.ItemId).ToHashSet();
        // An equipped item whose entry the pass dropped is unequipped with it, never left dangling.
        return new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, entries, Progression, FacingMdeg, Discoveries,
            Equipment.Where(kv => held.Contains(kv.Value)), Currency, Effects, Relationships, Conversations, Quests);
    }

    /// <summary>The same player with different progression (schema 4; the definition-ID pass rewrites its IDs too).</summary>
    public PlayerRecord WithProgression(CharacterProgression progression) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, Relationships, Conversations, Quests);

    /// <summary>The same player with different discovery records (schema 5; the definition-ID pass rewrites their location IDs).</summary>
    public PlayerRecord WithDiscoveries(IEnumerable<DiscoveryRecord> discoveries) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, discoveries, Equipment, Currency, Effects, Relationships, Conversations, Quests);

    /// <summary>The same player with different active effects (schema 7; the definition-ID pass rewrites their effect IDs).</summary>
    public PlayerRecord WithEffects(IEnumerable<ActiveEffect> effects) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, effects, Relationships, Conversations, Quests);

    /// <summary>The same player with different relationships and conversation memory (schema 10; the definition-ID pass rewrites their IDs).</summary>
    public PlayerRecord WithSocial(IEnumerable<RelationshipValue> relationships, IEnumerable<ConversationMemory> conversations) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, relationships, conversations, Quests);

    /// <summary>The same player with different quests (schema 11; the definition-ID pass rewrites their quest IDs).</summary>
    public PlayerRecord WithQuests(IEnumerable<QuestState> quests) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, Relationships, Conversations, quests);

    public EntityId Id { get; }
    public string Name { get; }
    public long XMm { get; }
    public long YMm { get; }
    public long ZMm { get; }

    /// <summary>The character's appearance identity (PERSISTENCE.md §5.1: "identity ... appearance seed").</summary>
    public ulong AppearanceSeed { get; }

    public ImmutableArray<InventoryEntry> Inventory { get; }

    /// <summary>Level, attributes, skills, known techniques, pools and guards (PROGRESSION.md §3-§4). Schema 4.</summary>
    public CharacterProgression Progression { get; }

    /// <summary>Which way the character faces, in millidegrees from +Z towards +X (PROTOTYPE.md §7.4). Schema 5.</summary>
    public int FacingMdeg { get; }

    /// <summary>Discovered-location records, sorted by location ID (PERSISTENCE.md §5.1). Schema 5.</summary>
    public ImmutableArray<DiscoveryRecord> Discoveries { get; }

    /// <summary>Equipment slots, each naming an item the inventory holds (SYSTEMS.md S-15). Schema 6.</summary>
    public ImmutableSortedDictionary<EquipSlot, EntityId> Equipment { get; }

    /// <summary>The purse (PROTOTYPE.md: coin is not an item). Schema 6.</summary>
    public long Currency { get; }

    /// <summary>
    /// Status effects on the character, with absolute deadlines in world ticks (SYSTEMS.md S-11: a save mid-fight keeps
    /// its bleeding, and dying's weakness cannot be saved away). Sorted by effect ID. Schema 7.
    /// </summary>
    public ImmutableArray<ActiveEffect> Effects { get; }

    /// <summary>What each NPC thinks of the player, sorted by NPC and dimension (PERSISTENCE.md §5.1: "relationship values"). Schema 10.</summary>
    public ImmutableArray<RelationshipValue> Relationships { get; }

    /// <summary>The lines of each conversation the player has heard, sorted by conversation (SYSTEMS.md S-28). Schema 10.</summary>
    public ImmutableArray<ConversationMemory> Conversations { get; }

    /// <summary>
    /// Every quest the player has started, sorted by quest, each with its objectives sorted by ID (SYSTEMS.md S-29: status,
    /// per-objective progress, branches as closed objectives, what ended it). Schema 11.
    /// </summary>
    public ImmutableArray<QuestState> Quests { get; }

    /// <summary>Full-equality digest over every field (T-01: "no field silently defaulted").</summary>
    public string Digest
    {
        get
        {
            using var h = new CanonicalHasher();
            h.Add("unnamed.player/v8").Add(Id.Value).Add(Name).Add(XMm).Add(YMm).Add(ZMm).Add(FacingMdeg).Add(AppearanceSeed).Add(Inventory.Length);
            foreach (var e in Inventory)
                h.Add(e.ItemId.Value).Add(e.DefId).Add(e.Count).Add(e.Quality);
            h.Add(Progression.Digest).Add(Discoveries.Length);
            foreach (var d in Discoveries)
                h.Add(d.LocationId).Add(DiscoveryMethods.Key(d.Method)).Add(d.Tick);
            h.Add(Equipment.Count);
            foreach (var (slot, item) in Equipment)
                h.Add(EquipSlots.Key(slot)).Add(item.Value);
            h.Add(Currency).Add(Effects.Length);
            foreach (var e in Effects)
                h.Add(e.EffectId).Add(e.Stacks).Add(e.ExpiresTick).Add(e.NextTickAt);
            h.Add(Relationships.Length);
            foreach (var r in Relationships)
                h.Add(r.NpcId).Add(r.Dimension).Add(r.Value);
            h.Add(Conversations.Length);
            foreach (var c in Conversations)
            {
                h.Add(c.DialogueId).Add(c.Heard.Length);
                foreach (string node in c.Heard)
                    h.Add(node);
            }
            h.Add(Quests.Length);
            foreach (var q in Quests)
            {
                h.Add(q.QuestId).Add(QuestKeys.Key(q.Status)).Add(q.StartedTick).Add(q.EndedTick ?? -1).Add(q.EndedBy ?? "").Add(q.Objectives.Length);
                foreach (var o in q.Objectives)
                    h.Add(o.Id).Add(QuestKeys.Key(o.Status)).Add(o.ActivatedTick).Add(o.EndedTick ?? -1).Add(o.Progress);
            }
            return h.Finish();
        }
    }
}
