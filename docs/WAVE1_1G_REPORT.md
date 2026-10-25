# Wave 1 WP 1G: building and property

This is the report on Wave 1 work package 1G, which re-ports the M7 building stack onto `playtest/humgen-main`.

- **Branch:** `claude/wave1-1G`, cut from `origin/playtest/humgen-main` at `e38fcdf4`. It is pushed, not merged, and has no PR.
- **Source:** `origin/claude/m7-factions-building` at `1278b9c`, the last M7 code commit. Its tip `d86654ae` adds only evidence and the audit.
- **Spec:** `docs/SYSTEMS_CHECKLIST.md` on `origin/docs/systems-checklist`, Wave 1 row 1G.

## Commits

Each commit is one coherent change, made pathspec-only.

1. **`WP-1G: re-port M7 building onto humgen-main (rules, runtime, nav hooks)`**
   - The domain lattice, sockets and snapper.
   - Piece content, `config.building`, the timber stack and the build area.
   - The BLD lints.
   - `BuildingSystem` and its placement rules.
   - Damage, repair and dismantle; chests and doors.
   - Navigation restamping and NPC door opening.
   - The tests.
2. **`WP-1G: build mode, the placed pieces drawn, and the build panel`**
   - Build mode, the structures greybox and the build panel on the HUD.
   - The key bindings and the help panel.
3. **`WP-1G: the content loader's ID list names building's definitions`**: the loader test's list of every pack ID.
4. **`WP-1G: report`**: this file.
5. **`WP-1G: placed pieces in the save (schema 44)`**: the final commit.
   - This is the one commit to renumber at merge (see [Renumbering at merge](#renumbering-at-merge)).
   - It adds the save section, the migration, the fixture, the loader's definition pass, baseline proof and rebasing for pieces, and the save tests.

## Row status

| Row | Status | What landed | What is left |
|---|---|---|---|
| WLD-80 Player building | **Done** | <ul><li>Socket/snap on the 3 m lattice, with quarter turns.</li><li>The ghost preview (`Simulation.PreviewPlacement`, the same 15 ordered checks the command runs).</li><li>Placement, repair (cost scaled by health missing) and dismantle (50% refund).</li><li>`src/Domain/Building`, `src/World/Runtime/Building*.cs` and `BuildingRules.cs`.</li><li>`Player/BuildMode.cs`, `Greybox/StructuresView.cs` and `Ui/BuildPanel.cs`.</li></ul> | Art (see [Visuals](#visuals)). |
| WLD-81 Navigation rebuild on placement | **Done** | <ul><li>Placed solids are grid inputs, and placed doors are gates.</li><li>Each footprint change restamps the touched tiles through `NavGrid.With` and publishes `NavigationRebuilt`.</li><li>Movers replan on the window stamp.</li><li>NPCs open placed doors (and never close them).</li><li>Tested with a house straddling four cells (RK-A2).</li></ul> | Nothing. Restamps are per change, not debounced per cell: one restamp per committed edit is cheap. |
| WLD-82 Pieces as content, taught by Berrin | **Done** | <ul><li>The seven pieces carry their data in the existing `content/pieces/*` files, with the same IDs and paths.</li><li>`TeachingTests` (10) pass, including `Berrin_TeachesTheBuildingPieces_EachAfterItsPrerequisite`.</li></ul> | Placement does **not** require the piece to be known. M7 had no such gate, so I did not invent one (see [Decisions for the owner](#decisions-for-the-owner)). |
| WLD-83 Quest/faction hooks | **Seam ready, hook not wired** | `PiecePlaced`, `PieceRemoved`, `PieceDestroyed` and `StructuresChanged` events carry the piece, its definition, the actor and the tick. | The `construct_building` objective and the `piece_placed` act live in Quests and Factions. Those are 1F's files and still map to `NotBuilt` there. 1F (or the merge) subscribes to `PiecePlaced`. |
| WLD-84 Chests and stations; persistence | **Done** | <ul><li>A chest is a 12-stack container whose identity derives from its piece.</li><li>It spills its stacks, keeping their IDs, when destroyed.</li><li>It refuses take-down while it holds anything.</li><li>A bench is an anvil station for crafting.</li><li>Pieces are saved as rows anchored to their host cell, proven by `baseline_hash`, at schema 44.</li></ul> | Nothing. |
| CAN-701 Building config | **Done** | `content/config/building.yaml` holds M7's numbers, validated by BLD006. | Nothing. |
| CAN-702 Piece schema | **Done** | Family, slot, rotations, bounds, parts, sockets, cost, health and `supports_roof`, linted by BLD001-BLD004. | "Owner-only door" is dropped (see [Decisions for the owner](#decisions-for-the-owner)). |
| CAN-703 Station and container pieces | **Done** | The anvil's `station`, and the chest's `container` with 12 slots that spills on destruction. | "Owner-only" chest access is dropped (see [Decisions for the owner](#decisions-for-the-owner)). |
| CAN-704 Protected zones and build areas | **Done as M7 had it** | <ul><li>The protection margins.</li><li>`build_areas` with `max_pieces`; there is one area, M7's crossing moved with the hollow.</li><li>The area check, BLD007 (lattice, clear of authored geometry and protected zones) and BLD009 (ceilings).</li></ul> | OPEN-25's B1-B4 (free building in the wild, a 6 m road buffer, town zones) is not confirmed, so build areas remain the mechanism (B1-compatible). |
| CAN-705 NPC works at a station | **Deferred** | Nothing. | M7's errand path (`works_at`, `AssignWorker`) is tied to M7's NPC model, which humgen-main replaced with routines and `NpcMover`. It belongs with the WP15a hirelings. The BLD005 `works_at` lint was dropped with it. |
| CAN-706 Timber stack | **Done** | `container.timber_stack` (80 timber) stands 5 m from the area's corner. | Nothing. |

## Gates

| Gate | Result |
|---|---|
| BLD lints | `BuildingContentTests` pass: 3 tests covering BLD001-BLD009 and WLD017, each with a crafted bad file. The content lint passes on `content/` (`lint --content-root content`: 0 errors). |
| Replay test | `BuildingReplayTests.ABuildingSession_ReplaysFromItsLog_ToTheSameWorld` passes. A session builds the house across the corner, then hangs and works a door, takes one refusal, deals blows, mends, takes down a piece, and places and fills a chest. Its command log, replayed into a second world from the same start, gives the **same raw `StateDigest`**, rows, IDs, refusals at the same ticks, nav grid and chest. |
| NavigationTests across four cells | `NavigationTests` passes 2 tests. <ul><li>A house over the corner of c_04_06, c_05_06, c_04_07 and c_05_07 restamps every cell's tile.</li><li>It is planned around and entered through its doorway, and only by door-openers while the door is shut.</li><li>Taking it down returns the bare grid.</li><li>Kera, whose place was moved inside, walks out of her shed, across the seams and in, opening the placed door.</li></ul> |
| TeachingTests | 10 of 10 pass. |
| Full Release suite | See [Full Release suite](#full-release-suite). |

The other building tests are M7's, re-run on the new area:

- `BuildingTests`: 19 tests.
- `BuildingSaveTests`: 8 tests.
- `BuildingRulesTests` (domain): 8 tests.
- `PieceSaveTests` (persistence): 4 tests.
- The v44 fixture.

### Full Release suite

`dotnet build src/UNNAMED.sln -c Release`, then `dotnet test src/UNNAMED.sln -c Release --no-build`:

The build passes and **every test passes**:

| Project | Passed | Skipped |
|---|---|---|
| EntityRegistry.Tests | 23 | 0 |
| Domain.Tests | 432 | 0 |
| Architecture.Tests | 23 | 0 |
| Persistence.Tests | 284 | 1 |
| World.Tests | 90 | 0 |
| Presentation.Tests | 434 | 0 |
| Soak.Tests | 10 | 3 |
| Content.Tests | 364 | 0 |
| Application.Tests | 706 | 0 |

The 4 skips are the base's own: 1 in Persistence.Tests and 3 in Soak.Tests.

The first full run found two expectation lists that did not know about building. Both are fixed, and the run above is on the final tree.

- The content loader's list of every pack ID now includes `config.building` and `loot.timber_stack`.
- The M6 acceptance save's list of fields added since schema 12 now includes the empty `Pieces` and `StructureSequence` 0.

The content lint (`lint --content-root content`, Release) reports 0 errors.

## Changes outside 1G's own files

Building code lives in its own files: `Building*.cs`, `BuildingRules.cs`, `WorldDelta.Pieces.cs`, `SectionCodec.Pieces.cs`, `SaveLoader.Pieces.cs`, `Migrations.Building.cs` and `Main.Building.cs`. The shared files below got a `partial` keyword plus one-line calls, except where noted.

- **Domain**
  - `EntityKind.Piece` (`pce`) and `EntityId.Derived`, both from M7.
  - `RegionLayout.BuildAreas` and `BuildAreaSite`.
- **World**
  - `WorldDelta`:
    - The class is now `partial`.
    - `AddPieces` joins the effective cell digest, only when a cell has pieces.
    - The snapshot, the load and the chest identity check are one line each.
  - `RuntimeState`: now `partial`, with the `Structures` slice.
  - `SystemContext`:
    - Now `partial`.
    - `SpaceFor` adds placed solids outdoors.
    - `ClosedDoors` adds shut placed leaves.
    - `FindContainer` adds placed chests.
    - `Walled` reads the built space.
  - `Simulation`: now `partial`, with `Setup.Building`, the slice claim, `Populate`, and routing for place, dismantle, repair, damage, door, chest and spill.
  - `Combat.cs`: a swing that strikes no creature may strike a piece instead of missing.
  - `Navigation.cs`: piece inputs, gates, `Restamp`, and a `CheckEdit` overload.
  - `NpcMover`: opens placed doors.
  - `Crafting` and `Party`: use `StationSites()`, which adds placed benches.
  - `Items.cs`: `WorldPosition` changed from private to internal.
  - Collision reads moved from `Setup.SpaceFor` to `SpaceFor` in Creatures, Companions, Pets, NpcMover and Systems. Terrain-only reads and creature home placement stay on the authored space (M7's guard G9).
  - `Creatures.SpaceNear`: its cache refreshes when the structure sequence moves.
  - `Interaction`: E on a placed door.
- **Application**
  - `GameSession` builds `Setup.Building`.
  - `PlayerMotion` predicts against `ActiveSpace`.
  - `StateDump` lists pieces and masks piece site keys (M7's normalisation).
- **Content**
  - `ContentLoader` runs `BuildingContent.Validate`.
  - `WorldContent` parses `build_areas`, linted as **WLD017**. WLD015 is the portals' code on this base.
  - `content/regions/starting_frontier.yaml` gains the build area and the timber stack.
- **Persistence**
  - `SaveFormat` gets `SchemaVersion = 44` and `ReservedSchemas`.
  - `Migrations`: the production list, and the 37 -> 38 step repointed at the frozen `Sections/V43`.
  - `SectionCodec`: two keys on the entities section, plus encode.
  - `SaveLoader`: the decode, the definition pass and the baseline proof.
  - `BaselineTransitions`: rebases pieces.
- **Presentation**
  - `Main.cs`: three calls.
  - `Main.Input.cs`:
    - Build mode owns the left button and the digits while on.
    - Esc leaves build mode first.
    - The key bindings.
  - `Main.Ui.cs`: placed benches open as stations.
  - `PlayerController`: placed doors, chests and benches as focus targets.
  - `CameraRig`: a zoom cap, which goes to 9 m in build mode.
  - `HelpPanel`: the building keys.
- **Tests**
  - Architecture allow-lists:
    - `WorldDelta.Piece`, `PiecesIn`, `Pieces` and `StructureSequence`.
    - `Simulation.PreviewPlacement`.
  - Persistence and Soak tests skip the reserved schemas 40-43 when iterating fixtures.
  - `CanonicalState` renders pieces.
  - M2.Probe writes v44 and mirrors fixture content 0.2.10.

## Decisions for the owner

1. **Property is a plain record.** Crime, law and justice are deferred, so no rule asks who may enter or take:
   - Anyone opens a placed door; NPCs open them in their way and never close them.
   - Anyone may store in or take from a placed chest.
   - M7's owner-only door and chest rules are therefore **not** ported.
   - Only the owner may dismantle or mend their own piece. That is ownership, not trespass, but it is a one-line change each way if the owner wants it otherwise.
2. **Build areas, not free building.** OPEN-25 (B1-B4) is unconfirmed, so building stays inside content-defined areas, as in M7. There is one area: M7's crossing, moved with Ashen Hollow (+399 m, +600 m), on the four-cell corner at (500 m, 700 m) and clear of the road.
3. **Learning a piece is not required to build it.** M7 had no such rule. With Berrin teaching pieces, gating placement on `Progression.Known` is a small rule (one check, one test). I left it for an owner ruling rather than invent it.
4. **Keys.** T is chat and Z frees the cursor on this base, so:
   - Mend is **N**, not M7's T.
   - Take-down is **Delete or Backspace twice**, not Z or Delete.
   - The rest is M7's: B, 1-7, PageUp/PageDown, R, the left mouse button, and Esc.
5. **G20, stack order, is upheld for building's own spends only.**
   - Placement and repair spend from the smallest stacks, whatever their IDs.
   - A take-down's refund is a `GrantItem`, which joins a stack by the inventory's own merge rule. That rule depends on item-ID order on this base.
   - Replays are unaffected, because IDs are minted in order. A world built from hand-chosen IDs can differ.
   - The fix belongs in `Items.cs` (M7's "fullest partial stack"), which is not 1G's file.

## Renumbering at merge

Schema 44 is 1G's reserved slot. Schemas 40-43 belong to the other Wave 1 packages. Until they merge, each is a `ReservedSchemaStep` that carries a save unchanged and has no fixture (`SaveFormat.ReservedSchemas`).

At merge:

1. Replace each `ReservedSchemaStep(n)` with that package's real step.
2. Empty `ReservedSchemas`.
3. If 1G's slot moves, renumber `SchemaV43ToV44`, `Sections/V43`, the v44 fixture (rewrite it with `M2.Probe fixture`), the README row and `docs/PERSISTENCE.md` §14.

The frozen entity envelope `Sections/V43` must then match the entity shape at the schema just before 1G's, including any keys 40-43 add.

## Visuals

The placed pieces and the ghost are drawn by M7's greybox kit: flat wood, roof, door and iron materials from `Greybox/Palette`, translucent ghost tones, and a build-area outline. No piece art exists yet. "Ultra-first" for building needs modelled timber pieces (pad, wall, doorway, door, roof, chest, anvil bench) bound through the art pipeline, which is not 1G's.

I could not run Godot in the cloud container, so none of the build mode has run in the engine:

- the headless smoke;
- the boot check;
- a windowed run.

It compiles, and `Presentation.Tests` (434) pass.

## What the coordinator should film (the build video)

Run windowed on RAZER at Ultra.

1. **Getting there.** Walk from the spawn east to the Hollow crossing (around region (501, 702)). Take the timber stack (E) at (483, 718).
2. **Build mode.**
   - Press **B**: the build panel appears at right, the camera eases out to 9 m, and the area outline shows.
   - Press **Esc**: it leaves build mode. Press **B** again.
3. **The ghost.** With the pad (1), sweep the ghost:
   - green and "can be built here", with its cost;
   - red with a reason outside the area ("you may only build inside a build area");
   - red too far away ("... m away; building reach is 6.00 m").
4. **The house across the corner.** Lay four pads over the corner at (500, 700), so the walk crosses the cell seams.
   - Rotate with **R**.
   - Put up walls (2), a doorway (3), a door hung in it (4), and roofs (5).
   - Try to close a one-square hut with no door: it is refused with "rooms need a doorway".
5. **Bodies.** Walk into a wall: it stops you. Open the placed door with **E**, walk in, and shut it behind you.
6. **Damage and mending.**
   - Leave build mode and swing at a wall: its health drops ("Timber Wall 190/200" in build mode's target line).
   - Mend it with **N**: timber is spent.
   - Swing at a doorway until it falls: its door goes with it.
7. **The chest.** Place a chest (6) and put items in it. Break it: the items spill on the ground and can be picked up.
8. **Take-down.** Press **Delete** twice on a wall to take it down, with half its timber back.
9. **An NPC and the door (optional).** An NPC whose path crosses the house opens the placed door. This is staged only in `NavigationTests`.
10. **Save and reload.** Quicksave (F5), quit, and load: the house stands exactly as built, with the door state kept.
11. **Frame rate.** Show the FPS overlay while building about 50 pieces.
