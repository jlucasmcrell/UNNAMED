# OTHERREACH — MASTER HANDOFF V3

**Snapshot:** 2026-09-23 — M2b complete, M2c not started.

**Supersedes:** the V1 and V2 handoffs. Because the repository is public, neither is committed. Both are kept only as local historical artifacts outside the repository, in `G:\UNNAMED_HISTORY\2026-09-23\`. Their status sections are stale: they say M2 is incomplete and describe a first-person game.

**Purpose:** orientation for a fresh session, whether design discussion or implementation. This is **not design authority**. Authority is `PHASE_0_COMPLETE.md` §1, as restated in `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` §1. If this handoff disagrees with a repository document, the repository document wins and this handoff has a bug.

---

## 1. Identity

- **Title:** **Otherreach**.
- **Codename:** `UNNAMED`, used for the folder, solution, `UNNAMED.<Project>` namespaces and the GitHub repository. Do not rename it casually.
- **Taglines:** "Beyond where. Beyond when." and "Every road leads somewhere. The Otherways do not." Preserve both.
- **Name caveat:** no game titled *Otherreach* was found, but "OtherReach" is an in-world realm name in the 2010 animated series *Tara Duncan*. Do a trademark/brand clearance before major public branding spend.
- **Repository:** `https://github.com/jlucasmcrell/UNNAMED`. It is **public** (verified 2026-09-23), so anything pushed is published. Default branch `main`. Local root `G:\UNNAMED`.
- **License:** source code is MIT (`LICENSE`). Non-code creative assets are not MIT unless explicitly licensed (`README.md` §License).

---

## 2. The Game in Brief

A single-player-first, open-world fantasy/science-fantasy RPG. The primary view is **full-body third-person / over-the-shoulder with seamless player-controlled zoom into first person**, and perspective never changes gameplay rules. It is inspired in *feel* by *Asheron's Call* and *EverQuest II*, and is wholly original.

The pitch is `README.md`; the vision is `docs/PROJECT_CHARTER.md`. The principles that summarize the direction:

- The world does not revolve around you.
- NPCs are participants in the world, not decorations placed for the player.
- The map shows what your character knows, not what the database knows.
- Actors react only to what they perceive, infer, or learn through communication.
- A powerful character is hard to kill because they are skilled, prepared, equipped, protected, altered or genuinely superhuman. A level number never makes a physically lethal strike harmless.
- If a crafting combination is logically possible, prefer meaningful costs and tradeoffs over arbitrary prohibition.
- Hotbars are convenience, not capability limits.
- Survival is preparation and adaptation, not maintenance chores.
- Social skill cannot create a reason for agreement where none exists.
- The simulation creates history. AI helps characters talk about it.
- Character progression asks "Who am I becoming in this life?"; Soul progression asks "What has persisted through all of my lives?"
- Otherreach grows by deepening working systems, not by shipping dozens of half-built ones.

---

## 3. Engineering State (verified 2026-09-23)

- **Stack:** Godot 4.7.2 .NET for presentation only (`src/Presentation`, an empty stub today); C# .NET 8; xUnit; MessagePack; YamlDotNet.
- **Complete:** Phase 0, M0, M1, M1b, M2, M2b. Evidence is in `M1_STATUS.md`, `M2_STATUS.md` and `M2B_STATUS.md`.
- **Tests:** 240 in total.

| Project | Tests |
|---|---:|
| Architecture | 11 |
| Content | 34 |
| Domain | 4 |
| EntityRegistry | 23 |
| World | 58 |
| Persistence | 110 |

- **Persistence:**
  - versions: `save_format` 1, `schema_version` 3, `worldgen_version` 2, `rng_contract_version` 2;
  - historical fixtures v1–v3, with the ordered v1→v2→v3 migration chain;
  - identity and baselines: definition-ID rename/removal maps, semantic RNG, per-cell baseline hashes, and the worldgen fingerprint;
  - crash-safe save and migration commits.
  - The owner-approved M2b refinement is `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md`.
- **Tools:** the content lint and the save tool (`save:migrate --dry-run`, `save:migrate`, `save:inspect`). `AGENTS.md` has the exact commands.
- **Known gaps:**
  - the content lint does not validate kind-specific fields and misses nested references. This is fixed in the pre-M3b hardening pass;
  - the suite has never run on Linux.
- **Git:**
  - local `main` is at `909c00c`, 14 ahead and 1 behind `origin/main`;
  - the remote-only commit `75303b1` is a one-line README edit. Integrate it by merge, never rebase, so the M2/M2b commit hashes survive;
  - CI (`.github/workflows/dotnet.yml`) runs only on pushes to `main`/`master` and on PRs into `main`.
- **Worktrees:**
  - `G:\UNNAMED` is dirty with DeepSeek's asset work and holds many untracked owner documents;
  - gameplay work happens in `G:\UNNAMED_CLAUDE` on branch `claude/phase1`;
  - a path committed on that branch that also sits untracked in `G:\UNNAMED` blocks that worktree's next pull, so remove the untracked copy first.

---

## 4. Roles (owner ruling, 2026-09-23)

- **Owner (Joe):** design authority. Merges to `main` at milestone gates.
- **Claude:** primary gameplay/code agent through M6. Works in its own worktree; pushes branches and keeps a draft PR into `main` (which triggers CI). Never merges to `main`.
- **DeepSeek/DSH:** asset generation, rigging, animation and asset-pipeline tooling only. It owns:
  - `tools/asset_pipeline/**` and `tools/godot_validate/**`;
  - `docs/WAVE_0_*.md` and `docs/ANIMATION_*.md`;
  - `docs/CANONICAL_BODY_AND_SKELETON.md` and `docs/ASTRAL_HOST.md`;
  - `assets/`.
- **Qwen:** paused from gameplay implementation.
- **Design sessions** (ChatGPT or other LLMs) use this handoff to continue design discussion. Their output enters the repository as documents the owner reviews.

---

## 5. Phase-1 Plan

The execution brief is `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md` (revision 2).

```text
worktree → merge origin/main → documentation intake → full verification + draft-PR CI
→ progression-axis audit (hard gate) → M2c
→ camera/presentation doc reconciliation → M3 (+ performance gate)
→ content-validation hardening → M3b → M3c → M3d → M3e → M3f → M4 → M5 → M6
→ STOP for owner playtest
```

Animation Wave 0 runs in parallel under DeepSeek. M3 uses graybox or proof motion and does not wait for polish.

**Scope rule:** `PROTOTYPE.md` governs what is in Phase 1; `ROADMAP.md` governs when.

**Phase-1 scope rulings:**

- **M3e:** three tiny magic domains/traditions with one formula each, using Resonance/Strain. No level-10 attunement.
- **M3f:** two recipes and one gather→craft loop. No profession-rank ladder.
- **M4:** a small NPC population, fully simulated. Tier-transition simulation and NPC schedules are deferred.
- **Social:** Phase 1 is M4's structured dialogue plus minimal continuity.
- **M6:** one companion with prototype behavior. No affinity ladder or personal quests.

**Performance gate:**

- **Scene and target:** the PROTOTYPE greybox scene at 1080p, sustained 60 FPS, on RAZER's RTX 4070 Ti.
- **Conditions:** OBS, H3 and other significant GPU workloads stopped, in a window agreed with the owner.
- **Recorded:** CPU/GPU frame times, 1% lows, RAM/VRAM and hitches.
- **Out of scope:** D-01's formal engine-revisit trigger is not evaluated in Phase 1. ROADMAP M3's 2×2 km greybox is still captured, but it is not the formal D-01 gate.
- **Stop line:** failure to sustain 1080p/60 FPS on a clean RAZER 4070 Ti after reasonable optimization is an owner-review stop.

**Acceptance after M6:**

- death/respawn;
- a recorded acceptance run;
- a field-by-field save/reload state comparison;
- performance evidence;
- the Phase-1 README and a replayable 10-minute scripted playthrough;
- later, 3–5 blind testers before Phase 2.

---

## 6. Settled Design Direction (owner-approved)

| Topic | Direction | Lives in |
|---|---|---|
| Camera | Full-body third-person primary; seamless zoom to first person; one set of gameplay rules; no FPS-only body | `CAMERA_PERSPECTIVE_AND_PRESENTATION.md` |
| Progression | Techniques/formulas learned in the world (teachers, books, quests, study, research, experimentation, discovery/first success, artifacts, cultural training); small starting package from the archetype; level-ups go to attributes; no level-granted skill points; generic ability-point pool removed unless the audit finds a separate need; weapon and crafting competence each get one home | `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` §3; `PROGRESSION_AXIS_RECONCILIATION.md` (written at M2c) |
| Magic | Health/Stamina/Focus; Resonance/Strain; contextual costs; **no universal mana**; known capability stays accessible; compositional in the long term | `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md` |
| Combat | Physical first: reach, position, timing, armor, anatomy; no level-based immunity; no HP sponges; one damage pipeline | `COMBAT_DAMAGE_ARMOR_AND_DEATH.md` |
| Perception | Awareness only from perception, inference or communication; no global aggro; last-known position | `STEALTH_DETECTION_AND_THREAT.md` |
| Hostility | Keep separate: relationship; fear/trust/grudge; reputation; legal status; faction relation; war state; identity knowledge; tactical hostility; attack legality. Reputation is access/standing only | `SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md`, `CRIME_LAW_REPUTATION_AND_JUSTICE.md` |
| Companions | Eventual normal party is player + up to 3; one in the prototype; never a max-six rule | `COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md` |
| Crafting | Compositional construction; material properties matter; no bulk-dagger grind; first/discovery/complexity progress | `CRAFTING_AND_ITEMIZATION.md` |
| Endgame | Finite meaningful mastery, Great Works, institutions, legacy, Soul continuity; no prestige/paragon treadmill (M12/M13) | `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` |
| Souls | A persistent identity above incarnations within a world; future | `SOULS_DEATH_REINCARNATION_AND_LEGACY.md` |
| Modding, servers, PvP | Seams only; reserved `<kind>.mod.*` namespace; attack-legality seam; D-12 in force. Future order: co-op seam → host/join → dedicated community servers → persistent worlds → trusted clusters → optional federation | `MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md` |
| Weather, survival | Preparation and adaptation, not chores; Phase 2+ | `WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT.md` |
| Social, languages | Phase 1 = structured dialogue + minimal continuity; the rest is future | `SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md` |
| AI narrative | Optional and provider-independent; never authoritative; the game is fully playable with it disabled | `AI_NARRATIVE_SERVICE.md` |
| Race vs culture | Race changes biology and what comes naturally; it does not lock learnable knowledge | `RACES.md`, `SKILLS_AND_DISCIPLINES.md` §8, `CHARACTER_CREATION_AND_LINEAGE.md` |
| Setting | Cosmology, mythology, eight playable peoples (Veth, Kal, Siann, Orenth, Mor, Constructed, Vaskaal, Ondrek); gods exist and their explanation remains open | `OTHERREACH_COSMOLOGY.md`, `MYTHOLOGY.md`, `RACES.md` |

**First world region for asset production (lives only here):** the **Otherhome Marches**, a working name. It is a 2×2 km region adjacent to Otherhome, not the central city. It is meant to exercise many systems:

- a road toward Otherhome with a distant skyline;
- a village/waystation;
- farms, woodland, water and highland;
- wilderness building space;
- a mine/quarry, ruins and small dungeons;
- a limited or inactive Othergate;
- travelers from several cultures.

The lore name and biome are not final. This is Phase-2 vertical-slice territory; Phase 1 is the 200 m prototype.

---

## 7. Asset and Animation Status

As reported in DeepSeek's Wave-0 documents on 2026-09-23 (not independently re-verified):

- 15/15 Wave-0 proof assets built and Godot-validated; 324/324 pack verification clean;
- canonical body/skeleton v1 for four fit families;
- animation metadata schema and validator;
- armor fit Method C proven. Generated-style → canonical-fit transfer is still future pipeline work;
- retarget/motion tooling in progress.

---

## 8. Open Questions and Next Design Topics

**Owner questions** (`DECISIONS.md`):

- **Title:** answered, **Otherreach**.
- **Setting tone:** open. The approved cosmic/science-fantasy direction is in the setting documents.
- **Visual fidelity:** default "readable and atmospheric".
- **"While you were away" catch-up policy:** needed before settlement simulation.
- **Platform baseline:** answered for Phase 1 by the RAZER 4070 Ti 1080p/60 gate.

**Next human design topics** (none blocks Phase 1):

- difficulty/accessibility pass;
- deeper religion/gods/culture pass;
- specs for server administration, a world-event director, the Soul Sheet UI and the spell-construction UI;
- charter small amendments (carried from the superseded `POST_M2_DOCUMENTATION_RECONCILIATION.md` §4);
- a conversation-parity audit, then a `DESIGN_DECISION_LEDGER.md` of compact numbered rules (`GD-001 — The world does not revolve around the player.` …), then a consolidated Design Bible.

**Systems to design once, deliberately, before any is built ad hoc:**

- a **Knowledge/Belief/Information** record (fact, source, confidence, timestamp, subject, location, staleness; truth known only to the simulation), used by maps, markers, stealth, witnesses, crime, rumors, NPC memory, deception, divination and news;
- **Contracts**, used by bounties, commissions, hirelings, trade, escort, deliveries and NPC adventurer work;
- **Relationships**, used by companions, witnesses, guards, romance, teaching, crime and social systems;
- **Resource channels**, used by magic, weapons, devices, crafting and Vaskaal technology. Never default an ability to mana.

**Known reconciliation items:**

- autonomous-NPC offscreen combat vs simulation-tier rules;
- the component item grammar vs `DATA_MODEL.md`;
- combat body coverage vs visual armor pieces (semantic coverage, not mesh slots);
- Soul identity and persistence (design it when it enters scope);
- Other vs Pacha vocabulary;
- weather/world-time persistence;
- generated-AI-content persistence as versioned state.

---

## 9. What a New Session Must Not Do

- Rewrite persistence because a later idea sounds useful. Schema changes go through the M2b migration harness with a new historical fixture.
- Add networking to Phase 1 or 2 (D-12), or make Godot presentation authoritative.
- Reintroduce GUID instance IDs or a second DefinitionId contract.
- Reduce test counts, or swap tests to keep a count.
- Make every NPC globally aware of combat, make maps omniscient, or keep one global karma/reputation number.
- Make hotbars limit learned abilities, or make every magical ability consume generic mana.
- Make race equal culture, or hard-lock ordinary cultural knowledge by race.
- Make crafting a closed recipe catalog.
- Make LLM output authoritative.
- Reset every dungeon identically, or respawn unique artifacts because a boss returns.
- Make prisons real-time waiting, pets purely decorative by default, or remote storage magically global.
- Make Social/Persuasion a mind-control stat.
- Assume all future design must ship in 1.0.
- Edit or commit DeepSeek-owned asset/animation files from a gameplay branch.
- Merge to `main`, or begin Phase 2, without the owner.

---

## 10. Read Order

**Implementation session:**

1. this handoff;
2. `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`;
3. `AGENTS.md`;
4. `CLAUDE_PHASE1_EXECUTION_PROMPT.md` when executing Phase 1;
5. `PROJECT_CHARTER.md` → `DECISIONS.md` → `ARCHITECTURE.md` → `PERSISTENCE.md` → `ROADMAP.md` → `PROTOTYPE.md`;
6. `PROGRESSION.md` only with the precedence doc's §3 warning in mind, until the M2c audit lands;
7. the latest `M*_STATUS.md` and the owning design documents for the current milestone.

**Design session:**

1. this handoff;
2. `README.md` and `PROJECT_CHARTER.md`;
3. `INDEX.md` for the design-extension set, then the documents for the topic;
4. the local V1 handoff in `G:\UNNAMED_HISTORY\2026-09-23\` (not in the repository) for the full record of earlier design conversations. Its status sections are stale.

---

## 11. Owner Workflow Preferences

- Verified, source-based answers rather than speculation.
- Concise, direct procedural instructions for Git/PowerShell, with exact copy/paste commands for dangerous Git operations.
- Durable decisions preserved in repository documents, not chat memory. Design sessions deliver downloadable Markdown files.
- One milestone at a time, with build/test evidence and completion reports tied to exit criteria.
- When briefing a coding agent: give the explicit project root, require it to read current docs, and distinguish examples from existing code.

---

## 12. Maintaining This Handoff

Refresh it at each milestone gate or after a major design session by writing a new version (V4…), never by appending. Keep old versions as local history outside the public repository.
