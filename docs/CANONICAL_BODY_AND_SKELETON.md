# CANONICAL BODY AND SKELETON CONTRACT (v1)

**Status: built and verified.** This is step 2 of the animation pipeline order —
*freeze character scale and body-reference conventions* — and the gate that armour fit,
rigging and retargeting all depend on.

Produced by `_blender_canonical_body.py`. Outputs live in `assets/rigs/<fit_family>/`.

---

## Why this is declared, not generated

A generated body differs every run, so armour fitted to one body fits nothing else. The
armour bakeoff established this the hard way: Method B, which fitted armour to a generated
mannequin, produced armour **welded to an arbitrary body** and had to be rejected.

So the reference body is a **specification**: landmark dimensions are declared constants,
and every character using a fit family is authored against the same reference. Armour authored
once then fits every character in that family.

---

## The four families built

| Fit family | Height | Shoulder width | Note |
|---|---|---|---|
| `standard_humanoid` | 1.80 m | 0.42 m | Veth, Siann, Orenth, ordinary NPCs |
| `compact_broad` | 1.30 m | 0.58 m | Kal — broad and short, dorsal wing channels |
| `tall_narrow` | 2.35 m | 0.34 m | Vaskaal — tall, extremely lean |
| `irregular_heavy` | 2.60 m | 0.82 m | Ondrek and heavy forms |

Measured extents from the exported GLBs:

```
humanoid_standard    [1.1421, 1.7482, 0.2144]   arm span, height, depth
compact_broad        [1.0557, 1.3108, 0.2924]
tall_narrow          [1.3519, 2.2462, 0.2004]
irregular_heavy      [1.7600, 2.5260, 0.4288]
```

The span is wider than the body because the reference pose is an A-pose.

## The 24-bone canonical humanoid skeleton

```
root
  pelvis
    spine_01 -> spine_02 -> spine_03 -> chest
      neck -> head
      clavicle_l -> upperarm_l -> lowerarm_l -> hand_l
      clavicle_r -> upperarm_r -> lowerarm_r -> hand_r
    thigh_l -> calf_l -> foot_l -> toe_l
    thigh_r -> calf_r -> foot_r -> toe_r
```

Bone names are lower-case with `_l`/`_r` suffixes, deliberately matching the **armour coverage
region vocabulary** in the modular asset standard, so a bone and a body region are spelled the
same way. A piece of chest armour, a `chest` bone and a `chest` coverage region all use one
word.

Actual joint positions for `standard_humanoid`, in the **export frame**:

| Bone | Head (x, y, z) |
|---|---|
| root | 0.0, 0.0, 0.0 |
| pelvis | 0.0, 0.95, 0.0 |
| spine_01 | 0.0, 1.025, 0.0 |
| spine_02 | 0.0, 1.10, 0.0 |
| spine_03 | 0.0, 1.21, 0.0 |
| chest | 0.0, 1.32, 0.0 |
| neck | 0.0, 1.50, 0.0 |
| head | 0.0, 1.6925, 0.0 |
| upperarm_l | 0.1722, 1.45, 0.0 |
| lowerarm_l | 0.3808, 1.2414, 0.0 |
| hand_l | 0.5611, 1.0611, 0.0 |
| thigh_l | 0.088, 0.95, 0.0 |
| calf_l | 0.088, 0.50, 0.0 |
| foot_l | 0.088, 0.09, 0.0 |
| toe_l | 0.088, 0.0315, 0.153 |

Right-side bones mirror x. The exact set is written to `<family>_body_skeleton.json`
alongside each body, so nothing has to be read off this table by hand.

## Conventions, frozen

| Property | Value |
|---|---|
| Units | metres |
| Export frame | Y-up, base on the ground plane (Y=0), footprint centred |
| Forward axis | **−Z** |
| Up axis | **+Y** |
| Reference / bind pose | **A-pose, arms 45° down** |
| Skinning | automatic weights, verified present (`JOINTS_0`, `WEIGHTS_0`, one skin) |

These match the existing Wave-0 asset standard exactly, so armour authored against the
modular interface contract needs **no conversion** to fit these bodies.

**Never normalise a character by arbitrary longest-axis scaling.** Height and limb dimensions
are semantic — pipeline rule, from the animation doc section 52.

## What each output contains

```
assets/rigs/<fit_family>/
  <fit_family>_body.glb            reference body, skinned to the canonical skeleton
  <fit_family>_body_skeleton.json  bone list, positions, parentage, pose, units
assets/blender_src/<fit_family>_body.blend   canonical Blender source
```

Build report: 24 bones, 2,688 vertices, 2,560 faces, skinned, per family.

## What this unblocks, and what is still missing

**Unblocked:** armour can now be authored against a declared fit boundary instead of generated
geometry, which is the transfer the armour bakeoff identified as the remaining work.

**Still missing, in order:**

1. **The Method A → Method C surface transfer.** Method C produces correct canonical fit shells
   (`_blender_fit_shell.py`); Method A produces convincing generated armour. The generated
   surface has not yet been projected onto the canonical fit boundary. The four armour proof
   pieces still use generated fit.
2. **Weapon attachment points on the skeleton.** The animation doc calls for standardized hand
   IK targets, weapon attachment points and look/aim helpers. These 24 bones are the core body
   only.
3. **Kal wing bones.** `compact_broad` is the Kal body, but dorsal wings need their own chain.
   The armour standard already reserves `SOCK_wing_channel_L/_R`, and the skeleton needs
   matching bones before Kal is riggable.
4. **Vaskaal extra articulation.** The race spec calls for additional forearm and ankle joints
   and 3+2 digit hands. `tall_narrow` currently uses the standard 24-bone chain.

## Verification

```
humanoid_standard    bones=24  verts=2688  skinned=True
compact_broad        bones=24  verts=2688  skinned=True
tall_narrow          bones=24  verts=2688  skinned=True
irregular_heavy      bones=24  verts=2688  skinned=True
```

Rendered for inspection in `assets/review/rigs/fit_families.jpg` — all four read as distinct
humanoids with the intended proportions.
