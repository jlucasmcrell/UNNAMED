# M3d behaviour matrix

**Generated from the build; do not edit.** `BehaviourMatrixTests` reads the creatures from the game's own content and measures them in the real simulation, and fails when this file differs from what the build produces. Regenerate after an intended change with `UNNAMED_WRITE_MATRIX=1 dotnet test --filter BehaviourMatrix` (from `src/`) and review the diff.

Two layers, kept apart (owner ruling, M3d): a **creature archetype** is a body and its blows - what it is; a **role** is what one creature does in its place - how it waits, how far it goes, whether it calls. Any archetype can take any role. The five archetypes are the content bible's (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §10); the grey wolf is the archetype the systems were proven on first. Every number is placeholder tuning (`PROTOTYPE.md` A-5).

## Archetypes: bodies and blows (content)

| Creature | For | Level | Health | Armor torso / head | Speed m/s | Turn deg/s | Blow | Also | Sight m / FOV deg / hearing m |
|---|---|---|---|---|---|---|---|---|---|
| Ash Ember Hound | fast predator: movement, timing, pursuit | 3 | 35 | 1 / 1 | 6 | 720 | Ember Lunge: fire 5-7, reach 1.2 m + 2.2 m lunge, tell 0.4 s | bites on the run | 30 / 160 / 35 |
| Bone Walker Husk | basic humanoid: readable melee and reach | 3 | 55 | 4 / 2 | 2.2 | 180 | Rusted Cleave: physical_slash 6-9, reach 2.4 m, tell 0.8 s | physical_pierce resisted 60%; immune to Bleeding, Venom | 18 / 120 / 20 |
| Animated Armour | armored heavy: armor and weak-point logic | 5 | 90 | 45 / 0 | 1.8 | 90 | Gauntlet Slam: physical_blunt 10-14, reach 1.8 m, tell 1.0 s | open head from behind; physical_pierce resisted 50%; immune to Bleeding, Venom | 15 / 100 / 18 |
| Bristleback Boar | charging brute: lateral movement and terrain | 4 | 70 | 8 / 4 | 4 | 240 | Gore: physical_pierce 6-9, reach 1.3 m, tell 0.5 s | Charge: 9 m/s from 5-14 m, physical_blunt 12-16, knocks down, stunned 2.0 s by what it runs into | 20 / 150 / 25 |
| Cave Hunting Spider | ambush, nonhuman: perception and avoidance | 3 | 40 | 3 / 2 | 5.5 | 720 | Venom Bite: physical_pierce 4-6, reach 1.2 m, tell 0.4 s, Venom 60% | - | 6 / 360 / 14 |
| Grey Wolf | the M3c proof archetype (pack beast) | 2 | 50 | 2 / 1 | 4.5 | 720 | Bite: physical_pierce 4-6, reach 1.4 m, tell 0.5 s, Bleeding 30% | - | 25 / 140 / 30 |

## Archetypes: measured in the simulation

Each alone on open ground in the neutral `roamer` role (with no route to walk, it holds its place), so only the archetype differs. Distances are centre to centre.

- **Opens with / tell at / lands from**: a level-3 archer 13 m in front looses one arrow, then stands. The first blow it begins, how far off its tell (the windup) began, how far off the blow landed, and seconds from the arrow to that blow.
- **Runs down a sprinter**: from 8 m, one arrow, then a straight sprint away (5.12 m/s): seconds until its first blow lands and which blow it was, or no.
- **Sword kill**: a level-3 fighter with the rusted sword, face to face, swinging whenever free; median over 8 worlds, from the first swing, and the worlds the fighter died in first.
- **Death standing**: a level-1 character in no armor wakes it with one swing and stands; median over 8 worlds from the first wound.
- **Walker seen at / sprinter heard at**: from 32 m straight at it - walking head-on, sprinting from behind - how far off it was when it first took notice (suspicious or more).

| Creature | Opens with | Tell at m | Lands from m | Arrow to blow s | Runs down a sprinter | Sword kill s (fighter died) | Death standing s | Walker seen at m | Sprinter heard at m |
|---|---|---|---|---|---|---|---|---|---|
| Ash Ember Hound | Ember Lunge | 2.4 | 0.8 | 2.1 | yes, 3.1 s (Ember Lunge) | 3.4 (0/8) | 26.0 | 28.8 | 15.7 |
| Bone Walker Husk | Rusted Cleave | 2.6 | 2.6 | 5.8 | no | 5.6 (0/8) | 29.3 | 16.8 | 15.8 |
| Animated Armour | Gauntlet Slam | 2.0 | 2.0 | 6.8 | no | 20.6 (1/8) | 22.1 | 13.9 | 15.9 |
| Bristleback Boar | Charge | 12.0 | 0.8 | 1.8 | yes, 1.3 s (Charge) | 7.9 (0/8) | 22.5 | 18.8 | 15.8 |
| Cave Hunting Spider | Venom Bite | 1.4 | 1.4 | 2.8 | no | 4.1 (0/8) | 19.2 | 5.1 | 13.7 |
| Grey Wolf | Bite | 1.5 | 1.5 | 3.0 | no | 5.6 (0/8) | 21.8 | 23.9 | 15.7 |

## Roles (content)

| Role | At rest | Territory m | Calls for help | Answers calls | Keeps off m | Flees below | Also |
|---|---|---|---|---|---|---|---|
| ambusher | hold | 14 | no | no | - | - | goes for a footfall in its territory |
| den_guardian | hold | 16 | yes | yes | - | - | - |
| hunter | wander | - | no | no | - | - | wanders 10 m |
| pack_hunter | hold | - | no | yes | - | - | flanks 3 m wide |
| roamer | patrol | - | no | yes | - | - | - |
| sentinel | hold | 10 | no | no | - | - | - |
| sleeper | sleep | - | no | yes | - | - | hears at 40% asleep |
| stray | wander | - | no | no | 7 | 30% | wanders 8 m; strikes inside 3 m |
| territorial | wander | 15 | no | no | - | - | wanders 5 m |

## Ashen Hollow's spawners (content)

| Spawner | At (x, z) m | Creatures, each in its role | Returns |
|---|---|---|---|
| spawn.hollow.boar_wallow | (18, 72) | Bristleback Boar as territorial | no |
| spawn.hollow.charwood_hound | (128, 150) | Ash Ember Hound as hunter | no |
| spawn.hollow.den_pack | (112, 184) | Grey Wolf as den_guardian, Grey Wolf as pack_hunter x2, Grey Wolf as sleeper | no |
| spawn.hollow.east_pack | (188, 180) | Grey Wolf as roamer x2 | after 20.0 min |
| spawn.hollow.iron_shelf_armour | (65, 34) | Animated Armour as sentinel | no |
| spawn.hollow.iron_shelf_husk | (48, 58) | Bone Walker Husk as roamer | no |
| spawn.hollow.spider_lair | (172, 48) | Cave Hunting Spider as ambusher | no |
| spawn.hollow.valley_strays | (118, 128) | Grey Wolf as stray x2 | no |
