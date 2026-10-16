# OTHERREACH systems-completeness plan (2026-10-17), revision 2

This plan is research only. No repo file was changed and nothing was committed. The first version is kept as `SYSTEMS_COMPLETENESS_PLAN_20261017_v1.md`.

**What changed in revision 2.** The owner has answered the section 3 decisions of v1:
- **Accepted as recommended:**
  - D3 creator scope.
  - D4 laden band.
  - D5 durability.
  - D6 weather.
  - D8 building in scope, with the B1-B4 defaults still to be proposed.
  - D11 one technique per weapon family.
  - A numbered calendar.
  - A local-only crash log.
  - Gamepad and mounts out of this bar.
- **Overruled under "nothing outside normal MMO rules":**
  - No pause anywhere.
  - No difficulty levels or presets.
  - The world keeps running while the player is logged off.
  - NPC adventurers live their own lives.
  - Villagers and vendors have full lives, not just schedules.
  - The economy runs on its own.
- **Added:** companions and hirelings "Baldur's Gate and/or New World style".

Before designing the new systems, the old material was searched (section 4.1): canon, the five RESTORE transcripts and every branch. The new systems build on that material:
- §2.8 is revised.
- §§2.12-2.15 are new.
- Sections 3, 5, 6 and 7 are rewritten.
- WP7 shrinks and WP8 shrinks.
- WP11-WP15 are new.

**The bar (owner, 2026-10-17):** the playtest must be "a 'complete' game in every aspect, other than actual content/maps/areas/quests. All of the functionality should be complete." Content only has to cover Ashen Hollow and Cairnwold. Every system has to work end to end and be ready to extend.

## Inputs checked

- **Repo:** `C:\Otherreach_Recovery\UNNAMED`, branch `restore/beast-oct02-snapshot`.
  - **HEAD has moved since the brief.** It is now **aa76445** ("Merge branch 'anim/locomotion' into integration/loco"). 3fe32fb is its ancestor, and anim/locomotion is merged.
  - Save schema is **15** (`src/Persistence/SaveModel.cs:20`).
  - There are about 730 tests in 8 projects.
- **Canon:** `canon_readonly`. Precedence follows `AUTHORITY.md:21-31`.
- **Plans:**
  - `ASSET_REBUILD\DIRECTION_AND_CONTENT_PLAN.md` (DCP)
  - `NEXT_AREA_TRAVEL_CITY_PLAN.md` (NA)
  - `NPC_PERSONALITY_BIBLE.md` (NPB)
  - `OWNER_DECISIONS_20261015.md` (OD15)
  - `CLOUD_TASKS_20261016.md`
  - `DEEPSEEK_PROJECT_BARROWWORKS.md`
- **Audits:**
  - `AUDIO_RECOVERY_20261016.md`
  - `UI_GAP_AND_RECOVERY_20261017.md` (UIG). It exists, and its gaps are merged into this plan.
  - `docs\restore\OPEN_ITEMS.txt`
  - `OWNER_RULINGS_2026-10-05_to_14.txt` (ORR)
- **Branches:** all 35 refs were checked. See section 4.

**Abbreviations:**
- `PJnn` = `canon_readonly\direction\player-journey\nn_*.md`
- `Cnn` = `canon_readonly\direction\capability-progression\nn_*.md`
- `OR` = `capability-progression\OWNER_RULINGS_2026-09-26.md`
- `O-nn` = `canon_readonly\outline\sections\nn_*.md`
- `SPN` = `owner\SECOND_PLAYTEST_NOTES.txt`
- Code paths are relative to the repo root.

**Corrections to the brief:**
1. **Slots.** The equipment slots do exist. `EquipSlot` has all 10: MainHand, OffHand, Head, Chest, Hands, Legs, Feet, Cloak, Ring, Amulet (`src/Domain/Items/Items.cs:10-22`). Two-handed weapons take OffHand too (`Items.cs:143`). The gap is **content**: items only use main hand, head, chest and amulet, and there is no shield or torch. Shoulders, waist and back are "flagged, not decided" (PJ04:220).
2. **Recipes for drops.** Spider silk, cinder sac, fellhorn hide and barrowback hide now have recipes (the content-fixes merge). The materials still unused are `boneleaf`, `herb_ashbloom` and `timber`. `haldas_bow` has no `skill_ref`.
3. **Merge status.** Every local branch except `feature/barrowworks` is already merged into HEAD.

---

## 1. System inventory

**Status key:**
- **COMPLETE:** works end to end in the build today.
- **PARTIAL:** the core exists, but a part the design calls for is missing.
- **MISSING:** nothing usable exists.
- **DEFERRED-BY-RULING:** missing, but an owner ruling puts it after this bar. It is still counted as MISSING; section 3 asks the owner to confirm it stays out.

### 1.1 Front end

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| F1 | Title / start screen | PARTIAL | `SavesPanel` is shown at launch, with `Greybox/StartCamera.cs` as the backdrop (UIG:81) | No wordmark, version, Settings, Controls or Credits entry. PJ02 wants Continue, New Game, Load, Settings, Accessibility, Credits and Quit (UIG:62). |
| F2 | New game | PARTIAL | `_session.NewGame("Wanderer", ...)` at `src/Presentation/Main.cs:292,454` | No creator and no name. There are no difficulty choices, by owner ruling. Asks for confirmation when saves exist. |
| F3 | Continue | COMPLETE | `SavesPanel` "Continue (newest loadable save)" (UIG:81) | — |
| F4 | Load | COMPLETE | `SavesPanel` slot list, opened with L (`Main.cs:2224-2262`) | Per-character save folders (OD-14, accepted, PJ14:362) not done. |
| F5 | Settings menu | PARTIAL | Only `Ui/SoundPanel.cs` and `Ui/GraphicsPanel.cs` (a tier that applies on the next start) | See S1-S9. |
| F6 | Credits | MISSING | Only `release/payload/THIRD_PARTY_NOTICES/*` (commit 8cd3ec0); nothing in game | PJ02:118 wants a main-menu credits entry. |
| F7 | Quit | COMPLETE | Start screen and System Menu | No "save before quitting?" prompt after 2 or more minutes (PJ10:228). |
| F8 | Character creator | MISSING | No name field or creator. `player=metahuman_f/m` is a dev visual option only (908cee6, `VisualOptions.cs:34`). `origin/character-creator-godot4` is an unrelated GDScript repo. | Canon flow is People → Body → Face → Call → Identity (PJ04:49-59). Scope is in section 3, D3. |
| F9 | Intro / opening sequence | MISSING | No opening code | Owner chose E1 = B, the "Leak" opening: wake-up, sky blend, 4 relics (OD15:24; DCP:448-463). The Road In gives control within 10 s (PJ14:30-46). |
| F10 | First-launch setup | MISSING | — | PJ03 §8: four presets. |

### 1.2 Settings

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| S1 | Graphics | PARTIAL | Low/Med/High/Ultra tiers (`Greybox/RenderTiers.cs`, `Ui/GraphicsPanel.cs:9-10`), applied on the next launch; `Engine.MaxFps` set from `Main` | Missing: window mode, resolution, render scale, vsync, frame cap, brightness (PJ03:262-272). Missing: per-feature dials, since the owner said "players get dials and sliders" (ORR:57-63; OPEN_ITEMS 20). |
| S2 | Audio | PARTIAL | Master, Music and Effects sliders (`Audio/AudioMix.cs:15-31`), buses created in code | Canon wants master, effects, ambience and interface buses (PJ03). Music already exists. There is no `default_bus_layout`. |
| S3 | Controls and rebinding | MISSING | Actions are built in code by `DefineInput()` (`Main.cs:2215-2263`); `project.godot` has no `[input]` section; `HelpPanel` is read-only | Remapping, hold/toggle, persistence. |
| S4 | Mouse | MISSING | Sensitivity is hard-coded at 0.0025 (`Player/CameraRig.cs:56`) | Sensitivity, invert Y. |
| S5 | FOV | MISSING | Hard-coded at 75 (`CameraRig.cs:35`) | FOV slider, plus camera shake, bob and flash hooks (PJ03 §8). |
| S6 | Accessibility | MISSING | No subtitles, UI scale, text size, high contrast or colour-vision support; the project uses `canvas_items` stretch only (UIG:84) | PJ03:161-167 marks subtitles and colour-vision support as core. PJ03 §8 needs text size, UI scale, high contrast and notification duration. |
| S7 | Gamepad | MISSING (DEFERRED-BY-RULING) | No JoyButton or JoyAxis anywhere | Owner: "Keyboard and mouse only for now" (PJ03:18). Controller parity comes before any **paid** release (OD-15, PJ14:388). |
| S8 | Settings persistence | PARTIAL | `user://audio_volume.json` and `user://graphics_tier.txt` only | One `client_prefs.json` (PJ03 §8). |
| S9 | No pause (owner ruling 2026-10-17: no pause anywhere) | COMPLETE | The System Menu "suspends the player's control, not time" (`Ui/SystemMenuPanel.cs:10,189`). This matches the owner's 2026-10-02 brief "NO TRUE PAUSE… The world continues" (UI/UX transcript, event 0). | The ruling supersedes canon OD-12 (PJ14:317-336). ASTRAL had a `NoPauseDoctrineTests` guard (UI/UX transcript, events 2320-2323); it is not on HEAD. WP1 restores it. |

### 1.3 Character

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| C1 | Attributes | COMPLETE | 7 attributes (`Domain/Progression/ProgressionKeys.cs:9`), spent with `SpendAttributeCommand` (`World/Runtime/Systems.cs:344`), preview and cancel in `Ui/CharacterPanel.cs:359` | — |
| C2 | Skills | COMPLETE | 9 skills in `content/skills`, learned by use with a challenge gate and a ceiling of 60 (`content/config/progression.yaml`) | `haldas_bow` has no `skill_ref`. |
| C3 | Level / XP | COMPLETE | Curve in `xp_curve.yaml`; `level_cap_phase1: 5` enforced at `ProgressionEngine.cs:112`; XP debt on death (`CharacterProgression.cs:54`) | The cap of 5 is a content number, not a system gap. |
| C4 | Skills page | MISSING | Only the top 4 skills, on `CharacterPanel.cs:282` | ASTRAL's `SkillsPanel` (key P) can be ported from the transcript (UIG:31, 101). |
| C5 | Techniques / player abilities | PARTIAL | `ProgressionEngine.Learn` and `TechniqueKinds` "ability" (`ProgressionKeys.cs:33`) exist, but `content/abilities/` only has 12 creature attacks | No player technique content and no way to trigger one. See section 3, D11. |
| C6 | Spells / magic | COMPLETE | 3 spells; Focus, Strain, fizzle and backlash (`Domain/Magic/Magic.cs:76-101`; `World/Runtime/Magic.cs:25-136`); learned from tomes (`World/Runtime/Items.cs:307-314`) | — |
| C7 | Spellbook / quick bar | PARTIAL | Spells cast from fixed keys 4-6 (`PlayerController.cs:120`; `Main.cs:1191-1202`); consumables auto-fill keys 1-3 (UIG:80) | No spellbook view. No assignable 8-slot bar (PJ09; C26). |
| C8 | Peoples (races) | MISSING | Only the `GrantSource.Race` enum value and an empty `species` schema stub (`SchemaTypeMapper.cs:333`) | The slice ships **Veth only** (E14 / OD-08 = B + M1, DCP:912), so the system only needs a `people` field and the Veth entry. |
| C9 | Calls (starting packages) | MISSING | The starting package is empty (`progression.yaml`) | Accepted OD-C03: CC-0 Calls are Wayfarer, Scholar, Smith's hand and Hedge healer. Each gives 4 bias points, 2 skills at 15, kit and a contact (C11:43-49, 109; OR:85-98). |
| C10 | Trainers (teacher role) | PARTIAL | Only recipes are taught, through hand-written `give_recipe` lines (Kera, Sel, Durn, Tam). Gates are node-visited checks plus Kera's trust +5. There are no fees, skill thresholds, technique or spell teaching, or teacher role in NPC data. | Canon: `canon/PROGRESSION.md:64,223,242`; C04:105-121; C12:263-274. See §2.3 C10. |

### 1.4 Items

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| I1 | Inventory | COMPLETE | 24 stack slots (`content/config/inventory.yaml`), `World/Runtime/Items.cs:174-266`, `Ui/InventoryPanel.cs` | — |
| I2 | Equipment slots | PARTIAL | All 10 slots are in Domain (`Items.cs:10-22`), but content only fills main hand, head, chest and amulet (one trinket) | No hands, legs, feet, cloak or ring items. No off-hand item: the torch is owner-asked (ORR:1333) and shields are "coming" (DCP:669). |
| I3 | Paper doll | PARTIAL | A 5×2 slot grid with ghost icons (`CharacterPanel`; `UI_PASS_2026-10-15.md:31`) | Canon proposes an "equipment figure" (PJ09). Visible equipment is held behind the VE gate (PJ04:364-368). See D12. |
| I4 | Item classes | MISSING | Category only (10 categories, `Content/ContentChecks.cs:225`) | E5 = A ruled: Ordinary, Masterwork, Notable find, Artefact, Completed artefact (DCP:626-636, 903; O-06:42). |
| I5 | Quality | PARTIAL | −1/0/+1 per stack (`Domain/Crafting/Crafting.cs:36-50`); weapons get ±2 damage (`World/Runtime/Combat.cs:432`) | No armour effect. Drops have no quality roll weighted by creature tier (DCP:662). No masterwork +2 (C12:186-205). |
| I6 | Rarity | PARTIAL | Field loaded (`ItemContent.cs:115`; `Items.cs:78`), read nowhere | Ruled: rarity is the storage field, **mapped from the class** (DCP:903). |
| I7 | Tooltip and comparison | COMPLETE | `InventoryPanel.cs:355-454`, with deltas against the worn item (`:398-416`) | Polish: delta arrows, a provenance line, familiarity pips (DCP:673-676). |
| I8 | Stacking | COMPLETE | `stack_max`; stacks split by quality (`Items.cs:174,237,266`; PERSISTENCE:244) | — |
| I9 | Weight / encumbrance | PARTIAL | Hard limit of 30 kg + 2 kg per Might; pickups refused over it (`Domain/Items/Items.cs:109-112`; `World/Runtime/Items.cs:172-184`) | Canon wants weight to affect stamina and movement, with no instant cliff (INVENTORY_STORAGE_AND_LOGISTICS:47-56). See D4. |
| I10 | Durability / repair | MISSING | Only an optional `durability_max` schema field (`ContentChecks.cs:256`) | Canon: "repair as the first real sink" (C12:427); the repair sink is a blocking prerequisite for durability content (C12:440); repair re-rolls capped by the original quality (C13:226). See D5. |

### 1.5 Crafting

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| K1 | Station UI | PARTIAL | A station column inside `InventoryPanel` (inputs vs carried, complexity chip, disabled reason); the 3D stations are greybox (`Greybox/CraftingView.cs`) | Canon keeps crafting contextual (UIG:79). Missing: an output-quality preview, the "why can't I" line (C26 W-wireframes), and real station models (art, not systems). |
| K2 | Recipe learning | PARTIAL | Recipes must be known (`World/Runtime/Crafting.cs:164`); NPCs teach them with `give_recipe` (`World/Runtime/Social.cs:365`) | Discovery and experimentation are missing: a small per-station experiment set is slice scope (C12:263-302, 427). Recipe books as items are missing too. |
| K3 | Quality outcomes | COMPLETE | `Crafting.cs:74-89` | The YAML crude max of 40 differs from the code default of 50. Masterwork belongs to I4/I5. |
| K4 | Skill gain | COMPLETE | Practice XP, first-craft Production XP and a novelty bonus (`World/Runtime/Crafting.cs:198-203`) | — |
| K5 | Recipe coverage | PARTIAL | 12 recipes on 4 station kinds | `boneleaf`, `herb_ashbloom` and `timber` are unused as recipe inputs. |

### 1.6 Economy

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| E1 | Merchants and the buy/sell UI | COMPLETE | `TradeSystem` (`World/Runtime/Social.cs:493-575`); trader column in `InventoryPanel` | — |
| E2 | Currency | COMPLETE | One "coin" (`RuntimeState.cs:137`; O-06:63) | — |
| E3 | Living economy: supply and demand, stock, prices, restock, merchant purse, NPC trade | MISSING | `stock_mode: fixed` (both merchant files); merchant coin is unlimited (`Social.cs:491`); `economy.yaml` holds only `sell_ratio: 0.4`; a bought-out trader stays empty by design (a665dbf) | Canon: ECONOMY.md (whole document), S-36 (`canon/SYSTEMS.md:393-399`), the MerchantProfile schema (`canon/DATA_MODEL.md:603-631`), exploit guards E-1 and E-2 (`canon/GAMEPLAY_LOOPS.md:232-233`). See §2.14. |
| E4 | Price table and sinks | PARTIAL | Buy = value × bias; sell = 40% (`Domain/Items/Items.cs:225-231`) | No sinks beyond buying. ECONOMY:136-155 lists repair, fees and training. M10 owns the price table (O-06:66). |

### 1.7 Quests

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| Q1 | Quest model | COMPLETE | `Domain/Quests/Quests.cs` (574 lines): objective graph, rewards, repeat cooldown | 16 objective types are explicitly not implemented (`Quests.cs:150-167`), including `dialogue_choice`, `faction_*`, `escort` and `boss`. The `reputation` and `title` rewards are not built (`:214`). |
| Q2 | Journal | COMPLETE | `Ui/JournalPanel.cs` (Active/Done/Failed) | No main-story track section. No Recent or notes (PJ10:250). |
| Q3 | Tracker | COMPLETE | `Hud.SetTracker` (`Hud.cs:365`; `Main.cs:1491`) | — |
| Q4 | Main-story framework | MISSING | No track, act or chapter concept; story is quests chained by dialogue; 6 story flags in `content/world_flags` | DCP:475 asks for a journal "main" track, choice flags, markers for tracked beats and `on_new_game`. Act I is "A Breath Late", beats M1-M8, with `world.main.carrier` (DCP:416-432). |
| Q5 | Choices and consequences | PARTIAL | One real fork: `content/quests/ashen_hollow/haldas_tally.yaml:17-25` | No generic choice record; no `dialogue_choice` objective. |
| Q6 | Bounties | COMPLETE | 9 bounties; `repeat_policy` enforced at `Quests.cs:338` | — |
| Q7 | Engine hooks the story needs | PARTIAL | — | Missing: `defeat_boss` and writing a flag on a creature kill (Barrowworks is blocked on this), `companion_present`, `travel_to`, `waystone_known` (NA:305, 463), `on_new_game` (DCP:302). |

### 1.8 Combat

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| X1 | Melee | COMPLETE | `Domain/Combat/Combat.cs`; `World/Runtime/Combat.cs` | — |
| X2 | Ranged and ammo | COMPLETE | `World/Runtime/Combat.cs:351,503` | — |
| X3 | Magic in combat | COMPLETE | `World/Runtime/Magic.cs:25-42` | — |
| X4 | Block | COMPLETE | `Domain/Combat/Combat.cs:117-119,194`; guard-broken toast at `Main.cs:1776` | — |
| X5 | Dodge | COMPLETE | `Domain/Combat/Combat.cs:120-123` | — |
| X6 | Stamina | COMPLETE | `Domain/Combat/Combat.cs:124-126` | — |
| X7 | Creature AI | COMPLETE | `World/Runtime/Creatures.cs:351-469` (idle, wander, patrol, engaged, flee, leash) | Boss telegraphs are capped at 2 (Barrowworks OQ-15). |
| X8 | Death and respawn | PARTIAL | XP debt, bound-stone return, Weakened (`World/Runtime/Combat.cs:985-1018`); the death sequence fix is 0ec24d8 | **Downed when a companion is present** was ruled (OD-13 = B, DCP:912) and is not built. |
| X10 | Loot and corpses | COMPLETE | 21 loot tables, seeded rolls (`World/Runtime/Items.cs:500-507`); corpse decay (`Creatures.cs:39-118`); container view with take-all | Content: husk, spider and hound have no loot table (DCP:661). Take All should close to play (OPEN_ITEMS 24). |

### 1.9 World

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| W1 | Day and night | COMPLETE | 48-minute day starting at 07:30 (`content/config/time.yaml`); `SkyView.Advance` (`Greybox/SkyView.cs:211-235`); creature hours (`Content/CombatContent.cs:95`) | — |
| W2 | Calendar | MISSING | — | Canon calendar (ORR:877-886): a Reckoning is 13 tallies × 14 days + Hollow Day. Seasons and festivals run on it. Restock (E3) needs it. |
| W3 | Weather | PARTIAL | Picked at launch from `--visual weather=`, default "fair" (`SkyView.cs:309`; `VisualOptions.cs:191`); presentation only (`Greybox/WeatherStates.cs:2`) | No cycle, not simulated, not saved. Canon: a regional state machine (WORLD_ARCHITECTURE:395); slice = simple regional weather (WEATHER…:707-712). See D6. |
| W4 | NPC life simulation, tiers A-D (needs, work, routines, homes) | MISSING | `NpcSystem.Tick` only turns NPCs to face their site or the player (`World/Runtime/Social.cs:149-164`). `schedule` is a stub content kind (`Content/SchemaResolution.cs:240`), and `schedule_ref` is ignored (`Content/SocialContent.cs:85`). The tier classifier is finished: `Domain/Spatial/Tiers.cs:7-52`, `TierSystem` in `World/Runtime/Systems.cs` (around 498-540), and `content/config/simulation_tiers.yaml`. The B and C simulations are empty stubs (`StubTierSimulation`, `Systems.cs:554`). | Canon: D-06 (`canon/DECISIONS.md:145-156`), `canon/WORLD_ARCHITECTURE.md` §7 (275-313) and NPC_SIMULATION.md (5 classes, tiers A-D). See §2.13. |
| W5 | Factions and reputation | MISSING | Placeholders only (`Quests.cs:163-164,215`) | The full system is on `origin/claude/m7-factions-building` (section 4). Waystation and Survey become chapters of the Wayhold and the Long Survey (DCP:213-217). |
| W6 | Companions and party control | PARTIAL | `World/Runtime/Companions.cs` (653 lines): recruit, follow/wait (G), downed/revive, catch-up, travel with the party. `CompanionRecord` holds only order, condition, pose and health (`World/PlayerState.cs:59`). `content/config/companion.yaml`. `Ui/CompanionsPanel.cs`. Only Tavar is a companion. | Missing: a party cap (player + 3, EXT/COMPANIONS:16), stances, tactics and the immediate commands (:106-128). Bug: companions freeze after a jump of more than about 150 m (NA:275). |
| W18 | Hirelings: contracts, wages, roles | MISSING | — | EXT/COMPANIONS:60-72, 213. See §2.15. |
| W19 | Companion approval, banter, personal quests | PARTIAL | The 5-dimension relationship model (`Domain/Social/Social.cs:36`) and barks exist | Missing: reactions to the player's deeds, party banter, a companion personal-quest hook (EXT/COMPANIONS:38-58, 189-191). |
| W20 | Companion progression and equipment | MISSING | No skills, XP or gear on `CompanionRecord` | EXT/COMPANIONS:74-92. |
| W16 | Persistent clock and offline continuation ("while you were away") | MISSING | The world tick resumes from the save (`Application/GameSession.cs:237`). `TickScheduler.RunTicks` ("catching up after offline time") is dead scaffolding with an empty `RunTick` (`Application/TickScheduler.cs:40-67`). `DateTime.UtcNow` is used only for the `CapturedAt` metadata (`GameSession.cs:298`). Creature respawns and node refills are already tick-based (`Creatures.cs:~752`; `Domain/Crafting/Crafting.cs:92-97`), so a catch-up would complete them naturally. | Canon: bounded catch-up as a pure function of `tick_delta`, persisted as `world_time_advanced_ticks` (`canon/PERSISTENCE.md:164,268-277`); cap "e.g. 30 game-days" (`canon/WORLD_ARCHITECTURE.md:248`); OD-18 open (PJ14:463-482). See §2.12. |
| W17 | NPC adventurers | MISSING | No `Adventurer` code on any ref | NPC_SIMULATION.md §§3-10 and 12; QUESTS_DUNGEONS…:39-82. See §2.13. |
| W7 | Interiors and dungeons | PARTIAL (in flight) | Interiors are furnished in-world, with doors as flags (`content/regions/ashen_hollow.yaml:249-262`); the space system is on `feature/barrowworks` | Barrowworks still owes: space persistence (v16), flag on creature defeat, the wedge opening G3, a 3rd telegraph, audio ids (report:137-173). |
| W8 | Travel | COMPLETE | `Domain/Travel/Travel.cs`, `World/Runtime/Travel.cs`, `Ui/WaystonePanel.cs`; schema 15 | Bed return points (N5) come later. Multi-region streaming is not needed while both areas sit in `region.starting_frontier`. |
| W9 | Map | COMPLETE | `Ui/MapPanel.cs` (discovered places, roads walked, stones, arrow) | Fog of war and annotations are open (OPEN_ITEMS 23; decisions C1-C4). |
| W10 | Compass | PARTIAL | Bearing only (`Ui/Compass.cs:10`) | Markers for the tracked story beat (DCP:475). The owner's compass-over-minimap doctrine stands. |
| W11 | Discovery | COMPLETE | `LocationDiscovered` (`World/Runtime/Events.cs:30`), toast at `Main.cs:1547`, stinger | — |
| W12 | Gathering and respawn | PARTIAL | 10 node files with daily refill (`World/Runtime/Crafting.cs:56,95`) | No sign that something is gatherable: "no indication when something can be gathered from" (ORR:1312-1420; UIG #9). |
| W14 | Housing / building | MISSING | `construct_building` not built (`Quests.cs:155`) | The full system is on the m7 branch (section 4). The owner wants to "build freely anywhere" (FPT:56; SPN:20). The OD-16 ladder is at PJ14:417-421. |
| W15 | Mounts | MISSING (DEFERRED-BY-RULING) | — | N6: horse and pack donkey **after the Undermeasure slice**; no mounts in PT-3 or PT-4 (NA:918; O-02:65). |

### 1.10 Dialogue

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| D-1 | Branching | COMPLETE | 8 dialogue files; reply purpose tags | — |
| D-2 | Conditions and effects | PARTIAL | `Domain/Social/Social.cs:90-170` | Missing: E-6 conditional node text `variants`, E-7 `deed` condition (NPB:506-519). Bug: a `world_state` condition naming `location.foldscar` throws outside its region (`DialogueSystem.CellOf`, NPB:476-479). |
| D-3 | Relationship and trust | COMPLETE | 5 dimensions (`Social.cs:36`), used in 7 of 8 files | Trust-tier perks are slice scope (O-04:31). |
| D-4 | Barks | COMPLETE | `Social.cs:53`; `World/Runtime/Social.cs:444`; npc-voice-barks merged | E-5 polish: subtitle option, distance fade, audio hook. |
| D-5 | Voice-over hooks | MISSING | A reply only plays `sfx.ui.select` (`Audio/SoundEvents.cs:308`) | E-10 stable line ids (`dialogue.id/node`, `bark.n`) and a VO lookup. VO comes after text (E13; OD15:77). |

### 1.11 Persistence

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| P1 | Save slots: manual, quick, auto | COMPLETE | `Persistence/SaveStore.cs:17,41` (5 rolling autosaves); F5/F9 (`Main.cs:2243-2244`) | Under "normal MMO rules", whether a player may load an older state at all is a decision (DN7). |
| P2 | Autosave triggers | PARTIAL | Every 300 s of playtime (`Application/GameSession.cs:331,373`) | No event triggers on travel, quest completion or entering a space (NA:602-620). Death is not a trigger, by design (PJ10:80). |
| P3 | Migrations | COMPLETE | 14 migrations, v1→v15 (`Persistence/Migrations.cs:72-85`); one fixture per version | — |
| P4 | Settings persistence | PARTIAL | Two separate files (S8) | — |
| P5 | Save UX | PARTIAL | — | Missing: per-character folders (OD-14), the save-before-quit prompt, a notice for damaged saves (PJ02). |

### 1.12 Feedback and onboarding

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| O1 | Tutorial hints and guidance | MISSING | F1 help only | Guidance levels Full/Light/Off, starting at Full (PJ07:109-119); no modal tutorials (PJ07:13); introduction timetable at PJ07:133-163; hint reset (PJ03 §8). |
| O2 | Notifications | COMPLETE | About 40 toast sites; `Ui/HudView.cs:933` | No notification-duration setting (S6). |
| O3 | Level-up feedback | COMPLETE | `Main.cs:1561`; banner at `HudView.cs:342-475`; sound (audio recovery) | — |
| O4 | Skill-up feedback | PARTIAL | `SkillPracticed` writes a log line only (`Main.cs:1778-1781`) | Needs a toast and a sound. |

### 1.13 Audio

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| A1 | Music | COMPLETE (interim) | `Audio/MusicPlayer.cs` and `Audio/MusicChoice.cs`: title, settlement day/night, combat, discovery stinger | The final score needs a download OK: ACE-Step or Kevin MacLeod (AUDIO_RECOVERY:116-117). |
| A2 | SFX | PARTIAL | `Audio/SoundEvents.cs` handles 27 events | Not hooked: QuestStarted, PlayerRespawned, SkillPracticed, companion hurt/downed, travel, building, doors (AUDIO_RECOVERY:45). Animal voices are silent. |
| A3 | Mixing | PARTIAL | 3 buses created at runtime | Missing: ambience and interface buses, a committed `default_bus_layout.tres`, ducking for VO. |

### 1.14 Performance, options and errors

| # | System | Status | Evidence | Gap |
|---|---|---|---|---|
| R1 | Performance options | PARTIAL | Quality tiers (`RenderTiers.cs`); harness in `Presentation/Perf` | Missing: dials and sliders (OPEN_ITEMS 20), a fix for the ~142 s startup load of 546 models (OPEN_ITEMS 22), 4070 Ti profiling (21). |
| R2 | Crash and error reporting | PARTIAL | `user://logs/godot.log` (`Main.cs:187`); fatal `OS.Alert` (`:195`); event handlers isolated (`:592-598`) | No `AppDomain.UnhandledException` or `TaskScheduler.UnobservedTaskException` hook. No crash marker with a next-launch notice (PJ02). **There is no network code anywhere in `src/`. Keep it that way.** |
| R3 | Release hygiene | COMPLETE | Dev keys F3/F4 limited to debug or `--dev`; clean export script; third-party notices in the payload | — |

### 1.15 Counts

| | COMPLETE | PARTIAL | MISSING | Total |
|---|---|---|---|---|
| Front end (F1-F10) | 3 | 3 | 4 | 10 |
| Settings (S1-S9) | 1 | 3 | 5 | 9 |
| Character (C1-C10) | 4 | 3 | 3 | 10 |
| Items (I1-I10) | 3 | 5 | 2 | 10 |
| Crafting (K1-K5) | 2 | 3 | 0 | 5 |
| Economy (E1-E4) | 2 | 1 | 1 | 4 |
| Quests (Q1-Q7) | 4 | 2 | 1 | 7 |
| Combat (X1-X8, X10; X9 removed) | 8 | 1 | 0 | 9 |
| World (W1-W20; W13 removed) | 4 | 6 | 9 | 19 |
| Dialogue (D-1-D-5) | 3 | 1 | 1 | 5 |
| Persistence (P1-P5) | 2 | 3 | 0 | 5 |
| Feedback (O1-O4) | 2 | 1 | 1 | 4 |
| Audio (A1-A3) | 1 | 2 | 0 | 3 |
| Performance and errors (R1-R3) | 1 | 2 | 0 | 3 |
| **Total** | **40** | **36** | **27** | **103** |

Two of the 27 MISSING are deferred by an owner ruling: gamepad (S7) and mounts (W15). Revision 2 removed X9 (difficulty: no presets, by owner ruling) and W13 (rest and time-skip, out under MMO rules per DN8). It added W16-W20 and C10 (trainers), and widened W4 and E3. DN8 (no time-skips) has since been accepted.

The core loop is genuinely complete:
- combat
- magic
- crafting with quality
- trade
- quests and bounties
- travel
- saves and migrations

The gaps cluster in five places:
1. **The front door:** settings, creator, opening, credits.
2. **Itemization depth:** item classes, durability, encumbrance.
3. **The persistent, living world.** This is now the largest block:
   - the offline clock
   - NPC lives
   - adventurers
   - a self-running economy
   - weather and calendar
   - factions and building
4. **Party play:** companions and hirelings.
5. **Narrative plumbing:** the story track, missing objective types, dialogue extensions, VO hooks.

---

## 2. Gap plan

Effort is in agent-days and includes tests, lint/xref and, for presentation work, a screenshot check.

**Executors:**
- **LC = local Claude:** GPU, Godot visual work, assets, anything that needs a running editor or screenshots.
- **CC = cloud Claude:** pure C#/YAML systems plus tests, from the GitHub branch.
- **DS = DeepSeek:** long, self-contained work.

### 2.1 Front end and settings

- **F1, F5, F6, F7, F10, S1-S6, S8, S9: front end and settings.**
  - Title screen:
    - Make `SavesPanel`'s start mode a real title: wordmark, version text, live hollow backdrop (`StartCamera`).
    - Buttons in PJ02 order: Continue, New Game, Load, Settings, Credits, Quit.
  - One `SettingsPanel` with five sections, applied at once:
    - **Display:** window mode, resolution, render scale, vsync, frame cap, brightness, quality tier plus per-feature dials.
    - **Audio:** master, music, effects, ambience, interface.
    - **Controls:** remap every `DefineInput` action, with a conflict check, reset, and hold/toggle for sprint, walk and crouch.
    - **Camera:** sensitivity, invert Y, FOV 60-100, shake, bob and flash reduction.
    - **Interface:** UI scale, text size, high contrast, notification duration, subtitles on/off, guidance level and hint reset.
  - Persistence: one `user://client_prefs.json` through a `SettingsStore`, which replaces the two loose files.
  - First launch: offer four presets (PJ03 §8).
  - Credits screen:
    - Built from the payload's THIRD_PARTY_NOTICES (Godot, .NET, Terrain3D, Sky3D, COGITO, CC-BY credits, AI-use note) plus an owner team line.
  - Quitting:
    - It writes the character's live state ("logging off"; see §2.12).
    - There is no save-before-quit prompt. Under MMO rules the game always saves on quit.
  - **No pause** (owner ruling 2026-10-17):
    - Restore the `NoPauseDoctrineTests` architecture guard from ASTRAL (UI/UX transcript, events 2320-2323). It fails the build if `src/Presentation` sets `Paused`, `ProcessModeEnum.WhenPaused` or `Disabled`, or `Engine.TimeScale` outside the `VisualAudit` capture harness.
    - Add a test that the tick is not gated on menu state.
    - No "pause while panels are open" option.
  - Port the 5-row ASTRAL SettingsPanel and SettingsStore from the transcript patch scripts (UIG §4.2: events 55, 57, 2369, 2478) as the template.
  - Colour-vision mode: one palette swap in `HudStyle` for the danger/aid chips. That is the only colour-coded information.
  - **Effort:** 6. **Depends on:** WP0's Main.cs split. **Executor:** LC (the store and InputMap persistence could be CC, but it is faster in one pair of hands). **Decisions:** none left. D2 (gamepad out) was accepted.
- **R1: performance.**
  - Per-feature dials (shadows, GI, foliage density, draw distance) in the Display section.
  - Distance-streamed model loading to cut the ~142 s startup (OPEN_ITEMS 22).
  - **Effort:** 3. **Executor:** LC (GPU, profiling).
- **R2: crash stance.**
  - Add `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` hooks that write `user://logs/crash_<utc>.txt` and a crash marker.
  - On the next launch, the title shows "Otherreach closed unexpectedly", with a button that opens the log folder.
  - Damaged-save notice in `SavesPanel`.
  - **Local only. No upload, telemetry or update check, ever.**
  - **Effort:** 0.5. **Executor:** CC (delivered in WP10).

### 2.2 Character creation, peoples, Calls, opening

- **F2, F8, C8, C9, P5 (per-character folders): character creator and new-game spec.**
  - **Domain part (CC):**
    - A `CharacterSpec`: name (1-24), pronouns (he/she/they), `people` (only `people.veth` exists), body preset id, `call` id.
    - There are no World Rules or difficulty presets (owner ruling 2026-10-17). OD-11 is closed as "standard rules only", and XP debt stays the one death consequence.
    - `GameSession.NewGame(CharacterSpec)` replaces the hard-coded "Wanderer".
    - Calls as content (`content/calls/*.yaml`): 4 CC-0 Calls (Wayfarer, Scholar, Smith's hand, Hedge healer). Each has 4 attribute-bias points, 2 skills at 15, kit and a contact, built only from existing items, skills and NPCs (C11:43-49, 109). The Call wording comes from C11; no new lore.
    - A Suggest name list from the canon Veth name rules.
    - Save fields in the identity section, plus per-character save folders (OD-14).
  - **UI part (LC):**
    - Creator flow following PJ04: People (Veth only, others hidden, PJ04:51) → Body → Identity → Call → Confirm, plus Quick start.
    - Body: a male/female blend preset (OD15:20), 3 build presets from the archived MetaHuman blends (OD15:21), rotate/zoom/lighting, randomise.
    - Face sliders are out (CC-1+).
  - **Effort:** CC 2.75, LC 4. **Depends on:** schema slot v17 (§5.2); the MetaHuman garment/outfit system for a clothed preview (Phase 4.1; an unclothed fallback is acceptable). D3 was accepted.
- **F9: opening, "the Leak", E1 = B.**
  - New game spawns the player lying down, with control in 10 s or less.
  - Sky-blend hook, wake-up and get-up clip, 4 relic trinkets, a first journal entry ("The same afternoon").
  - Skip is offered only after the player has completed it once (PJ14:30-46; DCP:448-463).
  - **Effort:** LC 3 (animation and staging); the CC part is in WP4 (`on_new_game` hook and journal entry). **Depends on:** anim/locomotion Phase 3 for the get-up clip; WP4.

### 2.3 Character progression UI

- **C4: skills page.**
  - Port `SkillsPanel` with `SkillsModel`/`TechniqueModel` from the transcript (patch 2524; events 49/52), adapted to `CharacterProgression`.
  - Key P, a System Menu row, and "See all skills [P]" on the Character sheet.
  - **Effort:** 1.5. **Executor:** LC.
- **C7: spellbook and quick bar.**
  - A "Known" tab with spells and techniques: cost, Strain, complexity versus skill, and the "why can't I" line (C26).
  - An assignable 8-slot quick bar (PJ09; C26) for spells, techniques and consumables, replacing the fixed 4-6 and 1-3 bindings. Slot assignments are saved.
  - **Effort:** 2.5. **Executor:** LC for the UI; the bar state could be CC. **Depends on:** WP0, S3 (rebindable slots).
- **C5: player techniques.** See D11.
  - Recommended minimum: one active technique per weapon family (blade, polearm, bow), using the existing technique model.
  - They are taught by Kera, Durn and Tavar with `give_technique` effects and triggered from the quick bar.
  - **Effort:** CC 2. **Depends on:** C7.
- **O4: skill-up feedback.**
  - A quiet toast "Bow 14" and a sound on `SkillPracticed` when the level changes.
  - **Effort:** 0.25. **Executor:** CC.
- **C10 (new in revision 2): trainers as a system, sized to the current areas.**
  - **Canon rules it must keep:**
    - "No teacher, item or payment raises a skill"; a teacher grants a technique at skill 0 and provides challenging practice (`canon/PROGRESSION.md:64,223`).
    - Learning is a typed event with a source; `teacher` is one of nine (`C04:105-121`).
    - A fee buys access, never standing (`C12:38`; GAMEPLAY_LOOPS E-7 at :238).
    - Teachers gate by threshold, trust and fee (`C12:263-274`).
    - "Nobody meets every teacher" (`PROGRESSION.md:242`).
    - Every technique names at least one findable source (P-7, `C01:172`).
  - **Teaching today:** recipes only, through `give_recipe` in the dialogue of Kera, Sel, Durn and Tam. Gates are only node-visited checks plus Kera's trust +5. No spell is NPC-taught; they come from tomes.
  - **The system:**
    - A `teaches:` block in NPC data. Each entry has:
      - a kind: recipe, technique, spell, or building piece;
      - an id;
      - requirements: skill band, trust, a prerequisite known entry, an optional deed or quest flag;
      - a fee in coin, which is a sink feeding the economy (§2.14).
    - **Availability follows the teacher's routine:** they teach only at their work anchor during work hours (§2.13).
    - One generic `teach` effect replaces the hand-written `give_recipe` lines. Existing dialogue is migrated by a content rewrite with identical outcomes.
    - A **Learn** column in the conversation's context sheet lists what this teacher offers, with met and unmet requirement chips and the fee. It uses the same reason grammar as the Skills page (C26).
    - Companions can teach through the same block (C04:121).
  - **Content, only what the current areas need:**
    - Kera: smithing and fletching recipes; the blade technique (D11).
    - Durn: smithing and leatherwork recipes; the polearm technique.
    - Tavar: the bow technique.
    - Sel: the herbal recipes.
    - Tam: cooking.
    - **The timber building pieces (WP8)** need one teacher from the existing cast. Berrin, who keeps Cairnwold's long lodge, is the natural fit, but that is the owner's call.
    - The three spells stay taught by tomes, as canon allows ("a teacher or a book"). Adding a spell teacher is a content choice and needs no system work.
    - **Coverage rule:** every recipe, technique, spell and building piece in content must have at least one findable source. A lint enforces it (P-7).
  - **Effort:** CC 2, LC 0.5. **Depends on:** WP12 for work-hour availability (always-available until it lands), WP14 for fee flows (a plain coin sink until it lands). It is added to WP9.

### 2.4 Itemization and economy

- **I4, I6: item classes.**
  - `item_class` on definitions: ordinary, masterwork, notable, artefact, completed_artefact.
  - Rarity is derived from the class and is no longer authored, so lint must reject a mismatch (DCP:903).
  - A **notable-instance record** on the stack: name, history line, familiarity 0-5, one quirk (OD-C07: familiarity is never generic damage). It needs a schema slot.
  - The tooltip shows class, provenance and familiarity pips (DCP:673-676).
- **I5: quality.**
  - Armour quality: ±1 armour per step, mirroring weapons.
  - Drop quality rolls weighted by creature tier (DCP:662).
  - Masterwork +2 is craft-only, from a fine roll on a recipe flagged `masterwork_possible` at an upgraded station, and carries a maker's mark. No drop may reproduce it (PROGRESSION:424).
- **I2: slots content.**
  - Use the existing slots:
    - Hands: smith gloves.
    - Legs: wool trousers.
    - Feet: boots.
    - Cloak: cloak.
    - These four garments are already in the wardrobe plan (WEARABLES:240-262).
  - Add an off-hand torch crafted at the forge. It is a light source and gives a stealth cost (owner ORR:1333).
  - Add one hide or timber shield. This needs no new slot logic.
- **I9: graded encumbrance (D4).**
  - Above 80% load: sprint drain ×1.5, no stamina regen while sprinting, and the dodge costs +25%.
  - The 100% refusal stays.
  - A HUD weight chip appears when the load is above 80%.
- **I10: durability and repair (D5).**
  - `durability_max` only on weapons, armour and tools. Wear comes from use events: a hit, being hit, a gather.
  - At 0 the item is **worn out**: it counts as crude, and it never breaks or disappears.
  - Repair at an anvil or workbench with one material plus a skill check. The result is re-rolled and capped at the original quality (C13:226).
  - Merchants also repair for coin, which is the first coin sink (C12:427).
  - It reacts to Combat and Gather **events** in the Items system, so it does not edit `Combat.cs`.
- **E3: restock and merchant coin.** These moved to the living economy (§2.14, WP14). WP3 no longer touches merchants beyond the repair service.
- **E4: sinks.** Repair and training fees, plus hireling wages (§2.15). They feed the merchant purses of §2.14.
- **K2: discovery.**
  - A recipe-book item kind (a `teaches_recipe` use like tomes).
  - A per-station experiment set: 2 at the workbench and 1 at the forge. It is seeded, so a reload changes nothing (C12:263-302).
- **K5:** recipes or uses for `boneleaf`, `herb_ashbloom` and `timber`, plus `skill_ref` on `haldas_bow` (content).
- **Effort and executor:** 9 in total, in two sequential sessions.
  - WP3a, about 5.5: classes, notable record, quality, slots content, discovery.
  - WP3b, about 3: durability, encumbrance, sinks.
  - Executor: CC; the tooltip delta and pips are a small presentation edit that LC checks later.
  - Depends on: one schema slot per session (§5.2). D4 and D5 were accepted.
  - Wear while away: offline catch-up (§2.12) does not wear the player's gear, because the player does nothing while logged off. A hireling's loaned gear wears through the hireling's abstract work (§2.15).

### 2.5 Quests and story framework

- **Q4: story tracks.**
  - `track: main | side | bounty` on quests, plus `act`.
  - A Main section in the journal ("The story so far"), with the Recent list and notes (PJ10:250).
  - Choice records: `choice` effects that set a namespaced world flag (`world.main.carrier`) and log a journal line.
  - `on_new_game` starts (DCP:302, 407).
  - Only the framework plus the Act I beats that sit in the two current areas; Act I content follows DCP:416-432 and is not invented here.
- **Q5, Q7: objective types.**
  - `defeat_creature` / `defeat_boss`, with a flag written on a creature's death through an event-driven listener. Barrowworks needs this for the Custodian.
  - `companion_present`, `dialogue_choice`, `travel_to`, `waystone_known`, and the `reputation` reward once WP6 lands (it is gated by content lint until then).
- **W10: compass marker.** One marker for the tracked beat's target location when it is known. There is no minimap.
- **P2: event autosaves.** Autosave on quest completion, travel arrival, entering or leaving a space, and Call/creator completion. Scoped and backgrounded, following PERSISTENCE §7.4.
- **Effort:** 5. **Executor:** CC; the marker and journal section are small Presentation edits that LC screenshot-checks. **Depends on:** WP0. It must merge before WP6's reputation reward and before Barrowworks' final quest wiring.

### 2.6 Dialogue engine and VO hooks

- **D-2:**
  - E-6 `variants: [{text, conditions}]` on nodes.
  - E-7 `deed` condition, which restores `act_done`.
  - Fix the region-safe `world_state` crash in `DialogueSystem.CellOf` (NPB:476-479).
- **D-5:**
  - E-10 stable line ids, derived as `dialogue.<id>/<node>` and `bark.<npc>.<n>`, never hand-written into YAML.
  - An optional `vo/<line-id>.ogg` lookup: if the file exists, play it on a new Voice bus and duck the music.
  - Subtitles always show for VO and barks when S6 subtitles are on.
- **D-4:** E-5 bark polish: distance fade and the audio hook.
- **Effort:** 3.5 (NPB:530-535 estimates about 3.5 for this cluster). **Executor:** CC; the Voice bus line in `AudioMix` is shared with WP1 (see section 3.2). **Depends on:** none.

### 2.7 Factions and reputation

- **W5:**
  - Re-port, not merge, `origin/claude/m7-factions-building`'s faction slice:
    - `src/Domain/Factions/Factions.cs` (FactionRules, Ledger, StandingLadder, ActRecord, Knowledge)
    - `src/World/Runtime/Factions.cs` (FactionSystem)
    - `src/Content/FactionContent.cs`, `content/config/factions.yaml`, `content/factions/*`
    - The FAC001 lint and its tests: `FactionRulesTests`, `FactionTests`, `ReputationTableTests`, `FactionContentTests`
  - Factions are renamed as local chapters: Waystation becomes the Wayhold chapter (Renn, Kera); Survey becomes the Long Survey chapter (Sel) (DCP:213-217). Cairnwold's lodge needs a canon ruling before it gets one (GAP:16).
  - Report-only standing: "a faction learns of an act only when you tell one of its members" (PJ07:157, 274). Reputation never decides who attacks (O-04:66).
  - Hook up `faction_reputation` rewards and the `faction_*` objectives (`Quests.cs:163-164,215`) and a dialogue `standing` condition.
  - Port the ReputationPanel from the transcript (UIG: events 55, 57, patch 2487) and drop the F6 debug panel.
- **Effort:** 3.5 (port estimate 2-3, plus the panel). **Executor:** CC for systems; LC for a screenshot pass on the panel. **Depends on:** WP4 (objective/reward plumbing) and WP5 (the condition parser); a schema slot (§5.2).

### 2.8 Calendar and weather (schedules moved to §2.13; rest removed)

- **W2: calendar.**
  - A `Calendar` that is a pure function of the world tick: day, tally (1-13), Reckoning, Hollow Day (ORR:877-886). It is shown in the System Menu status strip and on the map.
  - Tallies are numbered: the owner accepted "14th day of the 3rd tally, first Reckoning".
  - The economy (§2.14) and the life simulation (§2.13) key their day and tally rhythms to it.
- **W3: weather (D6 accepted).**
  - `WeatherSystem` in `World/Runtime`: a seeded regional state machine with fair, overcast, rain, fog and wind, plus a reserved `fold` state. Fronts last 4-12 game hours. It is saved and raises `WeatherChanged`.
  - **It is a pure function of (seed, region, hour index) through `RngChannel`.** That makes offline catch-up trivially chunk-invariant (§2.12), and the load reads "what the weather is now" without stepping through it.
  - `SkyView` and `WeatherStates` read the simulation; `--visual weather` stays as a dev override.
  - There are no survival effects. NPC routines react to weather (§2.13).
- **W13, rest and time-skip: removed.**
  - Under "normal MMO rules" a player cannot fast-forward the shared world. Beds set the return point and restore pools, but they do not skip time.
  - The built Waystone travel advances world time by ¼ of the road walk (travel/groundwork). That is a time-skip too. DN8 asks whether to keep it.
- **Effort:** CC 2 (calendar 0.5, weather 1.5). LC 1.5 (weather visuals from the simulation, the calendar line). D6 and D13 are settled.

### 2.9 Building and housing

- **W14:**
  - Re-port the m7 building stack:
    - `Domain/Building/Building.cs` (catalog, 3 m lattice, snapper, sockets)
    - `World/Runtime/Building.cs` and `BuildingRules.cs`
    - `content/pieces/*` (7 timber pieces, anvil station, storage chest)
    - `Player/BuildMode.cs` and `Greybox/StructuresView.cs`
    - Its tests and BLD lints
  - **The navigation port has moved to WP12**, which needs it first for NPC movement. WP8 consumes it: footprints and restamping on placement.
  - Port the BuildPanel and build palette from the transcript (UIG: events 1011 and 2532), restyled to the Field Instrument kit (SPN:20).
  - Storage chests give player storage.
  - Hireling roles (§2.15) can work at player-built stations. That reuses the m7 errand `BeginWork` path, which is ported in WP12.
  - **While away:** player buildings persist and hirelings keep working at them. Canon has property threats, but they belong with settlement threats (PJ11:269-285) and are out of this bar. Buildings take no off-screen damage.
- **B1-B4, proposed defaults (D8 accepted; values for confirmation):**
  - B1: one authored plot near each town, plus free building in the wild.
  - B2: 256 pieces per region.
  - B3: no building within 6 m of a road or inside town zones.
  - B4: NPCs never use wilderness builds, except the player's own hirelings.
- **Effort:** DS 6, LC 1. **Depends on:** WP12 (navigation); a schema slot.

### 2.10 Onboarding, Downed and audio hooks (difficulty removed)

- **O1: guidance.**
  - `content/hints/*.yaml`, about 20 hints on the PJ07:133-163 timetable, shown once each.
  - A non-modal HUD hint line; guidance level and reset in Settings.
  - New hints for the new systems: the while-you-were-away sheet, hiring, party commands, price changes.
  - **Effort:** CC 1.5, LC 0.5.
- **W12: gatherable affordance.** An outline or prompt on a node with charges. **Effort:** LC 0.5.
- **X8: Downed with a companion present (OD-13 = B, DCP:912).**
  - The player is Downed for 30 s and the companion revives them. With no companion, death works as today.
  - This is not difficulty. It is the party rule, and it extends naturally to hirelings (§2.15).
  - **Effort:** CC 1.5.
- **X9: difficulty and World Rules: REMOVED** by owner ruling. There are no presets, multipliers or New Game rules line.
- **A2, A3: audio.**
  - Hook the missing events, plus party and hireling events.
  - Add Ambience, Interface and Voice buses and a committed `default_bus_layout.tres`.
  - **Effort:** CC 1.
- **R2: local crash hooks** (from §2.1): CC 0.5 (accepted).
- **Effort:** CC 4.5, LC 1.

### 2.11 Interiors and dungeons (in flight, not re-planned)

- **W7:** DeepSeek finishes `feature/barrowworks`: space persistence at v16, the wedge opening G3, a 3rd telegraph, audio ids, and the licensing defect.
- WP4 provides the creature-defeat flag path.
- **New for revision 2:** flagged interior spaces continue while away, and unflagged ones are frozen (`canon/WORLD_ARCHITECTURE.md:393-394`). Barrowworks is unflagged, so the Custodian and the gates hold state.

### 2.12 Persistent clock and offline catch-up ("the world goes on while you're logged off")

**Old material (§4.1) gives most of the mechanism:**
- Canon specifies one integer `game_tick`, never advanced by the wall clock alone (`canon/WORLD_ARCHITECTURE.md:241-245`).
- Catch-up is a **pure function of the total `tick_delta`**, with the honoured amount persisted as `world_time_advanced_ticks`. "One 300-day step produces the same world as 300 one-day steps" (`canon/PERSISTENCE.md:164,268-277`; test T-22 at :619).
- The resume cap is bounded, "e.g. 30 game-days" (`WORLD_ARCHITECTURE.md:248`).
- What continues and what does not is listed in WORLD_ARCHITECTURE §9 (:379-401): "schedules, economies, populations, and records… not *action*".
- OD-18 is open. Its Option A is a bounded catch-up with clamped values and a short "while you were away" note (PJ14:463-482). The DECISIONS default is "no report in Phase 1-2" (`canon/DECISIONS.md:265`).
- The owner's 2026-10-17 ruling closes OD-18: the world continues, and the player gets a summary.

**Design:**

1. **The real-time bridge lives in Application, never in Domain.** The domain may not read the wall clock (`canon/SYSTEMS.md:78,441`).
   - On every save, `GameSession` writes `saved_utc` into the manifest. Today `CapturedAt` exists only as build metadata.
   - On loading the character's **live** save (DN7), `GameSession` computes `requested = max(0, now_utc − saved_utc) × 20 ticks/s`. The game clock already runs at real-time scale: one game day is 48 real minutes (`content/config/time.yaml`).
   - It then computes `honoured = min(requested, cap)` (DN1).
   - The domain receives only a tick count.
   - A clock moved backwards gives 0.
2. **Honoured advance is state.**
   - `world_time_advanced_ticks += honoured` is persisted, following canon.
   - The world tick jumps by `honoured`, and every system brings itself to the new tick.
3. **`IAbstractAdvance` per system** (World/Runtime). The interface is `Advance(fromTick, toTick, AdvanceLog log)`. Each system must be either **closed-form** or **stepped at a fixed abstract step** (one game hour for the life simulation, one game day for the economy). A chunk-invariance test proves `Advance(a, c) == Advance(a, b) + Advance(b, c)` (T-22). How each system advances:
   - **Clock, calendar, weather:** closed-form.
   - **Creature respawns, node refills, quest cooldowns:** already tick-deadline based, so they need nothing.
   - **Effects on the player:** expire (closed-form).
   - **NPC life (§2.13):** Tier-D hourly bands; outcomes from seeded event tables.
   - **Adventurers (§2.13):** daily goal and outcome resolution.
   - **Economy (§2.14):** daily production, consumption, trade, price relaxation, clamped to authored `[min, max]` (`DECISIONS.md:265`; RK-12 at `canon/RISK_REGISTER.md:242-258`).
   - **Hirelings, and companions left behind (§2.15):** wages, abstract work output, lives.
   - **Factions:** no off-screen acts; report-only standing does not change while the player is away.
   - **Player buildings:** persist; no damage.
   - **Flagged spaces:** continue (Barrowworks is unflagged).
4. **The existing scaffolding is replaced.** Delete `Application/TickScheduler.cs`, whose `RunTick` is an empty placeholder (`:40-52`), and use `Simulation` plus the advance pass.
5. **Load budget:**
   - Catch-up runs behind the loading screen, and the honoured cap must finish in **2 s or less** on the 3090 rig.
   - At the hour/day abstract step, 183 game days × about 40 agents is about 175k agent-hours of table lookups. That fits easily.
   - A benchmark test pins it.
6. **The player while away:**
   - The player stays where they logged off.
   - On login there is a **10 s login grace**: creatures that spawned near the player during the absence do not aggro. This matches MMO logins and has no menu.
   - Companions and hirelings who were in the party stay with the player. Dismissed ones live their lives (§2.15).
7. **"While you were away" sheet:**
   - `AdvanceLog` records only events the character could know: about known NPCs, merchants they traded with, hirelings and companions, bounties they saw, the towns' prices and news, and the weather now.
   - It shows as a dismissable Field Instrument sheet on login: time passed in game days and tallies, then grouped lines.
   - It is copied into the journal's Recent list (WP4).
   - It is **not modal-blocking**: the world runs underneath it, and the no-pause rule holds.
8. **Rollback rule (DN7):**
   - Catch-up applies only when the character's **live state** is loaded (Continue, or log in).
   - Rolling backups remain for recovery only, and loading one is an explicit "restore" with no catch-up and a warning.
   - Quickload is removed under MMO rules (DN7).
- **Save:**
  - Manifest fields `saved_utc` and `world_time_advanced_ticks`, plus an `AdvanceLog` tail (the last 50 lines).
  - Per-system state is saved in each owner's own sections.
  - One schema slot.
- **Effort:** CC 5.25 (framework, interface, manifest, chunk-invariance harness, clock/weather/respawn integration, log, login grace, benchmark). LC 0.5 (the sheet).
- **Depends on:** WP0. It merges before WP12, WP14 and WP15, which implement `IAbstractAdvance`.
- **Decisions:** DN1, DN7, DN8.

### 2.13 NPC life simulation and NPC adventurers

**Old material (§4.1):**
- **Canon tiers:**
  - A: full, near.
  - B: simplified regional: schedule bands, coarse position, routes between anchors, needs as coarse bands, no combat.
  - C: abstract schedule and economy.
  - D: stored state.
  - Sources: D-06, `canon/DECISIONS.md:145-156`; `canon/SYSTEMS.md:449`; `WORLD_ARCHITECTURE.md:250-346`.
- **Canon numbers:**
  - Radii: A 150 m, B 600 m, C 2000 m (built config `simulation_tiers.yaml`).
  - Caps: A=60, B=300, enforced by spawn suppression, never silent despawn (:259-263).
  - Hysteresis is at :315-328.
  - Promotion reconciles to a legal state; "an actor in combat cannot be demoted" (:290-313).
- **NPC_SIMULATION.md** (APPROVED DIRECTION):
  - Five classes: Ambient, Resident, Economic Agent, Adventurer Agent, Major Character (:12-29).
  - Adventurer state and goal chains: "need better bow → need money → find bounty…" (:31-54).
  - Contracts, parties, failure "die where permitted", progression and retirement (:97-159).
  - Planning is "deterministic/systemic where practical, not LLM-controlled" (:56).
- **QUESTS_DUNGEONS…:**
  - Adventurers take systemic and competitive opportunities; first actor wins (:39-82).
  - Repopulation by arrivals, never identical timer respawns (:97-113).
  - Role replacement (:133-147).
  - Resolved opportunities become history entries (:241-249).
- **Slice target:** 8 named NPCs plus about 14 Tier-B role NPCs, with observable schedules and no visible teleport in 30 minutes (`canon/VERTICAL_SLICE.md:46,260,373`).
- **Built code to reuse:**
  - The tier classifier and the `ITierSimulation` plug-in point (`World/Runtime/Systems.cs:547-565`; `Simulation.cs:144`).
  - The m7 **errand mover and navigation** (`origin/claude/m7-factions-building`: `World/Runtime/Errands.cs` 268 lines, `Navigation.cs` 201 lines, `Domain/Spatial/Nav*`, `content/config/navigation.yaml`). It is a deterministic, saved, never-teleporting NPC walker with door use and a repair pass at load. A routine is "a goal-chooser feeding the same mover".
- **No births or demographics design exists anywhere** (canon search). None is added here beyond arrivals.

**Canon conflict needing an owner ruling (DN2).** Canon forbids abstract tiers from doing combat, damage or death, inventory or equipment changes, or skill and wealth gain for named NPCs (`WORLD_ARCHITECTURE.md:275-288,385,397`; `DECISIONS.md:154`). NPC_SIMULATION and the owner's new direction need adventurers who level, earn, gear up and die off-screen. The two are reconciled like this:
- Abstract tiers still never simulate combat.
- They **resolve outcomes by rule**: seeded event tables for an activity give success or failure, injury, death, coin and abstract goods.
- Those outcomes write state (skills, wealth, gear tier, life state).
- Promotion reconciles to a legal physical state.

This amends D-06's mitigation text, so it needs the owner's explicit sign-off (AUTHORITY:21-31: owner rulings override the text they name).

**Design: residents and vendors (NPC_SIMULATION "Resident" and "Economic Agent"):**
- **Needs as coarse bands:**
  - Hunger, rest, social, coin (canon §7.1 allows coarse needs).
  - Each band is 0-3. Bands rise by hour and are satisfied by activities.
- **Routines:**
  - A utility goal-chooser over the activity set: work at the job site, eat (hearth, long lodge), sleep (home), socialise (NPC-NPC visits), shop (the market, §2.14), shelter (rain).
  - It is constrained by hour band and needs, uses the NPB E-1 activity ids, and its weights are authored per NPC in `content/routines/*.yaml`.
  - It replaces the stub `schedule` kind (`SchemaResolution.cs:240`).
- **Work produces:**
  - Each job has an output rate that feeds the settlement market (§2.14).
  - Examples: Kera and Durn make billets and tools, Mara herbs, Tam grain and stores, hunters hides.
- **Homes and anchors:**
  - Authored per NPC: home, work, eat, social.
  - Interiors use the doors and furnished buildings that already exist (`content/regions/ashen_hollow.yaml:249-262`).
- **Tier A:**
  - NPCs physically walk with the ported mover.
  - Talking pauses the routine (the NPB rule).
  - Merchants trade only while at work, and otherwise send the player to come back in the morning in character (the v1 D7 rule, kept).
  - Quest givers can always be talked to.
- **Tier B:** coarse position on routes at 2 Hz. **Tier C:** band cursor only. **Tier D:** hourly analytic advance, which is also the offline path.
- **NPC-NPC relationships:** a light pair table (friend, rival, partner). It drives visits and barks ("The world does not reserve every relationship for the player", EXT/COMPANIONS:201-203). Not romance.
- **Population:**
  - The 8 named NPCs plus a modest set of role residents: 6 per town (DN3, provisional). The current casts are **not** enlarged in this bar.
  - Role residents use existing MPFB and MetaHuman variants with no new art pipeline; LC dresses them. Character variety later comes from the MetaHuman/MPFB body archive (OD15:21), the garment library (WEARABLES) and CAS Phase 7 "Generic villagers" (`CHARACTER_ANIMATION_STRATEGY.md:564`, about 4 agent-days).
- **Uncapped by design (owner, 2026-10-17: "there shouldn't be a cap just yet"):**
  - No population limit in data, schema or code:
    - NPC records are a keyed collection with no fixed arrays.
    - Content lint has no maximum count.
    - Saves store NPC state sparsely per cell.
  - Canon's tier figures (A ≤60, B ≤300, C ≤20k records, `canon/WORLD_ARCHITECTURE.md:230-239,442-460`) are **performance budgets** applied by admission control and spawn suppression. They live in `content/config/simulation_tiers.yaml` and are tunable. They never limit how many NPCs exist.
  - Tier D and the offline advance are aggregate per settlement and job, so their cost grows with the number of settlements and roles rather than per-NPC detail.
  - **Structural test (cheap, in this bar):**
    - Load a synthetic save with 1,000 tier-D NPC records spread over cells.
    - Run one offline advance and a tier promotion and demotion cycle.
    - Assert that nothing throws, truncates or refuses because of the count, and that the records round-trip through save.
    - This is a correctness check with no frame-rate target.
- **Later, when cities are built (not in this bar):** see §3.3.
- **Death and replacement (DN4):**
  - The 8 named story NPCs are **essential**: they are never killed by rule, on-screen or off.
  - Role residents can die only through rule events. There are no such events in this bar beyond old age, which is not modelled, so in practice they can die only from adventurer-style work.
  - Vacancies fill with **arrivals** after 3-7 game days, each with a new identity and the same role slot (QUESTS:97-113, 133-147). Stability bounds keep every role filled.

**Design: adventurers (NPC_SIMULATION "Adventurer Agent"):**
- **State:**
  - Level and skills, on the player's own XP curve and skill rules (`ProgressionEngine`).
  - A gear tier as real items.
  - Coin, home or camp, risk tolerance, goal.
  - Faction standing is report-only.
- **Goal planner:**
  - The canon chain: need → money → bounty → buy → travel → hunt → collect → commission (NPC_SIMULATION:54).
  - It is a deterministic planner over a small action set.
- **Activities:**
  - **Take bounties from the same boards the player uses** (the 9 `content/quests/bounty` files). Competitive bounties go to the first actor, and the board shows "Taken by Aldric".
  - **Hunt:**
    - At tier A they really fight, reusing the companion combat behaviour, which WP15 extracts into a shared ally-combat helper.
    - Abstractly, they resolve by an outcome table against the creature population. Kills reduce the spawn count, which respawn timers then restore.
    - Hides and meat enter the market.
  - **Travel** the Old East Road between the towns, with no Waystones in the abstract.
  - **Buy and sell** at the merchants (§2.14): arrows, salves, repair; hides, herbs, salvage.
  - **Commission gear** from the smiths. This uses the real recipes and consumes market stock.
- **Progression:** practice-based skill gain at plausible rates (QUESTS:65-82) and the same level cap.
- **Failure:**
  - Injury makes them rest at home.
  - Death comes from outcome tables, with the risk scaled by creature danger against the adventurer's level and gear.
  - On death, the body and gear become a lootable corpse only if the player is at tier A. Otherwise the gear is gone.
- **Retirement:** at a wealth or age threshold they become a resident in an open role slot: merchant helper, guard, trainer (NPC_SIMULATION:149-159).
- **Replacement:** arrivals keep the population at the target (DN5).
- **Player interaction:**
  - Talk through templated structured barks and lines. There is no runtime LLM (QUESTS:65-82; NPC_SIMULATION:176-180).
  - Trade a few items.
  - **Hire them** (§2.15).
  - A party invite comes in a later bar.
- **Names:** from the canon per-people naming used by the creator's Suggest list (WP2). None are invented.

**Effort:**
- WP12, NPC life:
  - Port nav and the mover: 3.5
  - Tier B, C and D simulations: 3
  - Needs, routines and jobs: 3
  - Promotion and demotion: 1.5
  - Death, arrivals and relationships: 2
  - Offline `IAbstractAdvance`: 1
  - Total DS 14.
  - LC 6: role-NPC dressing 2.5, walking and activity presentation on the AnimationTree 3.5.
- WP13, adventurers: CC 10, LC 2 (bodies, presentation).
- **Depends on:**
  - WP12: WP11 and WP0 (Social.cs split); anim Phase 3 for presentation.
  - WP13: WP12, WP14 and WP15a (the ally-combat helper and hire hook).

### 2.14 Living economy

**Old material (§4.1):**
- ECONOMY.md ("Strong working direction"):
  - The actors include adventurers (:12-27).
  - Production chains (:29-39).
  - Adventurers as consumers and producers (:41-64).
  - Contracts exist "because an actor needs something" (:66-80).
  - Regional pricing with "stability bounds" (:96-108).
  - **Stabilizers:** baseline production, essential reserves, capped elasticity, imports, consumption limits, restocking floors, substitution (:110-123).
  - Sources and sinks (:125-155).
  - No global auction house (:170-181).
  - "Simulate enough economy to generate consequences and opportunities; stabilize enough economy to remain a game" (:195-197).
- **S-36** (`canon/SYSTEMS.md:393-399`): stock, restock deadlines, gold reserve, a supply and demand modifier, price-modifier flags, buy above sell, decay of the sell price against purchase history.
- **MerchantProfile** (`canon/DATA_MODEL.md:603-631`): `restock_min`, `gold_reserve [min,max]`, `price_bias`, `buys_tags`, `buys_rarity_max`. The rural example makes metal dear "so travel has economic value".
- **Exploit guards:** E-1 (arbitrage limited by carry, travel and liquidity; "the second run is worse than the first"; a 100-run harness) and E-2 (limited buys per restock) (`canon/GAMEPLAY_LOOPS.md:232-233`).
- **Tier rules:** "settlement economy totals, prices, stock counts (bounded)" may advance abstractly, and "per-item merchant stock is reified on interaction" (`WORLD_ARCHITECTURE.md:283,389`).
- **M10 exit criteria:** `canon/ROADMAP.md:308-315`.
- No code exists on any ref.

**Design:**
- **Settlement market per town** (Ashen Hollow, Cairnwold):
  - An abstract stock count per good, with a target level and an authored `[min, max]`.
  - Content: `content/markets/*.yaml`.
- **Flows, stepped daily:**
  - **Production:** resident jobs (§2.13), plus a baseline floor.
  - **Consumption:** residents eat; adventurers buy arrows, salves and repair.
  - **Player trade:** the player's buys and sells.
  - **Road trade:** one abstract trade link on the Old East Road. A daily carrier moves surplus to deficit with a travel delay.
  - **Imports:** a stabilizer for essentials.
- **Prices:**
  - `base × bias × clamp(1 + k·(target − stock)/target, 0.6, 1.6)`, with the elasticity cap per good.
  - Buy price always stays above sell price (S-36).
  - The sell price decays against the player's recent sells of that good in that town (E-1, E-2).
- **Merchants:**
  - Each merchant draws its shelf from the town market at each restock (`restock_min`, default 1 game day) and keeps a coin purse with `gold_reserve [min,max]`.
  - The purse is refilled from NPC sales and drained by buying from the player.
  - Liquidity is real: when the purse is empty, the merchant stops buying.
  - Shelf stock is reified when the player opens trade (canon).
  - Kera and Durn keep distinct buy tags (CLOUD_TASKS task 4).
- **Money:**
  - Sources: bounty rewards and loot sales, for the player and adventurers alike.
  - Sinks: repair, wages, training, lodging, and consumption.
  - A bounded money supply per town keeps it stable, and a test pins it.
- **Offline:** a daily step, clamped (RK-12). The player sees price arrows and news in the away sheet.
- **UI (LC, small):** a price trend arrow and a "the smith is short of iron" style line in the trade column.
- **Effort:** CC 8 (market model 3, prices, stabilizers and exploit harness 2.5, merchants and purses 1, road trade 1, offline 0.5). LC 0.5.
- **Depends on:** WP11. WP12's production hooks arrive later, and the baseline floors work without them.
- **Decision:** DN6 (how far prices may move).

### 2.15 Companions and hirelings ("Baldur's Gate and/or New World style")

**Canon** (EXT/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md, "Strong working design"):
- **Roles:**
  - A companion chooses to travel with you, a hireling works under contract, and a temporary ally shares a goal. People move between these states (:6-10).
- **Party:**
  - **Player + up to 3** (:12-18).
  - Combat animals and summons take a slot (OR:315-320).
- **Companions:**
  - Identity: goals, beliefs, money, skills, equipment, home, fears, personal quests, moral limits. They do not cease existing when dismissed (:20-36).
  - Avoid a single approval score; use several dimensions (:38-52).
  - They react by values and can comply, hesitate, refuse, argue or leave (:54-58).
- **Hirelings:**
  - What they care about: wages, duration, risk, supplies, treatment, loot.
  - Nonpayment ends the contract (:60-72).
- **Equipment:**
  - Personal versus loaned (:74-82).
  - Autonomy setting (:84-92).
  - Loot rules (:94-104).
- **Commands:**
  - Immediate commands and standing rules (:106-128).
  - Formations (:130-142).
- **Downed, dying, dead:**
  - Downed and dying states.
  - Guardrails against losing a beloved companion to technical jank (:167-179).
- **Life beyond the party:**
  - Off-screen autonomy (:181-183).
  - Retirement (:185-187).
  - Personal problems exist independently of approval; trust decides confiding (:189-191).
  - Romance is relationship-driven and never required (:193-199).

**Built:** `World/Runtime/Companions.cs`:
- recruit (`:160`), follow/wait, fight radius and leash, downed with a 60 s revive window, then a fallback to the Ashen Waystone (`companion.yaml`)
- trail-following catch-up
- travel with the party (`:214`)
- `CompanionRecord(NpcId, Order, Condition, pose, Health)` (`World/PlayerState.cs:59`)
- Tavar only

**Design:**
- **Party core (WP15a):**
  - A party of up to 3 slots; companions and hirelings share them.
  - The roster has three states: `companion`, `hireling` and `temporary_ally`, plus `dismissed` (living their own life).
  - The record grows to: state, contract, progression, equipment (loaned and personal), stance, relationship snapshot.
  - Fix the freeze after a jump of more than 150 m (NA:275).
- **Commands and tactics (WP15a):**
  - Stances: aggressive, defensive, passive.
  - Immediate commands on a radial menu or key chords (G opens it, with mouse targeting): attack my target, hold here, follow, move to, regroup, retreat, don't attack, help the downed.
  - Three standing rules for this bar: don't chase past the leash, protect injured allies, hold healing items for allies below 30%.
  - No formations beyond follow spacing; those come later.
  - Extract the ally combat behaviour into a shared helper, which adventurers reuse (§2.13).
- **Hirelings (WP15a):**
  - Hired through dialogue from a pool of role residents, idle adventurers and Cairnwold hands.
  - **The contract:** role, duration in game days, daily wage, loot rule (wage-only by default).
  - **Wages:**
    - Paid daily from the player's coin into the hireling's purse, which they then spend in the economy (§2.14).
    - When the player can't pay, there is one day's grace; then the contract lapses and they leave. The away sheet reports it.
  - **Roles:**
    - **Guard:** fights; today's companion behaviour.
    - **Porter:** adds carry capacity, which ties in with the laden band (D4).
    - **Gatherer:** assigned to a node area. Works daily, including offline, and delivers to a player storage chest (WP8) or the town stash.
    - **Crafter:** works a station from supplied materials, using the ported errand `BeginWork` (WP12).
  - **Dismissal:** any time; the remaining wage is not refunded. A dismissed hireling returns to their life.
- **Approval and relationship (WP15b):**
  - Keep the 5 built dimensions (affection, fear, grudge, respect, trust) and add **loyalty**.
  - Attraction and ideology come only with romance (DN10).
  - **Reactions:**
    - Each companion's content lists value tags; the player's deeds (WP5's `deed` condition) carry tags.
    - A tagged act fires a reaction bark plus a relationship change. Thresholds give comply, hesitate, refuse or leave.
    - No single approval bar shows in the UI. Instead the panel gives a short "how Tavar regards you" sentence from the dimensions.
- **Banter (WP15b):**
  - Party banter lines as barks with party-composition, location and recent-deed conditions.
  - Rate-limited, and subtitled (WP1 and WP5).
- **Progression and equipment (WP15b):**
  - Companions and hirelings get `CharacterProgression` and gain XP and skills from shared combat and from their role work, on the player's rules.
  - Equipment uses the same 10 slots.
  - The player can **give** (it becomes theirs) or **loan** (it comes back when they leave).
  - Equipment autonomy for this bar is player-assigned only; autonomous buying through the economy comes later (canon :84-92).
- **Falling (DN9):**
  - Downed with a 60 s revive window, as built.
  - When the window expires:
    - A **named companion** is "gravely wounded": they leave the party and recover at home for 2-4 game days, with no permadeath. This is the canon guardrail (:179).
    - **Hirelings and hired adventurers die.** Their personal gear drops and loaned gear comes back to the player. The risk is stated when hiring.
- **Personal quests (WP15b):**
  - A quest field `companion_ref`, a `trust ≥ N` "confides" gate on the quest's start dialogue, and an `approval_change` reward.
  - Framework plus **Tavar's hook only**: his "hear his part of the story first" thread (DCP:431).
  - The Wrong-Side Spear at trust ≥ 50 (DCP:655) is wired as that hook's reward.
- **Persistent world:**
  - Dismissed companions and hirelings become residents or adventurers in the life simulation (§2.13). They keep their level, gear and relationship and go on living, including offline (canon :181-183).
  - Hirelings under contract keep working and drawing wages while the player is offline.
  - **Adventurer NPCs can be hired** and keep their state.
  - Dismissed companions can be re-recruited where they now live.
- **Party UI (LC):**
  - The CompanionsPanel becomes a Party panel: cards with role or contract, wage, days left, health, stance, regard sentence and gear; commands; dismiss with a two-step confirm.
  - A hiring dialogue sheet.
  - Party member gear opens in the inventory panel as a second figure column.
- **Effort:**
  - WP15a (party core, commands and tactics, hirelings and roles, wages): CC 8.
  - WP15b (approval, reactions, banter, progression, equipment, falling, personal-quest hooks, offline integration): CC 7.
  - Party UI and hiring sheet: LC 3.
  - Total 18.
- **Depends on:**
  - WP15a: WP11 for offline wages; WP14 for wage flows (a stub sink is acceptable until then); WP12 for the crafter's errand mover. The other roles don't need WP12.
  - WP15b: WP5 for deeds and bark conditions; WP15a.
- **Decisions:** DN9-DN11.

### 2.16 Effort summary

| WP | Name | CC | LC | DS | Total |
|---|---|---|---|---|---|
| WP0 | Integration prep (adds the Social.cs split) | — | 2.5 | — | 2.5 |
| WP1 | Front end, settings, controls, performance, no-pause guard (crash hooks moved to WP10) | — | 9 | — | 9 |
| WP2 | Character creation, Calls, opening (no World Rules) | 2.75 | 7 | — | 9.75 |
| WP3 | Itemization (classes, quality, durability, encumbrance) | 8.5 | 0.5 | — | 9 |
| WP4 | Quests and story framework | 5 | 0.5 | — | 5.5 |
| WP5 | Dialogue engine and VO hooks | 3.5 | — | — | 3.5 |
| WP6 | Factions and reputation | 3.5 | 0.5 | — | 4 |
| WP7 | Calendar and weather | 2 | 1.5 | — | 3.5 |
| WP8 | Building and housing | — | 1 | 6 | 7 |
| WP9 | Progression UI, techniques, **trainers** | 4.25 | 4.5 | — | 8.75 |
| WP10 | Guidance, Downed, audio hooks, crash hooks | 4.5 | 1 | — | 5.5 |
| **WP11** | **Persistent clock and offline catch-up** | 5.25 | 0.5 | — | 5.75 |
| **WP12** | **NPC life simulation (tiers A-D, nav port)** | — | 6 | 14 | 20 |
| **WP13** | **NPC adventurers** | 10 | 2 | — | 12 |
| **WP14** | **Living economy** | 8 | 0.5 | — | 8.5 |
| **WP15** | **Companions and hirelings (15a and 15b)** | 15 | 3 | — | 18 |
| **Total** | | **72.25** | **40** | **20** | **≈132 agent-days** |

This is up from about 75 in v1.
- Pause, difficulty, rest and schedules came out (about −6).
- The persistent world, NPC lives, adventurers, the economy and the party added about 61.
- Trainers added 2.5.
- The no-cap structural test is within WP12's estimate.
- The city-scale work in §3.3 is **not** counted.

Calendar time:
- The local-Claude lane is now the critical path at about 40 days. It drops to about 20 with two local sessions; WP12's presentation and WP15's UI can run apart from WP1 and WP2.
- With 3-4 cloud lanes and DeepSeek on WP12 then WP8, the estimate is about **8 weeks with one local lane, or about 6 weeks with two**.
- Barrowworks' remaining ~3 DeepSeek days come on top.

---

## 3. Owner decisions

### 3.1 Settled (2026-10-17)

| v1 # | Ruling |
|---|---|
| D1 | **No pause anywhere.** OD-12 is superseded. |
| D2 | Gamepad is out of this bar. |
| D3 | Creator scope as recommended. |
| D4 | Laden band. |
| D5 | Durability as recommended. |
| D6 | Weather as recommended. |
| D7 | **Superseded by the full NPC life simulation** (§2.13). |
| D8 | Building is in scope; B1-B4 defaults are proposed in §2.9 for confirmation. |
| D9 | Mounts are out. |
| D10 | **No difficulty levels or presets.** OD-11 is closed as "standard rules only". |
| D11 | One technique per weapon family. |
| D13 | Numbered calendar. |
| D15 | Local-only crash log. |
| — | **OD-18 is closed:** the world continues while the player is offline, with a summary. |

Still open from v1, with recommendations unchanged:
- D12: paper doll. Keep the grid for now.
- D14: the credits team line.
- D16: the final score route.

### 3.2 New decisions: all accepted as recommended (2026-10-17), **PROVISIONAL**

The owner accepted every DN recommendation below, including DN7 (no quickload, one live world) and DN8 (no rest-until; Waystone travel advances no time). They are rules "for now": **each is provisional and may be revisited**. Code should keep them as content or config values where practical (caps, bands, wages, ranges) rather than hard-coding them. DN3 was amended: there is no design cap on population (§2.13).

| # | Decision | Context | Accepted rule (provisional) |
|---|---|---|---|
| DN1 | **Offline time ratio and catch-up cap** | Canon's example cap is 30 game days (`WORLD_ARCHITECTURE.md:248`). The clock is already real-time scaled: 1 game day is 48 real minutes, and 1 Reckoning is about 6.1 real days. | 1:1 real time. Honour up to **one Reckoning (183 game days, about 6 real days)** of absence, and treat anything longer as exactly one Reckoning, with the away sheet saying "a long time has passed". The abstract step keeps this within the 2 s load budget. |
| DN2 | **Amend canon D-06: abstract tiers may resolve outcomes** (death, injury, coin, gear tier, skill gain) by seeded rule tables, never by simulated combat | Canon forbids abstract combat, death, inventory and skill gain (`WORLD_ARCHITECTURE.md:275-288,385`; `DECISIONS.md:154`). NPC adventurers and the owner's new direction require them. | Approve the amendment as written in §2.13. Without it, adventurers cannot level, die or retire off-screen. |
| DN3 | **Resident population for the two towns** | Canon slice: 8 named plus about 14 Tier-B role NPCs (`VERTICAL_SLICE.md:46`). Each needs a body. The owner: "there shouldn't be a cap just yet", but the population doesn't need to grow now. | 8 named plus **6 role residents per town (12)**, dressed from existing variants; the current casts are not enlarged. **No population cap anywhere in data, schema or code.** Tier figures are tunable performance budgets, not limits. Scale work waits for the cities (§3.3). |
| DN4 | **NPC death and replacement** | Canon: role replacement; unique NPCs stay dead; no births design. | The 8 named story NPCs are essential and never die. Role residents and adventurers can die by rule. Vacancies fill with new arrivals after 3-7 game days, keeping every role slot filled. No births or ageing in this bar. |
| DN5 | **Adventurer population** | No canon number. | A **target of 8 in region 1** (4 based in each town), kept at target by arrivals. The target is a tunable content value with **no hard cap** (owner, DN3 amendment). Starting levels spread from 1 to 5 under the level cap. |
| DN6 | **Price movement bounds** | ECONOMY.md asks for capped elasticity and stability bounds. | Prices move within **0.6-1.6× base**, relaxing toward base by 10% a day. Merchants buy only while their purse holds coin. |
| DN7 | **Saves under MMO rules** | Built: manual saves, quicksave/quickload (F5/F9) and rolling autosaves. Under MMO rules there is one live state per character. | **One live state per character**, saved on events, every 5 minutes and on quit. F5 stays as "save now"; **remove quickload (F9) and manual load points**. Rolling backups remain only for an explicit "restore", which applies no catch-up. |
| DN8 | **Time-skips** | Rest-until was a v1 proposal (now removed). Built Waystone travel advances world time by ¼ of the road walk. | No "rest until". **Travel stops advancing world time** and costs only what it does now otherwise. A world-time cost is not MMO-normal, and with a living world it would let the player fast-forward everyone else. |
| DN9 | **Companion death** | The canon guardrail warns against jank deaths (EXT/COMPANIONS:179). The owner says they "can fall". | Named companions get **gravely wounded, not dead**: they recover at home for 2-4 days. **Hirelings and hired adventurers can die permanently**, and this is stated at hire. |
| DN10 | **Romance in this bar?** | Canon: relationship-driven and never required (EXT/COMPANIONS:193-199). It needs attraction dimensions and authored content. | **Out for now.** Build no romance plumbing; add it with the first romanceable character's content. |
| DN11 | **Party size and hireling wages** | Canon: player + 3. | **Player + 3**, companions and hirelings sharing the slots. Wages are content numbers per role. A first proposal is 4-8 coin a day, against a bounty reward of 15-40 coin; the owner tunes it. |

### 3.3 Later items, for when cities are built (not in this bar, not counted)

The owner wants big central cities later: "real cities with many people", with trainers for every skill family. "We will clearly have to optimize for larger towns and cities." These are recorded here so that the current design does not block them:
- **City-scale NPC engineering** (WP12 follow-on):
  - Tier A/B/C/D budgets tuned for hundreds to thousands of NPCs per city.
  - Aggregate settlement-level simulation off-screen.
  - **Streaming of NPC state by cell or area**, alongside the cell streaming S-21 that the region expansion needs (NA:615, 846).
  - Crowd rendering:
    - MultiMesh instancing for distant crowds, and impostors at the outer ring (`canon/WORLD_ARCHITECTURE.md:420-436`).
    - Animation LOD: 30 Hz at 15-40 m, 15 Hz beyond 40 m, off when off-screen (`CHARACTER_ANIMATION_STRATEGY.md:214-254,517-522`).
    - Pooled presentation nodes.
  - Performance budgets measured on BEAST's RTX 3090:
    - 60 fps at 1440p High;
    - world systems ≤4 ms on the main thread;
    - tier C ≤2 ms per tick;
    - characters ≤2 ms GPU and ≤1.5 ms CPU in the busiest view.
- **Scale test:**
  - A synthetic city of 500-1000 NPCs.
  - Offline catch-up at the cap in ≤2 s.
  - Holding 60 fps at 1440p High on the 3090 while walking through it.
- **Character variety for crowds:**
  - CAS Phase 7 generic villagers (8 base heads, 3 body types per sex, garment permutations; about 4 agent-days).
  - The MetaHuman/MPFB body archive and the garment library.
  - Race bodies for the eight peoples of Otherhome (NA:823, 878).
- **Full trainer coverage:**
  - Teachers for every canon skill and technique family: combat (13 at 1.0), magic (8 domains at 1.0), crafting (7 clusters), building (carpentry, masonry), world, social, and the Other-related methods.
  - Sources: `C03:455-530`; `C05:61-83`; `C06:235-252`; `C12:216-249`; `C20:105-123`; `C12:255-257`.
  - The trainer system in C10 already supports this, so it is content work.
- **Rough size when started:**
  - About 10-15 agent-days of engineering (LC and DS).
  - CAS Phase 7 at about 4 days.
  - The trainer content scales with the city casts.

---

## 4. Old material and unmerged code

### 4.1 What the earlier chats and repos already hold for the new systems

**Owner statements found:**
- 2026-10-02, owner brief (UI/UX transcript, event 0): "IMPORTANT — NO TRUE PAUSE… Opening a menu suspends player control only. The world continues: simulation, combat, effects, world time, quest timers, NPC/world state." The owner approved the matching design ("THE WORLD GOES ON", event 222).
- 2026-10-06 (canon session, event 609): "a 150+ hour single player game built like an MMO".
- Charter: "It should feel like an MMORPG world that happens to be populated by intelligent NPCs" (`canon/PROJECT_CHARTER.md:1,17`). Design principle 2: "The world continues when the player is absent" (`direction/extensions/00_BATCH_2_INDEX.md:20`).
- **None of the five transcripts holds an owner statement about offline simulation, NPC adventurers, NPC lives or the economy.** Those exist only as canon design text (below), so the owner's recollection matches the canon docs rather than lost chat rulings.

**Canon design reused here** (cited in §§2.12-2.15):
- **Offline time:**
  - D-06 tiers.
  - WORLD_ARCHITECTURE §§5-9: clock, caps, tier rules, what continues.
  - PERSISTENCE: pure-function catch-up, `world_time_advanced_ticks`, T-22.
  - OD-18 options A-C.
  - RK-12 bounds.
- **NPCs, adventurers, economy, party:**
  - NPC_SIMULATION.md: classes, adventurer state, goals, failure, retirement.
  - QUESTS_DUNGEONS…: competitive opportunities, repopulation, replacement.
  - ECONOMY.md: actors, chains, stabilizers, sinks.
  - S-36 and MerchantProfile.
  - E-1 and E-2 exploit guards.
  - COMPANIONS_HIRELINGS…
- **Difficulty:** OD-11 was **OPEN**, not "ruled A" as DCP:912 says. DCP was wrong, and the owner has now closed it as no presets.

**Code to reuse:**

| Piece | Where | Reuse |
|---|---|---|
| Tier classifier, config, `CellTierChanged`, `ITierSimulation` stubs | HEAD: `Domain/Spatial/Tiers.cs:7-52`; `World/Runtime/Systems.cs:~498-565`; `Simulation.cs:144`; `content/config/simulation_tiers.yaml` | As is. WP12 fills in the B and C stubs and adds D. |
| Tick-based respawn and node refill | HEAD: `Creatures.cs:~752`; `Domain/Crafting/Crafting.cs:92-97` | Already catch-up-safe. |
| Repeatable bounties with world-tick cooldowns | HEAD: 54f3991; `Quests.cs:27,338` | The adventurers' demand source. |
| Errand mover and navigation (deterministic, saved, never teleports, opens doors, repairs at load) | `origin/claude/m7-factions-building`: `World/Runtime/Errands.cs`, `Navigation.cs`, `Domain/Spatial/Nav*`, `content/config/navigation.yaml` | The best base for NPC movement. Ported in WP12 rather than WP8. Its schema numbers (15-17) must be renumbered. |
| Companion follow, downed and travel | HEAD: `World/Runtime/Companions.cs` | Extended by WP15. Its combat helper is shared with WP13. |
| `TickScheduler.RunTicks` | HEAD: `Application/TickScheduler.cs:40-67` | Dead placeholder; **delete it** in WP11. |

**Not found anywhere:** an offline or "while you were away" implementation, NPC needs or routine code, adventurer code, economy simulation code, or a births and demographics design.

### 4.2 Salvage verdicts for the unmerged branches (unchanged from v1)

| Branch | Verdict |
|---|---|
| `feature/barrowworks` | In flight (DeepSeek). Merge first (WP0) once v16 persistence and the licensing defect are closed. It conflicts in 8 files. |
| `origin/claude/m7-factions-building` | **Re-port, never merge.** It uses schema 15-17 for different contents than HEAD's 15, and 66 files overlap. Factions go to WP6, navigation and errands to WP12, building to WP8. |
| `origin/claude/sleepy-hawking-8kiehn`, `origin/review/m7-final-cloud-audit-2026-09-26` | Discard (keep the audit report if wanted). |
| `origin/character-creator-godot4` | Ideas only: an unrelated third-party GDScript repo. |
| Everything else | Merged into HEAD (aa76445). |

The transcript UI code (SkillsPanel, SettingsPanel, ReputationPanel, BuildPanel and their Models; UIG §4.2) lives only in a volatile scratchpad. WP0 copies it somewhere permanent and to Drive.

---

## 5. Order of work and conflict control

### 5.1 Hotspot files and owners

| File | Owner | Others (allowed touch) |
|---|---|---|
| `src/Presentation/Main.cs` (2264 lines) | WP0 splits it into `Main.Input.cs`, `Main.Ui.cs`, `Main.Events.cs`, `Main.Saves.cs`, `Main.Hud.cs` | Each WP adds lines only in the partial it names. |
| `src/World/Runtime/Social.cs` | WP0 splits it into `Dialogue.cs`, `Npcs.cs`, `Trade.cs` with no behaviour change | `Dialogue.cs`: WP5 (then WP6 for `standing`, WP15b for reaction conditions). `Npcs.cs`: WP12. `Trade.cs`: WP14 (WP3b only for the repair service). |
| `src/World/Runtime/Companions.cs` | WP15 | WP13 only calls the ally-combat helper that WP15a extracts. |
| `src/World/Runtime/Combat.cs` | WP10 (Downed) | WP15a only where downed and fallen states meet companions; it lands after WP10. |
| `src/World/Runtime/Creatures.cs` | WP13 (targeting adventurers; abstract kills reduce populations) | — |
| `src/World/Runtime/Systems.cs`, tier region (`TierSystem`, `ITierSimulation`) | WP12 | — |
| `src/World/Runtime/Simulation.cs` | Append-only: one registration line per new system | All |
| `src/Application/GameSession.cs` | WP11 (load and save paths, `saved_utc`, catch-up call) | WP4 (autosave hooks), WP2 (`NewGame(CharacterSpec)`) |
| `src/Persistence/*` and `tests/Persistence.Tests/Fixtures` | The schema train (§5.2) | — |
| `src/Content/SchemaResolution.cs`, `SchemaTypeMapper.cs`, `ContentLoader.cs` | Append-only, one block per WP | All |
| `src/Domain/Quests/Quests.cs` | WP4 | WP6 (faction objectives), WP15b (`companion_ref`), WP13 (competitive bounty claim), each landing after WP4 |
| `Ui/HudView.cs`, `Hud.cs` | WP9 (quick bar) | WP4 (marker), WP10 (hint line through the new `Ui/HintLine.cs`), WP11 (away sheet through the new `Ui/AwaySheet.cs`) |
| `Ui/SystemMenuPanel.cs` | WP0 turns its rows into a data list | One row each for WP1, WP6, WP8, WP9, WP15 |
| `content/npcs` | WP12 (anchors, routines) | WP6 (faction membership, keep-both), WP15 (companion values and roles) |
| `content/quests/bounty` | WP4 | WP13 adds `competitive: true` |

### 5.2 Rules

1. **WP0 first (LC, 2.5 days):**
   - Merge Barrowworks (v16).
   - Split `Main.cs` and `Social.cs`.
   - Turn the System Menu rows into a data list.
   - Push `cloud/base-20261017`. `cloud/base-20261016` is stale.
   - Copy the transcript UI sources to a permanent folder and to Drive.
2. **Schema train.** Every WP puts its Persistence edits in **one final commit** (section class, migration, a fixture per `Fixtures/README.md`) and takes the **next free version when it merges**; numbers are never reused. Expected order:

   | Version | Content |
   |---|---|
   | v16 | Barrowworks spaces |
   | v17 | WP2 identity and Call (no World Rules) |
   | v18 | WP11 manifest: `saved_utc`, `world_time_advanced_ticks`, away-log tail; the live-state and backup split (DN7) |
   | v19 | WP3a notable instances |
   | v20 | WP6 faction ledger |
   | v21 | WP7 weather (a seed cursor only, because weather is derived) |
   | v22 | WP14 town markets, merchant purses, sell history |
   | v23 | WP12 NPC life: anchors, needs, routine cursors, nav routes, errands, NPC-NPC pairs, life states, arrivals |
   | v24 | WP15a party roster, contracts, stances |
   | v25 | WP3b durability, plus WP9's quick-bar slots |
   | v26 | WP15b companion progression, equipment, loyalty |
   | v27 | WP13 adventurer agents |
   | v28 | WP8 building pieces |

   WP4 tries to need no bump.
3. **Every new world system implements `IAbstractAdvance`** (WP11) and ships its own chunk-invariance test (T-22). This is a merge gate for WP7, WP12, WP13, WP14 and WP15.
4. **Events, not edits.** WP3's wear, WP14's flows and WP13's market activity subscribe to events or go through commands. They never reach into another system's state (ARCHITECTURE rule).
5. **Cloud conventions:**
   - The standard exclusions apply (CLOUD_TASKS:3-6), except where a brief names a file.
   - Build, all suites, lint and xref must pass.
   - **No network requests.** The wall clock is read only in `GameSession`.
   - Commit by pathspec only.

### 5.3 Waves

| Wave | Cloud Claude (parallel) | Local Claude | DeepSeek |
|---|---|---|---|
| 0 | — | WP0 | Finish Barrowworks (v16) |
| 1 | WP11 clock and offline · WP4 quests and story · WP5 dialogue · WP2-domain · WP3a items | WP1 front end and settings | WP12 begins: nav and mover port, tier B/C/D framework (after WP0) |
| 2 | WP14 economy (after WP11) · WP6 factions (after WP4 and WP5) · WP7 calendar and weather · WP15a party core and hirelings · WP10-sim | WP2-UI creator and opening · WP9 skills, spellbook, quick bar | WP12 needs, routines, jobs, arrivals |
| 3 | WP13 adventurers (after WP12, WP14 and WP15a) · WP15b approval, banter, progression · WP3b durability and encumbrance · WP9 techniques | WP12 presentation (after anim Phase 3) · WP15 party UI · WP7 visuals · WP10-UI · WP11 away sheet · WP1 performance | WP8 building (after WP12) |
| 4 | Soak gates: a 183-day offline catch-up determinism run (one step against daily steps), the 100-run economy exploit harness, a 30-minute "no visible teleport" observation (`VERTICAL_SLICE.md:373`) | Integration playthrough of every system in both areas, including a log-off/log-on cycle | — |

---

## 6. Work-package briefs (ready to hand off)

Every cloud brief assumes the following:
- Repo `jlucasmcrell/otherreach-RESTORE-20261014`, base `cloud/base-20261017` (from WP0). Read `AGENTS.md` first.
- One new branch, a PR into the base, a short description.
- Build, all test suites, content lint and xref pass.
- **No network requests.** Never read the wall clock outside `Application/GameSession.cs`.
- No new lore; gaps become questions in the PR.
- Don't touch `src/Presentation/Audio` (unless named), travel/map/Waystone code (unless named), character/MetaHuman files or region terrain.
- Persistence changes go in **one final commit** with the next free schema version (§5.2).
- Every new world system implements `IAbstractAdvance` with a chunk-invariance test.

**WP0: integration prep (local Claude, 2.5 days)**
> Merge `feature/barrowworks` once its v16 space persistence and the ring-floor licensing defect are closed. With no behaviour change:
> - split `src/Presentation/Main.cs` into partials: Input, Ui, Events, Saves, Hud;
> - split `src/World/Runtime/Social.cs` into `Dialogue.cs`, `Npcs.cs` and `Trade.cs`;
> - make the `SystemMenuPanel` rows a data list.
>
> Run the full suite, the headless smoke and the boot check. Push `cloud/base-20261017`. Copy the transcript UI sources (UI_GAP §4.2) to `C:\Otherreach_Recovery\ASSET_REBUILD\ui_transcript_sources\` and to Drive (RESTORE).

**WP1: front end, settings, controls, performance (local Claude, 9 days). Edited: no pause.**
> **Title screen:** Continue, New Game, Load (only for backup restore, DN7), Settings, Credits, Quit, with wordmark, version and the StartCamera backdrop.
>
> **`SettingsPanel`**, persisted to `user://client_prefs.json` through a `SettingsStore` that absorbs `audio_volume.json` and `graphics_tier.txt`:
> - **Display:** window mode, resolution, render scale, vsync, frame cap, brightness, tier and per-feature dials.
> - **Audio:** master, music, effects, ambience, interface, voice.
> - **Controls:** remapping of every action in `Main.Input.cs`, with conflict check, reset, and hold/toggle.
> - **Camera:** sensitivity, invert Y and FOV feeding `CameraRig` (remove the hard-coded 0.0025 and 75), plus shake, bob and flash.
> - **Interface:** UI scale, text size, high contrast, colour-vision palette, notification duration, subtitles (read by WP5 and WP15), guidance level and hint reset (read by WP10).
>
> **Other front-end pieces:**
> - A first-launch preset screen.
> - A credits screen from `release/payload/THIRD_PARTY_NOTICES` plus an owner line.
> - Quit saves the live state with no prompt.
>
> **No pause:** nothing stops the world. Restore the `NoPauseDoctrineTests` architecture guard (it fails on `Paused`, `ProcessModeEnum.WhenPaused` or `Disabled`, or `Engine.TimeScale` in `src/Presentation` outside `VisualAudit`), and add a test that the tick advances with every panel open.
>
> **Performance:** dials, and distance-streamed model loading for the 142 s startup.
>
> Use the ASTRAL SettingsPanel and SettingsStore transcript code as the template. Check 1280x720, 1920x1080 and 2560x1440 with 100% keyboard reach.

**WP2: character creation, Calls, opening (cloud 2.75 days, then local 7 days). Edited: no World Rules.**
> **Cloud part:**
> - `CharacterSpec`: name 1-24, pronouns, `people`, `body_preset`, `call`.
> - `GameSession.NewGame(CharacterSpec)` replaces both `NewGame("Wanderer")` calls.
> - A `call` content kind with the 4 CC-0 Calls (C11:43-49, 109; OR:85-98).
> - `content/people/veth.yaml`.
> - A canon-sourced Suggest name list, exposed as a reusable name source for the role residents of WP12 and the adventurers of WP13.
> - Per-character save folders (OD-14).
> - **No World Rules or difficulty fields.**
> - Schema v17 as one final commit. Tests: a new game per Call; a round trip; a migration that gives existing saves "Wanderer" and no Call.
>
> **Local part:**
> - The creator UI: People (Veth) → Body (blend, 3 builds) → Identity → Call → Confirm, plus Quick start.
> - The "Leak" opening, with control within 10 s and the first journal entry through WP4's `on_new_game`.

**WP3: itemization (cloud: 3a ≈5.5 days, then 3b ≈3 days). Edited: restock moved to WP14.**
> **3a:**
> - `item_class`, with rarity derived from it and lint enforcing that.
> - The notable-instance record.
> - Armour quality ±1 per step.
> - Drop quality by creature tier.
> - Craft-only masterwork +2.
> - Recipe books and a seeded experiment set.
> - Content:
>   - gloves, trousers, boots, cloak;
>   - a forge-crafted off-hand torch;
>   - one shield;
>   - uses for `boneleaf`, `herb_ashbloom` and `timber`;
>   - `skill_ref` on `haldas_bow`;
>   - loot tables for husk, spider and hound.
> - Tooltip: class, provenance, pips.
>
> **3b:**
> - Durability (D5): wear from Hit and Gather **events**, worn out counts as crude, station repair (skill, capped quality) and a merchant repair service in `Trade.cs`.
> - The laden band (D4).
> - Each session takes the next free schema version.

**WP4: quests and story framework (cloud, 5 days)**
> As in v1, with additions:
> - The journal's **Recent** list also receives WP11's away-log lines.
> - Bounty quests gain `competitive: true` support. The first actor (player or adventurer) to complete one claims it, and the board shows the claimant. WP13 calls this.
>
> The v1 scope:
> - `track` and `act`, `on_new_game`, and `choice` effects.
> - The objectives `defeat_creature`/`defeat_boss` (a flag on the death event), `companion_present`, `dialogue_choice`, `travel_to`, `waystone_known`.
> - Event autosaves.
> - Journal Main, Recent and notes.
> - A compass marker for the tracked beat.

**WP5: dialogue engine and VO hooks (cloud, 3.5 days)**
> As in v1: E-6 variants, the E-7 `deed` condition (WP15b's companion reactions consume deeds), the `CellOf` region crash fix, derived line ids, an optional `vo/<line-id>.ogg` on the Voice bus with ducking, subtitles, bark fade. Files: `Domain/Social/Social.cs`, `World/Runtime/Dialogue.cs` (after WP0), `Content/SocialContent.cs`, `Ui/DialoguePanel.cs`, `Audio/VoicePlayer.cs` (new).

**WP6: factions and reputation (cloud, 4 days; after WP4 and WP5)**
> As in v1:
> - Re-port the m7 faction slice and rename the factions to the Wayhold and Long Survey chapters.
> - Report-only standing.
> - The reward, objectives and `standing` condition.
> - The ReputationPanel.
> - Faction state does not change while the player is away (no off-screen acts in this bar); its `IAbstractAdvance` is a no-op with a test.

**WP7: calendar and weather (cloud 2 days, then local 1.5 days). Edited: schedules and rest removed.**
> - `Calendar`: a pure function of the tick, numbered tallies.
> - `WeatherSystem`: weather is a pure function of (seed, region, game-hour index) through `RngChannel`, with fair, overcast, rain, fog, wind and a reserved fold state, and fronts of 4-12 game hours. It is saved as a seed cursor and raises `WeatherChanged`. Its `IAbstractAdvance` is closed-form.
> - **Local part:** `SkyView` and `WeatherStates` read the simulation (`--visual weather` stays as a dev override); a calendar line in the System Menu and on the map.
> - Removing the world-time advance from Waystone travel, if DN8 is accepted, is a 2-line change in `World/Runtime/Travel.cs` plus a test update. Do it here, by an exception named in this brief.

**WP8: building and housing (DeepSeek, 6 days; after WP12 and decision B1-B4)**
> Re-port the m7 building stack:
> - `Domain/Building`, `World/Runtime/Building.cs`, `BuildingRules.cs`
> - `content/pieces/*`, `content/config/building.yaml`
> - `Player/BuildMode.cs`, `Greybox/StructuresView.cs`
> - The BLD lints and tests
>
> **Navigation comes from WP12; don't port it again.** Then:
> - Protected zones and the budget per the B1-B4 defaults in §2.9.
> - A ported BuildPanel and palette in the Field Instrument kit.
> - Storage chests: hireling gatherers deliver into them (WP15a).
> - An `IAbstractAdvance` that persists only, with no damage.
> - Next free schema version.
> - Default-off behind `--visual building=1` until LC reviews it.
>
> Handoff in `C:\Otherreach_External_Assets\REPORT_FOR_CLAUDE_BUILDING.md`.

**WP9: progression UI and trainers (local 4.5 days, plus cloud 4.25 days)**
> As in v1:
> - The SkillsPanel port (key P).
> - A Known/spellbook view.
> - The assignable 8-slot quick bar.
> - One technique per weapon family, taught by NPCs.
> - Skill-up toast and sound.
>
> Party members' skills show on their own Party card (WP15), not here.
>
> **Trainers (cloud 2 days, local 0.5 day; §2.3 C10):**
> - A `teaches:` block in NPC data: kind (recipe, technique, spell, building piece), id, requirements (skill band, trust, prerequisite, deed or flag), and a coin fee as an economy sink.
> - One generic `teach` effect replaces `give_recipe`; migrate the existing dialogue with identical outcomes.
> - Teachers teach only at their work anchor in work hours, once WP12 lands.
> - A Learn column with requirement chips.
> - A lint that every recipe, technique, spell and building piece has a findable source (P-7).
> - Content: only the current teachers (Kera, Durn, Tavar, Sel, Tam), plus one building-piece teacher from the existing cast, chosen by the owner (Berrin suggested).
> - Canon: no fee or teacher ever raises a skill (`PROGRESSION.md:223`).

**WP10: guidance, Downed, audio hooks, crash hooks (cloud 4.5 days, then local 1 day). Edited: difficulty removed.**
> - The `hint` content kind, with about 20 hints including the away sheet, hiring, party commands and price changes.
> - Downed with a companion or hireling present (OD-13 = B), 30 s.
> - **No World Rules, presets or damage multipliers.**
> - Local-only crash hooks and the next-launch notice.
> - Missing sound hooks, including party and hireling events; the Ambience, Interface and Voice buses and `default_bus_layout.tres`.
> - **Local part:** the gatherable outline, a screenshot pass.

**WP11: persistent clock and offline catch-up (cloud 5.25 days, then local 0.5 day). New.**
> Implement §2.12:
> - `GameSession` writes `saved_utc` on every save. On loading a character's live state it computes `requested = max(0, now − saved_utc) × 20 ticks/s` and `honoured = min(requested, cap)`. The cap is one Reckoning, 183 game days, per DN1; put it in `content/config/time.yaml`.
> - Persist `world_time_advanced_ticks` (canon PERSISTENCE:164, 268-277).
> - Add `IAbstractAdvance` with an `AdvanceLog`. Run the advance behind the loading screen.
> - Integrate clock, calendar, respawns, nodes and cooldowns, which are already tick-based, plus player-effect expiry.
> - A 10 s login aggro grace.
> - Live state versus backups (DN7): remove quickload (F9) and manual load points; a backup restore applies no catch-up.
> - Delete `Application/TickScheduler.cs`, a dead placeholder.
> - Tests:
>   - T-22: one 183-day advance equals 183 one-day advances.
>   - A clock moved back gives 0.
>   - The cap applies.
>   - A benchmark proving 2 s or less at the cap with the WP12-sized population stubbed.
> - Schema: next free version.
>
> **Local part:** the "While you were away" sheet, `Ui/AwaySheet.cs`. It is dismissable, the world runs underneath it, and its lines also go to the journal's Recent list.

**WP12: NPC life simulation (DeepSeek 14 days, then local 6 days). New. After WP0 and WP11.**
> **Uncapped by design:**
> - No population limit in data, schema or code.
> - Canon tier figures are tunable budgets in `simulation_tiers.yaml`, applied by admission control and spawn suppression, never by refusing NPCs.
> - Add the structural test from §2.13: 1,000 synthetic tier-D NPCs round-trip through save, the offline advance and tier transitions, with no count-related failure.
> - City-scale performance, crowd rendering and the 500-1000 NPC perf test are **later** (§3.3).
> - Don't enlarge the current casts.
>
> Implement §2.13 "residents and vendors":
> - **Port** the m7 errand mover and navigation: `World/Runtime/Errands.cs`, `Navigation.cs`, `Domain/Spatial/Nav*`, `content/config/navigation.yaml`, and their tests. Renumber their persistence into the train.
> - Replace `StubTierSimulation` with real B, C and D simulations, with canon radii, caps (spawn suppression, never silent despawn), hysteresis and legal promotion (WORLD_ARCHITECTURE:250-346).
> - Needs as 0-3 bands (hunger, rest, social, coin).
> - A utility routine chooser over E-1 activities in a `routine` content kind, replacing the `schedule` stub, with authored anchors (home, work, eat, social) for the 8 named NPCs and 12 role residents (DN3).
> - Job output hooks into WP14's markets (behind an interface, with a stub until WP14 lands).
> - NPC-NPC pair relationships driving visits and barks.
> - Essential flags for the 8 named NPCs; arrivals refill vacant role slots after 3-7 game days (DN4).
> - Tier A: talking pauses the routine, and merchants trade only while at work.
> - Weather sheltering through `WeatherChanged`.
> - `IAbstractAdvance` stepped per game hour at tier D, with a T-22 test.
> - A 30-minute "no visible teleport" acceptance run.
> - Follow DeepSeek's licence and no-new-lore rules; names come from WP2's name source.
>
> **Local part:**
> - Dress the role residents from existing variants.
> - Walking and activity idles on the AnimationTree, which needs anim Phase 3.

**WP13: NPC adventurers (cloud 10 days, then local 2 days). New. After WP12, WP14 and WP15a.**
> Implement §2.13 "adventurers":
> - An `Adventurer` agent: level and skills through `ProgressionEngine`, real-item gear, coin, home or camp, risk tolerance, goal.
> - A deterministic goal planner over the canon chain (NPC_SIMULATION:54).
> - Activities:
>   - competitive bounties through WP4;
>   - hunting: real fights at tier A through WP15a's ally-combat helper, outcome tables in the abstract, kills reducing spawn counts;
>   - travel on the Old East Road;
>   - buying and selling through WP14;
>   - commissioning gear from the smiths, consuming market stock.
> - Injury, death by outcome table (DN2 amendment), retirement into a role slot, and arrivals keeping the population at a tunable target of 8 per region, with no hard cap (DN5).
> - Templated barks and lines; hireable through WP15a.
> - A `Creatures.cs` targeting change so creatures engage adventurers at tier A.
> - Next free schema version.
> - **Local part:** bodies from existing variants, presentation.

**WP14: living economy (cloud 8 days, then local 0.5 day). New. After WP11.**
> Implement §2.14:
> - A town market per settlement (`content/markets/*.yaml`): per-good stock, target and `[min,max]`.
> - Daily flows: production (baseline floors plus WP12 job hooks), consumption, player trade, one Old East Road carrier link, essential imports.
> - The price formula with a 0.6-1.6× cap and a 10% daily relaxation (DN6), buy always above sell, and sell-price decay against recent player sells (E-1, E-2).
> - Merchants draw the shelf from the market every `restock_min`, hold a `gold_reserve` purse, and stop buying when empty. Shelves are reified on opening trade.
> - Wage and repair sinks feed purses.
> - `IAbstractAdvance` stepped daily, clamped (RK-12).
> - Tests:
>   - a 100-run arbitrage harness where the second run is worse than the first;
>   - a 183-day soak that stays inside `[min,max]`;
>   - a money-supply bound;
>   - T-22.
> - Owns `World/Runtime/Trade.cs` and the new `World/Runtime/Markets.cs`.
> - Next free schema version.
> - **Local part:** a trend arrow and a shortage line in the trade column.

**WP15: companions and hirelings (cloud 15 days in two sessions, then local 3 days). New.**
> **15a (8 days):**
> - A party of up to 3 slots.
> - Roster states: companion, hireling, temporary ally, dismissed.
> - Stances, the immediate-command radial, three standing rules.
> - Extract the ally-combat helper, which WP13 consumes.
> - Fix the freeze after a 150 m jump.
> - Hirelings:
>   - a contract: role, days, wage, loot rule;
>   - daily wages from the player's coin, one day's grace, then a lapse;
>   - roles: guard, porter (adds carry capacity), gatherer (node-area output to a chest or the town stash), crafter (station work through WP12's errand `BeginWork`);
>   - dismissal.
> - `IAbstractAdvance` for wages and role output offline.
> - Next free schema version.
>
> **15b (7 days; after WP5):**
> - Add loyalty to the relationship model.
> - Value-tag reactions to WP5 deeds, with comply, hesitate, refuse and leave thresholds.
> - Party banter barks.
> - `CharacterProgression` and the 10 equipment slots for party members, give versus loan, player-assigned gear only.
> - Falling per DN9: a named companion is gravely wounded and recovers at home; hirelings die.
> - A quest `companion_ref` with a trust "confides" gate, and Tavar's hook only.
> - Dismissed members enter WP12 and WP13's life simulation with their state.
> - Next free schema version.
>
> **Local part (3 days):**
> - The Party panel replacing CompanionsPanel: cards, regard sentence, gear, commands, two-step dismiss.
> - A hiring sheet.
> - Party gear in the inventory.

---

## 7. Summary

- **Inventory:** 103 systems. **40 COMPLETE, 36 PARTIAL, 27 MISSING.** Two of the MISSING are deferred by owner ruling (gamepad, mounts).
- **What the old material held:**
  - Canon already designs most of the new direction:
    - bounded offline catch-up as a pure function of ticks (PERSISTENCE, WORLD_ARCHITECTURE §§5-9, D-06, OD-18);
    - NPC classes and tiers A-D, adventurer goal chains, failure, retirement and repopulation (NPC_SIMULATION, QUESTS_DUNGEONS…);
    - a stabilized supply-and-demand economy (ECONOMY, S-36, MerchantProfile, E-1 and E-2);
    - parties of player + 3 with hirelings (COMPANIONS_HIRELINGS…).
  - The transcripts hold the owner's 2026-10-02 "NO TRUE PAUSE… the world continues" brief, but no owner statements on offline simulation, adventurers or the economy.
  - The code holds:
    - the tier classifier and empty B/C stubs (HEAD);
    - tick-based respawns (HEAD);
    - a dead `TickScheduler` (HEAD);
    - the m7 errand mover and navigation (unmerged; the best base for NPC movement).
  - One canon conflict needs a ruling: abstract tiers may not do death, inventory or skill gain (DN2).
- **New work packages:**
  - WP11: persistent clock and offline catch-up.
  - WP12: NPC life simulation.
  - WP13: NPC adventurers.
  - WP14: living economy.
  - WP15: companions and hirelings.
- **Edited work packages:**
  - WP1: no pause; restores the no-pause guard.
  - WP2: no World Rules.
  - WP3: restock moved out.
  - WP7: calendar and weather only.
  - WP8: navigation moved to WP12.
  - WP10: difficulty removed.
- **Owner decisions DN1-DN11 (all accepted 2026-10-17, provisional):**
  - DN1: offline cap of one Reckoning (about 6 real days) at 1:1.
  - DN2: amend D-06 so abstract tiers resolve outcomes by rule.
  - DN3: 12 role residents now, with no population cap in data, schema or code; city-scale work later (§3.3).
  - DN4: named NPCs essential; others replaced by arrivals.
  - DN5: a target of 8 adventurers, no hard cap.
  - DN6: prices 0.6-1.6×.
  - DN7: one live state per character, no quickload.
  - DN8: no time-skips, and travel stops advancing world time.
  - DN9: named companions are gravely wounded rather than killed; hirelings can die.
  - DN10: romance out for now.
  - DN11: player + 3; wages are content numbers.
  - Still open from v1: D12, D14, D16.
- **Trainers:** a teacher system (C10, in WP9) sized to the current areas, with a lint that every recipe, technique, spell and building piece has a findable source.
- **All DN rules are accepted but provisional ("for now").** City-scale engineering (crowds, NPC state streaming, a 500-1000 NPC scale test on the 3090) and full trainer coverage are recorded as later items (§3.3), not counted.
- **Total effort:** about **132 agent-days** (cloud ≈72, local ≈40, DeepSeek ≈20), up from about 75, plus about 3 for Barrowworks. That is about 8 weeks with one local lane, or about 6 with two.
