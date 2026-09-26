# Citations whose cited lines CHANGED between e10d2c4 and a696931 (not auto-remapped)

Each row: design line (in the pre-reconciliation design) | file | old line(s) at e10d2c4 | approximate new position at a696931 | the design text around it. Verify the new line in the a696931 snapshot and rewrite the citation (and the claim, if the behaviour changed).

## 01_scope

| line | file | old | ~new | context |
|---|---|---|---|---|
| 127 | `docs/M6_STATUS.md` | 221 | 221 | OTOTYPE `:384`) \| ROADMAP M7 entry is silent; the window is still owed (`docs/M6_STATUS.md:221`) \| Not a design blocker. Listed as |
| 209 | `docs/M6_STATUS.md` | 221 | 221 | \| RAZER 1080p/60 window \| Still owed (`docs/M6_STATUS.md:221`). Owner process item; not a design |

## 02_existing_inventory

| line | file | old | ~new | context |
|---|---|---|---|---|
| 248 | `src/World/Runtime/Simulation.cs` | 116-137 | 116-137 | t`, registers carried items, constructs 21 systems plus `QuestDebugger` in a fixed list (`:116-137`), `RequireEverySliceOwned` (`: |
| 380 | `src/Persistence/Migrations.cs` | 69-81 | 70-82 | \| Migrations \| `src/Persistence/Migrations.cs:69-81` \| 12 ordered steps \| EXTENDED: ` |
| 380 | `src/Persistence/Migrations.cs` | 566 | 568 | /Migrations.cs:69-81` \| 12 ordered steps \| EXTENDED: `SchemaV13ToV14` appended; repoints `:566` (V13.EntitiesSection), `:701` (V13 |
| 382 | `src/Persistence/SectionCodec.cs` | 556 | 593 | \| Codec \| `src/Persistence/SectionCodec.cs:21`, `:75`, `:203`, `:409`, `:481`, `:556` \| String-keyed MessagePack; later  |
| 383 | `src/Persistence/SaveLoader.cs` | 146 | 144 |  pass only on a content-hash change, baseline proof, quarantine; hand-built snapshots at `:146`, `:358` \| EXTENDED: the pass cover |
| 383 | `src/Persistence/SaveLoader.cs` | 358 | 369 | ly on a content-hash change, baseline proof, quarantine; hand-built snapshots at `:146`, `:358` \| EXTENDED: the pass covers piece  |
| 385 | `src/World/WorldDelta.cs` | 587 | 626 | ` is the diff authority; `FromSnapshot` validates each record; `EffectiveCellDigest` v1 (`:587`) \| EXTENDED: `PieceRecord`, `NpcEr |
| 410 | `src/Presentation/Main.cs` | 489 | 775 | \| Input gating \| `Main.cs:374-407`, `:489` \| Dialogue takes 1-9; combat needs |
| 413 | `src/Presentation/Player/PlayerController.cs` | 127 | 95 | \| Prediction and focus \| `src/Presentation/Player/PlayerController.cs:127`, `:186`, `:202` \| `Kinematics.Step |
| 414 | `src/Presentation/Perf/PerfRun.cs` | 56 | 54 | \| Camera \| `Player/CameraRig.cs:23`, `:85`; `Perf/PerfRun.cs:56` \| `const MaxDistance = 6f`, read st |
| 418 | `src/Presentation/Main.cs` | 476 | 771 | \| Debug panels \| F3 overlay (`Main.cs:476`); F4 quest debugger (`Ui/JournalPa |
| 419 | `src/Presentation/Main.cs` | 103 | 141 | eltaShots.cs:43`; `UiShots.cs` \| `--playthrough` and `--delta-shots`: fixed seed (`Main.cs:103`), one tick per frame (`:305`), tra |
| 419 | `src/Presentation/Main.cs` | 305 | 533 | ` \| `--playthrough` and `--delta-shots`: fixed seed (`Main.cs:103`), one tick per frame (`:305`), transcripts and `StateDump` JSON |
| 445 | `src/World/WorldDelta.cs` | 520 | 545 | 21. Four sites hand-build a `DeltaSnapshot` (`WorldDelta.cs:520`, `SaveLoader.cs:146`, `:358`, `Bas |
| 445 | `src/Persistence/SaveLoader.cs` | 146 | 144 | 21. Four sites hand-build a `DeltaSnapshot` (`WorldDelta.cs:520`, `SaveLoader.cs:146`, `:358`, `BaselineTransitions.cs:1 |
| 445 | `src/Persistence/SaveLoader.cs` | 358 | 369 | 21. Four sites hand-build a `DeltaSnapshot` (`WorldDelta.cs:520`, `SaveLoader.cs:146`, `:358`, `BaselineTransitions.cs:112`), an |

## 04_building

| line | file | old | ~new | context |
|---|---|---|---|---|
| 1746 | `src/World/Runtime/Items.cs` | 558 | 563 | **The C1 fix.** `InventorySystem.Take`'s corpse clause (`src/World/Runtime/Items.cs:558`) becomes: |

## 06_cross_system

| line | file | old | ~new | context |
|---|---|---|---|---|
| 3449 | `src/Persistence/SaveLoader.cs` | 358 | 369 | the new records or resets `StructureSequence` \| Code rule: `with` copies at `SaveLoader.cs:358` and `BaselineTransitions.cs:112`,  |
| 3487 | `src/World/Runtime/Systems.cs` | 266-267 | 279-280 | -293`) \| A `pce_` branch first (a toggle through `OperatePieceDoor`). The close refusal (`:266-267`) checks every body: the player |
| 3489 | `src/World/Runtime/Items.cs` | 558 | 563 | ite.InstanceId` when set. `Check` refuses a non-owner of a piece chest. The C1 clause at `:558` gains `&& site.InstanceId is null` |
| 3504 | `src/Presentation/Player/PlayerController.cs` | 127 | 95 | \| Presentation \| `PlayerController.cs:127` passes `simulation.Space`. `Camera |
| 3504 | `src/Presentation/Perf/PerfRun.cs` | 56 | 54 | er.cs:127` passes `simulation.Space`. `CameraRig.MaxDistance` stays a `const` (`PerfRun.cs:56` reads it); `BuildMaxDistance = 9f`  |

## 07_persistence

| line | file | old | ~new | context |
|---|---|---|---|---|
| 3748 | `src/Persistence/Migrations.cs` | 566 | 568 | \| `Migrations.cs:566` (`SchemaV8ToV9`) \| `new EntitiesSe |
| 3753 | `src/Persistence/Sections/SchemaV8.cs` | 46 | 47 | 13 precedent of header-only edits). `V8.EntitiesSection` keeps `CreatureDto` (`SchemaV8.cs:46`), which schema 14 does not change. |
| 3811 | `src/Persistence/Migrations.cs` | 69-81 | 70-82 | er it by appending `new SchemaV13ToV14()` to `SchemaMigrations.Production` (`Migrations.cs:69-81`) and setting `SaveFormat.SchemaV |
| 3811 | `src/Persistence/SaveModel.cs` | 20 | 20 | uction` (`Migrations.cs:69-81`) and setting `SaveFormat.SchemaVersion = 14` (`SaveModel.cs:20`). |
| 3832 | `tests/Persistence.Tests/MigrationTests.cs` | 148 | 149 | ays `DecodeEntitySection(bytes).Entities`; the tuple deconstructions at `MigrationTests.cs:148`, `:179` and `:197` switch to prope |
| 3832 | `tests/Persistence.Tests/MigrationTests.cs` | 179 | 179 | odeEntitySection(bytes).Entities`; the tuple deconstructions at `MigrationTests.cs:148`, `:179` and `:197` switch to property acce |
| 3832 | `tests/Persistence.Tests/MigrationTests.cs` | 197 | 198 | ction(bytes).Entities`; the tuple deconstructions at `MigrationTests.cs:148`, `:179` and `:197` switch to property access. |
| 3840 | `src/Persistence/SaveLoader.cs` | 143-146 | 144-144 | \| `SaveLoader.cs:143-146` (decode) \| `var delta = Decode |
| 3841 | `src/Persistence/SaveLoader.cs` | 356-358 | 367-369 | \| `SaveLoader.cs:356-358` (definition pass) \| `delta wit |
| 3843 | `src/World/WorldDelta.cs` | 520 | 545 | \| `WorldDelta.cs:520` (`TakeSnapshot`) \| adds `Pieces`,  |
| 3909 | `src/World/WorldDelta.cs` | 587 | 626 | **`WorldDelta.EffectiveCellDigest` → `unnamed.effective-cell/v2`** (tag at `WorldDelta.cs:587`). Every v1 term keeps its place. A |

## 08_ui

| line | file | old | ~new | context |
|---|---|---|---|---|
| 4383 | `src/Presentation/Main.cs` | 582-583 | 870-870 | - **The prompt suffix** (`Main.cs:582-583`) reads "[Y] Let Kera Voss go h |
| 4410 | `src/Presentation/Perf/PerfRun.cs` | 56 | 54 | nst MaxDistance = 6f` stays (`CameraRig.cs:23`), because `src/Presentation/Perf/PerfRun.cs:56` reads it statically; |
| 4420 | `src/Presentation/Main.cs` | 305 | 533 | Nothing pauses today: `_session.Frame` runs every frame (`Main.cs:305`), and time scale is "debug/pause o |
| 4524 | `src/Presentation/Main.cs` | 342 | 600 | - `ReadInput` and `_UnhandledInput` do not run in scripted modes (`Main.cs:297-300`, `:342`), so beats never press keys. |

## 09_content

| line | file | old | ~new | context |
|---|---|---|---|---|
| 4596 | `src/Presentation/Art/art_bindings.json` | 25-31 | 21-25 |    - The building kit is withheld (`src/Presentation/Art/art_bindings.json:25-31`). |

## 10_slices

| line | file | old | ~new | context |
|---|---|---|---|---|
| 4921 | `tests/Persistence.Tests/MigrationTests.cs` | 78 | 78 | n` regeneration and hard-coded step-list edits (`tests/Persistence.Tests/MigrationTests.cs:78`, `:398`, `:428`; `HistoricalFixture |
| 4921 | `tests/Persistence.Tests/MigrationTests.cs` | 398 | 435 | neration and hard-coded step-list edits (`tests/Persistence.Tests/MigrationTests.cs:78`, `:398`, `:428`; `HistoricalFixtureTests.c |
| 4921 | `tests/Persistence.Tests/MigrationTests.cs` | 428 | 466 |  and hard-coded step-list edits (`tests/Persistence.Tests/MigrationTests.cs:78`, `:398`, `:428`; `HistoricalFixtureTests.cs:241`). |
| 4935 | `src/World/WorldDelta.cs` | 520 | 545 | ctureSequence` and errands live in `WorldDelta` and survive `TakeSnapshot` (`WorldDelta.cs:520`). |
| 5088 | `src/Presentation/Main.cs` | 103 | 141 | lta-shots` (the value list `Main.cs:1045`, the scratch-profile branch `:88-91`, the seed `:103`, one tick a frame `:305`, the inpu |
| 5088 | `src/Presentation/Main.cs` | 305 | 533 | t `Main.cs:1045`, the scratch-profile branch `:88-91`, the seed `:103`, one tick a frame `:305`, the input guard `:342`). Until E5 |
| 5088 | `src/Presentation/Main.cs` | 342 | 600 | ratch-profile branch `:88-91`, the seed `:103`, one tick a frame `:305`, the input guard `:342`). Until E5 it starts a new game at |
| 5143 | `src/Persistence/Migrations.cs` | 566 | 568 |   - the four repoints (`Migrations.cs:566`, `:701`, `:722`; `Sections/SchemaV |
| 5145 | `src/Persistence/SaveModel.cs` | 20 | 20 |   - `SaveFormat.SchemaVersion = 14` (`SaveModel.cs:20`); |
| 5295 | `src/World/Runtime/Systems.cs` | 266-267 | 279-280 |   - the close refusal (`Systems.cs:266-267`) extended to every `State.Npcs |
| 5704 | `src/Presentation/Main.cs` | 305 | 533 | ms`, `ticks` and `save_ms`: a `Stopwatch` around `_session.Frame(...)` in `Main` (`Main.cs:305`), `FrameResult.TicksRun` (`GameSes |
| 5704 | `src/Application/GameSession.cs` | 43 | 51 | d `_session.Frame(...)` in `Main` (`Main.cs:305`), `FrameResult.TicksRun` (`GameSession.cs:43`), and a separately timed `save_ms`  |
| 5704 | `src/Presentation/Main.cs` | 930-934 | 1224-1253 | un` (`GameSession.cs:43`), and a separately timed `save_ms` when `QuickSave` ran (`Main.cs:930-934`) \| `src/Presentation/Perf/Fram |

## 14_performance

| line | file | old | ~new | context |
|---|---|---|---|---|
| 6598 | `docs/M6_STATUS.md` | 221 | 221 | lated has been measured, and nothing at all has been measured on RAZER (`docs/M6_STATUS.md:221`). |
| 6620 | `src/Application/GameSession.cs` | 194 | 343 | - Autosave runs inside `Frame` every 300 s of playtime (`GameSession.cs:194`; `src/Persistence/SaveStore.cs:41` |
| 6640 | `docs/M6_STATUS.md` | 85 | 85 | t 798-840 fps, with a p99 frame of 3.5-4.1 ms and a GPU p99 of 0.21 ms (`docs/M6_STATUS.md:85`). |
| 6889 | `src/Presentation/Main.cs` | 305 | 533 | // src/Presentation/Main.cs, around _session.Frame (:305) and _stats?.Record (:309) |

## 15_risk

| line | file | old | ~new | context |
|---|---|---|---|---|
| 7096 | `src/Persistence/SaveLoader.cs` | 358 | 369 |  \| high: silent loss of player work, RK-11's worst class \| `with` copies at `SaveLoader.cs:358` and `BaselineTransitions.cs:112`;  |
| 7098 | `src/World/Runtime/Items.cs` | 558 | 563 | game crashes mid-tick, and progress since the last save is lost \| The C1 clause (`Items.cs:558` gains `&& site.InstanceId is null` |
| 7121 | `src/Presentation/Art/art_bindings.json` | 25-31 | 21-25 | sset dependencies.** The building kit is withheld (`src/Presentation/Art/art_bindings.json:25-31`); the chest and anvil models are |
| 7124 | `docs/M3_STATUS.md` | 4 | 4 | \| R-X9 \| **The RAZER window is still owed** (`docs/M3_STATUS.md:4`; `docs/M6_STATUS.md:221`) \| high: M7 |
| 7124 | `docs/M6_STATUS.md` | 221 | 221 | \| R-X9 \| **The RAZER window is still owed** (`docs/M3_STATUS.md:4`; `docs/M6_STATUS.md:221`) \| high: M7 will be built before i |
| 7152 | `src/Presentation/Perf/PerfRun.cs` | 56 | 54 | \| `CameraRig.MaxDistance` becoming an instance field and breaking `PerfRun.cs:56` \| audit_code F3 \| L7: the const sta |

