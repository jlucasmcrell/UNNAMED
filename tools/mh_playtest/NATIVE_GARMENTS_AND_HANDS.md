# Native MetaHuman garments and relaxed fingers

Use `rebind_piece_direction.py` for every rigged DevonLux garment, separately for each exact runtime body:

    blender --background --factory-startup --python-exit-code 2 --python rebind_piece_direction.py -- piece.glb native_body.glb garment_for_body.glb

The garment alone is posed by matching joint positions and the minimal rotation between bone directions. Bone roll is retained; terminal bones translate only. The Armature modifier is applied to the garment and the original author weights bind to the unchanged target armature. The target body mesh is omitted from the export. Never edit a MetaHuman skeleton or body to fit clothing. Original source garments and native body files remain unchanged.

The playtest catalog chooses `garment_directory` for chest/pants/hood, `boots_path` for per-body DevonLux boots, `trousers_path` for optional wool hose, and `hidden_mask` for the outfit's Body Hidden Face Map. Native hands are exposed; shell gloves/boots are not loaded. Mask generation leaves the native mesh buffers untouched and explicitly clears hand UV coverage.

`retarget_relaxed_hands.py` uses Epic's IK Rig/IK Retargeter and native GLTFExporter. In isolated retarget assets it removes all 18 target finger/metacarpal chains, resets only those retarget-pose offsets, and retains the root-motion correction. It exports all 26 original Quaternius/KayKit clips for each of the 12 native bodies. No skeleton, bind, or body changes; no Godot retargeting.

Run using Unreal's Python commandlet, `-NullRHI -run=pythonscript -script=<script>`, with `OTHERREACH_MH_ANIM_CHARACTERS` set to comma-separated catalog IDs. Paths in these owner-machine scripts point to the isolated RC d1 workspace on D:. `clean_relaxed_animations.py -- <id>` applies the same documented Blender glTF bridge as the accepted native bodies. `build_relaxed_animation_library.gd` validates every native bone name, parent and rest, and checks all exported finger rotation keys against native relaxed rest (0.1 degree tolerance) before saving external `animations_relaxed/<id>.res`.

Acceptance requires actual running-game close-ups and the packaged desktop feel pass. Offline scripts and successful exports alone do not establish visual acceptance.
