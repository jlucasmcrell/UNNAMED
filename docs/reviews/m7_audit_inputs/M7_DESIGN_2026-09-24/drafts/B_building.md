# M7 Part B - Building v1 (authoritative)

Status: design and implementation plan, 2026-09-24. This revises `drafts/panel/B_building_v1.md` against the adversarial critique `drafts/panel/B_building_critique.md`. Appendix A lists every finding and what was done with it. This document writes no production code.

Base: origin/main `e10d2c4`, read from the snapshot named in `00_SCOPE_RULINGS.md` §0. Every `path:line` is repo-relative at that commit. Every code fact that a decision depends on was re-read in the snapshot for this revision. Where it matters, the text says which navigation-candidate fact it relies on (`drafts/panel/nav_codefit.md`, `nav_minimal.md`, `nav_scale.md`) and which factions fact (`drafts/C_factions.md`).

---

## 0. Verdict, requirements, invariants, lattice

### 0.1 Verdict in one page

Building v1 places content-defined **pieces** on a world-anchored **3 m lattice** (squares, edges, a door slot per doorway) in **quarter turns**. Every blocking piece is an axis-aligned integer-mm box, so `Kinematics.Step`, combat traces and prediction need no new geometry. A placed piece is one row: derived `pce_` ID, definition ID, absolute anchor in mm, rotation, owner, current health, door flag. Its shape comes from the definition and is never saved.

M7 builds **seven greybox pieces** (ground pad, wall, doorway, door, roof, storage chest, anvil bench of the M3f `anvil` kind); **five player commands** (place, dismantle, repair, assign worker, release worker; `InteractCommand` works doors, `MoveItemCommand`/`TakeAllCommand` the chest); **one owning system**, `BuildingSystem` (`StateSlice.Structures`), plus `StateSlice.NpcErrands` on `NpcSystem`; and **one region-inline build area**, `build_area.hollow_crossing`, x and z 87-114 m, containing the four-cell corner (100, 100).

The main decisions:
- **Doors** are authoritative blockers with their open state on the row, reusing the `DoorSite` idea (`src/Domain/Spatial/RegionLayout.cs:8-12`).
- **Pads** are "foundation" and "floor" at once. Body Y stays terrain plus lift (`src/Domain/Spatial/Kinematics.cs:159`); a pad is drawn draped and accepted only where the ground rises ≤ 250 mm.
- **Damage** has three explicit sources, all the player's strikes. Repair and dismantle cost materials; dismantle refunds half. Destruction at 0 health takes the dismantle path without refund. Nothing collapses.
- **Navigability** refuses a placement that disconnects a reference point from the region spawn, for a 350 mm body, on a graph where every door and barrier is passable. That graph depends only on content and the piece set, so nothing else can make it stale (§17).
- **Materials:** one new material (`item.material.timber`) from one renewable M3f node (`node.wood.deadfall`, (84, 118)), with one baseline transition. No piece costs iron.
- **The worker:** Kera Voss's walk to and from a player-built bench is an `NpcErrandRecord` in `entities.msgpack`, anchored to her home cell like a creature record, so a save taken mid-walk continues identically.
- **Persistence:** schema 14 adds `pieces`, `structure_seq` and `npc_errands` to `entities.msgpack`; no new section file.
- **Identity:** piece and piece-chest IDs are derived from the owner and `structure_seq`, so replays and save-then-continue mint identical IDs.

The proof is "the Crossing Workshop" (§11): a 2 × 2 workshop over the four-cell corner whose south doorway straddles x = 100. Kera walks round it and in, crossing z = 100 inside it; a sealing vestibule is refused; a new wall changes her route home; a save mid-walk, with the companion pathing in, continues identically.

### 0.2 What ROADMAP M7 actually requires of building

The building text is `docs/ROADMAP.md:283`, the exit criteria are at `:284` and the proof at `:285`. The bracket after each answer names the scope-ruling row that governs it.

| ROADMAP phrase | M7 answer | § |
|---|---|---|
| "Socket/snap placement" | Content-declared sockets on a 3 m world lattice. Mates need exact position, type and axis, with 0 mm tolerance. | 5 |
| "foundations, walls, floors, roofs, doors" | Pad (foundation and floor are one ground-level piece), wall, doorway, door, roof. [ruling 2; row 2] | 1, 6, 20 |
| "free rotation and socketing" | Quarter turns. The definition keeps a `rotations` list and config `rotation_step_deg`, so 45° can come later with oriented footprints. [row 3, owner question 1] | 0.4 |
| "ownership" | `owner` (a `chr_` ID) on every row. It gates dismantle, repair, assignment, chest access and piece-door operation. It is not legal status. [ruling 3] | 12 |
| "per-piece health applied by explicit rules" | `health_max` on the definition, `health_current` on the row, and three named damage rules. [row 8] | 13 |
| "repair" | `RepairPieceCommand`, whose cost scales with the missing health. | 13.4 |
| "storage containers" | `piece.storage.chest`, which reuses `ContainerSite` and `ContainerRecord`. [row 9] | 14.1 |
| "one crafting station as a placeable" | `piece.station.anvil`, which reuses the `anvil` kind and the March Spear recipe. [row 9] | 14.2 |
| "persistence as a sparse world delta (D-05)" | Host-cell-anchored rows in `entities.msgpack`, proven by `baseline_hash`. | 9 |
| "rejects un-navigable or overlapping configurations" | Fifteen ordered checks: overlap by lattice slot and against authored geometry, navigability by reference-point connectivity. | 3, 17 |
| exit: "assign an NPC to work in it" | One persistent assignment of Kera Voss to the bench's work anchor. [row 5, owner question 3] | 15 |
| exit: "navigates in, through, and around it ... straddling a cell boundary" | The workshop covers (99-105)², four cells. Its doorway straddles x = 100, and Kera's route crosses z = 100 inside it. [row 6] | 11, 16 |
| exit: "save/load preserves every piece with correct ownership and health" | Row round-trip, digests, `StateDump`, and a v14 fixture. | 9, 22 |
| exit: "damage/repair works and is explicit" | A three-row rule table, each row one named code point. | 13 |
| exit: "the navmesh updates on placement" | Read as "the domain navigation is rebuilt deterministically on every footprint change" (ruling 1). | 21.1 |
| proof: "navmesh path test including the straddling-seam case" | A headless Application test plus the windowed build beats. Ashen Hollow never unloads a cell, so RK-14's "force a cell unload" becomes save and load. | 11, 22 |

**Not required, and deferred:** the `construct_building` objective (stays in `ObjectiveTypes.NotBuilt`, `src/Domain/Quests/Quests.cs:146`); property threats and the attack-frequency option [row 8]; upgrade tiers; beds, fences, posts, half and window walls; building skills and XP; a persisted "structure" record (§2.1); a faction act for placement (declined in C_factions §13).

### 0.3 Invariants and hard non-goals

1. **One storey (ruling 2).** Body Y stays `Terrain.HeightAtMm + lift` (`Kinematics.cs:159`), and landing on a blocker pushes the body off (`Kinematics.cs:124-127`). `Kinematics` and `MovementRules` gain no members. No stairs, ladders, upper floors, climbing, walk surfaces or terrain flattening.
2. **No overhang blockers** (`ClearanceMm` is always 0). `IsClear` and `Crosses` are height-blind (`Kinematics.cs:170-173`; `src/Domain/Spatial/Blockers.cs:26`), so a lintel blocker would close a doorway to sight and navigation. Lintels and roofs exist only in presentation.
3. **Axis-aligned integer-mm boxes** for every part (`Blockers.cs:40-41`).
4. **No structural simulation (D-08).** Support is checked at placement only; removing a wall never removes a roof.
5. **Authority.** Every mutation is a command applied at a tick boundary. The preview runs the same validation and writes nothing authority reads (§4).
6. **Determinism.** Integer placement math; order by derived piece ID (placement order) or geometry, never by a `NewId`-minted ID. The one float rule, which piece a strike hit, reuses today's trace arithmetic exactly (§13.2).
7. **Persistence.** A piece is a row (D-08). Space, socket index, footprints and navigation are derived and never saved.

### 0.4 The building lattice

**Module.**
- M = 3000 mm and H = 1500 mm, anchored at the world origin (lattice lines x = 3000·i, z = 3000·k). 3.0 m is also the asset kit's module (`docs/SCALE_AUDIT_REPORT.md:177-188`).
- Both navigation candidates choose a **250 mm** lattice (nav_codefit §0; nav_minimal A3; nav_scale §4.2): 3000 / 250 = 12, so every building line is a navigation line, and so is every 100 m seam (100 000 / 250 = 400).
- Seams are building lines only every third seam (100 000·n ≡ 1000·n mod 3000). None is in Ashen Hollow, so every square containing a seam straddles it. For 2 km regions, where anchors can lie on a seam, the host cell is `CellKey.OfWorld(XMm / 1000.0, ZMm / 1000.0)` of the anchor everywhere, which floors and is unambiguous (`src/World/Coordinates.cs`).
- `module_m` is save-locked (anchors are absolute) and never changes without a migration step.

**Rotation.** `rotation` is r ∈ {0, 1, 2, 3} quarter turns. The local +Z axis points along world facing r·90 000 mdeg, where 0 = +Z, clockwise towards +X (`content/regions/ashen_hollow.yaml:11`). The integer local-to-world maps are:
- R_0(lx, lz) = (lx, lz)
- R_1(lx, lz) = (lz, −lx)
- R_2(lx, lz) = (−lx, −lz)
- R_3(lx, lz) = (−lz, lx)

A local facing f becomes (f + r·90 000) mod 360 000. A local box rotates to the box bounding its four rotated corners, which is exact under quarter turns.

**Slots.** Each piece has one slot kind. Arithmetic uses `WorldMath.FloorMod`.

| Slot | Anchor rule (mm) | Rotation rule | Slot key | Geometry for area containment |
|---|---|---|---|---|
| `square` (pad) | x ≡ H, z ≡ H (mod M) | any | `sq:i:k`, i = (x−H)/M, k = (z−H)/M | the square [x±H] × [z±H] |
| `edge` (wall, doorway) | r even: x ≡ H, z ≡ 0; r odd: x ≡ 0, z ≡ H | parity fixes the axis | `ex:i:k` (r even) or `ez:i:k` (r odd), with i = ⌊x/M⌋, k = ⌊z/M⌋ | the edge segment |
| `door` | equals the anchor of an intact doorway | r ≡ the doorway's r (mod 2) | `dr:<doorway's edge key>` | the doorway's edge segment |
| `roof` | x ≡ H, z ≡ H | any | `rf:i:k` | the square |
| `furniture` (chest, bench) | x ≡ H, z ≡ H | any | `fu:i:k` | the square |

A slot key holds at most one intact piece. Different slot kinds never conflict geometrically, because BLD003 bounds them (§19). Piece-against-piece footprint overlap is therefore not checked: two walls meeting at a corner share a 400 × 400 mm corner square, and that is intended.

---

## 1. Piece definition (content kind `piece`)

### 1.1 Schema

The field set is closed: any other key is a `BLD002` error.

```yaml
id: piece.wall.timber               # piece.<family>.<name>
kind: piece
schema: 1
display_key: piece.wall.timber.name
tags: [piece]
name: Timber Wall
notes: ...
family: wall                        # closed: pad | wall | doorway | door | roof | storage | station
slot: edge                          # closed: square | edge | door | roof | furniture; one legal slot per family
rotations: [0, 1, 2, 3]             # quarter turns; config.building.rotation_step_deg is 90 in M7
bounds_m: [-1.7, -0.2, 1.7, 0.2]    # local placement bounds at r0 [min_x, min_z, max_x, max_z]
parts:                              # blocking footprint(s); omitted for pads and roofs
  - { box_m: [-1.7, -0.2, 1.7, 0.2], height_m: 3.0, traversal: solid }   # closed: solid | door
sockets:
  - { type: edge_mount, at_m: [0, 0], axis: x }
cost:                               # spent on placement, in list order
  - { item_ref: item.material.timber, count: 2 }
health_max: 200
supports_roof: true                 # optional, default false
# family-specific, closed:
# container: { stack_slots: 12, at_m: [0, 0.9] }                                   # storage only
# station:   { kind: anvil, work_anchor_m: [0, -0.25], work_facing_deg: 0 }        # station only
```

### 1.2 Field semantics

- **`slot`** is the family's one legal slot (pad → square; wall, doorway → edge; door → door; roof → roof; storage, station → furniture). It stays explicit, so a later `fence` (edge) or `post` (a vertex slot) is data plus one slot rule.
- **`bounds_m`** is the rectangle for reach, area containment, authored overlap and protected ground: the square for pads and roofs, the union of `parts` for blocking pieces.
- **`parts`** convert to mm with `Mm` (round half away from zero; `src/Content/WorldContent.cs:456-462`), must be whole mm and lie inside `bounds_m`. `solid` blocks always; `door` blocks only while the row's `DoorOpen` is false.
- **`sockets`** come from a closed type set:

  | Type | Role | Offered by | Needed by | Mates with |
  |---|---|---|---|---|
  | `edge` | provider | pad: (0, ±1.5) axis x; (±1.5, 0) axis z | - | `edge_mount` |
  | `square` | provider | pad: (0, 0) | - | `square_mount` |
  | `door` | provider | doorway: (0, 0), axis x | - | `door_mount` |
  | `edge_mount` | mount | - | wall, doorway | `edge` |
  | `square_mount` | mount | - | chest, bench | `square` |
  | `door_mount` | mount | - | door | `door` |

- **`cost`** is a list of `{item_ref, count ≥ 1}`. `item_ref` must name a plain `item` kind.
- **`health_max`** is a whole number ≥ 1. Following the DATA_MODEL naming corollary (`docs/DATA_MODEL.md:158`), the definition says `health_max` and the row says `health_current`.
- **`container`:** the chest's container site point (local metres) and its capacity.
- **`station`:**
  - `kind` must be used by some recipe: today `forge` (`content/recipes/smithing/iron_billet.yaml:9`) or `anvil` (`march_spear.yaml:8`);
  - `work_anchor_m` and `work_facing_deg` give the worker's pose in local terms.
- **Presentation asset ID: not in the definition.** Art bindings go in a new `pieces` section of `src/Presentation/Art/art_bindings.json`, keyed by piece definition ID. Bindings are presentation-only data that "touches no rule, save or content hash" (`src/Presentation/Art/ArtBindings.cs:28-31`). An asset field in rules content would move `content_hash` on every art change.

---

## 2. Runtime instance

### 2.1 No persisted "structure" record

WORLD_ARCHITECTURE §10 and PERSISTENCE §5.4 describe a ULID-keyed structure that owns its pieces (`docs/WORLD_ARCHITECTURE.md:409`; `docs/PERSISTENCE.md:250`). M7 does not build it. It would need merge, split and ownership copying, and it serves no exit criterion. In M7 a "structure" is a **derived** connected component of the socket graph. The F2 debug view may name it after its smallest piece ID. `bld` (`EntityKind.Building`, `src/Domain/EntityKind.cs:17`) stays reserved for a later settlement record.

### 2.2 The row: `PieceRecord` (`src/World/WorldDelta.cs`)

```csharp
/// A player-placed building piece (M7): one row, whole, anchored to its anchor's cell and proven against that cell's
/// baseline like a created instance. Its shape is its definition's; only what the definition cannot know is here.
public sealed record PieceRecord(
    EntityId InstanceId,      // pce_, derived (§18)
    string DefId,             // piece.*, through the definition-ID pass on load
    string HostCell,          // CellKey.OfWorld(XMm / 1000.0, ZMm / 1000.0): floor, so a seam anchor is unambiguous
    long XMm, long ZMm,       // the slot anchor, absolute world mm (the creature-record convention, WorldDelta.cs:162-175)
    int Rotation,             // 0..3 quarter turns
    EntityId Owner,           // chr_ of the placing character
    int HealthCurrent,        // 1..health_max
    string? BaselineHash = null)
{
    public bool DoorOpen { get; init; }   // door pieces only; false elsewhere
}
```

**Not stored** (derivable): footprints, bounds, sockets, slot keys, overlapped cells, `health_max`, cost, container site and capacity, station kind and work anchor, and the worker, whose assignment lives only in the NPC's errand record (§15), so a row and an errand can never disagree. The VS row's `socket_parent` (`docs/VERTICAL_SLICE.md:161`) is deliberately absent; dependents are recomputed from geometry (§10.1).

### 2.3 `StructureSequence`

`WorldDelta` gains one global persisted scalar, `long StructureSequence`, incremented once per change to the **footprint set** (each piece placed, dismantled or destroyed) and never by door toggles, damage, repair or assignment. It is the ordinal piece IDs derive from (§18) and the **structure revision** (`Simulation.StructureRevision`). Placement's navigation labels are a pure function of content plus the piece set (§17.1), so the revision keys them completely.

### 2.4 Derived runtime state (owned by `StateSlice.Structures`)

`RuntimeState` gains transient fields, each written only through `Require(owner, StateSlice.Structures)` (the wrapper pattern at `src/World/Runtime/RuntimeState.cs:163-175`):

| Field | Content | Rebuilt |
|---|---|---|
| `WalkSpace? Space` | `Setup.Layout.Space` plus each intact piece's **solid** parts: authored blockers in content order, then pieces by ID ascending (placement order), parts in definition order, IDs `"{pieceId}#{partIndex}"` | on place, dismantle, destroy |
| `ImmutableArray<Blocker> ClosedPieceDoors` | closed door parts, by piece ID | on the above and on every door toggle |
| `SocketIndex` | `SortedDictionary<(SocketType, long X, long Z, SocketAxis), ImmutableArray<EntityId>>` of intact providers, IDs ascending | with `Space` |
| `ImmutableArray<NavFootprint> StructureFootprints` | §21.1, sorted by (PieceId, Part) | with `Space` |

`Kinematics.Resolve` is order-dependent (`Kinematics.cs:183-206`), so the part order above is part of the determinism contract. Derived IDs order by placement, which satisfies nav_minimal's B3 ("never a wall-clock ULID").

**Why static, not dynamic, blockers** (nav_minimal B2, not nav_codefit §20 item 3): `Resolve` applies the jump tuck only to static blockers (`Kinematics.cs:189-193`), so the low chest and bench behave like authored low structures; and `ClosedDoors()` is rebuilt on every `Obstacles()` and `Walled` call (`src/World/Runtime/Systems.cs:48, 82-86`), while a rebuilt space costs nothing per call. Navigation reads `SystemContext.Space` either way (§21.1).

---

## 3. Placement command

### 3.1 Command and events (`src/World/Runtime/Building.cs`)

```csharp
public sealed record PlacePieceCommand(EntityId Actor, string PieceDefId, long XMm, long ZMm, int Rotation) : GameCommand(Actor);

public sealed record PiecePlaced(EntityId PieceId, string DefId, long XMm, long ZMm, int Rotation, EntityId Owner, long Revision, long Tick);
public sealed record StructuresChanged(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, StructureChangeKind Kind, long Revision, long Tick);
public enum StructureChangeKind { Placed, Dismantled, Destroyed }
```

- The command carries only a discrete pose. It never carries an aim point, a camera ray or a tolerance, which keeps `Commands_CarryNoCameraState_SoEveryPerspectiveSubmitsTheSameThing` green (`tests/Application.Tests/SessionTests.cs:162-178`).
- `DrainCommands` gains `PlacePieceCommand c => _building.Handle(c, WorldTick)` (`src/World/Runtime/Simulation.cs:273-299`).

### 3.2 Where the code lives

Domain has no project references (`src/Domain/Domain.csproj`), and `StackTake` is internal to World (`src/World/Runtime/Items.cs:84`). The validator therefore lives in World, and Domain keeps only World-free math.

- **Domain, `src/Domain/Building/Building.cs`** (`UNNAMED.Domain.Building`), pure: the definition records; `BuildingCatalog`; `BuildingConstants` with `Problem()`; `Lattice` (anchor rules, slot keys); `QuarterTurn` (R_r, boxes, facings); `BuildingMath` (integer overlaps, `Relief(TerrainGrid, square)`, cost scaling); `PiecePose` and `Snapper.Snap` (§5).
- **Domain, `src/Domain/Spatial/StructureFootprints.cs`** (`UNNAMED.Domain.Spatial`, so navigation's Domain code can consume them): `TraversalClass`, `NavFootprint`, `ReferencePoint`, `ReferenceKind`. They are positional records, so `ViewsAndEvents_HaveNoPublicSetters` passes (`tests/Architecture.Tests/ArchitectureTests.cs:134-155`).
- **Domain, `src/Domain/Spatial/RegionLayout.cs`:** `BuildAreaSite` and `RegionLayout.BuildAreas`; `ContainerSite` gains init properties `EntityId? InstanceId` and `EntityId? Owner`.
- **World, `src/World/Runtime/BuildingRules.cs`** (`internal static`): `Validate(PlacementContext, string defId, long x, long z, int r, NavCheckDepth? depth)` returning `PlacementCheck`, plus `PlacementContext` and `INavigability` (internal) and the public enums `PlacementRule` and `NavVerdict` (in `UNNAMED.World.Runtime`, since `PlacementPreview` exposes them). The handler and the preview both call it inside World, so the "same function" guarantee holds.

### 3.3 The ordered validation

`PlacementCheck(bool Allowed, PlacementRule? Failed, string? Reason, BoundsMm Bounds, ImmutableArray<BoxBlocker> Parts, ImmutableArray<StackTake> Takes, NavVerdict Navigability)`. Checks run in order and the **first** failure is returned. Reason texts follow the existing lowercase style.

| # | Rule | Exact test | Reason (example) |
|---|---|---|---|
| 1 | `Actor` | The actor is the player, not `Defeated`, and the combat phase is `Idle` at this tick (the crafting test, `src/World/Runtime/Crafting.cs:158-160`) | "dead" / "busy" |
| 2 | `Definition` | The ID is in the catalogue | "{id} is not a piece this build knows" |
| 3 | `Rotation` | r ∈ {0..3} ∩ `def.Rotations` | "{name} cannot be turned that way" |
| 4 | `Lattice` | (x, z, r) satisfies the slot rule of §0.4. For a door: an intact doorway has anchor (x, z) and r ≡ its r (mod 2). Whether the doorway already has a door is check 7's question | "that is not on the building grid" |
| 5 | `BuildArea` | The slot geometry (square or edge segment) lies inside one build area box, closed intervals. An edge on the boundary is inside, and its 200 mm end extensions may reach past it | "you may only build inside a build area" |
| 6 | `Reach` | (squared distance from the actor's body to the nearest point of `Bounds`) ≤ `place_reach_mm`² (6000²), in `long` | "that is 7.20 m away; building reach is 6.00 m" |
| 7 | `Slot` | No intact piece holds the slot key | "a Timber Doorway already stands there" / "that doorway already has a door" |
| 8 | `Support` | **Mounts:** every mount has a compatible intact provider at the same world position and axis. **Roof:** one of the square's four edges holds an intact `supports_roof` piece, or an edge-adjacent square holds an intact roof that has one itself (depth 1, evaluated now only) | "a wall needs a floor pad on one side" / "a roof needs a wall under one of its edges, or a roofed neighbour that has one" |
| 9 | `Terrain` | Pads only: max − min of `TerrainGrid.HeightAtMm` over a 13 × 13 sample lattice at 250 mm (corners and edges included) ≤ `pad_max_relief_mm` (250), all integer (`src/Domain/Spatial/TerrainGrid.cs:51-64`) | "the ground here is too uneven for a pad: 0.31 m of rise, 0.25 m allowed" |
| 10 | `Authored` | `Bounds` does not strictly overlap any authored static blocker, any authored door's closed box, or any barrier footprint, whatever their state. Box/box: minA < maxB ∧ minB < maxA on both axes. Box/circle: (cx − clamp(cx))² + (cz − clamp(cz))² < R², in `long`. Touching is not overlap | "that would build over rock_well" |
| 11 | `Protected` | `Bounds` does not strictly intersect a protected zone (§16.3) | "that ground is kept clear (Kera Voss's place)" |
| 12 | `Bodies` | Pieces with parts only: no part (a door is placed closed) strictly overlaps a body circle. Bodies are the player (350), every NPC including companions (350; `Systems.cs:86`), and every living creature (its `RadiusMm`). This is `Separation(...) is not null`, the door-closing test (`Systems.cs:267`) | "someone is standing there" |
| 13 | `PieceCap` | Intact pieces whose anchor lies in this area < `max_pieces` (256), the RK-06 per-area ceiling (`docs/RISK_REGISTER.md:158`) | "the crossing already holds 256 pieces" |
| 14 | `Materials` | For each cost line in order: carried, **unequipped** stacks of that definition, ordered by quality ascending then `ItemId` ordinal, cover the count. They become `Takes`. Worst quality goes first, since quality does nothing for a piece. Crafting spends best-first because there the weakest input caps quality (`Crafting.cs:178`) | "needs 2 item.material.timber" (the crafting wording, `Crafting.cs:188`) |
| 15 | `Navigability` | Only when the piece adds a **solid** part (wall, doorway, chest, bench), and only when `depth` is not null. This is §17 on the candidate state | "that would cut Kera Voss's work place off" |

Checks 1-14 cost microseconds; check 15's budget is §17.4 item 5.

### 3.4 Commit

```
string? Handle(PlacePieceCommand c, long tick):
    var check = BuildingRules.Validate(Context(c.Actor), c.PieceDefId, c.XMm, c.ZMm, c.Rotation, NavCheckDepth.Full)
    if (!check.Allowed) return check.Reason
    // the refusable cross-system step first (the GatheringSystem pattern, Crafting.cs:100-102)
    if (_context.Dispatch(new ExchangeItems(check.Takes, ItemId: null, Count: 0, Quality: 0)) is { } refused) return refused
    long seq = State.World.StructureSequence + 1
    var id   = EntityId.Derived(EntityKind.Piece, seq, "unnamed.piece/v1", c.Actor.Value)          // §18
    var row  = new PieceRecord(id, def.Id, CellOf(c.XMm, c.ZMm), c.XMm, c.ZMm, c.Rotation, c.Actor, def.HealthMax)
    State.PlacePiece(_owner, row, seq)          // WorldDelta: store, register id, StructureSequence = seq
    Rebuild()                                   // §2.4: Space, door cache, socket index, footprints
    _context.Dispatch(new RebuildNavigation(check.Bounds, StructureChangeKind.Placed, seq))   // §21.1: after the mutation, before events
    Publish(new PiecePlaced(id, def.Id, c.XMm, c.ZMm, c.Rotation, c.Actor, seq, tick))
    Publish(new StructuresChanged(check.Bounds..., StructureChangeKind.Placed, seq, tick))
    return null
```

`ExchangeItems` with `ItemId: null` spends only, all-or-nothing, and mints nothing (it decrements counts and retires emptied stacks; `src/World/Runtime/Items.cs:327-357`). A chest's container record materialises on first use, not at placement (§14.1). Two commands for one slot in one drain are FIFO; check 7 refuses the second (`Simulation.cs:271`).

---

## 4. Placement preview

```csharp
// Simulation - read-only, the Aim precedent (Simulation.cs:198-206). Added to TheSimulation_ExposesOnlyReadsAndTheCommandPath's
// allow-list (tests/Architecture.Tests/ArchitectureTests.cs:111-131) with the comment "the placement ghost (M7)".
public PlacementPreview PreviewPlacement(string pieceDefId, long xMm, long zMm, int rotation, bool checkNavigability);

public enum NavVerdict { NotApplicable, NotChecked, Proven, Refused, Unknown }
public sealed record PlacementPreview(bool Allowed, PlacementRule? Failed, string? Reason,
    long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, ImmutableArray<BoxBlocker> Parts,
    ImmutableArray<CostView> Cost, NavVerdict Navigability);
public sealed record CostView(string ItemId, int Count, int Carried);
```

**Contract.**
1. The preview calls `BuildingRules.Validate` with the player as actor and `depth = checkNavigability ? Window : null`. **It never runs the global pass.** `Window` returns `Proven` (connectivity provably unchanged), `Refused` (an enclosed pocket holds a previously satisfied point) or `Unknown` (the command decides).
2. **Parity.** At equal state, whenever `Navigability` ∈ {NotApplicable, Proven, Refused}, `(Allowed, Failed, Reason)` equals the command's result; a test asserts this over 40 poses covering every rule.
3. **Isolation.** The preview uses its own navigation scratch (a second `Navigator` over the same immutable state). The labels it reads are written only in commands and the constructor (§17.4). No read-only query writes anything an authoritative computation reads.
4. **Cadence.** Presentation asks for navigability only when the snapped pose or `Simulation.StructureRevision` changed; checks 1-14 may run every frame.
5. **Ghost:** green when allowed and `NotApplicable`/`Proven`; amber when allowed and `Unknown`/`NotChecked` ("the build will be checked when placed"); red when refused, with the reason under the reticle.
6. **Advisory.** Everything moves between preview and command, so the command can still refuse (D-11).

**Other read surfaces** are get-only properties, which need no allow-list change:
- `Simulation.Pieces` (`ImmutableArray<PieceView>`, by ID);
- `Simulation.Space`;
- `Simulation.Stations` (authored, then piece stations by ID);
- `Simulation.StructureRevision`;
- `Simulation.StructureAudit`;
- `Simulation.WorkAssignments`.

`Simulation.Containers` (`Simulation.cs:214`) appends piece chests.

```csharp
public sealed record PieceView(EntityId Id, string DefId, string Family, long XMm, long ZMm, int Rotation, EntityId Owner,
    int HealthCurrent, int HealthMax, bool DoorOpen, string? WorkerNpcId,        // WorkerNpcId derived from errands (§15)
    long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, ImmutableArray<BoxBlocker> Parts, string? ContainerKey, string? StationKey);
public sealed record WorkAssignmentView(string NpcId, EntityId PieceId, long AnchorXMm, long AnchorZMm, int FacingMdeg, NpcErrandPhase Phase);
```

**The H4 guard test.** Replay the same command log twice from the same save, once plain and once with 1,000 `PreviewPlacement` calls interleaved. The poses come from a fixed seed, and both `checkNavigability` values are used. The two runs must give equal replayable `StateDump`s, equal piece ID lists and equal `(Tick, Rejected)` sequences from `CommandLog`.

---

## 5. Snapping

### 5.1 Coordinates, compatibility, tolerance

- Socket positions are local integer mm, rotated by R_r and added to the integer anchor. Axes rotate with the piece: an x axis at odd r becomes a world z axis.
- Mating is exact equality of world position, type pairing (§1.2) and world axis. **Tolerance is 0 mm.** Every socket lies on a lattice position (BLD003) and every anchor is lattice-exact (check 4), so equality is well defined and float-free.
- **Providers.** The socket index (§2.4) lists intact providers by key with IDs ascending. When a mount has several providers (a wall between two pads), any one satisfies it, and none is recorded. Where a rule needs to know about providers (dependents, cascade), it asks for a **count** ("does another intact provider exist?"), so iteration order never decides anything.

### 5.2 Aim to pose (Domain, pure, presentation-side)

```csharp
// src/Domain/Building/Building.cs
public sealed record PiecePose(EntityId Id, string DefId, long XMm, long ZMm, int Rotation);
public static class Snapper
{
    public static (long XMm, long ZMm, int Rotation)? Snap(BuildingCatalog catalog, IReadOnlyList<PiecePose> pieces,
        string pieceDefId, long aimXMm, long aimZMm, int rotation);
}
```

`PieceView` maps to `PiecePose`. The rules, all in floor division:
- **square, roof, furniture:** x = M·⌊aimX/M⌋ + H, z = M·⌊aimZ/M⌋ + H, and r is kept.
- **edge:** the requested r fixes the axis.
  - r even: x = M·⌊aimX/M⌋ + H, z = M·⌊(aimZ + H)/M⌋.
  - r odd: x = M·⌊(aimX + H)/M⌋, z = M·⌊aimZ/M⌋ + H.
  - The result is unique by construction. An aim exactly on a half-way line resolves by floor, never by a tie.
- **door:** the candidates are intact doorways with no door whose anchor is within 2000 mm of the aim.
  - Take the minimum squared distance, in `long`. Ties go to the lower doorway ID, which is placement order.
  - Rotation: r' = r if r ≡ r_doorway (mod 2), otherwise (r + 1) mod 4. The two legal values choose the side the leaf swings towards, which is a presentation difference only.
  - The result is null when there is no candidate.

The player picks the piece and r with keys (§21.3). `Snap` produces the pose, the ghost shows `PreviewPlacement` of that pose, and the command carries that pose.

---

## 6. Foundations and terrain: what "floor at ground level" means

1. **Movement is untouched.** A pad emits no `Blocker`; a body on it stands at `Terrain.HeightAtMm(x, z)` exactly as beside it. Navigation sees only a `NonBlocking` footprint.
2. **A pad is exactly three things:** a placement anchor (four `edge` and one `square` provider), a terrain-tolerance gate (≤ 250 mm relief under the square), and a visual floor.
3. **How it is drawn.** Presentation drapes the pad top over the domain's own terrain triangles (the split `HollowView.BuildTerrain` uses, `src/Presentation/Greybox/HollowView.cs:91-128`), raised 20 mm with a 150 mm skirt, so feet sit 20 mm into the drawn floor and never float. Walls are drawn from the lowest terrain sample under them minus 200 mm (`HollowView.cs:169-170`). **A foundation visually covers uneven ground while gameplay stays terrain-bound**; the 250 mm cap keeps the drape reading as a floor, not a ramp.
4. **Why 250 mm.** With `HeightAtMm`'s exact integer arithmetic over the region grid (`ashen_hollow.yaml:15-62`), re-verified by the critique: the worst square in the area rises 228 mm (x 111-114, z 105-108), so all 81 squares take a pad; the four-cell square (99-102)² rises 96 mm (6352-6448).
5. **Height-blind pieces.** The chest (700 mm) and bench (900 mm) are under the 1150 mm apex (`content/config/base_speeds.yaml:15`), so the player can jump either (`Kinematics.cs:166-167`), and landing on one pushes the body off. Neither is a surface. Both still block sight and shots, the height-blind rule the fallen timber and fence already follow: recorded, not changed.
6. **A flat walkable floor is not built.** Its consequences would be: a walk-surface query and step-height rule in `Kinematics.Step` (every mover and prediction); surface heights in `Blocks`, `IsClear`, `CanStand`; "landed on" jumps; 2.5D navigation; vertical terms in traces and perception; a new meaning for saved body Y; and every movement determinism test re-proven. That is the vertical domain ruling 2 excludes.

---

## 7. Doors

**Model: an authoritative open/closed blocker.** It keeps the `DoorSite` concept, with the state moved onto the row. Authored doors keep their `world.*` flags. A piece door cannot use a flag, because flags are declared content and are cell-scoped (`src/World/WorldDelta.cs:250-259`).

### 7.1 Blocking

`SystemContext.ClosedDoors()` (`Systems.cs:48`) becomes three lists in this order:
1. the authored closed doors;
2. the standing barriers;
3. the cached `ClosedPieceDoors`.

Every consumer then sees a closed piece door exactly as it sees an authored closed door, and it blocks movement, blows, shots and sight while closed. The consumers are:
- `Obstacles()` (`Systems.cs:82-86`);
- the companion and creature obstacle lists (`Companions.cs:574`; `Creatures.cs:830`);
- every wall test through `SightWalls()` (§21.4);
- prediction through `Simulation.DynamicBlockers` (`Simulation.cs:254-255`).

### 7.2 Who may operate a piece door: `CanOperate`, pure

```
bool CanOperate(EntityId operatorId, PieceRecord door, RuntimeState s) =
      operatorId == door.Owner                                                        // the owner
   || (operatorId is an NPC instance whose NpcId ∈ s.Companions && the roster owner == door.Owner)   // the owner's companions
   || (s.World.NpcErrand(npcIdOf(operatorId)) is { WorkOwner: var o } && o == door.Owner)         // NPCs working for the owner (§15)
```

Creatures never operate doors. Other NPCs do not move in M7, so they never need to.

### 7.3 Commands

- **Player.** The player uses the existing `InteractCommand(Actor, TargetKey)`, with `TargetKey` = the door piece's `pce_` ID. `InteractionSystem.Handle` (`Systems.cs:251-273`) gains a first branch: when `EntityId.TryParse(TargetKey)` yields a `Piece`, it dispatches `OperatePieceDoor(pieceId, actor, Open: null)` (a toggle) and returns the result.
- **NPC.** A new internal command `OpenDoor(string DoorKey, EntityId Actor)`, handled by `InteractionSystem` (nav_codefit §12.2; nav_minimal §3.2), measures reach from the **NPC's** body (`DistanceTo(closed box) ≤ InteractReachMm`, 1600). An authored `door.*` key is permitted when the NPC has an errand (§15) or is a companion (navigation's capability; both candidates allow it): it dispatches `SetWorldFlag(cell, flag, 1)` and publishes `DoorToggled(actor, key, true, tick)`. A `pce_` key dispatches `OperatePieceDoor(pieceId, actor, Open: true)`. NPCs open and never close.
- **`OperatePieceDoor(EntityId PieceId, EntityId Operator, bool? Open)`** (internal, to `BuildingSystem`) checks in order: the piece is an intact door; `CanOperate`; the operator's body is within `InteractReachMm` of the closed box (`Systems.cs:262-264`); closing is refused while **any** body (player, NPCs including companions, living creatures) overlaps the closed box; a request for the current state is a no-op. On success it sets `DoorOpen`, rebuilds `ClosedPieceDoors` and publishes `DoorToggled(Operator, pieceId.Value, open, tick)` (`src/World/Runtime/Events.cs:25`). It neither increments `StructureSequence` nor dispatches `RebuildNavigation`: doors are gates evaluated at query time (§21.1).
- **Authored doors close only when every body is clear.** NPCs now walk through them, so the close refusal (`Systems.cs:267-268`) extends from the player's body to every body, closing the edge case nav_minimal §3.2 records. The text is unchanged.

### 7.4 Presentation

A hinged leaf swings ±90° towards its local +Z side, following the `HollowView` door pattern (`HollowView.cs:219-257`) but keyed by piece ID. Its camera collider turns with it. There are no locks or keys in M7.

---

## 8. Resources and crafting

### 8.1 Decision

Placement, repair and dismantle move items through the existing internal commands (`ExchangeItems` spends, `GrantItem` refunds). There is no construction currency, upkeep, build points or progress; placement is instant, like an M3f craft. Existing materials cannot supply building: the ash stand gives one haft a day (`content/nodes/wood/ash_stand.yaml`), and iron is finite (the seam's three strikes have `respawn: none`, `content/nodes/ore/iron_seam.yaml`; the den cache's three billets are one-shot, `content/loot/den_cache.yaml`). M7 therefore adds **one material and one renewable node through the unchanged M3f model, and no piece costs iron**:

```yaml
# content/items/material/timber.yaml
id: item.material.timber
kind: item
schema: 1
display_key: item.material.timber.name
tags: [item]
name: Rough Timber
notes: Split deadfall - what M7's pieces are built from (content/pieces). Traders do not buy it.
category: material
stack_max: 20
weight: 0.5
value_base: 2
rarity: common
no_sell: true            # explicit, not reliant on floor(2 x sell_ratio) = 0 (src/Domain/Items/Items.cs:230-231; field: ItemContent.cs:115)

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

The region gains `{ name: deadfall, node_ref: node.wood.deadfall, position_m: [84, 118] }` under `nodes:` (`ashen_hollow.yaml:157-159`): cell A (`r_0_0:c_00_01`), on the 7.15 m terrace, 5.0 m from the area's corner (87, 114) and 12.6 m from Sel (72, 122). It yields 24-32 timber a world day (`content/config/time.yaml:10`) plus the survival bonus (`Crafting.cs:117-121`); the §11 workshop costs 31, about a day's gathering.

### 8.2 The baseline consequence

Fixed nodes enter the generator (`src/Application/GameSession.cs:102-105`) and `CellBaseline.Digest` covers them (`src/World/Generation.cs:144-162`), so the deadfall changes the worldgen fingerprint and cell A's `baseline_hash`. M7 freezes the e10d2c4 running fingerprint as `private const string M6LayoutFingerprint = "sha256:…"` beside `M3LayoutFingerprint` (`GameSession.cs:77`; the value is read from any v13 save's `manifest.json` and recorded in `docs/M7_STATUS.md`), registers `new BaselineTransition("M7: the deadfall by the crossing", M6LayoutFingerprint, generator.Fingerprint)` with `DropVanishedTargets: false`, and keeps the M3f and M6 transitions, which target the running fingerprint (`GameSession.cs:108-117`). The transition test loads a v13 save whose cell A holds both authored-door flags and the waystation chest record, and asserts `CellsRebased` names the M7 transition and both carry unchanged.

The deadfall is **decided**: it is what makes repair sustainable. The recorded fallback, if persistence refuses any M7 baseline change, is an authored container `container.timber_stack` at the same point with a one-shot 80-timber loot table (containers are layout, not baseline, `RegionLayout.cs:69-70`).

### 8.3 Transactions

| Operation | Items | Path |
|---|---|---|
| Place | spend `cost` (check 14's `Takes`) | `ExchangeItems(takes, null, 0, 0)`, **before** any building state changes |
| Repair | spend ⌈count × missing × repair_cost_percent / (100 × health_max)⌉ per line, as (a·b + c − 1) / c | `ExchangeItems`, before the health change |
| Dismantle | grant ⌊count × refund_percent / 100⌋ per line, when > 0 | `GrantItem(itemId, n, Quality.Standard)` after removal. It never refuses: pack first, else the ground at the body (`Items.cs:367-374`). It merges into existing stacks before minting (`Items.cs` `Put`, `MergeInto`), so a refund to a partial timber stack mints no ID |
| Destroy | nothing refunded | - |

### 8.4 `config.building` (`content/config/building.yaml`)

```yaml
id: config.building
kind: config
schema: 1
display_key: config.building.name
tags: [config]
notes: Building v1 (M7). Socket/snap on a 3 m lattice, quarter turns, one storey (D-08; owner rulings 1-2).
module_m: 3.0                # save-locked (pieces store absolute anchors)
rotation_step_deg: 90        # quarter turns only in M7 (owner question 1)
place_reach_m: 6.0           # body centre to the nearest point of the piece's bounds
pad_max_relief_m: 0.25       # highest minus lowest ground under a pad, sampled every 0.25 m
refund_percent: 50           # of each cost line, rounded down, when taken down
repair_cost_percent: 100     # of each cost line, scaled by the health missing, rounded up
navigability_radius_m: 0.35  # the reference body for navigability; must equal base_speeds.body_radius_m
protection:                  # §16.3
  spawn_point_m: 3.0
  npc_site_m: 1.5
  site_m: 1.5                # authored containers, stations and nodes
  structure_margin_m: 1.5    # round every authored door's closed box, switch structure and barrier
  spawner_margin_m: 1.0      # beyond a spawner's disc (and each patrol leg) plus its largest creature's radius
damage:                      # the only sources of piece damage in M7 (§13.1)
  melee: 10
  shot: 2
  formula: 12
```

`BuildingConstants.Problem()` validates in the `CompanionTuning` pattern (`src/Domain/Companions/Companions.cs:64-71`):
- the module is exactly 3000 and the step exactly 90;
- reach is 1000-12 000;
- relief is 0-1000;
- refund is 0-100 and repair is 0-1000;
- the radius equals the body radius;
- protections are ≥ 0 and damages ≥ 1.

---

## 9. Persistence

### 9.1 Where

A `pieces` list, a `structure_seq` scalar and an `npc_errands` list go inside **`entities.msgpack`**, following the schema-6 (`containers`) and schema-8 (`creatures`) precedent. A new `buildings.msgpack` would break every historical fixture's integrity root (`SaveIntegrity.TryReadRoot` requires every `CheckedFiles` name, `src/Persistence/SaveLoader.cs:469`; research/persistence §0 item 2). Pieces, chest contents and errands then share one section, so a quarantine never leaves a cross-section dangling reference (`docs/PERSISTENCE.md:252`).

### 9.2 DTOs (`src/Persistence/SectionCodec.cs`)

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
}
[MessagePackObject] public sealed class NpcErrandDto
{
    [Key("npc_id")] public string NpcId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("phase")] public string Phase { get; set; } = "";          // "to_work" | "at_work" | "to_home"
    [Key("piece_id")] public string? PieceId { get; set; }
    [Key("work_owner")] public string? WorkOwner { get; set; }
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
    [Key("route")] public NavRouteDto? Route { get; set; }           // navigation's DTO (nav_codefit §13.1)
    [Key("stuck_ticks")] public int StuckTicks { get; set; }
}
// EntitiesSectionDto (SectionCodec.cs:202-218) gains, each "Required from schema 14. The 13 -> 14 step gives older saves none / 0.":
[Key("pieces")] public PieceDto[]? Pieces { get; set; }
[Key("structure_seq")] public long? StructureSeq { get; set; }
[Key("npc_errands")] public NpcErrandDto[]? NpcErrands { get; set; }
```

- On decode, a null for any of the three fields throws `FormatException("… (required from schema 14)")`: corrupt, not defaulted (the pattern at `SectionCodec.cs:399-416`).
- Decode validates ranges:
  - rotation is 0..3;
  - health is ≥ 1;
  - `structure_seq` is ≥ 0;
  - the phase key is known;
  - facing is 0..359 999;
  - `to_work`/`at_work` carry both `piece_id` and `work_owner`;
  - `to_home` carries no `piece_id`.
- `EncodeEntities`' `Prove` loop (`SectionCodec.cs:478-494`) adds each piece's and errand's host cell.
- Rows are sorted: pieces by `instance_id`, errands by `npc_id`, both ordinal. The output is byte-stable.

**Size.** About 200 bytes per piece with string keys, so the 256-piece cap is about 51 KB. That is inside PERSISTENCE's building budget (`docs/PERSISTENCE.md:553`).

### 9.3 World delta (`src/World/WorldDelta.cs`)

Stores: `SortedDictionary<EntityId, PieceRecord>` (`EntityId` is ordinal-`IComparable`), `long _structureSequence`, `SortedDictionary<string, NpcErrandRecord>` (ordinal).

| Addition | Detail |
|---|---|
| `DeltaSnapshot` | init properties `Pieces` (by ID), `StructureSequence`, `NpcErrands` (by NpcId) (`WorldDelta.cs:188-200`) |
| internal mutators | `PlacePiece(record, seq)` (registers, sets seq); `SetPiece`; `RemovePiece(id, seq)` (retires, sets seq); `SetStructureSequence`; `SetNpcErrand`; `RemoveNpcErrand`; `ReleaseContainer(key)` (removes a record, retiring **only** its `cnt_`, for the spill). Each is reached only through a `RuntimeState` wrapper that `Require`s `Structures`, `NpcErrands` or, for `ReleaseContainer`, `WorldItems` (`RuntimeState.cs:274-277` pattern) |
| public readers | `Piece(EntityId)`, `PiecesIn(CellKey)`, `Pieces`, `StructureSequence`, `NpcErrand(string)`, `NpcErrandsIn(CellKey)`, all on `WorldDelta_ExposesNoPublicMutation`'s allow-list (`ArchitectureTests.cs:61-79`) |
| `TakeSnapshot` | stamps pieces and errands with `Baseline(host).Digest` as created rows are (`WorldDelta.cs:505-508`). Pieces retire only by dismantle or destroy; errands when the NPC is back at the site pose (§15.3). No cell record; `dirty_reasons` unchanged (`WorldDelta.cs:489-494`) |
| `FromSnapshot` | cells, entities, created, **`SetStructureSequence`**, **pieces**, containers, creatures, **npc_errands** (the sequence first, since `TryApplyPiece` checks ID timestamps against it) |
| `TryApplyPiece` | parseable host cell whose baseline hash matches; kind `Piece`; valid `DefId` shape; owner kind `Character`; rotation 0..3; health ≥ 1; `HostCell == CellKey.OfWorld(X/1000.0, Z/1000.0)`; ID timestamp ≤ `StructureSequence`; not seen or registered. Then register |
| `TryApplyContainer` | a `container.pce_*` key needs its storage piece present ("its chest is gone") and `InstanceId == Derived(Container, piece.InstanceId.Timestamp, "unnamed.piece-container/v1", piece.InstanceId.Value)` ("not its chest's identity"). Its host cell is the **site point's** (`Items.cs:513-523`), proven independently of the piece's |
| `TryApplyNpcErrand` | host cell and hash; valid `NpcId` shape; §9.2's phase invariants; a named piece present, kind `Piece`; at most one `to_work`/`at_work` per piece (a later duplicate by NpcId is rejected and reported) |
| `EffectiveCellDigest` | per host cell, pieces by ID (id, def, x, z, rotation, owner, health, door_open), then errands by NpcId (npc, phase, piece ?? "-", owner ?? "-", x, z, facing, the route's canonical fields, stuck); tag `unnamed.effective-cell/v1` → **v2** (`WorldDelta.cs:587`) |

`Simulation.StateDigest` adds `World.StructureSequence`, and its tag moves `unnamed.simulation/v1` → **v2** (`Simulation.cs:360`). `StateDump.Render` picks up the three new `DeltaSnapshot` properties by reflection (`src/Application/StateDump.cs:25-46`).

### 9.4 Load pipeline (`src/Persistence`)

- **`SaveLoader.ResolveDefinitions`** (`SaveLoader.cs:206-359`):
  - each piece's `DefId` is resolved; a discarded definition drops the piece, reported as the loss "piece {id}: '{def}' was removed with no replacement; dropped";
  - each errand's `NpcId` is resolved; a discarded NPC drops the errand as a loss, and two errands resolving to one NPC keep the lower original key;
  - the rebuilt `DeltaSnapshot` at `SaveLoader.cs:356-358` **must** copy `Pieces`, `StructureSequence` and `NpcErrands`. This is the known hand-listing pitfall (research/persistence §0 item 5).
- **`ProveBaselines`** (`SaveLoader.cs:380-389`) calls `Check(host, hash)` for each piece and errand.
- **`SemanticRebase.Apply`** (`BaselineTransitions.cs:94-117`) moves pieces and errands "as they are", like created instances. The returned snapshot copies all three properties.
- **Migration.** `SchemaV13ToV14` reads a frozen `V13.EntitiesSection` and writes the current DTO with `pieces = []`, `structure_seq = 0` and `npc_errands = []`.
  - `Sections/SchemaV13.cs` holds the schema 9-13 entities shape, and `SchemaV8ToV9` (`Migrations.cs:534-582`) is repointed to write it.
  - The factions design freezes `V13.Player` in the same file and repoints `SchemaV12ToV13` (C_factions §14.4).
  - It is **one** step, shared with factions and navigation.

### 9.5 Fixtures

- `M2Fixtures.Historical.World` (`tests/M2.Probe/M2Fixtures.cs:112-248`) gains, in its existing ten cells (`CellsMatched` stays 10): a pad; a wall at 150/200; a doorway with an open door; a storage piece whose derived-ID container holds 3 items; a piece owned by a **different** `chr_` (the only way to reach "not yours" in single player); `structure_seq = 9`; and a `to_home` errand for a new fixture NPC `npc.fixture.smith`. Piece IDs use the derivation with `ts ≤ 9`, so `TryApplyPiece` accepts them.
- The writer pack `Fixtures/content-0.1.7/` adds the pieces, timber and `npc.fixture.smith`. The current pack renames one piece through `_aliases.yaml` (`piece.fixture.old_wall: piece.fixture.wall`), proving the rename reaches the piece record.
- `CanonicalState.Render` gains the three fields. Every older `expected.json` gains only `"pieces": []`, `"structure_seq": 0` and `"npc_errands": []`, reviewed line by line (`tests/Persistence.Tests/Fixtures/README.md:12-24`).

### 9.6 Where derived state is rebuilt, and the load audit

`Simulation`'s constructor (`Simulation.cs:100-145`) is the single rebuild point for both a new game and a load:

```
_building   = new BuildingSystem(_context, _state.Claim(nameof(BuildingSystem), StateSlice.Structures), player.Id);   // after _crafting
_navigation = /* the navigation design's system and slice */
_npcs       = new NpcSystem(_context, _state.Claim(nameof(NpcSystem), StateSlice.Npcs, StateSlice.NpcErrands));
... _state.RequireEverySliceOwned(); _effects.Seed(...);
_building.Populate();      // Space, door cache, socket index, footprints; health clamp; StructureAudit
_navigation.Build();       // reads StructureFootprints and Space
_npcs.Populate();          // bodies at site or errand pose; errand repair
_companions.Populate(); _creatures.Populate(); _tiers.Settle();
```

`Simulation.StructureAudit` is `ImmutableArray<StructureConflict(string Subject, string Problem)>`. It is building's list followed by `NpcSystem`'s errand repairs:
- loaded pieces that are now off-lattice, outside every area, over authored or protected ground, or "unsupported" (a mount whose provider the definition pass dropped). These are **kept and reported**, because player work is never silently discarded. The authored layout is not in any `baseline_hash` (research/persistence §0 item 4).
- a `health_current` above a retuned `health_max`, which is clamped;
- an errand whose piece is not an intact station, which becomes `to_home` with no route;
- an `at_work` errand whose body is not at the anchor, which becomes `to_work`.

`Populate` publishes nothing, because a load publishes nothing (`tests/Application.Tests/DeterminismAndViewTests.cs:128-143`).

### 9.7 The place → save → quit → reload test

`Building_RoundTripsThroughSave_AndNavigationDerivesIdentically`: place the §11 workshop on a new game, save through `GameSession`, dispose it, boot a fresh `GameSession` from disk, and assert equal rows (ID, def, pose, owner, health, door), equal `StructureSequence`, 0 `StateDump.Compare` differences between the dumps before save and after load, an equal navigation grid digest (the navigation design's), and element-wise equal `Simulation.Space.Blockers`.

---

## 10. Destruction and removal

M7 requires removal: dismantle (player, refund) and destroy (health 0, no refund). Both use one core, `RemoveCore(piece, kind)`.

### 10.1 Dismantle

`DismantlePieceCommand(EntityId Actor, EntityId PieceId)`. Refusals in order: the actor ("dead", "busy"); "there is no such piece"; not the owner ("that is not yours to take down"); reach (check 6); dependents ("take the door down first" / "take down what stands on it first"); a storage piece whose record holds any item ("empty the chest first").

Dependents come from the socket index: a **doorway**'s is its door; a **pad**'s are the furniture on its square and every wall-type piece on its edges whose **other** adjacent square has no intact pad. Nothing else has dependents; roofs never are (invariant 4). Removing blockers cannot disconnect anything, so dismantle needs no navigability check.

### 10.2 `RemoveCore`

1. A station with a `to_work`/`at_work` errand: dispatch `EndWork(npcId, reason)` (§15.2).
2. A storage piece with a record: dismantle dispatches the existing `DiscardContainer(key)` (`Items.cs:376-381`; the record is empty by the last refusal); destroy dispatches `SpillContainer(key)` (§10.3).
3. `seq = StructureSequence + 1`; `State.RemovePiece(_owner, id, seq)` retires the `pce_`.
4. `Rebuild()`, then `RebuildNavigation(bounds, kind, seq)`.
5. Publish `PieceRemoved(PieceId, DefId, Actor, ImmutableArray<CostView> Refund, Revision, Tick)` or `PieceDestroyed(PieceId, DefId, Source, Revision, Tick)`, then `StructuresChanged`.
6. Dismantle only: `GrantItem` per refund line.

### 10.3 Destroy (health reaches 0)

This is `RemoveCore` with `Destroyed`, and it differs from dismantle in three ways:
1. **No refund and no owner or reach check.** The rule destroys, not a player.
2. **Mount cascade.** Every intact piece whose mount would lose its **last** provider is removed first, recursively, in piece-ID order, each with its own `PieceDestroyed`. With M7's damage sources the only reachable case is a destroyed doorway taking its door: the door falls with its frame. Roofs never cascade.
3. **Chest spill.** `SpillContainer(string Key)` is a new internal command to `InventorySystem`:
   - it moves each `ContainerItem`, in record order, to the ground at the container site point, one created stack each, **keeping its item ID**. It uses `State.PlaceItem` at cell-relative cm, the drop pattern (`Items.cs:584-593`). `PlaceItem` does not touch the registry (`WorldDelta.cs:360-367`), so the IDs stay live;
   - it then calls `ReleaseContainer(key)`, which retires only the `cnt_`.

   `RemoveContainer` alone would retire every item ID (`WorldDelta.cs:390-400`), and that would be item loss.

---

## 11. The M7 building acceptance scenario: "the Crossing Workshop"

A headless Application test, `tests/Application.Tests/BuildingAcceptanceTests.cs`, drives it through `GameSession` and the existing harness with a fixed seed. The same beats are recorded as a windowed `--build-shots <dir>` mode, following the `DeltaShots` pattern.

**Start.** `Simulation.Start` with a new character's `PlayerRecord` plus:
- 45 `item.material.timber` (stacks of 20, 20 and 5, all standard);
- 1 `item.material.iron_ingot` and 1 `item.material.ash_haft`;
- knowledge of `recipe.smithing.march_spear`;
- a recruited Tavar Orr (`CompanionRecord`, order `Wait`, up) at (100.5, 108.5) facing 180°, built the way `CompanionTests` builds a recruited companion.

The carried weight is about 28.5 kg, under the 30 kg base limit. **Quicksave S0.**

0. **Replay proof (M1).** Load S0 a second time and replay step 1's command log at the same ticks. Assert:
   - the raw `StateDigest` is equal after step 1, because step 1 mints nothing;
   - the piece ID list equals `Derived(Piece, 1..19, owner)` element by element.

1. **Build (all accepted).** Anchors in mm:
   - **Pads** on squares (33, 33), (34, 33), (33, 34), (34, 34): (100500, 100500), (103500, 100500), (100500, 103500), (103500, 103500). Pad (33, 33) spans x 99-102, z 99-102, straddles all four cells, and is hosted in `c_01_01`.
   - **Edges:** south doorway (100500, 99000) r0, whose opening x 99 700-101 300 straddles x = 100, and wall (103500, 99000) r0; north walls (100500, 105000) r0 and (103500, 105000) r0; west walls (99000, 100500) r1 and (99000, 103500) r1; east walls (105000, 100500) r1 and (105000, 103500) r1.
   - **Door** (100500, 99000) r0; **roofs** on the four squares (each has two supporting edges).
   - **Bench** on (33, 34) r3: part x [99 500, 100 100], z [103 000, 104 000] (straddles x = 100); site (99 800, 103 500); work anchor (100 750, 103 500), facing 270° (towards the bench).
   - **Chest** on (34, 34) r0: part x [103 000, 104 000], z [104 100, 104 700]; site (103 500, 104 400).
   - **Cost** 4 + 14 + 2 + 1 + 4 + 2 + 4 = **31 timber**, asserted exactly. 19 placements; `StructureSequence` = 19.
2. **Overlap refused.** A wall at (100500, 99000) r0 is refused by `Slot`, "a Timber Doorway already stands there"; the digest is unchanged.
3. **Collision.** Walking north from (100.5, 97.0), the player is stopped by the closed door at z = 98.45, opens it (`InteractCommand` with the door's `pce_`) and walks in. `Aim` from (100.5, 101.0) facing 270° stops at x ≈ 99.2, the west wall's face.
4. **Craft at home.** At (100.6, 102.4), 1.36 m from the bench site, `CraftCommand(march_spear)` is accepted with no authored anvil in reach.
5. **Assign (H2 doors).** The player leaves through the doorway, closes the piece door, walks to the smithy, opens `door.forge_shed` (`ashen_hollow.yaml:144`), and within 1.95 m of Kera (61.6, 139.6) issues `AssignWorkerCommand(kera, benchId)`: accepted, `WorkerAssigned`. The player leaves, closes `door.forge_shed` (every body clear), and waits at (45, 140).
6. **In, through, around.** Kera, at a walk (1.6 m/s), opens `door.forge_shed` (`DoorToggled(Actor = NpcSystem.InstanceIdOf(kera), …, true)`), walks round the outside, opens the piece door (`DoorToggled` with her ID and the `pce_` key), and walks through the doorway to the anchor. Assertions:
   - body (x, z) **exactly** (100 750, 103 500), facing exactly 270 000;
   - arrival within ⌈1.25 × planned path length / 1.6 m/s × 20⌉ + 40 ticks (L12's rule; 40 for the two doors);
   - no teleport event;
   - **seams:** her cell crosses at least one seam outside the footprint, and crosses z = 100 exactly **once inside** the footprint union [98 800, 105 200]², from `c_01_00` to `c_01_01`. The geometry forces it: the doorway admits body centres only at x ∈ [100 050, 100 950], z < 99 000, and the anchor is at z = 103 500;
   - **RK-14 "never exits and re-enters":** every position from entering the opening to arrival lies inside the footprint union;
   - the plan (a digest navigation exposes) is recorded as P1; the expected deterministic choice is the shorter west side.
7. **Navigability refused (the vestibule).** From (100.5, 94.5): pad (100500, 97500) on square (33, 32) and walls (99000, 97500) r1 and (102000, 97500) r1 are accepted; wall (100500, 96000) r0 is **refused** by `Navigability`, "that would cut Kera Voss's work place off", because it would seal workshop and vestibule together. The two walls, then the pad, are dismantled (refunds 1 + 1 + 0).
8. **Damage, repair, destruction** (Kera at work).
   - From (100.5, 105.6) facing 180°, three sword blows on the north wall (100500, 105000): `PieceDamaged` ×3, source `melee`, health 170. `RepairPieceCommand` costs ⌈2 × 30 / 200⌉ = 1 timber (health 200); one more blow leaves **190**, kept through step 10.
   - **Chest (C1):** the player stores 2 timber (`MoveItemCommand` to `container.pce_…`; the record materialises with its derived `cnt_`), takes all, and stores 2 again, with no exception and one `cnt_` throughout.
   - From (103.5, 102.9) facing 0°, ten blows destroy the chest: `PieceDestroyed`, `StructureSequence` + 1; the 2 timber lie at (103.5, 104.4) with their IDs unchanged, and the player picks them up.
9. **Route alteration (west).**
   - A second chest goes on square (34, 33) r1 (part x [104 100, 104 700], z [100 000, 101 000]); the player stores 2 timber in it.
   - P2 is recorded: a read-only plan of Kera's route from her anchor to her site, from navigation's pure planner on `Simulation.Navigation`.
   - Pads (97500, 100500) and (97500, 103500) (squares (32, 33), (32, 34)) and wall (96000, 100500) r1 are placed. With the player standing at (96.0, 103.5), wall (96000, 103500) r1 is refused, "someone is standing there"; from (97.5, 103.5) it is placed. The line runs along x = 96 from z 98.8 to 105.2.
   - The player releases Kera from (101.5, 102.5). P3, a read-only plan in the same tick, differs from P2 and does not cross the segment x = 96, z ∈ [98 800, 105 200]; her first route equals P3. After 300 ticks she is outside the footprint, and the player, at (102, 102) inside, orders Tavar to follow.
10. **Save, quit, reload.** 100 ticks after the order, with Kera mid-walk home and Tavar mid-route, the player quicksaves. W1 continues 600 ticks (D1, S1); a fresh `GameSession` loads the save as W2 and continues 600 ticks (D2, S2). Assertions:
    - D1 == D2; `StateDump.Compare(S1, S2)` finds 0 differences; the dump of the save equals the dump just after load;
    - every piece row is equal (IDs, defs, poses, owners, health with the north wall at 190, doors); `StructureSequence` is 31; Kera's errand and the chest's 2 timber are equal; the navigation digest is equal;
    - in both worlds Tavar ends inside [99 200, 104 800]², having passed through the doorway opening, with no `CompanionCaughtUp` (`src/World/Runtime/Companions.cs:28`): the companion "in and through", repeated after the reload;
    - W1 then continues until Kera's errand is retired with her body exactly at her site pose.
11. **Replay.** From S0, steps 1-9's command log replayed at the same ticks gives an equal replayable `StateDump` and equal piece ID lists. The H4 guard test (§4) reuses this log.

**Timber:** 45 → build −31 = 14 → vestibule −5 = 9 → refunds +2 = 11 → repair −1 = 10 → chest cycle and spill ±0 = 10 → second chest −2 and stored −2 = 6 → pads and walls −6 = 0 carried, 2 in the chest.

**`StructureSequence`:** 19 after step 1; 20-22 vestibule placements and 23-25 its dismantles; 26 the chest destroyed; 27 the second chest, 28-31 pads and walls.

Shot and formula damage are proved in their own tests (§22), with the kit they need.

---

## 12. Ownership

- `Owner` is the `chr_` of the placing character; transfer is out of scope (`docs/WORLD_ARCHITECTURE.md:418`).
- **It gates:** dismantle, repair, assign and release (refused unless the actor owns the piece); chest moves (`InventorySystem.Check`, `Items.cs:450-460`, refuses `site.Owner ≠ actor` with "that chest is not yours"); piece-door operation (`CanOperate`, §7.2). **It does not gate damage.**
- **Separation (ruling 3).** Ownership is a building fact, not legal status, jurisdiction, faction property or a reputation input. Nothing in M7 derives crime, hostility, standing or attack legality from it, and building dispatches no `RecordAct` (C_factions §13).

---

## 13. Health, damage, repair

### 13.1 The explicit damage-rule table (the only M7 sources)

| Key | Producer (one code point) | Rule | Amount |
|---|---|---|---|
| `melee` | `CombatSystem`'s melee branch, at the active window's last tick, when `struck.IsEmpty` (`src/World/Runtime/Combat.cs:495-507`) | `FirstStop(body, attack.ReachMm)` (§13.2) yields a piece part: dispatch `DamagePiece(pieceId, 10, "melee")`, which replaces that swing's `AttackMissed` | 10 |
| `shot` | `CombatSystem.Loose` (`Combat.cs:511-517`) with a new `PieceDamageSource` parameter; ranged weapons pass `shot` | the trace found no creature, and its `FirstStop` yields a piece part | 2 |
| `formula` | the same `Loose`, called from `src/World/Runtime/Magic.cs:114` with `formula` | as `shot` | 12 |

There are no creature, weather, fire, raid, time or off-screen sources (off-screen damage is tier-illegal anyway, review C-8, `docs/reviews/ARCHITECTURE_AI_SESSION_REVIEW.md:217-239`). A boar charge ending against a piece still stuns the boar (`Creatures.cs:589-594`) and damages nothing. Reserved, unbuilt rows: `creature_charge`, `creature_blow`, `fire`, `raid`. Pads and roofs emit no blocker, so nothing reaches them. Hitting a piece practises no skill and awards no XP.

### 13.2 Which blocker a strike hit: `FirstStop` (exact, reuses today's trace)

```
// CombatSystem (World), private. Trace (Combat.cs:523-545) is rewritten to call it; Trace's outputs are unchanged.
(double ClearMm, ImmutableArray<Blocker> Hit) FirstStop(Body from, long lengthMm):
    (dx, dz) = the facing's Sin/Cos exactly as Combat.cs:527-528
    walls    = _context.SightWalls()                       // Space.Blockers ∪ ClosedDoors(), §21.4
    if !walls.Any(Crosses(from, from + d·length)): return (length, [])
    bisect exactly as Combat.cs:533-542 → clear, reach      (reach − clear ≤ 10 mm)
    return (clear, walls.Where(b => b.Crosses(from, from + d·reach)))   // list order
```

Whether a segment prefix crosses a blocker is monotone in its length, so every blocker in `Hit` is entered within (clear, reach], at most 10 mm. **Piece rule:** a piece is struck only if **every** blocker in `Hit` is a piece part, and then the lowest (piece ID, part index); an authored blocker, door or barrier in `Hit` shields and wins ties. This fixes v1's nearest-to-body melee choice and its 20 mm stop radius, and adds no float-order dependence: it reuses the existing `Sin`/`Cos` and bisection, and pieces cannot strictly overlap authored geometry (check 10).

### 13.3 `DamagePiece` (internal command, to `BuildingSystem`)

```
record DamagePiece(EntityId PieceId, int Amount, string Source) : InternalCommand
Handle: piece ?? "there is no such piece"; Amount < 1 → "no damage"
    to = max(0, HealthCurrent − Amount)
    to == 0 → RemoveCore(piece, Destroyed, Source)   // §10.3
    else SetPiece(piece with { HealthCurrent = to }); Publish(PieceDamaged(id, def, Amount, to, Source, Now))
```

Damage changes no footprint, so it causes no revision change and no navigation rebuild. A row with `health_current` 0 never exists. On load, health above a retuned `health_max` is clamped and reported (§9.6).

### 13.4 Repair

`RepairPieceCommand(EntityId Actor, EntityId PieceId)`. Refusals in order: the actor ("dead", "busy"); no such piece; not the owner ("that is not yours to mend"); reach (check 6); full health ("it needs no repair"); materials (check 14 over the scaled cost, §8.3). On success it dispatches `ExchangeItems`, sets `HealthCurrent = health_max` and publishes `PieceRepaired(PieceId, From, To, Tick)`: one command, one cost, no partial repair. A wall at 170/200 costs ⌈2 × 30 / 200⌉ = 1 timber.

Health in `melee` blows: walls and doorways 200 (20), door 120 (12), chest 100 (10), bench 300 (30).

---

## 14. The storage chest and the crafting station

### 14.1 Chest: the container model reused

**Site.** Each intact storage piece yields a synthesized `ContainerSite`: key `"container." + pieceId.Value.ToLowerInvariant()` (a valid definition-ID shape, since the second segment holds `_`, `src/Domain/DefinitionId.cs:19-20`, which `Materialize`'s `DefinitionId.Parse` needs, `Items.cs:518`); `LootTableId` `""`; the world point of `container.at_m`; 12 stack slots; `InstanceId` = `Derived(Container, seq, "unnamed.piece-container/v1", pieceId)`; `Owner` = the piece's owner.

**Reuse:** `SystemContext.FindContainer` (`Systems.cs:51-52`) checks authored, corpse, merchant, then **piece** sites. `InventorySystem.Baseline` (`Items.cs:494-510`) returns empty for `LootTableId == ""` on a non-merchant (today it would throw on the loot lookup). `Materialize` (`Items.cs:513-523`) uses `site.InstanceId` when present instead of minting. `Check` adds the owner refusal (§12).

**The C1 fix.** `InventorySystem.Take`'s corpse clause (`Items.cs:558-562`) becomes:

```csharp
if (items.IsEmpty && _context.Setup.Layout.FindContainer(site.Key) is null && site.InstanceId is null)
```

A piece chest's record is therefore never removed by emptying while its piece stands, which matches "A changed container stays recorded even if its contents come back" (`WorldDelta.cs:510-511`). Without the clause, emptying retires the derived `cnt_`, which the registry only tombstones (`src/EntityRegistry/EntityRegistry.cs:133-142`); the next deposit re-registers the same ID and `CreateEntity` throws (`EntityRegistry.cs:50-51`), but only in the unsaved run, so save-then-continue would differ from continue.

Only `RemoveCore` retires a piece chest's `cnt_`: `DiscardContainer` on dismantle, `ReleaseContainer` on destroy. Corpse and merchant behaviour is unchanged, because their `InstanceId` is null.

**Persistence** is the existing schema-6 `ContainerRecord`. Its host cell is the site point's cell, and the load check is in §9.3.

### 14.2 Station: the M3f `anvil` kind reused

The kind is `anvil`, from the existing March Spear recipe (`content/recipes/smithing/march_spear.yaml:8`): a bench lets a character who knows it make spears at home, while the billet still needs the smithy's forge. **No new recipe; no iron in the bench** (critique M4). Each intact station piece yields `StationSite("station." + pieceId.Value.ToLowerInvariant(), "anvil", part centre)` (`RegionLayout.cs:46`); the §11 bench's is (99 800, 103 500). `SystemContext.Stations()` returns authored sites, then piece sites by ID; `CraftingSystem` uses it instead of `_context.Setup.Layout.Stations` (`Crafting.cs:168-169`), and presentation reads `Simulation.Stations`. A removed bench leaves no site, and the next craft gets the existing "no anvil in reach".

---

## 15. NPC work-anchor assignment (scope item 5)

### 15.1 Data

- **The anchor.** The station definition gives `work_anchor_m` and `work_facing_deg`:
  - world anchor = piece anchor + R_r(local);
  - facing = (local + r·90°) mod 360°.
- **The NPC.** `NpcDefinition` (`src/Domain/Social/Social.cs:10`) gains `ImmutableArray<string> WorksAt { get; init; }`.
  - It is authored as `works_at: [anvil, forge]` on `content/npcs/ashen_hollow/kera_voss.yaml` only.
  - `SocialContent` parses it explicitly, next to the existing field checks (`src/Content/SocialContent.cs:77-89`). `BLD005` lints each kind.
- **The errand**, in `src/World/WorldDelta.cs`, is the single source of truth for an assignment:

```csharp
public enum NpcErrandPhase { ToWork, AtWork, ToHome }
/// A named NPC away from their place (M7): walking to a work anchor, at it, or walking home. Anchored to the NPC's home cell with
/// absolute mm, the creature-record convention (WorldDelta.cs:154-185). Absent means the NPC stands at their site: the baseline.
public sealed record NpcErrandRecord(string NpcId, string HostCell, NpcErrandPhase Phase, EntityId? PieceId, EntityId? WorkOwner,
    long XMm, long ZMm, int FacingMdeg, string? BaselineHash = null)
{
    public NavRoute Route { get; init; } = NavRoute.None;   // navigation's persisted route type (nav_codefit §13.1)
    public int StuckTicks { get; init; }
}
```

**Invariants:** `ToWork`/`AtWork` name an intact station owned by `WorkOwner`; `ToHome` has a null `PieceId` and keeps `WorkOwner` for door permission on the way out; one errand per NPC and at most one `ToWork`/`AtWork` per piece; companions never have one.

**Why `entities.msgpack`, not `player.msgpack`** (the critique's H2 and nav_minimal §3.1 suggest the player section): an NPC's position is world state, and the companion precedent (`src/World/PlayerState.cs:46-62`) rests on party membership, which a worker lacks. Moving world bodies already persist as `CreatureRecord`s anchored to a fixed home cell in absolute mm, with no record at baseline (`WorldDelta.cs:154-185`). And the errand names a piece in the same section, so a quarantine drops both together.

### 15.2 Commands

```csharp
public sealed record AssignWorkerCommand(EntityId Actor, string NpcId, EntityId PieceId) : GameCommand(Actor);
public sealed record ReleaseWorkerCommand(EntityId Actor, string NpcId) : GameCommand(Actor);
internal sealed record BeginWork(string NpcId, EntityId PieceId, EntityId Owner, long AnchorXMm, long AnchorZMm, int FacingMdeg) : InternalCommand;
internal sealed record EndWork(string NpcId, string Reason) : InternalCommand;
public sealed record WorkerAssigned(string NpcId, EntityId PieceId, long AnchorXMm, long AnchorZMm, int FacingMdeg, long Tick);
public sealed record WorkerReleased(string NpcId, EntityId PieceId, string Reason, long Tick);
public sealed record NpcArrivedAtWork(string NpcId, EntityId PieceId, long Tick);
public sealed record NpcReturnedHome(string NpcId, long Tick);
```

`BuildingSystem` handles both player commands (like `InteractionSystem`, it validates and dispatches); `NpcSystem` commits.

**Assign refusals, in order:**
1. the actor: "dead";
2. the NPC is not in `State.Npcs`: "there is no one called X here";
3. the NPC is a companion: "X travels with you";
4. `WorksAt` lacks the station's kind: "Kera Voss does not work an anvil";
5. the player's body is not within `TalkReachMm` (1950; `Systems.cs:67`) of the NPC's body: "Kera Voss is out of reach". You ask in person, which keeps information local (ruling 3);
6. the piece is not an intact station: "there is no such station";
7. the actor does not own it: "that bench is not yours";
8. another `ToWork`/`AtWork` errand names it: "someone already works there";
9. the NPC has a `ToWork`/`AtWork` errand: "Kera Voss already works for you". A `ToHome` errand is replaced;
10. **reachability:** navigation's planner, read-only at command time, finds a route from the NPC's body to the anchor for the errand capability (radius 350; opens authored doors and `Owner`'s piece doors; standing barriers solid, by current flags), else "Kera Voss cannot get there".

Then `BeginWork` is dispatched; `NpcSystem` writes the errand (`ToWork`, route `None`, stuck 0) and publishes `WorkerAssigned`. No footprint changes.

**Release** refuses on the actor, on no `ToWork`/`AtWork` errand at a piece the actor owns ("Kera Voss does not work for you"), and on talk reach. `EndWork(npc, "released")` then sets `ToHome` with a null `PieceId` and route `None` and publishes `WorkerReleased`. Dismantle and destroy dispatch `EndWork` with "dismantled" or "destroyed".

### 15.3 What `NpcSystem` does (it owns bodies and errands)

**`Tick`,** tier A only as for creatures and companions (`Creatures.cs:264`; `Companions.cs:263`), errands in `NpcId` order:
- **Goal:** the anchor for `ToWork`, the site for `ToHome`. The navigation follower yields a target point (walked at `Gait.Walk` with `Kinematics.Step(body, intent, Movement, _context.Space, Obstacles(npcId))`, the companion's obstacle list, `Companions.cs:571-581`), `OpenGate(key)` (dispatch `OpenDoor`, stand this tick), or `Unreachable` (stand and retry at the follower's retry tick). **Never teleport.**
- **Landing rule** (nav_minimal §3.1): within one tick's travel (80 mm at a walk), deflection is scaled to remaining / travel, so the step normally lands exactly.
- **Arrival:** `ToWork` becomes `AtWork` when body (x, z) equals the anchor exactly (`NpcArrivedAtWork`). `ToHome` at the site turns to the site facing; when x, z and facing all equal the site's, the record is **removed** (`NpcReturnedHome`) and the NPC is baseline again. A body left a few mm off keeps its record until exact: store what diverges, never snap.
- **Facing** in `NpcSystem.Tick`'s turning (`Social.cs:149-163`): towards the player while talking, else the work facing when `AtWork`, else the site facing. Walking NPCs face their step; non-errand NPCs are unchanged.
- **Write-through:** body and errand (pose, route, stuck) are written every tick they change, as creatures are.

**`Populate`** places an errand NPC at the errand pose, not the site (`Social.cs:127-138`), then applies the §9.6 repairs.

Kera stays a trader while working: trade and talk reach are measured to her body (`Social.cs:535-550`, `:237`), and her wares stay at her site (`Systems.cs:61-64`), which trade does not reach-check (`Items.cs:459`). There is no production, schedule, wage, housing or hireling: "work" is standing at the bench.

---

## 16. The build area in Ashen Hollow

### 16.1 Data

Region-inline, in `content/regions/ashen_hollow.yaml`, after `stations:` (`:161-163`):

```yaml
# Where the player may build (M7): boxes on the 3 m building lattice, clear of authored geometry, spawns and NPC stands.
# It sits on the four-cell corner (100, 100), so a structure there straddles both seams (ROADMAP M7 exit).
build_areas:
  - { key: build_area.hollow_crossing, box_m: [87, 87, 114, 114], max_pieces: 256 }
```

`RegionLayout` gains `ImmutableArray<BuildAreaSite> BuildAreas`, `record BuildAreaSite(string Key, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, int MaxPieces)`; build areas are layout, not baseline, so need no transition. 87 = 29 × 3 and 114 = 38 × 3 give 9 × 9 = 81 squares (i, k = 29..37); square (33, 33), x and z 99-102, contains (100, 100). The area covers all four cells (`ashen_hollow.yaml:8`): A `c_00_01` (x < 100, z ≥ 100), B `c_01_01`, C `c_00_00`, D `c_01_00`.

### 16.2 Why here

It is the one place near the waystation (35 m from Sel's table, about 60 m from the smithy) where one lattice square contains both seams, which delivers RK-A2's "test with a building straddling four cells" (`docs/WORLD_ARCHITECTURE.md:469`) with one pad. Every square takes a pad (worst relief 228 mm), no quest gate is needed, and widening it is data. The scripted routes that walk through (100, 100) (`src/Presentation/Playthrough.cs:56, 63, 68`; `src/Presentation/Perf/PerfRun.cs:26`) start new games with no pieces.

### 16.3 Clear of everything protected

Distances to the box [87, 114]²:
- **Authored structures:** the nearest are `tree_25` (112, 86), r 0.4, 0.6 m south of the box (`ashen_hollow.yaml:133`), and the survey table ending at x 75.2 (`:83`). None is inside.
- **NPC stands:** Sel (72, 122) is 17 m away; the others are further (`:152-155`).
- **Spawners:** the strays (118, 128) have a 6 m disc; protection 6 + 0.45 + 1 = 7.45 m against 14.56 m to the corner (114, 114) (`content/spawns/hollow/valley_strays.yaml`), and their 8 m wander (`content/config/creature_behaviour.yaml:27`) stays 6.56 m outside. The hound, den pack and patrol routes are further.
- **Sites:** the nearest is the deadfall (84, 118), 5.0 m from (87, 114). Every authored door, switch, barrier and container, and the spawn (30, 158), are far.

**Protected zones (check 11 and `BLD007`)** are, as strict intersection with the piece's bounds:
- the region spawn circle (3 m);
- each NPC site circle (1.5 m);
- each authored container, station and node point circle (1.5 m);
- each authored door's closed box, each switch structure and each barrier footprint, expanded by 1.5 m;
- each spawner's disc of radius `radius + largest member radius + 1 m`;
- each patrol leg, sampled every 500 mm (endpoints included) as circles of that same radius.

---

## 17. Navigability validation

### 17.1 What is checked

> For a candidate that adds solid parts: every reference point satisfied **before** the placement must still be satisfied **after** it, and every point the candidate itself creates must be satisfied after it. The agent is a disc of `navigability_radius_m` (350 mm). The graph is navigation's representation of authored geometry plus the piece set, with **every door (authored or piece, open or closed) and every barrier (standing or lifted) passable**. Bodies are ignored.

**Why.** With all gates passable, the labels are a pure function of content and the piece set, which `StructureSequence` keys, so no flag change, door toggle or preview can make them stale (H4, closed structurally). Every agent that must use a piece door may open it (§7.2); authored doors and barriers are world and quest state, and a barrier only ever lifts (`RegionLayout.cs:14-28`). Bodies move, so they are ignored. A point already unsatisfied does not block building elsewhere.

M9 reuse: `ReferencePoint` carries `OpensPieceDoors` (true for every M7 point). An agent without permission, such as a scheduled NPC in M9, is checked on a second graph with piece doors solid. M7 builds only the all-passable graph (critique M8, partly accepted, Appendix A).

### 17.2 Satisfaction

`ReferencePoint(string Label, ReferenceKind Kind, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, long WithinMm, bool OpensPieceDoors = true)`. A point is a degenerate box. A circular target is its centre with `WithinMm` + the radius.
- **Stand** (`WithinMm` = 0): the navigation node containing the point is free and in the spawn's component.
- **Reach** (`WithinMm` > 0): some free node in the spawn's component has its centre within `WithinMm` of the target box.

### 17.3 Reference points, in evaluation order

The first failure names the refusal.

1. **New points:** the candidate chest's site and the candidate station's site, each Reach 1600: "that would shut in the {piece}".
2. **Work anchors** of `ToWork`/`AtWork` errands, Stand, by NpcId: "that would cut {npc}'s work place off".
3. **Bodies,** Stand: the player ("that would shut you in"), then companions by NpcId, then errand NPCs by NpcId ("that would shut {npc} in"). **Creature bodies are excluded** (critique M6).
4. **Authored NPC sites,** Stand, by NpcId.
5. **Reach 1600** (the inventory reach, which gathering, containers and crafting use: `Crafting.cs:90, 167`; `Items.cs:218, 459`):
   - authored containers (by key), corpses (by key), piece chests (by ID);
   - authored stations (by key), piece stations (by ID);
   - nodes (by name);
   - switches, targeting the structure's footprint with 1600, which is `InteractionSystem`'s `DistanceTo` rule (`Systems.cs:278-282`);
   - created ground stacks, ordered by (x, z, def, count), never by their `NewId` item ID (critique L11).
6. **Authored door sides:** for each door by key, the closed box's centre offset ±(half thickness + 400) along its thin axis, each Reach 400.

### 17.4 Implementation contract (navigation implements; building calls)

```csharp
// World, internal - implemented by navigation's World-side Navigator
internal enum NavCheckDepth { Window, Full }
internal interface INavigability
{
    (NavVerdict Verdict, int FailedIndex) CheckPlacement(ImmutableArray<BoxBlocker> addedSolids, ImmutableArray<ReferencePoint> points, NavCheckDepth depth);
    bool CanReach(long fromXMm, long fromZMm, long toXMm, long toZMm, NavCapability capability);   // the assignment query, current gates
}
```

1. **Labels** of the all-passable graph for the current piece set are computed eagerly by navigation inside `RebuildNavigation` and in the constructor. **Nothing else writes them.**
2. **`Window`** (recommended algorithm). B = the nodes the candidate newly blocks; if B is empty, `Proven`. Flood the free-after nodes within B's box plus 30 m, starting from the ring of free nodes adjacent to B.
   - If every ring node is reached from one of them inside the window, the rest of the graph keeps its connectivity: `Proven`, unless the **M3 fallback** applies (a Stand point's node is in B, or every node that satisfied a Reach point is in B), which gives `Unknown`.
   - If a point satisfied before is now satisfied only by nodes in components exhausted inside the window that do not contain the spawn node: `Refused`.
   - Otherwise `Unknown`. New points are evaluated directly against the current labels minus B.
3. **`Full`:** relabel the candidate graph in scratch and evaluate every point.
4. **The command** runs `Window`, then `Full` on `Unknown`. **The preview** runs `Window` only, on its own scratch.
5. **Budgets,** measured on the Ashen Hollow grid: command check median < 2 ms and worst < 20 ms; preview `Window` < 2 ms.
6. **Ordering.** Nothing is ordered by a `NewId`-minted ID.

### 17.5 Examples in Ashen Hollow

Refused: the §11 vestibule; walling the companion into a room; enclosing a worker's anchor; a sealed room that swallows a dropped stack. Accepted: a U-shaped wall; a wall across the road at (100, 110) that leaves a way round; a sealed room with no reference point (a harmless solid block).

---

## 18. IDs

```csharp
// src/Domain/EntityId.cs, beside Create:
/// A runtime identity derived from what made it: the same commands make the same IDs, so a replay and a save-then-continue mint
/// identically (M7 pieces; NPC and creature identities are the same idea, Social.cs:121-125).
public static EntityId Derived(EntityKind kind, long ordinal, string tag, string salt)
    => Create(kind, ordinal, new CanonicalHasher().Add(tag).Add(salt).Add(ordinal).FinishBytes()[0..10]);

piece   pce_ = Derived(EntityKind.Piece,     seq,          "unnamed.piece/v1",           owner.Value)
chest   cnt_ = Derived(EntityKind.Container, piece's seq,  "unnamed.piece-container/v1", pieceId.Value)
```

- **`EntityKind.Piece`** (`pce`) is appended **after** `Character` in `src/Domain/EntityKind.cs`, so no value shifts; `TryInferFromDefinition` is unchanged (pieces always get explicit IDs).
- **Replay- and save-stable.** Same commands at the same boundaries give the same `seq` and IDs, and `seq` is persisted, so a replay from a save keeps an equal raw `StateDigest` until an item is minted (§11 step 0), and the next ID after a load equals the next without one.
- **Unique and ordered.** `seq` strictly increases per world (dismantles and destroys increment it too), so no ordinal is reused; the owner's random `chr_` salt separates characters (the network seam). The ULID timestamp holds `seq` and sorts lexically (`EntityId.cs:98-110`, compared ordinally at `:129-130`), so ID order is placement order and every tie-break by piece ID is replay-stable.
- **Registry-mediated.** Every derived ID goes through `registry.CreateEntity(DefinitionId.Parse(defId), id)` (`EntityRegistry.cs:44-66`), and is never re-registered while its tombstone lives (the C1 fix).
- **Not seed-derived.** `EntityId`'s remark (`EntityId.cs:17-21`) stays true; it, `docs/DATA_MODEL.md:123` and AGENTS.md are amended to name `EntityId.Derived`.
- **Items are unchanged:** refunds go through `GrantItem` (merge first, else `NewId`), and replay windows containing a mint compare the replayable `StateDump` (`StateDump.cs:20-45`), as crafting already requires.

---

## 19. Content kind and lints

### 19.1 Registration

| Item | Value | Where |
|---|---|---|
| Kind / FullKind | `piece` | `src/Content/SchemaResolution.cs`, a `kinds["piece"]` entry in the `faction` pattern (`:158-164`) |
| Directory | `content/pieces/` (subdirectories free) | the loader recurses (`src/Content/ContentLoader.cs:213`) |
| ID shape | `piece.<family>.<name>`, e.g. `piece.wall.timber` | `DefinitionId` grammar (`src/Domain/DefinitionId.cs:19-20`) |
| Reference suffix | `["piece_ref"] = new[] { "piece" }` | `src/Content/ContentChecks.cs:18-44` |
| Instance kind | `EntityKind.Piece`, `pce` | §18 |

`piece`, not `structure`: the region YAML already uses `structures:` for authored blockers (`ashen_hollow.yaml:64`), and DATA_MODEL already names `piece_ref` (`docs/DATA_MODEL.md:453`). `MOD001` reserves `piece.mod.*`; `LoadAll_Loads_Yaml_Files` gains the new IDs and `KnownDirectories_Is_Closed_Set` gains `pieces`.

### 19.2 Lints

`BuildingContent.Validate` is hooked into `ContentLoader.LoadAll` with the three-line pattern, after `QuestContent` (`ContentLoader.cs:155-197`), so the item, crafting, combat and social catalogues exist. `BuildingContent.Build(loader)` returns `BuildingSetup(BuildingCatalog, BuildingConstants)`, which lands on `SimulationSetup.Building`.

| Code | Checks |
|---|---|
| BLD001 | The catalogue builds (one `Try` wrapper), naming the definition's `SourceFile` |
| BLD002 | Closed field set; closed enums (`family`, `slot`, `traversal`, socket `type`/`axis`); slot legal for the family; `rotations` ⊆ {0..3}, non-empty, distinct; `health_max` ≥ 1 |
| BLD003 | Local-mm lattice consistency: `edge` providers at (0, ±1500) or (±1500, 0) with the right axis, `square` and `door` at (0, 0), mounts at (0, 0); square and roof bounds exactly the square; edge bounds within [−1700, −200, 1700, 200]; furniture bounds within [−1300, −1300, 1300, 1300] (clear of wall faces); parts inside bounds; `door` parts only on doors |
| BLD004 | `cost` names existing `item`-kind definitions, count ≥ 1 |
| BLD005 | `container` only on storage (`stack_slots` ≥ 1, `at_m` inside bounds); `station` only on station, with a recipe-used `kind` and a work anchor inside the square ≥ `navigability_radius + 300` (650) mm from every part and from the ±1300 wall faces; every NPC `works_at` kind is recipe-used |
| BLD006 | `config.building` present iff any piece or build area exists; module 3.0; step 90; §8.4's ranges |
| BLD007 | Build-area corners on the lattice; the box overlaps no authored structure, door box or barrier and meets no protected zone (§16.3). Spawner discs come from `CombatContent` (`SpawnSite`, `src/World/Runtime/Creatures.cs:23`), hence here, not in WorldContent (critique L9) |
| WLD015 | Build-area shape (`WorldContent`): `build_area.` prefix; min < max; inside region bounds and covered cells (`Covered`, `WorldContent.cs:386-400`); unique keys; `max_pieces` ≥ 1 |

---

## 20. The greybox catalogue, with dimensions

All figures are local mm at r0. The origin is the square centre or the edge midpoint.

| ID | Family / slot | Blocking parts (box, height) | Bounds | Cost | health_max | Other |
|---|---|---|---|---|---|---|
| `piece.pad.timber` | pad / square | none | [−1500, −1500, 1500, 1500] | 1 timber | 200 | 4 `edge` and 1 `square` provider; relief ≤ 250 |
| `piece.wall.timber` | wall / edge | [−1700, −200, 1700, 200], h 3000, solid | = part | 2 timber | 200 | `edge_mount`; `supports_roof` |
| `piece.doorway.timber` | doorway / edge | [−1700, −200, −800, 200] and [800, −200, 1700, 200], h 3000, solid | [−1700, −200, 1700, 200] | 2 timber | 200 | `edge_mount`, `door` provider; opening 1600; lintel 2400-3000 drawn only; `supports_roof` |
| `piece.door.timber` | door / door | [−800, −200, 800, 200], h 2400, door | = part | 1 timber | 120 | `door_mount`; `DoorOpen` |
| `piece.roof.timber` | roof / roof | none | the square | 1 timber | 100 | support rule; drawn as a 200 mm slab at wall top |
| `piece.storage.chest` | storage / furniture | [−500, 600, 500, 1200], h 700, solid | = part | 2 timber | 100 | `square_mount`; 12 slots; site (0, 900) |
| `piece.station.anvil` | station / furniture | [−500, 400, 500, 1000], h 900, solid | = part | **4 timber** | 300 | `square_mount`; kind `anvil`; work anchor (0, −250), facing 0 |

**Why these numbers.**

- **Module 3000 with a 250 mm navigation lattice.** Twelve nodes per module, and seam and lattice lines are shared (§0.4).
- **Walls 400 mm thick, centred on the lattice line.** This is the authored wall thickness (`ashen_hollow.yaml:68-78`).
  - One wall serves two rooms.
  - The room interior per module is 2600 mm, which leaves a 700 mm body 950 mm each side.
  - The ±200 end extensions fill corners, so rooms seal except at doorways, as the lodge and smithy do.
- **Walls 3000 mm tall.** This matches the smithy (`:74-78`) and is above the 1150 mm jump apex, so walls are never jumped.
- **Doorway opening 1600 mm, door 2400 mm.** These match both authored doors (`:143-144`), so whatever passes an authored doorway passes a player doorway. The jamb faces are at 700 and 2300 from the lattice line, so the body-centre band is [700 + r, 2300 − r]: person (350) [1050, 1950], wolf (450) [1150, 1850], boar (550) [1250, 1750]. On a 250 mm lattice these hold 3 / 3 / 1 corner-aligned node positions (1250, 1500, 1750) or 4 / 2 / 2 centre-aligned ones (1125 … 1875, nav_codefit's convention); a person with nav_codefit's 50 mm margin still has 3 or 4. Every M7 body class fits on either convention.
- **Door part.** It fills the opening exactly (touching is not overlap) and is 400 mm thick, so a closed door is a continuous wall.
- **Chest (1000 × 600, 700 tall).** It sits 100 mm from a wall's inner face (1300), leaving 1900 mm of floor; its site (0, 900) is within 1600 of the square's front half.
- **Bench (1000 × 600, 900 tall).** The work anchor (0, −250) is 650 mm from the bench face (a 350 mm body has 300 mm spare). With that clearance (BLD005), any 250 mm node containing the anchor has its centre within 177 mm of it and ≥ 473 mm from the bench, more than 350 + 50. A centred bench with its anchor 900 mm in front was rejected: it leaves a 300 mm band with no free node. The cost is 4 timber, not 2 timber + 2 billets (critique M4).
- **Heights.** The chest and bench are under the 1150 mm apex, so the player can jump them; neither is a surface (§6 item 5). Health is in §13.4.

**Not in M7:** post and fence (no proof needs them), half wall and window wall (optional, and zero C#), bed (no rest system).

---

## 21. Contracts offered and required

### 21.1 Navigation

1. **Footprints.** `SystemContext.StructureFootprints` and `Simulation.StructureFootprints`: `ImmutableArray<NavFootprint>`, sorted by (PieceId, Part), in world mm, axis-aligned.

   ```csharp
   public enum TraversalClass { Solid, Door, NonBlocking }
   public sealed record NavFootprint(EntityId PieceId, int Part, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm,
       long HeightMm, TraversalClass Class, bool DoorOpen, EntityId Owner);
   ```

   `Solid` is walls, jambs, chests and benches; `Door` is the leaf box, whose open state is read at query time and whose permission is `CanOperate`; `NonBlocking` is pad squares (an "indoors" hint). Roofs are not emitted. The list is readable before navigation's `Build()`.
2. **The current space.** `SystemContext.Space` holds exactly the solid parts in the §2.4 order; a test asserts its appended boxes equal the `Solid` footprints.
3. **Rebuild.** `RebuildNavigation(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, StructureChangeKind Kind, long Revision) : InternalCommand`, dispatched synchronously after every place, dismantle or destroy (after the mutation, before events), with the union of changed parts, not inflated. Never on door toggles, damage, repair or assignment. Navigation finalises the name; the payload is this.
4. **Gates at query time.** Authored doors, barriers and piece doors are never baked into derived data, as both candidates already do (nav_codefit §9; nav_minimal §3.2).
5. **Navigability:** `INavigability` (§17.4), eager all-passable labels, a separate preview scratch.
6. **Doors:** `OpenDoor` through `InteractionSystem`; `OperatePieceDoor`; the all-bodies close check.
7. **The errand mover:** `NpcSystem` persists the route and stuck count in the errand, calls the follower, never teleports, and excludes errand NPCs from site placement and site facing (§15.3).
8. **Timing:** `_building.Populate()`, `_navigation.Build()`, `_npcs.Populate()` (§9.6).
9. **Seams:** footprints are world mm with no per-cell split; a straddling piece is one footprint.

### 21.2 Persistence

Schema 14 adds `pieces`, `structure_seq` and `npc_errands` to `entities.msgpack` (required from 14); freezes `V13.EntitiesSection` and repoints `SchemaV8ToV9`; extends the definition pass (piece `def_id`, errand `npc_id`) and baseline proof and rebase; orders `structure_seq`, pieces, containers, errands in `FromSnapshot`; adds `ReleaseContainer`; moves `EffectiveCellDigest` and `simulation` to v2 (player v10 belongs to factions and navigation). Navigation's grid and labels join the PERSISTENCE §2 not-saved table.

### 21.3 Presentation

- **`StructuresView`** (`src/Presentation/Greybox/StructuresView.cs`) builds from `Simulation.Pieces` in `Resync()` (`src/Presentation/Main.cs:915-928`), adds and removes nodes on `PiecePlaced`/`PieceRemoved`/`PieceDestroyed`, and swings doors on `DoorToggled` with a piece key. Walls, jambs, doors, chests, benches and roofs are `Solid()` boxes with layer-1 camera colliders (`HollowView.cs:374-381`); pads are the draped slab with no collider; colours are `Palette.Wood`, `Roof`, `Door`.
- **Prediction** uses `simulation.Space` instead of `setup.Layout.Space` (`src/Presentation/Player/PlayerController.cs:127`); `DynamicBlockers` already carries closed piece doors.
- **Ghost:** `PreviewPlacement` of the `Snapper` pose with §4's colours, modelled on `Palette.Fold` (`src/Presentation/Greybox/Palette.cs:42-50`).
- **Keys** (direct only, no radial menu, ruling 5; listed in F1 under "BUILDING"; B, T, Y, Delete and F2 are free, `Main.cs:1061-1097`): **B** toggles build mode (no panel open). In build mode: **1-7** choose a piece (a panel lists costs), **R** rotates +90°, **LMB** places (attack suppressed), **Delete** dismantles and **T** repairs the piece under the reticle, **Esc** leaves build mode, taking **precedence** over `release_mouse` (`Main.cs:381, 489, 1089`). **E** works doors as always. **Y**, outside build mode and facing an NPC whose `works_at` matches an owned station, asks them to work at the nearest eligible one (ties by piece ID) or releases them. Build mode gates off R's take-all, cast keys 4-6 and attack, as the dialogue branch does (`Main.cs:374-386`).
- **Camera.** `CameraRig.MaxDistance` is a `const` (`src/Presentation/Player/CameraRig.cs:23`, used at `:85`); it becomes an instance field, default 6 m, raised to 9 m in build mode (bounded, not an RTS camera).
- **Refusals:** the five new commands join `Main.Subscribe`'s `CommandRejected` filters (`Main.cs:662`).
- **F2 building debug:** piece IDs, slot keys, owner, health, socket markers, the revision, `StructureAudit`, the last `StructuresChanged` rectangle, and errands with phase and route.
- **The moving NPC** is interpolated between ticks, as creatures are.

### 21.4 Factions

- **`SystemContext.SightWalls()`** = `Space.Blockers ∪ ClosedDoors()`, per C_factions §13. It is the one wall list: `Combat.cs:570`, `Companions.cs:598` and `Creatures.cs:893` all switch to it. Placed walls and closed placed doors then hide acts from witnesses exactly as they hide the player from creatures. Building supplies the factions fixture row with a placed wall.
- **No act.** Building dispatches no `RecordAct`, and ownership feeds no standing, legality or hostility.

### 21.5 Runtime reads switched to `_context.Space`

`Combat.cs:570`; `Companions.cs:132, 372, 398, 519, 567, 598`; `Creatures.cs:173, 588, 623, 762, 837, 893`; `Social.cs:136`; `Systems.cs:140, 160, 216`. The critique confirmed this list is complete for `src/World/Runtime`. Presentation's terrain-only reads may stay.

**Performance.** `ClosedDoors()` concatenates the cached `ClosedPieceDoors` rather than scanning pieces. If `SixtyCreatures_TickWithinTheBudget` (< 4 ms, `tests/Application.Tests/CreatureTests.cs:526-541`) fails with 200 pieces, add a broadphase that **filters and then iterates in the original list order**, because `Resolve` is order-dependent.

---

## 22. Test plan

**Domain.Tests (`tests/Domain.Tests/Building/`):** lattice rules per slot kind; R_r and rotated boxes for all r and every piece; socket world positions and axes; `Snapper` with fixed, half-module and door-tie aims; relief (the square at (111..114, 105..108) is 228 mm, (99..102)² is 96 mm); roof support cases; integer overlaps (touching allowed); repair ceiling and refund floor; `EntityId.Derived` stable, with ordinal order equal to seq order.

**Content.Tests:** the game pack builds the catalogue, `config.building`, the area and the deadfall; `LoadAll_Loads_Yaml_Files`; each of BLD001-BLD007 and WLD015 rejects a crafted bad file (the `EditedContent` harness); `piece_ref` resolves; Kera's `works_at`.

**Application.Tests:**
- Each piece: accepted placement, events, views, exact spend, IDs. Each of the 15 rules refuses with its reason and an unchanged digest (test packs: a steep area, a rock in the area, an area over a spawner, `max_pieces: 3`).
- Preview parity over 40 poses, and the H4 guard (§4).
- Collision: wall, doorway, door block and pass; piece and authored doors refuse to close on a companion, an NPC and a creature; prediction equals authority across a new wall; creatures blocked; `Aim` stops at walls.
- Chest: `APieceChest_EmptiedAndRefilled_KeepsOneIdentity` (three store/take-all/store cycles, one `cnt_`, no exception) and the same split by a save and load (0 `StateDump` differences); dismantle refused while not empty; destroy spills with IDs kept.
- Station: craft at a piece anvil; refused after dismantle.
- Damage: melee, shot (hunting bow, 5 rough arrows, might ≥ 9) and formula (impulse bolt known) amounts; a blow that hits a creature damages no piece; an authored wall touching a piece shields it; destroy at 0; a destroyed doorway takes its door.
- Repair, dismantle, assignment: every refusal, including "not yours" on a foreign-owner piece from a crafted save.
- Errands: assign, walk, arrive exactly; release, walk home, record retired exactly at the site; authored and piece doors opened, never closed; the work-facing override; save mid-walk then continue equals continue; dismantling under a worker sends them home.
- The four-cell pad's host cell and per-cell digests; §9.7's round trip; §11's acceptance test.
- RK-06 (`docs/RISK_REGISTER.md:154-158`): 200 pieces (81 pads, 81 roofs, 38 walls) with Tavar and Kera inside; save, reload, equal rows; both actors get a found plan from the doorway to an interior point; `entities.msgpack` grows ≤ 200 × 250 bytes over the empty save.
- Performance: 200 pieces plus 60 creatures tick < 4 ms (extending `CreatureTests.cs:526-541`); navigability median < 2 ms, worst < 20 ms; preview `Window` < 2 ms.

**Persistence.Tests:** the 13 → 14 step; the v14 fixture; "a schema-14 entities section without `pieces` / `structure_seq` / `npc_errands` is corrupt, not defaulted"; the definition pass for piece defs and errand NPCs (rename, removal as loss, merge); baseline proof and rebase for pieces and errands; an orphan chest container and a wrong `cnt_` rejected and reported; byte stability; every hard-coded step list (research/persistence §3.3 G).

**Architecture.Tests:** the `WorldDelta` readers; `PreviewPlacement` on the allow-list; no static mutable state in `Domain/Building`; no public setters on `Domain.Spatial` records.

---

## 23. Implementation map and order

Each slice is green and playable.

1. **Domain:** `src/Domain/Building/Building.cs` (§3.2); `src/Domain/Spatial/StructureFootprints.cs`; `RegionLayout` (`BuildAreaSite`, `ContainerSite.InstanceId`/`Owner`); `EntityKind.Piece`; `EntityId.Derived`; `NpcDefinition.WorksAt`.
2. **Content:** kind `piece`, `piece_ref`, `BuildingContent` (BLD001-BLD007), WLD015, `works_at`; files: 7 pieces, timber (`no_sell`), deadfall resource and node, `config.building`, region edits, Kera's `works_at`.
3. **World:** `PieceRecord`, `NpcErrandRecord`, `WorldDelta` stores, snapshot, digests; the two slices, wrappers and derived fields; `SystemContext.Space`, `Stations()`, `SightWalls()`, `ClosedDoors()`, `FindContainer`; `BuildingRules.cs` and `Building.cs` (`BuildingSystem`: 5 handlers plus `DamagePiece` and `OperatePieceDoor`, **no `Tick`**); `InventorySystem` (`Baseline`, `Materialize`, `Check`, the C1 clause, `SpillContainer`); `CombatSystem` (`FirstStop`, hooks); `InteractionSystem` (piece-door branch, `OpenDoor`, all-bodies close); `CraftingSystem` stations; `NpcSystem` errands; the Space switch; `Simulation` composition, `DrainCommands`/`Dispatch` arms, views, `PreviewPlacement`.
4. **Application:** `GameSession.Boot` builds `BuildingSetup` and registers the M7 transition.
5. **Persistence:** DTOs, the step, frozen V13, loader, rebase, fixtures.
6. **Presentation** (§21.3) and `--build-shots`.
7. **Docs** (§24) and `docs/M7_STATUS.md`.

---

## 24. Building-side document reconciliation (M7's first slice)

| Document | Change |
|---|---|
| `docs/ROADMAP.md:283-285` | "foundations … floors" → "ground pads (one ground-level piece; one storey, ruling 2)"; "free rotation" → "quarter-turn rotation (owner question; D-08)"; "the navmesh updates on placement" → "the domain navigation is rebuilt deterministically on every footprint change (ruling 1)"; the entry "NPCs and companions path reliably" is met inside M7 |
| `docs/DECISIONS.md` D-08 | dated note: quarter turns on a 3 m lattice; "free rotation" is a later catalogue and footprint question, never physics |
| `docs/WORLD_ARCHITECTURE.md` | §5 (`:128-145`): cells never partition movement or navigation. §10 (`:403-420`): `pce_` rows, no `bld`; `entities.msgpack`; host cell = the anchor's; legality adds area, bodies, navigability; navigability validated, not "guaranteed by snapping"; no defense or tiers. §11 (`:424-435`): the navmesh row moves to the domain. RK-A2 (`:469`): solved by a seam-free domain representation, proven by the Crossing Workshop |
| `docs/PERSISTENCE.md` | §2: navigation not saved; §3.2-3.3: `buildings.msgpack` deferred, pieces and errands in `entities.msgpack`; §5.3-5.4: the §9.2 fields; §6.2: rows 11 → 12 to 13 → 14; §7.4: `structure_seq`, pieces, containers, errands; building, then navigation, then NPCs populate |
| `docs/DATA_MODEL.md` | §1 kind `piece`; §2.2 `pce` and derived identities; §5 `piece_ref`; a §4.x piece schema "As implemented (M7)"; §6 save-sensitive rows; `:123` derived identities |
| `docs/SYSTEMS.md` S-32 | as built: no structure record, raids, attack frequency or construction progress |
| `docs/GAMEPLAY_LOOPS.md:282` | its Phase-2 "one upgrade tier; property threats with the … frequency reduction/skip option" is deferred; home-defense threats are M10's (scope row 8) |
| `docs/RISK_REGISTER.md` | RK-06 and RK-14 validations become §22's headless tests; RK-14's "needs the engine" is withdrawn (ruling 1); RK-06's "sub-linear" is met as a linear bound with a small constant (one row per piece) |
| `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:1080` | "no player settlement building" is superseded for M7 (owner question 4) |
| `src/Domain/EntityId.cs:17-21`, AGENTS.md | derived identities named |

---

## 25. Scope notes, owner questions, open issues

**Scope notes** (no challenge to a scope ruling): item 13 is kept (one material and one node through the unchanged M3f model; no currency or upkeep; timber is `no_sell`; no piece drains iron); item 8 is met by the player's own strikes, reachable and explicit, inventing no threat; item 5 adds one persisted record kind, the errand, and no schedule, production or hireling.

**Owner questions** (building adds none; it answers three of the scope list's):
1. Rotation (scope Q1): quarter turns on a 3 m lattice (default), or 45°/free with oriented footprints across `Kinematics`, traces and prediction?
2. Assignment (scope Q3): Kera Voss at a player-built anvil bench (default), or reconcile to M10?
3. Where to build (scope Q4): `build_area.hollow_crossing`, x/z 87-114 m (default), or anywhere legal? This also supersedes the Ashen Hollow bible's "no player settlement building" line for M7.

The deadfall-or-stack question from v1 is decided: the deadfall (§8.2).

**Open issues.**
- Authored layout is not in `baseline_hash`: a later layout edit that runs into saved pieces is audited, not proven (`docs/M2B_STATUS.md:135`).
- Item minting stays `NewId`: replay windows with a mint compare replayable dumps (refunds merge first, which reduces mints).
- Height-blind queries: the chest and bench block sight and shots although a jump clears them.
- Creatures are not navigability agents: the doorway admits them (§20), but nothing protects their routes.
- Presentation cost at 256 pieces is unmeasured; the RAZER window is still owed (`docs/M6_STATUS.md:221`). Merged meshes per structure are the fallback.
- "Name every lost structure" (`docs/PERSISTENCE.md:136`) is impossible when the entities section itself is quarantined; the report names the section.
- Navigation API names (`RebuildNavigation`, `NavRoute`, the follower, `CanReach`) are the navigation design's to finalise; payloads and semantics are fixed here.
- Foreign-owned piece doors (reachable only by a crafted save) count as passable, since M7 has one label class; M9's second class removes this.

---

## Appendix A. Critique disposition

| Finding | Disposition | Where |
|---|---|---|
| **C1** Emptying a piece chest retires its derived `cnt_`; the next deposit throws | **Fixed as proposed**: the corpse clause gains `site.InstanceId is null`; only `RemoveCore` retires it; both tests added. Verified `Items.cs:558-562`, `WorldDelta.cs:390-400`, `EntityRegistry.cs:50-51, 133-142` | 14.1, 22 |
| **H1** The "pure Domain" validator cannot compile | **Fixed**: Domain keeps World-free math; `Validate`, `PlacementContext`, `PlacementCheck`, `INavigability` are World-internal; `Snapper` takes `PiecePose`. Refinement: `ReferencePoint` and `NavFootprint` live in `Domain.Spatial` (no World dependency; navigation's Domain code consumes them). Verified `Domain.csproj`, `Items.cs:84` | 3.2 |
| **H2** Nobody owns the moving NPC's persisted state | **Fixed, different home**: `NpcErrandRecord` (phase, pose, `NavRoute`, stuck, `WorkOwner`) owned by `NpcSystem` (`StateSlice.NpcErrands`); Populate places by errand; Tick overrides facing; errand NPCs open, never close, authored doors via `OpenDoor`. **Rejected:** `player.msgpack`, in favour of `entities.msgpack` (§15.1). The errand is now the only record of the assignment; the row's `WorkerNpcId` is dropped | 15, 9 |
| **H3** The seam proof does not force a path through the structure; "crossing x = 100" was false | **Fixed as proposed**: doorway at (100500, 99000) r0; the route crosses z = 100 once inside the footprint and never exits and re-enters; vestibule moved south, route alteration moved west; companion beat added before and after the reload. Re-derived: the door admits centres at x ∈ [100 050, 100 950] | 11 |
| **H4** The revision misses flag changes; previews can contaminate authority | **Fixed differently**: `FlagEpoch` rejected. Navigability treats every door and barrier as passable, so its labels depend only on the piece set, which `StructureSequence` keys completely; labels are written only in commands and the constructor; the preview has its own scratch. **Accepted**: the no-write rule and the 1,000-preview guard test. Door toggles no longer move the revision | 2.3, 4, 17 |
| **M1** Replay never tests derived IDs under the raw digest | **Fixed**: S0 before step 1; replayed step 1 gives an equal raw digest and ID list. Verified `ExchangeItems` mints nothing (`Items.cs:327-357`) | 11 |
| **M2** The slow path runs on the main thread | **Fixed**: eager labels per footprint change; the command runs `Window` then `Full`; the preview `Window` only (amber on `Unknown`); budgets tested | 4, 17.4 |
| **M3** Fast-path hole for points under the footprint | **Fixed as proposed** | 17.4 |
| **M4** The bench spends finite iron | **Fixed**: 4 timber, no iron. Verified `iron_seam.yaml` `respawn: none`; den cache one-shot | 8.1, 20 |
| **M5** The scenario lacks the bow, arrows and formula | **Fixed (second option)**: shot and formula in their own tests; the scenario uses melee. Verified starting kit (`content/config/inventory.yaml`) | 11, 22 |
| **M6** Creature bodies as navigability points | **Fixed**: removed; the `Bodies` check still includes creatures | 17.3 |
| **M7** Melee and shot rules can pick the wrong blocker | **Fixed differently**: no `Blocker.EntryT`; `FirstStop` reuses `Trace`'s bisection, whose walled prefix enters every candidate within the final ≤ 10 mm; a piece is struck only if every candidate is a piece; authored geometry wins ties. Exact relative to the visible stop; no Domain API | 13.2 |
| **M8** Doors-passable is too generous | **Partly accepted**: every M7 reference agent may open piece doors, and unassigned NPCs never move (assignment grants permission). `OpensPieceDoors` is kept as the M9 seam; M7 computes one label class | 17.1 |
| **L1** Chest host cell | **Fixed**: the site-point cell is documented; `TryApplyContainer` checks the derived `cnt_` | 9.3 |
| **L2** "Never on a seam" | **Fixed**: claim dropped; the host cell is the floor `OfWorld` of the anchor | 0.4 |
| **L3** Second-door refusal order | **Fixed**: `Slot` refuses "that doorway already has a door" | 3.3 |
| **L4** Camera `const`; Esc | **Fixed as proposed** | 21.3 |
| **L5** `ClosedDoors()` cost | **Fixed**: cached `ClosedPieceDoors`; order-preserving broadphase as contingency | 2.4, 21.5 |
| **L6** Timber sale value | **Fixed**: `no_sell: true` | 8.1 |
| **L7** Owner question 4 | **Fixed**: the deadfall is decided, the fallback recorded | 8.2 |
| **L8** Spill mutator | **Fixed**: `ReleaseContainer` behind `WorldItems` | 9.3 |
| **L9** WLD015 needs spawners | **Fixed**: WLD015 checks shape; BLD007 checks lattice and protection | 19.2 |
| **L10** Doc gaps | **Fixed**: WA §5, RK-A2, GAMEPLAY_LOOPS:282, RK-06 bound | 24 |
| **L11** Rooms swallow ground items | **Fixed**: created stacks are Reach points, ordered geometrically | 17.3 |
| **L12** Commute estimate | **Fixed**: path length / speed × 1.25 plus door ticks | 11 |
| Critique §5: the bench is jumpable | **Fixed**: stated for bench and chest | 6 |
| Critique §7.1-7.6 (navigation) | **Addressed**: no `NavRevision` pair (§2.3); `CanOperate` (§7.2); §17.3; exact arrival and facing (§15.3); §0.4 and §21.1; §9.6 | - |
| Critique §8.1-8.7 (persistence) | **Addressed**: errand in entities; §9.3 order; full mutator list; L1; `StructureAudit` includes "unsupported" pieces (a dropped provider leaves its mount standing, since support is placement-time only) and errand repairs; fixture `ts ≤ 9` and a foreign owner (§9.5); the transition test (§8.2) | 8.2, 9 |
