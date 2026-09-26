# Facial Pipeline Bakeoff (Phase B remediation, first facial checkpoint)

Date 2026-09-26. Owner interventions: "Facial asset / facial animation pipeline reset" and "Free-first standardized character / face
pipeline bakeoff". The companion report on the character factory and reuse is
[CHARACTER_FACE_PIPELINE_BAKEOFF.md](CHARACTER_FACE_PIPELINE_BAKEOFF.md). Reviews are blind (labels shuffled by a seeded hash;
keys in each `key.json`), with Gemini (gemini-3.8-flash) and Pegasus 1.5; raw outputs are under `external_reviews/03_*` to `12_*`.
Staging (gitignored): `assets/_staging/charprod2/player/bakeoff/`, `assets/_staging/charstd/`.

Acceptance language follows [ACCEPTANCE_CONTRACT.md](ACCEPTANCE_CONTRACT.md). Nothing in this report reached normal gameplay: every
face result is an offline render or a Godot proof scene (G1-G3 at most for gameplay; G6 is the owner's).

## 1. Current facial-pipeline state

| Candidate | What it is | Status |
|---|---|---|
| Rejected Phase B player (`player_veth_wanderer_prod`, commit 22af661) | AI reconstruction + grafted head, painted eyes, no face channels | Frozen baseline. Codex audit H01: defective neck/collar geometry in clay. No blink, jaw or expression is possible. |
| CUSTOM_PIPELINE_CANDIDATE (`candE_hybrid`) | MPFB body, rig, eyes, teeth, ARKit channels; head wrapped onto the rejected head's sculpt (`hybrid_wrap.py`); textures, hair and garments baked across from the rejected model | **Frozen** at this checkpoint (per the owner). Not reuse-proven (no second face run). |
| MPFB targets (`candA1_targets`, `candA2_native`) | MPFB face modifiers solved to the source's landmarks (`solve_face_fit.py --points3d`) | Research only; caricatured at full weights, still weak at capped weights. Superseded by bounded presets. |
| **MPFB standard** (`charstd`, `humanoid_masculine_a`) | MPFB body at character macros + a bounded modifier preset (no solver) + stock MPFB hair/skin/eyes/teeth/tongue + ARKit-52 and both viseme sets, carried to the components by MPFB itself; optional identity skin texture from the concept portrait | Faces 1-3 built with zero per-face code. Godot contract proven in a proof scene. |
| Blender Human Base Meshes v1.4.1 | CC0 realistic heads/bodies | Rejected by inspection (section 9). |

## 2. Current visible defects (own inspection + reviewers)

- **Custom wrap**: wide, startled stare with excess sclera (every reviewer, every round) - the source's lid geometry is wrapped
  onto MPFB's eyeballs; blotchy projected-texture seams at the neck base; hair cards stiff.
- **MPFB standard, untextured**: young, clean, plastic skin; weak identity; generic stock hair (the player's `cortu_short_messy_hair`
  reads as floating curls).
- **MPFB standard, textured**: identity improved (stubble, grime, age), but painted-on stubble; the projected portrait's eye colours
  land on the closed upper lids (a reddish smear in a blink); the reviewers still rate it borderline/prototype.
- **All MPFB faces**: the resting mouth is slightly parted in MPFB's neutral - held shut by a rest offset `mouthClose 0.3` (section 13).

## 3. Existing local facial assets and tools found

Full audit: [pipeline_research/FACIAL_LOCAL_AUDIT.md](pipeline_research/FACIAL_LOCAL_AUDIT.md). In short: MPFB 2.0.17 (current
upstream), `faceunits01` (ARKit-52, CC0) installed; **no viseme pack was installed** (the code lists them, the data was absent);
no facial mocap, viseme, lip-sync or expression data in either depot; none of the 91 shipped rigged GLBs carries a shape key.

## 4. MPFB expression capability (verified in code and by rendering)

- `FaceService.load_targets(basemesh, microsoft, meta, arkit)` loads the channel shape keys; `interpolate_targets()` carries every
  channel onto brows, lashes, teeth, tongue, eyes and hair through each proxy's MHCLO correspondence. **This replaced my in-house
  `charprod2/proxy_face_keys.py` (retired).**
- `set_expression / save_expression / load_expression / apply_expression_file` - a weighted expression stack on the ARKit channels.
- `configure_lip_sync` maps visemes02 onto the third-party "Lip Sync" add-on (not installed; not needed for the contract).
- The `default` rig has eye, jaw and tongue bones; the `game_engine` rig (ours) has none - our skeleton adds `eye.L/R`
  (`charstd/standardize_rig.py`). Rigify with a full face control rig exists in MPFB (not used: the contract is shape-key based).

## 5. Facial-component inventory (reusable, stock MPFB, CC0 system assets)

| Component | Asset | Notes |
|---|---|---|
| Eyeballs + cornea shell | `eyes/high-poly` (12 iris materials) | One mesh; the cornea shell's texels are clear - exported alpha-tested (`export_character.py`) |
| Teeth | `teeth/teeth_base` | Carries jaw/mouth channels via MPFB |
| Tongue | `tongue/tongue01` | Channels via MPFB; no tongue bones on our rig |
| Brows | `eyebrows/eyebrow002` | Dyed per character |
| Lashes | `eyelashes/eyelashes01` | Follows blink |
| Tear line / eye occlusion | none | Gap; a CC0 procedural eye project is noted in the external research, not adopted |
| Facial hair | none in MPFB | Gap: stubble is texture only (identity texture) |

## 6. Expression / face-unit inventory

ARKit-52 face units (`faceunits01`, 54 targets incl. aliases) present on every face; verified rendering: blink (each eye), jaw open,
mouth close, smile, frown, brow up/down, pucker, funnel, eye look (shape + eye bones). Sheets:
`characters/face_bakeoff/mpfb_reference_motion.jpg`, `characters/standard/*_motion.jpg`.

## 7. Viseme inventory

Installed this checkpoint from the official MakeHuman functional packs (CC0, author Mika Suominen; SHA-256 in
`F:/Otherreach_External_3D/mpfb/asset_packs/SHA256SUMS.txt`): `visemes01` (22 Microsoft-style) and `visemes02` (15 Meta-style).
All 15 Meta visemes rendered on the reference head: `characters/face_bakeoff/mpfb_reference_visemes.jpg`. The speech segment of
every bakeoff video is driven by Meta visemes.

## 8. Facial animation assets already owned/downloaded

None before this checkpoint. New: one captured performance (MediaPipe, section 14) as channel curves; they are data, not a library.

## 9. External research

Full report with sources and eleven explicitly UNVERIFIED items:
[pipeline_research/FACIAL_EXTERNAL_RESEARCH.md](pipeline_research/FACIAL_EXTERNAL_RESEARCH.md). Decisive facts:

- **Audio2Face-3D** was open-sourced in 2025: SDK MIT, weights under the NVIDIA Open Model License (commercial use, NVIDIA claims no
  output ownership). Outputs ARKit weights; baked curves ship with no NVIDIA runtime. NVIDIA GPU needed at production time only. No
  official Blender plugin (Maya/UE5 only). **Not integrated** (the owner: research, not first dependency).
- **Rokoko** face capture: Pro tier ~$50-70/month + iPhone with TrueDepth; ownership of recorded output UNVERIFIED.
- **Reallusion CC5 / iClone 8**: ExPlus profile = ARKit-52 + 11 tongue; export to games allowed under the Standard License; CC5
  perpetual ~$299 (secondary source, UNVERIFIED); AccuFACE/AccuLIPS need iClone. Chain to Godot is FBX via Blender.
- **MetaHuman**: usable outside Unreal since the 2025 EULA (UNVERIFIED primary text: login wall), but faces are bone-driven
  (RigLogic), no blendshapes - no practical Godot path without baking in Unreal.
- **Blender Human Base Meshes 1.4.1** (downloaded, CC0, SHA-256 in `F:/Otherreach_External_3D/blender_human_base_meshes/`):
  the realistic heads carry **zero shape keys**, no rig, no teeth or tongue; adopting them means authoring every channel for each
  face family (or buying Faceit, $78) - a second production system. Rejected as a control by inspection.
- Research-only face models (DECA/FLAME/EMOCA class) and academic speech datasets: not adopted (non-commercial terms).

## 10. License / cost table

| Item | License / terms | Cost | Status |
|---|---|---|---|
| MPFB 2.0.17 add-on | GPL-3 (tool); output not GPL-encumbered | free | installed |
| MakeHuman base mesh, targets, skins, system assets | CC0 - official FAQ: models usable in commercial and closed-source games, and the assets may be used to build our own character generator (copies of the pages kept in `F:/Otherreach_External_3D/mpfb/LICENSE_EVIDENCE/`) | free | adopted |
| faceunits01, visemes01, visemes02 | CC0 (functional packs page) | free | installed |
| Hair `cortu_short_messy_hair` | CC0 (author Cortu Johnstone) | free | player |
| Hair `short02`, `short04` | CC0 (MakeHuman 2020 release) | free | Tavar, Renn |
| Tank `elvs_male_tankshirt1`, tunic `drednicolson_asymmetric_tunic_and_sash` | CC-BY (attribution recorded in each garment descriptor) | free | garments |
| MediaPipe Face Landmarker | Apache-2.0 | free | used (capture) |
| ambientCG Leather014 | CC0 | free | vest material |
| Blender Human Base Meshes 1.4.1 | CC0 | free | rejected |
| Audio2Face-3D | MIT + NVIDIA Open Model License | free | researched only |
| KeenTools FaceBuilder | commercial; 15-day trial; owner-reported low monthly cost | trial | **needs owner approval (section 25)** |
| Reallusion CC5 / iClone 8 | commercial | ~$299+ | researched only |
| Rokoko Face Capture | subscription | ~$50-70/mo | researched only |

## 11. Candidate production pipelines

- **A (open standard)**: MPFB topology + ARKit/visemes + stock components + bounded modifier presets + identity texture from a
  concept portrait; performance from MediaPipe (now) or Audio2Face (later). Built and measured.
- **B (commercial)**: CC5/iClone with ExPlus/HD profiles, AccuFACE/AccuLIPS, FBX to Blender to Godot. Researched only.
- **C (hybrid)**: MPFB standard contract (topology, rig, channels, components - kept) with a better identity source for the head
  shape and skin: FaceBuilder-style photogrammetric fitting (trial pending approval), or an artist sculpt pass per hero.
- **D (current custom)**: the frozen wrap candidate.

## 12. Player benchmark

The same test sequence for every candidate (`charprod2/face_bakeoff.py`): static (front, three-quarter, profile, rear/neck, eyes,
mouth), motion (13 poses incl. both blinks, jaw, mouth closed, smile, concern, brows up/down, eyes left/right, head left/up/down),
the 15 visemes where present, and an 8 s video (blinks, saccades, a viseme sentence, smile, concern, brows, head turn).

## 13. Static results

Reviews 03 (five candidates) and 09 (final four), reference R = the concept portrait.

| Candidate | Gemini identity | Gemini production | Key defects named |
|---|---|---|---|
| Rejected Phase B | best (1st in 03 and 09) | borderline | (textured stills hide the clay defect the Codex audit proved; no face channels at all) |
| Custom wrap | 2nd | borderline | startled stare, excess sclera, neck texture seams |
| MPFB standard, textured | 3rd | borderline | stubble painted on, eyes flat, simplified hair |
| MPFB standard, untextured / MPFB targets | last | prototype | young, plastic, generic; targets version caricatured |

Pegasus ranked the rejected baseline and the wrap first in static; its explanations were generic and it once rated every candidate
the same way it rated the best - treated as weak evidence (the owner's caution).

## 14. Motion results

- All MPFB-standard faces and the wrap blink, open the jaw, smile, frown, raise and lower the brows and look left/right cleanly
  (sheets in `characters/`). Review 10 claimed the textured standard player's "left blink is broken": that tile is the deliberate
  one-eye blink; the both-eyes tile closes both (checked).
- **Performance reuse (the contract proof)**: one MediaPipe capture of a real performance (`charstd/capture_face_performance.py`,
  145 frames, ARKit names + head rotation) played unchanged on four faces (`play_face_performance.py`):
  `characters/standard/performance_a_on_four_faces.mp4`. 51 channels driven on 7 meshes of each face; nothing indexes vertices.
- Reviews 07/12 (video): the wrap and Renn "transfer smoothly"; standard faces "stiff, lacking fleshy elasticity" in places.

## 15. Gemini review (summary)

Consistent across seven reviews: identity follows the rejected model's sculpt and textures; the wrap keeps most of that identity
but its eyes are wrong; the parametric standard face is clean and animatable but generic; texture lifts it to borderline.

## 16. Pegasus review

Raw outputs kept. Useful as a second vote on rankings; its descriptions repeat the rubric ("excellent facial details") and do not
name specific defects - flagged as shallow per the owner's instruction.

## 17. Godot compatibility (proof scene, not gameplay)

`export_character.py` exports the standard characters as .glb (helper geometry removed, identity baked into the basis, only the 89
face channels kept as morph targets, skin/eyes as plain glTF materials, hair/brows/lashes/eyes alpha-tested). Godot 4.7.2 imports
both test characters with **89 identically named channels each, 57-58 bones, `eye.L` present**; one script drives blink, smile, a
viseme, head/neck turn and eye bones **by name** on both at once:
`characters/standard/godot_face_contract_proof.mp4`, `godot_blink_smile.jpg`, script `godot_face_contract_proof.gd`.

## 18. Performance implications

The body carries 89 morph targets on ~10k vertices plus the components; Godot blends only non-zero shapes on the GPU. Plan: full
channels within dialogue range (LOD0), blink + jaw only in third person, no facial evaluation beyond ~18 m (the existing LOD0 limit).
Not measured in gameplay yet.

## 19. Character-creation implications

Every identity operation of the standard route is a slider value: MPFB macros (range-limited per archetype) and face modifiers
(validated against a 0.5 ceiling and MPFB's own target list by `build_character.py`), hair/brow/iris choice, colour, skin texture.
No solver, wrap or code runs per face - reducible to a runtime creator (the targets are CC0 and may ship).

## 20. Race / morphology implications

A: can share `humanoid_masculine_a` (and a feminine counterpart) - Veth, Siann (scaled). B: can adapt the contract with an
archetype of their own (Kal, Vaskaal - their hands/wings already need their own fit families). C: needs a distinct facial topology
(Mor, Constructed, Ondrek heavy forms). One archetype per morphological family.

## 21. Production-time comparison

See [CHARACTER_FACE_PIPELINE_BAKEOFF.md](CHARACTER_FACE_PIPELINE_BAKEOFF.md) section 14.

## 22. Recommended facial standard

- Topology/rig/components: **MPFB base mesh + our production skeleton + stock MPFB eyes/teeth/tongue/brows/lashes**.
- Channels: **ARKit-52 + Meta visemes (15)**; Microsoft visemes kept for tools that emit them. Rest offset `mouthClose 0.3`.
- Performance data: channel-named curves (MediaPipe now; Audio2Face after its license review; hand keys for authored beats).
- Identity: **not solved by the free route** - see the verdict below.

## 23. Custom code that remains genuinely necessary

`standardize_rig.py` (bone names, eye bones, sockets), `build_character.py` (config to character), `export_character.py`
(glTF-safe materials and channel-only morphs), `capture_face_performance.py` / `play_face_performance.py` (adapters to the
contract), `identity_texture.py` (drives existing tools), the QA renderers. All generic; none per face.

## 24. Custom work that can be retired

`proxy_face_keys.py` (MPFB interpolate_targets does it), the landmark target solve for identity (`solve_face_fit.py solve`,
`lift_landmarks.py` - caricatures), the head wrap and its bake/compose chain (`hybrid_*.py`, `compose_hybrid.py`) unless the owner
chooses the custom route, and every per-garment coverage heuristic added to `mpfb_assemble.py` (superseded by authored regions).

## 25. Verdict and owner decisions

**Recommendation: HYBRID** - keep the open MPFB standard for everything it proved (reuse, animation contract, components, Godot),
and escalate identity only. The free route **passes animation and reuse and fails identity quality**: three faces cost 13-16 s of
build each with zero new code and play the same performance, but blind reviewers call the standard faces prototype/borderline
with weak likeness, while the only candidates they rate closer to the concept inherit the rejected (geometrically defective) head.

Decisions needing the owner:
1. **KeenTools FaceBuilder 15-day trial** - installing it means accepting KeenTools' license agreement on your behalf, which I will
   not do without your explicit yes. Test plan if approved: same references, player + one second face, clay/texture/blink/jaw/
   smile/speech, measure likeness and minutes per face, and whether its head can drive our MPFB topology or must be its own face
   family.
2. Whether to spend artist time on a per-hero sculpt pass (brows, cheekbones, jaw) on MPFB topology instead of software.
3. Audio2Face-3D pilot on ~20 lines once dialogue audio exists (free; production machine only).
4. No purchases were made; nothing needs buying to continue the open standard.
