# MPFB posture clips

`*_posture.res` contains only `crouch_idle` and `crouch_walk`, baked onto the five original MPFB target rigs. The previous local-rest multiplication selected the clip but left an upright, partly sunk body with an outstretched arm.

The shipped route is Epic's IK Retargeter and native GLTFExporter, matching the owner's runbook. Target meshes are imported copies of the original MPFB GLBs; target chains exclude fingers, retarget root is pelvis, and root motion uses the rig's Root bone. The game continues to draw the original MPFB GLBs. No skeleton rest, skin binding, body shape, or mesh buffer is edited. MetaHuman gameplay never reads these clips.

`tools/metahuman/retarget_mpfb_posture.py` runs in UE 5.8's Python commandlet and exports both original Quaternius posture clips onto each of the five 53-bone MPFB rigs. It checkpoints assets, retargeters, excluded chains and output paths in `ue_mpfb_posture.json`.

The original exports held the planted toe 1.9 cm (feminine), 2.86 cm (masculine) or 2.21 cm (large variant) above its rest sole plane. The script applies the corresponding negative Z `TranslationOffsetGlobal` in Epic's Pelvis Motion operation, with `AffectIKVertical=1`. These are measured per-rig animation offsets, not changes to rests or geometry. Ground probes after the UE re-export measured at most 2.8 mm contact error through the sampled idle cycle.

Then run Godot 4.7.2 in a minimal project (`project.godot` containing `config_version=5`):

```
godot --headless --path <minimal-project> --script <repo>/tools/metahuman/build_mpfb_posture_library.gd
```

The packaging script verifies all 53 rests and names against the original game rig before copying target-space animation keys. It pins horizontal Root translation for controller-driven movement and inserts explicit rest holds for omitted tracks. There is no Godot retarget calculation or runtime retarget modifier. Export paths are recorded under `D:/Otherreach_Scratch/rcd5_20261021`; the checked rest discrepancy is below one micrometre. The in-game Tavar gate also checks lowered pelvis and grounded sole positions before capturing 20/10/5 m views; a selected clip name alone is insufficient.
