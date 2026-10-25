# Wave 1 · WP 1C — save and persistence completion: report

Branch `claude/wave1-1C`, off `origin/playtest/humgen-main` at `117f1b8a` (`e38fcdf4` is its ancestor). There is no merge and no PR into main. The save schema stays at **39**: nothing here needed a section, so no schema-42 commit was made.

## Rows closed

| Row | What was built | Where | Tests |
|---|---|---|---|
| SYS-147 (indicator part) | "Saving…" in the lower right while a save is written in the background, shown at least 0.8 s. A failed save's headline stays there until a later save is committed. | `Ui/SaveIndicator.cs`, `Ui/Models/SaveIndicatorModel.cs`, `GameSession.IsSaving` | `SaveIndicatorModelTests` (4), `SaveExperienceTests.WhileASaveIsWritten…` |
| SYS-134, SYS-148 | Damaged, refused, newer-build, too-old, unreadable, disk-full, file-held and folder-not-writable saves are said in a player's words, each with what to do. The kind is judged by error code down the exception chain. Copy report puts plain text on the clipboard (local only). Open saves folder is next to it. | `Persistence/SaveTrouble.cs`, `SaveOutcome.Trouble`, `Ui/SavesPanel.cs`, `Ui/TitleScreen.cs` | `SaveTroubleTests` (13), `SaveExperienceTests.ASaveOntoAFullDisk…` |
| SYS-151 (delete part) | Delete character from the title's Load list. The confirmation names the character and their save count. The folder is renamed out of the list in one move and then removed. The boot sweep finishes a removal a crash cut short. The character in play is refused. | `CharacterFolders.Delete/SweepDeleted`, `GameSession.DeleteCharacter`, `SavesPanel.Characters` | `SaveTroubleTests.ACharacter_IsDeletedAtomically…`, `SaveExperienceTests.Deleting…`, `…TheCharacterInPlay_CannotBeDeleted…` |
| SYS-114 | `preview.json` (name, level, place, quest followed) is staged and committed with every save the game takes, and shown on its row. `preview.png` is taken with F5, shrunk and encoded off the frame, and attached once the save is committed. Neither is checksummed or read by a load. | `SavePreview`, `SaveStore` (staging, `ReadPreview`, `AttachThumbnail`), `GameSession.Capture/AttachThumbnail`, `SaveIndicator.Picture`, `SavesPanel` rows | `PreviewTests` (3), `SaveExperienceTests.ASave_SaysWhoseItIs…` |
| SYS-143 | Autosaves on a new character's first frame and at a space boundary (at most once every 30 s). Every autosave is deferred while fighting until 10 s after the fight, capped at 60 s. "On waking" is not a trigger: there is no sleep (no time skips). Scoped (sections-only) saves are not built; a boundary takes a whole autosave. | `GameSession.Frame` | `SaveExperienceTests` (first frame, boundary + spacing, combat deferral, the cap) |
| SYS-116 | `savetool save:repair <save-dir> --content-root --worldgen --content-version` loads the save as the game does and commits what survived. The original is kept as `pre_repair_<slot>`. A whole save is left untouched; a corrupt player or manifest is refused. | `SaveStore.Repair`, `SaveToolApp` | `SaveToolTests.Repair_*` (3) |
| SYS-112 | **Removed from the doc, by decision.** The command log is kept in memory and replayed in process, never persisted. A persisted log replays nothing reachable under the MMO rules: the base save is overwritten and the world advances offline unlogged. The existing 200-command replay test stands in for T-28. | `docs/PERSISTENCE.md` §1.3, §3.2, §4.2, §5.7, §10 | `DeterminismAndViewTests.AScripted200CommandSession…` (existing) |
| SYS-113 | `journal.jsonl` is removed from the layout. The Recent list is already derived from quest records and the away log. | `docs/PERSISTENCE.md` §3.2 | — |
| SYS-153 (T-24) | A world that lived a Reckoning (183 days) saves at about 57 KB, inside §9's budget. Its sections are the same size across two saves with nothing changed, and a second Reckoning may add at most 256 KB. | `Soak.Tests/SaveBudgetTests` | `T24_…` |
| SYS-141 (T-25) | The frame's whole share of 200 saves, in a Reckoning-lived world. The median is held to 2 ms everywhere. The P99 is held to 2 ms on RAZER (`OTHERREACH_TARGET_MACHINE=1`) and to a 10 ms regression ceiling elsewhere. | `Soak.Tests/SaveBudgetTests` | `T25_…` |
| SYS-136 | The cloud-sync saves-folder notice on the title, dismissed with "Understood". | `TitleScreen`, `GameSession.CloudSyncRoot` | — (visible; film it) |
| SYS-150 (doc) | `PERSISTENCE.md` §3.2 now shows `characters/<id>/`, `character.json`, `.deleted-*`, `pre_repair_*`, `preview.json`. | `docs/PERSISTENCE.md` | — |

## Not done, and why

- **SYS-115 `orphans.msgpack` / the `quarantine` removal disposition.** It needs a new section (schema 42) and a disposition in `_aliases.yaml`. It also needs a way back for each record kind (carried, in a container, in the world) when a definition returns, and nothing in content is removed to quarantine today. I proposed it as its own schema-42 commit once a system needs it. Its absence costs nothing now, because removed definitions still map, convert or discard as before.
- **SYS-147, manual save create and rename.** This needs the owner's call on DN7 (no manual load points under MMO rules), so it is not built.
- **SYS-151, portrait, level and location per character in Load.** Each save row now shows name · level · place · quest (from `preview.json`). A character portrait waits on the creator's portrait.
- **Pictures for autosaves.** A frame readback on the main thread costs more than the 2 ms budget, and Godot's asynchronous readback is not wired. Only F5 takes a picture.
- **The "game closed while saving" notice (SYS-149 gap)** is not built.

## T-25 measured on the cloud build box (Release, not RAZER)

| | P50 | P99 | max |
|---|---|---|---|
| Whole frame share (`SaveInBackground`) | 1.40–1.67 ms | 3.00–5.18 ms | 5.2–20.2 ms |
| of which `WorldDelta.TakeSnapshot` | 0.64 ms | 1.40 ms | 4.5 ms |
| of which `CaptureRecord` | 0.21 ms | 0.60 ms | 0.69 ms |
| of which the preview (tracked quest, names) | 0.02 ms | 0.05 ms | 0.29 ms |

The P99 here is over 2 ms. Run `OTHERREACH_TARGET_MACHINE=1 dotnet test tests/Soak.Tests -c Release --filter "FullyQualifiedName~T25"` on RAZER to decide whether it passes there. If it does not, the snapshot is the place to cut.

## Seams outside 1C's files (small, for the coordinator to agree)

- `src/Application/GameSession.cs`:
  - `IsSaving`, `FolderOf`, `CloudSyncRoot`, `DeleteCharacter`, `AttachThumbnail`
  - `SaveOutcome.Trouble`
  - the preview in `Capture`
  - the boot sweep of `.deleted-*`
  - the autosave triggers in `Frame` and `Begin`
  - `GameOptions.FirstFrameAutosave` (default on)
- `src/Presentation/Main.cs`: drive the indicator each frame; the failure toast's words.
- `src/Presentation/Main.Ui.cs`: create and bind the indicator.
- `src/Presentation/Main.Saves.cs`: pass a failed load's exception to the saves list; take the picture with F5.
- `tests/Application.Tests/Harness.cs` and `AsyncSaveTests.cs` boot with `FirstFrameAutosave = false`, because they count autosaves and slots from zero. The trigger has its own tests.
- New Godot scripts (`SaveIndicator.cs`) are created in code, not by scenes. They have no `.uid` yet: the editor writes one on first open.

## For the coordinator to film (real time)

1. **"Saving…" on F5.** Press F5 in the hollow; the lower right shows "Saving…" for about a second.
2. **A failed save.** On a profile whose saves folder is read-only (or a full disk), F5 or wait for an autosave:
   - "Not saved: the game may not write to its saves folder." stays in the corner.
   - Open the saves list (L): the failure sentence and Copy report. Paste the report somewhere.
   - Make the folder writable again and F5: the corner clears.
3. **A damaged save.** Flip a byte in a quick save's `player.msgpack`, then launch:
   - The title says "could not be continued. This save is damaged."
   - Load shows the row's sentence, Copy report, and the backups beneath.
4. **A newer-build save.** Edit a save's `schema_version` up and re-sign it (as `SaveTroubleTests` does). The row says "made by a newer version of the game."
5. **Delete character.** With two characters, open Load from the title → Characters → Delete on one:
   - The confirmation names them and their save count.
   - Delete; the list and Continue now show only the other.
6. **Preview and picture.** Press F5 in the hollow and return to the title. Load shows the quick save with its picture and "name · level 1 · place · quest".
7. **First-frame autosave.** Start a new game and quit within 5 s. Relaunch: Continue offers "Autosave 1".
8. **Boundary autosave.** Walk into the Barrowworks threshold: "Saving…" shows at the crossing. Walk straight back out: no second save until 30 s have passed.
9. **Combat deferral.** Fight a wolf when an autosave is due. No "Saving…" during the fight; it comes about 10 s after the fight ends.
10. **Cloud-sync notice.** Point the profile at a OneDrive folder. The title shows the notice once, and "Understood" dismisses it.

## Gates run

All runs below are Release, on the cloud build box.

| Gate | Result |
|---|---|
| Persistence.Tests | 298 passed, 1 skipped (that test was already skipped) |
| Presentation.Tests | 438 passed |
| Soak.Tests | 12 passed, 3 skipped (opt-in long soaks); T-25 run alone: P50 1.67 ms, P99 3.00 ms |
| Application.Tests, save subset (AsyncSave, SaveFailure, SaveExperience, Resume, CharacterCreation, Login, StoryFramework, BarrowworksPersistence, SaveLoad) | 80 passed |
| Full suite at the branch tip | running; the result is added in the next commit |
