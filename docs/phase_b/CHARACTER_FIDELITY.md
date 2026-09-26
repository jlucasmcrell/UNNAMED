# Phase B — character visual fidelity

The owner's addition to Phase B: find exactly where the Phase-A characters lost their detail, then correct it through a reusable
pipeline — player first, then Renn Vale, Kera Voss, Sel Arien and Tavar Orr — and accept it visually.

Everything here is presentation. `--visual people=production` selects the production characters, and every Phase-B quality tier
(Low to Ultra) selects it; `people=phase_a` (or `--visual phase_a`) draws the Phase-A characters exactly. No M7 contract is touched: bodies keep the humanoid20 bones, heights and
footprints; the bones added are presentation only (fingers, eyes, a middle spine, toes).

## 1. Where Phase-A character detail was lost

Every stage from concept to screen was measured for all five characters, on the shipped files themselves
(stage audit: `G:\UNNAMED_HISTORY\phaseB\characters\STAGES_AUDIT.md`, sheets `<character>_stages.jpg`; runtime: the
`chars_default` captures' material and texture dumps). Ranked by how much of the visible loss each accounts for:

| # | Stage | What was lost | Measured |
|---|---|---|---|
| 1 | **Reconstruction: texture** | The image-to-3D texture stage re-synthesises colour in a voxel field; the concept's own pixels are never used. Its face has dark blobs for eyes, a smeared mouth, a pale mask. | The concept camera fitted to the mesh (IoU 0.95, 49.2° — the template's 49.13° Pixal3D camera) shows the mesh is pixel-aligned; rendered through that camera, the reconstruction's texture beside the concept: `characters/loss_reconstruction_texture.jpg`. |
| 2 | **Reconstruction: resolution** | A full-body reconstruction spends the voxel grid on 1.8 m; the head gets ~200 voxels. The face comes out soft and lumpy; hair a solid helmet with shards. | Clay: `characters/loss_head_resolution_clay.jpg`. The same concept's head crop reconstructed on its own has a formed face and separate hair clumps: `characters/fix_head_reconstruction.jpg`. |
| 3 | **Reconstruction: meshing** | The unsigned-distance-field remesh wraps every surface in a closed thin shell: an outer skin and an inner one facing in, enclosed and never visible. | Player: 20,942 of 39,586 triangles (53%) and 2.61 of 5.13 m² interior — half the triangle budget and half the atlas spent on nothing. |
| 4 | **UV layout** | ~950 sliver charts (largest 488 faces), 51–59% of the atlas used — half of that on the hidden shell — at one texel density everywhere. | Head 3–10% of the UV at the body's density: 4.6–6.6 px/cm on every face (player 6.6). Albedo capped at 2048 by the texture stage. |
| 5 | **Materials** | One material per character, one roughness field (~0.6 everywhere; the metal channel noise), no skin, hair, cloth, leather, metal or eye response. | Every character: 1 surface, 1 StandardMaterial3D. |
| 6 | **Eyes** | No eyes: a painted surface. Nothing to catch light, nothing to aim. | — |
| 7 | **Rig** | 20 bones: no fingers (hands cannot grip), no eyes, two spine bones. Renn and Tavar: sleeves and coat fused to the body in the reconstruction, weighted by the distance fallback (99.7% / 99.3% of vertices blending >1 bone); every re-fit made the tear worse. | Stage audit §Renn/§Tavar. |
| 8 | **Resize step (Kera, Sel)** | `glb_resize_textures.py` halved their 4096 normal/ORM bakes to 2048. Renn and Tavar never had 4096 (older tier). | — |
| 9 | **LODs** | Built and gated for every character, never drawn: `SkinnedModel` had no LOD path, so every person drew its full mesh at any distance. | — |
| — | Godot import | *Not* a loss stage: BC7 at full resolution with mips, anisotropic filtering, LOD0 drawn. | Runtime dump: albedo 2048 BptcRgba, 11 mips. |

The owner's instinct was right: the characters were not simply being "regenerated wrong". Four separate stages each threw detail
away, and the concept already held a readable face (its eyes are ~10 px/cm) that no stage ever used.

## 2. What corrected it — the production character pipeline

`tools/asset_pipeline/charprod/` — one driver, one spec per character (`build_character.py specs/<name>.json`), every stage
resumable and reported. From a humanoid20-fitted body and its concept:

| Stage | Fixes | How |
|---|---|---|
| `figure_mask`, `fit_camera` | — | The concept's figure (BiRefNet, the template's own model) and the camera under which the mesh's silhouette matches it. |
| `clean_mesh` | #3 | A face is interior when no ray from it escapes the mesh; large interior sets deleted, small holes filled. |
| `head_graft` (+ head reconstruction) | #2 | The concept's head crop reconstructed on its own (same seed and tier: ~5× the face's voxel density), placed by the two cameras (rotation, pixel-footprint scale, translation fitted at the neck), cut at the neck — the body loses what is connected to its old face inside a cylinder, so its fused collar and shoulders stay whole — bridged, the bridge oriented and relaxed into a collar, weights from the body's nearest head surface. |
| `eyes`, `eye_texture` | #6 | Lid openings cut along the concept's MediaPipe eyelid contour (mesh refined around each eye), 12 mm eyeballs behind them gazing along the concept's iris rays (player IPD 64 mm), lid walls closing the sockets; an iris of radial fibres in the concept's iris colour. |
| `uv_layout` | #4 | The face as one chart whose UVs are the concept camera's projection; the body charted by bone region, each cut in two along its axis and flattened by SLIM (LSCM where SLIM collapses; angle charts where hair folds); face 2.5×, head and hands 1.4× the body's density; 4096 atlas. |
| `bake_transfer` | #4, #8 | The reconstruction's albedo, ORM and tangent-space normals carried onto the new layout on the same mesh (per part: the head reads its own maps); the full 4096 normal bake used. |
| `project` | #1 | Every texel the concept sees takes the concept's own colour, through each part's own camera (the head through the head crop's), de-lit (key light and side rim lobes fitted and divided out), feathered into the colour-matched reconstruction texture where the concept cannot see. |
| `segment_zones`, `zones_to_mesh`, `orm_zones` | #5 | Material zones from text prompts (Grounding DINO + SAM 2, offline, Apache-2.0; nothing ships) carried onto the faces; each zone's roughness re-centred on its kind, metal set by kind; skin highlights the studio light left soft-clipped. |
| `assemble` + `CharacterMaterials` | #5 | One material per zone on the one atlas (skin, hair, cloth, cloth_heavy, leather, metal, eye); in Godot, by name (`Art/character_materials.json`): skin subsurface scattering, cloth rim sheen, leather clearcoat, a wet cornea. |
| `hand_landmarks`, `rig_production` | #7 | The humanoid20 bones kept (every clip and gameplay hook still works) plus spine_mid, toes, 30 finger bones and 2 eye bones (55): finger joints from the concept's MediaPipe hand landmarks lifted onto the hand, rolled so a grip is a turn about X; weights redistributed, never re-solved (fingers by a smooth thumb/blade split, lateral blend and along-finger partition, smoothed on the welded surface). `HandGrip` closes the holding hand; NPC eyes follow the player. UAL locomotion retargeted one-to-one onto the rig, fingers included (`retarget_maps/quaternius_ual_prod.json`). |
| `lods` | #9 | Skinned LOD1/LOD2 in the same GLB on the same skeleton and atlas (the face's skin protected); switched by distance with a dithered cross-fade — LOD0 to 18 m, past any dialogue or third-person framing. |

The pipeline is character-agnostic: zones are text prompts in the spec, so a science-fantasy character's crystal or chrome is
one more zone line (`material: metal`, or a new kind in `character_materials.json`).

## 3. Results

### The player

| | Phase A | Production |
|---|---|---|
| Triangles (visible) | 39,586 (19,181) | 49,955 LOD0 (all visible), 14,986 LOD1, 8,597 LOD2 |
| Face | reconstruction texture, 6.6 px/cm | the concept's own pixels, 36.8 px/cm |
| Body | 6.6 px/cm | 14.8 px/cm |
| Atlas | 2048 albedo, ~25% used on visible surface | 4096, 40% used, all visible |
| Materials | 1 | 6 (skin, cloth, cloth_heavy, leather, hair, eye) |
| Eyes | painted | eyeballs on eye bones |
| Bones | 20 | 55 (fingers, eyes, spine_mid, toes) |

`characters/accept_player.jpg` (concept, raw reconstruction, production mesh, Godot close / dialogue / third person),
`characters/player_production_views.jpg`, `characters/rig_finger_grip.jpg` (the hand at rest, and curled into a grip).

### All five (`character_report.py specs/*.json`)

| Character | Phase A triangles (visible) | Production LOD0 / LOD1 / LOD2 | Face px/cm | Body px/cm | Materials | Bones |
|---|---|---|---|---|---|---|
| Player (player_veth_wanderer_prod) | 39,586 (19,181) | 49,955 / 14,986 / 8,597 | 36.8 | 14.8 | 6: skin, cloth, cloth_heavy, leather, hair, eye | 55 |
| Kera Voss (npc_kal_smith_prod) | 38,944 (18,628) | 58,468 / 17,540 / 13,615 | 33.6 | 15.0 | 5: skin, cloth_heavy, leather, metal, eye | 55 |
| Sel Arien (npc_siann_archivist_prod) | 39,078 (19,604) | 39,407 / 11,822 / 7,116 | 23.4 | 12.0 | 5: skin, cloth, leather, hair, eye | 55 |
| Renn Vale (npc_veth_magistrate_prod) | 24,538 (4,474) | 36,876 / 11,062 / 6,268 | 26.6 | 11.0 | 7: skin, cloth, cloth_heavy, leather, metal, hair, eye | 55 |
| Tavar Orr (npc_orenth_guide_prod) | 24,607 (7,532) | 48,195 / 14,458 / 4,072 | 23.2 | 10.9 | 5: skin, leather, cloth_heavy, hair, eye | 55 |

Phase A drew every one of them at 4.6–6.6 px/cm with one material and 20 bones. Sheets (concept, raw reconstruction,
production mesh, Phase-A and production faces, Godot close / dialogue / third person for both): `characters/accept_<name>.jpg`.

* **Kera** — the same body as Phase A (it was sound), cleaned, head grafted, projected. The face reads as the concept's (brow,
  broken nose, set mouth) where Phase A's was a smeared mask; the apron, straps and bracers are separate leather and metal zones.
  MediaPipe found no hand in her concept, so her finger bones come from the template fallback (as do Renn's).
* **Sel** — the same body, cleaned and grafted. Eyes, brows and mouth read at dialogue distance; the dress, sash and satchel are
  separate cloth and leather zones, the satchel and sash kept off the back of the dress (`"wraps": false`).
* **Renn — the sleeve correction.** Phase A's Renn was reconstructed from a concept with his arms at his sides holding a book: the
  reconstruction fused the robe's sleeves and the arms into one mass (4,474 visible triangles) and every re-fit tore it. It was a
  mesh-production defect, so the mesh was remade rather than preserved: a new arms-clear bind concept of the same character
  (`renn/concepts/npc_veth_magistrate_bind_s333.png`), reconstructed at the hq tier, cleaned, rig-fitted, then the full pipeline.
  The sleeves are now open garment geometry around separate arms; the chain of office is a metal zone, the belt leather.
* **Tavar — the same correction.** His coat, sleeves and body were one fused mass (7,532 visible triangles); remade the same way
  from an arms-clear bind concept (`tavar/concepts/npc_orenth_guide_bind2_s333.png`). The long coat is its own zone (`"fill": true`
  claims every figure pixel no other zone does), with the rope, belt and fingerless gloves separate.
* **Their motion.** The Phase-A NPC clips were baked on the old 20-bone rigs; played on the production rigs they left each body in its
  bind A-pose. Under `people=production` each NPC plays the Quaternius UAL idle and talking idle (CC0) retargeted one-to-one onto
  its own rig (`people_clips_by_visual_option`), and Tavar also the UAL walk, jog, sword attack, hit and death, at the player's
  measured strides scaled by his leg length (walk 1.18 m/s, run 2.8 m/s).

## 4. Known limits (honest)

* **The fingers are a mitt.** The reconstruction fuses the four fingers into one blade (the thumb is separate). The finger bones
  curl it as one piece, which reads as a closed grip at play distance but not as separate fingers up close. Separate fingers need a
  hand mesh the reconstruction does not make (a sculpted or kit hand fitted to the wrist).
* **Concept lighting.** A concept's rim light clipped to white cannot be divided out; the player's left cheek edge keeps a
  pale band. Concepts rendered for this pipeline are asked for flat front light with no rim light.
* **The neck seam** reads as a crew collar; on a bare-necked character it would show.
* **No facial animation**: eyes aim, but there is no jaw or blink yet (no talk clip data drives them).
* **Open-palm gestures show the mitt.** The talking idle turns the palms up with the fingers open; on Renn at dialogue distance his
  hands then read as flat paddles (anatomically sized, but one blade). Same fix as the first item.
* **Renn's collar points** are jagged: the white shirt-collar tips beside the tie show torn edges at close range (reconstruction
  geometry, not texture).
* **Tavar's attack** is the UAL sword swing: no CC0 spear thrust in the cleared packs.
* **No secondary motion**: coats, robes and hair do not swing (no spring chains yet). Renn, Sel and Tavar's toe bones carry no
  weight (their boots move with the foot).
* **Texture cache** is built (557 textures, 0 failed, 1,085 MB BC7) by the editor-binary run with `--texture-cache`; the production
  characters load from it.
