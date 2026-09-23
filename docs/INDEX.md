# UNNAMED — Phase 0 Document Index

**Project:** Otherreach (codename UNNAMED) — an original full-body third-person (with seamless first-person zoom), solo-first, open-world fantasy RPG
**Phase:** 1 — Playable Prototype. M0, M1, M1b, M2, M2b and M2c are complete; M3 is implemented and waits on its RAZER performance measurement (`M3_STATUS.md`); M3b, M3c and M3d are complete (`M3B_STATUS.md`, `M3C_STATUS.md`, `M3D_STATUS.md`, `ROADMAP.md`). The documents below are the Phase 0 architecture set; implementation is underway in `src/` and `tests/`.
**Root:** `G:\UNNAMED`

## Authoritative documents (read in this order)

| Order | Document | Authority | Purpose |
|---|---|---|---|
| 1 | `PROJECT_CHARTER.md` | **Creative vision — highest authority** | The game we are building and the pillars that may not be altered to make implementation easier |
| 2 | `PHASE_0.md` | **Phase instructions** | What Phase 0 must produce and what it must not do |
| 3 | `DECISIONS.md` | **Technical authority** | Settled architectural decisions `D-01`…`D-12`, each with alternatives, consequences, and the condition that would justify revisiting it |
| 4 | `ARCHITECTURE.md` | Runtime design | Layers, repository structure, command/event model, state ownership, boot sequence |

**Conflict resolution:** the hierarchy is `PHASE_0_COMPLETE.md` §1, restated with the owner's current rulings in `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`. Charter beats decisions; decisions beat everything else; each specialist document wins for its own topic; `PROTOTYPE.md` governs what is in Phase 1 and `ROADMAP.md` governs when. If `DECISIONS.md` and `ARCHITECTURE.md` disagree, `DECISIONS.md` is right and `ARCHITECTURE.md` has a bug.

## Phase 0 deliverables

Line counts are approximate and drift as documents are revised; they are recorded only to indicate depth.

| # | Document | Lines | Covers |
|---|---|---|---|
| 1 | `ARCHITECTURE.md` | ~250 | Layer model, repo layout, commands/events, state ownership, boot sequence, testing strategy |
| 2 | `GAMEPLAY_LOOPS.md` | The interconnected loops, reward entry/exit points, circular-economy exploits, moment/session/long-term loops |
| 3 | `SYSTEMS.md` | Per-system responsibility, authoritative state, dependencies, persistent vs transient data, exposed events |
| 4 | `DATA_MODEL.md` | Content schemas (item, weapon, armor, creature, NPC, spell, ability, status effect, recipe, resource, quest, dialogue, faction, loot table); definition vs instance data |
| 5 | `PERSISTENCE.md` | Save layout, sparse world delta, versioning, migrations, corruption recovery, autosaves, mandatory tests |
| 6 | `WORLD_ARCHITECTURE.md` | Regions, cells, four simulation tiers, spawn budgets, NPC schedules, dungeons, player buildings |
| 7 | `PROGRESSION.md` | The orthogonal progression axes, XP policy, hybrid builds, mastery, endgame, rejected axes |
| 8 | `PROTOTYPE.md` | Phase 1 minimum scope, success criteria, what it deliberately excludes |
| 9 | `VERTICAL_SLICE.md` | Phase 2 scope, the epic quest graph, boss, playtest protocol, exit criteria |
| 10 | `ROADMAP.md` | Milestones, dependency order, critical path, gates, risk-reduction spikes |
| 11 | `RISK_REGISTER.md` | `RK-01`…`RK-16` with likelihood, impact, earliest inexpensive validation, mitigation; ends with **READY FOR IMPLEMENTATION** |
| 12 | `DECISIONS.md` | ADRs `D-01`…`D-12`, including the four open owner questions |
| — | `REVIEW.md` | **Not a deliverable** — audit trail. Three adversarial passes, 26 defects, and what was done about each. Currently reports **no open defects** |
| — | `reviews/` | Full text of the three independent adversarial passes, retained for provenance |

## The twelve settled decisions at a glance

| ID | Decision |
|---|---|
| `D-01` | Engine: **Godot 4.x with C#** — chosen for text/diffable/mergeable formats and headless testability under AI-assisted development |
| `D-02` | **Server-shaped single player**: authoritative state in engine-agnostic C#, mutations only via commands |
| `D-03` | **Content is YAML data**, validated at load; nothing hardcoded per-quest or per-item |
| `D-04` | **Identity**: dotted definition IDs (immutable) + ULID instance IDs (runtime-assigned) |
| `D-05` | **Persistence**: sparse deltas over a deterministic baseline; unchanged world costs zero bytes |
| `D-06` | **Simulation tiers** A/B/C/D plus a global tick; transitions reconcile rather than adopt |
| `D-07` | **Quests**: declarative objectives as predicates over world state; a closed type set, not a scripting language |
| `D-08` | **Building**: socket/snap assembly, explicitly not a structural simulator |
| `D-09` | **Progression**: only axes that answer a different question; no axis converts into another |
| `D-10` | **Entity Registry** owns identity and lifetime only — never gameplay rules |
| `D-11` | **Presentation never mutates state**; it submits commands and observes events |
| `D-12` | **Scope discipline**: region by region, no MMO infrastructure, working single-player wins every tie |

## Phase-1 execution and orientation

| Document | Role |
|---|---|
| `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` | The authority order restated with the owner's current rulings (progression, Phase-1 scope, performance gate, roles). Read it before any Phase-1 work |
| `PROGRESSION_AXIS_RECONCILIATION.md` | The M2c progression-axis audit (2026-09-23): the ratified axes, keep/merge/remove decisions, independence tests, rejected alternatives. `PROGRESSION.md` carries the result |
| `CLAUDE_PHASE1_EXECUTION_PROMPT.md` | The owner-approved execution brief for the Phase-1 run (M2c through M6). Orientation, not design authority |
| `OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md` | Snapshot for a fresh design or implementation session. Orientation, not design authority |
| `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md` | The owner-approved M2b refinement, now folded into `PERSISTENCE.md` |
| `M1_STATUS.md`, `M2_STATUS.md`, `M2B_STATUS.md`, `M2C_STATUS.md` | Milestone evidence |
| `POST_M2_DOCUMENTATION_RECONCILIATION.md` | **Superseded — historical record only.** Its open items are carried in the precedence document §11 |

## Design-extension documents

Owner-approved design direction. **Directional/future unless their milestone owns them**: a milestone that reaches one reconciles it into the normative documents first, and nothing here overrides a normative document before that. `00_DESIGN_PACK_INDEX.md` and `00_BATCH_2_INDEX.md` introduce the two design packs.

| Document | Owning milestone or phase |
|---|---|
| `CAMERA_PERSPECTIVE_AND_PRESENTATION.md` | M3 |
| `HUD_INPUT_AND_ACTIONS.md` | M3 onward |
| `MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md` | M3 (map data); the knowledge model later |
| `ENGINE_VALIDATION.md` | M3 performance gate; its dressed scene is later |
| `SKILLS_AND_DISCIPLINES.md` | M2c progression audit, M3f |
| `CHARACTER_CREATION_AND_LINEAGE.md`, `RACES.md`, `RACES_UPDATE_NOTES.md` | M2c audit; Phase 1 has one playable race |
| `INVENTORY_STORAGE_AND_LOGISTICS.md` | M3b |
| `CRAFTING_AND_ITEMIZATION.md` | M3b (items), M3f (two-recipe proof); the full grammar later |
| `COMBAT_DAMAGE_ARMOR_AND_DEATH.md` | M3c |
| `STEALTH_DETECTION_AND_THREAT.md` | M3d |
| `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md` | M3e (three tiny domains in Phase 1) |
| `NPC_SIMULATION.md` | M4 (a small, fully simulated population in Phase 1); the rest is Phase 2+ |
| `SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md` | M4: structured dialogue and minimal continuity only; all deeper social systems are future |
| `QUESTS_DUNGEONS_WORLD_EVENTS_AND_REPOPULATION.md` | M5 (quest framework); dungeons M8 |
| `COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md`, `COMPANION_MATRIX.md` | M6 (one companion); player + 3 later |
| `CRIME_LAW_REPUTATION_AND_JUSTICE.md` | M7 (factions, reputation); crime is Phase 3 |
| `WORLD_BUILDING_AND_PROPERTY_DESIGN.md` | M7 (Building v1) |
| `ECONOMY.md` | M10 regional economy (M3b has only a currency stub) |
| `WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT.md` | Phase 2+ |
| `PETS_ANIMALS_AND_ECOLOGY.md` | Phase 2+ |
| `TRAVEL_AND_TRAVERSAL.md` | Phase 3 (mounts, boats, caravans) |
| `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md`, `ENDGAME_QUESTLINES.md` | M12/M13 and later |
| `SOULS_DEATH_REINCARNATION_AND_LEGACY.md` | Future |
| `MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md` | Seams only; M15 validates the cooperative seam |
| `AI_NARRATIVE_SERVICE.md` | Future, optional |
| `OTHERREACH_COSMOLOGY.md`, `MYTHOLOGY.md`, `STONE_QUESTION.md`, `WORLD_MATERIALS.md` | Setting and content direction |
| `FEATURE_DELIVERY_STAGING.md` | Planning guidance; `ROADMAP.md` schedules |
| `ASSET_GENERATION_PLAN.md`, `AUDIO_DESIGN.md`, `AUDIO_PIPELINE.md`, `QUALITY_TIERS.md`, `WAVE_0_*.md` | Asset and audio track (asset pipeline owned by DeepSeek) |

## Status

- **Phase 0: complete and mechanically reconciled.** All twelve deliverables exist. Three adversarial passes raised 26 defects; all are fixed or ruled. A **fourth propagation pass** then verified each fix *in the authoritative document* and found 8 further defects caused by the earlier passes — including a **truncated `PERSISTENCE.md`** that had lost §6–§11, and **two documents claiming authority over the load sequence** with different orders. All 8 are corrected. See `REVIEW.md` §B-ter.
- **Start here for implementation:** **`PHASE_0_COMPLETE.md`** — hierarchy, engine, `D-01`…`D-12`, risks, owner defaults, starting milestone, and reading order.
- **Risk register:** `RK-01`..`RK-16`. `RK-01`..`RK-10` are the ten `PHASE_0.md` STEP 18 requires; `RK-11`..`RK-14` were promoted from document-local tables; `RK-15` and `RK-16` cover enforcement gaps (cross-slice mutation, read-path correctness). `RISK_REGISTER.md` is the single risk authority, and `PERSISTENCE.md` now carries `RK-P01`..`RK-P14`.
- **Single-authority map:** `PERSISTENCE.md` §7.4 owns the load sequence (not `ARCHITECTURE.md` §8.2); `ARCHITECTURE.md` §3 owns repository structure; `DATA_MODEL.md` §1 owns the content kind list; `PROTOTYPE.md` owns Phase-1 scope; `ROADMAP.md` owns scheduling.
- **Phase 1 M1 complete.** Domain skeleton, command/event bus, and headless test harness implemented. See `M1_STATUS.md` for full verification evidence.
- **Open owner input:** `DECISIONS.md` §"Open questions for the project owner" — setting tone, target platform baseline, visual fidelity target, and the "while you were away" offline catch-up policy. The title is answered: **Otherreach**. The owner's Phase-1 performance ruling (RAZER RTX 4070 Ti, 1080p, sustained 60 FPS) is recorded in `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` §4 and is written into `DECISIONS.md` before M3. None of the remaining items blocks Phase 1.
- **Phase 0 was documentation-only, by instruction.** Implementation began in Phase 1: source is in `src/`, headless tests in `tests/`.
- **M1 implementation status:** `docs/M1_STATUS.md` — complete with objective evidence for all exit criteria.
- **Phase 1 M2 complete.** Entity registry and D-04 identity, deterministic cell baseline (`RK-01` measured), and sparse-delta saves with atomic write, quarantine, backup rotation and crash recovery. `docs/M2_STATUS.md` has the evidence per exit criterion, the acceptance audit, and the deferrals.
- **Phase 1 M2c complete.** The progression spine on the audited model (`PROGRESSION_AXIS_RECONCILIATION.md`): level, attributes, skills and techniques as pure domain rules driven by content config, the anti-farm guards, the typed non-conversion API, independence tests for every neighboring pair of axes, and save schema 4 with its historical fixture. Evidence: `docs/M2C_STATUS.md`.
- **Phase 1 M2b complete.** Ordered schema migrations (v1 -> v2 -> v3) with committed historical fixtures, definition-ID rename/removal maps, and `save:migrate --dry-run`. It also adds baseline compatibility: content identity no longer seeds generation, randomness is addressed by semantic key, every changed cell proves the baseline it was made against, and generator drift is detected. Specification: `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md`; evidence: `docs/M2B_STATUS.md`.

## Where to start reading

0. **`IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`** — the authority order and the owner's current rulings. Read this before any Phase-1 work.
1. **`PHASE_0_COMPLETE.md`** — the implementation handoff. Read this first if you are about to write code.
2. `PROJECT_CHARTER.md` — the vision, and the only document that outranks the rest.
3. `DECISIONS.md` — `D-01`..`D-12`, the settled technical spine. Everything else cites these by ID.
4. `ARCHITECTURE.md` — how the pieces fit, including the enforced boundaries.
5. `PERSISTENCE.md` — read before writing any system; every system is written against the save boundary from its first commit.
6. `RISK_REGISTER.md` → **READY FOR IMPLEMENTATION** — engine, first milestone, first ten tasks, owner questions.
7. `REVIEW.md` — what was wrong, what was done about it, and what was consciously accepted.

**Do not read `reviews/` for design guidance** — it is provenance about an earlier state of these documents, deliberately unaltered, and contains claims later verification refuted.
