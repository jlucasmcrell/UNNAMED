# M6 status - Companion v1, and the content bible's four cells

**Date:** 2026-09-24. **Branch:** `claude/phase1`. **Closed out 2026-09-24** (owner-authorised; "Phase-1 closeout" at the end): merged to `main`, the asset library integrated, a Windows playtest build. **State:** complete except the RAZER performance window; the owner's playtest came back, its delta is applied ("Owner Playtest Delta" below), and M6 is stopped again for the owner (execution prompt §19). Part 1 is the layout reconciliation, part 2 Quest 2, part 3 the companion, part 4 the playable-prototype report; the exit criteria are taken one by one at the end.

**Entry:** M5 complete (`0eb6247`): quests and the quest debugger work, and NPC state persists.

## The rulings, as applied

- **Companion v1 (execution prompt §18):** one companion with prototype behaviour - recruit through the world, follow, wait, catch up, the downed and death behaviour the prototype requires, save/load - and no affinity ladder, personal quest, full party, romance or advanced tactics; no permanent maximum party size in code. ROADMAP M6 was rewritten to this first.
- **The layout (the M3d ruling):** "keep working M3 geometry and record divergences; reconcile toward four-cell layout before M6 acceptance". The bible's Quest 2 and its acceptance route need its cells, so the reconciliation comes first, in its own commit.

## Part 1 - the content bible's four cells

| Cell | The bible (§2-§8) | Built |
|---|---|---|
| A, X 0-100, Z 100-200 | Ashen Hollow Waystation | The Ashen Waystone (27, 158) with the spawn beside it at (30, 158) facing east; Renn's lodge (M3's longhouse, moved whole) at (44, 128); Kera's smithy (M3's forge shed, moved whole, hearth and anvil inside) at (58, 142); Sel at her survey table (72, 122); the well (49, 151); the storage chest (38, 137); a fence leaving a narrow gap at the smithy's north-west corner (§26's camera test). A level terrace at 7.2 m |
| B, X 100-200, Z 100-200 | Charwood Verge | Tree clusters clear of the trail and the stream bed; the ash hound (128, 150); the ruined merchant cart with the hunting bow and 12 arrows (157, 162); the ash stand (180, 138); the Woundmoss patch (151, 128); the wolves' den in the north-west corner (112, 184) with its cache, and the east pack's roamers on the north-east tree line (188, 180) - both moved there in part 4, clear of the bible's acceptance path; the strays (118, 128). 4-7 m |
| C, X 0-100, Z 0-100 | Blackvein Cut | A shallow quarry: rim 8-10 m, floor 1.7-2.5 m, entered down a ramp from the road (64, 104) past the upper overlook (54, 74); the husk patrolling the floor round (48, 58); the animated armour (65, 34); the boar's wallow on the north-west rim (18, 72); the iron seam on the floor (28, 45); the blocked shaft (76, 18); rocks on the rim |
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

## Part 3 - Tavar Orr, the companion (bible §17; execution prompt §18)

| What | Built |
|---|---|
| Recruiting | Through the world: once freed, Tavar is asked to walk back with the character (his conversation's `recruit_companion`). He can join because his NPC definition names how he fights (`companion`: 100 health, the March Spear, a leather jerkin) |
| Orders | Follow and wait - G for every companion on their feet, or a word in his conversation (`order_companion`); no radial menu (bible §9). An order to someone downed or not with the character is refused with a reason |
| Following | He walks the character's trail - a mark every metre, straight for the farthest mark in clear view - walking, running or sprinting by how far behind he is, and stands within 2.5 m |
| Catching up | More than 30 m behind, or no headway for 4 s, he is put down near the character: on the trail 2-6 m behind them in sight of them, or on a ring behind them (`CompanionCaughtUp`) |
| Waiting | He stands where he was told, fighting only what comes within 4 m |
| Fighting | He fights creatures engaged with the character within 10 m of him and 16 m of the character, with the spear's own numbers and none of the character's. An engaged creature turns on him when he stands nearer than the character by more than a metre (never with a charge); its blows land on him through the same combat rules. His kills earn the character no XP and count for no quest |
| Downed and death | At 0 health he is downed: he lies down, cannot talk, and says how long he has. Within reach, E helps him up with 40% health. Left 60 s, he falls, and is back at the Ashen Waystone, whole and waiting - the bible's §18 fiction, his share of it. He mends out of a fight |
| Saves | Schema 12: the player section holds the roster - order, condition, position and facing, health, and what his next ticks depend on (when he went down, how long without headway, when he last fought, the trail he is walking). The schema-11 player shape frozen; v12 fixture (the warden following Aelin mid-trail, renamed on load) |
| Presentation | The companion HUD (bible §19: name, how he is in words, what he is doing, the G hint, and while downed how long he has); toasts; his figure walks and lies down; the prompt "[E] Help Tavar Orr up" |

C16 (recruit, follow -> wait -> follow, path around the lodge with no snag over 15 s) is `ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds` - into the lodge through its door, round Renn and out, and round the building: he never stood held while behind, and never needed catching up - with `FollowWaitFollow_HeKeepsHisPlaceWhileWaiting_AndComesWhenCalled` and `Tavar_JoinsWhenAsked_AndFollows`. The companion-state row of `PROTOTYPE.md` §6.2 is `LeftFarBehind_HeCatchesUp_ToASpotNearTheCharacter`, `Downed_HeCannotTalk_ButCanBeHelpedUp`, `DownedTooLong_HeFalls_AndIsBackAtTheWaystone_Waiting`, `HeFightsWhatHuntsTheCharacter_AndItFightsHim` and `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame` (§7.4: "exists, is at the same position, in wait", then the same world on both sides of the load).

### Defaults taken (each flippable)

- His kills earn the character nothing and count for no quest.
- His death is not permanent: he falls and waits at the Ashen Waystone.
- The character passes through him, so he never holds a narrow door shut; creatures do not.
- G orders every companion at once; there is one in Phase 1, and nothing caps the roster.

## Part 4 - the playable-prototype report (execution prompt §19; `PROTOTYPE.md` §9)

The evidence is in `docs/acceptance/m6/`: the transcripts, the field-by-field state and its diff, the command logs and a JPEG per beat. The videos of both runs (`playthrough.mp4`, 29 MB, and `relaunch.mp4`, 3 MB, 1920x1080 at 20 fps - real time) are kept outside the public repository, on ASTRAL at `G:\UNNAMED_HISTORY\acceptance_final\`.

| §19 item | Evidence | Result |
|---|---|---|
| A recorded fresh-session acceptance run (`PROTOTYPE.md` §9 item 1) | `--playthrough <dir>`: a fresh profile and a fixed seed play the content bible's exact acceptance path (§31) through the same commands the keys send - the waystation chest, Kera and Iron Under Ash, the hound (fought), the cart's bow, an ash haft, Blackvein Cut and its Bone Walker (fought), ore, the billet and the spear, Kera shown it, Sel's primer and Tavar's story, the three Quiet Stones (the south-east one by the way round the spider), the heart, Tavar freed and recruited, home with him following, Wards past tolerance, a save - and quits (§30). Recorded: `transcript.md` (every beat and event with its game time and tick), 21 screenshots, the video | Every beat happened, in 6:04 of game time; both quests complete; no pathing intervention for Tavar on the way home (no catch-up was needed) |
| Save/reload compared field by field, as a diff (§9 item 2; bible §33) | `state_saved.json` written just before the save, the application quit, relaunched with `--playthrough-verify`, the save loaded, `state_loaded.json` written, and the two walked leaf by leaf (`StateDump`): `state_diff.txt` | **412 fields compared, 0 differences**; the state digest after the load equals the one at the save. The saved state touched everything §33 asks: progression changed (level 3), the bow acquired, the spear made and in hand, ore consumed, two containers changed, seven creature records, the four Foldscar switches set, Tavar recruited and following, Health 112/120, Strain 40, and the character 28 m from the spawn |
| Death and respawn, the penalty exactly once (C17) | The relaunch tells Tavar to wait, walks to the wolves' den and stands until the pack kills the character: `transcript_relaunch.md` | One death (the Grey Wolf's bite); XP debt 0 before, +78 added, 78 after - "the penalty applied exactly once"; returned at the Ashen Waystone, weakened |
| The replayable scripted playthrough (§9 item 6) | The run is one tick a frame from a fixed seed, so running it again replays it: the same build, a fresh profile, `--playthrough` again. `state_replay.json` names instance IDs by order of appearance (they are fresh every game, D-04) | The replay's `state_replay.json` is byte-identical to the recorded run's (SHA-256 `5DBE95FB...`); its transcript matches line for line and tick for tick, all but the raw state digest, which covers the fresh instance IDs. It is under ten minutes: the bible's first ten minutes and its whole §31 path both fit in a little over six |
| The Phase-1 README (§9 item 5) | `README.md`'s status brought up to date, and a new section: build and run the headless tests, validate the content, play, the controls, and the scripted checks, in under a page | Done - the owner's own README, so the change is kept to those two places |
| Performance evidence from the §8 gate (§9 item 4) | Needs the owner's clean RAZER window (1080p, sustained 60 fps on the RTX 4070 Ti with OBS, H3 and other GPU loads stopped); the capture is `godot --path src/Presentation -- --perf`. A trial on ASTRAL (RTX 5090, not the gate) after the fix below: average 798-840 FPS and 1% lows 192-213 FPS per segment, p99 frame 3.5-4.1 ms, GPU p99 0.21 ms, no hitch over 33 ms after the warm-up, VRAM 181 MB, working set 671 MB; the character struck 0 times | **Measured on RAZER 2026-09-25 and accepted by the owner as the Phase-1 baseline** (`PHASE1_TECHNICAL_CLOSEOUT.md`). The 2x2 km greybox is measurement only, not the D-01 revisit gate, and is still to run |

### What the acceptance run found, and what changed

- **Tavar's "Sel sent me" vanished a moment after his first line.** Hearing that line completes Quest 2, and the reply asked for the quest to be active; a player who paused before answering lost the way to recruit him. It now asks whether Sel ever gave the quest; `TheThreeQuietStones_PlaysEndToEnd...` waits before answering, as a regression test.
- **The wolves sat on the bible's path.** The east pack's patrol route was still on M3 coordinates, now through the middle of Charwood between the cart and the ash stand, and the den I had placed at Charwood's north edge watched the way to the cart; the scripted character was killed twice. The den (its rocks, cache, mouth and pack) moved to Charwood's north-west corner (112, 184) and the patrol to its north-east edge; the tests that walk to the den walk there.
- **Three items could not be got in a fresh session** (C18, which no test checked before): the hide cap (the unbuilt lost-token quest's reward), the ashbloom herb (no herb node since M3f) and Halda's token. The cap is in Kera's wares beside the vest, dried ashbloom in the waystation's storage chest, and the token back in the den cache where the prototype's quest put it. `ReachabilityTests` now proves every item, creature and node can be reached.
- **The death recap said "You wake at the outpost".** It says the bible's "You return at the Ashen Waystone".

### Found after the report (the overnight re-check), and fixed

- **The Bone Walker patrolled the wrong cell.** Part 1 moved the husk's spawn to the bible's (48, 58) but left its patrol route on M3's iron shelf, which after the move runs just north of the waystation: the husk walked out of Blackvein, so the recorded run met no Bone Walker at all (the bible's 7:00 beat) and saved it at (73, 169). It now patrols the quarry floor round (48, 58), between the ramp's foot and the iron vein. The acceptance run was recorded again: the character fights and kills it on the way to the seam (2:16), and every number above is from that run. Patrol routes are not part of a cell's baseline, so a save from before the fix loads as it did (the baseline hashes are unchanged). This was the second patrol the layout move stranded (the east pack's was the first), so `CombatContentTests.EverySpawnersPatrol_StaysOnItsLeash` now refuses a patrol point farther from its spawn than the 40 m leash, beyond which a creature gives up a fight; it fails on the old route.
- **The performance capture measured a fight.** Its walk (M6 part 1's loop) passed 4 m from the ash hound's home, and the hound chased and bit the character through the whole capture (in a trial on ASTRAL the character was down to 74/120 by the last segment); a death would have teleported the walker and broken the route in the owner's RAZER window. The walk now keeps at least 45 m from the hound's home and 20-30 m beyond every other creature's sight, patrol or ground, runs the obstruction segment first, and joins the four-cell loop where the two loops meet rather than by a straight line across the buildings. The summary's new `route` note counts the waypoints each segment reached and every blow that reached the character; the trial above is clean (0 blows, 0 deaths).
- **The capture's process time misread Godot's monitor.** That monitor holds the worst frame's process time of the last second and refreshes once a second; the capture sampled it every frame, so each second's worst frame counted about 800 times, and a screenshot's stall (the tool's, already left out of the frame times) came back as a 40-57 ms p99. `summary.json` now gives `process_ms_worst_each_second`: one figure a second, without the second a screenshot falls in. On ASTRAL it reads 8-9 ms typically; one run's third-person segment held one real 50 ms frame, which its frame times show too (`hitches_over_33_ms`: 1).

### `PROTOTYPE.md` §7.3, criterion by criterion

| | Evidence | |
|---|---|---|
| C9 walk to the den and back, never stuck | The playthrough crosses all four cells and the relaunch walks to the den; `SessionTests`, the smoke | Met |
| C10 kill the den pack with sword, bow and all three spells, each needed | The M3f finding stands: a level-2 character with a standard March Spear clears the den at 85/120 health, so no spell is needed | Was **Open** - the owner's tuning call (overnight report item 2). **Revised by owner ruling (2026-09-24):** the spear is not weakened; C10 now asks that melee, ranged combat and each proof formula work and be meaningfully useful in a fight that suits them, and no single encounter must force every tool (`PROTOTYPE.md` C10). Each works in its tests (the bolt's blow, the ward's armour, the mending's heal and cleared bleed) and in the runs below; how useful each feels is the playtest's judgement |
| C11 level 2 from the quest alone | Iron Under Ash pays 120 XP, enough alone for level 2 (`QuestTests`); in the playthrough the kills get there first (level 2 at the Bone Walker, 3 with Quest 2) | Met |
| C12 both resources, the daily one refills, the seam does not | `CraftingTests`; the playthrough gathers both | Met |
| C13 both recipes, exact counts | `CraftingTests`; the playthrough makes both | Met |
| C14 quality on the instance | `CraftingTests.AFineSpear_HitsHarder_AndACrudeOneSofter` (M3f's reading) | Met |
| C15 the quest, tracked | Both of the bible's quests in the playthrough, the journal and tracker on screen | Met |
| C16 the companion | Part 3's tests; Tavar home with the character in the playthrough | Met |
| C17 death, the penalty once | The relaunch; `CombatTests.Dying_OwesXpDebt_Once...` | Met |
| C18 everything reachable | `ReachabilityTests` | Met (after the changes above) |

## Verification so far

- Part 1: `dotnet test` (from `src/`) 635 passed; content lint 94 definitions, 0 errors.
- Part 2: `dotnet test` **647 passed**, 0 failed (Architecture 14, Content 131, Domain 142, EntityRegistry 23, World 60, Persistence 146, Application 131); content lint 101 definitions, 0 errors.
- Part 3: `dotnet test` **670 passed**, 0 failed (Architecture 14, Content 136, Domain 147, EntityRegistry 23, World 60, Persistence 150, Application 140); content lint 102 definitions, 0 errors.
- Part 4: `dotnet test` **675 passed**, 0 failed (Architecture 14, Content 136, Domain 147, EntityRegistry 23, World 60, Persistence 150, Application 145); content lint 102 definitions, 0 errors. The acceptance playthrough, its relaunch and its replay as above.
- The overnight re-check: `dotnet test` **676 passed**, 0 failed (Content 137, with the patrol guard); lint 0 errors; the headless smoke `PASS`; the acceptance run recorded again, relaunched (412 fields, 0 differences) and replayed (byte-identical); the perf capture on ASTRAL clean (above); the windowed `--ui-shots` run exits 0 with the husk on the quarry floor.
- Godot 4.7.2 headless smoke `PASS` on the new layout: the lodge door, Sel's book at her table, the quest from Kera at the smithy, the quicksave's identical digest; since part 2 it places four NPCs.
- The windowed `--ui-shots` run (ASTRAL) plays the whole M3-M5 journey on the new layout and exits 0: Renn in the lodge, Sel at her table outside it (`sel.png`), the archetype gallery, the boar fought on Blackvein's rim, the seam struck on the quarry floor (`seam.png`), the stand cut in Charwood, home to the smithy for Iron Under Ash's billet, spear, trade and completion, the spear outside the smithy (`spear.png`: the lodge, Sel's table, the fence), a stray wounded, and the death at the den.

## Exit criteria, one by one (ROADMAP M6)

| Criterion | Status |
|---|---|
| C16: recruit Tavar, follow -> wait -> follow, around the lodge with no snag over 15 s | Met - part 3 |
| The companion-state tests (follow/wait, catch-up, downed, death and revive, the behaviour state saved) green | Met - part 3 |
| He walks the bible's acceptance route with the character without a pathing intervention | Met for the way home from the Foldscar (part 4); he joins at the Foldscar, so the route before it is the character's alone |
| His state round-trips through save/load field by field | Met - the companion test and the acceptance relaunch (0 differences) |
| Both of the bible's quests complete | Met - part 2's tests and the playthrough |
| Proof: a recorded acceptance log with zero pathing interventions; the companion round trip; the §19 report | Met - part 4 |
| The early feel test (3-5 blind testers) | **The owner's**, after the playtest - its transcript and findings (a null result included) are part of ROADMAP M6's proof, and it is required before Phase 2. **Deferred by owner ruling (2026-09-24)** until a nontechnical Windows playtest build exists; not an M7 entry blocker. Not run; no result recorded |
| M6 acceptance (bible §35): 1080p/60 FPS evidence | **Measured on RAZER 2026-09-25, accepted as the baseline** (`PHASE1_TECHNICAL_CLOSEOUT.md`) |

## Known deferrals

- The RAZER performance window (M3's and the bible's §34) - measured in the Phase-1 closeout on 2026-09-25 and accepted as the baseline.
- C10's balance - the spear against the den pack - was the owner's call; ruled 2026-09-24 (C10 revised, the spear unchanged).
- The early feel test, deferred by the same ruling until a nontechnical Windows playtest build exists.
- A herb node (the bible's Woundmoss) is not built; the ashbloom herb is found dried in the waystation chest.
- The wolves (den pack, strays, respawning pack) stay, a divergence from the bible the owner can drop.

## Owner Playtest Delta

**Date:** 2026-09-24. The owner played M6 and asked for a polish and fix pass - no rewrite of what works, no M7. The M6 evidence above stands as recorded; this pass's own evidence is in `docs/acceptance/m6_delta/`.

### Findings, and what was done

| # | The owner found | Done |
|---|---|---|
| 1 | The jump is too weak | The jump is now a real arc in the simulation (it was a cosmetic hop): the feet rise to 1.15 m in 0.42 s and fall as long, in integer millimetres, in the same step as movement, so it is deterministic and saved. Tuned on the hollow's own geometry rather than by a multiplier (`JumpAndCrouchTests`). From a run at the fallen timber (0.8 m tall, 0.8 m through), every take-off from about 1.8 m short of it up to pressed against it carries over (take-off ticks 14-38 of a 4 m run-up; earlier ones land short). Nothing 1.2 m or taller is cleared from any take-off: the smithy fence at a run or a sprint, the merchant cart (1.4 m), the lodge wall, the smithy's closed door, the fold. In the air, a structure no taller than the apex meets the tucked legs (a 0.2 m radius) rather than the shoulders (0.35 m), which is what lets a run carry over the timber; walls, doors and bodies always meet the full radius. Nothing is stood on, climbed or mantled, and no place needs a jump (`ReachabilityTests` unchanged). In the air the body cannot jump again, crouch or dodge (each refused with a reason), and it lands by itself on the terrain. The camera and the figure follow the arc in both views, and the figure tucks its legs |
| 2 | Add a basic crouch | X crouches and stands (C stays the dodge). Crouched: half pace whatever the gait (asking to sprint walks, and spends no stamina); the body is 1.15 m tall instead of 1.8. An overhang is a structure with a clearance (content `clearance_m`, lint-checked), and the hollow has one: a beam across two stones at the Woundmoss patch, 1.3 m clear, which a crouched body passes under and a standing one goes round. Standing up or jumping under it is refused, "No room to stand", until the body is clear. The dodge is refused while crouched. The eye comes down to 1.05 m in both views, the figure's hips drop and its knees bend (a placeholder pose), and the HUD says "Crouched". Perception: a crouched body's footfalls are a walk's (3 m), so a sleeper 5 m off sleeps on while the character creeps back and forth past it, even asking to run, and hears the same character run once stood (tested) - the only stealth effect. The stance is in the state (`Simulation.Posture`) and the save for perception and stealth to read later |
| 3 | Aiming feedback | **The reticle** shows while a bow is drawn or aimed (right mouse held) and while a thrown working (Impulse Bolt) is in its tell, and always in first person with a bow in hand. It is drawn at the point the simulation says the shot would stop - a creature, a wall, or the end of its range - by the same trace the shot resolves with (`Simulation.Aim`), so off the shoulder it sits where the arrow will land, not at the screen's middle; it turns red over a creature. **Every loosed arrow and thrown working is published** (`ShotLoosed`: from, to, what it struck) and drawn along that path. The arrow flies with a pale streak behind it (it flies straight away from a shoulder camera, and is a speck without one) and sticks point-first where it stopped - a wall, a tree, the ground at the end of its range - for 10 s, or vanishes into a creature with a spark. The Impulse Bolt flies as DeepSeek's `vfx.force.impulse_bolt_travel` flipbook with its light and bursts as `vfx.force.impulse_bolt_impact`, a little short of the wall so the burst is not half inside it; a glowing sphere and a flash stand in without the asset workspace. The effect IDs come from the formula's ID by convention, so no content ID is in code. The hit still resolves at release, as before; the flight is drawn after it at 55 m/s (arrow) and 38 m/s (working). **No trajectory line in play:** F3's debug overlay alone draws one, from the body to the reticle's point. A missed working said "Your swing finds nothing"; it now says "Your Impulse Bolt finds nothing", and the miss message no longer names the bow's content ID in code (an M6 slip against the no-IDs rule) |
| 4 | "Take All"; loot UI stays open out of reach | R, or the "Take all [R]" row above an open container's stacks (a chest, a corpse, a cache, the cart): `TakeAllCommand` takes each stack, top to bottom, as much of it as the pack's weight and stack slots allow, reading the contents again after each move. What does not fit stays where it was, a partial stack split. The toast says what was taken and why anything stayed. Tested: the den cache emptied and the result round-tripped through a save; a pack near its limit takes what fits with every item conserved, the limit held and nothing left that could still have fitted; a container out of reach and a trader's wares are refused. **Closing out of reach is general now:** any panel opened at a place - a container, a searched corpse (even once the corpse is gone), a crafting station, a trader - closes when the body walks out of reach of it |
| 5 | A heading-only compass | Top right (the quest tracker moved below it), in first and third person alike: a dial that turns so its north points north, N, E, S and W on it, a fixed mark for where the view faces, and the bearing in words and degrees ("NE 045°"). No minimap, no markers, nothing read from the world's database. The dial is DeepSeek's `ui.hud.compass` when the asset workspace is present (its round face cut from the icon's painted background), and a drawn one otherwise |
| 6 | A controls overlay | F1, in two columns: the controls read from the input map as bound, so the list cannot drift from the keys - moving (move, sprint, walk, jump, crouch), looking (look, zoom, first person, shoulder), acting (interact, attack or shoot, guard or aim, dodge, salve, formulas, take all), screens (inventory, journal, character, controls, free the mouse), the companion (follow or wait), conversation, saving - and the developer's keys (F3, F4) under their own heading. Local hints: "[Tab] close" on the inventory, "Take all [R]" on a container, "[K] close" on the character sheet, "[F1] close" on the overlay, and "[K] a point to spend" on the HUD after a level-up |
| 7 | A working character screen | K: the name (Phase 1 has no archetype choice, and the sheet says so), level and XP to the next, the three attributes a derived value reads (might, endurance, will) with what a point in each changes, the four not in play yet named as such, the derived values (health, stamina, focus, resonance, strain tolerance, armor, carrying), skills with their XP, and each known technique with where it was learned. A level-up's attribute point is spent here (`SpendAttributeCommand`), and only on an attribute in play. No soul, prestige, faction or future system |
| 8 | Protect what worked | Dialogue, quests, crafting, buying and selling are untouched. The acceptance run replays the recorded M6 run exactly (below) |

**Saves (schema 13, through the M2b harness).** The player section holds the body's posture - `stance`, `airborne`, `air_ms` - so a save made mid-jump lands where the saved world would have, and one made crouched under the beam loads crouched (tested field by field). The schema-12 player shape is frozen (`Sections/SchemaV12.cs`) and the 11 -> 12 step repointed at it; 12 -> 13 stands every older save on the ground; a schema-13 player without a posture is corrupt, not defaulted. The v13 fixture (Aelin crouched) was written by `M2.Probe`, and every older fixture's `expected.json` gained the standing posture and nothing else (reviewed line by line). The player digest is `unnamed.player/v9`.

**DeepSeek's art** is read at run time from the asset workspace by manifest ID (`--asset-root <dir>`, else `UNNAMED_ASSET_ROOT`, else `<repo>/assets`, else greybox); no file of it is copied into this repository. The stills in `docs/acceptance/m6_delta/` are the greybox run for that reason; the same run with the workspace, compass and bolt art included, is on ASTRAL at `G:\UNNAMED_HISTORY\delta_shots_5\`.

### Verification

- `dotnet test` (from `src/`): **696 passed**, 0 failed (Architecture 14, Content 139, Domain 147, EntityRegistry 23, World 60, Persistence 154, Application 159). New: `JumpAndCrouchTests` (7), `CharacterSheetTests` (2), Take All (3), the shot's trace (2), the overhang and crouch content checks (2), the 12 -> 13 migration (2), and the v13 fixture. Content lint: 102 definitions, 0 errors. The Presentation build: 0 warnings, 0 errors.
- **Runtime proof of each change:** `--delta-shots <dir>` plays a fresh game through the real command path and takes a picture of each: the compass in both views, F1, Take All at the waystation's chest and the panel closing as the body walks away, a running jump caught over the timber, the crouch under the beam and the stand refused beneath it, the cart's Take All, the bow's reticle on a tree 16 m off, the arrow in flight and stuck in the tree, the primer read, the Impulse Bolt's reticle, flight and burst, and the character sheet (`docs/acceptance/m6_delta/transcript.md`). All 18 beats pass with and without the asset workspace. The runs caught five things, since fixed: the compass drew no W; the F1 list ran off the screen; the greybox drew the beam as a block standing on the ground; the arrow was a speck in flight; and the compass, first put at the top centre, had pushed the notices down over the trade and container panels (seen in the ui-shots' trade), so it moved to the top right and the notices and the target's bar are back where M6 had them.
- **The acceptance run, again** (`--playthrough`, the same fixed seed): every beat happened, and its transcript matches the recorded M6 run's row for row and tick for tick - 79 of 80 rows identical, the other the raw state digest, which covers fresh instance IDs and differs every run. Its `state_replay.json` equals the recorded one in all 410 of its fields and adds three: the posture, standing and on the ground. The quests, the dialogue, the crafting, the fights, Tavar and his way home all went exactly as before.
- **Save/load integrity:** the relaunch (`--playthrough-verify`) loaded the save with **415 fields compared, 0 differences** (412 before, plus the posture), then died at the den with the penalty applied once, as recorded.
- The headless smoke: **PASS** (the quicksave's digest identical).
- The windowed `--ui-shots` run plays the whole M3-M5 journey and exits 0: Kera's trade sells the old sword and buys arrows, a searched boar's remains show the Take All row and close their column once taken bare, and the HUD offers "[K] a point to spend" at level 2.

### Deferred on purpose

- No arrow art and no reticle icon: DeepSeek's manifests have neither (the bow's icon is an inventory icon), so the arrow is greybox and the reticle is drawn. `vfx.magic.cast_charge` and the other formulas' effects (Brace Ward's shell, Mending Thread's restore, the Strain overlay) are not wired - the owner's finding named the Impulse Bolt's flight and impact.
- The hit resolves at release, so a creature reacts before the arrow is drawn reaching it - up to about 0.7 s at the bow's 40 m range; drawing the hit on arrival would move a gameplay rule.
- Crouching changes only footfall noise; sight, light and cover are the perception and stealth work to come. The crouch and jump poses are placeholders for the animation pipeline.
- The jump has no fall damage, ledges or mantling, and nothing is stood on.
- The controls cannot be rebound; F1 only reads them.

## Commits, state, next

- M6 part 1 `8935b8d`, part 2 `4df79ea`, part 3 `f3a064e`, part 4 `708bb8d` (this report), the feel-test row `6742e24`, the overnight re-check (the husk's patrol, the perf walk, the evidence recorded again), and the owner-playtest delta (one commit) - on `claude/phase1`, draft PR #1; nothing merged to `main`.
- Next: **stop and report the delta to the owner.** M7 is not authorized, and no M7 work begins.

## Phase-1 closeout

**Date:** 2026-09-24. An owner-authorised closeout and consolidation pass: no M7 work, and the game's rules, dialogue, quests, crafting,
trade, movement and combat unchanged.

- **The owner's replay** of the M6 delta build succeeded.
- **Rulings:** the early feel test (G1) is deferred until a nontechnical Windows playtest build exists and is not an M7 entry blocker;
  C10 (G3) is revised and the March Spear is not weakened (both above, and in `PROTOTYPE.md` and `ROADMAP.md`).
- **Consolidation:** a verified backup came first (off-repository, with a git bundle). `claude/phase1` (`232049e`) and DeepSeek's
  asset-maintenance head (`e91890b`) were merged, Claude's first, in an integration worktree from `origin/main`, verified, and merged to
  `main` through PR #3 (`06d65a5`; PR #1 closed with it). No force push and no rebase. The canonical workspace is `G:\UNNAMED` on `main`,
  its ignored asset library untouched; the `G:\UNNAMED_CLAUDE` and `G:\UNNAMED_INTEGRATION` worktrees are kept and can be removed later.
- **Assets:** the library's current stable set is drawn by semantic ID with the greybox as fallback - what is drawn, what is withheld
  and why (rigs that do not fit their meshes, models built leaning, swatch textures, LOD files without materials), and the audio mapping:
  `PHASE1_ASSET_INTEGRATION.md`.
- **Audio:** the V3 set (230 IDs) is the current functional set; every entry is `human_auditioned: false`, so it is a working placeholder.
  V1 and V2 stay as history; nothing was regenerated.
- **The Windows playtest build:** a portable ZIP kept privately on ASTRAL (not published):
  `G:\UNNAMED_HISTORY\playtest_build\Otherreach_Phase1_Playtest_2026-09-24.zip`. Unzip anywhere and double-click `Otherreach.exe`; the
  .NET runtime is bundled. It finds `content/` and a trimmed copy of the asset workspace (`assets/`: the three manifests, the icons, the
  effects, the V3 sounds and the models and clips the bindings draw, 306 files) beside its executable. Exported with Godot 4.7.2's
  official templates and the `export_presets.cfg` preset "Windows Playtest"; the export needs the project file named for its assembly,
  so `Presentation.csproj` is now `UNNAMED.Presentation.csproj` beside a `UNNAMED.Presentation.sln`. Saves go to
  `%APPDATA%\Godot\app_userdata\Otherreach`. **Checked from the ZIP extracted to a fresh folder** away from the repository, with no
  asset-root variable: the exported game's headless smoke PASS; its acceptance playthrough played every beat (79 of 80 rows as the
  editor's run, the other the per-run digest), its replay byte-identical, and its relaunch loaded the save with 415 fields compared and
  0 differences before dying at the den with the penalty once; a plain launch boots clean.
- **Verification of the closeout (editor, with the asset workspace):** `dotnet test` 696 passed; content lint 102 definitions, 0 errors;
  the headless smoke PASS; `--ui-shots` exits 0; `--playthrough` 79 of 80 rows identical to the consolidation run with its 2,499
  commands identical (instance IDs masked), the replay byte-identical, the relaunch 415 fields / 0 differences; `--delta-shots` all 21
  beats (three new ones show the formulas' effects on the body and the Strain overlay). A fresh clone without assets (as CI sees it):
  the smoke PASS and all 21 delta beats in greybox.
- **Open at this closeout, since closed:** the RAZER performance window (M6's 1080p/60 evidence) - measured on 2026-09-25 and accepted
  as the baseline (`PHASE1_TECHNICAL_CLOSEOUT.md`).
