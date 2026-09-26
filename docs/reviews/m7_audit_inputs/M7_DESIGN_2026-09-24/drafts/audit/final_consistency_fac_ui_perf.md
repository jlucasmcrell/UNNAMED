# M7 design - final cross-section consistency audit B (factions, UI, content, performance, slices, criteria, tests, runtime proof)

Target: `G:/UNNAMED_HISTORY/M7_DESIGN_2026-09-24/M7_IMPLEMENTATION_DESIGN.md` (7,522 lines). Read fully: §5, §6, §7, §8, §9, §10, §11, §12, §13, §14, §15, §16 (plus §1, §2.17-2.18, §3.17-3.20, §4.12-4.13, §4.20-4.22, §17, §18 for cross-checks). Content and code facts spot-checked against the `e10d2c4` snapshot (Sel, Kera and Tavar dialogue, Kera's stock, the iron ingot, `Playthrough.cs` beats and legs, `Main.cs` bindings, the 102-definition count). Settled rulings (00_SCOPE_RULINGS, 01_LEAD_RULINGS_ON_AUDITS) are not re-reported.

Leftover hunt result: the witnessed channel, deadfall, shot/formula damage, `IsSettled` and the player faction screen appear only as M9/deferred/dropped items, with one exception (finding H1: a witnessed/unidentified fixture row claimed as M7 evidence). Optional instruments are presented as optional everywhere except the soft wording in L15.

Counts: 3 high, 5 medium, 18 low.

---

## HIGH

### H1. §5.3.3 claims the v14 fixture stores a `witnessed` and an `unidentified` knowledge row; §5.12 and §7.12 specify two `reported`/`identified` rows
- Line 2394: "The v14 fixture (section 7) stores one `witnessed` and one `unidentified` row so both keys round-trip; M7 code never writes either."
- Line 2750: "two `reported`/`identified` knowledge rows, with act 2 known by no faction".
- Line 4025 (the normative v14 player table): both knowledge rows are "`identified`, `reported`" (diggers via smith, delta -100; keepers via warden, delta +100). Line 3581: "M7 writes only `reported`/`identified` and stores no other witness field."
- Why it matters: §7.12 fixes the fixture values, the F-E2 M7 block and every leaf count; an implementer following §5.3.3 would add rows that break "act 2 known by no faction" and the stated expectations.
- Fix: replace the sentence at line 2394 with: "The v14 fixture (§7.12) stores two `reported`/`identified` rows. The `witnessed` and `unidentified` keys are exercised by unit row U1 and by the crafted save of fixture row F4 (`AnUnknownActor_MovesNoStanding_UntilIdentified`), which is saved through `SaveStore` and resumed; M7 code never writes either."

### H2. T2's CI assertion is "< 12 ms" in §11 and §12 but "< 6 ms" in §14, which owns the test
- Line 6301 (§12.8): "CI mean < 12 ms; ASTRAL evidence ≤ 2 ms". Line 5977 (criterion 33): "T2 is green at CI < 12 ms mean."
- Line 6916 (§14.12.3, T2 step 5): "**CI:** mean < 6 ms." Line 6840 (§14.11): "mean ≤ 2 ms | mean < 6 ms". Line 7035: "(CI mean < 6 ms)". The CI rule at lines 6053 and 6890: "Times are asserted only at 3× the ASTRAL target" (3 × 2 ms = 6 ms).
- §11's own decision (line 5748) makes this a STOP S9.
- Fix: line 6301 → "a tick with a full build area and the 60-creature crowd moved to x ≥ 120 m: CI mean < 6 ms (3× the 2 ms ASTRAL target); ASTRAL evidence ≤ 2 ms". Line 5977 → "T2 is green at CI < 6 ms mean."

### H3. E4 edits `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame`, but E4's STOP, criterion 7 and §2.18 forbid any edit to a companion test
- Line 5322 (E4 STOP): "Any existing companion test fails or needs an edit." Line 5793 (criterion 7): "C16 and every existing `CompanionTests` case pass unmodified." Line 477 (§2.18): "Existing tests named elsewhere as edited are the only permitted Phase-1 test edits: ..." (list omits `HisState_…`, `TheProbesContentMirror_IsTheFixtureContentPack` and `KnownDirectories_Is_Closed_Set`); line 484: "`ThroughTheLodgeAndRoundIt_…` and every other `CompanionTests` test" unchanged.
- Line 5307 (E4 tests): "`HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame`, which gains the route assertion". Line 6330 (§12.10 permitted edits): "the route assertion (E4)". Line 1238: "Changed by name: `HisState_…` (`CompanionTests.cs:285`) gains `Assert.Equal(before.Route, after.Route)`". The test lives in `tests/Application.Tests/CompanionTests.cs:285` (snapshot).
- An implementer doing E4 as written must stop on its own planned edit.
- Fix:
  - line 5322 → "Any existing companion test fails, or needs an edit other than the route assertion §12.10 permits in `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame`."
  - line 5793 → "C16 and every other existing `CompanionTests` case pass unmodified; `HisState_…` changes only by its permitted route assertion (§12.10)."
  - line 477 → "The only permitted Phase-1 test edits are those §12.10 lists by name." Line 484's row → "... and every other `CompanionTests` test (`HisState_…` gains only its route assertion, E4)".

---

## MEDIUM

### M1. Four criteria close in a slice before all of their named evidence exists
Rule (line 4894): "A criterion closes in the slice after which all its evidence exists".
- Criterion 2 closes in E1 (line 4883) but names N-A7 `TheSeam_SurvivesAReload` (line 5768), which lands in E5 (line 6091).
- Criterion 4 closes in E8 (line 4890) but names N-A3 (line 5781), which lands in E9 (line 6088).
- Criterion 12 closes in E7 (line 4889) but names N-A13 case (c) "from E8" and `CrossingWorkshop_7_TheVestibuleIsRefused` "(from E9: ...)" (line 5824; lines 6097, 6160).
- Criterion 25 closes in E3 (line 4885) but names `KeraAtWork_TradesAtTheBench_AndHerWaresStayHome` (line 5932), which lands in E9 (line 6156).
- Fix (§10.1 "Closes criteria" column): E1 "-"; E3 "22-24, 26, 27"; E5 "2, 3, 7, 9, 11, 20, 35"; E7 "-"; E8 "8, 16, 18"; E9 "4, 6, 12, 13, 14, 15, 17, 25, 28, 30, 36". (Alternative: drop N-A7 from criterion 2, N-A3 from 4, the E8/E9 parts from 12 and `KeraAtWork_…` from 25, and name them under the later criteria instead.)

### M2. `ALayoutEditUnderASavedPiece_IsAuditedAndKept` (lands in E5) is missing from the test matrix and from E5's test list
- Line 7180 (§15 tests): "`ALayoutEditUnderASavedPiece_IsAuditedAndKept` | Application.Tests | R-B6 ... Lands in E5". R-B6 (line 7084) names it as its only proof.
- Line 6032 (§12): every test "appears exactly once below"; §12.3 (lines 6106-6166) has no such row. Line 5399 (E5): "Tests. Every §12 row marked E5." So E5 never builds it.
- Fix: add to §12.3: "| `ALayoutEditUnderASavedPiece_IsAuditedAndKept` | App | E5 | R-B6: an authored blocker added under saved pieces and a lowered `health_max` are audited, kept and clamped; no M7 event on load | persistence |", and add it to E5's outline (line 5403, beside `Building_RoundTrips…`).

### M3. FAC001's refusal text for a `witness` block differs between §5 and §9
- Line 2890 (§5.14, FAC001's owner): "`witness` is refused by name: \"the witnessed channel is M9\"."
- Line 4771 (§9.8): "Its closed field set refuses a `witness` block by name: \"witnessed knowledge is not built in M7 (M9)\"."
- `FactionContent_RefusesDataModelFieldsItDoesNotBuild` (lines 3066, 6209) checks this refusal; S10 treats reason texts as asserted.
- Fix: line 4771 → "Its closed field set refuses a `witness` block by name with §5.14's text: \"the witnessed channel is M9\"."

### M4. §5.13 and §9.8 give different contents for `content/config/factions.yaml`
- Lines 2756-2778 (§5.13): an 11-row `ladder:` list, `notes: Factions and reputation (M7; PROGRESSION.md §10). One ladder for every faction; ... no rumour, no telling between factions.`, `acts:\n  log_capacity: 256`.
- Lines 4747-4752 (§9.8): `ladder:` followed only by YAML comments (the key parses as null, which FAC001 refuses: "ladder holds exactly StandingLadder.Keys", line 2887), and a different `notes:` string ("... Standing is whole points; ... no witnessing (M9), no rumour ...").
- An implementer copying §9.8 ships content that fails FAC001 (E3 STOP "FAC001 rejects shipped content"), and the two `notes` strings give different content hashes.
- Fix: replace lines 4747-4752 with "# config.factions (E3): exactly the file in §5.13 (the 11-row ladder, points, acts, and its notes text)."

### M5. Criterion 23's "no witness code exists" bullet has no deciding evidence
- Line 5918: "No witness code exists: no `WitnessRules` type and no `BestWitness` or `IsSettled` member exists under `src/`, and FAC001 refuses a `witness` block."
- Line 5919 (its evidence): `AFactionThatNeitherSawNorWasTold_…`, `AReport_GoesOnly…`, `ACompanionsKill_…`, `FactionSystem_ReadsNoSightNoBodiesAndNoFacing` (which scans `Factions.cs` for `SightWalls`, `Perception`, `.Body`, `FacingMdeg`, `State.Npcs`, `State.Conversation`, `Dispatch(`; line 2515), `FactionContent_Refuses…`. None checks for `WitnessRules`, `BestWitness` or `IsSettled`.
- Line 5744: "Each one is decided by a named test, a named run, or a command with an expected output."
- Fix: extend `FactionSystem_ReadsNoSightNoBodiesAndNoFacing` (§5.16, §12.4) with: "and no type named `WitnessRules` and no member named `BestWitness` or `IsSettled` is declared in the Domain or World assemblies (reflection)", and add that clause to criterion 23's evidence line.

---

## LOW

### L1. The E3 `--playthrough-verify` field estimate is "about 470" in §7 and "about 500" in §10 and §13
- Line 3942 (§7.9): "From E3: + 37 for the ledger ... = 464, plus about 3-9 ... : **about 470**." Line 4113 (F-E9): "427, then about 470". Line 4144 (§7.17): "`--playthrough-verify` at about 470".
- Line 5257 (E3): "verify shows 0 differences over about 500 fields (§13.8)". Line 6554 (§13.8): "E3-E4: about 500 (+37 for the ledger, about 28 for Kera's materialised wares record, the bought billet, ...)".
- §7.9 omits Kera's wares record (4 + 6 stacks × 4 = 28 leaves) that `m7_tell_kera`'s purchase materialises.
- Fix: line 3942 → "From E3: + 37 for the ledger (...), about 28 for Kera's wares record materialised by the `m7_tell_kera` purchase (4 + 4 per stack × 6 stacks), the bought billet, and a few dialogue-memory and relationship rows: **about 500**. From E5: about 540 (§13.8)." Lines 4113 and 4144: "about 470" → "about 500".

### L2. `build_repair` (T) lands in E8, but §8.15 and the key-binding test say the build rows land in E5 and only E9 is added later
- Line 4502: "Rows land with their actions: F2 in E1, F6 in E3, the build rows in E5, and Y in E9." Line 4560: `EveryM7Action_IsBoundToADirectKey` "lands in E5 with `build_debug` (E1) and `faction_debug` (E3); E9 adds `work_order`". Line 6289: "E5 (+E9 `work_order`)".
- Line 4238: `build_repair` slice E8. Line 5373 (E5): "the F1 BUILDING section (its build rows except Mend and Y)". Line 5569 (E8): "the `build_repair` action (T) and its F1 row".
- Fix: line 4502 → "the build rows in E5 except Mend, Mend in E8, and Y in E9"; line 4560 → "... E8 adds `build_repair`; E9 adds `work_order`"; line 6289 → "E5 (+E8 `build_repair`, +E9 `work_order`)"; add "`EveryM7Action_…` gains `build_repair`" to E8's tests (line 5584 list).

### L3. T4 is an optional instrument in §14 and §16 but missing from §10.16, §12.11 and §18's lists
- Lines 6969, 7038 (§14): "T3, T4, T5 and T7 as separate tests"; T4 `AnEditThatReplansBothMovers_StaysWithinTheTickBudget`. Line 7286 (§16.8): "T3, T4, T5, T7 and T8".
- Line 5704 (E10 OPTIONAL): "T3, T5, T7 and T8"; line 6349 (§12.11) lists T3, T5, T7, T8 only; line 7506 (§18 Not in M7): "T3, T5, T7 and T8". §12's decision (line 6032) requires every test to appear once.
- Fix: add T4 `AnEditThatReplansBothMovers_StaysWithinTheTickBudget` to line 6349's row (switch-on as §14.13), and make lines 5704 and 7506 read "T3, T4, T5, T7 and T8".

### L4. §9.10 misplaces the reputation fixture factions and omits the third one
- Line 4831: "E3 (section 5). The hand-built `faction.fixture.keepers` and `faction.fixture.delvers` inside `ReputationTableTests`."
- Lines 2926-2927: the fixture setup is "in `tests/Application.Tests/ReputationFixture.cs`"; line 2944 (F12) adds "a third fixture faction C (`faction.fixture.watchers`)".
- Fix: line 4831 → "E3 (section 5). The hand-built `faction.fixture.keepers`, `faction.fixture.delvers` and `faction.fixture.watchers` in `tests/Application.Tests/ReputationFixture.cs` (shared by `ReputationTableTests` and `FactionTests`)."

### L5. FAC001's rule inventory differs across §5.8, §5.14 and §6.1.10
- Line 2672: "It adds five rules ..., each enforced by FAC001: R1..R5". Line 2917 (§5.14): "R1, R3, R4 and R5 as stated in §5.8" (no R2 check and no R2 test in §5.16/§12.4). Line 3296 (§6.1.10): "FAC001 (R1-R5, FAC-M1 ..., FAC-M2 ..., `APlacedPieceReaction_IsRefused`)" omits FAC-R5, which §5.14 (lines 2900-2902) defines.
- Fix: line 2672 → "It adds five rules for every faction read or write. FAC001 enforces R1, R3, R4 and R5; R2 holds structurally, because `SocialContent.Conditions` gains only `reputation` (bounded by R1) and `act_done`, and no condition reads knowledge." Line 3296 → "FAC001 (R1, R3-R5, FAC-M1 seat placement, FAC-M2 no companion member, FAC-R5 single-instance subjects, `APlacedPieceReaction_IsRefused`)".

### L6. §9.6 attaches BLD009 (an area-sum lint) to piece definitions
- Lines 4705-4708: `piece.pad.timber` "BLD001-BLD004, BLD009"; wall, doorway, roof "same".
- Line 1942: BLD009 sums `max_pieces` over build areas per cell and region; line 4722 attaches BLD009 to the `build_areas` block.
- Fix: lines 4705-4708 lints → "BLD001-BLD004".

### L7. §7.11 names fewer slices whose lints may reject the fixture pack than §10.4
- Line 4011: "If a later lint (FAC001 in E3; BLD001-BLD009 in E5 and E7) rejects it".
- Line 4975: "(FAC001 in E3; BLD001-BLD009 in E5, E7, E8 and E9)"; BLD005 lands in E8 (line 5550) and E9 (line 5624).
- Fix: line 4011 → "(FAC001 in E3; BLD001-BLD009 in E5, E7, E8 and E9)".

### L8. RK-06's "navmesh rebuild" rewording is an E0 edit in §10.6 but an E10 edit in §15.4
- Line 4999 (E0): "RK-06's \"navmesh rebuild\" becomes \"domain grid restamped per footprint change\"."
- Lines 7152-7153 (§15.4 under **E10**): "\"navmesh rebuild on placement\" becomes \"the domain navigation grid, restamped over the changed rectangle per footprint change (≤ 108 nodes per piece)\"". Line 7051: ruling-derived text lands in E0, evidence text in E10.
- Fix: move line 7153's bullet to §15.4's E0 list with §10.6's wording ("domain grid restamped per footprint change"), keeping only "(≤ 108 nodes per piece)" and the other evidence items under E10.

### L9. Stale working-draft references
- Line 5697: "The J.12 RAZER checklist prepared"; line 6317: "the RAZER capture (section 14, J.12)". No §J.12 exists in this document; the checklist is §14.14.
- Line 3438 (G8): "(N-A8, `CrossingWorkshop_0and11`, the H4 plain run)". "H4" is B's draft guard, which this document names `PreviewsInterleaved_ChangeNothing`.
- Fix: "J.12" → "§14.14" at both lines; "the H4 plain run" → "the plain run of `PreviewsInterleaved_ChangeNothing`".

### L10. Dropped fixture row F7 is not recorded
- Lines 2935-2953 (§5.15) contain F1, F2, F4, F5, F8, F10-F14; line 2955 covers K7 by `Learn_WithoutARow_StoresNothing`.
- Line 6362 (§12.12) and line 7500 (§18) list only "fixture rows F3, F6, F9" as moved to M9. F7 (draft: "north stone turned; both watch", a witnessed K7 row) is also gone.
- Fix: lines 6362 and 7500 → "fixture rows F3, F6, F7, F9 (F7's K7 case is covered by `Learn_WithoutARow_StoresNothing`)".

### L11. G27's slice list omits E6, which E6 says extends it
- Line 6242: "G27 ... | E3 (+E4, E5, E8, E9 types)". Line 5477 (E6): "The view test and G27 gain `DoorToggled` with a `pce_` key."
- Fix: line 6242 → "E3 (+E4, E5, E6 `pce_` `DoorToggled`, E8, E9 types)", or delete "and G27" from line 5477 (DoorToggled is a reused Phase-1 type, and G27 covers "every M7 event type", line 3457). Pick one.

### L12. Q2's latest point differs in R-X2
- Line 7100: "Latest decision points: Q2 before E0; ...".
- Line 7335: "before E0 writes the ROADMAP reconciliation"; line 7480: "before E0 commit 2 (the ROADMAP text)"; line 4882: "Q2 (before commit 2)".
- Fix: line 7100 → "Q2 before E0 commit 2; Q3 before E2; Q5 before E3; Q1 and Q4 before E5."

### L13. `AuthorityNeverReadsAClock`'s banned tokens differ
- Line 6299 (§12.8): "no `Stopwatch`, `DateTime.Now`, `DateTime.UtcNow` or `DateTimeOffset.UtcNow`/`Now`".
- Line 6951 (§14.12.6): also bans `Environment.TickCount`.
- Fix: line 6299 → "no `Stopwatch`, `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` or `Environment.TickCount` ...".

### L14. §13.2 schedules `--build-shots` verify and second run from E1; the mode exists only from E5
- Line 6406: "`--build-shots` (+ verify, + a second run) | ... | E1 onwards, with every landed beat".
- Line 6394: `--build-shots-verify` "New in E5"; lines 6311-6312: verify and second run "E5 → E9"; line 5098 (E1): "beats b01 and b02 (§13.4), exit 0".
- Fix: line 6406 → "`--build-shots` from E1 with every landed beat; its verify and second run from E5".

### L15. §17's RAZER process item turns the optional `--perf-world` capture into an automatic one
- Line 7391: "If it happens before E10 closes, add the M7 captures in section 14 (including the optional `--perf-world` run)".
- Line 6964 (§14.13 switch-on): "The owner schedules the RAZER window before E10 closes **and wants the structures capture**; or T2 misses its ASTRAL target".
- Fix: line 7391 → "If it happens before E10 closes, add the M7 captures in §14.14; the optional `--perf-world` run is built and captured only on §14.13's condition (the owner wants the structures capture, or T2 misses its ASTRAL target)."

### L16. §18.4 understates E3's file overlap with the building slices
- Line 7462: "Its only overlaps with the building slices are one braced `if` in `Creatures.cs` ... and new arms in `Simulation.Dispatch`."
- E3 edits `InteractionSystem.Work` in `Systems.cs` (line 5216), which E4 (line 5286), E5 (line 5352) and E6 (line 5450) also edit; E3 edits `Social.cs` (lines 5217-5218), which E9 also edits (lines 5626, 5632); E3 hooks `ContentLoader` (line 5209), which E5 reorders (line 5345).
- Fix: line 7462 → "Its overlaps with the building slices are the braced `if` in `Creatures.cs`, `InteractionSystem` in `Systems.cs` (E4-E6), `Social.cs` (E9), the `ContentLoader` hook order (E5) and new arms in `Simulation.Dispatch`; each is additive."

### L17. Criterion 34 has no evidence line
- Lines 5991-5997: "No crime, bounty, pardon, territory, war-state or decay code exists." and "No optional instrument exists without its section 14 condition recorded as met." carry only a "Proves" line, against line 5744's rule.
- Fix: add "Evidence: the existing `NotBuilt` tests unchanged (R-X1); the `M7_STATUS` scope ledger and its optional-instrument trigger rows (§14.13); the command `grep -rEn \"Bounty|Pardon|WarState|StandingDecay|BuildingCounters|FactionCounters\" src/` returns nothing unless the ledger records the switch-on condition."

### L18. §3.20.4 lists `StateDumpTests` as unmodified; E2 edits a test in it
- Line 1236: "Must stay green, unmodified: ... `StateDumpTests`".
- Line 6329 (§12.10): "`ASaveAndALoad_CompareEqual_FieldByField`: the leaf count (E2)"; line 6337: "`StateDumpTests` apart from the leaf count". The test is `tests/Application.Tests/StateDumpTests.cs:19` (snapshot).
- Fix: line 1236 → "`StateDumpTests` (apart from `ASaveAndALoad_CompareEqual_FieldByField`'s leaf count, §12.10)".
