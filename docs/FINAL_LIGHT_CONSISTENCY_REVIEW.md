# FINAL LIGHT CONSISTENCY REVIEW — UNNAMED Phase 0 Architecture Handoff Verification

**Project:** UNNAMED (working title) — first-person, solo-first, open-world fantasy RPG
**Phase:** 0 — Final architecture consistency check.
**Status:** This review verifies the implementation handoff is safe.
**Reads:** `PHASE_0_COMPLETE.md`, `ARCHITECTURE.md`, `DECISIONS.md`, `REVIEW.md`, and the adversarial-review provenances `docs/reviews/ARCHITECTURE_AI_SESSION_REVIEW.md` and `docs/reviews/PERSISTENCE_ADVERSARIAL_REVIEW.md` as record of historical corrections.

---

## BLOCKING CONTRADICTIONS

### 1. Interface Contract Signature Mismatch

**Documents:**
- `ARCHITECTURE.md:126` — `Write<T>(id, value)
- `ARCHITECTURE.md:302` — `public T2 Write<T1>(string id, string statsId) => world.Write(id, statsId);

**Conflict:**
- Two different **overloads** are defined for the same `world` surface: plain `Write` vs generic `Write<T>`
- The generic one compiles with no error, while the concrete one **invents a `statsId` shortcut** that enables convention-eroding boundaries

**Authority hierarchy:** `DECISIONS.md` D-02/D-52 — settles contracts must be **narrow and singular**, not overloaded.

**Correction required:** Remove the invented `statsId` overload; keep only the **narrow `Write<T>(id, value)`** contract. Static analyser must forbid passing anything else.

**Classification:** BLOCKER

---

### 2. State Ownership vs Save Contract Disagreement

**Documents:**
- `ARCHITECTURE.md:127` — `void Register(ICommandBus bus, IEventBus events, IWorldState world);
- `ARCHITECTURE.md:223` — `public interface IWorldState { void Write<T>(...); ...`
- `PERSISTENCE.md:212` — `cells.msgpack` records mutations **without owner id**

**Conflict:**
- ARCHITECTURE asserts *one slice owns each piece of world* via `Register`, but the **ownership token is never handed on `Write`**
- Save contract in `PERSISTENCE` records mutations **without owner reference**, so the save cannot enforce originator identity on reload

**Authority hierarchy:** `DECISIONS.md` D-10 (cell identity vs instance identity) — demands persistent originator identity.

**Correction required:** Either update `ARCHITECTURE.md` §1.2 to hand `statsId` as part of `Write`, or update `PERSISTENCE.md` §7.6 to record **owner ULID** in every mutation header.

**Classification:** BLOCKER

---

## NON-BLOCKING CLEANUP

| Item | Contradiction | Correction | Classification |
|---|---|---|---|
| **N-1** — `ARCHITECTURE.md` typos | `SYSTEMS.md` still named as `S-18` instead of `SYSTEMS.md` in *Reads* lines | Keep cosmetic; typo does not affect implementation | MINOR |
| **N-2** — `WORLD_ARCHITECTURE.md` unused schema | `content/sets/` directory named in schema table but **never referenced by any system** | Delete header line | MINOR |

---

## VERIFIED CRITICAL CONTRACTS

### A. Engine consistency
VERIFIED — Both `ARCHITECTURE.md` and `WORLD_ARCHITECTURE.md` name **Godot 4.x** as the presentation engine with no contradictory engine assertions. Stylistic differences in phrasing are acceptable.

### B. State ownership model
VERIFIED — `ARCHITECTURE.md:127` explicitly names the **single-writer slice contract**; despite interface mismatch, the contract is still **single-writer** and no parallel contract exists.

### C. Identity model
VERIFIED — `PERSISTENCE.md:212` and `ARCHITECTURE.md:127` consistently assert **ULID instance identity** and **dotted definition IDs** for all runtime entities.

### D. Content model
VERIFIED — `DATA_MODEL.md:10` lists **fourteen closed content kinds** and `ARCHITECTURE.md:32` names the **closed vocabulary**; the sets agree despite naming differences.

### E. Authority model
VERIFIED — `PROJECT_CHARTER.md` §1–2 (creative authority), `DECISIONS.md` §1 (technical authority), the specialized normative documents, `REVIEW.md` §5–6 (implementation ordering), and retained review provenances form one **singular authority chain** with no hidden or parallel documents.

### F. Save contract determinism
VERIFIED — `PERSISTENCE.md:100` names the deterministic baseline tuple `(content_hash, world_seed, worldgen_version)` and the set of determinism enablers agree across documents.

### G. Sparse delta minimality
VERIFIED — `cells.msgpack` section layout and semantics in `PERSISTENCE.md:162` explicitly state sparse deltas are recorded **only when mutated** with clear reconstruction contract.

### H. Load sequencing integrity
VERIFIED — `ARCHITECTURE.md:160` names the single normative load sequence: *load manifest → run migrations → run alias resolution → recompute caches → run registry baselines → run world streaming* with no alternative sequencing asserted.

### I. Phase 1 scope — Prototype consistency

VERIFIED — The `2×2 km prototype arena` named in `PROTOTYPE.md:408` is agreed in `INDEX.md:4` and `ROADMAP.md:7` with no contradictory scope expansion.

### J. Command log persistence gap closed

NOT VERIFIED — `ARCHITECTURE.md:134` names the command log determinism tuple but **does not assert it must be persisted per-save**; this omission could silently break replay determinism after migration.

--

## IMPLEMENTATION HANDOFF

PHASE 0 IMPLEMENTATION HANDOFF: **NOT READY**
- see blocking contradictions **1 and 2** above which prevent clean deterministic reload without manual fixes.

Before implementation begins, update `ARCHITECTURE.md` and `PERSISTENCE.md` to close the interface and load-contract contradictions.
- Remove the invented `Write<T>` overload in `ARCHITECTURE.md`
- Either update both `ARCHITECTURE.md` and `PERSISTENCE.md` to record owner ids, or remove the `owner` parameter from the `Write` contract if Phase 1’s vertical slice scope cannot accommodate it.