# HumGen production motion

This is the RC d8 HumGen route. The MetaHuman and MPFB branches are frozen references.
No KayKit for **any** animation. No manual arm keys or Godot rest-frame retargeting.

1. Snapshot the original paid HumGen Godot exports outside Git, retaining each GLB SHA256.
   Production snapshots have 59 native humanoid joints, fitted clothing, hair and expressions.
   Both `ready/<id>/<id>.glb` and `rigged/<id>/<id>_rigged.glb` resolve to the same bytes.
2. `prepare_humgen_sources.py` imports the original free UAL files. `migrate_humgen_fal.py`
   uses Epic's dependency-aware migration from the acquired Free Animation Library project.
3. `probe_humgen_target.py` imports a snapshot through Epic's native glTF importer. The
   transport preview uses Body LOD3; the game continues to draw the original dressed GLB.
4. `retarget_humgen_probe.py` uses Epic IK Rig, IK Retargeter and GLTFExporter. Correct the
   target chain definitions, never its mesh, bones, rest pose or bind matrices. Exclude
   finger chains, retain relaxed native fingers, and reset foot retarget-pose offsets.
5. `audit_humgen_transport.py` checks all joint names, parents and global rest matrices,
   then measures actual head/hand/foot movement. Constant rest tracks alone do not pass
   as evidence that an arm moves.
6. `build_humgen_motion.gd` checks the transport again in Godot against the original
   target's 59 bones and local rests. It copies native tracks by name and extracts
   planar Root translation for the game controller. Every rotation remains as exported.
   It measures gait pace, contact times and phase **in seconds**, checks relaxed fingers,
   facing and upright torso, and writes an exact-target AnimationLibrary outside Git.
7. `run_humgen_cast_motion.ps1` runs these steps sequentially for the cast's staging
   receipt. It reads immutable staged inputs, not mutable export filenames. Owner-play
   interruptions stop owned workers; rerunning resumes/retries their partial outputs.

## Source-facing groups

The acquired FAL source sequences do not all share one facing convention. Their walk
strafes face a quarter-turn away from the preview reference. Only `fal_side_walk` uses
Epic's final **Additive Pose** operation with a Root-only 90-degree global yaw, converted
through the actual native reference pose. Jog strafes and backward clips use `fal`
without that offset. Spear motion uses `fal_spear` independently. No limb-key offsets
are applied. Reusing one facing correction for all FAL clips makes backward motion
sideways; Godot's mean-shoulder heading check catches this regression.

The reference pose comes from `AnimPoseExtensions.get_reference_pose`, not an
unregistered preview component whose bone transforms may still be identity.

## Launch and gates

Set `OTHERREACH_HUMGEN_ANIMATION_JOB` to a dedicated job directory containing a clean
UE project and the source receipts. Native commandlets use `-run=pythonscript`, a
forward-slash `-script` path, and `-NullRHI`. Workers run hidden, without Windows Terminal.
Actual renderer runs additionally acquire the shared GPU lock and defer to owner play.

The game loads only `<payload>/mocap_motion/<id>_motion.res` for HumGen and requires the
original target body hash, source receipt for every state, the required accepted states
and measured foot contacts. The 39-clip set is an integration candidate: bow source work
and rendered acceptance remain separate gates. Do not promote a package from numerical
checks alone.

`Diagnostics/HumGenRigGate.tscn` verifies the actual production rig roles, grip binding,
FACS blink/speech and material matching. Claude owns the rendered motion gate, using
the game's normal figures and production payload. Capture its exact window title
through WGC, never the desktop. Final acceptance requires normal game play and
packaged evidence.

## Native API references

- [Epic IK retargeting](https://dev.epicgames.com/documentation/unreal-engine/ik-rig-animation-retargeting-in-unreal-engine)
- [Epic retarget operation stack](https://dev.epicgames.com/documentation/unreal-engine/retargeting-operation-stack-in-unreal-engine-5-8)
- [Epic AnimPoseExtensions](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/AnimPoseExtensions)
- [Godot Animation](https://docs.godotengine.org/en/stable/classes/class_animation.html)
- [Quaternius Universal Animation Library](https://quaternius.com/packs/universalanimationlibrary.html)
