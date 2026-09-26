# M7 Navigation v1: candidate design (lens: minimum sufficient)

Status: an independent candidate from the M7 design panel, 2026-09-24. It is not normative. It follows `drafts/00_SCOPE_RULINGS.md` and does not challenge any scope ruling.
Base: origin/main `e10d2c4`, read from the snapshot. Every `path:line` is repo-relative at that commit. Numbers marked **estimate** are arithmetic, not measurements. §3.6 names the test that turns each one into a measurement.
Units: millimetres (`long`), millidegrees, and ticks (20 Hz, 50 ms). Terms: a **lattice cell** is a navigation grid square; a **world cell** is the 100 m persistence cell. The two are never interchangeable.

---

## 0. The recommendation on one page

1. **Consumers.** Two movers:
   - the one assigned settlement NPC (new: it walks to a work anchor, holds, and walks home);
   - Tavar, whose trail follower gains a route fallback.

   Placement validation queries nav, and a debug overlay shows it. Creatures, the player and static NPCs do not use it.
2. **Representation.** A region-wide integer clearance raster at 250 mm.
   - Each lattice cell holds two bytes: how many body-radius classes fit against solids, and how many against gates.
   - It is derived from exactly the blockers `Kinematics.Step` collides with.
3. **Routing.**
   - Windowed A* (8-connected, costs 10/14, octile heuristic, total-order tie-break, expansion budget).
   - String-pulled into at most 48 corners with the companion's own clear-view test (`src/World/Runtime/Companions.cs:584-595`).
   - Walked with his own "farthest point in clear view" steering (`:339-356`).
   - Collision stays in `Kinematics.Step`.
4. **Seams are not in the representation.**
   - 100 m / 250 mm = 400, so seams lie on lattice lines; nothing is duplicated or stitched.
   - Rebuilds are a min-reduction, so any partial or reordered rebuild is byte-identical to a full one.
5. **Dynamic update**, at one command boundary: piece committed → rebuild the footprint rectangle (+650 mm) → `RoutesInvalidated` → the NPC replans on the next tick. Door toggles and barrier lifts rebuild nothing.
6. **Saving.**
   - Nothing of nav is saved. The raster is a pure function of saved state, rebuilt in the `Simulation` constructor.
   - Routes are saved with their movers: the companion's is his existing trail; the NPC's is a small schema-14 record.
7. **Doors.** The NPC plans through closed doors and opens them within reach. The companion keeps Phase-1 behaviour.
8. **Cost** (estimates):
   - 1.25 MiB of raster;
   - full build < 3 ms; a piece's rebuild < 0.5 ms;
   - a search 0.3-2 ms, capped at 12,000 expansions.

   No new transcendental maths. `Kinematics`, `CreatureSystem` and the player's movement are untouched.

---

## 1. What exists, and what this design refuses to disturb

Facts verified in source:

- **Movement.** `Kinematics.Step` is the one movement function, shared with prediction (`src/Presentation/Player/PlayerController.cs:127`).
  - It pushes a circle out of footprints, in sub-steps of at most half the radius, with 4 relaxation passes, a bounds clamp and whole-mm rounding (`src/Domain/Spatial/Kinematics.cs:129-160`, `:183-208`).
  - `Blocks` decides what blocks a body (`:166-167`). Nothing is stood on (`:159`).
- **Geometry.** Axis-aligned boxes and circles only (`src/Domain/Spatial/Blockers.cs:40-41`). `Crosses` counts touching as crossing (`:89`). There is one region-wide `WalkSpace` (`Kinematics.cs:104`). Doors and barriers are flag-gated and re-read on every call (`src/World/Runtime/Systems.cs:41-48`).
- **The companion.**
  - He follows a persisted trail, heading for the newest mark in clear view, else the oldest (`src/World/Runtime/Companions.cs:308-316`, `:339-356`).
  - "Headway" is ≥ 30% of the expected travel (`:357-360`).
  - He teleports beyond 30 m, or after 80 ticks without headway (`:331-332`, `:367-393`).
  - His clear-view test checks three offset segments (`:584-598`).
- **Creatures** steer straight (`src/World/Runtime/Creatures.cs:820-838`).
- **NPCs** never move (`src/World/Runtime/Social.cs:149-164`). Their bodies are transient (`src/World/Runtime/RuntimeState.cs:60-61`) and **outside** `StateDigest` (`src/World/Runtime/Simulation.cs:357-364`).
- **Tests that must stay green.**
  - Replay equals the digest (`tests/Application.Tests/DeterminismAndViewTests.cs:49-77`).
  - Save-then-continue equals continue (`tests/Application.Tests/CompanionTests.cs:285-319`).
  - C16 (`CompanionTests.cs:119-160`).
- **Dimensions.**
  - Radius 350 mm (`content/config/base_speeds.yaml:10`); stand height 1.8 m (`:18`).
  - 1.6 m doorways in 0.4 m walls (`content/regions/ashen_hollow.yaml:68-78`, `:143-144`).
  - Creatures are 350-550 mm.
  - The region has four 100 m world cells (`ashen_hollow.yaml:8-10`).

Phase-1 code this design touches:
- one hook in `CompanionSystem.Follow`;
- an errand branch in `NpcSystem.Tick` that runs only for NPCs with an errand;
- one `Build()` call in the `Simulation` constructor;
- two read helpers on `SystemContext`.

It does not touch `Kinematics`, `Blockers`, `CreatureSystem` or `MovementSystem`.

---

## 2. Part A: the owner's brief

### A1. Who needs pathfinding in M7

| Consumer | Needs | Why, from M7 text | In M7? |
|---|---|---|---|
| **Assigned settlement NPC** (one; the NPC-assignment design picks who) | Route from its site to a work anchor inside a player structure, possibly through a door, across a seam; hold; route home when unassigned | ROADMAP M7 exit: "assign an NPC to work in it, verify the NPC navigates in, through, and around it — including a structure straddling a cell boundary" (`docs/ROADMAP.md:284`); scope ruling item 5 | **Yes: primary consumer** |
| **Companion (Tavar)** | A route when his trail fails: no trail mark and not the character in clear view (including right after wait → follow, which clears the trail), or half-way to a snag | ROADMAP M7 entry "NPCs and companions path reliably" (`:281`); D-08's justification; scope "used by the companion and the assigned NPC" | **Yes, as a fallback only.** The trail stays the primary mechanism, and catch-up stays the last resort |
| **Placement validation** (building) | Queries only: standable point, enclosed pocket, reachability | ROADMAP M7 "placement validation that rejects un-navigable ... configurations" (`:283`) | **Yes** |
| **Debug overlay / tests** | Read the raster, routes and counters | Scope "debug views sufficient to prove" | **Yes** |
| Creatures | Return home around walls | Scope: optional-if-cheap | **No.** See below |
| Player | Steers himself | — | Never |
| Static NPCs (Renn, Kera, Sel, and Tavar before recruitment) | Nothing moves them | `Social.cs:149-164` | No |

**Creatures are left out on purpose.**
- Their straight steer-and-slide is load-bearing:
  - the charge stun (`Creatures.cs:589-594`);
  - "searched for, not pathed to" (`docs/M3D_STATUS.md:78`);
  - the stealth rule that a creature goes only where it perceived the target (`Creatures.cs:466-470`).
- The gain is quality, not an exit criterion.
- If the lead wants it anyway, the smallest safe form is limited to the `Returning` mind: a class from the creature's radius, no door opening, a plan after 40 ticks without headway, a fallback to Phase-1 on `Unreachable`, and no new saved field.

### A2. Which representation

The candidates below are compared on Otherreach's actual constraints:
- integer-millimetre authoritative state, and a digest-tested save/replay;
- axis-aligned boxes and circles;
- 1.6 m doors in 0.4 m walls, and 0.35-0.55 m bodies;
- 100 m world cells, a 200 m region now and 2 km regions in M9;
- 20 Hz with at most 4 ms of world systems per tick;
- a companion who already follows "corner points in clear view".

| | **A. Uniform clearance raster (recommended)** | **B1. Waypoint graph** (coarse samples joined by clear-view edges) | **B2. Visibility graph** (inflated-footprint corners) | **C. Steering + local avoidance** | **D. Domain navmesh** (Recast-like, in C#) |
|---|---|---|---|---|---|
| Determinism | Integer end to end; smoothing reuses existing +-x/ predicates | Integer lattice; `double` edge tests | Many pairwise predicates; tangency degeneracies | Deterministic, but no global answer | Floating-point triangulation and funnel; high risk |
| Seams | None: seams lie on lattice lines | None if region-global | None if region-global | None | Tile stitching (RK-14's problem) |
| Building edits | Recompute a small rectangle; provably equal to a full build | Re-sample and re-edge; doors need building-injected nodes | Re-test the edges crossing the change | Nothing to rebuild or guarantee | Re-bake the touched tiles |
| 1.6 m doors | Exact; 2-3 lattice centres | A 2 m sample misses them | Exact | Cannot plan to the far side of a hut | Exact |
| Memory (hollow) | 1.25 MiB | Tiny | Tiny | 0 | Small |
| CPU | Build < 3 ms; search 0.3-6 ms, bounded | Cheap | Build ≈ 12.7M segment tests | Per tick only | Bake 0.1 s to seconds (spike: 6.2 s for 2 km, `docs/M3_STATUS.md:72`) |
| Placement reachability | Bounded flood: trivial | Fine | Fine | **Impossible** | Fine |
| 2 km later | Tile by world cell, tier-A tiles resident (~8 MB) | Sampling density | n² blow-up in forests | — | Ruled away from |
| Code (estimate) | ~500 Domain + ~250 runtime lines | ~600 + socket coupling | ~800 | ~150 | 3,000+ |

**Recommendation: A**, with two reuse decisions that make it cheaper than a textbook grid.

1. The path is not followed cell by cell. It is string-pulled into corners with the companion's existing clear-view predicate, and followed by the companion's existing steering rule. The planner and the follower therefore share one definition of "can walk straight there".
2. The raster stores clearance *classes*, not a single blocked bit. One raster then serves every body size in the content, which costs one extra comparison per footprint cell.

**C is rejected** because it cannot say "unreachable". Placement validation needs that answer, and so do the RK-14 fail conditions (`docs/RISK_REGISTER.md:292`). It also cannot bring an NPC in through a door on the far side of a hut.

**B2 is rejected** on rebuild cost and fragility. **B1 is rejected** because it forces building to publish nav nodes. **D is rejected** on code size and floating-point risk. It is also the representation owner ruling 1 moved away from.

### A3. Lattice resolution: 250 mm

**Inputs and definitions.**
- Inputs, all from source:
  - body radius 350 mm for people, 350-550 mm for creatures;
  - doorways and the fence gap W = 1.6 m, walls t = 0.4 m;
  - world cell 100 m;
  - run 160 mm per tick.
- Margin m = 100 mm.
- Class radii R = {350, 450, 550} mm. Each body maps to the smallest class radius ≥ its own.

**Test 1: doorways.**
- A doorway's corridor for body centres is `W − 2(r + m)` wide. An interval of length `L` contains at least `⌊L/s⌋` lattice centres.
- For a 1.6 m door, the corridors and guaranteed centres are:

  | Class | Corridor | s = 250 mm | s = 500 mm |
  |---|---|---|---|
  | 0 (350) | 0.70 m | 2 | 1 |
  | 1 (450) | 0.50 m | 2 | 1 |
  | 2 (550) | 0.30 m | 1 | **0** |

- At 500 mm the boar's route through a door would depend on where the door happens to sit against the lattice: a false "unreachable".
- At 250 mm, **every body in content fits a 1.6 m door in nav exactly when it fits in Kinematics**.

**Test 2: no leaks through walls.**
- A wall's blocked band for class 0 is t + 0.9 m = 1.3 m, against a diagonal step of `s√2` = 354 mm.
- Corner-cutting is forbidden, so even a zero-thickness wall (0.9 m band) cannot leak.
- Both resolutions pass.

**Test 3: seams.** 100,000 mm divides by both (400 and 200 lattice cells). The linter requires `100,000 mod s = 0`.

**Test 4: movement scale.** Steering re-decides every tick and corners are only waypoints, so resolution does not affect smoothness.

**Cost of 250 mm.**
- 4x the cells: 640,000, 1.25 MiB, against 0.31 MiB at 500 mm.
- About 2x the expansions per metre.
- The budget and window bound this, and searches are rare events (§3.6).

**The 100 mm margin.**
- `Resolve` can leave residual overlap in corners.
- `Crosses` counts touching as crossing (`Blockers.cs:89`): a corner exactly `r` from a wall would read "not in clear view" to the follower.
- The margin keeps every planned point standable and visible.

**Constraint handed to building.** A doorway's clear opening must be ≥ **1.40 m**, which gives class 0 two centres (0.50 m = 2s). **1.6 m is recommended.**

### A4. Authority: what is read, derived, cached, invalidated, rebuilt and saved

| Item | Kind | Source / owner | When read or rebuilt | Saved? |
|---|---|---|---|---|
| Region bounds, authored static blockers (structures and switch bodies) | Authoritative content | `Setup.Layout.Space` (`Kinematics.cs:104`) | At full build | Content |
| Authored doors (`DoorSite.ClosedFootprint`), barriers (`BarrierSite.Footprint`) | Authoritative content (footprints) | `RegionLayout.cs:12,28` | At full build (gate layer) | Content |
| Authored door open / barrier lifted | Authoritative state | Cell `world.*` flags via `SystemContext.IsOpen/IsLifted` (`Systems.cs:41-45`) | **At query time**, every search and sight test | Yes (cells.msgpack, existing) |
| Placed pieces: footprints and traversal class | Authoritative state | Building's world-delta records (building design) | Full build; rectangle rebuild on place/remove/destroy | Yes (building's schema-14 records) |
| Placed door open state | Authoritative state | Building | **At query time** | Yes (building) |
| Stand height for the blocking filter | Content | `config.base_speeds` `stand_height_m` (`base_speeds.yaml:18`) | At build | Content |
| Navigation tuning | Content | new `config.navigation` (§3.4) | At boot | Content |
| **`NavRaster`** (solid-fit and gate-fit bytes per lattice cell) | **Derived** | `NavigationSystem`, `StateSlice.Navigation` (transient) | Full build in the `Simulation` constructor; rectangle rebuild on `RebuildNavigation` | **No** |
| Gate list (authored + placed door leaves + barriers) | Derived | `NavigationSystem` | Rebuilt with the raster | No |
| Search scratch (g-costs, parents, heap) | Transient | Allocated per search, window-sized | Per search | No |
| Counters (`NavStats`) | Transient, diagnostic only | `NavigationSystem` | Incremented | No; never read by any decision |
| Companion route corners | **Authoritative mover state** | His trail (`PlayerState.cs:44`, `:52-62`) | Written when he plans | **Yes** (schema 12; already in the digest, `PlayerState.cs:360-363`) |
| NPC errand: goal, corners, status, stuck ticks, body | **Authoritative mover state** | `NpcSystem`, new `StateSlice.NpcErrands` | Every tick while on an errand | **Yes** (schema 14, §3.1) |

**Invariant:** no mover decision may read `NavStats`, a rebuild revision, a search-scratch leftover, or anything else outside the saved state plus the raster. The raster itself must be a pure function of saved state plus content. §A10 tests this directly.

### A5. Dynamic building updates: the exact sequence

All of this happens synchronously at **one command boundary** of `Simulation.DrainCommands` (`Simulation.cs:268-306`). A replay therefore reproduces it at the same boundary.

```
PlacePieceCommand (player, logged)                       [boundary N]
  BuildingSystem.Handle
    1 validate (includes nav queries on a TENTATIVE raster: §4 V2-V4)
    2 dispatch ExchangeItems (materials)  -- refusable step first
    3 commit the piece record to the world delta (RuntimeState gated setter)
    4 publish PiecePlaced
    5 dispatch RebuildNavigation(changed: [footprints of the piece])     <- nav starts here
        NavigationSystem.Handle
          a rect = LatticeRect.Around(changed, inflate = maxClassRadius + margin = 650 mm)
          b raster' = raster.With(rect, Solids(), Gates())   // copy, recompute rect cells only
          c State.SetNavigation(owner, raster'); refresh gate list; stats++
          d publish NavigationRebuilt(rect, cells, tick)
          e dispatch RoutesInvalidated(rect)
              NpcSystem.Handle: every errand not Arrived -> Corners = [], StuckTicks = 0   (saved effect)
Step N+1
  ... companions: trail follower; sight tests now include the new piece -> reactive fallback (A8)
  ... npcs: errand with empty corners -> plan (A7) -> steer -> Kinematics.Step
```

- **Removal, dismantle and destruction** take the same path with the removed footprints. Every errand then replans, and can take the shorter way.
- **Door toggles and barrier lifts** rebuild and invalidate nothing. Gates are evaluated at query time; openers plan through doors anyway; the companion meets a newly closed door through his clear-view test (§A8).

**What is recomputed.**
- Only a rectangle, never a world cell: the lattice cells whose centres fall in the footprint's bounding box inflated by 650 mm, clipped to the raster.
  - `i0 = ceilDiv(minX − 650 − x0 − s/2, s)`
  - `i1 = floorDiv(maxX + 650 − x0 − s/2, s)`
  - `j0` and `j1` the same on Z.
- A 3 m wall is about 17 x 7 = 119 cells. Neighbouring world cells are touched only where the rectangle overlaps them.

**Why a rectangle rebuild equals a full rebuild.**
- A cell's value is a `min` over footprints and bounds, and no footprint influences cells beyond 650 mm.
- So every cell that can change lies inside the rectangle, and each is recomputed from *all* current footprints.
- The result is independent of footprint order, IDs, world-cell order and history.

**Batching.** One piece per command, so one rebuild per command. No debounce is needed.

### A6. Cell seams

- **One lattice per region.**
  - Its origin is the region's origin: `x0 = rx · 2,000,000 mm`, 0 for `r_0_0`.
  - Lattice cell `(i, j)` covers `[x0 + i·s, x0 + (i+1)·s)` and has centre `x0 + i·s + 125`, an integer.
  - Ashen Hollow is `i, j ∈ [0, 800)`. The grid covers `Layout.Space` bounds: `[floorDiv(MinX − x0, s), ceilDiv(MaxX − x0, s))`.
- **No duplicated nodes, no ambiguous coordinates.**
  - Since 100,000 mod 250 = 0, world seams (x = 100 m, z = 100 m) fall *between* lattice indices 399 and 400.
  - Every lattice cell lies wholly inside one world cell, the one containing its centre.
  - A point exactly on a seam (x = 100,000 mm) maps to `i = 400`. That matches `CellKey.OfWorld`'s floor semantics (`src/World/Coordinates.cs:93-96`: x = 100 m is world cell 1).
  - Nav converts with integer floor division, never through `double` metres.
- **No disconnected borders.**
  - A neighbour is `index ± 1` or `± width`, whatever world cell either side is in.
  - A search window, a rebuild rectangle and a route can each span any number of world cells.
  - Stitching is not needed and does not exist.
- **No order-dependent rebuilds.** This is the min-reduction of §A5.
  - Rebuilding "the lattice under world cell c_01_01" is just the rectangle `i ∈ [400, 800), j ∈ [400, 800)`.
  - The seam test (A10 T9) rebuilds each world cell's rectangle in each of the 24 orders and asserts byte equality with the full build.
- **Storage.** M7 stores the raster as one flat array per layer. Its API (`SolidFit(i, j)`, `GateFit(i, j)`) hides storage, so M9 can tile it by world cell without changing a single result (§A9).
- **RK-14 restated for a domain representation.** RK-14 fails if:
  1. no path is found;
  2. the path exits and re-enters the structure;
  3. the path is valid before a reload and invalid after.

  (1) and (2) are asserted by T14. (3) becomes "rebuild the world cell's rectangle from authoritative state, and rebuild after a save/load: byte-identical raster, identical path", asserted by T9 and T18. The engine is not needed.

### A7. Pathfinding specification

**Inputs.**
- The `NavRaster`, and the gate list with an `isOpen(gate)` function.
- The agent: `RadiusClass k` and `OpensDoors`.
- Exact start and goal points in integer mm.

**Walkability of lattice cell c for agent (k, opens):**

```
walkable(c) = solidFit[c] > k
              && ( gateFit[c] > k
                   || every gate g with dist²(center(c), g.Footprint) < (R[k] + m)² is passable )
passable(g) = isOpen(g) || (opens && g.Kind == Door)          // barriers are never opened by an agent
```

The slow branch runs only for cells within 650 mm of a gate; the gate list is under 20 in M7.

**Rasterisation.** Integer only; the largest squared value is 4·10¹² mm² for a 2 km region, well inside `long`.
- Box footprint: `dx = max(minX − cx, 0, cx − maxX)`, `dz` likewise, `d² = dx² + dz²`. Class k fits iff `d² ≥ (R[k] + m)²`, i.e. 202,500, 302,500 or 422,500 mm².
- Circle footprint (centre C, radius Rb): class k fits iff `|c − C|² ≥ (Rb + R[k] + m)²`.
- Bounds: class k fits iff every distance to a bound is ≥ `R[k] + m`, matching `Kinematics`' clamp (`Kinematics.cs:199-200`) plus the margin.
- `fit` = the number of classes that fit (0-3), minimised over all footprints and the bounds.
- **Solids** are the blockers of the current `WalkSpace` (authored plus placed) with `Kinematics.Blocks(b, 0, StandHeightMm)`.
  - Agents stand 1.8 m tall and never leave the ground, like every non-player mover today (`Kinematics.cs:76`).
  - A roof above 1.8 m drops out exactly as `Kinematics` drops it; the 0.8 m timber stays in.
- Terrain is ignored, as `Kinematics` ignores slope (`:159`).

**Snapping the ends.**
- An end cell that is not walkable (a body may stand closer than `r + m` to a wall) snaps to the walkable cell within 2 m (Chebyshev radius 8) with the minimal key `(d², j, i)`.
- If there is none, the outcome is `StartBlocked` or `GoalBlocked`.

**Window.**
- The search runs in the start/goal bounding box, inflated by 80 cells (20 m) and clipped to the raster.
- Over 512 cells (128 m) on either axis, the outcome is `TooFar`.
- M7 needs are far smaller: the companion's target is < 30 m away, and the anchor is < 60 m from the NPC's site (contract N2).

**Enclosure probes.**
- Before A*, flood breadth-first from the goal and then from the start, each limited to 2,048 cells (128 m²).
- A probe that runs out of frontier without reaching the other end, and without touching the window edge, gives `Enclosed`. A sealed hut, or the companion inside the closed lodge, is detected in about 1,500 steps.
- The edge is ≥ 80 cells from both ends, so open ground cannot look sealed. Anything else is inconclusive, and A* runs.

**A\*.**
- Neighbours in the fixed order E, N, W, S, NE, NW, SW, SE. A diagonal is allowed only if both orthogonals are walkable (no corner cutting).
- Costs 10 (orthogonal) and 14 (diagonal); no door penalty.
- Heuristic `h = 10·max(|Δi|,|Δj|) + 4·min(|Δi|,|Δj|)`: admissible and consistent.
- Open set: our own binary min-heap of `(F, H, GlobalIndex)`, compared lexicographically, with lazy deletion.
  - The key is a total order, so the pop sequence depends on the data alone.
  - .NET `PriorityQueue` is not used: its order between equal priorities is unspecified.
- A neighbour is relaxed only on a strictly smaller `g`; the parent is stored as a direction byte.
- Outcomes:
  - the goal is popped: `Found`;
  - the open set empties: `Unreachable`, or `NotInWindow` if the search touched the window edge;
  - 12,000 expansions: `BudgetExhausted`.

**Smoothing** (string pull, deterministic and greedy).

```
corners = []; anchor = exactStart; k = 0; last = path.Count - 1
loop:
  if Sight(anchor, exactGoal): corners.Add(exactGoal); stop
  j = k + 1
  while j < last && Sight(anchor, center(path[j+1])): j++
  corners.Add(center(path[j])); anchor = center(path[j]); k = j
  if corners.Count == MaxCorners (48): stop            // the follower replans when they run out
```

- `Sight(a, b)` is the companion's three-segment clear-view test, moved unchanged into the Domain (§3.4). It runs against:
  - the solids;
  - every gate the agent cannot pass (closed doors for a non-opener; standing barriers for everyone).
- A footprint whose bounding box does not meet the segment's bounding box inflated by the radius is skipped before `Crosses` runs.
- Corners are integer lattice centres plus the exact goal: integer mm, safe to persist.

**When to replan.** Every trigger is a function of saved state, the tick, and raster geometry.

| Mover | Trigger | Rule |
|---|---|---|
| NPC errand | Corners empty and not Arrived (new errand, or `RoutesInvalidated`) | Plan on the next tick the NPC acts |
| NPC errand | `StuckTicks == 40` (2 s without headway; headway means ≥ 30% of the expected walk step, as the companion measures it, and `StuckTicks` counts on while it waits) | Empty the corners, so it plans next tick |
| NPC errand | `StuckTicks == 200` (10 s) | Status `Blocked`; publish `ErrandStatusChanged` |
| NPC errand | Status `Blocked` or `Unreachable` | Replan when `StuckTicks % 100 == 0` (every 5 s); no teleport, ever |
| NPC errand | Corners run out before the goal (route truncated at 48) | Plan from where it stands |
| Companion | Character not in clear view and no trail mark in clear view (including an empty trail) | Plan, throttled to ticks where `(tick + Phase(npcId)) % 10 == 0`. `Phase` is the stable hash offset `Creatures.cs:460-464` already uses |
| Companion | `StuckTicks == SnagTicks / 2` (40) | Plan once per stuck episode |

**Unreachable behaviour.**
- NPC: status `Unreachable` (or `Blocked` for the budget, window, `TooFar` or snap outcomes). `RouteUnreachable` is published once per change of status. The NPC stands, turned towards the goal, and retries every 100 ticks.
- Companion: nothing changes. The Phase-1 behaviour runs (head for the oldest mark, then snag, then catch-up), so C16 and the M6 guarantees still hold.

**No floating-point nondeterminism.**
- Rasterisation, windows, probes and A* are integer.
- Smoothing and steering use the existing +, −, x, / and `Sqrt` predicates, the class `Kinematics` relies on (`Kinematics.cs:115-118`).
- Nav adds no `Sin`, `Cos` or `Atan2`. Facing reuses `CombatRules.FacingTowards` (`src/Domain/Combat/Combat.cs:238-243`) as the companion does. That is an existing dependency.

### A8. Local movement: three layers, and the pathfinder solves none of the centimetres

1. **Route (rare).** §A7 produces at most 48 corners, and runs only when a trigger fires.
2. **Steering (every tick).** A pure `RouteFollower.Next(body, corners, goal, reachedMm, sight)` returns a point:
   - drop leading corners within 500 mm (the companion keeps his own 800 mm, `Companions.cs:98`);
   - then head for the goal if it is in sight;
   - else the last corner in sight, dropping the ones before it;
   - else the first corner (`Lost`).

   The mover turns the point into a `MoveIntent` as `CompanionSystem.Step` does (`Companions.cs:558-568`). The NPC walks.
3. **Collision (every tick).** `Kinematics.Step`, unchanged.
   - Obstacles are the `CompanionSystem.Obstacles` set (`:571-581`): closed gates, creatures, the character, other NPCs.
   - Bodies are never in the raster. They are handled by sliding, then the stuck replan, then `Blocked`.
   - There is no crowd avoidance: M7 has two movers.

**Doors sit between steering and collision** (openers only).
- Before stepping, find a closed door that is crossed by the three-segment line to the chosen point and lies within `InteractReachMm` (1.6 m, `DistanceTo` from the body, as the player's reach is measured, `Systems.cs:260-263`).
- Dispatch `OpenDoor(key, by)` for the nearest such door. A tie goes to the smaller `(MinX, MinZ)`, never to an ID, because IDs differ in a replay.
- `ClosedDoors()` re-reads flags on every call (`Systems.cs:48`), so obstacles built after the dispatch let the NPC through on the same tick.

**The companion hook** goes in `CompanionSystem.Follow`'s goal selection (`Companions.cs:339-356`):

```csharp
// after dropping reached marks
bool seesPlayer = InClearView(body, player.XMm, player.ZMm);
int seen = seesPlayer ? -2 : FarthestVisibleMark(body, trail);            // -1 = none in view
bool lost = !seesPlayer && seen < 0;                                     // includes an empty trail
bool snagHalf = !seesPlayer && c.StuckTicks == tuning.SnagTicks / 2;
if ((snagHalf || (lost && Navigation.PlanTick(c.NpcId, tick)))
    && _navigation.Route(c.NpcId, CompanionAgent, BodyPoint(body), PlayerPoint(player), tick) is { Outcome: RouteOutcome.Found } r)
{
    trail = r.Corners.Select(p => new TrailMark(p.XMm, p.ZMm)).Take(tuning.TrailLength).ToImmutableArray();
    seen = FarthestVisibleMark(body, trail);                              // ≥ 0: the first corner is visible by construction
}
// then the existing code, keeping its guard: seesPlayer or an empty trail -> goal = player; else trail[max(0, seen)]
```

- The route replaces the trail. `Mark` keeps appending the character's steps after it (`Companions.cs:308-316`).
- Nothing new is persisted.
- A failed plan changes nothing, so the Phase-1 path to catch-up remains. The catch-up check (`:331-332`) still runs first.

### A9. Future seams, not built in M7

| Future need | Seam left now | Added later |
|---|---|---|
| NPC schedules (M9/M10) | The errand (goal, route, status) is what a schedule issues | A schedule issuing errands. Tier-B coarse movement (`Systems.cs:521-530`) uses a road graph, not this raster. Promotion to tier A uses `IsStandable` and the snap rule |
| Settlements, 2 km regions | Storage hidden behind `SolidFit(i, j)`; windowed search; rectangle rebuilds; lattice lines on seams | Per-world-cell tiles (tier-A resident); a per-tile blocker index; portal search over global lattice lines for routes > 128 m |
| Locks, NPCs closing doors | Gates have a kind and key; one `passable` predicate; one open command | `LockId` on gates; an access set on `NavAgent`; a close-behind step |
| Bridges | One storey: a bridge is a 2D corridor over a blocked area | A "terrain blocked" (water, void) source for the solid layer |
| Roads | Integer costs, admissible heuristic | A cost byte per lattice cell (≥ 1x) |
| Creature sizes | Three radius classes rasterised | Give creatures their class |
| Jump or crouch | Agents stand on the ground | A height class per agent, with a raster per height class |
| 45° pieces | Rasterisation needs only point-to-footprint squared distance | An integer point-to-oriented-box distance |

### A10. Acceptance tests

Test homes:
- **Domain tests** go in `tests/Domain.Tests/Spatial/NavigationTests.cs` and use synthetic `WalkSpace`s, with no content.
- **Runtime tests needing internals** go in `tests/World.Tests/` (World grants it `InternalsVisibleTo`, `src/World/World.csproj:19`).
- **Real-content tests** go in `tests/Application.Tests/NavigationTests.cs` through `Harness`.

"r" means class 0 (350 mm) unless stated.

| # | Name | Setup | Assertions |
|---|---|---|---|
| T1 | `AnOpenField_RoutesStraight` | 40 x 40 m, empty | `Found`; corners == [goal]; ≤ 150 expansions |
| T2 | `AWall_IsRoutedAroundItsNearerEnd` | Off-centre 20 m wall across the line | Every corner `IsClear` at r + m; consecutive corners in `Sight`; passes the nearer end |
| T3 | `ADoorway_IsTheWayIn` | 10 x 8 m room, 0.4 m walls, one 1.6 m doorway | One corner in the doorway corridor; crosses the wall line once |
| T4 | `ASealedRoom_IsEnclosed_AndFoundFast` | The same room, no doorway | `Enclosed`; 0 A* expansions; ≤ 2,048 probe steps |
| T5 | `AClosedDoor_StopsANonOpener_NotAnOpener` | T3 plus a closed door | Non-opener `Enclosed`; opener `Found`; once open, both `Found` |
| T6 | `ABroaderBody_DoesNotFitANarrowGap` | A 1.2 m gap and a 30 m detour | Class 0 takes the gap; class 2 takes the detour |
| T7 | `IncrementalRebuild_EqualsFullRebuild_ByteForByte` | 200 seeded place/remove steps of random boxes and circles | After every step `With(rect)` has the same `Digest()` as a full `Build`, also with the footprint order reversed |
| T8 | `TheSameQuery_GivesTheSameRoute` | A symmetric obstacle | 100 repeats and 3 blocker orders: identical corners and expansions |
| T9 | `TheSeam_IsNotARepresentationBoundary` | 200 m region; a 6 x 6 m hut straddling all four world cells at (100,100), its door on the z = 100 seam | The route crosses both seams and enters only by the door; the four world-cell rectangles rebuilt in all 24 orders equal the full build |
| T10 | `TheBudget_EndsASearch_Deterministically` | Goal inside a 40 x 40 m sealed ring | `BudgetExhausted` at exactly `MaxExpansions`, every run |
| T11 | `EndsInsideTheMargin_Snap` | Start 360 mm from a wall | Snapped by the `(d², j, i)` key; `Found` |
| T12 | `NotInWindow_IsReportedApart` | The only path leaves the window | `NotInWindow` |
| T13 | `TheHollow_EveryPlaceIsReachable` (Application) | Real content | Spawn, NPC sites, stations, containers and door approaches are standable and reachable by an opener (fold taken as lifted). A reachability lint against layout edits (M6 stranded two patrols, `docs/M6_STATUS.md:90`) |
| T14 | `TheAssignedNpc_WalksIntoAStructureStraddlingTheSeam_AndHome` (Application) | By building commands: a hut at x 97-103, z 125-131 m, door in the west wall, station east of x = 100; assign, tick, unassign | Arrives within 1,200 ticks; the body is `IsClear` every tick; displacement ≤ 160 mm per tick (twice the walk step; a teleport is metres); crosses x = 100 inside the hut; enters and leaves only by the doorway; opens the door if closed (`DoorToggled` by the NPC); ends within 300 mm of its site. Variant: the door straddles x = 100 |
| T15 | `AWallPlacedAcrossTheRoute_IsWalkedAround` (Application) | Mid-walk, place a wall across the route | `NavigationRebuilt` and `RoutesInvalidated` at that boundary; new corners; never overlaps the wall; arrives |
| T16 | `ARemovedWall_OpensTheShorterWay` (Application) | Dismantle the piece forcing a detour | The new route is shorter than the old remainder |
| T17 | `AnUnreachableAnchor_LeavesTheNpcWaiting` (World.Tests, synthetic barrier ring) | Send the NPC | `RouteUnreachable` once; moves ≤ 1 tick of travel; 1 search per 100 ticks; arrives once the barrier lifts |
| T18 | `MidRoute_SaveLoad_GoesOnTheSame` (Application) | NPC mid-doorway; companion carrying a route; save and load | Raster digest and `StateDigest` equal; equal again after 400 ticks; same arrival tick |
| T19 | `APlacementSession_Replays_ToTheSameStateAndRoutes` (Application) | From a save: place, assign, doors, dismantle; replay the log | `StateDump(replayable)` shows 0 differences; raster and corners equal (`StateDigest` equal if piece IDs are replay-stable) |
| T20 | `RepeatedRebuilds_AreIdentical` (Application) | Place and dismantle one piece 50 times | The raster digest returns to its first value every time; identical corners |
| T21 | `TheCompanion_FollowsThroughAPlayerBuiltDoorway` (Application) | C16 round the T14 hut; a wall across his trail; wait then follow from behind the hut | 0 `CompanionCaughtUp`; `maxHeld` < 300 ticks; ≥ 1 companion plan recorded; ends within 4 m |
| T22 | `NavigationQueries_StayWithinBudget` (Application, `Stopwatch` as `CreatureTests.cs:526-542` does) | Real content | Full build < 20 ms; one piece's rebuild < 2 ms; 100 routes: mean < 2 ms, max < 8 ms; expansions ≤ 12,000; numbers logged for RAZER |

**Must stay green, unchanged:**
- C16 (`CompanionTests.cs:119-160`);
- `HisState_RoundTripsThroughASave...` (`:285-319`);
- `FollowWaitFollow_...`;
- `LeftFarBehind_...`;
- all of `JumpAndCrouchTests`, `CreatureTests` and `FoldscarTests`;
- the 200-command replay (`DeterminismAndViewTests.cs:49-77`);
- `SixtyCreatures_TickWithinTheBudget`.

---

## 3. Also required

### 3.1 Route state and the save-then-continue digest test

`StateDigest` covers only the player record and the world delta (`Simulation.cs:357-364`). Anything that affects the next tick must therefore be saved, or be a pure function of saved state.

- **The raster is a pure function of saved state.**
  - It depends on content, saved piece records and saved flags.
  - It is built in the `Simulation` constructor, the single start point for both new games and loads (`Simulation.cs:100-145`). That runs after `FromSnapshot`, migrations and alias resolution: PERSISTENCE §7.4 step k in practice.
  - The constructor order after `RequireEverySliceOwned()` (`:139`) becomes `_effects.Seed`, **`_navigation.Build()`**, `_npcs.Populate()` (which now also restores errands), `_companions.Populate()`, `_creatures.Populate()`, `_tiers.Settle()`.
  - T18 asserts the raster digest is equal across a save and load.
- **Route corners are saved.** A route depends on where the mover stood when it planned, so replanning after a load could choose different corners and break save-then-continue.
  - The companion's corners *are* trail marks: already saved and hashed mark by mark (`src/World/PlayerState.cs:360-363`). **No new field.**
  - The NPC's corners go in the schema-14 record below.
- **Triggers read only saved state:** the tick, `StuckTicks`, the corners, the status, and geometry derived from saved state. No trigger reads a revision or a cache. `RoutesInvalidated` changes saved state at a logged boundary, so a replay repeats it.

**The NPC errand record the persistence and assignment designs must carry.** The shape is this design's; where it lives is theirs. The recommendation is the player section, following the companions precedent (schema 12, `PlayerState.cs:52-62`), because non-companion NPC bodies are not in the digest today.

```csharp
// src/World/PlayerState.cs (or wherever the assignment design puts worker state)
public enum ErrandStatus { Going, Arrived, Blocked, Unreachable }          // saved as "going" | "arrived" | "blocked" | "unreachable"

public sealed record NpcErrandRecord(
    string NpcId,                       // npc.* definition id: goes through the definition-ID pass like companions
    long XMm, long ZMm, int FacingMdeg, // the body (region mm)
    long GoalXMm, long GoalZMm, int GoalFacingMdeg,
    bool Homeward,                      // true: walking back to its site; the record ends on arrival
    ErrandStatus Status)
{
    public ImmutableArray<TrailMark> Corners { get; init; } = ImmutableArray<TrailMark>.Empty;   // ≤ 48
    public int StuckTicks { get; init; }
}
```

- **Digest:** every field, corners in order. Bump the player digest tag to `unnamed.player/v10`.
- **Decode validation:** at most 48 corners, all inside region bounds; a known status key; `StuckTicks ≥ 0`.
- **Retirement (I-7).** The record is deleted only when a `Homeward` errand's body equals the site's body exactly (x, z and facing).
  - The walker's last step uses deflection = remaining distance / travel, so it normally lands exactly.
  - If rounding leaves it a few mm off, the record stays, as `Arrived` and `Homeward`. That is sparse-delta doctrine (store what diverges), and it avoids snapping the NPC back to its site on load.

### 3.2 How doors enter traversability

- **Placed doors.**
  - The frame posts are solids, in the solid layer.
  - The leaf is a gate, in the gate layer, rasterised on placement and removal.
  - Open or closed is building state, read at query time.
- **Authored doors and barriers** are gates too. Their state is the existing flags.
- **Planning.**
  - Non-openers see a closed door as a wall.
  - Openers plan through doors.
  - Nobody plans through a standing barrier.
  - **No rebuild on toggle.**
- **Following.** Openers exclude closed doors from `Sight` and open the door on reach (§A8). Non-openers see it as a wall (existing behaviour).
- **Opening.** A new internal command, `OpenDoor(string DoorKey, EntityId By)`, handled by `InteractionSystem` (which owns no state):
  - A `door.*` key dispatches `SetWorldFlag(cell, flag, 1)` exactly as the player's interaction does, and publishes `DoorToggled(By, key, true, tick)` (`Systems.cs:269-271`; the event is at `src/World/Runtime/Events.cs:25`), so presentation's door view follows as it does today.
  - Any other key is forwarded to building's placed-door command, which publishes the equivalent event.
  - NPCs never close doors in M7.
- **Known edge case, not new.** The character can close a door on an NPC standing in the doorway, because the close refusal checks only the character's own body (`Systems.cs:266-268`). `Kinematics` then pushes the NPC out by the nearest face. Building may extend the refusal to every body for placed doors. Nav tolerates either.

### 3.3 Per-agent capability model

```csharp
public readonly record struct NavAgent(int RadiusClass, bool OpensDoors);
// RadiusClass = index of the smallest configured class radius ≥ the body's radius
```

| Agent | Radius | Class | OpensDoors | Height |
|---|---|---|---|---|
| Assigned NPC | 350 (`base_speeds.yaml:10`) | 0 | **true** | standing 1.8 m |
| Companion | 350 | 0 | false (Phase-1 behaviour kept) | standing 1.8 m |
| Placement validation ("can the character still get everywhere") | 350 | 0 | true | — |
| Creatures (not in M7) | 350-550 | 0-2 | false | standing 1.8 m (as `Kinematics` treats them today) |

Height is one fixed value in M7: every agent stands, grounds itself and never jumps. Capabilities not modelled: jump, crouch, swim, locks. The seam for each is in §A9.

### 3.4 Where the code lives

**Domain: pure functions and immutable data, no World types.**

`src/Domain/Spatial/Navigation.cs` (~500 lines):

```csharp
public sealed record NavConfig(long LatticeMm, long MarginMm, ImmutableArray<long> ClassRadiiMm, long WindowMarginMm, long MaxWindowMm,
    int MaxExpansions, int EnclosureProbeCells, int MaxCorners, long CornerReachedMm, long ArriveMm, long StandHeightMm)
{
    public string? Problem();   // radii ascending and > 0; 100_000 % LatticeMm == 0; LatticeMm is even (integer centres);
                                // margin ≥ 0; MaxWindowMm ≥ 2 * WindowMarginMm; budgets > 0; 2 ≤ MaxCorners
}

public readonly record struct NavPoint(long XMm, long ZMm);
public readonly record struct NavAgent(int RadiusClass, bool OpensDoors);
public enum GateKind { Door, Barrier }
public sealed record NavGate(string Key, GateKind Kind, Blocker Footprint);
public readonly record struct LatticeRect(int I0, int J0, int I1, int J1);        // half-open

public sealed class NavRaster                                                    // immutable after construction
{
    public NavConfig Config { get; }
    public long OriginXMm { get; }  public long OriginZMm { get; }
    public int Width { get; }       public int Height { get; }
    public static NavRaster Build(NavConfig config, WalkSpace space, IReadOnlyList<Blocker> solids, IReadOnlyList<NavGate> gates);
    public NavRaster With(LatticeRect dirty, WalkSpace space, IReadOnlyList<Blocker> solids, IReadOnlyList<NavGate> gates);
    public LatticeRect Around(IEnumerable<Blocker> changed);                     // inflated by max radius + margin
    public int SolidFit(int i, int j);  public int GateFit(int i, int j);
    public (int I, int J) CellOf(long xMm, long zMm);  public NavPoint CenterOf(int i, int j);
    public string Digest();                                                      // CanonicalHasher over both layers
}

public enum RouteOutcome { Found, Enclosed, Unreachable, NotInWindow, BudgetExhausted, TooFar, StartBlocked, GoalBlocked }
public sealed record RouteResult(RouteOutcome Outcome, ImmutableArray<NavPoint> Corners, int Expansions, int ProbeSteps, int SightTests);

public static class NavSearch
{
    public static RouteResult Route(NavRaster raster, IReadOnlyList<Blocker> solids, IReadOnlyList<NavGate> gates,
        Func<NavGate, bool> isOpen, NavAgent agent, NavPoint from, NavPoint to);
    public static bool IsStandable(NavRaster raster, IReadOnlyList<NavGate> gates, Func<NavGate, bool> isOpen, NavAgent agent, NavPoint at);
    public static bool EnclosedPocket(NavRaster raster, IReadOnlyList<NavGate> gates, Func<NavGate, bool> isOpen, NavAgent agent,
        NavPoint seed, int limitCells);          // placement validation
}

public static class Sight                           // moved verbatim from Companions.cs:584-598
{
    public static bool InClearView(NavPoint from, NavPoint to, long radiusMm, IEnumerable<Blocker> walls);
}

public static class RouteFollower
{
    public static (NavPoint Toward, int Drop, bool Lost) Next(NavPoint body, ImmutableArray<NavPoint> corners, NavPoint goal,
        long reachedMm, Func<NavPoint, NavPoint, bool> sight);
}
```

- The layers are `byte[]` exposed as `ImmutableArray<byte>`. Verify `ImmutableCollectionsMarshal.AsImmutableArray` in the installed .NET 8 SDK; otherwise copy.
- Search scratch is allocated per call. Nothing is static.
- `CompanionSystem.InClearView` and `Walled` delegate to `Sight.InClearView`, with the same behaviour.

**World runtime.**

- `src/World/Runtime/Navigation.cs` (~200 lines):
  - `NavigationSystem` owns `StateSlice.Navigation`, documented *"Transient: derived from the region layout and placed pieces, rebuilt at start and on every piece change; never saved"*, as `CellTiers` is (`RuntimeState.cs:36`).
  - It has no `Tick`.
  - It handles `RebuildNavigation(ImmutableArray<Blocker> Changed)` and exposes `Route(string moverKey, NavAgent, NavPoint, NavPoint, long tick)` (wraps `NavSearch`, counts, publishes `RoutePlanned`), `Tentative(...)` for validation, and `Stats`.
  - It is constructed before `_npcs` and `_companions` in the composition list (`Simulation.cs:116-137`) and passed to both constructors, the way `QuestDebugger` receives other systems (`:137`).
- `RuntimeState`: `public NavRaster? Navigation { get; private set; }` and `SetNavigation(SliceOwner, NavRaster)`, gated.
- `src/World/Runtime/Social.cs`, `NpcSystem`:
  - It now also owns `StateSlice.NpcErrands` (saved).
  - It handles `SendNpc(NpcId, goalX, goalZ, goalFacing)`, `RecallNpc(NpcId)` and `RoutesInvalidated(LatticeRect)`.
  - `Tick` walks errand NPCs in tier-A cells only, as creatures and companions act (`Creatures.cs:264-265`, `Companions.cs:263-264`). It holds them while they are in conversation, and turns arrived NPCs to the goal facing at 18,000 mdeg per tick (`Social.cs:107`).
  - Non-errand NPCs behave exactly as today.
- `src/World/Runtime/Systems.cs`: `InteractionSystem.Handle(OpenDoor)`; `SystemContext.Solids()`, `Gates()`, `IsOpen(NavGate)`.
- `Simulation`:
  - dispatch arms for `RebuildNavigation`, `RoutesInvalidated`, `SendNpc`, `RecallNpc` and `OpenDoor` (`Simulation.cs:369-404`);
  - `_navigation.Build()` in the constructor;
  - a get-only `public NavigationView Navigation` for the debug overlay (raster, gates with state, errand routes, companion trails, `NavStats`). It is a property, so the allow-list in `tests/Architecture.Tests/ArchitectureTests.cs:111-131` needs no change.
- Events, public and past tense, views only:
  - `NavigationRebuilt(int I0, int J0, int I1, int J1, int Cells, long Tick)`;
  - `RoutePlanned(string MoverKey, string Outcome, int Corners, int Expansions, long Tick)`;
  - `ErrandStatusChanged(string NpcId, string Status, long Tick)`.

**Content.**

`content/config/navigation.yaml`:

```yaml
id: config.navigation
kind: config
schema: 1
display_key: config.navigation.name
tags: [config]
notes: Domain navigation (M7; owner ruling 1). A 250 mm clearance lattice over the region, derived from what Kinematics collides with; never saved.
lattice_m: 0.25            # divides the 100 m cell exactly; a 1.6 m doorway keeps 2-3 lattice centres for every body in content
margin_m: 0.10             # planned points keep this much more than the body radius from anything solid
radius_classes_m: [0.35, 0.45, 0.55]   # people and the husk; wolf, hound, armour; spider, boar
window_margin_m: 20
max_window_m: 128
max_expansions: 12000
enclosure_probe_cells: 2048
max_corners: 48
corner_reached_m: 0.5
arrive_m: 0.3
```

- `NavigationContent.Build` returns `NavConfig`. `stand_height_m` is read from `config.base_speeds`.
- `LoadAll_Loads_Yaml_Files` asserts the exact ID list (`tests/Content.Tests/ValidationTests.cs:508`) and gains `config.navigation`.
- The content hash changes, which is Class A content: no schema bump by itself.

### 3.5 Instrumentation

`NavStats` is an immutable snapshot record, read through `Simulation.Navigation.Stats`. It is transient, excluded from every digest, and never read by a decision. Timings are measured only in tests, because `Stopwatch` is absent from `src/World` by convention.

| Counter | Meaning |
|---|---|
| `FullBuilds`, `RectRebuilds`, `CellsRecomputed`, `LastRectCells` | Rebuild work |
| `Searches`, `SearchesByOutcome[8]` | Route calls by outcome |
| `ExpansionsTotal`, `ExpansionsMax`, `ProbeStepsTotal` | Search effort; `ExpansionsMax` is the budget-risk indicator |
| `SightTestsTotal`, `CornersTotal` | Smoothing effort |
| `Invalidations`, `RoutesCleared` | §A5 step e |
| `CompanionPlansLost`, `CompanionPlansSnag`, `CompanionPlansFailed` | How often the fallback runs, and fails |
| `NpcReplansStuck`, `NpcRetries`, `NpcDoorOpens` | Errand health |

The debug overlay is toggled from the debug menu or a key, never a radial (owner ruling 5). It draws:
- lattice cells within 20 m of the character that are not walkable for class 0 (a MultiMesh of flat quads);
- gates coloured by state;
- the errand route polyline;
- the companion's trail;
- the counters as text.

### 3.6 Memory and CPU, with the arithmetic

**Ashen Hollow now.**
- **Lattice:** 800 x 800 = 640,000 cells.
- **Raster:** 2 B x 640,000 = **1.22 MiB**, plus a second copy briefly during a rebuild.
- **Search scratch** (per call, 5 B per window cell for `g` and the parent):
  - a typical 60 m window: 57,600 cells, **288 KB**;
  - the largest window: 262,144 cells, **1.31 MB**;
  - the heap: ≤ 96k lazy entries x 12 B, **≤ 1.15 MB**.
- **Full build (estimate):** the 1.28 MB fill, plus ≈ 26,000 squared-distance evaluations:

  | Footprints | Evaluations |
  |---|---|
  | 32 trees, π(1.05 m)² / 0.0625 m² ≈ 55 each | 1,770 |
  | 13 rocks, ≈ 232 each | 3,000 |
  | 3 den rocks, ≈ 5,340 each | 16,000 |
  | 16 other boxes, ≈ 300 each | 4,800 |
  | Gates | ~800 |

  **< 3 ms.**
- **One 3 m wall piece:** 119 cells x ≤ 5 footprints, plus the copy: **< 0.5 ms**.
- **Search** (estimate: 0.25-0.5 µs per expansion):

  | Case | Expansions | Time |
  |---|---|---|
  | 30 m in the open | ~150 | 0.05 ms |
  | Round a 16 x 8 m building | 2-4k | 0.5-2 ms |
  | The cap | 12,000 | **3-6 ms**, once |
  | Probes | ≤ 4,096 steps | ≤ 1 ms |

  String pulling adds ≈ 15k `Crosses`, **≈ 0.1 ms**.
- **Frequency:** a plan per errand start, per structure edit and per stuck episode; for the companion, only when he is lost, throttled to ≤ 2 per second.
- **Steady state: about 0 ms per tick.** The 60-creature 0.43 ms (`docs/M3D_STATUS.md:53`) is untouched.

**A 2 km region later.**
- The full lattice would be 64M cells x 2 B = **128 MB**: not acceptable.
- Tiles per world cell are 320 KB each. With only the tier-A block resident (≤ 5 x 5 cells within 150 m), that is **8 MB**.
- A forested tile builds in **< 1 ms (estimate)**.
- Windowed searches do not grow with the region. Routes over 128 m need §A9's road graph.

---

## 4. What navigation needs from the other M7 parts (contracts)

**Building.**
- **B1.** Each placed piece exposes footprints as `BoxBlocker`s (region mm, with height and clearance), each with a traversal class:
  - `Solid`;
  - `Door`: the leaf is a gate, and the frame posts are separate solids;
  - `None`: roofs and floor pads, in neither `Kinematics` nor nav.

  At 90° rotation every footprint is an axis-aligned box.
- **B2.** Placed solids must be in exactly the blocker set `Kinematics.Step` uses, for both the simulation and prediction.
  - Recommended: a runtime `WalkSpace` (`Layout.Space with { Blockers = authored ++ placed }`), reached through `SystemContext.Space()` and a get-only `Simulation.Space`.
  - 13 collision and sight call sites in `src/World/Runtime` change (grep `Layout.Space`; its 4 terrain-only reads can stay), plus `PlayerController.cs:127`.
  - The three `Walled` implementations (`Creatures.cs:893-895`, `Companions.cs:597-598`, `Combat.cs:569-570`) must include placed solids and placed closed doors.
- **B3.** Placed blockers are ordered by a key that is equal in a replay, because `Kinematics.Resolve` is order-dependent (`Kinematics.cs:189-198`). Use `(placed tick, sequence)` or footprint coordinates, never a wall-clock ULID. Nav itself is order-independent.
- **B4.** After every add, remove or destroy, building dispatches `RebuildNavigation(changed)`. For validation it uses `NavigationSystem.Tentative(add, remove)`, whose result is never stored.
- **B5.** The doorway opening is ≥ 1.40 m (1.6 m recommended). The work anchor is `IsStandable`: ≥ 450 mm clear of every solid, the station included.
- **B6.** Navigability checks nav offers:
  - **V1** (building's own): no footprint overlaps a body.
  - **V2**: flood with `EnclosedPocket(opener, limit 16,384 cells = 1,024 m²)` from the walkable cells on the one-cell ring outside the candidate's rectangle. A flood that runs out below the limit rejects the placement: "this would close off a space with no way in".
    - Any newly sealed region must border the new piece, so the ring is sufficient.
    - Cost: ≤ 2-3 floods, **≤ 10 ms (estimate)**, per placement command. The ghost preview runs V2 only on snap change.
  - **V3**: a door's two approach points (the leaf centre ± (half the wall thickness + 700 mm)) are standable and meet through the leaf.
  - **V4**, at assignment: the anchor routes `Found` from the NPC's site.

**NPC assignment.**
- **N1.** `SendNpc` on assign; `RecallNpc` on unassign or when the station is destroyed.
- **N2.** The anchor is ≤ 60 m from the NPC's site.
- **N3.** A companion is never assigned.
- **N4.** The §3.1 errand record is persisted and in the digest.
- **N5.** A trader's wares stay at its site (`Systems.cs:61-64`); that is the assignment design's call.

**Persistence.** The errand record goes in schema 14. The raster is never saved. `_navigation.Build()` runs after `FromSnapshot` and before `Populate`.

**Presentation.**
- Prediction uses `Simulation.Space`.
- The moving NPC is interpolated between ticks, as creatures are (`src/Presentation/Greybox/CreaturesView.cs:282`).
- The debug overlay reads `Simulation.Navigation`.
- There is no Godot navigation anywhere.

**Factions.** None. Nav reads no standing. Territory gating, if built, is an explicit access query, never a nav cost.

---

## 5. Documents M7's first slice reconciles (navigation part of rulings 1 and 2)

- **DECISIONS.** Record owner ruling 1 as a decision: domain-side, deterministic, headless, replayable; derived from authoritative state; seam-independent; Godot never has authority. D-01's revisit trigger 2 (`docs/DECISIONS.md:47`) becomes presentation-only.
- **WORLD_ARCHITECTURE.**
  - `:137`: navmesh "Baked" becomes "navigation raster, derived, not persisted".
  - `:297`: "off navmesh" becomes "not standable".
  - `:414`: "per cell, debounced" becomes "the changed rectangle, at the command boundary".
  - `:435`: remove navigation from the §11 presentation table.
  - `:469` (RK-A2): solved by construction; T9 proves it.
- **RISK_REGISTER.**
  - RK-14 (`:280-296`): "needs the engine" becomes "headless T9 and T14, plus the playthrough recording".
  - RK-06 (`:154-158`): one rectangle rebuild per edit.
- **PERSISTENCE** `:83-84`: the raster is rebuilt in the `Simulation` constructor; routes that decide the next tick are saved with their movers.
- **SYSTEMS.**
  - S-25 "Transient: path following state" and `:443`: saved where it decides the next tick.
  - S-32 "navmesh dirty regions" becomes "navigation dirty rectangle".
- **ROADMAP M7** (`:281-285`): "navmesh" becomes "domain navigation raster". Record that the entry criterion is met inside M7.
- **Ruling 2.** Nav agents stand on the ground and use `Blocks` at lift 0, so nav cannot represent a second level. Say so in one line in WORLD_ARCHITECTURE §10.

---

## 6. Deliberately not built in M7

Each of these has value that appears only after M7:
- tiled or resident storage;
- per-tile blocker indices;
- hierarchical or portal search;
- time-sliced searches (they would force the search state into the save);
- connected-component labels (a global relabel on every edit; the bounded probes answer M7's questions);
- cost layers (roads, terrain);
- crowd or RVO avoidance;
- creature pathing, including return-home;
- companions opening doors;
- NPCs closing doors;
- jump or crouch links;
- flow fields;
- tier-B coarse routes;
- cross-region routing;
- a public `Simulation` route-query method (tests call the Domain function; building queries through its own validation surface).

---

## 7. Implementation order: each slice is green and playable

1. **Domain.** `Navigation.cs`, with T1-T12. No runtime change; the game is unaffected.
2. **Runtime raster.**
   - `NavigationSystem`, the slice, and `Build()` in the constructor, over authored content only.
   - The `config.navigation` content.
   - The debug overlay.
   - T13, and the raster part of T22.
3. **Building hooks.** Depends on the building slice.
   - `SystemContext.Space()`, `Solids()` and `Gates()`.
   - `RebuildNavigation` on place and remove.
   - `Tentative` for V2-V4.
   - T7-style equality on real content.
4. **NPC errands.**
   - `SendNpc`, `RecallNpc`, the walker, `OpenDoor` and `RoutesInvalidated`.
   - The persisted record in schema 14, landed by the persistence slice.
   - T14-T17.
5. **Companion fallback hook.** T21, with C16 and every existing companion test re-run.
6. **Acceptance.** T18-T20; the T22 perf pass on ASTRAL, and RAZER when that window opens; the playthrough M7 segment recording the seam walk.
7. **Docs.** §5.

---

## 8. Open issues

1. **Sight is not a true capsule test.** The three-segment clear view (`Companions.cs:584-598`) can miss a footprint narrower than the body radius lying between the offset lines. `Kinematics` then slides, and the stuck path replans. This is accepted from Phase 1. Replacing it would change the companion's M6 behaviour.
2. **Placed blockers in `Kinematics.Resolve` are order-dependent** (B3). If building orders them by random ULID, collision (not nav) can differ in a replay.
3. **Cross-machine determinism** of `FacingTowards` (`Atan2`) and of existing creature and companion trigonometry is unproven. Nav adds none. Route corners and the raster are integer.
4. **Budget spike.** A budget-exhausted search is estimated at 3-6 ms in one tick, above the 4 ms world budget. It is rare (a region split into two large parts), and repeats at most every 5 s per NPC. T22 measures it. If RAZER exceeds budget, lower `max_expansions` to 8,000: 500 m² of explored area still covers every M7 route.
5. **Non-companion NPC bodies are not in `StateDigest` today** (`Simulation.cs:357-364`). The errand record fixes this for the assigned NPC only. That is correct under "store what diverges", but the persistence design must confirm where it lives.
6. **The companion never opens doors.** A character who shuts a player-built door behind himself still triggers the Phase-1 catch-up. That is honest, but it is a "pathing intervention" in ROADMAP M6's sense (`docs/ROADMAP.md:273`). The one owner question below asks whether M7 should change it.
7. **An authored-layout edit after a save** can leave saved corners behind a new wall. It self-heals (the mover is lost and replans), so no migration is needed.
8. **The 60 m anchor limit (N2) and the 128 m window** are M7 limits. M9 revisits them together with tiling.

**Owner question (low priority; the default holds if unanswered).** May companions open doors in M7, authored and player-built? Default: **no**. They keep Phase-1 behaviour: they path through open doorways, and when the character shuts a door behind himself, catch-up still recovers them.
