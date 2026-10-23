# HumGen playtest pipeline

Owner ruling, 2026-10-23: HumGen is the only character pipeline from RC d8.
Work on `playtest/humgen-main`. The MH and MPFB branches and RC d7 packages are frozen references.
Do not build or test another matched pair.

DeepSeek owns `tools/humgen`, paid body generation, official Godot recipe exports and clothing fitting.
Codex owns game integration and the RC d8 gameplay queue. Coordinate through
`C:\Otherreach_External_Assets\CODEX_PROGRESS.md` and the shared GPU lease.
Never capture the desktop or send desktop input; record the game's own window by title.

## Bodies and gear

Use paid HumGen player presets and distinct named identities, with the named NPC's correct sex.
`Art/named_character_cast.json` carries the stable HumGen identity IDs. Each installed body needs
its measured height, fitted clothing, hair cards, materials, expressions and exact-target motion library.
The creator must select HumGen bodies. Slider editing comes later.

Fit paid or converted clothing using HumGen's own custom-content and deform-cloth tools.
Existing DevonLux gear is usable only after a successful HumGen conversion and visual review.
Do not attach its old MetaHuman skin/binds to a HumGen body.
Keep paid assets and motion payloads external. Do not push licensed assets to the public repository.

## Animation source rule

**No KayKit for any player or NPC animation. No manual arm keyframing.**
Retarget approved realistic sources onto each actual HumGen skeleton offline. Runtime only remaps node paths;
check the library's target-body SHA-256 before loading. Never reuse MPFB local hand rotations.

Accepted sources:

| Motion | Source |
| --- | --- |
| Forward walk/run/sprint, neutral idle | UAL1 Walk_Loop / Jog_Fwd_Loop / Sprint_Loop / Idle_Loop |
| Walk strafe L/R | FAL anim_Walk_Left_Loop / anim_Walk_Right_Loop |
| Jog strafe L/R | FAL anim_Jog_Loop_Left_L / anim_Jog_Loop_Right_R |
| Walk/run backwards | FAL anim_Walk_Bwd_Loop_L / anim_Jog_Loop_Bwd |
| Three sword attacks | UAL1 Sword_Attack; UAL2 Sword_Regular_A / Sword_Regular_B |
| Sword ready guard | UAL1 Pistol_Idle_Loop; upright two-hand guard, Claude accepted Ada06 for owner review |
| Spear ready/thrust | FAL Update_2/Spear AS_Spear_Idle / AS_Spear_Attack_01; HumGen visual gate pending |

Carry sit/lie, grounded captive kneel, idle variety, face driver and measured foot contacts too.
The imported 24-state comparison bake is an intermediate, not acceptance of the full animation port.

The original UAL1 Sword_Idle contains an authored deep squat. Native source/target measurements
confirmed Epic's pelvis-height scaling preserves that pose correctly. Replace the ready source;
do not lift the hips or keyframe arms to compensate. The accepted three strikes stay unchanged.

## Gates and delivery

Run the full solution tests after integrating pets, preserving save compatibility and deterministic catch-up.
Use `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` for builds/exports.
The headless `Diagnostics/HumGenRigGate.tscn` checks actual paid export bone roles, finger grip geometry
and FACS binding; it does not replace visual review.

Claude reviews real-time movement/combat, hand/sword grip, sit/lie/kneel and footstep timing;
review close-ups of faces, hands, hair and fitted garments, including named NPC identity/sex.
Then clean export and fresh-save start the single package, with grass/art present and pets included.
Normal startup uses `GameOptions`' accepted pets default; capture flags must not enable release features.
Finish the remaining RC d8 gameplay gates on this same line.
Package as `OTHERREACH - RC d8 (HumGen)` and promote only after the visual gates are accepted.
Keep the RC d7 shortcuts/packages as backups.
