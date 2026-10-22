# RC d6 finding 38: calmer motion

The normal locomotion graph mixes neutral idle rotations into descendants of spine_01/spine: 85% when backing, 20% when strafing and up to 30% when jogging forward. The pelvis and legs keep their original motion. OTHERREACH_CALM_BACK=0 selects the comparison baseline. This is a filtered AnimationNodeBlend2, not skeleton editing or runtime IK.

Three grounded KayKit attacks (Melee_1H_Attack_Chop, Melee_1H_Attack_Slice_Diagonal, Melee_1H_Attack_Stab) replace sword_attack_1/2/3 and the attack alias. Export with OTHERREACH_CALM_ATTACK_CLIPS=1 using the existing Epic retarget scripts and builders; append to the accepted 42-state libraries. No other clip changes. Fingers remain excluded. Native skeleton rests, binds and body buffers are unchanged.

Validation: 12 cases (four bodies, three movement directions), each 240 frames, preserve pelvis and leg positions and rotations exactly. Head-parent length drift is at most 0.000280 mm. Existing four-body real engine foot-contact callbacks pass. All 1,026 unrelated clip hashes in the 27 libraries match their previous versions.

Regression command: Godot --headless --path src/Presentation --script tools/metahuman/verify_calm_locomotion.gd -- <manifest.json>. The manifest contains entries with id, body and output paths for Ada, Mateo and MPFB fem_lt/male_lt. The test writes upper_filter_verification.json beside it. Existing verify_foot_plants.gd checks callback delivery.

Native animation job: D:/Otherreach_Scratch/rcd6_calm_20261021. Stage its runtime for the next MH package, not the previous inn-only runtime. MPFB libraries are versioned in Art/MPFBMotion.

Real-time gate: RCd6_38_Motion_Review/full_angles03 in the task outputs. Four approximately 84-second normal-controller videos show forward/back/strafe/run and all three AttackCommands, then side and three-quarter repeats. WGC video pacing is preserved; Master bus audio has a constant sync offset only. Engine.MaxFps=0, owner preferences loaded. Shadowless scatter is hidden for ankle visibility. Grip work in these review captures remains unaccepted and is not included in this commit. Both native targets also have 27 paused target/source comparison frames each. Claude decides motion acceptance; these are source-runtime gates, not a packaged owner approval.
