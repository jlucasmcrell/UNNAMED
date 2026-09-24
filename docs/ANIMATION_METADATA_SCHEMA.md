# ANIMATION METADATA SCHEMA (v1)

The authoritative animation registry. Implements section 19 of
`ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md`, which sketches this metadata and notes
"the exact schema is future work".

Implemented by `tools/asset_pipeline/_animation_registry.py`.

---

## Why a registry rather than clip names

Section 64 states the rule this schema exists to satisfy:

> **Do not bind authoritative combat logic directly to arbitrary imported clip names.
> Use stable animation IDs/metadata.**

So a clip's identity is an **id in a registry**, not a filename inside a GLB. Combat logic
refers to `anim.humanoid.polearm.thrust_01` and its declared event times. The GLB carrying that
clip can be re-sourced, re-exported or replaced without touching a line of gameplay code, and
a clip can be validated **before** any motion exists.

---

## Where it lives

```
assets/animation/
  clips/         one JSON per clip, filename == animation_id + .json
  source/        motion sources: purchased, mocap, ai, video, manual
  rigs/          one folder per canonical skeleton family
  blender/       work/ scratch and canonical/ sources
  ready/         exported animation-only GLBs, by delivery class
  manifests/     per-batch records
  review/        contact sheets and previews
  rejected/      clips that failed QA, with the reason
```

## The schema

```json
{
  "schema_version": "1",
  "animation_id": "anim.humanoid.polearm.thrust_01",
  "skeleton_family": "humanoid_standard",

  "category": "combat",
  "animation_family": "polearm",
  "clip_type": "attack",

  "loop": false,
  "root_motion": true,
  "duration_s": 0.82,

  "events": [
    { "id": "windup_start",     "time": 0.0  },
    { "id": "attack_commit",    "time": 0.21 },
    { "id": "hit_window_start", "time": 0.29 },
    { "id": "hit_window_end",   "time": 0.47 },
    { "id": "recovery_start",   "time": 0.52 }
  ],

  "hand_profile": { "left": "grip_secondary", "right": "grip_primary" },
  "tags": ["two_hand", "thrust", "forward_commit"]
}
```

## Id grammar

```
anim.<skeleton-or-creature>.<family>.<name>
```

Lower-case, dot-separated. Enforced by regex, not convention, because the id **is** the
contract the game binds to.

```
anim.humanoid.locomotion.walk_forward
anim.humanoid.polearm.thrust_01
anim.kal.locomotion.glide_brake
anim.kal.wing.deploy
anim.vaskaal.locomotion.walk
anim.mor.locomotion.drift
anim.weapon.telescoping_staff.deploy
anim.fps.sword.light_01
```

## Controlled vocabularies

| Field | Values |
|---|---|
| `skeleton_family` | `humanoid_standard`, `kal_compact_winged`, `vaskaal_tall_articulated`, `ondrek_heavy`, `constructed_standard`, `mor_special`, `creature_quadruped`, `creature_winged`, `mechanical` |
| `category` | `locomotion`, `combat`, `interaction`, `magic`, `traversal`, `social`, `profession`, `facial`, `death`, `mechanical` |
| `clip_type` | `idle`, `move`, `transition`, `attack`, `defend`, `react`, `cast`, `interact`, `emote`, `death`, `deploy`, `state` |
| `hand_profile.*` | `grip_primary`, `grip_secondary`, `grip_flat`, `grip_vaskaal`, `open`, `point`, `fist`, `none`, `support` |

**The hand profiles deliberately reuse the socket roles from the modular asset standard**, so
a clip's declared grip and the weapon socket it attaches to are spelled the same way.

## Events are gameplay timing, not frame numbers

Section 20: *"Do not make gameplay logic depend purely on 'frame 17' hard-coded in a script.
Use animation event metadata."*

Event ids come from a **closed vocabulary**, because an event nobody consumes is drift and an
event nobody declares becomes a hard-coded frame number somewhere:

```
windup_start, attack_commit, hit_window_start, hit_window_end, recovery_start,
projectile_release, ammo_consumed, block_active_start, block_active_end,
foot_contact, foot_leave, deploy_lock_complete, spell_release, effect_spawn,
sound_cue, interaction_point, grip_release, grip_acquire,
root_motion_start, root_motion_end
```

Timing is in **seconds**, not frames, so a clip can be played at a different rate — section 54
allows bounded playback scaling — without invalidating its metadata.

## Validation rules enforced

Structural, before any motion exists:

1. `animation_id` matches the grammar, and the filename matches the id
2. `schema_version` is present and current
3. `skeleton_family`, `category`, `clip_type` are in their vocabularies
4. `loop` and `root_motion` are booleans
5. `duration_s` is a positive number
6. every event id is in the vocabulary
7. every event time is within `0..duration_s`
8. events are **ordered** by time
9. a `combat` / `attack` clip declares `attack_commit` or `hit_window_start` — an attack clip
   with no commit cannot drive combat timing
10. `hand_profile.left` and `.right` are known profiles
11. `tags` is a list

## Verification

The validator was **negative-tested**, not just run against valid input — a validator that
passes everything is worse than none:

| Injected fault | Caught |
|---|---|
| combat attack with no commit/hit event | ✅ |
| events out of order | ✅ |
| event time beyond duration | ✅ |
| unknown event id | ✅ |
| id grammar violation | ✅ |
| unknown skeleton family | ✅ |
| unknown hand profile | ✅ |
| negative duration | ✅ |

**8 of 8 invalid clips rejected.**

## Status

**Built:** schema, validator, folder layout, two reference clips
(`anim.humanoid.locomotion.walk_forward`, `anim.humanoid.polearm.thrust_01`).

**Not yet built:** motion sourcing, the Blender retarget/bake tooling, animation-only GLB
export, and the Godot `AnimationTree` proof. Section 12 requires all of those before
Animation Wave 0 passes. The registry exists first on purpose — it is what the Blender side
writes against, and it makes clips validatable before the expensive work happens.
