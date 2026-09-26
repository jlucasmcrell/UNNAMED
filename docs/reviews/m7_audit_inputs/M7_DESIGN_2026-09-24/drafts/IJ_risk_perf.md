# M7 Parts I and J - Risk register and performance budget

Status: the risk and performance analyst's parts for the M7 design team, 2026-09-24. Design and implementation planning only; not an owner ruling. Base: origin/main `e10d2c4`, read in the read-only snapshot `main_e10d2c4`. Every `path:line` is repo-relative and was re-read there for this document. Inputs: `drafts/00_SCOPE_RULINGS.md`; Parts A, B and C; and Part D (`drafts/D_cross_system.md`), whose register (D1-D38) and guards (G1-G19) override A, B and C wherever they differ. This document follows D and raises no challenge to a scope ruling.

Number labels:
- **[computed]**: arithmetic from constants verified in source or content.
- **[model]**: A's scratch-model counts (A Appendix C).
- **[est]**: an estimate that a named test replaces with a measurement.
- **[measured]**: a number already in the repository's evidence.

Nothing M7-related has been measured, and nothing at all has been measured on RAZER (`docs/M6_STATUS.md:221`).

---

## Part I - M7 risk register

### I.1 How to read the table

- **L (likelihood)** is the chance that the risk reaches a merged M7 build or the owner's playtest. It assumes the design's mitigations are in place but not yet proven by implementation.
- **I (impact)** is the consequence if it does.
- Both are low, medium or high. "High" appears only where the row says why.
- **Test/proof** names the test (by its design name) whose failure would expose the risk. Names marked **(J)** are new here and specified in J.11.
- **RK** is the `docs/RISK_REGISTER.md` entry the row maps to:
  - "update" means I.3 proposes new text;
  - "local" means the risk is kept in `docs/M7_STATUS.md` and not promoted.

### I.2 The register

**A. Navigation**

| ID | Risk | L | I | Mitigation | Test / proof | RK |
|---|---|---|---|---|---|---|
| R-A1 | **Deterministic navigation is a large new integer module** (lattice, probes, A*, string pulling, follower, edit check). A soundness or tie-break bug makes movers stall or clip corners | medium | medium: movers stall, but Phase-1 fallbacks catch the companion and the NPC's view shows "Blocked" | A's derived 50 mm margin (C1) and exact `Int128` swept circle; one `StampRect` for build and rebuild; pure Domain functions tested without content; A's model counts as the oracle | N-D1..N-D19, especially N-D15 (`PointClear == Separation is null`), N-D16 (every lattice edge walkable), N-D11 (rectangle rebuild equals full build over 200 edits); N-X1 | RK-05, RK-06 (update) |
| R-A2 | **Cell seams**: a route found on one side fails on the other, exits and re-enters the structure, or differs after a reload | low: seams are not in the data. 100,000 / 250 = 400, so they fall between global node indices (A §6) | high: this is the ROADMAP exit and RK-14's named case | Each tile is a pure function of its key and the global inputs; tile keys are pinned against `CellKey.OfWorld`, including the negative region | N-D9 (24 build orders, evict and rebuild, a monolithic reference, 50 cross-seam pairs), N-D10, N-A2 on B's Crossing Workshop (D24), N-A7, N-W1 | RK-14 (update), WA RK-A2 |
| R-A3 | **Navigation rebuild cost** after an edit | low | low | A rectangle of ≤ 133 nodes per piece, ≤ 4 tiles (J.3); never a whole cell; door toggles rebuild nothing | N-A10 (< 2 ms); cost table T1 (J) | RK-06 (update) |
| R-A4 | **Pathfinding nondeterminism**: heap ties, hash-order iteration, ULIDs in stamps or tie-breaks, counters or scratch read by a decision, or float in navigation | low | high: replay and save-then-continue diverge, and digest proofs fail or, worse, pass hollowly | Total order `(f, h, idx)` on its own heap (no `PriorityQueue`); stamps hash geometry, never IDs; geometric door tie-break; A §4's invariant; lazily allocated scratch whose contents never matter (D36) | N-D12, N-D17, N-A6, N-A8, G15, G16/N-X1, G17/N-W3 | RK-09, RK-05 |
| R-A5 | **Companion regression (C16 and the M6 guarantees)** from Route mode, route resets, the `PersonObstacles` move, door opening, or the `SightWalls()` switch | medium: the companion is the most-edited Phase-1 system | high: RK-05 is a pillar, and these are M6 exit proofs | Route mode only when neither the character nor a trail mark is in clear view; hysteresis; snag and catch-up kept as the safety net | Unmodified: `ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds` (`tests/Application.Tests/CompanionTests.cs:119`), `FollowWaitFollow_...` (`:87`), `LeftFarBehind_...` (`:179`). `HisState_RoundTrips...` (`:285`) gains `Route`. N-A11, N-A12. **Gap:** A's N-A14 cites "the M6 random-walk soak", which exists in neither `tests/` nor `docs/M6_STATUS.md` (grep); T9 (J) writes it | RK-05 (update) |
| R-A6 | **An errand NPC stalls for good**: the anchor is unreachable, a foreign-owned piece door blocks the way (D34), or a body stands in a doorway. The NPC never teleports | low | low: visible and fixed by release | Refusal 10 via `Reachable` (D35); a retry every 40 ticks; the `StuckTicks`-driven "Blocked" view (D28); BLD008 keeps the area from cutting anything outside it | N-A5; B's errand tests; D28 | local |
| R-A7 | **Creature steering against placed walls.** Creatures never path (A §1), so a chaser slides along a wall and loses the player behind the workshop. A boar charging a player wall is stunned (`src/World/Runtime/Creatures.cs:588-594`), which is consistent but farmable. A creature can walk through an open door and be shut in (V-N1 treats every gate as passable) | medium | low: odd behaviour, no corruption; GAMEPLAY_LOOPS E-4's XP decay bounds farming | Spawner protection (B §16.3, BLD007); creature `Populate` never sees pieces (G9); creature pathing declined on purpose | `BehaviourMatrixTests` and `CreatureTests` unmodified; `ACreatureChasingRoundTheWorkshop_NeverOverlapsAPiece` (J): `IsClear` every tick for 600 ticks | local (accepted) |
| R-A8 | **A later, wider build area makes the local edit check inexact.** V-N1 is exact only because BLD008 caps each area at `seal_limit_nodes` (750.8 m² ≤ 1,024 m², D20) | medium, only if owner question 4 is answered "anywhere legal" | medium: a region larger than 16,384 nodes can be walled off unseen, and NPCs stall | BLD008 fails the content, which forces either a larger seal limit (the check's cost grows linearly with it, J.6) or labelled regions (M9) | BLD008 (a) and (b) on crafted packs | RK-06 |

**B. Building and persistence**

| ID | Risk | L | I | Mitigation | Test / proof | RK |
|---|---|---|---|---|---|---|
| R-B1 | **World-delta integration drops or duplicates pieces, `structure_seq` or errands**, through one of the four hand-built `DeltaSnapshot` sites (D15), the baseline proof, the rebase or the `FromSnapshot` order. Hand-written digests that miss a field (G12) would hide it | low with G11 and G12; medium without them | high: silent loss of player work, RK-11's worst class | `with` copies at `SaveLoader.cs:358` and `BaselineTransitions.cs:112`; the reflection guards G11 and G12; host-cell proof for every row | G11, G12; B §9.7 round trip; RK-11 round trips per M7 mutation class (J: place, dismantle, destroy, repair, piece door, assign, release, act) plus the negative bypass test | RK-11 (update), RK-06 |
| R-B2 | **The 13 → 14 save migration**: three parts in one step; `V13` `Player`, `EntitiesSection` and `Companion` frozen; `SchemaV8ToV9` and `SchemaV12ToV13` repointed; 13 `expected.json` files regenerated | low: every historical fixture migrates in CI, and each new field throws "(required from schema 14)" rather than defaulting | high: old saves become unloadable, or are silently defaulted | D §1.5 lands once, in S4, before any system writes the records; one v14 fixture carries every part, including an active route; `expected.json` is reviewed line by line | `HistoricalFixtureTests`; `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture`; N-P1..N-P4; `Schema13To14_GivesAnEmptyLedger`; the "corrupt, not defaulted" tests | RK-16, RK-03; PERSISTENCE RK-P07 |
| R-B3 | **The chest-crash class**: a derived ID is retired and later derived again. `CreateEntity` then throws "already exists" (`src/EntityRegistry/EntityRegistry.cs:51-52`), because `DestroyEntity` only tombstones (`:133-142`), and the throw happens inside `DrainCommands` or `Step`. B's critique C1 found one case: emptying a piece chest hits the corpse clause (`src/World/Runtime/Items.cs:558-562`), and `RemoveContainer` retires the `cnt_` (`src/World/WorldDelta.cs:390-400`) | medium: every derived-ID lifecycle can reopen it | high: the game crashes mid-tick, and progress since the last save is lost | B's C1 clause; a strictly increasing `StructureSequence` (no ordinal is reused); `RemoveCore` is the only retirer; `SpillContainer` keeps item IDs | `APieceChest_EmptiedAndRefilled_KeepsOneIdentity` and its save-split twin (B); `BuildingCommands_NeverThrow` (J, T10) | RK-06 |
| R-B4 | **ID minting breaks replay**: a wall-clock ULID enters a piece, an act or a route | low | medium: raw-digest proofs fail and ID-ordered logic diverges | `EntityId.Derived` (D4, D §1.7); acts use `Seq`; navigation stores no IDs; refunds go through `GrantItem`, which merges first, and a replay window containing a mint compares the replayable dump | G8 `M7SystemsMintNoWallClockIds`; B §11 step 0 (raw digest equal after a replay); N-A8 | RK-09 |
| R-B5 | **Read-path and order hazards**: creature homes recomputed on load from a space that includes pieces (G9); `Space` order depending on placement history (G10); an errand's pose disagreeing with the NPC's body (G18) | low | high for G9 (save-then-continue diverges); medium for the others | `Populate` keeps `Setup.Layout.Space` (`src/World/Runtime/Creatures.cs:173`); `StructureOrder`; `NpcSystem` writes both records in one tick | G9 `CreatureHomes_DoNotDependOnPlacedPieces`; G10; B's mid-walk save test | RK-16 |
| R-B6 | **An authored layout edit lands under saved pieces.** The layout is in no `baseline_hash` (`docs/M2B_STATUS.md:135`) | low: BLD007 makes authored geometry inside a build area a lint error, and M9 builds another region | medium: blockers overlap and push bodies; no data is lost | The load audit keeps and reports (`StructureAudit`, B §9.6); BLD007 | The v14 fixture loaded under a crafted pack that moves a rock into the area: the audit names it and nothing throws | RK-16; PERSISTENCE RK-P12 |
| R-B7 | **Pressure for more than one storey** (lofts, stairs, walkable roofs) from playtest or M9 | medium: the withheld kit already has floor planks | medium: it needs vertical `Kinematics`, 3-D navigation, a save change and a prediction change, a milestone of its own | Ruling 2 is written into DECISIONS and WA at S0. Invariants: `Kinematics` and `MovementRules` gain no members; `ClearanceMm` is always 0 on piece parts; roofs are never blockers. ROADMAP's answer is "more pieces, not physics" | BLD003 (no overhang parts); a reflection test that pins `MovementRules`' member list (J); N-X3 (the height filter) | accepted-risks row `docs/RISK_REGISTER.md:388` (update) |

**C. Factions**

| ID | Risk | L | I | Mitigation | Test / proof | RK |
|---|---|---|---|---|---|---|
| R-C1 | **Standing drifts into a global morality meter, or feeds hostility** | low: designed out | high: Charter §20, ruling 3 and ROADMAP M7's "no universal morality meter" | Per-faction points only, with no aggregate type; a ladder without hostility words; no reaction to trade, quests or building (G13); the tier is derived, never stored | G6 `TacticalCode_NeverReadsFactionState` extended per D29; G2 `QuestCode_NeverReadsStanding` and `PresentationSource_NeverDerivesStanding`; `TheLadder_HasNoHostilityTier`; `NoContent_TradesCurrencyForStanding`; F1 and P5 (opposite directions) | local; promote only when a milestone adds a propagation channel |
| R-C2 | **Psychic knowledge**: a faction learns without a witness or a report, or pooled knowledge travels | low | medium | K1-K10; FAC-M1 (members at the seat); FAC-M2 (no companion members); `report_act` informs only the speaker's own faction (R4). Accepted residue: pooling is immediate, and the errand NPC pools away from her seat (D30), which shipped content never reaches | N1 `AFactionThatNeitherSawNorWasTold_DoesNotUpdate`; F2, F6 (walls), F9 (companion), F12 | local |
| R-C3 | **Faction residues visible in play**: a companion's kill records no act (C open issue 1); migrated saves start with an empty log (2); a pre-M7 wares record never shows the billets (3); every shipped learning is by report (5) | high: players will notice | low: each is stated, and none corrupts | Recorded in `M7_STATUS`; owner question 5 confirms or overturns the defaults | `ACompanionsKill_IsNotThePlayersAct`; the migration tests | local |

**X. Cross-cutting**

| ID | Risk | L | I | Mitigation | Test / proof | RK |
|---|---|---|---|---|---|---|
| R-X1 | **Scope creep.** ROADMAP M7 still names crime and bounty, territory, free rotation and NPC work; three large parts; tempting "optional if cheap" items | high | high: RK-10's "architecture instead of a game", and M8 and M9 slip | Scope categories (scope rulings §4); D §3.2's slice gates; nothing optional before S10 is green; the `NotBuilt` kinds stay `NotBuilt` (`construct_building`, `src/Domain/Quests/Quests.cs:146`, and the faction objectives); no M8 work | A scope ledger in `M7_STATUS` that maps every built type to a ROADMAP phrase or scope row; existing `NotBuilt` tests unchanged | RK-10 |
| R-X2 | **An owner answer reverses a default after implementation**: 45° rotation, assignment moved to M10, building anywhere, a crime stub | medium | medium: rework | Decide rotation before S1b (it fixes shapes and the lattice), the build area before S2 (BLD008), and assignment before S8. Defaults are data-shaped where they can be | process | RK-10 |
| R-X3 | **Rulings 1 and 2 stay unwritten**, so a later agent follows WA §11's "Recast-style, baked per cell" (`docs/WORLD_ARCHITECTURE.md:435`) or RK-14's "it needs the engine" (`docs/RISK_REGISTER.md:290`) | medium: the documents contradict the rulings today | medium | Slice S0 writes them first | Review; `OnlyPresentation_MayReferenceGodot` (`tests/Architecture.Tests/ArchitectureTests.cs:27`) keeps Godot out of World regardless | RK-14 (update) |
| R-X4 | **Presentation takes authority**: the ghost decides validity, physics rays pick the pose, presentation derives tiers, or prediction reads a different blocker set from authority | medium: the new surface is large (build mode, ghost, `StructuresView`, the prediction switch) | high: RK-09 | Validity comes only from `BuildingRules.Validate`; the ghost only from `PreviewPlacement` (an allow-listed method); the aim point is the camera ray against the domain `TerrainGrid`; prediction reads `Simulation.Space` (D §1.1) | G4 `PresentationUsesNoPhysicsQueries`; G2's presentation scan; `PresentationSource_NeverReachesPastThePublicReadAndCommandSurface` (`ArchitectureTests.cs:163`); `Commands_CarryNoCameraState_...` (`tests/Application.Tests/SessionTests.cs:163`); B's "prediction equals authority across a new wall" | RK-09, RK-15 |
| R-X5 | **A read-only query writes something authority reads** (preview scratch, counters, registry) | low | high | `_previewScratch` and a null counter sink (D26); G7's source scan | H4 (1,000 interleaved previews give equal dumps, IDs and rejections); T5 (J) also asserts unchanged counters | RK-15 |
| R-X6 | **Asset dependencies.** The building kit is withheld (`src/Presentation/Art/art_bindings.json:25-31`); LOD files carry no material (`docs/PHASE1_ASSET_INTEGRATION.md:21`); the kit lives in DeepSeek-owned paths | high: art will not be ready | low for M7, which is greybox by scope. Later, 5-25k triangles per module (`docs/WAVE_0_MODULAR_ASSET_STANDARD.md:342`) × 256 pieces = 1.3-6.4 M triangles at full detail everywhere [computed] | Greybox primitives; bindings keyed by piece ID in `art_bindings.json` only when the owner chooses; no DeepSeek path is edited | Smoke; the J.11.4 capture repeated when the kit is released | RK-02 |
| R-X7 | **Float and transcendental leaks.** `Sin`, `Cos` and `Atan2` are already on authoritative paths (`Creatures.cs:190, 455, 616-617`; `Companions.cs:403`; `src/World/Runtime/Combat.cs:386, 528, 551, 607`; `src/Domain/Combat/Combat.cs:233-240`; `src/Domain/Creatures/Perception.cs:96-97`). M7 reuses them for witnesses (`Sees`), strike attribution (`FirstStop` over the trace at `Combat.cs:528, 551`) and errand facing | low: the contract is same-machine replay, and each CI test runs on one machine | low now; high only when a networked authority must agree across machines | N-X1 keeps navigation integer-only; building and factions add no new transcendental; D §6.2 records the exposure | N-X1; the replay tests | RK-09 (update) |
| R-X8 | **Content-lint gaps.** The loader enforces less than DATA_MODEL claims; builder lints stop at the first failure with no file or line; a load-phase error hides the semantic lints; an unregistered directory does not fail `LoadAll` (`research/content_registry.md` §0 items 5-7) | medium | medium: bad piece, area or faction data ships | BLD001-BLD008, NAV001-NAV007, FAC001 and WLD015, each with a crafted bad-file test; the exact list in `LoadAll_Loads_Yaml_Files`; `KnownDirectories_Is_Closed_Set` gains `pieces`; per-rule lints, not one `Try`, where the rule has an ID | Content.Tests per lint (A N-X3; B §22; C §17.2) | RK-08 |
| R-X9 | **The RAZER window is still owed** (`docs/M3_STATUS.md:4`; `docs/M6_STATUS.md:221`) | high: M7 will be built before it opens | medium: M7's cost lands on an unmeasured baseline, so a later failure cannot be attributed. RK-02's gate is an owner-review stop | J.12 captures the unchanged `--perf` and the M7 capture in one window, and runs the headless budget tests there to get the CPU ratio; ASTRAL targets are half-budget until then | The J.12 checklist | RK-02 (update) |
| R-X10 | **The main-thread tick budget**: more collision blockers, sight walls, follows and plans | low | medium | ≤ ~1.8 ms a tick at the tier-A cap [est], against 4 ms (J.5) | T2 (J) | RK-02, RK-06 |
| R-X11 | **Main-thread spikes.** A placement drains in the frame it was submitted in (a `CheckEdit` worst of 10 ms, D §1.2). One edit makes both movers replan in the next tick (J.2). The ghost asks for navigability on every change of snapped pose, 3-5 times a second while the aim sweeps | medium | medium: one frame drops; this is not a hitch over 33 ms | Per-call budgets; the J.13 levers (preview debounce, a per-tick plan budget), switched on only by a measurement | T3, T4, T5 (J); the J.12 frame columns | RK-02 |
| R-X12 | **Presentation cost per piece on RAZER**: at the cap, ~690 nodes, 284 mesh instances and 189 camera colliders (J.8) | medium: RAZER's CPU and render thread are unmeasured | medium | Measure draw calls and nodes; MultiMesh or merged meshes are the fallback (B open issue) | The J.11.4 capture | RK-02, RK-06 |
| R-X13 | **GC churn.** Each navigation edit copies one to four tiles' two 160,000-byte layers onto the large-object heap; building the 256-piece world allocates ~120 MB of it [computed] | low | medium: gen-2 pauses | Measure gen-2 collections and pause time (J.11.3). Lever: sub-tile blocks below 85,000 bytes (the grid is never saved, so there is no format change) | T6 logs `GC.CollectionCount(2)`; J.12 | RK-02 |
| R-X14 | **The save hitch.** Saves run synchronously in `GameSession.Frame` (`src/Application/GameSession.cs:194-199`), with a disk flush per file (`src/Persistence/SaveStore.cs:413`) and directory moves (`:430`). T-25 (≤ 2 ms P99, `docs/PERSISTENCE.md:599`) has never been measured. M7 adds ≤ ~50 KB (pieces) and ≤ ~100 KB (a ledger at its cap) of encoding [computed] | medium that the budget is already missed before M7 (not M7-caused) | medium: a hitch every 300 s (`SaveStore.cs:41`) | Measure. M7 reports the number and does not move I/O off the main thread (not M7 scope) | T7 (J); the quicksave frame in J.11.4 | RK-06 (size); T-25 |
| R-X15 | **Budget tests are flaky in CI.** CI runs `dotnet test` on `ubuntu-latest` (`.github/workflows/dotnet.yml`), which is slower than ASTRAL, so A's "cap plan ≤ 4 ms on ASTRAL" as a CI assert would fail there | medium | low: red CI and lost time | CI asserts deterministic work counts plus times at 3× the ASTRAL target; the tight numbers are logged evidence in `M7_STATUS` (J.11.2) | T1-T8 | local |
| R-X16 | **Region-wide blocker scans at scale.** `Kinematics.Resolve` walks every static blocker in the region on every pass (`src/Domain/Spatial/Kinematics.cs:183-198`), and pieces join that list region-wide (D §1.1). One area adds ≤ ~370 parts; four areas would push the tier-A-cap tick past budget [est] | low in M7 (one area, capped at 256) | medium at M9 | Lint BLD009 sets per-cell and per-region ceilings (J.4) until an order-preserving broadphase over A's per-tile input lists exists (B §21.5) | T2 on the densest layout (J) | RK-06 (update) |
| R-X17 | **Build-mode input and ruling 5.** B's direct keys collide with attack, take-all and `release_mouse` unless gated; a later "quick" radial wheel would break ruling 5 | low | low | B §21.3's gating; no radial anywhere | Smoke; the `--build-shots` beats; F1 lists the keys | local |

### I.3 Changes to `docs/RISK_REGISTER.md` and `docs/WORLD_ARCHITECTURE.md` (slices S0 and S10)

- **RK-02** (mitigation `:92`): add "M7: the unchanged `--perf`, the `--perf-world` structures capture and the headless budget tests, captured in the same RAZER window; record the CPU".
- **RK-05** (validation `:138`): the soak now exists (T9). Record catch-ups by reason, snags, and `RoutePlanned` outcomes as its path-failure count.
- **RK-06** (`:144-158`):
  - "navmesh rebuild" becomes "the domain navigation grid, restamped by rectangle per footprint change".
  - The validation is the headless RK-06 test (B §22) plus T2 and T7.
  - "grows sub-linearly" becomes "grows linearly at ≤ 250 bytes per piece (≈ 180 computed)". A row per piece (D-08) cannot be sub-linear, and it need not be: 5 MB holds ~29,000 pieces.
  - The ceiling becomes "256 intact pieces per build area; at most 256 per cell and per region (BLD009) until a broadphase exists".
  - Likelihood stays medium until the S10 evidence exists.
- **RK-09**: list the transcendental exposures (R-X7) and name `EntityId.Derived` as the replay-stable identity rule.
- **RK-11** (validation `:234`): the mutation classes gain piece place, dismantle, destroy and repair; the piece door; errand begin and end; and the faction act. The bypass test covers pieces.
- **RK-14** (`:280-296`):
  - withdraw "it needs the engine";
  - the validation becomes N-D9, N-D10, N-A2 (the Crossing Workshop) and N-A7;
  - delete "constrain or warn on placement that would straddle a seam", because straddling is proven;
  - lower the likelihood to low only after S10 is green.
- **Accepted risks** (`:388`): add "one storey (owner ruling 2): no upper floors, stairs, ladders, climbing or walkable roofs".
- **WA RK-A2** (`docs/WORLD_ARCHITECTURE.md:469`): "solved in M7 by a seam-free domain grid (A §6), proven by the Crossing Workshop". Write this after S10.
- **Not promoted:** R-A6, R-A7, R-C1, R-C2, R-C3, R-X15 and R-X17. They go in `M7_STATUS`'s local table. Each is guarded by a failing test or is a stated, accepted behaviour.

### I.4 The three rows that need owner awareness

1. **R-X1, scope.** It is the only risk that makes the others moot. Holding the defaults on the owner questions keeps M7 the size designed here.
2. **R-X9, the RAZER window.** One window must cover the Phase-1 gate and M7's capture. Without it, "60 fps" stays an assumption for two milestones.
3. **R-A5, the companion.** The M6 guarantees must survive unmodified, and RK-05's soak finally gets written.

---

## Part J - Performance budget

### J.0 Clocks, budgets and what is measured today

**Clocks.**
- The tick is 20 Hz (`content/config/time.yaml:7`), so 50 ms. A frame at 60 fps is 16.67 ms.
- `GameSession.Frame` drains commands every frame (`src/Application/GameSession.cs:183`), then steps whole ticks (`:185-191`). At 60 fps one frame in three carries a tick, and a command's cost lands in the frame it was submitted in, tick or not.
- After a stall, at most 0.25 s is replayed (`:52`), so at most 5 ticks run in one frame.

**Budgets** (explicitly unmeasured):
- target frame 16.6 ms at 1080p (`docs/WORLD_ARCHITECTURE.md:448`);
- main-thread world systems ≤ 4 ms (`:449`);
- streaming ≤ 3 ms per frame (`:450`);
- world content ≤ 1.5 GB (`:454`);
- save main-thread cost ≤ 2 ms P99 (`:455`; `docs/PERSISTENCE.md:533`; T-25, `:599`).

**Measured today (ASTRAL, not the gate).**
- 60 creatures cost 0.43 ms per tick (`docs/M3D_STATUS.md:53`); the test asserts < 4 ms (`tests/Application.Tests/CreatureTests.cs:526-542`).
- The capture ran 798-840 fps, with a p99 frame of 3.5-4.1 ms and a GPU p99 of 0.21 ms (`docs/M6_STATUS.md:85`).
- The worst process time each second is 8-9 ms (`docs/M6_STATUS.md:98`). Its make-up is unknown, because `FrameStats` has no simulation column (`src/Presentation/Perf/FrameStats.cs:78`).

**Machines.**
- ASTRAL is an RTX 5090 with a Ryzen 9 9950X3D (`docs/M3_STATUS.md:61`).
- RAZER is an RTX 4070 Ti. Its CPU is recorded nowhere; the capture's machine block will record it (`FrameStats.cs:151`).
- **Planning assumption until J.12 measures the ratio:** RAZER's main thread may be up to 2× slower than ASTRAL's, so every M7 number is designed to at most **half** its budget on ASTRAL.

**Timing never enters authority.** `src/Domain`, `src/World` and `src/Application` use no `Stopwatch` (research/sim_core §6.2). The only one in `src` is `src/Presentation/Spike/SpikeScene.cs:38`. M7 times only in tests and Presentation. World exposes deterministic work counters only.

### J.1 Navigation grid memory

| Item | Arithmetic | Size |
|---|---|---|
| Node | `SolidFit` + `ClosedFit` | 2 B |
| Tile (one 100 m cell) | 400 × 400 = 160,000 nodes × 2 B, stored as two 160,000-B layers | 320,000 B (312.5 KiB) |
| Ashen Hollow (4 cells) | 4 × 320,000 | **1,280,000 B = 1.22 MiB** [computed] |
| Inputs | ~64 B each (A §15.1). 67 authored inputs < 5 KB. A full area adds ≤ ~385 (≤ 370 solids plus doors); a straddling input sits in up to 4 tiles' lists | ≤ ~100 KB |
| One scratch | 262,144 × 9 B + 128,001 × 12 B + 16,384 × 4 B = 3,960,844 B | 3.78 MiB |
| Scratches per simulation | authoritative + preview (D26), both allocated lazily (D36) | ≤ 7.6 MiB |
| **Total per simulation** | | **≤ 8.9 MiB**, 0.6% of 1.5 GB [computed] |

**Later, 400 cells (2 km):**
- Everything resident would be 400 × 320,000 = 128 MB. That never happens.
- Residency follows tier A: ≤ 21 tiles within 160 m (A §15.2 [model]). That is 21 × 320,000 = 6.72 MB, plus 7.6 MiB of scratch, about **15 MB**.
- Forested tiles' input lists add ≤ 21 × 128 KB = 2.7 MB (2,000 trees × 64 B per tile).
- Building a tile on promotion takes 1-2 ms [est], one tile per tick, inside the 3 ms streaming budget.
- Scratch does not grow with the region, because the window is capped at 512 × 512 nodes.

**The large-object heap.** Each layer (160,000 B) exceeds .NET's 85,000-byte threshold.
- A copy-on-write edit therefore allocates two large-object arrays per touched tile: 320 KB to 1.28 MB per edit.
- Building the 256-piece perf world allocates ≈ 256 × 1.5 tiles × 320 KB ≈ **120 MB** of large-object garbage over the session [computed].
- At a human build rate this is expected to be harmless. That is measured, not assumed (T6, J.11.3). The lever is in J.13.

### J.2 Pathfinding: who plans, how often, and the caps

| Planner | When it plans | Rate bound |
|---|---|---|
| Kera's errand (`NpcSystem`, `Errands.cs`) | Only in `to_work` and `to_home`, never while `at_work` (D §1.3) | A §7.6's triggers |
| Tavar (`CompanionSystem`) | Only in Route or Nav mode: the character is not in clear view and no trail mark is | A §7.6's triggers |
| Assign refusal 10 (`Reachable`, D35) | Once per `AssignWorkerCommand` | human-paced |
| Placement and preview | Floods (`CheckEdit`), not A* | J.6 |
| Creatures, the player, static NPCs | never | 0 |

**Trigger rates** (A §7.6):
- `retry`: at most once per 40 ticks.
- `goal_moved`: companion only, at most once per 10 ticks. Running out of view at 3.2 m/s (`content/config/base_speeds.yaml:7`), the goal moves 2,000 mm in 12.5 ticks, so about **1.6 plans/s**.
- `stuck`: once per 20 ticks while stuck.
- `off_line`: at most once per 10 ticks.
- `geometry`: once per edit whose rectangle meets the route's watch window.
- `none` and `partial`: once per goal.

So one mover plans at most about twice a second in steady state, and each of the two M7 movers plans at most once per tick.

**Caps.** 16,000 expansions; probes of 2 × 2,048 nodes; a window of ≤ 512 × 512 nodes; ≤ 32 corners.

**Cost** at ~250 ns per expansion [est; N-A10 measures it]:

| Case | Expansions | Time |
|---|---|---|
| Typical M7 route | 121-4,618 [model] | 0.03-1.2 ms |
| Worst real Ashen Hollow route | 9,709 [model] | 2.4 ms |
| At the cap | 16,000 | 4.0 ms |

**Average cost per tick.**
- The companion: 1.6 plans/s × ~0.3 ms ≈ 0.5 ms/s, or 0.024 ms per tick.
- Kera: 1-3 plans per 840-tick trip.
- Following: 2 × 10-20 µs.
- Total ≈ **0.04-0.07 ms per tick**, matching A's 0.04.

**Worst single tick.**
- An edit at boundary N changes both movers' stamps, so both replan at N+1 (companions, then NPCs, `src/World/Runtime/Simulation.cs:326-327`).
- The plausible worst pair is 4,618 + 9,709 ≈ 14,300 expansions ≈ **3.6 ms** [est].
- The theoretical 8 ms needs both searches to exhaust the cap, and probes answer enclosed goals first (A §7.3).
- **Rule:** if T4 measures the edit tick's median above 4 ms on ASTRAL, or J.12 sees it on RAZER, switch on A §9's per-tick plan budget (deterministic, reset every tick, never saved). Otherwise do not build it.

**M9 note (not built).** 60 scheduled actors plan about 1.5 times per tick (A §15.2). That is the recorded trigger for the plan budget and the portal layer.

### J.3 Rebuild scope and cost after construction

The rebuilt nodes are those whose centres (250·i + 125) lie in the parts' AABB inflated by 600 mm (A §5.2). Using B §20's geometry:

| Piece | Parts AABB | Nodes (x × z) [computed] | Tiles touched |
|---|---|---|---|
| Wall, or doorway (the union of its jambs, D10), e.g. edge (103500, 99000) r0 | 3.4 × 0.4 m | 18 × 6 = **108** | 1-2 |
| Door leaf (a gate input: the `ClosedFit` layer) | 1.6 × 0.4 m | 12 × 6 = 72 | 1-2 |
| Chest or bench | 1.0 × 0.6 m | ≈ 8-9 × 7 ≈ 56-63 | 1-4. A chest on square (33, 33) at r2 lies within 600 mm of both seams, so 4 tiles |
| Pad or roof | no parts | 0 (neither is a navigation input) | 0 |
| A's 6 × 6 m hut edit | | ≤ 900 | |

Walls cannot touch 4 tiles. The nearest lattice lines to the 100 m seam are 99 m and 102 m, and a wall's inflated face stops at 99.8 m.

**Cost per footprint change.**
- Each node is recomputed from ≤ 5 inputs (A §15.1). A wall needs ≤ 540 `FitAt` evaluations, ≈ 5-11 µs at 10-20 ns each [est].
- Copy-on-write of 320 KB per touched tile: ≈ 15-30 µs [est].
- Refiltering the tile's inputs and hashing its stamp: ≈ 10-20 µs per tile [est].
- Total ≈ **0.05-0.2 ms** [est], against A's asserted < 2 ms.
- Building's own `Rebuild()` (sorting ≤ 434 blockers, a socket index of ≤ ~1,300 entries, footprints) adds ≈ 0.1-0.5 ms at the cap [est]. T3 measures it.

**Load.**
- The full build is a fill of 640,000 nodes, plus 20,756 authored evaluations [model], plus the perf world's pieces: 58 × 108 + 28 jambs × 48 + 14 × 72 + 8 × ~60 ≈ 9,100 [computed].
- That is ≈ 0.3-0.6 ms of evaluation plus the fill [est]. The densest area adds ≈ 18,000 evaluations. N-A10 asserts < 20 ms.

**Nothing rebuilds on** door toggles, barrier lifts, damage, repair or assignment (A §4).

**Pads and roofs.** B §3.4 dispatches `RebuildNavigation` with the placement bounds for every piece, while D10 says "the union of changed parts". For a pad or roof, navigation must treat the call as a no-op, or building must skip the dispatch. Tile stamps cannot change either way, because pads and roofs are not inputs.

### J.4 Pieces: the proof sizes and the RK-06 ceilings

| Case | Pieces | Solid parts (static blockers added) | Source |
|---|---|---|---|
| Crossing Workshop, after step 1 | 19 | 11 (2 jambs, 7 walls, bench, chest) + 1 door leaf | B §11 [computed] |
| Crossing Workshop, at the end | 23 intact, `StructureSequence` 31 | 13 | B §11 [computed] |
| B's RK-06 test | 200 (81 pads, 81 roofs, 38 walls) | **38**: a weak collision stress | B §22 |
| Perf world (J.11.4) | 256: 81 pads, 81 roofs, 58 walls, 14 doorways, 14 doors, 7 chests, 1 bench; 338 timber | 94 + 14 door leaves | [computed] |
| Densest legal area | 246 | **≈ 369** | [computed], below |

**The densest area.** In a 9 × 9-square area, a checkerboard of 41 pads (corners dark) touches all 144 interior edges and 20 of the 36 boundary edges. That allows 164 doorways (2 jambs each) plus 41 pieces of furniture: 369 parts from 246 pieces. Adding pads trades one-for-one and does not raise it. So one area holds at most **~370 solid parts**.

**Ceilings**, as RK-06 asks ("a piece-count ceiling per cell ... treated as a content constraint", `docs/RISK_REGISTER.md:158`):
- **per area:** 256 (`max_pieces`; it exists);
- **per cell:** the sum of `max_pieces` over the areas that meet the cell ≤ 256;
- **per region:** the same sum ≤ 256 until an order-preserving `Kinematics` broadphase exists.

Both new ceilings are one cheap lint, **BLD009**, in `config.building`. The reason is J.5: one full area costs ≈ 0.4-0.8 ms of collision per tick at 60 tier-A creatures, and four would cost ~2-3 ms, which breaks 4 ms once creature AI and sight are added. Ashen Hollow's single area meets both ceilings as authored.

### J.5 Collision and sight with pieces

**`Resolve`.**
- Each sub-step makes ≤ 4 passes over every static blocker, then the dynamic ones (`Kinematics.cs:183-208`).
- Sub-steps are ⌈travel / (r/2)⌉ (`:145`): 1 at a run (160 mm per tick, r = 350 mm) and 2 at a sprint (256 mm).
- The bodies stepped each tick are the player, tier-A creatures (13 placed by content, 60 at the cap), the companion and the errand NPC.
- Checks per tick ≈ bodies × ~1.5 passes [est] × static blockers:

| Scene | Static blockers | 16 bodies | 63 bodies |
|---|---|---|---|
| Ashen Hollow today (`content/regions/ashen_hollow.yaml:64-141`: 64 structures) | 64 | 1,500 | 6,000 |
| Crossing Workshop | 77 | 1,850 | 7,300 |
| Perf world | 158 | 3,800 | 14,900 |
| Densest area | 433 | 10,400 | 40,900 |

At 10-20 ns per `Blocks` + `Push` [est], the densest area costs **0.4-0.8 ms per tick** at 60 creatures. Separately, creatures push against every other living creature as a dynamic blocker (`Creatures.cs:585-587`). That cost is O(C²) and does not depend on pieces.

**Sight.**
- Creatures' `Walls()` concatenates every static blocker and `ClosedDoors()` (`Creatures.cs:893`), after range and cone checks (`Perception.cs:87-107`).
- Worst case: 60 × 433 = 26,000 `Crosses` per tick ≈ **0.5 ms** [est]. That happens only if every creature has the player in range and in its cone.
- `ClosedDoors()` builds a new array on every call (`src/World/Runtime/Systems.cs:48`). D appends the cached piece leaves, so the allocation grows only with closed piece doors.

**Total at the tier-A cap.** 0.43 [measured] + ≤ 0.8 + ≤ 0.5 + navigation ≤ 0.07 ≈ **1.8 ms per tick** [est], with the spikes of J.2 on top. That is under 4 ms, and meets the half-budget rule on ASTRAL.

### J.6 Placement validation: every frame vs per command

| Path | What runs | Cost | How often |
|---|---|---|---|
| Preview, every frame | `Snapper.Snap` plus checks 1-14 of B §3.3. The costlier checks: 169 terrain samples (pads only); overlap against 64 structures, 2 doors and 1 barrier; ≤ 65 bodies; a count of ≤ 256 pieces; ≤ 24 stacks | ~5-20 µs [est] and a few hundred bytes of garbage | every frame in build mode |
| Preview, on a pose or revision change | adds `CheckEdit` on `_previewScratch` | 0.3-2.6 ms [est]: in open ground one flood of ≤ 16,384 nodes at 20-40 ns per node; ≤ 4 floods when a pocket seals. D: median ≤ 2 ms, worst ≤ 10 ms | 3-5 times a second while sweeping [est: the aim moves ~10-14 m/s at a 6-9 m camera, over 3 m slots] |
| Command | checks 1-15, `ExchangeItems`, the commit, `Rebuild()`, `RebuildNavigation`, the events | `CheckEdit` + 0.2-0.7 ms | per placement, at human pace (≤ ~2/s) |
| Dismantle, repair | no navigability check | ≈ 0.2-0.7 ms | per command |

**Budget lines.**
- The per-frame preview costs ≤ 0.1 ms (0.6% of a frame).
- The navigability preview p99 must stay ≤ 4 ms on RAZER; otherwise use the debounce lever (J.13).
- A command frame must stay ≤ 16.7 ms on RAZER (no dropped frame), and in any case < 33 ms (no counted hitch, `FrameStats.cs:119`).

### J.7 Faction update complexity

- **Per tick: zero.** `FactionSystem` has no `Tick` (C D1).
- **`RecordAct`.**
  - Relevance is a lookup in a sorted set (2 pairs in shipped content).
  - For a relevant act: for each reacting faction (≤ 2) and each member (≤ 3), one `Perception.Sees`: range, cone, then ≤ W wall crossings. W is 64 today, 158 in the perf world, ≤ 433 at the densest.
  - So ≤ 2,600 `Crosses` ≈ 0.05 ms [est].
  - `Learn` is O(A + K), with A ≤ 256 acts and K ≤ 512 knowledge rows (`Single` scans plus one immutable copy). `Compact` is O(A + K).
  - Total **≤ 0.1 ms per act**.
- **`ReportAct`.** m matching acts (1 in shipped content, ≤ 256 at the cap), each an O(A + K) `Learn`. At the cap that is ≈ 200,000 comparisons ≈ **0.2-0.5 ms** [est], once per dialogue choice.
- **Gates.** The dialogue `reputation` condition and the stock row's `requires` are O(factions) per evaluation. `Simulation.Wares` evaluates them per call, which means per frame while the trade panel is open. The cost is trivial.
- **Scale.** Shipped content records ≤ 2 acts (C §4.3). At M9 the range check runs before any wall test, so the cost follows the NPCs within 30 m, not every NPC.

### J.8 Presentation cost per piece

The greybox `Solid()` helper is `MeshInstance3D` + `StaticBody3D` + `CollisionShape3D`, with a new `BoxMesh` and `BoxShape3D` each time (`src/Presentation/Greybox/HollowView.cs:374-381`, `:166-174`). A door adds a hinge `Node3D` (`:219-237`). Per piece, per B §21.3:

| Piece | Nodes | Mesh instances | Camera colliders |
|---|---|---|---|
| Pad | 1 | 1 (a draped slab, its own `ArrayMesh`) | 0 |
| Wall, roof, chest, bench | 3 | 1 | 1 |
| Doorway | 8 (a parent, 2 jamb `Solid`s, the lintel mesh) | 3 | 2. B does not say whether the lintel gets a camera collider (issue) |
| Door | 4 | 1 | 1 |

**Perf-world totals** [computed]:
- nodes 81 + 243 + 174 + 112 + 56 + 21 + 3 = **690**;
- mesh instances 81 + 81 + 58 + 42 + 14 + 7 + 1 = **284**;
- camera colliders 81 + 58 + 28 + 14 + 7 + 1 = **189**.

Today's structure layer is 64 × 3 + 2 doors × 4 + 2 roofs × 3 = 206 nodes, so this is **×4.3**.

**Draw calls.**
- Each mesh instance is drawn in every pass that includes it: opaque, depth prepass, and the sun's shadow pass (the sun casts shadows, `HollowView.cs:357`).
- Distinct `BoxMesh` resources are not batched together [est].
- Expect several hundred to ~1,500 more draw calls with the whole workshop in view [est]. Primitives are trivial (a box is 12 triangles).
- The GPU is not the risk (ASTRAL's GPU mean is ≈ 0.17 ms). The render thread's CPU is (`render_cpu_ms`, `FrameStats.cs:67`).

**Colliders.** They are static, on layer 1 with mask 0. They cost broadphase memory and the camera spring arm's query, but they are not simulated.

**Runtime edits.** `StructuresView` adds or frees 1-8 nodes per event. The first use of a mesh and material may compile a pipeline, which the `PipelineCompilations*` monitors count.

**Fallbacks, only on measurement:**
- one `MultiMesh` per identical definition and rotation parity (pads differ by draping);
- one merged `ArrayMesh` with one trimesh collider per connected structure (the terrain precedent, `HollowView.cs:124-126`).

**Art later.** 1.3-6.4 M triangles at the cap with no usable LOD (R-X6). Re-measure then.

### J.9 Save and load growth

Sizes are computed for MessagePack with Standard options, string keys and no compression (`src/Persistence/SectionCodec.cs:313-314`). A ULID is 26 characters plus a 4-character prefix; a cell key such as `r_0_0:c_01_01` is 13 characters.

| Record | Arithmetic (key bytes + value bytes) | Bytes |
|---|---|---|
| `PieceDto` | map 1; `instance_id` 12+31; `def_id` 7+18..20; `host_cell` 10+14; `x_mm` 5+5; `z_mm` 5+5; `rotation` 9+1; `owner` 6+31; `health` 7+2; `door_open` 10+1 | **≈ 180** |
| 256 pieces | | ≈ 46 KB |
| `structure_seq` | 14 + ≤ 9 | ≤ 23 |
| `NavRouteDto` | ≈ 100 + 10 per corner (≤ 32 corners) | 130-450 |
| `NpcErrandDto`, route included | ≈ 220 + route | 350-670 |
| `ActDto` | map 1 + keys 41 + values ≈ 83 (the subject `creature.construct.animated_armour` alone is 36) | ≈ 125 |
| `KnowledgeDto` | keys 42 + values ≈ 88 | ≈ 131 |
| `StandingDto` | | ≈ 50 |

**Growth.**
- An act learned by two factions costs 125 + 2 × 131 ≈ **390 B**.
- Shipped play (≤ 2 acts) costs < 1 KB.
- At the 256-act cap the ledger reaches ≈ **100 KB**, half of `player.msgpack`'s 200 KB upper budget (`docs/PERSISTENCE.md:549`). Only later content can reach it, because relevance and FAC-R5 hold shipped play to 2 acts.
- Growth is **linear at ~180 B per piece**. The entities budget is 2-40 MB (`:551`); the budget of the old separate buildings section is 10 KB-5 MB (`:553`), which would hold ~29,000 pieces.
- For scale, today's v13 fixture sections are 2,791 B (entities) and 2,364 B (player) (`tests/Persistence.Tests/Fixtures/v13/quick/`).

**Time.**
- Encoding ~50 KB of pieces plus ~100 KB of ledger at the cap takes ≈ 0.1-0.5 ms [est].
- The unmeasured part is the disk flush per file (`SaveStore.cs:413`), which predates M7.
- Load adds the same decode, plus ≤ 0.5 ms for `_building.Populate()` and ≤ 1 ms for `_navigation.Build()` [est].

### J.10 The budget sheet

| Cost | When | ASTRAL target (half-budget) | Budget it belongs to | Proof |
|---|---|---|---|---|
| Tick at the cap (60 creatures, perf world, both movers walking) | every tick | ≤ 2 ms mean | ≤ 4 ms per tick (WA:449) | T2 |
| Edit tick (both movers replan) | after an edit | ≤ 2 ms median; ≤ 4 ms worst real | ≤ 4 ms per tick | T4 |
| One plan at the cap | per trigger | ≤ 2 ms (A gates it at ≤ 4 ms) | ≤ 4 ms per tick | N-A10/T6 |
| Navigation restamp per piece | per footprint change | ≤ 0.2 ms | frame | T3, N-A10 |
| `CheckEdit` | per command; per pose change | ≤ 1 ms median; ≤ 5 ms worst | ≤ 16.7 ms frame; ≤ 10 ms (D) | T3, T5 |
| Preview checks 1-14 | every frame | ≤ 0.05 ms | frame | T5 |
| `RecordAct`; `ReportAct` at the cap | per act; per report | ≤ 0.1 ms; ≤ 0.5 ms | frame | T8 |
| Save capture + encode (no disk) | per save | ≤ 1 ms p99 | ≤ 2 ms P99 (WA:455) | T7 |
| Load increment | per load | ≤ 3 ms | boot | T7 |
| Memory | resident | ≤ 8.9 MiB of navigation; < 1 MB of pieces | ≤ 1.5 GB (WA:454) | T6 logs it |
| Presentation | every frame | measure: +690 nodes, +284 mesh instances, +189 colliders | 16.7 ms at 1080p/60 on RAZER | J.11.4, J.12 |

### J.11 Instrumentation M7 must add

#### J.11.1 Deterministic counters (World)

| Counter set | Contents |
|---|---|
| `NavCounters` | A §14, unchanged |
| `BuildingCounters` | `PlacementsAccepted`, `PlacementsRefusedByRule[15]`, `NavVerdicts[4]`, `Dismantles`, `Destroys`, `Repairs`, `PieceDoorOps`, `Rebuilds`; and the current values `StaticBlockers`, `ClosedPieceLeaves`, `SocketEntries` |
| `FactionCounters` | `ActsRecorded`, `ActsIgnored`, `WitnessChecks`, `Learns`, `Upgrades`, `Reports`, `Evictions` |

Rules for all three, carrying A §14 over:
- Each is an immutable snapshot built from a per-system sink.
- None is saved, or appears in a digest or `StateDump`, or is read by any decision.
- Previews never count (G7).
- They are exposed as get-only views: `Simulation.Navigation.Counters`, `Simulation.BuildingCounters` and `Simulation.FactionCounters`. No allow-list change is needed (`tests/Architecture.Tests/ArchitectureTests.cs:112`).
- New guard `CountersAreNeverRead`: a source scan in which counter members appear only in their owner's increments and in the view builders.

#### J.11.2 Headless budget tests (`tests/Application.Tests/M7BudgetTests.cs`)

**The CI rule.** Assert deterministic work counts exactly or as upper bounds. Assert times only at **3× the ASTRAL target**, because CI runs on `ubuntu-latest`. Log every time through `ITestOutputHelper`, as `CreatureTests.cs:539` does. The ASTRAL and RAZER evidence lines in `M7_STATUS` use J.10's targets.

| # | Test | Setup | Asserts (CI) / evidence |
|---|---|---|---|
| T1 | `M7CostTable_MatchesTheBuild`, generating `docs/M7_COST_TABLE.md` on the TTK pattern (`tests/Application.Tests/TtkTableTests.cs:14-18`, env `UNNAMED_WRITE_COST=1`) | Counts only: nodes and tiles per piece kind, in mid-cell and at the four-cell corner; flood nodes and verdict for each Crossing Workshop placement; expansions, corners and probe nodes for Kera's routes (site to anchor, back, and after the west wall) and for N-A11; full-build evaluations at 0, 23, 200 and 256 pieces; section bytes at those piece counts and at 0, 2 and 256 acts | The committed file equals the build. Counts are machine-independent, so every change shows as a reviewed diff |
| T2 | `TheCapWorkshop_SixtyCreaturesAndTwoMovers_TickWithinTheBudget` | The perf world (T11's helper). The crowd of `CreatureTests.cs:532` with its x origin moved from 90 to **120 m**: the original's x 90-162, z 80-120 overlaps the build area, 87-114. Tavar in Route mode; Kera walking. 20 warm-up ticks, 400 timed. Repeat on the densest layout (369 parts) | CI mean < 12 ms; evidence mean ≤ 2 ms; log p95, max and `GC.CollectionCount(2)`. `SixtyCreatures_TickWithinTheBudget` stays unmodified. **This replaces D S7's "the existing test with 200 pieces"** |
| T3 | `APlacementBurst_StaysWithinTheCommandBudget` | The 256 perf-world placements plus 100 LCG candidates that are refused. Each is timed as `Submit`, then `Frame(0)`, which drains without stepping (`GameSession.cs:177-191`) | Evidence: median ≤ 2 ms, max ≤ 10 ms (CI 6 ms / 30 ms); dismantle and repair ≤ 1 ms; `Rebuild()` ≤ 0.5 ms at the cap |
| T4 | `AnEditThatReplansBothMovers_StaysWithinTheTickBudget` | 20 fresh runs; a wall placed across both routes; the next tick timed | Exactly 2 `RoutePlanned(geometry)` at N+1; evidence median ≤ 2 ms (CI 12 ms). Feeds J.2's rule |
| T5 | `PreviewPlacement_StaysWithinTheFrameBudget` | 1,000 previews over the area, half with navigability | Without navigability, mean ≤ 0.05 ms; with it, median ≤ 1 ms and max ≤ 5 ms; counters and digest unchanged (with H4) |
| T6 | A's N-A10, with CI multiples | as A | Logs ns per expansion, the cap-plan time and gen-2 collections across 256 edits. "Cap plan ≤ 4 ms" is ASTRAL evidence, not a CI assert |
| T7 | `SaveAndLoad_WithTheCapWorkshop_StayInsideTheirBudgets` | 100 × (`SaveDocuments.Capture` + encode, no disk). The full `GameSession.Save` is timed and logged only, because disk speed varies | Evidence p99 ≤ 1 ms; `entities.msgpack` growth ≤ 256 × 250 = 64,000 B; `player.msgpack` growth with a 256-act ledger ≤ 110,000 B; load increment ≤ 3 ms |
| T8 | `FactionActs_AtTheLogCap_StayCheap` | 256 relevant acts, members in range, the perf world's walls | `RecordAct` mean ≤ 0.1 ms; `ReportAct` at the cap ≤ 0.5 ms |
| T9 | `TheCompanion_Soak`: RK-05's validation, and A's N-A14 made real | 36,000 ticks (30 min) of scripted walking through the lodge, the smithy gap, the workshop doorways and past the beam, with the character out of view half the time | Logs catch-ups by reason, snags, `RoutePlanned` by outcome and the longest hold; zero catch-ups on the reachable legs |
| T10 | `BuildingCommands_NeverThrow` (R-B3) | 5 seeds × 500 LCG commands: place, dismantle, repair, store, take, blows to destruction, assign, release, save and load | No exception; the replay equals the run |
| T11 | `PerfWorld_IsBuiltThroughCommands` (J.11.4's generator) | as J.11.4 | `StructureAudit` empty; every room's interior `Reachable` from the spawn; writes the save only when `UNNAMED_WRITE_PERF_WORLD` names a directory |

#### J.11.3 Presentation: `FrameStats` and `Main`

**In `Main._Process`:**
- a `Stopwatch` around `_session.Frame(...)` (`src/Presentation/Main.cs:305`), giving `sim_ms`;
- `FrameResult.TicksRun` and `AutosavedTo` (`GameSession.cs:43`);
- a separately timed `save_ms` when `QuickSave` ran this frame (`Main.cs:485-486`, `:930-934`).

Presentation never times `Step`, whose literal call is banned there (`ArchitectureTests.cs:163`).

**`FrameStats.Record(delta, in FrameProbe probe)`.** New columns go after the existing nine (`FrameStats.cs:78`), so old readers still work:

`sim_ms, ticks, save_ms, draw_calls, objects, primitives, nodes, pipeline_compiles, gc0, gc1, gc2, gc_pause_ms, alloc_kb, plans, expansions, nodes_restamped, edit_checks, flood_nodes, pieces, static_blockers, closed_leaves, preview_ms, preview_nav`

Sources, each verified:
- `Performance.Monitor.RenderTotalDrawCallsInFrame`, `RenderTotalObjectsInFrame`, `RenderTotalPrimitivesInFrame`, `ObjectNodeCount`, and the sum of `PipelineCompilations{Canvas, Draw, Mesh, Specialization, Surface}` (GodotSharp 4.7.2);
- `GC.CollectionCount`, `GC.GetTotalPauseDuration` and `GC.GetAllocatedBytesForCurrentThread` (the .NET 8.0.31 reference pack);
- the counter deltas, from J.11.1's views.

**The summary gains, per segment:**
- `sim_ms` distributions over all frames and over tick frames;
- the most ticks in one frame, and the largest `save_ms`;
- p50, p99 and max of draw calls, objects and nodes;
- how many frames compiled a pipeline;
- the gen-2 count, and total and largest GC pause;
- `alloc_kb` at p50 and p99;
- plans, and the most expansions in one frame;
- `preview_ms` at p50 and p99;
- the largest frame that drained a build command.

#### J.11.4 PerfRun: the structures capture and its world

**Generator (T11).** A save written on shipped content from a fixed seed:
- The crafted starting `PlayerRecord` (B §11's pattern) carries 378 timber in 19 stacks of 20, inside the 24 slots (`content/config/inventory.yaml:7`). Spending checks no weight (`src/World/Runtime/Items.cs:327-356` checks weight only for a received item).
- All 256 pieces go in by `PlacePieceCommand`, room by room, so every piece passes all 15 checks.
- Tavar is recruited; Kera is assigned to the bench.
- 40 timber is left carried.

**Layout.**
- 9 rooms of 3 × 3 squares, on the lines x, z ∈ {87, 96, 105, 114} m.
- 12 internal doorways, one per room adjacency, plus 2 exterior doorways on the west side, clear of `PerfRun`'s `Valley` route through (100, 100) (`src/Presentation/Perf/PerfRun.cs:26`).
- 7 chests, and the bench in the centre room.
- A `rooms = 4` variant (≈ 117 pieces) gives a second point on the curve.

**Flag.** `--perf-world <dir>` boots that save. `--perf` does not change, so it stays comparable with M6's ASTRAL numbers.

**Segments:**
1. `warmup`, 5 s.
2. `structures_obstruction`, 75 s at `CameraRig.MaxDistance`, orbiting every 20 s, on a route through the rooms by their doorways, opening closed doors as `PerfRun.cs:124-130` does.
3. `structures_third_person`, 75 s at 3.5 m, around and through the rooms.
4. `structures_first_person`, 75 s at 0 m, inside.
5. `structures_build`, 60 s in build mode:
   - the aim sweeps the area, so the ghost is previewed every frame;
   - an interior wall is dismantled and placed again every 2 s (30 cycles; the cap stays at 256; net cost 30 timber);
   - Kera is released at 5 s and assigned again at 35 s (two long plans and a walking errand);
   - a door is toggled every 5 s;
   - one quicksave at 30 s.

All placement goes through the build-mode command path, as a player's keys would (G4). The 12 proxy bodies stay.

**Notes written with the capture:** pieces, solid parts, closed leaves, nodes, waypoints reached per segment (a stuck walker shows as a count that stopped), and the timber left.

#### J.11.5 Guards on the instrumentation itself

- `CountersAreNeverRead` (J.11.1).
- A source scan: no `Stopwatch`, `DateTime.Now` or `UtcNow` in `src/World` or `src/Application`, and none in `src/Domain` outside `EntityId.NewId` (`src/Domain/EntityId.cs:48-53`). None exists today, so nothing breaks.
- The existing ban on `.Step()` and `.DrainCommands()` in presentation stays.

### J.12 What the owed RAZER window must additionally capture for M7

The owner provides a clean window. The agent terminates nothing (not OBS, not H3, not other GPU work).

**Order** (about 20-25 minutes):
1. Build Presentation at the M7 commit. If time allows, capture the `e10d2c4` build first; M7's zero-piece overhead is then attributable.
2. `godot --path src/Presentation -- --perf --perf-out <dir>/prototype`, unchanged: the Phase-1 gate with M7 code and no pieces.
3. `... -- --perf --perf-world <dir>/perf_world_256 --perf-out <dir>/structures`.
4. `... -- --spike --perf-out <dir>/spike`, unchanged.
5. `dotnet test src/UNNAMED.sln --filter "FullyQualifiedName~M7BudgetTests|FullyQualifiedName~SixtyCreatures_TickWithinTheBudget|FullyQualifiedName~Navigation_StaysWithinBudget" --logger "console;verbosity=detailed"`, with the output kept. It gives the ASTRAL:RAZER CPU ratio that replaces J.0's ×2 assumption.

**Record:**
- the machine block, including the CPU;
- per segment: average fps, 1% low, frame p99, hitches over 33 ms;
- `sim_ms` p99 on tick frames, and the most ticks in one frame;
- draw-call p99, node maximum, gen-2 count, largest GC pause, pipeline compiles;
- `preview_ms` p99, the largest command frame, the quicksave frame's ms;
- VRAM and working set.

**M7 pass lines:**
- every segment's 1% low ≥ 60 fps (the existing gate);
- frame p99 ≤ 16.7 ms;
- `sim_ms` p99 on tick frames ≤ 4 ms;
- no hitch over 33 ms after the warm-up, **except** the quicksave frame. That frame is recorded as T-25 evidence (pre-existing), not as an M7 failure;
- `preview_ms` p99 ≤ 4 ms, or the debounce lever is switched on;
- every command frame < 33 ms.

**Where the results go.** Raw captures stay outside the public repository, as M6's videos did (`docs/M6_STATUS.md:76`). The numbers and pass lines go in `docs/M7_STATUS.md`.

### J.13 Levers, in order: none is built until a measurement trips it

1. **Render thread or draw calls too high:**
   - a `MultiMesh` per piece definition;
   - one merged mesh and collider per structure;
   - a preview debounce: ask for navigability only after the snapped pose has held for 2 frames, and show amber `NotChecked` until then. This is presentation-only, so parity is untouched.
2. **Simultaneous replans:** A §9's per-tick plan budget; or `max_expansions` lowered in content, never below 12,000 (A).
3. **GC:** sub-tile navigation blocks below 85,000 bytes.
4. **Collision:** an order-preserving broadphase that filters, then iterates in the original list order (B §21.5).
5. **Content:** lower `max_pieces`. It is data, and it is the RK-06 ceiling.
6. Then WORLD_ARCHITECTURE's order: radii, then caps, then AI rate, then LOD (`docs/WORLD_ARCHITECTURE.md:460`).
