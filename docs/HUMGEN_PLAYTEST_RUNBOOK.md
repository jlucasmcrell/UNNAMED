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

## RC d9 wardrobe handoff

The NPC ATTIRE owner ruling supersedes `rcd9_wardrobe03`, cancelled `rcd9_wardrobe04`, and `rcd9_review05` outfit frames. Use the interim role/station assignments in `HUMGEN_RCD9_WARDROBE.json` and the per-NPC rationale in `HUMGEN_RCD9_FRONTIER_ATTIRE.md`. Never use garment-set uniqueness to justify modern costumes or sneakers on village workers.

The replacement development overlay is `D:/Otherreach_Scratch/humgen_rcd8_20261023/rcd9_runtime05`; do not adopt it until all fourteen native export, material, exact 59-bone, exact-target motion and cache gates pass and the new source-game review completes. New raw wardrobe frames belong under `rcd9_review08`. Catalogue approval does not accept a new cast export. Keep original full-resolution frames; contact sheets are indexes only.

The development overlay contains junctions into immutable reference art, not a portable package or shortcut target. Future RC d9 packaging must dereference those into its own payload and verify all sixteen body/library hashes. Keep the accepted RC d8 game, owner profile and shortcut intact.

Kera/Sel/Tavar's race bodies are DeepSeek's separate production job. Coordinate `people_ref` with the heritage session, then gate the new statures and exact-body libraries when delivered. The shared frontier wardrobe / armour plan remains an implementation plan; this pass builds no custom period garments or armour visuals.

The superseded review07 dress and cyan-button diagnostics are preserved. Final review08 uses practical workwear and neutral native detail colours; no animation/body correction was used to hide clothing defects.
