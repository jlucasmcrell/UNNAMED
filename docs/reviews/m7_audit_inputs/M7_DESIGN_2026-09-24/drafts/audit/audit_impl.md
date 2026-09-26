# M7 design audit: implementability and completeness

Status: adversarial audit by the implementability critic, 2026-09-24. Design and planning only. It reads the full draft set as the implementation agent would: `00_SCOPE_RULINGS.md`, `A_navigation.md`, `B_building.md`, `C_factions.md`, `D_cross_system.md`, `F_persistence.md`, `GH_content_ui.md`, `IJ_risk_perf.md` and `E_slices_tests.md`. It raises no challenge to an owner ruling or a scope ruling, and it re-litigates no architecture. Every code fact below was re-read in the snapshot of origin/main `e10d2c4` (`main_e10d2c4`). Draft citations are `file:line`.

**Method.** I read every draft in full. I built each slice's work list and asked three questions. Could I write this without inventing a rule? Does its STOP condition decide itself? Does the named test fail when the claim is false?

**Limit.** The owner brief is not in the design folder. The A1-A10 and C1-C9 items were recovered from the drafts' "brief item" headings (A §1-§10; C §3-§17). The B1-B11 and D-K items were recovered from B §0.2's ROADMAP table and the 18-section list. Section 5 says where that recovery is uncertain.

---

## 0. Verdict

The draft set can be implemented, and it is unusually concrete. Types, file homes, ordered validations, digest term orders, fixture values and per-slice commits are all given, and most of the code facts I re-checked hold. Keys B, T, Y, Z, Delete, PageUp/PageDown, F2 and F6 really are unbound (`src/Presentation/Main.cs:1061-1104`). The heart is a switch that sets `world.foldscar.steadied` (`content/regions/ashen_hollow.yaml:185-192`), so C's act hook works. `Crafting.cs:166-168` uses `Any` over stations, so station order cannot matter.

**It is not yet safe to hand to an implementer slice by slice, for three reasons.**

1. **One contradiction refuses every chest placement.** D §1.2's rule V-N3 requires the chest's site to be standable, but B §20 puts that site inside the chest's own solid part.
2. **The parts were never amended.** D §6.3 lists only decision numbers for each part. B's §4, §17, §2.4 and §21.1, among others, still read as normative text that D deleted or replaced.
3. **Some objective gates do not decide.** Acceptance criterion 4 contradicts E5's own rule for pads and roofs. Guard tests G9 and G10 cannot prove what they claim. The Crossing Workshop script lacks the player poses that reach (6 m), the Bodies check and the navigability Body points depend on.

In addition:
- No draft supplies final-document section 2 (inventory of existing systems).
- Section 16 (deferred work) exists only as scattered lists.
- The owner's roles ruling covers work "through M6" only, so M7's branch and worktree are an open process decision.

Findings follow, in order of severity.

---

## 1. Findings

### HIGH

**H1. V-N3 refuses every chest (D §1.2 contradicts B §20).**
- **Reading 1 (D).** V-N3 says "new functional points (`NewSite`): standable at the person radius (`PointClear`) and not in a sealed pocket" (`D_cross_system.md:93`). The NewSite list includes "the candidate chest's site" (`:100`). Check 15 runs V-N1..V-N3 for a chest (`:108`).
- **Reading 2 (B).** The chest's part is local `[-500, 600, 500, 1200]` and its site is `(0, 900)`: the site lies inside the part (`B_building.md:1027`; `container: { stack_slots: 12, at_m: [0, 0.9] }` at `:121`). BLD005 even requires `at_m` to be inside the bounds (`:1009`).
- **Consequence.** `PointClear(site, 350)` against the candidate's own solid is false, so every chest placement fails with "nothing could reach its work place". E8's `EachPiece_Places_…` chest row, workshop step 1 (19 pieces), criterion 8 and b07 all fail. D's table hints at the intended split, giving the chest site `RadiusMm 0` and `ReachMm 1,600` and the station anchor 350/250, but V-N3's text never uses it. A's original V-N3 spoke of the chest's "use point" (`A_navigation.md:227`), and B defines no such point.
- **Fix.** Restate V-N3 as follows:
  - a NewSite with `RadiusMm > 0` (the station work anchor) must be standable (`PointClear` at that radius) and not in a sealed pocket;
  - a NewSite with `RadiusMm = 0` (the chest site) must have a walkable node whose centre lies within `ReachMm` of the point, in an open component.

  Add an N-D19 case `EditCheck_AChestSiteInsideItsOwnBox_IsAReachPoint`.

**H2. The part documents were not amended, and superseded text still reads as normative.** D says "where this document and a part disagree, this document wins", and its per-part list is "mechanical": "B: D2, D3, D5, D6, D10…" (`D_cross_system.md:5`, `:611`). But an implementer who opens B for slice E5 reads, as design:
- `INavigability`, `NavCheckDepth`, the eager labels and `Window`/`Full` (`B_building.md:935-955`);
- the preview's "amber on `Unknown`" contract (`:277-289`);
- `OpenDoor(string DoorKey, EntityId Actor)` (`:390`);
- the `Space` ordered by piece ID (`:188`, `:193`);
- a nullable errand route (`:532`);
- door-side points at "+400, Reach 400" (`:933`);
- `navigability_radius_m` (`:478`);
- "interpolated as creatures are" (`:1083`);
- `CameraRig.MaxDistance` as an instance field (`:1080`).

Every one of these is reversed by D, GH or E. Nothing marks them as dead. The same holds for A: its errand record in the player section (`A_navigation.md:868`), the `PlaceNpc` write (`:476`), `work_anchor_max_m` (`:785`) and the hut at x 97-103 (`:565`).
- **Fix.** Before E0 closes, produce amended A and B, or write final-document sections 3 and 4 as merged text, not as part plus errata. Each D row's effect is applied in place, and each deleted paragraph is struck through with its D number. A merge checklist should list, for each D row, the exact part paragraph it replaces. This is the largest source of wrong implementation in the set.

**H3. M7's branch, worktree and "scratch worktree" step are not authorised by any ruling.**
- The roles ruling says Claude "is the primary gameplay/code agent through M6. It works only in the `G:\UNNAMED_CLAUDE` worktree on branch `claude/phase1`" (`AGENTS.md:13`).
- E.4 simply defers to that ruling (`E_slices_tests.md:654`), but it does not cover M7, a Phase-2 milestone.
- E2 and F §8.3 require writing the real M6 save "by the `e10d2c4` build in a scratch worktree" (`E_slices_tests.md:202`; `F_persistence.md:497`, `:671`). That is a new git worktree, a state change outside the one permitted worktree.
- **Fix:**
  - Add an owner item (a process decision, not a design question): M7's branch name, its worktree, and whether the draft-PR convention continues.
  - Drop the scratch worktree. Write `tests/Application.Tests/Saves/m6_hollow/` from the branch itself at the end of E0 or E1. Neither slice changes a persisted shape or a generator input (E1 persists nothing, `E_slices_tests.md:14`), so that save carries schema 13 and the M6 worldgen fingerprint that `M6LayoutFingerprint` needs.

### MEDIUM

**M1. Acceptance criterion 4 contradicts E5 and beat b04.**
- Criterion 4 says "Every committed place, dismantle and destroy sends exactly one `RebuildNavigation`" (`E_slices_tests.md:681`).
- E5 says "For pads and roofs building sends no rebuild (J.3)" (`:361`), and b04 asserts "no `NavigationRebuilt` for pads" (`:1091`).
- B §3.4 dispatches for every placement with `check.Bounds` (`B_building.md:260`), and D10 says "the union of changed parts" (`D_cross_system.md:24`), which is empty for a pad. IJ J.3 flagged this and left it open (`IJ_risk_perf.md:236`).
- **Fix.** Criterion 4 becomes: "every committed place, dismantle or destroy of a piece with at least one part (solid or door) sends exactly one `RebuildNavigation`; pads and roofs send none; every one of these increments `StructureSequence`". Amend B §3.4 and §10.2 step 4 to skip the dispatch when the union of parts is empty.

**M2. A refused `OpenDoor` loops forever, and the promised "replans on stuck" does not happen.**
- D34 says that at a foreign-owned door "`OperatePieceDoor` refuses, and the mover holds and replans on stuck" (`D_cross_system.md:48`).
- D's mover pseudocode, on `OpenGate`, dispatches `OpenDoor`, faces the door and keeps the route. It never touches `StuckTicks` (`:151`). D28 increments `StuckTicks` only on `Unreachable` (`:42`).
- The follower therefore returns `OpenGate` every tick, `OpenDoor` is refused every tick, the "stuck" trigger (`A_navigation.md:367`) never fires, and the view never reads "Blocked".
- For the companion, A §8.3 is ambiguous about whether an `OpenGate` tick counts as no headway (`A_navigation.md:442-445`).
- **Fix.** On an `OpenGate` step whose `OpenDoor` returns a refusal, set `StuckTicks += 1`, for both movers. An accepted `OpenDoor` leaves `StuckTicks` unchanged. Add an N-A5 variant with a foreign-owned door from a crafted save: it asserts "Blocked" after `blocked_view_s` and no more than one `OpenDoor` per tick.

**M3. B and D disagree on whether a sealed room is allowed.**
- B §17.5 says "Accepted: … a sealed room with no reference point (a harmless solid block)" (`B_building.md:959`), and B §17.1 checks only points that were satisfied before.
- D's V-N1 refuses any newly sealed pocket. With no protected point inside, the reason is "that would close off a space with no way in" (`D_cross_system.md:91`).
- A test writer who follows B expects a doorless hut to be accepted. N-D19 and N-A13 expect it refused (`A_navigation.md:552`, `:576`).
- **Fix.** Record the decision explicitly, D's reading: a placement that seals any walkable pocket is refused. Strike B §17.5's example and add `ADoorlessOneSquareHut_IsRefused` to `EachPlacementRule_Refuses_…` rule 15. The F1 BUILDING help text and the ghost wording should say that rooms need a doorway.

**M4. Guard G10 cannot pass as specified.**
- G10 places one piece set in two orders and asserts "`Space.Blockers` and `ClosedDoors()` are element-wise equal" (`D_cross_system.md:531`, `:67`).
- `Blocker` is `record Blocker(string Id, long HeightMm)`, so record equality includes `Id` (`src/Domain/Spatial/Blockers.cs:11`, `:41`). Piece-part IDs are `"{pieceId}#{partIndex}"` (`B_building.md:188`), and piece IDs come from `StructureSequence`. Two placement orders give different IDs for the same geometry, so the assertion fails even when `StructureOrder` is correct.
- **Fix.** Compare a projection `(Type, MinX, MinZ, MaxX, MaxZ, HeightMm, ClearanceMm)` element-wise, and separately assert that each part's `Id` resolves to a piece of the same definition and pose. Alternatively, derive part IDs from the slot key (for example `ex:33:33#0`); `FirstStop` can map a slot key back to its piece through the socket index.

**M5. Guard G9 cannot fail in any legal world.**
- G9 boots "the same save with and without its pieces" and asserts equal creature homes (`D_cross_system.md:530`).
- Homes come from spawn samples inside the spawner disc, tested with `IsClear` (`src/World/Runtime/Creatures.cs:173-191`).
- Check 11 and BLD007 already keep every piece outside the disc plus the largest member radius plus 1 m (`B_building.md:893-899`). No legal piece can overlap a sample, so switching `Creatures.cs:173` to `_context.Space` would leave G9 green.
- The hazard is real only for pieces the load audit keeps "over protected ground" after a content change (`B_building.md:608`).
- **Fix.** Build G9's save with a piece row injected over a spawn sample, through M2.Probe-style record construction or World.Tests internals, so that it bypasses check 11. Assert that homes are unchanged. Add a source-scan assertion that the `Populate` method body in `Creatures.cs` reads `Setup.Layout.Space`.

**M6. The Crossing Workshop script lacks the player poses its outcomes depend on.**
- B §11 step 1 lists 19 anchors, but not where the player stands for each placement (`B_building.md:669-675`).
- Reach is 6 m to the piece bounds (check 6). From b03's pose (100.5, 94.5) (`E_slices_tests.md:1090`) the north walls at z = 105 m are about 10 m away, so the script must move.
- Check 12 (Bodies) and V-N1's Body points make the result depend on the pose.
- The start pose is itself contradictory. E puts the new character at the spawn (30, 158) (`E_slices_tests.md:1047`), while B's step 1 begins with no walk (`B_building.md:657-669`).
- Step 3 needs the player outside the closed door at (100.5, 97.0) with the door closed, so the script must leave through the door after building from inside.
- **Fix.** Publish the scenario as a command table (tick, pose, command) that `CrossingWorkshop.Start()`, the step-group tests and `--build-shots` all consume. Suggested start pose: (100.5, 94.5), facing 0. Build steps 1-2 from (102.0, 102.0) inside the footprint; every step-1 bound is then within 3 m. Then open the door, walk out to (100.5, 97.0), close it, and run step 3. Give per-slice expectations for E5 and E6 as well: 16 pieces, 24 timber and sequence 16 before the door, bench and chest exist.

**M7. N-A3 and N-A4 have no coordinates on real content since D24.**
- A's N-A3 ("mid-walk, place a wall across the remaining corners") and N-A4 were written for A's hut at z 125-131 m (`A_navigation.md:565-567`), which lies outside the build area.
- D24 retired that hut (`D_cross_system.md:38`). E keeps N-A3 and N-A4 as E9 Application tests (`E_slices_tests.md:836-837`) with no pose, wall anchor or timing.
- A mid-walk wall must lie inside [87, 114]², within 6 m of the player, clear of Kera's body (check 12), and must not seal a pocket.
- **Fix.** Specify both tests by rule, not by guessed coordinates, since her real route on the workshop was never measured (L8):
  - **N-A3:** on Kera's first walk, at the first tick her body is inside the area, take the first remaining route segment that lies inside [87, 114]². Pre-place a pad on a lattice square that segment crosses. Then place a wall on an edge of that square that the segment crosses (`Snapper` gives the anchor), from a scripted pose within 6 m. Assert, before placement, that the wall's bounds intersect the segment. Then assert `NavigationRebuilt` at that boundary, `RoutePlanned(geometry)` on her next tick, no overlap and arrival.
  - **N-A4:** dismantle that wall on her return walk.

  Or map N-A3 explicitly onto `CrossingWorkshop_9` and delete the mid-walk claim.

**M8. The headless kill of the Animated Armour behind P1-P5 is unspecified.**
- Criterion 22 asserts the sign of P5 on shipped content (`E_slices_tests.md:749`).
- C says the P scenarios run "by the Quest 1 and Quest 2 walks already used in tests" (`C_factions.md:985`). The only Quest 1 walk test runs "without a fight" (`tests/Application.Tests/QuestTests.cs:108`), so no existing test kills the armour.
- The F scenarios weaken a wolf to 1 health (`C_factions.md:981`). Doing the same to the armour stops P1 being shipped content.
- **Fix.** Name the script. Use `CreatureTests`' arena placement (`tests/Application.Tests/CreatureTests.cs:304-311` places the armour as a sentinel) with the shipped definition, and kill it with the character's March Spear, blows from behind, a fixed seed and a tick cap. Or state that P1 may use an arena-placed armour with shipped stats, and keep the "shipped content" claim only for P2-P5's dialogue and gates.

**M9. E10 is sized M but carries a milestone of instrumentation.** E10 bundles all of the following (`E_slices_tests.md:609-645`; `IJ_risk_perf.md:402-479`):
- T1-T11, including a generated `docs/M7_COST_TABLE.md`;
- 23 new `FrameStats` columns;
- `BuildingCounters` and `FactionCounters`;
- a perf-world generator;
- four new PerfRun segments;
- the 30-minute soak T9.

T9 is M6's owed RK-05 soak (`IJ_risk_perf.md:37`), not M7 scope. The load is also a scope-creep vector (R-X1).
- **Fix.** Split E10 into a required set and an owner-optional set:
  - required: N-A8, N-A10, T2, T3, T5, T7, T10 and the RK-06 200-piece test;
  - optional, or into the RAZER window: T1, T8, T9, T11/`--perf-world` and the new `FrameStats` columns beyond `sim_ms`, `ticks` and `save_ms`.

  Record the split in the scope ledger.

**M10. Final-document section 2 (inventory of existing systems) has no draft.** The pieces exist:
- C §1's facts table (`C_factions.md:57-85`);
- A's Appendix B.1 (`A_navigation.md:1044-1052`);
- D §5 on reuse (`D_cross_system.md:546-584`);
- the research notes (`research/sim_core.md`, `spatial_movement.md`, `persistence.md`, `content_registry.md`, `social_quests_code.md`, `presentation_perf.md`).

No draft consolidates them into "what exists, where, and what M7 touches".
- **Fix.** Write section 2 as one table per subsystem: `Simulation` and slices, `Kinematics` and blockers, companions and NPCs, combat traces, containers and items, dialogue and trade, persistence (schema 13, four sections), content kinds, and presentation. Give the verified `path:line`, the M7 touch (none, minimal, extended) and the draft section that changes it. D §5.1 and §5.2 already hold most of the touch column.

### LOW

**L1. `NavFootprint.DoorOpen` goes stale on door toggles (B vs F).**
- B puts `bool DoorOpen` on `NavFootprint` (`B_building.md:1057`) but rebuilds `StructureFootprints` only "with Space", on place, dismantle and destroy (`:191`).
- F lists the footprints as rebuilt "on place, dismantle, destroy and door toggle" (`F_persistence.md:126`).
- Navigation promises that "gates are read at query time" (D G14). F2's gate colours and any future non-opener would read a stale flag.
- **Fix.** Remove `DoorOpen` from `NavFootprint`. Navigation's `IsGateOpen` reads `State.World.Piece(id).DoorOpen` at query time. Footprints then rebuild only on footprint changes, as B says.

**L2. Two seeding mechanisms for the faction ledger.**
- C: "`RuntimeState` reads `player.Factions` in its constructor" (`C_factions.md:684`), the existing pattern (`src/World/Runtime/RuntimeState.cs:102`, `Posture = player.Posture`).
- E.1: a seed-only `FactionSystem` "seeds it from `player.Factions` (the pattern of `_effects.Seed`)" (`E_slices_tests.md:60`).
- **Fix.** Use C's mechanism. In E2, `FactionSystem` only claims `StateSlice.Factions` (which `RequireEverySliceOwned` needs, `src/World/Runtime/Simulation.cs:139`). It has no `Seed`.

**L3. The shape of `ReputationChanged` differs across drafts.**
- D §2.5 lists it without `Via` (`D_cross_system.md:341`).
- E3 and GH add `string? Via` (`E_slices_tests.md:267`; `GH_content_ui.md:674`).
- **Fix.** Include `Via` in the unified contract (D §2.5).

**L4. Two rules for when an errand arrives.**
- B: `ToWork` becomes `AtWork` when (x, z) equals the anchor (`B_building.md:856`).
- D: on facing equal to the goal facing as well (`D_cross_system.md:148`).
- **Fix.** Follow D: arrival needs x, z and facing, and `NpcArrivedAtWork` is published on the tick the facing matches. State it in B §15.3.

**L5. The billet ware reference is wrong in GH and unstated everywhere.**
- A ware's ref is `{site.Key}#{i:00}` over stacks split by `stack_max` (`src/World/Runtime/Items.cs:485-490`).
- Kera's 60 arrows are three stacks at a `stack_max` of 20 (`content/items/ammo/arrow_rough.yaml:9`; `content/merchants/ashen_hollow/kera_voss.yaml:9-11`), so the existing refs are `#00..#04`, not "`#00..#02`" (`GH_content_ui.md:190`).
- A raw `BuyCommand` for a hidden ware (beat `m7_billet_refused`; `TheBilletGate_…`) needs the ref: `merchant.ashen_hollow.kera_voss#05` while the wares are untouched, and an `itm_` ID once the container has a record.
- **Fix.** Give the ref rule in C §8.2, and have the tests read it from the stock index rather than hard-coding it.

**L6. E3's "fails in 2 of 3 runs" is not a decision rule.**
- The STOP (`E_slices_tests.md:290`, `:1080`) presumes run-to-run variance.
- The playthrough is deterministic ("`state_replay.json` is byte-identical across two runs", `:1077`), so a failure repeats every time.
- **Fix.** STOP on the first failure of `m7_armour`, with the transcript attached.

**L7. E0's acceptance is "review", which is not objective.**
- Criterion 1's evidence is "E0 review" (`E_slices_tests.md:672-674`).
- The STOP is "a ruling contradicts rank-2 text in a way scope rulings §2 did not foresee" (`:115`).
- **Fix.** Add a checklist with grep-able outcomes:
  - "Recast" and "Baked" are absent from WA §11's navigation row;
  - "needs the engine" is absent from RK-14;
  - DECISIONS has two dated ruling entries;
  - ROADMAP M7 contains "domain navigation" and "ground pads".

  The owner signs the review in `M7_STATUS`.

**L8. The arrival bound and the crossing window are ambiguous, and the workshop route was never measured.**
- The bound is "⌈1.25 × planned path length / 1.6 m/s × 20⌉ + 40" (`B_building.md:682`; `E_slices_tests.md:1100`), which does not say which plan.
- Criterion 14's "crosses z = 100 exactly once" gives no time window (`E_slices_tests.md:712`); the walk home crosses z = 100 again.
- D24 promised that A's model numbers would be re-measured on the workshop (`D_cross_system.md:38`). None were, so "the shorter west side" is unverified.
- **Fix:**
  - The path length is that of the first `RoutePlanned` route after `WorkerAssigned`, from the body to the anchor, polyline in mm.
  - The crossing window runs from `WorkerAssigned` to `NpcArrivedAtWork`.
  - T1 records Kera's expansions and corners on the workshop, and N-A3's rule (M7) uses that logged route.

**L9. BLD009 is under-specified.**
- IJ says "one cheap lint, BLD009, in `config.building`" (`IJ_risk_perf.md:255`), which could mean a constant or a config key.
- D's lint list and GH's content checklist omit it. E5 builds it (`E_slices_tests.md:351`).
- **Fix.** Fix BLD009 as a lint with constants `PiecesPerCellCeiling = 256` and `PiecesPerRegionCeiling = 256` in `BuildingConstants`, checked over the sum of `max_pieces` of the areas that meet each cell and region. Add it to D §2.9.

**L10. `--perf-world` and PerfRun's Valley route.**
- The perf world fills [87, 114]² with 9 rooms (`IJ_risk_perf.md:458-459`).
- `Valley` walks (150, 100) → (100, 100) → (66, 112) in straight lines (`src/Presentation/Perf/PerfRun.cs:24-27`), which would run into walls if `--perf --perf-world` kept the standard segments.
- **Fix.** State that `--perf-world` replaces the segment list with J.11.4's five segments only.

**L11. The scripted-run plumbing for `--build-shots` is missing.**
- The start save lives in `tests/Application.Tests/Saves/m7_crossing_start/` (`E_slices_tests.md:1040`). No draft says how the Godot run copies it into a profile.
- `build_mode` is gated "not a scripted run" (`GH_content_ui.md:253`), while b03 says "B; key 1" (`E_slices_tests.md:1090`).
- **Fix.** `BuildShots` copies the save into a temporary profile, as the delta-shots precedent does. `BuildMode` exposes `Enter()`, `Select(int)` and `ScriptedAim` to scripted runs, and the beats call them instead of keys.

**L12. Criterion 31 contradicts criterion 29.**
- Criterion 31: "the existing architecture tests are unchanged" (`E_slices_tests.md:776`).
- Criterion 29: the allow-list gains `PreviewPlacement` (`:771`), and G6 is extended.
- **Fix.** Criterion 31 becomes: "no architecture test is weakened; the only allow-list addition is `PreviewPlacement`".

**L13. The fixture pack must pass lints that do not yet exist when it is committed.**
- F requires `Fixtures/content` 0.2.9 to pass "including the new validators" (`F_persistence.md:476-480`). It is committed in E2, while FAC001 lands in E3 and BLD001-BLD009 in E5 and E7.
- **Fix.** State that a lint failure on the pack in E3, E5 or E7 is fixed in the pack under a new version (0.2.10), with its hash and mirror updated in the same commit. This is neither S1 nor S2 (`E_slices_tests.md:79-80`).

**L14. The re-hosting repair for errands is unspecified.**
- F asks `NpcSystem.Populate` to "re-host an errand whose NPC's site moved to another cell" (`F_persistence.md:773`), and E9 adopts it (`E_slices_tests.md:566`).
- Re-hosting changes `HostCell`, which the baseline proof and `EffectiveCellDigest` key on, and nothing says who restamps `BaselineHash`.
- **Fix.** On re-host, restamp `BaselineHash` from the new host's baseline at the next `TakeSnapshot` (the created-row rule), and report it in `StructureAudit`.

**L15. The deferred and non-goal material is scattered.** It is spread over:
- scope rulings §4;
- A §9;
- B §0.2 and §25;
- C §19 and Appendix C;
- E's per-slice non-goals;
- GH H.13;
- IJ J.13.

The optional-if-cheap items were resolved in different places: creature return-home was declined (A §1), territory gating deferred (C §19), half and window walls deferred (E5 non-goals).
- **Fix.** Final section 16 lists every deferred item once, with its owner milestone and the draft that declined it.

**L16. The DRAFT dialogue text has no gate.** C marks all new lines DRAFT for the owner's tone review (`C_factions.md:874`, open issue 8), and E3 commits them. Nothing says whether M7 closes with the draft text.
- **Fix.** List it as an owner review item, not a question: it ships as written unless the owner objects before E10.

---

## 2. Decisions the drafts leave open or contradict, with both readings

| # | Topic | Reading 1 | Reading 2 | Recommended |
|---|---|---|---|---|
| 1 | Chest site under V-N3 | standable (D:93) | inside the chest box (B:1027) | H1 |
| 2 | Sealed doorless room | accepted (B:959) | refused (D:91) | M3: refused |
| 3 | Rebuild for pads and roofs | one per place (E:681; B:260) | none (E:361, :1091) | M1: none |
| 4 | Refused `OpenGate` | holds and replans on stuck (D:48) | no stuck increment (D:151) | M2 |
| 5 | Footprint rebuild on toggle | no (B:191) | yes (F:126) | L1 |
| 6 | Ledger seeding | `RuntimeState` ctor (C:684) | `FactionSystem.Seed` (E:60) | L2 |
| 7 | `ReputationChanged.Via` | absent (D:341) | present (E:267) | L3 |
| 8 | Errand arrival | (x, z) only (B:856) | plus facing (D:148) | L4 |
| 9 | Start pose | spawn (30, 158) (E:1047) | near the area (B:657-669 implicit) | M6 |
| 10 | Branch and worktree for M7 | `claude/phase1` in `G:\UNNAMED_CLAUDE` (AGENTS.md:13, "through M6") | unstated | H3 |

---

## 3. Slices whose STOP or tests do not decide

| Slice | Problem | Fix |
|---|---|---|
| E0 | Acceptance by review; subjective STOP | L7 checklist |
| E3 | "2 of 3 runs" on a deterministic run | L6 |
| E4 | "any row that differs must be traced … and recorded" (`E_slices_tests.md:329`) is not a gate | Byte-compare transcript rows up to `follow` against E3's; any diff is a STOP unless it is a `RoutePlanned` row |
| E5 | G10 fails for the wrong reason; G9 cannot fail | M4, M5 |
| E5/E6 | Per-slice workshop numbers (16 pieces, 24 timber, sequence 16) are implicit | M6 |
| E9 | Arrival bound and crossing window ambiguous; N-A3/N-A4 unscripted | L8, M7 |
| E3 (P rows) | The armour kill script is missing | M8 |
| E10 | Size and scope | M9 |
| Criterion 4 | False as written | M1 |

---

## 4. Coverage of the 18 required final-document sections

| # | Section | Source | Adequacy |
|---|---|---|---|
| 1 | Authoritative scope | 00 §1-§4; E0; criterion 34 | adequate; restate as owner-facing, since 00 is the design lead's text |
| 2 | Inventory of existing systems | none (research notes; C §1; A App. B; D §5) | **thin** (M10) |
| 3 | Navigation | A with D1-D38 | adequate after merge (H2) |
| 4 | Building | B with D, GH H.14, F §15 | adequate after merge; currently misleading (H2, H1) |
| 5 | Factions | C with D11-D14, D29-D30 | good |
| 6 | Cross-system contracts | D §2-§4 | good; add `Via` (L3) and BLD009 (L9) |
| 7 | Persistence and migration | F; D §1.5 | good |
| 8 | UI and controls | GH H | good; scripted-run API missing (L11) |
| 9 | Minimal content | GH G.6; B §8.1, §16, §20; C §16 | good; ware-ref error (L5) |
| 10 | Slices | E.3 | good; E10 sizing (M9) |
| 11 | Acceptance criteria | E §11 | mostly good; criteria 1, 4, 14 and 31 need repair |
| 12 | Test matrix | E §12 | good; G9 and G10 unsound; N-A3/N-A4 unscripted; test file homes missing for the B and C Application tests other than `BuildingAcceptanceTests.cs` and `ReputationTableTests.cs` |
| 13 | Runtime proof plan | E §13 | good; pose table (M6) and start-save plumbing (L11) |
| 14 | Performance | IJ J | good; the required/optional split is missing (M9) |
| 15 | Risk register | IJ I | good |
| 16 | Deferred work and non-goals | scattered | **thin** (L15) |
| 17 | Owner decisions | D §6.1; E deadlines | adequate; add the roles and branch item (H3) and the review items (L16, RAZER window, armour-beat fallback) as owner actions outside the five questions |
| 18 | Recommended order | E §18 | good |

---

## 5. The owner brief's asks, item by item

The brief itself is not in the folder, so this mapping is recovered (see Limit). "Gap" means no draft answers it adequately.

| Item | Ask (recovered) | Answered by | Status |
|---|---|---|---|
| A1 | Who needs pathfinding | A §1 | answered |
| A2 | Representation | A §2 | answered |
| A3 | Resolution | A §3 | answered |
| A4 | Authority (read, derived, cached, saved) | A §4, D §2.1 | answered |
| A5 | Updates on building edits | A §5, D §1.2, D10 | answered; pad and roof dispatch open (M1) |
| A6 | Cell seams | A §6 | answered; A's numbers not re-measured on the workshop (L8) |
| A7 | Pathfinding specification | A §7 | answered |
| A8 | Local movement | A §8, D §1.3 | answered; `OpenGate` refusal gap (M2) |
| A9 | Future seams | A §9 | answered |
| A10 | Acceptance tests | A §10, E §12.1 | answered; N-A3/N-A4 lost their geometry (M7); "different body size" is synthetic only (N-D6), acceptable because no M7 mover uses the larger classes |
| B1 | Socket/snap, lattice | B §0.4, §1, §5 | answered |
| B2 | Foundations and floors under one storey; terrain | B §6 | answered |
| B3 | Rotation | B §0.4; D §6.1 Q1 | answered (owner question) |
| B4 | Doors | B §7, D6/D7 | answered |
| B5 | Ownership | B §12 | answered |
| B6 | Health, explicit damage, repair, destruction | B §10, §13 | answered |
| B7 | Storage and one station | B §14 | answered; chest navigability (H1) |
| B8 | Materials and transactions | B §8 | answered |
| B9 | Persistence as sparse delta | B §9, F | answered |
| B10 | Placement validation (overlap, navigability) | B §3.3, §17; D §1.2 | answered, but contradictory until merged (H1, M3) |
| B11 | NPC work assignment and straddling proof | B §11, §15; D §1.3 | answered; script poses missing (M6) |
| C1 | Minimum entities | C §3 | answered |
| C2 | Act pipeline and fields | C §4 | answered |
| C3 | Knowledge | C §5 | answered |
| C4/C5 | Factions and proof case | C §7 | answered (owner Q5); headless kill missing (M8) |
| C6 | Personal relationship vs standing | C §11 | answered |
| C7 | Hostility | C §12 | answered |
| C8 | Persistence | C §14, F | answered |
| C9 | Tests and fixture table | C §17 | answered |
| D | Cross-system reconciliation | D | answered, but not propagated into the parts (H2) |
| E | Slices, criteria, matrix, proof, order | E | answered; STOP and test defects (§3) |
| F | Persistence (a)-(m) | F | answered |
| G | Content and assets | GH G | answered |
| H | UI and controls | GH H | answered |
| I | Risk | IJ I | answered |
| J | Performance | IJ J | answered; scope split missing (M9) |
| K | Final assembly (recovered as sections 1, 2, 16 and 17) | none | **gap**: no inventory (M10), no consolidated deferred list (L15), no merged part text (H2), and M7 roles not addressed (H3) |

---

## 6. What to do before the first slice

1. Apply H1 and M3 to D §1.2, then merge D into A and B (H2).
2. Raise H3 with the owner alongside Q1 and Q3, which are due before E2.
3. Repair criteria 1, 4, 14 and 31, and guards G9 and G10 (L7, M1, L8, L12, M4, M5).
4. Publish the Crossing Workshop command table (M6) and the N-A3, N-A4 and P1 scripts (M7, M8).
5. Write final sections 2 and 16 (M10, L15), and split E10 (M9).
