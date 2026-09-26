## 17. Owner decisions

Only five questions meet the bar. For each, two viable choices materially change gameplay or design, the current authority documents do not settle it (or conflict), and a wrong choice made now costs rework later. Every other choice in this document is settled with a recommended default.

The design proceeds on each recommendation. No question blocks any work before its **latest decision point**. If no answer has come by that point, the recommendation stands and is recorded in `docs/M7_STATUS.md`.

### Q1 - Rotation granularity for building pieces

- **QUESTION:** M7 pieces turn in quarter turns (90°) on a 3 m lattice. Should v1 instead support 45° steps or free rotation?
- **OPTION A:** Quarter turns. Every piece stays an axis-aligned box. `Kinematics.Step`, combat traces, sight, presentation prediction and the navigation rasteriser are untouched.
- **OPTION B:** 45° steps or free rotation. This needs oriented footprints in `Blockers.cs`, which is the shared movement function used by the player, creatures, the companion and prediction. It also needs oriented rasterisation in navigation, rotated socket math, and new combat-trace and sight tests.
- **YOUR RECOMMENDATION:** Option A.
- **WHY IT MATTERS NOW:**
  - D-08 (rank 2) promises "free rotation/socketing", and VERTICAL_SLICE says "45° steps". Option A is a staged reading of D-08, so the owner should confirm it.
  - Option B turns M7 into a change to the collision core, with determinism risk across every mover.
- **WHAT CAN BE DEFERRED:** Nearly everything. The saved rotation is an integer, and footprints are derived, so a later move to 45° changes no schema-14 shape. The cost of B is paid only when B is built. The lint-pinned `config.building.rotation_step_deg` key (90 in M7) keeps the door open.
- **Latest decision point:** before E5 (the first placement slice).

### Q2 - Crime, bounty, pardon and territory gating in M7

- **QUESTION:** ROADMAP M7 lists "crime/bounty records and pardon state (S-27)" and "territory gating derived from standing". Build a minimal version in M7, or defer?
- **OPTION A:** Defer beyond M7. The act log (with string-keyed source and identity) and the gate-access sets are the seams. Nothing derives legal status or hostility from standing.
- **OPTION B:** A minimal stub: an offense record, a bounty value and a pardon command. Territory gating would sit on the build area. Today nothing in the game can be a crime: NPCs cannot be harmed and containers have no owner. A stub would therefore also need NPC attackability or ownership-aware containers, witnesses and legal-status state.
- **YOUR RECOMMENDATION:** Option A.
- **WHY IT MATTERS NOW:**
  - PROTOTYPE `:40` and INDEX `:89` put crime in Phase 3, and VERTICAL_SLICE `:336` makes it a non-goal. The owner's ruling 3 requires legal status to stay separate from reputation.
  - A stub built now would either violate that separation, or grow into the witness, evidence and jurisdiction system the doctrine describes.
- **WHAT CAN BE DEFERRED:** The whole system. M7's act records and knowledge rows already carry the fields a later crime milestone needs (act kind and subject, location, tick, knower, source, via, identity).
- **Latest decision point:** before E0 writes the ROADMAP reconciliation.

### Q3 - "Assign an NPC to work in it"

- **QUESTION:** The M7 exit requires assigning an NPC to work in a player-built structure. Keep the smallest literal version in M7, or move the criterion to M10?
- **OPTION A:** Kera Voss can be assigned to a player-built anvil bench.
  - She walks there by navigation, through the player's doorway and across the cell seam, and stands at the work anchor. When released, she walks home.
  - The errand (phase, pose, route) is persisted.
  - There is no production, schedule, wage or hireling system.
- **OPTION B:** Reconcile the criterion to M10, where ROADMAP (`:312`) puts "NPC work assignment and production roles", in line with VERTICAL_SLICE's "hirelings 0". M7 then proves "navigates in, through and around" with the companion plus a test-driven mover. There is no `npc_errands` save field and no E9 slice.
- **YOUR RECOMMENDATION:** Option A.
- **WHY IT MATTERS NOW:**
  - Authority documents conflict: ROADMAP M7's exit against ROADMAP M10 and VS.
  - Option A is the single most expensive M7 item. It needs the first moving settlement NPC and its persistence.
  - Option B removes a slice but leaves the exit's "an NPC navigates" proven only by the companion.
- **WHAT CAN BE DEFERRED:** Production, schedules and assignment at scale belong to M10 under either option. If the answer is B after E2, the `npc_errands` list stays in schema 14, always empty, and costs nothing.
- **Latest decision point:** before E2 (the schema-14 slice), so no unused field is carried.

### Q4 - Where the player may build in M7

- **QUESTION:** Building is limited to one content-defined area, `build_area.hollow_crossing` (x and z 87-114 m, over the four-cell corner by the waystation road, no quest gate). Should the player instead be able to build anywhere legal in Ashen Hollow?
- **OPTION A:** One area.
  - It bounds the navigability check, since lint BLD008 proves the local check is exact within it. It also bounds the piece ceiling (256), the perf world and the tests.
  - It crosses the seams, which the RK-14 proof needs.
  - Widening it later is data.
- **OPTION B:** Anywhere legal. The navigability check must then handle unbounded regions, which means a global flood or a portal layer. Placement must protect every authored route, spawn and story site in the region, and the scripted presentation routes must be kept clear.
- **YOUR RECOMMENDATION:** Option A.
- **WHY IT MATTERS NOW:** It decides the placement-validation algorithm and the content lints (BLD007-BLD009). It also supersedes the owner-approved Ashen Hollow bible's "no player settlement building" line for M7, and WORLD_BUILDING §4's direction ("do not restrict construction to ... plots") is the long-term intent.
- **WHAT CAN BE DEFERRED:** Wider areas, plots granted by quests or factions, and "anywhere legal" belong to M9+, where jurisdiction and law exist. They add data plus a portal layer, not a rewrite.
- **Latest decision point:** before E5.

### Q5 - The working faction pair, the proof act and the gates

- **QUESTION:** Approve the M7 faction content (working names, not canon)?
- **OPTION A (as designed):**
  - **The Waystation** (Renn, Kera) and **the Survey** (Sel). Tavar is unaffiliated: he is the companion and a hired Orenth guide.
  - The proof act: the player destroys the Animated Armour in Blackvein Cut. The Waystation approves (+100: iron can be worked), and the Survey disapproves (−100: an old working destroyed before it was recorded). Steadying the Foldscar heart also raises the Survey (+100).
  - Factions learn only when the player tells a member (report-only knowledge in M7).
  - Gates at "accepted":
    - Sel's new `notes` reply (dialogue gate);
    - Kera's new iron-billet stock row (service gate, enforced in `TradeSystem`).
  - All new dialogue lines are drafts for tone review.
- **OPTION B:** A different pair or proof act. For example, a third faction for whoever turned the Quiet Stones (rejected in the design because it invents lore and proves nothing the pair cannot), or a proof act built on building (rejected because it would turn materials into standing, against E-7).
- **YOUR RECOMMENDATION:** Option A.
- **WHY IT MATTERS NOW:**
  - Faction identity, naming and tone are the owner's, and cultural naming is explicitly unsettled.
  - The proof act determines content, dialogue and the playthrough beat.
  - Report-only knowledge is a design call that is honest under ruling 3. The owner should know that NPCs do not yet "notice" deeds on their own.
- **WHAT CAN BE DEFERRED:** Final names, a third faction, the witnessed channel, faction-granted plots, and every Vessmere/Ashlings decision belong to M9.
- **Latest decision point:** before E3 (the faction slice).

### Owner process items (not design questions)

| Item | Why | When |
|---|---|---|
| Authorize M7 and name the implementing agent, worktree, branch and draft-PR convention | AGENTS.md scopes Claude "through M6" in `G:\UNNAMED_CLAUDE` on `claude/phase1`, and `docs/M6_STATUS.md:187` says M7 is not authorized | Before E0 |
| Schedule the owed RAZER 1080p/60 window. If it happens before E10 closes, add the M7 captures in §14.14; the optional `--perf-world` run is built and captured only on §14.13's condition (the owner wants the structures capture, or T2 misses its ASTRAL target). If no window is given by E10 closeout, it is recorded as owed | Phase-1 evidence still owed. M7 adds the structures and navigation load | Ideally before E10 closeout; optionally a baseline capture of `e10d2c4` first |
| R-1 tagged build | No git tags exist | At the M7 merge: tag, or waive |
| Tone review of new dialogue lines (Kera's and Sel's new replies, the gate lines, build-mode words) | All M7 text is draft | Lines ship as written unless the owner objects before E10 |
| The feel test | Deferred by the 2026-09-24 ruling; not an M7 entry blocker | Unchanged |

---
