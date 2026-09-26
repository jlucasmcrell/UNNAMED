## 14. Performance considerations

Units: mm, ticks (20 Hz), ms of main-thread time. Number labels: **[computed]** is arithmetic from constants verified in source or content at `e10d2c4`; **[model]** and **[bench]** are section 3's scratch-model counts and scratch-harness times on ASTRAL; **[est]** is an estimate that a named test replaces with a measurement; **[measured]** is a number already in the repository's evidence. Nothing M7-related has been measured, and nothing at all has been measured on RAZER (`docs/M6_STATUS.md:221`).

### Decisions

- The frame of reference is a 20 Hz tick, a 16.7 ms frame at 1080p60 on RAZER (RTX 4070 Ti), ≤ 4 ms of main-thread world systems per tick and ≤ 2 ms P99 per save.
- Until the RAZER window measures the CPU ratio, every M7 cost is designed to half its budget on ASTRAL (a 2 ms tick target).
- The authority never reads a clock. Timing exists only in tests and in Presentation (`AuthorityNeverReadsAClock`).
- The required instrumentation is exactly L4's list: `NavCounters`, `FrameStats` `sim_ms`/`ticks`/`save_ms`, N-A10, T2, T10, the RK-06 200-piece round trip, and lint BLD009.
- Every other instrument is optional, with a written switch-on condition (§14.13).
- CI asserts deterministic counts exactly, and times only at 3× the ASTRAL target. The tight numbers are logged evidence in `M7_STATUS`.
- `max_expansions` is 65,536. A tick that plans Kera's walk home exceeds the 4 ms tick budget by one plan; this is accepted in M7. The per-tick plan budget stays unbuilt unless the RAZER window measures a hitch.
- The piece ceilings are 256 per build area (`max_pieces`), and BLD009 holds the sum over areas at ≤ 256 per cell and per region until an order-preserving broadphase exists.
- Navigation costs ≈ 12.5 MB per simulation: 1.28 MB of tiles, and scratch that is allocated lazily.
- No optimisation lever is built until a measurement names it (§14.15).

### 14.1 Frame of reference

**Clocks.**
- The tick is 20 Hz (`content/config/time.yaml:7`), so 50 ms.
- `GameSession.Frame` drains commands every frame, then steps whole ticks, draining after each (`src/Application/GameSession.cs:183-191`). At 60 fps one frame in three carries a tick.
- A command's cost lands in the frame it was submitted in, whether or not that frame carries a tick.
- After a stall the frame is clamped to 0.25 s (`GameSession.cs:52`), so at most 5 ticks run in one frame.
- Autosave runs inside `Frame` every 300 s of playtime (`GameSession.cs:194`; `src/Persistence/SaveStore.cs:41`).

**Budgets** (`docs/WORLD_ARCHITECTURE.md` §12, explicitly unmeasured):

| Budget | Value | Source |
|---|---|---|
| Target frame | 16.6 ms at 60 fps, 1080p | WA `:448` |
| Main-thread world systems | ≤ 4 ms | WA `:449` |
| Streaming work | ≤ 3 ms per frame | WA `:450` |
| World content memory | ≤ 1.5 GB | WA `:454` |
| Save main-thread cost | ≤ 2 ms P99 | WA `:455`; `docs/PERSISTENCE.md:533`; T-25 (`:599`) |

**Machines.**
- ASTRAL: RTX 5090 with a Ryzen 9 9950X3D (`docs/M3_STATUS.md:61`). It runs CI-equivalent tests and every trial. It is not the gate.
- RAZER: RTX 4070 Ti. Its CPU is recorded nowhere yet; `FrameStats`' machine block records it (`src/Presentation/Perf/FrameStats.cs:151`).
- CI: `ubuntu-latest` (`.github/workflows/dotnet.yml:14`), slower than ASTRAL, with no Godot.
- **Planning assumption:** RAZER's main thread may be up to 2× slower than ASTRAL's, so every ASTRAL target is half its budget.

**Measured today (ASTRAL):**
- 60 creatures cost 0.43 ms a tick (`docs/M3D_STATUS.md:53`); `SixtyCreatures_TickWithinTheBudget` asserts < 4 ms (`tests/Application.Tests/CreatureTests.cs:526-542`).
- The `--perf` trial ran at 798-840 fps, with a p99 frame of 3.5-4.1 ms and a GPU p99 of 0.21 ms (`docs/M6_STATUS.md:85`).
- The worst process time each second is 8-9 ms. Its make-up is unknown, because `FrameStats` has no simulation column (`FrameStats.cs:78`).

**Timing never enters authority.** The only clock read in `src/Domain`, `src/World` and `src/Application` is `EntityId.NewId` (`src/Domain/EntityId.cs:52`); World exposes work counts only (§14.12.6; STOP S3).

### 14.2 Navigation memory

| Item | Arithmetic | Size |
|---|---|---|
| Node | `SolidFit` + `ClosedFit` | 2 B |
| Tile (one 100 m cell) | 400 × 400 = 160,000 nodes, stored as two 160,000-B layers | 320,000 B |
| Ashen Hollow (4 cells, one region) | 4 × 320,000 | 1,280,000 B (1.22 MiB) [computed] |
| Inputs | ~64 B each. 67 authored (64 structures, 2 doors, 1 barrier) ≈ 4.3 KB. A full area adds ≤ ~385 (≤ 370 solid parts plus leaves). A straddling input is listed in up to 4 tiles | ≤ ~100 KB |
| Authoritative scratch (lazy: first plan or flood) | window 262,144 nodes × 9 B (`G`, `Generation`, `Dir`) = 2,359,296; heap (8 × 65,536 + 1) × 12 B = 6,291,468; flood queue 16,384 × 4 B = 65,536 | 8,716,300 B (8.3 MiB) [computed] |
| Preview scratch (`Simulation._previewScratch`, lazy: first ghost check) | floods only: window arrays + queue | ≈ 2,424,832 B (2.3 MiB) [computed] |
| **Per simulation** | | **≈ 12.5 MB**, 0.8 % of the 1.5 GB world-content budget |

- A test boot that never plans or floods pays only the tiles. Two simulations in one process own separate scratch (G17, N-W3).
- Scratch does not grow with the region: the window is capped at 512 × 512 nodes, and the heap at the expansion cap.

**At 2 km (M9; nothing here is built in M7):**
- Every tile resident would be 400 × 320,000 = 128 MB. That never happens.
- Residency follows tier A: ≤ 21 tiles within 160 m [model] = 6.72 MB, plus ≈ 11.1 MB of scratch, plus forested input lists (≤ 21 × 2,000 trees × 64 B ≈ 2.7 MB): about **20 MB**.
- A tile built on promotion costs 1-2 ms [est], one per tick, inside the 3 ms streaming budget.

**Large-object heap.** Each 160,000-B layer exceeds .NET's 85,000-B threshold.
- A copy-on-write footprint edit therefore allocates two large-object arrays per touched tile: 320 KB to 1.28 MB per edit.
- Building the 256-piece full-area layout (§14.12.3) makes 94 footprint edits: ≈ 94 × 1.5 tiles × 320 KB ≈ **45 MB** of large-object garbage over the build [computed]. Pads and roofs rebuild nothing.
- At a human build rate (≤ 2 placements a second) this is ≤ ~1 MB/s. T2 logs gen-2 collections; the lever is §14.15 item 3.

### 14.3 Planning frequency and cost

| Planner | When it plans | Rate bound |
|---|---|---|
| Kera's errand (`NpcSystem`, `Errands.cs`) | only in `to_work` and `to_home`, on the triggers of §3.7.6 | 1-3 plans per trip |
| Tavar (`CompanionSystem`) | only in Route or Nav mode: neither the character nor a trail mark is in clear view | ≤ ~1.6 plans/s while the character runs out of view |
| Assign refusal 10 (`NavigationSystem.Reachable`) | once per `AssignWorkerCommand` | human-paced |
| Placement check 15 and the ghost | floods, not A* | §14.8 |
| Creatures, the player, static NPCs | never | 0 |

**Trigger throttles** (§3.7.6): `retry` every 40 ticks; `goal_moved` at most every 10 ticks, and the goal moves 2 m in 12.5 ticks at a run (160 mm a tick), so ≈ 1.6 plans/s; `stuck` every 20 ticks while stuck; `off_line` at most every 10 ticks; `geometry` once per edit whose rectangle meets the route's watch window; `none` and `partial` once per goal. `NavFollower.Next` plans at most once per call, so each mover plans at most once a tick, and M7 plans at most twice a tick.

**Cost per plan** (person, opener; §3.7.7):

| Route | Expansions | Time |
|---|---|---|
| Kera's site → bench anchor, workshop built | 22,685 [model] | 3.1 ms [bench] |
| Anchor → site (the walk home), the worst real M7 route | 40,751 [model] | 4.6 ms [bench] |
| Anchor → site after step 9's wall at x = 96 m | 40,639 [model] | - |
| Worst authored Ashen Hollow route (west of the lodge → Renn) | 9,709 [model] | 0.8 ms [bench] |
| Kera's site → (100.75, 96.0), no workshop | 1,179 [model] | - |
| A plan that reaches the cap | 65,536 | 6-9 ms [bench] |
| An enclosed goal | 0 (the probes decide it, ≤ 2 × 2,048 probe nodes) | ≤ 0.1 ms [est] |

Throughput is 80-140 ns per expansion [bench]; N-A10 re-measures it and logs ns per expansion.

**Average cost per tick** [est]: the companion's plans ≈ 1.6/s × ~0.3 ms ≈ 0.024 ms; following ≈ 10-20 µs per mover; Kera ≈ 7.7 ms of planning per ~2,600-tick round trip ≈ 0.003 ms. Total **≈ 0.05-0.07 ms a tick**.

**Worst single tick:**
- **Release.** The tick after `ReleaseWorkerCommand` plans the walk home: ≈ 4.6 ms [bench]. That exceeds the 4 ms tick budget by one plan and fits the 16.7 ms frame on ASTRAL. On RAZER it is ≈ 9 ms under the ×2 assumption.
- **An edit.** An edit at boundary N whose rectangle meets both movers' watch windows makes both replan at N+1 (companions, then NPCs; `Simulation.cs:326-327`). Plausible worst ≈ 5-6 ms [est].
- **The theoretical worst** is both searches exhausting the cap, ≈ 12-18 ms. It needs two unreachable, unenclosed goals at once. Shipped content cannot produce it: every authored pair and Kera's three workshop routes stay ≤ 43,690 expansions (N-A10).

**Rule.**
- One frame between 16.7 and 33 ms on a tick that plans a long route is accepted in M7. It happens only on a release or an edit, which are human-paced.
- A hitch over 33 ms is not accepted.
- STOP (§3.18): Kera's walk-home plan over 6 ms on ASTRAL.
- The per-tick plan budget is switched on only by §14.15 item 2's trigger.

### 14.4 Rebuild scope and cost after construction

The restamped nodes are those whose centres (250·i + 125) lie in the parts' union AABB inflated by 600 mm (`NavConfig.InfluenceMm`), clipped to each tile the rectangle meets (§3.5.2).

| Piece | Parts AABB | Nodes restamped | Tiles touched |
|---|---|---|---|
| Wall; doorway (the union of its jambs) | 3.4 × 0.4 m | 18 × 6 = 108 | 1-2 |
| Door leaf | 1.6 × 0.4 m | 12 × 6 = 72 | 1-2 |
| Chest, bench | 1.0 × 0.6 m | 56-63 | 1-4 |
| Pad, roof | none | 0: no `RebuildNavigation` is sent | 0 |

Walls never touch 4 tiles: the nearest lattice lines to the 100 m seam are 99 m and 102 m, and a wall's inflated face stops at 99.8 m.

**Cost per footprint change** [est]:
- ≤ 540 `FitAt` evaluations (108 nodes, ≤ 5 inputs each), ≈ 5-11 µs;
- one copy-on-write of 320,000 B per touched tile, ≈ 15-30 µs;
- refiltering and stamping each touched tile, ≈ 10-20 µs.
- Total **≈ 0.05-0.2 ms**. The ASTRAL target is < 2 ms, asserted in CI at < 6 ms (N-A10).
- Building's own `Rebuild()` (sorting ≤ ~434 static blockers, the socket index, the footprints) adds ≈ 0.1-0.5 ms at the cap [est], inside T2's logged command drain.

**Full build** (every new game and every load, in the `Simulation` constructor):
- a 640,000-node fill, plus 20,756 authored evaluations [model], plus piece evaluations: ≈ 9,100 for the full-area layout [computed], ≈ 18,000 for the densest legal area;
- ASTRAL < 20 ms, CI < 60 ms (N-A10).

**Nothing rebuilds on** door toggles, barrier lifts, damage that leaves health above 0, repair, assignment, release, errand phase changes or loads (§6.2.2, G14).

### 14.5 Piece ceilings, and why

| Layout | Pieces | Solid parts (static blockers added) | Leaves | Timber | Source |
|---|---|---|---|---|---|
| Crossing Workshop after step 1 | 19 | 11: 7 walls, 2 jambs, bench, chest | 1 | 31 | §4.22 |
| Crossing Workshop at the end | 23 | 13 | 1 | - | §4.22 |
| RK-06 round trip | 200: 81 pads, 37 walls, 1 doorway, 81 roofs | 39 | 0 | 238 | §4.22 |
| Full-area layout (T2) | 256: 81 pads, 81 roofs, 58 walls, 14 doorways, 14 doors, 7 chests, 1 bench | 94 | 14 | 338 | §14.12.3 |
| Densest legal area | 246: 41 pads in a checkerboard, 164 doorways, 41 furniture | ≈ 369 | 0 | 451 | [computed] |

**The densest area.** In a 9 × 9-square area, a checkerboard of 41 pads (dark corners) offers all 144 interior edges and 20 of the 36 boundary edges. Those 164 edges hold 164 doorways, whose jambs are 328 solid parts, and the 41 pad squares hold one piece of furniture each. Doorways are openings, so no pocket seals and every placement is legal. Adding pads trades one-for-one, so one area holds at most **~370 solid parts**.

**Shipped content limits real play far below the ceiling.** The only timber source is the 80-timber stack, and a dismantle refunds 50 % rounded down. A new game can therefore place at most 80 pieces (all pads), or about 40 walls. The 256 ceiling is a backstop for crafted starts and later content.

**Ceilings** (RK-06 asks for "a piece-count ceiling per cell ... treated as a content constraint", `docs/RISK_REGISTER.md:158`):
- **per build area:** 256 (`max_pieces`, placement check 13);
- **per cell:** the sum of `max_pieces` over the areas that meet the cell ≤ 256 (`BuildingConstants.PiecesPerCellCeiling`, lint BLD009);
- **per region:** the same sum ≤ 256 (`PiecesPerRegionCeiling`, BLD009), until an order-preserving `Kinematics` broadphase exists.

**Why.** `Kinematics.Resolve` walks every static blocker in the region on every pass (`src/Domain/Spatial/Kinematics.cs:183-198`), and pieces join that list region-wide. One densest area costs ≈ 0.4-0.8 ms of collision a tick at 60 tier-A creatures (§14.6). Four would cost ≈ 2-3 ms, which breaks 4 ms once creature AI and sight are added. Ashen Hollow's single area meets both ceilings as authored.

### 14.6 Tick cost with pieces: collision and sight

**`Resolve`.** Each sub-step makes ≤ 4 passes over every static blocker, then the dynamic ones. Sub-steps are ⌈travel / (r/2)⌉: 1 at a run (160 mm a tick), 2 at a sprint (256 mm). The bodies stepped each tick are the player, the tier-A creatures (13 placed by content, 60 at the cap), the companion and the errand NPC. Checks per tick ≈ bodies × ~1.5 passes × static blockers [est]:

| Scene | Static blockers | 16 bodies | 63 bodies |
|---|---|---|---|
| Ashen Hollow today (64 structures) | 64 | ≈ 1,500 | ≈ 6,000 |
| Crossing Workshop at the end | 77 | ≈ 1,850 | ≈ 7,300 |
| Full-area layout | 158 | ≈ 3,800 | ≈ 14,900 |
| Densest legal area | 433 | ≈ 10,400 | ≈ 40,900 |

At 10-20 ns per `Blocks` + `Push` [est], that is ≈ 0.15-0.3 ms for the full area and ≈ 0.4-0.8 ms for the densest area, at 60 creatures. Creature-to-creature pushes are O(C²) and do not depend on pieces.

**Sight.** Creatures, combat and the companion read `SightWalls()` = `Space.Blockers ∪ ClosedDoors()`. The worst case is every creature with the player in range and in its cone: 60 × ~175 walls ≈ 10,500 `Crosses` (full area) ≈ 0.2 ms, and 60 × ~436 ≈ 26,000 (densest) ≈ 0.5 ms [est]. `ClosedDoors()` builds a new array per call (`src/World/Runtime/Systems.cs:48`). Closed piece leaves are appended from the cache, so the allocation grows only with closed piece doors.

**Total at the tier-A cap** [est]: 0.43 [measured] + collision + sight + navigation ≤ 0.07 ≈ **1.0 ms** (full area) and **1.8 ms** (densest), against 4 ms. Both meet the ASTRAL half-budget. T2 measures the first.

### 14.7 Faction update complexity (report-only)

- **Per tick: zero.** `FactionSystem` has no `Tick`.
- **`RecordAct`:**
  - an ordinal sorted-dictionary lookup of `kind|subject` (2 pairs in shipped content); an irrelevant act returns at once;
  - a relevant act does one immutable append O(A) and `Compact` O(A + K), with A ≤ 256 acts and K ≤ 512 knowledge rows (2 factions × 256);
  - no sight, body, facing or wall test (`FactionSystem_ReadsNoSightNoBodiesAndNoFacing`);
  - microseconds [est].
- **`ReportAct`:** m matching acts (1 per subject in shipped content, ≤ 256 at the cap), each an O(A + K) `Learn`. At the cap that is ≈ 200,000 comparisons, ≈ 0.2-0.5 ms [est], once per dialogue choice.
- **Gates:** `StandingLevel` walks the 11-tier ladder, and `StandingOf` reads ≤ F rows. `Withheld` runs per ware on each `Wares` call, which means per frame while the trade panel is open. The cost is trivial.
- **Scale:** FAC-R5 holds shipped play to 2 acts, so the ledger stays under 1 KB. The M9 witnessed channel adds per-member sight tests; that cost is measured when it is built (FactionCounters, T8; §14.13).

### 14.8 Placement validation: the preview against the command

| Path | Work | Cost | How often |
|---|---|---|---|
| Preview, checks 1-14 | `Snapper.Snap`; 169 terrain samples (pads only); overlap against 64 authored statics, 2 doors and 1 barrier; ≤ ~65 bodies; a count of ≤ 256 pieces; ≤ 24 stacks | ~5-20 µs [est]; ASTRAL target ≤ 0.05 ms | every frame in build mode |
| Preview, check 15 | `NavEditCheck.Check` on `_previewScratch`, no counter sink: V-N1 floods of ≤ 16,384 nodes at ~20-40 ns per node; ≤ 4 floods when a pocket seals | median ≤ 2 ms, worst ≤ 10 ms (N-A10) | only when (definition, pose) or `StructureRevision` changes, and otherwise at most every 0.5 s; 3-5 times a second while the aim sweeps |
| `PlacePieceCommand` | checks 1-15 (`CheckEdit` on the authoritative scratch, counted), `ExchangeItems`, the commit, `Rebuild()`, `RebuildNavigation`, the events | `CheckEdit` + 0.2-0.7 ms [est]; it drains in the frame it was submitted in | human-paced, ≤ ~2 a second |
| Dismantle, repair | no navigability check; a dismantle rebuilds | ≈ 0.2-0.7 ms [est] | per command |

**Budget lines:**
- The per-frame preview costs ≤ 0.1 ms, 0.6 % of a frame.
- A worst-case sweep costs ≤ 5 × 10 ms = 50 ms of navigability checks a second, and typically 3-10 ms. §14.15 item 1's debounce is the lever.
- A command frame stays ≤ 16.7 ms on RAZER (no dropped frame), and in any case < 33 ms (no counted hitch, `FrameStats.cs:119`).

### 14.9 Presentation cost per piece

Per-piece counts come from the greybox builder (§9.3.5). A `Solid` is a `MeshInstance3D`, a `StaticBody3D` and a `CollisionShape3D`, with a new `BoxMesh` and `BoxShape3D` each.

| Piece | Nodes | Mesh instances | Camera colliders |
|---|---|---|---|
| Pad | 1 | 1 (a draped `ArrayMesh`) | 0 |
| Wall, roof, chest | 3 | 1 | 1 |
| Doorway | 9 | 3 (2 jambs, lintel) | 3 |
| Door | 4 | 1 | 1 (turning) |
| Bench | 4 | 2 (box, iron block) | 1 |

| Layout | Nodes | Mesh instances | Camera colliders [computed] |
|---|---|---|---|
| Crossing Workshop at the end (23) | 65 | 26 | 19 |
| RK-06 round trip (200) | 444 | 202 | 121 |
| Full-area layout (256) | 705 | 285 | 203 |

The authored structure layer today is ≈ 200 nodes (64 structures × 3, plus doors and roofs), so a full area is ≈ 3.5× that.

**Draw calls.**
- Each mesh instance is drawn in the opaque pass, the depth prepass and the sun's shadow pass; the sun casts shadows (`src/Presentation/Greybox/HollowView.cs:357`).
- Distinct `BoxMesh` resources are not batched [est]. A full area in view adds up to ≈ 850 draw calls [est].
- The GPU is not the risk (ASTRAL GPU p99 0.21 ms). The render thread's CPU is (`render_cpu_ms`, `FrameStats.cs:67`).

**Other costs.**
- Colliders are static (layer 1, mask 0) and are not simulated.
- `StructuresView` adds or frees ≤ 9 nodes per event. The first use of a mesh or material may compile a pipeline.
- Shipped timber holds real play to ≤ 80 pieces and ≤ ~330 nodes (the node-heaviest spend is doorways, 4.5 nodes per timber, each needing a pad).
- **Art later:** 5-25k triangles per building module (`docs/WAVE_0_MODULAR_ASSET_STANDARD.md:342`) × 256 pieces = 1.3-6.4 M triangles at full detail. Re-measure when the owner releases the kit.

### 14.10 Save and load growth

Sizes are for MessagePack with string keys and no compression [computed]. A ULID is 26 characters plus a 4-character prefix; a cell key is 13 characters.

| Record | Bytes |
|---|---|
| `PieceDto` (key + value: `instance_id` 12 + 31, `def_id` 7 + 18-21, `host_cell` 10 + 14, `x_mm` 5 + 5, `z_mm` 5 + 5, `rotation` 9 + 1, `owner` 6 + 31, `health` 7 + 1-3, `door_open` 10 + 1, map header 1) | ≈ 180 |
| `structure_seq` | ≤ 23 |
| `NavRouteDto` | ≈ 70 + ≤ 16 per corner: 70-582 (§3.9) |
| `NpcErrandDto`, route included | ≈ 220 + route |
| `ActDto` | ≈ 125 |
| `KnowledgeDto` | ≈ 131 |
| `StandingDto` | ≈ 50 |

**Growth.**
- **Per piece:** linear at ≈ 180 B. The bound asserted is ≤ 250 B a piece (`TwoHundredPieces_RoundTripAndStayNavigable`: ≤ 200 × 250 B). A 256-piece area adds ≈ 46 KB against an entities budget of 2-40 MB (`docs/PERSISTENCE.md:551`). A piece chest adds a schema-6 `ContainerDto` only once it is used.
- **Per act:** an act learned by two factions costs ≈ 125 + 2 × 131 + ≤ 2 × 50 ≈ 390-490 B. Shipped play (≤ 2 acts) adds < 1 KB. At the 256-act cap the ledger reaches ≈ 100 KB, half of `player.msgpack`'s 200 KB upper budget (`:549`), and only later content can reach it.

**Time** [est]:
- Encoding ≈ 46 KB of pieces and ≈ 100 KB of ledger at the caps takes ≈ 0.1-0.5 ms.
- The unmeasured part is the per-file disk flush (`SaveStore.cs:413`) and the directory moves (`:430`). Both predate M7 (R-X14).
- A load adds the same decode, plus ≤ 0.5 ms for `_building.Populate()`, plus the full build (§14.4).

### 14.11 Budget sheet

| Cost | When | ASTRAL target | CI assert | Budget it belongs to | Proof |
|---|---|---|---|---|---|
| Tick: full area, 60 creatures, both movers | every tick | mean ≤ 2 ms | mean < 6 ms | ≤ 4 ms a tick (WA `:449`) | T2 |
| The tick that plans the walk home | on release | plan ≤ 6 ms (STOP) | plan < 18 ms | 4 ms a tick, exceeded by one plan (accepted, §14.3) | N-A10 |
| Edit tick (movers replan) | after a footprint change | ≤ 6 ms | logged | as above | T2 (log) |
| A plan at the cap | never in shipped content | ≤ 10 ms, logged | not asserted | - | N-A10 |
| One piece's `RebuildNavigation` | per footprint change | < 2 ms | < 6 ms | frame | N-A10 |
| Full build | new game, load | < 20 ms | < 60 ms | load | N-A10 |
| `CheckEdit`, command and preview | per command; per pose change | median ≤ 2 ms, worst ≤ 10 ms | worst < 30 ms | 16.7 ms frame | N-A10 |
| Placement command drain | per command | median ≤ 2 ms, max ≤ 10 ms | logged | frame; 33 ms hitch line | T2 (log) |
| Preview, checks 1-14 | every frame in build mode | mean ≤ 0.05 ms | logged | frame | T2 (log) |
| `RecordAct`; `ReportAct` at the cap | per act; per report | µs; ≤ 0.5 ms [est] | - | frame | optional T8 |
| Save capture + encode (no disk) | per save | p99 ≤ 1 ms | logged | ≤ 2 ms P99 (WA `:455`) | T2 (log); `save_ms` on RAZER |
| Navigation memory | resident | ≈ 12.5 MB | - | ≤ 1.5 GB (WA `:454`) | §14.2 |
| Save growth | per piece | ≤ 250 B | exact bound | entities 2-40 MB | RK-06 round trip |
| Presentation | every frame | measured only by the optional structures capture | - | 16.7 ms at 1080p60 on RAZER | §14.14 |

### 14.12 Required instrumentation (L4)

#### 14.12.1 `NavCounters`

- `NavCounters` (§3.17) is the only instrument in the authority: `FullBuilds`, `RectRebuilds`, `TilesRestamped`, `NodesRestamped`, `Plans`, `PlansByOutcome`, `Expansions`, `MaxExpansionsOneQuery`, `EditChecks`, `EditRefusalsByRule`, `FloodNodes`.
- **L4's "placement-check timings"** are realised as the placement-check work counts (`EditChecks`, `EditRefusalsByRule`, `FloodNodes`) plus test-side `Stopwatch` timings in N-A10, because World never reads a clock.
- Never saved, digested, dumped or read by a decision (G15 `CountersAreNeverRead` covers `NavCounters` only); previews never count (G7). F2, N-A9, N-A10, `PreviewsInterleaved_ChangeNothing` and T10 read it through `Simulation.Navigation.Counters`.

#### 14.12.2 `FrameStats`: `sim_ms`, `ticks`, `save_ms`

`src/Presentation/Main.cs` and `src/Presentation/Perf/FrameStats.cs` change as follows (Presentation only):

```csharp
// src/Presentation/Perf/FrameStats.cs
public readonly record struct FrameProbe(double SimMs, int Ticks, double SaveMs);
public void Record(double delta, in FrameProbe probe);   // replaces Record(double delta) (:41)

// src/Presentation/Main.cs, around _session.Frame (:305) and _stats?.Record (:309)
var clock = Stopwatch.StartNew();
var frame = _session.Frame(/* unchanged argument */);
double simMs = clock.Elapsed.TotalMilliseconds;
double saveMs = _quickSaveMs                       // QuickSave() (:930), timed where it is called (:486); 0 when none ran
              ?? (frame.AutosavedTo is null ? 0 : simMs);   // an autosave runs inside Frame: an upper bound
_stats?.Record(delta, new FrameProbe(simMs, frame.TicksRun, saveMs));
```

- **CSV:** `,sim_ms,ticks,save_ms` is appended after the nine existing columns (`FrameStats.cs:78`), so older readers keep working.
- **Summary, per segment:** `sim_ms` as a distribution over all frames and over tick frames (`ticks ≥ 1`); `max_ticks_in_a_frame`; `save_frames`; `save_ms_max`. The machine block is unchanged.
- Presentation never times `Step`, whose literal call it may not make (`ArchitectureTests.cs:163`). The `Stopwatch` lives in `Main`, never in `src/Application`, so `save_ms` for an autosave is `sim_ms` of that frame.
- `--perf` keeps its route and segments, so M7 captures compare with M6's.

#### 14.12.3 Headless budget tests

Home: `tests/Application.Tests/M7BudgetTests.cs` (T2, T10). N-A10 `Navigation_StaysWithinBudget` is section 3's (§3.18), and its assertions are not repeated here.

**The CI rule.** Deterministic counts are asserted exactly or as upper bounds. Times are asserted only at 3× the ASTRAL target. Every time is logged through `ITestOutputHelper`, as `CreatureTests.cs:539` does, and the ASTRAL numbers go into `M7_STATUS` against §14.11.

**The full-area layout** (helper `FullAreaLayout.Build`, test-local, in `tests/Application.Tests/FullAreaLayout.cs`). It is required as T2's setup, and the optional T11 reuses it.

| Group | Count | Poses (mm; rotation) |
|---|---|---|
| Pads | 81 | every square anchor (3000·i + 1500, 3000·k + 1500), i and k in 29..37; r0 |
| Doorways | 14 | 12 internal, one in the middle edge of each shared room wall: (96000 or 105000, z) r1 and (x, 96000 or 105000) r0, with the other coordinate in {91500, 100500, 109500}; 2 exterior: (87000, 100500) r1 and (114000, 100500) r1 |
| Walls | 58 | every other edge on the lines x, z ∈ {87000, 96000, 105000, 114000} |
| Doors | 14 | one per doorway, at its anchor, with r of the same parity |
| Chests | 7 | the centre square of every room except the centre room and the south-east room ([105, 114] × [87, 96] m); r0 |
| Bench | 1 | (100500, 100500) r0, over both seams; work anchor (100500, 100250) facing 0 |
| Roofs | 81 | every square: first the 72 with a wall or doorway edge, then the 9 room centres (depth-1 support) |

- **Start:** a crafted `PlayerRecord` at (100.5, 94.5) carrying 17 stacks of 20 timber (340). Weight is checked only when items enter the pack. Tavar is recruited and ordered to wait at (91.5, 107.5) in the north-west room.
- **Order:** pads, doorways, walls, doors, chests and bench, roofs. The player's poses are the implementer's (reach 6 m, never inside a part).
- Every placement must be accepted. A refusal fails the helper, naming its rule and reason. It spends 338 timber.
- **Helper asserts:** 256 intact pieces; `StructureSequence` 256; exactly 94 `NavigationRebuilt` (one per piece with parts; criterion 4 at scale); `StructureAudit` empty; every room centre `Reachable` (person, opener) from (84.0, 100.5).

**T2 `TheCapWorkshop_SixtyCreaturesAndTwoMovers_TickWithinTheBudget`** (a sibling of `SixtyCreatures_TickWithinTheBudget`, which stays unmodified):
1. Open W0 with `Arena.OpenCreatures(session, session.Setup, (100.5, 94.5), 0, [], change: the crafted start)`, which has no creatures. Run `FullAreaLayout.Build`. Log each command's `Submit` + `Frame(0)` drain time (median, max): the T3 fold.
2. In W0, log 1,000 `PreviewPlacement` calls without navigability (cycling the 81 squares, 7 definitions and 4 rotations; mean) and 100 with navigability (median, max): the T5 fold.
3. Walk out through the west door to Kera. `AssignWorkerCommand(Kera, bench)` must be accepted; a refusal prints the plan outcome and fails. Save at once, while Kera is `to_work`.
4. Resume as W1 with `Arena.Resume` (`tests/Application.Tests/CombatTests.cs:71`), under a setup whose spawns are the 60-creature crowd of `CreatureTests.cs:532` with its x origin moved from 90 to **120 m** (x 120-192, z 80-120, clear of the area [87, 114]²). The resumed record's player pose is set to (126.0, 100.0), facing 270°.
5. Run 20 warm-up ticks, with `OrderCompanionCommand(Tavar, Follow)` at the first. Then time 400 ticks.
   - Asserted at the window's start: Kera's errand is `to_work`, and Tavar has published at least one `RoutePlanned`.
   - **CI:** mean < 6 ms.
   - **Logged:** mean, p95, max and the `GC.CollectionCount(2)` delta.
6. Edit tick (the T4 fold). The player walks to (116.0, 97.5) and dismantles the wall at (114000, 97500) r1 at boundary N; tick N+1 is timed.
   - Asserted: `NavigationRebuilt` at N. At N+1, `RoutePlanned(reason: geometry)` comes from each mover whose route is `Active` and whose watch window meets the changed rectangle. That set is computed and printed before the edit.
   - The wall is then placed again, and the area is back at 256 pieces.
7. Save (the T7 fold): 100 × (`SaveDocuments.Capture` + encode, no disk), logged at p99, with the `entities.msgpack` and `player.msgpack` sizes.

**T10 `BuildingCommands_NeverThrow`** (the chest-crash class, R-B3):
- **Start:** `CrossingWorkshop.Start()`.
- **Script:** 5 seeds × 500 commands from a test-local LCG. The mix is placements (a random definition, a lattice pose inside the area or one module outside it, a random rotation), dismantles and repairs of a random existing or unknown piece ID, `MoveItemCommand` and `TakeAllCommand` on a random piece chest, `InteractCommand` on a random piece door, and `AttackCommand` facing a random piece, so blows run to destruction. `MoveCommand` legs are interleaved, and from E9 `AssignWorkerCommand` and `ReleaseWorkerCommand` for Kera join.
- Every 100th command is a save through `GameSession` and a load into a fresh session, which continues.
- **Asserts:**
  - no exception escapes `Frame`, `Step` or `DrainCommands`;
  - every refusal is a `CommandRejected` reason;
  - at every save, `StateDump.Compare` before the save against after the load is 0;
  - a replay of the command log, with the same save/load points, ends with an equal replayable `StateDump`, equal `(Tick, Rejected)` pairs and equal `NavCounters`;
  - the raw `StateDigest` is compared only for windows that minted no `itm_` (G8).
- **Lands:** E8; extended in E9.

#### 14.12.4 The RK-06 200-piece round trip

`TwoHundredPieces_RoundTripAndStayNavigable` is section 4's (§4.22), and it is required instrumentation.
- It places 200 pieces through commands.
- It loads under a content copy whose `content_hash` differs, so the definition-ID pass and the `with` copies run.
- It asserts equal rows and `StructureSequence` 200, `entities.msgpack` growth ≤ 200 × 250 B, an equal grid digest, and an equal doorway-to-interior person plan after the load.

The actor half of RK-06's validation is covered by `CrossingWorkshop_5to6_KeraWalksInThroughAndAround` (an NPC inside a player structure) and `CrossingWorkshop_10_SaveQuitReloadGoesOnTheSame` (the companion through the doorway after a reload).

#### 14.12.5 Lint BLD009

`BuildingConstants.PiecesPerCellCeiling = 256` and `PiecesPerRegionCeiling = 256` are constants, not config keys. The lint sums `max_pieces` over the build areas that meet each cell and each region (§4.20). Its crafted bad file is in `EachBuildingLint_RefusesItsCraftedBadFile` (section 4).

#### 14.12.6 Guards on the instrumentation

- **`AuthorityNeverReadsAClock`** (Architecture.Tests, E1), a source scan of `src/Domain/**`, `src/World/**` and `src/Application/**`.
  - It fails on `Stopwatch`, `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` and `Environment.TickCount`.
  - Exactly one occurrence is allowed: `DateTimeOffset.UtcNow` inside `EntityId.NewId` (`src/Domain/EntityId.cs:52`).
  - `src/Persistence` (the manifest clock, `SaveStore.cs:82`) is outside the authority and is not scanned.
  - The scan is green at `e10d2c4`.
- **`CountersAreNeverRead`** (G15, section 6).
- The existing ban on `.Step()` and `.DrainCommands()` in presentation stays.

### 14.13 Optional instrumentation and switch-on conditions

None of these is built in M7 unless its condition is met. When one is switched on, the slice that builds it records the trigger in `M7_STATUS`.

| Item | What it is | Switch-on condition |
|---|---|---|
| `--perf-world <save-dir>` and T11 `PerfWorld_IsBuiltThroughCommands` | T11 writes a save of `FullAreaLayout` when `UNNAMED_WRITE_PERF_WORLD` names a directory. The mode boots that save and **replaces** `PerfRun`'s segment list, because `Valley` crosses (100, 100) (`src/Presentation/Perf/PerfRun.cs:24-27`). Segments: `warmup` 5 s; `structures_obstruction` 75 s at `CameraRig.MaxDistance`, through the rooms by their doorways; `structures_third_person` 75 s at 3.5 m; `structures_first_person` 75 s at 0 m; `structures_build` 60 s, in which the aim sweeps the area, an interior wall is taken down and placed again every 2 s, Kera is released at 5 s and assigned again at 35 s, a door toggles every 5 s, and one quicksave runs at 30 s. `--perf` is unchanged | The owner schedules the RAZER window before E10 closes and wants the structures capture; or T2 misses its ASTRAL target and the render side must be separated from the tick |
| The other `FrameStats` columns: `draw_calls, objects, primitives, nodes, pipeline_compiles, gc0, gc1, gc2, gc_pause_ms, alloc_kb, plans, expansions, nodes_restamped, edit_checks, flood_nodes, pieces, static_blockers, closed_leaves, preview_ms, preview_nav` | Godot `Performance.Monitor` values, .NET GC APIs, and `NavCounters` deltas | With `--perf-world`; or a RAZER pass line fails and `sim_ms`/`ticks`/`save_ms` plus the nine existing columns cannot attribute it |
| T1 `M7CostTable_MatchesTheBuild` → `docs/M7_COST_TABLE.md` | Machine-independent work counts: nodes and tiles per piece kind, flood nodes per workshop placement, and Kera's route expansions and corners. Regenerated with `UNNAMED_WRITE_COST=1` (the `TtkTableTests` pattern) | A `NavCounters` count changes between slices without an explanation (for example, Kera's route expansions move by more than 10 %); or M9 tuning needs reviewed count diffs |
| `BuildingCounters` | Placements accepted and refused by rule, dismantles, destroys, repairs, door operations, rebuilds; the current static blockers, closed leaves and socket entries | A T2 or RAZER measurement points at `Rebuild()`, the socket index or the leaf cache |
| `FactionCounters`; T8 `FactionActs_AtTheLogCap_StayCheap` | Acts recorded and ignored, learns, upgrades, reports, evictions; 256 relevant acts timed | A propagation channel lands (M9's witnessed channel), or content raises relevant acts past 16 |
| T3, T4, T5 and T7 as separate tests | Today folded into T2's logs (steps 1, 6, 2 and 7) | A folded log misses its ASTRAL target, so the regression needs its own CI assert |
| T9 `TheCompanion_Soak` | RK-05's 30-minute scripted soak (36,000 ticks through the lodge, the smithy gap, workshop doorways and past the beam, the character out of view half the time). It logs catch-ups by reason, snags, `RoutePlanned` by outcome and the longest hold | The owner schedules RK-05's validation, which is owed from M6 and is not M7 scope; or any companion catch-up appears in C16, N-A11, N-A12 or the playthrough (STOP S5) |

Rules for any optional instrument once it is built:
- counters live in World as immutable snapshots, never saved, digested or read by a decision, and `CountersAreNeverRead` is extended to them;
- previews never count;
- timing stays in tests and Presentation.

### 14.14 The owed RAZER window: what M7 adds, and the pass lines

**Conditions.** The owner provides a clean window: 1080p, with OBS, H3 and other GPU loads stopped by the owner. The agent terminates nothing.

**Order** (about 20-25 minutes):
1. Build Presentation at the M7 commit (`dotnet build src/Presentation/UNNAMED.Presentation.csproj`). If time allows, capture `e10d2c4` first, so M7's zero-piece overhead can be attributed.
2. `godot --path src/Presentation -- --perf --perf-out <dir>/prototype`: the unchanged route (the Phase-1 gate), now with `sim_ms`, `ticks` and `save_ms`.
3. `godot --path src/Presentation -- --spike`, unchanged.
4. `dotnet test src/UNNAMED.sln --filter "FullyQualifiedName~M7BudgetTests|FullyQualifiedName~SixtyCreatures_TickWithinTheBudget|FullyQualifiedName~Navigation_StaysWithinBudget" --logger "console;verbosity=detailed"`, with the output kept. The RAZER/ASTRAL ratio of the T2 and `SixtyCreatures` means replaces §14.1's ×2 assumption.
5. Only if `--perf-world` was built (§14.13): `godot --path src/Presentation -- --perf --perf-world <save-dir> --perf-out <dir>/structures`.

**Record:**
- the machine block, including the CPU;
- per segment: average fps, 1 % low, frame p99, hitches over 33 ms, `sim_ms` p99 on tick frames, the most ticks in one frame, the largest `save_ms`;
- the working set and VRAM;
- the budget tests' logged numbers and the CPU ratio.

**M7 pass lines:**
1. Every `--perf` segment's 1 % low ≥ 60 fps (the existing gate).
2. Frame p99 ≤ 16.7 ms in every segment.
3. `sim_ms` p99 over tick frames ≤ 4 ms.
4. No hitch over 33 ms after the warm-up, except a save frame. A save frame is recorded as T-25 evidence (pre-existing), not as an M7 failure.
5. Headless on RAZER: every CI assert green; T2 mean ≤ 4 ms; Kera's walk-home plan (N-A10) ≤ 12 ms, which is twice the ASTRAL STOP line.
6. If the structures capture ran: lines 1-4 in its segments, and every command frame < 33 ms.

**Where results go.** The numbers and pass lines go in `docs/M7_STATUS.md`. Raw captures stay outside the public repository, as M6's videos did (`docs/M6_STATUS.md:76`). A missed line switches on only the lever §14.15 names for it, and is reported to the owner.

If no window is given by E10 closeout, it is recorded as owed. That is the owner's gate, not a merge blocker, as in M6.

### 14.15 Do not optimise until measured

The levers are in order. Each is built only when its trigger is measured, and the slice that builds it records the measurement.

| # | Lever | Trigger |
|---|---|---|
| 1 | Render side: a `MultiMesh` per piece definition and rotation parity (pads excepted, because they drape); one merged `ArrayMesh` and trimesh collider per connected structure (§9.2 N5); a preview debounce that asks for navigability only after the snapped pose has held 2 frames, showing amber `NotChecked` until then (presentation-only, so parity is untouched) | A RAZER structures capture misses pass line 2 with `render_cpu_ms` dominant; or build-mode frames exceed 4 ms p99 in preview work |
| 2 | Simultaneous replans: a per-tick plan budget, spent in fixed mover order and reset every tick, with no cursor and nothing saved; a deferred mover keeps its stale persisted stamp (§3.19) | The RAZER window measures a hitch over 33 ms whose tick holds a plan; or Kera's walk-home plan exceeds 12 ms on RAZER |
| 3 | GC: sub-tile navigation blocks below 85,000 B (the grid is never saved, so there is no format change) | T2 logs gen-2 collections during the build or the timed window, or a capture attributes a hitch to GC |
| 4 | Collision: an order-preserving broadphase that filters by the per-tile input lists, then iterates in the original list order | T2 misses 2 ms on ASTRAL, or content needs more than 256 pieces in a region |
| 5 | Content: a lower `max_pieces` (data; the RK-06 ceiling) | Any of the above, when the code levers are not yet justified |
| 6 | WORLD_ARCHITECTURE's order: radii, then caps, then AI rate, then LOD (`docs/WORLD_ARCHITECTURE.md:460`) | A tier-A-cap measurement misses budget |

**Never done in M7, measured or not:**
- a heuristic weight above 1 (it gives up optimality and the predictable route);
- `max_expansions` below 1.5 × the worst real route;
- a search sliced across ticks with unsaved partial state;
- route sharing or caching between movers;
- save I/O moved off the main thread (T-25 is pre-existing);
- Godot navigation in any authoritative role.

### Not in M7 (section 14)

The optional instruments of §14.13 and the levers of §14.15. Section 16 lists each once, with its owner.

### Tests owned by this section

| Test | Project | What it proves |
|---|---|---|
| T2 `TheCapWorkshop_SixtyCreaturesAndTwoMovers_TickWithinTheBudget` | Application.Tests | A tick with a full build area, 60 creatures clear of it and both movers planning stays within budget (CI mean < 6 ms). It logs the folded T3, T4, T5 and T7 measurements, and asserts the full-area layout's counts (256 pieces, 94 rebuilds, no audit) and the edit tick's `geometry` replans |
| T10 `BuildingCommands_NeverThrow` | Application.Tests | No building, chest, door, damage or assignment command throws, across saves and loads; the replay equals the run (replayable dump, rejections, `NavCounters`) |
| `AuthorityNeverReadsAClock` | Architecture.Tests | Domain, World and Application read no clock except `EntityId.NewId` |
| Optional, only when switched on (§14.13): T1 `M7CostTable_MatchesTheBuild`, T3 `APlacementBurst_StaysWithinTheCommandBudget`, T4 `AnEditThatReplansBothMovers_StaysWithinTheTickBudget`, T5 `PreviewPlacement_StaysWithinTheFrameBudget`, T7 `SaveAndLoad_WithTheCapWorkshop_StayInsideTheirBudgets`, T8 `FactionActs_AtTheLogCap_StayCheap`, T9 `TheCompanion_Soak`, T11 `PerfWorld_IsBuiltThroughCommands` | Application.Tests | As §14.13 |

Required instrumentation owned elsewhere: N-A10 `Navigation_StaysWithinBudget` (section 3); `TwoHundredPieces_RoundTripAndStayNavigable` and BLD009's case in `EachBuildingLint_RefusesItsCraftedBadFile` (section 4); `CountersAreNeverRead` (G15, section 6); `SixtyCreatures_TickWithinTheBudget`, unmodified (section 2).

---

## 15. Risk register

### Decisions

- **L (likelihood)** is the chance a risk reaches a merged M7 build or the owner's playtest, with the design's mitigations in place but not yet proven. **I (impact)** is the consequence if it does.
- Each row names the test whose failure exposes the risk, or says "process".
- Risks that the lead rulings removed are listed once in §15.3 and not carried.
- `RISK_REGISTER.md` edits land in two commits: text that follows from the rulings in E0, and text that rests on evidence in E10.
- Rows with no project-register equivalent stay local in `M7_STATUS` (§15.5).
- Four rows need the owner's awareness (§15.6).

### 15.1 How to read the register

L and I are low, medium or high; "high" appears only where the row says why. **RK** is the `docs/RISK_REGISTER.md` entry the row maps to: "update" means §15.4 proposes new text, and "local" means `M7_STATUS` only.

### 15.2 The register

**A. Navigation**

| ID | Risk | L | I | Mitigation | Test / proof | RK |
|---|---|---|---|---|---|---|
| R-A1 | **Deterministic navigation is a large new integer module** (lattice, probes, A*, pulling, follower, edit check). A soundness or tie-break bug makes movers stall or clip corners | medium | medium: movers stall, but the stuck replan, the companion's catch-up and the errand's "Blocked" view make it visible | The C1 margin (358.8 mm diagonal clearance ≥ 350 mm); the exact `Int128` swept circle; one `StampRect` for build and rebuild; `NavRoute` built only through validating factories; the scratch generation wrap clears; pure Domain tests with no content | N-D1..N-D22, especially N-D11, N-D15, N-D16, N-D21, N-D22; N-X1 | RK-05, RK-06 (update) |
| R-A2 | **Cell seams**: a route found on one side fails on the other, exits and re-enters the structure, or differs after a reload | low: seams are not in the data (global node indices; 100,000 / 250 = 400) | high: the ROADMAP exit and RK-14's named case | Each tile is a pure function of its key and the global inputs; tile keys are pinned against `CellKey.OfWorld`, including the negative region; the 600 mm influence radius is the overlap | N-D9, N-D10, N-A2 (`CrossingWorkshop_5to6_KeraWalksInThroughAndAround`), N-A7, N-W1; section 13's seam recording | RK-14 (update); WA RK-A2 |
| R-A3 | **Navigation cost at the 65,536 cap**: the walk-home plan (4.6 ms [bench]) exceeds the 4 ms tick by one plan; a cap plan costs 6-9 ms; rebuilds are cheap | medium: a release or an edit makes a planning tick | low: one frame between 16.7 and 33 ms on RAZER at worst, never a hitch in shipped content | Plans only on triggers, at most one per mover per tick; §3.18's STOP (walk home > 6 ms on ASTRAL); the content rule `max_expansions` ≥ 1.5 × the worst real route; §14.15 lever 2 on a RAZER trigger | N-A10; T2 (edit-tick log); the RAZER pass line 5 | RK-02, RK-06 (update) |
| R-A4 | **Pathfinding nondeterminism**: heap ties, hash-order iteration, IDs in stamps, counters or scratch read by a decision, culture-sensitive comparers, float in navigation | low | high: replay and save-then-continue diverge, and digest proofs fail, or pass hollowly | A total order `(f, h, idx)` on its own heap (no `PriorityQueue`); geometric tie-breaks; stamps hash geometry, never IDs; scratch contents never matter; ordinal comparers; `NavTileKey : IComparable`; integer only | N-D12, N-D17, N-D22, N-A6, N-A8; G15 `CountersAreNeverRead`; G16 N-X1; G17 N-W3; G24 `M7Code_NamesItsComparers`; G28 | RK-09 (update) |
| R-A5 | **Companion regression (C16 and M6's guarantees)** from route mode, route resets, the `PersonObstacles` move, door opening, the `SightWalls()` switch or the recruit refusal | medium: the companion is the most-edited Phase-1 system | high: RK-05 is a pillar, and these are M6's exit proofs | Route mode only when neither the character nor a trail mark is in clear view; the snag and catch-up kept as the safety net; a refused `OpenDoor` counts as stuck | Unmodified: `ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds` (`tests/Application.Tests/CompanionTests.cs:119`), `FollowWaitFollow_…` (`:87`), `LeftFarBehind_…` (`:179`); `HisState_…` (`:285`, + route); N-A11, N-A12; G26; the playthrough's `follow` beat (STOP S5). The 30-minute soak does not exist (owed from M6; optional T9) | RK-05 (update) |
| R-A6 | **An errand NPC stalls for good**: an unreachable anchor, a foreign-owned piece door, a body in the doorway. She never teleports | low | low: visible, and fixed by a release | Assign refusal 10 through `Reachable`; a retry every 40 ticks; "Blocked" at 200 ticks; a refused door counts as stuck (G26); BLD008 keeps the area from cutting anything outside it | N-A5; `ARefusedDoor_CountsAsStuck_ForBothMovers`; `Assign_RefusesInOrder` | local |
| R-A7 | **Creature steering against placed walls.** Creatures never path, so a chaser slides along a wall and loses the player behind the workshop. A boar charging a player wall is stunned (`src/World/Runtime/Creatures.cs:588-594`): consistent, but a farmable stun. A creature can walk through an open door and be shut in | medium | low: odd behaviour, no corruption. Respawn farming is GAMEPLAY_LOOPS E-4's concern, unchanged by M7 | Spawner protection (BLD007); creature homes never see pieces (G9); creature pathing declined on purpose | `BehaviourMatrixTests`, `CreatureTests` unmodified; `ACreatureChasingRoundTheWorkshop_NeverOverlapsAPiece` (section 4); G9 | local (accepted) |
| R-A8 | **A later, wider build area makes the local edit check inexact.** V-N1 is exact only because BLD008 caps each area below `seal_limit_nodes` (750.8 m² ≤ 1,024 m²) | low under Q4's default; medium if Q4 is "anywhere legal" | medium: a space larger than 16,384 nodes can be walled off unseen, and NPCs stall | BLD008 fails the content, which forces a larger seal limit (cost linear in it) or a portal layer (M9) | BLD008 (a) and (b) in `EachBuildingLint_RefusesItsCraftedBadFile` | RK-06 |
| R-A9 | **Model figures are not measurements.** Every Crossing Workshop route number (22,685 / 40,751 / 40,639 expansions, 80.45 m, the 1,297-tick bound, the host-cell sequence, the west-side route), the N-A3/N-A4 geometry, and the new P-script legs come from scratch models | medium | low: a script fix or a recorded number, not a design change | Bounds are computed from the first `RoutePlanned` route; in-test preconditions fail loudly with the route printed; the first green run records ticks in `commands.tsv`; N-A10 re-measures; the playthrough stops on the first failure (L8) | N-A2, N-A3, N-A4, N-A10; `ReputationTableTests` | local |

**B. Building and persistence**

| ID | Risk | L | I | Mitigation | Test / proof | RK |
|---|---|---|---|---|---|---|
| R-B1 | **World-delta integration drops or duplicates pieces, `structure_seq` or errands** through a hand-built `DeltaSnapshot` site, the baseline proof, the rebase or the `FromSnapshot` order; a hand-written digest that misses a field hides it | low with G11 and G12; medium without them | high: silent loss of player work, RK-11's worst class | `with` copies at `SaveLoader.cs:358` and `BaselineTransitions.cs:112`; `DecodeEntitySection` returns a `DeltaSnapshot`; host-cell proof for every row; reflection guards | G11, G12; `T01_M7State_RoundTripsEveryField_ByteStable`; `ABuiltStaffedAndKnownWorld_CompareEqual_FieldByField`; `Building_RoundTripsThroughSave_AndNavigationDerivesIdentically`; `CrossingWorkshop_10_…` | RK-11 (update), RK-06 |
| R-B2 | **The 13 → 14 migration and the definition pass**: three parts in one step; frozen `V13` shapes; four repoints; 13 regenerated `expected.json` files; a pass that builds an invalid record (a standing merge to 0, a removed `via`) | low | high: old saves become unloadable, are silently defaulted, or crash | One step in E2 before any system writes; required-on-decode with "corrupt, not defaulted"; one v14 fixture; the `expected.json` diff exactly as §7.12 (STOP S4); a merge to 0 drops the row with a Warning; an `ArgumentException` from the pass becomes a Blocker; a real M6 save committed at the end of E0 | `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState` (1..14); `Schema13To14_GivesNothingBuiltNoLedgerAndNoRoutes`; the corrupt-not-defaulted set; `TwoFactionsMergedWithOppositeStanding_LoadNeutral_WithAWarning`; `ADefinitionPassThatBreaksARecord_IsABlocker_NotACrash`; `AnM6Save_LoadsIntoM7_WithNothingBuiltNeutralStandingNoErrandAndNoRoute` | RK-03, RK-16; PERSISTENCE RK-P07 |
| R-B3 | **The chest-crash class**: a derived ID is retired, then derived again, so `CreateEntity` throws "already exists" inside `DrainCommands` or `Step`, because `DestroyEntity` only tombstones (`src/EntityRegistry/EntityRegistry.cs:133-142`) | medium: every derived-ID lifecycle can reopen it | high: the game crashes mid-tick, and progress since the last save is lost | The C1 clause (`Items.cs:558` gains `&& site.InstanceId is null`); a strictly increasing `StructureSequence` (no ordinal reused); `RemoveCore` the only retirer; `ReleaseContainer` retires only the `cnt_`; `SpillContainer` keeps item IDs | `APieceChest_EmptiedAndRefilled_KeepsOneIdentity` and its save-split twin; T10 `BuildingCommands_NeverThrow`; `CrossingWorkshop_4and8_CraftBlowsMendAndSpill` | RK-06 |
| R-B4 | **ID minting and stack order against replay.** Item IDs are wall-clock values, unordered within a millisecond; a take or merge ordered by `ItemId` picks a different stack in the replay, so even the replayable dump's counts differ | low after L5 | medium: replay proofs flake, and raw-digest proofs fail | Pieces and chests use `EntityId.Derived`; acts use `Seq`; navigation stores no IDs; M7 systems mint nothing themselves; check 14 takes by (quality asc, count asc, `ItemId`); `Put` merges by (count desc, `ItemId`); raw-digest tests first assert that no `itm_` was minted. Residue: `ConsumeItem` and crafting still spend in `ItemId` order (Phase 1) | G8 `M7SystemsMintNoWallClockIds`; G20 `StackCounts_DoNotDependOnItemIdOrder`; `Put_MergesIntoTheFullestStackFirst_WhateverTheItemIds`; N-A8; `CrossingWorkshop_0and11_ReplaysFromTheLog`; T10 | RK-09 (update) |
| R-B5 | **Read-path and order hazards**: creature homes recomputed on load from a space that includes pieces (G9); `Space` order following placement history (G10); an errand pose disagreeing with its body (G18); an unsaved input read by an M7 decision (the conversation G21, tiers G22, `PlayerCombat`); one NPC with a companion record and an errand (G23); record equality over arrays (G28) | low | high: a save-then-continue divergence | `Populate` keeps `Setup.Layout.Space`; `StructureOrder`; both errand records written in one tick; no errand read of the conversation or tiers; the building commands' only actor refusal is `"dead"`; `Recruit` refuses an errand NPC; `SequenceEqual` equality | G9, G10, G18, G21 (both tests), G22, G23 (both tests), G28 | RK-16 (update) |
| R-B6 | **An authored layout edit lands under saved pieces, or a retune changes a piece.** The layout is in no `baseline_hash` | low: BLD007 makes authored geometry inside an area a lint error | medium: blockers overlap and push bodies; no data is lost | BLD007; the load audit keeps and reports every such piece in `StructureAudit` and clamps health above a retuned `health_max` | `ALayoutEditUnderASavedPiece_IsAuditedAndKept` (this section) | RK-16; PERSISTENCE RK-P12 |
| R-B7 | **Pressure for more than one storey** (lofts, stairs, walkable roofs) from the playtest or M9 | medium: the withheld kit already has floor planks | medium: vertical `Kinematics`, 3-D navigation, a save change and a prediction change, a milestone of its own | Ruling 2 written into DECISIONS at E0. Invariants: `Kinematics` and `MovementRules` gain no members; piece parts have `ClearanceMm = 0`; roofs are never blockers. ROADMAP's answer is "more pieces, not physics" | `MovementRules_GainsNoMembers` (this section); BLD003 (no overhang parts); N-X3 (the height filter) | accepted-risks table (update) |

**C. Factions**

| ID | Risk | L | I | Mitigation | Test / proof | RK |
|---|---|---|---|---|---|---|
| R-C1 | **Standing drifts into a global morality meter, or feeds hostility, movement or building** | low: designed out | high: Charter §20, ruling 3 and ROADMAP M7's "no universal morality meter" | Per-faction points only, with no aggregate type; a ladder with no hostility word; no reaction to trade, quests or building; the tier derived, never stored; R1-R5; the reflection check on tactical code | G6 `TacticalCode_NeverReadsFactionState`; G2 `PresentationSource_NeverDerivesStanding`; G13 `BuildingNeverRecordsAnAct`; `TheLadder_HasNoHostilityTier`; `NoContent_TradesCurrencyForStanding`; `TheSameKnownAct_MovesTwoFactionsInOppositeDirections`; `AxisIndependence_ReputationByLevel_2x2`, `…BySkill_2x2` | local; promote when a milestone adds a propagation channel |
| R-C2 | **Psychic knowledge propagation.** In report-only M7, a faction learns only when the player tells a member in person, and only the speaker's own reacting faction learns. Residue: pooling at the seat is immediate (K6), including Kera told at the bench (D30), which shipped content cannot observe | low | medium | K1, K5-K10; R4; FAC-M1 (members inside the seat); FAC-M2 (no companion member); `FactionSystem` reads no body, facing or wall | `AFactionThatNeitherSawNorWasTold_DoesNotUpdate`; `FactionSystem_ReadsNoSightNoBodiesAndNoFacing`; `AReport_GoesOnlyToTheSpeakersReactingFaction`; F12; `ACompanionCannotBeAMember`; `AMemberStandsInsideTheSeat` | local |
| R-C3 | **Faction residues visible in play**: a companion's kill records no act; migrated saves start with an empty log; a pre-M7 Kera wares record never shows the billets; every faction learns by report; relevance is fixed at boot; an evicted act cannot be reported | high: players will notice | low: each is stated, and none corrupts | Recorded in `M7_STATUS` (§5.19); owner Q5 confirms or overturns the defaults | `ACompanionsKill_IsNotThePlayersAct`; `AnM6Save_…` (E3 rows) | local |

**X. Cross-cutting**

| ID | Risk | L | I | Mitigation | Test / proof | RK |
|---|---|---|---|---|---|---|
| R-X1 | **Scope creep.** ROADMAP M7 still names crime, bounty, territory, free rotation and NPC work; three large parts; optional instruments | high | high: RK-10's "architecture instead of a game", and M8 and M9 slip | The lead rulings' trims (L1-L4, L9); optional instruments off by default (§14.13); the `NotBuilt` kinds stay `NotBuilt`; no M8 work; STOP S6 | A scope ledger in `M7_STATUS` mapping every built type to a ROADMAP phrase or a scope row; the existing `NotBuilt` tests unchanged | RK-10 |
| R-X2 | **An owner answer reverses a default after implementation**: 45° rotation, assignment moved to M10, building anywhere, a crime stub | medium | medium: rework | Latest decision points: Q2 before E0 commit 2; Q3 before E2; Q5 before E3; Q1 and Q4 before E5. Schema 14 freezes after E2 (STOP S2). Defaults are data-shaped where they can be | process | RK-10 |
| R-X3 | **Rulings 1 and 2 stay unwritten**, so a later agent follows WA §11's "Recast-style, baked per cell" (`docs/WORLD_ARCHITECTURE.md:435`) or RK-14's "it needs the engine" (`docs/RISK_REGISTER.md:290`) | medium: the documents contradict the rulings today | medium | E0 writes them, and its STOP is a grep-able checklist signed by the owner in `M7_STATUS` (L8) | The E0 checklist; G4 `PresentationUsesNoGodotNavigation_OutsideTheSpike`; `OnlyPresentation_MayReferenceGodot` | RK-14 (update) |
| R-X4 | **Presentation takes authority**: the ghost decides validity, a physics ray picks the pose, presentation derives a tier, prediction reads a different blocker set, or presentation writes the grid's bytes | medium: the new surface is large | high: RK-09 | Validity only from `BuildingRules.Validate`; the ghost only from `PreviewPlacement` (the one allow-listed method); the aim is the camera ray against the domain `TerrainGrid`; prediction reads `Simulation.Space`; reasons are words at source | G2; G4 (both scans); G7 `PlacementRules_AreReadOnly`; G25; `PreviewsInterleaved_ChangeNothing`; `Prediction_EqualsAuthority_AcrossANewWall`; `PresentationSource_NeverReachesPastThePublicReadAndCommandSurface`; `Commands_CarryNoCameraState_…`; `TheSimulation_ExposesOnlyReadsAndTheCommandPath` | RK-09, RK-15 |
| R-X5 | **A read-only query writes something authority reads** (preview scratch, counters, registry) | low | high | `_previewScratch` and a null counter sink; the source scan of G7 | `PreviewsInterleaved_ChangeNothing` (equal dumps, IDs, rejections and `NavCounters`) | RK-15 |
| R-X6 | **Asset dependencies.** The building kit is withheld (`src/Presentation/Art/art_bindings.json:25-31`); the chest and anvil models are withheld; the kit lives in DeepSeek-owned paths | high: the art will not be ready | low for M7, which is greybox by scope. Later, 1.3-6.4 M triangles at the cap with full-detail modules | Greybox primitives; no `pieces` section in `art_bindings.json`; no DeepSeek path touched (STOP S8) | `--smoke`; the `--build-shots` stills; a re-measure when the kit is released | RK-02 |
| R-X7 | **Float and transcendental leaks.** `Sin`, `Cos` and `Atan2` sit on authoritative paths (perception, combat traces, facing). M7 reuses the trace bisection in `FirstStop` and `CombatRules.FacingTowards` for errand facing. Faction witnessing through `Perception.Sees` is no longer an M7 path (L1) | low: the contract is same-machine replay, and each CI test runs on one machine | low now; high only when a networked authority or cross-machine replay must agree | N-X1 keeps navigation integer-only; M7 adds no new transcendental; evidence replays run on one OS; recorded in RK-09 | N-X1; the replay tests | RK-09 (update) |
| R-X8 | **Content-lint gaps.** The loader enforces less than DATA_MODEL claims; builder lints stop at the first failure; a load-phase error hides the semantic lints; `DIR003` does not fail the lint CLI | medium | medium: bad piece, area or faction data ships | NAV001-NAV007, BLD001-BLD009, WLD015 and FAC001, each with a crafted bad file; the exact list in `LoadAll_Loads_Yaml_Files`; a lint failure fixes the fixture pack (0.2.10), never the lint | N-X3; `EachBuildingLint_RefusesItsCraftedBadFile`; the FAC001 content tests; `LoadAll_Loads_Yaml_Files`; `NavConfigDefault_IsTheShippedFile` | RK-08 |
| R-X9 | **The RAZER window is still owed** (`docs/M3_STATUS.md:4`; `docs/M6_STATUS.md:221`) | high: M7 will be built before it opens | medium: M7's cost lands on an unmeasured baseline, and a later failure cannot be attributed | §14.14's checklist in one window with the Phase-1 gate; ASTRAL half-budget targets until then; not a merge blocker | §14.14's pass lines | RK-02 (update) |
| R-X10 | **The main-thread tick budget with pieces**: more static blockers, sight walls, follows and plans | low | medium | ≈ 1.0 ms a tick for a full area and ≈ 1.8 ms for the densest, at the tier-A cap [est], against 4 ms | T2; `SixtyCreatures_TickWithinTheBudget` unmodified | RK-02, RK-06 |
| R-X11 | **Main-thread spikes**: a placement drains in its frame (`CheckEdit` worst 10 ms); an edit replans both movers in the next tick; a release plans the walk home (4.6 ms [bench]); the ghost checks navigability 3-5 times a second while sweeping | medium | medium: a dropped frame, not a hitch over 33 ms | Per-call budgets (N-A10); levers 1 and 2 of §14.15, switched on only by measurement | N-A10; T2's logs; RAZER pass lines 4-6 | RK-02 |
| R-X12 | **Presentation cost per piece on RAZER**: a full area is ≈ 705 nodes, 285 mesh instances and 203 camera colliders, and several hundred more draw calls in view | medium: RAZER's render thread is unmeasured | medium | Shipped timber holds real play to ≤ 80 pieces; measure with the optional structures capture; MultiMesh or merged meshes as the fallback | The optional structures capture; `--smoke` | RK-02, RK-06 |
| R-X13 | **GC churn**: each footprint edit copies 1-4 tiles' two 160,000-B layers onto the large-object heap; a full-area build makes ≈ 45 MB of it | low | medium: gen-2 pauses | T2 logs gen-2 collections; the lever is sub-tile blocks below 85,000 B (no format change) | T2's log | RK-02 |
| R-X14 | **The save hitch (pre-existing).** Saves run synchronously in `GameSession.Frame` (`:194-199`) with a disk flush per file and directory moves; T-25 (≤ 2 ms P99) has never been measured. M7 adds ≤ ~46 KB (pieces) and ≤ ~100 KB (a full ledger) of encoding, and < 1 KB in shipped play | medium that the budget is already missed before M7 | medium: a hitch every 300 s | Measure: `save_ms` on RAZER and T2's capture-and-encode log. M7 reports the number and does not move I/O off the main thread | RAZER pass line 4 (a save frame is recorded as T-25 evidence); T2's log | RK-06 (size); T-25 |
| R-X15 | **Budget tests flake in CI** on `ubuntu-latest` | medium | low: a red CI and lost time | CI asserts counts exactly and times at 3× the ASTRAL target; the tight numbers are logged evidence | N-A10; T2 | local |
| R-X16 | **Region-wide blocker scans at scale**: `Resolve` walks every static blocker in the region (`Kinematics.cs:183-198`), and pieces join region-wide | low in M7: one area, and 80 timber | medium at M9 | BLD009's ceilings until an order-preserving broadphase exists (§14.15 lever 4) | BLD009's crafted bad file; T2 | RK-06 (update) |
| R-X17 | **Build-mode input and ruling 5**: the direct keys collide with attack, take-all and `release_mouse` unless gated; a later "quick" radial would break ruling 5 | low | low | Section 8's gating; direct keys only; no radial anywhere | `EveryM7Action_IsBoundToADirectKey`; the `--build-shots` beats | local |
| R-X18 | **Tier hysteresis is unsaved but gates movers.** Companions (`Companions.cs:263`) and creatures (`Creatures.cs:264`) act only in tier-A cells, and a cell 140-160 m away is A in a continuing world and B after a load. The companion's conversation hold (`Companions.cs:286-290`) reads the transient conversation in the same way | low in M7: a 200 m region, and the errand mover is neither tier-gated nor reads the conversation | high at M9's 2 km: a save-then-continue divergence | M7 adds no new consumer (G21, G22). Owed before M9: persist `CellTiers`, or gate without hysteresis | G22 `AnErrandFarFromThePlayer_SavedMidWalk_ContinuesEqual` (M7's part only) | RK-16 (update; cross-reference RK-07) |
| R-X19 | **Authorization and process.** M7 is not authorized (`docs/M6_STATUS.md:187`); AGENTS.md scopes Claude "through M6"; no worktree, branch or draft-PR convention is named; the repository is public, so anything pushed is published; DeepSeek-owned paths are off limits (STOP S8); R-1's tag and the tone review are open | high: this is the current state | high: no M7 work may start | The owner process items of section 17, before E0 | process | RK-10 |

### 15.3 Risks retired or deferred by the lead rulings (not carried)

| Former risk | Source | What removed it |
|---|---|---|
| Witness pooling: an errand member seeing acts 62 m from her seat, and members outside the seat | audit_scope HIGH-2 | L1: the witnessed channel is M9. M9 must exclude errand members and members outside the seat radius |
| The witness reading an unsaved facing or conversation, so save-then-continue diverges | audit_determinism H1 | L1. M9 takes facing from saved or content state, with an integer cone |
| The errand NPC holding for an open conversation (D27) | audit_determinism H1 | L5: the hold is deleted, and a talk with a walking errand NPC is refused from its saved phase (G21) |
| The tier-gated errand mover | audit_determinism M1 | L5: not tier-gated. The companion and creature part remains as R-X18 |
| The deadfall baseline transition and the frozen M6 layout fingerprint | audit_scope MEDIUM-4 | L3: an authored timber container, and no transition |
| Shot and formula damage changing `CombatSystem.Loose` and `Magic.cs` | audit_scope | L2: one damage source |
| V-N3 refusing every chest | audit_impl H1 | L6: a zero-radius site is a reach point (N-D20) |
| `NavFootprint.DoorOpen` going stale on toggles | audit_impl L1 | L6: no `DoorOpen`; door state is read at query time |
| A refused `OpenDoor` never counting as stuck | audit_determinism | L5: `StuckTicks += 1` (G26) |
| A scratch generation wrap leaking stale marks | audit_determinism L6 | L5: the wrap clears (N-D22) |
| A standing merge to 0, or a pass exception, crashing a load | audit_determinism M2 | L5: a Warning, and a Blocker (R-B2) |
| The faction guard red on unmodified code (the bare `TierOf` token) | audit_code F1 | L7: a reflection check; `StandingTierOf` |
| `CameraRig.MaxDistance` becoming an instance field and breaking `PerfRun.cs:56` | audit_code F3 | L7: the const stays; `BuildMaxDistance` and `Cap` are added |
| Settled-first eviction complexity | audit_scope | L9: oldest first |
| Instrumentation as horizontal infrastructure | audit_scope HIGH-1; audit_impl M9 | L4: the required/optional split (§14.12, §14.13) |

### 15.4 Proposed `docs/RISK_REGISTER.md` and `docs/WORLD_ARCHITECTURE.md` updates

**E0 (text that follows from the rulings):**
- **RK-14** (`:280-296`):
  - withdraw "and it needs the engine" (`:290`): the validation is headless, plus section 13's runtime recording;
  - replace the mitigation (`:294`) with: "One global integer lattice (owner ruling 1). A tile is a pure function of its key and the global inputs, and an edit restamps every tile its rectangle meets (the 600 mm influence radius is the overlap). Straddling is proven, not constrained. The grid is derived and never saved, so no RK-11 dirty reason is needed";
  - delete "constrain or warn on placement that would straddle a seam".
- **RK-06** (`:144-158`): "navmesh rebuild" becomes "domain grid restamped per footprint change" (§10.6).
- **Accepted risks** (after `:388`): "**One storey (owner ruling 2, 2026-09-24).** No upper floors, stairs, ladders, climbing or walkable roofs; a floor is a ground pad. Reopens only by an owner ruling with a milestone of its own."
- **WORLD_ARCHITECTURE §11** navmesh row (`:435`): "Domain navigation grid (owner ruling 1): integer, headless, one tile per cell on global indices, restamped per footprint change. Godot navigation is never authoritative."

**E10 (text that rests on M7's evidence):**
- **RK-02** (mitigation, `:92`): append "M7: `FrameStats` records `sim_ms`, `ticks` and `save_ms`. The owed RAZER window also runs the M7 headless budget tests for the CPU ratio, and the `--perf-world` structures capture if it was built."
- **RK-05** (validation, `:138`): append "Still owed from Phase 1 (the soak is specified as T9 in the M7 design and built only when scheduled). M7 adds the companion's route mode and door opening (N-A11, N-A12), and C16 passes unmodified."
- **RK-06** (`:144-158`):
  - the E0 text "domain grid restamped per footprint change" gains the measured bound "(≤ 108 nodes per piece)";
  - validation: "`TwoHundredPieces_RoundTripAndStayNavigable` (through a content-hash change, doorway-to-interior plans equal after the load); `CrossingWorkshop_5to6` and `CrossingWorkshop_10` (an NPC and the companion inside a player structure across a reload); T2";
  - "grows sub-linearly" becomes "grows linearly at ≤ 250 B per piece (≈ 180 B)". A row per piece (D-08) cannot be sub-linear, and it need not be;
  - ceiling: "256 intact pieces per build area; BLD009 holds the sum of `max_pieces` per cell and per region at ≤ 256 until an order-preserving `Kinematics` broadphase exists";
  - likelihood: medium → low.
- **RK-09:** add "Replay-stable identities: `EntityId.Derived` for pieces and piece chests. M7 systems mint nothing themselves. Authoritative float paths, same-machine only: `Perception.Sees`, the combat trace and `FirstStop` bisection, and `CombatRules.FacingTowards` (errand facing). Navigation is integer-only (N-X1)."
- **RK-11** (validation, `:234`): the mutation classes gain piece place, dismantle, destroy and repair, the piece door, errand begin and end, and the faction act and report. Cite `ABuiltStaffedAndKnownWorld_CompareEqual_FieldByField`, `CrossingWorkshop_10` and `FactionState_ContinuesAcrossASaveAndLoad`.
- **RK-14:** validation "N-D9, N-D10, N-A2 (`CrossingWorkshop_5to6`), N-A7 and the seam recording"; likelihood medium → low.
- **RK-16:** add "Unsaved inputs that gate movers: tier hysteresis (`Companions.cs:263`, `Creatures.cs:264`) and the companion's conversation hold (`Companions.cs:286-290`). Owed before M9: persist `CellTiers`, or gate without hysteresis. M7's errand mover reads neither (G21, G22)."
- **WORLD_ARCHITECTURE RK-A2** (`:469`): "Solved in M7 by a seam-free domain grid, proven by the Crossing Workshop (N-A2) and by N-D9 and N-A7."

### 15.5 Kept local in `M7_STATUS`

R-A6, R-A7, R-A9, R-C1, R-C2, R-C3, R-X15 and R-X17 go in `M7_STATUS`'s local table, not the project register. Each is guarded by a failing test, or is a stated and accepted behaviour.

### 15.6 Rows that need the owner's awareness

1. **R-X19, authorization.** No M7 work starts until the owner authorizes M7 and names the agent, worktree, branch and draft-PR convention.
2. **R-X1, scope.** It is the only risk that makes the others moot. Holding the defaults on Q1-Q5 keeps M7 the size designed here.
3. **R-X9, the RAZER window.** One window should cover the Phase-1 gate and M7's additions. Without it, "60 fps" stays an assumption for two milestones.
4. **R-A5, the companion.** M6's guarantees must survive unmodified, and RK-05's soak is still owed.

### Tests owned by this section

| Test | Project | What it proves |
|---|---|---|
| `MovementRules_GainsNoMembers` | Architecture.Tests | R-B7, the one-storey invariant: `MovementRules`' public members and `Kinematics`' public static methods equal their pinned `e10d2c4` lists |
| `ALayoutEditUnderASavedPiece_IsAuditedAndKept` | Application.Tests | R-B6. From `ANewGame_…`'s state (a pad at (88500, 112500) r0 and a wall at (87000, 112500) r1), save, then resume with `Arena.Resume` under the game setup with an authored circle blocker (r 0.4 m) added at (87.0, 112.5) m and `piece.wall.timber`'s `health_max` lowered to 150. Asserts: nothing throws; both rows are kept with equal IDs and poses; `StructureAudit` names the wall as over authored ground and reports its health clamp from 200 to 150; construction publishes no M7 event; the grid equals a fresh build over the changed inputs. Lands in E5 |

---

## 16. Explicit deferrals and non-goals

### Decisions

- Each deferred item appears here exactly once, with why M7 does not build it, the milestone or phase that owns it, and the ruling or section that declined it.
- A listed owner milestone owns the decision. It is not a commitment to build.
- Three things are never in M7, whatever is measured or asked: Godot navigation in any authoritative role, networking, and a required radial.
- M7 ships building with no property threat. That satisfies GAMEPLAY_LOOPS' anti-annoyance requirement at zero frequency (§16.9).
- The sections' own "Not in M7" lists point here. Where they differ, this list wins.

### 16.1 Building

| Item | Why not in M7 | Owner | Declined by |
|---|---|---|---|
| Vertical building: upper floors, stairs, ladders, lifts, climbing, walkable roofs, lofts, any walkable elevated surface | It needs a walk-surface rule in `Kinematics.Step`, surface heights in `Blocks`/`IsClear`/`CanStand`, 2.5-D navigation and a new meaning for saved body Y | Beyond Phase 2, and only by a new owner ruling | Owner ruling 2; §4.2, §4.8 |
| Terrain flattening or sculpting | Pads drape over the domain terrain; the relief gate (0.25 m) replaces it | Not scheduled | Ruling 2; §4.8 |
| Voxel or free-form building | Snapping is what guarantees navigability and clean persistence | Never (D-08; accepted risk `RISK_REGISTER.md:388`) | D-08 |
| Structural simulation: support, collapse, load, a recursive mount cascade (beyond "a destroyed doorway destroys its door") | "No structural simulation, no physics collapse" | Not scheduled | D-08; L9; §4.13 |
| 45° or free rotation with oriented footprints | It needs oriented boxes in `Blockers.cs` (the shared movement function), oriented rasterisation and rotated sockets | Owner Q1; a later milestone if chosen | Q1 default; §4.2, §3.19 |
| Building anywhere legal, more or wider areas, plots granted by quests or factions | The local edit check is exact only inside BLD008; an unbounded area needs a portal layer and jurisdiction | Owner Q4; M9 or later | Q4 default; §4.16 |
| More pieces: posts, fences, half and window walls, beds, upgrade tiers, locks and keys | No proof needs them | Later content, when a proof or playtest asks | §4.24; §9 |
| A persisted structure record (`bld`), merge and split, ownership transfer, `buildings.msgpack` | A structure is a derived set of pieces in M7 | M10 (settlements) | §4.4; §7.18 |
| A renewable timber node (`resource.wood.deadfall`, `node.wood.deadfall`), with its baseline transition, frozen layout fingerprint and two tests | The authored 80-timber stack covers M7; the node would add M7's only baseline transition | The milestone that wants renewable timber (M9 or later) | L3 |
| Piece damage from shots and formulas (a `Loose` damage-source parameter, the `Magic.cs:114` hook) | One explicit damage source satisfies "explicit rather than emergent" | M9, if its building needs them | L2; §4.12 |
| Piece damage from creature charges and blows, fire and raids | No threat system exists | M10 (home defense) | L2; §4.24 |
| `piece_placed` / `piece_destroyed` act kinds | A reaction would turn materials into standing (E-7) until a repeat rule exists | M9 | §5.2; G13 |
| The `construct_building` quest objective | Stays `NotBuilt` | M9 or later (quest content) | §4.24 |
| The production building UI: a catalogue with thumbnails; cost, shortfall and refund previews served by an authority read; wall runs, blueprints and multi-placement; a structure panel; per-activity camera memory; building audio | M7 is greybox on direct keys | M9 at the earliest, with the art kit | §8; §9 |
| The art kit, the chest and anvil models, a timber-pile look, a timber icon, and a `pieces` section in `art_bindings.json` | Withheld by the asset pipeline, in DeepSeek-owned paths | When the owner releases the kit | §9.2, §9.4 |
| Pause or slowed time in build mode | Building happens in the living world; the proof needs Kera and Tavar moving | Not planned | §8.12 |

### 16.2 Navigation

| Item | Why not in M7 | Owner | Declined by |
|---|---|---|---|
| Creature pathing (chase, search, return home, leash) and routes in `CreatureRecord` | It reopens `CreatureDto` (frozen since schema 8) and the behaviour matrix | Unscheduled; M9 at the earliest | §3.1; scope rulings |
| `medium` and `large` radius classes in content | No M7 mover uses them; N-D6 proves them synthetically | With creature pathing | L9; §3.3 |
| A per-tick plan budget | No hitch is measured. When built, it holds no state across ticks | M9/M10 schedules, or earlier on §14.15 lever 2's trigger | L4; §3.19 |
| A portal graph, tile residency and eviction, build-on-promotion, routes over 88 m | Ashen Hollow is 4 tiles, and one window covers every M7 route | M9 (2 km regions) | §3.19 |
| Door access sets (locks, keys, ownership, faction access); a second walkability with piece doors solid for non-permitted agents; NPCs closing doors behind them | Openers plan through every door; a refused door counts as stuck | M9/M10 | D7, D34; §3.10 |
| Crowd or local avoidance between movers | M7 has two movers | M9/M10 schedules | §3.8 |
| Roads (cost bytes), bridges (an impassable-terrain layer), jump and crouch agents | No content needs them | Unscheduled | §3.19 |
| A `Kinematics` broadphase from the per-tile input lists | BLD009's ceilings make it unnecessary | When an RK-06 measurement needs it (§14.15 lever 4) | §14.5 |
| Godot navigation in any authoritative role | Owner ruling 1; banned outside `src/Presentation/Spike/**` (G4) | Never | Ruling 1 |

### 16.3 Factions and knowledge

| Item | Why not in M7 | Owner | Declined by |
|---|---|---|---|
| Witnessed knowledge (K2-K4, `WitnessRules`, `BestWitness`, `config.factions.witness`, identity by sight, the occlusion rows such as `APlacedWall_HidesAnActFromAWitness`). When built: facing from saved or content state only, errand members and members outside the seat excluded, an integer cone | Shipped Ashen Hollow cannot exercise it, and it carried both high audit findings | M9 | L1; §5.3.4 |
| Rumour, NPC-to-NPC telling, transfer between factions, reports in flight, delay through `Via` and the seat, per-NPC memory logs | M7 has one channel: a report made in person | The first propagation milestone (M9 or later) | L1; §5.3 |
| Identity rungs beyond two; confidence, claims, lies | A report always identifies | M9 or later | §5.20 |
| War-state changes between factions, faction control, any hostility derivation; relations that move anything | Ruling 3; relations are static words that only views read | M9's reconciliation with VERTICAL_SLICE, and later | §5.9, §5.11 |
| Standing decay, caps, diminishing returns, repeat rules | FAC-R5 holds shipped play to two single-instance acts | M9 tuning | §5.4.4 |
| Joining or leaving a faction, player membership, multiple memberships, saved instance membership | Membership is static definition data | The first system that changes membership (unscheduled) | §5.9 |
| Companion faction membership; companion loyalty and reporting; party attribution of kills | FAC-M2: pooling a companion's knowledge would decide loyalty | M9 (attribution); Expansion (loyalty) | FAC-M2; §5.20 |
| NPC-to-NPC relationships | The relationship scale is player-to-NPC (M4) and untouched | Unscheduled; M9 at the earliest | §6.5.3 |
| A third faction (the stone-turners) | It invents lore and proves nothing the pair cannot | M9 candidate | Q5; §5.6.2 |
| Price modifiers by tier; whole-trade gates | One service gate proves gating | M9 | §5.20 |
| `faction_reputation` / `faction_state` objectives; the `reputation` quest reward | Standing moves only through learned acts (R5) | M9 quest reconciliation | §5.8 |
| A player-facing faction screen, limited to what the character knows | The ROADMAP proof is a fixture table; a true-standing screen would show what the character cannot know | M9 | §8.13 |
| `start_points`, standing by origin | Every standing starts at 0 | Character creation (unscheduled) | §5.4.2 |
| Anathema by explicit act, with atonement content | No atonement content exists | When atonement content exists | §5.4.1 |
| A world-global act log with an `actor` field (acts by NPCs) | Acts are the player's, on the player record | When NPC acts arrive (M9 or later) | §5.9; §7.18 |
| Faction visuals (banners, tints, colour-coded NPCs) | They would make standing a world-visible property (ruling 3) | Never as world-visible standing | §9.5 |
| Kaldrun Reach, Vessmere and the vertical slice's factions | M7 is proven in Ashen Hollow | M9 | §1; scope rulings |

### 16.4 Crime and law

| Item | Why not in M7 | Owner | Declined by |
|---|---|---|---|
| Crime, bounty, pardon, legal status, jurisdiction, guards, evidence, disguise; attackable NPCs and attack legality (the `CanAttemptAttack` seam is untouched); the `item_taken` and `npc_harmed` act kinds | Nothing in the game can be a crime yet: NPCs have no health, and containers have no owner. Ruling 3 keeps legal status separate from reputation | Phase 3 | Q2 default; §5.11 |
| Territory gating; faction access sets on gates; claims or permission checks on placement | It joins Q2 | With crime (Phase 3) | Q2; L9 |

### 16.5 Settlement and NPC work

| Item | Why not in M7 | Owner | Declined by |
|---|---|---|---|
| NPC schedules, production, wages, housing, hirelings, a second worker, worker management beyond one Y assignment, settlement simulation | ROADMAP M10 owns them (`docs/ROADMAP.md:312`); the vertical slice has no hirelings | M10 | Q3 default; §4.15 |
| Home-defense raids, property threats, and the attack-frequency option | ROADMAP M10 owns home defense and its frequency control (`:313`); off-screen raids are tier-illegal | M10 | §1.3 row 9; §16.9 |

### 16.6 Persistence

| Item | Why not in M7 | Owner | Declined by |
|---|---|---|---|
| A persisted open conversation, or save deferral during one | The walking-errand talk refusal makes it unnecessary for M7 | Not scheduled | G21; L5 |
| Persisted `CellTiers`, or hysteresis-free authoritative gates for companions and creatures | M7's errand mover is not tier-gated | Before M9 (R-X18) | L5; §2.16 |
| A schema-aware integrity root that allows new section files | M7 adds no section file | The first milestone that needs one | §7.18 |
| Command-log persistence (PERSISTENCE T-28) | Pre-existing and unrelated | Not scheduled | §2.16 |

### 16.7 UI and controls

| Item | Why not in M7 | Owner | Declined by |
|---|---|---|---|
| A required radial; an optional radial that mirrors the build keys | Owner ruling 5 | Required: never. Optional: unscheduled | Ruling 5; §8.16 |
| Gamepad bindings, rebinding, hold/toggle options, UI scale | The named action set is the seam | The controls and accessibility pass (unscheduled) | §8.16 |
| A compass hint to the build area | The build-area outline suffices | Revisit with wider areas (Q4) | L9 |

### 16.8 Instrumentation and networking

| Item | Why not in M7 | Owner | Declined by |
|---|---|---|---|
| `--perf-world` and T11, `docs/M7_COST_TABLE.md` (T1), `BuildingCounters`, `FactionCounters`, the other `FrameStats` columns, and T3, T4, T5, T7 and T8 as separate tests | Only measured need justifies them | Optional in M7, on §14.13's conditions | L4 |
| The 30-minute companion soak (T9) | It is owed RK-05 work from M6, not M7 scope | When the owner schedules RK-05's validation | L4 |
| Networking of any kind | Ruling 6, R-6 and D-12 keep only the four extension points; the command queue stays the one mutation path | No scheduled milestone | Ruling 6 |

### 16.9 The GAMEPLAY_LOOPS conflict on property threats

- **The conflict.** GAMEPLAY_LOOPS puts "property threats with the charter-mandated frequency reduction/skip option" in the phase that ships basic building (`docs/GAMEPLAY_LOOPS.md:282`), and requires the option "in the same phase `BUILD` ships" (`:134`). ROADMAP gives home defense to M10, whose exit is "defense frequency is player-controllable and defaults to rare" (`docs/ROADMAP.md:313`).
- **The resolution.** M7 ships building with no property threat of any kind: no raid, no creature damage to pieces, and no off-screen event. The anti-annoyance requirement therefore holds at zero frequency.
- **The rule.** Property threats and their frequency option land together in M10. Neither ships without the other.
- **The record.** E0's ROADMAP reconciliation records this beside ruling 2. It is not an owner question, because the ROADMAP already orders it.

### 16.10 Non-goals

M7 does not:
- depend on C10 (retired by ruling 4);
- touch `src/World/Legacy`, fixtures v1-v13, the writer packs `content-0.1.0` to `0.1.6`, or the generator's fixed nodes;
- start any M8 work.

Section 16 owns no test. Its scope is proven by section 11's "scope held" criterion and the scope ledger in `M7_STATUS`.
