# M7 build shots

Content 0.3.0 (sha256:7a983b38017265d17434974bb711c094363b74cc43db63d7116b23485e961bc8); world seed 0x0A5E202609240001; from the committed start save S0 at tick 0; slice E9; one tick a frame, the real command path.

| Tick | What happened |
|---|---|
| 30 | b02: 576 nodes within 3 m of (100, 100), columns 388-411 and rows 388-411, in 4 tiles, all walkable, none twice |
| 30 | **b02_grid_corner**: F2's navigation stage where the four cells meet at (100, 100), from S0: walkable on both sides of both seams, no gap, no row twice (in 30 ticks) ![b02_grid_corner](b02_grid_corner.jpg) |
| 89 | b03: the outline shown; the ghost Allowed; "Timber Pad: can be built here - 1 Rough Timber (45 carried)" |
| 89 | ![b03_build_mode](b03_build_mode.jpg) |
| 95 | ![b03_help](b03_help.jpg) |
| 96 | **b03_build_mode**: Build mode at (102, 102): the area outlined, the pad's ghost allowed over square (33, 33), its cost in words; F1's BUILDING section (in 66 ticks) |
| 97 | `PiecePlaced` piece.pad.timber at (100500, 100500) r0, sequence 1 |
| 98 | `PiecePlaced` piece.pad.timber at (103500, 100500) r0, sequence 2 |
| 99 | `PiecePlaced` piece.pad.timber at (100500, 103500) r0, sequence 3 |
| 100 | **b04_pads**: Four pads over the four-cell corner: sequence 1-4, the (33, 33) pad hosted in c_01_01, no rebuild (in 4 ticks) |
| 100 | `PiecePlaced` piece.pad.timber at (103500, 103500) r0, sequence 4 |
| 101 | `NavigationRebuilt` 108 nodes |
| 101 | `PiecePlaced` piece.doorway.timber at (100500, 99000) r0, sequence 5 |
| 102 | `NavigationRebuilt` 108 nodes |
| 102 | `PiecePlaced` piece.wall.timber at (103500, 99000) r0, sequence 6 |
| 103 | `NavigationRebuilt` 108 nodes |
| 103 | `PiecePlaced` piece.wall.timber at (100500, 105000) r0, sequence 7 |
| 104 | `NavigationRebuilt` 108 nodes |
| 104 | `PiecePlaced` piece.wall.timber at (103500, 105000) r0, sequence 8 |
| 105 | `NavigationRebuilt` 108 nodes |
| 105 | `PiecePlaced` piece.wall.timber at (99000, 100500) r1, sequence 9 |
| 106 | `NavigationRebuilt` 108 nodes |
| 106 | `PiecePlaced` piece.wall.timber at (99000, 103500) r1, sequence 10 |
| 107 | `NavigationRebuilt` 108 nodes |
| 107 | `PiecePlaced` piece.wall.timber at (105000, 100500) r1, sequence 11 |
| 108 | `NavigationRebuilt` 108 nodes |
| 108 | `PiecePlaced` piece.wall.timber at (105000, 103500) r1, sequence 12 |
| 128 | **b05_edges**: The doorway across x = 100 and seven walls: sequence 5-12, exactly 8 rebuilds; seen from 9 m (CameraRig.Cap) (in 28 ticks) ![b05_edges](b05_edges.jpg) |
| 129 | `PiecePlaced` piece.roof.timber at (100500, 100500) r0, sequence 13 |
| 130 | `PiecePlaced` piece.roof.timber at (103500, 100500) r0, sequence 14 |
| 131 | `PiecePlaced` piece.roof.timber at (100500, 103500) r0, sequence 15 |
| 132 | `PiecePlaced` piece.roof.timber at (103500, 103500) r0, sequence 16 |
| 133 | `NavigationRebuilt` 72 nodes |
| 133 | `PiecePlaced` piece.door.timber at (100500, 99000) r0, sequence 17 |
| 153 | **b06_roofs_and_door**: Four roofs, sequence 13-16 with no rebuild; the door hung in the doorway, sequence 17, one rebuild, shut: 17 pieces for 25 timber so far (in 25 ticks) ![b06_roofs_and_door](b06_roofs_and_door.jpg) |
| 154 | `NavigationRebuilt` 56 nodes |
| 154 | `PiecePlaced` piece.station.anvil at (100500, 103500) r3, sequence 18 |
| 155 | `NavigationRebuilt` 56 nodes |
| 155 | `PiecePlaced` piece.storage.chest at (103500, 103500) r0, sequence 19 |
| 156 | Step 1: 19 pieces, 31 timber spent, sequence 19 |
| 175 | **b07_bench_and_chest**: The bench across x = 100 and the chest in the north-east square, sequence 18 and 19: step 1 is 19 pieces for 31 timber, their IDs derived 1-19 (in 22 ticks) ![b07_bench_and_chest](b07_bench_and_chest.jpg) |
| 206 | **b08_overlap_refused**: A wall on the doorway's edge: refused in words - the ghost and the toast say why - and nothing changes (in 31 ticks) ![b08_overlap_refused](b08_overlap_refused.jpg) |
| 222 | `DoorToggled` pce_000000000HXJZ41QX3ADRZAGGV open by chr_01M3ECYY5671G34HVWKQXPQ463 |
| 240 | `DoorToggled` pce_000000000HXJZ41QX3ADRZAGGV shut by chr_01M3ECYY5671G34HVWKQXPQ463 |
| 285 | R12: stopped at z 98450 by the shut door, 40 ticks |
| 286 | `DoorToggled` pce_000000000HXJZ41QX3ADRZAGGV open by chr_01M3ECYY5671G34HVWKQXPQ463 |
| 303 | R14: the aim west from (100.5, 101.0) stops at x 99201 |
| 323 | **b09_door_and_inside**: The door opened from inside and shut from outside; the walk north stopped by the shut leaf; opened with E; the aim from inside stops at the west wall (in 117 ticks) ![b09_door_and_inside](b09_door_and_inside.jpg) |
| 334 | **b10_craft_at_home**: The March Spear made at the placed bench, 1.36 m from its site, no authored anvil in reach (in 11 ticks) |
| 335 | R15: the March Spear made 1360 mm from the bench's site |
| 366 | `DoorToggled` pce_000000000HXJZ41QX3ADRZAGGV shut by chr_01M3ECYY5671G34HVWKQXPQ463 |
| 842 | `DoorToggled` door.forge_shed open by chr_01M3ECYY5671G34HVWKQXPQ463 |
| 896 | `WorkerAssigned` npc.ashen_hollow.kera_voss: to (100750, 103500) facing 270000 |
| 897 | `RoutePlanned` npc.ashen_hollow.kera_voss: found, none, 7 corners, 22685 expansions |
| 897 | Kera's host cell: r_0_0:c_00_01 |
| 950 | `DoorToggled` door.forge_shed shut by chr_01M3ECYY5671G34HVWKQXPQ463 |
| 982 | `DoorToggled` door.forge_shed open by npc_00000000016AR2TTGP8A46K14V |
| 1009 | `RoutePlanned` npc.ashen_hollow.kera_voss: found, off_line, 5 corners, 22650 expansions |
| 1066 | `RoutePlanned` npc.ashen_hollow.kera_voss: found, off_line, 4 corners, 21590 expansions |
| 1483 | **b11_assign**: The door shut from outside; round to the forge shed, its door opened; Kera asked at the bench; the shed door shut behind; away to the vantage (in 1149 ticks) |
| 1789 | Kera's host cell: r_0_0:c_00_00 |
| 1817 | `RoutePlanned` npc.ashen_hollow.kera_voss: found, off_line, 2 corners, 128 expansions |
| 1834 | `DoorToggled` pce_000000000HXJZ41QX3ADRZAGGV open by npc_00000000016AR2TTGP8A46K14V |
| 1835 | `RoutePlanned` npc.ashen_hollow.kera_voss: found, off_line, 2 corners, 23 expansions |
| 1838 | Kera's host cell: r_0_0:c_01_00 |
| 1839 | b12: Kera in the doorway at (100030, 98628) |
| 1839 | ![b12_kera_walks](b12_kera_walks.jpg) |
| 1840 | **b12_kera_walks**: Kera on her way, opening both doors: F2's navigation stage as she comes into the workshop's doorway (in 357 ticks) |
| 1857 | Kera's host cell: r_0_0:c_01_01 |
| 1907 | `NpcArrivedAtWork` npc.ashen_hollow.kera_voss |
| 1908 | b13: L 80366 mm; arrived in 1011 ticks (bound 1296); worst step 81 mm; nearest the character 1798 mm; deepest contact 0.483 mm |
| 1927 | **b13_kera_at_work**: Kera at the bench: exactly on its work anchor, facing it, within the arrival bound; in, through and round, as criterion 14 says (in 87 ticks) ![b13_kera_at_work](b13_kera_at_work.jpg) |
| 1976 | `PiecePlaced` piece.pad.timber at (100500, 97500) r0, sequence 20 |
| 1977 | `NavigationRebuilt` 108 nodes |
| 1977 | `PiecePlaced` piece.wall.timber at (99000, 97500) r1, sequence 21 |
| 1978 | `NavigationRebuilt` 108 nodes |
| 1978 | `PiecePlaced` piece.wall.timber at (102000, 97500) r1, sequence 22 |
| 1989 | R24: the vestibule refused - "that would cut Kera Voss's work place off" |
| 2008 | ![b14_vestibule](b14_vestibule.jpg) |
| 2009 | `NavigationRebuilt` 108 nodes |
| 2009 | `PieceRemoved` piece.wall.timber, refund 1, sequence 23 |
| 2010 | `NavigationRebuilt` 108 nodes |
| 2010 | `PieceRemoved` piece.wall.timber, refund 1, sequence 24 |
| 2011 | **b14_vestibule**: South of the door, a pad and two walls; the wall that would close the vestibule refused as unnavigable - the red ghost and the toast say why - then all taken down for 1, 1 and 0 timber (in 84 ticks) |
| 2011 | `PieceRemoved` piece.pad.timber, refund 0, sequence 25 |
| 2159 | `PieceDamaged` piece.wall.timber -10 (melee), now 190 |
| 2179 | `PieceDamaged` piece.wall.timber -10 (melee), now 180 |
| 2199 | `PieceDamaged` piece.wall.timber -10 (melee), now 170 |
| 2210 | `PieceRepaired` 170 -> 200 |
| 2220 | `PieceDamaged` piece.wall.timber -10 (melee), now 190 |
| 2241 | b15: the target line "Timber Wall 190/200 - [T] mend   [Z] take down" |
| 2261 | ![b15_blows_and_mending](b15_blows_and_mending.jpg) |
| 2262 | **b15_blows_and_mending**: Round the east side to the north wall: three blows (170), mended with T for one timber (200), a fourth blow (190); its target line in words (in 251 ticks) |
| 2473 | `PieceDamaged` piece.storage.chest -10 (melee), now 90 |
| 2493 | `PieceDamaged` piece.storage.chest -10 (melee), now 80 |
| 2513 | `PieceDamaged` piece.storage.chest -10 (melee), now 70 |
| 2533 | `PieceDamaged` piece.storage.chest -10 (melee), now 60 |
| 2553 | `PieceDamaged` piece.storage.chest -10 (melee), now 50 |
| 2573 | `PieceDamaged` piece.storage.chest -10 (melee), now 40 |
| 2593 | `PieceDamaged` piece.storage.chest -10 (melee), now 30 |
| 2613 | `PieceDamaged` piece.storage.chest -10 (melee), now 20 |
| 2633 | `PieceDamaged` piece.storage.chest -10 (melee), now 10 |
| 2653 | `NavigationRebuilt` 56 nodes |
| 2653 | `PieceDestroyed` piece.storage.chest (melee), sequence 26 |
| 2684 | ![b16_chest_cycle_and_spill](b16_chest_cycle_and_spill.jpg) |
| 2685 | **b16_chest_cycle_and_spill**: In by the doorway to the chest: two timber stored, taken back, stored again under one derived ID; ten blows destroy it, and the two timber lie where it stood, picked up (in 423 ticks) |
| 2703 | `NavigationRebuilt` 56 nodes |
| 2703 | `PiecePlaced` piece.storage.chest at (103500, 100500) r1, sequence 27 |
| 2787 | `PiecePlaced` piece.pad.timber at (97500, 100500) r0, sequence 28 |
| 2788 | `PiecePlaced` piece.pad.timber at (97500, 103500) r0, sequence 29 |
| 2789 | `NavigationRebuilt` 108 nodes |
| 2789 | `PiecePlaced` piece.wall.timber at (96000, 100500) r1, sequence 30 |
| 2815 | `NavigationRebuilt` 108 nodes |
| 2815 | `PiecePlaced` piece.wall.timber at (96000, 103500) r1, sequence 31 |
| 2907 | `WorkerReleased` npc.ashen_hollow.kera_voss: released |
| 2908 | `RoutePlanned` npc.ashen_hollow.kera_voss: found, none, 6 corners, 40639 expansions |
| 2952 | Kera's host cell: r_0_0:c_01_00 |
| 2969 | `RoutePlanned` npc.ashen_hollow.kera_voss: found, off_line, 6 corners, 40485 expansions |
| 2971 | Kera's host cell: r_0_0:c_00_00 |
| 3056 | Kera's host cell: r_0_0:c_00_01 |
| 3227 | **b17_route_west**: A second chest with two timber in it; west of the workshop: two pads and a wall, the second refused while the character stands on its line, then placed; Kera let go, her way home round the new line (in 542 ticks) ![b17_route_west](b17_route_west.jpg) |
| 3235 | `RoutePlanned` npc.ashen_hollow.tavar_orr: found, none, 4 corners, 1873 expansions |
| 3254 | **b18_companion_in**: Tavar told to follow from inside: he plans a route round to the doorway (in 27 ticks) ![b18_companion_in](b18_companion_in.jpg) |
| 3258 | `RoutePlanned` npc.ashen_hollow.tavar_orr: found, off_line, 3 corners, 806 expansions |
| 3294 | Saved to quick at tick 3294, Tavar's route Active; digest `sha256:cb799fd46b5b597cf46a94575279972615ab8b85dee9ec174ac21d1df1eb2ba9` |
| 3323 | `RoutePlanned` npc.ashen_hollow.tavar_orr: found, off_line, 2 corners, 55 expansions |
| 3803 | `RoutePlanned` npc.ashen_hollow.kera_voss: found, off_line, 3 corners, 38 expansions |
| 3894 | 600 ticks on: Tavar at (100754, 99891); digest `sha256:14cb3be18a736e2ae0375de093433b54f5b500a6a9312d5228b1fa070bf518a4` |
| 3894 | **b19_save**: Saved to quick with Tavar on his route and Kera walking home; 600 ticks on he is inside, having come through the doorway (in 640 ticks) |
| 3929 | `NpcReturnedHome` npc.ashen_hollow.kera_voss |
| 3949 | **b20_kera_home**: Kera home: exactly at her place, facing its way, the errand retired (in 55 ticks) ![b20_kera_home](b20_kera_home.jpg) |
| 3950 | Done: 19 beats; subscriber failures 0; piece art fallbacks (piece:*) 7 of 7 piece entries |
