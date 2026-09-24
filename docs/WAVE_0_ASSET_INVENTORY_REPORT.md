# Wave 0 asset inventory report

Generated 2026-09-23 13:27 from `assets/catalog.json` (catalog built 2026-09-23 13:27:16).

Grouped by **asset-id prefix**, which is the authoritative family. The catalog groups by
inferred 3D sizing category instead, which mis-files things like `travel_ice_axe` under
weapons; that grouping is for choosing target dimensions, not for inventory.

## 1. Status

- **Assets built: 455** (16,348,196 triangles, 4.24 GB of base GLB)
- **Concepts rendered: 750** (9 of them pipeline experiments, not deliverables — see the note below)
- **Rigged: 82**
- **Proof set: 15/15**
- **Concepts rendered but not yet built: 162 3D**, 124 2D (2D needs no build)

Excluded from the outstanding-work counts: methodA_chestplate_ghost, methodA_gorget_ghost, methodB_chestplate_gap, methodB_gorget_gap, pommel_var_a_smooth_pear, pommel_var_b_round_ball, pommel_var_c_faceted_drop, test_gorget_with_shoulders, test_mace_head_with_stub. These were
rendered to test the sacrificial-stub convention, compare the armour fit methods, or
choose between pommel candidates. They select pipeline options rather than ship.

### Build state per family

| Family | Built | Still to build |
|---|---:|---:|
| Weapons (`weapon_`) | 106 | 1 |
| Props (`prop_`) | 65 | 45 |
| Creatures (`creature_`) | 62 | 0 |
| Items (`item_`) | 45 | 33 |
| Magic and ritual props (`magic_`) | 40 | 0 |
| Travel and traversal (`travel_`) | 36 | 0 |
| Resources (`resource_`) | 18 | 1 |
| Flora (`flora_`) | 14 | 0 |
| Weapon components (modular) (`weaponcomp_`) | 10 | 0 |
| Races (`race_`) | 8 | 0 |
| NPCs (`npc_`) | 8 | 0 |
| Containers (`container_`) | 8 | 12 |
| Animals (`animal_`) | 7 | 1 |
| Race bodies (canonical) (`racebody_`) | 7 | 1 |
| Vehicles (`vehicle_`) | 6 | 0 |
| Tools (`tool_`) | 5 | 0 |
| Armour (`armour_`) | 4 | 0 |
| Reagents (`reagent_`) | 4 | 0 |
| Magic components (modular) (`magiccomp_`) | 1 | 0 |
| Herbs (`herb_`) | 1 | 16 |
| Race classes (`raceclass_`) | 0 | 32 |
| World materials (2D) (`material_`) | 0 | 60 — 2D, no build |
| Races (wave 2 representatives) (`race2_`) | 0 | 14 |
| UI icons (2D) (`icon_`) | 0 | 64 — 2D, no build |
| Mounts (`mount_`) | 0 | 6 |

## 2. Every asset, by family

`R` marks a rigged export in `assets/rigged/`; `P` marks membership in the 15-asset proof
set. Dimensions are the measured bounding box of the base mesh.

### Weapons — 106

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `weapon_apprentice_staff` | 24,966 | 0.234 x 0.182 x 1.200 | 8000/2500/600 | - |
| `weapon_arming_sword` | 24,958 | 0.271 x 0.360 x 1.200 | 7999/2500/600 | - |
| `weapon_arming_sword_frontier` | 24,550 | 1.128 x 0.303 x 1.200 | 7999/2500/600 | - |
| `weapon_ash_wand_copper` | 24,668 | 0.104 x 0.285 x 1.200 | 8000/2500/600 | - |
| `weapon_bardiche_cleaver` | 24,970 | 0.226 x 0.335 x 1.200 | 8000/2500/600 | - |
| `weapon_barrow_axe_corroded` | 39,804 | 1.084 x 0.288 x 1.200 | 11999/4000/1000 | - |
| `weapon_battle_staff_metal` | 39,986 | 0.316 x 0.080 x 1.200 | 12000/4000/1000 | - |
| `weapon_belt_hatchet_small` | 39,601 | 0.861 x 0.422 x 1.200 | 12000/4000/999 | - |
| `weapon_bill_hook_peasant` | 39,811 | 0.810 x 0.252 x 1.200 | 11999/4000/1000 | - |
| `weapon_boar_spear_hunting` | 39,979 | 0.151 x 0.111 x 1.200 | 11999/4000/1000 | - |
| `weapon_boarding_axe_iron` | 39,896 | 0.634 x 0.306 x 1.200 | 12000/4000/1000 | - |
| `weapon_bollock_dagger` | 39,858 | 1.095 x 0.260 x 1.200 | 11999/4000/1000 | - |
| `weapon_bone_haft_greatsword` | 39,806 | 0.362 x 0.399 x 1.200 | 12000/4000/1000 | - |
| `weapon_bone_handled_axe_swamp` | 39,884 | 0.829 x 0.245 x 1.200 | 12000/4000/1000 | - |
| `weapon_bone_handled_skinner` | 39,856 | 1.052 x 0.259 x 1.200 | 12000/4000/999 | - |
| `weapon_bone_laminated_bow` | 39,970 | 0.275 x 0.363 x 1.200 | 12000/4000/1000 | - |
| `weapon_bone_wizard_staff` | 39,968 | 0.258 x 0.362 x 1.200 | 12000/3999/1000 | - |
| `weapon_broadsword_brass_hilt` | 39,108 | 1.172 x 0.170 x 1.200 | 12000/4000/999 | - |
| `weapon_bronze_ceremonial_mace` | 39,836 | 0.515 x 0.227 x 1.200 | 11999/4000/1000 | - |
| `weapon_bronze_temple_axe` | 39,862 | 1.030 x 0.333 x 1.200 | 11999/4000/1000 | - |
| `weapon_chitin_dagger_swamp` | 38,903 | 1.044 x 0.369 x 1.200 | 12000/3999/1000 | - |
| `weapon_claymore_highland` | 39,166 | 1.180 x 0.238 x 1.200 | 12000/4000/1000 | - |
| `weapon_composite_horse_bow` | 39,952 | 0.641 x 0.365 x 1.200 | 12000/4000/1000 | - |
| `weapon_corseque_serpent_blade` | 39,916 | 0.304 x 0.162 x 1.200 | 12000/4000/1000 | - |
| `weapon_couched_lance_knight` | 39,857 | 1.198 x 0.249 x 1.200 | 12000/4000/999 | - |
| `weapon_crystal_bladed_axe` | 39,354 | 0.963 x 0.337 x 1.200 | 11999/4000/1000 | - |
| `weapon_crystal_focus_staff` | 39,844 | 0.225 x 0.374 x 1.200 | 12000/4000/1000 | - |
| `weapon_crystal_headed_mace` | 39,853 | 0.309 x 0.433 x 1.200 | 12000/4000/1000 | - |
| `weapon_crystal_hilt_longsword` | 39,978 | 0.267 x 0.284 x 1.200 | 11999/4000/1000 | - |
| `weapon_crystal_shard_dagger` | 39,642 | 0.593 x 0.414 x 1.200 | 12000/3999/999 | - |
| `weapon_cutlass_salt_stained` | 24,881 | 1.195 x 0.358 x 1.200 | 8000/2500/600 | - |
| `weapon_double_headed_war_axe` | 39,491 | 0.608 x 0.355 x 1.200 | 12000/3999/1000 | - |
| `weapon_druidic_living_staff` | 39,592 | 0.385 x 0.300 x 1.200 | 12000/4000/1000 | - |
| `weapon_dwarven_axe_engraved` | 39,754 | 0.536 x 0.319 x 1.200 | 12000/3999/998 | - |
| `weapon_elven_glaive_hooked` | 39,676 | 1.149 x 0.203 x 1.200 | 12000/3999/1000 | - |
| `weapon_elven_recurve_bow` | 39,905 | 0.552 x 0.287 x 1.200 | 12000/3999/999 | - |
| `weapon_ember_wood_wand` | 39,493 | 1.167 x 0.366 x 1.200 | 12000/4000/1000 | - |
| `weapon_estoc_thrusting_blade` | 39,833 | 1.174 x 0.284 x 1.200 | 12000/4000/999 | - |
| `weapon_executioner_sword` | 39,860 | 1.119 x 0.323 x 1.200 | 11999/4000/1000 | - |
| `weapon_falchion_cleaver_blade` | 39,946 | 1.065 x 0.326 x 1.200 | 12000/4000/1000 | - |
| `weapon_flanged_mace_steel` | 39,866 | 0.511 x 0.314 x 1.200 | 12000/4000/1000 | - |
| `weapon_frost_crystal_staff` | 39,693 | 0.267 x 0.162 x 1.200 | 12000/4000/1000 | - |
| `weapon_frost_iron_greatsword` | 39,936 | 0.371 x 0.344 x 1.200 | 12000/4000/1000 | - |
| `weapon_frost_pike_crystal` | 39,379 | 0.780 x 0.341 x 1.200 | 11999/4000/1000 | - |
| `weapon_gilded_ceremonial_sabre` | 39,746 | 1.081 x 0.329 x 1.200 | 12000/4000/1000 | - |
| `weapon_gladius_bronze_legion` | 39,802 | 1.112 x 0.294 x 1.200 | 12000/3999/1000 | - |
| `weapon_gnarled_oak_staff` | 39,821 | 0.145 x 0.234 x 1.200 | 12000/4000/999 | - |
| `weapon_great_axe_timber_haft` | 39,362 | 0.361 x 0.324 x 1.200 | 12000/4000/1000 | - |
| `weapon_greatsword_flamberge` | 39,872 | 1.163 x 0.303 x 1.200 | 12000/4000/1000 | - |
| `weapon_halberd_guardsman` | 39,956 | 0.313 x 0.278 x 1.200 | 12000/4000/1000 | - |
| `weapon_heavy_arbalest_crank` | 39,488 | 1.200 x 0.341 x 1.092 | 12000/4000/1000 | - |
| `weapon_horsemans_axe_nomad` | 39,806 | 0.539 x 0.262 x 1.200 | 12000/4000/1000 | - |
| `weapon_horsemans_pick_hammer` | 39,988 | 0.549 x 0.309 x 1.200 | 12000/4000/1000 | - |
| `weapon_hunting_belt_knife` | 39,483 | 1.200 x 0.288 x 1.005 | 11999/3999/999 | - |
| `weapon_hybrid_focus_staff_spear_a` | 39,948 | 0.510 x 0.070 x 1.002 | 8000/2500/600 | P |
| `weapon_infantry_pike_long` | 39,998 | 0.054 x 0.344 x 1.200 | 11999/4000/999 | - |
| `weapon_iron_shod_club` | 39,996 | 0.698 x 0.294 x 1.200 | 12000/4000/1000 | - |
| `weapon_iron_shod_quarterstaff` | 39,998 | 0.215 x 0.097 x 1.200 | 11999/4000/1000 | - |
| `weapon_iron_war_axe` | 24,990 | 0.486 x 0.272 x 1.200 | 8000/2499/599 | - |
| `weapon_katzbalger_mercenary` | 39,765 | 1.008 x 0.397 x 1.200 | 12000/4000/1000 | - |
| `weapon_knobbed_club_wood` | 39,986 | 0.527 x 0.510 x 1.200 | 12000/4000/1000 | - |
| `weapon_kris_wavy_dagger` | 39,862 | 0.290 x 0.184 x 1.200 | 12000/4000/1000 | - |
| `weapon_light_crossbow_hunter` | 38,976 | 1.200 x 0.947 x 0.965 | 12000/4000/1000 | - |
| `weapon_longsword_bastard_blade` | 39,680 | 0.286 x 0.351 x 1.200 | 11999/4000/1000 | - |
| `weapon_lucerne_hammer_polearm` | 39,952 | 0.536 x 0.166 x 1.200 | 12000/4000/999 | - |
| `weapon_main_gauche_parry` | 39,960 | 0.974 x 0.332 x 1.200 | 12000/4000/1000 | - |
| `weapon_militia_crossbow_worn` | 39,800 | 1.115 x 0.965 x 1.200 | 12000/4000/1000 | - |
| `weapon_nomad_spiked_mace` | 39,735 | 0.442 x 0.413 x 1.200 | 12000/4000/1000 | - |
| `weapon_notched_greatsword_merc` | 39,962 | 0.236 x 0.088 x 1.200 | 12000/4000/1000 | - |
| `weapon_obsidian_edge_sword` | 38,895 | 1.047 x 0.341 x 1.200 | 12000/4000/1000 | - |
| `weapon_obsidian_ritual_knife` | 39,839 | 1.173 x 0.364 x 1.200 | 12000/4000/1000 | - |
| `weapon_obsidian_tipped_wand` | 39,814 | 0.953 x 0.260 x 1.200 | 12000/4000/1000 | - |
| `weapon_ornate_ceremonial_bow` | 39,994 | 0.175 x 0.307 x 1.200 | 12000/4000/1000 | - |
| `weapon_ornate_greatsword_gilded` | 39,886 | 0.319 x 0.240 x 1.200 | 12000/4000/1000 | - |
| `weapon_parrying_dirk_brass` | 39,761 | 1.177 x 0.368 x 1.200 | 11999/4000/1000 | - |
| `weapon_partisan_ornate` | 39,464 | 0.815 x 0.288 x 1.200 | 11999/4000/1000 | - |
| `weapon_priest_mace_corroded` | 39,221 | 0.404 x 0.430 x 1.200 | 12000/3999/999 | - |
| `weapon_rapier_duelling_swept` | 39,876 | 1.157 x 0.328 x 1.200 | 12000/4000/1000 | - |
| `weapon_recurve_hunting_bow` | 39,552 | 0.969 x 0.259 x 1.200 | 12000/4000/1000 | - |
| `weapon_repaired_haft_great_axe` | 39,965 | 0.540 x 0.272 x 1.200 | 12000/4000/1000 | - |
| `weapon_ritual_moon_axe` | 39,866 | 0.471 x 0.323 x 1.200 | 12000/4000/1000 | - |
| `weapon_rune_greatsword_blade` | 39,960 | 0.326 x 0.313 x 1.200 | 11999/4000/1000 | - |
| `weapon_rusted_militia_sword` | 39,928 | 0.309 x 0.320 x 1.200 | 12000/4000/1000 | - |
| `weapon_sabre_nomad_cavalry` | 39,925 | 1.149 x 0.383 x 1.200 | 11999/4000/1000 | - |
| `weapon_scimitar_desert_steel` | 39,952 | 1.052 x 0.391 x 1.200 | 12000/4000/1000 | - |
| `weapon_seax_short_blade` | 39,844 | 1.189 x 0.373 x 1.200 | 12000/4000/1000 | - |
| `weapon_serrated_assassin_dagger` | 39,936 | 1.164 x 0.317 x 1.200 | 12000/4000/1000 | - |
| `weapon_silvered_bishop_mace` | 39,206 | 0.386 x 0.347 x 1.200 | 12000/4000/1000 | - |
| `weapon_silvered_warden_sword` | 39,937 | 0.318 x 0.391 x 1.200 | 12000/3999/999 | - |
| `weapon_spiked_club_swamp` | 38,634 | 0.122 x 0.427 x 1.200 | 12000/4000/1000 | - |
| `weapon_starmetal_greatsword` | 39,828 | 1.144 x 0.320 x 1.200 | 12000/4000/1000 | - |
| `weapon_stiletto_thrusting` | 39,946 | 0.270 x 0.338 x 1.200 | 12000/4000/1000 | - |
| `weapon_studded_cudgel_iron` | 39,952 | 0.296 x 0.281 x 1.200 | 12000/4000/1000 | - |
| `weapon_swamp_root_staff` | 39,966 | 0.299 x 0.325 x 1.200 | 11999/4000/1000 | - |
| `weapon_temple_guard_axe_crescent` | 39,732 | 0.514 x 0.142 x 1.200 | 12000/4000/1000 | - |
| `weapon_throwing_knife_balanced` | 39,725 | 1.169 x 0.287 x 1.200 | 11999/4000/999 | - |
| `weapon_trident_gladiator` | 39,962 | 0.222 x 0.299 x 1.200 | 12000/4000/1000 | - |
| `weapon_two_handed_greatsword` | 24,968 | 0.274 x 0.270 x 1.200 | 8000/2500/600 | - |
| `weapon_two_handed_maul` | 39,724 | 0.535 x 0.451 x 1.200 | 12000/3999/999 | - |
| `weapon_viking_rune_axe` | 24,502 | 1.200 x 0.266 x 1.163 | 8000/2500/827 | - |
| `weapon_volcanic_glass_greatsword` | 39,296 | 0.754 x 0.352 x 1.200 | 12000/4000/1000 | - |
| `weapon_voulge_heavy_blade` | 39,552 | 1.200 x 0.340 x 1.187 | 12000/4000/1000 | - |
| `weapon_warhammer_pick_back` | 39,935 | 0.463 x 0.372 x 1.200 | 12000/4000/1000 | - |
| `weapon_woodcutter_hatchet` | 25,000 | 0.481 x 0.335 x 1.200 | 8000/2500/600 | - |
| `weapon_yew_longbow_warbow` | 39,901 | 0.251 x 0.402 x 1.200 | 12000/4000/1000 | - |
| `weapon_zweihander_parry_hooks` | 39,886 | 1.198 x 0.285 x 1.200 | 12000/4000/1000 | - |

### Props — 65

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `prop_apothecary_jar_rack` | 24,857 | 0.500 x 0.406 x 0.391 | 8000/2499/646 | - |
| `prop_banded_ash_sea_chest` | 22,691 | 0.464 x 0.500 x 0.330 | 7999/2500/1242 | - |
| `prop_banded_writing_desk` | 23,123 | 0.500 x 0.358 x 0.424 | 8000/2500/1292 | - |
| `prop_blacksmith_anvil_stump` | 24,634 | 0.500 x 0.465 x 0.471 | 8000/2500/600 | - |
| `prop_brass_balance_scales` | 24,787 | 0.464 x 0.200 x 0.500 | 8000/2500/600 | - |
| `prop_brass_hand_bell` | 23,921 | 0.240 x 0.240 x 0.500 | 8000/2500/600 | - |
| `prop_brass_processional_lantern` | 24,257 | 0.168 x 0.171 x 0.500 | 8000/2500/600 | - |
| `prop_brass_tinder_box` | 24,849 | 0.448 x 0.357 x 0.500 | 7999/2500/681 | - |
| `prop_broken_masonry_pile` | 20,638 | 0.500 x 0.488 x 0.453 | 8000/2499/1870 | - |
| `prop_bundle_of_grain_sacks` | 25,340 | 0.415 x 0.376 x 0.500 | 8000/2500/1359 | - |
| `prop_bundled_firewood_stack` | 19,955 | 0.442 x 0.500 x 0.470 | 8000/2500/1300 | - |
| `prop_butter_churn_with_dasher` | 24,782 | 0.195 x 0.264 x 0.500 | 8000/2499/600 | - |
| `prop_campfire_tripod` | 24,960 | 0.280 x 0.236 x 0.500 | 8000/2500/599 | - |
| `prop_canvas_haversack` | 19,009 | 0.371 x 0.387 x 0.500 | 8000/2500/1492 | - |
| `prop_canvas_rolled_bedroll` | 21,667 | 0.500 x 0.497 x 0.389 | 8000/2500/1236 | - |
| `prop_carpenters_tool_chest` | 39,330 | 0.568 x 0.550 x 0.600 | 12000/4000/1000 | - |
| `prop_carpenters_workbench` | 24,875 | 0.491 x 0.500 x 0.447 | 8000/2500/600 | - |
| `prop_carved_oak_armchair` | 24,883 | 0.345 x 0.370 x 0.500 | 8000/2500/600 | - |
| `prop_carved_stone_sarcophagus` | 39,735 | 0.481 x 0.500 x 0.415 | 12000/4000/1000 | - |
| `prop_carved_stone_trough` | 39,994 | 0.438 x 0.500 x 0.413 | 12000/4000/999 | - |
| `prop_clay_amphora_vessel` | 39,996 | 0.236 x 0.234 x 0.500 | 12000/4000/1000 | - |
| `prop_clay_oil_lamp_with_handle` | 40,000 | 0.475 x 0.491 x 0.500 | 12000/4000/1000 | - |
| `prop_clay_water_jug` | 23,939 | 0.397 x 0.294 x 0.500 | 8000/2500/1666 | - |
| `prop_cobblers_stitching_bench` | 39,126 | 0.473 x 0.500 x 0.468 | 12000/4000/1000 | - |
| `prop_coiled_straw_skep` | 33,381 | 0.466 x 0.500 x 0.410 | 12000/4000/2814 | - |
| `prop_collapsed_stone_arch` | 39,972 | 0.500 x 0.483 x 0.374 | 12000/4000/1000 | - |
| `prop_coopered_dye_vat_paddle` | 39,435 | 0.398 x 0.500 x 0.390 | 12000/4000/1000 | - |
| `prop_copper_water_bucket` | 39,281 | 0.393 x 0.500 x 0.500 | 12000/3999/1000 | - |
| `prop_cracked_stone_urn` | 39,766 | 0.332 x 0.293 x 0.500 | 12000/4000/1000 | - |
| `prop_dwarven_forge_lantern` | 39,894 | 0.270 x 0.246 x 0.500 | 12000/4000/1000 | - |
| `prop_forge_double_bellows` | 39,382 | 0.378 x 0.178 x 0.500 | 12000/4000/1000 | - |
| `prop_garden_wheelbarrow` | 38,764 | 0.500 x 0.471 x 0.414 | 12000/3999/999 | - |
| `prop_grain_cradle_scythe` | 38,020 | 0.354 x 0.317 x 0.500 | 11999/4000/1462 | - |
| `prop_hanging_cured_hams` | 39,871 | 0.494 x 0.252 x 0.500 | 12000/4000/1000 | - |
| `prop_hanging_inn_sign` | 39,874 | 0.500 x 0.202 x 0.430 | 12000/4000/1000 | - |
| `prop_hanging_iron_gibbet` | 39,946 | 0.302 x 0.294 x 0.500 | 12000/4000/1000 | - |
| `prop_headless_stone_statue` | 39,736 | 0.191 x 0.157 x 0.500 | 12000/4000/1000 | - |
| `prop_heap_of_raw_ore_chunks` | 39,706 | 0.500 x 0.448 x 0.312 | 12000/4000/1319 | - |
| `prop_herb_drying_hamper` | 38,379 | 0.500 x 0.446 x 0.339 | 11999/4000/3173 | - |
| `prop_iron_banded_oak_door` | 39,799 | 0.318 x 0.381 x 0.500 | 12000/4000/1000 | - |
| `prop_iron_banded_ore_cart` | 39,852 | 0.447 x 0.500 x 0.448 | 12000/4000/1000 | - |
| `prop_iron_brazier` | 24,759 | 0.453 x 0.458 x 0.500 | 7999/2500/1022 | - |
| `prop_iron_cauldron_tripod` | 39,757 | 0.350 x 0.224 x 0.500 | 12000/3999/999 | - |
| `prop_iron_coal_skip_basket` | 39,433 | 0.432 x 0.447 x 0.500 | 12000/4000/1194 | - |
| `prop_iron_five_branch_candelabra` | 39,935 | 0.307 x 0.433 x 0.500 | 12000/3999/1000 | - |
| `prop_iron_footed_brazier` | 39,793 | 0.469 x 0.500 x 0.454 | 12000/4000/1040 | - |
| `prop_iron_lantern` | 24,269 | 0.185 x 0.153 x 0.500 | 8000/2500/600 | - |
| `prop_iron_shackles_chain` | 39,802 | 0.362 x 0.106 x 0.500 | 12000/4000/1000 | - |
| `prop_iron_tipped_plough` | 39,946 | 0.494 x 0.500 x 0.446 | 12000/4000/1000 | - |
| `prop_iron_toothed_harrow` | 39,847 | 0.500 x 0.486 x 0.415 | 12000/4000/1000 | - |
| `prop_ironbound_strongbox` | 44,451 | 0.470 x 0.500 x 0.428 | 11999/4000/2052 | - |
| `prop_kick_potter_wheel` | 39,903 | 0.500 x 0.422 x 0.482 | 12000/4000/1000 | - |
| `prop_ladder_back_chair` | 39,940 | 0.305 x 0.330 x 0.500 | 12000/4000/1000 | - |
| `prop_leather_saddle_stand` | 38,864 | 0.452 x 0.500 x 0.452 | 12000/4000/2181 | - |
| `prop_leather_travel_trunk` | 39,331 | 0.498 x 0.500 x 0.417 | 11999/4000/2151 | - |
| `prop_leather_waterskin_flask` | 39,927 | 0.429 x 0.458 x 0.500 | 12000/3999/1000 | - |
| `prop_long_trestle_table` | 39,998 | 0.500 x 0.484 x 0.322 | 11999/3999/999 | - |
| `prop_market_stall_frame` | 39,190 | 0.500 x 0.408 x 0.414 | 12000/4000/1000 | - |
| `prop_mead_barrel_cradle` | 38,280 | 0.487 x 0.500 x 0.416 | 11999/4000/1210 | - |
| `prop_sacks_and_grain` | 24,859 | 0.500 x 0.251 x 0.429 | 8000/2500/600 | - |
| `prop_stack_of_firewood` | 20,584 | 0.413 x 0.500 x 0.340 | 8000/2500/1383 | - |
| `prop_wooden_barrel` | 22,478 | 0.335 x 0.458 x 0.500 | 7999/2500/1344 | - |
| `prop_wooden_bench` | 24,843 | 0.500 x 0.427 x 0.365 | 7998/2500/600 | - |
| `prop_wooden_cart_wheel` | 24,921 | 0.473 x 0.329 x 0.500 | 8000/2500/600 | - |
| `prop_wooden_crate` | 23,909 | 0.500 x 0.463 x 0.390 | 7999/2500/1528 | - |

### Creatures — 62

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `creature_ancient_bog_tortoise` | 24,817 | 1.800 x 1.768 x 1.299 | 8000/2500/600 | R |
| `creature_animated_armour` | 21,064 | 1.010 x 0.620 x 1.800 | 8000/2500/1284 | R |
| `creature_arctic_frost_wolf` | 24,482 | 1.778 x 1.800 x 1.774 | 7999/2500/2209 | R |
| `creature_armoured_thorn_lizard` | 24,912 | 1.800 x 1.513 x 1.520 | 7999/2500/600 | R |
| `creature_ash_ember_hound` | 24,976 | 1.347 x 1.145 x 1.800 | 8000/2500/600 | R |
| `creature_barrow_wight_lord` | 24,962 | 0.730 x 0.475 x 1.800 | 8000/2500/599 | R |
| `creature_bog_lurker_beast` | 18,811 | 1.604 x 1.190 x 1.800 | 8000/2500/1665 | R |
| `creature_bog_raven_scout` | 24,778 | 1.250 x 1.331 x 1.800 | 8000/2500/1419 | R |
| `creature_bone_walker_husk` | 24,802 | 0.640 x 0.428 x 1.800 | 7999/2500/600 | R |
| `creature_bristleback_boar` | 20,394 | 1.752 x 1.800 x 1.500 | 7999/2500/2134 | R |
| `creature_bronze_serpent_sentry` | 24,823 | 0.952 x 1.365 x 1.800 | 8000/2499/600 | R |
| `creature_cave_hunting_spider` | 24,319 | 1.800 x 1.592 x 1.294 | 8000/2500/779 | R |
| `creature_cave_marsh_bat` | 24,740 | 1.800 x 0.892 x 1.200 | 8000/2500/600 | R |
| `creature_cave_salamander` | 24,933 | 1.800 x 1.065 x 1.342 | 8000/2500/600 | R |
| `creature_clay_servitor` | 24,847 | 0.971 x 0.726 x 1.800 | 8000/2500/600 | R |
| `creature_cliff_hawk_hunter` | 23,824 | 1.800 x 1.380 x 1.345 | 8000/2500/1320 | R |
| `creature_crystal_antler_stag` | 24,840 | 0.903 x 1.067 x 1.800 | 7999/2500/600 | R |
| `creature_desert_camel_drover` | 23,291 | 1.445 x 1.033 x 1.800 | 8000/2500/1566 | R |
| `creature_desert_vulture` | 24,634 | 1.800 x 0.901 x 1.111 | 8000/2500/796 | R |
| `creature_drowned_hound` | 24,926 | 1.789 x 1.800 x 1.737 | 7999/2500/599 | R |
| `creature_dune_armoured_scorpion` | 22,350 | 1.800 x 1.572 x 1.296 | 8000/2500/1336 | R |
| `creature_dune_jackal_scout` | 24,980 | 1.258 x 1.203 x 1.800 | 8000/2500/600 | R |
| `creature_fey_thorn_cat` | 24,698 | 1.800 x 1.261 x 1.762 | 8000/2500/600 | R |
| `creature_forest_marten_runner` | 39,758 | 1.800 x 1.165 x 1.052 | 8000/2500/599 | - |
| `creature_forest_praying_mantis` | 24,519 | 1.552 x 1.543 x 1.800 | 7999/2500/599 | R |
| `creature_frost_hare_fey` | 18,463 | 1.202 x 0.995 x 1.800 | 8000/2500/1492 | R |
| `creature_frost_wolf` | 23,901 | 1.800 x 1.487 x 1.651 | 8000/2500/1954 | R |
| `creature_frost_wolf_side` | 24,606 | 1.800 x 0.858 x 1.194 | 8000/2718/2718 | R |
| `creature_giant_stag_beetle` | 24,491 | 1.483 x 0.686 x 1.800 | 8000/2500/600 | R |
| `creature_glacier_seal_hunter` | 24,994 | 1.800 x 1.300 x 1.547 | 8000/2500/599 | R |
| `creature_great_horned_owl` | 24,815 | 0.892 x 1.096 x 1.800 | 7999/2500/1750 | R |
| `creature_great_river_serpent` | 24,992 | 1.800 x 1.219 x 1.380 | 8000/2500/600 | R |
| `creature_grey_badger_warden` | 12,759 | 1.592 x 1.800 x 1.665 | 8000/2500/833 | R |
| `creature_grove_bound_dryad_ox` | 23,610 | 1.800 x 1.556 x 1.796 | 8000/2499/2179 | R |
| `creature_highland_brown_bear` | 39,687 | 1.417 x 1.800 x 1.538 | 12000/3999/2926 | R |
| `creature_iron_boar_automaton` | 24,320 | 1.604 x 1.800 x 1.778 | 8000/2500/1622 | R |
| `creature_marsh_giant_wasp` | 24,913 | 1.800 x 0.677 x 1.341 | 8000/2500/600 | R |
| `creature_marsh_heron_stalker` | 24,281 | 1.070 x 0.594 x 1.800 | 8000/2500/600 | R |
| `creature_mountain_ram_goat` | 24,174 | 1.565 x 1.504 x 1.800 | 7998/2500/1950 | R |
| `creature_musk_ox_herder` | 31,519 | 1.662 x 1.800 x 1.673 | 7999/2500/2410 | - |
| `creature_plains_antelope_doe` | 24,915 | 1.066 x 0.913 x 1.800 | 8000/2500/600 | R |
| `creature_red_fox_scavenger` | 24,717 | 1.800 x 1.495 x 1.645 | 7999/2500/1711 | R |
| `creature_ridgeback_lizard` | 24,773 | 1.800 x 1.419 x 1.359 | 7999/2500/600 | R |
| `creature_rotting_revenant` | 24,891 | 0.819 x 0.585 x 1.800 | 7999/2500/600 | R |
| `creature_sand_skink_runner` | 39,974 | 1.800 x 0.998 x 0.692 | 12000/4000/1000 | R |
| `creature_skeleton_hound` | 39,842 | 1.607 x 1.658 x 1.800 | 8000/2499/600 | R |
| `creature_slime_marsh_frog` | 39,820 | 1.598 x 1.800 x 1.257 | 12000/4000/999 | R |
| `creature_small_ember_drake` | 39,394 | 1.800 x 1.176 x 1.254 | 11999/4000/1000 | R |
| `creature_snow_hare_hopper` | 39,539 | 1.532 x 1.444 x 1.800 | 8000/2500/1469 | - |
| `creature_snow_lynx_stalker` | 39,733 | 1.785 x 1.666 x 1.800 | 12000/4000/1000 | R |
| `creature_spotted_hyena_brute` | 39,523 | 1.640 x 1.540 x 1.800 | 12000/4000/3072 | R |
| `creature_steppe_bison_bull` | 39,681 | 1.762 x 1.619 x 1.800 | 12000/4000/1211 | R |
| `creature_stone_guardian` | 39,753 | 1.240 x 1.058 x 1.800 | 12000/4000/1000 | R |
| `creature_storm_charged_ram` | 39,867 | 1.600 x 1.505 x 1.800 | 12000/4000/1000 | R |
| `creature_storm_falcon` | 39,703 | 1.800 x 1.280 x 1.175 | 11999/4000/1000 | R |
| `creature_tawny_cave_panther` | 39,886 | 1.800 x 1.269 x 1.286 | 11999/4000/1000 | R |
| `creature_timber_stag_elk` | 39,294 | 1.262 x 1.289 x 1.800 | 12000/4000/1094 | R |
| `creature_tundra_wolverine` | 32,609 | 1.800 x 1.439 x 1.550 | 12000/4000/2985 | R |
| `creature_tunnel_centipede_brute` | 39,775 | 1.800 x 1.406 x 1.504 | 12000/4000/1000 | R |
| `creature_twilight_moth` | 39,066 | 1.800 x 0.669 x 1.216 | 12000/4000/1000 | R |
| `creature_wart_toad_brute` | 39,934 | 1.553 x 1.800 x 1.313 | 11999/4000/1000 | R |
| `creature_woolly_highland_sheep` | 39,763 | 1.504 x 1.800 x 1.448 | 12000/3999/1471 | R |

### Items — 45

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `item_amber_chunk` | 35,726 | 0.500 x 0.343 x 0.440 | 8000/2500/1198 | - |
| `item_amethyst_geode` | 39,872 | 0.423 x 0.335 x 0.500 | 7999/2500/600 | - |
| `item_bloodstone_chunk` | 39,994 | 0.470 x 0.500 x 0.439 | 8000/2499/599 | - |
| `item_bog_iron_bloom` | 39,600 | 0.492 x 0.500 x 0.460 | 7999/2499/1025 | - |
| `item_bronze_ingot` | 36,006 | 0.492 x 0.500 x 0.486 | 8000/2500/1672 | - |
| `item_charcoal_steel_ingot` | 39,698 | 0.464 x 0.500 x 0.391 | 8000/2500/600 | - |
| `item_coldbloom_shard` | 39,915 | 0.381 x 0.312 x 0.500 | 8000/2500/599 | - |
| `item_emerald_crystal` | 39,395 | 0.226 x 0.280 x 0.500 | 8000/2500/2023 | - |
| `item_firstfold_iron_fragment` | 35,184 | 0.350 x 0.319 x 0.500 | 7999/2499/1547 | - |
| `item_garnet_crystal` | 39,003 | 0.203 x 0.187 x 0.500 | 8000/2500/1869 | - |
| `item_gold_ingot` | 39,885 | 0.500 x 0.416 x 0.401 | 7999/2500/600 | - |
| `item_grey_iron_ingot` | 39,537 | 0.500 x 0.425 x 0.427 | 8000/2500/600 | - |
| `item_health_potion` | 35,002 | 0.358 x 0.244 x 0.500 | 8000/2500/1378 | - |
| `item_jet_stone` | 39,523 | 0.443 x 0.341 x 0.500 | 8000/2499/600 | - |
| `item_lead_ingot` | 39,984 | 0.500 x 0.420 x 0.416 | 7999/2500/600 | - |
| `item_mana_potion` | 39,891 | 0.277 x 0.195 x 0.500 | 8000/2499/600 | - |
| `item_moonstone_cabochon` | 40,000 | 0.342 x 0.500 x 0.448 | 7999/2500/600 | - |
| `item_nullstone` | 39,552 | 0.447 x 0.500 x 0.373 | 8000/2500/870 | - |
| `item_otherfire_lamp` | 39,908 | 0.200 x 0.175 x 0.500 | 8000/2500/600 | - |
| `item_othergate_lens_frame` | 39,557 | 0.469 x 0.320 x 0.500 | 7999/2500/636 | - |
| `item_otherglass_lens` | 39,115 | 0.500 x 0.465 x 0.450 | 8000/2499/619 | - |
| `item_otherglass_shard` | 38,980 | 0.245 x 0.130 x 0.500 | 8000/2500/600 | - |
| `item_othersteel_ingot` | 39,161 | 0.500 x 0.363 x 0.395 | 8000/2500/600 | - |
| `item_otherwrought_key` | 39,982 | 0.277 x 0.126 x 0.500 | 8000/2499/600 | - |
| `item_pachakuti_residue_pile` | 32,692 | 0.500 x 0.430 x 0.226 | 8000/2500/1801 | - |
| `item_raw_copper_ore` | 39,874 | 0.485 x 0.500 x 0.427 | 8000/2500/600 | - |
| `item_raw_gold_ore` | 39,897 | 0.426 x 0.500 x 0.340 | 8000/2499/600 | - |
| `item_raw_iron_ore` | 39,950 | 0.476 x 0.500 x 0.405 | 8000/2500/600 | - |
| `item_raw_lead_ore` | 39,712 | 0.417 x 0.500 x 0.385 | 8000/2857/2857 | - |
| `item_raw_otherstone` | 39,974 | 0.463 x 0.500 x 0.436 | 7999/2500/600 | - |
| `item_raw_silver_ore` | 39,703 | 0.476 x 0.500 x 0.415 | 7999/2500/787 | - |
| `item_raw_tin_ore` | 39,886 | 0.459 x 0.500 x 0.395 | 8000/2500/600 | - |
| `item_resonance_quartz` | 40,000 | 0.186 x 0.228 x 0.500 | 8000/2500/600 | - |
| `item_ruby_crystal` | 39,978 | 0.195 x 0.248 x 0.500 | 8000/2500/600 | - |
| `item_rusty_key` | 39,564 | 0.246 x 0.126 x 0.500 | 8000/2499/600 | - |
| `item_sapphire_crystal` | 39,037 | 0.192 x 0.236 x 0.500 | 8000/2500/1996 | - |
| `item_silence_shard` | 39,962 | 0.160 x 0.482 x 0.500 | 8000/2500/599 | - |
| `item_silver_ingot` | 33,911 | 0.500 x 0.441 x 0.433 | 8000/2500/1691 | - |
| `item_thinshard_pouch` | 39,414 | 0.500 x 0.425 x 0.465 | 8000/2500/881 | - |
| `item_tideglass_pane` | 37,139 | 0.296 x 0.500 x 0.309 | 8000/2869/2869 | - |
| `item_tidepearl` | 39,472 | 0.408 x 0.366 x 0.500 | 8000/2500/600 | - |
| `item_topaz_crystal` | 38,573 | 0.192 x 0.302 x 0.500 | 7999/2500/934 | - |
| `item_torch` | 39,902 | 0.207 x 0.124 x 0.500 | 8000/2500/600 | - |
| `item_veiliron_ingot` | 40,000 | 0.500 x 0.385 x 0.409 | 7999/2499/600 | - |
| `item_veilwater_flask` | 35,010 | 0.500 x 0.488 x 0.429 | 8000/2499/1655 | - |

### Magic and ritual props — 40

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `magic_altar_portable` | 39,309 | 0.464 x 0.500 x 0.439 | 8000/2500/600 | - |
| `magic_amulet_copper` | 39,998 | 0.343 x 0.105 x 0.500 | 8000/2499/600 | - |
| `magic_astrolabe_brass` | 39,798 | 0.416 x 0.142 x 0.500 | 8000/2499/600 | - |
| `magic_athame_dagger` | 38,303 | 1.104 x 0.425 x 1.200 | 8000/2500/600 | - |
| `magic_bell_ritual` | 39,966 | 0.304 x 0.305 x 0.500 | 8000/2500/600 | - |
| `magic_brazier_small` | 39,771 | 0.444 x 0.500 x 0.434 | 8000/2500/600 | - |
| `magic_cauldron_small` | 39,226 | 0.441 x 0.500 x 0.480 | 8000/2499/600 | - |
| `magic_censer_brass` | 37,849 | 0.215 x 0.218 x 0.500 | 8000/2500/847 | - |
| `magic_compass_arcane` | 39,878 | 0.441 x 0.396 x 0.500 | 7999/2500/600 | - |
| `magic_crystal_shard_focus` | 39,161 | 0.114 x 0.124 x 0.500 | 7999/2500/599 | - |
| `magic_divining_rods` | 40,000 | 0.457 x 0.122 x 0.500 | 8000/2500/600 | - |
| `magic_focus_crystal_cluster` | 39,855 | 0.336 x 0.385 x 0.500 | 8000/2500/600 | - |
| `magic_gauntlet_channel` | 38,261 | 0.378 x 0.266 x 0.500 | 8000/2499/872 | - |
| `magic_grimoire_bound` | 39,522 | 0.484 x 0.371 x 0.500 | 8000/2499/600 | - |
| `magic_hourglass_brass` | 36,674 | 0.237 x 0.193 x 0.500 | 8000/2500/1440 | - |
| `magic_inkwell_brass` | 39,680 | 0.384 x 0.383 x 0.500 | 8000/2499/600 | - |
| `magic_inscription_scroll` | 38,154 | 0.388 x 0.269 x 0.500 | 8000/2606/2606 | - |
| `magic_lantern_warding` | 39,132 | 0.375 x 0.393 x 0.500 | 8000/2500/603 | - |
| `magic_lens_crystal` | 39,546 | 0.500 x 0.254 x 0.452 | 8000/2499/692 | - |
| `magic_mortar_pestle` | 39,980 | 0.399 x 0.406 x 0.500 | 7999/2500/600 | - |
| `magic_orb_scrying` | 39,985 | 0.350 x 0.356 x 0.500 | 8000/2500/600 | - |
| `magic_pendulum_bob` | 39,962 | 0.116 x 0.135 x 0.500 | 8000/2499/600 | - |
| `magic_phial_glass` | 35,007 | 0.165 x 0.242 x 0.500 | 8000/2500/1688 | - |
| `magic_ring_silver` | 39,973 | 0.429 x 0.422 x 0.500 | 8000/2500/600 | - |
| `magic_rod_bone` | 40,000 | 0.500 x 0.185 x 0.499 | 7999/2499/600 | - |
| `magic_rod_compact` | 39,746 | 0.500 x 0.137 x 0.481 | 8000/2500/600 | - |
| `magic_runestone_pillar` | 39,970 | 0.184 x 0.242 x 0.500 | 8000/2499/599 | - |
| `magic_scales_balance` | 38,621 | 0.500 x 0.210 x 0.412 | 8000/2500/1039 | - |
| `magic_scepter_regal` | 39,996 | 0.089 x 0.081 x 0.500 | 8000/2500/600 | - |
| `magic_scrying_bowl` | 40,000 | 1.200 x 1.154 x 1.068 | 8000/2500/600 | - |
| `magic_sigil_plate` | 39,922 | 0.180 x 0.300 x 0.175 | 7999/2500/607 | - |
| `magic_skull_ritual` | 39,762 | 0.500 x 0.429 x 0.389 | 8000/2500/600 | - |
| `magic_staff_ironbound` | 39,864 | 1.092 x 0.288 x 1.200 | 8000/2500/600 | - |
| `magic_staff_oaken` | 39,972 | 0.084 x 0.450 x 1.200 | 8000/2500/600 | - |
| `magic_talisman_bone` | 38,861 | 0.169 x 0.126 x 0.500 | 7999/2499/600 | - |
| `magic_totem_carved` | 39,978 | 0.292 x 0.232 x 0.500 | 8000/2500/600 | - |
| `magic_veilglass_pane` | 39,967 | 0.319 x 0.500 x 0.275 | 7999/2499/600 | - |
| `magic_wand_ash` | 39,840 | 0.466 x 0.060 x 0.500 | 8000/2500/600 | - |
| `magic_wand_blackthorn` | 39,874 | 0.488 x 0.066 x 0.500 | 8000/2500/600 | - |
| `magic_ward_stone_small` | 39,940 | 0.500 x 0.490 x 0.427 | 8000/2499/600 | - |

### Travel and traversal — 36

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `travel_bridge_rope` | 39,875 | 4.000 x 3.981 x 3.799 | 8000/2500/599 | - |
| `travel_bridge_stone_arch` | 39,618 | 4.000 x 2.972 x 2.057 | 8000/2500/600 | - |
| `travel_cairn_stone_pile` | 39,790 | 0.367 x 0.211 x 0.500 | 8000/2500/600 | - |
| `travel_camp_bedroll` | 74,511 | 0.500 x 0.495 x 0.271 | 8000/3470/3470 | - |
| `travel_camp_fire_tripod` | 39,964 | 0.287 x 0.229 x 0.500 | 8000/2500/599 | - |
| `travel_camp_tent_small` | 39,907 | 0.491 x 0.500 x 0.264 | 8000/2500/600 | - |
| `travel_climbing_pitons` | 39,888 | 0.500 x 0.107 x 0.302 | 8000/2500/600 | - |
| `travel_climbing_rope_coil` | 39,815 | 0.150 x 0.160 x 0.500 | 8000/2499/775 | - |
| `travel_crampons` | 38,787 | 0.500 x 0.338 x 0.432 | 8000/2499/824 | - |
| `travel_ferry_dock` | 37,602 | 0.462 x 0.500 x 0.311 | 8000/2500/600 | - |
| `travel_flying_mount_tack` | 39,071 | 0.489 x 0.316 x 0.500 | 8000/2500/899 | - |
| `travel_ford_marker` | 39,990 | 0.355 x 0.202 x 0.500 | 8000/2500/599 | - |
| `travel_gate_lens` | 39,954 | 4.000 x 3.214 x 3.493 | 8000/2500/599 | - |
| `travel_glider_canvas` | 39,872 | 0.500 x 0.408 x 0.188 | 8000/2500/600 | - |
| `travel_grappling_hook` | 39,812 | 0.122 x 0.102 x 0.500 | 8000/2500/600 | - |
| `travel_ice_axe` | 39,974 | 0.632 x 0.342 x 1.200 | 7999/2500/600 | - |
| `travel_lift_wooden` | 39,658 | 0.435 x 0.381 x 0.500 | 8000/2500/600 | - |
| `travel_othergate_ancient_arch` | 39,832 | 3.179 x 2.458 x 4.000 | 8000/2500/912 | - |
| `travel_othergate_invisible_threshold` | 39,916 | 4.000 x 1.960 x 2.936 | 8000/2499/600 | - |
| `travel_othergate_mechanical` | 39,871 | 3.666 x 2.692 x 4.000 | 8000/2499/599 | - |
| `travel_othergate_mirrored_door` | 39,884 | 1.742 x 0.702 x 4.000 | 8000/2500/600 | - |
| `travel_othergate_root_arch` | 39,901 | 4.000 x 2.804 x 2.911 | 8000/2500/599 | - |
| `travel_othergate_standing_stone` | 40,000 | 1.456 x 2.612 x 4.000 | 8000/2500/600 | - |
| `travel_othergate_well` | 39,821 | 2.674 x 2.969 x 4.000 | 8000/2500/600 | - |
| `travel_pack_panniers` | 34,286 | 0.389 x 0.500 x 0.384 | 8000/2500/1732 | - |
| `travel_recall_anchor` | 39,927 | 0.361 x 0.293 x 0.500 | 8000/2500/600 | - |
| `travel_rope_ladder` | 38,989 | 0.115 x 0.076 x 0.500 | 8000/2500/600 | - |
| `travel_signpost_crossroads` | 39,016 | 0.265 x 0.108 x 0.500 | 8000/2500/665 | - |
| `travel_snowshoes` | 39,662 | 0.400 x 0.059 x 0.500 | 7999/2499/812 | - |
| `travel_stable_rack` | 39,920 | 0.500 x 0.413 x 0.435 | 8000/2500/600 | - |
| `travel_survey_tripod` | 39,931 | 0.275 x 0.189 x 0.500 | 8000/2500/600 | - |
| `travel_toll_booth` | 38,644 | 0.492 x 0.450 x 0.500 | 8000/2500/1406 | - |
| `travel_torch_bracket` | 39,499 | 0.095 x 0.114 x 0.500 | 8000/2500/600 | - |
| `travel_transit_platform` | 39,972 | 0.500 x 0.411 x 0.216 | 8000/2500/600 | - |
| `travel_waystone_marker` | 39,988 | 0.306 x 0.349 x 0.500 | 7999/2499/600 | - |
| `travel_wing_harness` | 39,671 | 0.452 x 0.500 x 0.444 | 8000/2500/846 | - |

### Resources — 18

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `resource_arcane_essence_vial` | 38,998 | 0.282 x 0.246 x 0.500 | 8000/2500/600 | - |
| `resource_cured_leather_roll` | 38,871 | 0.475 x 0.313 x 0.500 | 8000/2499/1486 | - |
| `resource_dragon_bone_shard` | 39,388 | 0.447 x 0.149 x 0.500 | 8000/2500/600 | - |
| `resource_gold_ore_nugget` | 36,283 | 0.458 x 0.117 x 0.500 | 8000/2500/1696 | - |
| `resource_healing_herb_leaf` | 39,829 | 0.435 x 0.134 x 0.500 | 8000/2500/600 | - |
| `resource_herb_leaf` | 39,923 | 0.423 x 0.113 x 0.500 | 7999/2500/600 | - |
| `resource_iron_ingot` | 38,062 | 0.489 x 0.340 x 0.500 | 8000/2500/2253 | - |
| `resource_iron_ore` | 39,968 | 0.491 x 0.434 x 0.500 | 8000/2500/600 | - |
| `resource_iron_ore_chunk` | 38,914 | 0.492 x 0.107 x 0.500 | 8000/2500/724 | - |
| `resource_ironwood_log` | 40,000 | 0.420 x 0.500 x 0.402 | 8000/2500/600 | - |
| `resource_linen_fibre_bundle` | 39,743 | 0.373 x 0.139 x 0.500 | 8000/2499/1330 | - |
| `resource_moonpetal_flower` | 38,521 | 0.500 x 0.099 x 0.456 | 8000/2500/599 | - |
| `resource_oak_wood` | 34,519 | 0.500 x 0.388 x 0.478 | 8000/2500/2273 | - |
| `resource_raw_hide` | 39,894 | 0.489 x 0.070 x 0.500 | 8000/2500/600 | - |
| `resource_rawhide_pelt_fold` | 38,893 | 0.500 x 0.449 x 0.466 | 7999/2499/1910 | - |
| `resource_ruby_gem_crystal` | 39,173 | 0.350 x 0.132 x 0.500 | 8000/2500/720 | - |
| `resource_silk_thread_spool` | 39,785 | 0.454 x 0.500 x 0.454 | 8000/2499/672 | - |
| `resource_steel_ingot_bar` | 39,272 | 0.500 x 0.358 x 0.480 | 8000/2500/600 | - |

### Flora — 14

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `flora_birch_tree` | 31,479 | 0.442 x 0.428 x 0.500 | 7999/2539/2539 | - |
| `flora_boneleaf_bush` | 39,638 | 0.500 x 0.416 x 0.446 | 8000/2500/1876 | - |
| `flora_bracket_fungus` | 39,843 | 0.500 x 0.374 x 0.411 | 8000/2500/600 | - |
| `flora_bramble_bush` | 39,565 | 0.500 x 0.497 x 0.412 | 7999/2500/1543 | - |
| `flora_cattail_clump` | 39,797 | 0.454 x 0.452 x 0.500 | 8000/2500/600 | - |
| `flora_dead_tree` | 39,761 | 0.467 x 0.324 x 0.500 | 8000/2500/599 | - |
| `flora_fern_clump` | 39,463 | 0.500 x 0.448 x 0.439 | 7999/3198/3198 | - |
| `flora_glowcap_cluster` | 39,609 | 0.485 x 0.388 x 0.500 | 8000/2500/944 | - |
| `flora_heather_patch` | 32,850 | 0.499 x 0.500 x 0.391 | 8000/2512/2512 | - |
| `flora_mirrorfern` | 39,728 | 0.497 x 0.500 x 0.439 | 8000/2500/761 | - |
| `flora_oak_tree` | 35,919 | 0.453 x 0.500 x 0.491 | 8000/2901/2901 | - |
| `flora_pine_tree` | 37,109 | 0.304 x 0.393 x 0.500 | 7999/3455/3455 | - |
| `flora_slowtree` | 31,613 | 0.405 x 0.500 x 0.486 | 8000/2500/2350 | - |
| `flora_willow_tree` | 33,450 | 0.393 x 0.500 x 0.442 | 8000/3192/3192 | - |

### Weapon components (modular) — 10

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `weaponcomp_blade_arming_sword_a` | 29,072 | 0.077 x 0.122 x 0.713 | 8000/2499/1982 | P |
| `weaponcomp_grip_standard_a` | 47,252 | 0.391 x 0.290 x 0.880 | - | P |
| `weaponcomp_grip_vaskaal_a` | 29,174 | 0.422 x 0.155 x 0.396 | 7999/2661/2661 | P |
| `weaponcomp_haft_long_a` | 37,312 | 0.112 x 0.071 x 0.799 | 8000/2500/2141 | P |
| `weaponcomp_haft_short_a` | 2,188 | 0.070 x 0.070 x 0.050 | 2188/2186/600 | P |
| `weaponcomp_mace_head_flanged_a` | 34,837 | 0.379 x 0.219 x 0.281 | 8000/2500/2382 | P |
| `weaponcomp_mace_head_flanged_a_stub2` | 39,702 | 0.083 x 0.105 x 0.300 | 8000/2500/599 | - |
| `weaponcomp_mechanism_telescope_a` | 39,920 | 0.824 x 0.589 x 0.178 | 8000/2499/599 | P |
| `weaponcomp_pommel_counterweight_a` | 37,219 | 0.445 x 0.445 x 0.641 | - | P |
| `weaponcomp_shield_heater_a` | 35,361 | 0.374 x 0.980 x 0.538 | 8000/2499/1469 | P |

### Containers — 8

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `container_alchemy_cold_box` | 39,643 | 0.465 x 0.362 x 0.500 | 8000/2500/600 | - |
| `container_ammunition_case` | 39,586 | 0.500 x 0.445 x 0.363 | 8000/2499/684 | - |
| `container_armory_rack` | 39,959 | 0.417 x 0.373 x 0.500 | 8000/2500/599 | - |
| `container_backpack_traveller` | 38,641 | 0.446 x 0.460 x 0.500 | 8000/2499/1130 | - |
| `container_bank_strongbox` | 39,915 | 0.489 x 0.446 x 0.500 | 8000/2500/600 | - |
| `container_barrel_oak` | 39,247 | 0.369 x 0.422 x 0.500 | 8000/2696/2696 | - |
| `container_belt_pouch_leather` | 39,854 | 0.500 x 0.441 x 0.404 | 8000/2500/730 | - |
| `container_chest_iron_banded` | 39,455 | 0.500 x 0.435 x 0.373 | 7998/2500/1573 | - |

### NPCs — 8

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `npc_constructed_engineer` | 24,873 | 0.547 x 0.806 x 1.800 | 8000/2499/600 | R |
| `npc_kal_smith` | 23,143 | 0.924 x 0.649 x 1.800 | 8000/2500/1447 | R |
| `npc_mor_witness` | 24,983 | 0.512 x 0.328 x 1.800 | 8000/2500/600 | R |
| `npc_ondrek_stonecarver` | 24,791 | 1.569 x 1.800 x 1.689 | 8000/2500/599 | R |
| `npc_orenth_guide` | 24,607 | 0.748 x 0.614 x 1.800 | 8000/2500/1182 | R |
| `npc_siann_archivist` | 23,189 | 0.512 x 0.751 x 1.800 | 7998/2500/1189 | R |
| `npc_vaskaal_envoy` | 24,837 | 0.606 x 0.386 x 1.800 | 8000/2500/599 | R |
| `npc_veth_magistrate` | 24,542 | 0.756 x 0.568 x 1.800 | 8000/2500/1077 | R |

### Races — 8

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `race_constructed_representative` | 24,935 | 1.800 x 0.339 x 1.605 | 8000/2499/600 | R |
| `race_kal_representative` | 24,535 | 1.065 x 0.621 x 1.800 | 7999/2500/600 | R |
| `race_mor_representative` | 24,994 | 1.800 x 0.343 x 1.624 | 8000/2500/600 | R |
| `race_ondrek_representative` | 24,814 | 1.534 x 1.198 x 1.800 | 7998/2500/1487 | R |
| `race_orenth_representative` | 24,558 | 0.688 x 0.508 x 1.800 | 8000/2500/886 | R |
| `race_siann_representative` | 24,531 | 0.696 x 0.876 x 1.800 | 7999/2500/1140 | R |
| `race_vaskaal_representative` | 24,931 | 0.549 x 0.319 x 1.800 | 8000/2500/600 | R |
| `race_veth_representative` | 24,512 | 0.687 x 0.398 x 1.800 | 7999/2500/858 | R |

### Animals — 7

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `animal_guard_mastiff` | 39,765 | 1.800 x 1.639 x 1.725 | 8000/2500/600 | - |
| `animal_herding_dog` | 39,850 | 1.800 x 1.147 x 1.662 | 8000/2500/600 | - |
| `animal_messenger_raven` | 36,138 | 1.642 x 0.884 x 1.800 | 8000/2500/1580 | - |
| `animal_mouser_cat` | 39,816 | 1.469 x 1.405 x 1.800 | 8000/2500/599 | - |
| `animal_scout_hawk` | 35,829 | 1.311 x 1.124 x 1.800 | 7999/2500/1878 | - |
| `animal_tracking_hound` | 39,932 | 1.800 x 1.134 x 1.280 | 8000/2500/600 | - |
| `animal_truffle_pig` | 39,198 | 1.800 x 1.286 x 1.180 | 8000/2500/1363 | - |

### Race bodies (canonical) — 7

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `racebody_constructed_pose` | 39,976 | 0.600 x 0.450 x 1.800 | 12000/4000/1000 | R |
| `racebody_kal_pose` | 38,924 | 1.055 x 0.569 x 1.800 | 11999/4000/1000 | R |
| `racebody_mor_pose` | 39,568 | 1.800 x 0.273 x 1.575 | 11999/4000/1000 | R |
| `racebody_ondrek_pose` | 39,932 | 1.306 x 0.905 x 1.800 | 12000/4000/1000 | R |
| `racebody_orenth_pose` | 39,768 | 0.536 x 0.388 x 1.800 | 12000/4000/1000 | R |
| `racebody_vaskaal_pose` | 39,522 | 1.800 x 0.583 x 1.657 | 12000/4000/1000 | R |
| `racebody_veth_pose` | 39,157 | 1.800 x 0.279 x 1.574 | 12000/4000/1000 | R |

### Vehicles — 6

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `vehicle_coastal_ship` | 38,172 | 0.358 x 0.254 x 0.500 | 7999/2500/1422 | - |
| `vehicle_four_wheel_wagon` | 39,727 | 0.485 x 0.500 x 0.465 | 8000/2500/743 | - |
| `vehicle_handcart` | 39,940 | 0.500 x 0.484 x 0.321 | 8000/2499/600 | - |
| `vehicle_river_barge` | 38,992 | 0.500 x 0.388 x 0.328 | 8000/2500/697 | - |
| `vehicle_river_ferry_boat` | 39,506 | 0.500 x 0.443 x 0.327 | 8000/2500/1907 | - |
| `vehicle_wooden_cart` | 39,731 | 0.500 x 0.473 x 0.370 | 8000/2500/600 | - |

### Tools — 5

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `tool_blacksmith_hammer` | 24,578 | 0.249 x 0.172 x 0.600 | 8000/2499/599 | - |
| `tool_blacksmith_tongs` | 24,910 | 0.535 x 0.183 x 0.600 | 7999/2500/600 | - |
| `tool_hand_saw` | 24,994 | 0.600 x 0.187 x 0.583 | 8000/2500/600 | - |
| `tool_iron_shovel` | 39,942 | 0.191 x 0.124 x 0.600 | 12000/4000/1000 | - |
| `tool_mining_pick` | 24,713 | 0.487 x 0.202 x 0.600 | 8000/2500/600 | - |

### Armour — 4

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `armour_chest_plate_base_a` | 38,176 | 0.458 x 0.483 x 0.901 | 7999/2500/2003 | P |
| `armour_chest_underlayer_gambeson_a` | 38,990 | 0.738 x 0.366 x 0.926 | 7999/2500/1801 | P |
| `armour_gorget_plate_a` | 39,128 | 0.720 x 0.815 x 0.789 | 7999/2500/946 | P |
| `armour_kal_back_channel_a` | 36,052 | 0.622 x 0.372 x 0.786 | 8000/2500/1945 | P |

### Reagents — 4

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `reagent_ceramic_alchemy_jar` | 39,283 | 0.405 x 0.480 x 0.500 | 8000/2500/648 | - |
| `reagent_glass_apothecary_jar` | 34,522 | 0.320 x 0.424 x 0.500 | 7999/2500/2189 | - |
| `reagent_herb_drying_rack` | 39,891 | 0.500 x 0.408 x 0.490 | 8000/2499/600 | - |
| `reagent_woundmoss_bundle` | 36,257 | 0.500 x 0.375 x 0.488 | 8000/2551/2551 | - |

### Herbs — 1

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `herb_bitterroot` | 39,432 | 0.296 x 0.285 x 0.500 | 8000/2500/600 | - |

### Magic components (modular) — 1

| Asset | Tris | Dimensions (m) | LOD budgets | Flags |
|---|---:|---|---|---|
| `magiccomp_focus_crystal_a` | 29,421 | 0.265 x 0.284 x 0.461 | - | P |

## 3. Rendered but not yet built

- **UI icons (2D)** (64): `icon_acid_spray_cone`, `icon_arcane_missile_volley`, `icon_arcane_ward_bubble`, `icon_backstab`, `icon_blade_dance_flurry`, `icon_bleed_status_wound`, `icon_blessing_of_might`, `icon_burn_status_flame`, `icon_burning_hand_cast`, `icon_cleansing_purge_rite`, `icon_cleaving_axe_swing`, `icon_cursed_status_skull`, `icon_dodge_roll`, `icon_evasive_dodge_roll`, `icon_fire_rune_talisman`, `icon_fireball`, `icon_flame_orb_burst`, `icon_frost_shard_lance`, `icon_frostbolt`, `icon_frozen_status_shard`, `icon_gale_wind_slash`, `icon_haste_time_warp`, `icon_hasted_status_boots`, `icon_heal`, `icon_healing_light_touch`, `icon_holy_smite_hammer`, `icon_ice_storm_shards`, `icon_iron_shield_block`, `icon_life_link_transfer`, `icon_lightning`, `icon_lightning_bolt_strike`, `icon_meteor_falling_strike`, `icon_nimble_reflex_parry`, `icon_piercing_spear_thrust`, `icon_poison_bolt_hurl`, `icon_poison_cloud`, `icon_poison_status_mark`, `icon_power_attack`, `icon_profession_alchemy`, `icon_profession_blacksmith`, `icon_profession_enchanting`, `icon_profession_tailoring`, `icon_rooted_status_vines`, `icon_school_arcane_study`, `icon_school_blood_magic`, `icon_school_divine_light`, `icon_school_druidic_nature`, `icon_school_elemental_fire`, `icon_school_necromancy`, `icon_school_runic_carving`, `icon_school_shadow_arts`, `icon_shadow_bolt_dagger`, `icon_shadow_cloak_veil`, `icon_shield_block`, `icon_silenced_status_seal`, `icon_soul_drain_grasp`, `icon_stone_skin_plating`, `icon_stone_spike_eruption`, `icon_stunned_status_stars`, `icon_summon_minion`, `icon_swift_sprint_boots`, `icon_thunder_clap_shockwave`, `icon_weakened_status_arm`, `icon_whirlwind_spin_strike`
- **World materials (2D)** (60): `material_arcane_runestone`, `material_blackened_steel_plate`, `material_blighted_moss`, `material_bone_inlay_panel`, `material_burlap_sacking`, `material_carved_timber`, `material_cast_iron_surface`, `material_chainmail_links`, `material_charred_wood`, `material_churned_wet_mud`, `material_clay_roof_tiles`, `material_coarse_linen_cloth`, `material_copper_sheeting`, `material_corroded_brass_fittings`, `material_corrupted_growth`, `material_cracked_dry_earth`, `material_crushed_velvet`, `material_crystal_ore_vein`, `material_dragon_scale_hide`, `material_driftwood_planks`, `material_elm_butcher_block`, `material_fence_board_row`, `material_fine_desert_sand`, `material_flagstone_paving`, `material_frozen_ice_sheet`, `material_granite_block_wall`, `material_hammered_bronze`, `material_heathered_wool_weave`, `material_iron_banding_straps`, `material_limestone_ashlar`, `material_log_end_grain`, `material_loose_gravel`, `material_marble_veined`, `material_marsh_grass_turf`, `material_mossy_stone_course`, `material_oak_plank_floor`, `material_packed_dirt_ground`, `material_packed_snow_crust`, `material_pine_board_wall`, `material_plaster_lath_wall`, `material_polished_steel_sheet`, `material_quartzite_stone_wall`, `material_quilted_padding`, `material_rawhide_stretched`, `material_red_brick_wall`, `material_river_cobblestone`, `material_rough_tree_bark`, `material_rubble_stone_wall`, `material_rusted_iron_plate`, `material_sandstone_block`, `material_slate_roof_scale`, `material_tanned_leather_hide`, `material_tarnished_silver`, `material_thatch_reed_bundle`, `material_thick_fur_pelt`, `material_volcanic_ash_fall`, `material_volcanic_basalt_flow`, `material_wattle_daub_wall`, `material_weathered_teak_deck`, `material_wooden_shingles`
- **Props** (45): `prop_merchant_coin_coffer`, `prop_military_supply_chest`, `prop_mine_pit_prop_frame`, `prop_miners_iron_pickaxe`, `prop_monastic_reading_lectern`, `prop_mossy_stone_altar`, `prop_nested_brass_weights`, `prop_oak_spinning_wheel`, `prop_oak_tavern_barrel`, `prop_open_coin_chest`, `prop_ossuary_skull_niche`, `prop_pentagonal_iron_lantern`, `prop_plank_tavern_bench`, `prop_quench_trough_barrel`, `prop_roasting_spit_meat`, `prop_rocking_childs_cradle`, `prop_rope_tied_hay_bale`, `prop_rope_webbed_bed_frame`, `prop_salt_glazed_storage_jar`, `prop_shattered_pillar_drum`, `prop_slatted_pine_crate`, `prop_small_canvas_tent`, `prop_spice_drawer_cabinet`, `prop_split_rail_fence`, `prop_stacked_cheese_wheels`, `prop_standing_floor_loom`, `prop_stoneware_storage_crock`, `prop_tall_pine_cupboard`, `prop_tallow_candle_trio`, `prop_tallow_storage_tub`, `prop_tanning_stretcher_rack`, `prop_thick_cutting_board`, `prop_three_legged_cauldron`, `prop_three_legged_stool`, `prop_torch_wall_sconce`, `prop_treadle_grindstone`, `prop_two_wheel_hand_cart`, `prop_wall_tool_rack_kit`, `prop_weathered_grave_marker`, `prop_wicker_storage_basket`, `prop_wooden_counting_board`, `prop_wooden_dough_trough`, `prop_wooden_linen_press`, `prop_wooden_pack_frame`, `prop_wooden_peg_wall_shelf`
- **Items** (33): `item_amulet_ruby_pendant`, `item_ancient_scroll_map`, `item_antidote_potion_vial`, `item_blinding_powder_pouch`, `item_brass_skeleton_key`, `item_cheese_wedge_wheel`, `item_crusty_bread_loaf`, `item_cursed_relic_idol`, `item_fishing_rod_tool`, `item_frothy_ale_tankard`, `item_giant_strength_brew`, `item_gold_coin_stack`, `item_gold_coins`, `item_grilled_river_fish`, `item_guild_token_badge`, `item_health_potion_flask`, `item_hearty_stew_bowl`, `item_invisibility_draught`, `item_iron_lockpick_set`, `item_iron_longsword_blade`, `item_leather_boots_pair`, `item_mana_elixir_bottle`, `item_mining_pick_tool`, `item_oak_round_shield`, `item_red_apple_fruit`, `item_roast_meat_haunch`, `item_sealed_quest_letter`, `item_silver_coin_pouch`, `item_silver_ring_band`, `item_stamina_tonic_vial`, `item_steel_plate_helm`, `item_sweet_berry_pie`, `item_venom_coating_flask`
- **Race classes** (32): `raceclass_constructed_magic`, `raceclass_constructed_melee`, `raceclass_constructed_ranged`, `raceclass_constructed_science`, `raceclass_kal_magic`, `raceclass_kal_melee`, `raceclass_kal_ranged`, `raceclass_kal_science`, `raceclass_mor_magic`, `raceclass_mor_melee`, `raceclass_mor_ranged`, `raceclass_mor_science`, `raceclass_ondrek_magic`, `raceclass_ondrek_melee`, `raceclass_ondrek_ranged`, `raceclass_ondrek_science`, `raceclass_orenth_magic`, `raceclass_orenth_melee`, `raceclass_orenth_ranged`, `raceclass_orenth_science`, `raceclass_siann_magic`, `raceclass_siann_melee`, `raceclass_siann_ranged`, `raceclass_siann_science`, `raceclass_vaskaal_magic`, `raceclass_vaskaal_melee`, `raceclass_vaskaal_ranged`, `raceclass_vaskaal_science`, `raceclass_veth_magic`, `raceclass_veth_melee`, `raceclass_veth_ranged`, `raceclass_veth_science`
- **Herbs** (16): `herb_brightcap`, `herb_dreamroot`, `herb_emberthorn`, `herb_ghostfern`, `herb_gravebloom`, `herb_ironbark`, `herb_nightlace`, `herb_nullweed`, `herb_sorrows_ease`, `herb_stanchwort`, `herb_starfall_moss`, `herb_sunthread`, `herb_tidebloom`, `herb_veilthistle`, `herb_widows_thimble`, `herb_woundmoss`
- **Races (wave 2 representatives)** (14): `race2_constructed_representative`, `race2_kal_representative`, `race2_kal_wings_deployed`, `race2_kal_wings_folded`, `race2_mor_representative`, `race2_ondrek_representative`, `race2_orenth_representative`, `race2_race_lineup_silhouette`, `race2_siann_eyes_detail`, `race2_siann_representative`, `race2_vaskaal_hands`, `race2_vaskaal_representative`, `race2_veth_motion`, `race2_veth_representative`
- **Containers** (12): `container_courier_satchel`, `container_expedition_pack_large`, `container_mail_pigeon_cage`, `container_otherwrought_containment_box`, `container_potion_bandolier`, `container_quiver_arrow`, `container_sack_burlap`, `container_secure_evidence_vault`, `container_storage_crate`, `container_trade_crate_stack`, `container_warehouse_pallet`, `container_water_skin`
- **Mounts** (6): `mount_camel`, `mount_draft_ox`, `mount_pack_mule`, `mount_riding_horse`, `mount_sled_dog_team`, `mount_warhorse`
- **Weapons** (1): `weapon_hand_crossbow_pistol`
- **Animals** (1): `animal_pack_goat`
- **Resources** (1): `resource_oak_plank_bundle`
- **Race bodies (canonical)** (1): `racebody_siann_pose`

## 4. Where the pipeline stands

See `WAVE_0_NAMING_AND_HOST_ALLOCATION.md` for the host allocation, the naming migration
and the ASTRAL tunnel failure. In short: BEAST and ASTRAL build 3D on disjoint id
prefixes so they can share one `ready/` tree, and RAZER is a 2D-only host because
Trellis2 is core ComfyUI added in 0.34.0 and RAZER runs 0.33.0.
