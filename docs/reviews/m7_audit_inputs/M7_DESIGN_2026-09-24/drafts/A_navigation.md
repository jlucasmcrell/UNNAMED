# M7 Part A: Navigation v1 (authoritative synthesis)

Status: the navigation judge's synthesis for the M7 design team, 2026-09-24. It supersedes the three panel candidates (`drafts/panel/nav_minimal.md`, `nav_scale.md`, `nav_codefit.md`). It is design and implementation planning only. It follows `drafts/00_SCOPE_RULINGS.md` and raises no challenge to a scope ruling.

Base: origin/main `e10d2c4`. Every `path:line` is repo-relative and was re-read in the read-only snapshot for this document. Numbers marked **[model]** come from the judge's scratch model: a Python re-implementation of exactly the rules in this document, run over the real `content/regions/ashen_hollow.yaml` (Appendix C). They are counts (nodes, expansions, corners), not timings. Timings are estimates until test N-A10 measures them.

Units: millimetres (`long`), millidegrees, ticks (20 Hz, 50 ms). Terms: a **node** is a navigation lattice point; a **tile** is the nodes of one 100 m world cell; a **cell** is the persistence/tier cell. "Person" is the navigation class of every M7 mover (radius 350 mm).

Starting point: `nav_codefit` scored highest (Appendix A). This design keeps its integer geometry, soundness margin, slice ownership and persisted route. It grafts in `nav_minimal`'s rectangle rebuild, enclosure probes, sealed-pocket placement rule and windowed search, and `nav_scale`'s tile stamps, local (residency-independent) placement checks and 2 km plan. Section 17 lists every disagreement and the choice made.

---

## 0. The decisions on one page

1. **Consumers.** Exactly two movers: the one assigned settlement NPC (scope ruling item 5), who walks to a work anchor and home again, and the companion (Tavar), only when his Phase-1 trail cannot give him a goal. Placement validation queries navigation. Creatures, the player and static NPCs do not use it.
2. **Representation.** One global integer lattice at **250 mm**, stored as **one tile per 100 m cell** (400 x 400 nodes). Each node holds two bytes: `SolidFit` and `ClosedFit`, each the number of radius classes (350 / 450 / 550 mm) whose planning circle fits there. Doors and barriers are **gates**, evaluated at query time. It is derived from exactly the blockers `Kinematics.Step` collides with.
3. **Soundness.** Planning radius = body radius + **50 mm**. That margin is derived (section 3), so every lattice edge a route uses is provably clear for the real body. String pulling and route validation use an **exact integer swept-circle test**, the same predicate as `Separation(...) is null`.
4. **Search.** Enclosure probes (2 x 2,048 nodes), then windowed A* (8-connected, no corner cutting, costs 1000/1414, octile heuristic, total-order tie-break `(f, h, index)`, cap **16,000 expansions**, window = endpoints + 20 m, at most 128 m). Output: at most **32** integer-mm corners.
5. **Updates.** At one command boundary: piece committed, then `RebuildNavigation(changed footprints)`, then the nodes within 600 mm of the change are restamped in each touched tile (never a whole cell, never a neighbour by default), then each touched tile's input stamp changes. A mover sees the stamp change on its next tick and replans. Door toggles and barrier lifts rebuild nothing.
6. **Seams.** They do not exist in the data. 100,000 / 250 = 400, so seams fall between node indices, and a node's value is a pure function of the geometry within 600 mm. A tile built alone equals the same tile from a full build.
7. **Saving.** The grid, stamps, scratch and counters are never saved. They are rebuilt in the `Simulation` constructor. A mover's **route is saved with its body** (the companion in `CompanionRecord`, the NPC in its assignment record), because a route depends on where and when it was planned. This follows the M6 trail and stuck-counter precedent.
8. **Local movement is unchanged.** Navigation returns a target point. The mover builds a `MoveIntent` exactly as `CompanionSystem.Step` does, and `Kinematics.Step` does all collision. Navigation never writes a body.
9. **Doors.** M7 agents open unlocked doors (authored and placed) within reach, and leave them open. They plan through doors regardless of their state.
10. **Last resorts.** The companion keeps the Phase-1 snag and distance catch-up as a safety net. The assigned NPC never teleports.
11. **Code.** Pure functions in `src/Domain/Spatial/Nav*.cs` (namespace `UNNAMED.Domain.Spatial`, integer-only, enforced by a source-scan test). A `NavigationSystem` in `src/World/Runtime/Navigation.cs` owns a new transient `StateSlice.Navigation`. Tuning is content: `content/config/navigation.yaml`.
12. **Cost in Ashen Hollow.** 1.22 MiB of tiles plus 3.8 MiB of reusable scratch. The full build is 20,756 footprint evaluations [model]. Real M7 routes expand 121 to 9,709 nodes [model]. The steady state is about 20 µs per mover per tick.

---

## 1. Who needs pathfinding in M7 (brief item 1)

Today no mover paths. Everything steers straight and slides (`docs/M3D_STATUS.md:78`). The companion follows a persisted breadcrumb trail (`src/World/Runtime/Companions.cs:308-316`, `:325-361`), and teleports after 80 ticks without headway or beyond 30 m (`:331-332`, `:367-393`; `content/config/companion.yaml:10-11`).

| Consumer | M7? | Why, from the M7 text and the code | Goal |
|---|---|---|---|
| **Assigned settlement NPC** (one; scope ruling item 5) | **Yes, primary** | ROADMAP M7 exit: "assign an NPC to work in it, verify the NPC navigates in, through, and around it — including a structure straddling a cell boundary" (`docs/ROADMAP.md:284`). A teleport cannot satisfy "navigates", and there is no trail to follow. NPCs never move today (`src/World/Runtime/Social.cs:149-164`) | The station's work anchor; its `NpcSite` when unassigned |
| **Companion (Tavar)** | **Yes, as a fallback mode** | ROADMAP M7 entry "NPCs and companions path reliably" (`:281`); scope "used by the companion and the assigned NPC". The trail works (C16 passes with no catch-up, `tests/Application.Tests/CompanionTests.cs:119-160`), but it fails in three cases: no mark in clear view (he heads for the oldest mark, `Companions.cs:351-353`); wait-then-follow clears the trail (`:186-187`); and a wall placed across marks | The character's position |
| **Placement validation** | Yes (queries, not a mover) | "placement validation that rejects un-navigable or overlapping configurations" (`docs/ROADMAP.md:283`) | Section 5.4 |
| Debug overlay, tests, playthrough recording | Read-only | Scope: "debug views sufficient to prove" | — |
| Player | Never | Input-driven | — |
| Static NPCs (Renn, Kera, Sel, Tavar before recruitment) | No | They stand at their sites and only turn (`Social.cs:149-164`) | — |
| Creatures: chase, search, wander, patrol, flee | **No** | See below | — |
| Creature charge and lunge | **Never** | The boar's stun is "ran into something solid" (`src/World/Runtime/Creatures.cs:589-594`), and a test depends on it | — |

**Creature return-home ("optional if cheap") is declined.** It is not cheap:
- a pathing creature must persist its route, which reopens `CreatureDto` (frozen since schema 8) and the creature digest;
- it changes the generated behaviour matrix (`docs/M3D_BEHAVIOUR_MATRIX.md`) and a dozen creature tests;
- "searched for, not pathed to" (`docs/M3D_STATUS.md:78`) and "never to where it cannot know the target is" (`Creatures.cs:466-470`) are deliberate stealth rules.

The seam is kept: creatures already map to classes (radius 350-550 mm), and the follower is agent-generic (section 9).

---

## 2. Representation (brief item 2)

The candidates below are compared against Otherreach's real constraints:
- integer-mm state, and a digest-tested replay and save-then-continue;
- axis-aligned boxes and circles only (`src/Domain/Spatial/Blockers.cs:41`, `:95`);
- 1.6 m doorways in 0.4 m walls; bodies of 350-550 mm;
- 100 m cells, a 200 m region now, 2 km regions in M9;
- a 4 ms world-system budget (`docs/WORLD_ARCHITECTURE.md:449`);
- a companion who already follows "points in clear view".

| Criterion | **A. Tiled integer clearance-class lattice (chosen)** | B1. Waypoint graph from samples | B2. Visibility graph over inflated corners | C. Current steering + local avoidance | D. Domain navmesh (constrained triangulation) |
|---|---|---|---|---|---|
| Determinism | Integer bytes; `min` stamping is order-free; exact integer segment test | Edges need exact tests | Circle tangents need trig or polygonised circles | Deterministic; bug-following state must be persisted | Needs exact predicates the repo lacks; incremental edits are order-sensitive |
| Cell seams | Not a case: global indices, seams between nodes | Long edges cross cells | Long edges cross cells | None | RK-14's stitching, or global retriangulation per edit |
| Dynamic building | Restamp nodes within 600 mm of the piece (about 100-300) | Re-sample and re-link; doors need injected nodes | Retest edges near the piece, per radius | Nothing to update or guarantee | Local retriangulation, high risk |
| 1.6 m doors, 3 radii | One byte serves all classes (4/2/2 lanes) | A 2 m sample misses doors | Exact, but one graph per radius | Only sliding | One mesh per radius |
| Answers "unreachable" / "does this seal a room"? | Yes: bounded probes and floods | Yes | Yes | **No** | Yes |
| Memory: hollow / 2 km | 1.22 MiB / ≤ 21 resident tiles, 6.7 MB | Small / grows with density | Small / O(corners²) | 0 / 0 | Small / small |
| Headless tests | Byte-hashable grid | Harder | Harder | Easy, weak | Hard |
| Schedules, settlements | Portal graph from tile borders is an addition | Scales poorly | Scales poorly | Unusable | Good paths, same portal problem |
| Risk | Low: plain integer code | Medium | Medium-high | None, but fails the exit | High (the M3 spike's Godot bake overflowed Recast region IDs, `docs/M3_STATUS.md:72`) |

Flow fields (one field per goal, 1.3 MB each, dead on every edit) were also considered and rejected: the companion's goal moves every tick.

**Recommendation: A**, with C kept as the layer beneath it (the companion trail, `Kinematics` sliding, snag and catch-up).

- A is the only option that is at once integer-exact, seam-free by construction, updated locally and provably equal to a full rebuild, one structure for every radius, and checkable against `Kinematics` with the same predicate.
- C is rejected as the global layer because it cannot answer "unreachable" or "would this seal a room". Placement validation and RK-14's fail conditions (`docs/RISK_REGISTER.md:292`) need that answer. It also cannot bring an NPC in through a door on the far side of a hut.
- D is the upgrade path if routes ever need sub-lattice optimality. The public surface (`NavSearch.Plan`, `NavFollower.Next`) would not change.

---

## 3. Resolution (brief item 3): derived, not picked

Symbols: `s` node spacing; `r` class radius; `m` margin; `Rp = r + m` the planning radius. A node is walkable for a class when a circle of radius `Rp` at its centre overlaps no footprint (touching is clear, matching `Blockers.cs:52`, `:102`).

**C1. Every lattice edge must be walkable by the real body (soundness).**
- For two walkable nodes `L` apart and any point `q` of a convex footprint: `|A−q|, |B−q| ≥ Rp` implies every point of AB is at least `sqrt(Rp² − L²/4)` from `q`. Distance to a convex set is attained at one point, so the bound carries over to boxes and circles.
- The diagonal edge, `L = s·√2`, is the binding case: **`Rp² ≥ r² + s²/2`**.

**C2. The doorways that exist must stay open for the classes that use them.**
- An opening of clear width `W` leaves a band `W − 2Rp` wide for node centres, and a band of width `w` holds at least `floor(w/s)` centres.
- The smallest opening guaranteed passable is **`W_min = 2Rp + s`**.

**C3. The lattice must tile the cell exactly.** `s` divides 100,000 mm, so seams fall between nodes. It should also divide the 5 m terrain spacing and a 0.5 m building snap. `s` is even, so centres (`i·s + s/2`) are whole millimetres.

**C4. Movement scale.** Runs are 160 mm a tick (80 walking, `content/config/base_speeds.yaml:7-9`). Movers re-steer every tick and corners are only waypoints, so `s` above one tick's travel costs no smoothness.

| s | minimum m (C1, r = 350) | Person lanes / r450 / r550 through 1.6 m | Person `W_min` | Nodes per tile | Bytes per tile (2 layers) | Divides 100 m / 5 m / 0.5 m |
|---|---|---|---|---|---|---|
| 500 | 162 | 1 / 0 / 0 (m = 200) | 1,600 (zero slack) | 40,000 | 80 KB | yes / yes / yes |
| **250** | **42.1** | **3 / 2 / 1 guaranteed; 4 / 2 / 2 on the real doors** | **1,050** | **160,000** | **320 KB** | **yes / yes / yes** |
| 200 | 27.4 | 4 / 3 / 2 | 1,000 | 250,000 | 500 KB | yes / yes / **no** |
| 100 | 7.1 | 8 / ... | 900 | 1,000,000 | 2 MB | yes / yes / yes |

**Choice: `s = 250 mm`, `m = 50 mm`, classes `person 350`, `medium 450`, `large 550`.**
- It is the coarsest spacing that keeps all three classes through the existing 1.6 m doorways (500 mm leaves the boar class zero lanes).
- It divides the cell (400), the terrain spacing (20) and a 0.5 m snap (2).
- It has 1.56x fewer nodes than 200 mm, and A* cost scales with node count.
- C1 checks: person `400² = 160,000 ≥ 350² + 31,250 = 153,750`, so the minimum clearance along a diagonal edge is `sqrt(160,000 − 31,250) = 358.8 mm > 350`. For medium, 483.5 ≤ 500; for large, 577.7 ≤ 600.
- The model measured at least 413.8 mm of clearance along every walkable person edge in the lodge area [model], consistent with the 358.8 mm floor.

**Checked against the real content** [model]:
- **Lodge door** (`content/regions/ashen_hollow.yaml:143`, z 127.2-128.8), node column x = 51.875:
  - person lanes at z 127.625, 127.875, 128.125 and 128.375 (425, 675, 675 and 425 mm from the jambs, all ≥ 400);
  - medium and large: the middle two only (675 ≥ 500 and ≥ 600).
- **Smithy door** (`:144`): the same 4 / 2 / 2.
- **Fence gap** (x 51.4-53, `:80`): 3 person lanes.
- **Beam line** (`:89-91`, sealed from x 143.6 to 153.4): sealed for a standing person, as `Kinematics` makes it (the beam's 1.3 m clearance is under 1.8 m).

**Tradeoff, stated plainly.** An opening between about 0.8 m and 1.05 m that a 0.7 m body physically fits may or may not be found, depending on how it sits against the lattice. That is the price of 250 mm. Building must therefore:
- make doorway clear openings **≥ 1.6 m** (navigation guarantees 1.05 m for a person, and 1.45 m for all three classes);
- snap in multiples of 250 mm (0.5 m recommended), so lane counts do not depend on where a piece lands.

---

## 4. Authority (brief item 4)

| Aspect | Specification |
|---|---|
| **Reads (authoritative)** | (1) `Setup.Layout.Space`: bounds and authored static blockers, immutable content (`src/Domain/Spatial/Kinematics.cs:104`). (2) Authored `DoorSite`/`BarrierSite` footprints (`src/Domain/Spatial/RegionLayout.cs:12`, `:28`). (3) Door open and barrier lifted state through `SystemContext.IsOpen`/`IsLifted` (`src/World/Runtime/Systems.cs:41-45`), **at query time**. (4) Placed pieces through the building read API: footprint, traversal class (`Solid`, `Door`, `None`), height, clearance, and a placed door's open state (section 13.5). (5) `config.navigation` and `config.base_speeds` content. **Terrain is not read**: it never blocks (`Kinematics.cs:159`). |
| **Filter** | An input counts only if `Kinematics.Blocks(b, 0, 1_800)` (`Kinematics.cs:166-167`). M7 agents stand 1.8 m and never leave the ground, as every non-player mover does today. So the Woundmoss beam (clearance 1.3 m) blocks, and a roof given a clearance of at least 1.8 m would drop out. Roofs should never be blockers at all (section 13.5, B7). |
| **Derived** | `NavGrid`: per tile, `SolidFit` and `ClosedFit` bytes, the canonical input list, the gate list and the input stamp. |
| **Cached** | The `NavGrid` in `StateSlice.Navigation` (transient, like `CellTiers`, `src/World/Runtime/RuntimeState.cs:36`). A* and flood scratch is working memory whose contents never influence a result (generation stamps replace clearing). Nothing else is cached across ticks. |
| **Invalidates** | A change to the set of placed blocking or door footprints: place, dismantle, destroy. It is pushed by the building system as `RebuildNavigation`. |
| **Does not invalidate** | Door or barrier open/close (gates are read at query time); damage or repair (the footprint is unchanged); bodies moving; tier changes; saves. Content never changes at run time. |
| **Rebuilt** | All tiles of the region's cells in the `Simulation` constructor (new game and load alike). On `RebuildNavigation`, the node rectangles within 600 mm of the changed footprints, in each touched tile, plus those tiles' input lists and stamps. |
| **Saved** | Each mover's `NavRoute`, next to its body, by the body's owner: the companion's in `CompanionRecord`; the assigned NPC's in its assignment record (section 11). |
| **Not saved** | The grid, tiles, gate lists, input lists, stamps (except the copy inside a route), scratch, counters, and `NavigationRebuilt` history. Add a row to the PERSISTENCE §2 not-saved table: "Navigation grid: rebuilt in the `Simulation` constructor from layout and structure records; never persisted." |

**Invariant.** No mover decision may read a counter, a rebuild history, a revision number, or scratch contents. Every decision reads only persisted mover state, current authoritative geometry and flags (and the grid, a pure function of them), and the tick. Test N-A6 checks this end to end.

---

## 5. Dynamic building updates (brief item 5)

### 5.1 The deterministic sequence

All of it happens synchronously inside one command's handling at a tick boundary of `Simulation.DrainCommands` (`src/World/Runtime/Simulation.cs:268-306`). A replay of the command log therefore reproduces it at the same boundary.

```
Boundary N  (DrainCommands, FIFO)
 PlacePieceCommand ──► BuildingSystem.Handle(cmd, N)
   1. building's own validation (overlap, sockets, build area, terrain, protected geometry, bodies)
   2. navigation validation: _navigation.CheckEdit(add: piece footprints)  -> null | reason     (section 5.4; read-only)
   3. Dispatch(ExchangeItems(...))       refusable step first (the Crafting.cs:100-102 pattern)
   4. commit the piece record            (building's slice / world delta)
   5. Dispatch(RebuildNavigation(Changed: the piece's footprints, Reason: "placed"))
        NavigationSystem.Handle(c, Now = N)
          a. inputs  = CurrentInputs()     authored static + authored gates + building.PlacedFootprints(), canonical order
          b. for each changed footprint F: R_F = nodes whose centre lies in AABB(F) ⊕ 600 mm
             for each tile T that R_F meets: dirty[T] = bounding rect of (dirty[T] ∪ (R_F ∩ T))
          c. for T in (Tz, Tx) order:  T' = T.Restamped(dirty[T], inputs)     copy-on-write
                                        T'.Inputs = inputs whose AABB ⊕ 600 mm meets T's node-centre rect
                                        T'.Stamp  = InputStamp(T'.Key, T'.Inputs)
          d. State.SetNavigation(owner, grid with every T')
          e. publish NavigationRebuilt(tiles, nodesRestamped, "placed", N)          views and tests only
   6. publish PiecePlaced
Step N+1
   movement      the player's Kinematics.Step already sees the piece (effective blocker set, section 13.5 B2)
   creatures     slide along it like any wall
   companions    NavFollower: WindowStamp(route.Watch) != route.Stamp  -> replan "geometry" -> new corners
   work NPC      the same check on its own route
```

- **Dismantle and destroy** take the same path with `Reason: "removed"`. The building commits the removal first, then dispatches the footprints that went away.
- **Destruction during a step** (an explicit damage rule applied inside `Step`) dispatches `RebuildNavigation` inside that step, with `Now = N+1` (`Simulation.cs:367`). Movers later in the fixed tick order see the new grid in the same tick. That is deterministic, because the order is fixed (`Simulation.cs:312-339`).
- **Door toggles and barrier lifts** (the player's `InteractCommand`, an agent's `OpenDoor`) change gate state only. Nothing is rebuilt. The next query reads the flags, as `ClosedDoors()` already does (`Systems.cs:48`).

### 5.2 What is recomputed: a rectangle, never a cell, never a neighbour by default

- The recomputed region is the nodes whose centres lie in the changed footprint's AABB inflated by `RpMax = 600 mm`, clipped to each tile it meets.
- For a 3 m x 0.4 m wall piece that is 16 x 6 = 96 nodes [model]. For a 6 x 6 m hut edit it is at most about 30 x 30 = 900.
- A neighbouring tile is touched only when the inflated rectangle actually crosses the seam: the piece lies within 600 mm of it, or straddles it. Then only the strip inside the rectangle is restamped.
- The whole tile is restamped only by the reference builder: at construction, and in tests.

### 5.3 Why a rectangle rebuild equals a full rebuild

- `SolidFit[n] = min(boundsFit(n), min over solid inputs I of FitAt(I, n))`. `ClosedFit[n]` is the same with gate inputs added.
- `min` is commutative and associative, so the value is independent of input order, tile order and edit history.
- A node farther than `RpMax` from an input's AABB gets `FitAt = K` (3) from that input, the identity of the `min`. So only nodes inside `AABB ⊕ RpMax` of a changed input can change. Every one of them is recomputed from **all** current inputs.
- There is one stamping function, `StampRect(tile arrays, rect, inputs)`. `BuildTile` is `StampRect` over the whole tile after a fill with `K`. Tests N-D11 and N-A9 assert byte equality after hundreds of edits.

### 5.4 The navigability check for placement (`CheckEdit`)

Navigation owns the "un-navigable" half of ROADMAP's validation. The check is **local**: it never needs the region spawn or global labels, so it gives the same answer whatever tiles are resident in M9. It runs on a tentative copy of the touched tiles, in scratch, and never mutates the slice.

Inputs: the footprints to add (removal needs no check: removing inputs can only raise `Fit`). Agent: person, an opener, barriers per their current state.

Window `W`: the added footprints' AABB inflated by **32 m**, each side capped at 128 m.

**V-N1. No newly sealed pocket.**
- Take the walkable nodes on the one-node ring just outside `AABB(add) ⊕ 600 mm`, in `(j, i)` order.
- For each one not already labelled, flood (4-connected) in the AFTER grid, up to **16,384 nodes** (1,024 m²) inside `W`. Reaching the limit or `W`'s border means *open*.
- If the flood ends *sealed*, flood the same seed in the BEFORE grid. If that one was open, refuse: "that would close off a space with no way in".
- A newly sealed pocket must border the change, so the ring is sufficient.
- A pocket that was already sealed before (none exists in Ashen Hollow for an opener) does not count.

**V-N2. Protected points.** For every protected point inside `W`:
- a body or site circle (r 350) must not be overlapped by an added footprint;
- a walkable node must remain within 1,600 mm (interact reach) if one existed before;
- a door approach point keeps a walkable node within 250 mm if it had one.

The failure text names the point: "that would wall in Renn's place". Protected points are:
- the region spawn;
- the player's body;
- every companion's body;
- the assigned NPC's body and goal;
- every `NpcSite` circle, whether or not the NPC stands there, so a working NPC can always walk home and retire (section 8.4);
- authored containers and stations;
- both approach points of every authored and placed door: the leaf centre ± (half the leaf thickness + 700 mm) along its normal;
- every placed functional anchor.

**V-N3. Functional access.** A new functional piece's use anchor (the chest's use point, the station's work anchor) must be standable at 350 mm, and must lie in an open component. Else: "nothing could reach its work place".

**V-N4. Doors.** A new door piece's two approach points must each have a walkable node within 250 mm. Else: "the door would open onto a wall".

Cost: typically one or two AFTER floods of at most 16,384 nodes, plus a BEFORE flood only when a pocket seals, so about 0.5-2 ms per placement command (estimate). The ghost preview runs it only when the snap candidate changes, on its own scratch (section 13.7).

---

## 6. Cell seams (brief item 6)

**Mapping.**
- Node `(i, j)` is global: `i = FloorDiv(xMm, 250)`, centre `x = 250·i + 125`.
- A tile is `NavTileKey(Tx, Tz)`, with `Tx = FloorDiv(i, 400) = FloorDiv(xMm, 100_000)`. That equals `rx·20 + cx`, because cell origins are `(rx·2,000 + cx·100) m` (`src/World/Runtime/Systems.cs:498-499`).
- Domain cannot reference `CellKey` (a World type), so tile keys are integers. A World test (N-W1) pins the mapping against `CellKey.OfWorld` (`src/World/Coordinates.cs:93-96`) for the four hollow cells, the seam lines and the negative region `r_neg1_0`.

| Hazard | How the design removes it |
|---|---|
| Duplicated nodes | No node centre lies on a seam: centres sit 125 mm inside a tile, and seams are multiples of 100,000 mm. Every node belongs to exactly one tile. There are no border nodes and no overlap margin |
| Ambiguous coordinates | A point at x = 100,000 mm maps to node 400, which is in tile 1 (east). `CellKey.OfWorld(100.0, z)` gives cell 1 too. Nav converts with integer `FloorDiv`, never through `double` metres |
| Disconnected borders | A neighbour is `(i ± 1, j ± 1)` in global indices. Tile lookup is `FloorDiv`. A*, probes, floods and `SegmentClear` never see a tile boundary |
| Order-dependent rebuilding | Node values are a `min` over inputs within 600 mm (section 5.3). Building A then B, B then A, or B alone after evicting it gives identical bytes and stamps |
| Straddling structure | A straddling footprint is one input. It appears in both tiles' input lists, and each tile stamps its own nodes. A segment is tested against the union of the lists of the tiles it touches; a duplicate is harmless because the test is an AND |
| "Valid before a cell reload, invalid after" (RK-14 fail case 3) | A tile is a pure function of its key and the global inputs. Rebuild after save/load, or after eviction in M9, is byte-identical (N-A7, N-D9) |

**RK-14 restated for a domain representation.** It fails if (1) no path is found, (2) the path exits and re-enters the structure, or (3) the path is valid before a reload and invalid after. (1) and (2) are N-D10 and N-A2; (3) is N-D9(b) and N-A7. The "runtime recording" half is the playthrough's M7 beat (section 10.5). No engine navigation is involved.

RK-14's mitigation asked for "a stitched multi-cell neighborhood ... with a seam overlap margin" and neighbour dirtying (`docs/RISK_REGISTER.md:294`). Here the "overlap margin" is the 600 mm influence radius, and neighbour dirtying happens exactly when an edit's rectangle crosses a seam. The mitigation "constrain placement that would straddle a seam" becomes unnecessary: straddling is proven, not constrained (scope ruling item 6).

---

## 7. Pathfinding (brief item 7)

### 7.1 Agent and walkability

```csharp
public readonly record struct NavAgent(int ClassIndex, bool OpensDoors);   // M7: person = (0, true)
```

Node `n` is walkable for agent `(k, opens)` when all of these hold:

```
SolidFit[n] > k
&& ( ClosedFit[n] > k                                   // fast path: no gate within Rp_k
     || every gate g in tile(n).Gates with FitAt(g, n) <= k is passable )
passable(g) = g.Kind == Barrier ? IsLifted(g)                     // nobody opens a barrier
            : opens ? true                                        // openers plan through doors, whatever their state
            : IsOpenNow(g)                                        // non-openers read the state at query time
```

- The slow branch runs only for nodes within `Rp_k` of a gate. Ashen Hollow has 3 gates; M7 adds a handful.
- **Openers plan independently of door state**, so a door toggled by the player never changes an opener's route. The follower opens doors when it reaches them (section 8.2).
- Bounds: `boundsFit(n)` counts the classes for which `x − Rp ≥ MinX && x + Rp ≤ MaxX` (the same for z), the `IsClear` bounds rule (`Kinematics.cs:171-172`) at `Rp`.
- Obstacle inflation is built in: `FitAt` compares distances against `Rp_k`. There is no separate dilation pass.

### 7.2 Integer geometry (`NavGeometry`, pure)

- **`FitAt(input, node)`**, which feeds `SolidFit` and `ClosedFit`. It counts ascending classes, stopping at the first that fails:
  - box `[x0, z0, x1, z1]`: `dx = max(x0 − px, 0, px − x1)`, `dz` likewise, `d2 = dx² + dz²`; class k fits iff `d2 ≥ Rp_k²`;
  - circle `(cx, cz, Rc)`: class k fits iff `(px−cx)² + (pz−cz)² ≥ (Rp_k + Rc)²`.

  It is evaluated only for nodes within the inflated AABB, so every term is below 10¹² and `long` is ample.
- **`PointClear(p, R, input)`**: the same comparisons at an arbitrary radius. It equals `Separation(p, R) is null` exactly, because `Separation` squares `long`-valued inputs in `double`, below 2⁵³ (property test N-D15).
- **`SegmentClear(A, B, R, input)`**: the exact swept circle.
  - **Circle** `(C, Rc)`. Let `d = B − A`, `w = C − A`, `dot = w·d`, `len2 = d·d`, and `Q = R + Rc`.
    - If `len2 == 0`, use the point test.
    - If `dot ≤ 0`, clear iff `|w|² ≥ Q²`.
    - If `dot ≥ len2`, clear iff `|C − B|² ≥ Q²`.
    - Else clear iff `|w|²·len2 − dot² ≥ Q²·len2`, evaluated in **`Int128`** (products reach about 10²¹ for 128 m segments).
  - **Box.** The swept-circle obstacle is the open union of two rectangles, `[x0−R, x1+R] × [z0, z1]` and `[x0, x1] × [z0−R, z1+R]`, and four corner discs of radius R.
    - A segment meets an open rectangle iff its AABB overlaps the rectangle strictly on both axes, and the four corner values `(az−bz)(cx−ax) + (bx−ax)(cz−az)` (`long`) are strictly mixed in sign.
    - The corner discs use the circle test with `Rc = 0`.
    - Touching is clear throughout.
  - **Candidate inputs:** those in the tiles meeting the segment's AABB ⊕ R whose own AABB ⊕ R meets it. Duplicates and order cannot change the answer (it is an AND).
- `System.Int128` is in .NET 8 (verified in the installed 8.0.31 reference pack). There is no `double`, `float`, `Math.Sqrt` or transcendental anywhere in navigation (test N-X1).

### 7.3 The query pipeline: `NavSearch.Plan(grid, gates, agent, start, goal, config, scratch)`

1. **Snap the start.** Take the nodes within Chebyshev radius 4 (1 m) of `start`'s node, ordered by (squared mm distance from `start` to the centre, `j`, `i`). Choose the first walkable node with `SegmentClear(start, node, r − 50)`. (A body pressed against a wall can sit a few mm inside `r` after `Resolve`'s four passes, `Kinematics.cs:183-208`.) None: `StartBlocked`.
2. **Snap the goal.** The same over Chebyshev radius 8 (2 m), requiring `SegmentClear(node, goal, r)`. None: `GoalBlocked`.
   - If `goal` itself is standable (`PointClear` at r against solids and impassable gates, and inside bounds), the final corner is the exact goal.
   - Otherwise it is the snapped node's centre.
3. **Window.** The AABB of the two snapped nodes, inflated by 80 nodes (20 m), clipped to the resident grid.
   - If the endpoints' span exceeds 352 nodes (88 m) on either axis: `TooFar` (the window is at most 512 x 512 nodes, 128 m).
   - M7 routes are short: the companion's goal is under 30 m away (catch-up beyond), and the work anchor is limited to 64 m from the NPC's site (section 13.6, N2).
4. **Enclosure probes.**
   - A 4-connected BFS over walkable nodes, from the goal node, then from the start node, each limited to **2,048 nodes** (128 m²) and to the window.
   - A probe that reaches the other endpoint stops the probing, and A* runs.
   - A probe whose frontier empties below the limit, without touching the window border, proves an enclosed pocket: `Enclosed`, with zero expansions.
   - Anything else is inconclusive, and A* runs.
   - 4-connectivity is the same partition as the A* graph, because a legal diagonal implies both orthogonal steps exist.
   - [model] A non-opener aimed into the closed test hut: the goal probe answers `Enclosed` after 324 nodes. Without the probe, A* spent the full budget.
5. **A\*** (section 7.4). `Found` when the goal node is popped. The open set empties: `Exhausted`, or `NotInWindow` if a window-border node was closed. **16,000 expansions:** `Budget`.
6. **String pulling** (greedy, exact).
   - `cand = [start, n₀, …, nₖ, goal-if-standable]`.
   - From anchor `a`, extend `k` while `SegmentClear(cand[a], cand[k+1], r)` holds against solids and the gates the agent cannot pass. Emit `cand[k]`; it becomes the next anchor.
   - `cand[a] → cand[a+1]` is always clear between lattice nodes by C1, so every emission advances at least one node.
   - More than **32** corners: keep 32 and mark the route `Partial`.
   - [model] Real routes pull to 3-5 corners (Appendix C).
7. **Result:** `NavPlan(Outcome, Corners, Window, Expansions, ProbeNodes, PullTests)`. It maps to a `NavRoute` as `Active`, or `Unreachable` for every other outcome.

### 7.4 Costs, heuristic, neighbours, ties

- **Graph.** 8-connected. A diagonal is allowed only if both orthogonal neighbours are walkable (no corner cutting).
- **Costs.** `int`: 1,000 orthogonal, 1,414 diagonal. There is no door penalty and no proximity penalty in M7; both are seams (section 9). `g` fits in `int`: at most 262,144 nodes x 1,414 = 3.7·10⁸.
- **Heuristic.** `h = 1000·max(|di|,|dj|) + 414·min(|di|,|dj|)`. It is exactly the lattice distance on empty ground, so it is admissible and consistent.
- **Neighbour order.** Fixed: E(+1,0), N(0,+1), W(−1,0), S(0,−1), NE(+1,+1), NW(−1,+1), SW(−1,−1), SE(+1,−1). "N" is +Z. The direction table is a `private static readonly ImmutableArray<(int Di, int Dj, int Cost)>`, a struct type, so `StateAssemblies_HoldNoStaticMutableState` accepts it (`tests/Architecture.Tests/ArchitectureTests.cs:85-105`, `:204-209`). A `static readonly int[]` would fail that test.
- **Open set.** Our own binary min-heap of `(f, h, idx)`, compared lexicographically, where `idx = (j − wj0)·ww + (i − wi0)`: row-major `(j, i)` order inside the window.
  - This is a total order over distinct nodes, so the pop sequence depends on the data alone.
  - .NET `PriorityQueue` is not used; its order among equal priorities is unspecified.
  - Stale entries are skipped when popped (closed bit).
- **Relaxation.** Only on a strictly smaller `g`. The parent is a direction byte, so among equals the first-found parent wins.
- **Why optimal A\*.** The model tried heuristic weights 1.1 to 2.0 on the worst real route. Weight 1.25 saved only 20% of expansions (9,419 to 7,531) at the price of optimality [model]. It is not worth a second tuning knob.

### 7.5 Unreachable: always decided, never timed

| Outcome | Meaning | Route status |
|---|---|---|
| `Found` | Goal node popped | `Active` (`Partial` if truncated at 32) |
| `StartBlocked` | No walkable node within 1 m reachable in a straight line | `Unreachable` |
| `GoalBlocked` | Nowhere to stand within 2 m of the goal | `Unreachable` |
| `Enclosed` | A probe proved the goal or the start sealed in | `Unreachable` |
| `TooFar` | Endpoint span over 88 m | `Unreachable` |
| `Exhausted` / `NotInWindow` | Open set emptied (inside the window / touching its border) | `Unreachable` |
| `Budget` | 16,000 expansions | `Unreachable` |

Every outcome counts expansions, not time, so it is deterministic. What the mover does next is its owner's rule:
- **Assigned NPC:** it stands, turned towards the goal, and retries when `tick − PlannedTick ≥ 40` (2 s). After 200 ticks its view reads "Blocked". It **never teleports**.
- **Companion:** the Phase-1 behaviour runs unchanged: head for the oldest mark, or the character when the trail is empty (`Companions.cs:339-353`), then snag, then catch-up. So C16 and every M6 guarantee still hold.

### 7.6 Recalculation triggers

These are evaluated in `NavFollower.Next` at each mover tick, in this order; the first match wins. Every condition reads only persisted route fields, persisted mover state, current authoritative geometry and flags, and the tick.

| # | Reason | Condition | Throttle |
|---|---|---|---|
| 1 | `none` | `route.Status == None` | immediate |
| 2 | `retry` | `Status == Unreachable && tick − PlannedTick ≥ 40` | (otherwise hold) |
| 3 | `goal_moved` | `dist²(goal, route.Goal) > 2,000²` | `tick − PlannedTick ≥ 10` |
| 4 | `geometry` | `grid.WindowStamp(route.Watch) != route.Stamp` | immediate |
| 5 | `blocked` | a remaining corner-to-corner segment fails `SegmentClear(·,·, r)` against solids and the gates the agent cannot pass | immediate |
| 6 | `stuck` | the owner's `StuckTicks > 0 && StuckTicks % 20 == 0` | immediate |
| 7 | `partial` | `Corners` empty, `Partial`, not arrived | immediate |
| 8 | `off_line` | `SegmentClear(body, Corners[0], r − 50)` fails (a body shouldered it off the line) | `tick − PlannedTick ≥ 10` |

- `goal_moved` fires only for the companion, whose goal is the character. A running character (3.2 m/s) triggers it about every 0.6 s while out of view.
- Anchors and sites do not move.

### 7.7 Floating point

- Rasterisation, probes, A*, floods, pulling, stamps and route validation are integer.
- The only floating point on a nav mover's path is what exists today:
  - the mover's intent direction, as `CompanionSystem.Step` builds it (`Companions.cs:558-568`, `sqrt` and rounding);
  - `CombatRules.FacingTowards`'s `Atan2` for facing (`src/Domain/Combat/Combat.cs:238-243`);
  - `Kinematics.Step` itself.
- Navigation adds no transcendental and widens no existing exposure.

---

## 8. Local movement (brief item 8)

### 8.1 Three layers, three owners

| Layer | Function | Owner | M7 change |
|---|---|---|---|
| Global route | `NavSearch.Plan`: snap, probes, A*, pull | Domain, pure | new |
| Route following | `NavFollower.Next` returns a `NavStep`: a target point, `OpenGate`, `Arrived` or `Unreachable` | Domain, pure | new |
| Intent and facing | The owner's existing pattern: `CompanionSystem.Step` (`Companions.cs:558-568`) | Owner | none for the companion; the NPC copies it |
| Collision and bodies | `Kinematics.Step` with the owner's obstacle list | Domain | **none** |

- Bodies are never in the grid. The player, creatures and other NPCs are handled by `Kinematics` push-out and sliding, then the stuck replan, then (companion only) catch-up.
- The "who blocks whom" table is unchanged (`research/spatial_movement.md` §7.9). For example, creatures still pass through non-companion NPCs (`Creatures.cs:830-836`).
- There is no crowd avoidance: M7 has two movers.

### 8.2 `NavFollower.Next`

```csharp
public enum NavStepKind { Walk, Arrived, OpenGate, Unreachable }
public sealed record NavStep(NavRoute Route, NavStepKind Kind, NavPoint Target, string? GateKey, string? ReplanReason);

public static NavStep Next(NavQuery q, NavAgent agent, NavRoute route, NavPoint body, NavPoint goal, int stuckTicks, long tick)
```

`NavQuery` bundles the grid, the gate states, the config and a scratch.

1. **Replan** per section 7.6. On a replan: `route = FromPlan(q.Plan(agent, body, goal), tick, goal)`.
2. **Unreachable:** return `Unreachable`; the owner holds.
3. **Advance** at most 4 times: drop `Corners[0]` while `Corners.Length ≥ 2` and either:
   - `dist²(body, Corners[0]) ≤ 400²`; or
   - `SegmentClear(body, Corners[1], r)` (look one corner ahead).

   The last corner is never dropped here.
4. **Arrived:** only the final corner remains, it is the goal, and `dist²(body, goal) ≤ 300²`. Return `Arrived` with `Target = goal`.
5. **Door:** a closed, openable gate fails `SegmentClear(body, Corners[0], r)`, and `DistanceTo(gate) ≤ InteractReachMm` (1,600 mm, `content/config/base_speeds.yaml:11`, measured from the body as the player's reach is, `Systems.cs:259-262`). Return `OpenGate(key)`.
   - If several qualify, the nearest wins by (integer squared distance to the footprint, `MinXMm`, `MinZMm`). The tie-break is geometric, **never the key**, because a placed door's key is a ULID that differs in a replay.
6. Otherwise return `Walk` with `Target = Corners[0]`.

Cost per mover per tick:
- the stamp check: at most 4 tile stamps into one SHA-256, about 1 µs;
- validation: at most 8 remaining segments x about 20 nearby inputs;
- the look-ahead and door tests.

About 10-20 µs (estimate).

### 8.3 The companion (the change to `CompanionSystem.Follow`, `Companions.cs:325-361`)

```
Follow(c, npc, tick):
  catch-up check                      unchanged (CompanionRules.CatchUp; Companions.cs:331-332)
  gait; within 2.5 m stand            unchanged; standing also sets Route := None
  drop reached marks (800 mm)         unchanged (MarkReachedMm, Companions.cs:98)
  if InClearView(body, player):                     Direct: goal = player; Route := None        (unchanged branch)
  elif c.Route.Status == Active:                    Route:  step = Nav.Follow(person, c.Route, body, player, c.StuckTicks, tick)
  elif newest trail mark in clear view exists:      Trail:  goal = that mark; trim                 (unchanged branch)
  else:                                             Nav:    step = Nav.Follow(...)   (plans: replaces "the oldest mark")
       step.Kind == Unreachable -> goal = oldest mark, or the player if the trail is empty   (the Phase-1 fallback, unchanged)
  step.Kind == OpenGate  -> Dispatch(OpenDoor(step.GateKey, c.NpcId)); face the door; no Kinematics step this tick
  step.Kind == Walk/Arrived -> goal = step.Target
  moved = Step(npc, goal, gait)       unchanged helper
  headway, StuckTicks                 unchanged (displacement ≥ 30% of expected travel, Companions.cs:357-360)
  c.Route = step?.Route ?? c.Route
```

- **Hysteresis.** An `Active` route outranks trail marks, and only the character in clear view pre-empts it. There is no Trail-Route flip-flop.
- **Route resets.** `Route := None` also on `Order` (next to the existing trail clear, `:186-187`), on downed, on `Up`, and in `CatchUp` and `Fall`.
- **Unchanged.** The snag rule (80 ticks without headway) and the 30 m catch-up remain the safety net. So a route that fails in practice still ends in Phase-1 behaviour, and three stuck-replans (at 20, 40 and 60 ticks) come before any snag.
- **C16 needs no change.** Its route (`CompanionTests.cs:119-160`) runs through the Direct and Trail branches almost everywhere, and must pass unmodified with zero `CompanionCaughtUp`.

### 8.4 The assigned NPC (the mover contract for whichever system owns the assignment)

State (section 13.6): body, phase (`going_to_work`, `at_work`, `going_home`), goal (x, z, facing), `StuckTicks`, `NavRoute`.

```
TickWorker(w, npc, tick):                          tick order: after _companions.Tick, before _npcs.Tick
  if TierOf(npc.Body) != A: return w                         as creatures and companions (Creatures.cs:264-265)
  if conversation is with this NPC: face the character (turn 18,000 mdeg/tick); StuckTicks = 0; return
  if Phase == at_work: turn towards the anchor facing; return
  if body.(x, z) == goal.(x, z):
       turn towards goal facing (18,000 mdeg/tick, Social.cs:107);
       when facing == goal facing: going_to_work -> at_work, Route := None;
                                   going_home    -> retire the record (the NPC is at baseline again)
       return
  step = Nav.Follow(person, w.Route, body, goal, w.StuckTicks, tick)
  Unreachable -> face the goal; Route = step.Route; return                   (never teleport)
  OpenGate    -> Dispatch(OpenDoor(step.GateKey, w.NpcId)); face the door; Route = step.Route; return
  Walk/Arrived-> intent = toward step.Target at Gait.Walk (80 mm/tick), facing = FacingTowards(body, target)
                 if step.Target == goal and d = dist(body, goal) <= 80: intent = ExactLanding(goal)
                 moved = Kinematics.Step(body, intent, Setup.Movement, space, Obstacles(npcId), TickMs)
                 headway = landing ? true : dist(body, moved) >= 24          (30% of 80 mm, as the companion measures)
                 StuckTicks = headway ? 0 : StuckTicks + 1
                 Dispatch(PlaceNpc(npcId, moved)); Route = step.Route
```

- **Exact landing.** When `d ≤ travel` (80 mm), set `dir = (round(1000·dx/80), round(1000·dz/80))`. For `|dir| ≤ 1000`, `Kinematics.Step` moves exactly `dir·travel/1000` (`Kinematics.cs:137-147`). The rounding error is at most 0.04 mm per axis, and whole-mm quantisation (`:157-158`) then lands the body **exactly** on the integer goal when nothing obstructs it. So a working NPC reaches its anchor, and later its site, to the millimetre.
  - Retirement can therefore require exact equality of x, z and facing.
  - A loaded world whose record is gone places the NPC at its site (`Social.cs:127-139`), exactly where the continuing world has it. Save-then-continue is exact across retirement.
  - If a body blocks the last step, the record simply stays (`going_home`) until it lands. That is sparse-delta doctrine: store what diverges.
- **Obstacles.** Exactly the companion's set: closed gates, the effective structure blockers, living creatures, the player, and every other NPC including companions (`Companions.cs:571-581`).
- **Jams.** A body in a doorway shows up as rising `StuckTicks`, then a replan every 20 ticks, then the "Blocked" view after 200 ticks. The NPC keeps trying.

---

## 9. Future seams (brief item 9): designed now, not built

| Future need | Seam left in M7 | What arrives later, as an addition |
|---|---|---|
| **NPC schedules** (M9/M10) | The mover block (goal, route, phase, stuck) is exactly what a schedule issues; `NavFollower` is agent-generic | Schedules issue goals. A per-tick plan budget: a counter in `NavigationSystem`, reset whenever the tick passed in differs, never saved, granted in system order. It is not needed for two movers |
| **Tier B coarse movement** (`Systems.cs:521-530`; `docs/WORLD_ARCHITECTURE.md:235`) | Tiles are cell-aligned; walkable runs along tile edges are well defined | A **portal graph** per class: each maximal walkable run on a tile edge becomes a portal; intra-tile portal costs come from a bounded A*, cached per tile stamp (derived, never saved). Long routes plan on portals and refine in ≤ 128 m windows. Promotion to tier A snaps with the start-snap rule, which answers WORLD_ARCHITECTURE §7.2's "off navmesh" legality (`:297`) |
| **Larger settlements, 2 km regions** | Per-tile storage; global indices; windowed search; rectangle rebuilds; local edit checks; "tile alone == tile from full build" (N-D9) | Residency follows tier A (≤ 21 tiles, section 15), built on promotion, at most one tile per tick. A probe or flood reaching a non-resident tile is inconclusive. The per-tile input list becomes the `Kinematics` broadphase through an additive overload |
| **Doors: locks, permission, courtesy** | Gates have a kind and a key; one `passable(g)` predicate; one `OpenDoor` command | `NavAgent` gains an access set; `NavGate` gains `Access` (key item, owner, or a faction **access** check under ruling 3, never hostility). "Close behind" adds a persisted "door I opened" field and a body-clear close check |
| **Bridges** | Planar model | A bridge over void or water is a corridor where a future "impassable terrain" solid layer is absent. An overpass (two walkable levels at one x, z) is excluded by ruling 2 and would need a vertical domain `Kinematics` does not have (`:159`) |
| **Roads** | Integer costs, admissible heuristic | A per-tile cost byte (`1.0x`-`2.0x`). The heuristic uses the cheapest multiplier to stay admissible |
| **Creature sizes and creature pathing** | Classes 450 and 550 already rasterised in the same byte | `NavAgent(ClassFor(radius), OpensDoors: false)`, plus a route in `CreatureRecord` (a `CreatureDto` freeze and a migration) |
| **Jump and crouch agents** | One height class (1.8 m), selected by the `Blocks(b, 0, h)` filter | One `SolidFit`/`ClosedFit` pair per height class (the beam becomes passable for crouch-capable agents), plus explicit jump links over authored low obstacles |
| **45° or free rotation** | `FitAt` needs only a point-to-footprint squared distance; `SegmentClear` is per shape | An oriented-box integer distance and segment test. `Blocker` gains a shape, which also touches `Footprints.Center`, the presentation greybox and `Fitting` |
| **Networking** | Navigation is deterministic authority code keyed to authoritative state | A future authority runs it; clients never do |

---

## 10. Acceptance tests (brief item 10)

Homes:
- `tests/Domain.Tests/Spatial/NavigationTests.cs`: synthetic geometry, no content, pure functions.
- `tests/World.Tests/NavigationRuntimeTests.cs`: internals, through `InternalsVisibleTo World.Tests` (`src/World/World.csproj:19`).
- `tests/Application.Tests/NavigationTests.cs`: real Ashen Hollow content through `Harness`.
- Persistence, architecture and content tests where noted.

Deterministic randomness in tests is a test-local LCG, never `System.Random`.

### 10.1 The brief's required cases, mapped

| Brief case | Tests |
|---|---|
| Simple reachable path | N-D1 |
| Blocked path | N-D2, N-D4, N-D5 |
| Structure placed across the previous route | N-D7, N-A3 |
| Structure removed | N-D8, N-A4 |
| Cell-seam crossing | N-D9, N-D10, N-A2 |
| Save/load reconstruction | N-A6, N-A7 |
| Deterministic replay | N-A8, N-D12 |
| Different body size | N-D6 |
| Unreachable target | N-D4, N-D13, N-A5 |
| Repeated rebuild gives identical data and path | N-D11, N-A9 |

### 10.2 Domain

| # | Name | Setup | Assertions |
|---|---|---|---|
| N-D1 | `AnOpenField_RoutesStraight` | 40 x 40 m, empty; (10, 10) to (30, 25) m | `Found`; corners == [exact goal]; expansions ≤ 2 x the octile node distance ([model]: 121) |
| N-D2 | `AWall_IsRoutedAround_AndWalkedByKinematics` | Off-centre 20 m x 0.4 m wall across the line | `Found`; every segment `SegmentClear` at r; driving `Kinematics.Step` with `NavFollower` arrives within 1.3 x (length / speed) ticks; `Kinematics.IsClear` holds at every tick end |
| N-D3 | `ADoorway_IsTheWayIn` | 10 x 8 m room, 0.4 m walls, one 1.6 m doorway | Crosses the wall line once, inside the doorway |
| N-D4 | `ASealedRoom_IsEnclosed_WithoutAStar` | The same room, no doorway | `Enclosed`; 0 expansions; probe nodes ≤ 2,048 |
| N-D5 | `AClosedDoor_StopsANonOpener_NotAnOpener` | N-D3 plus a door gate | Non-opener `Enclosed` while closed, `Found` once open. Opener `Found` with **identical corners** whether open or closed. Toggling leaves `Grid.Digest()` unchanged |
| N-D6 | `BodyClasses_ThroughDoors` | Doorway z [10.0, 11.6] m; then [10.0, 11.2] m | Lanes 3 / 2 / 2; then 1 / 1 / 0. `large` gets `Unreachable` through the 1.2 m door; `person` gets `Found` |
| N-D7 | `APieceAcrossTheRoute_Invalidates` | Plan; add a box across corner segment 2; rect rebuild | `WindowStamp(route.Watch) != route.Stamp`; `Next` gives replan reason `geometry`; the new corners avoid the box. With the stamp check disabled in a test build, reason `blocked` fires instead |
| N-D8 | `ARemovedPiece_GivesTheShorterRoute` | A detour around a wall; remove it | Replan reason `geometry` on the next call; the new cost is lower than the old remainder |
| N-D9 | `TheSeam_IsNotARepresentationBoundary` | 200 x 200 m, 4 tiles; a hut straddling (100, 100) with a door on the z = 100 m seam | (a) the full build's digest equals building the four tiles in all 24 orders; (b) a tile built alone, and after evicting and rebuilding, equals the same tile from the full build; (c) a monolithic single-array reference raster built by the same `StampRect` equals the union of the tiles; (d) (100,000, z) is tile 1; (e) 50 fixed start/goal pairs across both seams: tiled and monolithic routes are identical |
| N-D10 | `AStraddlingHut_IsEnteredOnce_AndCrossedTwice` | 6 x 6 m hut centred on (100, 100) m (RK-A2's four cells), doors west and east | A route to the interior crosses the hut's outer AABB exactly once; west to east through it crosses exactly twice (RK-14 "exits and re-enters" fails it); both are walked by `Kinematics` |
| N-D11 | `RectRebuild_EqualsFullBuild_AlwaysAndBack` | 200 LCG place/remove edits of boxes and circles, a third within 600 mm of a seam | After every edit, the digest of `With(rect)` equals `Build(all)`; after undoing all, equal to the original; the same with the input order reversed |
| N-D12 | `TheSameQuery_GivesTheSameRoute` | Symmetric obstacle centred on the start-goal line | 100 repeats x 3 input orders x fresh or reused scratch: identical corners, expansions and probe counts; the tie resolves to the asserted lower-`(j, i)` side |
| N-D13 | `TheBudget_EndsASearch_Deterministically` | Goal inside a sealed 60 x 60 m ring (57,600 nodes, so probes are inconclusive) | `Unreachable(Budget)` at exactly 16,000 expansions, every run |
| N-D14 | `TheEnds_Snap_ByDistanceThenIndex` | Start 360 mm from a wall; goal inside a rock; goal out of reach | Snapped by `(d², j, i)`; `GoalBlocked` when nothing is within 2 m |
| N-D15 | `IntegerGeometry_EqualsSeparation` | 20,000 LCG points and radii; 5,000 LCG segments | `PointClear == (Separation(p, R) is null)` for boxes and circles; `SegmentClear` true implies no `Separation` hit at 10 mm samples, and false implies a hit within 1 mm of the reported contact |
| N-D16 | `EveryLatticeEdge_IsWalkableByTheBody` | Random cluttered field, all classes | Every walkable 8-edge, sampled every 10 mm, has `Separation(·, r) is null` (bound 358.8 mm for person) |
| N-D17 | `TheFollower_OpensTheNearestDoorInReach` | Two closed gates on the segment; keys swapped between runs | `OpenGate` only within 1,600 mm; the same door chosen whatever the keys |
| N-D18 | `EachReplanTrigger_FiresOnItsCondition` | A table of the 8 reasons | Each fires exactly on its condition and throttle, in order |
| N-D19 | `EditCheck_Rules` | Synthetic | Refused: the wall sealing a hut; burying a protected point; a door onto a wall. Allowed: a U-shaped hut; a wall with a 1.6 m gap; an edit next to an already-sealed pocket |

### 10.3 World (internals)

- **N-W1 `TileKeys_AreCellKeys`:** the tile-to-`CellKey.OfWorld` mapping for the 4 cells, points on x = 100,000 and z = 100,000, and `r_neg1_0`.
- **N-W2 `TheNavigationSlice_HasOneOwner_AndBuildingPublishesNothing`:** `EveryStateSlice_HasExactlyOneOwningSystem` stays green (`tests/Application.Tests/SessionTests.cs:150-160`); construction publishes no `NavigationRebuilt`.
- **N-W3 `TwoSimulations_ShareNoNavigationState`:** two worlds in one process with different edits keep distinct grid digests, scratch and counters.

### 10.4 Application (real content)

| # | Name | Script | Assertions |
|---|---|---|---|
| N-A1 | `TheHollow_EveryProtectedPointIsReachable` | Real content, person, opener, fold lifted | Every NPC site, container, station, door approach and location centre is in the spawn's flood; lodge and smithy doors 4 / 2 / 2 lanes; fence gap 3 person lanes; beam line sealed for a standing person. It is the Application twin of lint NAV006 |
| N-A2 | `TheAssignedNpc_WalksIntoAHutStraddlingTheSeam_AndHome` (**the exit criterion**) | Building commands: a hut at x 97-103, z 125-131 m with its door in the west wall, the station east of x = 100; assign the NPC; tick; unassign | Arrives exactly on the anchor within 1,200 ticks ([model]: Kera's route is 67 m, about 840 walking ticks). `DoorToggled` with the NPC's instance ID for each closed door on the way. `IsClear` every tick. Displacement ≤ 81 mm every tick (no teleport). Crosses x = 100 m inside the hut. Enters and leaves only through the doorway. Unassigned, walks home and retires, body == site exactly. Variant: the door itself straddles x = 100 m |
| N-A3 | `AWallPlacedAcrossTheRoute_IsWalkedAround` | Mid-walk, place a wall across the remaining corners | `NavigationRebuilt` at that boundary; `RoutePlanned(reason: geometry)` on the next NPC tick; never overlaps the wall; arrives |
| N-A4 | `ARemovedWall_OpensTheShorterWay` | Dismantle the piece that forced a detour | `RoutePlanned(geometry)` next tick; new route shorter than the old remainder; arrives earlier than a control world |
| N-A5 | `AnUnreachableGoal_LeavesTheNpcWaiting` (World.Tests) | Send the mover to a point inside the standing fold (`barrier.foldscar_fold`, `ashen_hollow.yaml:195-199`) | `Unreachable`; the body moves 0 mm; one `RoutePlanned` every 40 ticks; once `world.foldscar.steadied` is set, the next retry succeeds and it arrives |
| N-A6 | `MidRoute_SaveLoad_GoesOnTheSame` | NPC mid-doorway; companion in Route mode; save; load | Grid digest equal; both `NavRoute`s equal field by field; `StateDigest` equal after the load and every 50 ticks for 400 ticks; same arrival tick (the pattern of `CompanionTests.cs:285-319`) |
| N-A7 | `TheSeam_SurvivesAReload` | After N-A2's hut: save, load | Grid digest and tile stamps equal; routes planned from each side of the hut identical before and after |
| N-A8 | `APlacementSession_ReplaysToTheSameState` | From a save: PlacePiece, doors, companion orders, assign, dismantle; replay the log at the same boundaries (the pattern of `tests/Application.Tests/DeterminismAndViewTests.cs:49-78`) | Grid digest, every route, rejection log and `NavCounters` equal; replayable `StateDump` 0 differences; `StateDigest` equal (requires building's replay-stable piece IDs, section 13.5 B9) |
| N-A9 | `RepeatedRebuilds_AreIdentical` | Place and dismantle one piece 50 times | The grid digest returns to its first value every time; an identical route each time |
| N-A10 | `Navigation_StaysWithinBudget` | `Stopwatch` in the test, as `tests/Application.Tests/CreatureTests.cs:526-544` does | Full build < 20 ms; one piece's rebuild < 2 ms; `CheckEdit` < 10 ms; the 20 hardest hollow pairs (including west-of-lodge to Renn) `Found` within the cap; ns per expansion and maximum expansions logged; mean plan < 1 ms. The measured rate must keep 16,000 expansions ≤ 4 ms on ASTRAL, else lower `max_expansions` in content (never below 12,000) |
| N-A11 | `TheCompanion_OpensTheLodgeDoor_AfterWaitThenFollow` | Tavar waits inside the lodge; the character leaves and shuts the door; order Follow | Nav mode plans; `DoorToggled` by Tavar's instance ID; within 4 m; 0 `CompanionCaughtUp` (before M7 this script snags) |
| N-A12 | `TheCompanion_FollowsRoundAPlayerWall` | A wall placed across a fresh trail | 0 `CompanionCaughtUp`; held < 300 ticks (C16's measure) |
| N-A13 | `PlacementIsRefused_WhenNavigationWouldBreak` | Real content | A wall across the lodge door approach, "wall in"; the last wall of a doorless hut, "close off"; a station inside a doorless room, "reach its work place" |
| N-A14 | Soak (evidence, not a gate) | The M6 random-walk soak | Catch-ups by reason before and after M7; `snag` should fall towards zero on reachable ground |

### 10.5 Persistence, architecture, content, and the runtime recording

- **Persistence:**
  - N-P1 `Schema13To14_GivesEveryCompanionNoRoute`;
  - N-P2 `ASchema14CompanionWithoutARoute_IsCorrupt_NotDefaulted`;
  - N-P3 a v14 fixture companion with an active 3-corner route round-trips, and `CanonicalState` renders it;
  - N-P4 route decode rejects: an unknown status key, an odd corner array, more than 32 corners, a `None` route with non-zero fields, and a watch rect with min > max.
- **Architecture:** N-X1 `NavDomain_IsIntegerOnly`. A source scan of `src/Domain/Spatial/Nav*.cs` finds none of `double`, `float`, `Math.Sqrt`, `Math.Sin`, `Math.Cos`, `Math.Atan`, `Math.Pow`, `Math.Round`, the `PresentationSource_...` scan style.
- **Content:**
  - N-X2 `LoadAll_Loads_Yaml_Files` gains `config.navigation` (`tests/Content.Tests/ValidationTests.cs:508`);
  - N-X3 every lint NAV001-NAV007 is refused on a bad config, and the shipped content passes all of them, **including Tavar's site at the centre of the fold** (section 13.3).
- **Runtime recording.** A playthrough beat builds the straddling hut, assigns the NPC, and records its walk with the debug overlay on (walkable nodes, gates, the route polyline, the counters). That is RK-14's "runtime recording", with no engine navigation.

**Must stay green, unmodified:**
- C16 (`CompanionTests.cs:119-160`), `FollowWaitFollow_...`, `LeftFarBehind_...`;
- all of `JumpAndCrouchTests`, `CreatureTests`, `FoldscarTests`, `SpatialTests` and `BehaviourMatrixTests`;
- `SixtyCreatures_TickWithinTheBudget`;
- the 200-command replay (`DeterminismAndViewTests.cs:49-78`) and `SavingIsNotAnEvent_AndLoadingPublishesNothing` (`:128-143`);
- `StateDumpTests`.

**Changed:**
- `HisState_RoundTripsThroughASave_...` (`CompanionTests.cs:285-319`) gains `Assert.Equal(before.Route, after.Route)`;
- `CanonicalState`, `HistoricalFixtureTests`, `M2Fixtures` and the `MigrationTests` step lists change with the shared M7 schema bump;
- `ValidationTests.cs:508` gains `config.navigation`.

---

## 11. Route persistence and the save-then-continue test

**Decision: persist the committed route with the mover's body.** It is not a pure function of persisted state:
- a route is planned from the position `P_T` at tick `T`, and its corners and watch window depend on `P_T`;
- after a load at `T + k`, a route re-derived from the current body can choose different corners, so the next `Kinematics.Step` differs and the digest diverges;
- replanning every tick would make it pure, at up to 20 plans per second per mover, with flips between equal-cost alternatives. That is rejected.

M6 made the same choice for the companion's trail and stuck counter, which are persisted so "a loaded world goes on as the saved one would" (`src/World/PlayerState.cs:46-62`; tested at `CompanionTests.cs:285-319`).

```csharp
// src/Domain/Spatial/NavRoute.cs   (namespace UNNAMED.Domain.Spatial)
public readonly record struct NavPoint(long XMm, long ZMm);
public readonly record struct NavRect(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm);
public enum NavRouteStatus { None, Active, Unreachable }

public sealed record NavRoute(NavRouteStatus Status, long GoalXMm, long GoalZMm, ImmutableArray<NavPoint> Corners,
                              long PlannedTick, ulong Stamp, NavRect Watch)
{
    public bool Partial { get; init; }
    public static NavRoute None { get; } = new(NavRouteStatus.None, 0, 0, ImmutableArray<NavPoint>.Empty, 0, 0, default);
    public string? Problem();   // None => canonical zeros; Active => 1..32 corners; Unreachable => 0 corners; Watch min <= max
}
```

- **`Watch`** is the planning window in mm, not node indices, so a later change of `node_m` in content cannot make a saved window meaningless.
- **`Stamp`** is `WindowStamp(Watch)`: the first 8 bytes of SHA-256 (`CanonicalHasher`, `src/Domain/CanonicalHasher.cs:35`, `:41`) over `"unnamed.nav-window/v1"` and, for each tile meeting `Watch` in `(Tz, Tx)` order, `Tx, Tz, tile.Stamp`.
- **A tile's `Stamp`** hashes `"unnamed.nav-tile/v1"`, the config digest (node, margin, classes, agent height), `Tx, Tz`, and every input in canonical order: kind, shape, geometry, height, clearance, openable. **It never includes an instance ID**, so a replay that mints different piece ULIDs produces equal stamps.
- **Consumed corners are removed**, so `Corners[0]` is always the current target. There is no cursor.

**Why exact continuity holds.** At a tick boundary, the loaded world and the continuing world have:
1. equal grids, because the grid is a pure function of content, placed-piece records and nothing else (section 5.3; N-A7);
2. equal door and barrier flags, and placed-door states (persisted);
3. equal mover records: body, phase, goal, `StuckTicks`, `NavRoute` (persisted);
4. equal other bodies, which are persisted or derived from content;
5. the same tick.

`NavFollower.Next`, `NavSearch.Plan` and every trigger are pure functions of these. Scratch contents never influence a result, counters are never read, and there is no per-tick budget in M7. So tick `T+1` is identical, and by induction every later tick. N-A6 asserts it.

**Digest coverage.**
- The companion's route enters `PlayerRecord.Digest` after its trail (`src/World/PlayerState.cs:359-364`) as: status key, goal, planned tick, stamp, watch, partial, corner count, then every corner. It shares M7's bump of the tag from `unnamed.player/v9` to `v10` (`:328`).
- The NPC's route enters whichever digest covers the assignment record (section 13.6).

**Doctrine reconciled.** SYSTEMS:443 forbids "persisting live AI, pathing, or animation state". M6 already persisted the trail. M7 amends the line to: "search state, grids and caches are never persisted; a mover's committed route is mover state, saved with the body it moves (precedent: the schema-12 trail)". Also:
- S-25 "Transient: path following state" becomes "persisted route";
- ARCHITECTURE:164's "cached pathfinding ... rebuilt on load" stays true: the grid and search are rebuilt; the route is not a cache.

---

## 12. Doors, barriers and the per-agent capability model

### 12.1 How doors enter traversability

| Element | Source | Grid layer | Openable | Blocks planning when | Blocks `Kinematics` when |
|---|---|---|---|---|---|
| Authored door (`door.longhouse`, `door.forge_shed`) | `Layout.Doors` + `world.*` flag (`Systems.cs:41`) | gate (in `ClosedFit` only) | yes (no locks in M7) | closed and the agent does not open doors | closed (unchanged) |
| Placed door leaf | Building: a door piece's leaf box and its open state | gate | yes | same | closed (building joins closed leaves to the structure blockers, B2) |
| Placed door frame (jambs, lintel) | Building: separate boxes | solid | — | always | always |
| Barrier (`barrier.foldscar_fold`) | `Layout.Barriers` + flag | gate | never | standing | standing (unchanged) |

- **A toggle never rebuilds.** State is read at query time.
- **Openers plan through doors in any state.** Non-openers and barriers read the state.
- **Opening.** `internal sealed record OpenDoor(string DoorKey, string NpcId) : InternalCommand`, handled by `InteractionSystem`, which "owns no state" and "asks the owner" (`Systems.cs:236-273`):
  - It checks reach from the NPC's body: `ClosedFootprint.DistanceTo(body) ≤ InteractReachMm`, as `Handle(InteractCommand)` does for the player (`:259-262`).
  - An already-open door is accepted as a no-op.
  - An authored door dispatches `SetWorldFlag(cell, flag, 1)`, exactly as the player's interaction does (`:269`).
  - A placed door is forwarded to the building system's door command.
  - It publishes the existing `DoorToggled(npc.InstanceId, key, true, tick)` (`src/World/Runtime/Events.cs:25`; `Actor` is already an `EntityId`, and NPC instance IDs are derived and stable, `Social.cs:121-125`). Presentation's door view follows as it does today.
- **A refusal** (out of reach, or unknown) is returned; the mover holds and the stuck replan follows.
- **M7 agents never close doors.** They leave them open (the owner question, section 18).
- **Known edge case, not new.** The character can close a door on an NPC standing in the doorway, because the refusal checks only the character's own body (`Systems.cs:266-267`). `Kinematics` then pushes the NPC out by the nearest face. Building should extend the refusal to every body for authored and placed doors (B8). Navigation tolerates either.

### 12.2 Capability model

```csharp
public readonly record struct NavAgent(int ClassIndex, bool OpensDoors);
// ClassIndex = the smallest configured class whose radius >= the body radius; a body larger than every class is not navigable (lint)
```

| Agent (M7) | Body radius | Class | OpensDoors | Height | Jump / crouch |
|---|---|---|---|---|---|
| Assigned NPC | 350 (`base_speeds.yaml:10`; NPCs use the player's rules, `Companions.cs:567`) | person (0) | **true** | stands 1.8 m | never |
| Companion | 350 | person (0) | **true** | stands 1.8 m | never |
| Placement validation | 350 | person (0) | true | — | — |
| Creatures (not in M7) | 350-550 | 0-2 | false | 1.8 m (as `Kinematics` treats them, `Creatures.cs:829`) | — |

Reserved and not built: a height class (jump/crouch), an access set (keys, ownership, faction access), and "closes behind". Each is additive (section 9).

---

## 13. Code placement and the contracts offered

### 13.1 Domain: `src/Domain/Spatial/` (namespace `UNNAMED.Domain.Spatial`, integer only)

The namespace is exactly `UNNAMED.Domain.Spatial`, so `ViewsAndEvents_HaveNoPublicSetters` scans the new records (`ArchitectureTests.cs:134-155`). There are no sub-namespaces.

| File | Contents |
|---|---|
| `NavConfig.cs` | `NavConfig(long NodeMm, long MarginMm, long AgentHeightMm, ImmutableArray<NavClass> Classes, NavLimits Limits)`, `NavClass(string Id, long RadiusMm)`, `NavLimits` (window, expansions, probes, corners, snap radii, seal limit, edit window, follower thresholds, retry and replan ticks), `Problem()` (NAV001-NAV005, NAV007), `RpMm(k)`, `ClassFor(radius)`, `Digest()` |
| `NavGeometry.cs` | `FitAt`, `PointClear`, `SegmentClear` (with `Int128`), `BoundsFit`, `DistanceSquaredTo(footprint, point)` |
| `NavInputs.cs` | `enum NavInputKind { Solid, Door, Barrier }`; `NavInput(NavInputKind Kind, Blocker Shape, string? GateKey)`; the canonical comparer: AABB `(MinX, MinZ, MaxX, MaxZ)`, kind, shape tag, height, clearance, then the gate key (a final tie-breaker only; overlap validation makes equal geometry impossible) |
| `NavTile.cs` | `NavTileKey(long Tx, long Tz)` ordered `(Tz, Tx)`; `NavTile` (immutable class): `Key`, `SolidFit` and `ClosedFit` (`ImmutableArray<byte>`, 160,000 each, local index `lz·400 + lx`), `Inputs`, `Gates`, `Stamp`; `Restamped(rect, inputs)` (copy-on-write) |
| `NavGrid.cs` | `NavGrid` (immutable class): `Config`, `Bounds`, `ImmutableSortedDictionary<NavTileKey, NavTile> Tiles`; `Build(config, bounds, tileKeys, inputs)`; `With(changedFootprints, inputs)`; `Walkable(i, j, agent, gateState)`; `WindowStamp(NavRect)`; `Digest()` (lazy: tests and debug only) |
| `NavScratch.cs` | `NavScratch` (a class, one per owner, never static): `int[] g`, `int[] stamp` (generation), `byte[] dir`, a heap of `(int f, int h, int idx)` bounded at 128,001 entries, `int[] queue` (16,384) |
| `NavSearch.cs` | `NavPlan Plan(NavQuery q, NavAgent a, NavPoint from, NavPoint to)`; `enum NavOutcome` (section 7.5); probes; A*; `StringPull` |
| `NavRoute.cs` | `NavPoint`, `NavRect`, `NavRouteStatus`, `NavRoute` (section 11) |
| `NavFollower.cs` | `NavStepKind`, `NavStep`, `Next(...)` (section 8.2) |
| `NavEditCheck.cs` | `NavEditVerdict(bool Ok, string? Reason, int NodesFlooded)`; `Check(q, add, protectedPoints)` (V-N1..V-N4) |
| `NavCounters.cs` | `NavCounters` immutable snapshot record (section 14) |

- `NavQuery` bundles `(NavGrid Grid, Func<NavInput, bool> IsGateOpen, NavConfig Config, NavScratch Scratch, NavCounterSink? Counters)`.
- The byte layers are built in a `byte[]` and exposed with `ImmutableCollectionsMarshal.AsImmutableArray`. It is in .NET 8's `System.Collections.Immutable` (verified in the installed reference pack), with no copy.

### 13.2 World runtime

- **`src/World/Runtime/RuntimeState.cs`:**
  - `StateSlice.Navigation`, documented: "Transient: derived from the region layout and the placed structures; rebuilt at start and on every structure change; never saved";
  - `public NavGrid? Navigation { get; private set; }`;
  - `SetNavigation(SliceOwner, NavGrid)`, starting with `Require(owner, StateSlice.Navigation)`.
- **`src/World/Runtime/Navigation.cs` (new):**
  - `internal sealed class NavigationSystem`: `/// Owns: <see cref="StateSlice.Navigation"/>`. Its fields are the scratch and a counter sink (instance fields, so two simulations share nothing).
    - `Build()`: all tiles of `Setup.Layout.CellKeys`; publishes nothing.
    - `Handle(RebuildNavigation, long tick)`: section 5.1.
    - `Follow(NavAgent, NavRoute, NavPoint body, NavPoint goal, int stuckTicks, long tick) → NavStep`.
    - `CheckEdit(IReadOnlyList<NavInput> add) → string?`.
    - `CurrentInputs()`.
    - `View()`.
    - There is no `Tick`.
  - `internal sealed record RebuildNavigation(ImmutableArray<Blocker> Changed, string Reason) : InternalCommand;`
  - `internal sealed record OpenDoor(string DoorKey, string NpcId) : InternalCommand;`
  - Public events (past tense, views and tests only):
    - `NavigationRebuilt(ImmutableArray<NavTileKey> Tiles, int NodesRestamped, string Reason, long Tick)`;
    - `RoutePlanned(string MoverKey, string Outcome, string Reason, int Corners, int Expansions, long Tick)` (`MoverKey` = the NPC definition ID).
  - Public views: `NavigationView(NavGrid Grid, ImmutableArray<NavGateView> Gates, ImmutableArray<NavMoverView> Movers, NavCounters Counters)`, `NavGateView(string Key, string Kind, Blocker Footprint, bool Open)`, `NavMoverView(string NpcId, NavRoute Route, bool Blocked)`.
- **`src/World/Runtime/Simulation.cs`:**
  - `SimulationSetup.Navigation { get; init; } = NavConfig.Default` (the pattern of `Simulation.cs:14-33`);
  - compose `_navigation = new NavigationSystem(_context, _state.Claim(nameof(NavigationSystem), StateSlice.Navigation))` before `_companions` in the composition list (`:116-137`), and pass it to `CompanionSystem` and the assignment owner by constructor, the way `QuestDebugger` receives systems (`:137`);
  - call `_navigation.Build()` right after `_effects.Seed(...)` and before `_npcs.Populate()` (`:140-141`). That is the de facto PERSISTENCE §7.4 step k, after the whole load pipeline, on both new-game and load paths;
  - `Dispatch` arms (`:369-404`): `RebuildNavigation c => _navigation.Handle(c, Now)` and `OpenDoor c => _interaction.Handle(c, Now)`;
  - a get-only `public NavigationView Navigation => _navigation.View();`. A property needs no allow-list edit (`ArchitectureTests.cs:111-131`);
  - **no new `GameCommand`** and no `Step` change for navigation itself. The assignment owner's `Tick` sits between `_companions.Tick` and `_npcs.Tick` (`:326-327`).
- **`src/World/Runtime/Companions.cs`:** `CompanionState.Route`; `Follow` per section 8.3; `Route := None` at the listed resets; `Records()` carries it.
- **`src/World/PlayerState.cs`:** `CompanionRecord.Route { get; init; } = NavRoute.None`; the digest (section 11).
- **`src/World/Runtime/Systems.cs`:** `InteractionSystem.Handle(OpenDoor, long)`; and the effective blocker helper building introduces (B2).

### 13.3 Content

`content/config/navigation.yaml`:

```yaml
id: config.navigation
kind: config
schema: 1
display_key: config.navigation.name
tags: [config]
notes: Domain navigation (M7; owner ruling 1). A 250 mm clearance-class lattice on global indices, tiled per 100 m cell, derived from exactly what Kinematics collides with; nothing here is saved.
node_m: 0.25              # divides the 100 m cell (400) and the 5 m terrain spacing (20); lint: 100 m % node == 0, node even in mm
margin_m: 0.05            # lint NAV003: (r + margin)^2 >= r^2 + node^2 / 2 for every class, so every lattice edge is walkable by the body
agent_height_m: 1.8       # lint: equals config.base_speeds stand_height_m
classes:                  # ascending radius; a body uses the smallest class at least its radius
  - { id: person, radius_m: 0.35 }   # lint: equals config.base_speeds body_radius_m
  - { id: medium, radius_m: 0.45 }
  - { id: large, radius_m: 0.55 }
window_margin_m: 20
window_max_m: 128
max_expansions: 16000     # N-A10 measures ns/expansion; never below 12000 (the hollow's worst real route expands 9709)
probe_nodes: 2048
max_corners: 32
start_snap_m: 1.0
goal_snap_m: 2.0
corner_reach_m: 0.4
arrive_m: 0.3
off_line_tolerance_m: 0.05
replan_min_gap_s: 0.5
retry_s: 2
stuck_replan_s: 1
goal_moved_m: 2.0
blocked_view_s: 10
seal_limit_nodes: 16384   # 1,024 m2; lint (building content): the build area is smaller
edit_window_margin_m: 32
work_anchor_max_m: 64     # lint (assignment content): straight-line distance from the NPC's site
```

`src/Content/NavigationContent.cs` holds `Validate(loader)`, hooked like the others (`src/Content/ContentLoader.cs:170-190`), and `Build(loader) → NavConfig`. `GameSession.Boot` sets `Navigation = NavigationContent.Build(loader)` (`src/Application/GameSession.cs:88-99`).

Lints:
- **NAV001** `node_m` divides 100 m exactly, is even in mm, and is ≥ 50 mm.
- **NAV002** class IDs unique; radii strictly ascending and > 0; a class named `person` exists.
- **NAV003** the soundness inequality for every class.
- **NAV004** `person` radius == `base_speeds.body_radius_m`.
- **NAV005** `agent_height_m` == `base_speeds.stand_height_m`.
- **NAV006** in every region: with barriers **lifted** and doors passable, every `NpcSite`, container, station, door approach point and location centre shares the spawn's person flood.
  - This deliberately allows Tavar's site at the exact centre of the fold (`ashen_hollow.yaml:155`, `:195-199`), as WLD012 does ("An NPC may stand behind a barrier", `src/Content/WorldContent.cs:305`).
- **NAV007** `window_max_m` ≤ 128; `max_expansions` ≤ 20,000 (the scratch bound).

### 13.4 Persistence (schema 14; shares the bump with building and factions)

```csharp
[MessagePackObject]
public sealed class NavRouteDto
{
    [Key("status")] public string Status { get; set; } = "none";        // "none" | "active" | "unreachable"
    [Key("goal_mm")] public long[] GoalMm { get; set; } = new long[2];
    [Key("corners_mm")] public long[] CornersMm { get; set; } = Array.Empty<long>();   // flat x, z pairs; <= 64 values
    [Key("planned_tick")] public long PlannedTick { get; set; }
    [Key("stamp")] public ulong Stamp { get; set; }
    [Key("watch_mm")] public long[] WatchMm { get; set; } = new long[4]; // min x, min z, max x, max z
    [Key("partial")] public bool Partial { get; set; }
}
```

- `CompanionDto.Route` is nullable in the DTO and **required from schema 14**: a missing route throws "corrupt, not defaulted", and decode validates `NavRoute.Problem()` (the posture pattern, `src/Persistence/SectionCodec.cs:399-416`). About 70 bytes plus 16 per corner.
- **Freeze first.** `CompanionDto` is referenced by the frozen `V12.Player` (`src/Persistence/Sections/SchemaV12.cs:6`, `:32`), so the current shape is copied into `V12` (and into the new `V13` player freeze that M7's player changes create) before the field is added.
- `SchemaV13ToV14` gives every companion `route: {status: "none"}`.
- `CanonicalState` renders the route; every older `expected.json` gains `route: none` on its companions and nothing else (review line by line).
- Routes hold no definition IDs and no instance IDs, so the definition-ID pass and the baseline proof are untouched.
- The assigned NPC's record carries the same `NavRouteDto` (section 13.6).
- The grid is never saved, and there is no new section file (a new file would break every older fixture's integrity root, `research/persistence.md` §0 item 2).

### 13.5 Contracts with building (what navigation needs, and why)

- **B1. Read API.** Every placed piece as `NavInput`s, in integer world mm, axis-aligned (90° steps, scope ruling item 3):
  - `Solid` boxes with `HeightMm > 0` and `ClearanceMm = 0`;
  - `Door`: the leaf box, gated, plus the frame boxes as `Solid`;
  - pads and roofs are `None`, absent from every list.

  It must be readable before `_navigation.Build()` in the constructor, which holds if the records are initialised from the loaded world.
- **B2. One effective blocker set.** Placed solids and closed placed leaves must reach every query that authored structures and closed doors reach: every mover's movement, prediction (`src/Presentation/Player/PlayerController.cs:127`), sight, shots, clear view, catch-up. Navigation needs only "the same set as `Kinematics`".
  - **Recommended:** a `SystemContext.StructureBlockers()` helper (closed authored gates, placed solids, closed placed leaves, in canonical geometric order) replacing the nine `ClosedDoors()` call sites (`Systems.cs:83`; `Companions.cs:574`, `:598`; `Creatures.cs:585`, `:618`, `:830`, `:893`; `Combat.cs:570`). Prediction gets it through `DynamicBlockers` (`Simulation.cs:255`), and the three `Walled` copies are covered.
  - **Why not a runtime `WalkSpace`:** `CreatureSystem.Populate` places baseline creatures with `IsClear` against `Layout.Space` (`Creatures.cs:173`, `:191`). If pieces entered that space, a never-diverged creature's home could move after a load, breaking save-then-continue.
  - **Its cost:** dynamic blockers use the full radius while airborne, statics the tucked radius (`Kinematics.cs:189-197`), so a low placed piece is slightly harder to jump than an equal authored one. M7's required walls (≥ 2.4 m) are never jumped (apex 1.15 m). Building makes the final call.
- **B3. Canonical order.** Placed blockers are ordered geometrically, never by ULID, because `Resolve` applies pushes in list order (`Kinematics.cs:189-198`). Navigation itself is order-independent.
- **B4. Rebuild dispatch.** A synchronous `RebuildNavigation(changed footprints, reason)` after every committed place, dismantle or destroy. Never on damage, repair or door toggles.
- **B5. Validation.** `CheckEdit` is called in `PlacePiece` validation after building's own checks and before materials. Its reason text is returned verbatim.
- **B6. Dimensions.**
  - snap step a multiple of 250 mm (0.5 m recommended);
  - doorway clear opening ≥ 1.6 m (navigation guarantees 1.05 m for a person);
  - walkable gaps ≥ 1.05 m;
  - the build area ≤ 1,024 m² (V-N1's seal limit) and clear of the scripted presentation routes (`src/Presentation/Playthrough.cs:47-69`).
- **B7. Roofs and pads are never blockers.** `Crosses`, `IsClear` and sight are height-blind (`Blockers.cs:26`; `Kinematics.cs:170-173`), so a roof blocker would block sight, shots, the companion's clear view and catch-up even with a high clearance.
- **B8. Doors.**
  - A door command reachable from `InteractionSystem` for both `InteractCommand` and `OpenDoor`.
  - The placed door's open state readable at query time.
  - "Cannot close: something is in the doorway" extended from the character's body to every body.
- **B9. Replay-stable piece IDs (recommended).** Runtime IDs come from the wall clock (`src/Domain/EntityId.cs:48-53`; `src/EntityRegistry/EntityRegistry.cs:71-72`). Only NPC and creature IDs are hash-derived (`Social.cs:121-125`; `Creatures.cs:161-166`). A piece ID derived from `(world tick, per-tick placement ordinal, definition, anchor)` through `EntityId.Create` would keep `StateDigest` equal across a replay with `PlacePiece` (N-A8). Navigation never orders by, stores, or hashes piece IDs, so it is correct either way.
- **B10. Anchors.** Each functional piece exposes a use anchor in integer mm, standable at 350 mm, with a facing.

What navigation offers building:
- `CheckEdit`;
- `NavigationSystem.View()`;
- the pure `NavEditCheck.Check` for the preview, on the preview's own scratch;
- the guarantee that a placement which passes cannot seal a space, bury a protected point, or strand a door or work anchor.

### 13.6 Contract with the NPC-assignment owner

- **N1. Persist the mover block** in the assignment record, inside a digest:
  - `NpcId` (an `npc.*` definition ID, through the definition-ID pass like companions);
  - `XMm, ZMm, FacingMdeg`;
  - `Phase` (`going_to_work`, `at_work`, `going_home`);
  - `GoalXMm, GoalZMm, GoalFacingMdeg`;
  - `StuckTicks`;
  - `NavRoute`.

  Recommended home: the player section next to companions (the schema-12 precedent), because non-companion NPC bodies are outside `StateDigest` today (`Simulation.cs:357-364`) and a player record needs no baseline proof.
- **N2.** The work anchor lies within 64 m straight-line of the NPC's site (content lint), which keeps every route inside one 128 m window.
- **N3.** `NpcSystem.Tick` skips an NPC with a mover record, as it skips companions (`Social.cs:153`). The owner's `Populate` re-places the saved body with `PlaceNpc` after `NpcSystem.Populate`, as `CompanionSystem.Populate` does (`Companions.cs:125-143`).
- **N4.** The mover follows section 8.4. It never teleports; exact landing and exact retirement apply.
- **N5.** A companion is never assigned. The trader's wares stay at the site (`Systems.cs:61-64`), which is the assignment design's call.

**Recommended NPC.** Kera. Her walk out of the smithy's closed door exercises "through" a door before she reaches the hut [model: 67 m, 1,182 expansions, 5 corners].

### 13.7 Contract with presentation

- There is no Godot navigation anywhere. The M3 spike's navmesh bake (`src/Presentation/Spike/SpikeScene.cs:159-185`) stays spike-only.
- The walking NPC needs no new view code. `NpcsView` already draws every NPC at its authoritative body each frame and derives stride speed from displacement (`src/Presentation/Greybox/NpcsView.cs:43-49`), as it does for the companion.
- Doors opened by NPCs are drawn from `DoorToggled`, as today.
- **The debug overlay** reads `Simulation.Navigation`. It draws:
  - person-unwalkable nodes within 24 m of the character (a MultiMesh of flat quads);
  - gates coloured by state;
  - each mover's corners as a polyline;
  - the companion's trail;
  - the counters as text.

  It is toggled from the debug menu or a key, never a radial (owner ruling 5).
- **Placement preview.** A read-only query (the `Simulation.Aim` precedent; one allow-list addition owned by building) runs `NavEditCheck.Check` on a **separate scratch and no counter sink**. So the simulation's counters stay equal between a UI-driven run and its replay.

### 13.8 Factions

None in M7. Navigation reads no standing. A future access predicate on gates may express territory **access**, and never hostility (scope ruling item 10; owner ruling 3).

### 13.9 Documents M7's first slice reconciles (navigation's part of rulings 1 and 2)

- **DECISIONS:** record owner ruling 1 (domain-side, deterministic, headless, replayable, derived, seam-independent; Godot never authoritative).
- **WORLD_ARCHITECTURE:** `:137` "navmesh ... Baked" becomes "navigation grid, derived in the domain, never saved"; `:297` "off navmesh" becomes "not standable"; `:414` "per cell, debounced" becomes "the affected rectangle, within the placement command"; `:435` loses its navmesh row; `:469` RK-A2 "solved by construction (N-D9, N-D10)"; §10 gains one line for ruling 2 (agents use `Blocks` at lift 0, so there is no second level).
- **RISK_REGISTER:** RK-14 (`:280-296`) is validated headless (N-D9, N-D10, N-A2, N-A7) plus the playthrough recording, proven in M7; RK-06: one rectangle rebuild per edit.
- **PERSISTENCE** `:83-84` and §2: the grid is rebuilt in the `Simulation` constructor; a committed route is saved with its mover.
- **SYSTEMS:** `:443` and S-25 amended (section 11); S-32 `:356` "navmesh dirty regions" becomes "navigation dirty rectangles".
- **ROADMAP M7** (`:281-285`): "navmesh" becomes "domain navigation grid"; the entry criterion is recorded as met inside M7 (scope ruling item 14).

---

## 14. Instrumentation

`NavCounters` is an immutable snapshot built from a per-system sink. The counters are deterministic work counts: never saved, never in a digest, never read by a decision. Preview queries do not count.

| Group | Counters |
|---|---|
| Builds | `FullBuilds`, `RectRebuilds`, `TilesRestamped`, `NodesRestamped`, `FitEvaluations`, `LastRebuildNodes` |
| Searches | `Plans`, `PlansByOutcome[8]`, `PlansByReason[8]`, `Expansions` (sum), `MaxExpansionsOneQuery`, `HeapPushes`, `ProbeNodes`, `ProbeEnclosed` |
| Following | `Follows`, `PullTests`, `ValidityTests`, `StampChecks`, `OpenGateRequests`, `OpenGateRefusals` |
| Placement | `EditChecks`, `EditRefusalsByRule[4]`, `FloodNodes` |
| Fallbacks | `CompanionNavUnreachable`, `CompanionCatchUpsWhileNav` |

- **Timing** is measured only outside `src/Domain` and `src/World`, which have no `Stopwatch` by convention (`research/sim_core.md` §6.2): in N-A10 and in the presentation performance capture (`--perf`).
- **Events.** `RoutePlanned` carries outcome, reason, corner count and expansions, so a playthrough log can reconstruct every plan.
- **Overlay.** The debug overlay shows the counters next to the grid, gates and routes.

---

## 15. Memory and CPU, with the arithmetic

### 15.1 Ashen Hollow now (200 x 200 m, 4 tiles)

| Item | Arithmetic | Size or time |
|---|---|---|
| Tiles | 4 x 160,000 nodes x 2 bytes | **1,280,000 B (1.22 MiB)** |
| Inputs and gates | 64 blocking structures + 2 doors + 1 barrier (per tile 6 / 13 / 17 / 32 [model]) x about 64 B | < 10 KB |
| Search scratch | 512 x 512 = 262,144 nodes x (4 B g + 4 B generation + 1 B dir) = 2,359,296 B; heap ≤ 8 x 16,000 + 1 = 128,001 entries x 12 B = 1,536,012 B; queue 16,384 x 4 B | **≈ 3.8 MiB**, once per `Simulation` |
| Total per simulation | | **≈ 5.1 MiB** (a two-world test holds two) |
| Full build | Fill 640,000 nodes with 3; bounds strips of about 4 x 800 x 3 nodes; **20,756** footprint evaluations [model] at about 10-20 ns | **< 1 ms** estimated; asserted < 20 ms |
| One 3 m wall piece | 96 nodes x ≤ 5 inputs, plus a copy-on-write of one tile's two 160 KB layers | **< 0.1 ms** |
| Plan: open 30 m | 121 expansions [model] | ≈ 0.03 ms |
| Plan: spawn to Kera's smithy | 301 [model] | ≈ 0.08 ms |
| Plan: Kera to the seam hut | 1,182 expansions, 5 corners [model] | ≈ 0.3 ms |
| Plan: round the beam line | 788, 3 corners [model] | ≈ 0.2 ms |
| Plan: into a hut from its far side | 4,618, 4 corners [model] | ≈ 1.2 ms |
| Plan: west of the lodge to Renn (worst real route) | 9,709, 5 corners [model]; large class 10,320 | ≈ 2.4 ms |
| Plan at the cap | 16,000 | ≈ 4 ms (N-A10 gate) |
| Probes | ≤ 4,096 BFS nodes | ≤ 0.2 ms |
| Follow, per mover per tick | Stamp (≤ 4 tiles), ≤ 8 segments x about 20 inputs, look-ahead, door test | ≈ 10-20 µs |
| `CheckEdit` | 1-3 floods ≤ 16,384 nodes | ≈ 0.5-2 ms, per command |
| Steady state | Plans happen on triggers only (a new goal, geometry, stuck, goal moved ≤ 2 per second for the companion) | **≈ 0.04 ms a tick** for two movers; the 60-creature 0.43 ms (`docs/M3D_STATUS.md:53`) is untouched |

The time per expansion (about 250 ns: a heap push/pop and eight neighbour tests over two byte reads) is an estimate. N-A10 measures it, and `max_expansions` is content, so the cap can be tuned without code.

### 15.2 A 2 km region later (20 x 20 cells, 8,000 x 8,000 = 64M nodes)

- **Everything resident** would be 400 tiles x 320,000 B = **128 MB**. It never happens.
- **Tier-A residency.** Movers act only in tier A (`Creatures.cs:264-265`; `Companions.cs:263-264`). A cell enters A within 140 m and leaves beyond 160 m (`src/Domain/Spatial/Tiers.cs:36-47`; `content/config/simulation_tiers.yaml:7-10`). At most **21** cells lie within 160 m of the player (16 within 150 m) [model], so **21 x 320 KB = 6.7 MB**, plus the fixed 3.8 MiB scratch.
- **Build on promotion.** A forested tile (2,000 trees, about 64 nodes each) is 128,000 evaluations plus the fill, about 1-2 ms (estimate). A cell crossing promotes at most 5 tiles, built one per tick.
- **Searches do not grow with the region.** The window is capped at 128 m and scratch is window-sized. Routes over 88 m wait for the portal layer.
- **Schedules.** At the 60-actor tier-A cap (`docs/WORLD_ARCHITECTURE.md:451`), about 1.5 plans a tick: that is when the per-tick plan budget seam is switched on.
- **Flagged, not nav.** `Kinematics.Resolve` scans every blocker on every pass (`Kinematics.cs:189-198`); the per-tile input list is the ready broadphase when RK-06's 200-piece test needs it.

---

## 16. Implementation order (each slice green and playable)

| Slice | Content | Gate |
|---|---|---|
| N1 | Domain: `NavConfig`, `NavGeometry`, `NavInputs`, `NavTile`, `NavGrid`, `NavScratch`, `NavSearch`, `NavRoute`, `NavFollower`, `NavEditCheck`, `NavCounters` | N-D1..N-D19, N-X1 |
| N2 | Content: `config.navigation`, `NavigationContent`, lints NAV001-NAV007 | N-X2, N-X3 |
| N3 | Runtime: slice, `NavigationSystem.Build`, `View`, `Simulation.Navigation`, debug overlay, over authored content only | N-W1..N-W3, N-A1, the build part of N-A10 |
| N4 | Companion: `Route` in state and record; `Follow` per section 8.3; `OpenDoor`; the schema-14 companion field (with the persistence slice) | N-A11, N-P1..N-P4, C16 and every existing companion test re-run |
| N5 | Building joins: B1-B5 and B7-B8 (building's slice); `RebuildNavigation`; `CheckEdit` | N-A3, N-A4, N-A9, N-A13 |
| N6 | The assigned NPC mover (the assignment slice) | N-A2, N-A5, N-A6, N-A7 |
| N7 | Evidence: N-A8, N-A10 on ASTRAL (RAZER when its window opens), the N-A14 soak, the playthrough recording | M7 status doc |
| N8 | Documents (section 13.9) | — |

---

## 17. Where the candidates disagreed, and the choice made

| Question | minimal | scale | codefit | **Choice and reason** |
|---|---|---|---|---|
| Storage | One flat region array | Tiles per cell, lazy and evictable | Tiles per cell, all built | **Tiles per cell, all built in M7.** Cell-aligned storage is the only one that scales to 2 km without a format change; eager building keeps the read path free of mutation |
| Node value | 2 bytes: class counts, solids and gates | 1-byte clearance in 5 mm units + flags | 1-byte class count + sparse gate-cell list | **Two class-count bytes (`SolidFit`, `ClosedFit`).** Class counts are exact squared-distance comparisons (no `Isqrt`, no unit rounding); the second byte gives a one-read fast path; classes are content, so a new size is a rebuild, not a migration |
| Margin | 100 mm | none at nodes; 100 mm in LOS | 50 mm, derived | **50 mm, derived (C1)**, which also keeps 4 person lanes in the real doors (100 mm leaves 2). Scale's node rule (clearance ≥ r) lets diagonal edges pass 302 mm from a corner |
| Straight-line test | Companion's 3-segment `InClearView` | Integer supercover over node clearance | Exact integer swept circle | **Exact swept circle.** The 3-segment test misses a thin footprint lying between its lines (a 0.2 m jamb or parallel wall); supercover lets the line pass up to 177 mm from the tested node centres |
| Rebuild granularity | Rectangle | Rectangle, per tile | Whole tile + whole-window labels | **Rectangle, one `StampRect` function for both build and rebuild.** It is provably equal to a full build, and 100-900 nodes instead of 160,000 per tile, which matters for the placement preview |
| How a mover learns of an edit | Push `RoutesInvalidated` (NPC only) | Stamp comparison (pull) | Per-tick validity + periodic replan every 2 s | **Tile stamps in the route, plus per-tick exact validity.** Pure and persisted; no fan-out command to several owners (`Simulation.Dispatch` routes one type to one handler, `:369-404`); no periodic churn; removal is noticed on the next tick |
| Unreachable detection | Enclosure probes | Bounded A* + partial | Global run-length labels | **Probes + bounded A\*.** Labels are global, so at 2 km they depend on residency; probes are local and cost ≤ 0.2 ms. No partial routes on budget in M7 (they can lead into dead ends) |
| Placement rule | No new sealed pocket (ring floods) | Local connectivity R1-R3 | Protected anchors share the spawn's label | **No new sealed pocket + protected points + functional access + door approaches, all local.** The spawn-label rule is residency-dependent; "no sealed pocket" is simpler to explain than "connectivity preserved within W" |
| Companion's route | Written into the trail | `CompanionRecord.Route` | `CompanionRecord.Route` | **A separate `Route`.** In the trail, route corners compete with the player's marks for 48 slots, and `Mark` drops the oldest (`Companions.cs:313-315`), which are the corners he needs next |
| Companion opens doors | No | Yes | Yes | **Yes**: the same code as the NPC; it removes a known snag (N-A11) |
| Door cost for openers | none | +500, state-independent | +500 when closed | **None, state-independent**: stable under toggles; a penalty is a seam |
| Expansion cap | 12,000 | 12,000 | 40,000 | **16,000**: the measured worst real route (9,709) with 65% headroom, about 4 ms; 40,000 (about 10 ms) breaks the 4 ms budget |
| Window | endpoints + 20 m, ≤ 128 m | endpoints + 12 m, ≤ 128 m | whole region (11 MB) | **+20 m, ≤ 128 m**: detours round the den rocks (16 x 18 m) fit; scratch is independent of region size |
| Placed solids join | runtime `WalkSpace` | runtime `WalkSpace` | `ClosedDoors()` | **Recommend a `StructureBlockers()` join** (B2), because of the creature-`Populate` load hazard; building decides |

Smaller choices: costs 1000/1414 (codefit; 0.01% ratio error, fewer near-ties) over 10/14 and 250/354; 32 corners (scale; real routes pull to 3-5 [model]); no per-tick plan budget in M7 (scale's is kept as a seam); navigation as a slice like `CellTiers` (codefit, minimal) rather than scale's lazy service; creature return-home declined (all three).

---

## 18. Open issues and the owner question

**Owner question (low priority; the default holds if unanswered).** May companions and the assigned NPC open unlocked doors, including the player's own placed doors, and should they close them behind them?
- **Default:** they open authored and placed doors on their way and **leave them open**.
- Closing behind needs a persisted "door I opened" field and a body-clear close rule; it is a seam (section 9).

**Open issues:**
1. **The per-expansion time is estimated.** The model gives counts only. N-A10 measures it on ASTRAL, and on RAZER when its window opens. If the capped worst case exceeds 4 ms, `max_expansions` drops in content, never below 12,000.
2. **Where placed solids join the blocker set belongs to building** (B2). The recommended dynamic join changes the jump tucked-radius rule for low placed pieces only; the `WalkSpace` alternative needs spawn-disc protection against the creature-`Populate` load hazard.
3. **Replay-stable piece IDs** (B9) decide whether N-A8 can assert raw `StateDigest` equality, or only the replayable dump plus the navigation digests.
4. **The companion's catch-up teleport stays as a last resort.** ROADMAP M6 counts it as a "pathing intervention" (`docs/ROADMAP.md:273`). M7's acceptance routes (N-A2, N-A11, N-A12) require zero; N-A14 measures the rest.
5. **The 88 m single-search span** limits a work anchor to 64 m from its NPC's site until the portal layer exists.
6. **The seal limit (1,024 m²)** bounds the M7 build area; widening the area later (scope item 7) must widen the limit with it.
7. **Agent-agent asymmetries are kept.** The walking NPC blocks the player (the player's obstacles exclude only companions, `Systems.cs:82-86`), and creatures walk through it (`Creatures.cs:830-836`).
8. **The existing `Atan2`/`Sin`/`Cos` cross-machine gap** in facing and creature steering remains (`research/sim_core.md` §6.3). Navigation adds none.
9. **Scope ruling item 5 is an owner question.** If assignment moves to M10, the M7 proof uses the companion (N-A11, N-A12) and a World.Tests mover driven directly (N-A5 style) for the straddling hut. The mover code is the same.

---

## Appendix A — Candidate scoring

Each criterion is scored 0-10.

| Criterion | minimal | scale | codefit |
|---|---|---|---|
| Meets the M7 exit and the rulings | 8 | 7 | 9 |
| Determinism, save and replay correctness | 7 | 8 | 9 |
| Seam handling | 9 | 9 | 9 |
| Dynamic-update correctness and cost | 8 | 8 | 7 |
| Reuse of working Phase-1 systems | 9 | 6 | 8 |
| Future fit without present overbuild | 6 | 9 | 7 |
| Testability | 8 | 8 | 9 |
| Clarity for an implementer | 8 | 7 | 8 |
| **Total (of 80)** | **63** | **62** | **66** |

- **nav_codefit (66), the base.** Its soundness argument (C1), exact integer segment test, integer-only discipline, correct route-persistence argument and the most complete tests make it verifiable against `Kinematics`. It loses points for whole-tile rebuilds plus whole-window labels per edit (about 9 ms), residency-dependent global labels, an 11 MB region-sized scratch, a 40,000-expansion cap (about 10 ms), a periodic replan, and lint NAV007, which rejects the shipped content.
- **nav_minimal (63).** Best reuse and the leanest runtime: the rectangle rebuild with its min-reduction proof, enclosure probes, the sealed-pocket rule, windowed search. It loses points for a flat region raster (128 MB at 2 km), route corners stored in the trail's 48-mark FIFO, the inexact 3-segment test, invalidation pushed to the NPC only, and a companion that never opens doors.
- **nav_scale (62).** Best future fit: cell tiles, residency-independent results, tile stamps, local edit checks, the portal plan. It loses points for node passability without a margin (edges can pass 302 mm from a corner), an inexact supercover LOS, a lazy cache mutated on read paths, a per-tick budget and progress watermark with no M7 need, and an undercounted tier-A residency.

---

## Appendix B — Adversarial check of the candidates' load-bearing code claims

Every claim below was re-read in the snapshot.

### B.1 Verified true (the load-bearing ones)

- **`Kinematics.Step`:** sub-steps ≤ r/2 (`Kinematics.cs:144-145`); up to 4 passes, statics then dynamics in list order (`:183-208`); tucked radius for static blockers only (`:192`); whole-mm rounding and Y = terrain + lift (`:157-159`); step = `dir/length · travel · min(length,1000)/1000` (`:137-141`), which makes exact landing possible. `Blocks` is height-aware (`:166-167`); `IsClear` is height-blind (`:170-173`).
- **Geometry:** `Separation` treats touching as clear (`Blockers.cs:52`, `:102`); `Crosses` treats it as crossing (`:89`, `:123`). `ClosedDoors()` re-reads flags per call (`Systems.cs:48`); `Obstacles()` = closed gates + living creatures + non-companion NPCs (`:82-86`), which prediction reads (`Simulation.cs:255`; `PlayerController.cs:127`).
- **Companion:** trail and stuck counter persisted (`PlayerState.cs:52-62`; `SectionCodec.cs:74-90`) and hashed (`PlayerState.cs:359-364`); goal selection (`Companions.cs:339-356`); displacement headway (`:357-360`); `Order` clears the trail (`:185-187`); `InClearView` = three `Crosses` segments (`:584-598`); `Action`/`TargetKey` not persisted (`PlayerState.cs:46-50`).
- **NPCs:** transient bodies outside `StateDigest` (`Social.cs:88`; `Simulation.cs:357-364`); `Tick` skips companions (`Social.cs:153`); `NpcsView` draws authoritative bodies (`NpcsView.cs:43-49`). Construction order as stated (`Simulation.cs:116-144`); `Dispatch` routes one type to one handler (`:369-404`).
- **IDs:** commands cannot mint IDs deterministically today. Items use `Registry.CreateEntity`, so `EntityId.NewId` (wall clock + crypto RNG) (`Items.cs:657`; `EntityRegistry.cs:71-72`; `EntityId.cs:48-53`); only NPC and creature IDs are hash-derived (`Social.cs:121-125`; `Creatures.cs:161-166`); fresh-ID runs compare through the replayable dump (`StateDumpTests.cs:49-60`); the 200-command replay replays from a save with Move/Interact only (`DeterminismAndViewTests.cs:49-78`).
- **Call sites:** 13 collision/sight uses of `Layout.Space` plus 4 terrain-only in `src/World/Runtime` (minimal's count is right); the nine `ClosedDoors()` sites in B2; `CreatureSystem.Populate` uses `Layout.Space` and homes only (`Creatures.cs:173-191`).
- **Tests and libraries:** static readonly arrays count as mutable (`ArchitectureTests.cs:204-209`); a `Simulation` property needs no allow-list change (`:111-131`); `CompanionDto` is referenced by `V12.Player` (`SchemaV12.cs:6`, `:32`); `ImmutableCollectionsMarshal` and `Int128` exist in the installed .NET 8.0.31 pack. Lodge lanes 425/675/675/425 mm (scale) and 3 fence-gap lanes (codefit) confirmed [model].

### B.2 False, or false as stated

1. **nav_codefit NAV007:** "no `NpcSite` lies within 1 m of any door or barrier footprint". This lint **rejects the shipped content**. Tavar's site (145, 42) is the exact centre of `barrier.foldscar_fold`'s circle `[145, 42, 3]` (`content/regions/ashen_hollow.yaml:155`, `:197`), a placement WLD012 explicitly allows ("An NPC may stand behind a barrier - that is what the fold is for", `src/Content/WorldContent.cs:305`). Replaced by NAV006's "barriers lifted" reachability.
2. **nav_codefit §16 and nav_scale §13.3:** tier-A residency is "at most 4 x 4 = 16 tiles" / "at most 16 tier-A cells". False with hysteresis. A cell stays tier A until more than 160 m away (`Tiers.cs:36-47`: `radius + HysteresisMm` while at or below the tier). With the player at a cell centre, **21** cells lie within 160 m [model]. 16 is the count within 150 m. Scale's derived "36 resident tiles, 11.5 MB" inherits the undercount.
3. **nav_codefit §1:** "Every mover, every wall test and prediction concatenates `ClosedDoors()`". True for movers, walls and prediction, but not for creature spawn placement (`Creatures.cs:173-191`) or `CanStand` (`Systems.cs:140`, `:160`). So "placed solids joined into `ClosedDoors()`" would not reach baseline creature placement. That omission is actually what protects load determinism (B2), but the claim as stated is too broad.
4. **nav_minimal §A3:** "every body in content fits a 1.6 m door in nav exactly when it fits in Kinematics". True only at 1.6 m. With minimal's 100 mm margin, the guaranteed width for the 550 mm class is `2·650 + 250 = 1.55 m`, while `Kinematics` admits that body through any opening over 1.1 m.
5. **nav_minimal §3.1:** "The companion's corners are trail marks ... **No new field**" presented as equivalent to a route. The persistence claim is true, but the mechanism is not route-safe. `Mark` appends the character's steps and drops the **oldest** marks beyond 48 (`Companions.cs:313-315`), and the oldest are the route's first corners, the ones he needs next. A companion lost for more than about `48 − corners` metres of the character's walking loses his route from the front.
6. **nav_scale §8.1 and §8.5 (soundness, stated as sufficient):**
   - "passable implies a true distance of at least r, which is exactly `Separation == null`" is true for **nodes**, not for the **edges** A* then walks. Two nodes each exactly r from a corner allow the diagonal edge's midpoint to pass `sqrt(350² − 177²) ≈ 302 mm` from it.
   - The supercover LOS "each visited node ... clearance ≥ r + margin" guarantees only `r + margin − 177 mm` along the line.
   - Scale's T2 asserts clearance at waypoints only, so these gaps would pass its own test.
7. **nav_codefit §16 / §11.3:** the 40,000-expansion budget is "about 10 ms, then 40 ticks of cooldown", presented as acceptable. It is internally consistent, but it contradicts the 4 ms world-system budget (`docs/WORLD_ARCHITECTURE.md:449`) that the same document cites for scale. The measured worst real route needs 9,709 expansions [model], so 16,000 suffices.
8. **nav_scale §5.1:** the navigation cache "is **not** a `StateSlice` ... lazy builds on a read path would have to carry an owner token". It presents the slice route as unworkable. In M7 nothing needs lazy building (four tiles, built in the constructor), and `CellTiers` is the precedent for a transient derived slice (`RuntimeState.cs:36`). Not false as a statement about lazy builds, but the premise that M7 needs them is.

Nothing else load-bearing was found false. Each candidate's citations of line ranges were accurate to within a line or two.

---

## Appendix C — The judge's scratch model: method and numbers

- **Method.**
  - A Python re-implementation of: the `FitAt` rules (section 7.2) at s = 250, m = 50, classes 350/450/550, height filter `Blocks(b, 0, 1800)`; walkability with gates (section 7.1); snapping, window (+80 nodes), probes (2,048); A* with this document's order, costs, heuristic and `(f, h, idx)` tie-break; greedy exact string pulling.
  - Input: the 64 structures, 2 doors and 1 barrier of `content/regions/ashen_hollow.yaml` at `e10d2c4`.
  - A synthetic seam hut was added for the M7 scenarios: walls 0.4 m at x 97-103, z 125-131, a 1.6 m doorway in the west wall with a door leaf.
  - It lives only in the judge's session scratchpad and is not a deliverable. The numbers are counts, reproducible by the C# tests named beside them.
- **Build.** 20,756 footprint evaluations (inflation 600 mm), input counts per tile c_00_00 6, c_01_00 13, c_00_01 17, c_01_01 32. A 3 m x 0.4 m wall piece dirties 96 nodes (16 x 6).
- **Door lanes.** Lodge and smithy: person 4, medium 2, large 2. Fence gap: person 3.
- **Routes** (person, opener):

  | Route | Expansions | Corners |
  |---|---|---|
  | open (10,10) to (40,35) | 121 | — |
  | den front to den cache | 77 | — |
  | spawn to Kera | 301 | — |
  | beam north to south | 788 | 3 |
  | Kera to (101, 128) | 1,180 | — |
  | Kera to the hut anchor | 1,182 | 5 |
  | east of the hut to its anchor | 4,618 | 4 |
  | west of the lodge to Renn | 9,709 (large class 10,320) | 5 |

- **Probes.** A non-opener aimed into the closed hut: `Enclosed` after 324 nodes. Without the probe, A* exhausted a 12,000 budget from the east side.
- **Heuristic weights** on the worst route: expansions 9,419 at w = 1.0, 8,584 at 1.1, 7,531 at 1.25, 6,151 at 1.5, 4,292 at 2.0. The path cost was unchanged here, but optimality is not guaranteed; w stays 1.0.
- **Edge soundness.** The minimum clearance along every walkable person edge in the lodge area was 413.8 mm (theoretical floor 358.8 mm).
- **Tier A.** The maximum number of cells whose nearest point lies within 150 m of the player is 16; within 160 m it is 21.
