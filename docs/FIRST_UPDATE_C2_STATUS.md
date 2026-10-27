# C2 — Ashen Hollow and the waystation inn

Owner brief: `BRIEFS_FIRST_UPDATE_20261027.md`, C2 / `PLAYTEST2_PLAN.md`, note 11.
Branch: `codex/first-update-c2`. Local commits only; no push. Claude's frame review is the acceptance step.

## Population and schedules

Ashen Hollow now supports four named people, three workers and one travelling adventurer, rather than four named people, six workers and three adventurers. The fletcher, hunter and miner slots are removed. Cairnwold's population is unchanged.

| Resident | Day job (world X, Z metres) | Inn home (X, Z metres) |
|---|---|---|
| Renn | Keeper's counter, 441 / 725.3 | 438.25 / 715.5 |
| Sel | Survey table, 472 / 722 | 443.35 / 715.5 |
| Kera | Smithy, 461.6 / 739.6 | 448.35 / 715.5 |
| Cook | Common-room hearth, 448.8 / 729.6 | 437.3 / 719.05 |
| Guard | Road watch, 464 / 748 | 438.9 / 719.05 |
| Porter | Road deliveries, 476 / 731 | 440.5 / 719.05 |
| Tavar, once freed | Road scouting, 478 / 731 | 442.6 / 719.05 |

Each has a supper place after work until 22:00, and an upstairs bed at night. Existing merchant hours remain 06:00–20:00. Tavar remains at 545 / 642 while the existing Foldscar barrier stands; freeing him enables his routine, and hiring him still overrides it. This is a barrier reference on the routine, not a change to the quest.

The travelling adventurer has a separate supper chair and cot. The player cot is reserved in the first north-side chamber from the upper entrance, at 449.6 / 720.6. Neither cot is used as a resident's bed.

## Building and movement

The existing licensed waystation kit remains the ground-floor common hall, with its hearth, counter and dining tables. The same kit forms a raised six-chamber sleeping wing over sealed stores, joined to the hall by a covered timber stair. Existing Barrowworks/Cairnwold beds, tables, stools, lanterns and material maps are reused; no source assets or asset-pipeline files changed.

The upper floor is at world Y = 10.0 m. The stair foot meets the actual terrain at 7.2 m and rises 2.8 m over eight metres. The sealed-store arrangement lets the existing single-support navigation represent the building without overlapping public paths at different heights. The same authored supports drive movement, player foot planting, NPC bodies, furniture placement and camera collision. Room walls, furniture, stair rails, the upper roof and stair awning have collision. Each chamber has its own shelf lantern.

Supper uses the existing seated pose with the engine's arm IK to alternate a bite and a drink. Night uses the existing bed pose. These are presentation gestures selected by the fixed schedule; no hunger/needs system or new animation pipeline was added.

## Rental

Renn's existing `rent_room` consequence and `RentRoom` handler are reused. Five coin restores health and stamina, saves through the existing inn flow, and skips no world time (DN8). His room line directs the player upstairs to the reserved cot. No save schema changes.

## Validation and proof

- Presentation build: passes, with existing compiler warnings.
- Focused gameplay tests: 7 pass, covering population, paid rest, the four daily phases, Tavar's captivity/release, and a stair traversal from ordinary ground through the actual upper doorway and back down. Each ascent step checks for an incorrect vertical snap.
- Related NPC-life and social content tests: 51 pass.
- Final Release content lint and native proof receipts accompany the handoff.

Proof is two floor stills plus one viewport-recorded day tour at 08:00, 13:00, 21:00 and 23:00. The game's normal offline advance places residents; the capture does not write their state directly. Captions distinguish the captive Tavar from residents already living at the inn. The census records every resident's actual body and activity for each phase.

Capture uses Godot's built-in MovieWriter at 30 fps and the existing Low preset. The clock jumps are intentional; this is a schedule/layout proof, not a frame-rate measurement. High startup lost the graphics device during the capture setup on both Vulkan (`-4`) and D3D12 (`0x887a0005`); High was not visually validated. No shipping render preset was changed. Failed startup and desktop-recorder attempts are excluded from the proof.

All native runs check `OWNER_PLAYING.flag`, use the official GPU lock, stop at the lease deadline, and release the lock between runs. Final evidence and the exact commit/base are recorded in `CODEX_PROGRESS.md`.
