# Phase-1 audio event contract

This document is for **Claude**, who owns gameplay. It says what audio needs to know, not how to
implement it. No gameplay code is prescribed or implied.

Everything here is backed by `assets/manifests/playable_prototype_audio.json`, which carries the
stable id, the file, the channels, the loop flag, the target loudness and the measured values for
every sound. The names below are the contract; the manifest is the data.

**Naming.** Stable ids are dotted and semantic: `sfx.player.footstep.dirt.walk.01`,
`amb.charwood_verge.01`. They are not filenames you should parse — look them up in the manifest.
Variation families are numbered `…01`, `…02`, `…03`; a family is meant to be picked from at random
per event, with the same variation not repeating back to back.

---

## 1. What audio needs, in one line each

| Audio needs to know | Because |
|---|---|
| **Surface** under the player | Three footstep families exist (dirt, stone, wood). There is no default. |
| **Gait** | `walk` and `run` are separate families. Sprint reuses `run`, per the brief. |
| **Weapon family** | Sword, bow and polearm do not share a swing sound. |
| **Target material** | Blade-into-flesh and blade-into-plate are different assets. A single generic "impact" is the failure this set exists to prevent. |
| **Severity** | Light and heavy swings are separate. Impacts are not yet split by severity — see §4. |
| **Creature archetype** | All five have their own identity set. They are not pitch-shifted from one another. |
| **Strain band** | Moderate / high / critical are three separate layers, meant to cross-fade. |
| **Whether a loop is ending** | Area ambience is a looping bed per cell, not a one-shot. |

## 2. PLAYER

| Event | Needs | Ids |
|---|---|---|
| `footstep(surface, gait)` | surface ∈ {dirt, stone, wood}; gait ∈ {walk, run}. Sprint may use `run`. | `sfx.player.footstep.<surface>.<gait>.01…04` (dirt 4 variants, stone and wood 3 each) |
| `land(surface)` | surface ∈ {dirt, stone} | `sfx.player.land.<surface>.01` |
| `jump_effort()` | — | `sfx.player.jump.effort.01` |
| `hurt(severity)` | severity ∈ {light, heavy} | `sfx.player.hurt.light.01…03`, `sfx.player.hurt.heavy.01…02` |
| `downed()` | entered the downed state | `sfx.player.downed.01` |
| `death()` | the final collapse | `sfx.player.death.01` |
| `crouch_enter()` / `crouch_exit()` | stance change | `sfx.player.crouch.enter.01`, `sfx.player.crouch.exit.01` |
| `gear_shift()` | cloth and equipment settling, e.g. on a turn or an equip | `sfx.player.gear.cloth.01…03`, `sfx.player.gear.metal.01…03` |

`gear_shift` is the one to be careful with: cloth and metal are separate families because leather
shifting and a buckle striking are different events, and the metal set is loud enough to mask a
footstep. Fire it on a real equipment change rather than on movement, or it becomes clutter.

Footsteps are mono and positional. All four dirt walk variants should be cycled, not one repeated:
this is the single most-repeated sound in the game.

## 3. WEAPON

| Event | Needs | Ids |
|---|---|---|
| `draw(weapon_family)` | sword only | `sfx.weapon.sword.draw.01` |
| `swing(weapon_family, intensity)` | sword light/heavy; polearm thrust; bow release | `sfx.weapon.sword.swing.light.01…03`, `…heavy.01…02`, `sfx.weapon.polearm.thrust.01…03`, `sfx.weapon.bow.release.01…03` |
| `draw_bow()` | bow only | `sfx.weapon.bow.draw.01…02` |
| `projectile_flight(weapon_family)` | bow only, optional | `sfx.weapon.bow.arrow.flight.01` |
| `impact(weapon_family, target_material)` | see §4 | the matrix below |
| `block()` | sword only, if the event is exposed | `sfx.weapon.sword.block.01…03` |
| `handle(weapon_family)` | polearm shaft adjustment | `sfx.weapon.polearm.handle.01…02` |
| `ready(weapon_family)` | polearm brought to guard | `sfx.player.weapon.spear.ready.01` |
| `nock()` | bow only, arrow onto the string | `sfx.player.weapon.bow.nock.01` |
| `sheathe(weapon_family)` | sword only | `sfx.player.weapon.sword.sheath.01` |

## 4. The impact matrix

This is the part worth reading twice. The set is named so that
**weapon family × target material** resolves to a family, and the variation is chosen within it.

| Weapon | flesh | plate | wood | stone |
|---|---|---|---|---|
| sword | `sfx.weapon.sword.impact.flesh.01…03` | `sfx.weapon.sword.impact.plate.01…03` | `sfx.weapon.sword.impact.wood.01…02` | `sfx.weapon.sword.impact.stone.01…02` |
| arrow | `sfx.weapon.bow.arrow.impact.flesh.01…03` | — | `sfx.weapon.bow.arrow.impact.wood.01…03` | `sfx.weapon.bow.arrow.impact.stone.01…02` |
| spear | `sfx.weapon.polearm.impact.flesh.01…03` | `sfx.weapon.polearm.impact.plate.01…03` | **aliases sword wood** | **aliases sword stone** |

The spear's wood and stone impacts are declared `alias_of` the sword's rather than being duplicated
under two names. Section 11 of the brief asks for reuse where it is physically sensible; a metal tip
into timber does not change much with the length of the shaft behind it. Resolve an alias by reading
`alias_of` and loading that id's file — there is no second copy on disk to drift out of sync.

**Not yet in the set:** impacts are not split by *severity*. Light and heavy are distinguished for
swings, not for impacts. If combat ends up needing "blade on plate, hard" versus "blade on plate,
glancing", that is a real gap and a small one to fill — say so and it can be generated.

## 5. CREATURE

Every one of the five archetypes has the same five events. `creature` ∈
{`ash_ember_hound`, `bone_walker_husk`, `animated_armour`, `bristleback_boar`,
`cave_hunting_spider`}.

| Event | Ids | Count |
|---|---|---|
| `idle()` | `sfx.creature.<creature>.idle.01…02` | 2 |
| `alert()` | `sfx.creature.<creature>.alert.01…02` | 2 |
| `attack()` | `sfx.creature.<creature>.attack.01…03` | 3 |
| `hurt()` | `sfx.creature.<creature>.hurt.01…02` | 2 |
| `death()` | `sfx.creature.<creature>.death.01…02` | 2 |

Every creature vocalisation is mono and positional. `idle` is the longest and quietest; it should
not be triggered more often than roughly every 8–15 s per creature or it becomes a loop rather than
an ambience. `attack` is timed to fire with the attack animation's commit, not on the state change.

### Locomotion

One sound is **one footfall** for the four walking archetypes, fired per step and cycled, exactly as
the player's footsteps are. This is a deliberate choice and not an oversight: a single footfall read
as a unit is what lets the engine drive creature movement from the same per-step hook the player uses,
and it is why these are short. Do not treat them as a walk cycle.

| Event | Ids | Count |
|---|---|---|
| `walk_step()` | `sfx.creature.<creature>.walk.01…03` | 3 |
| `run_step()` | `sfx.creature.ash_ember_hound.run.01…03` | 3 |
| `move_fast_step()` | `sfx.creature.bone_walker_husk.move_fast.01…02` | 2 |
| `move_heavy_step()` | `sfx.creature.animated_armour.move_heavy.01…02` | 2 |
| `charge_step()` | `sfx.creature.bristleback_boar.charge.01…03` | 3 |
| `scuttle_step()` | `sfx.creature.cave_hunting_spider.scuttle.01…04` | 4 |
| `scuttle_fast_step()` | `sfx.creature.cave_hunting_spider.scuttle_fast.01…04` | 4 |

**The spider is the exception and matters.** A spider's eight legs move in overlapping groups, so a
single footfall is not a meaningful unit for it. Its sounds contain **two steps each** and are meant
to be cycled across the engine's repeat: `scuttle` is 0.70 s over two steps, 350 ms each, and
`scuttle_fast` is 0.60 s over two, 300 ms each. They carry four variants rather than three, matching
the player's dirt footsteps, because the engine picks at random per event and must not repeat one
back to back.

Every locomotion sound is mono and positional. `walk` is the most-repeated per creature and wants its
variants cycled; `charge` and the two spider families are the fastest and want the least variation
between repeats.

## 6. MAGIC

The three formulas are Force, Warding and Vital. They do not share sounds.

| Formula | Event | Ids |
|---|---|---|
| **Impulse Bolt** (Force) | `cast_start()` | `sfx.magic.impulse_bolt.cast.01…02` |
| | `travel()` | `sfx.magic.impulse_bolt.travel.01` |
| | `impact()` | `sfx.magic.impulse_bolt.impact.01…03` |
| **Brace Ward** (Warding) | `activate()` | `sfx.magic.brace_ward.activate.01…02` |
| | `ward_hit()` | `sfx.magic.brace_ward.hit.01…02` |
| | `end()` | `sfx.magic.brace_ward.end.01` |
| **Mending Thread** (Vital) | `cast_start()` | `sfx.magic.mending_thread.cast.01…02` |
| | `resolve()` | `sfx.magic.mending_thread.resolve.01` |

`cast_start` should be fired from the animation's `windup_start` event and the effect from
`spell_release`, both of which are declared in the animation registry — not from a hard-coded frame.

### Strain

Strain is a continuous value, not a resource bar. Three layers cross-fade with it:

| Band | Id | Stereo | Notes |
|---|---|---|---|
| moderate | `sfx.magic.strain.moderate.01` | yes | loop, ~3 s |
| high | `sfx.magic.strain.high.01` | yes | loop, ~3 s |
| critical | `sfx.magic.strain.critical.01` | yes | loop, ~3 s |

These are **stereo and non-positional** on purpose: Strain is a property of the caster, so a
positional one-shot would be wrong. They are mixed low (their target is −32 LUFS, the quietest group
in the set) and they loop, so they want to live on a persistent player, not be re-triggered.

## 7. WORLD AND INTERACTION

| Event | Ids |
|---|---|
| `interaction_pickup()` | `sfx.interaction.pickup.01…03` |
| `interaction_open()` / `interaction_close()` — chest | `sfx.interaction.chest.open.01`, `sfx.interaction.chest.close.01` |
| `interaction_open()` / `interaction_close()` — door | `sfx.interaction.door.open.01`, `sfx.interaction.door.close.01` |
| `gather_hit()` — pick into the iron seam | `sfx.crafting.mining.strike.01…03` |
| `gather_complete()` — ore breaks free | `sfx.crafting.ore.break.01` |
| `station_ambience()` — the forge, looping | `sfx.crafting.forge.ambience.01` |
| `craft_hit()` — hammer on anvil | `sfx.crafting.anvil.strike.01…04` |
| `craft_complete()` | `sfx.crafting.complete.01` |
| `loot_take_all()` | `sfx.interaction.loot.take_all.01` |

The bible's craft chain is raw iron ore → Iron Billet → March Spear. `gather_hit` is the mining
strike against the node; `craft_hit` is the anvil, and it is the one that wants four variations
because a forge session plays it dozens of times.

## 8. AMBIENCE

One looping bed per cell, stereo, non-positional:

| Cell | Id |
|---|---|
| A — Ashen Hollow Waystation | `amb.ashen_hollow.waystation.01` |
| B — Charwood Verge | `amb.charwood_verge.01` |
| C — Blackvein Cut | `amb.blackvein_cut.01` |
| D — Foldscar Ruin | `amb.foldscar_ruin.01` |

Each is ~20 s, stereo, and cross-faded at its own seam so it wraps continuously. Cross-fade the four
on cell entry rather than hard-cutting. The Foldscar bed is deliberately almost devoid of wildlife
with a faint tonal instability under the wind — it should read as *wrong*, not as horror, so nothing
should be layered on top of it to "fix" the quiet.

### Cell detail one-shots

Sparse events that sit **on top of** the looping bed rather than replacing it. Mono and positional,
so they can be placed in the world; trigger them at irregular intervals and never on a fixed cadence,
or they read as a loop.

| Cell | Event | Ids |
|---|---|---|
| A — Ashen Hollow Waystation | `cell_detail()` | `sfx.amb.ashen_hollow.forge_distant.01`, `sfx.amb.ashen_hollow.timber_creak.01`, `sfx.amb.ashen_hollow.settlement_activity.01` |
| B — Charwood Verge | `cell_detail()` | `sfx.amb.charwood.branch_movement.01`, `sfx.amb.charwood.raven_call.01`, `sfx.amb.charwood.stream_detail.01` |
| C — Blackvein Cut | `cell_detail()` | `sfx.amb.blackvein.pebble_fall.01`, `sfx.amb.blackvein.rock_shift.01`, `sfx.amb.blackvein.winch_creak.01` |
| D — Foldscar Ruin | `cell_detail()` | `sfx.amb.foldscar.stone_resonance.01`, `sfx.amb.foldscar.tone_displacement.01` |

Foldscar carries two details rather than three on purpose. It is the cell whose silence is the point,
and the two it has are the ones that reinforce the wrongness rather than relieving it.

## 9. UI

| Event | Id | Priority |
|---|---|---|
| `menu_open()` | `sfx.ui.menu.open` | P1 |
| `menu_close()` | `sfx.ui.menu.close` | P1 |
| `confirm()` | `sfx.ui.select` | P1 |
| `back()` | `sfx.ui.back` | P1 |
| `equip()` | `sfx.ui.equip` | P1 |
| `error()` | `sfx.ui.error` | P1 |

UI sounds are stereo and non-positional. **The whole UI group is P1 and is the weakest family in the
set** — `sfx.ui.select` measured and looked like a short low-frequency thump rather than a wooden
tick. It is functional and it is not worth blocking on; if it bothers you in play, say so and it will
be regenerated with a different prompt rather than patched.

## 10. What audio needs from gameplay, and what it does not

**Needs:** the event, and the two or three discriminators listed in §1. Nothing else. No frame
numbers, no blend weights, no mix decisions.

**Does not need:** audio to know about damage numbers, health, inventory or AI. Sound is triggered by
an event that has already happened.

**Volume in code:** don't hard-code linear volumes. Each sound is already levelled to a relative
target (a footstep at −30 LUFS, a boar impact at −16) so the *relative* mix is already correct.
Uniform playback volume across the set, with per-category bus trims if needed, preserves that.

**Known gap:** there is no music, and none is planned in this sprint. The set is effects and
ambience only.
