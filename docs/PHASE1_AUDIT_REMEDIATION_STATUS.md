# Phase-1 technical-audit remediation - status

The fixes for `PHASE1_INDEPENDENT_TECHNICAL_AUDIT_2026-09-24.md` (the independent technical review of Phase 1; kept with the project history, not in this repository). This is not M7. Branch `claude/phase1-audit-fixes`, from `main` at `e10d2c4`, in its own worktree; the asset-remediation work goes on separately. The IDs below are the audit's.

## Fixed

| IDs | What changed | Commit |
|---|---|---|
| C-01, L-19 | Iron Under Ash: ore already smelted into a billet or a spear still counts (`acquire_item` takes `or_item_refs`); the whole quarry floor discovers Blackvein Cut; lint QST001 refuses an acquisition nothing renews; the quest debugger names ground items and only recipes the character can work | `4ed7e85` |
| H-01, L-18 | Hearing a line is not receiving it: Sel's primer is given on its own line (`primer_given`), and offered until it is; Tavar's thanks stay offered until answered; lint SOC001 refuses a gift on a spent line with no fallback | `e1156f5` |
| M-01, M-04 | A bought-out trader stays empty across a save; nothing can be put into a corpse, so nothing is lost when it decays | `a665dbf` |
| B-01 | A start screen with Continue (the newest whole save, autosaves included) and every slot, backup and previous copy loadable; L opens the list in the game; a failed load opens it with the reason; a displaced unproven quick or manual save is kept one deep as `.prev-<slot>` | `f06e8f4` |
| H-02 | Event subscribers are isolated (a throwing observer cannot re-run a tick or double-apply a system); effect, icon and sound manifests are validated per entry at load and a bad entry is withheld; the scene build falls back to greybox. `ArtLibrary` is handed off - see below | `8623690` |
| M-02, L-03, M-07 | A failing autosave is an outcome, retried after 30, 60, 120 and 240 s, not an exception every frame; every IO step of a commit is guarded and undone; one game per profile (`<profile>/.lock`); a game that cannot start says why and exits 2 | `3f2b5b2`, `1620fdf` |
| M-03, L-27 | One modal rule: while a conversation, the inventory, the character sheet or the saves list is open, no gameplay key reaches the world (the world is not paused); the mouse is decided once a frame; a load closes the previous world's panels; key labels follow the keyboard layout | `988b10a` |
| M-05, L-14 | After a respawn a key still held is sent again; a dodge is not drawn along the jump to the Waystone; a working's recovery is drawn walking, as it moves | `570e8bb` |
| M-06 | The UI scales with the window (`canvas_items`, `expand`); the pack and a trader's list scroll; the dialogue sits bottom-centre with scrolling replies | `da3dde7` |
| M-08, L-21 to L-24 | A stun is not cut short by a staggering blow; only a physical blow counts against a guard; the hands cannot change under a guard or mid-action; one swing lands on one body; NPCs and companions are solid to a charge | `88537ea` |
| P-01, P-07 | Saves are taken on the frame and written off it, in capture order; the extended performance route (see below) | `7d2d744` |
| T-03 | Every field of the player is filled, round-tripped and moves the digest (reflection-driven) | `8c328ee` |
| T-01, L-20 | The relaunch compares the world as the game rebuilt it (a `live` section of every creature, NPC, companion, container, node, switch, door, barrier, quest and the combat pools), fails unless the load was complete and the digest is the save's, and a new run clears the last run's evidence | `59b2600` |
| T-02 | The M6 acceptance run's own save (schema 12, written by `3e6dbcc`) is committed and loaded by today's game, compared field by field, and played on | `2851935` |
| L-06 | Schema 14: a creature's charge cooldown, stagger immunity and the stagger it is in (a stun among them), and the sounds made on the last tick, are saved | `91dd06c` |
| L-09 to L-13, L-15 to L-17, L-25, L-26 | Walls stop talk and trade; an air swing is not a fight; no crouch mid-dodge; XP shown truly; the shot flies where the reticle points; landings resolve at full radius; no door closes on a body; Sel knows the Foldscar is quiet; the extra tick between actions documented; `HealthChanged` says what changed | `9164b03` |

## Not fixed, and why

- **H-02, the part in `src/Presentation/Art/ArtLibrary.cs`**: the file has uncommitted work in the asset-remediation worktree, so this pass leaves it alone. The handoff is below.
- **L-25** is documented, not changed: changing it moves every action's cadence by a tick (player, creature, companion) and the balance the TTK table records. Pinned by `SwingsAskedForAtEveryTick_StartOneEveryTotalTicksPlusOne`; the owner rules.
- **L-29, L-30** (suspicions) were not reproduced, and so not touched, as instructed.
- Not in this pass's list: L-01 (the save tool reports real saves as blocked), L-02 (a retired creature record cannot be set again), L-04 (a hash-valid malformed section can crash the loader), L-05 (pre-migration backups matched by suffix), L-07 (the M3-M6 layout transition carries positions verbatim), L-08 (two crafts in one tick share a quality roll), L-28 (the playtest build is hand-assembled), T-04 to T-11, P-02 to P-06, and the observations.
- The design rulings the brief reserved to the owner are untouched: companion XP and quest kill credit, shots resolving at release, the bow and leash ranges, the world running while the UI is open, the ULID identity doctrine, and cross-OS deterministic mathematics.

## Tests

696 on `main` (`e10d2c4`, counted from an archive of it), 767 now, all passing: 71 more. Each fix has a regression test; those for M-08, L-21 to L-24, L-06 and the LOWs in `9164b03` were each run against the code without the fix and fail there.

## Saves: schema 14

- The creature shape schemas 8 to 13 wrote is frozen as `Sections.V8.Creature`, the entities shape of schemas 9 to 13 as `Sections.V13.EntitiesSection`; the 7 -> 8 and 8 -> 9 steps are repointed at them; `SchemaV13ToV14` gives older saves no cooldown, stagger or immunity and nothing to hear.
- `Fixtures/v14` was written by the probe (`M2.Probe fixture`): a wolf mid-stun with a cooldown and an immunity, and two sounds waiting, one a howl naming the renamed hound so the definition-ID pass reaches it. Every `expected.json` was regenerated and reviewed: older versions gained only the empty new fields.
- Proof: every fixture v1 to v14 loads to its expected state and migrates through the commit path; the M6 acceptance save (schema 12) loads 12 -> 13 -> 14 under today's content, equal field by field to what its build recorded but for what the steps add; the owner's ten local saves (schema 12 and 13, a scratch copy, not committed) load complete and play 400 ticks with nothing wrong.
- Live continuation: a boar stunned at the save stays down and charges again on the same tick as the unsaved world; a guardian's howl made on the tick of the save still brings its pack - both compared tick by tick with the world saved from, both failing with the restore removed.

## Runtime proof (the final commit)

- `dotnet test`: 767 passed, 0 failed. Content lint: 102 definitions, 0 errors.
- Headless smoke: PASS. Input check: PASS (no gameplay key reaches the world with any panel open; a load closes a conversation's panel).
- The M6 acceptance playthrough, twice: every beat, saved at tick 7303; each relaunch went through Continue to the acceptance save, loaded complete, compared 956 fields with 0 differences and the save's own digest, and the death at the den added its XP debt once. The two runs' `state_replay.json` are identical, and so are their command logs. Evidence: `docs/acceptance/phase1_audit/`.
- Stale evidence (L-20, on the build of `59b2600`, whose clearing code is unchanged since): a run over a finished run's directory cleared it, stopped before its save, and the relaunch failed ("Continue loaded nothing").
- The layout check at 1366x768 and 1280x720, with a full pack of 24 stacks: every button on screen, the pack's last row scrolls into view; pictures in the evidence folder.
- The resume flow: the start screen, Continue, and the in-game list, pictured; delta-shots (the owner's M6 playtest delta) and ui-shots run through.

## The extended performance route

`--perf --perf-route extended` plays what the gate's route avoids, through the real command path: a conversation with Sel and the primer read, each formula worked, the Charwood hound fought, its body and the merchant cart searched with the inventory open and Take All, then 150 s traversals in third and first person - past 300 s of play, so the autosave lands inside the capture. Each goal ends its segment when met, or is reported NOT MET. The summary marks the frame the autosave was taken and the one its write finished, with the frame times around each against the segment's median.

The final commit's capture (`docs/acceptance/phase1_audit/perf/summary.json`) ran on the development machine - ASTRAL, an RTX 5090 and a Ryzen 9 9950X3D, 1920x1080, vsync off - not RAZER; it is not the gate, which waits on the owner's RAZER window.

| Segment | Seconds | Goal | Median ms | 99th percentile ms | Worst ms | Over 33 ms | Within 16.7 ms |
|---|---|---|---|---|---|---|---|
| warmup | 5.0 | - | 2.38 | 5.56 | 150.0 | 4 | 99.79% |
| conversation | 16.3 | met in 16.3 s | 2.08 | 4.59 | 92.6 | 2 | 99.97% |
| magic | 4.5 | met in 4.6 s | 2.08 | 4.79 | 33.9 | 1 | 99.90% |
| combat | 23.4 | met in 23.5 s | 2.08 | 3.97 | 70.4 | 2 | 99.98% |
| loot | 22.6 | met in 22.7 s | 2.08 | 3.78 | 33.0 | 0 | 99.99% |
| third person | 150.0 | - | 2.08 | 4.69 | 34.9 | 1 | 99.99% |
| first person | 150.0 | - | 2.08 | 4.55 | 60.4 | 1 | 99.99% |

- The autosave was taken at 300.0 s of play, in the first-person segment: that frame and the two after took 1.67, 2.73 and 1.85 ms against the segment's median of 2.08; the frames around the moment its write finished in the background, 1.67, 1.51 and 1.67 ms. The save no longer costs a frame.
- The goals: Sel's primer taken and read (3 formulas known), 3 formulas worked, the hound killed, its body searched with Take All (the hound's loot rolled nothing this time) and the merchant cart's 2 stacks taken.
- The frames over 33 ms are isolated single frames, none at or near the save. This capture does not show their cause (its process-time column is a slowly sampled monitor, not per frame); the audit's P-05 - assets loaded synchronously on first use - is one candidate, and this pass does not change it.

## H-02 handoff to the asset agent: `src/Presentation/Art/ArtLibrary.cs`

Two parse sites still throw on a malformed record.

1. `ArtLibrary.Material(id)` (the material record): `GetString()!` on a JSON `null` for `basecolor`, `normal` or `orm` reaches `Path.Combine(folder, null)`, an `ArgumentNullException` the catch (`JsonException`, `KeyNotFoundException`, `InvalidOperationException`) does not take. At boot `Main.BuildScene` now falls back - but to greybox for the whole scene; at run time it stops `Main.Draw` every frame. A `tile_size_m` of 0 gives an infinite UV scale (cosmetic).
2. `ArtLibrary.Info(id)` (the clip record): `e.GetProperty("id").GetString()!` stores a null event ID for a JSON `null`, which can throw wherever event IDs are compared.

Suggested: check each field's `ValueKind`, treat a missing or wrong-kind field as a problem (`Problem(id, ...)`, greybox for that record), add `ArgumentException` to both catches, and never let `ArtLibrary` throw to its caller. Check: `--smoke` against an asset root whose material has `"basecolor": null` - that one material in greybox, PASS, and no "building the scene ... failed" line.

## For the owner

- `.prev-<slot>` (B-01): a new file in the profile, the displaced unproven quick or manual save, one deep.
- A save's `build_timestamp` is now when its world was taken, not when it was written (P-01).
- L is the in-game load key. Not done: an autosave on quit, and a loading label during `_Ready`.
- A charge into an NPC or a companion ends and stuns the charger (L-24); the hands are locked under a guard and mid-action (L-22).
- Blackvein Cut's discovery circle now covers the quarry floor and both approaches (C-01), and `world_state` dialogue conditions may name a place (L-17): content schema additions.
- A save made before H-01 that took and read the primer can be offered it again: old saves recorded hearing the offer, not taking it.
- L-25: whether an action should come round in exactly its authored time.
- An air swing no longer delays health regeneration either (L-10's side effect), and a facing is sent on every turn while the body faces the camera (L-13), within the command log's existing bound (P-04).

## Merging

The asset-remediation worktree's uncommitted work is in `src/Presentation/Art/` and `tools/asset_pipeline/`; this branch touches neither, and the asset branch's only commits since `e10d2c4` add a document. The overlap risk is later work on `Main.cs` (418 lines added and 164 removed here), `Ui/InventoryPanel.cs`, `Ui/DialoguePanel.cs`, `Ui/AssetCatalog.cs`, `Audio/SoundBank.cs` and `project.godot` (the stretch mode). New files here (`SavesPanel.cs`, `InputCheck.cs`, `LayoutCheck.cs`, `Perf/PerfActivities.cs`) have no `.uid` yet; the editor writes them on first open.
