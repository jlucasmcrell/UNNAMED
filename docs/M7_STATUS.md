# M7 status - Factions, Reputation, and Building v1

**Date:** 2026-09-25.
**Authorization:** owner authorization of 2026-09-25 ("M7 is now explicitly AUTHORIZED").
**State:** in progress. E0 done. E1 stopped at N-A10 on 2026-09-25, and the owner resolved the STOP the same day (see "STOP - E1, N-A10" and the ruling after it); E1 continues under the corrected bounds.

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

## Slices

| Slice | Name | State | Commits | Tests (total) | Lint definitions | Notes |
|---|---|---|---|---|---|---|
| E0 | The rulings on paper | done | `b0846a5`, `3515f03`, `13a5cca` | 825 (unchanged) | 102 | Documents only. Draft PR #8 CI green (`build-and-test`, run 36178835111) |
| E1 | Navigation you can see | done | `521d7d0` (E1.1), `f279a4d` (E1.2), `7766e95` and `9e2a7f1` (E1.3), `355fda5` (E1.4) | 867: Domain 161, Application 206, Persistence 173, Content 160, World 64, Presentation 57, EntityRegistry 23, Architecture 23 | 103 | Stopped at N-A10, resolved by the owner's ruling the same day (below). See "E1 evidence" |
| E2 | Schema 15, landed once | done | `d17a67e` (E2.1), `f7931e1` (E2.2), `e5f221d` (E2.3), `fd12b6c` (E2.4), `8ff394b` (E2.5) | 899: Domain 162, Application 209, Persistence 198, Content 160, World 67, Presentation 57, EntityRegistry 23, Architecture 23 | 103 | Stopped at E2.3 (S9) and E2.4 (S9/S10), both resolved by the owner the same day. See "E2 evidence" |
| E3 | Factions v1 | done | `a36d286` (E3.1), `c7e088d` (E3.2), `8dea83a` (E3.3), `f922db0` (E3.4), E3.5, E3.6 | 952: Domain 169, Application 229, Persistence 198, Content 183, World 67, Presentation 57, EntityRegistry 23, Architecture 26 | 106 | Stopped at `m7_armour` (S5), resolved by the owner (option (a), the beat tuned). See "E3 evidence" |
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

## E3 evidence (2026-09-26, the machine that ran E1 and E2)

| Proof | Result |
|---|---|
| `dotnet build src/UNNAMED.sln` | 0 errors; the one warning is the pre-existing `MagicContent.cs(165,21)` CS8602 |
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
| E3 | FAC001's cost | FAC001 reads the layouts, spawns, NPCs, dialogues, quests and merchants once per validation (about 30 ms on the shipped content) |

## Local risks (not promoted to RISK_REGISTER)

| Risk | Where it is tracked |
|---|---|
| Tier hysteresis is unsaved but gates the companion and creatures (owed before M9) | §15, R-X18 |
| The companion's conversation hold reads the transient conversation (pre-existing) | §2.16 |
| Kera's walk-home plan is modelled at about 4.6 ms on ASTRAL, one plan over the 4 ms tick | §3.18, §14; measured in E9 |
| `AsyncSaveTests.AQuicksave_AskedForDuringAnAutosave_WaitsItsTurn_AndHoldsTheLaterWorld` (Phase 1, P-01) intermittently fails in full-solution local runs from E3.2 on: "Expected: 1, Actual: 0" at `Held.WaitReached(1)`, which allows the background save 5 s (500 × 10 ms) to reach its hook. Measured on this machine, with about a third of its 16 logical cores busy with other work while idle: 0 of 3 at `8ff394b` (E2), 1-2 of 3 at E3.3, and 1 of 3 with `FactionTests` excluded. It passes alone every time, and CI has stayed green. E3 made test boots heavier (FAC001) and added heavier tests, so the save worker is starved longer under load | Here. Not changed: the test is Phase 1's, outside §12.10's permitted edits. If it reaches CI, the fix is to give `WaitReached` a longer budget, an owner decision |

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
| E3 | `o_ore` and `o_billet` (Quest 1) are `acquire_item` objectives with `or_item_refs`: once the Waystation is accepted, a bought billet satisfies both - reachable only after the armour kill and a report. `o_spear` still needs the anvil (C-01) |
