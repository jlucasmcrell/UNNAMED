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
| Foreman Nine | MR-0161 elo pez7 Unit 666 industrial maintenance body; newly rigid-part rigged |
| Salt Walker | MR-0136 Colorado Stark native exo-frame; native 43-joint rig, newly authored motion |
| Under the Stairs | MR-0142 piacenti Octobot's complete rigged eight-limbed assembly, native worn-material variant |
| Specification | A distributed field of native Unit 666 armour plates; no humanoid body/head/limbs |
| Bell That Answers | MR-0398 Greeble3d Zorag deep angler; original idle/swim/bite/alert, native hit/death additions |

The source Mech FBX's nominal Take001 is static (439 channels, none changing).
Unit666 has no native rig/actions. Neither is called animation-complete based
on a nominal action name. Authored state files have matching native rigs/rests;
no UAL or unrelated spider takes are silently assigned.

`native_boss_rig_motion_guard.json`: eight distinct bodies, no missing matching
bone names, rest mismatches, missing clips or static bound state takes.
`native_candidate_cache_gate.json`: 322 image references, 192 at >=2K, zero
failures. Original full-resolution map data is retained; signed cache aliases
reuse only identical embedded-image hashes and roles. No reduced-quality path.

These seven candidates are not a claim that all 35 D19 boss appearances are
finished. B-008 and the real cave/warden context frames remain next in the queue.
