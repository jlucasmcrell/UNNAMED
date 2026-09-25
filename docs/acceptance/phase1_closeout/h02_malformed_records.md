# H-02 at runtime: malformed asset records (Phase-1 closeout, 2026-09-25)

Build: the integration branch at `b5cfb27` (Presentation code identical to `29d4f45`). Asset root: a scratch copy of the package's `assets/`
(609 files, byte-identical to the Phase-A snapshot manifest before and after the test). Command:
`godot --headless --path src/Presentation -- --smoke --asset-root <copy>`. The unit-level matrix is `tests/Presentation.Tests/ArtRecordsTests.cs`.

## One run, thirteen records broken at once

Each record degraded on its own - one line naming it and why, greybox for that one thing - and nothing else changed.

| # | Record | Broken as | Logged |
|---|---|---|---|
| M1 | `material_packed_dirt_ground` | `maps.basecolor: null` | maps.basecolor is null, not a file name |
| M2 | `material_loose_gravel` | `tile_size_m: "2.0"` | tile_size_m is "2.0", not a positive number of metres |
| M3 | `material_limestone_ashlar` | `maps.basecolor: "../../../escape.png"` | maps.basecolor (../../../escape.png) is not a file in the material's folder |
| M4 | `material_churned_wet_mud` | `{not json` | it is not JSON (...) |
| M5 | `material_marsh_grass_turf` | empty file | it is not JSON (...) |
| M6 | `material_rubble_stone_wall` | `pbr: 1` | pbr is a number, not an object |
| C1 | `anim.player.veth_wanderer.idle` | `events: 7` | events is a number, not a list |
| C2 | `anim.player.veth_wanderer.walk` | `{not json` | it is not JSON (...) |
| C3 | `anim.npc.kal_smith.idle` | `duration_s: -1` | duration_s is -1, not a length in seconds |
| C4 | `anim.creature.frost_wolf.walk` | `events[0].time: "0.0"` | event 0 (foot_contact) has no time in seconds |
| C5 | `anim.npc.veth_magistrate.idle` | empty file | it is not JSON (...) |
| C6 | `anim.creature.frost_wolf.run` | `events[0].id: null` | event 0 has no id (it is null) |
| C7 | `anim.creature.bristleback_boar.idle` | `loop: "yes"` | loop is "yes", not true or false |

| | Clean copy | Thirteen broken |
|---|---|---|
| Smoke | PASS, exit 0 | PASS, exit 0 |
| Boot line | 115 drawn, 0 withheld | 109 drawn, 8 withheld or unavailable |
| Art coverage | 169 requested, 168 resolved, 1 fallback, 0 unexpected | 169 requested, 162 resolved, 7 fallbacks, 6 unexpected |
| Exceptions in the log | 0 | 0 |
| `building the scene from the asset library failed` | absent | absent |

The creature and NPC clips (C3-C7) were first asked for during play, after the boot line: each was reported once, with no
per-frame repeat. The coverage gate counts the broken records as unexpected, which is what fails a package gate (`run_package.ps1`).
The smoke's save/load digest differs between runs only because the smoke picks a new world seed each run (the log's `world: seed`).

## The bindings file (`src/Presentation/Art/art_bindings.json`, compiled into the package's `.pck`)

| Case | Result |
|---|---|
| A creature binding naming a model that does not exist | PASS: one line (`creature_does_not_exist: no file at rigged\...`), that creature greybox, coverage 1 unexpected, 0 exceptions |
| `steps_per_sound: "2"` (a string where a number belongs) | The whole file is refused with one warning and every bound thing is drawn greybox (1 drawn, 107 unexpected); 0 exceptions; smoke PASS |
| A creature binding with no `model` key | Residual: `ArgumentNullException` from `ArtLibrary.Rigged` through `SkinnedCreature.Create` in `CreaturesView.Draw`, every frame (765 times in the smoke), which stops the rest of that frame's `Main.Draw`; the smoke still PASSes and coverage does not see it |

The residual is in a repository file the game builds into its pack, not in the asset library: the shipped file has a model for every
creature and person, so no player build can reach it. It is carried to M7 as a follow-up (a shape check of `art_bindings.json` in
`Presentation.Tests`, or skipping a binding with no model the way projectiles already are).

Logs: `G:\UNNAMED_HISTORY\phase1_integration_20260925\h02\` (baseline, malformed, bindings_int, bindings_nomodel, bindings_unknown, and
the script that broke and restored the records).
