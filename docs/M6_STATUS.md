# M6 status - Companion v1, and the content bible's four cells

**Date:** 2026-09-24. **Branch:** `claude/phase1`. **State:** in progress. Part 1, the layout reconciliation, and part 2, Quest 2, are done and verified (below); the companion and the §19 playable-prototype report are next. This document grows with them and is complete only when M6 is.

**Entry:** M5 complete (`0eb6247`): quests and the quest debugger work, and NPC state persists.

## The rulings, as applied

- **Companion v1 (execution prompt §18):** one companion with prototype behaviour - recruit through the world, follow, wait, catch up, the downed and death behaviour the prototype requires, save/load - and no affinity ladder, personal quest, full party, romance or advanced tactics; no permanent maximum party size in code. ROADMAP M6 was rewritten to this first.
- **The layout (the M3d ruling):** "keep working M3 geometry and record divergences; reconcile toward four-cell layout before M6 acceptance". The bible's Quest 2 and its acceptance route need its cells, so the reconciliation comes first, in its own commit.

## Part 1 - the content bible's four cells

| Cell | The bible (§2-§8) | Built |
|---|---|---|
| A, X 0-100, Z 100-200 | Ashen Hollow Waystation | The Ashen Waystone (27, 158) with the spawn beside it at (30, 158) facing east; Renn's lodge (M3's longhouse, moved whole) at (44, 128); Kera's smithy (M3's forge shed, moved whole, hearth and anvil inside) at (58, 142); Sel at her survey table (72, 122); the well (49, 151); the storage chest (38, 137); a fence leaving a narrow gap at the smithy's north-west corner (§26's camera test). A level terrace at 7.2 m |
| B, X 100-200, Z 100-200 | Charwood Verge | Tree clusters clear of the trail and the stream bed; the ash hound (128, 150); the ruined merchant cart with the hunting bow and 12 arrows (157, 162); the ash stand (180, 138); the Woundmoss patch (151, 128); the wolves' den at the north edge (134, 178) with its cache; the east pack's roamers (175, 175); the strays (118, 128). 4-7 m |
| C, X 0-100, Z 0-100 | Blackvein Cut | A shallow quarry: rim 8-10 m, floor 1.7-2.5 m, entered down a ramp from the road (64, 104) past the upper overlook (54, 74); the husk (48, 58); the animated armour (65, 34); the boar's wallow on the north-west rim (18, 72); the iron seam on the floor (28, 45); the blocked shaft (76, 18); rocks on the rim |
| D, X 100-200, Z 0-100 | Foldscar Ruin | A basin at 2.6 m; the three Quiet Stones on raised ground (5-7 m) at (150, 78), (122, 38) and (181, 31); the Foldscar's heart (153, 48); the spider (172, 48); the route round the spider (160, 65) - (187, 65) - (194, 45) - (181, 31) kept clear |

- **Terrain:** a 41 x 41 grid at 5 m, falling from 9.6 m at the north-west road ridge to the quarry floor's 1.7 m, so about 8 m of relief where `PROTOTYPE.md` §3 said at most 6 (the bible asks about 10). The stream is a bed without water in the greybox: the M3 water plane sits under the whole terrain now.
- **Places:** `location.outpost` is the waystation (55, 145), radius 30; `location.iron_shelf` became `location.blackvein_cut` (54, 74), radius 20, with an alias in the new `content/_aliases.yaml` so a save that discovered the shelf loads; the den moved with the wolves; `location.herb_patch` is the Woundmoss patch; new: `location.foldscar` (153, 48, radius 30) and `location.ruined_cart` (157, 162).
- **The starting kit** is the bible's §14: the sword in hand, a water flask and one salve. The bow is found at the cart (§11), not carried from the start.
- **Words:** Kera's lesson and "where", Renn's greeting, place and news lines, Sel's greeting, and Iron Under Ash's shelf step now name Blackvein Cut and the smithy; the quest's "return" is within 25 m of the waystation.
- **Saves:** the region's authored node moved, so the generator's fingerprint changed. `GameSession` registers a transition from M3's layout (its fingerprint a frozen constant) to the four cells: cells are rebased, a harvest record against the old seam is declared lost, and containers, creatures and created instances carry as they are (`ASaveFromTheM3Layout_LoadsOntoTheFourCells_LosingOnlyTheOldSeamsStrike`).
- **Tests and scripts:** every test that walked M3's places walks the bible's (the smithy and lodge moved whole, so their interiors translate exactly); tests that need a bow are handed one; the boar-charge tests run at the Foldscar's heart on level ground, and the charge's damage is asserted as the body region it lands on makes it; the behaviour matrix was regenerated (its only change is the five moved spawners). The smoke, the ui-shots and the perf run follow the new routes; the perf run's obstruction path goes through the lodge and the smithy's fence gap.

### Divergences kept from the bible

| The bible | Built | Why |
|---|---|---|
| No wolves | The prototype's den pack, strays and respawning pack kept, in Charwood | `PROTOTYPE.md` C10 and C12 are read against them; the owner can drop them |
| Kera's smithy smoke, a road with widths | Greybox boxes and bare terrain | Art is not a prototype criterion |
| Woundmoss as an optional herb | A discoverable place only | No herb node is built (M3f) |

## Part 2 - Quest 2, *The Three Quiet Stones* (bible §16)

| What | Built |
|---|---|
| The stones and the heart | The region's new `switches` (lint WLD013): the north, south-west and south-east Quiet Stones and the heart of the Foldscar, each on its rock, each setting a `world.foldscar.*` flag in the Foldscar's cell, once. The heart requires the three stones and until then refuses in its own words. `InteractionSystem` works them, measured from the body like a door; the event is `SwitchSet` |
| The fold | The region's new `barriers` (lint WLD014): a 3 m circle round Tavar at the bible's recovery position (145, 42). It blocks movement, blows and sight like a closed door until the heart is steadied - so he cannot be spoken to before (the talk reach is 1.95 m) |
| Tavar Orr | `npc.ashen_hollow.tavar_orr`, standing in the fold, and his conversation: every line is spoken by a man already free, and hearing the first ends the quest. Recruiting him is part 3 |
| The quest | `quest.ashen_hollow.three_quiet_stones`: learn of Tavar from Sel -> reach the Foldscar -> the three stones in any order (three `world_state` objectives joined by the next) -> steady the heart -> speak to Tavar. 150 XP and Sel's trust +10. No objective asks for a fight |
| Sel | Asked about the ruin, she tells of Tavar (the reply starts the quest) and guesses at the stones - and warns of the spider: "walk past it, don't run". Told that Tavar is free, she thanks you; that reply starts the quest too, so a Foldscar steadied before she said a word still completes it at once (bible §32) |
| Dialogue | A `visited` condition may name a line of another conversation (`dialogue_ref`): Sel knows whether Tavar has been spoken to |
| Presentation | Prompts from the content ("[E] Turn the north Quiet Stone", "[E] Steady the heart of the Foldscar"); a switch's words as a toast; a pale band on a stone in line; the fold as a faint violet haze that goes when it lifts, with its own words at its edge |
| The quest debugger | A `world_state` objective lists the switches that set its flag, where they stand, and what they still wait on |

The non-kill requirement (bible §8, §16) is a test: `TheThreeQuietStones_PlaysEndToEnd_WithTheSpiderAlive_AndPaysOnce` plays the quest in the world with every creature in it, reaching the south-east stone by the bible's route round the spider, which keeps beyond its 14 m hearing; no blow is struck and the spider lives. `AFoldscarSteadiedBeforeTheQuest_CountsAtOnce_AndSelHearsTavarIsFree` is §32's order.

## Verification so far

- Part 1: `dotnet test` (from `src/`) 635 passed; content lint 94 definitions, 0 errors.
- Part 2: `dotnet test` **647 passed**, 0 failed (Architecture 14, Content 131, Domain 142, EntityRegistry 23, World 60, Persistence 146, Application 131); content lint 101 definitions, 0 errors.
- Godot 4.7.2 headless smoke `PASS` on the new layout: the lodge door, Sel's book at her table, the quest from Kera at the smithy, the quicksave's identical digest; since part 2 it places four NPCs.
- The windowed `--ui-shots` run (ASTRAL) plays the whole M3-M5 journey on the new layout and exits 0: Renn in the lodge, Sel at her table outside it (`sel.png`), the archetype gallery, the boar fought on Blackvein's rim, the seam struck on the quarry floor (`seam.png`), the stand cut in Charwood, home to the smithy for Iron Under Ash's billet, spear, trade and completion, the spear outside the smithy (`spear.png`: the lodge, Sel's table, the fence), a stray wounded, and the death at the den.

## Still to do in M6

- Tavar Orr, the companion (bible §17): recruit, follow, wait, catch up, fight, downed and death, save/load; C16.
- The §19 playable-prototype report: C17's death evidence, a recorded fresh-session run, §7.4's field-by-field save comparison as a diff, the README, the replayable 10-minute scripted playthrough. The 1080p/60 FPS evidence needs the RAZER window.
