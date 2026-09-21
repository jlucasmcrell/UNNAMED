# WORLD_ARCHITECTURE.md — Spatial Structure and Tiered Simulation (Phase 0)

**Project:** UNNAMED (working title) — first-person, solo-first, open-world fantasy RPG
**Status:** Phase 0 design. No implementation exists. This document is the contract for whoever implements world streaming, cell state, and tiered simulation first.
**Normative decisions:** `D-06` (four simulation tiers + a global tick), `D-05` (sparse deltas over a deterministic baseline), `D-04` (identity namespaces), `D-12` (region by region; no MMO infrastructure), `D-08` (player building is socket/snap, not physics), `D-11` (presentation never mutates state).
**Normative charter text:** "region by region rather than generating a gigantic world first"; a **2×2 km** area containing one excellent town, wilderness, 3–5 dungeons, secrets, crafting, building, companions, a boss and an epic quest.
**Where this file and `PROJECT_CHARTER.md` disagree, the charter wins and this file is wrong.**

Companion document: `PERSISTENCE.md` owns how any of this is written to disk. This document owns what exists, where, and at what fidelity.

---

## 1. The problem this document solves

The charter asks for a world that feels ancient, large, inhabited and partially unknown, with NPCs that "appear to have lives", while explicitly forbidding full-fidelity simulation of distant actors and forbidding speculative MMO infrastructure (`D-12`).

The resolution is a **presentation of continuity** rather than a simulation of it: the world is divided into addressed cells; each cell and each entity is assigned a simulation tier; only the tiers near the player cost real CPU; distant tiers advance cheap the things that are cheap to advance and store state for the rest.

Everything below is chosen so that a **2×2 km region is fully playable at Phase 2** and additional regions can be added later without changing the model. It is explicitly *not* a design for a 100 km² world in Phase 1. `D-12` governs: one region, completely.

---

## 2. Scale ladder

| Level | Extent | Role |
|---|---|---|
| **World** | unbounded, sparse | Collection of regions. Exists as an index, never as a loaded entity |
| **Region** | **2000 m × 2000 m** (2×2 km) | The unit of authoring, of shipping, and of the charter's discipline. One region contains one excellent town, wilderness, 3–5 dungeons, secrets, crafting/building/companion content, a boss, and an epic quest |
| **Cell** | **100 m × 100 m** | The unit of streaming, LOD, simulation bucketing, persistence sharding and spawn budgeting. 20×20 = 400 exterior cells per region |
| **Sub-cell / locality** | 10–25 m | Proximity group used for interaction prompts, ambient audio, crowd grouping. Not persisted, not addressed |
| **Space** | region or interior | The addressing namespace. Every position is `(space, local position)` |
| **Interior / dungeon** | authored, 50 m – 1 km² | A separate space with its own local cell grid and a portal anchor in a region |

**Why 2 km regions and 100 m cells.** The region size is mandated by the charter. The cell size is a derived budget: at 100 m a cell is small enough that a single cell's terrain mesh, props, and spawn set load inside one frame budget, and large enough that a region is only 400 cells — a number a solo developer and an AI session can reason about, and small enough that per-cell metadata for a whole region fits comfortably in memory. Cells of 50 m quadruple bookkeeping; cells of 250 m make spawn budgeting and interest management too coarse for a first-person game.

---

## 3. Coordinate scheme and addressing

### 3.1 Units and axes

- **1 unit = 1 meter.** All authored and procedural geometry uses meters; no unit conversion layer exists anywhere.
- World is **right-handed, Y-up**, with a Godot-compatible convention (Godot is Y-up, -Z forward; the domain layer stores raw floats and never assumes a handedness — convention lives in presentation).
- Regions tile the **XZ plane**; Y is entirely intra-region height. A region owns its own terrain, including vertical relief. **No region stacking, no underground region layer.** Underground content is an interior space (§8).
- Region indices `(rx, rz)` are signed integers. Cell indices `(cx, cz)` are signed integers in `[0, 19]` **within** a region.

### 3.2 Keys (string, stable, sortable, human-readable)

| Concept | Key format | Example |
|---|---|---|
| Region | `r_<rx>_<rz>` | `r_0_0`, `r_neg1_2` |
| Cell | `<region>:c_<cx>_<cz>` | `r_0_0:c_07_11` |
| Interior space | `int.<def_id>` | `int.dungeon.mine.hollow_deep` |
| Interior cell | `int.<def_id>:c_<cx>_<cz>` | `int.dungeon.mine.hollow_deep:c_02_03` |
| Portal anchor | `<interior key>#entry_<n>` | `int.dungeon.mine.hollow_deep#entry_0` |

Negative indices are spelled `neg<abs>` rather than `-` so keys stay filesystem-safe, URL-safe, and free of quoting problems — this matters because these keys become filenames and CLI arguments in the persistence and tooling layers.

### 3.3 Conversions (exact, no floating-point accumulation)

```
regionOf(world)   = ( floor(world.x / 2000), floor(world.z / 2000) )
cellOf(world)     = ( floormod(floor(world.x / 100), 20), floormod(floor(world.z / 100), 20) )
regionLocal(world)= ( world.x - 2000*rx, world.z - 2000*rz )

floormod(a, n)    = ((a % n) + n) % n      // Euclidean; NOT the language's remainder
```

`floor`, not truncation — truncation makes cells straddling the origin asymmetric, a classic off-by-one that produces a one-cell seam at world origin and nowhere else.

**`floormod`, not `%` — this is a second, independent trap in the same line and it is the one that bites.** In C#, C, C++ and JavaScript the remainder operator truncates toward zero, so it returns a **negative** result for negative operands; the cell index would then fall outside the `[0, 19]` range defined in §3.2 and produce keys such as `r_neg1_0:c_neg1_11` that no baseline generator, delta record, or navmesh bake will ever match. Python's `%` happens to be Euclidean, which is why this error survives review by anyone reasoning from Python. `floormod` must be a single shared helper, not re-derived at each call site.

Every conversion must be tested at `x,z ∈ {-200.0, -100.0, -0.001, 0, 0.001, 100.0}` **with these expected cell indices stated, so the test can fail**:

| `world.x` | `floor(x/100)` | naive `% 20` (**wrong**) | required `floormod` |
|---|---|---|---|
| `-200.0` | `-2` | `-2` ✗ out of range | **`18`** |
| `-100.0` | `-1` | `-1` ✗ out of range | **`19`** |
| `-0.001` | `-1` | `-1` ✗ out of range | **`19`** |
| `0.0` | `0` | `0` ✓ | `0` |
| `0.001` | `0` | `0` ✓ | `0` |
| `100.0` | `1` | `1` ✓ | `1` |

Three of the six mandated values fail under the naive operator. `regionOf` uses `floor` with no modulo and therefore has no analogous trap, but it must still be tested at the same values.

### 3.4 Deterministic streams are keyed by cell, never by traversal order

Every procedural decision (vegetation placement, prop scatter, minor encounter roll, resource node jitter, cosmetic variation) draws from a stream derived as:

```
stream(cell_key, purpose) = PRNG( hash(world_seed, worldgen_version, content_hash, cell_key, purpose) )
```

This is the mechanism that makes `D-05` delta saves possible: a cell's baseline is a pure function of its key, so it can be regenerated on demand, in any order, on any machine, in any session. **A generator that consumes a shared global stream in traversal order is a save-corrupting bug**, because loading a different subset of cells changes every subsequent draw.

---

## 4. Authored vs procedurally assisted

The charter: "Procedural generation may assist development but should NOT replace intentional world design."

| Content | Authoring | Notes |
|---|---|---|
| Region layout, biome masks, road network, rivers | **Hand-authored** (data files + editor tooling) | The shape of a region is a creative decision |
| Town footprint, building plots, walls, landmarks | **Hand-authored** | One excellent town is the region's anchor |
| Dungeon layouts, boss arenas, secret rooms, puzzle spaces | **Hand-authored** | Charter: major dungeons receive intentional design |
| Quest locations, epic-quest staging | **Hand-authored** | |
| Terrain mesh detail, erosion, cliff shaping | Procedural *assist* from authored control curves | Deterministic per content version |
| Vegetation, rocks, ground clutter, decals | Procedural from authored biome + density rules | Cell-keyed streams |
| Resource node placement | Procedural within authored density/category rules | Deterministic node keys (§5.4) |
| Minor encounters, wildlife population composition | Procedural from authored creature families + budgets | Cell-keyed streams |
| Loot variation, cosmetic variation, name suffixes | Procedural from authored tables | Persisted RNG streams (`PERSISTENCE.md` §5.5) |
| NPC schedules | Authored as schedule definitions + anchors | Anchors are hand-placed; timing is authored |

**Consequence for tooling.** Because procedural output is a pure function of `(seed, content, key)`, the editor must support "re-roll this cell with a different local seed" as a *content-authoring* action that writes an authored override into the content pack — not as a runtime-only variation. Otherwise a designer's hand-tuned cell would silently change on every run.

**Authoring artifact.** Each region has a `region.yaml` declaring: region key, biome palette, cell-level generation parameters, the list of authored cells, interiors anchored in it, spawn population tables, world-event hooks, and the region's content-completeness checklist (town, wilderness, 3–5 dungeons, secrets, boss, epic quest). That checklist is a validator, not prose.

---

## 5. Cells: the unit of everything

A cell is not a file. It is a **record** produced by joining authored data, procedural output, and persistent delta:

| Cell facet | Source | Persisted? |
|---|---|---|
| Terrain heightfield, splat, hole flags | Procedural from region control data | No (regenerable) |
| Authored overrides (a hand-placed ruin, a bridge) | Content pack, keyed by cell | No (content) |
| Vegetation / props | Procedural from cell stream | No |
| Collision, navmesh, occlusion | Baked from the above | No |
| Resource nodes | Procedural placement + authored density | Baseline no; harvested state **yes** (`cells.msgpack`) |
| Spawn populations | Authored population tables per cell | Baseline no; diverged counters **yes** |
| Live creatures/NPCs/items | Runtime | **Yes** if diverged (`entities.msgpack`) |
| Player buildings | Runtime | **Yes** (`buildings.msgpack`) |
| World flags (doors, switches, traps, destruction) | Runtime | **Yes** (`cells.msgpack`) |
| Assignable NPC schedule anchors | Authored | No |

### 5.1 Cell lifecycle (a state machine, and the states are named)

```
Unloaded  --(streamer interest / abstract tick)-->  Abstract   (Tier C/D)
Abstract  --(player approaches, promotion)-------->  Simulated  (Tier B)
Simulated --(player within full-sim radius)------->  Loaded     (Tier A)
Loaded    --(player leaves)----------------------->  demote, stepwise, A->B->C->D
```

Transitions are **stepwise**: A→B→C→D on demotion, D→C→B→A on promotion. No A→D shortcuts, because each step is where reconciliation happens (§7.3). Exceptions: on a hard load boundary (interior entry, fast travel, death respawn) the player's origin cell is force-promoted and the rest are force-demoted with the same reconciliation functions run in bulk; the *step functions* are reused, only the batch differs.

### 5.2 The dirty flag lives with the cell, and it lives in the domain layer

Per `D-05`, a cell is persisted only when it diverges. Divergence is tracked by per-cell dirty bits with enumerated reasons (`nodes`, `spawns`, `flags`, `entities`, `buildings`, `terrain`). Bits are set by the mutation dispatcher, never by presentation (`D-11`).

**The bits are a performance hint; the authoritative dirty set is derived at save time** by diffing against the regenerated baseline (`PERSISTENCE.md` §5.2, invariant `I-7`). This distinction is deliberate and resolves `RK-P02`/`RK-11` in the direction that cannot lose data: a bit that is *wrongly set* costs a wasted diff, and a bit that is *missed* — because a change never passed through the dispatcher — costs nothing, because the save-time diff still finds the divergence. A save that trusted the bits alone would grow monotonically in *commands issued* rather than in *world state*.

The residual risk is therefore **performance, not correctness**, and `PERSISTENCE.md` §11 `RK-P02` records it that way. The dispatcher is still required: it is what keeps the hint accurate enough to make the diff cheap, and it is what `RK-11`'s completeness audit exercises.

### 5.3 Unloading rules

| Situation | Action |
|---|---|
| Cell demotes out of Loaded with **no** dirty bits | Free runtime objects; nothing written |
| Cell demotes out of Loaded with dirty bits | The streamer may **not** discard. It requests a scoped save of that cell (or its shard), then discards. This is a correctness rule, not an optimization |
| Cell demotes out of Simulated/Abstract | Coarse state (schedule cursor, counters) is handed to the tier systems; live actors are removed by reconciliation (§7.3) |
| Cell is an interior currently being visited | Never unloaded while the player is inside; its exterior anchor cell may be |
| Cell contains a player building | Treated as `pinned`; its persisted record is retained in memory while the region is loaded, and it is exempt from the "no dirty bits → discard" path for the *record*, though its geometry is fully streamed out |

### 5.4 Resource nodes and deterministic identity

A resource node is a **property of the world**, not an owned instance, so it does not get a ULID (`D-04` distinguishes exactly this case). Its identity is:

```
node_key = "node.<cell_key>.<index>"     // index assigned by the cell's deterministic generator
```

Stored state per harvested node: `state`, `last_harvest_tick`, `harvest_seq` (see `PERSISTENCE.md` §5.5). The ready time is a pure function of persisted data plus world time, with jitter drawn from `hash(node_key, harvest_seq, world_seed)`. Therefore:

- A harvested node cannot respawn inconsistently across save/load: nothing about it is stored as a countdown.
- Two sessions that load the same save at the same `game_tick` compute the same respawn set.
- Reload-scumming for a rare node yield is impossible because yield draws from a persisted RNG stream.

Nodes whose baselines are untouched contribute **zero bytes**. A cell the player walked across without gathering is not persisted at all.

### 5.5 Spawn populations and budgeting

Populations are authored per cell as *families with budgets*, not as fixed spawn lists:

```yaml
# illustrative, not content
cell: r_0_0:c_07_11
populations:
  - id: pop.r_0_0.c_07_11.wolves
    family: creature.beast.wolf_grey
    budget: { target: 5, min: 2, max: 7 }
    respawn_window_ticks: 72000        # = 3600 real seconds = 60 real minutes at 20 ticks/s.
                                       # In GAME time that is 1800 game minutes = 30 game hours
                                       # (config.time: 1 game minute = 2 real seconds), i.e. longer
                                       # than a game day — so this window is deliberately a
                                       # multi-day population respawn, NOT "1 game hour".
                                       # Game-time units are defined in DATA_MODEL.md §4.19 only.
    night_bias: 0.6                    # fraction of budget present at night
    aggro_radius: 40
    spawn_points: [ { p: [12.0, 1.2, 41.0], r: 90 }, … ]
  - id: pop.r_0_0.c_07_11.deer
    family: creature.beast.deer
    budget: { target: 3, min: 1, max: 4 }
    flee_only: true
```

Rules:

- `alive` count is **the only persisted quantity** for an undiverged population. Individual undiverged creatures are never stored.
- Spawn positions are drawn deterministically from the population stream; a diverged creature (killed by the player, tamed, quest-flagged, carrying a unique item) is promoted to an `entities.msgpack` record and thereafter remembered individually.
- Budgets are enforced at two levels: **per-cell** (a cell never exceeds its authored budget) and **global live-actor caps** by tier (§6.2). If global caps are saturated, spawn suppression is applied by distance, then by `min` guarantee (never suppress below `budget.min` in a Loaded cell).
- Population drift over time (over-hunting lowers `target`, neglect raises it) is a Tier C economy/schedule concern, is bounded by `[min, max]`, and is persisted in the cell delta.
- **Honest limitation:** population counts are the *only* thing that "continues to exist" for generic wildlife when the player is away. Individual generic wolves do not live simulated lives; they are counts, respawning on a deterministic schedule. Only named/flagged creatures have continuity. This is stated plainly because pretending otherwise would be a lie the player could eventually detect.

---

## 6. The four tiers and the global tick

`D-06` assigns every cell and entity a tier: **A** full / **B** simplified regional / **C** abstract schedule+economy / **D** stored state only.

| Tier | Name | What runs | Update rate | Entities | Lives where |
|---|---|---|---|---|---|
| **A** | Loaded / full simulation | Full AI, combat, physics, animation, pathfinding, perception, inventory mutation, interaction, dialogue | per-frame (AI decisions at 10–20 Hz) | ≤ 60 actors | Cells inside the full-simulation radius |
| **B** | Simulated regional | Coarse movement along authored/derived routes, schedule band evaluation, simple threat/flee, no combat resolution, no inventory, no dialogue | 2 Hz, position interpolation only | ≤ 300 actors | Annulus between full-sim and regional radii |
| **C** | Abstract | Schedule-band state machine (work/sleep/eat/travel/visit), coarse position at **anchor granularity**, settlement economy (production/consumption/prices), population drift, construction progress, event timers | Poisson-bucketed, ~0.2–2 Hz aggregate | thousands (data, not objects) | Everything else in loaded regions + a bounded list of "interesting" distant actors |
| **D** | Stored state only | Nothing executes. The record is read on demand when the entity is referenced (e.g. a quest checks an NPC's location) | n/a | unbounded | Unloaded regions, dormant interiors, distant settlements |

### 6.1 The global tick

There is exactly one authoritative clock: an integer `game_tick`, advanced by the domain layer (never by the renderer, never by wall-clock time alone).

| Property | Value | Rationale |
|---|---|---|
| Nominal rate | **20 ticks/second** at 1× time scale | Coarser than frames, fine enough for schedules and combat-relevant timing; an integer tick makes save/load and offline catch-up exact |
| Accumulation | Fixed-step accumulator from real time, clamped | Prevents spiral-of-death after a stall and after a long load |
| Persisted | `game_tick` in the manifest | Offline/abstract advance is computed as `tick_delta`, and everything derived must be a function of it |
| Max catch-up per session resume | bounded (e.g. 30 game-days) + a policy flag for "while you were away" | Unbounded catch-up is a correctness hazard and a load-time hitch |
| Time scale | debug/pause only | Slow-motion effects are presentation; they must not change tick semantics |

**What the tick does per tier:** A actors run per-frame with decisions gated on the tick; B runs on a 2 Hz sub-tick; C runs on a bucketed scheduler that spreads work across ticks (bucket by hashing the entity key, so no single entity's cost lands in the same frame every time — and so the work distribution is deterministic). D consumes no tick at all.

### 6.2 Radii and budgets (targets, not measurements)

| Knob | Default target | Range | Notes |
|---|---|---|---|
| Visual streaming radius | 400 m (9×9 cells) | 200–800 m | Terrain/props/foliage LOD |
| Physics/collision radius | 120 m | 60–200 m | |
| Full simulation radius (Tier A) | 150 m | 80–300 m | Actors |
| Regional simulation inner radius (Tier B) | 600 m | 300–1200 m | |
| AI activation distance | 45 m | 25–80 m | Perception/behaviour start |
| Global live actor caps | A: 60, B: 300 | — | Enforced with spawn suppression, never with silent despawn of a diverged actor |
| Global tick | 20 Hz | fixed | |
| Interior load budget | ≤ 500 ms P95, hidden by a transition | — | |
| Frame target | 16.6 ms (60 fps) at 2×2 km region | — | **Unmeasured.** `D-01`'s revisit trigger 1 explicitly requires measurement before any engine re-evaluation |

All of these are **single configuration values in one place**, readable at runtime, with a debug overlay that shows the tier of every actor in view. That overlay is a Phase-1 deliverable, because tier bugs are invisible without it.

---

## 7. Tier transitions: the known danger

`D-06` names transitions as the hard part. This section is the mitigation.

### 7.1 What abstract tiers may and may not do

| May advance at Tier B/C | May **never** advance at Tier B/C |
|---|---|
| Schedule band (working / sleeping / travelling / eating / visiting) | Combat resolution of any kind |
| Coarse position at anchor granularity | Damage, death, downing |
| Travel along a defined route between anchors | Inventory or container contents |
| Settlement economy totals, prices, stock counts (bounded) | Equipment, durability, charges |
| Population counts and respawn clocks | Quest objective advancement that requires player presence |
| Construction/build progress queued before the player left | Dialogue, relationship decisions, faction-defining acts |
| Event timers (sieges, festivals, migrations) | Loot generation tied to a specific killed actor |
| Needs (hunger, fatigue) as coarse bands | Anything that would be illegal for a live actor to be in |

That second column is the whole safety argument: **abstract tiers never produce a state that Tier A cannot legally instantiate.**

### 7.2 Promotion (C/B → A): reconcile, never adopt

The NPC arrives near the player. The abstract state says: band `travelling`, coarse position = anchor `town_gate`, no fine position, no equipment state, no health, no inventory.

Reconciliation order (this order is normative):

1. **Resolve identity.** The record is keyed by ULID (`D-04`). If the ULID does not resolve in the registry, the promotion is a bug and is logged loudly rather than fabricating a new identity.
2. **Resolve a legal fine position.** The abstract anchor is a *region*, not a point. Pick a concrete point inside it using a deterministic draw from `hash(ulid, game_tick, "promotion")`. Never place the actor at the anchor's exact origin, and never pick a point the actor cannot legally occupy (inside geometry, off navmesh, inside a player building without permission). Fall back to the anchor's authored stand point; if that fails, refuse the promotion and keep the actor at Tier B.
3. **Instantiate missing state from defaults, marked as such.** Health, stamina, equipment, inventory and status effects come from the NPC's *definition* defaults (plus any persisted overrides in `entities.msgpack`). An abstract tier must never have invented a health value, so there is nothing to adopt.
4. **Advance the schedule clock to now.** The schedule cursor is set from `game_tick`, so the actor's band is consistent with the world clock the moment it exists.
5. **Legality pass.** Run the domain's invariant validator on the promoted actor before it becomes visible: valid position, valid definition, no two containers holding it, own inventory consistent, no illegal status combination. A failed legality pass is a *bug report*, not a silent repair.
6. **Presentation pop-in.** The actor is instantiated off-screen or occluded where possible; promotion is scheduled on the tick, not mid-frame, so the first visible frame shows a consistent actor.

**The rule in one sentence:** on promotion, abstract state supplies *intent and coarse location only*; everything else is reconstructed to a legal default and then validated — never copied.

### 7.3 Demotion (A → B → C): checkpoint what is legal to abstract

On demotion, live state is compressed only along the sanctioned axes:

- Fine position → the nearest schedule anchor on the actor's route; the residual difference is discarded.
- Combat: an actor in combat **cannot** be demoted into an abstract tier. It is first resolved (combat ends, or the actor is pinned at Tier B with a conservative "was fighting" flag) so that "abstract combat" never exists.
- Inventory/equipment: written to `entities.msgpack` if diverged, then released from memory. Abstract tiers hold no item state.
- Kills/loot: fully resolved at Tier A. Nothing is deferred into abstraction.
- Needs and schedule: band + cursor persisted.

### 7.4 Hysteresis: entities must not thrash

Naive radius checks produce boundary thrash — an NPC pacing back and forth over a radius line would be promoted and demoted repeatedly, each time paying instantiation cost and risking visible pop-in.

| Tuning | Value (target) | Purpose |
|---|---|---|
| Promote radius vs demote radius | Demote at **1.25×** the promote radius (e.g. promote 150 m, demote 188 m) | Creates a dead band |
| Minimum dwell time | 5 s in a tier before any further change | Prevents oscillation on jitter |
| Hysteresis quota | At most N promotions and N demotions per tick (e.g. 4 each), priority-ordered by distance to the player and by `pinned` flag | Prevents a boundary sweep from stalling a frame |
| Boundary cooldown | An actor that demoted within the last 10 s cannot be promoted by radius alone; only by a `pinned` reason (quest, combat, dialogue) | Targeted at exactly the thrash case |
| Pinned exemptions | Actors in dialogue, in combat, quest-flagged, or within 25 m are **never** demoted by radius | Player-visible correctness beats budget |
| Per-cell hysteresis | A cell's tier changes at most once per 2 s | Cell-level work (navmesh, spawn, props) is far more expensive than actor-level |

Cell tier and actor tier are **separate assignments** that normally correlate. An actor can be Tier A inside a Tier B cell only when a `pinned` reason forces it; such an actor is charged against the Tier A cap and its cell is *not* promoted wholesale.

#### Tier A admission control — the cap must be an admission test, not a spawn rule

An adversarial review found that the Tier A actor cap (§6.2, "A: 60") was enforced **only** by spawn suppression — which governs *creating* new actors (`S-31`), not *promoting* existing ones. The promotion path consults proximity, `pinned` reasons and a per-tick quota, and never the cap. An actor could therefore be promoted into Tier A while the cap was already full, which makes the frame budget in §12 and the `RK-02` measurement non-reproducible: the same build could pass or fail depending on play pattern.

**Rule.** Promotion to Tier A includes an **admission test against the cap**, and the test's outcome is explicit rather than silent:

| Outcome | When | Consequence |
|---|---|---|
| **Admit** | Tier A occupancy < cap | Normal promotion |
| **Queue** | Occupancy = cap, but the candidate is `pinned` | Held at Tier B; re-evaluated next tick with a bounded retry window; if the window expires the actor is admitted **anyway** and the over-budget reason is logged |
| **Refuse** | Occupancy = cap, candidate is not `pinned` | Stays at Tier B; no error, because this is the designed outcome |

**`pinned` actors are subject to the test too.** They win ties, but they cannot silently exceed the cap: the "admit anyway with a logged reason" path exists precisely so that player-visible correctness (a quest NPC, a companion in a fight) still wins while the budget violation becomes *measurable* rather than invisible. A settlement with 22 town NPCs, a fight, a quest NPC and a 6-companion roster can legitimately produce more pinned candidates inside 150 m than the cap allows; that situation must show up as a counter on the debug overlay, not as an inexplicable frame drop.

**The debug overlay required by §12 must display Tier A occupancy against its cap**, headroom included, because an unbounded cap is the one budget failure that no profiling session can distinguish from ordinary content density. In addition, `RK-15`/`RK-16`-style enforcement: admission decisions increment `PromotionRefused` and `PromotionOverBudget` counters, and the Phase-2 frame-budget spike asserts `PromotionOverBudget == 0` on its scripted camera path.

**Interaction with §12's response order.** §12 says that on a missed frame budget the first lever is "reduce radii". That lever *increases* the number of promotion candidates per second, so it must be applied together with a **reduced Tier A cap**, or the response makes the admission problem worse while appearing to help. Stated here so the two sections cannot be applied independently.

### 7.5 The visible failure modes (what to watch for in playtest)

`D-06`'s revisit trigger is player-observed discontinuity. Concretely, the three symptoms:

1. **Teleporting** — an NPC visually jumps on promotion. Cause: coarse anchor is too coarse, or step 2 was skipped.
2. **Schedule contradiction** — an NPC is in the tavern at a time its schedule says it is asleep, or two copies of the same ULID exist. Cause: promotion adopted abstract state, or the actor was duplicated across a demotion.
3. **Boundary thrash** — stutter near a radius line. Cause: hysteresis dead band or dwell time missing/too small.

---

## 8. Interiors and dungeons

### 8.1 Separate space, embedded anchor

An interior is its **own space** (`int.<def_id>`) with its own local cell grid, its own tier assignment per interior cell, and its own delta record. It is not embedded geometrically in the region; the region contains a **portal anchor** that declares where the interior is entered and exited.

Rejected alternative: world-embedded interiors (a dungeon is just cells under the terrain of the same region). Reasons: interiors must be free to exceed region bounds (a 1 km² underground complex), vertical stacking would break the flat region tiling of §3.1, and interior content is audited and shipped separately from outdoor content. The cost we accept is an explicit load boundary.

### 8.2 Load boundary rules

- Entering an interior is a **hard boundary**: the exterior is demoted in bulk (A→B or C) with the §7.3 checkpoints, the interior's entry cell is promoted to A, and pending dirty exterior state triggers a scoped save (`PERSISTENCE.md` §8.1).
- The player's exterior position is persisted as the return anchor plus the portal ID, so exiting is exact even if the portal definition moved between versions (the portal resolves through the content alias map).
- Interiors are **not** simulated while the player is outside unless explicitly flagged (`persistent: true`), in which case they run at Tier C: event timers, occupant factions, boss-alive flags. An unflagged dungeon is frozen — nothing in it happens while the player is away. This is a deliberate, defensible simplification and should be stated to the player indirectly through design (an unflagged dungeon is a place outside time).
- Large dungeons may contain shortcuts, secret areas, multiple entrances, hidden bosses (charter §17). Each entrance is a separate anchor; entering via a different anchor reaches the same interior space and the same persisted delta. Anchors are **not** separate instances of the dungeon.

### 8.3 Interior content discipline

Interiors get the strict authored treatment of §4: hand-authored layouts, no procedural room generation for major dungeons. Deterministic procedural assistance is permitted for clutter, minor loot placement, and cosmetic variation, always from the interior cell's keyed stream.

---

## 9. What continues to exist when the player is far away

This is the section the charter's "world that exists independently of you" pillar rests on, so it is stated concretely and honestly.

| Thing | Continues to exist? | At what tier | Notes |
|---|---|---|---|
| Named/important NPCs | **Yes** | C (or D if in an unloaded region) | Schedule bands advance, coarse anchor updates, relationships/quest flags persist. They do *not* gain skills, items or wealth through abstract play in Phase 2 |
| Generic NPCs (guards, villagers, unnamed hirelings) | **Partly** | C | Existence + schedule band + settlement membership. No individual agency |
| Generic wildlife | **No** — counts only | C | Populations drift within `[min,max]`; individual generic wolves are not tracked |
| Named creature bosses / rare spawns | **Yes** | C/D | Alive/dead/respawn window is real state; location is a spawn anchor |
| Settlements (production, stock, prices) | **Yes** | C | Coarse economy only; per-item merchant stock is reified on interaction |
| Faction control, regional tension | **Yes** | C | Bounded event log, no simulation of individual faction members |
| Resource nodes | **Yes** — as schedules | D until visited | Ready time is a pure function of persisted `(last_harvest_tick, harvest_seq)` + world time |
| Player buildings | **Yes** | C/D, `pinned` | Stored as explicit exception records in their cells; never lossy-unloaded |
| Interior dungeons (unflagged) | **No** — frozen | D | Nothing happens inside until entered |
| Interior dungeons (flagged persistent) | **Yes** | C | Event timers, occupants, boss flags |
| Roads, weather, day/night | **Yes** | global tick + region weather state | Weather is a regional state machine, not per-cell simulation |
| World events (caravans, incursions, migrations) | **Yes**, bounded | C | A capped set (e.g. 8 region-wide) with authored weights; events resolve abstractly and persist their outcome |
| Physics, combat, dialogue, pathfinding anywhere the player is not | **No** | — | Never simulated. Stated plainly: this is the design, not a limitation to be worked around |

**The honest summary.** What continues is *schedules, economies, populations, and records*. What does not continue is *action*. The player's perception of a living world comes from returning to find the schedule moved, the stock changed, the population drifted, and the named NPC somewhere its own life plausibly put it — not from anything having actually fought while out of sight.

---

## 10. Player buildings as persistent exceptions

Per `D-08`, building is socket/snap assembly of authored pieces; a piece is a row, not a physics resolution. That makes buildings the cleanest persistent exception in the system.

| Aspect | Rule |
|---|---|
| Identity | A building is a ULID-keyed structure (§`PERSISTENCE.md` §5.4) with a footprint of one or more cells; pieces are ULID-keyed rows with a `def_id` and a socket path |
| Persistence | Written to `buildings.msgpack`; the owning cells are marked dirty with reason `buildings` |
| Cell unload | The building record is `pinned`. Geometry streams out; the record stays in memory while the region is loaded and always survives in the save. A building is never a casualty of streaming |
| Cross-cell footprints | A building may span cells. Ownership/geometry is stored once on the structure and referenced by each cell's delta, so a cell boundary cannot split a structure's state |
| Placement legality | Validated by domain rules at command time (terrain slope, socket graph connectivity, no blocking of authored story geometry, no overlap with a pinned spawn) — never by physics |
| Navigability | Guaranteed by the socket/snap model (`D-08`) so NPCs and companions can path through player structures. Navmesh around buildings is rebuilt per cell on change, debounced |
| Damage | Per-piece health applied by explicit rules; repaired by explicit command. Damage is persisted per piece |
| NPC assignment | Buildings hold ULID references to assigned NPCs; assignment state persists; abstract tiers know a settlement has a steward/guards without simulating them |
| Defense | Attack frequency, alert level and `disabled_attacks` persist. The charter requires players who dislike base defense to be able to greatly reduce or disable it, so this is a persisted gameplay setting, not a client preference |
| Ownership transfer | Out of scope in Phase 0; the `owner` field is a ULID so it can be added later without a schema break |

**Interaction with tiers.** A building in a loaded region whose cell is Tier C still consumes/produces abstractly (a forge with fuel and an assigned smith produces at Tier C). Nothing about that production is simulated per-tick on the main thread; it is computed from `(state, tick_delta)` on demand — the same determinism rule as resource respawn.

---

## 11. Streaming, LOD, and the presentation contract

| System | Approach | Notes |
|---|---|---|
| Terrain | Cell-sized tiles loaded by the streamer, morphed/LOD'd at distance, single authored control mesh per region | Godot has no World Partition equivalent (`D-01`); this is ours |
| Foliage/vegetation | MultiMesh instancing per cell, density by biome and LOD ring; culled beyond the visual radius | Instancing is the primary lever for the charter's "large environments on a normal PC" |
| Props | Instanced static meshes; per-cell grouping so a cell unload is a single bulk free | |
| Actors | Pooled presentation nodes; the domain knows nothing about pooling | `D-11`: pooling is a presentation concern |
| LOD | 3 tiers + impostor at the outer ring, authored per asset; a distance table in one config | |
| Occlusion | Portals for interiors; authored occluders in the town | Interiors are naturally occlusion-friendly, which is part of why they are separate spaces |
| Shadows | Cascaded, capped radius; distant cells use a coarse proxy | |
| Navmesh | Recast-style, baked per cell, stitched at cell borders, rebuilt debounced on building change | Seam handling is a known hard part; budget a dedicated pass |
| Audio | Cell-scoped ambience + locality groups; no per-actor source beyond the regional radius | |

**Contract.** Presentation reads cell/tier/actor state and submits commands. It never assigns tiers, never marks cells dirty, and never mutates world state (`D-11`). Tier assignment is a domain-layer decision; presentation may *request* a priority hint (e.g. "this actor is on screen") which the domain is free to ignore.

---

## 12. Performance budget assumptions (explicitly unmeasured)

No profiling has been done. The following are **budgets to design against**, in the spirit of `D-01`'s requirement that engine re-evaluation only follow *measurement*.

| Assumption | Value | If it proves wrong |
|---|---|---|
| Target frame | 16.6 ms at 60 fps, 1080p | Reduce visual streaming radius first; it is the cheapest lever with the least gameplay impact |
| Main-thread world systems | ≤ 4 ms | Move C-tier buckets to a worker; reduce A-tier caps |
| Streaming work per frame | ≤ 3 ms, spread over ≥ 4 frames per cell | Pre-load ahead of movement direction; add a short transition cover |
| Tier A actors | ≤ 60 | Reduce full-sim radius before reducing AI quality |
| Tier B actors | ≤ 300 at 2 Hz | Increase B tick interval to 1 Hz |
| Tier C entities | ≤ 20k records, ≤ 2 ms/tick aggregate | Increase bucket spreading; move distant settlements to D |
| Peak memory | ≤ 4 GB total, ≤ 1.5 GB world content | Shrink LOD rings, reduce foliage density |
| Save main-thread cost | ≤ 2 ms P99 (`PERSISTENCE.md` §8.2) | Shard more aggressively |
| Interior load | ≤ 500 ms P95 | Pre-warm interiors adjacent to the player |

**Knobs, all in one configuration object, all runtime-inspectable:** visual streaming radius, collision radius, full-sim radius, regional-sim radius, AI activation distance, tier caps (A/B/C), global tick rate, tier bucket width, hysteresis factors (§7.4), LOD distances, foliage density scalar, shadow distance, and interior pre-warm count. The debug overlay must display: current cell key, its tier, every actor's tier and the reason for its last transition, the caps' headroom, and the last save time.

**Risk if budgets are wrong.** The 2×2 km region is the test. If a single region cannot hold 60 fps with 60 Tier A actors, the response order is: reduce radii → reduce caps → reduce AI decision rate → reduce LOD/foliage → *then* reconsider `D-01` (`D-01` revisit trigger 1, which requires exactly the measurement we would then have).

---

## 13. Risks and open problems (honest list)

| ID | Risk | Why it matters | Current state |
|---|---|---|---|
| RK-A1 | Tier-transition reconciliation incompleteness | The documented failure mode of `D-06`; visible as teleporting or contradictory NPCs | Mitigated by design (§7.2/§7.4) but **unimplemented and untested**. Needs a headless harness that promotes/demotes synthetic actors thousands of times and asserts legality. Related project risk: `RK-07` |
| RK-A2 | Navmesh stitching at cell seams with player buildings | Broken navmesh silently breaks companions, which the charter says must not feel frustrating | Not solved. Dedicated implementation pass; test with a building straddling four cells |
| RK-A3 | Determinism of generation across content versions | The baseline must regenerate identically or every delta is invalid | Same risk as project `RK-01`, viewed from the world side. A CI cell-hash test is the earliest cheap validation |
| RK-A4 | Offline catch-up blowup | A long absence needs a huge `tick_delta`, causing load hitches and absurd abstract outcomes (a settlement producing for 300 days) | Partially mitigated by bounding catch-up (§6.1). **Open problem:** the right bound and the right "while you were away" policy need a design decision and playtest |
| RK-A5 | "Frozen dungeon" reading as a bug | An unflagged dungeon where nothing changes may read as a broken world | Accepted simplification, but a *design* risk rather than a technical one. Revisit if playtest reports it |
| RK-A6 | Cell size choice (100 m) | Wrong cell size is expensive to change later because it pervades keys, saves and spawn tables | Chosen for budget reasons (§2). A migration path exists (keys are strings, cells could be re-derived) but needs a save migration; decide before Phase-2 content volume grows |
| RK-A7 | Population drift tuning | Drift that is too strong exterminates or floods wildlife and looks broken | Bounded by `[min,max]`; needs playtest tuning |
| RK-A8 | Global live-actor caps vs. persistence | Spawn suppression must never appear to *delete* a diverged actor | Mitigated by the rule that diverged actors are `entities` records and are exempt from cap-driven removal; needs a test |
| RK-A9 | Weather/world-event scope | Regional weather and capped events are the most likely place to accidentally build a simulation that cannot be bounded | Capped by design (≤ 8 region events); enforce it in code, not by convention |

### Assumptions recorded

- **Platform baseline** per `DECISIONS.md` open question 2: Windows desktop, 16 GB RAM, mid-range discrete GPU, non-VR, non-console, Phase 0–2.
- **Region content budget** per the charter: 2×2 km, one excellent town, wilderness, 3–5 dungeons, secrets, crafting/building/companion content, one boss, one epic quest.
- **World extent at Phase 2:** exactly **one region**. Multi-region tiling exists in the model and is *not* populated. Per `D-12`, no region-adjacency or cross-region simulation work is scheduled before the vertical slice works.
- **Interiors are separate spaces** (a decision with an explicit load boundary, §8.1).
- **No structural physics** anywhere in building or terrain (`D-08`).
- **No networking** in the world model. The extension points preserved, per `D-12`, are: mutation only via commands, stable identities, sparse versioned persistence, and tiered simulation — all four are visible in this document, and none of them requires a server to exist.
- **Numbers in §6.2 and §12 are budgets.** Any statement that a frame target is *met* would be unsupported until the vertical slice has been profiled on the target machine.
