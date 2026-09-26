## 1. Authoritative M7 scope

### Decisions

- M7 is built and proven in **Ashen Hollow**: region `r_0_0`, 200 x 200 m, four 100 m cells. The vertical slice's Kaldrun Reach, Vessmere and VS factions are M9 content and are not built here. Everything M7 builds must be reusable by M9 without rework.
- ROADMAP's M7 entry is the scope text. It predates the owner's 2026-09-24 rulings. Where the two conflict, the ruling wins until M7's first slice writes it into the normative documents (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:33`).
- Owner rulings 1 (domain navigation, never Godot Navigation) and 2 (one storey) are **recorded nowhere** in the repository, in the V4 handoff or in the history reports. Slice E0 records both.
- The M7 exit's "navmesh" means **the domain navigation grid** of section 3: derived, deterministic, headless, updated on placement.
- Crime, bounty, pardon and territory gating are **deferred** (default; owner question Q2). The act log and gate-access sets are their seams.
- Faction knowledge in M7 is **report-only**. The witnessed channel is specified for M9 and not built (section 5).

### 1.1 The ROADMAP M7 entry, verbatim (`docs/ROADMAP.md:278-286`)

> ### M7 — Factions, Reputation, and Building v1 — `FEATURE` — **PHASE 2**
>
> - **Class:** FEATURE. **Depends on:** M4 (NPC persistence), M3f (materials). **Phase:** **2** — this milestone is Phase-2 work; an earlier draft of this roadmap placed it in Phase 1, which contradicted `PROTOTYPE.md`, `GAMEPLAY_LOOPS.md` §15 ("**BUILD** — **Not in Phase 1**"), and `SYSTEMS.md` §3 (S-27 Factions and S-32 Buildings are both Phase 2). `RISK_REGISTER.md` declares Phase 1 *failed* if any system outside `PROTOTYPE.md`'s minimum list is built, so building this in Phase 1 would have failed the phase by the register's own criterion.
> - **Entry:** NPCs and companions path reliably — D-08's whole justification for snap-based building is that structures must stay navigable and persist reliably.
> - **Work (factions/reputation, moved here from M4):** faction definitions and membership graph; per-faction player reputation with directional, per-faction reactions and **no universal morality meter** (Charter §20); cross-faction attitude relations; crime/bounty records and pardon state (`S-27`); service, dialogue and territory gating derived from standing. Reputation answers *access* and never converts into character power (`D-09`).
> - **Work (building):** Socket/snap placement; foundations, walls, floors, roofs, doors; free rotation and socketing; ownership; per-piece health applied by explicit rules (**no structural simulation, no physics collapse** — D-08); repair; storage containers; one crafting station as a placeable; player-built structure persistence as a sparse world delta (D-05); placement validation that rejects un-navigable or overlapping configurations.
> - **Exit criteria:** Build a structure, assign an NPC to work in it, verify the NPC navigates in, through, and around it — **including a structure straddling a cell boundary**, which is `RK-14`'s explicitly unsolved case and must be proven here rather than discovered in playtest; save/load preserves every piece with correct ownership and health; damage/repair works and is explicit rather than emergent; the navmesh updates on placement; the same act moves two factions in opposite directions in a fixture.
> - **Proof:** Playable build; navmesh path test including the straddling-seam case; structure round-trip through save; a reputation fixture table.
> - **Notes:** Less creative freedom than voxel building is the accepted cost of guaranteed navigability and clean persistence (D-08). If playtest says expression is limited, the answer is **more pieces, not physics**.

ROADMAP rules that bind M7:

- R-1: playable throughout; merged, tagged and smoke-green at each milestone.
- R-2: vertical, not horizontal.
- R-3: dependency order.
- R-6: no networking; only the four D-12 extension points.
- The dependency note at `:93`: "Snap building guarantees navigability; navigability is only testable once NPCs and companions actually path (D-08)".
- M10 (`:312`) owns "NPC work assignment and production roles" and home-defense threats.
- M9 (`:303`) owns the slice's "2–3 factions with mutually constraining reputation".

### 1.2 Authority order used by this design

- `PHASE_0_COMPLETE.md` §1: Charter > DECISIONS > rank-3 specialists (ARCHITECTURE, DATA_MODEL, PERSISTENCE, WORLD_ARCHITECTURE, SYSTEMS, PROGRESSION) > PROTOTYPE / ROADMAP / VERTICAL_SLICE / GAMEPLAY_LOOPS > RISK_REGISTER. Within a rank, the more specialised document wins.
- Design-extension documents (`CRIME_LAW_REPUTATION_AND_JUSTICE.md`, `WORLD_BUILDING_AND_PROPERTY_DESIGN.md`, both owned by M7 per `docs/INDEX.md:89-90`) are directional until M7 reconciles them. Handoffs are orientation only.
- ROADMAP governs **when**; PROTOTYPE governs **what is in Phase 1** (`docs/ROADMAP.md:447`). For Phase-2 content the governing documents are ROADMAP M7 (scope), SYSTEMS S-27/S-32 and PROGRESSION §10 (mechanics), D-08/D-09 (decisions) and the owner's rulings.

Where each owner ruling is recorded today:

| Ruling | Recorded at | M7 action |
|---|---|---|
| 1 Navigation: domain, deterministic, headless; Godot Navigation never authoritative | **Nowhere.** Supporting principles only: navigation and pathfinding are derived state (`docs/PERSISTENCE.md:83-84`, `docs/ARCHITECTURE.md:164`) | E0 writes a dated DECISIONS entry and amends WORLD_ARCHITECTURE §5/§10/§11/RK-A2, RISK_REGISTER RK-14/RK-06, PERSISTENCE §2 and SYSTEMS S-25/S-32 |
| 2 One storey | **Nowhere** | E0 writes a dated DECISIONS entry and amends ROADMAP M7 ("floors" means ground pads) |
| 3 Faction separation, no morality meter, no psychic knowledge | `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:548-564`; `docs/PROGRESSION.md:444-452`; D-09 as amended (`docs/DECISIONS.md:209`) | Enforced by the section 5 rules and architecture tests |
| 4 C10 retired | `docs/PROTOTYPE.md:298` (the untracked V4 handoff §18 is stale) | None |
| 5 No required radial | `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:645` | Section 8 uses direct keys only; E0 softens `docs/HUD_INPUT_AND_ACTIONS.md:36` |
| 6 No networking | D-02, D-12, R-6 | None |
| 7 Engine-independent authority; dotted IDs; ULID instance IDs; sparse deltas | D-02, D-04, D-05, D-11 | E0 adds a dated D-04 note on derived ULID-shaped IDs (section 4) |

### 1.3 Conflicts, and how this design resolves each

| # | Text | Conflicting authority or ruling | Resolution in this design |
|---|---|---|---|
| 1 | ROADMAP "the navmesh updates on placement"; "navmesh path test" | Ruling 1. WA §11 (`:435`) lists a Recast navmesh "baked per cell" in the presentation/streaming table. RK-14 (`docs/RISK_REGISTER.md:290`) says the test "needs the engine". The only navmesh in code is the Godot bake in the presentation-only spike (`src/Presentation/Spike/SpikeScene.cs:159-179`) | "Navmesh" means the domain navigation grid (section 3). It is updated synchronously by `RebuildNavigation` on every placement, dismantle or destroy that changes a blocking footprint. RK-14 is proven headless plus a recording. A guard test bans Godot navigation types outside the spike |
| 2 | ROADMAP "foundations, walls, floors, roofs, doors" | Ruling 2. The body cannot stand on anything (`src/Domain/Spatial/Kinematics.cs:159`) | One storey. A floor or foundation is a **ground pad**: a visual placement anchor with a terrain-relief gate. Gameplay stays terrain-bound, and there is no walkable elevated surface. Roofs never block walkers |
| 3 | ROADMAP and D-08 "free rotation and socketing" | VS: 45° steps. Code: axis-aligned boxes and circles only (`src/Domain/Spatial/Blockers.cs:40-41`) | Quarter turns on a 3 m lattice (default; **Q1**). Pieces stay axis-aligned boxes, and `Kinematics.Step`, combat traces and prediction are untouched. The data shape leaves room for 45° later |
| 4 | ROADMAP "crime/bounty records and pardon state (S-27)" | PROTOTYPE `:40` and INDEX `:89` put crime in Phase 3; VS `:336` makes it a non-goal. Ruling 3 keeps legal status separate. Nothing can be committed today: NPCs cannot be harmed and containers have no owner | Deferred beyond M7 (default; **Q2**). The act log and its string-keyed source/identity fields are the seam. Nothing in M7 derives legal status or hostility |
| 5 | ROADMAP "service, dialogue and territory gating derived from standing" | PROGRESSION §10: access only | One dialogue gate and one service gate are built. Territory gating is deferred with crime (**Q2**) |
| 6 | ROADMAP exit "assign an NPC to work in it" | VS hirelings 0 (`:97`). ROADMAP M10 (`:312`) owns NPC work assignment. NPC bodies are not saved today | The smallest literal proof (default; **Q3**): Kera Voss is assigned to a player-built anvil bench as a persisted errand. She walks there and back by navigation. No production, schedules or wages |
| 7 | ROADMAP exit "structure straddling a cell boundary" | VS keeps its plot inside one cell (`:161`). RK-14 permits constraining placement | Required and proven. The build area sits over the four-cell corner at (100, 100), and the acceptance structure straddles a seam with its interior route crossing it |
| 8 | Where the player may build (ROADMAP silent) | VS: one quest-granted plot. WORLD_BUILDING §4: do not restrict to plots | One content-defined area, `build_area.hollow_crossing` (x and z 87-114 m), with no quest gate (default; **Q4**). Widening it later is data |
| 9 | "per-piece health ... damage/repair works and is explicit" | GAMEPLAY_LOOPS wants property threats plus a frequency option in the phase building ships; ROADMAP M10 owns home defense; off-screen raids are tier-illegal | Per-piece health, **one** explicit damage rule (the player's melee strike on a piece), repair with materials, destruction at 0 health. No raids and no threat system. The GAMEPLAY_LOOPS conflict is recorded in section 16 |
| 10 | "storage containers; one crafting station as a placeable" | VS is inconsistent about stations | One storage-chest piece (reuses the container record) and one anvil-bench piece (reuses the M3f station model) |
| 11 | "cross-faction attitude relations" | Ruling 3 | Static, directional relation words in content, read by views only. They never move standing, and there is no war-state change in M7 |
| 12 | Reputation ladder: VS 5 tiers incl. "Hostile"; PROGRESSION 11 (-5..+5); DATA_MODEL example 4 | PROGRESSION is rank 3 and the specialist | PROGRESSION §10's eleven tiers. Tiers are derived from stored points and never stored, and no tier name implies an attack order |
| 13 | VS §5.6: backing a faction "puts town guards on you" | D-09 as amended ("access only; never hostility"); PROGRESSION §10; ruling 3 | Excluded. Standing never decides who attacks, enforced by an architecture test (section 5) |
| 14 | DATA_MODEL §4.13 FactionDefinition fuses `laws`, `guards_hostile` and `enemy_of` into the faction | Ruling 3; S-27 `:309` | Those fields are refused by lint in M7. Law belongs to places and institutions (CRIME_LAW `:20`) and arrives with crime |
| 15 | ROADMAP entry "NPCs and companions path reliably" | No pathfinding exists: creatures steer straight and slide, NPCs never move, and the companion follows a breadcrumb trail with a teleport safety net | The entry criterion is met **inside** M7 by slices E1 (grid and planner) and E4 (companion route mode), before any building slice. This is recorded honestly in M7_STATUS |
| 16 | PROTOTYPE A-2: "`faction_id` field exists on NPCs and is saved" | No such field exists in code or saves | Corrected in E0. M7 adds NPC `faction_ref` as static content, not saved |
| 17 | Feel test: "not an M7 entry blocker" (ROADMAP `:274`, owner ruling) | IMPL_PRECEDENCE `:156,:321`, the content bible and the V3/V4 handoffs still say "before Phase 2" | The owner ruling stands. E0 amends IMPL_PRECEDENCE to cite it |
| 18 | RAZER 1080p/60 evidence "before Phase 2" (bible `:1047-1070`; PROTOTYPE `:384`) | ROADMAP M7 entry is silent; the window is still owed (`docs/M6_STATUS.md:221`) | Not a design blocker. Listed as an owner process item. Section 14 says what M7 adds to that capture |
| 19 | ROADMAP `:422` "No other milestone requires owner input" | The stop-after-M6 ruling (IMPL_PRECEDENCE `:315-323`; `docs/M6_STATUS.md:187`) | M7 needs owner authorization and a named agent, worktree and branch (AGENTS.md scopes Claude "through M6"). Owner process item |
| 20 | Ruling 3 lists "directly witnessed" among possible knowledge sources | Shipped Ashen Hollow cannot exercise witnessing (members stand inside buildings or far from any relevant act), and the channel carried two audit-found hazards | M7 is report-only. Witnessing is specified as the M9 extension, with its fixes, and the stored knowledge rows already carry the fields it needs |

### 1.4 REQUIRED FOR M7

1. **Document reconciliation (E0).** Rulings 1 and 2 recorded as DECISIONS entries. ROADMAP M7 amended to this scope. WORLD_ARCHITECTURE, RISK_REGISTER, PERSISTENCE §2, SYSTEMS, GAMEPLAY_LOOPS, the Ashen Hollow content bible, PROTOTYPE A-2, HUD_INPUT and IMPL_PRECEDENCE amended where they conflict; DATA_MODEL changes land as built in E2, E3 and E10. A dated D-04 note on derived IDs. `docs/M7_STATUS.md` created.
2. **Navigation v1 (section 3).**
   - A derived 250 mm integer lattice, stored one tile per 100 m cell.
   - Deterministic windowed A* with string pulling.
   - Routes persisted with their movers.
   - `RebuildNavigation` on structure edits.
   - The companion's route mode, used as the fallback when his trail fails; he opens doors.
   - The errand mover.
   - One navigability rule for placement.
   - A headless seam proof and a runtime recording.
3. **Building v1 (section 4).**
   - Seven greybox pieces: pad, wall, doorway, door, roof, storage chest, anvil bench.
   - A 3 m lattice with quarter turns and deterministic snapping.
   - Ordered placement checks with an advisory preview that matches the command.
   - Ownership; per-piece health, one damage rule, repair, dismantle and destroy.
   - Authoritative piece doors.
   - The build area over the four-cell corner.
   - The Crossing Workshop, a structure that straddles a seam.
   - Materials (timber) consumed through existing inventory transactions, from an authored timber stack.
4. **NPC work assignment (default Q3).** Kera Voss is assigned to the player's anvil bench and released from it. The errand is persisted.
5. **Factions v1 (section 5).**
   - Two working factions, the Waystation and the Survey.
   - Static membership and relations.
   - An act log with two built act kinds.
   - Data-defined per-faction reactions.
   - Report-only knowledge.
   - The PROGRESSION ladder.
   - One dialogue gate and one service gate.
   - The reputation fixture table, in which the same act moves two factions in opposite directions.
6. **Persistence (section 7).**
   - One schema bump, 13 to 14, with its migration step, frozen V13 shapes and a v14 fixture.
   - A committed real M6 save.
   - Field-by-field StateDump evidence, deterministic replay, and save-then-continue equality.
7. **Presentation (sections 8 and 9).**
   - Build mode on direct keys with no radial.
   - A greybox `StructuresView` and an advisory ghost.
   - F2 structure/navigation and F6 faction debug views.
   - HUD lines for reported reputation changes.
8. **Runtime proof (section 13).** A `--build-shots` scripted run with verify and replay, faction beats in `--playthrough`, and a StateDump comparison.
9. **Required instrumentation (section 14).**
   - `NavCounters`.
   - FrameStats `sim_ms`, `ticks`, `save_ms`.
   - Headless budget tests.
   - The RK-06 200-piece round trip.
   - Lint BLD009.

### 1.5 OPTIONAL IF CHEAP

- Instrumentation beyond the required set: `--perf-world` and its generator, the generated cost table, building and faction counters, extra FrameStats columns, and the companion soak (owed M6/RK-05 work). Each is built only when a measurement calls for it, or for the owner's RAZER window. Section 14 gives the switch-on conditions.
- Additional greybox piece variants, such as a half wall or window wall, only if they are pure content that needs no new mechanism. None is planned.

Everything the scope rulings once listed as optional was decided during design and is recorded in section 16:
- Creature return-home by navigation: declined. It would reopen `CreatureDto` and the behaviour matrix.
- Territory gating: deferred with crime (Q2).

### 1.6 DEFERRED BEYOND M7

Section 16 lists every deferred item once, with the milestone that owns it. In short:

- **Building:** vertical building of any kind; oriented or 45° rotation (unless Q1 says otherwise).
- **Crime and law:** crime, bounty, pardon, legal status, jurisdiction, guards, territory gating.
- **Knowledge:** witnessed knowledge, rumor, in-flight reports, war-state changes, standing decay, joining factions, memory logs.
- **Settlement:** NPC schedules, production, hirelings, settlement simulation, raids and the attack-frequency option.
- **Damage and resources:** damage sources beyond player melee; renewable timber.
- **Navigation:** creature pathing and extra radius classes.
- **Presentation:** the production building UI, the art kit, a player faction screen.
- **Never in M7:** Godot navigation in any authoritative role, networking, and Kaldrun Reach content.

### 1.7 M7 entry status (at `e10d2c4`)

| Entry condition | Status |
|---|---|
| M4 (NPC persistence) and M3f (materials) complete | Met (`docs/M4_STATUS.md`, `docs/M3F_STATUS.md`) |
| "NPCs and companions path reliably" | Not met before M7. Met inside M7 by E1 and E4 (row 15 above) |
| Owner authorization to start M7 | **Not given** (`docs/M6_STATUS.md:187`). Owner process item |
| Feel test | Waived as an entry blocker by owner ruling (`docs/ROADMAP.md:274`) |
| RAZER 1080p/60 window | Still owed (`docs/M6_STATUS.md:221`). Owner process item; not a design blocker |
| R-1 tagged build | No git tags exist. The owner tags at merge, or waives the rule |

---
