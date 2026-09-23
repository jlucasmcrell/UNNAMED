# PROTOTYPE.md — Phase 1 Playable Prototype

**Project:** Otherreach (codename UNNAMED) — full-body third-person with seamless first-person zoom, solo-first, open-world fantasy RPG
**Phase:** 1 — Playable Prototype
**Status:** Specification for implementation
**Authority:** `PROJECT_CHARTER.md` is the authoritative creative vision; `DECISIONS.md` records settled architecture (cited by ID, never contradicted here); `PHASE_0.md` STEP 15 defines this prototype's required capability set.

> **This document is not the vertical slice.** `VERTICAL_SLICE.md` is Phase 2 and answers "is this game actually fun?". This document answers one engineering question and nothing else. Per `PHASE_0.md` STEP 15, the prototype must stay SMALL.

## 1. The single question this prototype answers

> **Can the D-02 authority model carry a real RPG session end-to-end — input to domain command to event to view to save file and back — without the seam leaking, without the engine owning truth, and without the domain layer needing the engine to be tested?**

If the answer is no, every later system is built on the wrong foundation. If the answer is yes, all subsequent content is added *through* a proven channel. This is the only question. Fun is explicitly not measured here (§9).

**Second-order questions the prototype also settles, but does not lead with:**

| # | Question | Answered by |
|---|---|---|
| Q2 | Is N=1 enough breadth for the content pipeline (D-03) to be worth it, or does YAML+validator cost more than it saves at this scale? | §5 build story, §7 criteria |
| Q3 | Does a sparse-delta save (D-05) round-trip a *changed* world (mined node, killed wolf, moved companion) rather than only an unchanged one? | §6, §7.4 |
| Q4 | Does a single companion with a 6-state behaviour machine read as "helpful rather than frustrating" at the most primitive level? | §7.3 C16 |

**Hard rule.** If a proposed prototype task is not traceable to a row in §4 or a criterion in §7, it does not go in. Prototype creep is the failure mode §2 exists to prevent.

## 2. Explicit non-goals — what this prototype deliberately does NOT include

Each exclusion is a scope decision, not an oversight. Anything not listed here is also out of scope by default.

| Excluded | Why it is excluded now | Where it lands |
|---|---|---|
| Building / construction (any piece) | D-08 socket assembly is a *system*. It deserves its own proof, and it is not on the STEP 15 list. | Vertical slice |
| Factions and faction reputation | Needs multiple settlements or at least two opposed groups to be meaningful. | Vertical slice |
| Economy depth (regional pricing, supply, merchant specialisation) | One merchant with static prices is enough to prove "buy/sell moves items and coin through commands". | Vertical slice |
| A second region, or world streaming | One 200 m square. Streaming is a Phase 2 risk; proving it early would mask seam bugs. | Vertical slice |
| Dungeons (instanced or interior) | The wolf den is an open cave mouth, not a dungeon. | Vertical slice |
| Bosses, rare spawns, unique world encounters | Needs non-scaling difficulty tuning, which needs a working damage model first. | Vertical slice |
| Mounts, boats, caravans | Pure traversal convenience; teaches nothing about authority. | Phase 3 |
| Day/night cycle, weather, seasons | Time-of-day compiles down to "a number that advances"; a static `world_time` counter is enough to test respawn. | Vertical slice |
| Crime, bounties, guards reacting to the player | Requires faction + settlement state. | Phase 3 |
| Survival meters (hunger, thirst, temperature) | Charter lists survival as *optional* and difficulty-gated; zero authority value. | Undecided |
| Enchanting, augmentation, sockets, durability, curses, set items | One modifier pipeline stage, not six. | Vertical slice |
| Races as a real decision (multiple playable races) | One playable race. Race is a `race_id` field with one row; the seam is what is under test. | Vertical slice |
| Full class/archetype spread, talent trees | Free attribute point spend only (D-09 "attributes = build shape"). No tree UI. | Vertical slice |
| Multiple magic schools | Three schools (Ember, Mend, Ward), one spell each. | Vertical slice |
| Skills as a levelled list | Two skills only, each with a level and a passive effect. | Vertical slice |
| Companions as a roster, companion quests, morale/fear/beliefs | One companion, one follow/wait order. | Vertical slice |
| Multi-stage or branching quests | One quest, four objective types, linear. | Vertical slice |
| Crafting professions as progression (quality tiers, discoveries) | Two recipes, no profession level. | Vertical slice |
| Settlement growth, hireling housing, home defense | Depends on building. | Vertical slice |
| NPC schedules, ecology, abstract regional simulation (D-06 Tier B/C) | Prototype is one cell; all NPCs are Tier A. Seam for tiers exists, tiers are not implemented. | Vertical slice |
| Networking of any kind, LAN, co-op, dedicated server | D-12. Only the command/event seam exists. | Never in Phase 1–2 |
| Difficulty options, accessibility settings, HUD customization, voice, cinematics | Real work, zero architecture value. | Vertical slice |
| Any content beyond §4 | Hundreds of items/spells/recipes/monsters are explicitly forbidden by `PHASE_0.md`. | Phase 3 |
| A second playable build / save slots / cloud saves | One save slot + one rolling backup (D-05) is enough. | Vertical slice |

## 3. World and session shape

| Property | Value |
|---|---|
| Playable area | **200 m × 200 m** (0.04 km²) — **4 exterior cells** at the canonical 100 m cell size (`WORLD_ARCHITECTURE.md` §2), so the prototype exercises the real cell addressing and per-cell delta save from day one rather than a special case. |
| Boundary | Enclosed by authored geometry (a sheer ravine on three sides) plus a movement stop at the edge. No invisible-wall jank, no streaming, no LOD. |
| Named place | *Ashen Hollow* — a shallow valley floor |
| Settlement | **One** outpost, **~40 m × 40 m**, 2 enterable interiors (longhouse, forge shed), 3 NPCs — entirely inside one 100 m cell, `r_0_0:c_00_00` |
| Wilderness | The remaining ~0.035 km²: grass/heath, a treeline, a stream, a rock shelf with iron deposits, a herb patch, and one cave mouth (**the den**) |
| Traversal | ~90 s to walk the full diagonal at base speed; ~35 s from outpost centre to den mouth |
| Verticality | ≤ 6 m elevation change. No climb, no jump-required geometry. Jump exists but is never load-bearing. |
| Save slots | 1 manual + 1 rolling autosave + 1 rolling backup (D-05) |
| Session length | **Target: 18–25 min** for a fresh player to complete the quest. **Hard cap: 40 min** to see everything the prototype contains, including one deliberate save/quit/reload cycle. If a playtester can still find novel content after 40 minutes, the prototype is too big. |
| Total build-to-playable target | **10 working days** of implementation effort, not counting the domain scaffolding already implied by D-02/D-05/D-10. |

## 4. Scope table — the exact minimum content

Justification column is mandatory: **any count above 3 must be argued here or it is reduced.**

### 4.1 Content budget

| Category | Count | Exact items | Why this number, not fewer |
|---|---|---|---|
| Exterior cells | 4 | `r_0_0:c_00_00` … `c_01_01` (4 × 100 m cells = the 200 m square) | 4, not 1, because the real cell key, the real address, and the real per-cell delta path must be exercised; not more, because a fifth cell would only test streaming. |
| Settlement | 1 | Outpost of 3 NPCs, 2 interiors | 0 settlements leaves no safe zone to prove "quest hand-in, craft, rest, save" as a distinct place. |
| Interiors | 2 | longhouse, forge shed | The forge must be a distinct place from the quest giver, or "one crafting station" is not location-gated. |
| Named NPCs | **3** | `npc.keeper_halda` (quest giver), `npc.smith_orren` (crafting station owner + merchant), `npc.warden_kesh` (companion-to-be) | 3 is the minimum that separates *quest giver*, *crafting access*, and *companion recruit* into different characters. Collapsing them into 1 hides the dialogue/relationship seam behind a single entity. |
| Companion | 1 | `npc.warden_kesh` as companion | Charter PHASE 1 and STEP 15 both say one. |
| Enemy archetypes | **1** | `creature.beast.wolf_grey` (level 2) | One archetype proves spawn → AI → damage → death → loot → despawn. A second archetype tests nothing new; the *second* archetype is a vertical-slice job. |
| Enemy spawners | 3 | den wolf pack (4), valley strays (2), one respawning pack (2) at a 20 min `world_time` interval | Respawn is required to prove (a) resources/spawns are driven by simulated time and (b) a mined/killed world state is *persisted as an exception*. |
| Weapons | **3** | `item.weapon.rusted_sword` (1H), `item.weapon.hunting_bow` (2H ranged), plus the starter spell counted under spells | 3 is the minimum that proves melee + ranged are different code paths and that *equipping* changes the resolved attack. 1 weapon would let a hardcoded melee path pass. |
| Ammunition | 1 | `item.ammo.arrow_rough` | A bow with infinite ammo does not prove inventory consumption during combat, which is a mandatory test (§6.2). |
| Armor pieces | 2 | `item.armor.hide_vest` (body), `item.armor.hide_cap` (head) | 2 slots is the minimum proving equipment is a *set of slots*, not a single `equippedItem` field. |
| Spells | **3** | `spell.ember.bolt` (direct damage), `spell.mend.salve` (heal self over 6 s), `spell.ward.oakskin` (10 s armor buff) | 3 is the minimum proving spells are data-driven rows with (a) a damage effect, (b) a healing effect, (c) a timed status effect. A single damage spell could be a hardcoded projectile. |
| Magic schools | **3** | `magic.ember`, `magic.mend`, `magic.ward` — one spell each | 3 is the minimum proving a school is a data row with its own identity, not a colour on a damage number. |
| Skills | 2 | `skill.survival` (gathering yield +1 at level 3), `skill.athletics` (move speed +8 % at level 3) | 2 proves skills are separate from level and from attributes (D-09), and that a skill level *does* something observable. |
| Attributes | **7** | The canonical set from `PROGRESSION.md` §4.1: Might, Endurance, Agility, Precision, Will, Insight, Presence — of which only Might, Endurance and Will have any observable effect at level cap 5. | The *count* is not a prototype scope choice: attributes are persisted per character, so shipping three now and seven later is a save migration, and `DATA_MODEL.md`'s schemas and `VERTICAL_SLICE.md` both commit to the seven. The prototype exercises all seven **in the schema** while only three are **mechanically live**, which is the honest minimum: the plumbing is proven, the balance is not pretended. |
| Levels | Cap 5 | XP curve on config | Level cap 5 keeps the curve testable by hand. |
| Abilities/talents | 0 | — | Deferred to the slice; no talent tree in the prototype. |
| Weapon mastery | 1 track | `mastery.one_handed` only, 3 ranks | 1 track proves the mastery axis exists separately from skill and level without opening a second content surface. |
| Recipes | **2** | `recipe.alchemy.salve_minor` (herb ×2 + 1 flask charge), `recipe.smithing.sword_temper` (iron ×1 + salve_minor ×1 → +2 damage on a *specific item instance*) | 2 is the minimum proving (a) resource consumption and (b) that crafting can mutate an existing item instance rather than only create new ones. A third recipe repeats both. |
| Crafting stations | 1 | `station.forge_shed` (in the forge shed) | Recipes are station-gated by `station_type` from day one, or the gate becomes a refactor. |
| Gathering resources | **2** | `node.herb.ashbloom` (respawns at +1 `world_time` day), `node.ore.iron_seam` (finite, 3 charges, does not respawn in prototype) | 2 proves both respawn classes. Also required: the salve recipe consumes one material and one flask charge, so consumption is non-trivial. |
| Quest | 1 | `quest.hollow.lost_token` (4 objectives, §4.3) | 1 quest with **4 objective types** proves D-07's predicate model generalises; 1 quest with 1 kill-counter proves nothing. |
| Dialogue | 1 conversation graph | `dlg.keeper_halda` (7 nodes, 2 branch points), plus 2 minimal non-branching conversations for the smith and the warden | Branching proof lives in one graph; the rest only need to open and close. |
| Loot sources | 3 | wolf corpse drop table, one placed chest (`cont.den_cache`), quest-item turn-in | Proves drops, containers, and quest-granted items are three different paths. |
| Loot tables | 2 | `loot.wolf_grey` (meat 70 %, hide 45 %, fang 15 %), `loot.den_cache` (iron ×3, arrow ×12, salve ×2, all static) | 1 random + 1 static proves both shapes. |
| Item definitions | **15** total | listed in §4.2 | 15 is the smallest set that covers every category in §4.1 exactly once. |
| Status effects | 3 | `effect.burning` (DoT), `effect.oakskin` (armor buff), `effect.bleeding` (DoT, player+companion) | Bleeding is applied by the wolf; burning by the player spell. |
| Factions | 0 defined, 1 structural slot | `faction_id` field exists on NPCs and is saved; no faction has reputation logic. | Cost of the field is zero; adding it later to a shipped save schema is not. |
| UI panels | 6 | Health/stamina/spell resource bar, interaction prompt, inventory, equipment, character sheet (level/XP/attributes/skills/mastery), journal (quest + objectives) | Journal is required because the quest must be *readable* even in the prototype; it is also where "no quest markers" starts (the journal stores text directions, not waypoints). |

### 4.2 The complete item list (all 15)

| Definition ID | Kind | Notes |
|---|---|---|
| `item.weapon.rusted_sword` | weapon | Starter. 1H. `base_damage: 7`, `reach: 1.8 m`, `attack_time: 0.7 s`. |
| `item.weapon.hunting_bow` | weapon | 2H ranged. `base_damage: 9`, `draw_time: 0.9 s`, requires `item.ammo.arrow_rough`. |
| `item.armor.hide_vest` | armor | Body. `armor: 6`, `weight: 4`. |
| `item.armor.hide_cap` | armor | Head. `armor: 3`, `weight: 1`. |
| `item.consumable.salve_minor` | consumable | Heals 18 HP over 6 s. Also a crafting *ingredient* for `sword_temper`. |
| `item.ammo.arrow_rough` | ammo | Stack of 20. Consumed on fire. |
| `item.material.herb_ashbloom` | material | Gathered. |
| `item.material.iron_ore` | material | Gathered. |
| `item.material.iron_ingot` | material | Not craftable in prototype; exists to prove materials have tiers and to feed `sword_temper` from the chest. |
| `item.material.wolf_hide` | material | Drop. |
| `item.material.raw_meat` | material | Drop. Used for the companion's one "feed to raise trust" interaction. |
| `item.trinket.wolf_fang` | trinket | Drop. `+2 %` crit. Proves a non-armor, non-weapon equipment slot exists. |
| `item.quest.halda_token` | quest item | `no_drop: true`, `no_sell: true`, unique instance. |
| `item.tome.ember_primer` | book | Read once → grants `spell.ember.bolt` + `spell.ward.oakskin`. Proves content can grant capabilities. |
| `item.tool.water_flask` | tool | 1 charge; refillable at the stream (a `refill` interaction, not a node). Consumed by `salve_minor`. Proves a *tool* item class distinct from material and consumable, and gates the first recipe behind having found the stream. |

### 4.3 The one quest — `quest.hollow.lost_token`

Structure is declarative (D-07). Each objective is a `type` + parameters; state lives in the world-state store; the journal generates its text from the content file.

| # | Objective | Type | Predicate |
|---|---|---|---|
| O1 | "Hear what Halda wants." | `talk_to` | `npc.keeper_halda` conversation node `dlg.keeper_halda.offer` visited |
| O2 | "Find where the wolves den." | `visit_location` | player enters `loc.den_mouth` radius (8 m) → fires `location.discovered` |
| O3 | "Recover Halda's token from the pack." | `acquire_item` | instance of `item.quest.halda_token` is in player inventory, AND `creature.beast.wolf_grey` kill count at `loc.den_mouth` ≥ 3 |
| O4 | "Bring Halda a salve for her brother's leg." | `craft` + `deliver` | `recipe.alchemy.salve_minor` crafted ≥ 1 since O1, AND 1× `item.consumable.salve_minor` handed to `npc.keeper_halda` |

**Rewards:** 220 XP (levels a fresh character from 1 → 2), 40 coin, `item.armor.hide_cap`, and `relationship.keeper_halda +10` (stored, not yet surfaced beyond a dialogue line change).

**Note on lever design:** `recipe.smithing.sword_temper` requires `item.material.iron_ingot`, which drops in **no** loot table except `cont.den_cache`. The good sword upgrade is therefore gated behind opening the chest, which chains exploration → combat → loot → crafting → combat in one line.

**What the quest deliberately is not:** not branching, not timed, not fail-able, not multi-stage, not one of "several quests". `PHASE_0.md` STEP 15 says *one quest*; the charter's PHASE 1 bullet says *several quests*. Where these disagree, the stricter prototype instruction wins, and the "several" requirement is discharged in the vertical slice. **Recorded as assumption A-1 (§10).**

### 4.4 Content file layout (D-03)

```
content/
  items/        15 files (or one file per kind, one entry each)
  creatures/    wolf_grey.yaml
  spawns/       3 spawner definitions
  spells/       3        skills/       2        recipes/      2
  nodes/        2        loot/         2 tables
  quests/       hollow_lost_token.yaml
  dialogue/     3 graphs
  locations/    4 named locations (outpost, den_mouth, iron_shelf, herb_patch)
  config/       xp_curve, level_cap, base_speeds, damage_constants
```

39 definitions total. No other content directory may exist in Phase 1; the validator (§6.3) fails the build on an unknown top-level content kind.

## 5. Core loop the player actually performs

The prototype's loop is a *horizontal* version of the final game's explore → fight → progress → gather → craft → quest → return chain (`PHASE_0.md` STEP 2), compressed into one session.

| Step | Player action | Systems touched | State written |
|---|---|---|---|
| 1 | Spawns at the outpost gate, first-person, sword equipped, no marker on screen | presentation bootstrap, `EquipmentSystem`, journal | save manifest created on first autosave |
| 2 | Walks into the longhouse, reads the journal's one line of direction | `InteractionSystem`, dialogue | — |
| 3 | Talks to Halda; picks **1 of 2** opening replies | `DialogueSystem`, `QuestSystem` (O1) | conversation node visited, quest accepted |
| 4 | Reads journal: *"the den is northwest, up the rock shelf above the stream."* Walks out. No waypoint. | journal only | — |
| 5 | Gathers 2× ashbloom and refills the water flask at the stream | `GatheringSystem`, `InventorySystem` | node charge count decremented; `world_time` recorded per node |
| 6 | Hears a wolf; fights 1 stray wolf with the sword. Takes damage. Uses `ember.bolt` to finish it at range. | `CombatSystem`, `DamageSystem`, `StatusEffectSystem`, `LootSystem` | wolf entity → dead; corpse lootable; XP granted; `mastery.one_handed` +1 tick |
| 7 | Fights the den pack of 4; dies once (deliberate tuning — the first attempt with a level-1 character and a rusted sword is intended to be survivable but costly) | `DeathSystem` | death event: −10 % XP debt (not lost XP), respawn at outpost, 60 s of `effect.weakened` |
| 8 | Recovers corpse-less death penalty, buys nothing, kills the pack, loots 3 wolves + the den cache | `InventorySystem`, `ContainerSystem`, `LootSystem` | chest entity marked emptied; wolf corpses removed |
| 9 | Returns to the outpost; recruits Kesh (a companion conversation with one accept/decline branch) | `CompanionSystem`, `RelationshipSystem` | companion entity created with ULID, `state: following` |
| 10 | Crafts `salve_minor` at the forge (2 herb + 1 water consumed) and `sword_temper` on the equipped sword (1 ingot + 1 salve consumed) | `CraftingSystem`, `ResourceSystem` | recipe crafted; **specific sword instance** gains `+2 damage` modifier |
| 11 | Hands the salve to Halda, completing O4 and the quest | `QuestSystem`, `DialogueSystem` | quest state → completed; rewards granted; relationship +10 |
| 12 | Saves, quits to desktop, relaunches, loads | `SaveSystem` (D-05) | §7.4 must hold exactly |

**Target session:** 18–25 min. **Micro-loop targets:** one wolf fight 25–45 s; one gather run 30–60 s; one craft 5–10 s of UI; one dialogue 20–40 s. Nothing in the prototype should take longer than 60 s without a state change the player can see.

**Exploitable shortcuts identified now, and the prototype's answer:**

| Shortcut | Prototype answer |
|---|---|
| Farm the 2-wolf respawner forever for XP | XP per kill falls off after 5 kills of the same archetype within one `world_time` day (`diminishing_returns` config, values are prototype-placeholder). Enough to prove the hook exists. |
| Sell the quest item for coin | `no_sell: true` on `item.quest.halda_token`. |
| Craft → sell → craft loop | Merchant buys crafted goods at 40 % of value; ingredients cost more than that. One-line config, proves the valve exists. |
| Skip combat by kiting wolves into the outpost | NPCs are non-combatants and take no damage in the prototype; wolves leash at 35 m from spawn. Documented limitation. |

## 6. Build / run / test story (D-01, D-02)

### 6.1 Solution structure

```
repo/
  src/Domain/            no Godot reference; engine-agnostic C#  ← D-02
  src/Application/       no Godot reference; command/event bus, scheduling, save orchestration
  src/Presentation/      Godot 4.x C# project; references Domain + Application
  tests/Domain.Tests/    xUnit; runs headless with `dotnet test`
  tests/Content.Tests/   content validation gates
  content/               YAML (D-03), kind list per DATA_MODEL.md §1
  tools/content-lint/    schema dump + ID cross-reference checker
```

The Domain `.csproj` has **no Godot package reference**. A presentation type used in the domain is a *compile error*, not a code-review note (D-02).

### 6.2 What runs headlessly (no engine process)

`dotnet test tests/Domain.Tests` — must run in CI on every commit, in under 60 s:

| Test group | Covers |
|---|---|
| Movement & collision-independent | Player position is a domain value; move commands integrate against a stub collision oracle. |
| Combat resolution | Damage formula determinism, armor mitigation, crit, resistances, stagger threshold, reach gating. |
| Status effects | Apply/stack/tick/expire against domain `world_time`. |
| Death | Death event, penalty application, respawn state, XP debt arithmetic. |
| XP & leveling | Curve boundaries (no off-by-one at the exact threshold), multi-level-in-one-grant, attribute point grant. |
| Inventory transfers | Move, split stack, merge stack, drop, pick up, swap, capacity/weight refusal — including *refusal does not mutate*. |
| Equipment | Equip/unequip by slot; swapping returns the old item to inventory; two-handed conflicts with off-hand; `sword_temper` modifier attaches to an instance, not a definition. |
| Crafting | Recipe requirement check, resource consumption (exact counts), output instance creation, station gating, failure leaves inputs intact. |
| Resource consumption | Node charge decrement, node depletion event, respawn scheduling against `world_time`. |
| Quest state | O1–O4 predicate evaluation, ordering, idempotency (re-firing a satisfied objective changes nothing), completion, reward grant exactly once. |
| Companion state | Follow/wait transition rules, distance-based auto-catch-up, death and revive, persistence of the behaviour state. |
| NPC state | Named NPC alive/dead, relationship delta application and clamping, conversation-node-visited set. |
| Loot generation | Seeded table rolls are deterministic; N rolls from seed S produce an exact expected multiset; 10 000 rolls stay within statistical bounds. |
| Item uniqueness | Every generated instance receives a distinct ULID (D-04); a definition is never used as instance identity. |
| Save/load round-trip | Domain-only: serialize → deserialize → deep-compare, including a *changed* world (deltas), and a corruption-truncated save rejected cleanly with the backup loaded. |
| Registry (D-10) | ULID assignment at spawn, lookup by instance ID, no entity without an ID, no gameplay rules in the registry. |

### 6.3 What requires the engine

| Test | How | Cadence |
|---|---|---|
| Content validation pass (D-03) | `godot --headless --script res://tools/validate_content.cs` — every definition parses against its C# schema; every referenced ID resolves; every location/creature/item ID used in a quest or loot table exists. Failure prints `file:line`. | Every commit |
| Boot smoke | `godot --headless --quit-after 300` — project boots, content loads, a headless world is constructed, no errors in the log. | Every commit |
| View-subscription test | Headless: subscribe a stub view to the event bus, run a scripted 200-command session, assert the view saw exactly the events it should and wrote nothing. (D-11 enforcement.) | Every commit |
| Frame budget | Windowed run, scripted camera path through the hollow, log frame times. Target: ≥ 60 fps at 1080p on the baseline machine (DECISIONS open question 2 default). | Weekly |
| Input→command latency | Instrumented: timestamp input, timestamp domain event. Budget ≤ 1 frame + 8 ms. | Weekly |

### 6.4 Which of the charter's mandatory tests are in scope

The charter's TESTING section lists 16 systems. The prototype covers **13 in full** — save/load, inventory transfers, equipment, death, XP, leveling, crafting, resource consumption, quest state, companion state, NPC state, loot generation, item uniqueness — all in §6.2, with save/load additionally accepted manually per §7.4.

The remaining three are **explicitly out of scope**, and the prototype does not claim them:

| Charter test | Status | Why |
|---|---|---|
| Relationship changes | **Partial** | Deltas are stored, clamped, and saved; the only observable effect is one dialogue line. No relationship *system* exists yet. |
| Faction reputation | **No** | 0 factions in the prototype (§4.1). The `faction_id` field is in the save schema from day one so the schema does not break later. **Assumption A-2 (§10).** |
| Building persistence | **No** | Building is a vertical-slice system (D-08). |

The two "No" rows are the *first two tasks* of the vertical slice, not prototype debt.

## 7. Success criteria (observable and falsifiable)

No criterion in this section may be subjective. "Feels fun", "feels responsive", and "looks good" are **not** prototype criteria; they belong to `VERTICAL_SLICE.md` §8.

### 7.1 Architecture criteria

| ID | Criterion | Verified by |
|---|---|---|
| C1 | `src/Domain` compiles with zero references to Godot assembly types. | CI: build + `grep` for the Godot namespace in `src/Domain/**` returns zero matches. |
| C2 | `dotnet test tests/Domain.Tests` is green and completes in ≤ 60 s with no engine process running. | CI log. |
| C3 | A scripted 200-command session delivered only through the command bus produces the same final world-state hash as the same session delivered through the UI, on the same seed. | Headless harness prints both hashes; they must be equal. |
| C4 | No view writes authoritative state: a static-analysis pass finds zero assignments to domain state from `src/Presentation/**`, and the stub-view test (§6.3) passes. | CI. |
| C5 | Every runtime instance in the final world state has a ULID instance ID, and no two are equal (D-04). | Assertion at save time; also a test over a 10 000-entity stress world. |

### 7.2 Content pipeline criteria

| ID | Criterion | Verified by |
|---|---|---|
| C6 | Deleting a required field from any one of the 15 item files fails the content validation with `file:line`, and the game refuses to boot rather than crashing later. | Manual: deliberately break one file, run validator, restore. |
| C7 | Adding one new item requires editing exactly one YAML file and **zero** C# files. | Timed exercise: add `item.material.wolf_hide_coarse`, run it in the world, revert. |
| C8 | All 39 content definitions (15 item, 3 spell, 2 recipe, 2 node, 1 creature, 3 NPC, 3 dialogue, 2 loot, 1 quest, 2 skill, 5 config) load with zero validator warnings. | Validator output. |

### 7.3 Gameplay criteria (each is a fresh-session acceptance test)

A "fresh session" = delete all saves, launch, no console commands, no debug spawns.

| ID | A fresh session must be able to… | Falsified if |
|---|---|---|
| C9 | walk from the outpost to the den mouth and back without falling through geometry, getting stuck, or needing a noclip. | Any one run stalls > 30 s on a non-obstacle, or the player leaves the 200 m square. |
| C10 | kill the den pack using only sword, bow, and the three spells, and the three spells must each be *used* to succeed under the intended tuning. | Any spell is never needed, or one spell alone trivialises the pack. |
| C11 | reach level 2 from quest turn-in alone, and observe the attribute point become spendable and the level-up change a displayed stat. | XP from the quest alone is insufficient, or the attribute point has no visible effect. |
| C12 | gather both resource types, watch a herb node refill after one in-game day, and watch the iron seam *not* refill. | Either node behaves like the other. |
| C13 | craft both recipes, and observe the ingredient counts in the inventory drop by exactly the recipe cost. | Off-by-one, or a failed craft consumes inputs. |
| C14 | see the sword's damage number change after `sword_temper` while a second, untempered sword's does not. | The modifier lands on the definition instead of the instance. |
| C15 | complete `quest.hollow.lost_token` — all four objectives — in ≤ 30 min of play, with the journal tracking each objective as it completes. | Any objective cannot be completed, or completes without its predicate being true. |
| C16 | recruit Kesh, then order follow → wait → follow, and have Kesh path around the longhouse without getting permanently stuck. | Kesh requires > 15 s to recover from any single geometry snag. |
| C17 | die at least once and respawn with the stated penalty applied exactly once (XP debt arithmetic visible in the character sheet). | Penalty applied twice, or not at all, or lost XP instead of XP debt. |
| C18 | see every enemy, node, and item definition reachable in the world without a debug command. | Any of the 15 items is unobtainable in a fresh session. |

### 7.4 Save/load round-trip criterion (the one that matters most)

A save must round-trip this **exact** scenario, asserted field-by-field:

1. Accept the quest (O1 complete), discover the den (O2 complete).
2. Kill 2 of the 4 den wolves; loot one corpse but **leave the other corpse unlooted and unlooted corpses must persist**.
3. Mine the iron seam **once** (2 charges remaining).
4. Pick up 3× ashbloom, so the herb node has 0 remaining and is respawn-scheduled.
5. Recruit Kesh; set Kesh to `wait` at a specific world position ~18 m from the player.
6. Temper the equipped sword (+2 damage) and equip the hide cap; leave the hide vest in the chest.
7. Open the den chest and take **one** of its three items.
8. Purchase one arrow stack from the smith.
9. **Save, quit to desktop, relaunch, load.**

After load, all of the following must be true, and the prototype fails if any is false:

- Player position, facing, health, stamina, and spell resource are equal to the values at save time.
- Inventory contents (counts, order-insensitive multiset) and equipped slots are identical.
- The tempered sword has the modifier; the other sword does not.
- The den chest contains exactly the two items left behind, at the same container entity ID.
- The iron seam has 2 charges; the herb node has 0 charges with a respawn scheduled at the same `world_time`.
- 2 wolves at the den are alive at their spawn-adjacent positions; 1 corpse is present and unlooted; 1 wolf entity is gone.
- Kesh exists, is at the same position, in `wait`.
- Quest state places O1 and O2 complete, O3 in progress with the exact kill/acquire counters.
- XP, level, attribute spends, skill levels, and `mastery.one_handed` ticks are identical.
- `relationship.keeper_halda` is identical.
- `world_time` is identical (not reset to 0, not advanced by the load).
- Save load completes in ≤ 2 s on the baseline machine, and the save file is ≤ 256 KB for this state (D-05 sparse deltas).
- The **backup** slot is a valid, loadable save from one autosave earlier, not a copy of the current one.
- A deliberately truncated save file produces a clean "save is corrupt, load backup?" path, never a crash (D-05).

### 7.5 Anti-criteria (the prototype fails if any of these happen)

Any gameplay rule exists in `src/Presentation/**`; save/load needs "and then we recompute X" for any field; an item, quest, or spell ID is hardcoded in C#; a new content *type* requires code (C7); or any vertical-slice system is implemented "while we're here". Each of these means the failure has already happened upstream, not that the criterion is too strict.

## 8. Technical risk this prototype retires first

Per `PHASE_0.md` STEP 17, the dependency order is: identity and registry → content definitions and validation → domain state store and command bus → a single vertical thread through the loop → breadth. **The prototype exists to retire the risk at the front of that order, before anything is built on it.**

**Primary risk retired: `RK-09` — "local-player assumptions silently destroy the server-shaped seam" (`RISK_REGISTER.md`), expressed as the temptation to abandon D-02/D-11 under Phase-1 pressure.**

Why it is first:

1. It is the only risk in the project that, if wrong, requires **rewriting every other system**. Direct scene-object authority would be faster to prototype and is exactly what D-02 rejected; the temptation peaks in Phase 1 and is documented in D-02's consequences ("this will feel like overhead during Phase 1 and *must not* be abandoned because of that feeling").
2. It is cheap to falsify *now* and expensive to falsify in Phase 2. A 200 m square with 15 items either round-trips through commands and deltas or it does not; a 2×2 km region will not be more forgiving.
3. It is the prerequisite for headless testing, which is the property D-01 selected Godot for. If the seam is not testable without the engine, the engine choice loses its main advantage.

**The implementing order that retires it (this is the prototype's critical path):**

| Step | Deliverable | Retires |
|---|---|---|
| P1 | `src/Domain`: Entity Registry (D-10) + ULID assignment + `WorldStateStore` | C5 |
| P2 | Content loader + schema validator + `content/` with **1** item; the `RK-01` cell-hash determinism test passes twice from one seed | C6, C8, D-03 proven at N=1 |
| P3 | Command bus + event bus + one command end-to-end (`MovePlayer`) with a stub view | C3, C4, D-02 proven |
| P4 | Sparse-delta save/load of the *unchanged* world, then of a changed cell | C2, D-05 proven |
| P5 | **The vertical thread:** one item, one node, one wolf, one command each for gather/kill/loot/equip → save → load, all headless | C1–C5 |
| P6 | Godot presentation shell: first-person controller, one view per system, content-driven spawn | D-11 proven under real input latency |
| P7 | Breadth: the remaining 14 items, 3 spells, 2 recipes, 1 quest, 3 NPCs, 1 companion | §4 complete |
| P8 | Death, XP/level, skills, mastery | §7.3 |
| P9 | Acceptance run per §7.3 and §7.4, recorded | §9 |

Steps P1–P5 involve **no Godot gameplay code at all** and should be completed and green in `dotnet test` before P6 begins. If P5 cannot be made to pass, the correct response is to fix the seam — not to skip to P6.

**Explicitly not this prototype's risk** (tracked in `RISK_REGISTER.md` / `WORLD_ARCHITECTURE.md`, owned by the vertical slice, and the prototype must not be blamed for them): `RK-02` frame budget in a 2×2 km region, `RK-01` cross-version determinism beyond one generation, `RK-06` building persistence and navigability, `RK-05` companion AI quality, `RK-08` content scale beyond 39 definitions.

## 9. What the prototype does not prove — and definition of done

This list exists so that "the prototype is done" is never mistaken for "the game works." The prototype does **not** prove: whether the game is fun (nothing here measures enjoyment; see `VERTICAL_SLICE.md`); whether a 2×2 km region streams at 60 fps (D-01 revisit trigger 1 is **not** evaluated here); whether the world feels like it does not revolve around the player (a 200 m square with 3 NPCs cannot make that claim); whether combat is readable at speed or enemy families are distinct; whether building, factions, economy, or companions-as-a-roster are implementable; whether content authoring stays pleasant at 500+ definitions (C7 proves the *mechanism*, not the *experience*); or whether the save schema survives a content-version change — the prototype must *have* a `schema_version` (the gameplay-state version that drives the migration chain — **not** `save_format`, which is the container-layout version) and a migration hook (D-05) without inventing migrations for content that does not exist yet.

The prototype is **done** when, and only when:

1. All §7.1 and §7.2 criteria pass in CI; all §7.3 criteria pass in a recorded fresh-session run.
2. §7.4 passes field-by-field, recorded as a diff of expected vs. actual world-state dump.
3. No §7.5 anti-criterion is true.
4. The build runs at ≥ 60 fps on the scripted camera path on the baseline machine.
5. A `README.md` **written during Phase 1, not Phase 0** states how to build, run the headless tests, run the content validator, and play the prototype in under one page — because the next session is an AI session that must resume without asking. (At the end of Phase 0 no such file exists anywhere in the repository and none is expected: `G:\UNNAMED` contains only `docs\`.)
6. A single recorded (video or log) **10-minute scripted playthrough** exists that a third party can replay step-by-step to the same end state.

Anything not on this list is not a prototype blocker. Art, audio, animation quality, and UI polish are explicitly **not** definition-of-done items.

## 10. Assumptions recorded

| ID | Assumption | Reversible? |
|---|---|---|
| A-1 | `PHASE_0.md` STEP 15's "one quest" overrides `PROJECT_CHARTER.md` PHASE 1's "several quests" for the prototype. The prototype ships one quest with four objective types. | Yes — add quests without changing the framework. |
| A-2 | Faction reputation is out of prototype scope (`PHASE_0.md` STEP 15 does not list it), but `faction_id` appears in the NPC and save schemas from day one so no save migration is needed later. Relationship changes are stored and clamped with exactly one observable effect (a dialogue line); the relationship *system* is vertical-slice work. | Schema field cost is near zero. |
| A-3 | The prototype uses one playable race and free attribute spending only; no class, no talent tree. Archetypes are a slice deliverable. | Yes. |
| A-4 | Tone follows `DECISIONS.md` open question 1's default (melancholic, ancient, low-magic-feeling high fantasy); baseline is open question 2's default (Windows, 16 GB RAM, mid-range discrete GPU). Only names, the frame budget, and load times depend on these. | Yes, cheap while content volume is ~0. |
| A-5 | Numeric tuning values in §4.2 and §7.3 (damage, XP curve, respawn intervals) are *placeholders with a stated shape*; they will be set empirically in P8 and are expected to change. The criteria they appear in test *behaviour*, not the exact numbers, except where the number is the test. | Yes, by design. |
| A-6 | The sibling Phase-0 documents (`ARCHITECTURE.md`, `PERSISTENCE.md`, `WORLD_ARCHITECTURE.md`, `RISK_REGISTER.md`, `ROADMAP.md`, `PROGRESSION.md`, `SYSTEMS.md`) landed in `docs/` during this writing pass. This document is reconciled to `WORLD_ARCHITECTURE.md`'s canonical **100 m cell** and separate-interior model and to `RISK_REGISTER.md`'s `RK-xx` IDs; where they would still define a numeric detail, this document states a concrete value and cites the governing `D-xx`. | Reconcile again if those documents change their numbers. |

