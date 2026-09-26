## 3. Navigation v1

Navigation is a derived, integer, headless part of the authority. It answers where a mover steps next, whether a goal is reachable, and whether a placement would make the world un-navigable. It never writes a body, never refuses a rebuild and never reads faction state. Cross-system names (slices, commands, events, views, helpers, guards G1-G28) are defined in section 6; this section owns the behaviour behind them.

Units: mm (`long`), millidegrees, ticks (20 Hz). A **node** is a lattice point; a **tile** is the nodes of one 100 m cell. **[model]** marks a count from the design's Python re-implementation of these rules over `content/regions/ashen_hollow.yaml` at `e10d2c4` (it reproduces the working paper's 20,756 build evaluations and 9,709-expansion hollow route exactly). **[bench]** marks a time from a scratch C# harness of the same A* on ASTRAL (Ryzen 9 9950X3D), not the implementation. N-A10 re-measures both.

### Decisions

- Movers in M7: the errand NPC (Kera Voss) and the companion's fallback mode. Creatures, the player and static NPCs are never pathed.
- Representation: one global integer lattice at 250 mm, one tile per 100 m cell, two bytes per node (`SolidFit`, `ClosedFit`).
- Planning radius = body radius + 50 mm, which makes every lattice edge provably walkable by the real body.
- Only the `person` class (350 mm) ships; the byte counts classes, so more classes are content-only later.
- Doors and barriers are gates read at query time, never baked into the grid; a toggle never rebuilds.
- Inputs: authored statics from `Setup.Layout.Space`, pieces from `SystemContext.StructureFootprints`; terrain is never read.
- The grid, scratch and counters are never saved; `NavigationSystem.Build()` rebuilds them in the `Simulation` constructor.
- A structure edit rebuilds only the nodes within 600 mm of the changed parts, per touched tile, synchronously at the edit's tick.
- Seams do not exist in the data: node indices are global, and a tile built alone equals the same tile from a full build.
- Search: enclosure probes, then windowed optimal A* (8-connected, no corner cutting, costs 1000/1414, octile heuristic, tie-break `(f, h, idx)`).
- `max_expansions` is 65,536, not 16,000: Kera's real workshop routes need 22,685 and 40,751 expansions [model].
- Routes pull to at most 32 integer corners with an exact integer swept-circle test (`Int128`).
- A mover's committed route is saved with its body: the companion's in `CompanionRecord.Route`, the errand's in `NpcErrandRecord.Route`.
- `NavRoute` is built only through validating factories and compares by value (`SequenceEqual` over corners).
- Replan trigger 7 is `Partial && Corners.Length == 1 && within corner_reach`.
- Navigation returns a target point; the owner builds the `MoveIntent`; `Kinematics.Step` does all collision.
- Movers open unlocked doors they may operate, plan through every door, and never close one; a refused `OpenDoor` adds 1 to `StuckTicks`.
- Companion priority: Direct, then Route, then Trail, then a Nav plan; C16 passes unmodified.
- The errand mover is not tier-gated, never reads conversation state, never teleports, and lands exactly on its goal.
- One navigability rule for placement: `NavEditCheck.Check` (V-N1..V-N4) on the all-gates-passable graph; any newly sealed pocket is refused.
- Instrumentation is `NavCounters` only: deterministic work counts, never saved, never digested, never read by a decision.

### 3.1 Consumers

Today nothing paths. Creatures steer straight and slide (`docs/M3D_STATUS.md:78`); named NPCs stand and turn (`src/World/Runtime/Social.cs:149-164`); the companion follows a persisted breadcrumb trail with a catch-up teleport after 80 ticks without headway or beyond 30 m (`Companions.cs:325-361`, `content/config/companion.yaml:10-11`).

| Consumer | M7 use | Goal | Entry point |
|---|---|---|---|
| Errand NPC (Kera Voss) | **Primary.** The exit "the NPC navigates in, through, and around it" cannot be met by a teleport, and she has no trail | The work anchor (`to_work`); her `NpcSite` (`to_home`) | `NavigationSystem.Follow` from `NpcSystem.Tick` (`Errands.cs`) |
| Companion (Tavar) | **Fallback mode** when his trail gives no goal: no mark in clear view, an empty trail after wait-then-follow (`Companions.cs:186-187`), a wall across the marks | The character | `NavigationSystem.Follow` from `CompanionSystem.Follow` |
| Assignment refusal 10 | Read-only reachability at command time | The work anchor, from the NPC's body | `NavigationSystem.Reachable` |
| Placement validation | Flood queries, not A* | - | `NavigationSystem.CheckEdit` (command and preview), which runs `NavEditCheck.Check` |
| F2 debug view, tests, `--build-shots` | Read-only | - | `Simulation.Navigation` |
| Player | Never: input-driven | - | - |
| Static NPCs (Renn, Sel, Kera at home, Tavar before recruitment) | Never: they stand at their sites | - | - |
| Creatures (chase, search, wander, patrol, flee, return-home) | **Never in M7:** it would reopen `CreatureDto` (frozen since schema 8) and the behaviour matrix, and contradict "searched for, not pathed to" (`docs/M3D_STATUS.md:78`) | - | - |
| Creature charge and lunge | Never: the boar's stun is "ran into something solid" (`Creatures.cs:589-594`) | - | - |

### 3.2 Representation

The owner's four families, scored against Otherreach's constraints: integer-mm state with digest-tested replay and save-then-continue; axis-aligned boxes and circles only (`src/Domain/Spatial/Blockers.cs:41`, `:95`); 1.6 m doorways in 0.4 m walls; 100 m cells, a 200 m region now and 2 km regions in M9; a 4 ms world-system budget (`docs/WORLD_ARCHITECTURE.md:449`).

| Criterion | **A. Uniform grid: tiled integer clearance lattice (chosen)** | B. Graph from cells or samples (waypoints; visibility graph over inflated corners) | C. Steering + local avoidance (today's layer) | D. Other: domain navmesh (constrained triangulation); flow fields |
|---|---|---|---|---|
| Determinism | Integer bytes; order-free `min` stamping; exact integer segment test | Edges need exact tests; tangents need trig | Deterministic; bug-following state must be saved | Needs exact predicates; edits order-sensitive |
| Seams, 100 m cells | Not a case: seams fall between global node indices | Long edges cross cells | None | Stitching or global retriangulation |
| Dynamic building | Restamp 56-108 nodes per piece | Re-sample, re-link, per-radius graphs | Nothing guaranteed | Risky retriangulation; a flow field dies per edit |
| "Unreachable", "seals a room" | Yes: probes and floods | Yes | **No** | Yes |
| Memory | 320,000 B per tile; 1.22 MiB here | Small, grows with density | 0 | Small; 1.3 MB per flow-field goal |
| CPU | Plans on triggers; ~0.1 µs per expansion [bench] | Costly rebuilds | Cheapest | Costly edits |
| 2 km regions, larger worlds | ≤ 21 resident tiles (6.7 MB); window-capped search; portals later | Scales poorly | Unusable | Same portal problem |
| Headless tests | Byte-hashable; tile-alone equals full build | Harder | Easy, weak | Hard |
| Save/load | Only routes saved; rebuild proven equal | Same | Nothing | Harder proof |
| Schedules | Agent-generic mover block and follower | Scales poorly | Unusable | Workable |

**Recommendation: A**, with C kept beneath it (the trail, `Kinematics` sliding, snag and catch-up). A alone is integer-exact, seam-free by construction, updated locally, provably equal to a full rebuild, and checkable against `Kinematics` with the same predicate. C cannot answer "unreachable" or "would this seal a room", which placement validation and RK-14 need. D is the upgrade path if routes ever need sub-lattice optimality; `NavSearch.Plan` and `NavFollower.Next` would not change. Godot navigation has no authoritative role (G4 `PresentationUsesNoGodotNavigation_OutsideTheSpike`).

### 3.3 Resolution and margin

Symbols: `s` node spacing; `r` class radius; `m` margin; `Rp = r + m` the planning radius. A node is walkable for a class when a circle of radius `Rp` at its centre overlaps no footprint; touching is clear, matching `Separation` (`Blockers.cs:52`, `:102`).

- **C1, soundness.** For two walkable nodes `L` apart and any point `q` of a convex footprint, `|A−q|, |B−q| ≥ Rp` implies every point of AB is at least `sqrt(Rp² − L²/4)` from `q`. The diagonal edge (`L = s·√2`) binds: **`Rp² ≥ r² + s²/2`**.
- **C2, doorways.** An opening of clear width `W` leaves a band `W − 2Rp` for node centres; a band of width `w` holds at least `floor(w/s)` centres. The guaranteed-passable width is **`W_min = 2Rp + s`**.
- **C3, tiling.** `s` divides 100,000 mm (seams fall between nodes), the 5 m terrain spacing and the 3 m module, and is even, so centres (`250·i + 125`) are whole millimetres.
- **C4, movement scale.** Movers re-steer every tick (80 mm a walk, 160 mm a run), so a spacing above one tick's travel costs no smoothness.
- **C5, walls.** Wall thickness sets no lower bound on `s`. `FitAt` inflates every footprint by `Rp`, and C1 holds for any convex footprint. A 0.4 m wall or jamb therefore removes a 1.2 m band of nodes (4-5 rows at `s = 250`), and no walkable lattice edge crosses it.

| s (mm) | Minimum m for r = 350 (C1) | Person `W_min` at m = max(50, minimum) | Nodes per tile | Bytes per tile | Divides 100 m / 5 m / 3 m |
|---|---|---|---|---|---|
| 500 | 147.5 | 1,495 (one lane through 1.6 m) | 40,000 | 80,000 | yes / yes / yes |
| **250** | **42.1** | **1,050** | **160,000** | **320,000** | **yes / yes / yes** |
| 200 | 27.5 | 1,000 | 250,000 | 500,000 | yes / yes / no |
| 100 | 7.1 | 900 | 1,000,000 | 2,000,000 | yes / yes / yes |

**Choice: `s = 250 mm`, `m = 50 mm`, `person r = 350 mm`, `Rp = 400 mm`.** C1: `400² = 160,000 ≥ 350² + 31,250 = 153,750`, so the minimum clearance along a diagonal edge is `sqrt(160,000 − 31,250) = 358.8 mm > 350`. It is the coarsest spacing that keeps several lanes through the existing 1.6 m doorways, and it tiles the cell, the terrain and the building module (3000 / 250 = 12).

**Doorway lanes on real geometry [model]:**

| Opening | Clear width | Person node lanes (centres) |
|---|---|---|
| `door.longhouse` (`ashen_hollow.yaml:143`) | 1.6 m | 4: z 127,625 / 127,875 / 128,125 / 128,375 |
| `door.forge_shed` (`:144`) | 1.6 m | 4: z 141,625 / 141,875 / 142,125 / 142,375 |
| Crossing Workshop doorway (100500, 99000) r0 | 1.6 m (x 99,700-101,300) | 4: x 100,125 / 100,375 / 100,625 / 100,875 |
| Smithy fence gap (`:80`) | x 51.4-53.0 | 3: x 51,875 / 52,125 / 52,375 |
| Woundmoss beam line (`:89-91`) | clearance 1.3 m < 1.8 m | 0: sealed for a standing body, as `Kinematics` makes it |

**Classes in content.** Only `person` ships (`classes: [{ id: person, radius_m: 0.35 }]`). `SolidFit` and `ClosedFit` count fitting classes (0..K), so adding a class is a content change and a rebuild, never a migration. The synthetic three-class configuration (350 / 450 / 550) exists only in test N-D6.

**Influence radius.** `NavConfig.InfluenceMm = 600` (constant): a node farther than 600 mm from an input's AABB is unaffected by it. NAV002 keeps every planning radius ≤ 600 mm (any class up to r = 550 mm); a larger class needs a code change. Building keeps openings ≥ 1.6 m and `module_m` a multiple of `node_m`, so lane counts never depend on where a piece lands (§3.15).

### 3.4 Authority

| Aspect | Specification |
|---|---|
| **Reads (authoritative)** | `Setup.Layout.Space` (bounds, authored statics); `Layout.Doors` and `Layout.Barriers` footprints (`RegionLayout.cs:12`, `:28`); gate state **at query time** (`SystemContext.IsOpen`, `IsLifted`, `Systems.cs:41-45`; piece doors through `State.World.Piece(id).DoorOpen`); `SystemContext.StructureFootprints` (`NavFootprint`s in `StructureOrder`); `config.navigation`; `MovementRules`. **Never:** terrain (it never blocks, `Kinematics.cs:159`), bodies, faction state, the conversation, cell tiers, the structure revision |
| **Filter** | An input counts only if `Kinematics.Blocks(b, 0, agent_height_mm)` (`Kinematics.cs:166-167`): movers stand 1.8 m and never leave the ground. The beam (clearance 1.3 m) blocks; pieces (clearance 0) always block; pads and roofs have no parts |
| **Derived** | `NavGrid`: per tile, `SolidFit` and `ClosedFit` bytes, the canonical input list, the gate list and the input stamp |
| **Cached** | The `NavGrid` in `StateSlice.Navigation`, transient like `CellTiers` (`RuntimeState.cs:37`). Scratch is working memory whose contents never influence a result |
| **Invalidates** | Place, dismantle or destroy of a piece with at least one solid or door part, pushed by `BuildingSystem` as `RebuildNavigation` (section 6.2.2) |
| **Does not invalidate** | Door toggles, barrier lifts, damage, repair, assignment, release, errand phase changes, bodies moving, tier changes, saves, loads |
| **Rebuilt** | Every tile of `Layout.CellKeys` in the `Simulation` constructor after `_building.Populate()` (new game and load alike: PERSISTENCE §7.4 step k); on `RebuildNavigation`, the nodes of `Changed ⊕ 600 mm` in each touched tile, with those tiles' input lists and stamps |
| **Saved** | Each mover's `NavRoute` beside its body: `CompanionRecord.Route` (`player.msgpack`, `unnamed.player/v10`); `NpcErrandRecord.Route` (`entities.msgpack`, `unnamed.effective-cell/v2`) |
| **Not saved** | The grid, tiles, input and gate lists, tile stamps (except the window stamp inside a route), scratch, `NavCounters`, rebuild history. PERSISTENCE §2 gains "Navigation grid: rebuilt in the `Simulation` constructor from layout and piece rows; never persisted" |

**Invariant.** No decision reads a counter, a rebuild history, the structure revision (`RebuildNavigation.Revision` is for views only) or scratch contents. Decisions read persisted mover state, current geometry and flags (and the grid, a pure function of them), and the tick (G15, N-A6).

### 3.5 Dynamic building updates

#### 3.5.1 The sequence

Everything runs synchronously inside one command, so a command-log replay reproduces it at the same boundary.

```
Boundary N (DrainCommands, FIFO) - or tick N+1 for a destroy inside _combat.Tick
 PlacePieceCommand -> BuildingSystem.Handle
   checks 1-15 (check 15 = NavigationSystem.CheckEdit, §3.13; read-only)
   ExchangeItems (the refusable step)
   PlacePiece row + StructureSequence            the structure delta
   Rebuild()                                      Space, closed leaves, socket index, StructureFootprints
   Dispatch(RebuildNavigation(Changed, Kind, Revision))       only if the piece has a solid or door part
     NavigationSystem.Handle(c, Now):
       a. inputs = CurrentInputs()                authored statics + authored gates + footprints, canonical order
       b. R = nodes whose centre lies in Changed ⊕ 600 mm; for each tile T meeting R: dirty[T] = R ∩ T
       c. for T in (Tz, Tx) order: T' = T.Restamped(dirty[T], inputs)   copy-on-write of T's two layers
                                   T'.Inputs = inputs whose AABB ⊕ 600 mm meets T's node-centre rect
                                   T'.Stamp  = TileStamp(T'.Key, T'.Inputs)
       d. State.SetNavigation(owner, grid with every T')
       e. publish NavigationRebuilt(tiles, nodesRestamped, kind key, Now)     views and tests only
   PiecePlaced, StructuresChanged
Next follow of each mover (same tick if the edit happened in _combat.Tick; else tick N+1):
   NavFollower.Next: grid.WindowStamp(route.Watch) != route.Stamp -> replan "geometry"
```

- **Dismantle and destroy** take the same path through `RemoveCore` with `Kind = Dismantled` or `Destroyed`; removing inputs only raises `Fit`, so no check runs. A destroyed doorway holding a door is two removals, door first, with two dispatches.
- **Destruction during a step** dispatches with `Now = N+1`; companions and errand NPCs move later in that tick and see the new grid (fixed order, `Simulation.cs:320-333`).
- Door toggles and barrier lifts rebuild nothing (G14). `Handle` never refuses and never dispatches. Construction publishes nothing (N-W2, `ConstructionAndLoad_PublishNoM7Event`).

#### 3.5.2 Rebuild scope

The restamped nodes are those whose centres lie in the parts' union AABB inflated by 600 mm, clipped to each tile it meets: never a whole cell, and a neighbouring tile only for the strip the rectangle actually covers.

| Piece (section 4 catalogue) | Parts AABB | Nodes restamped | Tiles touched |
|---|---|---|---|
| Wall, or doorway (the union of its jambs) | 3.4 × 0.4 m | 18 × 6 = 108 | 1-2 |
| Door leaf | 1.6 × 0.4 m | 12 × 6 = 72 | 1-2 |
| Chest or bench | 1.0 × 0.6 m | 56-63 | 1-4 (a chest on square (33, 33) can lie within 600 mm of both seams) |
| Pad, roof | none | 0: no dispatch | 0 |

Cost per footprint change: ≤ 540 `FitAt` evaluations, one copy-on-write of 320,000 B per touched tile, and a re-filter and stamp per tile; estimated 0.05-0.2 ms, asserted < 2 ms (N-A10).

#### 3.5.3 Why a rectangle rebuild equals a full build

`SolidFit[n] = min(BoundsFit(n), min over solid inputs of FitAt(I, n))`; `ClosedFit[n]` adds the gate inputs. `min` is order-free, and an input farther than 600 mm contributes `K` (the class count), the identity of `min`, so only nodes in `Changed ⊕ 600` can change, and each is recomputed from **all** current inputs. There is one stamping function, `StampRect(tile layers, rect, inputs)`; `Build` is `StampRect` over each whole tile after a fill with `K`. N-D11 and N-A9 assert byte equality after hundreds of edits.

### 3.6 Cell seams

**Mapping.**
- Node `(i, j)` is global: `i = FloorDiv(xMm, 250)`, centre `x = 250·i + 125`; likewise `j` from `zMm`. `FloorDiv` is integer floor division in `NavGeometry` (Domain cannot use World's `WorldMath`).
- A tile is `NavTileKey(long Tx, long Tz)` with `Tx = FloorDiv(i, 400) = FloorDiv(xMm, 100,000)`. That equals `Rx·20 + Cx` for cell `c_Cx_Cz` of region `r_Rx_Rz`, because cell origins are `(Rx·2,000 + Cx·100) m` (`Systems.cs:498-499`). `NavigationSystem.Build` parses `Layout.CellKeys` with `CellKey.Parse` to get the tile keys.
- Local index in a tile: `(j − Tz·400)·400 + (i − Tx·400)`.
- `NavTileKey : IComparable<NavTileKey>`, ordered (Tz, Tx). `NavGrid.Tiles` is an `ImmutableArray<NavTile>` in that order, looked up by binary search; no sorted dictionary is built (G24).
- N-W1 pins the mapping against `CellKey.OfWorld` (`src/World/Coordinates.cs:93-96`) for the four hollow cells, points on x = 100,000 and z = 100,000, and the negative region `r_neg1_0`.

| Hazard | How the design removes it |
|---|---|
| Duplicated nodes | Centres sit 125 mm inside a tile; seams are multiples of 100,000 mm; every node is in exactly one tile. No border nodes, no overlap margin |
| Ambiguous coordinates | x = 100,000 mm is node 400, tile 1, as `CellKey.OfWorld(100.0, z)`. Integer `FloorDiv`, never `double` metres |
| Disconnected borders | Neighbours are `(i ± 1, j ± 1)` in global indices. A*, probes, floods and `SegmentClear` never see a tile boundary |
| Order-dependent rebuilding | Values are a `min` over inputs within 600 mm (§3.5.3): any edit or tile order gives identical bytes and stamps |
| Straddling footprints | One input (footprints are world mm, never split). It is in the list of every tile its AABB ⊕ 600 meets; each tile stamps its own nodes. A segment test unions the lists of the tiles it touches; duplicates are harmless (AND) |
| Valid before a reload, invalid after | A tile is a pure function of its key and the global inputs: a rebuild after a load, or after eviction in M9, is byte-identical (N-D9, N-A7) |

**RK-14 for a domain representation** fails if no path is found (N-D10, N-A2), if the path exits and re-enters the structure (N-D10, N-A2), or if it is valid before a reload and invalid after (N-D9(b), N-A7). Its "overlap margin" is the 600 mm influence radius; neighbour dirtying happens exactly when an edit's rectangle crosses a seam. Straddling is proven, not constrained; the runtime recording is section 13's seam recording.

### 3.7 Pathfinding

#### 3.7.1 Agent and walkability

```csharp
public readonly record struct NavAgent(int ClassIndex, bool OpensDoors);   // every M7 mover and check: (0, true)
```

Node `n` is walkable for agent `(k, opens)` when:

```
SolidFit[n] > k
&& ( ClosedFit[n] > k                                        // fast path: no gate within Rp_k
     || every gate g in tile(n).Gates with FitAt(g, n) <= k is passable )
passable(g) = g.Kind == Barrier ? isGateOpen(g)              // nobody opens a barrier; lifted = open
            : opens ? true                                   // openers plan through doors in any state
            : isGateOpen(g)                                  // a non-opener reads the state (seam; no M7 mover)
```

- Openers plan independently of door state, so a toggle never changes an opener's route.
- `BoundsFit(n)` counts the classes for which `x − Rp ≥ MinX && x + Rp ≤ MaxX` (and z), the `IsClear` bounds rule (`Kinematics.cs:171-172`) at `Rp`.
- Obstacle inflation is built into `FitAt`; there is no dilation pass.

#### 3.7.2 Integer geometry (`NavGeometry`, pure)

- **`FitAt(input, node)`** counts ascending classes, stopping at the first that fails. Box `[x0, z0, x1, z1]`: `dx = max(x0 − px, 0, px − x1)`, `dz` likewise; class k fits iff `dx² + dz² ≥ Rp_k²`. Circle `(cx, cz, Rc)`: fits iff `(px−cx)² + (pz−cz)² ≥ (Rp_k + Rc)²`. Evaluated only inside the inflated AABB; every term is below 10¹².
- **`PointClear(p, R, input)`**: the same comparisons at any radius. It equals `Separation(p, R) is null` exactly, because `Separation` squares `long`-valued inputs in `double` below 2⁵³ (N-D15).
- **`SegmentClear(A, B, R, input)`**, the exact swept circle; touching is clear throughout.
  - Circle `(C, Rc)`, `Q = R + Rc`, `d = B − A`, `w = C − A`, `dot = w·d`, `len2 = d·d`. `len2 == 0`: the point test. `dot ≤ 0`: clear iff `|w|² ≥ Q²`. `dot ≥ len2`: clear iff `|C − B|² ≥ Q²`. Else clear iff `|w|²·len2 − dot² ≥ Q²·len2`, in **`Int128`** (products reach about 10²¹ for 128 m segments).
  - Box: the swept obstacle is the open union of `[x0−R, x1+R] × [z0, z1]`, `[x0, x1] × [z0−R, z1+R]` and four corner discs of radius R. A segment meets an open rectangle iff its AABB overlaps it strictly on both axes and the four corner values `(az−bz)(cx−ax) + (bx−ax)(cz−az)` are strictly mixed in sign; the corner discs use the circle test with `Rc = 0`.
  - Candidate inputs: those in the tiles meeting the segment's AABB ⊕ R whose own AABB ⊕ R meets it.
- `BoundsFit`, `DistanceSquaredTo(footprint, point)` and `FloorDiv` complete the file. There is no `double`, `float`, `Math.Sqrt` or transcendental anywhere in `Nav*.cs` (N-X1).

#### 3.7.3 The query pipeline: `NavSearch.Plan(NavQuery q, NavAgent a, NavPoint from, NavPoint to) → NavPlan`

`NavQuery` bundles `(NavGrid Grid, Func<NavInput, bool> IsGateOpen, NavConfig Config, NavScratch Scratch, NavCounterSink? Counters)`.

1. **Snap the start.** Nodes within Chebyshev radius 4 (`start_snap_m` 1 m), ordered by (squared distance from the start to the centre, `j`, `i`); the first walkable one with `SegmentClear(start, node, r − 50)` wins (a body pressed on a wall can sit a few mm inside `r` after `Resolve`). None: `StartBlocked`.
2. **Snap the goal.** The same over radius 8 (`goal_snap_m` 2 m) with `SegmentClear(node, goal, r)`. None: `GoalBlocked`. A standable goal (`PointClear` at r against solids and impassable gates, in bounds) is the exact final corner; otherwise the snapped centre is.
3. **Window.** The snapped nodes' AABB inflated by `window_margin_m` (20 m = 80 nodes), clipped to the grid. A span over 352 nodes (88 m) on either axis is `TooFar` (the window is at most 512 nodes, 128 m).
4. **Enclosure probes.** A 4-connected BFS from the goal node, then the start node, each limited to `probe_nodes` (2,048) and the window. Reaching the other endpoint stops probing. A frontier that empties below the limit without touching the window border proves `Enclosed` with zero expansions; anything else is inconclusive. (4-connectivity is the A* partition: a legal diagonal implies both orthogonal steps.)
5. **A\*** (§3.7.4). `Found` when the goal node is popped; an empty open set is `Exhausted`, or `NotInWindow` if a window-border node was closed; `max_expansions` reached is `Budget`.
6. **String pulling** (greedy, exact). `cand = [start, n₀, …, nₖ, goal-if-standable]`; from anchor `a`, extend `k` while `SegmentClear(cand[a], cand[k+1], r)` holds against solids and impassable gates, emit `cand[k]` as the next anchor. Lattice edges are clear by C1, so every emission advances. Over `max_corners` (32): keep 32, mark `Partial`.
7. **Result:** `NavPlan(NavOutcome Outcome, ImmutableArray<NavPoint> Corners, bool Partial, NavRect Window, int Expansions, int ProbeNodes)`; `Found` maps to `NavRoute.Active`, every other outcome to `NavRoute.Unreachable`.

#### 3.7.4 Costs, heuristic, neighbours, ties

- **Graph.** 8-connected. A diagonal is allowed only if both orthogonal neighbours are walkable (no corner cutting).
- **Costs.** `int`: 1,000 orthogonal, 1,414 diagonal. No door or proximity penalty in M7. `g` fits in `int` (262,144 × 1,414 = 3.7·10⁸).
- **Heuristic.** `h = 1000·max(|di|,|dj|) + 414·min(|di|,|dj|)`: exact on empty ground, admissible and consistent. The search is optimal A* (weight 1).
- **Neighbour order.** E(+1,0), N(0,+1), W(−1,0), S(0,−1), NE(+1,+1), NW(−1,+1), SW(−1,−1), SE(+1,−1); "N" is +Z. The table is a `private static readonly ImmutableArray<(int Di, int Dj, int Cost)>`, which `StateAssemblies_HoldNoStaticMutableState` accepts (`ArchitectureTests.cs:204-209`); a `static readonly int[]` would fail it.
- **Open set.** Our own binary min-heap of `(f, h, idx)` compared lexicographically, with `idx = (j − wj0)·ww + (i − wi0)` (row-major inside the window). This is a total order over distinct nodes, so the pop sequence depends on the data alone. .NET `PriorityQueue` is not used (its order among equal priorities is unspecified). Stale entries are skipped by a closed bit.
- **Relaxation.** Only on a strictly smaller `g`; the parent is a direction byte, so the first-found parent wins among equals.

#### 3.7.5 Outcomes: always decided by counts, never by time

| Outcome (key) | Meaning | Route |
|---|---|---|
| `Found` (`found`) | Goal node popped | `Active` (`Partial` if truncated at 32) |
| `StartBlocked` (`start_blocked`) | No walkable node within 1 m reachable in a straight line | `Unreachable` |
| `GoalBlocked` (`goal_blocked`) | Nowhere to stand within 2 m of the goal | `Unreachable` |
| `Enclosed` (`enclosed`) | A probe proved the goal or the start sealed in | `Unreachable` |
| `TooFar` (`too_far`) | Endpoint span over 88 m on an axis | `Unreachable` |
| `Exhausted` / `NotInWindow` (`exhausted` / `not_in_window`) | Open set emptied inside the window / touching its border | `Unreachable` |
| `Budget` (`budget`) | `max_expansions` reached | `Unreachable` |

What a mover does next is its owner's rule (§3.11, §3.12). No partial route is ever followed on `Budget`: it could lead into a dead end.

#### 3.7.6 Replan triggers

Evaluated in `NavFollower.Next` at each mover tick, in this order; the first match replans with that reason. Every condition reads only persisted route fields, persisted mover state, current geometry and flags, and the tick.

| # | Reason key | Condition | Throttle |
|---|---|---|---|
| 1 | `none` | `route.Status == None` | immediate |
| 2 | `retry` | `route.Status == Unreachable`: replan when `tick − PlannedTick ≥ retry` (40 ticks); **otherwise return `Unreachable` at once and evaluate nothing further** (hold) | 40 ticks |
| 3 | `goal_moved` | `dist²(goal, route.Goal) > goal_moved²` (2 m) | `tick − PlannedTick ≥ 10` |
| 4 | `geometry` | `grid.WindowStamp(route.Watch) != route.Stamp` | immediate |
| 5 | `blocked` | a remaining corner-to-corner segment fails `SegmentClear(·, ·, r)` against solids and the gates the agent cannot pass | immediate |
| 6 | `stuck` | the owner's `StuckTicks > 0 && StuckTicks % 20 == 0` | immediate |
| 7 | `partial` | `route.Partial && Corners.Length == 1 && dist²(body, Corners[0]) ≤ corner_reach²` (400 mm) | immediate |
| 8 | `off_line` | `SegmentClear(body, Corners[0], r − 50)` fails against solids and impassable gates (a body shouldered it off the line) | `tick − PlannedTick ≥ 10` |

- In play only the companion's goal moves; a running character out of view triggers `goal_moved` about every 0.6 s.
- Trigger 2's hold means an errand holding `Unreachable` (whose `StuckTicks` rises every tick, §3.12) retries every 40 ticks, not every 20.
- Trigger 7 can fire: the follower never drops the last corner, and a partial route's last corner is not the goal, so arriving at it replans from there. `NavRoute.Problem()` is unchanged.

#### 3.7.7 The expansion cap

The working papers set `max_expansions` to 16,000 from the hollow's worst authored route (9,709 [model]). The Crossing Workshop was never measured against it. Measured now [model; bench on ASTRAL]:

| Route (person, opener) | Expansions | Corners | Length | Time [bench] |
|---|---|---|---|---|
| Kera's site (61.6, 139.6) → bench anchor (100.75, 103.5), workshop built | **22,685** | 7 | 80.45 m | 3.1 ms |
| Anchor → site (the walk home) | **40,751** | 7 | 80.54 m | 4.6 ms |
| Anchor → site after workshop step 9's wall at x = 96 m | 40,639 | 6 | 81.3 m | - |
| West of the lodge (30, 128) → Renn | 9,709 | 5 | - | 0.8 ms |
| Kera's site → (100.75, 96.0), no workshop | 1,179 | 4 | 76.9 m | - |

Both workshop routes exceed 16,000: the smithy's west door and the workshop's south doorway put a detour at each end, so optimal A* expands an ellipse of open ground. With 16,000, assign refusal 10 would answer "Kera Voss cannot get there" and the exit scene would fail. **Decision: `max_expansions: 65536`** (1.6 × the worst route), search kept optimal: a heuristic weight of 2 cuts the walk home to 10,896 expansions but gives up optimality and the predictable west-side route. A plan at the cap costs about 6-9 ms [bench], so a planning tick can exceed the 4 ms world-system budget by one plan; plans happen only on triggers (Kera 1-3 per trip; the companion typically under 1 ms each), bounded by §3.18. The scratch is sized to the cap (§3.14).

#### 3.7.8 Floating point

Rasterisation, probes, A*, floods, pulling, stamps and route validation are integer. The only floating point on a mover's path already exists: the owner's intent direction (`Companions.cs:558-568`), `CombatRules.FacingTowards`' `Atan2` for facing (recorded in `M7_STATUS` as a one-machine-deterministic path), and `Kinematics.Step`. Navigation adds none.

### 3.8 Local movement

| Layer | Function | Owner | M7 change |
|---|---|---|---|
| Global route | `NavSearch.Plan`: snap, probes, A*, pull | Domain, pure | new |
| Route following | `NavFollower.Next` → `NavStep`: a target point, `OpenGate`, `Arrived` or `Unreachable` | Domain, pure | new |
| Intent and facing | The owner builds a `MoveIntent` towards the target | `CompanionSystem.Step` (unchanged); the errand mover (same pattern, plus exact landing) | errand only |
| Collision and bodies | `Kinematics.Step(body, intent, Setup.Movement, _context.Space, obstacles, TickMs)` | Domain | **none** |

- Bodies are never in the grid: `Kinematics` push-out and sliding handle them, then the stuck replan, then (companion only) the Phase-1 catch-up. There is no crowd avoidance: M7 has two movers.
- Both movers' obstacles are `SystemContext.PersonObstacles(npcId)` (closed doors and closed piece leaves, living creatures, the player, every other NPC), moved verbatim from `Companions.cs:570-581`.
- Asymmetries are unchanged: an errand NPC blocks the player (`Systems.cs:82-86` exclude only companions); creatures pass through non-companion NPCs (`Creatures.cs:830-836`).

**`NavFollower.Next`:**

```csharp
public enum NavStepKind { Walk, Arrived, OpenGate, Unreachable }
public sealed record NavStep(NavRoute Route, NavStepKind Kind, NavPoint Target, string? GateKey,
                             string? ReplanReason, NavOutcome? Outcome, int Expansions);
public static NavStep Next(NavQuery q, NavAgent agent, NavRoute route, NavPoint body, NavPoint goal, int stuckTicks, long tick);
```

1. **Replan** per §3.7.6. On a replan: `plan = Plan(q, agent, body, goal)`; `route = Active(goal, plan.Corners, tick, WindowStamp(plan.Window), plan.Window, plan.Partial)` or `Unreachable(goal, tick, WindowStamp(plan.Window), plan.Window)`. `ReplanReason`, `Outcome` and `Expansions` are set only on a replan tick.
2. **Unreachable:** return `Unreachable`; the owner holds.
3. **Advance**, at most 4 times: while `Corners.Length ≥ 2` and either `dist²(body, Corners[0]) ≤ corner_reach²` or `SegmentClear(body, Corners[1], r)` against solids and impassable gates, `route = route.Advance(1)`. The last corner is never dropped here.
4. **Arrived:** `Corners.Length == 1 && dist²(body, Corners[0]) ≤ arrive²` (300 mm). Return `Arrived` with `Target = Corners[0]`.
5. **Door:** if a closed, openable gate fails `SegmentClear(body, Corners[0], r)` and its integer squared distance to the body is ≤ `InteractReachMm²` (1,600 mm, `base_speeds.yaml:11`, measured from the body as the player's reach is, `Systems.cs:259-264`), return `OpenGate(key)`. Several qualify: the nearest by (squared distance to the footprint, `MinXMm`, `MinZMm`). The tie-break is geometric, never the key.
6. Otherwise return `Walk` with `Target = Corners[0]`.

Consumed corners are removed, so `Corners[0]` is always the current target; there is no cursor. Cost per mover per tick: one window stamp (≤ 9 tile stamps hashed; 4 in Ashen Hollow), ≤ 32 segment validations against nearby inputs, the look-ahead and the door test: about 10-20 µs.

### 3.9 Route persistence and save-then-continue

**Why routes are saved.** A route depends on the body position at the tick it was planned. Re-derived after a load it can choose different corners, and the digest diverges; replanning every tick (20 plans a second, flipping between equal-cost routes) is rejected. M6 made the same choice for the trail and stuck counter (`src/World/PlayerState.cs:52-62`; `CompanionTests.cs:285-319`).

```csharp
// src/Domain/Spatial/NavRoute.cs (namespace UNNAMED.Domain.Spatial)
public readonly record struct NavPoint(long XMm, long ZMm);
public readonly record struct NavRect(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm);   // inclusive
public enum NavRouteStatus { None, Active, Unreachable }                                      // keys "none" | "active" | "unreachable"

public sealed record NavRoute
{
    public const int MaxCorners = 32;
    private NavRoute(NavRouteStatus status, long goalXMm, long goalZMm, ImmutableArray<NavPoint> corners,
                     long plannedTick, ulong stamp, NavRect watch, bool partial) { /* assigns get-only properties */ }
    public NavRouteStatus Status { get; }  public long GoalXMm { get; }  public long GoalZMm { get; }
    public ImmutableArray<NavPoint> Corners { get; }  public long PlannedTick { get; }  public ulong Stamp { get; }
    public NavRect Watch { get; }  public bool Partial { get; }

    public static NavRoute None { get; }                                         // canonical zeros
    public static NavRoute Active(NavPoint goal, ImmutableArray<NavPoint> corners, long plannedTick, ulong stamp, NavRect watch, bool partial);
    public static NavRoute Unreachable(NavPoint goal, long plannedTick, ulong stamp, NavRect watch);
    public NavRoute Advance(int count);                                          // Active only; keeps at least one corner
    public static string? ProblemOf(NavRouteStatus status, long goalXMm, long goalZMm, ImmutableArray<NavPoint> corners,
                                    long plannedTick, ulong stamp, NavRect watch, bool partial);
    public string? Problem();                                                    // ProblemOf over this route
    public void AddTo(CanonicalHasher h);                                        // the one digest term order
    public bool Equals(NavRoute? other);                                         // SequenceEqual over Corners
    public override int GetHashCode();
}
```

- **Validity (`ProblemOf`).** `None`: goal (0, 0), no corners, tick 0, stamp 0, watch zeros, not partial. `Active`: 1..32 corners, tick ≥ 0, watch min ≤ max. `Unreachable`: no corners, not partial, tick ≥ 0, watch min ≤ max. Every factory and `Advance` throws `ArgumentException` with that text, so an invalid route cannot exist and a bug fails at the mutation, never at a save or load.
- **Properties are get-only**, so `with` cannot bypass a factory; there is no computed public property (`StateDump` serialises every public property).
- **`Watch`** is the planning window in mm, `(wi0·s, wj0·s, wi1·s + s − 1, wj1·s + s − 1)`, so `config.navigation` is not save-locked.
- **Tile stamp:** the first 8 bytes of `CanonicalHasher` over `"unnamed.nav-tile/v1"`, the config digest (node, margin, influence, classes, agent height), `Tx`, `Tz`, and every tile input in canonical order as (kind, shape tag, AABB, circle, height, clearance, openable). **It never includes an instance ID**, so replays and reloads produce equal stamps.
- **Window stamp (`Stamp`):** the first 8 bytes over `"unnamed.nav-window/v1"` and, for each resident tile meeting `Watch` in (Tz, Tx) order, `Tx, Tz, tile.Stamp`.
- **Digest terms** (`AddTo`): status key, goal x, goal z, planned tick, stamp, watch min x, min z, max x, max z, partial, corner count, then each corner x, z. `PlayerRecord.Digest` calls it inside each companion after the trail marks (`unnamed.player/v10`); `EffectiveCellDigest` calls it inside each errand (`unnamed.effective-cell/v2`).

**Route homes (full DTOs in section 7).**

| Mover | Record | Section file | Digest | Written by |
|---|---|---|---|---|
| Companion | `CompanionState.Route` → `CompanionRecord.Route` (init, default `NavRoute.None`), carried at both copy sites (`Companions.cs:125-155`) | `player.msgpack`, `CompanionDto.route` | `unnamed.player/v10` | `CompanionSystem` |
| Errand NPC | `NpcErrandRecord.Route` (init, default `NavRoute.None`) with `StuckTicks` | `entities.msgpack`, `NpcErrandDto.route` | `unnamed.effective-cell/v2` | `NpcSystem` (`Errands.cs`) |

Both use `NavRouteDto { status, goal_mm[2], corners_mm[≤ 64], planned_tick, stamp, watch_mm[4], partial }`, required on every companion and every errand from schema 14; about 70 bytes plus 16 per corner. A route holds no definition or instance ID, so the definition-ID pass and the baseline proof never touch it. A route that fails `ProblemOf` is a decode failure on either host: in `player.msgpack` it makes the player section corrupt; in `entities.msgpack` it quarantines the entities section (§7.5, §7.13).

**Why exact continuity holds.** At a boundary the loaded and the continuing world have equal grids (N-A7), gate flags and door states, mover records (body, phase, `StuckTicks`, `NavRoute`), other bodies and tick. `NavFollower.Next`, `NavSearch.Plan` and every trigger are pure functions of these: scratch never influences a result, counters are never read, tie-breaks are geometric, the errand mover reads neither tiers nor the conversation, and there is no per-tick plan budget. By induction every later tick is identical (N-A6). The window stamp is saved, not recomputed on load: a fighting or talking companion keeps an `Active` route unevaluated, and a stale saved stamp makes both worlds replan at the same tick.

**Doctrine (E0).** SYSTEMS `:443` becomes "search state, grids and caches are never persisted; a mover's committed route is mover state, saved with the body it moves (precedent: the schema-12 trail)"; S-25's "transient: path following state" becomes "persisted route".

### 3.10 Doors and the capability model

| Element | Source | Grid layer | Openable | Blocks planning when | Blocks `Kinematics` when |
|---|---|---|---|---|---|
| Authored door (`door.longhouse`, `door.forge_shed`) | `Layout.Doors`; flag at query time | gate (`ClosedFit` only) | yes; no locks in M7 | never for an opener | closed (unchanged) |
| Piece door leaf | `NavFootprint` of class `Door`; `DoorOpen` read from the piece row at query time | gate | yes, if `CanOperate` allows | never for an opener | closed (through `ClosedDoors()`) |
| Doorway jambs | `NavFootprint`s of class `Solid` | solid | - | always | always |
| Barrier (`barrier.foldscar_fold`) | `Layout.Barriers`; flag | gate | never | standing | standing |

`NavFootprint` carries no `DoorOpen`: door state is read at query time, so footprints change only when pieces change.

**Capability model.**

| Agent (M7) | Body radius | `NavAgent` | Height | Jump / crouch |
|---|---|---|---|---|
| Errand NPC | 350 (`base_speeds.yaml:10`) | `(0, OpensDoors: true)` | stands 1.8 m | never |
| Companion | 350 | `(0, true)` | stands 1.8 m | never |
| Placement check, `Reachable` | 350 | `(0, true)` | - | - |

`ClassIndex` is the smallest configured class whose radius ≥ the body radius (`NavConfig.ClassFor`). Reserved and not built: an access set on the agent, a height class, and "closes behind".

**Opening.** `OpenDoor(string DoorKey, string NpcId)` is handled by `InteractionSystem` exactly as section 6.1.4 specifies: companions and errand NPCs only, reach from the NPC's body, an open door is a no-op, `door.*` sets the flag and publishes `DoorToggled` with the NPC's instance ID, `pce_` goes through `OperatePieceDoor` and `CanOperate` (the owner, the owner's companion, an NPC whose errand `WorkOwner` is the owner).

**On an `OpenGate` step** (both movers): dispatch `OpenDoor`, turn towards the gate, take no `Kinematics` step. **Accepted:** `StuckTicks` unchanged; the mover walks through next tick. **Refused:** `StuckTicks += 1`, so a door the mover may not open (a foreign-owned piece door, only in a crafted save) gives a stuck replan every 20 ticks and at most one `OpenDoor` per tick; the errand shows "Blocked" at 200 ticks, and the companion reaches its snag catch-up at 80 ticks first (G26).

**Closing.** Movers never close doors. The close refusal covers every body for authored and piece doors (section 6.5.2), so nobody shuts a door on a mover in the doorway.

### 3.11 Companion integration

The change is confined to `CompanionSystem.Follow` (`Companions.cs:325-361`). Everything else in the companion, including the conversation hold (`:286-290`) and the tier gate (`:263`), is unchanged.

```
Follow(c, npc, tick):
  catch-up check                                        unchanged (CompanionRules.CatchUp)
  gait; within follow_near (2.5 m): stand               unchanged; also Route := None
  drop reached marks (800 mm)                           unchanged
  if InClearView(body, player):              Direct:  goal = player; Route := None            (unchanged branch)
  elif c.Route.Status == Active:             Route:   step = Follow((0, true), c.Route, body, player, c.StuckTicks, tick)
  elif a trail mark is in clear view:        Trail:   goal = the newest such mark; trim        (unchanged branch)
  else:                                      Nav:     step = Follow((0, true), c.Route, body, player, c.StuckTicks, tick)
       step.Kind == Unreachable -> goal = the oldest mark, or the player if the trail is empty   (the Phase-1 fallback)
  step.Kind == OpenGate    -> Dispatch(OpenDoor(step.GateKey, c.NpcId)); turn to the gate; no Step;
                              StuckTicks += (refused ? 1 : 0)
  step.Kind == Walk/Arrived -> goal = step.Target
  moved = Step(npc, goal, gait)                          unchanged helper, now on _context.Space and PersonObstacles
  headway and StuckTicks                                 unchanged (displacement ≥ 30% of expected travel)
  c.Route = step?.Route ?? c.Route; publish RoutePlanned when step.ReplanReason != null
```

- **Priority:** Direct > Route > Trail > Nav plan. Only the character in clear view pre-empts an `Active` route, so Trail and Route never flip-flop.
- **Route resets** (`Route := None`): standing within follow_near, Direct, `Order` (beside the trail clear, `:186-187`), downed, `Up`, `CatchUp`, `Fall`.
- **Safety net unchanged:** the 80-tick snag and 30 m catch-up remain; three stuck replans (20, 40, 60 ticks) precede any snag.
- **C16 unmodified.** `ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds` runs through Direct and Trail almost everywhere and passes with zero `CompanionCaughtUp`, as does every other `CompanionTests` case.

### 3.12 The errand mover

`NpcSystem` becomes `partial`; the mover lives in `src/World/Runtime/Errands.cs` and runs first inside `NpcSystem.Tick`, which already follows `_companions.Tick` (`Simulation.cs:326-327`). The record, phases and commands are section 4's and section 6's; the mover is:

```
NpcSystem.Tick(tick):
  foreach errand in State.World errands, ordinal by NpcId:                                // Errands.cs
    npc = State.Npcs[errand.NpcId]; body = (errand.XMm, errand.ZMm, errand.FacingMdeg)     // equal to npc.Body (G18)
    (goal, goalFacing) = errand.Phase is ToWork or AtWork
                         ? the piece's work anchor and facing (row + definition)
                         : the NpcSite position and facing
    if Phase == AtWork: turn towards goalFacing; write if changed; continue
    if body.(x, z) == goal:
        turn towards goalFacing (18,000 mdeg per tick, the integer turn of Social.cs:158-161)
        if facing == goalFacing:
            ToWork -> AtWork, Route := None, StuckTicks := 0; publish NpcArrivedAtWork
            ToHome -> RemoveNpcErrand; publish NpcReturnedHome                            // the NPC is baseline again
        write; continue
    step = _navigation.Follow(NavAgent(0, true), errand.Route, body.xz, goal, errand.StuckTicks, tick)
    if step.ReplanReason != null: publish RoutePlanned(npcId, outcome key, reason, corners, expansions, tick)
    Unreachable  -> turn towards the goal; StuckTicks += 1; Route = step.Route
    OpenGate     -> refused = Dispatch(OpenDoor(step.GateKey, npcId)) != null; turn towards the gate;
                    StuckTicks += refused ? 1 : 0; Route = step.Route
    Walk/Arrived -> travel = SpeedMmPerSecond(Walk) * TickMs / 1000                        // 80 mm
                    d = step.Target - body
                    landing = step.Target == goal && d.x² + d.z² <= travel²
                    dir = landing ? (DivRound(1000 * d.x, travel), DivRound(1000 * d.z, travel))
                                  : the companion's normalised direction (Companions.cs:561-566)
                    moved = Kinematics.Step(body, MoveIntent(dir, Walk, FacingTowards(body, target)),
                                            Setup.Movement, _context.Space, _context.PersonObstacles(npcId), TickMs)
                    headway = landing || dist²(body, moved) >= 24²                           // 30% of travel, integer
                    StuckTicks = headway ? 0 : StuckTicks + 1; Route = step.Route
    write the body (Npcs) and the errand (x, z, facing, Route, StuckTicks) in the same tick when any changed
  existing facing loop over NPCs that are neither companions nor on an errand (Social.cs:149-164, one exclusion)
```

- **No tier gate.** Tiers are unsaved and have hysteresis (`Tiers.cs:36-47`): a cell 140-160 m away is A when continuing and B after a load. The mover runs for every errand every tick (G22). The companion and creature tier gates keep the hazard, recorded in `M7_STATUS` and `RISK_REGISTER` as owed before M9.
- **Never reads conversation state** (G21). A talk with an NPC whose persisted phase is `to_work` or `to_home` is refused by `DialogueSystem`. At work she keeps her work facing and talks and trades normally; at home she has no record.
- **Exact landing.** Within one tick's travel, `dir` is the remaining offset in per-mille of `travel`, rounded half away from zero in integers. `Kinematics.Step` moves `dir·travel/1000` within 0.1 mm (`Kinematics.cs:137-147`) and whole-mm quantisation (`:157-158`) lands the body **exactly** on the integer goal. Arrival and retirement require exact x, z and facing, so a loaded world without the record places her at her site (`Social.cs:127-139`) exactly where the continuing world has her.
- **Never teleports.** A body blocking the last step keeps the record in its phase until she lands. Jams show as rising `StuckTicks`, a replan every 20 ticks, and "Blocked" at 200 ticks. Displacement never exceeds 81 mm a tick.
- **Populate** places an errand NPC at its errand pose, drops an errand whose NPC is a saved companion (an `ErrandAudit` line, shown in the `StructureAudit` view; G23), and publishes nothing. Creature homes never see pieces (G9).

### 3.13 The placement navigability check

There is **one rule**: `NavEditCheck.Check`, pure, in `src/Domain/Spatial/NavEditCheck.cs`. The command and the ghost both reach it through `NavigationSystem.CheckEdit`: the command with the authoritative scratch and sink, the ghost (`Simulation.PreviewPlacement` → `BuildingRules.Validate`) with `_previewScratch` and a null sink. Both carry the system in `PlacementContext.Navigation` (section 4). It never mutates the slice.

```csharp
public enum NavPointKind { NewSite, WorkAnchor, Body, NpcSite, Spawn, Reach, DoorApproach }
public sealed record NavProtectedPoint(string Label, NavPointKind Kind, NavRect Target, long RadiusMm, long ReachMm);   // a point is a degenerate rect
public sealed record NavEditVerdict(bool Ok, string? Rule, string? Reason, int NodesFlooded);                          // Rule: "V-N1".."V-N4"
public static NavEditVerdict Check(NavGrid before, NavConfig config, NavScratch scratch,
    ImmutableArray<NavInput> addSolids, ImmutableArray<NavInput> addDoors, ImmutableArray<NavProtectedPoint> points);
// src/World/Runtime/Navigation.cs
internal NavEditVerdict CheckEdit(ImmutableArray<NavInput> addSolids, ImmutableArray<NavInput> addDoors,
                                  ImmutableArray<NavProtectedPoint> points, NavScratch scratch, NavCounterSink? counters);
```

**Graph.** The `person` class with **every gate passable** (all doors and barriers, in any state): walkability is `SolidFit[n] > 0` alone, so the verdict is a pure function of content, the piece set and the points, and no toggle can make a preview stale. BEFORE is the live grid; AFTER is `min(SolidFit(n), FitAt(add, n))` per node on demand, which equals `StampRect` over the touched tiles. **Window** `W`: the added AABB inflated by `edit_window_margin_m` (32 m), at most 128 m a side, clipped to the grid; only points whose `Target` meets W are passed.

**Rules, in order; the first failure returns.**

- **V-N1, no newly sealed pocket** (when `addSolids` is non-empty). Seeds: the walkable AFTER nodes on the one-node ring just outside `AABB(add) ⊕ 600 mm`, in (j, i) order. For each seed not yet labelled, flood AFTER (4-connected) inside W up to `seal_limit_nodes` (16,384). Reaching the cap or W's border means open. A flood that ends sealed re-floods the same seed in BEFORE; sealed after and open before means **refuse**. **Any** newly sealed walkable pocket is refused, whether or not a protected point lies in it; a doorless one-square hut is refused. A new pocket must border the change, so the ring is sufficient. The reason names the first protected point, in list order, with a walkable node in the pocket (the table below); with none, "that would close off a space with no way in; rooms need a doorway".
- **V-N2, existing protected points** (kinds `WorkAnchor` … `DoorApproach`, inside W). (a) No added solid strictly overlaps a point's circle of `RadiusMm`. (b) If BEFORE had a walkable node whose centre lies within `ReachMm` of `Target`, AFTER still has one. The reason is the point's phrase.
- **V-N3, new functional points** (`NewSite`). `RadiusMm > 0` (the station work anchor): `PointClear` at that radius against the AFTER solids, and a walkable node within `ReachMm` of it whose AFTER flood is open. `RadiusMm = 0` (the chest site, which lies inside the chest's own box): a walkable node within `ReachMm` whose AFTER flood is open. Reason: "nothing could reach the {piece}".
- **V-N4, door pieces** (when `addDoors` is non-empty). Both approach points (leaf centre ± (half thickness + 700 mm) along the thin axis) have a walkable AFTER node within 250 mm. Reason: "the door would open onto a wall".

**Protected points**, gathered by the World-internal `BuildingRules.ProtectedPoints(state, candidate)` in this canonical order:

| Order | Kind | Points | RadiusMm | ReachMm | Phrase (V-N1 naming and V-N2) |
|---|---|---|---|---|---|
| 1 | `NewSite` | the candidate chest's site; the candidate station's work anchor | 0 / 350 | 1,600 / 250 | "that would shut in the {piece}" |
| 2 | `WorkAnchor` | anchors of `to_work` and `at_work` errands, by NpcId | 350 | 250 | "that would cut {npc}'s work place off" |
| 3 | `Body` | the player; companions by NpcId; errand NPCs by NpcId; never creatures | 350 | 1,600 | "that would shut you in" / "that would shut {name} in" |
| 4 | `NpcSite` | every authored `NpcSite`, by NpcId, whether or not its NPC stands there | 350 | 1,600 | "that would wall in {name}'s place" |
| 5 | `Spawn` | the region spawn | 350 | 1,600 | "that would wall in the waystone" |
| 6 | `Reach` | authored containers by key; corpses by key; piece chests by ID; authored stations by key; piece stations by ID; nodes by name; switches (`Target` = the switch body's AABB, the `DistanceTo` rule of `Systems.cs:276-282`); created ground stacks by (x, z, def, count), never by item ID | 0 | 1,600 | "that would shut {label} away" |
| 7 | `DoorApproach` | each authored door by key, then each placed door in `StructureOrder`: both approach points | 0 | 250 | "that would close off {door}" |

**When it runs.** Building's check 15 calls it only for a candidate with a solid part (wall, doorway, chest, bench: V-N1 to V-N3) or a door (V-N4). Pads and roofs never call it; dismantle never calls it. `NavVerdict`: not called → `NotApplicable`; a preview asked without navigability → `NotChecked`; `Ok` → `Proven`; otherwise `Refused` (section 6.1.6).

**Completeness: BLD008** (section 4). (a) Every build area grown by the 200 mm overhang holds at most `seal_limit_nodes` nodes (Ashen Hollow: 12,013). (b) Filled solid, it disconnects no walkable node outside it from the spawn. By monotonicity a pocket can only lie inside an area, below the flood cap and within W, so V-N1 decides every M7 pocket exactly. Widening an area must keep BLD008 green or raise the limit.

**Budgets** (ASTRAL, command and preview alike): median ≤ 2 ms, worst ≤ 10 ms, measured by N-A10; presentation asks only when the snapped pose or `StructureRevision` changes, and otherwise at most every 0.5 s (bodies move). **Preview parity:** the preview runs the same `BuildingRules.Validate` with `_previewScratch` and a null counter sink, so at equal state its `(Allowed, Failed, Reason)` always equals the command's; it never dispatches, publishes, registers, counts or writes (G7).

**Examples in Ashen Hollow [model]:** the Crossing Workshop vestibule wall (100500, 96000) r0 seals workshop and vestibule into a 428-node pocket (the same seed floods 615,891 nodes before), so it is refused naming Kera's work anchor. A wall with a 1.6 m gap, a U-shaped hut and a lone wall are accepted.

### 3.14 Code placement

**Domain, `src/Domain/Spatial/` (namespace exactly `UNNAMED.Domain.Spatial`, integer only, no sub-namespace, inside N-X1's scan):**

| File | Contents |
|---|---|
| `NavConfig.cs` | `NavConfig(long NodeMm, long MarginMm, long AgentHeightMm, ImmutableArray<NavClass> Classes, NavLimits Limits)`; `NavClass(string Id, long RadiusMm)`; `NavLimits` (window, expansions, probes, corners, snaps, seal limit, edit window, follower thresholds, retry, stuck and replan ticks, blocked view); `InfluenceMm = 600`; `RpMm(k)`; `ClassFor(radiusMm)`; `Problem()` (NAV001-NAV003, NAV007); `Digest()`; `Default` (equals the shipped file) |
| `NavGeometry.cs` | `FitAt`, `PointClear`, `SegmentClear` (`Int128`), `BoundsFit`, `DistanceSquaredTo`, `FloorDiv` |
| `NavInputs.cs` | `enum NavInputKind { Solid, Door, Barrier }`; `NavInput(NavInputKind Kind, Blocker Shape, string? GateKey)`; the canonical comparer (AABB MinX, MinZ, MaxX, MaxZ; kind; shape tag; height; clearance; gate key ordinal as the final tie-break) |
| `NavTile.cs` | `NavTileKey(long Tx, long Tz) : IComparable<NavTileKey>` (Tz, Tx); `NavTile` (immutable): `Key`, `SolidFit`, `ClosedFit` (`ImmutableArray<byte>`, 160,000 each), `Inputs`, `Gates`, `Stamp`; `Restamped(rect, inputs)` |
| `NavGrid.cs` | `NavGrid` (immutable): `Config`, `Bounds`, `ImmutableArray<NavTile> Tiles` (key order); `Build(config, bounds, tileKeys, inputs)`; `With(NavRect changed, inputs)`; `Walkable(i, j, agent, isGateOpen)`; `WindowStamp(NavRect)`; `Digest()` (tests and views only) |
| `NavScratch.cs` | a class, one per owner, never static: window-sized `int[] G`, `int[] Generation`, `byte[] Dir` (with the closed bit), flood `int[] Queue` (16,384); the heap arrays `(int f, int h, int idx)` sized `8 × max_expansions + 1`, allocated on the first A*. Everything is allocated lazily; a generation wrap clears the arrays and restarts at 1 |
| `NavSearch.cs` | `NavAgent`; `NavQuery`; `enum NavOutcome`; `NavPlan`; `Plan` (snap, probes, A*, `StringPull`) |
| `NavRoute.cs` | `NavPoint`, `NavRect`, `NavRouteStatus`, `NavRoute` (§3.9) |
| `NavFollower.cs` | `NavStepKind`, `NavStep`, `Next` (§3.8) |
| `NavEditCheck.cs` | `NavPointKind`, `NavProtectedPoint`, `NavEditVerdict`, `Check` (§3.13) |
| `NavCounters.cs` | `NavCounters` (immutable snapshot) and `NavCounterSink` (an instance class; §3.17) |

The byte layers are built in a `byte[]` and exposed with `ImmutableCollectionsMarshal.AsImmutableArray` (no copy); the builder array is never kept after publication, and `ImmutableCollectionsMarshal` appears only in `NavTile.cs` and `NavGrid.cs` (G25). `StructureFootprints.cs` (`TraversalClass`, `NavFootprint`, `StructureOrder`) is building's file in the same namespace (section 4).

**World, `src/World/Runtime/Navigation.cs` (new):**
- `internal sealed class NavigationSystem` — "Owns: `StateSlice.Navigation`". Instance fields: the authoritative `NavScratch` and a `NavCounterSink`, exposed by the internal accessors `Scratch` and `Counters` for the command path's `PlacementContext`. Members: `Build()`; `Handle(RebuildNavigation, long tick)`; `Follow(NavAgent, NavRoute, NavPoint body, NavPoint goal, int stuckTicks, long tick) → NavStep`; `CheckEdit(...) → NavEditVerdict`; `Reachable(NavAgent, NavPoint from, NavPoint to) → bool` (the plan's outcome is `Found`; counts; barriers by current flags); `CurrentInputs()`; `View()`. No `Tick`.
- The internal commands `RebuildNavigation(NavRect Changed, StructureChangeKind Kind, long Revision)` and `OpenDoor(string DoorKey, string NpcId)`; the events `NavigationRebuilt` and `RoutePlanned`; the views `NavigationView(NavGrid Grid, ImmutableArray<NavGateView> Gates, ImmutableArray<NavMoverView> Movers, NavCounters Counters)`, `NavGateView(string Key, string Kind, Blocker Footprint, bool Open)` (`Kind` ∈ `door`, `piece_door`, `barrier`), `NavMoverView(string NpcId, NavRoute Route, bool Blocked)`.
- `CurrentInputs()`: authored statics from `Setup.Layout.Space` passing the height filter; authored doors and barriers as gates; `StructureFootprints` (`Solid` → solid, `Door` → gate with `GateKey = PieceId.Value`); sorted by the canonical comparer. It never reads `SystemContext.Space`, which already contains the piece solids.

**Other World touch points** (full list in section 6.5.2):
- `RuntimeState.cs`: `StateSlice.Navigation` ("Transient: derived from the region layout and the placed pieces; rebuilt at start and on every `RebuildNavigation`; never saved"); `public NavGrid? Navigation { get; private set; }`; `SetNavigation(SliceOwner, NavGrid)` beginning with `Require(owner, StateSlice.Navigation)`.
- `Simulation.cs`: `SimulationSetup.Navigation { get; init; } = NavConfig.Default`; `_navigation` composed after `_crafting` and passed by constructor to `_building`, `_npcs` and `_companions`; `Build()` after `_building.Populate()`; `Dispatch` arms `RebuildNavigation → _navigation`, `OpenDoor → _interaction`; the get-only `Navigation => _navigation.View()`.
- `SystemContext.PersonObstacles(npcId)`; `InteractionSystem.Handle(OpenDoor, long)`; `CompanionState.Route` and `Follow` (§3.11); `Errands.cs` (§3.12); `GameSession.Boot` sets `Navigation = NavigationContent.Build(loader)` (`src/Application/GameSession.cs:88-99`).

### 3.15 Contracts

**Offered by navigation:**

| To | Contract |
|---|---|
| Building | `CheckEdit` for the command and the preview (the preview with `_previewScratch` and a null sink); `Reachable` for assign refusal 10; the guarantee that an accepted placement cannot seal a space, bury a protected point, or strand a door or work anchor; BLD005's per-axis limit, `window_max_m − 2·window_margin_m − start_snap_m − goal_snap_m` = 85 m from every `works_at` NPC's site to every build-area corner |
| Errands, companion | `Follow` (target, `OpenGate`, `Arrived`, `Unreachable`), decided from persisted state only |
| Persistence | `NavRoute` (factories, `ProblemOf`, value equality, `AddTo`); nothing else (G3) |
| Presentation | `Simulation.Navigation` (`Grid.Walkable`, `Gates`, `Movers` with `Blocked`, `Counters`); `NavigationRebuilt`, `RoutePlanned`; no Godot navigation outside `src/Presentation/Spike/**` (G4) |
| Factions | Nothing: navigation reads no standing (G6); a future gate access set may express **access**, never hostility |

**Required by navigation:**

| From | Contract |
|---|---|
| Building | Every placed part as a `NavFootprint` (world mm, axis-aligned, `StructureOrder`) readable before `Build()`; none for pads and roofs; `RebuildNavigation` per section 6.2.2 with `Changed` = the parts' union AABB; door state on the piece row; openings ≥ 1.6 m; BLD005, BLD006 (`module_m` a multiple of `node_m`), BLD008 |
| Interaction | `OpenDoor` as §3.10; the all-bodies close refusal |
| Content | `config.navigation` or `NavConfig.Default`; `MovementRules` from `config.base_speeds` |

### 3.16 Content and lints

`content/config/navigation.yaml` (optional; when absent `NavConfig.Default` applies, and `NavConfigDefault_IsTheShippedFile` pins them equal):

```yaml
id: config.navigation
kind: config
schema: 1
display_key: config.navigation.name
tags: [config]
notes: Domain navigation (M7; owner ruling 1). A 250 mm clearance-class lattice on global indices, tiled per 100 m cell, derived from exactly what Kinematics collides with; nothing here is saved.
node_m: 0.25              # NAV001: divides 100 m (400) and the 5 m terrain spacing (20); even in mm
margin_m: 0.05            # NAV003: (r + margin)^2 >= r^2 + node^2 / 2 for every class
agent_height_m: 1.8       # NAV005: equals the built MovementRules stand height
classes:                  # ascending radius; a body uses the smallest class at least its radius
  - { id: person, radius_m: 0.35 }   # NAV004: equals the built MovementRules body radius
window_margin_m: 20
window_max_m: 128
max_expansions: 65536     # covers Kera's workshop walk home (40,751 [model]) with 60% headroom
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
seal_limit_nodes: 16384   # 1,024 m2; BLD008 keeps every build area below it
edit_window_margin_m: 32
```

`src/Content/NavigationContent.cs` holds `Validate(loader)` and `Build(loader) → NavConfig`, hooked in `ContentLoader.LoadAll` after `QuestContent` and before `BuildingContent` (section 6.1.10). Every sorted collection uses `StringComparer.Ordinal`.

| Lint | Checks |
|---|---|
| NAV001 | `node_m` divides 100 m exactly, is even in mm, and is ≥ 50 mm |
| NAV002 | Class IDs unique; radii > 0 and strictly ascending; a class named `person` exists; every class's planning radius (radius + margin) ≤ 600 mm (`InfluenceMm`) |
| NAV003 | The soundness inequality `(r + m)² ≥ r² + s²/2` for every class |
| NAV004 | The `person` radius equals the **built** `MovementRules.BodyRadiusMm` (not the raw key; the fixture pack omits optional keys) |
| NAV005 | `agent_height_m` equals the built `MovementRules.StandHeightMm` |
| NAV006 | In every region, with every door and barrier passable, the spawn's 4-connected person flood satisfies every authored point: each `NpcSite`, container, station and node (a flooded node within 1,600 mm); each switch (within 1,600 mm of its structure's footprint); each door approach point (within 250 mm); each **location anchor as a reach point**: a flooded node within its `discovery.radius_m`, or within 1,600 mm of the footprint that contains the anchor. Tavar's site at the fold's centre passes, as WLD012 intends (`src/Content/WorldContent.cs:304-305`) |
| NAV007 | `window_max_m ≤ 128`; `2·window_margin_m + start_snap_m + goal_snap_m < window_max_m`; `1 ≤ max_expansions ≤ 65,536` (the scratch bound); `1 ≤ probe_nodes ≤ seal_limit_nodes ≤ 16,384`; `1 ≤ max_corners ≤ 32`; every distance > 0; every time ≥ one tick |

**NAV006 on shipped content [model]:** the walkable area is one component (616,991 nodes). The Foldscar (153, 48) and Ruined Cart (157, 162) anchors lie inside rocks, so their centre nodes are unwalkable; both pass as reach points, as does every other point, including `container.timber_stack` at (84, 118).

### 3.17 Instrumentation

`NavCounters` is the only M7 instrument in the authority (L4). It is an immutable snapshot built from the `NavigationSystem`'s `NavCounterSink`: deterministic work counts, never saved, never in a digest or `StateDump`, never read by a decision (`CountersAreNeverRead`, G15). Preview queries never count.

| Group | Counters | Read by |
|---|---|---|
| Builds | `FullBuilds`, `RectRebuilds`, `TilesRestamped`, `NodesRestamped` | F2; N-A9 |
| Searches | `Plans`, `PlansByOutcome` (by `NavOutcome`), `Expansions` (sum), `MaxExpansionsOneQuery` | F2; N-A10 (ns per expansion) |
| Placement | `EditChecks`, `EditRefusalsByRule` (V-N1..V-N4), `FloodNodes` | F2; N-A10 |

- **Timing** exists only outside `src/Domain` and `src/World`, which never read a clock: N-A10 times with a test `Stopwatch`; the RAZER capture times frames.
- **Events.** `RoutePlanned` (outcome, reason, corners, expansions) lets a transcript reconstruct every plan; `NavigationRebuilt` carries tiles, nodes and the kind.
- **F2** (section 8) draws person-unwalkable nodes within 24 m, gates by state, each mover's corners and the trail, and prints the counters and the last rebuild and plan.

**Memory (Ashen Hollow):** tiles 4 × 320,000 B = 1.22 MiB; authoritative scratch, lazy, ≈ 8.7 MB (window arrays 262,144 × 9 B, heap 524,289 × 12 B, queue 65,536 B); preview scratch, floods only, ≈ 2.4 MB. **Steady state:** following costs 10-20 µs per mover per tick (estimate), about 0.05 ms a tick for two movers; the 60-creature tick (0.43 ms, `docs/M3D_STATUS.md:53`) is untouched.

### 3.18 Budgets asserted

N-A10 (`Navigation_StaysWithinBudget`) asserts in CI at 3× the ASTRAL targets and logs ASTRAL evidence for `M7_STATUS`:

| Measure | ASTRAL target | CI assert |
|---|---|---|
| Full build of the four tiles (640,000-node fill, 20,756 evaluations [model]) | < 20 ms | < 60 ms |
| One piece's `RebuildNavigation` (≤ 108 nodes) | < 2 ms | < 6 ms |
| `CheckEdit` over the Crossing Workshop placements and the RK-06 200-piece layout | median ≤ 2 ms, worst ≤ 10 ms | worst < 30 ms |
| Kera's walk-home plan, the worst real M7 route (4.6 ms [bench]) | ≤ 6 ms | < 18 ms |
| Every ordered pair of authored protected points with a span ≤ 88 m per axis, plus Kera's three workshop routes | all `Found`; mean < 2 ms; max expansions ≤ 43,690 (two thirds of the cap) | counts exact |
| A plan at the cap (6-9 ms [bench]) | ≤ 10 ms, logged | not asserted |

STOP if the walk-home plan exceeds 6 ms on ASTRAL. Content tuning keeps `max_expansions` ≥ 1.5 × the worst real route. The lever after a RAZER measurement is the per-tick plan budget (§3.19), not built in M7.

### 3.19 Future seams (designed, not built)

| Future need | Seam left in M7 | What arrives later, as an addition |
|---|---|---|
| NPC schedules (M9/M10) | The mover block (phase, goal, route, stuck) is what a schedule issues; the follower is agent-generic | Schedules issue goals. A per-tick plan budget: spent in fixed mover order, reset every tick, never saved, no cursor; a deferred mover keeps its stale persisted stamp. Trigger: 60 tier-A actors plan about 1.5 times a tick |
| Larger settlements, 2 km regions | Per-tile storage, global indices, windowed search, rectangle rebuilds, local edit checks, tile-alone equals full build | Tier-A residency (≤ 21 tiles, 6.7 MB), one tile built per tick on promotion; a probe or flood meeting a non-resident tile is inconclusive. A **portal graph** from walkable tile-edge runs, cached per tile stamp, for routes over 88 m. Per-tile input lists as the `Kinematics` broadphase when RK-06 needs it |
| Doors: locks, permission, courtesy | Gates carry a kind and key; one `passable(g)`; one `OpenDoor` | An agent access set and gate `Access` (key, owner, faction access, never hostility); a walkability with piece doors solid for non-permitted agents; "close behind" via a persisted "door I opened" field |
| Bridges | A planar model | A corridor where a future impassable-terrain layer is absent; overpasses stay excluded (ruling 2) |
| Roads | Integer costs, admissible heuristic | A per-tile cost byte (1.0×-2.0×); the heuristic uses the cheapest multiplier |
| Creature sizes and pathing | The class-count byte; `ClassFor`; the generic follower | `medium` 450 and `large` 550 in content (2 lanes each through 1.6 m); `NavAgent(ClassFor(r), false)`; a route in `CreatureRecord` (a `CreatureDto` freeze and migration) |
| Jump and crouch agents | One height class via `Blocks(b, 0, h)` | A `SolidFit`/`ClosedFit` pair per height class; jump links |
| 45° or free rotation (Q1) | `FitAt` needs only a point-to-footprint distance | An oriented-box integer distance and segment test |
| Networking | Deterministic authority code | A future authority runs it; clients never do |

### 3.20 Acceptance tests

Homes: `tests/Domain.Tests/Spatial/NavigationTests.cs` (synthetic, pure); `tests/World.Tests/NavigationRuntimeTests.cs` (internals, `src/World/World.csproj:19`; real content through `TestWorlds.HollowSetup()`, which E1 adds: it builds `SimulationSetup` from `content/` with the same Content builders as `GameSession.Boot` (`src/Application/GameSession.cs:86-99`) plus `NavigationContent.Build`, because World.Tests references Content but not Application (`tests/World.Tests/World.Tests.csproj:22-25`) and `TestWorlds.cs` today only hashes a content root (`:52-58`)); `tests/Application.Tests/NavigationTests.cs` (real Ashen Hollow content through `Harness`); Content and Architecture tests as noted. Randomness in tests is a test-local LCG, never `System.Random`.

**The required cases:**

| Case | Tests |
|---|---|
| Simple reachable path | N-D1 |
| Blocked path | N-D2, N-D4, N-D5 |
| Structure placed across the route | N-D7, N-A3 |
| Structure removed | N-D8, N-A4 |
| Seam crossing | N-D9, N-D10, N-A2 |
| Save/load reconstruction | N-A6, N-A7 |
| Deterministic replay | N-A8, N-D12 |
| Different body size (synthetic classes) | N-D6 |
| Unreachable target | N-D4, N-D13, N-A5 |
| Repeated rebuild gives identical data and path | N-D11, N-A9 |

#### 3.20.1 Domain

| # | Name | Setup | Assertions |
|---|---|---|---|
| N-D1 | `AnOpenField_RoutesStraight` | 40 × 40 m, empty; (10, 10) → (30, 25) m | `Found`; corners == [exact goal]; expansions ≤ 2 × the octile node distance |
| N-D2 | `AWall_IsRoutedAround_AndWalkedByKinematics` | An off-centre 20 m × 0.4 m wall across the line | `Found`; every segment `SegmentClear` at r; `Kinematics.Step` driven by `NavFollower` arrives within 1.3 × (length / speed) ticks; `IsClear` at every tick end |
| N-D3 | `ADoorway_IsTheWayIn` | 10 × 8 m room, 0.4 m walls, one 1.6 m doorway | The route crosses the wall line once, inside the doorway |
| N-D4 | `ASealedRoom_IsEnclosed_WithoutAStar` | The same room, no doorway | `Enclosed`; 0 expansions; probe nodes ≤ 2,048 |
| N-D5 | `AClosedDoor_StopsANonOpener_NotAnOpener` | N-D3 plus a door gate; E6 adds a piece-door gate | Non-opener `Enclosed` while closed, `Found` when open; opener `Found` with identical corners either way; toggling leaves `Grid.Digest()` unchanged (G14) |
| N-D6 | `BodyClasses_ThroughDoors` | Synthetic config 350 / 450 / 550; doorway z [10.0, 11.6] m, then [10.0, 11.2] m | Lanes 3 / 2 / 2, then 1 / 1 / 0; `large` `Unreachable` through the 1.2 m opening, `person` `Found` |
| N-D7 | `APieceAcrossTheRoute_Invalidates` | Plan; add a box across the second corner segment; `With(rect)` | `WindowStamp(route.Watch) != route.Stamp`; `Next` replans with `geometry`; the new corners avoid the box; with the stamp check disabled in a test seam, `blocked` fires instead |
| N-D8 | `ARemovedPiece_GivesTheShorterRoute` | A detour round a wall; remove the wall | `geometry` on the next call; the new cost is below the old remainder |
| N-D9 | `TheSeam_IsNotARepresentationBoundary` | 200 × 200 m, 4 tiles; a hut straddling (100, 100), its door on the z = 100 m seam | (a) the full build equals the four tiles built in all 24 orders; (b) a tile built alone, or evicted and rebuilt, equals the same tile from the full build; (c) a monolithic raster by the same `StampRect` equals the union of the tiles; (d) (100,000, z) is tile 1; (e) 50 fixed pairs across both seams route identically tiled and monolithic |
| N-D10 | `AStraddlingHut_IsEnteredOnce_AndCrossedTwice` | 6 × 6 m hut centred on (100, 100) m, doors west and east | A route to the interior crosses the hut's outer AABB exactly once; west to east crosses it exactly twice; both are walked by `Kinematics` |
| N-D11 | `RectRebuild_EqualsFullBuild_AlwaysAndBack` | 200 LCG place/remove edits of boxes and circles, a third within 600 mm of a seam | After every edit, `With` equals `Build(all)` byte for byte; after undoing all, equal to the original; the same with the input order reversed |
| N-D12 | `TheSameQuery_GivesTheSameRoute` | A symmetric obstacle centred on the start-goal line | 100 repeats × 3 input orders × fresh or reused scratch: identical corners, expansions and probe counts; the tie resolves to the asserted lower-(j, i) side |
| N-D13 | `TheBudget_EndsASearch_Deterministically` | Test config `max_expansions: 16000`; goal at the centre of a sealed 60 × 60 m ring (57,600 interior nodes, so probes are inconclusive); start 10 m west of the ring | `Unreachable(Budget)` at exactly 16,000 expansions, every run |
| N-D14 | `TheEnds_Snap_ByDistanceThenIndex` | Start 360 mm from a wall; goal inside a rock; goal out of reach | Snapped by (d², j, i); `GoalBlocked` when nothing stands within 2 m |
| N-D15 | `IntegerGeometry_EqualsSeparation` | 20,000 LCG points and radii; 5,000 LCG segments | `PointClear == (Separation(p, R) is null)` for boxes and circles; `SegmentClear` true implies no `Separation` hit at 10 mm samples, false implies a hit within 1 mm of the contact |
| N-D16 | `EveryLatticeEdge_IsWalkableByTheBody` | A random cluttered field; person and the synthetic classes | Every walkable 8-edge, sampled every 10 mm, has `Separation(·, r) is null` (floor 358.8 mm for person) |
| N-D17 | `TheFollower_OpensTheNearestDoorInReach` | Two closed gates on the segment; keys swapped between runs | `OpenGate` only within 1,600 mm; the same door chosen whatever the keys |
| N-D18 | `EachReplanTrigger_FiresOnItsCondition` | A table of the 8 reasons, including a partial route walked to its last corner | Each fires exactly on its condition and throttle, in order; an `Unreachable` route holds until the retry tick even while `StuckTicks` crosses multiples of 20 |
| N-D19 | `EditCheck_Rules` | Synthetic | Refused: the wall sealing a hut (V-N1, generic reason); a doorless one-square hut; burying a protected point (V-N2); a station whose anchor is not standable (V-N3); a door onto a wall (V-N4). Allowed: a U-shaped hut; a wall with a 1.6 m gap; an edit beside an already-sealed pocket. `NodesFlooded` is equal for a fresh and a reused scratch (the counter sink is `CheckEdit`'s; `PreviewsInterleaved_ChangeNothing` covers the null-sink path) |
| N-D20 | `EditCheck_AChestSiteInsideItsOwnBox_IsAReachPoint` | A chest whose site lies inside its own solid part | Accepted: V-N3 finds a walkable node within 1,600 mm in an open component |
| N-D21 | `NavRoute_IsBuiltOnlyThroughValidatingFactories` | Every invalid shape: 0 or 33 corners for `Active`, corners on `Unreachable`, non-zero `None` fields, min > max watch, `Advance` past the last corner | Each factory throws `ArgumentException` with the `ProblemOf` text; no public constructor exists (reflection); `with` cannot set a property |
| N-D22 | `TheScratchGenerationWrap_ClearsAndAnswersTheSame` | Scratch generation started at `int.MaxValue − 1`; three plans and two floods across the wrap | Results equal those of a fresh scratch |

#### 3.20.2 World (internals)

- **N-W1 `TileKeys_AreCellKeys`:** the tile-to-`CellKey.OfWorld` mapping for the four cells, points on x = 100,000 and z = 100,000, and `r_neg1_0`.
- **N-W2 `TheNavigationSlice_HasOneOwner_AndBuildingPublishesNothing`:** `EveryStateSlice_HasExactlyOneOwningSystem` stays green; construction publishes no `NavigationRebuilt`.
- **N-W3 `TwoSimulations_ShareNoNavigationState`:** two worlds in one process with different edits keep distinct grid digests, scratch and counters (G17).
- **N-A5 `AnUnreachableGoal_LeavesTheNpcWaiting`** (World.Tests, real content through `TestWorlds.HollowSetup()`): a crafted `to_home` errand for `npc.ashen_hollow.tavar_orr` (not recruited), body at (140.0, 42.0) m, route `None`, with `barrier.foldscar_fold` standing. His home is the fold's centre, so the plan is `GoalBlocked`: the body moves 0 mm; one `RoutePlanned` every 40 ticks; `NavMoverView.Blocked` from tick 200. Setting `world.foldscar.steadied` makes the next retry `Found`; he walks home, lands exactly on (145,000, 42,000) facing 53,000 and the errand retires with `NpcReturnedHome`.

#### 3.20.3 Application (real content)

| # | Name | Script | Assertions |
|---|---|---|---|
| N-A1 | `TheHollow_EveryProtectedPointIsReachable` | Real content, person, every gate passable | Every point of NAV006 satisfied; the lodge, smithy and workshop-doorway lanes of §3.3 (4 each, by centre); fence gap 3 lanes; beam line sealed for a standing person. The Application twin of NAV006 |
| N-A2 | `TheAssignedNpc_WalksIntoAHutStraddlingTheSeam_AndHome`, implemented as `CrossingWorkshop_5to6_KeraWalksInThroughAndAround` (`tests/Application.Tests/BuildingAcceptanceTests.cs`, section 4's command table): **the exit criterion** | Workshop steps 1-6 | Kera lands exactly on (100,750, 103,500) facing 270,000, within ⌈1.25 × L / 1.6 m/s × 20⌉ + 40 ticks, L = the polyline length of the first `RoutePlanned` route after `WorkerAssigned` ([model]: 80.45 m, 1,297 ticks); `DoorToggled` with her ID for `door.forge_shed` and the piece door; between `WorkerAssigned` and `NpcArrivedAtWork` she crosses z = 100 m exactly once inside [98,800, 105,200]², `c_01_00` → `c_01_01`, never exiting and re-entering the footprint after entering the opening; ≤ 81 mm and `IsClear` every tick. Host-cell changes are logged ([model]: `c_00_01 → c_00_00 → c_01_00 → c_01_01`). Release and exact retirement are asserted by `CrossingWorkshop_9`/`_10` at R43 and R47, and by `Kera_WalksToTheBench_ArrivesExactly_AndHomeAgain` |
| N-A3 | `AWallPlacedAcrossTheRoute_IsWalkedAround` | See the script below | `NavigationRebuilt` at the placement boundary; `RoutePlanned(reason: geometry)` on Kera's next tick; no new segment fails `SegmentClear` against the wall; she never overlaps it (`IsClear`); she arrives exactly |
| N-A4 | `ARemovedWall_OpensTheShorterWay` | The same, with the wall placed before assignment and dismantled mid-walk; a control world runs the same script without the dismantle | `RoutePlanned(geometry)` on her next tick; the new remaining polyline is shorter than the old remainder; she arrives earlier than in the control world |
| N-A6 | `MidRoute_SaveLoad_GoesOnTheSame` | (a) companion: N-A11's script, saved 20 ticks after Tavar's first `RoutePlanned`; (b) errand: workshop step 6, saved on the first tick Kera's body z ∈ [98,600, 99,400] | Grid digest equal; each `NavRoute` equal by value (G28); `StateDigest` equal at the load and every 50 ticks for 400 ticks; the same arrival tick (G15) |
| N-A7 | `TheSeam_SurvivesAReload` | After workshop step 1: save, load | Grid digest and every tile stamp equal; pure plans (`NavSearch.Plan` on each world's grid with a test scratch) from eight fixed points (3 m outside the midpoint of each wall; the four inner corners 0.8 m in from the walls) to the anchor are identical before and after |
| N-A8 | `APlacementSession_ReplaysToTheSameState` | From the Crossing Workshop start save, the command table's steps 1-9; replay the log at the same boundaries | Grid digest, every route, the `(tick, rejected)` pairs and `NavCounters` equal; the replayable `StateDump` shows 0 differences; the raw `StateDigest` is compared only for windows that minted no `itm_` (G8) |
| N-A9 | `RepeatedRebuilds_AreIdentical` | Crossing Workshop start with 60 timber, from (100.5, 94.5): pad (94500, 94500); then 50 × (place wall (94500, 96000) r0; dismantle it) | After every dismantle the grid digest equals the pad-only digest, after every place the first placed digest; a pure plan from (94.5, 92.0) to (94.5, 100.0) m gives identical corners in each state every time |
| N-A10 | `Navigation_StaysWithinBudget` | §3.18 | §3.18 |
| N-A11 | `TheCompanion_OpensTheLodgeDoor_AfterWaitThenFollow` | Tavar recruited (built as `CompanionTests` builds him), ordered to wait at (46.0, 128.0) inside the lodge; the character walks out through `door.longhouse` to (53.0, 128.0), closes it, walks to (58.0, 128.0) and orders Follow | Nav mode plans (`RoutePlanned`); `DoorToggled` with Tavar's instance ID; he ends within 4 m of the character; 0 `CompanionCaughtUp`. On Phase-1 code this script snags |
| N-A12 | `TheCompanion_FollowsRoundAPlayerWall` | Crossing Workshop start (Tavar waiting at (100.5, 108.5)); from (100.5, 101.0) the character places pad (100500, 103500) and wall (100500, 105000) r0, asserts Tavar is not in clear view, and orders Follow (the order clears the trail) | Nav mode plans round a wall end; 0 `CompanionCaughtUp`; no hold longer than 300 ticks (C16's measure); he ends within follow_near |
| N-A13 | `PlacementIsRefused_WhenNavigationWouldBreak` | From the state after workshop step 1. (a) Square (30, 30), from (95.0, 91.5): pad (91500, 91500), walls (91500, 90000) r0, (91500, 93000) r0, (90000, 91500) r1, then (93000, 91500) r1. (b) The same hut built from (91.5, 91.5), inside it. (c) From (100.5, 94.5): pad (100500, 94500), doorway (100500, 96000) r0, chest (100500, 94500) r0 (accepted), then door (100500, 96000) r0 | (a) V-N1: "that would close off a space with no way in; rooms need a doorway"; (b) V-N1 naming the player's `Body`: "that would shut you in"; (c) V-N4, the south approach (100500, 95100) lying on the chest: "the door would open onto a wall" [model-verified]. `StateDigest` unchanged each time. The vestibule is `CrossingWorkshop_7`. V-N2 and V-N3 cannot fire first in a legal M7 build; N-D19 proves them |

**N-A3 and N-A4 script.** Chosen by rule with concrete geometry; the geometry comes from the modelled first route (corner segment (52,625, 137,375) → (93,125, 103,375), which crosses x = 90,000 at z ≈ 106,000).
1. From the command table's state after step 4 (player at (100.6, 102.4), piece door open), the player walks by straight legs (100.5, 97.0) → (97.0, 97.0) → (91.5, 110.0) and places pad (91500, 106500) r0 on square (30, 35). N-A4 also places wall (90000, 106500) r1 (bounds x 89,800-90,200, z 104,800-108,200).
2. Legs (70.0, 128.0) → (51.5, 136.5) → (52.0, 142.0); open `door.forge_shed`; (53.9, 142.0) → (60.0, 140.2); `AssignWorkerCommand(kera, bench)` (1.71 m from her body).
3. At once, running: (53.9, 142.0) → (50.0, 142.0) → (51.5, 136.5) → (70.0, 128.0) → (91.5, 110.0). Assert the player stands there before Kera's body reaches x = 75,000.
4. At the first boundary after Kera's body has x ≥ 75,000: N-A3 places wall (90000, 106500) r1; N-A4 dismantles it. Precondition asserted before the edit: in N-A3 the wall's bounds fail `SegmentClear` with a remaining segment of her route; in N-A4 her remaining route passes round a wall end. A failed precondition fails the test with the route printed, never silently.

#### 3.20.4 Content, architecture and persistence

- **N-X1 `NavDomain_IsIntegerOnly`** (Architecture.Tests, G16): a source scan of `src/Domain/Spatial/Nav*.cs` finds none of `double`, `float`, `Math.Sqrt`, `Math.Sin`, `Math.Cos`, `Math.Atan`, `Math.Pow`, `Math.Round`.
- **N-X2 `LoadAll_Loads_Yaml_Files`** (`tests/Content.Tests/ValidationTests.cs:508`) gains `config.navigation`.
- **N-X3 `NavigationLints_RefuseBadConfigs_AndPassTheGame`:** each of NAV001-NAV007 refuses a crafted bad config or region; the shipped content and the fixture pack pass all of them, including Tavar's site at the fold's centre and the two location anchors inside rocks.
- **`NavConfigDefault_IsTheShippedFile`** (Content.Tests): an absent `config.navigation` means the shipped values.
- **Persistence** (section 7 owns the tests): N-P1 = `Schema13To14_GivesNothingBuiltNoLedgerAndNoRoutes`; N-P2 = `ASchema14CompanionWithoutARoute_IsCorrupt_NotDefaulted` (plus status, corner and watch cases); N-P3 = the v14 fixture's active three-corner partial companion route and two-corner errand route; N-P4 = the route cases of the corrupt set.
- **Guards** in section 6 that exercise navigation: G1 `OnlyBuildingDispatchesRebuildNavigation`, G3 `PersistenceNeverReferencesTheNavigationGrid`, G4 `PresentationUsesNoGodotNavigation_OutsideTheSpike`, G15 `CountersAreNeverRead`, G22, G25 `NavigationGridBytes_AreNeverUnwrapped`, G26 `ARefusedDoor_CountsAsStuck_ForBothMovers`, G28 `NavRoute_AndFactionLedger_EqualByValue_AfterADecode`.

**Must stay green, unmodified:** C16 and every other `CompanionTests` case (including `FollowWaitFollow_…` and `LeftFarBehind_…`); `JumpAndCrouchTests`, `CreatureTests`, `FoldscarTests`, `BehaviourMatrixTests`, `StateDumpTests` (apart from `ASaveAndALoad_CompareEqual_FieldByField`'s leaf count, §12.10); the `TerrainGridTests`, `KinematicsTests` and `TierRulesTests` in `tests/Domain.Tests/Spatial/SpatialTests.cs`; `SixtyCreatures_TickWithinTheBudget` (`CreatureTests.cs:526-542`); `AScripted200CommandSession_ThroughFrames_EqualsItsReplayThroughTheCommandBus` and `SavingIsNotAnEvent_AndLoadingPublishesNothing`.

**Changed by name:** `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame` (`CompanionTests.cs:285`) gains `Assert.Equal(before.Route, after.Route)`, valid because `NavRoute` compares by value. `LoadAll_Loads_Yaml_Files` gains `config.navigation`.

### 3.21 Not in M7

| Item | Belongs to |
|---|---|
| Creature pathing (chase, search, return-home), and routes in `CreatureRecord` | Unscheduled; M9 at the earliest |
| `medium` and `large` navigation classes in content (N-D6 stays synthetic) | With creature pathing |
| A per-tick plan budget | M9/M10 schedules, or earlier only if the RAZER window measures a hitch |
| The portal graph, tile residency and eviction, build-on-promotion, routes over 88 m | M9 (2 km regions) |
| Door access sets (locks, keys, ownership, faction access), a second walkability for non-openers, "close behind" | M9/M10 |
| Crowd or local avoidance between movers | M9/M10 schedules |
| Roads (cost bytes), bridges (an impassable-terrain layer), jump and crouch agents | Unscheduled |
| Oriented footprints for 45° or free rotation | Owner Q1, if answered that way; otherwise later |
| The 30-minute companion soak (T9) | Owed RK-05 work from M6; optional in M7 (section 14) |
| A `Kinematics` broadphase from the per-tile input lists | When an RK-06 measurement needs it |
| `work_anchor_max_m` | Removed; BLD005's 85 m per-axis rule replaces it |

### Tests owned by this section

| Test | Project | What it proves |
|---|---|---|
| N-D1 `AnOpenField_RoutesStraight` | Domain.Tests | A simple reachable path with bounded expansions |
| N-D2 `AWall_IsRoutedAround_AndWalkedByKinematics` | Domain.Tests | Follower plus `Kinematics` walks a planned detour |
| N-D3 `ADoorway_IsTheWayIn` | Domain.Tests | A route uses the doorway |
| N-D4 `ASealedRoom_IsEnclosed_WithoutAStar` | Domain.Tests | Probes decide an enclosure with 0 expansions |
| N-D5 `AClosedDoor_StopsANonOpener_NotAnOpener` | Domain.Tests | Gates are read at query time; toggles leave the grid (G14) |
| N-D6 `BodyClasses_ThroughDoors` | Domain.Tests | Different body sizes, with synthetic classes |
| N-D7 `APieceAcrossTheRoute_Invalidates` | Domain.Tests | The stamp change forces a `geometry` replan |
| N-D8 `ARemovedPiece_GivesTheShorterRoute` | Domain.Tests | Removal is noticed on the next call |
| N-D9 `TheSeam_IsNotARepresentationBoundary` | Domain.Tests | Tile and order independence; tile-alone equals full build |
| N-D10 `AStraddlingHut_IsEnteredOnce_AndCrossedTwice` | Domain.Tests | RK-14's "exits and re-enters" never happens |
| N-D11 `RectRebuild_EqualsFullBuild_AlwaysAndBack` | Domain.Tests | A rectangle rebuild equals a full build |
| N-D12 `TheSameQuery_GivesTheSameRoute` | Domain.Tests | Deterministic ties |
| N-D13 `TheBudget_EndsASearch_Deterministically` | Domain.Tests | The expansion cap is exact |
| N-D14 `TheEnds_Snap_ByDistanceThenIndex` | Domain.Tests | End snapping order and `GoalBlocked` |
| N-D15 `IntegerGeometry_EqualsSeparation` | Domain.Tests | The integer predicates equal `Separation` |
| N-D16 `EveryLatticeEdge_IsWalkableByTheBody` | Domain.Tests | The 50 mm soundness margin |
| N-D17 `TheFollower_OpensTheNearestDoorInReach` | Domain.Tests | Geometric door tie-break within reach |
| N-D18 `EachReplanTrigger_FiresOnItsCondition` | Domain.Tests | The 8 triggers, corrected trigger 7, the retry hold |
| N-D19 `EditCheck_Rules` | Domain.Tests | V-N1..V-N4 refusals and acceptances |
| N-D20 `EditCheck_AChestSiteInsideItsOwnBox_IsAReachPoint` | Domain.Tests | V-N3's reach rule for a zero-radius site |
| N-D21 `NavRoute_IsBuiltOnlyThroughValidatingFactories` | Domain.Tests | No invalid route can be constructed |
| N-D22 `TheScratchGenerationWrap_ClearsAndAnswersTheSame` | Domain.Tests | Scratch history never changes a result |
| N-W1 `TileKeys_AreCellKeys` | World.Tests | Tiles map to cells, including the negative region |
| N-W2 `TheNavigationSlice_HasOneOwner_AndBuildingPublishesNothing` | World.Tests | One owner; construction is silent |
| N-W3 `TwoSimulations_ShareNoNavigationState` | World.Tests | No shared grid, scratch or counters (G17) |
| N-A5 `AnUnreachableGoal_LeavesTheNpcWaiting` | World.Tests | Unreachable holds and retries, never teleports; lifts are picked up |
| N-A1 `TheHollow_EveryProtectedPointIsReachable` | Application.Tests | Authored content is navigable; real door lanes |
| N-A2 `TheAssignedNpc_WalksIntoAHutStraddlingTheSeam_AndHome` (= `CrossingWorkshop_5to6_KeraWalksInThroughAndAround`, file owned by section 4) | Application.Tests | The exit: in, through, around, straddling, exact, no teleport |
| N-A3 `AWallPlacedAcrossTheRoute_IsWalkedAround` | Application.Tests | A rebuild at the boundary, then a `geometry` replan round the wall |
| N-A4 `ARemovedWall_OpensTheShorterWay` | Application.Tests | Removal shortens the route |
| N-A6 `MidRoute_SaveLoad_GoesOnTheSame` | Application.Tests | Save-then-continue for both movers (G15) |
| N-A7 `TheSeam_SurvivesAReload` | Application.Tests | The grid, stamps and plans are equal after a load |
| N-A8 `APlacementSession_ReplaysToTheSameState` | Application.Tests | A full replay of a building session |
| N-A9 `RepeatedRebuilds_AreIdentical` | Application.Tests | 50 place/dismantle cycles return the digest and the route |
| N-A10 `Navigation_StaysWithinBudget` | Application.Tests | Build, rebuild, plan and `CheckEdit` budgets |
| N-A11 `TheCompanion_OpensTheLodgeDoor_AfterWaitThenFollow` | Application.Tests | The entry criterion: the M6 snag is gone |
| N-A12 `TheCompanion_FollowsRoundAPlayerWall` | Application.Tests | The companion plans round a placed wall |
| N-A13 `PlacementIsRefused_WhenNavigationWouldBreak` | Application.Tests | The ROADMAP "un-navigable" refusal on real content |
| N-X1 `NavDomain_IsIntegerOnly` | Architecture.Tests | No float or transcendental in `Nav*.cs` (G16) |
| N-X2 `LoadAll_Loads_Yaml_Files` (gains `config.navigation`) | Content.Tests | The exact content list |
| N-X3 `NavigationLints_RefuseBadConfigs_AndPassTheGame` | Content.Tests | NAV001-NAV007, NAV006 with location reach points |
| `NavConfigDefault_IsTheShippedFile` | Content.Tests | An absent config means the shipped values |
| `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame` (+ route) | Application.Tests | The companion route round-trips by value |
