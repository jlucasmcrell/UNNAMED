// UNNAMED World - player state (PERSISTENCE.md §5.1)
// No Godot references - pure C#

using System.Buffers.Binary;
using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;

namespace UNNAMED.World;

/// <summary>An inventory stack held by the player, keyed by its item instance ID.</summary>
public sealed record InventoryEntry(EntityId ItemId, string DefId, int Count)
{
    /// <summary>The stack's quality (M3f, schema 9): crude -1, standard 0, fine +1, masterwork +2. It belongs to the instance, never the definition.</summary>
    public int Quality { get; init; }

    /// <summary>A notable instance's record (WP3a): name, provenance, familiarity. Null for an ordinary stack.</summary>
    public NotableRecord? Notable { get; init; }
}

/// <summary>How a notable-instance record enters a digest: nothing for an ordinary stack, so ordinary stacks hash as they always have.</summary>
public static class NotableDigest
{
    public static CanonicalHasher Add(CanonicalHasher h, NotableRecord? notable) =>
        notable is null
            ? h
            : h.Add("notable").Add(notable.Name).Add(ProvenanceKinds.Key(notable.Provenance)).Add(notable.SourceRef).Add(notable.Tick)
                .Add(notable.Maker ?? "-").Add(notable.Familiarity).Add(notable.Uses);
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

/// <summary>
/// A Waystone the character knows (S-38's unlock record; schema 15): which stone, in which region, how they came to know it, and when.
/// The region is what lets a later area's stones join the same list without rework; today every stone is in the region played.
/// </summary>
public sealed record WaystoneRecord(string StoneKey, string RegionId, WaystoneMethod Method, long Tick);

/// <summary>What one NPC thinks of the player on one dimension (SYSTEMS.md S-26; schema 10). A value of 0 is never stored.</summary>
public sealed record RelationshipValue(string NpcId, string Dimension, int Value);

/// <summary>The lines of one conversation the player has heard, sorted (SYSTEMS.md S-28; schema 10): what keeps a one-time line spent.</summary>
public sealed record ConversationMemory(string DialogueId, ImmutableArray<string> Heard);

/// <summary>A mark on the character's trail, which a following companion walks (M6).</summary>
public sealed record TrailMark(long XMm, long ZMm);

/// <summary>
/// A companion the character has recruited (SYSTEMS.md S-25; schema 12): who, under which order, up or downed, where they stand and
/// how hurt they are - and what the rest of their day depends on, so a loaded world goes on as the saved one would: when they went
/// down, the trail they are walking, how long they have made no headway, and when they last fought. Only a blow in progress is not
/// kept, as with creatures.
/// </summary>
public sealed record CompanionRecord(string NpcId, CompanionOrder Order, CompanionCondition Condition, long XMm, long ZMm, int FacingMdeg, int Health)
{
    /// <summary>The tick they went down; 0 while up.</summary>
    public long DownedTick { get; init; }

    public int StuckTicks { get; init; }

    public long LastCombatTick { get; init; }

    public ImmutableArray<TrailMark> Trail { get; init; } = ImmutableArray<TrailMark>.Empty;
}

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
        IEnumerable<QuestState>? quests = null, IEnumerable<CompanionRecord>? companions = null)
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
            if (!DefinitionId.IsValid(entry.DefId) || entry.Count <= 0 || entry.Notable is { IsValid: false } || (entry.Notable is not null && entry.Count != 1))
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
        Companions = (companions ?? Array.Empty<CompanionRecord>()).OrderBy(c => c.NpcId, StringComparer.Ordinal).ToImmutableArray();
        foreach (var c in Companions)
        {
            bool down = c.Condition == CompanionCondition.Downed;
            if (!DefinitionId.IsValid(c.NpcId) || !c.NpcId.StartsWith("npc.", StringComparison.Ordinal) || !Enum.IsDefined(c.Order) || !Enum.IsDefined(c.Condition)
                || c.FacingMdeg is < 0 or >= 360_000 || c.Health < 0 || down != (c.Health == 0) || c.DownedTick < 0 || (!down && c.DownedTick != 0)
                || c.StuckTicks < 0 || c.LastCombatTick < 0 || c.Trail.IsDefault)
                throw new ArgumentException($"Invalid companion record for {c.NpcId}", nameof(companions));
        }
        if (Companions.Select(c => c.NpcId).Distinct(StringComparer.Ordinal).Count() != Companions.Length)
            throw new ArgumentException("A companion is recorded once", nameof(companions));
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
            Equipment.Where(kv => held.Contains(kv.Value)), Currency, Effects, Relationships, Conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions };
    }

    /// <summary>The same player with different progression (schema 4; the definition-ID pass rewrites its IDs too).</summary>
    public PlayerRecord WithProgression(CharacterProgression progression) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, Relationships, Conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions };

    /// <summary>The same player with different discovery records (schema 5; the definition-ID pass rewrites their location IDs).</summary>
    public PlayerRecord WithDiscoveries(IEnumerable<DiscoveryRecord> discoveries) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, discoveries, Equipment, Currency, Effects, Relationships, Conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions };

    /// <summary>The same player with different active effects (schema 7; the definition-ID pass rewrites their effect IDs).</summary>
    public PlayerRecord WithEffects(IEnumerable<ActiveEffect> effects) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, effects, Relationships, Conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions };

    /// <summary>The same player with different relationships and conversation memory (schema 10; the definition-ID pass rewrites their IDs).</summary>
    public PlayerRecord WithSocial(IEnumerable<RelationshipValue> relationships, IEnumerable<ConversationMemory> conversations) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, relationships, conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions };

    /// <summary>The same player with different quests (schema 11; the definition-ID pass rewrites their quest IDs).</summary>
    public PlayerRecord WithQuests(IEnumerable<QuestState> quests) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, Relationships, Conversations, quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions };

    /// <summary>The same player with different companions (schema 12; the definition-ID pass rewrites their NPC IDs).</summary>
    public PlayerRecord WithCompanions(IEnumerable<CompanionRecord> companions) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, Relationships, Conversations, Quests, companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions };

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

    /// <summary>The companions the character has recruited, sorted by NPC (SYSTEMS.md S-25). Schema 12.</summary>
    public ImmutableArray<CompanionRecord> Companions { get; }

    /// <summary>
    /// Standing or crouched, and how far into a jump (the owner's M6 playtest). Schema 13: a save made crouched under an overhang, or in
    /// mid-air, loads the same way.
    /// </summary>
    public Posture Posture
    {
        get => _posture;
        init
        {
            if (!Enum.IsDefined(value.Stance) || value.AirMs < 0 || (!value.Airborne && value.AirMs != 0))
                throw new ArgumentException($"Invalid posture {value}", nameof(Posture));
            _posture = value;
        }
    }

    private readonly Posture _posture;

    /// <summary>The Waystones the character knows, sorted by stone (schema 15): touched awake, or woken by them.</summary>
    public ImmutableArray<WaystoneRecord> Waystones
    {
        get => _waystones;
        init
        {
            var stones = value.IsDefault ? ImmutableArray<WaystoneRecord>.Empty : value.OrderBy(w => w.StoneKey, StringComparer.Ordinal).ToImmutableArray();
            foreach (var stone in stones)
            {
                if (!stone.StoneKey.StartsWith("waystone.", StringComparison.Ordinal) || !DefinitionId.IsValid(stone.RegionId)
                    || !stone.RegionId.StartsWith("region.", StringComparison.Ordinal) || !Enum.IsDefined(stone.Method) || stone.Tick < 0)
                    throw new ArgumentException($"Invalid Waystone record {stone}", nameof(Waystones));
            }
            if (stones.Select(w => w.StoneKey).Distinct(StringComparer.Ordinal).Count() != stones.Length)
                throw new ArgumentException("A Waystone is known once", nameof(Waystones));
            _waystones = stones;
        }
    }

    private readonly ImmutableArray<WaystoneRecord> _waystones = ImmutableArray<WaystoneRecord>.Empty;

    /// <summary>
    /// The stone the character chose to return to (schema 15): one they know, made their return point by a deliberate act and never by
    /// travel. Null until they choose: they return at the region's spawn, the Ashen Waystone.
    /// </summary>
    public string? ReturnPoint
    {
        get => _returnPoint;
        init
        {
            if (value is not null && !Waystones.Any(w => w.StoneKey == value))
                throw new ArgumentException($"The return point {value} is not a Waystone the character knows", nameof(ReturnPoint));
            _returnPoint = value;
        }
    }

    private readonly string? _returnPoint;

    /// <summary>The stretches of road the character has walked, sorted (schema 15): what the map draws of the roads.</summary>
    public ImmutableArray<string> RoadsWalked
    {
        get => _roadsWalked;
        init
        {
            var stretches = value.IsDefault ? ImmutableArray<string>.Empty : value.OrderBy(k => k, StringComparer.Ordinal).ToImmutableArray();
            if (stretches.Any(k => !k.StartsWith("road.", StringComparison.Ordinal) || !k.Contains('#')) || stretches.Distinct(StringComparer.Ordinal).Count() != stretches.Length)
                throw new ArgumentException("Walked roads are distinct road.<id>#<stretch> keys", nameof(RoadsWalked));
            _roadsWalked = stretches;
        }
    }

    private readonly ImmutableArray<string> _roadsWalked = ImmutableArray<string>.Empty;

    /// <summary>
    /// How many times the character has done each deed, and when last, sorted by tally key (E-7; schema 16): killed a creature, crafted
    /// or harvested something, delivered an item to someone. What dialogue's <c>deed</c> condition reads.
    /// </summary>
    public ImmutableArray<DeedTally> Deeds
    {
        get => _deeds;
        init
        {
            var deeds = value.IsDefault ? ImmutableArray<DeedTally>.Empty : value.OrderBy(d => d.Key, StringComparer.Ordinal).ToImmutableArray();
            foreach (var d in deeds)
            {
                if (!DeedKeys.Tallied.Contains(d.Kind) || !DefinitionId.IsValid(d.SubjectId) || d.Count < 1 || d.LastTick < 0
                    || (d.NpcId is not null && (d.Kind != DeedKind.Delivered || !DefinitionId.IsValid(d.NpcId) || !d.NpcId.StartsWith("npc.", StringComparison.Ordinal))))
                    throw new ArgumentException($"Invalid deed tally {d}", nameof(Deeds));
            }
            if (deeds.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count() != deeds.Length)
                throw new ArgumentException("A deed is tallied once", nameof(Deeds));
            _deeds = deeds;
        }
    }

    private readonly ImmutableArray<DeedTally> _deeds = ImmutableArray<DeedTally>.Empty;

    /// <summary>The same player with different deed tallies (schema 16; the definition-ID pass rewrites their IDs).</summary>
    public PlayerRecord WithDeeds(IEnumerable<DeedTally> deeds) => this with { Deeds = deeds.ToImmutableArray() };

    /// <summary>
    /// The character's faction ledger (WP6; schema 20): the acts some faction reacts to, in sequence; what each faction knows of them, by
    /// knower then act; and the standing each holds, by faction, never 0. A tier is derived from points when read, never saved.
    /// </summary>
    public FactionLedger Factions
    {
        get => _factions;
        init
        {
            var ledger = value ?? throw new ArgumentNullException(nameof(Factions));
            if (ledger.NextActSeq < 1 || ledger.Acts.IsDefault || ledger.Knowledge.IsDefault || ledger.Standing.IsDefault)
                throw new ArgumentException("A faction ledger counts acts from 1 and holds its lists", nameof(Factions));
            long previous = 0;
            foreach (var a in ledger.Acts)
            {
                if (a.Seq <= previous || a.Seq >= ledger.NextActSeq || !ActKinds.Built.Contains(a.Kind) || !DefinitionId.IsValid(a.Subject)
                    || string.IsNullOrEmpty(a.CellKey) || a.Tick < 0)
                    throw new ArgumentException($"Invalid act {a}: acts are built kinds, in ascending sequence below the next", nameof(Factions));
                previous = a.Seq;
            }
            var acts = ledger.Acts.Select(a => a.Seq).ToHashSet();
            foreach (var k in ledger.Knowledge)
            {
                if (!IsFaction(k.Knower) || !acts.Contains(k.Act) || !Identities.All.Contains(k.Identity) || !KnowledgeSources.All.Contains(k.Source)
                    || (k.Via is not null && (!DefinitionId.IsValid(k.Via) || !k.Via.StartsWith("npc.", StringComparison.Ordinal))) || k.Tick < 0)
                    throw new ArgumentException($"Invalid faction knowledge {k}", nameof(Factions));
            }
            if (!ledger.Knowledge.SequenceEqual(ledger.Knowledge.OrderBy(k => k.Knower, StringComparer.Ordinal).ThenBy(k => k.Act))
                || ledger.Knowledge.Select(k => (k.Knower, k.Act)).Distinct().Count() != ledger.Knowledge.Length)
                throw new ArgumentException("A faction knows an act once, rows by knower then act", nameof(Factions));
            foreach (var st in ledger.Standing)
            {
                if (!IsFaction(st.FactionId) || st.Points == 0 || st.Points < FactionLedger.MinPoints || st.Points > FactionLedger.MaxPoints)
                    throw new ArgumentException($"Invalid standing {st}", nameof(Factions));
            }
            if (!ledger.Standing.Select(st => st.FactionId).SequenceEqual(ledger.Standing.Select(st => st.FactionId).Distinct().OrderBy(f => f, StringComparer.Ordinal)))
                throw new ArgumentException("A standing is held once per faction, by faction", nameof(Factions));
            _factions = ledger;
        }
    }

    private readonly FactionLedger _factions = FactionLedger.Empty;

    private static bool IsFaction(string id) => DefinitionId.IsValid(id) && id.StartsWith("faction.", StringComparison.Ordinal);

    /// <summary>The same player with a different faction ledger (schema 20; the definition-ID pass rewrites its IDs).</summary>
    public PlayerRecord WithFactions(FactionLedger factions) => this with { Factions = factions };

    /// <summary>
    /// Who the character is besides their name (schema 17): pronouns, people, body preset and Call, as the creator made them. A character
    /// from before the creator is "they", a Veth of the default body, with no Call.
    /// </summary>
    public CharacterIdentity Identity
    {
        get => _identity;
        init
        {
            if (value is null || !Enum.IsDefined(value.Pronouns) || !DefinitionId.IsValid(value.PeopleId) || !value.PeopleId.StartsWith("people.", StringComparison.Ordinal)
                || !DefinitionId.IsValid(value.BodyPresetId) || !value.BodyPresetId.StartsWith("body.", StringComparison.Ordinal)
                || value.CallId is { } call && (!DefinitionId.IsValid(call) || !call.StartsWith("call.", StringComparison.Ordinal)))
                throw new ArgumentException($"Invalid character identity {value}", nameof(Identity));
            _identity = value;
        }
    }

    private readonly CharacterIdentity _identity = CharacterIdentity.BeforeTheCreator;

    /// <summary>The same player holding themselves differently.</summary>
    public PlayerRecord WithPosture(Posture posture) => this with { Posture = posture };

    /// <summary>Full-equality digest over every field (T-01: "no field silently defaulted").</summary>
    public string Digest
    {
        get
        {
            using var h = new CanonicalHasher();
            h.Add("unnamed.player/v10").Add(Id.Value).Add(Name).Add(XMm).Add(YMm).Add(ZMm).Add(FacingMdeg).Add(AppearanceSeed).Add(Inventory.Length);
            foreach (var e in Inventory)
                NotableDigest.Add(h.Add(e.ItemId.Value).Add(e.DefId).Add(e.Count).Add(e.Quality), e.Notable);
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
            h.Add(Companions.Length);
            foreach (var c in Companions)
            {
                h.Add(c.NpcId).Add(CompanionKeys.Key(c.Order)).Add(CompanionKeys.Key(c.Condition)).Add(c.XMm).Add(c.ZMm).Add(c.FacingMdeg).Add(c.Health)
                    .Add(c.DownedTick).Add(c.StuckTicks).Add(c.LastCombatTick).Add(c.Trail.Length);
                foreach (var mark in c.Trail)
                    h.Add(mark.XMm).Add(mark.ZMm);
            }
            h.Add(StanceKeys.Key(Posture.Stance)).Add(Posture.Airborne ? 1 : 0).Add(Posture.AirMs);
            h.Add(Waystones.Length);
            foreach (var w in Waystones)
                h.Add(w.StoneKey).Add(w.RegionId).Add(WaystoneMethods.Key(w.Method)).Add(w.Tick);
            h.Add(ReturnPoint ?? "-").Add(RoadsWalked.Length);
            foreach (string stretch in RoadsWalked)
                h.Add(stretch);
            h.Add(Deeds.Length);
            foreach (var d in Deeds)
                h.Add(DeedKeys.Key(d.Kind)).Add(d.SubjectId).Add(d.NpcId ?? "-").Add(d.Count).Add(d.LastTick);
            h.Add(PronounKeys.Key(Identity.Pronouns)).Add(Identity.PeopleId).Add(Identity.BodyPresetId).Add(Identity.CallId ?? "-");
            h.Add(Factions.NextActSeq).Add(Factions.Acts.Length);
            foreach (var a in Factions.Acts)
                h.Add(a.Seq).Add(a.Kind).Add(a.Subject).Add(a.CellKey).Add(a.XMm).Add(a.ZMm).Add(a.Tick);
            h.Add(Factions.Knowledge.Length);
            foreach (var k in Factions.Knowledge)
                h.Add(k.Knower).Add(k.Act).Add(k.Identity).Add(k.Source).Add(k.Via ?? "-").Add(k.Tick).Add(k.Delta);
            h.Add(Factions.Standing.Length);
            foreach (var st in Factions.Standing)
                h.Add(st.FactionId).Add(st.Points);
            return h.Finish();
        }
    }
}

/// <summary>The stances' stable keys in saves and dumps (the owner's M6 playtest).</summary>
public static class StanceKeys
{
    public static string Key(Stance stance) => stance switch
    {
        Stance.Standing => "standing",
        Stance.Crouched => "crouched",
        _ => throw new ArgumentOutOfRangeException(nameof(stance), stance, null),
    };

    public static Stance Parse(string key) => key switch
    {
        "standing" => Stance.Standing,
        "crouched" => Stance.Crouched,
        _ => throw new FormatException($"Unknown stance '{key}'"),
    };
}
