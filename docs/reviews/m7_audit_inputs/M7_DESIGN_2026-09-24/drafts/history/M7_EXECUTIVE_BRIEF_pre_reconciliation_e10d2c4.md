# Otherreach M7 - Executive Brief

**M7:** Factions, Reputation, and Building v1 (Phase 2). **Date:** 2026-09-24. **Status:** proposed design only. Nothing in the repository was changed, and M7 is not authorized.
**Base analysed:** `origin/main` `e10d2c4`. (Local `main` in `G:\UNNAMED` is `dbb877a` = `e10d2c4` plus seven asset-audit commits that M7 does not depend on.)
**Full design:** `M7_IMPLEMENTATION_DESIGN.md` (18 sections), in this folder.

## What M7 delivers, in play

Everything happens in Ashen Hollow; Kaldrun Reach and Vessmere stay M9.

1. **Building.** The player collects timber from a stack by the crossing, presses **B**, and snaps ground pads, walls, a doorway, a door, a roof, a storage chest and an anvil bench onto a 3 m grid. The build area sits over the corner where four 100 m cells meet, so the player's workshop straddles a cell seam.
2. **Navigation.** Tavar follows the player around the new walls and opens doors. When he can no longer see his breadcrumb trail, he plans a route.
3. **A worker.** Kera Voss can be assigned to the player-built bench. She walks from the smithy, through the player's doorway and across the seam, to the bench, and walks home when released. She reroutes if a new wall blocks her way.
4. **Damage.** The player's melee blows damage pieces. Timber repairs them, and dismantling refunds half.
5. **Factions.** Destroying the Blackvein Animated Armour is one act that two factions read in opposite directions:
   - The **Waystation** (Renn, Kera) approves: the iron can now be worked.
   - The **Survey** (Sel) disapproves: an old working was destroyed before it was recorded.
   - A faction learns only when the player tells one of its members.
   - Standing opens exactly two things: Kera's iron billets and Sel's notes.
6. **Saves.** Everything survives save and load, field by field. Replays are deterministic.

## Decisions for you (five; each proceeds on the recommendation unless you say otherwise)

| # | Question | Recommendation | Decide by |
|---|---|---|---|
| Q1 | Rotation: quarter turns on a 3 m grid, or 45°/free rotation (D-08 says "free")? | **Quarter turns.** 45° means oriented collision in the movement core that every mover shares. The saved data already allows a later change. | Before slice E5 |
| Q2 | Crime, bounty and pardon (ROADMAP lists them) and territory gating: build a stub, or defer? | **Defer.** PROTOTYPE, INDEX and VS put crime in Phase 3, and nothing in the game can be a crime yet. The act log is the seam. | Before E0 |
| Q3 | "Assign an NPC to work in it": keep the literal version (Kera walks to your bench), or move it to M10, where ROADMAP puts NPC work? | **Keep the literal version.** It is the only real proof that an NPC navigates in, through and around a player structure. | Before E2 |
| Q4 | Build only in one area by the crossing, or anywhere legal? | **One area** (x and z 87-114 m). It bounds validation and tests, and it crosses the seams. Widening it later is data. It overrides the Hollow bible's "no player building" line for M7. | Before E5 |
| Q5 | Approve the working faction pair (Waystation / Survey), the armour act (+100 / −100), the two gates, and report-only knowledge? | **Approve.** The names are working names, not canon. | Before E3 |

**Process items (not design questions):**
- Authorize M7 and name the agent, worktree and branch. AGENTS.md scopes Claude only "through M6".
- Schedule the owed RAZER 1080p/60 window.
- Decide the R-1 tag at merge: tag, or waive.
- Tone-review the new dialogue lines. They are all drafts.

## The ten design calls that matter most

1. **Your two unrecorded rulings get written down first.**
   - Rulings 1 (domain navigation) and 2 (one storey) exist nowhere in the repo today.
   - Several documents still describe a Recast-style navmesh "baked per cell" that "needs the engine".
   - Slice E0 records both rulings and reconciles ROADMAP M7 before any code.
2. **Navigation is a derived integer grid inside the authority.**
   - A 250 mm lattice, one tile per 100 m cell, with windowed deterministic A*.
   - It is never saved and is rebuilt at load. A structure edit rebuilds only the nodes near the change.
   - Seams do not exist in the data: node indices are global. The straddling-structure proof (RK-14) runs headless.
   - Godot Navigation is banned outside the old perf spike.
3. **Movers keep what works.**
   - Kinematics.Step still does all collision; navigation only supplies target points.
   - Tavar keeps his breadcrumb trail and uses a planned route only when the trail fails. His M6 guarantee C16 must pass unmodified.
   - Creatures stay unpathed.
4. **One storey, stated plainly.** "Floors" are ground pads that sit on the terrain. There is no walkable elevated surface, and roofs never block walkers.
5. **Building is snap assembly with authoritative checks.**
   - Placement runs fifteen ordered checks.
   - The ghost preview runs the same validator and is advisory only.
   - Any placement that would seal off a space is refused ("rooms need a doorway").
   - Pieces are one saved row each. IDs derive from a saved sequence, so replays match.
6. **Factions never become a morality meter, and they are never psychic.**
   - Standing gates access only. An architecture test forbids combat, creature, companion, navigation and building code from reading faction state.
   - Personal trust and faction standing never touch each other.
   - Knowledge is report-only in M7. Witnessing is designed for M9, with its determinism fixes already specified.
7. **One save bump, 13 → 14, landed once.** Slice E2 migrates, freezes and fixture-tests every new field before any system writes one. There is no new save file and no baseline transition. A real M6 save is committed as historical proof.
8. **Smallest honest proofs.**
   - One damage source.
   - One authored timber stack instead of a renewable node.
   - One build area, two factions, two gates.
   - Required instrumentation only; the extras wait for a measurement.
9. **Determinism hazards found and closed during design.**
   - Item-ID order no longer decides stack counts: this is a one-line change to how `Put` merges stacks.
   - An errand NPC never reads conversation state.
   - Routes compare by value.
   - The 16,000-expansion search cap would have made Kera's real walk home unreachable. It is now 65,536.
10. **No asset work.**
    - Every M7 visual is code-built greybox. Nothing waits on DeepSeek or the asset-remediation pass.
    - Factions need no visuals.
    - There is no player faction screen: HUD lines for reported changes, plus an F6 debug panel.

## Where ROADMAP's M7 text is not followed literally

| ROADMAP says | This design | Why |
|---|---|---|
| "the navmesh updates on placement" | the domain grid updates on placement | ruling 1 |
| "floors" | ground pads, one storey | ruling 2 |
| "free rotation" | quarter turns | Q1 |
| "crime/bounty records and pardon state"; "territory gating" | deferred | Q2 |
| "assign an NPC to work in it" | one persisted errand, no production | Q3 |
| entry "NPCs and companions path reliably" | met inside M7 (slices E1 and E4), not before | no pathfinding existed |

## Plan

Eleven vertical slices. Each ends merged, green and playable.

| Slice | Delivers |
|---|---|
| E0 | Rulings and reconciliation written; M7_STATUS created; the M6 save committed |
| E1 | Navigation grid, planner, F2 overlay (saves nothing) |
| E2 | Schema 14, landed once |
| E3 | Factions v1 |
| E4 | Companion routes and door opening |
| E5 | Build mode: pads, walls, doorways, roofs, timber |
| E6 | Piece doors |
| E7 | Navigability check |
| E8 | Chest, bench, damage, repair |
| E9 | Kera works at the player's bench |
| E10 | Evidence and closeout |

- **Critical path:** E0 → E1 → E2 → E4 → E5 → E6 → E7 → E8 → E9 → E10. E3 can run alongside E4-E9.
- **Acceptance:** 37 criteria, each decided by a named test or run and mapped to a ROADMAP clause or ruling.

## Risks to watch

- **Scope creep.** ROADMAP still names more than M7 builds.
- **A companion regression** from route mode and door opening.
- **Planning spikes.**
  - Kera's walk-home plan is about 4.6 ms on ASTRAL, just over the 4 ms tick budget. It is accepted as a single-tick residue.
  - A per-tick plan budget is the lever if the RAZER window shows a hitch.
- **The RAZER window is still owed**, so M7's cost lands on an unmeasured baseline.
- **Pressure for more than one storey** after playtest. The answer stays "more pieces, not physics" until an owner decision says otherwise.

## Supporting material

- `research/`: ten cited evidence notes.
- `drafts/`: scope and audit rulings, the part designs, the reconciliation register D1-D38, and the panel candidates.
- `drafts/audit/`: eight audits.
