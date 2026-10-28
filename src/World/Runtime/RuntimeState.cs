// UNNAMED World - authoritative runtime state and who may write it (ARCHITECTURE.md §5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Knowledge;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// The typed slices of authoritative runtime state. Every slice has exactly one owning system, claimed at
/// composition; a write by any other system is refused (ARCHITECTURE.md §5 "every component key is claimed
/// by exactly one system").
/// </summary>
public enum StateSlice
{
    /// <summary>The current camp rest posture and death-screen choice. Transient, never saved.</summary>
    CampSession,
    /// <summary>The timed activity the character is in the middle of - a cast, a wait, a reel, a pick working a lock. Transient, never saved.</summary>
    Activity,
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

    /// <summary>How many times the character has done each deed (E-7), which dialogue's <c>deed</c> condition reads. Saved with the player (schema 16).</summary>
    Deeds,

    /// <summary>
    /// The weather's seed cursor: the game hour the front last announced began (WP7). The weather itself is derived from the world seed, the
    /// region and the hour, so nothing else of it is state.
    /// </summary>
    Weather,
    /// <summary>The character's faction ledger (WP6): the act log, what each faction knows of it, and the standing each holds. Saved with the player (schema 20).</summary>
    Factions,

    /// <summary>The town markets' stock and sell history, the goods on the road, and the economy's day (S-36; WP14). In the world delta.</summary>
    Markets,

    /// <summary>The merchants' purses and restocks (S-36; WP14). In the world delta.</summary>
    Merchants,
    /// <summary>What is on each slot of the six action bars (WP9; the quick bar is the first).</summary>
    QuickBar,

    /// <summary>The character's saved gear sets (named outfits of the equipment). Saved with the player.</summary>
    GearSets,

    /// <summary>Each past or present party member's progression, gear and belongings (WP15b). Saved with the player.</summary>
    PartySheets,

    /// <summary>Open offers to set out together and the temporary-ally groups that formed (WP16). Transient: never saved.</summary>
    Lfg,

    /// <summary>Every NPC's life - needs, activity, condition, pose and route - the role slots' arrivals and the pairs (WP12). In the world delta.</summary>
    NpcLife,

    /// <summary>Every NPC adventurer's life and the slots awaiting arrivals (WP13). In the world delta.</summary>
    Adventurers,

    /// <summary>What the character's experiments at stations taught (UI progression F). Saved with the player (schema 31).</summary>
    Findings,

    /// <summary>The lore pages the character has unlocked (spec 41). Saved with the player (schema 35).</summary>
    Lore,

    /// <summary>The animals that travel with the character (spec 43). Saved with the player (schema 36).</summary>
    Pets,

    /// <summary>The Orenth crossing record: the hidden toll, its costs and the planar anchor (RACE_ABILITIES_PROPOSAL.md §3.4). Saved with the player (schema 40).</summary>
    Crossing,
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
        Deeds = player.Deeds.ToImmutableSortedDictionary(d => d.Key, d => d, StringComparer.Ordinal);
        Factions = player.Factions;
        Recovering = player.Party.Recovering.ToImmutableSortedDictionary(r => r.NpcId, r => r.UntilTick, StringComparer.Ordinal);
        Fallen = player.Party.Fallen.ToImmutableSortedSet(StringComparer.Ordinal);
        QuickBar = player.QuickBar;
        ExtraBars = player.ExtraBars;
        GearSets = player.GearSets;
        Bags = player.Bags;
        QuickBarCleared = player.QuickBarCleared.ToImmutableSortedSet(StringComparer.Ordinal);
        HeldFamily = player.HeldFamily;
        FamilyBars = player.FamilyBars;
        PartySheets = player.PartySheets.ToImmutableSortedDictionary(p => p.NpcId, p => p, StringComparer.Ordinal);
        Findings = player.Findings;
        Lore = player.Lore;
        PeopleId = player.Identity.PeopleId;
        Crossing = player.Crossing;
        // The pets are placed by their own system, which reads the species and the body each one stands on out of content (spec 43).
    }

    public WorldDelta World { get; }
    public long WorldTick { get; private set; }

    /// <summary>The character's people, as the creator made them: fixed for the run.</summary>
    public string PeopleId { get; }

    public Crossing Crossing { get; private set; }
    public Body Body { get; private set; }
    public Body? CampRestAt { get; private set; }
    public bool ReturnToCamp { get; private set; }

    public void SetCampSession(SliceOwner owner,Body? resting,bool returnToCamp)
    {
        Require(owner,StateSlice.CampSession);
        CampRestAt=resting;
        ReturnToCamp=returnToCamp;
    }
    /// <summary>The cast, wait, reel or lock being worked now, or null. Transient: one interrupted by a save is dropped, not saved.</summary>
    public PlayerActivity? Activity { get; private set; }
    public void SetActivity(SliceOwner owner,PlayerActivity? activity)
    {
        Require(owner,StateSlice.Activity);
        Activity=activity;
    }
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
    public ImmutableSortedDictionary<string, DeedTally> Deeds { get; private set; }
    public ImmutableArray<Finding> Findings { get; private set; }
    public ImmutableArray<LorePageRecord> Lore { get; private set; }

    /// <summary>The animals that travel with the character, by instance (spec 43; part of the pets' slice, saved with the player).</summary>
    public ImmutableSortedDictionary<string, PetState> Pets { get; private set; } =
        ImmutableSortedDictionary.Create<string, PetState>(StringComparer.Ordinal);
    public long? WeatherCursor { get; private set; }
    public LfgState Lfg { get; private set; } = LfgState.Empty;
    public FactionLedger Factions { get; private set; }

    /// <summary>Companions gravely wounded, recovering at home, and the tick they are well again (WP15a; part of the companions' slice).</summary>
    public ImmutableSortedDictionary<string, long> Recovering { get; private set; }

    /// <summary>Hirelings who died in the character's service (DN9; part of the companions' slice): never put back in the world.</summary>
    public ImmutableSortedSet<string> Fallen { get; private set; }
    public ImmutableArray<string?> QuickBar { get; private set; }

    /// <summary>Action bars 2 to 6 (part of the quick bar's slice).</summary>
    public ImmutableArray<ImmutableArray<string?>> ExtraBars { get; private set; }

    /// <summary>The character's gear sets, by place.</summary>
    public ImmutableArray<GearSet> GearSets { get; private set; }

    /// <summary>The bags equipped in the bag slots: the carried bag in each, or null (schema 33). Owned with the inventory.</summary>
    public ImmutableArray<EntityId?> Bags { get; private set; }

    /// <summary>The weapon family whose layout the main bar shows ("" until the first tick adopts the one held) - part of the quick bar's slice (schema 34).</summary>
    public string HeldFamily { get; private set; } = "";

    /// <summary>The main bar's layout for each family held so far; the held family's is the main bar itself (schema 34).</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<string?>> FamilyBars { get; private set; } = Domain.Progression.WeaponFamilies.None;

    /// <summary>The six action bars, the quick bar first.</summary>
    public ImmutableArray<ImmutableArray<string?>> ActionBars => ExtraBars.Insert(0, QuickBar);

    /// <summary>What the player cleared off the quick bar on purpose (part of the quick bar's slice): not offered a slot while held.</summary>
    public ImmutableSortedSet<string> QuickBarCleared { get; private set; }

    /// <summary>Each past or present party member's progression, gear and belongings, by NPC (WP15b).</summary>
    public ImmutableSortedDictionary<string, PartySheet> PartySheets { get; private set; }
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

    public void SetCrossing(SliceOwner owner, Crossing crossing)
    {
        Require(owner, StateSlice.Crossing);
        Crossing = crossing.Problem() is { } problem ? throw new InvalidOperationException($"Invalid crossing record: {problem}") : crossing;
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

    public void SetDeed(SliceOwner owner, DeedTally tally)
    {
        Require(owner, StateSlice.Deeds);
        Deeds = Deeds.SetItem(tally.Key, tally);
    }

    /// <summary>The character's points with a faction; 0 when nothing moved them.</summary>
    public int StandingOf(string factionId) => FactionRules.PointsOf(Factions, factionId);

    public void SetFactions(SliceOwner owner, FactionLedger ledger)
    {
        Require(owner, StateSlice.Factions);
        Factions = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public void SetQuickBar(SliceOwner owner, ImmutableArray<string?> bar)
    {
        Require(owner, StateSlice.QuickBar);
        if (!Domain.Progression.QuickBar.IsValid(bar))
            throw new InvalidOperationException("A quick bar is twelve slots, each empty or holding one item, formula, technique or gear-set switch once");
        QuickBar = bar;
    }

    public void SetFindings(SliceOwner owner, ImmutableArray<Finding> findings)
    {
        Require(owner, StateSlice.Findings);
        Findings = findings;
    }

    public void SetLore(SliceOwner owner, ImmutableArray<LorePageRecord> lore)
    {
        Require(owner, StateSlice.Lore);
        Lore = lore;
    }

    /// <summary>Replace action bar <paramref name="bar"/> (0 to 5; 0 is the quick bar).</summary>
    public void SetActionBar(SliceOwner owner, int bar, ImmutableArray<string?> slots)
    {
        if (bar == 0)
        {
            SetQuickBar(owner, slots);
            return;
        }
        Require(owner, StateSlice.QuickBar);
        if (bar is < 0 or >= Domain.Progression.QuickBar.Bars || !Domain.Progression.QuickBar.IsValid(slots))
            throw new InvalidOperationException("An action bar is twelve slots, each empty or holding one item, formula, technique or gear-set switch once");
        ExtraBars = ExtraBars.SetItem(bar - 1, slots);
    }

    public void SetGearSets(SliceOwner owner, ImmutableArray<GearSet> sets)
    {
        Require(owner, StateSlice.GearSets);
        if (!GearSetRules.IsValid(sets))
            throw new InvalidOperationException("Gear sets are at most ten, each place once, named, wearing each slot and item once");
        GearSets = sets;
    }

    /// <summary>The main bar and its per-family layouts together: the held family, and every family's layout (the held one's is <paramref name="bar"/>).</summary>
    public void SetMainBar(SliceOwner owner, string family, ImmutableArray<string?> bar, ImmutableSortedDictionary<string, ImmutableArray<string?>> families)
    {
        Require(owner, StateSlice.QuickBar);
        if (family.Length > 0 && !Domain.Progression.WeaponFamilies.IsValid(family) || !Domain.Progression.QuickBar.IsValid(bar)
            || families.Any(f => !Domain.Progression.WeaponFamilies.IsValid(f.Key) || !Domain.Progression.QuickBar.IsValid(f.Value)))
            throw new InvalidOperationException("The main bar and each family's layout are twelve valid slots under a valid family");
        (HeldFamily, QuickBar, FamilyBars) = (family, bar, families);
    }

    public void SetQuickBarCleared(SliceOwner owner, ImmutableSortedSet<string> cleared)
    {
        Require(owner, StateSlice.QuickBar);
        QuickBarCleared = cleared;
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

    public void SetLfg(SliceOwner owner, LfgState lfg)
    {
        Require(owner, StateSlice.Lfg);
        Lfg = lfg ?? throw new ArgumentNullException(nameof(lfg));
    }

    public void SetWeatherCursor(SliceOwner owner, long? startHour)
    {
        Require(owner, StateSlice.Weather);
        WeatherCursor = startHour;
    }

    /// <summary>World time passing at once (the offline catch-up): the clock moves on by whole ticks.</summary>
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

    public void SetBags(SliceOwner owner, ImmutableArray<EntityId?> bags)
    {
        Require(owner, StateSlice.PlayerInventory);
        if (bags.Length != UNNAMED.Domain.Items.PackLimits.BagSlots)
            throw new InvalidOperationException("The pack has a fixed number of bag slots");
        Bags = bags;
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

    public void PlaceItem(SliceOwner owner, CellKey cell, EntityId itemId, string defId, int count, int xCm, int zCm, int quality = 0,
        NotableRecord? notable = null, ItemWear? wear = null)
    {
        Require(owner, StateSlice.WorldItems);
        World.PlaceItem(cell, itemId, defId, count, xCm, zCm, quality, notable, wear);
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

    public void SetEconomy(SliceOwner owner, EconomyState economy)
    {
        Require(owner, StateSlice.Markets);
        World.SetEconomy(economy);
    }

    public void SetMerchant(SliceOwner owner, MerchantState merchant)
    {
        Require(owner, StateSlice.Merchants);
        World.SetMerchant(merchant);
    }

    public void SetCompanion(SliceOwner owner, CompanionState companion)
    {
        Require(owner, StateSlice.Companions);
        Companions = Companions.SetItem(companion.NpcId, companion);
    }

    public void SetPet(SliceOwner owner, PetState pet)
    {
        Require(owner, StateSlice.Pets);
        Pets = Pets.SetItem(pet.Key, pet);
    }

    /// <summary>Every pet at once - the world starting from a save, and nothing else.</summary>
    public void SetPets(SliceOwner owner, ImmutableSortedDictionary<string, PetState> pets)
    {
        Require(owner, StateSlice.Pets);
        Pets = pets;
    }

    /// <summary>An animal stops being a spawner's creature (spec 43's taming): it is the character's now, or it was taken and is gone.</summary>
    public void RemoveCreature(SliceOwner owner, string key)
    {
        Require(owner, StateSlice.Creatures);
        Creatures = Creatures.Remove(key);
    }

    /// <summary>Someone leaves the party (WP15a): dismissed, their contract over, gravely wounded or dead.</summary>
    public void RemoveCompanion(SliceOwner owner, string npcId)
    {
        Require(owner, StateSlice.Companions);
        Companions = Companions.Remove(npcId);
    }

    /// <summary>A companion recovering at home until a tick; null: well again.</summary>
    public void SetRecovering(SliceOwner owner, string npcId, long? untilTick)
    {
        Require(owner, StateSlice.Companions);
        Recovering = untilTick is { } until ? Recovering.SetItem(npcId, until) : Recovering.Remove(npcId);
    }

    public void SetPartySheet(SliceOwner owner, PartySheet sheet)
    {
        Require(owner, StateSlice.PartySheets);
        PartySheets = PartySheets.SetItem(sheet.NpcId, sheet);
    }

    public void RemovePartySheet(SliceOwner owner, string npcId)
    {
        Require(owner, StateSlice.PartySheets);
        PartySheets = PartySheets.Remove(npcId);
    }

    public void AddFallen(SliceOwner owner, string npcId)
    {
        Require(owner, StateSlice.Companions);
        Fallen = Fallen.Add(npcId);
    }

    public void SetNpcLife(SliceOwner owner, NpcLifeState life)
    {
        Require(owner, StateSlice.NpcLife);
        World.SetNpcLife(life);
    }

    public void SetAdventurers(SliceOwner owner, AdventurerState adventurers)
    {
        Require(owner, StateSlice.Adventurers);
        World.SetAdventurers(adventurers);
    }

    /// <summary>An NPC is gone from the world: a hireling who died (DN9).</summary>
    public void RemoveNpc(SliceOwner owner, string npcId)
    {
        Require(owner, StateSlice.Npcs);
        Npcs = Npcs.Remove(npcId);
    }

    private void Require(SliceOwner owner, StateSlice slice)
    {
        if (!owner.Slices.Contains(slice) || _owners.GetValueOrDefault(slice) != owner.System)
            throw new InvalidOperationException($"{owner.System} wrote {slice}, which {_owners.GetValueOrDefault(slice) ?? "no system"} owns");
    }
}
