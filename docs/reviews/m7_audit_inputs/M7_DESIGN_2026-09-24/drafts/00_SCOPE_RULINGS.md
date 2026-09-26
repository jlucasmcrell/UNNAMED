# M7 design - scope rulings from the design lead (input to every design agent)

Status: working input for the M7 design team, 2026-09-24. This is not an owner ruling. Where it says "OWNER QUESTION", the design proceeds on the stated default and records the question for the owner.

## 0. Base and sources

- Canonical code/doc base: **origin/main `e10d2c4`** ("Merge pull request #5 ... phase1/closeout-followup"). The local `main` in `G:\UNNAMED` has moved to `dbb877a`, which is `e10d2c4` plus 7 asset-audit commits (one doc and seven `tools/asset_pipeline/_*.py` scripts). They do not affect M7.
- Read-only snapshot of `e10d2c4`: `C:/Users/jluca/AppData/Local/Temp/claude/G--UNNAMED/bfa8b7ba-4180-4f2e-915c-4c2bd4993f45/scratchpad/main_e10d2c4`. Cite repo-relative `path:line`.
- Research notes (cited evidence; read the ones your topic needs, in full): `G:/UNNAMED_HISTORY/M7_DESIGN_2026-09-24/research/{authority,building_docs,faction_docs,world_lore,sim_core,spatial_movement,persistence,content_registry,social_quests_code,presentation_perf}.md`.
- Never modify anything under `G:/UNNAMED`, `G:/UNNAMED_CLAUDE` or `G:/UNNAMED_INTEGRATION`. Run no git command that changes state. Write production code nowhere. Pseudocode, schemas and YAML examples inside your design document are expected.

## 1. The authority position

- ROADMAP M7 (`docs/ROADMAP.md:278-286`) is the scope text. It predates the owner's 2026-09-24 rulings and was never reconciled.
- Two rulings are recorded nowhere in the repo: ruling 1 (domain-side deterministic navigation, Godot Navigation not authoritative) and ruling 2 (one storey). `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:33` says an owner ruling beats conflicting text until the owning milestone writes it in. **M7's first slice therefore writes rulings 1 and 2 into the normative docs**: WORLD_ARCHITECTURE §5/§10/§11, RK-14 and RK-A2 wording, the PERSISTENCE §2 not-saved table, and the ROADMAP M7 entry.
- Hierarchy (PHASE_0_COMPLETE §1): Charter > DECISIONS > rank-3 specialists (ARCHITECTURE, DATA_MODEL, PERSISTENCE, WORLD_ARCHITECTURE, SYSTEMS, PROGRESSION) > PROTOTYPE / ROADMAP / VERTICAL_SLICE / GAMEPLAY_LOOPS > RISK_REGISTER. Design-extension docs (CRIME_LAW..., WORLD_BUILDING...) are directional until M7 reconciles them. Handoffs are orientation only.
- VERTICAL_SLICE describes the M9 slice region (Kaldrun Reach / Vessmere), not M7. M7 is built and proven in **Ashen Hollow** (region `r_0_0`, 200 x 200 m, four 100 m cells). It must be reusable by M9 without rework. Do not build Kaldrun Reach, Vessmere or the VS factions in M7.

## 2. How each conflict resolves (design-lead defaults)

| # | ROADMAP M7 text | Conflicting authority / ruling | Default in this design |
|---|---|---|---|
| 1 | "the navmesh updates on placement"; "navmesh path test" | Ruling 1; WA §11 lists a Recast navmesh in the presentation/streaming table; RK-14 "needs the engine" | Read "navmesh" as **the domain navigation representation**. It is derived from authoritative state and updated deterministically on placement. RK-14's seam test becomes a headless domain test plus a runtime recording. |
| 2 | "foundations, walls, floors, roofs, doors" | Ruling 2; the body cannot stand on anything (`Kinematics.cs:159`) | One storey. A "floor"/"foundation" is a **ground-level pad**: gameplay stays terrain-bound and the pad is visual plus a placement anchor. No walkable elevated surface. Roofs have no gameplay collision for walkers. If a designer believes a flat walkable floor is unavoidable, they must state the architectural consequences instead of hiding them. |
| 3 | "free rotation and socketing" | D-08 also says "free rotation/socketing" (rank 2). VS says 45° steps. Code has axis-aligned boxes and circles only (`Blockers.cs:40-41`); presentation fitting turns models by 90° only | **Default: 90° rotation steps**, which keep every piece an axis-aligned box and leave `Kinematics.Step`, combat traces and prediction untouched. Keep the definition shape open to 45°/oriented footprints later. **OWNER QUESTION** (D-08 wording). |
| 4 | "crime/bounty records and pardon state (S-27)" | PROTOTYPE:40 and INDEX:89 put crime in Phase 3; VS:336 makes it a non-goal; ruling 3 requires legal status to stay separate; today nothing can be committed (NPCs cannot be harmed and containers have no owner) | **Default: defer crime, bounty and pardon beyond M7.** M7 preserves the seam: legal status is a named, separate, unbuilt layer, and nothing in M7 derives legal status or hostility from standing. **OWNER QUESTION.** |
| 5 | "assign an NPC to work in it" (exit) | VS hirelings 0; ROADMAP M10 owns "NPC work assignment and production roles"; NPC bodies are not saved and NPCs never move today | **Default: the smallest literal proof.** One persistent assignment of one existing settlement NPC to one work anchor on a player-placed functional piece (the station). The NPC walks there by navigation, stays there, and walks back when unassigned. No production, schedules, wages or hireling system. This forces NPC position, assignment and route into the save. **OWNER QUESTION** (alternative: move it to M10 and prove navigation with the companion plus a test-only NPC). |
| 6 | "including a structure straddling a cell boundary" (exit) | VS keeps its plot inside one cell; RK-14 permits constraining placement until stitching is proven | Straddling is **required and proven** in M7: navigation design makes seams a non-case. Player placement is allowed across seams in the permitted area. |
| 7 | (silent on where the player may build) | VS: one quest-granted plot; WORLD_BUILDING §4: do not restrict to plots | **Default: a content-defined build area (data) in Ashen Hollow, placed so it crosses a cell seam near the waystation**, with no quest gate. Widening it later is data. **OWNER QUESTION.** |
| 8 | "per-piece health ... damage/repair works and is explicit rather than emergent" | GAMEPLAY_LOOPS wants property threats plus a frequency option in the phase building ships; ROADMAP M10 owns home-defense threats; off-screen raids are tier-illegal (review C-8) | Build per-piece health, `DamagePiece` applied only by an explicit rule table, `RepairPiece` with materials, and destruction at 0 health (same removal path as dismantle). **No raids and no home-defense system.** The M7 damage sources must be named and explicit; designers propose the smallest set that makes damage reachable in play without inventing threats. |
| 9 | "storage containers; one crafting station as a placeable" | VS is internally inconsistent about stations | In scope as **one storage chest piece** (reuses the container model) and **one crafting-station piece** (reuses an existing M3f station kind and recipes). No new recipes beyond the building pieces' own costs. |
| 10 | "service, dialogue and territory gating derived from standing" | PROGRESSION: reputation gates access only, never power or hostility | Dialogue gating (a standing-tier condition) and **one** service gate are in scope. Territory gating is optional-if-cheap, and only as an access check. It must never feed hostility. |
| 11 | "cross-faction attitude relations" | ruling 3 separation | Static faction-to-faction relation data (content). No runtime war-state changes in M7. Faction relation must not automatically move standing. |
| 12 | Reputation ladder | VS 5 tiers incl. "Hostile"; PROGRESSION 11 (-5..+5, rank 3); DATA_MODEL example 4 | **PROGRESSION §10 governs** (rank 3, the specialist). Tier names contain no hostility word. Designers decide stored representation (raw integer points plus derived tier is the default). |
| 13 | Materials | "Depends on M3f (materials)" | Placement consumes items through the existing inventory transaction patterns. Repair consumes materials. Dismantle may refund part. No new economy. |
| 14 | Entry: "NPCs and companions path reliably" | No pathfinding exists today | M7's navigation slice satisfies it first. Record honestly that the entry criterion is met inside M7, not before it. |

## 3. Code realities every design must respect (verify in source before relying on them)

- The live runtime is `src/World/Runtime`. `Simulation` holds a FIFO command queue applied at 20 Hz tick boundaries. Systems own `StateSlice` values, and a slice with no owner or two owners is a boot error. Events go to presentation and tests only; no system subscribes. Cross-system work is a **synchronous internal command** (the `RecordDeed` pattern). The M1 scaffold (`ISystem.Configure`, `IWorldStateWriter`, `CommandBus`) is used by Domain.Tests only.
- `Kinematics.Step` is the only movement function, shared with presentation prediction. Navigation must emit `MoveIntent`s and leave collision inside `Kinematics.Step`. Positions are integer mm. `WalkSpace` is one region-wide space built once from content, and cells do not partition movement. Doors and barriers are flag-gated dynamic blockers, re-read on every query. No API exists to add or remove a structure at run time.
- Determinism is tested: command-log replay equals the state digest, and save-then-continue equals continue (the companion trail and stuck counter were persisted for exactly this reason). Anything that affects the next tick must be persisted or be a pure function of persisted state. `Math.Sin`/`Cos` already leak into authoritative paths; the new navigation code must not add float-order dependence.
- Instance IDs: `EntityId.NewId` uses the wall clock. NPC and creature IDs are hash-derived. Designers must say how placed-structure IDs stay compatible with replay and digest tests. Check how created item stacks do it today.
- Persistence: schema 13, four section files. A new section file breaks every older fixture's integrity root (research/persistence.md gap 1). There is no global (non-cell) world record today. `SaveLoader.ResolveDefinitions` and `SemanticRebase` list `DeltaSnapshot` properties by hand. `CanonicalState`, `PlayerRecord.Digest` and `EffectiveCellDigest` are hand-written.
- Content: `faction` is already a registered kind (`factions/`, `faction_ref`); structure pieces have no kind yet. `LoadAll_Loads_Yaml_Files` asserts the exact ID list. The linter enforces less than DATA_MODEL claims.
- Presentation reads immutable `Simulation` views each frame, submits `GameCommand`s through `GameSession.Submit`, and the `Simulation` method surface is allow-listed by an architecture test. `Simulation.Aim` is the precedent for a read-only query that runs the authority's own function, which is the model for a placement-preview query. Structures are greybox primitives built once at boot, and there is no runtime add/remove path in presentation.

## 4. Scope categories (default)

REQUIRED FOR M7
- Rulings 1 and 2 recorded; M7 conflict reconciliation written into ROADMAP M7 and the owning docs.
- Domain navigation v1: derived, deterministic, headless, seam-proof, updated on structure edits; used by the companion and the assigned NPC.
- Building v1: socket/snap, one storey, ground pads, walls, doorway/door (authoritative open/closed), roof, storage chest, one crafting station; materials consumed; ownership; per-piece health, explicit damage, repair, dismantle, destruction; placement validation (overlap, sockets, terrain tolerance, build area, authored-geometry and spawn protection, navigability); sparse-delta persistence; straddling-seam structure.
- One NPC work-anchor assignment (default per item 5).
- Factions v1: two working factions (a third only if it proves something the two cannot); definitions; static membership; static cross-faction relations; per-faction player standing with the PROGRESSION ladder; act records; data-defined per-faction reaction tables; knowledge-gated application (no psychic factions); one dialogue gate and one service gate; a reputation fixture table in which the same act moves two factions in opposite directions.
- Save schema 14 with migration, fixture and field-by-field evidence; deterministic replay; save/load continuity.
- A playable build with a greybox building kit and debug views sufficient to prove all of the above.

OPTIONAL IF CHEAP
- Creature return-home or leash using navigation (charges and lunges stay un-pathed).
- Territory gating as an access check.
- Additional greybox piece variants (half wall, window wall).

DEFERRED BEYOND M7 (non-goals)
- Upper floors, stairs, ladders, lifts, climbing, walkable elevated surfaces, terrain flattening, voxel or structural physics, oriented free rotation (if the owner keeps the 90° default).
- NPC schedules, production, wages, hirelings, settlement simulation, home-defense raids and the attack-frequency option (record the GAMEPLAY_LOOPS conflict), off-screen damage.
- Crime, bounty, pardon, legal status, witnesses as testimony, evidence, disguise, jurisdiction law, guards (default per item 4).
- Rumor networks, in-flight report travel, faction-to-faction war-state changes, standing decay, joining factions, per-NPC memory log, NPC-to-NPC relationships.
- Godot Navigation in any authoritative role, networking, the production building UI, the modular art kit, and the Kaldrun Reach content.

## 5. Provisional owner questions (the final list is at most 5)

1. Rotation granularity: 90° (default) or 45°/free (oriented footprints).
2. Crime/bounty/pardon: defer (default) or a minimal stub in M7.
3. "Assign an NPC to work in it": minimal work-anchor assignment (default) or reconcile to M10.
4. Where the player may build in M7: one content-defined build area crossing a seam (default) or anywhere legal.
5. Reserved for a faction question the faction design finds genuinely open (for example, approval of the working faction pair), or none.
