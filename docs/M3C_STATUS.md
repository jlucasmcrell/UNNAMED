# M3c status - Combat Core

**Date:** 2026-09-23. **Branch:** `claude/phase1`. **State:** implemented and verified; Phase-1 subset only (`PROTOTYPE.md` §4, the owner's scope rulings).

## What M3c built

| Layer | What | Where |
|---|---|---|
| Domain | The one damage pipeline, `CombatRules.Resolve`: attack, contact region (head, torso, limbs), that region's armor, penetration, then damage, stagger and on-hit effect (`COMBAT_DAMAGE_ARMOR_AND_DEATH.md` §18). No level input anywhere. Reach and the front arc, line of sight past walls (`Blocker.Crosses`), creature definitions, status effects and their stacking | `src/Domain/Combat`, `src/Domain/Spatial/Blockers.cs` |
| World | `CombatSystem` owns the player's combat state and every creature combatant: attacks in windup, active window and recovery; the guard; the dodge; stamina and recovery; creature chase, bite and leash; every loss of health. `StatusEffectSystem` owns effects on every body. `DeathSystem` settles a death: XP debt, respawn, weakness. Commands: `AttackCommand`, `BlockCommand`, `DodgeCommand`, `UseItemCommand` | `src/World/Runtime/Combat.cs` |
| Content | `config.damage_constants`; `effect.bleeding`, `effect.weakened`, `effect.mending`; `creature.beast.wolf_grey` and its `ability.creature.wolf_bite`; `spawn.hollow.valley_strays`; the salve's use; the one-hand blade's level-3 passive. `CombatContent` builds them in whole ticks and lints them (`CMB001`) | `content/`, `src/Content/CombatContent.cs` |
| Persistence | Schema 7: the player's active effects, with absolute world-tick deadlines. The frozen V6 player shape, 5 -> 6 repointed at it, the 6 -> 7 step, effects in the definition-ID pass, the v7 fixture (writer pack 0.1.3; the current fixture pack is 0.2.4) | `src/Persistence` |
| Presentation | The wolves drawn between ticks with a readable tell (the windup rears and lights the muzzle), combat poses, health and stamina bars, active effects, the target's health, a combat log, and a death recap. Left button swings or shoots, right holds a guard (or aims the bow), C dodges, H uses a salve; a swing, a guard and an aimed bow go where the camera looks | `src/Presentation` |

## How a fight works

- **Timing is the authority.** A swing is the weapon's attack time split 40% windup, 20% active window, 40% recovery (the rusted sword: 6, 3 and 5 ticks). The tick an action is taken is not one of its own, so swings asked for back to back start every 15 ticks, not 14 - as does every action, one tick past its own length (the Phase-1 technical audit, L-25: documented rather than changed, for the owner to rule on). The bow draws for its `draw_time`, releases once along the facing and spends an arrow, then nocks for 0.3 s. The wolf's bite is 0.45 s of windup, the tell, then 0.15 s active and 0.8 s of recovery, the opening. Presentation poses the body from these phases; a clip bound later must put its `hit_window_start` where the windup ends (`ANIMATION_METADATA_SCHEMA.md`'s event vocabulary: `windup_start`, `attack_commit` and `hit_window_start` at the end of the windup, `hit_window_end` and `recovery_start` after the active window, `projectile_release` and `ammo_consumed` at the bow's release). No gameplay reads a frame number.
- **Position matters.** A melee blow lands on what is inside the weapon's reach and a 90-degree front arc during the active window, with no wall in between; the sword reaches 1.8 m and the bite 1.4 m. A shot takes the first body along the facing within 40 m that no wall hides.
- **Defence is a choice with a cost.** A raised guard takes 70% off a physical blow from the front 120 degrees and absorbs its stagger, for 10 stamina a blow; a blow meeting a guard with no stamina behind it breaks it and staggers. A dodge (20 stamina) gives 0.25 s of invulnerability over a 2.5 m dash; it can abandon a windup, never a committed blow.
- **Stagger never locks.** A blow whose impact reaches 14.5% of the target's maximum health staggers it for 0.6 s, then it cannot be staggered for 2 s.
- **One place health falls.** Blows and effect ticks alike reduce health in `CombatSystem`, where a death is noticed; the death system settles it at the end of the tick. Rolls are keyed by attacker, target and world tick, so a replay and a reload roll the same.
- **Death costs debt, not progress** (PROTOTYPE.md §5 step 7, C17): 10% of the level's XP span as debt (AG-8), a respawn at the outpost with full pools and no effects, then 60 s of `effect.weakened` (three-quarter blows, half-speed stamina). The death names its killer and the last five blows.

## Reconciliation (brief §12)

- `SYSTEMS.md` S-12: the "threat/aggro table" is gone. Who an actor fights is that actor's own record, derived from what it perceived, inferred or was told, and owned by S-23 (`STEALTH_DETECTION_AND_THREAT.md` §1, §6). Until S-23 arrives (M3d), a wolf knows only what it has felt: it turns on whoever wounds it, and gives up 40 m from home. Its packmate does not join; it cannot hear yet (`kill.png` shows the other stray still calm beside a kill).
- The reconciliation note under S-12 records the single pipeline, S-11's persisted effects (players save mid-fight) and S-06/S-07's death. The current pools stay on the progression record, where M2c put them.
- `ROADMAP.md` M3c's three families are `PROTOTYPE.md`'s sword, bow and spell; the spell family, Focus and Strain are M3e's. The design bands are `VERTICAL_SLICE.md` §5.1's.
- No universal level-based immunity: `CombatRules.Resolve` has no level parameter (`Level_IsNotAnInput_...`), and a landing blow always wounds.
- `DATA_MODEL.md` records the Phase-1 creature fields, creature attacks, effect and spawn subsets, `config.damage_constants`, and skill passives (pulled forward from M3f); `PROTOTYPE.md` §4.4 records `effects/` and `abilities/`.

## Exit criteria (`ROADMAP.md` M3c)

| Criterion | Evidence |
|---|---|
| A tuning table generated from the build shows time-to-kill within design bands across level bands 1-3 | `docs/M3C_TTK_TABLE.md`, written by `TtkTableTests` from real fights in 24 worlds each, and failing CI when it drifts from the build. Median kill of a level-2 wolf: sword 5.6 / 5.6 / 4.85 s at levels 1 / 2 / 3, bow 7.45 s at each - inside 4-8 s. Standing still against the valley pair, death comes in 13.2 s (14.45 s in hide), inside 8-15 s; bare hands (12-13.3 s) are reported as a fallback, not a family |
| Combat is readable: a tester can name what killed them | `PlayerDied` carries the killer, the cause and the last five blows (`Dying_OwesXpDebt_Once_...`); the HUD shows them, with a combat log and the windup tell (`death.png`, `combat.png`) |
| No out-of-band HP sponges | Level is not an input; the table's over-band row - the same wolf authored at level 6 with a doubled bite - dies exactly as fast (5.6 s) and kills faster (14.5 s against 24.65 s). `DATA_MODEL.md` §4.4 makes it a content rule: harder by what a creature does, never by a larger pool |
| Weapon-skill XP accrues only from effective contribution | `SkillPracticed` telemetry: one practice per wounding blow and none for anything else (`TheSword_KillsAWolf_AndOnlyEffectiveBlowsTrainTheBlade`, `ASwingAtNothing_Misses_AndTeachesNothing`); the difficulty is the creature's level, so the same wolves stop teaching as the skill rises (PROGRESSION.md §4.2) |
| Playable state: fight, die, be rewarded | The windowed `--ui-shots` run fights a stray to its death (+35 XP, the blade trained), wounds the other, and dies to it through the real command path |

Also proven: the bow spends an arrow at each release and cannot shoot without one; a wall stops an arrow; the guard takes a frontal bite and not one from behind; a dodge inside the windup avoids the bite; weakness lightens a blow; stagger immunity holds; a fight replayed from its command log ends in the identical state on the identical tick; effects resume after a save and load on the same ticks; the salve mends 18 over 6 s and is used up; sprinting spends stamina and an empty pool only runs; a wolf wounded from beyond its leash gives up, still wounded.

## Decisions (flippable)

1. **The numbers** are placeholders with a stated shape (`PROTOTYPE.md` A-5): armor mitigates `armor / (armor + 50)`, a pierce ignores 30% of it, criticals are 5% at x1.5, regions fall 10/60/30 and matter x1.5/x1/x0.75, Might adds 4% to physical blows per point above the base. A fraction of a point is kept as a chance of one more, so a few points of armor matter on average.
2. **The wolf** is level 2 with 50 health, fur armor 1/2/1, a 4-6 piercing bite that bleeds 30% of the time, 4.5 m/s (between a run and a sprint), and 30 kill XP.
3. **Bleeding** is a point a second per stack for 6 s, up to 3 stacks, and slows stamina; **mending** is the salve's 18 over 6 s, which M3e's mend spell is meant to share. Five effects in all once M3e adds burning and oakskin, where `PROTOTYPE.md` §4.1 counts three: §5 step 7 needs the weakness, and the salve needs its healing.
4. **Health returns out of combat**, a point a second after 8 s without a blow. `PROTOTYPE.md` does not say; without it a character has no way to heal before M3e's mend and M3f's salve recipe.
5. **The valley strays are placed from M3c**, so there is something to fight. Creature state is not saved until M3d: a load places both strays afresh, whole, at their site.
6. **A swing, a raised guard and an aimed bow face where the camera looks**; walking alone faces where the body goes, as in M3.
7. **One attack per weapon.** `VERTICAL_SLICE.md`'s light and heavy attacks and directional block are slice work; Phase 1 has one swing, a frontal guard and a dodge.
8. **The respawn point is the region's spawn**, inside the outpost palisade.

## Verification

- `dotnet test src/UNNAMED.sln`: **490 passed**, 0 failed. Architecture 14, Content 87, Domain 120, EntityRegistry 23, World 58, Persistence 130, Application 58.
- Content lint: 43 definitions, 0 errors.
- Godot 4.7.2: the headless smoke prints `PASS` (it now swings at nothing and sees the swing run its phases and miss, and checks the two strays exist). The windowed `--ui-shots` run (Astral) writes `combat.png` (mid-fight: bars, target bar, log), `kill.png` (the wolf on its side, +35 XP, the other stray calm) and `death.png` (the recap naming the Grey Wolf's bite, the last blows, XP debt +10, weakened 60 s, respawned at the outpost). I reviewed all three.

## Not done, and why

- Creature perception, spawning with respawn, saved creature state, corpses and loot: M3d ("creature framework, AI baseline, loot").
- The spell family, burning and oakskin, Focus and Strain spending: M3e.
- The wolf fang's +2% critical chance: items carry no modifiers yet, and fangs drop from M3d.
- Combat poses are procedural greybox; the real clips come with Animation Wave 0's rig, timed to the phases above.
- The M3 performance gate still waits on the RAZER window.
