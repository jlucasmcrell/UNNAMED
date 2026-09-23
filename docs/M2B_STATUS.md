# M2b Implementation Status

**Date:** 2026-09-23
**Phase:** 1
**Milestone:** M2b — Save Migration Harness and Baseline Compatibility (`ROADMAP.md`, refined by `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md`)
**Status:** **COMPLETE** — every exit criterion is evidenced by an automated test. Deferred items are listed at the end.

---

## What M2b fixed

M2 fed the whole compiled `content_hash` into every cell's random streams, so a one-value balance edit (wolf HP 30 -> 31) moved every rock, node and spawn slot in the world. M2b separates the two things that were conflated:

- **Exact content identity:** `content_hash`, "what exact content pack wrote this save".
- **Baseline procedural compatibility:** proven per changed cell by the hash of the baseline it was made against.

It then builds the save-evolution machinery on top.

## Exit criteria

| Criterion (`ROADMAP.md` M2b, spec §19) | Evidence (test) | Status |
|---|---|---|
| Ordered schema migrations exist | `SchemaMigrations.Production` = 1->2, 2->3; `MigrationTests.TheProductionChain_HasOneStepPerVersion_InOrder` | PASS |
| Multi-hop migration is tested | `MigrationTests.Schema1_MigratesStepByStep_ThroughEveryVersion_ToTheCanonicalState` (steps 1->2 then 2->3, compared with `Fixtures/v1/expected.json`) | PASS |
| Every historical fixture loads under current code | `HistoricalFixtureTests.Fixture_LoadsToItsExpectedCurrentState` (v1, v2, v3) and `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState` (v1, v2, v3) | PASS |
| Fixture policy is documented | `tests/Persistence.Tests/Fixtures/README.md`, `PERSISTENCE.md` §6.2; enforced by `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture` | PASS |
| A required player field ships with a migration, fixtures and a CI test | Schema 3's `appearance_seed`: `Schema2To3_AddsTheRequiredAppearanceSeed_DerivedFromTheUlid` (value pinned independently) | PASS |
| Rename/removal requires an explicit map; a missing map fails CI | `ARenamedDefinition_ResolvesThroughTheAliasMap`, `ARenameWithoutAMap_FailsTheLoad_NamingTheId`, `RemovingTheAliasFromTheContentPack_FailsTheFixtureLoads`, `ARemovalWithAReplacement_ConvertsTheReference`, `ARemovalWithNoReplacement_DropsTheReference_AsReportedLoss`, `ARemovalWithNoDisposition_FailsTheLoad` | PASS |
| An unrelated content change does not perturb generation | `World.Tests` `RK01_BaselineNeutralContentChange_ChangesContentHash_NotTheBaseline`; `MigrationTests.ABalanceEdit_ChangesContentHash_AndTheSaveLoadsWithoutReshuffle` | PASS |
| Changed-cell deltas record and verify `baseline_hash` | `Schema1To2_AddsTheRequiredBaselineHashes_DerivedFromTheRegeneratedBaseline`; `WorldDeltaTests.EverySnapshotRecord_NamesTheBaselineItWasMadeAgainst` | PASS |
| A baseline mismatch never silently accepts an old delta | `ABaselineAffectingChange_IsDetectedPerCell_AndRefused`, `ExitCriteriaTests.RiskSpike_GenerationChange_IsRefused_NeverAppliedToAnotherBaseline`, `WorldDeltaTests.ARecordMadeAgainstAnotherBaseline_IsRejected_NeverApplied` | PASS |
| One synthetic baseline-affecting rebase is proven | `ARegisteredTransition_RebasesTheChangedCells_ByStableIdentity`, `ATransition_NeverReaimsARecordWhoseTargetVanished`, `ACreatedInstance_IsProvenAgainstItsHostCell_AndRebasedOnlyByATransition` | PASS |
| Accidental generator drift is detectable | `World.Tests` `AccidentalGeneratorDrift_IsDetected_WithoutAVersionBump`, `Worldgen2_IsPinned_ProbesFingerprintAndRegion`; `MigrationTests.GeneratorDrift_WithTheSameVersion_IsCaughtByTheBaselineHashes` | PASS |
| Random channels are semantic and call-order isolated | `StableRandomTests.Channel_MatchesIndependentReference`, `Samples_AreAddressed_NotDrawnInSequence`, `EveryKeyComponent_SelectsAnotherChannel` | PASS |
| An unrelated random draw shifts nothing else | `StableRandomTests.AnUnrelatedNewDraw_MovesNothingThatAlreadyExists`, `ChangingOneRulesCount_KeepsItsExistingNodesInPlace`; `GeneratorTests.BaselineAffectingChange_ChangesTheFingerprint_AndOnlyTheAffectedOutput` | PASS |
| `save:migrate --dry-run` reports the plan | `SaveToolTests.DryRun_ReportsTheMigrationPlan`, `DryRun_OfABlockedSave_ExitsNonZero_AndNamesTheBlocker` | PASS |
| The dry run is non-mutating | `SaveToolTests.DryRun_ChangesNoPersistentFile_WhetherReadyOrBlocked`, `ThePlanThroughTheStore_WritesNothing_NotEvenLoadProof` (every file's bytes and write time, and every directory, compared before and after) | PASS |
| Migration commits through the M2 atomic path | `SaveStore.Migrate` calls the same commit as `Save`; `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState` | PASS |
| An interruption leaves the old or the new save, never a partial migration | `MigrationTests.AMigrationKilledAtAnyStep_LeavesTheOriginalOrTheMigratedSave`: a real process kill at each of the six commit steps, on Windows | PASS |
| CI proof: v1 -> v2 -> v3 non-lossy | `Fixture_LoadsToItsExpectedCurrentState(1)` and `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState(1)` | PASS |

The fourteen required tests of spec §10 map to the rows above. Spec 12.1 corresponds to the two required-field rows: 1->2 derives `baseline_hash`, and 2->3 adds the player's `appearance_seed`.

## Compatibility model (as implemented)

| Field | Meaning | On mismatch |
|---|---|---|
| `save_format` (1) | Container layout. Unchanged by M2b | Refuse |
| `schema_version` (3) | Shape of persisted state | Ordered chain; a gap or a newer schema refuses |
| `content_version` | Human label | Reported only |
| `content_hash` | Exact content pack, alias map included. Not a generation input | Definition-ID pass: rename, convert, destroy; unresolved refuses naming the ID |
| `world_seed` | Seed | - |
| `worldgen_version` (2) | Human epoch of the generator contract | Registered worldgen migration, or refuse |
| `worldgen_fingerprint` | Generator id + version + RNG contract + placement data + canonical probe output | Warning; cells are decided by `baseline_hash` |
| `rng_contract_version` (2) | Key-to-value encoding | As `worldgen_version` |
| `baseline_hash` (per changed cell; entity and created host cells via the entities section's `baselines` table) | Digest of that cell's generated output | Equal: apply. Different: registered transition, or refuse |

**Name mapping to M2.** Schema 1's `worldgen_digest` was replaced by `worldgen_fingerprint` + `rng_contract_version`; the 1->2 migration checks the old digest before it trusts the old baseline. All other M2 field names are unchanged.

## Stable RNG (RNG contract 2)

`RngChannel.Open(world_seed, cell, subsystem, semantic_key)` takes the first 8 bytes of SHA-256 over the length-prefixed fields; sample `n` is SplitMix64 at counter `(n << 32 | attempt) + 1`. Values are therefore addressed rather than drawn in sequence. `Int` uses unbiased rejection, with each attempt drawing from the same sample's own counter space. There is no content hash and no `worldgen_version` in the key; the contract version is recorded per save. Worldgen 2 uses these channels:
- `terrain/height` sample `i`;
- `resources/<rule>/count` and `resources/<rule>/<NN>` (samples 0 and 1 give x and z);
- `wildlife/<population>/<NN>`.

Node keys are `node.<cell>.<rule>.<NN>`. Every value is pinned to an independent Python implementation (`tests/World.Tests/Reference/worldgen_v2_reference.py`).

## Baseline hash and fingerprint behaviour

- **Load:** regenerate each changed cell and compare it with its saved hash. Equal: apply the delta. Different: look for a transition registered for exactly (save fingerprint -> running fingerprint) and rebase conservatively, or else refuse, listing every mismatched cell. `WorldDelta.FromSnapshot` independently rejects any record whose hash does not match.
- **Canonical probes:** seed `0x0DDB1A5E5BAD5EED`; cells `r_0_0:c_00_00`, `r_0_0:c_19_19`, `r_neg1_neg1:c_10_10`, `r_7_neg3:c_03_17`. The probe digest, the fingerprint (`sha256:9810297b…`) and a region digest are pinned under the reference profile.

## Historical fixtures

| Version | Written by | Contents |
|---|---|---|
| v1 | M2's writer at `7ff4c57`, in a scratch worktree | Player (renamed potion in inventory), four changed cells, tombstoned wolf, moved deer |
| v2 | The schema-2 writer (`b310979`) | The same world |
| v3 | The schema-3 writer | The same world plus a created instance (iron sword in `r_0_0:c_00_06`) |

The fixtures load against a fixture content pack (0.2.0, which renames `item.potion.healing_draught`) and `worldgen_profile.json`. v1 and v2 load to the same state, differing only in generated ULIDs. That shows the 1->2 migration reproduces what the v2 writer produces natively.

## CLI

```text
dotnet src/SaveTool/bin/Debug/net8.0/UNNAMED.SaveTool.dll save:migrate --dry-run <save-dir> --content-root <dir> --worldgen <profile.json> [--content-version <v>]
dotnet src/SaveTool/bin/Debug/net8.0/UNNAMED.SaveTool.dll save:migrate <save-dir> --content-root <dir> --worldgen <profile.json> --content-version <v>
dotnet src/SaveTool/bin/Debug/net8.0/UNNAMED.SaveTool.dll save:inspect <save-dir>
```

Exit codes: 0 when up to date or ready, 1 when blocked, 2 for bad arguments.

## Crash and fault testing

- **Kill-mid-migration:** a real process kill at each of the six §7.1 steps during a v1 -> v3 migration, then the boot sweep. The slot always loads to the expected state, as either the original schema-1 save or the committed schema-3 save. No staging or trash remains, and any pre-migration copy passes its integrity check.
- **Kill-mid-save:** the M2 kill tests remain, and now assert the exact step each kill hit.
- **Fault injection:** the in-process recovery tests cover each recovery path. Corruption and quarantine tests are unchanged from M2.

## Test totals

| Project | Tests |
|---|---|
| Architecture.Tests | 11 |
| Content.Tests | 34 |
| Domain.Tests | 4 |
| EntityRegistry.Tests | 23 |
| World.Tests | 58 |
| Persistence.Tests | 110 |
| **Total** | **240, all passing** (`dotnet test src/UNNAMED.sln`, Windows, 2026-09-23) |

## Decisions taken

1. **Two genuine schema steps.** M2b added two real schema steps rather than synthetic ones, so the chain and the fixtures describe real history:
   - 1->2: baseline compatibility;
   - 2->3: the required player field and created instances.
2. **Schema-3 changes.** The required player field is `appearance_seed`, which `PERSISTENCE.md` §5.1 lists as player identity; it is derived from the ULID for older saves, so it is deterministic. Created persistent instances were needed because the M2 format could not represent the "created persistent entity" the fixtures must contain.
3. **Transitions are keyed by the exact fingerprints.** The generic "safe automatic rebase" of spec §7 applies only through such a registered transition, which reconciles spec §7 with the §9 matrix ("no migration: refuse").
4. **Frozen section shapes.** Each version's section shapes are frozen (`Sections/V1`, `Sections/V2`), and each step reads and writes frozen types.
5. **The fingerprint is behavioural.** It covers the generator's output on the probe cells rather than the assembly's bytes, which are not reproducible across builds (`PERSISTENCE.md` §11.2).
6. **Removal dispositions.** `removed: old: ~` is the explicit `destroy` disposition: reported loss, never silent.
7. **Doc reconciliation.** The docs were reconciled as one change:
   - load order per spec §16: schema chain before the ID pass;
   - one `quick` slot, per M2's implementation and §3.2, replacing §8.2's "10 quick" (a policy value, raisable without a format change);
   - sibling staging names;
   - two backup generations;
   - `chr` prefix;
   - the ULID case wording.

## Deferred, with owner

- **`quarantine` disposition** for removed definitions: needs `orphans.msgpack` (first system whose records can be orphaned).
- **Relic conversion** of removed item definitions (`DATA_MODEL.md` §6 rule 4): with item instance records that carry rolled properties.
- **Registered worldgen migrations beyond the 1->2 schema step:** none exist yet. The registry is the transition list; the first future generator change adds its transition plus a fixture.
- **Position legality after a rebase** (terrain geometry): created instances and moved occupants are carried as-is, because the baseline has only a terrain signature so far.
- **Pre-migration copy retirement:** kept indefinitely until a UI confirms the migrated save loads.
- **Production world configuration:** the CLI takes the generation profile as a file, since the game has none yet (the fixtures carry theirs).
- **Content lint gaps predating M2** (M1b): no per-kind schema validation; references inside nested definition fields are not checked.
- **Linux CI:** not yet observed for these commits, because nothing is pushed.
