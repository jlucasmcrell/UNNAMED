# M7 Part C - Factions and Reputation v1 (authoritative synthesis)

Status: authoritative Part C for the M7 design, 2026-09-24. Design and implementation planning only; not an owner ruling. It synthesises two panel candidates (`drafts/panel/fac_minimal.md`, `drafts/panel/fac_doctrine.md`) inside `drafts/00_SCOPE_RULINGS.md`. Base: `origin/main` `e10d2c4`. Every `path:line` is repo-relative and was re-read in the snapshot `main_e10d2c4` for this document. Research notes used in full: `research/faction_docs.md`, `research/world_lore.md`, `research/social_quests_code.md`.

Rule for the synthesis: the minimal candidate's mechanism size, plus the doctrine candidate's seams only where retrofitting them later would cost a save-format change - and then only as data shape, not behaviour. Appendix A compares the candidates; Appendix B lists the false or imprecise claims found; Appendix C lists what was deliberately deferred.

No challenge to a scope ruling is raised.

---

## 0. The design on one page

- **Two working factions in Ashen Hollow**, named as plain-English working names:
  - **the Waystation** (`faction.ashen_hollow.waystation`): Renn Vale and Kera Voss;
  - **the Survey** (`faction.ashen_hollow.survey`): Sel Arien.
  - Tavar Orr belongs to no faction in M7. He is a hired Orenth guide and the companion. Lint refuses `faction_ref` on any NPC with a `companion:` block.
- **One proof act.** The player destroys the Animated Armour that guards the Blackvein iron seam (`creature.construct.animated_armour`, spawner `spawn.hollow.iron_shelf_armour`, respawn `none`):
  - the Waystation reads it as the seam made workable, **+100**;
  - the Survey reads it as an old working destroyed before anyone recorded it, **-100**.
- **Three records, all on the player**, in one new player-section field at schema 14:
  - the **act log** (what happened);
  - **faction knowledge rows** (what each faction learned, through which channel and member, and whether it knows who did it);
  - **standing points** per faction. The tier is derived and never stored.
- **Two knowledge channels, no others.**
  - *Witnessed*: a member NPC's body sees the actor at the moment the act is recorded, by the existing `Perception.Sees`.
  - *Reported*: the player tells a member in dialogue (`report_act`).
  - No rumour, no propagation between factions, no NPC-to-NPC telling, no companion testimony, no scripted transfer to a faction nobody in the conversation belongs to.
- **Standing moves only** when a faction knows an act, knows the actor, and has a reaction row for it. Nothing else writes standing: no `add_reputation`, no `reputation` quest reward.
- **Two gates.**
  - Dialogue: Sel's new `notes` reply is offered only while the Survey is at `accepted` or better.
  - Service: a new stock row on Kera's merchant, 3 iron billets, is sold only while the Waystation is at `accepted` or better. It is enforced inside `TradeSystem.Handle(BuyCommand)`, so a direct `BuyCommand` cannot bypass it.
- **Nothing hostile.** No tier, relation or reaction feeds combat, creature minds, companions, attack legality or law. An architecture test makes that a build failure.
- **Building produces no faction act in M7.** Its faction effect is occlusion: placed walls and closed placed doors hide acts from witnesses. `piece_placed` is a reserved act-kind key (section 13).

### Decisions at a glance

| # | Decision |
|---|---|
| D1 | Mechanism: one system (`FactionSystem`), one slice (`StateSlice.Factions`), two internal commands (`RecordAct`, `ReportAct`), no `Tick` |
| D2 | Records: `ActRecord` + flat `FactionKnowledge` rows + `FactionStanding`, in one `FactionLedger` on `PlayerRecord` |
| D3 | Act identity: a persisted sequence number, never a ULID |
| D4 | Act kinds built: `creature_killed`, `switch_set`. Reserved: `piece_placed` and others |
| D5 | Knowledge: witnessed (sight at the act) or reported (dialogue), pooled per faction at that tick |
| D6 | Identity: two built rungs, `unidentified` and `identified`, stored as a string key |
| D7 | Ladder: PROGRESSION §10's 11 tiers on points [-1000, +1000]; ordinary reactions clamp at -999 |
| D8 | Proof: Animated Armour kill, Waystation +100, Survey -100 |
| D9 | Gates: `reputation` dialogue condition; a `requires` gate on a merchant stock row |
| D10 | Membership: NPC `faction_ref` only; static; not saved |
| D11 | Relations: static attitude words; read by nothing but views |
| D12 | No mutable faction-global state in M7 |
| D13 | No faction hostility; guarded by an architecture test |
| D14 | Building: no faction act in M7; placed blockers occlude witnesses |
| D15 | Persistence: one player field at schema 14, shared step with building, digest tag `unnamed.player/v10` |

---

## 1. Code facts this design stands on (verified in the snapshot)

| Fact | Where |
|---|---|
| Cross-system work is a synchronous internal command routed by `Simulation.Dispatch`; `Now` is the stepped tick, or the boundary tick while draining | `src/World/Runtime/Simulation.cs:367`, `:369-404` |
| Events are for views and tests only, "never persisted" | `src/World/Runtime/Events.cs:10-11` |
| `RecordDeed` is the act fan-in precedent; a kill counts only `if (killer == _player)` | `src/World/Runtime/Quests.cs:62`; `src/World/Runtime/Creatures.cs:701-706` |
| A switch sets its flag once ("already set" refusal), then publishes `SwitchSet` | `src/World/Runtime/Systems.cs:276-292` (refusal `:284`, flag `:288`, event `:290`) |
| `SwitchSite(Key, FlagId, Body, Requires, ...)`; `LocationSite(Id, XMm, ZMm, DiscoveryRadiusMm, DiscoveryXp)` | `src/Domain/Spatial/RegionLayout.cs:20-21`, `:31` |
| `Perception.Sees(eye, senses, x, z, walls)`: range, FOV cone, then `Blocker.Crosses`; returns distance or null | `src/Domain/Creatures/Perception.cs:87-107` |
| `Crosses` is 2-D and ignores `HeightMm`; a segment starting inside a circle counts as crossing | `src/Domain/Spatial/Blockers.cs:26`, `:77-90`, `:117-124` |
| Creature sight walls = static blockers + closed doors and standing barriers; combat and companions repeat the same expression | `src/World/Runtime/Creatures.cs:893`; `Combat.cs:570`; `Companions.cs:598`; `SystemContext.ClosedDoors` `Systems.cs:48` |
| A companion's body is an NPC body (`State.Npcs`), moved by `PlaceNpc` | `src/World/Runtime/RuntimeState.cs:72`; `src/World/Runtime/Social.cs:98-104`, `:141-147` |
| `NpcDefinition` has no faction field; `BuildNpcs` refuses five named fields and ignores other keys | `src/Domain/Social/Social.cs:10-16`; `src/Content/SocialContent.cs:69-94` |
| Dialogue conditions are a closed switch over `IDialogueFacts`; unoffered replies are refused and hidden | `src/Domain/Social/Social.cs:124-159`; `src/World/Runtime/Social.cs:264-266`, `:306` |
| `open_service` only publishes an event; `BuyCommand` needs no conversation; `TradeSystem` owns no state | `src/World/Runtime/Social.cs:373-375`, `:462-550` |
| Only `Trade` moves a trader's wares; `MoveItem` and `TakeAll` are refused on wares | `src/World/Runtime/Items.cs:136-142`, `:450-459` |
| Untouched wares are the authored stock (`#00`, `#01`... refs); a touched container is a persisted record | `src/World/Runtime/Items.cs:488-501` |
| `MerchantStock(ItemId, Count, PriceBias)`; stock rows parsed in `MerchantOf` | `src/Domain/Items/Items.cs:220`; `src/Content/ItemContent.cs:191-204` |
| `faction` kind registered (`factions/`, prefix `faction`); `faction_ref` is a known reference suffix; unknown `*_ref` names are XREF004 | `src/Content/SchemaResolution.cs:158-163`; `src/Content/ContentChecks.cs:34`, `:132-135`, `:150-160` |
| `SchemaTypeMapper.GetSchemaType` (holding the empty `FactionSchema`) has no caller | `src/Content/SchemaTypeMapper.cs:19`, `:250-256` (grep) |
| Quest objectives `faction_reputation`/`faction_state` wait on "factions (M7)"; reward `reputation` not built | `src/Domain/Quests/Quests.cs:154-155`, `:203-206` |
| Player state is an init property carried by each `With*`; digest tag `unnamed.player/v9` | `src/World/PlayerState.cs:222-253`, `:306-320`, `:323-368` |
| Save schema 13; migration table; posture decode pattern; definition pass | `src/Persistence/SaveModel.cs:20`; `Migrations.cs:69-81`, `:708-747`; `SectionCodec.cs:399-416`; `SaveLoader.cs:316-357` |
| `Simulation` public methods are allow-listed; get-only properties are not | `tests/Architecture.Tests/ArchitectureTests.cs:111-131` |
| Command-log replay must equal `StateDigest()` | `tests/Application.Tests/DeterminismAndViewTests.cs:48-77` |
| `NpcTests` boots shipped content and expects Kera's hide vest listed and buyable at neutral | `tests/Application.Tests/NpcTests.cs:287-307` |
| NPC placements and facings (0 = +Z, clockwise towards +X); Renn and Kera inside walled buildings; Sel outdoors facing 135° | `content/regions/ashen_hollow.yaml:67-83`, `:141-155` |
| `location.outpost` anchor (55, 145), discovery radius 30 m | `content/locations/outpost.yaml` |

---

## 2. The five doctrine layers and what M7 builds

The doctrine rule: "What happened, what was perceived, what people believe happened, what can be proven, and what the law says about it are different things." (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:8`). Each built layer has its own record, so later milestones add layers rather than split a merged one.

| Layer | M7 representation | Status |
|---|---|---|
| What happened | `ActRecord` in the act log: kind, subject, cell, position, tick, sequence | Built |
| What was perceived | `FactionKnowledge.Source` (`witnessed`/`reported`) and `Via` (the member) | Built, per faction |
| What is believed | `FactionKnowledge.Identity` (`unidentified`/`identified`); the row references the act, never copies it | Built, two rungs |
| What is proven | nothing | Reserved: evidence with provenance and confidence |
| What the law says | nothing; no code derives legal status from standing | Reserved: jurisdiction and legal-status records (Phase 3, scope item 4) |

In M7 "perceived" and "believed" coincide, because neither built channel can be wrong. They are still stored as separate fields, so a later lie or rumour becomes a knowledge row with a different source, not a change of shape.

---

## 3. Item 1 - minimum entities and data

| Candidate | M7 | Form and reason |
|---|---|---|
| FactionDefinition | Yes | Content, `kind: faction` (already registered). Fields: `name`, `seat_location_ref`, `reactions`, `relations`. Every other DATA_MODEL §4.13 field is refused by name (section 16.4) |
| FactionRelationship (faction to faction) | Static data only | `relations: [{ faction_ref, attitude }]` with a closed attitude word set. No runtime type, no rule reads it (section 10.1) |
| ReputationRecord | Yes | `FactionStanding(FactionId, Points)` on the player; the tier is derived at read time. Stored points plus a derived tier resolves the "stored or derived" conflict (`research/faction_docs.md` C-11) without migrating thresholds |
| KnownAct / reported event | Yes, split | `ActRecord` (truth) and `FactionKnowledge` rows (perception and belief), in two separate lists |
| Value / tag preferences | No | A faction's values are its reaction rows, each naming one act kind and one exact subject. Tag rows add an overlap rule and prove nothing here |
| Membership | Yes, static | One optional `faction_ref` on the NPC definition. The faction lists no members; the member index is derived at content build |
| Hostility thresholds | No | Standing is access only (`docs/PROGRESSION.md:444-452`). There is no field that could hold such a mapping |

Every row of the reaction table is data, so "quest X gives +10 to faction A" cannot be expressed: quests have no reputation reward, and a reaction names a world act, not a quest.

### 3.1 Domain types (`src/Domain/Factions/Factions.cs`, new)

```csharp
namespace UNNAMED.Domain.Factions;

public static class ActKinds
{
    public const string CreatureKilled = "creature_killed";   // subject: creature definition ID
    public const string SwitchSet = "switch_set";             // subject: the world.* flag definition the switch set
    public static readonly ImmutableArray<string> Built = ImmutableArray.Create(CreatureKilled, SwitchSet);
    /// Named and refused, each with what it waits for (the ObjectiveTypes.NotBuilt pattern, Quests.cs:141-159).
    public static readonly ImmutableSortedDictionary<string, string> NotBuilt = ...
    {
        ["piece_placed"]    = "a repeat rule: a placement consumes materials, so reacting to it turns items into standing (GAMEPLAY_LOOPS E-7); M9",
        ["piece_destroyed"] = "a repeat rule (M9)",
        ["item_taken"]      = "ownership (crime, Phase 3)",
        ["npc_harmed"]      = "NPCs that can be harmed",
    };
}

public static class KnowledgeSources { public const string Witnessed = "witnessed", Reported = "reported"; }   // closed set
public static class Identities       { public const string Unidentified = "unidentified", Identified = "identified"; }   // closed set

public sealed record ActRecord(long Seq, string Kind, string Subject, string CellKey, long XMm, long ZMm, long Tick);

public sealed record FactionKnowledge(
    string Knower,      // a faction ID in M7 (the key is "knower" so a later per-NPC row fits the same list)
    long Act,           // ActRecord.Seq
    string Identity,    // unidentified | identified
    string Source,      // witnessed | reported: the channel of the row's latest change
    string? Via,        // the member NPC who saw it or was told; null only after that NPC was removed from content
    long Tick,          // the tick of the row's latest change
    int Delta);         // the standing change this row caused: 0 while unidentified

public sealed record FactionStanding(string FactionId, int Points);   // Points != 0; zero is not stored

public sealed record FactionLedger(long NextActSeq, ImmutableArray<ActRecord> Acts,
    ImmutableArray<FactionKnowledge> Knowledge, ImmutableArray<FactionStanding> Standing)
{
    public static FactionLedger Empty { get; } = new(1, [], [], []);
}

public sealed record Reaction(string Kind, string Subject, int Delta);
public sealed record Relation(string FactionId, string Attitude);
public sealed record FactionDefinition(string Id, string Name, string SeatLocationId,
    ImmutableArray<Reaction> Reactions, ImmutableArray<Relation> Relations);

public sealed record StandingTier(string Key, int Level, int MinPoints);
public sealed record StandingLadder(ImmutableArray<StandingTier> Tiers, int MinPoints, int MaxPoints, int OrdinaryFloor)
{
    public StandingTier TierOf(int points);   // highest tier whose MinPoints <= points
}
public sealed record WitnessRules(Senses Senses, long IdentifyMm);
public sealed record StandingRequirement(string FactionId, int MinLevel);   // a gated stock row (section 8.2)
```

Canonical orderings, validated on construction of `PlayerRecord` (section 14.2):
- `Acts` ascending by `Seq`;
- `Knowledge` by (`Knower` ordinal, `Act` ascending), at most one row per pair;
- `Standing` by `FactionId` ordinal.

---

## 4. Item 2 - the act: from an authoritative gameplay act to a faction input

### 4.1 The pattern

The acting system dispatches one synchronous internal command, exactly as it dispatches `RecordDeed` today (`Creatures.cs:705-706`, `Crafting.cs:110`, `Social.cs:355`). No system subscribes to events. Replay therefore stays a pure function of the command log.

```csharp
// src/World/Runtime/Factions.cs (new)
/// To FactionSystem: the character did something a faction may react to, standing here, now.
internal sealed record RecordAct(string Kind, string Subject, long XMm, long ZMm) : InternalCommand;

/// To FactionSystem: the character told this NPC about their own act (a dialogue report_act).
internal sealed record ReportAct(string Kind, string Subject, string SpeakerNpcId) : InternalCommand;
```

`Simulation.Dispatch` gains two arms: `RecordAct a => _factions.Handle(a, Now)` and `ReportAct r => _factions.Handle(r, Now)`. Both handlers return null; a dispatcher ignores the result, as `DialogueSystem.Apply` does for its other consequences.

`Deed` is not reused. It is a quest counter with quest-only kinds and no position (`src/Domain/Quests/Quests.cs:297-300`). M5 stays untouched.

### 4.2 The two built act kinds and their dispatch sites

| Kind | Subject | Dispatched from | Exact hook | Position |
|---|---|---|---|---|
| `creature_killed` | creature definition ID | `CreatureSystem.Die` | inside the existing `if (killer == _player)` block, immediately after the `RecordDeed` dispatch (`Creatures.cs:705-706`). The block becomes braced | the player's body, `State.Body` |
| `switch_set` | the `world.*` flag the switch set (`site.FlagId`) | `InteractionSystem.Work` | after `SetWorldFlag` is accepted (`Systems.cs:288`), before `SwitchSet` is published (`:290`) | the player's body |

Why these choices:
- **The subject of `switch_set` is the flag, not the switch key.** A flag is a definition ID, so it is resolved by the generic `flag_ref` walker, and it passes through the alias pass. A switch key is a layout key, which neither covers. In Ashen Hollow each switch sets its own flag. Dialogue `set_world_flag` and quest `world_flag` rewards set flags without being acts: they are content's bookkeeping, not the player's deed.
- **The position is the actor's body.** A witness identifies an actor by seeing them. Seeing only the effect (a body falling, a ring going quiet) belongs to the deferred evidence layer. With the effect's position, a witness who saw the armour fall but not the archer 25 m away would "identify" the player; the actor's position cannot overclaim.
- **A companion's kill is not the player's act**, mirroring the M6 rule ("His kills earn the character nothing and count for no quest", `docs/M6_STATUS.md:69`; `Creatures.cs:697-699`). Section 20 records the cost.

**Not act kinds, deliberately:**
- **Trade**: coin never buys standing (E-7, `docs/GAMEPLAY_LOOPS.md:238`).
- **Quest completion**: it is the player's bookkeeping. Factions react to the world act the quest required.
- **Dialogue choice**: a said act is the only honest home for a future `add_reputation`; it is not built.
- **Container takes**: they need ownership, which is the crime milestone's.
- **Building**: see section 13.

### 4.3 Relevance: which acts are recorded at all

At content build, `FactionSetup.Relevant` is the ordinal-sorted set of (kind, subject) pairs named by any faction's reaction rows. `FactionSystem.Handle(RecordAct)` returns immediately, recording nothing, for a pair outside the set.

- In Ashen Hollow the set has two pairs: (`creature_killed`, `creature.construct.animated_armour`) and (`switch_set`, `world.foldscar.steadied`).
- Both subjects are single-instance: the armour's spawner has respawn `none` (`content/spawns/hollow/iron_shelf_armour.yaml`), and a switch sets once (`Systems.cs:284`). Shipped play records at most two acts.
- **Accepted consequence:** content added later cannot react to an act recorded before it existed. This is the same rule as quest deed counting ("only while active").

A wolf kill is not recorded, because no faction reacts to wolves.

### 4.4 Identity and ordering

- **Identity is `Seq`,** taken from the persisted `FactionLedger.NextActSeq` (starts at 1, incremented once per recorded act).
- **Acts are history rows, not registry entities,** so D-04's ULID rule does not apply; the precedent is `harvest_seq` on node records (`src/Persistence/SectionCodec.cs:192`). `EntityId.NewId` reads the wall clock (`src/Domain/EntityId.cs:48-53`): an act ID minted by it would differ between a played session and its command-log replay, and `StateDigest()` hashes the player record, so `DeterminismAndViewTests.cs:48-77` would fail.
- **Order is dispatch order,** which is fully determined:
  - commands drain FIFO at a boundary (`Simulation.cs:268-306`);
  - `Step` runs a fixed system order (`:312-339`);
  - internal commands are synchronous (`:369-404`);
  - every system iterates sorted collections (`State.Creatures`, `State.Npcs` are ordinal `ImmutableSortedDictionary`s).
- **Inside the faction code:** factions in ordinal ID order; candidate witnesses in ordinal NPC-ID order; at most one reaction row per (faction, kind, subject), enforced by lint; reported acts in ascending `Seq`.
- Nothing depends on event handling or dictionary enumeration order.

### 4.5 The act record's fields (the brief's list)

| Brief asks for | Where it lives | M7 content |
|---|---|---|
| Actor identity | Truth: implicitly the player, because the ledger is the player's (every `GameCommand` handler refuses a non-player actor). Belief: `FactionKnowledge.Identity` per faction | The actor is never stored as a field in M7. When NPC acts arrive, the act log moves to a world-global record and gains `actor`; the migration fills it with the player's ID, which is in the save |
| Actor known / unknown | `FactionKnowledge.Identity` | `unidentified` = "it happened, we don't know who" |
| Location / cell | `ActRecord.CellKey` (`CellKey.OfWorld(x/1000.0, z/1000.0)`), `XMm`, `ZMm` | integer mm |
| Tick | `ActRecord.Tick` = `Now` when dispatched | |
| Witnesses | `FactionKnowledge` rows with `Source = witnessed`, `Via` = the best witness of that faction | Unaffiliated NPCs and non-best witnesses are not stored: in M7 nobody can use them. The seam for a full witness list is the `knower` key (a later per-NPC row) |
| Sequence | `ActRecord.Seq` | |

---

## 5. Item 3 - knowledge: when a faction knows

### 5.1 The rule, stated so each clause is testable

- **K1 - two channels only.** A faction's knowledge rows are created or changed only by K2 (witnessed) or K5 (reported). Nothing else writes them.
- **K2 - witnessed.** Inside `FactionSystem.Handle(RecordAct)`, synchronously, at the act's tick, for each faction F (ordinal) that has a reaction row for the act, each candidate M is tested:
  - M is in `State.Npcs`, its definition's `FactionId == F`, and it is not in `State.Companions`;
  - `Perception.Sees(M.Body, rules.Senses, act.XMm, act.ZMm, _context.SightWalls())` is non-null.
  NPC bodies are taken as they are at that instant (an NPC in conversation faces the player; `NpcSystem.Tick` runs later in the step).
- **K3 - one witness per faction.** Among M's that see: identified beats unidentified, then the smaller distance, then the ordinal-lower NPC ID. That witness becomes `Via`.
- **K4 - identity.** `identified` iff the seen distance is at most `IdentifyMm`, else `unidentified`.
- **K5 - reported.** When a reply carrying `report_act (kind, subject)` is chosen in a conversation with NPC S, S's faction F learns every act in the log with that (kind, subject), in ascending `Seq`, as `identified`, `Source = reported`, `Via = S`. An act F already knows as identified is unchanged. The actor names themselves, so a report always identifies.
- **K6 - pooled at the tick.** When a member learns, the faction learns at that tick. Standing is per faction. There is no delay and no per-member memory.
- **K7 - no row, nothing stored.** A faction without a reaction row for the act gets no knowledge row. Knowledge exists in M7 only to drive reactions.
- **K8 - idempotent, one upgrade.** A second witness or report of an act a faction already knows identified changes nothing. An `unidentified` row upgrades to `identified` exactly once.
- **K9 - no other writer.** Nothing but K2 and K5 writes knowledge, and nothing but K2 and K5 through `Learn` (section 6.3) writes standing. No faction ever learns from another faction; relations move nothing.
- **K10 - relevance.** An act outside `Relevant` (section 4.3) is not recorded, so no faction can learn it.

**Scripted transfer** is not a third mechanism. The dialogue is the script, and `report_act` can only inform the speaker's own faction (lint R4, section 9). A consequence that informs a faction nobody in the conversation belongs to would be psychic, and cannot be written.

### 5.2 Why pooling is honest here, and how that is enforced

"Same faction does not imply instant shared awareness" (`docs/STEALTH_DETECTION_AND_THREAT.md:100`). K6 is M7's one stated approximation of it. It is allowed only because every M7 faction is local, and M7 makes that checkable:

- **Content constraint FAC-M1:** every faction names `seat_location_ref`, and every member's authored placement lies inside that location's discovery radius.
  - In Ashen Hollow both factions are seated at `location.outpost` (anchor (55, 145), radius 30 m). Renn stands 20.6 m from the anchor, Kera 8.5 m and Sel 28.6 m.
  - Runtime ignores the seat except in views. It is a data seam: a later propagation milestone can make "at the seat" a runtime condition without a save change.
- **Content constraint FAC-M2:** no companion is a member. A companion walks 100 m from the seat with the player. Pooling what he sees would make the Survey know at Blackvein what Tavar saw, before he could have told anyone. It would also decide companion loyalty (whether a companion reports on the player), which is Expansion work (`docs/FEATURE_DELIVERY_STAGING.md:101`, "crime testimony/accomplice behavior").
- **Accepted residue:** the one NPC that M7 moves, the work-anchor assignment (scope item 5), keeps its faction and pools what it sees wherever it stands. It walks inside Ashen Hollow's 200 m square, and no relevant act can happen near the build area in M7 content. Recorded as open issue 4.

`Via` is the seam for delay: a later milestone can ask "has the member who knows been home yet?" without changing the act or knowledge shapes.

### 5.3 Witness numbers (`config.factions`)

| Key | Value | Why this value |
|---|---|---|
| `witness.sight_m` | 30 | The keenest Phase-1 sight, the ash ember hound's (`content/creatures/beast/ash_ember_hound.yaml:17`). A person notices an act no further off than a hunting beast notices the player |
| `witness.fov_deg` | 140 | The grey wolf's cone (`content/creatures/beast/wolf_grey.yaml:19`). Someone facing away does not see |
| `witness.identify_m` | 15 | Half of sight range. Nearer, a face is seen; farther, only that it happened |

`Senses` hearing is 0: M7 witnesses do not hear acts.

### 5.4 One sight rule, and its known limits

The witness check reuses `Perception.Sees`, the rule creatures already use (`Creatures.cs:296`), so there is one tested sight model:
- **Floating point:** it uses `Math.Sqrt`, `Sin` and `Cos` (`Perception.cs:90-97`), exactly as creature perception does in authoritative paths. It runs once per act with fixed inputs in sorted order, so it adds no call-order or float-order dependence. Replays are same-machine, as today.
- **2-D sight:** `Crosses` ignores height (`Blockers.cs:77-90`, `:117-124`). Low furniture blocks witnesses as it blocks creatures. Sel's authored facing (135°) points through her own survey table (box 73.4-75.2 × 120.4-122.2, `ashen_hollow.yaml:83`): the ray enters the table about 2 m out, so she sees nothing between bearings of about 82° and 139°. No M7-relevant act can happen in that direction within 30 m, so this changes nothing in M7. It is recorded for the sight-model owner (open issue 6).

### 5.5 What this means in shipped Ashen Hollow

- Renn and Kera stand inside walled buildings (`ashen_hollow.yaml:67-78`, `:151-155`).
- Sel stands at the survey table, 88 m from the armour and 110 m from the heart.
- Tavar is no member.

So **in shipped play every faction learns by report**. The witnessed channel, identity ranges and the unidentified-then-reported upgrade are proven by the fixture table (section 17.1). This is stated plainly, not hidden: the knowledge rule is general, and Ashen Hollow's geometry makes reports the only path. M9's town geometry will exercise witnessing.

---

## 6. Standing: ladder, application, repeats

### 6.1 The ladder (PROGRESSION §10 governs)

PROGRESSION §10 gives 11 named tiers with "per-faction numeric accumulation inside a tier" (`docs/PROGRESSION.md:432-434`). No tier name implies an attack order (`docs/PROGRESSION_AXIS_RECONCILIATION.md:185`). The lint requires exactly these keys in this order, so VERTICAL_SLICE's "Hostile" tier cannot appear.

| Tier key | Level | Points (inclusive) | Width |
|---|---|---|---|
| `exalted` | +5 | 1000 | the cap |
| `allied` | +4 | 700 .. 999 | 300 |
| `honoured` | +3 | 450 .. 699 | 250 |
| `trusted` | +2 | 250 .. 449 | 200 |
| `accepted` | +1 | 100 .. 249 | 150 |
| `neutral` | 0 | -99 .. 99 | 199 |
| `wary` | -1 | -249 .. -100 | 150 |
| `disliked` | -2 | -449 .. -250 | 200 |
| `despised` | -3 | -699 .. -450 | 250 |
| `outcast` | -4 | -999 .. -700 | 300 |
| `anathema` | -5 | -1000 | explicit acts only |

The numbers:
- **Range and floor.** Points are held in [-1000, +1000]. An ordinary reaction clamps at **-999** (`ordinary_floor`), because "`Anathema` requires explicit acts and is escapable via atonement content" (`docs/PROGRESSION.md:454`). No atonement content exists, so no M7 row may reach Anathema. A row flag `anathema: allowed` is reserved and refused by lint.
- **One major local act crosses one tier.** The proof deltas are ±100, and Accepted starts at 100. Both gates open or close on one learned act, so the prototype needs no grinding.
- **The neutral band is wide (-99..99).** Small acts of ±10-30 in M9 content accumulate inside Neutral without flipping access. That is PROGRESSION's "accumulation inside a tier".
- **Widening tiers (150 → 300)** keep M9's larger content from needing a re-base of saved points.
- **The scale is ten times the relationship scale** ([-100, 100], `src/Domain/Social/Social.cs:32-33`). No one can mistake a standing for a trust value.
- **The tier is never stored.** A threshold change in content reclassifies saved points (`docs/DATA_MODEL.md:846`). That is accepted, because tiers gate access only.

### 6.2 Default standing

Every faction starts at 0 (Neutral). There is no `start_points` field in M7; DATA_MODEL's `player_start_reputation` is refused by name. Origin-dependent standing is character-creation work.

### 6.3 Application: `FactionRules.Learn` (pure, Domain)

```csharp
public static (FactionLedger Ledger, Learned? Change) Learn(FactionLedger ledger, FactionDefinition faction, long actSeq,
    string source, string via, string identity, long tick, StandingLadder ladder)
{
    var act = ledger.Acts.Single(a => a.Seq == actSeq);
    var row = faction.Reactions.SingleOrDefault(r => r.Kind == act.Kind && r.Subject == act.Subject);
    if (row is null) return (ledger, null);                                    // K7
    var known = ledger.Knowledge.SingleOrDefault(k => k.Knower == faction.Id && k.Act == actSeq);
    bool upgrade = known is { Identity: Identities.Unidentified } && identity == Identities.Identified;
    if (known is not null && !upgrade) return (ledger, null);                  // K8: idempotent
    int delta = 0, from = PointsOf(ledger, faction.Id), to = from;
    if (identity == Identities.Identified)
    {
        to = Math.Clamp(from + row.Delta, ladder.OrdinaryFloor, ladder.MaxPoints);
        delta = to - from;
    }
    var row2 = new FactionKnowledge(faction.Id, actSeq, identity, source, via, tick, delta);
    return (ledger.WithKnowledge(row2).WithPoints(faction.Id, to),
            new Learned(faction.Id, actSeq, source, via, identity, upgrade, from, to));
}
```

| Existing row | New knowledge | Result |
|---|---|---|
| none | unidentified | add the row, `Delta = 0` |
| none | identified | add the row; apply the clamped delta; store it in `Delta` |
| unidentified | identified | upgrade: overwrite `Identity`, `Source`, `Via`, `Tick`; apply once |
| unidentified | unidentified | no change |
| identified | anything | no change |

`Delta` records what actually moved after the clamp, so the knowledge rows are the bounded attribution log PERSISTENCE T-21 asks for ("Reputation values plus their bounded event log persist", `docs/PERSISTENCE.md:595`).

### 6.4 Repeated acts

- **One act instance:** applied at most once per faction, whatever the number of witnesses and reports.
- **Distinct instances of the same subject:** each applies. Farming a faction by deeds is sanctioned (`docs/PROGRESSION.md:123`).
- **Content cannot reach unbounded repeats in M7:**
  - lint FAC-R5 allows a `creature_killed` row only if every spawner placing that creature has `respawn: none`;
  - switches set once.
  A repeatable subject needs a repeat rule first (GAMEPLAY_LOOPS E-9, "diminishing-reward policy"), which is M9 tuning.
- **Decay is deferred** (scope §4). A later decay needs a "last changed" tick: the migration that adds it takes the latest knowledge-row tick per faction.

### 6.5 Retirement (PERSISTENCE I-7)

- **Cap:** `acts.log_capacity`, 256.
- **Eviction:** when a new act would exceed the cap, evict the lowest-`Seq` act that is *settled*: every faction with a row for it has an identified knowledge row. If none is settled, evict the lowest `Seq`. Remove its knowledge rows with it. Standing never changes on eviction.
- **Reach:** shipped content records at most two acts. The cap is a backstop, proven by one test.

---

## 7. Items 4 and 5 - the factions and the proof case

Names are working names, not canon: "Names are working names until cultural naming is finalized" (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:265`). They are plain descriptions, not cultural names. IDs follow the content's social scoping (`npc.ashen_hollow.*`, `merchant.ashen_hollow.*`) and avoid `faction.hollow.wardens`, which `tests/Content.Tests/QuestContentTests.cs:120` uses as a deliberately missing reference.

Peoples (Veth, Kal, Siann, Orenth) are not factions: "Race is biology. Culture/community/polity is social" (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:623-631`). Neither faction is keyed to a people.

### 7.1 The Waystation - `faction.ashen_hollow.waystation`

| | |
|---|---|
| Working name | the Waystation |
| Purpose | Keep Ashen Hollow's road stop running: shelter, a forge with iron, a road travellers survive |
| Values and interests | A stop that is safe to reach; iron for the forge; useful work ("Kera needs iron and a steady pair of hands", `content/dialogue/ashen_hollow/renn_vale.yaml:53`; "Iron doesn't care how you got to it", `kera_voss.yaml:75`) |
| Why here | It is Ashen Hollow's only community. Renn is the "practical local authority" (bible `:267-269`): "I keep this waystation, such as it is." (`renn_vale.yaml:11`). Implied group G1 in `research/world_lore.md` §4.1 |
| Acts it cares about in M7 | The Blackvein sentinel destroyed: +100 |
| Relation to the Survey | `cordial`: it houses her work at its survey table |
| Members | `npc.ashen_hollow.renn_vale`, `npc.ashen_hollow.kera_voss` |
| What it proves | Report knowledge; the service gate; an approving faction whose members' personal regard is unmoved (Kera's `respect` and `trust` never change on a report) |

### 7.2 The Survey - `faction.ashen_hollow.survey`

| | |
|---|---|
| Working name | the Survey |
| Purpose | Sel Arien's survey of the Foldscar and the old workings around Ashen Hollow, kept for an institution the fiction leaves unnamed |
| Values and interests | An accurate record of old things before they are used up ("half of what's on it is older than the waystation", `sel_arien.yaml:11`); the Quiet Stones set right ("I think they matter", `:93`) |
| Why here | The Foldscar is its subject; Sel's post faces it (`ashen_hollow.yaml:153`). Implied group G3 |
| Acts it cares about in M7 | The Foldscar's heart steadied: +100. The Blackvein sentinel destroyed: -100 |
| Relation to the Waystation | `cordial` |
| Members | `npc.ashen_hollow.sel_arien` |
| What it proves | Disapproval of an act another faction approves; the dialogue gate; a gate that opens and then closes; the split between Sel's personal trust and the Survey's institutional view |

A one-member faction is deliberate. Tavar would be the obvious second member ("He guides my surveys", `sel_arien.yaml:93`), but he is a hired Orenth guide, whose law is "contractual and portable" (`docs/MYTHOLOGY.md:215-219`). As the companion, he would also be a travelling witness (section 5.2). The Survey's second member arrives when M9 content has one who stays at a seat.

### 7.3 A third faction: considered and rejected for M7

"Whoever turned the Quiet Stones out of line" (`sel_arien.yaml:93`; `tavar_orr.yaml:43`) is the strongest opposed seed in Phase-1 content (G4). As a faction it would disapprove of steadying the heart and have no members present. It would prove one thing: a faction without knowledge never updates. The pair already proves that, when the player tells one faction and not the other. It would also invent lore about who turned the stones. The fixture table proves the "no member present" case with a fixture faction (row F12) instead. The stone-turners are the natural M9 candidate.

### 7.4 The proof case

**One act: the player destroys the Animated Armour in Blackvein Cut.**
- **To the Waystation** it was a guard standing between the stop and its iron. Scrap, and the seam can be worked: **+100**.
- **To the Survey** it was an old working still keeping a post nobody remembers giving it, the only testimony about who worked Blackvein and why. Destroyed before it was recorded: **-100**.

Neither faction is the villain. This is the small-scale shape of the setting's "war crime to one people and a necessity to another" (`docs/WORLD_MATERIALS.md:81`).

It is a real choice:
- The sentinel holds its post (role `sentinel`) at (65, 34), 38 m from the seam at (28, 45).
- "The player should be able to get ore without being forced to defeat every enemy" (bible `:219`).
- Quest 1 has no kill objective (`content/quests/ashen_hollow/iron_under_ash.yaml`).
- A player can sneak past the sentinel, or smash it and then tell, or not tell.

| How the player plays it | Waystation | Survey |
|---|---|---|
| Kills it, tells no one | 0, neutral (does not know) | 0, neutral (does not know) |
| Tells Kera | +100, accepted (reported via Kera) | 0 (does not know) |
| Tells Sel | unchanged | -100, wary (reported via Sel) |
| Tells both | +100 accepted | -100 wary: **the same act, opposite directions** |
| Tells Sel twice (impossible in content; forced in a test) | - | -100 once |

### 7.5 The M7 acceptance walk (shipped content)

1. Take Quest 1, kill the armour, mine ore. Act #1 is recorded. Nobody knows. Kera's billets are not listed.
2. Tell Kera "The armour at the seam won't stand guard any more." The Waystation goes +100, to accepted. Kera's billets are listed, and a `BuyCommand` for one succeeds; before this, the same raw command was refused.
3. Quest 2: turn the three stones and steady the heart. Act #2 is recorded. Nobody knows: Tavar is no member.
4. Speak to Tavar, then tell Sel "Tavar's free." The Survey goes +100, to accepted, by report. Sel's `trust` +5 is the reply's authored, separate relationship event. Sel's `notes` reply is now offered.
5. Tell Sel "The armour in Blackvein Cut is down." The Survey goes -100, to 0 and neutral. `notes` is hidden again. Sel's trust does not move.
6. Save, quit and load. Standings, acts and knowledge rows are identical; the gates are in the same state; `StateDump.Compare` shows 0 differences.

The player who wants Sel's notes keeps quiet about the armour, or spares it. That is legible in play, and it is the whole point of "information is not magically global".

---

## 8. The two gates

### 8.1 Dialogue gate: `reputation` (Sel's notes)

**Condition.** DATA_MODEL already names the kind `reputation` (`docs/DATA_MODEL.md:525-526`); M7 builds it with tier keys.

```yaml
{ kind: reputation, faction_ref: faction.ashen_hollow.survey, min_tier: accepted }   # max_tier optional; not: refused
```

**Code hooks:**
- **Domain:** `public sealed record ReputationCondition(string FactionId, int MinLevel, int MaxLevel) : DialogueCondition;` in `src/Domain/Social/Social.cs`. `IDialogueFacts` gains `int StandingLevel(string factionId);` (`:125-144`). `DialogueRules.Holds` gains `ReputationCondition r => facts.StandingLevel(r.FactionId) is var l && l >= r.MinLevel && l <= r.MaxLevel` (`:148-159`).
- **Content:** `SocialContent.Conditions` gains `"reputation"` (`src/Content/SocialContent.cs:26-27`). `Condition(...)` gains a parse arm resolving tier keys to levels through the built ladder; `min_tier` defaults to `anathema`, `max_tier` to `exalted` (`:159-184`).
- **Runtime:** `DialogueSystem` and `SpeakerFacts` implement `StandingLevel` as `Setup.Factions.Ladder.TierOf(State.StandingOf(id)).Level` (`src/World/Runtime/Social.cs:397-459`).
- **Debugger:** `QuestDebugger.DescribeCondition` gains an arm, for example "needs the Survey at accepted or better; it is neutral (0)" (`src/World/Runtime/QuestDebugger.cs:372-387`).

**Why it cannot be bypassed:** `Handle(ChooseCommand)` refuses a reply whose conditions do not hold (`Social.cs:264-266`), and `View()` hides it (`:306`).

**Effect:** the reply `notes` in Sel's `again` node, offered while the Survey is at `accepted` or better (section 16.5).

### 8.2 Service gate: Kera's iron billets

**Content:** one new stock row, appended last, so existing ware refs (`#00`..) keep their indices (`Items.cs:490`):

```yaml
  - { item_ref: item.material.iron_ingot, count: 3, price_bias: 1.0, requires: { faction_ref: faction.ashen_hollow.waystation, min_tier: accepted } }
```

**Code hooks:**
- **Domain:** `MerchantStock` (`src/Domain/Items/Items.cs:220`) gains `public StandingRequirement? Requires { get; init; }`. `MerchantOf` parses `requires` (`src/Content/ItemContent.cs:191-204`). The nested `faction_ref` is already resolved by the generic walker.
- **Runtime:** `TradeSystem` (`src/World/Runtime/Social.cs:468-551`) gains one private check:

```csharp
private string? Withheld(NpcState npc, Merchant merchant, string itemId) =>
    merchant.Stock.FirstOrDefault(s => s.ItemId == itemId)?.Requires is { } gate
    && _context.Setup.Factions.Ladder.TierOf(State.StandingOf(gate.FactionId)).Level < gate.MinLevel
        ? $"{npc.Definition.Name} will not sell you that" : null;

// Handle(BuyCommand): after the count check (:491-492), before pricing (:493):
//     if (Withheld(npc, merchant, ware.DefId) is { } withheld) return withheld;
// View(npcId) (:526-528): .Where(i => Withheld(npc, merchant, i.DefId) is null) - a withheld ware is not listed
```

**Predicate.** A ware whose item has a gated stock row at this merchant is sold only while the player's tier with the gate's faction has at least `MinLevel`. The gate is keyed by item definition, so a billet the player sold Kera is withheld too ("the waystation's own come first"). Selling to Kera is not gated in M7.

**Why it cannot be bypassed:**
- `Handle(BuyCommand)` is the only path that moves a trader's wares. `Trade` is dispatched only from there, and `InventorySystem.Check` refuses `MoveItem` and `TakeAll` on wares (`Items.cs:136-142`, `:450-459`).
- Presentation cannot construct an internal command (`src/World/Runtime/Systems.cs:14-15`).
- The check is in the authority, not in dialogue: `open_service` is only an event (`Social.cs:373-375`), and the simulation accepts a `BuyCommand` with no conversation open.
- A raw `BuyCommand` naming the billet ware's ref is refused with a reason, which `CommandRejected` already toasts (`src/Presentation/Main.cs:780-783`).

**Why this gate, not the two alternatives.**

*Gating the existing hide vest and cap (the minimal candidate):*
- It removes Phase-1 stock at Neutral. The cap exists for C18 reachability (`content/merchants/ashen_hollow/kera_voss.yaml:11`).
- It breaks `NpcTests.BuyingAndSelling_MoveCoinAndGoodsExactly_AndRefuseCleanly` (`tests/Application.Tests/NpcTests.cs:296-307`). That test boots shipped content and expects the vest listed and refused only for coin.

*A whole-trade gate below Neutral:* unreachable, because no Waystation-negative act exists in Ashen Hollow.

*The billet row:* it is additive, it changes nothing at Neutral, and it fits the fiction: the sentinel's death opens the seam, and the smith keeps iron for the stop's friends. It is also PROGRESSION's own example of what reputation gates, "prices and stock" (`docs/PROGRESSION.md:440`).

**It does not bypass Quest 1.** `o_billet` is a `craft_item` deed; a bought billet does not satisfy it (`iron_under_ash.yaml`). Billets already exist in the world: the den cache holds 3, and the armour drops one half the time.

**Migrated saves (open issue 3).** A Kera wares container a pre-M7 save already traded with is a persisted record, and it never re-reads stock (`Items.cs:488-490`). Such a save never sees the billets. Migrations may not read content (`Migrations.cs:49-53`), so this is accepted. The M7 proof runs on a new game.

---

## 9. The M7 rule for faction reads, and the Phase-1 leaks

Phase-1 dialogue reads global player state: cross-dialogue `visited`, `relationship` on any `npc_ref`, `quest_state`, `has_item` (`research/social_quests_code.md` §7). **M7 does not fix Phase 1.** It adds five lint-enforced rules (in `FactionContent`, code FAC001) for every faction read or write:

- **R1.** A `reputation` condition may name only a faction of which every participant of its conversation is a member. What an NPC's own people think of the player is theirs to know, and it moved only through what they learned.
- **R2.** No condition reads another faction's standing, and no condition reads a knowledge row. There is no `faction_knows` condition.
- **R3.** `act_done` reads ground truth: the player's own act log. It may appear only on a reply that also carries `report_act` for the same kind and subject. Ground truth then gates only the player's own decision to tell, which the player knows. It never gates what an NPC says unprompted. `act_done` holds iff the log holds at least one act with that kind and subject.
- **R4.** `report_act` may appear only in a conversation whose participants all belong to one faction that has a reaction row for that kind and subject.
- **R5.** Nothing but `FactionSystem` writes standing.
  - The dialogue consequence `add_reputation` stays refused, with the specific reason "reputation moves only through acts a faction learns of (M7)".
  - The quest reward `reputation` stays in `RewardKinds.NotBuilt` (`src/Domain/Quests/Quests.cs:205-206`).
  - `faction_reputation` and `faction_state` stay in `ObjectiveTypes.NotBuilt`, their reason changed from "factions (M7)" to "faction quest content (M9)" (`:154-155`). M7 has no content user for them, and a quest predicate reading global standing is a question for M9's quest reconciliation.
  - Merchant `requires.faction_ref` must be the faction of every NPC whose `merchant_ref` names that merchant.

**Grandfathered:** the existing Phase-1 reads are unchanged. M7 adds no new cross-NPC read. Sel's `tavar_back` reply is offered on a cross-dialogue `visited` check of Tavar's greeting (`sel_arien.yaml:23`, `:60`), which is an existing leak. The `report_act` M7 adds to it is legitimate: the Survey learns from the player's words ("Tavar's free."), not from the condition. **Recommendation for M9:** hold `relationship` conditions to the speaker, as R1 holds `reputation`.

---

## 10. Relations, membership, and where faction state lives

### 10.1 Static cross-faction relations

- **Form:** `relations: [{ faction_ref, attitude }]`.
  - `attitude` is a closed word set: `close`, `cordial`, `indifferent`, `strained`, `opposed`.
  - Relations are directional (the canon inter-people matrix is directional, `docs/MYTHOLOGY.md:421-430`): at most one row per target, never self.
  - `allied` is deliberately not an attitude word, because it is a standing tier.
- **What it replaces:**
  - DATA_MODEL's `attitude_default` in [-1, 1] (`docs/DATA_MODEL.md:560`), which would be a third numeric scale beside standing and relationships (C-4);
  - `enemy_of` "(hard hostility edges)" (`:563`), which would fuse relation with hostility.
  Words instead of numbers mean no code can do arithmetic between relation and standing.
- **What it does in M7:** nothing in any rule.
  - It is data, lint-checked, shown in the debug view and the fixture table.
  - A test pins that an act moving one faction never moves another (fixture row F11).
  - Its first reader will be the reserved hostility derivation (faction relation and war state, beside legal status, identity and perception, `docs/PROGRESSION.md:444-450`), or NPC flavour lines.
- **M7 content:** Waystation → Survey `cordial`, and Survey → Waystation `cordial`. The proof needs two factions with incommensurable interests that are *not* enemies: the player can be accepted by one and wary with the other while the factions stay cordial. That is ruling 3's separation, made visible.

### 10.2 Membership and the PROTOTYPE A-2 gap

- **Form:** membership is one optional `faction_ref` on the NPC definition, where DATA_MODEL already puts it (`docs/DATA_MODEL.md:297`).
  - `NpcDefinition` gains `public string? FactionId { get; init; }` (`src/Domain/Social/Social.cs:10-16`), read by `BuildNpcs` (`src/Content/SocialContent.cs:90-93`).
  - The faction definition has no `members:` list; `members` is refused by name, so there is one source of truth.
  - `FactionSetup.MembersOf(factionId)` is derived at content build, ordinal-sorted.
- **The A-2 gap.** PROTOTYPE says "`faction_id` field exists on NPCs and is saved" (`docs/PROTOTYPE.md:110`, `:395`). No such field exists (`Social.cs:10`), and non-companion NPC bodies are not saved at all (`src/World/Runtime/Social.cs:98-104`). M7's resolution:
  - membership is definition data while nothing can change it, so it needs no save field (I-1, "store nothing derivable");
  - saved, instance-level membership arrives with the first system that changes membership (joining, defection, generated members);
  - A-2's text is corrected in the M7 reconciliation (section 18).
- **The player is never a member in M7.** Joining is deferred.

### 10.3 Where faction-global state lives

**There is no mutable faction-global state in M7.**
- Standing, the act log and knowledge are all about the player, so they live on the player record, like relationships since schema 10 (`src/World/Runtime/RuntimeState.cs:63-64`). PERSISTENCE §5.1 already puts "faction reputation and crime records" in the player section (`docs/PERSISTENCE.md:188`).
- Relations and membership are content.
- World flags stay cell-scoped and are not used for factions. A faction fact tied to one arbitrary cell would be wrong (`research/social_quests_code.md` §4).
- **The first genuinely world-global faction state** (war state, faction control, NPC-authored acts, faction-to-faction sharing) needs a world-global save record that does not exist today. When it arrives, the act log moves there by migration. `FactionKnowledge.Knower` is already an explicit field, and the act gains `actor` in that step (section 4.5).

---

## 11. Item 6 - personal relationship vs faction standing

| | Personal relationship | Faction standing |
|---|---|---|
| Holder | one NPC | one faction |
| Scale | 5 dimensions, int [-100, 100] | one int [-1000, 1000] plus a derived tier |
| Moved by | `ChangeRelationship`, from dialogue `record_relationship_event` or a quest `relationship` reward (`src/World/Runtime/Social.cs:370-372`; `Quests.cs:165-168`) | `FactionSystem.Learn`, only on a learned act |
| Slice | `StateSlice.Relationships`, owner `RelationshipSystem` | `StateSlice.Factions`, owner `FactionSystem` |
| Read by | the `relationship` condition and the `relationship_value` objective | the `reputation` condition, the trade gate, views |

**They do not interact in M7.**
- No code converts one into the other. `FactionSystem` never dispatches `ChangeRelationship`, and `RelationshipSystem` never dispatches to factions.
- Nothing is replaced: the eight shipped relationship writes stay exactly as authored.
- Content may carry both on one reply, as two explicit consequences. Sel's `tavar_back` keeps its `trust +5` and gains an independent `report_act`: Sel is personally grateful, and the Survey as an institution judges the steadying.
- **Divergence is the point:**
  - telling Sel about the armour costs the Survey 100 and Sel's `trust` nothing;
  - Sel's trust (+20 over Quest 2 in shipped content) cannot open `notes`, only the Survey's standing can;
  - Kera's `respect` +10 for fine work (`kera_voss.yaml:52`) says nothing about the Waystation.
  "Someone can like the player, trust them, respect their skill, and still despise their necromancy." (`docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md:52`)
- A test pins both directions (section 17.2).

---

## 12. Item 7 - hostility: out of M7, with the separation enforced

M7 requires no faction hostility and no combat against NPCs:
- the player cannot strike an NPC (`src/World/Runtime/Combat.cs:496`, `:554`), and NPCs have no health (`NpcState`, `src/World/Runtime/Social.cs:88`);
- creatures carry no faction, and tactical hostility is their perception-driven mind (`src/World/Runtime/Creatures.cs:36`).

| Ruling-3 concept | M7 state | Where |
|---|---|---|
| Personal relationship; trust, fear, grudge | exists (M4), untouched | `Relationships` |
| Reputation / standing | built | `FactionStanding` |
| Legal status | not built; nothing writes or derives it | reserved: per-jurisdiction records, never a single bounty number (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:118-128`) |
| Faction relation | static content | `FactionDefinition.Relations` |
| War state | not built | reserved: a world-global record |
| Known identity | two rungs per knowledge row | `FactionKnowledge.Identity` |
| Tactical threat | creature mind (M3d) | `CreatureRecord` |
| Attack legality | not built; the `CanAttemptAttack(attacker, target, context)` seam is untouched | `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:825` |

**Guard:** Architecture.Tests gains `TacticalCode_NeverReadsFactionState`, a source scan in the style of `PresentationSource_NeverReachesPastThePublicReadAndCommandSurface` (`tests/Architecture.Tests/ArchitectureTests.cs:163-182`).
- It scans `src/World/Runtime/Combat.cs`, `Creatures.cs`, `Companions.cs` and `Magic.cs`.
- It fails on any of: `State.Factions`, `StandingOf`, `Setup.Factions`, `FactionRules`, `TierOf`, `StandingLevel`.
- The `new RecordAct(` dispatch in `Creatures.cs` contains none of them, so it passes.

**The coupling text goes** (section 18): VERTICAL_SLICE's "Hostile" tier and "puts town guards on you" (`docs/VERTICAL_SLICE.md:95`, `:169`) are flagged for M9, and S-27's "legal/offense state derived from it" (`docs/SYSTEMS.md:304`) is removed.

---

## 13. Building: does it produce a faction-relevant act in M7?

**Decision: no faction act in M7.**
- The building system dispatches no `RecordAct`.
- `piece_placed` (and `piece_destroyed`) are reserved keys in `ActKinds.NotBuilt`.
- A reaction row naming them is refused by lint with the reserved reason.

**Why:**
1. **A reaction to placement turns items into standing.** Placement consumes materials and dismantle refunds part (scope ruling 13). A place-dismantle-place loop would buy standing with materials, which E-7 forbids ("Reputation and access are earned through deeds and faction relationship, never purchase", `docs/GAMEPLAY_LOOPS.md:238`). AGENTS.md says the same for every axis ("never add an overload that takes gold, items or another axis's currency"). A safe `placed` reaction needs a repeat rule (once per faction, or only for a piece that still stands after some time), and that is M9 tuning.
2. **Nothing needs it.** The ROADMAP exit is met by the armour. PROGRESSION's link between building and reputation runs the other way: standing gates "settlement-building rights" (`docs/PROGRESSION.md:440`). That is a read, deferred with territory gating.
3. **Retrofit is free of save cost.** Act kinds are stored as string keys, so adding `piece_placed` later is one entry in `Built`, one dispatch line after a committed placement, and one lint rule. No migration.
4. **A dispatch whose every act is dropped is dead code.** The relevance filter would discard every placement in production. A fixture proving only that one line runs is not worth the surface.

**What building does do for factions in M7: occlusion.**
- `SystemContext` gains `public IEnumerable<Blocker> SightWalls()`, returning `Setup.Layout.Space.Blockers ∪ ClosedDoors()` today. `FactionSystem` uses it for K2. `CreatureSystem.Walls()` (`Creatures.cs:893`) delegates to it: one line, no behaviour change.
- **Building's contract:** placed walls and closed placed doors join `SightWalls()`. Placed walls then hide acts from witnesses exactly as they hide the player from creatures. Whether `Combat.cs:570` and `Companions.cs:598` switch to the helper is building's decision; they need the placed pieces anyway.
- Fixture row F6 proves occlusion with authored walls, and building adds one row with a placed wall.

---

## 14. Item 8 - persistence

### 14.1 Where

Everything lives in `player.msgpack`, following the relationships pattern (schema 10):
- no world-delta change;
- no new section file, so no integrity-root trap (`research/persistence.md` gap 1);
- no `DeltaSnapshot` property, so none of the hand-listed-property pitfalls in `SaveLoader.ResolveDefinitions` or `SemanticRebase`.

### 14.2 `PlayerRecord` (`src/World/PlayerState.cs`)

- **New property:** `public FactionLedger Factions { get; init; } = FactionLedger.Empty;`. Its setter validates, throwing `ArgumentException`, as `Posture` does (`:306-320`):
  - `NextActSeq >= 1`;
  - acts strictly ascending by `Seq`, each `1 <= Seq < NextActSeq`, `Kind` in `ActKinds.Built`, `Subject` a valid definition ID, `CellKey` parseable;
  - knowledge sorted by (`Knower`, `Act`), unique; each `Act` present in `Acts`; `Knower` a valid `faction.*` ID; `Identity` and `Source` in their closed sets; `Delta == 0` when unidentified;
  - standing sorted, unique, `Points != 0`, in [-1000, 1000].
- **Every `With*` copy** carries `{ Posture = Posture, Factions = Factions }` (`:222-253`).
- **Digest:** the tag goes from `unnamed.player/v9` to `v10`, hashing the whole ledger, field by field, after posture (`:328`, `:365`).
- **Runtime wiring:**
  - `Simulation.CaptureRecord` adds `Factions = _state.Factions` beside `Posture` (`Simulation.cs:342-351`);
  - `RuntimeState` reads `player.Factions` in its constructor;
  - `RuntimeState` gains `StateSlice.Factions` ("The player's act log, what factions know of it, and standing (S-27; M7). Saved with the player (schema 14)."), `FactionLedger Factions`, `int StandingOf(string factionId)` (0 when absent), and `SetFactions(SliceOwner owner, FactionLedger ledger)`.

### 14.3 DTO (`src/Persistence/SectionCodec.cs`)

```csharp
/// Required from schema 14. The 13 -> 14 step gives older saves an empty ledger: nothing before M7 was witnessed or told.
[Key("factions")] public FactionsDto? Factions { get; set; }        // on PlayerDto

public sealed class FactionsDto
{
    [Key("next_act_seq")] public long NextActSeq { get; set; }
    [Key("acts")] public ActDto[] Acts { get; set; }
    [Key("knowledge")] public KnowledgeDto[] Knowledge { get; set; }
    [Key("standing")] public StandingDto[] Standing { get; set; }
}
public sealed class ActDto       { seq:long, kind:string, subject:string, cell_key:string, x_mm:long, z_mm:long, tick:long }
public sealed class KnowledgeDto { knower:string, act:long, identity:string, source:string, via:string?, tick:long, delta:int }
public sealed class StandingDto  { faction_id:string, points:int }
```

- Enum-like values are string keys, never ordinals: `creature_killed|switch_set`, `witnessed|reported`, `unidentified|identified`.
- `DecodePlayer` throws `FormatException("player.msgpack has no factions (required from schema 14)")` when the field is null, following the posture pattern (`SectionCodec.cs:399-416`). The `PlayerRecord` validation then rejects bad keys and ranges.

### 14.4 Migration: one `SchemaV13ToV14` step, shared with building

- It freezes the current `PlayerDto` as `Sections/SchemaV13.cs` `V13.Player`, and repoints `SchemaV12ToV13` to write `V13.Player` (`Migrations.cs:710-747`; `tests/Persistence.Tests/Fixtures/README.md` policy 2).
- It reads `V13.Player` and writes the current `PlayerDto` with `Factions = { next_act_seq: 1, acts: [], knowledge: [], standing: [] }`.
- Building's world-delta change goes in the same step. The summary starts `"schema 13 -> 14:"`, and the step is appended to `SchemaMigrations.Production` (`:69-81`). `SaveModel.SchemaVersion` becomes 14 (`SaveModel.cs:20`).
- **Pre-M7 acts are not reconstructed.** A save in which the heart was already steadied or the armour already destroyed loads with an empty log and neutral standing. Its report replies are never offered, because `act_done` is false. Reconstructing acts from world flags or corpses would be a guess, and migrations never resolve content (`Migrations.cs:49-53`). The rule: "factions arrived with M7; nothing before was witnessed or told".

### 14.5 Definition-ID pass (`SaveLoader.ResolveDefinitions`, beside `:316-357`)

| Stored ID | Removed from content | Two IDs resolve to one |
|---|---|---|
| `standing.faction_id` | the row is dropped; loss reported | points summed, then clamped to [-999, 1000] |
| `knowledge.knower` | the row is dropped | union by (`knower`, `act`); an identified row beats an unidentified one, then the lower `tick` |
| `acts.subject` (creature or flag definition) | the act and its knowledge rows are dropped; standing is kept; loss reported | the subject is rewritten |
| `knowledge.via` (NPC) | set to null; the row survives | rewritten |

The ledger is rebuilt as `player with { Factions = resolved }` at the chain's end (`:357`).

### 14.6 Digests, dumps, fixtures

- **`StateDigest`** covers the ledger through `PlayerRecord.Digest`.
- **`StateDump`** picks it up automatically, because the dump serialises `CaptureRecord()` (`src/Application/StateDump.cs:25-33`). `tests/Persistence.Tests/CanonicalState.cs` renders it by hand, field by field.
- **The v14 fixture** is written by `M2Fixtures.Historical.Player()` (`tests/M2.Probe/M2Fixtures.cs`) and carries the proof in stored form:
  - act 1: `creature_killed` of `creature.beast.wolf_grey`, known identified by fixture faction `faction.fixture.keepers` (+100, via `npc.fixture.warden_sera`) and by `faction.fixture.delvers` (-100, reported);
  - act 2: `switch_set` of a fixture flag, known unidentified by `delvers`;
  - standing: keepers +100, delvers -100.
- **Writer and current packs:**
  - the writer pack `Fixtures/content-0.1.7` = 0.1.6 plus the two fixture factions, `config.factions`, `faction_ref` on the warden, the fixture flag, and whatever building adds;
  - the current fixture pack (0.2.8 → 0.2.9) aliases `faction.fixture.delvers` → `faction.fixture.diggers` in `_aliases.yaml`, which proves a rename reaches standing and knowledge rows.
- **`expected.json`:** older fixtures gain only the empty ledger, reviewed line by line.
- **Nothing new is transient.** S-27's "witness-propagation working set" and "service-availability cache" (`docs/SYSTEMS.md:308`) do not exist: witnessing is synchronous at the act, and gates are evaluated live. Save-then-continue equals continue by construction, and there are no in-flight reports to persist (C-13).

---

## 15. Code plan (files, types, hooks)

| File | Change |
|---|---|
| `src/Domain/Factions/Factions.cs` (new) | Section 3.1 types; `FactionRules`: `Learn`, `BestWitness`, `Compact`, `PointsOf`, `IsSettled` |
| `src/Domain/Social/Social.cs` | `NpcDefinition.FactionId`; `ReputationCondition`, `ActDoneCondition(string Kind, string Subject)`, `ReportActConsequence(string Kind, string Subject)`; `IDialogueFacts.StandingLevel`, `.ActDone`; two `Holds` arms |
| `src/Domain/Items/Items.cs` | `MerchantStock.Requires` init property |
| `src/World/Runtime/Factions.cs` (new) | `FactionSetup(ImmutableSortedDictionary<string, FactionDefinition> Factions, StandingLadder Ladder, WitnessRules Witness, int LogCapacity) { Empty; Relevant; ReactorsTo(kind, subject); MembersOf(id) }`; `RecordAct`, `ReportAct`; `FactionSystem` (owns `StateSlice.Factions`, no `Tick`); events; views |
| `src/World/Runtime/RuntimeState.cs` | `StateSlice.Factions`; `Factions`; `StandingOf`; `SetFactions` |
| `src/World/Runtime/Simulation.cs` | `SimulationSetup.Factions` (init, default `FactionSetup.Empty`); `_factions` composed after `_relationships` (`:132`); two `Dispatch` arms; `CaptureRecord`; get-only `Factions` and `Acts` properties (no allow-list change) |
| `src/World/Runtime/Systems.cs` | `SystemContext.SightWalls()`; `RecordAct` in `Work` after `:288` |
| `src/World/Runtime/Creatures.cs` | `RecordAct` in `Die` after `:706`; `Walls()` delegates to `SightWalls()` |
| `src/World/Runtime/Social.cs` | `DialogueSystem.Apply`: `case ReportActConsequence r: _context.Dispatch(new ReportAct(r.Kind, r.Subject, open.NpcId))`; facts `StandingLevel` and `ActDone` in `DialogueSystem` and `SpeakerFacts`; `TradeSystem.Withheld` in Buy and View |
| `src/World/Runtime/QuestDebugger.cs` | `DescribeCondition` arms for `reputation` and `act_done` |
| `src/World/PlayerState.cs` | `Factions` property, validation, `With*` carry, digest v10 |
| `src/Content/FactionContent.cs` (new, code FAC001) | `Validate` (sections 9 and 16.6 lints) and `Build` (definitions, ladder, witness rules, relevance) |
| `src/Content/SocialContent.cs` | NPC `faction_ref`; `reputation`, `act_done` parse; `report_act` parse; the `add_reputation` refusal text |
| `src/Content/ItemContent.cs` | stock-row `requires` parse |
| `src/Content/ContentLoader.cs` | call `FactionContent.Validate` after `SocialContent` (`:190`) |
| `src/Application/GameSession.cs` | `Factions = FactionContent.Build(loader)` in the `SimulationSetup` initializer (`:88-99`) |
| `src/Persistence/*` | section 14 |
| `src/Presentation` | `Main.cs`: subscribe to `ReputationChanged` beside `RelationshipChanged` (`:778`); a read-only debug panel in `src/Presentation/Ui` |

**Events** (public records, `src/World/Runtime/Factions.cs`):
- `ActRecorded(long Seq, string Kind, string Subject, string CellKey, long Tick)`
- `FactionLearned(string FactionId, long ActSeq, string Source, string? Via, string Identity, bool Upgraded, long Tick)`
- `ReputationChanged(string FactionId, int From, int To, string TierFrom, string TierTo, long ActSeq, string Source, long Tick)`

These are S-27's `ReputationChanged` with the tier change folded in. `StandingTierChanged` is not a separate type.

**Views:**
- `FactionView(string Id, string Name, int Points, string Tier, int Level, string SeatLocationId, ImmutableArray<string> Members, ImmutableArray<RelationView> Relations)`
- `ActView(long Seq, string Kind, string Subject, string CellKey, long XMm, long ZMm, long Tick, ImmutableArray<KnowledgeView> Known)`

**`FactionSystem.Handle(RecordAct a, long tick)`:**
1. If `(a.Kind, a.Subject)` is not in `Relevant`, return null.
2. Append `ActRecord(NextActSeq, ...)`; increment `NextActSeq`; publish `ActRecorded`.
3. For each faction in `ReactorsTo(a.Kind, a.Subject)` (ordinal): build candidates from `State.Npcs.Values` (ordinal) per K2; if `FactionRules.BestWitness(...)` returns a witness, call `Learn(..., witnessed, witness.NpcId, identity, tick)`; publish `FactionLearned`, and `ReputationChanged` when `To != From`.
4. `Compact`; `SetFactions`; return null.

**`FactionSystem.Handle(ReportAct r, long tick)`:**
1. The faction is `Setup.Social.Npcs[r.SpeakerNpcId].FactionId`; if null, return null (lint R4 prevents it).
2. For each act matching (`r.Kind`, `r.Subject`) in ascending `Seq`, call `Learn(..., reported, r.SpeakerNpcId, identified, tick)` and publish as above.
3. `SetFactions`; return null.

**Presentation (no new command, no radial):**
- A HUD log line on `ReputationChanged` only when `Source == reported`: the character was there when they told. For example, "The Survey: wary (-100), told to Sel Arien". A witnessed change stays silent, because the character need not know they were seen.
- The debug panel lists factions, points, tier, seat, members and relations; the act log with each knowledge row (source, via, identity, delta); and both gate states.
- The wares panel needs no change: `Simulation.Wares` simply omits withheld rows.

---

## 16. Content (YAML)

### 16.1 `content/config/factions.yaml`

```yaml
id: config.factions
kind: config
schema: 1
display_key: config.factions.name
tags: [config]
notes: Factions and reputation (M7; PROGRESSION.md §10). One ladder for every faction; a standing is whole points and its tier is derived at read time, never saved. A faction learns an act only when a member sees the actor at the act or the player tells a member - no rumour, no telling between factions.
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
witness:
  sight_m: 30        # no further than the keenest Phase-1 creature sees (the ash ember hound)
  fov_deg: 140       # the grey wolf's cone: someone facing away does not see
  identify_m: 15     # nearer, they know who; farther, only that it happened
acts:
  log_capacity: 256  # the oldest act every reacting faction has settled goes first
```

### 16.2 `content/factions/ashen_hollow/waystation.yaml`

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

### 16.3 `content/factions/ashen_hollow/survey.yaml`

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

### 16.4 NPC and merchant additions

```yaml
# content/npcs/ashen_hollow/renn_vale.yaml and kera_voss.yaml - one line each
faction_ref: faction.ashen_hollow.waystation
# content/npcs/ashen_hollow/sel_arien.yaml
faction_ref: faction.ashen_hollow.survey
# content/npcs/ashen_hollow/tavar_orr.yaml - no faction_ref (a companion; refused in M7)

# content/merchants/ashen_hollow/kera_voss.yaml - appended as the last stock row
  - { item_ref: item.material.iron_ingot, count: 3, price_bias: 1.0, requires: { faction_ref: faction.ashen_hollow.waystation, min_tier: accepted } }   # M7: the waystation's own come first
```

### 16.5 Dialogue additions (all new text is DRAFT for the owner's tone review; no existing line's text or conditions change)

Kera, in `again`, a new reply before `trade`, and a new node:

```yaml
      - id: armour
        text: "The armour at the seam won't stand guard any more."
        conditions:
          - { kind: act_done, act: creature_killed, creature_ref: creature.construct.animated_armour }
          - { kind: visited, node: armour_down, not: true }
        consequences:
          - { command: report_act, act: creature_killed, creature_ref: creature.construct.animated_armour }
        next: armour_down
  armour_down:
    text: "Then the seam's only a seam again. I keep a few billets back for the waystation's own. Ask, and they're yours to buy."
    once: true
    next_if_exhausted: again
    choices:
      - { id: back, text: "I'll look.", next: again }
```

Sel, in `again`, two new replies, and two new nodes:

```yaml
      - id: notes
        text: "What's in the notes you keep back?"
        conditions: [{ kind: reputation, faction_ref: faction.ashen_hollow.survey, min_tier: accepted }]
        next: notes
      - id: armour
        text: "The armour in Blackvein Cut is down."
        conditions:
          - { kind: act_done, act: creature_killed, creature_ref: creature.construct.animated_armour }
          - { kind: visited, node: armour_down, not: true }
        consequences:
          - { command: report_act, act: creature_killed, creature_ref: creature.construct.animated_armour }
        next: armour_down
  notes:
    text: "The stones weren't knocked out of line. Each was turned the same quarter, by someone who knew the ring. That's more than I've written down."
    choices:
      - { id: back, text: "I'll keep it to myself.", next: again }
  armour_down:
    text: "Down. It kept that post longer than this waystation has stood, and now nobody can ask it who set it there."
    once: true
    next_if_exhausted: again
    choices:
      - { id: back, text: "It was in the way.", next: again }
```

Sel's two existing `tavar_back` replies (`sel_arien.yaml:21-27` in `greet`, `:57-65` in `again`) each gain one consequence, after the existing relationship event:

```yaml
          - { command: report_act, act: switch_set, flag_ref: world.foldscar.steadied }
```

Migrated saves stay safe:
- `report_act` is a no-op without a matching act;
- no existing reply gains a condition, so Quest 2's start and completion are untouched for schema-13 saves;
- `act_done` is false on a migrated save, so the new report replies are simply not offered.

### 16.6 Lint (`FactionContent`, FAC001)

**`config.factions`:**
- required iff any faction exists (the `config.companion` precedent, `SocialContent.cs:42-43`);
- exactly the 11 tier keys in PROGRESSION's order, levels +5..-5, `min` strictly descending, and `neutral` contains 0;
- `points.min = anathema.min`, `ordinary_floor = outcast.min`, `exalted.min <= points.max`;
- `0 < identify_m <= sight_m`, `0 < fov_deg <= 360`, `log_capacity >= 16`.

**Faction:**
- `name` and `seat_location_ref` are required;
- `act` is in `ActKinds.Built`; a `NotBuilt` kind is refused with its reason;
- exactly one subject field, matching the kind: `creature_ref` for `creature_killed`, `flag_ref` for `switch_set`;
- at most one row per (act, subject);
- `delta` is non-zero with `|delta| <= 250`;
- FAC-R5: a `creature_killed` subject must have every spawner at `respawn: none`;
- relations name an existing faction other than self, at most one row per target, with `attitude` in the closed set;
- refused by name, each with its reason: `members` ("membership is the NPC's faction_ref"), `player_start_reputation` and `reputation_tiers` ("one ladder, config.factions; every standing starts at 0"), `laws` ("crime is not built; law belongs to places, not to a faction"), `enemy_of` and `attitude_default` ("relations are attitude words; hostility is not a faction field"), `territory`, `services_gated`, `joinable`, `join_requirements` ("not built in M7").

**NPC:**
- `faction_ref` exists;
- FAC-M1: the NPC's authored placement lies within the faction's seat radius (read from each region layout that places the NPC; an unplaced member is an error);
- FAC-M2: `faction_ref` together with a `companion:` block is refused.

**Dialogue and merchant:** R1-R5 (section 9). A gated item appears in exactly one stock row of its merchant.

**Tests:** `LoadAll_Loads_Yaml_Files` gains `config.factions`, `faction.ashen_hollow.survey` and `faction.ashen_hollow.waystation` (`tests/Content.Tests/ValidationTests.cs:508-547`).

---

## 17. Item 9 - acceptance tests and the reputation fixture table

### 17.1 The reputation fixture table (the ROADMAP proof)

**What it is.** `docs/M7_REPUTATION_TABLE.md`, **generated from the build**, on the precedent of `docs/M3D_BEHAVIOUR_MATRIX.md`:
- the generator is `tests/Application.Tests/ReputationTableTests.cs`;
- it fails when the committed file differs from what the build produces;
- regenerate with `UNNAMED_WRITE_REPUTATION=1 dotnet test --filter ReputationTable` (from `src/`), the `UNNAMED_WRITE_MATRIX` pattern (`tests/Application.Tests/BehaviourMatrixTests.cs:78`);
- the test also asserts the signs of rows F1 and P5 directly, so the table cannot drift from the exit criterion.

**Its sections:**
1. **Ladder:** tier, level, points range, from `config.factions`.
2. **Reaction matrix (from content):** rows are (act, subject); columns are factions; each cell is `+100`, `-100` or `-`. A row whose signs differ across factions is marked **opposite**.
3. **Factions:** seat, members, relations (from content).
4. **Knowledge numbers:** sight, FOV, identify range, log capacity.
5. **Scenarios, measured in the real simulation:** one row per scenario, driven only through public `GameCommand`s.

**How the fixture scenarios (F) run.** A copy of the booted setup, following `NpcTests.cs:250-255` and `Arena.OpenCreatures` (`tests/Application.Tests/CombatTests.cs:54-60`):
- two fixture factions: A = `faction.fixture.keepers` (Renn, Kera) and B = `faction.fixture.delvers` (Sel). A: `creature_killed wolf_grey` +100. B: `creature_killed wolf_grey` -100 and `switch_set world.foldscar.stone_north_aligned` +100;
- members are moved by editing `Layout.Npcs` sites;
- a grey wolf is set to 1 health through the combat setup, so one `AttackCommand` kills it;
- coordinates are the test's own, and the table prints the measured distances.

**How the Ashen Hollow scenarios (P, N) run:** on the game's own content, by the Quest 1 and Quest 2 walks already used in tests.

**Table format and the rows it must contain:**

| # | Scenario | Act (seq, kind, subject, tick, cell) | Members in sight (faction: npc, m, identified?) | Reports | A / Waystation: source, via, identity, Δ, points, tier | B / Survey: source, via, identity, Δ, points, tier | Other checks | Proves |
|---|---|---|---|---|---|---|---|---|
| F1 | wolf killed; A and B members watch within 15 m | 1, creature_killed, wolf_grey | A: renn 6.0 yes; B: sel 10.0 yes | - | witnessed, renn, identified, +100, 100, accepted | witnessed, sel, identified, -100, -100, wary | | **the same act moves two factions in opposite directions (ROADMAP exit)** |
| F2 | F1, both members facing away | 1, ... | none | - | -, 0, neutral | -, 0, neutral | act 1 recorded | no knowledge, no change |
| F3 | B's member at 20 m | 1, ... | B: sel 20.0 no | - | - | witnessed, sel, unidentified, 0, 0, neutral | | an unknown actor moves nothing |
| F4 | F3, then tell Sel | 1, ... | as F3 | B, at a later tick | - | reported, sel, identified (upgraded), -100, -100, wary | one `ReputationChanged` | a report upgrades once |
| F5 | F1, then tell both | 1, ... | as F1 | A, B | unchanged | unchanged | no `ReputationChanged` | reports are idempotent |
| F6 | A's member inside the forge, wall between (4.4 m) | 1, ... | A: kera, walled | - | -, 0 | -, 0 | | walls hide acts |
| F7 | north stone turned; both watch | 1, switch_set, stone_north_aligned | A, B yes | - | no row stored (A has no reaction) | witnessed, +100, 100, accepted | | known without a row: nothing stored |
| F8 | two wolves killed, A watches | 1, 2 | A yes | - | +100, +100, 200, accepted | - | | distinct acts each apply |
| F9 | Tavar made an A member in the hand-built setup, recruited, following at 2.5 m | 1, ... | none (companion excluded) | - | 0 | - | | companions never witness (runtime guard behind lint FAC-M2) |
| F10 | F3, save and load between the act and the report | 1, ... | as F3 | B, after the load | - | -100, wary | `StateDump.Compare` 0 differences vs an unsaved control | continuity across a save |
| F11 | F1 with relations A→B `opposed`, B→A `close` | 1, ... | A only | - | +100 | 0 | | relations never move standing |
| F12 | wolf killed; a third fixture faction C reacts but has no member placed | 1, ... | none for C | - | | | C: no row, 0 | a faction with no member present never learns |
| F13 | a boar killed (no faction reacts) | none | - | - | 0 | 0 | `NextActSeq` unchanged | irrelevant acts are not recorded |
| F14 | 257 relevant acts, the first 200 settled | 1..257 | ... | ... | standing unchanged by eviction | | act 1 evicted, then act 2... | retirement: the oldest settled goes first |
| P1 | Ashen Hollow: armour killed | 1, creature_killed, animated_armour | none | - | -, 0, neutral | -, 0, neutral | billets not listed; raw `BuyCommand` refused | nobody knows |
| P2 | tell Kera `armour` | 1 | - | Waystation via kera | reported, kera, identified, +100, 100, accepted | -, 0 | billets listed; `BuyCommand` succeeds; Kera respect/trust unchanged | report knowledge; the service gate |
| P3 | heart steadied | 2, switch_set, steadied | none (Tavar no member) | - | - | -, 0 | | |
| P4 | tell Sel `tavar_back` | 2 | - | Survey via sel | - | reported, sel, identified, +100, 100, accepted | `notes` offered; Sel trust +5 (authored event) | the dialogue gate opens |
| P5 | tell Sel `armour` | 1 | - | Survey via sel | unchanged 100 | reported, sel, identified, -100, 0, neutral | `notes` hidden; Sel trust unchanged | **one act: Waystation +100, Survey -100**; the gate closes |
| N1 | control: armour killed, tell nobody | 1 | - | - | 0 | 0 | `act_done` holds; both report replies offered | no psychic factions |

### 17.2 Named tests

**Domain.Tests (pure `FactionRules`):**
- `TierOf_FollowsTheLadder_AtEveryBoundary` (including -99/-100, 99/100 and -999/-1000)
- `Learn_AppliesARowOnce_PerFactionPerAct`
- `Learn_Unidentified_KnowsButMovesNothing`
- `Learn_AReportUpgradesUnidentifiedExactlyOnce`
- `Learn_WithoutARow_StoresNothing`
- `Learn_ClampsAtTheOrdinaryFloor_AndTheCap`
- `BestWitness_IdentifiedBeatsUnidentified_ThenNearer_ThenLowerNpcId`
- `BestWitness_RangeFacingAndWallsHideTheActor`
- `Compact_EvictsTheOldestSettledActFirst`

**Application.Tests:**
- `ReputationTableTests`: the table above; F1 and P5 are the ROADMAP exit.
- `TheSameKnownAct_MovesTwoFactionsInOppositeDirections` (P1-P5 on shipped content).
- `AFactionThatNeitherSawNorWasTold_DoesNotUpdate` (N1, and P2 for the Survey).
- `AnUnknownActor_MovesNoStanding_UntilIdentified` (F3, F4).
- `RepeatedReportsAndWitnesses_ApplyOnce` (F5) and `DistinctActs_EachApply` (F8).
- `FactionState_ContinuesAcrossASaveAndLoad` (F10: `StateDump.Compare`, 0 differences) and `SaveThenContinue_EqualsContinue_WithFactions` (`StateDigest`).
- `FactionActs_ReplayFromTheCommandLog_EndIdentical` (the pattern of `DeterminismAndViewTests.cs:48-77`, the P script) and `TwoFreshRuns_ProduceTheSameReplayableDump` (the pattern of `StateDumpTests.cs:54-55`).
- `PersonalRelationships_StaySeparateFromStanding`: over P1-P5, Sel's trust equals exactly the authored events, and Kera's respect and trust are untouched by P2. Also a source scan: `src/World/Runtime/Factions.cs` never constructs `ChangeRelationship`, and `Social.cs`'s `RelationshipSystem` never dispatches `RecordAct` or `ReportAct`.
- `FactionRelations_NeverMoveStanding` (F11).
- `ACompanionsKill_IsNotThePlayersAct`: Tavar kills the armour; no act is recorded, and `act_done` is false.
- `TheBilletGate_HidesTheWare_AndRefusesARawBuyCommand_UntilAccepted`, and a check that `NpcTests.cs:287-307` still passes unchanged.
- `TheReputationCondition_OpensAndClosesSelsNotes`.
- `ActDone_OffersTheReportOnlyAfterTheAct`.
- `AxisIndependence_ReputationByLevel_2x2` and `AxisIndependence_ReputationBySkill_2x2`, owed at M7 (`docs/PROGRESSION_AXIS_RECONCILIATION.md:246`).
  - Each cell is reached through sanctioned vectors in the fixture setup: high standing at level 1 by a witnessed switch act (no XP, no quest active); high level at neutral standing by killing creatures no faction reacts to.
  - The test asserts that no axis's currency moved another.

**Content.Tests:**
- every FAC001 lint, negative and positive, including `TheLadder_HasNoHostilityTier`, `ACompanionCannotBeAMember`, `AMemberStandsInsideTheSeat`, `AStandingCondition_NamesTheSpeakersOwnFaction` (R1), `ActDone_OnlyBesideAReportOfTheSameAct` (R3), `AGate_BelongsToTheTradersFaction`, `APlacedPieceReaction_IsRefused`, `ARespawningCreatureReaction_IsRefused`;
- `NoContent_TradesCurrencyForStanding` (the E-7 grep test: no `reputation` reward, no `add_reputation` consequence, no trade act kind);
- `FactionContent_RefusesDataModelFieldsItDoesNotBuild`.

**Persistence.Tests:**
- `Schema13To14_GivesAnEmptyLedger`
- `ASchema14PlayerWithoutFactions_IsCorrupt_NotDefaulted` (the pattern of `MigrationTests.cs:318`)
- `TheFactionLedger_GoesThroughTheDefinitionPass_RenamesMergesAndRemovals`
- the v14 fixture loads and migrates to its `expected.json`
- the hard-coded step lists are updated

**Architecture.Tests:** `TacticalCode_NeverReadsFactionState` (section 12).

---

## 18. Documents M7 reconciles (faction side)

| Document | Change |
|---|---|
| `docs/ROADMAP.md` M7 (`:282-285`) | "Crime/bounty records and pardon state": deferred to Phase 3 (scope item 4); the act log is the seam. "Territory gating": deferred. The opposite-directions fixture is `docs/M7_REPUTATION_TABLE.md` |
| `docs/SYSTEMS.md` S-27 (`:302-309`) | Drop "legal/offense state derived from it" and `AddReputation`. Owns the act log, faction knowledge and standing (tier derived); static relations; transient: none. Add rules K1-K10 and the three events |
| `docs/DATA_MODEL.md` §4.13 (`:557-581`) | Replace with the section 16.2 shape; record where each dropped field went (`members` → NPC `faction_ref`; `laws` → future jurisdictions; `enemy_of`/`attitude_default` → `relations` plus future war state; `reputation_tiers` → `config.factions` and stock `requires`; `territory`/`joinable` deferred) |
| `docs/DATA_MODEL.md` §4.12, §4.15 | `reputation` takes tier keys; add `act_done` and `report_act`; `add_reputation` stays unbuilt; stock rows take `requires` |
| `docs/PROGRESSION.md` §10 | The ladder's numbers live in `config.factions`; tiers are derived; decay deferred; "trade volume" means goods supplied, never coin; Anathema unreachable until atonement content exists. Write ruling 3 ("smallest useful set", the separate concepts) here |
| `docs/PROTOTYPE.md:110`, `:264`, `:395` (A-2) | Membership is definition data and is not saved until something can change it |
| `docs/PERSISTENCE.md` §5.1, §6.2 | Schema-14 paragraph; chain rows; T-21 met by knowledge rows with `delta` |
| `docs/INDEX.md` (CRIME row, `:89`) | "M7 reconciled: act records and faction knowledge only; crime is Phase 3" |
| `docs/VERTICAL_SLICE.md:95`, `:169` | Noted as conflicting (a "Hostile" tier; "puts town guards on you"); M9 reconciles them as relation, war state and legal status |

---

## 19. Deferred beyond M7 (non-goals)

- **Crime and law:** crime, legal status, bounty, pardon, jurisdiction, guards, witnesses as testimony, evidence, disguise, container ownership (scope item 4).
- **Knowledge richness:** rumour; knowledge travelling between factions or NPCs; in-flight reports; per-member memory; lying, bribed or mistaken witnesses; the doctrine candidate's "watched" channel; identity rungs beyond two; `confidence`, `claim` and staleness fields.
- **Standing and factions:** war state, faction control, hostility from any faction quantity, decay, caps and diminishing returns, joining or leaving, multi-membership, tag-based reaction rows, `start_points`.
- **Content surfaces:** quest objectives and rewards that touch reputation (M9), territory gating, price modifiers by tier, whole-trade gates, a third faction.
- **Building acts** (`piece_placed`, `piece_destroyed`), which wait for a repeat rule.
- **Companions:** whether a companion reports on the player (loyalty, concealment); party attribution of kills.

---

## 20. Owner question, open issues, contracts

**Owner question (slot 5 of the scope list).** Approve the working pair and the proof act:
- the Waystation (Renn, Kera) and the Survey (Sel), with Tavar belonging to no faction as a hired guide and companion;
- the proof act: destroying the Blackvein Animated Armour, which the Waystation approves (+100) and the Survey disapproves (-100);
- the two gates: Sel's notes at Survey `accepted`, and Kera's billets at Waystation `accepted`.

Default: approved as written. The considered alternative (the steadying of the heart read against an absent "stone-turners" faction) would invent lore and could never learn anything in M7.

**Open issues:**
1. **A companion's kill is not the player's act.** If Tavar lands the killing blow on the armour, no act is recorded and it cannot be reported. Most players meet the armour in Quest 1, before Tavar is free. Party attribution is M9's.
2. **No history before M7.** Migrated saves start with an empty log and neutral standing; deeds done in M6 can be neither reported nor judged.
3. **Billets on migrated saves.** A Kera wares container that a pre-M7 save already traded with never shows the billet row (`Items.cs:488-490`).
4. **Pooling is immediate.** It is honest only because every member is authored inside one seat (FAC-M1) and no companion is a member (FAC-M2). The M7 work-assignment NPC keeps its faction wherever it stands. `Via` and the seat are the seams for delay.
5. **In shipped Ashen Hollow every faction learns by report.** The witnessed channel, identity range and upgrade are fixture-proven (F1-F10). M9's town geometry exercises them in play.
6. **Sight is 2-D and floating point.** Low furniture occludes witnesses exactly as it occludes creatures; Sel's authored facing crosses her own survey table. Replays are same-machine deterministic, as creature perception already is.
7. **Relevance is fixed at boot.** Content added later cannot react to an act recorded before it.
8. **All new dialogue text is a draft** for the owner's tone review. Sel's `notes` line hints at the stone-turners, a lore hook the owner may reject.
9. **The live tier reaches the player only through the HUD line of a report and the debug panel.** A player-facing faction panel, and what the character may know of a faction's view, are M9 presentation questions.

**Cross-system contracts:**
- **Building:**
  - no `RecordAct` in M7; `piece_placed` is reserved;
  - placed walls and closed placed doors join `SystemContext.SightWalls()`;
  - one shared `SchemaV13ToV14` step, v14 fixture and `content-0.1.7` writer pack; building owns the world-delta and effective-cell digest changes, factions own the player field and digest `v10`.
- **Navigation and work assignment:**
  - witnesses and trade read `State.Npcs[..].Body` at the instant of the act or command;
  - a work-assigned member pools what it sees;
  - if the assigned NPC is Kera, her trade reach follows her body while her wares container stays at her authored site (`Systems.cs:61-64`, `Social.cs:549`). That is building's and navigation's to reconcile; factions need nothing.
- **Persistence:** the player-section `factions` field, required from schema 14; enum values as string keys; the definition pass per section 14.5.
- **Presentation:**
  - read-only `Simulation.Factions`, `Simulation.Acts`, `Simulation.Wares`;
  - HUD on `ReputationChanged` with `Source == reported`;
  - reports and purchases use the existing `ChooseCommand` and `BuyCommand`; no new command, no radial.
- **Combat, creatures, companions:** must never read faction state (architecture test).
- **Quests:** no faction objective or reward in M7.
- **Future crime and law:** they consume the act log and knowledge rows, never standing.

---

## Appendix A - the two candidates compared

| Topic | Minimal candidate | Doctrine candidate | This Part C |
|---|---|---|---|
| Systems and slices | one system, one slice | `ActSystem` + `FactionSystem`, two slices, `NoteAct`/`ForgetAct` | minimal. A later slice split is a runtime change with no save change |
| Knowledge storage | rows nested in each act | flat list keyed (knower, act) | **doctrine's flat list**: a future claim (belief with no act) cannot nest in an act, and reshaping later is a save migration |
| Identity field | `bool ActorKnown` | string `identity` with reserved rungs | **doctrine's string key**, same cost, no future migration |
| Knower key | `FactionId` | `Knower` string | **`knower`**: a naming seam at zero cost |
| Actor on the act | implicit (player) | `Actor` EntityId stored | minimal: added by migration when acts move to a world record |
| `ReactionCount`, `limit`, reaction `id` | none | yes | minimal, plus the FAC-R5 lint, which keeps content to single-instance subjects |
| `ChangedTick` on standing | no | yes | minimal: derivable from knowledge-row ticks at the migration that adds decay |
| Channels | witnessed, reported | witnessed (seat, 25 m, no FOV), watched, reported | minimal's two |
| Sight model | `Perception.Sees` (FOV, walls) | integer range + `Blocker.Crosses`, no FOV | minimal: one sight rule shared with creatures |
| Act position | actor's body | creature body / switch centre | minimal: cannot overclaim identity |
| Seat | none | runtime seat check | **doctrine's field, as lint only** (FAC-M1) |
| Tavar | Survey member, travelling witness | unaffiliated | **doctrine**: no companion testimony, no psychic pooling across distance |
| Switch subject | switch site key | flag definition ID | **doctrine**: alias-resolvable, walker-checked |
| Kind names | `killed`, `set_switch`, `placed` | `creature_killed`, `switch_set`, `piece_placed` | doctrine's: `killed` would be ambiguous once NPCs can die |
| Ladder | 25/75/150/300/500, ±1000, no floor | 100/250/450/700/1000, floor -999 | **doctrine's numbers**, with proof deltas ±100 so one act crosses one tier |
| Proof deltas | ±30 | +60 / -120 / +120 | ±100 |
| Reactions | 3 rows | 5 rows including building | 3 rows |
| Service gate | existing hide vest/cap | new billets row, greyed with text | **new billets row**, hidden below the tier (no `WareView` change) |
| Gate data location | faction `service_gates` | merchant stock `requires` | doctrine: the gate sits where the ware is authored |
| Dialogue condition name | `faction_standing` | `reputation` | `reputation` (DATA_MODEL's name) |
| `faction_reputation` objective | deferred | built | deferred (no M7 content user) |
| Building act | dispatched, no content row | dispatched, one content row | **not dispatched**; reserved (section 13) |
| Relations | int attitude | attitude words | attitude words |
| HUD | every change | reported changes only | reported only |
| Third faction | rejected | rejected, fixture only | rejected, fixture row F12 |

## Appendix B - false or imprecise claims found in the candidates

Each was checked in the snapshot.

**Minimal candidate:**
1. **The service gate silently regresses Phase 1.** Gating `item.armor.hide_vest` and `hide_cap` "needs no change" in presentation, but it breaks `tests/Application.Tests/NpcTests.cs:296-307`. That test boots shipped content and expects the vest listed and buyable (refused only for coin) at neutral. The design does not disclose it. It also puts the C18 reachability cap behind a level-5 sentinel fight.
2. **"Sequence numbers keep `StateDump(replayable)` equal ... A ULID ... would not" is misstated.** `StateDump.Render(replayable: true)` renames IDs by order of first appearance (`src/Application/StateDump.cs:21-24`, `:35-43`), so ULIDs would still compare equal there. What a wall-clock ULID actually breaks is `StateDigest` equality under command-log replay (`DeterminismAndViewTests.cs:48-77`). The conclusion (use `Seq`) stands; the reason does not.
3. **"Tavar sees it (he faces the heart from 10 m)" overstates.** The geometry is right: facing 53°, bearing 53.1°, 10 m. But the design's own act position is the player's body, and `rock_foldscar_heart` (circle r 1.5 m, `ashen_hollow.yaml:108`) is a static blocker whose `Crosses` ignores height (`Blockers.cs:117-124`). A player steadying the heart from its far side is hidden from Tavar.
4. **"Tavar, following at 2.5 m, always identifies" is unproven.** 2.5 m is where he stops following (`content/config/companion.yaml`). In a fight he engages within 10 m of himself and 16 m of the character, and faces his target, so a 140° cone need not contain the player.
5. **Internal inconsistency.** Section 18 defers "whether a companion reports on the player", while sections 5 and 7 make Tavar, a companion and Survey member, an automatic reporter through instant faction pooling. That pooling also lets Sel "know" at the tick what Tavar saw 100 m away, the same-faction-telepathy STEALTH §6 forbids (`docs/STEALTH_DETECTION_AND_THREAT.md:100`).
6. **The Anathema tier is reachable by ordinary accumulation** (≤ -500 with no floor). That contradicts PROGRESSION §10, "`Anathema` requires explicit acts" (`docs/PROGRESSION.md:454`).
7. **Citation drift.** The `RecordAct` hook is given as `Creatures.cs:716-717`; the `if (killer == _player)` / `RecordDeed` lines are `:705-706`.
8. **"No Waystation member can see the build area: both stand inside walls" assumes closed doors.** The lodge door is on the east wall that Renn faces (`ashen_hollow.yaml:67-72`, `:143`, `:152`). The build area's position is not yet designed, so the claim is unverifiable.

**Doctrine candidate:**
1. **The billets gate omits the migration effect.** A Kera wares container touched by a pre-M7 trade is a persisted `ContainerRecord` and never re-reads stock (`src/World/Runtime/Items.cs:488-490`), so those saves never see the gated row.
2. **The "watched" channel contradicts the sight code.** Sel "perceives any act within [the Foldscar's] discovery radius" 110 m away, with no line check. The design says there is "no long-range sight model", yet this is one. Under the shared sight rule, Sel's authored facing (135°) passes through her own survey table about 2 m out (`ashen_hollow.yaml:83`, `:153`; 2-D `BoxBlocker.Crosses`, `Blockers.cs:77-90`), so she sees nothing toward the Foldscar.
3. **"25 m is the upper band of creature sight (grey wolf 25 m)" is false.** The ash ember hound sees 30 m (`content/creatures/beast/ash_ember_hound.yaml:17`).
4. **"Fill `FactionSchema`" is a no-op.** `SchemaTypeMapper.GetSchemaType` has no caller in `src` (`src/Content/SchemaTypeMapper.cs:19`), so filling the record changes nothing.
5. **Placing the act at the creature's body can overclaim identity.** A witness who sees the sentinel fall but not the archer 25 m away is recorded as having identified the player. Seeing the effect is evidence, not identification (CRIME §4: "Witnesses perceive events").
6. **The proof act does not change access.** The Waystation's +60 stays Neutral (below 100), so the service gate needs a second, unrelated act (the Foldscar +60).
7. **Building `faction_reputation` smuggles M9 surface** with no M7 content user; the candidate admits this in its open issue 11.
8. **The seat is a runtime check with a 1.4 m margin.** Sel stands 28.6 m from the outpost anchor against a 30 m radius. Any re-placement silently removes the Survey's only member from its seat, and with it every channel. This Part C keeps the seat as lint only (FAC-M1), which fails loudly instead.

Both candidates' other code claims checked out: the `RecordDeed` pattern, `Dispatch` routing, the switch hook, the `Items.cs:456-459` wares guard, the `TradeSystem` hooks, `SectionCodec` and `Migrations` patterns, `PlayerRecord` `With*` and the digest, the `LocationSite` radius, NPC distances from the outpost anchor, and the architecture allow-list.

## Appendix C - doctrine-candidate items deliberately deferred

| Item | Why deferred | Retrofit cost |
|---|---|---|
| Separate `ActSystem` owning the truth log | crime and evidence, its other consumers, are Phase 3 | runtime only: move the slice owner |
| `watched` channel, `watch_location_refs` | an unoccluded long-range perception; not needed for the proof | content field + closed-set key; no save change |
| Runtime seat check | M7 members never leave the seat; lint FAC-M1 guards authoring | one check in `BestWitness`; no save change |
| `ReactionCount`, `limit`, reaction `id` | M7 content is single-instance (FAC-R5) | a saved counter; derivable from knowledge rows while the log has not evicted, which with ≤ 2 acts it cannot |
| `ChangedTick` on standing | decay deferred | migration takes the latest knowledge-row tick |
| `Actor`, `Target`, `Site` on acts | M7 has one actor; no rule reads target or site | fields filled by migration when the log moves to a world record |
| `start_points` | every M7 faction starts at 0 | content field, default 0 |
| `faction_reputation` quest objective | no content user; a global read | move from `NotBuilt` to `Built` in M9 |
| Building reaction `raised_a_workplace` | turns materials into standing (E-7) without a repeat rule | enable `piece_placed` plus a repeat rule |
| Greyed wares with `WareView.Withheld` text | Kera's line announces the billets; a hidden ware needs no presentation change | a positional `WareView` field + panel change |
| Reserved identity rungs, sources, `confidence`, `claim` | named in the doctrine; not stored in M7 | string-key sets extend without a migration |
