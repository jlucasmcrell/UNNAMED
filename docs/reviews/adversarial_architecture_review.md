# Adversarial Architecture Review — UNNAMED Phase 0 Findings

## 1. Executive summary
No **Phase 0 blockers** were identified **after adversarial review**. Implementation could begin after the corrections in section 6.

## Implementation Readiness Assessment

The architecture is **NOT READY** until the changes in *Critical corrections now* are implemented. Specifically, the architecture is **not enforceable in code** until the compile-time guard and static analyser integration are added, and the determinism baseline is **not testable** until the replay test is added.

---


## Phase 0 blockers

No Phase 0 blockers identified.

## IMPORTANT PRE-IMPLEMENTATION CHANGES

1. Add compile-time ownership guard and static analyser check in `src/Domain/**` (`ARCHITECTURE.md` §6.1)
2. Publish single normative load sequence and add integrity test in `src/Save/**` (`ARCHITECTURE.md` §6.2)
3. Record command log for every save and add replay test in CI pipeline (`SYSTEMS.md` vs `ARCHITECTURE.md` §6.3)

---



## 2. Conventions violations summary

1. **`ARCHITECTURE.md` §8 step 5 is wrong in substance and enabler.** Ownership guards are **not enforced** in the domain layer. The single-writer discipline is **not protected by code or tests**, only by convention. Evidence: `ARCHITECTURE_AI_SESSION_REVIEW.md:15–35`, `SYSTEMS.md:70` vs `ARCHITECTURE.md:226` own-slice test.

2. **Load path is underspecified and self-contradictory** (`ARCHITECTURE.md:255` vs `SYSTEMS.md:340` vs `PERSISTENCE.md` §7). Three documents specify three incompatible sequences, none of which reconciles applied migrations with delta baselines. This creates a **critical** defect: the obvious per-section load path produces stale derived state and dangling references.

3. **Command log is not persisted**, so no save is ever truly replayable (`SYSTEMS.md:50` vs `ARCHITECTURE.md:134` vs `PROTOTYPE.md:371`).

4. **Deterministic baseline is underspecified** — no command specifies `system order`, float vs double policy, locale (explicitly named in `ARCHITECTURE.md:53` as culture-sensitive vs invariant comparison), and none of the enablers are ever verified by a test.

**No blocker was created by any of these violations; they are convention and specification defects, not runtime or functional ones.** Every defect can be corrected by **adding a test that checks the boundary**, not redesigning any system.

## 3. Integration violations with state ownership

1. **State ownership boundary rots silently** (`DECISIONS.md D-02` vs `ARCHITECTURE.md` §1). The `IWorldState` interface is handed whole to systems inside tick drain, enabling state-mutating shorts like `world.Write(statsId, newStats)` which compile and pass tests. Evidence: `ARCHITECTURE_AI_SESSION_REVIEW.md:25–35`.

2. **"Save key contract" has no test boundary** (`ARCHITECTURE.md:6.2` vs `PERSISTENCE.md` T-02). The document asserts a full baseline-determinism tuple must appear in one test, but no test checks it, enabling a silent regression.

## 4. Correct architectural decisions worth preserving

1. **Single-writer discipline is enforced in `src/Presentation/**`** (`ARCHITECTURE.md:127–267`, `PROTOTYPE.md:274–279`). Every system owning presentation state is separated into its own project, verified by CI build + grep. The convention is **code-enforced and testable**, which is the strongest possible boundary.

2. **"World State Store is the God object" is named as the critical architectural finding** (`ARCHITECTURE.md:127`). It is the **single hardest architectural decision to change after Phase 1** and must never be violated. The document also names **every defect that could enable an erosion of the boundary**: systems mutating other systems' state, presentation layer owning state, derived state being cached in baselines. These are **critical failures if violated**, not speculative ones.

## 5. Safe to defer

| Finding | System | Current defect | Recommended correction | Cost now | Cost later |
|---|---|---|---|---|---|
| **Deterministic generation baseline** | `PERSISTENCE.md` T-02 | Baseline is underspecified; no test checks the full tuple | Add a **replay test** that checks the full tuple: `(seed, content_hash, worldgen_version, system_order, float_policy)` | Low | High — retrofitting proof is harder once code depends on the baseline |
| **Save/load path determinism** | `ARCHITECTURE_AI_SESSION_REVIEW.md C-2` | Three incompatible load paths, none of which checks migrations + alias resolution + cache recomputation | Publish **one normative load sequence**, make it the only one, and add a test that asserts it: "apply save baseline → run migrations → run alias resolution → recompute caches → apply player/companion state → validate integrity"
|  |  |  |  |  |  |

## 6. Critical corrections now

1. **Add a compile-time guard for ownership in `src/Domain/**`**
   - Interface `IWorldState` must narrow to `IIdentityView`, `IInventoryStore`, `IQuestStore` per system
   - Every system must implement `Register(IWorldState narrowed)` with static analyser guard `C4`
   - **Static analyser must fail the build if any system mutates outside its narrowed view**
   **Cost:** Low — a few header files and a compiler guard. **Classification:** IMPORTANT

2. **Add a replay test for command log determinism**
   - Record command log for every save (`SYSTEMS.md:50` must change from `None` to `Persistent`)
   - Add test `T-command_log_replay` that asserts equality after feeding log to fresh world
   - Pin `Ordinal` sort on instance IDs and `InvariantCulture` float/double in CI
   **Cost:** Low — one test file and one CI job. **Classification:** IMPORTANT

3. **Add a per-save baseline integrity check**
   - Every save must include a recorded command log in `journal.jsonl`
   - The command log must feed the integrity baseline: `(content_hash + command_log_hash)`
   - **Static analyser must fail the build if any save lacks the command log baseline**
   **Cost:** Low — one integrity test per save. **Classification:** IMPORTANT

## 7. Architecture strengths worth preserving

1. **State ownership separation enforced by compile boundaries** (`ARCHITECTURE.md:5.1`, `PROTOTYPE.md:274–279`).
2. **Sparse deltas over baselines** (`ARCHITECTURE.md:127`, `PERSISTENCE.md:§3`).
3. **One system owns each slice** (`ARCHITECTURE.md:212`) — protected by `.csproj` boundaries.

**Conclusion:** These strengths align with both charter vision and adversarial review findings. They must not be weakened or changed.

**Final directive:** After corrections in §6.1, §6.2 and §6.3, the architecture is **READY**..