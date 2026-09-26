# M7 Navigation v1: candidate design, "scale and future fit" lens

Status: panel candidate, 2026-09-24. Design and implementation planning only. Base: origin/main `e10d2c4` (snapshot `main_e10d2c4`). Every `path:line` is repo-relative at that commit and was checked in the snapshot. Evidence notes: `research/spatial_movement.md`, `sim_core.md`, `persistence.md`, `authority.md` §1-4, `building_docs.md` §4.3, §4.9, §5-§9. Scope: `drafts/00_SCOPE_RULINGS.md`, which this design follows. There is no challenge to a scope ruling. §16.4 records one recommendation made inside the latitude a ruling already allows.

---

## 0. The recommendation on one page

- **Representation.** A single deterministic integer lattice covers the whole world. Nodes sit every **250 mm**. Storage is chunked into **nav tiles** of exactly one 100 m world cell (400 × 400 nodes). Each node holds one byte of **clearance**: the distance to the nearest blocking footprint, in 5 mm units, capped at 1,275 mm. It also holds one byte of **flags**. Doors and barriers are a separate **overlay** that is read live, as `ClosedDoors()` is today. One tile layer serves every body radius, because a radius is only a threshold on clearance.
- **Seams are not a case.** Node indices are global, so every node belongs to exactly one tile by floor division. A node's clearance is a pure function of the global geometry within 1,275 mm of it. Tile contents therefore do not depend on build order, on neighbours, or on residency. Nothing is stitched.
- **Updates are exact.** The design stores no dirty flags and uses no debounce. When the set of placed footprints changes, the service recomputes the rectangle `piece AABB ⊕ 1,275 mm` in each tile it touches, and that tile's input stamp. It rebuilds nothing else. The result is byte-identical to a full rebuild, and a test proves it.
- **Planning.** A* on the lattice uses integer costs (250 per straight step, 354 per diagonal), a total-order tie-break `(f, h, row, column)`, a fixed neighbour order and no corner-cutting. It has a 12,000-expansion cap and a window capped at 128 m. The path is string-pulled with an integer supercover line-of-sight into at most 32 integer-mm waypoints.
- **Local movement is unchanged.** The route follower emits a target point. The consumer builds a `MoveIntent` with its existing helper. `Kinematics.Step` still does all collision (`src/Domain/Spatial/Kinematics.cs:129-160`). The pathfinder never places a body.
- **What is derived and what is saved.** Tiles, overlays, stamps, search scratch and counters are derived and never saved. They are rebuilt at load inside the `Simulation` constructor, before any `Populate`. A mover's committed route (remaining waypoints, goal, stamp, plan tick, outcome, progress mark) is mover state. It is saved with its owner, as the companion trail already is (schema 12). That makes save-then-continue exact without a proof that depends on search inputs.
- **Consumers in M7.** Two: the companion (Tavar), only when the trail cannot give him a goal in clear view, and the one assigned work NPC. Creatures stay as they are, and charges and lunges are never pathed.
- **Scale.** Ashen Hollow now needs 1.28 MB of tiles and about 3.5 MB of reusable search scratch. It needs about 50,600 exact node evaluations to build, computed from the 67 footprints in `ashen_hollow.yaml`. A 2 km region later keeps at most 36 tiles resident (11.5 MB). Tiles are built lazily and can be evicted, and residency never changes a result. A coarse portal graph for long routes, NPC schedules and tier B is an addition on top of the tiles, not a replacement for them.

---

## 1. What exists, and what M7 keeps

These facts were verified in the snapshot:

| Fact | Where | Consequence for nav |
|---|---|---|
| One movement function. Circle-vs-footprint push-out, sub-steps ≤ r/2, 4 relaxation passes, then rounding to whole mm | `src/Domain/Spatial/Kinematics.cs:129-160, 183-208` | Nav emits targets. Kinematics keeps collision. Prediction (`src/Presentation/Player/PlayerController.cs:127`) is untouched |
| Movement blocking is height-aware: `Blocks(b, lift, height)` | `Kinematics.cs:166-167` | The nav raster includes a footprint only if `Blocks(b, 0, agentHeight)`. Companions and creatures always use the 1,800 mm default height |
| Footprints are axis-aligned boxes and circles only, in integer mm | `src/Domain/Spatial/Blockers.cs:40-41, 95` | Distances to them are exact in integer arithmetic (§8.6) |
| `WalkSpace` is one region-wide, immutable record built from content | `Kinematics.cs:104`; `src/Content/WorldContent.cs:292` | Nav reads it plus placed pieces |
| Doors and barriers are flag-gated. `ClosedDoors()` re-reads the flags on every call | `src/World/Runtime/Systems.cs:41-48`; `src/Domain/Spatial/RegionLayout.cs:93-97` | Doors are an overlay read live, never baked |
| No pathfinding exists. Creatures steer straight and slide | `docs/M3D_STATUS.md:78`; `src/World/Runtime/Creatures.cs:820-837` | M7 builds the first pathfinder, so ROADMAP's M7 entry criterion is met inside M7 (scope ruling item 14) |
| The companion walks the player's trail and aims at the newest mark in clear view. With no mark in view, he aims at the oldest mark | `src/World/Runtime/Companions.cs:325-361` | Nav replaces only that last fallback branch (§9.3) |
| The companion's "headway" is displacement, not progress | `Companions.cs:357-360` | Nav mode measures progress along the route instead |
| Catch-up teleports him at > 30 m, or after 80 ticks without headway | `Companions.cs:331-332, 367-393`; `content/config/companion.yaml:10-11` | Kept as the last-resort safety net |
| The trail and stuck counter are persisted, so a loaded world continues identically | `CompanionTests.cs:285-319`; `src/Persistence/SectionCodec.cs:89` (`trail_mm`) | The route follows this precedent (§5.2) |
| Tick order: movement, tiers, combat, creatures, companions, NPCs, ... clock | `src/World/Runtime/Simulation.cs:319-333` | Nav has no tick of its own. Movers query it during their ticks |
| Load and new game both pass through the `Simulation` constructor. `Populate` runs after `RequireEverySliceOwned` | `Simulation.cs:100-145` | Nav rebuild happens there (§5.1) |
| Static mutable state is forbidden, and public records in `UNNAMED.Domain.Spatial` may have no public setters | `tests/Architecture.Tests/ArchitectureTests.cs:86, 135-145` | Nav caches are per-Simulation instance fields. Nav records live in `UNNAMED.Domain.Spatial` |

Patrol and wander, which are pure functions of the tick (`Creatures.cs:419-457`), and the charge stun (`Creatures.cs:565-596`) are not disturbed.

---

## 2. Who needs pathfinding in M7 (A1)

| Consumer | In M7? | Why | Planner input | Where its route persists |
|---|---|---|---|---|
| **Companion (Tavar)** following the player | **Yes, as a fallback mode** | ROADMAP M7 exit: companions path "in, through, and around" a player structure. Placed walls cut across trail marks. A wait-then-follow order clears the trail (`Companions.cs:186-187`). The companion cannot follow player-only jumps and crouches | Goal = player position. Nav is used only when neither the player nor any trail mark is in clear view (§9.3) | `CompanionRecord.Route` (player.msgpack) |
| **The assigned work NPC** (scope ruling item 5) | **Yes** | The exit criterion "assign an NPC to work in it … navigates in, through, and around". Settlement NPCs never move today (`src/World/Runtime/Social.cs:149-164`) | Goal = the station piece's work anchor, and back to its `NpcSite` when unassigned | The work-assignment record (owner decided by the building/NPC design) |
| Other settlement NPCs | No | They stand at their sites (`Social.cs:127-139`) | — | — |
| Creatures (return home, search, chase) | **No** (§16.4) | Optional-if-cheap in the ruling. It is not cheap: it would persist a route in `CreatureDto` (another frozen shape and migration) and reopen M3d balance | — | — |
| Charge and lunge | **Never** | The stun depends on hitting a structure (`Creatures.cs:589-594`) | — | — |
| The player | No | Input-driven | — | — |
| Placement validation (the building system) | **Yes (query, not a mover)** | "placement validation that rejects un-navigable … configurations" (`docs/ROADMAP.md:283`) | `CheckEdit` (§11) | Nothing |
| Test harnesses and the presentation playthrough | Read-only | Headless proofs and the runtime recording | `NavigationService.Plan` via tests; `Simulation.NavDebug` | Nothing |

That makes two movers and one validator. The design is sized for them, and every structure below is chosen so that more movers (schedules, creatures) are additions.

---

## 3. Representation (A2)

### 3.1 Options compared for Otherreach

| Criterion | **A. Uniform grid (global lattice, cell-sized tiles, clearance byte)** | **B. Graph from cells or samples** (visibility graph over inflated corners, or a waypoint per ~2 m) | **C. Current steering + deterministic local avoidance** | **D. Navmesh** (constrained Delaunay of free space) |
|---|---|---|---|---|
| Determinism | Integer only. A total-order heap makes results independent of the heap implementation | Visibility-graph tangent points on circles need trigonometry or rounded polygons, so float risk. A sampled graph needs RNG-keyed samples | Already deterministic | Robust CDT needs exact predicates. None exist in the repo. Incremental insert and delete is order-sensitive unless canonicalised |
| Cell seams | A non-case: global indices, and each node's value is a pure function of nearby geometry (§7) | A global graph has no seams, but edges must be tested against blockers in both cells | No seams (region-wide `WalkSpace`) | Per-cell meshes need stitching (the RK-14 problem). A global mesh must be retriangulated around every edit |
| Dynamic building | Exact dirty-rect rewrite, about 260 nodes for a 3 m wall piece | Every edge within reach of the piece must be retested, and corners added or removed. Per-radius graphs multiply this | Nothing to update | Local retriangulation; complex |
| Body sizes | One layer. Radius is a threshold on clearance | A separate inflated graph per radius (5 radii in content today) | Only Kinematics sliding | One mesh per radius, or clearance annotations |
| Memory, hollow | 1.28 MB of tiles | Small (~300 nodes × 5 radii) | 0 | Small |
| Memory, 2 km region | 11.5 MB resident (36 tiles). 128 MB if all 400 tiles were built, which never happens | O(corners²) edges. With ~5,000 blockers, about 20,000 corners, unpartitioned | 0 | Small, but per-radius |
| CPU per edit | ~50 µs rasterize plus stamp | Up to O(n) LOS retests per new corner | 0 | ms, with high implementation risk |
| Solves concave cases (lodge interior, den notch, walls placed across a route) | Yes | Yes | **No**: "A target behind a wall is searched for, not pathed to" (`docs/M3D_STATUS.md:78`) | Yes |
| Headless tests and save/load | Trivial: derived, rebuilt at load | Same | Same | Same |
| Future NPC schedules and larger settlements | A coarse portal graph over tiles is an addition (§14) | Scales poorly | Fails | Good path quality. Same portal problem |
| Implementation risk for M7 | Low: plain integer code | Medium | None, but it fails the exit criterion | High |

### 3.2 Recommendation: A for global routes, C as the local layer

Option A is the only one that meets every requirement at once:
- it is integer-exact, so it inherits the determinism claim of `Kinematics.cs:115-118`;
- it makes seams a non-case by construction, not by stitching;
- it applies building edits exactly and locally;
- one representation serves every radius.

Option C stays as the local layer: route targets, then `Kinematics.Step`, then a watchdog. Option D is rejected for M7. It would recreate the per-cell stitching problem RK-14 describes, and it needs an exact-predicate CDT the repo does not have. Option B is rejected because it needs one graph per radius and trigonometry for circles.

### 3.3 What a tile holds

```
NavTile (one per 100 m world cell; immutable; copy-on-write on edit)
  Key          NavTileKey(long Tx, long Tz)                  // global tile coordinates; Tx = rx*20 + cx
  Clearance    byte[160_000]   row-major (local j * 400 + local i); units of 5 mm, 255 = ">= 1,275 mm"
  Flags        byte[160_000]   bit0 DoorZone, bit1 Outside (outside region bounds/cells), bits 2-7 reserved
  DoorZones    ImmutableArray<NavDoorZone>  sorted by footprint (MinX, MinZ, MaxX, MaxZ), then Ref ordinal
  Stamp        ulong           hash of the tile's canonical input list (§6.3)
NavDoorZone(string Ref, NavDoorKind Kind, Blocker Footprint, bool Openable,
            ImmutableArray<(int Local, byte Units)> Cells)   // nodes within the cap of the footprint
```

In M7 there is exactly one **height class** (1,800 mm), so one clearance layer. A later height class adds a layer. It is never a new format.

---

## 4. Grid resolution (A3)

### 4.1 The constraints, from the actual numbers

- **Bodies.** Player, companion and NPC radius 350 mm (`content/config/base_speeds.yaml:10`). Creature radii 350-550 mm (research `spatial_movement.md` §4.3).
- **Openings.** The lodge and smithy doorways are 1.6 m (`content/regions/ashen_hollow.yaml:143-144`), and so is the fence gap. The kit's door frame (a report, not normative) is 2.20 m tall. Its width is unknown. Building v1 could pick a narrower cottage door, down to 1.2 m.
- **Walls.** 0.4 m thick (`ashen_hollow.yaml:68-78`).
- **Grids.** Cells are 100 m (`src/World/Coordinates.cs:16`). The terrain grid is 5 m (`ashen_hollow.yaml` terrain `spacing_m: 5`). The likely building snap unit is 0.5 m or 1 m, and the kit module is 3 m.
- **Movement per tick.** 80 mm walk, 160 mm run, 256 mm sprint (`spatial_movement.md` §4.1).

The centre-line corridor through an opening of width W for radius r is `W − 2r`. For node spacing s, a corridor contains at least `floor((W − 2r)/s)` node columns. Nodes are tested conservatively (§8.6), and robust smoothing wants at least 2 columns.

| s (mm) | nodes/tile | bytes/tile (2 B) | Columns, r = 350, W = 1.6 m (0.9 m) | Columns, r = 550, W = 1.6 m (0.5 m) | Columns, r = 350, W = 1.2 m (0.5 m) | Divides 100 m / 5 m / 0.5 m / 3 m? | Relative flood cost |
|---|---|---|---|---|---|---|---|
| 200 | 250,000 | 500 KB | 4 | 2 | 2 | yes / yes / **no** / yes | 1.56 |
| **250** | **160,000** | **320 KB** | **3-4** | **2** | **2** | **yes / yes / yes / yes** | **1.00** |
| 400 | 62,500 | 125 KB | 2 | 1 | 1 | yes / **no** / **no** / **no** | 0.39 |
| 500 | 40,000 | 80 KB | 1-2 | 0-1 (fails after the 5 mm conservative rounding) | 0-1 | yes / yes / yes / yes | 0.25 |

### 4.2 Decision: 250 mm

250 mm is the coarsest spacing that meets four conditions:

1. **It keeps at least 2 node columns** through a 1.6 m door for the largest current creature (boar, 0.55 m), and through a 1.2 m door for a person.
2. **It divides the cell (400), the terrain spacing (20), a 0.5 m snap (2) and the 3 m module (12) exactly.** A structure moved by one snap step therefore rasterises to exactly the same pattern, shifted. This is the basis of the translation-invariance and seam tests (§15).
3. **It is finer than the body.** 250 < 350, so no body fits between nodes, and it is coarser than one tick of travel.
4. **It holds the cost to 1.0 in the table above**, against 1.56 at 200 mm.

A 500 mm grid would halve memory but break the boar through doors (row 4 of the table), and would make door smoothing fail at a 1.2 m opening.

**Check against the real lodge door.** The opening is z 127.2-128.8, with nodes at z = 125 + 250k mm. The four nodes at 127.625, 127.875, 128.125 and 128.375 lie 425, 675, 675 and 425 mm from the jambs. All four are passable for r = 350 (≥ 350). The middle two meet the smoothing clearance r + 100 = 450. Both of the middle two are passable for the boar (≥ 550). The smithy door (z 141.2-142.8) and the fence gap (x 51.4-53.0) give the same four nodes. The 0.2 m gap between `rock_beam_west` and `tree_10` is correctly sealed.

### 4.3 Clearance units

5 mm units, capped at 255 units (1,275 mm). The cap exceeds the largest planned radius: the boar is 550 mm, and the canon `irregular_heavy` body (0.82 m shoulders) is about 450-600 mm. The cap is also the **influence radius** for dirty rectangles. A larger cap would dirty neighbour tiles more often for near-seam edits. The unit and the cap are code constants (`NavLattice.FormatVersion = 1`). Because no tile is ever saved, changing them later is a rebuild, not a migration.

---

## 5. Authority (A4)

### 5.1 Inputs, derived data, invalidation, saving

| Aspect | Specification |
|---|---|
| **Reads (authoritative)** | (1) `Setup.Layout.Space`: bounds and static blockers, which are immutable content. (2) Placed pieces from the building slice: footprint, traversal class, height and clearance, door ref, door open state (§16.1 C-B1). (3) Authored door and barrier sites plus their `world.*` flags through `SystemContext.IsOpen` / `IsLifted` (`Systems.cs:41-45`). (4) Content: `config.navigation` tuning and agent classes (§12.4). Terrain is **not** read, because terrain never blocks (`Kinematics.cs:159`). |
| **Derived** | Tiles (clearance, flags, door zones, stamp), the per-tile blocker index, route stamps, window components for edit checks. |
| **Cached** | Tiles, keyed by `NavTileKey` in a per-Simulation dictionary, built lazily on first query. A per-tile list of input footprints (footprint ⊕ cap intersects the tile). The last-seen placed-footprint list, for diffing. One reusable search scratch (§13). |
| **Invalidates** | A change to the *set of blocking or door footprints* of placed pieces: place, dismantle, destroy. The change is detected by pull (§6.1). A door opening or closing invalidates **nothing** (overlay). A barrier lifting invalidates nothing. Content never changes at run time. |
| **Rebuilt** | The rectangle `footprint AABB ⊕ 1,275 mm` in each touched tile, plus that tile's stamp. At load or new game: nothing eagerly. The first query builds what it needs. In M7 the `Simulation` constructor pre-builds all four tiles before `_npcs.Populate()` (`Simulation.cs:141`), so the first tick carries no build spike and the order rule of `research/persistence.md` §8.3 holds. |
| **Saved** | Per mover, inside its owner's record: `NavRoute` (§5.2). Nothing of the grid. |
| **Not saved** | Tiles, overlays, blocker index, stamps (except the copy inside a route), search scratch, counters, the per-tick plan budget, window components. |

The cache is **not** a `StateSlice`. A slice is authoritative state with one writer and a persistence story. The tile cache is a memoised pure function of other slices, which is exactly the "transient … cached pathfinding … spatial indices" that ARCHITECTURE keeps "outside world state and … rebuilt on load" (`docs/ARCHITECTURE.md:164`). If it were a slice, lazy builds on a read path would have to carry an owner token, and readers would be misled into thinking it is saved.

### 5.2 Route state and the save-then-continue digest test

**Decision: persist the committed route.** It is mover state, like `CompanionState.Trail`.

```csharp
// src/Domain/Spatial/NavRoute.cs
public readonly record struct NavPoint(long XMm, long ZMm);
public enum NavOutcome { Complete, Partial, Unreachable }
public sealed record NavRoute(long GoalXMm, long GoalZMm, ImmutableArray<NavPoint> Waypoints, ulong Stamp, long PlannedTick, NavOutcome Outcome)
{
    public long BestRemainingMm { get; init; }   // progress watermark for the stuck watchdog
}
```

Save shape: `NavRouteDto { goal_x_mm, goal_z_mm, waypoints_mm (flat x,z long[], ≤ 64 values), stamp (uint64), planned_tick, outcome ("complete"|"partial"|"unreachable"), best_remaining_mm }`. That is about 60 bytes plus 16 per waypoint.

**Why exact continuity holds.** At a tick boundary the loaded world has:
1. tiles equal to the live world's, because tiles are a pure function of authoritative geometry and T10 proves dirty-rect updates equal a full rebuild;
2. the same door flags;
3. the same routes and consumer counters, because they are persisted;
4. a per-tick plan budget of zero in both worlds, because it resets at every tick boundary.

The follower (§9.1) and the planner are pure functions of these values and of the tick. So the next tick is identical, and by induction every later tick.

**Considered and rejected: route as a pure function of a persisted "plan key".** Storing only the start, goal, stamp and cursor, and recomputing the waypoints at load, would literally satisfy `docs/SYSTEMS.md:443` ("Persisting live AI, pathing … Rebuilt at load"). But it is correct only while *every* search input is covered by the stamp: window geometry, barrier states, door states for non-openers and tuning. Every future cost layer (roads, property, territory) would silently have to join the stamp, or save-then-continue would break in a way no current test catches. Persisting about 0.5 KB per mover is robust and has precedent: the trail (schema 12) and creature minds (schema 8) are persisted for exactly this reason (`research/sim_core.md` §12 item 10). M7 amends SYSTEMS:443 (§16.2).

**Digests.** The companion's route enters `PlayerRecord.Digest` (M7 already bumps it to `unnamed.player/v10`), `CanonicalState.Render` and, automatically, `StateDump` through `CompanionRecord`. The work NPC's route enters whichever digest covers its record (`EffectiveCellDigest` if it is a world-delta record, `research/persistence.md` §3.3 A.2).

---

## 6. Dynamic building updates (A5)

### 6.1 The deterministic sequence

```
Boundary N (DrainCommands, FIFO)
  PlacePieceCommand ─► BuildingSystem.Handle
       validate: overlap, sockets, terrain, build area, protected geometry  (building design)
       NavigationService.CheckEdit(add:[F], remove:[], protected)            (§11)   ─► refusal text or OK
       ExchangeItems (materials) ─► commit piece to the Structures slice ─► publish PiecePlaced
  (nav is not called to "update"; it pulls on its next query)
Step N+1
  MovementSystem.Tick  ── Kinematics sees F through the effective space (§16.1 C-B2)
  CreatureSystem.Tick  ── creatures slide along F as they slide along any wall
  CompanionSystem.Tick ─► NavigationService.EnsureCurrent()
                            placed-footprint list reference changed → canonical diff (old vs new)
                            → added {F}, removed {} → dirty rects F.AABB ⊕ 1,275 mm per touched tile
                            → RebuildRect in each tile (copy-on-write) → recompute each touched tile's Stamp
                          route.Stamp ≠ StampFor(route window) → replan (reason Stamp), subject to the plan budget
                          → new route avoids F; follower continues
  NpcSystem / work tick  ── same check on the NPC's route
```

The same sequence applies to dismantle, and to destruction at 0 health. A destruction may happen during a step, from an explicit damage rule. Movers later in the same tick then see the new geometry. That is deterministic, because the tick order is fixed.

**Pull, not push.** `EnsureCurrent()` compares the reference of the building's placed-footprint collection with the one it saw last. Runtime slices are immutable collections, replaced on every write. If the reference changed, it merge-walks the two canonically sorted lists (§6.3) to find added and removed footprints. Damage-only writes change the reference but not the footprints, so the diff is empty and costs O(pieces). A forgotten `Invalidate()` call therefore cannot exist. `CheckEdit` also calls `EnsureCurrent()` first.

### 6.2 What is recomputed: a rectangle, not a cell or a neighbourhood

For each tile T whose node rectangle intersects `R = F.AABB ⊕ 1,275 mm`, restricted to `R ∩ T`:

1. Set `clearance = min(cap, bounds distance)`, and 0 with `Outside` for nodes outside the region cells or bounds.
2. For every input footprint G in T's blocker index with `(G ⊕ cap) ∩ R ≠ ∅`, and every node n in `R ∩ (G ⊕ cap)`: `clearance[n] = min(clearance[n], units(dist(n, G)))`.
3. Recompute the `DoorZone` bit and the door-zone cells for doors that intersect R.
4. Recompute `T.Stamp`.

Taking a minimum is commutative. The result therefore does not depend on the order of blockers, the order of edits, or on which tile was rebuilt first.

For a 3 m × 0.4 m wall piece, `R` is 5.55 × 2.95 m, which is 262 nodes. The whole cell is never recomputed. A neighbouring tile is touched only when the piece lies within 1,275 mm of the seam, and then only the strip inside R.

### 6.3 Canonical order and IDs

Piece instance IDs (ULIDs) never order anything in nav. Inputs are sorted by `(MinX, MinZ, MaxX, MaxZ, kind, HeightMm, ClearanceMm)`, and door zones by footprint and then `Ref`. Replays that mint different ULIDs, and fresh runs, therefore build byte-identical tiles and equal stamps.

`Stamp = first 8 bytes (little-endian) of SHA-256` over:
- `"unnamed.nav-tile/v1"`, `FormatVersion`, `Tx`, `Tz`, the height class;
- then every input in canonical order: kind (solid, door or barrier), box min/max or circle centre and radius, height, clearance.

`CanonicalHasher` supplies it (`src/Domain/CanonicalHasher.cs:15, 41`). Hashing the input list, rather than 320 KB of output, costs microseconds. Equal inputs give equal tiles because the tiles are a pure function of their inputs.

---

## 7. Cell seams (A6)

| Hazard | How the design removes it |
|---|---|
| **Duplicated nodes** | Node `(i, j)` has centre `(250i + 125, 250j + 125)` mm, and its tile is `(FloorDiv(i, 400), FloorDiv(j, 400))`. A seam lies at a multiple of 100,000 mm, and no node centre can lie on one, so every node belongs to exactly one tile |
| **Ambiguous coordinates** | A point at x = 100,000 mm lies in node 400, which is tile 1. This matches `CellKey.OfWorld`'s floor semantics (`Coordinates.cs:93-96`). Negative coordinates use integer floor division (the `WorldMath.FloorMod` convention) |
| **Disconnected borders** | Neighbour lookup computes the global `(i ± 1, j ± 1)` and fetches that tile. A tile edge is an array boundary, not a graph boundary. A* and line-of-sight never see tiles |
| **Order-dependent rebuilding** | A node's value depends only on footprints within the cap of it, taken from the global input set. Building A then B, B then A, or B alone later gives identical bytes (T5a) |
| **Straddling structures** | A straddling piece is one input footprint and appears in both tiles' blocker indices. Each tile rasterises its own part of the same global geometry |
| **"Valid before a cell reload, invalid after"** (RK-14 fail case, `docs/RISK_REGISTER.md:292`) | Eviction and rebuild are pure, so a rebuilt tile is byte-identical to the evicted one (T5d) |

RK-14's mitigation asks for "a stitched multi-cell neighborhood … with a seam overlap margin" and neighbour dirtying (`RISK_REGISTER.md:294`). The design implements that exactly: the overlap margin is the 1,275 mm influence radius, and neighbour dirtying happens only when an edit's rectangle actually crosses the seam. RK-A2 (`docs/WORLD_ARCHITECTURE.md:469`, "test with a building straddling four cells") is covered by a hut centred on the four-cell corner (100 m, 100 m) (T5c).

---

## 8. Pathfinding (A7)

### 8.1 Passability for an agent

A node n is passable for an agent `a` when three conditions hold:
- `Clearance[n] ≥ ceil(a.RadiusMm / 5)`. This is conservative: stored units are floored, so passable implies a true distance of at least r, which is exactly `Separation == null` in `Blockers.cs:52`;
- `Outside` is not set;
- no door-zone cell of a *blocking* door lies within `units < ceil(r/5)`.

A door blocks when its kind is `Barrier` and it is standing, or when it is closed and `a.Doors == Never`. For `OpensUnlocked` agents, an openable door never blocks. Instead it adds a flat cost, whatever its current state (§10). Obstacles are inflated by the body radius because clearance is a distance, so no separate dilation pass exists.

### 8.2 Costs, heuristic, neighbours

- **Step cost.** 250 straight, 354 diagonal (250√2 = 353.55, rounded up to keep the octile heuristic admissible and consistent).
- **Proximity penalty.** +125 on entering a node whose clearance is below `r + 250 mm`. This keeps routes a node away from walls.
- **Door penalty.** +500 on entering a node inside an openable door's zone, for agents that open doors.
- **Heuristic.** `h = 250·(max(dx,dz) − min(dx,dz)) + 354·min(dx,dz)`, with dx and dz in nodes. All values are `int`.
- **Neighbour order.** Fixed: E(+1,0), N(0,+1), W(−1,0), S(0,−1), NE, NW, SW, SE. "N" is +Z.
- **Diagonals.** Allowed only when both orthogonal neighbours are passable (no corner-cutting).
- **Relaxation.** Strictly `newG < g[n]`, so the first-found parent wins among equals.

### 8.3 Deterministic tie-breaking

The open set is a binary heap of `(f, h, idx)`, where `idx = (j − j0)·W + (i − i0)` inside the search window, so ordering by idx is row-major `(j, i)`. The key is a total order, which makes the pop sequence independent of heap internals. Stale duplicate entries are skipped when popped (the node is already closed).

### 8.4 Window, cap, snapping, unreachable

- **Window.** The AABB of the start and goal nodes, grown by 48 nodes (12 m), clipped to the region bounds, with each side capped at 512 nodes (128 m). If the start-to-goal span exceeds 416 nodes (104 m), the outcome is `Unreachable` with reason `"too far for one search"`. M7 lint and assignment keep every work anchor within 100 m of its NPC's site. The coarse layer (§14) removes this limit later.
- **Start snapping.** If the start node is not passable (a node centre can be up to 177 mm from the body), take the first passable node in Chebyshev rings 1-4 (1 m). Order the candidates by squared mm distance to the exact start, then `(j, i)`. With none, the outcome is `Unreachable("start is walled in")`.
- **Goal snapping.** The same over rings 1-8 (2 m), measured to the exact goal. With none, the outcome is `Unreachable("nowhere to stand there")`.
- **Termination.** Popping the goal gives `Complete`. Reaching 12,000 expansions, or emptying the heap, ends the search. Then pick the closed node with minimum h, breaking ties by g and then `(j, i)`. If its h is at least 1,000 (4 nodes) below the start's h, the outcome is `Partial` to that node. Otherwise it is `Unreachable("no way there")`.
- **Output.** A node path, then string pulling (§8.5), then at most 32 waypoints. If more remain, keep the first 32 and mark the route `Partial`. The follower replans when the route is consumed.

### 8.5 Line of sight and string pulling (integer only)

- **Supercover line.** `LOS(a, b, agent, marginMm)` walks the integer supercover line between the two node indices. Diagonal ties include both side nodes. Each visited node must be passable with `clearance ≥ r + marginMm`. The start node and its 8 neighbours are exempt from the margin, so small deviations do not cause spurious replans.
- **String pulling.** From the anchor (the start node), advance k while `LOS(anchor, path[k], a, 100)` holds. Emit the last visible node as a waypoint and repeat from it. If `path[k+1]` itself fails LOS from the anchor (possible only at the margin), emit `path[k+1]`. Adjacent nodes are always connected, so this terminates.
- **Waypoint values.** Waypoints are node centres in integer mm. The last waypoint is the exact goal when the goal was not snapped, and otherwise the snapped node centre.

### 8.6 Floating point

The Domain nav files (`src/Domain/Spatial/Nav*.cs`) contain no `double`, `float` or `Math.*` transcendental. An architecture test enforces this (T12):
- distances use `long` squared terms and an integer square root, `Isqrt(long)` by Newton iteration with floor correction;
- circle distance is `Isqrt(dx² + dz²) − R`, clamped at 0;
- box distance clamps dx and dz and takes `Isqrt(dx² + dz²)`.

The only floating point the feature adds is in the World consumer, which builds the `MoveIntent` direction with the existing `sqrt`-and-round helper (`Companions.cs:558-568`). `CombatRules.FacingTowards` keeps its existing `Atan2` for facing (`src/Domain/Combat/Combat.cs:238-243`). Facing never feeds position in `Kinematics.Step`. It does feed perception arcs, but that is an existing exposure, and nav does not widen it.

### 8.7 Recalculation triggers

These are evaluated at each consumer tick, in this order. The first match wins.

| # | Trigger | Condition | Throttle |
|---|---|---|---|
| 1 | No route | `route is null` | Immediate. Budget permitting (§8.8) |
| 2 | Geometry | `route.Stamp ≠ StampFor(window(route))`, over the tiles the planning window covered | `tick − PlannedTick ≥ 5` |
| 3 | Blocked | `LOS(body, waypoint[0], agent, 0)` fails with current doors and barriers | `≥ 5` |
| 4 | Goal moved | `‖goal_now − route.Goal‖ > 2,000 mm` (companion). Anchors do not move | `≥ 10` |
| 5 | Stuck | the consumer's no-progress counter reaches 20 ticks (1 s): replan once | `≥ 5` |
| 6 | Unreachable retry | `Outcome == Unreachable` | `≥ 40` (2 s), or sooner on 2 or 4 |
| 7 | Consumed | no waypoints left and not arrived | `≥ 5` |

Every condition reads only persisted route fields, persisted consumer state, current authoritative geometry and flags, and the tick.

### 8.8 Per-tick planning budget

At most **1 plan per tick** in M7 (`plans_per_tick`, config). The budget counter lives in `NavigationService`. It resets when the tick number passed in differs from the last one seen, so it is always 0 at a boundary and never needs saving. Requests are granted in call order: system order, then key order within a system. A mover that is refused keeps its current route, or holds still with no route, and asks again next tick. With the companion and one NPC, the worst wait is 1 tick (50 ms).

---

## 9. Local movement (A8)

### 9.1 Three layers

1. **Global route** (§8): a sparse list of waypoints.
2. **Route following** (`NavFollow.Next`, pure Domain). Given `(route, body, agent, tile view, door states, tuning)`, it returns:

   ```csharp
   public readonly record struct NavFollowStep(NavPoint Target, bool Arrived, bool Progressed,
       string? Replan, string? OpenDoorRef, NavRoute Route /* waypoints advanced, BestRemainingMm updated */);
   ```

   - **Advance.** Drop `waypoint[0]` when it is within 400 mm of the body. When `‖body − wp0‖ ≤ 1,500 mm` and `LOS(body, wp1, a, 0)` holds, drop wp0 early. That second rule is one extra LOS a tick, and only when near a waypoint.
   - **Door.** If the segment from the body to wp0 enters a zone cell (units < r) of an openable door that is currently closed, and the body is within `InteractReachMm` (1,600 mm, `base_speeds.yaml:11`) of the door's footprint, return `OpenDoorRef` and hold. Otherwise target wp0 as usual. The closed leaf stops the body at the door until it is in reach.
   - **Progress.** `remaining = ‖body − wp0‖ + Σ‖wp_k − wp_k+1‖`, using the integer `Isqrt`. `Progressed = remaining < BestRemainingMm − 100`. When progress is made, `BestRemainingMm = remaining`. When a waypoint is dropped, `BestRemainingMm` is recomputed.
3. **Collision.** The consumer turns `Target` into a `MoveIntent` with its existing helper, and `Kinematics.Step` resolves the move against static geometry, placed pieces and dynamic bodies. Bodies are not planned around. They push and slide as today, and the watchdog handles jams.

The pathfinder never solves centimetres. Waypoints are 0.25 m-quantised corners, and the last centimetres are the consumer's arrival rule: 2.5 m of the player for the companion, 300 mm of the anchor for the work NPC.

### 9.2 Who opens the door

`OpenDoorRef` makes the consumer dispatch `AgentOpensDoor(DoorRef, Actor, ActorXMm, ActorZMm)` (§10). The consumer holds for that tick with an idle intent, facing the door centre.

### 9.3 Companion integration (minimal change to `CompanionSystem.Follow`)

```
Follow(c, npc, tick):                        // Companions.cs:325-361, extended
  catch-up check (unchanged: CompanionRules.CatchUp(distance, c.StuckTicks))
  gait (unchanged)
  trim reached marks (unchanged)
  if InClearView(body, player):              mode Direct → goal = player;  c.Route = null
  elif newest mark in clear view exists:     mode Trail  → goal = mark;    c.Route = null
  else:                                      mode Nav    (replaces "oldest mark", Companions.cs:351-353)
      decide replan per §8.7 (goal = player position)
      if replanning and budget granted: c.Route = Navigation.Plan(Person, body, player, tick)
      if c.Route is null or Outcome == Unreachable: goal = oldest mark or player (today's behaviour) → snag → catch-up
      else step = NavFollow.Next(...);  if step.OpenDoorRef → Dispatch(AgentOpensDoor); hold
           goal = step.Target;  c.Route = step.Route
  moved = Step(npc, goal, gait)               // unchanged helper, Kinematics.Step
  headway = mode == Nav ? step.Progressed : displacement ≥ 30 % of expected (unchanged)
  c.StuckTicks = headway ? 0 : c.StuckTicks + 1
```

Where the trail works, behaviour is identical: the C16 lodge route (`tests/Application.Tests/CompanionTests.cs:119-160`) takes the Direct and Trail branches almost everywhere. Nav takes over exactly where the trail fails today: after a wait-then-follow order (trail cleared), when placed walls hide every mark, and after player-only jumps or crouches. Catch-up stays the safety net (80-tick snag, > 30 m).

### 9.4 Work-NPC integration (for the NPC/building designers)

- **Agent profile.** `Person`, gait `Walk`.
- **Phases.** `GoingToWork → AtWork → GoingHome → Home`.
- **Goal.** The work anchor (§16.1 C-B6). On unassignment, the NPC's `NpcSite`.
- **Arrival.** Within 300 mm, then face the station.
- **Unreachable.** Stay put, set the view's `Blocked = "no way to the work place"`, and retry every 40 ticks. **Never teleport**: the exit criterion needs a walk.
- **Jams.** No progress for 200 ticks (10 s), for example because the player is standing in the doorway, sets `Waiting`. The NPC keeps retrying.
- **Retirement.** Back at the site with `Home`, the record is deleted and the body is transient again, at baseline. That is the record's retirement path (PERSISTENCE I-7).

---

## 10. Doors, barriers and agent capabilities

```csharp
public enum NavDoorPolicy { Never, OpensUnlocked }
public sealed record NavAgent(string Id, long RadiusMm, long HeightMm, NavDoorPolicy Doors);
// M7 registers exactly one class from config:  person = (350, 1800, OpensUnlocked)
public enum NavDoorKind { AuthoredDoor, PlacedDoor, Barrier }
```

| Overlay element | Source | Openable | Blocks planning when | Blocks movement (Kinematics) when |
|---|---|---|---|---|
| Authored door (`door.longhouse`, `door.forge_shed`) | `Layout.Doors` + flag (`Systems.cs:41`) | Yes: no locks exist in M7 | Closed and the agent's policy is `Never` | Closed (unchanged) |
| Placed door leaf | Building slice: door piece `Open` state | Yes, unless building reports it locked (none in M7) | Same | Closed (building puts closed leaves into `ClosedDoors()`, §16.1) |
| Barrier (`barrier.foldscar_fold`) | `Layout.Barriers` + flag | Never | Standing | Standing (unchanged) |

- **Planning is door-state-independent for openers.** For `OpensUnlocked`, the flat door penalty makes routes stable while the player opens and closes doors, and the state is only consulted at execution (§9.1).
- **Doors are baked open.** Door leaves are excluded from the clearance layer. Walls on either side are in it.
- **Opening.** `internal sealed record AgentOpensDoor(string DoorRef, EntityId Actor, long ActorXMm, long ActorZMm) : InternalCommand`. `Simulation.Dispatch` routes it by door kind:
  - authored doors go to `InteractionSystem`, which checks `ClosedFootprint.DistanceTo(actor) ≤ InteractReachMm`, dispatches `SetWorldFlag(cell, flag, 1)` and publishes `DoorToggled(Actor, key, true, tick)` (`src/World/Runtime/Events.cs:25`, whose Actor is already an `EntityId`);
  - placed doors go to `BuildingSystem`.

  A refusal becomes the route's replan reason, and the door is then planned as blocked for that mover for 200 ticks. That exclusion is transient and within a tick, so no state is needed: the refusal simply repeats. M7 has no refusal paths.
- **M7 agents do not close doors behind them.** That is §16.3, question 1.
- **Recommended to building** (not a nav requirement): closing any door, authored or placed, should be refused while *any* body overlaps its footprint. Today it checks only the player's body (`Systems.cs:267-268`).

Future capabilities (§14) fit this record: locks and keys become `OpensUnlocked | OpensWithKey(set)`, permission becomes a door predicate, and `CanCrouch` and `CanJump` select additional layers.

---

## 11. Placement navigability check (the contract to building)

ROADMAP asks for "placement validation that rejects un-navigable or overlapping configurations" (`docs/ROADMAP.md:283`). Nav owns the *navigable* half, as a **local** check that works at any region size. It never needs the region spawn.

```csharp
public sealed record NavEditVerdict(bool Ok, string? Reason, int NodesFlooded);
NavEditVerdict CheckEdit(IReadOnlyList<NavFootprint> add, IReadOnlyList<NavFootprint> remove, IReadOnlyList<NavProtectedPoint> protectedPoints);
```

- **Window W.** The AABB of the edited footprint together with every placed footprint chained to it within 2 m (its structure), grown by 8 m, with sides capped at 128 m.
- **Graph.** Nodes passable for `Person`, with openable doors passable and barriers by state. 8-connected with the no-corner-cutting rule, as in A*. It is computed on a copy-on-write overlay of W. The cache is never mutated by a what-if.
- **Anchors.** Passable nodes on W's boundary ring. Plus protected points inside W:
  - the player's body;
  - companion and NPC bodies and every `NpcSite`;
  - both approach points of each door (1 m either side of the footprint centre, along the door's normal);
  - authored containers and stations;
  - placed functional-piece anchors.

  Each protected point maps to the nearest passable node within 1.6 m (reach).
- **R1 (no severing).** Any two anchors connected inside W before the edit must be connected inside W after it.
- **R2 (functional access).** A newly placed functional piece's anchor must be connected, after the edit, to at least one boundary-ring anchor.
- **R3 (no burial).** A protected point with no passable node within 1.6 m after the edit is refused, with the reason "that would wall in <name>".
- **Removal needs no check.** Removal and door toggles cannot reduce connectivity.
- **Cost.** Two flood fills over W. A 30 × 30 m window is 14,400 nodes, about 0.3 ms. The cap is 262,144 nodes, about 5 ms. This runs in the command handler, at human frequency. The placement preview query runs it only when the candidate changes snap cell, never every frame.
- **Accepted limitation.** R1 is conservative. A wall that forces a detour longer than the window is refused ("that would cut off the way round; leave a gap"). A sealed empty room is allowed, because nothing protected is inside it.

---

## 12. Where the code lives

### 12.1 Domain: `src/Domain/Spatial/` (namespace `UNNAMED.Domain.Spatial`; pure, integer-only)

| File | Contents |
|---|---|
| `NavLattice.cs` | `NodeMm = 250`, `NodesPerTileAxis = 400`, `ClearanceUnitMm = 5`, `ClearanceCapUnits = 255`, `FormatVersion = 1`. `FloorDiv(long,long)`, `NodeOf(mm)`, `CenterMm(node)`, `TileOf(node)`. `readonly record struct NavTileKey(long Tx, long Tz)`, ordered by `(Tz, Tx)`. `readonly record struct NodeRect`. Domain may not reference World, so it never sees `CellKey` |
| `NavAgent.cs` | `NavAgent`, `NavDoorPolicy`, `NavDoorKind` |
| `NavInputs.cs` | `NavFootprint(Blocker Shape, NavDoorKind? Door, string? Ref, bool Openable)` and `NavInputs(bounds, ImmutableArray<NavTileKey> RegionTiles, long HeightClassMm)`, plus canonical ordering |
| `NavTile.cs` | The immutable tile (§3.3). Internal `byte[]` wrapped read-only. `WithRebuiltRect(...)` returns a copy |
| `NavRaster.cs` | `BuildTile(key, inputs, footprints)`, `RebuildRect(tile, rect, footprints)`, `Isqrt`, `DistanceUnits(node, footprint)`, `StampOf(key, footprints)` |
| `NavSearch.cs` | `NavScratch`: a class with generation-stamped `int[] g`, `int[] gen`, `byte[] dir` and a heap, sized for 512 × 512, allocated by the caller and never static. `NavLimits`. `NavSearch.Find(INavTiles tiles, NavAgent a, NavDoorView doors, start, goal, NavLimits, NavScratch) → NavPlan` |
| `NavPath.cs` | `NavPoint`, `NavRoute`, `NavOutcome`, supercover `Los`, `StringPull` |
| `NavFollow.cs` | `NavFollow.Next(...) → NavFollowStep` (§9.1) |
| `NavConnectivity.cs` | `CheckEdit` flood fills (§11) |

`INavTiles` is a Domain interface (`NavTile Get(NavTileKey)`) that the World service implements, so the Domain functions can build tiles lazily through it.

### 12.2 World: `src/World/Runtime/Navigation.cs`

- `internal sealed class NavigationService` is one per `Simulation`, created in the constructor, and exposed as `SystemContext.Navigation`. The `SystemContext` constructor gains the parameter.
  - **Fields:** the tile dictionary, the blocker index, the last-seen placed-footprint list, `NavScratch`, the per-tick budget and `NavCounters`.
  - **Methods:** `EnsureCurrent()`, `Plan(agent, from, to, tick)`, `Follow(...)`, `StampFor(window)`, `CheckEdit(...)`, `NearestPassable(agent, x, z, withinMm)`, `Prebuild()`.
- `internal sealed record AgentOpensDoor(...) : InternalCommand`, with an arm in `Simulation.Dispatch`.
- `public sealed record NavSetup(NavTuning Tuning, ImmutableSortedDictionary<string, NavAgent> Agents) { public static NavSetup Empty … }` becomes `SimulationSetup.Navigation { get; init; }` (the pattern of `Simulation.cs:14-33`).
- **Views.** `public sealed record NavCounters(...)` and `public sealed record NavDebugView(NavTileKey Around, long OriginXMm, long OriginZMm, int Side, ImmutableArray<byte> Clearance, ImmutableArray<NavRouteView> Routes)`.
- **Public surface.** `Simulation.NavDebug` and `Simulation.NavigationCounters` are **get-only properties**. `NavDebug` returns a 32 × 32 m square around the player. Get-only properties need no architecture allow-list edit (`ArchitectureTests.cs:111-131`).

### 12.3 Other projects

- **Content.** `src/Content/NavigationContent.cs` holds `Validate` and `Build`. Lint:
  - NAV001: the `person` radius equals `base_speeds.body_radius_m`;
  - NAV002: every radius ≤ 1.275 m;
  - NAV003: `search_window_max_m` ≤ 128;
  - NAV004: every agent height is a registered height class (only 1.8 in M7).

  `GameSession.Boot` sets `Navigation = NavigationContent.Build(loader)` (`src/Application/GameSession.cs:88-99`). `tests/Content.Tests/ValidationTests.cs:508` (`LoadAll_Loads_Yaml_Files`) gains `config.navigation`.
- **Persistence.** `NavRouteDto`. `CompanionDto` gains `route` (nullable: "no route"; the 13 → 14 step gives null). Because `V12.Player` references `CompanionDto` (`src/Persistence/Sections/SchemaV12.cs:6, 32`), the current `CompanionDto` is first frozen into the V12 and new V13 namespaces (research `persistence.md` §3.3 C). `CanonicalState` renders the route.

### 12.4 `content/config/navigation.yaml`

```yaml
id: config.navigation
kind: config
schema: 1
display_key: config.navigation.name
tags: [config]
notes: Domain navigation (M7, owner ruling 1) - derived from authoritative geometry, never saved; tuning of search and following.
search_expansions_max: 12000
search_margin_m: 12
search_window_max_m: 128
plans_per_tick: 1
replan_min_ticks: 5
replan_goal_moved_m: 2.0
replan_goal_moved_ticks: 10
replan_stuck_ticks: 20
retry_unreachable_ticks: 40
waypoint_reach_m: 0.4
waypoint_skip_check_m: 1.5
smoothing_margin_m: 0.1
proximity_margin_m: 0.25
proximity_penalty: 125
door_penalty: 500
start_snap_m: 1.0
goal_snap_m: 2.0
route_waypoints_max: 32
edit_window_margin_m: 8
agents:
  person: { radius_m: 0.35, height_m: 1.8, doors: opens_unlocked }
```

---

## 13. Instrumentation, memory and CPU

### 13.1 Counters

`NavCounters` holds counts only. It is deterministic, never in a digest, and never saved.

| Group | Counters |
|---|---|
| Tiles | `TilesBuilt`, `RectRebuilds`, `NodesRasterized` |
| Searches | `Searches`, `Expansions` (sum), `MaxExpansions`, `Capped`, `Partial`, `Unreachable` |
| Replans | `ReplanStamp`, `ReplanBlocked`, `ReplanGoalMoved`, `ReplanStuck`, `ReplanEmpty`, `BudgetDeferrals` |
| Edits and doors | `EditChecks`, `EditRefusals`, `DoorsOpenedByAgents` |
| Fallbacks | `CatchUpsAfterNavFailure` |

Wall time is measured outside the domain: tests use `Stopwatch`, as `CreatureTests.cs:526-544` does, and so does the presentation perf capture. No `Stopwatch` enters `src/Domain` or `src/World` (`research/sim_core.md` §6.2). The debug overlay shows the counters next to the clearance heat-map and the routes.

### 13.2 Ashen Hollow now (4 tiles)

- **Tiles.** 4 × 160,000 nodes × 2 B = **1,280,000 B (1.22 MiB)**.
- **Door zones.** One door: (0.4 + 2·1.275) × (1.6 + 2·1.275) = 12.2 m², about 196 cells × 8 B, about 1.6 KB. 2 doors, 1 barrier and a few placed doors come to under 10 KB.
- **Search scratch.** 512 × 512 = 262,144 nodes × 9 B (g, generation, dir) = 2.36 MB. The heap is bounded at 12,000 × 8 = 96,000 entries × 12 B = 1.15 MB. That is **≈ 3.5 MB**, allocated once per Simulation.
- **Build at load.** 21 boxes and 46 circles (67 footprints, counting doors and barrier), each over its `footprint ⊕ 1,275 mm` box, give **34,256** node distance evaluations. The region edge strip within the cap gives 4 × 800 × 5.1 = **16,320** bound nodes. Clearing 1.28 MB is trivial. At about 20-50 ns per evaluation in C#, the total is **≈ 1-3 ms once, at load**. Per-tile input counts: c_00_00 6, c_00_01 17, c_01_00 13, c_01_01 32.
- **Edits.** A wall piece is about 262 nodes × (1 + a few overlapping inputs), well under 0.1 ms. The stamp hashes about 20 inputs, in µs.
- **Planning.** The cap is 12,000 expansions. **Assumed ≥ 4 M expansions/s** gives ≤ 3 ms worst case, at most once per tick (the budget). Typical companion searches (≤ 30 m, since catch-up takes over beyond 30 m) expand 200-3,000 nodes: 0.05-0.75 ms. T13 measures the rate. If it is below 4 M/s, `search_expansions_max` (content) is lowered to keep the worst case ≤ 3 ms, inside the 4 ms world budget (`docs/WORLD_ARCHITECTURE.md:449`).
- **Following.** One LOS to wp0 (≤ 240 supercover nodes for 30 m) plus a stamp check over ≤ 4 tiles: a few µs per mover per tick.
- **Placement check.** About 0.3 ms for a typical window.
- **RK-06's 200-piece test.** 200 × (check + rect) ≈ 100 ms across the whole scripted test.

### 13.3 A 2 km region later (400 cells)

- **Resident tiles.** Pathfinding runs only in tier A (`docs/WORLD_ARCHITECTURE.md:234`). With `full_radius_m: 150` plus 10 m hysteresis (`content/config/simulation_tiers.yaml:7-10`), a player at a cell corner reaches cells whose nearest point is within 160 m. That is columns −2..+1 on each axis, with the far diagonal cell at √(101² + 101²) = 142.8 m, so **at most 16 tier-A cells**. Adding a one-cell ring gives at most 6 × 6 = **36 resident tiles × 320 KB = 11.5 MB**, plus 3.5 MB scratch.
- **Everything built.** 400 × 320 KB = 128 MB. This never happens: tiles are lazy and evicted least-recently-used beyond `MaxResidentTiles`, a service option that is unlimited in M7.
- **Streaming.** A tile builds in about 0.1-1 ms (a dense town tile of 500 inputs is about 150,000 evaluations). A seam crossing brings in up to 6 ring tiles, built lazily and prefetched at most one per tick. Residency never changes a result (T5d, T14). For comparison, the M3 spike's Godot bake took 6.2 s for 2 km and overflowed Recast's region IDs as one bake (`docs/M3_STATUS.md:72`).
- **NPC schedules.** 30 town NPCs replanning about once per game hour (120 s real) make about 0.25 plans/s. At the 60-actor tier-A cap (`WORLD_ARCHITECTURE.md:451`), with every actor pathing, it is about 30 plans/s (1.5 per tick). That needs `plans_per_tick: 2` and the coarse layer (§14).
- **Kinematics at scale (not nav, flagged).** `Resolve` scans every static blocker on every sub-step (`Kinematics.cs:189-198`). With 200 placed pieces the scan grows from 64 to about 264 per pass. The per-tile blocker index built for nav is the ready-made spatial index for a later Kinematics change. M7 does not change Kinematics (§16.5).

---

## 14. Future seams (A9): designed now, not built

| Future need | How it arrives as an addition |
|---|---|
| **Long routes, NPC schedules, tier B** | A **portal graph** derived from the tiles: per tile edge and agent class, each maximal run of border nodes passable on both sides becomes a portal at its mid-node. Intra-tile portal costs come from a bounded A*, cached per tile stamp. Long routes plan on portals, then refine with the fine A* in ≤ 128 m windows. Tier-B coarse movement (`docs/WORLD_ARCHITECTURE.md:235`) advances along portal routes at 2 Hz. Promotion snaps with `NearestPassable`, which answers WA §7.2's "off navmesh" legality (`WORLD_ARCHITECTURE.md:297`). Tiles, lattice and saves are unchanged. |
| **Larger settlements** | More inputs per tile, the evictable cache and the portal layer. Resident memory is set by the tier-A radius, not settlement size. |
| **Doors, locks, permission** | `NavDoorPolicy` gains `OpensWithKey`. A permission predicate (`CanPass(door, agent)`) comes from building ownership or faction access. Territory gating stays an access check, never a hostility input (scope ruling item 10). |
| **Roads** | A new byte layer, a cost class per node, rasterised from road splines (content). A* adds a per-class step multiplier. The heuristic uses the cheapest class's per-step cost to stay admissible. The flags byte already reserves bits 2-7. |
| **Bridges** | A planar bridge over impassable terrain (river, ravine) is a corridor where a "water" footprint layer is absent, which works in 2D with no change. A bridge **over another walkable path** (an overpass) needs two walkable levels at one (x, z). Owner ruling 2 excludes that. It would need a vertical domain, which does not exist (`Kinematics.cs:159`). |
| **Creature sizes** | Radius is already a threshold on clearance, up to the 1,275 mm cap. Larger bodies change the unit to 10 mm, which is a format constant and needs no migration. Height classes, for example a 2.6 m `irregular_heavy`, add one clearance layer per class. Crouching and jumping agents select a different layer or an extra exclusion list. |
| **Creature pathing (return home, search)** | `CreatureRecord` gains a `NavRoute` (a CreatureDto freeze and a migration). Creatures use `NavAgent(Id, definition radius, 1800, Never)`. Charges and lunges stay un-pathed. |
| **Terrain slope limits** | A per-class "too steep" flag bit, rasterised from `TerrainGrid` gradients. The grid is 5 m = 20 nodes, which aligns. |
| **Networking (seam only)** | Nav is deterministic domain code keyed to authoritative state, so a future authority runs it and clients never need to. |

---

## 15. Acceptance tests (A10)

- **Domain** tests go in `tests/Domain.Tests/Spatial/NavigationTests.cs`: pure, synthetic `NavInputs`, headless.
- **Application** tests go in `tests/Application.Tests/NavigationTests.cs`, plus extensions to `CompanionTests`, `DeterminismAndViewTests` and the building tests. They use the real content through `Harness`.
- **Persistence** tests go in `RoundTripTests` and the v14 fixture.

| # | Test | Assertions |
|---|---|---|
| T1 | `OpenGround_PathIsStraight_AndIntegral` | Open 40 m field, start (10,10) m, goal (30,25) m. `Complete`. One waypoint equal to the exact goal. Expansions ≤ 2 × path nodes. All waypoints are integer mm |
| T2 | `WallBetween_RoutesAround_KeepingClear` | A 20 × 0.4 m wall between start and goal. `Complete`. Every waypoint is at least r from the wall. Walking the route with `Kinematics.Step` reaches the goal within 1.2 × route length / speed ticks |
| T3 | `PieceAcrossRoute_NextTickReplans_AndArrives` (App) | The work NPC is mid-route. `PlacePiece` lays a wall across the remaining route. The next NPC tick replans (`ReplanStamp` == 1). The new route clears the wall. The NPC arrives. No `CompanionCaughtUp`-style relocation, and the NPC position never jumps more than 1 tick of travel |
| T4 | `PieceRemoved_ShortcutTaken` (App) | A detour route around a wall piece. Dismantle it. Replan with reason Stamp. The new route length is ≤ 60 % of the old, and arrival is earlier than with the old route (compared against a control world) |
| T5a | `Tiles_AreIndependentOfBuildOrder` | For every permutation of building tiles {c_00_00 … c_01_01}, and one tile built alone and evicted: bytes, door zones and stamps are equal. A monolithic 200 × 200 m raster equals the union of the four tiles |
| T5b | `SeamPath_EqualsMonolithicPath` | 50 start/goal pairs across x = 100 m and z = 100 m, drawn from `RngChannel`. Tiled search and monolithic search give identical node paths |
| T5c | `FourCellHut_EnterAndLeaveOnce` | A 6 × 6 m hut centred on (100, 100) m, straddling four cells, with doors on its west and east walls. A path from (90, 100) to the interior crosses the hut boundary exactly once. West to east through it crosses exactly twice (RK-14 "exits and re-enters" fails the test). Both routes are walked with Kinematics |
| T5d | `EvictAndRebuild_SamePathsSameBytes` | Evict one tile (the "cell unload"), query again: identical bytes and identical routes. RK-14's reload case |
| T5e | `SnapShift_IsTranslationInvariant` | The same hut shifted by k × 500 mm on open ground: the clearance pattern equals the original shifted by 2k nodes, and waypoints shift by exactly k × 500 mm |
| T6 | `RouteRoundTrips_AndGoesOnTheSame` (App + Persistence) | The companion in Nav mode mid-route, and separately the NPC mid-walk. Save and load. `NavRoute` is equal field by field. `StateDigest` is equal after the load and again after 200 ticks. Extends `CompanionTests.cs:285-319`. The v14 fixture carries a companion with a two-waypoint route |
| T7 | `ReplayWithEditsAndDoors_EqualsItsDigest` (App) | Extends `DeterminismAndViewTests.cs:49-78`: 200 commands including PlacePiece, Dismantle, Interact(door), companion orders and an NPC assignment, replayed from the save. Digest and rejection log are equal. Plus `TwoSimulations_ShareNoNavState`: two worlds in one process with different edits keep their tiles and stamps distinct |
| T8 | `BodySizes_Thresholds` | The real lodge door open. r = 350 and r = 550 get `Complete` through the door. r = 850 gets `Unreachable` from outside to inside. r = 550 through a 1.2 m synthetic door gets `Unreachable`, and r = 350 gets `Complete` |
| T9 | `Unreachable_IsBoundedAndHandled` | Goal inside a sealed box: `Unreachable("nowhere to stand there")` or `("no way there")`, expansions ≤ cap. App: companion Nav mode with an unreachable player goes snag, then catch-up, within 80 + 5 ticks. The NPC gets `Blocked` and never moves off its site |
| T10 | `DirtyRect_EqualsFullRebuild_AlwaysAndBack` | 100 random place/remove edits via `RebuildRect`. After each one, tile bytes equal `BuildTile` from scratch. After undoing all of them, bytes and stamps equal the originals. The same query gives the same path every time |
| T11 | `Doors_OpenersOpen_OthersDetourOrFail` (App) | Player inside the lodge, door closed, companion outside in Nav mode. He opens the door (`DoorToggled` with his instance ID as actor) and arrives with no catch-up. A `Never` agent (a test profile) gets `Unreachable`. Closing the door on a `Never` agent's next segment gives `ReplanBlocked` |
| T12 | `NavDomain_IsIntegerOnly` (Architecture) | No `double`, `float`, `Math.Sqrt`, `Math.Sin`, `Math.Cos`, `Math.Atan`, `Math.Pow` or `Math.Round` in `src/Domain/Spatial/Nav*.cs`. No static mutable fields (already covered) |
| T13 | `NavBudget_OnTheHollow` (App, `Stopwatch`) | Build all 4 tiles in < 20 ms. The 100 hardest pairs (outside to inside the lodge and smithy, across the den notch) all finish within the cap, with a mean < 1 ms. Log expansions per second. The whole M7 companion + NPC scripted route stays within `plans_per_tick` |
| T14 | `Residency_NeverChangesResults` | A synthetic 1 km × 1 km (100 tiles) field with 2,000 blockers from `RngChannel`, and 200 routes, run with `MaxResidentTiles = 9` and with unlimited residency. Identical routes. Resident count ≤ 9 |
| T15 | `C16_Unchanged` | The existing `ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds` passes unmodified, with zero `CompanionCaughtUp` |
| T16 | `EditCheck_Rules` (App) | Refused: sealing the player in (R3), sealing an NPC site (R1), a station inside a doorless room (R2). Allowed: a sealed empty room, and a wall with a 1.6 m gap |
| T17 | `WorkNpc_InThroughAround` (App, the exit criterion) | Assign the NPC to the station inside the seam-straddling build-area structure. It walks from its site, opening the door, reaches the anchor within 300 mm, and stays 200 ticks. Unassigned, it walks back to its site. The route crosses the seam. Save mid-walk continues identically. The presentation playthrough records the same script (the runtime recording that replaces RK-14's "needs the engine") |

---

## 16. Contracts, reconciliation, questions, open issues

### 16.1 Cross-system contracts

**What nav needs from building:**
- **C-B1.** A canonical read of placed pieces: `NavFootprint`s in integer-mm AABBs with 90° rotation (scope ruling item 3), traversal class (Solid, Door, or NonBlocking such as a pad or roof), `HeightMm > 0`, `ClearanceMm = 0` for solids, a door `Ref` and its `Open` state. It is exposed as an immutable collection whose reference changes when footprints change.
- **C-B2.** One effective collision set. Placed solids join every blocking query exactly as authored structures do:
  - `Kinematics.Step` for all movers, and presentation prediction (`PlayerController.cs:127` today reads `Setup.Layout.Space` plus `DynamicBlockers`);
  - `IsClear`;
  - the three `Walled` copies (`Creatures.cs:895`, `Companions.cs:597-598`, `Combat.cs:569`);
  - sight (`Creatures.cs:296`);
  - `CanStand`.

  Closed placed door leaves join `ClosedDoors()`. The recommended form is `SystemContext.Space`, an effective `WalkSpace` of authored plus placed solids in canonical geometric order, with a get-only `Simulation.Space` for prediction. Nav correctness depends only on "same set as Kinematics".
- **C-B3.** The snap translation step is a multiple of 250 mm (0.5 m recommended). Door clear opening ≥ 1,200 mm; 1,600 mm (the Phase-1 precedent) is recommended. Gaps intended to be walkable are ≥ 950 mm.
- **C-B4.** `PlacePiece` validation calls `NavigationService.CheckEdit` (§11) and returns its reason text verbatim.
- **C-B5.** Piece IDs never order nav input. Building may use any ID scheme.
- **C-B6.** Each functional piece exposes a work or access anchor: a point 1.0 m in front of its footprint face, in integer mm.
- **C-B7.** A per-tile piece ceiling (RK-06), for example 400 pieces per 100 m cell, as a content constraint.

**What nav offers:**
- **C-N1.** `Plan`, `Follow`, `NearestPassable`, `CheckEdit`, `Reachable(agent, a, b)`.
- **C-N2.** The `NavRoute` value type and `NavRouteDto`, embedded by owners.
- **C-N3.** The `AgentOpensDoor` internal command, handled by `InteractionSystem` for authored doors and by `BuildingSystem` for placed doors.
- **C-N4.** `Simulation.NavDebug` and `NavigationCounters`.

**Other parts:**
- **NPC/work.** The work record persists body, phase and `NavRoute`, and enters a digest. `Person` agent, `Walk` gait. It never teleports.
- **Persistence (schema 14).** `CompanionDto.route?` (freeze `CompanionDto` into V12 and V13 first), the route inside the work-assignment DTO, `CanonicalState`, the digest tag bump, a v14 fixture with routes, and a `MigrationTests` step. No new section file.
- **Presentation.** A debug overlay showing the clearance heat-map, routes, door zones and counters. It draws NPC-opened doors from `DoorToggled`. Godot Navigation is never authoritative. A cosmetic Godot navmesh stays spike-only.
- **Factions.** None in M7. Future territory access plugs into the door and permission predicate (§14) and never into hostility.

### 16.2 Documentation to reconcile in M7's first slice (rulings 1 and 2)

- `WORLD_ARCHITECTURE.md:137`: "Collision, navmesh … Baked" becomes "Collision (domain: content + placed pieces); navigation tiles (domain, derived, never saved)".
- `WORLD_ARCHITECTURE.md:414`: "rebuilt per cell on change, debounced" becomes "the exact affected rectangle, on the next query; no debounce".
- `WORLD_ARCHITECTURE.md:435`: remove the navmesh row from the presentation/streaming contract (ruling 1).
- `WORLD_ARCHITECTURE.md:469`: RK-A2 becomes "solved by construction; `NavigationTests` T5".
- `RISK_REGISTER.md:290`: RK-14 validation becomes headless (T5c, T5d, T17), plus the playthrough recording.
- `PERSISTENCE.md:83-84`: navigation tiles are derived at load and never saved. "A moving NPC's committed route is saved with its owner."
- `SYSTEMS.md:443`: amend to "search state and caches are never persisted; a mover's committed route is mover state (precedent: schema-12 trail)".
- SYSTEMS S-25: "Transient: path following state" becomes "persisted route".
- ROADMAP M7 exit: "the navmesh updates on placement" becomes "domain navigation reflects every placement on the next tick".

### 16.3 Owner questions (nav proposes one)

1. **May NPCs and companions open unlocked doors themselves?** Default: yes. M7 agents open authored and placed doors on their way and **leave them open**. The alternative is "close behind them", which adds persisted door-courtesy state and a closing rule. Either way nothing is locked in M7.

### 16.4 Recommendation within the scope ruling's latitude

Creature return-home via nav is "optional if cheap". This design says it is **not cheap**: it would add a `CreatureDto` shape change (a frozen copy, a migration and fixture expectations) and reopen M3d behaviour tuning. The seam is recorded in §14.

### 16.5 Open issues

- **Kinematics cost.** Its scan cost grows linearly with placed pieces (§13.3). Measure it in RK-06's 200-piece test. The nav blocker index is the ready fix, outside M7 unless the measurement fails 4 ms.
- **Three `Walled` copies.** Placed pieces must reach all three (`Creatures.cs:895`, `Companions.cs:597-598`, `Combat.cs:569`). Building should consolidate them into `SystemContext` in the same change, or one copy will miss placed walls.
- **Height-blind queries.** `IsClear`, `Walled` and sight ignore height (`Kinematics.cs:170-173`); nav follows height-aware movement. They agree for solid pieces; an optional 1.2 m half-wall blocks movement and sight, as the fence does today.
- **The 104 m single-search limit** bounds work-anchor distance from the NPC's site until the portal layer exists (content lint).
- **The expansion rate (4 M/s) is assumed.** T13 must confirm it on ASTRAL and on RAZER (window still owed).
- **Tier transitions** for moving NPCs are not exercised in the hollow, where every cell is tier A (`docs/M3_STATUS.md:50`); `NearestPassable` is ready for M3g/M9.
