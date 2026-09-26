# Final consistency audit A: navigation, building, cross-system, persistence vs slices, criteria, tests, runtime, order

Target: `G:/UNNAMED_HISTORY/M7_DESIGN_2026-09-24/M7_IMPLEMENTATION_DESIGN.md` (7,522 lines). Read in full: §3 (498-1304), §4 (1305-2165), §6 (3070-3562), §7 (3563-4198), §10-§13 (4859-6578), §18 (7399-7522); spot reads of §14 (T2, budget sheet) and §15 (R-B6 test). Source facts checked against the `e10d2c4` snapshot. Settled rulings in `00_SCOPE_RULINGS.md` and `01_LEAD_RULINGS_ON_AUDITS.md` are not re-reported.

Verified consistent (no finding): NavigationSystem API (§3.14 = §6.1.2); the RebuildNavigation dispatch rule (§3.4, §3.5.1, §4 decisions, §4.5, §4.13, §6.2.2, criterion 4); door rules and `CanOperate` (§3.10, §4.9, §6.1.4); errand phases and `NpcErrandRecord` fields vs `NpcErrandDto`, CanonicalState and the digest; `PieceRecord` vs `PieceDto`, CanonicalState, digest and StateDump leaves; `NavRoute.AddTo` order = §7.8 route terms; `FromSnapshot` order (§4.19 = §6.1.9 = §7.3 = §7.7); constructor/rebuild order (§3.4, §4.23, §6.1.2, §7.3 step k, §10.7); the Crossing Workshop counts (recomputed row by row: E5 16/24/16 -> 20/15/20, E6 17/25/17 -> 21/14/21, E7 17/25/17 -> 21/11/27, E8/E9 19/31/19 -> 23/0/31, timber and sequence ledgers, refunds 1/1/0, repair 1 timber, blows 20/12/10/30); piece geometry of R07, R08, R36 and the N-A13(c) V-N4 approach; the v14 fixture rows; the 17-line/6-line `expected.json` deltas; `max_expansions` 65,536, 43,690, 6/18 ms, 20/60 ms, 2/6 ms, 2/10/30 ms, memory 1.22 MiB / 8.7 MB / 2.4 MB; BLD005's 85 m; per-slice content counts 102 -> 116; `V13.Player` 18 keys and `V13.Companion` 11 keys (source `SectionCodec.cs:21-90`); Kera (61.6, 139.6) f300, Tavar (145, 42) f53, spawn (30, 158) f90, `door.forge_shed` x 53.0-53.4 (source `ashen_hollow.yaml:12,144,154-155,195-198`).

Findings, most severe first.

---

## CRITICAL

### C1. G12 cannot pass: `BaselineHash` is a public persisted property that no digest hashes
- §6.4 G12, line 3442: "for `PieceRecord`, `NpcErrandRecord`, `NavRoute`, the `FactionLedger` rows and `CompanionRecord`, changing each public property in turn changes `EffectiveCellDigest` or `PlayerRecord.Digest`" (also F-E5 line 4109, §12.5 line 6230).
- §4.4 line 1454 and §4.15 line 1772: both records carry the positional (public) `string? BaselineHash = null`.
- §7.8 lines 3901-3902: the effective-cell v2 terms are "id, def, x, z, rotation, owner, health, `door_open`" and "npc, phase key, `piece ?? "-"`, `owner ?? "-"`, x, z, facing, the route terms above, stuck": no `BaselineHash`. Source precedent: `src/World/WorldDelta.cs:604-605` hashes created rows without their `BaselineHash`.
- Adding the stamp to the digest is not a fix: a live row is committed with a null stamp (§4.5 line 1526) while a loaded row is stamped at decode (§7.5 line 3823), so D1 == D2 in `CrossingWorkshop_10` (line 2043) would fail.
- **Fix (G12, line 3442; F-E5 line 4109):** "changing each public property in turn, except the proof stamps `PieceRecord.BaselineHash` and `NpcErrandRecord.BaselineHash` (stamped by `TakeSnapshot` and at decode, and outside the digest as `CreatedEntityRecord.BaselineHash` is outside effective-cell v1), changes `EffectiveCellDigest` or `PlayerRecord.Digest`. The test names its exemption list explicitly."

## HIGH

### H1. `StructureAudit` has two writers, but its slice has one owner
- §4.4 lines 1464 and 1472: `StructureAudit` is derived state "owned by `StateSlice.Structures`, written only through `RuntimeState` wrappers", rebuilt by "`Populate` only". §6.1.1 lines 3096 and 3101: every slice has exactly one owner, and `Structures` is `BuildingSystem`'s alone.
- `NpcSystem` writes audit lines: §3.12 line 961 ("drops an errand whose NPC is a saved companion (a `StructureAudit` line, G23)"), §4.15 line 1835 ("repairs, each with a `StructureAudit` line"), §6.1.2 line 3141, G23 line 3453, §7.3 line 3686 (owner "`BuildingSystem`, `NpcSystem`"), and §10.15 line 5631.
- `NpcSystem` claims only `Npcs` and `NpcErrands` (line 3131), so a wrapper that starts with `Require(owner, StateSlice.Structures)` throws. Otherwise the one-owner rule is broken. `_npcs.Populate` also runs after `_building.Populate` (lines 3139-3141), so `BuildingSystem` cannot write these lines for it.
- **Fix:** add a transient `ErrandAudit` (`ImmutableArray<StructureConflict>`) under `StateSlice.NpcErrands`, written only by `NpcSystem.Populate` through a wrapper that requires `NpcErrands`. The view `Simulation.StructureAudit` returns `State.StructureAudit` followed by `State.ErrandAudit`. At lines 961, 1835, 3141, 3453 and 5631, replace "`StructureAudit` line" with "`ErrandAudit` line (shown in the `StructureAudit` view)". Split §7.3's row at line 3686 into "`StructureAudit` | `BuildingSystem` (`Structures`) | `_building.Populate()`" and "`ErrandAudit` | `NpcSystem` (`NpcErrands`) | `_npcs.Populate()`".

## MEDIUM

### M1. T2's CI assert is 6 ms in §14 but 12 ms in §11 and §12
- Criterion 33, line 5977: "T2 is green at CI < 12 ms mean". §12.8, line 6301: "CI mean < 12 ms".
- §14.11, line 6840: "mean ≤ 2 ms | mean < 6 ms". T2 step 5, line 6916: "**CI:** mean < 6 ms". §14's tests table, line 7035: "(CI mean < 6 ms)". §12.1 rule, line 6053: timings are asserted at 3× the ASTRAL target, which is 3 × 2 = 6.
- **Fix:** lines 5977 and 6301 read "CI mean < 6 ms".

### M2. The M6-save test's E2 rows use views that land only in E5
- §7.14, line 4092 (E2 row): "`Simulation.Pieces` is empty; `StructureRevision == 0`".
- §10.11, line 5361: the views `Pieces`, `Space`, `StructureRevision`, `StructureAudit` and `StructureFootprints` land in E5. E2 (line 5127) lands only the `WorldDelta` readers `Piece`, `PiecesIn`, `Pieces`, `StructureSequence`, `NpcErrand` and `NpcErrandsIn`. The E2 row does not compile in E2.
- **Fix:** line 4092 reads "E2 | `simulation.World.Pieces` is empty; `simulation.World.StructureSequence == 0`; `World.NpcErrand("npc.ashen_hollow.kera_voss")` is null; Kera stands at her site; the navigation grid digest equals a new game's". Add an E5 row after line 4099: "E5 | `Simulation.Pieces` is empty and `StructureRevision == 0`".

### M3. `BuildingRules.Validate` has no way to reach `NavigationSystem.CheckEdit`
- §4.17, line 1873: "`Validate` calls `NavigationSystem.CheckEdit(addSolids, addDoors, points, scratch, counters)`". §3.13, lines 974-975: `CheckEdit` is an `internal` instance method.
- §4.5, lines 1486-1487: `PlacementContext(SystemContext Context, EntityId Actor, NavScratch Scratch, NavCounterSink? Counters, bool CheckNavigability)` carries no `NavigationSystem`, and `BuildingRules` is `internal static`. §6.1.7 gives `SystemContext` no navigation accessor. The preview path is `Simulation.PreviewPlacement` → the same `Validate` (lines 1548 and 3236), and §3.13 line 965 says the ghost reaches the pure `NavEditCheck.Check` instead. Neither path can be written as specified.
- **Fix:** `internal sealed record PlacementContext(SystemContext Context, NavigationSystem Navigation, EntityId Actor, NavScratch Scratch, NavCounterSink? Counters, bool CheckNavigability);`. `BuildingSystem.Handle` passes `_navigation, …, _navigation.Scratch, _navigation.Counters`, and `PreviewPlacement` passes `_navigation, …, _previewScratch, null`. Check 15 calls `ctx.Navigation.CheckEdit(addSolids, addDoors, points, ctx.Scratch, ctx.Counters)`. Line 965 reads "the command and the ghost both reach it through `NavigationSystem.CheckEdit`: the command with the authoritative scratch and sink, the ghost (`Simulation.PreviewPlacement` → `BuildingRules.Validate`) with `_previewScratch` and a null sink".

### M4. An invalid errand route: §3.9 rejects the row alone, but §7 quarantines the whole section
- §3.9, line 861: "an errand row whose route fails `ProblemOf` is rejected alone". §4.19, line 1916: `TryApplyNpcErrand` checks "`Route.Problem()` null".
- §7 decisions, line 3573: "an invalid route is a decode failure for either host". §7.5, lines 3811, 3817 and 3819: the failure is rethrown as `FormatException`, and "An entities-section decode failure quarantines the section". §7.13, line 4059: a malformed or invalid route quarantines the whole section. §12.6, line 6262: `AnInvalidErrandRow_IsRejectedAlone` has "no bad-route case: a bad route is a decode failure".
- **Fix:** the last sentence of line 861 reads "A route that fails `ProblemOf` is a decode failure on either host: in `player.msgpack` it makes the player section corrupt; in `entities.msgpack` it quarantines the entities section (§7.5, §7.13)." On line 1916, delete "`Route.Problem()` null,".

### M5. Row R45's run legs cross the workshop's west wall in E9
- R43, line 2026 (E9): the player ends inside the workshop at "@(101.5, 102.5) f300". R44 (line 2027) keeps that pose.
- R45, line 2028: "→ (97.5, 97.0) → (100.5, 97.6) → (100.5, 100.3); @(102.0, 102.0) f0". The first run leg goes from (101.5, 102.5) to (97.5, 97.0). It meets x = 99.2 at z ≈ 99.3, inside wall (99000, 100500) r1 (x 98.8-99.2, z 98.8-102.2, placed at R04, line 1987). The player snags in the south-west inner corner, so `CrossingWorkshop_9`, `_10`, `_0and11` and beats b18/b19 cannot reach (102.0, 102.0) in E9. In E5-E8 the legs are right: the player starts at R42's (97.5, 103.5), outside the workshop.
- **Fix, R45 Player pose:** "E5-E8: → (97.5, 97.0) → (100.5, 97.6) → (100.5, 100.3); @(102.0, 102.0) f0. E9 (the player is inside at (101.5, 102.5) after R43-R44): @(102.0, 102.0) f0". b18 (line 6475) already reads "@(102.0, 102.0) f0".

### M6. The RK-06 200-piece test places from one pose, beyond the 6 m reach
- §4.22, line 2062: "from a crafted start at (100.5, 94.5) … place through commands 81 pads, 37 walls, one doorway and 81 roofs". No other pose is given.
- Check 6, line 1504: "squared distance from the actor's body to the nearest point of `Bounds` ≤ `place_reach_mm`² (6000²)". Pads at (88 500, 88 500) or (112 500, 112 500) are more than 13 m away, so the script as written is refused ("building reach is 6.00 m"). §14's `FullAreaLayout`, line 6905, states the missing rule: "The player's poses are the implementer's (reach 6 m, never inside a part)".
- **Fix:** after "(238 timber)" on line 2062, add "The player's poses are the implementer's (reach 6 m, never inside a part, never inside a strip a placement would seal), as for `FullAreaLayout` (§14); every placement must be accepted, and a refusal fails the test naming its rule and reason."

### M7. "The player stays east of Kera's route" cannot be asserted, and the coordinates contradict it
- R20, line 2003: "the player stays east of Kera's route". Beat b11, line 6468, lists it under **Asserts**: "the player's legs stay east of Kera's route". A failed in-beat assertion is STOP S5.
- §3.20.3, line 1221, gives Kera's modelled segment (52 625, 137 375) → (93 125, 103 375). R20's own legs contradict "east of": the first waypoint (51.8, 136.0) is west of (52.625, 137.375), and (62.0, 130.0) lies 0.38 m from the segment. No measurable rule is given, so an implementer must invent one (S9).
- **Fix:** replace both phrases with "the player's body is never within 1,000 mm (centre to centre) of Kera's body between `WorkerAssigned` and `NpcArrivedAtWork`", or delete the expectation from R20 and b11.

### M8. §4.15's copy of the errand mover differs from §3.12 in persisted state
- §3.12, lines 933-938: at the goal, "turn towards goalFacing … if facing == goalFacing: ToWork -> AtWork, Route := None, StuckTicks := 0 … `write; continue`".
- §4.15, lines 1815-1818: "turn towards goal facing; when equal: ToWork → AtWork, Route = None, NpcArrivedAtWork … `continue`". It has no `StuckTicks = 0`, and it continues before the write on line 1826 ("write the body (Npcs) and the errand … in the same tick").
- An implementer who follows §4.15 never persists the turned facing, so Kera never reaches 270 000 and `CrossingWorkshop_5to6` fails. `StuckTicks` is persisted and digested, so the two readings also give different saves.
- **Fix:** lines 1815-1818 read "if body.(x, z) == goal.(x, z): turn towards goal facing (18 000 mdeg/tick); when equal: ToWork → AtWork, Route = None, StuckTicks = 0, NpcArrivedAtWork; ToHome → RemoveNpcErrand, NpcReturnedHome; write; continue". Or replace the block with "the mover is §3.12's, verbatim".

## LOW

### L1. §10.1's "closes criteria" column is wrong for criteria 2, 3, 4, 8, 11, 12 and 25
- Line 4894 states the rule: "A criterion closes in the slice after which all its evidence exists".
- Criterion 2 (line 5768) cites N-A7, which lands in E5 (line 6091), but line 4883 closes it in E1.
- Criterion 3 cites G10 (+E6 closed leaves, line 6228), but line 4887 closes it in E5.
- Criterion 4 (line 5781) cites N-A3, which lands in E9 (line 6088), but line 4890 closes it in E8.
- Criterion 8 (line 5802) cites `CrossingWorkshop_0and11` (E5 → E9, line 6163) and "the per-slice counts … in every slice", but E8 closes it.
- Criterion 11 needs rule 7's door case (E6, line 6123), but E5 closes it.
- Criterion 12 (line 5824) cites N-A13(c) (E8) and `CrossingWorkshop_7`'s exact text (E9, line 6160), but line 4889 closes it in E7.
- Criterion 25 (line 5932) cites `KeraAtWork_TradesAtTheBench_AndHerWaresStayHome` (E9, line 6156), but line 4885 closes it in E3.
- **Fix, the column at lines 4882-4892:** E0 1; E1 -; E2 19; E3 22-24, 26, 27; E4 5; E5 2, 7, 9, 20, 35; E6 3, 10, 11; E7 -; E8 16, 18; E9 4, 6, 8, 12, 13, 14, 15, 17, 25, 28, 30, 36; E10 21, 29, 31, 32, 33, 34, 37.

### L2. The E3 `--playthrough-verify` field estimate is about 470 in §7 but about 500 in §10 and §13
- §7.9, line 3942: "= 464, plus about 3-9 … **about 470**". F-E9, line 4113: "427, then about 470".
- §10.9, line 5257: "about 500 fields (§13.8)". §13.8, line 6554: "E3-E4: about 500 (+37 for the ledger, about 28 for Kera's materialised wares record, the bought billet, …)".
- **Fix:** line 3942 reads "From E3: + 37 for the ledger … = 464, plus about 28 for Kera's materialised wares record, the bought billet and a few dialogue-memory and relationship rows: **about 500**". Line 4113 reads "427, then about 500".

### L3. N-A2 includes release and retirement, but its implementing test covers only steps 5-6
- §3.20.3, line 1209: N-A2's script is "Workshop steps 1-6; then release … Released, she retires exactly at her site pose". §12.2 (line 6087) and §4.22 (line 2156) say N-A2 *is* `CrossingWorkshop_5to6` ("Steps 5-6").
- Release is R43 (step 9, line 2026), and retirement is R47 (step 10, line 2030) or `Kera_WalksToTheBench_ArrivesExactly_AndHomeAgain` (line 2148).
- **Fix, line 1209:** Script "Workshop steps 1-6"; delete "Released, she retires exactly at her site pose." Add "(release and exact retirement are asserted by `CrossingWorkshop_9`/`_10` at R43 and R47, and by `Kera_WalksToTheBench_ArrivesExactly_AndHomeAgain`)".

### L4. N-D19 asks for a counter-sink comparison that the pure check cannot take
- N-D19, line 1192: "`NodesFlooded` is equal with and without a counter sink" (a Domain test).
- §3.13, lines 971-972: `NavEditCheck.Check(NavGrid before, NavConfig config, NavScratch scratch, … points)` has no sink parameter. Only World's `CheckEdit` (lines 974-975) has one.
- **Fix, line 1192:** "`NodesFlooded` is equal for a fresh and a reused scratch". Move the sink comparison to `PreviewsInterleaved_ChangeNothing`, or add `NavCounterSink? counters` to `Check`'s signature on line 971.

### L5. §3.13's preview cadence omits the 0.5 s refresh
- §3.13, line 1003: "presentation asks only when the snapped pose or `StructureRevision` changes".
- §4.6 (line 1552), §8.6 (lines 4301 and 4311) and §14 (line 6773) add "or every 0.5 s (bodies move)". Body protected points make the verdict depend on moving bodies.
- **Fix, line 1003:** "presentation asks only when the snapped pose or `StructureRevision` changes, and otherwise at most every 0.5 s (bodies move)".

### L6. F-E4 lands in E2 commit 1 but depends on the v14 fixture from commit 3
- §7.6, line 3838: F-E4 runs "after asserting that `Posture` and `Factions` are non-default in the v14 fixture player".
- §7.17 (line 4143) and §10.8 (line 5187) put F-E4 in commit 1. The M2.Probe v14 player is part of commit 3 (lines 5144 and 5189).
- **Fix:** move F-E4 to E2 commit 3 at lines 4143 and 5187. Or reword line 3838 as "on a test-built `PlayerRecord` with a non-empty ledger and a non-default posture (commit 1), and again on the v14 fixture player (commit 3)".

### L7. §6.1.3 says "dead" is the only actor refusal, but four part rules start with "unknown actor"
- §6.1.3, line 3168: "Their only actor refusal is `"dead"`".
- Check 1 (line 1499): "unknown actor {id}" / "dead". Repair (line 1719), dismantle (line 1725) and assign (line 1791) have the same pair.
- **Fix, line 3168:** "Their actor refusals are "unknown actor {id}" and `"dead"` (`PlayerCombat.Defeated`) only; there is no combat-phase `"busy"` clause."

### L8. §7.11 lists fewer slices than §10.4 for BLD lints that can reject the fixture pack
- §7.11, line 4011: "BLD001-BLD009 in E5 and E7".
- §10.4 (line 4975): "E5, E7, E8 and E9". §12.3 (line 6120): BLD005 lands in E8 and E9.
- **Fix, line 4011:** "BLD001-BLD009 in E5, E7, E8 and E9".

### L9. G27's slice list conflicts with E6
- §10.12, line 5477: "The view test and G27 gain `DoorToggled` with a `pce_` key".
- §12.5, line 6242: G27 is "E3 (+E4, E5, E8, E9 types)", with no E6. G27 (line 3457) covers M7 event types, and `DoorToggled` is reused (line 3213).
- **Fix, line 5477:** "The view test gains `DoorToggled` with a `pce_` key".

### L10. Criterion 16 names formulas, but the test's description names bolts
- Criterion 16, line 5856: "Arrows, formulas and a boar's charge damage no piece". §4.12, line 1702: "Arrows and formulas stop at piece walls and damage nothing".
- The test row (line 2141): `DamageRules_OnlyAMeleeBlow_DamagesAPiece`, "arrows, bolts and charges damage nothing".
- **Fix, line 2141:** "The one damage source (10); arrows, bolts, formulas and a boar's charge damage nothing".

### L11. A test that §15 lands in E5 is missing from the matrix and from E5's list
- §15, line 7180: `ALayoutEditUnderASavedPiece_IsAuditedAndKept` "Lands in E5".
- §12 (line 6032) claims completeness, and §10.11 (line 5399) runs "Every §12 row marked E5". This test is in neither.
- **Fix:** add a §12.3 row: "`ALayoutEditUnderASavedPiece_IsAuditedAndKept` | App | E5 | R-B6: a layout edit and a `health_max` retune under saved pieces are audited, clamped and kept | persistence".

### L12. §6.1.8 describes `NavRoute`'s constructor in a way that cannot be written
- §6.1.8, line 3259: `NavRoute` has "a private positional constructor".
- §3.9, lines 825-848: an ordinary private constructor with get-only properties, and "Properties are get-only, so `with` cannot bypass a factory". A C# primary (positional) constructor cannot be private, and positional properties are `init`, so `with` could bypass the factories (N-D21).
- **Fix, line 3259:** "with a private (non-primary) constructor and get-only properties (§3.9)".
