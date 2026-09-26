# M7 research notes - spatial model, movement, collision, blockers, terrain, pathing, following (key: `spatial_movement`)

Source of truth: read-only snapshot of `origin/main` at commit `e10d2c4`. All `path:line` citations are repo-relative and valid at `e10d2c4`.
Other sources are labelled: **[untracked, authority unconfirmed]** = a file only in the `G:/UNNAMED/docs` working tree; **[report, not source]** = `G:/UNNAMED_HISTORY/*`.
**FACT** = read in code/content/docs and cited. **INFERENCE** = my reading or derivation; verify before relying on it.
Units everywhere in the domain: integer millimetres (`long ...Mm`), millidegrees (`int ...Mdeg`, `[0, 360000)`), ticks (`long`, 20 Hz, 50 ms).

---

## 0. The design-relevant conclusions (read this if nothing else)

1. **There is no pathfinding anywhere in the authoritative simulation.** Every mover steers straight at a goal point and lets `Kinematics.Step` slide it along whatever it hits. FACT: `docs/M3D_STATUS.md:78` ("Pathfinding: creatures move straight and slide along blockers. A target behind a wall is searched for, not pathed to."). The only "path" structure is the companion's breadcrumb **trail** of the player's own positions (`src/World/Runtime/Companions.cs:308-316`, `:325-361`), plus content **patrol routes** (`route_m`) walked leg by leg in straight lines (`src/World/Runtime/Creatures.cs:425-433`).
2. **Geometry is 2D footprints on the XZ plane, extruded to a height:** axis-aligned boxes (`BoxBlocker`) and circles (`CircleBlocker`), integer-mm fields, with an optional `ClearanceMm` for overhangs (`src/Domain/Spatial/Blockers.cs:11-125`). No OBBs, no polygons, no polylines. Code comment: "Structures in the greybox are axis-aligned, which keeps the math exact" (`src/Domain/Spatial/Blockers.cs:40`). ROADMAP M7 asks for "free rotation and socketing" (`docs/ROADMAP.md:283`), which the current blocker set cannot represent (INFERENCE: it needs an OBB shape, or rotation snapped to 90°).
3. **Collision is circle-vs-footprint push-out, not sweep:** sub-steps no longer than half the body radius, up to 4 relaxation passes per sub-step, then a clamp to the region's bounds, then rounding to whole mm (`src/Domain/Spatial/Kinematics.cs:129-214`). Terrain never blocks. There is **no slope limit and no step height**: Y is always `terrain height + jump lift` (`src/Domain/Spatial/Kinematics.cs:159`). Nothing is stood on.
4. **`Kinematics.Step` is the one movement function**, used by the player's `MovementSystem`, every creature, the companion, and presentation's prediction (`src/Presentation/Player/PlayerController.cs:127`). A nav system that emits **`MoveIntent`s** (steering) and keeps collision inside `Kinematics.Step` preserves this contract; one that writes positions would break it (INFERENCE).
5. **Static walkable space is one region-wide `WalkSpace`** (bounds + `TerrainGrid` + `ImmutableArray<Blocker>`), built once from content and never changed at run time (`src/Domain/Spatial/Kinematics.cs:104`; `src/Content/WorldContent.cs:292`). The only dynamic geometry is **doors and barriers**, whose passability is a `world.*` flag in the cell of the footprint's centre, re-read on every query (`src/World/Runtime/Systems.cs:41-48`; `src/Domain/Spatial/RegionLayout.cs:94-97`). There is no API to add or remove a structure at run time - building pieces would be the first.
6. **Cells do not partition movement.** The whole 200 m x 200 m region (4 cells) is one `WalkSpace`; seams are invisible to collision. Cells matter only for flags, tiers, deltas and RNG keys. One static blocker already straddles the x = 100 m seam (`den_rock_west`, `content/regions/ashen_hollow.yaml:94`). The seam problem for nav is future work (`docs/RISK_REGISTER.md:36`, RK-14; `docs/WORLD_ARCHITECTURE.md:469`, RK-A2).
7. **The companion's reliability comes from teleport, not navigation:** more than 30 m behind, or 80 ticks (4 s) without "headway", Tavar is put down near the player (`src/Domain/Companions/Companions.cs:85-86`; `content/config/companion.yaml:10-11`). "Headway" is displacement of at least 30% of expected travel, **not progress towards the goal** (`src/World/Runtime/Companions.cs:357-360`), so sliding along a wall counts as headway (INFERENCE).
8. **Determinism is a hard, tested property:** a command log replays to the same `StateDigest` (`tests/Application.Tests/DeterminismAndViewTests.cs:49-78`), and a saved-then-loaded world "goes on the same" (digest equal after 200 more ticks: `tests/Application.Tests/CompanionTests.cs:285-319`). Companion trail, stuck counter, creature mind and body are **persisted** so the loaded world continues identically. A nav cache must be either persisted or a pure function of persisted state (INFERENCE).
9. **Owner rulings 1 (headless deterministic nav) and 2 (one storey) are not written anywhere in the repo at `e10d2c4`, nor in the V4 handoff** (searched: "Godot Navigation", "NavigationServer", "storey", "stair", "upper floor"). Several tracked docs describe a "Recast-style navmesh" in the streaming/presentation table (`docs/WORLD_ARCHITECTURE.md:435`), and the M3 spike bakes a **Godot** navmesh (`src/Presentation/Spike/SpikeScene.cs:159-185`) - see §13.
10. **Body sizes:** player/companion/NPC radius 350 mm; creatures 350-550 mm. Doorways and the smithy fence gap are 1.6 m. So a centre-line corridor through a door is 0.9 m for a person and 0.5 m for the boar (INFERENCE from the numbers in §4-5).

---

## 1. Geometry model: blockers

### 1.1 Types (FACT, `src/Domain/Spatial/Blockers.cs`)

```csharp
public abstract record Blocker(string Id, long HeightMm)          // :11
{
    public long ClearanceMm { get; init; }                           // :14  "0 for one that stands on it, more for an overhang"
    public abstract (double Dx, double Dz)? Separation(double x, double z, double radius);   // :20
    public abstract double DistanceTo(double x, double z);           // :23  0 inside
    public abstract bool Crosses(double x0, double z0, double x1, double z1);   // :26  segment test ("a blow or an arrow stops here")
}
public static class Footprints { public static (long XMm, long ZMm) Center(Blocker blocker) }   // :29-38 box middle / circle centre
public sealed record BoxBlocker(string Id, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, long HeightMm) : Blocker   // :41
public sealed record CircleBlocker(string Id, long CenterXMm, long CenterZMm, long RadiusMm, long HeightMm) : Blocker    // :95
```

- Header comment: "Something a body cannot walk through, as a footprint on the XZ plane, standing HeightMm tall. Movement is planar - no climbing, nothing stood on - but since the owner's M6 playtest a jump passes over a structure lower than the feet, and a crouched body passes under an overhang (ClearanceMm) higher than its head" (`src/Domain/Spatial/Blockers.cs:6-10`).
- Only two concrete shapes exist; `Footprints.Center` throws on any other (`src/Domain/Spatial/Blockers.cs:36`), as does presentation's greybox builder (`src/Presentation/Greybox/HollowView.cs:198`). Adding an OBB/polygon shape touches: `Blockers.cs`, `Footprints.Center`, `WorldContent.Blocker` parser (`src/Content/WorldContent.cs:347-372`), `HollowView` (greybox mesh + Godot collider), `Art/Fitting.Structure` (`src/Presentation/Art/Fitting.cs:22-40`), and every switch/barrier cell lookup.
- Heights are **relative to the local ground**: no base Y is stored. Presentation stands a structure on the lowest terrain under it (`LowestUnder`, `src/Presentation/Greybox/HollowView.cs:211`). `Blocks()` compares jump lift (above ground at the body) with `HeightMm`, so on a slope the "height" is local (INFERENCE).

### 1.2 Coordinate system and numeric types (FACT)

- XZ is the ground plane, Y up; 1 unit = 1 m in docs (`docs/WORLD_ARCHITECTURE.md:40-45`), but the domain stores **integer millimetres** (`Body(long XMm, long YMm, long ZMm, int FacingMdeg)`, `src/Domain/Spatial/Kinematics.cs:34`). Content is authored in metres and converted with `Math.Round(metres * 1000, AwayFromZero)` (`src/Content/WorldContent.cs:456-462`).
- Facing: millidegrees in `[0, 360000)`, "measured from +Z towards +X" (`src/Domain/Spatial/Kinematics.cs:31-34`); `CombatRules.FacingTowards` = `Atan2(dx, dz)` rounded to mdeg (`src/Domain/Combat/Combat.cs:238-243`). Content comment: "0 = +Z, clockwise towards +X" (`content/regions/ashen_hollow.yaml:11`).
- Blocker fields are `long` mm; geometry queries take `double` coordinates and return `double`. Separation/segment math is in `double`.

### 1.3 The math (FACT)

- **Box `Separation`** (`src/Domain/Spatial/Blockers.cs:46-67`): closest point by clamping; if the distance² < r², push out along the normal by `r - d`. If the centre is **inside** the box, leave by the nearest face (ties resolved left, right, back, front in that order): push = face distance + r.
- **Box `DistanceTo`**: distance to the clamped point, 0 inside (`:69-74`).
- **Box `Crosses`**: slab test on the segment's parameter range (`:77-91`). Touching counts as crossing (`<=`).
- **Circle `Separation`** (`:97-109`): push along centre-to-centre by `(r + R) - d`; exactly concentric pushes +X by `r + R`.
- **Circle `Crosses`**: closest point on the segment, `<= R²` (`:117-124`).
- None of `Separation`, `DistanceTo`, `Crosses` looks at `HeightMm`/`ClearanceMm`. Height is applied only by `Kinematics.Blocks` (§2.3).

### 1.4 Where blockers come from (FACT)

| Kind | Stored as | Built from | Blocks movement | Blocks sight/blows/shots (`Crosses`) |
|---|---|---|---|---|
| Structure (64 in content) | `WalkSpace.Blockers` (`ImmutableArray<Blocker>`) | `structures:` in region YAML (`content/regions/ashen_hollow.yaml:64-140`) via `WorldContent.Blocker` (`src/Content/WorldContent.cs:146-148, 347-372`) | yes, height/clearance aware | yes, height-blind |
| Door (2) | `DoorSite(string Key, string FlagId, BoxBlocker ClosedFootprint)` (`src/Domain/Spatial/RegionLayout.cs:12`) | `doors:` (`content/regions/ashen_hollow.yaml:142-144`); must be a box (`src/Content/WorldContent.cs:157-158`) | while its flag is 0 | while closed |
| Barrier (1) | `BarrierSite(Key, FlagId, Blocker Footprint, Prompt)` (`src/Domain/Spatial/RegionLayout.cs:28`) | `barriers:` (`content/regions/ashen_hollow.yaml:194-199`) | while its flag is 0 ("standing") | while standing |
| Switch body (4) | `SwitchSite.Body` = one of the **structures** (`src/Domain/Spatial/RegionLayout.cs:20`; `src/Content/WorldContent.cs:250-251`) | `switches:` names a structure id | always (it is a structure) | always |
| Player body | `CircleBlocker("player", x, z, BodyRadiusMm, 0)` built per query | runtime | for creatures and companions | no |
| Creature body | `CircleBlocker(creature.Key, x, z, Definition.RadiusMm, 0)` | runtime | see per-mover table §7.9 | no |
| NPC body | `CircleBlocker(npc.Definition.Id, x, z, Movement.BodyRadiusMm, 0)` | runtime | see §7.9 | no |

Dynamic bodies are built fresh on every call with `HeightMm = 0`, which `Kinematics.Blocks` treats as "unknown height: always blocks, never jumped" (`src/Domain/Spatial/Kinematics.cs:161-167`).

Ids: structure ids are plain snake_case strings unique within the region (`longhouse_north`, `tree_17`), checked for duplicates (`src/Content/WorldContent.cs:148`); door/switch/barrier keys must start with `door.` / `switch.` / `barrier.` (`src/Content/WorldContent.cs:159, 256, 278`). Dynamic blockers use the creature key (`spawn.hollow.den_pack#0`), the NPC definition id, or `"player"`. No ULIDs appear in the blocker set (INFERENCE: player-built pieces would be the first ULID-keyed blockers).

### 1.5 Content lint that constrains geometry (FACT, `src/Content/WorldContent.cs`)

- `WLD002`: bounds min < max; bounds must lie inside the region's listed cells (`Covered`, 1 mm-inset corners per cell-sized tile, `:386-400`); structure ids unique.
- `WLD003`: terrain grid must cover the walkable bounds (`:143-144`).
- `WLD004`: door keys/flags; two doors cannot share a flag (`:159-164`).
- `WLD007`: the spawn must be `IsClear` with **every door shut and every barrier standing** (`:304-308`).
- `WLD012`: each NPC must stand `IsClear` of structures and doors - barriers treated as lifted, "An NPC may stand behind a barrier - that is what the fold is for" (`:304-311`).
- `WLD013`: a switch stands on an existing structure; its required flags must be set by a switch in the **same cell** (`:245-270`).
- `WLD014`: a barrier must be lifted by a switch in its own cell - "a door is toggled by hand and would lift it both ways" (`:280-282`).
- Clearance: `clearance_m` must be `>= 0` and, if positive, `< height_m` (`:352-354`).
- Nothing lints overlap between structures, reachability of places, or door approachability (INFERENCE: a nav build could add "every place/NPC/node/station reachable from the spawn" as a lint).

---

## 2. `Kinematics.Step` - the exact algorithm

### 2.1 Types (FACT, `src/Domain/Spatial/Kinematics.cs`)

- `enum Gait { Walk, Run, Sprint }` (`:8`); `enum Stance { Standing, Crouched }` (`:15`).
- `readonly record struct Posture(Stance Stance, bool Airborne, int AirMs)`; `Posture.Grounded => default` (`:25-28`).
- `record Body(long XMm, long YMm, long ZMm, int FacingMdeg)` (`:34`).
- `readonly record struct MoveIntent(int DirXPermille, int DirZPermille, Gait Gait, int FacingMdeg)` (`:41`); `FullDeflection = 1000`, `FullTurnMdeg = 360_000`; `Idle(facing)` = zero direction, `Gait.Run` (`:46`). `Problem()` **rejects** (never clamps) a component beyond ±1000, a facing outside `[0, 360000)`, or an unknown gait (`:51-59`). The vector itself may be longer than 1000 (e.g. (1000,1000)); it is clamped to full speed in `Step`.
- `record MovementRules(long BaseSpeedMmPerSecond, int WalkPercent, int SprintPercent, long BodyRadiusMm, long InteractReachMm)` (`:62`) with init-only defaults `JumpApexMm = 1_150`, `JumpRiseMs = 420`, `JumpTuckRadiusMm = 200`, `StandHeightMm = 1_800`, `CrouchHeightMm = 1_150`, `CrouchPercent = 50` (`:65-81`); `AirtimeMs => 2 * JumpRiseMs` (`:84`); `SpeedMmPerSecond(gait)` = walk% / 100% / sprint% of base (`:86-91`); `SpeedMmPerSecond(gait, stance)`: crouched = `CrouchPercent` of base whatever the gait (`:93-94`); `HeightMm(stance)` (`:96`); `LiftMm(airMs) = JumpApexMm * airMs * (AirtimeMs - airMs) / (JumpRiseMs²)` in `long`, 0 outside `(0, AirtimeMs)` (`:99-100`).
- `record WalkSpace(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, TerrainGrid Terrain, ImmutableArray<Blocker> Blockers)` (`:104`).

### 2.2 Signatures (FACT)

```csharp
public static Body Step(Body body, MoveIntent intent, MovementRules rules, WalkSpace space,
    IReadOnlyList<Blocker> dynamicBlockers, int dtMs)                                     // :119  (grounded posture)
public static (Body Body, Posture Posture) Step(Body body, Posture posture, MoveIntent intent, MovementRules rules,
    WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers, int dtMs)                    // :129
public static bool Blocks(Blocker blocker, long liftMm, long bodyHeightMm)                // :166
public static bool IsClear(long xMm, long zMm, long radiusMm, WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers)   // :170
public static bool CanStand(Body body, MovementRules rules, WalkSpace space)              // :176
```

### 2.3 Algorithm, step by step (FACT, `src/Domain/Spatial/Kinematics.cs:129-214`)

1. `x, z` = body position as `double`; `radius = rules.BodyRadiusMm`; `height = rules.HeightMm(posture.Stance)`.
2. Air time: `airFrom = posture.Airborne ? posture.AirMs : 0`; `airTo = min(airFrom + dtMs, AirtimeMs)` if airborne.
3. `length = sqrt(dx² + dz²)` of the intent's permille vector. If `length > 0 && dtMs > 0`:
   - `deflection = min(length, 1000) / 1000`; `travel = speed(gait, stance) * dtMs / 1000 * deflection`; the step vector is the unit direction times `travel`.
   - **Sub-steps:** `steps = max(1, ceil(travel / (radius / 2)))` - "Sub-steps no longer than half the body radius, so a fast step cannot tunnel through a thin wall" (`:144-145`).
   - Per sub-step `s`: `lift = LiftMm(airFrom + (airTo - airFrom) * (s + 1) / steps)` (integer division), then `Resolve(x + dx/steps, z + dz/steps, ...)`.
4. Else if airborne (no input): one `Resolve` in place at `LiftMm(airTo)` (a body landing on a structure is pushed off it).
5. **`Resolve`** (`:183-208`): up to **4 passes**; each pass iterates **all static blockers in content order**, then **all dynamic blockers in list order**; for each blocker with `Blocks(blocker, lift, height)` it applies `Separation` immediately (Gauss-Seidel style). Static blockers use the **tucked radius** `min(radius, JumpTuckRadiusMm)` while airborne if `blocker.HeightMm <= JumpApexMm`; dynamic blockers always use the full radius. Then clamp to `[Min + radius, Max - radius]` on both axes. Stop early when a pass moved nothing.
6. Posture: still airborne while `airTo < AirtimeMs`, else grounded with `AirMs = 0` (`:156`).
7. Quantise: `qx, qz = Math.Round(x/z, MidpointRounding.AwayFromZero)`; `Y = Terrain.HeightAtMm(qx, qz) + LiftMm(next.AirMs)`; facing = `intent.FacingMdeg` (the player turns instantly; creatures/NPCs apply their own turn rates before building the intent) (`:157-159`).

**`Blocks(blocker, lift, bodyHeight)`** = `blocker.HeightMm <= 0 || (lift < blocker.HeightMm && lift + bodyHeight > blocker.ClearanceMm)` (`:166-167`): "all of one whose height is unknown (0: a creature, a person); a structure only where its span, from its clearance to its top, meets the body's".

**`IsClear`** checks bounds with the radius and `Separation is null` for **all** static and given dynamic blockers - it **ignores height and clearance** (`:170-173`). **`CanStand`**: no overhang (`ClearanceMm > 0`) that blocks a standing body overlaps the body (`:176-177`).

Doc comment on determinism: "Floating point only in sums, products, quotients and square roots, which IEEE 754 rounds exactly, then quantised to whole millimetres: the result is the same on every machine" (`:115-118`).

### 2.4 Slopes, step heights, vertical (FACT)

- No slope test, no step height, no ledge, no fall damage: `Y` is simply the terrain height at the quantised point (`:159`). M6 delta: "The jump has no fall damage, ledges or mantling, and nothing is stood on" (`docs/M6_STATUS.md:181`). `PROTOTYPE.md`: "No climb, no jump-required geometry. Jump exists but is never load-bearing" (`docs/PROTOTYPE.md:67`), restated after the playtest: "every place is reached without either" (`docs/PROTOTYPE.md:74`).
- The content's steepest terrain (quarry walls, ~43°, §3) is therefore walkable at full speed (INFERENCE from the formula and the heights).
- M3 decision: "Movement is planar; height comes from the terrain; jump is cosmetic... A vertical domain arrives when something needs it" (`docs/M3_STATUS.md:46`) - the "jump is cosmetic" half is **superseded** by the M6 playtest delta (`docs/M6_STATUS.md:154`).

### 2.5 Jump and crouch numbers (FACT unless marked)

- Content: `jump_apex_m: 1.15`, `jump_rise_s: 0.42`, `jump_tuck_radius_m: 0.2`, `stand_height_m: 1.8`, `crouch_height_m: 1.15`, `crouch_percent: 50` (`content/config/base_speeds.yaml:15-20`). Lint: tuck radius `<= BodyRadiusMm`, crouch < stand (`src/Content/WorldContent.cs:78-81`).
- Air time 840 ms = 17 ticks to land (INFERENCE: `airTo` grows 50/tick and lands when it reaches 840).
- Over the 0.8 m fallen timber the feet are above 0.8 m for `airMs` in roughly [188, 652] (INFERENCE, solving `LiftMm >= 800`). The test finds a contiguous take-off window (`tests/Application.Tests/JumpAndCrouchTests.cs:53-67`); M6 records take-off ticks 14-38 of a 4 m run-up (`docs/M6_STATUS.md:154`).
- Jump refused while airborne, mid-action, guarding or defeated, and from a crouch with no room to stand (`src/World/Runtime/Systems.cs:129-147`). Crouch refused in the air, and standing refused under an overhang (`:150-165`). Crouched intents are forced to `Gait.Walk` for noise/stamina (`MovementSystem.Effective`, `:126`).
- **Only the player jumps or crouches.** Creatures and companions always call the 7-argument grounded `Step`, whose `MovementRules` carry the default `StandHeightMm = 1800` (creatures build `new MovementRules(speed, 50, 100, radius, 0)`: `src/World/Runtime/Creatures.cs:829`). So every creature, including the spider, is treated as 1.8 m tall and cannot pass under the 1.3 m Woundmoss beam (INFERENCE).

---

## 3. Terrain (FACT unless marked)

- `TerrainGrid(long originXMm, long originZMm, long spacingMm, int columns, int rows, IEnumerable<long> heightsMm)`; heights row-major, point (column i along X, row j along Z) is `HeightsMm[j * Columns + i]`; at least 2 x 2 points (`src/Domain/Spatial/TerrainGrid.cs:14-43`).
- `HeightAtMm(x, z)`: clamp into the grid (outside takes the edge height), pick the quad, split along its (0,0)-(1,1) diagonal, interpolate linearly on the triangle, **integer arithmetic** with a symmetric round-half-away division (`src/Domain/Spatial/TerrainGrid.cs:51-67`). "a render mesh built from the same grid points with the same split is exactly this surface" (`:9-12`).
- Content: origin (0, 0), `spacing_m: 5`, 41 x 41 points (1,681 heights) covering x, z in [0, 200] m (`content/regions/ashen_hollow.yaml:15-62`). Test asserts `(41, 41, 5_000)` and a height range of 6-11 m (`tests/Content.Tests/WorldContentTests.cs:52, 57`).
- Heights: min 1.66 m, max 9.60 m (INFERENCE: my scan of the YAML). `docs/M6_STATUS.md:21`: "falling from 9.6 m at the north-west road ridge to the quarry floor's 1.7 m, so about 8 m of relief".
- The waystation sits on a level terrace at 7.20 m (`docs/M6_STATUS.md:16`), about x 35-80, z 120-165 (INFERENCE from the grid rows).
- Steepest adjacent-point gradient: 0.944 (43.3°) at x 50→55, z 80 (the quarry wall); 72 of 4,880 grid edges are steeper than 0.5 (26.6°) (INFERENCE: my scan). All of them are walkable today (§2.4).
- **The movement terrain is authored, not generated.** The generator's per-cell "terrain" is only a hash of `RngChannel` samples from `generation.terrain` (`base_height_mm: 0, amplitude_mm: 0, samples_per_axis: 2`, `content/regions/ashen_hollow.yaml:14`; `src/World/Generation.cs:226-235`). The authored grid is not part of any cell's `baseline_hash` (INFERENCE from `CellBaseline.Digest`, `src/World/Generation.cs:143-162`, which covers terrain hash, nodes and populations only). Structures are not in the baseline either.
- Resolution vs bodies: 5 m terrain spacing is far coarser than a 0.35 m body radius and 1.6 m doorways, so a nav representation cannot reuse the terrain grid as its cell size (INFERENCE).

---

## 4. Body dimensions and speeds

### 4.1 Player (FACT)

`content/config/base_speeds.yaml:7-20`; asserted as `new MovementRules(3_200, 50, 160, 350, 1_600)` (`tests/Content.Tests/WorldContentTests.cs:58`).

| Quantity | Value | Per 50 ms tick |
|---|---|---|
| Base (run) speed | 3.2 m/s ("about 90 s to walk the 283 m diagonal", `content/config/base_speeds.yaml:7`) | 160 mm |
| Walk | 50% = 1.6 m/s | 80 mm |
| Sprint | 160% = 5.12 m/s (spends stamina; empty pool runs) | 256 mm (2 sub-steps) |
| Crouched | 50% of base whatever the gait | 80 mm |
| Dodge | 2.5 m over `DodgeTicks` = `iframes_s` 0.25 s = 5 ticks (`content/config/damage_constants.yaml:18`; `src/Content/CombatContent.cs:170-172`) → 10 m/s | 500 mm (3 sub-steps) (INFERENCE) |
| Body radius | 350 mm | |
| Interact reach | 1,600 mm, measured from the body | |
| Talk reach | inventory reach + body radius = 1.95 m (`src/World/Runtime/Systems.cs:67`; `docs/M6_STATUS.md:41`) | |
| Height | stand 1,800 / crouch 1,150 mm | |
| Jump | apex 1,150 mm, rise 420 ms, tuck radius 200 mm | |

`MovementSystem.Tick` modifies the intent for combat: defeated/staggered/dodge recovery → idle; dodge phase → the dodge direction at dodge speed; windup/active/recovery → walk (facing held after windup); guarding → walk; sprint with 0 stamina → run (`src/World/Runtime/Systems.cs:182-213`).

### 4.2 Companion (Tavar) and NPCs (FACT)

- NPC definitions carry **no** size or speed (`content/npcs/ashen_hollow/tavar_orr.yaml`; no `radius`/`speed` field in any NPC file). All NPC bodies use the player's `BodyRadiusMm` 350 mm as blockers (`src/World/Runtime/Systems.cs:86`; `src/World/Runtime/Companions.cs:579`).
- The companion moves with the **player's** `MovementRules` (`_context.Setup.Movement`, `src/World/Runtime/Companions.cs:567`): walk 1.6, run 3.2, sprint 5.12 m/s, radius 350, height 1.8 m, never jumps or crouches, no stamina. NPC turn rate 360°/s = 18,000 mdeg/tick (`src/World/Runtime/Social.cs:107`; `src/World/Runtime/Companions.cs:601-609`).
- Static NPCs never translate: `NpcSystem.Tick` only turns them towards a talking player or back to their site facing (`src/World/Runtime/Social.cs:149-164`). "Phase 1 has no schedules... and no simulation tiers for NPCs" (`src/World/Runtime/Social.cs:99-103`).

### 4.3 Creatures (FACT: `content/creatures/**`, `content/abilities/creature/*.yaml`; table `docs/M3D_BEHAVIOUR_MATRIX.md`)

Default turn rate 720°/s when `turn_deg_s` is absent (`src/Domain/Combat/Combat.cs:274`; `src/Content/CombatContent.cs:280`). Creature `MovementRules(speed, 50, 100, radius, 0)`: Walk = 50% of `move_speed`, Run = Sprint = 100% (`src/World/Runtime/Creatures.cs:829`). Height for movement = 1.8 m default (INFERENCE, §2.5).

| Creature | Radius | Speed (run) | Walk | Turn °/s | Sight / FOV / hearing | Blow reach (+ lunge) | Notes |
|---|---|---|---|---|---|---|---|
| `creature.beast.wolf_grey` | 0.45 m | 4.5 m/s | 2.25 | 720 | 25 m / 140° / 30 m | 1.4 m | test-asserted (`tests/Content.Tests/CombatContentTests.cs:67`) |
| `creature.beast.ash_ember_hound` | 0.40 | 6.0 | 3.0 | 720 | 30 / 160 / 35 | 1.2 + lunge 2.2, `advance: true` | "cannot be outrun" |
| `creature.beast.bristleback_boar` | 0.55 | 4.0 | 2.0 | 240 | 20 / 150 / 25 | gore 1.3; **charge** 9 m/s, 5-14 m, stun 2 s, cooldown 6 s | charge = straight line (`content/abilities/creature/boar_charge.yaml`) |
| `creature.beast.cave_hunting_spider` | 0.50 | 5.5 | 2.75 | 720 | 6 / 360 / 14 | 1.2 | |
| `creature.construct.animated_armour` | 0.45 | 1.8 | 0.9 | 90 | 15 / 100 / 18 | 1.8 | |
| `creature.undead.bone_walker_husk` | 0.35 | 2.2 | 1.1 | 180 | 18 / 120 / 20 | 2.4 | |

Derived per tick at run (INFERENCE): wolf 225 mm (2 sub-steps), hound 300 mm (2), boar 200 mm (1), charge 450 mm (2 at r 0.55), spider 275 mm (2), armour 90 mm, husk 110 mm.

Content places **13 creatures**: boar 1, hound 1, den pack 4, east pack 2, armour 1, husk 1, spider 1, strays 2 (`content/spawns/hollow/*.yaml`; `docs/M3D_STATUS.md:74` "all 13 creatures placed").

---

## 5. Building-scale dimensions in the current content (FACT: `content/regions/ashen_hollow.yaml`; widths are my arithmetic)

64 structures = 19 boxes + 45 circles (lines 64-140), plus 2 door boxes (142-144) and 1 barrier circle (194-199). `docs/M3_STATUS.md:13` says "44 structures" - that was M3's layout, before M6.

### 5.1 The lodge (Renn's; M3's longhouse "moved whole") - lines 67-72, door line 143

- Walls, all `height_m: 3.2`, 0.4 m thick: north `[36, 131.6, 52, 132]`, south `[36, 124, 52, 124.4]`, west `[36, 124, 36.4, 132]`, east in two parts `[51.6, 128.8, 52, 132]` and `[51.6, 124, 52, 127.2]`.
- Outside 16 m (x) x 8 m (z); inside x 36.4-51.6 (15.2 m) by z 124.4-131.6 (7.2 m).
- Doorway on the east wall: z 127.2-128.8 = **1.6 m wide**, filled when closed by `door.longhouse` `[51.6, 127.2, 52, 128.8]`, `height_m: 2.4`, flag `world.hollow.longhouse_door_open`.
- Renn stands inside at (46.5, 126.2) facing 90 (`content/regions/ashen_hollow.yaml:152`): 1.8 m from the inner south wall.
- The waystation chest `container.waystation_chest` is at (38, 137), north of the lodge (`:148`). Containers are points, not blockers.

### 5.2 The smithy (Kera's; M3's forge shed "moved whole") - lines 73-78, door line 144

- Walls `height_m: 3.0`, 0.4 m thick: north `[53, 145.6, 63, 146]`, south `[53, 138, 63, 138.4]`, east `[62.6, 138, 63, 146]`, west in two parts `[53, 142.8, 53.4, 146]` and `[53, 138, 53.4, 141.2]`.
- Outside 10 m x 8 m; inside x 53.4-62.6 (9.2 m) by z 138.4-145.6 (7.2 m).
- Doorway on the west wall: z 141.2-142.8 = **1.6 m**, `door.forge_shed` `[53, 141.2, 53.4, 142.8]`, `height_m: 2.4`, flag `world.hollow.forge_shed_door_open`.
- Stations inside (points, not blockers): `station.forge_hearth` (60.5, 143.5), `station.forge_anvil` (57.5, 140.5) (`:162-163`). Kera at (61.6, 139.6) facing 300 (`:154`).

### 5.3 Other settlement pieces

- `fence_smithy` `[44, 146, 51.4, 146.4]`, `height_m: 1.2` - "leaving a narrow gap at its north-west corner (bible §26's camera test)" (`:79-80`). Gap between the fence's east end (x 51.4) and the smithy's west wall (x 53) = **1.6 m**.
- `rock_well` circle (49, 151) r 1.0, h 1.0 (`:81`). `rock_waystone` circle (27, 158) r 0.8, h 2.4 (`:66`); spawn beside it at (30, 158) facing 90 (`:12`).
- `survey_table` box `[73.4, 120.4, 75.2, 122.2]` (1.8 x 1.8 m), h 0.9 (`:83`); Sel at (72, 122) (`:153`).
- Bible settlement footprint "X 30-85, Z 115-170"; road widths "main road: 3-4 m; settlement paths: 1.5-2.5 m; forest trails: 1-1.5 m" (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:120-160`). There is **no road/path data** in the domain or content (INFERENCE: the road spline at bible lines 99-117 exists only as prose).

### 5.4 Wild obstacles

- `cart_wreck` `[155.5, 161.2, 158.5, 163.2]` h 1.4; `fallen_timber` `[163, 147, 171, 147.8]` h 0.8 (the one jumpable obstacle) (`:85-86`).
- Overhang: `beam_woundmoss` `[146, 131.6, 152, 132.4]` h 1.7, `clearance_m: 1.3`, between `rock_beam_west` (145.3, 132) r 0.7 h 1.8 and `rock_beam_east` (152.7, 132) r 0.7 h 1.8 (`:87-91`). A crouched body (1.15 m) passes under it; a standing one goes round.
- Wolves' den, a notch in rock: `den_rock_west` `[93, 180, 109, 198]` h 6; `den_rock_east` `[115, 180, 131, 196]` h 5; `den_rock_back` `[109, 190, 115, 198]` h 6 (`:94-96`). The notch is x 109-115 (6 m) by z 180-190, open to the south. `den_rock_west` straddles the x = 100 m cell seam.
- Blackvein: `rock_iron_seam` (28, 46.2) r 1.2; `rock_blocked_shaft` `[73, 16, 79, 21]` h 4; four rim rocks r 1.8-2.2 (`:98-103`).
- Foldscar: three Quiet Stones r 0.9 h 2.8 at (150, 78), (122, 38), (181, 31); heart (153, 48) r 1.5 h 3.5 (`:105-108`), all four are switch bodies (`:166-192`).
- 32 trees, each circle r 0.4 h 7.0 (`:109-140`). Presentation gives them no canopy collider ("Foliage: scenery only", `src/Presentation/Greybox/HollowView.cs:187`).
- The fold: `barrier.foldscar_fold` circle (145, 42) r 3, h 2.6, lifted by `world.foldscar.steadied` (`:194-199`). **Tavar's NPC site is its exact centre** (145, 42) (`:155`).

### 5.5 Narrowest static gaps (INFERENCE: pairwise scan of all 67 footprints)

| Gap | Between | Consequence for a 0.35 m body |
|---|---|---|
| 0.20 m | `rock_beam_west` and `tree_10` (144, 132) | sealed: the beam line runs continuously from x 143.6 to 153.4 |
| 1.60 m | lodge east doorway; smithy west doorway | 0.9 m centre corridor when open |
| 1.60 m | `fence_smithy` to smithy north-west corner | 0.9 m corridor (the camera-test gap) |

Walls meet at their corners with zero gap, so both buildings are sealed except at their doors.

### 5.6 Art kit vs collision footprints (FACT, contradiction)

`docs/ASSET_MATERIAL_PASS_2026-09-24.md:61-64`: the modular art kit declares `forge_shed` **6.0 x 6.0 m** and `longhouse` **9.0 x 6.0 m** ("modular wall run on whole 3 m modules"), built envelope 6.0/9.0 x 7.102 x 4.729 m. The collision footprints are **10 x 8 m** and **16 x 8 m** (§5.1-5.2). Presentation never rescales art to a collider. Where the model would leave "more than half a metre of invisible wall at a side", **the greybox stands instead** and the mismatch is reported (`src/Presentation/Art/Fitting.cs:9-14`). Owner ruling in that doc: the bible fixes the placements, "not the 6 x 6 / 9 x 6 assembled exterior bounds" (`docs/ASSET_MATERIAL_PASS_2026-09-24.md:50-55`). INFERENCE: M7's building-piece dimensions (a 3 m module grid?) have no agreed relationship yet to the 0.4 m-thick, 1.6 m-door greybox walls. Presentation also infers "a building" only from structure id prefixes `longhouse_` / `forge_` (roofs: `src/Presentation/Greybox/HollowView.cs:203-216`; indoor footsteps: `src/Presentation/Art/ArtBindings.cs:93`). The domain has no building concept.

---

## 6. Cells, regions, seams, tiers (FACT)

- Constants: `RegionSizeMeters = 2000`, `CellSizeMeters = 100`, `CellsPerRegionAxis = 20` (`src/World/Coordinates.cs:15-18`). Keys: region `r_<rx>_<rz>` (negatives spelled `neg<abs>`), cell `<region>:c_<cx:00>_<cz:00>` (`src/World/Coordinates.cs:33, 98`). `CellKey.OfWorld(double xMeters, double zMeters)` uses floor division and a Euclidean `FloorMod` (`src/World/Coordinates.cs:93-96`); callers pass `mm / 1000.0`.
- Region `region.ashen_hollow`, `region_key: r_0_0`, cells `[r_0_0:c_00_00, r_0_0:c_00_01, r_0_0:c_01_00, r_0_0:c_01_01]`, bounds `min [0, 0] max [200, 200]` ("Movement stops at the edge... the ravine beyond is scenery") (`content/regions/ashen_hollow.yaml:7-10`).
- The four bible cells (`docs/M6_STATUS.md:14-19`; `content/regions/ashen_hollow.yaml:6`):
  - A `c_00_01` (x 0-100, z 100-200): Ashen Hollow Waystation.
  - B `c_01_01` (x 100-200, z 100-200): Charwood Verge (hound, cart, ash stand, Woundmoss beam, den, east pack, strays).
  - C `c_00_00` (x 0-100, z 0-100): Blackvein Cut quarry (husk patrol, armour, boar, iron seam, shaft).
  - D `c_01_00` (x 100-200, z 0-100): Foldscar Ruin (stones, heart, spider, fold, Tavar).
- Seams are the lines x = 100 m and z = 100 m. Movement ignores them (one `WalkSpace`). What is per-cell: `world.*` flags (the cell of the footprint's centre: `Simulation.CellOf`, `src/World/Runtime/Simulation.cs:170-183`); tiers; creature record host cell (the spawner's cell, `src/World/Runtime/Creatures.cs:802`); RNG channels.
- Flag cells: both door flags in A (`c_00_01`); the four Foldscar flags and the barrier in D (`c_01_00`) (INFERENCE from the footprint centres).
- Tiers: `TierRules(FullRadiusMm, RegionalRadiusMm, AbstractRadiusMm, HysteresisMm)` = 150 / 600 / 2000 m, 10 m hysteresis (`content/config/simulation_tiers.yaml:7-10`; `src/Domain/Spatial/Tiers.cs:20`); stepwise, one step per tick, never A→D (`src/Domain/Spatial/Tiers.cs:27-31`). Distance is from the player to the nearest point of the cell square (`src/World/Runtime/Systems.cs:496-505`). "In the hollow every cell is always tier A" (`docs/M3_STATUS.md:50`). Creatures and companions act only if their body's cell is tier A (`src/World/Runtime/Creatures.cs:264-265`; `src/World/Runtime/Companions.cs:263-264`). Tier B/C are `StubTierSimulation` no-ops; the comment says "B: coarse movement on routes" (`src/World/Runtime/Systems.cs:521-530`).
- Tier-B design intent: "Coarse movement along authored/derived routes... 2 Hz" (`docs/WORLD_ARCHITECTURE.md:235`). Tier A lists "pathfinding" among what runs (`docs/WORLD_ARCHITECTURE.md:234`) - not built.

---

## 7. Every mover: how it chooses where to go, and what happens when blocked

Tick order (FACT, `src/World/Runtime/Simulation.cs:312-333`): **movement (player)** → tiers → tier simulations → player combat → **creatures** → **companions** → **NPCs** → dialogue → effects → death → discovery → quests → clock. Commands are drained at tick boundaries before `Step` (`src/World/Runtime/Simulation.cs:268-305`). So creatures see the player's new position this tick, companions see creatures' new positions, and NPC turning happens last (INFERENCE from the order).

### 7.1 Player (FACT)

- Presentation turns camera-relative stick input into a world-space `MoveIntent` and submits `MoveCommand` only when the wish changes (direction, gait, or facing by more than 0.5°) (`src/Presentation/Player/PlayerController.cs:76-96`). Facing follows the camera in first person or while fighting, else the walk direction.
- `MovementSystem.Handle(MoveCommand)` validates `Problem()` and stores the intent; the intent persists until replaced and is **not saved** (`src/World/Runtime/Systems.cs:104-108, 167-175`).
- `MovementSystem.Tick` → `Kinematics.Step(from, posture, intent, rules, Layout.Space, _context.Obstacles(), TickMs)`; publishes `BodyMoved(Actor, From, To, Tick)` when the body changed (`src/World/Runtime/Systems.cs:215-222`; `src/World/Runtime/Events.cs:15`).
- Blocked: slides; nothing else. On death: `Relocate` to the region spawn (`src/World/Runtime/Combat.cs:976-978`; `src/World/Runtime/Systems.cs:225-233`).
- **Presentation prediction:** `PlayerController.Predict(alpha)` = `Kinematics.Step(_body, simulation.Posture, intent, setup.Movement, setup.Layout.Space, simulation.DynamicBlockers, round(alpha * TickMs))` from the last authoritative body (`src/Presentation/Player/PlayerController.cs:103-129`). `Simulation.DynamicBlockers => _context.Obstacles()` ("Prediction needs them", `src/World/Runtime/Simulation.cs:254-255`). A direction change mid-tick "can pop the drawn body forward by up to a tick's travel (16 cm at a run)" (`docs/M3_STATUS.md:51`). Creatures are drawn by lerping between the previous and current tick (`src/Presentation/Greybox/CreaturesView.cs:282`).

### 7.2 Creatures - movement primitive (FACT)

`CreatureSystem.Move(c, toX, toZ, gait)` (`src/World/Runtime/Creatures.cs:820-838`): unit vector to the target as permille; facing = `Turn(c, FacingTowards(...))` (turn-rate limited, `:876-884`); rules `new MovementRules(MoveSpeed, 50, 100, Radius, 0)`; obstacles = closed doors/standing barriers + the player (r 350) + companions who are up (r 350) + every other living creature (its radius); **not** non-companion NPCs. One `Kinematics.Step` of one tick. There is no stuck detection and no memory of being blocked. `Arrived` = within **700 mm** (`:869`).

### 7.3 Creatures - decision logic (FACT, `src/World/Runtime/Creatures.cs`)

- Mind states: `Unaware, Suspicious, Engaged, Searching, Returning, Fleeing` (via `Decide`, `:324-371`). Perception each tick: sight = range + FOV + no `Crosses` with static blockers or closed doors (`Walls()`, `:893`; `src/Domain/Creatures/Perception.cs:87-106`); hearing = noise radius and hearing range, **no occlusion by walls** (`src/Domain/Creatures/Perception.cs:109-114`). Nothing is shared except a same-kind call ("There is no shared awareness", `:124-125`).
- **Leash:** `creature_leash_m: 40` (`content/config/damage_constants.yaml:24`). An engaged creature turns `Returning` when the **player's** distance from the creature's **home** exceeds the leash (`:328, 349-350`) - measured home-to-player, not home-to-creature. At rest or returning it only notices things inside its own ground (territory, else leash) (`Keeps`, `:872-873`).
- **Unaware/Idle** (`:419-439`): `Hold` walks back home if displaced; `Wander` picks a point per **120-tick leg** from `RngChannel(seed, cell, "wander", "{key}@{leg}")`, uniform in a disc of `WanderMm` around home, and walks to it (`:441, 448-457`); `Patrol` walks the spawner's `Route` end to end and back, **one leg per 400 ticks**, the leg index a pure function of `(tick + Offset(key)) / 400` (`:425-433, 442`); `Sleep` holds.
- **Suspicious:** turn towards the known point; if awareness ≥ 60 (heard noise) and the point is in its ground, go there (run if ≥ 80, a call) (`:391-399`).
- **Searching:** one budget of `2 * SearchTicks` = 240 ticks (12 s) for getting there **and** looking around - "a place it cannot reach is given up like one it searched"; then Returning (`:400-408`; `search_s: 6`, `content/config/creature_behaviour.yaml:14`).
- **Returning:** walk straight home; on arrival (≤ 700 mm) back to Unaware (`:409-412`). **No time budget and no stuck handling** (INFERENCE: a creature pinned against a wall between itself and home stays pinned).
- **Engaged** (`Engage`, `:471-540`): target = the last known player position (or the player itself when a companion is the foe; `Foe` picks a companion who is up and nearer by more than `FoeMarginMm = 1000`, `:844-861`). Territory roles past their edge go home at a run or face the intruder (`:482-487`). Wary roles (`keep_distance_m`) back off 3 m straight away from the player (`:489-498`). Charge if it can (§7.4). Strike if within `reach + lunge/2 + player radius - 100` and not walled, after turning to face within 30° (`:518-528`). Otherwise run at the target; **pack hunters flank** by offsetting the goal `FlankMm` (3 m) sideways while more than 3 m away, side chosen by the key's last digit (`:531-539`).
- **Fleeing:** run at a point 5 m directly away from the player (`:385-390`); ends when unseen and more than leash/2 = 20 m away (`:334-338`).
- **Spawn placement** (`Populate`, `:171-225`): up to 16 samples from `RngChannel(seed, cell, "spawn", key)` uniform in the spawner's disc; the first `IsClear` (static blockers + other creatures' homes; **doors and barriers not included**) wins; else the spawner centre. Facing from sample 40. Loaded records override the body.
- **Respawn** at home (not re-sampled) once the timer has passed and the player is more than the leash from home (`:753-766`).

### 7.4 Charge and lunge - movement that is deliberately not steering (FACT)

- **Charge** (`Run`, `:565-596`): the line is fixed in the windup (`Aim`, `:546-559`); each active tick is one `Kinematics.Step` at 9 m/s along that line; obstacles = closed doors + other living creatures only (**not** the player, companions or NPCs). The player is hit by distance (`radius + player radius + 300 mm`). If the step moved less than half the expected distance, the boar has "run into something solid" → `CreatureStunned`, staggered 2 s (`:589-594`). **The stun mechanic is built on "blocked by a structure"**, and test `ADodgedCharge_RunsTheBoarIntoTheRock_AndStunsIt` depends on it (`tests/Application.Tests/CreatureTests.cs:326-352`).
- **Lunge** (`Strike`, `:605-624`): the body is carried forward along its facing through the active window at `LungeMm / active time`; obstacles = closed doors + the player + companions (not other creatures).

### 7.5 Companion (Tavar) - follow (FACT, `src/World/Runtime/Companions.cs`; tuning `content/config/companion.yaml`)

`CompanionTuning` (`src/Domain/Companions/Companions.cs:46-72`) from content: `follow_near_m 2.5`, `run_beyond_m 4`, `sprint_beyond_m 8`, `catch_up_beyond_m 30`, `snag_s 4` (= 80 ticks), `trail_step_m 1`, `trail_marks 48`, `fight_radius_m 10`, `leash_m 16`, `guard_radius_m 4` (`content/config/companion.yaml:7-16`). Lint: the distances must nest, the trail has at least 2 marks (`src/Domain/Companions/Companions.cs:64-71`).

Per tick (`Live`, `:258-295`), if downed wait/fall; if not tier A hold; mend; if following, **Mark**; combat phases; in conversation stand and face; fight a target if any; else **Follow** (or stand if waiting).

- **Mark** (`:308-316`): append the player's position when it is at least `TrailStepMm` (1 m) from the newest mark; keep at most 48 marks (drop the oldest). No mark while the player is defeated. The trail is `ImmutableArray<TrailMark>`, `record TrailMark(long XMm, long ZMm)` (`src/World/PlayerState.cs:44`).
- **Follow** (`:325-361`):
  1. `CompanionRules.CatchUp(distance, StuckTicks)` → `"distance"` if > 30 m, `"snag"` if `StuckTicks >= 80` → **CatchUp** (§7.6).
  2. `CompanionRules.FollowGait(distance)`: ≤ 2.5 m stand (face the player, `StuckTicks = 0`); > 8 m sprint; > 4 m run; else walk (`src/Domain/Companions/Companions.cs:78-82`).
  3. Drop leading marks within `MarkReachedMm = 800` of the body.
  4. Goal = the player, if `InClearView`; else the **newest** mark in clear view (scan newest→oldest); if none, the **oldest** mark ("the way back onto the trail"); drop the marks before the goal.
  5. `InClearView(from, to)` = no `Crosses` (static blockers + closed doors) on three parallel segments: the centre line and the two lines offset by ±body radius (`:584-595`). It ignores bodies and heights.
  6. One `Kinematics.Step` towards the goal (`Step`, `:558-568`) with obstacles = closed doors + living creatures + the player + every other NPC (`Obstacles`, `:571-581`).
  7. `headway = displacement >= 0.3 * expected travel for the gait`; `StuckTicks = headway ? 0 : StuckTicks + 1` (`:357-360`).
- Following is **string-pulling over the player's breadcrumbs** by line of sight: it works when the player has walked a path the companion can also walk. It is not guaranteed when the player jumped (timber) or crouched (beam), because the companion can do neither (INFERENCE; no test covers it). The likely result is sliding, then a snag teleport after 4 s.
- **Order** follow/wait clears the trail and resets `StuckTicks`: "Told to follow, they set off from where they stand: the trail walked while they waited is not theirs to retrace" (`:186-187`). After wait→follow the companion therefore heads straight for the player until new marks appear.

### 7.6 Companion - catch-up, ring, fall (FACT)

- **CatchUp** (`:367-393`): scan the trail newest→oldest for a mark 2-6 m from the player (`CatchUpNearMm`, `CatchUpFarMm`, `:101`) that is `IsClear` (static + obstacles, height-blind) and not walled from the player; else `Ring(player, player.Facing + 180°, 2,500 mm)`: eight points 45° apart starting behind the player, first clear and unwalled (`:396-408`). Nowhere fits → reset `StuckTicks` and try again next time. Publishes `CompanionCaughtUp(NpcId, Reason, From, To, Tick)` (`:28`); trims the trail up to the chosen mark.
- **Fall** after `revive_window_s: 60` downed: `Ring(spawn, spawn.Facing + 90°, 2,000)` or the spawn itself; order becomes Wait (`:514-523`).
- `CompanionCaughtUp` is what ROADMAP M6 calls a "pathing intervention" ("he walks the bible's acceptance route... without a pathing intervention", `docs/ROADMAP.md:273`). Met for the way home from the Foldscar only (`docs/M6_STATUS.md:80, 131`).

### 7.7 Companion - fighting movement (FACT)

Target = the current target while alive, tier A and within 1.5 x radius; else the nearest creature that is `Engaged`, within `FightRadiusMm` (10 m, following) or `GuardRadiusMm` (4 m, waiting) of the companion, within `LeashMm` (16 m) of the player when following, and not walled (`:416-433`). Fight: within `reach + target radius - 100` and unwalled → turn and swing; else following companions `Step` straight at the target at a run, waiting ones only turn (`:436-453`).

### 7.8 Static NPCs and projectiles (FACT)

- Renn, Kera, Sel, and Tavar before recruitment: placed at their `NpcSite` with terrain height (`src/World/Runtime/Social.cs:127-139`); only turn. A companion's body moves through `PlaceNpc` to `NpcSystem` (`src/World/Runtime/Companions.cs:81, 551-555`; `src/World/Runtime/Social.cs:141-147`). Non-companion NPC bodies are not saved; a companion's is saved in the player record (`CompanionRecord`, `src/World/PlayerState.cs:52-62`).
- Projectiles do not move in the simulation: a shot resolves at release along the facing - the first creature whose radius the ray passes within, unless `Walled` first; else the first wall found by bisection to 10 mm; else full range (`Trace`/`RangedTarget`, `src/World/Runtime/Combat.cs:523-567`). Presentation draws the flight afterwards (`docs/M6_STATUS.md:156`).

### 7.9 Who blocks whom (FACT; the asymmetries matter for nav)

| Mover (call site) | Static structures | Closed doors / standing barrier | Player | Companions (up) | Non-companion NPCs | Other creatures |
|---|---|---|---|---|---|---|
| Player (`Systems.cs:82-86`) | yes (height-aware) | yes | - | **no** ("keeps out of the way... a narrow door is never held shut by a friend") | yes | yes |
| Creature `Move` (`Creatures.cs:830-836`) | yes | yes | yes | yes | **no** | yes |
| Boar charge (`Creatures.cs:585-587`) | yes | yes | no (hit test) | **no** | **no** | yes |
| Creature lunge (`Creatures.cs:618-622`) | yes | yes | yes | yes | no | **no** |
| Companion `Step` (`Companions.cs:571-581`) | yes | yes | yes | (other NPCs incl. companions) yes | yes | yes |
| Spawn placement (`Creatures.cs:184, 191`) | yes (height-blind) | **no** | no | no | no | homes of earlier creatures |
| Catch-up / ring (`Companions.cs:379, 404`) | yes (height-blind) | yes | yes | yes | yes | yes |

`docs/SYSTEMS.md:280` (S-24 M4 note) says NPC "bodies block movement", true for the player and companions but not for creatures (INFERENCE: a creature can walk through Renn).

---

## 8. Existing path, waypoint, trail or graph structures

1. **Companion trail** (runtime, persisted): ≤ 48 `TrailMark`s at ≥ 1 m spacing (§7.5). Saved in schema 12 (`docs/M6_STATUS.md:62`) and hashed into the player digest (`src/World/PlayerState.cs:360-363`). The only authoritative "path".
2. **Patrol routes** (content): `route_m: [[x, z], ...]` on a spawner, parsed to `SpawnSite.Route` (`src/Content/CombatContent.cs:371-379`; `src/World/Runtime/Creatures.cs:23-29`). Two routes exist: `spawn.hollow.east_pack` `[[192,165],[194,182],[180,194]]` and `spawn.hollow.iron_shelf_husk` `[[40,52],[54,52],[54,62],[40,62]]`. Legs are straight, not checked for obstruction. The only validation is `EverySpawnersPatrol_StaysOnItsLeash` (every point within 40 m of the spawner) (`tests/Content.Tests/CombatContentTests.cs:50-59`), added after the M6 layout move **stranded two patrols** (`docs/M6_STATUS.md:90, 96`). Routes are "not part of a cell's baseline" (`docs/M6_STATUS.md:96`).
3. **Wander targets**: random points in a disc, no clearness check (§7.3).
4. **Presentation scripted routes** (not authoritative, but acceptance evidence): hand-authored metre waypoints walked by straight `SteerWorld` with 0.5 m arrival (`src/Presentation/Playthrough.cs:47-69, 587-603`), plus `DeltaShots.cs:32-41`, `PerfRun.cs:44-125` (its obstruction route goes through the lodge and opens its door), `UiShots.cs:47`, `Smoke.cs`. They encode today's static layout: `ToTheSmithyDoor = { (40,140), (47,139), (51.8,139), (51.8,142) }`, `ToTheSouthEastStone = { (140,62), (160,65), (187,65), (194,45), (182.5,31) }` (the "route round the spider", `docs/M6_STATUS.md:19`). Any M7 building content in the settlement could invalidate them (INFERENCE).
5. **Test harness routes**: `Harness.WalkTo` (straight line, 300 mm tolerance, 4,000 tick cap) and `WalkPath` (`tests/Application.Tests/Harness.cs:45-67`); the C16 lodge route (§10).
6. **Bible road spline** (prose only): (0,180) → (30,165) → (55,145) → (78,125) → (105,108) → (132,92) → (158,70) → (200,55) (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:99-117`).
7. **Godot navmesh in the M3 spike only**: `SpikeScene` bakes `NavigationRegion3D` tiles (8 x 8 tiles of 250 m, `CellSize 1`, `CellHeight 0.5`, `AgentRadius 1`, `AgentHeight 2`, `AgentMaxClimb 1`, `AgentMaxSlope 40`) over a synthetic 2 km heightmap purely to measure frame cost (`src/Presentation/Spike/SpikeScene.cs:12-26, 159-185`). Result: 64 tiles in 6.2 s, 14,291 polygons; "One 2 km bake overflowed Recast's region IDs" (`docs/M3_STATUS.md:72`). Nothing gameplay-side uses it. The only other Godot physics is the camera `SpringArm3D` against greybox colliders (`src/Presentation/Player/CameraRig.cs:31`; `docs/M3_STATUS.md:33`).

No graph, grid, visibility structure, navmesh, flow field or spatial index exists in Domain, World or Application (FACT: grep for `Navigation|NavMesh|AStar|pathfind` in `src` hits only `SpikeScene.cs`).

---

## 9. Dynamic blockers and how changes propagate (FACT)

- **Doors:** `InteractCommand(Actor, "door.*")` → `InteractionSystem.Handle`: reach measured from the body to the footprint (`DistanceTo <= InteractReachMm`, 1.6 m); refuses to close "something is in the doorway" **only if the player's body overlaps** the closed footprint; dispatches `SetWorldFlag(cell, flag, open ? 0 : 1)`; publishes `DoorToggled` (`src/World/Runtime/Systems.cs:251-273`). `WorldFlagSystem` writes the flag into the sparse delta and publishes `WorldFlagChanged` (`:307-315`).
- **Barriers:** lifted when their flag is set by a switch (`src/Domain/Spatial/RegionLayout.cs:23-28`). The only barrier, the fold, drops when `world.foldscar.steadied` is set by `switch.foldscar_heart`, which requires the three stone flags in the same cell (`content/regions/ashen_hollow.yaml:185-192`). "It blocks movement, blows and sight like a closed door until the heart is steadied" (`docs/M6_STATUS.md:41`).
- **Switches** set flags once and never unset; switch bodies stay solid (they are structures) (`src/Domain/Spatial/RegionLayout.cs:14-21`; `src/World/Runtime/Systems.cs:276-292`).
- **Propagation:** there is no cache. `SystemContext.ClosedDoors()` = `Layout.ClosedDoors(IsOpen, IsLifted)` re-reads the flags on every call and builds a new `ImmutableArray` (`src/World/Runtime/Systems.cs:41-48`; `src/Domain/Spatial/RegionLayout.cs:94-97`). A flag change is visible to the next movement, sight, shot or clear-view query in the same tick. Presentation mirrors door state from `DoorToggled` events (`src/Presentation/Player/PlayerController.cs:67`).
- **Persistence:** door and switch state is the cell delta's `Flags` (`CellDeltaRecord`, `src/World/WorldDelta.cs:18-24`), proven against the cell's `baseline_hash`; closing a door again rebases the record away (`docs/M3_STATUS.md:24`).
- **Moving bodies** (player, creatures, NPCs, companions) are rebuilt as `CircleBlocker`s per call from current state (§1.4).
- **Edge case (INFERENCE):** a door closed on a creature or companion standing in the doorway is not refused; its next `Kinematics.Step` pushes it out of the 0.4 m door box by the nearest face, which may be either side.
- Nothing else is dynamic: no destructible structures, no runtime-created blockers. `CreatedEntityRecord` ("a placed chest", cm positions) exists in the world delta but creates no blocker (`src/World/WorldDelta.cs:48-62`).

---

## 10. Tests that assert movement, collision, following and determinism (FACT)

### Domain (`tests/Domain.Tests/Spatial/SpatialTests.cs`)
- `TerrainGridTests`: grid points exact; the two triangles on either side of the diagonal; edge heights outside the grid; a mismatched height count refused (`:6-35`).
- `KinematicsTests` with a 20 cm wall at x = 10 m: full deflection covers base speed (160 mm in 50 ms); a diagonal is no faster; half deflection is half speed; a wall stops the body at `10,000 - radius`; a glancing move slides; **a sprint cannot tunnel through a thin wall**; the bounds stop the body; a dynamic blocker blocks only while passed in; height from terrain and facing from the intent; the same step gives the same body every time; intent `Problem()` cases (`:37-136`).
- `TierRulesTests`: one step at a time; never A→D; hysteresis band (`:138-162`).
- `tests/Domain.Tests/CompanionRulesTests.cs` (follow gait / catch-up rules); `tests/Domain.Tests/Combat/CombatRuleTests.cs:199-205` (walls and trunks block, via `Crosses`).

### Application (the real content)
- `SessionTests`: move at configured speed on the next tick; gaits scale speed; frame loop runs whole ticks; a long stall is clamped not replayed; **movement stops at the region edge** (`ZMm == BodyRadiusMm`); **the body follows the terrain** (Y = `HeightAtMm`, > 8 m on the north-west ridge) (`tests/Application.Tests/SessionTests.cs:40-127`).
- `InteractionAndDiscoveryTests`: the longhouse door opens within reach and is a world flag in its cell; out of reach is refused (measured from the body); **a closed door blocks and an open one lets the body in**; **a door will not close on the body**; discovery waits for tier A (`tests/Application.Tests/InteractionAndDiscoveryTests.cs:23-138`).
- `JumpAndCrouchTests`: a run-up jump clears the fallen timber over one contiguous window of take-off ticks, and never clears the fence, the cart, a lodge wall, the closed smithy door or the fold; airborne rules; crouched half pace; crouched footfalls are a walk's; **the beam passes a crouched body but not a standing one, with no room to stand or jump under it**; a save mid-jump or crouched under the beam loads and goes on the same (`tests/Application.Tests/JumpAndCrouchTests.cs:53-204`).
- `CompanionTests`:
  - `Tavar_JoinsWhenAsked_AndFollows`: within 4 m after a walk (`:55-84`).
  - `FollowWaitFollow_...`: waits in place, ~18 m apart; called back, reaches within 3 m in < 300 ticks; **no catch-up** ("18 m in the open is walked, not skipped") (`:87-116`).
  - **`ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds` (C16):** the player walks (44,138)→(54.5,134)→(53,128), opens `door.longhouse`, walks in round Renn and out, then round the building [(55,121),(35,121),(34,134),(44,138)], then stands 100 ticks. It asserts `maxHeld < 15 * 20` ticks, where "held" = the companion's position unchanged while more than 3.5 m from the player; **no `CompanionCaughtUp` at all**; ends within 4 m (`:119-160`). The M6 run recorded "no snag at all" ([report, not source] `G:/UNNAMED_HISTORY/OVERNIGHT_REPORT_2026-09-24.md:58-59`).
  - `LeftFarBehind_HeCatchesUp_ToASpotNearTheCharacter`: one `"distance"` catch-up, placed 2-3 m away (the ring), and `IsClear` (`:179-199`).
  - `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame`: trail, stuck ticks and every field equal; **state digest equal after the load and again after 200 further ticks in both worlds** (`:285-319`).
- `CreatureTests`: a wall hides the player face to face; **lost behind a door it searches, gives up and goes home** (ends Unaware within 0.8 m of home) (`:154-176`); a roamer walks its route (`:209`); a dodged charge runs the boar into the rock and stuns it (`:326-352`); `SixtyCreatures_TickWithinTheBudget` (< 4 ms a tick, §11).
- `FoldscarTests`: the fold holds Tavar until the heart steadies; Quest 2 end to end reaching the south-east stone round the spider with no blow struck (`tests/Application.Tests/FoldscarTests.cs:94-194`).
- `DeterminismAndViewTests.AScripted200CommandSession_ThroughFrames_EqualsItsReplayThroughTheCommandBus`: 200 random move/door commands through uneven frames, replayed into the command bus at the same ticks → equal `StateDigest` and equal rejection log (`tests/Application.Tests/DeterminismAndViewTests.cs:49-78`).
- `ReachabilityTests` is **item/creature/node sourcing**, not geometric reachability (`tests/Application.Tests/ReachabilityTests.cs:7-68`). No test proves every place is walkable from the spawn; C9 ("walk from the outpost to the den mouth and back without... getting stuck", fail if "stalls > 30 s", `docs/PROTOTYPE.md:297`) is met by the scripted playthrough and `SessionTests` (`docs/M6_STATUS.md:104`).

### Content
- `WorldContentTests`: the hollow builds (4 cells, 41 x 41 at 5 m, 2 doors, 6 locations, movement and tier rules, 50 ms tick); a spawn inside a wall, an overhang starting at its own top, a crouch no lower than standing, bounds beyond the cells and a wrong-shaped grid are refused; switch/barrier lint (`tests/Content.Tests/WorldContentTests.cs:46-138`).
- `CombatContentTests.EverySpawnersPatrol_StaysOnItsLeash` (§8).

### Persistence
- The v12 fixture carries a companion mid-trail with `StuckTicks = 2` (`tests/M2.Probe/M2Fixtures.cs:152`; `tests/Persistence.Tests/HistoricalFixtureTests.cs:226`); v13 carries posture (`docs/M6_STATUS.md:163`). The canonical state dump includes `stuck_ticks` (`tests/Persistence.Tests/CanonicalState.cs:137`).

---

## 11. Performance characteristics

- **Counts (FACT):** 64 static structures, 2 doors, 1 barrier, 13 creatures, 4 NPCs (one a companion) in one region-wide list.
- **Measured (FACT):** 60 creatures of all six archetypes, awake in tier A: **0.43 ms per 50 ms tick on ASTRAL**; test asserts < 4 ms (`docs/M3D_STATUS.md:53`; `tests/Application.Tests/CreatureTests.cs:526-544`). Whole-game ASTRAL trial: 798-840 FPS average (`docs/M6_STATUS.md:85`). The RAZER gate (1080p / 60 on an RTX 4070 Ti) is still owed (`docs/M3_STATUS.md:21-22`).
- **Budgets (FACT):** "Main-thread world systems ≤ 4 ms"; tier A ≤ 60 actors (`docs/WORLD_ARCHITECTURE.md:449-451`); "AI decisions at 10-20 Hz" (`docs/WORLD_ARCHITECTURE.md:234`).
- **Cost structure (INFERENCE from code):**
  - `Kinematics.Step` = sub-steps x ≤ 4 passes x (64 static + D dynamic) `Separation` calls; no spatial index, so every query scans every region blocker.
  - Every creature `Move` builds a fresh obstacle list of all other creatures (O(N²) per tick) plus a freshly built `ClosedDoors()` array.
  - Every `Walled`/`Sees`/`InClearView` scans all static blockers plus closed doors. `Walled` is implemented **three times** with the same body (`src/World/Runtime/Creatures.cs:895`, `src/World/Runtime/Companions.cs:597-598`, `src/World/Runtime/Combat.cs:569-570`).
  - Companion `Follow` worst case: 48 marks x 3 segments x ~67 blockers ≈ 9.6k segment tests a tick.
  - At region scale (400 cells, thousands of props) all of this needs a per-cell spatial index. A nav build would want the same index.
- The spike's Godot navmesh bake (6.2 s for 2 km, `docs/M3_STATUS.md:72`) is a presentation-thread measurement, not a sim-budget number.

---

## 12. Weaknesses and known bugs a nav system would fix or break

### 12.1 Would fix (INFERENCE unless cited)
1. **No path around concave obstacles.** Straight steering plus sliding traps movers in U-shapes: the den notch, the lodge's interior walls between a mover and its door, the beam/rock/tree line (sealed from x 143.6 to 153.4, §5.5). Creatures behind walls cannot path: "A target behind a wall is searched for, not pathed to" (`docs/M3D_STATUS.md:78`).
2. **Returning creatures have no stuck handling or time budget** (§7.3), so a creature whose home is behind a wall can stay pinned.
3. **The companion's snag metric measures displacement, not progress** (`src/World/Runtime/Companions.cs:357-360`). A companion sliding along a wall or oscillating never counts as snagged; only the 30 m rule rescues it.
4. **The trail assumes the companion can walk wherever the player walked.** Player-only traversals (jump over the timber, crouch under the beam) and wait→follow (trail cleared) produce goals the companion cannot reach directly, so it snags and teleports.
5. **Teleport catch-up is the reliability mechanism.** ROADMAP counts it as a "pathing intervention" (`docs/ROADMAP.md:273, 275`). An exploratory soak saw **134 catch-ups in 24 runs of 30 simulated minutes** with a random walk that sought out creatures ([report, not source] `G:/UNNAMED_HISTORY/OVERNIGHT_REPORT_2026-09-24.md:76-79`). M7's exit asks that an NPC "navigates in, through, and around" a structure (`docs/ROADMAP.md:284`), which a teleport cannot satisfy.
6. **Wander, flee and back-off targets are never validated.** They can lie inside rocks, walls or past the bounds; the mover pushes against the blocker until the next leg (120 ticks) or the state changes.
7. **Patrol routes are unvalidated straight legs;** two were stranded by a layout move (`docs/M6_STATUS.md:90, 96`).
8. **Hand-authored presentation routes** (§8.4) duplicate knowledge of the layout and are acceptance evidence; a query-able nav could drive them.

### 12.2 Could break, or must be preserved (FACT citations, INFERENCE conclusions)
1. **Determinism and replay:** nav output must be a pure function of authoritative state and the tick (or be persisted). `StateDigest` equality across replay and across save/load-then-continue is tested (§10). Integer mm outputs; stable iteration orders (the code sorts by distance then `StringComparer.Ordinal` key, e.g. `src/World/Runtime/Companions.cs:431`).
2. **Transcendental maths:** creature/companion steering and placement use `Math.Sin/Cos/Atan2` (`src/World/Runtime/Creatures.cs:188-190, 454-455, 616-617`; `src/World/Runtime/Companions.cs:402-403`; `src/Domain/Combat/Combat.cs:240`; `src/Domain/Creatures/Perception.cs:95-97`). Results are quantised to integers, but these functions are not correctly rounded by IEEE 754, so cross-platform bit-identity is not proven. The tests are same-machine and cross-process ("cross-process determinism", `AGENTS.md`). `Kinematics` itself avoids them (`src/Domain/Spatial/Kinematics.cs:115-118`). A nav build should stay to +, -, x, / and sqrt, or integers.
3. **The boar charge's stun depends on collision with structures** (§7.4); nav must not make charges path around rocks.
4. **Perception is deliberately local and non-omniscient:** creatures go "where it last perceived its target... never to where it cannot know the target is" (`src/World/Runtime/Creatures.cs:466-470`). Pathing to a known point is fine; pathing to the true player position would break stealth rules (`STEALTH_DETECTION_AND_THREAT.md` §1, §6 per `docs/M3D_STATUS.md:22`).
5. **Collision stays in `Kinematics.Step`.** Presentation predicts with it and the same `DynamicBlockers` (§7.1). A nav layer should produce intents or goals, not bodies.
6. **Save schema:** trail and stuck counter are schema 12, posture schema 13, creature minds schema 8 (`docs/M6_STATUS.md:62, 163`; `docs/SYSTEMS.md:270`). New persisted nav state means a schema bump through the M2b harness; frozen fixtures must not be edited (`AGENTS.md`).
7. **Height-blind queries:** `IsClear`, `Walled`/`Crosses` and sight ignore `HeightMm`/`ClearanceMm`. So the 0.8 m timber, 0.9 m table, 1.0 m well, 1.2 m fence and 1.4 m cart block sight, blows, shots and companion "clear view", and the beam blocks sight through its 1.3 m gap. Half-walls, fences and low building pieces in M7 will be inconsistent between movement and visibility unless this is decided.
8. **Agent-agent asymmetries** (§7.9): the player passes through companions; creatures pass through NPCs; charges pass through companions and NPCs. A nav with local avoidance must pick one rule.
9. **`Resolve` is order-dependent and capped at 4 passes;** crowded corners can leave residual overlap, and movement never validates the final position with `IsClear`. Nav corridors need a margin.
10. **Nothing is stood on:** floors, foundations or raised building pieces cannot change body Y (`src/Domain/Spatial/Kinematics.cs:159`). One-storey building still needs a rule for foundations on sloped ground: flat to the terrain, a new surface model, or terrain flattening as a delta.
11. **Axis-aligned boxes only** vs ROADMAP M7's "free rotation" (§0.2).
12. **Region-global static list, no per-cell partition:** building edits and streaming will need cell-scoped storage and a rebuild region; there is no invalidation event today because nothing static ever changes.

---

## 13. The owner's rulings - where they are recorded, and conflicting documents

| # | Ruling (context) | Recorded at | Conflicts or tensions |
|---|---|---|---|
| 1 | Nav not Godot-authoritative; deterministic, headless, replayable, derived from world/structure state, responds to edits, works across seams | **Not found** in the repo at `e10d2c4` nor in the V4 handoff (searched). Supporting principles: "Godot is presentation only" (`AGENTS.md`); D-11 (`docs/DECISIONS.md:232-244`); "Transient state (derived stats, cached pathfinding...) lives outside world state and is rebuilt on load" (`docs/ARCHITECTURE.md:164`); `docs/PERSISTENCE.md:84` pathfinding rebuilt on promotion | `docs/WORLD_ARCHITECTURE.md:435` puts "Navmesh / Recast-style, baked per cell, stitched at cell borders, rebuilt debounced on building change" in **§11 "Streaming, LOD, and the presentation contract"**; `:414` "Navmesh around buildings is rebuilt per cell on change, debounced"; `:137` "Collision, navmesh, occlusion - Baked from the above"; `:297` promotion legality "off navmesh"; `docs/PERSISTENCE.md:83` "Navmesh, collision... - Baking / streaming pipeline"; `docs/ROADMAP.md:284-285` M7 exit "the navmesh updates on placement" and proof "navmesh path test"; `docs/RISK_REGISTER.md:288` "Cell-based streaming (WORLD_ARCHITECTURE.md §11) rebuilds navmesh per cell"; `docs/ROADMAP.md:194` M3 spike "a navmesh" (built as a **Godot** `NavigationRegion3D` bake, `src/Presentation/Spike/SpikeScene.cs:159-185`). The word "navmesh" is not itself Godot-specific; the conflict is the §11 placement and the "baked"/Recast framing |
| 2 | Building v1 is one storey: no upper floors, stairs, lifts, climbing, generalised vertical nav, voxels or structural physics; socket/snap | **Not found** in the repo or the V4 handoff (searched "storey", "stair", "upper floor"). Consistent: D-08 (`docs/DECISIONS.md:174-186`); `docs/PROTOTYPE.md:67, 74`; `docs/M6_STATUS.md:181`; `src/Domain/Spatial/Blockers.cs:7-10` "no climbing, nothing stood on" | `docs/ROADMAP.md:283` M7 work "foundations, walls, **floors**, roofs, doors" (ambiguous: floor pieces or storeys); `docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:29-38` lists "stairs; lifts" as hidden interior transitions (a directional M7-owned doc per `docs/INDEX.md:90`); DeepSeek animation docs list climb/ladder clips (asset scope, not gameplay) |
| 5 | No core action may require a radial | [untracked, authority unconfirmed] `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:78-82` (includes "building function", "companion command"); tracked: `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:639-645`; companion orders are G or dialogue, "no radial menu" (`docs/M6_STATUS.md:56`) | `docs/HUD_INPUT_AND_ACTIONS.md:36`: "Controller users should retain the same underlying capability through context/radials" (a controller-only radial dependency is possible) |
| 6 | No networking in M7 | [untracked] V4 handoff `:47, :131` ("No networking in Phase 0-2"); D-12 (`docs/DECISIONS.md:246-252`) | none found |
| 7 | Engine-independent C#; commands; dotted definition IDs; ULID instances; sparse deltas | `AGENTS.md` (architecture rules); D-11 | Structure ids are undotted snake_case (`longhouse_north`), unique only within the region file (§1.4). They are content keys, not definition IDs. M7 pieces will need `def_id` + ULID (`docs/WORLD_ARCHITECTURE.md:409`) |
| 4 | C10 retired | `docs/PROTOTYPE.md:298`; `docs/M6_STATUS.md:105, 141` | (not this topic) |
| 3 | Factions: no global meter, information not global | (not this topic). Analogue in code: creature perception has "no shared awareness" (`src/World/Runtime/Creatures.cs:124-125`) | - |

---

## 14. Contradictions (doc vs doc, doc vs code)

1. **`docs/M3_STATUS.md:46` "jump is cosmetic"** vs code: the jump is a simulated arc that passes low structures (`src/Domain/Spatial/Kinematics.cs:129-160`; `docs/M6_STATUS.md:154`). Superseded, not corrected in place.
2. **`docs/M3_STATUS.md:13` "44 structures"** vs 64 in content now (M6 layout).
3. **`docs/M3D_STATUS.md:39-40`** places the husk at the iron shelf (56,160)-(74,168)-(70,186), the armour at (76,180), and the spawn at (55,60). Content now has the husk round (48,58), the armour at (65,34) and the spawn at (30,158). Superseded by M6 part 1.
4. **`content/spawns/hollow/iron_shelf_armour.yaml` notes** say the bible's (65,34) is "inside the M3 forge shed; this keeps the working layout", yet `at` is (65,34) and the forge now stands at x 53-63, z 138-146. The note is stale.
5. **`docs/PROTOTYPE.md:64`** puts the settlement "entirely inside one 100 m cell, `r_0_0:c_00_00`"; the waystation is now `c_00_01` (`docs/PROTOTYPE.md:72` corrects it). `:72` also says the den is kept "at its north edge", while `docs/M6_STATUS.md:90` moved it to Charwood's north-west corner (112,184).
6. **`docs/PROTOTYPE.md:67` "≤ 6 m elevation"** vs about 8 m of relief (1.66-9.60 m); acknowledged at `:72`.
7. **`docs/WORLD_ARCHITECTURE.md:43` "the domain layer stores raw floats"** vs integer millimetres everywhere (`Body(long XMm...)`).
8. **`docs/WORLD_ARCHITECTURE.md:134` terrain "Procedural from region control data"** vs a fully authored 41 x 41 grid in the region YAML, outside the baseline hash (§3).
9. **`docs/ARCHITECTURE.md:191` "Position (actors) - Movement system"** vs split ownership: `MovementSystem` owns `PlayerBody`, `CreatureSystem` owns creature bodies, `NpcSystem` owns NPC bodies with companions moved by `PlaceNpc` (`src/World/Runtime/RuntimeState.cs:19-49`; `src/World/Runtime/Social.cs:98-104`).
10. **`docs/SYSTEMS.md` S-25 "Transient: Path following state"** and **`docs/SYSTEMS.md:443` "Persisting live AI, pathing... Rebuilt at load"** vs code persisting the companion trail and stuck ticks (schema 12) and creature minds (schema 8) so that a loaded world goes on identically.
11. **`docs/SYSTEMS.md:280` NPC "bodies block movement"** vs creatures ignoring non-companion NPC bodies (`src/World/Runtime/Creatures.cs:830-836`).
12. **`docs/WORLD_ARCHITECTURE.md:234` tier A runs "pathfinding"**, and §6.2 lists an "AI activation distance 45 m" (`:261`); neither exists (`docs/M3D_STATUS.md:78`).
13. **Art kit footprints 6 x 6 / 9 x 6 m** vs collision 10 x 8 / 16 x 8 m (§5.6); the owner ruled the kit bounds are not canonical.
14. **`docs/WORLD_ARCHITECTURE.md` §11 navmesh in the presentation/streaming contract** vs owner ruling 1 (§13).
15. **`docs/ROADMAP.md:283` "floors"** and `docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:34-35` "stairs; lifts" vs owner ruling 2 (§13).

---

## 15. Open questions for the M7 designer

1. Which movers use nav: companions only, NPCs working in buildings (M7's exit), creatures (search/return/chase)? The boar's charge and ambush-straight-line behaviours should stay un-pathed.
2. Should nav treat jump-over (≤ 1.15 m, player only) and crouch-under (clearance > 1.15 m) as passable per agent capability? Companions can do neither today.
3. Representation: exact geometry (inflated boxes/circles → visibility graph or constrained triangulation) vs a fine grid (≤ 0.25 m to resolve 0.9 m door corridors) vs per-cell tiles stitched at seams. The terrain's 5 m grid is too coarse (INFERENCE).
4. One inflation radius (0.35 m) or several (0.35-0.55 m)? The boar fits a 1.6 m door with 0.5 m of corridor.
5. Should `IsClear`, `Walled` and `Sees` start honouring `HeightMm`/`ClearanceMm`, so that low fences and half-walls are consistent between movement, sight and shots?
6. Rotation: OBB blockers or 90°-snapped building pieces?
7. Floors and foundations: flat at terrain height ("nothing is stood on"), or a new walkable-surface concept? How do foundations sit on the 0-43° terrain?
8. Where does nav live and who owns it: a pure Domain query service (no slice), rebuilt from `WalkSpace` + building deltas + door/barrier flags, or a World system with a transient slice? If any nav state affects the next tick (a path cursor), it must be persisted or be recomputable from persisted state for `StateDigest` continuity.
9. Door semantics for nav: are closed doors passable edges for agents who can open them (only the player can interact today)? Should NPCs open doors via commands?
10. Replace or keep the snag/catch-up teleport as a last resort? M6's "no snag over 15 s" test forbids any catch-up on the lodge route.
11. Seam proof: M7 needs "a structure straddling a cell boundary" (`docs/ROADMAP.md:284`). Today's single region-wide `WalkSpace` makes seams trivial; is M7 expected to introduce per-cell nav partitions now (streaming is Phase 2), or prove seam-independence with the region-global model?
12. Per-cell spatial index for blockers: introduce with nav, given every query today is O(all blockers)?
