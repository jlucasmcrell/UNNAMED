## 4. Building v1

Section 6 owns every cross-system name used here; section 3 owns the navigation algorithms building calls; section 7 owns the save schema; section 8 owns the build-mode UI.

### Decisions

- Pieces snap to a world-anchored 3 m lattice in quarter turns (owner question Q1 default); every blocking part is an axis-aligned integer-mm box with `ClearanceMm = 0`.
- One storey (ruling 2): a "foundation" or "floor" is a ground pad; bodies stay on the terrain; M7 has **no walkable elevated surface**.
- Seven greybox pieces: pad, wall, doorway, door, roof, storage chest, anvil bench. Every cost is timber.
- A placed piece is one saved row, `PieceRecord`. Shape, sockets, `health_max`, container and station data come from the definition and are never saved.
- `StructureSequence` is a persisted per-world counter, bumped by exactly one on every place, dismantle and destroy. It is the piece-ID ordinal and the structure revision.
- `BuildingSystem` owns `StateSlice.Structures`, has no `Tick`, and handles five player commands plus the internal `OperatePieceDoor` and `DamagePiece`.
- Placement runs fifteen ordered checks and returns the first failure. All fifteen ship in M7; check 15 (navigability) lands in slice E7.
- `Simulation.PreviewPlacement` runs the same `BuildingRules.Validate` on a preview scratch. It is advisory and writes nothing.
- Every committed place, dismantle or destroy of a piece with a solid or door part sends exactly one `RebuildNavigation`; pads and roofs send none; door toggles send none.
- A piece door's open/closed state is authoritative on its row. The owner, the owner's companion and NPCs working for the owner may open it; NPCs never close; a close is refused while any body overlaps the leaf.
- Timber comes only from the authored `container.timber_stack` (80 timber) at (84, 118). There is no renewable node and no baseline transition.
- Material takes order stacks by (quality ascending, count ascending, `ItemId` ordinal). Dismantle refunds 50 % rounded down. Repair costs the missing fraction rounded up.
- Exactly one damage rule: a player melee blow on a piece part deals 10. Authored geometry shields. Pads and roofs hold health that no M7 rule reaches.
- Destruction at 0 health takes the removal path without refund. A destroyed doorway destroys its door first. A destroyed chest spills its stacks with their item IDs kept; a chest's `cnt_` is retired only with its piece (the C1 fix).
- Ownership gates dismantle, repair, assign, release, chest access and piece doors. It never gates damage and never feeds standing, legality or hostility.
- One build area, `build_area.hollow_crossing` (x and z 87-114 m), over the four-cell corner (Q4 default).
- Kera Voss can be assigned to a player-built anvil bench. The assignment is an `NpcErrandRecord` owned by `NpcSystem` (Q3 default, D1).
- Piece and piece-chest IDs are `EntityId.Derived` from `StructureSequence`. E0 adds a dated D-04 note.
- Building records no faction act. Its placed walls reach combat, creatures and companions through `SightWalls()`.
- The Crossing Workshop is the acceptance scenario, driven by one command table that the headless tests and `--build-shots` share.

### 4.1 What ROADMAP M7 requires of building

| ROADMAP M7 (`docs/ROADMAP.md:283-285`) | M7 answer | § |
|---|---|---|
| Socket/snap placement | Content sockets on a 3 m lattice, exact mating (0 mm tolerance) | 4.2, 4.7 |
| Foundations, walls, floors, roofs, doors | Pad (foundation and floor are one ground piece), wall, doorway, door, roof | 4.8, 4.21 |
| Free rotation and socketing | Quarter turns (Q1); the definition keeps a `rotations` list | 4.2 |
| Ownership | `Owner` on every row | 4.11 |
| Per-piece health by explicit rules; repair | `health_max` / `HealthCurrent`, one named damage rule, `RepairPieceCommand` | 4.12 |
| Storage containers; one crafting station as a placeable | `piece.storage.chest`, `piece.station.anvil` | 4.14 |
| Sparse world delta (D-05) | Host-cell rows in `entities.msgpack` | 4.19 |
| Rejects un-navigable or overlapping configurations | Checks 7, 10, 12 (overlap) and 15 (navigability) | 4.5 |
| Exit: build, assign an NPC, in/through/around, straddling | The Crossing Workshop, Kera Voss at the bench | 4.15, 4.22 |
| Exit: save/load keeps pieces, owner, health | Row round trip, digests, `StateDump`, the v14 fixture | 4.19, 4.22 |
| Exit: damage/repair explicit | One rule, one code point | 4.12 |
| Exit: "the navmesh updates on placement" | The domain grid is restamped by `RebuildNavigation` (ruling 1) | 4.5, 4.23 |

### 4.2 Invariants and the lattice

**Invariants.**
1. **One storey.** Body Y is always terrain plus jump lift (`src/Domain/Spatial/Kinematics.cs:157-159`). `Kinematics` and `MovementRules` gain no members.
2. **No overhang parts.** Every part has `ClearanceMm = 0`. `IsClear` and `Crosses` are height-blind, so a lintel or roof blocker would close a doorway to sight and navigation. Lintels and roofs exist only in presentation.
3. **Axis-aligned integer boxes.** Quarter turns keep every rotated part an exact axis-aligned box.
4. **No structural simulation (D-08).** Support is checked at placement only. Removing a wall never removes a roof.

**Module.** M = 3000 mm, H = 1500 mm, anchored at the world origin (lattice lines x = 3000·i, z = 3000·k). `module_m` is save-locked: anchors are absolute. 3000 / 250 = 12, so every lattice line is a navigation node boundary (BLD006). Cell seams (multiples of 100 000 mm) are lattice lines only every third seam; neither interior seam of Ashen Hollow (x or z = 100 m) is one, so every square containing a seam straddles it.

**Rotation.** `r ∈ {0, 1, 2, 3}` quarter turns. Local +Z points along world facing `r · 90 000` mdeg (0 = +Z, clockwise towards +X). The integer maps are:
- R0(lx, lz) = (lx, lz)
- R1(lx, lz) = (lz, −lx)
- R2(lx, lz) = (−lx, −lz)
- R3(lx, lz) = (−lz, lx)

A local facing f becomes `(f + r · 90 000) mod 360 000`. A rotated box is the box bounding its four rotated corners, which is exact.

**Slots.** Arithmetic uses `WorldMath.FloorMod`/`FloorDiv`.

| Slot | Anchor rule (mm) | Rotation rule | Slot key | Area-containment geometry |
|---|---|---|---|---|
| `square` (pad) | x ≡ H, z ≡ H (mod M) | any listed | `sq:i:k`, i = (x−H)/M, k = (z−H)/M | the square [x±H] × [z±H] |
| `edge` (wall, doorway) | r even: x ≡ H, z ≡ 0; r odd: x ≡ 0, z ≡ H | parity fixes the axis | `ex:i:k` (r even) or `ez:i:k` (r odd), i = ⌊x/M⌋, k = ⌊z/M⌋ | the edge segment |
| `door` | equals an intact doorway's anchor | r ≡ the doorway's r (mod 2) | `dr:<the doorway's edge key>` | the doorway's segment |
| `roof` | x ≡ H, z ≡ H | any listed | `rf:i:k` | the square |
| `furniture` (chest, bench) | x ≡ H, z ≡ H | any listed | `fu:i:k` | the square |

A slot key holds at most one intact piece. Piece-against-piece footprint overlap is not checked: two walls meeting at a corner share a 400 × 400 mm corner square by design, and BLD003 keeps slot kinds apart.

**Host cell.** `CellKey.OfWorld(XMm / 1000.0, ZMm / 1000.0)` of the anchor. It floors, so an anchor on a seam is unambiguous. A straddling piece is one row in one host cell.

### 4.3 Piece definitions (content kind `piece`)

```yaml
# content/pieces/wall/timber.yaml
id: piece.wall.timber
kind: piece
schema: 1
display_key: piece.wall.timber.name
tags: [piece]
name: Timber Wall                     # DRAFT display name
notes: A 3 m timber wall on a pad edge (M7, one storey).
family: wall                          # pad | wall | doorway | door | roof | storage | station
slot: edge                            # square | edge | door | roof | furniture
rotations: [0, 1, 2, 3]
bounds_m: [-1.7, -0.2, 1.7, 0.2]      # local [min_x, min_z, max_x, max_z] at r0
parts:
  - { box_m: [-1.7, -0.2, 1.7, 0.2], height_m: 3.0, traversal: solid }
sockets:
  - { type: edge_mount, at_m: [0, 0], axis: x }
cost:
  - { item_ref: item.material.timber, count: 2 }
health_max: 200
supports_roof: true
# storage only:  container: { stack_slots: 12, at_m: [0, 0.9] }
# station only:  station:   { kind: anvil, work_anchor_m: [0, -0.25], work_facing_deg: 0 }
```

| Field | Type / units | Required | Meaning and constraint |
|---|---|---|---|
| `id` | definition ID | yes | `piece.<segment>.<name>`. The second segment need not equal `family` (fixture pieces are `piece.fixture.*`) |
| `family` | closed key | yes | `pad`, `wall`, `doorway`, `door`, `roof`, `storage`, `station` |
| `slot` | closed key | yes | the family's one legal slot: pad → square; wall, doorway → edge; door → door; roof → roof; storage, station → furniture |
| `rotations` | int list | yes | non-empty, distinct, ⊆ {0, 1, 2, 3} |
| `bounds_m` | 4 × m | yes | reach, area containment, authored overlap and protected ground. The square for pads and roofs; the union of `parts` otherwise |
| `parts[]` | box_m, height_m, traversal | no (absent for pads, roofs) | converted with `Mm` (round half away from zero), whole mm, inside `bounds_m`. `traversal` ∈ {`solid`, `door`}: `solid` always blocks; `door` blocks only while the row's `DoorOpen` is false; `door` only on the door family |
| `sockets[]` | type, at_m, axis | family-dependent | closed type set below; `axis` ∈ {x, z} for edge and door sockets, absent for square sockets |
| `cost[]` | item_ref, count ≥ 1 | yes | spent in list order; `item_ref` names a plain `item` definition |
| `health_max` | int ≥ 1 | yes, every family | pads and roofs carry it too; no M7 rule reaches them |
| `supports_roof` | bool | no (false) | the piece supports a roof on either adjacent square |
| `container` | stack_slots ≥ 1, at_m | storage only | the chest's container site point, local metres, inside `bounds_m` |
| `station` | kind, work_anchor_m, work_facing_deg | station only | `kind` must be used by a recipe (`anvil`, `forge`); the anchor is the worker's pose in local terms |

The field set is closed: any other key is BLD002. There is no presentation asset field: art bindings are presentation-only and M7 adds none (section 8).

**Socket types.**

| Type | Role | Offered by | Needed by | Mates with |
|---|---|---|---|---|
| `edge` | provider | pad: (0, ±1.5) axis x; (±1.5, 0) axis z | - | `edge_mount` |
| `square` | provider | pad: (0, 0) | - | `square_mount` |
| `door` | provider | doorway: (0, 0) axis x | - | `door_mount` |
| `edge_mount` | mount | - | wall, doorway | `edge` |
| `square_mount` | mount | - | chest, bench | `square` |
| `door_mount` | mount | - | door | `door` |

Code: the definition records, `BuildingCatalog`, `BuildingConstants` (with `Problem()`), `Lattice`, `QuarterTurn`, `BuildingMath`, `PiecePose` and `Snapper` live in `src/Domain/Building/Building.cs` (namespace `UNNAMED.Domain.Building`), pure and World-free. `BuildingContent.Build(loader)` returns `BuildingSetup(BuildingCatalog Catalog, BuildingConstants Constants)` for `SimulationSetup.Building` (default `BuildingSetup.Empty`).

### 4.4 The runtime instance

There is **no persisted "structure" record**. A structure is a derived connected set of pieces; F2 may name it after its lowest piece ID. `EntityKind.Building` (`bld`) stays reserved for a later settlement record.

```csharp
// src/World/WorldDelta.cs
/// A player-placed building piece (M7): one row, anchored to its anchor's cell and proven against that cell's baseline.
/// Its shape is its definition's; only what the definition cannot know is stored.
public sealed record PieceRecord(
    EntityId InstanceId,   // pce_, EntityId.Derived (4.18)
    string DefId,          // piece.*; through the definition-ID pass on load
    string HostCell,       // CellKey.OfWorld(XMm / 1000.0, ZMm / 1000.0)
    long XMm, long ZMm,    // slot anchor, absolute world mm
    int Rotation,          // 0..3
    EntityId Owner,        // chr_ of the placing character
    int HealthCurrent,     // 1..health_max; a row at 0 never exists
    string? BaselineHash = null)
{
    public bool DoorOpen { get; init; }   // door family only; false elsewhere
}
```

**Not stored:** footprints, bounds, sockets, slot keys, overlapped cells, `health_max`, cost, container site and capacity, station kind and work anchor, and the worker. The worker lives only in the NPC's errand (4.15), so a row and an errand can never disagree. The record exposes no computed public property (the `StateDump` rule).

**`StructureSequence`** (`long`, on `WorldDelta`, saved as `structure_seq`): +1 on every place, dismantle and destroy, pads and roofs included; never on door toggles, damage, repair, assignment or load. It never decreases and no ordinal is reused. `Simulation.StructureRevision` equals it.

**Derived state** (owned by `StateSlice.Structures`, written only through `RuntimeState` wrappers, rebuilt by `Rebuild()`):

| Field | Content | Rebuilt on |
|---|---|---|
| `SystemContext.Space` (`WalkSpace`) | `Setup.Layout.Space with { Blockers = authored (content order) ++ piece solid parts (StructureOrder) }`; part IDs `"{pieceId}#{partIndex}"` | place, dismantle, destroy, `Populate` |
| Closed piece leaves (`ImmutableArray<Blocker>`) | door parts of intact doors whose row has `DoorOpen = false`, in `StructureOrder` | the above, and every door toggle (`SetClosedPieceLeaves`) |
| Socket index | `Slots`: slot key → piece ID (ordinal); `Providers`: (socket type, world x, world z, axis) → count of intact providers | place, dismantle, destroy, `Populate` |
| `StructureFootprints` (`ImmutableArray<NavFootprint>`) | one per solid or door part (`TraversalClass { Solid, Door }`), in `StructureOrder`. Pads and roofs emit none | place, dismantle, destroy, `Populate` |
| `StructureAudit` (`ImmutableArray<StructureConflict(string Subject, string Problem)>`) | load-time report of the piece lines (4.19), written by `BuildingSystem.Populate` through `SetStructureAudit(owner, lines)` | `Populate` only |

The errand lines are not `Structures` state: `NpcSystem.Populate` writes them (4.15) to a transient `ErrandAudit` (`ImmutableArray<StructureConflict>`) under `StateSlice.NpcErrands`, through `SetErrandAudit(owner, lines)`, which requires `NpcErrands`. The view `Simulation.StructureAudit` returns `State.StructureAudit` (piece lines), then `State.ErrandAudit` (errand lines, in `NpcId` order).

`StructureOrder` (section 6) is `(MinXMm, MinZMm, MaxXMm, MaxZMm, HeightMm, then Id ordinal)`, so `Space` depends on the piece set, never on placement history (G10). Providers are asked only for a **count**, so no iteration order decides anything. Pieces join the **static** blocker list (the low chest and bench get the airborne tucked radius like authored low structures). `CreatureSystem.Populate` alone keeps reading `Setup.Layout.Space` (G9).

### 4.5 Placement

```csharp
// src/World/Runtime/Building.cs
public sealed record PlacePieceCommand(EntityId Actor, string PieceDefId, long XMm, long ZMm, int Rotation) : GameCommand(Actor);
public enum PlacementRule { Actor, Definition, Rotation, Lattice, BuildArea, Reach, Slot, Support, Terrain, Authored, Protected,
                            Bodies, PieceCap, Materials, Navigability }
public enum NavVerdict { NotApplicable, NotChecked, Proven, Refused }

// src/World/Runtime/BuildingRules.cs (internal static; reads RuntimeState)
internal sealed record PlacementContext(SystemContext Context, NavigationSystem Navigation, EntityId Actor, NavScratch Scratch,
                                        NavCounterSink? Counters, bool CheckNavigability);
internal sealed record PlacementCheck(bool Allowed, PlacementRule? Failed, string? Reason, BoundsMm Bounds,
    ImmutableArray<BoxBlocker> Parts, ImmutableArray<StackTake> Takes, NavVerdict Navigability);
internal static PlacementCheck Validate(PlacementContext ctx, string defId, long xMm, long zMm, int rotation);
```

The command carries only a discrete pose: no aim point, camera ray or tolerance (`Commands_CarryNoCameraState_SoEveryPerspectiveSubmitsTheSameThing` stays green). `DrainCommands` routes it to `_building.Handle(c, WorldTick)`. Candidate parts carry the IDs `"candidate#{i}"`; committed parts are renamed `"{pieceId}#{i}"`. The command passes `_navigation` with navigation's authoritative scratch and counter sink (`_navigation.Scratch` and `_navigation.Counters`, internal accessors on `NavigationSystem`, lazily allocated); the preview passes `_navigation` with `_previewScratch` and a null sink.

**The fifteen checks, in order; the first failure is returned.** All fifteen are M7. Checks 1-14 land in E5 (check 15 answers `NotApplicable` for pads and roofs and `NotChecked` otherwise until E7). Territory, claim and permission-to-build checks are not in M7 (4.24).

| # | Rule | Exact test | Reason |
|---|---|---|---|
| 1 | `Actor` | the actor is the player and `PlayerCombat.Defeated` is false. There is no combat-phase "busy" clause | "unknown actor {id}" / "dead" |
| 2 | `Definition` | the ID is in the catalogue | "{id} is not a piece this build knows" |
| 3 | `Rotation` | r ∈ {0..3} ∩ `rotations` | "{name} cannot be turned that way" |
| 4 | `Lattice` | (x, z, r) satisfies the slot rule (4.2). A door needs an intact doorway with anchor (x, z) and r ≡ its r (mod 2) | "that is not on the building grid" |
| 5 | `BuildArea` | the slot geometry lies inside one build-area box, closed intervals. An edge on the boundary is inside; its 200 mm end extensions may reach past it | "you may only build inside a build area" |
| 6 | `Reach` | squared distance from the actor's body to the nearest point of `Bounds` ≤ `place_reach_mm`² (6000²), in `long` | "that is 7.20 m away; building reach is 6.00 m" |
| 7 | `Slot` | no intact piece holds the slot key | "a {Name} already stands there" / "that doorway already has a door" |
| 8 | `Support` | every mount socket has a compatible intact provider at the same world position and axis. A roof needs an intact `supports_roof` piece on one of its square's four edges, or an edge-adjacent intact roof that itself has one (depth 1, evaluated now only) | "a wall needs a floor pad on one side" / "a roof needs a wall under one of its edges, or a roofed neighbour that has one" |
| 9 | `Terrain` | pads only: max − min of `TerrainGrid.HeightAtMm` over a 13 × 13 sample lattice at 250 mm across the square (edges included) ≤ `pad_max_relief_mm` (250), integer | "the ground here is too uneven for a pad: 0.31 m of rise, 0.25 m allowed" |
| 10 | `Authored` | `Bounds` does not strictly overlap any authored static blocker, any authored door's closed box, or any barrier footprint, whatever their state. Box/box: minA < maxB ∧ minB < maxA on both axes; box/circle: squared clamp distance < R², `long`. Touching is not overlap | "that would build over something already standing there" |
| 11 | `Protected` | `Bounds` does not strictly intersect a protected zone (4.16) | "that ground is kept clear ({what})", e.g. "(Kera Voss's place)" |
| 12 | `Bodies` | pieces with parts only (a door is placed closed): no part strictly overlaps a body circle: the player (350), every NPC including companions (350), every living creature (its `RadiusMm`); `Separation(...) is not null` | "someone is standing there" |
| 13 | `PieceCap` | intact pieces anchored in this area < its `max_pieces` (256) | "this build area already holds 256 pieces" |
| 14 | `Materials` | for each cost line in order, carried **unequipped** stacks of that definition, ordered (quality asc, count asc, `ItemId` ordinal), cover the count; they become `Takes` | "needs {count} {itemId}" (the crafting wording; presentation maps IDs to names) |
| 15 | `Navigability` | a candidate with a solid part (wall, doorway, chest, bench) runs V-N1..V-N3; a door runs V-N4; pads and roofs skip (`NotApplicable`). Through `ctx.Navigation.CheckEdit` (4.17) | the first protected point's text, e.g. "that would cut Kera Voss's work place off" |

Checks 10 and 11 are defence in depth: BLD007 keeps shipped areas clear, so they are reachable only through a setup edited after load (the rule tests) or a later layout edit.

**Commit** (section 6 order):

```
string? Handle(PlacePieceCommand c, long tick):
    var check = BuildingRules.Validate(new PlacementContext(_context, _navigation, c.Actor, _navigation.Scratch, _navigation.Counters, true), ...)
    if (!check.Allowed) return check.Reason
    if (_context.Dispatch(new ExchangeItems(check.Takes, ItemId: null, Count: 0, Quality: 0)) is { } refused) return refused
    long seq = State.World.StructureSequence + 1
    var id   = EntityId.Derived(EntityKind.Piece, seq, "unnamed.piece/v1", c.Actor.Value)
    State.PlacePiece(_owner, new PieceRecord(id, def.Id, HostOf(c.XMm, c.ZMm), c.XMm, c.ZMm, c.Rotation, c.Actor, def.HealthMax), seq)
    Rebuild()                                                           // Space, leaves, socket index, footprints
    if (!check.Parts.IsEmpty)
        _context.Dispatch(new RebuildNavigation(UnionOf(check.Parts), StructureChangeKind.Placed, seq))
    Publish(new PiecePlaced(id, def.Id, c.XMm, c.ZMm, c.Rotation, c.Actor, seq, tick))
    Publish(new StructuresChanged(check.Bounds..., StructureChangeKind.Placed, seq, tick))
    return null
```

`ExchangeItems` with a null `ItemId` spends only, all or nothing, and mints nothing. A chest's container record materialises on first use, not at placement. Two commands for one slot in one drain are FIFO; check 7 refuses the second. `RebuildNavigation.Changed` is the union AABB of the parts, not inflated.

### 4.6 Placement preview

```csharp
// Simulation: the only addition to the method allow-list ("the placement ghost (M7)")
public PlacementPreview PreviewPlacement(string pieceDefId, long xMm, long zMm, int rotation, bool checkNavigability);
public sealed record PlacementPreview(bool Allowed, PlacementRule? Failed, string? Reason,
    long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, ImmutableArray<BoxBlocker> Parts,
    ImmutableArray<CostView> Cost, NavVerdict Navigability);
public sealed record CostView(string ItemId, int Count, int Carried);
```

1. **Same function.** The preview calls `BuildingRules.Validate` with the player as actor, `Navigation = _navigation`, `Scratch = _previewScratch` (a `Simulation` field, allocated lazily), `Counters = null`, and `CheckNavigability = checkNavigability`.
2. **Parity.** At equal state with `checkNavigability: true`, `(Allowed, Failed, Reason)` equals the command's, for all fifteen rules. With `false`, check 15 answers `NotChecked` for solids and doors.
3. **Isolation.** It never dispatches, publishes, registers, counts or writes (G7).
4. **Advisory.** State can move between preview and command; the command may still refuse (D-11). Presentation always submits the ghost's pose and shows the authority's refusal.
5. **Cadence.** Presentation asks for navigability only when the snapped pose or `StructureRevision` changes, or every 0.5 s (bodies move); checks 1-14 may run every frame.
6. **Ghost tone** (section 8): refused → red with the reason; allowed and `Proven`/`NotApplicable` → green; allowed and `NotChecked` → amber ("will be checked when placed").

### 4.7 Snapping

```csharp
// src/Domain/Building/Building.cs (pure; called by presentation)
public sealed record PiecePose(EntityId Id, string DefId, long XMm, long ZMm, int Rotation);
public static class Snapper
{
    public static (long XMm, long ZMm, int Rotation)? Snap(BuildingCatalog catalog, IReadOnlyList<PiecePose> pieces,
        string pieceDefId, long aimXMm, long aimZMm, int rotation);
}
```

The aim point is the camera-centre ray intersected with the domain `TerrainGrid` in C#; no engine physics query (G4). All divisions are floor divisions.
- **square, roof, furniture:** x = M·⌊aimX/M⌋ + H, z = M·⌊aimZ/M⌋ + H; r is kept.
- **edge:** r even: x = M·⌊aimX/M⌋ + H, z = M·⌊(aimZ + H)/M⌋. r odd: x = M·⌊(aimX + H)/M⌋, z = M·⌊aimZ/M⌋ + H. Unique by construction; an aim on a half-way line resolves by floor, never by a tie.
- **door:** candidates are intact doorways without a door whose anchor is within 2000 mm of the aim. Take the minimum squared distance (`long`); ties go to the lower anchor x, then the lower anchor z. Rotation r' = r if r ≡ r_doorway (mod 2), else (r + 1) mod 4; the two legal values only choose the leaf's swing side. No candidate → null (the ghost hides).

**Mating.** Socket positions are local integer mm, rotated by Rr and added to the anchor; axes rotate with the piece (x at odd r becomes z). A mount mates a provider by exact equality of world position, type pairing and world axis. **Tolerance is 0 mm**: every socket lies on a lattice position (BLD003) and every anchor is lattice-exact (check 4). A mount with several providers (a wall between two pads) is satisfied by any; none is recorded.

### 4.8 Foundations and terrain

1. **M7 has no walkable elevated surface.** Nothing a player builds is stood on. A body on a pad stands at `TerrainGrid.HeightAtMm(x, z)` exactly as beside it. Roofs are drawn slabs with no authority blocker. Landing on the chest or bench pushes the body off.
2. **A pad is exactly three things:** a placement anchor (four `edge` and one `square` provider), a terrain-relief gate (check 9, ≤ 250 mm), and a visual floor. It emits no blocker and no navigation footprint.
3. **Drawing** (section 8): the pad top is draped over the domain's own terrain samples, 20 mm up with a 150 mm skirt, so feet never float. The 250 mm cap keeps the drape reading as a floor. There is no terrain flattening.
4. **Shipped relief** (integer `HeightAtMm` over `content/regions/ashen_hollow.yaml`): the worst square in the area rises 228 mm (x 111-114, z 105-108), so all 81 squares take a pad; the four-cell square (99-102)² rises 96 mm.
5. **Height-blind low pieces.** The chest (700 mm) and bench (900 mm) are under the 1150 mm jump apex; the player can jump them. Both still block sight and shots, as the authored fallen timber does. Recorded, not changed.
6. A walkable floor would need a walk-surface rule in `Kinematics.Step`, surface heights in `Blocks`/`IsClear`/`CanStand`, 2.5-D navigation and a new meaning for saved body Y. Ruling 2 excludes it.

### 4.9 Doors

**Model.** A door piece is an authoritative open/closed blocker: `DoorOpen` on its row (flags are declared content, so a placed door cannot use one). `SystemContext.ClosedDoors()` returns authored closed doors, then standing barriers, then the cached closed piece leaves in `StructureOrder`, so every consumer (`Obstacles()`, `PersonObstacles`, creature obstacles, `SightWalls()`, prediction through `DynamicBlockers`) treats a closed piece door like a closed authored door.

**Who may operate** (`BuildingRules.CanOperate`, pure):

```
CanOperate(EntityId operatorId, string? operatorNpcId, PieceRecord door, RuntimeState s) =
      operatorId == door.Owner                                                       // the owner
   || (operatorNpcId is { } n && s.Companions.ContainsKey(n) && door.Owner == player) // the owner's companion
   || (operatorNpcId is { } m && s.World.NpcErrand(m) is { WorkOwner: var o } && o == door.Owner)   // working for the owner
```

Creatures never operate doors. Other NPCs never move in M7.

**Commands.**
- **Player:** the existing `InteractCommand(Actor, TargetKey)` with `TargetKey` = the door's `pce_` value. `InteractionSystem.Handle` tries a `pce_` target first and dispatches `OperatePieceDoor(id, player, null, Open: null)` (a toggle).
- **NPC:** `OpenDoor(string DoorKey, string NpcId)` (section 6), sent by the companion and the errand mover. A `pce_` key dispatches `OperatePieceDoor(id, NpcSystem.InstanceIdOf(npc), npc, Open: true)`. **NPCs open and never close.**
- **`OperatePieceDoor(EntityId PieceId, EntityId Operator, string? OperatorNpcId, bool? Open)`** to `BuildingSystem`, checks in order:
  1. the piece is an intact door ("there is no such door");
  2. `CanOperate` ("that door is not yours");
  3. the operator's body is within `InteractReachMm` (1600) of the closed box, `DistanceTo` ("the door is 1.80 m away; reach is 1.60 m");
  4. a close is refused while **any** body overlaps the closed box: the player, every `State.Npcs` body (companions included) and every living creature at its radius ("the door cannot close: something is in the doorway");
  5. asking for the current state is an accepted no-op (no event).

  On success: `SetPiece(row with { DoorOpen = open })`, `SetClosedPieceLeaves`, `DoorToggled(Operator, pieceId.Value, open, tick)`.
- **Toggles never rebuild navigation** and never change `StructureSequence`: gates are read at query time (G14; N-D5 extended).
- **Authored doors** get the same all-bodies close refusal, because NPCs now walk through them. There are no locks or keys in M7.

### 4.10 Materials

**Timber** (L3): one new item, one authored source, no renewable node.

```yaml
# content/items/material/timber.yaml
id: item.material.timber
kind: item
schema: 1
display_key: item.material.timber.name
tags: [item]
name: Rough Timber
notes: What M7's pieces are built from (content/pieces). Traders do not buy it.
category: material
stack_max: 20
weight: 0.5
value_base: 2
rarity: common
no_sell: true

# content/loot/timber_stack.yaml
id: loot.timber_stack
kind: loot
schema: 1
display_key: loot.timber_stack.name
tags: [loot]
notes: The timber stack by the crossing (M7) - all static, taken once.
rolls: 0
guaranteed:
  - { item_ref: item.material.timber, count_range: [80, 80] }

# content/regions/ashen_hollow.yaml, containers:
  - { key: container.timber_stack, loot_ref: loot.timber_stack, position_m: [84, 118], stack_slots: 12 }
```

The stack opens as four standard stacks of 20 and never refills (a touched container keeps its record). 80 covers the Crossing Workshop (45) plus repairs and experiments. Containers are layout, not generator input, so M6 saves load with no baseline transition. The stack is 5.0 m from the area corner (87, 114), outside it.

**Transactions** (existing internal commands; no construction currency, upkeep or progress; placement is instant):

| Operation | Items | Path |
|---|---|---|
| Place | spend each `cost` line (check 14's `Takes`) | `ExchangeItems(takes, null, 0, 0)`, before any building state changes |
| Repair | spend ⌈a / c⌉ per line, a = count × missing × `repair_cost_percent`, c = 100 × `health_max`, computed as (a + c − 1) / c in `long` | `ExchangeItems`, before the health change |
| Dismantle | grant ⌊count × `refund_percent` / 100⌋ per line, when > 0 | `GrantItem(itemId, n, Quality.Standard)` after removal; it merges into partial stacks before minting and falls back to the ground; it never refuses a known item |
| Destroy | nothing | - |

**Stack order (L5).** Check 14 (placement and repair) takes stacks by (quality ascending, count ascending, `ItemId` ordinal). Worst quality goes first because quality does nothing for a piece; equal-count stacks are interchangeable, so the counts left never depend on ID order. `InventorySystem.Put`'s merge order at `src/World/Runtime/Items.cs:576` and `:600` becomes (count descending, `ItemId` ordinal); section 6 (G20) owns that change and its test.

**`config.building`** (`content/config/building.yaml`):

```yaml
id: config.building
kind: config
schema: 1
display_key: config.building.name
tags: [config]
notes: Building v1 (M7). Socket/snap on a 3 m lattice, quarter turns, one storey (D-08; owner rulings 1-2).
module_m: 3.0              # save-locked: anchors are absolute
rotation_step_deg: 90      # quarter turns only (owner question Q1)
place_reach_m: 6.0         # body centre to the nearest point of the piece's bounds
pad_max_relief_m: 0.25     # highest minus lowest ground under a pad, sampled every 0.25 m
refund_percent: 50         # of each cost line, rounded down, when taken down
repair_cost_percent: 100   # of each cost line, scaled by the health missing, rounded up
protection:
  spawn_point_m: 3.0       # the region spawn
  npc_site_m: 1.5          # each authored NPC site
  site_m: 1.5              # authored containers, stations and nodes
  structure_margin_m: 1.5  # round authored door boxes, switch structures and barriers
  spawner_margin_m: 1.0    # beyond a spawner's disc (and each patrol leg) plus its largest member's radius
damage:
  melee: 10                # the only M7 damage source
```

There is no `navigability_radius_m`: the navigation `person` class is the one source of the body radius (D22).

### 4.11 Ownership

- `Owner` is the `chr_` of the placing character. Transfer is not in M7.
- It gates: dismantle ("that is not yours to take down"), repair ("that is not yours to mend"), assign and release, chest moves (`InventorySystem.Check` gains a refusal "that chest is not yours" when `site.Owner` is set and differs from the actor) and piece-door operation (`CanOperate`).
- It does not gate damage.
- It is a building fact only. Nothing in M7 derives crime, hostility, standing, legality or attack permission from it, and building dispatches no `RecordAct` (G13). Foreign-owned pieces exist only in crafted saves and the v14 fixture.

### 4.12 Health, the one damage rule, repair

**The damage table** (L2: exactly one source).

| Key | Producer (one code point) | Rule | Amount |
|---|---|---|---|
| `melee` | `CombatSystem`'s melee branch, at the active window's last tick, when no creature was struck (`src/World/Runtime/Combat.cs:506`) | `FirstStop(body, attack.ReachMm)` yields a piece part → `DamagePiece(pieceId, 10, "melee")`, which replaces that swing's `AttackMissed` | 10 |

Every other source is reserved (4.24). Arrows and formulas stop at piece walls and damage nothing; a boar charge ending on a piece stuns the boar and damages nothing. Hitting a piece practises no skill and awards no XP. No rule reaches the stored health of pads and roofs.

**Which part a blow hit: `FirstStop`** (World, private to `CombatSystem`). `Trace` (`Combat.cs:523-545`) is rewritten to call it with unchanged outputs:

```
(double ClearMm, ImmutableArray<Blocker> Hit) FirstStop(Body from, long lengthMm):
    (dx, dz) = the facing's Sin/Cos exactly as Trace computes them today
    walls    = _context.SightWalls()                      // Space.Blockers ∪ ClosedDoors()
    if no wall Crosses(from, from + d·length): return (length, [])
    bisect exactly as Trace does → clear, reach           // reach − clear ≤ 10 mm
    return (clear, walls.Where(b => b.Crosses(from, from + d·reach)))   // SightWalls() list order
```

A piece is struck only if **every** blocker in `Hit` is a piece part; then the first element of `Hit` (list order, which is geometric: `StructureOrder` within each list). An authored blocker, door or barrier in `Hit` shields the piece and wins the tie. Pieces cannot strictly overlap authored geometry (check 10), and the rule adds no new float arithmetic.

**`DamagePiece(EntityId PieceId, int Amount, string Source)`** to `BuildingSystem`: no piece → "there is no such piece"; `Amount < 1` → "no damage"; `to = max(0, HealthCurrent − Amount)`; `to == 0` → destroy (4.13); else `SetPiece(row with { HealthCurrent = to })` and `PieceDamaged(id, def, Amount, to, Source, Now)`. Damage changes no footprint: no sequence change and no navigation rebuild.

**Repair.** `RepairPieceCommand(EntityId Actor, EntityId PieceId)`. Refusals in order: actor ("unknown actor", "dead"); "there is no such piece"; "that is not yours to mend"; reach (check 6's rule); full health ("it needs no repair"); materials (check 14 over the scaled cost). Success: `ExchangeItems`, `HealthCurrent = health_max`, `PieceRepaired(PieceId, From, To, Tick)`. One command, one cost, no partial repair. A wall at 170/200 costs ⌈2 × 30 × 100 / 20 000⌉ = 1 timber.

Blows to destroy: walls and doorways 20, door 12, chest 10, bench 30.

### 4.13 Removal: dismantle and destroy

**Dismantle.** `DismantlePieceCommand(EntityId Actor, EntityId PieceId)`. Refusals in order: actor ("unknown actor", "dead"); "there is no such piece"; "that is not yours to take down"; reach (check 6's rule); dependents ("take the door down first" / "take down what stands on it first"); a storage piece whose container record holds any item ("empty the chest first"). Dependents come from the socket index: a doorway's is its door; a pad's are the furniture on its square and every wall-family piece on its edges whose other adjacent square has no intact pad. Nothing else has dependents; roofs never do. Removal only adds connectivity, so dismantle runs no navigability check.

**`RemoveCore(piece, kind, source)`**, shared by both (section 6 order):
1. a station named by a `to_work`/`at_work` errand: `EndWork(npcId, "dismantled" | "destroyed")`;
2. a storage piece with a container record: `DiscardContainer(key)` (dismantle; the record is empty by the last refusal) or `SpillContainer(key)` (destroy);
3. `seq = StructureSequence + 1`; `State.RemovePiece(_owner, id, seq)` retires the `pce_`;
4. `Rebuild()`; if the piece has parts, `RebuildNavigation(UnionOf(parts), kind, seq)`;
5. `PieceRemoved(PieceId, DefId, Actor, Refund, Revision, Tick)` or `PieceDestroyed(PieceId, DefId, Source, Revision, Tick)`, then `StructuresChanged`;
6. dismantle only: one `GrantItem` per refund line. `PieceRemoved.Refund` lists the lines as `CostView(ItemId, Count, Carried: 0)`.

**Destroy** (health reaches 0) differs from dismantle in exactly three ways:
1. no refund, and no owner or reach check: the rule destroys, not a player;
2. **a destroyed doorway destroys its door first** (L9): the door gets its own `RemoveCore` (sequence + 1, one `RebuildNavigation`, `PieceDestroyed` with the doorway's source), then the doorway gets its own. No other cascade exists;
3. **chest spill**: `SpillContainer(string Key)` to `InventorySystem` moves each `ContainerItem`, in record order, to the ground at the container site point, one created stack each, **keeping its item ID** (`State.PlaceItem` at cell-relative cm; `PlaceItem` never touches the registry). It then calls `ReleaseContainer(key)`, which retires only the `cnt_`. `RemoveContainer` would retire every item ID, which would be item loss.

**The C1 fix.** `InventorySystem.Take`'s corpse clause (`src/World/Runtime/Items.cs:558`) becomes:

```csharp
if (items.IsEmpty && _context.Setup.Layout.FindContainer(site.Key) is null && site.InstanceId is null)
```

An emptied piece chest keeps its empty record while the piece stands. Without the clause, emptying retires the derived `cnt_`, which the registry only tombstones, so the next deposit throws in `CreateEntity`, and only in the unsaved run. Corpses and merchants are unchanged (their `InstanceId` is null). T10 `BuildingCommands_NeverThrow` (section 14) and the two chest-identity tests guard the class.

### 4.14 The storage chest and the anvil bench

**Chest: the container model reused.** Each intact storage piece yields a synthesized `ContainerSite`:
- key `"container." + pieceId.Value.ToLowerInvariant()` (a valid definition-ID shape, which `Materialize` parses);
- `LootTableId` `""`; position = the world point of `container.at_m`; `StackSlots` from the definition;
- init properties `InstanceId = Derived(Container, pieceId.Timestamp, "unnamed.piece-container/v1", pieceId.Value)` (the piece's ordinal) and `Owner = piece.Owner` (new on `ContainerSite`, null for authored sites).

Reuse: `FindContainer` checks authored, corpse, merchant, then piece sites; `Baseline` is empty for `LootTableId == ""` on a non-merchant; `Materialize` uses `site.InstanceId` instead of minting; `Check` adds the owner refusal; `Simulation.Containers` appends piece chests. The record is the schema-6 `ContainerRecord`, hosted in the site point's cell.

**Bench: the M3f station model reused.** `piece.station.anvil` has kind `anvil`, the March Spear's station (`content/recipes/smithing/march_spear.yaml:8`). No new recipe. Each intact station piece yields `StationSite("station." + pieceId.Value.ToLowerInvariant(), kind, part centre)`. `SystemContext.Stations()` returns authored sites (content order), then piece sites in `StructureOrder`; `CraftingSystem` reads it instead of `Setup.Layout.Stations` (`src/World/Runtime/Crafting.cs:168`), and presentation reads `Simulation.Stations`. A removed bench leaves no site; the next craft gets the existing "no anvil in reach". The craft uses the existing `"craft"` random channel.

### 4.15 NPC work-anchor assignment

**Data.**
- The station definition gives `work_anchor_m` and `work_facing_deg`: world anchor = piece anchor + Rr(local); facing = (local + r · 90°) mod 360°.
- `NpcDefinition` gains `ImmutableArray<string> WorksAt { get; init; }` (default empty). Only `content/npcs/ashen_hollow/kera_voss.yaml` sets it: `works_at: [anvil, forge]`. `SocialContent` parses it explicitly.
- The errand is the single source of truth for an assignment. `NpcSystem` owns `StateSlice.NpcErrands` beside `Npcs` (D1):

```csharp
// src/World/WorldDelta.cs
public enum NpcErrandPhase { ToWork, AtWork, ToHome }       // keys "to_work" | "at_work" | "to_home"
/// A named NPC away from their place (M7), anchored to the cell of the NPC's authored site with absolute mm.
/// Absent means the NPC stands at their site: the baseline.
public sealed record NpcErrandRecord(string NpcId, string HostCell, NpcErrandPhase Phase, EntityId? PieceId,
    EntityId? WorkOwner, long XMm, long ZMm, int FacingMdeg, string? BaselineHash = null)
{
    public NavRoute Route { get; init; } = NavRoute.None;   // section 3's type; required on the DTO
    public int StuckTicks { get; init; }
}
```

Invariants: `ToWork`/`AtWork` name an intact station owned by `WorkOwner`; `ToHome` has a null `PieceId` and keeps `WorkOwner` for door permission on the way out; one errand per NPC; at most one `ToWork`/`AtWork` per piece; companions never have one. The goal is derived, never stored: the work anchor (from the row plus its definition) for `ToWork`/`AtWork`, the `NpcSite` for `ToHome`.

**Commands** (`BuildingSystem` validates, `NpcSystem` commits):

```csharp
public sealed record AssignWorkerCommand(EntityId Actor, string NpcId, EntityId PieceId) : GameCommand(Actor);
public sealed record ReleaseWorkerCommand(EntityId Actor, string NpcId) : GameCommand(Actor);
internal sealed record BeginWork(string NpcId, EntityId PieceId, EntityId Owner, long AnchorXMm, long AnchorZMm, int FacingMdeg) : InternalCommand;
internal sealed record EndWork(string NpcId, string Reason) : InternalCommand;   // released | dismantled | destroyed
```

**Assign refusals, in order:**
1. actor: "unknown actor {id}" / "dead";
2. the NPC is not in `State.Npcs`: "there is no one called {npcId} here";
3. the NPC is a companion: "{name} travels with you";
4. the player's body is not within `TalkReachMm` (1950) of the NPC's body: "{name} is out of reach" (asking is in person);
5. the piece is not an intact station: "there is no such station";
6. the actor does not own it: "that {station} is not yours";
7. `WorksAt` lacks the station's kind: "{name} does not work an {kind}";
8. another `ToWork`/`AtWork` errand names the piece: "someone already works there";
9. the NPC has a `ToWork`/`AtWork` errand: "{name} already works for you" (a `ToHome` errand is replaced);
10. `NavigationSystem.Reachable(NavAgent(person, OpensDoors: true), body, anchor)` is false: "{name} cannot get there" (authoritative planner; barriers by current flags).

Then `BeginWork`: `NpcSystem` writes the errand (`to_work`, pose = the current body, route `None`, stuck 0, host = the site's cell) and publishes `WorkerAssigned`. No footprint changes.

**Release refusals:** actor; no `ToWork`/`AtWork` errand at a piece the actor owns ("{name} does not work for you"); talk reach ("{name} is out of reach"). Then `EndWork(npc, "released")`: `to_home`, `PieceId` null, route `None`, stuck 0, `WorkerReleased`. Dismantle and destroy send `EndWork` with their reason.

**Phases.** `to_work` → `at_work` on exact arrival; `at_work` → `to_home` on `EndWork`; `to_work` → `to_home` on `EndWork`; `to_home` → `to_work` on a new assign; `to_home` → removed on exact return.

**The mover** (`src/World/Runtime/Errands.cs`, `partial class NpcSystem`; section 3 owns `Follow`):

```
NpcSystem.Tick(tick):
  foreach errand in State.World errands, ordinal by NpcId:          // no tier gate; never reads State.Conversation
    npc = State.Npcs[errand.NpcId]; goal = Goal(errand)              // (x, z, facing)
    if Phase == AtWork: turn towards goal facing (18 000 mdeg/tick); write if changed; continue
    if body.(x, z) == goal.(x, z):
        turn towards goal facing (18 000 mdeg/tick); when equal: ToWork → AtWork, Route = None, StuckTicks = 0, NpcArrivedAtWork
                                                                  ToHome → RemoveNpcErrand, NpcReturnedHome
        write; continue
    step = _navigation.Follow(NavAgent(person, OpensDoors: true), errand.Route, body, goal, errand.StuckTicks, tick)
    Unreachable → face the goal; StuckTicks + 1; Route = step.Route
    OpenGate    → refused = Dispatch(new OpenDoor(step.GateKey, npcId)); face the door;
                  StuckTicks += refused is null ? 0 : 1; Route = step.Route
    Walk/Arrive → intent at Gait.Walk (80 mm/tick), exact landing when within 80 mm of the goal;
                  Kinematics.Step(body, intent, Movement, _context.Space, _context.PersonObstacles(npcId), TickMs);
                  StuckTicks = headway ? 0 : StuckTicks + 1
    write the body (Npcs) and the errand (pose, route, stuck) in the same tick           // G18
  then the existing facing loop over NPCs that are neither companions nor on an errand
```

- **Never teleport.** Arrival and retirement need exact equality of x, z and facing; a body a few mm off keeps its record until exact (store what diverges, never snap).
- **Facing.** An errand NPC's facing is written only by the mover. At work she keeps the work facing even while talking; the facing loop skips her (L5 deletes D27's hold).
- **Conversation.** `DialogueSystem.Handle(TalkCommand)` refuses an NPC whose persisted phase is `to_work` ("{name} is walking to work") or `to_home` ("{name} is walking home"). At work, or at home with no record, she talks and trades normally. An open conversation whose NPC walks off is ended by the existing reach check (`Social.cs:294`).
- **Trade.** Talk and trade reach are measured to her body; her wares stay at her site (`Systems.cs:61-64`), unchanged (D12).
- **Recruit.** `CompanionSystem.Handle(Recruit)` refuses an NPC that has an errand: "{name} cannot join you while on an errand".
- **`Populate(companions)`** places an errand NPC at the errand pose, publishes nothing, and repairs, each with an `ErrandAudit` line (shown in the `StructureAudit` view; 4.4): an errand whose NPC is a saved companion is dropped; an errand for an NPC with no `NpcSite` is dropped; a `to_work`/`at_work` errand whose piece is not an intact station becomes `to_home` with route `None`; an `at_work` errand whose pose is not the anchor becomes `to_work`; an errand whose NPC's site moved to another cell is re-hosted, and its `BaselineHash` is restamped from the new host at the next `TakeSnapshot`.
- There is no production, schedule, wage, housing or hireling. "Work" is standing at the bench.

### 4.16 The build area

```yaml
# content/regions/ashen_hollow.yaml, after stations:
# Where the player may build (M7): boxes on the 3 m building lattice, clear of authored geometry, spawns and NPC stands.
# It sits on the four-cell corner (100, 100), so a structure there straddles both seams (ROADMAP M7 exit).
build_areas:
  - { key: build_area.hollow_crossing, box_m: [87, 87, 114, 114], max_pieces: 256 }
```

`RegionLayout` gains `ImmutableArray<BuildAreaSite> BuildAreas` with `BuildAreaSite(string Key, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, int MaxPieces)`. Build areas are layout, not baseline.

**Geometry.** 87 = 29 × 3 and 114 = 38 × 3: 9 × 9 = 81 squares, i and k ∈ 29..37. Square (33, 33), x and z 99-102, contains (100, 100). The area meets all four cells: `c_00_01` (x < 100, z ≥ 100), `c_01_01`, `c_00_00`, `c_01_00`.

**Why here.** It is the one place near the waystation (the corner is 36 m from Sel's stand and 55 m from Kera's) where one lattice square contains both seams, which delivers RK-A2's "a building straddling four cells" with one pad. Every square takes a pad (worst relief 228 mm), and no quest gate is needed. Widening it is data, subject to BLD008 and BLD009.

**Clearances** (grown box [86.8, 114.2]²): `tree_25` (112, 86, radius 0.4) is 0.4 m outside it; the valley strays' protection (6 + 0.45 + 1 = 7.45 m) against 14.56 m to (114, 114); Sel's site 17 m; every door, switch, barrier, station, other spawner and the spawn further.

**Protected zones** (check 11 and BLD007; strict intersection with a piece's bounds):
- the region spawn circle (3 m);
- each authored NPC site circle (1.5 m);
- each authored container, station and node point circle (1.5 m);
- each authored door's closed box, each switch structure and each barrier footprint, grown by 1.5 m;
- each spawner's disc of radius `radius + largest member radius + 1 m`;
- each patrol leg, sampled every 500 mm (endpoints included) as circles of that radius.

**Seal limit and ceilings.** BLD008: the area grown by 200 mm is 27.4 m square, 750.8 m² (12 013 nodes of 250 mm) ≤ `seal_limit_nodes` (16 384); filled solid it disconnects nothing outside. BLD009: 256 pieces per cell and per region.

### 4.17 Navigability: what building passes to navigation

Section 3 owns `NavEditCheck.Check` and rules V-N1..V-N4. Building calls it as check 15 and supplies the inputs.

- **When.** A candidate with a solid part (wall, doorway, chest, bench) sends `addSolids` = its solid parts as `NavInput`s and runs V-N1..V-N3. A door sends `addDoors` = its leaf and runs V-N4. Pads and roofs never call it. Dismantle never calls it.
- **Graph.** The `person` class; every door (authored or placed, open or closed) and every barrier (standing or lifted) is passable. The verdict is a pure function of content, the piece set and the protected points.
- **One rule (L6).** Any newly sealed walkable pocket is refused, whether or not a protected point is inside. A doorless one-square hut is refused: "that would close off a space with no way in; rooms need a doorway". With a protected point inside, the first in list order names the refusal.
- **Call.** Check 15 calls `ctx.Navigation.CheckEdit(addSolids, addDoors, points, ctx.Scratch, ctx.Counters)`: the command with the authoritative scratch and sink, the ghost (`Simulation.PreviewPlacement` → `BuildingRules.Validate`) with `_previewScratch` and a null sink (4.5, 4.6). It maps `Ok` → `Proven`, otherwise `Refused`.

**Protected points** (`BuildingRules.ProtectedPoints(state, candidate)`; only points whose target meets the check window are passed):

| Order | Kind | Points | RadiusMm | ReachMm |
|---|---|---|---|---|
| 1 | NewSite | the candidate chest's site; the candidate station's work anchor | 0 / 350 | 1600 / 250 |
| 2 | WorkAnchor | anchors of `to_work`/`at_work` errands, by NpcId | 350 | 250 |
| 3 | Body | the player; companions by NpcId; errand NPCs by NpcId (never creatures) | 350 | 1600 |
| 4 | NpcSite | every authored NPC site, by NpcId | 350 | 1600 |
| 5 | Spawn | the region spawn | 350 | 1600 |
| 6 | Reach | authored containers by key; corpses by key; piece chests by piece ID; authored stations by key; piece stations by piece ID; nodes by name; switches (target = the switch body's AABB); created ground stacks by (x, z, def, count), never by item ID | 0 | 1600 |
| 7 | DoorApproach | authored doors by key, then placed doors in `StructureOrder`: leaf centre ± (half thickness + 700 mm) along the thin axis | 0 | 250 |

V-N3 (L6): a `NewSite` with `RadiusMm > 0` (the station anchor) must be standable at that radius and lie in an open component; with `RadiusMm = 0` (the chest site, which lies inside the chest's own box) it needs a walkable node within `ReachMm` in an open component. Refusal texts by kind: WorkAnchor "that would cut {npc}'s work place off"; the player's Body "that would shut you in"; another Body "that would shut {name} in"; NpcSite "that would wall in {name}'s place"; NewSite "that would shut in the {piece}"; Reach "that would shut {label} away"; DoorApproach "that would close off {door}".

### 4.18 Instance IDs

```csharp
// src/Domain/EntityId.cs, beside Create
/// A runtime identity derived from what made it: the same commands make the same IDs, so a replay and a
/// save-then-continue mint identically. The timestamp field holds the ordinal, not a time (M7 pieces).
public static EntityId Derived(EntityKind kind, long ordinal, string tag, string salt)
{
    using var h = new CanonicalHasher();
    return Create(kind, ordinal, h.Add(tag).Add(salt).Add(ordinal).FinishBytes().AsSpan(0, 10));
}
```

| Instance | Derivation |
|---|---|
| piece `pce_` | `Derived(EntityKind.Piece, StructureSequence + 1, "unnamed.piece/v1", owner.Value)` |
| piece chest `cnt_` | `Derived(EntityKind.Container, the piece's ordinal, "unnamed.piece-container/v1", pieceId.Value)` |

- `EntityKind.Piece` (`pce`) is appended after `Character`; no value shifts; `TryInferFromDefinition` is unchanged.
- Ordinals strictly increase per world and are never reused, so a tombstone never blocks a later ID. The owner's `chr_` salt separates characters. IDs sort by ordinal.
- Every derived ID is registered through `registry.CreateEntity(DefinitionId.Parse(defId), id)` inside the `WorldDelta` mutators. `BuildingSystem` never calls `NewId` (G8). Item IDs are minted only through the existing item commands (refund `GrantItem`, splits, takes).
- NPC and creature derivations are not moved onto `Derived`: re-keying would change every saved ID.
- **D-04 note (L9).** E0 adds a dated note to `docs/DECISIONS.md` D-04: "Derived identities (NPC since M4, creature since M3d, piece and piece chest since M7) are ULID-shaped with an ordinal or constant timestamp. They are replay-stable, never time-ordered across worlds, and never compared across saves. The piece ordinal is a per-world persisted counter, acceptable because single-player saves never merge." E2 amends the `EntityId.Create` and `Timestamp` doc comments in the same commit as `Derived`; `docs/DATA_MODEL.md:123` and AGENTS.md name `EntityId.Derived`. `TryApplyPiece` documents that it reads `Timestamp` as the derivation ordinal. Not an owner question: the NPC and creature precedents exist.

### 4.19 Persistence (summary; section 7 is normative)

- **Where.** `entities.msgpack` gains `pieces` (sorted by `instance_id`; keys `instance_id`, `def_id`, `host_cell`, `x_mm`, `z_mm`, `rotation`, `owner`, `health`, `door_open`; about 180 bytes a row), `structure_seq` and `npc_errands` (sorted by `npc_id`, each with a required `route`). Piece chests are ordinary `containers` rows. No new section file; one step 13 → 14 shared with navigation and factions; every field required on decode and defaulted only by the step (`[]`, `0`, `[]`).
- **Load.** `FromSnapshot` order: `StructureSequence`, cells, entities, created, pieces, containers, creatures, errands. `TryApplyPiece` checks host cell and hash, kind `Piece`, `DefId` shape, owner kind `Character`, rotation 0..3, health ≥ 1, `HostCell` = the anchor's cell, `InstanceId.Timestamp ≤ StructureSequence`, and not seen; it does not compare the ID with `Derived(…, Owner)` (the transfer seam). `TryApplyContainer` needs a `container.pce_*` chest's piece and derived `cnt_`. `TryApplyNpcErrand` checks the phase invariants, `WorkOwner` = the piece's owner for working phases, and one working errand per piece.
- **Definition pass** (on a content-hash change): piece `def_id` renamed, or the piece dropped as a loss; a dropped storage piece **spills** its chest's items to the ground at its host cell with their IDs; errand `npc_id` renamed or dropped. The pass and the rebase use `with` copies, so `StructureSequence` survives (G11).
- **Digests.** `unnamed.effective-cell/v2` hashes pieces by ID (id, def, x, z, rotation, owner, health, door_open) then errands by NpcId; `unnamed.simulation/v2` hashes `StructureSequence` after the player digest (G12).
- **Load audit** (`BuildingSystem.Populate`; kept and reported in `StructureAudit`, never silently dropped): pieces now off-lattice, outside every area, over authored or protected ground, or unsupported; `health_current` above a retuned `health_max` (clamped); `door_open` on a non-door (treated false). The authored layout is in no `baseline_hash`, so pieces under a later layout edit are audited, not proven.

### 4.20 Content kind and lints

| Item | Value |
|---|---|
| Kind | `piece`, registered in `src/Content/SchemaResolution.cs` in the `faction` pattern |
| Directory | `content/pieces/<family>/<name>.yaml` (the loader recurses) |
| ID shape | `piece.<segment>.<name>` |
| Reference suffix | `piece_ref` → `piece` (`src/Content/ContentChecks.cs`) |
| Validator | `BuildingContent.Validate`, after `NavigationContent`, before `FactionContent` (all after `QuestContent`) |
| Tests touched | `LoadAll_Loads_Yaml_Files` gains the 10 building IDs (7 pieces, `config.building`, `item.material.timber`, `loot.timber_stack`); `KnownDirectories_Is_Closed_Set` may add `pieces` (it asserts `Contains` only) |

| Code | Checks |
|---|---|
| BLD001 | the catalogue builds (one `Try` wrapper), naming the definition's source file |
| BLD002 | closed field set; closed enums (`family`, `slot`, `traversal`, socket `type`/`axis`); slot legal for the family; `rotations` non-empty, distinct, ⊆ {0..3}; `health_max` ≥ 1 on every family; the ID's second segment is not tied to `family` |
| BLD003 | local lattice consistency: `edge` providers at (0, ±1500) axis x or (±1500, 0) axis z; `square` and `door` providers and every mount at (0, 0); square and roof bounds exactly the square; edge bounds within [−1700, −200, 1700, 200]; furniture bounds within [−1300, −1300, 1300, 1300]; parts inside bounds, `height_m` > 0, no clearance key; `door` parts only on doors |
| BLD004 | every `cost` line names an existing `item` definition, count ≥ 1 |
| BLD005 | `container` only on storage (`stack_slots` ≥ 1, `at_m` inside bounds); `station` only on stations, with a recipe-used `kind` and a work anchor inside the square at least person radius + 300 mm (650) from every part and from the ±1300 faces; every NPC `works_at` kind is recipe-used; every build-area corner within 85 m per axis of every `works_at` NPC's site (85 = `window_max_m` − 2·`window_margin_m` − `start_snap_m` − `goal_snap_m` from `config.navigation`; Kera: 52.6 m) |
| BLD006 | `config.building` present iff any piece or build area exists; `module_m` 3.0 and a whole multiple of `config.navigation.node_m`; `rotation_step_deg` 90; reach 1-12 m; relief 0-1 m; refund 0-100; repair 0-1000; protections ≥ 0; `damage` keys closed to {`melee`}, each ≥ 1 |
| BLD007 | build-area corners on the lattice; the area grown by 200 mm overlaps no authored structure, door box or barrier and meets no protected zone (spawner discs from `CombatContent`) |
| BLD008 | (a) each area grown by 200 mm holds ≤ `seal_limit_nodes` nodes; (b) filled solid, it disconnects no walkable person node outside it from the spawn, every gate passable |
| BLD009 | `BuildingConstants.PiecesPerCellCeiling = 256` and `PiecesPerRegionCeiling = 256`: the sum of `max_pieces` over the areas meeting each cell, and each region, is within the ceiling |
| WLD015 | build-area shape (`WorldContent`): `build_area.` prefix; min < max; inside region bounds and covered cells; unique keys; `max_pieces` ≥ 1 |

`BuildingConstants.Problem()` validates the built constants in the `CompanionTuning` pattern.

### 4.21 The greybox catalogue

Local mm at r0; origin at the square centre or the edge midpoint. Every cost is `item.material.timber`.

| ID (display, DRAFT) | Family / slot | Blocking parts (box; height) | Bounds | Sockets and extras | Cost | `health_max` |
|---|---|---|---|---|---|---|
| `piece.pad.timber` (Timber Pad) | pad / square | none | [−1500, −1500, 1500, 1500] | 4 `edge` + 1 `square` providers; relief ≤ 250 | 1 | 200 |
| `piece.wall.timber` (Timber Wall) | wall / edge | [−1700, −200, 1700, 200]; 3000; solid | = part | `edge_mount` axis x; `supports_roof` | 2 | 200 |
| `piece.doorway.timber` (Timber Doorway) | doorway / edge | [−1700, −200, −800, 200] and [800, −200, 1700, 200]; 3000; solid | [−1700, −200, 1700, 200] | `edge_mount` and `door` axis x; `supports_roof`; opening 1600; lintel 2400-3000 drawn only | 2 | 200 |
| `piece.door.timber` (Timber Door) | door / door | [−800, −200, 800, 200]; 2400; door | = part | `door_mount` axis x; `DoorOpen` | 1 | 120 |
| `piece.roof.timber` (Timber Roof) | roof / roof | none | the square | support rule (check 8); drawn as a 200 mm slab at wall top | 1 | 100 |
| `piece.storage.chest` (Storage Chest) | storage / furniture | [−500, 600, 500, 1200]; 700; solid | = part | `square_mount`; `container: { stack_slots: 12, at_m: [0, 0.9] }` | 2 | 100 |
| `piece.station.anvil` (Anvil Bench) | station / furniture | [−500, 400, 500, 1000]; 900; solid | = part | `square_mount`; `station: { kind: anvil, work_anchor_m: [0, −0.25], work_facing_deg: 0 }` | 4 | 300 |

Why these numbers:
- **Walls 400 mm thick, 3000 mm tall, centred on the lattice line**, as the authored walls; one wall serves two rooms, and the ±200 end extensions fill corners, so rooms seal except at doorways.
- **Doorway opening 1600 mm, door 2400 mm**, matching both authored doors. A 350 mm body passes at local x ∈ [−450, 450]. The closed leaf fills the opening exactly, so a closed door is a continuous wall.
- **Chest** site (0, 900) lies inside its own box, so V-N3 treats it as a reach point. **Bench** anchor (0, −250) is 650 mm from its face, clear for a 350 mm body; the bench costs no iron because Ashen Hollow's iron is finite.

### 4.22 Acceptance: the Crossing Workshop

A 2 × 2 workshop over the four-cell corner: its south doorway straddles x = 100, the bench straddles x = 100, and Kera's route crosses z = 100 inside it.

**Where the code lives.** One command table, `CrossingWorkshop.Rows`, and the start builder `CrossingWorkshop.Start()` live in `src/Application/Evidence/CrossingWorkshop.cs` (data and a `PlayerRecord` builder, beside `StateDump`). `tests/Application.Tests/BuildingAcceptanceTests.cs` runs them headless; `src/Presentation/BuildShots.cs` runs the same rows as `--build-shots` beats (L8). Each row names the slice that adds it; a slice runs every row whose slice has landed, in order.

**S0 (start).** `CrossingWorkshop.Start()`: a new character's `PlayerRecord` at **(100.5, 94.5) facing 0**, world seed `Playthrough.Seed`, tick 0, plus:
- 45 `item.material.timber` as standard stacks of 20, 20 and 5; one `item.material.iron_ingot`; one `item.material.ash_haft`;
- `recipe.smithing.march_spear` known;
- Tavar Orr recruited (`CompanionRecord`, order `Wait`, up) at (100.5, 108.5) facing 180°.

Carried weight is about 28.5 kg. An env-gated test (`UNNAMED_WRITE_BUILD_START=1`) writes it once, in E5, to `tests/Application.Tests/Saves/m7_crossing_start/`; `TheCommittedBuildStart_IsTheCrossingWorkshopStart` checks the committed save against the builder after the definition pass. `--build-shots` copies it into a temporary profile.

**Notation.** Poses are metres and degrees. `@(x, z) f`: `Harness.WalkTo(…, Gait.Walk, toleranceMm: 50)` after the listed run waypoints (`→`, tolerance 300), then one frame of `MoveCommand(Idle(f·1000))`. Tick `pose` = the first boundary after the pose is reached; `+n` = n ticks after the previous row; `until` = frames until the event, with a cap. The first green run records absolute ticks in `commands.tsv`; the replays use them. `Place(def, x, z, r)` = `PlacePieceCommand(player, …)` in mm with the seven short names of 4.21; Kera and Tavar are their `npc.ashen_hollow.*` IDs. "E6+: command" means the pose row runs from its slice and the command only from E6. A waypoint that snags in the first green run is a script fix, not a design change.

| Row | Step | From | Tick | Player pose | Command | Expect |
|---|---|---|---|---|---|---|
| R00 | 0 | E5 | 0 | (100.5, 94.5) f0 | load S0 | - |
| R01 | 1 | E5 | pose | @(102.0, 102.0) f0 | - | - |
| R02 | 1 | E5 | +1 each | same | `Place(pad, 100500, 100500, 0)`, `(pad, 103500, 100500, 0)`, `(pad, 100500, 103500, 0)`, `(pad, 103500, 103500, 0)` | seq 1-4; pad (33, 33) hosted in `c_01_01`; no `NavigationRebuilt` |
| R03 | 1 | E5 | +1 | same | `Place(doorway, 100500, 99000, 0)` | seq 5; opening x 99 700-101 300 straddles x = 100 |
| R04 | 1 | E5 | +1 each | same | `Place(wall, …)`: (103500, 99000, 0), (100500, 105000, 0), (103500, 105000, 0), (99000, 100500, 1), (99000, 103500, 1), (105000, 100500, 1), (105000, 103500, 1) | seq 6-12; R03-R04 publish 8 `NavigationRebuilt` |
| R05 | 1 | E5 | +1 each | same | `Place(roof, …)` on the four square anchors, r0 | seq 13-16; no rebuild |
| R06 | 1 | E6 | +1 | same | `Place(door, 100500, 99000, 0)` | seq 17; one rebuild; placed closed |
| R07 | 1 | E8 | +1 | same | `Place(bench, 100500, 103500, 3)` | seq 18; part x [99 500, 100 100], z [103 000, 104 000]; site (99 800, 103 500); anchor (100 750, 103 500) facing 270 000 |
| R08 | 1 | E8 | +1 | same | `Place(chest, 103500, 103500, 0)` | seq 19; part x [103 000, 104 000], z [104 100, 104 700]; site (103 500, 104 400); totals below |
| R09 | 2 | E5 | +1 | same | `Place(wall, 100500, 99000, 0)` | refused `Slot`, "a Timber Doorway already stands there"; `StateDigest` unchanged |
| R10 | 3 | E5 | pose | @(100.5, 100.3) f180 | E6+: `InteractCommand(door)` | opened |
| R11 | 3 | E5 | pose | @(100.5, 97.6) f0 | E6+: `InteractCommand(door)` | closed |
| R12 | 3 | E5 | pose | @(100.5, 97.0) f0 | `MoveCommand(north, Walk)` × 40 ticks | E6+: body z ≤ 98 450 on every tick and ≥ 98 400 at the end; E5: z > 99 200 at the end |
| R13 | 3 | E6 | +1 | same | `InteractCommand(door)` | opened |
| R14 | 3 | E5 | pose | @(100.5, 101.0) f270 | `Simulation.Aim(270000, 20000)` | stop x ∈ [99 200, 99 210], `OnCreature` false |
| R15 | 4 | E8 | pose | @(100.6, 102.4) f315 | `CraftCommand(recipe.smithing.march_spear)` | accepted, 1.36 m from the bench site, no authored anvil in reach |
| R16 | 5 | E9 | pose | → (100.5, 100.3); @(100.5, 97.6) f0 | `InteractCommand(door)` | closed |
| R17 | 5 | E9 | pose | → (100.5, 96.0) → (96.0, 96.0) → (90.0, 112.0) → (80.0, 118.0) → (62.0, 130.0) → (51.8, 136.0); @(51.8, 142.0) f90 | `InteractCommand(door.forge_shed)` | opened |
| R18 | 5 | E9 | pose | → (54.5, 142.0); @(60.0, 140.2) f110 | `AssignWorkerCommand(player, Kera, benchId)` | accepted; `WorkerAssigned` |
| R19 | 5 | E9 | pose | → (54.5, 142.0); @(51.8, 142.0) f90 | `InteractCommand(door.forge_shed)` | closed (every body clear) |
| R20 | 5 | E9 | pose | → (51.8, 136.0) → (62.0, 130.0) → (80.0, 118.0) → (90.0, 112.0) → (106.5, 111.0) → (106.5, 96.0); @(108.0, 95.0) f270 | - | the player's body is never within 1,000 mm (centre to centre) of Kera's body between `WorkerAssigned` and `NpcArrivedAtWork` |
| R21 | 6 | E9 | until `NpcArrivedAtWork` (cap: the arrival bound) | same | - | the step-6 assertions below |
| R22 | 7 | E7 | pose | @(100.5, 94.5) f0 | `Place(pad, 100500, 97500, 0)` | accepted |
| R23 | 7 | E7 | +1 each | same | `Place(wall, 99000, 97500, 1)`, `Place(wall, 102000, 97500, 1)` | accepted |
| R24 | 7 | E7 | +1 | same | `Place(wall, 100500, 96000, 0)` | refused `Navigability`, rule V-N1; E9 text "that would cut Kera Voss's work place off"; digest unchanged |
| R25 | 7 | E7 | +1 each | same | `DismantlePieceCommand`: the wall at x = 99, the wall at x = 102, then the pad | refunds 1, 1, 0 |
| R26 | 8 | E8 | pose | → (106.5, 96.0) → (106.5, 106.0); @(100.5, 105.8) f180 | - | - |
| R27 | 8 | E8 | +20 each | same | `AttackCommand` × 3 | `PieceDamaged` × 3, source `melee`, north wall (100500, 105000) at 170 |
| R28 | 8 | E8 | +20 | same | `RepairPieceCommand(north wall)` | 1 timber; `PieceRepaired(170, 200)` |
| R29 | 8 | E8 | +1 | same | `AttackCommand` | 190, kept to the end |
| R30 | 8 | E8 | pose | → (106.5, 106.0) → (106.5, 96.0) → (100.5, 97.6) → (100.5, 100.3); @(103.5, 102.9) f0 | - | the door is open |
| R31 | 8 | E8 | +1 | same | `MoveItemCommand(timber, Carried → In(chest key), 2)` | the record materialises with the derived `cnt_` |
| R32 | 8 | E8 | +1 | same | `TakeAllCommand(chest key)` | 2 back; the empty record stays |
| R33 | 8 | E8 | +1 | same | `MoveItemCommand(timber, Carried → In(chest key), 2)` | stored; one `cnt_` throughout; no exception |
| R34 | 8 | E8 | +20 each | same | `AttackCommand` × 10 | the tenth: `PieceDestroyed`; the 2 timber lie at (103.5, 104.4) with their item ID unchanged |
| R35 | 8 | E8 | +1 | same | `MoveItemCommand(that item, Ground → Carried, 2)` | picked up |
| R36 | 9 | E8 | pose | @(102.9, 100.5) f90 | `Place(chest, 103500, 100500, 1)` | part x [104 100, 104 700], z [100 000, 101 000]; site (104 400, 100 500) |
| R37 | 9 | E8 | +1 | same | `MoveItemCommand(timber, Carried → In(chest 2 key), 2)` | stored |
| R38 | 9 | E9 | same | same | test-side: record P2, a read-only plan (section 3 planner, own scratch) from Kera's anchor to her site | - |
| R39 | 9 | E5 | pose | → (100.5, 100.3) → (100.5, 97.6) → (97.5, 97.0); @(97.5, 102.0) f270 | `Place(pad, 97500, 100500, 0)`, then `Place(pad, 97500, 103500, 0)` | accepted |
| R40 | 9 | E5 | +1 | same | `Place(wall, 96000, 100500, 1)` | accepted |
| R41 | 9 | E5 | pose | @(96.0, 103.5) f0 | `Place(wall, 96000, 103500, 1)` | refused `Bodies`, "someone is standing there" |
| R42 | 9 | E5 | pose | @(97.5, 103.5) f270 | `Place(wall, 96000, 103500, 1)` | accepted; the line x = 96 runs z 98.8-105.2 |
| R43 | 9 | E9 | pose | → (97.5, 97.0) → (100.5, 97.6) → (100.5, 100.3); @(101.5, 102.5) f300 | `ReleaseWorkerCommand(player, Kera)` | `WorkerReleased(released)`; P3 (a read-only plan in the same tick) differs from P2 and does not cross x = 96 000, z ∈ [98 800, 105 200]; her first route equals P3 |
| R44 | 9 | E9 | +300 | same | - | Kera's body outside [98 800, 105 200]² |
| R45 | 9 | E5 | pose | E5-E8: → (97.5, 97.0) → (100.5, 97.6) → (100.5, 100.3); @(102.0, 102.0) f0. E9 (the player is inside at (101.5, 102.5) after R43-R44): @(102.0, 102.0) f0 | `OrderCompanionCommand(player, Tavar, Follow)` | `RoutePlanned` for Tavar |
| R46 | 10 | E5 | +100 | same | `session.Save(SaveSlots.Quick)`; W1 continues 600 ticks; a fresh `GameSession` loads the save as W2 and continues 600 ticks | the step-10 assertions below |
| R47 | 10 | E9 | W1 until `NpcReturnedHome` | same | - | Kera exactly at (61 600, 139 600) facing 300 000; errand retired |

**Step assertions.**
- **Step 0 (replay, M1).** Load S0 a second time and replay R01-R08 at the logged ticks. The registry's `itm_` count is unchanged over the window (it mints nothing), so the raw `StateDigest` is equal, and the piece ID list equals `Derived(Piece, 1..n, owner)` element by element.
- **Step 1.** Exact spend and sequence per the counts table; one `PiecePlaced` per row.
- **Step 6** (criterion 14; implements N-A2). Kera:
  - sends `DoorToggled(NpcSystem.InstanceIdOf(Kera), door.forge_shed, true)` and `DoorToggled(…, pce_door, true)`;
  - arrives with body (x, z) exactly (100 750, 103 500) and facing exactly 270 000, within ⌈1.25 × L / 80⌉ + 40 ticks, where L is the polyline length in mm from her body through her route's corners on the tick of her first `RoutePlanned` after `WorkerAssigned` (read from `Simulation.Navigation.Movers`);
  - between `WorkerAssigned` and `NpcArrivedAtWork`, crosses z = 100 000 exactly once inside the footprint union [98 800, 105 200]², from `c_01_00` to `c_01_01`; the doorway admits body centres only at x ∈ [100 050, 100 950];
  - from entering the opening to arrival, stays inside the footprint union (never exits and re-enters);
  - moves at most 81 mm a tick and is `IsClear` against `Space` plus her obstacles every tick; no body placement other than by the mover;
  - her host-cell sequence is logged (expected `c_00_01 → c_00_00 → c_01_00 → c_01_01`; asserted only by the invariants above).
- **Step 7.** Until E9 the assertion keys on `Failed == Navigability` and `Rule == "V-N1"` (in E7 the first point in the pocket is the door's approach, in E8 the chest). From E9 the text is exact.
- **Step 10.** `StateDigest` D1 == D2; `StateDump.Compare(S1, S2)` finds 0 differences; the dump of the save equals the dump just after load; every piece row is equal (IDs, defs, poses, owners, health with the north wall at 190, door state); `StructureSequence` equal (31 in E8+); Kera's errand (E9) and chest 2's 2 timber equal; the navigation grid digest equal; in both worlds Tavar ends inside [99 200, 104 800]², having passed the doorway opening, with no `CompanionCaughtUp`. At the save, Kera has a `to_home` errand (E9) and Tavar's route is `Active`.
- **Step 11.** From S0, R01-R45 replayed at the logged ticks give an equal replayable `StateDump`, equal piece ID lists and equal `(Tick, Rejected)` pairs. The window mints (splits, the craft), so the raw digest is not compared (G8). `PreviewsInterleaved_ChangeNothing` reuses this log.

**Per-slice expected counts.**

| Slice | After step 1: pieces / timber spent / `StructureSequence` | End of the table: intact pieces / timber carried / `StructureSequence` |
|---|---|---|
| E5 | 16 / 24 / 16 | 20 / 15 / 20 |
| E6 | 17 / 25 / 17 | 21 / 14 / 21 |
| E7 | 17 / 25 / 17 | 21 / 11 / 27 |
| E8 | 19 / 31 / 19 | 23 / 0 (2 in chest 2) / 31 |
| E9 | 19 / 31 / 19 | 23 / 0 (2 in chest 2) / 31 |

**Timber ledger (E8, E9):** 45 → build −31 = 14 → vestibule −5 = 9 → refunds +2 = 11 → repair −1 = 10 → chest cycle and spill ±0 = 10 → chest 2 −2 and stored −2 = 6 → pads and walls −6 = 0. Stacks: check 14 takes the 5, then a 20, then 6 of the last 20; refunds merge into the one partial stack.

**Sequence ledger (E8, E9):** 1-19 step 1; 20-22 the vestibule placed; 23-25 its dismantles; 26 the chest destroyed; 27 chest 2; 28-31 the west pads and walls.

**New-game proof (L8).** `ANewGame_TakesTimber_BuildsAPadAndAWall_AndLoadsEqual`: `GameSession.NewGame` at `Playthrough.Seed` (spawn (30, 158) f90) → (40.0, 140.0) → (47.0, 139.0) → (51.8, 136.0) → (65.0, 136.0); @(84.0, 116.6) f0: `MoveItemCommand("container.timber_stack#00", In(container.timber_stack) → Carried, 3)`; @(88.5, 116.0) f180: `Place(pad, 88500, 112500, 0)` and `Place(wall, 87000, 112500, 1)`; save; a fresh session loads. Asserts: both accepted; one `NavigationRebuilt` (the wall); `StructureSequence` 2; 77 timber left in the stack's record; 0 timber carried; `StateDump.Compare` finds 0 differences between the dump before save and after load. The same pad and wall form the short playthrough beat (placed by section 13 after the faction beats, whose later legs never cross the area): take 3 timber at the stack, place the pad and wall from (88.5, 116.0), assert the two `PiecePlaced` and one rebuild; the playthrough's own save/verify then covers the rows.

**RK-06 at scale.** `TwoHundredPieces_RoundTripAndStayNavigable`: from a crafted start at (100.5, 94.5) carrying 12 stacks of 20 timber (weight is checked only when items enter the pack), place through commands 81 pads, 37 walls, one doorway and 81 roofs (238 timber): east-west lines of walls on z = 93 000, 102 000 and 111 000 across all nine columns, with the doorway at (100500, 102000) r0 in the middle line; walls on x = 99 000 for all nine rows; walls (105000, 100500) r1 and (105000, 103500) r1; roofs last, directly supported rows first. Nothing seals (each strip stays open at an area edge). The player's poses are the implementer's (reach 6 m, never inside a part, never inside a strip a placement would seal), as for `FullAreaLayout` (§14); every placement must be accepted, and a refusal fails the test naming its rule and reason. Save, then load under a copy of the game content whose `content_hash` differs (one definition's `notes` edited), so the definition pass and the `with` copies run. Assert equal rows and `StructureSequence` 200; `entities.msgpack` grows ≤ 200 × 250 bytes over the empty save; equal grid digest; a person-class plan from (100 500, 100 500) to (100 500, 103 500) is found before and after, through the doorway opening, with equal corners.

### 4.23 Contracts offered

Section 6 lists every signature; this is what building guarantees behind them.

**To navigation (section 3).**
- `StructureFootprints`: one `NavFootprint` per solid or door part, world mm, `StructureOrder`, no `DoorOpen` (read at query time from the row), no per-cell split; readable before `_navigation.Build()`. `Space` appends exactly the `Solid` footprints' boxes (asserted by a test).
- `RebuildNavigation` only from `BuildingSystem` (G1), after the row mutation and `Rebuild()`, before the piece events, under the rule of 4.5 and 4.13.
- Check 15's inputs and protected points (4.17); `CanOperate`; the all-bodies close refusal; `OperatePieceDoor` for `OpenDoor` on `pce_` keys; the errand record, route and stuck count (4.15).
- Rebuild order: `_building.Populate()`, `_navigation.Build()`, `_npcs.Populate(companions)`.

**To persistence (section 7).** The records of 4.4 and 4.15, the piece-chest `ContainerRecord`, the `WorldDelta` mutators reached only through `RuntimeState` wrappers, the public readers (`Piece`, `PiecesIn`, `get_Pieces`, `get_StructureSequence`, `NpcErrand`, `NpcErrandsIn`), and the rules and audit of 4.19.

**To presentation (section 8).**
- Reads: `Pieces` (`PieceView(Id, DefId, Family, XMm, ZMm, Rotation, Owner, HealthCurrent, HealthMax, DoorOpen, WorkerNpcId, MinXMm, MinZMm, MaxXMm, MaxZMm, Parts, ContainerKey, StationKey)`, by ID; `WorkerNpcId` derived from errands), `Space`, `Stations`, `StructureRevision`, `StructureAudit`, `StructureFootprints`, `WorkAssignments` (`WorkAssignmentView(NpcId, EntityId? PieceId, AnchorXMm, AnchorZMm, FacingMdeg, NpcErrandPhase Phase)`, one row per errand; a `to_home` row carries the site pose and a null piece), `Containers` and `DynamicBlockers`.
- `PreviewPlacement` (4.6), the pure `Snapper` (4.7), and the piece and worker events. Prediction reads `simulation.Space`. Every building action is a direct key (no radial); the build camera cap is 9 m through `CameraRig.BuildMaxDistance` and `Cap`. No `pieces` section in `art_bindings.json`.

**To factions (section 5).** `SightWalls()` carries placed walls and closed piece leaves to combat, creatures and companions; factions do not read it in M7. Building records no act (G13) and never references faction state (G6, G19); `piece_placed` and `piece_destroyed` stay `NotBuilt`.

### 4.24 Not in M7

| Item | Belongs to |
|---|---|
| `shot` and `formula` piece damage (a `Loose` source parameter, the `Magic.cs:114` hook) | M9, if the slice's building needs them |
| `creature_charge`, `creature_blow`, `fire`, `raid` damage; property threats and the attack-frequency option | M10 (home defense) |
| A renewable timber node, its baseline transition, a frozen M6 layout fingerprint and their two tests | the milestone that wants renewable timber (M9 or later) |
| A recursive mount cascade, collapse, or any structural support simulation | not scheduled (D-08) |
| A persisted structure record (`bld`), merge and split, ownership transfer | M10 (settlements) |
| Territory gating, claims or permission checks on placement | with crime (Phase 3; owner Q2) |
| `piece_placed` / `piece_destroyed` act kinds | M9 |
| `construct_building` objective | M9 or later (quest content) |
| A second navigability graph with piece doors solid for non-permitted agents; NPCs closing doors behind them | M9/M10 |
| NPC schedules, production, wages, hirelings, a second worker | M10 |
| Vertical building (upper floors, stairs, ladders, walkable roofs), terrain flattening | beyond Phase 2 (ruling 2) |
| 45° or free rotation with oriented footprints | owner Q1 (quarter turns by default) |
| Building anywhere legal, or more build areas | owner Q4; later content within BLD008/BLD009 |
| Posts, fences, half and window walls, beds, upgrade tiers, locks and keys | later content milestones |
| The production building UI (catalogue, blueprints, cost and refund previews), piece art bindings, building sounds | when the owner releases the kit |
| `BuildingCounters`, merged meshes or MultiMesh per structure | optional in M7, switched on by measurement (section 14) |

### Tests owned by this section

| Test | Project | What it proves |
|---|---|---|
| `Lattice_AnchorsAndSlotKeys_ForEverySlotKind` | Domain.Tests | The slot rules and keys of 4.2 |
| `QuarterTurn_MapsBoxesAndFacingsExactly` | Domain.Tests | Integer rotation of boxes and facings for every r and piece |
| `Sockets_HaveTheirWorldPositionsAndAxes` | Domain.Tests | Socket positions and axes after rotation |
| `Snapper_SnapsFixedHalfModuleAndDoorAims` | Domain.Tests | Aim to pose, including half-way aims and the geometric door tie-break |
| `Relief_OfTheKnownSquares` | Domain.Tests | The square (111-114, 105-108) rises 228 mm and (99-102)² 96 mm |
| `RoofSupport_DepthOneOnly` | Domain.Tests | Roof support is depth 1, evaluated at placement only |
| `IntegerOverlap_TouchingIsNotOverlap` | Domain.Tests | Box/box and box/circle overlap maths |
| `RepairCost_RoundsUp_AndRefund_RoundsDown` | Domain.Tests | Cost scaling |
| `EntityIdDerived_IsStable_AndOrdersLikeItsOrdinal` | Domain.Tests | Replay-stable derived IDs that sort by ordinal |
| `TheGamePack_BuildsTheCatalogueTheAreaAndTheTimberStack` | Content.Tests | The shipped pieces, `config.building`, the area and the 80-timber stack build |
| `EachBuildingLint_RefusesItsCraftedBadFile` | Content.Tests | BLD001-BLD009 and WLD015 each reject a crafted bad file |
| `KerasWorksAt_Parses_AndIsRecipeUsed` | Content.Tests | The `works_at` field and its lint |
| `EachPiece_Places_WithItsExactSpendEventsViewAndId` | Application.Tests | Every piece: acceptance, exact spend, events, view, derived ID; pads and roofs have no parts |
| `EachPlacementRule_RefusesWithItsReason_AndLeavesTheDigest` | Application.Tests | All fifteen checks refuse with their reason and leave `StateDigest` unchanged (rules 9-11 and 13 on setups edited after load) |
| `ADoorlessOneSquareHut_IsRefused` | Application.Tests | Any newly sealed pocket is refused, with the "rooms need a doorway" reason (L6) |
| `EachFootprintChange_SendsExactlyOneRebuild_AndPadsAndRoofsSendNone` | Application.Tests | Criterion 4: one `RebuildNavigation` per committed change of a piece with parts, none for pads, roofs, toggles, damage or repair; every change bumps the sequence |
| `Preview_EqualsTheCommand_OverFortyPoses` | Application.Tests | Preview parity over all fifteen rules |
| `PreviewsInterleaved_ChangeNothing` | Application.Tests | G7: 1,000 interleaved previews change no dump, ID, rejection or counter |
| `WallsDoorwaysAndDoors_BlockAndPass` | Application.Tests | Collision through walls, doorways and doors |
| `NoDoorCloses_OnAnyBody` | Application.Tests | Authored and piece doors refuse to close on the player, NPCs, companions and creatures |
| `ACompanion_OpensTheOwnersPieceDoor` | Application.Tests | `CanOperate` for the owner's companion |
| `Prediction_EqualsAuthority_AcrossANewWall` | Application.Tests | Prediction reads `Simulation.Space` |
| `Creatures_AreBlockedByPieces` | Application.Tests | Creature steps use the piece-bearing space |
| `Aim_StopsAtAPieceWall` | Application.Tests | `SightWalls()` includes placed walls |
| `Dismantle_RefusesInOrder_AndRefundsHalf` | Application.Tests | Dismantle refusals and refunds |
| `ForeignPieces_AreNotYoursToTouch` | Application.Tests | Ownership gates on a foreign-owned piece from a crafted save |
| `TheFourCellPad_IsHostedByItsAnchor_AndDigestedOnce` | Application.Tests | Host cell and per-cell digest of a straddling pad |
| `Building_RoundTripsThroughSave_AndNavigationDerivesIdentically` | Application.Tests | Rows, sequence, grid digest and `Space` after a load |
| `TwoHundredPieces_RoundTripAndStayNavigable` | Application.Tests | RK-06: 200 pieces through a content-hash change, ≤ 250 B per piece, doorway plans equal after load |
| `APieceChest_EmptiedAndRefilled_KeepsOneIdentity` | Application.Tests | The C1 fix |
| `APieceChest_EmptiedAndRefilled_AcrossASave_KeepsOneIdentity` | Application.Tests | The C1 fix across a save |
| `AChestWithItems_CannotBeTakenDown` | Application.Tests | The dismantle refusal for a filled chest |
| `ADestroyedChest_SpillsItsStacks_KeepingTheirIds` | Application.Tests | No item loss on destroy |
| `APieceAnvil_Crafts_UntilTakenDown` | Application.Tests | The station piece |
| `DamageRules_OnlyAMeleeBlow_DamagesAPiece` | Application.Tests | The one damage source (10); arrows, bolts, formulas and a boar's charge damage nothing |
| `ABlowThatHitsACreature_DamagesNoPiece` | Application.Tests | `FirstStop` runs only when no creature was struck |
| `AnAuthoredWall_ShieldsATouchingPiece` | Application.Tests | The shielding rule |
| `APieceAtZero_IsDestroyed_AndADoorwayTakesItsDoor` | Application.Tests | Destruction and the doorway-door rule |
| `Repair_CostsTheScaledTimber_AndRefusesInOrder` | Application.Tests | Repair cost and refusals |
| `Assign_RefusesInOrder` | Application.Tests | Assign refusals 1-10 |
| `Release_RefusesInOrder` | Application.Tests | Release refusals |
| `Kera_WalksToTheBench_ArrivesExactly_AndHomeAgain` | Application.Tests | Exact landing, arrival and retirement |
| `AnErrand_OpensDoors_AndNeverClosesThem` | Application.Tests | NPCs open authored and piece doors and never close them |
| `AnErrandNpc_KeepsHerWorkFacing_WhileTalking` | Application.Tests | The mover alone writes an errand NPC's facing (L5) |
| `AnErrand_SavedMidWalk_ContinuesLikeTheUnsavedWorld` | Application.Tests | G18: save-then-continue for the errand |
| `TakingDownTheBench_SendsTheWorkerHome` | Application.Tests | `EndWork` on dismantle and destroy |
| `KeraAtWork_TradesAtTheBench_AndHerWaresStayHome` | Application.Tests | D12, including the billet gate at the bench |
| `CrossingWorkshop_1to3_BuildRefuseAndWalkIn` | Application.Tests | Steps 1-3: build, overlap refusal, collision |
| `CrossingWorkshop_4and8_CraftBlowsMendAndSpill` | Application.Tests | Steps 4 and 8: station, damage, repair, chest cycle and spill |
| `CrossingWorkshop_5to6_KeraWalksInThroughAndAround` | Application.Tests | Steps 5-6; the implementation of N-A2 (criterion 14) |
| `CrossingWorkshop_7_TheVestibuleIsRefused` | Application.Tests | Navigability in the scenario |
| `CrossingWorkshop_9_ANewWallChangesHerWayHome` | Application.Tests | "Around": the new wall changes her route home |
| `CrossingWorkshop_10_SaveQuitReloadGoesOnTheSame` | Application.Tests | D1 == D2 and 0 `StateDump` differences with movers mid-route |
| `CrossingWorkshop_0and11_ReplaysFromTheLog` | Application.Tests | Raw-digest replay of step 1, replayable-dump replay of the table |
| `TheCommittedBuildStart_IsTheCrossingWorkshopStart` | Application.Tests | The committed S0 equals the builder |
| `ANewGame_TakesTimber_BuildsAPadAndAWall_AndLoadsEqual` | Application.Tests | The new-game building loop from the timber stack through a save (L8) |
| `ACreatureChasingRoundTheWorkshop_NeverOverlapsAPiece` | Application.Tests | R-A7: creatures slide along pieces and never overlap one |
