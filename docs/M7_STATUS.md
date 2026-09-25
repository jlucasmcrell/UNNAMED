# M7 status - Factions, Reputation, and Building v1

**Date:** 2026-09-25.
**Authorization:** owner authorization of 2026-09-25 ("M7 is now explicitly AUTHORIZED").
**State:** in progress. E0 done; E1-E10 to follow in the single-agent order.

**Normative design:** `M7_IMPLEMENTATION_DESIGN.md`, with its executive brief `M7_EXECUTIVE_BRIEF.md`. Both live outside this repository, in the project history folder `G:\UNNAMED_HISTORY\M7_DESIGN_2026-09-24\`. Section references below (§N) are to that design.

**Entry:**
- M6 complete, and Phase 1 closed and merged at `a696931` (`docs/PHASE1_TECHNICAL_CLOSEOUT.md`).
- ROADMAP's entry criterion "NPCs and companions path reliably" is met inside M7, by E1 (the grid and planner) and E4 (companion routes). That is recorded here, not claimed before it is true.

## Implementation record

| Item | Value |
|---|---|
| Implementing agent | Claude |
| Worktree | `G:\UNNAMED_M7` |
| Branch | `claude/m7-factions-building` |
| Base | `main` at `a69693108b89d471cdb409a229ba0bdf78b08fa4` (Phase 1 closed) |
| Pull-request convention | One persistent draft PR, `claude/m7-factions-building` → `main`, opened after E0 is pushed and kept as a draft through E10. The agent merges nothing to `main` and creates no tag |
| Base verification (2026-09-25) | `origin/main` = `a696931`; the new worktree clean at `a696931`; no uncommitted work |
| Baseline tests at the base | 825 passed, 0 failed: Domain 147, Application 204, Persistence 173, Content 146, World 61, Presentation 57, EntityRegistry 23, Architecture 14 |
| Save schema at the base | 14. M7 migrates 14 → 15 |
| Digests at the base | `unnamed.player/v9`, `unnamed.effective-cell/v2`, `unnamed.simulation/v2`. M7: v10, v3, v3 |

## The rulings, as applied

- **Ruling 1, navigation** (2026-09-24). Recorded as `docs/DECISIONS.md` D-13. Authoritative navigation is a derived, deterministic, headless domain grid. Godot navigation is never authoritative. WORLD_ARCHITECTURE §5/§10/§11 and RK-A2, RISK_REGISTER RK-14/RK-06, PERSISTENCE §2 and SYSTEMS S-25/S-32 follow it.
- **Ruling 2, one storey** (2026-09-24). Recorded as D-14. Ground pads only, and no walkable elevated surface. An accepted-risk row is in RISK_REGISTER.
- **Rulings 3-7** (faction separation, C10 retired, no required radial, no networking, engine-independent authority). They were already recorded (§1.2). HUD_INPUT_AND_ACTIONS now makes radials optional mirrors.
- **D-04 note:** derived identities. **D-08 note:** quarter turns on a 3 m lattice (Q1).
- Every amended paragraph carries the E0 marker, a parenthesised "M7 reconciliation" with the ruling date. Checklist line 9 finds them.

## Owner decisions

| Decision | Answer | Date |
|---|---|---|
| Q1 rotation | Quarter turns on a 3 m lattice | approved 2026-09-25 |
| Q2 crime, bounty, pardon, territory gating | Deferred beyond M7; the act log and gate-access sets are the seams | approved 2026-09-25 |
| Q3 "assign an NPC to work in it" | Kera Voss walks to a player-built anvil bench as a persisted errand | approved 2026-09-25 |
| Q4 where the player may build | One content-defined build area at the crossing | approved 2026-09-25 |
| Q5 factions, proof act, gates | The Waystation and the Survey; the Animated Armour act (+100 / −100); gates at "accepted"; report-only knowledge | approved 2026-09-25 |
| Art coverage | Report player-visible `piece:*` greybox fallbacks honestly. Do **not** add `piece:*` to the allowlist, and do not weaken the Phase-A missing/broken-asset gate | 2026-09-25 |
| Dialogue tone | The drafted Kera and Sel M7 lines ship as written. A tone checkpoint is needed only if implementation materially changes their wording or adds lore | 2026-09-25 |
| R-1 tag | Not waived. At the owner-authorized M7 merge, the merge commit receives the annotated tag `m7` ("M7 — Factions, Reputation, and Building v1"). The agent creates no tag | 2026-09-25 |
| Feel test | Schedulable independently. Not an M7 blocker | 2026-09-25 |
| RAZER | The Phase-A result is the accepted baseline, and its first-use hitches are not M7 work. M7's building-segment capture is evidence when the owner has a window; it is not an entry gate | 2026-09-25 |
| Phase B | Not an input to M7. A Phase-B change that needs a gameplay-facing contract change stops M7 for an owner report | 2026-09-25 |

## Slices

| Slice | Name | State | Commits | Tests (total) | Lint definitions | Notes |
|---|---|---|---|---|---|---|
| E0 | The rulings on paper | done | `b0846a5`, `3515f03`, this commit | 825 (unchanged) | 102 | Documents only |
| E1 | Navigation you can see | - | | | 103 expected | |
| E2 | Schema 15, landed once | - | | | 103 | |
| E3 | Factions v1 | - | | | 106 expected | |
| E4 | Companion routes and opened doors | - | | | 106 | |
| E5 | Build mode: pads, walls, doorways, roofs | - | | | 113 expected | |
| E6 | Piece doors | - | | | 114 expected | |
| E7 | Navigable by construction | - | | | 114 | |
| E8 | Chest, bench, blows and mending | - | | | 116 expected | |
| E9 | Kera works at your bench | - | | | 116 | |
| E10 | Evidence and closeout | - | | | 116 | |

## E0 checklist

Run from the repository root at the E0 head (§10.6).

| # | Command | Expected | Result |
|---|---|---|---|
| 1 | `grep -c "^## D-13 " docs/DECISIONS.md`; `grep -c "^## D-14 " docs/DECISIONS.md` | 1 and 1, each dated 2026-09-24 | 1 and 1; both headings dated 2026-09-24 - **pass** |
| 2 | `grep -c "Derived identities" docs/DECISIONS.md`; `grep -c "quarter turns" docs/DECISIONS.md` | ≥ 1; ≥ 1 | 1; 1 - **pass** |
| 3 | `grep -c "Recast" docs/WORLD_ARCHITECTURE.md` | 0 | 0 - **pass** |
| 4 | `grep -n "RK-A2" docs/WORLD_ARCHITECTURE.md` | the row reads "to be proven in M7 by a seam-free domain grid" and does not say "solved" | the row reads "to be proven in M7 by a seam-free domain grid"; no "solved" - **pass** |
| 5 | `grep -c "needs the engine" docs/RISK_REGISTER.md` | 0 | 0 - **pass** |
| 6 | `grep -c "Navigation grid" docs/PERSISTENCE.md` | ≥ 1 | 1 - **pass** |
| 7 | `grep -c "navmesh dirty regions" docs/SYSTEMS.md`; `grep -c "navigation dirty rectangles" docs/SYSTEMS.md`; `grep -c "Path following state" docs/SYSTEMS.md` | 0; 1; 0 | 0; 1; 0 - **pass** |
| 8 | the ROADMAP M7 section contains "domain navigation grid", "ground pad" and "deferred" | ≥ 1 each | 1; 1; 1 - **pass** |
| 9 | files in `docs/` carrying the E0 marker | exactly the eleven documents of §10.6 | DECISIONS, GAMEPLAY_LOOPS, HUD_INPUT_AND_ACTIONS, IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS, PERSISTENCE, the content bible, PROTOTYPE, RISK_REGISTER, ROADMAP, SYSTEMS, WORLD_ARCHITECTURE (11) - **pass** |
| 10 | `test -f docs/M7_STATUS.md && grep -c "E0 checklist" docs/M7_STATUS.md` | ≥ 1 | 2 - **pass** |
| 11 | `grep -c "tests/Application.Tests/GameSaves/" AGENTS.md` | ≥ 1 | 1 - **pass** |
| 12 | `git diff --stat a696931 -- src content ':(glob)tests/**/*.cs'` | empty | empty - **pass** |

**Owner sign-off:** given in advance. The owner's authorization of 2026-09-25 says "This authorization counts as the owner's E0 go-ahead", conditional on the checklist passing and the tests staying green.

## Scope ledger

Every M7 type, command, event, content item and test maps to a ROADMAP M7 phrase or a design row. Deviations are listed here as they arise. None so far.

## Local risks (not promoted to RISK_REGISTER)

| Risk | Where it is tracked |
|---|---|
| Tier hysteresis is unsaved but gates the companion and creatures (owed before M9) | §15, R-X18 |
| The companion's conversation hold reads the transient conversation (pre-existing) | §2.16 |
| Kera's walk-home plan is modelled at about 4.6 ms on ASTRAL, one plan over the 4 ms tick | §3.18, §14; measured in E9 |

## Residues and deferrals

See §5.19 and §16. They are recorded here as each slice lands.
