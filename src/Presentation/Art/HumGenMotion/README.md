# Human Generator target motion

The two `*_motion.res` files hold the game's own 24 clip states plus three aliases (`sword_attack`, `stop`, `turn`)
retargeted onto each Human Generator trial body **offline**, so the runtime only relocates animation node paths. There
is no runtime cross-skeleton conversion on this arm: `SkinnedModel.ComparisonClips` copies track paths and nothing else,
exactly as it does for the MPFB libraries.

How they were made (`tools/humgen/build_humgen_motion_library.gd`, run headless):

1. Each source clip is loaded from the shipped asset base (`anim.player.mh.<state>.glb`) with its own skeleton.
2. Every track's bone is matched **by name** on the target and re-based onto the target's own rest with the game's own
   formula (`key' = restTarget * restSource^-1 * key`, `ArtLibrary.Retarget`). Tracks for bones the target lacks are
   dropped; tracks Godot's glTF import omits (a bone held at its rest) are added back at the target's rest, so a clip
   cannot inherit the previous pose.
3. Only `Root` and `pelvis` keep translation; every other bone's position and scale is held at the target's rest, and
   `Root`'s horizontal travel is zeroed because the controller owns movement.
4. **Feet are planted by measurement**: the clip is played on the target skeleton, the lowest of `foot_l/r` and
   `ball_l/r` is read per frame, and `Root` carries the difference back to that bone's rest height. On every grounded
   clip the result is a lowest foot of exactly 0.0222 m (the rig's own rest value) at every sampled frame. Clips that
   are airborne for their whole length (`jump_start`, `fall`, `land`, `death`) are not planted.
5. Gait paces are measured on the target (walk 1.389, run 2.893, sprint 4.136, crouch_walk 0.458 m/s) and stored as the
   library's `motion_paces`, with foot-plant phases in `motion_phases` and each body's own GLB SHA-256 in
   `target_body_sha256`, which the loader checks before using a library.

**What this arm does not have, measured rather than assumed:** the source clips are MetaHuman-native, so limb lengths
differ from the Human Generator body and nothing here solves IK. The foot planting puts the contact point exactly on
the ground, and the *pelvis* absorbs the difference: the walk bobs 0.185 m where the source bobs 0.06 m, and
`crouch_idle` resolves into a deep squat (pelvis 0.038 m). Epic's IK Retargeter — what the MPFB arm used — is the tool
that removes this, and it needs the Human Generator rig imported into UE first. A global-frame variant of the rotation
re-base was tried and rejected: it folded the legs up by 0.5–1.1 m on every clip.
