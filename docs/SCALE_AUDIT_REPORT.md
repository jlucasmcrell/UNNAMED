# Semantic scale audit

Generated 2026-09-23 21:19 from `assets/catalog.json` (built 2026-09-23 21:18:34) against `assets/manifests/semantic_dimensions.json`.

## The problem

The 3D pipeline scaled every asset by its **longest axis** to a per-category default (`UNIT_HINT_MAP` in `_blender_cleanup.py`: prop 0.5 m, weapon 1.2 m, creature 1.8 m, building 4.0 m). Size therefore records the asset's *category*, not what the object is. A birch tree, an oak door and a coastal ship all measure exactly 0.5 m; a bollock dagger measures 1.2 m.

## Two independent tests

1. **Normalisation signature** — objective, needs no expectation table. Does the measured longest axis land exactly on a category default? If so, the category decided the size. This cannot be argued with.
2. **Semantic comparison** — measured against a declared expected size. `UNKNOWN_SCALE` is a real result, not a failure: an honest unknown beats a fabricated expectation.

## Results

- Audited: **500** of 500 catalogued assets
- **PASS_SCALE**: 248
- **SUSPECT_SCALE**: 127
- **FAIL_SCALE**: 51
- **UNKNOWN_SCALE**: 74
- Carrying the **category-normalisation signature**: **364** (72.8%)

Expected/measured ratio bands: PASS 0.8-1.25, SUSPECT 0.4-2.5, outside that FAIL.

## By family

| Family | assets | PASS | SUSPECT | FAIL | UNKNOWN |
|---|---:|---:|---:|---:|---:|
| `weapon_` | 108 | 27 | 56 | 15 | 10 |
| `prop_` | 70 | 69 | 1 | 0 | 0 |
| `creature_` | 62 | 31 | 22 | 9 | 0 |
| `item_` | 47 | 7 | 8 | 2 | 30 |
| `magic_` | 40 | 5 | 7 | 9 | 19 |
| `travel_` | 36 | 36 | 0 | 0 | 0 |
| `resource_` | 21 | 5 | 14 | 2 | 0 |
| `flora_` | 14 | 14 | 0 | 0 | 0 |
| `building_` | 13 | 12 | 1 | 0 | 0 |
| `weaponcomp_` | 10 | 0 | 0 | 0 | 10 |
| `animal_` | 8 | 1 | 5 | 2 | 0 |
| `container_` | 8 | 8 | 0 | 0 | 0 |
| `npc_` | 8 | 6 | 2 | 0 | 0 |
| `race_` | 8 | 6 | 2 | 0 | 0 |
| `racebody_` | 8 | 6 | 2 | 0 | 0 |
| `armour_` | 6 | 4 | 0 | 1 | 1 |
| `mount_` | 6 | 0 | 0 | 6 | 0 |
| `vehicle_` | 6 | 0 | 0 | 5 | 1 |
| `tool_` | 5 | 3 | 2 | 0 | 0 |
| `reagent_` | 4 | 0 | 4 | 0 | 0 |
| `landmark_` | 3 | 3 | 0 | 0 | 0 |
| `rock_` | 3 | 3 | 0 | 0 | 0 |
| `race2_` | 2 | 2 | 0 | 0 | 0 |
| `forge_` | 1 | 0 | 0 | 0 | 1 |
| `herb_` | 1 | 0 | 1 | 0 | 0 |
| `longhouse_` | 1 | 0 | 0 | 0 | 1 |
| `magiccomp_` | 1 | 0 | 0 | 0 | 1 |

## Worst offenders (most undersized vs expectation)

| Asset | Measured | Expected | Ratio | Category-normalised to |
|---|---:|---:|---:|---|
| `vehicle_coastal_ship` | 0.5 | 20.0 | 0.025 | prop (0.5 m) |
| `vehicle_river_ferry_boat` | 0.5 | 20.0 | 0.025 | prop (0.5 m) |
| `vehicle_four_wheel_wagon` | 0.5 | 3.5 | 0.143 | prop (0.5 m) |
| `vehicle_handcart` | 0.5 | 3.5 | 0.143 | prop (0.5 m) |
| `vehicle_wooden_cart` | 0.5 | 3.5 | 0.143 | prop (0.5 m) |
| `mount_camel` | 0.5 | 2.4 | 0.208 | prop (0.5 m) |
| `mount_draft_ox` | 0.5 | 2.4 | 0.208 | prop (0.5 m) |
| `mount_pack_mule` | 0.5 | 2.4 | 0.208 | prop (0.5 m) |
| `mount_riding_horse` | 0.5 | 2.4 | 0.208 | prop (0.5 m) |
| `mount_sled_dog_team` | 0.5 | 2.4 | 0.208 | prop (0.5 m) |
| `mount_warhorse` | 0.5 | 2.4 | 0.208 | prop (0.5 m) |
| `resource_ironwood_log` | 0.5 | 2.4 | 0.208 | prop (0.5 m) |
| `resource_linen_fibre_bundle` | 0.5 | 2.4 | 0.208 | prop (0.5 m) |
| `magic_altar_portable` | 0.5 | 1.8 | 0.278 | prop (0.5 m) |
| `magic_divining_rods` | 0.5 | 1.8 | 0.278 | prop (0.5 m) |
| `magic_rod_bone` | 0.5 | 1.8 | 0.278 | prop (0.5 m) |
| `magic_rod_compact` | 0.5 | 1.8 | 0.278 | prop (0.5 m) |
| `weapon_couched_lance_knight` | 1.2 | 3.2 | 0.375 | weapon (1.2 m) |
| `weapon_infantry_pike_long` | 1.2 | 3.2 | 0.375 | weapon (1.2 m) |
| `resource_cured_leather_roll` | 0.5 | 1.2 | 0.417 | prop (0.5 m) |
| `resource_raw_hide` | 0.5 | 1.2 | 0.417 | prop (0.5 m) |
| `resource_rawhide_pelt_fold` | 0.5 | 1.2 | 0.417 | prop (0.5 m) |
| `weapon_bill_hook_peasant` | 1.2 | 2.2 | 0.545 | weapon (1.2 m) |
| `weapon_corseque_serpent_blade` | 1.2 | 2.2 | 0.545 | weapon (1.2 m) |
| `weapon_elven_glaive_hooked` | 1.2 | 2.2 | 0.545 | weapon (1.2 m) |

## Worst offenders (most oversized vs expectation)

| Asset | Measured | Expected | Ratio | Category-normalised to |
|---|---:|---:|---:|---|
| `weapon_chitin_dagger_swamp` | 1.2 | 0.3 | 4.0 | weapon (1.2 m) |
| `weapon_crystal_shard_dagger` | 1.2 | 0.3 | 4.0 | weapon (1.2 m) |
| `weapon_kris_wavy_dagger` | 1.2 | 0.3 | 4.0 | weapon (1.2 m) |
| `weapon_main_gauche_parry` | 1.2 | 0.3 | 4.0 | weapon (1.2 m) |
| `weapon_parrying_dirk_brass` | 1.2 | 0.3 | 4.0 | weapon (1.2 m) |
| `weapon_serrated_assassin_dagger` | 1.2 | 0.3 | 4.0 | weapon (1.2 m) |
| `weapon_stiletto_thrusting` | 1.2 | 0.3 | 4.0 | weapon (1.2 m) |
| `weapon_throwing_knife_balanced` | 1.2 | 0.3 | 4.0 | weapon (1.2 m) |
| `weapon_bollock_dagger` | 1.2 | 0.32 | 3.75 | weapon (1.2 m) |
| `weapon_ash_wand_copper` | 1.2 | 0.35 | 3.429 | weapon (1.2 m) |
| `weapon_ember_wood_wand` | 1.2 | 0.35 | 3.429 | weapon (1.2 m) |
| `weapon_obsidian_tipped_wand` | 1.2 | 0.35 | 3.429 | weapon (1.2 m) |
| `item_otherwrought_key` | 0.5 | 0.15 | 3.333 | prop (0.5 m) |
| `item_rusty_key` | 0.5 | 0.15 | 3.333 | prop (0.5 m) |
| `magic_lantern_warding` | 0.5 | 0.15 | 3.333 | prop (0.5 m) |
| `magic_ring_silver` | 0.5 | 0.15 | 3.333 | prop (0.5 m) |
| `magic_talisman_bone` | 0.5 | 0.15 | 3.333 | prop (0.5 m) |
| `magic_ward_stone_small` | 0.5 | 0.15 | 3.333 | prop (0.5 m) |
| `creature_frost_hare_fey` | 1.8 | 0.55 | 3.273 | creature (1.8 m) |
| `creature_snow_hare_hopper` | 1.8 | 0.55 | 3.273 | creature (1.8 m) |
| `animal_messenger_raven` | 1.8 | 0.6 | 3.0 | creature (1.8 m) |
| `animal_scout_hawk` | 1.8 | 0.6 | 3.0 | creature (1.8 m) |
| `creature_bog_raven_scout` | 1.8 | 0.6 | 3.0 | creature (1.8 m) |
| `creature_cave_marsh_bat` | 1.8 | 0.6 | 3.0 | creature (1.8 m) |
| `creature_cliff_hawk_hunter` | 1.8 | 0.6 | 3.0 | creature (1.8 m) |

## Full listing

| Asset | Verdict | Measured | Expected | Ratio | Confidence | Expectation source |
|---|---|---:|---:|---:|---|---|
| `animal_messenger_raven` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'raven' |
| `animal_scout_hawk` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'hawk' |
| `armour_gorget_plate_a` | FAIL_SCALE | 0.8154 | 0.32 | 2.548 | medium | subject rule 'gorget' |
| `creature_bog_raven_scout` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'raven' |
| `creature_cave_marsh_bat` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'bat' |
| `creature_cliff_hawk_hunter` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'hawk' |
| `creature_desert_vulture` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'vulture' |
| `creature_frost_hare_fey` | FAIL_SCALE | 1.8 | 0.55 | 3.273 | high | subject rule 'hare' |
| `creature_great_horned_owl` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'owl' |
| `creature_snow_hare_hopper` | FAIL_SCALE | 1.8 | 0.55 | 3.273 | high | subject rule 'hare' |
| `creature_storm_falcon` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'falcon' |
| `creature_twilight_moth` | FAIL_SCALE | 1.8 | 0.6 | 3.0 | medium | subject rule 'moth' |
| `item_otherwrought_key` | FAIL_SCALE | 0.5 | 0.15 | 3.333 | high | subject rule 'key' |
| `item_rusty_key` | FAIL_SCALE | 0.5 | 0.15 | 3.333 | high | subject rule 'key' |
| `magic_altar_portable` | FAIL_SCALE | 0.5 | 1.8 | 0.278 | medium | subject rule 'altar' |
| `magic_divining_rods` | FAIL_SCALE | 0.5 | 1.8 | 0.278 | high | subject rule 'rod' |
| `magic_lantern_warding` | FAIL_SCALE | 0.5 | 0.15 | 3.333 | high | subject rule 'ward' |
| `magic_ring_silver` | FAIL_SCALE | 0.5 | 0.15 | 3.333 | high | subject rule 'ring' |
| `magic_rod_bone` | FAIL_SCALE | 0.5 | 1.8 | 0.278 | high | subject rule 'rod' |
| `magic_rod_compact` | FAIL_SCALE | 0.5 | 1.8 | 0.278 | high | subject rule 'rod' |
| `magic_scrying_bowl` | FAIL_SCALE | 1.2 | 0.45 | 2.667 | high | subject rule 'bowl' |
| `magic_talisman_bone` | FAIL_SCALE | 0.5 | 0.15 | 3.333 | high | subject rule 'talisman' |
| `magic_ward_stone_small` | FAIL_SCALE | 0.5 | 0.15 | 3.333 | high | subject rule 'ward' |
| `mount_camel` | FAIL_SCALE | 0.5 | 2.4 | 0.208 | low | family default 'mount_' |
| `mount_draft_ox` | FAIL_SCALE | 0.5 | 2.4 | 0.208 | low | family default 'mount_' |
| `mount_pack_mule` | FAIL_SCALE | 0.5 | 2.4 | 0.208 | low | family default 'mount_' |
| `mount_riding_horse` | FAIL_SCALE | 0.5 | 2.4 | 0.208 | low | family default 'mount_' |
| `mount_sled_dog_team` | FAIL_SCALE | 0.5 | 2.4 | 0.208 | low | family default 'mount_' |
| `mount_warhorse` | FAIL_SCALE | 0.5 | 2.4 | 0.208 | low | family default 'mount_' |
| `resource_ironwood_log` | FAIL_SCALE | 0.5 | 2.4 | 0.208 | medium | subject rule 'log' |
| `resource_linen_fibre_bundle` | FAIL_SCALE | 0.5 | 2.4 | 0.208 | medium | subject rule 'bundle' |
| `vehicle_coastal_ship` | FAIL_SCALE | 0.5 | 20.0 | 0.025 | medium | asset override |
| `vehicle_four_wheel_wagon` | FAIL_SCALE | 0.5 | 3.5 | 0.143 | medium | subject rule 'wagon' |
| `vehicle_handcart` | FAIL_SCALE | 0.5 | 3.5 | 0.143 | medium | subject rule 'cart' |
| `vehicle_river_ferry_boat` | FAIL_SCALE | 0.5 | 20.0 | 0.025 | medium | subject rule 'boat' |
| `vehicle_wooden_cart` | FAIL_SCALE | 0.5 | 3.5 | 0.143 | medium | subject rule 'cart' |
| `weapon_ash_wand_copper` | FAIL_SCALE | 1.2 | 0.35 | 3.429 | high | subject rule 'wand' |
| `weapon_bollock_dagger` | FAIL_SCALE | 1.2 | 0.32 | 3.75 | high | asset override |
| `weapon_chitin_dagger_swamp` | FAIL_SCALE | 1.2 | 0.3 | 4.0 | high | subject rule 'dagger' |
| `weapon_couched_lance_knight` | FAIL_SCALE | 1.2 | 3.2 | 0.375 | medium | subject rule 'lance' |
| `weapon_crystal_shard_dagger` | FAIL_SCALE | 1.2 | 0.3 | 4.0 | high | subject rule 'dagger' |
| `weapon_ember_wood_wand` | FAIL_SCALE | 1.2 | 0.35 | 3.429 | high | subject rule 'wand' |
| `weapon_infantry_pike_long` | FAIL_SCALE | 1.2 | 3.2 | 0.375 | medium | subject rule 'pike' |
| `weapon_kris_wavy_dagger` | FAIL_SCALE | 1.2 | 0.3 | 4.0 | high | subject rule 'dagger' |
| `weapon_main_gauche_parry` | FAIL_SCALE | 1.2 | 0.3 | 4.0 | high | subject rule 'main gauche' |
| `weapon_obsidian_tipped_wand` | FAIL_SCALE | 1.2 | 0.35 | 3.429 | high | subject rule 'wand' |
| `weapon_parrying_dirk_brass` | FAIL_SCALE | 1.2 | 0.3 | 4.0 | high | subject rule 'dirk' |
| `weapon_seax_short_blade` | FAIL_SCALE | 1.2 | 0.45 | 2.667 | high | subject rule 'seax' |
| `weapon_serrated_assassin_dagger` | FAIL_SCALE | 1.2 | 0.3 | 4.0 | high | subject rule 'dagger' |
| `weapon_stiletto_thrusting` | FAIL_SCALE | 1.2 | 0.3 | 4.0 | high | subject rule 'stiletto' |
| `weapon_throwing_knife_balanced` | FAIL_SCALE | 1.2 | 0.3 | 4.0 | high | subject rule 'throwing knife' |
| `animal_truffle_pig` | PASS_SCALE | 1.8 | 1.5 | 1.2 | high | subject rule 'pig' |
| `armour_chest_plate_base_a` | PASS_SCALE | 0.9015 | 0.9 | 1.002 | medium | subject rule 'chest' |
| `armour_chest_underlayer_gambeson_a` | PASS_SCALE | 0.926 | 0.9 | 1.029 | medium | subject rule 'chest' |
| `armour_glove_pair_a` | PASS_SCALE | 0.26 | 0.26 | 1.0 | high | subject rule 'glove' |
| `armour_leg_garment_a` | PASS_SCALE | 1.05 | 1.05 | 1.0 | high | subject rule 'leg_garment' |
| `building_beam` | PASS_SCALE | 3.0 | 3.0 | 1.0 | high | subject rule 'beam' |
| `building_door_frame` | PASS_SCALE | 2.44 | 2.44 | 1.0 | high | subject rule 'door_frame' |
| `building_fence_panel` | PASS_SCALE | 2.4 | 2.4 | 1.0 | high | subject rule 'fence_panel' |
| `building_floor_planks` | PASS_SCALE | 3.0 | 3.0 | 1.0 | high | subject rule 'floor_planks' |
| `building_post` | PASS_SCALE | 2.6 | 2.6 | 1.0 | high | subject rule 'post' |
| `building_roof_panel` | PASS_SCALE | 3.0 | 3.0 | 1.0 | high | subject rule 'roof_panel' |
| `building_ruin_wall` | PASS_SCALE | 2.9909 | 3.0 | 0.997 | high | subject rule 'ruin_wall' |
| `building_step` | PASS_SCALE | 1.2 | 1.2 | 1.0 | high | subject rule 'step' |
| `building_wall_stone` | PASS_SCALE | 3.0003 | 3.0 | 1.0 | high | subject rule 'wall_stone' |
| `building_wall_timber` | PASS_SCALE | 3.0 | 3.0 | 1.0 | high | subject rule 'wall_timber' |
| `building_well` | PASS_SCALE | 2.82 | 2.82 | 1.0 | high | subject rule 'well' |
| `building_window_frame` | PASS_SCALE | 1.1 | 1.1 | 1.0 | high | subject rule 'window_frame' |
| `container_alchemy_cold_box` | PASS_SCALE | 0.8 | 0.8 | 1.0 | medium | subject rule 'box' |
| `container_ammunition_case` | PASS_SCALE | 0.45 | 0.45 | 1.0 | high | subject rule 'ammunition_case' |
| `container_armory_rack` | PASS_SCALE | 0.8 | 0.8 | 1.0 | medium | subject rule 'armory_rack' |
| `container_backpack_traveller` | PASS_SCALE | 0.5 | 0.45 | 1.111 | high | subject rule 'pack' |
| `container_bank_strongbox` | PASS_SCALE | 0.8 | 0.8 | 1.0 | medium | subject rule 'box' |
| `container_barrel_oak` | PASS_SCALE | 0.85 | 0.85 | 1.0 | high | subject rule 'barrel' |
| `container_belt_pouch_leather` | PASS_SCALE | 0.45 | 0.45 | 1.0 | high | subject rule 'pouch' |
| `container_chest_iron_banded` | PASS_SCALE | 0.8 | 0.8 | 1.0 | medium | subject rule 'chest' |
| `creature_animated_armour` | PASS_SCALE | 2.0 | 2.0 | 1.0 | medium | subject rule 'animated_armour' |
| `creature_ash_ember_hound` | PASS_SCALE | 1.3 | 1.3 | 1.0 | high | subject rule 'hound' |
| `creature_barrow_wight_lord` | PASS_SCALE | 1.8 | 1.8 | 1.0 | high | subject rule 'wight' |
| `creature_bog_lurker_beast` | PASS_SCALE | 1.8 | 2.0 | 0.9 | medium | subject rule 'lurker' |
| `creature_bone_walker_husk` | PASS_SCALE | 1.8 | 1.8 | 1.0 | high | subject rule 'walker' |
| `creature_bristleback_boar` | PASS_SCALE | 1.5 | 1.5 | 1.0 | medium | subject rule 'boar' |
| `creature_cave_hunting_spider` | PASS_SCALE | 1.2 | 1.2 | 1.0 | medium | subject rule 'spider' |
| `creature_clay_servitor` | PASS_SCALE | 1.8 | 2.0 | 0.9 | medium | subject rule 'servitor' |
| `creature_crystal_antler_stag` | PASS_SCALE | 1.8 | 2.2 | 0.818 | medium | subject rule 'stag' |
| `creature_desert_camel_drover` | PASS_SCALE | 1.8 | 2.2 | 0.818 | medium | subject rule 'camel' |
| `creature_frost_wolf` | PASS_SCALE | 1.3 | 1.3 | 1.0 | high | subject rule 'wolf' |
| `creature_giant_stag_beetle` | PASS_SCALE | 1.8 | 2.2 | 0.818 | medium | subject rule 'stag' |
| `creature_glacier_seal_hunter` | PASS_SCALE | 1.8 | 1.5 | 1.2 | medium | subject rule 'seal' |
| `creature_great_river_serpent` | PASS_SCALE | 3.0 | 3.0 | 1.0 | medium | subject rule 'serpent' |
| `creature_grove_bound_dryad_ox` | PASS_SCALE | 1.8 | 2.2 | 0.818 | medium | subject rule 'ox' |
| `creature_highland_brown_bear` | PASS_SCALE | 2.0 | 2.0 | 1.0 | medium | subject rule 'bear' |
| `creature_iron_boar_automaton` | PASS_SCALE | 1.8 | 1.5 | 1.2 | medium | subject rule 'boar' |
| `creature_marsh_heron_stalker` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'creature_' |
| `creature_mountain_ram_goat` | PASS_SCALE | 1.8 | 1.5 | 1.2 | medium | subject rule 'goat' |
| `creature_musk_ox_herder` | PASS_SCALE | 1.8 | 2.2 | 0.818 | medium | subject rule 'ox' |
| `creature_plains_antelope_doe` | PASS_SCALE | 1.8 | 2.2 | 0.818 | medium | subject rule 'antelope' |
| `creature_rotting_revenant` | PASS_SCALE | 1.8 | 1.8 | 1.0 | high | subject rule 'revenant' |
| `creature_steppe_bison_bull` | PASS_SCALE | 1.8 | 2.2 | 0.818 | medium | subject rule 'bison' |
| `creature_stone_guardian` | PASS_SCALE | 1.8 | 2.0 | 0.9 | medium | subject rule 'guardian' |
| `creature_storm_charged_ram` | PASS_SCALE | 1.8 | 1.5 | 1.2 | medium | subject rule 'ram' |
| `creature_tawny_cave_panther` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'creature_' |
| `creature_timber_stag_elk` | PASS_SCALE | 1.8 | 2.2 | 0.818 | medium | subject rule 'stag' |
| `creature_tundra_wolverine` | PASS_SCALE | 1.8 | 1.5 | 1.2 | medium | subject rule 'wolverine' |
| `creature_tunnel_centipede_brute` | PASS_SCALE | 1.8 | 2.0 | 0.9 | medium | subject rule 'brute' |
| `creature_wart_toad_brute` | PASS_SCALE | 1.8 | 2.0 | 0.9 | medium | subject rule 'brute' |
| `creature_woolly_highland_sheep` | PASS_SCALE | 1.8 | 1.5 | 1.2 | medium | subject rule 'sheep' |
| `flora_birch_tree` | PASS_SCALE | 15.0 | 15.0 | 1.0 | medium | asset override |
| `flora_boneleaf_bush` | PASS_SCALE | 1.2 | 1.2 | 1.0 | medium | subject rule 'bush' |
| `flora_bracket_fungus` | PASS_SCALE | 0.45 | 0.45 | 1.0 | medium | subject rule 'fungus' |
| `flora_bramble_bush` | PASS_SCALE | 1.2 | 1.2 | 1.0 | medium | subject rule 'bush' |
| `flora_cattail_clump` | PASS_SCALE | 1.8 | 1.8 | 1.0 | medium | subject rule 'cattail' |
| `flora_dead_tree` | PASS_SCALE | 14.0 | 14.0 | 1.0 | medium | subject rule 'tree' |
| `flora_fern_clump` | PASS_SCALE | 0.45 | 0.45 | 1.0 | medium | subject rule 'fern' |
| `flora_glowcap_cluster` | PASS_SCALE | 0.45 | 0.45 | 1.0 | medium | subject rule 'glowcap' |
| `flora_heather_patch` | PASS_SCALE | 0.45 | 0.45 | 1.0 | medium | subject rule 'heather' |
| `flora_mirrorfern` | PASS_SCALE | 0.45 | 0.45 | 1.0 | medium | subject rule 'fern' |
| `flora_oak_tree` | PASS_SCALE | 14.0 | 14.0 | 1.0 | medium | subject rule 'oak' |
| `flora_pine_tree` | PASS_SCALE | 14.0 | 14.0 | 1.0 | medium | subject rule 'pine' |
| `flora_slowtree` | PASS_SCALE | 14.0 | 14.0 | 1.0 | medium | subject rule 'tree' |
| `flora_willow_tree` | PASS_SCALE | 14.0 | 14.0 | 1.0 | medium | subject rule 'tree' |
| `item_bronze_ingot` | PASS_SCALE | 0.3 | 0.3 | 1.0 | high | subject rule 'ingot' |
| `item_charcoal_steel_ingot` | PASS_SCALE | 0.3 | 0.3 | 1.0 | high | subject rule 'ingot' |
| `item_gold_ingot` | PASS_SCALE | 0.3 | 0.3 | 1.0 | high | subject rule 'ingot' |
| `item_grey_iron_ingot` | PASS_SCALE | 0.3 | 0.3 | 1.0 | high | subject rule 'ingot' |
| `item_health_potion` | PASS_SCALE | 0.22 | 0.22 | 1.0 | high | subject rule 'potion' |
| `item_leather_boots_pair` | PASS_SCALE | 0.32 | 0.32 | 1.0 | high | subject rule 'boot' |
| `item_steel_plate_helm` | PASS_SCALE | 0.3 | 0.3 | 1.0 | high | subject rule 'helm' |
| `landmark_ashen_waystone` | PASS_SCALE | 2.8 | 2.8 | 1.0 | high | subject rule 'landmark_ashen_waystone' |
| `landmark_foldscar_core` | PASS_SCALE | 6.0 | 6.0 | 1.0 | high | subject rule 'landmark_foldscar_core' |
| `landmark_quiet_stone` | PASS_SCALE | 2.2 | 2.2 | 1.0 | high | subject rule 'landmark_quiet_stone' |
| `magic_amulet_copper` | PASS_SCALE | 0.15 | 0.15 | 1.0 | high | subject rule 'amulet' |
| `magic_brazier_small` | PASS_SCALE | 0.5 | 0.45 | 1.111 | high | subject rule 'brazier' |
| `magic_cauldron_small` | PASS_SCALE | 0.5 | 0.45 | 1.111 | high | subject rule 'cauldron' |
| `magic_censer_brass` | PASS_SCALE | 0.5 | 0.45 | 1.111 | high | subject rule 'censer' |
| `magic_grimoire_bound` | PASS_SCALE | 0.35 | 0.35 | 1.0 | high | subject rule 'grimoire' |
| `npc_constructed_engineer` | PASS_SCALE | 1.8 | 1.8 | 1.0 | medium | family default 'npc_' |
| `npc_kal_smith` | PASS_SCALE | 1.3 | 1.3 | 1.0 | high | subject rule 'kal' |
| `npc_mor_witness` | PASS_SCALE | 1.8 | 1.8 | 1.0 | medium | family default 'npc_' |
| `npc_orenth_guide` | PASS_SCALE | 1.8 | 1.8 | 1.0 | medium | family default 'npc_' |
| `npc_siann_archivist` | PASS_SCALE | 1.8 | 1.8 | 1.0 | medium | family default 'npc_' |
| `npc_veth_magistrate` | PASS_SCALE | 1.8 | 1.8 | 1.0 | medium | family default 'npc_' |
| `prop_apothecary_jar_rack` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'jar' |
| `prop_banded_ash_sea_chest` | PASS_SCALE | 1.1 | 1.1 | 1.0 | medium | subject rule 'chest' |
| `prop_banded_writing_desk` | PASS_SCALE | 1.6 | 1.6 | 1.0 | medium | subject rule 'writing_desk' |
| `prop_blacksmith_anvil_stump` | PASS_SCALE | 0.7 | 0.7 | 1.0 | high | subject rule 'anvil' |
| `prop_blocked_shaft` | PASS_SCALE | 2.6 | 2.6 | 1.0 | high | subject rule 'prop_blocked_shaft' |
| `prop_brass_balance_scales` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'balance_scales' |
| `prop_brass_hand_bell` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'hand_bell' |
| `prop_brass_processional_lantern` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'lantern' |
| `prop_brass_tinder_box` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'tinder_box' |
| `prop_broken_masonry_pile` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'masonry_pile' |
| `prop_bundle_of_grain_sacks` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'grain_sacks' |
| `prop_bundled_firewood_stack` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'stack' |
| `prop_butter_churn_with_dasher` | PASS_SCALE | 0.85 | 0.85 | 1.0 | high | subject rule 'churn' |
| `prop_campfire_tripod` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'campfire_tripod' |
| `prop_canvas_haversack` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'haversack' |
| `prop_canvas_rolled_bedroll` | PASS_SCALE | 1.4 | 1.4 | 1.0 | medium | subject rule 'bedroll' |
| `prop_carpenters_tool_chest` | PASS_SCALE | 1.1 | 1.1 | 1.0 | medium | subject rule 'chest' |
| `prop_carpenters_workbench` | PASS_SCALE | 1.6 | 1.6 | 1.0 | medium | subject rule 'workbench' |
| `prop_cart_damaged_merchant` | PASS_SCALE | 2.2 | 2.2 | 1.0 | high | subject rule 'prop_cart_damaged_merchant' |
| `prop_carved_oak_armchair` | PASS_SCALE | 1.1 | 1.1 | 1.0 | medium | subject rule 'armchair' |
| `prop_carved_stone_sarcophagus` | PASS_SCALE | 1.4 | 1.4 | 1.0 | medium | subject rule 'sarcophagus' |
| `prop_carved_stone_trough` | PASS_SCALE | 0.85 | 0.85 | 1.0 | high | subject rule 'trough' |
| `prop_clay_amphora_vessel` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'amphora' |
| `prop_clay_oil_lamp_with_handle` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'oil_lamp' |
| `prop_clay_water_jug` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'jug' |
| `prop_cobblers_stitching_bench` | PASS_SCALE | 1.4 | 1.4 | 1.0 | medium | subject rule 'bench' |
| `prop_coiled_straw_skep` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'skep' |
| `prop_collapsed_stone_arch` | PASS_SCALE | 2.4 | 2.4 | 1.0 | medium | subject rule 'arch' |
| `prop_coopered_dye_vat_paddle` | PASS_SCALE | 0.85 | 0.85 | 1.0 | high | subject rule 'dye_vat' |
| `prop_copper_water_bucket` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'bucket' |
| `prop_cracked_stone_urn` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'urn' |
| `prop_dwarven_forge_lantern` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'lantern' |
| `prop_forge_double_bellows` | PASS_SCALE | 1.3 | 1.3 | 1.0 | medium | subject rule 'bellows' |
| `prop_garden_wheelbarrow` | PASS_SCALE | 1.3 | 1.3 | 1.0 | medium | subject rule 'wheelbarrow' |
| `prop_grain_cradle_scythe` | PASS_SCALE | 1.4 | 1.4 | 1.0 | medium | subject rule 'cradle' |
| `prop_hanging_cured_hams` | PASS_SCALE | 0.8 | 0.8 | 1.0 | low | subject rule 'hams' |
| `prop_hanging_inn_sign` | PASS_SCALE | 2.4 | 2.4 | 1.0 | medium | subject rule 'sign' |
| `prop_hanging_iron_gibbet` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'gibbet' |
| `prop_headless_stone_statue` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'statue' |
| `prop_heap_of_raw_ore_chunks` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'ore_chunks' |
| `prop_herb_drying_hamper` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'hamper' |
| `prop_iron_banded_oak_door` | PASS_SCALE | 2.05 | 2.05 | 1.0 | high | asset override |
| `prop_iron_banded_ore_cart` | PASS_SCALE | 2.05 | 2.05 | 1.0 | high | subject rule 'iron banded' |
| `prop_iron_brazier` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'brazier' |
| `prop_iron_cauldron_tripod` | PASS_SCALE | 1.3 | 1.3 | 1.0 | medium | subject rule 'cauldron' |
| `prop_iron_coal_skip_basket` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'basket' |
| `prop_iron_five_branch_candelabra` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'candelabra' |
| `prop_iron_footed_brazier` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'brazier' |
| `prop_iron_lantern` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'lantern' |
| `prop_iron_shackles_chain` | PASS_SCALE | 0.8 | 0.8 | 1.0 | low | subject rule 'shackles' |
| `prop_iron_tipped_plough` | PASS_SCALE | 1.3 | 1.3 | 1.0 | medium | subject rule 'plough' |
| `prop_iron_toothed_harrow` | PASS_SCALE | 1.3 | 1.3 | 1.0 | medium | subject rule 'harrow' |
| `prop_iron_vein_outcrop` | PASS_SCALE | 1.2 | 1.2 | 1.0 | high | subject rule 'prop_iron_vein_outcrop' |
| `prop_ironbound_strongbox` | PASS_SCALE | 1.1 | 1.1 | 1.0 | medium | subject rule 'strongbox' |
| `prop_kick_potter_wheel` | PASS_SCALE | 1.3 | 1.3 | 1.0 | medium | subject rule 'potter_wheel' |
| `prop_ladder_back_chair` | PASS_SCALE | 1.1 | 1.1 | 1.0 | medium | subject rule 'chair' |
| `prop_leather_saddle_stand` | PASS_SCALE | 1.4 | 1.4 | 1.0 | medium | subject rule 'saddle_stand' |
| `prop_leather_travel_trunk` | PASS_SCALE | 1.1 | 1.1 | 1.0 | medium | subject rule 'trunk' |
| `prop_leather_waterskin_flask` | PASS_SCALE | 0.4 | 0.4 | 1.0 | high | subject rule 'flask' |
| `prop_long_trestle_table` | PASS_SCALE | 1.6 | 1.6 | 1.0 | medium | subject rule 'trestle_table' |
| `prop_market_stall_frame` | PASS_SCALE | 1.6 | 1.6 | 1.0 | medium | subject rule 'market_stall' |
| `prop_mead_barrel_cradle` | PASS_SCALE | 0.85 | 0.85 | 1.0 | high | subject rule 'barrel' |
| `prop_quarry_winch` | PASS_SCALE | 1.8 | 1.8 | 1.0 | high | subject rule 'prop_quarry_winch' |
| `prop_sacks_and_grain` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'sacks_and_grain' |
| `prop_stack_of_firewood` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'stack' |
| `prop_wooden_barrel` | PASS_SCALE | 0.85 | 0.85 | 1.0 | high | subject rule 'barrel' |
| `prop_wooden_bench` | PASS_SCALE | 1.4 | 1.4 | 1.0 | medium | subject rule 'bench' |
| `prop_wooden_cart_wheel` | PASS_SCALE | 1.3 | 1.3 | 1.0 | medium | subject rule 'cart_wheel' |
| `prop_wooden_crate` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'crate' |
| `race_constructed_representative` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'race_' |
| `race_kal_representative` | PASS_SCALE | 1.3 | 1.3 | 1.0 | high | subject rule 'kal' |
| `race_mor_representative` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'race_' |
| `race_orenth_representative` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'race_' |
| `race_siann_representative` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'race_' |
| `race_veth_representative` | PASS_SCALE | 1.8 | 1.8 | 1.0 | high | asset override |
| `race2_veth_bindpose` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'race2_' |
| `race2_veth_representative` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'race2_' |
| `racebody_constructed_pose` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'racebody_' |
| `racebody_kal_pose` | PASS_SCALE | 1.3 | 1.3 | 1.0 | high | asset override |
| `racebody_mor_pose` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'racebody_' |
| `racebody_orenth_pose` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'racebody_' |
| `racebody_siann_pose` | PASS_SCALE | 1.8 | 1.8 | 1.0 | high | asset override |
| `racebody_veth_pose` | PASS_SCALE | 1.8 | 1.8 | 1.0 | low | family default 'racebody_' |
| `resource_ash_haft` | PASS_SCALE | 1.9 | 1.9 | 1.0 | high | subject rule 'resource_ash_haft' |
| `resource_iron_billet` | PASS_SCALE | 0.45 | 0.45 | 1.0 | high | subject rule 'resource_iron_billet' |
| `resource_iron_ingot` | PASS_SCALE | 0.3 | 0.3 | 1.0 | high | subject rule 'ingot' |
| `resource_iron_ore` | PASS_SCALE | 0.2 | 0.2 | 1.0 | medium | subject rule 'ore' |
| `resource_woundmoss` | PASS_SCALE | 0.3 | 0.3 | 1.0 | high | subject rule 'resource_woundmoss' |
| `rock_boulder` | PASS_SCALE | 1.8 | 1.8 | 1.0 | high | subject rule 'boulder' |
| `rock_field_cluster` | PASS_SCALE | 0.6 | 0.6 | 1.0 | high | subject rule 'field_cluster' |
| `rock_outcrop_shelf` | PASS_SCALE | 4.0 | 4.0 | 1.0 | medium | subject rule 'outcrop' |
| `tool_blacksmith_hammer` | PASS_SCALE | 0.6 | 0.6 | 1.0 | high | subject rule 'hammer' |
| `tool_blacksmith_tongs` | PASS_SCALE | 0.6 | 0.6 | 1.0 | high | subject rule 'tongs' |
| `tool_iron_shovel` | PASS_SCALE | 0.6 | 0.6 | 1.0 | high | subject rule 'shovel' |
| `travel_bridge_rope` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'bridge' |
| `travel_bridge_stone_arch` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'bridge' |
| `travel_cairn_stone_pile` | PASS_SCALE | 1.0 | 1.0 | 1.0 | medium | subject rule 'cairn' |
| `travel_camp_bedroll` | PASS_SCALE | 2.2 | 2.2 | 1.0 | medium | subject rule 'camp' |
| `travel_camp_fire_tripod` | PASS_SCALE | 2.2 | 2.2 | 1.0 | medium | subject rule 'camp' |
| `travel_camp_tent_small` | PASS_SCALE | 2.2 | 2.2 | 1.0 | medium | subject rule 'camp' |
| `travel_climbing_pitons` | PASS_SCALE | 4.0 | 4.0 | 1.0 | medium | subject rule 'climb' |
| `travel_climbing_rope_coil` | PASS_SCALE | 4.0 | 4.0 | 1.0 | medium | subject rule 'rope' |
| `travel_crampons` | PASS_SCALE | 0.7 | 0.7 | 1.0 | high | subject rule 'crampon' |
| `travel_ferry_dock` | PASS_SCALE | 6.0 | 6.0 | 1.0 | medium | subject rule 'ferry_dock' |
| `travel_flying_mount_tack` | PASS_SCALE | 1.0 | 1.0 | 1.0 | low | subject rule 'mount_tack' |
| `travel_ford_marker` | PASS_SCALE | 1.0 | 1.0 | 1.0 | medium | subject rule 'ford_marker' |
| `travel_gate_lens` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'gate' |
| `travel_glider_canvas` | PASS_SCALE | 3.5 | 3.5 | 1.0 | low | subject rule 'glider_canvas' |
| `travel_grappling_hook` | PASS_SCALE | 0.7 | 0.7 | 1.0 | high | subject rule 'hook' |
| `travel_ice_axe` | PASS_SCALE | 0.7 | 0.7 | 1.0 | high | subject rule 'ice axe' |
| `travel_lift_wooden` | PASS_SCALE | 3.5 | 3.5 | 1.0 | low | subject rule 'lift_wooden' |
| `travel_othergate_ancient_arch` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'gate' |
| `travel_othergate_invisible_threshold` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'gate' |
| `travel_othergate_mechanical` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'gate' |
| `travel_othergate_mirrored_door` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'gate' |
| `travel_othergate_root_arch` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'gate' |
| `travel_othergate_standing_stone` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'gate' |
| `travel_othergate_well` | PASS_SCALE | 8.0 | 8.0 | 1.0 | medium | subject rule 'gate' |
| `travel_pack_panniers` | PASS_SCALE | 1.0 | 1.0 | 1.0 | low | subject rule 'panniers' |
| `travel_recall_anchor` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'recall_anchor' |
| `travel_rope_ladder` | PASS_SCALE | 4.0 | 4.0 | 1.0 | medium | subject rule 'ladder' |
| `travel_signpost_crossroads` | PASS_SCALE | 2.6 | 2.6 | 1.0 | high | subject rule 'signpost' |
| `travel_snowshoes` | PASS_SCALE | 1.0 | 1.0 | 1.0 | low | subject rule 'snowshoes' |
| `travel_stable_rack` | PASS_SCALE | 1.0 | 1.0 | 1.0 | low | subject rule 'stable_rack' |
| `travel_survey_tripod` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'survey_tripod' |
| `travel_toll_booth` | PASS_SCALE | 3.5 | 3.5 | 1.0 | low | subject rule 'toll_booth' |
| `travel_torch_bracket` | PASS_SCALE | 1.2 | 1.2 | 1.0 | low | subject rule 'torch_bracket' |
| `travel_transit_platform` | PASS_SCALE | 3.5 | 3.5 | 1.0 | low | subject rule 'transit_platform' |
| `travel_waystone_marker` | PASS_SCALE | 1.0 | 1.0 | 1.0 | medium | subject rule 'waystone' |
| `travel_wing_harness` | PASS_SCALE | 1.0 | 1.0 | 1.0 | low | subject rule 'wing_harness' |
| `weapon_arming_sword` | PASS_SCALE | 1.0 | 1.0 | 1.0 | high | subject rule 'arming sword' |
| `weapon_arming_sword_frontier` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'arming sword' |
| `weapon_bardiche_cleaver` | PASS_SCALE | 1.2 | 1.1 | 1.091 | high | subject rule 'bardiche' |
| `weapon_boar_spear_hunting` | PASS_SCALE | 2.2 | 2.2 | 1.0 | high | subject rule 'spear' |
| `weapon_broadsword_brass_hilt` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'broadsword' |
| `weapon_crystal_hilt_longsword` | PASS_SCALE | 1.2 | 1.2 | 1.0 | high | subject rule 'longsword' |
| `weapon_cutlass_salt_stained` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'cutlass' |
| `weapon_double_headed_war_axe` | PASS_SCALE | 1.2 | 1.1 | 1.091 | high | subject rule 'double headed' |
| `weapon_falchion_cleaver_blade` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'falchion' |
| `weapon_gilded_ceremonial_sabre` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'sabre' |
| `weapon_great_axe_timber_haft` | PASS_SCALE | 1.2 | 1.1 | 1.091 | high | subject rule 'great axe' |
| `weapon_hunting_belt_knife` | PASS_SCALE | 0.6 | 0.6 | 1.0 | high | subject rule 'hunting belt' |
| `weapon_hunting_bow` | PASS_SCALE | 1.7 | 1.7 | 1.0 | high | subject rule 'bow' |
| `weapon_longsword_bastard_blade` | PASS_SCALE | 1.2 | 1.2 | 1.0 | high | subject rule 'longsword' |
| `weapon_march_spear` | PASS_SCALE | 2.2 | 2.2 | 1.0 | high | subject rule 'spear' |
| `weapon_militia_crossbow_worn` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'militia' |
| `weapon_obsidian_edge_sword` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'obsidian edge' |
| `weapon_rapier_duelling_swept` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'rapier' |
| `weapon_recurve_hunting_bow` | PASS_SCALE | 1.7 | 1.7 | 1.0 | high | subject rule 'bow' |
| `weapon_repaired_haft_great_axe` | PASS_SCALE | 1.2 | 1.1 | 1.091 | high | subject rule 'great axe' |
| `weapon_rusted_militia_sword` | PASS_SCALE | 1.0 | 1.0 | 1.0 | high | subject rule 'militia' |
| `weapon_sabre_nomad_cavalry` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'sabre' |
| `weapon_scimitar_desert_steel` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'scimitar' |
| `weapon_silvered_bishop_mace` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'silvered' |
| `weapon_silvered_warden_sword` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'silvered' |
| `weapon_temple_guard_axe_crescent` | PASS_SCALE | 1.2 | 1.0 | 1.2 | high | subject rule 'temple guard' |
| `weapon_yew_longbow_warbow` | PASS_SCALE | 1.7 | 1.7 | 1.0 | high | subject rule 'longbow' |
| `animal_guard_mastiff` | SUSPECT_SCALE | 1.8 | 1.3 | 1.385 | high | subject rule 'mastiff' |
| `animal_herding_dog` | SUSPECT_SCALE | 1.8 | 1.3 | 1.385 | high | subject rule 'dog' |
| `animal_mouser_cat` | SUSPECT_SCALE | 1.8 | 0.75 | 2.4 | high | subject rule 'cat' |
| `animal_pack_goat` | SUSPECT_SCALE | 1.8 | 1.3 | 1.385 | high | subject rule 'goat' |
| `animal_tracking_hound` | SUSPECT_SCALE | 1.8 | 1.3 | 1.385 | high | subject rule 'hound' |
| `building_road_segment` | SUSPECT_SCALE | 4.0 | 3.0 | 1.333 | high | subject rule 'road_segment' |
| `creature_ancient_bog_tortoise` | SUSPECT_SCALE | 1.8 | 1.0 | 1.8 | medium | subject rule 'tortoise' |
| `creature_arctic_frost_wolf` | SUSPECT_SCALE | 1.8 | 1.3 | 1.385 | high | subject rule 'wolf' |
| `creature_armoured_thorn_lizard` | SUSPECT_SCALE | 1.8 | 1.0 | 1.8 | medium | subject rule 'lizard' |
| `creature_bronze_serpent_sentry` | SUSPECT_SCALE | 1.8 | 3.0 | 0.6 | medium | subject rule 'serpent' |
| `creature_cave_salamander` | SUSPECT_SCALE | 1.8 | 1.0 | 1.8 | medium | subject rule 'salamander' |
| `creature_drowned_hound` | SUSPECT_SCALE | 1.8 | 1.3 | 1.385 | high | subject rule 'hound' |
| `creature_dune_armoured_scorpion` | SUSPECT_SCALE | 1.8 | 1.2 | 1.5 | medium | subject rule 'scorpion' |
| `creature_dune_jackal_scout` | SUSPECT_SCALE | 1.8 | 0.9 | 2.0 | medium | subject rule 'jackal' |
| `creature_fey_thorn_cat` | SUSPECT_SCALE | 1.8 | 0.9 | 2.0 | medium | subject rule 'cat' |
| `creature_forest_marten_runner` | SUSPECT_SCALE | 1.8 | 0.9 | 2.0 | medium | subject rule 'marten' |
| `creature_forest_praying_mantis` | SUSPECT_SCALE | 1.8 | 1.2 | 1.5 | medium | subject rule 'mantis' |
| `creature_frost_wolf_side` | SUSPECT_SCALE | 1.8 | 1.3 | 1.385 | high | subject rule 'wolf' |
| `creature_grey_badger_warden` | SUSPECT_SCALE | 1.8 | 0.9 | 2.0 | medium | subject rule 'badger' |
| `creature_marsh_giant_wasp` | SUSPECT_SCALE | 1.8 | 1.2 | 1.5 | medium | subject rule 'wasp' |
| `creature_red_fox_scavenger` | SUSPECT_SCALE | 1.8 | 0.9 | 2.0 | medium | subject rule 'fox' |
| `creature_ridgeback_lizard` | SUSPECT_SCALE | 1.8 | 1.0 | 1.8 | medium | subject rule 'lizard' |
| `creature_sand_skink_runner` | SUSPECT_SCALE | 1.8 | 1.0 | 1.8 | medium | subject rule 'skink' |
| `creature_skeleton_hound` | SUSPECT_SCALE | 1.8 | 1.3 | 1.385 | high | subject rule 'hound' |
| `creature_slime_marsh_frog` | SUSPECT_SCALE | 1.8 | 1.0 | 1.8 | medium | subject rule 'frog' |
| `creature_small_ember_drake` | SUSPECT_SCALE | 1.8 | 2.5 | 0.72 | low | subject rule 'drake' |
| `creature_snow_lynx_stalker` | SUSPECT_SCALE | 1.8 | 0.9 | 2.0 | medium | subject rule 'lynx' |
| `creature_spotted_hyena_brute` | SUSPECT_SCALE | 1.8 | 0.9 | 2.0 | medium | subject rule 'hyena' |
| `herb_bitterroot` | SUSPECT_SCALE | 0.5 | 0.25 | 2.0 | low | family default 'herb_' |
| `item_bog_iron_bloom` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | high | subject rule 'bloom' |
| `item_coldbloom_shard` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | high | subject rule 'bloom' |
| `item_lead_ingot` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | high | subject rule 'ingot' |
| `item_mana_potion` | SUSPECT_SCALE | 0.5 | 0.22 | 2.273 | high | subject rule 'potion' |
| `item_othersteel_ingot` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | high | subject rule 'ingot' |
| `item_silver_ingot` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | high | subject rule 'ingot' |
| `item_veiliron_ingot` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | high | subject rule 'ingot' |
| `item_veilwater_flask` | SUSPECT_SCALE | 0.5 | 0.22 | 2.273 | high | subject rule 'flask' |
| `magic_crystal_shard_focus` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | medium | subject rule 'crystal' |
| `magic_focus_crystal_cluster` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | medium | subject rule 'crystal' |
| `magic_inscription_scroll` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | high | subject rule 'scroll' |
| `magic_lens_crystal` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | medium | subject rule 'crystal' |
| `magic_runestone_pillar` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | high | subject rule 'rune' |
| `magic_staff_ironbound` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `magic_staff_oaken` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `npc_ondrek_stonecarver` | SUSPECT_SCALE | 1.8 | 2.6 | 0.692 | high | subject rule 'ondrek' |
| `npc_vaskaal_envoy` | SUSPECT_SCALE | 1.8 | 2.35 | 0.766 | high | subject rule 'vaskaal' |
| `prop_quarry_rail_track` | SUSPECT_SCALE | 3.0 | 1.2 | 2.5 | low | subject rule 'rail' |
| `race_ondrek_representative` | SUSPECT_SCALE | 1.8 | 2.6 | 0.692 | high | subject rule 'ondrek' |
| `race_vaskaal_representative` | SUSPECT_SCALE | 1.8 | 2.35 | 0.766 | high | subject rule 'vaskaal' |
| `racebody_ondrek_pose` | SUSPECT_SCALE | 1.8 | 2.6 | 0.692 | high | asset override |
| `racebody_vaskaal_pose` | SUSPECT_SCALE | 1.8 | 2.35 | 0.766 | high | asset override |
| `reagent_ceramic_alchemy_jar` | SUSPECT_SCALE | 0.5 | 0.25 | 2.0 | low | family default 'reagent_' |
| `reagent_glass_apothecary_jar` | SUSPECT_SCALE | 0.5 | 0.25 | 2.0 | low | family default 'reagent_' |
| `reagent_herb_drying_rack` | SUSPECT_SCALE | 0.5 | 0.25 | 2.0 | low | family default 'reagent_' |
| `reagent_woundmoss_bundle` | SUSPECT_SCALE | 0.5 | 0.25 | 2.0 | low | family default 'reagent_' |
| `resource_arcane_essence_vial` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | low | family default 'resource_' |
| `resource_cured_leather_roll` | SUSPECT_SCALE | 0.5 | 1.2 | 0.417 | low | subject rule 'leather' |
| `resource_dragon_bone_shard` | SUSPECT_SCALE | 0.5 | 0.2 | 2.5 | medium | subject rule 'shard' |
| `resource_gold_ore_nugget` | SUSPECT_SCALE | 0.5 | 0.2 | 2.5 | medium | subject rule 'ore' |
| `resource_healing_herb_leaf` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | low | family default 'resource_' |
| `resource_herb_leaf` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | low | family default 'resource_' |
| `resource_iron_ore_chunk` | SUSPECT_SCALE | 0.5 | 0.2 | 2.5 | medium | subject rule 'ore' |
| `resource_moonpetal_flower` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | low | family default 'resource_' |
| `resource_oak_wood` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | low | family default 'resource_' |
| `resource_raw_hide` | SUSPECT_SCALE | 0.5 | 1.2 | 0.417 | low | subject rule 'hide' |
| `resource_rawhide_pelt_fold` | SUSPECT_SCALE | 0.5 | 1.2 | 0.417 | low | subject rule 'hide' |
| `resource_ruby_gem_crystal` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | low | family default 'resource_' |
| `resource_silk_thread_spool` | SUSPECT_SCALE | 0.5 | 0.35 | 1.429 | low | family default 'resource_' |
| `resource_steel_ingot_bar` | SUSPECT_SCALE | 0.5 | 0.3 | 1.667 | high | subject rule 'ingot' |
| `tool_hand_saw` | SUSPECT_SCALE | 0.6 | 0.9 | 0.667 | medium | subject rule 'saw' |
| `tool_mining_pick` | SUSPECT_SCALE | 0.9 | 0.6 | 1.5 | high | subject rule 'pick' |
| `weapon_apprentice_staff` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `weapon_barrow_axe_corroded` | SUSPECT_SCALE | 1.2 | 0.6 | 2.0 | high | subject rule 'barrow axe' |
| `weapon_battle_staff_metal` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `weapon_belt_hatchet_small` | SUSPECT_SCALE | 1.2 | 0.6 | 2.0 | high | subject rule 'hatchet' |
| `weapon_bill_hook_peasant` | SUSPECT_SCALE | 1.2 | 2.2 | 0.545 | high | subject rule 'bill hook' |
| `weapon_boarding_axe_iron` | SUSPECT_SCALE | 1.2 | 0.6 | 2.0 | high | subject rule 'boarding axe' |
| `weapon_bone_haft_greatsword` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_bone_laminated_bow` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'bow' |
| `weapon_bone_wizard_staff` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `weapon_bronze_ceremonial_mace` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'mace' |
| `weapon_claymore_highland` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'claymore' |
| `weapon_composite_horse_bow` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'bow' |
| `weapon_corseque_serpent_blade` | SUSPECT_SCALE | 1.2 | 2.2 | 0.545 | high | subject rule 'corseque' |
| `weapon_crystal_focus_staff` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `weapon_crystal_headed_mace` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'mace' |
| `weapon_druidic_living_staff` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `weapon_dwarven_axe_engraved` | SUSPECT_SCALE | 1.2 | 0.6 | 2.0 | high | subject rule 'dwarven axe' |
| `weapon_elven_glaive_hooked` | SUSPECT_SCALE | 1.2 | 2.2 | 0.545 | high | subject rule 'glaive' |
| `weapon_elven_recurve_bow` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'bow' |
| `weapon_executioner_sword` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'executioner' |
| `weapon_flanged_mace_steel` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'mace' |
| `weapon_frost_crystal_staff` | SUSPECT_SCALE | 1.2 | 0.6 | 2.0 | high | subject rule 'frost' |
| `weapon_frost_iron_greatsword` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_frost_pike_crystal` | SUSPECT_SCALE | 1.2 | 0.6 | 2.0 | high | subject rule 'frost' |
| `weapon_gladius_bronze_legion` | SUSPECT_SCALE | 1.2 | 0.7 | 1.714 | high | subject rule 'gladius' |
| `weapon_gnarled_oak_staff` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `weapon_greatsword_flamberge` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_halberd_guardsman` | SUSPECT_SCALE | 1.2 | 2.2 | 0.545 | high | subject rule 'halberd' |
| `weapon_heavy_arbalest_crank` | SUSPECT_SCALE | 1.2 | 0.8 | 1.5 | high | subject rule 'arbalest' |
| `weapon_horsemans_axe_nomad` | SUSPECT_SCALE | 1.2 | 0.6 | 2.0 | high | subject rule 'horsemans axe' |
| `weapon_horsemans_pick_hammer` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'horsemans pick' |
| `weapon_hybrid_focus_staff_spear_a` | SUSPECT_SCALE | 1.0017 | 1.8 | 0.556 | high | subject rule 'staff' |
| `weapon_iron_shod_club` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'club' |
| `weapon_iron_shod_quarterstaff` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `weapon_katzbalger_mercenary` | SUSPECT_SCALE | 1.2 | 0.7 | 1.714 | high | subject rule 'katzbalger' |
| `weapon_knobbed_club_wood` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'club' |
| `weapon_light_crossbow_hunter` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'bow' |
| `weapon_lucerne_hammer_polearm` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'lucerne' |
| `weapon_nomad_spiked_mace` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'mace' |
| `weapon_notched_greatsword_merc` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_ornate_ceremonial_bow` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'bow' |
| `weapon_ornate_greatsword_gilded` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_partisan_ornate` | SUSPECT_SCALE | 1.2 | 2.2 | 0.545 | high | subject rule 'partisan' |
| `weapon_priest_mace_corroded` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'mace' |
| `weapon_rune_greatsword_blade` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_spiked_club_swamp` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'club' |
| `weapon_starmetal_greatsword` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_studded_cudgel_iron` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'cudgel' |
| `weapon_swamp_root_staff` | SUSPECT_SCALE | 1.2 | 1.8 | 0.667 | high | subject rule 'staff' |
| `weapon_trident_gladiator` | SUSPECT_SCALE | 1.2 | 2.2 | 0.545 | high | subject rule 'trident' |
| `weapon_two_handed_greatsword` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_volcanic_glass_greatsword` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'greatsword' |
| `weapon_voulge_heavy_blade` | SUSPECT_SCALE | 1.2 | 2.2 | 0.545 | high | subject rule 'voulge' |
| `weapon_warhammer_pick_back` | SUSPECT_SCALE | 1.2 | 0.75 | 1.6 | high | subject rule 'warhammer' |
| `weapon_woodcutter_hatchet` | SUSPECT_SCALE | 1.2 | 0.6 | 2.0 | high | subject rule 'hatchet' |
| `weapon_zweihander_parry_hooks` | SUSPECT_SCALE | 1.2 | 1.7 | 0.706 | high | subject rule 'zweihander' |
| `armour_kal_back_channel_a` | UNKNOWN_SCALE | 0.7857 | - | - | - | no expectation declared |
| `forge_shed` | UNKNOWN_SCALE | 6.318 | - | - | - | no expectation declared |
| `item_amber_chunk` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_amethyst_geode` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_bloodstone_chunk` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_emerald_crystal` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_firstfold_iron_fragment` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_garnet_crystal` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_jet_stone` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_moonstone_cabochon` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_nullstone` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_otherfire_lamp` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_othergate_lens_frame` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_otherglass_lens` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_otherglass_shard` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_pachakuti_residue_pile` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_raw_copper_ore` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_raw_gold_ore` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_raw_iron_ore` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_raw_lead_ore` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_raw_otherstone` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_raw_silver_ore` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_raw_tin_ore` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_resonance_quartz` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_ruby_crystal` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_sapphire_crystal` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_silence_shard` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_thinshard_pouch` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_tideglass_pane` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_tidepearl` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_topaz_crystal` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `item_torch` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `longhouse` | UNKNOWN_SCALE | 9.0 | - | - | - | no expectation declared |
| `magic_astrolabe_brass` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_athame_dagger` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `magic_bell_ritual` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_compass_arcane` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_gauntlet_channel` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_hourglass_brass` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_inkwell_brass` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_mortar_pestle` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_orb_scrying` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_pendulum_bob` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_phial_glass` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_scales_balance` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_scepter_regal` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_sigil_plate` | UNKNOWN_SCALE | 0.3 | - | - | - | no expectation declared |
| `magic_skull_ritual` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_totem_carved` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_veilglass_pane` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_wand_ash` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magic_wand_blackthorn` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `magiccomp_focus_crystal_a` | UNKNOWN_SCALE | 0.4605 | - | - | - | no expectation declared |
| `vehicle_river_barge` | UNKNOWN_SCALE | 0.5 | - | - | - | no expectation declared |
| `weapon_bone_handled_axe_swamp` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_bone_handled_skinner` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_bronze_temple_axe` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_crystal_bladed_axe` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_estoc_thrusting_blade` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_iron_war_axe` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_obsidian_ritual_knife` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_ritual_moon_axe` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_two_handed_maul` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weapon_viking_rune_axe` | UNKNOWN_SCALE | 1.2 | - | - | - | no expectation declared |
| `weaponcomp_blade_arming_sword_a` | UNKNOWN_SCALE | 0.7126 | - | - | - | no expectation declared |
| `weaponcomp_grip_standard_a` | UNKNOWN_SCALE | 0.8804 | - | - | - | no expectation declared |
| `weaponcomp_grip_vaskaal_a` | UNKNOWN_SCALE | 0.4225 | - | - | - | no expectation declared |
| `weaponcomp_haft_long_a` | UNKNOWN_SCALE | 0.7986 | - | - | - | no expectation declared |
| `weaponcomp_haft_short_a` | UNKNOWN_SCALE | 0.0703 | - | - | - | no expectation declared |
| `weaponcomp_mace_head_flanged_a` | UNKNOWN_SCALE | 0.3785 | - | - | - | no expectation declared |
| `weaponcomp_mace_head_flanged_a_stub2` | UNKNOWN_SCALE | 0.3 | - | - | - | no expectation declared |
| `weaponcomp_mechanism_telescope_a` | UNKNOWN_SCALE | 0.8235 | - | - | - | no expectation declared |
| `weaponcomp_pommel_counterweight_a` | UNKNOWN_SCALE | 0.6413 | - | - | - | no expectation declared |
| `weaponcomp_shield_heater_a` | UNKNOWN_SCALE | 0.9799 | - | - | - | no expectation declared |
