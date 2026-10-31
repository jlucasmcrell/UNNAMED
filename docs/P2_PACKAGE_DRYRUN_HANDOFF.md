# Playtest 2 packaging dry run: pinned66ca5630

Source command was the integrator's pinned build_playtest.ps1 command, on D:,
AssetBase D:\Otherreach_Scratch\release_assetbase_p2. Actual output:
D:\Otherreach_Scratch\release\20261031b\game.

This is a completed DRY RUN WITH BLOCKERS, not RC FINAL or release acceptance.
No existing package, owner install, preferences or save was changed.

## Packaging correction (code34c3054c)

The unmodified command exported successfully, copied the assets, then failed
its obsolete MetaHuman verification at primary/rigged/mh_veth_f. The accepted
Test02 native HumGen root has no such retired model.

- Carry the separate humgen_characters bundle beside the EXE (43 native exact-
  target motion resources,71 files including their existing metadata).
- Carry AssetBase art notices, then overlay repository canonical notices.
- Verify the accepted_native_character_delivery manifest against all114 exact
  file hashes (43 bodies and71 motion files). These pins derive from the prior
  accepted target-body guard and motion-delivery receipts, NOT the candidate's
  own bytes. The previous MetaHuman sealing/verification mode stays available.
- Require the actual content overlay and native body/motion delivery, not the
  optional unused primary/bindings.json absent from Test02.

The required metadata is ALREADY in AssetBase at
primary/manifests/approved_player_bodies.json, with a copy of this accepted
114-file manifest at docs/P2_APPROVED_NATIVE_CHARACTER_FILES.json. Accepted
future motion/body updates require their own reviewed receipt and refreshed
pins; never seal candidate bytes as approval.

Validation: three focused corruption/missing/escaping-file checks PASS;
PowerShell parser PASS. Actual operator continuation verified114files, ran
all original packaged cache/required-file checks, then the actual Vulkan boot.

## Actual evidence

D:\Otherreach_Scratch\playtest2_dryrun_66ca5630_20261031 contains original logs,
PACKAGE_B_RESULT.json, OPERATOR_DELIVERY_REPAIRS.json, DRY_RUN_COMPLETION.json,
DRY_RUN_PROBLEMS.json, native_boot/RESULT.json and the unedited30PNGs/sheet.

- Clean pinned import/build/export passed. Initial failed export was our isolated
  APPDATA missing installed Windows templates; matching4.7.2 templates fixed it.
- Packaged cache:13802references,6716large images,0failures.
- All591audio files exist and match their manifest hashes.
- Native3090 Vulkan/Forward+/Ultra boot: exit0,547model-load-order lines,
  410.79-second lease. This is engine quit-after30, not normal user Quit.
- All30stills reviewed, final original inspected full-size. Actual1920x1080
  despite requested3840x2160: initial window is clamped to the screen. Do not
  call this4K acceptance; oversized capture must resize and verify its output.
- Boot559drawn/3withheld: explicit art gate FAIL. Missing cw_gravel_stones
  material record on two ritual circles; missing old arena_chalk_ring model;
  beam_woundmoss's oversized cw_dead_trunk_b binding fails fit.
- Large pale diagonal sky bands are visible in actual clean66title pan08-29.
  B006 investigation belongs to the Steps lane. Canonical Foldscar masonry is
  preserved (resolvedB004); it is not being misreported as creature geometry.

## Final run

Claude alone integrates. Use the final pinned SHA and accepted reconciled art,
a fresh D: label/export worktree, and the accepted packaging correction.
Do not hold one GPU lease over export+CPUcopy+6-minute cache audit+boot. Export
under one<=10-minute lease; release for CPU work; use a fresh native boot lease.
The native load-order proof can be windowed Vulkan instead of a duplicate
headless boot, retaining count>=100 and actual exit0. Record its exact mode.
The3090-only played gate remains separate; farm pass was waived by the owner.
No marketing or publication by this lane.
