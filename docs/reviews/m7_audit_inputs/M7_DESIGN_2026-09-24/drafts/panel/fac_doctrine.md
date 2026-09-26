# M7 Factions and Reputation v1 - candidate design (lens: doctrine fidelity)

Status: candidate for the M7 design panel, 2026-09-24. Design and implementation planning only. Base: `origin/main` `e10d2c4`; every code citation is repo-relative `path:line` in the read-only snapshot named by `drafts/00_SCOPE_RULINGS.md`. This candidate designs inside the scope rulings. It raises no challenge to them (see §19).

Evidence: research notes `faction_docs`, `world_lore`, `social_quests_code` (read in full), plus the relevant parts of `sim_core`, `persistence`, `content_registry` and `authority`. Every code fact below was re-checked in the snapshot.

---

## 0. The recommendation on one page

1. **Three records, nothing more.** An **act log** (what happened), **faction knowledge** (what each faction believes happened, from whom, and whether it knows who did it), and **standing** (integer points per faction; tier derived from the PROGRESSION §10 ladder, never stored). Everything else is static content: faction definitions (seat, reactions, relations), membership on the NPC definition, one ladder config.
2. **Standing moves in one way only:** when a faction *knows an act* and *knows who did it*. No `add_reputation`, no `reputation` quest reward, no direct write. That rule prevents both psychic factions and "quest X gives +10 faction A".
3. **Three knowledge channels, no others.** **Witnessed** (a member at the faction's seat, within 25 m, clear line: actor identified); **watched** (a member keeps watch over a named place: act known, actor **unidentified**); **reported** (the player tells a member: identified). No gossip, rumor, courier, companion testimony or faction-to-faction sharing.
4. **Two working factions.** The **Waystation** (Renn Vale, Kera Voss) and **the Survey** (Sel Arien). Tavar Orr is unaffiliated, a hired Orenth guide.
5. **The proof act.** Destroying the Animated Armour that guards the Blackvein iron seam: Waystation +60 ("the seam is safe to work"), Survey -120 ("an old working destroyed before anyone recorded who set it or why"). It is a genuine choice: the bible requires ore without that fight (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:219`).
6. **Gates.** Dialogue: one new Sel reply while Survey ≥ `accepted`. Service: Kera's iron billets while Waystation ≥ `accepted`, enforced in `TradeSystem.Handle(BuyCommand)`, so no command bypasses it.
7. **No faction hostility in M7.** No tier, relation or reaction feeds combat, creature minds, companions or attack legality; an architecture test makes that a build failure.
8. **Persistence.** Schema 14 (shared with building): two required player-section records, `acts` and `factions`, empty for migrated saves.

---

## 1. Doctrine, mapped onto M7

### 1.1 The five layers (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:8`, `:10`, `:248`)

"What happened, what was perceived, what people believe happened, what can be proven, and what the law says about it are different things." In M7 each layer has either a record of its own or a named, reserved place. None of them shares a record with another.

| Layer | M7 representation | Status in M7 |
|---|---|---|
| What happened | `ActRecord` in a new `StateSlice.Acts`, owned by a new `ActSystem`. The simulation records the true actor, subject, place and tick (doctrine `:14`) | **Built** |
| What was perceived | The channel through which a faction learned the act (`witnessed` / `watched` / `reported`) and the member it came through (`via`), stored on the knowledge record | **Built in minimal form.** Per-NPC perception memory is reserved |
| What is believed | `FactionKnowledge(knower, act, identity, ...)` in `StateSlice.Factions`, owned by `FactionSystem`. It references the act; it does not copy it | **Built** at faction level. Per-NPC belief and false belief ("claims") are reserved |
| What is proven | nothing | **Reserved:** `EvidenceRecord` with provenance and confidence (doctrine `:83`) |
| What law applies | nothing | **Reserved:** jurisdiction and legal-status records. They belong to places and institutions (`:20`), never to standing and never to the faction definition |

In M7, "perceived" and "believed" coincide by construction. M7's channels cannot be wrong: there is no darkness, disguise, distance error, lie or bribe. The records still keep the two apart:
- the act log is truth, and knowledge only references it;
- a future false belief becomes a knowledge record whose proposition is a *claim* rather than an act (reserved, §2.4).

"The justice system does not know the truth. The simulation does" (`:248`) is therefore true of M7's factions. The act log is the simulation's; a faction knows only what reached it.

### 1.2 The eleven concepts (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:548-564`; owner ruling 3)

| Concept | Where it lives after M7 | Status |
|---|---|---|
| Personal relationship | `RelationshipSystem`, `StateSlice.Relationships` (`src/World/Runtime/Social.cs:177-198`) | Existing (M4). Untouched |
| Trust / fear / grudge | The same system's dimensions, integers in [-100, 100] (`src/Domain/Social/Social.cs:30-40`) | Existing. Untouched |
| Reputation / standing | `FactionStanding.Points`, tier derived | **Built** |
| Legal status | nothing | **Reserved**: Phase 3 (`docs/PROTOTYPE.md:40`), scope default 4 |
| Faction relation | static `relations:` on the faction definition | **Built as data.** No rule reads it |
| War state | nothing | **Reserved**: runtime per-pair state in a future world-global save record |
| Known identity | `FactionKnowledge.Identity` (`unidentified` / `identified`) | **Built, minimal.** The rest of the identity ladder (`:95`) is reserved |
| Tactical threat | the creature mind machine (`src/World/Runtime/Creatures.cs:36`) | Existing. It must never read standing (test, §12) |
| Attack legality | nothing | **Reserved**: the `CanAttemptAttack(attacker, target, context)` seam (`docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:825`) |
| Settlement standing (the MODDING §43 list) | nothing | **Reserved.** The Waystation faction is a community, not a settlement polity |
| Fame (§28) | nothing | **Reserved.** Fame is how many know you; M7's knowledge records are per act, not a fame count |

---

## 2. Q1 - the minimum entities

The brief asks for eight candidate entities. The answer for each:

| Candidate | M7? | Form |
|---|---|---|
| FactionDefinition | Yes | Content (`kind: faction`, already registered: `src/Content/SchemaResolution.cs:158-164`). Fields: `name`, `seat_location_ref`, `start_points` (0 in M7), `reactions`, `relations` |
| FactionRelationship | Yes, as static data | `relations:` on the definition: target faction and an attitude word. No runtime state |
| ReputationRecord | Yes | `FactionStanding(FactionId, Points, ChangedTick)` |
| KnownAct / reported event | Yes, split in two | `ActRecord` (truth) and `FactionKnowledge` (belief and provenance) |
| Value / tag preferences | Yes, and only as reaction rows | A faction's values are its reaction table. There is no separate `values:` vocabulary |
| Membership | Yes, static | `faction_ref` on the NPC definition. The faction lists no members; the member index is derived at content build |
| Hostility thresholds | **No** | Nothing maps a tier to hostility. There is no field to hold such a mapping |
| (added) Reaction application count | Yes | `ReactionCount(FactionId, ReactionId, Count)`, so that `limit` survives act eviction (§3.6) |

### 2.1 `ActRecord` (World, saved with the player)

```csharp
// src/World/PlayerState.cs, beside RelationshipValue (:38)
public sealed record ActRecord(long Seq, long Tick, string Kind, string Subject, EntityId Actor, long XMm, long ZMm)
{
    public string? Site { get; init; }       // stable site key: spawn key, switch key, build-area key
    public EntityId? Target { get; init; }   // the instance acted on: creature, placed piece
}
public sealed record ActLog(long NextSeq, ImmutableArray<ActRecord> Records);   // Records ascending by Seq
```

### 2.2 `FactionKnowledge`, `FactionStanding`, `ReactionCount`

```csharp
public sealed record FactionKnowledge(string Knower, long Act, long LearnedTick, string Source, string? Via, string Identity)
{
    public long? SettledTick { get; init; }   // when the faction judged the act (it knew the actor); null until then
    public string? ReactionId { get; init; }  // the row that judged it
    public int Delta { get; init; }           // what standing actually moved (after clamp and limit)
}
public sealed record FactionStanding(string FactionId, int Points, long ChangedTick);
public sealed record ReactionCount(string FactionId, string ReactionId, int Count);
public sealed record FactionLedger(ImmutableArray<FactionStanding> Standings, ImmutableArray<FactionKnowledge> Knowledge,
    ImmutableArray<ReactionCount> Applied);   // each canonically sorted (ordinal; Knowledge by (Knower, Act))
```

The knowledge record, with `ReactionId` and `Delta`, *is* the "bounded event log" that PERSISTENCE T-21 requires ("Reputation values plus their bounded event log persist", `docs/PERSISTENCE.md:595`). Every point of standing is attributable to one act, one channel and one reaction row.

### 2.3 Which fields are used now, which are seams, which are reserved

This answers the lens's "name exactly which fields exist now as seams and which are reserved".

- **Stored and read by M7 rules:** `ActRecord.Seq, Tick, Kind, Subject, XMm, ZMm`; `ActLog.NextSeq`; `FactionKnowledge.Knower, Act, Identity, SettledTick`; `FactionStanding.FactionId, Points`; `ReactionCount.*`.
- **Stored now as seams** (filled; read only by views and tests):
  - `ActRecord.Actor` (always the player in M7; the seam for NPC acts and NPC crime, doctrine `:240`), `.Target` ("who was harmed / what changed", `:14`), `.Site` (which authored thing, for later site-specific reactions);
  - `FactionKnowledge.Source, Via, LearnedTick` (provenance, `:83`) and `.ReactionId, Delta` (the attribution log);
  - `FactionStanding.ChangedTick` (lets PROGRESSION §10's weekly decay become a pure function later, with no value migration);
  - `Knower` as a string (M7 writes only faction IDs).
- **Reserved** (named, not stored; default in brackets):
  - `FactionKnowledge.Confidence` [1000 = certain; every M7 channel is certain]; `.Claim`, a proposition that references no act (lies, false reports, rumor, framing, `:85-89`), as an alternative to `Act` [none]; `.StaleAfterTick` [none];
  - identity values `described`, `named`, `confirmed` (the ladder at `:95`); sources `told_by_npc`, `rumor`, `courier`, `evidence`, `scripted`; knower grammar `npc.<id>` (per-NPC memory) and `faction.<id>@<location_id>` (a remote branch);
  - on acts: `Items` ("what property changed hands", `:14`) and the self-defense facts (`:142`);
  - on factions: war state, control/territory, membership changes, `anathema: allowed` rows, the decay rate.

### 2.4 Compatibility with the future Knowledge/Belief record

`SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:749-765` says to avoid ad-hoc shapes that are incompatible with the generic record. M7's `FactionKnowledge` maps field for field onto that record:

| Generic field | M7 equivalent |
|---|---|
| proposition | `act:<Seq>` (later also `claim:<id>`) |
| source | `Source` + `Via` |
| confidence | reserved, implicitly 1000 |
| timestamp | `LearnedTick` |
| subject | the actor, gated by `Identity` |
| location | the act's `XMm`/`ZMm` |
| staleness | reserved |

When the generic record arrives, it is a migration of this record, not a replacement.

---

## 3. Q2 - the act: from an authoritative gameplay act to a faction input

### 3.1 The pattern

M7 reuses the live fan-in pattern. An acting system dispatches one synchronous internal command to an owner, exactly as `RecordDeed` does today:
- `src/World/Runtime/Quests.cs:62` declares `RecordDeed`;
- `src/World/Runtime/Creatures.cs:704-705` dispatches it when `killer == _player`;
- `src/World/Runtime/Social.cs:355` dispatches it for a delivery.

No system subscribes to events; events stay presentation-only (`src/World/Runtime/Events.cs:10-11`). The new internal command:

```csharp
// src/World/Runtime/Factions.cs
internal sealed record RecordAct(ActDraft Draft) : InternalCommand;                 // to ActSystem
public sealed record ActDraft(string Kind, string Subject, long XMm, long ZMm) { public string? Site { get; init; } public EntityId? Target { get; init; } }
```

It is not the `Deed` type. `Deed` is a quest counter with no position and quest-only kinds (`src/Domain/Quests/Quests.cs:297-300`); M5 stays untouched.

### 3.2 The closed act-kind set for M7 and its dispatch sites

| Kind | Subject (content kind) | Site | Target | Dispatched from |
|---|---|---|---|---|
| `creature_killed` | creature definition ID | spawn key (`c.Site.Key`) | creature instance ID (derived, deterministic) | `CreatureSystem.Die`, inside the existing `if (killer == _player)` block, right after `RecordDeed` (`src/World/Runtime/Creatures.cs:700-706`). The position is the creature's body |
| `switch_set` | the `world.*` flag the switch sets (a definition ID) | switch key | none | `InteractionSystem.Work`, after `SetWorldFlag` is accepted and before `SwitchSet` is published (`src/World/Runtime/Systems.cs:276-293`). The position is `Footprints.Center(site.Body)` |
| `piece_placed` | the placed piece's definition ID (building design's kind) | the build-area key | the piece instance ID | the building system, after a placement fully commits (contract, §18) |

- **Reserved kinds:** `piece_dismantled`, `piece_destroyed`, `item_taken` (needs ownership), `npc_harmed` (needs NPC combat), `said` (a spoken public act; the only honest home for a future `add_reputation`), and `supplied_goods` (the only honest home for PROGRESSION's "supplying goods" vector).
- **Not act kinds, deliberately:**
  - `bought` / `sold`. Coin never buys standing (E-7, `docs/GAMEPLAY_LOOPS.md:238`). "Trade volume with faction merchants" (`docs/PROGRESSION.md:438`) is reconciled to "goods supplied", which is a deed, not a price.
  - quest completion. A quest is the player's bookkeeping, not a world event. Factions react to the world act the quest required: steadying the heart, not "completed Quest 2". This is also what makes "a stone turned before the quest counts" work for factions.
  - a companion's kill. It is not the player's act, consistent with M6: "a companion's kill does neither" (`src/World/Runtime/Creatures.cs:696-699`). "Companion association" is Expansion (`docs/FEATURE_DELIVERY_STAGING.md:101`).

### 3.3 Notability: which acts are recorded at all

`ActSystem` records a draft only if some faction would still react to it:

```
Notable(draft) = exists F in Factions (ordinal): row = FirstMatch(F, draft.Kind, draft.Subject) is not null
                                                 and Applied[F, row.Id] < row.Limit
```

- A wolf kill is not recorded in M7, because no faction reacts to wolves.
- A second crafting station is not recorded once the Waystation's once-only row has applied.

The act log therefore holds only history that someone could still judge. This is a pure read of the `Factions` slice; cross-slice reads are allowed (`sim_core` §3.3). A draft that is not notable is not an error: `RecordAct` returns null.

### 3.4 Deterministic identity and order

- Acts are **not registry entities** and get no ULID: `EntityId.NewId` uses the wall clock, which would break replay digests (`src/Domain/EntityId.cs:48-53`). An act's identity is `Seq`, a per-world `long` from 1, taken from the persisted `ActLog.NextSeq`.
- `Seq` is assigned in dispatch order, which is fully determined: GameCommands drain FIFO at a boundary (`src/World/Runtime/Simulation.cs:268-306`), `Step` runs a fixed system order (`:312-339`), internal commands are synchronous (`:369-404`), and `Tick` is `Now` (`:366-367`).
- Two acts in one tick are ordered by `Seq` alone. Nothing depends on event subscription or dictionary enumeration: factions iterate in ordinal ID order (`ImmutableSortedDictionary`), members in ordinal NPC-ID order, reaction rows in authored order, reported acts in ascending `Seq`.

### 3.5 Where the act log lives, and who owns it

`ActSystem` owns `StateSlice.Acts`, saved with the player at schema 14. It holds the log, assigns sequence numbers, evicts old acts, and then dispatches `NoteAct(seq)` to `FactionSystem`. Two systems, because the doctrine's first layer (truth) has other future consumers besides factions: crime, evidence, per-NPC memory. It must not be owned by the thing that forms beliefs about it.

### 3.6 Retirement (PERSISTENCE I-7)

The log is capped at `act_log_cap: 256` (config). When a notable act arrives and the log is full, `ActSystem` evicts one act:

```
settled(a) = for every faction F with FirstMatch(F, a) not null: Knowledge[F, a.Seq] exists and SettledTick is not null
evict = lowest Seq among settled acts, else lowest Seq overall
```

It then dispatches `ForgetAct(seq)` so that `FactionSystem` removes the knowledge records that reference the evicted act. `ReactionCount` survives eviction, so `limit` cannot be reset by flooding the log. Standing never changes on eviction. With M7 content the log cannot plausibly exceed about 3 acts; the cap is a backstop that is proven by a test.

---

## 4. Q3 - knowledge: when a faction knows an act occurred

### 4.1 The rule

**A faction knows an act only through one of its members, and only while that member is at the faction's seat.** The seat is a content location (`seat_location_ref`), and "at the seat" means that the member's authoritative body is within that location's discovery radius (`LocationSite.DiscoveryRadiusMm`, `src/Domain/Spatial/RegionLayout.cs:31`).

The faction's knowledge is the pool held at its seat; members at the seat share it instantly. That is M7's single, stated simplification of "same faction does not imply instant shared awareness" (`docs/STEALTH_DETECTION_AND_THREAT.md:100`). It stays honest because the lint requires every static member's placement to lie inside the seat, a member away from the seat (a companion, or an NPC walked to a work anchor) contributes nothing, and a faction with no member at its seat can learn nothing, ever.

Checked against the content (`content/regions/ashen_hollow.yaml:151-155`; outpost anchor (55, 145), radius 30 m, `content/locations/outpost.yaml`): Renn at (46.5, 126.2) is 20.6 m from the anchor, Kera at (61.6, 139.6) 8.5 m, Sel at (72, 122) 28.6 m. All three are inside, and both M7 factions are seated at `location.outpost`.

### 4.2 The three channels

**Witnessed.** Evaluated by `FactionSystem.Handle(NoteAct)` at the tick of the act. For each faction F with a matching row, a member M witnesses when all of these hold:
- M is present in `State.Npcs` and is not a companion;
- M is at F's seat;
- `dx^2 + dz^2 <= witness_range^2`, in integer millimetres (`witness_range_m: 25`);
- no blocker crosses the segment from M's body to the act position. The blockers are the same set creature sight uses (`Layout.Space.Blockers` plus `ClosedDoors()`, `src/World/Runtime/Creatures.cs:893`) plus placed blocking pieces (contract, §18).

`Via` is the nearest witness (ties by ordinal ID). Identity is `identified`: the one stranger at a four-person waystation, seen within 25 m, is known. No field of view in M7 (NPC `Senses` reserved). `Perception.Sees` is deliberately not used because it adds `Math.Sin`/`Math.Cos` (`src/Domain/Creatures/Perception.cs:87-107`); `Blocker.Crosses` works in doubles exactly as the existing combat and creature line checks do (`src/World/Runtime/Combat.cs:570`). 25 m is the upper band of creature sight (grey wolf 25 m, `docs/M3D_BEHAVIOUR_MATRIX.md`), and smaller than the 30 m seat, so one member does not see the whole waystation.

**Watched.** If no member witnessed the act, a member at the seat whose NPC definition lists a location in `watch_location_refs` perceives any act within that location's discovery radius, **unidentified**. This models exactly a perception the shipped content already asserts: "Sel keeps her books at the survey table east of here, where she can watch the Foldscar" (`content/dialogue/ashen_hollow/renn_vale.yaml:47`) and "I saw the ring go quiet from the rise" (`content/dialogue/ashen_hollow/sel_arien.yaml:108`). She sees the effect about 110 m away, not the person, with no long-range sight model and no omniscience.

**Reported.** The dialogue consequence `report_act` dispatches `ReportActs(listenerNpcId, pattern)`. The listener's faction learns every act in the log matching the pattern that it does not already know as identified, in ascending `Seq`, with source `reported`, `Via` the listener, identity `identified` (the actor names themselves). The listener must be at the seat, and the lint requires their faction to have a matching row. A report is honest by construction: its reply is offered only when the act is in the log (`did_act`, §4.5). Lying is reserved (a claim, §2.3).

**Not built in M7:** NPC-to-NPC or cross-faction telling; rumor; couriers and in-flight reports (they would need persisted travel state, the C-13 hazard in `faction_docs`); companion testimony; evidence discovery; scripted transfer (the reserved `inform_faction` must name the member at the seat through whom knowledge enters).

### 4.3 Identity, and late attribution

A faction judges an act only when `Identity == identified`. Knowing that something happened does not move standing; knowing who did it does. When a later channel identifies the actor of an act the faction already knows as unidentified, the record is upgraded. `Source` and `Via` keep the first channel. The reaction is judged then, once. "Criminal association raises suspicion rather than automatically proving guilt" (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:89`) holds in miniature: M7 never infers the actor.

### 4.4 The learning algorithm (`FactionSystem`)

```
Learn(F, act, source, via, identity, now):
  row = FirstMatch(F, act)                              // authored order; null => F does not care => store nothing
  if row is null: return
  k = Knowledge[F, act.Seq] ?? new FactionKnowledge(F, act.Seq, now, source, via, identity)
  if Rank(identity) > Rank(k.Identity): k = k with { Identity = identity }
  if k.Identity == identified and k.SettledTick is null:
      delta = 0
      if Applied[F, row.Id] < row.Limit:
          from = PointsOf(F); to = FactionRules.Apply(Ladder, from, row.Delta); delta = to - from
          Applied[F, row.Id] += 1
          if to != from: Standing[F] = (to, now); publish ReputationChanged(F, from, to, act.Seq, row.Id, now)
          if TierOf(from) != TierOf(to): publish StandingTierChanged(F, TierOf(from).Key, TierOf(to).Key, now)
      k = k with { SettledTick = now, ReactionId = row.Id, Delta = delta }
  Knowledge[F, act.Seq] = k; publish FactionLearned(F, act.Seq, source, via, k.Identity, now)
```

`NoteAct` calls `Learn` for each faction, ordinal, through the first channel that applies (witnessed, else watched). `ReportActs` calls it for the listener's faction. Everything runs synchronously inside the dispatch chain of the act or of the `ChooseCommand`.

### 4.5 The M7 rule for faction reads, and the Phase-1 leaks

Today dialogue conditions read global player state. Examples:
- `visited` of another NPC's conversation;
- any `quest_state`;
- any NPC's `relationship` (`src/Domain/Social/Social.cs:65-95`);
- flags read in the speaker's cell (`src/World/Runtime/Social.cs:400-402`).

M7 does not fix Phase 1. It adds one rule for every **faction read**: **an NPC may only act on its own faction's view.** Enforced by lint:

1. A dialogue `reputation` condition may name only a faction of which every participant of the conversation is a member.
2. `report_act` informs only the speaker's own faction.
3. A merchant stock `requires` row may name only a faction of which the trading NPC is a member.
4. `did_act` reads the *player's own* history: it offers the player a line about something the player did. It never implies that the NPC knows the act. Only `report_act` makes the NPC's faction know.
5. The quest objective `faction_reputation` reads truth, as every M5 predicate does (quests are the player's bookkeeping, D-07). Stated as accepted.

The existing leaks (cross-dialogue `visited`, `quest_state`, and `relationship` on any NPC) are listed for the knowledge milestone. M7 content uses none of them for faction purposes.

---

## 5. Standing: the ladder, the numbers, and repeat handling

### 5.1 The ladder

PROGRESSION §10 governs (`docs/PROGRESSION.md:432-434`). The ladder is config (`content/config/reputation.yaml`, `config.reputation`). The lint requires exactly these 11 keys, in this order, with no renames. That guarantees there is never a "hostile" tier (`docs/PROGRESSION_AXIS_RECONCILIATION.md:185`).

| Tier | Level | Points (inclusive) |
|---|---|---|
| exalted | +5 | 1000 |
| allied | +4 | 700 .. 999 |
| honoured | +3 | 450 .. 699 |
| trusted | +2 | 250 .. 449 |
| accepted | +1 | 100 .. 249 |
| neutral | 0 | -99 .. 99 |
| wary | -1 | -249 .. -100 |
| disliked | -2 | -449 .. -250 |
| despised | -3 | -699 .. -450 |
| outcast | -4 | -999 .. -700 |
| anathema | -5 | -1000 |

- **Range.** Points are held in [-1000, 1000]. Ordinary reactions clamp at **-999**: "Anathema requires explicit acts and is escapable via atonement content" (`docs/PROGRESSION.md:454`). No atonement content exists yet, so M7 content cannot put a player there. The row flag `anathema: allowed` is reserved and refused by the lint in M7.
- **Why these numbers.** Widening tiers (100, 150, 200, 250, 300) give "per-faction numeric accumulation inside a tier". The first positive tier needs about two moderate known acts. Upper tiers need sustained history: at VERTICAL_SLICE's pacing, "≥ 40 reputation" per 30 minutes (`docs/VERTICAL_SLICE.md:265`), Trusted takes roughly three hours of such play.
- **The tier is derived and never stored.** This follows ARCHITECTURE's "Derived from standings; not separately stored" (`docs/ARCHITECTURE.md:208`). A threshold change in content reclassifies a player (`docs/DATA_MODEL.md:846`). That is accepted, because tiers gate access only.
- **Default standing.** The default is the faction's `start_points` (0 for both M7 factions). A standing record is stored once it has first changed.

### 5.2 Repeat handling

- **The same act, learned again** (witnessed then reported, or reported twice) is a no-op: one knowledge record per (faction, act).
- **Distinct acts of the same kind** apply until the matching row's `limit`, from 1 to 100, is reached. The lint refuses unlimited rows in M7, which bounds farming.
- A row that has reached its limit still settles the act (Delta 0), so the act counts as "judged".
- **First match wins.** For each faction, only the first row in authored order that matches an act judges it. Authors put the more specific rows first; the lint warns when a row is unreachable.
- **Decay** is deferred (scope §4). `ChangedTick` makes it a pure function later.

---

## 6. Q5 - the working factions

Names are working names. Neither is canon: "Names are working names until cultural naming is finalized" (bible `:265`). Both IDs are stable and aliasable.

### 6.1 The Waystation - `faction.ashen_hollow.waystation`

- **Purpose / members.** The small community that keeps the road stop running (shelter, iron, trade, a quiet road): Renn Vale (steward), Kera Voss (smith).
- **Values.** Useful work and supply (Kera: "Iron doesn't care how you got to it", `content/dialogue/ashen_hollow/kera_voss.yaml:75`), a safe road, a stop that grows. It echoes Veth "keeping" (`docs/MYTHOLOGY.md:97-98`) but is a community, not a people.
- **Why here.** It *is* Ashen Hollow: "I keep this waystation, such as it is." (`renn_vale.yaml:11`)
- **Acts it cares about.** Blackvein seam made safe +60; Foldscar made quiet +60; a workplace raised at the stop +40 (each limit 1). Cordial toward the Survey.
- **Proves.** The reported channel, the building-produced act, the service gate, and that an approving faction's members can be personally unmoved (Renn's `respect` is untouched).

### 6.2 The Survey - `faction.ashen_hollow.survey`

- **Purpose / members.** Sel Arien's survey post, recording the ruins around Ashen Hollow for an institution the fiction deliberately leaves unnamed (Siann "accumulate in archives, courts, universities and religious orders", `docs/MYTHOLOGY.md:182-184`).
- **Values.** The Record, "an obsession with accurate testimony" (`MYTHOLOGY.md:165-167`); old things understood before they are used up ("half of what's on it is older than the waystation", `sel_arien.yaml:11`); the Quiet Stones set right.
- **Why here.** Sel's table faces the Foldscar (`content/regions/ashen_hollow.yaml:153`, facing 135°).
- **Acts it cares about.** The stones restored +120; an old working destroyed unrecorded -120 (each limit 1). Cordial toward the Waystation.
- **Proves.** The watched channel, unknown-actor knowledge and late attribution, the dialogue gate, and the split between Sel's personal trust and the Survey's institutional view.

### 6.3 Tavar Orr: unaffiliated

Tavar is a hired guide. "Veth hire Orenth"; Orenth law is "contractual and portable" (`docs/MYTHOLOGY.md:215-219`, `:421-430`). His companion status would make him a travelling witness, and companion testimony is Expansion. The lint therefore refuses `faction_ref` on any NPC with a `companion:` block in M7.

### 6.4 No third faction in shipped content

A third faction would prove one thing the pair cannot: a faction that would react but has no member present, and so never learns. That invariant is proven in the **test content pack** with `faction.fixture.absent` (zero members). No lore is shipped for it. The world's strongest opposed seed, "whoever turned the Quiet Stones out of line" (`sel_arien.yaml:93`; `tavar_orr.yaml:43`), is offered to the owner as the alternative proof (§20), not built.

---

## 7. Q4 - the proof case: one act, two readings

**The act.** The player destroys the Animated Armour (`creature.construct.animated_armour`, spawn `spawn.hollow.iron_shelf_armour`, respawn `none`). It "stands guard over the iron seam" in Blackvein Cut (`content/spawns/hollow/iron_shelf_armour.yaml`). Its maker is unknown in the fiction (`world_lore` §4.1 G7).

**Why the two readings make sense in Otherreach.**
- To the Waystation it is a guard between the stop and its iron. Once it is scrap, the seam can be worked.
- To the Survey it was the only testimony about who worked Blackvein and why. Destroying it before it was recorded is exactly the loss the Record exists to prevent.

It is the small-scale shape of the Stone Question, where one act is "a war crime to one people and a necessity to another" (`docs/WORLD_MATERIALS.md:81`). No one is the villain.

**Why it is a real choice.** The sentinel holds its post (role `sentinel`, `Hold`), and the bible requires ore without forcing every fight (`:219`). A player can sneak past it, smash it, then tell or not tell.

**The flow:**

```
AttackCommand (boundaries) -> CombatSystem.Tick -> WoundCreature -> CreatureSystem.Die(armour, killer = player)   (tick T)
  -> RecordDeed(Killed ...)                                   [existing, quests]
  -> RecordAct(creature_killed, creature.construct.animated_armour, site spawn.hollow.iron_shelf_armour, target cre_..., (65000, 34000))
       ActSystem: notable (waystation.seam_made_safe 0/1)  -> Seq 1 recorded, ActRecorded published
       -> NoteAct(1) -> FactionSystem: survey: Sel at seat, 90 m away, no watch covers (65,34) -> nothing
                                       waystation: Renn, Kera 100+ m away -> nothing
TalkCommand(renn) .. ChooseCommand(armour_news)  [did_act holds]  -> ReportActs(renn, creature_killed/armour)
       -> waystation learns 1 (reported, via renn, identified) -> seam_made_safe: 0 -> +60 (neutral), ReputationChanged
TalkCommand(sel) .. ChooseCommand(armour_confess) -> ReportActs(sel, ...)
       -> survey learns 1 (reported, via sel, identified) -> old_working_destroyed: 0 -> -120 (wary), ReputationChanged, StandingTierChanged
```

The same act (Seq 1) moves the two factions in opposite directions. That is ROADMAP M7's exit criterion (`docs/ROADMAP.md:284`). Tell only Renn, and the Survey never moves. That is the no-psychic-factions criterion, and it arises in ordinary play.

**The companion case: steadying the Foldscar.**
1. The heart switch sets `world.foldscar.steadied` at (153, 48), which is inside `location.foldscar`'s 30 m radius. Sel watches that place.
2. The Survey learns the act **unidentified**, and its standing does not move.
3. The player later says "Tavar's free." The existing reply now also carries `report_act`. The Survey identifies the actor, and +120 applies (accepted).
4. Sel's existing personal `trust` +5 on the same reply is untouched and separate.

---

## 8. The gates

### 8.1 Dialogue gate: Sel shares her notes (Survey at least `accepted`)

**Content.** One new reply in Sel's `again` node:
```yaml
      - id: who_turned
        text: "Who turned the stones out of line?"
        conditions: [{ kind: reputation, faction_ref: faction.ashen_hollow.survey, min_tier: accepted }]
        next: who_turned
  who_turned:
    text: "Someone who knew the work. Each stone a quarter out of true, the same quarter, and nothing left behind worth calling a track. That's what I've written down. I'd not tell just anyone."   # DRAFT copy for the owner's tone review
    choices:
      - { id: back, text: "I'll keep my eyes open.", next: again }
```

**Where it hooks.** A new `ReputationCondition(string FactionId, int MinLevel, int MaxLevel) : DialogueCondition` in `src/Domain/Social/Social.cs` (content uses tier keys; the builder resolves levels); `IDialogueFacts.StandingLevel(string factionId)` (`:125-144`); the `Holds` arm `ReputationCondition r => facts.StandingLevel(r.FactionId) is var l && l >= r.MinLevel && l <= r.MaxLevel` (`:148-159`); implementations in `DialogueSystem` and `SpeakerFacts` (`src/World/Runtime/Social.cs:397-459`); the parse arm and list entry (`src/Content/SocialContent.cs:26-27`); a `DescribeCondition` arm (`src/World/Runtime/QuestDebugger.cs:372-387`).

**Bypass: none.** `Handle(ChooseCommand)` already refuses a reply whose conditions do not hold (`src/World/Runtime/Social.cs:265`), and `View()` hides it (`:306`).

**What it shows.** Access follows live standing: after steadying (+120) and then confessing the sentinel (-120), the reply disappears again. Sel's personal trust (+20 from existing content) cannot open it; institutional access is not personal liking.

### 8.2 Service gate: Kera's iron billets (Waystation at least `accepted`)

**Content.** One gated stock row in `content/merchants/ashen_hollow/kera_voss.yaml`:
```yaml
  - { item_ref: item.material.iron_ingot, count: 3, price_bias: 1.5,
      requires: { faction_ref: faction.ashen_hollow.waystation, min_tier: accepted,
                  withheld_text: "Those billets are spoken for. The waystation's own come first." } }   # DRAFT copy
```

**Where it hooks.** `MerchantStock` (`src/Domain/Items/Items.cs:220`) gains `StandingRequirement? Requires { get; init; }`, with `StandingRequirement(string FactionId, int MinLevel, int MaxLevel, string WithheldText)` in `Domain/Factions`. `TradeSystem.Handle(BuyCommand)` checks it right after the ware is found and before pricing (`src/World/Runtime/Social.cs:488-493`): `if (Withheld(merchant, ware.DefId) is { } refused) return refused;`, where `Withheld` is the stock row's `Requires` failing `FactionRules.Permits(Requires, StandingLevel(Requires.FactionId))`. `WareView` gains `string? Withheld` (from `TradeSystem.View`, `:522-529`) so the panel greys the ware and shows the text.

**Bypass: none.** `Handle(BuyCommand)` is the only way anything leaves a trader's wares: `Trade` is dispatched only from there, `InventorySystem.Check` refuses every non-trade move of wares (`src/World/Runtime/Items.cs:456-458`), and presentation cannot construct internal commands (`src/World/Runtime/Systems.cs:14-15`). A test submits a raw `BuyCommand` with no conversation open.

**Rule details.** The gate is keyed by item definition within the merchant, so billets the player sells to Kera are withheld too (open issue). The trader-level `requires` ("a low standing makes a faction refuse service", `docs/PROGRESSION.md:452`) uses the same predicate but is **reserved**: M7 content cannot drive the Waystation negative, so it would be unreachable code.

---

## 9. Relations and membership

### 9.1 Static cross-faction relations

- **Form.** `relations: [{ faction_ref, attitude }]`. `attitude` is a closed word set: `allied (+2)`, `cordial (+1)`, `neutral (0)`, `strained (-1)`, `opposed (-2)`. Relations are directional: the canon inter-people matrix is directional (`world_lore` §4.2).
- **What it replaces.** DATA_MODEL's `attitude_default` in [-1, 1] (`docs/DATA_MODEL.md:560`) and `enemy_of` "(hard hostility edges)" (`:563`). The first is a float scale beside integer standing (C-4). The second fuses relation with hostility (K-08).
- **What it does in M7: nothing in any rule.** It is data, lint-checked, shown in the debug view and the fixture table, so that "faction relation" exists as its own layer from day one. By scope ruling 11 it must never move standing, and M7 never lets it.
- **Its first consumer** will be the reserved hostility derivation (faction relation plus war state plus legal status plus identity plus perception), or NPC dialogue flavour.
- **M7 content.** Waystation to Survey: cordial. Survey to Waystation: cordial. The proof needs two factions with incommensurable interests that are *not* enemies. A player can be accepted by one and wary with the other while the factions stay cordial. That is ruling 3's separation made visible.

### 9.2 Membership, and PROTOTYPE A-2

Membership is **one optional `faction_ref` on the NPC definition**:
- DATA_MODEL already puts it there (`docs/DATA_MODEL.md:297`);
- today the builder silently ignores it (`src/Content/SocialContent.cs:85-89` does not refuse it);
- M7 makes it built and read.

The faction definition carries **no `members:` list**, which avoids a second source of truth. `FactionSetup.Members` is derived at content build (faction to sorted NPC IDs). Multi-affiliation (the "credentials" of `SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:255-270`) is reserved as `affiliations:`. The player is never a member in M7: joining is deferred.

**The A-2 gap.** PROTOTYPE says "`faction_id` field exists on NPCs and is saved" (`docs/PROTOTYPE.md:110`, `:264`, `:395`). No such field exists (`src/Domain/Social/Social.cs:10`), and NPCs are not saved (`src/World/Runtime/Social.cs:98-104`). M7's resolution:
- membership is *definition data* while nothing can change it;
- saved, instance-level membership arrives with the first system that changes membership (joining, defection, generated members);
- storing a static field in the save now would violate I-1 ("store nothing derivable").

PROTOTYPE A-2 is corrected in the M7 reconciliation (§17).

### 9.3 Where faction-global state lives

M7 has **no** world-global faction state: every M7 faction record is either about the player (standing, the player's acts, what factions know of them) or static content. The player section is the honest home, following the relationships precedent (`src/World/Runtime/RuntimeState.cs:63-64`) and PERSISTENCE §5.1 ("faction reputation and crime records", `docs/PERSISTENCE.md:188`). World flags stay cell-scoped and unused for factions. The first genuinely world-global faction state (war state, faction control, NPC-authored acts, faction-to-faction sharing) is reserved for a new world-global save record that does not exist today (`persistence` §0 item 3). When it arrives, the act log moves there by migration; `ActRecord.Actor` and `FactionKnowledge.Knower` are explicit fields, not an implicit "the player", so the move is mechanical.

---

## 10. Q6 - personal relationship vs faction reputation

- **Two systems, no coupling in either direction.** `RelationshipSystem` (M4) owns `StateSlice.Relationships`; `FactionSystem` owns `StateSlice.Factions`. Neither dispatches to the other: a reaction never calls `ChangeRelationship`, a relationship value never feeds a reaction, gate or knowledge, and standing never gates a relationship event.
- **Authored co-occurrence is explicit.** One reply may carry both a `record_relationship_event` and a `report_act`. Sel's `tavar_back` keeps `trust +5` exactly as authored and gains an independent `report_act`: Sel is personally grateful; the Survey, as an institution, judges the steadying.
- **Divergence is the point.** Confessing the sentinel costs the Survey 120 and Sel's `trust` nothing. "Someone can like the player, trust them, respect their skill, and still despise their necromancy" (`docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md:52`).
- **Nothing is replaced.** The eight shipped relationship events stay (`social_quests_code` §1.4); no trust value becomes standing. Test 8 (§16) proves it.

---

## 11. Q7 - hostility: out of M7, with the separation enforced

M7 needs no faction hostility and no faction combat:
- NPCs cannot be struck (`src/World/Runtime/Combat.cs:496`, `:554`);
- creatures carry no faction, and "every creature against the player, indifferent to each other" stands (`docs/ROADMAP.md:226`).

Nothing is built. What M7 does to keep the layers apart:

1. **No field can express hostility.** The ladder refuses renames (no "hostile" tier); relations include `opposed` but no rule reads them; there are no `laws` or `enemy_of`.
2. **An architecture test forbids tactical code from reading reputation.** `TacticalCode_NeverReadsReputation`, a source scan in the style of `PresentationSource_NeverReachesPastThePublicReadAndCommandSurface` (`tests/Architecture.Tests/ArchitectureTests.cs:163`): no line of `src/World/Runtime/Combat.cs`, `Creatures.cs`, `Companions.cs` or `Magic.cs` may contain `StateSlice.Factions`, `StandingOf`, `FactionRules`, `StandingLevel` or `Knowledge[`.
3. **Reserved shapes, each its own layer:** `WarState(FactionA, FactionB, State)` (world-global); `LegalStatus(JurisdictionId, Layer)` with the doctrine's seven layers, never one bounty number (`CRIME_LAW...:118-128`); `CanAttemptAttack(attacker, target, context)`. Each will read acts and knowledge, never standing.
4. **The coupling text goes** (§17): VERTICAL_SLICE's "Hostile" tier and "puts town guards on you" (`docs/VERTICAL_SLICE.md:95`, `:169`); S-27's "legal/offense state derived from it" (`docs/SYSTEMS.md:304`).

---

## 12. Building: the faction-relevant act it produces

**Yes, in one narrow way.** The building system dispatches `RecordAct(piece_placed, ...)` after every successful placement. Notability (§3.3) decides whether anything is recorded.

M7 content has exactly one building reaction: Waystation `raised_a_workplace`, `piece_placed`, `tag: crafting_station`, +40, limit 1. "A second workplace at a frontier stop" is a public good to a steward and a smith. It is also the one reaction that exercises the witnessed channel in ordinary play: if Kera or Renn stands within 25 m with a clear line (a door open, or the work-assignment NPC nearby), the Waystation learns the act at once. Otherwise the player can report it to Renn.

**Why tag matching.** The faction content does not depend on the building design's piece ID. The tag is expanded at content build to the set of piece definitions carrying it. The lint requires that set to be non-empty.

**Not reacted to:** dismantle and destroy (reserved act kinds), walls, and roofs. M7 does not turn construction into a morality test.

---

## 13. Code layout for the implementer

| Where | What |
|---|---|
| `src/Domain/Factions/Factions.cs` (new) | `FactionDefinition(Id, Name, SeatLocationId, StartPoints, Reactions, Relations)`; `ReactionRow(Id, ActPattern, Delta, Limit)`; `ActPattern(Kind, ImmutableSortedSet<string> Subjects)` (tags pre-expanded); `Tier`, `StandingLadder(Tiers, Min, Max, OrdinaryFloor)`, `ReputationRules(Ladder, WitnessRangeMm, ActLogCap)`, `StandingRequirement`; `ActKinds`, `KnowledgeSources`, `ActorIdentity`, `Attitudes` (each with `Built`/`Reserved`); pure `FactionRules`: `FirstMatch`, `TierOf`, `Apply`, `Permits`, `IsNotable`, `SettledEverywhere` |
| `src/Domain/Social/Social.cs` | `NpcDefinition` init props `FactionId`, `WatchLocationIds`; `ReputationCondition`, `DidActCondition(ActPattern, Negated)`, `ReportActConsequence(ActPattern)`; `IDialogueFacts.StandingLevel`, `.DidAct`; `Holds` arms |
| `src/Domain/Quests/Quests.cs` | `faction_reputation` moves to `Built` (`:141-158`) with `IQuestFacts.StandingLevel`; `faction_state`, and the `reputation`/`access` rewards (`:203-206`), stay unbuilt |
| `src/World/PlayerState.cs` | The §2 records; `PlayerRecord` init props `Acts`, `Factions` (default empty, validated), carried by **every** `With*` as `Posture` is, hashed into `Digest` under tag `unnamed.player/v10` (`:322-368`) |
| `src/World/Runtime/RuntimeState.cs` | `StateSlice.Acts`, `StateSlice.Factions` ("saved with the player (schema 14)"), initialized from the record, gated setters |
| `src/World/Runtime/Factions.cs` (new) | `FactionSetup(Factions, Rules) { Members }`; internal commands `RecordAct`, `NoteAct(Seq)`, `ReportActs(ListenerNpcId, ActPattern)`, `ForgetAct(Seq)`; `ActSystem` and `FactionSystem` (no `Tick`); events `ActRecorded`, `ActForgotten`, `FactionLearned(FactionId, Act, Source, Via, Identity, Tick)`, `ReputationChanged(FactionId, From, To, Act, ReactionId, Tick)`, `StandingTierChanged(FactionId, From, To, Tick)` (S-27's names); views `FactionView`, `ActView` |
| `src/World/Runtime/Simulation.cs` | `SimulationSetup.Factions`; composed after `_trade` (`:134`); `Dispatch` arms (`:369-404`); get-only `Factions`, `Acts` (no allow-list change); `CaptureRecord` (`:342-351`) |
| `Creatures.cs`, `Systems.cs`, `Social.cs` (World/Runtime) | The two `RecordAct` sites; `report_act` in `DialogueSystem.Apply` (`:359-387`); the trade gate; `WareView.Withheld` |
| `src/Content/FactionContent.cs` (new) + Social/Quest/merchant builders | Builders and lints (§14); fill `FactionSchema` (`src/Content/SchemaTypeMapper.cs:253-255`); hook in `ContentLoader.LoadAll`; `GameSession.Boot` sets `Factions` |
| `src/Presentation` | Debug panel "Factions" (standing and tier, knowledge with source/via/identity/settled/delta, act log, relations, both gate states). Player-facing: the tier word per *met* faction (a line heard from a member). HUD log for `ReputationChanged` only for `reported` knowledge; witnessed and watched changes are silent, since the character need not know they were seen. No new command, no radial |

Neither new system ticks; the `Step` order does not change.

---

## 14. Content: YAML and lint

### 14.1 `content/factions/ashen_hollow/waystation.yaml`

```yaml
id: faction.ashen_hollow.waystation
kind: faction
schema: 1
display_key: faction.ashen_hollow.waystation.name
tags: [faction]
name: the Waystation            # working name, not canon (bible :265)
notes: The people who keep the Ashen Hollow road stop running (Renn Vale, Kera Voss). Values supply, a safe road and a stop that grows. Members carry faction_ref; this file lists none.
seat_location_ref: location.outpost
start_points: 0
reactions:                      # first match wins, per faction
  - { id: seam_made_safe,     act: creature_killed, creature_ref: creature.construct.animated_armour, delta: 60, limit: 1 }
  - { id: foldscar_quieted,   act: switch_set,      flag_ref: world.foldscar.steadied,                delta: 60, limit: 1 }
  - { id: raised_a_workplace, act: piece_placed,    tag: crafting_station,                            delta: 40, limit: 1 }
relations:
  - { faction_ref: faction.ashen_hollow.survey, attitude: cordial }
```

### 14.2 `content/factions/ashen_hollow/survey.yaml`

```yaml
id: faction.ashen_hollow.survey
kind: faction
schema: 1
display_key: faction.ashen_hollow.survey.name
tags: [faction]
name: the Survey                # working name; the institution behind Sel is deliberately unnamed
notes: Sel Arien's survey post. Keeps the Record of the ruins around Ashen Hollow; wants old workings understood before they are used up, and the Quiet Stones set right.
seat_location_ref: location.outpost
start_points: 0
reactions:
  - { id: stones_restored,       act: switch_set,      flag_ref: world.foldscar.steadied,                delta: 120,  limit: 1 }
  - { id: old_working_destroyed, act: creature_killed, creature_ref: creature.construct.animated_armour, delta: -120, limit: 1 }
relations:
  - { faction_ref: faction.ashen_hollow.waystation, attitude: cordial }
```

### 14.3 NPC additions

```yaml
# content/npcs/ashen_hollow/renn_vale.yaml and kera_voss.yaml
faction_ref: faction.ashen_hollow.waystation
# content/npcs/ashen_hollow/sel_arien.yaml
faction_ref: faction.ashen_hollow.survey
watch_location_refs: [location.foldscar]     # "where she can watch the Foldscar" (renn_vale.yaml:47)
# tavar_orr.yaml: no faction_ref (a companion; refused in M7)
```

Both `faction_ref` and `watch_location_refs` are already checked by the generic `*_ref` walker (`src/Content/ContentChecks.cs:34`, `:36`).

### 14.4 Dialogue additions

The text of every existing line is unchanged. New replies are marked `DRAFT copy`.

```yaml
# sel_arien.yaml, on BOTH existing tavar_back replies (:21-27 and :57-65), appended after the relationship event:
          - { command: report_act, act: switch_set, flag_ref: world.foldscar.steadied }
# sel_arien.yaml, a new reply in `again`:
      - id: armour_confess
        text: "I broke the old armour in Blackvein Cut."
        conditions:
          - { kind: did_act, act: creature_killed, creature_ref: creature.construct.animated_armour }
          - { kind: visited, node: armour_confess, not: true }
        consequences: [{ command: report_act, act: creature_killed, creature_ref: creature.construct.animated_armour }]
        next: armour_confess
  armour_confess:
    text: "Broke it. It stood there longer than this waystation has, doing a job nobody remembers giving it. I'd have liked to know who gave it."   # DRAFT copy
    once: true
    next_if_exhausted: again
    choices: [{ id: back, text: "It was in my way.", next: again }]
# renn_vale.yaml, two new replies in `again` (same shape): armour_news / foldscar_news
#   armour_news:   "The armour that stood over the seam is scrap."  -> "Is it. Then Kera gets her iron without paying for it in blood."  (DRAFT)
#   foldscar_news: "The Foldscar's quiet. The stones are back in line." -> "Quiet's the best news that ruin has ever sent us."  (DRAFT)
#   each: conditions did_act(...) + visited(not), consequence report_act(same pattern)
```

### 14.5 `content/config/reputation.yaml`

```yaml
id: config.reputation
kind: config
schema: 1
notes: PROGRESSION.md §10's ladder, from the top; a tier holds points >= its min. Names are fixed by PROGRESSION (no hostility word). Tiers are derived, never saved.
ladder:
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
points: { min: -1000, max: 1000, ordinary_floor: -999 }
witness_range_m: 25
act_log_cap: 256
```

### 14.6 Lint (`FAC001`, builder-style, plus new SOC and QST arms)

- **Ladder:** exactly the 11 keys in order; levels +5..-5; `min` strictly descending; `neutral` contains 0; `ordinary_floor` > the `anathema` min.
- **Factions:** `seat_location_ref` exists; `start_points` in the neutral band; unique reaction ids; `act` is a built kind; exactly one subject selector, either the ref field matching the kind (`creature_ref` / `flag_ref` / the building design's piece ref) or a `tag` that expands to at least one definition; `delta` non-zero, `|delta| <= 500`; `limit` 1..100; `anathema:` refused; a shadowed row warns; `relations` targets exist, are not self, and use the closed attitude set. `laws`, `members`, `enemy_of`, `territory`, `joinable`, `join_requirements`, `reputation_tiers`, `services_gated`, `attitude_default` are refused: "not built in M7; see M7 design §17".
- **NPCs:** `faction_ref` exists; the NPC's placement is inside the seat radius; `watch_location_refs` requires `faction_ref`; `faction_ref` is refused with a `companion:` block.
- **Dialogue:** `reputation` needs every participant to be a member; `did_act` must match some faction's row; `report_act` needs a single-faction speaker whose faction has a matching row.
- **Merchants:** `requires.faction_ref` is the trading NPC's faction.

**Tests.** `LoadAll_Loads_Yaml_Files` gains `config.reputation`, `faction.ashen_hollow.survey` and `faction.ashen_hollow.waystation` (`tests/Content.Tests/ValidationTests.cs:508-547`).

---

## 15. Q8 - persistence: schema 14

- **What is added.** The player section gains two **required** fields:
  - `acts`: `ActLogDto { next_seq, records[] }`;
  - `factions`: `FactionLedgerDto { standings[], knowledge[], applied[] }`.

  Keys are snake_case strings. Enums are string keys, never ordinals (`src/Persistence/SectionCodec.cs:17-18`).

  ```
  ActDto           { seq: long, tick: long, kind: string, subject: string, site: string?, target: string?, actor: string, x_mm: long, z_mm: long }
  StandingDto      { faction_id: string, points: int, changed_tick: long }
  KnowledgeDto     { knower: string, act: long, learned_tick: long, source: string, via: string?, identity: string,
                     settled_tick: long?, reaction_id: string?, delta: int }
  AppliedDto       { faction_id: string, reaction_id: string, count: int }
  ```
- **Decode ("corrupt, not defaulted").** A schema-14 player without `acts` or `factions` throws `FormatException` (pattern `src/Persistence/SectionCodec.cs:399-409`). Decode also rejects: seqs not strictly ascending or ≥ `next_seq`; kinds, sources or identities outside the closed sets; knowledge whose act is not in `records`, or that is settled while unidentified; points outside [-1000, 1000]; counts below 1.
- **Migration.** `SchemaV13ToV14` is **one step shared with the building design**. It reads a frozen `V13.Player` (the current `PlayerDto` field list copied into `Sections/SchemaV13.cs`, with `SchemaV12ToV13` repointed to write it, `src/Persistence/Migrations.cs:710-747`), writes `acts = { next_seq: 1, records: [] }` and `factions = { [], [], [] }`, has a summary starting `"schema 13 -> 14:"`, and is appended to `Production` (`:69-81`). A migrated M6 save therefore has no history: migrations may not read content, so they cannot infer that `world.foldscar.steadied = 1` was the player's act (open issue).
- **Definition-ID pass** (`src/Persistence/SaveLoader.cs:316-354`, extended):
  - Faction IDs (`standings`, `knowledge.knower`, `applied`) resolve; a removed faction takes its records with it (reported loss). Two IDs resolving to one merge: points summed and clamped, max `changed_tick`, knowledge unioned by `(knower, act)` preferring the settled record, max count per row.
  - `acts.subject` resolves; an act whose subject was removed is retired with its knowledge (reported loss), and standing is kept.
  - `knowledge.via` resolves as an NPC; if removed, `via` becomes null.
  - `reaction_id` values are row names, not definitions: kept verbatim; the debug view flags orphans.
- **Digests.** The player digest tag becomes `unnamed.player/v10`; `StateDump` includes the new public `PlayerRecord` properties automatically (`persistence` §9.1); `CanonicalState.Render` adds them field by field.
- **v14 fixture.** `M2Fixtures.Historical.Player()` carries the proof ledger: act 1 `creature_killed`, settled by two fixture factions with opposite deltas; act 2 `switch_set`, known unidentified by one. The writer pack becomes `Fixtures/content-0.1.7` (0.1.6 plus two fixture factions, and a fixture creature and flag if missing). The current pack aliases `faction.fixture.keepers` → `faction.fixture.wardens` to prove the rename reaches the ledger. Older `expected.json` files gain only empty `acts`/`factions`, reviewed line by line.
- **Nothing new is transient.** S-27's "witness-propagation working set" and "service-availability cache" (`docs/SYSTEMS.md:308`) do not exist: witnessing is synchronous at the act and gates are evaluated live, so save-then-continue equals continue by construction.

---

## 16. Q9 - acceptance tests and the fixture table

### 16.1 Tests

Unless noted, each test runs through `GameSession`, driven by real `GameCommand`s. Shorthand: W = Waystation, S = Survey.

1. **`TheSameAct_MovesTwoFactions_InOppositeDirections`** (Application.Tests). Kill the armour, report to Renn, then to Sel: one act (Seq 1), two knowledge records referencing it, W +60, S -120, opposite `ReputationChanged` signs (ROADMAP `:284`).
2. **`AFactionThatWasNotTold_DoesNotMove`**. Report to Renn only: S has no record and default standing. Control: report to no one: neither moves, `did_act` still holds.
3. **`AFactionWithNoMemberPresent_NeverLearns`** (test pack, `faction.fixture.absent`): its reaction matches, no record is ever made.
4. **`AWitnessAtTheSeat_Learns` / walls, distance, away-from-seat**: act at < 25 m with a clear line: identified; behind a closed door, at 26 m, or with the member moved off the seat by `PlaceNpc`: nothing.
5. **`AnUnknownActor_MovesNoStanding_UntilIdentified`**. Steady the heart: S records `watched`/Sel/`unidentified`, unsettled, standing unchanged; choose `tavar_back`: `identified`, S +120 (accepted), source stays `watched`.
6. **`RepeatedActs`**: reporting twice is a no-op; witnessed-then-reported applies once; a second crafting station is not recorded (row exhausted); flooding the log past its cap evicts settled acts first and `ReactionCount` survives.
7. **`TheGates_FollowStanding_AndCannotBeBypassed`**: `who_turned` hidden at neutral (a `ChooseCommand` for it is refused), offered at accepted, hidden again after the confession; a raw `BuyCommand` for billets is refused at W neutral with the withheld text and succeeds at W accepted.
8. **`PersonalRelationships_StaySeparate`**: over the proof script, Sel's `trust` equals exactly the M6-authored events, confessing leaves it unchanged, and a source scan shows `Factions.cs` never constructs `ChangeRelationship`.
9. **`SaveThenContinue_EqualsContinue`**: save between "watched, unidentified" and the `tavar_back` report; after load and continue, `StateDump.Compare` shows 0 differences against an unsaved control, with the same `ReputationChanged` sequence.
10. **`TheReplayOfTheCommandLog_EndsInTheSameDigest`**: the proof script replayed from a loaded save through the command log (pattern `tests/Application.Tests/DeterminismAndViewTests.cs:48-77`): equal `StateDigest`, equal act seqs.
11. **Axis independence** (owed at M7, `docs/PROGRESSION_AXIS_RECONCILIATION.md:246`): reach each cell of AX-REP × AX-LVL and AX-REP × AX-SKL through sanctioned vectors only; no act changes XP and no XP changes standing.
12. **`NoContentGrantsReputationForCoin`** (E-7, `docs/GAMEPLAY_LOOPS.md:238`): no `reputation` reward built, no trade act kind, no reaction names a merchant.
13. **`TacticalCode_NeverReadsReputation`** (Architecture.Tests, §11).
14. **Persistence**: `Schema13To14_GivesAnEmptyActLogAndLedger`; `ASchema14PlayerWithoutActsOrFactions_IsCorrupt_NotDefaulted`; `TheFactionLedger_GoesThroughTheDefinitionPass_RenamesMergesAndRemovals`; the v14 fixture loads and migrates to its expected state; hard-coded step lists updated (`persistence` §3.3 G).
15. **Content**: every §14.6 lint, negative and positive, including "no hostility word in the ladder" and "companion NPCs refuse `faction_ref`".
16. **Domain**: `FactionRules` (every tier boundary, the -999 clamp, first match, notability, `SettledEverywhere`) and the `faction_reputation` objective and its debugger term.

### 16.2 The reputation fixture table (ROADMAP proof)

`docs/M7_REPUTATION_TABLE.md` is **generated from the build**, on the precedent of `docs/M3D_BEHAVIOUR_MATRIX.md`. `ReputationTableTests` (Application.Tests) builds it from the game's own content and the real simulation. It fails when the committed file differs; regenerate with `UNNAMED_WRITE_REPUTATION=1`. Its sections:

1. **Ladder.** Tier, level, points range (from `config.reputation`).
2. **Reaction matrix.** Rows are act patterns and columns are factions. Each cell is `row_id ±delta (limit n)` or `-`. Rows whose signs differ across factions are marked **opposite**.
3. **Knowledge channels.** Per faction: seat, members and the watches each keeps.
4. **Scenarios, measured.** One row per step. Columns:

| # | Step (commands) | Act | Waystation: knows (source/via/identity) | Δ | pts | tier | Survey: knows | Δ | pts | tier | Sel trust | Sel `who_turned` | Kera billets |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| P1 | kill the armour | #1 creature_killed armour | - | | 0 | neutral | - | | 0 | neutral | 0 | hidden | withheld |
| P2 | tell Renn `armour_news` | | reported/renn/identified | +60 | 60 | neutral | - | | 0 | neutral | 0 | hidden | withheld |
| P3 | tell Sel `armour_confess` | | | | 60 | neutral | reported/sel/identified | -120 | -120 | wary | 0 | hidden | withheld |

   The scenarios are P (proof), N (not told), U (unknown actor then identified), R (repeats), G (gates), and L (save/load in the middle of U, with the row after the load identical). The test also asserts the P-row signs directly, so the table cannot drift from the exit criterion.

---

## 17. Documents M7 must reconcile (factions part)

| Document | Change |
|---|---|
| `ROADMAP.md:282` | Crime/bounty/pardon: "deferred (Phase 3, `PROTOTYPE.md:40`); the act log is the seam". Territory gating: deferred. The opposite-directions fixture points at `M7_REPUTATION_TABLE.md` |
| `SYSTEMS.md` S-27 (`:302-309`) | Drop "legal/offense state derived from it" and `AddReputation`. Owns act log (via `ActSystem`), knowledge, standing (tier derived), static relations; transient: none; add the §4.1 knowledge rule and the §13 events |
| `DATA_MODEL.md` §4.13 (`:557-581`) | Replace with §14.1's shape; record where each dropped field went (`members` → NPC `faction_ref`; `laws` → future jurisdictions; `enemy_of`/`attitude_default` → `relations` + future war state; `reputation_tiers` → `config.reputation` + `requires`; `territory`/`joinable` deferred) |
| `DATA_MODEL.md` §4.12 (`:525-529`), §4.11 | `reputation` takes tiers; add `did_act`, `report_act`; `add_reputation` stays unbuilt (a future `said` act); `faction_reputation` takes tiers |
| `PROGRESSION.md` §10 | Point to `config.reputation`; decay deferred (`ChangedTick` seam); "trade volume" means goods supplied (a deed), never coin; Anathema unreachable until atonement content exists |
| `VERTICAL_SLICE.md:95`, `:169` | 11-tier ladder, no "Hostile"; "hostile / town guards on you" rephrased as relation, war state, legal status (M9) |
| `PROTOTYPE.md:110`, `:264`, `:395` (A-2) | Membership is definition data until something can change it |
| `PERSISTENCE.md` §5.1 / §6.2; `ARCHITECTURE.md` §5 | Schema-14 paragraph and chain rows; `ActSystem` ownership row; tier derived |
| `CRIME_LAW_REPUTATION_AND_JUSTICE.md:3`; owner ruling 3 | Status note (what-happened and believed layers built; proof and law reserved); write "smallest useful set" and the eleven-concept separation into PROGRESSION §10 and S-27 |

---

## 18. Cross-system contracts

- **Building to factions.** After each committed placement, dispatch `RecordAct(new ActDraft("piece_placed", pieceDefId, x, z) { Site = buildAreaKey, Target = pieceId })`. Tag the crafting-station piece definition `crafting_station`. Expose placed blocking pieces (walls, closed placed doors) in the wall set that creature sight and witness lines both use (`Layout.Space.Blockers ∪ ClosedDoors() ∪ placed`).
- **NPC work assignment and navigation to factions.** Witness and seat checks read `State.Npcs[..].Body` at the act. A member walked outside the seat radius contributes no knowledge. If the assigned NPC is Kera or Renn, place the work anchor inside the outpost's 30 m to keep them a witness, or accept that they witness nothing while working there.
- **Persistence.** One 13 -> 14 step and one v14 fixture, shared with building. The player digest becomes `/v10` (factions). The effective-cell digest bump belongs to building.
- **Presentation.** Read-only use of `Simulation.Factions`, `Simulation.Acts` and `WareView.Withheld`. No new public method; no new command. Reports and purchases use the existing `ChooseCommand` and `BuyCommand`.
- **Quests.** `faction_reputation` is built, with an `IQuestFacts.StandingLevel` debugger term.
- **Combat, creatures, companions.** They must not read faction state (test 13).
- **Future crime and law.** It consumes the act log and knowledge, and never standing.

---

## 19. Scope-ruling position

No challenge: crime deferred (item 4), one dialogue and one service gate with territory not built (item 10), static relations that move nothing (item 11), the PROGRESSION §10 ladder (item 12).

## 20. Owner questions, and open issues

**Owner question (for slot 5).** Approve the working pair and the proof act:
- the pair: the Waystation (Renn, Kera) and the Survey (Sel), with Tavar unaffiliated as a hired guide;
- the proof act: destroying the Blackvein sentinel, which the Waystation approves and the Survey disapproves.

The alternative is the steadying of the Foldscar read against an absent "stone-turners" interest. That interest would have no members in Ashen Hollow, so in M7 it could never learn anything, and it would put new lore about who turned the stones into the world.

**Open issues:**
1. **No history before M7.** Migrated saves start with an empty act log. Deeds done in M6 cannot be reported or judged.
2. **The witnessed channel rarely fires in shipped play.** Renn and Kera stand inside walled buildings. Fixtures prove the channel; in play it fires only opportunistically.
3. **The player sees live tiers.** The tier word is shown for factions the character has met, which slightly leaks what the character may not know. A "last observed in conversation" display is the doctrine-pure refinement.
4. **Instant sharing at the seat.** Members at a seat share knowledge instantly. That is the one stated simplification of STEALTH §6.
5. **Line-of-sight arithmetic.** It uses `Blocker.Crosses` (doubles). That is same-machine deterministic, like the existing sight lines.
6. **Phase-1 leaks remain.** Dialogue conditions still read global state (cross-dialogue `visited`, `quest_state`, `relationship` on any NPC). They belong to the knowledge milestone.
7. **The billet gate covers sold billets.** It is keyed by item definition, so billets the player sells to Kera are withheld too.
8. **A companion's kill cannot be reported.** When Tavar kills the sentinel, the act is not the player's.
9. **Decay and Anathema.** PROGRESSION decay is deferred. Anathema is unreachable until atonement content exists.
10. **Content copy.** All new lines are drafts for the owner's tone review.
11. **The `faction_reputation` objective has no content user in M7.** It is built because the code comment promises it for "factions (M7)" and M9 needs it. The alternative is to defer it to M9.
