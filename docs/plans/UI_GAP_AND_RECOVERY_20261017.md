# OTHERREACH UI gap and recovery: what existed on ASTRAL, what exists on BEAST, what to rebuild

Written 2026-10-17. Research only; no repo file was changed. Repo checked: `C:\Otherreach_Recovery\UNNAMED`, branch `restore/beast-oct02-snapshot` at 3fe32fb (merge of audio/recovery into rc/20261017b).

Short version:
- ASTRAL did hold a full 12-screen "Field Instrument" UI (finished 2026-10-04, owner-accepted in direction). BEAST has 7 of the 12, plus some screens ASTRAL never had (Map, Waystone, Sound).
- Missing on BEAST: **Skills, Reputation, Building (and build mode with its palette), Settings, and character creation/naming.**
- Nearly all of the missing screens' design is recoverable, and so is most of their code, from the UI/UX transcript. The code sits in "patch scripts" applied to C4 originals that the same transcript also dumps (section 4).
- There was no real "title screen" or "crafting screen" on ASTRAL either. Both exist on BEAST in the same form (start screen = `SavesPanel`; crafting = a station column inside `InventoryPanel`).
- The file `ASSET_REBUILD\PLAYTEST_COMPLETENESS_AUDIT_20261016.md` does not exist.

---

## 1. What existed before the crash (ASTRAL)

### 1.1 The Field Instrument UI, 12 screens (2026-10-04)

Evidence: UI/UX transcript `RESTORE_transcripts\RESTORE_transcript_Otherreach_UI_UX_presentation_design_9BGiBqKo.json`, event 3234 (the final handoff), 2026-10-04T10:27:

> "the twelve panels (`SystemMenuPanel`, `CharacterPanel`, `SkillsPanel`, `ReputationPanel`, `EffectsPanel`, `CompanionPanel`, `BuildPanel`, `SettingsPanel`, `InventoryPanel`, `JournalPanel` ..., `SavesPanel`, `HelpPanel`)"
> "System Menu · Character · Skills · Reputation · Effects · Companions · Building Management · Settings · Inventory · Journal · Save / Load · Help ... all PASS (1920x1080 native, empty and populated)"

The checkout was `G:\UNNAMED_CLAUDE_UI_POLISH`, branch `claude/field-instrument-ui-20261004`, commits 3f67965, 26b2c0b and 045cb2b, and 15 files in `src/Presentation/Ui` (+2093/-374). Neither the worktree nor the commits survive on BEAST (`git cat-file` can't find them; `G:\UNNAMED_HISTORY` is gone). The only record is the transcripts and `docs\restore\UIUX_BRIEF_AND_SPEC_2026-10-02.txt`.

Per-screen record (event 3122, `02_SCREEN_IMPLEMENTATION.md`; design in the spec file, PART D/E):

| Screen | Template | Features and design notes |
|---|---|---|
| System Menu | STANDARD 880 | "THE WORLD GOES ON" cue; Continue (primary, default focus); YOU (Character, Skills, Inventory, Effects) / THE WORLD (Journal, Companions, Reputation, Building) / SYSTEM (Settings, Save and load, Controls and help, Quit apart and quiet); live key caps read from InputMap; arrow-grid neighbours. 13 buttons. |
| Character | WIDE, 3 columns of 352 | Identity band (name, `LevelWords`, XP gauge, debt chip, points chip). Attributes with `+1` only while points are unspent, per-point gains. Derived stats grid in pool colours. Equipped (slot words), Practised (**top four skills**, brass line), Known (newest four with provenance). "No tabs." |
| **Skills** | DENSE 1152x720, list 368 / gutter 32 / detail 704 | Whole roster by family (COMBAT, CRAFTING, MAGIC, WORLD). Trained rows bright with level and a brass progress line; untrained dim with a dash. Technique-count chip, ember-edged when one is READY. "Everything known" pseudo-row. Detail: level, 16-segment gauge, "31 OF 52 XP TO 9". Techniques with KNOWN/READY/BLOCKED chips, requirement chips (met Lichen, unmet Wound, "YOU HAVE 0"), "after: <prereq>", provenance ("taught by Kera Voss"), the authority's refusal verbatim in muted 16 px, ALSO NEEDED BY, HOW SKILLS RISE. No points, no costs, no IDs. Opens on the first discipline with a READY technique. Key `P`. Deferred: threshold ruler, discipline map. |
| **Reputation** | STANDARD | One row per faction: name, seat/members/regards, signed points and a tier chip (NEUTRAL etc.). All-zero shows the model's sentence as a callout ("a faction learns of an act only when you tell one of its members"). Empty state NO STANDING YET. |
| Effects | STANDARD | HARM/AID chip, name, stacks, "every N s", brass time gauge plus seconds left (updated live); Strained callout. |
| Companions | STANDARD | Card per companion: state chip, health gauge, Follow me / Wait here, "Help them up" while downed (live countdown), away reason, refusal callout. |
| **Building** | DENSE | List: Built (condition, health), Work, Audit/conflicts, What can be built. Detail: Repair, **Take down (two-step confirm, 3 s)**, Assign a worker, Call them off, catalogue. Stat strip PIECES / DAMAGED / AT WORK / CONFLICTS. |
| **Settings** | STANDARD | Five rows: Look sensitivity, Master volume, Window mode, Vertical sync, Frame ceiling. Caption, control, value readout. Applies at once; saved by a `SettingsStore`. Node ids `sensitivity`, `volume`, `window`, `vsync`, `cap`. Footnote "Every setting here is one the game reads today." |
| Inventory | STANDARD pack-only, WIDE with a container/station/trader | Pack rows with the HUD slot frame, slot-words chip, focusable buttons, Drop last and quiet. Context column: container (Take all primary), station (skills, Make with inputs, disabled with the reason), trader (Coin, Buy/Sell). |
| Journal | STANDARD empty, DENSE with quests | ACTIVE/DONE/FAILED, objectives with the HUD tracker's diamond bullets, progress in brass. |
| Save / Load | STANDARD | **Start screen**: "Otherreach", Continue (primary), New game, Quit; new-game warning. In play: quicksave/quickload key caps, failure callouts, older copies indented. |
| Help | WIDE, 3 columns | Every line with live key caps; no new hotkeys. |

Shared kit (event 2440, `UiKit.cs`, 30,712 chars written): Sheet, Section, Stat, Gauge, Chip, KeyCap, Row, ListRow, Action, Callout, EmptyState, ListDetail. Tokens: Soot 0.94, Coal and Lichen added to HudStyle, 16 px text floor on a 1920x1080 reference, management band x 384-1536 / y 128-848, 35 % scrim. The sheet steps around HUD elements that reach into the band (companion strip, tracker).

Acceptance (event 3234): keyboard reach 100 % on every screen, 16/16 command types identical, no raw IDs, "no pause" proved (WorldTick +70 over 180 frames with the menu open), build 0 errors, 1445 tests. Captures were at `G:\UNNAMED_HISTORY\FIELD_INSTRUMENT_UI_2026-10-04\captures\{1920x1080,2560x1440,1280x720}` and are lost.

### 1.2 Other UI work on ASTRAL, Oct 5-6 (implementation transcript `..._OTHERREACH_implementation_pass_tMkF9py7.json`)

- Commit **f441ec4** (2026-10-05T11:03), "bring dialogue, build and tooltips into the Field Instrument UI": dialogue reply-purpose tags (continues / progresses / completes / turns in a quest), the **Build palette** as a Field Instrument panel in the production HUD (soot sheet, a key cap per piece, cost against what is carried, chosen piece marked), opaque hover tooltips (`HudStyle.StyleTooltips`), autorun.
  - Owner's complaint: "The Build menu did not receive the same visual language as the Field Instrument UI."
- Event 806 (the Ui folder as of 2026-10-05) lists: `BuildPanel.cs` (387 lines), `SkillsPanel` (297), `SettingsPanel` (193), `SettingsStore` (106), `ReputationPanel` (107), `CompanionPanel` (201), `FactionDebugPanel`, `StructureDebugPanel`, `Quickbar.cs`, and a `Models\` folder (UiText, SkillsModel, TechniqueModel, SettingsModel, BuildModel, ReputationModel, ...).
- Commit **9e1ae57** (2026-10-06): "the discovered map (M)": blank at start, reveals ground around each discovered place, names places, shows position and facing. BEAST rebuilt this as `MapPanel` on 2026-10-16 (c6cdc7a).
- First-play items the owner marked "retained": attribute preview/cancel/confirm, authoritative item and comparison tooltips, quickbar mechanics, loot Take All close, level-up notification, delivery item/count clarity, quest reward feedback (`OWNER_BRIEF_SECOND_PLAY_2026-10-05.txt`, sections 6 and 41).
- Character creator: designed (`canon_readonly\direction\player-journey\04_CHARACTER_CREATION.md`, 45 KB). OPEN_ITEMS 4: "body choice + name with Veth-adjacent Suggest (c927832 committed; panel never seen". Commit c927832 is not in this repo. Owner, first play: "no way to name your character"; deferred as "future work".

### 1.3 Owner feedback that shaped the UI (`docs\restore\OWNER_RULINGS_2026-10-05_to_14.txt`, lines 1312-1420)

- Level-up needs a banner or sound. Items need stats pop-ups (the owner wanted to know whether the rusted sword beats the march spear). Quick bars for salve and flask. Take All must not reopen the inventory. Quest turn-ins must say what was given. The owner could not tell which tree was gatherable ("no indication when something can be gathered from"). No explicit keystrokes in NPC speech. Dialogue sway made them "seasick".

### 1.4 Design intent never built (any machine)

From `canon_readonly\direction\player-journey\{02,03,09}` and `capability-progression\26_UI_INFORMATION_REQUIREMENTS.md` (all PROPOSED, not implemented):
- Main menu: Continue, New Game (opens the creator), Load, Settings, Accessibility, Credits, Quit. A live backdrop of the hollow (the BEAST `StartCamera` is a start on this). First-launch "Set up Otherreach" screen with presets. Notices for a crash or damaged save.
- Doc 03 section 8, **"must exist before any external playtest"**: display mode, resolution, render scale, tier, frame cap, brightness; four volume buses; keyboard and mouse remapping, sensitivity, invert, hold/toggle; FOV; text size, UI scale, high contrast, notification duration; guidance level; first-launch setup; `client_prefs.json`.
- Doc 09: tabs for Inventory (with an "equipment figure", i.e. a paper doll), Character, Journal, Map, People, Property, Field Guide. The 8-slot quick-access bar.
- Doc 26: Character Overview / Disciplines / Techniques with a "why can't I?" reason line, Craft section, Magic and formula tuning, 8-slot bar, item provenance (W1-W16 wireframes).
- Doc 09 and the 2026-10-02 doctrine disagree on pause; the owner-accepted Field Instrument says nothing pauses.

---

## 2. What exists now (verified in the code)

`src/Presentation/Ui` (5,953 lines in the .cs files), wired in `Main.cs` lines 410-430:

| Item | State |
|---|---|
| System Menu | 9 rows: Character, Inventory, Effects, Journal, **Map**, Companions, Save and load, **Sound**, Controls and help. Status strip (name, level, XP gauge, debt, time played, last save), Quit. No Skills, Reputation, Building or Settings rows. |
| CharacterPanel | Attributes with preview/spend, derived stats, equipment 5x2 icon grid, **Practised = top 4 skills only** (`OrderByDescending(...).Take(4)`, line 282), Known newest 4 with provenance (line 304). XP gauge with exact numbers. |
| InventoryPanel | Pack rows, item card with stats and comparison to what's worn, container/trader/station columns. **Crafting is here**: recipes for the station, complexity chip, inputs vs carried, Make (primary, disabled when short). |
| Crafting | `Greybox/CraftingView.cs` is only the 3D stations and gather nodes (anvil etc.). The crafting UI is the InventoryPanel station column. Matches canon doc 09 ("Crafting stays contextual"). |
| Journal, Saves, Map, Waystone, Help, Effects, Companions, Dialogue, Hud, HudView, Compass, Reticle | Present. Dialogue has reply tags. HUD has quick slots (keys 1-3), level-up and quest banners, tracker, compass. `Hud` has a death recap. |
| Start screen | `SavesPanel` at launch: Continue (newest loadable save), New game (with a confirm when saves exist), Quit, and the saves list. `Greybox/StartCamera.cs` gives a backdrop. `GraphicsPanel` (quality tier, applies on next start). It is a working start screen, not a designed title screen: no wordmark art, Settings, Credits, or Load/character split. |
| Settings | Only `SoundPanel` (Master/Music/Effects sliders, `AudioMix`) and the quality tier on the start screen. |
| Controls | `HelpPanel` is read-only. No remapping anywhere (`InputMap` is only touched by harness code). |
| Hard-coded | Mouse sensitivity 0.0025 (`Player/CameraRig.cs:56`), FOV 75 (`CameraRig.cs:35`), vsync and window mode untouched, frame cap from `Main` (`Engine.MaxFps`). No subtitles, no invert, no UI/text scale (the project uses `canvas_items` stretch with `aspect=expand` only). Dev keys F3/F4 are gated to debug builds or `--dev`. |
| Character creation / naming | None (no creator, no name field). |
| Not in the code base at all | Domain has no `Factions` and no building system (no `DismantlePiece`, no build mode). Those lived on `origin/claude/m7-factions-building` (FactionDebugPanel, StructureDebugPanel only). `UiText`, `SkillsModel`, `TechniqueModel`, `SettingsModel`, `SettingsStore`, `BuildModel`, `ReputationModel` do not exist in any ref (`git log --all -S"class SkillsPanel"` finds nothing). |
| Domain support that does exist | `CharacterProgression` has `Skills`, `Known` and technique learning (`ProgressionEngine.Learn`). 9 skills in `content\skills`, 12 abilities, 12 recipes, 3 spells. So a Skills screen has data to show. |

Oct 15 pass (`docs\restore\UI_PASS_2026-10-15.md`): D1 accepted the Field Instrument UI, D2 Escape closes to play, D3 dedicated panel icons. 45 icons wired.

---

## 3. Gap list, ranked for an outside-tester playtest

Estimates are agent working time including build and screenshot checks. "Port" means rebuilding from the transcript material in section 4.

| # | Gap | Why it matters to a stranger | Effort |
|---|---|---|---|
| 1 | **Settings screen with the real basics**: look sensitivity, FOV, invert Y, window mode, vsync, frame cap, master volume; move Sound into it. Persist to one prefs file. | Doc 03 section 8 names these a precondition for any external playtest. Hard-coded sensitivity 0.0025 and FOV 75 will be wrong for many players; there is no way to leave fullscreen or fix a bad frame rate. The 5-row ASTRAL SettingsPanel is a template. | M: 1-1.5 days. Port 5-row panel + store (about 0.5 day), add FOV, invert, window mode wiring and prefs file. |
| 2 | **Key rebinding** (and a controls section in Settings) | Strangers with non-QWERTY layouts or conflicts can't play. `HelpPanel` shows keys but never edits. ASTRAL never had it either (new work). | M: 1-1.5 days (InputMap edit, conflict check, persist, reset; Help already reads live). |
| 3 | **Skills screen** (key P, System Menu row) | The only view of skills is the top four on the Character sheet. Techniques, their requirements and "why blocked" aren't visible anywhere. | M: about 1 day via port (SkillsPanel patch script + SkillsModel/TechniqueModel dumped in the transcript, minus comments); 1.5 days from spec. Read-only UI, Domain already there. |
| 4 | **Character name** (minimum), then the body/race creator | Owner flagged "no way to name your character". Full creator is a big feature tied to the MetaHuman route. | Name field and Suggest on New game: S, 0.5 day (needs a save field; check schema). Full creator: L, 3-5 days plus art. |
| 5 | **Start/title screen polish**: Settings and Controls reachable before play, wordmark, Credits, version text, a "How to play" pointer. | Strangers meet this first. A start screen exists and works, but cannot reach any setting. | S-M: 0.5-1 day. First-launch setup wizard: M, 1 day. |
| 6 | **Accessibility basics**: UI/text scale, subtitles for voiced NPC lines (the `npc-voice-*` branches add voice), reduced-motion flag, high contrast | Doc 03 section 8; the 10.7 px captions at 1280x720 were left "for the deferred UI-scale setting". Dialogue is shown as text already, but barks/voice need subtitles. | M: UI scale 0.5 day (content scale factor), subtitles 0.5-1 day depending on the voice branch, others S. |
| 7 | **Reputation screen** | Only useful once factions exist. The Domain has no Factions, so it'd be an empty panel. | Blocked on factions. Port the panel + model (S, 0.5 day) after the faction systems are restored from `origin/claude/m7-factions-building`. Likely out of scope for the first tester build. |
| 8 | **Building screen and build mode with its palette** | The Build palette HUD panel and `BuildPanel` came with M7 building, which is not in the code base. | L: depends on restoring M7 building (days). UI port itself is M (1-1.5 days). Likely out of scope for the first tester build. |
| 9 | **Gather affordance** ("no indication which trees can be gathered from") and a hint line | Owner second-play complaint. HUD prompt exists; check coverage on trees and rocks. | S: 0.5 day (prompt/outline). Not a screen. |
| 10 | Character screen: "See all skills [P]" link | Makes the "top 4" explicit once Skills exists. | XS: 1 hour after #3. |
| 11 | Inventory equipment figure (paper doll); per-item search/sort | Canon proposes it; the 5x2 icon grid already covers the need. | L: 2-3 days. Skip for the tester build. |
| 12 | Journal extras (Recent events, notes), Knowledge, People tabs | Canon proposals only. | Skip. |
| 13 | Quick-access bar beyond 3 consumable slots (8-slot, assignable) | Canon: 8 slots. Current: keys 1-3 auto-fill. | M: 1-2 days. Defer unless testers hit it. |
| 14 | Death screen styling (current `Hud` recap box), loading feedback, Escape-back option | Death recap exists as text; polish only. | S each. |

Suggested tester-build cut: **#1, #2, #3, #5, #4-name, #6-UI scale** (about 5-6 days), then #7/#8 only if M7 returns.

---

## 4. Recoverable specs, mockups and source

### 4.1 Design specs on disk (authoritative)

- `C:\Otherreach_Recovery\UNNAMED\docs\restore\UIUX_BRIEF_AND_SPEC_2026-10-02.txt` (309 KB; also in `UNNAMED_ui\docs\restore\`). Line numbers: PART D (final handoff) 1256; PART E docs `02` design system 1396, `03` System Menu wireframe 1760, `04` Character wireframe 1843, **`05` Skills wireframe 1940**, **`06` other screens (Reputation, Effects, Companions, Building, Settings, Inventory, Journal, Save/Load, Help) 2049**, `07` implementation plan 2141, `00` audit of the C4 UI 2241. Wireframes are ASCII with reference-pixel coordinates.
- `docs\restore\UI_PASS_2026-10-15.md` (the Oct 15 pass), `OPEN_ITEMS.txt` items 4, 20, 31, 32; `OWNER_RULINGS_2026-10-05_to_14.txt`; `C:\Otherreach_Recovery\UI_PASS_BRIEF.md`.
- `canon_readonly\direction\player-journey\02_FIRST_BOOT_MAIN_MENU.md` (title/main menu, first launch), `03_SETTINGS_ACCESSIBILITY_CONTROLS.md` (full taxonomy; section 8 is the tester minimum), `04_CHARACTER_CREATION.md`, `09_UI_HUD_INFORMATION_ARCHITECTURE.md`.
- `canon_readonly\direction\capability-progression\26_UI_INFORMATION_REQUIREMENTS.md` (Character/Skills/Craft/Magic wireframes W1-W16, reason grammar, quick bar), `03_ATTRIBUTES_SKILLS_AND_DISCIPLINES.md`, `12_CRAFTING_PROFESSIONS_AND_DISCOVERY.md`, `diagrams\D05_CRAFTING_LOOP.md`.
- `canon_readonly\proposals\existing\untracked-docs\PHASE1_HUD_UI_CONTROLS_AND_FEEDBACK_SPEC.md`; `canon_readonly\direction\extensions\HUD_INPUT_AND_ACTIONS.md`, `INVENTORY_STORAGE_AND_LOGISTICS.md`, `SKILLS_AND_DISCIPLINES.md`.

### 4.2 Source code that survives only in the UI/UX transcript

`RESTORE_transcripts\RESTORE_transcript_Otherreach_UI_UX_presentation_design_9BGiBqKo.json`, events (index in `events[]`):

- **C4 originals** (before restyle; Godot panels plus Models; some versions have comments stripped):
  - 46: SystemMenuPanel, CharacterPanel, SkillsPanel.
  - 49 and 52: SystemMenuModel, CharacterModel, **SkillsModel**, **TechniqueModel**, PanelText, UiText.
  - 55: ReputationPanel, EffectsPanel, CompanionPanel, BuildPanel, SettingsPanel.
  - 57: ReputationModel, EffectsModel, CompanionModel, BuildModel, SettingsModel, ItemText.
  - 2349-2369: the same files again from the polish checkout (2355 CharacterPanel + SkillsPanel + CharacterModel; 2369 Companion/Build/Settings panels; 2361/2363/2366 model record signatures).
- **The restyle** (what 045cb2b did), as Python patch scripts in `Write` tool inputs, each applied to the C4 file: 2478 patch_settings.py, 2487 patch_rep_eff.py, 2495 patch_comp.py, 2509 patch_char.py, **2524 patch_skills.py** (12 KB), **2532 patch_build.py** (14 KB), 2539 journal, 2544/2579 help, 2562 inventory (16 KB), 2587 saves, 2458 SystemMenu rewrite, 2869/2879/2971 later fixes. `UiKit.cs` in full: 2440. BEAST's current `UiKit.cs` (783 lines) was rebuilt from this and is API-compatible (Sheet, Section, Chip, Row, ListDetail ...).
- **Already extracted copy (volatile temp folder, copy it somewhere safe if needed):** `C:\Users\jluca\AppData\Local\Temp\claude\C--Users-jluca-ComfyUI\dc857f6a-e8f9-4971-86bd-8b7ee605e32a\scratchpad\or\recon_ui\` holds the 18 patch scripts, `UiKit.cs`, `FieldProof.cs` harness, `run_native.ps1`, and the design records `00`-`07` from `CLAUDE_UI_UX_PREP_2026-10-02` (an earlier recon dated Oct 14).
- **Oct 5 BuildPanel** (387 lines): impl transcript event 1011. UiText: impl events 634 and 1995.
- Doc text of the Oct 4 handoff pack: events 3122, 3210, 3212, 3221, 3223, 3230, 3234 (`02`-`08`). Keyboard/focus rules in 3210, command-identity receipts in 3212.

Caveat: the C4 Application layer (the Models' inputs: `SkillsSheet`, `TechniqueEntry`, `BuildSheet`, `UiSettings`, `ReputationEntry`) must be matched to BEAST's Domain. Panels bind to records built by the Models, so port the Models too and adapt `CharacterProgression` reads. For Skills the data exists. For Settings the store is trivial. For Reputation/Building the systems are missing.

### 4.3 Images

- Current screens (BEAST, Oct 15): `C:\Otherreach_Recovery\asset_work\ui\before_after_20261015\01-19*.png` (system menu, character, inventory x3, journal x2, effects, companions, help, saves, HUD, resolutions) and `asset_work\ui\shots\after_*`. Copied to Drive `G:\My Drive\OTHERREACH_RESTORE\UI_PASS_20261015`.
- ASTRAL's 29 + 13 + 13 Oct 4 captures (including the Skills, Reputation, Building and Settings sheets) are **lost**. The spec wireframes are the only layout reference for those four.
- `canon_readonly\website\media\*-concept.webp` are character concept art only, no UI.
- No UI mockup images were found in `canon_readonly`.

### 4.4 Search coverage and limits

Searched all five transcripts (events parsed, not whole-file read) for the listed terms. `Humble_Bundle`, `claude_md` and `M7_final_cloud_audit` held nothing UI-specific beyond audits and asset census text. Not found anywhere: a minimap (owner doctrine: compass over minimap), a paper doll, an accessibility settings screen, subtitles, a keybind UI. A filename sweep of `C:\Otherreach_Recovery`, `C:\Users\jluca` and Drive `OTHERREACH_RESTORE` for `SkillsPanel*`, `SettingsPanel*`, `BuildPanel*`, `ReputationPanel*` found only the recon copy above (and unrelated web-viewer JS). `G:\Other computers\My Computer\G` holds only Downloads/FileHistory, not ASTRAL's G drive. Every branch (local and origin) was checked: no ref contains a SkillsPanel, SettingsPanel, ReputationPanel, BuildPanel or any of the Models.

### 4.5 Recommended order if rebuilding

1. Settings (store + panel, add FOV and invert) and its System Menu row. Then rebinding.
2. Skills panel + models (port 2524 + 49/52), System Menu row, `P` key, link from Character.
3. Start screen: Settings/Controls entry, name field.
4. UI scale and subtitles.
5. Reputation/Building only after the factions and building systems return.
