# HumGen production motion payload

The paid HumGen production libraries are external assets, not source files.
Install `<asset-id>_motion.res` in `humgen_characters/mocap_motion/` beside the package.
The launcher sets `OTHERREACH_CHARACTER_ROOT` to that payload folder; developer runs may set it explicitly.
The retired 24-state comparison bakes remain in the frozen DeepSeek history and its external test output.

Each library must match the installed body's `target_body_sha256`, include every state required by
`Ui/Models/HumGenMotionPolicy.cs`, carry an `animation_sources` dictionary for every state and alias,
and supply measured gait `motion_paces`, `motion_phases` (seconds), `foot_plants_schema=1`
and `foot_plants` events. Runtime changes node paths only, and refuses incomplete or KayKit-sourced payloads.

DeepSeek owns the HumGen export/bake tools. A successful bake is not visual acceptance:
Claude reviews movement, attacks/grip, sitting/lying/kneeling, faces and contacts on the production bodies.

Production texture preparation keeps the original HumGen image resolutions, including 4K garment maps.
Run `tools/animation/fix_humgen_lod_materials.py original.glb staged.glb` to assign LOD0 baked garment
materials to matching LOD1+ primitives. Never prune or remap buffer views without preserving sparse
accessor indices and values (facial morphs); run its adjacent stdlib regression test and byte-audit
all ordinary and sparse accessor payloads against the original body before staging.

The existing signed BC7 cache covers runtime HumGen images. In the Godot editor binary, run headless
`res://Diagnostics/HumGenTextureCacheGate.tscn -- <asset-root> <model-id> <receipt.json>` for each body,
using the normal CPU-tool guard. Ship its `texture_cache/` DDS files and merged manifest in the PRIMARY
asset root, with `textures=cache` (already used by all quality presets). Source GLBs stay untouched;
never publish an incomplete cache batch. Repack exact-body motion libraries after any GLB hash change,
using the existing RateScale-aware native builder, and keep visual acceptance separate from bake success.
