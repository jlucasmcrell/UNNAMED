# Skeleton Contract Reconciliation

**Status:** Resolved, for owner review
**Date:** 2026-09-24
**Question:** Is the canonical humanoid skeleton 24 bones or 20, and do the NPCs need re-rigging?

## Answer

There are **three** different things, and the ambiguity came from treating them as one:

| # | What | Bones | Where it lives | Who uses it |
|---|---|---|---|---|
| 1 | **Canonical full humanoid** (`humanoid_standard_v2`) | **52** | `assets/rigs/<family>/body.glb` | armour fit boundary, player, equipment |
| 2 | **Core contract** | **24** | the `role: "core"` subset of #1 — *not a separate file* | the shared body chain everything humanoid agrees on |
| 3 | **Simplified fixed rig** | **20** | `assets/rigged/<id>/` via `_blender_rig.py --rig humanoid` | fixed-dress NPCs and humanoid creatures |

All three numbers are correct. They describe different things.

## 1. The canonical full humanoid: 52 bones

Verified by reading the assets, not the prose. All four family bodies are 52 bones:

```
compact_broad/compact_broad_body.glb          52 bones
humanoid_standard/humanoid_standard_body.glb  52 bones
irregular_heavy/irregular_heavy_body.glb      52 bones
tall_narrow/tall_narrow_body.glb              52 bones
```

and the player rig matches: `race2_veth_representative` and `race2_veth_bindpose` are 52 bones.

The composition is exact and cleanly separated by the contract's own `role` field:

```
core    24   root pelvis spine_01..03 chest neck head
             clavicle_l/r upperarm_l/r lowerarm_l/r hand_l/r
             thigh_l/r calf_l/r foot_l/r toe_l/r
finger  20   index/middle/ring/pinky 01..02 _l/_r  +  thumb 01..02 _l/_r
ik       8   IK_elbow_l/r  IK_hand_l/r  IK_knee_l/r  IK_foot_l/r
        ---
        52
```

The authoritative contract file is `assets/rigs/<family>/<family>_body_skeleton.json`, which declares
its own counts and is the thing to read rather than a status document:

```json
"skeleton_version": "humanoid_standard_v2"
"bone_count": 52
"core_bone_count": 24
"deform_bone_count": 44
"reference_pose": "A-pose, arms 45 degrees down"
"export_frame": "Y-up, base on ground plane, footprint centred"
"forward_axis": "-Z", "up_axis": "+Y"
"height_m": 1.8
```

### Weapon attachment points already exist

`CANONICAL_BODY_AND_SKELETON.md` lists "weapon attachment points on the skeleton" as still missing.
That is **no longer true**. The contract declares five equipment attachments:

```
SOCK_hand_l       parent hand_l    equipment_attach
SOCK_hand_r       parent hand_r    equipment_attach
SOCK_attach_back  parent chest     equipment_attach
SOCK_attach_hip_l parent pelvis    equipment_attach
SOCK_attach_hip_r parent pelvis    equipment_attach
```

These are attachments rather than bones, which is almost certainly why the doc and the contract
disagree: they are not in the bone list, so a check that counts bones does not see them.

## 2. The core contract: 24 bones

The 24 is the `role: "core"` subset of the 52, with these exact names:

```
root pelvis spine_01 spine_02 spine_03 chest neck head
clavicle_l clavicle_r upperarm_l upperarm_r lowerarm_l lowerarm_r hand_l hand_r
thigh_l thigh_r calf_l calf_r foot_l foot_r toe_l toe_r
```

There is no `*_core.glb` and no separate core contract file. The 24 exists as a *subset marker* on the
52-bone bodies, which is why a search for a 24-bone asset finds nothing.

## 3. The simplified fixed rig: 20 bones — and it is not a subset

The 20-bone rig used for NPCs is a **different skeleton**, not a reduced canonical one. The bone
names do not correspond:

| 20-bone simplified | 52-bone canonical |
|---|---|
| `hips` | `pelvis` |
| `spine` (one) | `spine_01`, `spine_02`, `spine_03` |
| `upper_arm.L` | `upperarm_l` |
| `forearm.L` | `lowerarm_l` |
| `shin.L` | `calf_l` |
| `shoulder.L` | `clavicle_l` |
| — | `toe_l/r`, 20 finger bones, 8 IK bones |

Two consequences follow, and they matter more than the bone count:

- **The NPCs cannot play canonical player animation.** Not because they have 20 bones instead of 24,
  but because the joint *names* differ, so every clip binds to nothing. This is a naming problem
  before it is a count problem.
- **The NPCs cannot wear canonical-fit armour** as authored against the fit boundary, for the same
  reason: the bones armour would skin to are absent or differently named.

## Do the NPCs need mass re-rigging?

**Not for M6, and not in this pass.** The maintenance brief asks me to resolve the ambiguity "without
churn", so the position is:

- For M6 as it stands, **no**. The NPCs are rigged, weighted (`0 unweighted vertices` each) and each
  family has its own animation clips that bind exactly. They are animation-capable today.
- Re-rigging becomes necessary **only if** a concrete need appears:
  1. an NPC must play *canonical player* animation (shared locomotion, shared combat), or
  2. an NPC must wear armour authored against the canonical fit boundary.

Neither is required for Phase-1 M6. Both are real future needs, and when either arrives the work is:
re-rig the affected NPCs onto `humanoid_standard_v2`, which the tooling already supports
(`_blender_canonical_body.py`, `_blender_bind_canonical.py`), then rebind their clips.

**Mass re-rigging is a stop-and-ask item** under the maintenance brief, and I am not starting it.

## What I corrected

`docs/CANONICAL_BODY_AND_SKELETON.md` contained two stale figures. Both are corrected in place with a
note, because a document that understates the skeleton is how this ambiguity started:

- "Build report: 24 bones … per family" and a verification block reading `bones=24` for all four
  families — the bodies on disk are **52**.
- "Weapon attachment points on the skeleton … still missing" — five `SOCK_*` equipment attachments are
  declared by the contract.

## How to check any of this

`_verify_character_skeleton.py` checks **one** body against **one** contract, and it is the right tool
for this question:

```powershell
python tools\asset_pipeline\_verify_character_skeleton.py `
  --glb W:\UNNAMED\assets\rigs\humanoid_standard\humanoid_standard_body.glb `
  --contract W:\UNNAMED\assets\rigs\humanoid_standard\humanoid_standard_body_skeleton.json
```

Its output for the standard family is the independent confirmation of everything above:

```
nodes    : 59   skins: 1   skin joints: 52
sockets  : 5 present at contracted positions
core     : 24 present, 24 within 0.012 m
fingers  : 20 deform bones, 20 carry skin weight
ik       : 8 helper bones, 8 correctly weightless
weighted : 37 joints actually influence the mesh
geometry : height 1.7482 m, base Y 0.0900, centre X 0.0000 Z 0.0000
RESULT: VERIFIED
```

Note what it confirms about the attachments: **5 sockets present at contracted positions**. They are
real, checked, and were simply invisible to a bone-counting check.

For the raw numbers on the family bodies:

```powershell
python -c "import json;d=json.load(open(r'W:\UNNAMED\assets\rigs\humanoid_standard\humanoid_standard_body_skeleton.json'));print(d['bone_count'], d['core_bone_count'], [a['name'] for a in d['attachments']])"
```

The verifier also surfaces something worth knowing before a character is built on this body: the
reference body's lowest vertex sits at **Y=0.0900, not 0**, because the block fit-figure has no
geometry below the ankle. A character mesh must reach the ground; the fit figure does not.

All numbers and namings above were read from those files during this pass.
