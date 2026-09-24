# M2 Implementation Status

**Date:** 2026-09-23 (completed; audited the same day before M2b)
**Phase:** 1
**Milestone:** M2 — Entity Registry, Identity, Persistence Baseline (`ROADMAP.md`)
**Status:** **COMPLETE** — all four exit criteria and the determinism risk spike pass as automated tests. The acceptance audit before M2b found and corrected regressions and gaps (below). M2b then refined the persistence model (`M2B_STATUS.md`).

---

## Exit criteria

| ID | Criterion (`ROADMAP.md` M2) | Evidence | Status |
|---|---|---|---|
| **ME-1** | Save a 10-cell world with 2 changed cells; the save contains only the deltas | `ExitCriteriaTests.ME1_ME2_TenCellWorld_SavesOnlyTheTwoChangedCells_AndLoadsEqual`: all ten cells are generated, and `cells.msgpack` decodes to exactly `r_0_0:c_00_00` and `r_0_0:c_00_03`; `entities.msgpack` is empty | PASS |
| **ME-2** | Load it and verify world equality | Same test: the effective-state digest of all ten cells and the player digest are equal after load, and the load is complete (nothing quarantined or rejected) | PASS |
| **ME-3** | `(seed, content_version)` determinism across two fresh processes | `ME3_Determinism_AcrossTwoFreshProcesses`: two separate `tests/M2.Probe` processes print the same region digest, equal to the in-process value. Since M2b the tuple under test is `(seed, generator contract)` (`PERSISTENCE.md` §1.3) | PASS |
| **ME-4** | Atomic write survives a simulated kill during write | `ME4_AtomicWrite_SurvivesAKillAtEveryStep` (all six §7.1 step boundaries) and `ME4_FirstEverSave_KilledMidWrite_LeavesNoCorruptSlot`. The probe process kills itself at the step (`Process.Kill`: TerminateProcess on Windows, SIGKILL on Linux), so no cleanup code runs; since the audit it first prints the step, and the test asserts the kill hit exactly that step. After the boot sweep, the slot loads as exactly the old or the new save, complete, with no staging or trash left | PASS |

## Risk spike: determinism (`RK-01`)

| Check (`ROADMAP.md` M2 risk spike) | Evidence as the tests now stand | Result |
|---|---|---|
| Generate twice from the same seed and diff | `World.Tests` `RK01_RegionDigest_IsIdenticalAcrossRuns`: a 2x2 km region (400 cells), 100 runs; ME-3 across processes | Bit-stable |
| The baseline is pinned | `GeneratorTests.Worldgen2_IsPinned_ProbesFingerprintAndRegion` (M2b, pinned to an independent implementation). M2's worldgen-1 golden, `sha256:cd369d84…`, is still pinned by `LegacyWorldgen1_WasSeededByTheWholeContentHash`, because the schema 1 -> 2 migration regenerates worldgen 1 | Pinned |
| A version mismatch triggers migration, not silent regeneration | `ExitCriteriaTests.ASchemaWithNoRegisteredChain_IsRefused`; the M2b chain tests (`M2B_STATUS.md`) | PASS |
| A generation change is refused | `ExitCriteriaTests.RiskSpike_GenerationChange_IsRefused_NeverAppliedToAnotherBaseline`: every changed cell's `baseline_hash` differs, and there is no registered transition | PASS |
| A content-only change does not move the world | `RiskSpike_ContentOnlyChange_LoadsWithoutReshuffling`; `RK01_BaselineNeutralContentChange_ChangesContentHash_NotTheBaseline` | PASS (M2b) |
| Nothing in generation depends on the platform | Integer-only generation; `CultureTests` (tr-TR, de-DE, ar-SA, th-TH), for generation and for save bytes | PASS |

**Measured answer for `RK-01`:** generation is bit-stable across runs, processes and cultures on Windows x64 / .NET 8. The Linux CI run is still to be observed.

**What M2 got wrong, and M2b fixed.** M2's generator keyed every cell stream on `(world_seed, worldgen_version, content_hash, cell, purpose)`, as `WORLD_ARCHITECTURE.md` §3.4 then specified. So its risk-spike test proved that a wolf-HP edit *changed* the world, and counted that as success. It was the coupling M2b removed. See `PERSISTENCE.md` `RK-P14` and `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md`.

## Work items

| `ROADMAP.md` M2 work | Implementation | Tests |
|---|---|---|
| `EntityRegistry` (D-10): creation, ULID assignment, lookup, no gameplay rules | `src/EntityRegistry` | `EntityRegistry.Tests`; `Architecture.Tests` (the registry depends on Domain only) |
| Definition-ID vs instance-ID namespaces (D-04) | `src/Domain/EntityId.cs`, `EntityKind.cs`: `<prefix>_<ULID>` with a per-kind prefix and canonical Crockford encoding; `src/Domain/DefinitionId.cs` is the one ID grammar (content validation calls it since the audit) | `Create_EncodesCanonicalUlid` uses the ULID specification's reference vector |
| Deterministic baseline generation behind a frozen interface | `src/World`: `ICellBaselineGenerator`, `WorldDelta` | `World.Tests` |
| Sparse-delta save, atomic write, backups (D-05) | `src/Persistence`: `SaveStore`, `SectionCodec`, `SaveModel` | `Persistence.Tests` |
| Corruption-recovery path | `SaveStore.Load` (§7.2, §7.4), `SaveStore.RecoverInterruptedCommits` | `IntegrityTests`, `RecoveryTests` |
| Autosave and manual save slots | `SaveSlots`, `SaveStore.NextAutosaveSlot`, `AutosaveCadence` | `SlotTests` |

## Test totals

At M2 completion: 165 tests (Domain 4, EntityRegistry 23, Content 29, World 44, Persistence 65). After the audit (`4e9523c`): 186. Current totals are in `M2B_STATUS.md`.

## Acceptance audit (2026-09-23, before M2b)

The audit reproduced the 165 passing tests and all four exit criteria. The WIP commit `5e68382`, which preceded the M2 commits, had regressed M1b; every finding below is corrected in `4e9523c`.

| Finding | Correction |
|---|---|
| **M1b content validation regressed.** Four source files were deleted and 19 of the 29 M1b tests replaced (the count was unchanged). The lint reported 10 DIR003 errors on the real `content/` yet printed "Validation passed" and exited 0 | Restored `src/Content` and its tests from `2782414`; re-added M2's content hash; M1b's 29 tests plus new ones pass; real content lints clean |
| **Two DefinitionId contracts.** The content validator was case-insensitive; M1b had two looser regex copies | Content validation calls `Domain.DefinitionId.IsValid` |
| **Malformed YAML was silently dropped** (a DEBUG print) | Reported as `LOAD003` |
| **`_aliases.yaml` was parsed by hand** (comments broke it) | Parsed as YAML; discards (`~`), replacement targets and retired-but-defined IDs validated |
| **The authoritative-state writer was public** (`ISystemWriterInternals`), with no architecture test, contrary to `ARCHITECTURE.md` §5 | `IWorldStateWriter` and `ISystem.Configure` are internal to Domain; `tests/Architecture.Tests` added |
| **`WorldDelta` mutators were public**: a backdoor to authoritative state | Internal to World; only test assemblies see them |
| **Leftovers** (`M1b_changes.patch`, `cli-test-lint.ps1`, two tracked `.rar` archives, debug projects) | Removed; the archives untracked and ignored |
| **AGENTS.md errors (mine)**: a lint command form the CLI silently ignores; the writer described as internal when it was not | Corrected |
| **Kill tests did not prove where the kill landed** | The probe prints the step before dying; tests assert it |
| **No culture test** | Added for generation, identity text and save bytes |

Found and not corrected (predate M2, recorded): the content lint has no per-kind schema validation, and does not check references inside nested definition fields (both M1b and the rewrite missed `heal_dangling.yaml`'s dangling spell reference).

## Decisions recorded at M2 completion, now settled in the docs

1. **Backup generations.** The ROADMAP's "one rolling backup slot" is superseded by `PERSISTENCE.md` §7.3's two generations (ROADMAP, DECISIONS D-05 and DATA_MODEL §6 updated).
2. **"Retired on a verified load".** Only a save proven by a complete, clean load of its bytes enters the chain (`PERSISTENCE.md` §7.3).
3. **Staging location.** Staging and trash are siblings of the slot, `.staging-<slot>-<ulid>` and `.trash-<slot>-<ulid>` (`PERSISTENCE.md` §7.1).
4. **Player instance prefix `chr_`.** Added to `DATA_MODEL.md` §2.2.
5. **Quick slots.** One `quick` slot (`PERSISTENCE.md` §3.2, and §8.2 now matches).
6. **Unreadable `sections.sha256`.** Re-derived and reported, and the load does not count as complete; the "recorded sizes" reference is removed (`PERSISTENCE.md` §3.3).
7. **Directory fsync.** File-level write-through + flush; verification and the boot sweep cover a lost rename (`PERSISTENCE.md` §7.1).
8. **Windows sharing violations.** Renames and deletes retry with bounded backoff; access denial surfaces with the path (`PERSISTENCE.md` §7.1).

The **open question** recorded at M2 completion - `content_hash` seeding every cell stream while §6.1 expected content changes to need only the alias pass - is **resolved by M2b** (`PERSISTENCE.md` §1.3, §6.4).

## Deferred, with the milestone that owns it

- **Missing sections.** `companions`, `buildings`, `journal`, `command_log` (with `command_log_sha256`) and `orphans` arrive with the systems that own them.
- **Cell `overrides`** (the authored re-roll path): no authored overrides exist yet.
- **Game-loop wiring:** save/load/autosave through the command/event bus, the `WorldLoaded` event, and the autosave main-thread budget (≤ 2 ms P99) - the first gameplay milestone that saves.
- **Domain invariant validation on load** (§7.2): with the first system that references entity instances. Until then rebase retires an entity's identity without a reference check.
- **Terrain** is a digest-only signature; the heightfield comes with world rendering.
- **`harvest_seq`** restarts when a regrown node's record is rebased away. This is deterministic, but it matters once respawn uses the jitter.
- **`.failed-*` directories** are kept for diagnosis and never cleaned.
- **Cloud-sync warning UI.** Detection is done (`SaveStore.CloudSyncRoot`); the warning belongs to Presentation.

## Verify

```bash
dotnet test src/UNNAMED.sln
dotnet test tests/Persistence.Tests --filter "FullyQualifiedName~ExitCriteriaTests"
```

Commits: `51057ff` identity, `e683b29` world generation and delta, `a533e0f` persistence, `fee81a0` autosave rotation and cloud-sync detection, `7ff4c57` status; audit corrections `4e9523c`.
