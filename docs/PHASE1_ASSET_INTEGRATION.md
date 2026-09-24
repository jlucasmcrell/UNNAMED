# Phase-1 asset integration

**Date:** 2026-09-24. **Scope:** the owner-authorised Phase-1 closeout, Stage D: the asset pipeline's current stable library wired into
the game by semantic ID, with the greybox as the fallback. Nothing here changes a rule, a save or the content hash; the replay stays
byte-identical (see Verification). The library itself is never copied into this repository: the game reads it where it lies (the
`--asset-root`, else `UNNAMED_ASSET_ROOT`, else the untracked `assets/` beside `content/`), and without it every view draws greybox.

## How it is bound

`src/Presentation/Art/art_bindings.json` maps each game ID to the pipeline's stable IDs (presentation data - no content ID is named in
game code). Its rules:

- **Authored size is authoritative.** Every model is drawn at its own size, centred on the structure it dresses, its long side laid along
  the structure's, its lowest point on the terrain. Nothing is rescaled to a collision shape. Where the authored size and the world's
  structure disagree by more than 0.5 m a side (or the model stops more than 0.6 m below the structure's top), the greybox stands and
  the mismatch is reported (below). A modular piece (`tile`: a fence panel) repeats at its own length and is never stretched.
- **Weapons are held by their own sockets.** A weapon's `SOCK_grip_primary` (+X toward the head, +Y fixing the roll, per
  `WAVE_0_MODULAR_ASSET_STANDARD.md`) is laid on a grip frame: the greybox hand's, or a skinned rig's (stated in the bindings where the rig
  has no hand socket). The weapons are the playable-prototype manifest's slots: `weapon_arming_sword` (the rusted sword),
  `weapon_yew_longbow_warbow` (the hunting bow) and `weapon_boar_spear_hunting` (the March Spear and Tavar's spear).
- **Static models are the full files.** Every `_lod1`, `_lod2` and `_lod3` file in the library - 1,455 files, 485 of each - carries no
  material, so a lighter variant draws white. All Phase-1 models therefore use their full GLB: the waystone, the Quiet Stone, the boulder,
  the well, the cart, the oak door, the haversack, the bellows, the three weapons, and the skinned bodies below.
- **Withheld assets** (the bindings' `withheld`, each with its reason) are asked for, reported and drawn greybox; restoring one is a
  one-line data change once the pipeline fixes it.

## What the game draws

| Area | From the library | Greybox |
|---|---|---|
| The character | the greybox body holding the real sword, bow and spear | the body: both player rigs are withheld (below) |
| NPCs | Renn (`npc_veth_magistrate`: handover and talk clips), Sel (`npc_siann_archivist`: work-table and talk), Tavar (`npc_orenth_guide`: the husk's locomotion and fighting clips, talk, and the spear upright in his hand) | Kera (`npc_kal_smith` withheld) |
| Creatures | the bone walker husk (`creature_bone_walker_husk`, its six clips) | the hound, boar, spider, armour and wolf (rigs withheld) |
| Landmarks and props | the Ashen Waystone, the three Quiet Stones, the ruined cart, the two boulders under the Woundmoss beam, the well, the oak door on the lodge and the forge, the dropped-item haversack, the forge's bellows | the Foldscar's heart, the iron seam's outcrop, the blocked shaft, the rim boulders, the trees, the chests, the anvil, the ash stand, the survey table, the fence |
| Buildings and ground | - | the longhouse, the forge shed and the four cells' ground (the kit and the world materials are withheld) |
| HUD | the icon manifest's tiles: the four pools beside their bars (health, stamina, focus, Strain), the three formulas on their keys, active effects (bleeding, weakened, Strained), Tavar's order (follow, wait, downed), item icons in the inventory, trade and container rows, and the compass dial | text stands wherever an icon is missing |
| Formulas | the effect manifest's flipbooks: the cast charge in the casting hand from the windup to the release, the Brace Ward's shell for as long as the character is braced, the Mending Thread's restore (played once, then fading), the Impulse Bolt's travel and impact, and the Strain overlay at the manifest's curve with its pulse above 0.65 | the greybox glow when the charge is missing |
| Audio | the V3 set, all 230 IDs (below) | - |

## Reported to the asset pipeline

Evidence was measured from the GLBs themselves; the scripts are kept privately with the project history
(`asset_audit_scripts_2026-09-24`). In every rig case below the skin's bind poses equal the joints' rest transforms, so the export is
consistent: it is the skeleton that does not sit in the mesh.

### Rigs that do not fit their meshes

| ID | Evidence | Effect in game |
|---|---|---|
| `race2_veth_bindpose` (the manifest's P0 player) | the mesh's arms hang lower than the skeleton's A-pose: both hand bones 0.16 m, the finger bones 0.21-0.26 m from the nearest vertex; the hand-weighted vertices' centroid (-0.43, 0.87, 0.07) lies 0.24 m from `hand_r` (-0.56, 1.06, 0) | a held weapon floats off the drawn hand; arm clips bend the arms about points off the body |
| `race2_veth_representative` | an arms-down mesh 0.62 m wide on an A-pose skeleton with its hands at x = ±0.56 m; no vertex weighted to either hand bone, 22 to `lowerarm_r` | each arm rides its upper-arm bone whole |
| `creature_ash_ember_hound` | skeleton 0.88 m tall in a 1.30 m mesh, spine along +Z; leg bones up to 0.36 m from the nearest vertex | limbs deform about points outside the body |
| `creature_bristleback_boar` | skeleton 0.90 m tall in a 1.32 m mesh, spine along +Z while the body faces -X; leg bones up to 0.39 m out | as above; the body walks sideways |
| `creature_cave_hunting_spider` | skeleton 0.59 m tall in a 0.86 m mesh; leg bones up to 0.32 m out | as above |
| `creature_frost_wolf` | skeleton 0.81 m tall in a 1.19 m mesh; leg bones up to 0.45 m out | as above |
| `creature_animated_armour` | a 1.60 m humanoid skeleton in a 2.00 m mesh; `foot.R` 0.26 m, `neck` and `shoulder.L` 0.18 m out | as above |
| `npc_kal_smith` | a 1.60 m humanoid skeleton in a 1.30 m mesh; the head bone 0.30 m above the head | the body draws contorted in its clips |

Also: `npc_siann_archivist` carries no vertex weighted over 0.5 to either forearm or hand (drawn; its gestures move whole arms);
`npc_orenth_guide` and the other 20-bone NPC rigs have no hand sockets (Tavar's grip frame is stated in the bindings).

### Models built leaning or tilted

The files' nodes are all upright; the lean is in the vertices (the plane through each model's lowest points, and the axis through its
slice centroids).

| ID | Evidence |
|---|---|
| `flora_oak_tree` | trunk axis 31°, base plane 20° |
| `flora_pine_tree` | trunk axis 24°, base plane 37° |
| `flora_dead_tree` | trunk axis 17°, base plane 13° |
| `container_chest_iron_banded` | lid and base both about 15° |
| `prop_blacksmith_anvil_stump` | face and base both about 10° |
| `prop_wooden_crate` | top 21°, base 18° |
| `resource_ash_haft` | 33° off vertical |
| `prop_long_trestle_table` | the top slopes and the legs splay |
| `landmark_foldscar_core` | modelled on its edge: a ring of stones round a crater stood up like a wheel (4.39 m tall) |

Drawn but flagged for the owner's eye: `prop_cart_damaged_merchant` (7-11°, and damaged by design) and `prop_canvas_haversack` (a soft
bag, 15° under it).

### Textures that are swatches, not surfaces

The ten world materials (`material_packed_dirt_ground`, `material_marsh_grass_turf`, `material_loose_gravel`,
`material_churned_wet_mud`, `material_rubble_stone_wall`, `material_plaster_lath_wall`, `material_oak_plank_floor`,
`material_limestone_ashlar`, `material_slate_roof_scale`, `material_cast_iron_surface`) are each a photograph of a sample on a grey
backdrop, so they cannot tile; the building kit that uses them (`building_wall_timber`, `building_wall_stone`, `building_roof_panel`,
`building_floor_planks`, `building_fence_panel`) and the two buildings assembled from it (`forge_shed`, `longhouse`) show the backdrop
through; `flora_birch_tree`'s trunk is a spike of a few huge triangles.

### Sizes the world disagrees with

Not asset defects: the world's greybox structures were sized before the art existed. Drawn greybox rather than rescaled; the owner's
call whether the world or the art moves. `prop_iron_vein_outcrop` is 1.20 × 0.88 m, 0.84 m tall against the iron seam's 2.40 m circle,
1.50 m tall; `prop_blocked_shaft` 2.60 × 2.59 m, 1.67 m tall against the shaft's 6.00 × 5.00 m, 4.00 m tall; `rock_boulder` 1.80 × 1.52
m, 1.42 m tall against the four rim rocks' 3.6-4.4 m circles, 2.0-2.4 m tall.

### Gaps

- `weapon_rusted_militia_sword` has a grip in `_author_weapon_sockets.py` but none in its GLB; `weapon_hunting_bow` and
  `weapon_march_spear` have no grip socket at all. The manifest's three socketed weapons are used instead.
- No crouch or jump clips for the 52-bone humanoid; no locomotion clips for the 20-bone NPC rig (Tavar borrows the husk's).
- No wolf voice or footfall set (the wolves use the hound's).

## Audio

The V3 set (`manifests/playable_prototype_audio_v3.json`, 230 IDs, 118 variation families) is the current functional set; V1 and V2
stay as history. Every V3 entry has `human_auditioned: false`, so the set is a working placeholder, not an approved mix. Nothing was
regenerated.

The contract's events (`PHASE1_AUDIO_EVENT_CONTRACT.md`) are the audio side's names for moments, not the game's API: a mapping layer
(`src/Presentation/Audio/SoundEvents.cs`) recognises each from what the game already has - an event the simulation published, or the
drawn world changing between frames - and resolves it to a family of IDs, through the bindings for every discriminator. The player
(`SoundBank.cs`) picks a variation at random that is never the one a family played last; a mono sound plays at its place in the world,
the stereo loops (the four beds, the three Strain layers, the forge) and the UI play flat; every sound plays at its delivered level.

| Contract event | Recognised from | IDs |
|---|---|---|
| `footstep(surface, gait)` | the drawn feet: one per 0.75 m walking, 1.1 m running (a sprint uses run); surface from the cell (dirt, stone) or indoors (wood) | `sfx.player.footstep.*` |
| `land`, `jump_effort`, `crouch_enter/exit` | a landing after 0.3 s in the air; `Jumped`; `StanceChanged` | `sfx.player.land.*`, `.jump.effort`, `.crouch.*` |
| `hurt(severity)`, `death` | `HitResolved` on the character (heavy: a critical, or 15% of health); `PlayerDied` | `sfx.player.hurt.*`, `.death` |
| `gear_shift`, `equip` | `ItemEquipped` / `ItemUnequipped` (metal for a weapon slot, cloth otherwise) | `sfx.player.gear.*`, `sfx.ui.equip` |
| `draw`, `sheathe`, `ready` | the weapon in hand changing | `sfx.weapon.sword.draw`, `sfx.player.weapon.sword.sheath`, `.spear.ready` |
| `swing(family)` | `AttackStarted`, heard at its commit (after the windup) | `sfx.weapon.sword.swing.light`, `.polearm.thrust` |
| `draw_bow`, `nock`, `release`, `projectile_flight` | the bow's `AttackStarted`; `ShotLoosed` | `sfx.weapon.bow.*`, `sfx.player.weapon.bow.nock` |
| `impact(family, material)` | `HitResolved`: the attacker's weapon family × the target's flesh or plate (an arrow when it lands); an arrow at rest in a wall or on stone | `sfx.weapon.*.impact.*` |
| `block` | a blow blocked by the character's sword | `sfx.weapon.sword.block` |
| creature `idle`, `alert`, `attack`, `hurt`, `death` | every 8-15 s near a calm creature; `CreatureNoticed` (engaged); `AttackStarted` at its commit; `HitResolved`; `CreatureKilled` | `sfx.creature.<voice>.*` |
| `walk_step`, `run_step`, `charge_step`, `move_fast_step`, `move_heavy_step`, `scuttle_step` | a creature moving within 40 m: one footfall per sound (0.5 s walking, 0.3 s running); the spider's sounds carry two steps each and come at their own length (0.70 s, 0.60 s) | `sfx.creature.<voice>.<gait>` |
| the formulas' `cast_start`, `activate`, `resolve`, `travel`, `impact`, `ward_hit`, `end` | `CastStarted`, `CastCompleted`, the drawn bolt's burst, `HitResolved` while braced, `EffectExpired` | `sfx.magic.*` |
| Strain layers | Strain as a share of tolerance, cross-faded: moderate from 0.35, high from 0.65, critical from 0.85 | `sfx.magic.strain.*` |
| interactions | `DoorToggled`; a chest opened or closed; `TookAll`; a pickup from the ground; `NodeGathered` at the ore seam (two strikes, then the ore breaking); `ItemCrafted` at the anvil or forge (three strikes, then complete) | `sfx.interaction.*`, `sfx.crafting.*` |
| `station_ambience` | the forge, full within 3 m and gone by 20 m | `sfx.crafting.forge.ambience.01` |
| beds, `cell_detail` | the cell underfoot, cross-faded over 3 s; details at irregular 7-23 s intervals, never the same twice running, placed 12-28 m round the listener | `amb.*`, `sfx.amb.*` |
| UI | a panel opening or closing; a reply, a purchase or a sale; a conversation ending; a refused command | `sfx.ui.*` |

Unused in Phase 1, by the game's shape rather than a fault: `sfx.player.downed.01` (the character is never downed, only companions),
`sfx.weapon.sword.swing.heavy` (no heavy attack), `sfx.weapon.polearm.handle`. An arrow at rest in dirt is silent (the set has no dirt
impact), and an arrow into plate takes the spear's plate impact (the matrix has no arrow-on-plate asset).

## Verification

See `M6_STATUS.md`, "Phase-1 closeout".
