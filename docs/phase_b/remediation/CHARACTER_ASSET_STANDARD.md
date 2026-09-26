# Otherreach Character Asset Standard (humanoid archetypes, garments, faces)

Status: proposed at the Phase B remediation checkpoint (2026-09-26); owner approval pending. Tools: `tools/asset_pipeline/charstd/`.
This is an art/asset production contract. It does not define gameplay equipment slots or save data (M7/authoritative systems own
those); runtime code only selects prepared assets. It extends `docs/WAVE_0_MODULAR_ASSET_STANDARD.md` (naming, transforms, PBR,
LODs) for characters.

Producing a garment or a character by following this document needs **no new code** - only a descriptor, a preset, assets, and at
most a small authored edit.

## 1. Canonical body archetype

`humanoid_masculine_a` (`charstd/archetypes/humanoid_masculine_a.json`): the MakeHuman base mesh (MPFB 2.0.17, CC0) at neutral
macros (gender 1, all others 0.5), 1.80 m to the crown, with MPFB's high-poly eyes, eyebrow002, eyelashes01, teeth_base, tongue01,
the ARKit-52 face units and both viseme sets. Build it once:

```
blender -b --python charprod2/mpfb_base.py -- --spec charstd/archetypes/humanoid_masculine_a.json --out <dir>
blender -b --python charstd/standardize_rig.py -- --blend <dir>/mpfb_base.blend --out <dir>/archetype.blend
```

The archetype is the fitting reference for every garment. A character is an **instance**: its own macros (a controlled morph of
this body), identity preset, hair/brows/iris, skin, and an outfit. MPFB's helper geometry (tights, skirt, hair and joint helpers)
never ships.

## 2. Skeleton

`charstd/skeleton_humanoid_a.json`: MPFB's game-engine rig renamed one-to-one to the production vocabulary (root, hips, spine,
spine_mid, chest, neck, head, shoulder/upper_arm/forearm/hand .L/.R, three bones per finger and thumb, thigh/shin/foot/toe .L/.R),
plus `eye.L/.R` (at each eyeball's centre, the eyeball bound to it) and `SOCK_hand.L/.R` (grip frames on the knuckle line). Every
retarget map in `retarget_maps/*_prod.json` already targets these names.

## 3. Rest pose

MPFB's rest A-pose (arms ~45 degrees down). Garments are fitted and skinned in this pose; animation is retargeted onto it.

## 4. Scale

Metres; the instance's `height_m` (default 1.80) is the top of the head as rendered, applied as one uniform scale on the rig.
Morph ranges (archetype file): muscle 0.3-0.9, weight 0.3-0.8, proportions 0.3-0.9, age 0.35-0.8, height 1.68-1.92 m.

## 5. Body regions

`charstd/archetypes/humanoid_masculine_a.regions.json` - authored once (first pass by `author_regions.py`, then this file is the
authority; hand edits allowed). 27 regions, the combat doc's sixteen with sides, chest and back split at a collar plane:

`head face neck shoulder_L/R chest_upper chest back_upper back upperarm_L/R elbow_L/R forearm_L/R hand_L/R abdomen groin
thigh_L/R knee_L/R shin_L/R foot_L/R`

Check render: `docs/phase_b/remediation/characters/standard/body_regions.jpg`. Every instance shares the topology, so the map holds
for all of them.

## 6. Garment roles

Asset-production roles (not game slots): `top`, `bottom`, `footwear`, `gloves`, `headwear`, `outer_torso`, `shoulders`,
`robe_coat`, `accessory`. A descriptor may `replace` a role (the hide vest replaces the top - curated, no layering to manage).
Presentation maps an authoritative equipment slot to a garment in `art_bindings.json` (e.g. `item.armor.hide_vest`, chest slot ->
`torso_hide_vest_a`); it never becomes a second authority.

## 7. Garment fitting workflow

1. Pick a source (MPFB wardrobe, a sourced mesh, a generated mesh). Triage: A direct / B moderate / C reject (see below).
2. Write `charstd/garments/<id>.json` (template: any existing descriptor): archetype, role, layer, `hides`, source (kind, author,
   license, attribution), material, `offset_m` (standard layer offsets: padding 0.008, mail 0.016, plate 0.024), optional `cut`, and
   `authored_edits` (a list of what was done by hand and why).
3. `blender -b --python charstd/author_garment.py -- --archetype <archetype.blend> --id <id> --out <dir>` - fits it to the canonical
   body once (MPFB clothes fit by their own MHCLO; other sources are fitted in Blender by hand - shrinkwrap/cage - and saved as a
   .blend the descriptor names).
4. `blender -b --python charstd/garment_check.py -- --body <archetype.blend> --outfit <ids> --garments <dir> --out sheet.png` - rest
   views and stress poses (arms raised, arms forward, stride, crouch, twist) with the regions hidden exactly as the game hides them.
5. Fix what the sheet shows by editing the mesh in Blender or the descriptor - never by adding pipeline code for one garment.

Triage: **A** close fit - scale, material, weights, regions, done. **B** strong design needing a cage fit, some cleanup or UVs - use
it if the result justifies it. **C** needs reconstruction, repeated custom software or constant clipping fixes - reject and source
another.

Measured on this checkpoint: first garment (the tank) took most of a day because it doubled as the attempt at automatic coverage
(now retired); the second, visibly different garment (the hide vest: a CC-BY tunic + a CC0 leather) took a descriptor, one material
download and one authored edit (its `hides` list) - about 15 minutes and **no new code**; trousers and boots were descriptor-only.

## 8. Body hiding

A garment lists the regions it hides. The build removes those regions' faces; the runtime hides region meshes. No coverage is
inferred. If a boundary needs finer control, author a garment-specific region (add it to the regions file) - do not write code.
Example: the vest hides `abdomen, back` but not `chest` (it sits higher at the neck than the tank; hiding `chest` opened a gap -
`authored_edits` records it).

## 9. Skin-weight transfer

`common.skin`: weights copied from the body surface under the garment (Data Transfer, nearest face interpolated), limited to 4
influences, normalised; transferred **before** regions are hidden (a garment weighted from what is left takes the nearest visible
skin). Exceptional joints are cleaned by hand in the garment's .blend and recorded in `authored_edits`.

## 10. Morph strategy

Standard body + controlled morphs: MPFB macros within the archetype's ranges, and face modifiers within a 0.5 ceiling. Garments
follow the body's morph by `common.conform` (a Surface Deform bound on the canonical body, driven to the instance) at build time. The
runtime receives prepared meshes; a future creator would bake the same conform offline or ship per-garment corrective morphs.

## 11. Hair fitting

Hair is an ordinary asset variant: an MPFB hair proxy (MHCLO-fitted to the scalp, carries the face channels so brows/forehead
motion moves it) chosen in the character file, dyed by `hair_colour` (luminance-preserving). Hairstyles are asset choices, not
per-skull reconstruction. Scalp: the body's skin texture; no hiding rule needed for short hair.

## 12. Footwear

Footwear hides `foot_L/R`; the descriptor records its sole depth (`sole_m`, shoes03: 0.0255 m). Exact surface coincidence is not
relied on.

## 13. LOD rules

Per WAVE_0 section 6c: the assembled character gets one LOD chain generated from the assembly (face channels on LOD0 only; blink
and jaw on LOD1). Garments under 4k triangles get no chain of their own. Not yet produced for the standard characters.

## 14. Material conventions

One material per garment, `MAT_<asset id>_base` (base colour, normal, roughness; tiling materials declare `uv_scale`). Skin: one
plain material from the character's skin texture on export (`export_character.py`). Eyes, hair, brows, lashes: alpha-tested
(MASK), never alpha-blended. Kinds for the runtime's `CharacterMaterials` come from the material name's suffix.

## 15. Export rules

`blender -b --python charstd/export_character.py -- --blend <character.blend> --out <character.glb>`: helper geometry removed,
identity and macro targets baked into the basis, only face channels exported as morph targets (ARKit-52 + visemes), rest pose, one
skin, Y-up glTF. Provenance and licenses travel in the garment descriptors and identity presets.

## 16. Runtime expectations

Godot selects prepared meshes, uses the shared skeleton, hides region meshes, sets morph values by channel name, picks LOD and
material. It never fits, wraps, solves or reconstructs. Proven in a proof scene: `characters/standard/godot_face_contract_proof.*`.

## 17. Race / body-archetype expansion

One archetype per morphological family, each with its own regions file and garment conversions: `humanoid_masculine_a` and a
feminine counterpart (Veth, Siann scaled); Kal (`compact_broad`, dorsal wing channels) and Vaskaal (`tall_narrow`, 3+2 hands) as
adapted archetypes on the same channel contract; Mor, Constructed and heavy Ondrek forms as distinct families. A garment converts
to another archetype once (a cage fit), not per character.
