# M7 integration handoff

**For:** the owner's integration decision, and the Ashen Hollow visual overhaul (`claude/phase-b-visual-overhaul`, `G:\UNNAMED_PHASEB`).
**From:** M7 (`claude/m7-factions-building`, `G:\UNNAMED_M7`, draft PR #8), 2026-09-26. E0-E10 are complete (`docs/M7_STATUS.md`).
**Status:** nothing is merged, no tag is made and M8 is not begun. The owner merges M7 into `main` and tags the merge `m7` (R-1, not
waived).

## 1. What to integrate

| Item | Value |
|---|---|
| Branch | `claude/m7-factions-building`, cut from `main` at `a696931` |
| The final tested commit | `1278b9cf18e9b9c70a0d4e59f1018b91d1d1080c`: every runtime mode of the M7 design's §13 ran on it (`docs/M7_STATUS.md`, "Final runtime checks"), and CI is green on it (run 36270151122) |
| After it | one commit that adds only this handoff, the final-runs record in `docs/M7_STATUS.md` and the evidence in `docs/acceptance/m7/` and `docs/acceptance/m7_build/`: no code, content or test changes |
| Tests | 1,093, all passing: Domain 186, Application 322, Persistence 214, Content 187, World 72, Presentation 57, EntityRegistry 23, Architecture 32 |
| Content | 0.3.0, 116 definitions, the lint clean |

## 2. Migration and compatibility

- **Saves.** Schema 14 → 17 (§`docs/PERSISTENCE.md`):
  - 15 adds the faction ledger, companion routes, placed pieces, the structure sequence and NPC errands;
  - 16 adds the character's vitals (the owner's first E8.5 ruling);
  - 17 adds a creature's attack in progress (the second).
- **Older saves.** Every older save migrates through the commit path: the committed fixtures v1-v17 do, and so does the M6 acceptance
  save (12 → 17, five steps, nothing built, every faction neutral, no errand). A migrated save loads "rested" (schema 16) and "with no
  attack in progress" (schema 17): the timing it never kept cannot be reconstructed.
- **Forward compatibility.** A save written by M7 (schema 17) does not load in a build before M7; the loader refuses a newer schema.
  `SaveFormat.Current` is unchanged, and no section file is added.
- **Digests.** `unnamed.player/v11`, `unnamed.effective-cell/v4`, `unnamed.simulation/v3`. The worldgen fingerprint is unchanged, so
  M7 regenerates the same baselines.
- **Content-sensitive tags.** `EntityId.Derived`'s `unnamed.piece/v1` and `unnamed.piece-container/v1` re-key every placed piece and
  piece chest if changed (`docs/DATA_MODEL.md` §6).
- **The SaveTool** (L-01, unfixed since Phase 1): `save:migrate --dry-run` reports some real saves as blocked that the game loads. The
  game's own load is the check of record.

## 3. Runtime evidence

- **Final runs on `1278b9c`** (ASTRAL):
  - smoke and boot;
  - `--build-shots` twice and verify: 19 beats, 0 differences, the replay byte-identical to E9's;
  - `--playthrough` twice and verify: 0 differences, the replay byte-identical to E8's and E9's;
  - `--ui-shots`, `--delta-shots`, `--input-check`, and `--layout-check` at 1366x768 and 1280x720;
  - the seam recording and the ASTRAL perf trial.
- **Where the evidence is.** In the repository: `docs/acceptance/m7/` and `docs/acceptance/m7_build/`. Outside it:
  `G:\UNNAMED_HISTORY\M7_EVIDENCE\` holds the videos of both seam recordings and both perf captures.
- **Budgets.** Every line of criterion 33 is met in Release on ASTRAL. The budget-sheet placement and save lines are met as the owner's
  E10 ruling measures them: `docs/M7_STATUS.md`, "Budgets on ASTRAL", with the original distributions kept in the STOP records.
- **Owed.** M7's RAZER capture of the `building` segment, when the owner has a window (not a gate).

## 4. Files M7 shares with the visual branches

Recomputed on 2026-09-26 with read-only git at the local and pushed refs, which agree:
- `claude/phase-b-visual-overhaul` at `df5362f` (2026-09-26 14:19);
- `claude/pb-hud` at `19c95e3`;
- `claude/pb-vfx` at `916e678`.

Both lanes are ancestors of the visual branch, so integrating it covers them. Conflicts were computed by an in-memory three-way merge
(`git merge-tree`) of M7's head against each branch from `a696931`.

| File | M7 | The visual branch | Merge |
|---|---|---|---|
| `src/Presentation/Main.cs` | input actions and the build branch of `ReadInput`; build mode in `Draw`; event subscriptions (the worker's toasts and log lines among them); `Describe` for `pce_` keys; `OpenStation` and `KeepContainerInReach`; Y's `WorkOrder`; the `--build-shots` / `--build-shots-verify` modes and S0's load; `sim_ms` around `GameSession.Frame` | the showcase mode, `--realtime` playthroughs, the tick count per frame, its HUD and art wiring | **5 conflict blocks.** Keep both: the new-game and load branches need both M7's build-shots condition and the showcase's; the frame loop keeps M7's stopwatch and its one-tick-a-frame list (build shots included) with the visual branch's `--realtime` exception and `_ticksLastFrame`; both subscriptions stay |
| `src/Presentation/Ui/Hud.cs` | `SetBuild`, `BuildPanel`, the `Toasted` event | `SetSheet`, the production view's toast | **2 conflict blocks.** Keep both APIs; a toast must still raise `Toasted` (the scripted checks read it) and reach the production view |
| `src/Presentation/Greybox/Palette.cs` | five additive materials: `GhostAllowed`, `GhostUnchecked`, `GhostRefused`, `Highlight`, `AreaOutline` | additive materials | merges cleanly |
| `src/Presentation/Playthrough.cs` | the six faction beats and `m7_build` | takes a container's stacks last-first | merges cleanly; the take order changes the playthrough's take rows, so its transcript must be re-baselined |
| `content/regions/ashen_hollow.yaml` | `container.timber_stack` at (84, 118) and `build_area.hollow_crossing` [87, 114]² | 107 authored trees (`tree_33` onward, circle blockers 0.4 m, 7 m tall) in clusters away from the crossing | merges cleanly as text. **Semantically it is gameplay-facing:** authored structures are collision, navigation and sight inputs. They are not in the worldgen fingerprint or the cells' baseline hashes, so saves stay compatible; only the content hash changes. No tree stands in the build area, at the timber stack or at T2's resume pose |

M7 does not touch `src/Presentation/Art/**`, `Audio/**`, `HollowView`, `CraftingView`, `ItemsView` or `NpcsView` (M7 design §9.1).
`NpcsView` poses every NPC from `npc.Body` each frame, so Kera walking to work and home renders on either branch unchanged.

## 5. Contracts the visual branch builds against

**Input actions** (all in `Main.DefineInput`; an Architecture test pins every binding):

| Action | Key | When |
|---|---|---|
| `build_mode` | B | toggles build mode |
| `build_piece_1` … `build_piece_7` | 1-7 | in build mode only; outside it 4-6 stay the formula keys and 1-n answer in a conversation |
| `build_piece_next` / `build_piece_prev` | PageDown / PageUp | in build mode |
| `build_rotate` | R | in build mode |
| `build_place` | Left mouse | in build mode, mouse captured |
| `build_dismantle` | Z or Delete | in build mode, press twice |
| `build_repair` | T | in build mode |
| `build_debug` | F2 | the structure and navigation debug overlay |
| `faction_debug` | F6 | the faction debug panel |
| `work_order` | Y | outside build mode: ask the NPC in focus to work at a station of yours, or let them go home |

Build mode suppresses the combat keys, caps the camera at 9 m, shows the build-area outlines, and ends when any modal panel opens or the
character is down; Esc leaves it before freeing the mouse. `Main.Modal` is unchanged by M7. The inventory request (a movement key
closing non-critical panels) is not implemented: no owner ruling authorizes it.

**What presentation reads:**
- Views: `Simulation.Pieces` (`PieceView`, with `WorkerNpcId`), `Stations`, `Containers`, `StructureRevision`, `StructureFootprints`,
  `Navigation` (the grid, gates, movers with errands, counters), `WorkAssignments`, `Factions`, `Acts`, and `PreviewPlacement(...)`.
- Events: `PiecePlaced`, `PieceRemoved`, `PieceDamaged`, `PieceDestroyed`, `PieceRepaired`, `StructuresChanged`, `NavigationRebuilt`,
  `RoutePlanned`, `DoorToggled`, `ActRecorded`, `FactionLearned`, `ReputationChanged`, `WorkerAssigned`, `WorkerReleased`,
  `NpcArrivedAtWork` and `NpcReturnedHome`.
- Views rebuild on the next frame, keyed on `StructureRevision`. `FrameStats.Record` takes a `FrameProbe` (`sim_ms`, `ticks`,
  `save_ms`), appended after the nine CSV columns.

## 6. Tests a combined build must pass

1. `dotnet test src/UNNAMED.sln` (1,093 on M7 alone, plus the visual branch's) and the content lint.
2. The layout-sensitive M7 tests, re-run and read, because the trees change collision and navigation:
   - N-A10 `Navigation_StaysWithinBudget`: every authored pair `Found`, and Kera's three routes within two thirds of the cap;
   - `CrossingWorkshop_*`, N-A2 to N-A6 and N-A8;
   - the errand tests and T2 (the crowd's spawn homes are recomputed against the new blockers);
   - `TwoHundredPieces_RoundTripAndStayNavigable`.
3. `--smoke`, `--quit-after 300`.
4. `--build-shots` and `--build-shots-verify`: every beat, 0 differences, v3 on b20's tick, a second run byte-identical. The replay's
   hash will differ from M7's alone if the trees change any route.
5. `--playthrough` and `--playthrough-verify`, with the transcript re-baselined for the take order and any route the trees change.
6. `--input-check`: the build actions through the real input path, every panel and L ending build mode, the Esc order, and the recapture
   click never placing.
7. `--layout-check` at 1366x768 and 1280x720: the build panel and F1's BUILDING section beside the production HUD style.
8. `--ui-shots` and `--delta-shots`.
9. The perf run with the `building` segment, and M7's RAZER capture of it.
10. Piece art: M7 adds no `pieces` art binding, so every piece stays greybox and is reported as a `piece:*` fallback, never allowlisted.

## 7. Observed in M7's runs, for the visual branch

- The greybox interiors under placed roofs are dark (b09, the same from E7): lighting under a roof is a presentation matter.
