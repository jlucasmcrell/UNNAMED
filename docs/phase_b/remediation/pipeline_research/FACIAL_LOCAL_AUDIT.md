# Facial Assets & Tooling — Local Audit

Read-only audit performed 2026-09-26. Scope: MPFB2 Blender extension (code + installed
user-data), the asset-pack depot on `F:\Otherreach_External_3D` and
`F:\Otherreach_External_Assets`, and the game workspace at `G:\UNNAMED_PHASEB`. No files were
modified, moved, or deleted; this document is the only artifact created by the audit.

---

## 1. MPFB version installed

`blender_manifest.toml` at
`%APPDATA%\Blender Foundation\Blender\5.2\extensions\user_default\mpfb\blender_manifest.toml`:

```
id = "mpfb"
version = "2.0.17"
name = "MPFB"
maintainer = "Joel Palmius"
blender_version_min = "4.2.0"
license = ["SPDX:GPL-3.0-or-later"]
```

Confirmed: **MPFB 2.0.17**, matching the believed version. Code lives under
`...\extensions\user_default\mpfb\` (the add-on itself); per-user downloaded assets live in the
parallel `...\extensions\.user\user_default\mpfb\data\` tree (different folder, easy to miss).

---

## 2. Installed asset packs vs. depot

Depot manifest `F:\Otherreach_External_3D\mpfb\asset_packs\SHA256SUMS.txt` lists 11 packs:
`eyebrows01_cc0`, `eyelashes01_cc0`, `faceunits01`, `hair01_cc0`, `hair02_ccby`, `hair03_ccby`,
`hands01_cc0`, `makehuman_system_assets_cc0`, `shirts02_ccby`, `shirts03_ccby`, `skins01_cc0`.

Installed-pack manifests found under `...\.user\user_default\mpfb\data\packs\`:
`eyebrows01.json`, `eyelashes01.json`, `faceunits01.json`, `hair01.json`, `hair02.json`,
`hair03.json`, `hands01.json`, `makehuman_system_assets.json`, `shirts02.json`, `shirts03.json`,
`skins01.json`.

**Every pack present in the depot is installed. Nothing in the depot is uninstalled.**
Installed asset counts (by folder under `...\.user\user_default\mpfb\data\`):

| Category | Installed items |
|---|---|
| eyes | high-poly, low-poly meshes + 12 iris-color materials (blue, bluegreen, brown, brownlight, deepblue, green, grey, ice, lightblue, …) |
| eyebrows | 12 `eyebrow0NN` + 14 `mindfront_eyebrows_*` = 26 meshes |
| eyelashes | 4 `eyelashesNN` + 5 `mindfront_eyelashes_*` = 9 meshes |
| teeth | `teeth_base` + 5 `teeth_shapeNN` variants |
| tongue | 1 variant (`tongue01`) |
| hair | ~70 styles across hair01/02/03 |
| skins | 28 skin textures |
| clothes | 52 items |
| targets/faceunits | **52 ARKit face-unit `.target` files** (see §3) |
| targets/hands | hand pose/shape targets (not facial, not detailed here) |
| poses | empty (no bundled non-T poses) |
| expressions | empty (README confirms: "intentionally empty in the current iteration") |

There is **no separate viseme asset pack in the depot at all** — `SHA256SUMS.txt` has no
`visemes01`/`visemes02`/similar entry, and no such zip exists anywhere under
`F:\Otherreach_External_3D`. This is a code/data gap in the upstream MPFB distribution itself,
not a missed local install (see §3).

---

## 3. MPFB facial capability in code

All facial logic is centralized in
`...\extensions\user_default\mpfb\services\faceservice.py` (`FaceService`, static-method class,
1045 lines — designed for headless/scripted use).

### Viseme and ARKit constants (exact names)

**Microsoft visemes (22, `MICROSOFT_VISEMES`)**: `aa_02, aa_ah_ax_01, ao_03, aw_09, ay_11,
d_t_n_19, er_05, ey_eh_uh_04, f_v_18, h_12, k_g_ng_20, l_14, ow_08, oy_10, p_b_m_21, r_13,
sh_ch_jh_zh_16, sil_00, s_z_15, th_dh_17, w_uw_07, y_iy_ih_ix_06`

**Meta/ARKit-v2 visemes (15, `META_VISEMES`)**: `viseme_aa, viseme_CH, viseme_DD, viseme_E,
viseme_FF, viseme_I, viseme_kk, viseme_nn, viseme_O, viseme_PP, viseme_RR, viseme_sil,
viseme_SS, viseme_TH, viseme_U`

**ARKit face units (52, `ARKIT_FACEUNITS`)**: `browDownLeft, browDownRight, browInnerUp,
browOuterUpLeft, browOuterUpRight, cheekPuff, cheekSquintLeft, cheekSquintRight, eyeBlinkLeft,
eyeBlinkRight, eyeLookDownLeft, eyeLookDownRight, eyeLookInLeft, eyeLookInRight,
eyeLookOutLeft, eyeLookOutRight, eyeLookUpLeft, eyeLookUpRight, eyeSquintLeft, eyeSquintRight,
eyeWideLeft, eyeWideRight, jawForward, jawLeft, jawOpen, jawRight, mouthClose,
mouthDimpleLeft, mouthDimpleRight, mouthFrownLeft, mouthFrownRight, mouthFunnel, mouthLeft,
mouthLowerDownLeft, mouthLowerDownRight, mouthPressLeft, mouthPressRight, mouthPucker,
mouthRight, mouthRollLower, mouthRollUpper, mouthShrugLower, mouthShrugUpper,
mouthSmileLeft, mouthSmileRight, mouthStretchLeft, mouthStretchRight, mouthUpperUpLeft,
mouthUpperUpRight, noseSneerLeft, noseSneerRight, tongueOut`

### Which of these actually have target files on disk

`FaceService.load_targets(basemesh, load_microsoft_visemes, load_meta_visemes,
load_arkit_faceunits)` (line 296) builds a target stack and calls
`TargetService.bulk_load_targets`. That function (`targetservice.py:722`) resolves each name via
`TargetService.target_full_path` and, **if the file can't be found, logs a warning and silently
skips it** (`targetservice.py:764-765`) — it does not raise.

- **ARKit face units: all 52 present.** `...\.user\user_default\mpfb\data\targets\faceunits\*.target`
  contains exactly 52 files, one per name above, matching `faceunits01.json`
  (author "Mika Suominen", license **CC0**, description "Autogenerated ARKit faceunit").
  `FaceService.is_faceunits01_installed()` (probes for `cheekPuff.target`) would return `True`.
- **Microsoft visemes (22) and Meta visemes (15): zero target files exist anywhere on this
  machine.** A full-tree search of the entire Blender extensions folder for `*viseme*` turns up
  only four *UI descriptor* JSON files (`visemes01.json`, `visemes02.json` under
  `ui/operations/faceops/properties/`, plus two export-panel property files) — these are just
  checkbox labels/tooltips, not asset packs. There is no `is_visemes01_installed()` /
  `is_visemes02_installed()` guard anywhere in the code (only `is_faceunits01_installed()`
  exists), and the "Face operations" panel (`faceopspanel.py`) shows the visemes01/02 checkboxes
  unconditionally with no install check. **Practical effect: ticking "Load visemes01/02" and
  pressing "Load Face Shape Keys" in MPFB's own UI will silently produce zero shape keys** — no
  error, no warning surfaced to the user, just nothing loaded. This is a genuine gap in the
  upstream MPFB 2.0.17 distribution itself (no visemes zip exists even in the depot, §2), not a
  local misconfiguration.
- `FaceService.configure_lip_sync()` and `VISEMES02_TO_LIPSYNC` exist to wire Meta/ARKit visemes
  into the third-party "Lip Sync" Blender add-on's per-viseme slots — but this is moot while no
  viseme target files exist to produce the shape keys it would map.

### Expression system

`FaceService` implements a full expression system built entirely on the 52 ARKit shape keys
(no separate MakeHuman-style bone-pose "expression units" system was found active in code —
the bundled `data/poses/*_fk/` folders contain only a `t-pose.json` each, no facial poses):
- `set_expression` / `clear_expression` / `read_current_expression` — read/write `!ex-*` shape
  keys from a bare-name `{ARKit name: weight}` dict.
- `save_expression` / `load_expression` — JSON file format (`docs/fileformats/expression.md`),
  versioned (`EXPRESSION_FORMAT_VERSION`), CC0-style metadata fields.
- `list_available_expressions`, `aggregate_expression_stack`, `apply_expression_file`,
  `rebuild_expression_stack`, `clear_applied_expressions`, `set_stack_weight` — a stacking
  system (`basemesh["mpfb_applied_expressions"]`) that composites multiple weighted expression
  files into one live shape-key state; used by an "expressions library" UI panel.
- Every entry point that actually writes shape keys gates on
  `FaceService.is_faceunits01_installed()` (confirmed `True` here — §2).
- Because this system is **shape-key based, it is rig-independent** — it works identically
  whichever skeleton (`default`, `game_engine`, Rigify, …) is attached, since shape keys live on
  the mesh, not the armature.

### Bone-driven facial articulation (rig-dependent — this is where "default" vs "game_engine" matters)

Bone names extracted directly from the bundled rig JSON files
(`...\mpfb\data\rigs\standard\*.json`, top-level keys = bone names):

| Rig file | Total bones | Face-relevant bones |
|---|---|---|
| `rig.default.json` | 163 | `eye.L`, `eye.R`, `jaw`, plus a 12-bone tongue chain (`tongue00`…`tongue07`, with `.L`/`.R` splits) |
| `rig.game_engine.json` | 53 | **none** (only `head`, `neck_01`) |
| `rig.game_engine_with_breast.json` | similar to game_engine | none (not re-verified bone-by-bone, but game_engine already has none) |
| `rig.cmu_mb.json` | 31 | none |

So MPFB's own "default" rig is the one with bone-driven eye-look and jaw/tongue articulation;
the game-ready `game_engine` rig (the one this project's charprod pipeline actually uses, per
`mpfb_base.py`'s docstring, §6) strips all of that out and relies purely on shape keys for facial
motion — which lines up correctly with the ARKit-52-shape-key approach.

### Rigify integration — a full bone-based face rig is also available

`services/rigservice.py` has real Rigify plumbing: `generate_rigify_rig()` (calls
`bpy.ops.pose.rigify_generate()`, gated on `SystemService.check_for_rigify()` and Rigify's own
`is_valid_metarig`), `identify_rig()`, `infer_metarig_type_from_generated()`. Notably, line 1121
explicitly calls `bpy.ops.pose.rigify_upgrade_face()` ("Switch to the new face rig") during rig
generation — MPFB deliberately targets Rigify's modern face rig, not the legacy one.

The bundled meta-rig `...\mpfb\data\rigs\rigify\rig.human.json` (257 KB) contains **103 distinct
face-related bone names** (grep for brow/cheek/chin/eye/jaw/lid/lip/nose/teeth/tongue), e.g.
`brow.T.L.002`, `lid.B.R.003`, `jaw_master`, `teeth.T`/`teeth.B`, `nose_master`,
`DEF-eye_master.L/.R`, `DEF-eye_iris.L/.R`, plus a small `joint-tongue-1..4` deform chain. This is
Blender's standard advanced Rigify face rig (control bones + `DEF-*` deform bones), available by
generating from the bundled `rigify.human` meta-rig if the Rigify add-on is enabled. It is a
heavier, animator-facing control rig — not verified here whether/how it would map onto a
game-engine humanoid facial-bone standard; it is a Blender-side authoring tool, not itself a
runtime rig.

### Shape-key propagation to proxies (eyelashes, eyebrows, teeth, tongue)

`FaceService.interpolate_targets(basemesh)` (line 333) is exactly this function, already built
into stock MPFB: for every child mesh that has an associated `.mhclo` file (which covers
eyebrows, eyelashes, teeth, tongue, and clothes — anything loaded as an MPFB "clothes" asset), it
uses the MHCLO barycentric vertex mapping to compute the interpolated offset each viseme/ARKit
shape key would produce on that child mesh, and only creates the shape key on the child if the
interpolated offset exceeds `SIGNIFICANT_SHIFT_MINIMUM = 0.0001` (skips proxies the shape key
wouldn't visibly affect, e.g. it won't create a `jawOpen` key on eyebrows). It temporarily
disables the basemesh's modifiers while doing this and restores their state after.

### Eye / teeth / tongue proxies and materials

- **Eyes**: `high-poly` and `low-poly` mesh variants (`.mhclo`+`.obj`), 12 iris-color `.mhmat`
  materials (simple diffuse). There is **no separate cornea mesh/proxy** and no material or
  shader anywhere named "cornea" or "wet" (`grep -rli cornea` over the entire extensions tree:
  zero hits). Instead, `services/materialservice.py` recognizes a **procedural eye shader node
  group** (`data/node_trees/procedural_eyes.json`, 178 KB, identified via a
  `"IrisSection4Color"` node-group marker) that includes `Clearcoat` / `Clearcoat Roughness` /
  `Specular Tint` inputs — i.e. the "wet" corneal look is simulated procedurally on the single
  eyeball mesh via a Principled-BSDF clearcoat layer, not via a separate transparent cornea
  bump mesh. This is a Blender-material technique; it would need to be reproduced (e.g. baked or
  rebuilt as a clearcoat/second-UV-layer material) for a game-engine shader.
- **Teeth**: `teeth_base` + 5 `teeth_shapeNN` mesh variants, plain diffuse `.mhmat`
  (phong shader, `teeth.png` texture, no transparency/SSS).
- **Tongue**: one variant (`tongue01`), plain diffuse texture, no special wet-shader.
- Per `proxy_face_keys.py` in this project's own pipeline (§6): the eyeballs deliberately get
  **no** propagated shape keys at all — eye motion is bone-driven (`eye.L`/`eye.R`), matching
  MPFB's own `default`-rig bone set above.

### Scripted API entry points

`FaceService`, `TargetService`, `HumanService`, `ObjectService`, `AssetService` are all
static-method service classes explicitly designed to be driven from headless Python
(`bpy -b --python ...`), which is exactly how this project's own `charprod2` scripts already use
them (§6) — no GUI interaction is required for any of the facial functionality above.

---

## 4. Un-recognized facial/mocap/viseme/lipsync resources in the depots

Searched `F:\Otherreach_External_Assets\` and `F:\Otherreach_External_3D\` (filenames, all
extensions) for `*face*`, `*facial*`, `*viseme*`, `*lipsync*`/`*lip_sync*`, `*blendshape*`,
`*arkit*`, `*expression*`, `*emotion*`, `*mocap*`, `*rhubarb*`, `.bvh` files, and blendshape CSVs.

**Result: nothing found beyond MPFB's own `faceunits01` pack.** Specifically:
- The only filename hits for "face" are `polyhaven__rock_face_01` (a CC0 rock-cliff
  photogrammetry texture — unrelated) and MPFB's own faceunits pack.
- `EXTERNAL_ASSET_CATALOG.md`/`.csv` (274 KB / 91 KB, the depot's own asset ledger) contain no
  row for any viseme, lip-sync, blendshape, or ARKit product. The only facial-adjacent entries
  are body-locomotion mocap sources under the `animations` category: `100style__dataset` (CC BY
  4.0, walk/run/idle only, **not downloaded**), `accad__mocap_lab_osu` (CC BY 3.0, **not
  downloaded**), `cgspeed__bvh_conversion_of_cmu_mocap` (**not downloaded**),
  `cmu__graphics_lab_motion_capture_database` (custom permissive, **not downloaded**),
  `rokoko__free_mocap_packs` (unclear/account-gated license, **not downloaded**, flagged
  `owner_action_required`), and `rancidmilk__free_character_animations` (custom permissive,
  chained from CMU terms, **downloaded** — body locomotion/combat/dance only, no facial
  channels). None of these are facial-capture data even where downloaded.
- No `.bvh` files, no Rhubarb Lip Sync binaries/assets, and no blendshape-curve CSVs exist
  anywhere in either depot tree.

**Conclusion: this machine owns exactly one piece of licensed facial data (MPFB's CC0
`faceunits01` — the 52 ARKit shape-key targets) and nothing else facial/viseme/lipsync-related
was downloaded and left unrecognized.**

---

## 5. Existing Otherreach rigs/GLBs — do any already carry facial shapes or face bones?

All 91 `*_rigged.glb` files under `G:\UNNAMED_PHASEB\assets\rigged\*\` were parsed directly
(glTF binary JSON chunk, no Blender needed) for (a) any mesh primitive with a `targets` array
(morph targets / shape keys) or `extras.targetNames`, and (b) node names containing
jaw/eye/tongue/brow/lid/lip/cheek/nose/chin/teeth/cornea.

**Result:**
- **Zero of the 91 GLBs contain any morph targets / shape keys.** No exported character has any
  blend-shape data at all today.
- **Five GLBs have `eye.L` / `eye.R` bones** (matching MPFB's `default`-rig eye-bone names) and
  nothing else facial: `npc_kal_smith_prod_rigged.glb`, `npc_orenth_guide_prod_rigged.glb`,
  `npc_siann_archivist_prod_rigged.glb`, `npc_veth_magistrate_prod_rigged.glb`,
  `player_veth_wanderer_prod_rigged.glb` — all "_prod" variants. No `jaw`, `tongue`, `brow`,
  `lid`, or `teeth` bones exist in any of the 91 files. (One filename false-positive was
  excluded: `creature_highland_brown_bear` matches the substring "brow" inside "brown" — not an
  actual facial bone.)

So today's shipped/rigged character assets carry no facial animation data of any kind beyond
two eye-aim bones on the five "_prod" characters.

---

## 6. Other locally-present work that bears directly on this decision

This is the most consequential finding of the audit. **A working, purpose-built MPFB-based
facial identity pipeline already exists in this repo**, at
`G:\UNNAMED_PHASEB\tools\asset_pipeline\charprod2\`, well beyond anything stock MPFB provides on
its own. Evidence is the scripts' own docstrings (read, not executed — nothing in this pipeline
was run as part of this audit):

- **`mpfb_base.py`** — "*A character's clean-topology base from MPFB2 ... the MakeHuman base mesh
  at the spec's macro settings and height, **the game-engine rig**, high-poly eyes, eyebrows,
  eyelashes, teeth, tongue, a hair proxy, a starting skin, and **the ARKit-52 face units as shape
  keys** — saved as a `.blend`". This is the entry point, and it confirms the project has already
  standardized on ARKit-52 shape keys on the `game_engine` rig (matching §3's rig-bone findings).
- **`mpfb_face_fit.py` + `solve_face_fit.py` + `warp_face.py` + `delight_face.py`** — a
  from-scratch (not part of MPFB) photo-likeness fitting system: render the MPFB head through an
  orthographic camera, run Google MediaPipe Face Landmarker (478 landmarks, Apache-2.0) on both
  the render and a reference photo, solve a bounded least-squares fit of MPFB's own face target
  weights that best reproduces the reference's landmarks (`solve_face_fit.py`), thin-plate-spline
  warp the reference photo onto the MPFB head's own feature layout (`warp_face.py`), and remove
  the reference's baked-in lighting before projection (`delight_face.py`).
- **`mpfb_bake_face.py`** — projects the warped reference face from the fit camera onto the
  MPFB body's own UV layout and bakes it (color + a camera-facing-angle mask) for
  `composite_skin.py`.
- **`proxy_face_keys.py`** — an independent (not MPFB's own `interpolate_targets`, though
  functionally similar) implementation that propagates the body's face shape keys to brows,
  lashes, teeth, and tongue via nearest-triangle barycentric mapping, explicitly documented as:
  body macro-target keys (`$md*`) and `Basis` are skipped; teeth/tongue only take jaw/mouth keys
  (not surface keys); **eyeballs get none — they rotate via their own bones**, matching MPFB's
  `eye.L`/`eye.R` bone approach exactly.
- **`mpfb_assemble.py`**, **`hybrid_fit.py` / `hybrid_wrap.py` / `build_hybrid.py` /
  `hybrid_bake.py` / `hybrid_finish.py` / `compose_hybrid.py`** — final material/texture
  assembly and a parallel "hybrid" character track (an MPFB base head-wrapped onto a
  differently-sourced body).
- **`face_checks.py`** — a QA contact sheet specifically exercising `blink`
  (`eyeBlinkLeft/Right`), `jaw open` (`jawOpen`), plus smile/brow/frown ARKit shapes and neck
  range-of-motion, "the deformation the head wrap and the proxies' face keys must survive."

**This confirms the ARKit-52 shape-key set, the `game_engine` rig, and a bespoke MediaPipe-based
identity-fitting workflow are already the project's chosen approach in code**, and that
substantial engineering against exactly this problem has already happened.

**However**, per §5, none of that pipeline's output has reached the shipped `assets/rigged/*.glb`
files yet — zero shape keys exist in any of the 91 production GLBs. That means one of: the
`charprod2` facial pipeline is mid-development and not yet wired into the main
build/export step; its `.blend` outputs exist only as intermediates under `_staging`/working
directories not yet exported to GLB; or it simply hasn't been run yet for the currently-shipped
characters. This was not further verified (no execution was performed) — treat "the facial
pipeline works end-to-end" as unconfirmed, while "the code implementing it already exists,
in a self-consistent and clearly deliberate way" is well evidenced.

No independent facial mocap, expression library, or lip-sync resource was found anywhere else on
the machine beyond what is described in §§2–6.
