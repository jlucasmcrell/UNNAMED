# M7 scope audit

Auditor: scope auditor for the M7 design team, 2026-09-24. Design audit only. No draft was edited.

Inputs read in full: `drafts/00_SCOPE_RULINGS.md`, `A_navigation.md`, `B_building.md`, `C_factions.md`, `D_cross_system.md`, `E_slices_tests.md`, `F_persistence.md`, `GH_content_ui.md` and `IJ_risk_perf.md`. I also read `research/authority.md` §0-§2. Every code or doc fact cited below was re-read in the read-only snapshot of origin/main `e10d2c4` (`scratchpad/main_e10d2c4`), and every citation is repo-relative. Draft citations are `<file> §<section>` or `<file>:<line>`.

Precedence I applied, as the drafts state it: D overrides A, B and C. E, F, GH and IJ refine D. A finding against a part that D already corrected is not repeated here.

---

## 0. Verdict

The draft set is unusually complete against ROADMAP M7. Every Work, Exit and Proof clause of `docs/ROADMAP.md:278-286` maps to a designed mechanism and a named test (§2), with one exception, territory gating. No draft makes Godot authoritative, requires a radial menu, adds networking, lets standing feed hostility, adds a global morality meter, or hides a walkable elevated surface. Each of those was checked against the code (§3).

The audit found no critical defect. It found two high findings:
- **Scope creep.** Parts IJ and E10 add a large layer of performance and measurement infrastructure that no ROADMAP M7 line asks for. The design's own "build nothing until a measurement trips it" rule is not applied to it.
- **Ruling violation.** D30 knowingly accepts a psychic-knowledge case: a faction member away from her seat pools what she sees into the faction at once. A one-predicate fix exists.

The medium findings:
- two further trims to the smallest satisfying design: the witnessed-knowledge channel has no shipped consumer, and there are three damage sources where one suffices;
- a baseline transition added only for renewable timber;
- territory gating dropped without owner sign-off;
- the D-04 instance-ID doctrine amended below the rank-2 decision it bends;
- an owner-question list that includes one non-question (Q3) and turns defaults into blocking gates.

The low findings are internal inconsistencies, missing process gates and small speculative seams.

---

## 1. Findings, ordered by severity

### HIGH-1 · Scope creep · IJ J.11-J.12 and E E10: performance instrumentation is horizontal infrastructure M7 does not require

**Where:** `IJ_risk_perf.md` §J.11.1-J.11.4 and §J.12; `E_slices_tests.md` §E10 (the code list, `:613-617`); E §13.1 (`--perf-world`); E acceptance criterion 33.

**Problem.** M7 is planned to build all of the following:
- three deterministic counter sets: `NavCounters` (about 30 counters, A §14), `BuildingCounters` and `FactionCounters`;
- a `CountersAreNeverRead` source-scan guard;
- 23 new `FrameStats` columns, with a summary block that gains about 12 statistics;
- a new `PerfRun` mode, `--perf-world <dir>`, with 5 segments;
- a 256-piece, 9-room perf-world generator (T11) and an env-gated save writer;
- a generated `docs/M7_COST_TABLE.md` (T1);
- four standalone budget tests (T3, T5, T7, T8);
- a 30-minute companion soak (T9).

ROADMAP M7 has no performance exit or proof line (`docs/ROADMAP.md:284-285`). E itself labels criterion 33 "Recorded, not a ROADMAP line". The build breaks three stated rules:
- **R-2**, "Vertical, not horizontal. Three working weapons beat architecture for 500" (`docs/ROADMAP.md:16`);
- **D-12**, "no speculative abstraction for futures that are not scheduled" (`docs/DECISIONS.md:248`);
- **IJ's own rule**: J.13 says "Levers, in order: none is built until a measurement trips it". The design applies that rule to optimisations but not to the instruments.

The RAZER window these instruments serve is Phase-1 debt (`docs/M6_STATUS.md:221`), so this work also pulls the Phase-1 performance gate into M7. T9 exists only because A's N-A14 cites "the M6 random-walk soak", which IJ R-A5 found does not exist.

**Fix.**
- **Keep in M7:**
  - N-A10: the navigation build, plan and `CheckEdit` budgets, with ns per expansion logged;
  - T2: the tick at the cap with pieces, which replaces D S7's "the existing test with 200 pieces";
  - T10: `BuildingCommands_NeverThrow`;
  - B's RK-06 200-piece test;
  - `SixtyCreatures_TickWithinTheBudget`, unchanged;
  - only the `NavCounters` that the F2 overlay and N-A10 read (plans by outcome, expansions, rebuilds, edit refusals).
- **Remove from M7**, and record each in `M7_STATUS` as an instrument to build only if the RAZER window or T2 measures a problem:
  - T1 and `M7_COST_TABLE.md`;
  - `BuildingCounters`, `FactionCounters` and `CountersAreNeverRead`;
  - the 23-column `FrameStats` extension (add only `sim_ms`, if anything);
  - `--perf-world`, T11 and the J.12 additions;
  - T3, T5, T7 and T8 as separate tests: fold their timing logs into N-A10 and T2;
  - T9, and with it N-A14.

---

### HIGH-2 · Ruling violation (psychic knowledge) · D30, C §5.2, IJ R-C2: a working member pools what she sees into her faction from 62 m away

**Where:**
- `D_cross_system.md:44` (D30: "Accept; no code");
- `C_factions.md` §5.2 ("Accepted residue ... keeps its faction and pools what it sees wherever it stands") and §20 open issue 4;
- `IJ_risk_perf.md` R-C2 ("Accepted residue").

**Problem.** C justifies K6 (a faction learns at the tick any member learns) only by the seat invariant, FAC-M1: every member stands inside the seat's radius. C §5.2 itself quotes the contrary doctrine, "Same faction does not imply instant shared awareness" (`docs/STEALTH_DETECTION_AND_THREAT.md:100`).

M7 then creates the first NPC who leaves her seat. Kera's work anchor at (100.75, 103.5) is about 62 m from `location.outpost`'s anchor (55, 145), whose discovery radius is 30 m (`content/locations/outpost.yaml`; `content/regions/ashen_hollow.yaml:154`). Anything Kera witnesses at the bench reaches Renn in the lodge in the same tick. That is exactly the "magically global information" that owner ruling 3 forbids.

The case is unreachable in shipped M7 content:
- the sentinel "guards its post and no further", with `territory_m: 10` (`content/config/creature_behaviour.yaml:30`);
- the Foldscar heart is about 76 m from the bench.

But the rule is written into the code as correct, and the scope rulings require M7's systems to be reusable by M9 without rework (`00_SCOPE_RULINGS.md` §1). An accepted ruling violation is not an owner question either: the design can fix it.

**Fix.** In K2 (`FactionRules.BestWitness`), exclude any member that has an `NpcErrandRecord`, exactly as companions are excluded. Equivalently, exclude any member whose body lies outside its faction's seat radius at the act tick; that is the doctrine candidate's runtime seat check. It is one predicate and changes no save shape.
- Add fixture row F15: an errand member within 15 m of a relevant act, away from the seat, produces no knowledge row.
- Replace D30 and R-C2's "accepted residue" with the guard.
- Add a line to C §18's reconciliation list stating the seat-local pooling approximation against STEALTH §6.

---

### MEDIUM-1 · Missing required / silently resolved · C §18, E E0: territory gating is dropped from ROADMAP without an owner question

**Where:** `C_factions.md:1063` ("'Territory gating': deferred"); `E_slices_tests.md:106` (E0 rewrites the ROADMAP M7 entry with "territory gating deferred"); `D_cross_system.md` §6.1 (Q2 covers crime only).

**Problem.** ROADMAP M7 Work lists "service, dialogue and territory gating derived from standing" (`docs/ROADMAP.md:282`). Scope ruling row 10 made territory "optional-if-cheap" (`00_SCOPE_RULINGS.md:32`). The drafts then delete it from the ROADMAP text in E0.
- Crime, from the same ROADMAP bullet, gets an owner question (Q2).
- Territory, a ROADMAP work item removed from the owner's own schedule text, gets none.
- The scope rulings are a design lead's input and state that they are "not an owner ruling" (`00_SCOPE_RULINGS.md:3`).

**Fix.** Fold it into Q2: "Crime, bounty and pardon (S-27) **and territory gating** are deferred beyond M7 (default); the act log and the reserved gate access set (A §9) are the seams." A cheap build is not recommended. It would need an access check on placement or a door, and D's G19 and the G6 scan forbid building and assignment from reading standing.

---

### MEDIUM-2 · Scope creep (M9 machinery) · C §5, §17.1: the witnessed-knowledge channel has no shipped-content consumer in M7

**Where:** `C_factions.md` §5.1 (K2-K4), §5.3-§5.5, §6.5, §17.1 rows F1-F10 and F12; D11; E3/E5/E6 (the placed-wall and closed-piece-door witness rows).

**Problem.** C says it plainly: "in shipped play every faction learns by report ... M9's town geometry will exercise witnessing" (`C_factions.md:309`, §5.5; §20 open issue 5). Everything below is built and tested in M7 only against fixtures:
- the witness sight check (`Perception.Sees` with `WitnessRules`: 30 m, a 140° cone, a 15 m identify range);
- `BestWitness` and its tie-breaks;
- the `unidentified` rung and its one-time upgrade;
- the occlusion rows for authored walls, placed walls and closed piece doors.

This is M9 behaviour carried in M7. The ROADMAP needs per-faction directional reactions and "the same act moves two factions in opposite directions in a fixture" (`docs/ROADMAP.md:282`, `:284`). P5 proves that on shipped content through reports alone (C §17.1), and reports alone already satisfy "knowledge-gated, no psychic factions".

**Fix.** Recommended: defer `witnessed` to M9.
- M7 records acts and learns only by `report_act`.
- Keep the stored `source` and `identity` string keys, which in M7 are always `reported` and `identified`. C Appendix C confirms that string-key sets extend without a migration.
- Re-express F1 as a report-only fixture: tell an A member and a B member.
- Keep P1-P5, F5, F8, F10, F11, F13 and N1.
- Drop `WitnessRules`, `BestWitness`, the identify range, FAC-M1's runtime role, and the witness occlusion rows.

`SightWalls()` stays, because building needs it for combat, creatures and companions. If the team keeps witnessing instead, it must own a shipped-content use, or be recorded in the `M7_STATUS` scope ledger as deliberate M9 pre-work. Either way, HIGH-2's guard is needed only while witnessing exists.

---

### MEDIUM-3 · Scope creep · B §13.1, E criterion 16: three damage sources where the scope ruling asks for the smallest set

**Where:** `B_building.md` §13.1 (melee 10, shot 2, formula 12); B §22 (shot and formula tests need a hunting bow, 5 arrows, might ≥ 9 and the impulse bolt); `E_slices_tests.md:726` (criterion 16: "exactly three damage sources").

**Problem.** Scope ruling row 8 asks for "the smallest set that makes damage reachable in play without inventing threats" (`00_SCOPE_RULINGS.md:30`). One source satisfies "damage/repair works and is explicit rather than emergent" (`docs/ROADMAP.md:284`). The other two cost extra code and risk:
- **Shot** adds a new damage-source parameter to `CombatSystem.Loose` (`src/World/Runtime/Combat.cs:511-517`).
- **Formula** adds a hook at `src/World/Runtime/Magic.cs:114`.
- Both touch combat code the Phase-1 suite protects, and add their own kit-dependent tests.

**Fix.** Ship `melee` only. The `FirstStop` rewrite is needed for melee anyway. List `shot` and `formula` as reserved rows beside `creature_charge` and `fire`. Criterion 16 becomes "exactly one named damage source". Arrows and formulas keep stopping at piece walls, as they stop at authored walls today.

---

### MEDIUM-4 · Scope creep with persistence risk · B §8.1-§8.2, F §8.3: a renewable timber node forces M7's only baseline transition

**Where:** `B_building.md` §8.1-§8.2 ("The deadfall is decided"); `F_persistence.md` §8.3; E5 (the `M6LayoutFingerprint` and transition tests).

**Problem.** Fixed nodes are generator input: `GameSession` passes `fixedNodes` into the `GenerationProfile` (`src/Application/GameSession.cs:104-107`). Adding `node.wood.deadfall` therefore:
- changes cell `r_0_0:c_00_01`'s baseline and the worldgen fingerprint;
- forces a frozen `M6LayoutFingerprint`, a new `BaselineTransition` and two new tests;
- makes every M6 save rebase cell A on its first M7 load.

B's own recorded fallback is an authored `container.timber_stack` with a one-shot loot table. Containers are region layout, not generator input: `RegionLayout.Containers` is an init property, and only the nodes reach the generator (`src/Domain/Spatial/RegionLayout.cs:69-70`; `GameSession.cs:104-107`). So the fallback needs no transition.

The stated reason for the node is "it is what makes repair sustainable". Sustainability is not an M7 exit criterion, and scope row 13 says "no new economy" (`00_SCOPE_RULINGS.md:35`).

**Fix.** Adopt the fallback: `container.timber_stack` at (84, 118), with a one-shot table sized for the Crossing Workshop plus repairs (B suggests 80). Keep `item.material.timber`. Drop `resource/node.wood.deadfall`, the M7 transition, `M6LayoutFingerprint` and the two transition tests.

Keep the committed M6 save (F §12.2): it still proves the 13 → 14 load (criterion 20). If the owner later wants renewable timber, the node lands with its transition in the milestone that wants it.

---

### MEDIUM-5 · Conflict resolved silently (ruling 7 / D-04) · B §18, D4, D §1.7: derived piece IDs bend a rank-2 decision that only lower documents are amended around

**Where:** `B_building.md` §18; `D_cross_system.md` D4 and §1.7; E0 and E10 document lists (AGENTS.md, DATA_MODEL `:123` and the `EntityId` remark are amended; DECISIONS D-04 is not).

**Problem.** The design mints piece IDs as `EntityId.Derived(Piece, StructureSequence, …)`. It writes a persisted per-world counter into the ULID timestamp field, and `TryApplyPiece` then checks `Timestamp ≤ StructureSequence`. This contradicts the governing texts on four points:
- D-04 says instance IDs are "generated at runtime as **ULIDs**" (`docs/DECISIONS.md:101`), and rejects "Integer handles (... require a central counter that must persist ...)" (`:103`).
- `EntityId.Timestamp` is documented as "Creation time in Unix milliseconds" (`src/Domain/EntityId.cs:45`).
- `Create` is documented "For fixtures and tests; runtime code uses NewId" (`:56`), and the class remark says instance IDs are "never derived from a seed" (`:18`).
- AGENTS.md says "never build one by hand" (`AGENTS.md:45`).

Owner ruling 7 restates "ULID runtime instance IDs". The design edits AGENTS, DATA_MODEL and the code comment, all below rank 2, and leaves D-04 untouched.

Precedent exists, so this is not an owner question:
- NPC IDs are `EntityId.Create(EntityKind.Npc, 1, hash)` at runtime (`src/World/Runtime/Social.cs:121-125`);
- creature IDs likewise (`src/World/Runtime/Creatures.cs:165`).

But the rank-2 text must say so.

**Fix.** E0 adds a dated D-04 note: "Derived identities (NPC since M4, creature since M3d, piece and piece chest since M7) are ULID-shaped with an ordinal or constant timestamp. They are replay-stable, never time-ordered across worlds, and never compared across saves. The piece ordinal is a per-world persisted counter, acceptable because single-player saves never merge." Also:
- amend `EntityId.Create`'s and `Timestamp`'s doc comments in the same slice as `Derived` (E2), not in E10;
- have `TryApplyPiece` state that it reads the timestamp as the derivation ordinal.

---

### MEDIUM-6 · Owner questions · D §6.1, E §E.1 and §18: Q3 is not genuinely open, and E turns defaults into blocking gates on a false schema dependency

**Where:** `D_cross_system.md:590-598`; `E_slices_tests.md:62` ("the owner answers that could change the shapes (Q1: rotation 0..3 vs 0..7; Q3 ...) are therefore due before E2 merges"), `:229` (the STOP rule), and `:1180-1190` ("Start E2 only when both are answered or the defaults are confirmed").

**Problem.**
1. The scope rulings say "the design proceeds on the stated default and records the question for the owner" (`00_SCOPE_RULINGS.md:3`). E instead makes Q1 and Q3 blocking gates on the critical path.
2. **Q3** ("assign an NPC to work in it": default or M10) asks the owner to confirm his own ROADMAP exit text (`docs/ROADMAP.md:284`), and the default implements that text literally. Only the owner can remove an exit criterion, and he does not need to be asked in order to keep one. If he later moves it to M10, the `npc_errands` list simply stays empty, and E9's STOP already describes the replan.
3. **Q1's** claimed schema dependency is false. `PieceDto.rotation` is an `int` on the wire (B §9.2). F moved the range checks from decode to `TryApplyPiece` (F §0 item 4, §6.4), and footprints, slots and sockets are derived, never saved. A 45° answer changes a value range, the lattice code and blocker shapes. It does not change schema 14's wire shape, so it cannot gate E2.

**Fix.** The final owner list, four questions within the cap of five:
1. **Rotation** (Q1): needed before E5, where `Lattice`, `QuarterTurn` and `Snapper` land. Not before E2.
2. **Crime, bounty and pardon, and territory gating, deferred** (Q2 widened per MEDIUM-1): needed before E0 writes the ROADMAP text.
3. **Where to build** (Q4): genuine, because it supersedes the owner-approved content bible line "no player settlement building". Needed before E5.
4. **The working faction pair, the proof act and the two gates** (Q5): genuine, because it is lore and tone, and the dialogue text is a DRAFT. Needed before E3.

Move Q3 to a recorded default in `M7_STATUS` with no deadline. Remove the "answered before E2 merges" gate. No other genuine owner decision is missing.

---

### LOW-1 · Internal inconsistency · E criterion 4 vs E5 and beat b04: pads and roofs

**Where:** `E_slices_tests.md:681` (criterion 4: "Every committed place, dismantle and destroy sends exactly one `RebuildNavigation`"); `:361` (E5: "For pads and roofs building sends no rebuild (J.3)"); `:1091` (b04: "no `NavigationRebuilt` for pads"); `B_building.md` §3.4 (dispatches for every placement).

**Problem.** The acceptance criterion contradicts the slice code and its own beat. IJ J.3 raised the issue. E5 resolved it, but criterion 4 and B §3.4 were not amended.

**Fix.** Criterion 4 becomes: "every committed place, dismantle or destroy of a piece with solid or door parts sends exactly one `RebuildNavigation`; pads and roofs send none." Add B §3.4 to E.5's list of changes asked of B.

---

### LOW-2 · Missing process gates · E §18, E.4, E10: authorization, the agent role and R-1's tagged build

**Where:** `E_slices_tests.md:1190` ("First action for the implementing agent. Land E0 as three commits"); §E.4.

**Problem.** No draft says that M7 is not yet authorized. The drafts do not mention it at all (grep finds no "authoriz" in any of them).
- `docs/M6_STATUS.md:187` says "M7 is not authorized, and no M7 work begins".
- The roles ruling names Claude's worktree and branch only "through M6" (`AGENTS.md:13`).
- ROADMAP R-1 requires "a tagged build number" at each milestone (`docs/ROADMAP.md:15`), and the research notes record that no tag has ever existed. E does not mention it.

**Fix.** Give E0 a precondition: the owner authorizes M7 and names the agent, worktree and branch. E10's closeout records R-1 as the owner's tag at merge, or records the owner's waiver. An agent tags nothing.

---

### LOW-3 · Missing proof · B §11, E §13.1: no proof covers the building loop a playtester actually takes

**Where:** `B_building.md` §11 "Start" (45 timber, recruited Tavar); `E_slices_tests.md` §13.1 (the committed start save `m7_crossing_start`); E13.2 (the playthrough has faction beats but no building beat).

**Problem.** Every building proof starts from a crafted save. No test or beat takes a new game from gathering or collecting timber, to entering build mode, to placing, to saving. Yet "Playable build" (`docs/ROADMAP.md:285`) is what the owner's playtest will exercise from a new game.

**Fix.** Add one headless test: new game, collect timber from the M7 source (the node or the MEDIUM-4 stack), place a pad and a wall in the area, save, load, 0 `StateDump` differences. Add one short `--playthrough` beat that does the same.

---

### LOW-4 · Speculative abstraction bundle (A, B, C, GH)

**Where and problem:**
- **(a) Three navigation radius classes.** A ships `person` 350, `medium` 450 and `large` 550 in `config.navigation` (A §3, §13.3), but its own table lists "Creatures (not in M7)" as the only users of classes 1-2 (`A_navigation.md:689`), and creature pathing is declined (A §1).
- **(b) Act-log eviction.** C's act log has a 256 cap with a "settled-first" eviction rule and `IsSettled` (C §6.5, row F14), while shipped play records at most two acts (C §4.3).
- **(c) Destruction cascade.** B's destroy runs a general recursive mount cascade (B §10.3) whose only reachable case is a doorway taking its door.
- **(d) Compass hint.** GH adds a compass-word "nearest build area is {d} m to the {direction}" footer (GH H.4.5), which is UI polish.

**Fix.**
- (a) Ship `classes: [person]`. The byte encoding stays a class count, so adding classes later is content-only (A §17). Keep N-D6 as a synthetic Domain test.
- (b) Evict oldest-first and drop `IsSettled`.
- (c) Replace the recursion with the explicit rule "a destroyed doorway destroys its door".
- (d) Drop the footer.

---

### LOW-5 · Nominal requirement · B §13, §20: pads and roofs carry health no rule can change

**Where:** `B_building.md:730` ("Pads and roofs emit no blocker, so nothing reaches them"); the §20 catalogue (pad health 200, roof 100).

**Problem.** "Per-piece health ... save/load preserves every piece with correct ... health" is met for pads and roofs only vacuously. No M7 rule can damage or repair them.

**Fix.** State in B §13 and `M7_STATUS` that M7 health applies to pieces with blocking parts, and that pad and roof health is stored for a later rule. Alternatively, omit `health_max` for the `pad` and `roof` families by lint. Either is acceptable; the choice must be written.

---

### LOW-6 · Ruling-1 guard gap · D G4: Godot navigation types are not banned in presentation

**Where:** `D_cross_system.md` G4, which bans `IntersectRay`, `PhysicsRayQueryParameters3D`, `RayCast3D` and `ShapeCast3D` only.

**Problem.** Nothing stops a later presentation change from driving the walking NPC's visual with `NavigationAgent3D`, which would let the drawn Kera diverge from the authoritative one. Today no such use exists outside `src/Presentation/Spike` (grep).

**Fix.** Extend G4 to `NavigationServer3D`, `NavigationAgent3D`, `NavigationRegion3D` and `NavigationMesh` everywhere in `src/Presentation` except `src/Presentation/Spike/**`.

---

### LOW-7 · Incomplete RK-06 validation · B §22

**Where:** `B_building.md` §22 (`TwoHundredPieces_RoundTripAndStayNavigable`); IJ J.4 ("a weak collision stress": 38 walls).

**Problem.** RK-06's validation says "save, change a `content_version`-independent input, reload" (`docs/RISK_REGISTER.md:154`). B's test omits the change step. Yet I.3 proposes marking RK-06 as validated by that test.

**Fix.** Run the 200-piece round trip through a content-hash change, so the definition pass and the `with` copies run on it. Use F's alias pack or a content copy with a changed hash. Then assert the doorway-to-interior plans after the load.

---

## 2. ROADMAP M7 coverage (`docs/ROADMAP.md:278-286`)

| ROADMAP clause | Design | Test / evidence | Status |
|---|---|---|---|
| Entry: NPCs and companions path reliably | A §8.3 companion Route mode; A §8.4 errand mover | C16 unmodified, N-A11, N-A12, E criterion 7 | Covered; met inside M7 (scope row 14) |
| Faction definitions and membership graph | C §3, §10.2: NPC `faction_ref`; member index derived | FAC001 set, E criterion 26 | Covered |
| Per-faction reputation, directional reactions, no universal morality meter | C §6, §16; no aggregate type | F1/P5, G6, `TheLadder_HasNoHostilityTier` | Covered |
| Cross-faction attitude relations | C §10.1: static attitude words | F11 | Covered (static, scope row 11) |
| Crime/bounty records and pardon state | reserved seam (act log) | criterion 34 | Deferred, with Q2 |
| Service and dialogue gating | C §8: Kera's billets, Sel's notes | the gate tests, criterion 25 | Covered |
| Territory gating | none | none | **Deferred without an owner question (MEDIUM-1)** |
| Reputation is access only (D-09) | C §12; the axis-independence tests | criterion 27 | Covered |
| Socket/snap; free rotation | B §0.4, §5: quarter turns | lattice and `Snapper` tests | Covered; rotation is Q1 |
| Foundations, walls, floors, roofs, doors | B §20: pad = foundation and floor (ruling 2) | `EachPiece_Places_…` | Covered, reconciled |
| Ownership | B §12 | `ForeignPieces_…`; the v14 fixture's foreign owner | Covered |
| Per-piece health, explicit rules; repair | B §13 | criterion 16 | Covered (MEDIUM-3, LOW-5) |
| Storage containers; one crafting station | B §14 | chest and anvil tests | Covered |
| Sparse world delta (D-05) | B §9, F | criterion 19, fixture v14 | Covered |
| Validation: overlap and un-navigable | B §3.3 rules 7, 10 and 12; D5 V-N1..V-N4 with BLD008 | N-D19, N-A13, `CrossingWorkshop_7` | Covered |
| Exit: build, assign an NPC, in/through/around, straddling | B §11 Crossing Workshop; D24 | N-A2 = `CrossingWorkshop_5to6`, b11-b13 | Covered |
| Exit: save/load keeps pieces, owner and health | F §9-§10 | criterion 15 | Covered |
| Exit: damage/repair explicit | B §13 | criterion 16 | Covered |
| Exit: "navmesh updates on placement" | A §5 rectangle rebuild; D10 | N-A3, criterion 4 | Covered (LOW-1 wording) |
| Exit: the same act, two factions, opposite, in a fixture | C §17.1 F1 and P5 | `ReputationTableTests` | Covered |
| Proof: playable build | E criterion 32 | `--playthrough`, `--build-shots` | Covered (LOW-3 gap) |
| Proof: path test incl. the straddling seam | N-D9, N-D10, N-A2, N-A7 | criteria 5 and 14 | Covered |
| Proof: structure round trip | B §9.7 | criterion 15 | Covered |
| Proof: reputation fixture table | `docs/M7_REPUTATION_TABLE.md` | criterion 22 | Covered |
| PROGRESSION: AX-REP joins the axis tests at M7 | C §17.2 | `AxisIndependence_Reputation*` | Covered |

---

## 3. Owner-ruling compliance (checked in the snapshot)

| Ruling | Result | Evidence |
|---|---|---|
| 1 Navigation domain-side, deterministic, headless, replayable, derived, edit-responsive, seam-proof | Compliant | A §4, §5.3, §6: integer-only (N-X1); seams fall between node indices (100,000 / 250 = 400); rebuilt in the `Simulation` constructor; never saved. Guard gap in LOW-6 |
| 2 One storey | Compliant, no hidden elevated surface | Body Y is always `Terrain.HeightAtMm + LiftMm` (`src/Domain/Spatial/Kinematics.cs:159`). "Nothing is stood on ... landing on one pushes the body off it" (`Kinematics.cs:124-127`). Pads emit no blocker (B §6). Roofs are presentation-only slabs with camera colliders (GH G.3.1). `ClearanceMm = 0` on every part (B invariant 2). `MovementRules_GainsNoMembers` guard (E) |
| 3 Smallest faction set; separate layers; same act, two factions; no global information | Compliant except HIGH-2 | C §12's separation table; G6 extended by D29; relations never move standing (F11); no hostility tier. D30 accepts a psychic case (HIGH-2) |
| 4 C10 retired | Compliant | E criterion 34 |
| 5 No required radial | Compliant | GH H.2 key table: every action is a named `InputMap` action on a direct key; `EveryM7Action_IsBoundToADirectKey` |
| 6 No networking | Compliant | Seams only (E criterion 31) |
| 7 Engine-independent authority; commands; dotted IDs; ULID instances; sparse deltas | Compliant, with a doctrine note owed (MEDIUM-5) | `PlacePieceCommand` carries a discrete pose (B §3.1). The ghost comes only from `PreviewPlacement` (D26, G7). No physics queries (G4). Derived IDs follow the NPC precedent (`src/World/Runtime/Social.cs:121-125`) |

**Presentation deciding outcomes: none found.**
- The target rule (GH H.4.7) and the Y station choice (GH H.5) only choose which ID to name.
- The authority re-validates reach, ownership, kind and reachability (B §15.2 refusals 1-10).
- The ghost's tone logic caches authority answers and never computes legality.

---

## 4. Conflicts: resolved in the open vs silently

**Resolved in the open, and correctly:**
- A vs B on the mover's home (D1), the blocker join (D2 with G9) and the edit check (D5 with BLD008);
- IDs (D4);
- the route type (D19);
- the scene (D24);
- F's refinements of B's decode checks and of D14's missing `SchemaV11ToV12` repoint;
- GH's `CameraRig.MaxDistance` const;
- E's vertical re-cut of D §3.2;
- `SYSTEMS.md:443` (A §11), stated as an amendment;
- PROGRESSION §10 decay (`docs/PROGRESSION.md:454`), deferred and recorded in C §18.

**Resolved silently, or left inconsistent:**
- territory gating (MEDIUM-1);
- D-04 (MEDIUM-5);
- the seat invariant vs the moving member (HIGH-2);
- criterion 4 (LOW-1);
- E's deadlines vs the scope rulings' "proceed on default" (MEDIUM-6).

---

## 5. Checked and found clean

- **Nothing from M8 or later is built.** No dungeon, boss, multi-stage quest, settlement simulation, schedule, wage, hireling, raid or home-defense frequency option is built. `construct_building`, `faction_reputation` and `faction_state` stay `NotBuilt` (E criterion 34). A §9 and §15.2 (portals, 2 km residency, roads) are notes, explicitly "designed now, not built".
- **Kaldrun Reach, Vessmere and the VS factions** are untouched (scope §1).
- **DeepSeek paths** are untouched (GH G.1; D §5.3).
- **Persistence:** one schema bump, no new section file, required-on-decode fields, and G11 and G12 against the hand-list traps (F). This is the smallest safe persistence design.
- **Building acts** are refused (C §13, G13), which keeps items from converting into standing (E-7).

---

## Appendix: why nothing is rated critical

Every clause of the ROADMAP exit and proof maps to a test, and no shipped behaviour breaks an owner ruling. HIGH-2 is unreachable in shipped content, and HIGH-1 costs effort without breaking behaviour. Both are high because they are either an accepted ruling breach or the largest block of work that M7 does not need. Both have small, reversible fixes.
