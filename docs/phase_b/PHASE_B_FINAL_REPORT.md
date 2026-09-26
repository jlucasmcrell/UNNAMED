# Phase B final report - the visual overhaul

> **REJECTED at the owner's visual review (2026-09-26).** Kept unchanged below as the baseline the remediation is measured
> against (tag `phaseB-owner-review-rejected-baseline`, commit `dd5283c`). The remediation checkpoint is
> `docs/phase_b/remediation/REMEDIATION_CHECKPOINT.md`.

Stopped for the owner's visual review. Nothing is merged. This report answers the brief's items 1-25; the B0 decisions and
their evidence are in `PHASE_B0_ADOPTION_REPORT.md` beside it.

## 1. Branch and head

`claude/phase-b-visual-overhaul`, pushed to origin: this report's commit, on top of the last code commit `22af661`. Worktree `G:\UNNAMED_PHASEB`.

## 2. Base

main `a69693108b89d471cdb409a229ba0bdf78b08fa4` (Phase-A 29d4f45 and the RAZER baseline). M7 was not merged in and its worktree
was only read.

## 3. External systems: adopted, adapted, rejected

| System | Decision | Role |
|---|---|---|
| Terrain3D 1.0.2 | ADOPTED | the ground's renderer only (the domain `TerrainGrid` stays the truth; its collision is off) |
| Sky3D 2.1 | ADOPTED | sun, moon, stars, sky dome and atmosphere by the hour |
| SunshineClouds2 | ADAPTED | volumetric clouds, Ultra tier only |
| Godot's built-in skeleton modifiers (TwoBoneIK3D, LookAtModifier3D, SpringBoneSimulator3D) | ADOPTED | foot planting, head look-at, tail spring |
| Godot VFX Library | ADAPTED | 2D only; its sparks and embers recipes ported to 3D particle recipes |
| Poly Haven HDRI as the sky | REJECTED | the photographed ground floats above our horizon |
| Boujie Water 1.0.1 | REJECTED | ocean-tuned; removed from the project |
| RetargetModifier3D at runtime | NOT NEEDED NOW | clips are baked onto our rig offline; reconsider at the 52-bone migration |

## 4. Exact versions and commits

- **Terrain3D 1.0.2-stable** (MIT, Cory Petkovsek and Roope Palmroos): release zip SHA-256
  `a071850250ec5e596aa54da61c01d75768774eb379ee997584d426a45f4884a2`. Only the Windows and Linux binaries are kept, as
  `addons/terrain_3d/OTHERREACH_NOTE.txt` records.
- **Sky3D 2.1** (MIT, Cory Petkovsek, J. Cuéllar and contributors).
- **SunshineClouds2** (MIT, David House), pinned at `a73a80b08fb37b255f8f10d49debd71e5541bd70` (2026-09-06; it has no tagged release).
  The pin is recorded in `addons/SunshineClouds2/PINNED_COMMIT.txt`, and the LICENSE was fetched raw at that commit.
- **Godot 4.7.2-stable mono** (unchanged).

## 5. External assets actually used, and their licenses

Every asset lives in the gitignored asset workspace with its provenance record: pack, author, source URL, license, source and
output SHA-256s, and what was changed.

- **CC0, Poly Haven:**
  - terrain layers: grass_ground, forest_floor, rocky_trail, rock_ground, dark_rock_02;
  - rock: mountainside, boulder_01, namaqualand_boulder_02, coast_rocks_01, coastal_cliff_01;
  - plants: grass_medium_01, nettle_plant, fern_02, dandelion_01;
  - ground litter: dry_quiver_leaf (built into three leaf-litter clumps) and bark_debris_01.
- **CC0, ambientCG:** Onyx015, worn by the Foldscar heart and its shards. MetalPlates006 and Rust009 are prepared for B9's
  industrial pieces but not used yet.
- **CC0, zonked on OpenGameArt ("Alien Artifacts Sculptures"):** sculpture 1 (the heart) and sculpture 3 (its shards).
- **CC0, Unity Labs "Free VFX Image Sequences and Flipbooks":** FireBall01 (through a cold ramp), Flame02, WispySmoke01 and Cloud01.
- **MIT, Godot VFX Library:** its spark particle texture (16 px).
- **CC0, animation:** KayKit Character Animations 1.1 (Kay Lousberg) and Universal Animation Library 1 and 2 (Quaternius),
  retargeted by the factory; the catalogue is under item 14. The production characters' locomotion, idles and talking idles are
  UAL 1.
- **Offline tools for the character pipeline, nothing shipped:** Grounding DINO and SAM 2 (Apache-2.0) for material zones,
  MediaPipe face and hand landmarkers (Apache-2.0), BiRefNet (MIT) for the figure mask. Their weights stay in the workspace.
- **Generated here, project-owned:** the rain streak texture; the timber building kit and the archetype props (procgen from the
  hollow's own materials).
- **Prepared but not used:** Poly Haven fir_tree_01, tree_stump_01 and dead_tree_trunk. The fir decimated to a game budget read
  as bare poles, so the Charwood's own pines and oaks stand on the far rims instead.
- **Audio:** the 24 proof sounds are not shipped. V3 is unchanged.

## 6. Attribution requirements

- **MIT notices must ship with copies:** Terrain3D, Sky3D and SunshineClouds2 (their LICENSE files are in their addon folders)
  and the Godot VFX Library (its LICENSE text sits in the workspace beside the staged library). The packaged build must carry all
  four; a credits file is a packaging step.
- **CC0 assets:** no attribution is required. Poly Haven, ambientCG, zonked, Unity Labs, Kay Lousberg and Quaternius are credited
  in the provenance records.
- **Audio, if the proofs are adopted:** Helton Yan (CC BY 4.0) and congusbongus/blukotek (CC BY 3.0). Sonniss audio may never feed
  an AI system.

## 7. Renderer changes (B1)

- **Quality tiers** (`--visual tier=low|medium|high|ultra`), with High as the default and the RTX 4070 Ti 1080p/60 target:
  - **High:** the Phase-A lighting with TAA alone. MSAA 4x cost 0.5 ms and 170 MB for little on alpha-tested foliage.
  - **Medium:** drops SSIL, uses fewer SDFGI cascades and thins the plant models to 55%.
  - **Low:** drops SDFGI and the volumetric fog, halves the shadow atlas, uses SMAA and the card grass only.
  - **Ultra:** adds the volumetric clouds, SSR, finer SDFGI rays, MSAA with TAA, and longer shadows.
- **Choosing a tier:** a player picks it on the start screen, and it applies at the next launch. `--visual phase_a` draws Phase A
  exactly.
- **Texture cache:**
  - Every texture is compressed once to BC7 with its mip chain. Alpha-tested colour keeps its coverage down the chain.
  - Each texture is shared across the LOD files that each embedded their own copy.
  - It is built by `--texture-cache` (the editor binary; `Image.Compress` is editor-only) and read by every run.
  - At High, texture VRAM fell from 4.9 GB to under 1.1 GB, and total VRAM from 7.2 GB to 3.0 GB.
- **Found and fixed:** a screen-reading shader drew fading instances black, because they are missing from the screen copy.

## 8. Terrain and landforms (B2)

- **Terrain3D** draws the ground from the domain heights at 0.0 mm error, from five Poly Haven layers. The autoshader textures
  the slopes, the Charwood floor is tinted towards ash, and the paths are blended in.
- **Scenery beyond the walkable edge** comes from one seeded height function: the ravine on the west, north and east, and the
  cliff to the south, with ridges out to the horizon.
- **Real rock on every side:** cliff shells on the ravine's far faces, boulders on the rim lip, shelves on the floor, and 92 m
  cliff runs on the south face.
- **A wood on the far rims** (pines and oaks), so the horizon is not bare hills.

## 9. Sky and weather (B5)

- **Sky3D** by the hour. The sunset is calmed and a moonlight key light makes night readable (Sky3D's night ambient boost does
  nothing under SDFGI).
- **Weather states as data** (`weather_states.json`: fair, clear, overcast, storm) drive Sky3D's clouds and light, the fog
  density, the wind, and the rain.
- **No domain clock or weather exists.** The hour and the weather are presentation choices (`--visual hour=`, `weather=`).
- **Found and fixed by the package gate:** with Sky3D the Phase-A sun it replaces was built and never freed. Its light leaked at
  exit, and the exported release build died there of heap corruption (0xC0000374) after every check had passed. Bisected on the
  packaged build (only `sky=classic` exited cleanly); the classic sun is now freed once Sky3D's sun has its shadow tuning.

## 10. Vegetation and wind (B3, B4)

- **Plant models:** a scatter kind can scatter a prepared model (`model:<id>`) with its LOD levels, under the same deterministic
  ScatterRules. There are eight model kinds: tufts, nettles, ferns, three leaf-litter clumps, bark and dandelions.
- **One world wind signal** (global shader parameters: direction, strength, gust, clock) drives:
  - the plants, by the height in their vertex colours;
  - the trees, rebuilt from their own materials, the whole tree bending by height squared with a leaf flutter;
  - the weather's strength.
- The procedural leaf cards are replaced by the leaf models.

## 11. Water (B6)

- A flowing river on the three ravine floors, drawn by our flow shader: ripples advected along the current in two blended
  phases, depth colour, thin foam and streaks, refraction and the sky. Mist hangs over it.
- **Boujie is rejected.** There is no water inside the hollow, because its terrain has nothing below a water level (M7's height
  semantics).

## 12. Building and prop pipeline (B7, B8)

- **Timber building kit** (B7): pad, wall, doorway, door and roof, fitted to M7's piece contract (design §4.21). The kit sheet
  draws M7's blocking boxes over the assembled house: every model is inside its boxes and the doorway is clear. The binding is
  recorded in M7's documented future shape; M7's StructuresView must read it (item 24).
- **Prop archetypes** (B8): `procgen_lib/archetypes.py` holds one parameterised builder per shared form - timber frame, plank
  box (crate or chest), wheel and axle, bench/table, posts and rails, rope/chain, pipe/conduit, technological housing, and three
  artifact components - with shared fittings (straps, rivets, hinges, hasps, ring handles). One generic driver builds any of them
  from a JSON instance file. Proven on seven families with two variants each (14 props, LOD1/LOD2, promoted to `assets/ready`):
  every second variant cost 12-39% of the first's lines, inside the reuse gate's half. The contact sheet caught two geometry
  bugs in `tech_housing` (a floating seam, vents above the case), fixed. None is placed in the hollow yet (placement is content).
  `tools/asset_pipeline/ARCHETYPES.md`.

## 13. Science-fantasy additions (B0.6, B9)

- **The fold:** a restrained distortion with no silhouette (the bible's doubling), replacing the violet box.
- **The heart:** a staged alien sculpture wearing dark ambientCG onyx over its carving, fitted to its own collision circle.
- **Its ground:** shards of the same kind lie half-buried round it, and a few stones rest a hand's breadth above the ground,
  still - a fact about them, not an effect.
- **Restraint:** nothing glows except the particle magic, and the staged sci-fi kits remain reference only.

## 14. Character and animation (B0.4, B12)

- **Retarget factory** (`tools/asset_pipeline/_retarget_clip.py` plus the bone maps): one command per clip. It verifies source
  hashes, records provenance, and checks foot slide, stretch, bends and loops.
- **Motion catalogue** (`assets/animation/review/retarget/B12_CATALOGUE.md`): 34 states requested from KayKit - locomotion
  13 of 15 shipped (2 had no source), combat 11 of 11, magic 2 of 2, world 5 of 6 plus 1 rejected - each checked for foot slide,
  skin stretch, sideways joint bends and loop seams. KayKit's stylised rig carries the arms 40-50° off the body, which read as
  flared arms on a realistic body, so the locomotion was re-sourced from Quaternius UAL: idle, walk (0.96 m/s), run, sprint,
  crouch, jump and fall all ship at measured paces. The player plays the UAL locomotion in every tier; run and the weapon
  states stay procedural (the grips are calibrated to them).
- **Character visual fidelity** (the owner's addition; full record `docs/phase_b/CHARACTER_FIDELITY.md`, sheets
  `docs/phase_b/characters/accept_*.jpg`). Where the Phase-A detail was lost, measured on the shipped files for all five
  characters, and what corrected each loss:
  1. *Reconstruction texture* - the image-to-3D stage re-synthesised colour and never used the concept's pixels (dark blobs for
     eyes, a smeared mouth). Corrected by projecting the concept itself through a fitted camera (the mesh is pixel-aligned to it),
     de-lit, feathered into the reconstruction's texture where the concept cannot see.
  2. *Reconstruction resolution* - a full body spends the voxel grid on 1.8 m, so a face got ~200 voxels. Corrected by
     reconstructing the concept's head crop on its own (~5x the face's voxels) and grafting it at the neck by the two cameras.
  3. *Meshing* - the remesh wrapped every surface in an enclosed inner shell: 53% of the player's triangles and half the atlas
     spent on nothing. Corrected by ray-escape interior removal.
  4. *UV layout* - ~950 sliver charts, the head at the body's density (4.6-6.6 px/cm on every face). Corrected by a face chart
     from the concept projection and bone-region charts at face 2.5x / head and hands 1.4x the body's density on a 4096 atlas:
     faces now 23-37 px/cm, bodies 11-15.
  5. *Materials* - one material per character. Corrected by text-prompted material zones (skin, hair, cloth, heavy cloth,
     leather, metal) with per-kind roughness and metal, and Godot materials by kind (skin subsurface scattering, cloth sheen,
     leather clearcoat, wet corneas).
  6. *Eyes* - painted. Corrected by lid openings cut on the concept's eyelid landmarks and eyeballs on eye bones that follow the
     player.
  7. *Rig* - 20 bones, no fingers; Renn's and Tavar's sleeves (and Tavar's coat) fused into the body mass by the reconstruction.
     Corrected by a 55-bone production rig (the humanoid20 bones kept, plus 30 finger bones, eyes, a middle spine and toes) and,
     for Renn and Tavar, new meshes from arms-clear concepts of the same characters with the garments as open geometry.
  8. *Resize step* - Kera's and Sel's 4096 bakes were halved to 2048 before shipping. Corrected by using the full bakes.
  9. *LODs* - built but never drawn, so every person drew its full mesh at every distance. Corrected by skinned LOD1/LOD2 in the
     same GLB, switched by distance (LOD0 to 18 m, past any dialogue or third-person framing).

  Godot's import was not a loss stage (BC7 at full resolution with mips). All five production characters are drawn in every
  tier (`people=phase_a` draws Phase A); each NPC plays UAL idles retargeted onto its own rig.
- **In game (`body=modifiers`, every tier):**
  - The player's and NPCs' feet are planted on the ground. On a 20° slope both ankles land at the flat clearance, and the
    planting fades out while a body is in the air.
  - An NPC's head follows the player within 6 m.
  - The spring bone was proved on a tail; there are no coat bones yet.
- **52-bone contract:** staged. The production characters already carry fingers, grips, eyes and a third spine bone on a
  55-bone rig that keeps every humanoid20 name (so every clip and gameplay hook still binds); moving the remaining bodies and
  M7's contracts to the 52-bone skeleton, with one map per pack and per race, stays the owner's decision.

## 15. VFX (B0.7, B11)

- **Particle recipes as data** (`vfx_recipes.json`), built by one factory into GPUParticles3D.
- **In play:**
  - the Impulse Bolt's trail and burst;
  - sparks where an arrow strikes;
  - the smithy chimney's smoke.
- **The air:** ash in the Charwood, pollen over the meadow, rain in storms, and mist over the river.
- A blind Gemini 3.8 motion review ranked every recipe above the Phase-A flat flipbooks.

## 16. Ambient life (B10)

- Crows circle over the hollow; butterflies (by day, fair weather) and fireflies (by night) live round the camera.
- Each kind is one MultiMesh flown by its shader: no node, script or simulation state per animal.
- They are kept to their grounds and out of buildings.

## 17. Audio hooks

- **`audio_hooks.json`:** wind, rain, thunder, the river, day and night insects, foliage gusts, distant birds, the smithy's fire
  and the fold's hum, placed by weather, hour, ground and nearness to the ravine.
- **Playing now:** three hooks (gusts, ravens, the fire).
- **Silent until their audio exists:** seven hooks. They are never requested, so the audio coverage gate is unchanged.
- **The 24-sound proof batch:** auditioned in game. It is not accepted until the owner listens (item 19).

## 18. Phase-A visual defects resolved

- **Flat magic VFX** that stopped at an invisible square: replaced by particle recipes.
- **The start screen** showed whatever the camera faced: it now opens on a slow establishing shot of the hollow, with a
  graphics-quality choice.
- **Stiff bodies on slopes** (feet in the hill or in the air): feet are planted.
- **NPCs that never looked at the player:** heads follow within 6 m.
- **Water nobody could see,** and a Foldscar that read as a debug volume.

- **Faceless, smeared people:** all five characters rebuilt (item 14): readable faces, real eyes, separate garments and
  material zones, fingers that grip.

## 19. Remaining defects

- **Characters** (`CHARACTER_FIDELITY.md` section 4):
  - The fingers are a mitt (the reconstruction fuses them). It reads as a grip at play distance but not up close, and an
    open-palm gesture shows it (Renn's talking idle). The fix is a sculpted or kit hand fitted at the wrist.
  - A concept's clipped rim light leaves a pale band on the player's cheek edge.
  - The neck seam reads as a crew collar.
  - There is no jaw or blink animation.
  - Renn's white collar points are jagged at close range.
  - Tavar attacks with a sword swing, since no cleared spear thrust exists.
  - No secondary motion (coats, robes and hair do not swing).
- **Audio:** the 24-sound proof batch awaits the owner's audition (`G:\UNNAMED_HISTORY\phaseB\audio_audition`). V3 plays
  unchanged.
- **Animation:**
  - The KayKit clips flare the arms and stay off.
  - Run and the weapon states are procedural.
- **Foldscar heart:** the groove light was dropped; it needs a cavity bake.
- **Owner or M7 decisions** (item 24): there is no domain clock or weather, no water inside the hollow, Kera and Sel idle away
  from their stations, and no building piece is drawn until M7's `StructuresView` reads the kit.

## 20. Before and after

Phase A beside Phase B at the same camera, same hour:
- `docs/phase_b/final/`: horizon, middle distance, near ground, buildings and people, creatures, Foldscar.
- The characters: `docs/phase_b/characters/accept_{player,kera,sel,renn,tavar}.jpg`. Each sheet holds the concept, the Phase-A
  mesh, the production mesh, both faces, and Godot close / dialogue / third person for both.
- Per-system proofs: `docs/phase_b/b0/`.

## 21. Performance against Phase A (RTX 5090, 1080p, unpaced)

`docs/phase_b/final/perf_route.md` has the full route (obstruction, third person, first person) for Phase A and every tier.

| Run (third person) | avg fps | GPU p99 | VRAM |
|---|---|---|---|
| Phase A | 254 fps | 4.82 ms | 5.8 GB |
| Low | 339 fps | 2.27 ms | 2.3 GB |
| Medium | 253 fps | 3.66 ms | 3.0 GB |
| High | 222 fps | 4.50 ms | 3.2 GB |
| Ultra | 142 fps | 7.89 ms | 3.7 GB |

- **High** stays inside the 6 ms GPU budget that projects to 60 fps on RAZER's 4070 Ti.
- **VRAM:** High uses 2.6 GB less than Phase A, mostly through the texture cache.
- **The five production characters**, same build at High, measured against the Phase-A characters:
  - frame rate: 4-10 fps lower, GPU p50 and p99 each +0.1 ms, no new hitches;
  - VRAM: +263 MB;
  - working set: +446 MB median. It was +638 MB before the loader released decoded images it no longer needed.
- The tier rows were measured before the tiers drew the production characters.

## 22. RAZER

Not measured. RAZER is the owner's streaming machine, and the Phase-A numbers were captured by the owner running a package there.
A Phase-B package is prepared for the same run (`G:\UNNAMED_HISTORY\playtest_build\Otherreach_PhaseB_Review_2026-09-26_22af661.zip`, 2.84 GB, SHA-256
`74c8e75381f4a0b7c5ef18925d373abbfd5b78170a0c15259ed1ca7d7ad2ca07`, gated from a fresh extraction: item 23); the 5090 numbers above project to RAZER at roughly 2.3-2.7x.

## 23. Tests, content lint, regression

All at `22af661`:

- **Unit and contract tests:** 825 passed, 0 failed, across the eight suites. Application 204, Architecture 14, Content 146,
  Domain 147, EntityRegistry 23, Persistence 173, Presentation 57, World 61.
- **Editor smoke** at the default High tier, with the production characters: PASS, 0 unexpected art fallbacks. All five
  production models and their 23 clips were drawn, and no RIDs leaked at exit.
- **Package gate:** the Phase-A gate (A11) run against a fresh extraction of the ZIP. No asset root was given and it started from
  an unrelated directory, one instance at a time (`G:\UNNAMED_HISTORY\phaseB\package_gate\package\summary.txt`). It runs at
  the default High tier. Every step exited 0 with 0 unexpected art fallbacks and 0 missing sounds:
  - smoke;
  - delta shots;
  - the scripted playthrough;
  - relaunch verification: 956 authoritative fields compared, 0 differences;
  - resume shots;
  - a replay whose state is byte-identical to the playthrough's.
- **A package that failed the gate:** the first package (`899c3c1`) failed on the smoke's exit code. It crashed at exit
  (item 9) and is kept as `..._899c3c1_REJECTED_sky3d_exit_crash.zip`.
- **Merge check:** `git merge-tree` with M7 (`c4c8f9d`) is clean.

## 24. Conflicts and reconciliation with M7

- **No M7 contract changed.** Building dimensions, footprints, the lattice, doorways, navigation, movement, the save schema, piece
  persistence, work anchors and terrain height semantics are all untouched. Terrain3D and every other adopted system are
  presentation only.
- **Reconciliation at merge:**
  1. M7's `StructuresView.BuildPiece` should wrap an `ArtPiece` reading `art_bindings.json`'s `pieces` section, which Phase B has
     recorded in M7's documented shape.
  2. Both branches touch `Main.cs`, `HollowView.cs` and `art_bindings.json`.
- **Owner decisions outside Phase B:**
  - a domain clock and weather (the sky and weather are ready to follow them);
  - water in the hollow (needs terrain below a water level);
  - Kera's and Sel's placements (their anvil is 4.2 m away and their table edge 1.4 m, so they idle; this is simulation content
    and M7 owns Kera's work assignment);
  - the 52-bone rig migration.

## 25. Ready for owner review?

Yes, for the owner's visual review. It is not a merge request: nothing is merged into main, and M7 is untouched.

What the owner is asked to do:

1. **Look.**
   - The before and after sheets (item 20).
   - The five character acceptance sheets.
   - The package: High is the default, and the start screen chooses a tier.
2. **Audition the 24 audio proofs**
   (`G:\UNNAMED_HISTORY\phaseB\audio_audition\ab\audition.mp4`, key in `audition_key.json`). V3 stays until they are
   accepted.
3. **Decide the items outside Phase B's authority** (item 24):
   - a domain clock and weather;
   - water in the hollow;
   - Kera's and Sel's placements;
   - the 52-bone migration.
4. **Choose the next character follow-ups:**
   - separate fingers (a fitted hand);
   - jaw and blink;
   - garment spring chains for coats, robes and hair.
5. **Authorize the merge and reconciliation with M7:** `StructuresView` reads the timber kit, and the shared files are
   `Main.cs`, `HollowView.cs` and `art_bindings.json`.
