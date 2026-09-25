# M7 status - Factions, Reputation, and Building v1

**Date:** 2026-09-25.
**Authorization:** owner authorization of 2026-09-25 ("M7 is now explicitly AUTHORIZED").
**State:** **stopped in E1 for an owner decision** (2026-09-25): N-A10 measured an authored-pair route over the E1 bound of 43,690 expansions. See "STOP - E1, N-A10" below. E0 done; E1 commits 1-2 done and commit 3 partly done; nothing after it has started.

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
| E0 | The rulings on paper | done | `b0846a5`, `3515f03`, `13a5cca` | 825 (unchanged) | 102 | Documents only. Draft PR #8 CI green (`build-and-test`, run 36178835111) |
| E1 | Navigation you can see | **stopped** (N-A10) | `521d7d0` (E1.1), `f279a4d` (E1.2), E1.3 in part | 857: Domain 161, Application 205, Persistence 173, Content 160, World 64, Presentation 57, EntityRegistry 23, Architecture 14 | 103 | Done: the Domain grid and planner (N-D1, N-D3-N-D6, N-D9, N-D11-N-D16, N-D21, N-D22); `config.navigation` and NAV001-NAV007 (N-X2, N-X3, `NavConfigDefault_IsTheShippedFile`); the `Navigation` slice, `NavigationSystem`, `SimulationSetup.Navigation`, `Simulation.Navigation`, `GameSession.Boot` (N-W1-N-W3, N-A1). Not done: N-A10 (the STOP), the E1 architecture guards, commit 4 (overlay, panel, `--build-shots`) |
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

## STOP - E1, N-A10 (2026-09-25)

**What fired.** §10.7's E1 STOP: "N-A10 on ASTRAL shows ... a route over 43,690 expansions". Measured on ASTRAL (AMD Ryzen 9 9950X3D), three authored pairs exceed it. The other three E1 N-A10 conditions hold.

**The implementation matches the design's model.** The same routes give the same numbers, node for node:

| Route (person, opener) | Model (§3.7.7) | Measured |
|---|---|---|
| West of the lodge (30, 128) → Renn | 9,709 expansions, 5 corners | 9,709 expansions, 5 corners |
| Kera's site → (100.75, 96.0), no workshop | 1,179 expansions, 4 corners, 76.9 m | 1,179 expansions, 4 corners, 76.935 m |

N-D9(e) also checks the A* against an independently written reference over a monolithic raster (50 pairs across both seams: identical expansions and corners). So the counts below are properties of the shipped hollow and of optimal A* as §3.7.4 specifies, not of the code.

**N-A10 as measured** (the E1 part: the full build, and every ordered pair of authored protected points no more than 88 m apart on either axis):

| Measure | E1 STOP bound | Release | Debug (as CI runs) | Result |
|---|---|---|---|---|
| Full build of the four tiles (median of 7) | ≥ 20 ms | 1.05 ms | 2.25 ms | holds |
| Authored pairs not `Found` | any | 0 of 180 | 0 of 180 | holds |
| Mean plan over the 180 pairs | ≥ 2 ms | 1.35 ms | 3.23 ms | holds in Release; over in Debug |
| Largest expansion count | > 43,690 | 52,985 | 52,985 | **fires** |

The pairs over 43,690 expansions:

| Pair | Expansions | Release ms |
|---|---|---|
| `node iron_seam` → Renn Vale's place | 52,985 | 14.4 |
| `container.den_cache` → Kera Voss's place | 46,942 | 10.9 |
| `container.den_cache` → `station.forge_hearth` | 44,464 | 10.1 |

22 of the 180 pairs take more than 16,000 expansions. The median plan takes 0.44 ms in Release.

**The point set used.** The design gives no plan endpoint for a protected point a body cannot stand on, so this reading was taken, as a test definition and not a game rule. The endpoints are the 20 authored protected points of §3.13 at a new game:
- the spawn;
- the four NPC places;
- the three containers;
- the two stations;
- the two resource nodes;
- the four switches;
- both approach points of each of the two doors.

Each point is used where it stands when a person can stand there. Otherwise the endpoint is the nearest walkable node within the point's reach: the iron seam's rock face, the switch stones. Plans run for a person with every gate passable, as §3.13's graph does. No reading of "authored protected points" leaves out the den cache (a container) or Kera's place (an NPC site), so the over-bound pairs remain under any reading.

**Why the design's figure differs.** §3.7.7 took the working papers' worst authored route, west of the lodge → Renn (9,709), and §3.18 states "every authored pair ... ≤ 43,690". The model does reproduce that route exactly, but it does not appear to have evaluated every pair. The detour routes are the expensive ones: the quarry to the lodge's east door, and the den to the smithy's west door. They expand an ellipse of open ground, as §3.7.7 describes for Kera's workshop routes.

**A forward risk found with it: the cost per expansion.**
- §3.18's timings assume about 0.1 µs per expansion [bench].
- Measured in Release after optimising the search: about 0.27-0.29 µs per expansion overall, plans included. That counts JIT-optimised hot loops and a cached tile lookup, both of which leave the results unchanged.
- The worst pair takes 14.4 ms. Kera's walk home (40,751 expansions [model]) would take about 11-12 ms at that rate, against E9's STOP bound of 6 ms. A plan at the 65,536 cap would take about 18 ms.
- This is not an E1 STOP, since the E1 mean holds in Release. It is recorded now because E9 would meet it.

**What the owner is asked to decide** (nothing is changed until then):
1. **The authored-pair bound.**
   - (a) Keep optimal A* and the 65,536 cap, and restate N-A10's authored-pair bound as "all `Found` within the cap". The two-thirds-of-the-cap bound would then apply to the M7 movers' own routes: Kera's workshop routes, and the companion's plans, which the 30 m catch-up keeps short. The three pairs are recorded as residue.
   - (b) Raise the bound, which would also mean raising the 65,536 scratch ceiling. §3.18's rule of 1.5 × the worst real route would ask for about 79,500.
   - (c) Change the search, for example to an admissible landmark heuristic that keeps routes optimal. That is a design change to §3.7.4.
2. **The speed target.** Whether 6 ms for Kera's walk home on ASTRAL stands. More optimisation that leaves results unchanged is possible (a flat window walkability array, packed heap keys), but 0.1 µs per expansion is unlikely in safe C#.

## Scope ledger

Every M7 type, command, event, content item and test maps to a ROADMAP M7 phrase or a design row. Deviations and as-built readings are listed here as they arise.

| Slice | Item | Reading or deviation |
|---|---|---|
| E1 | `NavigationLayout` (`src/World/Runtime/Navigation.cs`) | A public helper, not in §3.14's file list. It gives a region's tile keys and authored inputs, so NAV006 (Content) and `NavigationSystem` build the same grid from one reading |
| E1 | `NavGeometry.DistanceSquaredTo` | Takes a box only: a circle's squared distance to its edge needs a square root. Distance tests against any footprint go through `NavGeometry.Within(shape, x, z, d)`, exact in integers |
| E1 | Expansions | Every node popped counts, the goal among them, as the design's model counts (9,709 and 1,179 reproduced). `Budget` returns with exactly `max_expansions` |
| E1 | The window of an early outcome | `StartBlocked`, `GoalBlocked` and `TooFar` have no planning window. Their `Window` (the route's watch) is the ends' node rectangle inflated by `window_margin_m`, clipped to the grid when anything is left of it |
| E1 | N-A1's workshop-doorway lanes | Asserted once the workshop's doorway piece exists (E5); the lodge, smithy, fence-gap and beam lanes are asserted in E1 |
| E1 | NAV lints on a malformed `config.navigation` | Reported as NAV007 |

## Local risks (not promoted to RISK_REGISTER)

| Risk | Where it is tracked |
|---|---|
| Tier hysteresis is unsaved but gates the companion and creatures (owed before M9) | §15, R-X18 |
| The companion's conversation hold reads the transient conversation (pre-existing) | §2.16 |
| Kera's walk-home plan is modelled at about 4.6 ms on ASTRAL, one plan over the 4 ms tick | §3.18, §14; measured in E9 |

## Residues and deferrals

See §5.19 and §16. They are recorded here as each slice lands.
