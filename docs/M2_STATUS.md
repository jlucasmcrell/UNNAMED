# M2 Implementation Status

**Date:** 2026-09-23
**Phase:** 1
**Milestone:** M2 — Entity Registry, Identity, Persistence Baseline (`ROADMAP.md`)
**Status:** **COMPLETE** — all four exit criteria and the determinism risk spike pass as automated tests. Deferred items are listed at the end, each with the milestone that owns it.

---

## Exit criteria

| ID | Criterion (`ROADMAP.md` M2) | Evidence | Status |
|---|---|---|---|
| **ME-1** | Save a 10-cell world with 2 changed cells; the save contains only the deltas | `ExitCriteriaTests.ME1_ME2_TenCellWorld_SavesOnlyTheTwoChangedCells_AndLoadsEqual`: all ten cells are generated, and `cells.msgpack` decodes to exactly `r_0_0:c_00_00` and `r_0_0:c_00_03`; `entities.msgpack` is empty | PASS |
| **ME-2** | Load it and verify world equality | Same test: the effective-state digest of all ten cells and the player digest are equal after load, and the load is complete (nothing quarantined or rejected) | PASS |
| **ME-3** | `(seed, content_version)` determinism across two fresh processes | `ME3_Determinism_AcrossTwoFreshProcesses`: two separate `tests/M2.Probe` processes print the same region digest, equal to the in-process value | PASS |
| **ME-4** | Atomic write survives a simulated kill during write | `ME4_AtomicWrite_SurvivesAKillAtEveryStep` (all six §7.1 step boundaries) and `ME4_FirstEverSave_KilledMidWrite_LeavesNoCorruptSlot`. The probe process kills itself at the step (`Process.Kill`: TerminateProcess on Windows, SIGKILL on Linux), so no cleanup code runs. After the boot sweep, the slot loads as exactly the old or the new save, complete, with no staging or trash left | PASS |

## Risk spike: determinism (`RK-01`)

| Check (`ROADMAP.md` M2 risk spike) | Evidence | Result |
|---|---|---|
| Generate twice from the same seed and diff | `World.Tests` `RK01_RegionDigest_IsIdenticalAcrossRuns`: a 2x2 km region (400 cells), 100 runs; ME-3 across processes | Bit-stable |
| The baseline is pinned | `GoldenRegionDigest_PinsWorldgenV1`: `sha256:cd369d84e2227558d74a01eca846d0ce2eba39b93a505a26e0815bfcff434e14` | Pinned |
| `(seed, v1)` then `(seed, v2)`: the mismatch triggers migration, not silent regeneration | `RiskSpike_ContentChange_RoutesToMigration_NotSilentRegeneration` (`content_hash` differs: `BaselineMismatchException`, migratable); `RK01_TrivialContentChange_IsDetected_NotSilent` (wolf hp 30 to 31 through the real content loader changes the hash) | PASS |
| A generation change is refused | `RiskSpike_GenerationChange_IsRefused_NeverAppliedToAnotherBaseline` (`worldgen_digest` differs: refused, not migratable) | PASS |
| Nothing in generation depends on the platform | Integer-only generation: xoshiro256** streams keyed per cell and purpose (`WORLD_ARCHITECTURE.md` §3.4), integer range sampling, no floating point. `StreamTests` checks the streams against an independent Python implementation | By construction |

**Measured answer for `RK-01`:** generation is bit-stable across runs and across processes on Windows x64 / .NET 8. The golden digest was computed on Windows; the CI runner is Linux, so the first CI run after these commits are pushed is the cross-OS measurement.

## Work items

| `ROADMAP.md` M2 work | Implementation | Tests |
|---|---|---|
| `EntityRegistry` (D-10): creation, ULID assignment, lookup, no gameplay rules | `src/EntityRegistry` | `EntityRegistry.Tests` |
| Definition-ID vs instance-ID namespaces (D-04) | `src/Domain/EntityId.cs`, `EntityKind.cs`: `<prefix>_<ULID>` with a per-kind prefix and canonical Crockford encoding | `Create_EncodesCanonicalUlid` uses the ULID specification's reference vector |
| Deterministic baseline generation behind a frozen interface | `src/World`: `ICellBaselineGenerator`, `CellBaselineGeneratorV1`, `WorldDelta` | `World.Tests` |
| Sparse-delta save, atomic write, backups (D-05) | `src/Persistence`: `SaveStore`, `SectionCodec`, `SaveModel` | `Persistence.Tests` |
| Corruption-recovery path | `SaveStore.Load` (§7.2, §7.4), `SaveStore.RecoverInterruptedCommits` | `IntegrityTests`, `RecoveryTests` |
| Autosave and manual save slots | `SaveSlots`, `SaveStore.NextAutosaveSlot`, `AutosaveCadence` | `SlotTests` |

The save follows `PERSISTENCE.md`, which owns persistence:
- **Layout:** a directory per slot (§3.2) holding `manifest.json`, `player.msgpack`, `cells.msgpack` and `entities.msgpack`.
- **Integrity root:** `sections.sha256`, in `sha256sum` format. It covers the manifest too, and the manifest carries no checksum of itself (§4.2).
- **Sparse deltas:** cell records are written only for diverged cells; entity records are keyed by slot and merged on load (§5.2, §5.3).
- **Rebase:** a record back at baseline is deleted, and a rebased entity's registry identity is retired (§5.6).
- **Write:** the atomic write sequence of §7.1.
- **Load:** quarantine versus hard error per §7.2, and the load order of §7.4.

## Test totals

| Project | Tests |
|---|---|
| Domain.Tests | 4 |
| EntityRegistry.Tests | 23 |
| Content.Tests | 29 |
| World.Tests | 44 |
| Persistence.Tests | 65 |
| **Total** | **165, all passing** (`dotnet test src/UNNAMED.sln`, Windows, 2026-09-23) |

Before M2, `Persistence.Tests` had 14 tests and 9 failed. The old loader silently returned the backup when a save was corrupt, and the backup was a copy of the save just written.

## Decisions made where the documents are silent or disagree

1. **Backup generations.** `ROADMAP.md` says "one rolling backup slot"; `PERSISTENCE.md` §7.3 says two generations. Two were implemented, because `PERSISTENCE.md` owns persistence.
2. **"Retired on a verified load".** A displaced save enters the backup chain only if a complete, clean load proved it. The proof is recorded in `rotation.json` as the digest of the save's integrity root. An unproven save is dropped when it is displaced, so the last proven save survives any number of later saves (`SavesAfterASilentProblem_NeverPushTheLastGoodSaveOut`).
3. **Staging location.** §7.1 writes to `<slot>/.staging-<ulid>/`, then renames `.staging-<ulid>` to `<slot>`, which cannot happen if it is inside the slot. Staging and trash directories are therefore siblings of the slot: `.staging-<slot>-<ulid>` and `.trash-<slot>-<ulid>`, so the boot sweep knows which slot each belongs to.
4. **Player instance prefix.** The player uses `chr_`; `DATA_MODEL.md` defines no prefix for characters.
5. **Quick slots.** §3.2 names a single `quick` slot, but §8.2 says "10 quick". One `quick` slot was implemented, alongside `manual_<slug>` and `auto_01`..`auto_05`.
6. **Unreadable `sections.sha256`.** It is re-derived per §3.3, and the load reports it (`IntegrityRootRederived`; the load does not count as complete). §3.3 also says to check against "manifest.json's recorded sizes", but the §4.2 manifest schema records no sizes, and no field was invented to hold them.
7. **Directory fsync.** .NET cannot fsync a directory on Windows. Files are written with `WriteThrough` plus `Flush(true)`, and the step 6 verification and the boot sweep cover a rename lost to a crash.
8. **Windows sharing violations.** Renames and deletes retry with bounded backoff on `IOException` (§7.1). An access denial is surfaced with the path.

## Open question for the document owner

**Content changes versus the alias pass.** `content_hash` seeds every cell stream (`WORLD_ARCHITECTURE.md` §3.4; `PERSISTENCE.md` I-1, I-3 and §7.4 g), so any content change, including a one-value tuning change, moves every regenerated baseline. `PERSISTENCE.md` §6.1 sends a content change with an unchanged `schema_version` through the alias/tombstone pass only. That leaves open how cell and entity deltas, which are keyed by node index and slot ordinal, are re-anchored to the new baseline.

M2 stops at the decision point §7.4 c requires: a `BaselineMismatchException` with `Migratable = true`, and never a silent regeneration. M2b must define the re-anchoring.

## Deferred, with the milestone that owns it

- **M2b** owns the migration chain, the alias/tombstone pass, the historical fixture set and `save:migrate --dry-run`. The load path raises a migratable `BaselineMismatchException` where the chain will plug in.
- **Missing sections.** `companions`, `buildings`, `journal`, `command_log` (with `command_log_sha256`), `orphans` and `preview` arrive with the systems that own them. `command_log_sha256` is omitted today, which per §4.2 means replay is unavailable.
- **Cell `overrides`** (the authored re-roll path): no authored overrides exist yet.
- **Game-loop wiring** belongs to the first gameplay milestone that saves. It covers save, load and autosave through the command/event bus, the `WorldLoaded` event, and the autosave main-thread budget (≤ 2 ms P99, §8.2), which is unmeasured without a game loop. `SaveDocuments.Capture` (the diff) is the only simulation-thread work; `SaveStore.Save` may run on any thread.
- **Domain invariant validation on load** (§7.2: no item in two containers, no orphan reference) comes with the first system that references entity instances. Until then, rebase retires an entity's identity without a reference check.
- **Terrain** in the baseline is a digest-only signature of height samples; the heightfield data structure comes with world rendering.
- **`harvest_seq`** restarts when a regrown node's record is rebased away, so the jitter of its next harvest repeats the first one. This is still deterministic, but it matters once respawn uses the jitter (`PERSISTENCE.md` §5.5).
- **`.failed-*` directories**, left by a commit that fails verification, are kept for diagnosis and never cleaned.
- **Cloud-sync warning UI.** Detection is done (`SaveStore.CloudSyncRoot`, from the OneDrive environment variables and Dropbox's `info.json`); the warning belongs to Presentation.

## Known gaps outside M2 (found during M2)

- **M1b content validation.** Per-kind schema validation is not implemented. The loader now ignores fields that are not in the base definition shape (without this it loaded 0 of 3 real definitions), so kind-specific fields go unchecked. The kind registry also does not match `DATA_MODEL.md` §1: `npc`, `spell` and others are missing, so those directories report DIR003.

## Verify

```bash
dotnet test src/UNNAMED.sln
dotnet test tests/Persistence.Tests --filter "FullyQualifiedName~ExitCriteriaTests"
```

Commits: `51057ff` identity (D-04, D-10), `e683b29` world generation and delta, `a533e0f` persistence, `fee81a0` autosave rotation and cloud-sync detection.
