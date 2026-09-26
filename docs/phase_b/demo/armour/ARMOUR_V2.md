# Animated Armour v2 (creature.construct.animated_armour)

Replacement for `assets/rigged/creature_animated_armour/`. The old mesh was rejected: 2,054 loose fragments, 2,008 of them under 50 vertices.

## Output
- `assets/rigged/creature_animated_armour_v2/creature_animated_armour_v2_rigged.glb`: 7.8 MB, glTF binary, Y-up, textures embedded.
- `assets/rigged/creature_animated_armour_v2/creature_animated_armour_v2_rig.json`: the old report's keys, plus triangles, rest deltas and vertices per bone.
- Asset id for the bindings: `creature_animated_armour_v2`. The clips stay `creature.animated_armour` and their files are unchanged.

## Route
**Route B.** Clean plates are built in Blender 5.2 from parametric surfaces, then Solidify, rolled rims, rivets and bosses. The scripts are in `tools/asset_pipeline/armour/`.

**Route A (TRELLIS.2) was not run, and ComfyUI-8190 was not used:**
- Its output is one fused surface that would still have to be cut per bone. The old model came from that path and is shattered.
- The morning run hung the shared server.

The concept image was the design reference throughout (`armour_v2_concept_compare.jpg`).

## Geometry and rig
- **Size:** 41,430 triangles, 41,002 vertices (GLB), 81 plate pieces.
  - Height 2.01 m (the old model was 2.00 m).
  - Bounds x -0.54..0.58, z -0.26..0.46 (Y-up).
- **Skeleton:** the armature is copied from the old GLB. The 20 bone names and the hierarchy are the same.
  - Rest TRS matches the old file to within 2.5e-6, inverse binds to within 6.3e-6.
  - In Godot the rest matches each clip's skeleton exactly (0.0).
- **Rigid skinning:** every vertex has one influence at weight 1.0, so there is no skin deformation. 19 bones carry plates; root carries none.
- **Plate-to-bone mapping:**
  - helm → head
  - gorget and standing collar → neck
  - breastplate and backplate → chest
  - quilted waist → spine
  - belt, faulds, culet, central plate and quilted trunks → hips
  - pauldron cap → shoulder
  - pauldron lames, padded sleeve, strap and couter (dome plus skirt) → upper_arm
  - vambrace → forearm
  - gauntlet → hand
  - cuisse, tassets and poleyn (dome plus skirt) → thigh
  - greave → shin
  - sabaton, instep lames, sole, ankle lame and boss → foot
- **Joints:** overlapping plates are nested with clearance and do not cross. For example, the vambrace and greave tops slide inside the couter and poleyn skirts. Dark quilted padding sits under the gaps. Plate interiors use a separate near-black `_void` material, so the suit reads as empty.
- **Sabatons:** they have a rocker sole (toe spring 4.6 cm, heel lift 1.8 cm).
  - The clips were solved by `_blender_anim_creature.py` so that the old mesh's lowest foot vertex met the ground.
  - Those clips pitch planted feet toe-down by about 20°, so a flat, long sole pierced the floor by 6.9 cm in walk.

## Materials
- **Maps:** one 2048² atlas, baked in Cycles on the CPU:
  - basecolor (JPEG q92)
  - normal (PNG, tangent-space, OpenGL convention; tangents exported)
  - ORM (JPEG: R occlusion, G roughness, B metal)
- **Look:** dark aged steel with mottling, scratch networks, pits and hammer dents. Rust appears only in each plate's own crevices. Brass bosses and rivets, a leather belt and straps, dark ribbed quilting.
- **Occlusion:** baked per piece in isolation. Rest-pose occlusion between separate pieces had shown up as dark rust bands whenever a clip opened a joint.
- **Material names:**
  - `creature_animated_armour_v2_metal` (textured). The `_metal` suffix picks up the `metal` kind in `character_materials.json` (specular 0.5).
  - `creature_animated_armour_v2_void` (untextured interior).
- **Sources:** ambientCG Rust009 (rust colour and height) and ambientCG Leather014 (leather colour, height and roughness), both CC0 1.0. The licence files sit beside them in `F:\Otherreach_External_Assets\materials\`. Everything else is procedural. Nothing was downloaded.

## Verification (sheets in this folder, all reviewed by eye)
- `armour_v2_clay_wire_rest.jpg`: bind pose from four views, plus the idle stance.
- `armour_v2_textured.jpg`: four views, plus torso and leg close-ups.
- `armour_v2_clip_{idle,walk,run,attack,hit,death}.jpg`: each clip at 3 moments, from the front-3/4 and back-3/4.
- `armour_v2_joint_closeups.jpg`: joints at the most extreme clip frames.
- `armour_v2_concept_compare.jpg`: the concept beside the bind pose and the idle stance.
- **Headless Godot 4.7.2 load:** PASS (`godot_check_armour.gd`, the same `GLTFDocument` path as `ArtLibrary`).
  - Both surfaces have tangents.
  - The metal surface has albedo, normal, roughness, metallic and AO textures.
  - All tracks of all six clips resolve.
- **Interpenetration audit** (`audit_intersections.py`, `armour_v2_intersections.json`):
  - It counts intersecting triangle pairs between plates on different bones.
  - Each clip is sampled at 13 frames; cloth padding is excluded.
  - The first build had about 3,000 pairs in every frame, including the bind pose.

  | Pose | Pairs per frame (min-max) | Mean |
  |---|---|---|
  | Bind pose | 0 | 0 |
  | Idle | 27-69 | 43 |
  | Walk | 35-352 | 194 |
  | Hit | 38-464 | 223 |
  | Attack | 38-628 | 368 |
  | Run | 164-737 | 514 |
  | Death | 38-1006 | 646 |

- **Lowest point per clip:**

  | Clip | Lowest point |
  |---|---|
  | Idle, attack, hit | 0 cm |
  | Run | -0.5 cm |
  | Walk | -1.7 cm |
  | Death (during the fall) | -11.7 cm |

  The old model stays at about 0 in every clip, because the clips were fitted to it.

## Remaining defects (honest)
1. **Rigid plates still clip in large motions:**
   - Pauldron lames against the cap on overhead and big arm swings (attack windup, run, hit, death).
   - Helm, collar and gorget on large head tilts (hit, death).
   - Greave against sabaton during steps.
   - Tassets against the lowest fauld when the thigh lifts.

   At gameplay scale the sheets show no pieces through each other; the counts above are what a close look finds.
2. **Attack windup:** with the arm overhead, the pauldron lames read as open hoops around the padded sleeve, separated from the cap.
3. **Feet below the floor:** by up to 1.7 cm in walk and 11.7 cm during the death fall. Fully fixing it needs the clips re-solved on this mesh, or foot IK. `BodyModifiers.PlantFeet` exists but creatures do not use it.
4. **Inherited from the skeleton; not fixable without changing it:**
   - In the bind pose the right foot floats 9 cm, because the right leg is 9 cm shorter. Every clip plants both ankles at about 0.11 m.
   - The torso is yawed 15° to the left relative to the legs and head; the plates follow the joints.
5. **Less massive than the concept:** slimmer limbs, smaller pauldrons and gauntlets, fewer rivets, and darker, less mirror-like steel. The chevron, bosses, belt, central plate, faulds, tassets, poleyns, greaves and layered sabatons are all present.
6. **Shared trim band:** rims, rivets, bosses and plate edges bake into overlapping UVs, so they are uniform bright steel or brass without per-rim variation.
7. **No LOD chain.**

## Binding (lead)
1. In `art_bindings.json`, point `creature.construct.animated_armour` at `"model": "creature_animated_armour_v2"`. Keep `"clips": "creature.animated_armour"`.
2. `assets/` is git-ignored, so the GLB is not in the commit. Put `rigged/creature_animated_armour_v2/` in the asset workspace the game reads (`--asset-root` / `AssetCatalog`). The old rig report says `W:\UNNAMED\assets`.
3. Verify in-game with a visual capture before accepting.

## Rebuild
Run `powershell -File tools/asset_pipeline/armour/run_pipeline.ps1` (build, bake and export, under a minute), then `render_all_sheets.ps1`.

Checks:
- `python verify_glb.py <old.glb> <new.glb> <rig.json>`
- `godot --headless --script godot_check_armour.gd -- <glb> <clips dir>`
- `blender -b -P audit_intersections.py -- <build.blend> <clips dir> <out.json>`
