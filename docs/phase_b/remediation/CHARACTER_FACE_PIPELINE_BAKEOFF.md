# Character / Face Pipeline Bakeoff - the free-first factory test

Date 2026-09-26. Owner direction: "Free-first standardized character / face pipeline bakeoff". Companion reports:
[FACIAL_PIPELINE_BAKEOFF.md](FACIAL_PIPELINE_BAKEOFF.md) (facial detail, reviews, licenses) and
[CHARACTER_ASSET_STANDARD.md](CHARACTER_ASSET_STANDARD.md) (the contract). Tools: `tools/asset_pipeline/charstd/` (the standard) and
`tools/asset_pipeline/charprod2/` (the frozen custom candidate and the shared QA renderers). No purchases; no trial started.

## 1. Current custom candidate (frozen as CUSTOM_PIPELINE_CANDIDATE)

`charprod2/build_hybrid.py`: MPFB body + rig + face channels; the rejected player's head sculpt wrapped on (`hybrid_wrap.py`:
landmark similarity + thin-plate spline, then coarse-to-fine surface pull); its hair shell, garment and skin textures baked across
(`hybrid_fit.py`, `hybrid_bake.py`, `compose_hybrid.py`, `hybrid_finish.py`). Outputs in `assets/_staging/charprod2/player/candE_hybrid/`.

- Strengths: best identity of any animatable candidate (it borrows the rejected model's likeness); clean MPFB neck and topology;
  blink/jaw/expressions/eye bones work.
- Failures: the startled stare (source lids on MPFB eyes); neck texture seams; needs a source character mesh per identity - i.e. an
  earlier reconstruction - so its identity quality is capped by those reconstructions and it inherits their existence as a
  prerequisite.
- The 86 mm displacement flagged by the owner was a smoothing artefact at the crown (vertices dragged toward the source's hair
  shell); after the rewrite (landmark alignment first, skin-only pulls, eye/mouth zones held) the maximum is 71 mm, at the crown
  under the hair, median 14 mm; blink, jaw and expressions verified after the wrap (`characters/face_wrap_candidate/`).
- Engineering: roughly a working day of agent time and ~15 scripts. A second face was **not** run through it (it needs Renn's
  rejected reconstruction as its source); it is therefore **not reuse-proven** and not recommended.

## 2. One-time versus per-face work

| Step | Standard route (charstd) | Custom route (charprod2) |
|---|---|---|
| Archetype body, skeleton, regions | one-time (`mpfb_base.py`, `standardize_rig.py`, `author_regions.py`) | one-time |
| Garments | one-time per garment (`author_garment.py` + descriptor) | per character (baked from the source) |
| Identity shape | **per-face configuration**: an MPFB preset of ~25 bounded slider values | per-face algorithm run on a source mesh (wrap, ICP, bakes) |
| Identity skin | per-face configuration: a portrait + an opacity (`identity_texture.py` runs existing tools) | per-face bake from the source |
| Hair, brows, iris, colour | per-face configuration (stock assets + values) | per-face (source hair shell cut and rebound) |
| Face channels | automatic (MPFB) | automatic (MPFB) |
| QA | automatic (`face_bakeoff.py`, `garment_check.py`) | automatic |
| Artistic cleanup | optional (none done) | needed (stare, seams) |

## 3. MPFB version and license

MPFB 2.0.17 (current upstream). Official MakeHuman pages (saved in `F:/Otherreach_External_3D/mpfb/LICENSE_EVIDENCE/`): core assets
(base mesh, targets, skins, system assets) are CC0; models made with it may be used in commercial and closed-source games; the
assets may be reused to build another character generator. Third-party assets checked one by one (table in the facial report).

## 4. Installed face-unit / viseme packs

`faceunits01` (ARKit, was installed), `visemes01` and `visemes02` (installed today from the official CC0 functional packs; hashes
recorded). Eyes, brows, lashes, teeth, tongue, hair from MPFB's CC0 system assets.

## 5. Blender Human Base Meshes review

v1.4.1 downloaded (CC0). Realistic heads have no shape keys, rig, teeth or tongue: every channel would have to be authored per face
family. Not built (the owner's rule: only if the topology clearly justifies a second system; it does not beat MPFB's).

## 6. MediaPipe review

Apache-2.0; Face Landmarker gives 52 ARKit-named coefficients + head pose per frame. Used for **performance capture**, not topology
surgery: `capture_face_performance.py` turns ordinary video into channel curves. Its landmarks also drive the identity texture warp
(an existing tool). Limits: depth-poor on one view; blink and smile reliable, jaw modest; needs a clear, well-lit frontal face (the
first local clip tried - a dark scene - gave no detections).

## 7. Free identity-fitting research

Tried, in order: (a) MPFB face modifiers solved to landmarks - caricature at full weights, generic at capped weights; (b) bounded,
art-directed presets (~25 sliders, 0.1-0.45) - clean but generic; (c) presets + identity skin texture from the concept portrait -
best free result (borderline). Shortlist of other free/commercial fitting tools is in the external research; none free was found
that produces game topology with commercial output rights and beats (c) without new algorithms.

## 8. Face #1 (the player)

`charstd/characters/player_veth_wanderer.json` + `identities/player_veth_wanderer.mpfb_preset.json`. Build 13 s; identity texture
29 s. Result: `characters/standard/player_veth_wanderer_textured_*.jpg`. Reviewers: borderline, moderate likeness ("stubble painted
on, eyes flat, hair simplified").

## 9. Face #2 reuse result (Renn Vale)

Same archetype, same tools, a new character file and preset (21 values) and his concept portrait. **New pipeline code: none.** One
data error (eye/cheek target names) cost two failed builds; a generic name check was then added to the builder (a fix, not a
per-face feature). Build 13-14 s; texture ~30 s; authoring the preset ~10 minutes against the portrait. Review 11: moderate
resemblance (grey hair, stern), cyan iris and rounder jaw diverge; production prototype.

## 10. Face #3 reuse result (Tavar Orr)

Same process. **New pipeline code: none.** Build 14-16 s. Distinct from faces 1-2 (square jaw, heavy brow, dark short hair); the
beard of the concept is missing (no facial-hair asset - texture only).

## 11. Facial animation reuse

One captured performance played on faces 1-3 and on the custom candidate without any per-face change
(`characters/standard/performance_a_on_four_faces.mp4`); the same 8 s bakeoff sequence and all 15 visemes run on every face. Godot
drives the same names on two characters at once (`godot_face_contract_proof.mp4`).

## 12. Character-creator compatibility

Everything per face is a value the player could set with sliders (macros within the archetype's morph range, face modifiers under
the 0.5 ceiling, stock hair/brows/iris, colours, a skin preset). The identity texture from a portrait is a production-side step for
authored characters only; a creator would offer skin presets instead.

## 13. Godot compatibility

Proven in a proof scene (not gameplay): import, 89 channel morphs per character, bones by name, blink + gaze + head motion combined.

## 14. Time / cost comparison

| Pipeline | Software cost | Face 1 effort | Face 2 effort | Face 3 effort | New code | Visual quality (blind) | Animation quality | Creator-compatible |
|---|---|---|---|---|---|---|---|---|
| Custom wrap (frozen) | $0 | ~1 working day of engineering + minutes of compute | not run (needs a source reconstruction) | not run | ~15 scripts | borderline; stare defect | good (MPFB channels) | no (needs a source mesh) |
| MPFB standard, untextured | $0 | factory: ~half a day; face: 13 s build + ~10 min preset | 13 s + ~10 min | 14 s + ~10 min | none per face | prototype, generic | good; "a little stiff" | yes |
| MPFB standard + identity texture | $0 | + 29 s texture | + ~30 s | + ~30 s | none per face | borderline, moderate likeness | good | presets yes; portrait texture is production-side |
| MediaPipe capture (contribution) | $0 | one capture ~1 min | reused as-is | reused as-is | 2 adapters, one-time | n/a | real blinks/smiles/head; jaw weak | n/a |
| KeenTools FaceBuilder trial | trial, then ~$20/mo (owner's figure) | not reached | - | - | - | - | - | - |
| Reallusion CC5/iClone | ~$299+ (UNVERIFIED) | researched only | - | - | - | - | - | proprietary assets |

## 15. Free pipeline verdict

**Passes**: one topology for many identities; reusable components; the same channel contract, blink, expression and viseme tracks
on every face; identity by configuration; face 2 far cheaper than face 1; face 3 with zero identity-specific code; the same Godot
contract. **Fails**: identity and realism - blind reviewers rate the standard faces prototype/borderline with weak-to-moderate
likeness. Per the owner's rule this is the point to escalate identity only.

## 16. Low-cost trial verdict

Not reached: starting the FaceBuilder trial means accepting KeenTools' license agreement, which needs the owner's explicit yes.

## 17. Commercial alternatives requiring owner approval

KeenTools FaceBuilder (trial, then subscription); Faceform Wrap (topology transfer - would replace `hybrid_wrap.py`, only relevant
if the custom route is chosen); Reallusion CC5/Headshot (would replace most of the factory but binds us to proprietary assets and an
FBX chain); Faceit ($78, only if a non-MPFB face family is ever needed).

## 18. Recommended production contract

[CHARACTER_ASSET_STANDARD.md](CHARACTER_ASSET_STANDARD.md): MPFB archetype + production skeleton + authored regions + garment
descriptors + ARKit/viseme channels + identity presets; identity quality to be raised by the approved escalation.

## 19. Custom code that can be retired

See the facial report section 24.

## 20. Remaining genuinely necessary custom code

`charstd/`: `standardize_rig.py`, `author_regions.py`, `author_garment.py`, `garment_check.py`, `build_character.py`,
`export_character.py`, `identity_texture.py`, `capture_face_performance.py`, `play_face_performance.py`, `common.py`; QA renderers
`charprod2/face_bakeoff.py`, `face_checks.py`. All generic. Nothing was mass-propagated: Renn and Tavar here are reuse proofs, not
production replacements.
