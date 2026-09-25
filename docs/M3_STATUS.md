# M3 status - Player, Camera, Movement, Interaction, World Cells

**Date:** 2026-09-23. **Branch:** `claude/phase1`.
**State:** implemented and verified headlessly. Exit (a) was measured on RAZER on 2026-09-25 with the Phase-A package (`docs/PHASE1_TECHNICAL_CLOSEOUT.md`); **exit (b) still waits on a RAZER window** (below).

## What M3 built

| Layer | What | Where |
|---|---|---|
| Domain | The walkable surface (an authored height grid, triangle-interpolated in integer millimetres), collision footprints, `Kinematics.Step` - the one movement function - and the tier rules (A/B/C/D, stepwise, with hysteresis) | `src/Domain/Spatial` |
| World | The running simulation: `Simulation` is the composition root, command queue and fixed 20 Hz tick. Systems: clock, movement, interaction, world flags, progression, discovery, tiers. Every slice of runtime state has exactly one owning system, claimed at composition; a write by any other system throws (`ARCHITECTURE.md` §5, 4(b)) | `src/World/Runtime` |
| Application | `GameSession`: boot (content validates or the game refuses to start), new game, load, save, autosave, and the frame loop (commands drained at each tick boundary, ticks run from an accumulator clamped at 0.25 s) | `src/Application/GameSession.cs` |
| Content | `region.ashen_hollow` (the 4 cells, a 41 x 41 height grid at 5 m, 44 structures, 2 doors), 4 locations, 2 door world flags, `config.base_speeds`, `config.simulation_tiers`; `WorldContent` builds and lints them (`WLD001`-`WLD008`) | `content/`, `src/Content/WorldContent.cs` |
| Persistence | Schema 5: the player gains facing and discovered-location records. Frozen V4 player shape, 3 -> 4 repointed at it, the 4 -> 5 step, discoveries in the definition-ID pass, the v5 fixture (written with fixture content 0.1.2, loaded against 0.2.2, which renames the place) | `src/Persistence`, `tests/Persistence.Tests/Fixtures` |
| Presentation | The Godot 4.7.2 .NET project. The greybox hollow is built from the same layout the domain collides against. It has a full-body mannequin, a continuous camera, input turned into commands, prediction with `Kinematics.Step`, a HUD with an F3 overlay (every cell's tier), and quicksave/quickload (F5/F9). Modes: smoke, performance capture, 2 x 2 km spike | `src/Presentation` |

## Exit criteria (`ROADMAP.md` M3)

| | Criterion | State |
|---|---|---|
| (a) | The playable build walks the 200 m x 200 m / 4-cell area at a sustained 60 FPS at 1080p on RAZER's RTX 4070 Ti, with OBS, H3 and other GPU workloads stopped | **Measured 2026-09-25** on the Phase-A package (`docs/PHASE1_TECHNICAL_CLOSEOUT.md`, RAZER section): the third- and first-person walks held a 1% low of 83-93 FPS, a median frame of 8.3-8.6 ms and a p99 of 10.6-10.7 ms at 1920x1080 in three warm runs, with two repeatable 30-39 ms hitches on the walk. The owner accepted the result as the Phase-1 baseline on 2026-09-25; a short magic segment's missed 1% low and the repeatable hitches are a targeted follow-up |
| (b) | The isolated 2 x 2 km greybox produces a capture, recorded in `RK-02` | **Pending the same window.** Built and trialled. It is not the formal `D-01` revisit gate in Phase 1 |
| (c) | Player state mutates only through commands; a test fails if presentation writes domain state | Done. The simulation's public surface is only reads plus the command path (`TheSimulation_ExposesOnlyReadsAndTheCommandPath`); views and events are immutable (`ViewsAndEvents_HaveNoPublicSetters`); presentation source may not reach past it (`PresentationSource_NeverReachesPastThePublicReadAndCommandSurface`); the view-subscription test (below) |
| (d) | Cell save/load preserves world changes | Done. An open door is its cell's delta with the cell's `baseline_hash`; closing it again rebases the record away; the round trip restores the exact state digest (`SaveLoadTests`) |

**Camera minimum (`CAMERA_PERSPECTIVE_AND_PRESENTATION.md` §22).**

| Requirement | How |
|---|---|
| Full-body third person | `Avatar` is one mannequin body at every distance |
| Seamless zoom to first person | The wheel moves one camera from 6 m down to the eye (V jumps between them); the shoulder offset fades out with distance |
| Shoulder swap | Q cycles right, left, centre |
| Camera collision | A spring arm against every structure, roof and door. Indoors it compresses by itself and never forces first person (§4) |
| Interaction at every distance | Targeting uses the body's reach (1.6 m, checked by the simulation), not the camera. In first person the target must be near the crosshair |
| Same commands in every perspective | `MoveCommand` and `InteractCommand` carry no camera state (`Commands_CarryNoCameraState...`) |
| No presentation state writes | Exit (c) |
| Basic body visibility | First person hides only the head (it still casts a shadow); looking down shows torso and legs |

**PROTOTYPE §6.3's engine tests.** Boot smoke: `--quit-after 300`, clean log. The view-subscription test runs headless in `dotnet test`, not in the engine: `AViewThatListensToEverything_SeesExactlyWhatHappened_AndWritesNothing`. A stub view hears every event of a scripted 200-command session. It sees exactly the refusals, door toggles, flag changes, discoveries and XP that happened, and an unbroken chain of body moves. The world ends in the same state as a run with nobody listening.

**C3 in shape (full criterion at the prototype's end).** `AScripted200CommandSession_ThroughFrames_EqualsItsReplayThroughTheCommandBus` runs a session through uneven frames. It then replays its command log straight into the command bus at the same tick boundaries, and the two runs end with the same state digest.

## Owner decisions this needs, and the defaults taken (all flippable)

1. **Where the runtime systems live: `src/World/Runtime`, not `src/Domain`.** The world delta's mutators are internal to World, and Domain cannot see World. M1's `ISystem` needs Domain internals, so a World system cannot implement it. Enforcement is unchanged in kind: every writer is internal, no assembly outside the tests is granted internals, and slice ownership is checked. M1's Domain `ISystem` and writer remain for Domain-internal stores.
2. **Movement is planar; height comes from the terrain; jump is cosmetic.** `PROTOTYPE.md` §3: "Jump exists but is never load-bearing". A vertical domain arrives when something needs it.
3. **Interiors are enterable buildings inside the outpost cell**, because `PROTOTYPE.md` §3 puts both "entirely inside one 100 m cell". `WORLD_ARCHITECTURE.md` §8's separate interior spaces, with a hard load boundary, arrive with dungeons (M8).
4. **Two content kinds were added: `regions/` (1) and `world_flags/` (2).** `DATA_MODEL.md` requires every location's `region_ref` to resolve and every world flag to be declared, and both kinds are in its closed table. `PROTOTYPE.md` §4.4 is updated. Location IDs use `DATA_MODEL.md`'s `location.` prefix (the prototype wrote `loc.`).
5. **Discovery is S-30's Phase-1 subset**, and S-30 as a whole is Phase 2 (`SYSTEMS.md` §3). A place is discovered by walking into it, once, for its content-defined XP (25 each; the outpost, where a character starts, gives 0). "Cartography map data" in M3 is those records plus the places' anchors. There is no map screen: PROTOTYPE's six panels have none, and `MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md` §1-2 and §6 are honoured (knowledge, not the database; no markers).
6. **Tiers.** All four are assigned from distance (`config.simulation_tiers`: 150 / 600 / 2000 m, 10 m hysteresis), stepwise, never A <-> D. A and D are implemented. B and C are `ITierSimulation` stubs until M4. In the hollow every cell is always tier A; the tests use a smaller radius to exercise the rest.
7. **Commands apply at the next tick boundary in the same frame**, so input reaches a domain event within one frame. Prediction draws the body with `Kinematics.Step` from the last tick. A change of direction mid-tick can pop the drawn body forward by up to a tick's travel (16 cm at a run).
8. **The command log is kept in memory, not saved.** `command_log.jsonl` stays absent, which `PERSISTENCE.md` §4.2 defines as "replay unavailable". Saving it is a small follow-up if the owner wants replayable saves before M6.
9. **The game's generation profile is empty until M3d/M3f.** The region declares only its terrain rule, so nodes and populations will change the baseline before any save ships.
10. **`content_version` is the label `0.3.0`**, set in `GameOptions` (diagnostic only).

## Verification

- `dotnet test src/UNNAMED.sln`: **401 passed**, 0 failed - Architecture 14, Content 58, Domain 98, EntityRegistry 23, World 58, Persistence 122, Application 28 (new project).
- Content lint: 19 definitions, 0 errors.
- Godot 4.7.2 headless on Astral: `--quit-after 300` boots with no error or warning; `-- --smoke` prints `PASS`. It walks to the longhouse through the command path, opens the door, and a quicksave loads back to the identical state digest.
- Windowed trial captures on Astral, looked at frame by frame (the screenshots are in each capture's folder). These are **not the gate**: the machine is an RTX 5090 and Ryzen 9 9950X3D, not RAZER.

| Trial (Astral, 1080p, vsync off) | Average FPS | 1% low | p99 frame | GPU p99 | Hitches > 33 ms |
|---|---|---|---|---|---|
| Prototype, third person | 979 | 172 | 3.5 ms | 0.16 ms | 1 (screenshot; since excluded) |
| Prototype, first person | 991 | 177 | 3.8 ms | 0.16 ms | 1 (screenshot; since excluded) |
| Prototype, obstruction | 998 | 184 | 3.6 ms | 0.18 ms | 1 (screenshot; since excluded) |
| Spike, third person | 939 | 221 | 3.5 ms | 0.47 ms | 0 |
| Spike, first person | 939 | 219 | 3.6 ms | 0.46 ms | 0 |
| Spike, obstruction | 928 | 204 | 3.9 ms | 0.46 ms | 0 |

The spike's navmesh bakes as 64 tiles of 250 m in 6.2 s (14,291 polygons). One 2 km bake overflowed Recast's region IDs, and tiles are what streaming needs anyway (`WORLD_ARCHITECTURE.md` §11). Over the spike, tiers averaged A 14 cells, B 112, C 274.

## The performance gate - what the owner is asked for

Agree a window on RAZER with OBS, H3 and other significant GPU workloads stopped; nothing will interrupt a stream or a render. In that window, from a checkout of this branch:

1. `dotnet build src/Presentation/Presentation.csproj`
2. `godot --path src/Presentation -- --perf --perf-out <dir>/prototype`: about 5 minutes. A warm-up, then 75 s each of camera obstruction (in and round the waystation's buildings), third person and first person (a loop through all four cells). The player walks the hollow through the real command path, clear of every creature's senses (M6), with 12 animated stand-in bodies on top of the world's own people and creatures. The summary's `route` note counts the waypoints reached and any blow that reached the character: a clean capture is struck 0 times.
3. `godot --path src/Presentation -- --spike --perf-out <dir>/spike`: the navmesh bake, then the same three segments over the 2 x 2 km greybox.

Each run writes `summary.json` (per segment: frame-time distribution, 1% and 0.1% lows, CPU render time, the worst frame's process time in each second (Godot refreshes that monitor once a second, so it is summarised once a second, without the second a screenshot falls in), GPU time, RAM and VRAM peaks, hitches, and whether the 1% low held 60 FPS), `frames.csv` (its `process_ms` column is that monitor as each frame read it), and a screenshot per segment. The numbers go into `RISK_REGISTER.md` `RK-02` and here. If either scene cannot sustain 1080p / 60 after reasonable optimization, that is the owner-review stop, before M3b builds on the engine.

## Not done, and why

- Gamepad bindings, input remapping and the accessibility options of `CAMERA_PERSPECTIVE_AND_PRESENTATION.md` §21 and `HUD_INPUT_AND_ACTIONS.md` §9 beyond FOV and sensitivity. Keyboard and mouse only for now.
- Foliage fade (§5): the camera passes through canopies; they have no collider.
- The boot smoke is not in CI. The Linux runner has no Godot, and CI is unchanged for this run. It runs locally with the commands above.
- For DeepSeek (DeepSeek-owned file, not edited): `ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md` still assumes first person (lines 6 and 843). The mannequin will be swapped for Wave 0's rig when it is handed over.
