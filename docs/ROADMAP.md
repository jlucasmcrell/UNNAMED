# ROADMAP.md — Incremental Development Roadmap

**Project:** Otherreach (codename UNNAMED) — full-body third-person with seamless first-person zoom, solo-first, open-world fantasy RPG
**Phase:** 0 — Vision and Architecture (STEP 17, plus STEP 1/STEP 15/STEP 16 sequencing)
**Authority:** `PROJECT_CHARTER.md` is the authoritative creative vision. `DECISIONS.md` records settled decisions. Where this document and the charter disagree, the charter wins and this document is wrong.

**Reader assumption:** another AI coding session implementing from these documents alone. Milestone IDs (`M0`..`M16`) are stable and citable. Every milestone names its entry criteria (what must already be demonstrably true), its exit criteria (what must be demonstrably true to leave it), its proof (the artifact that proves it), and its class: **FEATURE**, **RISK SPIKE**, **INFRA**, or **GATE**.

---

## 1. Governing Rules

| ID | Rule | Source |
|---|---|---|
| `R-1` | **Playable throughout.** Every milestone on the critical path ends with a runnable build merged to the main branch, a tagged build number, and a green smoke test. A milestone that leaves the build unplayable for more than one working session is a process failure, not a trade-off. | Charter "DEVELOPMENT PHILOSOPHY" |
| `R-2` | **Vertical, not horizontal.** Three working weapons beat architecture for 500. Five excellent creatures beat a spreadsheet of 200. One dungeon beats plans for fifty. | Charter "DEVELOPMENT PHILOSOPHY" |
| `R-3` | **Dependency order is a hard constraint.** Crafting cannot precede items. Epic quests cannot precede the quest framework. Settlement simulation cannot precede NPC persistence. A milestone may consume only interfaces that already exist and pass their exit criteria. | PHASE_0 STEP 17 |
| `R-4` | **Gates require owner review.** Marked `GATE`. No work on the next milestone begins until the owner has answered the gate question. Gates are placed where a wrong answer is expensive to reverse. | This document |
| `R-5` | **Cheap validation first.** Every high-risk assumption gets a `RISK SPIKE` milestone sized at ≤1 week that can *fail* and still be useful, before any feature depends on it. | PHASE_0 STEP 18 |
| `R-6` | **No MMO infrastructure.** No networking, no server authority, no concurrency persistence format. Only the four extension points named in D-12 are preserved: command-based mutation (D-02), stable identities (D-04), sparse versioned persistence (D-05), tiered simulation (D-06). | D-12 |
| `R-7` | **Infrastructure is scheduled where it is needed, never "later".** Content validation (D-03), the quest debugger (D-07), the save migration harness (D-05), and the headless test harness (D-01) each appear on the critical path at the milestone that first depends on them. | D-01, D-03, D-05, D-07 |
| `R-8` | **One region completely before a second region.** | D-12 |

---

## 2. Dependency Graph

Solid arrows are hard dependencies; the letters reference the rules that make them hard.

```text
M0 Repo + Architecture docs                                    [GATE]
 └─> M1 Domain skeleton + headless test harness + CI          [INFRA]
      ├─> M1b Content validation tooling (schema + ID xref)   [INFRA]  (D-03)
      ├─> M2 Entity Registry + identity + persistence baseline[INFRA]
      │    └─> M2b Save migration harness                     [INFRA]  (D-05)
      └─> M2c Progression spine: XP/level/attributes            [FEATURE] (D-09)
           └─> M3 Player, movement, interaction, world cells    [FEATURE]
                ├─> M3b Item/equipment/inventory                [FEATURE]
                │    └─> M3c Combat core + damage + status      [FEATURE]
                │         └─> M3d Enemy creatures + loot tables [FEATURE]
                │              └─> M3e Basic magic (1 school)   [FEATURE]
                │                   └─> M3f Skills + one        [FEATURE]
                │                       profession (gather+craft)
                │                        └─> M4 Settlement + NPC
                │                            persistence + dialogue
                │                             └─> M5 Quest framework
                │                                 + QUEST DEBUGGER
                │                                  └─> M6 Companion v1
                │                                       │  ══ PHASE 1 ENDS ══
                │                                       │  (early feel test here —
                │                                       │   cheap fun verdict)
                │                                       └─> M7 Factions + reputation
                │                                            + Building v1   [PHASE 2]
                │                                             └─> M8 Dungeon + boss
                │                                                  + multi-stage quest
                │                                                   └─> M9 VERTICAL SLICE
                │                                                       [GATE — fun at
                │                                                        full content scale]
                │                                                        └─> M10..M16
                └─> M3g Streaming cells + LOD + simulation tiers [RISK SPIKE]
```

**Explicitly forbidden early work** (R-3): crafting before `M3b` items; epic/artefact quests before `M5`; settlement simulation or NPC schedules before `M4`; player building before `M4` NPC persistence (companions and settlement NPCs must be able to path through/around structures — D-08); mass content authoring before `M1b` validation; any networking before `M15`.

---

## 3. Critical Path

```text
PHASE 1 (playable prototype)
M0 → M1 → M1b/M2/M2b/M2c → M3 → M3b → M3c → M3d → M3e → M3f
   → M4 → M5 → M6   [early feel test — fun question asked cheaply here]

PHASE 2 (vertical slice)
M7 → M8 → M9  → M10 → M12 → M14   [M9 = fun GATE at full content scale]
```

**Phase boundary.** Phase 1 ends at `M6` and is exactly the prototype scope in `PROTOTYPE.md`. `M7` and `M8` are Phase-2 work. An earlier draft of this roadmap ran `M7`/`M8` inside Phase 1, which three other documents contradict and which `RISK_REGISTER.md` would have scored as a Phase-1 failure.

Rationale for each link:

| Link | Why it is critical |
|---|---|
| `M1 → M1b` | Content is data (D-03). Without the validator, every later milestone can ship broken references that surface as mid-quest null crashes — the exact failure D-03 exists to prevent |
| `M1 → M2` | Identity must exist before anything persistent exists; retrofitting instance IDs onto live save state is the expensive version (D-04) |
| `M2 → M2b` | Save versioning must exist before there is save data worth migrating. The migration harness retrofitted after 6 months of schema drift is the single most expensive deferral on this list (D-05) |
| `M2 → M2c` | Progression math is pure domain logic and must be unit-testable with no engine in the loop (D-01, D-02); it shapes combat design in `M3c` |
| `M3b → M3c` | Damage needs items, materials, resistances, and equipment before it can be tuned |
| `M3d → M3e` | Magic needs damage types and status effects to be meaningful (Charter §6) |
| `M3f → M4` | Crafting/harvesting output must be consumable by an economy before NPCs can trade in it |
| `M4 → M5` | Quest objectives are predicates over *world state*, and NPC state is most of that state (D-07) |
| `M5 → M6` | Companion personal quests and affinity changes are quest objectives |
| `M4 → M7` | Snap building guarantees navigability; navigability is only testable once NPCs and companions actually path (D-08) |
| `M7/M8 → M9` | The vertical slice is the integration test of everything above |
| `M9 → M10` | Region 2 and settlement simulation must not precede the slice proving the loop is fun (Charter PHASE 3 gating) |

**Off-critical-path (parallelisable) tracks**, allowed to start once their first dependency clears: art/audio pipeline (after `M1`), animation set for the three Phase-1 weapons (after `M3c`), UI shell/HUD (after `M3`), accessibility options (after `M3`), telemetry instrumentation (after `M2c`).

---

## 4. Milestones

### M0 — Repository, Architecture, and Decision Lock — `GATE`

- **Class:** GATE. **Depends on:** nothing.
- **Entry:** Phase-0 documents exist and are consistent with each other.
- **Work:** Establish the repository structure mandated by D-02 (`src/Domain/` and `src/Application/` engine-agnostic C#, `src/Presentation/` Godot-only, separate `.csproj` with no Godot reference so a boundary violation fails to compile; tests under top-level `tests/`), `content/` YAML tree (D-03), `docs/`, CI config. Confirm the settled decision set D-01..D-12 as frozen. Confirm the four open owner questions in `DECISIONS.md` (tone, platform baseline, fidelity target, "while you were away" policy) are answered or defaults accepted.
- **Exit criteria:** Empty solution builds; a placeholder domain unit test passes headlessly in CI; the presentation project cannot reference domain-internal state; owner has answered the gate question.
- **Proof:** Green CI run on an empty repository.
- **Gate question:** *Is the architecture (engine, authority model, data-driven content, identity, persistence shape, tiered simulation) accepted for a multi-year commitment?* A "no" here is cheap. A "no" at M9 is not.

### M1 — Domain Skeleton, Headless Test Harness, CI — `INFRA`

- **Class:** INFRA. **Depends on:** M0.
- **Entry:** M0 gate passed; engine installed; CI runner available.
- **Work:** `src/Domain/` projects (`Core`, `Progression`, `Items`, `Combat`, `Quests`, `World`, `Persistence`); `src/Application/` project holding the command/event bus (D-02) as a synchronous in-process implementation with a deterministic test scheduler; `IWorldStateStore` seam; test harness in `tests/Domain.Tests/` that constructs a world, runs N deterministic command-ticks, and asserts on events; CI that runs build + domain tests on every change with no engine binary in the loop.
- **Exit criteria:** A `PickUpItem` command flows command → system → event and the test asserts the resulting event sequence with no Godot types referenced anywhere in `src/Domain/`. Test suite runs in under 30 seconds.
- **Proof:** CI green; `src/Domain/*.csproj` has zero Godot references; a sample test file demonstrating the pattern for later sessions to copy.
- **Notes:** D-02 explicitly warns that this indirection *will feel like overhead during Phase 1 and must not be abandoned because of that feeling*. This milestone is where that temptation first appears. Do not dissolve the boundary.

### M1b — Content Validation Tooling — `INFRA`

- **Class:** INFRA. **Depends on:** M1. **Required by:** every content milestone.
- **Entry:** Domain skeleton builds; YAML loader stub exists.
- **Work:** Schema definitions for the D-03/STEP-7 content kinds; loader that fails with **file and line** on schema violation; definition-ID cross-reference check (every referenced ID exists); duplicate-ID detection; definition-ID immutability check against a committed registry of shipped IDs (D-04 — renaming requires a migration map); CLI targets `content:lint`, `content:schema-dump`, `content:xref`.
- **Exit criteria:** Introducing a typo, a duplicate ID, and a dangling reference each produce a distinct, actionable, line-numbered error; CI fails on all three; content hot-reload works in developer builds (D-03).
- **Proof:** Three intentionally broken fixture files, each caught with a readable error, in CI.
- **Why here:** D-03 states this validator "is a Phase-1 deliverable, not a 'later' task". Placing it after content authoring begins means every content milestone ships latent reference bugs.

### M2 — Entity Registry, Identity, Persistence Baseline — `INFRA`

- **Class:** INFRA (+ one `RISK SPIKE` inside). **Depends on:** M1, M1b.
- **Entry:** Domain skeleton and content loader pass.
- **Work:** `EntityRegistry` (D-10) owning creation, ULID assignment, and lookup for every runtime instance, with **no gameplay rules**; definition-ID vs instance-ID namespaces (D-04); deterministic baseline world generation behind a frozen interface; sparse-delta save (manifest + player state + changed cells) with atomic write (staging + rename + verify) and rolling backups (D-05; two generations per `PERSISTENCE.md` §7.3); corruption-recovery path; autosave/manual save slots.
- **`RISK SPIKE` inside:** *determinism*. Generate a cell twice from the same seed and diff. Generate from `(seed, v1)` then `(seed, v2)` and confirm the version-mismatch path triggers migration rather than silent regeneration. Fail-fast if generation is not bit-stable.
- **Exit criteria:** Save in a 10-cell world with 2 changed cells and verify the save contains only the deltas; load it and verify world equality; verify `(seed, content_version)` determinism across two fresh processes; verify atomic write survives a simulated kill during write. (Met; `M2_STATUS.md`. M2b later removed content identity from generation, so the determinism under test is of the baseline tuple, `PERSISTENCE.md` §1.3.)
- **Proof:** `RK-01` (determinism) has a measured answer, not an assumption. D-05 calls this "the single most dangerous constraint in the project"; this milestone is where it is proven or the design changes.

### M2b — Save Migration Harness and Baseline Compatibility — `INFRA`

- **Class:** INFRA. **Depends on:** M2. **Must exist before any schema change ships.**
- **Refined by** `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md` (owner-approved), after M2 found that the whole `content_hash` seeded every cell's random draws, so a one-value balance edit moved the entire world.
- **Entry:** Save format v1 exists and round-trips.
- **Work:**
  - Versioned save manifest; ordered migration chain (`v1→v2→v3...`).
  - A fixture directory of **every historical save version**, generated at each schema change and committed; a CI test that loads every fixture and asserts it migrates cleanly.
  - A migration-map mechanism for renamed/removed definition IDs (D-04, D-05).
  - A `save:migrate --dry-run` CLI that reports what a migration would change.
  - **Baseline compatibility:**
    - content identity separated from procedural entropy;
    - semantic, call-order-isolated random channels;
    - a per-cell `baseline_hash` on every changed-cell delta;
    - a computed `worldgen_fingerprint` with canonical probe cells;
    - registered transitions for baseline changes.
- **Exit criteria:**
  - Migration harness:
    - Adding a required field to player state ships with (a) a migration function, (b) a regenerated fixture set, (c) a CI test that loads all prior fixtures.
    - A multi-hop migration is tested.
  - Content identity:
    - Removing/renaming a content definition without a migration map fails CI.
    - An unrelated content change (a balance value) changes `content_hash` but does not perturb world generation.
  - Baseline compatibility:
    - Changed-cell deltas record and verify their baseline hash.
    - A baseline mismatch never silently accepts an old delta.
    - One synthetic baseline-affecting rebase through a registered transition is proven.
    - Generator drift without a `worldgen_version` bump is detected.
  - RNG stability: adding an unrelated random draw does not shift any other subsystem's output.
  - Crash safety: a migrated save commits through the M2 atomic path, so an interruption leaves the old or the new save, never a partial one.
  - CLI: `save:migrate --dry-run` reports the plan and is proven non-mutating.
- **Proof:** CI loads the v1 fixture under v3 code, `v1 → v2 → v3`, and asserts a correct, non-lossy result.
- **Why here:** Retrofitting migrations after months of unversioned schema drift means every existing save is disposable. This is on the critical path (R-7) and is deliberately *not* deferred to Phase 3.

### M2c — Progression Spine — `FEATURE`

- **Class:** FEATURE. **Depends on:** M1, M2.
- **Entry:** Registry and save baseline work; `PROGRESSION.md` ratified by the progression-axis audit (`PROGRESSION_AXIS_RECONCILIATION.md`, 2026-09-23).
- **Work:** XP ledger with `source_kind` tagging and the `AG-1..AG-8` anti-farm guards as data-driven constants; level curve and tier table; a level-up grants attribute points only; seven attributes and the derived values (Health/Stamina/Focus maxima, Resonance, Strain tolerance — no mana); the skill model with use-under-challenge XP, the difficulty gate and the common ceiling; the technique/formula knowledge record, learned only through typed learning events, with a default starting package; the non-conversion law enforced as a typed API (each axis accepts only its own currency type — a compile-time guard where possible, a runtime assertion where not); per-axis advancement telemetry; `schema_version` 4 through the M2b migration harness, with a v4 historical fixture.
- **Exit criteria** (rewritten by the progression-axis audit: the old (b) required "undiminished mastery", an axis that no longer exists). Headless tests prove:
  - (a) XP per hour stays inside the `AG-6` band across four scripted activity profiles;
  - (b) a scripted 6-hour farm at one spawn cluster yields ≥0.10× but <0.30× level XP after saturation, while `AG-1..AG-3` change no other currency. Weapon-skill progress is identical with and without them (only its own difficulty gate applies), and material yield and objective credit are unchanged (`AG-4`);
  - (c) no API exists by which gold, items or another axis's currency can advance level XP, attributes or skill; knowledge enters only through a typed learning event, which changes nothing else;
  - (d) every retained neighboring pair of axes passes its independence test (`PROGRESSION.md` §2);
  - (e) a level-up grants exactly the configured attribute points and nothing else;
  - (f) every historical save fixture migrates to schema 4 with the documented defaults, and the v4 fixture round-trips.
- **Proof:** A telemetry report from a scripted run showing per-axis advancement rates and the absence of cross-axis conversion.
- **Notes:** The charter requires that killing creatures not be the only path. This milestone is where that is proven in math before it is proven in content.

### M3 — Player, Movement, Interaction, World Cells — `FEATURE`

- **Class:** FEATURE (+ `RISK SPIKE`). **Depends on:** M0, M1, M2.
- **Entry:** C# presentation project builds and can host a scene; domain contracts stable.
- **Work:** One full-body third-person controller with seamless zoom into first person, shoulder swap and camera collision, the same authoritative movement and interaction commands at every camera distance (`CAMERA_PERSPECTIVE_AND_PRESENTATION.md` §22), with presentation-side prediction for feel only (D-11 — never writing state); interaction system issuing commands; **the prototype's 200 m × 200 m area authored as 4 real 100 m cells** (`PROTOTYPE.md` §3) through the real cell-addressing and per-cell-delta path; four simulation tiers (A/B/C/D per D-06) with at least tier A and tier D implemented and tier B/C stubbed behind the interface; discovery-credit world state; cartography map data. **Streaming the 2×2 km region is Phase 2** (`VERTICAL_SLICE.md`); Phase 1 exercises the cell path at 4 cells, not the streaming path at 400.
- **`RISK SPIKE`:** Frame budget under a *representative* load, not an empty field — target the vertical slice's entity budget, measured for the representative player camera: the third-person view, the first-person view and a camera-obstruction path. **It is not D-01's formal revisit gate in Phase 1** (owner ruling, 2026-09-23): that trigger needs the streaming and LOD work, which does not exist yet. The capture is recorded (the number is the deliverable, `RK-02`), and failing to sustain 1080p / 60 FPS on the clean baseline machine after reasonable optimization - in this greybox or in the prototype scene - is an owner-review stop before further engine-dependent systems are built.
  - **The spike is an isolated greybox experiment, explicitly NOT the playable prototype.** It is the scene `RISK_REGISTER.md` `RK-02` specifies: **2×2 km of untextured heightmap plus a few hundred instanced proxies, a navmesh, and the `D-06` tier scaffolding**, measured with a profiler. It needs no content, no player character beyond a debug camera, and no save round-trip. Its only output is a frame-time capture.
  - **Rationale — this distinction is load-bearing.** `PROTOTYPE.md` §2 defines the playable artifact as **200 m × 200 m over 4 exterior cells with streaming explicitly excluded as a Phase-2 risk** ("proving it early would mask seam bugs"). A 2×2 km *playable* build is the vertical slice's job (`VERTICAL_SLICE.md`, Phase 2), not Phase 1's. Folding the two together would silently convert the prototype into a streaming implementation and violate `PHASE_0.md` STEP 16's scope discipline. The spike exists solely to make `D-01`'s revisit trigger measurable.
- **Exit criteria:** (a) the playable build walks the **200 m × 200 m / 4-cell** prototype area in `PROTOTYPE.md` §3 at a sustained 60 FPS at 1080p on the baseline machine - RAZER's RTX 4070 Ti with OBS, H3 and other significant GPU workloads stopped (owner ruling, 2026-09-23) - under the prototype's entity budget; (b) the isolated risk spike produces a profiler capture for a 2×2 km greybox and the numbers are recorded in the register; (c) player state mutates only through commands (verified by a test that fails if presentation writes domain state); (d) cell save/load preserves world changes.
- **Proof:** A profiler capture with numbers from the 2×2 km spike; a playable 200 m prototype build. **These are two artifacts, not one.**
- **Playable state at exit:** You can walk a real landscape, interact with objects, and save/load.

### M3b — Items, Inventory, Equipment — `FEATURE`

- **Class:** FEATURE. **Depends on:** M2, M2c, M3.
- **Entry:** Registry, save, and content validation work; item schema defined (D-03, STEP 7).
- **Work:** Item/weapon/armor/resource definitions in YAML; item instance state (durability where justified, sockets, enchantments, per-instance familiarity per `PROGRESSION.md` §6); inventory with capacity and transfers; equipment slots with attribute/skill/family minima (not level minima — `PROGRESSION.md` §11.1); ground containers and world item persistence; loot table evaluation; currency; merchant stock definitions (stub merchants, no economy).
- **Exit criteria:** Headless tests cover inventory transfer, capacity overflow, equipment swap, container persistence across save/load, loot table distribution over 10⁵ rolls inside designed bounds, and item **uniqueness** (two instances of the same definition are distinct identities — D-04). Charter's TESTING list items "inventory transfers", "equipment", "loot generation", "item uniqueness" are green.
- **Proof:** Green test suite named after the charter's TESTING bullets, so coverage is auditable at a glance.
- **Forbidden:** crafting (R-3).

### M3c — Combat Core — `FEATURE`

- **Class:** FEATURE. **Depends on:** M3b. **Requires:** at least 3 weapons and 3 creature stubs to tune against (R-2).
- **Entry:** Items, equipment, and damage-relevant item properties exist.
- **Work:** Attack resolution for melee, ranged, and unarmed; blocking, dodging, armor mitigation, resistances, critical hits, stagger, status effects, buffs/debuffs, damage types, weapon reach, Stamina, Focus and Strain (Charter §7; there is no mana); three weapon families implemented to full quality (`one_hand_blade`, `bow`, `staff`) rather than eleven done badly; weapon-skill advancement wired to `PROGRESSION.md` §6.
- **Exit criteria:** A tuning spreadsheet, generated *from* the build rather than authored by hand, shows time-to-kill within design bands across level bands 1–3; combat is readable (a tester can name what killed them); no out-of-band HP sponges (an over-band creature must be lethal by damage, not by health pool); weapon-skill XP accrues only from effective contribution.
- **Proof:** Playable combat build; generated TTK table; weapon-skill accrual telemetry.
- **Playable state at exit:** You can fight, die, and be rewarded.
- **Phase-1 reconciliation (M3c):** the three families are `PROTOTYPE.md`'s sword (`one_hand_blade`), bow and spell; the spell family (`staff` above) is built with M3e's magic, and so are Focus and Strain, which only casting spends. The design bands are `VERTICAL_SLICE.md` §5.1's. "Aggro" is local and perception-derived (`SYSTEMS.md` S-12, S-23).

### M3d — Creature Framework, AI Baseline, Loot — `FEATURE`

- **Class:** FEATURE. **Depends on:** M3c.
- **Entry:** Damage, status effects, and loot tables exist.
- **Work:** Creature definitions (STEP 7); a small number of *excellent* creatures across families — predator, prey, humanoid, undead, magical — each with distinct behaviour rather than reskins (R-2); spawn populations and respawn rules; habitat constraints; lightweight ecological behaviours where cheap; faction hostility rules; loot-table-driven drops; death processing and corpse state.
- **Exit criteria:** Five creatures that play differently are provably different (a behaviour matrix, not a claim); spawn/respawn persists correctly across save/load; `AG-3` spawn-site saturation is observable; creature AI cost is within the M3 profiling budget at scale.
- **Proof:** Behaviour matrix; saved/loaded spawn state; profiler capture.
- **Phase-1 reconciliation (M3d):** the five creatures are the content bible's five archetypes (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §10) - predator (the ash ember hound), humanoid and undead (the bone walker husk), magical construct (the animated armour), charging brute (the bristleback boar), ambush beast (the cave hunting spider) - beside the prototype's wolf; prey and ecology are cheap-only (a boar roots about its wallow, strays wander, wolves sleep). Behaviour roles are a separate data layer over archetypes (owner ruling). Faction hostility is "every creature against the player, indifferent to each other"; habitat is a spawner's place, route and territory. Creature AI cost is measured headless (`M3D_STATUS.md`); the frame-budget capture joins M3's RAZER window.

### M3e — Basic Magic — `FEATURE`

- **Class:** FEATURE. **Depends on:** M3c, M3d.
- **Entry:** Damage types and status effects exist.
- **Work (Phase-1 scope, owner ruling):** three tiny representative magic domains with one formula each - the content bible's Impulse Bolt (Force), Brace Ward (Warding) and Mending Thread (Vital) (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §13), where `PROTOTYPE.md` had Ember, Mend and Ward - costed in Focus and Strain, with Resonance scaling their force; there is no mana. Casting with a tell and concentration under damage; Strain accumulating per working and recovering at rest, with backlash rather than a lockout past the character's tolerance (`MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md` "Unsafe casting"); domain skill (`PROGRESSION.md` §7) making a formula steadier and cheaper; formulas known through learning events such as study; clear feedback in the HUD. Not built: attunement slots or any level-10 threshold (removed by the ratified progression model), more domains, reaction chains, a spell editor, Great Works, Otherwhen, divine systems.
- **Exit criteria:** The three formulas cast from content and do different things (damage at range, a timed protection, a recovery that stops bleeding); Strain accumulates and recovers, and casting past tolerance costs health; a wound during the tell breaks the cast; trivial repeated casting grants no domain skill and no formula, while study yields a formula without casting (the two-mechanic split, `PROGRESSION_AXIS_RECONCILIATION.md` §4.6).
- **Proof:** Tests asserting that 500 trivial casts grant zero domain skill and no formula and that reading a primer teaches formulas never cast; Strain, backlash and interruption tests; a windowed capture of the casting UI.

### M3f — Skills, Gathering, One Profession — `FEATURE`

- **Class:** FEATURE. **Depends on:** M3b, M3e.
- **Entry:** Items and resources exist; skill XP framework from M2c exists.
- **Work (Phase-1 scope, owner ruling):** two recipes and one gather→craft proof loop, with no profession ranks: the content bible's blacksmithing proof (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §12) - raw iron ore smelted into an Iron Billet at the forge, and billet plus an Ash Haft made into the March Spear at the anvil, where `PROTOTYPE.md` had the salve and the sword temper. Harvesting nodes and their persistence, keeping `PROTOTYPE.md` C12's two respawn classes (a finite iron seam; an ash stand that refills each world day). Skill use-hooks: gathering trains survival, crafting trains smithing, both through the difficulty gate. Quality and material properties matter: a crafted item's quality is rolled from the smith's skill against the recipe's complexity and is capped by the weakest material, and a weapon's quality changes its damage (per-instance, saved). The production-XP first-time-only rule. Not built: profession ranks, discoveries and experiments, crafting time, tools, the full combinatorial laboratory.
- **Exit criteria:** The loop plays end to end: gather both materials, smelt, forge, equip, and fight with the result. A craft consumes exactly its recipe (C13) and needs the recipe known, the station in reach and the inputs carried. Quality varies with skill and materials and lands on the instance, not the definition (C14's point). First-time-only production XP is verified; repeated trivial crafting and gathering teach nothing past the gate. Harvested-node state, including the seam's depletion and the stand's refill, survives save/load.
- **Proof:** Named test cases; the windowed gather→craft→equip capture.

### M4 — Settlement NPCs, Persistence, Dialogue — `FEATURE`

- **Class:** FEATURE. **Depends on:** M3f. **Unlocks:** quests, companions, building.
- **Entry:** Items, crafting, and combat are stable enough that an NPC can exist, act, and be interacted with.
- **Work (Phase-1 scope, owner ruling):** a small NPC population - the prototype's, as the content bible casts it: the waystation's steward, the smith and the archivist - fully simulated, every one of them all the time, in the one settlement's authored layout. NPC definitions and instances (STEP 7, STEP 10; D-10), each keeping its identity, its relevant life state and basic continuity: what the character has heard of each conversation, and what each NPC thinks of the character (relationship values per dimension, `SYSTEMS.md` S-26). Dialogue as data (D-03), deterministic and structured: its conditions and consequences are closed sets over world state, and a consequence is a command to the system that owns what it changes. Merchant interaction on top of the M3b economy stub: buying and selling through the smith. The game needs no runtime LLM and works with AI narrative disabled.
- **Deferred (owner ruling):** tier-transition simulation and the 200-promotion risk spike below, to the first milestone that actually introduces simulation tiers; NPC schedules, which `PROTOTYPE.md` puts in the vertical slice.
- **`RISK SPIKE` (tier transitions) - deferred with the tiers it tests:** D-06 names tier transitions as the place such systems break. When NPC tiers arrive, promote an NPC from C to A and assert it arrives in a *legal, consistent* state ("reconciled", never naively adopted); 200 scripted promotions/demotions across a fast-forwarded clock produce zero discontinuity.
- **Exit criteria:** Each NPC stands in the world and can be talked to; the conversation's lines and replies follow from their conditions, and its consequences - an item given, a recipe taught, a relationship moved, a flag set, trade opened - happen through their owners' commands. What each NPC has said once stays said, and what each thinks of the character stays thought, across save/load. Buying and selling move coin and goods exactly, refuse cleanly, and the trader's stock survives save/load.
- **Deliberately NOT in this milestone (Phase 2):** **faction and reputation state** (`AX-REP`, `S-27`). An earlier draft of this roadmap placed it here, which contradicted `PROTOTYPE.md` (faction reputation is explicitly out of prototype scope) and `SYSTEMS.md` §3 (S-27 is Phase 2). It moves to `M7`. `PHASE_0.md` STEP 15 does not list factions for the prototype. Nor persuasion, rumour, languages or knowledge simulation (`SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md` is future direction).
- **Proof:** Named test cases; a saved and loaded world in which every conversation and relationship continues where it was; the windowed capture of the conversations and a trade.
- **Forbidden:** settlement *simulation* (economy, growth, production) — that is M10. This milestone proves **persistence and continuity** only (R-3).

### M5 — Quest Framework + Quest Debugger — `FEATURE` + `INFRA`

- **Class:** FEATURE + INFRA. **Depends on:** M4.
- **Entry:** World state is queryable and NPC state persists (quest objectives are predicates over it — D-07).
- **Work:** Quest graph definitions in YAML; a **closed set** of objective types with strict schemas (exploration, dialogue, item acquisition, crafting, construction, combat, boss, faction state, relationship, puzzle, hidden, timed, world-state); predicate evaluation against world state with no per-quest engine code (D-07); branching, failure states, and consequence hooks; quest state persistence; journal/UI surface.
- **Phase-1 build (M5, as implemented):** the closed vocabulary is `DATA_MODEL.md` §4.11's. Phase 1 builds the eleven types whose systems exist - `talk_to`, `visit_location`, `explore_location`, `acquire_item`, `craft_item`, `harvest_resource`, `kill_creature`, `deliver_item`, `world_state`, `relationship_value`, `wait_until` - and the lint names the rest (construction, bosses, factions, puzzles, companions, knowledge, time of day...) and refuses them until their systems arrive. Branching is `branch: first` (the first alternative satisfied closes the others), joins are `all_of`, failure is `fail_if` and timed objectives with `on_fail`; rewards go through their owners' commands, once. One quest is authored, the content bible's Quest 1 (`quest.ashen_hollow.iron_under_ash`); Quest 2 ends in recruiting the companion and lands with M6. `M5_STATUS.md` has the details.
- **`QUEST DEBUGGER` (same milestone, non-negotiable):** a tool that answers, for any active quest, *what is this quest waiting on right now*, with the current value of every predicate term, the events that would satisfy each unsatisfied term, and a tick-by-tick trace of the last N evaluations. D-07 states this is a Phase-1 tool, "not a luxury", because long artefact quests are undiagnosable without it.
- **Exit criteria:** The charter's TESTING item "quest state" is green (persistence across save/load, branch integrity, no orphaned objectives); the debugger correctly explains three deliberately-failing quests; a content-validation rule rejects a quest referencing a nonexistent objective type, item, NPC, or faction.
- **Proof:** Debugger transcript resolving a broken quest; content-validation failure fixture; green quest test suite.
- **Playable state at exit:** The game has real objectives, and a session can diagnose why one will not complete.

### M6 — Companion v1 — `FEATURE`

- **Class:** FEATURE. **Depends on:** M4, M5.
- **Entry:** NPC persistence and quest objectives work.
- **Work (Phase-1 scope, owner ruling):** one companion with prototype behaviour - the content bible's Tavar Orr (§9, §17), recruited through the world: the bible's Quest 2, *The Three Quiet Stones* (§16), frees him, and he joins when asked. He follows, waits where he is told, catches up when left behind or snagged, fights beside the character, and is downed and dies as the prototype requires (`PROTOTYPE.md` §6.2: death and revive); his state persists. The companion AI must be *boringly* reliable, not clever. **Not built (owner ruling):** an affinity ladder, a personal quest, a full player+3 party, romance, offscreen marriages, institutions and advanced tactics; the code encodes no permanent maximum party size. **Also in this milestone:** the reconciliation of the M3 layout to the content bible's four cells (the M3d ruling: "before M6 acceptance"), which the bible's second quest and its acceptance route need, and the playable-prototype report of the execution prompt's §19.
- **Earlier draft (superseded by the ruling above):** one fully developed companion with its own progression (`AX-CMP`), affinity ladder, opinions, reactions to player decisions, personal quest hooks, inventory/equipment management, and the command set from Charter §15; death/parting consequences without forcing reload (Charter §22). Those arrive with the companion roster (Phase 2+).
- **Exit criteria:** `PROTOTYPE.md` C16 - recruit Tavar, order follow -> wait -> follow, and he paths around the lodge without any snag lasting more than 15 s; the prototype's companion-state tests (follow/wait transitions, distance-based catch-up, downed, death and revive, the behaviour state saved) are green; he walks the bible's acceptance route (§30) with the character without a pathing intervention; his state round-trips through save/load field by field; both of the bible's quests complete.
- **`EARLY FEEL TEST` (added after adversarial review — do not skip):** the prototype is first *playable end to end* at the end of this milestone, and the fun question must not wait for `M9`. Run `PROTOTYPE.md`'s §9-style protocol here, in its cheapest form: **3–5 blind testers**, the prototype's 40-minute scripted hollow plus **one deliberately differently-shaped second quest bolted on**, scored against `VERTICAL_SLICE.md`'s felt-experience list (F1–F5). This is deliberately *not* a gate and has no pass/fail — its only job is to make a "the core loop is not fun" finding arrive **before** the ~180 content files of `M9` are authored rather than after. If the loop reads as unfun here, fixing it is cheap; the same finding at `M9` is the most expensive outcome in the project. **Owner ruling (2026-09-24):** deferred until a nontechnical Windows playtest build exists; it is **not an M7 entry blocker**. It has not been run and no result is recorded.
- **Proof:** Recorded session log of the acceptance route with zero pathing interventions; companion state round-trip through save/load; the §19 playable-prototype report; **the early feel-test transcript and its findings, including a null result** (the owner runs the feel test after the playtest).
- **Notes:** Charter §15 — "hirelings should feel like individuals rather than equipment slots with faces". Hirelings (generic, cheaper) follow in Phase 3; this milestone proves the *hard* case first.

### M7 — Factions, Reputation, and Building v1 — `FEATURE` — **PHASE 2**

- **Class:** FEATURE. **Depends on:** M4 (NPC persistence), M3f (materials). **Phase:** **2** — this milestone is Phase-2 work; an earlier draft of this roadmap placed it in Phase 1, which contradicted `PROTOTYPE.md`, `GAMEPLAY_LOOPS.md` §15 ("**BUILD** — **Not in Phase 1**"), and `SYSTEMS.md` §3 (S-27 Factions and S-32 Buildings are both Phase 2). `RISK_REGISTER.md` declares Phase 1 *failed* if any system outside `PROTOTYPE.md`'s minimum list is built, so building this in Phase 1 would have failed the phase by the register's own criterion.
- **Entry:** NPCs and companions path reliably — D-08's whole justification for snap-based building is that structures must stay navigable and persist reliably.
- **Work (factions/reputation, moved here from M4):** faction definitions and membership graph; per-faction player reputation with directional, per-faction reactions and **no universal morality meter** (Charter §20); cross-faction attitude relations; crime/bounty records and pardon state (`S-27`); service, dialogue and territory gating derived from standing. Reputation answers *access* and never converts into character power (`D-09`).
- **Work (building):** Socket/snap placement; foundations, walls, floors, roofs, doors; free rotation and socketing; ownership; per-piece health applied by explicit rules (**no structural simulation, no physics collapse** — D-08); repair; storage containers; one crafting station as a placeable; player-built structure persistence as a sparse world delta (D-05); placement validation that rejects un-navigable or overlapping configurations.
- **Exit criteria:** Build a structure, assign an NPC to work in it, verify the NPC navigates in, through, and around it — **including a structure straddling a cell boundary**, which is `RK-14`'s explicitly unsolved case and must be proven here rather than discovered in playtest; save/load preserves every piece with correct ownership and health; damage/repair works and is explicit rather than emergent; the navmesh updates on placement; the same act moves two factions in opposite directions in a fixture.
- **Proof:** Playable build; navmesh path test including the straddling-seam case; structure round-trip through save; a reputation fixture table.
- **Notes:** Less creative freedom than voxel building is the accepted cost of guaranteed navigability and clean persistence (D-08). If playtest says expression is limited, the answer is **more pieces, not physics**.

### M8 — Dungeon, Boss, and the First Multi-Stage Quest — `FEATURE` — **PHASE 2**

- **Class:** FEATURE. **Depends on:** M3d, M3e, M4, M5. **Phase:** **2** — see the note on M7. `PROTOTYPE.md` states plainly that "the wolf den is an open cave mouth, not a dungeon" and defers dungeons, bosses and rare spawns to the vertical slice; `SYSTEMS.md` §3 places S-34 Dungeons and S-35 Bosses in Phase 2. `PHASE_0.md` STEP 15 does mention "one dungeon" for the prototype while STEP 16 assigns dungeons and a boss to the slice; the stricter, more detailed document (`PROTOTYPE.md`) governs, and this roadmap now follows it.
- **Entry:** Quest framework and debugger (M5) exist — R-3 forbids epic quests before the framework. Creatures, magic, and NPCs work.
- **Work:** One hand-authored dungeon that is a *place* rather than a combat corridor (Charter §17): multiple entrances, a shortcut, a secret area, environmental storytelling; one boss with readable mechanics and phases (no HP-sponge design); one multi-stage quest chain that exercises the objective type set (research → locate → acquire → combat → craft → choice) and remains active across multiple play sessions.
- **Exit criteria:** The multi-stage quest is completable, and every stage is diagnosable through the quest debugger; the dungeon is completable solo at the design level with at least two different builds; the boss is defeated by a tester who can articulate why they won.
- **Proof:** Two recorded playthroughs with different builds; quest-debugger trace; dungeon map document with justification for each space.

**Phase-1 / Phase-2 boundary (normative).** Phase 1 ends at **M6**. Phase 2 runs **M7 → M8 → M9**. `M9` is the integration point where the systems built in M7–M8 meet the slice's content target, and it is the gate where the fun question is answered *at full content scale* — the cheap early version of that question is answered at M6's early feel test, by design.

### M9 — Phase 2 Vertical Slice — `GATE`

- **Class:** GATE. **Depends on:** everything above.
- **Entry:** All **Phase-1** systems complete (that is, through `M6`), **and** the Phase-2 systems `M7` (factions/reputation/building) and `M8` (dungeon/boss/multi-stage quest) complete; each prior milestone's exit criteria still hold (regression suite green). The slice is the integration test of the whole stack, which is why it runs last within Phase 2.
- **Carried from the M6 early feel test:** if the early feel test found the core loop unfun, that finding is an **entry blocker for M9**, not merely an input — fix the loop before authoring ~180 content files against it. If the early test was a null result or positive, the slice proceeds and its own §9.2 protocol decides.
- **Target content (Charter PHASE 2, STEP 16):** one meaningful region (≈2×2 km); one town; wilderness areas; 3–4 dungeons; 10+ enemy archetypes across multiple families; multiple weapons; 3–5 magic schools of *unequal but real* depth; several skill trees; 3+ professions; basic building; one recruitable companion with a personal quest; 2–3 factions with mutually constraining reputation; merchants and a regional economy stub; crafting progression to rank 3; one major multi-stage quest; one boss; one epic equipment reward with an authored story rather than a random drop.
- **Exit criteria (the only question that matters):** *Is this game actually fun?* Operationalised: (a) a fresh player reaches level 8–12 in 4–6 hours without being told to grind; (b) the player has ≥2 meaningful build directions available and can articulate the difference; (c) at least one "I should not be here yet" moment occurs naturally (Charter Pillar 1); (d) at least one player-authored story of the charter's CREATIVE DIRECTIVE shape is reported from the session; (e) the epic reward is obtained and the player describes it as important; (f) no crash, no lost save, no quest that cannot be diagnosed with the M5 debugger.
- **Proof:** A playtest report with the above six items answered, plus the shipped build tag.
- **Gate question:** *Do we proceed to Phase 3 expansion, or do we fix the loop first?* If (a)–(f) are not all satisfied, the answer is fix first — expanding a loop that is not fun multiplies the problem. This gate is deliberately the most expensive one to get wrong.

### M10 — Settlement Simulation and Regional Economy — `FEATURE`

- **Class:** FEATURE. **Depends on:** M9 gate passed; M4 NPC persistence.
- **Entry:** M9 gate passed.
- **Work:** Settlements as simulated entities (production, consumption, stock, prices by rarity/craftsmanship/material/location/faction/supply — Charter §19); NPC work assignment and production roles; settlement growth; profitable-but-bounded merchant trade; **exploit closure** — an adversarial pass on infinite-money loops, arbitrage, and salvage/repair cycles (Charter §19, PHASE_0 STEP 2's circular-economy check); home defense threats with frequency controls and an option to substantially reduce or disable them (Charter §14).
- **Exit criteria:** Prices respond measurably to supply, region, and faction; three named money exploits are closed with regression tests; defense frequency is player-controllable and defaults to rare; settlement state persists as a sparse delta.
- **Proof:** Economy simulation trace; exploit regression tests; a settlement saved, reloaded, and observed to still function.

### M11 — Region 2 and World Content Expansion — `FEATURE`

- **Class:** FEATURE. **Depends on:** M10.
- **Entry:** Region 1 is complete per D-12.
- **Work:** Second region with a distinct biome, danger band, culture, and economy; additional creature families and dungeons; travel network between regions (earned fast travel — Charter §3); world events that are encountered rather than icon-announced (Charter §18); rare roaming bosses; hidden/secret dungeons; treasure maps and cartography depth.
- **Exit criteria:** A player can travel between regions through earned means; each region has content for a distinct level band with no global scaling; world events occur without UI spam.
- **Proof:** Cross-region playthrough; event-frequency telemetry showing "encountered, not announced".

### M12 — Deep Magic, Archetype Expansion, Post-Cap Progression — `FEATURE` + `GATE`

- **Class:** FEATURE + GATE. **Depends on:** M11.
- **Entry:** Region 2 complete; M9 loop still validated.
- **Work:** Remaining magic schools with genuinely distinct mechanics (illusion, alteration, blood, runic, divine, enchanting, protection/summoning depth); the six archetypes' Phase-3 expansions; post-cap progression per `PROGRESSION.md` §8 (mastery designations and deep technique chains, formula refinement, artifact-level crafting, faction apex, Great Works); races' full ability packages; the D-09 duplication telemetry audit (correlation of axis advancement rates) with a written verdict.
- **Exit criteria:** Level 50 is reached by a scripted player in the target 65–85 hour window; post-cap tracks demonstrably do **not** produce strictly larger raw damage than a well-built level-50 character; no two axes show correlation above the D-09 threshold without a recorded justification.
- **Proof:** Pacing report; endgame power audit; D-09 telemetry verdict.
- **Gate question:** *Is the progression system still orthogonal after real content exists, or must two axes be merged?* D-09's revisit condition is exactly this test, and it belongs here rather than in a document review.

### M13 — Artefact Quest Chains and High-Level Content — `FEATURE`

- **Class:** FEATURE. **Depends on:** M12.
- **Entry:** Post-cap progression and deep magic exist.
- **Work:** 2–3 long-form artefact quest chains of the charter's STEP-11 shape (research, locate a maker, obtain knowledge, defeat bosses, recover fragments, obtain materials, perform a ritual, make a final choice affecting the item's nature); legendary crafting via profession mastery projects; superbosses; extremely dangerous regions; companion-story conclusions.
- **Exit criteria:** Each chain is completable across multiple sessions; every stage is quest-debugger diagnosable; the reward is described by testers as genuinely important; no chain is blocked by a single unrecoverable state (failure produces consequences, per Charter §22).
- **Proof:** Recorded multi-session completion of one full chain; debugger traces for every stage transition.

### M14 — Content Scale Pass, Performance, and Polish Infrastructure — `FEATURE` + `INFRA`

- **Class:** FEATURE + INFRA. **Depends on:** M11.
- **Entry:** Two regions and all core systems exist; content volume is real enough to strain the pipeline.
- **Work:** Bulk content expansion driven by the M1b validator (hundreds of items, recipes, creatures, dozens of quests) with **CI enforcement** that content volume never requires new gameplay code; compiled content cache for shipped builds (D-03); LOD/instancing/pooling and simulation-distance tuning against measured budgets; save-size and load-time budgets under a large playthrough; accessibility and options pass (Charter: difficulty, base attack frequency, survival toggles, UI scaling, subtitles, colour, camera effects, motion blur, FOV, sensitivity, rebinding); HUD customisation.
- **Exit criteria:** A large save loads within budget; frame time holds on the baseline platform under maximum content density; a content author adds 50 items and 5 quests without touching `src/Domain/` code; accessibility options verified.
- **Proof:** Measured budgets with before/after numbers; a "no code changes" content-addition commit as evidence.

### M15 — Cooperative Seam Validation — `RISK SPIKE` (deferred deliberately)

- **Class:** RISK SPIKE. **Depends on:** M14. **Not** an implementation of multiplayer.
- **Entry:** Systems are stable; nothing about this milestone may change gameplay architecture (R-6, D-12).
- **Work:** A headless validation harness that exercises the four extension points preserved by D-12 — command-only mutation (D-02), stable identities (D-04), sparse versioned persistence (D-05), tiered simulation (D-06) — by attempting to relocate player authority behind a process boundary *in a test build only*. The deliverable is a **report on which seams leak**, not a server.
- **Exit criteria:** A written list of every place where presentation or a system assumes local authority, each with an estimated retrofit cost. Zero production code changed.
- **Proof:** The leak report. D-12 already accepts that future multiplayer will be harder than if designed for now; this milestone makes that cost *known* instead of assumed, at the cheapest possible point.

### M16 — Ship Readiness — `GATE`

- **Class:** GATE. **Depends on:** M14, M15.
- **Entry:** Content scale, performance, and stability targets met.
- **Work:** Save-compatibility sweep across all historical fixtures (M2b harness); corruption-recovery drill; quality-rules audit against the charter's QUALITY RULES list (no placeholder architecture, no swallowed errors, no broken references, no dead code, no duplicated systems, no unexplained magic numbers, no save-breaking schema changes without migration, no debug cheats in normal gameplay, no ignored major warnings); final death/economy/pacing balance pass; documentation refresh so a fresh session can resume from the repository alone (Charter CONTINUITY).
- **Exit criteria:** Every quality rule verified or explicitly waived with a recorded reason; a v1 save loads under current code; documentation pass complete.
- **Proof:** Quality audit checklist; migration drill transcript; refreshed docs.
- **Gate question:** *Ship, or hold?* Owner decision, informed by the audit rather than by schedule pressure.

---

## 5. Milestone Summary Table

| ID | Name | Class | Phase | Gate | Risk spike | On critical path |
|---|---|---|---|---|---|---|
| M0 | Repo + architecture + decision lock | GATE | 0 | ✔ | | ✔ |
| M1 | Domain skeleton, headless harness, CI | INFRA | 1 | | | ✔ |
| M1b | Content validation tooling | INFRA | 1 | | | ✔ |
| M2 | Registry, identity, persistence baseline | INFRA | 1 | | ✔ (determinism) | ✔ |
| M2b | Save migration harness | INFRA | 1 | | | ✔ |
| M2c | Progression spine | FEATURE | 1 | | | ✔ |
| M3 | Player, movement, world cells | FEATURE | 1 | | ✔ (frame budget) | ✔ |
| M3b | Items, inventory, equipment | FEATURE | 1 | | | ✔ |
| M3c | Combat core | FEATURE | 1 | | | ✔ |
| M3d | Creatures, AI baseline, loot | FEATURE | 1 | | | ✔ |
| M3e | Basic magic | FEATURE | 1 | | | ✔ |
| M3f | Skills, gathering, one profession | FEATURE | 1 | | | ✔ |
| M4 | Settlement, NPC persistence, dialogue | FEATURE | 1 | | ✔ (tier transitions) | ✔ |
| M5 | Quest framework + **debugger** | FEATURE+INFRA | 1 | | | ✔ |
| M6 | Companion v1 | FEATURE | 1 | | | ✔ |
| M7 | Factions, reputation, building v1 | FEATURE | 2 | | | ✔ |
| M8 | Dungeon, boss, multi-stage quest | FEATURE | 2 | | | ✔ |
| M9 | **Phase 2 vertical slice** | GATE | 2 | ✔ | | ✔ |
| M10 | Settlement simulation + economy | FEATURE | 3 | | | |
| M11 | Region 2 + world content | FEATURE | 3 | | | |
| M12 | Deep magic + post-cap progression | FEATURE+GATE | 3 | ✔ | | |
| M13 | Artefact chains + high-level content | FEATURE | 3+ | | | |
| M14 | Content scale, performance, polish | FEATURE+INFRA | 3+ | | | |
| M15 | Cooperative seam validation | RISK SPIKE | 3+ | | ✔ | |
| M16 | **Ship readiness** | GATE | 3+ | ✔ | | |

---

## 6. Risk-Reduction Spikes (explicitly not features)

| Spike | Where | What it decides | Cost if deferred |
|---|---|---|---|
| World-generation determinism | inside M2 | Whether D-05's sparse-delta save is viable at all | Catastrophic: deltas against a shifting baseline corrupt worlds. D-05's first revisit trigger |
| Frame budget under representative load | inside M3 | Whether D-01's engine choice holds — the *only* sanctioned evidence for reopening it | Expensive: streaming/LOD architecture built on an assumption |
| NPC tier-transition reconciliation | inside M4 | Whether D-06's four-tier simulation produces visible discontinuity | High: every quest, schedule, and companion system sits on top of it |
| Quest-debugger adequacy | inside M5 (as a deliverable, tested adversarially) | Whether D-07's predicate model is diagnosable in practice | High: D-07's revisit trigger is exactly "the abstraction is wrong" |
| Companion AI reliability | inside M6 | Whether companions are an asset or a frustration (Charter §15) | Medium-high: companion-dependent content scales badly if the AI is unreliable |
| Cooperative seam leakage | M15 | Which of the four D-12 extension points actually holds | Low if done here; high if discovered during a real multiplayer effort |

---

## 7. Gate Summary and Owner Review Points

| Gate | Milestone | Question the owner must answer | Cost of a wrong answer |
|---|---|---|---|
| G-0 | M0 | Accept the architecture for a multi-year commitment? | Very high if wrong later; near zero if wrong here. Also confirms the three open questions in `DECISIONS.md` (tone, platform baseline, fidelity target) |
| G-1 | M9 | Is the vertical slice actually fun, and do we expand or fix? | Highest in the project. Expanding an unfun loop multiplies the defect across all Phase-3 content |
| G-2 | M12 | Are the D-09 axes still orthogonal under real content, or must two merge? | Medium-high. Merging axes late means re-balancing every character and save |
| G-3 | M16 | Ship, or hold? | Medium. Informed by audit, not schedule |

**No other milestone requires owner input.** Per PHASE_0's autonomous working rules, everything between gates proceeds on documented inference; significant assumptions are recorded in the relevant document's assumptions section rather than escalated as questions.

---

## 8. First Ten Development Tasks in Dependency Order

These are the concrete first tasks after this document is accepted. Each is sized to be completable and verifiable in a single working session.

1. **Repository scaffolding** per D-02: `src/Domain/` solution projects with **no Godot reference**, `src/Presentation/` Godot project, `content/`, `tests/`, `docs/`, `.gitignore`, CI workflow file.
2. **Headless test harness bootstrap:** one test project, one passing trivial domain test, CI green on push.
3. **Command/event bus v1:** synchronous in-process implementation, deterministic test scheduler, unit tests for ordering and re-entrancy.
4. **Content loader + schema validator v1** for `ItemDefinition` only, with file/line error reporting and a deliberately broken fixture test.
5. **Entity Registry v1:** ULID assignment, definition/instance namespaces, lookup, and the no-gameplay-rules constraint enforced by an architecture test.
6. **Deterministic cell generator v1:** `(seed, generator contract)` → cell contents, with the repeatability test from M2's risk spike.
7. **Sparse-delta save v1:** manifest + player state + changed-cell delta, atomic write, rolling backup, round-trip test.
8. **Save fixture + migration harness v1:** version field, migration chain interface, committed v1 fixture, CI load test.
9. **Progression spine v1:** XP ledger with `source_kind`, level curve, seven attributes, derived pools, and the non-conversion API guard.
10. **Telemetry skeleton:** per-axis advancement events with timestamps and source kinds, written to a non-shipped log, plus the `AG-6` rate-banding report generator.

Task 4 exists before task 5 because content validation failures are cheapest to fix before definitions proliferate. Task 6 exists before task 7 because deltas are meaningless without a stable baseline. Task 7 exists before task 9 because progression state must be saved the moment it exists.

---

## 9. Assumptions Recorded

1. **Milestone sizing and phase boundaries.** Milestones are sequenced for a small, heavily AI-assisted effort. **`M1`–`M6` form the Phase-1 prototype; `M7`–`M9` are Phase 2, with `M9` the vertical slice and its fun gate; `M10`+ is Phase 3+**, per the charter's own phase definitions. This boundary matches `PROTOTYPE.md` (building, dungeons, bosses and factions are all "Vertical slice"), `GAMEPLAY_LOOPS.md` §15 (`BUILD` and `RETURN` "Not in Phase 1"), and `SYSTEMS.md` §3 (S-27/S-32/S-34/S-35 all Phase 2). **`ROADMAP.md` is the scheduling authority for *when* work happens, but `PROTOTYPE.md` is the authority for *what is in Phase 1*** — this roadmap is cut to match it, not the reverse.
2. **The Phase-1 prototype is not a throwaway.** Per D-12 and the charter's QUALITY RULES, Phase 1 builds the production architecture with a small content set. The "prototype" label refers to content scope, never to architecture quality.
3. **Quest debugger is in M5, not M8 or later.** D-07 is explicit that long artefact quests are undiagnosable without it; M8 (the first multi-stage quest) is already downstream.
4. **Save migration harness is in M2b, before progression, combat, or quests exist.** Every one of those adds save schema.
5. **Content validation tooling is in M1b, before the first real definitions.** D-03 calls it a Phase-1 deliverable.
6. **M15 is a spike, not a feature, and is explicitly last.** D-12 forbids building MMO infrastructure; M15 only produces a report.
7. **Economy and settlement simulation are Phase 3 (M10).** M4 proves NPC *persistence and continuity* only. This is the practical form of PHASE_0's "do not build settlement simulation before NPC persistence works".
8. **Building (M7) deliberately follows NPC persistence (M4).** D-08's navigability guarantee is only meaningful once NPCs actually path.
9. **Marketing/compliance/store work is out of scope** for Phase 0–2 milestones; nothing here assumes a release date.
