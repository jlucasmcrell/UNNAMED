# Native player swimming

The existing water-depth, shoreline, speed and stamina rules remain authoritative. A body crossing deep water in the air joins swimming on landing. Float depth follows the saved creator stature; legacy body presets supply their nominal stature when no appearance is saved.

Player HumGen libraries must contain both owned water takes. This includes bodies used at boot before the creator-body swap and older saved-player choices. A missing take or a crouch-walk source is refused rather than displayed as swimming.

| State | Owned Free Animation Library source | Original duration |
| --- | --- | --- |
| swim | fal/anim_Swim_Surface_Fwd | 1.0667 seconds |
| tread | fal/anim_SwimIdle | 2.9333 seconds |

Both retain source RateScale 1. The native AnimationTree blends these takes with land locomotion over 0.45 seconds on entry/exit. Stationary treading keeps its original playback speed. Carried weapons, grip/haft control, the off-hand light, crouch/tuck and ground foot planting are withheld through water animation and restored on land.

The source pack is already owned and catalogued as HAVE in the private asset library (Fab listing `481ef75b-892b-424f-a213-f1cc058c9c19`). No generated animation or new paid download is used. Runtime body geometry, materials and native rests are unchanged.

## Private motion delivery

Water takes use the existing Epic IK Retargeter -> native GLTFExporter -> exact-target Godot pipeline. `tools/animation/build_humgen_motion.gd` verifies the target's 59 native joint names, parents and rests, excluded fingers and original playback timing.

Build a two-take supplement against the exact body GLB that will ship, then run `tools/animation/merge_humgen_swim_motion.gd` headlessly with a JSON manifest:

```json
{
  "body": "<exact-runtime-body.glb>",
  "original": "<latest-accepted-motion.res>",
  "supplement": "<exact-target-water.res>",
  "output": "<separate-staging-directory>/body_motion.res"
}
```

The merge requires matching body hashes, refuses replacement of the baseline path and writes a readback receipt. Every original animation key and metadata value is checked unchanged; only the two takes and their source receipts are added. Always merge against the latest accepted character-quality library so its idle/facial work survives. Changed body files must pass the native-rest check again; different rests require a new exact-target retarget.

The motion libraries, native transport meshes and owned source assets stay outside this public repository. Stage their private delta into a new candidate package, preserving the approved package and unrelated manifest entries.

## Water sound contract

The DS water set binds exactly `wade`, `swim`, `tread`, `enter_water` and `exit_water`. Native ground contacts choose shallow or knee-deep wades. Swimming/treading suppress dry footfalls. Wet/dry crossings play one transition, and the underwater bed follows the actual camera below the surface.

Looping accepts a bound family as well as an exact sound ID. `tools/animation/test_water_audio.gd` checks the actual C# bank with headless/Dummy audio: the bound underwater family resolves to its delivered looping stream, and exact-ID playback remains available.

The 19 private water sound files and manifest rows belong to the swimming update. Merge those rows by stable ID into its candidate art root; keep them out of the already pinned Playtest 2 package.

## Verification

`SwimmingTests` covers deep water, shallow wades, speed/stamina, shore exit, airborne entry and short/tall or legacy stature. `HumGenMotionPolicyTests`, `WaterMovementTests` and `WaterSoundBindingTests` cover motion receipts and the five audio names. `tools/animation/test_water_blend.gd` exercises the actual body/library through 720 CPU frames, checks continuing swim/tread motion, both transitions and interruption reset, and records per-frame joint changes.

CPU checks are development evidence. Acceptance requires real-time native video from the fresh packaged game, populated world, Ultra and normal game camera: enter, swim, stop/tread, turn, exit and weapon restoration, including short-body buoyancy. Record frame-diff results and package/source/art pins. Native packaged proof remains pending until the package gate releases the GPU.
