# M7 panel critique of B_building_v1.md (adversarial review)

Status: adversarial review, 2026-09-24. Target: `drafts/panel/B_building_v1.md` (the "design"). Base: origin/main `e10d2c4`, read from the snapshot named in `00_SCOPE_RULINGS.md` §0. Every `path:line` is repo-relative at that commit and was read in the snapshot for this review. Four lenses: (a) scope, (b) code fit, (c) determinism, (d) one storey and terrain honesty. Findings are ordered by severity. Each one gives its lens, the evidence and a concrete fix.

Overall verdict: the design is strong. Its lattice, ID and persistence choices are sound, and most of its code citations are accurate (see §6). It has **one crash-class defect**, **four blocking gaps** that an implementer cannot resolve alone, and a set of medium and low repairs. None of them needs a change to a scope ruling.

---

## 1. CRITICAL

### C1. Emptying a player chest retires its derived `cnt_` ID, and the next deposit throws (b, c)

**Evidence.**
- `InventorySystem.Take` has a container branch. When a container is emptied and is not an authored layout container, it removes the record and dispatches `CorpseEmptied`: `if (items.IsEmpty && _context.Setup.Layout.FindContainer(site.Key) is null) { State.RemoveContainer(...); _context.Dispatch(new CorpseEmptied(site.Key)); }` (`src/World/Runtime/Items.cs:558-562`). A piece chest is not in `Setup.Layout`, so taking its last item (by `MoveItemCommand` or `TakeAllCommand`) takes this corpse path.
- `WorldDelta.RemoveContainer` calls `_registry.DestroyEntity` on the container's ID (`src/World/WorldDelta.cs:389-400`).
- `DestroyEntity` only tombstones: the ID stays in `_instances` (`src/EntityRegistry/EntityRegistry.cs:133-142`).
- `CreateEntity(def, id)` throws `"Instance ID ... already exists"` whenever `_instances` contains the ID, dead or alive (`EntityRegistry.cs:50-51`).
- The design's `Materialize` re-registers the **same derived** chest ID (§15.1, §6.3).

So: store, take everything, store again, and the simulation throws `InvalidOperationException` inside a command handler. After a save and load, the registry is fresh and the same deposit succeeds. That makes **save-then-continue differ from continue**: one run crashes and the other does not.

The design's own chest test ("store; take; persists", §23) would expose the crash only if it re-deposits. The acceptance scenario (§22 step 8) never empties the chest, so it misses the defect.

**Fix.**
1. A piece chest's record is never removed while its piece stands. In `Take`, add `&& site.InstanceId is null` to the corpse condition. The `InstanceId` init property on `ContainerSite` is the one the design already adds. This matches the documented rule "A changed container stays recorded even if its contents come back" (`WorldDelta.cs:510-511`).
2. Only `RemoveCore` retires a piece chest's `cnt_`: by `DiscardContainer` on dismantle, or by the new spill on destroy.
3. Add these tests:
   - `APieceChest_EmptiedAndRefilled_KeepsOneIdentity`: store, take all, store again, for three cycles, with no exception and one `cnt_`.
   - The same sequence split by a save and load, with `StateDump.Compare` showing 0 differences.

---

## 2. HIGH

### H1. The "pure Domain" validator cannot be compiled where the design puts it (b)

**Evidence.** Section 7.2 places `BuildingRules.Validate`, `PlacementCheck`, `Snap`, `ReferencePoint` and `NavFootprint` in `src/Domain/Building/Building.cs` (§24 step 1). Domain has **no project references** (content_registry §1.1; `src/Domain/Domain.csproj`). Yet these types consume World types:
- `PlacementCheck.Takes` is `ImmutableArray<StackTake>`. `StackTake` is `internal sealed record` in World (`src/World/Runtime/Items.cs:84`).
- "The carried inventory and equipment" means `InventoryEntry`, which is in World (`src/World/PlayerState.cs:18`).
- "Spawners" means `SpawnSite`, which is in World (`src/World/Runtime/Creatures.cs:23`). Spawners are not in `RegionLayout` (`src/Domain/Spatial/RegionLayout.cs:60-98`).
- `Snap(..., IReadOnlyList<PieceView> pieces, ...)` takes `PieceView`, which the design defines as a Simulation view in World (§9).
- `internal interface INavigability` is unusable either way. If it lives in Domain, World cannot implement it (Domain's only friend is `Domain.Tests`, `Domain.csproj:14`). If it lives in World, Domain cannot see it.

**Fix.** Split by dependency. The implementer needs exact homes:
- **Domain (`src/Domain/Building/`)** holds only World-free math: `Lattice` (anchor rules, slot keys), `QuarterTurn` (R_r, box rotation), socket world positions, `Relief(TerrainGrid, square)`, integer box/box and box/circle overlap, and the cost scaling (ceiling and floor), plus the definition records. `Snap` takes `IReadOnlyList<PiecePose>` with `public sealed record PiecePose(EntityId Id, string DefId, long XMm, long ZMm, int Rotation)` defined in Domain. `PieceView` maps to it.
- **World (`src/World/Runtime/BuildingRules.cs`, `internal static`)** holds `Validate(PlacementContext, ...)`, which returns `PlacementCheck` with `ImmutableArray<StackTake>`. Put `PlacementContext`, `INavigability` (internal, World) and `ReferencePoint` here. The preview and the handler both call it from inside World, so the "same function" guarantee (§9) survives.
- Change §24's implementation map to match.

### H2. Nobody owns the moving NPC's persisted state, so step 10 and scope item 5 cannot be built (a, c)

**Evidence.**
- Named NPC bodies are transient ("nothing moves them", `src/World/Runtime/RuntimeState.cs:59-61`).
- `NpcSystem.Populate` puts every NPC back at their `NpcSite` on every construction, load included (`src/World/Runtime/Social.cs:127-138`).
- `NpcSystem.Tick` turns every non-companion NPC back to `npc.Site.FacingMdeg` every tick (`Social.cs:149-163`). That overrides the design's "facing 270° ± 1°" work pose (§22 step 6).
- The design delegates body and route persistence to "the navigation and NPC designs" (§16.3, §26). The navigation drafts disagree about where it goes:
  - `nav_codefit.md:24` says the NPC's route is persisted "in its assignment record", which is the design's piece row.
  - `nav_minimal.md:470` recommends the player section.
- Scope ruling item 5 says the assignment "forces NPC position, assignment and route into the save". Without an owner, step 10 ("reloaded mid-detour. It goes on identically") fails: after load, Kera is back at the smithy.

**Fix.** Building decides, because it defines the assignment:
1. The piece row keeps only `WorkerNpcId`, which is the assignment and is owned by `StateSlice.Structures`. Do **not** put the route on the piece row: `NpcSystem` would then write another system's slice.
2. Add `public sealed record NpcErrand(string NpcId, EntityId? PieceId, long XMm, long ZMm, int FacingMdeg, NpcErrandPhase Phase, NavRoute? Route, int StuckTicks)`, owned by `NpcSystem`. Its phases are `ToWork | AtWork | ToHome`. There is no record at home.
3. Persist it in `player.msgpack` as `npc_errands`. This follows the `CompanionRecord` precedent (`src/World/PlayerState.cs:52`, schema 12), the same bump as the rest of schema 14, and gives a `PlayerRecord.Digest` tag bump to v10. Also run the definition pass on `NpcId`.
4. Split `StateSlice.Npcs` into transient bodies and saved errands.
5. `NpcSystem.Populate` places an NPC with an errand at the errand's pose, not the site.
6. `NpcSystem.Tick`'s wanted facing is the errand facing while `AtWork`.
7. **Authored doors.** Kera's commute passes `door.forge_shed`, which defaults closed (`content/regions/ashen_hollow.yaml:144`). The assignment check calls `Connected(..., doorsOpenable)` (§16.2 refusal 9), which accepts even though, in M7 as written, she cannot open that door. The acceptance script opening the door first hides the problem, and the playable build soft-locks as soon as the player closes the forge door. Rule for M7: an NPC with an errand may open, never close, any authored door within `InteractReachMm`, by dispatching `SetWorldFlag`. The event is `DoorToggled(npcInstanceId, door.Key, true, tick)`.

### H3. The seam proof does not force a path through the structure across a seam, and one claim is geometrically false (a)

**Evidence.**
- The design says Kera "goes through the doorway, crossing x = 100 into cell B" (§22 step 6). The doorway is at x = 105 (`(105000, 100500) r1`), and the work anchor is at (100 750, 103 500). Both are at x > 100, in cells D/B and B. Her only certain seam crossing (A → B, the `c_00_01 → c_01_01` assertion) happens **outside** the building, while she walks around it.
- Inside, she crosses z = 100 only if her route happens to enter the doorway south of z = 100. Nothing forces that.
- RK-14's failure case is a path through a straddling structure from one side to the other. Its validation also fails if "the path exits and re-enters the structure" (`docs/RISK_REGISTER.md:290-292`). The design asserts neither.
- The ROADMAP entry and RK-06 also require **companions** to path in and through (`docs/ROADMAP.md:281`; `docs/RISK_REGISTER.md:154`). The building acceptance never moves a companion.

**Fix.**
1. Put the doorway on the south wall at `(100500, 99000) r0`. Its opening, x 99 700-101 300 at z = 99, straddles x = 100. Keep the anchor at (100 750, 103 500) in B. Kera, coming from the north-west, must then:
   - go **around** the west side (A → C, outside);
   - come **in** through the doorway (C/D);
   - go **through** to the anchor, crossing z = 100 inside the footprint (D or C → B).

   Swap the old doorway edge `(105000, 100500) r1` for a plain wall. The cost is unchanged.
2. Assert that the route between the doorway and the anchor lies entirely inside the footprint union [98 800, 105 200]² and crosses z = 100 once. That is RK-14's "never exits and re-enters".
3. Redo step 7 on the south side:
   - pad (100500, 97500);
   - walls (99000, 97500) r1 and (102000, 97500) r1 are accepted;
   - wall (100500, 96000) r0 is refused.
4. Redo step 9 on the west route:
   - pads (97500, 100500) and (97500, 103500), both inside the area (x ≥ 87);
   - walls (96000, 100500) r1 and (96000, 103500) r1, forming a line at x = 96 from z 98.8 to z 105.2;
   - P3 must then detour.
5. Add a companion beat: the companion in follow mode enters behind the player through the closed door (permitted) and reaches the interior. Repeat it after the reload.

### H4. The navigation "revision" does not cover flag-driven changes, and preview calls can then contaminate authority (c, d)

**Evidence.**
- `StructureSequence` is called "the navigation revision" and "a derived cache keyed by revision is valid across save and load" (§6.2, §19.2).
- Navigability treats barriers "as they stand now" (§18.1). Barriers change by world flag (`SystemContext.IsLifted`, `src/World/Runtime/Systems.cs:45`; `RegionLayout.ClosedDoors`, `RegionLayout.cs:94-97`) without touching `StructureSequence`. Freeing Tavar lifts `barrier.foldscar_fold` (`ashen_hollow.yaml:194-200`).
- Authored-door flags also change NPC passability once H2 lets NPCs open them.
- `PreviewPlacement` is called from presentation (§9) and may run the slow path, and the navigation design is invited to cache (§18.4).

If any cache is filled by a preview call and keyed only by `StructureSequence`, the authoritative command reuses stale labels after a barrier lift. A replay, which never makes preview calls, recomputes fresh. The same command log then gives different accept or refuse outcomes. That is a presentation-authority leak through a cache.

**Fix.**
1. Define `NavRevision = (StructureSequence, FlagEpoch)`. `FlagEpoch` is a transient counter that `WorldFlagSystem` bumps on any door or barrier flag change. Alternatively, `WorldFlagSystem` dispatches `NavigationInvalidated` for door and barrier flags.
2. Make it a written rule that any navigation cache is a pure function of its full key and is never written by a read-only query. It is either computed eagerly inside the command or populated copy-on-write.
3. Add a test: the same command log with and without 1,000 interleaved `PreviewPlacement` calls ends with equal `StateDigest` and equal `(Tick, RejectedReason)` sequences.

---

## 3. MEDIUM

### M1. The replay proof never exercises derived piece IDs under the raw digest (c)
The quicksave is taken "just before step 2" (§22 step 11). All 19 placements happen in step 1, and every later step mints items through `NewId` (a spear in step 4, split stacks and refunds after it). The central claim of §6.3, that replay gives identical `pce_` IDs and an equal raw `StateDigest`, is therefore never tested by raw digest.

**Fix.** Quicksave before step 1. Step 1 mints nothing: `ExchangeItems` spends by decrementing counts (`Items.cs:327-357`), and the chest is not materialised. Replay step 1 from that save and assert an equal raw `StateDigest`, plus the ID list `Derived(Piece, 1..19, owner)`, element for element.

### M2. The slow path runs on the main thread, in the command and in the preview (a, d)
The worst case is 20 ms, and the preview may run it "a few times a second" (§9, §18.4). A 250 mm lattice over 200 × 200 m is 640 000 nodes, so a full labelling pass at 20 ms is plausible but unmeasured.

**Fix.**
- Compute connected-component labels once per `NavRevision`, eagerly, inside the mutating command. At most one pass per structure change is paid at a tick boundary.
- The candidate check is: fast path, else a relabel restricted to the window, with a global pass only if the window proof fails.
- `PreviewPlacement` never runs a global pass. It returns `NavigabilityChecked: false` and the ghost shows amber, not green. The command decides.
- Budget test: median command-time check < 2 ms and worst < 20 ms, measured on the Ashen Hollow grid.

### M3. The fast path has a soundness hole for reference points under the new footprint (c)
The fast path proves only that unblocked cells keep their connectivity (§18.4). It does not prove that a Reach point keeps **some** connected cell within 1 600 mm, or that a Stand point's own cell is not newly blocked. The M7 catalogue cannot trigger this: slots and furniture bounds keep solids off work anchors, and the Bodies check keeps them off bodies. The contract is written for the navigation design to adopt and for M9 reuse, so the hole matters.

**Fix.** Fall back to the slow path whenever the dilated new footprint contains a Stand point's cell, or contains every free cell within `WithinMm` of a Reach point.

### M4. The bench spends finite iron (a)
The bench costs 2 `item.material.iron_ingot` (§5), and its repair is 100% of cost lines, rounded up (§13.3). The world's iron is finite:
- the seam has 3 strikes of 1-2 ore and never respawns (`content/nodes/ore/iron_seam.yaml`; `content/resources/ore/iron.yaml`);
- the den cache holds 3 billets, one-shot (`content/loot/den_cache.yaml`);
- the armour drops one with a 50% chance.

Quest 1 wants a billet and a spear (`content/quests/ashen_hollow/iron_under_ash.yaml:35,40`). One sword blow of damage on the bench then costs a billet to repair. This is a new drain on the economy, which scope item 13 forbids ("No new economy"). The design calls it "the only new item sink".

**Fix.** The bench costs 4 timber and 0 iron. The station kind (`anvil`) and the existing recipe are still reused, as scope item 9 requires.

### M5. The scenario lacks the weapons that steps 8 and 10 use (b)
The start package is 40 timber, 3 billets and 1 haft (§22). The starting kit is a sword, a salve and a flask (`content/config/inventory.yaml`). Step 8 fires a shot, which needs `item.weapon.hunting_bow` equipped and `item.ammo.arrow_rough`. The formula row needs a known `spell.force.impulse_bolt`.

**Fix.** Add a hunting bow, 5 arrows and the known impulse bolt to the scenario's `PlayerRecord`, or move the shot and formula damage rows to their own Application tests.

### M6. Creature bodies should not be navigability reference points (a, c)
Section 18.2 lists "every living creature" as a Stand point. That:
- forbids walling in a hostile wolf, which is a legitimate tactic and unrelated to NPC and companion navigability, D-08's purpose;
- makes accept or refuse depend on where 60 wandering creatures happen to stand, which widens the gap between preview and command.

**Fix.** The Stand points are: authored NPC sites, work anchors, the player body, companion bodies, and the bodies of NPCs with an errand. Spawner protection (check 11) already guards creature homes.

### M7. The melee and shot target rules can pick the wrong blocker (b, c)
- **Melee.** Choosing "the one with the smallest `DistanceTo(body)`" among the crossed blockers (§14.1) is not first-hit along the swing: an axis-aligned box off to the side can be closer to the body than the box the segment enters first.
- **Shot.** "The piece part whose `DistanceTo(stop point)` ≤ 20 mm" can charge a piece when an authored blocker stopped the shot, because pieces may touch authored blockers (check 10 forbids only strict overlap).

**Fix.** Use the slab test's entry parameter, `low` in `BoxBlocker.Crosses` (`src/Domain/Spatial/Blockers.cs:77-91`). Add an internal `double? EntryT(x0, z0, x1, z1)` to `Blocker`.
- Melee picks the minimal `EntryT` over the space plus `ClosedDoors()`. Ties go to authored blockers first, then to the lower piece ID.
- A shot damages a piece only if the minimal-`EntryT` blocker on the segment from the body to (stop + 20 mm along the facing) is a piece part.

### M8. Treating doors as passable is too generous for NPCs without permission (a)
Every reference point is checked with every door passable (§18.1). Only the owner, the owner's companions and assigned workers may open piece doors (§11). An authored NPC site enclosed behind a piece door would pass validation while that NPC cannot get out. The Ashen Hollow build area is at least 17 m from every site, so M7 is safe. M9 reuses the contract.

**Fix.** Put an `OpensPieceDoors` flag on each reference point. NPC sites without permission are checked on the graph with doors closed.

---

## 4. LOW

- **L1 (b). The chest record's host cell.** `Materialize` records `CellOf(site.XMm, site.ZMm)`, the site point's cell (`src/World/Runtime/Items.cs:513-523`), not "the piece's host cell" (§15.1). A chest at r2 on the four-cell square has its site at (100.5, 99.6) in `c_01_00`, while its piece is hosted in `c_01_01`. **Fix:** document the site-point cell as correct (both records are proven independently), and make `TryApplyContainer` also check the derived `cnt_` equals `Derived(Container, pieceSeq, ..., pieceId)`.
- **L2 (b, M9 reuse). "Never on a seam" is region-specific.** 100 000·n ≡ 1 000·n (mod 3 000), so every third 100 m seam (x = 300 m, 600 m and so on) **is** a lattice line in a 2 km region. There, edge anchors lie on seams, and squares abut seams rather than straddle them. Host-cell selection stays deterministic because `CellKey.OfWorld` floors. **Fix:** drop the claim in §3.1, define the host cell as `OfWorld` of the anchor (floor), and have `TryApplyPiece` apply the same rule.
- **L3 (b). Misleading refusal order.** Check 4 (Lattice) includes "an intact doorway **without a door**", so placing a second door is refused as "not on the building grid" instead of by `Slot`. **Fix:** Lattice requires only an intact doorway at that anchor. Slot (`dr:<edge key>`) refuses the second door.
- **L4 (b). Presentation specifics.** `CameraRig.MaxDistance` is `public const float` (`src/Presentation/Player/CameraRig.cs:23`), so build mode needs an instance field. Escape is already `release_mouse` (`src/Presentation/Main.cs:1089`), so "Esc leaves build mode" must take precedence explicitly. B, T, Y, Delete and F2 are free (`Main.cs:1070-1097`).
- **L5 (b). Performance.** `ClosedDoors()` is rebuilt on every `Obstacles()` and `Walled` call (`Systems.cs:48, 82-86`; `Combat.cs:569-570`; `Creatures.cs:893`). Scanning pieces there multiplies the cost. **Fix:** cache the closed piece-door blockers beside `RuntimeState.Space` and rebuild on each door toggle. If `SixtyCreatures_TickWithinTheBudget` (< 4 ms, `tests/Application.Tests/CreatureTests.cs:526-541`) fails with 200 pieces, add a broadphase that **filters, then iterates in the original list order**. `Kinematics.Resolve` is order-dependent (`Kinematics.cs:183-206`).
- **L6 (a). Timber's sale value.** Timber's zero sale value depends on `floor(2 × 0.4)` (`src/Domain/Items/Items.cs:230-231`), which a later retune of `sell_ratio` breaks. **Fix:** add `no_sell: true` (the field exists, `src/Content/ItemContent.cs:115`).
- **L7 (a). One owner question too many.** Building owner question 4 (deadfall or stack) is new, and the M7 list is capped at 5, with slot 5 reserved for factions (scope §5). **Decide it in the design:** keep the renewable deadfall. It is the only thing that makes repair sustainable, and the transition machinery is tested (M3f, M6). Keep the fallback as the recorded alternative. Questions 1-3 are duplicates of the scope list.
- **L8 (b). Spill mutator.** `SpillContainer` needs a new internal `WorldDelta` mutator, for example `ReleaseContainer(key)`, which retires only the `cnt_`. §20.3's mutator table omits it. Add it, and add it to `RuntimeState` behind `Require(owner, StateSlice.WorldItems)`.
- **L9 (b). WLD015's protected-zone test needs spawner discs.** Those are built by `CombatContent` (`SpawnSite`), not `WorldContent`. **Fix:** keep WLD015 to shape and lattice checks, and move the protected-zone overlap to `BLD007` in `BuildingContent`. That runs after Combat in `LoadAll` if it is hooked after `CraftingContent` (`src/Content/ContentLoader.cs:185-197`).
- **L10 (a). Doc reconciliation gaps.** §25 omits:
  - WORLD_ARCHITECTURE §5 and RK-A2 (`docs/WORLD_ARCHITECTURE.md:469`), both named by scope §1;
  - the GAMEPLAY_LOOPS §15 "property threats ... must exist in the same phase" conflict (scope row 8);
  - a note that RK-06's "sub-linear" save growth is met as a linear bound with a small constant, since a row per piece is linear by construction.
- **L11 (a). Sealed rooms can swallow ground items.** A sealed room with no reference point is accepted (§18.5), so it can enclose dropped stacks (created records) and corpses: item loss by building. **Fix:** add each created stack's point as a Reach reference, or refuse a solid part that strictly overlaps a created stack. Otherwise record the loss as accepted.
- **L12 (b). Commute estimate.** Kera's commute is about 90 m, not 75 m: she exits west, rounds the shed, then rounds the workshop. That is about 56 s at 1.6 m/s (`content/config/base_speeds.yaml:7-8`), still inside 1 500 ticks. After H3 the route is longer. **Fix:** assert against the navigation path length divided by speed plus 25%, not a constant.

---

## 5. One storey and terrain honesty (lens d): the design passes

- **No walkable elevated surface.** Body Y is always terrain plus lift (`src/Domain/Spatial/Kinematics.cs:159`), and landing on a blocker pushes the body off (`Kinematics.cs:124-127`). There are no overhang blockers (invariant 2), and roofs have no domain footprint.
- **One unstated case.** The **anvil bench (900 mm) is also below the 1 150 mm jump apex** (`content/config/base_speeds.yaml:15`), so it is jumped like the chest (`Kinematics.Blocks`, `Kinematics.cs:166-167`). Add it to the height-blind note in §12.2 and §26. It is still not a surface.
- **No terrain modification.** Pads are draped by presentation only. The relief figures were recomputed for this review with the exact integer `HeightAtMm` (`src/Domain/Spatial/TerrainGrid.cs:51-64`) over `ashen_hollow.yaml` and all match:
  - worst square 228 mm at (111, 105);
  - the four-cell square 96 mm (6 352-6 448);
  - area range 5 968-6 930 mm;
  - no square over 250 mm;
  - deadfall ground 7 150 mm.
- **Presentation authority.** The ghost, `Snap`, the reticle pick for dismantle and repair, and the Y-key station choice all end in a lattice pose or an ID that the authority re-validates. The roof and door camera colliders are presentation only.
- **The one real leak risk is the cache path in H4.** Fixing H4 closes this lens.

---

## 6. Code claims checked

**Verified true in the snapshot:**
- movement: `Kinematics.cs:159`, `:170-173`;
- blockers and combat: `Blockers.cs:26`, `:40-41`, `:52`; `Combat.cs:495-507`, `:511-517`, `:527-528`, `:534-542`, `:569-570`; `Magic.cs:114`;
- crafting: `Crafting.cs:100-102`, `:158-161`, `:168-170`, `:177-178`, `:188`;
- items: `Items.cs:84-91`, `:327-357`, `:367-374`, `:376-381`, `:459`, `:494-510`, `:513-523`, `:584-593`; `GameSession.cs:91-92` (container reach is the interact reach);
- systems and simulation: `Systems.cs:48`, `:51-52`, `:67` (1 950 = 1 600 + 350), `:82-86`, `:251-273`, `:267-268` (player-only door check); `Simulation.cs:198-206`, `:214`, `:254-255`, `:271-299`, `:357-364`;
- world delta and identity: `WorldDelta.cs:162-200`, `:390-400`, `:489-494`, `:587`; `EntityId` implements ordinal `IComparable` (`EntityId.cs:23, 129-130`), so `SortedDictionary<EntityId, …>` needs no comparer; `EntityId.Create` accepts any 48-bit timestamp (`EntityId.cs:58-69`); `NpcSystem.InstanceIdOf` (`Social.cs:121-125`);
- architecture tests: `ArchitectureTests.cs:61-79`, `:111-131`; `SessionTests.cs:162-178`, which the new command field names pass;
- persistence: `SectionCodec.cs:202-218`, `:478-494`; `SaveLoader.cs:356-358`, `:380-389`, `:469`; `BaselineTransitions.cs:94-117`; `SchemaV8ToV9` writes `EntitiesSectionDto` (`Migrations.cs:534-582`); `GameSession.cs:77`, `:102-117`;
- content: `SchemaResolution.cs:158-164`; `ContentChecks.cs:18-44`; `ContentLoader.cs:185-197`, `:213`; `WorldContent.cs:386-400`, `:456-462`; every `Setup.Layout.Space` read listed in §12.1 (the list is complete for `src/World/Runtime`);
- presentation: `PlayerController.cs:127`; `HollowView.cs:169-170`, `:374`; `Palette.cs` (Wood, Roof, Door, Fold);
- data: every Ashen Hollow coordinate quoted, the anvil and chest rotated boxes, the 19-placement count, the 29-timber total, and the running timber balance.

**False or unverified:**
- the chest emptying path (C1);
- the Domain placement of validator types (H1);
- "through the doorway, crossing x = 100" (H3);
- revision as a sufficient cache key (H4);
- the container host cell (L1);
- "never on a seam" as a general claim (L2);
- "only the chest is jumpable" (§5 of this critique);
- the "75 m" commute (L12);
- `CameraRig.MaxDistance` being adjustable (L4).

---

## 7. What the design must still specify for navigation to be implementable

1. **The exact navigation read contract.** `StructureFootprints` sorted by `(PieceId, Part)`, as specified. Add:
   - `NavRevision = (StructureSequence, FlagEpoch)` (H4);
   - the rule "published after the mutation, before events".
2. **The door-permission predicate as a pure function:** `CanOperate(EntityId operatorNpcOrPlayer, PieceRecord door, State)`, covering owner, the owner's companions, and NPCs whose errand targets a station the owner owns. Also the authored-door rule from H2.
3. **Reference-point assembly:** the ordered list from §18.3, with M6 (no creatures) and M8 (`OpensPieceDoors`) applied, and the fast-path fallback conditions from M3.
4. **The work pose:** world anchor, facing, and the "at work" tolerance of 300 mm and ±1°, plus the `NpcSystem.Tick` facing override (H2).
5. **Seam indifference:** footprints in world mm with no per-cell split. The navigation lattice must have lines on the 3 m module (250 mm does: 3 000 / 250 = 12).
6. **Timing:** navigation is built in the `Simulation` constructor after `_building.Populate()` and before `_npcs.Populate()`, whose errand placement needs it.

## 8. What the design must still specify for persistence to be implementable

1. **The `NpcErrand` record** (H2): fields, owner, `player.msgpack` key `npc_errands`, validation, and a definition pass on `NpcId`. Shared with the faction additions in one 13 → 14 step. **Both** `V13.Player` and `V13.EntitiesSection` are frozen, and both `SchemaV12ToV13` (player) and `SchemaV8ToV9` (entities) are repointed.
2. **`FromSnapshot` order:** apply `StructureSequence` **before** pieces, since `TryApplyPiece` checks the ID timestamp against `StructureSequence`. Then pieces, then containers.
3. **The new `WorldDelta` mutators** in full: `PlacePiece`, `SetPiece`, `RemovePiece`, `SetStructureSequence`, and `ReleaseContainer` (L8). Public readers go on the `WorldDelta_ExposesNoPublicMutation` allow-list.
4. **Container rules:** the piece-chest container host-cell rule (L1), and the derived-`cnt_` equality check in `TryApplyContainer`.
5. **`StructureAudit` additions:** "mount without provider", for pieces whose provider was dropped by the definition pass or rejected at load.
6. **Fixture detail:** v14 fixture piece IDs built with `EntityId.Create(Piece, ts ≤ 9, …)`, so `TryApplyPiece` accepts them against `structure_seq = 9`. Also a foreign-owner piece (a different `chr_`) in one test save, the only way to reach the "not yours" refusals in single player.
7. **The M7 baseline transition test** must cover a v13 save that has both authored-door flags and the waystation chest record in cell A (`c_00_01`), whose `baseline_hash` the deadfall changes.
