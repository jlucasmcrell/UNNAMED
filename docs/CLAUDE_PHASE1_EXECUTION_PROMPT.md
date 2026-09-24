# CLAUDE / OPUS — OTHERREACH PHASE-1 EXECUTION PROMPT

**Revision 2 — 2026-09-23.** Revised after review, with the owner's rulings of 2026-09-23 incorporated. This file, `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` and `OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md` in `G:\UNNAMED\docs` supersede the copies in `OTHERREACH_PRE_CLAUDE_BUNDLE_2026-09-23` (folder and zip).

You are the primary gameplay-engineering agent for **Otherreach**.

Repository codename: `UNNAMED`.

The owner wants you to continue from the current completed **M2b** state through the **Phase-1 playable prototype**, but only by passing each milestone gate in order.

Do not treat this as a greenfield project.

Do not attempt to implement the entire design bible.

Do not begin Phase 2.

This file is an execution brief, not design authority. Design authority is the hierarchy in `PHASE_0_COMPLETE.md` §1, restated together with the owner's rulings in `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`.

---

# 0. Important Current Context

M2 and M2b are complete.

Latest documented test totals:

- Architecture.Tests: 11
- Content.Tests: 34
- Domain.Tests: 4
- EntityRegistry.Tests: 23
- World.Tests: 58
- Persistence.Tests: 110
- Total: 240

M2b provides:

- v1→v2→v3 save migrations;
- historical fixtures;
- semantic RNG;
- per-cell baseline hashes;
- worldgen fingerprint/probes;
- DefinitionId rename/removal migrations;
- crash-safe save migration;
- migration dry-run/inspect tooling.

Do not rewrite M2/M2b unless a current failing test proves a defect.

## 0.1 Roles for this run (owner ruling)

- **Claude** is the primary gameplay/code agent through M6 and works only in its own worktree (§1).
- **DeepSeek/DSH** owns asset generation, rigging, animation and asset-pipeline tooling during this run, and nothing else.
- **Qwen** is paused from gameplay implementation.
- **The owner** merges to `main` at milestone gates. Claude may push branches and maintain draft PRs; Claude never merges to `main`.

## 0.2 Phase-scope rule (owner ruling)

`PROTOTYPE.md` governs **what** is in Phase 1. `ROADMAP.md` governs **when**.

Where a ROADMAP milestone's work items or exit criteria ask for more than `PROTOTYPE.md`'s scope, PROTOTYPE wins, and the ROADMAP text is reconciled in that milestone's "reconcile owning docs" step. The owner's milestone-specific rulings are in §14–§18.

---

# 1. DO NOT USE THE DIRTY ASSET WORKTREE FOR GAMEPLAY WORK

The primary repository worktree `G:\UNNAMED` has active DeepSeek/BEAST asset and animation pipeline edits.

These paths are DeepSeek-owned. Do not stage, edit, overwrite, revert, format or commit them, in any worktree. Read them for integration context only, until DeepSeek hands them off:

- `tools/asset_pipeline/**` and `tools/godot_validate/**`;
- `docs/WAVE_0_*.md`;
- `docs/ANIMATION_METADATA_SCHEMA.md`, `docs/ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md`, `docs/CANONICAL_BODY_AND_SKELETON.md`;
- `docs/ASTRAL_HOST.md`;
- generated assets (`assets/`).

Create a separate Git worktree/branch for this execution:

```powershell
cd G:\UNNAMED
git status
git fetch origin
git worktree add G:\UNNAMED_CLAUDE -b claude/phase1 main
cd G:\UNNAMED_CLAUDE
```

If `claude/phase1` already exists, inspect it rather than recreating it.

Untracked files do not follow into a new worktree. The DeepSeek-owned docs above exist only in `G:\UNNAMED`; read them there.

Do not run destructive cleanup in `G:\UNNAMED`.

---

# 2. INTEGRATE REMOTE SAFELY

Local `main` was 14 commits ahead of `origin/main` and 1 behind. The remote-only commit, `75303b1`, is a one-line README edit.

Inside the Claude worktree:

1. inspect `git log --oneline --decorate --graph --all -40`;
2. inspect divergence;
3. merge `origin/main`; **do not rebase the M2/M2b sequence**;
4. resolve the README conflict (the only conflict, per `git merge-tree`) by keeping the committed Otherreach README. Its statement that LAN/co-op/community-server seams are preserved, rather than an MMO being built now, already covers the remote line's LAN/WAN/MMORPG wording, so do not add that line;
5. build/test;
6. commit the merge.

Do not push until the local integration is green.

---

# 3. INSTALL THE OWNER-APPROVED DOCUMENT BUNDLE

Install into `docs/` from the bundle folder `G:\UNNAMED\OTHERREACH_PRE_CLAUDE_BUNDLE_2026-09-23\` (byte-identical to the zip):

- `CAMERA_PERSPECTIVE_AND_PRESENTATION.md`
- `WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT.md`
- `SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md`
- `MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md`
- `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md`

Install into `docs/` from `G:\UNNAMED\docs\`. These are the revised versions and supersede the bundle and zip copies:

- `CLAUDE_PHASE1_EXECUTION_PROMPT.md` — this brief, committed as the run's governing execution brief;
- `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`;
- `OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md`.

**Local history only — never committed (owner ruling: the repository is public).** Preserve these byte-exact as local historical artifacts outside any repository, in `G:\UNNAMED_HISTORY\2026-09-23\`. They are not current guidance: their status claims are stale, and V1/V2 still say M2 is incomplete and describe the game as first-person. Commit only the clean V3 handoff.

- `OTHERREACH_MASTER_HANDOFF_2026-09-23.md` (V1);
- `OTHERREACH_MASTER_HANDOFF_2026-09-23_V2.md`;
- `PRE_CLAUDE_RECONCILIATION_REVIEW.md`.

The originals stay untracked in `G:\UNNAMED\docs` until the owner removes them.

Small edits in the same intake:

- `SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md`: replace the "Implementation stage" line with the owner ruling. Phase 1 is structured M4 dialogue and minimal NPC continuity only; all deeper social systems are future.
- `POST_M2_DOCUMENTATION_RECONCILIATION.md` stays in the repository because it is already part of its history. Add an unmistakable **SUPERSEDED — historical record** banner under the title, pointing to `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` §11, which carries its open items. Change nothing else in it.
- `RACES_UPDATED.md`: `git rm` it. It was byte-identical to `RACES.md`, and only the precedence doc and the review named it (verified 2026-09-23). Re-check both with `cmp` and a grep before removing. `RACES.md` is canonical.
- Title hygiene (precedence doc §11): in the live documents' `**Project:**` header lines (not review files), replace `UNNAMED (working title)` with `Otherreach (codename UNNAMED)`. Mark `DECISIONS.md` open question 1 answered for the title. Leave the setting-tone part to the owner, with a pointer to `OTHERREACH_COSMOLOGY.md`.
- `docs/INDEX.md`:
  - point to the authority hierarchy (`PHASE_0_COMPLETE.md` §1 via the precedence doc) and the phase-scope rule;
  - list every design-extension document (the two design-pack indexes plus the new documents), labelled **directional/future unless their milestone owns them**;
  - note SOCIAL's Phase-1 scope;
  - mark `POST_M2_DOCUMENTATION_RECONCILIATION.md` as superseded/historical;
  - label this brief and the V3 handoff as orientation, not design authority.
- `AGENTS.md`:
  - replace its "Authority order" line with a pointer to `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`;
  - point to this brief and the V3 handoff;
  - state the §0.1 roles and the §1 DeepSeek-owned paths.

Do not commit:

- the V1 and V2 Master Handoffs and `PRE_CLAUDE_RECONCILIATION_REVIEW.md` (local history only, above);
- `BUNDLE_MANIFEST.md`;
- any ZIP (`GPT.zip`, the bundle zip, `docs/*.zip`);
- `RACES.md.bak-before-biology-update`;
- `OTHERREACH_NEXT_SESSION_BOOTSTRAP.txt`;
- `REVIEW_GIT_*.txt`;
- the stale root `PROJECT_CHARTER.md`. It is the pre-camera charter from `2782414`; the canonical charter is `docs/PROJECT_CHARTER.md`;
- any DeepSeek-owned path from §1. The bundle contains no animation or Wave-0 documents.

Commit this documentation intake separately.

**Pull-collision note.** Any path this branch commits that also sits untracked in `G:\UNNAMED` blocks that worktree's next pull. Git aborts even when the files are byte-identical; this was tested 2026-09-23. At each merge gate, give the owner the exact list of colliding paths and the command to remove them. Do not delete them yourself.

---

# 4. RUN THE FULL FOUNDATION VERIFICATION

Before M2c:

```powershell
dotnet build src/UNNAMED.sln
dotnet test src/UNNAMED.sln
```

Run the content lint.

Run the key M2/M2b exit-criteria tests.

Confirm the 240-test baseline or explain any deliberate count change.

Push `claude/phase1` and open a **draft PR into `main`**. The existing workflow runs on pull requests into `main`; pushes to other branches trigger nothing. Do not change the CI triggers. The suite has never run on Linux, so treat the first run as real verification.

If CI fails, stop milestone progression and fix it first.

---

# 5. HARD GATE: PROGRESSION-AXIS RECONCILIATION

Do **not** implement M2c from the current `PROGRESSION.md` without this review.

Read:

- `docs/PROGRESSION.md`
- `docs/reviews/PROGRESSION_SCOPE_ECONOMY_REVIEW.md` as historical adversarial evidence, not authority
- `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`
- `SKILLS_AND_DISCIPLINES.md`
- `CHARACTER_CREATION_AND_LINEAGE.md`
- `CRAFTING_AND_ITEMIZATION.md`
- `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md`
- `SOULS_DEATH_REINCARNATION_AND_LEGACY.md`
- `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md`
- relevant normative `DATA_MODEL`, `SYSTEMS`, `ROADMAP`, `PROTOTYPE`, `PERSISTENCE`.

Produce a written progression-axis decision before implementation.

## 5.1 Questions that must be answered

For every proposed progression axis:

- What distinct player question does it answer?
- What exact events/currency advance it?
- Can legitimate play advance it independently of neighboring axes?
- Is it state, a gate, a technique unlock, or truly a progression axis?
- Does another axis already represent the same thing?

At minimum analyze:

- character level / XP;
- attributes;
- skills and disciplines;
- weapon mastery;
- magic mastery;
- abilities/techniques;
- professions;
- reputation;
- equipment familiarity;
- companion progression.

## 5.2 Owner rulings already made (apply; do not re-open)

**Techniques and level-ups.**

- Techniques and formulas are learned in the world: through teachers, books, quests, study, research, experimentation, discovery/first success, artifacts and cultural training.
- Starting archetypes grant a small starting technique package.
- Level-ups do not buy techniques from a giant ability tree.
- Level-up advancement goes to attributes. Skills/disciplines improve through the ratified use/challenge model, so levels grant no skill points.
- Remove the generic ability-point pool unless this audit establishes a genuinely separate need for it. If it does, state that need and its advance vector explicitly.

**Magic.**

- Health / Stamina / Focus fundamentals;
- Resonance/Strain;
- optional contextual reagents/charges/etc.;
- **no universal mana**.

Remove or reconcile old `mana`, `spell resource pool` and equivalent assumptions. As of 2026-09-23 they appear at `PROGRESSION.md:149`, `DATA_MODEL.md:261` and `:305`, `SYSTEMS.md:96`, `:104` and `:169`, and `ROADMAP.md:207` and `:224`.

Known capability generally remains accessible. Preparation and implements affect speed, strain, stability and convenience. Do not retain a hard slot system or a school-attunement casting lockout (`PROGRESSION.md:235-236`) without justification. Phase 1 has no level-10 attunement system (§14).

**Companions.** The future normal active party is player + up to 3 companions/hirelings. Remove the max-six active-party rule (`PROGRESSION.md:47` and `:318`, `PERSISTENCE.md:524`). The prototype uses exactly **one** companion.

**Reputation / hostility.** Reputation is an access/standing axis, not the enemy-state variable. Keep these separate future concepts:

- relationship;
- legal status;
- faction relation;
- war state;
- tactical hostility;
- attack legality.

`SYSTEMS.md` S-27's `HostileToPlayerChanged` couples reputation to hostility and must be reconciled.

**Endgame.** Replace "prestige project" and "paragon" implications (`PROGRESSION.md:87`, `:257` and `:392`, `SYSTEMS.md:413`, `ROADMAP.md:317`) with the approved mastery / Great Works / legacy vocabulary. Implement none of those systems in M2c.

## 5.3 Contradictions you must resolve

**Weapon-family skill vs weapon mastery.** `PROGRESSION.md`'s skill list (`:157`) has no weapon-family skill. But `SKILLS_AND_DISCIPLINES.md` (owner direction) lists weapon families as skills: sword families, axes, blunt, polearms, daggers, shields, bows, crossbows, thrown, unarmed/grappling. `AX-WM` covers the same competence per family.

The old 2×2 proof ("high-skill / low-mastery swordsman") is therefore not valid as written. Weapon competence currently has two homes. Pick one home, or define a genuinely independent axis and prove its advance vector differs. Do not preserve two axes merely because D-09 listed both; D-09 itself says axes merge if they answer the same question.

**Crafting skill vs profession.** `SKILLS_AND_DISCIPLINES.md`'s crafting skills (smithing, woodworking, leather, textiles, alchemy, artificing, …) duplicate the `AX-PRF` professions (`PROGRESSION.md:280`). Resolve this the same way.

**Skill points vs use-based XP.** This is resolved by the §5.2 ruling: levels grant no skill points. Remove the level-granted skill points (`PROGRESSION.md:72`) and define the one coherent use/challenge model.

## 5.4 Independence tests and M2c exit criteria

Add a real independence test for **every retained neighboring pair** of progression axes. The test is a fixture proving each combination is reachable through the sanctioned advance vectors without the other axis moving as a side effect, in the style of the old 2×2.

You may rewrite old M2c exit criteria that assume an axis which does not survive reconciliation. For example, `ROADMAP.md:178` (b) requires "undiminished mastery" from a farm, which contradicts a merged, difficulty-gated skill model. Record every rewritten criterion and why.

## 5.5 Output

Create:

`docs/PROGRESSION_AXIS_RECONCILIATION.md`

It must include:

- current axes;
- keep/merge/remove decisions;
- advancement vectors;
- schema implications;
- rejected alternatives;
- the independence tests.

Then reconcile:

- `PROGRESSION.md`;
- `DECISIONS.md` D-09 if necessary;
- `DATA_MODEL.md`;
- `SYSTEMS.md`;
- `ROADMAP.md` (M2c exit criteria);
- `PROTOTYPE.md`;
- `PERSISTENCE.md` (companion roster note);
- the status header of `SKILLS_AND_DISCIPLINES.md`;
- relevant risk entries.

### Owner gate

If two genuinely different, plausible player-experience models remain and the choice between them is subjective, **STOP and ask the owner**. The technique-acquisition question is already decided (§5.2).

Do not stop for a choice that can be resolved mechanically from established design principles.

---

# 6. M2c — PROGRESSION SPINE

Once the progression model is ratified, implement M2c.

Every persisted schema change must go through the already-proven M2b migration harness. Do not bypass historical fixtures: a schema bump adds a fixture per `tests/Persistence.Tests/Fixtures/README.md`.

Required:

- level/XP model;
- attributes (the level-up destination) and derived pools, with no mana pool;
- the skill/discipline model under the ratified use/challenge rules;
- a technique/formula knowledge record, including the starting package, with no generic ability-point pool unless §5 established a separate need;
- only the masteries/profession structures that survived reconciliation;
- advancement telemetry;
- anti-farm guards;
- typed non-conversion enforcement where meaningful;
- the §5.4 independence tests.

Update the M2c exit criteria wherever reconciliation changed an axis's name or meaning.

Do not implement future Soul progression.

Create `docs/M2C_STATUS.md`.

Commit M2c separately.

Full build/test required.

---

# 7. BEFORE M3 — CAMERA / PRESENTATION RECONCILIATION

Read:

- `CAMERA_PERSPECTIVE_AND_PRESENTATION.md`
- `ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md`, `CANONICAL_BODY_AND_SKELETON.md`, `ANIMATION_METADATA_SCHEMA.md` — DeepSeek-owned, in `G:\UNNAMED\docs`, read-only
- `ENGINE_VALIDATION.md`
- `PROJECT_CHARTER.md`
- `ROADMAP.md`
- `PROTOTYPE.md`
- `RISK_REGISTER.md`
- `DECISIONS.md`.

Owner-approved direction:

> full-body third-person / over-the-shoulder primary representation with seamless zoom into first person.

Update live normative first-person-only wording. As of 2026-09-23 it appears at:

- `PROJECT_CHARTER.md:1109`, "first-person movement" in the Phase-1 list;
- `ROADMAP.md:186`, the M3 "First-person controller";
- `PROTOTYPE.md:169` and `:352`;
- `RISK_REGISTER.md:24` and `:82`;
- `DECISIONS.md:46`;
- `ENGINE_VALIDATION.md:8`;
- `COMBAT_DAMAGE_ARMOR_AND_DEATH.md:17`.

Leave the historical engine-comparison cells in `DECISIONS.md` alone.

`ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md` still assumes first person (`:6`, and `:843`: "Otherreach is first-person"). Report this to the owner/DeepSeek; do not edit it.

Do not rewrite historical review provenance unnecessarily.

RK-02 and D-01 must measure a representative player-camera load, not first-person only. Record the owner's performance ruling (§8) in:

- `RISK_REGISTER.md` RK-02;
- `PROTOTYPE.md` (the baseline machine);
- `DECISIONS.md` open question 2 (answered for Phase 1);
- `ENGINE_VALIDATION.md` (its fully dressed scene is later work).

---

# 8. M3 — PLAYER, CAMERA, MOVEMENT, INTERACTION, WORLD CELLS

Implement M3, preserving the current scope distinction.

## Playable prototype artifact

- 200 m × 200 m;
- 4 real 100 m cells;
- real cell addressing/delta path;
- not the full 2×2 km region.

The Godot project lives in `src/Presentation/` (`ARCHITECTURE.md` §3). It is an empty stub today. Godot 4.7.2 .NET is installed.

## Camera minimum

- full-body third person;
- seamless zoom to first person;
- shoulder swap;
- camera collision;
- interaction works at all supported distances;
- same authoritative commands in all perspectives.

Do not build two gameplay controllers.

## Authority

Presentation never writes authoritative position/state.

Prediction is visual only.

## Performance gate (owner ruling)

The Phase-1 performance gate is the **PROTOTYPE greybox scene at 1080p with a sustained 60 FPS**, on RAZER's RTX 4070 Ti. Measure with OBS, H3 and other significant GPU workloads stopped.

- **Coordinate the window.** Agree the measurement window with the owner. Never interrupt a live stream or an H3 render.
- **Record:** CPU and GPU frame times, 1% lows, RAM/VRAM, and hitches.
- **No D-01 verdict.** Do not evaluate D-01's formal engine-revisit trigger in Phase 1. That trigger needs the streaming and LOD systems, which do not exist yet.
- **2×2 km greybox.** ROADMAP M3's isolated 2×2 km greybox capture still runs and its numbers are recorded (RK-02: "the number produced is the deliverable"). It is **not** the formal D-01 revisit gate.
- **Stop line.** Failure to sustain 1080p / 60 FPS on a clean RAZER 4070 Ti after reasonable optimization is still an owner-review stop, whether it shows in the PROTOTYPE greybox scene or the 2×2 km greybox. Stop before any further engine-dependent systems are built.
- **Dressed scene.** The fully dressed `ENGINE_VALIDATION.md` scene is later work.

Create `docs/M3_STATUS.md`.

Commit separately.

---

# 9. ANIMATION WAVE 0 — PARALLEL TRACK

DeepSeek/BEAST is actively implementing animation tooling.

Do not duplicate or overwrite its pipeline. Do not commit or edit DeepSeek-owned animation/Wave-0 files (§1). Read them for integration context only until DeepSeek hands them off. Integrate handed-off artifacts rather than reinventing them.

Animation Wave 0 target includes:

- canonical humanoid skeleton;
- idle/walk/run;
- representative combat clips;
- retarget;
- animation-only GLB;
- Godot AnimationTree;
- IK proof.

M3 must **not** wait for final animation polish.

Graybox/proof motion is acceptable.

Coordinate any integration that needs asset-pipeline changes with DeepSeek through a distinct branch, never through concurrent edits.

---

# 10. CONTENT VALIDATION HARDENING — BEFORE CONTENT SCALE

The current implementation report notes:

- kind-specific semantic fields are not fully validated;
- nested references can be missed.

Before M3b creates meaningful item/equipment content, close this gap for the Phase-1 schemas actually in use.

Required:

- recursive nested-reference discovery;
- expected target-kind validation (`DATA_MODEL.md` §5);
- required semantic/range checks for Phase-1 content;
- tests with invalid nested refs;
- the reserved mod namespace lint. Core content may not use a `mod` segment directly after its kind: `<kind>.mod.<package_namespace>.*`, with kinds per the `DATA_MODEL.md` §1 kind table (e.g. `item.weapon.mod.*`, `creature.mod.*`). Nothing collides today. Do not build a mod loader.

Do not build a giant general-purpose validation language.

Call this a pre-M3b infrastructure hardening task, not a new gameplay feature.

Commit separately.

---

# 11. M3b — ITEMS, INVENTORY, EQUIPMENT

Implement the Phase-1 subset only. `PROTOTYPE.md`'s scope table decides what is in; for example, enchanting, sockets, durability and set items are out.

Preserve future compositional crafting direction without implementing the entire item grammar.

Required:

- stable item definitions/instances;
- inventory/container transfers;
- equipment;
- persistence;
- currency;
- simple loot evaluation;
- appropriate requirements.

Do not hard-code item IDs.

Do not create an item-power treadmill that makes every item obsolete every two levels.

Do not implement full crafting yet.

Create status doc and commit.

---

# 12. M3c — COMBAT CORE

Before implementation, reconcile `SYSTEMS.md`/roadmap with:

- `COMBAT_DAMAGE_ARMOR_AND_DEATH.md`;
- `STEALTH_DETECTION_AND_THREAT.md`.

Important:

- no universal level-based physical immunity;
- weapon reach/position/timing matter;
- avoid HP sponges;
- damage has one authoritative pipeline;
- "aggro" must not become global psychic awareness. `SYSTEMS.md` S-12's "threat/aggro table" becomes local, per-actor and perception-derived.

For Phase 1, implement only the depth required by the roadmap and three weapon families.

Do not implement every exotic weapon, grappling subsystem, or advanced injury feature yet.

Combat event timing should be compatible with the animation metadata system, but gameplay authority must not depend on arbitrary clip frame numbers.

Create status doc and commit.

---

# 13. M3d — CREATURES, PERCEPTION, AI, LOOT

Implement five distinct creatures/roles as scoped.

Perception rule:

> Actors react only to what they perceive, infer, or learn through communication.

Use:

- sight;
- sound;
- last-known position;
- local call-for-help where perceived/communicated.

Do not introduce global faction aggro.

A faction relation can influence policy after identity/knowledge exists; it does not give every remote actor omniscience.

Preserve the future systemic-hostility seam, but do not implement the full crime/faction system yet.

Create status doc and commit.

---

# 14. M3e — BASIC MAGIC

Before implementation, reconcile:

- `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md`;
- the progression decision;
- `SYSTEMS.md` S-06/S-13;
- `DATA_MODEL.md`;
- `ROADMAP.md`;
- prototype HUD/resource language.

Owner-approved core:

- Health;
- Stamina;
- Focus;
- Resonance/Strain;
- contextual costs where applicable;
- **no universal mana pool**.

**Phase-1 scope (owner ruling):** three tiny representative magic domains/traditions with one formula each, using Resonance/Strain. There is no level-10 attunement system. This matches `PROTOTYPE.md`'s three schools with one spell each and its C10 criterion that each of the three must be used to succeed.

Rewrite ROADMAP M3e to this scope before coding. ROADMAP M3e currently specifies one elemental school to full depth, spell resources, and attunement slots at level 10. Keep or replace its named research-vs-casting test according to the ratified progression model.

Phase-1 proof:

- the three formulas and casting;
- Strain accumulation/recovery;
- interruption/concentration;
- one implement distinction if cheap;
- clear UI feedback.

Do not implement:

- full custom spell editor;
- Great Works;
- Otherwhen;
- all domains;
- divine cosmology systems.

Create status doc and commit.

---

# 15. M3f — SKILLS, GATHERING, CRAFTING PROOF

Implement the ratified skill model.

**Phase-1 scope (owner ruling):** two recipes and one gather→craft proof loop. There is no full profession-rank ladder. This matches `PROTOTYPE.md`: "Two recipes, no profession level".

Rewrite ROADMAP M3f to this scope before coding. It currently specifies alchemy ranks 0–2 with quality tiers and discoveries, plus a rank-vs-skill test that assumes profession ranks.

Use the crafting principles:

- quality/material properties matter;
- no "craft thousands of daggers" mastery;
- meaningful first/discovery/complexity progress.

Do not implement the full future combinatorial crafting laboratory.

Create status doc and commit.

---

# 16. M4 — NPC PERSISTENCE + DIALOGUE

**Phase-1 scope (owner ruling):**

- **Included:** a small NPC population (the prototype's), fully simulated.
- **Deferred:** tier-transition simulation and ROADMAP M4's 200-promotion risk spike, to the first milestone that actually introduces simulation tiers.
- **Also deferred:** NPC schedules. `PROTOTYPE.md` puts them in the vertical slice.

Reconcile ROADMAP M4's settlement, schedule and tier text accordingly before coding.

NPCs must retain:

- identity;
- relevant life state;
- basic continuity.

Dialogue remains deterministic/structured.

Do not require runtime LLM.

The game must work with AI narrative disabled.

Social scope in Phase 1 is structured dialogue and minimal continuity only. Use the social design document as future direction, not a reason to implement persuasion, rumor or language simulation now.

Create status doc and commit.

---

# 17. M5 — QUEST FRAMEWORK + DEBUGGER

Implement the declarative quest graph/objective system.

The debugger is mandatory:

> "What is this quest waiting on right now?"

At least one purposeful non-kill objective must work.

Do not author huge quest volumes.

Create status doc and commit.

---

# 18. M6 — COMPANION V1

**Phase-1 scope (owner ruling):** one companion with prototype behavior. There is no affinity-ladder or personal-quest requirement. Reconcile ROADMAP M6's work items accordingly before coding.

Minimum:

- recruit through world;
- follow;
- wait;
- catch-up;
- downed/death behavior required by prototype;
- save/load.

Do not implement:

- full player+3 party;
- romance;
- offscreen marriages;
- institutions;
- advanced tactics.

But do not encode a permanent max-six active-party assumption.

Create status doc and commit.

---

# 19. STOP AFTER M6

M6 is the owner playtest gate.

Do not automatically begin:

- M7;
- factions/building;
- M8 dungeon/boss;
- M9 vertical slice;
- multiplayer;
- mod loader.

At M6 provide a complete playable-prototype report that evidences the Phase-1 acceptance requirements:

- death/respawn with the penalty applied exactly once (`PROTOTYPE.md` C17);
- a recorded fresh-session acceptance run (`PROTOTYPE.md` §9 item 1);
- a save/reload world-state comparison, field by field, as a diff of expected vs. actual (§9 item 2);
- performance evidence from the §8 gate (§9 item 4);
- the Phase-1 README and the replayable 10-minute scripted playthrough (§9 items 5–6).

Then stop for owner playtesting.

The 3–5 blind-tester early feel test (ROADMAP M6) remains a requirement before Phase 2. The owner runs it after the playtest; prepare what it needs if asked.

---

# 20. GIT / COMMIT RULES

Each milestone/hardening pass gets its own coherent commit(s).

Never mix:

- DeepSeek asset-pipeline edits;
- generated assets;
- unrelated documentation;
- gameplay feature work.

Do not rewrite M2/M2b history.

Keep the worktree clean at each milestone gate.

Push `claude/phase1` and keep a draft PR into `main` open so CI runs. Never merge to `main`; the owner merges at milestone gates.

After an owner merge, fetch and merge `origin/main` into `claude/phase1` (never rebase), then open the next draft PR.

At each gate, report the §3 pull-collision list.

Do not merge a failing milestone to main.

---

# 21. STATUS DOCUMENT FORMAT

For each milestone report:

- entry criteria;
- work completed;
- key design decisions;
- files changed;
- tests by project;
- runtime validation;
- exit criteria one-by-one;
- known deferrals;
- Git commit(s);
- clean/dirty status;
- next milestone.

No "complete" claim without evidence.

---

# 22. OWNER-APPROVED FUTURE DESIGN — DO NOT IMPLEMENT NOW

These docs are directional:

- weather/seasons/survival;
- full social system/languages (everything beyond M4 structured dialogue and minimal continuity);
- systemic hostility;
- Great Works/Legacy;
- Souls/reincarnation;
- modding;
- community servers;
- federation;
- PvP;
- cosmic/Otherwhen systems.

Preserve seams only.

---

# 23. FINAL PHASE-1 DELIVERABLE

At M6 the owner should be able to:

- launch Otherreach;
- move through the 4-cell prototype;
- zoom smoothly between third and first person;
- interact;
- equip items;
- fight;
- encounter creatures;
- cast three basic formulas using Resonance/Strain, each needed to succeed;
- gather and craft through the two-recipe loop;
- talk to NPCs;
- complete a quest;
- recruit one companion;
- die and respawn with the stated penalty;
- save, quit, relaunch, load;
- see the same world state, proven by the field-by-field comparison;
- see the recorded acceptance run and the performance evidence from RAZER.

This is the first real internal playable Otherreach prototype.

Then STOP and ask the owner to play it.
