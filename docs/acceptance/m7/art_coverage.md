# Art coverage (playthrough)

UNNAMED art coverage: 125 requested, 3 resolved, 122 fallbacks, 121 unexpected

A visual is a thing the game draws, by what it stands for; resolved is drawn with an asset from the library, a fallback is drawn in greybox. Allowed fallbacks are named in `src/Presentation/Art/art_coverage_allowlist.json`, each with its reason.

| Kind | Requested | Resolved | Fallbacks | Unexpected |
|---|---|---|---|---|
| barrier | 1 | 1 | 0 | 0 |
| building | 2 | 0 | 2 | 2 |
| cliffs | 1 | 0 | 1 | 1 |
| container | 3 | 0 | 3 | 3 |
| creature | 6 | 0 | 6 | 6 |
| debug | 1 | 0 | 1 | 0 |
| door | 2 | 0 | 2 | 2 |
| effect | 4 | 0 | 4 | 4 |
| icon | 12 | 0 | 12 | 12 |
| node | 4 | 0 | 4 | 4 |
| overlay | 1 | 1 | 0 | 0 |
| person | 5 | 0 | 5 | 5 |
| piece | 2 | 0 | 2 | 2 |
| roof | 2 | 0 | 2 | 2 |
| scatter | 1 | 0 | 1 | 1 |
| station | 2 | 0 | 2 | 2 |
| structure | 64 | 0 | 64 | 64 |
| switch_mark | 4 | 0 | 4 | 4 |
| terrain | 4 | 0 | 4 | 4 |
| water | 1 | 1 | 0 | 0 |
| weapon | 3 | 0 | 3 | 3 |

## Unexpected fallbacks

- `building:forge` (asked for `forge_shed`): not drawn: its walls and roof are drawn in greybox
- `building:longhouse` (asked for `longhouse`): not drawn: its walls and roof are drawn in greybox
- `cliffs:ravine` (asked for `material_rubble_stone_wall`): its material would not load
- `container:container.den_cache` (asked for `container_chest_iron_banded`): not drawn: a brown box
- `container:container.timber_stack`: no binding: a brown box
- `container:container.waystation_chest` (asked for `container_chest_iron_banded`): not drawn: a brown box
- `creature:creature.beast.ash_ember_hound` (asked for `creature_ash_ember_hound`): not drawn: greybox blocks
- `creature:creature.beast.bristleback_boar` (asked for `creature_bristleback_boar`): not drawn: greybox blocks
- `creature:creature.beast.cave_hunting_spider` (asked for `creature_cave_hunting_spider`): not drawn: greybox blocks
- `creature:creature.beast.wolf_grey` (asked for `creature_frost_wolf`): not drawn: greybox blocks
- `creature:creature.construct.animated_armour` (asked for `creature_animated_armour`): not drawn: greybox blocks
- `creature:creature.undead.bone_walker_husk` (asked for `creature_bone_walker_husk`): not drawn: greybox blocks
- `door:door.forge_shed` (asked for `prop_iron_banded_oak_door`): its building is drawn in greybox, so its door is a panel
- `door:door.longhouse` (asked for `prop_iron_banded_oak_door`): its building is drawn in greybox, so its door is a panel
- `effect:cast_charge` (asked for `vfx.magic.cast_charge`): its flipbook is not in the effect manifest, or would not load: the greybox tell
- `effect:effect.braced` (asked for `vfx.warding.brace_ward_shell`): its flipbook is not in the effect manifest, or would not load: the greybox tell
- `effect:effect.mending` (asked for `vfx.vital.mending_thread_restore`): its flipbook is not in the effect manifest, or would not load: the greybox tell
- `effect:strain` (asked for `vfx.resonance.strain_overlay`): its flipbook is not in the effect manifest, or would not load: the greybox tell
- `icon:compass` (asked for `ui.hud.compass`): no compass dial in the icon manifest: a plain strip
- `icon:effect.braced` (asked for `ui.formula.brace_ward`): its icon is not in the icon manifest, or would not load
- `icon:effect.mending` (asked for `ui.formula.mending_thread`): its icon is not in the icon manifest, or would not load
- `icon:focus` (asked for `ui.resource.focus`): its icon is not in the icon manifest, or would not load
- `icon:follow` (asked for `ui.companion.follow`): its icon is not in the icon manifest, or would not load
- `icon:health` (asked for `ui.resource.health`): its icon is not in the icon manifest, or would not load
- `icon:spell.force.impulse_bolt` (asked for `ui.formula.impulse_bolt`): its icon is not in the icon manifest, or would not load
- `icon:spell.vital.mending_thread` (asked for `ui.formula.mending_thread`): its icon is not in the icon manifest, or would not load
- `icon:spell.warding.brace_ward` (asked for `ui.formula.brace_ward`): its icon is not in the icon manifest, or would not load
- `icon:stamina` (asked for `ui.resource.stamina`): its icon is not in the icon manifest, or would not load
- `icon:strained` (asked for `ui.status.strained`): its icon is not in the icon manifest, or would not load
- `icon:wait` (asked for `ui.companion.wait`): its icon is not in the icon manifest, or would not load
- `node:node.ore.iron_seam:ready` (asked for `node_iron_seam_ore`): not drawn: greybox lumps
- `node:node.ore.iron_seam:spent` (asked for `node_iron_seam_worked`): not drawn: greybox lumps, spent
- `node:node.wood.ash_stand:ready` (asked for `flora_young_ash_stand`): not drawn: three greybox trunks
- `node:node.wood.ash_stand:spent` (asked for `flora_ash_stumps`): not drawn: three greybox trunks, spent
- `person:npc.ashen_hollow.kera_voss` (asked for `npc_kal_smith`): not drawn: the greybox mannequin
- `person:npc.ashen_hollow.renn_vale` (asked for `npc_veth_magistrate`): not drawn: the greybox mannequin
- `person:npc.ashen_hollow.sel_arien` (asked for `npc_siann_archivist`): not drawn: the greybox mannequin
- `person:npc.ashen_hollow.tavar_orr` (asked for `npc_orenth_guide`): not drawn: the greybox mannequin
- `person:player` (asked for `player_veth_wanderer`): not drawn: the greybox mannequin
- `piece:piece.pad.timber`: M7 ships no piece art
- `piece:piece.wall.timber`: M7 ships no piece art
- `roof:forge_roof`: its building is drawn in greybox: a flat slab over the walls
- `roof:longhouse_roof`: its building is drawn in greybox: a flat slab over the walls
- `scatter:stones` (asked for `material_limestone_ashlar`): its material would not load
- `station:anvil` (asked for `prop_blacksmith_anvil_stump`): not drawn: greybox blocks
- `station:forge` (asked for `prop_forge_hearth`): not drawn: greybox blocks
- `structure:beam_woundmoss` (asked for `prop_woundmoss_beam`): not drawn: a box
- `structure:cart_wreck` (asked for `prop_cart_damaged_merchant`): not drawn: a box
- `structure:den_rock_back` (asked for `rock_den_back`): not drawn: a box
- `structure:den_rock_east` (asked for `rock_den_east`): not drawn: a box
- `structure:den_rock_west` (asked for `rock_den_west`): not drawn: a box
- `structure:fallen_timber` (asked for `flora_fallen_log`): not drawn: a box
- `structure:fence_smithy` (asked for `building_fence_panel`): not drawn: a box
- `structure:forge_east`: no binding: a box
- `structure:forge_north`: no binding: a box
- `structure:forge_south`: no binding: a box
- `structure:forge_west_north`: no binding: a box
- `structure:forge_west_south`: no binding: a box
- `structure:longhouse_east_north`: no binding: a box
- `structure:longhouse_east_south`: no binding: a box
- `structure:longhouse_north`: no binding: a box
- `structure:longhouse_south`: no binding: a box
- `structure:longhouse_west`: no binding: a box
- `structure:rock_beam_east` (asked for `rock_outcrop_beam_support`): not drawn: a cylinder
- `structure:rock_beam_west` (asked for `rock_outcrop_beam_support`): not drawn: a cylinder
- `structure:rock_blocked_shaft` (asked for `prop_blocked_shaft`): not drawn: a box
- `structure:rock_foldscar_heart` (asked for `landmark_foldscar_heart`): not drawn: a cylinder
- `structure:rock_iron_seam` (asked for `rock_iron_seam_outcrop`): not drawn: a cylinder
- `structure:rock_rim_01` (asked for `rock_outcrop_rim_a`): not drawn: a cylinder
- `structure:rock_rim_02` (asked for `rock_outcrop_rim_b`): not drawn: a cylinder
- `structure:rock_rim_03` (asked for `rock_outcrop_rim_c`): not drawn: a cylinder
- `structure:rock_rim_04` (asked for `rock_outcrop_rim_a`): not drawn: a cylinder
- `structure:rock_stone_north` (asked for `landmark_quiet_stone`): not drawn: a cylinder
- `structure:rock_stone_southeast` (asked for `landmark_quiet_stone`): not drawn: a cylinder
- `structure:rock_stone_southwest` (asked for `landmark_quiet_stone`): not drawn: a cylinder
- `structure:rock_waystone` (asked for `landmark_ashen_waystone`): not drawn: a cylinder
- `structure:rock_well` (asked for `building_well`): not drawn: a cylinder
- `structure:survey_table` (asked for `prop_long_trestle_table`): not drawn: a box
- `structure:tree_01` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_02` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_03` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_04` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_05` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_06` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_07` (asked for `flora_pine_tree`): not drawn: a cylinder
- `structure:tree_08` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_09` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_10` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_11` (asked for `flora_pine_tree`): not drawn: a cylinder
- `structure:tree_12` (asked for `flora_pine_tree`): not drawn: a cylinder
- `structure:tree_13` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_14` (asked for `flora_pine_tree`): not drawn: a cylinder
- `structure:tree_15` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_16` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_17` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_18` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_19` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_20` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_21` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_22` (asked for `flora_pine_tree`): not drawn: a cylinder
- `structure:tree_23` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_24` (asked for `flora_pine_tree`): not drawn: a cylinder
- `structure:tree_25` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_26` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_27` (asked for `flora_pine_tree`): not drawn: a cylinder
- `structure:tree_28` (asked for `flora_pine_tree`): not drawn: a cylinder
- `structure:tree_29` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_30` (asked for `flora_dead_tree`): not drawn: a cylinder
- `structure:tree_31` (asked for `flora_oak_tree`): not drawn: a cylinder
- `structure:tree_32` (asked for `flora_pine_tree`): not drawn: a cylinder
- `switch_mark:switch.foldscar_heart`: a pale band drawn in code round the greybox stone's top, shown once it is set; no asset
- `switch_mark:switch.stone_north`: a pale band drawn in code round the greybox stone's top, shown once it is set; no asset
- `switch_mark:switch.stone_southeast`: a pale band drawn in code round the greybox stone's top, shown once it is set; no asset
- `switch_mark:switch.stone_southwest`: a pale band drawn in code round the greybox stone's top, shown once it is set; no asset
- `terrain:r_0_0:c_00_00` (asked for `material_loose_gravel`): its material would not load
- `terrain:r_0_0:c_00_01` (asked for `material_packed_dirt_ground`): its material would not load
- `terrain:r_0_0:c_01_00` (asked for `material_churned_wet_mud`): its material would not load
- `terrain:r_0_0:c_01_01` (asked for `material_marsh_grass_turf`): its material would not load
- `weapon:weapon_arming_sword` (asked for `weapon_arming_sword`): not drawn: a greybox weapon
- `weapon:weapon_boar_spear_hunting` (asked for `weapon_boar_spear_hunting`): not drawn: a greybox weapon
- `weapon:weapon_yew_longbow_warbow` (asked for `weapon_yew_longbow_warbow`): not drawn: a greybox weapon

## Allowed fallbacks

- `debug:discovery_rings`: the F3 overlay's discovery rings, flat translucent discs - allowed: the F3 debug overlay's markers, drawn only while a developer has the overlay on; never shown in normal play (charter §3: no markers)
