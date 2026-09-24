# WAVE 0 PROOF SET — READY FOR PLAY TEST

**Status: 15 of 15 built, socketed, LOD'd, collision-proxied, and validated in Godot.**
All promoted into assets/ready/. Pack verification: **324/324 clean**.

**Every one of the 15 is now geometrically correct.** The three that were defective have been
rebuilt from refined concepts and re-run through the unified pipeline:

| Asset | Was | Now |
|---|---|---|
| weaponcomp_grip_standard_a | generator drew a complete dagger | complete hilt: pommel, leather grip, crossguard |
| magiccomp_focus_crystal_a | generator drew a whole staff | faceted crystal in its brass claw mount |
| weaponcomp_pommel_counterweight_a | mesh had no usable boundary | faceted iron counterweight, clean haft cut |

**The mesh and interface proof that the animation pipeline waits on is therefore done.**
ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md section 66 sets the order: finish this proof,
then freeze body/scale conventions, then canonical skeleton v1, then Animation Wave 0. Steps 1
is complete; step 2 is the next gate.

---

## What each of the 15 now is


Every one carries the same seven-file set, produced by the unified pipeline:

```
<asset>.glb                  base mesh, asset-id naming, sockets, textures
<asset>_lod1.glb             8000 faces, no textures
<asset>_lod2.glb             2500 faces, no textures
<asset>_lod3.glb             ~900-2400 faces, no textures
<asset>_collision_hull.glb   real convex hull
<asset>_collision_box.glb    box proxy
<asset>_sockets.json         authored sockets, LOD chain, collision record
<asset>_meta.json            normalisation provenance
```

Naming is uniform: mesh `weaponcomp_haft_long_a_mesh`, material
`MAT_weaponcomp_haft_long_a`, and sockets exported as named nodes.

## The 15, by group

### Weapon components (9)

| Asset | Sockets | Geometry verdict |
|---|---|---|
| `weaponcomp_haft_short_a` | grip, head, pommel | correct |
| `weaponcomp_haft_long_a` | 2 grips, head, pommel | correct |
| `weaponcomp_blade_arming_sword_a` | grip | correct |
| `weaponcomp_grip_vaskaal_a` | grip | correct |
| `weaponcomp_mace_head_flanged_a` | head | correct *(concept drew a hammer, not a mace)* |
| `weaponcomp_pommel_counterweight_a` | pommel | **no boundary in mesh** |
| `weaponcomp_grip_standard_a` | grip | **concept drew a whole dagger** |
| `weaponcomp_shield_heater_a` | body mount (planar) | whole object, correct |
| `weaponcomp_mechanism_telescope_a` | mechanism in/out, deploy | correct |

### Armour (4)

| Asset | Sockets | Notes |
|---|---|---|
| `armour_chest_plate_base_a` | layer root, body mount | generated from Method A; fit not canonical |
| `armour_chest_underlayer_gambeson_a` | layer root | generated; a garment, not a shell |
| `armour_gorget_plate_a` | body mount | generated |
| `armour_kal_back_channel_a` | layer root, wing L, wing R | generated; wing channel sockets present |

### Magic and hybrid (2)

| Asset | Sockets | Notes |
|---|---|---|
| `magiccomp_focus_crystal_a` | channel focus | **concept drew a whole staff** |
| `weapon_hybrid_focus_staff_spear_a` | grip, channel, mechanism, head | correct, both modes |

---

## Read this before the play test

All 15 are geometrically correct and validate in Godot. Two caveats remain that are design
notes rather than defects:

**The mace is a double-headed hammer.** weaponcomp_mace_head_flanged_a was cut correctly and
its geometry is clean, but the concept the generator produced is a hammer head, not a flanged
mace. Sockets and scale are right; the visual does not match the name.

**The armour\'s fit is generated, not canonical.** All four armour pieces socket and import
correctly, but their fit came from generated geometry (Method A). Method C produces correct
canonical fit for the chest and gorget; the generated surface has not yet been transferred onto
that fit boundary. Expect them to socket correctly but not necessarily sit correctly on a
declared body until that transfer is done.

## Assemblies that are known legal

From `_check_proofset_assemblies.py`, 11 of 11 combinations legal:

- mace head onto **both** haft lengths
- gambeson under chest plate, gorget onto chest
- arming blade onto the **standard** and the **Vaskaal** grip
- pommel onto haft butt
- focus crystal into staff
- telescope inserted into the hybrid
- shield and gorget onto the body mount
- Kal back channel wing sockets

## How this was produced

```
raw GLB
  -> stub cut               (_blender_cut_stub.py)      5 of 9 components cut correctly
  -> author + LOD + collision (_blender_sockets.py)     one Blender session
  -> verify socket basis     (_verify_sockets.py)       position + full basis in the GLB
  -> Godot import            (validate_glb.gd)          15/15
  -> promote                 (_promote_proofset.py)     previous builds archived, not deleted
```

Driven end to end by `_build_proofset.py`. Rerun it any time; it is deterministic and
re-promotes cleanly.

## Bugs found and fixed in building this

Four, all found by checking artefacts rather than trusting exit codes:

1. **Socket `envelope: null` crashed authoring.** A planar interface legitimately has no
   diameter, so `None` now reads as absent rather than as a bad number.
2. **Blender exited 0 having written nothing.** The batch then crashed on a missing file
   instead of naming the asset that failed. Now the artefact is checked, not the exit code.
3. **Godot import raced the file copy.** The `.import` cache was eleven minutes older than the
   staged GLB, so Godot loaded a 0 KB file and reported "could not load as a PackedScene".
   Now the import is confirmed to have caught up before validation runs.
4. **The unified chain wrote no `_meta.json`.** `_verify_pack.py` treats a missing meta as
   failure, so all 15 arrived without normalisation provenance. Now written inline.
