# Charwood trees: variants and LOD chains (2026-09-26)

Sheets: `<id>_lods.jpg` per tree (10/30/80 m at the matching level, each switch as a split frame at the recommended bands
and at the game's current distances) and `trees_lineup.jpg` (all eight at LOD0 30 m, LOD3 80 m, and as a grove).
Renders are Eevee, AgX, sky + sun, 1920x1080 player camera (75 deg vertical FOV, eye 1.7 m); from 75 m without sun
shadows, as past the game's shadow range (60-120 m by render tier).

## Ids

| id | species preset | seed | height m | footprint m | LOD0 / LOD1 / LOD2 / LOD3 tris |
|---|---|---|---|---|---|
| flora_oak_tree (original) | oak | 3101 | 13.7 | 12.3 x 14.1 | 21,454 / 10,714 / 4,274 / 16 |
| flora_oak_tree_b | oak_b | 3217 | 11.8 | 14.8 x 14.1 | 24,268 / 12,118 / 4,838 / 16 |
| flora_oak_tree_c | oak_c | 3343 | 14.0 | 14.1 x 12.4 | 23,110 / 11,538 / 4,610 / 16 |
| flora_pine_tree (original) | pine | 2207 | 13.4 | 7.5 x 7.6 | 13,702 / 6,840 / 2,738 / 16 |
| flora_pine_tree_b | pine_b | 2311 | 15.3 | 8.0 x 7.0 | 14,322 / 7,158 / 2,856 / 16 |
| flora_pine_tree_c | pine_c | 2423 | 14.1 | 8.9 x 8.1 | 16,272 / 8,126 / 3,242 / 16 |
| flora_dead_tree (original) | dead | 1409 | 12.8 | 11.0 x 7.3 | 10,130 / 4,142 / 2,010 / 16 |
| flora_dead_tree_b | dead_b | 1523 | 11.9 | 10.2 x 7.1 | 10,336 / 4,364 / 2,062 / 16 |

LOD1 is 50 % of LOD0 (dead trees 41-42 %: simplifying the tubes alone already lands under budget), LOD2 20 %.
Every file is in `assets/ready/<id>/` with `_lod1..3.glb`, `_meta.json` (lods, lod_report, remediation record); the
new ids also carry trunk collision (`_collision_hull/_box.glb`, policy "trunk" as in `_build_foliage.py`).

## What changed

- `tools/asset_pipeline/_procgen_tree.py`: five variant presets (`--species oak_b|oak_c|pine_b|pine_c|dead_b`) derived
  from the originals' presets; `builder` indirection. Oak variants: broader (b) or taller, higher-based (c) crowns, more
  lumping, cards on inner branches, wider card roll, and new `foliage.clump`: each branch's spray shades as its own
  rounded mass (normals bent toward the spray's centre as well as the crown's), so the crown reads as layered lobes, not
  one ball. Pine variants: taller/narrower (b), fuller (c), clump shading per whorl branch. dead_b: its own limb table,
  opposite lean, snapped top. The three originals regrow geometry-identical (checked accessor by accessor).
- `tools/asset_pipeline/_procgen_tree_lods.py` (new): regrows the tree, proves it matches the base GLB, and cuts
  LOD1/LOD2 from the skeleton (twigs and least important branches dropped first, fewer sides and rings, cards thinned
  evenly per crown cell and grown from their outer edge inward), with the base's own textures at 1024/512. LOD3: four
  crossed planes, each two single-sided quads 2 cm apart showing the view from its own side (8 orthographic Cycles
  renders; albedo with baked sky occlusion, tangent normals, alpha MASK 0.5, 1024x512 atlas), albedo calibrated so its
  lit colour matches the base's within 6 %. Silhouette IoU vs LOD0 (8 views): LOD1 >= 0.90, LOD2 >= 0.80, LOD3 0.84-0.88
  (dead trees 0.51: thin twigs).
- `_render_tree_lods.py`, `_tree_review_sheets.py` (new): the review renders above.
- Godot 4.7.2 headless check: all 32 files load through GLTFDocument, every surface textured (ArtLibrary accepts the
  full chain), bounds of every level inside LOD0's, same origin.

## For the lead

- New ids reach the game only when added to `art_bindings.json` `tree_` models; rebuild the texture cache after.
- LOD switch distances: `ArtLibrary.ModelWithLods` uses 4x / 10x / 22x the largest extent, i.e. ~55 / 140 / 310 m for
  these trees. The chains were judged at 25 / 50 / 80 m (recommended). Row 3 of each sheet shows why: at 130-300 m the
  alpha-cut pine needles and dead twigs of LOD1/LOD2 break up into sky, where the impostor stays solid.
- Suggest `CastShadow = Off` on the last level (as ScatterView does for far cards): inside the sun's shadow range the
  crossed planes shadow each other in straight wedges along the trunk axis.

## Remaining weaknesses

- Impostor (LOD3): softer than LOD2 and a faint vertical seam on the trunk axis where planes cross, most visible when the
  view is 20-25 deg off a plane (visible at 2-3x zoom at 80 m, slight at true size). Dead-tree impostors thicken and
  blur the finest twigs; pine impostors are marginally bluer at the top.
- Wind shader risk (not verified in engine): `WindField.TreeShader` is `cull_disabled`, and every card here (originals
  included) is two coplanar quads back to back, so both draw at one depth and the rear one's flipped normal points into
  the crown. `if (!FRONT_FACING) discard;` for foliage would remove it.
- The original oak still reads as a ball (untouched by brief); oak_b/oak_c carry the fuller, lobed crown. Edge-on cards
  still show as streaks within ~15 m on all broadleaf trees.
- The originals' meta `collision` records a full-bounds hull (12 x 14 x 13 m for the oak) under policy "trunk", left by an
  earlier `--refresh-geometry`; not changed here (the game does not read those files).
- Each LOD file embeds its own textures (LOD1 9 x 1024^2, LOD2 9 x 512^2, LOD3 2 x 1024x512): ~65 MB uncompressed per
  oak or pine id on top of LOD0, until the texture cache compresses them.
