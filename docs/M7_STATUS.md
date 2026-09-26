# M7 status - Factions, Reputation, and Building v1

**Date:** 2026-09-25.
**Authorization:** owner authorization of 2026-09-25 ("M7 is now explicitly AUTHORIZED").
**State:** STOPPED again at E8.5 (S9; 2026-09-26). The owner ruled option (a) on the first E8.5 STOP, and schema 16 now saves the character's vitals (`1dd66e7`, CI green). With it the vitals are equal across the save, but step 10 still differs: at the save a wolf is one tick from biting, and a creature's attack in progress is, by the documented policy, not saved. See "STOP - E8.5 (second), S9". E0-E7 are done; E8.1-E8.4 and schema 16 are done and pushed; E8.5 is written and not committed. Every earlier STOP was resolved by an owner ruling, recorded below.

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
| E1 N-A10 STOP: expansion bound | Option (a). Keep optimal A* and `max_expansions: 65536`. The exhaustive authored-pair sweep is diagnostic: every pair `Found` within the cap, no two-thirds bound, expensive pairs recorded as residue. The two-thirds (1.5× headroom) rule applies to actual M7 mover routes: Kera's workshop routes, companion routes | 2026-09-25 |
| E1 N-A10 STOP: speed target | The 6 ms walk-home STOP is superseded. Actual M7 mover plans on ASTRAL in Release: ≤ 15 ms target, > 20 ms STOP; also STOP on a repeatable, player-visible planning hitch. Diagnostic pairs are measured and reported, never the mover criterion. Escalation: profile, optimize without changing results, re-measure, then the per-tick plan budget seam; any algorithmic change needs owner review | 2026-09-25 |
| E2.3 STOP (S9): the fixture pack's `config.navigation` | Option (b). The current fixture pack omits the optional `config.navigation`, so `NavigationContent.Build` uses `NavConfig.Default`, which `NavConfigDefault_IsTheShippedFile` pins to the production file. The writer pack `content-0.1.7` keeps its copy; production `content/config/navigation.yaml` is unchanged; NAV001-NAV007 are unchanged. Options (a), completing the fixture's combat and magic content, and (c), skipping the NAV lint without a tick, are rejected. The ruling covers this optional file only; it is not general permission to omit a required fixture definition | 2026-09-25 |
| E2.4 STOP (S9/S10): the M6 save's step count | Option (a). The schema-12 save traverses four schema versions and executes three migrations: `Steps.Count == 3`, in order `schema 12 -> 13:`, `schema 13 -> 14:`, `schema 14 -> 15:`, asserted exactly. §7.14's "the fourth of four" was a counting error, corrected to "the third of three". M7 still has exactly one new migration, `SchemaV14ToV15`; no other step, and no save-format change beyond schema 15, is authorized | 2026-09-25 |
| E3 STOP (S5): `m7_armour` | Option (a): tune the runtime beat only - a mending stop, and an approach to the sentinel's rear using movement the authoritative perception rules treat as quiet. Nothing about the sentinel, combat, the character, content, factions or the encounter changes; no teleport, disabled hearing, paused AI or forced awareness. Pre-authorized fallback to option (b) if one reasonable tuned version still dies, or if the rules make an undetected rear approach impossible. The Phase-1 async-save flake stays a recorded local risk, not an E3 edit | 2026-09-26 |
| E8.5 second STOP (S9): a creature's attack in progress; the AsyncSave finding | Option (a): persist the minimum authoritative state that continues an ordinary creature attack in progress deterministically, as schema 16 -> 17 (schema 16's meaning and bytes unchanged; older saves migrate as "no attack in progress", a documented limitation). Trace the actual attack path; no animation, VFX, audio, presentation timers or runtime objects; the character's own transient actions stay excluded unless a separate equality failure proves otherwise. PERSISTENCE.md distinguishes transient state (not persisted) from authoritative state whose omission changes deterministic continuation (persisted when the owning system requires it), not broadened beyond this case in M7. A restored attack whose target cannot validly be restored follows an explicit, tested load rule. `CrossingWorkshop_10` passes unchanged: no moved save, removed wolf, weaker equality, "nothing attacking" precondition, altered timing or combat reset on load. AsyncSave: an isolated single-writer lane (one long-lived worker, not a thread per save, no parallel writers), with ordering, atomic writes, profile locking, queue semantics, shutdown, and error propagation kept; the 5 s test budget kept and proven under the parallel suite; if it still fails, STOP with measurements. Then finish E8.5, E9 and E10; no M8, merge or tag | 2026-09-26 |
| E8.5 STOP (S9, S2): the character's regeneration clocks | Option (a): correct persistence of the player's regeneration continuation state, by a versioned schema change and migration (a narrow exception to the E2.4 restriction). Not to be hidden: no moving the save out of combat, removing the wolf, a resting precondition, a weaker equality assertion or a changed workshop sequence. Trace the whole pool-continuation state; save what continuation needs, not the whole runtime object; keep the rates and cooldowns; keep the documented policy for attacks and actions in progress separate, and if it conflicts with an equality guarantee, name the case and state the contract honestly. Schema 16, deterministic defaults for older saves (historical timing cannot be reconstructed), fixtures byte-for-byte, the new fixture by the procedure, migration counts reconciled with their order kept. First a focused regression showing the failure. Investigate the AsyncSave failure honestly. Then finish E8.5 through E10; do not stop at the fix; no M8. Coordinate with Claude 1 (the Ashen Hollow visual demo, `G:\UNNAMED_PHASEB`, not to be modified) and Codex B/C (review and QA tooling; no competing graphical captures, no killing another task's process) | 2026-09-26 |
| E5.2 STOP (S1): BLD006 | Option (b). BLD006's "iff" is amended: `config.building` is required when any piece or build area exists; when present it is validated in full, even in a pack with nothing to build, and a valid inert one is allowed. The converse is not required. `ProgressionContentTests.ACopyOfTheGameConfig_Lints` is not edited (§2.18, §12.10). §4.20 amended; no other building rule changes. Tests prove the three cases | 2026-09-26 |

## Slices

| Slice | Name | State | Commits | Tests (total) | Lint definitions | Notes |
|---|---|---|---|---|---|---|
| E0 | The rulings on paper | done | `b0846a5`, `3515f03`, `13a5cca` | 825 (unchanged) | 102 | Documents only. Draft PR #8 CI green (`build-and-test`, run 36178835111) |
| E1 | Navigation you can see | done | `521d7d0` (E1.1), `f279a4d` (E1.2), `7766e95` and `9e2a7f1` (E1.3), `355fda5` (E1.4) | 867: Domain 161, Application 206, Persistence 173, Content 160, World 64, Presentation 57, EntityRegistry 23, Architecture 23 | 103 | Stopped at N-A10, resolved by the owner's ruling the same day (below). See "E1 evidence" |
| E2 | Schema 15, landed once | done | `d17a67e` (E2.1), `f7931e1` (E2.2), `e5f221d` (E2.3), `fd12b6c` (E2.4), `8ff394b` (E2.5) | 899: Domain 162, Application 209, Persistence 198, Content 160, World 67, Presentation 57, EntityRegistry 23, Architecture 23 | 103 | Stopped at E2.3 (S9) and E2.4 (S9/S10), both resolved by the owner the same day. See "E2 evidence" |
| E3 | Factions v1 | done | `a36d286` (E3.1), `c7e088d` (E3.2), `8dea83a` (E3.3), `f922db0` (E3.4), `c4c8f9d` (the S5 STOP record), `ee3d18a` (E3.5 and E3.6, one commit) | 952: Domain 169, Application 229, Persistence 198, Content 183, World 67, Presentation 57, EntityRegistry 23, Architecture 26 | 106 | Stopped at `m7_armour` (S5), resolved by the owner (option (a), the beat tuned). See "E3 evidence" |
| E4 | Companion routes and opened doors | done | `ebf1012` (E4.1), `e1686e7` (E4.2), `d7acca4` (E4.3), `9d76d77` (E4.4), and the status commit | 962: Domain 175, Application 232, Persistence 198, Content 183, World 68, Presentation 57, EntityRegistry 23, Architecture 26 | 106 | No STOP. Closes criterion 5 (N-D10 with E1's N-D9 and N-W1); criterion 7 has all but N-A12 (E5). See "E4 evidence" |
| E5 | Build mode: pads, walls, doorways, roofs | done | `9361019` (E5.1), `373e874` and `137aef8` (the S1 STOP record), `1638b06` (E5.2), `47a9e1f` (E5.3), `aad371f` (E5.4), `7509c36` (E5.5), and E5.6 with this status | 1,009: Domain 182, Application 263, Persistence 198, Content 186, World 68, Presentation 57, EntityRegistry 23, Architecture 32 | 113 | Stopped at E5.2 (S1), resolved by the owner (option (b), BLD006 corrected). See "STOP - E5.2, S1", the ruling after it, and "E5 evidence" |
| E6 | Piece doors | done | `d18da45` (E6.1), and E6.2 with this status | 1,013: Domain 183, Application 266, Persistence 198, Content 186, World 68, Presentation 57, EntityRegistry 23, Architecture 32 | 114 | No STOP. See "E6 evidence" |
| E7 | Navigable by construction | done | `881dc98` (E7.1), `0aa235f` (E7.2), `9ff759c` (E7.3), and E7.4 with this status | 1,018: Domain 185, Application 269, Persistence 198, Content 186, World 68, Presentation 57, EntityRegistry 23, Architecture 32 | 114 | No STOP. See "E7 evidence" |
| E8 | Chest, bench, blows and mending | stopped at E8.5 (second) | `194bab7` (E8.1), `f776dfd` (E8.2), `6c5efef` (E8.3), `fb16e33` and `8573229` (the planner, results unchanged), `fb525f7` (E8.4), `1dd66e7` (schema 16, the owner's ruling); E8.5 written, not committed | 1,046 at `1dd66e7`, all passing in a clean worktree; 1,035 at E8.4: Domain 186, Application 285, Persistence 198, Content 186, World 68, Presentation 57, EntityRegistry 23, Architecture 32 | 116 | Stopped at E8.5 (S9, S2), resolved by the owner (option (a), schema 16); stopped again at E8.5 (S9). See "STOP - E8.5 (second), S9" |
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

## E1 evidence (2026-09-25, ASTRAL)

| Proof | Result |
|---|---|
| `dotnet build src/UNNAMED.sln` | 0 errors; no Presentation warning. The build's remaining warnings are the pre-existing ones in `MagicContent.cs`, `PickUpItemTests.cs`, `ReachabilityTests.cs` and `SaveFailureTests.cs` |
| `dotnet test src/UNNAMED.sln` | 867 passed, 0 failed |
| Content lint | 103 definitions, 0 errors |
| `--smoke` (headless) | PASS; its save/load digest identical |
| `--quit-after 300` (headless) | exit 0 |
| `--build-shots` (windowed) | exit 0, 0 subscriber failures. b01 in 250 ticks: 276 nodes of the five smithy walls drawn unwalkable, 1,841 quads within 24 m, `door.forge_shed` drawn shut (red). b02 in 487 ticks: the 576 nodes within 3 m of (100, 100), columns and rows 388-411, in 4 tiles, all walkable, none drawn twice. `piece:*` greybox fallbacks: 0 (no pieces exist yet) |
| `--playthrough` then `--playthrough-verify` | every beat passed (378 s); verify: 956 fields compared, 0 differences |
| A second `--playthrough` | exit 0; `state_replay.json` byte-identical, SHA-256 `f7d79bf0b813a35112866279ea7806c69d1c38344051773d9176008b20d04868` |
| `--ui-shots`, `--delta-shots` | both exit 0 (299 s, 161 s), 0 error lines in either log. The headless smoke log carries 911 `keyboard_get_keycode_from_physical` "Not supported by this display server" lines from `HelpPanel.Key` in the HUD prompt; that call is unchanged from the base commit and the headless display server has no keyboard layout, so they are pre-existing headless noise, not an E1 change |
| N-A10 on ASTRAL, Release | full build median 1.05 ms (target < 20); 180 authored pairs all `Found`, mean 1.35 ms (target < 2), largest 52,985 expansions (diagnostic, recorded residue); the model's routes reproduced exactly: west of the lodge → Renn 9,709 expansions, Kera → (100.75, 96.0) 1,179 |

The worktree has no asset workspace (`assets/` is generated elsewhere and is not an input to M7), so every E1 run drew greybox; the art-coverage gate counts, never fails, and its reports are in each run's directory.

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

## Owner ruling on the E1 STOP (2026-09-25)

The owner resolved the STOP as a correction to the design's benchmark, not as an implementation failure. The reasons given:
- the implementation reproduces the model exactly on the routes the model covered;
- the monolithic-raster reference agrees with it;
- all 180 authored pairs are `Found` under the 65,536 cap;
- the 43,690 authored-pair ceiling came from incomplete modelling, not from a gameplay requirement.

**The bounds now in force** (the design's §3.18, §10.7, §10.15, §11 criterion 33, §14 and §15 R-A3/R-X11 were amended on 2026-09-25; the pre-ruling copy is kept in the design's `drafts/history/`):

| Kind | Bound | Kind of check |
|---|---|---|
| Authored protected-point pairs (N-A10's exhaustive sweep) | every pair `Found` within `max_expansions` (65,536); mean < 2 ms in Release; every count and time reported | diagnostic coverage |
| Actual M7 mover routes: Kera's three Crossing Workshop routes (E9), companion Nav plans (E4) | `Found` in ≤ 43,690 expansions (two thirds of the cap, the 1.5× headroom rule); plan ≤ 15 ms on ASTRAL in Release as the target, > 20 ms a STOP | mover budget |
| Runtime | a repeatable, player-visible hitch attributable to planning that materially exceeds the frame-time expectations is a STOP | runtime evidence |
| Unchanged | the 250 mm grid, optimal A*, the 65,536 cap, the tie-breaking, route persistence, navigation authority, and the cheap follower and rebuild budgets | - |

**Recorded residue: the expensive diagnostic pairs** measured at the STOP. All three are `Found`, and none is a mover route.

| Pair | Expansions |
|---|---|
| `node iron_seam` → Renn Vale's place | 52,985 |
| `container.den_cache` → Kera Voss's place | 46,942 |
| `container.den_cache` → `station.forge_hearth` | 44,464 |

## STOP - E2.3, S9 (2026-09-25)

**What fired.** §10's S9: "A part section's rule cannot be implemented as written without inventing a rule, or two sections of this document contradict each other on something the slice needs". §7.11 has the current fixture pack (`tests/Persistence.Tests/Fixtures/content`, 0.2.8 → 0.2.9) add `config.navigation` with the game's values, and says the pack "passes every check that exists then". It cannot, as written:

- §3.16's NAV007 requires "every time ≥ one tick", and `config.navigation` states its times in seconds (`replan_min_gap_s`, `retry_s`, `stuck_replan_s`, `blocked_view_s`). `NavigationContent` converts them through the tick, which the repository reads only from `config.time` (`WorldContent.TickMilliseconds`; Domain, World and Application have no other tick source).
- The fixture pack has no `config.time`. With `config.navigation` added, its lint reports NAV007 "config.navigation is malformed: config.time is missing", so `Fixtures.Content()` refuses the pack and every Persistence test that loads it fails.
- Adding `config.time` to the pack does not help: it switches on the combat and magic checks, which a pack without a tick skips (`CombatContent.cs:18-19`, `MagicContent.cs:25`). The pack then fails CMB001 "creatures: 'attack_set' must be a list" and MAG001 "spells: 'domain' is missing". The fixture creatures (`deer`, `wolf_grey`, `ash_ember_hound`) and `spell.ember.bolt` are stubs; `deer` and `bolt` have no game counterpart.

**The readings.**

| Reading | What changes | Cost |
|---|---|---|
| (a) Fix the pack to the letter | Add `config.time`, then give the fixture creatures and spell full combat and magic definitions, and whatever those checks pull in (abilities, loot tables, the combat and magic configs) | Invents creature, ability and spell content for a save-migration fixture. The mirror grows well past §7.11's 12 IDs. Large and open-ended |
| (b) The pack omits `config.navigation` | The current pack adds 11 of §7.11's 12 IDs. Without `config.navigation`, `NavigationContent` uses `NavConfig.Default`, which equals the game's `navigation.yaml` (E1's `NavConfigDefault_IsTheShippedFile`). NAV004-NAV006 still run on the pack, against the default. The writer pack `content-0.1.7` keeps its `config/navigation.yaml` (writer packs need not pass today's checks; only their hash matters) | Smallest. Verified diagnostically: the pack without it validates with 0 errors. Deviates from §7.11's list by one file |
| (c) The NAV lint skips a pack without a tick | `NavigationContent` treats a pack without `config.time` the way combat and magic do: no tick, so no navigation to check | Changes a lint to fit the pack, which §7.11 forbids ("the pack is fixed, never the lint") |

**Recommendation.** (b). It keeps the rule "the pack is fixed, never the lint", adds no invented content and changes no lint. The design amendment would read, in §7.11's current-pack list: "`config.building` and `config.factions`, not `config.navigation`: its times need `config.time`, and `config.time` switches on checks that the fixture's stub creatures and spell do not meet. The pack navigates with `NavConfig.Default`, the shipped values." The mirror then gains 11 IDs.

**State at the STOP.** E2.1 (`d17a67e`) and E2.2 (`f7931e1`) are committed and pushed; E2.1's CI is green. E2.3 is in progress and uncommitted in the worktree. Its source builds; the packs, the probe rows and the test edits are applied; and the writer pack's hash is computed. A patch of the in-progress work is at `G:\UNNAMED_HISTORY\M7_DESIGN_2026-09-24\drafts\wip\E2.3_wip_2026-09-25.patch`. Nothing of E2.3 is committed or pushed. The design is not amended.

## Owner ruling on the E2.3 STOP (2026-09-25)

The owner chose reading (b). As applied:

- `tests/Persistence.Tests/Fixtures/content/config/navigation.yaml` is not in the current pack (0.2.9), and the probe mirror (`M2Fixtures.Historical.CurrentContent()`) gains 11 IDs, not 12. `CurrentContentHash` is `sha256:9820bc73fe59078cb57d2ca56019cdde0faa3fd527c5c7aecfa92aadb01cc1c6`, computed over the pack as committed.
- `content-0.1.7/config/navigation.yaml` stays. `WriterContentHash` is `sha256:8598b534627cd78459b1f3a2eba40dde566513ba6bfbd44137b63c34393f42e6`.
- `content/config/navigation.yaml` and NAV001-NAV007 are unchanged. `NavConfigDefault_IsTheShippedFile` stays green, so the default the fixture navigates with is the production file's effective configuration.
- The current pack loads with 0 content errors. For comparison, before the ruling, the pack with `config.navigation` failed NAV007, and with `config.time` added it failed CMB001 and MAG001.
- The design's §7.11 is amended in the history folder (backup `drafts/history/M7_IMPLEMENTATION_DESIGN.before_E2_S9_ruling_2026-09-25.md`). The current-pack list no longer names `config.navigation` and records the omission with its reason; the mirror is 11 IDs; the principle "the pack is fixed, never the lint" gains the owner's clarification (a fixture carries the content it exercises, not optional files that switch on unrelated domains); and an owner-ruling paragraph preserves the original text and this STOP's evidence.

## STOP - E2.4, S9 and S10 (2026-09-25)

**What fired.** S9, "two sections of this document contradict each other on something the slice needs", and S10, "a number this document states as asserted differs from the implementation". §7.14 specifies the new `TheM6AcceptanceSave_LoadsIntoM7_NothingBuiltNeutralNoErrand` as asserting that "`Report.Steps` ends with a step starting `"schema 14 -> 15:"`, the fourth of four". The same document states the save's path as "12 → 13 → 14 → 15" in seven places (§1 item 169, §7.14 twice, §10 purpose, §11 criterion 20, §12.6, R-B2). That path is three steps, and the loader reports three.

**Measured.** Loading the committed `m6_acceptance` save (schema 12) reports `Steps.Count == 3`: `schema 12 -> 13`, `schema 13 -> 14`, `schema 14 -> 15`. With the count read as 3, every other E2 row of the test holds, checked diagnostically:
- no transition, mismatch, blocker, loss or alias; the manifest's fingerprint equals the session generator's;
- no piece, sequence 0, no errand for Kera, and Kera at her authored site;
- the empty ledger;
- Tavar with `NavRoute.None` and his saved trail;
- `pre_migration_12_manual_acceptance` byte-identical to the committed save after the first save;
- 0 `StateDump.Compare` differences on reload, and no subscriber failure.

**The readings.**

| Reading | The assertion | Consequence |
|---|---|---|
| (a) The chain | `Steps.Count == 3`, the last starting `schema 14 -> 15:` ("the third of three") | Matches the design's seven statements of 12 → 13 → 14 → 15, and the implementation. §7.14's "the fourth of four" is amended |
| (b) "The fourth of four" | `Steps.Count == 4` | Needs a fourth migration step that no section of the design defines; it cannot be met without inventing one |

**Recommendation.** (a): "the fourth of four" counts the four schemas in the path, not the three steps between them. Nothing else in E2 depends on it.

**State at the STOP.** E2.1 (`d17a67e`), E2.2 (`f7931e1`) and E2.3 (`e5f221d`) are committed and pushed. E2.3 builds, 898 tests pass, and the content lint reports 0 errors. E2.4's test is written in the worktree with the design's literal assertion (`Steps.Count == 4`), uncommitted; it fails on that line alone. E2.5, the documents, is not started. The E2 runtime gate is run over E2.3 as verification only, and its results are below.

## Owner ruling on the E2.4 STOP (2026-09-25)

The owner chose reading (a). As applied:
- `TheM6AcceptanceSave_LoadsIntoM7_NothingBuiltNeutralNoErrand` asserts `Report.Steps.Count == 3`, with `Steps[0]` starting `schema 12 -> 13:`, `Steps[1]` `schema 13 -> 14:` and `Steps[2]` `schema 14 -> 15:`, the third of three (E2.4, `fd12b6c`).
- The design's §7.14 is amended in the history folder (backup `drafts/history/M7_IMPLEMENTATION_DESIGN.before_E2_S10_ruling_2026-09-25.md`). The line now reads that `Report.Steps` holds exactly three steps, the last the third of three. An owner-ruling note keeps the original wording, "ends with a step starting `"schema 14 -> 15:"`, the fourth of four", and names the error: it counted the four versions (12, 13, 14, 15) instead of the three transitions.
- A search of the whole design found no other instance of the error. The remaining references are correct and unchanged: the full-chain step lists for fixtures v1 and v2, and the "four repoints".
- M7 still adds one migration, `SchemaV14ToV15`.

## E2 record

**What schema 15 saves.**
- **Player.** `factions`: `next_act_seq`; `acts` (`seq`, `kind`, `subject`, `cell_key`, `x_mm`, `z_mm`, `tick`); `knowledge` (`knower`, `act`, `identity`, `source`, `via`, `tick`, `delta`); `standing` (`faction_id`, `points`).
- **Each companion.** `route`: `status`, `goal_mm`, `corners_mm`, `planned_tick`, `stamp`, `watch_mm`, `partial`.
- **Entities.**
  - `pieces`: `instance_id`, `def_id`, `host_cell`, `x_mm`, `z_mm`, `rotation`, `owner`, `health`, `door_open`.
  - `structure_seq`.
  - `npc_errands`: `npc_id`, `host_cell`, `phase`, `piece_id`, `work_owner`, `x_mm`, `z_mm`, `facing_mdeg`, `route`, `stuck_ticks`.

Every one is required on decode. All are empty in live play until E3, E5 and E8-E9 write them.

**Frozen shapes.** `Sections/SchemaV14.cs` freezes three shapes:
- `V14.Player`, 18 keys, schemas 13-14;
- `V14.Companion`, 11 keys, schemas 12-14;
- `V14.EntitiesSection`, 6 keys, with `noises`.

The 11 -> 12, 12 -> 13 and 13 -> 14 steps write the frozen shapes, and `SchemaV12.cs` names `V14.Companion`. `SchemaV13.cs` is untouched.

**Digest tags.** `unnamed.player/v10`, `unnamed.effective-cell/v3` (M7's terms after v2's) and `unnamed.simulation/v3` (`StructureSequence` after the player digest term).

**Fixture.** `v15/quick` was written by `M2.Probe fixture`: world tick 5000, writer identity 0.1.7. `CellsMatched` stays 10.

**The M6 acceptance save's extended assertions.**
- `TheM6AcceptanceSave_LoadsUnderTodaysGame_AsItWasSaved_AndPlaysOn`: five `AddedSince12` rows, which are the ledger, Tavar's route, `Pieces`, `NpcErrands` and `StructureSequence`. The difference count is exactly +5.
- `TheM6AcceptanceSave_LoadsIntoM7_NothingBuiltNeutralNoErrand`, E2's rows:
  - the three steps;
  - no transition, mismatch, blocker, loss or alias;
  - the generator's fingerprint;
  - nothing built, sequence 0, no errand, and Kera at her site;
  - the empty ledger;
  - Tavar with no route and his trail;
  - the exact pre-migration copy;
  - a clean reload.

**Measured dump counts.**
- A new game: 662 leaves, 660 without M7, so +2: `StructureSequence` and `NextActSeq`.
- `--playthrough-verify`: 968 fields, which is E1's 956 plus Tavar's `None` route (10), `StructureSequence` (1) and `NextActSeq` (1).
- The `live` section's `pieces`, `work_assignments` and `factions` are empty, and add nothing.

**Recorded limits.**
- The region layout (authored structures, doors and barriers) is outside the baseline hash (`src/World/Generation.cs:144-162`), so a later layout edit under a placed piece is not caught by the baseline proof. The structure audit reports it (unscheduled).
- `StateDump.Order` sorts arrays of objects by content, so route corners and trail marks are order-checked only by the raw dump and the digests.

## STOP - E5.2, S1 (2026-09-26)

**What fired.** S1: an existing test must change beyond the edits §12.10 lists by name.

**Where.** E5.2, the building content. §4.20 defines BLD006 as "`config.building` present iff any piece or build area exists".

**The test.** `tests/Content.Tests/ProgressionContentTests.cs`, `ACopyOfTheGameConfig_Lints` (Phase 1, M2c).
- Its constructor copies every file of `content/config` and `content/skills` into a temporary pack, except `inventory.yaml` and `damage_constants.yaml`, and the test asserts the loader reports no error.
- With E5's `content/config/building.yaml` shipped, that pack holds `config.building` and no piece or build area.
- The "only if" half of BLD006 therefore refuses it: "BLD006: config.building is present, but there is no piece and no build area".
- The test is not in §12.10's list, and the design does not name it anywhere.

**The two readings.**
- **(a) As written, both directions.** BLD006 keeps the refusal. The Phase-1 test must then leave out `building.yaml`, as it already leaves out `inventory.yaml` and `damage_constants.yaml`: a one-line edit that §12.10 does not permit. That is S1.
- **(b) Required when needed.** BLD006 refuses a pack whose pieces or build areas lack `config.building`, and keeps every value check (module, turns, reach, relief, refund, repair, protections, damage, the closed setting list). A `config.building` with nothing to build is allowed, as `config.navigation` and `config.factions` already are in the same test's pack. No test changes.

**Recommendation: (b).** The "only if" half guards nothing a player can reach: a `config.building` with no pieces changes no behaviour. The two other M7 configs already work this way, and no Phase-1 test is touched. Nothing else changes: BLD001-BLD004, BLD007, BLD009 and WLD015 stand as built.

**State at the STOP.**
- E5.1 (`9361019`, the building domain and its seven Domain tests) is committed and pushed.
- E5.2 is written and not committed: the content (the four pieces, `config.building`, timber, `loot.timber_stack`, `container.timber_stack`, `build_area.hollow_crossing`); `BuildingContent` with BLD001-BLD004, BLD006, BLD007 and BLD009; WLD015; `BuildingSetup`, `ProtectedZones`, `SimulationSetup.Building`; `BuildingContentTests` (2 tests, 25 crafted refusals); and the `LoadAll_Loads_Yaml_Files` IDs.
- Measured with reading (a) as written:
  - lint: 113 definitions, 0 errors. BLD007 finds the shipped area clear, and the relief is as §4.8 states.
  - tests: Content 184 of 185 (this test); Application 231 of 232; everything else green.
  - The Application failure is the recorded Phase-1 `AsyncSaveTests` flake. It passes alone (5 of 5), and nothing in E5.2 touches it.
- Measured with reading (b): Content 185 of 185.


## Owner ruling on the E5 STOP (2026-09-26)

The owner chose option (b) and amended BLD006.
- **The rule now.** If the pack contains any piece definition or build area, `config.building` is required. If `config.building` is present, every value is validated exactly as before: `module_m` 3.0 and a whole multiple of the navigation node size, `rotation_step_deg` 90, the reach, relief, refund and repair ranges, protections at least 0, and damage keys closed to `melee`. With no pieces and no build areas, `config.building` may be absent, or present and valid. A valid inert config file is never required to be removed.
- **Why.** The rule exists so that buildable content is never read without the configuration that defines its building semantics. A valid config in a pack with nothing to build creates no ambiguous authoritative state and switches on no building content.
- **The Phase-1 test.** `ProgressionContentTests.ACopyOfTheGameConfig_Lints` copies the general game configuration into a pack without every feature's content. It stays unedited; §2.18 and §12.10 keep that protection.
- **Design.** §4.20's BLD006 row now reads "`config.building` is required when any piece or build area exists; when present, it is validated fully even if the pack contains no buildable content", with the ruling noted in place. No other section repeated the building "iff" (the `config.factions` "required iff any faction exists" of §5 is unrelated and unchanged).
- **As built.** `BuildingContent.Validate` drops the "present, but there is no piece and no build area" refusal and nothing else. `Bld006_RequiresTheConfigWhereAnythingCanBeBuilt_AndValidatesItWheneverPresent` proves the three cases:
  1. pieces and an area with no config, and an area alone with no config: BLD006 "config.building is missing";
  2. no pieces, no area and a valid config: no BLD error;
  3. no pieces, no area and an invalid config (reach 20 m; an unknown setting): BLD006.
- **Measured at E5.2.** Content 186 of 186, `ACopyOfTheGameConfig_Lints` included, unmodified; the whole suite 972 of 972.


## STOP - E8.5 (second), S9 (2026-09-26)

**What fired.** S9: with the vitals saved, step 10's equality still cannot hold in the scenario as written, because of the documented policy that an attack in progress is not saved. The owner's ruling keeps that policy separate and asks, if it conflicts with an equality guarantee, that the exact case be named and the contract stated honestly. It also requires `CrossingWorkshop_10` to pass unchanged in its setup and equality checks. Both cannot hold, so this is reported, not resolved.

**The case, measured** (headless, with the E8 rows):
- R46 saves at tick 1475. A grey wolf (`spawn.hollow.valley_strays#0`) is at the character in its bite's windup, 1 tick from release.
- The saved and loaded vitals are identical: last blow 1175, last exertion 1083, never a working, nothing accrued.
- W1 (the world that went on) bites at 1477 (3 damage) and 1506 (4). W2 (the loaded one) lost the windup, as PERSISTENCE.md §5.3 says ("A blow in progress - an attack's windup, a charge's run - is still not kept"). It begins again and bites at 1486 (6) and 1515 (5). From 1544 the bites land on the same ticks in both.
- Each bite starts a bleed on its own schedule. The bleed's ticks re-stamp the last-blow tick, so health regenerates on different ticks, and the two worlds never meet again: after 600 ticks health is 80 in W1 and 85 in W2 (`HealthMilli` 950 and 500). Nothing else differs.
- Before schema 16 the same 80 and 85 were read with a different cause (the reset clocks). The two causes coincide numerically here; the per-tick trace shows the clocks are now equal at the load and the divergence starts at the lost bite.

**The two readings.**
- **(a) Save a creature's attack in progress**: each creature record's current attack (its ability, the tick it began, and the target), as schema 14 already saves its charge cooldown, stagger and immunity (L-06). The loaded wolf then bites at 1477 like the unsaved one, and step 10 passes unchanged. This extends combat persistence beyond the character's vitals, and the owner's ruling says this authorization does not silently do that. The character's own action in progress (a swing, dodge, working or stagger) would stay transient unless also ruled in.
- **(b) Keep the policy.** The contract is then: a save and its continuation are equal only for a save taken while no attack or action is in progress. Step 10's save lands in one, so `CrossingWorkshop_10` cannot pass without a change to the scenario or the assertion, both of which the ruling forbids.

**Recommendation: (a)**, limited to a creature's attack in progress (the case that fired), in the schema-16 entities section. It is the only reading under which step 10 passes as the ruling requires. It closes the same class of gap for any save taken in a fight.

**State at this STOP.**
- Pushed, CI green (run 36246528627): schema 16, `1dd66e7`, on E8.1-E8.4.
- E8.5 written, not committed (as at the first STOP). The runtime gate, the E8 beats, E9 and E10 wait on the ruling: `--build-shots-verify`'s continuation check and the table's step 10 meet the same save.
- Coordination: `docs/M7_VISUAL_INTEGRATION_HANDOFF.md` (the shared files, the input and HUD contracts, the save format, the combined-build tests, and why the inventory request is not implemented).

## AsyncSave record (2026-09-26)

- **Cause, measured.** A background save ran as a continuation on the shared .NET thread pool (`_writer.ContinueWith(..., TaskScheduler.Default)`). The test gives the autosave 5 s (500 × 10 ms) to reach its first hook, which it normally reaches in 11-25 ms. In a full run the other test classes held the pool: 4,675 ms and 13,195 ms before the save began, with 17 pool threads busy and 10 work items queued, and the 5 s sleep loop took 8.4 s of wall clock. Not caused by schema 16: on `9e3ef79`, the commit before it, the test failed in 3 of 4 full Application runs. In the game the pool is idle, so this was a test exposure of a real dependency, not a player-visible failure.
- **The fix (the owner's ruling).** `SaveLane` (src/Application) is one long-lived thread per session, started by the first background save. It takes saves in the order queued, one at a time, and never needs a pool thread to begin. `GameSession.Queue` enqueues on it; nothing else changed. Kept as before:
  - capture order;
  - one writer;
  - `SaveStore`'s atomic commit and profile lock;
  - one autosave pending at a time;
  - `WaitForSaves` before a load or a synchronous save;
  - a failure as the save's outcome, never an exception out of the frame.
- **Closing.** Disposing the session waits for the saves (10 s) and closes the lane, whose thread ends when its queue is done. A lane dropped without closing is collected, and its thread then writes what it was given and ends.
- **Tests (`SaveLaneTests`).**
  - 60 queued saves run in order, never two at once.
  - A throwing save faults its own task, and the next still runs.
  - Closing with saves queued finishes them, ends the thread, and refuses more.
  - Closing while a save runs lets it finish.
  - A lane never used has no thread.
  - A dropped lane's thread ends (this test fails without the finalizer).
  - A disposed session leaves no lane thread, and its two saves load.
  - With every pool thread held and work queued behind them, a save still begins within 1 s (about 220 ms per test run). This test runs alone after the parallel ones.
- **Measured.**
  - `AsyncSaveTests` and `SaveLaneTests` alone: 35 runs, all passing.
  - On the final code: 11 of 11 full Application runs, plus one full-solution run, with every AsyncSave and lane test passing. The AsyncSave test took 2-3 s. The only other failure was `CrossingWorkshop_10`, in the E8.5 WIP, waiting on schema 17.
  - Across every earlier full run with the lane in place, the AsyncSave test never failed.
  - The 5 s budget is unchanged.
  - An earlier saturation test held only twice the pool's minimum, and failed once in 5 runs after the pool had grown past that. It is now adaptive.
- **Found on the way.**
  - `NavigationTests.Navigation_StaysWithinBudget` (M7, E7.3) failed once in 11 full runs. The worst placement check took 47 ms against the design's CI bound of 30 ms, with a median of 1.3 ms: other test classes' work landed inside single-call timings. Its class now runs alone after the parallel tests; the bound is unchanged. It then passed in all 12 full runs.
  - A first version of the saturation test disposed its hold event while blockers were still queued, which crashed the test host in 3 of 6 runs. That was a fault of the test itself. It was fixed before any commit, and the host did not crash in the 11 runs after the fix.

## Schema 16 record (2026-09-26)

- **The regression, preserved.** `VitalsContinuationTests` failed on the code before the fix: a save inside health's regeneration pause (stamina diverged at tick 137, 43 against 42), stamina after swings, and Focus after a working (68 against 69 at tick 21). Each is compared tick by tick with the unsaved world, then field by field and by digest.
- **The persisted set, traced through `PlayerCombat`.** The pauses `LastCombat` (health; also whether a working was in the thick of a fight), `LastExertion` (stamina) and `LastCast` (Focus and Strain) are saved as absolute ticks or never. So are the part-points `HealthMilli`, `StaminaMilli`, `FocusMilli`, `StrainMilli` and `SprintMilli`, each 0-999. Not saved, as the documented policy: `Action`, `Blocking`, `StaggerImmuneUntil`, `Recent` (the death recap) and `Defeated` (settled within the tick). Rates and cooldowns are unchanged.
- **Persistence.** The player DTO gains required, strictly checked `vitals`. Schema 15's player shape is frozen as `Sections/V15`, and 14 -> 15 is repointed at it. 15 -> 16 gives an older save `VitalsClock.Rested`: every pause over, nothing accrued, exactly how every older save always loaded. The timing it did not keep cannot be reconstructed. The player digest is `unnamed.player/v11`.
- **Fixtures.** v1-v15 are untouched byte for byte; each `expected.json` gains only the rested vitals (10 lines, nothing removed). v16 was written by the probe with writer pack 0.1.7: its cells and entities sections are v15's sizes, and its player carries a blow 10 ticks before the save, an exertion 2 before, a working 40 before, and part-points 999, 1, 350, 500 and 250.
- **Migration counts** carry the new step, with every order check kept: the chain tests from v1-v14, `Schema14To15_…` (two steps), and the M6 acceptance save (four: 12 -> 13 to 15 -> 16, its added-field table gaining the rested vitals).
- **Tests.** `Schema16Tests`: round trips at the edges (never, tick 0, `long.MaxValue`, 0 and 999); nine impossible values refused; a player without vitals corrupt; the step alone giving rest and changing nothing else, the same bytes twice. The completeness tests reach every vitals field, each moving the digest.
- **Verified.** 1,046 tests on `1dd66e7` in a clean worktree, all passing (the AsyncSave test among them on that run); CI green.
- **Documents.** PERSISTENCE.md §5.1 (the vitals, and the contract for what the combat state does not keep), §6.2's chain, the schema-16 tests; the fixture README.

## STOP - E8.5, S9 and S2 (2026-09-26)

**What fired.** S9: §4.22's step 10 cannot be met as written, with E8's rows, in the shipped world, without inventing a rule. The fix at the root needs a new persisted field after E2, which is S2. Both readings are reported below; neither has been picked.

**Where.** E8.5 lands the Crossing Workshop's E8 rows (R07, R08, R15, R26-R37) in `CrossingWorkshop.Rows`, with `Landed = E8`. `CrossingWorkshop_10_SaveQuitReloadGoesOnTheSame` then fails. After the R46 save, W1 goes on 600 ticks, and W2, a fresh session loading the save, goes on 600 ticks. They differ in 4 of 1,465 fields:
- the character's health, 80 in W1 and 85 in W2 (`$.live.combat.Health` and `$.player.Progression.Pools.Health`);
- the two digests that cover it.

Every other field is equal: every piece row, the structure sequence (31), the grid digest, Tavar and his route, and every creature.

**Why, measured.**
- E8's rows add about 700 ticks before the vestibule and the west walls. At tick 1175, during R39's walk from (100.5, 97.6) to (97.5, 97.0), a grey wolf bites the character (4 damage, 120 to 116). The E7 table, 700 ticks shorter, never met it. The wolf is still at the character when R46 saves, and keeps biting in both W1 and W2.
- The character's regeneration clocks are not saved. `PlayerCombat.LastCombat` is the last blow taken or dealt, and holds health regeneration for `out_of_combat_s` (8 s). The same holds for `LastExertion` and `LastCast`, and for the part-point accumulators `HealthMilli` and `StaminaMilli`.
- A load starts them all from `PlayerCombat.Rested` (`RuntimeState.cs:147`, `Combat.cs:243`). W2 therefore regenerates at once, where W1 still waits out the 8 s since the last bite, and W2 gains health that W1 does not.
- This is a Phase-1 gap, not an E8 change. A save taken while the character is hurt resumes regeneration early, by up to 8 s and up to one point's accumulator. It never showed before because no Phase-1 or earlier M7 save-and-continue proof saves in a fight. The creatures' own transients have been saved since L-06 (schema 14); the character's are not.

**The two readings.**
- **(a) Save the character's regeneration clocks** in the player record: `LastCombat`, `LastExertion` and `LastCast` as absolute ticks, plus `HealthMilli` and `StaminaMilli`. Save-and-continue is then exact in a fight, and step 10 passes as written. This is a new persisted field after E2 (S2), beyond the ruling on the E2.4 STOP ("no save-format change beyond schema 15"). It touches the player digest, the codec, `PlayerRecordCompletenessTests` and the fixtures.
- **(b) Keep the save format.** Record the gap as a Phase-1 residue. Change the table so that step 10 saves only at rest: no blow taken, dealt or worked within `out_of_combat_s`, and the pools full. R46 could wait for that, or the table's world could keep the wolf away. This invents a precondition the design does not state. It may also break again at E9, whose rows (R16-R21, Kera's walk) move the timeline by about 1,000 more ticks.

**Recommendation: (a).** It removes the defect rather than the evidence of it: any save taken in a fight diverges today. (b) would hide the gap in the one proof that found it.

**State at the STOP.**
- Committed and pushed, with draft-PR CI green (run 36240458313): E8.1 `194bab7`, E8.2 `f776dfd`, E8.3 `6c5efef`, the planner `fb16e33` and `8573229`, and E8.4 `fb525f7`.
- E8.5 is written and not committed. It contains:
  - presentation: the mend key T (`build_repair`) with its F1 row, the target line, the panel legend, the controller's submitter and the `InputCheck` action; the damage, destruction and mending log lines and toast; piece chests and benches in focus; `OpenStation` reading `Simulation.Stations`; `Describe` naming `container.pce_` and `station.pce_` keys; the panel closing when its bench goes; and the bench's anvil block;
  - `EveryM7Action_IsBoundToADirectKey` with `build_repair`;
  - the E8 rows with their new actions and "+20 each" gaps, played by both runners;
  - `CrossingWorkshop_1to3` with R07 and R08, and the new `CrossingWorkshop_4and8_CraftBlowsMendAndSpill`;
  - the step-11 replay's translation of a picked-up item the run minted (below);
  - `PreviewsInterleaved`'s raw digest, compared only for a window that minted nothing (G8).
- Not yet written: the `--build-shots` beats b07, b10, b15, b16 and b17's chest rows. The E8 runtime gate has not run.
- Measured with E8.5 as written: 1,036 tests, all passing except `CrossingWorkshop_10` (this STOP) and the recorded `AsyncSaveTests` flake. Lint: 116 definitions, 0 errors.

**E8 so far**, for the E8 evidence once resumed:
- **E8.3 proven neutral.** Every combat and `Aim` test passes unchanged. The playthrough transcript equals E7's row for row, apart from the content hash and the save's run-specific raw digest. Verify compared 1,192 fields with 0 differences. Its replayable dump differs from E7's only by E8.1's null `InstanceId` and `Owner` on the five authored container sites and E8.2's empty `piece_stations`.
- **Kera's three workshop routes (N-A10).** They take 22,685, 40,751 and 40,639 expansions with 7, 7 and 6 corners, exactly the design's model; all are within two thirds of the cap (43,690).
  - Release medians: 3.5, 6.0 and 6.1 ms (target 15 ms; the STOP is 20 ms).
  - Debug medians: 4.1, 7.2 and 7.3 ms (CI bound 45 ms).
  - Before the two planner commits, the walk home took 22.1 ms in Debug and 8.8 ms in Release. `fb16e33`'s message says "about 3.5 ms Release", written before measuring. The measured value was 6.6 ms, and 6.0 ms after `8573229`.
- **CI.** The first attempt of run 36237433970 failed `SixtyCreatures_TickWithinTheBudget` at 4.94 ms (0.49 ms on this machine; runner variance), and the re-run passed. T10 takes 12-15 s a seed on CI.

## E7 evidence (2026-09-26, the machine that ran E1-E6)

| Proof | Result |
|---|---|
| `dotnet test src/UNNAMED.sln` | 1,018 tests: Domain 185, Application 269, Persistence 198, Content 186, World 68, Presentation 57, EntityRegistry 23, Architecture 32. All pass but the recorded Phase-1 `AsyncSaveTests` flake, which failed in both full-solution runs and in one of two Application-only runs, and passes alone every time (the local risk below) |
| Content lint | 114 definitions, 0 errors: BLD008 accepts the shipped area |
| `--smoke`, `--quit-after 300` (headless) | PASS (save/load digest identical; 911 display-server lines, as before); exit 0 |
| `--build-shots` (from S0) | every beat passed, b14 among them: from (100.5, 94.5) a pad and two walls south of the door; the ghost of the wall across the vestibule red, and its status the command's refusal in words; the command refused, counted by the command path as rule V-N1, in the words "that would close off the Timber Door" (the door's approach is the first protected point in the pocket, as §4.22 foresaw for E7); the walls and the pad taken down for 1, 1 and 0 timber. Step 1: 17 / 25 / 17; the table's end: 21 pieces, 11 timber carried, sequence 27; `SubscriberFailures` 0 |
| `--build-shots-verify` | v1: **1,376 fields**, 0 differences; v2: **1,370 fields**, 0 differences; digests equal |
| A second `--build-shots` | `state_replay.json` byte-identical, SHA-256 `4fa4a82893cb4a318507a2f50e4acf37f84445aa822af9ec779d20498be95611` |
| `piece:*` art coverage (R16) | 5 of 5 piece entries fall back to greybox: reported, not allowlisted |
| `--playthrough`, verify, a second run | every beat passed (554 s); verify **1,182 fields**, 0 differences; `state_replay.json` byte-identical, SHA-256 `1c4788615d6820be719acf207d66f450a094e6e657399e41489b901b0580d343` (E5's and E6's); the transcript is E6's row for row but the save's run-specific raw digest |
| `--ui-shots`, `--delta-shots`, `--input-check`, `--layout-check` (both sizes) | all PASS, 0 error lines |
| The check's budget (N-A10, this machine) | Release: median **0.38 ms**, worst **0.78 ms** over a proven wall and the refused vestibule (targets 2 ms and 10 ms; the STOP is a worst over 10 ms); Debug: median 0.83 ms, worst 1.37 ms. CI asserts three times the targets (6 ms and 30 ms): the first E7 push measured a 11.8 ms median there (the runner is about five times slower than this machine in Debug) and failed; the check was then made cheaper with every verdict unchanged (below), and the later run is the one recorded under CI |
| Tests E7 adds | N-D19 `EditCheck_Rules`, N-D20; BLD008's two refusals; `EachPlacementRule` rule 15; `ADoorlessOneSquareHut_IsRefused`; preview parity over all fifteen rules; `PreviewsInterleaved_ChangeNothing` asking navigability; `CrossingWorkshop_7_TheVestibuleIsRefused`; N-A13 (a) and (b); N-A10's check timings; G7 over `NavEditCheck.cs` |
| STOPs | none: the worst check is 1.72 ms; BLD008 accepts the shipped area; preview and command agree everywhere they are compared |

## E6 evidence (2026-09-26, the machine that ran E1-E5)

| Proof | Result |
|---|---|
| `dotnet test src/UNNAMED.sln` | 1,013 passed, 0 failed: Domain 183, Application 266, Persistence 198, Content 186, World 68, Presentation 57, EntityRegistry 23, Architecture 32 |
| Content lint | 114 definitions, 0 errors |
| `--smoke`, `--quit-after 300` (headless) | PASS (its save/load digest identical; the 911 error lines are all "Not supported by this display server", as before); exit 0 |
| `--build-shots` (from S0) | every beat passed. b06 hangs the door: sequence 17, one rebuild, shut. b09: the door opened from inside and shut from outside by the character; the walk north stopped at z 98 450 by the shut leaf on every one of its 40 ticks; opened with E (R13); each toggle's leaf drawn on its hinge (its centre R_r(+800, 0) from the hinge shut, R_r(0, +800) open, within 1 cm; the check fails when the swing angle is flipped, by mutation, then restored). Step 1: 17 pieces, 25 timber spent, sequence 17; the table's end: 21 pieces, 14 timber carried, sequence 21. Tavar through the open door and inside 600 ticks on; 0 `CompanionCaughtUp`; `SubscriberFailures` 0 |
| `--build-shots-verify` | v1: **1,376 fields**, 0 differences, the same digest; v2: **1,370 fields**, 0 differences, the same digest |
| A second `--build-shots` | `state_replay.json` byte-identical, SHA-256 `5f0bfae207083f049e7de595f93f15f4c679e894e8b6d38ca5a73452b411c552` |
| `piece:*` art coverage (R16) | **5 of 5 piece entries fall back to greybox** (the door added): reported, not allowlisted |
| `--playthrough`, `--playthrough-verify`, a second run | every beat passed (553 s); verify **1,182 fields**, 0 differences; `state_replay.json` byte-identical, SHA-256 `1c4788615d6820be719acf207d66f450a094e6e657399e41489b901b0580d343` - E5's own, since the playthrough hangs no door. Its transcript is E5's row for row, but for the header's content hash and the save's raw digest (fresh instance IDs every new game: two runs of one build differ there too) |
| `--ui-shots`, `--delta-shots`, `--input-check`, `--layout-check` (1366x768, 1280x720) | all exit 0 and PASS, 0 error lines; the layout check over the full-pack content copy with the door added |
| Tests E6 adds | the door rows of `EachPiece_Places`, `EachPlacementRule` (rules 4 and 7), `WallsDoorwaysAndDoors_BlockAndPass`, `EachFootprintChange` (toggles: no rebuild, no sequence, the same grid digest), `ForeignPieces` and G10; `NoPieceDoorCloses_OnAnyBody`; `ACompanion_OpensTheOwnersPieceDoor`; G26 `ARefusedDoor_CountsAsStuck_ForBothMovers` (the companion); N-D5 over a piece gate; the view test's placed door; `CrossingWorkshop_1to3` with the door |
| STOPs | none: no toggle rebuilds the grid or moves the sequence (`EachFootprintChange`), and no leaf shuts on a body (`NoPieceDoorCloses_OnAnyBody`) |

## E5 evidence (2026-09-26, the machine that ran E1-E4)

| Proof | Result |
|---|---|
| `dotnet build src/UNNAMED.sln` | 0 errors; the Phase-1 warnings only (the xUnit analyzers, `MagicContent.cs(165,21)`) and Godot's `BuildMode.Rotation` CS0108 |
| `dotnet test src/UNNAMED.sln` | 1,009 tests: 1,008 passed; the one failure in the full-solution run is the recorded Phase-1 `AsyncSaveTests` flake (below), which then passed alone 3 of 3. Domain 182, Application 263, Persistence 198, Content 186, World 68, Presentation 57, EntityRegistry 23, Architecture 32 |
| Content lint | 113 definitions, 0 errors (BLD007 and BLD009 accept the shipped area) |
| `--smoke` (headless) | PASS; its save/load digest identical (911 error lines, all "Not supported by this display server", as in E1-E4) |
| `--quit-after 300` (headless) | exit 0 |
| `--build-shots` (from S0) | every beat passed: b02, b03 (two stills: build mode, F1), b04, b05, b06, b08, b09, b17, b18, b19. Step 1: 16 pieces, 24 timber spent, sequence 16; the table's end: 20 pieces, 15 timber carried, sequence 20. The aim west from (100.5, 101.0) stops at x 99 201. Tavar saved on his route (`Active`), inside at (100.754, 99.891) 600 ticks on, through the doorway's opening; 0 `CompanionCaughtUp`; `SubscriberFailures` 0 |
| `--build-shots-verify` | v1: **1,343 fields**, 0 differences, the same digest; v2 (600 ticks on): **1,337 fields**, 0 differences, the same digest; `SubscriberFailures` 0. The relaunch replans Tavar's route at the run's tick, 588 |
| A second `--build-shots` | `state_replay.json` byte-identical, SHA-256 `5e0382ad386a87f2e90f30d70151788f9d2ae5eb6fcd8b96ec074dc6c0fe770e` |
| `piece:*` art coverage (R16) | **4 of 4 piece entries fall back to greybox**, in both runs and the relaunch: reported, not allowlisted, not a failure. M7 ships no piece art |
| `--playthrough` then `--playthrough-verify` | every beat passed (553 s), `m7_build` included; verify: **1,182 fields**, 0 differences (E4's 1,080 plus the two pieces, the timber stack's record and the structure rows) |
| Rows before `m7_build` | E4's, byte for byte: every one of the 99 rows up to and including `m7_tell_sel_armour`. Only the header differs, in the content hash that E5.2's content changes (the `Space` switch is neutral while nothing is built) |
| `m7_build` | two `PiecePlaced`, one `NavigationRebuilt` (the wall), sequence 2, 77 timber left in the stack's record, 0 carried; still `26_first_build` |
| A second `--playthrough` | `state_replay.json` byte-identical, SHA-256 `1c4788615d6820be719acf207d66f450a094e6e657399e41489b901b0580d343` |
| `--ui-shots`, `--delta-shots` | both exit 0, 0 error lines |
| `--input-check` (windowed) | PASS, the build steps included (every panel, L's included, ends build mode; Esc leaves build mode and keeps the mouse; the click that takes the mouse back places nothing) |
| `--layout-check` at 1366x768 and 1280x720 | PASS at both sizes, the build panel on screen, over a full-pack content copy |
| Tests E5.6 adds | `CrossingWorkshop_1to3_BuildRefuseAndWalkIn`, `CrossingWorkshop_9_ANewWallChangesHerWayHome`, `CrossingWorkshop_10_SaveQuitReloadGoesOnTheSame`, `CrossingWorkshop_0and11_ReplaysFromTheLog`, `PreviewsInterleaved_ChangeNothing`, `TheCommittedBuildStart_IsTheCrossingWorkshopStart`, `TheCrossingWorkshopStart_IsWrittenOnlyWhenAsked` (the env-gated writer), `ANewGame_TakesTimber_BuildsAPadAndAWall_AndLoadsEqual`, `ACreatureChasingRoundTheWorkshop_NeverOverlapsAPiece` (`BuildingAcceptanceTests.cs`); and `TheM6AcceptanceSave_LoadsIntoM7_NothingBuiltNeutralNoErrand`'s E5 rows (the untouched timber stack, no audit, no pieces, revision 0). The chase test fails when the creature step sites use the authored space (checked by mutation, then restored) |
| Budgets | `SixtyCreatures_TickWithinTheBudget` and N-A10's one-piece rebuild (108 nodes, under 6 ms) pass in the suite |

## E4 evidence (2026-09-26, the machine that ran E1-E3)

| Proof | Result |
|---|---|
| `dotnet build src/UNNAMED.sln` | 0 errors. 6 warnings, all Phase 1's xUnit analyzer warnings in test projects (`ReachabilityTests` 3, `PickUpItemTests` 2, `SaveFailureTests` 1). Content was already built, so Phase 1's `MagicContent.cs(165,21)` CS8602 did not reprint; E3's `FactionContent.cs` CS8601 is fixed |
| `dotnet test src/UNNAMED.sln` | 962 passed, 0 failed |
| Content lint | 106 definitions, 0 errors |
| `--smoke` (headless) | PASS; its save/load digest identical. Its 911 error lines are all "Not supported by this display server" (headless), as in E1-E3 |
| `--quit-after 300` (headless) | exit 0 |
| `--build-shots` | exit 0 (b01, b02) |
| `--playthrough` then `--playthrough-verify` | every beat passed (522 s); verify: **1,080 fields**, 0 differences (as E3: the companion's route was already in the dump since E2) |
| A second `--playthrough` | `state_replay.json` byte-identical, SHA-256 `6ac18af17e4e068510b628e063fdb66ed81ae4bea48a53acdb3d9744f021246a` |
| `SubscriberFailures` | 0 in both runs: both playthrough logs hold 0 error lines |
| `--ui-shots`, `--delta-shots` | both exit 0 (303 s, 161 s), 0 error lines |
| The `follow` beat | 0 "caught up" rows (0 in the whole run). One `RoutePlanned` row: at tick 6987, "Tavar Orr planned a route: found (none), 2 corners, 19 expansions" |
| Rows up to and including `follow` | E3's 77 rows, byte for byte, with the one `RoutePlanned` row added. After `follow` the rows are E3's except the persistence-acceptance save row: Tavar waits at (51.2, 135.4), not (51.3, 135.4), having come home by his route, so the digest at the save differs. That is outside the E4 STOP window and is the save the verify compares with 0 differences |
| Companion routes against the E1 ruling | Actual companion plans: 19 expansions (the playthrough) and 49 (N-A11), against the two-thirds bound of 43,690; each well under a millisecond in the Debug test run. Kera's routes are measured in E8-E9 |
| Tests E4 adds | N-D2, N-D7, N-D8, N-D10, N-D17, N-D18 (Domain); `OpenDoor_ACompanionInReach_OpensAnAuthoredDoor_AndNeverClosesIt` (World); `NoDoorCloses_OnAnyBody` (its authored half), N-A11, N-A6 (a) (Application). N-A11 fails when nav mode is replaced by the Phase-1 trail rule (checked by mutation, then restored) |
| Existing tests | Every `CompanionTests` case passes unmodified except the permitted route assertion in `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame`; C16 and `FollowWaitFollow` still see 0 `CompanionCaughtUp`. `AScripted200CommandSession_…` and `SixtyCreatures_TickWithinTheBudget` pass. G5 and G27 gain `RoutePlanned` |

## E3 evidence (2026-09-26, the machine that ran E1 and E2)

| Proof | Result |
|---|---|
| `dotnet build src/UNNAMED.sln` | 0 errors. (Corrected in E4: this line said "the one warning is the pre-existing `MagicContent.cs(165,21)` CS8602". A full rebuild also showed E3's own `FactionContent.cs(421,20)` CS8601, fixed in E4, and six Phase-1 xUnit analyzer warnings in test projects) |
| `dotnet test src/UNNAMED.sln` | 952 passed, 0 failed (the Phase-1 async-save flake did not fire in this run; see the local risks) |
| Content lint | 106 definitions, 0 errors |
| `--smoke` (headless) | PASS; its save/load digest identical |
| `--quit-after 300` (headless) | exit 0 |
| `--build-shots` | exit 0 (b01, b02) |
| `--playthrough` then `--playthrough-verify` | every beat passed (523 s), the six faction beats included; verify: **1,080 fields**, 0 differences. Over E2's 968: the ledger's 37 (two acts, three knowledge rows, one standing row), and the rest from Kera's wares record, the bought billet, and the new dialogue memory and relationship rows |
| A second `--playthrough` | `state_replay.json` byte-identical, SHA-256 `5023c13aa3a59ed96230661a0e4d9c62a907962648d32a6bc715ead5ec97ee74` |
| `SubscriberFailures` | 0 in both runs. `Main` pushes an error for every handler that throws ("a handler for ... threw"), and both playthrough logs hold 0 error lines |
| `--ui-shots`, `--delta-shots` | both exit 0 (295 s, 160 s), 0 error lines |
| The faction beats | `m7_wait`, `m7_billet_refused` (refused with "Kera Voss will not sell you that"; no billet listed), `m7_armour`, `m7_tell_kera` (the Waystation 0 -> 100, neutral -> accepted, act 2 via Kera; one billet bought for 20 coin; Kera's respect and trust unchanged), `m7_tell_sel_tavar` (the Survey 0 -> 100 on act 1; Sel's trust +5 only; `notes` offered), `m7_tell_sel_armour` (the Survey 100 -> 0 on act 2; `notes` closed; the HUD line "The Survey: neutral (-100), told to Sel Arien"; Sel's trust unchanged). Stills `22_armour_down` to `25_factions_f6`. M6's transcript rows appear in order, with only M7 rows added |
| The armour, measured | At the step in, the sentinel is `suspicious`, awareness 60 (it heard the last walking step), facing 256° (it had turned 14° from 242°), 2.68 m away; the character at 120/120 health after the mending stop. The character killed it and ended at 27/120. `CreatureKilled` names the character; act 2 was recorded; no faction learned of it until Kera was told |

## E2 evidence (E2.4 + E2.5, 2026-09-25, ASTRAL)

| Proof | Result |
|---|---|
| `dotnet build src/UNNAMED.sln` | 0 errors; the one warning is the pre-existing `MagicContent.cs(165,21)` CS8602 |
| `dotnet test src/UNNAMED.sln` | 899 passed, 0 failed |
| Content lint | 103 definitions, 0 errors |
| `--smoke` (headless) | PASS; the save round trip at schema 15, digest identical |
| `--quit-after 300` (headless) | exit 0 |
| `--build-shots` | exit 0 (b01, b02) |
| `--playthrough` then `--playthrough-verify` | every beat passed (378 s); verify: 968 fields, 0 differences (breakdown in "E2 record") |
| A second `--playthrough` | `state_replay.json` byte-identical, SHA-256 `e566931abe91bac8250a084662971abb5bcd17ea6101417e6068b1018341fe78`, the same as at E2.3 |
| `--ui-shots`, `--delta-shots` | both exit 0 (296 s, 160 s), 0 error lines |

## E2 evidence at the STOP (E2.3, `e5f221d`, 2026-09-25, ASTRAL)

E2.4 adds a test and E2.5 documents; neither changes a runtime path, so these runs stand for E2's runtime unless E2.4 or E2.5 changes that.

| Proof | Result |
|---|---|
| `dotnet build src/UNNAMED.sln` | 0 errors; the only warning is the pre-existing one in `MagicContent.cs` |
| `dotnet test src/UNNAMED.sln` | 898 passed, 0 failed. Draft PR #8 CI green (`build-and-test`, run 36197330347) |
| Content lint | 0 errors |
| Schema 14 → 15 | `Schema14To15_GivesNothingBuiltNoLedgerAndNoRoutes` passes on `Copy(14)` |
| Fixtures v1-v15 | `Fixture_LoadsToItsExpectedCurrentState` (15, with the M7 block) and `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState` (1..15) pass; the older `expected.json` diffs are exactly §7.12's (scope ledger) |
| The real M6 acceptance save | `TheM6AcceptanceSave_LoadsUnderTodaysGame_AsItWasSaved_AndPlaysOn` passes: 12 → 13 → 14 → 15, field by field, with exactly 5 more differences than before M7, as §7.14 states |
| `--smoke` (headless) | PASS; its save round trip runs at schema 15, digest identical |
| `--quit-after 300` (headless) | exit 0 |
| `--build-shots` | exit 0 (b01, b02) |
| `--playthrough` then `--playthrough-verify` | every beat passed (379 s). Verify: **968 fields**, 0 differences. That is E1's 956, plus Tavar's `None` route (10), `StructureSequence` (1) and `NextActSeq` (1), as §7.9 states |
| A second `--playthrough` | `state_replay.json` byte-identical, SHA-256 `e566931abe91bac8250a084662971abb5bcd17ea6101417e6068b1018341fe78` |
| `--ui-shots`, `--delta-shots` | both exit 0 (283 s, 161 s), 0 error lines. The smoke log's 911 headless keyboard-layout lines are E1's pre-existing noise |

## STOP - E3.5, S5 (`m7_armour`) (2026-09-25)

**What fired.** §13.7: "If `m7_armour` fails (a death or the budget): STOP on the first failure; the run is deterministic. Report to the owner with the transcript, with two options." S5 also names it: a scripted runtime proof failing once. The first `--playthrough` with the six faction beats died in `m7_armour`:

| 5:07 | 6145 | Act 1: switch_set world.foldscar.steadied |
|---|---|---|
| 5:58 | 7167 | Tavar Orr: wait |
| 5:58 | 7168 | **Tavar told to wait before the fight** |
| 6:03 | 7265 | **Kera will not sell the waystation's billets to a stranger** |
| 7:04 | 8483 | The character died: ability.creature.armour_slam (Animated Armour); XP debt added 78 |
| 7:04 | 8484 | FAILED at 'm7_armour': the character died |

The beats before it passed: `m7_wait`, and `m7_billet_refused` (the raw buy refused with "Kera Voss will not sell you that", no billet listed). The `heart` beat gained its act row as designed.

**Why, measured.** In the playthrough's world (seed `0x0A5E202609240001`) the sentinel stands at (65, 34) facing 242°. Its senses are 15 m sight in a 100° cone and 18 m hearing all round; it has 90 health, and the character 120. The beat, as built, leaves the smithy by `HomeToTheSmithy` reversed to (54, 74). It then runs to `ToTheArmour` (58, 55), which lies on a bearing of 342° from the sentinel, 22 m out, and runs straight on to the point 1.8 m behind it (66.6, 34.8) before `Engage`. That approach is outside the sight cone (100° off its facing), but a running character is heard within 18 m. The sentinel turns, and the fight is face to face, plate first. The headless P script never meets this: it starts the character 1.8 m behind a sentinel that has not noticed it.

**The options** (§13.7):

| Option | What changes | Consequence |
|---|---|---|
| (a) Tune the beat | For example: a mending stop before the fight (the beat does not mend; `Travel` mends only below half health), and the last leg walked or crouched round behind the sentinel so that it is not heard. Both are script changes inside `m7_armour`; no rule, number or content changes | The playthrough keeps the kill, and the Waystation learns of it by report as designed. Whether a quieter approach avoids the hearing radius needs a measured run |
| (b) Drop the kill from the playthrough | `m7_armour` and `m7_tell_kera` leave the playthrough. It keeps `m7_tell_sel_tavar`, since the heart act alone opens the Survey's gate, and `m7_tell_sel_armour` goes too | P1-P5 stay proven headless on shipped dialogue and gates by `ReputationTableTests` and `TheSameKnownAct_MovesTwoFactionsInOppositeDirections`. The runtime transcript then shows one faction gate, not both |

**Recommendation.** (a), mending first and approaching from behind at a walk, because it keeps the runtime proof of both gates and the kill. If the tuned beat still dies, (b).

**State at the STOP.** E3.1-E3.4 are committed and pushed: the domain rules, the content and FAC001, the runtime with its gates and guards, and the generated reputation table. E3.5 (F6, the HUD line and the beats) and E3.6 (the documents) are written and uncommitted in the worktree, backed up at `G:\UNNAMED_HISTORY\M7_DESIGN_2026-09-24\drafts\wip\E3.5-6_wip_2026-09-25.patch`. With them, 952 tests pass and the Presentation build is clean. The E3 runtime gate did not run past the first playthrough.

## Owner ruling on the E3 STOP (2026-09-26)

The owner chose option (a). What the current rules say, verified in code before tuning:
- The character's footfalls are a `Noise` whose carry is set by gait alone (`CreatureSystem.PlayerNoises`, `config.creature_behaviour` `noise_m`): walk 3 m, run 8 m, sprint 16 m. A body that is not moving makes none. A swing's windup and active phases carry 10 m, whatever the gait.
- A creature hears a noise within both the noise's carry and its own hearing (`Perception.Hears`). The sentinel's hearing is 18 m, so a run is heard at 8 m and a walk at 3 m. Hearing sets awareness to 60 (`heard_noise`) and gives it a place to look, never the target itself. It engages only once it sees the character at full awareness, and it turns at 90° a second.
- The spear lands within 2.7 m of the sentinel's centre (reach 2.4 m, plus its 0.45 m body, less the 0.15 m margin `Engage` keeps). So no approach reaches striking range entirely unheard: the last 0.3 m of a walk is audible. The rules still let a walker be heard a fraction of a second before the first swing, rather than a second or more as a run was.

**The tuned beat, as built.** The beat, in order:
1. `HomeToTheSmithy` reversed to (54, 74), then `ToTheArmour` (58, 55), both as before.
2. A run to 12 m behind the sentinel along its facing. The whole leg stays outside its 100° sight cone and at least 11.2 m from it, beyond a run's 8 m.
3. A mending stop: the mending formula, or a salve, until nine tenths of health, or until nothing more can be worked without strain costing health.
4. A walk to 3.1 m behind it, beyond a walk's 3 m.
5. The last step in, at a walk.
6. `Engage`, unchanged.

Nothing in the game changed: the sentinel, combat, the character, the content and the encounter are as shipped. The first run of the tuned beat passed: the armour was killed by the character, act 2 was recorded, and no faction learned of it until Kera was told. The measured awareness at the step in, and the character's health before and after, are in the E3 evidence below. The fallback, option (b), was not needed.

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
| E1 | N-A10's plan endpoints | The 20 authored protected points of §3.13 at a new game, each at its own position where a person can stand, otherwise at the nearest walkable node within its reach; planned with every gate passable |
| E1 | F1's F2 row | "Navigation debug (the grid and its gates)" while F2 has its one stage; E5 gives it the design's wording when the structures stage lands |
| E1 | `--build-shots` files | E1 writes `transcript.md`, a JPEG per still and the coverage reports. `commands.tsv` and the state files arrive with the command-table rows and b19 (E5). The run-level "words only" check lands with the first M7 toasts and status lines (E5); the camera-in-a-wall check has no piece to test until E5 |
| E1 | What the overlay drew | `NavigationOverlay` keeps the nodes it sampled, its quad count and each gate's colour, so the beats assert on what was drawn, not on a second computation |
| E2 | Commit order | §7.15 lists the player's `Factions` and a companion's `Route` in E2.1. They move to E2.3, with the codec that saves them: `PlayerRecordCompletenessTests` requires every `PlayerRecord` property to reach the save, so adding them before the codec would leave E2.1 red. E2.1 keeps the world half (pieces, the structure sequence, errands, the `Factions` slice) |
| E2 | The M6 acceptance rows | The world's three M7 rows of `TheM6AcceptanceSave_LoadsUnderTodaysGame_AsItWasSaved_AndPlaysOn` (`Pieces`, `NpcErrands` `[]`, `StructureSequence` `0`) land in E2.1 with the fields they cover, not in E2.4; E2.3 adds the player's two (`Factions`, each companion's `Route`) with the bump that creates them, so every commit stays green; E2.4 is the new test alone |
| E2 | The fixture pack without `config.navigation` | The owner's ruling on the E2.3 STOP (above): 11 mirror IDs; the writer pack keeps its copy |
| E2 | `PlayerRecordCompletenessTests` | `Coupled` gains exactly §6's three: route `status`, act `cell_key` and knowledge `identity`. Two other changes let the rest reach the digest alone. `Words` gains the ledger's vocabularies (`ActKinds.Built`, `KnowledgeSources.All`, `Identities.All`) beside the relationship dimensions: they are string constants, not enum keys. `Full()`'s ledger leaves room above its last act (`NextActSeq + 1`), so an act's sequence can move alone. The built companion carries an `unreachable` route |
| E2 | F-E4 compares by value | `EveryPlayerRecordInitProperty_SurvivesEveryWithMethod` compares each property by its JSON. Record equality compares `ConversationMemory.Heard`, an `ImmutableArray`, by reference, and the constructor rebuilds it, so `Equals` reported a drop that was not one |
| E2 | Where the tests live | G11, G28, G29, F-E4, the corrupt, quarantine, row-rejection and definition-pass tests are in `Schema15Tests.cs` (§12.6); F-E1 is in `RoundTripTests.cs`; `PiecesAndErrands_AreProvenByTheirHostCells_AndRebasedByATransition` is in `MigrationTests.cs`, beside its created-instance twin, whose fixture helpers it shares; G12's player half is `EveryPersistedPlayerField_MovesThePlayerDigest` in `PersistedDigestTests.cs` |
| E2 | A malformed piece-chest key | The chest clause of `TryApplyContainer` reads the piece ID with `EntityId.TryParse`, so a hash-valid `container.pce_` key that is no ULID is a rejected row ("its chest is gone") rather than a `FormatException` thrown past the loader (the L-04 class) |
| E2 | `StateDump.Live` | `pieces`, `work_assignments` and `factions` read the world's records and the faction slice until their views land (factions E3, pieces E5, work assignments E8-E9). All three are empty in play at E2 and add no leaf |
| E2 | `ASaveAndALoad_CompareEqual_FieldByField` | Measured: 662 leaves, 660 without the M7 keys, so exactly +2 (`StructureSequence`, `NextActSeq`), as §7.9 states |
| E2 | The older `expected.json` diffs | Reviewed line by line, exactly §7.12's (no S4). In v1-v14: a comma after `posture`, then the 6-line `factions` object; a comma after `noises`, then the three root lines. In v12-v14 also: a comma after the warden's `trail_mm`, then the 17-line `route`. The removed lines are only the closing lines those commas change |
| E3 | Commit order | `FactionSetup`, `SimulationSetup.Factions`, `RuntimeState.StandingOf` and the two explicit `IDialogueFacts` members land in E3.1, with the domain rules: adding `StandingLevel` and `ActDone` to `IDialogueFacts` breaks the World build until both implementations exist. Kera's billet stock row lands in E3.3, with its gate, not in E3.2's content: without `TradeSystem.Withheld` the billets would be sold at neutral |
| E3 | The 0.2.10 rule | FAC001's FAC-R5 (b) refused the fixture pack's `faction.fixture.diggers` reaction to `world.lever.mill_gate`, which no switch in the fixture region sets. The pack dropped that reaction under 0.2.10 (`Fixtures.ContentVersion`, `CurrentContentVersion`, `CurrentContentHash`, the `_aliases.yaml` header and README policy 4 in the same commit); no `expected.json` changed |
| E3 | The act_done description | F4 describes `act_done` by the creature's definition ID ("needs the character to have killed creature.construct.animated_armour; the act log holds none"): the World holds no creature display names, and the quest debugger names creatures by ID throughout |
| E3 | The P script | Built as `Begin` and `Step(run, n)` so that the save-then-continue and replay tests can resume it. Tavar is approached at (145, 43.3), inside talk reach. The raw billet buy is made at Kera's side in P3, before she is told, because from Blackvein Cut the reach check refuses first |
| E3 | `ACompanionsKill_IsNotThePlayersAct` | The fixture armour is a `pack_hunter` placed so it sees the character: a sentinel that has not noticed the character never engages, so Tavar has nothing to fight |
| E3 | `NpcTests.ATraderBoughtOut_StaysEmpty_AcrossASaveAndLoad` | The design's permitted edit (§5.7.2): after the buy-out at neutral, Kera's container holds exactly the three withheld billets, before and after the load |
| E4 | `NavFollower.Next`'s reach | `Next` takes a `reachMm` argument that §3.8's signature leaves implicit: the door step opens only a door within the reach a body interacts at. `NavigationSystem.Follow` keeps the design's signature and passes `InteractReachMm` (1.6 m) |
| E4 | `OpenDoor`'s order of checks | A companion (else "<npc> opens no doors"), a door by that key, reach from the NPC's body, then the already-open no-op: the order §4's `OperatePieceDoor` gives piece doors. The record lives in `Navigation.cs` beside `RoutePlanned` (§3.14) |
| E4 | Turning at a gate | On `OpenGate` the companion turns towards the corner beyond the door, which lies across that line, not the door's centre: the step carries the gate key and the corner, and a piece door (E6) has no authored footprint to look up |
| E4 | Unreachable in route mode | The Phase-1 fallback (the oldest mark, or the character with an empty trail) applies to an unreachable step from route mode's replan as well as from nav mode |
| E4 | `CatchUp`'s route reset | On both of its returns, including the one that finds no spot |
| E4 | `RoutePlanned.Corners` | The corners of the route the mover now follows, after that tick's advance past corners already reached; 0 when unreachable |
| E4 | N-A6 (a)'s save point | 10 ticks after Tavar's first `RoutePlanned`, not 20. In N-A11's script his route is active for about 16 ticks (planned at tick 65; the door open by tick 81 and the route `None`, Direct, by tick 83), so at 20 it would already be `None`. At 10 the route is active and the door still shut; the continuation covers the open, and both worlds publish the same `DoorToggled` |
| E4 | Where E4's tests live | `NoDoorCloses_OnAnyBody` (the authored half), N-A11 and N-A6 (a) are in `tests/Application.Tests/NavigationTests.cs` (§3.20's home). The `OpenDoor` checks are `OpenDoor_ACompanionInReach_OpensAnAuthoredDoor_AndNeverClosesIt` in World.Tests, through `InteractionSystem.Handle` by reflection, as N-W3 reaches `_navigation`; `TestWorlds.HollowSimulation` gains an optional `change` for the player record |
| E4 | G5 and G27 | G5's scripted session has no mover, so the view test asserts no movers and no `RoutePlanned`, and holds a write-attempting `RoutePlanned` view; N-A11 and N-A6 carry the companion's events. G27 subscribes `RoutePlanned` on the new game and on the load |
| E4 | Trail marks on F2 | Read from `Simulation.CaptureRecord().Companions` at most four times a second while F2 is on: no view carries the trail, and §8.14 adds none. The route lines are read from `Navigation.Movers` and the companions' bodies |
| E4 | F1's F2 row | Unchanged ("the grid and its gates") until E5 gives it the design's wording |
| E4 | `FactionContent.cs:421` | E3's own CS8601 (`FilePath = file` with a nullable `file`), fixed with `?? string.Empty` as `ContentChecks.cs:212` does |
| E5 | `PlacementContext` | Carries the player's ID beside the actor's: `RuntimeState` holds no player ID |
| E5 | Rule 1's "dead" | Never met at a command boundary: a death and the return at the Waystone are one tick. The test covers the unknown actor |
| E5 | Support refusal texts | Beyond the wall's: an edge mount "a {family} needs a floor pad on one side" (wall, doorway); a square mount "a {name} needs a floor pad under it"; a door "a door needs a doorway" |
| E5 | `PieceView.Parts` | `PiecePartView`s with no part IDs: a part ID embeds the `pce_` value, which the replayable dump cannot mask |
| E5 | Protected-zone labels | "the spawn point", "{NPC name}'s place", otherwise the site key or node name. Patrol legs are sampled every 500 mm (⌈length / 500⌉ steps, ends included) |
| E5 | BLD002 and BLD003 | BLD002's closed set holds the envelope (`id`, `kind`, `schema`, `display_key`, `tags`, `notes`, `defines`, `deprecated`) and `name`. BLD003 also requires a mount's or door provider's axis to be x at turn 0, pads and roofs to have no parts, and bounds to be the union of the parts |
| E5 | `Space` with nothing built | The authored `WalkSpace` itself, not a copy |
| E5 | Load-audit texts | "{def} is not a piece this build knows", "health X is above {name}'s Y: clamped", "a {family} is not a door: its door_open is read as shut", "it is off the building grid", "it stands outside every build area", "it stands over {id}", "it stands on ground kept clear ({what})", "nothing holds it up now" |
| E5 | Tests on edited setups | `EachPlacementRule` and `Preview_EqualsTheCommand` (0.10 m relief, a post at (97.5, 103.5), Renn moved to (97.5, 97.5), room for six pieces); Kera behind a wall (Kera at (100.5, 101.2), NPC protection 0); `Creatures_AreBlockedByPieces` (a roamer route across the wall line, added after placement); G9 (a spawner on the wall). The shipped area's relief was cross-checked against the region grid for every square (228 and 96 as §4.8 states) |
| E5 | Words | Dotted IDs become display names through `BuildMode.Words` (`DottedId`, now internal so `--build-shots` checks with the same pattern), used by `Main`'s refusal filter and the status line |
| E5 | The target line and F2 | No "[T] mend" until E8 binds `build_repair`. F2's markers are the provider sockets, ID tails within 12 m and the last change's rectangle; work-anchor discs come with stations (E8). `StructuresView.SetDoor` is a no-op until E6 hangs doors |
| E5 | The `building` perf segment | Built (`PerfActivities.BuildAtTheCrossing`, the `building` segment) and run only in the E10 ASTRAL trial and the owed RAZER window |
| E5 | `LayoutCheck` | Checks the build panel's rectangle on screen; F1's BUILDING fit is its existing help-panel check |
| E5 | The table as data | `CrossingWorkshop.Rows` holds E5's rows (R00-R05, R09-R12, R14, R39-R42, R45, R46); each row and action names its slice, and E6-E9 add theirs (the door commands of R06, R10, R11 and R13 among them) |
| E5 | R46's save point | 60 ticks after Tavar's order, not 100. At 100 he has just come into clear view of the character through the doorway (headless: his route is `Active` to tick +99 and `None` at +100), so the save would miss "his route `Active`" by a tick. At 60 he is in the west corridor with a margin either way; the step-10 assertions are otherwise as written. The same kind of move as E4's N-A6 (a) |
| E5 | G8's "no `itm_` minted" | `WorldDelta.Registry` is internal to World, which `Application.Tests` cannot reach, so the raw-digest replays assert it through the state: no item ID appears in the dump that was not there before the window. `PreviewsInterleaved_ChangeNothing` compares `NavCounters` by its JSON (its dictionaries compare by reference) |
| E5 | R-A7 at a corner | `Kinematics.Step` resolves a push in doubles and rounds the body to whole millimetres, so at a convex corner a body can sit less than a millimetre inside contact (measured: 0.39 mm at the workshop's north-east corner). That is Phase 1's step, the same against an authored box. The chase test allows the rounding and nothing more (an overlap of 1 mm or more fails); it fails at 187 mm when the creature steps read the authored space |
| E5 | The chase | The wolf starts at (104, 93), 5 m from the lap, so it hears the character's run (8 m); its spawn facing is seeded, so sight alone may never find the character |
| E5 | b08's digest | Presentation may not drain commands (`PresentationSource_NeverReachesPastThePublicReadAndCommandSurface`), and the digest carries the tick, so "`StateDigest` unchanged" is asserted headless (`CrossingWorkshop_1to3` reads the digest either side of the command, before the tick). Windowed, b08 asserts the pieces, the sequence and the pack unchanged |
| E5 | F1 and build mode | The help panel is not one of the panels that end build mode (`Main.Modal`: the inventory, a conversation, the character sheet, the saves list), so b03's F1 still is taken over build mode |
| E5 | `--build-shots` timing | Rows play a tick a frame. A row's outcome is checked a frame later (`Then`), and a row's `+n` wait counts ticks from the previous row's last action, so a still in between shifts nothing. Each run records its own ticks in `commands.tsv` (tick, row, command). The files: `state_saved.json`, `state_replay.json`, `state_digest.txt` at the save; `state_continued.json` and `state_continued_digest.txt` 600 ticks on; the relaunch writes `state_loaded.json`, `state_diff.txt`, `state_continued_loaded.json`, `state_continued_diff.txt`, `transcript_verify.md` and `commands_verify.tsv`. `Hud.Toasted` carries each notice to the words check |
| E5 | S0 | Written by `TheCrossingWorkshopStart_IsWrittenOnlyWhenAsked` under `UNNAMED_WRITE_BUILD_START=1` (refused under `CI=true`) through `SaveStore`, as the `quick` slot; `GameSaves/README.md` has its row. `Main` copies it into `<dir>/profile/quick` before `Boot`, and both `--build-shots` and its relaunch load `quick` by name (exit 2 when they cannot) |
| E5 | The playthrough's still | `Still` gains `factions: false`, so `26_first_build` is taken without F6 |
| E6 | `CanOperate` | Takes the player's ID beside the operator's, as `PlacementContext` does: `RuntimeState` holds none. The errand clause lands with errands (E9) |
| E6 | `OperatePieceDoor` | Reach is measured from the operator's body to the door's shut box, as for an authored door; the texts are §4.9's ("there is no such door", "that door is not yours", "the door is 1.80 m away; reach is 1.60 m", "the door cannot close: something is in the doorway"), the distance written invariantly |
| E6 | Closed leaves | `ClosedDoors()` appends `RuntimeState.ClosedPieceLeaves` only when there is one; a toggle rebuilds only that cache (`SetClosedPieceLeaves`), and `Rebuild()` derives it the same way |
| E6 | Door gates | Every placed door's leaf is a navigation gate keyed by its piece ID; `IsGateOpen` reads the row. `Navigation.Gates` (F2) lists the placed doors after the authored ones |
| E6 | Where E6's tests live | `ACompanion_OpensTheOwnersPieceDoor` and G26 are in `NavigationTests` beside N-A11, sharing `BehindAShutDoor` (a line of three pads, walls and a door; the way round twice the way through). The piece-door half of `NoDoorCloses_OnAnyBody` is its own test, `NoPieceDoorCloses_OnAnyBody`: a door cannot be placed on a body, so it is saved open and each case loaded with a body in the doorway. G26's "at most one `OpenDoor` a tick" is asserted as the stuck count climbing at most one a tick |
| E6 | The view test | Its sessions load `builder_start`, the start with four timber taken first: taking from an untouched container mints the stack's IDs, which the two sessions would not share. The door segment runs before the random script, from the longhouse door to the crossing and back |
| E6 | `--build-shots` | b06 is `b06_roofs_and_door`. The eye check skips an open door's leaf, which has swung out of its shut box. b09 asserts the leaf's drawn position through `StructuresView.LeafCentre` |
| E6 | The door prompt | "[E] Open the Timber Door": `Main.Describe(session, key)` names a `pce_` key by its definition |
| E7 | V-N1's seeds | The ring round the edit, as §3.13 says: a new pocket borders the change, and a placement is one piece. N-D19's doorless hut is three walls standing and the fourth added; four walls added in one edit would enclose their pocket inside the ring, which no placement can do |
| E7 | Naming the pocket | A protected point is in a pocket when its nearest walkable node after the edit, within its reach (by distance, then j, then i), lies in it |
| E7 | V-N2 (b) | Asked only where the edit can lower walkability: where the point's reach meets the added solids grown by the largest planning radius. Fits change nowhere else, so the verdict is the same |
| E7 | The points' words | "you"; the NPCs' names; "the waystone"; containers, stations, nodes and switches by their keys in words ("the timber stack"); corpses "the remains"; ground stacks by their item's definition, which presentation's words turn into its name; authored doors "the forge shed door"; placed doors "the Timber Door". The candidate's own sites (E8) and work places (E9) join with their pieces and errands |
| E7 | The refusal's rule | §6's `PlacementPreview` carries no rule, so the tests (and b14) read V-N1 from the command path's `EditRefusalsByRule` |
| E7 | The check's cost | Its first version measured a 4.1 ms median and an 18 ms worst in Release. Made cheaper in two rounds, every verdict unchanged (the N-D19/N-D20 cases, the workshop's refusals and words, and a byte-identical `--build-shots` replay): the scratch arrays held directly, neighbours by index with no call per neighbour, the tile's bytes kept at hand, the added solids' reach as a rectangle, V-N2 (b) only where affected, a pocket named only by points whose reach meets its box, and the second flood stamped in the scratch and stopped at the first node of a component already found open after the edit (such a node was walkable, and joined to it, before the edit too). The second round followed CI's failure of the 6 ms bound (run on `1f5229e`). `NavScratch` gains `Before`, the second flood's stamps |
| E7 | Preview parity over rule 15 | The forty poses stay on the edited rules; the hut and its fourth wall follow on the game's own, which have the room and the timber for it |
| E7 | BLD008's cost | About 43 ms a content load in Debug (a second grid of the region and two floods from the spawn): 215 to 259 ms |
| E3 | FAC001's cost | FAC001 reads the layouts, spawns, NPCs, dialogues, quests and merchants once per validation (about 30 ms on the shipped content) |
| E8 | The replayable dump's one mask | It masks `station.pce_` keys as well as `container.pce_` ones. A bench's key, in `PieceView.StationKey` and in `StateDump.Live`'s new `piece_stations`, embeds its piece's ID, which a fresh run mints anew; unmasked, two fresh runs would differ |
| E8 | `SystemContext.Stations()` | The authored stations in content order, then each bench, in the `StructureOrder` of its one part, at that part's centre |
| E8 | BLD005's work anchor | Clearance is `config.navigation`'s person radius plus 300 mm (650 mm), from every part and from the ±1300 faces. The check is skipped when `config.navigation` does not build, which the NAV codes report |
| E8 | Protected points | A new bench's work anchor is a `NewSite` (radius 350 mm, reach 250 mm). Standing chests and benches are `Reach` points, "the chest" and "the bench" |
| E8 | The planner (`fb16e33`, `8573229`) | Under the E1 ruling's path, after Kera's routes took 22 ms a plan in Debug (CI bound 45 ms; CI has run up to 10 times slower). A* keeps one heap entry per node and moves it up on a shorter way; the pop order is unchanged, since the key (f, h, index) is a total order and a node's better entry always popped first. (f, h) is one `long`; the loop runs in window coordinates; a diagonal reuses its two orthogonals' walkability. All 231 plans N-A10 makes have equal expansions, probe nodes and paths before and after. The heap holds at most a window's nodes (was 8 × the cap), plus `NavScratch.HeapAt`, 4 bytes a window node |
| E8 | T10's script | Aimed at the world, not uniform. It builds round the character, walks up to a piece before a blow, a store or a door, mends what is damaged, fetches timber from the stack, and places at random one time in six. Seeds 1-5 place 20-54 pieces, strike 157-198 times, destroy 1-6 and store 2-9 times; the test asserts a minimum of each. "Replay" is the same script run again, since the log's item IDs are minted anew |
| E8 | R30 and R35 wait 20 ticks | A blow lands at its swing's end, 8 ticks after it is asked for. §4.22's "+1" and "pose" would read R29's blow and the tenth of R34 before they land |
| E8 | The step-11 replay | A logged pick-up of an item the run minted (the spilled timber, R35) names the replay's item of the same definition and count at the same place (R-B4). The runner records what each pick-up took |
| E8 | The view test | `HangAndWorkADoor` adds two blows on the hung door, its mending, and blows until it is destroyed; `builder_start` takes five timber, one for the mending |
| E8 | `ADestroyedChest_…` | The spilled timber, picked up, joins the carried stack of its kind; the ingot, alone of its kind, keeps its ID into the pack |
| E8 | `ABlowThatHitsACreature_…` | The wolf is loaded in after the building: a spawner's disc is ground kept clear |

## Local risks (not promoted to RISK_REGISTER)

| Risk | Where it is tracked |
|---|---|
| Tier hysteresis is unsaved but gates the companion and creatures (owed before M9) | §15, R-X18 |
| The companion's conversation hold reads the transient conversation (pre-existing) | §2.16 |
| Kera's walk-home plan: 6.0 ms in Release on this machine (E8, N-A10), one plan over the 4 ms tick when it happens | §3.18, §14; the in-game plan measured in E9 |
| CI runner variance on the 3× budgets: `SixtyCreatures_TickWithinTheBudget` failed once at 4.94 ms (run 36237433970, first attempt; 0.49 ms here) and passed on the re-run | Here. A second failure on a re-run would be S7 |
| The character's regeneration clocks are not saved (Phase 1): a save taken in a fight resumes regeneration early | "STOP - E8.5, S9 and S2" |
| Resolved: `AsyncSaveTests.AQuicksave_AskedForDuringAnAutosave_WaitsItsTurn_AndHoldsTheLaterWorld` (Phase 1, P-01) failed intermittently in full local runs from E3.2 on. The background save ran on the shared thread pool and waited up to 13 s just to begin | "AsyncSave record" |
| `Kinematics.Step` rounds a body to whole millimetres after resolving a push, so at a convex corner a body can sit under 1 mm inside contact (Phase 1). E9's step 6 asserts Kera `IsClear` every tick, which is strict, and her route turns corners inside the workshop | Here; measured in E9 against the rows it would fail, before any change |

## Residues and deferrals

See §5.19 and §16. They are recorded here as each slice lands.

| Slice | Residue |
|---|---|
| E3 | A companion's kill is not the character's act: if Tavar lands the killing blow, nothing can be reported |
| E3 | No history before M7: a migrated save starts with an empty act log and every faction neutral, whatever it had already done |
| E3 | A Kera wares container already traded with before M7 is a persisted record that never re-reads stock, so it never shows the billets. A Kera bought fully out under a pre-audit build left no record, so it shows the full authored stock, billets included, until bought out again (M-01) |
| E3 | Knowledge is pooled at once (K6): Kera, told at the bench about 62 m from the seat, moves the Waystation at that tick (D30). Unobservable in shipped content |
| E3 | Every faction learns by report; NPCs do not notice deeds on their own until M9's witnessed channel |
| E3 | Relevance is fixed at boot; an act evicted from the 256-act log can no longer be reported |
| E3 | All new dialogue text is DRAFT for the owner's tone review; Sel's `notes` line is a lore hook the owner may reject |
| E3 | The live tier reaches the character only through the HUD line and F6 (no faction screen in M7) |
| E4 | The companion plans only when neither the character nor a trail mark is in clear view (and keeps an active route until the character is). C16 and the M6 tests still run through Direct and Trail almost everywhere |
| E4 | NPCs never close doors: a door Tavar opens stays open until the character closes it |
| E4 | The mid-route save proves the companion's half of G15; the errand's half (N-A6 (b)) and Kera's routes against the two-thirds bound are E8-E9 |
| E3 | `o_ore` and `o_billet` (Quest 1) are `acquire_item` objectives with `or_item_refs`: once the Waystation is accepted, a bought billet satisfies both - reachable only after the armour kill and a report. `o_spear` still needs the anvil (C-01) |
| E5 | No piece art: every placed piece and the ghost are greybox (4 of 4 `piece:*` coverage entries fall back), reported in every run |
| E5 | Creatures never path: a chaser slides along a wall and can lose the character behind the workshop (R-A7, accepted) |
| E5 | The vestibule and every sealing refusal wait for E7's check 15 |
| E6 | NPCs never close doors: a door Tavar opens stays open until the character shuts it. No locks or keys |
| E6 | Every door is passable to a door-opening mover whoever owns it: a foreign door (a crafted save only) stalls the companion to his snag catch-up (G26) |
| E7 | The check treats every door and barrier as passable: a room whose only way in is a door is open, whoever may open it |
