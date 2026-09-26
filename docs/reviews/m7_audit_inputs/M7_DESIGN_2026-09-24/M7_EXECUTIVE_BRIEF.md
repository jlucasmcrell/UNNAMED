# Otherreach M7 - Executive Brief

**M7:** Factions, Reputation, and Building v1 (Phase 2).
**Dates:** designed 2026-09-24; reconciled to the closed Phase 1 on 2026-09-25.
**Status:** design complete and ready for implementation authorization. Nothing in the repository was changed, and M7 is not authorized.
**Design base:** canonical `main` at `a69693108b89d471cdb409a229ba0bdf78b08fa4` (`a696931`), Phase 1 closed. Every source citation in the design is verified at this commit.
**Save schema:** 14 at the base. M7 migrates **14 → 15**.
**Digests:** the base is `player/v9`, `effective-cell/v2`, `simulation/v2`. M7 moves them to **v10, v3 and v3**.
**Full design:** `M7_IMPLEMENTATION_DESIGN.md` (18 sections), in this folder.

## What M7 delivers, in play

Everything happens in Ashen Hollow; Kaldrun Reach and Vessmere stay M9.

1. **Building.** The player takes timber from a stack by the crossing, presses **B**, and snaps ground pads, walls, a doorway, a door, a roof, a storage chest and an anvil bench onto a 3 m grid. The build area sits over the corner where four 100 m cells meet, so the workshop straddles a cell seam.
2. **Navigation.** Tavar follows the player around the new walls and opens doors. When his breadcrumb trail fails, he plans a route.
3. **A worker.** Kera Voss can be assigned to the player-built bench. She walks from the smithy, through the player's doorway and across the seam, to the bench, and walks home when released. She reroutes if a new wall blocks her way.
4. **Walls behave like walls.** A player-built wall stops talking, trading and assigning through it, just as authored walls do in Phase 1.
5. **Damage.** Melee blows damage pieces. Timber repairs them, and dismantling refunds half.
6. **Factions.** Destroying the Blackvein Animated Armour is one act that two factions read in opposite directions:
   - The **Waystation** (Renn, Kera) approves.
   - The **Survey** (Sel) disapproves.
   - A faction learns only when the player tells one of its members.
   - Standing opens exactly two things: Kera's iron billets and Sel's notes.
7. **Saves.** Everything survives save and load. The proof is the live reconstructed-state comparison Phase 1 established (956 fields at the Phase-1 baseline, 0 differences). Replays are deterministic.

## Owner decisions

- **Q1-Q5 were approved on 2026-09-25:**
  - Q1: quarter-turn rotation.
  - Q2: crime, bounty, pardon and territory gating deferred.
  - Q3: Kera's walk to the player's bench is the work-assignment proof.
  - Q4: one crossing build area.
  - Q5: the Waystation and the Survey, report-only knowledge, the Animated Armour act.
- **The RAZER ruling** accepts the measured Phase-A result as the baseline. The first-use hitches are Phase-1 follow-ups, not M7 work.
- **Remaining design decisions:** none that block. One minor choice with a default:
  - The Phase-A art-coverage report will count player-built greybox pieces as `piece:*` fallbacks, because M7 ships no piece art.
  - Default: report the count in M7's evidence, and don't allowlist it. The allowlist's own rule forbids allowlisting what a player sees in normal play.
  - The alternative is a scoped allowlist exception. The code is the same either way.

## What remains before implementation (owner process, not design)

- **Authorize M7, and name the agent, worktree, branch and draft-PR convention.** `AGENTS.md` still scopes Claude "through M6", and `docs/M6_STATUS.md` still says M7 is not authorized.
- **The R-1 tag.** No git tags exist. At the M7 merge: tag, or waive.
- **Dialogue tone review.** The new lines are drafts and ship as written unless you object before E10.
- **The feel test** is now schedulable: its Windows-build precondition is met. It is not an M7 blocker.

## What changed in the reconciliation to the closed Phase 1

- **Persistence.**
  - Schema 14 now belongs to Phase 1, which added creature continuation and a noise list. M7 is **14 → 15**: it freezes the schema-14 shapes into `SchemaV14.cs`, repoints four earlier steps, and carries the noise list forward.
  - The effective-cell and simulation digests go to v3, because Phase 1 already used v2. The player digest's v10 is unchanged.
  - M7 no longer writes an M6 save. It extends the committed Phase-1 test of the real M6 acceptance save.
- **Already done by Phase 1, so dropped from M7:**
  - the any-body door-close refusal on authored doors;
  - the chest-crash fix;
  - moving save writing off the main thread.

  M7 keeps only the piece-door half, through a new shared `BodyIn` helper.
- **New Phase-1 behaviour M7 must follow:**
  - talk and trade stop at walls (placed walls now join that check);
  - charges stop on every NPC, so Kera can stun a charging boar;
  - saves are captured on the frame and written in the background, so M7's saved records must be immutable (new guard G29);
  - presentation observers are isolated, so every M7 test asserts `SubscriberFailures == 0`;
  - one modal input rule: build mode is a gameplay mode that exits whenever a panel opens, and every key label follows the keyboard layout.
- **Evidence.**
  - Save/load proof uses the 956-field live comparison.
  - Performance proof uses the extended route Phase 1 built, plus one new `building` segment appended at the end.
  - M7's RAZER capture of that segment is evidence, not an entry gate.
- **Content.** Since Phase 1's Iron Under Ash fix, a billet bought from Kera counts toward Quest 1's ore and billet steps. The purchase needs the armour kill and a report first, and the spear still needs the anvil. It is recorded as a known interaction, not a design change.
- **Citations.** All 580 file citations were remapped to `a696931` and range-checked.

## The design calls that matter most (unchanged)

1. **Rulings recorded first.** E0 writes rulings 1 (domain navigation) and 2 (one storey) into the normative docs before any code. They are still recorded nowhere in the repository.
2. **Navigation.**
   - A derived 250 mm integer grid with deterministic A*.
   - Never saved; rebuilt at load, and locally on each structure edit.
   - Seams do not exist in the data.
   - Godot Navigation is banned outside the old perf spike.
3. **Movers keep what works.**
   - `Kinematics.Step` still does all collision.
   - Tavar keeps his trail, and M6's companion guarantee C16 must pass unmodified.
   - Creatures stay unpathed.
4. **One storey.** "Floors" are ground pads on the terrain. There is no walkable elevated surface.
5. **Building.**
   - Snap assembly with fifteen ordered checks.
   - The ghost preview runs the same validator.
   - Sealed rooms are refused ("rooms need a doorway").
   - One saved row per piece, with replay-stable IDs.
6. **Factions.**
   - Never a morality meter and never psychic. Standing gates access only, and code that decides who attacks can't read it (an architecture test enforces this).
   - Personal trust and faction standing never touch.
7. **Smallest honest proofs.** One damage source, one authored timber stack, one build area, two factions, two gates. No asset work: pieces are code-built greybox.

## Plan

Eleven vertical slices. Each ends merged, green and playable.

| Slice | Delivers |
|---|---|
| E0 | Rulings and reconciliation written; `M7_STATUS` |
| E1 | Navigation grid, planner, F2 overlay, `--build-shots` harness |
| E2 | Schema 15, landed once |
| E3 | Factions v1 |
| E4 | Companion routes and door opening |
| E5 | Build mode, walls blocking talk and trade, the `building` perf segment |
| E6 | Piece doors |
| E7 | Navigability check |
| E8 | Chest, bench, damage, repair |
| E9 | Kera works at the player's bench |
| E10 | Evidence and closeout |

- **Critical path:** E0 → E1 → E2 → E4 → E5 → … → E10. E3 can run alongside.
- **Acceptance:** 37 criteria, each decided by a named test or run.
- **Test baseline:** Phase 1 closed with 825 tests.

## Risks to watch

- **Scope creep.** ROADMAP still names more than M7 builds.
- **A companion regression** from route mode and door opening.
- **Kera's walk-home plan** is one tick over the 4 ms budget on ASTRAL. This is accepted residue, and a per-tick plan budget is the lever if RAZER shows a hitch.
- **Pressure for more than one storey** after playtest. The answer stays "more pieces, not physics".
- **The art-coverage report** will show a non-zero `piece:*` count until piece art exists.

## Supporting material

- `M7_POST_PHASE1_RECONCILIATION_PREP.md`: the analysis of the Phase-1 audit remediation's impact.
- `recon/`: the reconciliation rulings, the citation remap and its report.
- `research/`, `drafts/` (including `drafts/history/`, the pre-reconciliation versions), and `drafts/audit/`.
