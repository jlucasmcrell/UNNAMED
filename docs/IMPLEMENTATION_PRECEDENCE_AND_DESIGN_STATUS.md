# OTHERREACH — Implementation Precedence & Design Status

**Revision 2 — 2026-09-23.** Revised after review, with the owner's rulings of 2026-09-23 incorporated.

**Purpose:** Prevent coding agents from treating every design document as an instruction to implement everything immediately.

---

# 1. Authority Order

The authority hierarchy is `PHASE_0_COMPLETE.md` §1. This document restates it and does not replace it.

1. `PROJECT_CHARTER.md` — creative intent. Always wins.
2. `DECISIONS.md` — settled technical decisions `D-01`…`D-12`.
3. Specialist documents, each authoritative for its own topic:
   - `ARCHITECTURE.md` — layers and repository structure;
   - `DATA_MODEL.md` — content kinds, identity grammar and validation;
   - `PERSISTENCE.md` — save format and write/load sequence;
   - `WORLD_ARCHITECTURE.md` — addressing, cells and tiers;
   - `SYSTEMS.md` — system ownership;
   - `PROGRESSION.md` — axes and XP.
4. `PROTOTYPE.md` (**what** is in Phase 1), `ROADMAP.md` (**when**), `VERTICAL_SLICE.md` and `GAMEPLAY_LOOPS.md`.
5. `RISK_REGISTER.md` — the single risk authority.

**Phase-scope rule (owner ruling).** Where a ROADMAP milestone's work items or exit criteria ask for more than `PROTOTYPE.md`'s scope, PROTOTYPE wins, and the ROADMAP text is reconciled at that milestone.

Below all of the above:

- **Owner-approved design-extension documents** give direction for the milestone that eventually owns them.
- **Orientation, not authority:** this document's status sections, `CLAUDE_PHASE1_EXECUTION_PROMPT.md`, the Master Handoff and `INDEX.md`.
- **Provenance only:** `REVIEW.md` and `reviews/`, and superseded documents such as `POST_M2_DOCUMENTATION_RECONCILIATION.md`.

**Owner rulings** recorded here (§3, §4, §9) are owner decisions. The owning milestone writes each one into the normative documents. Until it does, the ruling takes precedence over the conflicting text it names.

When a future design document conflicts with current normative architecture:

> **Do not silently implement the future document. Reconcile the conflict at the owning milestone.**

---

# 2. Completed / Normative Now

Completed:

- M0
- M1
- M1b
- M2
- M2b

The test baseline is 240 (`M2_STATUS.md`, `M2B_STATUS.md`).

Current proven persistence architecture includes:

- canonical DefinitionId contract;
- prefixed ULID instance IDs;
- deterministic baseline generation;
- semantic call-order-isolated RNG;
- sparse world deltas;
- per-cell baseline hashes;
- worldgen fingerprint/probes;
- ordered save migrations;
- historical fixtures;
- crash-safe save/migration commits.

Do not regress these.

---

# 3. Hard Gate Before M2c

## Progression-axis reconciliation

**Status: resolved 2026-09-23.** The audit ratified the model in `PROGRESSION_AXIS_RECONCILIATION.md`, and `PROGRESSION.md`, D-09, `DATA_MODEL.md`, `SYSTEMS.md`, `ROADMAP.md` (M2c), `PROTOTYPE.md`, `PERSISTENCE.md` and `GAMEPLAY_LOOPS.md` were reconciled to it. The record below is kept for traceability.

Do not implement M2c directly from the current `PROGRESSION.md` without a fresh audit (`CLAUDE_PHASE1_EXECUTION_PROMPT.md` §5).

**Owner rulings (apply; do not re-open):**

- Techniques and formulas are learned in the world: through teachers, books, quests, study, research, experimentation, discovery/first success, artifacts and cultural training.
- Starting archetypes grant a small starting technique package.
- Level-ups do not buy techniques from a giant ability tree.
- Level-up advancement goes to attributes. Skills/disciplines improve through the ratified use/challenge model, so levels grant no skill points.
- The generic ability-point pool is removed unless the audit establishes a genuinely separate need for it.

Known issues the audit must resolve:

1. **Weapon competence has two homes.** `PROGRESSION.md`'s skill list has no weapon-family skill. But `SKILLS_AND_DISCIPLINES.md` lists weapon families as skills, and `AX-WM` covers the same competence. The old 2×2 proof ("high sword skill / low mastery") is therefore not valid as written.
2. **Crafting competence has two homes.** `SKILLS_AND_DISCIPLINES.md`'s crafting skills duplicate the `AX-PRF` professions.
3. **Level-granted skill points vs use-based skills.** Resolved by the ruling above: remove the skill points.
4. **Mana.** Magic still assumes mana/spell-resource pools in several normative docs, while owner-approved design is Resonance/Strain.
5. **Attunement lockout.** School attunement currently hard-disables casting of entire known schools. Review it against the owner-approved philosophy: known capability remains accessible, and preparation changes convenience/strain rather than existence. Phase 1 has no level-10 attunement system.
6. **Party size.** `AX-CMP` and `PERSISTENCE.md` still say six active companions. The owner-approved normal party is player + 3.
7. **Reputation vs hostility.** `AX-REP` must remain reputation/access and must not become the source of legal or tactical hostility (see `SYSTEMS.md` S-27's `HostileToPlayerChanged`).
8. **Endgame vocabulary.** Post-cap "prestige projects" and "paragon" should reconcile with the approved mastery / Great Works / legacy model.

Every retained neighboring pair of axes gets a real independence test. Old M2c exit criteria that assume an axis which does not survive may be rewritten, with the reason recorded.

The audit may merge, rename, or restructure progression axes **before** the first M2c save schema is committed.

If two genuinely different player-experience models remain equally plausible, stop for owner review.

---

# 4. Owner-Approved Design — Implement at Owning Milestone

These are approved direction, not permission to expand current scope. Items marked **(ruling)** are owner rulings of 2026-09-23.

## Camera — M3
- full-body third-person/over-the-shoulder primary;
- seamless zoom to first person;
- perspective-neutral authority.

## Performance — M3 (ruling)
- The Phase-1 gate is the PROTOTYPE greybox scene at 1080p with a sustained 60 FPS, on RAZER's RTX 4070 Ti, measured with OBS, H3 and other significant GPU workloads stopped.
- Record CPU and GPU frame times, 1% lows, RAM/VRAM, and hitches.
- D-01's formal engine-revisit trigger is not evaluated in Phase 1. It belongs after the required streaming/LOD systems exist.
- ROADMAP M3's isolated 2×2 km greybox is still captured and recorded, but it is **not** the formal D-01 revisit gate.
- Failure to sustain 1080p / 60 FPS on a clean RAZER 4070 Ti after reasonable optimization, in either scene, is still an owner-review stop.
- The fully dressed `ENGINE_VALIDATION.md` scene is later work.

## Combat — M3c
- physical reach/armor/anatomy/condition;
- no level-based physical immunity;
- avoid HP sponges.

## Stealth/perception — M3d
- actors react only to what they perceive/infer/learn;
- no global psychic aggro.

## Magic — M3e
- Resonance/Strain;
- Focus remains the general concentration resource;
- no universal mana;
- **Phase 1 (ruling):** three tiny representative magic domains/traditions with one formula each; no level-10 attunement system;
- custom spell grammar later.

## Crafting — M3f and beyond
- compositional construction grammar;
- materials have mechanical properties;
- no bulk-dagger grind;
- **Phase 1 (ruling):** two recipes and one gather→craft proof loop; no full profession-rank ladder;
- the full open grammar arrives incrementally.

## NPCs and social — M4
- **Phase 1 (ruling):** a small NPC population, fully simulated. Tier-transition simulation is deferred to the first milestone that actually introduces simulation tiers. NPC schedules stay in the vertical slice, per `PROTOTYPE.md`.
- **Phase 1 social scope (ruling):** structured dialogue and minimal continuity only. All deeper social systems (persuasion, rumor, languages, beliefs, hostility layers) are future.

## Companions — M6 and later
- **Phase 1 (ruling):** one companion with prototype behavior; no affinity ladder or personal-quest requirement;
- the eventual normal active party target is player + 3; never encode a max-six rule;
- full relationships/romance/offscreen life later.

## Phase-1 acceptance — after M6 (ruling)
- Keep the `PROTOTYPE.md` §9 requirements: death/respawn, a recorded acceptance run, a field-by-field save/reload state comparison, and performance evidence.
- 3–5 blind testers (ROADMAP M6 early feel test) before advancing to Phase 2. (M7 reconciliation (2026-09-24): the owner ruled on 2026-09-24 that the feel test is deferred until a nontechnical Windows playtest build exists and is **not an M7 entry blocker**, `ROADMAP.md` M6. That build now exists, so the test is schedulable independently of M7.)

---

# 5. Content Validation Hardening — Before Content Scale

The implementation report notes that current content lint:

- does not validate kind-specific fields deeply;
- misses references nested inside definitions.

This predates M2.

Do not block M2c solely for it.

Before M3b/M3d expands real item/creature content, add a focused hardening gate:

- recursive/nested reference validation;
- kind-target validation for Phase-1 types;
- semantic/range checks required by the actual schemas being used;
- the reserved `<kind>.mod.*` core-namespace lint (§7).

Do not build an enormous speculative schema framework.

---

# 6. Design-Only / Do Not Implement Yet

Unless the roadmap milestone explicitly reaches them:

- Soul reincarnation;
- Soul Sheet;
- Great Works/institutions;
- full systemic crime/courts;
- advanced social simulation (anything beyond M4 structured dialogue and minimal continuity);
- seasons/survival;
- full custom spell editor;
- Otherwhen;
- cosmic Great Works;
- mod loader;
- community servers;
- networking;
- federation;
- PvP.

Preserve seams only.

---

# 7. Modding Seam — Preserve, Do Not Build

Approved now:

- reserve the `<kind>.mod.<package_namespace>.*` DefinitionId space, with kinds per the `DATA_MODEL.md` §1 kind table;
- do not use it for core IDs. A content lint enforces this, added in the pre-M3b hardening pass (§5);
- keep content data-driven;
- avoid hidden filesystem-order patch semantics;
- saves remain explicit about content identity.

No mod loader/package manager in Phase 1.

---

# 8. PvP Seam — Preserve, Do Not Build

Do not hard-code "player targets are always invalid" deep in combat.

Attack legality should eventually be a policy seam.

Single-player remains the only implemented mode.

---

# 9. Roles and Parallel Asset Work (ruling)

- **Claude** is the primary gameplay/code agent through M6. It works in its own worktree (`G:\UNNAMED_CLAUDE`, branch `claude/phase1`).
- **DeepSeek/DSH** owns asset generation, rigging, animation and asset-pipeline tooling during this run, and nothing else.
- **Qwen** is paused from gameplay implementation.
- **Pushes and merges.** Claude may push branches and maintain draft PRs into `main`. The draft PR is what triggers the existing Linux CI; CI triggers are not changed. Claude never merges to `main`; the owner merges at milestone gates.
- **DeepSeek-owned paths** are never staged, edited or committed by gameplay agents. Read them for integration context only until DeepSeek hands them off:
  - `tools/asset_pipeline/**` and `tools/godot_validate/**`;
  - `docs/WAVE_0_*.md` and `docs/ANIMATION_*.md`;
  - `docs/CANONICAL_BODY_AND_SKELETON.md` and `docs/ASTRAL_HOST.md`;
  - `assets/`.

---

# 10. Current Asset Status

As reported in DeepSeek's Wave-0 documents on 2026-09-23 (not independently re-verified):

- 15/15 Wave-0 proof assets built and Godot-validated;
- 324/324 pack verification clean;
- canonical body/skeleton v1 built for four fit families;
- animation metadata schema/validator built;
- animation retarget/motion tooling is actively in progress;
- armor Method C fit is proven; generated-style → canonical-fit transfer remains future pipeline work.

Do not block M3 movement on polished final animation.

Use the best available proof rig/animation or graybox presentation.

---

# 11. Documentation Hygiene

Reconcile before the Phase-1 work that depends on it:

- **First-person-only language:** before M3 (execution brief §7).
- **Mana / spell-resource language:** in M2c and M3e.
- **Title:** the "UNNAMED (working title)" headers and `DECISIONS.md` open question 1, at the documentation intake. The title is **Otherreach**; the codename remains `UNNAMED`.
- **`INDEX.md`:** it omits most design-extension documents; fix at the intake.
- **Master Handoff:** replaced by the clean `OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md`. V1, V2 and `PRE_CLAUDE_RECONCILIATION_REVIEW.md` are never committed, because the repository is public. They are kept only as local historical artifacts outside the repository (`G:\UNNAMED_HISTORY\2026-09-23\`).
- **`RACES_UPDATED.md`:** duplicate of the canonical `RACES.md`; remove at the intake.
- **Never commit:** backup and zipped design artifacts, and the stale root `PROJECT_CHARTER.md`.

`POST_M2_DOCUMENTATION_RECONCILIATION.md` is superseded by this document. It stays in the repository as a historical record under a SUPERSEDED banner. Its items are carried forward here:

- **Done:**
  - README (its §2) is done;
  - RACES (§12) is done: `RACES.md` is the updated version.
- **At the documentation intake:**
  - INDEX (§3);
  - promotion of the reviewed design documents (§13).
- **Open, for an owner-reviewed documentation pass (not a Phase-1 blocker):** charter small amendments (§4): title, the Other cosmology as long-term identity, NPCs as world participants, knowledge-driven exploration, flexible systemic crafting, and the optional future community-server path.
- **At their owning milestones:**
  - future WORLD_ARCHITECTURE, SYSTEMS, DATA_MODEL and RISK_REGISTER expansions (§5, §6, §8, §10);
  - existing crafting/inventory schemas (§11), which evolve incrementally at M3b and M3f.
  - Do not pull any of these into Phase 1 unless `PROTOTYPE.md` requires it.
- **Covered elsewhere:**
  - the engine-validation spike (§7) is covered by M3's performance gate, with the dressed `ENGINE_VALIDATION.md` scene later;
  - the progression questions (§9) are answered by the M2c audit and the §3 rulings;
  - Git discipline (§14) and the rule for coding agents (§15) continue in §1 and §12 here.

Keep review files as provenance, not normative guidance.

---

# 12. Milestone Discipline

For every milestone:

```text
inspect
→ reconcile owning docs
→ implement
→ build
→ automated tests
→ runtime proof
→ update status/docs
→ commit
→ verify exit criteria
→ proceed
```

If a gate fails, fix it before proceeding.

---

# 13. Stop Point

Claude is authorized through M6 and must stop after:

> **M6 — Phase-1 playable prototype**

Then the owner plays/reviews the game before M7. The Phase-1 acceptance requirements (§4) remain in force, including the 3–5 blind-tester feel test before Phase 2 (M7 reconciliation (2026-09-24): not an M7 entry blocker, per the owner's 2026-09-24 ruling; the owner authorized M7 on 2026-09-25).

Do not automatically continue into the Phase-2 vertical slice.
