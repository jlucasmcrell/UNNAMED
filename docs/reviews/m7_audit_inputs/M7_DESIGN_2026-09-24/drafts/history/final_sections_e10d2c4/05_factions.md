## 5. Factions and reputation v1

This section owns the behaviour behind the faction names that section 6 catalogues: the act log, faction knowledge, standing, the two gates, the faction content and its lint, and the reputation fixture table. Section 6 owns the names, the dispatch order and the guards G1-G28; section 7 owns the wire format; section 8 owns the HUD line and the F6 panel.

### Decisions

- One system, `FactionSystem`, owns one slice, `StateSlice.Factions`. It handles two internal commands, `RecordAct` and `ReportAct`, and has no `Tick` and no `Seed`.
- Knowledge is **report-only** in M7. The only writer of knowledge and standing is `ReportAct`, sent by a dialogue `report_act` consequence.
- Acts (what happened) are recorded apart from knowledge (who knows). A recorded act moves nothing until a faction is told.
- Two act kinds are built: `creature_killed` and `switch_set`. Every other kind is a reserved key. Building records no act.
- An act's identity is a persisted sequence number, `Seq`, and never an `EntityId`.
- Knowledge rows keep the string keys `source`, `via` and `identity`, which are always `reported` / `identified` in M7. They are the stored seam for the M9 witnessed channel.
- There are two working factions: **the Waystation** (Renn Vale, Kera Voss) and **the Survey** (Sel Arien). Tavar Orr belongs to neither. The names are working names, not canon.
- The proof act is destroying the Blackvein Animated Armour: Waystation +100, Survey −100. Steadying the Foldscar heart gives the Survey +100.
- The ladder is PROGRESSION §10's 11 tiers on points [−1000, 1000], with an ordinary floor of −999. The tier comes from `StandingLadder.StandingTierOf` and is never stored.
- There are two gates, both at `accepted`: Sel's `notes` reply (dialogue) and Kera's iron-billet stock row (service, enforced in `TradeSystem`).
- Membership is the static NPC `faction_ref`. It is not saved, and the player is never a member.
- Faction relations are static, directional attitude words. Only views read them.
- There are **no hostility thresholds**. A reflection check keeps tactical code from reading faction state.
- All faction state lives on the player record, in the `player.msgpack` `factions` field (schema 14, `unnamed.player/v10`).
- The act log holds 256 acts and evicts the oldest first.
- Code never connects personal relationships and standing. Content may put both consequences on one reply.
- M7 adds no player command, no faction screen and no radial.

### 5.1 Minimum entities and data

| Candidate | In M7? | Form |
|---|---|---|
| FactionDefinition | Yes | Content `kind: faction` (already registered, `src/Content/SchemaResolution.cs:158-163`): `name`, `seat_location_ref`, `reactions`, `relations`. Other DATA_MODEL §4.13 fields are refused by name (§5.14) |
| FactionRelationship | Static data only | `relations: [{ faction_ref, attitude }]` with closed attitude words. No rule reads it; `FactionView` shows it |
| ReputationRecord | Yes | `FactionStanding(FactionId, Points)` on the player. Tier derived when read; 0 not stored |
| KnownAct | Yes, as two records | `ActRecord` (truth: what, where, when) and `FactionKnowledge` (belief: which faction knows, by which channel and member, and whether it knows who) |
| Value or tag preferences | No | A faction's values are its reaction rows: one act kind, one exact subject, one delta |
| Membership | Yes, static | Optional NPC `faction_ref`. The faction lists no members; `FactionView.Members` is derived from `Setup.Social.Npcs` when read |
| Hostility thresholds | **None** | No field can hold one. Standing is access only (`docs/PROGRESSION.md:444-452`) |

A reaction names a world act, never a quest, and quests have no reputation reward, so "quest X gives +10 to faction A" cannot be expressed.

#### 5.1.1 Domain types (`src/Domain/Factions/Factions.cs`, new, namespace `UNNAMED.Domain.Factions`)

```csharp
public static class ActKinds
{
    public const string CreatureKilled = "creature_killed";   // subject: the creature definition ID
    public const string SwitchSet = "switch_set";             // subject: the world.* flag the switch set
    public static readonly ImmutableArray<string> Built = ImmutableArray.Create(CreatureKilled, SwitchSet);
    /// Named and refused, each with what it waits for (the ObjectiveTypes.NotBuilt pattern).
    public static readonly ImmutableSortedDictionary<string, string> NotBuilt = new Dictionary<string, string>
    {
        ["piece_placed"]    = "a repeat rule: placement consumes materials, so a reaction would turn items into standing (E-7); M9",
        ["piece_destroyed"] = "a repeat rule (M9)",
        ["item_taken"]      = "ownership (crime, Phase 3)",
        ["npc_harmed"]      = "NPCs that can be harmed",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}

public static class KnowledgeSources { public const string Witnessed = "witnessed", Reported = "reported"; public static readonly ImmutableArray<string> All = [Witnessed, Reported]; }
public static class Identities       { public const string Unidentified = "unidentified", Identified = "identified"; public static readonly ImmutableArray<string> All = [Unidentified, Identified]; }
public static class Attitudes        { public static readonly ImmutableArray<string> All = ["close", "cordial", "indifferent", "strained", "opposed"]; }

public sealed record ActRecord(long Seq, string Kind, string Subject, string CellKey, long XMm, long ZMm, long Tick);

public sealed record FactionKnowledge(
    string Knower,     // a faction ID in M7; the key is "knower" so a later per-NPC row fits the same list
    long Act,          // ActRecord.Seq
    string Identity,   // Identities: always "identified" when M7 writes it
    string Source,     // KnowledgeSources: always "reported" when M7 writes it; the channel of the row's latest change
    string? Via,       // the member told; null only after the definition pass removed that NPC
    long Tick,         // the tick of the row's latest change
    int Delta);        // the standing change this row caused, after the clamp; 0 while unidentified

public sealed record FactionStanding(string FactionId, int Points);   // Points != 0

public sealed record FactionLedger(long NextActSeq, ImmutableArray<ActRecord> Acts,
    ImmutableArray<FactionKnowledge> Knowledge, ImmutableArray<FactionStanding> Standing)
{
    public const int MinPoints = -1000, MaxPoints = 1000, OrdinaryFloor = -999;
    public static FactionLedger Empty { get; } = new(1, [], [], []);
    public FactionLedger WithAct(ActRecord act);                 // appends; NextActSeq = act.Seq + 1
    public FactionLedger WithKnowledge(FactionKnowledge row);    // inserts or replaces the (Knower, Act) row, keeping canonical order
    public FactionLedger WithPoints(string factionId, int points); // 0 removes the row
    public FactionLedger WithoutAct(long seq);                   // removes the act and its knowledge rows; standing untouched
    // Equals/GetHashCode compare the three arrays with SequenceEqual (G28). No computed public instance property (StateDump contract).
}

public sealed record Reaction(string Kind, string Subject, int Delta);
public sealed record Relation(string FactionId, string Attitude);
public sealed record FactionDefinition(string Id, string Name, string SeatLocationId,
    ImmutableArray<Reaction> Reactions, ImmutableArray<Relation> Relations);   // no rule reads SeatLocationId (lint FAC-M1; FactionView shows it)

public sealed record StandingTier(string Key, int Level, int MinPoints);
public sealed record StandingLadder(ImmutableArray<StandingTier> Tiers, int MinPoints, int MaxPoints, int OrdinaryFloor)
{
    /// PROGRESSION §10's keys and levels, top down. FAC001 pins config.factions to exactly these.
    public static readonly ImmutableArray<(string Key, int Level)> Keys = [("exalted", 5), ("allied", 4), ("honoured", 3),
        ("trusted", 2), ("accepted", 1), ("neutral", 0), ("wary", -1), ("disliked", -2), ("despised", -3), ("outcast", -4), ("anathema", -5)];
    public static StandingLadder Default { get; }            // the §5.4.1 numbers; used by FactionSetup.Empty
    public static int LevelOf(string tierKey);                // throws FormatException on an unknown key
    public StandingTier StandingTierOf(int points);           // the first tier, top down, whose MinPoints <= points
}

public sealed record StandingRequirement(string FactionId, int MinLevel);   // a gated stock row (§5.7.2)

public sealed record Learned(string FactionId, long ActSeq, string Source, string? Via, string Identity, bool Upgraded, int From, int To);

public static class FactionRules
{
    public static (FactionLedger Ledger, Learned? Change) Learn(FactionLedger ledger, FactionDefinition faction, long actSeq,
        string source, string via, string identity, long tick, StandingLadder ladder);          // §5.4.3
    public static (FactionLedger Ledger, ImmutableArray<long> Evicted) Compact(FactionLedger ledger, int capacity);   // §5.4.5
    public static int PointsOf(FactionLedger ledger, string factionId);                           // 0 when absent
}
```

Elsewhere in Domain:
- `src/Domain/Social/Social.cs`:
  - `NpcDefinition` gains `public string? FactionId { get; init; }`.
  - New types: `ReputationCondition(string FactionId, int MinLevel, int MaxLevel) : DialogueCondition`, `ActDoneCondition(string Kind, string Subject) : DialogueCondition` and `ReportActConsequence(string Kind, string Subject) : DialogueConsequence`.
  - `IDialogueFacts` gains `int StandingLevel(string factionId)` and `bool ActDone(string kind, string subject)`.
- `src/Domain/Items/Items.cs`: `MerchantStock` gains `public StandingRequirement? Requires { get; init; }`.

#### 5.1.2 Invariants

`PlayerRecord` validates the ledger in its `Factions` init setter and throws `ArgumentException`, as `Posture` does (`src/World/PlayerState.cs:306-317`):
- `NextActSeq ≥ 1`.
- Acts:
  - strictly ascending by `Seq`, each with `1 ≤ Seq < NextActSeq`;
  - `Kind` is in `ActKinds.Built`, and `Subject` is a valid `DefinitionId`;
  - `CellKey == CellKey.OfWorld(XMm / 1000.0, ZMm / 1000.0).ToString()` (L5).
- Knowledge:
  - sorted by (`Knower` ordinal, `Act` ascending), with at most one row per pair;
  - every `Act` is present in `Acts`, and `Knower` is a valid `faction.*` ID;
  - `Identity` is in `Identities.All`, and `Source` is in `KnowledgeSources.All`;
  - `Via` is null or a valid `npc.*` ID;
  - `Delta == 0` when `Identity == unidentified`.
- Standing: sorted by `FactionId` ordinal, unique, `Points ≠ 0`, and within [`MinPoints`, `MaxPoints`].

Every sorted collection M7 builds from content uses `StringComparer.Ordinal` (G24).

### 5.2 The act: from a gameplay act to a faction input

#### 5.2.1 The command

```csharp
// src/World/Runtime/Factions.cs (namespace UNNAMED.World.Runtime)
/// To FactionSystem: the character did something a faction may react to, standing here, now.
internal sealed record RecordAct(string Kind, string Subject, long XMm, long ZMm) : InternalCommand;
```

`RecordAct` is synchronous, the `RecordDeed` pattern (`src/World/Runtime/Creatures.cs:705-706`). `Simulation.Dispatch` routes it to `_factions.Handle(a, Now)`. It writes the act log only: **no knowledge and no standing**. It returns null, and a dispatcher ignores the result.

The dispatch sites pass `ActKinds.CreatureKilled` and `ActKinds.SwitchSet`. These are `public const string`, so the compiler inlines them and no member reference into `UNNAMED.Domain.Factions` reaches the IL of a tactical file. `TacticalCode_NeverReadsFactionState` (G6) therefore stays green. They must never become `static readonly`.

`Deed` is not reused. It is a quest counter with quest-only kinds and no position, and M5 stays untouched.

#### 5.2.2 Dispatch sites

| Kind | Subject | Site | Exact hook | Position |
|---|---|---|---|---|
| `creature_killed` | the creature definition ID | `CreatureSystem.Die` | inside the existing `if (killer == _player)` block, immediately after the `RecordDeed` dispatch (`Creatures.cs:705-706`). The `if` becomes braced | the player's body (`State.Body`) at dispatch |
| `switch_set` | `site.FlagId`, the `world.*` flag the switch set | `InteractionSystem.Work` | after the accepted `SetWorldFlag` (`src/World/Runtime/Systems.cs:288`), before `SwitchSet` is published (`:290`) | the player's body |

Rules:
- A blow kill happens in `_combat.Tick`; a damage-over-time kill in `_effects.Tick` (`Harm` → `HarmCreature` → `Die`). Both are deterministic.
- **A companion's kill is not the player's act**: it is outside the `killer == _player` block, as in M6.
- **The subject of `switch_set` is the flag**, a definition ID that the `flag_ref` walker and the alias pass cover; a switch key is layout and covered by neither.
- Dialogue `set_world_flag` and quest `world_flag` rewards are not acts; they are content bookkeeping.
- **The position is the actor's body, never the effect's.** No M7 rule reads it; it is stored for the M9 witnessed channel and for crime, where seeing the actor identifies and seeing only the effect is evidence.
- `BuildingSystem` never dispatches `RecordAct` (G13).
- Deliberately not act kinds: trade (E-7: coin never buys standing), quest completion (factions react to the world act the quest required), dialogue choices, container takes (ownership is crime's), and building. A reaction to placement would turn materials into standing through place-dismantle loops (E-7) until a repeat rule exists; nothing needs it for the exit; and adding `piece_placed` later is one `Built` entry, one dispatch line and one lint rule, with no migration.

#### 5.2.3 The relevance filter

`FactionSetup.IsRelevant(kind, subject)` is true when some faction has a reaction row for the pair. `Handle(RecordAct)` returns at once, recording nothing, for an irrelevant pair; a wolf kill is not recorded.

In Ashen Hollow the set has exactly two pairs, both single-instance, so shipped play records at most two acts:
- (`creature_killed`, `creature.construct.animated_armour`): its only spawner, `spawn.hollow.iron_shelf_armour`, has `respawn: { kind: none }`;
- (`switch_set`, `world.foldscar.steadied`): the switch sets once (`Systems.cs:284`).

Relevance is fixed at boot: content added later cannot react to an act recorded before it existed (the quest-deed rule).

#### 5.2.4 Identity and ordering

- **Identity is `Seq`** from the persisted `NextActSeq` (starts at 1, +1 per recorded act). Acts are history rows, not registry entities, so D-04's ULID rule does not apply (precedent: `harvest_seq`). A wall-clock `EntityId.NewId` would differ between a session and its command-log replay and break `StateDigest` equality.
- **Order is dispatch order**, fully determined: FIFO drain at a boundary, the fixed `Step` order, synchronous internal commands, ordinal sorted collections.
- Inside the faction code: factions in ordinal ID order, acts in ascending `Seq`, at most one reaction row per (faction, kind, subject) (lint). No result depends on event handling or dictionary enumeration order (G5).

#### 5.2.5 The act record's fields

| Asked for | Where it lives | M7 content |
|---|---|---|
| Actor identity | Implicitly the player: the ledger is the player's, and every `GameCommand` handler refuses a non-player actor | Not stored as a field. When NPC acts arrive, the log moves to a world-global record and gains `actor`; that migration fills in the player's ID, which is in the save |
| Actor known or unknown | `FactionKnowledge.Identity` | Always `identified` in M7, because a report names its teller |
| Location and cell | `ActRecord.XMm`, `ZMm` (integer mm) and `CellKey` (checked against x and z on load) | the player's body |
| Tick | `ActRecord.Tick` = `Now` at dispatch | |
| Witnesses | None in M7. `FactionKnowledge.Via` names the member who was told | The M9 witnessed rows use the same field |
| Sequence | `ActRecord.Seq` | |

### 5.3 Knowledge: when a faction knows (report-only)

#### 5.3.1 The rules

The K numbers are kept from the working draft; K2-K4 are the M9 extension (§5.3.4).

- **K1 - one channel.** In M7 knowledge rows are created or changed only by K5.
- **K5 - reported.** When a reply carrying `report_act (kind, subject)` is chosen in a conversation with NPC S, whose faction is F, F learns every act in the log with that (kind, subject), in ascending `Seq`, through `FactionRules.Learn` with `source = reported`, `via = S`, `identity = identified`. The actor names themselves, so a report always identifies. An act F already knows as identified is unchanged; a `report_act` with no matching act does nothing.
- **K6 - pooled at the tick.** When a member is told, the faction knows at that tick. No delay, no per-member memory.
- **K7 - no row, nothing stored.** A faction without a reaction row for the act gets no knowledge row. Lint R4 keeps content from reaching this case; `Learn_WithoutARow_StoresNothing` proves the rule.
- **K8 - idempotent, one upgrade.** A second report of an act already known as identified changes and publishes nothing. An `unidentified` row upgrades to `identified` exactly once.
- **K9 - no other writer.** Nothing but K5 writes knowledge, and nothing but `FactionRules.Learn` (called from K5) writes standing. No faction learns from another; relations move nothing.
- **K10 - relevance.** An act outside the relevant set is never recorded, so no faction can learn it.

**Unknown actor.** Not reachable in M7, because a report always identifies. The `identity` key is the seam: unit row U1 (§5.15) and crafted-save row F4 (`AnUnknownActor_MovesNoStanding_UntilIdentified`) prove an `unidentified` row applies no delta until identified, then applies it once.

**Scripted transfer** is not a third mechanism: it is a `report_act` consequence on a reply said to a member. By R4 it informs only the speaker's own faction, and only when that faction reacts to the act; a consequence informing a faction nobody in the conversation belongs to cannot be written.

**Propagation.** None beyond K6: no NPC-to-NPC telling, no rumour, no reports in flight, no transfer between factions.

#### 5.3.2 Why pooling is honest here

"Same faction does not imply instant shared awareness" (`docs/STEALTH_DETECTION_AND_THREAT.md:100`). K6 is M7's one stated approximation, made checkable by two lints:
- **FAC-M1 (seat).** Every faction names a `seat_location_ref`, and every member's authored placement lies inside that location's discovery radius. Both factions sit at `location.outpost` (anchor (55, 145), radius 30 m): Renn 20.6 m, Kera 8.5 m, Sel 28.6 m from the anchor. No runtime code reads the seat; it is the data seam for a later "at the seat" condition.
- **FAC-M2 (no companion member).** A companion travels with the player; pooling a report told to him on the road would reach the seat instantly and would decide companion loyalty.

**Accepted residue (D30).** Kera, `at_work` at the bench about 62 m from the seat, can be told, and the Waystation learns at that tick. Shipped content cannot observe it: the only reads of the Waystation's standing are Kera's own billet row and views. Recorded in `M7_STATUS`. A talk with her is refused while she walks (`to_work` / `to_home`, G21).

#### 5.3.3 Seams kept as data

So that M9 needs no migration: the string keys `Source`, `Via` and `Identity`, whose closed sets already include `witnessed` and `unidentified`; `Knower` (a per-NPC knower fits the same list); the act's position, cell and tick; and `seat_location_ref` (lint only). The v14 fixture (§7.12) stores two `reported`/`identified` rows. Decode accepts `witnessed` and `unidentified` (§7.5); unit row U1 and the crafted save of fixture row F4 (`AnUnknownActor_MovesNoStanding_UntilIdentified`), which is saved through `SaveStore` and resumed, exercise both keys; M7 code never writes either.

#### 5.3.4 The M9 extension: the witnessed channel (specified, not built)

M7 builds none of this: no `WitnessRules`, no `BestWitness`, no `witness` config block (FAC001 refuses the key); `FactionSystem` never calls `SightWalls()` or reads an NPC body. M9 adds it with no save change, and must honour the three audit fixes marked **(fix)**:
- **K2 - witnessed.** Inside `Handle(RecordAct)`, for each reacting faction F (ordinal), candidates are NPCs with `FactionId == F` and a body in `State.Npcs`, not companions, **with no errand record (fix)**, and **whose body is inside F's seat radius (fix)**.
- **Eye (fix).** Position from the body; facing **only from saved or content state** (`npc.Site.FacingMdeg`), never the transient body facing the conversation turn changes (`src/World/Runtime/Social.cs:149-164`).
- **Sight (fix).** An **integer cone** `WitnessRules.InCone`, not the floating-point `Perception.Sees`: squared distances in mm, a dot product in `Int128` against a precomputed integer cos² threshold for the half angle, and occlusion by `SightWalls()` (placed walls and closed piece doors hide acts).
- **K3.** One witness per faction: identified beats unidentified, then the smaller squared distance, then the ordinal-lower NPC ID; it becomes `Via`.
- **K4.** `identified` within `identify_m`, else `unidentified` with `Delta = 0` until a report upgrades it (K8).
- Numbers (`config.factions.witness`, M9): `sight_m: 30`, `fov_deg: 140`, `identify_m: 15`. Witnessed changes are silent in the HUD. Sel's authored facing (135°) crosses her own survey table 2 m out; M9 re-faces or moves her.
- M9 owns the tests: `BestWitness` ordering and sight, `APlacedWall_HidesAnActFromAWitness` (and a closed piece door), `AWitnessJustAfterAConversation_LearnsTheSameAfterASaveAndLoad`, and the fixture rows for range, facing, walls and companions.

### 5.4 Standing

#### 5.4.1 The ladder (PROGRESSION §10 governs)

| Tier key | Level | Points (inclusive) | Width |
|---|---|---|---|
| `exalted` | +5 | 1000 | the cap |
| `allied` | +4 | 700 .. 999 | 300 |
| `honoured` | +3 | 450 .. 699 | 250 |
| `trusted` | +2 | 250 .. 449 | 200 |
| `accepted` | +1 | 100 .. 249 | 150 |
| `neutral` | 0 | −99 .. 99 | 199 |
| `wary` | −1 | −249 .. −100 | 150 |
| `disliked` | −2 | −449 .. −250 | 200 |
| `despised` | −3 | −699 .. −450 | 250 |
| `outcast` | −4 | −999 .. −700 | 300 |
| `anathema` | −5 | −1000 | explicit acts only |

- **Range and floor.** Points are held in [−1000, 1000]. An ordinary reaction clamps at −999, because "`Anathema` requires explicit acts" (`docs/PROGRESSION.md:454`) and no atonement content exists; a row flag `anathema: allowed` is refused.
- **One major local act crosses one tier.** The proof deltas are ±100 and Accepted starts at 100, so each gate opens or closes on one learned act.
- **The neutral band is wide** (−99..99): small M9 acts of ±10-30 accumulate inside Neutral ("accumulation inside a tier"). Tiers widen from 150 to 300 points so M9 content needs no re-base.
- **Ten times the relationship scale** ([−100, 100]), so a standing cannot be mistaken for a trust value.
- **The tier is never stored**; a threshold change in content reclassifies saved points, which is accepted because tiers gate access only.

`StandingLadder.StandingTierOf(points)` returns the first tier, top down, whose `MinPoints ≤ points`. **Never name it `TierOf`**: that is the simulation-tier helper in `Creatures.cs` and `Companions.cs` (L7).

#### 5.4.2 Default standing

Every faction starts at 0 (Neutral). There is no `start_points` field; `player_start_reputation` is refused by name.

#### 5.4.3 Application: `FactionRules.Learn` (pure)

```csharp
public static (FactionLedger, Learned?) Learn(FactionLedger ledger, FactionDefinition faction, long actSeq,
    string source, string via, string identity, long tick, StandingLadder ladder)
{
    var act = ledger.Acts.Single(a => a.Seq == actSeq);
    var row = faction.Reactions.SingleOrDefault(r => r.Kind == act.Kind && r.Subject == act.Subject);
    if (row is null) return (ledger, null);                                                   // K7
    var known = ledger.Knowledge.SingleOrDefault(k => k.Knower == faction.Id && k.Act == actSeq);
    bool upgrade = known is { Identity: Identities.Unidentified } && identity == Identities.Identified;
    if (known is not null && !upgrade) return (ledger, null);                                 // K8
    int from = PointsOf(ledger, faction.Id), to = from;
    if (identity == Identities.Identified)
        to = Math.Clamp(from + row.Delta, ladder.OrdinaryFloor, ladder.MaxPoints);
    var knowledge = new FactionKnowledge(faction.Id, actSeq, identity, source, via, tick, to - from);
    return (ledger.WithKnowledge(knowledge).WithPoints(faction.Id, to),
            new Learned(faction.Id, actSeq, source, via, identity, upgrade, from, to));
}
```

| Existing row | New knowledge | Result |
|---|---|---|
| none | unidentified | add the row with `Delta = 0`; points unchanged (reachable only from M9, or in a unit test) |
| none | identified | add the row, apply the clamped delta, store it in `Delta` |
| unidentified | identified | upgrade: overwrite `Identity`, `Source`, `Via`, `Tick` and `Delta`; apply the delta once |
| unidentified | unidentified | no change |
| identified | anything | no change |

`Delta` records what actually moved after the clamp, so the knowledge rows are PERSISTENCE T-21's bounded attribution log.

#### 5.4.4 Repeated acts

- **One act instance** applies at most once per faction, however often it is reported (K8).
- **Distinct instances of one subject each apply** (F8); earning standing by deeds is sanctioned.
- **Content cannot reach unbounded repeats in M7:** lint FAC-R5 allows reactions only to single-instance subjects (a creature whose every spawner has `respawn: none`; a flag some switch sets and no dialogue or quest reward writes). A repeatable subject first needs a repeat rule (GAMEPLAY_LOOPS E-9, M9).
- **Decay is deferred**; the migration that adds it takes the latest knowledge-row tick per faction as "last changed".

#### 5.4.5 Log capacity and eviction (L9)

`config.factions.acts.log_capacity` is 256. `FactionRules.Compact(ledger, capacity)` runs in `Handle(RecordAct)` after the append: while `Acts.Length > capacity`, it removes the **lowest-`Seq`** act with its knowledge rows. Standing never changes on eviction, and `NextActSeq` never decreases. An evicted act can no longer be reported. There is no "settled" rule and no `IsSettled`. Shipped play records at most two acts; the cap is a backstop proven by `Compact_EvictsTheOldestActFirst` and fixture row F14.

### 5.5 `FactionSystem`, setup, events and views

**Setup** (`src/World/Runtime/Factions.cs`, public, built by `FactionContent.Build(loader)` and set in `GameSession.Boot`):

```csharp
public sealed record FactionSetup(ImmutableSortedDictionary<string, FactionDefinition> Factions, StandingLadder Ladder, int LogCapacity)
{
    public static FactionSetup Empty { get; }                             // no factions, StandingLadder.Default, 256
    public bool IsRelevant(string kind, string subject);                  // some faction has a row for the pair
    public ImmutableArray<string> ReactorsTo(string kind, string subject); // faction IDs, ordinal
    // Backed by an ordinal ImmutableSortedDictionary keyed kind + "|" + subject, built in the constructor.
}
```

`SimulationSetup.Factions` is an init property that defaults to `FactionSetup.Empty`.

**State.**
- `RuntimeState` gains `StateSlice.Factions`, `FactionLedger Factions`, `int StandingOf(string factionId)` (0 when absent) and `SetFactions(SliceOwner owner, FactionLedger ledger)`.
- Its constructor reads `player.Factions`, as it reads `Posture`.
- `Simulation.CaptureRecord` adds `Factions = _state.Factions`.

**Composition.** `_factions = new FactionSystem(_context, _state.Claim(nameof(FactionSystem), StateSlice.Factions));` goes right after `_relationships` (section 6.1.2).

**`Handle(RecordAct a, long tick)`:**
1. If `!Setup.Factions.IsRelevant(a.Kind, a.Subject)`, return null.
2. Take `seq = ledger.NextActSeq`. Append `ActRecord(seq, a.Kind, a.Subject, CellKey.OfWorld(a.XMm / 1000.0, a.ZMm / 1000.0).ToString(), a.XMm, a.ZMm, tick)`. Publish `ActRecorded(seq, kind, subject, cellKey, tick)`.
3. Run `Compact(ledger, Setup.Factions.LogCapacity)`, then `SetFactions`, and return null. No knowledge is written.

**`Handle(ReportAct r, long tick)`:**
1. Look up `speaker = Setup.Social.Npcs.GetValueOrDefault(r.SpeakerNpcId)`. If `speaker?.FactionId` is null or names no faction in `Setup.Factions`, return null. Lint R4 makes this unreachable in content.
2. For each act with `Kind == r.Kind && Subject == r.Subject`, in ascending `Seq`:
   - call `Learn(ledger, faction, act.Seq, Reported, r.SpeakerNpcId, Identified, tick, Ladder)`;
   - on a change, publish `FactionLearned(F, seq, "reported", speaker, "identified", Upgraded, tick)`;
   - then, if `To != From`, publish `ReputationChanged(F, From, To, TierFrom, TierTo, seq, "reported", speaker, tick)`. Both tiers are computed with `Ladder.StandingTierOf`.
3. Call `SetFactions` if the ledger changed, and return null.

**What `FactionSystem` never touches.** It reads no NPC body, no facing, no `State.Npcs`, no `State.Conversation` and no `SightWalls()`. It dispatches nothing. The architecture test `FactionSystem_ReadsNoSightNoBodiesAndNoFacing` enforces this: `src/World/Runtime/Factions.cs` contains none of `SightWalls`, `Perception`, `.Body`, `FacingMdeg`, `State.Npcs`, `State.Conversation` or `Dispatch(`, and no type named `WitnessRules` and no member named `BestWitness` or `IsSettled` is declared in the Domain or World assemblies (reflection).

**The dialogue hook.**
- `DialogueSystem.Apply` gains `case ReportActConsequence report: _context.Dispatch(new ReportAct(report.Kind, report.Subject, open.NpcId)); break;`.
- Consequences apply in authored order.
- `ReportAct(string Kind, string Subject, string SpeakerNpcId)` is internal, lives in `Factions.cs`, and is routed to `_factions.Handle(r, Now)`.

**Events** (public records in `src/World/Runtime/Factions.cs`; exact shapes in section 6.1.5):
- `ActRecorded(long Seq, string Kind, string Subject, string CellKey, long Tick)`;
- `FactionLearned(string FactionId, long ActSeq, string Source, string? Via, string Identity, bool Upgraded, long Tick)`;
- `ReputationChanged(string FactionId, int From, int To, string TierFrom, string TierTo, long ActSeq, string Source, string? Via, long Tick)`.

In M7 every `FactionLearned` and `ReputationChanged` carries `Source = "reported"` and `Identity = "identified"`. Construction and load publish none of them (G27). There is no eviction event.

**Views** (get-only on `Simulation`; no allow-list change):
- `Factions` → `ImmutableArray<FactionView>`, ordinal by ID. `FactionView(string Id, string Name, int Points, string Tier, int Level, string SeatLocationId, ImmutableArray<string> Members, ImmutableArray<RelationView> Relations)`, with `RelationView(string FactionId, string Attitude)`.
- `Acts` → `ImmutableArray<ActView>`, in ascending `Seq`. `ActView(long Seq, string Kind, string Subject, string CellKey, long XMm, long ZMm, long Tick, ImmutableArray<KnowledgeView> Known)`, with `KnowledgeView(string Knower, string Identity, string Source, string? Via, long Tick, int Delta)`.
- `Wares(npcId)` omits withheld wares (§5.7.2).

The tier and level reach presentation only through `FactionView` and `ReputationChanged` (G2).

**File change list:**

| File | Change |
|---|---|
| `src/Domain/Factions/Factions.cs` (new) | §5.1.1 |
| `src/Domain/Social/Social.cs` | `NpcDefinition.FactionId`; the three new records; `IDialogueFacts.StandingLevel` and `ActDone`; two `DialogueRules.Holds` arms (`:148-159`) |
| `src/Domain/Items/Items.cs` | `MerchantStock.Requires` (`:220`) |
| `src/Domain/Quests/Quests.cs` | the `faction_reputation` and `faction_state` `NotBuilt` reasons (`:154-155`) become "faction quest content (M9)". Text only |
| `src/World/Runtime/Factions.cs` (new) | `FactionSetup`, `RecordAct`, `ReportAct`, `FactionSystem`, the three events, the views |
| `src/World/Runtime/RuntimeState.cs`, `Simulation.cs` | slice, fields and wrappers; composition; two `Dispatch` arms; `CaptureRecord`; `Factions` and `Acts` views |
| `src/World/Runtime/Creatures.cs`, `Systems.cs` | the two `RecordAct` dispatches (§5.2.2) |
| `src/World/Runtime/Social.cs` | the `ReportActConsequence` arm; `StandingLevel` and `ActDone`, implemented explicitly by `DialogueSystem` (`:206`) and `SpeakerFacts` (`:432`); `TradeSystem.Withheld` in `Handle(BuyCommand)` and `View` |
| `tests/Domain.Tests/DialogueRulesTests.cs:12` | the `Facts` fake implements both new members explicitly (no default interface members) |
| `src/World/Runtime/QuestDebugger.cs` | `DescribeCondition` arms (`:372`) for `reputation` and `act_done` |
| `src/World/PlayerState.cs` | the `Factions` property and its validation; all seven `With*` copies (`:228-253`) carry `{ Posture = Posture, Factions = Factions }`; digest v10 (section 7) |
| `src/Content/FactionContent.cs` (new, FAC001) | `Validate` (§5.14) and `Build` (definitions, ladder, capacity) |
| `src/Content/SocialContent.cs` | NPC `faction_ref`; the `reputation` and `act_done` parse arms; `report_act`; the `add_reputation` refusal text |
| `src/Content/ItemContent.cs` | stock-row `requires` (`MerchantOf`, `:191-204`) |
| `src/Content/ContentLoader.cs` | `FactionContent.Validate`, after `BuildingContent` (the order is Quest → Navigation → Building → Faction; until E5 lands, Faction follows Navigation) |
| `src/Application/GameSession.cs` | `Factions = FactionContent.Build(loader)` in the `SimulationSetup` initializer (`:88-99`) |
| `src/Persistence/*` | section 7 |

### 5.6 The working factions and the proof case

Names are working names, not canon: "Names are working names until cultural naming is finalized" (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:265`). IDs follow the content's social scoping. They avoid `faction.hollow.wardens`, which `tests/Content.Tests/QuestContentTests.cs:120` uses as a deliberately missing reference. Peoples (Veth, Kal, Siann, Orenth) are not factions, and neither faction is keyed to a people.

#### 5.6.1 The factions

| | The Waystation | The Survey |
|---|---|---|
| ID | `faction.ashen_hollow.waystation` | `faction.ashen_hollow.survey` |
| Purpose | Keep Ashen Hollow's road stop running: shelter, a forge with iron, a road travellers survive | Sel Arien's survey of the Foldscar and the old workings, kept for an institution the fiction leaves unnamed |
| Values | A stop safe to reach; iron for the forge; useful work | An accurate record of old things before they are used up; the Quiet Stones set right |
| Why here | It is Ashen Hollow's only community, and Renn is its practical local authority | The Foldscar is its subject |
| Reactions (M7) | `creature_killed` `creature.construct.animated_armour` **+100** | `switch_set` `world.foldscar.steadied` **+100**; `creature_killed` `creature.construct.animated_armour` **−100** |
| Relation | → Survey `cordial` | → Waystation `cordial` |
| Members | `npc.ashen_hollow.renn_vale`, `npc.ashen_hollow.kera_voss` | `npc.ashen_hollow.sel_arien` |
| Seat | `location.outpost` | `location.outpost` |
| What it proves | report knowledge; the service gate; an approving faction whose members' personal regard does not move | disapproval of an act another faction approves; the dialogue gate opening and closing; Sel's personal trust apart from the Survey's view |

Tavar Orr is unaffiliated. He is a hired Orenth guide and the companion, and lint FAC-M2 refuses `faction_ref` on any NPC with a `companion:` block. The Survey's second member arrives when M9 content has one who stays at a seat.

#### 5.6.2 A third faction: considered and deferred

"Whoever turned the Quiet Stones out of line" is the strongest opposed seed in Phase-1 content. As a faction, it would disapprove of steadying the heart and have no member present. In report-only M7 it could never be told anything. It would prove only that a faction without knowledge never updates, which the pair already proves (tell one faction, not the other) and fixture row F12 proves with a memberless fixture faction. It would also invent lore. It is the natural M9 candidate.

#### 5.6.3 The same act, two responses

**One act: the player destroys the Animated Armour in Blackvein Cut.**
- **To the Waystation** it was a guard standing between the stop and its iron. Scrapped, it leaves the seam free to work: **+100**.
- **To the Survey** it was an old working still keeping a post nobody remembers giving it, destroyed before anyone recorded it: **−100**.

Neither faction is the villain. It is a real choice: the sentinel holds its post at (65, 34), 38 m from the seam, ore must be reachable without defeating every enemy (content bible), and Quest 1 has no kill objective.

| How the player plays it | Waystation | Survey |
|---|---|---|
| Kills it, tells no one | 0, neutral (does not know) | 0, neutral (does not know) |
| Tells Kera | +100, accepted | 0 (does not know) |
| Tells Sel | 0 | −100, wary |
| Tells both | +100, accepted | −100, wary: **the same act, opposite directions** |
| Tells Sel twice (impossible in content; forced in fixture F5) | - | −100, once |

The player who wants Sel's notes keeps quiet about the armour, or spares it. That is legible in play, and it is what "information is not magically global" means here.

#### 5.6.4 The acceptance walk on shipped content (the P script)

This is a headless script in `tests/Application.Tests/ReputationFixture.cs`, shared by `ReputationTableTests` and `FactionTests`. It uses shipped content, dialogue and gates. Only the kill uses an arena placement (L8).

1. **P1 (kill).** Open the arena like `CreatureTests.Armed` (`tests/Application.Tests/CreatureTests.cs:36-44`) with `item.weapon.march_spear`: the shipped `creature.construct.animated_armour` as a `sentinel` at (120.0, 63.0), the player 1.8 m behind it along its facing, as `TheArmoursOpenBack_IsWhereABlowFromBehindLands` (`:303-311`) places them; seed 42. `Fight(armour, 600, stayPut: true)`. Assert the character killed it, `ActRecorded` seq 1 `creature_killed`, and no `FactionLearned`. The arena's spawn list replaces the shipped spawns, so no other creature is present. The arena record carries 40 coin, because a new character starts with none and P3 buys a billet (20 coin at `ceil(20 x 1.0)`).
2. **P2 (heart).** Walk (120, 63) → (140, 80), then the playthrough legs `ToTheNorthStone`, `ToTheSouthWestStone`, `ToTheSouthEastStone`, `ToTheHeart` (`src/Presentation/Playthrough.cs:64-67`), turning the three stones and steadying the heart with `InteractCommand`s. Assert act 2 `switch_set world.foldscar.steadied`; the stone flags record nothing. Walk to within talk reach of Tavar (145, 42), choose `found`, then leave (he is not recruited).
3. **P3 (tell Kera).** Walk `HomeWithTavar` (`:68`), then (51.8, 139) and (51.8, 142); open `door.forge_shed`; walk `IntoTheSmithy` and on to (60.3, 140.3). Talk to Kera: her first node is `greet`, so choose `leave`, talk again, and choose `armour` in `again`. Buy one billet by its ware ref (§5.7.2).
4. **P4 (tell Sel of Tavar).** Walk `OutToSel`, then to (71, 123). At Sel's `greet` choose `tavar_back`, then `back`; assert `notes` is offered in `again`.
5. **P5 (tell Sel of the armour).** Choose `armour`, then `back`; assert `notes` is not offered.

Each leg added here is checked against the layout's blockers when written, as section 13 checks `ToTheArmour`. **STOP on the first failure**: the run is deterministic.

The runtime counterpart is `--playthrough`'s faction beats (section 13). There, the heart is act 1, the armour is act 2, and Tavar is ordered to wait before the fight.

### 5.7 The gates

#### 5.7.1 Dialogue gate: `reputation` (Sel's `notes`)

```yaml
{ kind: reputation, faction_ref: faction.ashen_hollow.survey, min_tier: accepted }   # max_tier optional; not: refused
```

- **Predicate.** It holds iff `MinLevel ≤ Ladder.StandingTierOf(State.StandingOf(FactionId)).Level ≤ MaxLevel`. `min_tier` defaults to `anathema` (−5) and `max_tier` to `exalted` (+5). A `min_tier` above `max_tier` is refused.
- **Domain.** `DialogueRules.Holds` gains `ReputationCondition r => facts.StandingLevel(r.FactionId) is var l && l >= r.MinLevel && l <= r.MaxLevel`.
- **Content.**
  - `SocialContent.Conditions` gains `reputation` and `act_done` (`src/Content/SocialContent.cs:26-27`).
  - The parse arm resolves `faction_ref` to kind `faction`, and tier keys through `StandingLadder.LevelOf`.
  - `not` is refused, because the existing rule allows it only on four kinds.
- **Runtime.**
  - `DialogueSystem` and `SpeakerFacts` implement `StandingLevel(id) => Setup.Factions.Ladder.StandingTierOf(State.StandingOf(id)).Level`.
  - `ActDone(kind, subject) => State.Factions.Acts.Any(a => a.Kind == kind && a.Subject == subject)`.
- **Debugger.** `DescribeCondition` says, for example, "needs the Survey at accepted or better; it is neutral (0)". For `act_done` it says "needs the character to have killed Animated Armour; the act log holds none".
- **Bypass is impossible.** `Handle(ChooseCommand)` refuses a reply whose conditions do not hold (`src/World/Runtime/Social.cs:265-266`), and `View()` hides it (`:306`).

#### 5.7.2 Service gate: Kera's iron billets

**Content.** One new stock row, appended **last** to `content/merchants/ashen_hollow/kera_voss.yaml`:

```yaml
  - { item_ref: item.material.iron_ingot, count: 3, price_bias: 1.0, requires: { faction_ref: faction.ashen_hollow.waystation, min_tier: accepted } }   # M7: the waystation's own come first
```

`requires` takes exactly `faction_ref` and `min_tier`. `MerchantOf` parses it into `StandingRequirement(FactionId, StandingLadder.LevelOf(min_tier))`.

**Runtime** (`TradeSystem`, `src/World/Runtime/Social.cs:468-551`):

```csharp
private string? Withheld(NpcState npc, Merchant merchant, string itemId) =>
    merchant.Stock.FirstOrDefault(s => s.ItemId == itemId)?.Requires is { } gate
    && _context.Setup.Factions.Ladder.StandingTierOf(State.StandingOf(gate.FactionId)).Level < gate.MinLevel
        ? $"{npc.Definition.Name} will not sell you that" : null;

// Handle(BuyCommand): after the count check (:491-492), before pricing (:493):
//     if (Withheld(npc, merchant, ware.DefId) is { } withheld) return withheld;
// View(npcId) (:522-528): .Where(i => Withheld(npc, merchant, i.DefId) is null)  - a withheld ware is not listed
```

- **Predicate.** A ware whose item definition has a gated stock row at this merchant is listed and sold only while the player's tier with the gate's faction is at `MinLevel` or above. The gate is keyed by item definition, so an ingot the player sold to Kera joins her wares and is withheld too. Selling is not gated.
- Nothing changes at Neutral: `NpcTests.BuyingAndSelling_MoveCoinAndGoodsExactly_AndRefuseCleanly` (`tests/Application.Tests/NpcTests.cs:287-307`) passes unchanged. Quest 1 is not bypassed: `o_billet` is a `craft_item` deed.

**The ware ref rule (#05).** While a trader's wares are untouched, a ware's ref is `{merchantId}#{i:00}` over the stock split into stacks by `stack_max`, in row order (`src/World/Runtime/Items.cs:488-510`). Kera's 60 arrows (`stack_max` 20) are `#00`-`#02`, the vest `#03`, the cap `#04`, and the three ingots (`stack_max` 10) one stack, **`merchant.ashen_hollow.kera_voss#05`**; appending the row last keeps `#00`-`#04` unchanged. Once the container has a record, the ref is the stack's `itm_` ID, which tests read from `Simulation.World.Container("merchant.ashen_hollow.kera_voss")` because a withheld ware is not in `Wares`. Tests compute the untouched ref from the stock rows and the catalogue; they never hard-code `#05`.

#### 5.7.3 Why the service gate cannot be bypassed

- `Handle(BuyCommand)` is the only path that moves a trader's wares: `Trade` is dispatched only by `TradeSystem.Handle(BuyCommand)` (`src/World/Runtime/Social.cs:496`) and `Handle(SellCommand)` (`:515`); only the Buy path moves a stack out of a trader's wares, and `InventorySystem.Check` refuses `MoveItem` and `TakeAll` on wares.
- The check is in the authority, not the dialogue: `open_service` only publishes an event, and a `BuyCommand` needs no open conversation. Presentation cannot construct an internal command.
- A raw `BuyCommand` naming the billet's ref is refused with the reason, which `CommandRejected` already toasts (`src/Presentation/Main.cs:780-784`).

**Migrated saves.** A Kera wares container that a pre-M7 save already traded with is a persisted record that never re-reads stock, so that save never sees the billets. Migrations may not read content; accepted and recorded in `M7_STATUS`. The proof runs on a new game.

### 5.8 Faction reads: rules R1-R5 and the grandfathered Phase-1 leaks

M7 does not fix Phase 1. It adds five rules for every faction read or write. FAC001 enforces R1, R3, R4 and R5; R2 holds structurally, because `SocialContent.Conditions` gains only `reputation` (bounded by R1) and `act_done`, and no condition reads knowledge:
- **R1.** A `reputation` condition may name only a faction of which every participant of its conversation is a member.
- **R2.** No condition reads another faction's standing or any knowledge row; there is no `faction_knows` condition.
- **R3.** `act_done` reads ground truth (the player's own log; it holds iff at least one act of that pair is logged). It may appear only on a reply that also carries `report_act` for the same kind and subject, so it gates only the player's own decision to tell, never what an NPC says unprompted.
- **R4.** `report_act` may appear only in a conversation whose participants all belong to one faction, and that faction has a reaction row for that kind and subject.
- **R5.** Nothing but `FactionSystem` writes standing: `add_reputation` is refused with "reputation moves only through acts a faction learns of (M7)"; the quest reward `reputation` stays in `RewardKinds.NotBuilt`; `faction_reputation` and `faction_state` stay in `ObjectiveTypes.NotBuilt`; a merchant's `requires.faction_ref` must be the faction of every NPC whose `merchant_ref` names that merchant.

**Grandfathered.** Phase-1 dialogue reads global player state (cross-dialogue `visited`, `relationship` on any `npc_ref`, `quest_state`, `has_item`). These reads are unchanged, and M7 adds no new cross-NPC read. Sel's `tavar_back` is offered on a cross-dialogue `visited` of Tavar's greeting, an existing leak; the `report_act` M7 adds to it is legitimate, because the Survey learns from the player's words, not from the condition. Recommendation for M9: hold `relationship` conditions to the speaker, as R1 holds `reputation`.

**Code-level read rule** (section 6, G2 and G6): faction state is read only by `FactionSystem`, `DialogueSystem`, `TradeSystem`, `QuestDebugger` (describe only) and views; tactical files never reference it; presentation never derives a tier.

### 5.9 Relations, membership, and where faction state lives

**Static relations.** `relations: [{ faction_ref, attitude }]`, `attitude` ∈ {`close`, `cordial`, `indifferent`, `strained`, `opposed`}; directional, at most one row per target, never self. `allied` is not an attitude word, because it is a tier. Words replace DATA_MODEL's `attitude_default` ([−1, 1], a third numeric scale) and `enemy_of` (relation fused with hostility), so no code can do arithmetic between a relation and a standing. No M7 rule reads a relation; F6 and the table show it, and fixture F11 pins that relations never move standing. M7 content is `cordial` both ways: the player can be accepted by one faction and wary with the other while the factions stay cordial.

**Membership and the PROTOTYPE A-2 correction.** Membership is the NPC definition's optional `faction_ref` (where DATA_MODEL puts it); the faction has no `members:` field (refused by name). PROTOTYPE says "`faction_id` field exists on NPCs and is saved" (`docs/PROTOTYPE.md:110`); no such field exists, and non-companion NPC bodies are not saved at all. M7's resolution: membership is definition data while nothing can change it, so it needs no save field; saved instance membership arrives with the first system that changes membership (joining, defection, generated members). E3 corrects A-2. The player is never a member in M7.

**Where faction state lives.** There is no mutable faction-global state in M7. The act log, knowledge and standing are about the player, so they live on the player record, as relationships have since schema 10 (PERSISTENCE §5.1 already puts "faction reputation and crime records" in the player section). Relations and membership are content. World flags are cell-scoped and unused for factions. The first world-global faction state (war state, faction control, NPC acts, sharing between factions) needs a world-global save record that does not exist yet; when it arrives, the act log moves there by migration and gains `actor`.

### 5.10 Personal relationship vs faction reputation

| | Personal relationship | Faction standing |
|---|---|---|
| Holder | one NPC | one faction |
| Scale | five dimensions (affection, fear, grudge, respect, trust), int [−100, 100] | one int [−1000, 1000] plus a derived tier |
| Moved by | `ChangeRelationship`, from dialogue `record_relationship_event` or a quest `relationship` reward | `FactionRules.Learn`, only on a reported act |
| Slice and owner | `StateSlice.Relationships`, `RelationshipSystem` | `StateSlice.Factions`, `FactionSystem` |
| Read by | the `relationship` condition, the `relationship_value` objective | the `reputation` condition, the billet gate, views |

**They never interact in M7.**
- `FactionSystem` never dispatches `ChangeRelationship`, and `RelationshipSystem` never dispatches `RecordAct` or `ReportAct`.
- The shipped relationship writes stay exactly as authored.
- Content may carry both consequences on one reply. Sel's `tavar_back` keeps its `trust +5` and gains an independent `report_act`: Sel is personally grateful, and the Survey as an institution judges the steadying.
- Divergence is the point:
  - telling Sel about the armour costs the Survey 100 and Sel's trust nothing;
  - Sel's trust cannot open `notes`; only the Survey's standing can;
  - Kera's `respect` +10 for fine work says nothing about the Waystation.

`PersonalRelationships_StaySeparateFromStanding` pins both directions.

### 5.11 Hostility: out of M7, with the layers kept separate

M7 builds no faction hostility and no combat against NPCs:
- NPCs have no health (`NpcState`, `src/World/Runtime/Social.cs:88`), and the player's blows resolve only against creatures;
- creatures carry no faction, and their hostility is their perception-driven mind (M3d).

| Ruling-3 layer | M7 state | Where |
|---|---|---|
| Personal relationship | exists (M4), untouched | `Relationships` |
| Trust, fear, grudge | exist as relationship dimensions (M4), untouched | `Relationships` |
| Reputation / standing | **built** | `FactionStanding`, tier derived |
| Legal status | not built; nothing writes or derives it | reserved: per-jurisdiction records, never one bounty number (Phase 3, Q2) |
| Faction relation | static content | `FactionDefinition.Relations` |
| War state | not built | reserved: a world-global record |
| Known identity | a stored key, always `identified` in M7 | `FactionKnowledge.Identity` |
| Tactical threat | creature mind (M3d) | `CreatureRecord` |
| Attack legality | not built; the `CanAttemptAttack(attacker, target, context)` seam is untouched | `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:825` |

**The guard** is `TacticalCode_NeverReadsFactionState` (G6, owned by section 6), a **reflection check**, not a token grep: no type declared in `Combat.cs`, `Creatures.cs`, `Companions.cs`, `Magic.cs`, `Errands.cs`, `Navigation.cs`, `Building.cs`, `BuildingRules.cs`, `src/World/Runtime/Quests.cs` or `src/Domain/Quests/Quests.cs` may reference `UNNAMED.Domain.Factions`, `RuntimeState.Factions`, `RuntimeState.StandingOf`, `SimulationSetup.Factions` or `IDialogueFacts.StandingLevel`. It never bans the bare token `TierOf`. The `RecordAct` dispatch in `Creatures.cs` passes, because `RecordAct` is declared in `UNNAMED.World.Runtime` and its kind argument is an inlined constant.

`TheLadder_HasNoHostilityTier` requires exactly PROGRESSION's 11 keys, so VERTICAL_SLICE's "Hostile" tier cannot appear; FAC001 refuses `enemy_of` and `attitude_default`. A future gate access set may read faction access, and only that.

### 5.12 Persistence (summary; section 7 specifies)

- **Where.** One new field, `PlayerDto.factions` (`FactionsDto`: `next_act_seq`, `acts[]`, `knowledge[]`, `standing[]`) in `player.msgpack`, required from schema 14 (`DecodePlayer` throws `FormatException("player.msgpack has no factions (required from schema 14)")`). Enum-like values are string keys. No world-delta change, no new section file, no `DeltaSnapshot` property.
- **Migration.** The shared `SchemaV13ToV14` step writes `{ next_act_seq: 1, acts: [], knowledge: [], standing: [] }`. **Pre-M7 acts are never reconstructed** ("factions arrived with M7; nothing before was told"): a save whose armour or heart was already done loads with an empty log and neutral standing, and its report replies are not offered.
- **Digest.** `unnamed.player/v10` hashes, after posture: `NextActSeq`; each act (seq, kind, subject, cell, x, z, tick); each knowledge row (knower, act, identity, source, via or "-", tick, delta); each standing row (faction, points).
- **Definition pass** (only when `content_hash` differs):

  | Stored reference | If renamed or replaced | If removed | If merged |
  |---|---|---|---|
  | standing `faction_id` | rewritten | row dropped, with `Loss` | points summed, then clamped to [−999, 1000]; **a sum of 0 drops the row with a `Warning`** ("standing with {a} and {b} merged to neutral", L5) |
  | knowledge `knower` | rewritten | row dropped | union by (`knower`, `act`); identified beats unidentified, then the lower tick |
  | act `subject` | rewritten | the act and its rows dropped; standing kept; `Loss` | - |
  | knowledge `via` | rewritten | set to null with a `Warning`; the row is kept | - |

  An unresolvable `via` is a Blocker. An `ArgumentException` from the pass maps to a Blocker that names the pass (L5).
- **Never saved:** tiers, relevance, member lists, relations, `FactionSetup`, the views. Nothing is in flight: no witness working set, no service cache, no reports in transit.
- **Evidence** (section 7): the v14 fixture ledger (two acts; one act moving two fixture factions in opposite directions; two `reported`/`identified` knowledge rows, with act 2 known by no faction; the `faction.fixture.delvers → diggers` alias reaching standing and knowledge; the warden rename reaching `via`); `CanonicalState` renders the ledger; the real M6 save loads with every `FactionView` at 0, neutral, level 0, `Acts` empty, the billets withheld and `notes` hidden.

### 5.13 Content (YAML)

`content/config/factions.yaml`:

```yaml
id: config.factions
kind: config
schema: 1
display_key: config.factions.name
tags: [config]
notes: Factions and reputation (M7; PROGRESSION.md §10). One ladder for every faction; a standing is whole points and its tier is derived at read time, never saved. In M7 a faction learns an act only when the player tells a member - no rumour, no telling between factions.
ladder:                          # PROGRESSION.md §10's eleven tiers, top down; a tier holds points >= its min
  - { tier: exalted,  level: 5,  min: 1000 }
  - { tier: allied,   level: 4,  min: 700 }
  - { tier: honoured, level: 3,  min: 450 }
  - { tier: trusted,  level: 2,  min: 250 }
  - { tier: accepted, level: 1,  min: 100 }
  - { tier: neutral,  level: 0,  min: -99 }
  - { tier: wary,     level: -1, min: -249 }
  - { tier: disliked, level: -2, min: -449 }
  - { tier: despised, level: -3, min: -699 }
  - { tier: outcast,  level: -4, min: -999 }
  - { tier: anathema, level: -5, min: -1000 }
points: { min: -1000, max: 1000, ordinary_floor: -999 }   # Anathema needs explicit acts (PROGRESSION §10); none exist yet
acts:
  log_capacity: 256  # the oldest act goes first
```

`content/factions/ashen_hollow/waystation.yaml`:

```yaml
id: faction.ashen_hollow.waystation
kind: faction
schema: 1
display_key: faction.ashen_hollow.waystation.name
tags: [faction]
name: the Waystation            # working name, not canon (content bible §9)
notes: The people who keep Ashen Hollow's road stop running - Renn Vale's stewardship and Kera Voss's forge. Wants the stop safe and supplied. Members are the NPCs whose faction_ref names it; this file lists none. An M7 working faction.
seat_location_ref: location.outpost
reactions:
  - { act: creature_killed, creature_ref: creature.construct.animated_armour, delta: 100 }   # the seam can be worked, and the forge gets its iron
relations:
  - { faction_ref: faction.ashen_hollow.survey, attitude: cordial }                         # read by nothing but views in M7
```

`content/factions/ashen_hollow/survey.yaml`:

```yaml
id: faction.ashen_hollow.survey
kind: faction
schema: 1
display_key: faction.ashen_hollow.survey.name
tags: [faction]
name: the Survey                # working name; the institution behind Sel is deliberately unnamed
notes: Sel Arien's survey of the Foldscar and the old workings around Ashen Hollow. Wants old things recorded before they are used up, and the Quiet Stones set right. An M7 working faction.
seat_location_ref: location.outpost
reactions:
  - { act: switch_set,      flag_ref: world.foldscar.steadied,                delta: 100 }    # the stones set right, the fold quiet
  - { act: creature_killed, creature_ref: creature.construct.animated_armour, delta: -100 }   # an old working destroyed before anyone recorded who set it there, or why
relations:
  - { faction_ref: faction.ashen_hollow.waystation, attitude: cordial }
```

**NPC lines** (one each):
- `renn_vale.yaml` and `kera_voss.yaml` gain `faction_ref: faction.ashen_hollow.waystation`;
- `sel_arien.yaml` gains `faction_ref: faction.ashen_hollow.survey`;
- `tavar_orr.yaml` gets none.

**Merchant row:** §5.7.2.

**Dialogue additions.** All new text is **DRAFT for the owner's tone review**; it ships as written unless the owner objects before E10. No existing line's text or conditions change.

Kera, in `again`, gains one new reply placed before `trade`, and one new node:

```yaml
      - id: armour
        text: "The armour at the seam won't stand guard any more."   # DRAFT (tone review)
        conditions:
          - { kind: act_done, act: creature_killed, creature_ref: creature.construct.animated_armour }
          - { kind: visited, node: armour_down, not: true }
        consequences:
          - { command: report_act, act: creature_killed, creature_ref: creature.construct.animated_armour }
        next: armour_down
  armour_down:
    text: "Then the seam's only a seam again. I keep a few billets back for the waystation's own. Ask, and they're yours to buy."   # DRAFT
    once: true
    next_if_exhausted: again
    choices:
      - { id: back, text: "I'll look.", next: again }   # DRAFT
```

Sel, in `again`, gains two new replies placed before `leave`, and two new nodes:

```yaml
      - id: notes
        text: "What's in the notes you keep back?"   # DRAFT (tone review)
        conditions: [{ kind: reputation, faction_ref: faction.ashen_hollow.survey, min_tier: accepted }]
        next: notes
      - id: armour
        text: "The armour in Blackvein Cut is down."   # DRAFT
        conditions:
          - { kind: act_done, act: creature_killed, creature_ref: creature.construct.animated_armour }
          - { kind: visited, node: armour_down, not: true }
        consequences:
          - { command: report_act, act: creature_killed, creature_ref: creature.construct.animated_armour }
        next: armour_down
  notes:
    text: "The stones weren't knocked out of line. Each was turned the same quarter, by someone who knew the ring. That's more than I've written down."   # DRAFT; a lore hook the owner may reject
    choices:
      - { id: back, text: "I'll keep it to myself.", next: again }   # DRAFT
  armour_down:
    text: "Down. It kept that post longer than this waystation has stood, and now nobody can ask it who set it there."   # DRAFT
    once: true
    next_if_exhausted: again
    choices:
      - { id: back, text: "It was in the way.", next: again }   # DRAFT
```

Sel's two existing `tavar_back` replies, in `greet` (`content/dialogue/ashen_hollow/sel_arien.yaml:21-27`) and in `again` (`:57-65`), each gain one consequence, appended after the existing relationship event:

```yaml
          - { command: report_act, act: switch_set, flag_ref: world.foldscar.steadied }
```

**Migrated saves stay safe:**
- `report_act` does nothing without a matching act;
- no existing reply gains a condition, so Quest 2's start and completion are untouched;
- `act_done` is false on a migrated save, so the new report replies are not offered.

`LoadAll_Loads_Yaml_Files` (`tests/Content.Tests/ValidationTests.cs:508-547`) gains `config.factions`, `faction.ashen_hollow.survey` and `faction.ashen_hollow.waystation`.

### 5.14 Lint FAC001 (`src/Content/FactionContent.cs`)

**`config.factions`:**
- It is required iff any faction exists (the `config.companion` precedent).
- `ladder` holds exactly `StandingLadder.Keys`, in order and with those levels. `min` is strictly descending, and `neutral.min ≤ 0 < accepted.min`.
- `points.min == anathema.min == FactionLedger.MinPoints` (−1000), `points.max == FactionLedger.MaxPoints` (1000), `exalted.min ≤ points.max`, and `points.ordinary_floor == outcast.min == FactionLedger.OrdinaryFloor` (−999).
- `acts.log_capacity ≥ 16`.
- `witness` is refused by name: "the witnessed channel is M9".

**Faction:**
- `name` and `seat_location_ref` are required, and the seat is a `location`.
- Reactions:
  - `act` is in `ActKinds.Built`; a `NotBuilt` kind is refused with its reason (`piece_placed` and `piece_destroyed` included: G13);
  - there is exactly one subject field, matching the kind: `creature_ref` for `creature_killed`, `flag_ref` for `switch_set`;
  - at most one row per (act, subject);
  - `delta ≠ 0` and `|delta| ≤ 250`;
  - `anathema: allowed` is refused.
- **FAC-R5 (single-instance subjects).**
  - (a) A `creature_killed` subject has every spawner that places it at `respawn: none`.
  - (b) A `switch_set` flag is set by at least one layout switch, and no dialogue `set_world_flag` consequence and no quest `world_flag` reward writes it.
- Relations name an existing faction other than self, at most one row per target, with `attitude` in `Attitudes.All`.
- **Refused by name, each with its reason:**
  - `members` ("membership is the NPC's faction_ref");
  - `player_start_reputation` and `reputation_tiers` ("one ladder, config.factions; every standing starts at 0");
  - `laws` ("crime is not built; law belongs to places, not to a faction");
  - `enemy_of` and `attitude_default` ("relations are attitude words; hostility is not a faction field");
  - `territory`, `services_gated`, `joinable` and `join_requirements` ("not built in M7").

**NPC:**
- `faction_ref` names an existing faction.
- **FAC-M1.** The NPC's authored placement, in each region layout that places it, lies within the discovery radius of its faction's seat location. The seat location's `region_ref` must be that region. An unplaced member is an error. Sel's margin is 1.4 m, so any re-placement fails loudly.
- **FAC-M2.** `faction_ref` together with a `companion:` block is refused.

**Dialogue and merchant:**
- R1, R3, R4 and R5 as stated in §5.8.
- A gated item appears in exactly one stock row of its merchant.

### 5.15 The reputation fixture table

**What it is.** `docs/M7_REPUTATION_TABLE.md`, **generated from the build** on the precedent of `docs/M3D_BEHAVIOUR_MATRIX.md`. The generator `tests/Application.Tests/ReputationTableTests.cs` fails when the committed file differs from the build; regenerate with `UNNAMED_WRITE_REPUTATION=1 dotnet test --filter ReputationTable` from `src/` (the `UNNAMED_WRITE_MATRIX` pattern, `tests/Application.Tests/BehaviourMatrixTests.cs:78`). It also asserts the signs of F1 and P5 directly, so the table cannot drift from the exit criterion.

**Its sections:** (1) Ladder: tier, level, point range from `config.factions`. (2) Reaction matrix from content: rows (act, subject), columns factions, cells `+100` / `−100` / `-`, a row with differing signs marked **opposite**. (3) Factions: seat, members, relations. (4) Knowledge: channels built (`reported`) and reserved (`witnessed`, M9), log capacity. (5) Scenarios, measured in the real simulation and driven only through public `GameCommand`s, in the format below.

**The fixture setup (F rows)**, in `tests/Application.Tests/ReputationFixture.cs`: `session.Setup with { … }` opened through `Arena.OpenCreatures`.
- **Factions.** A = `faction.fixture.keepers` (Renn, Kera, by setting `NpcDefinition.FactionId`): `creature_killed creature.beast.wolf_grey` +100. B = `faction.fixture.delvers` (Sel): `creature_killed wolf_grey` −100, `switch_set world.foldscar.stone_north_aligned` +100. Relations `cordial` both ways. The shipped ladder; `LogCapacity` 256, or 16 for F14.
- **Dialogues.** Kera and Sel get fixture dialogues whose root node offers `wolf` (`act_done` + `report_act creature_killed wolf_grey`, `next` the same node); Sel's also offers `stone`. No `visited` guard, so F5 can report twice. R1-R5 hold.
- **Placement.** Sel's site moves to (59.0, 139.6), facing 90°; the player acts from (60.3, 140.3), 1.48 m from both, inside the 1.95 m talk reach. The axis tests instead move Sel within talk reach of `switch.stone_north`.
- **Kills.** Fixture wolves are `sleeper`s with the combat setup's `wolf_grey` at 1 health: one blow each. Coordinates are the test's own; the table prints them.
- **Save and resume** use `SaveStore` and `Arena.Resume(setup, …)`, as `CompanionTests.HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame` does (`tests/Application.Tests/CompanionTests.cs:285-310`), which keeps the fixture setup.

**Format and rows.** A is the Waystation in P rows and the keepers in F rows; B is the Survey or the delvers.

| # | Scenario | Act (seq, kind, subject) | Reports | A: source, via, identity, Δ, points, tier | B: source, via, identity, Δ, points, tier | Other checks | Proves |
|---|---|---|---|---|---|---|---|
| F1 | wolf killed; tell Kera, then Sel | 1, creature_killed, wolf_grey | A via kera; B via sel | reported, kera, identified, +100, 100, accepted | reported, sel, identified, −100, −100, wary | two `ReputationChanged` | **the same act moves two factions in opposite directions, in a fixture (ROADMAP exit)** |
| F2 | wolf killed; tell nobody | 1 | - | -, 0, neutral | -, 0, neutral | act 1 recorded; no knowledge row; `act_done` holds | no report, no change |
| F4 | crafted save: act 1 known `unidentified` by B (witnessed, via sel, Δ 0); then tell Sel | 1 | B via sel | - | before: 0, neutral. After: reported, sel, identified (upgraded), −100, −100, wary | one `FactionLearned` with `Upgraded`; one `ReputationChanged` | the identity seam: nothing moves until identified, then the delta applies once |
| F5 | F1, then tell Kera and Sel again | 1 | A, B again | unchanged | unchanged | no `FactionLearned`, no `ReputationChanged` | reports are idempotent |
| F8 | two wolves killed; tell Kera once | 1, 2 | A via kera | +100, +100: 200, accepted | - | two `FactionLearned`, in ascending `Seq` | distinct acts each apply |
| F10 | F2; save and resume between the act and the report; tell Sel after the resume | 1 | B via sel | - | reported, sel, identified, −100, −100, wary | `StateDump.Compare`: 0 differences against an unsaved twin that did the same | continuity across a save |
| F11 | F1's act with relations A → B `opposed`, B → A `close`; tell Kera only | 1 | A via kera | +100, 100, accepted | -, 0, neutral | | relations never move standing |
| F12 | wolf killed; a third fixture faction C (`faction.fixture.watchers`) reacts +100 but has no member; tell Kera and Sel | 1 | A, B | +100 | −100 | C: no row, 0, neutral | a faction nobody told never learns |
| F13 | a `creature.beast.bristleback_boar` killed; no faction reacts | none | - | 0 | 0 | no `ActRecorded`; `NextActSeq` unchanged | irrelevant acts are not recorded |
| F14 | capacity 16: wolf 1 killed and told to Kera, then 16 more wolves killed | 1..17 | A via kera (act 1) | +100, 100, accepted, unchanged by eviction | - | after act 17: act 1 and its row are gone; acts 2..17 remain; `NextActSeq` 18 | the oldest act goes first; standing is untouched |
| U1 | Domain unit: `Learn` with `identity: unidentified` | synthetic | - | row stored, Δ 0, points unchanged | | | an unidentified row applies no delta |
| P1 | shipped: armour killed (§5.6.4) | 1, creature_killed, animated_armour | - | -, 0, neutral | -, 0, neutral | billets absent from `Wares(Kera)`; a raw `BuyCommand` for the billet ref is refused with "Kera Voss will not sell you that" | nobody knows |
| P2 | heart steadied | 2, switch_set, world.foldscar.steadied | - | - | -, 0, neutral | the three stone flags record no act | Tavar is no member: nobody knows |
| P3 | tell Kera `armour`; buy one billet | 1 | Waystation via kera | reported, kera, identified, +100, 100, accepted | -, 0, neutral | billets listed and one bought; Kera's respect and trust unchanged | report knowledge; the service gate opens; the Survey does not hear |
| P4 | tell Sel `tavar_back` | 2 | Survey via sel | unchanged | reported, sel, identified, +100, 100, accepted | `notes` offered; Sel's trust moved only by the authored writes that fired (the `brought_tavar_back` event and, if Quest 2 completes, its `relationship` reward) | the dialogue gate opens |
| P5 | tell Sel `armour` | 1 | Survey via sel | unchanged: 100, accepted | reported, sel, identified, −100, 0, neutral | `notes` hidden; Sel's trust unchanged by P5; the HUD line "The Survey: neutral (-100), told to Sel Arien" | **one act: Waystation +100, Survey −100**; the gate closes |
| N1 | checkpoint after P2 | 1, 2 | none | 0 | 0 | no knowledge rows; `act_done` holds for both acts; Kera's and Sel's `armour` replies are offered when first shown (P3, P4) | no psychic factions |

Rows dropped to M9 with the witnessed channel: range and identify distance, facing, walls, placed walls and closed piece doors, and a companion as witness. The runtime path of K7 cannot be reached in content (R4), so the Domain test `Learn_WithoutARow_StoresNothing` covers it.

### 5.16 Acceptance tests

File homes: `tests/Domain.Tests/Factions/FactionRulesTests.cs`; `tests/Application.Tests/ReputationTableTests.cs` (the generator), `ReputationFixture.cs` (the fixture setup and the P script) and `FactionTests.cs` (every other Application test); `tests/Content.Tests/FactionContentTests.cs`; `tests/Architecture.Tests/ArchitectureTests.cs`.

| Case | Tests |
|---|---|
| The same known act moves two factions differently | `TheSameKnownAct_MovesTwoFactionsInOppositeDirections` (P1-P5 on shipped content); F1 in `ReputationTableTests` |
| A faction without knowledge does not update | `AFactionThatNeitherSawNorWasTold_DoesNotUpdate` (N1; P3's Survey; F2; F12) |
| Save and load | `FactionState_ContinuesAcrossASaveAndLoad` (F10; after P5, a save and resume with `StateDump.Compare` at 0 differences); `SaveThenContinue_EqualsContinue_WithFactions` (save between P3 and P4, run P4-P5 in both worlds, equal `StateDigest`) |
| Replay | `FactionActs_ReplayFromTheCommandLog_EndIdentical`: the P script replayed from its command log. P3's purchase materialises Kera's wares and mints `itm_` IDs, so by G8's rule it compares the replayable dump, plus the `FactionLedger` by value and the sequence of the three faction events. Also `TwoFreshRuns_ProduceTheSameReplayableDump` |
| Repeated act | `RepeatedReports_ApplyOnce` (F5); `DistinctActs_EachApply` (F8); `Learn_AppliesARowOnce_PerFactionPerAct`; lint `ARespawningCreatureReaction_IsRefused` and `AReactionToAFlagContentCanReset_IsRefused` |
| Unknown actor: not relevant in M7; the seam | `Learn_Unidentified_KnowsButMovesNothing` (U1); `Learn_AReportUpgradesUnidentifiedExactlyOnce`; `AnUnknownActor_MovesNoStanding_UntilIdentified` (F4) |
| The relationship stays separate | `PersonalRelationships_StaySeparateFromStanding`: across P1-P5, Sel's trust equals exactly the authored writes that fired, and Kera's respect and trust are untouched by P3. A source scan also checks that `Factions.cs` never constructs `ChangeRelationship` and that `RelationshipSystem` never dispatches `RecordAct` or `ReportAct` |
| The gates | `TheBilletGate_HidesTheWare_AndRefusesARawBuyCommand_UntilAccepted` (from a crafted ledger holding the armour act; also, a billet sold to Kera at neutral is withheld); `TheReputationCondition_OpensAndClosesSelsNotes` (a crafted record with acts 1 and 2 and Tavar's `greet` in dialogue memory); `ActDone_OffersTheReportOnlyAfterTheAct` |
| Attribution | `ACompanionsKill_IsNotThePlayersAct`: a fixture armour at 1 health; Tavar, following, lands the blow; no `ActRecorded`; Kera's `armour` reply is not offered |
| No currency crossing (owed at M7) | `AxisIndependence_ReputationByLevel_2x2` and `AxisIndependence_ReputationBySkill_2x2`. High standing at level 1 comes from a reported fixture switch act (a switch pays no XP, and no quest is active). High level or skill at neutral comes from kills and practice that no faction reacts to. Each asserts that no axis's currency moved another |
| Report-only is structural | `FactionSystem_ReadsNoSightNoBodiesAndNoFacing` |

Guards owned by section 6 that cover factions: `TacticalCode_NeverReadsFactionState` (G6, G19), `PresentationSource_NeverDerivesStanding` (G2), `SystemsNeverSubscribe` and the extended `AViewThatListensToEverything_SeesExactlyWhatHappened_AndWritesNothing` (G5), `BuildingNeverRecordsAnAct` (G13), `EveryPersistedField_MovesItsDigest` (G12), `ConstructionAndLoad_PublishNoM7Event` (G27), `NavRoute_AndFactionLedger_EqualByValue_AfterADecode` (G28). Section 7 owns the persistence tests: the 13 → 14 migration, `ASchema14PlayerWithoutFactions_IsCorrupt_NotDefaulted`, `TheFactionLedger_GoesThroughTheDefinitionPass_RenamesMergesAndRemovals`, `TwoFactionsMergedWithOppositeStanding_LoadNeutral_WithAWarning`, the unmapped-`via` Blocker, and the v14 fixture.

### 5.17 Presentation touch points (section 8 specifies)

- A HUD log line on every `ReputationChanged` whose `Source == "reported"`, which in M7 is every one: "{Faction}: {TierTo} ({±(To − From)}), told to {name of Via}". Nothing in presentation derives a tier.
- F6 is a read-only faction debug panel built from `Simulation.Factions`, `Simulation.Acts` and `Simulation.Wares`. The service gate shows as "on offer". The dialogue gate is not evaluated in presentation.
- Reports and purchases use the existing `ChooseCommand` and `BuyCommand`. There is no new command, no faction screen and no radial.

### 5.18 As-built document updates (E3)

- **SYSTEMS S-27:** owns the act log, knowledge and standing (tier derived) and static relations; nothing transient; K1, K5-K10, the M9 extension note and the three events; drop "legal/offense state derived from it" and `AddReputation`.
- **DATA_MODEL:** §4.13 takes the §5.13 shape and records where each dropped field went; §4.12 `reputation` takes tier keys, adds `act_done` and `report_act`, `add_reputation` stays unbuilt; §4.15 stock rows take `requires`.
- **PROGRESSION §10:** numbers in `config.factions`; tiers derived; decay deferred; "trade volume" means goods supplied, never coin; Anathema unreachable until atonement content exists; ruling 3's separation written here.
- **PROTOTYPE A-2** (`:110`, `:395`): membership is definition data, not saved until something can change it. **INDEX** CRIME row: "M7 reconciled: act records and faction knowledge only; crime is Phase 3". **VERTICAL_SLICE** `:95`, `:169` flagged as conflicting (a "Hostile" tier; "puts town guards on you"), for M9 to reconcile as relation, war state and legal status. PERSISTENCE §5.1: section 7. ROADMAP M7: E0.

### 5.19 Residues recorded in `M7_STATUS`

1. A companion's kill is not the player's act; if Tavar lands the killing blow, nothing can be reported.
2. No history before M7: migrated saves start with an empty log and neutral standing.
3. A Kera wares container touched before M7 never shows the billets.
4. Pooling is immediate (K6); Kera told at the bench moves the Waystation at that tick (D30), unobservable in shipped content.
5. Every faction learns by report; NPCs do not notice deeds on their own until M9.
6. Relevance is fixed at boot; an evicted act can no longer be reported.
7. All new dialogue text is a draft; Sel's `notes` line is a lore hook the owner may reject.
8. The live tier reaches the player only through the HUD line and F6.

### 5.20 Not in M7

| Item | Belongs to |
|---|---|
| The witnessed channel (K2-K4, `WitnessRules`, `BestWitness`, `config.factions.witness`, identity by sight, the occlusion rows including `APlacedWall_HidesAnActFromAWitness`), with the fixes of §5.3.4 | M9 |
| Settled-first eviction and `IsSettled` | dropped (L9) |
| `piece_placed` / `piece_destroyed` act kinds (they need a repeat rule) | M9 |
| `item_taken`, `npc_harmed` act kinds | Phase 3 crime / harmable NPCs |
| Crime, bounty, pardon, legal status, jurisdiction, guards, evidence, disguise | Phase 3 (owner Q2) |
| Territory gating and faction access sets on gates | with crime (Q2) |
| Rumour, NPC-to-NPC telling, transfer between factions, reports in flight, per-member memory, delay through `Via` and the seat | the first propagation milestone (M9 or later) |
| Identity rungs beyond two; `confidence`, claims, lies | M9 or later |
| War state, faction control, any hostility derivation | M9 reconciliation (VERTICAL_SLICE) and later |
| Decay, caps, diminishing returns, repeat rules | M9 tuning |
| Joining or leaving a faction, player membership, multiple memberships, saved instance membership | the first system that changes membership (unscheduled) |
| `start_points` and standing by origin | character creation (unscheduled) |
| `faction_reputation` / `faction_state` objectives; the `reputation` quest reward | M9 quest reconciliation |
| A player-facing faction screen limited to what the character knows | M9 |
| Price modifiers by tier; whole-trade gates | M9 |
| A third faction (the stone-turners) | M9 candidate |
| Companion loyalty and reporting; party attribution of kills | M9 (attribution), Expansion (loyalty) |
| Anathema by explicit act, with atonement content | when atonement content exists |
| `FactionCounters`; the log-cap timing test T8 | optional, switched on by measurement (section 14) |

### Tests owned by this section

| Test | Project | What it proves |
|---|---|---|
| `StandingTierOf_FollowsTheLadder_AtEveryBoundary` | Domain.Tests | The ladder at −1000/−999, −100/−99, 99/100 and 999/1000 |
| `Learn_AppliesARowOnce_PerFactionPerAct` | Domain.Tests | One act applies at most once per faction |
| `Learn_Unidentified_KnowsButMovesNothing` | Domain.Tests | U1: an unidentified row applies no delta |
| `Learn_AReportUpgradesUnidentifiedExactlyOnce` | Domain.Tests | The identity seam upgrades once and applies once |
| `Learn_WithoutARow_StoresNothing` | Domain.Tests | K7: no reaction row, no knowledge row |
| `Learn_ClampsAtTheOrdinaryFloor_AndTheCap` | Domain.Tests | Ordinary reactions stop at −999 and 1000; `Delta` records the clamped change |
| `Compact_EvictsTheOldestActFirst` | Domain.Tests | Eviction by lowest `Seq`, with its rows; standing untouched |
| `ReputationTableTests` (F1-F14, U1, P1-P5, N1; generates `docs/M7_REPUTATION_TABLE.md`) | Application.Tests | The ROADMAP proof table equals the build; the signs of F1 and P5 |
| `TheSameKnownAct_MovesTwoFactionsInOppositeDirections` | Application.Tests | The exit on shipped content through reports |
| `AFactionThatNeitherSawNorWasTold_DoesNotUpdate` | Application.Tests | No psychic factions (N1, P3, F2, F12) |
| `AnUnknownActor_MovesNoStanding_UntilIdentified` | Application.Tests | F4: a crafted unidentified row moves nothing until a report upgrades it once |
| `RepeatedReports_ApplyOnce` | Application.Tests | F5: reports are idempotent |
| `DistinctActs_EachApply` | Application.Tests | F8: distinct acts of one subject each apply |
| `FactionState_ContinuesAcrossASaveAndLoad` | Application.Tests | 0 `StateDump` differences across a save (F10, and after P5) |
| `SaveThenContinue_EqualsContinue_WithFactions` | Application.Tests | `StateDigest` continuity mid-script |
| `FactionActs_ReplayFromTheCommandLog_EndIdentical` | Application.Tests | Replay reproduces the ledger and the faction events (G5) |
| `TwoFreshRuns_ProduceTheSameReplayableDump` | Application.Tests | Independence from fresh IDs |
| `PersonalRelationships_StaySeparateFromStanding` | Application.Tests | Relationships and standing never move each other |
| `FactionRelations_NeverMoveStanding` | Application.Tests | F11: static relations move nothing |
| `ACompanionsKill_IsNotThePlayersAct` | Application.Tests | Attribution: a companion's kill records no act |
| `TheBilletGate_HidesTheWare_AndRefusesARawBuyCommand_UntilAccepted` | Application.Tests | The service gate, keyed by item, cannot be bypassed |
| `TheReputationCondition_OpensAndClosesSelsNotes` | Application.Tests | The dialogue gate opens and closes |
| `ActDone_OffersTheReportOnlyAfterTheAct` | Application.Tests | A report reply needs a recorded act, and is offered once |
| `AxisIndependence_ReputationByLevel_2x2` | Application.Tests | AX-REP × AX-LVL: no currency crossing |
| `AxisIndependence_ReputationBySkill_2x2` | Application.Tests | AX-REP × AX-SKL: no currency crossing |
| `FactionSystem_ReadsNoSightNoBodiesAndNoFacing` | Architecture.Tests | Report-only is structural: `Factions.cs` reads no sight, body, facing, conversation or NPC state, and dispatches nothing; and no type named `WitnessRules` and no member named `BestWitness` or `IsSettled` is declared in the Domain or World assemblies (reflection) |
| `TheLadder_HasNoHostilityTier` | Content.Tests | The ladder is exactly PROGRESSION's 11 keys and levels |
| `ACompanionCannotBeAMember` | Content.Tests | FAC-M2 |
| `AMemberStandsInsideTheSeat` | Content.Tests | FAC-M1 |
| `AStandingCondition_NamesTheSpeakersOwnFaction` | Content.Tests | R1 |
| `ActDone_OnlyBesideAReportOfTheSameAct` | Content.Tests | R3 |
| `AReport_GoesOnlyToTheSpeakersReactingFaction` | Content.Tests | R4 |
| `AGate_BelongsToTheTradersFaction` | Content.Tests | R5's merchant clause; one gated row per item |
| `APlacedPieceReaction_IsRefused` | Content.Tests | Reserved building act kinds are refused (G13) |
| `ARespawningCreatureReaction_IsRefused` | Content.Tests | FAC-R5 (a) |
| `AReactionToAFlagContentCanReset_IsRefused` | Content.Tests | FAC-R5 (b) |
| `NoContent_TradesCurrencyForStanding` | Content.Tests | E-7: no `reputation` reward, no `add_reputation` (refused with its reason), no trade act kind |
| `FactionContent_RefusesDataModelFieldsItDoesNotBuild` | Content.Tests | The closed field set, including `config.factions.witness` |
| `LoadAll_Loads_Yaml_Files` (extended) | Content.Tests | The three new definition IDs load |
