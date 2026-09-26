# VERTICAL_SLICE.md — Phase 2 Vertical Slice

**Project:** Otherreach (codename UNNAMED) — full-body third-person with seamless first-person zoom, solo-first, open-world fantasy RPG
**Phase:** 2 — Vertical Slice
**Status:** Specification for implementation
**Authority:** `PROJECT_CHARTER.md` (creative vision), `DECISIONS.md` (settled architecture, cited by ID), `PHASE_0.md` STEP 16 (required shape). `PROTOTYPE.md` remains the Phase 1 contract; **the slice does not renegotiate the prototype's architecture, it adds content and systems through it.**

> **The slice's job is not to be a game. The slice's job is to answer one question with evidence: is this game actually fun?** Every scope decision below is made in service of answering that question honestly within one region. Subjective fun is measured here — and only here — by the protocol in §9.

## 1. What "fun" means for this specific game

The charter's fun is not the fun of a combat sandbox or a cinematic campaign. It is the fun of **a world that was already happening when you arrived**. Five felt experiences define it. If the slice delivers these, the game is viable; if it does not, no amount of additional content will save it.

| # | The felt experience | The mechanism that must produce it | Where measured |
|---|---|---|---|
| F1 | *"I saw something I wasn't told about, and I wanted to go there."* | Sight-driven discovery, no UI markers, readable silhouettes at distance | §8 pillar P4, §9 |
| F2 | *"I should not be here yet."* → later *"I can come back now."* | Non-scaling bands, remembered geography, gear that matters | §8 pillar P2, §9 |
| F3 | *"I chose to go hunting because I wanted something specific."* | Grinding that feeds crafting/faction/hides rather than only XP | §8 pillar P6, §9 |
| F4 | *"My companion is a person, and it was worth bringing them."* | Companion with a voice, an opinion, and a mechanical contribution | §8 pillar P3, §9 |
| F5 | *"That artifact was a story I earned."* | Multi-session epic quest with a real forging and a real choice | §7, §9 |

**Anti-goals for fun:** no "just one more marker" compulsion loop, no power fantasy from scaling, no spectacle that the mechanics cannot back up.

## 2. Region definition — one 2×2 km region, complete

The charter's region-by-region rule is adopted verbatim (D-12): **one region, finished, before a second exists.**

| Property | Value |
|---|---|
| Region | **Kaldrun Reach** — one contiguous 2 000 m × 2 000 m region (**4 km²**) |
| Cells (D-06 / streaming) | **400 exterior cells of 100 m × 100 m** (`WORLD_ARCHITECTURE.md` §2: 20×20 per region). Streaming radius 400 m (9×9 cells); budget 8 loaded / regional-simulated ring / stored for the rest. The cell is the streaming unit *and* the delta-save unit (D-05). |
| Elevation range | 0–180 m. One river, one lake, one pass, one escarpment. |
| Named locations | **11**: the town, 4 wilderness areas, 3 dungeons, 1 deep-water site, 1 faction outpost, 1 road nexus. That is the whole list. |
| Roads / paths | 1 main road (town ↔ pass, ~1.6 km), 2 trails, 1 river route (boat-less in the slice) |
| Traversal | Walking edge-to-edge ≈ 18–22 min. Fast travel, once earned, ≈ 40 s. Walking must remain a real choice (§8 pillar P5). |
| Danger bands | **Three, spatial, never level-scaled**: Band A (levels 1–6, around town) → Band B (7–14, mid-reach) → Band C (15–22, the escarpment, the deep sites). Band C is *enterable at level 4* and is meant to kill you. |
| Level cap in slice | **22** (the extrapolated cap for the full game remains higher; the slice cap exists so builds differentiate before the content ends) |
| Slice playtime target | **6–10 hours** for a first-time player to: reach level ~14, complete the epic quest, and clear two dungeons. 3–4 hours for a focused main-line run. |

### 2.1 The town — one meaningful settlement

| Property | Value |
|---|---|
| Name | **Vessmere** (fishing + iron, on the lake outlet) |
| Footprint | **~150 m × 150 m** — walkable end to end in ~2 min |
| Population | **8 named NPCs** + **~14 Tier-B role NPCs** (guards, dockhands, a farmer, a miner crew) = 22 total. Not 60. |
| Interiors | **6 enterable**, each its own interior space with a portal anchor (`WORLD_ARCHITECTURE.md` §9): longhouse/hall, smithy, apothecary, tavern, warehouse, factor's office. Town footprint spans **4 exterior cells** (2×2). |
| Functions present | 1 smithing station, 1 alchemy station, 1 woodworking station, 1 merchant, 1 provisioner, 1 skill trainer, 1 faction representative, 1 companion recruit, quest givers |
| Why it is "meaningful" | It has a **problem** (the iron seam is deliberately sealed and the Ashlings want it opened), a **memory** (the town reacts to two specific slice events), and **3 exits** (road, lake shore, escarpment trail) each leading somewhere a player can see from the gate. |
| Player property | **One buildable plot** on the town's north edge, granted by the epic quest at stage 5. This is the slice's only building site. |

### 2.2 The eight named NPCs (complete list)

| NPC | Role | Slice function |
|---|---|---|
| `npc.factor_ilesa` | Town factor / quest giver | Epic quest stages 1–2 and 9; faction choice pressure |
| `npc.smith_bregan` | Master smith | Smithering trainer, forging ritual (stage 7–8), reconstructed-fragment handoff |
| `npc.herbalist_osen` | Apothecary | Alchemy trainer, one recipe discovery, herb knowledge for the ritual |
| `npc.keeper_daun` | Reconstructor-scholar | Epic stages 2–4: artifact research, the codex, the three-knowledge gate |
| `npc.warden_saelis` | Companion | Recruitable; personal stake in dungeon D2; one disagreement scene |
| `npc.outcast_venn` | Faction contact (the Ashlings) | Optional faction path; alternate fragment source in D3 |
| `npc.captain_rooke` | Town authority | Reputation gate, crime/bounty stub, home-defense warning |
| `npc.trader_malk` | Merchant | Economy proof (specialisation: buys hides and ore, refuses crafted goods at a loss) |

Eight is the minimum to staff: quest chain, three crafting professions, one companion, two factions, one authority. **No generic "guard #3" is ever named** — that is what Tier-B role NPCs are for.

## 3. Content budget

Every count above 3 has a one-line justification. Volume is a *cost*, not a feature; the slice must be dense, not large.

| Category | Count | Justification |
|---|---|---|
| Regions | **1** | D-12. |
| Towns | **1** | STEP 16. A second town would double the writing and prove nothing new about fun. |
| Wilderness areas | **4** | STEP 16 says "several". Four = one per danger band plus the lake, each visually distinct from the town gate; three would merge Bands B and C. |
| Dungeons | **3** | STEP 16 says 2–4. Three: **D1** required for the epic path, **D2** required for the epic path + companion story, **D3** optional and faction-gated → gives the alternate fragment (epic choice). Two required for the main line, one optional. No fourth: a fourth would be content with no system behind it. |
| Enemy families | **3** | STEP 16 says "multiple". **Beast** (wolves, bears, a lurker), **Humanoid** (raiders, a raider captain, one caster), **Undead** (drowned, a barrow-wight). Three families is the minimum proving damage-type/resistance and AI differences matter. |
| Enemy archetypes | **12** | Enough that each family has 3–4 with distinct behaviour (pack, charger, ranged, ambusher) and every level band is populated. |
| Bosses | **2** | 1 major (epic-quest apex, non-scaling, level 18 area), 1 optional (D3's guardian, level 16). STEP 16 requires one; the second exists because the epic quest needs a *second* fragment holder and reusing the first boss would teach nothing. |
| Weapon types | **5** | 1H sword, 1H axe, 2H axe, bow, staff. Covers melee/ranged/magic, one- and two-handed, and one shared-skill pair (sword vs axe) so mastery differentiation is testable. |
| Weapon/armor definitions | **~45** | Hand-authored base items across 3 material tiers × 5 weapon types × 3 armor slots, plus ~10 dungeons drops. The remaining variety comes from the modifier pipeline (§5.3), not from authoring more bases. |
| Modifier affixes | **18** | Enough that two level-14 swords can differ meaningfully. Beyond ~20, authoring cost grows faster than perceived variety at this content volume. |
| Magic schools | **5** | **Ember** (damage over time), **Ward** (protection), **Mend** (restoration), **Reave** (necrotic, own resource economy), **Iron** (a *construct* school: summons a short-lived scrap construct that fights for you; the Lantern Warden is foreshadowed by it). Each school has a *different mechanic* — Ember stacks, Ward absorbs, Mend is interruptible, Reave harvests, Iron summons. Covers "several" and proves schools are mechanically distinct, not five colours of damage. |
| Spells | **20** | 4 per school: basic / utility / defensive / signature. Enough for two builds to play differently from level 8 onward. |
| Skills | **8** | Two per non-crafting axis family (melee, ranged, arcane, survival), each levelling by use. This is the "learned capability" axis of D-09. |
| Attributes | **7** | The canonical set from `PROGRESSION.md` §4.1: Might, Endurance, Agility, Precision, Will, Insight, Presence. The slice uses all seven because the attribute model is a save-schema commitment — a reduced set here would be a migration later, and the whole point of the slice is to exercise the real schema. |
| Ability/talent choices | **12 nodes** | 3 tiers × 4 options, one point per 2 levels. Enough to force a real choice at 4 decision points. |
| Playable races | **2** | STEP 16 does not require races; the charter does eventually. Two (`race.human_vess`, `race.ashborn`) with genuinely different start bonuses and one racial ability each is the minimum proving race is not cosmetic. |
| Starting archetypes | **4** | Blade, Hunter, Ember Adept, Warden. Four covers melee/ranged/magic/support and at least two hybrids by level 10. |
| Professions | **3 crafting + 2 gathering** | STEP 16 says "multiple". **Blacksmithing**, **Alchemy**, **Woodworking**; gathered by **Mining**, **Herbalism**. Three professions is the minimum proving professions are separate progression tracks (materials cross-feed: mining+woodworking → blacksmithing hafts, herbalism → alchemy → blacksmithing tempering oils). |
| Recipes | **~24** | 8 per profession across 3 tiers. Not 100: the slice tests whether crafting *stays relevant*, which is answered by 24 recipes spanning levels 3–20, not by volume. |
| Gathering resource types | **6** | Iron, ashbloom, wyrdwood, silver, resin, lake-crystal. Silver and lake-crystal exist only in Band C/dungeons, which is the mechanism that keeps crafting relevant past dungeon loot (charter §12). |
| Material properties | **5 material families** | Iron, silver, star-metal, wyrdwood, ash-glass. Charter §12 names iron/silver/star-metal/wood/bone as needing to differ meaningfully. Each has a distinct property table row (weight, damage bias, enchant affinity). |
| Factions | **2** | **The Vessmere Compact** (town authority, order, trade) and **the Ashlings** (outcast reclaimers of the old works). Two opposed factions is the minimum for reputation to be a *choice*. A third would need a third region to be meaningful. |
| Reputation tiers | **5 per faction** | Hostile / Wary / Neutral / Trusted / Sworn. Gates dialogue, prices, and one slice objective. *(Flagged by M7, 2026-09-25: the as-built ladder has no hostility tier; M9 reconciles this as relation, war state and legal status.)* |
| Companions | **2 recruitable** | `npc.warden_saelis` (story companion, personal quest in D2) and `npc.outcast_venn` (faction-locked; mutually exclusive in practice with Saelis's preferred outcome → real choice). STEP 16 says "a recruitable companion"; two is the minimum where the *choice* of companion means anything. |
| Hirelings | **0** | Home defense, hireling housing, and steward roles need settlement systems the slice does not build. Deferred explicitly (§10). |
| Building piece families | **4** | Foundation/floor, wall, roof, opening. 4 families × ~6 pieces = ~24 pieces: enough for a cottage with a door and a window (the charter's own ladder step 4). No towers, no gates, no farmland. |
| Crafting station types | **5** | Forge, alchemy bench, woodworking bench, **player-built workbench** (buildable), and one placeable **smelter**. The player-built workbench is the proof that building integrates with gameplay (charter §13) rather than being decorative. |
| Quests | **1 epic (9 stages) + 9 side** | One substantial multi-stage quest (§7) plus nine small quests, one per slice subsystem, each 5–20 min. Ten total, not fifty. |
| Epic reward | **1 artifact, 3 variants** | §7.4. One artifact is "one memorable epic reward" (STEP 16) and its three outcomes come from one final choice, not three new systems. |
| Unique/set item relationships | **2 sets** | One 3-piece martial set, one 3-piece caster set, both mid-tier, to prove set bonuses exist as data. No more. |
| Cartography | **1 map item + fog-of-war** | The charter's exploration pillar needs a map that *gains* detail. One purchasable map upgrade and region fog reveal. No treasure maps in the slice. |
| World events | **4 types** | Roaming rare creature, caravan ambush, undead incursion at the road nexus, travelling merchant. Four proves the event system is data-driven and non-iconic. |
| Fast-travel nodes | **5 attunement stone nodes** + 1 town recall | §8 pillar P5. Not 20, and none given for free. |
| Mounts | **0** | Deferred (§10). Earned fast travel is the slice's traversal answer. |
| Save slots | 1 manual + 3 rotating autosaves + 1 backup | D-05 atomic write + rolling backup. |
| Total content files | **~180 YAML** | This is the honest size. If implementation finds itself authoring 600, the slice has become a product and has stopped being a test. |

**Content-scale note.** Everything above is authored in YAML against the schemas validated in Phase 1 (D-03) with **zero new gameplay code per definition**. If adding the 45th weapon requires C#, the slice has found a real architectural defect and should stop and fix it before adding content.

## 4. What the slice adds to the Phase 1 architecture

The prototype proved the seam. The slice adds *content-bearing systems* on top of it; each must be expressible through the existing command/event bus (D-02) and persist through the existing sparse-delta save (D-05).

| New system | Why the slice cannot ship without it | New persistent state |
|---|---|---|
| **Three-tier simulation (D-06)** | 4 km² with 22 town NPCs cannot be fully simulated. A/B/C transitions must exist or the world is either empty or unaffordable. | Per-NPC tier, coarse position, current schedule block |
| **Building (D-08)** | STEP 16 requires it; it is the only system proving player-created persistent world change. | Placed pieces (piece ID + transform + socket parent + health), plot ownership |
| **Factions & reputation** | STEP 16 requires them; the epic quest's final choice is expressed through them. | Per-faction standing, gate flags |
| **Professions** | STEP 16 requires crafting progression; the epic forging ritual depends on it. | Profession level, XP, discovered recipes |
| **Modifier / quality pipeline** | 45 base items must produce a slice's worth of loot variety. | Per-instance modifier list, quality tier, crafted-by |
| **Sets · World events · Cartography** | Charter §8 set relationships, §18 world events, §3 map that gains detail — each a data-shape proof. | Set membership (definition query only); event instance state; per-cell discovery flags and map-detail level |
| **Tiered enemy AI + non-scaling bands** | The "I should not be here" pillar is the slice's central claim. | Per-spawner population state (not per-creature for Tier C) |
| **Quest graph + debugger (D-07)** | The epic quest cannot be built or diagnosed without the debugger. | Quest instance state per objective, edge cursor |

Everything else the charter asks for — mounts, hireling roles, settlement founding, treasure maps, taming, enchanting, augmentation, crime/bounty depth, a second region — is **not added** (§10).

## 5. Systems at slice fidelity

### 5.1 Combat

Target: readable, preparation-rewarding, non-spongy (charter §7). Attack resolution is stamina-gated light/heavy with directional block (stamina cost) and a dodge-roll of 0.25 s i-frames. Damage runs on **4 channels** — physical (slash/pierce/blunt), fire, ward-reactive, necrotic — each with resistance rows on armor and creatures. **Time-to-kill** at same level and peer gear is 4–8 s per standard enemy; **time-to-die** is 8–15 s of sustained mistakes. 8 status effects total: burning, bleeding, staggered, slowed, weakened, warded, mending, reaved. Stagger is threshold-based per creature with a visible tell, never a stun-lock. **Non-scaling rule:** every creature has an authored level and stat block, there is no scaling code path in the slice, and adding one is a charter violation — Band C creatures are level 15–22 and remain so forever. **Hard cap: an enemy is never made harder by raising HP alone.** Difficulty comes from archetype behaviour, resistances, and enemy count.

### 5.2 Magic

Five schools, five *different* mechanics (charter §6). **Reconciled (M3e, owner-approved magic model):** there is no mana; read "Mana" below as Focus and Strain (`MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md`, `PROGRESSION.md` §4.1). Essence and the scrap component stay as contextual costs.

| School | Resource | Core mechanic | Proof spell |
|---|---|---|---|
| **Ember** | Mana | Applies burning stacks; damage scales with stacks already on target | `spell.ember.cinder_lance` |
| **Ward** | Mana | Absorb pool + reactive retaliation; cannot be cast on others in the slice | `spell.ward.stone_skin` |
| **Mend** | Mana | Heal-over-time that is interrupted by taking a heavy hit; rewards positioning | `spell.mend.green_tide` |
| **Reave** | **Essence**, harvested from corpses (charter §6 necromancy example) | Spends a separate resource that only exists after kills; expires out of combat | `spell.reave.grave_tithe` |
| **Iron** | Mana + a scrap component | Summons a short-lived construct that fights for ~20 s; the component is consumed | `spell.iron.slag_sentinel` |

Essence is the slice's proof that a school can run on a *different resource economy* entirely; Iron is the proof that a school can consume a *material* and create an entity. Both make their characters play differently by construction rather than by number.

### 5.3 Loot, itemization, and crafting relevance

The charter's §8 and §12 demands ("a rare weapon found at 15 stays useful at 25", "crafting must not become irrelevant") are tested by exactly three mechanisms:

1. **Quality tiers and crafted-by attribution.** A master-crafted iron item beats a dungeon-drop iron item of the same type. `crafted_by` and `quality` are per-instance fields.
2. **Band-C-only materials.** Silver, lake-crystal, and star-metal exist only in Band C and D1/D3. Dungeon loot gives you *items*; crafting gives you items in materials that drop nowhere. This is the whole anti-obsolescence argument, and it is small enough to actually implement.
3. **Modifier budget, not item replacement.** Rare items carry 2–4 affixes; the slice's affix pool includes two that scale with a *skill level* rather than an item level, so an early rare can grow with the character.

Name, don't enumerate: the slice hand-authors **one** memorable pre-epic item — `item.weapon.silvered_ash_hatchet`, a level-11 axe with a bleed affix found in D2 — as the worked example of "useful past its level". The other ~44 bases are ordinary.

### 5.4 Building (D-08)

**Socket/snap assembly of authored pieces: no structural simulation, no collapse, no load-bearing math** (D-08). ~24 pieces across 4 families (foundation/floor, wall, roof, opening), grid-snapped to the plot, snapped socket-to-socket, free rotation in 45° steps, invalid overlaps rejected with a visible reason rather than placed-then-fixed. **One plot**, Vessmere north edge, **40 m × 40 m**, entirely inside one cell (`r_0_0:c_03_07`), granted by epic quest stage 5 — building before stage 5 is impossible, deliberately, so building arrives with narrative weight and so no player structure straddles a cell seam in the slice's only building test. Three functional pieces only: a **workbench** (crafting), a **storage chest** (container), and a **bed** (rest / time-of-day advance). Damage is per-piece `health` applied by explicit scripted events (the one home-defense probe, §8 P1) and repaired with materials at the piece — **never emergent physics damage**. Persistence is a row: `{piece_def_id, plot_id, cell_id, transform, socket_parent, health, owner_instance_id}`, a world delta (D-05) that must round-trip in save/load. Companions and town NPCs must path through a player-built doorway — **a slice exit criterion** (`E4`, §11.1), because D-08 chose snapping specifically to guarantee it.

### 5.5 Companions

Two recruitable companions, each with the state the charter's §15 demands but bounded. Identity: name, voice register, 3 beliefs, 2 loyalties, 1 grudge. Relationship: **Trust** and **Respect** (−100..100), moved by keeping them alive, giving them gear, 6 flagged dialogue decisions, and one disagreement scene each. Orders: **6** — follow, hold, attack my target, attack freely, avoid combat, use ranged only (tactically meaningful, not the charter's full list). Progression: their own level curve, an equippable weapon plus 2 armor slots, and 3 perks unlocked by trust tier. Saelis has a 3-stage personal quest inside D2 whose resolution changes epic stage 8. A companion can be knocked down (revivable) and, if trust bottoms out, **leaves permanently** with a farewell scene — real loss, but never a reload-forcer. **Exclusivity:** Saelis and Venn's preferred outcomes conflict at stage 9; recruiting both is possible, keeping both happy to the end is not.

### 5.6 Factions and reputation

**The Vessmere Compact** (town authority, order, trade) wants the iron seam reopened and the Ashlings expelled; backing it makes the Ashlings hostile, locks D3, and pleases Saelis. **The Ashlings** (outcast reclaimers of the old works) want the seam left shut and the works reclaimed; backing them raises Compact prices, puts town guards on you, and pleases Venn. Five tiers per faction gate **dialogue, prices, and exactly one slice objective each**. Reputation is never a single good/evil meter (charter §20) — the two factions have *incommensurable* goals, and the epic quest's final choice is where they collide. *(Flagged by M7, 2026-09-25: conflicts with the as-built ladder, which has no hostility tier, and with standing as access only; M9 reconciles it as relation, war state and legal status.)*

### 5.7 World events and ecology

Four data-driven event types, spawned by Tier-C abstract simulation and promoted to Tier A when the player is near (D-06). Events **never** appear as map icons; they appear as things you see or hear. Rare roaming creature at a Band-C edge; caravan ambush on the main road; undead incursion at the road nexus at night; travelling merchant who moves between three spots. One ecological behaviour: wolves hunt the region's deer, and the deer's Tier-C count affects wolf spawn counts — a single chain, enough to prove the abstraction produces *visible* consequences.

## 6. Quest framework requirements (D-07)

The epic quest is content, not code. That claim is only true if the objective model is already general. The slice must implement this **closed set** of objective types, each with a strict schema:

| Objective type | Predicate over world state | Used by epic quest? |
|---|---|---|
| `talk_to` | Conversation node visited | Yes (stages 1, 4, 6, 9) |
| `visit_location` | Player enters location radius | Yes (stages 2, 3, 5) |
| `acquire_item` | Definition or specific instance in an inventory/container | Yes (stages 3, 4, 6) |
| `craft_item` | A craft event with recipe ID, at or above quality | Yes (stage 4) |
| `kill_creature` | Creature definition or instance dies, with location/tier filters | Yes (stage 6) |
| `defeat_boss` | Specific creature instance dies | Yes (stage 8) |
| `build_piece` | Placed piece of a definition exists on an owned plot | Yes (stage 5) |
| `reach_reputation` | Faction standing ≥/≤ tier | Yes (stage 9, both paths) |
| `relationship_threshold` | Companion trust/respect ≥/≤ value | Yes (stage 9) |
| `learn_recipe` | Recipe known by a profession | Yes (stage 4) |
| `world_state` | Arbitrary predicate over a named world-state key (the escape hatch) | Yes (stage 7 ritual state) |
| `choice` | A player decision recorded at a conversation node | Yes (stage 9) |
| `timed` | Predicate must hold within N `world_time` units | No (side quest only) |

**Graph semantics.** Objectives form a DAG with **AND**, **OR**, and **optional** edges declared in YAML. Objective state, edge cursors, and any `world_state` keys a quest writes are persistent and saved (D-05). No visual scripting (D-07/charter forbid it); no per-quest C#.

**Mandatory tooling, not a luxury (D-07):** a **quest debugger** that, for any active quest, prints the graph and answers *"what is this quest waiting on right now, and which predicate is false?"* The epic quest is long-running across sessions; without this tool we cannot diagnose it, and the slice should be considered incomplete without it.

## 7. The epic multi-stage quest — `quest.artifact.sundered_lantern`

Follows the charter's §9 shape exactly. Named here only where it defines a system boundary.

### 7.1 Premise (one paragraph, deliberately)

A pre-Compact lantern-forge at D1 produced a single instrument — the **Sundered Lantern** — that a now-dead order used to hold something closed. It was broken on purpose. It is not a sword of destiny and the player is not prophesied; a factor's ledger, a scholar's grudge, and an outcast's memory are the only reason anyone cares. The player can walk away at any stage, and the world will not end.

### 7.2 Stage graph

| # | Stage | Objective types used | Where | Gate |
|---|---|---|---|---|
| 1 | **The ledger** — a dying town's iron problem turns out to be a *closed* seam, not an empty one | `talk_to` ×2 | Vessmere | Level 6+, `reach` town |
| 2 | **Research the artifact** — Daun identifies the Lantern from the ledger and three fragments of a Codex | `talk_to`, `acquire_item` ×3 | Vessmere + Band B ruin | 3 codex fragments, any order (OR set) |
| 3 | **Locate a reconstructor** — Daun can translate but not reconstruct; the knowledge dies with the order unless you find their last works | `visit_location`, `acquire_item` | D1 (Barrowworks) | Clear D1's inner ring |
| 4 | **Obtain crafting knowledge** — three separate knowledges, deliberately in three different systems | `craft_item`, `learn_recipe`, `talk_to` | Vessmere | Blacksmithing 12 + the *silver-bind* recipe from the Ashlings; Alchemy 10 for the quench; a smith's trust |
| 5 | **Claim the works** — the plot is granted; the ritual needs a forge you own | `build_piece` (forge or workbench), `talk_to` | Vessmere plot | Player builds at least a workbench + the smelter piece |
| 6 | **Recover the three fragments** — held by three separate holders, in any order (OR) | `kill_creature`, `visit_location`, `acquire_item` | D2, D3 (optional path), Band C | Band C fragment requires surviving level 18 creatures |
| 7 | **Difficult materials** — star-metal and ash-glass, neither purchasable | `acquire_item`, `world_state` | Band C + D3/lake | Mining 14; the ash-glass requires the Ashlings or a dangerous dive |
| 8 | **The forging ritual** — a multi-input ritual at the player's forge; the two companions' personal arcs determine whether it holds | `defeat_boss`, `craft_item`, `world_state` | Vessmere forge + one boss encounter | Guardian of the second fragment |
| 9 | **The final choice** — what the Lantern becomes, which is also what the region becomes | `choice`, `reach_reputation`, `relationship_threshold` | Vessmere | Compact ≥ Trusted **or** Ashlings ≥ Trusted; Saelis trust or Venn respect threshold |

**Shape rules the slice enforces:**

- **Stages 6, 7 and 8 are enterable out of order.** The graph is not a corridor; a player who wanders into Band C at level 8 and survives one fragment gets a fragment.
- **No stage is a kill-counter.** Every kill objective is attached to a named holder with a reason (stage 6).
- **The quest is abandonable and resumable** at any stage, including across save/load and across a content version bump (D-05 migration path).
- **No stage requires the other stages' content to be *replayed*.** Idempotent objectives (a re-fired objective changes nothing) is a tested property, not an aspiration.

### 7.3 Why the epic quest is the slice's integration test

The epic quest is **not** extra content bolted onto the systems; it *is* the load-bearing argument for the slice's scope. Stage 2 needs the quest graph's OR edges and `acquire_item` across three locations; stage 3 needs a dungeon that is a *place* with an inner ring rather than a corridor; stage 4 needs three professions at real levels and a recipe gated behind faction access; stage 5 needs a functional placed building piece (D-08) that persists (D-05); stage 6 needs three dungeon-equivalents and non-scaling Band C; stage 7 needs Band-C-only materials and gathering skills that matter; stage 8 needs a boss, per-instance crafted output, and companion state affecting an outcome; stage 9 needs factions, reputation tiers, relationship thresholds, and a recorded choice. **If any one of the slice's systems is fake, stage 9 exposes it.** That is the point.

### 7.4 The major boss and the epic reward (design intent, not lore volume)

**Boss: `creature.construct.lantern_warden`** — the thing the order left *holding the door*, still doing its job after four centuries.

| Intent | Requirement |
|---|---|
| Non-scaling | Authored level 20, stat block never varies with player level. Under-geared at level 12, it kills you in ~6 s. |
| Readable | 3 phases, each with one telegraphed heavy (wind-up ≥ 1.1 s) and one area denial. No attack is unreactable. |
| Preparation-based | Phase 3's aura punishes the wrong damage type; the fight is won by bringing silvered/wyrdwood gear and the right school, not by grinding five more levels. |
| Solvable solo | Tuned so a solo character with one companion and correct preparation wins at level 17–18. Never requires a party (charter §2). |
| Multi-system | Its arena is inside the D2 vault; killing it is `defeat_boss`; its death drops the second fragment **and** is the world-state key that flips Vessmere's dialogue. |

**Reward: `item.artifact.sundered_lantern`** — one item, three natures, chosen at stage 9:

| Variant | Final choice | Mechanical identity | Why it stays relevant |
|---|---|---|---|
| **Reforged** (Compact path) | Bind it closed | Ward-school focus: absorbs, and reflects a share of absorbed damage | Scales with Ward mastery, not item level |
| **Unbound** (Ashling path) | Open it | Reave-school focus: harvests essence from the dead at range | Scales with Reave essence economy |
| **Kept** (refuse both) | Leave it broken | No focus bonus; grants the passive ability to see the region's hidden sites on the map — a *cartography/exploration* reward | Scales with nothing, and is therefore the choice for an explorer build |

Memorability comes from: it is the only item in the slice with a unique ability, it is the only item the player *made*, it changes what the region does next, and it is granted by a choice rather than a drop (charter §9: "epic rewards should usually involve STORIES, not random drops").

## 8. What the slice proves about each design pillar

Each pillar gets a **binary acceptance test** and a **telemetry signal**. A pillar that cannot produce both is not actually being tested.

| Pillar | Slice proof | Acceptance test | Telemetry signal |
|---|---|---|---|
| **P1 — A world that does not revolve around the player** | Town NPCs run Tier-B schedules you can observe (shops close, a smith goes home, a guard patrol swaps); 4 world events fire without the player's involvement; the wolf/deer Tier-C chain changes spawn counts | Stand in Vessmere square for 20 in-game minutes without accepting a quest and observe ≥ 5 autonomous NPC state changes and ≥ 1 world event | Count of NPC state changes and events observed with 0 quests active |
| **P2 — No level scaling** | Band C is level 15–22 at all times; the epic quest's Band-C fragment is reachable at level 8 and lethal | At level 6, enter Band C: die within 60 s. Return at level 16: clear the same three spawns | Deaths in Band C by player level; time-to-kill of the same spawn at level 8 vs 16 |
| **P3 — Solo-first, not lonely** | Two companions with orders, opinions, disagreements and loss; the boss is solo-tuned with preparation | Complete D2 and the boss with exactly one companion, no party, no hireling | Companion damage share and healing share; player-survival delta with/without companion |
| **P4 — Earned fast travel** | 5 attunement nodes must be *found and attuned*; no node is given; the town recall is unlocked by stage 5 of the epic quest | Fast travel to a node never visited is impossible from a fresh save | % of travel legs walked before the second attunement; fast-travel use rate after full attunement |
| **P5 — Exploration driven by sight, not markers** | Compass and journal *text* only. No quest markers, no POI icons, no objective arrows. Distant landmarks (the escarpment, the D1 tower, the lake light) are visible from the town gate and are the navigation | A first-time player, given only journal text, reaches D1's mouth without opening the map | Journal/map opens per hour; % of locations discovered while no quest pointed at them |
| **P6 — Grinding is useful** | Hides→leatherworking-adjacent crafting, ore→professions, fangs→alchemy, faction reputation from Band-B cleanups, rare spawns at 3 spots | A player who hunts for 30 min gains a *specific* material they needed for a planned craft, plus profession XP, plus ≥ 40 reputation | Voluntary combat time with no active quest; materials gained per hour of voluntary hunting |
| **P7 — Crafting stays relevant** | Band-C-only materials; master-crafted beats dungeon-drop of the same base; the epic forging stage requires Blacksmithing 12 and a player-built forge | A level-18 character's best weapon can be one they crafted, not one they looted | % of equipped items with `crafted_by == player` at level 20; number of crafted items in the top-3-power slot |
| **P8 — Failure is real, not a reload prompt** | Death applies XP debt + a temporary injury + a corpse-recovery trip (no permanent item loss); companions can leave; faction choices lock content; the epic quest can be completed three different ways and never failed outright | A player who dies 5 times continues playing without loading an earlier save | Deaths per session vs. manual saves loaded; voluntary post-death playtime |

## 9. Evaluation criteria and playtest protocol — "is it fun?"

### 9.1 Evidence tiers (this project's existing evidence bar, applied to design)

| Tier | Evidence | Counts as |
|---|---|---|
| **T0** | Automated assertion in `Domain.Tests` or `Presentation.Tests` | Proof of a *behaviour* |
| **T1** | Reproducible artifact: saved game + world-state dump + timestamped telemetry | Proof of a *state* |
| **T2** | Recording + written observation from ≥ 2 independent observers, or the director's own captured play plus one independent reviewer | Proof of an *experience claim* |
| **T3** | A single person's unrecorded impression | **Anecdote. Does not count alone.** |

Vague positives ("it felt good", "very immersive") are not evidence at any tier. A claim must be tied to an observable event in the recording.

### 9.2 Playtest protocol

**Participants:** minimum **4 testers**, at least 1 of whom has never played an old-school RPG, and at least 2 of whom have (they are the target audience and the harshest judges). The director plays a full pass personally and records it.

**Session design:**

1. **Blind brief.** Testers are told: "first-person fantasy RPG, one region, do what you want." They are **not** told the pillars, **not** told what to look for, **not** told where anything is. No reading of this document.
2. **Think-aloud, unprompted.** Testers narrate. If they ask a question, the facilitator **does not answer** and records it as a clarity-gap datum. This is the same blind-pass discipline used elsewhere in the project: never lead the witness.
3. **Length:** one 90-minute session, plus an optional second 90-minute session within 72 h. The second session is where the real verdict lives — whether they *choose* to return.
4. **Instrumentation always on:** time per system; quest/journal opens per hour; map opens per hour; deaths and whether they retried or loaded; time spent in voluntary (no-quest) combat and gathering; locations discovered with and without an active objective; distance walked vs. fast-travelled; companion share of damage/healing; where they stood still for > 45 s (confusion/stuck signal).
5. **Post-session, structured self-report:** 6 questions only, each answered on a 1–5 scale, each tied to one pillar (P1–P6 above), plus one free-text: *"describe the most memorable thing that happened."* The free-text is compared against what actually happened in the recording.
6. **The one question that decides it:** *"Would you keep playing this tomorrow if it existed?"* Yes/No, plus one sentence why.

### 9.3 Decision rules (written before the data arrives, so they cannot be renegotiated after)

| Signal | Threshold | Consequence |
|---|---|---|
| Voluntary second session | **≥ 3 of 4** testers return without being asked | Below this, the slice's core loop does not hold. Rework the loop before adding content. |
| Memorable-thing recall | Free-text identifies an event the tester *was not directed to* | If ≥ 3 of 4 can do this, F1 (sight-driven discovery) is real. |
| P2 (non-scaling danger) | Tester reports *unprompted* both "I shouldn't be here" and a later return | If 0 of 4 report either, the difficulty bands are not reading. |
| P4 (marker independence) | Map opens ≤ 3 per hour after the first hour, and ≥ 1 tester reaches D1 on journal text alone | If testers navigate primarily by map, the no-marker pillar is failing at the interface level, not the design level. |
| Fast travel | ≤ 40 % of travel legs fast-travelled *before* full attunement is possible to exceed | If fast travel becomes the default mode, P4's "earned" framing is undermined; cut nodes. |
| P6 (useful grinding) | ≥ 2 testers describe a hunt they chose for a *specific material* | If grinding is only tolerated, not chosen, P6 fails and crafting's material chain needs rework. |
| Deaths | Testers continue after ≥ 3 deaths without loading an earlier save | If deaths drive reloads, the death system is punitive rather than consequential. |
| Framerate | No session drops below 45 fps on the baseline machine | Below this, the experience data is contaminated by performance. |

### 9.4 Kill criteria (the honest part)

The following findings are **stop-the-line**, not "iterate later":

1. **Fewer than half the testers voluntarily return**, and their recorded sessions show no self-directed goal formation. Conclusion: the loop is not yet fun; **do not proceed to Phase 3 content scaling.**
2. **The non-scaling bands are not perceived at all** — no tester can describe a place as too dangerous. Conclusion: the world reads as flat, and the game's central pillar is absent.
3. **Companions are consistently described as a liability or ignored.** Conclusion: P3 fails; companion AI is reworked before any content is added, because companions are the solo-first answer to loneliness.
4. **The epic quest is described as a checklist** by ≥ 3 testers. Conclusion: stages have been reduced to objectives without meaning; the quest graph needs design work, not more stages.
5. **The slice can only produce its fun feelings with this specific hand-authored content.** Conclusion: the fun is in the content, not the systems — which is exactly the failure this project cannot afford at 500 items. This is the most dangerous finding of all, and the only way to detect it is the §9.2 protocol run on a *second*, differently-shaped small quest.

### 9.5 What "fun" is not allowed to be judged on

Art fidelity, animation quality, audio, UI polish, name quality, and lore volume are **out of scope for this verdict** (charter: readable and atmospheric, not AAA — D-01's accepted trade). If the slice is only enjoyable because of production value, it is not enjoyable.

## 10. Explicit non-goals of the slice

Every row is a promise *not* to build it in Phase 2. All of them land in Phase 3 or later unless stated otherwise.

| Non-goal | Why it is excluded |
|---|---|
| A second region, or any content outside the 2×2 km | D-12: one region completely, first |
| Multiplayer in any form (LAN, co-op, dedicated server, authority formats) | D-12; only the command/event seam exists. Not scheduled at all |
| The full profession spread (charter §12's 20 professions) | 3 crafting + 2 gathering proves the axis; the rest is a content-scaling exercise |
| Mounts, boats, caravans, player-created teleport anchors | Earned fast travel via attunement nodes is the slice's traversal answer |
| Settlement founding, settlement growth, hireling housing, steward/merchant/farmer hirelings | Needs population + assignment systems far beyond one plot |
| Home defense as a real system (frequent attacks, traps, guards, trained animals) | One scripted probe proves the seam; frequency is a Phase 3 balance question (charter §14 warns against making building annoying) |
| Endgame / post-cap mastery, superbosses, high-level regions | The slice cap is level 22 and ends there |
| Full race/archetype spread (the charter's 20+ builds) | 2 races, 4 archetypes, 12 talent nodes proves builds differentiate |
| Crime, bounties, jail, guard-pursuit depth | One authority NPC and reputation exist; the *system* does not |
| Enchanting, augmentation, sockets, cursing, durability | Sets and affixes prove the data shape; deeper item systems are additive |
| Taming, pets, and summoning as a *school of its own* | Iron's construct and Reave's essence are the only summon-adjacent mechanics |
| Treasure maps, a cartography skill tree, further map tiers | Fog-of-war + one purchasable map upgrade is the minimum proof |
| Ecology beyond the wolf→deer→spawn-count chain | One chain proves the abstraction produces visible consequences |
| A central main storyline with a plot arc | The epic quest is the slice's story; there is no chosen-one arc (charter §21) |
| Voice acting, cinematics, orchestral audio, full sound design | Placeholder audio and text only |
| Console/VR/Steam Deck targets, full accessibility suite | Windows desktop baseline only (D-01 / DECISIONS open question 2). Unplanned |
| A full UI/UX pass, HUD customization, key-rebinding suite | Functional UI is enough to judge systems |
| Hundreds of any content type | This document exists to prevent exactly that |

## 11. Exit criteria — must hold before Phase 3 expansion begins

Phase 3 is "expand the world: races, archetypes, spells, professions, creatures, NPCs, settlements, dungeons, artifacts." **None of that starts until every row below is true.**

### 11.1 Functional

| # | Exit criterion | Evidence tier |
|---|---|---|
| E1 | The epic quest `quest.artifact.sundered_lantern` is completable in all three endings from a fresh save, on a shipped build, without developer tools | T1 (3 saved end-states + 3 recordings) |
| E2 | All 9 stages are resumable across save/quit/relaunch, and a stage entered out of order still completes | T1 (state dumps) |
| E3 | The quest debugger correctly names the blocking predicate for 20 deliberately-broken synthetic quest states | T0 (test suite) |
| E4 | A player-built structure (workbench + walls + door + roof) persists across save/load, and **both companions and a town NPC path through the player-built doorway** | T0 + T1 (pathfinding test + recording) |
| E5 | Both dungeons' required content and the optional third dungeon are completable solo with one companion | T1 (recordings) |
| E6 | The Lantern Warden is killable solo at level 17–18 with correct preparation, and is fatal at level 12 without it | T1 (two recorded attempts, two outcomes) |
| E7 | All 5 magic schools have a build that is viable to level 20 without the other four | T1 + T2 (2 recorded builds) |
| E8 | All 3 professions reach their slice cap through normal play, and each produces at least one item better than the best equivalent dungeon drop | T0 + T1 |
| E9 | Faction choice at epic stage 9 locks and unlocks content as specified, in both directions | T1 |
| E10 | Fast travel is unreachable until an attunement node is found and attuned, verified from a fresh save | T0 |

### 11.2 Technical

| # | Exit criterion | Evidence tier |
|---|---|---|
| E11 | 60 fps at 1080p on the baseline machine while walking the full 2 km road at speed, with 8 exterior cells loaded and a populated town | T1 (frame-time log) |
| E12 | Save file ≤ 2 MB after a 6-hour playthrough, load ≤ 4 s, and a save from content version N loads on N+1 through a migration with no silent drop of moved/deleted definitions (D-05 sparse deltas + atomic writes) | T1 |
| E13 | The full domain test suite (`PROTOTYPE.md` §6.2 plus building persistence, faction reputation, profession progression, modifier pipeline, quest graph and world events) is green in CI in ≤ 5 min with the engine absent, and **no gameplay rule exists in `src/Presentation/**`** | T0 |
| E14 | Tier B→A and C→A NPC transitions produce no observable teleport or illegal state in a 30-minute observation of the town and one road (`RK-07`) | T1 |
| E15 | Content validator reports zero errors across all ~180 YAML files, cross-reference checking catches a deliberately broken definition ID, and adding a new creature, item and recipe requires **zero C# changes** | T0 |
| E16 | A scripted 500-command session through the bus yields the same world-state hash as the same session driven through the UI | T0 |

### 11.3 Design / evidence

| # | Exit criterion | Evidence tier |
|---|---|---|
| E17 | ≥ 3 of 4 testers voluntarily return for a second session **and** can name a memorable event they were never directed to | T2 |
| E18 | Every pillar P1–P8 in §8 has met its stated acceptance test, and no §9.4 kill criterion is triggered | T2 |
| E19 | The slice's fun does not depend on one hand-authored quest: a second, differently-shaped 30-minute quest passes E17's bar (§11.3) in a smaller follow-up test | T2 |
| E20 | A new AI session can, from the repository alone, build, run the tests, launch the slice, and find the epic quest's stage graph in under one hour, with no prior conversation | T1 (observed, timed) |

### 11.4 Explicitly NOT exit criteria

So that Phase 3 is not blocked on the wrong thing: art fidelity, animation quality, audio, UI polish, lore volume, number of items/spells/creatures, and the presence of any deferred system in §10 are **not** blockers. Content volume is Phase 3's *output*, not its *precondition*.

## 12. Assumptions recorded

| ID | Assumption | Reversible? |
|---|---|---|
| VS-1 | The slice is developed on the Phase 1 prototype's architecture without renegotiation. If Phase 2 finds a genuine architectural defect, fixing it is in scope; redesigning the seam is not. | — |
| VS-2 | Content volume is capped at ~180 YAML files. Exceeding it by > 50 % is a scope failure requiring an explicit decision, not a silent drift. | Yes, by decision. |
| VS-3 | The slice's level cap (22) is a slice artifact, not the game's cap. | Yes. |
| VS-4 | Two recruitable companions (not one, not five) is the minimum that makes companion *choice* meaningful — above STEP 16's "a recruitable companion", justified by the epic quest's stage-9 relationship thresholds. Three dungeons (D1 and D2 required, D3 optional) satisfies STEP 16's "2–4" with two on the critical path and one as the alternate-outcome source. | Yes. |
| VS-5 | Tone and naming follow `DECISIONS.md` open question 1's default (melancholic, ancient, low-magic-feeling high fantasy). Names here are provisional and cheap to change while content volume is low. | Yes. |
| VS-6 | The four evaluation pillars that need telemetry (P1, P4, P5, P6) require an in-build telemetry recorder that is local-only, off by default in shipped builds, and never network-transmitting. An implementation task, not a design assumption. | Yes. |
| VS-7 | The sibling Phase-0 documents landed in `docs/` during this writing pass. This document is reconciled to `WORLD_ARCHITECTURE.md`'s canonical **100 m cell**, `20×20 = 400 cells per region`, and separate-interior model, and to `RISK_REGISTER.md`'s `RK-xx` IDs. Where one of them would define a numeric detail, this document states a concrete value and cites the governing `D-xx`. | Reconcile again if those documents change their numbers. |
| VS-8 | The playtest protocol assumes 4 human testers are available. If they are not, the director's own recorded pass plus one independent reviewer satisfies T2 for the *behavioural* criteria but **cannot** satisfy E17, and Phase 3 must not begin on the strength of a single-player evaluation. | **No — this is a real gate.** |
