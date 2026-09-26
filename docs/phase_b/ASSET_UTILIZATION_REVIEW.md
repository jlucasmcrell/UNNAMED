# External Asset Utilization Review (Phase B remediation)

Date 2026-09-26. Input: the Codex audit's 207-row inventory
(`G:/UNNAMED_HISTORY/CODEX_PHASEB_FORENSIC_AUDIT_2026-09-26/asset_inventory/external_asset_utilization.csv`) and the catalogue
`F:/Otherreach_External_Assets/EXTERNAL_ASSET_CATALOG.csv`. Outcomes use the brief's four words: **USED**, **ADAPTED**,
**REJECTED AFTER TEST**, **HELD** (with a named reason). "Used" states the acceptance gate reached
([remediation/ACCEPTANCE_CONTRACT.md](remediation/ACCEPTANCE_CONTRACT.md)). Rows not listed keep the audit's status: 129 rows have
no active use and **no recorded evaluation** - that is unknown, not rejected.

## 1. The audit's counts (rejected baseline, 22af661)

USED at default High 22, ADAPTED 1, partly used 1, Ultra-only 1, prepared unused 4, evaluated/prepared rejected 3, duplicate 1,
offline proof only 1, no active use found (evaluation unknown) 129, not downloaded / owner action 44.

## 2. Moved from "downloaded" to "used" in this remediation

| Asset (catalogue id) | Outcome | Use | Gate reached |
|---|---|---|---|
| `rancidmilk__free_character_animations` (CMU mocap FBX) | ADAPTED | run (`run_cmu3517`) and sprint (`sprint_cmu12707`) cycles, cut and retargeted onto the player's production rig (`charprod2/cmu_cycle.py`, map `cmu_rancidmilk_prod.json`) | G4 selected in normal play (state log), G5 filmed; G6 pending |
| `quaternius__universal_animation_library_2` (UAL2) | ADAPTED | sword attacks 2-4 (Regular A/B/C), block | G4, G5; G6 pending |
| `quaternius__universal_animation_library` (UAL1) | ADAPTED (more of it) | sword ready stance, sword attack 1, jump start, fall, land | G4, G5 |
| MakeHuman / MPFB system assets + `faceunits01` (CC0) | USED | the character standard's body, rig, eyes, teeth, tongue, brows, lashes, ARKit channels | offline + Godot proof scene (G3 in a proof, not gameplay) |
| MakeHuman `visemes01`, `visemes02` (CC0; downloaded today) | USED | the speech channels | same |
| MPFB wardrobe: `male_casualsuit06`, `shoes03` (CC0), `elvs_male_tankshirt1` (CC-BY), `drednicolson_asymmetric_tunic_and_sash` (CC-BY) | ADAPTED | garment standard: trousers, boots, tank, hide vest | offline QA sheets |
| MPFB hair `cortu_short_messy_hair` (CC0), `short02`, `short04` (CC0) | USED | hair variants of faces 1-3 | offline |
| ambientCG `Leather014` (CC0; downloaded today - the depot had no leather) | USED | the hide vest material | offline |
| Blender Human Base Meshes 1.4.1 (CC0; downloaded today) | REJECTED AFTER TEST | no shape keys, rig, teeth or tongue: would need a second facial system | - |
| MediaPipe Face Landmarker (Apache-2.0; already installed) | USED | performance capture to channel curves | offline + Godot proof |
| Poly Haven / ambientCG ground and plant models already used | USED (unchanged) | terrain layers, plant scatter | the old crossed-card grass they sat beside is now off at Medium/High/Ultra (H07) |

## 3. Held, with reasons

| Asset | Outcome | Reason |
|---|---|---|
| KayKit Character Animations (144 names) | HELD | its locomotion flares the arms on our rigs (Phase B B12); its melee set is the next candidate source for spear (two-hand stab) - not yet A/B'd on the production rig |
| Kenney Animated Characters 3 | HELD | four FBX, stylised, no clip the player lacks |
| Poly Haven `fir_tree_01`, `tree_stump_01`, `dead_tree_trunk` | HELD | the tree proof (M02) is not done this checkpoint; the fir failed preparation once (budget decimation) - to be re-prepared with an LOD chain rather than one decimation |
| Rust009, MetalPlates006 | HELD | industrial-piece preparation; no placement task this checkpoint |
| Quaternius sci-fi / fantasy kits, KayKit sci-fi | HELD | stylised low-poly; reference only for a realism target; no Tier-1/2 remediation item needs them |
| Audio packs | HELD | V3 retained by owner direction; no bulk replacement |
| Mixamo, LAFAN1, Fab/Megascans, Textures.com, NASA 3D, Truebones, Rokoko, GPL shipped content | not used | not approved (brief) |

## 4. External asset time savings

Estimates, with the basis stated; "from scratch" means authoring the same thing by hand at comparable quality.

| Category | Sourced asset | Adaptation needed | Work avoided (estimate) |
|---|---|---|---|
| Clothing | MPFB wardrobe (CC0/CC-BY) fitted by its own MHCLO | a descriptor, a material, one authored edit | a modelled, UV'd, weighted garment is 1-3 artist-days; here ~15 min per garment after the standard existed |
| Characters / faces | MPFB base mesh, rig, face units, visemes, eyes, teeth, tongue | skeleton renaming, eye bones, export clean-up (one-time) | a rigged base body with 89 face shapes is weeks of artist time; the factory took about half a day. Identity quality still falls short (the facial report) |
| Animations | CMU mocap run/sprint, UAL1/UAL2 sword set | cycle cutting, retarget map, pace and phase measurement | 6 production clips at mocap quality - hand-keying each is 1-2 days |
| Vegetation | Poly Haven plants (Phase B); generated blade grass (`_procgen_grass_blades.py`) | scatter rules, a shader flag | the grass itself is generated, not sourced: the depot had no near-field grass that beat cards without heavy cost |
| Trees | Poly Haven fir (held) | re-preparation with LODs | not yet realised |
| Materials | ambientCG Leather014; Poly Haven terrain | none / tiling scale | a scanned leather set is not practical to author; hours saved per material |
| Props | Quaternius / KayKit kits | would need re-texturing to the realism target | not realised (held) |
| VFX | Unity Labs / Godot spark textures (Phase B), generated strand texture | recipe data | the Mending Thread effect is recipe data over existing textures: hours rather than a hand-animated flipbook |

Where a category saved nothing, the reason is named above (held, not yet reached, or no depot candidate beat generation).
