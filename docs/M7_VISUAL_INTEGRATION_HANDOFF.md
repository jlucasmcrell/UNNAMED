# M7 → visual branch: shared interfaces and changed contracts

**For:** the Ashen Hollow visual overhaul (`claude/phase-b-visual-overhaul`, `G:\UNNAMED_PHASEB`).
**From:** M7 (`claude/m7-factions-building`, `G:\UNNAMED_M7`), 2026-09-26, pushed through E9 (the commits are in `docs/M7_STATUS.md`); E10
still to land. E10 recomputes the shared files below against the visual branch and names the exact tested commit.
**Status:** M7 is not complete and nothing here is merged. This is the contract the visual branch can build against, and the merge
plan. Nothing in it asks the visual branch to wait for M7.

## 1. Files both branches change

Computed from the two pushed branches against `origin/main` (plus M7's unpushed E8.5 work):

| File | M7 | Expect |
|---|---|---|
| `src/Presentation/Main.cs` | input actions and the build branch of `ReadInput`; `BuildFrame` and the build panel in `Draw`; event subscriptions (§8.10), the worker's toasts and log lines (E9) among them; `Describe` for `pce_`, `container.pce_…` and `station.pce_…` keys; `OpenStation` reading `Simulation.Stations`; `KeepContainerInReach` closing a vanished bench's panel; Y's `WorkOrder` and the talk prompt's Y suffix (E9); the `--build-shots` / `--build-shots-verify` scripted modes | a textual merge conflict; resolve by keeping both sets of additions |
| `src/Presentation/Ui/Hud.cs` | `SetBuild(panel, status, target, BuildTone)`, `BuildPanel`, the `Toasted` event (scripted checks read toasts) | a textual merge; the visual `hud=production` style must also style the build panel and the target line |
| `src/Presentation/Greybox/Palette.cs` | five additive materials: `GhostAllowed`, `GhostUnchecked`, `GhostRefused`, `Highlight`, `AreaOutline` (translucent, emissive, cull-disabled, no shadows) | additive on both sides |

M7 does not touch `src/Presentation/Art/**`, `Audio/**`, `HollowView`, `CraftingView`, `ItemsView` or `NpcsView` (M7 design §9.1).
`NpcsView` already poses every NPC from `npc.Body` each frame, so Kera walking to work and home renders on either branch without a
change there.

## 2. Input actions M7 adds (all in `Main.DefineInput`; an Architecture test pins every binding)

| Action | Key | When |
|---|---|---|
| `build_mode` | B | toggles build mode |
| `build_piece_1` … `build_piece_7` | 1-7 | in build mode only: choose a piece. Outside it 4-6 stay the formula keys and 1-n answer in a conversation; build mode suppresses the formulas |
| `build_piece_next` / `build_piece_prev` | PageDown / PageUp | in build mode |
| `build_rotate` | R | in build mode |
| `build_place` | Left mouse | in build mode, mouse captured |
| `build_dismantle` | Z or Delete | in build mode, press twice |
| `build_repair` | T | in build mode (E8.5) |
| `build_debug` | F2 | the structure and navigation debug overlay |
| `faction_debug` | F6 | the faction debug panel |
| `work_order` | Y | outside build mode (E9): ask the NPC in focus to work at a station of yours, or let them go home |

Build mode suppresses the combat keys (`fighting = captured && !_build.Active`), caps the camera at `CameraRig.BuildMaxDistance` (9 m;
`CameraRig.Cap` restores 6 m on exit), shows the build-area outlines, and ends when any modal panel opens or the character is down.
Esc leaves build mode before it frees the mouse. `Main.Modal` (inventory, dialogue, character sheet, saves) is **unchanged** by M7.

## 3. The inventory request (movement closes non-critical panels)

Not implemented. `docs/phase_b/M7_INPUT_COORDINATION.md` hands M7 the request that a movement key close the inventory, character sheet
and journal and move the body in the same frame, while a conversation, the saves list, a death recap or a confirmation keep blocking.
The owner's instruction is to implement it only if an existing owner ruling authorizes it. The normative M7 design does not contain it,
and no owner ruling authorizes it, so M7 leaves `Main.Modal` and the modal early return in `ReadInput` exactly as Phase A had them. If
the owner authorizes it, it belongs in `ReadInput` (M7 owns input), with the suggested `--input-check` step.

## 4. What presentation reads from the simulation (new in M7)

- Views: `Simulation.Pieces` (`PieceView`: definition, pose, owner, health, door state, bounds, parts, `ContainerKey`, `StationKey`),
  `Stations` (authored, then each placed bench), `Containers` (placed chests included), `StructureRevision`, `StructureFootprints`,
  `Navigation` (grid, gates, movers, counters), `Factions`, `Acts`, `PreviewPlacement(...)`.
- Views (E9): `Simulation.WorkAssignments` (`WorkAssignmentView`: the NPC, the station, its work anchor and facing, the phase);
  `PieceView.WorkerNpcId`; `Navigation.Movers` includes errands.
- Events: `PiecePlaced`, `PieceRemoved`, `PieceDamaged`, `PieceDestroyed`, `PieceRepaired`, `StructuresChanged`, `NavigationRebuilt`,
  `RoutePlanned`, `DoorToggled` (a placed door's key starts `pce_`), `ActRecorded`, `FactionLearned`, `ReputationChanged`, and (E9)
  `WorkerAssigned`, `WorkerReleased`, `NpcArrivedAtWork`, `NpcReturnedHome`.
- Views rebuild only on the next frame, keyed on `StructureRevision` (never inside a handler).
- `PlayerController.FocusOn` now also offers placed doors, placed chests (named by their piece) and placed benches.
  `PlayerController.Repair`, `Assign` and `Release` submit the E8-E9 commands.
- F2's navigation stage (`NavigationOverlay`) draws routes for every mover and, from E9, each work anchor as a post with a facing stroke.

## 5. Save format

Schema 17 (the owner's ruling on the second E8.5 STOP): a creature record keeps its ordinary attack in progress (`continuation.attack`).
Schema 16 (the first E8.5 ruling): the player section gains `vitals`. Schema 15 added the faction ledger, companion routes, placed
pieces, the structure sequence and NPC errands (written from E9). Every older save migrates; nothing on the visual branch reads or
writes save data, so there is no overlap. A combined build must use M7's `src/Persistence` whole.

## 6. Combined-build tests (when the owner integrates)

`dotnet test src/UNNAMED.sln`; the content lint; `--smoke`; `--quit-after 300`; `--input-check` (build actions through the real
input path, every panel and L ending build mode, Esc order, the recapture click never placing); `--layout-check` at 1366x768 and
1280x720 (the build panel and F1's BUILDING section beside the production HUD style); `--ui-shots`; `--delta-shots`; `--playthrough`
and `--playthrough-verify`; `--build-shots` and `--build-shots-verify` (E9: b11-b13 and b20, Kera's walk in and home, and verify v3,
her arrival home on the same tick); the perf run with the `building` segment (E9: Kera asked and let go). Piece art: M7 adds no
`pieces` art binding, so every piece stays greybox and is reported as a `piece:*` fallback, never allowlisted.

## 7. Observed in M7's runs, for the visual branch

- The greybox interiors under placed roofs are dark (b09, the same at E7 and E8): lighting under a roof is a presentation matter, not
  an M7 change.
