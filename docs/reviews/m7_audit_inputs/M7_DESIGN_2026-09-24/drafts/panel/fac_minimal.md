# M7 Factions / Reputation v1 - candidate design (lens: minimum code and data that proves it)

Status: design candidate for the M7 panel, 2026-09-24. Not an owner ruling. Base: origin/main `e10d2c4`; every `path:line` is repo-relative in the snapshot `main_e10d2c4`. Built within `drafts/00_SCOPE_RULINGS.md`; no challenge to a scope ruling is raised (section 18 explains the one place I lean on a default).

---

## 0. The design on one page

M7 factions are **two local working factions in Ashen Hollow whose members are the four existing NPCs**, a **persisted log of the player's faction-relevant acts**, **per-faction knowledge rows** that say which faction learned which act and how, and **per-faction integer standing** whose tier is derived from one ladder in content. The whole mechanism is one new system, two internal commands, three dialogue vocabulary words, one trade check and one player-section field.

- **Acts** are what happened. An acting system dispatches `RecordAct` the way it already dispatches `RecordDeed` (`src/World/Runtime/Quests.cs:62`). M7 has three act kinds: `killed` (a creature the player killed), `set_switch` (a switch the player set) and `placed` (a building piece the player placed). An act is recorded only if some content can react to it or report it.
- **Knowledge** is what a faction perceived. A faction learns an act only when (a) a member NPC **sees** it at the moment it happens, using the existing `Perception.Sees` (`src/Domain/Creatures/Perception.cs:87-106`) against the same walls creatures see through, or (b) the player **tells** a member in dialogue (`report_act`). Nothing else informs a faction: no rumour, no propagation between factions, no quest reward that writes standing.
- **Standing** is what the faction now thinks. It moves only when a faction learns an act **with the actor identified** and its own reaction table has a row for that act. A witness beyond identifying range records "it happened, actor unknown" and moves nothing. A later report upgrades that knowledge once.
- **Proof in Ashen Hollow:** killing the Animated Armour that guards Blackvein's seam. The Waystation (Renn, Kera) approves, +30. The Survey (Sel, Tavar) disapproves, -30. Each moves only if it learns of the kill. The Waystation's approval opens a **service gate**: Kera sells the hide vest and cap only to those the Waystation has Accepted. The Survey's view drives a **dialogue gate**: Sel shares her notes only while the Survey holds the player at Accepted, which the player reaches by steadying the Foldscar's heart. Kill the armour where Tavar can see, or tell Sel, and her notes close again.
- **Nothing reads standing except** one dialogue condition, one trade check and the views. Standing never feeds hostility, combat, legal status or personal relationships. An architecture test pins this.
- **Persistence:** one new `factions` record on the player (schema 14, shared with building's schema 14 step). No world-delta change and no global world record, because nothing mutable in M7 faction state is world-global.

What M7 does **not** build is listed in section 18.

---

## 1. Code facts this design stands on (verified in the snapshot)

| Fact | Where |
|---|---|
| The live runtime is `Simulation` + `RuntimeState` + `StateSlice`/`SliceOwner`; a slice with no owner or two owners is a boot error | `src/World/Runtime/Simulation.cs:115-139`, `src/World/Runtime/RuntimeState.cs:19-74` |
| Systems never subscribe to events; cross-system work is a synchronous internal command routed by `Simulation.Dispatch` | `src/World/Runtime/Simulation.cs:369-404`; events "never persisted", `src/World/Runtime/Events.cs:10-11` |
| `RecordDeed` is the act fan-in precedent: a player kill dispatches it only `if (killer == _player)` | `src/World/Runtime/Creatures.cs:701-717` |
| A switch sets its flag in `InteractionSystem.Work`, then publishes `SwitchSet` | `src/World/Runtime/Systems.cs:276-292` |
| `faction` is a registered kind (`factions/`, prefix `faction`); `FactionSchema` is an empty placeholder; the generic walker resolves `*_ref` and `*_refs` | `src/Content/SchemaResolution.cs:158-164`; `src/Content/SchemaTypeMapper.cs:250-256`; `src/Content/ContentChecks.cs:34`, `:132`, `:158` |
| `NpcDefinition` has no faction field; `BuildNpcs` refuses five Phase-1 fields and ignores unknown keys | `src/Domain/Social/Social.cs:10-16`; `src/Content/SocialContent.cs:69-94` |
| Relationships: five dimensions, ints in [-100, 100], zero not stored, changed only by `ChangeRelationship` | `src/Domain/Social/Social.cs:30-40`; `src/World/Runtime/Social.cs:94`, `:188-197` |
| Dialogue conditions are a closed switch over `IDialogueFacts`; consequences are commands; `open_service` only publishes an event | `src/Domain/Social/Social.cs:124-159`; `src/World/Runtime/Social.cs:359-387`, `:373-375` |
| `TradeSystem` owns no state; `BuyCommand` is accepted without any conversation; the gate is `Trader(...)` | `src/World/Runtime/Social.cs:462-550` |
| Quests refuse `faction_reputation`/`faction_state` ("factions (M7)") and the `reputation` reward | `src/Domain/Quests/Quests.cs:154-155`, `:205-206` |
| Creature sight walls are the static blockers plus closed doors and standing barriers | `src/World/Runtime/Creatures.cs:893` |
| A circle blocker whose inside holds the eye counts as crossing (Tavar inside the fold sees nothing) | `src/Domain/Spatial/Blockers.cs:117-124` |
| Player-held state is added as an init property carried by every `With*` copy, hashed in `Digest` (tag `unnamed.player/v9`) | `src/World/PlayerState.cs:222-253`, `:306-320`, `:323-368` |
| World flags are per cell; there is no global world record in the save | `src/World/WorldDelta.cs:250-262`; research `persistence.md` §0 item 3 |
| Tavar stands in the fold at (145, 42) facing 53 degrees, 10 m from the heart at (153, 48) | `content/regions/ashen_hollow.yaml:155`, `:185-192` |

---

## 2. The five layers, and what M7 builds of each

The doctrine keeps five things apart (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:8`). M7 builds the first two, a two-rung stub of the third, and nothing of the last two. Each built layer has its own record, so later milestones add layers instead of splitting a merged one.

| Layer | M7 representation | Owner | Not in M7 |
|---|---|---|---|
| What happened | `ActRecord` in the act log: kind, subject, cell, position, tick, sequence | `FactionSystem` | NPC acts, acts nobody can react to |
| What was perceived | `FactionKnowledge` rows on the act: which faction, via witnessed or reported, by which member, when | `FactionSystem` | per-member memory, delay, distortion, lying witnesses |
| What is believed | `ActorKnown` bool: the faction knows the player did it, or only that it happened | `FactionSystem` | the identity ladder beyond two rungs, suspicion, disguise |
| What is proven | nothing | - | evidence, provenance, confidence |
| What the law says | nothing; no code derives legal status from standing | - | crime, jurisdiction, bounty, pardon |

---

## 3. Minimum entities and data (brief item 1)

| Candidate entity | M7 decision | Why (Otherreach-specific) |
|---|---|---|
| **FactionDefinition** | **Yes.** Content, `kind: faction`. Fields: `name`, `reactions`, `relations`, `service_gates`. Every other DATA_MODEL §4.13 field is refused by name. | The kind is already registered (`SchemaResolution.cs:158-164`), so this is a builder plus lint, not a registry change. |
| **FactionRelationship** (faction to faction) | **Static data only**: `relations: [{ faction_ref, attitude }]` on the definition. There is no runtime type and no runtime effect. | Scope ruling 11. ROADMAP lists "cross-faction attitude relations"; M7 has no war state to feed. |
| **ReputationRecord** | **Yes**: `FactionStanding(FactionId, Points)` on the player. The tier is derived. | Store points, derive tiers. This resolves the SYSTEMS "values and tiers" vs ARCHITECTURE "derived" tension (research `faction_docs.md` C-11) without migration-locking thresholds. |
| **KnownAct / reported event** | **Yes**: `ActRecord` (ground truth) carrying `FactionKnowledge` rows (perception). Two layers, one list. | Needed for idempotency (a witness followed by a report counts once), the unknown-to-known upgrade, the player's report reply, and attribution (T-20 "attributable to source events"). |
| **Value/tag preferences** | **No.** A reaction row names one act kind and one exact subject ID. | Ashen Hollow needs three rows. Tag matching adds an ambiguity rule for overlapping rows and nothing to prove. M9 can add `subject_tag` rows additively. |
| **Membership** | **Yes**: NPC `faction_ref` (one). The faction definition has **no** `members` list. | One source of truth. The walker already resolves `faction_ref` on an NPC, and a missing faction is an XREF error (research `content_registry.md` §3.3 PROBE). |
| **Hostility thresholds** | **No.** | Standing is access only (`docs/PROGRESSION.md:444-452`). M7 has nothing hostile that standing could drive. |

---

## 4. Acts (brief item 2)

### 4.1 Kinds, and where each is emitted

```csharp
// src/Domain/Factions/Factions.cs
public enum ActKind { Killed, SetSwitch, Placed }   // keys: killed, set_switch, placed (snake_case, never ordinals)
```

| Kind | Subject | Emitted by | Exact hook | Act position |
|---|---|---|---|---|
| `killed` | creature definition ID | `CreatureSystem.Die` | after the `RecordDeed` dispatch inside `if (killer == _player)` (`Creatures.cs:716-717`) | the player's body |
| `set_switch` | switch site key (e.g. `switch.foldscar_heart`) | `InteractionSystem.Work` | after `SetWorldFlag` succeeds, before `SwitchSet` is published (`Systems.cs:288-290`) | the player's body |
| `placed` | piece definition ID (building's kind) | building's placement handler | after a placement is committed | the player's body |

The position is the **actor's** body, because a witness sees the actor. Seeing only the effect from afar ("I saw the ring go quiet from the rise") belongs to the evidence layer, which is deferred.

`killed` mirrors the deed rule exactly: a companion's kill is not the player's act (`docs/M6_STATUS.md:69`). This is a deliberate consistency choice. Its cost is recorded in section 19.

Trade, dialogue choices, quest completion, harvesting and container takes are **not** act kinds in M7:
- trade would make gold a reputation vector (E-7, `docs/GAMEPLAY_LOOPS.md:238`);
- a quest-completion act is "quest X gives faction A" by another name;
- a container take would need ownership, which is the crime milestone's concern.

### 4.2 The command, not an event

```csharp
// src/World/Runtime/Factions.cs
/// <summary>To FactionSystem: the character did something a faction may react to, here, now.</summary>
internal sealed record RecordAct(ActKind Kind, string Subject, long XMm, long ZMm) : InternalCommand;

/// <summary>To FactionSystem: the character told this NPC about their own act (a dialogue report_act).</summary>
internal sealed record ReportAct(ActKind Kind, string Subject, string SpeakerNpcId) : InternalCommand;
```

Both are routed in `Simulation.Dispatch` (`Simulation.cs:369-404`) as `RecordAct a => _factions.Handle(a, Now)` and `ReportAct r => _factions.Handle(r, Now)`. There is no subscription, which keeps the live pattern ("Systems never subscribe", research `sim_core.md` §0 item 5) and keeps replay a function of the command log.

### 4.3 Relevance filter (bounds the log)

At boot, `FactionSetup.Relevant` is the ordinal-sorted set of `(ActKind, subject)` pairs named by:
- any faction reaction row;
- any dialogue `act_done` condition;
- any dialogue `report_act` consequence.

`Handle(RecordAct)` returns immediately, recording nothing, for a pair outside the set. In Ashen Hollow the set has two pairs:
- `(killed, creature.construct.animated_armour)`: its spawner has no respawn, so at most one act ever;
- `(set_switch, switch.foldscar_heart)`: the switch sets once, so at most one act ever.

The log therefore holds at most two records in shipped content.

Consequence (accepted): content added later cannot react to an act recorded before that content existed, because irrelevant acts were never recorded. This is the same rule as quest deed counting ("only while active").

### 4.4 Identity and ordering (deterministic, no ULIDs)

- An act's identity is its **sequence number** `Seq`, taken from a persisted counter `NextActSeq` (starting at 1) that `FactionSystem` increments.
- Sequence order is dispatch order. Dispatch order is fixed by:
  - FIFO command drain (`Simulation.cs:268-306`);
  - the fixed step order (`Simulation.cs:312-339`);
  - each system's sorted iteration (for example `State.Creatures` is an ordinal `ImmutableSortedDictionary`).
- No event ordering is involved.
- Acts are **history rows, not world instances**, so D-04's ULID rule does not apply. The precedent is `harvest_seq` on node records in `CellDto`.
- Sequence numbers keep `StateDump(replayable)` equal across two fresh runs of one script. A ULID minted from the wall clock (`EntityId.NewId`) would not.

### 4.5 The act record's fields

```csharp
// src/Domain/Factions/Factions.cs
public sealed record ActRecord(
    long Seq,                 // >= 1, unique, < ledger.NextActSeq
    ActKind Kind,
    string Subject,           // definition ID (killed, placed) or switch site key (set_switch)
    string CellKey,           // CellKey.OfWorld(x, z) of the actor, e.g. "r_0_0:c_01_00"
    long XMm, long ZMm,       // the actor's body when the act happened (integer mm)
    long Tick,                // the tick the act belongs to (Simulation.Now)
    ImmutableArray<FactionKnowledge> Known);   // sorted by FactionId, ordinal; at most one row per faction

public enum KnowledgeSource { Witnessed, Reported }   // keys: witnessed, reported

public sealed record FactionKnowledge(
    string FactionId,
    bool ActorKnown,          // false: "it happened, we don't know who" (seen beyond identify range)
    KnowledgeSource Via,
    string? LearnedFrom,      // the member NPC who saw it or was told (the witness); null only after a removed NPC
    long Tick,                // when the faction learned it
    int Delta);               // the standing change it caused; 0 while ActorKnown is false or the faction has no row
```

The actor is always the player in M7, because every command's actor is the player (research `sim_core.md` §2.6), so no `actor_id` is stored. The act log is on the player record for that reason. Section 13 says where it moves when NPCs act.

The **witnesses** the brief asks for are the `Witnessed` knowledge rows: one per faction, the member who saw best. Unaffiliated NPCs are not stored, because in M7 they have nobody to tell. There are none in Ashen Hollow anyway.

---

## 5. Knowledge: when a faction knows (brief item 3)

### 5.1 The rule

A faction learns an act **only** through one of these:

1. **Witnessed.** At the moment the act is recorded, a member NPC's body sees the act position:
   - `Perception.Sees(npc.Body, witness.Senses, act.XMm, act.ZMm, context.SightWalls())` is non-null;
   - the member is not a downed companion (`State.Companions[npc].Condition == Downed` sees nothing);
   - `ActorKnown = distance <= witness.IdentifyMm`.
2. **Reported.** A dialogue reply carrying `report_act` is chosen while speaking to a member. The speaker's faction learns every recorded act matching `(kind, subject)` that it does not yet know with the actor identified, in ascending `Seq`, with `ActorKnown = true`, `Via = Reported`, `LearnedFrom = speaker`.
3. **Scripted transfer** is not a third mechanism. The dialogue is the script, and it can only inform the **speaker's own** faction. No consequence informs a faction nobody in the conversation belongs to. That would be psychic.

A faction that is not informed through (1) or (2) does not change. There is no propagation between factions, no rumour network, no in-flight report (so nothing pending to persist, research `faction_docs.md` C-13) and no NPC-to-NPC telling.

### 5.2 Witness selection (deterministic)

The sight check is a pure function in Domain:

```csharp
// src/Domain/Factions/Factions.cs
public sealed record WitnessRules(Senses Senses, long IdentifyMm);

/// For each faction with a member who sees the point: the best witness. Known beats unknown;
/// then the nearer; then the lower NPC ID (ordinal). Candidates arrive sorted by NPC ID.
public static ImmutableArray<(string FactionId, string NpcId, bool ActorKnown)> Witnesses(
    IEnumerable<(string NpcId, string FactionId, Body Body)> candidates, WitnessRules rules,
    long xMm, long zMm, IEnumerable<Blocker> walls);
```

`FactionSystem` builds `candidates` from `State.Npcs` (ordinal sorted). It skips NPCs without a faction and downed companions.

`SystemContext.SightWalls()` is a new one-line helper, `Setup.Layout.Space.Blockers.Concat(ClosedDoors())`. `CreatureSystem.Walls()` (`Creatures.cs:893`) delegates to it. Building adds placed walls and doors there once, so placed walls block witnesses and creatures alike (section 12).

**Determinism note.** `Sees` uses `Math.Sqrt/Sin/Cos` (`Perception.cs:90-97`), exactly as creature perception already does in authoritative paths. Replays are same-machine, as today. The witness call adds no new floating-point order dependence, because it runs once per act, with fixed inputs, in sorted order.

### 5.3 Intra-faction sharing (the one approximation)

When one member learns, the faction learns: standing is per faction. The doctrine says "same faction does not imply instant shared awareness" (`docs/STEALTH_DETECTION_AND_THREAT.md:100`). M7 honours it at the scale that matters: every M7 faction is **local**, and all of its members stand inside the 200 m Ashen Hollow square. The residual gap is the time Tavar takes to walk home, and M7 does not model it.

The seam is `LearnedFrom`. A later milestone can make a gate ask "has the member who knows been home yet?" without a schema change to the act. A faction with members in two settlements, a "remote cell" in the doctrine's sense, is out of M7 scope. The faction lint refuses nothing about member positions, so this is recorded in section 19.

### 5.4 Witness numbers (`config.factions`)

| Key | Value | Why this value |
|---|---|---|
| `witness.sight_m` | 30 | The keenest Phase-1 sight, the ash ember hound (`content/creatures/beast/ash_ember_hound.yaml:17`). A person notices an act no further off than a hunting beast notices the player. |
| `witness.fov_deg` | 140 | The grey wolf's cone (`wolf_grey.yaml:19`). Someone facing away does not see. |
| `witness.identify_m` | 15 | Beyond this, a witness knows it happened but not who did it. Tavar, following at 2.5 m (`content/config/companion.yaml`), always identifies. Sel at her survey table identifies nobody at the Foldscar, which is 110 m off. |

---

## 6. Standing (the PROGRESSION ladder)

### 6.1 Representation

- The stored value is `Points`, an `int` per faction. Zero is not stored (the relationship convention).
- Points are clamped to `[-limit, +limit]` with `limit = 1000`.
- The tier is **derived** at read time from `config.factions`, and never stored.

### 6.2 The ladder (`docs/PROGRESSION.md:432-434`)

| Tier (key) | Points | Width |
|---|---|---|
| Exalted (`exalted`) | >= 500 | to the limit |
| Allied (`allied`) | 300 .. 499 | 200 |
| Honoured (`honoured`) | 150 .. 299 | 150 |
| Trusted (`trusted`) | 75 .. 149 | 75 |
| Accepted (`accepted`) | 25 .. 74 | 50 |
| Neutral (`neutral`) | -24 .. 24 | 49 |
| Wary (`wary`) | -74 .. -25 | 50 |
| Disliked (`disliked`) | -149 .. -75 | 75 |
| Despised (`despised`) | -299 .. -150 | 150 |
| Outcast (`outcast`) | -499 .. -300 | 200 |
| Anathema (`anathema`) | <= -500 | to the limit |

- **First tier at 25.** Ashen Hollow has two relevant acts, so one significant act (30 points) must cross one tier. That makes both gates reachable in the prototype without grinding.
- **Widening steps (50, 75, 150, 200).** They leave headroom for M9's larger content without re-basing saved points.
- **Limit of 1000**, twice Exalted's floor, keeps accumulated history without letting it buy anything more.
- **No tier name implies an attack order**, the ratified naming rule (`docs/PROGRESSION_AXIS_RECONCILIATION.md:185`).

### 6.3 Application

```csharp
// src/Domain/Factions/Factions.cs
public static class FactionRules
{
    public static StandingTier TierOf(int points, TierLadder ladder);
    public static int DeltaFor(FactionDefinition faction, ActKind kind, string subject);   // 0 when no row
    /// Learn one act for one faction; returns the new ledger and what changed (for the event).
    public static FactionLedger Learn(FactionLedger ledger, long actSeq, FactionDefinition faction, bool actorKnown,
        KnowledgeSource via, string? from, long tick, int limit, out Learned learned);
}
public sealed record Learned(bool Changed, int Delta, int PointsBefore, int PointsAfter);
```

`Learn` works through these cases:

| Faction's existing row for the act | New knowledge | Result |
|---|---|---|
| none | actor unknown | add the row with `Delta = 0` |
| none | actor known | add the row with `Delta = DeltaFor(...)`; apply `Clamp(points + delta)` |
| actor unknown | actor known | upgrade: set `ActorKnown`, `Via`, `LearnedFrom` and `Tick` to the new source; apply the delta once; store it in `Delta` |
| actor known | anything | nothing changes (idempotent) |

Lint allows at most one row per `(faction, act kind, subject)`.

### 6.4 Repeated acts

- **The same act instance:** at most one application per faction, whatever the number of witnesses and reports.
- **Distinct instances of the same kind and subject** each apply. PROGRESSION sanctions farming a faction (`docs/PROGRESSION.md:123`), and E-7 forbids only gold, not deeds.
- **No caps, no diminishing returns** in M7. Shipped content reacts only to single-instance subjects. Caps are M9 tuning.

Decay is deferred (scope ruling §4).

---

## 7. The factions and the proof case (brief items 4 and 5)

Names are working names (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:265`). They are plain English descriptions, not cultural names. The IDs follow the existing `world.hollow.*` and `spawn.hollow.*` scoping. Avoid the ID `faction.hollow.wardens`, which `tests/Content.Tests/QuestContentTests.cs:120` uses as a deliberately missing reference.

### 7.1 `faction.hollow.waystation`, "the Waystation"

| | |
|---|---|
| Purpose | The people who keep Ashen Hollow's road stop running: Renn Vale's stewardship and Kera Voss's forge. |
| Values and interests | The stop is safe to reach; the forge has iron; the road keeps its travellers. |
| Why here | It **is** Ashen Hollow's only community ("I keep this waystation", `content/dialogue/ashen_hollow/renn_vale.yaml:11`; "Kera needs iron", `:53`). |
| Acts it cares about in M7 | Blackvein made safe again: the Animated Armour killed, +30. |
| Relation to the Survey | Attitude +10: it houses the Survey and tolerates it. |
| What it proves | Report-only knowledge (its members never leave the waystation); the service gate; that an act with no row is known but ignored (the heart). |
| Members | `npc.ashen_hollow.renn_vale`, `npc.ashen_hollow.kera_voss` |

### 7.2 `faction.hollow.survey`, "the Survey"

| | |
|---|---|
| Purpose | Sel Arien's survey of the Foldscar and the old workings around it, and the guide it hires. |
| Values and interests | An accurate record, and the old things that make one ("half of what's on it is older than the waystation", `sel_arien.yaml:11`); their own people. |
| Why here | The Foldscar is its subject; Tavar is its guide ("He guides my surveys", `sel_arien.yaml:93`). |
| Acts it cares about in M7 | The Foldscar's heart steadied, +30. The Animated Armour destroyed, -30: an old working that still kept its post, broken before anyone could ask it why. |
| Relation to the Waystation | Attitude +10 |
| What it proves | Witness knowledge through a travelling member (Tavar, as companion); the dialogue gate; a gate that opens and then closes. |
| Members | `npc.ashen_hollow.sel_arien`, `npc.ashen_hollow.tavar_orr` |

### 7.3 A third concept, considered and rejected for M7: "the stone-turners"

The unknown party who turned the Quiet Stones out of line ("Somebody turned them out of line before we ever came out here. Not me.", `tavar_orr.yaml`) would be a faction that disapproves of steadying the heart and has no members present. It would show that a faction without knowledge never updates. The two factions already show that: kill the armour alone and tell no one, and nothing moves.

It proves nothing mechanical the pair cannot, and it would invent lore. It is the natural M9 content candidate.

Peoples (Veth, Kal, Siann, Orenth) are **not** factions. Culture and polity are social; race is biology (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:623-631`). Neither M7 faction is keyed to a people.

### 7.4 The proof case

**One act: the player kills `creature.construct.animated_armour` in Blackvein Cut.**
- The Waystation reads it as the quarry made a quarry again: **+30**.
- The Survey reads it as an unrepeatable old working destroyed: **-30**.

Neither moves until it knows:

| How the player plays it | Waystation | Survey |
|---|---|---|
| Kills it alone, tells no one | 0 (unaware) | 0 (unaware) |
| Tells Kera ("The armour at the seam won't stand guard any more.") | +30 Accepted (reported) | 0 (unaware) |
| Kills it with Tavar following (he sees, within 15 m) | 0 (unaware) | -30 (witnessed) |
| Tells Sel ("The armour in Blackvein Cut is down.") | - | -30 (reported) |
| Tavar saw it, and the player then tells Sel | - | -30 once (the report is idempotent) |

This is the ROADMAP exit, "the same act moves two factions in opposite directions" (`docs/ROADMAP.md:284`), made legible in play: two people, told the same thing, react oppositely, and each reaction is visible in what they will do for you.

### 7.5 What a player experiences (the M7 acceptance walk)

1. Steady the Foldscar's heart. Tavar sees it (he faces the heart from 10 m), or he is told when he greets you. **Survey +30, Accepted.** At the waystation, Sel now offers "What else is in your notes?" (the dialogue gate).
2. Take Tavar (following) down to Blackvein and kill the armour. **Survey -30, now 0, Neutral.** Sel's notes reply is gone.
3. Tell Kera. **Waystation +30, Accepted.** Kera's wares now include the hide vest and cap (the service gate). A direct `BuyCommand` for either was refused until now.
4. Kera's personal respect and trust are unchanged throughout. Relationships move only by their own events.
5. Save, quit, load: standings, the act log and the knowledge rows are identical, the gates are in the same state, and the save/relaunch comparison shows 0 differences.

---

## 8. The two gates, and the M7 rule for faction reads

### 8.1 Dialogue gate: `faction_standing`

```csharp
// src/Domain/Social/Social.cs
public sealed record FactionStandingCondition(string FactionId, StandingTier Min, StandingTier Max) : DialogueCondition;
// IDialogueFacts: StandingTier StandingTier(string factionId);
// DialogueRules.Holds: FactionStandingCondition f => facts.StandingTier(f.FactionId) is var t && t >= f.Min && t <= f.Max
```

- **Content:** `{ kind: faction_standing, faction_ref: <id>, min_tier: accepted }`. `min_tier` defaults to `anathema`, `max_tier` to `exalted`; lint requires `min <= max`; `not:` is not accepted.
- **Hooks:**
  - `SocialContent.Conditions` and `Condition(...)` (`src/Content/SocialContent.cs:26-27`, `:159-184`);
  - `IDialogueFacts` in `DialogueSystem` and `SpeakerFacts` (`src/World/Runtime/Social.cs:397-459`), both reading `State.Factions` with `Setup.Factions.Ladder`;
  - `QuestDebugger.DescribeCondition`, which gains an arm (`src/World/Runtime/QuestDebugger.cs:372-387`): "needs the Survey at accepted or better; it is neutral (0)".
- **The one M7 use:** Sel's `notes` reply (section 15).

### 8.2 Service gate: the trade service at ware granularity

The faction content declares a gate:

```yaml
service_gates:
  - { service: trade, merchant_ref: merchant.ashen_hollow.kera_voss,
      item_refs: [item.armor.hide_vest, item.armor.hide_cap], min_tier: accepted }
```

- **Predicate:** a ware whose item appears in a gate for this merchant is sold only while `TierOf(standing[gate.faction]) >= gate.min_tier`.
- **Whole-service gates:** an empty or absent `item_refs` gates the whole trade, both buying and selling. This is the doctrine's "a low standing makes a faction refuse service" (`docs/PROGRESSION.md:452`) as data, with no extra code. Ashen Hollow content does not use it.

Hook, all inside `TradeSystem`:

```csharp
// src/World/Runtime/Social.cs, TradeSystem
private string? Shut(NpcState npc, string merchantId, string? itemId)
{
    var f = _context.Setup.Factions;
    foreach (var gate in f.GatesFor(merchantId))                     // content order, deterministic
        if ((gate.ItemIds.IsEmpty || (itemId is not null && gate.ItemIds.Contains(itemId)))
            && FactionRules.TierOf(State.StandingOf(gate.FactionId), f.Ladder) < gate.MinTier)
            return gate.ItemIds.IsEmpty ? $"{npc.Definition.Name} will not trade with you"
                                        : $"{npc.Definition.Name} will not sell you that";
    return null;
}
// Handle(BuyCommand), after the ware is found (:488-492):   if (Shut(npc, merchant.Id, ware.DefId) is { } s) return s;
// Handle(SellCommand), after Trader(...) (:504-505):        if (Shut(npc, merchant.Id, null) is { } s) return s;   // whole-service gates only
// View(npcId) (:526-528): .Where(i => Shut(npc, merchant.Id, i.DefId) is null)   // gated wares are not listed
```

**Why it cannot be bypassed:**
- The check is in the authority, in the handler of the only command that moves wares (`Items.cs:456-459` refuses any other move of a trader's wares).
- It is not in dialogue: `open_service` is only an event (`Social.cs:373-375`), and the simulation accepts `BuyCommand` with no conversation.
- A `BuyCommand` naming the vest's ware ref directly is refused with a reason, which `CommandRejected` then publishes.
- Presentation needs no change: `Simulation.Wares` already feeds the panel and now omits the gated rows.

**Why a ware gate rather than a whole-trade floor.** Kera's whole trade must stay open at Neutral, or Phase-1 play breaks. A floor below Neutral is unreachable in Ashen Hollow: no Waystation-negative act exists that a Waystation member could witness, because Renn and Kera stand inside walls (the lines to the chest and the road cross the lodge and forge walls). Stock gating is PROGRESSION's own example of what reputation gates ("prices and stock", `docs/PROGRESSION.md:440`).

**Reachability.** The hide cap was kept in stock for reachability (PROTOTYPE C18, `content/merchants/ashen_hollow/kera_voss.yaml:11`). It stays reachable, now behind a reachable standing.

### 8.3 `act_done` and `report_act`

```csharp
public sealed record ActDoneCondition(ActKind Act, string Subject) : DialogueCondition;      // IDialogueFacts.ActDone(act, subject)
public sealed record ReportActConsequence(ActKind Act, string Subject) : DialogueConsequence;
// DialogueSystem.Apply: case ReportActConsequence r: _context.Dispatch(new ReportAct(r.Act, r.Subject, open.NpcId)); break;
```

- **Content:**
  - `{ kind: act_done, act: killed, creature_ref: <creature> }`
  - `{ command: report_act, act: set_switch, switch: <switch key> }`
- **Subject field by kind:**
  - `creature_ref` for `killed`, resolved by the generic walker;
  - `switch` for `set_switch`, checked by `FactionContent` against the region layout's switch keys;
  - building's piece `*_ref` for `placed`.
- **Behaviour:** `report_act` is a no-op when nothing matches, so it may be added to existing replies without changing when they are offered.

### 8.4 The M7 rule for faction reads (the existing leaks)

Phase-1 dialogue reads global player state (research `social_quests_code.md` §7). M7 does not fix Phase 1. It adds four lint-enforced rules for every faction read:

- **F1.** A `faction_standing` condition may name only the faction of the conversation's speaker. Lint: every participant's `faction_ref` equals the condition's `faction_ref`. What an NPC's own people think of you is theirs to know, and it only ever moved through what they learned.
- **F2.** No condition reads another faction's standing, and no condition reads any knowledge row. (M7 has no `faction_knows` condition.)
- **F3.** `act_done` reads ground truth, so it may appear only on a reply that also carries `report_act` for the same act and subject. Ground truth then gates only **the player's own decision to tell**, which the player knows. It never gates what an NPC says unprompted.
- **F4.** Nothing writes standing except `FactionSystem` on a known act.
  - The dialogue `add_reputation` consequence and the quest `reputation` reward stay unbuilt, with their refusal text changed to "reputation moves only through acts a faction learns of (M7)".
  - `faction_reputation` and `faction_state` stay unbuilt. Their `NotBuilt` reason changes from "factions (M7)" to "faction quest content (M9)" (`src/Domain/Quests/Quests.cs:154-155`).

**Grandfathered Phase-1 reads** (cross-dialogue `visited`, `relationship` on any `npc_ref`, `quest_state`, `has_item`) are unchanged. M7 content adds no new cross-NPC read. The recommendation for M9 is to hold `relationship` to the speaker as F1 does.

---

## 9. Relations, membership, and where faction state lives

- **Static relations:**
  - `relations: [{ faction_ref, attitude }]`, with `attitude` an int in [-100, 100] (the relationship scale, a different quantity);
  - asymmetric, at most one row per target, never self;
  - the only reader in M7 is the debug view.
  - A test pins that an act moving one faction never moves another through relations.
  - DATA_MODEL's `[-1, 1]` float `attitude_default` and `enemy_of` ("hard hostility edges", `docs/DATA_MODEL.md:563`) are refused by name.
- **Membership:**
  - NPC `faction_ref`, single, optional. `NpcDefinition` gains `public string? FactionId { get; init; }` (`src/Domain/Social/Social.cs:10-16`), read by `BuildNpcs` (`src/Content/SocialContent.cs:90-93`).
  - Membership is **content, not save state**.
- **The PROTOTYPE A-2 gap** ("`faction_id` field exists on NPCs and is saved", `docs/PROTOTYPE.md:110`, `:264`, `:395`) is false in code. M7 corrects the text: membership is definition data. NPC records are not saved at all (`src/World/Runtime/Social.cs:98-104`), and static membership needs no save field. A membership **change** at run time (joining, defecting) would be instance state and is deferred.
- **Where faction-global state lives:** there is **no mutable faction-global state in M7**.
  - Standing and knowledge are about the player, so they live in the player record, like relationships (schema 10).
  - Relations and membership are content.
  - The first milestone with war state or faction control adds a world-global record, not cell flags: a faction fact tied to one arbitrary cell is wrong (research `content_registry.md` §0 item 10).

---

## 10. Personal relationship vs faction standing (brief item 6)

| | Personal relationship | Faction standing |
|---|---|---|
| Holder | one NPC | one faction |
| Scale | 5 dimensions, int [-100, 100] | one int [-1000, 1000] plus a derived tier |
| Moves by | `ChangeRelationship` from dialogue or quest reward (`Social.cs:370-372`, `Quests.cs:165-168`) | `FactionSystem` on a known act only |
| Slice | `StateSlice.Relationships`, owner `RelationshipSystem` | `StateSlice.Factions`, owner `FactionSystem` |
| Read by | the `relationship` condition, the `relationship_value` objective | the `faction_standing` condition, the trade gate, views |

**They do not interact in M7.** No code converts one into the other. Kera's respect (+10 for fine work, `kera_voss.yaml:52`) says nothing about the Waystation, and the Waystation's acceptance says nothing about Kera's trust. A test pins both directions.

Content may move both from one reply, as two explicit consequences. M7's new replies deliberately do not, so the separation is visible in the acceptance transcript.

---

## 11. Hostility (brief item 7)

M7 requires **no** faction hostility and no combat against NPCs. The player cannot strike an NPC (`src/World/Runtime/Combat.cs:496`, `:554`; NPCs have no health, `src/World/Runtime/Social.cs:88`). Creatures carry no faction, and their hostility is their perception-driven mind (`src/World/Runtime/Creatures.cs:36`). M7 changes none of that.

| Concept (ruling 3) | M7 state | Where |
|---|---|---|
| Personal relationship, trust, fear, grudge | exists (Phase 1), untouched | `Relationships` |
| Reputation / standing | built | `FactionStanding` |
| Legal status | **not built**; nothing writes it | - |
| Faction relationship | static content | `FactionDefinition.Relations` |
| War state | not built | - |
| Known identity | two rungs per knowledge row | `FactionKnowledge.ActorKnown` |
| Tactical threat | creature mind (Phase 1) | `CreatureRecord` |
| Attack legality | not built; no `CanAttemptAttack` seam is touched | - |

**Guard:** a new architecture test, `HostilityCode_NeverReadsStanding`. It scans `src/World/Runtime/Combat.cs`, `Creatures.cs`, `Companions.cs` and `Magic.cs` and fails if they contain the identifiers `Faction`, `Standing` or `StandingTier`. The only exception is `Creatures.cs`'s `RecordAct` dispatch, which is written as `new RecordAct(`: the scan forbids reads (`StandingOf`, `Setup.Factions`), not the dispatch.

---

## 12. Building's faction-relevant act

**How:** building's placement handler dispatches `RecordAct(ActKind.Placed, pieceDefId, player.XMm, player.ZMm)` after a committed placement. That is one line, and it goes through the same relevance filter, so it costs nothing while no content reacts.

**Whether (for Ashen Hollow content): no reaction row in M7.** No Waystation member can see the build area: both stand inside walls. A confession reply for building would be content padding. The seam is proven by one fixture-table row (a fixture faction reacting to `placed`).

**Contract:** building registers its piece kind's `*_ref` suffix, and faction reaction rows name pieces with it. Placed walls and closed placed doors join `SystemContext.SightWalls()`, so they hide acts from witnesses exactly as they hide the player from creatures.

---

## 13. Persistence (brief item 8)

### 13.1 Where

Everything lives in `player.msgpack`, as the relationships pattern did (schema 10). There is no world-delta change, no new section file (so no integrity-root trap, research `persistence.md` §0 item 2), and no `DeltaSnapshot` property (so no hand-listed-property pitfall at `SaveLoader.cs:356-358` or `BaselineTransitions.cs:112-117`).

### 13.2 Domain record

```csharp
// src/Domain/Factions/Factions.cs
public sealed record FactionLedger(long NextActSeq, ImmutableArray<ActRecord> Acts, ImmutableArray<FactionStanding> Standing)
{
    public static FactionLedger Empty { get; } = new(1, ImmutableArray<ActRecord>.Empty, ImmutableArray<FactionStanding>.Empty);
}
public sealed record FactionStanding(string FactionId, int Points);   // Points != 0
```

`PlayerRecord` changes (`src/World/PlayerState.cs`):
- `public FactionLedger Factions { get; init; } = FactionLedger.Empty;` validates on init:
  - `NextActSeq >= 1`;
  - acts sorted by `Seq`, unique, `1 <= Seq < NextActSeq`, kind defined, subject non-empty, cell key parseable;
  - `Known` sorted by `FactionId`, one row per faction, faction IDs valid `faction.*`, `Delta == 0` when `!ActorKnown`;
  - standing sorted by `FactionId`, unique, `Points != 0`.
- Every `With*` copy carries `{ Posture = Posture, Factions = Factions }` (`:222-253`).
- `Digest` tag `unnamed.player/v9` becomes `v10`, hashing the whole ledger after posture (`:328`, `:365`).

`Simulation.CaptureRecord` adds `Factions = _state.Factions` (`Simulation.cs:342-351`). `RuntimeState`'s constructor reads `player.Factions` (`RuntimeState.cs:97-115`).

### 13.3 DTO (`src/Persistence/SectionCodec.cs`)

```csharp
[Key("factions")] public FactionsDto? Factions { get; set; }   // PlayerDto. Required from schema 14. The 13 -> 14 step gives an empty ledger.

public sealed class FactionsDto  { [Key("next_act_seq")] long; [Key("acts")] ActDto[]; [Key("standing")] StandingDto[]; }
public sealed class ActDto       { seq:long, kind:string, subject:string, cell_key:string, x_mm:long, z_mm:long, tick:long, known:KnownDto[] }
public sealed class KnownDto     { faction_id:string, actor_known:bool, via:string, learned_from:string?, tick:long, delta:int }
public sealed class StandingDto  { faction_id:string, points:int }
```

- Enum values are keys (`killed|set_switch|placed`, `witnessed|reported`), never ordinals.
- Decode throws `FormatException("player.msgpack has no factions (required from schema 14)")` when the field is null, and validates keys and ranges (the posture pattern, `SectionCodec.cs:399-416`).

### 13.4 Migration (one step shared with building)

The single `SchemaV13ToV14` step:
- freezes the current `PlayerDto` as `Sections/SchemaV13.cs` `V13.Player`, and repoints `SchemaV12ToV13` to write it (`Migrations.cs:722`);
- reads `V13.Player` and writes the current `PlayerDto` with `Factions = { next_act_seq: 1, acts: [], standing: [] }`.

Building's entity or section change goes in the same step. The summary starts `"schema 13 -> 14:"`.

**Pre-M7 acts are not reconstructed.** A save in which the heart was already steadied loads with the Survey at 0 and an empty log. Reconstructing acts from world state would be a guess, and migrations never resolve content (`Migrations.cs:49-53`). The rule is "factions arrived with M7; nothing before was witnessed".

### 13.5 Definition-ID pass (`SaveLoader.ResolveDefinitions`, beside `:316-354`)

| Stored ID | Removed | Merged by a rename |
|---|---|---|
| Standing `faction_id` | the row is dropped; the loss is reported | points summed, then clamped at 1000 (one faction's opinion, split by a rename) |
| Knowledge `faction_id` | the row is dropped | first row by `FactionId` kept |
| Act `subject` for `killed` / `placed` | the act is dropped with its knowledge; the loss is reported | |
| Act `subject` for `set_switch` | kept verbatim: layout site keys are not definitions (as container keys) | |
| `learned_from` NPC ID | set to null; the knowledge survives | |

The ledger is rebuilt as `player with { Factions = ... }` at `:357`.

### 13.6 Evidence obligations

**Fixtures:**
- `M2Fixtures.Historical.Player()` gains the proof state: act #1 `killed` of a fixture creature, known with the actor identified by fixture factions A (+30) and B (-30), and standing A +30, B -30. The v14 save fixture therefore **is** the reputation proof in stored form.
- The writer pack `content-0.1.7` adds `factions/fixture/*.yaml` and `config.factions`.
- The current pack renames fixture faction B through `_aliases.yaml`, which proves "the rename must reach the standing and knowledge records".

**Other obligations:**
- `CanonicalState.Render` renders the ledger.
- All `expected.json` files are regenerated and reviewed; older fixtures gain only the empty ledger.
- `StateDump` picks the ledger up automatically as a public `PlayerRecord` property.
- `StateDigest` covers it through `PlayerRecord.Digest`.

**Retirement (I-7).** The log holds at most `acts.log_capacity` (256) records. On overflow, evict the lowest-`Seq` act whose every reacting faction knows it with the actor identified. If none is settled, evict the lowest `Seq`. Shipped content never exceeds two.

---

## 14. Code plan (files, types, hooks)

| File | Change |
|---|---|
| `src/Domain/Factions/Factions.cs` (new) | `ActKind`, `ActKinds` (key parse), `StandingTier` (-5..+5) and keys, `TierLadder`, `Reaction(ActKind, string Subject, int Delta)`, `ServiceGate(string Service, string MerchantId, string FactionId, ImmutableArray<string> ItemIds, StandingTier MinTier)`, `FactionDefinition(string Id, string Name, ImmutableArray<Reaction> Reactions, ImmutableSortedDictionary<string,int> Relations, ImmutableArray<ServiceGate> ServiceGates)`, `WitnessRules`, `ActRecord`, `FactionKnowledge`, `KnowledgeSource`, `FactionStanding`, `FactionLedger`, `FactionRules` (`TierOf`, `DeltaFor`, `Learn`, `Witnesses`, `Compact`) |
| `src/Domain/Social/Social.cs` | `NpcDefinition.FactionId`; `FactionStandingCondition`, `ActDoneCondition`, `ReportActConsequence`; two `IDialogueFacts` members; two `Holds` arms |
| `src/World/Runtime/Factions.cs` (new) | `FactionSetup(Factions, Ladder, Witness, int Limit, int LogCapacity) { Empty; Relevant; GatesFor(merchantId) }`; `RecordAct`, `ReportAct`; `FactionSystem` (owns `StateSlice.Factions`; no `Tick`); events `ActRecorded(long Seq, string Kind, string Subject, string CellKey, long Tick)` and `FactionLearned(string FactionId, long ActSeq, bool ActorKnown, string Via, string? LearnedFrom, int Delta, int PointsAfter, string TierBefore, string TierAfter, long Tick)`; views `FactionView(Id, Name, Points, Tier, Relations)` and `ActView` |
| `src/World/Runtime/RuntimeState.cs` | `StateSlice.Factions` ("saved with the player, schema 14"); `Factions`; `StandingOf(id)`; `SetFactions(owner, ledger)` |
| `src/World/Runtime/Simulation.cs` | `SimulationSetup.Factions`; compose `_factions` after `_relationships` (`:132`); two `Dispatch` arms; `CaptureRecord`; get-only properties `Factions` and `FactionActs` (no new public method, so `ArchitectureTests.cs:114-122` is unchanged) |
| `src/World/Runtime/Systems.cs` | `SystemContext.SightWalls()`; `RecordAct` in `Work` after `:288` |
| `src/World/Runtime/Creatures.cs` | `RecordAct` beside `:717`; `Walls()` delegates to `SightWalls()` |
| `src/World/Runtime/Social.cs` | `Apply` gains a `report_act` arm; facts `StandingTier`/`ActDone` in `DialogueSystem` and `SpeakerFacts`; `TradeSystem.Shut` in Buy, Sell and View |
| `src/World/Runtime/QuestDebugger.cs` | `DescribeCondition` arms for the two conditions |
| `src/World/PlayerState.cs` | `Factions` init property, `With*` carry, digest v10 |
| `src/Content/FactionContent.cs` (new, lint `FAC001`) | `Validate` and `Build` (definitions, `config.factions`, witness rules, gates, relevance set) |
| `src/Content/SocialContent.cs` | NPC `faction_ref`; conditions and consequence parse and lists; rules F1 and F3 in `CrossCheck` |
| `src/Content/ContentLoader.cs` | call `FactionContent.Validate` after `SocialContent` (`:190`) |
| `src/Application/GameSession.cs` | `Factions = FactionContent.Build(loader)` in `Boot` (`:88-99`) |
| `src/Persistence/*` | section 13 |
| `src/Presentation/Main.cs` | subscribe to `FactionLearned` beside `:778`; debug panel (below) |

**`FactionSystem.Handle(RecordAct a, long tick)`**
1. If `(a.Kind, a.Subject)` is not in the relevant set, return null.
2. `seq = ledger.NextActSeq`. Build the `ActRecord` with an empty `Known`. Publish `ActRecorded`.
3. For each `(faction, npc, known)` in `Witnesses(...)` (ordinal by faction), call `Learn`. When anything changed, publish `FactionLearned`.
4. Compact to capacity; `SetFactions`; return null.

**`FactionSystem.Handle(ReportAct r, long tick)`**
1. `faction = Setup.Social.Npcs[r.SpeakerNpcId].FactionId`. If null, return null (lint prevents it).
2. For each act matching `(r.Kind, r.Subject)` by ascending `Seq`, call `Learn(actorKnown: true, via: Reported, from: speaker)`.
3. Publish `FactionLearned` for each change.

**Presentation (no new command, no radial):** a HUD log line on `FactionLearned` when `Delta != 0` ("The Survey: -30 (Accepted to Neutral), from Tavar Orr"); unknown-actor knowledge appears only in a debug panel (factions, points, tiers, relations, the act log). The wares panel needs nothing.

---

## 15. Content (YAML)

### 15.1 `content/config/factions.yaml`

```yaml
id: config.factions
kind: config
schema: 1
display_key: config.factions.name
tags: [config]
notes: Factions and reputation (M7; PROGRESSION.md §10). One ladder for every faction. A standing is whole points; its tier is derived at read time and never stored. Witnessing is sight only - there is no rumour and no telling between factions.
standing:
  limit: 1000          # points are clamped to [-limit, +limit]: twice Exalted's floor, so history is kept but buys nothing more
  accepted: 25         # a positive tier's lowest value ...
  trusted: 75
  honoured: 150
  allied: 300
  exalted: 500
  wary: -25            # ... and a negative tier's highest; Neutral is everything strictly between -25 and +25
  disliked: -75
  despised: -150
  outcast: -300
  anathema: -500
witness:
  sight_m: 30          # no further than the keenest Phase-1 creature sees (the ash ember hound)
  fov_deg: 140         # the grey wolf's cone: someone facing away does not see
  identify_m: 15       # inside this a witness knows who; beyond it, only that it happened
acts:
  log_capacity: 256    # relevant acts kept; the oldest settled act goes first
```

Lint:
- required iff any faction exists (the `config.companion` precedent, `SocialContent.cs:42-43`);
- positive thresholds strictly increasing, and `0 < accepted`, `exalted <= limit`;
- negative thresholds mirror that, strictly decreasing;
- `0 < identify_m <= sight_m`, `0 < fov_deg <= 360`, `log_capacity >= 16`.

### 15.2 `content/factions/hollow/waystation.yaml`

```yaml
id: faction.hollow.waystation
kind: faction
schema: 1
display_key: faction.hollow.waystation.name
tags: [faction]
name: The Waystation      # working name (content bible §9)
notes: The people who keep Ashen Hollow's road stop running - Renn Vale's stewardship and Kera Voss's forge. Wants the stop safe and supplied. Members are the NPCs whose faction_ref names it. An M7 working faction, not canon.
reactions:
  - { act: killed, creature_ref: creature.construct.animated_armour, delta: 30 }   # Blackvein is only a quarry again, and the forge gets its iron
relations:
  - { faction_ref: faction.hollow.survey, attitude: 10 }                           # attitude only: M7 reads it nowhere but the debug view
service_gates:
  - { service: trade, merchant_ref: merchant.ashen_hollow.kera_voss, item_refs: [item.armor.hide_vest, item.armor.hide_cap], min_tier: accepted }
```

### 15.3 `content/factions/hollow/survey.yaml`

```yaml
id: faction.hollow.survey
kind: faction
schema: 1
display_key: faction.hollow.survey.name
tags: [faction]
name: The Survey          # working name (content bible §9)
notes: Sel Arien's survey of the Foldscar and the old workings around it, and the guide it hires. Values an accurate record and the old things that make one. An M7 working faction, not canon.
reactions:
  - { act: set_switch, switch: switch.foldscar_heart, delta: 30 }                  # the fold steadied, and their guide walks out of it
  - { act: killed, creature_ref: creature.construct.animated_armour, delta: -30 }  # an old working that still kept its post, broken before anyone could ask it why
relations:
  - { faction_ref: faction.hollow.waystation, attitude: 10 }
```

**Faction lint (`FAC001`):**
- `name` is required;
- `act` is one of the three kinds, and the subject field matches the kind;
- `delta` is a non-zero int with `|delta| <= 200`;
- one row per `(act, subject)`;
- relations name another existing faction, with `attitude` in [-100, 100];
- a gate's `service` is built (`trade`); its merchant exists; **every NPC trading with that merchant is a member of this faction**; every `item_refs` item is in that merchant's stock; `min_tier` is a ladder key;
- the DATA_MODEL §4.13 fields are refused by name, each with its reason:

| Refused field | Reason given |
|---|---|
| `members` | "membership is the NPC's faction_ref" |
| `reputation_tiers`, `player_start_reputation` | "one ladder, config.factions; every standing starts at 0" |
| `laws` | "crime is not built (deferred beyond M7)" |
| `enemy_of`, `attitude_default` | "relations are attitude only; use relations" |
| `territory`, `services_gated`, `joinable`, `join_requirements` | "not built in M7" |

### 15.4 NPC additions (one line each)

```yaml
# renn_vale.yaml, kera_voss.yaml
faction_ref: faction.hollow.waystation
# sel_arien.yaml, tavar_orr.yaml
faction_ref: faction.hollow.survey
```

### 15.5 Dialogue additions

The text is draft, for owner tone review (the owner protects the M6 tone). No existing line's text or conditions change.

Kera, `again` (a new reply, and a new node):

```yaml
      - id: armour
        text: "The armour at the seam won't stand guard any more."
        conditions:
          - { kind: act_done, act: killed, creature_ref: creature.construct.animated_armour }
          - { kind: visited, node: armour_down, not: true }
        consequences:
          - { command: report_act, act: killed, creature_ref: creature.construct.animated_armour }
        next: armour_down
  armour_down:
    text: "Then the seam's only a seam again. Good. Ask me for the vest and the cap now - I keep those back for people the waystation owes."
    once: true
    next_if_exhausted: again
    choices:
      - { id: back, text: "I'll look.", next: again }
```

Sel, `again` (two new replies, and two new nodes):

```yaml
      - id: notes
        text: "What else is in your notes?"
        conditions: [{ kind: faction_standing, faction_ref: faction.hollow.survey, min_tier: accepted }]
        next: notes
      - id: armour
        text: "The armour in Blackvein Cut is down."
        conditions:
          - { kind: act_done, act: killed, creature_ref: creature.construct.animated_armour }
          - { kind: visited, node: armour_down, not: true }
        consequences:
          - { command: report_act, act: killed, creature_ref: creature.construct.animated_armour }
        next: armour_down
  notes:
    text: "The parts I trust. The stones were turned by hand, not long before we came - the scoring was fresh. Whoever did it knew the ring. It's more than I've written down."
    choices:
      - { id: back, text: "I'll keep it to myself.", next: again }
  armour_down:
    text: "Down. It kept that post longer than the waystation has stood. Whoever set it there had a reason, and now nobody can ask it."
    once: true
    next_if_exhausted: again
    choices:
      - { id: back, text: "It was in the way.", next: again }
```

Tavar, `greet`: a consequence is added to both existing replies, `sent` and `found`. His greeting already presumes he saw the stones turned ("do you just go about turning stones?").

```yaml
          - { command: report_act, act: set_switch, switch: switch.foldscar_heart }
```

Migrated saves are safe. `report_act` is a no-op without a matching act, and no existing reply gains a condition, so Quest 2's start and completion are untouched for schema-13 saves.

---

## 16. Tests (brief item 9) and the reputation fixture table

### 16.1 The table (ROADMAP proof)

**What it is:** `docs/M7_REPUTATION_TABLE.md`, generated like `docs/M3D_BEHAVIOUR_MATRIX.md`:
- the generator is `tests/Application.Tests/ReputationTableTests.cs`;
- it fails when the file differs from what the build produces;
- regenerate with `UNNAMED_WRITE_REPUTATION_TABLE=1 dotnet test --filter ReputationTable`.

**How it runs:** each row is a headless scenario on a hand-built `SimulationSetup` (the `CreatureTests`/`CombatTests` pattern): fixture factions A and B with members at authored sites and facings, one wall, a 1-HP creature `creature.fixture.old_guard`, one switch and a `placed` subject, driven only through public commands, with the shipped ladder numbers.

A second section renders the **Ashen Hollow reaction tables from the game's own content** (factions, rows, gates, members, relations), as the behaviour matrix renders archetypes from content.

**Format (the first rows):**

| # | Act (kind, subject) | Seq / tick / cell | Members in sight (faction: npc, m, identified) | Reports | A: via / delta / points / tier | B: via / delta / points / tier | Proves |
|---|---|---|---|---|---|---|---|
| 1 | killed old_guard | 1 / 40 / r_0_0:c_00_00 | A: warden 8.0 yes; B: delver 11.2 yes | - | witnessed / +30 / 30 / accepted | witnessed / -30 / -30 / wary | **same act, opposite directions (ROADMAP exit)** |
| 2 | killed old_guard | 1 / 40 / ... | none (both face away) | - | - / 0 / 0 / neutral | - / 0 / 0 / neutral | no knowledge, no change |
| 3 | killed old_guard | 1 / 40 / ... | B: delver 22.0 no | - | - / 0 / 0 / neutral | witnessed, actor unknown / 0 / 0 / neutral | unknown actor moves nothing |
| 4 | row 3, then tell delver | 1 / 40 / ... | B: delver 22.0 no | B at tick 120 | - | reported (upgraded) / -30 / -30 / wary | report upgrades once |
| 5 | row 1, then tell both | 1 / 40 / ... | A, B yes | A, B | unchanged | unchanged | idempotent report |
| 6 | killed old_guard behind the wall | 1 / 40 / ... | A: warden 6.0, wall between | - | - / 0 | - / 0 | walls hide acts |
| 7 | set_switch fixture switch | 1 / 12 / ... | A, B yes | - | witnessed / 0 (no row) / 0 | witnessed / +30 / 30 / accepted | known but no row: no change |
| 8 | killed x2 (two guards) | 1, 2 | A yes | - | +30, +30 / 60 / accepted | - | distinct acts each apply |
| 9 | row 1 with a save and load between the act and a report | 1 / 40 / ... | A yes | B after the load | 30 | -30 | continuity across a save |
| 10 | row 1 with member A a downed companion | 1 / 40 / ... | A: downed, none | - | 0 | -30 | the downed do not witness |
| 11 | placed fixture piece | 1 / 60 / ... | A yes | - | witnessed / +10 / 10 / neutral | - | building's act reaches factions |

### 16.2 Named tests

**Domain.Tests (pure `FactionRules`):** `TierOf_FollowsTheLadder_AtEveryBoundary`, `Learn_AppliesARowOnce_PerFactionPerAct`, `Learn_UnknownActor_KnowsButMovesNothing`, `Learn_AReportUpgradesUnknownOnce`, `Learn_WithoutARow_KnowsButMovesNothing`, `Learn_ClampsAtTheLimit`, `Witnesses_KnownBeatsUnknown_ThenNearer_ThenLowerNpcId`, `Witnesses_RangeFacingAndWallsHideTheAct`, `Compact_EvictsTheOldestSettledActFirst`.

**Application.Tests:**
- `ReputationTableTests`: the table above; row 1 is the ROADMAP exit.
- `AFactionThatNeitherSawNorWasTold_DoesNotUpdate`.
- `FactionState_ContinuesAcrossASaveAndLoad` (`StateDump.Compare`, 0 differences) and `SaveThenContinue_EqualsContinue_WithFactions` (`StateDigest`).
- `FactionActs_ReplayFromTheCommandLog_EndIdentical` (the pattern of `DeterminismAndViewTests.cs:48-77`) and `TwoFreshRuns_ProduceTheSameLedger` (replayable `StateDump`: sequence numbers, not ULIDs).
- `PersonalRelationships_AreUntouchedByStanding_AndStandingByRelationships`, `FactionRelations_NeverMoveStanding`, `ACompanionsKill_IsNotThePlayersAct`.
- `TheGatedWare_IsNotListed_AndABuyCommandForItIsRefused_UntilAccepted`, `AWholeTradeGate_RefusesBuyAndSellBelowItsTier`, `TheStandingCondition_OpensAndClosesAReply`, `ActDone_OffersTheReportOnlyAfterTheAct`.
- `AxisIndependence_ReputationByLevel_2x2` and `AxisIndependence_ReputationBySkill_2x2`. These are owed "at M7" (`docs/PROGRESSION_AXIS_RECONCILIATION.md:246`). The fixture reaches each cell through sanctioned vectors:
  - high standing at level 1: steady a switch, which awards no XP;
  - high level at neutral standing: kill creatures no faction reacts to;
  - it then asserts that no axis's currency moved another.
- `AshenHollow_ArmourMovesTheWaystationUpAndTheSurveyDown`, on the game's content: steady the heart (the `FoldscarTests` walk), greet Tavar, and assert Sel's `notes` is offered. Then kill the armour and report to Kera, and assert that the vest is buyable. Then report to Sel, and assert that `notes` is gone and Kera's respect and trust are unchanged.

**Content.Tests:** `LoadAll_Loads_Yaml_Files` gains `config.factions`, `faction.hollow.survey`, `faction.hollow.waystation` (`tests/Content.Tests/ValidationTests.cs:508-547`); `FactionContent_RefusesDataModelFieldsItDoesNotBuild`; `AStandingCondition_NamesTheSpeakersOwnFaction`; `ActDone_OnlyBesideAReportOfTheSameAct`; `AServiceGate_BelongsToTheTradersFaction`; `NoContent_TradesCurrencyForStanding` (the E-7 guard: reaction kinds are the closed three, no `reputation` reward, no `add_reputation` consequence).

**Persistence.Tests:** `Schema13To14_GivesAnEmptyLedger`, `ASchema14PlayerWithoutFactions_IsCorrupt_NotDefaulted`, `TheFactionLedger_GoesThroughTheDefinitionPass_RenamesAndRemovals`, the v14 fixture and regenerated expectations, and the step lists in `MigrationTests.cs` (research `persistence.md` §3.3 G).

**Architecture.Tests:** `HostilityCode_NeverReadsStanding` (section 11).

---

## 17. Documents M7 reconciles (faction side)

| Document | Change |
|---|---|
| `docs/ROADMAP.md` M7 | "Crime/bounty records and pardon state" is deferred (scope item 4). The reputation proof is `docs/M7_REPUTATION_TABLE.md`. "Territory gating" is deferred. |
| `docs/SYSTEMS.md` S-27 | An M7 reconciliation paragraph. It owns the act log, faction knowledge and standing. There are no crime records and no transient witness set (witnessing is synchronous at the act). |
| `docs/DATA_MODEL.md` §4.13 | The built subset and the refused fields, with reasons; membership is the NPC's `faction_ref`. |
| `docs/PROGRESSION.md` §10 | The ladder's numbers live in `config.factions`; tiers are derived; decay is deferred. |
| `docs/PROTOTYPE.md` A-2 | Corrected: membership is content and is not saved. |
| `docs/PERSISTENCE.md` §5.1 / §6.2 | A schema-14 paragraph; chain rows 11 -> 12, 12 -> 13 and 13 -> 14. |
| `docs/INDEX.md` (CRIME doc row) | "M7 reconciled: act records and faction knowledge only; crime is Phase 3." |

`docs/VERTICAL_SLICE.md`'s "Hostile" tier and "puts town guards on you" are M9's to reconcile. M7 notes them as conflicting with PROGRESSION §10 and SYSTEMS S-27.

---

## 18. Deferred beyond M7 (explicit non-goals)

- **Crime and law:** crime, legal status, bounty, pardon, jurisdiction, guards, witnesses as testimony, evidence, disguise, and ownership of authored containers (scope item 4).
- **Knowledge richness:** rumour, knowledge travelling between factions or between NPCs, in-flight reports, per-member memory, lying or bribed witnesses, and suspicion.
- **Standing and factions:** war state, faction control, hostility from any faction quantity, decay, caps and diminishing returns, joining or leaving factions, multi-membership, and tag-based reaction rows.
- **Quests and content:** quest objectives and rewards that touch reputation (M9 content), territory gating, and a third faction.
- **The companion question:** whether a companion reports on the player (companion loyalty and concealment).

I raise no challenge to the scope rulings. I rely on scope item 10's "one service gate" and pick its ware-granular form; section 8.2 gives the reason.

---

## 19. Owner question, open issues, contracts (summary)

**Owner question (for slot 5 of the scope list).** Approve the working pair:
- the Waystation (Renn, Kera) and the Survey (Sel, Tavar);
- the Blackvein Animated Armour as the act they read oppositely;
- Tavar, as a Survey member, telling his faction what he sees while he follows the player.

Default: approved as written.

**Open issues:**
1. **Companion kills are not the player's acts** (the M6 rule, mirrored). If Tavar lands the killing blow on the armour, no act is recorded and it cannot be reported. Most players meet the armour in Quest 1, before Tavar is free. Revisit party attribution in M9.
2. **Sharing within a faction is immediate.** This is acceptable only because every M7 faction is local. `LearnedFrom` is the seam for delay.
3. **Witnessing uses `Perception.Sees`** (`Math.Sin/Cos/Sqrt`). It is same-machine deterministic, like creature perception.
4. **Pre-M7 saves start with an empty log and neutral standing.** A migrated player who already freed Tavar never gains the Survey's +30 from it.
5. **The act position is the actor's body.** Seeing an effect without the actor (the evidence layer) is not modelled.
6. **The new dialogue text is placeholder** for owner tone review. Sel's `notes` line hints at the stone-turners (a lore hook the owner may reject).
7. **The acts the player can report are limited to the relevant set.** Content added later cannot see older acts.
8. **An act evicted from a full log can no longer be reported.** This is unreachable with shipped content: at most two acts against a capacity of 256.
