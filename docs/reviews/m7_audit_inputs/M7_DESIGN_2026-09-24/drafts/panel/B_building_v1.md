# M7 panel draft B - Building v1

Status: design and implementation plan, first full draft, 2026-09-24. It covers building only. This document writes no code. Navigation and factions are designed in parallel; this draft specifies only what building offers navigation (section 19) and what it needs from the NPC and navigation designs (section 16).

Base: origin/main `e10d2c4`, read from the snapshot named in `00_SCOPE_RULINGS.md` §0. Every `path:line` citation is repo-relative at that commit. The research notes this draft relies on are `research/{building_docs,spatial_movement,sim_core,persistence,content_registry,presentation_perf,social_quests_code}.md`.

---

## 0. Verdict in one page

Building v1 places content-defined **pieces** on a world-anchored **3 m lattice** (squares, edges, and a door slot in each doorway), in **quarter-turn rotations**. The lattice keeps every blocking piece an axis-aligned integer-mm box, so `Kinematics.Step`, combat traces and presentation prediction keep working unchanged. Each piece is one persisted row: a derived `pce_` ID, a definition ID, an absolute anchor in mm, a rotation, an owner, and `health_current`, plus a door flag and a worker slot where they apply. Nothing about a piece's shape is saved. Shape comes from the definition.

What M7 builds:
- **Seven greybox pieces**: ground pad, wall, doorway, door, roof, storage chest, anvil bench (the M3f `anvil` station kind).
- **Five player commands**: place, dismantle, repair, assign worker, release worker. Two existing commands are reused: `InteractCommand` works doors, and `MoveItemCommand`/`TakeAllCommand` work the chest.
- **One owning system**, `BuildingSystem`. It owns a new `StateSlice.Structures`.
- **One region-inline build area**, `build_area.hollow_crossing`: x 87-114 m, z 87-114 m. It contains the four-cell corner at (100, 100).

Doors are authoritative blockers. Each keeps its open/closed state on its piece row. They reuse the `DoorSite` idea, "closed, its footprint blocks movement; open, it does not" (`src/Domain/Spatial/RegionLayout.cs:8-12`).

Pads are the "foundations" and "floors". They add no walkable surface: the body stays on the terrain (`src/Domain/Spatial/Kinematics.cs:159`). A pad is drawn as a slab draped over the ground, and it is only accepted where the ground under it rises no more than 250 mm.

Damage has exactly three explicit sources, all the player's own strikes: melee, shot and formula. Repair and dismantle cost materials; dismantle refunds half, rounded down. Destruction at 0 health uses the dismantle removal path, with no refund. There is no collapse: support is checked when a piece is placed, and never again.

Placement is refused when it would disconnect any reference point from the region's anchor, for a 350 mm body, with every door treated as openable (section 18).

Materials:
- One new material, `item.material.timber`, from one new M3f node, `node.wood.deadfall`, at (84, 118).
- Adding the node changes cell A's `baseline_hash`, so M7 registers a baseline transition from the M6 fingerprint.
- The anvil also costs two iron billets.

Persistence:
- Pieces become a new `pieces` list plus a `structure_seq` scalar in `entities.msgpack` at schema 14. There is no `buildings.msgpack`.
- Piece IDs are derived from the owner and `structure_seq`. Replay from a save, and save-then-continue, therefore produce identical IDs and digests.

Proof scenario, "the Crossing Workshop" (section 22):
1. The player builds a 2 x 2-square workshop that straddles all four cells, with its doorway on the z = 100 seam.
2. Kera Voss is assigned to the anvil bench. She walks from the smithy around the building and in through the door.
3. A placement that would seal her work place off is refused.
4. The player extends a wall line that cuts her old way home, and she takes a deterministic detour.
5. The world is saved and reloaded mid-detour. It goes on identically.

---

## 1. What ROADMAP M7 actually requires of building

The building work item is at `docs/ROADMAP.md:283`, the exit criteria at `:284` and the proof at `:285`. The table maps each phrase to its M7 answer. The scope-ruling item that governs each answer is in brackets.

| ROADMAP phrase | M7 answer in this draft | Section |
|---|---|---|
| "Socket/snap placement" | Content-declared sockets on a world 3 m lattice. Mating needs exact position, type and axis equality, with 0 mm tolerance. | 8 |
| "foundations, walls, floors, roofs, doors" | Pad (foundation and floor are the same ground-level piece), wall, doorway, door, roof. [ruling 2; conflict row 2] | 5, 10 |
| "free rotation and socketing" | Quarter turns (0/90/180/270°). The definition shape keeps a `rotations` list and a config `rotation_step_deg`, so 45° can arrive later with oriented footprints. [conflict row 3, owner question] | 3, 4 |
| "ownership" | `owner` on every piece row, a `chr_` ID. It gates dismantle, repair, worker assignment, chest access and door permission. It is not legal status. [ruling 3] | 6.5 |
| "per-piece health applied by explicit rules" | `health_max` on the definition, `health_current` on the row. `DamagePiece` is dispatched only by the three rows of `config.building.damage`. [conflict row 8] | 14 |
| "repair" | `RepairPieceCommand`, which costs materials in proportion to the missing health. | 14.3 |
| "storage containers" | `piece.storage.chest`, reusing the container model (`ContainerSite`/`ContainerRecord`). [row 9] | 15.1 |
| "one crafting station as a placeable" | `piece.station.anvil`, which reuses the M3f `anvil` kind and the existing March Spear recipe. [row 9] | 15.2 |
| "persistence as a sparse world delta (D-05)" | Piece rows in the entities section, host-cell anchored and proven by `baseline_hash`. | 20 |
| "placement validation that rejects un-navigable or overlapping configurations" | Fifteen ordered checks. Overlap is judged by lattice slot and against authored geometry; navigability by reference-point connectivity. | 7, 18 |
| exit: "assign an NPC to work in it" | One persistent assignment of Kera Voss to the anvil bench's work anchor. [row 5, owner question] | 16 |
| exit: "navigates in, through, and around it ... straddling a cell boundary" | The Crossing Workshop at (99-105, 99-105) straddles four cells. Its doorway straddles z = 100. [row 6] | 17, 22 |
| exit: "save/load preserves every piece with correct ownership and health" | Row round-trip, digest and StateDump coverage, and a v14 fixture. | 20, 23 |
| exit: "damage/repair works and is explicit rather than emergent" | A three-row rule table, where each source is one named code point. | 14 |
| exit: "the navmesh updates on placement" | Read as "the domain navigation is invalidated by a deterministic `StructuresChanged(rect, revision)` after every structure change". [row 1] | 19 |
| proof: "navmesh path test including the straddling-seam case" | A headless Application test plus the windowed build beats. There is no streaming in Ashen Hollow, so RK-14's "force a cell unload" becomes save/load. | 22, 23 |

The following are **not** required by ROADMAP M7 and are deferred:
- the `construct_building` quest objective, which stays in `ObjectiveTypes.NotBuilt` (`src/Domain/Quests/Quests.cs:146`) until the M9 slice needs it;
- property threats and an attack-frequency option [row 8];
- upgrade tiers;
- beds, fences, posts, half walls and window walls (all optional variants);
- masonry or carpentry skill gating (PROGRESSION §417 names masonry, but no such skill exists in `content/skills/`);
- building XP;
- building interiors inside interior spaces;
- a persisted "structure" grouping record (see 6.1).

---

## 2. Invariants and hard non-goals

These are enforced by design, and most of them by tests:

1. **One storey (ruling 2).** No piece gives a walkable surface.
   - Body Y stays `Terrain.HeightAtMm + lift` (`src/Domain/Spatial/Kinematics.cs:159`).
   - `Kinematics` and `MovementRules` gain no fields.
   - There are no stairs, ladders, upper floors, climbing, walk surfaces or terrain flattening.
2. **No overhang blockers.** No piece emits a `Blocker` with `ClearanceMm > 0`.
   - `IsClear` ignores height and clearance (`Kinematics.cs:170-173`).
   - `Crosses` is height-blind (`src/Domain/Spatial/Blockers.cs:26`).
   - Navigation will be height-blind too. A lintel blocker would therefore close the doorway to sight, spawn and catch-up checks and navigation, even though it lets bodies under it.
   - Lintels and roofs are drawn by presentation only.
3. **Axis-aligned footprints only.** Every piece part is a `BoxBlocker` with integer mm corners (`Blockers.cs:40-41`).
4. **No structural simulation (D-08).** Support is a placement precondition, never an invariant kept afterwards. Removing a wall never removes a roof.
5. **Authority.** Every mutation is a `GameCommand` or an internal command, applied at a tick boundary. The placement preview is a read-only query that runs the same validation function (section 9).
6. **Determinism.** Placement math is integer. Collections are ordered by piece ID, and piece IDs order by placement sequence. The one float-dependent rule is choosing which piece a melee or shot struck. It uses the facing's `Math.Sin/Cos` exactly as today's combat trace does (`src/World/Runtime/Combat.cs:527-528`), and adds no new float-order dependence.
7. **Persistence.** A piece is a row, not a physics resolution (D-08). Navigation, collision space and the socket graph are derived and never saved.

---

## 3. The building lattice, module and conventions

### 3.1 Module

- **Module M = 3000 mm, half-module H = 1500 mm**, anchored at the world origin. Lattice lines are x = 3000·i and z = 3000·k.
  - This matches the asset kit's 3.0 m wall, floor and roof modules (`docs/SCALE_AUDIT_REPORT.md:177-188`), so art can arrive later without moving any saved piece.
- 3000 is divisible by 100, 125, 150, 200, 250, 300, 375, 500, 600, 750, 1000 and 1500 mm. A navigation grid of any of those sizes, aligned to the world origin, has cell lines on every lattice line.
- The 100 m seams (x = 100 000, z = 100 000) are **not** lattice lines (100 000 / 3000 is not whole).
  - Therefore no piece anchor, square centre or edge midpoint ever lies exactly on a seam. The host cell `CellKey.OfWorld(x/1000.0, z/1000.0)` of every anchor is unambiguous.
  - Every lattice square that contains a seam straddles it, which is how the M7 structure straddles cells by construction.
- `module_m` is **save-locked tuning**. Saved anchors are absolute, so changing the module makes every saved piece off-lattice. Never change it without a migration step (`docs/PERSISTENCE.md:276`).

### 3.2 Recommended navigation cell sizes

The navigation representation is not designed here. The building only guarantees the following.

A doorway's jamb inner faces sit 700 mm and 2300 mm along its edge from the lattice line. The table counts the navigation cells that fit wholly inside the free band [700 + r, 2300 − r]. "Free" is inclusive (distance ≥ r), which matches `Blocker.Separation` returning null at exactly r (`Blockers.cs:52`).

| Nav cell | r = 350 (people) | r = 450 (wolf) | r = 550 (boar) |
|---|---|---|---|
| 200 mm | 3 cells | 3 | 1 |
| 250 mm | 2 | 2 | 2 (inclusive) / 0 (strict) |
| 500 mm | **0** | 0 | 0 |

Recommendation to the navigation design: use a **200 or 250 mm** cell. 500 mm cannot pass a 1.6 m doorway even for a person. This draft's validation reference agent is r = 350 (section 18). Creatures are not a reference class in M7.

### 3.3 Rotation convention

- `rotation` is an integer r in {0, 1, 2, 3} quarter turns.
- A piece's local +Z axis points along world facing r·90 000 mdeg, in the facing convention of "0 = +Z, clockwise towards +X" (`content/regions/ashen_hollow.yaml:11`; `Kinematics.cs:30-34`).
- The local-to-world offset R_r (exact integers) is:
  - R_0(lx, lz) = ( lx,  lz)
  - R_1(lx, lz) = ( lz, −lx)
  - R_2(lx, lz) = (−lx, −lz)
  - R_3(lx, lz) = (−lz,  lx)
- A local facing f mdeg becomes (f + r·90 000) mod 360 000.
- A local box [x0, z0, x1, z1] rotates to the axis-aligned box that bounds the four rotated corners. It is exact, because quarter turns map boxes to boxes.

### 3.4 Slots and anchors (the lattice's "sockets")

Each piece has one **slot kind**. The command's anchor (x, z) and rotation must satisfy that kind's rule exactly. Arithmetic uses `WorldMath.FloorMod`.

| Slot kind | Anchor rule (mm) | Rotation rule | Slot key (occupancy) | Lattice geometry used for build-area containment |
|---|---|---|---|---|
| `square` (pads) | x ≡ H, z ≡ H (mod M) | any | `sq:i:k`, with i = (x−H)/M and k = (z−H)/M | the square [x−H, x+H] × [z−H, z+H] |
| `edge` (walls, doorways) | r even: x ≡ H, z ≡ 0 (an X-axis edge); r odd: x ≡ 0, z ≡ H (a Z-axis edge) | parity fixes the axis | `ex:i:k` or `ez:i:k` | the edge segment |
| `door` | the anchor of an intact doorway | r ≡ the doorway's r (mod 2) | `dr:<doorway key>`, which is the doorway's edge key | the doorway's edge segment |
| `roof` | x ≡ H, z ≡ H | any | `rf:i:k` | the square |
| `furniture` (chest, anvil) | x ≡ H, z ≡ H | any | `fu:i:k` | the square |

A slot key holds at most one intact piece. Different slot kinds never conflict geometrically by construction (section 5 shows the clearances). That is why piece-against-piece footprint overlap is **not** checked: walls meeting at a corner overlap in the 400 × 400 mm corner square, and that overlap is intended.

---

## 4. Piece definition: the content kind `piece`

### 4.1 Kind registration

| Item | Value | Where |
|---|---|---|
| Kind / FullKind | `piece` | `src/Content/SchemaResolution.cs`, a new `kinds["piece"]` entry following the `faction` pattern at `:158-164` |
| Directory | `content/pieces/` (subdirectories free: `pieces/timber/`, `pieces/fixtures/`) | the loader recurses (`src/Content/ContentLoader.cs:213`) |
| ID prefix / shape | `piece.<family>.<name>`, for example `piece.wall.timber` | the `DefinitionId` grammar (`src/Domain/DefinitionId.cs:19-20`) |
| Reference suffix | `["piece_ref"] = new[] { "piece" }` | `src/Content/ContentChecks.cs:18-44` |
| Instance kind | new `EntityKind.Piece`, prefix `pce`; `bld` stays reserved for a future structure or settlement record | `src/Domain/EntityKind.cs:17,40`; `TryInferFromDefinition` stays unchanged, because pieces are always created with explicit IDs |
| Lint family | `BLD001`-`BLD006` (below), plus `WLD015` for build areas | `src/Content/BuildingContent.cs` (new), `WorldContent.cs` |

Why `piece`, not `structure`:
- The region YAML already uses `structures:` for authored blockers with local IDs (`ashen_hollow.yaml:64`). Reusing the word would blur authored and built geometry.
- DATA_MODEL's own objective already names `piece_ref` (`docs/DATA_MODEL.md:453`). Registering `piece` makes that documented field validate, which resolves contradiction C3 in building_docs.

Other registration consequences:
- `MOD001` automatically reserves `piece.mod.*`.
- `tests/Content.Tests/ValidationTests.cs` `LoadAll_Loads_Yaml_Files` gains the new IDs. `KnownDirectories_Is_Closed_Set` gains `pieces`.

### 4.2 Schema

The field set is closed. Any other key is a `BLD002` error, following the quest builder's "not a quest field" style (content_registry §3.2 step 3).

```yaml
id: piece.wall.timber               # piece.<family>.<name>
kind: piece
schema: 1
display_key: piece.wall.timber.name
tags: [piece]
name: Timber Wall
notes: ...
family: wall                        # closed: pad | wall | doorway | door | roof | storage | station
slot: edge                          # closed: square | edge | door | roof | furniture (each family has exactly one legal slot)
rotations: [0, 1, 2, 3]             # quarter turns (config.building.rotation_step_deg must be 90 in M7)
bounds_m: [-1.7, -0.2, 1.7, 0.2]    # local placement bounds at rotation 0 [min_x, min_z, max_x, max_z]
parts:                              # blocking footprint(s); omitted for pads and roofs
  - { box_m: [-1.7, -0.2, 1.7, 0.2], height_m: 3.0, traversal: solid }   # closed: solid | door
sockets:                            # what it offers (provides) and what it needs (mounts)
  - { type: edge_mount, at_m: [0, 0], axis: x }
cost:                               # items spent on placement, in list order
  - { item_ref: item.material.timber, count: 2 }
health_max: 200
supports_roof: true                 # optional (default false): counts as a wall under a roof edge
# family-specific, closed:
# container: { stack_slots: 12, at_m: [0, 0.9] }                   # storage only: where the chest's site point is
# station:   { kind: anvil, work_anchor_m: [0, -0.25], work_facing_deg: 0 }   # station only
```

Field semantics:

- `slot` must be the one legal for the family: pad → square, wall/doorway → edge, door → door, roof → roof, storage/station → furniture. The field is kept explicit so that a future `fence` family (edge) or `post` family (a vertex slot kind, not built) is data, not code.
- `bounds_m` is the rectangle used for reach, build-area protection, authored overlap and protected ground. For pads and roofs it is the square [−1.5, −1.5, 1.5, 1.5]. For blocking pieces it is the union of `parts`.
- `parts` are converted to mm at load with `Mm` (round half away from zero; `src/Content/WorldContent.cs:456-462`). They must be whole mm and lie inside `bounds_m`.
- `traversal`:
  - `solid` parts block always.
  - A `door` part blocks only while its piece's `door_open` is false.
  - Non-blocking pieces have no parts. They are published to navigation as a `NonBlocking` footprint of their bounds (section 19).
- `sockets` in M7 (types are a closed set):

  | Socket type | Kind | Offered by | Needed by | Mates with |
  |---|---|---|---|---|
  | `edge` | provider | pad: four at (0, ±1.5) with axis x, and (±1.5, 0) with axis z | - | `edge_mount` |
  | `square` | provider | pad: one at (0, 0) | - | `square_mount` |
  | `door` | provider | doorway: one at (0, 0), axis x | - | `door_mount` |
  | `edge_mount` | mount | - | wall, doorway | `edge` |
  | `square_mount` | mount | - | chest, anvil | `square` |
  | `door_mount` | mount | - | door | `door` |

  A mount is satisfied when an **intact** piece offers a compatible provider at exactly the same world position (after rotation) with the same world axis. Tolerance is 0 mm. Roofs have no mount: they use the support rule (section 7.2, check 8).
- `cost` lists `{item_ref, count ≥ 1}`. `item_ref` must name an `item` kind (not a weapon or armor sub-kind). `ExchangeItems` spends these (section 13).
- `health_max` is a whole number ≥ 1. The definition names `health_max` and the row names `health_current`, per the DATA_MODEL naming corollary (`docs/DATA_MODEL.md:158`).
- `container.at_m` is the chest's container site point in local metres. `container.stack_slots` is its capacity.
- `station.kind` must be a station kind some recipe uses. Today that is `forge` (`content/recipes/smithing/iron_billet.yaml:9`) or `anvil` (`march_spear.yaml:8`). `work_anchor_m` and `work_facing_deg` give the worker's pose in local terms.
- **No presentation asset field.** Art for pieces goes in a new `pieces` section of `src/Presentation/Art/art_bindings.json`, keyed by piece definition ID. Bindings are "data, so no content ID lives in the game's code; presentation only, so nothing here touches a rule, a save or the content hash" (`src/Presentation/Art/ArtBindings.cs:28-31`). Putting an asset ID in rules content would move `content_hash` on an art change.

### 4.3 Lints

The builder is `BuildingContent.Validate`, hooked into `ContentLoader.LoadAll` with the three-line pattern at `src/Content/ContentLoader.cs:155-197`, after `CraftingContent`. Per-field checks carry the file and line through `ContentChecks`-style `Fields` helpers where the field is scalar.

| Code | Checks |
|---|---|
| BLD001 | The catalogue builds, with a single `Try` wrapper. It is fail-fast and must at least name the definition's `SourceFile`. |
| BLD002 | Closed field set. `family`, `slot`, `traversal` and socket `type`/`axis` come from closed sets, and `slot` is legal for the family. `rotations` ⊆ {0..3}, non-empty and distinct. `health_max` ≥ 1. |
| BLD003 | Lattice consistency, all in local mm. Every provider socket lies on a lattice position for its type: `edge` at (0, ±1500) or (±1500, 0) with the right axis, `square` and `door` at (0, 0). Every mount is at (0, 0). `bounds_m` of square and roof pieces is exactly the square. Edge pieces' bounds lie within [−1700, −200, 1700, 200]. Furniture bounds lie within [−1300, −1300, 1300, 1300], which keeps furniture clear of a wall's inner face at 1300. `parts` lie within `bounds_m`. A `door` part appears only on `family: door`. |
| BLD004 | `cost` entries name existing `item`-kind definitions, with count ≥ 1. |
| BLD005 | `container` appears only on storage, with `stack_slots` ≥ 1 and `at_m` inside bounds. `station` appears only on station; `kind` is used by some recipe; the work anchor lies inside the square and not inside any part inflated by `config.building.navigability_radius_m`. |
| BLD006 | `config.building` is present iff any `piece` or build area exists. `module_m` is 3.0 and `rotation_step_deg` is 90 in M7. Every value is in range (section 13.4). |
| WLD015 | Build areas (section 17): key prefix `build_area.`; box corners on the lattice; the box inside the region bounds and cells (reusing `Covered`, `WorldContent.cs:386-400`); `max_pieces` ≥ 1; unique keys; and the box clear of every protected zone of section 7.2 check 11. |

---

## 5. The greybox catalogue (M7: seven pieces)

All dimensions are local mm at rotation 0. The origin is the slot anchor: the square centre, or the edge midpoint.

| ID | Family / slot | Blocking parts (box [x0, z0, x1, z1], height) | Bounds | Cost | health_max | Other |
|---|---|---|---|---|---|---|
| `piece.pad.timber` | pad / square | none | [−1500, −1500, 1500, 1500] | 1 timber | 200 | sockets: 4 `edge`, 1 `square`; terrain relief ≤ 250 mm |
| `piece.wall.timber` | wall / edge | [−1700, −200, 1700, 200], h 3000, solid | = part | 2 timber | 200 | `edge_mount`; `supports_roof: true` |
| `piece.doorway.timber` | doorway / edge | [−1700, −200, −800, 200] h 3000 solid; [800, −200, 1700, 200] h 3000 solid | [−1700, −200, 1700, 200] | 2 timber | 200 | `edge_mount`, `door` provider; opening 1600 wide; lintel drawn 2400-3000, not a blocker; `supports_roof: true` |
| `piece.door.timber` | door / door | [−800, −200, 800, 200], h 2400, **door** | = part | 1 timber | 120 | `door_mount`; `door_open` state |
| `piece.roof.timber` | roof / roof | none | [−1500, −1500, 1500, 1500] | 1 timber | 100 | support rule; drawn as a 200 mm slab at wall top |
| `piece.storage.chest` | storage / furniture | [−500, 600, 500, 1200], h 700, solid | = part | 2 timber | 100 | `square_mount`; container `stack_slots: 12`, `at_m: [0, 0.9]` |
| `piece.station.anvil` | station / furniture | [−500, 400, 500, 1000], h 900, solid | = part | 2 timber + 2 `item.material.iron_ingot` | 300 | `square_mount`; station `kind: anvil`, `work_anchor_m: [0, −0.25]`, `work_facing_deg: 0` |

Why these numbers:

- **Wall thickness 400 mm, centred on the lattice line.** This is the authored wall thickness (`ashen_hollow.yaml:68-78`). Centring lets one wall serve two rooms.
  - Room interior per module is 3000 − 400 = 2600 mm. A body of diameter 700 has 950 mm to spare on each side.
  - The ±200 mm extension past the edge ends fills corners, so a room is sealed except at its doorways. That is the same property the lodge and smithy walls have (spatial_movement §5.5).
- **Wall height 3000 mm.** This matches the smithy walls (`ashen_hollow.yaml:74-78`).
  - It is above the 1150 mm jump apex (`content/config/base_speeds.yaml:15`), so walls are never jumped.
  - It is above the 1800 mm standing height, which matters only to presentation.
- **Doorway opening 1600 mm and door height 2400 mm.** These match both authored doors (`ashen_hollow.yaml:143-144`), so whatever passes an authored doorway passes a player doorway.
  - For a 350 mm body there is 450 mm to spare on each side.
  - Section 3.2 gives the navigation cell counts.
  - The domain has one 1800 mm stand height for every body (`base_speeds.yaml:18`). The 2.35 m and 2.60 m body families (`docs/CANONICAL_BODY_AND_SKELETON.md:29-30`) are not M7 agents.
- **Door part.** It fills the opening exactly: its ±800 edges touch the jambs, and touching is not overlap. It is 400 mm thick like authored doors, so a closed door is a continuous wall.
- **Chest (1000 × 600, 700 high).** It sits against the back of its square, with a 100 mm gap to a wall's inner face at 1300. That leaves 1900 mm of open floor in front.
  - At 700 mm tall it is jumpable by the player (apex 1150) and solid to every other mover.
  - Its site point (0, 900) is within the 1600 mm container reach (`src/World/Runtime/Items.cs:459`, reach = interact reach per `src/Application/GameSession.cs:91-92`) of anywhere in the square's front half.
- **Anvil bench (1000 × 600, 900 high).** It sits against the back of its square in the same way.
  - The work anchor at (0, −250) faces the bench, 300 mm from its front face (1900 − 1250 − 350 = 300 in module-local terms).
  - The anchor was chosen so that its containing navigation cell is free on both a 200 and a 250 mm grid:
    - 250: cell [1250, 1500] is 400 mm from the face at 1900;
    - 200: cell [1200, 1400] is 500 mm from it.
  - An anchor 900 mm in front of a centred bench would fall between the bench and the wall, in a 300 mm band that holds no free 250 mm cell. That layout was rejected.
- **Health.**
  - Walls and doorways take 200 / 10 = 20 sword blows.
  - A door takes 12 blows.
  - A chest takes 10 blows.
  - The anvil bench takes 30 blows.
  - Pads and roofs have health for uniformity, but no M7 damage source can reach them: they emit no blocker (section 14.1).

Pieces that are **not** in M7:
- **post and fence.** The roof rule does not need posts (section 7.2, check 8), and no M7 proof needs a fence.
- **half wall and window wall.** They are optional variants. Adding one needs a new definition file plus the art binding, with zero C# changes.
- **bed.** Rest and time advance are not an M7 system.

---

## 6. The runtime piece instance

### 6.1 No persisted "structure" record

WORLD_ARCHITECTURE §10 and PERSISTENCE §5.4 describe a ULID-keyed structure that owns its pieces (`docs/WORLD_ARCHITECTURE.md:409`; `docs/PERSISTENCE.md:250`). M7 does not build that record. A structure would have to:
- merge when a piece joins two structures;
- split when a removal disconnects the socket graph;
- carry ownership that pieces must copy anyway.

None of that serves an M7 exit criterion. In M7 a "structure" is a **derived** connected component of the socket graph. The F2 debug view may name it by its smallest piece ID. `bld` stays reserved in `EntityKind` for the settlement-level record that M10 may need.

### 6.2 The row: `PieceRecord` (`src/World/WorldDelta.cs`)

```csharp
/// A player-placed building piece (M7): one row, whole, anchored to the cell of its anchor point and proven against that
/// cell's baseline like a created instance. Its shape is its definition's; only what the definition cannot know is here.
public sealed record PieceRecord(
    EntityId InstanceId,      // pce_, derived (6.3)
    string DefId,             // piece.*  - through the definition-ID pass on load
    string HostCell,          // CellKey.OfWorld(XMm/1000.0, ZMm/1000.0), never on a seam (3.1)
    long XMm, long ZMm,       // the slot anchor, absolute world mm (the creature-record convention, WorldDelta.cs:169-170)
    int Rotation,             // 0..3 quarter turns
    EntityId Owner,           // chr_ of the placing character
    int HealthCurrent,        // 1..health_max
    string? BaselineHash = null)
{
    public bool DoorOpen { get; init; }          // door pieces only; false elsewhere
    public string? WorkerNpcId { get; init; }    // station pieces only: npc.* definition ID of the assigned worker
}
```

Not stored, because each is derivable from the definition, the pose or the other rows:
- footprints, bounds, sockets and socket parents;
- `health_max` and cost;
- the container site point and capacity;
- the station kind and work anchor;
- slot keys;
- which cells the piece overlaps.

The VS row's `socket_parent` (`docs/VERTICAL_SLICE.md:161`) is deliberately absent. Storing parents would force re-parenting whenever one of two supporting pads is dismantled. Dependencies are recomputed from geometry when needed (section 14.4).

Coordinates are absolute mm plus a host cell, not the created record's cell-relative cm (`WorldDelta.cs:54`). A piece's bounds cross cells. The creature record already uses absolute mm with a fixed host cell (`WorldDelta.cs:162-175`).

The world also keeps one global scalar: `long StructureSequence`. It is incremented once for every structure mutation: each piece placed, dismantled or destroyed, and each door opened or closed. It is persisted (section 20.1), and it is the navigation **revision**.

### 6.3 Instance-ID minting (replay- and digest-safe)

```csharp
// src/Domain/EntityId.cs, beside Create:
/// A runtime identity derived from what made it, not drawn at random: the same commands make the same IDs, so a replay
/// and a save-then-continue mint identically (M7 pieces; the NPC and creature identities are the same idea).
public static EntityId Derived(EntityKind kind, long ordinal, string tag, string salt);
//   = Create(kind, ordinal, new CanonicalHasher().Add(tag).Add(salt).Add(ordinal).FinishBytes()[0..10])

piece id     = EntityId.Derived(EntityKind.Piece,     seq, "unnamed.piece/v1",           owner.Value)
chest cnt_ id = EntityId.Derived(EntityKind.Container, seq, "unnamed.piece-container/v1", pieceId.Value)
```

Here `seq` is `StructureSequence` after the increment for the placement. Properties:

1. **Replay-stable.** The same commands at the same boundaries give the same `seq`, so the same IDs. The raw `Simulation.StateDigest()` (`src/World/Runtime/Simulation.cs:357-364`) is then equal between a session replayed from a save and the original. Existing precedent: `NpcSystem.InstanceIdOf` (`src/World/Runtime/Social.cs:121-125`) and creature identities.
2. **Save-then-continue-stable.** `seq` is persisted. The next ID after a load equals the next ID without one.
3. **Unique.** `seq` never repeats within a world. Including the owner's random `chr_` ID in the hash makes IDs distinct across characters, which is the network seam.
4. **Not derived from the world seed.** This keeps `EntityId`'s remark (`src/Domain/EntityId.cs:17-21`) true in letter; the remark is amended to name derived identities.
5. **Ordered.** The ULID timestamp field holds `seq`. Crockford-encoded big-endian timestamps sort lexicographically, so ordinal order of `EntityId.Value` is placement order. Every "tie-break by piece ID" in this draft is therefore "tie-break by placement order", which is replay-stable.
6. **Registry-mediated.** Every derived ID is registered with `registry.CreateEntity(DefinitionId.Parse(defId), id)` (`src/EntityRegistry/EntityRegistry.cs:44-66`). The documented rule "Generated only by the Entity Registry" (`docs/DATA_MODEL.md:123`) and AGENTS.md's "never build one by hand" are amended to "from `EntityId.NewId`, `EntityId.Derived` or the registry".

Item identities are unchanged. Refunds mint items through `GrantItem` → `NewItem` → `NewId` (`src/World/Runtime/Items.cs:367-374, 657`). Replay tests whose window contains a refund compare `StateDump.Render(replayable: true)` (`src/Application/StateDump.cs:25-46`), exactly as crafting output already requires.

### 6.4 Health

- A piece is placed at `health_current = health_max`.
- A row with `health_current` 0 never exists: reaching 0 removes the piece in the same handler.
- On load, a saved `health_current` above the current `health_max` (a content retune) is clamped to `health_max`. The clamp is written by `BuildingSystem.Populate` through its own slice, and reported in `Simulation.StructureAudit` (section 20.6).

### 6.5 Ownership

- `Owner` is the `chr_` ID of the character that placed the piece. Transfer is out of scope, as WORLD_ARCHITECTURE already says (`docs/WORLD_ARCHITECTURE.md:418`).
- What ownership gates in M7:
  - dismantle, repair and assign/release are refused unless the actor owns the piece;
  - a storage chest's container moves are refused unless the actor owns the chest;
  - a piece door may be opened by its owner, the owner's companions, and NPCs assigned to any piece the owner owns (section 11).
- **Separation (ruling 3).** Ownership is a building fact. It is not legal status, jurisdiction, faction property or a reputation input. Nothing in M7 derives crime, hostility, standing or attack legality from it. The seam for a future faction act is one line in `BuildingSystem.Place` (section 7.3), not built.

---

## 7. Placement: the authoritative command

### 7.1 Command and events

```csharp
// src/World/Runtime/Building.cs
public sealed record PlacePieceCommand(EntityId Actor, string PieceDefId, long XMm, long ZMm, int Rotation) : GameCommand(Actor);

public sealed record PiecePlaced(EntityId PieceId, string DefId, long XMm, long ZMm, int Rotation, EntityId Owner, long Revision, long Tick);
public sealed record StructuresChanged(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, StructureChangeKind Kind, long Revision, long Tick);
public enum StructureChangeKind { Placed, Dismantled, Destroyed, DoorOpened, DoorClosed }
```

- The command carries the discrete pose: a lattice anchor plus quarter turns. It never carries an aim point, a camera ray or a tolerance. This keeps `Commands_CarryNoCameraState_SoEveryPerspectiveSubmitsTheSameThing` green (`tests/Application.Tests/SessionTests.cs:162-178`).
- The authority needs no tolerance: the pose must be lattice-exact.
- `DrainCommands` gains the arm `PlacePieceCommand c => _building.Handle(c, WorldTick)` (`src/World/Runtime/Simulation.cs:273-299`).

### 7.2 The ordered validation (`BuildingRules.Validate`)

One pure function, in `src/Domain/Building/Building.cs`, is used by the command handler and the preview query alike (section 9). It takes a read-only `PlacementContext`:
- the catalogue, constants and build areas;
- the authored layout (static blockers, doors, barriers, NPC sites, containers, stations, nodes, spawners, spawn);
- the current piece rows;
- current bodies (player, NPCs, companions, living creatures, with radii);
- the carried inventory and equipment;
- the terrain grid;
- an `INavigability` handle.

It returns `PlacementCheck(bool Allowed, PlacementRule? Failed, string? Reason, BoundsMm Bounds, ImmutableArray<BoxBlocker> Parts, ImmutableArray<StackTake> Takes)`. Checks run in this order, and the **first** failure is returned. The reason texts follow the existing lowercase style. Presentation renders IDs through `DisplayName`.

| # | Rule (`PlacementRule`) | Exact test | Reason text (example) |
|---|---|---|---|
| 1 | `Actor` | The actor is the player; not `Defeated`; combat phase `Idle` at this tick (as crafting, `src/World/Runtime/Crafting.cs:158-161`). | "dead" / "busy" |
| 2 | `Definition` | `PieceDefId` is in the catalogue. | "{id} is not a piece this build knows" |
| 3 | `Rotation` | r ∈ {0..3} and r ∈ `def.Rotations`. | "{name} cannot be turned that way" |
| 4 | `Lattice` | (x, z, r) satisfies the slot rule of 3.4. For a door, an intact doorway without a door has anchor (x, z) and r ≡ its r (mod 2). | "that is not on the building grid" |
| 5 | `BuildArea` | The piece's slot geometry (square, or edge segment) lies inside one build area box, with closed intervals. Edges on an area's boundary line are inside; their 200 mm corner extensions may reach past it. | "you may only build inside a build area" |
| 6 | `Reach` | The squared distance from the actor's body (x, z) to the nearest point of `Bounds` is ≤ `place_reach_mm`² (6000²), in long arithmetic. | "that is 7.20 m away; building reach is 6.00 m" |
| 7 | `Slot` | No intact piece holds the slot key. | "a Timber Doorway already stands there" |
| 8 | `Support` | **Mounts:** every mount socket has a compatible provider on an intact piece at the same world position and axis (edge_mount → a pad on either adjacent square; square_mount → a pad on the square; door_mount → the doorway). **Roof:** at least one of the square's four edges holds an intact piece with `supports_roof`, **or** an edge-adjacent square holds an intact roof that has such an edge itself. The span is depth 1 and evaluated now only. | "a wall needs a floor pad on one side" / "a roof needs a wall under one of its edges, or a roofed neighbour that has one" |
| 9 | `Terrain` | Pads only: max − min of `TerrainGrid.HeightAtMm` over a 13 × 13 sample lattice at 250 mm spacing across the square, corners and edges included, is ≤ `pad_max_relief_mm` (250). Integer (`src/Domain/Spatial/TerrainGrid.cs:51-64`). | "the ground here is too uneven for a pad: 0.31 m of rise, 0.25 m allowed" |
| 10 | `Authored` | `Bounds` does not strictly overlap any authored static blocker (`Setup.Layout.Space.Blockers`, the authored list only), any door's closed footprint, or any barrier footprint, whatever their state. Box/box overlap: minA < maxB ∧ minB < maxA on both axes. Box/circle: (cx − clamp(cx))² + (cz − clamp(cz))² < R², in long. Touching is not overlap. | "that would build over rock_well" |
| 11 | `Protected` | `Bounds` does not strictly intersect any protected zone: the region spawn circle (3000 mm); each NPC site circle (1500); each authored container, station and node point circle (1500); each authored door's closed box expanded by 1500 on every side; each spawner's disc of radius `radius + largest creature radius in it + 1000`; and each patrol leg, sampled every 500 mm (endpoints included, integer division rounding away from zero) as circles of that same radius. | "that ground is kept clear (Kera Voss's place)" |
| 12 | `Bodies` | Pieces with parts only: no part (a door is placed closed) strictly overlaps a body circle. Bodies are the player (350), every NPC including companions (350; `src/World/Runtime/Systems.cs:86`), and every living creature (its `RadiusMm`). This is `Separation` not null, the same test the door-closing check uses (`Systems.cs:267`). | "someone is standing there" |
| 13 | `PieceCap` | Intact pieces whose anchor lies in this build area < `max_pieces` (256). This is the RK-06 per-area ceiling as a content constraint (`docs/RISK_REGISTER.md:158`). | "the crossing already holds 256 pieces" |
| 14 | `Materials` | For each cost line in order: the carried, **unequipped** stacks of that definition, ordered by quality ascending then `ItemId` ordinal, cover the count. The resulting `Takes` are those stacks and counts. Worst materials go first, since quality does nothing for pieces. Crafting spends best-first (`Crafting.cs:177-178`) because there the weakest input caps the work. | "needs 2 item.material.timber" (the crafting wording, `Crafting.cs:188`) |
| 15 | `Navigability` | Only when the piece adds a **solid** part (wall, doorway, chest, anvil), and only when the caller asks. The section 18 check on the candidate state. | "that would cut Kera Voss's work place off" / "that would shut in the chest" |

Every check is a pure read. Checks 1-14 cost microseconds. Check 15 is the only one that may cost milliseconds (section 18.4).

### 7.3 Commit

```
string? Handle(PlacePieceCommand c, long tick):
    var check = BuildingRules.Validate(Context(c.Actor), c.PieceDefId, c.XMm, c.ZMm, c.Rotation, checkNavigability: true)
    if (!check.Allowed) return check.Reason
    // the refusable cross-system step first (the GatheringSystem pattern, Crafting.cs:100-102)
    if (_context.Dispatch(new ExchangeItems(check.Takes, ItemId: null, Count: 0, Quality: 0)) is { } refused) return refused
    long seq = State.StructureSequence + 1
    var id   = EntityId.Derived(EntityKind.Piece, seq, "unnamed.piece/v1", c.Actor.Value)
    var row  = new PieceRecord(id, def.Id, CellOf(c.XMm, c.ZMm), c.XMm, c.ZMm, c.Rotation, c.Actor, def.HealthMax)
    State.PlacePiece(_owner, row, seq)            // WorldDelta: store row, register id, set StructureSequence = seq
    State.SetSpace(_owner, RebuildSpace())         // 12.1
    _context.Dispatch(new NavigationInvalidated(check.Bounds, StructureChangeKind.Placed, seq))   // 19.2
    Publish(new PiecePlaced(id, def.Id, c.XMm, c.ZMm, c.Rotation, c.Actor, seq, tick))
    Publish(new StructuresChanged(check.Bounds..., StructureChangeKind.Placed, seq, tick))
    // seam, not built: a faction act for building would be dispatched here (RecordDeed pattern)
    return null
```

- `ExchangeItems` with `ItemId: null` spends only, and it is all-or-nothing (`src/World/Runtime/Items.cs:86-91, 327-357`). It cannot refuse after check 14 passed in the same drain, but the handler still honours a refusal.
- A chest's container record is **not** created at placement. It materialises on first use through the existing path (section 15.1). An empty chest is therefore no save row beyond its piece.
- Two commands for the same slot in one drain: FIFO. The second is refused by check 7 (`Simulation.cs:271`).

---

## 8. Snapping

### 8.1 Socket coordinates, compatibility and tolerance

- Socket positions are local integer mm, rotated by R_r and added to the integer anchor. Axes rotate with the piece: axis x at an odd r becomes a world-z axis.
- The compatibility table is in 4.2. Matching is exact equality of world position (mm), type pairing and world axis. **Tolerance: 0 mm.** Every socket lies on lattice positions (BLD003) and every anchor is lattice-exact (check 4), so equality is well defined and float-free.
- An **index** of intact providers is rebuilt with the space (12.1): `SortedDictionary<(SocketType, long X, long Z, Axis), ImmutableArray<EntityId>>`, with IDs ascending.
  - When a mount finds several providers (a wall between two pads), any one satisfies it, and none is recorded.
  - Where a rule needs one (the dismantle dependency test), it asks "does another intact provider exist?". That is a count, so no iteration-order question arises.

### 8.2 Snapping an aim point to a pose (presentation-side, pure Domain)

```csharp
// src/Domain/Building/Building.cs - pure, called by presentation every frame; never by the authority
public static (long XMm, long ZMm, int Rotation)? Snap(BuildingCatalog catalog, IReadOnlyList<PieceView> pieces,
    string pieceDefId, long aimXMm, long aimZMm, int rotation);
```

Snapping is deterministic, with no unordered iteration:
- **square, roof, furniture:** x = M·⌊aimX/M⌋ + H, z = M·⌊aimZ/M⌋ + H (floor division; the region is non-negative, but `FloorMod` semantics are used anyway). r is kept.
- **edge:** the requested r fixes the axis.
  - r even: x = M·⌊aimX/M⌋ + H, z = M·⌊(aimZ + H)/M⌋.
  - r odd: x = M·⌊(aimX + H)/M⌋, z = M·⌊aimZ/M⌋ + H.
  - Unique by construction. An aim exactly on a half-way line resolves by the floor, never by a tie.
- **door:** candidates are intact doorways with no door whose anchor is within 2000 mm of the aim. Take the minimum squared distance (long). Ties go to the lower doorway ID, which is placement order. r becomes the doorway's r, or r + 2 when the player's requested r has the other parity-preserving sense. That is how the player chooses which side the door swings to (a presentation-only difference). Returns null when there is no candidate.

The player picks the piece and r with keys (section 21). Snap produces the pose. The ghost shows `PreviewPlacement` of that pose. The command carries that pose.

---

## 9. The placement preview

```csharp
// Simulation (src/World/Runtime/Simulation.cs) - read-only, the Aim precedent (:198-206); added to the allow-list in
// tests/Architecture.Tests/ArchitectureTests.cs:111-131 with the comment "the placement ghost (M7)".
public PlacementPreview PreviewPlacement(string pieceDefId, long xMm, long zMm, int rotation, bool checkNavigability);

public sealed record PlacementPreview(
    bool Allowed, PlacementRule? Failed, string? Reason,
    long MinXMm, long MinZMm, long MaxXMm, long MaxZMm,
    ImmutableArray<BoxBlocker> Parts, ImmutableArray<CostLine> Cost, bool NavigabilityChecked);
public sealed record CostLine(string ItemId, int Count, int Carried);
```

- The preview calls `BuildingRules.Validate` with the actor = the player, exactly as the handler does. At equal state it therefore returns the same first failure and reason as the command would. A test asserts this for a table of poses (section 23).
- **Cost of calling it.**
  - Checks 1-14 are cheap enough to call every frame.
  - Presentation calls with `checkNavigability: true` only when the snapped pose or `Simulation.StructureRevision` changed since its last call, which is at most a few times a second.
  - Between those calls, the ghost shows the last navigability verdict for that pose.
- **What stays advisory.** Bodies move between the preview and the command, and so does anything else in the world. The ghost can be green and the command still refused ("someone is standing there"). That is the intended model: the simulation validates the final command (D-11).
- **Other read surfaces**, as get-only properties that need no allow-list change:
  - `Simulation.Pieces` (`ImmutableArray<PieceView>`, sorted by ID);
  - `Simulation.Space` (the current `WalkSpace`, 12.1);
  - `Simulation.Stations` (authored plus piece stations);
  - `Simulation.StructureRevision`;
  - `Simulation.StructureAudit`;
  - `Simulation.WorkAssignments`.

```csharp
public sealed record PieceView(EntityId Id, string DefId, string Family, long XMm, long ZMm, int Rotation, EntityId Owner,
    int HealthCurrent, int HealthMax, bool DoorOpen, string? WorkerNpcId,
    long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, ImmutableArray<BoxBlocker> Parts, string? ContainerKey, string? StationKey);
```

---

## 10. Foundations and terrain: what "floor at ground level" means

1. **Movement is untouched.** A pad has no `Blocker` and changes nothing in `Kinematics`. A body on a pad stands at `Terrain.HeightAtMm(x, z)`, exactly as next to it. Navigation sees a pad only as a `NonBlocking` footprint (section 19).
2. **What a pad is**, and all it is:
   - a placement anchor: it provides four `edge` sockets and one `square` socket;
   - a terrain-tolerance gate: relief ≤ 250 mm under the square;
   - a visual floor.
3. **What it looks like.** Presentation draws the pad top as a surface **draped** on the domain's own terrain triangles (the same split `HollowView.BuildTerrain` uses, `src/Presentation/Greybox/HollowView.cs:91-128`), raised 20 mm, with a 150 mm skirt round its edge.
   - Feet stand at the terrain, so they sit 20 mm into the drawn floor on every slope: invisible, and never floating.
   - Walls are drawn from the lowest terrain sample under them minus 200 mm, the existing structure convention (`HollowView.cs:169-170`).
   - A foundation therefore **visually covers uneven ground while gameplay stays terrain-bound**, and the 250 mm relief cap keeps the drape looking like a floor rather than a ramp.
4. **Why 250 mm.** The worst lattice square inside the M7 build area has 228 mm of relief: the square at x 111-114, z 105-108. That was computed with `HeightAtMm`'s exact integer arithmetic over the region grid (`ashen_hollow.yaml:15-62`). All 81 squares of the area are buildable. The four-cell square (99-102)² rises 96 mm (6352-6448 mm). The workshop's other squares in section 22 rise between 64 and 140 mm.
5. **A flat walkable floor is not built.** If a future milestone truly needs one, the architectural consequences are:
   - `Kinematics.Step` gains a walk-surface query (Y = max(terrain, surface)) and a step-height rule. Every mover and the presentation prediction change with it.
   - `Blocks`, `IsClear` and `CanStand` gain surface heights.
   - Jump landing gains "landed on a surface" semantics.
   - Navigation becomes 2.5D (heights per cell and step constraints).
   - Combat traces and perception gain vertical terms.
   - The persisted body Y changes meaning.
   - Every movement determinism test is re-proven.
   - This is exactly the vertical domain ruling 2 excludes. M7 does not open it.

---

## 11. Doors

**Model: an authoritative open/closed blocker, the `DoorSite` concept kept, with its state moved onto the piece row.** Authored doors keep their `world.*` flags. A player door cannot use a flag, because flags are declared content and are cell-scoped (`src/World/WorldDelta.cs:250-259`; lint WLD004, `src/Content/WorldContent.cs:159-164`).

- **Blocking.** A door piece with `DoorOpen == false` contributes its part as a movable blocker. `SystemContext.ClosedDoors()` (`src/World/Runtime/Systems.cs:48`) becomes the authored closed doors, then standing barriers, then **closed piece doors in piece-ID order**. Therefore:
  - every mover's `Obstacles()` (`Systems.cs:82-86`);
  - creature and companion obstacle lists (`Creatures.cs:585, 618, 830`; `Companions.cs:574`);
  - every `Walled` test (`Combat.cs:569-570`; `Companions.cs:598`; `Creatures.cs:893`);
  - and presentation prediction through `Simulation.DynamicBlockers` (`Simulation.cs:254-255`)

  all see a closed piece door exactly as they see an authored closed door. It blocks movement, blows, shots and sight while closed.
- **Player operation.** The player uses the existing `InteractCommand(Actor, TargetKey)`, with `TargetKey` = the door piece's ID string (`pce_...`).
  - `InteractionSystem.Handle` (`Systems.cs:251-273`) gains a first branch: if `EntityId.TryParse(TargetKey)` yields a `Piece`, it dispatches `OperatePieceDoor(pieceId, operatorId: _player, open: null /*toggle*/)` and returns its refusal.
  - The E prompt and presentation focus therefore need only a piece-door candidate.
- **Internal command** (to `BuildingSystem`): `OperatePieceDoor(EntityId PieceId, EntityId Operator, bool? Open)`. It validates:
  1. The piece exists, is intact and is a door.
  2. The operator is permitted: the owner; a companion in the owner's roster (`State.Companions`); or an NPC assigned to a station the owner owns. **Creatures never operate doors.** Other NPCs never operate doors in M7 (none moves).
  3. The operator's body is within `InteractReachMm` (1600) of the door's closed box (`DistanceTo`, the rule at `Systems.cs:262-264`).
  4. Closing: refused if **any** body overlaps the closed box. Bodies are the player, NPCs including companions, and living creatures. This is stricter than the authored-door check, which tests only the player's body (`Systems.cs:267-268`; spatial_movement §9 edge case).
  5. A no-op if the door is already in the requested state.

  On success:
  - `StructureSequence += 1`;
  - `DoorOpen` is set;
  - `NavigationInvalidated(doorBox, DoorOpened/DoorClosed, seq)` is dispatched;
  - `DoorToggled(Operator, pieceId.Value, open, tick)` is published (the existing event, `src/World/Runtime/Events.cs:25`);
  - `StructuresChanged` is published.
- **NPCs through doors.** The NPC mover (navigation/NPC design) dispatches `OperatePieceDoor(open: true)` when its next path step crosses a closed permitted door within reach. M7 NPCs never close doors behind them; that is the smallest behaviour. Doors a player left closed stay closed until an agent needs to pass.
- **Presentation.** A hinged leaf swings ±90° toward the door's local +Z side, the `HollowView` door pattern (`HollowView.cs:219-257`), keyed by piece ID instead of the wall-prefix heuristic. The camera collider rotates with the leaf.
- **Locks and keys:** none in M7.

---

## 12. Collision integration

### 12.1 One current walk space

Pieces are structures with known heights, so they belong in the static set, where `Kinematics.Resolve` applies the jump tuck rule to low blockers (`Kinematics.cs:189-193`).

- `RuntimeState` gains `WalkSpace Space`, written only by the `Structures` owner. It is the authored `Setup.Layout.Space` with each intact piece's **solid** parts appended:
  - order: authored blockers in content order, then pieces by ID ascending, parts in definition order;
  - each part's `Blocker.Id` is `"{pieceId}#{partIndex}"`.
  - `Resolve` is order-dependent (`Kinematics.cs:186-206`), so this order is part of the determinism contract.
- The space is rebuilt on every piece place, dismantle or destroy. It is not rebuilt on a door toggle: doors live in `ClosedDoors()`.
- `SystemContext` gains `public WalkSpace Space => State.Space ?? Setup.Layout.Space;`.
- Every current read of `_context.Setup.Layout.Space` in the runtime switches to `_context.Space`. These are:
  - `Combat.cs:570`;
  - `Companions.cs:132, 372, 398, 519, 567, 598`;
  - `Creatures.cs:173, 588, 623, 762, 837, 893`;
  - `Social.cs:136`;
  - `Systems.cs:140, 160, 216`.
- **Prediction.** `PlayerController.Predict` switches `setup.Layout.Space` to `simulation.Space` (`src/Presentation/Player/PlayerController.cs:127`). Prediction and authority then read the same pieces, and the drawn body never pops through a new wall.
- Presentation's other `Setup.Layout.Space` reads are terrain-only or authored-prefix heuristics (`SoundEvents.cs:65, 448`; `ItemsView.cs:62`; `Main.cs:530, 709`). They may stay.

### 12.2 Sight, blows, shots, spawns

- Solid piece parts join the static set, so `Walled`, perception's wall list and `Trace` stop at them automatically.
- The chest (700 mm) blocks sight and shots although a jump clears it. That is the same height-blind inconsistency the fallen timber and the fence already have (spatial_movement §12.2 item 7), recorded, not fixed.
- Creature baseline placement is unaffected. `CreatureSystem.Populate` samples only inside each spawner's disc (`Creatures.cs:171-195`), and check 11 keeps every piece outside every spawner disc expanded by creature radius + 1 m. Adding pieces to the space therefore never changes where a baseline creature is placed.

---

## 13. Materials and resources

### 13.1 The decision

**Placement, repair and the anvil consume items through `ExchangeItems`.** There is no construction currency, upkeep, build points or construction progress: placement is instant, as M3f crafting is.

Existing materials cannot supply building:
- the ash stand gives one haft a world day (`content/nodes/wood/ash_stand.yaml`);
- the iron seam is finite, three strikes (`content/nodes/ore/iron_seam.yaml`).

M7 therefore adds **one material and one renewable node through the unchanged M3f model**:

```yaml
# content/items/material/timber.yaml
id: item.material.timber
kind: item
schema: 1
display_key: item.material.timber.name
tags: [item]
name: Rough Timber
notes: Split deadfall - what M7's pieces are built from (content/pieces). Worth nothing to a trader.
category: material
stack_max: 20
weight: 0.5              # 20 carried = 10 kg of the 30 kg base limit (config.inventory)
value_base: 2            # sells for floor(2 x 0.4) = 0 (config.economy), so timber is never a coin source (GAMEPLAY_LOOPS E-10)
rarity: common

# content/resources/wood/deadfall.yaml
id: resource.wood.deadfall
kind: resource
schema: 1
display_key: resource.wood.deadfall.name
tags: [resource]
name: Deadfall
yields:
  - { item_ref: item.material.timber, count_range: [3, 4] }

# content/nodes/wood/deadfall.yaml
id: node.wood.deadfall
kind: node
schema: 1
display_key: node.wood.deadfall.name
tags: [node]
name: Deadfall
resource_ref: resource.wood.deadfall
charges: 8
respawn: daily
harvest_skill: skill.survival
difficulty: 2
```

The region gains `{ name: deadfall, node_ref: node.wood.deadfall, position_m: [84, 118] }` under `nodes:` (`ashen_hollow.yaml:157-159`). That point:
- is in cell A (`r_0_0:c_00_01`);
- is 5 m from the build area's north-west corner (87, 114);
- is 12.6 m from Sel's stand (72, 122);
- lies on the terrace at 7.15 m.

A node is a point, not a blocker.

Yield:
- 8 harvests × 3-4 = 24-32 timber per world day (57 600 ticks; `content/config/time.yaml:10`);
- plus the survival bonus from level 3 (`Crafting.cs:117-121`).

The Crossing Workshop (section 22) costs 29 timber and 2 billets, so it is about one world day of gathering.

### 13.2 The persistence consequence

A new fixed node changes the generation profile. Fixed nodes enter the generator in `GameSession.Boot` (`src/Application/GameSession.cs:102-105`), and `CellBaseline.Digest` covers nodes (`src/World/Generation.cs:144-162`). So the worldgen fingerprint and cell A's `baseline_hash` change. M7 therefore:

- freezes the e10d2c4 running fingerprint as `private const string M6LayoutFingerprint = "sha256:…"`, next to `M3LayoutFingerprint` (`GameSession.cs:77`). The implementer reads it from any v13 save written by the e10d2c4 build (`manifest.json` → `worldgen_fingerprint`) or by booting e10d2c4, and records the value in `docs/M7_STATUS.md`;
- registers `new BaselineTransition("M7: the deadfall by the crossing", M6LayoutFingerprint, generator.Fingerprint)` with `DropVanishedTargets: false`. Nothing vanished: M7 only adds a node. `SemanticRebase` carries node, flag, container, created and creature records as it already does (`src/Persistence/BaselineTransitions.cs:94-117`);
- keeps the existing M3f and M6 transitions, which target the running fingerprint and so keep working (`GameSession.cs:108-117`);
- adds a test: an M6-era save that has records in cell A loads, with `CellsRebased` naming the M7 transition.

Fallback if the persistence lead refuses any M7 baseline change: replace the node with an authored container `container.timber_stack` at the same point. Containers are layout, not baseline (`src/Domain/Spatial/RegionLayout.cs:69-70`). Give it a loot table of 80 guaranteed timber. It is one-shot, needs no transition, and its limit is recorded as an M7 limitation.

### 13.3 Transactions

| Operation | Items | Path |
|---|---|---|
| Place | Spend `cost` (7.2 check 14 plan) | `ExchangeItems(takes, null, 0, 0)` dispatched **before** any building state changes |
| Repair | Spend ⌈count × missing × repair_cost_percent / (100 × health_max)⌉ per line, rounded up in integers: (a·b + c − 1) / c | `ExchangeItems`, before the health change |
| Dismantle | Grant ⌊count × refund_percent / 100⌋ per line, if > 0 | `GrantItem(itemId, n, Quality.Standard)` after removal. It never refuses: pack, else ground at the body (`Items.cs:367-374`) |
| Destroy | Nothing refunded | - |

### 13.4 `config.building` (`content/config/building.yaml`)

```yaml
id: config.building
kind: config
schema: 1
display_key: config.building.name
tags: [config]
notes: Building v1 (M7). Placement is socket/snap on a 3 m lattice, quarter turns, one storey (D-08; owner rulings 1-2).
module_m: 3.0                # the lattice; save-locked (pieces store absolute anchors)
rotation_step_deg: 90        # quarter turns only in M7 (owner question: 45-degree steps need oriented footprints)
place_reach_m: 6.0           # body centre to the nearest point of the piece's bounds
pad_max_relief_m: 0.25       # highest minus lowest ground under a pad, sampled every 0.25 m
refund_percent: 50           # of each cost line, rounded down, when a piece is taken down
repair_cost_percent: 100     # of each cost line, scaled by the health missing, rounded up
navigability_radius_m: 0.35  # the reference body for navigability (NPCs and companions, config.base_speeds)
protection:                  # ground placement keeps clear of (7.2 check 11)
  spawn_point_m: 3.0
  npc_site_m: 1.5
  site_m: 1.5                # authored containers, stations and nodes
  door_approach_m: 1.5       # round every authored door's closed footprint
  spawner_margin_m: 1.0      # beyond a spawner's disc (and each patrol leg) plus its largest creature's radius
damage:                      # the only sources of piece damage in M7 - explicit rules, never physics (D-08)
  melee: 10                  # a player's blow that strikes no creature and whose reach meets a piece first
  shot: 2                    # a player's arrow that stops on a piece
  formula: 12                # a player's projectile formula (the impulse bolt) that stops on a piece
```

`BuildingConstants` validates these values (`Problem()`, the `CompanionTuning` pattern at `src/Domain/Companions/Companions.cs:64-71`):
- module 3000;
- step 90;
- reach 1000-12 000;
- relief 0-1000;
- refund 0-100;
- repair 0-1000;
- radius equal to `base_speeds.body_radius_m`;
- protection values ≥ 0;
- damage values ≥ 1.

---

## 14. Damage, repair, dismantle, destruction

### 14.1 The explicit damage-rule table (M7)

| Source key | Producer (the one code point) | Rule | Amount |
|---|---|---|---|
| `melee` | `CombatSystem` melee branch, at the active window's last tick, when `struck.IsEmpty` (`Combat.cs:495-507`) | Take the facing segment from the body centre, of length `attack.ReachMm`. Of all blockers it crosses (the current space plus `ClosedDoors()`), take the one with the smallest `DistanceTo(body)`; ties go authored first, then piece ID. If that blocker is a piece part, dispatch `DamagePiece(pieceId, 10, "melee")` and publish no `AttackMissed`. | 10 |
| `shot` | `CombatSystem.Loose` (`Combat.cs:511-517`), with a new `DamageKind` parameter: ranged weapons pass `shot` | When `Trace` returns no target and stops short of range, the piece part whose `DistanceTo(stop point)` ≤ 20 mm (bisection resolves to 10 mm, `Combat.cs:534-542`) is damaged. Nearest wins; ties go to the lower piece ID. | 2 |
| `formula` | the same `Loose`, called from `Magic.cs:114` with `formula` | as `shot` | 12 |

These are the only M7 damage sources. There are no creature, weather, fire, raid or time-based sources.
- Off-screen and tier-B/C damage is illegal anyway (review C-8, `docs/reviews/ARCHITECTURE_AI_SESSION_REVIEW.md:217-239`).
- A boar charge that ends against a piece still stuns the boar through the existing "ran into something solid" rule (`Creatures.cs:589-594`), but does not damage the piece. The boar's territory (18, 72) is 85 m from the build area, so that row would be unreachable in play.

Reserved future rows, not built: `creature_charge`, `creature_blow`, `fire`, `raid`.

Ownership does not affect damage: the player can damage any piece. Pads and roofs are unreachable by these sources because they emit no blocker.

### 14.2 `DamagePiece` (internal command, to `BuildingSystem`)

```
string? Handle(DamagePiece d):            // record DamagePiece(EntityId PieceId, int Amount, string Source) : InternalCommand
    piece = Find(d.PieceId) ?? return "there is no such piece"
    if d.Amount < 1 return "no damage"
    int to = max(0, piece.HealthCurrent - d.Amount)
    if to == 0 { Destroy(piece, d.Source); return null }
    State.SetPiece(_owner, piece with { HealthCurrent = to })
    Publish(new PieceDamaged(piece.Id, piece.DefId, d.Amount, to, d.Source, Now))
```

Damage changes no geometry, so it causes no revision bump and no navigation invalidation.

### 14.3 Repair

`RepairPieceCommand(EntityId Actor, EntityId PieceId)`. Refusals, in order:
1. unknown actor, "dead", or "busy";
2. "there is no such piece";
3. not the owner: "that is not yours to mend";
4. reach, as 7.2 check 6;
5. `HealthCurrent == health_max`: "it needs no repair";
6. materials, as check 14, over the scaled cost.

On success: dispatch `ExchangeItems`; set `HealthCurrent = health_max`; publish `PieceRepaired(PieceId, From, To, Tick)`. Repair always restores to full: one command, one cost, no partial steps.

Example: a wall at 170/200 needs ⌈2 × 30 / 200⌉ = 1 timber.

### 14.4 Dismantle

`DismantlePieceCommand(EntityId Actor, EntityId PieceId)`. Refusals, in order:
1. actor, "dead", "busy";
2. exists;
3. owner: "that is not yours to take down";
4. reach;
5. dependents (below): "take the door down first" / "take down what stands on it first";
6. a storage piece whose container record holds any item: "empty the chest first".

Dependents are recomputed from the socket index:
- a **doorway**'s dependent is its door;
- a **pad**'s dependents are the furniture on its square, and every wall-type piece on its four edges whose **other** adjacent square has no intact pad;
- walls, doors, roofs, chests and anvils have no dependents;
- roofs are never dependents, because support is a placement-time rule only (invariant 4).

Success, through `RemoveCore(piece, StructureChangeKind.Dismantled)`:
- a station with a worker: clear `WorkerNpcId`, publish `WorkerReleased(npc, piece, "dismantled")`;
- a storage piece with a materialised (empty) record: dispatch the existing `DiscardContainer(key)` (`Items.cs:376-381`), which retires the `cnt_` identity;
- `StructureSequence += 1`; `State.RemovePiece(_owner, id, seq)`, which also retires the ID with the registry;
- rebuild the space; dispatch `NavigationInvalidated(bounds, Dismantled, seq)`;
- publish `PieceRemoved(PieceId, DefId, Actor, ImmutableArray<CostLine> Refund, Revision, Tick)` and `StructuresChanged`.

Then refund through `GrantItem` per line. Dismantle never needs a navigability check: removing blockers cannot disconnect anything.

### 14.5 Destruction (health reaches 0)

This is the **same removal path** as dismantle (`RemoveCore`), with three differences:
1. No refund, and no owner or reach check: the rule, not a player, destroys.
2. **Mount cascade.** Every intact piece whose **mount** would lose its last provider is removed first, recursively, in piece-ID order, each with `StructureChangeKind.Destroyed` and its own `PieceDestroyed` event. With M7 sources the only reachable case is a destroyed doorway taking its door, "the door falls with its frame". This follows socket dependency, not physics. No roof ever cascades, and nothing is decided by load or span.
3. **Chest spill instead of refusal.** A destroyed storage piece dispatches the new internal command `SpillContainer(string Key)` to `InventorySystem` before removal. It:
   - moves every `ContainerItem` to the ground at the container site point, one created stack each, **keeping its item ID**, through `State.PlaceItem` at cell-relative cm (the drop pattern, `Items.cs:584-593`);
   - removes the container record, retiring only the `cnt_` identity.

   `RemoveContainer` alone would destroy the items' identities (`WorldDelta.cs:390-400`), which is item loss. That is why a new command is needed.

It publishes `PieceDestroyed(PieceId, DefId, Source, Revision, Tick)`. A destroyed station's worker is released with reason "destroyed".

---

## 15. The storage chest and the crafting station

### 15.1 Storage chest: the container model reused

- **Site.** Each intact storage piece yields a synthesized `ContainerSite`:
  - Key: `"container." + pieceId.Value.ToLowerInvariant()`, for example `container.pce_0000000019…`. This is a valid definition-ID shape: the second segment holds `_` (`src/Domain/DefinitionId.cs:19-20`). `Materialize` needs that, because it calls `DefinitionId.Parse(site.Key)` (`Items.cs:518`).
  - LootTableId: `""`.
  - X, Z: the world position of `container.at_m`.
  - StackSlots: 12.
  - A new init property on `ContainerSite`: `EntityId? InstanceId`, the derived chest `cnt_` ID (6.3).
- `SystemContext.FindContainer` (`Systems.cs:51-52`) checks authored, then corpse, then merchant, then **piece** sites. `Simulation.Containers` (`Simulation.cs:214`) appends piece chests, so the existing inventory panel works unchanged.
- `InventorySystem.Baseline(site)` (`Items.cs:494-510`) returns **empty** when `site.LootTableId == ""` and the site is not a merchant. Today it would throw on the loot lookup.
- `InventorySystem.Materialize` (`Items.cs:513-523`) uses `site.InstanceId` when present, `registry.CreateEntity(DefinitionId.Parse(site.Key), id)`, instead of minting a random `NewId`.
- `InventorySystem.Check` (`Items.cs:450-460`) additionally refuses a piece chest whose owner ≠ actor, with "that chest is not yours".
- **Persistence** is the existing schema-6 `ContainerRecord`, keyed by the piece key, with host cell = the piece's host cell (`WorldDelta.cs:76`). Nothing new is needed for contents.

### 15.2 Crafting station: the M3f station kind reused

- **Kind `anvil`.** It is the kind of the existing March Spear recipe (`content/recipes/smithing/march_spear.yaml:8`). A player bench lets a character who knows the recipe make spears at home from a billet and a haft. The billet still needs the smithy's forge.
  - No new recipe.
  - The bench's own cost (2 billets) is the only new item sink in M7.
- **Site.** Each intact station piece yields `StationSite(Key: "station." + pieceId.Value.ToLowerInvariant(), Kind: "anvil", XMm/ZMm: the part's centre)`, reusing `RegionLayout.cs:46`.
- `SystemContext.Stations()` returns authored sites, then piece sites by ID. `CraftingSystem` replaces `_context.Setup.Layout.Stations` with `_context.Stations()` at `Crafting.cs:168-170`. `Simulation.Stations` feeds presentation, whose `Main.cs:1005` currently reads the static list.
- A destroyed or dismantled bench leaves no site: the next craft is refused "no anvil in reach", the existing text.

---

## 16. The NPC work-anchor assignment (scope item 5)

### 16.1 Data

- **The piece.** The station definition's `work_anchor_m` and `work_facing_deg` give the anchor pose: world = anchor + R_r(local), facing = (local + r·90°) mod 360°. The assignment is the row's `WorkerNpcId`, an `npc.*` definition ID. That follows the Phase-1 norm of referencing named NPCs by definition ID, which the definition-ID pass covers (persistence research §5).
- **The NPC.** `NpcDefinition` gains `ImmutableArray<string> WorksAt`, the station kinds this NPC can work.
  - It is authored as `works_at: [anvil, forge]` on `content/npcs/ashen_hollow/kera_voss.yaml` only.
  - `SocialContent` parses it and lints each kind with the BLD005 rule.
  - Today unknown NPC keys are silently ignored (`src/Content/SocialContent.cs:85-89`), so the parse must be added explicitly.
- **Invariants.** At most one worker per station, and at most one station per NPC. A companion cannot be assigned.

### 16.2 Commands

```csharp
public sealed record AssignWorkerCommand(EntityId Actor, string NpcId, EntityId PieceId) : GameCommand(Actor);
public sealed record ReleaseWorkerCommand(EntityId Actor, string NpcId) : GameCommand(Actor);
public sealed record WorkerAssigned(string NpcId, EntityId PieceId, long AnchorXMm, long AnchorZMm, int FacingMdeg, long Tick);
public sealed record WorkerReleased(string NpcId, EntityId PieceId, string Reason, long Tick);
```

Assign refusals, in order:
1. actor; "dead";
2. the NPC is present in `State.Npcs`: "there is no one called X here";
3. not a companion;
4. `WorksAt` contains the station's kind: "Kera Voss does not work an anvil";
5. the player's body is within `TalkReachMm` (1950; `Systems.cs:67`) of the NPC's body: "Kera Voss is out of reach". You ask in person, which keeps information local (ruling 3);
6. the piece exists, is intact, is a station and is owned by the actor;
7. the station has no worker;
8. the NPC has no other assignment;
9. **reachability:** `INavigability.Connected(npcBody, anchorPose, 350, doorsOpenable)` holds: "Kera Voss cannot get there".

On success: set `WorkerNpcId`; publish `WorkerAssigned`. There is no geometry change, so no revision bump.

Release: the actor; the NPC is assigned to a piece the actor owns; talk reach. It clears `WorkerNpcId` and publishes `WorkerReleased(…, "released")`. Dismantle and destruction release too (14.4, 14.5). On load, a worker whose NPC definition was discarded by the alias pass is cleared, and that is reported as loss (20.4).

### 16.3 What building offers and needs (cross-system)

- **Offers.**
  - `Simulation.WorkAssignments` and `SystemContext.WorkAssignments()`: a sorted list of `(NpcId, PieceId, AnchorXMm, AnchorZMm, FacingMdeg)`.
  - Door permission for workers (section 11).
  - Assigned anchors and workers' bodies are navigability reference points (section 18).
- **Needs** (NPC and navigation designs). The system that owns NPC bodies reads the assignments each tick and does four things:
  1. Walks an assigned NPC to the anchor, emitting `MoveIntent`s into `Kinematics.Step`, never positions.
  2. Holds the NPC at the anchor pose. "At work" means within 300 mm of the anchor point, turned to the facing.
  3. Walks an unassigned NPC who is away from their `NpcSite` home.
  4. Opens permitted doors on the way.
- **Needs.** Positions and route state of moving NPCs must be persisted, or be a pure function of persisted state, so that save-then-continue equals continue. Non-companion NPC bodies are transient today (`src/World/Runtime/RuntimeState.cs:60-61`; `Social.cs:98-104`).
- **Needs.** Kera's commute leaves the smithy through the authored door `door.forge_shed` (`ashen_hollow.yaml:144`), whose flag defaults to closed. Either NPCs may operate authored doors in their own settlement (a navigation/NPC design item), or the acceptance script opens it first. Section 22 does the latter so that the building proof does not depend on the former.
- Kera stays a trader while working. `TradeSystem.Trader` measures reach to her **body** (`Social.cs:535-550`), and the wares container is not reach-checked in a trade (`Items.cs:459`). Talking also measures to her body (`Social.cs:237`).
- There is no production, schedule, wage, housing or hireling in M7. "Work" is standing at the bench.

---

## 17. The build area in Ashen Hollow

Region-inline data in `content/regions/ashen_hollow.yaml`, after `stations:` (`:161-163`):

```yaml
# Where the player may build (M7): boxes on the 3 m building lattice, clear of authored geometry, spawns and NPC stands.
# The crossing sits on the four-cell corner (100, 100), so a structure there straddles both seams (ROADMAP M7 exit).
build_areas:
  - { key: build_area.hollow_crossing, box_m: [87, 87, 114, 114], max_pieces: 256 }
```

- **Lattice fit.** 87 = 29 × 3 and 114 = 38 × 3. That gives 9 × 9 = 81 squares (i, k = 29..37), with square (33, 33) = x 99-102, z 99-102 containing (100, 100).
- **Domain.** `RegionLayout` gains `ImmutableArray<BuildAreaSite> BuildAreas` with `record BuildAreaSite(string Key, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, int MaxPieces)`. `WorldContent` parses and lints it (WLD015). Build areas are layout, not baseline, so they need no transition.
- **Cells.** The area covers parts of all four cells: A `c_00_01` (x < 100, z ≥ 100), B `c_01_01`, C `c_00_00`, D `c_01_00` (`ashen_hollow.yaml:8`; cell letters per `docs/M6_STATUS.md:14-19`).
- **Terrain.** Heights 5968-6930 mm. The worst square has 228 mm of relief (10.4).
- **Clear of everything protected** (7.2 check 11), measured to the box [87, 114]²:
  - Authored structures: the nearest are `tree_25` (112, 86), r 0.4, 0.6 m south of the box edge, and the survey table ending at x 75.2 (`ashen_hollow.yaml:83, 133`). **None inside.**
  - NPC stands: Sel (72, 122) is 17 m away; Renn, Kera and Tavar are further.
  - Spawners: the strays at (118, 128) have a disc of 6 m, protection 6 + 0.45 + 1 = 7.45 m, and the nearest box corner (114, 114) is 14.6 m away (`content/spawns/hollow/valley_strays.yaml`). Their 8 m wander disc (`content/config/creature_behaviour.yaml:27`) also stays 6.6 m outside. The hound (128, 150) and every other spawner and patrol route are further.
  - Containers, stations, nodes: the nearest is the new deadfall at (84, 118), 5.0 m from the corner (87, 114), beyond its 1.5 m protection.
  - Authored doors, the spawn point (30, 158) and every authored container are far away.
- **Scripted routes.** `Playthrough`, `PerfRun` and `UiShots` walk through (100, 100) and (100-105, 108-112) (`src/Presentation/Playthrough.cs:56, 63, 68`; `src/Presentation/Perf/PerfRun.cs:26`). Those runs start new games with no pieces, so they are unaffected. The M7 build beats run after them, or in their own mode.
- **Why here and not elsewhere.**
  - It is the one place near the waystation (35 m from Sel's table, about 60 m from the smithy) where a single lattice square contains both seams. That delivers RK-A2's "test with a building straddling four cells" (`docs/WORLD_ARCHITECTURE.md:469`) with a single pad.
  - Its worst square rises 228 mm, so every square in it takes a pad.
  - It needs no quest gate.
  - Widening it later, or adding areas, is data.

---

## 18. Navigability validation

### 18.1 What is checked

For a candidate placement that adds solid parts, and on the navigation representation of the **candidate** state (authored space + current pieces + the candidate):

> Every reference point that is connected to the region anchor now must remain connected after the placement, and every reference point the placement itself creates must be connected after it - for a body of radius `navigability_radius_m` (350 mm), with every door (authored and piece) treated as passable and every barrier as it stands now. Bodies are ignored.

Why each clause:
- **Doors are treated as passable** because every agent that must path through the player's structures (the owner, the companions, the workers) can open them (section 11). Placing a door never fails navigability.
- **Barriers are treated as they stand** because nothing but their switch can lift them (`RegionLayout.cs:23-28`).
- **Bodies are ignored** because they move.
- **"Remain connected" is relative to now.** A point that is already unreachable does not block building elsewhere. Tavar's site inside the standing fold (`ashen_hollow.yaml:155, 197`) is the live example.

### 18.2 Reference points

| Class | Points | Satisfied when |
|---|---|---|
| Anchor | the region spawn, (30, 158) (`ashen_hollow.yaml:12`) | - (the root) |
| Stand | every authored NPC site; every assigned work anchor; the current bodies of the player, every NPC including companions, and every living creature | the exact point's navigation cell is connected |
| Reach(1600) | every authored container, station and node point; each side of every authored door (the closed box's centre offset ±(half-thickness + 400) along its thin axis); every piece chest's site point; every piece station's site point | some connected navigable position lies within 1600 mm of the point (the interact and inventory reach) |
| Doorway (Reach 1600) | each intact piece door or doorway: at least **one** side | as Reach |
| New points | the chest or station being placed (its Reach point); the anchor of a station being placed has no worker yet, so it is not required | must be connected after |

### 18.3 The query building needs from navigation

```csharp
// implemented by the navigation design; building never sees cells, tiles or graphs
internal interface INavigability
{
    /// Which of these points lose connection to the anchor if these footprints are added (and none removed), for a body of
    /// this radius, doors passable, barriers as they stand. Deterministic; ordered like the input.
    ImmutableArray<int> Disconnected(ImmutableArray<NavFootprint> added, BoundsMm changed,
        ImmutableArray<ReferencePoint> points, long radiusMm);
    bool Connected(long fromXMm, long fromZMm, long toXMm, long toZMm, long radiusMm);
}
public sealed record ReferencePoint(string Label, long XMm, long ZMm, long WithinMm);   // WithinMm 0 = Stand
```

`Label` is the refusal wording. The first disconnected point, in input order, names the reason. Input order: new points, work anchors, bodies, NPC sites, then authored sites, piece sites and doors, each group by key or ID.

### 18.4 Recommended evaluation (for the navigation design to adopt or improve)

- **Fast path.** If every free navigation cell bordering the newly blocked cells can reach every other such bordering cell without leaving a bounded window (the change's bounds expanded by 30 m), nothing can have been cut off. That proof is in the panel notes: any path that used the blocked cells can be rerouted between two bordering cells. Accept without touching any reference point. Almost every wall placement takes this path.
- **Slow path.** Otherwise, label connected components over the whole region grid once and test each reference point.
  - Budget: ≤ 20 ms worst case on the 200 × 200 m region.
  - A placement is a rare player event, and the command runs at a tick boundary.
  - Measured by a test (section 23).
- **The preview** runs this only when the pose or the revision changes (section 9).

### 18.5 Examples in Ashen Hollow

These are refused:
- sealing the doorway side of the workshop with a vestibule (section 22, step 7);
- walling in the companion who stands inside a room;
- enclosing an assigned worker's anchor.

These are accepted:
- a U-shaped wall;
- a wall across the road at (100, 110) that leaves a way round;
- a sealed room that contains no reference point, which is a harmless solid block.

---

## 19. What building offers navigation (the contract)

### 19.1 Footprints

```csharp
public enum TraversalClass { Solid, Door, NonBlocking }
public sealed record NavFootprint(EntityId PieceId, int Part, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm,
    long HeightMm, TraversalClass Class, bool DoorOpen, EntityId Owner);
// SystemContext / Simulation read surface:
ImmutableArray<NavFootprint> StructureFootprints { get; }   // sorted by (PieceId, Part); rebuilt with the space
long StructureRevision { get; }                              // = World.StructureSequence (persisted)
```

- `Solid`: walls, doorway jambs, chests, anvils, in world mm after rotation. Their `HeightMm` is informative only. Every M7 navigating agent treats a solid as impassable, and nothing is jumped or crouched under.
- `Door`: the door leaf's closed box, with `DoorOpen`. Permission is evaluated per agent (section 11).
- `NonBlocking`: pads' squares. They may be used as an "indoors / floor" hint and never block. Roofs are not emitted.
- Authored geometry is **not** in this list. Navigation reads the authored `WalkSpace`, `DoorSite`s and `BarrierSite`s as today.

### 19.2 Invalidation

```csharp
internal sealed record NavigationInvalidated(BoundsMm Changed, StructureChangeKind Kind, long Revision) : InternalCommand;
```

- Dispatched synchronously by `BuildingSystem`, after the mutation and before its events, on every place, dismantle, destroy, door open and door close. It goes to whichever system the navigation design makes the owner of the derived navigation state. This is the internal-command pattern: systems never subscribe to events (`src/World/Runtime/Events.cs:10-11`; `Simulation.cs:369-404`).
- `Changed` is the union of the added or removed parts' boxes (door: the leaf box), in world mm and **not inflated**. Navigation inflates by its own largest agent radius.
- `Revision` is the new `StructureSequence`. It strictly increases and is persisted, so a derived cache keyed by revision is valid across save and load.
- The public event `StructuresChanged` carries the same rectangle and revision for views and tests.
- **At load, no invalidation is sent.** Navigation builds from full state in the `Simulation` constructor, after `BuildingSystem.Populate()` and before `_npcs.Populate()` (20.6).
- **Seams.** Footprints are world mm, and a piece straddling cells is one footprint. Nothing in the contract is per-cell, so seams are a non-case for building. The movement space already ignores cells (spatial_movement §6).

---

## 20. Persistence (schema 14, shared with the rest of M7)

### 20.1 Where

Pieces go in a new `pieces` list, and `structure_seq` as a scalar, inside **`entities.msgpack`**. This follows the schema-6 (`containers`) and schema-8 (`creatures`) precedent.
- A new `buildings.msgpack` would break every historical fixture's integrity root. `SaveIntegrity.TryReadRoot` requires every `CheckedFiles` name (`src/Persistence/SaveLoader.cs:469`; `src/Persistence/SaveModel.cs:31-32`), and a re-derived root fails `IsComplete` and `Migrate` (persistence research §0.2).
- Keeping chest contents (`containers`) and chest pieces in the same section also means one quarantine never leaves a cross-section dangling reference (`docs/PERSISTENCE.md:252`).
- PERSISTENCE §3.2, §3.3 and §5.4 are reconciled to say so (section 25).

### 20.2 DTO (`src/Persistence/SectionCodec.cs`)

```csharp
[MessagePackObject] public sealed class PieceDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("rotation")] public int Rotation { get; set; }
    [Key("owner")] public string Owner { get; set; } = "";
    [Key("health")] public int Health { get; set; }
    [Key("door_open")] public bool DoorOpen { get; set; }
    [Key("worker_npc_id")] public string? WorkerNpcId { get; set; }
}
// EntitiesSectionDto (SectionCodec.cs:202-218) gains:
[Key("pieces")] public PieceDto[]? Pieces { get; set; }          // Required from schema 14. The 13 -> 14 step gives older saves none.
[Key("structure_seq")] public long? StructureSeq { get; set; }    // Required from schema 14. The 13 -> 14 step gives older saves 0.
```

- On decode, a null `pieces` or `structure_seq` is `FormatException("… (required from schema 14)")`: corrupt, not defaulted (the posture pattern, `SectionCodec.cs:399-416`).
- Ranges are validated on decode: rotation 0..3, health ≥ 1, `structure_seq` ≥ 0.
- `EncodeEntities`' `Prove` loop (`SectionCodec.cs:478-494`) adds each piece's host cell.
- Rows are sorted by `instance_id` ordinal. Byte-stable.

Size: about 220 bytes per piece with string keys, so the 256-piece cap is about 56 KB. That is inside PERSISTENCE's 10 KB-5 MB building budget (`docs/PERSISTENCE.md:553`).

### 20.3 World delta (`src/World/WorldDelta.cs`)

Store: `SortedDictionary<EntityId, PieceRecord>` (ordinal by `Value`) and `long _structureSequence`.

| Addition | Detail |
|---|---|
| `DeltaSnapshot` | init properties `ImmutableArray<PieceRecord> Pieces` (sorted by ID) and `long StructureSequence` (`WorldDelta.cs:188-200`) |
| internal mutators | `PlacePiece(record, seq)` (registers the ID, sets seq), `SetPiece(record)`, `RemovePiece(id, seq)` (retires the ID), `SetStructureSequence(seq)`. Reached only through `RuntimeState` wrappers that `Require(owner, StateSlice.Structures)` (`src/World/Runtime/RuntimeState.cs:329-333`) |
| public readers | `Piece(EntityId)`, `PiecesIn(CellKey)`, `Pieces` (all, sorted), `StructureSequence`. Added to the allow-list in `WorldDelta_ExposesNoPublicMutation` (`tests/Architecture.Tests/ArchitectureTests.cs:61-79`) |
| `TakeSnapshot` | stamps each row with `Baseline(host).Digest`, like created rows (`WorldDelta.cs:505-508`). A piece never equals a baseline; its retirement path (I-7) is dismantle or destroy. Piece host cells do not make a cell record, and `dirty_reasons` is unchanged (`WorldDelta.cs:489-494`) |
| `FromSnapshot` | new order: cells, entities, created, **pieces**, containers, creatures. `TryApplyPiece`: parseable host cell; hash equals the host's regenerated baseline; kind `Piece`; valid `DefId`; owner kind `Character`; rotation 0..3; health ≥ 1; `HostCell == CellKey.OfWorld(X/1000.0, Z/1000.0)`; ID timestamp (= its seq) ≤ `StructureSequence`; not seen twice; not already registered. Then register. `TryApplyContainer` additionally rejects a `container.pce_*` key whose piece is absent: "its chest is gone" (a `RejectedRecord`, reported) |
| `EffectiveCellDigest` | per host cell, pieces sorted by ID: id, def, x, z, rotation, owner, health, door_open, worker ?? "-". Tag `unnamed.effective-cell/v1` → **v2** (`WorldDelta.cs:587`) |

`Simulation.StateDigest` adds `World.StructureSequence`. Tag `unnamed.simulation/v1` → **v2** (`Simulation.cs:360`). `StateDump.Render` picks up both new `DeltaSnapshot` properties automatically (`src/Application/StateDump.cs:25-46`).

### 20.4 Load pipeline (`src/Persistence`)

- **`SaveLoader.ResolveDefinitions`** (`SaveLoader.cs:206-359`):
  - each piece's `DefId` resolves. Discarded means the piece is dropped, with loss "piece {id}: '{def}' was removed with no replacement; dropped";
  - each `WorkerNpcId` resolves. Discarded means the assignment is cleared, with loss;
  - the rebuilt `DeltaSnapshot` at `SaveLoader.cs:356-358` **must** copy `Pieces` and `StructureSequence`. This is the known hand-listing pitfall (persistence research §0.5);
  - container keys are not resolved today and stay unresolved, correctly.
- **`ProveBaselines`** (`SaveLoader.cs:380-389`): `Check(piece.HostCell, piece.BaselineHash)`.
- **`SemanticRebase.Apply`** (`BaselineTransitions.cs:94-117`): pieces move "as they are" to the new baseline, like created instances. The returned snapshot copies them and `StructureSequence`.
- **Migration.** `SchemaV13ToV14` reads the frozen `V13.EntitiesSection` and writes the current DTO with `pieces = []` and `structure_seq = 0`.
  - `V13.EntitiesSection` is new: `Sections/SchemaV13.cs` holds the schema 9-13 entities shape.
  - `SchemaV8ToV9` (`src/Persistence/Migrations.cs:566`) is repointed to write it.
  - The step is shared with the faction and NPC additions. M7 makes one schema bump.

### 20.5 Fixtures

- `M2Fixtures.Historical.World` (`tests/M2.Probe/M2Fixtures.cs:112-248`) gains, in one of its ten cells, with `EntityId.Create`-built IDs:
  - a pad;
  - a wall at 150/200 health;
  - a doorway with an open door;
  - a chest piece whose container holds 3 items;
  - `structure_seq = 9`.
- The writer pack `Fixtures/content-0.1.7/` adds the piece definitions and `item.material.timber`.
- The current pack renames one piece through `_aliases.yaml` (`piece.fixture.old_wall: piece.fixture.wall`). That proves "the rename must reach the piece record".
- `CanonicalState.Render` (`tests/Persistence.Tests/CanonicalState.cs`) gains pieces and `structure_seq`.
- Every older `expected.json` gains only `"pieces": []`, `"structure_seq": 0`, reviewed line by line (`tests/Persistence.Tests/Fixtures/README.md:12-24`).

### 20.6 Where derived building state is rebuilt

In the `Simulation` constructor (`Simulation.cs:100-145`), the single rebuild point for new-game and load, the building system is claimed and populated in this order:

```
_building = new BuildingSystem(_context, _state.Claim(nameof(BuildingSystem), StateSlice.Structures), player.Id);   // after _crafting
... _state.RequireEverySliceOwned(); _effects.Seed(...);
_building.Populate();      // current WalkSpace, socket index, footprints, health clamp + StructureAudit
/* navigation build (its design) */
_npcs.Populate(); _companions.Populate(); _creatures.Populate(); _tiers.Settle();
```

`StructureAudit` is `ImmutableArray<StructureConflict(EntityId PieceId, string Problem)>`. It lists loaded pieces that are now:
- off-lattice;
- outside every build area;
- overlapping authored geometry or protected ground;
- clamped in health.

The pieces are **kept**: player work is never silently discarded. The authored layout is not in any `baseline_hash` (`src/World/Generation.cs:144-162`), so a later layout edit can only be audited, not proven. That is an open issue. `Populate` publishes nothing, because loading publishes nothing (`tests/Application.Tests/DeterminismAndViewTests.cs:128-143`).

---

## 21. Presentation (greybox; enough to prove M7)

- **`StructuresView`** (`src/Presentation/Greybox/StructuresView.cs`, new):
  - builds from `Simulation.Pieces` in `Resync()` (`src/Presentation/Main.cs:915-928`);
  - adds and removes nodes incrementally on `PiecePlaced`, `PieceRemoved` and `PieceDestroyed`;
  - swings doors on `DoorToggled` where `DoorKey` parses as a piece;
  - uses `Solid()` boxes with camera colliders on layer 1 for walls, jambs, doors, chests, benches and roofs (`HollowView.cs:374-381`);
  - draws pads as the draped slab with no collider;
  - draws roofs as a 200 mm slab at the square centre's terrain height + 3000 mm, with its collider (so the camera compresses indoors);
  - uses `Palette.Wood`, `Roof` and `Door`.
- **Ghost.** Translucent green or red materials, modelled on `Palette.Fold` (`src/Presentation/Greybox/Palette.cs:42-50`). The refusal reason is shown under the reticle.
- **Keys.** Direct keys only; no radial (ruling 5). All are listed in F1's `HelpPanel.Sections` under "BUILDING".

  | Key | Where | Effect |
  |---|---|---|
  | B | no panel open | toggle build mode |
  | 1-7 | build mode | choose a piece (a catalogue panel also lists them with costs) |
  | R | build mode | rotate +90° |
  | LMB | build mode | place (attack is suppressed in build mode) |
  | Delete | build mode | dismantle the piece under the reticle |
  | T | build mode | repair the piece under the reticle |
  | Esc | build mode | leave build mode |
  | E | always | open or close doors |
  | Y | out of build mode, facing an NPC who works this station kind | "Ask Kera Voss to work at your anvil bench" / "Release Kera Voss from work". Presentation picks the nearest eligible owned station to the player, ties by piece ID |

- The free keys are verified in presentation research §2.2. In build mode, R's take-all, the cast keys 4-6 and LMB attack are gated off, exactly like the dialogue branch gates them (`Main.cs:374-386`).
- **Camera.** Build mode raises the spring arm's `MaxDistance` from 6 m to 9 m. That is "somewhat farther", bounded, not an RTS camera (`docs/CAMERA_PERSPECTIVE_AND_PRESENTATION.md:266-272`; `src/Presentation/Player/CameraRig.cs:23`). The simulation keeps running in build mode, since nothing pauses today.
- **Refusal toasts.** Add the five new commands to `Main.Subscribe`'s `CommandRejected` filters (`Main.cs:662-670`). Otherwise refusals never reach the player.
- **F2 building debug** (a free key):
  - piece IDs;
  - slot keys;
  - owner;
  - health;
  - sockets as small markers;
  - the revision;
  - `StructureAudit`;
  - the last `StructuresChanged` rectangle.

---

## 22. The M7 building acceptance scenario: "the Crossing Workshop"

This is a headless Application test (`tests/Application.Tests/BuildingAcceptanceTests.cs`) through `GameSession` and `Harness`. The same beats are recorded as a windowed `--build-shots <dir>` mode (the `DeltaShots` pattern; presentation research §9.8). The seed is fixed.

- **Start.** `Simulation.Start` with a `PlayerRecord` carrying 40 `item.material.timber`, 3 `item.material.iron_ingot` and 1 `item.material.ash_haft`, and knowing `recipe.smithing.march_spear`. The bench costs 2 billets; step 4's craft spends the third billet and the haft. The windowed run gathers at the deadfall instead.
- **Setup.** The player opens `door.forge_shed` first (the existing `InteractCommand`; see 16.3).

1. **Build (all accepted).** Anchors in mm. The anchor math is in section 3.4.
   - **Pads** on squares (33, 33), (34, 33), (33, 34), (34, 34): anchors (100500, 100500), (103500, 100500), (100500, 103500), (103500, 103500). Pad (33, 33) spans x 99-102, z 99-102 and **straddles all four cells**. Its host cell is `c_01_01`.
   - **Walls, south** (z = 99): (100500, 99000) r0; (103500, 99000) r0.
   - **Walls, north** (z = 105): (100500, 105000) r0; (103500, 105000) r0.
   - **Walls, west** (x = 99): (99000, 100500) r1; (99000, 103500) r1.
   - **Walls, east** (x = 105): (105000, 103500) r1.
   - **Doorway** at (105000, 100500) r1. Its opening runs z 99 700-101 300, so **the doorway straddles the z = 100 seam** (cells D and B). Door at the same anchor, r1.
   - **Roofs** on the four squares (support: each has two walled edges).
   - **Anvil bench** on square (33, 34), r3 (backs onto the west wall).
     - Part: x [99 500, 100 100], z [103 000, 104 000]. It **straddles x = 100**.
     - Site (99 800, 103 500).
     - Work anchor (100 750, 103 500), facing 270° (−X), toward the bench.
   - **Chest** on square (34, 34), r0.
     - Part: x [103 000, 104 000], z [104 100, 104 700].
     - Site (103 500, 104 400).
   - **Cost:** 4 + 14 + 2 + 1 + 4 + 2 + 2 = 29 timber, plus 2 billets. Assert the inventory decreased by exactly that.
   - **Sequence and IDs:** 19 placements give `StructureSequence` = 19 and IDs `Derived(Piece, 1..19, owner)`.
2. **Overlap refused.** A wall at the doorway's edge (105000, 100500) is refused by `Slot`: "a Timber Doorway already stands there". State is unchanged (digest equal).
3. **Collision.** The player walks from (107, 100.5) west into the doorway and is stopped by the closed door. The player opens it (E) and walks in. A shot aimed west from inside at the west wall stops at x ≈ 99.2 (`Aim`, `Simulation.cs:202-206`).
4. **Craft at home.** At the bench, `CraftCommand(march_spear)` with the carried billet and haft is accepted with no authored anvil in reach.
5. **Assign.** The player walks to Kera Voss (61.6, 139.6; `ashen_hollow.yaml:154`) and issues `AssignWorkerCommand(kera, anvilId)` within 1.95 m. Accepted; `WorkerAssigned`.
6. **In, through, around.** Within 1500 ticks (75 s; about 75 m of path takes 47 s at a walk of 1.6 m/s), Kera:
   - leaves the smithy by its door;
   - goes **around** the workshop, by its north or south side as the navigation design decides deterministically, because the doorway faces away from the smithy;
   - opens the closed piece door, which is permitted for a worker (`DoorToggled` with Actor = her NPC ID);
   - goes **through** the doorway, crossing x = 100 into cell B;
   - stands within 300 mm of (100 750, 103 500), facing 270° ± 1°.

   Assertions:
   - no `CompanionCaughtUp`-style teleport of any kind;
   - her path's cell sequence includes `c_00_01` → `c_01_01`;
   - the navigation's path for this query, taken as a digest the navigation design exposes, is recorded as P1.
7. **Navigability refused (the vestibule).**
   - Pad on square (35, 33): anchor (106500, 100500).
   - Walls at (106500, 99000) r0 and (106500, 102000) r0 are accepted.
   - A wall at (108000, 100500) r1 is **refused** by `Navigability`: "that would cut Kera Voss's work place off". The room plus vestibule would be sealed. Kera's anchor, her body and the chest's reach point all lose the anchor.
   - The two vestibule walls and the pad are dismantled (refunds 1 + 1 + 0 timber). The inventory increases by 2, and the minted item IDs are masked in comparison.
8. **Damage, repair, destruction** (Kera still at work).
   - Three sword blows from (100.5, 105.6), facing 180°, on the north wall at (100500, 105000): health 170. Three `PieceDamaged` with source `melee`.
   - `RepairPieceCommand` costs 1 timber; health 200.
   - A shot at the door costs it 2 (source `shot`).
   - The player stores 2 timber in the chest (`MoveItemCommand` → `container.pce_…`). The chest's container record materialises with its derived `cnt_` ID. Ten blows destroy the chest:
     - `PieceDestroyed`, `NavigationInvalidated`, `StructureSequence` +1;
     - the 2 timber lie on the ground at the chest's site point (103.5, 104.4) with their item IDs unchanged;
     - the player picks them up.
   - Timber running total from the 40: build −29 → 11; vestibule −5 → 6; refunds +2 → 8; repair −1 → 7; the stored 2 come back → 7.
9. **Route alteration.**
   - Before any change, a read-only navigation query records P2: Kera's route from her anchor to her authored site (61.6, 139.6), out of the doorway and round the workshop.
   - The player places pads (103500, 106500) and (103500, 109500), squares (34, 35) and (34, 36), then walls on their east edges (105000, 106500) r1 and (105000, 109500) r1. That is a wall line along x = 105, z 104.8-111.2, costing 6 timber (1 left).
   - A body-overlap refusal is included: with the player standing at (105.0, 109.5), the second wall is refused "someone is standing there". The player steps to (106.5, 109.5) and places it.
   - The same query now returns P3 ≠ P2. P3 no longer crosses x = 105 between z 104.8 and 111.2. It passes either north of about z 111.5 or round the south side; the navigation design decides which, deterministically.
   - The player releases Kera (`ReleaseWorkerCommand`, within talk reach inside the workshop). She walks home along P3 and reaches her site.
10. **Save, quit, reload.**
    - With Kera mid-walk on P3, the player quicksaves.
    - World W1 continues 400 ticks → digest D1, `StateDump` S1.
    - A fresh `GameSession` loads the save → W2 continues 400 ticks → D2, S2.

    Assertions:
    - D1 == D2;
    - `StateDump.Compare(S1, S2)` gives 0 differences;
    - the dump of the save equals the dump just after load, 0 differences;
    - every piece row (ID, def, pose, owner, health, door, worker) is equal;
    - `StructureRevision` is equal;
    - the navigation digest is equal before the save and after the load, and the current path of Kera is equal. The ROADMAP's "path is valid before a cell reload and invalid after" failure mode (RK-14, `docs/RISK_REGISTER.md:290-292`) is exercised by save and load, because Ashen Hollow never unloads a cell.
11. **Replay.** The test also quicksaves just before step 2. A second load of that save replays the command log of steps 2-9 at the same ticks. The pattern is `DeterminismAndViewTests.cs:49-78`.
    - Raw `StateDigest` is equal through step 3, where no item has been minted. Step 4's spear and later refunds are minted through `NewId`.
    - The replayable `StateDump` is equal to the end.

---

## 23. Test plan

**Domain.Tests (`tests/Domain.Tests/Building/`):**
- Lattice anchor rules for every slot kind.
- R_r transforms and rotated boxes for all 4 r, for every catalogue piece.
- Socket world positions and axes.
- `Snap` with fixed aims, including aims exactly on half-module lines and door ties by ID.
- Terrain relief: square (111..114, 105..108) is 228 mm; the (99..102)² square matches its expected value.
- Roof support cases: a walled edge; a neighbour depth-1; a far span refused.
- Integer box/box and box/circle overlap: touching is allowed.
- Repair ceil and refund floor.
- `EntityId.Derived` is stable, and ordinal order equals seq order.

**Content.Tests:**
- The game pack builds the catalogue, `config.building`, the build area and the deadfall.
- `LoadAll_Loads_Yaml_Files` has the new IDs.
- Each BLD001-BLD006 and WLD015 rule rejects a crafted bad file, using the `EditedContent` harness (content_registry §3.2 step 9).
- `piece_ref` resolves.
- Kera's `works_at`.

**Application.Tests:**
- For each piece: accepted placement, events, views, exact material spend, IDs.
- Each of the 15 rules refuses with its reason and leaves the digest unchanged:
  - terrain and authored overlap use a test content pack with a steep area or a rock inside the area;
  - protected ground uses an area over a spawner;
  - the piece cap uses `max_pieces: 3`.
- **Preview parity:** for 40 poses covering every rule, `PreviewPlacement(...).Reason` equals the command's refusal, and `Allowed` holds exactly when the command is accepted.
- **Collision:** wall; doorway pass; door block and pass; door refuses to close on a companion or creature; prediction equals authority across a new wall (`Kinematics.Step` with `simulation.Space`); creatures blocked; `Aim` stops at walls.
- **Chest:** store; take; persists; dismantle refused while not empty; destroy spills with IDs kept.
- **Station:** crafting at a piece anvil; refused after dismantle.
- **Damage:** melee, shot and formula amounts; a blow that hits a creature does not damage; an authored wall in front shields; destroy at 0; doorway destruction takes its door.
- **Repair, dismantle, assignment:** the rules above.
- The four-cell pad's host cell and per-cell digests.
- The acceptance test (section 22).
- **Performance:**
  - 200 pieces placed, plus 60 creatures: tick < 4 ms. This extends `SixtyCreatures_TickWithinTheBudget` (`tests/Application.Tests/CreatureTests.cs:526-544`).
  - Median fast-path placement validation < 2 ms.
  - Worst slow path < 20 ms.
- **RK-06:** place 200 pieces, save; the `entities.msgpack` size grows by no more than 200 × 250 bytes over the empty save; reload gives equal rows.

**Persistence.Tests:**
- The 13→14 step.
- The v14 fixture.
- "A schema-14 entities section without `pieces` is corrupt, not defaulted."
- The definition-ID pass: rename, and removal as loss, for piece defs and worker NPCs.
- Baseline proof of piece host cells, and rebase through a transition.
- An orphan chest container is rejected and reported.
- Byte stability.
- Every hard-coded step list updated (persistence research §3.3 G).

**Architecture.Tests:**
- The `WorldDelta` public surface additions.
- `Simulation` gains `PreviewPlacement`.
- The static-state scan passes: no static mutable collections in Domain/Building.

---

## 24. Implementation map and order of work

1. **Domain** `src/Domain/Building/Building.cs` (pure):
   - `PieceDefinition`, `PieceFamily`, `SlotKind`, `SocketType`, `SocketDef`, `FootprintPart`, `TraversalClass`;
   - `BuildingCatalog`, `BuildingConstants` (+`Problem()`), `BuildAreaSite` (in `Spatial/RegionLayout.cs`);
   - `Lattice` (anchor rules, slot keys), `Rotation` (R_r, boxes);
   - `BuildingRules` (`Validate`, `Snap`, `Relief`, `Support`, cost scaling), `PlacementRule`, `PlacementCheck`, `ReferencePoint`, `NavFootprint`;
   - `EntityKind.Piece` (`pce`) and `EntityId.Derived`.
2. **Content:**
   - kind `piece`; `piece_ref`; `BuildingContent` (Validate, BuildCatalog, BuildConstants);
   - the WLD015 build-area parse;
   - the `works_at` parse in `SocialContent`;
   - content files: 7 pieces, the timber item, the deadfall resource and node, `config.building`, the region edits, and Kera's `works_at`.
3. **World:**
   - `PieceRecord`, the `WorldDelta` store, snapshot and digest;
   - `StateSlice.Structures` and wrappers, `RuntimeState.Space`;
   - `SystemContext.Space`, `Stations()`, `WorkAssignments()`, and piece sites in `FindContainer`/`ClosedDoors()`;
   - `BuildingSystem` in `src/World/Runtime/Building.cs`: five handlers plus `DamagePiece` and `OperatePieceDoor`;
   - the `SpillContainer` handler, and the `Baseline`/`Materialize`/`Check` changes in `InventorySystem`;
   - the damage hooks in `CombatSystem`;
   - the piece-door branch in `InteractionSystem`;
   - `CraftingSystem` stations;
   - every `Setup.Layout.Space` read switched to `Space` (12.1);
   - `Simulation` composition, `DrainCommands` and `Dispatch` arms, the views, and `PreviewPlacement`.
4. **Application:** `GameSession.Boot` builds `BuildingSetup`; the M7 transition.
5. **Persistence:** DTOs, the step, frozen V13, loader, rebase, fixtures.
6. **Presentation:** `StructuresView`, build mode, ghost, keys, F2, the camera bound, and `--build-shots`.
7. **Docs** (section 25) and `docs/M7_STATUS.md`.

`BuildingSystem` has **no `Tick`**. Nothing about a piece evolves with time in M7, so it is not in `Simulation.Step`'s list.

---

## 25. Building-side document reconciliation (M7's first slice)

- **`docs/ROADMAP.md:283-285`:**
  - "foundations, … floors" → "ground pads (foundation and floor are one ground-level piece; one storey, owner ruling 2)";
  - "free rotation" → "quarter-turn rotation (owner question; D-08)";
  - "the navmesh updates on placement" → "the domain navigation is invalidated deterministically on every structure change (owner ruling 1)";
  - record that M7's entry criterion "NPCs and companions path reliably" is met inside M7.
- **`docs/DECISIONS.md` D-08:** a dated note. v1 rotation is quarter turns on a 3 m lattice, and "free rotation" is a later catalogue and footprint question, never physics.
- **`docs/WORLD_ARCHITECTURE.md` §10 (`:409-420`):**
  - identity: pieces are `pce_` rows, and `bld` structures are not built;
  - persistence: `entities.msgpack` `pieces`;
  - cross-cell: host cell = the anchor's cell, with overlap derived;
  - placement legality: add build area, bodies and navigability;
  - navigability: validated, not "guaranteed by snapping" (contradiction C23);
  - defense and tiers: not built.
- **`docs/WORLD_ARCHITECTURE.md` §11 (`:435`):** the navmesh row moves to the domain (ruling 1).
- **`docs/PERSISTENCE.md`:**
  - §2 not-saved: navigation is domain-derived;
  - §3.2 and §3.3: `buildings.msgpack` is deferred, and M7 pieces live in `entities.msgpack`;
  - §5.4: add the field table from 20.2;
  - §7.4: pieces are applied before containers, and building is populated before NPCs;
  - §6.2: add rows 11→12 through 13→14.
- **`docs/DATA_MODEL.md`:**
  - §1 kind table: `piece` / `pieces` / `piece`;
  - §2.2: the `pce piece` prefix, and derived identities;
  - §3.1 row 148: as built;
  - §5: the `piece_ref` row;
  - a new §4.x piece schema with "As implemented (M7)";
  - §6: save-sensitive building rows.
- **`docs/SYSTEMS.md` S-32:** an as-built note. No structure record, no raids, no attack frequency, no construction progress.
- **`docs/RISK_REGISTER.md`:** RK-06 and RK-14 validation become the headless tests of section 23. RK-14's "needs the engine" is withdrawn under ruling 1.
- **`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:1080`:** the "player settlement building" non-goal is superseded for M7 by the scope ruling that M7 is proven in Ashen Hollow. Owner question 4 covers it.
- **`src/Domain/EntityId.cs:17-21` remark and AGENTS.md:** derived identities are named.

---

## 26. Scope notes, owner questions, open issues

### Scope notes (no challenge to a scope ruling)

- **Item 13 ("no new economy").** The one new material, one node and one baseline transition are content through the unchanged M3f model: no currency, no upkeep, and timber sells for 0. The fallback in 13.2 avoids the transition if the persistence lead prefers.
- **Item 8.** The M7 damage sources are the player's own strikes only. They are reachable in play, explicit, and invent no threat. Creature damage to pieces is deliberately absent.

### Provisional owner questions (building)

1. Rotation: quarter turns on a 3 m lattice (default), or 45°/free (oriented footprints across `Kinematics`, traces and prediction)?
2. Kera Voss as the assigned worker at a player-built anvil bench (default), or another settlement NPC, or reconcile to M10 (scope item 5)?
3. The build area at the four-cell crossing, x/z 87-114 m (default), or anywhere legal? This also supersedes the Ashen Hollow bible's "no player settlement building" line for M7.
4. A renewable timber deadfall (needs an M7 baseline transition, default), or a one-shot timber stack (no transition)?

### Open issues

- **Authored layout is not in `baseline_hash`.** A later layout edit that runs into saved pieces is audited (`StructureAudit`), not proven. A layout digest or transition policy is future work (M2b's deferred "position legality after a rebase", `docs/M2B_STATUS.md:135`).
- **NPC moving state.** Its persistence, the NPC operation of **authored** doors, and the exact detour choice belong to the navigation and NPC designs. The acceptance test depends on them.
- **Item minting stays non-deterministic** (`NewId`). Replay windows with refunds compare ID-masked dumps. Item spend order within such windows is the pre-existing crafting limitation.
- **Height-blind queries** make the 700 mm chest block sight and shots although a jump clears it. This is recorded, not fixed.
- **Creatures (r up to 550 mm) are not a navigability reference class.** A 250 mm navigation grid passes them through 1.6 m doorways only under inclusive distance.
- **Presentation cost** of one node plus one collider per piece at 256 pieces is unmeasured. The RAZER window is still owed (`docs/M6_STATUS.md:221`). Merged meshes per structure are the fallback.
- **"Name every lost structure"** (`docs/PERSISTENCE.md:136`) is impossible when the entities section itself is corrupt. The quarantine report names the section only.
- **An orphaned chest container** (its piece definition removed with `~`) is a reported item loss.
- **The `construct_building` objective** and building-related faction acts are deferred. Their seams are named in 7.3.
- **The art kit is withheld** (`src/Presentation/Art/art_bindings.json:25-31`). M7 ships greybox pieces, with an empty `pieces` binding section ready.
