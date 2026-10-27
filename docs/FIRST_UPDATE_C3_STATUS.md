# C3 — field camps and first-hour accessibility

Own worktree: `D:\Otherreach_Scratch\codex_c2_wt`, branch `codex/first-update-c3`.
Base: `claude/pp11-fixes` at `7c1ae133`; includes accepted C2 and H-healing cherry-pick `c7cf0fe7` (original `e186c872`). Local commits only; Claude integrates and reviews the native proof.

## Player behavior

Kera sells a cheap bedroll/fire kit. Use it from the pack on dry, level, clear outdoor ground, outside towns and roads. One camp is allowed; pack it at the fire before placing another. The fire and bedroll persist through saves and absence. Packing returns the kit if the pack has room.

The fire uses the existing cookfire station and crafting panel: roast meat, a timber/lint torch, salves, boiled water and woundmoss tea. Boiling spends a full flask and returns the empty bottle. Field repair restores bog/grey iron at Standard quality, capped by the original make. A better original make requires its proper station for full restoration.

Rest at the fire for three times normal out-of-combat health regeneration. Movement, combat or leaving the fire ends rest. Time and weather continue; rest does not skip time. After death, choose camp or the existing bound-Waystone return during the existing respawn countdown. With no bound stone the existing starting-point fallback still applies.

The starting flask now supplies a small heal (6 HP over 3 seconds), leaving an empty bottle. Refill at the stream bank or a well. Empty/full variants use existing atomic inventory exchange. No hunger or thirst meters.

Renn gives one torch on the first greeting, with an off-hand cue; the existing persisted conversation visit prevents repeat gifts. The first forge visit teaches the torch, and first cookfire access teaches the camp basics. Dark interiors, raw meat, first combat and forge access request their hints. F1 help is reachable on Light guidance. H prefers healing items, strongest heal per tick first.

## Implementation boundaries

Camp pieces reuse persisted `CreatedEntityRecord` item instances and their cell baseline hashes; no save-schema migration. The public camp view names its owner, position and placed pieces. Domain/World remain independent of Godot. Rest and death-return choices are transient owned session state. Authored and dynamic stations share the existing crafting/repair paths.

Presentation reuses the native stone fire pit, hearth particles and a textured bedroll. The fire light casts no shadows. Rare violet sparks occur only near the standing Fold barrier; cyan remains reserved for Waystones. No tarp or general construction system.

## Validation

- Focused `CampTests`: 4/4 pass. Covers placement exclusions, one-camp/packing, actual roast and field repair, live rest and movement cancellation, persistence/absence, camp and Waystone returns, flask sip/refill/boiling, one-time Renn gift and forge teaching.
- Architecture: 23/23 pass.
- Content lint: 680 definitions, zero errors.
- Presentation build: zero errors; existing warnings retained.
- `git diff --check`: clean.

Native proof inspected: daylight camp, normal crafting panel and night camp, plus the continuous placement/cooking clip. The completion receipt reports zero command rejections; phase receipts show the kit consumed, two persistent pieces created, raw meat replaced by roast, and rest established while world ticks advance. Source: `1b55aec0`; the final forge hint text correction does not affect these frames.

Review: `C:\Users\jluca\Documents\Codex\2026-10-18\from-local-claude-for-the-owner\outputs\Camp_C3_20261027\review.html`.

The private capture supplies a kit and raw meat and advances the clock only for the labelled day/night comparison. Placement, station access, cooking and rest use the public gameplay commands. Low / fixed 20 fps is a proof fixture, not a High launch or performance measurement. The original MovieWriter sequence is 91 frames / 4.55 seconds because existing review waits use wall time. Both original-speed H.264/AAC and explicitly labelled 4x slow inspection playback are included. Original PNGs are unedited.

Take01 never invoked the review because the parser lacked the new option; it was stopped and its GPU lease released. `1b55aec0` registers that option. Take02 completed and released its lease before the nine-minute deadline. No Vulkan -4 occurred. Native teardown still reports Godot renderer/object RID leaks and light geometry unpairing errors after completion; logs are included rather than claiming a warning-free engine exit. No extra GPU testing was added for unrelated teardown warnings.

## C2 follow-up

The requested side-angle native still confirms all three bunk sleepers supine, with faces/chests/hands upward. No further pose change. C2 walls accepted by Claude. P0 High closed by Claude's 09:55 ruling; no further walk or High retry.

Side proof: `C:\Users\jluca\Documents\Codex\2026-10-18\from-local-claude-for-the-owner\outputs\Waystation_Inn_C2_Side_20261027\review.html`.
