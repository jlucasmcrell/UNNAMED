# M2c Status — Progression Spine

**Project:** Otherreach (codename UNNAMED)
**Milestone:** M2c — Progression Spine (`ROADMAP.md`)
**Status:** Complete. Every exit criterion below is backed by an automated test.
**Branch:** `claude/phase1` (draft PR into `main`, jlucasmcrell/UNNAMED#1)

---

## 1. Entry criteria

| Criterion | Evidence |
|---|---|
| Registry and save baseline work (M2, M2b) | `M2_STATUS.md`, `M2B_STATUS.md`; 240/240 tests before this milestone, green on Windows and on Linux CI |
| `PROGRESSION.md` ratified | The progression-axis audit (`PROGRESSION_AXIS_RECONCILIATION.md`, commit `47fb3f5`). Owner gate not triggered |

---

## 2. Work completed

- **Domain** (`src/Domain/Progression/`), as pure functions over an immutable record:
  - **Level/XP:** the curve, `source_kind` tagging, the guards `AG-1`..`AG-3`, first-time production (`AG-7`) and the `AG-8` death debt.
  - **Attributes:** seven attributes, allocation, bounded one-time grants, and derived Health/Stamina/Focus maxima, Resonance and Strain tolerance. There is no mana.
  - **Skills:** use under challenge, with the difficulty gate, novelty bonus, and common ceiling.
  - **Techniques:** the technique/formula/recipe knowledge record, learned only through typed learning events, with a starting package.
  - **Telemetry:** per-axis advancement records.
- **Non-conversion as a typed API.** `ProgressionEngine` advances each axis only through its own currency type (`XpAward`, `SkillPractice`, `TechniqueLearning`, `AttributeAllocation`, `AttributeGrant`). Domain cannot see items or gold: it references no World, Persistence or Content assembly.
- **Persistence, schema 4:**
  - `PlayerRecord` carries the progression record; the player digest is now `unnamed.player/v3`.
  - `player.msgpack` gains a `progression` map; enums are saved as snake_case keys.
  - The frozen `Sections/V3` shapes now back the 2 → 3 step; the 3 → 4 step (`SchemaV3ToV4`) gives older saves the empty record.
  - The definition-ID pass now covers skills, known techniques, first-time records and kill records.
  - The v4 historical fixture is written by this build, with the fixture README updated.
- **Content:**
  - A `skill` kind (`content/skills/`).
  - `config.time`, `config.xp_curve`, `config.level_cap` and `config.progression`.
  - The prototype's three skills (athletics, survival, one_hand_blade).
  - `ProgressionContent` builds the domain rules from the config, and the content lint checks it (PRG001–PRG005).
- **Docs:**
  - The audit (previous commit).
  - `PERSISTENCE.md`: schema 4, and the 3 → 4 row of the chain table.
  - `PROGRESSION.md` AG-1 row and `DATA_MODEL.md` `config.time` comment corrected (see §3).
  - This report.

---

## 3. Key design decisions (for owner review)

1. **AG-2 and AG-3 share one floor.**
   - **Rule:** each guard floors at 0.10, and together they never compound below 0.10.
   - **Why:** exit criterion (b) requires a saturated farm to yield at least 0.10×, and a product of two floors would give 0.01×.
2. **AG-1 never zeroes a new species.** A species the character has never killed gets at least the smallest non-zero band value (0.05, `new_species_floor`).
3. **AG-1 table corrected.**
   - **Problem:** the first row read `+4..0 → 1.00`, which overlapped the explicit above-level rows (`+1..+5 → 1.15`).
   - **Fix:** the above-level rows govern, so the row is now `0 → 1.00` (`PROGRESSION.md` §3.4).
4. **`config.time` arithmetic corrected.**
   - **Problem:** `DATA_MODEL.md`'s comment said a game day is 2880 game minutes, or 96 real minutes. Its own constants (2 real seconds per game minute, 24 × 60 minutes) give 1440 game minutes: 48 real minutes, or 57,600 ticks.
   - **Fix:** the comment now matches the constants. AG-2 counts per world day of 57,600 ticks.
5. **Debt repayment.**
   - Later XP repays the `AG-8` debt before it counts as progress.
   - Total debt is capped at one level span.
   - XP past the level cap is kept only in the lifetime totals.
6. **Level and progress are stored separately**, never recomputed from total XP, so a later curve change never silently moves an existing character's level.
7. **Phase-1 derived values use only the three live attributes.** Might, Endurance and Will drive them, per `PROTOTYPE.md`. The other four are in the schema and join the formulas later.
8. **Balance constants are initial data values** (`config.progression`):
   - skill XP 10 per use at one margin past the gate, capped at 2×;
   - failure teaches at 0.5;
   - novelty bonus 25;
   - skill curve 20 + 4 × level.
9. **The starting package is empty in Phase 1.** The prototype's known formulas and recipes join it when M3e and M3f add their definitions; the lint rejects a package naming undefined content.
10. **Fixture content.**
    - The v4 fixture is written with a new provenance pack, `content-0.1.1`: 0.1.0 plus the definitions its progression record names.
    - The current fixture pack (0.2.1) renames the formula it knows, so every fixture load proves the definition pass reaches the knowledge record.

---

## 4. Files changed

**Source:**

- **Domain:** `src/Domain/Progression/{ProgressionKeys,ProgressionRules,CharacterProgression,ProgressionEngine}.cs`.
- **World:** `src/World/PlayerState.cs`.
- **Persistence:**
  - `src/Persistence/{SaveModel,SectionCodec,Migrations,SaveLoader}.cs`;
  - `src/Persistence/Sections/{SchemaV3,ProgressionSection}.cs`.
- **Content:** `src/Content/{SchemaResolution,ContentLoader,ProgressionContent}.cs`.

**Content:** `content/config/{time,xp_curve,level_cap,progression}.yaml`, `content/skills/{athletics,survival,one_hand_blade}.yaml`.

**Tests:**

- `tests/Domain.Tests/Progression/*` (8 files).
- `tests/Content.Tests/{ProgressionContentTests,ProgressionBalanceTests,ValidationTests}.cs`.
- `tests/Persistence.Tests/{MigrationTests,HistoricalFixtureTests,RoundTripTests,SaveToolTests,CanonicalState}.cs`.
- `tests/Persistence.Tests/Fixtures/{v4/,content-0.1.1/,content/,v1-v3/expected.json,README.md}`.
- `tests/M2.Probe/M2Fixtures.cs`.

**Docs:** `docs/{M2C_STATUS,PERSISTENCE,PROGRESSION,DATA_MODEL,PROGRESSION_AXIS_RECONCILIATION}.md`.

---

## 5. Tests by project

| Project | Before | After |
|---|---:|---:|
| Architecture | 11 | 11 |
| Content | 34 | 49 |
| Domain | 4 | 67 |
| EntityRegistry | 23 | 23 |
| World | 58 | 58 |
| Persistence | 110 | 117 |
| **Total** | **240** | **325** |

No test was removed or weakened. Four existing tests were updated because the chain gained a step:

- the two migration step-count assertions;
- the chain-gap message;
- the fixture alias list for v4.

`ContentLoaderTests.LoadAll_Loads_Yaml_Files` now lists the new game definitions.

---

## 6. Runtime validation

M2c is domain and persistence only; there is no game runtime until M3. It is validated headlessly:

- the full suite on Windows;
- Linux CI on the draft PR;
- the content lint on the game content (10 definitions, 0 errors).

**The telemetry report** (the ROADMAP "Proof") is produced from the shipped config by `ProgressionBalanceTests`:

```bash
UNNAMED_WRITE_PROGRESSION_REPORT=report.json dotnet test tests/Content.Tests --filter TheTelemetryReport
```

Its figures, against a Novice-tier target of 4,280 XP/h:

| Profile | XP/h | × target | Source kinds |
|---|---:|---:|---|
| explorer | 4,025 | 0.94 | discovery, quest_objective, combat |
| quester | 3,600 | 0.84 | discovery, quest_objective, social |
| fighter | 4,261 | 1.00 | quest_objective, combat |
| crafter | 3,320 | 0.78 | discovery, quest_objective, production |

A twelve-hour varied session (every profile, with practice and lessons) shows every axis-pair correlation over ten-minute windows at |r| ≤ 0.24. That is far below §13.2's 0.8 alarm, including level/attributes, which are coupled by design.

**These are Novice-tier reference awards, not content values.** No creature, quest or recipe carries XP values yet. The profiles' per-event XP values are the targets Phase-1 content is authored against, and the tests become content-driven when that content exists.

---

## 7. Exit criteria, one by one

The criteria as rewritten by the audit (`ROADMAP.md` M2c):

| # | Criterion | Evidence |
|---|---|---|
| (a) | XP/hour inside the `AG-6` band across four scripted activity profiles | `ProgressionBalanceTests.EveryActivityProfile_StaysInsideTheAg6Band` (4 cases; 0.78×–1.00×); each profile needs at most 3 source kinds |
| (b) | A 6-hour farm at one cluster yields ≥0.10× and <0.30× level XP after saturation, while `AG-1..AG-3` change no other currency | `ProgressionBalanceTests.ASixHourFarmAtOneCluster_Saturates_WhileEveryOtherCurrencyIsUntouched`: 0.107× (8 of 75 XP per kill). Weapon-skill progress is identical with and without the guards; skills, knowledge, allocation and pools are untouched after every kill. See also `GuardTests.AG4_*` |
| (c) | No API by which gold, items or another axis's currency advances level XP, attributes or skill; knowledge enters only through a typed learning event | `NonConversionTests`: parameter allow-list, one currency per axis, plain-data currencies, and a Domain that cannot see items. `SkillAndTechniqueTests.Learning_ChangesNothingButTheKnowledgeRecord` |
| (d) | Every retained neighboring pair passes its independence test | `IndependenceTests`: level×skill, skill×technique, level×technique, attributes×skill, discipline×discipline |
| (e) | A level-up grants exactly the configured attribute points and nothing else | `LevelTests.ALevelUp_GrantsExactlyTheConfiguredAttributePoints_AndNothingElse` |
| (f) | Every historical fixture migrates to schema 4 with the documented defaults; the v4 fixture round-trips | `HistoricalFixtureTests` (v1–v4 load and migrate through the commit path), `MigrationTests.Schema3To4_AddsTheProgressionRecord_AtItsEmptyValue`, `RoundTripTests.T01_Progression_RoundTripsEveryField_ByteStable` |

**Also covered:**

- **The curve:** it sums to the published 2,448,025, and the lint rejects drift (PRG003).
- **AG-1..AG-3 row by row:** `GuardTests`.
- **The death debt:** applied once per death; a level is never lost (C17).
- **Skill mechanics:** the difficulty gate, failure rate, novelty, and ceiling.
- **Attribute grants:** the §11.3 budget.
- **The definition-ID pass over progression:** a rename, a removal reported as loss, and an unresolved skill blocking the load.
- **A malformed progression record** is corruption, not a character.

---

## 8. Known deferrals

| Item | Where it lands | Why not now |
|---|---|---|
| Mastery designations (skills past 60) | M12 | No Phase-1 character approaches the ceiling at level cap 5; building it now is outside `PROTOTYPE.md`'s list (`RK-10`) |
| Skill decay below 60 | Vertical slice | 0.5 points per world week of disuse is unobservable in a prototype session |
| Technique prerequisites (skill thresholds, known prerequisites) | M3c, M3e, M3f, with their content | Only structural checks exist until technique definitions carry requirements |
| Wiring into the runtime (commands, events, the telemetry log writer) | M3 | The simulation host is built in M3; the engine is ready to be called from it |
| Reputation, companion progression, equipment familiarity | M7, M6, with unique items | Not Phase-1 axes (`PROTOTYPE.md`); their independence tests are owed then |
| Content XP values (creature, quest, recipe) | M3b–M5 | The balance tests use reference awards until then |
| The shipped starting package | M3e, M3f | The formulas and recipes it names do not exist yet |

---

## 9. Git

- `47fb3f5`: M2c gate — the progression-axis audit and doc reconciliation.
- The M2c implementation commit follows it on `claude/phase1` (see `git log`).
- Nothing is merged to `main`; the owner merges at milestone gates.

---

## 10. Clean/dirty status

The Claude worktree (`G:\UNNAMED_CLAUDE`) is clean after the commit. The main worktree (`G:\UNNAMED`) was not touched.

---

## 11. Next milestone

Before M3, the camera/presentation reconciliation (execution brief §7):

- live first-person-only wording;
- RK-02 and D-01 measuring a representative player-camera load;
- the owner's performance ruling recorded in DECISIONS, PROTOTYPE, RISK_REGISTER and ENGINE_VALIDATION.

Then M3: player, camera, movement, interaction and world cells.
