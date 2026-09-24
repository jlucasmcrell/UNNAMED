# M3d status - Creature Framework, AI Baseline, Loot

**Date:** 2026-09-23. **Branch:** `claude/phase1`. **State:** implemented and verified; Phase-1 subset only (`PROTOTYPE.md` §4, the owner's scope rulings and the two M3d rulings below).

## The owner's M3d rulings, as applied

- **Five archetypes, not one body in five roles.** The content bible's five (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §10) are five creature definitions, each with its own body, blows and senses: `creature.beast.ash_ember_hound`, `creature.undead.bone_walker_husk`, `creature.construct.animated_armour`, `creature.beast.bristleback_boar`, `creature.beast.cave_hunting_spider`. The grey wolf, on which the systems were proven first, stays as the prototype's pack animal. **Roles are a separate data layer** (`config.creature_behaviour`): how a creature waits, how far it goes, whether it calls. A spawner gives each member a role, and any archetype can take any role. Placeholder geometry stands in for the bodies (see Presentation).
- **The bible is the destination, not an M3d rebuild.** The M3 greybox layout is kept. The bible supplies the archetypes, what each is for, the perception principles (a walker can pass what a runner cannot) and the content targets. Where its coordinates conflict with working M3 geometry, the geometry stays and the divergence is recorded below; the runtime moves toward the bible's four-cell layout step by step before M6 acceptance.

## What M3d built

| Layer | What | Where |
|---|---|---|
| Domain | Perception: `Senses` (sight, field of view, hearing); awareness from 0 to 100, gained faster the closer the target is seen and lost unseen; noises with a radius (walk 3 m, run 8, sprint 16, a swing 10, a blow 20, a howl 45). Walls block sight. Roles (`CreatureRole`). Creatures gain senses, a charge, a turn rate, a weak point and tags. Creature attacks gain a lunge, a charge (speed, range, stun), a cooldown, a forced stagger and advancing through the windup. Effects gain immunity tags | `src/Domain/Creatures/Perception.cs`, `src/Domain/Combat/Combat.cs` |
| World | `CreatureSystem` owns every creature (the `Creatures` slice). It places creatures from their spawners. Perception drives a mind: unaware, suspicious, engaged, searching, returning or fleeing. The role sets what it does at rest (hold, wander, patrol a route, sleep). In a fight it holds a territory line, keeps its distance, flanks, charges, lunges, and turns only as fast as its body allows. A creature answers calls only from its own kind. A death leaves a corpse holding the creature's loot table. Corpses decay or empty. A spawner brings its creature back on a timer, as a new generation with a new identity; AG-3 saturation doubles the timer. Creature blows still go through the one damage pipeline, as internal commands to `CombatSystem` | `src/World/Runtime/Creatures.cs`, `Combat.cs`, `Items.cs`, `Systems.cs` |
| Content | The five archetypes and their six abilities, `effect.venom`, and `config.creature_behaviour` (awareness, noise, corpses, nine roles). Six new spawners, eight in all. Loot tables for the boar and the armour. 65 definitions | `content/` |
| Persistence | Schema 8: creature records in the entities section (see "Saved" below). The V6 entities shape is frozen. The 7 -> 8 step and the v8 fixture (writer pack 0.1.4; fixture pack 0.2.5 renames a creature) | `src/Persistence` |
| Presentation | A placeholder body per archetype: quadruped, biped or arachnid, with hide colour and proportions per archetype and size from content. A mark over the head (`?` suspicious or searching, `!` engaged, `z` asleep). Sleepers lie down; the dead lie still and vanish once emptied or decayed. E searches a corpse ("[E] Search the Bristleback Boar"), and the container column closes when the corpse is emptied | `src/Presentation` |

## How creatures work

- **They perceive; nothing is shared.** A creature sees within its sight range and field of view, past no wall, and hears what reaches it. Seeing builds awareness. Hearing gives a place to look, never the target itself. Nothing is shared between creatures: a creature's target is its own record (`STEALTH_DETECTION_AND_THREAT.md` §1, §6). The one exception is a call. A den guardian that finds its target howls once, and the howl brings its own kind at a run; a boar in earshot ignores it.
- **Minds.** Suspicious at 30 awareness: it turns to look, and at 60 or more (anything heard) it goes to look. Engaged once it sees the target at full awareness. After a second unseen it is searching: it goes to the last place it knew, then looks around, all within one budget of 12 s. Returning when it gives up or passes its leash. A returning creature forgets, and needs something new to turn it round. Fleeing when a role's nerve breaks. Creatures take notice only of what lies inside their own ground: a territory if the role keeps one, otherwise the 40 m leash.
- **Roles.** `den_guardian` holds the den mouth, keeps a 16 m territory and howls. `pack_hunter` answers a howl and closes from the side. `sleeper` hears at 40% until woken: a sprint wakes it, a walk may not. `stray` wanders, keeps 7 m off until hurt, strikes inside 3 m and flees below 30%. `roamer` walks its spawner's route. `hunter` ranges its patch. `sentinel` guards its post and goes no further. `territorial` roots about and defends 15 m. `ambusher` waits in its lair and goes for a footfall inside it.
- **Archetypes** (measured side by side in `M3D_BEHAVIOUR_MATRIX.md`):
  - *Ash Ember Hound* runs 6 m/s, faster than a sprint. It bites on the run: its windup does not stop it. Its fire lunge goes through a raised guard.
  - *Bone Walker Husk* is slow (2.2 m/s, turns at 180°/s). Its rusted cleave reaches 2.4 m, further than the sword. It resists arrows (60% of piercing), and nothing bloodless bleeds.
  - *Animated Armour* has plate (torso 45, limbs 35) and turns at 90°/s. Its helm is open behind: a blow from behind always lands on the unarmored head. Its 1 s slam is the slowest tell.
  - *Bristleback Boar* charges at 9 m/s from 5-14 m and knocks down what it meets, unless the blow is guarded or dodged. It cannot turn in the run: a rock in its line stuns it for 2 s.
  - *Cave Hunting Spider* sees 6 m and hears 14 m, in every direction. A walker passes it; a runner is taken. It never leaves its 14 m lair, so it can always be gone around. Its bite carries venom.
- **Death, corpses, loot.** A dead creature leaves a corpse, searched like a chest. Its contents are the creature's loot table, rolled from the corpse's key until the player changes them (M3b's containers). An emptied corpse is gone at once; an unsearched one decays after 10 minutes.
- **Respawn and AG-3.** A spawner with a timer brings its creature back once the window has passed and the player is at least 40 m from home, as a new generation with a new identity: the east pack after 20 minutes. When the player has killed at a cluster the AG-3 number of times within its window, the spawner emits `SpawnerSaturated` and the window doubles. The den pack, the strays and the five archetypes do not return.
- **Saved.** Schema 8 records every creature that differs from its spawner's baseline, in the entities section's `creatures` list. A record holds its key (`spawner#member`), instance ULID, host cell and baseline hash, generation, condition (alive, corpse, gone), position, facing, health, death and respawn ticks, and its mind (awareness, what it knows and where, when it last saw its target, its search deadline, whether it has called). Wander and patrol derive from the world tick, so they need no storage. An attack in its windup is transient, like the player's.

## Reconciliation

- `SYSTEMS.md` S-23 and S-31 carry M3d notes (below the M3c one under S-12). `DATA_MODEL.md` records the creature, creature-attack, effect, spawner and config fields; `PERSISTENCE.md` records schema 8 and the 7 -> 8 step; `PROTOTYPE.md` §4.4 recounts the content; `ROADMAP.md` M3d names the Phase-1 subset.
- **Content bible divergences, recorded rather than rebuilt:**
  1. *Placement.* The bible puts the Bone Walker patrol at (48,58) and the Animated Armour at (65,34), which fall inside the M3 outpost's longhouse and forge. Both stand at the iron shelf instead: the husk walks (56,160) - (74,168) - (70,186) and the armour guards (76,180). The hound (128,150), the boar (18,72) and the spider (172,48) are at the bible's coordinates.
  2. *Layout.* The bible's four cells are not adopted yet: cell A's Ashen Waystation and Waystone, the player starting at (30,158), and its longhouse and forge placement. The M3 outpost (spawn at (55,60)) stays until the layout is reconciled.
  3. *Identifiers.* The bible's `creature.ashen_hollow.*` names are placeholders. The definitions use `DATA_MODEL.md`'s family scheme (`creature.<family>.<name>`).
  4. *Death.* The bible's Downed, Dying, Dead and return at the Waystone replace M3c's death (XP debt, weakened, respawn at the outpost), which stays until the layout brings the Waystone.
  5. *Weapons and start.* The bible's families are sword, bow and spear, and the player starts without the bow. `PROTOTYPE.md`'s sword, bow and spell stay (M3e builds the spell), with the bow in the starting kit.
  6. The grey wolf is not among the bible's five. It stays as `PROTOTYPE.md` §4.1's den pack and respawning pack and M3c's valley strays.

## Exit criteria (`ROADMAP.md` M3d)

| Criterion | Evidence |
|---|---|
| Five creatures that play differently are provably different: a behaviour matrix, not a claim | `docs/M3D_BEHAVIOUR_MATRIX.md`, written by `BehaviourMatrixTests` from content and from real fights, and failing CI when it drifts. Measured: the boar opens with its charge from 12 m and the others from 1.4-2.6 m. The husk's blow lands from 2.6 m, where every other lands inside 2.0 m. Only two archetypes run down a sprinter: the hound by pace (3.1 s) and the boar by its charge (1.3 s). A level-3 swordsman kills the hound in 3.4 s and the armour in 20.6 s (dying first in 1 of 8 worlds). A walker is noticed by the hound at 28.8 m and by the spider only at 5.1 m. The test asserts every signature distinct and each archetype's defining behaviour |
| Spawn/respawn persists correctly across save/load | `ACreaturesDeathWoundsAndCorpse_SurviveSaveAndLoad`: a killed creature's corpse and loot, and a wounded one mid-hunt, reload to the identical state digest. `ASpawnerBringsItsCreatureBack_AfterItsWindow_EvenAcrossALoad`: saved halfway through the window, the creature returns on the exact tick after the load, as generation 1 with a new identity. The v8 fixture - a searching wolf, a half-searched corpse, and a gone creature of a renamed species that is due back - migrates through the chain and loads |
| AG-3 spawn-site saturation is observable | `SpawnerSaturated` names the spawner; the kill that saturates it doubles its respawn window (`ASaturatedSpawnCluster_TakesTwiceAsLongToRefill`) |
| Creature AI cost within the M3 profiling budget at scale | 60 creatures of all six archetypes, awake and in tier A: 0.43 ms per 50 ms tick on ASTRAL - under 3% of a 16.7 ms frame even on the frame that carries the tick (`SixtyCreatures_TickWithinTheBudget`, asserted under 4 ms a tick). The content places 13. The RAZER frame-budget capture, now with the creatures present, stays in the owner's measurement window |

Also proven: a walk behind a wolf goes unheard and a sprint is heard. A wall hides the player even face to face. A sleeping wolf wakes to a sprint but not a walk. A wolf that loses its target behind a door searches, gives up and goes home. A stray keeps its distance until hurt, then runs when badly hurt. A roamer walks its spawner's route. The hound's lunge goes through a raised guard. The husk out-reaches the sword, and arrows barely scratch it. The armour's first blow taken from behind lands on the open helm, while blows on its plate average under 5. A dodged charge runs the boar into a boulder and stuns it; an undodged one knocks the player down. The spider lets a walker pass, takes a runner and keeps to its lair. Undead and constructs do not bleed. A corpse holds its loot and, searched empty, is gone.

## Decisions (flippable)

1. **The numbers** are placeholders with a stated shape (`PROTOTYPE.md` A-5): awareness 40/s seen at the edge of sight and 200/s close, fading at 10/s; suspicious at 30, hearing sets 60 and a howl 80; the noise radii above; a 12 s search budget; corpses last 10 minutes.
2. **The archetypes' tuning** is in their YAML and summarized in the matrix. Health stays a same-level body's (M3c's rule): the armour is hard because of plate and its back, not its pool.
3. **Turn rates matter.** A creature faces where it is going only as fast as its body turns, and it strikes only once it faces its target within 30°. Without that, the armour's back could not be reached.
4. **The hound bites on the run** (`advance: true`). It keeps running through its windup and begins the lunge at half its reach, so a fleeing target cannot open the gap the tell would otherwise give.
5. **Calls are species-only**, and only roles that answer calls come.
6. **At rest or returning, a creature takes notice only inside its own ground** (its territory, else the leash). This stops it flickering between giving up and turning back.
7. **A respawn waits until the player is 40 m from home**, so nothing appears under the player's eyes.
8. **Only tier-A creatures act**: one outside tier A holds where it is. Tier transitions are deferred (owner ruling, M4).
9. **The placeholder looks** are chosen in presentation by archetype ID. They carry nothing the simulation reads, and give way to the asset pipeline's bodies.
10. **The TTK table was regenerated** (`M3C_TTK_TABLE.md`). Wolves now turn at a finite rate and flank, so the den's four kill a standing character in 7.25 s instead of 8.05 s, and bare-hand times shift by up to 0.65 s. The asserted rows (every weapon family inside 4-8 s; the valley pair at 13.05 s, inside 8-15 s) still hold.

## Verification

- `dotnet test` (from `src/`, as CI runs it): **515 passed**, 0 failed. Architecture 14, Content 87, Domain 120, EntityRegistry 23, World 58, Persistence 134, Application 79.
- Content lint: 65 definitions, 0 errors.
- Godot 4.7.2: the headless smoke prints `PASS` with all 13 creatures placed. The windowed `--ui-shots` run (ASTRAL) writes the inventory; one picture per archetype where it lives (the den's sleeper under its `z`); the boar's tell and `!` as it notices; the charge landing mid-fight; the kill; the search prompt; the remains panel (Raw Meat x2); the emptied carcass gone; and the death recap. I reviewed every picture.

## Not done, and why

- Pathfinding: creatures move straight and slide along blockers. A target behind a wall is searched for, not pathed to.
- Level bands rolled within a band, habitat-driven spawn selection, and faction rules beyond "every creature is hostile to the player and ignores the others". The prototype's spawners are placed by hand.
- Creatures outside tier A hold still; tier transitions and abstract simulation are deferred to M4 (owner ruling).
- The bible's layout, death flow, spear and starting kit: reconciled incrementally before M6 acceptance (above).
- Real bodies and clips come with Animation Wave 0. The phases the placeholders pose from are the timing a clip must fit.
- The M3 performance gate still waits on the RAZER window.
