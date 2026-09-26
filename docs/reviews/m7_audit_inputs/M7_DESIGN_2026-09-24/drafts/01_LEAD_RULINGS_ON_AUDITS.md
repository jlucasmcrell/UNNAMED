# M7 design - design-lead rulings on the audits (input to every final-section editor)

Date: 2026-09-24. Inputs: `audit/audit_scope.md`, `audit/audit_code.md`, `audit/audit_determinism.md`, `audit/audit_impl.md`, and the reconciliation register in `D_cross_system.md` §1.

Precedence for the final document: **these rulings > D's reconciliation register > A/B/C/E/F/GH/IJ drafts.** Every audit finding not named below is **accepted as written** (apply its FIX). A finding is rejected only where this file says so.

## L1. Knowledge channel: M7 is report-only (the witnessed channel is deferred to M9)

- M7 builds exactly one knowledge channel: **reported** (`report_act` in a conversation with a member). Nothing else writes knowledge or standing.
- The witnessed channel (C K2-K4, `WitnessRules`, `BestWitness`, witness sight/FOV/identify config, identity upgrade, the witness-occlusion reputation rows) is **specified as the M9 extension, not built**. Keep its rule text in §5 as "M9 extension (not built)". Include the audit fixes that M9 must honour when it builds it:
  - facing from saved or content state only;
  - exclude errand members and members outside the seat radius;
  - an integer cone instead of `Perception.Sees` floats.
- Keep the seams as **stored data shape**, so M9 needs no migration: knowledge rows keep `source`, `via` and `identity` string keys (always `reported` / `identified` in M7), the act log, and `seat_location_ref` (lint-only).
- Why:
  - shipped Ashen Hollow cannot exercise witnessing (every faction learns by report, C §5.5);
  - the channel carried both high findings: psychic pooling for the errand member (scope) and unsaved-facing save/continue divergence (determinism);
  - reports alone prove the ROADMAP exit and "no psychic factions".
- Consequences:
  - Tests "faction without knowledge does not update" and "same act, two factions" run through reports (tell one faction, not the other).
  - "Unknown actor" is **not relevant in M7**, because a report always identifies. The identity field is the seam, and a unit test proves an `unidentified` row applies no delta.
  - `SystemContext.SightWalls()` is still built, for combat, creature and companion sight with placed walls, but not for factions.

## L2. Damage: one source in M7

- Exactly one damage rule ships: **player melee strikes on a piece's blocking part** (`FirstStop`, authored geometry wins ties).
- Shot and formula damage are reserved rows beside creature and fire damage, not built. This removes the `CombatSystem.Loose` signature change and the Magic hook.
- Acceptance criterion: "exactly one named damage source".
- Pads and roofs: health is stored but no M7 rule reaches it. Say so explicitly (lint allows `health_max` on them; §4 and M7_STATUS state it).

## L3. Timber: an authored container, not a renewable node

- Adopt B's fallback: an authored `container.timber_stack` near (84, 118) in the region layout, with a one-shot table of **80 `item.material.timber`**. That covers the Crossing Workshop (45) plus repairs and a playtester's experiments.
- Drop:
  - `resource.wood.deadfall` and `node.wood.deadfall`;
  - the M7 BaselineTransition;
  - `M6LayoutFingerprint` and its two tests.
- Keep `item.material.timber` (`no_sell`). The layout is outside the baseline hash, so M6 saves load without a transition.
- Still commit one real M6 save (`tests/Application.Tests/Saves/m6_hollow/`) for the historical-save proof. It is written **from the M7 branch before any schema or generator change (end of E0 or E1)**. There is no scratch worktree at e10d2c4.
- A renewable timber node can land later with its own transition. List it in the deferred section.

## L4. Performance and instrumentation: measure what M7 changes, build nothing speculative

- REQUIRED in M7:
  - `NavCounters`, including placement-check timings; F2 and the tests read them. They are never saved, never in a digest and never read by a decision (guard `CountersAreNeverRead` covers NavCounters only).
  - FrameStats gains `sim_ms`, `ticks`, `save_ms`.
  - Headless budget tests: N-A10 (plan and rebuild budgets, CI asserts at 3x, ASTRAL numbers logged), T2 (a tick with a full build area; a sibling of `SixtyCreatures_TickWithinTheBudget` with its crowd moved clear of the area; the original unchanged), T10 (building commands never throw).
  - The RK-06 200-piece round trip, run through a content-hash change and asserting doorway-to-interior plans after load.
  - Lint BLD009.
- OPTIONAL, built only when a measurement calls for it or for the owner's RAZER window:
  - the `--perf-world` mode and its 256-piece generator T11;
  - the generated `M7_COST_TABLE.md` (T1);
  - `BuildingCounters` and `FactionCounters`;
  - the other FrameStats columns;
  - T3/T5/T7/T8 as separate tests (fold their timing logs into N-A10 and T2);
  - the 30-minute companion soak T9. Note that it is owed RK-05 work from M6, not M7 scope.
- Each optional item is listed in §14 with its switch-on condition.

## L5. Determinism fixes adopted (from audit_determinism)

- **Stack order:** building's material take (check 14, placement and repair) orders stacks by (quality asc, count asc, ItemId). `Put`'s merge order (Items.cs:576, :600) changes to (count desc, ItemId). This is a minimal Phase-1 touch that fixes a latent replay-count divergence; list it under "touched minimally" with a test.
  - Raw-digest replay tests assert that no `itm_` was minted in the window; otherwise they compare the replayable dump.
  - Reword "no M7 system mints a wall-clock ID" to "M7 systems mint nothing themselves; item mints go only through the existing item commands".
- **Errand NPCs never read conversation state.** A conversation cannot be started with an errand NPC whose phase is `to_work` or `to_home`; the refusal reads the persisted phase. At work, or at home, she talks and trades normally. D27's "hold while in conversation" is deleted. The existing companion hold (Companions.cs:286-290) is a recorded pre-existing hazard, not changed in M7.
- The errand mover is **not tier-gated** in M7. Record the tier-hysteresis hazard for companions and creatures in M7_STATUS and RISK_REGISTER as owed before M9.
- NavRoute is built only through factories that validate. Trigger 7 is redefined (`Partial && Corners.Length == 1 && within corner reach`). NavRoute and FactionLedger override Equals/GetHashCode with SequenceEqual.
- **Refused `OpenDoor`:** `StuckTicks += 1` for both movers.
- Ordinal comparers everywhere; `NavTileKey : IComparable` ordered by (Tz, Tx); scratch generation wrap clears the arrays.
- A standing merge to 0 drops the row with a Warning. An `ArgumentException` from the definition pass maps to a Blocker.
- Building and assignment commands keep the `dead` refusal and drop the combat-phase `busy` clause.
- `CompanionSystem.Handle(Recruit)` refuses an NPC with an errand. `NpcSystem.Populate` drops, with a StructureAudit line, an errand whose NPC is a companion.
- Redundant saved copies are cross-checked on load (ActRecord.CellKey equals the key of its x, z; errand WorkOwner equals the piece's Owner for to_work and at_work).

## L6. Navigability check: one rule, D's reading, with the chest fix

- Any newly sealed walkable pocket is refused, whether or not a protected point is inside it. A doorless one-square hut is refused, and the reason says rooms need a doorway. Strike B §17.5's "harmless solid block".
- V-N3 restated: a NewSite with `RadiusMm > 0` (station work anchor) must pass PointClear at that radius and lie in an open component. A NewSite with `RadiusMm = 0` (chest site) needs a walkable node within `ReachMm` in an open component.
- `NavFootprint` carries no DoorOpen. Door state is read at query time.
- NAV006 and N-A1 evaluate location anchors as reach points (a walkable node in the spawn flood within discovery radius, or within 1,600 mm of the footprint containing the anchor). Re-verify against shipped content.

## L7. Guards corrected (from audit_code)

- The faction-read ban is a **reflection/semantic check**: no type in the tactical files (Combat, Creatures, Companions, Magic, Errands, Navigation, Building, BuildingRules, both Quests files) references `UNNAMED.Domain.Factions` or `State.Factions`. Never ban the bare token `TierOf`. The ladder method is named `StandingLadder.StandingTierOf`.
- The Godot-navigation guard bans `NavigationServer3D`, `NavigationAgent3D`, `NavigationRegion3D` and `NavigationMesh` in `src/Presentation/**` except `src/Presentation/Spike/**`.
- G10 compares a geometric projection, not Blocker records with Ids. G9 injects a piece row over a spawn sample, and adds a source-scan assertion that `CreatureSystem.Populate` reads `Setup.Layout.Space`.
- `ConstructionAndLoad_PublishNoM7Event` subscribes to every new M7 event type.
- IDialogueFacts' third implementer (`tests/Domain.Tests/DialogueRulesTests.cs:12`) is in the change list.
- The CameraRig const stays. Add `BuildMaxDistance = 9f` and an instance `Cap`.

## L8. Acceptance and scenario fixes (from audit_impl)

- **One command table** `(tick, pose, command)` drives `CrossingWorkshop.Start()`, the step-group tests and `--build-shots`:
  - start at (100.5, 94.5) facing 0;
  - build step 1 from (102.0, 102.0) inside the footprint;
  - open the door, walk out to (100.5, 97.0), close it, then run step 3.
  - Give the per-slice expected counts (pieces, timber, StructureSequence) for E5, E6, E8 and E9.
- **Criterion 4:** every committed place, dismantle or destroy of a piece with at least one blocking or door part sends exactly one `RebuildNavigation`. Pads and roofs send none, and all of them bump StructureSequence.
- **N-A3/N-A4** are specified by rule (the mid-walk wall on Kera's first in-area route segment, from a scripted pose within 6 m), or mapped onto `CrossingWorkshop_9`. The editor picks one and specifies it completely.
- **P1 (armour kill) headless:** the CreatureTests arena placement with the shipped armour definition, killed with the March Spear, fixed seed, tick cap. The "shipped content" claim covers P2-P5's dialogue and gates. The playthrough beat kills the real armour, with Tavar ordered to wait. STOP on the first failure; the run is deterministic.
- **A new-game building proof:** one headless test (new game → take timber from the stack → place a pad and a wall in the area → save → load → 0 StateDump differences) and one short playthrough beat.
- **E0's STOP** is a grep-able checklist signed by the owner in M7_STATUS.
- Criterion 31: "no architecture test is weakened; the only allow-list addition is PreviewPlacement."
- Fixture-pack lint failures in later slices bump the pack to 0.2.10 (hash and mirror in the same commit).

## L9. Scope ledger (from audit_scope)

- Speculative abstractions trimmed:
  - navigation content ships only the `person` class; the class-count encoding keeps more classes content-only later, and N-D6 stays synthetic;
  - the act log evicts oldest-first, with no `IsSettled`;
  - "a destroyed doorway destroys its door" replaces the recursive mount cascade;
  - no compass hint footer.
- **Territory gating** joins owner question Q2 (deferred with crime, bounty and pardon).
- **D-04 note:** E0 adds a dated note to D-04. Derived identities (NPC since M4, creature since M3d, piece and piece-chest since M7) are ULID-shaped with an ordinal or constant timestamp, replay-stable and never time-ordered. Not an owner question; the precedent exists. Amend the `EntityId.Create`/`Timestamp` doc comments in E2.

## L10. Owner questions (final five) and owner process items

The design proceeds on every default. No question blocks a slice before its latest decision point, and after that point the default stands.

1. **Rotation.** Quarter turns on a 3 m lattice (default), or 45°/free with oriented footprints? D-08 (rank 2) says "free rotation". Latest decision point: before E5.
2. **Crime, bounty, pardon (S-27) and territory gating.** Defer beyond M7 with the act log and gate-access seams (default), or a minimal stub? This deviates from ROADMAP M7's text. Latest: before E0 writes the ROADMAP reconciliation.
3. **"Assign an NPC to work in it".** Kera Voss walks to a player-built anvil bench as a persisted errand (default, the literal ROADMAP exit), or reconcile the exit to M10, where ROADMAP puts NPC work assignment, and VS's "hirelings 0"? Latest: before E2, to avoid carrying an unused schema field; E9 is skipped if moved.
4. **Where the player may build in M7.** One content-defined area, `build_area.hollow_crossing` (x/z 87-114 m over the four-cell corner, no quest gate) (default), or anywhere legal? This also supersedes the Ashen Hollow bible's "no player settlement building" line for M7. Latest: before E5.
5. **The working faction pair and proof.** The Waystation (Renn, Kera) and the Survey (Sel), Tavar unaffiliated. The Blackvein Animated Armour kill: Waystation +100, Survey −100. Gates at "accepted": Sel's notes reply and Kera's iron billets. Report-only knowledge in M7. Working names are not canon. Latest: before E3.

Owner process items (not design questions):

- Authorize M7 and name the agent, worktree, branch and draft-PR convention (AGENTS.md scopes Claude "through M6").
- Schedule the owed RAZER window.
- R-1's tagged build: tag at merge, or waive.
- Tone review of the new dialogue lines; they ship as written unless the owner objects before E10.
- The feel test remains deferred; it is not an M7 entry blocker by the 2026-09-24 ruling.
