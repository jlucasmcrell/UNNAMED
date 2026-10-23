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
