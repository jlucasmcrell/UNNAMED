# Approved body motion and posture (2026-10-18)

Owner order: integrate approved bodies and staged motions first, then correct posture without replacing the accepted jog. Clothing follows; frontier performance remains paused.

## Diagnosis

The WP17 test asset root still played the old 159-joint UAL idle/walk/run. Its neck base was 8–14 cm ahead of the shoulders in twelve-phase measurements. The new Fab clips reduce that to 0–17 mm. The original UE body and approved 167-joint body's shoulder/neck bind frames agree within 0.001 mm and 0.001 degrees; the converted female head/neck surface agrees within 0.001 mm. Do not reshape the approved body or move its shoulder girdle.

The old private rebake is also 159 joints. Three non-Manny maps lacked 22 runtime-driven helpers, and the combat tool still selected the old game_ws skeleton. All MetaHuman maps now declare the current 167 joints; stance and combat tools accept an explicit `--mh-target` like locomotion.

## Retarget correction

The accepted jog keeps its source take, timing, gait, torso, shoulders, limbs and global head orientation (gaze). Only its sustained cervical pitch is calibrated: one constant retarget-pose offset, computed from twelve phases against the target bind neck/head direction in the body's yaw frame. Neck lengths and neck-base position stay unchanged. No hand-authored frame corrections, runtime pose modifier or body edits.

`locomotion_fab.py` enables `--neutral-neck` only for the MetaHuman jog. Other takes and production NPCs retain their existing posture. The accepted jog remains 0.8333 s at 2.9581 m/s authored speed; ankle seam is zero and maximum rotation seam 0.007 degrees. On both Average bodies the calibration is approximately -23.2 degrees. Female original-head ear landmarks reduce the jog's ear/shoulder offset from 63–74 mm to 24–37 mm; idle/walk already lie within 6–22 mm and are not calibrated.

## Reproduce

Use an independent asset root, preserving the integration-A root. Bake on the current assembled male 167-joint body (female clips are carried rest-relative by the game):

```powershell
python tools/asset_pipeline/stance_clips.py --out <stance-stage> --rigs mh,mh_combat --mh-target <assembled-male-glb>
python tools/asset_pipeline/combat_mocap_clips.py --out <combat-stage> --rigs mh --mh-target <assembled-male-glb>
python tools/asset_pipeline/locomotion_fab.py --exports <UE-exports> --out <locomotion-stage> --rigs mh --mh-target <assembled-male-glb>
```

Install those stages in that order using `locomotion_install.py`, without changing the bindings. This preserves the `_mc` active combat clips over their fallback counterparts. Do not reinstall the stale legacy clips from `install_bodies.py`.

```powershell
python tools/asset_pipeline/check_neck_retarget.py --source <original-jog-glb> --male <assembled-male-glb> --female <assembled-female-glb> --out <check.json>
godot --path src/Presentation -- --anim-sheet <shots> --anim-profiles --anim-states idle,walk,run --asset-root <new-primary> --bindings-overlay content/_art_bindings.json --visual player=metahuman_f
```

Repeat the rendered sheet with `player=metahuman_m`. Each state is held at twelve phases, from the side/front/back, with runtime rest-relative retargeting, corrective helpers and the bound outfit. The Python check asserts unchanged body/stride/shoulder matrices, neck-base position, cervical lengths and global head rotation on both sexes. Numerical checks do not replace looking at the images.

## Validation status

Release Presentation build: zero errors. Presentation tests: 243 passed. Real-asset retarget invariants: both sexes, twelve phases passed. Native sheets and an original UE RigLogic pose reference are queued behind the rendered integration playthrough; visual acceptance remains pending.

Private evidence: `D:\Otherreach_Scratch\body_posture_20261018`. Integration A's assets remain in `D:\Otherreach_Scratch\integration_20261018\assetbase`.
