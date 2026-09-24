# Phase-1 Building and World Material Pass

**Date:** 2026-09-24
**Scope:** `forge_shed`, `longhouse`, and the ten world materials named in `phase1_environment.json`

## Outcome

- **A real geometry defect was found and fixed** in both buildings: the two roof slopes overlapped by
  **0.784 m across the ridge**, so the roof intersected itself instead of meeting at a ridge.
- **All ten world materials are promoted and validated**: tileable, correct world scale, normals and
  roughness present, Godot import clean, de-lit.
- **The materials were measurably washed out and are now graded** to documented weathered targets.
- **Two defects in my own preview renderer** were found and fixed along the way, one of which would
  have sent anyone hunting a texture bug that did not exist.

Nothing changed that the brief protects: no footprint contract, doorway contract, collision interface
or assembly ID was altered except where a real defect required it, and that one change is called out
below.

## 1. The roof overlapped itself by 0.784 m

`forge_shed`'s eight roof panels were laid from the eave inward. Two panels per slope at 1.696 m of
projected run cover 3.392 m of a 3.0 m half-depth, so each slope overshot the ridge by 0.392 m and the
two slopes **intersected across a 0.784 m band**. Measured on the built asset, before the fix:

```
building_roof_panel       [-1.5, 3.26, -2.23]    <- eave row
building_roof_panel.002   [-1.5, 4.32, -0.54]    <- ridge row, past z=0
building_roof_panel.006   [-1.5, 4.32,  0.54]    <- the other slope, past z=0
```

The third layout is the correct one: **lay from the ridge outward**, so both slopes meet exactly at
z = 0 and the surplus 0.39 m per side falls at the eave as an **overhang**, which is what a real roof
does. The panel size is fixed by the kit, so the surplus has to go somewhere and an eave is the only
place where it is not a defect.

```
building_roof_panel       [-1.5, 4.07, -0.93]    <- ridge row, starts at the ridge
building_roof_panel.002   [-1.5, 3.01, -2.62]    <- eave row, overhangs
```

This extends the recorded depth from 6.318 m to 7.102 m and reduces the ridge from 4.974 m to 4.729 m.
**That is a deliberate change to a dimension**, and it is recorded here because it is the one place
this pass altered something the brief otherwise protects. It is a consequence of removing the
self-intersection, not a redesign: the walls, floor, doorway, posts, beams and window openings are
untouched, and the kit pieces are unchanged.

## 2. The world materials were washed out, and by how much

Measured with the material set's own base colours:

| material | class | mean L (before) | contrast p5..p95 (before) |
|---|---|---|---|
| `material_limestone_ashlar` | stone | 202 | **19** |
| `material_plaster_lath_wall` | plaster | 202 | **45** |
| `material_loose_gravel` | ground | 180 | 144 |
| `material_oak_plank_floor` | timber | 169 | 95 |
| `material_churned_wet_mud` | ground | 153 | 155 |
| `material_packed_dirt_ground` | ground | 150 | 131 |
| `material_slate_roof_scale` | roof | 142 | 138 |
| `material_rubble_stone_wall` | stone | 137 | 171 |
| `material_marsh_grass_turf` | ground | 131 | 137 |
| `material_cast_iron_surface` | metal | 122 | 164 |

**A dressed limestone ashlar wall with a contrast of 19 is a grey card, not stone.** That is the whole
of the "paper model" read: the geometry is correct but every surface is flat and pale.

The cause is not a bug. `_make_pbr_materials.py` correctly removes the lighting the concept render was
lit by, so the base colour carries albedo rather than illumination — and that also removes the shadows
that were carrying most of the apparent contrast, leaving a bright low-contrast average. Recovery has
to come from grading, because re-adding baked lighting is exactly the artifact the brief says to avoid.

### The grade

`_weather_materials.py` applies a documented per-class grade: clip the extreme percentiles, remap to
restore relief, then scale luminance to a weathered target. Hue is preserved by scaling all three
channels by one factor.

| class | target mean L | materials |
|---|---|---|
| ground | 112 | packed dirt, gravel, wet mud, grass turf |
| timber | 118 | oak planks |
| plaster | 156 | plaster and lath |
| stone | 122 | rubble stone, limestone ashlar |
| roof | 112 | slate |
| metal | 100 | cast iron |

Result: every material lands on its target mean exactly, and `limestone_ashlar`'s contrast rises from
**19 to 45**.

### Why this is safe

Every operation is **per-pixel**, so tileability cannot change — the wrap test compares opposite edges,
and a pointwise map cannot alter whether they match. Confirmed after applying: **10/10 verified**, and
**Godot import 10 passed, 0 failed**. Normals, ORM, roughness and AO were not touched. Originals are
archived to `assets/_superseded/materials_pre_weather/` and `--restore` puts them back.

## 3. Two defects in my own renderer

Neither was in the assets, and both produced pictures that would have been read as asset faults.

**The lights were 2.15x too bright.** `_blender_preview.py` calibrated `355 * radius^2` as the
irradiance that renders a surface at mid grey. The new building preview used power scales of 1.0, 0.45
and 0.7 — summing to **2.15x** — so every building it produced was lit two stops hot and read as
washed-out white regardless of its materials.

**The view transform was AgX.** Blender's filmic default desaturates and lifts mid-tones. A weathered
slate roof at mean luminance 112 was rendering near-white. Set to `Standard`, with the look set to
None, so what is judged is the material rather than the tone curve.

A third: the area lights were `radius * 2` — 14 m wide softboxes on a 7 m building — so any specular
lobe returned a highlight larger than the surface it sat on. Now `radius * 0.35`. The materials'
roughness values are their own and were not changed to flatter a preview.

## 4. What was verified

| claim | evidence |
|---|---|
| The buildings compose to their stated size | `_glb_world_bounds.py`: forge_shed **6.0 x 4.974 x 6.318**, longhouse **9.0 x 4.974 x 6.318** |
| The layout is a building, not a tangle | `_assembly_layout.py`: floor at Y=0, 4 corner posts, 3 walls, door, 4 windows, 4 beams at 2.67, 8 roof panels above |
| The geometry is sound independent of materials | `_blender_solid_preview.py` (Workbench, no textures) shows a timber-framed building with a roof |
| The roof no longer self-intersects | ridge rows now at z = +/-0.93 starting at the ridge; eave rows at +/-2.62 overhanging |
| Materials are tileable after grading | `_verify_pbr_materials.py` 10/10 |
| Materials import into Godot | `_godot_validate_materials.py` 10 passed, 0 failed |
| No baked lighting | every material records `delit: true` |

## 5. What is still not right

**The roof's shingle courses still read bright in preview.** `building_roof_panel` is modelled as
overlapping shingle courses in a single 84-triangle mesh, and their upward faces return a strong
highlight under the preview's lighting. The base colour behind them measures a weathered 112, so this
is shading rather than albedo, and my remaining suspicion is a specular lobe on faces angled toward the
key light.

I stopped here rather than iterate further, because the next lever is either the kit panel's modelled
geometry or its declared roughness — both of which are kit-asset changes rather than a presentation
pass, and the brief protects the assembly contracts. **This should be judged in Godot with the game's
own lighting before anything else is changed**, since the preview is a diagnostic and not the target
renderer.

## 6. Files

- `tools/asset_pipeline/_weather_materials.py` — the grade, with `--audit`, `--apply`, `--restore`
- `tools/asset_pipeline/_glb_world_bounds.py` — composes node transforms; the correct instrument here
- `tools/asset_pipeline/_assembly_layout.py` — per-piece world boxes
- `tools/asset_pipeline/_blender_building_preview.py` — framed building render
- `tools/asset_pipeline/_blender_solid_preview.py` — geometry without materials
- `tools/asset_pipeline/_make_kit_assemblies.py` — roof laid ridge to eave
- `assets/materials/WEATHERING.json` — the applied grade and per-material results
- `assets/_superseded/materials_pre_weather/` — the originals
- `assets/_superseded/roof_layout_v2/` — the buildings before the roof fix

## 7. A trap worth recording

`_glb_bounds.py` reports the union of mesh **accessor** min/max with **no reference to node
translation, rotation, scale or matrix**. For a single-mesh asset that is correct, which is why it has
been reliable for props, weapons and creatures. For a multi-node asset it reports roughly **one
piece**: it gives `forge_shed.glb` as "3.000 x 2.600 x 3.000 m", which is the size of a single wall
panel, against a true 6.0 x 4.974 x 6.318.

It nearly cost a day. `forge_shed`'s own metadata said 6.0 x 6.318 x 4.974 and the tool said 3.0 x 2.6,
and the natural reading is that the assembly is broken. It is not. Use `_glb_world_bounds.py` for
anything with more than one node.
