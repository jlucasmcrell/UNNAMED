# REVIEW.md — Phase 0 Adversarial Review

**Status: COMPLETE — no open defects.** Every finding raised by the review has been either **fixed** or **ruled and recorded**. There is no `OPEN` or owner-pending item remaining in the Phase 0 document set. This document is the audit trail for that claim.

**This document is not part of the Phase 0 deliverable set** and does not carry design authority — it records defects found in those documents and what was done about each.

**Method.** Three independent adversarial passes, a mechanical integrity sweep, and a final consistency reconciliation:

1. **Cross-reference audit** — all 156 `Reads.` references in `SYSTEMS.md` expanded against system titles; all 163 backticked `.md` references resolved against disk; all ~200 `D-nn` citations checked against `DECISIONS.md`; shared constants compared across documents.
2. **Adversarial design review** — `PROGRESSION.md`, `PROTOTYPE.md`, `VERTICAL_SLICE.md`, `ROADMAP.md`, `GAMEPLAY_LOOPS.md`, `RISK_REGISTER.md` attacked against the charter, plus separate persistence/world and architecture/session passes.
3. **Arithmetic and formula re-derivation** — the XP curve and the cell-addressing formula were recomputed independently rather than taken on trust.
4. **Propagation reconciliation (§B-ter)** — every fix this review claims was verified *in the authoritative document*, not taken on the review's word. This pass found eight defects, including a truncated `PERSISTENCE.md` and two documents still claiming authority over the load sequence.

Known register risks `RK-01`..`RK-16` are not restated; findings that merely duplicate them were discarded.

**Contents.** §A records the nine defects fixed in the first pass. §B records the ten defects the first pass left open and how each was resolved. §B-bis records the seven findings of the third pass and their resolutions. **§B-ter records the eight propagation failures found by the final reconciliation**, including the `PERSISTENCE.md` truncation. §C records findings consciously accepted rather than changed. §D lists the full text of each independent pass. §E states what this review did **not** do.

---

## A. Fixed in this session

### A-1 · CRITICAL — XP curve did not sum to its stated total · **FIXED**

`PROGRESSION.md` §3.2 stated `XP(n) = 100·(n−1)^1.85` with a +15% band multiplier at levels 11–30, and claimed "Total to 50 ≈ 1.9M XP".

Re-derived independently:

| | XP to level 50 |
|---|---|
| Sum of the stated formula, with the stated multipliers and rounding | **2,448,025** |
| Sum with no band multiplier | 2,369,959 |
| Stated total | ~1,900,000 |
| Error | **+28.8%** |

This was not cosmetic. `AG-6` ("designed XP/hour band: 0.6×–1.4× of the tier target rate") is a **Phase-1 exit criterion** (`ROADMAP.md` — "XP per hour stays inside the AG-6 band"), and every tier rate, the pacing table, and the prototype's quest reward were derived from the wrong total. The failure mode is silent: it surfaces as "levelling feels 30% slow" after content is authored.

**Fix applied.** Stated total corrected to the evaluated 2,448,025, with an explicit note that the figure is *derived* and that changing the exponent, rounding, or band multiplier requires re-deriving §3.2 and `AG-6`. Implied rates now stated: ≈28.8k XP/h at 85 h, ≈37.7k at 65 h, ≈32.6k at 75 h.

**Recommended follow-up (not applied).** Make the published total a *test* — a unit test that sums the curve and asserts the documented figure — so this class of error cannot recur silently.

---

### A-2 · MAJOR — Death penalty specified three incompatible ways · **FIXED**

Three documents specified three different mechanics, and the Phase-1 gate contradicted the progression spec:

| Source | Specification | Model |
|---|---|---|
| `PROGRESSION.md` `AG-8` | "bounded fraction of the **current level's** progress … floored so a level is never lost" | **Lost XP** |
| `PROTOTYPE.md` step 7 | "−10 % **XP debt** (not lost XP)" | **Debt** |
| `PROTOTYPE.md` `C17` | fails the build on "**lost XP instead of XP debt**" | **Debt**, enforced |
| `VERTICAL_SLICE.md` P8 | "XP debt + temporary injury + corpse-recovery trip" | **Debt** + recovery |

So `C17` would have **failed an implementation that followed `AG-8`**.

**Fix applied.** `AG-8` rewritten to specify **XP debt, not lost XP**, retaining the never-lose-a-level floor and the exclusion of mastery/profession/reputation/skill. The assumption note now names debt as canonical and instructs that any change be made in `PROGRESSION.md` first and propagated to `PROTOTYPE.md` C17 and `VERTICAL_SLICE.md` P8.

---

### A-3 · HIGH — 29 wrong `S-nn` cross-references in `SYSTEMS.md` · **FIXED**

Every `Reads.` entry was expanded and matched against the system it named. References were correct for `S-03`…`S-13` and wrong for many later systems — e.g. `S-21 World Streaming` read "S-16 (player position)" where S-16 is Items & Loot and player position is S-22; `S-29 Quests` read "S-26 (building)" twice, where buildings are S-32 and relationships S-26.

**30 corrections applied**, each verified by responsibility rather than by pattern. Notable: `S-07`→S-15 (equipment), `S-08`→S-12/S-29/S-17/S-32, `S-09`→S-12/S-13/S-17, `S-12`→S-15/S-13/S-22, `S-17`→S-32, `S-20`→S-32/S-24/S-30+S-34, `S-21`→S-22/S-31/S-23, `S-22`→S-32/S-34, `S-23`→S-22, `S-24`→S-26/S-30, `S-25`→S-32, `S-29`→S-32, `S-30`→S-22/S-34, `S-28`→S-30. One non-system reference ("S-03 hardware budget settings") was replaced with a pointer to `WORLD_ARCHITECTURE.md` §12, since streaming budgets are configuration, not a system.

---

### A-4 · MEDIUM — Stale "these documents do not exist yet" assertion · **FIXED**

`SYSTEMS.md` assumption 1 still claimed `PERSISTENCE.md`, `WORLD_ARCHITECTURE.md` and `PROGRESSION.md` were absent (true when written, false now). Rewritten as a superseded note that defers to those documents as authoritative for their detail.

---

### A-5 · MEDIUM — Mislabeled citations and a companion-gating contradiction · **FIXED**

- `GAMEPLAY_LOOPS.md` attributed "dangerous places you can enter too early" to `RK-10` (project scope); it is the charter's pillar 1. Corrected to cite the charter.
- `GAMEPLAY_LOOPS.md` cited "`RK-08` content tuning" for node respawn tuning; `RK-08` is authoring throughput. Reference removed.
- `GAMEPLAY_LOOPS.md` said Phase 1 ships a companion "only after `RK-05`'s reliability metrics pass", while `PROTOTYPE.md` explicitly makes those metrics **measured but not gating**. Rewritten to match `PROTOTYPE.md`: Phase 1 must prove a companion can be recruited, follow, fight and survive a save/load; AI *quality* is a vertical-slice concern.

---

### A-6 · HIGH — Risk register was not the single risk authority · **FIXED**

`PERSISTENCE.md` kept local risks `RK-P01`..`RK-P08` and `WORLD_ARCHITECTURE.md` kept `RK-A1`..`RK-A9`, and `RISK_REGISTER.md` mentioned neither — the cross-referencing was one-way, so a session reading only the register would miss them. The four with **no** register equivalent were promoted as `RK-11`..`RK-14`:

| New ID | Risk | Why it earned promotion |
|---|---|---|
| `RK-11` | Dirty-flag completeness | Silent world-state loss with a *valid-looking* save — the only risk here with no error signal at all |
| `RK-12` | Offline catch-up across the save boundary | Needs a **design decision**, not just a mitigation |
| `RK-13` | Atomic commit untested on the target OS | Backs the corruption-recovery guarantee; Windows rename semantics differ from POSIX |
| `RK-14` | Navmesh stitching at cell seams under player buildings | Recorded as **explicitly unsolved** and gates `RK-05`/`RK-06` |

The remaining 13 local IDs were deliberately **not** promoted, and a mapping table now records why each was excluded, so the omission is a visible decision rather than a gap.

---

### A-7 · MEDIUM — Owner-question list disagreed between documents · **FIXED**

Promoting `RK-12` introduced a fourth owner question, but `DECISIONS.md` still said three and `RISK_REGISTER.md` said "three plus one". `DECISIONS.md` is now the authority and lists four, with the "while you were away" policy added and a documented default.

---

### A-8 · CRITICAL — Cell addressing returned out-of-range indices for negative coordinates · **FIXED**

`WORLD_ARCHITECTURE.md` §3.3 specified, under a heading reading "Conversions (exact, no floating-point accumulation)":

```
cellOf(world) = ( floor(world.x / 100) mod 20, floor(world.z / 100) mod 20 )
```

and mandated that every conversion be tested at `x,z ∈ {-200.0, -100.0, -0.001, 0, 0.001, 100.0}` — but never stated the expected outputs, so the test could not fail.

The document correctly anticipates the *floor-versus-truncation* trap, then contains a second, independent trap in the same expression: in C#, C, C++ and JavaScript the remainder operator truncates toward zero, so it returns a **negative** value for negative operands.

| `world.x` | `floor(x/100)` | naive `% 20` | required floor-mod |
|---|---|---|---|
| `-200.0` | `-2` | **`-2`** ✗ out of range | `18` |
| `-100.0` | `-1` | **`-1`** ✗ out of range | `19` |
| `-0.001` | `-1` | **`-1`** ✗ out of range | `19` |

Three of the six mandated values fall outside the `[0, 19]` range defined in §3.2, producing keys like `r_neg1_0:c_neg1_11` that no baseline generator, delta record or navmesh bake would match. This is a save-format-shaped bug in the function every other subsystem's addressing, persistence sharding and spawn budgeting depends on. Python's `%` *is* Euclidean, which is likely why it survived review by anyone reasoning from Python.

**Fix applied.** The formula now calls an explicit `floormod(a, n) = ((a % n) + n) % n` helper, the trap is documented as a second independent hazard, the mandated test values now carry **stated expected outputs** so the test is falsifiable, and the helper is specified as a single shared function rather than re-derived per call site.

---

### A-9 · MAJOR — Two documents gave opposite answers on manifest integrity · **FIXED**

`DATA_MODEL.md` asserted "the manifest carries a checksum over the state payload" and "a failed load never leaves a partially applied world". `PERSISTENCE.md` explicitly rejects the first as "a trap" — "the manifest carries **no** checksum of itself; the integrity root is `sections.sha256`, which covers every other file including `manifest.json`" — and sanctions partial load as "partial recovery beats none".

Two documents, both declared normative for the implementer, describing incompatible integrity mechanisms, where a reader trusting `DATA_MODEL.md`'s one-line summary would implement a checksum that must not exist.

**Fix applied.** `DATA_MODEL.md`'s integrity bullet now defers to `PERSISTENCE.md` as the authority, states that the manifest does not checksum itself, and describes quarantine-with-explicit-loss rather than the contradictory partial-load claim.

---

## B. Resolved in this session (second pass)

Every defect the first pass left open has been closed. Ten were fixed; the rest were **deliberate design rulings** that the review could not make on its own and which have now been made and recorded. **No open defect remains in the Phase 0 set.**

| # | Was | Resolution |
|---|---|---|
| **B-1** | **CRITICAL** — three incompatible attribute sets (7 / 6 / 5) across `PROGRESSION.md`, `DATA_MODEL.md`, `VERTICAL_SLICE.md` — and `PROTOTYPE.md` carried a fourth (3) that the audit had missed | **Ruled: `PROGRESSION.md`'s seven are canonical** (`might`, `endurance`, `agility`, `precision`, `will`, `insight`, `presence`). All four documents now agree. `PROTOTYPE.md` uses all seven **in schema** with only three **mechanically live**, since the count is a save-schema commitment and shipping three now would be a migration later. |
| **B-2** | **HIGH** — `ROADMAP.md` scheduled `M7` (Building) and `M8` (Dungeon/boss) inside Phase 1, while `PROTOTYPE.md`, `GAMEPLAY_LOOPS.md` and `SYSTEMS.md` all defer them, and `RISK_REGISTER.md` would have scored that as a Phase-1 *failure* | **Ruled: the phase boundary is `M6`.** Phase 1 = `M0`–`M6`; Phase 2 = `M7`–`M9`. `M7`/`M8` relabelled Phase 2, the dependency graph and critical path re-cut, and the authority rule stated: **`ROADMAP.md` governs *when*, `PROTOTYPE.md` governs *what is in Phase 1***. Faction/reputation moved out of `M4` into `M7`; the day/night requirement removed from `M4`'s exit criteria. |
| **B-3** | **HIGH** — content kinds disagreed three ways, and `PROTOTYPE.md` declared a validator rule its own directory list could not satisfy | **Fixed.** `DATA_MODEL.md`'s closed kind table extended with the four missing kinds (`node`, `spawn`, `location`, `config`), each given a real schema and example (§4.16–§4.19). `ARCHITECTURE.md`'s conflicting illustrative tree corrected to match, and the kind table declared the authority. |
| **B-4** | **HIGH** — `ARCHITECTURE.md` specified 3 projects + `tests/Domain.Tests`; `PROTOTYPE.md`/`ROADMAP.md` said 2 projects + `src/Domain.Tests` | **Fixed.** Three projects (`Domain`, `Application`, `Presentation`), tests under top-level `tests/`. `PROTOTYPE.md`, `ROADMAP.md` and `ARCHITECTURE.md` aligned, with `ARCHITECTURE.md` §3 declaring itself the structure authority and explaining why `Application` exists. |
| **B-5** | **HIGH** — `PROTOTYPE.md`/`DATA_MODEL.md` cited a `save_version` field that does not exist in the manifest | **Fixed.** Both now cite `schema_version`, with an explicit note that it is not `save_format` (container) or `content_version`. |
| **B-6** | **HIGH** — `S-04 Time & Calendar` marked Phase 2 while **14** Phase-1 systems declare a read on it | **Ruled: S-04 is split, not deferred.** The **clock** (monotonic `world_tick`, absolute deadlines, elapsed-time queries) ships in **Phase 1**; the **calendar** (day/night, seasons, timescale controls) is **Phase 2**. `PROTOTYPE.md` independently reached the same conclusion. Durations are stored as absolute deadlines so they survive a variable `tick_delta` — which is also what makes `RK-12` resolvable. |
| **B-7** | **MEDIUM** — five `S-nn` refs resolved to a system that does not own the claimed state | **Fixed by ownership, not by pattern-swap.** `S-24` no longer reads a "killed" set (it owns its own life-state); `S-28` reads `S-22` for known facts (S-22 owns the journal; S-30 owns *locations*); `S-26` reads `S-14`/`S-36` for gift/trade and `S-29` for quest outcomes (**not** `S-17` crafting, and **not** `S-08` XP — a second mislabel I found while verifying); `S-20` reads `S-31` for spawn population state. `S-11`→`S-12` retained deliberately, since `S-11` owns removal triggers and does not own deaths. |
| **B-8** | **MEDIUM** — `SYSTEMS.md`'s `GenerateBaseline(seed, contentVersion)` dropped `worldgen_version` and `content_hash`, the fields `RK-01` calls the likeliest save-corrupting bug | **Fixed.** Signature widened to `GenerateBaseline(worldSeed, worldgenVersion, contentHash)` and the determinism contract restated as the full tuple, with an explicit note on why the narrower form was wrong. |
| **B-9** | **MAJOR** — no loop listed coin as an output, yet coin had sinks; every guard bounded *rate*, never *stock* | **Ruled and fixed.** Coin added to `FIGHT`'s outputs as the **primary faucet** (loot tables + bounties), sinks enumerated per phase, and **E-10** added: the architectural rule is that a sink must be **non-discretionary** (a player who ignores it degrades), with a measured ceiling on total coin outstanding per level. |
| **B-10** | **MAJOR** — `AX-LVL`/`AX-SKL`/`AX-WM` all advance from the same event, so three of ten "orthogonal" axes may be one axis | **Ruled: the objection is accepted as live, and the verdict is moved earlier.** A **2×2 fixture test** in `M2c` — four characters at (high/low skill) × (high/low mastery), each reachable without the other axis moving — settles it as a **pure domain unit test**, before the save schema freezes, instead of waiting for telemetry at `M12`. Also recorded: skill may **gate** other axes but never be **spent** as their currency, and if the fixture fails the merge happens before `M2c` rather than after saves exist. |

---

## B-bis. Additional findings from the architecture/session review — all now closed

A third independent pass (`reviews/ARCHITECTURE_AI_SESSION_REVIEW.md`) targeted architecture, simulation tiers and AI-session hazards. Its findings and their resolutions:

| Severity | Finding | Resolution |
|---|---|---|
| **CRITICAL** | **Slice-ownership had no enforcement.** `IWorldState` was handed whole to every system with an unguarded `Write<T>`, so "a system may mutate only its own slice" was a sentence, not a boundary — while the presentation seam *is* compile-enforced. `RK-09` covered presentation→domain, which the existing checks catch; domain→domain was in no register entry and no test. | **Fixed structurally** (`ARCHITECTURE.md` §5). Write operations moved to `IWorldStateWriter`, **`internal` to `Domain`**, so `Application` and `Presentation` cannot reach them at all — the same class of guarantee as the Godot-reference ban. Component keys are registered at composition, making "a component nobody owns" a **startup error**. Both checks assert in Milestone 1–2, before there is anything to violate. The residual (a Domain system deliberately writing another's component) is recorded as **`RK-15`**, not claimed as solved. |
| **CRITICAL** | **Three incompatible load orders**, and derived caches with no defined recomputation point | **Fixed.** `ARCHITECTURE.md` §8 now separates **boot** from **load**, publishes the normative load sequence (a)–(i), declares `PERSISTENCE.md` the authority, and rules explicitly on the two contradictions: a **`save_format` mismatch refuses**, while a **quarantinable corrupt section loads without it** — both paths exist and are distinct. Derived caches are recomputed at **one** point, after migration and alias resolution. Recorded as **`RK-16`**. |
| **CRITICAL** | **The determinism contract was unverifiable**: `command_log` named as an input but persisted only in debug saves; no replay test; two sources of truth for system order; no comparison-culture or float/double policy | **Fixed.** `SYSTEMS.md` §1 restates the contract as the full tuple, requires **ordinal comparison** and a pinned numeric policy set once in `Directory.Build.props`, and makes the **command log a persisted artifact for every save** exercised by a replay test. |
| **CRITICAL** | **Tier A had no admission control** — the cap was enforced only by spawn suppression, so the `RK-02` frame-budget measurement was not reproducible | **Fixed** (`WORLD_ARCHITECTURE.md` §7.4). Promotion now includes an **admission test** with three explicit outcomes (admit / queue / refuse), `pinned` actors are subject to the test and merely win ties, over-budget admissions are **counted and surfaced on the debug overlay**, and the interaction with §12's "reduce radii" lever is stated so the two cannot be applied independently and cancel each other out. |
| **MAJOR** | **Content tuning silently reinterpreted saved derived values**, because `content_hash` matching skips migration | **Fixed** (`PERSISTENCE.md`, `DATA_MODEL.md`). Split *shape* from *tuning*: content that a persisted derived value was computed from is **baseline-locked**, and changing it requires a schema/migration step rather than being treated as a mere mismatch detector. |
| **MAJOR** | **Game-time units were undefined** — no document fixed ticks-per-day or the game-to-real ratio, yet `RK-12`, `AG-2`, spawn windows, quest deadlines and `PROTOTYPE.md` C12 all used that unit; `WORLD_ARCHITECTURE.md`'s `72000 # ~1 game hour` was arithmetically one *real* hour | **Fixed.** `config.time` (`DATA_MODEL.md` §4.19) now **defines** the units in one place: 20 ticks/s, 1 game minute = 2 real seconds ⇒ one game day = 96 real minutes. The wrong comment in `WORLD_ARCHITECTURE.md` is corrected to state what the window actually is (a multi-day population respawn). `config.rest` adds the wait/fast-forward that C12 needs, since C12 cannot mean 96 real minutes of standing still. |
| **MAJOR** | **The prototype's most instructive item had no schema** — `item.tome.ember_primer` "grants a spell when read", the one item exercising the half of C7 that requires **zero** C# changes | **Fixed.** `ItemDefinition` gained **`use.grants`**, a list of the *existing* closed reward kinds from §4.11 — no new vocabulary, so the validator's reward-kind check covers it with no new rules. `ember_primer` is now the worked example, and the same field carries ability/recipe/title/access grants with no further schema work. |

---

## B-ter. Final consistency reconciliation (pre-implementation)

A fourth pass checked that every fix above had actually **propagated into the authoritative documents**, treating this review's own claims as unproven. It found propagation failures that the earlier passes could not have seen, because they were caused *by* those passes.

| # | Defect found | Resolution |
|---|---|---|
| **R-1** | **`PERSISTENCE.md` had been truncated to a 154-line revision.** It retained §1–§5 but lost §6–§11 — including §7.1/§7.2, which its own tests T-13/T-14 and six other documents cited, and the mandatory test list beyond T-14. | **Restored.** The full eleven-section specification is rebuilt, including the numbered write sequence (§7.1), corruption/quarantine table (§7.2), backup rotation (§7.3), load sequence (§7.4), size budgets (§9), and the complete T-01…T-28 test list. All 16 external section citations now resolve. |
| **R-2** | **The load sequence had two normative authors.** `ARCHITECTURE.md` §8.2 published a full ordered list while `PERSISTENCE.md` owned the format; they differed in order *and* on whether partial load is legal. | **Resolved: `PERSISTENCE.md` §7.4 is the sole authority.** `ARCHITECTURE.md` §8.2 no longer restates a sequence; `SYSTEMS.md` S-33's inline sequence — which omitted alias resolution and derived-cache recomputation — is marked superseded. The partial-load/refusal distinction is now stated identically in all three. |
| **R-3** | **`SYSTEMS.md` S-01 still said the command log is "written only when a diagnostic/debug save is requested"**, directly contradicting the persisted-and-replay-tested requirement. | **Fixed.** S-01 now states the log is persisted for **every** save, names `command_log.jsonl` and `command_log_sha256`, and records the old wording as superseded. |
| **R-4** | **`ARCHITECTURE.md` §4.2's `ISystem.Register` still took only `IWorldState`**, contradicting §5's `IWorldStateWriter` split added by the same review — the code sample handed every system the writer contract's whole point was to withhold. `SYSTEMS.md` S-03 likewise still advertised `Write<T>` on `IWorldState`. | **Fixed.** The signature is `Register(ICommandBus, IEventBus, IWorldState, IWorldStateWriter)`; S-03 documents the split and marks the old wording superseded. |
| **R-5** | **`ROADMAP.md` M3 required a *playable* 2×2 km streaming build as its proof artifact**, while `PROTOTYPE.md` — the Phase-1 scope authority — defines the playable area as 200 m over 4 cells and lists streaming as an explicit non-goal. | **Fixed.** The 2×2 km frame-budget experiment is preserved as an **isolated greybox `RISK SPIKE`** (exactly as `RK-02` specifies), and the playable deliverable is the prototype's 200 m / 4-cell area. Two artifacts, not one; M3's "Work" and exit criteria now say so. |
| **R-6** | **`DATA_MODEL.md`'s closed kind table did not cover six directories its own tree declared** (`regions/`, `flags/`, `facts/`, `species/`, `schedules/`, `sets/`), plus `anchor` — so the validator rule "fails the build on an unknown top-level content kind" would reject every file the document declared legal. Two of these were recorded as "assumptions" while being hard requirements for reference resolution. | **Fixed, not papered over.** The seven missing kinds are now table rows with schemas, prefixes, owning directories, and minimum required content. `flags/` → `world_flags/` to match the `world.` ID prefix actually used. The underscore-file exclusion (`_tags.yaml`, `_aliases.yaml`) is now explicit, and the directory↔kind **bijection** is stated as the property that makes the validator implementable. §5's resolver table and §6's version axes are extended to match. |
| **R-7** | **`DATA_MODEL.md` §6 still named a `save_version` axis**, the exact field `B-5` recorded as removed, and documented the old `content/flags/` path. | **Fixed.** The axis is `schema_version`, `save_format` is named as a distinct field with a distinct failure mode, and a fourth axis (`worldgen_digest`) is added. The reconciliation is recorded in place. |
| **R-8** | **Repeated constants drifted**: the game-day arithmetic appeared in two places, and `WORLD_ARCHITECTURE.md` cited `PERSISTENCE.md §11 RK-02` — a project-register ID used for a document-local risk, on a line that also stated the pre-rebase dirty-flag model. | **Fixed.** The citation is `RK-P02`, and the line now states the hint-versus-derived-set distinction. All repeated constants verified consistent. |

**New local risks opened and closed during this pass** (`PERSISTENCE.md` §11.1, `RK-P09`…`RK-P13`): delta growth without rebase, entity/baseline identity collision, RNG draw-order sensitivity, content tuning silently reinterpreting persisted derived values, and the unbudgeted scoped-save side effect. The first four are addressed by mechanism (`I-7`, `I-8`, `I-9`, and the shape/tuning split); `RK-P13` remains open and is recorded as such. `RK-A8`'s promotion mapping now points at `RK-15`, since the "diverged-actor rule" it deferred to is now the slot-key merge rule.

---

## C. Findings accepted as-is

- **`DATA_MODEL.md` is 611 lines**, over the 250–450 target set for the document set. The subagent flagged this itself: 14 mandated schemas each needing a schema plus a concrete example, plus 7 required prose sections, does not compress below ~600 without deleting the examples the brief requires. Accepted; the document notes its examples graduate into `content/` in Phase 1.
- **`README.md` dangling reference** — the only unresolved `.md` target, and it is explicitly annotated as a Phase-1 exit criterion rather than a Phase 0 artifact.
- **`PROTOTYPE.md` A-6 / `VERTICAL_SLICE.md` VS-7** list some sibling documents as "landed during this writing pass" while omitting `GAMEPLAY_LOOPS.md`, `DATA_MODEL.md`, `DECISIONS.md` and `INDEX.md`. Cosmetically stale, factually harmless.

---

## C-bis. Withdrawn

This section previously listed seven open findings from the third adversarial pass. **All seven are now resolved — see §B-bis above**, which carries each finding, its severity, and its resolution. It is retained as a heading only so that a reader following an older reference to "C-bis" lands somewhere that tells them where the content went.

Its closing recommendation — *"(1) collapse the load/integrity contract into one numbered normative sequence and fix the boot order; (2) make ownership and determinism enforced rather than declared, with both tests scheduled before the code they constrain; (3) pick a single phase-membership authority and re-cut the roadmap to match it"* — is the specification for what §B-bis records as done.

---

## D. Reviews on disk

Full text of the three independent passes, retained for provenance:

| File | Scope |
|---|---|
| `reviews/ARCHITECTURE_AI_SESSION_REVIEW.md` | Architecture, simulation tiers, AI-session hazards |
| `reviews/PERSISTENCE_ADVERSARIAL_REVIEW.md` | Delta/baseline identity, dirty-vs-divergence, RNG model, Windows commit |
| `reviews/PROGRESSION_SCOPE_ECONOMY_REVIEW.md` | XP arithmetic, axis duplication, death penalty, currency faucet, slice scope |

---

## E. What the review did **not** do

- It did not re-derive balance numbers other than the XP curve.
- It did not evaluate whether the game will be *fun* — that is `VERTICAL_SLICE.md`'s playtest protocol, by design.
- It did not audit `ARCHITECTURE.md` or `DECISIONS.md` adversarially in depth; both were read in full and quote-checked, and no defect was found in either beyond B-4's tree, but they did not receive the same treatment as the other documents.
- Every finding above is traceable to a quoted line in the audited documents. Line numbers drift as documents are edited; findings are anchored to quoted text.
