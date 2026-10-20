# Native MetaHuman garments and relaxed fingers

Use `rebind_piece_direction.py` for every rigged DevonLux garment, separately for each exact runtime body:

    blender --background --factory-startup --python-exit-code 2 --python rebind_piece_direction.py -- piece.glb native_body.glb garment_for_body.glb

The garment alone is posed by matching joint positions and the minimal rotation between bone directions. Bone roll is retained; terminal bones translate only. The Armature modifier is applied to the garment and the original author weights bind to the unchanged target armature. The target body mesh is omitted from the export. Never edit a MetaHuman skeleton or body to fit clothing. Original source garments and native body files remain unchanged.

The playtest catalog chooses `garment_directory` for chest/pants/hood, `boots_path` for per-body DevonLux boots, `trousers_path` for optional wool hose, and `hidden_mask` for the outfit's Body Hidden Face Map. Native hands are exposed; shell gloves/boots are not loaded. Mask generation leaves the native mesh buffers untouched and explicitly clears hand UV coverage.

`retarget_relaxed_hands.py` uses Epic's IK Rig/IK Retargeter and native GLTFExporter. In isolated retarget assets it removes all 18 target finger/metacarpal chains, resets only those retarget-pose offsets, and retains the root-motion correction. It exports all 26 original Quaternius/KayKit clips for each of the 12 native bodies. No skeleton, bind, or body changes; no Godot retargeting.

Run using Unreal's Python commandlet, `-NullRHI -run=pythonscript -script=<script>`, with `OTHERREACH_MH_ANIM_CHARACTERS` set to comma-separated catalog IDs. Paths in these owner-machine scripts point to the isolated RC d1 workspace on D:. `clean_relaxed_animations.py -- <id>` applies the same documented Blender glTF bridge as the accepted native bodies. `build_relaxed_animation_library.gd` validates every native bone name, parent and rest, and checks all exported finger rotation keys against native relaxed rest (0.1 degree tolerance) before saving external `animations_relaxed/<id>.res`.

Acceptance requires actual running-game close-ups and the packaged desktop feel pass. Offline scripts and successful exports alone do not establish visual acceptance.

Owner rejection06:35: bare-feet isolation found automatic retarget-pose foot offsets near67degrees. retarget_native_extremities.py resets foot_l/ball_l/foot_r/ball_r offsets AFTER automatic chain alignment, alongside the excluded fingers; native rest/skeleton remain unchanged. Isolated idle/walk bare and booted captures now match the source horizontal stance. native_wrist_uv.gd restores an8cm region at each native wrist in the material mask. Visual acceptance remains Claude/owner's, with new packaged bare/booted front/side/three-quarter and both-wrist idle/walk proof in the opt-in ExtremitiesReview. No performance phase runs in that diagnostic.

For the selected full-length trouser plus tall-boot outfit, native_covered_legs_uv.gd adds the native lower-body UVs below pelvis+12cm to the material mask. raster_native_hands_masks.py unions that coverage with the posed-cloth mask, then restores native hands and8cm wrists LAST. This fixes exposed shins/hips without changing body arrays. verify_native_extremities.gd catches the downward-foot defect at idle; negative old-library test fails8 feet, corrected set passes24. Packaged diagnostic uses MetaHumanFigure's same fill layer and lifts staged figures35cm above terrain only to inspect their soles.
