// UNNAMED World - player state (PERSISTENCE.md §5.1)
// No Godot references - pure C#

using System.Buffers.Binary;
using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
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

    /// <summary>The wear it has taken and the quality it was made at (WP3b, D5). Null: unworn, at its made quality.</summary>
    public ItemWear? Wear { get; init; }
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

/// <summary>How a stack's wear enters a digest: nothing for an unworn stack, so unworn stacks hash as they always have (WP3b).</summary>
public static class WearDigest
{
    public static CanonicalHasher Add(CanonicalHasher h, ItemWear? wear) =>
        wear is null ? h : h.Add("wear").Add(wear.Points).Add(wear.MadeQuality);
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

/// <summary>
/// The character's attack under way (schema 27): what makes the blow - a weapon's definition ID, "unarmed" or a technique's ID - the tick it
/// began, and the creatures it has struck. Its timing is not kept: the weapon in hand and the technique make the blow again on load.
/// </summary>
public sealed record AttackRecord(string Source, long StartTick, ImmutableArray<EntityId> Struck);

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
    /// <summary>Null in historical saves: use the NPC's home space. Otherwise the actual space where they were saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Space { get; init; }
    /// <summary>The tick they went down; 0 while up.</summary>
    public long DownedTick { get; init; }

    public int StuckTicks { get; init; }

    public long LastCombatTick { get; init; }

    public ImmutableArray<TrailMark> Trail { get; init; } = ImmutableArray<TrailMark>.Empty;

    /// <summary>Why they travel with the character (WP15a): a companion, a hireling under contract, or a temporary ally.</summary>
    public PartyMembership Membership { get; init; } = PartyMembership.Companion;

    /// <summary>How readily they fight (WP15a).</summary>
    public CompanionStance Stance { get; init; } = CompanionStance.Aggressive;

    /// <summary>The standing rules they keep (WP15a).</summary>
    public PartyTactics Tactics { get; init; } = PartyTactics.Default;

    /// <summary>A hireling's contract (WP15a); null for anyone else.</summary>
    public HirelingContract? Contract { get; init; }
}

/// <summary>A companion gravely wounded (DN9): away at home until <see cref="UntilTick"/>, recovering, and not to be asked along before.</summary>
public sealed record RecoveryRecord(string NpcId, long UntilTick);

/// <summary>
/// What the party has lost and who is mending (WP15a): companions recovering at home from a grave wound, and the hirelings who died in
/// the character's service (DN9) - who stay dead, so are never put back in the world.
/// </summary>
public sealed record PartyLedger(ImmutableArray<RecoveryRecord> Recovering, ImmutableArray<string> Fallen)
{
    public static readonly PartyLedger Empty = new(ImmutableArray<RecoveryRecord>.Empty, ImmutableArray<string>.Empty);

    /// <summary>Sorted, so equal ledgers are equal records and digests.</summary>
    public PartyLedger Sorted() => new(Recovering.OrderBy(r => r.NpcId, StringComparer.Ordinal).ToImmutableArray(),
        Fallen.OrderBy(f => f, StringComparer.Ordinal).ToImmutableArray());

    public bool Equals(PartyLedger? other) =>
        other is not null && Recovering.SequenceEqual(other.Recovering) && Fallen.SequenceEqual(other.Fallen);

    public override int GetHashCode() => HashCode.Combine(Recovering.Length, Fallen.Length);
}

/// <summary>
/// A piece a party member wears (WP15b): the item, with its identity, and whether the character only loaned it - a loan comes back when they
/// leave the party or are asked for it; a gift is theirs.
/// </summary>
public sealed record PartyGear(InventoryEntry Item, bool Loaned);

/// <summary>
/// What a party member, past or present, has made of themselves (WP15b; canon :20-36, :74-82): their progression on the character's rules,
/// what they wear in the same ten slots, and what is theirs but not worn. It outlives their time in the party: someone dismissed keeps their
/// level, gear and belongings, and has them again when they come back.
/// </summary>
public sealed record PartySheet(string NpcId, CharacterProgression Progression)
{
    public ImmutableSortedDictionary<EquipSlot, PartyGear> Gear { get; init; } = ImmutableSortedDictionary<EquipSlot, PartyGear>.Empty;

    /// <summary>Their own things, not worn: gifts a later gift displaced. Sorted by item instance.</summary>
    public ImmutableArray<InventoryEntry> Belongings { get; init; } = ImmutableArray<InventoryEntry>.Empty;

    /// <summary>Every item they hold, worn or not.</summary>
    public IEnumerable<InventoryEntry> Items => Gear.Values.Select(g => g.Item).Concat(Belongings);

    public bool Equals(PartySheet? other) =>
        other is not null && NpcId == other.NpcId && Progression.Digest == other.Progression.Digest && Gear.SequenceEqual(other.Gear)
        && Belongings.SequenceEqual(other.Belongings);

    public override int GetHashCode() => HashCode.Combine(NpcId, Gear.Count, Belongings.Length);
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
            if (!DefinitionId.IsValid(entry.DefId) || entry.Count <= 0 || entry.Notable is { IsValid: false } || (entry.Notable is not null && entry.Count != 1)
                || entry.Wear is { IsValid: false } || (entry.Wear is not null && entry.Count != 1))
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
            if (!DefinitionId.IsValid(r.NpcId) || !PartyMemberKeys.IsMember(r.NpcId) || !Domain.Social.Relationships.IsDimension(r.Dimension)
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
            if (!DefinitionId.IsValid(c.NpcId) || !PartyMemberKeys.IsMember(c.NpcId) || !Enum.IsDefined(c.Order) || !Enum.IsDefined(c.Condition)
                || c.FacingMdeg is < 0 or >= 360_000 || c.Health < 0 || down != (c.Health == 0) || c.DownedTick < 0 || (!down && c.DownedTick != 0)
                || c.StuckTicks < 0 || c.LastCombatTick < 0 || c.Trail.IsDefault || !Enum.IsDefined(c.Membership) || !Enum.IsDefined(c.Stance)
                || c.Tactics is null || (c.Membership == PartyMembership.Hireling) != (c.Contract is not null) || c.Contract is { } k && !ValidContract(k))
                throw new ArgumentException($"Invalid companion record for {c.NpcId}", nameof(companions));
        }
        if (Companions.Select(c => c.NpcId).Distinct(StringComparer.Ordinal).Count() != Companions.Length)
            throw new ArgumentException("A companion is recorded once", nameof(companions));
    }

    private static bool ValidContract(HirelingContract k) =>
        Enum.IsDefined(k.Role) && k.Days >= 1 && k.DailyWage >= 0 && !string.IsNullOrWhiteSpace(k.LootRule) && k.DaysServed >= 0 && k.DaysServed < k.Days
        && k.UnpaidDays >= 0 && k.Purse >= 0 && k.WorkSite is not "" && (k.RecipeId is null || DefinitionId.IsValid(k.RecipeId))
        && k.Supply.All(kv => DefinitionId.IsValid(kv.Key) && kv.Value > 0);

    /// <summary>
    /// What the party has lost and who is mending (WP15a): sorted, each NPC once, and none of them in the party now.
    /// </summary>
    public PartyLedger Party
    {
        get => _party;
        init
        {
            var party = (value ?? throw new ArgumentNullException(nameof(Party))).Sorted();
            var ids = party.Recovering.Select(r => r.NpcId).Concat(party.Fallen).ToList();
            if (ids.Any(id => !DefinitionId.IsValid(id) || !PartyMemberKeys.IsMember(id)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count
                || party.Recovering.Any(r => r.UntilTick < 0) || ids.Any(id => Companions.Any(c => c.NpcId == id)))
                throw new ArgumentException("Invalid party ledger: each NPC recovering or fallen once, and not in the party", nameof(Party));
            _party = party;
        }
    }

    private readonly PartyLedger _party = PartyLedger.Empty;

    /// <summary>
    /// The first action bar's twelve slots (WP9; schema 23, widened to twelve at 32), each empty or holding a usable item, a known formula or a known technique by definition ID,
    /// nothing twice. A character before the bar has it empty.
    /// </summary>
    public ImmutableArray<string?> QuickBar
    {
        get => _quickBar;
        init => _quickBar = Domain.Progression.QuickBar.IsValid(value) ? value
            : throw new ArgumentException("A quick bar is twelve slots, each empty or holding one item, formula, technique or gear-set switch once", nameof(QuickBar));
    }

    private readonly ImmutableArray<string?> _quickBar = Domain.Progression.QuickBar.Empty;

    /// <summary>
    /// The other five action bars (schema 32), bars 2 to 6, each twelve slots by the quick bar's rules. A character before them has them empty.
    /// </summary>
    public ImmutableArray<ImmutableArray<string?>> ExtraBars
    {
        get => _extraBars;
        init => _extraBars = !value.IsDefault && value.Length == Domain.Progression.QuickBar.Bars - 1 && value.All(Domain.Progression.QuickBar.IsValid) ? value
            : throw new ArgumentException("There are five further action bars, each twelve valid slots", nameof(ExtraBars));
    }

    private readonly ImmutableArray<ImmutableArray<string?>> _extraBars = EmptyExtraBars;

    internal static ImmutableArray<ImmutableArray<string?>> EmptyExtraBars { get; } =
        Enumerable.Repeat(Domain.Progression.QuickBar.Empty, Domain.Progression.QuickBar.Bars - 1).ToImmutableArray();

    /// <summary>The character's gear sets (schema 32), by place: named outfits of what is worn. A character before them has none.</summary>
    public ImmutableArray<GearSet> GearSets
    {
        get => _gearSets;
        init => _gearSets = GearSetRules.IsValid(value) ? value : throw new ArgumentException("Gear sets are at most ten, each place once, named, wearing each slot and item once", nameof(GearSets));
    }

    private readonly ImmutableArray<GearSet> _gearSets = ImmutableArray<GearSet>.Empty;

    /// <summary>
    /// What the player cleared off the quick bar on purpose (schema 23), sorted: the bar offers none of these a slot, at a login either,
    /// until the player puts it back - or, an item, until it is next picked up after leaving the pack.
    /// </summary>
    public ImmutableArray<string> QuickBarCleared
    {
        get => _quickBarCleared;
        init
        {
            var cleared = value.IsDefault ? throw new ArgumentNullException(nameof(QuickBarCleared)) : value.Order(StringComparer.Ordinal).ToImmutableArray();
            if (cleared.Any(id => !Domain.Progression.QuickBar.CanHold(id)) || cleared.Distinct(StringComparer.Ordinal).Count() != cleared.Length)
                throw new ArgumentException("A quick bar's cleared record holds items, formulas or techniques, each once", nameof(QuickBarCleared));
            _quickBarCleared = cleared;
        }
    }

    private readonly ImmutableArray<string> _quickBarCleared = ImmutableArray<string>.Empty;

    /// <summary>
    /// The party's sheets (WP15b), sorted by NPC: each past or present party member's progression, gear and belongings. Each NPC once; an item
    /// instance is held once - by one member, and never also carried by the character.
    /// </summary>
    public ImmutableArray<PartySheet> PartySheets
    {
        get => _partySheets;
        init
        {
            var sheets = value.IsDefault ? ImmutableArray<PartySheet>.Empty
                : value.Select(p => p with { Belongings = p.Belongings.OrderBy(e => e.ItemId.Value, StringComparer.Ordinal).ToImmutableArray() })
                    .OrderBy(p => p.NpcId, StringComparer.Ordinal).ToImmutableArray();
            foreach (var sheet in sheets)
            {
                sheet.Progression.Validate();
                if (!DefinitionId.IsValid(sheet.NpcId) || !PartyMemberKeys.IsMember(sheet.NpcId) || sheet.Gear.Values.Any(g => g is null)
                    || sheet.Items.Any(e => e.ItemId.Kind != EntityKind.Item || !DefinitionId.IsValid(e.DefId) || e.Count <= 0 || e.Notable is { IsValid: false } || e.Wear is { IsValid: false } || (e.Wear is not null && e.Count != 1)))
                    throw new ArgumentException($"Invalid party sheet for {sheet.NpcId}", nameof(PartySheets));
            }
            var items = sheets.SelectMany(p => p.Items).Select(e => e.ItemId).ToList();
            if (sheets.Select(p => p.NpcId).Distinct(StringComparer.Ordinal).Count() != sheets.Length || items.Distinct().Count() != items.Count
                || items.Intersect(Inventory.Select(e => e.ItemId)).Any())
                throw new ArgumentException("Each NPC has one party sheet, and an item instance is held once", nameof(PartySheets));
            _partySheets = sheets;
        }
    }

    private readonly ImmutableArray<PartySheet> _partySheets = ImmutableArray<PartySheet>.Empty;

    /// <summary>
    /// The attack the character had under way (schema 27), or null when none: a save mid-windup loads mid-windup. Its source is the weapon's
    /// own blow, bare hands' or a technique's; what it has struck are creatures, each once, sorted.
    /// </summary>
    public AttackRecord? Attack
    {
        get => _attack;
        init
        {
            var attack = value is null ? null : value with { Struck = value.Struck.IsDefault ? ImmutableArray<EntityId>.Empty : value.Struck.OrderBy(e => e.Value, StringComparer.Ordinal).ToImmutableArray() };
            if (attack is not null && (string.IsNullOrWhiteSpace(attack.Source) || attack.StartTick < 0
                || attack.Struck.Any(e => e.Kind != EntityKind.Creature) || attack.Struck.Distinct().Count() != attack.Struck.Length))
                throw new ArgumentException($"Invalid attack under way {attack}", nameof(Attack));
            _attack = attack;
        }
    }

    private readonly AttackRecord? _attack;

    /// <summary>
    /// What the character's experiments at stations have taught (schema 31), oldest first: one Finding for each question it closed. A question
    /// is closed once - the key of each is distinct - so an attempt cannot be farmed for more.
    /// </summary>
    public ImmutableArray<Finding> Findings
    {
        get => _findings;
        init
        {
            var findings = value.IsDefault ? ImmutableArray<Finding>.Empty : value;
            foreach (var f in findings)
            {
                if (string.IsNullOrWhiteSpace(f.StationKey) || string.IsNullOrWhiteSpace(f.StationKind) || f.Tick < 0 || f.Materials.IsDefault
                    || (f.Kind == FindingKind.Nothing) != (f.RecipeId is null) || f.Materials.Distinct(StringComparer.Ordinal).Count() != f.Materials.Length)
                    throw new ArgumentException($"Invalid finding {f.Key}", nameof(Findings));
            }
            if (findings.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != findings.Length)
                throw new ArgumentException("A question is closed by one Finding", nameof(Findings));
            _findings = findings;
        }
    }

    private readonly ImmutableArray<Finding> _findings = ImmutableArray<Finding>.Empty;

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
            Equipment.Where(kv => held.Contains(kv.Value)), Currency, Effects, Relationships, Conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions, Party = Party, QuickBar = QuickBar, QuickBarCleared = QuickBarCleared, PartySheets = PartySheets, Attack = Attack, Findings = Findings, SpaceId = SpaceId, SpaceTraversal = SpaceTraversal, ExtraBars = ExtraBars, GearSets = GearSets };
    }

    /// <summary>The same player with different progression (schema 4; the definition-ID pass rewrites its IDs too).</summary>
    public PlayerRecord WithProgression(CharacterProgression progression) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, Relationships, Conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions, Party = Party, QuickBar = QuickBar, QuickBarCleared = QuickBarCleared, PartySheets = PartySheets, Attack = Attack, Findings = Findings, SpaceId = SpaceId, SpaceTraversal = SpaceTraversal, ExtraBars = ExtraBars, GearSets = GearSets };

    /// <summary>The same player with different discovery records (schema 5; the definition-ID pass rewrites their location IDs).</summary>
    public PlayerRecord WithDiscoveries(IEnumerable<DiscoveryRecord> discoveries) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, discoveries, Equipment, Currency, Effects, Relationships, Conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions, Party = Party, QuickBar = QuickBar, QuickBarCleared = QuickBarCleared, PartySheets = PartySheets, Attack = Attack, Findings = Findings, SpaceId = SpaceId, SpaceTraversal = SpaceTraversal, ExtraBars = ExtraBars, GearSets = GearSets };

    /// <summary>The same player with different active effects (schema 7; the definition-ID pass rewrites their effect IDs).</summary>
    public PlayerRecord WithEffects(IEnumerable<ActiveEffect> effects) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, effects, Relationships, Conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions, Party = Party, QuickBar = QuickBar, QuickBarCleared = QuickBarCleared, PartySheets = PartySheets, Attack = Attack, Findings = Findings, SpaceId = SpaceId, SpaceTraversal = SpaceTraversal, ExtraBars = ExtraBars, GearSets = GearSets };

    /// <summary>The same player with different relationships and conversation memory (schema 10; the definition-ID pass rewrites their IDs).</summary>
    public PlayerRecord WithSocial(IEnumerable<RelationshipValue> relationships, IEnumerable<ConversationMemory> conversations) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, relationships, conversations, Quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions, Party = Party, QuickBar = QuickBar, QuickBarCleared = QuickBarCleared, PartySheets = PartySheets, Attack = Attack, Findings = Findings, SpaceId = SpaceId, SpaceTraversal = SpaceTraversal, ExtraBars = ExtraBars, GearSets = GearSets };

    /// <summary>The same player with different quests (schema 11; the definition-ID pass rewrites their quest IDs).</summary>
    public PlayerRecord WithQuests(IEnumerable<QuestState> quests) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, Relationships, Conversations, quests, Companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions, Party = Party, QuickBar = QuickBar, QuickBarCleared = QuickBarCleared, PartySheets = PartySheets, Attack = Attack, Findings = Findings, SpaceId = SpaceId, SpaceTraversal = SpaceTraversal, ExtraBars = ExtraBars, GearSets = GearSets };

    /// <summary>The same player with different companions (schema 12; the definition-ID pass rewrites their NPC IDs).</summary>
    public PlayerRecord WithCompanions(IEnumerable<CompanionRecord> companions) =>
        new(Id, Name, XMm, YMm, ZMm, AppearanceSeed, Inventory, Progression, FacingMdeg, Discoveries, Equipment, Currency, Effects, Relationships, Conversations, Quests, companions) { Posture = Posture, Waystones = Waystones, ReturnPoint = ReturnPoint, RoadsWalked = RoadsWalked, Deeds = Deeds, Identity = Identity, Factions = Factions, Party = Party, QuickBar = QuickBar, QuickBarCleared = QuickBarCleared, PartySheets = PartySheets, Attack = Attack, Findings = Findings, SpaceId = SpaceId, SpaceTraversal = SpaceTraversal, ExtraBars = ExtraBars, GearSets = GearSets };

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
    /// The space the character stands in (schema 20, M8 D10). Empty means the exterior, which is where every
    /// character was before the Barrowworks existed and where most still are - the same empty-name convention
    /// <see cref="SpaceId"/> uses everywhere else, so a save from before schema 20 reads as the exterior.
    /// </summary>
    /// <remarks>
    /// This is what makes a save taken inside the ring load back inside it. Before schema 16 the position was
    /// the only thing a save held about where a body was, and the ring's local coordinates sit inside the
    /// exterior grid - so a save taken in the Vestibule loaded the body into the Ashen Hollow at the same
    /// metres. The space is the missing half of the coordinate.
    /// </remarks>
    public string SpaceId
    {
        get => _spaceId;
        init
        {
            var name = value ?? "";
            if (name.Length > 0 && (!name.StartsWith("space.", StringComparison.Ordinal) || !DefinitionId.IsValid(name)))
                throw new ArgumentException($"A space is named space.<id>, got '{name}'", nameof(SpaceId));
            _spaceId = name;
        }
    }

    private readonly string _spaceId = "";

    /// <summary>
    /// The portal the character came into its space through (schema 20): null while it is in the exterior, or a
    /// <c>SpaceTraversal</c> portal key. This is the whole of the return rule - the design is explicit that the
    /// stored entry is not a reason to force a particular exit, so the portal is kept and no coordinate is.
    /// </summary>
    /// <remarks>
    /// Stored rather than derived because a body that saved mid-traversal must load mid-traversal, or saving would
    /// be a way to escape the rule that leaving goes back out of the portal you came in by.
    /// </remarks>
    public string? SpaceTraversal
    {
        get => _spaceTraversal;
        init
        {
            if (value is not null && (!value.StartsWith("portal.", StringComparison.Ordinal) || !DefinitionId.IsValid(value)))
                throw new ArgumentException($"A traversal is a portal.<id> key or null, got '{value}'", nameof(SpaceTraversal));
            _spaceTraversal = value;
        }
    }

    private readonly string? _spaceTraversal;

    /// <summary>The same player standing in a different space, traversing it differently (schema 20).</summary>
    public PlayerRecord WithSpace(string spaceId, string? traversal) =>
        this with { SpaceId = spaceId, SpaceTraversal = traversal };
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
                WearDigest.Add(NotableDigest.Add(h.Add(e.ItemId.Value).Add(e.DefId).Add(e.Count).Add(e.Quality), e.Notable), e.Wear);
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
                h.Add(PartyKeys.Key(c.Membership)).Add(PartyKeys.Key(c.Stance)).Add(c.Tactics.KeepToLeash ? 1 : 0).Add(c.Tactics.ProtectInjured ? 1 : 0)
                    .Add(c.Tactics.HoldHealing ? 1 : 0);
                if (c.Contract is { } k)
                {
                    h.Add(PartyKeys.Key(k.Role)).Add(k.Days).Add(k.DailyWage).Add(k.LootRule).Add(k.DaysServed).Add(k.UnpaidDays).Add(k.Purse)
                        .Add(k.WorkSite ?? "-").Add(k.RecipeId ?? "-").Add(k.Supply.Count);
                    foreach (var (item, count) in k.Supply)
                        h.Add(item).Add(count);
                }
                else
                    h.Add("no-contract");
                // Historical/home-space parties keep their digest. An away companion's space is authoritative too.
                if (c.Space is { } space)
                    h.Add("companion-space").Add(space);
            }
            h.Add(StanceKeys.Key(Posture.Stance)).Add(Posture.Airborne ? 1 : 0).Add(Posture.AirMs);
            h.Add(Waystones.Length);
            foreach (var w in Waystones)
                h.Add(w.StoneKey).Add(w.RegionId).Add(WaystoneMethods.Key(w.Method)).Add(w.Tick);
            h.Add(ReturnPoint ?? "-").Add(RoadsWalked.Length);
            foreach (string stretch in RoadsWalked)
                h.Add(stretch);
            // Schema 20: the space and the portal into it. Both are in the digest because two bodies at the same
            // metres in different spaces are in different places, and a digest that could not tell them apart
            // would let a load land in the wrong one without any test noticing.
            h.Add(SpaceId).Add(SpaceTraversal ?? "-");
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
            h.Add(Party.Recovering.Length);
            foreach (var r in Party.Recovering)
                h.Add(r.NpcId).Add(r.UntilTick);
            h.Add(Party.Fallen.Length);
            foreach (string fallen in Party.Fallen)
                h.Add(fallen);
            h.Add(QuickBar.Length);
            foreach (string? slot in QuickBar)
                h.Add(slot ?? "-");
            h.Add(QuickBarCleared.Length);
            foreach (string cleared in QuickBarCleared)
                h.Add(cleared);
            // The further bars and the gear sets (schema 32) only when there are any, so every older record digests as it always has.
            if (ExtraBars.Any(b => b.Any(slot => slot is not null)))
            {
                h.Add("extra-bars");
                foreach (var bar in ExtraBars)
                    foreach (string? slot in bar)
                        h.Add(slot ?? "-");
            }
            if (!GearSets.IsEmpty)
            {
                h.Add("gear-sets").Add(GearSets.Length);
                foreach (var set in GearSets)
                {
                    h.Add(set.Index).Add(set.Name).Add(set.Items.Length);
                    foreach (var item in set.Items)
                        h.Add((int)item.Slot).Add(item.Item.Value).Add(item.DefId);
                }
            }
            // The party's sheets (WP15b) only when there are any, so every older record digests as it always has.
            if (!PartySheets.IsEmpty)
            {
                h.Add("party-sheets").Add(PartySheets.Length);
                foreach (var sheet in PartySheets)
                {
                    h.Add(sheet.NpcId).Add(sheet.Progression.Digest).Add(sheet.Gear.Count);
                    foreach (var (slot, gear) in sheet.Gear)
                        WearDigest.Add(NotableDigest.Add(h.Add(EquipSlots.Key(slot)).Add(gear.Loaned ? 1 : 0).Add(gear.Item.ItemId.Value).Add(gear.Item.DefId)
                            .Add(gear.Item.Count).Add(gear.Item.Quality), gear.Item.Notable), gear.Item.Wear);
                    h.Add(sheet.Belongings.Length);
                    foreach (var e in sheet.Belongings)
                        WearDigest.Add(NotableDigest.Add(h.Add(e.ItemId.Value).Add(e.DefId).Add(e.Count).Add(e.Quality), e.Notable), e.Wear);
                }
            }
            // The attack under way (schema 27) only when there is one, so every older record digests as it always has.
            if (Attack is { } attack)
            {
                h.Add("attack").Add(attack.Source).Add(attack.StartTick).Add(attack.Struck.Length);
                foreach (var struck in attack.Struck)
                    h.Add(struck.Value);
            }
            // The Findings (schema 31) only when there are some, so every older record digests as it always has.
            if (!Findings.IsEmpty)
            {
                h.Add("findings").Add(Findings.Length);
                foreach (var f in Findings)
                {
                    h.Add(f.Key).Add(FindingKeys.Key(f.Kind)).Add(f.StationKind).Add(f.Tick).Add(f.Materials.Length);
                    foreach (string material in f.Materials)
                        h.Add(material);
                }
            }
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

/// <summary>
/// Who can stand in the party's records (relationships, the party, its ledger, its sheets): a named NPC (<c>npc.</c>), or - schema 28,
/// WP12b - a role resident, known by their slot's key (<c>routine.</c>), since residents have no NPC file of their own.
/// </summary>
public static class PartyMemberKeys
{
    public static bool IsMember(string id) =>
        DefinitionId.IsValid(id) && (id.StartsWith("npc.", StringComparison.Ordinal) || id.StartsWith("routine.", StringComparison.Ordinal));
}
