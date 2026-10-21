# Direct MPFB target motion

The five `*_motion.res` files contain 32 CC0 UAL1/UAL2/KayKit clips plus five state aliases, retargeted directly onto each original 53-bone MPFB game rig by Epic UE 5.8 IK Retargeter. Runtime only relocates animation node paths; it performs no MetaHuman-to-MPFB conversion.

`tools/metahuman/retarget_mpfb_motion.py` uses a fresh `OTHERREACH_PIPELINE_ROOT` and existing read-only source/imported target assets, with separate pelvis retarget root and Root motion bone. Finger chains are excluded and auto-aligned feet reset. Original game bodies, rests, binds and geometry are untouched.

`build_mpfb_motion_library.gd` verifies all original target rests, fixes horizontal Root travel for controller movement, retains root/pelvis translation only, and holds other bone positions and scales at target rest. It adds omitted rest holds, measures target gait paces and foot-plant times, and records the exact target body SHA256. Runtime rejects a library paired with a different body.

`verify_mpfb_motion.gd` checks all five libraries, 37 states each, target hashes and constant non-root bone lengths; samples 12 locomotion/combat/posture clips on every original rig. Set OTHERREACH_PIPELINE_ROOT to the job directory and run the scripts with Godot headless in a minimal project. RC d6 evidence is under D:/Otherreach_Scratch/rcd6_20261021/evidence.

The obsolete MPFBPosture libraries are retained as historical evidence; runtime no longer loads them. All posture and regular motion now use the same direct target route.

Ten NPC variants (`mpfb_named_*` plus two `mpfb_resident_*`) use documented MPFB face targets and fitted system hair over the original feminine/masculine light-outfit rigs. `stage_mpfb_named_variants.gd` verifies all 53 original rests and parent names exactly before copying the accepted direct Epic tracks with each variant body hash. No retarget calculation or rest conversion occurs. `build_mpfb_named_variants.py`, `export_mpfb_named_copy.py` and `mpfb_named_recipes.json` reproduce the source; Blender native glTF colour factors use supported ShaderNodeMix RGBA Multiply nodes after MPFB Export Copy rebuilds materials. Variant staging and ten distinct body-position hashes are recorded under the RC d6 evidence directory. `verify_mpfb_motion.gd` now checks all fifteen bodies (555 states, 180 sampled cycles).
