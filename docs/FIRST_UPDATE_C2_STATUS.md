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

Supper uses the existing seated pose with the engine's arm IK to alternate a bite and a drink. Night uses the existing bed pose, aligned along each mattress toward its pillow. Inn bed places may author a facing; legacy coordinate pairs keep their existing facing. These are presentation gestures selected by the fixed schedule; no hunger/needs system or new animation pipeline was added.

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

## Final local handoff — 2026-10-27

Captured gameplay source: `576f50f00356baff22937814c9016e2b63acf8d6`, based on current `claude/pp11-fixes` `7c1ae133`. No push.

Final proof: `outputs/Waystation_Inn_C2_20261027/review.html` in the Codex task workspace. It contains the two untouched floor PNGs, one 35.67-second 1920 × 1080 H.264/AAC day-cycle clip, raw resident census, launch provenance, hashes and test logs. MP4 full decode reports zero errors. Floor stills and sampled video frames were checked; the traveller has a separate chair and sleepers lie along their mattresses. Claude's full frame acceptance remains pending.

Final focused gameplay tests: 7/7. Related content tests: 51/51. Final Release content lint: 666 definitions, zero errors. Presentation build: zero errors, existing warnings retained. Native capture completed all four hours; shutdown logged geometry/light-indexing and resource cleanup diagnostics, retained with the evidence. The GPU lease was released before the next lane acquired it.

## Art revision after Claude's verdict

Six native tavern tables and benches now dress the common room with a plank floor, ceiling beams and hanging lanterns, Renn's bar/shelves/barrels/mugs, a rug and notices. Warm interior fills and a separate hearth light use no extra shadow maps. The new counter and benches have authoritative collision; the existing supper anchors and stair remain reachable (revised tests 2/2 PASS). Upstairs timber uses world-space sampling instead of stretched UVs. Native Cairnwold timber beds now carry thick ticking, wool covers and pillows; each chamber has candles on a stool as well as its lantern. Lower-storey shutters, a door and a storey beam clarify the exterior.

The completed revision proof contains the same two floor stills plus common-room, bar, bunk-room and exterior views. Claude's art acceptance remains pending; the ordinary High launch result below is a startup check, not a gameplay safety guarantee.

Recorder-free High autostart result: FAIL. Without MovieWriter or the inn harness, autostart loses the Vulkan device (VkResult -4) during scene construction, before gameplay. The stack reaches ArtLibrary.LoadScene/ForceDraw through ItemsView.BuildContainers. Raw evidence: D:\Otherreach_Scratch\codex_c2_proof\normal_high. Autostart itself selects harness settings in Main.cs; this establishes recorder independence, but the ordinary player-settings launch was checked separately. No baseline attribution or shipping preset change is claimed.

Ordinary High result: PASS for the requested launch check. With no harness/autostart/capture flags, the normal loading-screen and saved-settings path reached the front end and remained there for 60 seconds without graphics-device loss. A separate custom user directory held High preferences; the owner's preferences were untouched. Gameplay was not entered, so this is a startup check rather than a full High playtest. Evidence: `D:\Otherreach_Scratch\codex_c2_proof\ordinary_high`. The earlier ordinary-launch failure conclusion is withdrawn; the recorder-free autostart failure remains real.

## Completed art-revision proof

Gameplay/captured source: `b78873e0`. The final bundle is `C:\Users\jluca\Documents\Codex\2026-10-18\from-local-claude-for-the-owner\outputs\Waystation_Inn_C2_Art_20261027\review.html`, superseding the original C2 art proof. It includes six untouched 1920 x 1080 PNGs, a 38.9-second H.264/AAC day-cycle clip, actual resident census, source/launch records, image/video hashes, both High diagnostics and the relevant build/test/lint logs. All six stills and the 1 fps contact sheets were inspected; full video decode reports zero errors. The final 3090 lease ran 08:48:34 to 08:55:14, then released to DeepSeek. Shutdown retains four geometry/light-unpair errors in the raw native log; no clean-shutdown claim is made.

Final presentation build: zero errors (existing warnings). Revised stair and resident schedule tests: 2/2; the earlier focused 7/7 and related 51/51 remain recorded above. Final content lint: 666 definitions, zero errors. Local branch only, no push, rebased/up to date on `claude/pp11-fixes` `7c1ae133`. C3 has been read and prepared, but its brief explicitly requires C2 acceptance before implementation.

## Claude acceptance and two small fixes (2026-10-27, after 09:15 verdict)

Claude accepted the C2 art revision. Follow-up source `6ffea22c` (after `58d9094b`) changes only presentation: sleepers hold the existing native get-up clip at its back-lying first pose, with the same bed contact alignment; round disc pillows become rounded rectangular cloth cushions. Upper timber repair now includes LOD1/2 material suffixes. A small shared shader projects the existing timber colour/normal/ORM maps in world metres, with grain along horizontal logs on either wall axis. Roof and stone materials remain native.

Two original 1920 x 1080 stills, personally inspected, are in `C:\Users\jluca\Documents\Codex\2026-10-18\from-local-claude-for-the-owner\outputs\Waystation_Inn_C2_Two_Fixes_20261027\review.html`. The short proof uses `--inn-fixes` to capture only the bunk room and player's cot room at Day 2 23:00, with the existing MovieWriter path at fixed 30 fps/Low. It is not a High test or an FPS measurement. Build: zero errors, existing warnings; native shader compiled and both stills completed. Native teardown diagnostics are retained with the proof.

### P0 ordinary High gameplay: INCOMPLETE, not a walking pass

Three strict ordinary saved-settings High launches used no autostart, harness or recorder. Ordinary creator UI entered live Ashen Hollow without Vulkan -4/device loss. The required two-minute walking interval was not completed: desktop automation key taps did not trigger gameplay actions. Attempt3 reached world tick 2400 (08:30); its log records the character beginning and player control. Do not infer that idle gameplay passes the requested walk.

A fourth launch tried ordinary mouse bindings in private preferences. The Windows computer-use API timed out on `activate_window`; refreshing selection and retrying also timed out. The owned process tree was stopped, project configuration restored and official GPU released. No owner preference or shipping preset was changed. Evidence folders `high_walk`, `high_walk2`, `high_walk3`, `high_walk_mouse` are copied beside the two stills. Device-loss comparison against shipped `7c1ae133` remains untriggered: no ordinary-launch device loss was reproduced. Recorder-free autostart's earlier Vulkan -4 remains unresolved.

The two art fixes are ready for review; the overall P0 check is not finished. C3 is pending behind P0. Preparatory branch `codex/first-update-c3` contains only the requested H-healing preference cherry-pick (`c7cf0fe7`, original `e186c872`) on top of `6ffea22c`; no camp implementation or C3 completion is claimed. All work remains local, no push.
