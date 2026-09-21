# PHASE_0_COMPLETE.md — Implementation Handoff

**Project:** UNNAMED (working title) — first-person, solo-first, open-world fantasy RPG
**Purpose:** The single entry point for an implementation session. Everything below is a pointer or a summary; the normative detail lives in the documents listed in §1.

---

## 1. Authoritative document hierarchy

Authority flows downward. A document lower in this list never overrides one above it; where two documents at the **same** level conflict, the specialized document wins for its own topic and the other is wrong.

| Rank | Document | Owns |
|---|---|---|
| 1 | `PROJECT_CHARTER.md` | Creative intent, vision, non-negotiables. **Always wins.** |
| 2 | `DECISIONS.md` | Technical intent. `D-01`…`D-12` are settled; everything else cites them by ID |
| 3 | `ARCHITECTURE.md` | Layer model, **repository structure**, core runtime patterns, state ownership and its enforcement, threading, boot sequence (§8.1), dependency direction, testing strategy |
| 3 | `DATA_MODEL.md` | **Content kind list**, identity grammar, definition schemas, cross-reference/validation rules, save-version sensitivity, `_aliases.yaml` format |
| 3 | `PERSISTENCE.md` | **Save container, manifest, sections, write sequence (§7.1), load sequence (§7.4)**, corruption recovery, autosave, size budgets, mandatory persistence tests |
| 3 | `WORLD_ARCHITECTURE.md` | Coordinate/addressing scheme, cell model, the four simulation tiers, tier transitions, interiors, streaming/LOD, performance budgets |
| 3 | `SYSTEMS.md` | The 38 systems, their owned slices, required execution order, phase mapping |
| 3 | `PROGRESSION.md` | Progression axes, XP math, death penalty, equipment/companion pacing |
| 4 | `PROTOTYPE.md` | **What is in Phase 1.** Scope table, success criteria, non-goals |
| 4 | `ROADMAP.md` | **When** work happens: milestones `M0`…`M16`, dependency graph, critical path, gates |
| 4 | `VERTICAL_SLICE.md` | The Phase-2 region and its content budget |
| 4 | `GAMEPLAY_LOOPS.md` | Loop/drain analysis, exploit guards `E-1`…`E-10`, telemetry |
| 5 | `RISK_REGISTER.md` | **Single risk authority** (`RK-01`…`RK-16`), owner questions, and the **READY FOR IMPLEMENTATION** section |
| — | `INDEX.md` | Reading order and status |
| — | `REVIEW.md`, `reviews/` | **Evidence, not architecture.** Audit trail only |
| — | `PHASE_0.md` | The Phase-0 brief that commissioned the above |

**Conflict rule for phase scope:** `ROADMAP.md` governs *when*; `PROTOTYPE.md` governs *what is in Phase 1*.

---

## 2. Accepted engine and architecture

**Engine: Godot 4.x with C#** (`D-01`). Chosen for text-based, diffable, mergeable project formats and headless testability under AI-assisted development. `D-01` carries its own revisit triggers — see §4 `RK-02`.

**Architecture: server-shaped single player** (`D-02`). Authoritative state lives in engine-agnostic C#; nothing mutates state except a command; presentation submits commands and observes events (`D-11`).

**Three projects** (`ARCHITECTURE.md` §3 — the structure authority):

```
UNNAMED.sln
├─ src/
│  ├─ Domain/          ← NO Godot reference
│  ├─ Application/     ← NO Godot reference; bus, scheduling, save orchestration
│  └─ Presentation/    ← the Godot project
├─ tests/
│  ├─ Domain.Tests/
│  └─ Content.Tests/
├─ tools/
│  ├─ content-lint/
│  └─ quest-debug/
└─ content/            ← outside the engine project; content is not a Godot resource
```

**The load-bearing invariants:**

- **Presentation may never mutate domain state.** Enforced by project reference, not convention.
- **A system may mutate only its own slice.** `IWorldState` is a public reader; `IWorldStateWriter` is `internal` to `Domain`, so `Application` and `Presentation` cannot name it. A write to a component key no system declared ownership of is a **startup error**. The residual (one `Domain` system writing another's component) is review-only and is risk `RK-15`.
- **Determinism runs on one fixed 20 Hz domain tick.** The domain tick is the only place lazy state evolves; no system mutates state from a callback outside `Tick`.
- **The headless domain is the product.** Progression math, persistence, generation and validation must be testable with no engine present.

---

## 3. Settled decisions `D-01` … `D-12`

| ID | Decision |
|---|---|
| `D-01` | **Engine: Godot 4.x with C#** — text/diffable/mergeable formats and headless testability under AI-assisted development |
| `D-02` | **Server-shaped single player**: authoritative state in engine-agnostic C#; mutations only via commands |
| `D-03` | **Content is data, not code**: YAML definitions validated against schemas; adding content touches zero C# files |
| `D-04` | **Identity**: ULID instance IDs, dotted definition IDs; definition IDs are a public API |
| `D-05` | **Persistence**: sparse deltas over a deterministic baseline; the unchanged world costs zero bytes |
| `D-06` | **World simulation**: four tiers (A/B/C/D) plus one global tick |
| `D-07` | **Quests**: declarative objectives over a closed type set, evaluated against world state |
| `D-08` | **Building**: socket/snap assembly, explicitly *not* a structural simulator |
| `D-09` | **Progression**: orthogonal axes answering distinct questions; no axis converts into another |
| `D-10` | **Entity Registry** owns identity and lifetime only — never gameplay rules |
| `D-11` | **Presentation never mutates state**; it submits commands and observes events |
| `D-12` | **Scope discipline**: region by region, no MMO infrastructure; working single-player wins every tie |

---

## 4. Known risks `RK-01` … `RK-16`

`RISK_REGISTER.md` is the authority; each entry carries likelihood, impact, an owner, the earliest inexpensive validation, and a mitigation. The two added by adversarial review are marked **†**.

| ID | Risk |
|---|---|
| `RK-01` | World-generation determinism breaks, so sparse-delta saves corrupt (`D-05`) |
| `RK-02` | Godot cannot hold the first-person frame budget in a 2×2 km slice (`D-01`) |
| `RK-03` | Definition IDs are a public API and get renamed/deleted after saves exist (`D-04`) |
| `RK-04` | The closed objective-type set cannot express real quests, so per-quest code returns (`D-07`) |
| `RK-05` | Companion AI is unreliable, or powerful enough to trivialize combat |
| `RK-06` | Player-built structures fail to round-trip, or become un-navigable |
| `RK-07` | Tier transitions produce visible NPC discontinuity (`D-06`) |
| `RK-08` | The content pipeline cannot author at target scale |
| `RK-09` | Local-player assumptions silently destroy the server-shaped seam (`D-02`, `D-11`) |
| `RK-10` | Scope too large for a small AI-assisted effort |
| `RK-11` | Dirty-flag completeness: a missed flag loses world changes while the save still validates (`D-05`) |
| `RK-12` | Offline catch-up: world time advances across a save boundary and abstract tiers must converge (`D-06`) |
| `RK-13` | Atomic save commit is untested on the target OS (Windows has no POSIX rename-over-directory) |
| `RK-14` | Navmesh stitching at cell seams under player-built structures (`D-08`) |
| `RK-15` **†** | Cross-slice state mutation inside the domain erodes the authority boundary (`D-02`, `D-10`) |
| `RK-16` **†** | The load path was specified three incompatible ways, and read-path correctness was unowned (`D-05`) |

**Document-local risks** are retained in `PERSISTENCE.md` §11.1 (`RK-P01`…`RK-P13`) and `WORLD_ARCHITECTURE.md` §13 (`RK-A1`…`RK-A9`), each naming its project-level ID where one exists. `RISK_REGISTER.md` records which were promoted and why the rest were not.

**Top risks requiring owner awareness:** `RK-02` (the only sanctioned trigger to reopen `D-01`, and it requires a *measurement*), `RK-10` (scope), `RK-16` (the load path — now specified once, in `PERSISTENCE.md` §7.4).

---

## 5. Deferred owner questions and their defaults

Four questions remain open. **All four have defaults that let implementation proceed unanswered**; none blocks Milestone 1. `DECISIONS.md` is the authority.

| # | Question | Default if unanswered |
|---|---|---|
| 1 | Working title and setting temperature (grim-dark / high fantasy / weird-ancient / bronze-age mythic) | Melancholic, ancient, low-magic-feeling high fantasy tuned for "the world is the primary character" |
| 2 | Target platform baseline | Windows desktop, 16 GB RAM, mid-range discrete GPU; no VR or console in Phase 0–2 |
| 3 | Visual fidelity target | "Readable and atmospheric" rather than AAA |
| 4 | The "while you were away" policy (`RK-12`) | Honour a bounded catch-up window, clamp abstract values to authored `[min, max]`, present no absence summary in Phase 1–2 |

**Only #2 has a soft dependency:** the `RK-02` frame-budget spike cannot be judged pass/fail without a stated baseline, and `RK-13`'s atomic-commit test must run on that platform. Everything else is fully reversible while content volume is near zero.

---

## 6. Canonical Phase-1 starting milestone

**Milestone 1 — "the seam is real and the world is reproducible."**

A headless, engine-free C# solution that can: load content from YAML with validation errors carrying file and line; create a player entity through the registry so it has a ULID from birth; generate a fixed region deterministically from a seed and reproduce a stable digest of it; award XP and apply a level-up through a command; place one item into the player's inventory and move it to a container; write a sparse-delta save and load it back to an identical domain state.

**Explicitly not in Milestone 1:** rendering, art, animation, dialogue, quests, combat feel, companions, building, or anything a player would call "the game." Its exit artifact is a **green test run, not a screenshot.**

**First task:** repository and build scaffolding (`ROADMAP.md` `M0`) — the three projects in §2, `dotnet test` green on an empty suite in CI.

**Then follow `RISK_REGISTER.md` → "First 10 development tasks, in dependency order."** That list is the risk-focused view of the critical path; `ROADMAP.md` §8 is the scheduling authority, and the register documents its deliberate differences from it.

**Phase-1 scope** is the 200 m × 200 m, 4-cell prototype in `PROTOTYPE.md` §3. The 2×2 km frame-budget work is an **isolated greybox risk spike** (`RK-02`, `ROADMAP.md` M3), not a playable deliverable — the playable 2×2 km region is the Phase-2 vertical slice.

---

## 7. Mechanical reconciliation statement

**The document set was mechanically reconciled after adversarial review, and this statement is the record of that fact.**

Three independent adversarial passes plus a fourth propagation pass reviewed the Phase 0 set. **26 findings were raised across the first three passes; all are fixed or ruled.** The fourth pass then verified — in the authoritative documents, not from the review's claims — that every fix had actually propagated, and found **8 further defects caused by the earlier passes themselves**. The most serious: `PERSISTENCE.md` had been truncated to a 154-line revision that retained §1–§5 and lost §6–§11, including the write sequence its own tests cited; and two documents simultaneously claimed authority over the load sequence and specified different orders.

All 8 were corrected. The verification performed at completion:

| Check | Result |
|---|---|
| Deliverables present | 12 / 12 |
| Dangling `.md` cross-references | none |
| Risk IDs defined / cited | 16 defined, 0 dangling citations |
| Decision IDs defined / cited | 12 defined (`D-01`…`D-12`), 0 dangling |
| System IDs defined / cited | 38 defined, 0 dangling |
| `PERSISTENCE.md` section references from other documents | all resolve |
| Obsolete wording sweeps (`save_version`, diagnostic-only command log, `content/flags/`, `src/Domain.Tests`, old two-project layout, `vigor`) | 0 live occurrences; each survives only inside an explicitly marked superseded-note |
| Implementation artifacts in the repository | 0 |

The full audit trail, including what was **consciously accepted rather than changed**, is `REVIEW.md`. Its original review files under `reviews/` are **provenance and are deliberately unaltered**; they contain claims that later verification refuted, and they are not normative.

---

## 8. Implementation-agent reading order

Read in this order. Stop at any point where a document's authority is unclear and resolve it by §1's hierarchy.

1. **`PROJECT_CHARTER.md`** — the vision, and the one document that outranks everything.
2. **`DECISIONS.md`** — `D-01`…`D-12`. Every other document cites these by ID.
3. **`ARCHITECTURE.md`** — how the pieces fit: layers, repository structure, the enforced boundaries, boot sequence.
4. **`PERSISTENCE.md`** — before writing any system. Every system must be written against the save boundary from its first commit.
5. **`DATA_MODEL.md`** §1–§2 — the content kind list and identity grammar; both are closed.
6. **`SYSTEMS.md`** §1–§2 — the execution model and the system that owns each slice.
7. **`PROTOTYPE.md`** — what Phase 1 actually builds.
8. **`RISK_REGISTER.md`** → **READY FOR IMPLEMENTATION** — engine, milestone 1, first ten tasks, and the owner questions. **This is where implementation starts.**
9. `WORLD_ARCHITECTURE.md`, `PROGRESSION.md`, `GAMEPLAY_LOOPS.md`, `VERTICAL_SLICE.md` — as the task at hand requires.

**Before writing code, read `ARCHITECTURE.md` §5 and `PERSISTENCE.md` §7.4.** The two rules most expensive to retrofit are slice ownership and the load order.

**Do not read `reviews/` for design guidance.** It is evidence about an earlier state of these documents.

---

## PHASE 0 IMPLEMENTATION HANDOFF: READY
