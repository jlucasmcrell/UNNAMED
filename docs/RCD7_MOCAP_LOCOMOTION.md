# RC d7 mocap locomotion

Owner approval 2026-10-23: "FAL is better, yes." Adopt FAL walk/jog lateral and backward clips on both matched casts. Keep forward walk/run/sprint on Quaternius UAL1. No KayKit for player/NPC locomotion. The owner also approved walk-back/run-back after the original-time 08 comparison.

The accepted lateral/backward graph uses separate walk and jog blend points with measured native stride/contact timing. Old libraries retain their original graph. Native character meshes, rests, skeletons and binds are unchanged.

Licensed animation payloads stay outside this public repository. Stage with tools/metahuman/build_mocap_overlay.gd and a fresh OTHERREACH_PIPELINE_ROOT containing mocap_overlay.json. Each pair is [id, baseline .res, exact Epic-target donor .res, unchanged target body, fresh output .res]. Copy only the six approved locomotion clips and contact/pace metadata; the tool also aliases idle_look to the existing UAL1 idle and asserts every remaining animation digest is identical. Current accepted payload: D:/Otherreach_Scratch/rcd7_mocap_full_accepted_20261023/runtime. It contains both native player libraries/catalog and 15 exact MPFB target variants under mocap_motion. Include that payload in the next matched RC d7 pair; leave reviewed RC d6 packages unchanged.

MPFB first loads <mh_characters>/mocap_motion/<id>_motion.res when installed, then checks the exact target-body hash through the existing guard. Older packages retain their embedded libraries. Native MH continues to use its catalog-selected library and existing body-hash guard.

Source policy is shared by the canonical Epic retarget scripts in mocap_sources.py. Retarget native rigs in UE, export with Epic GLTFExporter, bridge through the documented pipeline, then select clips; no Godot retarget. Use canonical native/MPFB library builders for loop and gait metadata.

There are no dedicated turn clips: normal character heading rotation uses the active gait or idle pool. idle_look now selects the existing exact-target UAL1 Idle_Loop instead of KayKit Idle_B. No arm keys were edited.

Remaining KayKit style audit: pick_up/PickUp; spear_ready and spear_thrust; bow_ready/draw/release; the previously accepted calmer sword attacks. Dodge_Backward is a movement-adjacent combat action requiring a suitable replacement; anim_LandRoll_R is a landing roll and is not relabelled as a dodge. Existing accepted combat fixes remain unchanged.
