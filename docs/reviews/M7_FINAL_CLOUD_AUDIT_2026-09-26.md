# M7 final independent cloud audit (2026-09-26)

**Milestone:** M7, Factions, Reputation and Building v1.
**Reviewer:** an independent cloud review agent. It did not implement M7, modified no implementation file, and wrote only this report.
**Executed:** 2026-09-26/27 UTC, in a Linux cloud container (4 vCPU, .NET SDK 8.0.131). The container has no Godot and no Windows.

## Verdict

**READY WITH ACCEPTED MINOR OBSERVATIONS.**

- No blocker and no major finding survived verification.
- The implementation matches the design and the owner's rulings in every dimension audited.
- Every pre-M7 test keeps its strength.
- `1278b9c` → `ac8d9d8` changes documentation and evidence only.
- The cloud build and the full test suite reproduce the reported results.

The findings are minor documentation gaps, minor test-strength gaps, and latent edge cases that no shipped content or normal save reaches (§12). None of them needs a code change before the merge. One of them, M-1 (recording the owner's COLD_FIRST_BENCH_COMMIT classification), should be written into the M7 documents at or before the merge, because the owner's condition for that classification is that it remains documented.

This verdict rests on code and test evidence. The target-hardware performance numbers and the Godot runtime runs were **not** reproduced in the cloud; §14 lists exactly what was not.

---

## 1. Reviewed hashes

| Role | Hash | Verified how |
|---|---|---|
| Pre-M7 `main` | `a69693108b89d471cdb409a229ba0bdf78b08fa4` | Equals `origin/main`. Equals the base of PR [jlucasmcrell/UNNAMED#8](https://github.com/jlucasmcrell/UNNAMED/pull/8). |
| Final runtime-tested commit | `1278b9cf18e9b9c70a0d4e59f1018b91d1d1080c` | A descendant of `a696931`. CI run 36270151122 ran on it. |
| M7 branch head | `ac8d9d82779e67a28d9c9541338047be3f911767` | Equals `origin/claude/m7-factions-building`. Equals the head of PR jlucasmcrell/UNNAMED#8. |
| Review branch head | `17e638c38b33af4d175dc927599a8dd4faefa823` | Its parent is `ac8d9d8`. It adds only `docs/reviews/m7_audit_inputs/` (97 files). |

- **Ancestry:** `a696931` < `1278b9c` < `ac8d9d8` < `17e638c`.
- **Commits:** 70 commits in `a696931..ac8d9d8`, all by one author.
- **Size:** `a696931..1278b9c` changes 253 files (+28,231 / −336). Of that, `src` is 80 files (+12,238 / −198) and `tests` is 132 files (+14,228 / −96).
- **The Phase-B branch:** `origin/claude/phase-b-visual-overhaul` is at `df5362f`, as the handoff states.

## 2. Is `1278b9c` → `ac8d9d8` documentation and evidence only?

**Yes, verified.**

- **The last code-changing commit is `b505deb`.** It made `WorldDelta.NpcErrands` internal again (the S1 correction).
- **`1278b9c` itself changes only documentation:** `AGENTS.md`, `README.md`, `docs/DATA_MODEL.md`, `docs/M7_STATUS.md`, `docs/RISK_REGISTER.md`, `docs/SYSTEMS.md` and `docs/WORLD_ARCHITECTURE.md`. So the tree that was runtime-tested has exactly `b505deb`'s code.
- **`1278b9c..ac8d9d8` touches 81 files, all under `docs/`:**
  - `docs/acceptance/m7/**`;
  - `docs/acceptance/m7_build/**`;
  - `docs/M7_STATUS.md`;
  - `docs/M7_VISUAL_INTEGRATION_HANDOFF.md`.
- **No path outside `docs/` changed** (`git diff --name-only 1278b9c ac8d9d8 | grep -v '^docs/'` is empty).
- **No test is affected by those doc changes.** Four documents are read by tests:
  - `docs/M7_REPUTATION_TABLE.md` (`tests/Application.Tests/ReputationTableTests.cs:263`);
  - `docs/M3D_BEHAVIOUR_MATRIX.md`;
  - `docs/M3C_TTK_TABLE.md`;
  - `docs/M2_STATUS.md`.

  None of them changed after `1278b9c`, and no test enumerates `docs/`.
- **The review commit `17e638c`** adds only `docs/reviews/m7_audit_inputs/**`, which no code or test reads.

There is therefore no untested implementation after the final tested commit.

## 3. Scope compliance

**No scope creep found.**

- **Deferred features are reserved names only.** A keyword sweep of the M7 diff (crime, bounty, pardon, territory, hostility, witness, rumour, settlement) finds only:
  - refusal messages in `src/Content/FactionContent.cs` ("crime is not built", "hostility is not a faction field", `territory`, `witness` "M9");
  - the reserved `ActKinds.NotBuilt` entries (`src/Domain/Factions/Factions.cs:20`);
  - `KnowledgeSources.Witnessed`.

  Nothing consumes them. The `Territory` hits are M3d creature territory, which predates M7. The design's forbidden grep (`Bounty|Pardon|WarState|StandingDecay|BuildingCounters|FactionCounters`) returns nothing.
- **Faction relations and attitudes are inert data.** They are read only by a view and the F6 debug panel (`src/World/Runtime/Factions.cs:62,136`).
- **Factions.**
  - There are two factions:
    - the Waystation: Animated Armour kill +100;
    - the Survey: Animated Armour kill −100, and `switch_set world.foldscar.steadied` +100, a reaction the design names at `M7_IMPLEMENTATION_DESIGN.md:2599`.
  - Knowledge is report-only.
  - The log holds 256 acts, compacted oldest first, and standing is unchanged by compaction.
- **Combat and creatures.** The changes are limited to what M7 needs:
  - `Combat.cs` +93: melee damage to pieces, the shared `FirstStop` line march, and the vitals clock (schema 16).
  - `Creatures.cs` +32: the built space and sight walls, the attack in progress (schema 17), the `CreatureKilled` act, and `SpillContainer`.
  - `Magic.cs`, all progression code and all progression content are unchanged.
- **Content.** 21 files: the building pieces, timber, factions, the M7 dialogue lines and NPC `faction_ref`s, the build area and the timber stack. There is no M8 content.
- **Presentation.** Greybox and UI only: build mode, the structures view, the F2 navigation overlay, the debug panels and the palette. There is no production art:
  - `src/Presentation/Art/**` and `src/Presentation/Audio/**` are untouched;
  - all 7 `piece:*` entries report greybox fallbacks and are not allowlisted.
- **DeepSeek-owned paths are untouched** over `a696931..ac8d9d8`: `tools/asset_pipeline`, `tools/godot_validate`, `assets/`, `docs/WAVE_0_*`, `docs/ANIMATION_*`, `CANONICAL_BODY_AND_SKELETON.md` and `ASTRAL_HOST.md`.
- **Phase B** is not an input. No Phase-B file is in the M7 diff.

## 4. Persistence and schema review (14 → 15 → 16 → 17)

**Sound.** The M6 acceptance save migrates, and every older schema loads.

- **The migration chain is complete.**
  - `Migrations.Production` holds 16 steps covering schemas 1→17 (`src/Persistence/Migrations.cs:70-89`).
  - `TryChain` refuses a gap.
  - The full order is asserted (`tests/Persistence.Tests/MigrationTests.cs:78-93` and the per-fixture step lists).
  - Migrations are pure array maps, with no clock, random source or dictionary order. The 15→16 and 16→17 steps are tested byte-identical on a second run.
- **Frozen shapes decode the old bytes.**
  - `V14.Player` and `V14.Companion` match the pre-M7 DTOs key for key.
  - `V16.Creature`, `V16.CreatureContinuation` and `V14.EntitiesSection` match theirs.
  - `SaveFormat.Current` is still 1, and the save is still four files.
- **Fixtures.**
  - v15, v16 and v17 were added.
  - The v1–v14 binaries are byte-identical to `a696931`.
  - v1–v14 `expected.json` gain only default-valued fields: `factions`, `vitals`, `structure_seq`, `pieces: []`, `npc_errands: []`, `creatures[].attack: null` (v8+) and a `none` route (v12+). **No existing leaf changed or disappeared.** This is `Fixtures/README.md` policy 3.
  - Every schema from 1 to 17 loads against its expected state (`HistoricalFixtureTests.ShippedSchemas`).
- **Schema 16 persists exactly the ruled eight fields.**
  - The fields are `last_combat_tick` (the last blow taken *or dealt*), `last_exertion_tick`, `last_cast_tick`, and `health_milli`, `stamina_milli`, `focus_milli`, `strain_milli` and `sprint_milli`.
  - They are saved at `src/World/Runtime/Simulation.cs:419` and restored at `RuntimeState.cs:126`.
  - Older saves migrate to `VitalsClock.Rested`: three "never" ticks and zero accumulators.
- **Schema 17 is additive and minimal.**
  - It stores `start_tick` and `struck` only. There is no animation, VFX or audio state.
  - The 16→17 step reads the frozen `V16` shape and adds only `attack`; every other key re-encodes identically (`Schema17Tests.cs:88-133`).
  - The v16 and v17 player and cells bytes are identical.
  - Older saves migrate to "no attack in progress".
  - The blow's roll is keyed on the attacker, the target, the body's cell and the impact tick, all of which are saved (`Combat.cs:720, 905-908`). Nothing is keyed on the attack's start.
- **Invalid attack targets resolve deterministically.**
  - No target is stored: the foe is re-picked each tick, so a target cannot go stale.
  - `WorldDelta.cs:1136-1139` rejects a creature record whose attack is impossible:
    - an attack on a creature that is not alive;
    - an attack during a stagger;
    - a start tick below 0;
    - a `struck` value without an attack;
    - a `struck` value that is not a character or NPC.
  - The whole creature record is dropped with an audit warning (`SaveLoader.cs:193-194`), which matches the existing policy for impossible creature state and `PERSISTENCE.md` §5.3. Tested at `Schema17Tests.cs:48-71`.
- **Pieces and errands are proven by their host cells' `baseline_hash`.**
  - A missing baseline row blocks the load (`SaveLoader.cs:524-548`).
  - The encoder refuses to write a record without one (`SectionCodec.cs:745-762`).
- **The navigation grid is never saved.** No `NavGrid`, `NavTile`, `NavScratch` or `NavCounters` type appears in `src/Persistence`, and guard G3 enforces this (`tests/Architecture.Tests/M7GuardTests.cs:60`).
- **The M6 acceptance save migrates.**
  - `TheM6AcceptanceSave_LoadsIntoM7_NothingBuiltNeutralNoErrand` asserts exactly five steps, 12→13 … 16→17 (`tests/Application.Tests/GameSaveTests.cs:123-128`).
  - It asserts no rebased cells, mismatches, blockers or losses, and an unchanged worldgen fingerprint.
  - It asserts nothing built, sequence 0, no errand, an empty ledger, every faction neutral, and Tavar's route `none`.
  - It asserts that the pre-migration copy is byte-identical to the committed save, and that a reload equals the state before it.
- **Forward incompatibility is refused and documented.**
  - A newer schema is refused ("schema N is newer than this build", `SaveLoader.cs:103-106`).
  - It is documented in `docs/M7_VISUAL_INTEGRATION_HANDOFF.md` §2 and generically in `PERSISTENCE.md`.
  - The refusal itself has no test (m-8).
- **`PERSISTENCE.md` matches the code** on the 14→15, 15→16 and 16→17 rows, the schema-17 load rules, and the split between transient and persisted state.

## 5. Async-save review (the single-writer save lane)

**Matches the owner's ruling.** Source: `src/Application/SaveLane.cs`, commit `682c449`.

- **One lane per session.** It is one dedicated long-lived thread, started by the first background save (`SaveLane.cs:71-76`, `GameSession.cs:273`). It is not a thread per save, not a larger timeout, and not polling.
- **Ordering.** The queue is first in, first out. The next save is taken under the lane's lock and run outside it. The queue is re-checked in a `while` before waiting, so no wake-up is lost (`SaveLane.cs:85-110`).
- **Single writer.** A synchronous `Save` or a `Load` first waits for every queued save (`GameSession.cs:222, 242`).
- **Atomic writes and the profile lock are unchanged.** `SaveStore`'s commit and `ProfileLock` are untouched by M7.
- **The one-pending-autosave rule is unchanged** (`GameSession.cs:359`).
- **Failures propagate.**
  - A throwing save faults only its own task; the loop continues (`:100-108`).
  - The failure reaches the player as `SaveOutcome.Failure`, polled on the frame thread in order (`GameSession.cs:297-307`).
  - No exception is swallowed.
- **No deadlocks.**
  - The lane's lock is never held during a save, and `Close` joins outside it (`:114-121`).
  - The lane never touches the profile lock, never waits on itself, and has no synchronisation context.
- **Shutdown.**
  - Closing takes no more saves and drains the queued ones.
  - Enqueueing after close throws instead of dropping the save (`:69`).
  - `_ExitTree` waits up to 10 s. Because the queue is first in, first out, the last task finishing means all have.
- **No lost or reordered saves** could be constructed from the code.
- **The background worker is never authority for simulation order.**
  - The lane encodes only the captured, immutable document (`SaveStore.cs:215-221`).
  - It never calls back into the simulation.
  - Completion is observed by the frame thread.
- **Tests.**
  - `SaveLaneTests` cover:
    - ordering;
    - one save at a time;
    - a throwing save;
    - closing while queued and while running;
    - thread lifetime;
    - a save beginning while every pool thread is held.
  - `AsyncSaveTests.cs` is byte-identical to `a696931`, so its 5 s budget (500 × 10 ms) is unchanged.

Findings: m-6 (a thread that outlives an undisposed session, test processes only), and observations O-5 and O-6.

## 6. Navigation and building review

**Correct as designed.**

- **The grid.**
  - It is integer-only; the Domain navigation and building files contain no floating point.
  - It uses a 250 mm lattice on global indices, one tile per 100 m cell.
  - It is derived from exactly what `Kinematics` collides with.
  - It is never saved.
  - Seams do not exist in the data: N-D9 (a)-(e), N-D10 and N-W1.
- **The planner.**
  - It is optimal A* with a total `(f, h, index)` order.
  - It stops hard at `max_expansions: 65536`, the owner's E1 ruling (`content/config/navigation.yaml`; `NavSearch.cs:333-335`; N-D13).
  - Kera's three workshop routes plan at 22,685, 40,751 and 40,639 expansions, inside the two-thirds bound of 43,690.
- **Lever 3 (`5906d8d`, 16 KB blocks) is a correct copy-on-write storage change.**
  - A tile's 160,000 nodes sit in 10 blocks, the last holding 12,544 B; the indexing handles the remainder (`NavTile.cs:86, 111-117`).
  - An edit copies every block in its written range before writing (`:120-126, 206-209, 249-291`), and a layer is never mutated after construction. An old grid therefore cannot be aliased.
  - Write order is unchanged. The block-wise hash equals the contiguous hash.
  - There is no static pool and no save-format dependency.
  - Results are pinned by independent references that predate lever 3:
    - N-D9(c): every node against a raster from `NavGeometry.FitAt`;
    - N-D9(e): routes against a separately written A*;
    - N-D11: 200 random edits, incremental against full (`tests/Domain.Tests/Spatial/NavigationTests.cs:283-290, 300-318, 331-356, 362-420, 446-495`).
  - All of these pass.
- **The preview cannot change the command's result.**
  - `Simulation.PreviewPlacement` runs `BuildingRules` on its own generation-stamped scratch with no counters (`Simulation.cs:303`; `NavScratch.cs:42-57`).
  - It publishes and dispatches nothing.
  - The real place command always re-runs the full navigability check (`Building.cs:201`).
  - Proven by `Preview_EqualsTheCommand_OverFortyPoses` and `PreviewsInterleaved_ChangeNothing`.
- **Building rules.**
  - 3 m lattice, quarter turns 0-3.
  - One storey is structural: slots are keyed by x and z only.
  - Build-area containment is closed on the slot footprint.
  - Only the player's melee blow damages a piece (`config.building damage.melee: 10`).
  - Repair costs timber, rounded up.
  - Dismantling refunds 50 %, rounded down, so 1-timber pieces refund 0. This is tested and documented.
  - A refund that does not fit goes to the ground.
  - A chest with items refuses to be taken down. A destroyed chest spills its stacks with their item IDs kept.
  - Ownership is checked for dismantle, repair, doors, assign and release.
  - Sealed rooms and un-navigable configurations are refused with worded reasons: N-D19, N-D20, N-A13, `ADoorlessOneSquareHut_IsRefused`, `CrossingWorkshop_7`.
- **BLD006 S1 is implemented exactly as ruled** (`src/Content/BuildingContent.cs:48-76`).
  - Any piece or build area requires `config.building`.
  - A present `config.building` is validated in full, even when nothing can be built.
  - A valid, inert `config.building` with no build content is **accepted**; the converse is not required.
  - Tests cover all three cases.
- **Identity.**
  - `EntityId.Derived` puts the structure ordinal in the ULID time field, plus a SHA-256 of tag, salt and ordinal, with no clock (`src/Domain/EntityId.cs:80`).
  - The structure sequence rises on every place and every take-down, and `WorldDelta.PlacePiece` / `RemovePiece` throw otherwise. So re-placing in a freed slot gives a new ID, and two pieces cannot share one.
  - A load checks ordinals and rejects duplicate IDs (`WorldDelta.cs:1039-1044`).
  - The salt is the character's (D-04, wall-clock) ID. Two *fresh* new games therefore derive different piece IDs, while replay and save-then-continue are identical. This matches D-04.

## 7. Determinism review

**No determinism fault found.**

- **Iteration order.**
  - `WorldDelta._pieces` and `_errands` are ordinal `SortedDictionary`s, handed to callers as `ToList()` copies.
  - `Npcs`, `Creatures` and `Companions` are `ImmutableSortedDictionary`s.
  - The structure rebuild sorts with `StructureOrder`, which always breaks ties: geometry, then ID, then part. `NavGrid.Build` is a stable `OrderBy`, and tiles are built in key order.
  - Faction reactors go through a sorted dictionary, and `ReportAct` walks acts in sequence order.
  - Plain hash collections are used only for lookups: `StructureIndex.Providers`, `BuildingRules.Takes`' `used` map, and `NavFollower`'s duplicate set.
- **No source of nondeterminism in the authority.**
  - No `System.Random`, `DateTime.Now`/`UtcNow`, `Environment.TickCount`, `Guid.NewGuid`, `Parallel`, `Task.Run` or `[ThreadStatic]` in M7-added code outside Presentation.
  - `Stopwatch` appears only in Presentation, for timing.
  - The only new thread is the save lane.
  - New static state is immutable: `static readonly` sorted dictionaries, `ImmutableArray`s, and comparers with no state.
- **Randomness and the queue.**
  - Combat rolls are keyed on saved state (§4).
  - The command queue and fixed 20 Hz tick are unchanged.
  - A background worker never orders the simulation (§5).
- **System boundaries.**
  - Each new slice (Navigation, Structures, Npcs + NpcErrands, Factions) is claimed exactly once (`Simulation.cs:144-148`). Startup checks that every slice has an owner.
  - Every new `RuntimeState` write calls `Require`.
  - Effects on other systems go only through commands: `ExchangeItems`, `GrantItem`, `SpillContainer`, `DiscardContainer`, `BeginWork`, `EndWork`, `RebuildNavigation`, `DamagePiece`, `OperatePieceDoor`, `OpenDoor`, `PlaceNpc`, `RecordAct` and `ReportAct`.
  - Presentation only submits commands and reads views.
- **Replay evidence in the repository.**
  - N-A8 `APlacementSession_ReplaysToTheSameState`.
  - `CrossingWorkshop_0and11`.
  - `FactionActs_ReplayFromTheCommandLog_EndIdentical`.
  - G20 and G28.

  All pass in the cloud.
- **Runtime evidence** (Godot runs, not reproducible here):
  - The committed `docs/acceptance/m7/state_replay.json` hashes to `bc57ee2a…108a7b`, and `docs/acceptance/m7_build/state_replay.json` to `304095a5…8e7239`, both as `M7_STATUS` states.
  - The E9 (`e9_seam`) and E10 (`e10_final_seam`) build-shot `state_replay.json` files are byte-identical. This supports "lever 3 and the capture fix changed nothing in the state".
  - Their raw saved-state digests differ only through item instance IDs minted from the wall clock (`itm_01M3FE…` vs `itm_01M3FS…`): the crafted spear and the timber inside the piece chest.
  - The replay projection canonicalises those IDs to ordinals. That is D-04 by design, not a regression. The "byte-identical" claims apply to the replay projection, which is how `M7_STATUS` states them.

## 8. Test-integrity audit (every pre-M7 test change, `a696931` → `ac8d9d8`)

**No pre-M7 test was weakened.**

- **Scope.** 37 pre-existing test files were modified; none was deleted or renamed. 95 files are pure additions.
- **Fixtures.** The fixture binaries v1–v14, the historical content packs `0.1.0`–`0.1.6` and `GameSaves/m6_acceptance` are byte-unchanged.
- **Build configuration.** No test `.csproj`, runner config or `CollectionBehavior` changed.

| File | Classification |
|---|---|
| `Application.Tests/CompanionTests.cs` | **Strengthened.** One added line, `Assert.Equal(before.Route, after.Route)` at :364 (E4.3, `d7acca4`). C16 and every other case are byte-unchanged, and `content/config/companion.yaml` is unchanged. |
| `Architecture.Tests/ArchitectureTests.cs` | Additions permitted by §12.10 only; see below. Both allow-lists are still compared with exact `Assert.Equal`. |
| `Application.Tests/GameSaveTests.cs` | Exact-valued `AddedSince12` rows for schemas 15, 16 and 17 (:35-46). The count is still exact (:82). One new test. |
| `Application.Tests/NpcTests.cs` | The bought-out container now asserts an exact 3 iron billets instead of empty (:459-460, :472), per design §5.7.2. `Wares` is still asserted empty. |
| `Application.Tests/DeterminismAndViewTests.cs` | Both sessions load `builder_start`, and a scripted door segment runs first. The flag-parity check is re-scoped to authored doors, and placed-door toggles are checked separately and exactly (:151, :198). Control-digest equality is kept. Equal strength, ledgered at `M7_STATUS.md:1060, :1080`. |
| `Persistence.Tests/MigrationTests.cs` | Every step list extended, with all old prefixes kept. `Assert.Single` became an exact 4-element list (:295). Refusal text updated to schema 17. Equal strength. |
| `Persistence.Tests/HistoricalFixtureTests.cs` | Alias arms, an M7 block with a `< 15` branch asserting empties, and the ridge creature looked up by key instead of by index (the same element below schema 17). Equal strength. |
| `Persistence.Tests/PlayerRecordCompletenessTests.cs` | `Full()` gains a route, factions and vitals. `Coupled` gains exactly three entries and is still compared with `SequenceEqual`. `Words` gains faction vocabularies, which lets more perturbations reach the new fields. |
| `Persistence.Tests/CanonicalState.cs`, `RoundTripTests.cs`, `World.Tests/WorldDeltaTests.cs`, `TestWorlds.cs`, `ItemTests.cs`, `QuestTests.cs`, `StateDumpTests.cs` | Additive only. |
| `Content.Tests/ValidationTests.cs`, `Domain.Tests/DialogueRulesTests.cs`, `Application.Tests/Harness.cs` | New IDs in an exact list. A fake gains two interface members. An optional parameter that defaults to identical behaviour. |
| `Fixtures/v1`–`v14/expected.json`, `M2.Probe/M2Fixtures.cs`, `Fixtures/README.md` | Default-valued additions only, by README policies 2-4 (§4). |

- **The E9 episode is confirmed and correctly reverted.**
  - `6f7565c` (E9.2) made the `WorldDelta.NpcErrands` getter public and added `"get_" + nameof(WorldDelta.NpcErrands)` to `WorldDelta_ExposesNoPublicMutation`.
  - `b505deb` reverted both: `WorldDelta.cs:589` is `internal`, and the allow-list line is gone.
  - `b505deb` is an ancestor of `1278b9c`.
  - **The final allow-list change against `a696931` is exactly §12.10.** `WorldDelta` gains `Piece`, `PiecesIn`, `get_Pieces`, `get_StructureSequence`, `NpcErrand` and `NpcErrandsIn`; the simulation gains `PreviewPlacement`.
- **No bypasses in the production code.**
  - No new `#if`, `[Conditional]`, `Skip=` or CI detection. No new OS checks: the `OperatingSystem` checks in `ProfileLock`, `SaveLocation` and `SaveFailureTests` predate M7.
  - One `InternalsVisibleTo`, from Persistence to Persistence.Tests, as design §2 names.
  - The new environment-variable writer gates (`UNNAMED_WRITE_BUILD_START`, `UNNAMED_WRITE_REPUTATION`) refuse to run under `CI=true`, and the reputation test always compares the committed table.
- **Time budgets are not relaxed.** `AsyncSaveTests.cs` (5 s) and `CreatureTests.SixtyCreatures_TickWithinTheBudget` (`ms < 4`) are byte-unchanged.

Findings: m-4 (a status claim that is too broad), and observations O-9 and O-10.

## 9. Kera (E9) and T2 review

**Kera's errand.**

- **Assign and release** refuse in the documented order (`Assign_RefusesInOrder`, `Release_RefusesInOrder`).
- **Taking down the bench** sends her home (`TakingDownTheBench_SendsTheWorkerHome`).
- **A new wall** triggers a geometry replan (N-A3, N-A4, `CrossingWorkshop_9_ANewWallChangesHerWayHome`).
- **Load repairs** write audit lines only. Decoding already rejects a phase that does not match its piece (`WorldDelta.cs:1061-1074`).
- **An unreachable goal** leaves her waiting. She retries every 40 ticks and shows "blocked" from tick 200, deterministically (N-A5, `tests/World.Tests/ErrandWorldTests.cs:44-75`).
- **`CrossingWorkshop_5to6_KeraWalksInThroughAndAround`** (`tests/Application.Tests/BuildingAcceptanceTests.cs:530`) asserts:
  - the arrival bound, 1,296 ticks (:615);
  - exactly one crossing of z = 100 m, `c_01_00` → `c_01_01`, in the doorway (:620), never out again (:623);
  - at most 81 mm per tick (:624);
  - clearance every tick, with `overlap >= 1` mm failing (:597, :715).

  The exact L (80,366 mm), the 1,011 ticks and the 0.483 mm contact are logged, not asserted. The home pose is asserted in `ErrandTests.cs:23, :127`.

**The under-1 mm allowance** is confined to test assertions and is not used to hide interpenetration.

- It appears only in `BuildingAcceptanceTests.cs:597, :715` (`>= 1` fails) and in the chase test at :1178 (`gap > -1`).
- `Kinematics` and the collision blockers are unchanged since `a696931`. No new epsilon was added anywhere in `src`.
- Rounding each axis of a position to the nearest millimetre can place a body at most about 0.71 mm inside contact, so the bound covers exactly that mechanism and nothing larger.

**T2** (`TheCapWorkshop_SixtyCreaturesAndTwoMovers_TickWithinTheBudget`, `tests/Application.Tests/M7BudgetTests.cs:341`) matches the owner's ruling and exercises everything the ruling requires:

- **Pieces.** 256 pieces are placed through real commands, with 94 navigation rebuilds and every room reachable (`FullAreaLayout.cs:87-98`). 256 are re-asserted after the wall is rebuilt (`M7BudgetTests.cs:485`).
- **Kera, save and load.** Kera is assigned and saved mid-walk, and the save is loaded (:402-414).
- **The ruled setup.** The character resumes at **(84.0, 110.0)** facing 90, with **60 creatures alive** (:425-431). The crowd's x origin moves from 90 to 120 m, as ruled; no creature is disabled.
- **Tavar and the character.** Tavar plans a real route, `RoutePlanned`, not a catch-up (:437-440). The character does not die (:454).
- **The edit.** The west wall at **(87 000, 106 500)** comes down and goes back up (:473-484).
- **Game code is unchanged.** The E10 T2 commits touched only tests. Nothing disables creatures, stops attacks, changes follower rules or bypasses building or pathing.

Findings: m-3, two assertions that can pass without testing anything.

## 10. Performance-code review

Target-hardware numbers are **not reproduced** here (§14). What the code and repository evidence show:

- **The capture allocation fix (`dc3e32e`).**
  - It changes only where each record's baseline hash comes from: once per cell per snapshot, instead of once per record.
  - The captured fields are identical. The digest caches are locals of one snapshot (`WorldDelta.cs:643-646`), so nothing can go stale between saves, and there is no static cache.
  - `CellBaseline.Digest` is a pure function of an immutable record.
  - No mutable buffer is reused across captures. Snapshot records are `ImmutableArray` / `ImmutableSortedDictionary` with init-only properties (`WorldDelta.cs:20-265`, `PlayerState.cs:260-330`), so the lane cannot race the next capture.
  - The saved bytes are unchanged: every fixture and replay test passes, and the E9 and E10 replay states are byte-identical.
  - The 406,680 B → 43,256 B figure is logged only (`M7BudgetTests.cs:505`).
- **The three save measures (A/B/C) are kept apart** in T2: capture alone (`M7BudgetTests.cs:497`), encode (:499-502), and end to end (:510-517). All three are logged.
- **`FrameStats.save_ms` in play** is the capture plus `Enqueue` for a quicksave. On autosave frames it is the whole `Frame` (O-5), so it is an upper bound on A, never an understatement.
- **The placement cold paths.** No cache is invalidated by an edit, a cell change or a load.
  - JIT happens once per process.
  - The preview scratch (`Simulation.cs:303`) and the planning scratch (`NavScratch.cs:44-50, 86-92`) belong to the world, so their allocation recurs after each new game or load. It does not recur per placement.
  - The ghost preview runs the only bench-specific navigability branch (`PointClearAfter`, `NavEditCheck.cs:488-493`) before the commit. So the bench's 9-11 ms first *commit* is plausibly JIT of the commit-side station path.
  - That attribution is consistent with the COLD_FIRST_BENCH_COMMIT classification, but no committed test isolates it. See M-1.
- **Recurring placements stay inside the budget.** T2 logs 249 ordinary commits at a maximum of 1.6-8.8 ms on ASTRAL, and nothing in the code makes an ordinary commit's cost grow with repetition.
- **The planning-tick overrun** (Kera's walk-home plan, about 7 ms against the 4 ms world-system budget) is the accepted residue of §14.3. The per-tick plan budget remains the lever if RAZER shows a hitch.
- **Hard CI bounds** (Debug, `ubuntu-latest`): T2's tick mean `< 6` ms (:455), the cold first use `< 33` ms (:355), and `SixtyCreatures` `< 4` ms. All passed in this cloud run too (Debug, 4 vCPU).

## 11. Phase-B integration risks

The handoff's facts check out.

- **The conflict list is accurate.**
  - `git merge-tree --write-tree ac8d9d8 origin/claude/phase-b-visual-overhaul` conflicts only in `src/Presentation/Main.cs` and `src/Presentation/Ui/Hud.cs`.
  - `Greybox/Palette.cs`, `Playthrough.cs` and `content/regions/ashen_hollow.yaml` auto-merge.
  - This matches `docs/M7_VISUAL_INTEGRATION_HANDOFF.md` §4.
- **Gameplay-facing Phase-B changes.**
  - 107 authored trees (`tree_33`+, 0.4 m circle blockers, 7 m tall). These are collision, navigation and sight inputs.
  - The playthrough's container take order becomes last-first.
- **Tree placement, measured here:**
  - no tree lies inside `build_area.hollow_crossing` [87, 114]²;
  - the nearest is `tree_83` at (116.1, 114.4), **2.1 m from the area's north-east corner**, and `tree_88` is 5.9 m north of the area;
  - the timber stack's nearest tree is 28.0 m away;
  - T2's resume pose's nearest tree is 29.6 m away;
  - Kera's home's nearest tree is 48.1 m away, and the nearest to the straight home-to-area line is 21.6 m away.
- **The handoff's §6 revalidation list is adequate.** It covers:
  - N-A10;
  - `CrossingWorkshop_*`;
  - N-A2 to N-A6 and N-A8;
  - the errand tests;
  - T2 with its recomputed crowd homes;
  - `TwoHundredPieces_RoundTripAndStayNavigable`;
  - `--build-shots` and `--playthrough` with re-baselining;
  - the input and layout checks;
  - the perf run.

  Given `tree_83`'s nearness to the area's corner, the full-area tests should be *read*, not only run green. Their routes around the north-east corner may change.
- **One addition to that list: an M7-alone save loaded into the combined build.** Take a save made on M7 alone with an active errand and a companion route (for example the `m7_crossing_start` S0 save, or a quicksave mid-walk) and load it into the combined build. The grid is rebuilt from the new content at load and the content hash changes, but no committed test loads an M7-era save, with a route or errand in flight, under the new blockers. This audit did not verify how a saved in-flight route reacts to a blocker it now crosses.
- **Other Phase-B changes.** The branch adds GDExtension and GDScript addons (Terrain3D, Sky3D, SunshineClouds2). They are presentation-only, but they are a build and packaging risk for the combined Windows export, outside M7's scope.

M7 should become the stable gameplay baseline first, as the brief intends. Nothing in M7 makes the later integration harder than the handoff describes.

## 12. Findings by severity

### Blocker

None.

### Major

None.

### Minor

**M-1. The owner's COLD_FIRST_BENCH_COMMIT classification is not recorded, and the documented exemption is broader than it.** Documentation.

- **The recorded ruling exempts only the first ghost ask.** The E10 second-STOP ruling as recorded (`docs/M7_STATUS.md:191`) sets apart only "the first ghost ask that runs the navigability check", and holds every committed placement to "≤ 10 ms worst".
- **The closeout exempts more.** `docs/M7_STATUS.md:94-96` and `docs/RISK_REGISTER.md:95` exempt "each piece kind's first commit". The first bench measured 8.9-11.1 ms, over 10 ms once in 8 runs.
- **The classification is missing.** The owner's later classification, COLD_FIRST_BENCH_COMMIT (bench only, one-time, under the 33 ms ceiling, ordinary commits inside 10 ms), appears nowhere in the repository.
- **One figure has no committed source.** "The second and third bench commits 1.06-1.22 ms" comes from no committed test; T2 places one bench.
- **Code consistency.** The code is consistent with a one-time cost (§10), but the per-world scratch part recurs after each load.
- **Fix:** record the classification by name in `M7_STATUS` and `RISK_REGISTER`, narrow "each piece kind's first commit" to the bench, and note that the scratch part is once per world, not once per process. The measured first-pad commit (7.5-9.0 ms) is already inside 10 ms, so nothing else needs the exemption.

**M-2. The faction-hostility guard (G6) scans only one level deep, over a fixed list of files.** Test strength.

- **What it scans.** `TacticalCode_NeverReadsFactionState` (`tests/Architecture.Tests/M7GuardTests.cs:272-336`) reads the IL of the types declared in 10 listed files: Combat, Creatures, Companions, Magic, Errands, Navigation, Building, BuildingRules, Quests and Domain/Quests. It bans `Domain.Factions` types and four named members.
- **What it misses:**
  - calls into helpers in `Systems.cs` (`SystemContext`) or `Simulation.cs`;
  - `src/Domain/Creatures/Perception.cs` and `src/Domain/Combat/*`, which are not listed.
- **Today's code is clean.** Standing is read only in `Social.cs:440/574`, `QuestDebugger.cs:428` and the item `StandingRequirement`, and `Creatures.cs:895` `Foe()` picks by distance.
- **The risk** is a future helper that bypasses the guard. Suggest scanning those files too, or following calls transitively.

**M-3. Two T2 assertions can pass without testing anything.** Test strength.

- **The replanning check.** At the edit, the list of movers expected to replan is computed from state and never asserted non-empty (`M7BudgetTests.cs:466-469, 482`). If it and the replans were both empty, the equality would still pass.
- **Kera in the timed window.** Nothing asserts that Kera plans or moves during the timed window; only her `ToWork` phase is checked (:439).
- **The logs show both happen** (M7_STATUS STOP record: "Kera replans for geometry on the edit, exactly the movers predicted").
- **Fix:** `Assert.Contains(Kera, expected)` and a check that her position changed.

**M-4. `M7_STATUS`'s "no other edit outside §12.10" (:509-510) is too broad.** Documentation.

- Several pre-M7 test edits go beyond §12.10's literal list:
  - the view test's `builder_start` / `HangAndWorkADoor` re-scoping;
  - the schema 16 and 17 rows;
  - the `Words` additions;
  - `Harness.WalkTo`.
- All of them keep their strength, and each is ledgered or ruled (`M7_STATUS.md:727, :772, :1060, :1080`). The sentence should say "no *weakening*".

**M-5. The planner's heap key would overflow on a finer `node_m` that the lint allows.** Latent.

- `NavSearch.cs:419-420` packs `(f << 20) | h` and assumes `h < 2^20` "in any window".
- NAV001 (`NavConfig.cs:83`) accepts any even `node_m` of at least 50 mm that divides 100 m.
- At a 128 m window, any `node_m` below about 0.175 m makes h exceed 2^20 (about 1.13 M at 0.16 m). The order then stays deterministic but loses `(f, h)` order and optimality.
- The shipped 0.25 m is safe: the largest h is 722,554.
- **Fix:** tighten NAV001, or check the maximum h against the window at load.

**M-6. A never-disposed session keeps its save-lane thread alive.** Test processes only.

- `SaveLane.cs:15-16` says a dropped lane leaves no thread behind.
- But the worker's loop local `next` (`:89`) keeps the last save closure reachable while the thread waits. That closure captures the session (`GameSession.cs:291`), which owns the lane. So the lane is never finalized, and the thread waits for good.
- The game is unaffected: `Main` holds one session for the process's life.
- `ALaneDroppedWithoutClosing_…` uses closures that do not reference the lane, so it cannot catch this.
- **Fix:** clear `next` before waiting, or have tests dispose their sessions.

**M-7. Saved ticks in the future are not rejected on load.** Hand-edited saves only.

- The schema-17 check rejects `AttackTick < 0` but not an attack tick later than the save's `world_tick` (`WorldDelta.cs:1136`). The codec round-trips `long.MaxValue` (`Schema17Tests.cs:26`), and such a creature would stay in windup forever (`Combat.cs:183-190`).
- The schema-16 vitals check (`PlayerState.cs:471-477`) has the same gap.
- The outcome is deterministic, but no explicit load rule covers it.

**M-8. Nothing tests the "newer schema" refusal.**

- The refusal exists (`SaveLoader.cs:103-106`; `SaveStore.cs:159`) and forward incompatibility is documented. But no test exercises it; `ExitCriteriaTests.cs:160` covers only a missing migration chain.
- This predates M7, but M7 is the first milestone to ship three schema steps at once.

**M-9. Stale counts in the E2 records and a test comment.**

- `M7_STATUS.md:418` and `:936` (E2 records) say the M6 save's difference count is "+5" and its path is "12 → 13 → 14 → 15". The E2.4 ruling row (:188) says `Steps.Count == 3`.
- The live assertions are `2 + 4N + 1 + 3 + 1 + companions + 1 + 2N` (`GameSaveTests.cs:82`) and 5 steps (:123).
- The test's own comment at `GameSaveTests.cs:122` still says "three migrations run".
- The historical records may stay as history, but a one-line pointer to the schema-16 and 17 amendments would stop them misleading.

### Observations

**O-1. Replay identity is scoped to the replay projection.** Item instance IDs remain wall-clock ULIDs (D-04), so raw save digests differ between runs, while `state_replay.json` is byte-identical (§7).

- G8 (`M7SystemsMintNoWallClockIds`, `M7GuardTests.cs:83`) scans only the five M7 system files. Timber items come from the pre-existing Items system.
- Piece IDs are salted with the character's wall-clock ID, so two fresh new games derive different piece IDs.

**O-2. An empty `damage: {}` in `config.building` passes BLD006 and makes every piece indestructible.** A swing then sends damage 0 and produces neither a hit nor a miss (`Building.cs:155-161`; `Combat.cs:535`).

**O-3. Taking the bench down while Kera is still walking to it is untested.** The code path is shared with the arrived case (`Building.cs:420-421`; `ErrandTests.cs:239-281` covers only that).

**O-4. Lever 3's "five states compared before and after" (`M7_STATUS.md:486`) is not a committed test.** The independent-reference tests in §6 pin the results. No test re-checks a pre-edit grid after an edit; the code makes that safe.

**O-5. `save_ms` on autosave frames is the whole `Frame`.** That includes `NextAutosaveSlot()`'s synchronous reads of up to five manifests (`Main.cs:605-608, 633`; `SaveStore.cs:179-199`). It is an upper bound on the capture.

**O-6. Save-lane test gaps.**

- G29 (`Schema15Tests.cs:419-458`) checks types only. It skips non-`UNNAMED`/non-`Immutable` types and never inspects private fields; none exist today.
- `QuittingWithASaveInFlight` holds the save after the commit point. It also releases the hold with a `System.Threading.Timer`, which runs on the shared pool.
- Nothing tests the dispose timeout path or enqueueing after dispose (which now throws `ObjectDisposedException`, `SaveLane.cs:69`).

**O-7. Two pre-existing session-teardown issues** (unchanged since `a696931`, not M7's):

- **The profile lock.** If a save outlasts `Dispose`'s 10 s wait, the profile lock is released while the lane still writes (`GameSession.cs:134-136`). Only the smoke test reaches this.
- **A stale autosave failure after a load.** A failed autosave that completes after a `Load` sets `_lastAutosave` from the old world's playtime (`GameSession.cs:316-318`; identical at `a696931:305`). That can delay the loaded world's autosaves by up to about 50 minutes of play.

**O-8. `SixtyCreatures_TickWithinTheBudget` failed once in CI** at 4.94 ms against its 4 ms bound, then passed on re-run (`M7_STATUS.md:816`). The bound is unchanged, but CI headroom is thin on a Debug `ubuntu-latest` runner.

**O-9. `NpcTests.ATraderBoughtOut_StaysEmpty…`** keeps its name while now asserting 3 withheld billets in the container (justified by design §5.7.2). A rename would read better.

**O-10. Criterion 19's row reads weaker than design §11.2's text** (one schema step, 15 fixtures, "only §7.12's lines"). It is marked "met, as amended by the rulings", and the two E8.5 rulings do authorize the amendment.

**O-11. New build warning CS0108.** `BuildMode.Rotation` (an `int`) hides `Node3D.Rotation` (`src/Presentation/Player/BuildMode.cs:46`). The types differ, so misuse fails to compile; it is hygiene only.

**O-12. Minor persistence-policy notes, pre-existing.**

- The MessagePack decoder silently skips unknown keys (`SectionCodec.cs:485-486`).
- A malformed `struck` ID fails the whole entities section's decode, which is quarantined with a warning (`SaveLoader.cs:571-583`); `PERSISTENCE.md` documents this.
- "Rejected with its record" drops the whole creature record, so a dead creature whose record is dropped would reload as its baseline spawn.

**O-13. Criterion 14's exact figures are logged rather than asserted.** Its asserted bounds are listed in §9.

## 13. Evidence index

- **Build:** `dotnet build src/UNNAMED.sln` gives 0 errors and 8 warnings, 2 unique:
  - `MagicContent.cs:165` CS8602, pre-existing;
  - `BuildMode.cs:46` CS0108, M7 (O-11).
- **Tests:** `dotnet test src/UNNAMED.sln` at `17e638c`, whose code is `1278b9c`'s. **1,092 passed, 1 skipped, 0 failed**:

  | Project | Result |
  |---|---|
  | Domain | 186 |
  | Application | 322 |
  | Persistence | 213 + 1 skipped |
  | Content | 187 |
  | World | 72 |
  | Presentation | 57 |
  | EntityRegistry | 23 |
  | Architecture | 32 |

  The skip is `SaveFailureTests.ASaveWhosePredecessorIsHeldOpen_…`, a pre-M7 `[WindowsFact]` (`7151816`) that M7 did not touch. With it, the total matches the reported 1,093 on Windows.
- **Content lint** (Release build of Content): **116 definitions, 0 errors.**
- **CI:**
  - run 36270151122 on `1278b9c`: success;
  - run 36273004200 on `ac8d9d8`: success.

  Both are `pull_request` runs on PR jlucasmcrell/UNNAMED#8. The workflow (`.github/workflows/dotnet.yml`, unchanged by M7) runs restore, build and test in Debug on `ubuntu-latest`. It does not run the content lint or any Godot mode.
- **Commits cited:**

  | Commit | What |
  |---|---|
  | `682c449` | save lane |
  | `1dd66e7` | schema 16 |
  | `a8f2ce1` | schema 17 |
  | `5906d8d` | lever 3 |
  | `dc3e32e` | capture |
  | `752791b`, `07df8bf` | T2 |
  | `6f7565c`, `b505deb` | E9 accessor and revert |
  | `d7acca4` | CompanionTests route line |

- **Evidence files hashed:**
  - `docs/acceptance/m7/state_replay.json` = `bc57ee2a3912865ef8d396b0b8c8df17f89630080caf384f5a0bcb438b108a7b`;
  - `docs/acceptance/m7_build/state_replay.json` = `304095a5dd36f50037c80a2d34dfa7bb985d842664441314a008728eefc7e239`, equal to both `docs/reviews/m7_audit_inputs/M7_EVIDENCE_TEXT/{e9_seam,e10_final_seam}/build_shots/state_replay.json`;
  - `e10_final_perf/commit.txt` = `1278b9c…`;
  - `e10_perf_astral/commit.txt` = `f1e7594…`;
  - `kera_home_tick.txt` = 3929 in both E9 and E10.

## 14. What could not be independently verified

- **Every Godot runtime mode.** Godot is not installed in the cloud container. That covers:
  - `--smoke` and `--quit-after 300`;
  - `--build-shots` and its verify run (19 beats, v1 1,535 fields and v2 1,523 fields with 0 differences);
  - `--playthrough` and its verify run (1,216 fields, 0 differences);
  - `--ui-shots`, `--delta-shots`, `--input-check` and `--layout-check`;
  - the second-run byte-identity.

  Only the committed text outputs of those runs were checked (§7, §13).
- **All ASTRAL and RAZER performance figures**: the Release budget lines, the 275.5 fps / 163.1 fps 1 % low perf trial, the placement and save distributions, and the 406,680 B → 43,256 B allocation. The committed `frames.csv` and `summary.json` exist, but the hardware runs were not reproduced. The M7 RAZER capture of the `building` segment is still owed (not a gate).
- **The seam recordings** (the videos under `G:\UNNAMED_HISTORY\M7_EVIDENCE\`) are not in the repository.
- **The Windows-only test** `ASaveWhosePredecessorIsHeldOpen_…`, skipped on Linux.
- **The "second and third bench commits 1.06-1.22 ms" figure** (no committed source; M-1).
- **Phase-B's combined build**: no merge was performed or tested, by instruction.

## 15. Conclusion

**READY WITH ACCEPTED MINOR OBSERVATIONS.**

M7 meets its design and the owner's rulings in code:

- report-only factions;
- a single-storey snap building system with damage, repair, refund and ownership;
- a derived, integer, never-saved navigation grid with optimal A*;
- Kera's persisted errand across the cell seam;
- companions planning through doors;
- a gap-free 14 → 15 → 16 → 17 migration chain that keeps every older save and the M6 acceptance save loadable;
- a single-writer save lane that preserves ordering, atomicity, the profile lock, drain and failure propagation.

No pre-M7 test was weakened. The only implementation after the tested commit is documentation. The build, tests and lint reproduce in the cloud, and CI is green on both reported commits.

Before or at the merge, the owner should:

1. Record the COLD_FIRST_BENCH_COMMIT classification in `M7_STATUS` and `RISK_REGISTER`, narrowing the "each piece kind's first commit" wording (M-1). This is a documentation change that needs no runtime re-verification.
2. Optionally, schedule the test-strength and latent items (M-2, M-3, M-5 to M-8) as follow-ups. None of them changes M7's behaviour or blocks the merge.
3. Keep the handoff's combined-build revalidation, with the M7-save-into-combined-build load added (§11), as the gate for integrating Phase B.
