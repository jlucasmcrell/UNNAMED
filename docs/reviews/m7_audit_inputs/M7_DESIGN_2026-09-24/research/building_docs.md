# M7 research notes: building, structures, placement, sockets and navigability (key: building_docs)

Reader: an M7 designer who will not reread the sources. Research only; nothing is implemented.

**Source.** A read-only snapshot of `origin/main` at commit `e10d2c4`. Every `path:line` citation is repo-relative and valid at that commit. Citations beginning `G:/UNNAMED/docs/...` point at **untracked** files in the live working tree. Their authority is unconfirmed, and each one carries that label. `G:/UNNAMED_HISTORY/...` holds reports, not source.

**Tags.**
- **[FACT]** means the claim is quoted or paraphrased from a cited line.
- **[INFERENCE]** means the conclusion is mine.
- Short verbatim quotes are in quotation marks.

---

## 0. Executive summary

1. **The core rule is D-08, and every building document agrees with it.** Building is "socket- and snap-based assembly of authored pieces", with "no structural integrity simulation, no physics-driven collapse, and no load-bearing calculation" (`docs/DECISIONS.md:176`). The stated reasons are navigability and persistence (`docs/DECISIONS.md:180`).
2. **The M7 building scope is written in ROADMAP M7.** The work is "Socket/snap placement; foundations, walls, floors, roofs, doors; free rotation and socketing; ownership; per-piece health …; repair; storage containers; one crafting station as a placeable; … persistence as a sparse world delta (D-05); placement validation that rejects un-navigable or overlapping configurations" (`docs/ROADMAP.md:283`).
   - The exit criteria require NPC navigation "in, through, and around" a structure, "including a structure straddling a cell boundary" (`docs/ROADMAP.md:284`).
3. **The docs assume a navmesh. None of them assigns it to the domain.**
   - `WORLD_ARCHITECTURE.md` §11 puts "Navmesh | Recast-style, baked per cell, stitched at cell borders, rebuilt debounced on building change" in the *presentation/streaming* table (`docs/WORLD_ARCHITECTURE.md:435`).
   - `RISK_REGISTER.md` RK-14 says its validation "needs the engine" (`docs/RISK_REGISTER.md:290`).
   - The only navmesh in the code is a Godot `NavigationServer3D` bake. It lives in the presentation-only performance spike (`src/Presentation/Spike/SpikeScene.cs:157-181`).
   - These statements are in tension with **owner ruling 1** (navigation stays deterministic, headless and in the domain). **Ruling 1 is not recorded anywhere in the repo or in the untracked docs.**
4. **Owner ruling 2 (a one-storey Building v1) is recorded nowhere.** No document contradicts it for v1. Two constraints apply:
   - The charter's eventual list includes "towers" (`docs/PROJECT_CHARTER.md:512`).
   - Today's movement code cannot stand on anything. Body Y is always terrain height (`src/Domain/Spatial/Kinematics.cs:158`), and nothing is stood on (`src/Domain/Spatial/Blockers.cs:8`). So even a raised ground-floor foundation needs a new walk-surface concept.
5. **Five sources give data shapes, and they disagree on identity, the objective name and the structure/plot model.** The five are SYSTEMS S-32, DATA_MODEL §3.1/§6, WORLD_ARCHITECTURE §10, PERSISTENCE §5.4 and VERTICAL_SLICE §5.4.
   - `DATA_MODEL.md` has **no `piece`/`building` content kind** and **no `piece_ref` cross-reference row**, but its own quest objective uses `piece_ref` (see §15).
6. **No building code exists in Phase 1.**
   - `EntityKind.Building` ("bld") exists (`src/Domain/EntityKind.cs:17,40`).
   - `construct_building` is named as "building (M7)" in the not-built objective list (`src/Domain/Quests/Quests.cs:146`).
   - The save writes no `buildings.msgpack` (`src/Persistence/SaveModel.cs:24-32`). The save schema is 13 (`src/Persistence/SaveModel.cs:20`).
7. **Dimension data is thin in the normative docs and lives mainly in asset reports.**
   - The asset reports give a 3.0 m wall module, 2.6 m walls, a 2.20 m door frame and a 32° roof pitch (`docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:149-154`; `docs/ASSET_MATERIAL_PASS_2026-09-24.md:61-73`).
   - Phase-1 region content gives 0.4 m wall thickness, 1.6 m door openings, a 2.4 m door height and a 0.35 m body radius (`content/regions/ashen_hollow.yaml:63-78,143-144`; `content/config/base_speeds.yaml:10`).
   - Art and domain disagree on the Phase-1 buildings' size (§15, C20).

---

## 1. Authority map for building

- **[FACT] Authority order.**
  - Charter first, then `DECISIONS.md`.
  - Then the specialist docs: ARCHITECTURE, DATA_MODEL, PERSISTENCE, WORLD_ARCHITECTURE, SYSTEMS, PROGRESSION.
  - Then PROTOTYPE (what), ROADMAP (when), VERTICAL_SLICE and GAMEPLAY_LOOPS.
  - Then RISK_REGISTER.
  - Below all of these: design-extension docs, then orientation and provenance docs (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:13-31`).
  - Owner rulings "take precedence over the conflicting text" until written in (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:33`).
  - Conflicts go to reconciliation: "Do not silently implement the future document. Reconcile the conflict at the owning milestone." (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:37`).
- **[FACT] Milestone ownership.** `docs/INDEX.md:90` maps `WORLD_BUILDING_AND_PROPERTY_DESIGN.md` to "M7 (Building v1)". `CRIME_LAW_REPUTATION_AND_JUSTICE.md` maps to "M7 (factions, reputation); crime is Phase 3" (`docs/INDEX.md:89`).
- **[FACT] Status of the design doc.** `WORLD_BUILDING_AND_PROPERTY_DESIGN.md` is "Strong working design; future implementation" (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:3`). It is directional, not normative, until M7 reconciles it (AGENTS.md; `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:29`).
- **[FACT] Phase placement.**
  - SYSTEMS puts S-32 Buildings in Phase 2 (`docs/SYSTEMS.md:426`).
  - PROTOTYPE excludes "Building / construction (any piece)" from Phase 1 (`docs/PROTOTYPE.md:32`). It also excludes building persistence (`docs/PROTOTYPE.md:265`) and settlement growth, hireling housing and home defense (`docs/PROTOTYPE.md:50`).
  - GAMEPLAY_LOOPS: "BUILD | Not in Phase 1" (`docs/GAMEPLAY_LOOPS.md:282`).
- **[FACT] M7 is not yet authorised.**
  - Claude was authorised only through M6. After that the owner plays and reviews before M7 (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:317-323`).
  - The untracked V4 handoff (authority unconfirmed) says "Do **not** begin M7 until Phase-1 consolidation, performance proof, and required playtest work are finished" (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:20`) and "M7 remains unauthorized" (`…V4.md:527`).

---

## 2. Owner rulings: where each is recorded, and what conflicts

| # | Ruling | Recorded? | Conflicting or tense text |
|---|---|---|---|
| 1 | Navigation not Godot-authoritative; deterministic, headless, replayable, in the domain; derived from structure state; responds to edits; works across seams | **Not recorded** anywhere in the snapshot or the untracked docs. Searches for "storey", "Godot Navigation", "NavigationServer" and "radial" in `docs/` find no navigation ruling. Supporting text exists: D-11 (`docs/DECISIONS.md:234`); transient pathfinding (`docs/ARCHITECTURE.md:164`); "Persisting live AI, pathing, or animation state" rejected (`docs/SYSTEMS.md:443`); S-32 owns "navmesh dirty regions" as transient domain state (`docs/SYSTEMS.md:356`) | `docs/WORLD_ARCHITECTURE.md:435` (navmesh in the presentation/streaming table, "Recast-style, baked per cell"); `docs/PERSISTENCE.md:83` (navmesh rebuilt by "Baking / streaming pipeline"); `docs/RISK_REGISTER.md:290` (RK-14 validation "needs the engine"); `docs/ROADMAP.md:284-285` ("the navmesh updates on placement"; "navmesh path test"); code: Godot `NavigationRegion3D` bake in the spike (`src/Presentation/Spike/SpikeScene.cs:157-181`) |
| 2 | Building v1 is one storey: no upper floors, stairs, elevators, climbing, general vertical nav, voxels or structural physics | **Not recorded.** The "no voxel / no structural physics" part is D-08 (`docs/DECISIONS.md:176-178`) | No conflict for v1. The charter's eventual list includes "towers" (`docs/PROJECT_CHARTER.md:512,563`). `docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:34-35` names stairs and lifts, but only as hidden *interior-transition* devices, not player building. The Kal cultural hint "vertical foundries, landing shelves, cliff structures, roof access and balconies" is at `docs/RACES.md:195-197`. The kit already has `building_step` at 1.2 m (`docs/SCALE_AUDIT_REPORT.md:184`) |
| 3 | Smallest useful faction set; no global morality meter; separate layers; one act can move two factions differently; information not global | Partly: `docs/PROGRESSION.md:436` ("same act may raise standing with one faction and lower another"); "never decides who attacks" (`docs/PROGRESSION.md:444`, `docs/SYSTEMS.md:309`); `docs/ROADMAP.md:282,284`; "Remote places do not learn about crimes instantly" (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:114`) | Building touchpoint: reputation gates "settlement-building rights" (`docs/PROGRESSION.md:440`), while jurisdiction decides "whether building is permitted" (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:81`). These are two different gates, which fits the ruling as long as they stay separate |
| 4 | C10 wolf-den requirement retired | **Recorded**: `docs/PROTOTYPE.md:298` ("Revised by owner ruling (2026-09-24)"); `docs/M6_STATUS.md:105,141,196` | The untracked V4 handoff calls this direction "not yet formally owner-ratified" (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:611-618`, authority unconfirmed). That is stale against PROTOTYPE:298 |
| 5 | No core action may require a radial menu | **Recorded**: `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:639-670`, which explicitly covers "building function" (line 645). Also untracked `G:/UNNAMED/docs/PHASE1_HUD_UI_CONTROLS_AND_FEEDBACK_SPEC.md:17-23` ("building action") and V4 handoff lines 78-82 | `docs/HUD_INPUT_AND_ACTIONS.md:30` lists "radial menus" as an access method; line 36: "Controller users should retain the same underlying capability through context/radials" (tension, see C24) |
| 6 | No networking in M7; seams only | **Recorded**: D-12 (`docs/DECISIONS.md:248`); SYSTEMS C-10 (`docs/SYSTEMS.md:25`); `docs/ARCHITECTURE.md:342`; `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:197,201` | None. PvP "building damage" griefing is explicitly "Do not solve these now" (`docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:753-756`) |
| 7 | Engine-independent C# authority; commands in, events out; dotted definition IDs; ULID instance IDs; sparse deltas | **Recorded**: D-02/D-04/D-05/D-11 (`docs/DECISIONS.md:96-141,232-242`); SYSTEMS C-1..C-7 (`docs/SYSTEMS.md:16-22`) | None in building docs. The VS persistence row lacks an instance ID (C1) |

---

## 3. D-08, quoted in full

`docs/DECISIONS.md:174-184`:

> **D-08 — Building: socket/snap assembly, explicitly not a structural simulator**
>
> **Decision.** Player building is **socket- and snap-based assembly** of authored pieces. There is **no structural integrity simulation, no physics-driven collapse, and no load-bearing calculation.**
>
> **Alternatives considered.** Voxel building (deeply satisfying, enormous cost, changes the art direction, poor fit for authored fantasy architecture); free-form placement (flexible, produces ugly and buggy results, hard to guarantee navigability); full structural simulation (a second game we are not making — the charter says "do not design a structural-engineering simulator unless justified", and it is not justified here).
>
> **Why selected.** Snapping guarantees that player-built structures remain **navigable** (NPCs and companions can path through them) and **reliable to persist** (a piece is a row in a table, not a physics resolution). Both properties are prerequisites for companions and settlement NPCs to function in and around buildings.
>
> **Consequences.** Less creative freedom than voxel or free-form building; players cannot build arbitrary shapes. Mitigated by a generous, well-designed piece catalogue and free rotation/socketing rather than by loosening the structural model. "Damage" is a per-piece health value applied by explicit rules, not emergent physics.
>
> **Revisit if.** Playtesting shows the snap catalogue is the actual limiter on player expression — the response is more/better pieces, not a physics engine.

Related restatements:
- [FACT] "Structural simulation for buildings (D-08)" is explicitly deferred (`docs/ARCHITECTURE.md:345`).
- [FACT] It is an anti-pattern: "Any structural-integrity or physics-collapse simulation for buildings | D-08" (`docs/SYSTEMS.md:444`).
- [FACT] WORLD_ARCHITECTURE assumptions: "No structural physics anywhere in building or terrain (D-08)" (`docs/WORLD_ARCHITECTURE.md:484`).
- [FACT] It is an accepted risk: "Player freedom deferred: no voxel or free-form building … The response to player-expression limits is a better piece catalogue, never a physics engine or a looser structural model" (`docs/RISK_REGISTER.md:388`).
- [FACT] ROADMAP M7 note: "If playtest says expression is limited, the answer is **more pieces, not physics**" (`docs/ROADMAP.md:286`).
- [FACT] Origin: PHASE_0 STEP 12 asked for placement, "snapping where appropriate", foundations, structural pieces, crafting stations, storage, ownership, damage, repair, defenses, NPC assignment, persistence and possible attacks. It closed with "Do not design a structural-engineering simulator unless justified." (`docs/PHASE_0.md:431-451`).

---

## 4. Requirements, by source

### 4.1 PROJECT_CHARTER §13 and §14 (highest authority)

- [FACT] "The player should eventually be able to build extensively." (`docs/PROJECT_CHARTER.md:496`)
- [FACT] The ladder: "campfire → camp → shelter → cottage → home → workshop → estate → fortified homestead → settlement" (`docs/PROJECT_CHARTER.md:500`).
- [FACT] Constructible things: foundations, walls, floors, roofs, doors, windows, fences, gates, towers, crafting rooms, storage, gardens, farms, animal pens, magical structures, defensive structures, workshops, bedrooms, libraries, trophy rooms (`docs/PROJECT_CHARTER.md:502-523`).
- [FACT] "Building should integrate with gameplay rather than be purely decorative." (`docs/PROJECT_CHARTER.md:525`). Buildings can provide crafting, storage, food production, defenses, research, magical functions, hireling housing, merchants, resource processing and training (`docs/PROJECT_CHARTER.md:527-538`).
- [FACT] Home defense:
  - "The player's property may occasionally face threats depending upon where it is built and what the player has done." (`docs/PROJECT_CHARTER.md:544`)
  - Threats: bandits, hostile factions, monsters, undead, wild animals, magical events (`:546-553`).
  - Preparation: walls, gates, traps, guards, hirelings, defensive magic, towers, trained animals (`:555-564`).
  - "Do NOT make attacks so frequent that building becomes annoying." and "Allow players who dislike base defense to greatly reduce or disable its frequency." (`:566-568`).

### 4.2 SYSTEMS S-32 Buildings & Construction, in full (`docs/SYSTEMS.md:350-357`)

- **Responsibility.** "Own player-placed structures, ownership, and per-piece condition."
- **Owns.** "Building instance records (piece definition ref, socket graph position/rotation, owner, cell), per-piece health and damage state, repair state, stations and storage attached to a building, NPC/companion assignment to anchors, garden/farm plots, defense structures and their ammunition/cooldown state, settlement-level aggregates (population capacity, services offered)."
- **Reads.** "S-18 (piece definitions and socket compatibility), S-14 (materials to consume and storage contents), S-20 (cell delta), S-09/S-10 (masonry skill and known construction techniques), S-29 (construction objectives), S-25 (assignees), S-12 (raid damage events), S-24 (NPC housing)."
- **Persistent.** "Every building instance (these are pure player-authored state and are never regenerable), piece damage, assignments, container contents via S-14, farm plot crops and growth deadlines, defense state."
- **Transient.** "Ghost/preview placement, snap candidates, navmesh dirty regions, construction progress."
- **Interface.** `PlacePiece(pieceDefId, socketId, rotation)`, `RemovePiece`, `DamagePiece`, `RepairPiece`, `AssignOccupant`, `IsValidPlacement`.
- **Events.** `PiecePlaced`, `PieceRemoved`, `PieceDamaged`, `PieceDestroyed`, `BuildingCompleted`, `SettlementStateChanged`, `HomeAttacked`.
- **The rule.** "**No structural simulation** (D-08): snapping guarantees navigability and persistence; 'destruction' is per-piece health applied by explicit rules. Attack frequency is a player-facing option and defaults low (charter §14)."

Related S-entries:
- [FACT] **S-14 Inventory & Containers** reads "S-32 (owned property/container ownership)" (`docs/SYSTEMS.md:181`).
  - `Transfer(itemId, fromContainer, toContainer, count)` is "the **only** sanctioned path for item movement" (`docs/SYSTEMS.md:184`).
  - `TransferRejected(capacity|permission|category)` exists, so container permission is S-14's (`docs/SYSTEMS.md:184`).
  - Container definitions own "capacity, slot rules, allowed categories, owner" and "container access permissions" (`docs/SYSTEMS.md:180`).
- [FACT] **S-17 Crafting** reads "S-32 (station buildings)" (`docs/SYSTEMS.md:209`).
  - As built in Phase 1: "`CraftCommand` works a known recipe at a station of its kind within reach, from carried materials". The runtime `CraftingSystem` "owns no state" (`docs/SYSTEMS.md:213`).
- [FACT] **S-20 World Persistence** reads "S-32 (buildings)" (`docs/SYSTEMS.md:238`).
- [FACT] **S-22 Player** reads "S-32 (build mode)" (`docs/SYSTEMS.md:256`).
  - As built (M6): "Phase 1's interactables are doors and switches, both named by an `InteractCommand` and measured from the body to the thing's footprint." (`docs/SYSTEMS.md:260`)
- [FACT] **S-24 NPCs** persist "anchor reassignment after building changes" (`docs/SYSTEMS.md:277`).
  - As built, M4 has "no tiers, no schedules" and NPCs "stand where the region places them" (`docs/SYSTEMS.md:280`).
- [FACT] **S-25 Companions** read "S-32 (housing assignment)" and persist "housing/settlement assignment" (`docs/SYSTEMS.md:286-287`).
  - As built, anchors and several other features are "Not built (owner ruling)" (`docs/SYSTEMS.md:290`).
- [FACT] **S-29 Quests** reads "S-32 (building)" (`docs/SYSTEMS.md:325`).
- [FACT] **S-34 Dungeons & Interiors** reads "S-32 (player structures inside interiors)" (`docs/SYSTEMS.md:378`). See C17.
- [FACT] **S-36 Merchants** reads "S-32 (settlement market state)" (`docs/SYSTEMS.md:396`).
- [FACT] **S-38 Fast Travel** reads "S-32 (settlement network membership)". Unlock methods include "construction" (`docs/SYSTEMS.md:414,417`).
- [FACT] **Phase mapping.** S-32 is Phase 2. Deferred: "farming/crop simulation beyond garden plots, settlement population growth simulation, … post-cap mastery and Great Works" (`docs/SYSTEMS.md:426-427`).
- [FACT] **Anti-pattern.** "Persisting live AI, pathing, or animation state | Rebuilt at load" (`docs/SYSTEMS.md:443`).

### 4.3 WORLD_ARCHITECTURE: player buildings and everything nav- or seam-related

**§10 "Player buildings as persistent exceptions"** (`docs/WORLD_ARCHITECTURE.md:403-420`), all rows:

- "Per `D-08`, building is socket/snap assembly of authored pieces; a piece is a row, not a physics resolution." (`:405`)
- **Identity.** "A building is a ULID-keyed structure … with a footprint of one or more cells; pieces are ULID-keyed rows with a `def_id` and a socket path" (`:409`).
- **Persistence.** "Written to `buildings.msgpack`; the owning cells are marked dirty with reason `buildings`" (`:410`).
- **Cell unload.** "The building record is `pinned`. Geometry streams out; the record stays in memory while the region is loaded and always survives in the save. A building is never a casualty of streaming" (`:411`).
- **Cross-cell footprints.** "A building may span cells. Ownership/geometry is stored once on the structure and referenced by each cell's delta, so a cell boundary cannot split a structure's state" (`:412`).
- **Placement legality.** "Validated by domain rules at command time (terrain slope, socket graph connectivity, no blocking of authored story geometry, no overlap with a pinned spawn) — never by physics" (`:413`).
- **Navigability.** "Guaranteed by the socket/snap model (`D-08`) so NPCs and companions can path through player structures. Navmesh around buildings is rebuilt per cell on change, debounced" (`:414`).
- **Damage.** "Per-piece health applied by explicit rules; repaired by explicit command. Damage is persisted per piece" (`:415`).
- **NPC assignment.** "Buildings hold ULID references to assigned NPCs; assignment state persists; abstract tiers know a settlement has a steward/guards without simulating them" (`:416`).
- **Defense.** "Attack frequency, alert level and `disabled_attacks` persist. … this is a persisted gameplay setting, not a client preference" (`:417`).
- **Ownership transfer.** "Out of scope in Phase 0; the `owner` field is a ULID so it can be added later without a schema break" (`:418`).
- **Tiers.** "A building in a loaded region whose cell is Tier C still consumes/produces abstractly (a forge with fuel and an assigned smith produces at Tier C). … computed from `(state, tick_delta)` on demand" (`:420`).

**Other WORLD_ARCHITECTURE statements:**
- [FACT] **Scale.** Region 2000 × 2000 m; cell 100 × 100 m, "20×20 = 400 exterior cells per region"; sub-cell locality 10–25 m ("Not persisted, not addressed"); interiors are separate spaces (`docs/WORLD_ARCHITECTURE.md:28-32`).
- [FACT] "1 unit = 1 meter"; Y-up; "No region stacking, no underground region layer. Underground content is an interior space" (`:42-44`).
- [FACT] **Cell keys.** `r_<rx>_<rz>:c_<cx>_<cz>` (`:52`). `floormod` is required; a wrong key would "no … navmesh bake will ever match" (`:71`).
- [FACT] **Cell facets** (`:132-143`):
  - "Collision, navmesh, occlusion | Baked from the above | No" — not persisted.
  - "Player buildings | Runtime | Yes (`buildings.msgpack`)".
  - "World flags (doors, switches, traps, destruction) | Runtime | Yes (`cells.msgpack`)".
- [FACT] **Authoring.** "Town footprint, building plots, walls, landmarks | Hand-authored" (`:112`).
- [FACT] **Dirty reasons** are enumerated: `nodes`, `spawns`, `flags`, `entities`, `buildings`, `terrain` (`:158`). The "authoritative dirty set is derived at save time" (`:160`).
- [FACT] **Unloading.** "Cell contains a player building | Treated as `pinned`; … exempt from the 'no dirty bits → discard' path for the *record*, though its geometry is fully streamed out" (`:172`).
- [FACT] **Tiers.**
  - Tier A runs "pathfinding" (`:234`).
  - Tier C runs "construction progress" (`:236`).
  - B/C "May advance … Construction/build progress queued before the player left" (`:284`).
  - B/C may never advance "Combat resolution of any kind", "Damage, death, downing" (`:279-280`).
- [FACT] **Promotion.** Never place an actor at a point "the actor cannot legally occupy (inside geometry, off navmesh, inside a player building without permission)" (`:297`).
- [FACT] **Per-cell hysteresis.** "A cell's tier changes at most once per 2 s | Cell-level work (navmesh, spawn, props) is far more expensive than actor-level" (`:326`).
- [FACT] **What persists far away.** "Player buildings | Yes | C/D, `pinned` | Stored as explicit exception records in their cells; never lossy-unloaded" (`:392`). "Physics, combat, dialogue, pathfinding anywhere the player is not | No" (`:397`).
- [FACT] **Streaming table.** "Navmesh | Recast-style, baked per cell, stitched at cell borders, rebuilt debounced on building change | Seam handling is a known hard part; budget a dedicated pass" (`:435`).
  - The contract that follows says presentation "never assigns tiers, never marks cells dirty, and never mutates world state" (`:438`).
- [FACT] **Local risk RK-A2.** "Navmesh stitching at cell seams with player buildings | Broken navmesh silently breaks companions … | Not solved. Dedicated implementation pass; test with a building straddling four cells" (`:469`).
- [FACT] **RK-A6.** Cell size is "expensive to change later" (`:473`).

### 4.4 PERSISTENCE (structures, placed objects, deltas)

- [FACT] **Invariants.**
  - I-1: nothing derivable from the baseline is saved; no engine node paths.
  - I-2: every persisted instance carries a registry ULID.
  - I-3: every delta names its baseline.
  - I-4: "State is written only via domain commands; the save system reads the world-state store, never the scene tree".
  - I-7: every record has a retirement path.
  - I-8: slot keys.
  - (`docs/PERSISTENCE.md:31-39`)
- [FACT] **Not saved** (reconstructed instead):
  - "Navmesh, collision, occlusion, LOD meshes, GPU resources | Baking / streaming pipeline".
  - "Pathfinding, AI blackboards … | Fresh instantiation on tier promotion".
  - (`docs/PERSISTENCE.md:83-84`)
- [FACT] **Layout.** "`buildings.msgpack` # player structures and their pieces" (`docs/PERSISTENCE.md:115`).
- [FACT] **Section semantics.** "`buildings.msgpack` | Regenerable from nothing — a player structure is authored state | Loss is unrecoverable player work | Load **without** it and report every lost structure explicitly; never fail the whole load" (`docs/PERSISTENCE.md:136`).
- [FACT] **Cell delta fields.** `cell_key`, `baseline_hash`, `dirty_reasons` (incl. `buildings`), `flags`, `overrides` (`docs/PERSISTENCE.md:208-212`).
- [FACT] **Created instances (schema 3).** "A persistent instance that no baseline slot generates - a dropped item, a placed chest - is stored whole in the section's `created` list: `instance_id`, `def_id`, `host_cell`, position, (from schema 6) `count`, and (from schema 9) `quality`. It is proven against its host cell's baseline like a slot record." (`docs/PERSISTENCE.md:240`). This is the one existing precedent for a *placed* object.
- [FACT] **Changed world containers (schema 6).** `key`, `instance_id`, `host_cell`, whole contents (`item_id`, `def_id`, `count`, `quality`) (`docs/PERSISTENCE.md:242`).
- [FACT] **§5.4, in full** (`docs/PERSISTENCE.md:248-252`):
  - "A building is a ULID-keyed structure with a footprint of one or more cells; pieces are ULID-keyed rows with a `def_id` and a socket path. … Nothing here is regenerable: a player structure exists nowhere else, so corruption is unrecoverable rather than merely annoying, and the quarantine path must name every lost structure."
  - "`storage` entries reference container ULIDs that live in `entities.msgpack`. … a dangling reference whose target section was dropped is **reported loss**, not a corruption, and must not be re-quarantined."
  - §5.4 gives **no field table**.
- [FACT] **Rebase.** Trigger: "Cell unload; autosave; explicit compaction pass". Outcome: "if the difference is below epsilon, **delete the record**" (`docs/PERSISTENCE.md:286-288`).
  - Cross-cell: "An entity whose *host cell* is pristine — a placed chest … — is stored as an entity record with its `slot_key` naming its host cell, and does not imply a cell record" (`docs/PERSISTENCE.md:292`).
- [FACT] **Baseline proof** covers "cell records, the host cells of entity records, and the host cells of created instances". Otherwise it rebases through a registered `BaselineTransition` or refuses (`docs/PERSISTENCE.md:392-394`). Registered transitions so far: M3f and M6 (`docs/PERSISTENCE.md:398`).
- [FACT] **Quarantine.**
  - "Quarantined section is `cells`/`entities`/`buildings` | Load **without** it … **Partial recovery beats none**".
  - "Reference target is in a **quarantined** section | **Reported loss**" (`docs/PERSISTENCE.md:458,461`).
- [FACT] **Load sequence.** Steps a–n (`docs/PERSISTENCE.md:482-497`) mention cells and entities (h–j) but **no buildings step**.
- [FACT] **Scoped saves.** Unconditional when triggered; capped at one per 30 s. Autosave every 5 min, main-thread cost ≤ 2 ms P99 (`docs/PERSISTENCE.md:521-534`).
- [FACT] **Size budget.** "`buildings.msgpack` | 10 KB–5 MB | Player structures" (`docs/PERSISTENCE.md:553`).
- [FACT] **Tests.** T-01 full-equality round trip; T-03 delta minimality and rebase; T-24 save-size budget (`docs/PERSISTENCE.md:575,577,598`). "RK-P04 | Building volume exceeds the save budget | Promoted as RK-06" (`docs/PERSISTENCE.md:617`).
- [FACT] **Schema-change procedure.** Every bump adds a `vN/` fixture, a migration step and frozen section types in the same commit (`tests/Persistence.Tests/Fixtures/README.md:12-19`).

### 4.5 DATA_MODEL (kinds, identity, fields)

- [FACT] **The closed kind table has no building, piece, structure, plot or station kind** (`docs/DATA_MODEL.md:44-68`).
  - "This list is closed and normative"; a kind with no directory, or a directory with no kind, is a validator error (`docs/DATA_MODEL.md:36`).
- [FACT] **Region as implemented (M3–M6)** lists `structures` (box or circle footprints), `doors` ("a footprint that blocks while its `world.*` flag is 0, toggled by hand"), `containers`, `nodes`, `stations`, `npcs`, and M6's `switch` and `barrier` (`docs/DATA_MODEL.md:72`).
- [FACT] **Instance prefixes** (`docs/DATA_MODEL.md:123`):
  - "`bld` building, `cnt` container, … `plt` farm plot".
  - "Generated **only** by the Entity Registry (S-02)".
  - "Content files never contain instance IDs".
  - There is **no piece prefix**.
- [FACT] **Three tests** decide persistence: sharing, baseline, authority (`docs/DATA_MODEL.md:129-133`).
- [FACT] **§3.1 row** (`docs/DATA_MODEL.md:148`): "Building piece | sockets, material cost, health, nav footprint, station capability | transform, owner, current health, repair state, attached storage, occupant assignment".
- [FACT] **Naming corollary.** "base-vs-current pairs are always named so (`durability_max` … `durability_current`) — a bare `durability` is a schema-review failure" (`docs/DATA_MODEL.md:158`). [INFERENCE] Piece health should be `health_max` (definition) and `health_current` (instance). VS §5.4's bare `health` would fail this review.
- [FACT] **NPC services.** `trade|repair|train|craft_station|rest|stable|bank` (`docs/DATA_MODEL.md:295`).
- [FACT] **Recipe station field.** The §4.9 example uses `station_ref: station.forge` (`docs/DATA_MODEL.md:396`). As built (M3f), a recipe names "the `station` *kind* … (`forge`, `anvil`: a region places stations of each kind)" (`docs/DATA_MODEL.md:414`).
- [FACT] **Objective types.**
  - "`construct_building` (`piece_ref` or `tag`, `count`, `at?`) | matching building instances placed".
  - "`upgrade_settlement` (`settlement_ref`, `metric`, `threshold`)" (`docs/DATA_MODEL.md:453-454`).
  - Reward kinds include `property` (`docs/DATA_MODEL.md:468`).
- [FACT] **FactionDefinition** has `territory: [region|cell]` and `laws: [{offense, response, bounty_base}]` (`docs/DATA_MODEL.md:561-562`). [INFERENCE] This is the natural hook for jurisdiction.
- [FACT] **Cross-reference patterns** (`docs/DATA_MODEL.md:807-819`) have **no `piece_ref`, `building_ref`, `plot_ref` or `settlement_ref` row**. "A `*_ref` field matching no pattern is an error … `XREF004`" (`docs/DATA_MODEL.md:821`).
- [FACT] **Save-version-sensitive surfaces** include "building instance records (piece refs, socket IDs, transforms, owner, health, assignments — regenerable from nothing, so corruption is unrecoverable)" (`docs/DATA_MODEL.md:846`).

### 4.6 GAMEPLAY_LOOPS: the BUILD loop and the §15 phase table

- [FACT] **Loop edges.**
  - CRAFT → BUILD "pieces, stations, processed goods" (`docs/GAMEPLAY_LOOPS.md:52`).
  - BUILD → CRAFT "stations, storage, housing, research"; → RECRUIT "housing, food, defense, income base"; → QUEST "construction objectives, settlement state" (`:57-59`).
  - BUILD -.-> FIGHT "threat events target the property" (`:71`).
- [FACT] **§7 BUILD, all bullets** (`docs/GAMEPLAY_LOOPS.md:126-135`):
  - Verbs: "claim a site, place a piece, snap, rotate, socket, assign, station, store, repair, upgrade, expand, defend."
  - Inputs: materials/components, "a legal site (EXPLORE)", labor, "something worth protecting".
  - Outputs: crafting stations, storage, food production, defenses, research, magical functions, hireling housing, merchants, resource processing, training facilities, settlement state.
  - Reward: "capability, not currency: the home *enables* other loops rather than paying out."
  - Drains: "the largest material sink in the game, deliberately"; "upkeep and repair, staff wages and food, and the risk of loss to FIGHT".
  - "**Anti-annoyance constraint (charter §14, hard requirement)** — … players who dislike base defense must be able to **greatly reduce or disable** its frequency. This is an options requirement, not a difficulty setting, and it must exist in the same phase BUILD ships."
  - "**D-08 constraint** — socket/snap assembly only. … This is what guarantees that player-built structures stay navigable for NPCs and companions and remain cheap to persist (RK-06)."
- [FACT] **Moment-to-moment commit** includes "place a piece" (`docs/GAMEPLAY_LOOPS.md:181`). The session loop's deposit step includes "start a build" (`:195`).
- [FACT] **Long-term loop.** "Home and settlement transformation — the charter's campfire → … → settlement progression" (`docs/GAMEPLAY_LOOPS.md:207`).
- [FACT] **Anti-isolation invariant 4.** "BUILD must pay out in capability, not currency, or BUILD becomes a money printer." (`docs/GAMEPLAY_LOOPS.md:220`)
- [FACT] **Exploit E-6.** "BUILD as a passive income engine": capped by "staffing, land, inputs, and upkeep"; "production is not advanced abstractly without staffing and inputs". Test: "advance the clock 30 in-game days with the player absent and assert settlement output stays under a stated ceiling" (`docs/GAMEPLAY_LOOPS.md:237`).
- [FACT] **Exploit E-10.** Sinks include "construction materials and property upkeep (Phase 2+)". A "sink must be non-discretionary" (`docs/GAMEPLAY_LOOPS.md:241`).
- [FACT] **§15 phase table, BUILD row** (`docs/GAMEPLAY_LOOPS.md:282`):
  - Phase 1: "**Not in Phase 1.** A campfire/rest point is the only exception, if needed for the save/load proof."
  - Phase 2: "Basic building: foundations, walls, a station, storage, one upgrade tier; property threats with the charter-mandated frequency reduction/skip option."
  - Deferred: "Settlement growth; NPC assignment at scale; defenses; estate and fortified homestead tiers."
- [FACT] "Companions, factions, settlement simulation, base defense at frequency, world events …" are out of scope in Phase 1 (`:287`).

### 4.7 VERTICAL_SLICE (Phase 2): every building item

- [FACT] Authority: "the slice does not renegotiate the prototype's architecture, it adds content and systems through it" (`docs/VERTICAL_SLICE.md:6`).
- [FACT] **Region.** Kaldrun Reach, 2 000 × 2 000 m, 400 cells of 100 m. Streaming radius 400 m (9×9 cells). "The cell is the streaming unit *and* the delta-save unit" (`docs/VERTICAL_SLICE.md:30-31`).
- [FACT] **Town.** Vessmere, ~150 × 150 m, 22 NPCs (8 named + ~14 Tier-B), 6 enterable interiors, footprint "spans 4 exterior cells (2×2)" (`docs/VERTICAL_SLICE.md:44-47`).
  - Functions: "1 smithing station, 1 alchemy station, 1 woodworking station" and more (`:48`).
- [FACT] **Player property.** "**One buildable plot** on the town's north edge, granted by the epic quest at stage 5. This is the slice's only building site." (`docs/VERTICAL_SLICE.md:50`)
- [FACT] **Budget.**
  - "Hirelings | 0 | Home defense, hireling housing, and steward roles need settlement systems the slice does not build" (`:97`).
  - "Building piece families | 4 | Foundation/floor, wall, roof, opening. 4 families × ~6 pieces = ~24 pieces: enough for a cottage with a door and a window (the charter's own ladder step 4). No towers, no gates, no farmland." (`:98`)
  - "Crafting station types | 5 | Forge, alchemy bench, woodworking bench, **player-built workbench** (buildable), and one placeable **smelter**. The player-built workbench is the proof that building integrates with gameplay" (`:99`).
- [FACT] **New persistent state.** "**Building (D-08)** | STEP 16 requires it; it is the only system proving player-created persistent world change. | Placed pieces (piece ID + transform + socket parent + health), plot ownership" (`docs/VERTICAL_SLICE.md:119`).
- [FACT] **§5.4 Building (D-08), in full** (`docs/VERTICAL_SLICE.md:161`):
  - "Socket/snap assembly of authored pieces: no structural simulation, no collapse, no load-bearing math (D-08)."
  - "~24 pieces across 4 families (foundation/floor, wall, roof, opening), grid-snapped to the plot, snapped socket-to-socket, free rotation in 45° steps, invalid overlaps rejected with a visible reason rather than placed-then-fixed."
  - "**One plot**, Vessmere north edge, **40 m × 40 m**, entirely inside one cell (`r_0_0:c_03_07`), granted by epic quest stage 5 — building before stage 5 is impossible, deliberately, so building arrives with narrative weight and so no player structure straddles a cell seam in the slice's only building test."
  - "Three functional pieces only: a **workbench** (crafting), a **storage chest** (container), and a **bed** (rest / time-of-day advance)."
  - "Damage is per-piece `health` applied by explicit scripted events (the one home-defense probe, §8 P1) and repaired with materials at the piece — **never emergent physics damage**."
  - "Persistence is a row: `{piece_def_id, plot_id, cell_id, transform, socket_parent, health, owner_instance_id}`, a world delta (D-05) that must round-trip in save/load."
  - "Companions and town NPCs must path through a player-built doorway — **a slice exit criterion** (E4, §11.1), because D-08 chose snapping specifically to guarantee it."
- [FACT] **Objective type.** "`build_piece` | Placed piece of a definition exists on an owned plot | Yes (stage 5)" (`docs/VERTICAL_SLICE.md:187`).
- [FACT] **Epic stage 5.** "**Claim the works** — the plot is granted; the ritual needs a forge you own | `build_piece` (forge or workbench), `talk_to` | Vessmere plot | Player builds at least a workbench + the smelter piece" (`docs/VERTICAL_SLICE.md:215`).
  - Stage 8 is "a multi-input ritual at the player's forge" (`:218`).
  - The integration argument: "stage 5 needs a functional placed building piece (D-08) that persists (D-05)" (`:230`).
- [FACT] **Pillar P7.** "the epic forging stage requires Blacksmithing 12 and a player-built forge" (`docs/VERTICAL_SLICE.md:266`).
- [FACT] **Non-goals.**
  - "Settlement founding, settlement growth, hireling housing, steward/merchant/farmer hirelings".
  - "Home defense as a real system (frequent attacks, traps, guards, trained animals) | One scripted probe proves the seam".
  - (`docs/VERTICAL_SLICE.md:332-333`)
- [FACT] **Exit criteria.**
  - "E4 | A player-built structure (workbench + walls + door + roof) persists across save/load, and **both companions and a town NPC path through the player-built doorway** | T0 + T1 (pathfinding test + recording)" (`docs/VERTICAL_SLICE.md:358`).
  - E12 save ≤ 2 MB after 6 hours, load ≤ 4 s (`:371`).
  - E13 domain suite incl. "building persistence" green headless ≤ 5 min, "no gameplay rule exists in `src/Presentation/**`" (`:372`).
  - E15 adding a creature, item or recipe needs "zero C# changes" (`:374`).

### 4.8 ROADMAP: M7 in full, plus dependencies

- [FACT] **Graph.** M6 → "M7 Factions + reputation + Building v1 [PHASE 2]" → M8 → M9 (`docs/ROADMAP.md:52-56`).
- [FACT] **Forbidden early.** "player building before M4 NPC persistence (companions and settlement NPCs must be able to path through/around structures — D-08)" (`docs/ROADMAP.md:63`).
- [FACT] **Critical link.** "M4 → M7 | Snap building guarantees navigability; navigability is only testable once NPCs and companions actually path (D-08)" (`docs/ROADMAP.md:93`). Assumption 8: "Building (M7) deliberately follows NPC persistence (M4). D-08's navigability guarantee is only meaningful once NPCs actually path." (`docs/ROADMAP.md:454`)
- [FACT] **M7** (`docs/ROADMAP.md:278-286`):
  - **Class.** "FEATURE. **Depends on:** M4 (NPC persistence), M3f (materials). **Phase:** **2**".
  - **Entry.** "NPCs and companions path reliably — D-08's whole justification for snap-based building is that structures must stay navigable and persist reliably."
  - **Work (building).** "Socket/snap placement; foundations, walls, floors, roofs, doors; free rotation and socketing; ownership; per-piece health applied by explicit rules (**no structural simulation, no physics collapse** — D-08); repair; storage containers; one crafting station as a placeable; player-built structure persistence as a sparse world delta (D-05); placement validation that rejects un-navigable or overlapping configurations."
  - **Exit criteria.** "Build a structure, assign an NPC to work in it, verify the NPC navigates in, through, and around it — **including a structure straddling a cell boundary**, which is RK-14's explicitly unsolved case and must be proven here rather than discovered in playtest; save/load preserves every piece with correct ownership and health; damage/repair works and is explicit rather than emergent; the navmesh updates on placement; the same act moves two factions in opposite directions in a fixture."
  - **Proof.** "Playable build; navmesh path test including the straddling-seam case; structure round-trip through save; a reputation fixture table."
  - **Notes.** "Less creative freedom than voxel building is the accepted cost of guaranteed navigability and clean persistence (D-08). If playtest says expression is limited, the answer is **more pieces, not physics**."
- [FACT] **M3 spike.** "2×2 km of untextured heightmap plus a few hundred instanced proxies, a navmesh, and the D-06 tier scaffolding" (`docs/ROADMAP.md:194`).
- [FACT] **M4 deferred (owner ruling).** "tier-transition simulation … NPC schedules, which PROTOTYPE.md puts in the vertical slice" (`docs/ROADMAP.md:249`). Also forbidden in M4: "settlement *simulation* (economy, growth, production) — that is M10" (`docs/ROADMAP.md:254`).
- [FACT] **Early feel test.** "**Owner ruling (2026-09-24):** deferred until a nontechnical Windows playtest build exists; it is **not an M7 entry blocker**" (`docs/ROADMAP.md:274`).

### 4.9 RISK_REGISTER: every building, nav and seam risk

- [FACT] **RK-06 Building persistence and navigability** (`docs/RISK_REGISTER.md:144-158`):
  - Likelihood Medium: "the risk concentrates in navmesh rebuild on placement and in save-size behaviour at hundreds of pieces per cell".
  - Impact High: "a home that half-loads or that companions cannot walk through fails a headline promise."
  - Why it matters: "Player-built structures are also the largest single writer of new baseline-divergent state … hundreds of pieces placed in one cell, each a persisted entity with a D-04 ULID."
  - **Validation.** "Phase 1 or early Phase 2: place ~200 snapped pieces, place one companion and one NPC inside the structure, save, change a `content_version`-independent input, reload, and assert (a) piece count and transforms match, (b) the navmesh rebuild lets both actors path from the doorway to an interior socket, (c) the delta save grows sub-linearly with piece count. All three are automatable headlessly and cost one afternoon."
  - **Fails if.** "Any piece fails to round-trip, either actor cannot path to an interior socket after reload, or save size grows super-linearly with piece count. The navigation assertion is the one most likely to fail first."
  - **Mitigation.** "D-08's socket model retained strictly — no free-form placement creep …; every piece is a registry-created (D-10) entity with a D-04 instance ID and a health value applied by explicit rules; navmesh updates batched per building edit rather than per piece; a piece-count ceiling per cell defined by the Phase-1 measurement and treated as a content constraint (RK-08)."
- [FACT] **RK-14 Navmesh stitching at cell seams under player buildings** (`docs/RISK_REGISTER.md:280-296`):
  - Likelihood Medium: "D-08 guarantees pieces snap, but says nothing about the navmesh across a cell boundary."
  - Impact High: "a broken navmesh does not announce itself; companions and settlement NPCs simply path wrong or stall."
  - **Risk.** "A player-built structure straddling a cell boundary produces a navmesh seam that does not stitch, so companions and settlement NPCs cannot path through the player's own home."
  - Why it matters: "Cell-based streaming … rebuilds navmesh per cell, and a building that crosses a seam is the case where two independently built navmeshes must agree. … both RK-06 (building navigability) and RK-05 (companion reliability) are gated on it."
  - **Validation.** "Phase 1/early Phase 2, and it needs the engine: place a snapping structure across a cell boundary, then ask a companion to path from one side to the other. Assert a complete path exists and is traversable, then force a cell unload/reload and repeat. The cheapest useful version is a single straddling wall with a doorway."
  - **Fails if.** "No path is found, the path exits and re-enters the structure, or the path is valid before a cell reload and invalid after — the last outcome meaning navmesh state is not surviving streaming, which is worse than a seam bug."
  - **Mitigation.** "Rebuild navmesh across a stitched multi-cell neighborhood rather than per cell in isolation, with a seam overlap margin; treat any change to a cell's building set as dirtying its neighbors' navmesh as well (this is also a new dirty-flag reason and must be registered with the RK-11 taxonomy); constrain or warn on placement that would straddle a seam until stitching is proven; add the straddling case to the Phase-2 test set."
- [FACT] **RK-05 Companion AI.** Failure includes "a companion that blocks doorways, breaks pathing through player-built structures (D-08)". Phase-1 metrics: "stuck events, path-failure count, friendly-fire incidents, and time-to-kill ratio" (`docs/RISK_REGISTER.md:136-138`).
- [FACT] **RK-11 Dirty-flag completeness.** The validation round-trips each mutation class, including "place a building piece", plus a negative bypass test (`docs/RISK_REGISTER.md:234`).
- [FACT] **RK-02.** The greybox stress scene includes "a navmesh" (`docs/RISK_REGISTER.md:86`).
- [FACT] RK-P04 maps to RK-06 (`docs/RISK_REGISTER.md:344`). The accepted risk "no voxel or free-form building" is at `docs/RISK_REGISTER.md:388`.
- [FACT] **Unaddressed review finding C-8** (provenance only, `docs/reviews/ARCHITECTURE_AI_SESSION_REVIEW.md:217-239`). "Base defense and world events cannot legally occur off-screen". The Tier B/C never-list forbids damage and combat, so "`HomeAttacked` has no tier-legal producer". The review suggests "a closed set of abstract-resolution outcomes … or state plainly that raids and ambushes require a Tier A cell". [FACT] No later doc addresses it (grep of REVIEW.md, WORLD_ARCHITECTURE and RISK_REGISTER finds nothing).

### 4.10 WORLD_BUILDING_AND_PROPERTY_DESIGN, all sections (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md`)

- **Status** "Strong working design; future implementation"; "M2 impact: None" (`:3-4`).
- **§1 Composition.** Hand-authored macro geography; "deterministic procedural assistance" for clutter and minor resources; handcrafted important spaces. "The goal is a world players can learn and remember" (`:8-14`).
- **§2 Region/cell.** "Cells are streaming/simulation/persistence units. Player buildings are persistent world-state changes layered over baseline world generation. Streaming reconstructs authored baseline + deterministic baseline + persistent deltas + player construction." (`:21-23`)
- **§3 Seamless world.** Towns entered "without visible loading screens". Interiors "may technically occupy separate streamed spaces", with transitions hidden via "doors; caves; tunnels; stairs; lifts; gates; magical thresholds; Otherways" (`:27-38`).
- **§4 Building freedom.** "> If the terrain, jurisdiction and physical constraints permit a structure, the player should usually be allowed to build it." "Do not restrict construction to a small set of pre-designated 'player home plots' unless a settlement's law requires it." "The wilderness is the broadest building space, but freedom comes with consequences." (`:44-50`)
- **§5 Property states.** `Unclaimed`, `WildernessClaim`, `Leased`, `Owned`, `FactionGranted`, `SettlementControlled`, `PlayerSettlement`. "These are design concepts, not present schema commitments." (`:54-64`)
- **§6 Jurisdiction.** Kinds: none, tribal, village, town, city, faction, religious, disputed.
  - It can determine "whether building is permitted; taxes/rent; required permits; prohibited crafts; weapon restrictions; necromancy/magic law; guard protection; theft/trespass rules; available services".
  - "Different cultures should have genuinely different laws." (`:68-91`)
- **§7 Wilderness.** Advantages: little or no tax, freedom of construction, restricted crafts allowed, resources, settlement founding. Costs: danger, distance, "no automatic guard response", logistics, maintenance, raids.
  - "Base attacks should be **rare enough not to make building annoying**, and ideally configurable or reducible through defenses, reputation and location choice." (`:95-112`)
- **§8 Settlement property.** Rent, lease, purchase, quest or faction grant, inherit. Benefits: safety, services, markets, banks/storage, fast travel, law, social access. Costs: taxes, rent, building codes, cultural restrictions, limited land (`:116-140`).
- **§9 Growth.** "camp → shelter → cabin → homestead → workshop → hamlet → village". "Growth should result from capabilities and population rather than a 'Become Mayor' button." NPCs are attracted by beds/housing, safety, food, employment, crafting stations, trade, transport, resources and religious/cultural facilities (`:144-160`).
- **§10 NPC construction.** Use "authored house archetypes; modular room kits; approved footprints; furniture sets; upgrade paths … without asking general AI to solve arbitrary architecture" (`:166-174`).
- **§11 Building technology.** "Use socket/snap/module systems where helpful for: persistence; pathfinding; structural legibility; AI use; visual polish. The player should still have enough freedom that every home does not look identical." (`:178-186`)
- **§12 Settlement simulation** may track population, housing capacity, food, safety, employment, production, trade access, services, reputation, infrastructure, jurisdiction and travel connectivity. "Do not over-simulate before these values produce meaningful gameplay." (`:190-205`)
- **§13 Economy.** Construction consumes lumber, stone, metal, textiles, tools, labor, specialist services, magical/technological components. "Remote construction therefore creates contracts, transport needs and opportunities" (`:209-220`).
- **§14 The Other.** Later: Otherfold settlements, Othergate hubs, regulation of gate construction, Otherwrought structures (`:224-230`).
- **§15 Rule.** "> Property should feel like part of the world economy and jurisdiction, not a detached housing minigame." (`:234`)

### 4.11 Other documents with building statements

- **INVENTORY_STORAGE_AND_LOGISTICS** ("Strong working direction", owning milestone M3b per `docs/INDEX.md:80`):
  - Home/camp storage: "chests; shelves; armories; warehouses; crafting storage; food storage; specialized secure storage". "The player should be able to view ownership/storage information without needing perfect memory." (`docs/INVENTORY_STORAGE_AND_LOGISTICS.md:58-70`)
  - "> Remote view does not automatically mean remote physical access." (`:76`)
  - Warehouses improve "remote construction" (`:109-116`).
  - "> Storage convenience should be something the player can improve through infrastructure rather than a magical property of every chest." (`:130`)
- **CRAFTING_AND_ITEMIZATION.** Its "sockets" are *item* component interfaces ("standardized interfaces/sockets", `docs/CRAFTING_AND_ITEMIZATION.md:286`; compatibility families incl. "sockets", `:259`). They are not building sockets. [INFERENCE] The word "socket" is overloaded three ways: item enchant sockets (`docs/DATA_MODEL.md:184`), weapon asset sockets `SOCK_*` (`docs/WAVE_0_MODULAR_ASSET_STANDARD.md:112,126-139`), and building sockets. M7 naming should disambiguate.
- **WORLD_MATERIALS** (building-relevant rows only):
  - Grey iron "weapons, structural" (`docs/WORLD_MATERIALS.md:39`).
  - Stone uses: Fieldstone "walls, foundations | The default."; Grey slate "roofing, flooring"; Limestone "mortar, building, lime … the bones of ordinary construction"; Granite "monuments, Ker holds … The Kal build in granite and will not quarry worked stone"; Basalt "roads, mills" (`:65-69`).
  - Otherstone: "Gates, anchors, architecture, magical storage, settlement defences" (`:80`).
  - Kal stone: "**Do not treat as a resource.**" (`:81`). CRIME_LAW jurisdictions regulate "Kal ancestral stone" (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:30`).
  - Terrain texture groups: "Timber | oak plank, pine plank, weathered board, log wall, bark, thatch, shingle" and "Construction | dressed stone, rough stone wall, daub, brick, mortar, rubble" (`docs/WORLD_MATERIALS.md:259-260`).
- **TRAVEL_AND_TRAVERSAL.** Fast travel is "earned, discovered, constructed or learned" (`docs/TRAVEL_AND_TRAVERSAL.md:10`). Tier 4 includes "constructed transit infrastructure" (`:40`). There is no statement on roads, bridges or doors as buildable.
- **CRIME_LAW_REPUTATION_AND_JUSTICE.**
  - Jurisdiction regulates "building/trespass" (`:33`).
  - Evidence includes "damaged locks; stolen property" (`:73-74`).
  - Property crimes: "theft, burglary, trespass, vandalism, fencing, fraud" (`:132`).
  - Absence consequences include "attacks on remote property" (`:206`).
  - "Avoid one morality meter." (`:217`)
  - Player settlements may later set policies for "property … taxation, and guard funding" (`:242-244`).
- **WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT** ("Mostly Phase 2+", `:4`):
  - Rest quality ranges from exposed ground up to "private home; high-comfort estate". "Homes therefore provide real utility beyond decoration." (`:251-268`)
  - Shelter properties: "weather protection; insulation; ventilation; comfort; defensibility. Construction quality and materials should influence actual living conditions. A beautiful building with a leaking roof remains a bad shelter." (`:315-325`)
  - Infrastructure damage: "damage poorly protected buildings. Ordinary weather should not constantly punish the player's property." (`:557-568`)
- **QUESTS_DUNGEONS_WORLD_EVENTS_AND_REPOPULATION.**
  - "A cleared crypt may later become … player home" (`:113`).
  - "Where law and physics allow, a cleared location can become a home, stash, faction base, or settlement outpost." (`:117`)
  - Mutation via "construction"; "Persistent overlays should alter authored locations without requiring full replacement." (`:121-131`)
  - Town destruction and reconstruction: "Rebuilding needs real resources, labor, specialists, security, and money." (`:210-220`)
- **SOCIAL_INTERACTION §39.** Enemies may target "rented property … player settlements" (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:660-667`).
- **NPC_SIMULATION.** Adventurers have "home/room/camp" (`:40`), can "acquire property" (`:146`), and can "buy a home; … found a homestead; join a settlement" (`:153-157`).
- **ECONOMY.** Sinks include "property; building" (`:148-149`). Banks provide "safe storage" (`:161`). "Physical goods still require logistics" (`:168`).
- **COMPANIONS.** Companions have "property/home" (`:30`). AI should understand "shield doorway control" (`:146`).
- **STEALTH.** Doors are tracks/signs (`:44`) and noise factors (`:121`). A patrol may "lock entrances" (`:141`).
- **CAMERA_PERSPECTIVE §16.** "Building may permit a somewhat farther camera than ordinary exploration. Do not turn it into an unrestricted RTS camera. Camera distance must remain bounded to avoid scouting abuse." (`docs/CAMERA_PERSPECTIVE_AND_PRESENTATION.md:266-272`). Contextual camera memory includes "building" (`:320-328`). Indoor camera compresses distance and never forces first person (`:76-86`).
- **HUD_INPUT_AND_ACTIONS.** "A hotbar is a convenience layer, not a capability limit." (`:8`). Access methods include "radial menus" (`:30`) — see C24.
- **MODDING seams.** Mods may add "buildings" (`:78`). PvP "building damage" is deferred (`:753`).
- **PROGRESSION.**
  - Crafting skills include `masonry` (`docs/PROGRESSION.md:212`). "**Masonry** is a crafting skill that gates construction pieces." (`:417`)
  - "Crafted gear is the only source of: … constructed structures." (`:422`)
  - Reputation gates "settlement-building rights" (`:440`).
  - Quest XP weights "construction … as highly as combat" (`:114`).
- **SKILLS_AND_DISCIPLINES.** Building skills: "carpentry; masonry; architecture; fortification; utilities; settlement planning" (`docs/SKILLS_AND_DISCIPLINES.md:56-62`). Example synergy: "Geology + Construction → superior foundation choice" (`:111`).
- **RACES.**
  - Kal ancestral architecture: "vertical foundries, landing shelves, cliff structures, roof access and balconies" (`docs/RACES.md:195-197`).
  - Kal mechanics: "innate structural understanding … Natural engineers" (`:207-208`).
  - Mor "cannot open a door because they have no hands" (`:325`). [INFERENCE] Door interaction may need a non-hand path if Mor are playable or NPC actors.
- **FEATURE_DELIVERY_STAGING** (planning guidance only, `:5`). Property/settlement:
  - Proof: "one home; wilderness camp; storage".
  - Vertical slice: "ownership/rent; modular building; tax/jurisdiction; workers".
  - Expansion: "homestead → hamlet; NPC migration; services; trade".
  - Late: "governance policy; destruction/rebuilding; gate hubs; server politics" (`docs/FEATURE_DELIVERY_STAGING.md:251-274`).
- **ENGINE_VALIDATION.** The stress scene includes "a small settlement; 10–20 buildings; several usable interiors" and "chunked navigation" (`docs/ENGINE_VALIDATION.md:22-24,31`).
- **ARCHITECTURE.**
  - `IWorldState` holds "building records" (`docs/ARCHITECTURE.md:162`).
  - "Transient state (derived stats, cached pathfinding, animation state, spatial indices) lives **outside** world state and is rebuilt on load" (`:164`).
  - "Buildings & pieces | Building system | Sparse delta, per-cell exceptions (D-08)" (`:200`).
  - Runtime validation, not headless, covers "building placement" (`:332`). Charter tests include "building persistence" (`:334`).
- **PHASE1 content bible.** "player settlement building" is an explicit Ashen Hollow non-goal (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:1080`). Camera-test locations include "narrow smithy/fence gap; small communal-building interior" (`:772-774`).
- **ENDGAME Great Works** (construct an Othergate, a settlement, a large ship, wards) is "Design-Only / Do Not Implement Yet" (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:188`; `docs/ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md:233-248`).

---

## 5. Data shapes proposed by the docs

| Source | Entity | Fields |
|---|---|---|
| SYSTEMS S-32 (`docs/SYSTEMS.md:353`) | Building instance record | piece definition ref, socket graph position/rotation, owner, cell; per-piece health and damage state; repair state; attached stations and storage; NPC/companion assignment to anchors; garden/farm plots; defense structures + ammunition/cooldown; settlement aggregates (population capacity, services offered) |
| SYSTEMS S-32 (`:357`) | Commands | `PlacePiece(pieceDefId, socketId, rotation)`, `RemovePiece`, `DamagePiece`, `RepairPiece`, `AssignOccupant`, `IsValidPlacement` |
| SYSTEMS S-32 (`:357`) | Events | `PiecePlaced`, `PieceRemoved`, `PieceDamaged`, `PieceDestroyed`, `BuildingCompleted`, `SettlementStateChanged`, `HomeAttacked` |
| DATA_MODEL §3.1 (`docs/DATA_MODEL.md:148`) | Piece definition / piece instance | Def: sockets, material cost, health, nav footprint, station capability. Instance: transform, owner, current health, repair state, attached storage, occupant assignment |
| DATA_MODEL §6 (`:846`) | Building instance record (save-sensitive) | piece refs, socket IDs, transforms, owner, health, assignments |
| WORLD_ARCHITECTURE §10 / PERSISTENCE §5.4 (`docs/WORLD_ARCHITECTURE.md:409-418`; `docs/PERSISTENCE.md:250-252`) | Structure + pieces | Structure: ULID, footprint of ≥1 cells, ownership/geometry stored once and referenced by each cell's delta, `owner` (a ULID), assigned-NPC ULIDs, defense settings (attack frequency, alert level, `disabled_attacks`). Piece: ULID, `def_id`, socket path, per-piece health. `storage` → container ULIDs in `entities.msgpack` |
| VERTICAL_SLICE §5.4 (`docs/VERTICAL_SLICE.md:161`) | Piece row | `{piece_def_id, plot_id, cell_id, transform, socket_parent, health, owner_instance_id}` |
| VERTICAL_SLICE §4 (`:119`) | New persistent state | "Placed pieces (piece ID + transform + socket parent + health), plot ownership" |
| DATA_MODEL §4.11 (`:453-454,468`) | Quest objectives / rewards | `construct_building` (`piece_ref` or `tag`, `count`, `at?`); `upgrade_settlement` (`settlement_ref`, `metric`, `threshold`); reward kind `property` |
| VERTICAL_SLICE §6 (`:187`) | Quest objective | `build_piece`: "Placed piece of a definition exists on an owned plot" |
| WORLD_BUILDING §5/§6/§12 | Property / jurisdiction / settlement concepts | 7 property states; 8 jurisdiction kinds; 12 settlement metrics. "not present schema commitments" (`:64`) |
| PERSISTENCE §5.3 (`:240`) | Created instance (existing precedent for placed objects) | `instance_id`, `def_id`, `host_cell`, position, `count`, `quality` |
| Phase-1 region content (code; `docs/DATA_MODEL.md:72`; `content/regions/ashen_hollow.yaml:63-163`) | Authored structure, door, container, station | structure `{id, box_m [min_x,min_z,max_x,max_z] or circle_m [x,z,r], height_m, clearance_m?}`; door `{key: door.*, flag_ref: world.*, box_m, height_m}`; container `{key: container.*, loot_ref, position_m, stack_slots}`; station `{key: station.*, kind, position_m}` |
| Domain records (code) | Door, barrier, container, station | `DoorSite(Key, FlagId, BoxBlocker ClosedFootprint)`; `BarrierSite(Key, FlagId, Blocker Footprint, Prompt)`; `ContainerSite(Key, LootTableId, XMm, ZMm, StackSlots)`; `StationSite(Key, Kind, XMm, ZMm)` (`src/Domain/Spatial/RegionLayout.cs:12,28,37,46`) |
| WAVE_0 (asset side, weapons only) (`docs/WAVE_0_MODULAR_ASSET_STANDARD.md:148-179`) | Socket schema | `position`, `primary`, `secondary`, `roll`, `depth`, `envelope`, `family`, `role`, `mate` (`antiparallel`/`aligned`). Legal pair: origins within `min(depth_a, depth_b)`, `primary_a·primary_b = −1 ± 0.017` (1°), secondaries within 1°. Naming `SOCK_<interface>_<role>[_<side>]` |

[INFERENCE] **Consolidated minimum the docs imply for Building v1.**
- A content kind for piece definitions: id, family, sockets, material cost, `health_max`, nav footprint, collision footprint, optional station capability, optional container capacity, optional bed/rest function, masonry/technique gate.
- A structure record: `bld_` ULID, owner ULID, host cells, `baseline_hash` proofs, defense settings.
- A piece record: its own ULID and prefix (undecided), def id, parent structure, socket path or parent socket, transform in integer mm and mdeg, `health_current`, repair state, optional container ULID, occupant assignment.

---

## 6. Every dimension number found

**World and cell scale**

| Quantity | Value | Source |
|---|---|---|
| Unit | 1 unit = 1 m; Y-up | `docs/WORLD_ARCHITECTURE.md:42-43` |
| Domain precision | integer millimetres; facing in millidegrees | `src/Domain/Spatial/Kinematics.cs:31`; `docs/PERSISTENCE.md:196` |
| Region | 2000 × 2000 m | `docs/WORLD_ARCHITECTURE.md:28` |
| Cell | 100 × 100 m; 20×20 = 400 per region | `docs/WORLD_ARCHITECTURE.md:29` |
| Locality (not persisted) | 10–25 m | `docs/WORLD_ARCHITECTURE.md:30` |
| Phase-1 prototype | 200 × 200 m, four 100 m cells | `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:5` |
| Phase-1 terrain grid | 5 m spacing, 41 × 41 points | `content/regions/ashen_hollow.yaml:15-19` |

**Vertical-slice building scale**

| Quantity | Value | Source |
|---|---|---|
| Building plot | 40 × 40 m in one cell `r_0_0:c_03_07` | `docs/VERTICAL_SLICE.md:161` |
| Piece catalogue | 4 families × ~6 pieces = ~24 pieces | `docs/VERTICAL_SLICE.md:98` |
| Rotation step | 45° | `docs/VERTICAL_SLICE.md:161` |
| Town | ~150 × 150 m spanning 2×2 cells | `docs/VERTICAL_SLICE.md:45,47` |

**Tiers, timing and save budgets**

| Quantity | Value | Source |
|---|---|---|
| Tier radii (config) | A 150 m, B 600 m, C 2000 m, hysteresis 10 m | `content/config/simulation_tiers.yaml:7-10` |
| Cell tier change rate | at most once per 2 s | `docs/WORLD_ARCHITECTURE.md:326` |
| Tick | 20 Hz; Tier-A AI decisions 10–20 Hz; Tier B 2 Hz | `docs/WORLD_ARCHITECTURE.md:234-235,245` |
| Stress test | ~200 snapped pieces | `docs/RISK_REGISTER.md:154` |
| Save budget | `buildings.msgpack` 10 KB–5 MB | `docs/PERSISTENCE.md:553` |
| Slice save | whole save ≤ 2 MB after 6 h; load ≤ 4 s | `docs/VERTICAL_SLICE.md:371` |
| Save cadence | autosave 5 min; ≤ 2 ms P99; scoped saves ≤ 1 per 30 s | `docs/PERSISTENCE.md:532-534` |

**Body and movement (code)**

| Quantity | Value | Source |
|---|---|---|
| Body radius | 0.35 m (0.7 m diameter) | `content/config/base_speeds.yaml:10` |
| Interaction reach | 1.6 m, measured from the body | `content/config/base_speeds.yaml:11` |
| Stand / crouch height | 1.8 m / 1.15 m | `content/config/base_speeds.yaml:18-19` |
| Jump | apex 1.15 m; tuck radius 0.2 m; clears 0.8 m timber, never the 1.2 m fence | `content/config/base_speeds.yaml:12-17` |
| Movement substeps | ≤ half the body radius, "so a fast step cannot tunnel through a thin wall" | `src/Domain/Spatial/Kinematics.cs:144-145` |
| Companion follow | stands within 2.5 m; catch-up beyond 30 m or after 4 s with no headway | `docs/SYSTEMS.md:290` |

**Phase-1 authored buildings (domain collision)**

| Quantity | Value | Source |
|---|---|---|
| Wall thickness | 0.4 m (e.g. `longhouse_north` z 131.6–132) | `content/regions/ashen_hollow.yaml:66-78` |
| Longhouse | footprint x 36–52, z 124–132 (16 × 8 m); walls 3.2 m; door gap on the east wall, z 127.2–128.8 (1.6 m) | `content/regions/ashen_hollow.yaml:66-70` |
| Forge shed | x 53–63, z 138–146 (10 × 8 m); walls 3.0 m; door gap on the west wall, z 141.2–142.8 (1.6 m) | `content/regions/ashen_hollow.yaml:72-76` |
| Doors | footprint 0.4 × 1.6 m, height 2.4 m | `content/regions/ashen_hollow.yaml:143-144` |
| Smithy fence | 1.2 m tall, leaving a narrow gap | `content/regions/ashen_hollow.yaml:78` |
| Woundmoss beam | 1.7 m tall with 1.3 m clearance: passable crouched, not standing | `content/regions/ashen_hollow.yaml:87` |
| Chest capacity | `stack_slots: 12` (waystation chest); cart 8 | `content/regions/ashen_hollow.yaml:147-149` |

**Asset kit (asset reports; not normative for gameplay)**

| Quantity | Value | Source |
|---|---|---|
| Wall module | "a wall must be exactly 3.0 m so three span 9.0 m" | `docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:153-154` |
| Door frame | "exactly 2.20 m so a 1.80 m Veth fits under it" | `docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:154` |
| Wall height | 2.6 m | `docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:149-150`; single wall panel "3.000 x 2.600 x 3.000 m" at `docs/ASSET_MATERIAL_PASS_2026-09-24.md:189` |
| Beams | at 2.67 m | `docs/ASSET_MATERIAL_PASS_2026-09-24.md:151` |
| Roof | panel 2 m pitched at 32° spans 1.70 m horizontally | `docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:173-174`; projected run 1.696 m at `docs/ASSET_MATERIAL_PASS_2026-09-24.md:22` |
| forge_shed assembly | declared 6 × 6 m (24 pieces), assembled 28 pieces; built envelope 6.0 × 7.102 × 4.729 m; ridge 4.4746 m; eave overhang 0.3922 m | `docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:149,183`; `docs/ASSET_MATERIAL_PASS_2026-09-24.md:63` |
| longhouse assembly | declared 9 × 6 m (32 pieces), assembled 38; envelope 9.0 × 7.102 × 4.729 m | `docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:150,184`; `docs/ASSET_MATERIAL_PASS_2026-09-24.md:64` |
| Footprint rule | "`declared_footprint_m` is the modular wall run on whole 3 m modules" | `docs/ASSET_MATERIAL_PASS_2026-09-24.md:69` |
| Owner ruling 2026-09-24 | the bible fixes placement and overall settlement footprint, "not … the 6 x 6 / 9 x 6 assembled exterior bounds" | `docs/ASSET_MATERIAL_PASS_2026-09-24.md:48-55` |
| Kit collision | "one box per solid piece — 27 for the forge shed, 37 for the longhouse — and **skips `building_door_frame`** entirely" | `docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:520-526` |
| Kit pieces, longest axis | wall_stone 3.0003; wall_timber 3.0; floor_planks 3.0; roof_panel 3.0; beam 3.0; post 2.6; **door_frame 2.44**; fence_panel 2.4; step 1.2; window_frame 1.1; well 2.82; ruin_wall 2.99; road_segment 4.0 vs expected 3.0 (SUSPECT) | `docs/SCALE_AUDIT_REPORT.md:177-188,425` |
| Props, longest axis | iron-banded oak door 2.05; tool chest and sea chest 1.1; carpenter's workbench 1.6; anvil stump 0.7; bellows 1.3; bedroll 1.4; barrel 0.85; crate 1.2 | `docs/SCALE_AUDIT_REPORT.md:264-331` |
| Audit method | measures the **longest axis only**; "a birch tree, an oak door and a coastal ship all measure exactly 0.5 m" under the old normaliser. Widths and thicknesses are not in the audit | `docs/SCALE_AUDIT_REPORT.md:7` |

**WAVE_0 standard (DeepSeek-owned; read only)**

| Quantity | Value | Source |
|---|---|---|
| Category default | building 4.0 m (longest-axis normalisation) | `docs/WAVE_0_MODULAR_ASSET_STANDARD.md:53` |
| Modular exception | "Independent longest-axis normalisation must not be used for modular components"; declare `"nominal_size_m": [width, height, depth]` | `docs/WAVE_0_MODULAR_ASSET_STANDARD.md:80-96` |
| Building module | 5–25k triangles; LODs 3 at 100/40/15/5%; collision "box only" | `docs/WAVE_0_MODULAR_ASSET_STANDARD.md:342,368,381` |
| Conventions | origin at footprint centre, base on the ground plane; pivot overridden per socket family | `docs/WAVE_0_MODULAR_ASSET_STANDARD.md:74-75` |
| Coverage | **No building grid, door, wall or storey dimensions**; the socket vocabulary is weapons only | `docs/WAVE_0_MODULAR_ASSET_STANDARD.md:124-139` |

**Body fit families**

| Family | Height / shoulder width | Source |
|---|---|---|
| standard_humanoid (Veth, Siann, Orenth, NPCs) | 1.80 m / 0.42 m | `docs/CANONICAL_BODY_AND_SKELETON.md:27` |
| compact_broad (Kal) | 1.30 m / 0.58 m | `docs/CANONICAL_BODY_AND_SKELETON.md:28` |
| tall_narrow (Vaskaal) | 2.35 m / 0.34 m | `docs/CANONICAL_BODY_AND_SKELETON.md:29` |
| irregular_heavy (Ondrek) | 2.60 m / 0.82 m | `docs/CANONICAL_BODY_AND_SKELETON.md:30` |

**Spike navmesh (presentation, Godot)**

| Quantity | Value | Source |
|---|---|---|
| Tiling | 8 × 8 tiles of 250 m, stitched | `src/Presentation/Spike/SpikeScene.cs:25` |
| Bake settings | cell size 1 m, cell height 0.5 m, agent radius 1 m, height 2 m, max climb 1 m, max slope 40°, border 2 m | `src/Presentation/Spike/SpikeScene.cs:159-181` |
| Bake result | 64 tiles in 6.2 s, 14,291 polygons; "One 2 km bake overflowed Recast's region IDs" | `docs/M3_STATUS.md:72` |

[INFERENCE] **No normative document states** a building grid unit, wall thickness, door width, door height, storey height, snap increment or piece size.
- The only numbers are in VERTICAL_SLICE (45° rotation, 40 m plot) and in non-normative asset reports: the 3.0 m module, 2.6 m wall and 2.20 m door frame.
- The Phase-1 domain uses 0.4 m walls and 1.6 m door openings, which do not agree with the kit.
- M7 must choose these dimensions. The 1.6 m opening against a 0.7 m body diameter leaves 0.45 m clearance per side, and nothing documents that as a rule.

---

## 7. Persistence rules for structures (consolidated)

1. [FACT] **Never regenerable.** A structure is "pure player-authored state" (`docs/SYSTEMS.md:355`; `docs/PERSISTENCE.md:136,250`).
2. [FACT] **Section.** Structures live in `buildings.msgpack`. The owning cells are dirtied with reason `buildings` (`docs/WORLD_ARCHITECTURE.md:410`; `docs/PERSISTENCE.md:115,210`).
3. [FACT] **Identity.** Structures and pieces are ULID-keyed (`docs/WORLD_ARCHITECTURE.md:409`). "every piece is a registry-created (D-10) entity with a D-04 instance ID" (`docs/RISK_REGISTER.md:158`). The structure prefix is `bld` (`docs/DATA_MODEL.md:123`).
4. [FACT] **Pinned records.** The record stays in memory while the region is loaded; geometry streams out (`docs/WORLD_ARCHITECTURE.md:172,411`). Buildings are "C/D, `pinned` … never lossy-unloaded" (`:392`).
5. [FACT] **Cross-cell.** State is "stored once on the structure and referenced by each cell's delta" (`docs/WORLD_ARCHITECTURE.md:412`).
6. [FACT] **Storage.** Container contents persist via S-14 in `entities.msgpack`. A building's `storage` holds container ULIDs. If `entities` is quarantined, a dangling reference is "reported loss" (`docs/SYSTEMS.md:355`; `docs/PERSISTENCE.md:252,461`).
7. [FACT] **Corruption.** Load without the section, "report every lost structure explicitly; never fail the whole load" (`docs/PERSISTENCE.md:136,458`).
8. [FACT] **Damage and defense.** Per-piece damage persists (`docs/WORLD_ARCHITECTURE.md:415`). Attack frequency, alert level and `disabled_attacks` persist as a gameplay setting (`:417`).
9. [FACT] **Assignments.** Assigned-NPC ULIDs persist (`docs/WORLD_ARCHITECTURE.md:416`). S-24 persists "anchor reassignment after building changes" (`docs/SYSTEMS.md:277`).
10. [FACT] **Transient.** Ghost/preview, snap candidates, "navmesh dirty regions" and "construction progress" are transient (`docs/SYSTEMS.md:356`). Navmesh and pathfinding are never saved (`docs/PERSISTENCE.md:83-84`; `docs/SYSTEMS.md:443`).
11. [FACT] **Version sensitivity.** Building records are save-version-sensitive; any shape change bumps `schema_version` (`docs/DATA_MODEL.md:846`). The procedure is a fixture per version plus a migration step (`tests/Persistence.Tests/Fixtures/README.md:12-19`).
12. [FACT] **Baseline proof.** Every changed cell's delta names its `baseline_hash`. Entity records and created instances are proven against their host cell (`docs/PERSISTENCE.md:392`). [INFERENCE] A structure whose cells' baseline later changes (an authored edit to the region) needs a registered `BaselineTransition`, or the load refuses. M6 had to register exactly such a transition for a layout change (`docs/PERSISTENCE.md:398`). Nothing specifies what a transition does with a player building that now overlaps new authored geometry.
13. [FACT] **Budget.** 10 KB–5 MB (`docs/PERSISTENCE.md:553`). Growth must be sub-linear in piece count (`docs/RISK_REGISTER.md:154-156`).
14. [FACT] **Code today.** No `buildings.msgpack`: the checked files are `cells`, `entities`, `manifest`, `player` (`src/Persistence/SaveModel.cs:24-32`). The v13 fixture has no buildings file (`tests/Persistence.Tests/Fixtures/v13/quick`). Schema is 13 (`src/Persistence/SaveModel.cs:20`). [INFERENCE] M7 adds a section and a schema bump. The §7.4 load sequence also needs a buildings step (C15).

---

## 8. Navigability rules (consolidated) and the current code state

**What the docs require**
- [FACT] NPCs and companions must path through player structures. This is D-08's justification (`docs/DECISIONS.md:180`; `docs/WORLD_ARCHITECTURE.md:414`; `docs/GAMEPLAY_LOOPS.md:135`).
- [FACT] Placement validation must reject "un-navigable or overlapping configurations" (`docs/ROADMAP.md:283`). WORLD_ARCHITECTURE validates "terrain slope, socket graph connectivity, no blocking of authored story geometry, no overlap with a pinned spawn — never by physics" (`docs/WORLD_ARCHITECTURE.md:413`). VS says invalid overlaps are "rejected with a visible reason rather than placed-then-fixed" (`docs/VERTICAL_SLICE.md:161`).
- [FACT] Navmesh is rebuilt on change, debounced (`docs/WORLD_ARCHITECTURE.md:414,435`), batched per building edit (`docs/RISK_REGISTER.md:158`), and "the navmesh updates on placement" (`docs/ROADMAP.md:284`).
- [FACT] Across seams:
  - Rebuild over "a stitched multi-cell neighborhood … with a seam overlap margin".
  - A building change dirties neighbours' navmesh too, as "a new dirty-flag reason".
  - "constrain or warn on placement that would straddle a seam until stitching is proven" (`docs/RISK_REGISTER.md:294`).
  - ROADMAP requires the straddling case be "proven here" (`docs/ROADMAP.md:284`). WORLD_ARCHITECTURE RK-A2 says to "test with a building straddling four cells" (`docs/WORLD_ARCHITECTURE.md:469`).
- [FACT] Tests:
  - RK-06: ~200 pieces; companion and NPC path "from the doorway to an interior socket" after reload (`docs/RISK_REGISTER.md:154`).
  - RK-14: one straddling wall with a doorway; path before and after a forced cell unload/reload (`:290`).
  - VS E4: "both companions and a town NPC path through the player-built doorway" (`docs/VERTICAL_SLICE.md:358`).
- [FACT] Pathfinding runs only at Tier A (`docs/WORLD_ARCHITECTURE.md:234`). It is never simulated where the player is not (`:397`). Promotion must not place an actor "off navmesh, inside a player building without permission" (`:297`).

**What exists in code**
- [FACT] **Movement.** One deterministic movement function, `Kinematics.Step`, integer mm, shared with presentation prediction (`src/Domain/Spatial/Kinematics.cs:106-121`).
  - Collision is a body circle pushed out of footprints (`:113-116`).
  - Footprints are axis-aligned boxes or circles only: "Structures in the greybox are axis-aligned, which keeps the math exact" (`src/Domain/Spatial/Blockers.cs:40-41`).
  - "Movement is planar - no climbing, nothing stood on" (`src/Domain/Spatial/Blockers.cs:8`). Body Y = `Terrain.HeightAtMm(x,z)` + jump lift (`src/Domain/Spatial/Kinematics.cs:158`).
  - `WalkSpace` holds an `ImmutableArray<Blocker>` of static blockers (`:104`). Doors, barriers and bodies are passed as `dynamicBlockers` (`:119-121`; `src/Domain/Spatial/RegionLayout.cs:93-97`).
- [FACT] **Pathfinding.** None in the domain. Companions walk the character's trail, "straight for the farthest mark in clear view" (`src/World/Runtime/Companions.cs:321-322`). They are put down near the character when more than 30 m behind or stuck 4 s (`docs/SYSTEMS.md:290`). Creatures steer straight or walk authored routes (`src/World/Runtime/Creatures.cs:425-431,530`). NPCs stand still (`docs/SYSTEMS.md:280`).
- [FACT] **Navmesh.** The only one is the Godot `NavigationRegion3D` bake in the presentation-only `--spike` (`src/Presentation/Spike/SpikeScene.cs:13,157-181`). The spike is "Explicitly NOT the playable prototype" (`:1-2`).
- [FACT] **Soak evidence.** A companion soak found the companion "never stood inside a structure, door or barrier or out of bounds" (`G:/UNNAMED_HISTORY/OVERNIGHT_REPORT_2026-09-24.md:76-79`; report, not source).
- [INFERENCE] ROADMAP M7's entry, "NPCs and companions path reliably" (`docs/ROADMAP.md:281`), is **not met by Phase 1 as built**: there is no pathfinding at all. M7 must introduce domain pathfinding, not only reuse it.
- [INFERENCE] A domain navigation representation derived from `WalkSpace` blockers plus structure pieces plus door state would fit ruling 1 and the existing Kinematics collision model. Examples: a deterministic integer grid or polygon graph per cell with seam overlap. Rotated pieces (45° per VS) need either oriented boxes, which the current Blocker set lacks, or a restriction to 90°.

---

## 9. Doors

**What the docs say**
- [FACT] "doors" appear in the charter's build list (`docs/PROJECT_CHARTER.md:508`), the ROADMAP M7 work list (`docs/ROADMAP.md:283`), and the VS "opening" family (`docs/VERTICAL_SLICE.md:98,161`). VS E4 requires paths through "the player-built doorway" (`:358`).
- [FACT] Door world flags live in `cells.msgpack` (`docs/WORLD_ARCHITECTURE.md:142`). "a door opened and closed" is a rebase example (`docs/PERSISTENCE.md:282`).
- [FACT] S-34 owns interior "doors/keys opened" and emits `DoorUnlocked` (`docs/SYSTEMS.md:377,381`).
- [FACT] Stealth wants doors to affect noise propagation (`docs/STEALTH_DETECTION_AND_THREAT.md:121`). Crime evidence includes "damaged locks" (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:73`).
- [FACT] No document specifies locks, keys or permissions for player doors, or which actors may open them. The closest is WORLD_ARCHITECTURE's "inside a player building without permission" (`docs/WORLD_ARCHITECTURE.md:297`).

**Phase-1 precedent in code**
- [FACT] `DoorSite(Key, FlagId, BoxBlocker ClosedFootprint)`: "Closed, its footprint blocks movement; open, it does not" (`src/Domain/Spatial/RegionLayout.cs:8-12`).
- [FACT] The door key must start `door.`. The flag must be a declared `world_flag`. No two doors share a key or a flag (lint WLD004, `src/Content/WorldContent.cs:159-164`).
- [FACT] Doors are toggled by `InteractCommand` within reach of the footprint (`docs/SYSTEMS.md:260`). They block movement, blows and sight while closed (`docs/SYSTEMS.md:260`).
- [FACT] Presentation draws a hinged door swinging inward (`src/Presentation/Greybox/HollowView.cs:219-226`).
- [FACT] A companion "passes through" the character; creatures do not (`docs/SYSTEMS.md:290`).
- [FACT] Kinematics notes that "a body of unknown height (a creature, a person, a door) is never jumped" (`src/Domain/Spatial/Kinematics.cs:127`).

[INFERENCE] **Mismatch with player-built doors.**
- Player-built doors cannot use the Phase-1 mechanism as-is: a door's state is a *declared content* `world.*` flag, and content cannot know runtime instances.
- Door open state for a placed door must be piece instance state.
- Navigation must treat a closed door as traversable by permitted actors (open-then-pass) or as blocked. The docs do not decide which.

---

## 10. Ownership and property

- [FACT] **S-32 owns ownership** (`docs/SYSTEMS.md:352-353`). The `owner` field is a ULID. Ownership transfer is "Out of scope in Phase 0" (`docs/WORLD_ARCHITECTURE.md:418`).
- [FACT] **VS ownership model.** `owner_instance_id` plus "plot ownership". `build_piece` requires placement "on an owned plot" (`docs/VERTICAL_SLICE.md:119,161,187`). The plot is granted by quest stage 5 (`:50,215`). DATA_MODEL has a `property` reward kind (`docs/DATA_MODEL.md:468`).
- [FACT] **Property concepts.** Seven states, "not present schema commitments" (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:54-64`). Jurisdiction decides permission, taxes, permits, trespass rules and more (`:79-89`). Settlement acquisition modes are rent, lease, purchase, grant and inherit (`:116-122`).
- [FACT] **Container permission** is S-14's (`TransferRejected(…permission…)`) (`docs/SYSTEMS.md:184`). S-14 reads S-32 for "owned property/container ownership" (`:181`).
- [FACT] **Access gates.** Reputation gates "settlement-building rights" (`docs/PROGRESSION.md:440`). Faction definitions carry `territory` and `laws` (`docs/DATA_MODEL.md:561-562`). CRIME_LAW jurisdiction regulates "ownership and theft" and "building/trespass" (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:24,33`).
- [INFERENCE] **Owner ruling 3 applies here.** Legal permission to build (jurisdiction/law), faction access (reputation tier) and personal relationships must stay separate inputs to `IsValidPlacement`. Trespass and theft of stored goods touch crime, which is Phase 3 per `docs/INDEX.md:89`.

---

## 11. Per-piece health, damage, repair and defense

- [FACT] "'Damage' is a per-piece health value applied by explicit rules, not emergent physics" (`docs/DECISIONS.md:182`).
- [FACT] S-32 owns per-piece health, damage state and repair state, with `DamagePiece`/`RepairPiece` and the events `PieceDamaged`/`PieceDestroyed` (`docs/SYSTEMS.md:353,357`).
- [FACT] Repair is "by explicit command" (`docs/WORLD_ARCHITECTURE.md:415`). VS: "repaired with materials at the piece" (`docs/VERTICAL_SLICE.md:161`).
- [FACT] VS damage comes from "explicit scripted events (the one home-defense probe, §8 P1)" (`docs/VERTICAL_SLICE.md:161`). [FACT] §8's P1 row does not actually describe a home-defense probe (`docs/VERTICAL_SLICE.md:260`). The probe is referenced but undefined.
- [FACT] **Attack frequency.** "Attack frequency is a player-facing option and defaults low" (`docs/SYSTEMS.md:357`). It is persisted (`docs/WORLD_ARCHITECTURE.md:417`). The option "must exist in the same phase BUILD ships" (`docs/GAMEPLAY_LOOPS.md:134`).
- [FACT] **Off-screen attacks.** They are tier-illegal as specified, and review finding C-8 is unresolved (`docs/reviews/ARCHITECTURE_AI_SESSION_REVIEW.md:217-239`).
- [FACT] **Weather.** Weather may "damage poorly protected buildings" but "should not constantly punish the player's property" (`docs/WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT.md:564-568`, Phase 2+).
- [INFERENCE] **Health naming.** Per the DATA_MODEL naming corollary (`docs/DATA_MODEL.md:158`), use `health_max` on the definition and `health_current` on the instance.
- [INFERENCE] **Damage path.** Piece damage should arrive as commands from a combat-side or event source to S-32 ("A system changes another system's state only by submitting a command", AGENTS.md). S-32 reads S-12 raid damage events (`docs/SYSTEMS.md:354`).

---

## 12. Storage and crafting stations as placeables

- [FACT] **ROADMAP M7.** "storage containers; one crafting station as a placeable" (`docs/ROADMAP.md:283`).
- [FACT] **VS.**
  - Functional pieces: workbench (crafting), storage chest (container), bed (rest / time-of-day advance) (`docs/VERTICAL_SLICE.md:161`).
  - Station budget: "player-built workbench (buildable), and one placeable smelter" (`:99`).
- [FACT] **Piece definitions** carry "station capability"; instances carry "attached storage" (`docs/DATA_MODEL.md:148`).
- [FACT] **Phase-1 stations** are authored points: `station.forge_hearth` kind `forge` at (60.5, 143.5) and `station.forge_anvil` kind `anvil` (`content/regions/ashen_hollow.yaml:161-163`). A craft needs "a station of its kind within reach" (`docs/SYSTEMS.md:213`). Stations have no collision footprint of their own in the domain.
- [FACT] **Phase-1 containers** are authored with `key`, `loot_ref`, `position_m`, `stack_slots` (`content/regions/ashen_hollow.yaml:146-149`). A changed container persists whole in `entities.msgpack` (`docs/PERSISTENCE.md:242`).
- [FACT] **Placed chests.** "a placed chest" is already named as a *created instance* (`docs/PERSISTENCE.md:240,292`).
- [INFERENCE] **Placed containers.** A placed container can reuse the created-instance and changed-container records, which exist since schemas 3 and 6. S-32 would hold only a reference (`docs/PERSISTENCE.md:252`).
- [INFERENCE] **Placed stations.** A placed station must satisfy the M3f station-kind lookup. `CraftingSystem` must see runtime stations as well as authored `StationSite`s.

---

## 13. What the docs defer (for Building v1 scope)

- **Structural simulation, voxel building, free-form placement: never** (`docs/DECISIONS.md:176-178`; `docs/ARCHITECTURE.md:345`).
- **Deferred beyond Phase 2** (`docs/GAMEPLAY_LOOPS.md:282`): settlement growth, NPC assignment at scale, defenses, and estate/fortified tiers.
- **VS non-goals** (`docs/VERTICAL_SLICE.md:97-98,332-333`):
  - hirelings (0), hireling housing, stewards;
  - towers, gates, farmland;
  - home defense as a system (frequent attacks, traps, guards, trained animals).
- **Other deferrals.**
  - Ownership transfer (`docs/WORLD_ARCHITECTURE.md:418`).
  - Settlement *simulation* (economy, growth, production) is M10 (`docs/ROADMAP.md:254`).
  - Farming beyond garden plots and settlement population growth (`docs/SYSTEMS.md:427`).
  - Great Works are design-only (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:188`).
  - Crime systems are Phase 3 (`docs/INDEX.md:89`).
  - NPC construction via archetypes is later (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:162-174`).
  - The Other-related building is "Later possibilities" (`:224`).
  - Weather damage and shelter properties are Phase 2+ (`docs/WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT.md:4`).
  - PvP building damage is "Do not solve these now" (`docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:756`).
- **Undecided by the docs.**
  - Cross-character or shared storage: "needs later design" (`docs/INVENTORY_STORAGE_AND_LOGISTICS.md:96`).
  - The "while you were away" policy for settlement production (`docs/DECISIONS.md:265`).

---

## 14. Code facts that constrain M7

- `EntityKind.Building` → `bld`. `FarmPlot` → `plt`. There is no piece kind (`src/Domain/EntityKind.cs:17,23,40,46`).
- The objective `construct_building` is listed as not built, "building (M7)". `faction_reputation`/`faction_state` → "factions (M7)" (`src/Domain/Quests/Quests.cs:146,154-155`).
- `RegionLayout` is built from content, and "presentation builds its greybox from the same data, so what is drawn and what blocks are one thing" (`src/Domain/Spatial/RegionLayout.cs:54-59`).
- Presentation derives roofs by grouping structure IDs by prefix (`longhouse_`, `forge_`) (`src/Presentation/Greybox/HollowView.cs:202-217`). [INFERENCE] This is a presentation heuristic that player structures should not rely on.
- Phase-1 kit art for buildings is **withheld**. The longhouse and forge shed are drawn as greybox (`docs/PHASE1_ASSET_INTEGRATION.md:35`). "the world's greybox structures were sized before the art existed … the owner's call whether the world or the art moves" (`:93`).
- The untracked V4 handoff says Ashen Hollow buildings "use deterministic modular assemblies, **not** single-image 3D reconstruction" (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:801`, authority unconfirmed). The same file, lines 1733-1739, says "Buildings should be modular assemblies".

---

## 15. Contradictions

- **C1. Piece identity.**
  - WORLD_ARCHITECTURE and PERSISTENCE say pieces are "ULID-keyed rows" (`docs/WORLD_ARCHITECTURE.md:409`; `docs/PERSISTENCE.md:250`). RK-06 says each piece is a registry entity with an instance ID (`docs/RISK_REGISTER.md:158`).
  - The VS persistence row `{piece_def_id, plot_id, cell_id, transform, socket_parent, health, owner_instance_id}` has **no piece instance ID and no structure ID** (`docs/VERTICAL_SLICE.md:161`).
  - DATA_MODEL defines only the `bld` building prefix, with no piece prefix (`docs/DATA_MODEL.md:123`).
- **C2. Objective vocabulary.**
  - DATA_MODEL's closed set has `construct_building` (`piece_ref` or `tag`, `count`, `at?`) (`docs/DATA_MODEL.md:453`), and the code uses that name (`src/Domain/Quests/Quests.cs:146`).
  - VS names it `build_piece` "on an owned plot" (`docs/VERTICAL_SLICE.md:187`).
  - VS also names `reach_reputation`, `relationship_threshold`, `learn_recipe`, `choice` and `timed`. None of these is in DATA_MODEL's closed set, and VS calls `world_state` "Arbitrary predicate … (the escape hatch)" (`docs/VERTICAL_SLICE.md:188-193` vs `docs/DATA_MODEL.md:445-466`). The "owned plot" condition has no DATA_MODEL equivalent.
- **C3. No content kind or reference row for pieces.**
  - DATA_MODEL §3.1 describes building-piece definitions (`docs/DATA_MODEL.md:148`), and §4.11 uses a `piece_ref` field (`:453`).
  - The closed kind table has no piece, building or station kind (`:44-68`). The cross-reference table has no `piece_ref` row (`:807-819`).
  - "A `*_ref` field matching no pattern is an error" (XREF004, `:821`). As written, the documented objective would fail validation.
- **C4. Seam-straddling.**
  - ROADMAP M7 requires proving "a structure straddling a cell boundary" (`docs/ROADMAP.md:284`).
  - VS deliberately places the only plot "entirely inside one cell … so no player structure straddles a cell seam" (`docs/VERTICAL_SLICE.md:161`).
  - RK-14's mitigation says "constrain or warn on placement that would straddle a seam until stitching is proven" (`docs/RISK_REGISTER.md:294`).
  - WORLD_ARCHITECTURE says to test a building straddling *four* cells (`docs/WORLD_ARCHITECTURE.md:469`). Owner ruling 1 requires seam support.
- **C5. Where the navmesh lives (and owner ruling 1).**
  - WORLD_ARCHITECTURE §11 lists "Navmesh | Recast-style, baked per cell …" in the presentation/streaming table (`docs/WORLD_ARCHITECTURE.md:435`).
  - PERSISTENCE says the "Baking / streaming pipeline" rebuilds it (`docs/PERSISTENCE.md:83`). RK-14's validation "needs the engine" (`docs/RISK_REGISTER.md:290`). ROADMAP M7's proof is a "navmesh path test" (`docs/ROADMAP.md:285`). The only implementation is Godot `NavigationServer3D` in the spike (`src/Presentation/Spike/SpikeScene.cs:157-181`).
  - On the other side, S-32 owns "navmesh dirty regions" as domain transient state (`docs/SYSTEMS.md:356`). VS E13 requires "no gameplay rule exists in `src/Presentation/**`" (`docs/VERTICAL_SLICE.md:372`). Ruling 1 requires domain-side navigation.
  - The documents never state which layer owns path queries.
- **C6. Navmesh tile size.** WORLD_ARCHITECTURE says "baked per cell" (100 m) (`docs/WORLD_ARCHITECTURE.md:414,435`). The spike used 250 m tiles (`src/Presentation/Spike/SpikeScene.cs:25`). RK-14 wants multi-cell neighbourhoods (`docs/RISK_REGISTER.md:294`).
- **C7. Rotation.**
  - D-08 and ROADMAP say "free rotation" (`docs/DECISIONS.md:182`; `docs/ROADMAP.md:283`). VS says "free rotation in 45° steps" (`docs/VERTICAL_SLICE.md:161`).
  - Domain collision supports only axis-aligned boxes and circles (`src/Domain/Spatial/Blockers.cs:40-41,94-95`).
- **C8. Which stations are buildable (VS contradicts itself and ROADMAP).**
  - VS §3: workbench buildable, smelter "placeable", forge a town station (`docs/VERTICAL_SLICE.md:99`). VS §5.4: functional pieces are "only" workbench, chest and bed, with no smelter (`:161`).
  - Stage 5 is "`build_piece` (forge or workbench)", with the gate "at least a workbench + the smelter piece" (`:215`). Stage 8 happens "at the player's forge" (`:218`). P7 requires "a player-built forge" (`:266`).
  - ROADMAP M7: "one crafting station as a placeable" (`docs/ROADMAP.md:283`).
- **C9. NPC assignment.**
  - ROADMAP M7 exit: "assign an NPC to work in it" (`docs/ROADMAP.md:284`).
  - VS: "Hirelings | 0", with steward/merchant/farmer hirelings and hireling housing as non-goals (`docs/VERTICAL_SLICE.md:97,332`). GAMEPLAY_LOOPS defers "NPC assignment at scale" (`docs/GAMEPLAY_LOOPS.md:282`).
  - As built: no anchors, schedules or relocation. Anchors are "Not built" (`docs/SYSTEMS.md:280,290`). NPC schedules are in the slice (`docs/ROADMAP.md:249`).
- **C10. ROADMAP M7 entry is unmet.** "NPCs and companions path reliably" (`docs/ROADMAP.md:281`). Phase 1 has no pathfinding: trail-following companions and standing NPCs (§8). The roadmap assumes M4 made NPCs path (`docs/ROADMAP.md:93,454`), but M4 as built did not (`docs/SYSTEMS.md:280`).
- **C11. Property threats in the building phase.**
  - GAMEPLAY_LOOPS Phase 2 BUILD includes "one upgrade tier; property threats with the charter-mandated frequency reduction/skip option". The option "must exist in the same phase BUILD ships" (`docs/GAMEPLAY_LOOPS.md:134,282`).
  - ROADMAP M7's work list omits both threats and upgrade tiers (`docs/ROADMAP.md:283`). VS has only "one scripted probe" and defers home defense (`docs/VERTICAL_SLICE.md:161,333`). The probe VS cites in "§8 P1" is not described in §8 (`:260`).
  - Off-screen attacks are tier-illegal (review C-8, unresolved).
- **C12. Plot restriction.**
  - WORLD_BUILDING: "Do not restrict construction to a small set of pre-designated 'player home plots' unless a settlement's law requires it" (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:48`).
  - VS: one plot, "the slice's only building site" (`docs/VERTICAL_SLICE.md:50`). WORLD_ARCHITECTURE: "building plots | Hand-authored" (`docs/WORLD_ARCHITECTURE.md:112`).
  - [INFERENCE] VS is a deliberate slice restriction. M7 must choose between them.
- **C13. "Plot" naming collision.** `plt` is "farm plot" (`docs/DATA_MODEL.md:123`; `src/Domain/EntityKind.cs:23,46`), and S-32 owns "garden/farm plots" (`docs/SYSTEMS.md:353`). VS uses `plot_id` for a *building* plot (`docs/VERTICAL_SLICE.md:161`).
- **C14. Cells per structure.** The VS row has a single `cell_id` (`docs/VERTICAL_SLICE.md:161`). WORLD_ARCHITECTURE and PERSISTENCE say a structure has "a footprint of one or more cells" (`docs/WORLD_ARCHITECTURE.md:409`; `docs/PERSISTENCE.md:250`).
- **C15. No buildings step in the load sequence.** PERSISTENCE lists `buildings.msgpack` in the layout and quarantine rules (`docs/PERSISTENCE.md:115,136,457-458`), but the "only normative load order" (a–n) has no buildings step (`docs/PERSISTENCE.md:482-497`). Code writes no such section (`src/Persistence/SaveModel.cs:24-32`).
- **C16. No field schema for buildings.** PERSISTENCE §5.4 is prose only, unlike §5.2 and §5.3, which have field tables (`docs/PERSISTENCE.md:248-252` vs `:206-212,225-232`).
- **C17. Player structures inside interiors.**
  - S-34 reads "S-32 (player structures inside interiors)" (`docs/SYSTEMS.md:378`). QUESTS allows a cleared crypt to become a "player home" (`docs/QUESTS_DUNGEONS_WORLD_EVENTS_AND_REPOPULATION.md:113-117`).
  - WORLD_ARCHITECTURE §10 models buildings as exterior-cell exceptions (`docs/WORLD_ARCHITECTURE.md:409-412`). Interiors are separate spaces with their own cell grid (`:362`).
  - [INFERENCE] Building v1 should exclude interiors explicitly.
- **C18. Floors versus planar movement (doc vs code).**
  - Charter, ROADMAP and VS require foundations and floors (`docs/PROJECT_CHARTER.md:504-506`; `docs/ROADMAP.md:283`; `docs/VERTICAL_SLICE.md:98`).
  - Movement cannot stand on anything; Y is terrain height (`src/Domain/Spatial/Blockers.cs:8`; `src/Domain/Spatial/Kinematics.cs:158`).
  - [INFERENCE] Either floors are cosmetic and flush with terrain, with foundations accepted only on near-flat ground, or the one movement function gains a walk surface. Even one storey forces this choice on slopes.
- **C19. Door state storage (doc/code vs M7 need).**
  - Doors are "World flags (doors, switches …)" in `cells.msgpack` (`docs/WORLD_ARCHITECTURE.md:142`). Phase-1 door flags must be declared content `world_flag`s (`src/Content/WorldContent.cs:160`).
  - A player-built door has no declared flag, so its state must be instance state. No doc says so.
- **C20. Art versus domain footprints (Phase-1 buildings).**

  | Building | Domain collision | Kit asset |
  |---|---|---|
  | Longhouse | 16 × 8 m, 3.2 m walls, door on the **east** wall, 1.6 m opening, 2.4 m door (`content/regions/ashen_hollow.yaml:66-70,143`) | 9 × 6 m, 2.6 m walls, "with a south door" (`docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:150`) |
  | Forge shed | 10 × 8 m, 3.0 m walls (`content/regions/ashen_hollow.yaml:72-76`) | 6 × 6 m (`docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:149`) |

  - This is recorded as open, "the owner's call whether the world or the art moves" (`docs/PHASE1_ASSET_INTEGRATION.md:93`).
  - [INFERENCE] M7's piece grid must pick one of these scales.
- **C21. Door-frame height.** "a door frame exactly 2.20 m" (`docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:154`). The scale audit measures `building_door_frame` at 2.44 m on its longest axis (`docs/SCALE_AUDIT_REPORT.md:178`). [INFERENCE] Probably clear opening versus outer frame, but unstated. The domain door is 2.4 m tall.
- **C22. Door height versus body families.**
  - A 2.20 m frame fits "a 1.80 m Veth" (`docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md:154`).
  - The fit families include 2.35 m Vaskaal and 2.60 m / 0.82 m-shouldered Ondrek (`docs/CANONICAL_BODY_AND_SKELETON.md:29-30`). The domain uses one 1.8 m stand height and a 0.35 m radius for every body (`content/config/base_speeds.yaml:10,18`).
  - No doc sets a door clearance rule that covers the largest body.
- **C23. "Snapping guarantees navigability" versus RK-14.**
  - D-08, S-32, WORLD_ARCHITECTURE §10 and GAMEPLAY_LOOPS §7 say snapping *guarantees* navigability (`docs/DECISIONS.md:180`; `docs/SYSTEMS.md:357`; `docs/WORLD_ARCHITECTURE.md:414`; `docs/GAMEPLAY_LOOPS.md:135`).
  - RK-14 says D-08 "says nothing about the navmesh across a cell boundary" (`docs/RISK_REGISTER.md:282`). ROADMAP adds placement validation that *rejects* un-navigable configurations (`docs/ROADMAP.md:283`), which implies snapping alone does not guarantee navigability.
- **C24. Radial menus (owner ruling 5).**
  - HUD_INPUT lists radials as an access method, and "Controller users should retain the same underlying capability through context/radials" (`docs/HUD_INPUT_AND_ACTIONS.md:30,36`).
  - The content bible: no core action "or building function" may require a radial (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:645`).
- **C25. Entry gate before Phase 2.**
  - IMPLEMENTATION_PRECEDENCE requires "3–5 blind testers … before advancing to Phase 2" (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:156,321`).
  - ROADMAP's later owner ruling (2026-09-24): the feel test is "not an M7 entry blocker" (`docs/ROADMAP.md:274`).
- **C26. Two home ladders.**
  - Charter and GAMEPLAY_LOOPS: "campfire → camp → shelter → cottage → home → workshop → estate → fortified homestead → settlement" (`docs/PROJECT_CHARTER.md:500`; `docs/GAMEPLAY_LOOPS.md:207`).
  - WORLD_BUILDING: "camp → shelter → cabin → homestead → workshop → hamlet → village" (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:146`).
- **C27. Construction progress: transient or tier-advanced?**
  - S-32 lists "construction progress" as **transient** (`docs/SYSTEMS.md:356`).
  - WORLD_ARCHITECTURE lets Tier C run "construction progress" and B/C advance "Construction/build progress queued before the player left" (`docs/WORLD_ARCHITECTURE.md:236,284`).
  - GAMEPLAY_LOOPS' session loop "start a build" implies progress across time (`docs/GAMEPLAY_LOOPS.md:195`).
  - Transient state cannot survive tier changes or saves.
- **C28. Recipe station reference.** DATA_MODEL's §4.9 example uses `station_ref: station.forge` (`docs/DATA_MODEL.md:396`), but there is no `station` content kind. M3f as built uses a station *kind* string (`docs/DATA_MODEL.md:414`). A placeable station piece needs "station capability" (`:148`), and its link to recipe station kinds is unspecified.
- **C29. Save budgets.** VS allows a ≤ 2 MB *total* save after 6 h (`docs/VERTICAL_SLICE.md:371`). PERSISTENCE budgets `buildings.msgpack` alone at up to 5 MB and `entities` at 2–40 MB (`docs/PERSISTENCE.md:551-553`).
- **C30. Unrun Phase-1 validations.**
  - RK-06's piece-count ceiling is "defined by the Phase-1 measurement" (`docs/RISK_REGISTER.md:158`). RK-11's Phase-1 round trip includes "place a building piece" (`:234`).
  - Phase 1 excluded building (`docs/PROTOTYPE.md:32,265`). The measurement does not exist, and the RK-06/RK-14 validations are still owed.
- **C31. Stale untracked handoff.** The V4 handoff treats the C10 direction as "not yet formally owner-ratified" (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:611-618`, untracked). PROTOTYPE records the owner ruling (`docs/PROTOTYPE.md:298`).

---

## 16. Open questions for the M7 designer

Each item is a question the docs leave unanswered; where a proposal is mine it is marked [INFERENCE].

1. **Navigation representation (ruling 1).**
   - What domain structure replaces the documented navmesh: a grid, a polygon graph, or visibility over blockers?
   - How is it keyed per cell, stitched at seams and invalidated? RK-14 wants a new dirty reason for neighbours.
   - [INFERENCE] Should RK-14's validation be rewritten as a headless domain test instead of "needs the engine"?
2. **Structure model.**
   - Structure plus piece records (WORLD_ARCHITECTURE/PERSISTENCE), or flat piece rows with a plot (VS)?
   - Does each piece get its own ULID and prefix? `bld` is defined only for buildings.
3. **Content kind.**
   - Which `kind`, directory and ID prefix hold piece definitions (e.g. `piece.*`)?
   - This needs rows in the DATA_MODEL §1 kind table and the §5 `piece_ref` table, and SEM checks.
4. **Grid and socket geometry.**
   - Grid unit: the 3.0 m kit module, or the domain's current 0.4 m-walled 16 × 8 m greybox?
   - Also needed: wall thickness, opening width (currently 1.6 m) and height (2.4 m in the domain vs 2.20 m in the kit), storey or wall height (2.6, 3.0 or 3.2 m), and rotation step (45° per VS vs 90° for axis-aligned collision).
   - Door clearance for the 2.35 m and 2.60 m body families.
5. **Floors on terrain.** How does a floor or foundation interact with sloped terrain when bodies cannot stand on structures? Options: a max slope, snapping foundations to terrain, or a new walk-surface in `Kinematics` (a change to the one movement function shared with presentation prediction).
6. **Door model.** What holds instance state, who may open (owner, companions, NPCs, hostiles), and how does navigation treat closed doors? Also locks and keys, and Mor with no hands.
7. **Seams.** Allow straddling from day one (ROADMAP, ruling 1), or constrain it as RK-14 and VS suggest?
8. **Placement legality inputs.** The four listed are terrain slope, socket connectivity, authored story geometry and pinned spawns (`docs/WORLD_ARCHITECTURE.md:413`). What else? Candidates: jurisdiction/permission (ruling 3 separation), overlap with other actors and with the player, and blocking of doors, NPCs and containers.
9. **Station and storage placeables.** How do placed stations register with `CraftingSystem`'s station-kind lookup? Do placed containers reuse the created-instance and changed-container records (schemas 3 and 6)?
10. **Damage source.** What produces piece damage in v1, given that off-screen raids are tier-illegal (review C-8) and VS's "home-defense probe" is undefined? Is the attack-frequency option required in M7 (GAMEPLAY_LOOPS §7) if no attacks ship?
11. **NPC assignment.** What does "assign an NPC to work in it" (ROADMAP exit) mean without anchors or schedules? Is it a minimal anchor on a piece, or a reconciliation to drop this criterion?
12. **Baseline transitions.** What happens to a player structure when a later authored region edit changes its cells' `baseline_hash` or overlaps new authored geometry?
13. **Save shape.** A `buildings.msgpack` field table, a schema-14 bump plus fixture, and where buildings enter the §7.4 load order: before or after the entity merge, since storage references container ULIDs in `entities`.
14. **Construction progress.** Instant placement (as M3f made crafting instant) or timed construction? The latter conflicts with S-32's "transient" (C27).

---

## Sources read (all at `e10d2c4` unless marked)

**Core and design docs**
- `docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md` (all); `docs/SYSTEMS.md` (§1, S-01..S-38, §3, §4); `docs/DECISIONS.md` (D-04..D-12, open questions); `docs/DATA_MODEL.md` (§1, §2.2, §3, §4.5, §4.9, §4.11, §4.13, §5, §6, assumptions); `docs/WORLD_ARCHITECTURE.md` (all).
- `docs/GAMEPLAY_LOOPS.md` (§1, §7, §11-§13, §15); `docs/RISK_REGISTER.md` (matrix, RK-02/05/06/11/14, promoted table, accepted risks); `docs/PERSISTENCE.md` (§1-§3, §5, §6.4, §7.2, §7.4, §8, §9, §10, §11); `docs/VERTICAL_SLICE.md` (§2-§12); `docs/ROADMAP.md` (graph, critical path, M3, M4, M7, notes).
- `docs/WORLD_MATERIALS.md`, `docs/CRAFTING_AND_ITEMIZATION.md`, `docs/INVENTORY_STORAGE_AND_LOGISTICS.md`, `docs/TRAVEL_AND_TRAVERSAL.md` (all).

**Asset and scale docs**
- `docs/SCALE_AUDIT_REPORT.md` (method and building/prop rows); `docs/WAVE_0_MODULAR_ASSET_STANDARD.md` (§0-§4, §7-§10; read only); `docs/PHASE1_BIBLE_ASSET_SPRINT_STATUS.md` (§5, §14); `docs/ASSET_MATERIAL_PASS_2026-09-24.md`; `docs/PHASE1_ASSET_INTEGRATION.md`; `docs/CANONICAL_BODY_AND_SKELETON.md` (fit families).

**Authority, other design docs and reviews**
- `docs/PROJECT_CHARTER.md` §13-§14; `docs/PHASE_0.md` STEP 12; `docs/PROTOTYPE.md`; `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`; `docs/INDEX.md`; `docs/ARCHITECTURE.md`; `docs/FEATURE_DELIVERY_STAGING.md`; `docs/ENGINE_VALIDATION.md`.
- Relevant excerpts of CRIME_LAW, WEATHER, QUESTS_DUNGEONS, SOCIAL, MODDING, NPC_SIMULATION, ECONOMY, COMPANIONS, STEALTH, CAMERA, HUD_INPUT, PROGRESSION, SKILLS, RACES, and the Phase-1 content bible.
- `docs/reviews/ARCHITECTURE_AI_SESSION_REVIEW.md` C-8.

**Code**
- `src/Domain/Spatial/{Kinematics,Blockers,RegionLayout,TerrainGrid,Tiers}.cs`; `src/Domain/EntityKind.cs`; `src/Domain/Quests/Quests.cs`; `src/Persistence/SaveModel.cs`; `src/Presentation/Spike/SpikeScene.cs`; `src/Presentation/Greybox/HollowView.cs`; `src/World/Runtime/{Companions,Creatures}.cs`; `src/Content/WorldContent.cs`.
- `content/regions/ashen_hollow.yaml`; `content/config/{base_speeds,simulation_tiers}.yaml`; `tests/Persistence.Tests/Fixtures/README.md`.

**Untracked (authority unconfirmed)**
- `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md`, `PHASE1_HUD_UI_CONTROLS_AND_FEEDBACK_SPEC.md`, `M3_TO_M6_SYSTEM_ASSET_INTEGRATION_MATRIX.md`, `DIFFICULTY_ACCESSIBILITY_AND_PLAYER_CUSTOMIZATION_DRAFT.md`, `FOR DEEPSEEK/DEEPSEEK_POST_M6_ASSET_MAINTENANCE_PROMPT.md`.

**Reports**
- `G:/UNNAMED_HISTORY/OVERNIGHT_REPORT_2026-09-24.md`.
