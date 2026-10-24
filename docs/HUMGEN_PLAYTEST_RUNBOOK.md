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

Use the full-cast candidate under `D:\Otherreach_Scratch\humgen_rcd8_20261023\rcd9_runtime02` only after its
`RC_D9_PAYLOAD_RECEIPT.json` reports `READY_FOR_NATIVE_REVIEW` and the native review runner completes.
The set includes `primary`, `humgen_characters`, corrected `content`, `NPC_OUTFIT_AUDIT.json` and `MATERIAL_OPACITY_AUDIT.json`.
All fourteen NPC bodies receive the selections in `docs/HUMGEN_RCD9_WARDROBE.json`; both player bodies/libraries remain unchanged.
Use the new cast hashes, exact-target libraries and signed texture cache together. The native wear-API exports,
original-geometry/sparse-image checks and exact 59-bone rest checks are in `rcd9_wardrobe03`.

This development overlay contains directory junctions into immutable reference art. It is not a portable package or a
shortcut target. A future clean RC d9 package must dereference those directories into its own payload and verify all sixteen
body/library pairs. Do not use the superseded four-body `rcd9_runtime` or the unchanged `runtime/production_native` NPC set.
Keep the accepted RC d8 package, owner profile and shortcut intact.

Final source-game inspection frames belong under `rcd9_review05`: fourteen NPC front idles, fourteen moving three-quarter
views, Tavar's normal captive kneel and both peaceful player spawns. The full creator body-choice stills remain under
`rcd9_review03`: the player assets and UI code are unchanged; final05's fast Begin path does not repeat those stills. Frames and receipts label
the actual person/people IDs; choose opaque Veth pool representatives to inspect clothing while preserving canonical race
shaders in normal play. Original full-resolution PNGs are the evidence; cropped contact sheets are review indexes only.
Catalogue approval does not approve a new cast export; Claude judges the in-game frames.

The armour approach is separately documented in `HUMGEN_WORN_ARMOUR_PLAN.md`; this batch implements no armour visuals.
