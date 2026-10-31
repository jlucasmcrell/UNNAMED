# Owner D19 visual repair (Codex, 2026-10-30)

Worktree `D:\Otherreach_Scratch\build2_integ_codex`, branch `fix/owner-visual-bugs`.
Claude alone integrates into `rc/playtest-2` and judges the frames. No release merge,
installation, owner profile write or marketing capture is part of this repair.
DeepSeek retains the boss definitions/stats; the inspected data is pinned `f5268759`.

## Matriarch: restore the accepted native delivery

The accepted `7a47ff2f` Mixall tarantula is the Matriarch, not the older hunting-spider
body from the attempted `394a5b04` replacement. The sealed body survived in
`creature_upgrade_d5_20261027\batch_b_delivery`, but its six native animation files
were omitted from that delivery. The complete accepted files survive in
`D:\Otherreach_Scratch\creature_upgrade_d5_20261027\batch_b_payload`.

Private staged delivery:
`D:\Otherreach_Scratch\owner_boss_looks_20261030\delta\primary`.
`matriarch_accepted_art_restore.json` records every source/delivery SHA-256.
Body SHA-256: `3a1dd675b732ef9d27290d7bbac7d72f82b210bcc164d5918f6aa913f4d482cc`.

Carry the model directory, all six `animation/ready/creatures` GLBs, their six
`animation/clips` records, and the signed cache entry/DDS together. Merge cache
entries by key; never replace an assembled manifest wholesale. Keep previous
`165797cd` and `aaffd2f5` art deliveries.

The binding adds `creature.boss.den_gorge.matriarch` using the accepted beast's
body and six matching 70-joint takes. It removes the incompatible Nephila hit
reaction from the original beast binding. No dedicated native hit take exists;
that limitation is explicit, not filled with a different skeleton or an idle.
No change to stats, radius, anatomy, native materials, textures or accepted scale.

`matriarch_native_motion_guard.json`: all six clips contain motion, every native
joint/rest matches. `matriarch_cache_gate.json`: native 2K cache, zero failures.
First native diagnostic `matriarch_native_proof`: no art problems, exit 0,
546 frames / 18.2 seconds. Its camera used imported unskinned bounds and is too
wide for a visual verdict. Corrected close proof is tracked separately; no
acceptance or actual-cave proof is implied by that first diagnostic.

## B-009 candidates (not a visual verdict)

The seven stone-giant copies require seven bodies with the canon's silhouettes.
The private native candidates and exact source receipts live beside the Matriarch
delivery. Every body and motion is subject to Claude's eye before integration.

| Boss | Catalogue source / intended read |
|---|---|
| Gate Warden | MR-0002 accepted stone giant; this one retains it |
| First Formed | MR-0150 native rock golem, fixed Kal wing-frame rigid to chest |
| Foreman Nine | MR-0161 elopez7 Unit 666 industrial maintenance body; newly rigid-part rigged |
| Salt Walker | MR-0136 Colorado Stark native exo-frame; native 43-joint rig, newly authored motion |
| Under the Stairs | MR-0142 piacenti Octobot's complete rigged eight-limbed assembly, native worn-material variant |
| Specification | ARCH-9afe2c54784b complete native Red Echo source slabs in a rule-field; no humanoid body/head/limbs |
| Bell That Answers | MR-0398 Greeble3d Zorag deep angler; original idle/swim/bite/alert, native hit/death additions |

The source Mech FBX's nominal Take001 is static (439 channels, none changing).
Unit666 has no native rig/actions. Neither is called animation-complete based
on a nominal action name. Authored state files have matching native rigs/rests;
no UAL or unrelated spider takes are silently assigned.

`native_boss_rig_motion_guard.json`: eight distinct bodies, no missing matching
bone names, rest mismatches, missing clips or static bound state takes.
`native_candidate_cache_gate.json`: 337 image references, 207 at >=2K, zero
failures. Original full-resolution map data is retained; signed cache aliases
reuse only identical embedded-image hashes and roles. No reduced-quality path.

These seven candidates are not a claim that all 35 D19 boss appearances are
finished. B-008 and the real cave/warden context frames remain next in the queue.

## Matriarch native close evidence completed

`matriarch_native_close`: 12 native originals and every one of 18 one-second
samples personally inspected. The native spider is complete and articulated;
its leap and curling death read without wrong-rig distortion. 546 frames /
18.2 seconds, decode clean, process exit 0, 61.532-second lease. Producer
review is not Claude acceptance. Exact art/cache delivery is pinned in
`D19_MATRIARCH_ART_DELIVERY.json`. Real cave context remains a separate queued
check. The initial over-wide proof is retained as rejected framing.

## Producer rejections and affected reproof

The initial centroid-based First Formed wing placement, closed-shell Octobot
export and Unit666 joint/antenna Specification experiment are rejected. Exact
old files and reasons are retained under `rejected_firstformed_centroid`,
`rejected_understairs_closed`, and `rejected_specification_mech_parts`. Those
frames cannot approve the revised art. The corrected First Formed uses native
wing_root and a back anchor. Resetting the Octobot's saved rest pose alone
did not change its native bind matrices, so that attempted correction is also
rejected. Selecting the largest XZ footprint from its original ArmatureAction
was also wrong: that selects the closed outer casing, as the v4 native proof
shows. Evaluating the original Blender geometry at frames 1..110 identifies
the unfolded final frame 110. That native pose becomes the authored takes'
base by bone name. Its two independent native skin roots share the floor
correction; no foreign skeleton or body replacement is introduced.
The Specification now has nine
complete engraved native Red Echo slabs (9 x 7,820 source triangles), native
full-resolution2K/4K maps/UVs, modest inlay light and no robot body. The only
normal-map adapter is the source manifest's DirectX-to-OpenGL green-channel
conversion at unchanged resolution. Updated affected cache:337refs207large,
zero failures; native rig/motion guard still passes.

The first corrected batch completed every First Formed state, then stopped
at Octobot with `Packaged texture >=2K is uncompressed`. The producer rebuilt
Octobot clips after that renderer had loaded its cache manifest; this input
race is not device loss. The inputs are now copied together into immutable
`frozen_native_v4/primary`; the completed two-body proof used that snapshot.
No input mutation is permitted while its validation is queued or running.

The v4 process completed in 252.264 seconds with exit 0 and no art/cache
errors. Its closed Octobot pose is rejected. Specification's native body,
idle, locomotion, attack and hit previews are producer-reviewed; the death
put nine slabs into three coplanar overlaps. That final state now settles
into separate floor rows. Preservation receipt proves the body and all
other Specification state GLBs remain byte-identical to v4. The corrected
Octobot uses immutable v5; the Specification idle/death check uses v6.
Both affected native checks are now complete; Claude's judgement remains.

Octobot v5: exit 0 in 161.165 seconds, art problems 0. Full-size idle, all
16 originals and all 50 usable one-second samples personally inspected;
setup sample 1 rejected. The open eight-limbed body stays intact through
locomotion, three attacks, hit and grounded collapse. All ten Octobot GLBs
remain byte-identical in final v6. The native recording has import holds;
its 51.4-second container is not a real-time or FPS result.

Specification v6: exit 0 in 36.912 seconds, art problems 0. All four revised
idle/death originals and six usable one-second samples reviewed, including
full-size final landing: nine slabs lie in separate floor rows. Earlier
fourteen nondeath originals and raw samples 55..77 supply the unchanged
body/other-state proof. The old overlapping death is rejected. The shortened
native review video joins only the unchanged body excerpt and revised death.

`D19_B009_ART_DELIVERY.json` pins final immutable v6 files/cache, retained
Warden dependencies, every actual pinned D19 move mapping, licence/provenance
and the actor review receipts. Seven B-009 bosses now have seven distinct
body bindings; only the Warden retains the old stone giant. This is a
FINISHED source/private-art handoff for Claude's visual verdict, not RC
acceptance. DS data/stats and all prior accepted art deliveries remain intact.

## Actual world: B-008, cave and Warden context

Six unedited native originals are pinned in `D19_WORLD_CONTEXT_DELIVERY.json`:
ford close-up, creek bank, two wider Warden court views and two Matriarch
chamber views. All six were personally inspected; Claude judges them.

The accepted B-008 ford correction is `0b947e38`: a 4.8 m gravel bar uses
`material_loose_gravel`, rather than an undersized 0.32 x 0.45 m pebble model
that fails fitting and produces a grey stand-in. The initial private world
overlay incorrectly merged that old model field back into the surface-only
record. That fixture and its ford verdict are rejected. Corrected v7 preserves
every existing source binding record whole and adds absent pinned D19 entries
only. Its close-up shows the native gravel material. The authored circular
bar geometry is unchanged; this is no new terrain/collision/baseline edit.
Native golem/giant dependencies and full-resolution gravel maps/cache are
pinned for delivery, not inferred from the incomplete farm art root.

Corrected ford/court capture completed in 449.522 seconds. It logged requested
quit 0 but returned 1, so no clean-quit claim. Cave capture completed in
249.799 seconds and returned 0; the native 70-joint Matriarch is present with
the smaller spiders. Every lease remained below 565 seconds, with full world
and NPC presentation. Initial head-cropped Warden frames and the animal-
obscured near-bank frame are rejected as clear framing. Wider views retain
the court's foreground architecture and both actual Wardens.

**B-030 remains open with DS D19 data:** legacy and boss Wardens overlap at
the Stopped Gate; legacy and boss Matriarchs are both alive in separate cave
chambers. The source data/spawns were not edited and neither actor was hidden.
Nineteen missing frontier dressing assets and the cave's `flora_fallen_log`
remain explicitly reported; no zero-placeholder, RC gate or marketing claim.
Claude alone integrates and judges; packaged RC gate videos are a separate
step after his reviewed integration.

Corrected First Formed: full-size idle, all 14 originals and raw one-second
samples 2..22 personally inspected. Wing frame stays behind the torso and
clear of the legs through seven state previews. A decode-clean 21-second
actor-only clip uses exact raw frames 33..663, excluding setup and the later
batch failure. The batch exit was 2; no whole-batch or clean-quit claim.
Foreman, Salt Walker and Bell originals plus every selected one-second sample
are also producer-reviewed; exact actor spans and limits are recorded in
`selected_actor_review/*_producer_review.json`. Claude judges all candidates.
