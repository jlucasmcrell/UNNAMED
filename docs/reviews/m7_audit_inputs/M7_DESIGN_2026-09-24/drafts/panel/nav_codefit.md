# M7 Navigation v1: candidate design (lens: code fit and determinism rigor)

Status: a candidate design from the M7 design panel, 2026-09-24. It is design and implementation planning only; it contains no production code.
Base: origin/main `e10d2c4`. Citations are repo-relative `path:line` in the read-only snapshot. Every code fact that the design depends on was re-read in source for this document.
Inputs: `drafts/00_SCOPE_RULINGS.md` (followed throughout) and the research notes `spatial_movement`, `sim_core`, `persistence`, `authority` §1-4, and `building_docs` §4.3, §4.9, §6, §8 and §9.

---

## 0. The recommendation in one page

- **Representation.** One deterministic, integer-only **clearance grid**. Its lattice is 250 mm, and it is indexed in global node coordinates, so the lattice lines fall exactly on the 100 m cell seams. It is stored as one tile per 100 m cell. Each node holds one byte, `Fit`: how many agent radius classes can stand with their centre at that node.
  - Doors and barriers are a per-tile **gate overlay**, evaluated at query time. Opening or closing a door never rebuilds anything.
  - Connectivity labels are computed per class over the loaded window, as run-length rows with union-find.
- **Pathfinding.** A* on the 8-connected lattice, with no corner cutting and integer costs 1000/1414. The octile heuristic is consistent. Ties break by the total order `(f, h, node index)`. There is a hard expansion budget.
- **Smoothing.** Greedy string pulling uses an **exact integer swept-circle test** against the real footprints: Int128 for circles, a separating-axis test for boxes. The same test re-validates a persisted route every tick.
- **Soundness.** Nodes are planned at body radius plus 50 mm. That margin is derived so that every grid edge a body walks is provably clear for its own radius (§4).
- **Local movement is unchanged.**
  - Navigation hands the mover's owning system a **target point**.
  - The owner builds its `MoveIntent` exactly as it does today (`src/World/Runtime/Companions.cs:558-568`).
  - `Kinematics.Step` still resolves all collision (`src/Domain/Spatial/Kinematics.cs:129-160`).
  - `Kinematics`, `MoveIntent`, `WalkSpace` and `Blocker` gain no members and change no signatures.
- **Authority.**
  - The grid is derived, transient state in a new `StateSlice.Navigation`, owned by `NavigationSystem`. It is rebuilt in the `Simulation` constructor and by one internal command, `RebuildNavigation`, which the building system dispatches synchronously after every structural change.
  - **Routes are persisted by the owner of the mover's body**: the companion's in `CompanionRecord`, and the assigned NPC's in its assignment record. Routes are not a pure function of persisted state, because the tick on which a route was planned and the position it was planned from are gone after a load. The existing precedent is the same: the companion trail and stuck counter were persisted for exactly this reason (`src/World/PlayerState.cs:46-62`).
- **Consumers in M7:**
  - the companion: the route replaces only the "oldest mark" fallback when neither the character nor any trail mark is in clear view;
  - the one assigned NPC (scope ruling item 5).
  - Creatures stay unpathed. The optional "creature return-home" item is declined, with reasons in §2.
  - Nothing in the navigation code ever orders by instance ID, so fresh-ULID runs compare equal (`tests/Application.Tests/StateDumpTests.cs:50`).
- **Persistence.** Schema 14 adds one required `route` object to `CompanionDto` and to the assignment record's DTO. The grid, labels, search scratch and counters are never saved.

---

## 1. What the design plugs into (verified)

| Fact | Where | Consequence for navigation |
|---|---|---|
| Boxes and circles, integer-mm fields, `double` maths | `src/Domain/Spatial/Blockers.cs:11-27,41,95` | Integer predicates reproduce `Separation(...) is null` exactly (§6.1) |
| Touching is clear (`squared >= radius²` → null) | `Blockers.cs:52,102` | Navigation uses strict `<` for overlap |
| Height rule `Kinematics.Blocks` | `Kinematics.cs:166-167` | Navigation filters with `Blocks(b, 0, 1800)`: one rule |
| One region-wide immutable `WalkSpace` | `Kinematics.cs:104`; `src/Content/WorldContent.cs:292` | Read, never replaced |
| `IsClear` bounds rule | `Kinematics.cs:170-173` | The grid's bounds rule is identical |
| Every mover, every wall test and prediction concatenates `ClosedDoors()` | `src/World/Runtime/Systems.cs:48,82-86,216`; `Companions.cs:574,598`; `Creatures.cs:830,893`; `Combat.cs:570`; `Simulation.cs:255`; `src/Presentation/Player/PlayerController.cs:127` | Placed solid pieces and closed placed door leaves belong in `ClosedDoors()`. That single join reaches movement, sight, shots, companion clear-view and prediction (a building contract, §20) |
| Door state is a flag re-read per query | `Systems.cs:41-48`; `RegionLayout.cs:12,94-97` | Gates are evaluated at query time |
| Companion goal selection: the character in clear view; else the newest mark in clear view; else **the oldest mark** | `Companions.cs:342-355` | The route replaces the third branch only |
| Headway, snag, catch-up | `Companions.cs:331-332,356-360,367-393`; `src/Domain/Companions/Companions.cs:85-86` | Kept as last resort |
| Creature `Move` steers straight; `Arrived` is 700 mm | `Creatures.cs:820-838,869` | Untouched |
| NPC bodies are transient and turn back to their site facing every tick, except companions | `src/World/Runtime/Social.cs:88,127-139,149-164` | The assigned NPC must be excluded from that turn, like companions (§20) |
| One owner per slice | `src/World/Runtime/RuntimeState.cs:19-74,144-161` | New transient `Navigation` slice, like `CellTiers` (`:36`) |
| Constructor order: `RequireEverySliceOwned`, `_effects.Seed`, `_npcs.Populate`, `_companions.Populate`, `_creatures.Populate`, `_tiers.Settle` | `Simulation.cs:139-144` | `_navigation.Build()` goes right after `_effects.Seed` |
| A load publishes nothing | `tests/Application.Tests/DeterminismAndViewTests.cs:129-143` | `Build()` at construction publishes no event |
| `StateDigest` = tick + player digest + effective cell digests | `Simulation.cs:357-364` | The grid stays out; routes enter through the records that carry them |
| `Simulation` method allow-list | `tests/Architecture.Tests/ArchitectureTests.cs:111-131` | Navigation adds properties only |
| No static mutable fields; arrays count as mutable | `ArchitectureTests.cs:85-105,204-209` | Neighbour tables must be `ReadOnlySpan<T>` properties or `static readonly ImmutableArray<T>`, never `static readonly int[]` |
| Public records in exactly `UNNAMED.Domain.Spatial` have no public setters | `ArchitectureTests.cs:134-155` | Navigation types use that namespace, not a sub-namespace |
| Terrain never blocks | `TerrainGrid.cs:51-67`; `Kinematics.cs:159` | Ignored; a slope limit must land in both |
| 100 m cells, floor division | `src/World/Coordinates.cs:15-18,93-96` | Tile = cell; a seam point belongs east/north |
| `Atan2`/`Sin`/`Cos` already in authority | `src/Domain/Combat/Combat.cs:238-243`; `Companions.cs:402-403` | Navigation adds none |

Ashen Hollow numbers used below (`content/regions/ashen_hollow.yaml`):
- bounds [0, 200] m (`:10`); four cells (`:8`);
- lodge walls 0.4 m thick with a 1.6 m doorway at z 127.2-128.8 (`:68-72,143`);
- smithy doorway at z 141.2-142.8 (`:74-78,144`);
- fence gap 1.6 m, x 51.4-53 (`:80`);
- beam line sealed x 143.6-153.4 (`:89-91,118`);
- fold barrier: circle r 3 m at (145, 42) (`:194-199`);
- Tavar's site at its exact centre (`:155`).

Bodies (`content/config/base_speeds.yaml:10,18`; `research/spatial_movement.md` §4.3):
- person radius 350 mm, stand height 1.8 m;
- creature radii 350-550 mm.

---

## 2. Q1: Who needs pathfinding in M7

| Mover | Needs a path in M7? | Why |
|---|---|---|
| **Assigned NPC** (scope ruling item 5: one settlement NPC walks between their `NpcSite` and a work anchor on a player-placed station) | **Yes, primary** | The ROADMAP exit, "navigates in, through, and around" (`docs/ROADMAP.md:284`). A teleport cannot satisfy it, and there is no trail to follow |
| **Companion (Tavar)** following | **Yes, as a fallback** | Three cases need it: the trail has no mark in clear view; wait → follow cleared the trail (`Companions.cs:186-187`); and a structure placed across the trail. Today these slide into walls and then snag-teleport (`research/spatial_movement.md` §12.1 items 3-5). The trail itself works: C16 passes with no catch-up (`tests/Application.Tests/CompanionTests.cs:119-160`). **Keep it.** |
| Player | No | Input-driven |
| Settlement NPCs not assigned | No | They stand still (`Social.cs:149-164`) |
| Creatures (chase, search, return, wander, patrol) | **No (optional item declined)** | See below |
| Charges and lunges | Never | The stun depends on hitting a structure (`Creatures.cs:565-596`; `tests/Application.Tests/CreatureTests.cs:326-352`) |
| Presentation scripted routes (playthrough) | No authoritative use | They may call pure Domain functions for the debug overlay only |

Why the optional creature return-home is declined:
1. The owner's criteria are met without it.
2. It would change the M3d behaviour matrix, which is generated and asserted (`BehaviourMatrixTests`, `docs/M3D_BEHAVIOUR_MATRIX.md`) and has twelve creature tests behind it.
3. It forces a `route` field onto `CreatureDto`, a shape frozen since schema 8 (`research/persistence.md` §2), and onto the creature digest.
4. A pathing creature needs `OpensDoors = false` gate handling at query time, which no M7 consumer otherwise exercises.

The seam is left in place: creatures map to radius classes (§4), and `NavFollower` is agent-generic.

---

## 3. Q2: Which representation

The ruling is fixed (deterministic, headless, in the domain, derived and seam-safe). The options compared:

| Criterion | **A. Uniform integer clearance grid, tiled per cell (recommended)** | B. Waypoint or visibility graph from walkable samples or inflated corners | C. Current steering plus deterministic local avoidance (wall-following / Bug2) | D1. Integer constrained-Delaunay navmesh | D2. Flow fields per goal |
|---|---|---|---|---|---|
| Determinism | Integer bytes; `min` stamping is order-free | Exact LOS per edge; circle corners need trig or polygons | Stateful (bug mode must persist) | Needs robust predicates; order-freedom hard to prove | Yes |
| Cell seams | Seams are node boundaries; nothing to stitch | Long edges cross cells | n/a | RK-14's stitching problem | Span cells |
| Dynamic building | 1-4 tiles, < 1 ms each; doors never rebuild | Re-link edges through the area | Cannot answer "is this placement navigable?" | Local re-triangulation | Every field dies |
| Memory (Ashen Hollow) | 640 KB of Fit + 80 KB of gate bits | Small | None | Small | 2.5 MB per goal |
| CPU | A* 0.1-1 ms typical, 10 ms at budget | Rebuild ≈ 450 × 450 × 67 ≈ 13.6M tests | O(1) | Fast | 20-40 ms per goal; the companion's goal moves every tick |
| 2 km region | Tier-A window ≤ 16 tiles; portals later (§14) | Quadratic in props | Unchanged | Good, costly | Infeasible |
| 100 m cells | Tile = cell exactly (400 × 400 nodes) | Awkward | n/a | Tiles, plus stitching | n/a |
| Headless tests | Byte-hashable; trivial to compare | Harder | Easy but weak | Hard | Easy |
| Save/load | Nothing saved; rebuilt identically | Same | Must persist the bug state | Same | Same |
| Schedules, larger worlds | Follower + tier-B portal graph from tile borders | Natural roadmap, poor scaling | Unusable | Good | No |

**Recommendation: A.**
- It is the only option that is at once provably order-free, seam-free by construction, cheap to invalidate locally, and directly checkable against `Kinematics` by the same predicate.
- C is kept as the layer below it: `Kinematics` sliding, the companion trail and the snag rule.
- D1 is the upgrade path if paths ever need sub-grid optimality. The public surface (`NavFollower.Next`) does not change with it.

---

## 4. Q3: Resolution, derived rather than picked

Symbols:
- `s` = node spacing;
- `r` = body radius;
- `m` = margin;
- `Rp = r + m` = the planning radius (a node is walkable for a class when a circle of radius `Rp` at its centre overlaps no footprint).

**Constraint 1: grid edges must be walkable by the real body.**
- Take two walkable nodes a distance `L` apart and a point obstacle `q`. Then `|A−q|, |B−q| ≥ Rp` implies that every point of AB is at least `sqrt(Rp² − L²/4)` from `q`.
- For a convex footprint, apply the same argument to its nearest point.
- For a circle of radius R: `(Rp+R)² − (r+R)² ≥ Rp² − r²`, so point obstacles are the worst case.
- Diagonal edges (`L = s√2`) are the binding case: **`Rp² ≥ r² + s²/2`**.

**Constraint 2: the doorways that exist must stay open for the classes that must use them.**
- An opening of clear width W leaves a band of node-centre positions `W − 2Rp` wide.
- Any closed interval of width w contains at least `floor(w/s)` lattice centres.
- The smallest opening guaranteed passable is therefore **`W_min = 2Rp + s`**.

**Constraint 3: the lattice must tile the cell and terrain exactly.**
- `s` must divide 100,000 mm, so each cell holds a whole number of nodes and seams fall on node boundaries.
- `s` should divide the 5 m terrain spacing and the 1 m or 0.5 m snap steps building is likely to use.

Candidates, for the person class `r = 350`, with `m` rounded up to 50 mm:

| s | minimum m from constraint 1 | Rp (m = 50) | lanes through a 1.6 m door (person / r450 / r550) | W_min (person) | nodes per cell | Fit bytes per cell |
|---|---|---|---|---|---|---|
| 500 | 162 → m = 200 | 550 | 1 / 0 / 0 | 1,600 (zero slack) | 40,000 | 39 KB |
| 250 | 42.1 → m = 50 | 400 | ≥3 / ≥2 / ≥1 | **1,050** | 160,000 | 156 KB |
| 200 | 27.4 → m = 50 | 400 | ≥4 / ≥3 / ≥2 | 1,000 | 250,000 | 244 KB |
| 100 | 7 → m = 50 | 400 | ≥8 / ... | 900 | 1,000,000 | 977 KB |

For `s = 500`, constraint 1 needs `Rp ≥ sqrt(350² + 125,000) = 512`. With the doorway rule, 1.6 m of door leaves the boar class zero lanes.

**Choice: `s = 250 mm`, `m = 50 mm`.**
- It is the coarsest spacing that keeps all three radius classes through the existing 1.6 m doorways: humanoid 350, medium 450 (wolf, hound, armour), large 550 (boar, spider).
- It divides the cell (400 nodes) and the terrain spacing (20 nodes).
- It costs 1.56× fewer nodes than 200 mm, and A* cost scales with node count.

Constraint 1 checked:
- `400² = 160,000 ≥ 350² + 250²/2 = 153,750`. The clearance along a diagonal edge is at least `sqrt(160,000 − 31,250) = 358.8 mm > 350`, so a body walking any grid edge never even touches a footprint.
- For r = 450, the bound needs 483.5, which is below Rp 500. For r = 550 it needs 577.7, below 600.

The same numbers for the real content:
- **Lodge door** (z 127.2-128.8): the person band [127.6, 128.4] holds centres 127.625, 127.875, 128.125 and 128.375, so 4 lanes.
- **Smithy door**: 4 lanes.
- **Fence gap** (x 51.4-53): the band [51.8, 52.6] holds 51.875, 52.125 and 52.375, so 3 lanes.
- **Tradeoff.** Openings between 0.8 and 1.05 m that a 0.7 m body physically fits may or may not be found, depending on alignment. That is the price of 250 mm. Building must therefore use ≥ 1.6 m doorways and snap steps that are multiples of 250 mm, so that lane counts do not depend on placement (§20).

Agent classes (content, §15): `humanoid 350`, `medium 450`, `large 550`; `K = 3`.
- A mover uses the smallest class whose radius is at least its body radius. The husk (350) is humanoid, the hound (400) medium.
- `Fit` is a byte holding how many classes (in ascending radius) fit at that node, so node n is walkable for class k iff `Fit[n] > k`.

---

## 5. Data model (Domain, namespace `UNNAMED.Domain.Spatial`, files in `src/Domain/Spatial/Navigation/`)

```csharp
public sealed record NavAgentClass(string Id, long RadiusMm);

public sealed record NavSetup(long NodeMm, long MarginMm, long TileMm, long AgentHeightMm, ImmutableArray<NavAgentClass> Classes)
{
    public ImmutableArray<string> LabelledClasses { get; init; }      // M7: ["humanoid"]
    public int MaxExpansions { get; init; } = 40_000;
    public int MaxCorners { get; init; } = 64;
    public int ReplanPeriodTicks { get; init; } = 40;                 // 2 s
    public int MinReplanGapTicks { get; init; } = 10;                 // rate limit for body-segment replans
    public int RetryTicks { get; init; } = 40;
    public int StuckReplanTicks { get; init; } = 20;                  // 1 s; the companion snag stays at 80
    public long GoalMoveMm { get; init; } = 2_000;
    public long ArriveMm { get; init; } = 300;
    public int SnapRadiusNodes { get; init; } = 4;                    // 1 m
    public int GatePenalty { get; init; } = 500;                      // per closed-gate node, cost unit = 1000 per orthogonal step
    public long PlanningRadiusMm(int k) => Classes[k].RadiusMm + MarginMm;
    public int ClassFor(long bodyRadiusMm);                           // smallest k with RadiusMm >= body; -1 = not navigable
    public string? Problem();                                         // lint, §15
    public static NavSetup Default { get; }                           // 250 / 50 / 100_000 / 1_800 / [humanoid 350]
}

public readonly record struct NavAgent(int ClassIndex, long BodyRadiusMm, bool OpensDoors);
public sealed record NavGate(string Key, Blocker Footprint, bool Openable);   // door (openable) or barrier (not)
public sealed record NavInputs(WalkSpace Space, ImmutableArray<Blocker> Solids, ImmutableArray<NavGate> Gates);
public readonly record struct NavTileKey(int Tx, int Tz);
public readonly record struct NavGateCell(int Node, byte GateLocal, byte FitWhenClosed);
public readonly record struct NavRun(int X0, int X1, int Label);

public sealed class NavTile            // immutable after construction
{
    public NavTileKey Key { get; }
    public ImmutableArray<byte> Fit { get; }              // 160,000; index = localZ * 400 + localX
    public ImmutableArray<ulong> GateBits { get; }        // 2,500 words; bit set = a NavGateCell exists
    public ImmutableArray<NavGateCell> GateCells { get; } // sorted (Node, GateLocal)
    public ImmutableArray<NavGate> Gates { get; }         // tile-local table, sorted (footprint min X, min Z, Key)
    public ImmutableArray<Blocker> Blockers { get; }      // statics + solids whose inflated AABB meets the tile
    public string Hash { get; }                           // SHA-256 of Key, Fit, GateCells, gate footprints (never IDs)
}

public sealed class NavGrid
{
    public NavSetup Setup { get; }
    public long MinGx { get; } public long MinGz { get; } public int Width { get; } public int Height { get; }
    public ImmutableSortedDictionary<NavTileKey, NavTile> Tiles { get; }                      // ordered (Tz, Tx)
    public ImmutableArray<ImmutableArray<ImmutableArray<NavRun>>> Labels { get; }             // [labelled class][row]
    public string Hash();                                                                     // tiles in order + labels
}

public sealed record NavPoint(long XMm, long ZMm);
public enum NavRouteStatus { None, Active, Unreachable }
public sealed record NavRoute(NavRouteStatus Status, long GoalXMm, long GoalZMm, ImmutableArray<NavPoint> Corners, int Next, long PlannedTick)
{
    public bool Partial { get; init; }
    public long RetryTick { get; init; }
    public static NavRoute None { get; } = new(NavRouteStatus.None, 0, 0, ImmutableArray<NavPoint>.Empty, 0, 0);
    public string? Problem();   // Next in [0, Corners.Length]; None => canonical zeros; Unreachable => no corners, RetryTick > 0
}

public enum NavStepKind { Walk, Arrived, OpenGate, Unreachable }
public sealed record NavStep(NavRoute Route, NavStepKind Kind, long TargetXMm, long TargetZMm, string? GateKey, string? ReplanReason);
```

Notes:
- `NavGrid`/`NavTile` are classes over immutable arrays (`ImmutableCollectionsMarshal.AsImmutableArray`, .NET 8, no copy). An edit yields a new `NavGrid`, so readers hold consistent snapshots.
- There is **no revision counter that any decision reads**. Invalidation is by content (§8), so nothing depends on how many rebuilds happened.

---

## 6. The integer geometry (`NavGeometry`, pure)

### 6.1 Point clearance: equal to `Separation(...) is null`
- **Box** `[x0, z0, x1, z1]`: `dx = max(x0 − px, 0, px − x1)`, `dz` likewise; overlap iff `dx² + dz² < R²`. Use `long`: products are at most (2×10⁶)² for a 2 km region.
- **Circle** `(cx, cz, Rc)`: overlap iff `(px−cx)² + (pz−cz)² < (R + Rc)²`.
- **Bounds** (the `IsClear` rule, `Kinematics.cs:171-172`): `px − R ≥ MinX && px + R ≤ MaxX`, and the same for z.
- **Height filter** (`Kinematics.cs:166-167`): only blockers with `Kinematics.Blocks(b, 0, AgentHeightMm)` count. For Ashen Hollow this keeps all 64 structures; the beam's clearance of 1.3 m is below 1.8 m.
- **Equivalence.** `Separation` computes in `double` from `long` inputs; the squares are below 2⁵³ and so exact. A property test asserts the integer and `Separation` answers are equal (§17 D12).

### 6.2 Segment clearance: exact swept circle, `SegmentClear(A, B, R, blocker)`
- **Circle blocker (C, Rc).** Let `d = B − A`, `w = C − A`, `dot = w·d`, `len2 = d·d` (all `long`).
  - If `dot ≤ 0`: distance² = |w|².
  - Else if `dot ≥ len2`: distance² = |C − B|².
  - Else the segment is blocked iff `|w|²·len2 − dot² < (R+Rc)²·len2`, evaluated in `Int128`.
  - `A == B` reduces to the point test.
- **Box blocker.** The open Minkowski sum is the union of two open rectangles, `[x0−R, x1+R] × [z0, z1]` and `[x0, x1] × [z0−R, z1+R]`, and four open corner discs of radius R. A segment meets an open rectangle iff:
  1. `max(ax, bx) > rx0 && min(ax, bx) < rx1`, and the same for z;
  2. the four corner values `nᵢ = (az−bz)(cxᵢ−ax) + (bx−ax)(czᵢ−az)` (`Int128`) are strictly mixed: `min < 0 < max`.
  - `A == B` uses the strict point-in-rectangle test.
  - Touching at the boundary is clear, consistent with `Blockers.cs:52`.
- **Blocker sets.** The tiles overlapped by the segment's AABB, inflated by R, supply their `Blockers` and `Gates`. Duplicates from a footprint that straddles a seam appear twice, which is harmless because the result is an AND. So the **order and multiplicity of blockers cannot change any answer**.

There is no `sqrt`, `Atan2`, `Sin`, `Cos` or `double` anywhere in navigation.

---

## 7. Building the grid

### 7.1 Inputs (`Navigator.Inputs()`, World side)
- `Space` = `Setup.Layout.Space`: the bounds and the 64 static structures (`src/Content/WorldContent.cs:292`).
- `Solids` = the placed pieces whose traversal class is Solid, as `BoxBlocker`s with their real `HeightMm > 0` (from the building read API, §20).
- `Gates`:
  - authored doors (`Layout.Doors`, `Openable: true`);
  - authored barriers (`Layout.Barriers`, `Openable: false`, `RegionLayout.cs:28`);
  - placed door pieces (`Openable: true`; the key is the piece instance ID).
- Non-blocking pieces (roof, floor pad) are **not passed at all**.
- Bodies (player, creatures, NPCs, companions) are never in the grid.
- Terrain is never in the grid.

### 7.2 Tile build (`NavBuild.BuildTile`: a pure function of the inputs and the tile key)
1. Tile `(tx, tz)` covers global nodes `gx ∈ [400·tx, 400·tx + 400)` and the same range in z. Node centres are `(gx·250 + 125, gz·250 + 125)`.
2. `Blockers` = the statics and solids whose AABB, inflated by `RpMax + 1` (601 mm), intersects the tile's node-centre rectangle `[T·tx + 125, T·tx + T − 125]`, where T = 100,000 mm. Gates are selected the same way.
3. Initialise `Fit[n] = K` inside the bounds rule for the largest class, and less where the bounds cut a class.
4. For each blocker, **stamp** by `min` over the nodes of its inflated AABB clipped to the tile. The value at a node is the number of classes, taken ascending, whose `Rp` does not overlap.
5. For each gate, compute `fitClosed` over its inflated AABB. Where `fitClosed < Fit[n]`, emit `NavGateCell(n, gateLocal, fitClosed)` and set the gate bit. `Fit` itself is computed with every gate treated as open.
6. Hash.

Consequences:
- `Fit[n]` depends only on n's centre, the bounds, and the set of footprints that can reach it. Because `min` is commutative and associative, **the stamping order is irrelevant**.
- Any footprint that can reach any node of the tile is in the tile's list, so **a tile built alone equals the same tile from a full build**. That is the formal basis of the seam and rebuild tests.

### 7.3 Connectivity labels (`NavBuild.Label`, per labelled class, over the whole loaded window)
1. With every gate treated as passable (an optimistic topology), row by row in ascending gz, list the maximal runs of nodes with `Fit > k`, in ascending x.
2. Union each run with every run in the row below whose x-interval overlaps. This is 4-connectivity, which equals the A* graph's 8-connectivity without corner cutting: a legal diagonal implies both orthogonal steps exist.
3. Number the components 1..C in order of the first run met in (gz, x0) order.
4. Store the runs per row. A node's label is found by binary search.

Labels are a pure function of `Fit`, so they are identical after any rebuild or load. Cost for Ashen Hollow: 800 rows with about 8 runs each, so about 6,400 runs, and a single pass over 640,000 bytes.

---

## 8. Q4: Authority

| | What | Where |
|---|---|---|
| **Reads (authoritative)** | Region layout: bounds, statics, doors, barriers (content) | `Setup.Layout` (`Simulation.cs:14`) |
| | Navigation tuning (content) | `Setup.Navigation` (a new init property, default `NavSetup.Default`) |
| | Placed pieces' footprints and traversal class | the building system's slice or world-delta records (§20) |
| | Gate state: door flags, barrier flags, placed-door open state | `SystemContext.IsOpen` / `IsLifted` (`Systems.cs:41-45`) and a building read, **at query time** |
| | Mover body, route, stuck counter, goal | the owner's records |
| **Derived** | `NavGrid`: tiles, gate overlays, per-tile blocker index, labels | `StateSlice.Navigation`, owned by `NavigationSystem`; transient |
| **Cached** | Nothing across ticks except the grid. A* scratch is working memory only: its contents never influence a result, because generation stamps replace clearing | `Navigator` (inside `SystemContext`) |
| **Invalidates** | Place, dismantle or destroy a piece (a change of footprint set) | `RebuildNavigation` |
| **Does not invalidate** | Door or barrier open/close (gates are evaluated at query time); piece damage or repair (the footprint is unchanged); bodies moving; tier changes | — |
| **Rebuilt** | Everything, in the `Simulation` constructor (new game and load alike); the dirty tiles plus all labels, on `RebuildNavigation` | §9 |
| **Saved** | Each mover's `NavRoute`, next to its body, by the owner of the body | §12.3, §13.1 |
| **Not saved** | Grid, tiles, labels, gate overlays, blocker index, scratch, counters, `NavigationRebuilt` history | Add to the PERSISTENCE §2 not-saved table: "navigation grid and labels: rebuilt in the `Simulation` constructor from layout + structure records; never persisted" |

**Why routes are persisted rather than derived.** A route planned at tick T from position P_T is followed with a cursor; after a load at T+3 neither P_T nor T is recoverable, so a re-derived route, and the next `Kinematics.Step`, would differ. Replanning every tick would make the route a pure function of (body, goal, grid, gates), at the price of an A* per mover per tick and jitter between equal-cost alternatives. Persisting costs about 20 integers plus at most 64 corners (typically under 300 bytes). M6 made the same choice for the trail and stuck counter (`src/World/PlayerState.cs:46-62`; `CompanionTests.cs:285-319`).

---

## 9. Q5: The deterministic sequence for a building update

```
PlacePieceCommand (drain, tick boundary N)                      -- building design
 └─ BuildingSystem.Handle
     1. validate: overlap, sockets, build area, bodies            -- building
     2. navigability: _context.Navigator.CheckPlacement(candidate) -- §10.4; read-only, candidate tiles in scratch
     3. dispatch ExchangeItems (materials; refusable first)        -- existing pattern (Crafting.cs:100-102)
     4. commit piece record (its own slice / world delta)
     5. dispatch RebuildNavigation(minX, minZ, maxX, maxZ, "placed")   // AABB of old ∪ new footprints
          └─ NavigationSystem.Handle
              a. dirty = tiles whose node-centre rect meets the AABB inflated by 601 mm   (1, 2 or 4 tiles)
              b. inputs = Navigator.Inputs()   (reads the committed piece set)
              c. for dirty tiles in (Tz, Tx) order: tile' = BuildTile(inputs, key)   -- whole tile, never a patch
              d. labels' = Label(grid with tiles') for each labelled class           -- whole window
              e. State.SetNavigation(owner, grid')
              f. publish NavigationRebuilt(dirty, reason, tick)  -- presentation/tests only
     6. publish PiecePlaced
Step N+1: each nav mover's owner calls Navigator.Follow → the route's remaining corner-to-corner segments are re-tested
          exactly (§11.4); a segment that now hits the new footprint → immediate replan → new corners → new target point.
```

- Dismantle and destroy follow the same path with reason `removed`. Destruction can happen during a Step through the explicit damage rule; the rebuild then happens inside that dispatch, so movers later in the same Step already see the new grid.
- A route that is still valid after a removal is shortened within `ReplanPeriodTicks` (40) by the periodic replan.
- **Granularity.** Whole tiles, never sub-tile patches: a tile costs under 1 ms (§16) and the whole-tile rule is what makes "rebuilt equals built" provable. Neighbouring tiles are rebuilt only when the inflated AABB reaches them (a piece within 601 mm of a seam, or straddling it). Labels are always recomputed over the whole window, because one piece can split or join components anywhere.
- Door toggles by anyone (the player's `InteractCommand`, an NPC's `OpenDoor`) change gate state only. The next query sees them, and nothing is rebuilt.

---

## 10. Q6: Cell seams

### 10.1 No seams in the data
- There is one global lattice: `gx = FloorDiv(xMm, 250)`, with the same floor semantics as `CellKey.OfWorld`.
- 100,000 / 250 = 400 exactly, so every seam coincides with a node boundary, and every node centre lies 125 mm inside exactly one tile. There are no border nodes, no duplicates, and no overlap margin.
- A world point exactly on x = 100,000 mm maps to `gx = 400`, in tile 1, the east cell. That is the same answer `CellKey.OfWorld(100.0, z)` gives.
- A* neighbours across a seam are ordinary index arithmetic (`gx ± 1`). The search never knows a seam exists.

### 10.2 Tiles are keyed by integers, not `CellKey`
Domain cannot reference World (`CellKey` is in `src/World/Coordinates.cs`), so `NavTileKey(Tx, Tz)` uses `Tx = rx·20 + cx`. A World test pins the tile-to-cell mapping against `CellKey.OfWorld` for the four Ashen Hollow cells, the seam points and a negative region (`r_neg1_0`).

### 10.3 The straddling structure
- A footprint that crosses x = 100 m is listed in both tiles' `Blockers` and stamped into each, clipped. Each tile's node values are independent of the other's.
- Route segments are tested exactly against the union of the tiles' lists (§6.2), so the result is the same whichever tile a segment starts in.
- **Order-independent rebuilding.** Tiles are independent functions of the inputs, and labels are canonical by scan order. So rebuilding {A}, {B}, {A, B} or {B, A}, or evicting and rebuilding any subset, always yields the same `NavGrid.Hash()`.

### 10.4 The navigability rule for placement (`CheckPlacement`, read-only)
1. Build candidate tiles in scratch: the dirty tiles, with the candidate's solids stamped and its doors as gates.
2. Compute humanoid labels on the candidate window.
3. Refuse if any **protected anchor** that shares the spawn's label now does not. Reasons are free text, in the existing rejection style (`Commands.cs:9-12`). Protected anchors are:
   - the region spawn;
   - every `NpcSite` (Tavar's included: with barriers treated as open, it is reachable);
   - every container and station site;
   - both approach points of every door, 0.8 m out from its footprint on each side;
   - every location centre;
   - the player's body;
   - every companion and assigned NPC body and work anchor;
   - every existing placed piece's use anchor.
4. Refuse if the candidate's own use anchors (a chest's use point, the station's work anchor) do not share the spawn's label.
5. Anchors snap to the nearest walkable node within 1 m (§11.2). An anchor that has no such node before the placement is not protected; the lint in §15 makes that impossible for authored anchors.

This makes RK-14's mitigation, "constrain or warn on placement that would straddle a seam until stitching is proven" (`docs/RISK_REGISTER.md:294`), unnecessary. Straddling is proven, not constrained.

---

## 11. Q7: Pathfinding

### 11.1 Graph and costs
- **Nodes.** Walkable for agent `(k, OpensDoors)` iff `Fit[n] > k`, and for every `NavGateCell` at n with `FitWhenClosed ≤ k` whose gate is closed: the gate is openable and the agent opens doors. That case is passable at a cost; a closed barrier is never passable.
- **Moves.** 8-connected, **no corner cutting**: a diagonal needs both orthogonal neighbours walkable under the same rule.
- **Costs** are integers: orthogonal 1,000, diagonal 1,414, plus `GatePenalty` (500) on entering a node that is only passable because the agent will open a closed gate (about 5 nodes, so about 2.5 m of detour-equivalent per closed door). `g` is a `long`.
- **Heuristic.** Octile: `1000·max(|dx|,|dz|) + 414·min(|dx|,|dz|)`. It is exactly the graph distance on an empty lattice, so it is consistent, and penalties only add cost.
- **Neighbour order** is fixed: (+1,0), (0,+1), (−1,0), (0,−1), (+1,+1), (−1,+1), (−1,−1), (+1,−1). A parent is replaced only on a **strictly** smaller g, so the first-found parent wins among equals.
- **Open list.** A binary heap keyed by the total order **(f ascending, h ascending, node index ascending)**, where node index = `(gz − MinGz)·Width + (gx − MinGx)`. The key is a total order over distinct nodes, so the pop sequence does not depend on the heap's internals. Stale duplicates are skipped by the closed stamp.
- **Scratch.** `long g[]`, `int stamp[]`, and `byte parentDir[]` with bit 7 as "closed". Arrays are sized to the window and owned by the `Navigator` instance; there is no static state, so two simulations share nothing. A generation stamp avoids clearing.

### 11.2 Snapping, start and goal
- `Snap(x, z, agent)`: collect the nodes within Chebyshev radius 4 of `(FloorDiv(x,250), FloorDiv(z,250))`, ordered by (squared distance from (x, z) to the node centre, node index). Take the first node that is walkable and whose segment from (x, z) is `SegmentClear` at the body radius r.
- A start with no such node is `StartBlocked`; a goal with none is `GoalBlocked`.
- A body 350 mm from a wall usually sits on a node that is unwalkable at Rp 400, which is why snapping is needed.

### 11.3 Unreachable: always decided, never timed
In this order:
1. `StartBlocked` / `GoalBlocked`.
2. Different humanoid labels: `Disconnected`, answered in O(log runs) with zero expansions.
3. A* empties its open list or spends `MaxExpansions` (40,000): `Exhausted`. This only happens when a closed gate the agent cannot pass separates two nodes of the same optimistic component (the fold, for example), or on a pathological detour.

Every one is reported as `NavRouteStatus.Unreachable` with `RetryTick = tick + 40`. The owner decides the fallback (§12). The budget counts expansions, not time, so it is deterministic.

### 11.4 String pulling and route validity
- **Pulling.** Candidates are the body position, the path nodes P₀..Pₙ₋₁, and the goal point if `SegmentClear(Pₙ₋₁, goal, r)`. Greedily from the anchor, extend k while `SegmentClear(anchor, candidate[k+1], r)`; the corner is candidate[k], which becomes the next anchor.
  - The step to the next grid node always succeeds: grid edges have at least 358.8 mm of clearance, which exceeds r (§4), and body → P₀ is guaranteed by the snap. So progress is at least one node per corner.
  - Test count is O(n).
  - Corners are node centres or the goal point, in integer mm. More than 64 corners are truncated with `Partial = true`.
- **Validity, every tick** (`NavFollower.Next`). Every remaining corner-to-corner segment is re-tested with `SegmentClear(·, ·, r)` against:
  - the tiles' `Blockers`;
  - closed barriers;
  - closed doors, but only for agents that do not open doors.

  About 10 corners × about 40 nearby footprints is about 400 integer tests, around 15 µs.
- **Replan triggers.** All read persisted fields or the tick:

| Reason | Condition | Immediate? |
|---|---|---|
| `none` | `Status == None` | yes |
| `retry` | `Status == Unreachable && tick >= RetryTick` | yes |
| `goal_moved` | `(goal − route.Goal)² > GoalMoveMm²` | yes |
| `blocked` | a remaining corner-to-corner segment fails | yes |
| `periodic` | `tick − PlannedTick ≥ 40` | yes |
| `stuck` | the owner's `stuckTicks > 0 && stuckTicks % 20 == 0` | yes |
| `off_line` | body → `Corners[Next]` fails (the body was pushed off the line by a body or a newly placed piece) | only if `tick − PlannedTick ≥ 10` |
| `partial` | `Next == Corners.Length && Partial` | yes |

- **Advancing.** While `Next < Len` and either `dist²(body, Corners[Next]) ≤ 300²` or `SegmentClear(body, Corners[Next+1], r)`, increment `Next`.

---

## 12. Q8: Local movement, doors and capability

### 12.1 Three layers, three owners

| Layer | Function | Owner | Changed in M7 |
|---|---|---|---|
| Global route | `NavSearch.Plan`: snap, A*, pull | Domain, pure | new |
| Local steering | `NavFollower.Next` returns `NavStep` (a target point, or `OpenGate`, `Arrived` or `Unreachable`) | Domain, pure | new |
| Intent and facing | the owner's existing code: `CompanionSystem.Step` (`Companions.cs:558-568`, including `FacingTowards`) | owner | **no** |
| Collision and bodies | `Kinematics.Step` with the owner's obstacle list | Domain | **no** |

- Dynamic bodies are handled only by `Kinematics` push-out and sliding. The "who blocks whom" table (`research/spatial_movement.md` §7.9) is unchanged.
- A body in a doorway shows up as the owner's stuck counter rising: a replan every 20 ticks, and then the owner's last resort.

### 12.2 Doors and the capability model
- `NavAgent(ClassIndex, BodyRadiusMm, OpensDoors)`. The M7 values:
  - companion: `(humanoid, 350, true)`;
  - assigned NPC: `(humanoid, 350, true)`.
- Future flags reserved in the design, not built: `CanJump`, `CanCrouch` (height classes, §14), and a gate permission policy.
- **Closed openable door on the route.** When the segment from the body to the target crosses a closed openable gate footprint (`SegmentClear` at radius r fails on that gate only) and the body's `DistanceTo(footprint) ≤ InteractReachMm` (1.6 m), `Next` returns `OpenGate(key)`.
  - The owner dispatches `internal OpenDoor(string DoorKey, EntityId Actor, long FromXMm, long FromZMm)` and holds its intent idle for that tick.
  - If several gates qualify, the nearest wins, by (squared distance to the footprint, footprint `MinXMm`, `MinZMm`). The tie-break is geometric, **not by key**, so a fresh ULID never changes the choice.
- `InteractionSystem.Handle(OpenDoor)` (it owns no state and already "asks the owner", `Systems.cs:236-273`):
  - checks reach from the actor's body;
  - authored door: dispatch `SetWorldFlag(cell, flag, 1)`;
  - placed door: dispatch the building system's door command;
  - publish the existing `DoorToggled(Actor, DoorKey, true, tick)` (`Events.cs:25`, whose Actor is already an `EntityId`).
- The M7 default is that **the NPC leaves the door open** (owner question 2). Closing behind would need a persisted "door I opened" field and a body-clear check.
- Barriers are never opened by anyone. A standing barrier is a wall.

### 12.3 Consumer integration

**Companion** (`CompanionSystem.Follow`, `Companions.cs:325-361`). The goal order becomes:
1. The character is in clear view (existing `InClearView`): the goal is the character, and `Route := None`.
2. `Route.Status == Active`: follow the route (`Navigator.Follow`). This is hysteresis, so there is no trail/route flip-flop.
3. The newest trail mark in clear view: the existing code, with the trail trimmed.
4. Otherwise (**new**): `Navigator.Follow` toward the character:
   - `Walk`: the goal is the step's target;
   - `OpenGate`: dispatch, turn, stand;
   - `Unreachable`: the **existing oldest-mark fallback, unchanged** (`:351-353`).

Unchanged: `Step`, headway, `StuckTicks`, the 80-tick snag, the 30 m distance catch-up. `Route := None` also on `Order` (`:187`), downed (`:232-242`), `Up` (`:498-508`), `CatchUp` and `Fall`. The route goal is the character's position, so a moving character triggers `goal_moved` about every 0.6 s while out of view. Reasons publish `RoutePlanned(MoverKey, Status, Corners, Reason, Tick)` for views and tests.

**Assigned NPC** (the owning system is the building/assignment design's). The navigation contract:
- persist `(XMm, ZMm, FacingMdeg, StuckTicks, Phase, NavRoute)` for the NPC;
- in tier A each tick, call `Navigator.Follow(agent, route, body, anchorX, anchorZ, stuckTicks, tick)`, then build the intent with the companion's existing `Step` pattern, including `Obstacles(npcId)` (`Companions.cs:571-581`);
- move the body with `PlaceNpc` (`Social.cs:141-147`);
- `Unreachable` means stand, face the anchor, retry at `RetryTick`, and publish `RoutePlanned` once. **Never teleport.**
- `NpcSystem.Tick` must skip assigned NPCs as it skips companions (`Social.cs:153`), and `NpcSystem.Populate` must be overridden by the saved body, as `CompanionSystem.Populate` does (`Companions.cs:125-142`).

---

## 13. Code placement and wiring

| File | Change |
|---|---|
| `src/Domain/Spatial/Navigation/NavSetup.cs`, `NavGeometry.cs`, `NavTile.cs`, `NavGrid.cs`, `NavBuild.cs`, `NavSearch.cs`, `NavFollower.cs`, `NavCounters.cs` | New. **Namespace `UNNAMED.Domain.Spatial`**, so `ViewsAndEvents_HaveNoPublicSetters` scans the records. Direction tables are `static ReadOnlySpan<sbyte> Dx => new sbyte[] {...}` (compiled data, not a field) |
| `src/Content/NavigationContent.cs` | `Build(loader)` → `NavSetup`; `Validate` (NAV001-NAV007) hooked like the others (`src/Content/ContentLoader.cs:170-190`) |
| `content/config/navigation.yaml` | New (§15) |
| `src/World/Runtime/RuntimeState.cs` | `StateSlice.Navigation` ("Transient: derived from the layout and the placed structures; rebuilt at start and on every structure change; never saved"); `public NavGrid? Navigation { get; private set; }`; `SetNavigation(SliceOwner, NavGrid)` behind `Require` |
| `src/World/Runtime/Navigation.cs` | New. `NavigationSystem` (owns `Navigation`; `Build()`; `Handle(RebuildNavigation, long)`); `internal sealed record RebuildNavigation(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, string Reason) : InternalCommand`; `internal sealed record OpenDoor(...) : InternalCommand`; `internal sealed class Navigator` (`Inputs`, `Follow`, `Plan`, `CheckPlacement`, `IsGateClosed`, `Stats`, scratch); public events `NavigationRebuilt(ImmutableArray<NavTileKey> Tiles, string Reason, long Tick)` and `RoutePlanned(...)` |
| `src/World/Runtime/Systems.cs` | `SystemContext.Navigator` (created in the context constructor with `setup.Navigation`); `InteractionSystem.Handle(OpenDoor, long)` |
| `src/World/Runtime/Simulation.cs` | `SimulationSetup.Navigation { get; init; } = NavSetup.Default`; compose `_navigation = new NavigationSystem(_context, _state.Claim(nameof(NavigationSystem), StateSlice.Navigation))`; call `_navigation.Build()` after `_effects.Seed` and before `_npcs.Populate()` (`:140-141`), with no event; Dispatch arms for `RebuildNavigation → _navigation.Handle(c, Now)` and `OpenDoor → _interaction.Handle(c, Now)` (`:369-404`); get-only properties `Navigation` and `NavStats`. **No `Step` change and no new `GameCommand`** |
| `src/World/Runtime/Companions.cs`; `src/World/PlayerState.cs` | `CompanionState.Route`, `CompanionRecord.Route` (default `NavRoute.None`); Follow per §12.3; digest adds the route and bumps the tag once for M7 (`unnamed.player/v10`, `PlayerState.cs:322-368`) |
| `src/Application/GameSession.cs` | `Navigation = NavigationContent.Build(loader)` in the setup initializer (`:88-99`) |
| Persistence | §13.1 |

The navigation layer is never ordered by instance ID:
- tiles are ordered by `(Tz, Tx)`;
- tile gates by footprint geometry;
- A* by node index;
- labels by scan order;
- door choice by geometry.

Keys are only looked up, never sorted into a decision.

### 13.1 Persistence (schema 14; shares the bump with building and factions)
- `NavRouteDto` (MessagePack, snake_case):
  - `status`: `"none" | "active" | "unreachable"`;
  - `goal_mm [x, z]`, `corners_mm long[]` (flat pairs), `next`, `planned_tick`, `partial`, `retry_tick`.
- `CompanionDto.Route`: nullable in the DTO, **required from schema 14**. Decode throws "corrupt, not defaulted" when it is absent, and validates `NavRoute.Problem()`.
  - `CompanionDto` is shared with the frozen `V12.Player` (`src/Persistence/Sections/SchemaV12.cs:6,32`), so a copy must be frozen first (as `V12.Companion`), and `V13.Player` created (the M7 player freeze).
- The step `SchemaV13ToV14` gives every companion `route: {status: "none"}`.
- The assigned NPC's record carries the same `NavRouteDto`, wherever building places the record.
- `CanonicalState` renders the route; every `expected.json` gains `route: none` on the v12/v13 companions and nothing else.
- Routes hold no definition IDs, so there is no alias-pass change. Gate keys are never persisted.
- A content change to `config.navigation` (for example `node_m`) is not baseline-locked. Persisted corners are world mm and are re-validated exactly (§11.4), so the worst effect is one replan.

---

## 14. Q9: Future seams (not built in M7)

- **NPC schedules.**
  - Tier A: schedule anchors are `NavAnchor`s walked by `NavFollower`, the same code as the work anchor.
  - Tier B ("coarse movement on routes", `Systems.cs:529`): a portal graph derived from tile borders. Each tile edge's walkable runs per class become portals, with intra-tile portal-to-portal costs from a tile-local A*, cached per tile as derived data. That is HPA*, and the run-based labels are its first half.
- **Larger settlements and 2 km regions.**
  - Tiles are resident only in the tier-A window.
  - Tiles are built on promotion, one per tick if needed, and are deterministic, so their timing is irrelevant.
  - A* scratch becomes an open-addressing map bounded by the budget.
  - Labels are computed per window.
  - The per-tile blocker index is the natural `Kinematics` broadphase later, as an additive overload.
- **Doors.** `NavGate` gains `Permission` (a key item, or a faction access check by ruling 3: access only, never hostility) and `AutoClose`.
- **Bridges** (planar: the gap is scenery). A bridge deck is simply walkable while its sides are Solid footprints. A terrain-blocking layer would join `Fit` by the same `min` stamping.
- **Roads.** An optional per-tile cost byte multiplies the step cost; the heuristic stays admissible if the discount is folded into the base cost.
- **Creature sizes.** `medium` and `large` already exist. Creature pathing is `NavAgent(ClassFor(radius), r, OpensDoors: false)` plus a persisted route in `CreatureRecord`.
- **Jump and crouch.** Add height classes, so `Fit` becomes per (radius class × height class) with the `Kinematics.Blocks(b, 0, h)` filter per height. Beams become passable for crouch-capable agents.
- **Multi-storey.** Excluded by ruling 2. The planar model has no vertical links, deliberately.

---

## 15. Content: `content/config/navigation.yaml` and lints

```yaml
id: config.navigation
kind: config
schema: 1
display_key: config.navigation.name
tags: [config]
notes: Domain navigation (M7, owner ruling 1). A 250 mm clearance grid tiled per 100 m cell; nothing here is saved.
node_m: 0.25            # divides the 100 m cell (400) and the 5 m terrain spacing (20)
margin_m: 0.05          # Rp = radius + margin; lint: Rp^2 >= r^2 + node^2 / 2 for every class
agent_height_m: 1.8     # lint: equals config.base_speeds stand_height_m
agent_classes:          # ascending radius
  - { id: humanoid, radius_m: 0.35 }    # lint: equals config.base_speeds body_radius_m
  - { id: medium, radius_m: 0.45 }
  - { id: large, radius_m: 0.55 }
labelled_classes: [humanoid]
max_expansions: 40000
max_corners: 64
replan_period_s: 2
min_replan_gap_s: 0.5
retry_s: 2
stuck_replan_s: 1
goal_move_m: 2
arrive_m: 0.3
snap_radius_m: 1
gate_penalty: 500
```

Lints:
- **NAV001**: the node size divides 100,000 mm and is at least 50.
- **NAV002**: class IDs are unique and radii strictly ascending.
- **NAV003**: the soundness inequality holds for every class.
- **NAV004**: the humanoid class radius equals the body radius.
- **NAV005**: the agent height equals the stand height.
- **NAV006**: in the region, every authored protected anchor (§10.4) shares the spawn's humanoid label.
- **NAV007**: no `NpcSite` lies within 1 m of any door or barrier footprint, so doorways are never held by a static body.

Adding the file changes `LoadAll_Loads_Yaml_Files` (`tests/Content.Tests/ValidationTests.cs:508`).

---

## 16. Performance: arithmetic and counters

**Ashen Hollow now** (200 × 200 m, 4 tiles, 800 × 800 = 640,000 nodes):

| Item | Arithmetic | Size / time |
|---|---|---|
| Fit | 640,000 × 1 B | 640 KB |
| Gate bits | 640,000 / 8 | 80 KB |
| Gate cells | 2 doors × about 72 nodes + the fold (circle r 3 inflated by 0.6 m: π·3.6² = 40.7 m² × 16 = about 650) | about 800 × 6 B, about 5 KB |
| Labels (humanoid) | about 800 rows × about 8 runs × 12 B | about 77 KB |
| A* scratch | g 640,000 × 8 + stamp × 4 + dir × 1 = 8.3 MB, + heap ≤ 160,000 × 16 B = 2.6 MB | about 11 MB (per simulation; a two-world test holds two) |
| Full build | memset of 640 KB; stamping ≈ Σ inflated areas × 16 nodes/m² × 3 classes. Den rocks about 3 × 320 m², rim rocks about 5 × 25, walls about 20 × 20, trees 32 × 4 → about 1,500 m² → about 72,000 integer tests | under 2 ms (asserted under 20 ms) |
| Tile rebuild (placement) | 160,000 init + the tile's stamps; 200 greybox pieces (RK-06) at about 440 nodes × 3 → about 264,000 tests | ≤ 2 ms per tile; ≤ 4 tiles + labels ≤ 1 ms → **≤ 9 ms per edit, at drain, not per tick** |
| A* typical (settlement, 20-40 m with a detour) | 1,000-4,000 expansions × about 250 ns | 0.25-1 ms |
| A* worst (budget) | 40,000 × 250 ns | about 10 ms, then 40 ticks of cooldown |
| Periodic replans | 2 movers × 0.5 Hz | about 1 ms/s on average |
| Follow per tick | ≤ 10 corners × about 40 footprints | about 15 µs per mover |
| `ClosedDoors()` growth (building's join) | per mover per sub-step pass: + solid pieces (M7 greybox hut ≈ 12 boxes) | noise; at 200 pieces a broadphase is needed (§14) |

**2 km region later** (8,000 × 8,000 = 64M nodes, 400 tiles):
- A fully resident Fit would be 64 MB, so tiles are never all resident.
- With the tier-A radius at 150 m (`content/config/simulation_tiers.yaml:7`) the window is at most 4 × 4 = 16 tiles: 2.5 MB of Fit + 0.3 MB of gate bits.
- Dense wilderness of 2,000 props per cell × about 64 nodes × 3 classes is about 384,000 tests, or 2-3 ms per tile build on promotion. A cell-crossing promotes up to 4 tiles, so about 10 ms, amortised at 1 tile per tick.
- Window labels over 2.56M nodes take about 3 ms.
- Long paths leave tier A and belong to the tier-B portal graph.

**Counters** (`NavCounters`; deterministic *work counts*; `Simulation.NavStats` is an immutable snapshot):
- builds: `TilesBuilt`, `NodesInitialised`, `StampTests`, `GateCells`, `LabelRuns`, `LabelPasses`;
- queries: `Plans`, `PlansByReason[8]`, `Expansions`, `MaxExpansionsOneQuery`, `HeapPushes`, `Disconnected`, `Exhausted`, `StartBlocked`, `GoalBlocked`;
- follow: `PullTests`, `ValidityTests`, `Follows`, `OpenGateRequests`;
- placement: `PlacementChecks`, `PlacementRefusals`.

There is no `Stopwatch` in `src/World` or `src/Domain`; tests time things themselves, as `CreatureTests.cs:526-544` does.

**Rule.** Presentation-side probes and placement previews use a **separate** `Navigator` instance with its own scratch and counters. The simulation's counters therefore stay equal between a UI-driven run and its replay.

---

## 17. Q10: Acceptance tests

### Domain (`tests/Domain.Tests/Spatial/NavigationTests.cs`: synthetic geometry, no content)
- **D1. Simple reachable path.** A 20 × 20 m open field, (2, 2) → (18, 18). One corner, which is the goal; the cost equals the octile distance.
- **D2. Blocked path.** A wall across the field with a 1.6 m gap. The route passes the gap, and every segment is `SegmentClear` at r. Driving `Kinematics.Step` with the follower reaches the goal within 1.3 × (length / speed) ticks, and at every tick end `Separation(body, r)` is null for every footprint.
- **D3. Sealed wall.** The result is `Unreachable(Disconnected)` with **zero** expansions, and `RetryTick = tick + 40`. No replan happens before `RetryTick` (counter).
- **D4. Structure placed across the route.** Plan; add a box across corner segment 2; `Rebuild`. The next `Next` gives reason `blocked`, and the new corners avoid the box.
- **D5. Structure removed.** Remove the detour's cause. The route stays valid until the `periodic` replan (≤ 40 ticks), after which the new route cost is lower.
- **D6. Seam.** A window of 2 × 1 tiles (0-200 m in x), with a wall and doorway straddling x = 100 m.
  - (a) `Build(all)` has the same `Hash()` as `Build(tile0)` then `Build(tile1)`, and as the reverse order.
  - (b) Evicting tile 1 and rebuilding it gives the same hash and identical corners.
  - (c) Global node indices across tiles are disjoint and complete (count = Width × Height).
  - (d) (100,000, z) maps to tile 1.
  - (e) The route crosses the structure's outer AABB boundary exactly twice for "through" and once for "in", and never exits and re-enters (RK-14's failure mode).
- **D7. Four-cell corner.** A structure straddling (100 m, 100 m) behaves as D6 over 2 × 2 tiles (RK-A2's "four cells" case).
- **D8. Repeat and permutation determinism.** Building twice gives an identical hash. Building with the solids and gates list reversed and shuffled by a fixed permutation gives the same hash. Plans are identical.
- **D9. Body sizes.** A doorway [10.0, 11.6] m: 3 / 2 / 2 walkable lanes for humanoid / medium / large. A doorway [10.0, 11.2]: 1 / 1 / 0, so `large` is `Unreachable` and `humanoid` passes.
- **D10. Gates.**
  - A closed door blocks `OpensDoors = false` and is passable, at cost, for openers. The follower returns `OpenGate` within 1.6 m.
  - Toggling the door leaves `Hash()` unchanged.
  - A closed barrier blocks everyone: A* empties its open list and returns `Exhausted`, deterministically.
- **D11. Tie-break.** A symmetric map, with the obstacle centred on the start-goal line, gives the same asserted corners on every run, on the lower-node-index side.
- **D12. Integer equivalence.** 20,000 LCG-generated points and radii (a test-local generator): `PointClear` equals `Separation(...) is null`, for boxes and circles.
- **D13. Edge soundness.** For every walkable edge in a random cluttered field, samples every 10 mm along the edge all give `Separation(x, z, r) is null`.

### Application (`tests/Application.Tests/NavigationTests.cs`: real Ashen Hollow content)
- **A1. Lint parity.** Every protected anchor shares the spawn's label; the lodge and smithy doors have 4 humanoid lanes; the beam line is sealed for humanoid.
- **A2. Assigned NPC (the exit criterion; needs building).** Assign Kera to the placed station in a straddling hut. She walks out of the smithy (the door is shut: she opens it), across the seam, in through the hut's door (shut: she opens it) to the anchor. Then she is unassigned and walks back. It completes within bounds, with **zero teleports**, and `RoutePlanned` reasons are only `none`, `periodic` and `goal_moved`.
- **A3. Straddling seam with reload.** The companion and the NPC each path from one side of the straddling wall to the other through its doorway. Then `save` → `load`: `NavGrid.Hash()` is equal before and after, and both routes are field-equal.
- **A4. Save then continue.** With the companion mid-route (no clear view) and the NPC mid-walk: save, load, then run 400 ticks in both worlds. `StateDigest` is equal at every 50th tick (the pattern of `CompanionTests.cs:285-319`).
- **A5. Deterministic replay.** A scripted session with `PlacePiece`, `Dismantle`, door toggles and a following companion, replayed from the command log at the same boundaries (the pattern of `DeterminismAndViewTests.cs:49-78`). `StateDigest` is equal, `NavStats` equal, `NavGrid.Hash()` equal.
- **A6. Wait → follow from inside the lodge.** The trail is cleared and the character is outside with the door shut. Tavar opens the door and reaches the character with **no** `CompanionCaughtUp`. Before M7, the same script snags.
- **A7. Placement refused.** A wall across the lodge doorway is refused with a reason naming Renn's place; a wall that seals the build area's own chest anchor is refused too.
- **A8. Performance.** 1,000 deterministic random reachable pairs: median expansions and p99 logged. The asserts, on ASTRAL:
  - median plan under 1 ms and max under 12 ms;
  - full build under 20 ms;
  - `CheckPlacement` under 15 ms;
  - 200 greybox pieces in one tile: tile rebuild under 5 ms.
- **A9. Soak.** The M6 random-walk soak is re-run: `CompanionCaughtUp` with reason `snag` should fall toward zero on reachable ground. `distance` catch-ups remain; that is the design.
- **Persistence.**
  - `Schema13To14_GivesEveryCompanionNoRoute`;
  - `ASchema14CompanionWithoutARoute_IsCorrupt_NotDefaulted`;
  - a v14 fixture companion with an active 3-corner route, round-tripped.

The runtime recording: a playthrough beat builds the straddling hut, assigns Kera, and records her walk with the debug overlay (walkable nodes, gates, route polyline). That satisfies the "runtime recording" half of RK-14 without the engine being authoritative.

### Existing tests that change because of navigation

| Test | Change |
|---|---|
| `tests/Content.Tests/ValidationTests.cs:508` `LoadAll_Loads_Yaml_Files` | Add `config.navigation` to the list |
| `tests/Application.Tests/CompanionTests.cs:285-319` `HisState_RoundTrips...` | Add `Assert.Equal(before.Route, after.Route)`; extend the scenario to be mid-route |
| `tests/Persistence.Tests/CanonicalState.cs:~137` | Render `route`; regenerate all `expected.json` (v12/v13 gain `route: none`; review line by line) |
| `tests/Persistence.Tests/HistoricalFixtureTests.cs:226-227` | Assert the route (none below 14; active at 14) |
| `tests/M2.Probe/M2Fixtures.cs:152-154` | The v14 companion gets a route |
| `tests/Persistence.Tests/MigrationTests.cs` (step counts and prefixes, `:78`, `:104`...`:428`) | Shared M7 schema bump; plus the two new tests above |

Unedited but load-bearing: `SessionTests.EveryStateSlice_HasExactlyOneOwningSystem` (`:150-160`, enum-driven); `SavingIsNotAnEvent_AndLoadingPublishesNothing` (`DeterminismAndViewTests.cs:129-143`: `Build()` publishes nothing); C16, `FollowWaitFollow` and `LeftFarBehind` (behaviour changes only with nothing in clear view); every creature, behaviour-matrix, jump/crouch and `SpatialTests` case (`Kinematics` and creatures untouched); `ArchitectureTests` `:62-79`, `:85-105`, `:111-131`, `:134-155` (no WorldDelta member, no static state, no `Simulation` method, no public setter); `StateDumpTests` (more leaves, 0 differences).

---

## 18. Documents to reconcile (the first M7 slice)

- **`docs/WORLD_ARCHITECTURE.md` §11, the navmesh row (`:435`).** Replace with: "Navigation | Domain-owned deterministic clearance grid (`src/Domain/Spatial/Navigation`), derived from the region layout and structure records, tiled per 100 m cell on a global lattice so seams need no stitching, rebuilt per touched tile on structure change, never persisted; presentation never navigates (owner ruling 2026-09-24)." Move it into §5 cell facets as derived domain state.
- **`docs/WORLD_ARCHITECTURE.md` §10 (`:414`) and §5 (`:137`).** Change "navmesh ... rebuilt per cell on change, debounced" to "navigation tiles rebuilt synchronously within the placement command; doors are gates evaluated at query time".
- **RK-14 (`docs/RISK_REGISTER.md:280-296`).** The validation becomes "headless domain test D6/D7 plus Application A3 and a runtime recording; 'cell reload' is tile eviction and rebuild plus save/load; no engine". Status: proven in M7. **RK-A2**: the same.
- **`docs/PERSISTENCE.md:83`.** "Navmesh ... baking/streaming pipeline" becomes "navigation grid: derived, rebuilt in the `Simulation` constructor". §2 adds the routes-are-saved-with-their-mover note.
- **`docs/ROADMAP.md:284-285`.** "the navmesh updates on placement" becomes "the navigation grid updates on placement"; "navmesh path test" becomes "navigation path test". The entry criterion is recorded as met inside M7 (scope ruling item 14).

---

## 19. Challenges and notes on the scope rulings

- No challenge. Item 3 (90° rotation) is load-bearing here: it keeps every piece a `BoxBlocker`, so the exact integer tests and the stamping stay valid. A 45° rotation would need an oriented-box `SegmentClear`, still in integers but with twice the code and a new `Blocker` shape across `Footprints.Center`, `HollowView` and `Fitting`.
- "OPTIONAL IF CHEAP: creature return-home" is declined for M7 (§2). It is not cheap once persistence and the behaviour matrix are counted.

---

## 20. Cross-system contracts

**From building (what navigation needs):**
1. A read API giving every placed piece as `(Key, BoxBlocker Box, NavTraversal {Solid | Door | NonBlocking}, HeightMm > 0)` in world integer mm, axis-aligned (90° steps). Doors come as a leaf box separate from the jamb boxes. The API must be readable before `_navigation.Build()` in the constructor.
2. Synchronous `RebuildNavigation(AABB of old ∪ new footprints, reason)` after every committed place, dismantle or destroy. Not on damage, repair or door toggles.
3. Solid boxes and **closed** door leaves joined into `SystemContext.ClosedDoors()`, so that movement, sight, shots, companion clear-view and prediction see exactly what navigation sees. Non-blocking pieces (roof, floor pad) appear nowhere.
4. The door command reached from `InteractionSystem` for both `InteractCommand` and the new `OpenDoor`. The "cannot close: something is in the doorway" check (`Systems.cs:267`) extended from the player's body to every body.
5. Use anchors (`NavAnchor(Key, XMm, ZMm)`) for the chest and the station work anchor.
6. `CheckPlacement` called at command time. Previews use presentation's own `Navigator`.
7. Snap steps in multiples of 250 mm; doorway clear width ≥ 1.6 m (navigation guarantees ≥ 1.05 m).

**From the assignment owner:**
- persist the NPC body and `NavRoute` (§12.3);
- exclude assigned NPCs from `NpcSystem.Tick` turning and from site re-placement in `Populate`;
- never teleport.

**Offered to presentation:**
- `Simulation.Navigation` (an immutable `NavGrid`);
- `Simulation.NavStats`;
- the events `NavigationRebuilt` and `RoutePlanned`;
- `CompanionView.Route` (the corners) for the debug overlay;
- pure Domain functions for probes, run on a separate `Navigator`.

**Persistence:** `NavRouteDto` in `CompanionDto` and in the assignment DTO (§13.1); nothing else.

**Factions:** none in M7. `NavGate` has room for an access policy later, and it may never feed hostility.

## 21. Implementation order

| Slice | Content | Gate |
|---|---|---|
| N1 | Domain (§5-§7, §11) | Tests D1-D13 green |
| N2 | Config and lints | — |
| N3 | Runtime wiring (§13): slice, system, `Navigator`, constructor build, properties | A1 |
| N4 | Companion route and schema-14 companion field | A4, A6, persistence tests |
| N5 | Building joins (§20.1-4, 6) | A7 |
| N6 | Assigned NPC mover and `OpenDoor` | A2, A3, A5 |
| N7 | Debug overlay, playthrough recording, perf and soak evidence | A8, A9 |

## 22. Owner questions and open issues

Owner questions:
1. **Doors.** May the companion and the assigned NPC open a closed door (default: yes)? Do they leave it open (default), or close it behind them? Closing needs a persisted "door I opened" field and a body-clear check.
2. **Companion last resort.** Keep the snag and distance catch-up as the companion's fallback when navigation reports `Unreachable`, for example after the character jumps the timber into a pocket (default: keep)? The assigned NPC never teleports.

Open issues:
- Placed pieces join the dynamic blocker list scanned by every `Kinematics.Step` pass. RK-06's 200 pieces will need an additive broadphase overload.
- Barriers are optimistic in the labels, so an unreachable goal behind a standing barrier costs one budgeted A* (≤ 10 ms) per 40 ticks.
- The 11 MB region-sized A* scratch is an M7 simplification; 2 km needs budget-bounded maps.
- Openings of 0.8-1.05 m may be missed at 250 mm resolution.
- The existing `Atan2`/`Sin`/`Cos` gap in facing remains; navigation does not widen it.
- The build area must avoid the scripted playthrough routes (`src/Presentation/Playthrough.cs:48-69`). A content test should assert that no waypoint segment crosses it. Candidate: x 92-108, z 122-140.
- `NavTile.Hash` should be computed lazily, since only tests read it.
