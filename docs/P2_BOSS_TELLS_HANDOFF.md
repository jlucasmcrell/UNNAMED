# Playtest 2 boss tells and CMB-42

Source: `visual/boss-ground-telegraphs`, based on accepted `66ca5630`.
The implementation source is `510c1627` (CMB-42 geometry/time correction `1266c47b`; Custodian anchor `808fab63`; accepted cave log name `f88b01cc`; ceiling roots `4d2b0e2c`).
Claude is the only RC integrator. This branch has not been merged to the RC.

## Scope

The shipped R11 encounter is the single accepted legacy Ring Custodian. The held D19 deep Custodian is not spawned or substituted. This source adds read-only presentation for that actor, the accepted native Matriarch, and the Gate Warden in their actual areas.

Ground marks follow authoritative attack source, phase, committed charge direction, and remaining travel. They retain the combat contact allowance of 300 mm; extracting its helper does not change strike rules. Moving melee includes its unspent lunge. Charges show a rounded contact capsule that shrinks to the remaining contact disk, rather than a fresh full-length lane ahead of a moving boss. Windup is amber; active contact orange. Recovery/death have no stale attack mark.

An existing full-quality dust recipe and terrain-conforming chalk wave mark engagement and authored phase transitions. These physical encounters do not use violet (reserved for the Other). Native R11 has no authored health phases; the Matriarch has no authored weak point. Neither is invented in presentation. Weak cues follow authored openness. R11 explicitly anchors its cue to the native `index` bone and measured rear gear, in both bindings overlays; the existing deep binding receives the same body metadata without adding a spawn.

Court dressing uses first-party terrain-conforming paint and suppresses only local vegetation under the boss courts. No missing optional `arena_chalk_ring` binary is fabricated. The accepted full-quality native body/animation/material delivery remains required; no body, clip, texture, mip or LOD quality is reduced.

## Checks and evidence

- CMB-42: five focused `BossTelegraphModelTests` pass, including actual committed bearing, elapsed integer charge travel, target contact allowance, moving-melee lunge and stale-mark removal.
- Anchor binding: five `BarrowworksGateTests` pass, including identical native index-bone data in public/regional overlays and the integrated overlay hash.
- Public Release: zero errors (32 existing warnings). Private proof modules/hooks are excluded from Git and restored after compilation; frozen private/public assemblies have separate receipts.
- Native proof root: `D:\Otherreach_Scratch\playtest2_dryrun_66ca5630_20261031\boss66`.
- Original Custodian `frames/custodian_1266_01`: 16 originals, exit 0, 240.398 s GPU lease, exactly one actual live actor, empty art-problem list. All frames personally reviewed. Ground tells/court accepted by producer; generic low weak cue REJECTED and replaced by source `808fab63`. Longest windup capsule partly outside camera; active remaining capsule fully visible.
- Minimal Custodian correction `frames/custodian_anchor_808f_01`: three originals personally reviewed, idle inspected full size. Gold marker now surrounds the actual red rear index gear and follows idle/windup/active poses; normal depth, one legacy actor, no art problems, exit 0, 228.60 s lease. Producer review ready for Claude.
- First Matriarch `frames/matriarch_1266_01`: 18 originals personally reviewed; REJECTED. Ceiling-root caps overlap the boss and the old fallen-log ID is missing. `f88b01cc` uses the already delivered full-LOD log. `4d2b0e2c` seats the same five native roots in the actual roof samples instead of at floor+4.6 m. Body, scale, spawn, collisions and vault geometry remain unchanged. Superseded by the final room-fit proof below.
- First Warden `frames/warden_1266_01`: 19 originals personally reviewed, rear cue and charge original inspected full size. One Warden, three authored phases, exit 0, 471.89 s lease. Sweep/grasp, phases, recovery and rear torso cue read. Long-charge proof angle behind a foreground rise REJECTED; superseded by the side/elevated charge proof below. No world assets are hidden for framing.
- Full world draw also exposes missing `creature_doubled_hart` rigged body, plus the already reported missing `cw_gravel_stones` material and bad `beam_woundmoss` fit. These world-art gaps remain open; no zero-withheld final gate claim.
- Native Matriarch vault replacement: no missing art (18 frames, exit 0,117.55 s), but overhead camera still occludes the body; REJECTED. Native skinned bounds17.6x16.6m exceed the original14x11m antechamber. Candidate510c1627 moves the unchanged body/scale to the actual29x21m great chamber, narrow initial spread0.25m, safe return on the squeeze approach. Cave geometry/stats/moves/loot/schema unchanged; Content lint1131defs/0errors. This is a placement proposal on our visual branch, not an accepted RC content change. Persisted actors may need integration reconciliation; fresh/old-save gates remain required. Normal1.7m roof-clear camera proof is recorded below at frames/matriarch_greatroom_510c_01.
- Final great-chamber proof `frames/matriarch_greatroom_510c_01`: 12 originals personally reviewed, lunge active and phase1 inspected full size. Body is clear of roots/room walls in these views; all moves, remaining-charge/contact end, recovery and three phases read. One actual boss, no art problems, exit0,130.90 s lease. The room/court change remains a proposal for Claude/D19 review; body/scale/combat rules remain unchanged.
- Warden replacement `frames/warden_side_4d2b_01`: three originals personally reviewed, windup inspected full size. Side/elevated view reveals remaining-charge capsule/contact end on the actual terrain. Exit0,331.96 s lease. The far windup tip still passes under outside-court vegetation; no depth trick or vegetation removal was used. Combine with original non-charge/phases/rear-cue frames.

Requested implementation and proof handoff is **FINISHED for Claude's verdict**, not self-awarded art acceptance or a final gate. Curated review: `D:\Otherreach_Scratch\playtest2_dryrun_66ca5630_20261031\boss66\REVIEW_FOR_CLAUDE\REVIEW.html`. Its HANDOFF.json pins all **47 untouched originals** and source hashes. Rejected low-cue/obstructed-camera sets stay outside that selection. Five telegraph tests, five binding tests, Release build and Content lint are the affected checks; no repeated full suite or fight campaign.

The fixture stages public CreatureViews over actual live actors and native arenas, with normal depth testing and all other world actors retained. These frames prove presentation, not a played fight, phase-trigger gameplay, performance, normal Quit, marketing or final RC acceptance. Static-string/RID teardown warnings remain in original logs; do not hide them behind exit 0.

## Integration

The accepted `systems/boss-stingers` commit `aeacf3e2` is already an ancestor of `66ca5630`; no audio branch fork or merge is needed. Bring this source through Claude's normal integration review, preserve newer bindings while transferring the two authored Custodian cue fields and refreshing the accepted overlay hash. Do not replace the RC's whole content overlay with this older baseline.

## Private delivery and placement caveats

Use accepted full `release_assetbase_p2`, assembled per the pinned66 recipe, plus the preserved native character motion bundle. This source adds no external art binaries or reduced assets. Required bodies are `bw_codex_custodian`, `creature_d5_tarantula_matriarch` and `creature_d5_stone_giant`, their existing exact native clips and signed full-resolution texture caches. The eight accepted Custodian clip JSON records must be in primary/animation/clips (Info reads primary); unchanged native motions remain in their accepted roots. The corrected cave log is existing fallback/ready/flora_ph_fallen_log with all delivered LODs/cache. Preserve art notices/credits.

Matriarch placement510c1627 is deliberately separate: Claude may hold that proposal while taking the tell code. Do not silently overwrite a newer encounter/recovery record. It changes location/initial spread/court/safe return, not stats, and can change the fight's spatial experience. Previously persisted actor placement/baseline compatibility and the actual encounter remain for final integration gates. Native proof is on candidate content; it does not certify an old save. The accepted current66 legacy Custodian remains in R11; no deep spawn has been reintroduced.
