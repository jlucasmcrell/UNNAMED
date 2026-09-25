# WAVE 0 — Modular Asset Standard

The production contract every modular asset must satisfy. Written before mass production
of Wave 1/2, because a component that does not fit its neighbours is not a component.

**Status:** approved, not yet demonstrated. No Wave 1/2 GLB may be mass-produced until the
proof set in section 12 passes.

**Scope note (refinement 1).** The existing **250 ready assets are compliant with the base
standard** - metres, Y-up, base at ground, centred, transforms applied, single-sided, four
PBR maps, dimensionless LODs, collision proxies. They are **not yet compliant with the
modular-interface standard**, because they carry no sockets, no asset-id naming and no
provenance metadata. They require **extension, not regeneration.**

---

## 0. Why this exists

The existing pipeline was built for **individual finished assets** — creatures, props,
weapons as whole objects. It normalises each one independently by scaling its longest axis
to a category default. That is correct for a standalone barrel and wrong for a mace head
that must seat on a haft, because independent longest-axis scaling destroys the absolute
dimensions that make two parts fit.

Measured failure this week: `race2_`, `racebody_` and `raceclass_` were absent from the
category table, so seven character assets silently built at **0.5 m instead of 1.8 m**.
The build succeeded, verification passed, and the assets were wrong. Nothing failed loudly
because the category was *guessed* rather than *declared*.

Both faults have the same root: **the pipeline infers interface properties it should be
told.** Wave 0 replaces inference with declaration.

---

## 1. What already exists (do not reinvent)

Confirmed by inspecting the live library, not assumed:

| Concern | Existing convention | Source of truth |
|---|---|---|
| Units | metres | `_blender_cleanup.py` |
| Up axis, exported | **Y-up** | `export_yup=True` |
| Base | sitting on the ground plane | `normalize_transform()` |
| Footprint | centred on X and Z | `normalize_transform()` |
| Transforms | applied before export | `apply_transforms()` |
| Materials | single-sided (`use_backface_culling`) | `force_single_sided()` |
| PBR maps | baseColor, metallicRoughness, normal, occlusion | `_verify_glb.py` |
| Asset id | `<category>_<subject>[_<variant>]` | request files |
| File naming | `<stem>.glb`, `<stem>_lod1..3.glb`, `<stem>_collision_hull.glb`, `<stem>_collision_box.glb`, `<stem>_meta.json` | `_blender_cleanup.py` |
| Per-asset schema | `source`, `name`, `category`, `target_size_m`, `transform`, `source_faces`, `base`, `lods`, `collision`, `outputs` | `_cleanup` meta.json |
| Rig schema | `asset`, `plan`, `method`, `bones`, `bones_with_weights`, `unweighted_vertices`, `max_influences` | `_blender_rig.py` |
| Rig plans | `humanoid`, `quadruped`, `worm` | `_blender_rig.py` |
| Category sizes | weapon 1.2, tool 0.6, prop 0.5, creature 1.8, character 1.8, building 4.0 | `_blender_cleanup.py` |

**Verified live:** `race_veth_representative` extent `[0.6867, 1.8, 0.3979]` (longest Y,
base at Y=0) and `weapon_viking_rune_axe` extent `[1.2, 1.1633, 0.2662]` (longest X).

Everything below extends this. None of it replaces it.

---

## 2. Coordinate and transform conventions

Adopt the existing contract and state it explicitly so modular parts can rely on it.

| Property | Convention |
|---|---|
| Units | metres, real-world scale |
| Handedness | right-handed (glTF 2.0) |
| Blender authoring | Z-up, −Y forward (Blender default) |
| GLB export | **Y-up**, `export_yup=True` |
| In-engine up | +Y |
| In-engine forward | **−Z** (glTF convention) |
| Object origin | footprint centre, base on the ground plane |
| Pivot | origin by default; **overridden per socket family** for modular parts |
| Transform application | all transforms applied before export; no residual scale or rotation |

### The modular exception — absolute sizing

**Independent longest-axis normalisation must not be used for modular components.**

For a component with an interface, the size is not a category default, it is a
**specification**. A mace head is 0.11 m tall because it must seat in a 0.11 m socket, not
because props are 0.5 m.

So every interface component declares:

```
"nominal_size_m": [width, height, depth]
```

and cleanup **scales to the nominal size, not to the longest axis**. A component whose
longest axis is not the declared axis still scales by the declared dimension.

`--category auto` stays valid for standalone assets. Interface components must declare
their size explicitly or the build fails.

---

## 3. Naming conventions

Stable, machine-readable, independent of display names.

| Thing | Pattern | Example |
|---|---|---|
| Asset id | `<category>_<subject>[_<variant>]` | `armour_gorget_plate_a` |
| Blender collection | `AST_<asset_id>` | `AST_armour_gorget_plate_a` |
| Blender object | `<asset_id>` | `armour_gorget_plate_a` |
| Mesh datablock | `<asset_id>_mesh` | `armour_gorget_plate_a_mesh` |
| Material | `MAT_<asset_id>_<map>` | `MAT_armour_gorget_plate_a_base` |
| Armature | `RIG_<asset_id>` | `RIG_race2_kal_representative` |
| Socket / attachment | `SOCK_<interface>_<role>[_<side>]` | `SOCK_grip_primary`, `SOCK_hand_R` |
| Exported GLB | `<asset_id>.glb` | `armour_gorget_plate_a.glb` |
| LOD variant | `<asset_id>_lod<n>.glb` | `armour_gorget_plate_a_lod2.glb` |
| Collision | `<asset_id>_collision_hull.glb`, `_collision_box.glb` | as existing |
| Metadata | `<asset_id>_meta.json` | as existing |

**Every exported mesh must carry its asset id**, not `Mesh_0`. The current library exports
`Mesh_0` and `Material_0` for every asset, which makes an imported scene unreadable. Fixing
this is part of Wave 0.

---

## 4. Weapon interfaces

Canonical socket vocabulary. These are new; nothing in the current schema conflicts.

| Socket | Purpose |
|---|---|
| `SOCK_grip_primary` | main hand, single-handed grip |
| `SOCK_grip_secondary` | support hand on long hafts |
| `SOCK_head` | where a head, blade or striking component seats |
| `SOCK_pommel` | butt end, counterweights, caps |
| `SOCK_mechanism` | folding, telescoping, deploying parts |
| `SOCK_channel_focus` | magical or technical conduit |
| `SOCK_ammo` | projectile feed or nocking point |
| `SOCK_deploy` | transform pivot for mode changes |
| `SOCK_mount` | attachment to armour, shields or vehicles |

### Interface dimensions

### Socket schema — a complete local transform basis

A socket is **not** a point plus a longitudinal axis. A single axis cannot express roll, so
two parts could satisfy "grip axis aligned" and still seat rotated relative to each other.
Every socket therefore carries a full orthonormal local basis (refinement 2).

```json
"SOCK_grip_primary": {
  "position":  [0.0, 0.0, 0.0],
  "primary":   [0.0, 1.0, 0.0],
  "secondary": [0.0, 0.0, 1.0],
  "roll":      0.0,
  "depth":     0.02,
  "envelope":  0.030,
  "family":    "standard",
  "role":      "grip",
  "mate":      "antiparallel"
}
```

| Field | Meaning |
|---|---|
| `position` | socket origin in asset-local metres |
| `primary` | the mating axis; unit vector. **+primary points into the mating part** |
| `secondary` | a unit vector fixing roll; re-orthogonalised against `primary` on load |
| `roll` | rotation about `primary` in degrees, applied after the basis is built |
| `depth` | insertion depth in metres |
| `envelope` | the reference diameter or cross-section the mating part must fall within |
| `family` | grip/interface family (`standard`, `vaskaal`, …) |
| `role` | what the socket is for |
| `mate` | `antiparallel` for a joint, `aligned` for a continuation |

Blender object rotation is derived as the 3×3 matrix `[primary, cross, secondary]` with
handedness corrected, exported as a named empty. Nothing else about the socket is inferred.

**A mating pair is legal when:** the two origins are within `min(depth_a, depth_b)` of each
other along the shared axis, `primary_a · primary_b = −1 ± 0.017` (1°), and the basis
matrices agree after removing the 180° flip, i.e. the `secondary` vectors align within 1°.

### Reference envelopes, not shapes

The grip diameters are **interface envelopes, not a requirement that a visible grip be a
cylinder of exactly that size** (refinement 3). A grip may be ovoid, wrapped, waisted,
sculpted, or have a pommel flare; what matters is that the surface a hand closes around
falls within the declared envelope, and that the mating ends fit their sockets. Envelope is
a fit constraint on the interface region, not a statement about the whole part's form.

| Property | Rule |
|---|---|
| Grip axis | socket `primary`, default **+Y** toward the head |
| Socket orientation | has a full basis; `+primary` points into the mating part |
| Insertion depth | declared per socket, default `0.02 m` |
| Grip envelope | standard **0.030 m**; Vaskaal **0.036 m** — reference, not shape |
| Scale tolerance | **±0.5%** on interface features only; body features may vary freely |
| Hand position | hand centre at the grip socket origin, fingers wrapping `+primary` |

---

## 5. Armour interfaces and fit families

Preserving the doc's conceptual names where they already align, without turning them into
code contracts yet.

| Fit family | Applied to | Notes |
|---|---|---|
| `standard_humanoid` | Veth, Siann (scaled), Ondrek | the baseline; most armour is authored here |
| `compact_broad` | Kal | **plus dorsal wing channels** — see below |
| `tall_narrow` | Vaskaal | 2.2–2.5 m; own glove/hand fit |
| `irregular_heavy` | Ondrek heavy forms, large creatures | non-uniform silhouette |
| `modular_synthetic` | Constructed | socket-based, retractable |
| `nonphysical` | Mor | no conventional equipment |

### Body-region naming (16 regions, from the combat doc)

`head, face, neck, shoulder, chest, back, upperarm, elbow, forearm, hand, abdomen, groin,
thigh, knee, shin, foot`

Suffix `_L` / `_R` for paired regions. Midline regions take no suffix.

### Skeleton and skinning expectations

| Concern | Rule |
|---|---|
| Bone names | must match the rig plan's vocabulary (`_blender_rig.py`) |
| Armour origin | at the covered region's bone origin, not world origin |
| Layer offsets | underlayer 0.000, padding 0.008, mail 0.016, plate 0.024 m (outward) |
| Clipping tolerance | armour may not intrude more than 0.003 m into the body mesh |
| Symmetry | `_L`/`_R` mirrored from one source; never independently authored |
| Skinning | rigid armour binds to one bone; flexible layers may span two |

### Kal dorsal wings — explicit

Kal armour requires **wing clearance channels** running either side of the spine. The
contract:

- channel origin `SOCK_wing_channel_L` / `_R`, at `±0.06 m` from the spine line
- channel accepts the folded spar as a rigid part with its own `SOCK_mechanism`
- back plates and cloaks must not intersect the deployed wing envelope
- the deployed envelope is authored as a **reference volume**, not a mesh

### Vaskaal hands — explicit

`3 primary fingers + 2 opposing thumbs`. Every grip interface therefore takes a family
parameter:

```
SOCK_grip_primary  family=standard   diameter=0.030
SOCK_grip_primary  family=vaskaal    diameter=0.036   digits=3+2
```

Only **grips, guards, controls and hand interfaces** are Vaskaal-specific. Blades, heads,
shafts and mechanisms are shared.

---

## 6. Material and PBR standard

| Property | Rule |
|---|---|
| Maps | baseColor, metallicRoughness, normal, occlusion |
| Textures | **2048²** default; 1024² for small components; 4096² only for hero characters |
| Metal/rough packing | glTF convention: occlusion R, roughness G, metallic B |
| Colour space | baseColor sRGB; normal, roughness, metallic, AO linear |
| Material naming | `MAT_<asset_id>_<map>`; one material per asset unless a layered asset requires more |
| Transparency | avoid; if required, declare `alphaMode` explicitly |
| Emissive | only where the fiction requires it; declare emissive strength |
| Single-sided | yes, existing convention retained |

---

## 6b. Transform-mechanism families

Six families (refinement 9 adds sliding). Each is a **declared mechanism**, so the
deployment pivot, collision states and animation needs are recorded rather than improvised.

| Family | Motion | Example |
|---|---|---|
| `telescoping` | axial extension along `primary` | collapsing haft, extending spear |
| `folding` | rotation about a fixed hinge | Kal wing, folding crossbow arm |
| `retractable_blade` | blade withdrawn into a housing | concealed dagger, staff blade |
| `detachable_component` | full separation, re-mount allowed | bayonet, modular head |
| `rotating_head` | indexed rotation about one axis | multi-mode polearm head |
| `deploy_aperture` | an opening that presents a chamber | focus chamber, ammo gate |
| **`sliding_rail`** | **translation constrained to a linear rail, not axial** | **foregrip adjustment, adjustable sight, sliding counterweight** |

`sliding_rail` differs from `telescoping` in the important way: telescoping changes the
part's *length*, sliding moves a part *along* a track without changing its own dimensions.
The two need different bones and different collision behaviour.

Each mechanism records:

```
"mechanism": {
  "family": "sliding_rail",
  "pivot_socket": "SOCK_mechanism",
  "track": {"axis": [0,1,0], "min_m": 0.0, "max_m": 0.14, "indexed": [0.0, 0.07, 0.14]},
  "bone": "mech_slide",
  "collision_states": ["stowed", "deployed"],
  "state_attachments": {"stowed": {"SOCK_grip_primary": [0.0, 0.02, 0.0]},
                        "deployed": {"SOCK_grip_primary": [0.0, 0.16, 0.0]}}
}
```

`state_attachments` is what allows a transforming weapon to expose **mode-dependent grip
positions** (refinement 8) — the grip socket is not a fixed point for a transforming part.

## 6c. LOD plan — assembled, not per-component

**Tiny components do not get independent LOD chains** (refinement 10). A three-level
reduction of a 2,000-triangle gorget costs more to produce than it saves.

The expected runtime optimisation is **item-level or cached assembled LODs**: assemble the
legal combination once, then generate LODs from the *assembled* result. Component LODs are
generated only where a component is large enough to justify them, or where it is used
standalone in the world.

| Case | LOD strategy |
|---|---|
| Small component (<4k tris) | none |
| Large component (>10k tris) | own chain |
| Assembled item | **one LOD chain generated from the assembly** |
| Hero character | own chain |
| Worn armour set | LOD derived from the character LOD at each tier |

---

## 7. Geometry standards

Proposed budgets. These are **targets for Wave 0 validation**, to be revised once the proof
set measures what Trellis actually produces at each scale.

| Category | Triangles | LODs | Notes |
|---|---|---|---|
| Hero character body | 30–45k | 3 | rigged, deformable |
| Character body base | 25–40k | 3 | rigged, no equipment |
| Armour component | 2–8k | 1–2 | small; LOD chain often pointless |
| Weapon component | 1–6k | 1–2 | heads and fittings |
| Whole weapon | 8–20k | 2 | assembled |
| Small prop | 3–10k | 2 | |
| Large prop | 10–30k | 3 | |
| Building module | 5–25k | 2–3 | |
| Background clutter | <2k | 0 | no LOD chain |

**Small components get no LOD chain.** Generating a 3-level reduction for a 2,000-triangle
gorget costs more than it saves.

| Concern | Rule |
|---|---|
| Normals | smooth where the surface is smooth; hard edges preserved on faceted parts |
| Manifold | preferred, not required — Trellis output is not reliably manifold |
| Holes | filled where they would be visible; interior geometry removed |
| Loose geometry | removed (existing `loose_verts_removed`) |
| UVs | one atlas, non-overlapping, existing `UnwrapMesh` output |
| Interior geometry | stripped from LODs and collision |

---

## 8. LOD standard

| Category | LOD count | Reduction targets |
|---|---|---|
| Hero character | 3 | 100% / 30% / 10% / 3% |
| Character base | 3 | as above |
| Armour component | 0–1 | skip below 4k triangles |
| Whole weapon | 2 | 100% / 35% / 12% |
| Prop | 2 | 100% / 30% / 10% |
| Building module | 3 | 100% / 40% / 15% / 5% |
| Clutter | 0 | none |

LODs carry their **material, UVs and textures** (downscaled per level: 1024 / 512 / 256 by
default). The earlier convention of stripping them (`strip_surface_data()`, `export_materials=NONE`)
left all 1,455 LODs in the library drawing untextured, and the decimation on the unwelded mesh
cracked them; it was retired in the 2026-09 asset remediation. `_verify_glb.py` now fails a drawn
file (base or LOD) with no material, no texture, an unbound primitive, a bad material slot or no
UVs; only collision proxies are geometry-only.

---

## 9. Collision standard

| Case | Collision |
|---|---|
| Static world object | convex hull + box (existing) |
| Building module | box only |
| Armour component | **none** — it is skinned to a body |
| Weapon component | **none** unless used as a world object |
| Held weapon | none; the hitbox is authored in the combat system |
| Small prop | box only |
| Clutter | none |

Rule: **collision is for things the player or physics touches as world geometry.** A
gorget the player wears never needs a hull.

---

## 10. GLB export requirements

A "production-ready GLB" means all of:

- correct real-world scale in metres
- Y-up, base at ground, footprint centred
- all transforms applied, no residual scale or rotation
- **mesh named after the asset id**, not `Mesh_0`
- **material named after the asset id**, not `Material_0`
- PBR maps embedded with correct colour space
- no unused nodes, no orphan meshes, no cameras or lights
- expected armature and skin weights where applicable
- expected sockets present as named empty nodes where the pipeline exports them
- imports into Godot without warnings

The current library fails the naming requirement. That is a Wave 0 fix.

---

## 11. Automation that already enforces the standard

| Requirement | Enforced by | Status |
|---|---|---|
| Metres, base on ground, centred | `_blender_cleanup.py normalize_transform()` | exists |
| Transforms applied | `_blender_cleanup.py apply_transforms()` | exists |
| Single-sided materials | `_blender_cleanup.py force_single_sided()` | exists |
| Y-up export | `export_yup=True` | exists |
| LOD generation | `_rebuild_lods.py` (from the cleaned base; `_make_assets.py` Stage 2b) | replaced 2026-09 |
| Collision proxies | `_blender_cleanup.py make_collision()` | exists |
| LODs carry material, UVs and textures | `_verify_glb.py` (drawn files), `_verify_pack.py` (every LOD) | enforced 2026-09 |
| Structural verification | `_verify_glb.py`, `_verify_pack.py` | exists |
| Rigging and weight reporting | `_blender_rig.py` | exists |
| Category sizing | `_make_assets.py infer_category()` | exists, **just fixed** |
| Visual inspection | `_blender_preview.py` | exists |
| GLB axis inspection | `_inspect_glb_axes.py` | exists |

## 11b. Automation still needed

| Requirement | Needs |
|---|---|
| **Socket creation and export** | new: author named empties at declared positions, export as nodes |
| **Absolute sizing for components** | new: scale by declared nominal size, not longest axis |
| **Asset-id mesh/material naming** | new: rename `Mesh_0` / `Material_0` on import |
| **Fit-family validation** | new: assert a component's family matches its target body |
| **Socket alignment verification** | new: assert two parts' sockets coincide within tolerance |
| **Body-fit reference skeletons** | new: canonical body reference per fit family |
| **Clipping / penetration test** | new: measure armour intrusion into a body reference |
| **Nominal-size table** | new: machine-readable component dimension registry |
| **Provenance manifest** | extend: the fields in section 13 |
| **Godot import validation** | new: headless import check |

---

## 12. The proof set

Approximately 15 assets, chosen to exercise every interface in the standard. **No Wave 1/2
mass production until this passes.**

| # | Asset id | Exercises |
|---|---|---|
| 1 | `weaponcomp_mace_head_flanged_a` | head socket, nominal size |
| 2 | `weaponcomp_haft_short_a` | short haft, grip socket both ends |
| 3 | `weaponcomp_haft_long_a` | long haft, primary + secondary grip |
| 4 | `weaponcomp_pommel_counterweight_a` | pommel socket |
| 5 | `weaponcomp_blade_arming_sword_a` | blade, head socket, tang |
| 6 | `weaponcomp_shield_heater_a` | mount socket, no collision |
| 7 | `armour_chest_plate_base_a` | standard fit, layer 0.024 |
| 8 | `armour_chest_underlayer_gambeson_a` | standard fit, layer 0.008 |
| 9 | `armour_gorget_plate_a` | gap piece, midline, no LOD |
| 10 | `weaponcomp_grip_standard_a` | standard grip, 0.030 m |
| 11 | `weaponcomp_grip_vaskaal_a` | Vaskaal grip, 0.036 m, 3+2 digits |
| 12 | `armour_kal_back_channel_a` | wing channel, `compact_broad` |
| 13 | `magiccomp_focus_crystal_a` | focus/channel socket |
| 14 | `weaponcomp_mechanism_telescope_a` | deploy socket, transform state |
| 15 | `weapon_hybrid_focus_staff_spear_a` | assembled hybrid, two modes |

### Combinations that must assemble legally

1. mace head on **both** haft lengths
2. chest plate **over** gambeson **plus** gorget
3. **one shared weapon component mounted to both the standard and Vaskaal grip families**
4. **Kal wing armour through folded → deployment → deployed states**
5. focus staff extended to spear, with **mode-dependent grip positions and collision
   profiles**

Items 3, 4 and 5 are the added validations (refinement 8), and each tests something the
others do not:

| Test | What it proves |
|---|---|
| 3, shared component, two grip families | a single blade or head genuinely serves both morphologies. The entire "do not duplicate the weapon catalogue" decision depends on this passing. |
| 4, Kal folded → deployment → deployed | a `folding` mechanism with armour attached to a *moving* structure, and that the wing envelope does not intersect back armour in any state |
| 5, transforming staff/spear | `state_attachments` works, so the grip socket moves between modes, and collision differs per mode |

### Validations, not impressions

For each: socket origin coincidence within depth, **full basis alignment and not just the
primary axis**, absolute scale, clipping into the body reference, Blender transform state,
material and colour space, GLB export, **Godot import**, and rig compatibility for worn
pieces.

### Godot import validation is part of done

Verified in the actual engine, using existing project conventions and without modifying M2
gameplay or persistence code:

- correct real-world scale
- correct orientation (up and forward)
- expected materials and correct colour space
- expected rig, and bones resolving
- socket nodes present, named, and positioned as authored
- no import warnings
- transform animation where applicable
- collision behaviour where applicable

---

## 13. Provenance metadata

Extend the existing meta.json rather than replacing it. New fields:

``**From the beginning, every modular asset carries modular_interface_version**
(refinement 4). It is the contract version it was built against, so a later change to the
socket schema can identify which assets need migration rather than invalidating everything.

`
asset_id, category, modular_interface_version, concept_source, spec_version, race_culture,
generation_model, generation_machine, generation_date,
concept_path, raw_3d_path, blender_path, glb_path,
status, fit_family, socket_family,
texture_resolution, triangles, rigged, lod_status, collision_status,
godot_validated, license_notes
```

**Status values:** `concept`, `concept_approved`, `generated_raw`, `blender_cleanup`,
`interface_normalized`, `rigged`, `export_ready`, `godot_validated`, `rejected`,
`superseded`.

Never overwrite an approved source concept silently; mark the old one `superseded` and
record why.

---

## 14. Blender remains the production truth layer

Trellis output is source geometry, not a product. Blender owns: scale and orientation
normalisation, origin and pivot, topology cleanup, decimation, normals, UV inspection, PBR
assignment, socket authoring, rigging, weight painting, collision proxies, LOD generation,
naming, and GLB export. Every deterministic step must be scripted, not hand-corrected.

## 15. Engine-neutral sources

Truth chain: `concept → raw generation → Blender source → GLB + PBR textures`. Godot
imports are a validation step, never the only surviving copy.

## 16. Production readiness is not generation time

Track separately: concept generated, raw 3D generated, Blender normalised, production
cleaned, rigged, interface validated, GLB exported, Godot validated. "Trellis finished" is
not "asset complete".

## 17. The reuse gate (owner directive, 2026-09-25)

The asset library is produced by families, not one asset at a time. Before any asset is produced it is classified in
`assets/manifests/asset_production.json` as one of:

1. **template_variant**: an instance of an existing family or template, made by changing dimensions, components,
   materials or skins.
2. **new_archetype**: a new reusable family worth a template.
3. **reconstruction_unique**: a unique silhouette suited to image-to-3D reconstruction plus the shared pipeline.
4. **bespoke_hero**: a genuinely unique hero asset that no family can express.

Rules:

- No new one-off procedural generator unless the asset is genuinely unique (class 4) and cannot reasonably be
  expressed through an existing family or template.
- Every generator, template or bespoke, takes its baking, material, UV, validation and export machinery from the common
  library (`tools/asset_pipeline/procgen_lib`), never from a copy. `_check_reuse_gate.py` fails a generator that
  re-implements it. The ten generators written before the gate are listed as legacy until ported; the list only shrinks.
- A new archetype records the production cost (asset-specific lines, agent tokens, wall minutes) of its first and
  second assets. The second must be materially cheaper (at most half the lines and tokens); if it is not, the archetype
  is `not_yet_reusable` and its template is improved before a third asset.
- The image-to-3D path takes only classes 3 and 4 (`_make_assets.py` refuses the rest); concepts are generated only for
  classified assets (`_make_concepts.py`). Both call `_reuse_gate.py`.
- The 163 unbuilt 3D concepts of 2026-09-25 (concept images with no model, excluding `icon_` and `material_`) are
  frozen until clustered into reusable production families; a cluster entry marked `released` unfreezes its members.
  Finishing Phase 1 does not unfreeze them.
