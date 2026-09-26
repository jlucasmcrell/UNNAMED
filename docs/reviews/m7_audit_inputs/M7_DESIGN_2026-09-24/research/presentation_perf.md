# M7 research notes - presentation, input, UI doctrine, runtime acceptance tooling, performance (key: `presentation_perf`)

Canonical source: snapshot of `origin/main` at `e10d2c4`. Every `path:line` citation is repo-relative at that commit unless marked otherwise.

Labels used throughout:
- **FACT** - read in the cited file.
- **INFERENCE** - my reading or recommendation, not stated in a source.
- **UNTRACKED** - from a doc that exists only in the `G:/UNNAMED/docs` working tree (untracked, authority unconfirmed).
- **HISTORY** - from `G:/UNNAMED_HISTORY` (reports and run outputs, not source).

---

## 0. Executive summary (the design-relevant conclusions)

1. **Input is defined in code, not in `project.godot`.** `project.godot` has no `[input]` section (`src/Presentation/project.godot:1-21`). Every action is created at runtime by `Main.DefineInput()` (`src/Presentation/Main.cs:1061-1104`) with `PhysicalKeycode` bindings, so bindings follow key position, not layout (`Main.cs:1068`). Remapping and gamepad are not implemented (`docs/M3_STATUS.md:86`; `docs/M6_STATUS.md:182`: "The controls cannot be rebound; F1 only reads them").
2. **Free keys for a building mode:** letters B, F, L, M, N, O, P, T, U, Y, Z; digit 0; F2, F6, F7, F8, F10, F11, F12; middle mouse; Enter, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Alt, CapsLock. The digits 1-3 and 7-9 are bound only as dialogue replies and do nothing outside a conversation, but the content bible reserves 1-3 and 7-8 for an unbuilt 8-slot hotbar. Collision risks: the **mouse wheel is camera zoom**, **LMB is attack**, **R is take-all** (acts only while the inventory panel is open), **Q is shoulder swap**, **Esc frees the mouse or leaves a dialogue**.
3. **UI doctrine.** No core action may require a radial menu, and the bible's formal rule explicitly names "building function" (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:645`). The primary interfaces are "direct keybinds; hotbars; contextual prompts; tabs; searchable/categorized lists; conventional menus" (`...CONTENT_BIBLE.md:647-654`). The building camera "may permit a somewhat farther camera ... Do not turn it into an unrestricted RTS camera ... Camera distance must remain bounded" (`docs/CAMERA_PERSPECTIVE_AND_PRESENTATION.md:266-272`).
4. **Presentation reads immutable views and submits commands through `GameSession`.** It reads `Simulation` properties and query methods each frame, subscribes to events, and submits with `GameSession.Submit(GameCommand)`. The public surface of `Simulation` is allow-listed by `tests/Architecture.Tests/ArchitectureTests.cs:112-133`. `Simulation.Aim` (M6) is the precedent for a read-only query that runs the authority's own function for a presentation aid (`src/World/Runtime/Simulation.cs:198-206`). An M7 placement preview ("ghost") would follow that pattern and must be added to the allow-list on purpose.
5. **Prediction.** The player's body is drawn with the simulation's own `Kinematics.Step` against `Layout.Space` plus `Simulation.DynamicBlockers` (`src/Presentation/Player/PlayerController.cs:127-128`). Creatures are interpolated between their last two ticks. NPCs are followed, not predicted. Any M7 structure that blocks movement must appear in the set `Kinematics.Step` reads. Otherwise prediction and authority disagree and the drawn body pops.
6. **Structures are drawn once at boot.** `HollowView.Build(RegionLayout)` creates a greybox `MeshInstance3D` for every `Blocker` (box or cylinder), each with a camera-only `StaticBody3D` on collision layer 1 and `CollisionMask = 0` (`src/Presentation/Greybox/HollowView.cs:36-54, 374-381`). There is no runtime add or remove path for structures. Doors and barriers change only through events (`SetDoor`, `SetFlags`). `ItemsView.Refresh` (free everything and rebuild on an event) is the only runtime rebuild pattern (`src/Presentation/Greybox/ItemsView.cs:58-80`). Roofs come from a hard-coded list of two building-ID prefixes (`HollowView.cs:203-217`), and a door's swing side is inferred from its wall group.
7. **Godot physics is used only for the camera.** The pieces are the `SpringArm3D` (`src/Presentation/Player/CameraRig.cs:31, 73-75`) and static camera colliders on terrain, structures, roofs, doors, the ravine and containers. Nothing collides with the body presentation-side: "Colliders exist only for the camera; the body collides in the domain" (`HollowView.cs:13`). **Godot navigation appears only in the isolated 2x2 km spike** (`src/Presentation/Spike/SpikeScene.cs:26, 77, 159-187, 204-213`). There it bakes a tiled navmesh purely to measure bake time and is never queried for a path. Nothing in the domain, World or Application uses Godot (enforced by `ArchitectureTests.cs:27`).
8. **Debug conventions.** F3 toggles a text overlay (FPS, tick, alpha, pending commands, body, cell, camera, every cell's tier, discoveries), discovery-radius rings and the aim line. F4 toggles the in-game quest debugger. F1 lists the controls from the input map and keeps a separate "DEVELOPER" section. All are plain `Label` or `PanelContainer` elements on a `CanvasLayer`.
9. **Runtime acceptance tooling** is a set of command-line modes on the one Godot project: `--smoke` (headless, exits 0 or 1), `--ui-shots <dir>`, `--playthrough <dir>` and `--playthrough-verify <dir>`, `--delta-shots <dir>`, `--perf`, `--spike`, and `--art-gallery <dir>`. Scripted runs drive `PlayerController` (the same commands the keys send), run one tick per frame from a fixed seed, and write transcripts (a markdown tick table), `commands.tsv` from `Simulation.CommandLog`, PNG screenshots and `StateDump` JSON. An M7 runtime proof would be one more mode, or more beats, in the same shape (section 9.8).
10. **Performance.** The target is a sustained 60 FPS at 1080p on RAZER's RTX 4070 Ti in a clean window (owner ruling 2026-09-23). It is **still unmeasured on RAZER** (`docs/M6_STATUS.md:221`: "Still open: the RAZER performance window"). The only numbers are ASTRAL trials (RTX 5090, not the gate): about 800-840 FPS average, 1% lows of about 200-220 FPS, GPU p99 about 0.21 ms, VRAM 181 MB. `FrameStats` records frame, process, render CPU and GPU times and memory, but **no draw calls, object counts, physics or simulation-tick timing**. The design budgets to build against are in `docs/WORLD_ARCHITECTURE.md:448-456`: main-thread world systems ≤ 4 ms, streaming ≤ 3 ms per frame, save ≤ 2 ms P99. One headless test enforces a simulation budget: `SixtyCreatures_TickWithinTheBudget` requires under 4 ms per tick (`tests/Application.Tests/CreatureTests.cs:526-541`).
11. **Greybox conventions for new pieces.** Pieces use primitive meshes (`BoxMesh`, `CylinderMesh`, `SphereMesh`, `PlaneMesh`) with flat `StandardMaterial3D` colours from the static `Palette` class (`src/Presentation/Greybox/Palette.cs:11-67`). Every solid carries a camera collider. The asset kit for buildings (`building_wall_timber`, `building_wall_stone`, `building_roof_panel`, `building_floor_planks`, `building_fence_panel`) exists but is **withheld** (`src/Presentation/Art/art_bindings.json:25-31`), so walls stay greybox. The asset standard for a building module is 5-25k triangles, LOD ×3 (100/40/15/5 %), with box-only collision (`docs/WAVE_0_MODULAR_ASSET_STANDARD.md:342, 368, 381`). The kit's nominal sizes are wall 3.0 m, floor 3.0 m, roof panel 3.0 m, post 2.6 m, beam 3.0 m, door frame 2.44 m, window frame 1.1 m and step 1.2 m (`docs/SCALE_AUDIT_REPORT.md:177-188`).

---

## 1. Presentation architecture - boot, frame loop, reads, commands

### 1.1 Boot and run modes (FACT)
- `Main : Node3D` is the only scene script (`src/Presentation/Main.tscn`: one `Node3D` with `res://Main.cs`). Main scene `res://Main.tscn`; features `4.7`, `C#`, `Forward Plus`; viewport 1920x1080 (`src/Presentation/project.godot:11-17`). Csproj `Godot.NET.Sdk/4.7.2`, net8.0, references only `Application` and `Domain` (`src/Presentation/UNNAMED.Presentation.csproj`).
- The run modes are documented in `Main.cs:25-28`: `--smoke`, `--perf [--perf-out dir] [--perf-seconds n]`, `--spike`, `--ui-shots dir`, `--playthrough dir`, `--playthrough-verify dir`, and `--asset-root dir`. `--delta-shots dir` (`Main.cs:204-209`) and `--art-gallery dir` (`Main.cs:79-84`) exist but are not listed in that summary.
- Arguments that take a value are listed by hand in `ParseArguments` (`Main.cs:1045-1047`). **A new mode that takes a value must be added to that list**, or it is parsed as a flag.
- Boot: `GameSession.Boot(new GameOptions(contentRoot, profile))`. A content error quits with code 2 and names every file (`Main.cs:92-101`). New game at once, `NewGame("Wanderer", seed)`, with seed `Playthrough.Seed` for `--playthrough` and `--delta-shots`, otherwise 0 (`Main.cs:103`). **There is no title, pause, settings or load menu**: the game starts a new character at boot, and loading is F9 quickload only (`Main.cs:485-488, 943-961`).
- Profiles: scripted and perf modes use a scratch profile `user://scratch/run-<pid>`. Playthrough uses `<dir>/profile`. Normal play uses `user://saves/default` (`Main.cs:86-91`).
- Scene construction order (`Main.cs:116-188`): HollowView (terrain and structures) → avatar (skinned if the asset library has one, else greybox `Avatar`) → CameraRig → Hud → ItemsView → CreaturesView → CraftingView → NpcsView → ProjectilesView → MagicEffects → SoundBank and SoundEvents → PlayerController → panels (Inventory, Dialogue, Journal, QuestDebug, Character, Help) → `Subscribe()` → `Resync()` → `DefineInput()`.

### 1.2 Frame loop (FACT, `Main.cs:226-315`)
Order in each `_Process(delta)`:
1. If a scripted or perf mode is active, it advances (`_perf.Update`, `_smoke.Update`, `_shots.Update`, `_delta.Update`, `_play.Update`). Otherwise `ReadInput()` runs, which submits commands (`Main.cs:231-300`).
2. `KeepContainerInReach()` closes any panel opened at a place once the body walks out of reach (`Main.cs:498-507`).
3. `_session.Frame(dt)`. Scripted modes pass `TickSeconds` so exactly one tick runs per frame. Normal play passes real `delta` (`Main.cs:305`).
4. An autosave toast if one happened (`Main.cs:306-307`). `Draw(alpha, delta)` (`Main.cs:308`). `_stats?.Record(delta)` (`Main.cs:309`). A perf screenshot if due (`Main.cs:310-314`).

`GameSession.Frame(realSeconds)` (`src/Application/GameSession.cs:177-201`):
- Clamps the elapsed time to `MaxFrameSeconds = 0.25` (`GameSession.cs:52`) and adds it to an accumulator.
- Calls `simulation.DrainCommands()`, then while the accumulator ≥ `TickSeconds`, calls `simulation.Step()` and drains again ("anything an event handler queued applies at the next boundary").
- Runs an autosave on the main thread when `AutosaveCadence.IsDue` (every 300 s of playtime; `src/Persistence/SaveStore.cs:39-46`).
- Returns `FrameResult(int TicksRun, double Alpha, string? AutosavedTo)` (`GameSession.cs:43`).

The tick is 20 Hz (50 ms; `docs/M3_STATUS.md:11`; the transcript header "50 ms ticks", `docs/acceptance/m6/transcript.md:3`).

**Input-to-event latency (FACT and INFERENCE).** `ReadInput` runs before `Frame` in the same `_Process`, and `Frame` drains commands first. A command therefore applies, and its events fire, in the frame the key was read (`docs/M3_STATUS.md:51`: "Commands apply at the next tick boundary in the same frame, so input reaches a domain event within one frame"). PROTOTYPE §6.3 asks for an *instrumented* latency test, "timestamp input, timestamp domain event. Budget ≤ 1 frame + 8 ms", run weekly (`docs/PROTOTYPE.md:253`). **No such instrumentation or test exists.** I searched `tests/` for latency, same-frame and within-one-frame checks and found none (INFERENCE from the negative search).

### 1.3 How presentation reads authoritative state (FACT)
- The only handle is `GameSession`: `Setup` (`SimulationSetup`: layout, items, combat, magic, crafting, social, quests), `Simulation` (read-only), `Content`, `TickSeconds`, `DisplayName(defId)`, `Subscribe<T>`/`Unsubscribe<T>` (`GameSession.cs:121-205`).
- `Simulation`'s read surface (`src/World/Runtime/Simulation.cs:185-262`) includes: `PlayerId`, `WorldTick`, `World` (a `WorldDelta`), `Posture`, `Player` (`PlayerView`), `Containers`, `WorldItems`, `Doors`, `Switches`, `Barriers`, `Combat` (`CombatView`), `Nodes`, `Creatures`, `Npcs`, `Companions`, `Conversation`, `Wares(npcId)`, `Quests`, `Diagnose(questId)`, `CellTiers`, `DynamicBlockers`, `SliceOwners`, `CommandLog`, `PendingCommands`, `Aim(facingMdeg, rangeMm)`, `CaptureRecord()`, `StateDigest()`. The views are immutable records (`ArchitectureTests.ViewsAndEvents_HaveNoPublicSetters`, `ArchitectureTests.cs:135`).
- `DynamicBlockers` = closed doors + living creatures as circles + non-companion NPCs as circles (`src/World/Runtime/Systems.cs:82-86`). Standing barriers are also included, per the doc comment at `Simulation.cs:254` ("closed doors, standing barriers, living creatures and NPCs").
- **Reading pattern.** Most views are read **every frame** in `Main.Draw` (`Main.cs:540-637`) and in the view nodes: `CreaturesView.Draw` (`src/Presentation/Greybox/CreaturesView.cs:33-62`), `NpcsView.Draw`, `CraftingView.Refresh`, which says nodes are "read each frame, never kept" (`src/Presentation/Greybox/CraftingView.cs:11-13`). Event subscriptions trigger discrete changes: door toggles, flags, item moves, toasts and logs (`Main.cs:639-870`). A full copy of state is taken only in `Resync()` after a new game or a load (`Main.cs:915-928`; `PlayerController.Resync`, `PlayerController.cs:50-59`).
- `PlayerController` keeps copies of what it is told: `_body` from `BodyMoved`, `_open` doors from `DoorToggled`, `_intent` (`PlayerController.cs:37-69`). Its summary: "It keeps a copy of what it was told (events and the load snapshot), never the truth, and writes nothing" (`PlayerController.cs:29-34`).

### 1.4 How presentation submits commands (FACT)
- `GameSession.Submit(GameCommand)` enqueues the command for the next tick boundary (`GameSession.cs:168-170`). `Simulation.DrainCommands` dispatches by type to the owning system, logs every command (with its rejection reason, if any) into `CommandLog`, and publishes `CommandRejected(command, reason, tick)` on refusal (`Simulation.cs:268-306`). Refusals are events, never exceptions (`tests/Application.Tests/SessionTests.cs:130`).
- `PlayerController` methods and the commands they send (`PlayerController.cs:76-271`):
  - `Steer`/`SteerWorld` send `MoveCommand(actor, MoveIntent(dirXPermille, dirZPermille, gait, facingMdeg))`, only when the wish changes by more than 0.5° or in direction or gait. "Scripted runs steer this way" (`PlayerController.cs:83-96`).
  - `Attack` → `AttackCommand`; `Jump` → `JumpCommand`; `Crouch(bool)` → `CrouchCommand`; `Cast(formulaId)` → `CastCommand`; `Guard(bool)` → `BlockCommand`; `Dodge(dir)` → `DodgeCommand(x‰, z‰)`; `UseConsumable` → `UseItemCommand` on the first carried item that has a use.
  - `Interact(key)` → `InteractCommand` (doors and switches); `Talk` → `TalkCommand`; `Order` → `OrderCompanionCommand`; `Revive` → `ReviveCommand`; `Gather` → `GatherCommand`; `Craft` → `CraftCommand`; `PickUp` → `MoveItemCommand(Ground → Carried)`.
  - Panels submit directly as well: Inventory, Trade and Character panel buttons submit `MoveItemCommand`, `EquipCommand`, `BuyCommand`, `SellCommand`, `TakeAllCommand` and `SpendAttributeCommand`. "Each row's buttons submit the same commands the headless tests do" (`src/Presentation/Ui/InventoryPanel.cs:14-19`).
- Commands carry no camera state, so every perspective submits the same thing (`tests/Application.Tests/SessionTests.cs:163` `Commands_CarryNoCameraState_SoEveryPerspectiveSubmitsTheSameThing`; `docs/M3_STATUS.md:35`).
- **Refusal feedback is opt-in by command type.** `Main.Subscribe` toasts `CommandRejected.Reason` only for listed command types (`Main.cs:662-670`, `780-784`, `864-869`). **A new M7 command must be added to one of these filters, or its refusal reasons will never reach the player** (INFERENCE from the code shape).

### 1.5 The prediction model (FACT)
- `PlayerController.Predict(alpha)`: the last authoritative body, advanced by `alpha × tick ms` with the simulation's own `Kinematics.Step(body, posture, intent, setup.Movement, setup.Layout.Space, simulation.DynamicBlockers, ms)`. Special cases: a dodge extrapolates the last step, a stagger holds still, attack phases and guarding walk, and an empty stamina pool downgrades sprint to run (`PlayerController.cs:98-129`).
- "A change of direction mid-tick can pop the drawn body forward by up to a tick's travel (16 cm at a run)" (`docs/M3_STATUS.md:51`).
- D-11: "Slight latency ... must be handled by **presentation-side prediction for feel only** ... never by writing state" (`docs/DECISIONS.md:240`).
- Creatures: drawn "between its last two ticks, so a 20 Hz body moves smoothly" (`CreaturesView.cs:13-18`; `Advance` on a new tick, then `Pose(creature, alpha, delta)` at lines 49-51).
- NPCs: "its drawn position between ticks is not predicted, only followed" (`src/Presentation/Greybox/NpcsView.cs:44`).
- **M7 implication (INFERENCE):** player-placed pieces that block movement must live in the collision set `Kinematics.Step` reads: `WalkSpace.Blockers` (static, from `RegionLayout`) and/or `DynamicBlockers`. `WalkSpace` is `record WalkSpace(MinX, MinZ, MaxX, MaxZ, TerrainGrid Terrain, ImmutableArray<Blocker> Blockers)` (`src/Domain/Spatial/Kinematics.cs:104`). Prediction reads `setup.Layout.Space`, a boot-time object, so runtime pieces must either rebuild that space or reach prediction through the `DynamicBlockers` path. Blockers are axis-aligned boxes (`BoxBlocker(Id, MinX, MinZ, MaxX, MaxZ, HeightMm)`, plus `ClearanceMm` for overhangs) or circles (`src/Domain/Spatial/Blockers.cs:41, 95`). **There is no rotated box.** Rotation in 90° steps fits AABBs, but the "free rotation" of D-08 and ROADMAP M7 does not (see section 13).

### 1.6 Architecture enforcement that binds M7 presentation code (FACT)
- `OnlyPresentation_MayReferenceGodot` (`tests/Architecture.Tests/ArchitectureTests.cs:27`).
- `TheSimulation_ExposesOnlyReadsAndTheCommandPath`: the public methods of `Simulation` must equal an exact allow-list: `Start, NewCharacter, CellOf, Enqueue, DrainCommands, Step, CaptureRecord, StateDigest, Wares, Diagnose, Aim`, and no property may have a setter (`ArchitectureTests.cs:112-133`). The comment on `Aim` reads: "where a shot would stop, for the aiming reticle (the owner's M6 playtest)" (`ArchitectureTests.cs:121`). **Any M7 query method, for example a placement validity or preview query, must be added here deliberately.**
- `PresentationSource_NeverReachesPastThePublicReadAndCommandSurface` scans every `.cs` under `src/Presentation` for the literal strings `BindingFlags.NonPublic`, `UnsafeAccessor`, `InternalsVisibleTo`, `.SetValue(`, `Activator.CreateInstance`, `System.Runtime.CompilerServices.Unsafe`, `.Step()`, `.DrainCommands()`, `IWorldStateWriter` and `SaveStore` (`ArchitectureTests.cs:163-183`). **Gotcha (INFERENCE):** any presentation identifier that ends in `.Step()` fails the build, for example a placement wizard's `wizard.Step()`, even if unrelated to the simulation.
- `StateAssemblies_HoldNoStaticMutableState` (`ArchitectureTests.cs:86`) applies to the state assemblies, not to Presentation. `Palette`'s static materials are presentation-only.

---

## 2. The complete current input map

### 2.1 Actions → bindings (FACT, `src/Presentation/Main.cs:1061-1104`; mouse handling `Main.cs:340-361`)

| Action | Binding | Behaviour, gating, where handled |
|---|---|---|
| `move_forward` / `move_back` / `move_left` / `move_right` | W/Up, S/Down, A/Left, D/Right | camera-relative stick → `Steer` (`Main.cs:365-367, 388`) |
| `sprint` | Shift (hold) | gait Sprint (`Main.cs:368`) |
| `walk` | Ctrl (hold) | gait Walk |
| `interact` | E | acts on `FocusOn(camera)`: door/switch → Interact; container → open panel; item → PickUp; node → Gather; station → open crafting panel; NPC → Talk, or Revive if a downed companion (`Main.cs:411-440`) |
| `jump` | Space | `JumpCommand` (`Main.cs:457-458`) |
| `crouch` | X | toggle; "C stays the dodge" (`Main.cs:459-460, 1078`) |
| `character` | K | character sheet toggle; frees or captures the mouse (`Main.cs:463-469`) |
| `help` | F1 | controls overlay (`Main.cs:470-471`) |
| `take_all` | R | **only while the inventory panel is visible** (`Main.cs:461-462, 1081`) |
| `first_person` | V | jump to or from first person (`Main.cs:472-473`) |
| `shoulder_swap` | Q | cycles right → left → centre (`Main.cs:474-475`; `CameraRig.cs:101-106`) |
| `debug_overlay` | F3 | HUD debug text, discovery rings and aim line (`Main.cs:476-477`) |
| `quest_debug` | F4 | quest debugger panel (`Main.cs:480-484`) |
| `journal` | J | journal panel (`Main.cs:478-479`) |
| `quicksave` / `quickload` | F5 / F9 | quick slot (`Main.cs:485-488`) |
| `release_mouse` | Escape | frees the mouse; in a dialogue, `Leave()` (`Main.cs:381-382, 489-490`) |
| `inventory` | Tab, I | open or close the inventory (`Main.cs:450-456`) |
| `dodge` | C | only when the mouse is captured and no panel is open (`Main.cs:403-407`) |
| `use` | H | first usable consumable (the salve) (`Main.cs:408-409`) |
| `companion_order` | G | toggles follow/wait for every companion that is up; comment "(content bible §9: no radial menu)" (`Main.cs:441-449, 1093`) |
| `cast_1..cast_3` | 4, 5, 6 | the formulas known, in learned order (`Main.cs:33, 387-399, 1094-1095`) |
| `reply_1..reply_9` | 1-9 | **only while the dialogue panel is visible** (`Main.cs:374-380, 1096-1097`) |
| `attack` | Left mouse | only when captured (`Main.cs:386, 1098-1103`) |
| `guard` | Right mouse (hold) | guard, or aim a bow (`Main.cs:385, 400-402, 521`) |
| (not an action) mouse motion | look | only when captured and no inventory, dialogue or character panel is open (`Main.cs:346-349`); sensitivity 0.0025, pitch clamp -1.35..1.2 rad (`CameraRig.cs:56, 79-83`) |
| (not an action) wheel up/down | zoom ∓0.35 m | always, including with a panel open (`Main.cs:350-355`); clamp 0..6 m (`CameraRig.cs:85`) |
| (not an action) Left mouse while the mouse is free and no panel is open | recapture the mouse | `Main.cs:356-359` |

Mode gating (FACT): `captured = MouseMode == Captured && !inventory && !dialogue && !character` (`Main.cs:384`) gates attack, guard, cast and dodge. The dialogue branch takes the number keys first (`Main.cs:374-383`). Scripted and perf modes skip `ReadInput` and `_UnhandledInput` entirely (`Main.cs:297-300, 342-343`). Headless runs never capture the mouse (`Main.cs:220, 467, 754, 760, 999, 1012`).

The README's controls line matches this map (`README.md:432`).

### 2.2 Free keys (FACT from the binding list; INFERENCE on suitability)
- **Unbound letters:** B, F, L, M, N, O, P, T, U, Y, Z.
- **Unbound function keys:** F2, F6, F7, F8, F10, F11, F12. The used ones are F1 help, F3 debug, F4 quest debugger, F5 save and F9 load.
- **Digits:** 0 is unbound. 1-3 and 7-9 are unbound outside dialogue, but the bible's "Recommended prototype convenience layer: 8 slots" puts sword, bow and spear on 1-3 and a restorative and a torch on 7-8 (`...CONTENT_BIBLE.md:570-589`; UNTRACKED HUD spec `PHASE1_HUD_UI_CONTROLS_AND_FEEDBACK_SPEC.md:33-49`). **The 8-slot hotbar is not implemented**: only 4-6 are hotbar keys, and weapon switching goes through the inventory panel.
- **Other unbound keys:** Enter, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Alt, CapsLock, the punctuation keys, the numpad, the middle mouse button and the extra mouse buttons.
- **Collision risks for a building mode (INFERENCE):**
  - The wheel is zoom and is handled in `_UnhandledInput` even with panels open. Rotating a piece on the wheel would need either a mode flag that suppresses zoom or a modifier.
  - LMB is attack, gated only by `captured`. A "click to place" must be gated like the dialogue branch.
  - R already means take-all, but only inside the container panel, so an R-to-rotate in build mode is context-separable.
  - Q is shoulder swap.
  - Esc frees the mouse. Nothing uses Esc as "cancel" outside dialogue, so a build mode could claim it, but it would collide with the current mouse-release meaning.
  - Tab and I are the inventory.
  - Keys use `PhysicalKeycode`, so on AZERTY the physical W position is still "forward". The F1 overlay prints `OS.GetKeycodeString(key.Keycode != None ? key.Keycode : key.PhysicalKeycode)` (`src/Presentation/Ui/HelpPanel.cs:117`), which shows the physical name.
- **F1 lists only what `HelpPanel.Sections` names.** The keys are read from the input map ("never a list that could drift from it"), but the set of actions and their sections is a hard-coded table (`HelpPanel.cs:14-45`; the developer keys at `42-45`). **A new build action must be added to `Sections`, or F1 will not show it** (FACT from the code; the M6 claim "the list cannot drift from the keys", `docs/M6_STATUS.md:159`, applies to the keys, not to the set of actions).

### 2.3 Not implemented (FACT)
- Rebinding and remapping, and gamepad: `docs/M3_STATUS.md:86` ("Keyboard and mouse only for now"); `docs/M6_STATUS.md:182`.
- Hold/toggle options, HUD modes (Full / Minimal / Auto-hide / Off; `...CONTENT_BIBLE.md:686-691`), a switch to disable the tracker (UNTRACKED HUD spec §20 line 301 "tracker can be disabled"), and UI scale. A grep of `src/Presentation` for HudMode, Minimal, AutoHide and UiScale found nothing.
- Pause: nothing pauses. `_session.Frame` runs every frame whatever panel is open (`Main.cs:305`); there is no pause code in `src/`. UNTRACKED `DIFFICULTY_ACCESSIBILITY_AND_PLAYER_CUSTOMIZATION_DRAFT.md` §17 (draft): "Single-player should pause in menus where technically appropriate unless a specific system intentionally remains live." `WORLD_ARCHITECTURE.md:249` sets the time scale as "debug/pause only". **INFERENCE:** a building UI in which the player browses a catalogue while creatures keep acting is the current default. Whether M7 build mode pauses is an open design question.

---

## 3. UI doctrine (quoted)

### 3.1 Radial menus (owner ruling 5)
- Bible §20, the formal rule: "**A radial menu may exist as an optional convenience layer, but no core action, spell, weapon, companion command, building function, emote, or interaction may require a radial menu.**" (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:645`). Also "Radial menus are not the default interaction model in Otherreach" (`:641`) and "No capability disappears if radial menus are disabled." (`:670`).
- Bible §20, the primary interfaces: "direct keybinds; hotbars; contextual prompts; tabs; searchable/categorized lists; conventional menus" (`:647-654`).
- Bible §20, the design of any optional radial: "large forgiving slices; strong snap; text labels plus icons; center dead-zone protection; configurable sensitivity; remembered last selection; optional explicit confirmation; no mandatory release-to-select; no nested radial chains where avoidable; keyboard/controller parity; optional pause or slow-time in single-player" (`:656-668`).
- Bible §9: "No radial menu is required." for the companion's Follow/Wait (`:288`). This is implemented as G (`Main.cs:1093`; `docs/M6_STATUS.md:56`: "no radial menu (bible §9)").
- UNTRACKED HUD spec §2: "**Radial menus are optional convenience interfaces only. No core capability may require one.**" and "No weapon, spell, technique, companion order, building action, emote, or interaction is radial-only." (`G:/UNNAMED/docs/PHASE1_HUD_UI_CONTROLS_AND_FEEDBACK_SPEC.md:19-21`). §20 acceptance: "no required radial menus" (`:302`). §9: "No required companion wheel." (`:153`).
- UNTRACKED V4 handoff §3: "Owner strongly dislikes mandatory radial menus." and "**Radial menus are optional convenience only; no core action, spell, weapon, companion command, building function, emote or interaction may require a radial.**" (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:78, 82`).
- UNTRACKED accessibility draft §12: "never mandatory; optional only ... Everything in a radial must also exist via direct/menu access." (`DIFFICULTY_ACCESSIBILITY_AND_PLAYER_CUSTOMIZATION_DRAFT.md:130-138`).
- **Soft conflict:** `docs/HUD_INPUT_AND_ACTIONS.md:24-36` lists "radial menus" among the access methods and says "Controller users should retain the same underlying capability through context/radials." (`:36`). This does not make radials mandatory for keyboard, but it can be read as radials being *the* controller path, which bible §20's "keyboard/controller parity" and "No capability disappears if radial menus are disabled" rule out. The doc's status is "Strong working direction" (`:3`), older than the bible.
- Code: **no radial exists anywhere in `src/`**. The only match for "radial" is the comment at `Main.cs:1093`.

### 3.2 Direct keys, hotbar, prompts
- "**A hotbar is a convenience layer, not a capability limit.**" (`docs/HUD_INPUT_AND_ACTIONS.md:8`). "The hotbar is a shortcut, not the only way to access capability." (`...CONTENT_BIBLE.md:589`).
- Prompt style: UNTRACKED HUD spec §3 "Center — reticle / contextual interaction", with examples `[E] Talk to Kera`, `[E] Open`, `[E] Mine Iron Vein`, `[E] Use Forge`, `[E] Inspect Quiet Stone` (`PHASE1_HUD...SPEC.md:51-61`).
- Implemented prompts (FACT, `Main.cs:573-587`), bottom centre (`src/Presentation/Ui/Hud.cs:64-67`): `[E] Open/Close the <door>`, `[E] Open the <container>` / `[E] Search the <corpse>`, node prompts, `[E] Work at the <station>`, `[E] Help <npc> up`, `[E] Talk to <npc>`, `[E] <Verb> the <Name>` for switches (both words from content), the barrier's own content prompt, and `[E] Pick up <item>`.
- UNTRACKED runtime acceptance plan §5: "Prompts should not trigger through walls or beyond intended range." (`PHASE1_RUNTIME_ACCEPTANCE_AND_PLAYTEST_PLAN.md:118`). **FACT:** `FocusOn` checks only the reach rule plus camera alignment, with **no occlusion test** (`PlayerController.cs:181-238`; alignment must be ≥ 0.5 in first person, `:229`). **INFERENCE:** once players build thin walls, a chest or door within 1.6 m on the other side of a wall could be offered through it, unless the simulation's reach rule gains a line-of-sight check.
- Local hints: "[Tab] close", "Take all [R]", "[K] close", "[F1] close", "[K] a point to spend" (`docs/M6_STATUS.md:159`; `Main.cs:595`).

### 3.3 HUD philosophy, and debug vs player-facing
- UNTRACKED HUD spec §1: "**The HUD should show what the player needs to act, not everything the simulation knows.**" (`PHASE1_HUD...SPEC.md:9`).
- UNTRACKED HUD spec §19 keeps a debug layer for "coordinates/cell; FPS; AI awareness; damage; hit region; exact HP; persistence IDs; event logs" and says "Do not delete useful instrumentation when cleaning the player-facing HUD." (`:275-287`).
- UNTRACKED runtime acceptance plan §2: "Developer overlays may remain available but should be disableable." (`PHASE1_RUNTIME...PLAN.md:28`).
- Bible §19: "Exact numeric health may remain in development/debug mode." (`:568`). The M3 debug HUD "may remain while systems are under active development" (`:534`).
- "Do not create two independent game UIs." (`docs/CAMERA_PERSPECTIVE_AND_PRESENTATION.md:316`). The HUD is shared across perspectives (`Hud.cs` summary).
- Charter "no markers": the F3 discovery rings are "Never shown in normal play: no markers (charter §3)" (`HollowView.cs:331`). The journal gives "text directions, never a marker (charter §3)" (`src/Presentation/Ui/JournalPanel.cs:13-14`).

### 3.4 Camera doctrine relevant to building
- Building: "Building may permit a somewhat farther camera than ordinary exploration. Do not turn it into an unrestricted RTS camera. Camera distance must remain bounded to avoid scouting abuse." (`docs/CAMERA_PERSPECTIVE_AND_PRESENTATION.md:266-272`).
- Collision: "The third-person camera must not become a surveillance drone. It cannot: pass through walls; orbit arbitrarily far around corners; reveal spaces the camera physically cannot reach." (`:90-98`).
- Indoors: "Camera collision should compress distance naturally indoors ... Do not force a perspective change on players who dislike it." (`:76-86`).
- Contextual camera memory: an optional preferred distance per activity includes "building", and "Manual player zoom always overrides." (`:320-330`).
- Perspective-neutral: "Perspective affects presentation/input mapping only." (`:124`).
- Implemented camera (FACT, `CameraRig.cs`): one `SpringArm3D`. `MaxDistance = 6f` (`:23`), `FirstPersonBelow = 0.35f` (`:26`), `ShoulderOffset = 0.45f` (`:28`), FOV 75, near 0.05 (`:35`), default distance 3.5 m and pitch -0.3 rad (`:41-44`). The spring arm uses a sphere of radius 0.2 with margin 0.05 against `HollowView.CameraCollisionLayer` = 1 (`:73-75`). The eye is at 1.62 m standing (`src/Presentation/Player/Avatar.cs:19`) and 1.05 m crouched (`CameraRig.cs:54`).
- **There is no free camera or fly camera in play.** Only the spike has a scripted `DebugCamera` (`SpikeScene.cs:31, 283-303`), and `UiShots` can override the viewpoint for its creature gallery (`src/Presentation/UiShots.cs:123-124, 246-258`). **INFERENCE:** a "somewhat farther" building camera would be a new bounded `MaxDistance` for build mode, for example raising the 6 m cap. The spring arm keeps its wall collision, which requires new pieces to carry camera colliders.

### 3.5 Accessibility hooks to preserve (doctrine)
- `docs/HUD_INPUT_AND_ACTIONS.md:110-120`: scalable UI, remapping, hold/toggle options, colour-independent indicators, subtitle controls, combat assists, controller parity.
- `docs/CAMERA_PERSPECTIVE_AND_PRESENTATION.md:342-352`: FOV, camera distance, shoulder, camera shake, head bob, motion blur, sensitivity, inversion, auto-camera behaviour.
- UNTRACKED HUD spec §17 (`:230-244`) and the UNTRACKED accessibility draft §11 ("full remapping; hold/toggle options; sprint toggle; aim toggle; crouch toggle ...", `:118-127`).

---

## 4. HUD and panel inventory (FACT) - conventions a building UI would reuse

- **`Hud : CanvasLayer`** (`src/Presentation/Ui/Hud.cs`):
  - status text top-left (`:57-58` area);
  - companion line at (24, 112);
  - prompt bottom centre (`:64-67`);
  - compass top right (`:71-72`);
  - toasts top centre (`:75-77`), via `Toast(text, seconds=4)` (`:277`);
  - tracker top right below the compass (`:86-90`);
  - debug label top right, 500x600, hidden by default (`:93-96`);
  - vitals bottom left;
  - target bar top centre;
  - combat log bottom right, 7 lines, via `Log(line)` (`:129-133, 221-227`);
  - death panel centred;
  - crosshair and reticle.
  - Setters: `SetStatus`, `SetPrompt`, `SetCrosshair`, `SetHeading`, `SetReticle`, `SetDebug`, `SetCompanions`, `SetTracker`, `SetVitals`, `SetMagicPools`, `SetMagic`, `SetEffects`, `SetTarget`, `ShowDeath`, `DebugVisible` (`:146-277`).
- **Panels** are `CanvasLayer` subclasses with `Visible=false` at `_Ready`, built from `PanelContainer`, `VBoxContainer`, `Label` and buttons in code, with no `.tscn`:
  - `InventoryPanel`: carried, container, station, trader and "Take all [R]" (`InventoryPanel.cs:20-66`);
  - `DialoguePanel`, `JournalPanel` (J), `QuestDebugPanel` (F4), `CharacterPanel` (K) and `HelpPanel` (F1).
- A panel "opened at a place" has an `Anchor` and **closes by itself when the body walks out of reach** (`InventoryPanel.cs:39-43`; `Main.cs:493-507`; `docs/M6_STATUS.md:157`). Opening one frees the mouse and closing captures it (`Main.cs:990-1014`). **INFERENCE:** a placeable crafting station or storage container in M7 gets this behaviour for free if it is modelled as a `StationSite` or container in reach. The current stations come from `RegionLayout.Stations` (`Main.cs:1005`), which is static.
- Styling: font-size overrides of 15-22, a gold heading colour `Color(0.95, 0.85, 0.55)` (`HelpPanel.cs:131`), and fixed positions for 1920x1080. **No theme resource and no UI scale.**
- Icons: `HudIcons` from the asset manifest when `--asset-root` is set, with text fallback (`Main.cs:183-185`).
- Words, not IDs: panels use `session.DisplayName(defId)` and the key-to-words helper `Main.Describe`: `door.forge_shed` → "forge shed door" (`Main.cs:1024-1031`). DeltaShots: "Scripts may name content; the game may not." (`src/Presentation/DeltaShots.cs:23`). M6 fixed a slip "against the no-IDs rule" (`docs/M6_STATUS.md:156`).

---

## 5. How a structure or blocker is rendered today (greybox builders)

### 5.1 `HollowView.Build(RegionLayout)` - one pass at boot (FACT, `src/Presentation/Greybox/HollowView.cs:36-54`)
Summary: "Builds Ashen Hollow from `RegionLayout`: the terrain surface (the same triangles the domain samples), every structure and door as a box or cylinder, and scenery the domain never sees - roofs, the stream's water, the ravine, trees' canopies, the sky. **Colliders exist only for the camera; the body collides in the domain.**" (`:10-14`).

- **Terrain** (`:91-128`): the domain's triangles (each quad split along its (0,0)-(1,1) diagonal), one `SurfaceTool` surface per cell so each cell can wear its bound ground material, and one `ArrayMesh`. Camera collider: `StaticBody3D{CollisionLayer=1, CollisionMask=0}` with `mesh.CreateTrimeshShape()` (`:124-126`).
- **Each `Blocker`** in `layout.Space.Blockers` is drawn by `ArtStructure(...)` if the art bindings give a usable model, otherwise by `BuildStructure(...)` (`:40-41`):
  - `BoxBlocker` → `BoxMesh` plus `BoxShape3D` of the same size. The base sits 0.2 m under the lowest terrain sample of its corners and centre, and the height is `HeightMm + 0.2`. **An overhang** (`ClearanceMm > 0`) is drawn from the clearance up (`:166-174`).
  - `CircleBlocker` → a `CylinderMesh` (top radius 0.8r, or 0.7r for IDs starting `tree_`) plus a `CylinderShape3D`. Trees add a canopy `SphereMesh` (r 2.4, h 3.6) with **no collider**, so the camera passes through foliage (`:176-195`).
  - Material by ID prefix: `den_rock*` or `rock_*` → `Palette.Rock`, otherwise `Palette.Wood` (`:383-384`).
- **Roofs** are scenery with camera colliders. For each of the **hard-coded groups `{"longhouse", "forge"}`**, the union AABB of `BoxBlocker`s whose ID starts `<group>_` gets a 0.3 m slab 0.6 m wider than the walls (`:202-217`).
- **Doors**: a hinge `Node3D` plus a panel `Solid` (`BoxMesh` + `BoxShape3D`, `Palette.Door`). The swing direction is inferred from the average position of the same-prefix wall group (`:219-257`). `SetDoor(key, open)` rotates the hinge by ±90°, and **the camera collider rotates with it** (`:56-60`).
- **Switch marks**: a pale band, no collider, visible when set. **Barriers**: a translucent `Palette.Fold` haze, no collider, visible while standing (`:260-296`). Both are toggled by `SetFlags` from `WorldFlagChanged` (`Main.cs:653`).
- **Water** plane, the **ravine and cliff** boxes (camera colliders), **debug markers** (discovery-radius rings, alpha material, hidden) and **lighting** (a shadowed `DirectionalLight3D` plus a `WorldEnvironment` with a procedural sky, filmic tonemap and fog 0.002) (`:299-372`).
- `Solid(name, mesh, shape, material)` is the one helper: `MeshInstance3D` + `StaticBody3D(layer 1, mask 0)` + `CollisionShape3D` (`:374-381`).

### 5.2 Art bindings over greybox (FACT)
- `ArtBindings.Structure(id)`: exact ID first, then a prefix list (a tree's model chosen by an FNV hash of the ID) (`src/Presentation/Art/ArtBindings.cs:69-84`). The bindings live in `res://Art/art_bindings.json` (`:36`) and are "Data, so no content ID lives in the game's code; presentation only, so nothing here touches a rule, a save or the content hash" (`:29-32`).
- `Fitting.Structure`: the model at its **authored size**, "nothing here rescales one to a collision shape". If the model would leave more than 0.5 m of invisible wall at a side, or stops more than 0.6 m below the top, the greybox stands instead and the mismatch is reported. `tile` repeats a modular piece along its run (`src/Presentation/Art/Fitting.cs:9-19, 44-80`). **The camera collider stays the greybox shape**: "what the body collides with is the truth" (`HollowView.cs:150-157`).
- `building_walls` prefixes decide indoor footsteps: the AABB of same-prefix walls is "indoors" (`src/Presentation/Audio/SoundEvents.cs:62-70, 439`). They also set arrow-impact material sounds (`StructureMaterial`, `ArtBindings.cs:87-98`; `art_bindings.json:87-90`).
- **Building kit status:** withheld. The colour maps are "photograph[s] of a sample on a grey backdrop" that cannot tile. The kit pieces are `building_wall_timber`, `building_wall_stone`, `building_roof_panel`, `building_floor_planks` and `building_fence_panel`, and the two buildings assembled from it (`forge_shed`, `longhouse`) are also withheld (`art_bindings.json:25-31`; `docs/PHASE1_ASSET_INTEGRATION.md:35, 85-88`). "Buildings and ground: - (library) | the longhouse, the forge shed and the four cells' ground (the kit and the world materials are withheld)" (`PHASE1_ASSET_INTEGRATION.md:35`). The `fence_smithy` does use `building_fence_panel` with `"fit": "tile"` (`art_bindings.json:77`).
- The LOD files are unusable: "Every `_lod1`, `_lod2` and `_lod3` file in the library - 1,455 files, 485 of each - carries no [material]" (`PHASE1_ASSET_INTEGRATION.md:21`). So the full-detail model is drawn at every distance.

### 5.3 Runtime-changing geometry today (FACT)
- `ItemsView.Refresh(simulation)` **frees every ground-item node and rebuilds from `simulation.WorldItems`** on each `ItemMoved` touching the ground (`ItemsView.cs:58-80`; `Main.cs:676-681`). Containers are built once, each with a 0.9×0.6×0.6 box camera collider (`ItemsView.cs:31-56`).
- `CreaturesView` creates a figure lazily per creature ID and frees figures whose ID disappeared (`CreaturesView.cs:33-62`). `Reset()` runs after a load (`:73-79`).
- **No view adds or removes structures at runtime.** **INFERENCE for M7:** a `StructuresView` or `BuildingsView` node would need:
  - (a) an initial build from the authoritative building records;
  - (b) incremental add and remove on piece events, which is cheaper than an `ItemsView`-style full rebuild once pieces reach hundreds;
  - (c) a reset in `Main.Resync()` after load (`Main.cs:915-928`);
  - (d) camera colliders on layer 1 for every solid piece, since the camera doctrine forbids seeing through walls;
  - (e) explicit door-hinge and roof data per piece rather than the prefix heuristics of `HollowView.cs:203-217, 250-254`.

---

## 6. Godot physics and navigation usage - the complete list (FACT)

A search of `src/` for `.cs`, `.tscn` and `.godot` files (excluding `obj/` and `bin/`) for `Navigation|NavMesh|PhysicsServer|RayCast|CharacterBody|RigidBody|StaticBody|CollisionShape|SpringArm|Area3D|IntersectRay|ShapeCast|MoveAndSlide` returns only these:

| Where | What | Purpose |
|---|---|---|
| `src/Presentation/Player/CameraRig.cs:31, 73-75` | `SpringArm3D`, sphere r 0.2, mask = layer 1 | third-person camera collision and indoor compression |
| `src/Presentation/Greybox/HollowView.cs:124-126` | terrain `StaticBody3D` + trimesh | camera collision |
| `HollowView.cs:152-156, 374-381` | `StaticBody3D` + box/cylinder `CollisionShape3D` on every structure, roof, door panel and ravine box | camera collision; layer 1, mask 0 |
| `src/Presentation/Greybox/ItemsView.cs:41-42` | container box collider | camera collision |
| `src/Presentation/Spike/SpikeScene.cs:26, 159-187, 189-199, 204-213` | `NavigationServer3D.MapSetCellSize(1)` / `MapSetCellHeight(0.5)`; 64 `NavigationRegion3D` tiles (8×8 of 250 m; `CellSize 1`, `CellHeight 0.5`, agent radius 1, height 2, max climb 1, max slope 40°, border 2; parsed geometry = `StaticColliders` from group `spike_navigation`); `BakeNavigationMesh(onThread: true)` one tile after another; `HeightMapShape3D` terrain | **measurement only**: bake time and polygon count go into the spike's `summary.json` `navmesh` note (`SpikeScene.cs:313`). No path is ever queried. |

- **No character body, rigid body, raycast, area or NavigationAgent exists anywhere.** Body movement, collision, reach, line of fire (`Combat.Trace` via `Simulation.Aim`) and creature and companion movement are all domain or World code.
- The spike result: "The spike's navmesh bakes as 64 tiles of 250 m in 6.2 s (14,291 polygons). One 2 km bake overflowed Recast's region IDs, and tiles are what streaming needs anyway" (`docs/M3_STATUS.md:72`). HISTORY: `perf_astral_m6_route3/spike/summary.json` gives "64 tiles of 250 m in 6.2 s, 14291 polygons".
- **Relation to owner ruling 1 (INFERENCE):** the codebase already complies. Godot navigation is non-authoritative and confined to a presentation-only spike. The only existing measurement of navigation cost, however, is of **Godot's Recast bake**, which ruling 1 excludes from authority. There is no measurement of a domain-side navigation rebuild. ENGINE_VALIDATION asks for "navigation cost" among its metrics (`docs/ENGINE_VALIDATION.md:56`).
- Several older docs describe a baked navmesh without saying which layer owns it. Examples: "Navmesh | Recast-style, baked per cell, stitched at cell borders, rebuilt debounced on building change" (`docs/WORLD_ARCHITECTURE.md:435`); "the navmesh updates on placement", "navmesh path test" (`docs/ROADMAP.md:284-285`); the RK-06 and RK-14 wording (`docs/RISK_REGISTER.md:146, 154, 286-294`). None of them says Godot's NavigationServer is authoritative, but "baked" and "Recast-style" read naturally as the engine navmesh. See section 13.
- Godot 4.7.2 exposes navigation monitors (`Performance.Monitor.NavigationPolygonCount`, `TimeNavigationProcess`, etc.; verified in the local `GodotSharp.xml`, `C:/Users/jluca/.nuget/packages/godotsharp/4.7.2/lib/net8.0/GodotSharp.xml`). They would measure only engine navigation and are irrelevant if navigation stays in the domain.

---

## 7. Debug and prototype UI conventions

- **F3 debug overlay** (`Main.cs:476-477, 625-636`) shows the top-right label with:
  - `<fps> fps <ms> ms`;
  - `tick <n> alpha <a> pending <commands>`;
  - body position in metres and facing;
  - `cell <CellKey.OfWorld>`;
  - camera mode, effective/target distance and side;
  - `tiers:` one line per cell with its tier;
  - `discovered:` places with their tick.
  - F3 also shows the discovery-radius rings (`HollowView.cs:331-348`) and the bow or bolt aim line from the body to the reticle point, "never otherwise" (`Main.cs:509-537`; `docs/M6_STATUS.md:156`: "No trajectory line in play: F3's debug overlay alone draws one").
- **F4 quest debugger** (`src/Presentation/Ui/JournalPanel.cs:72-117`): for every quest in content, `simulation.Diagnose(id)` answers "what is this quest waiting on right now?". The active quest shows its full diagnosis and last 3 trace entries; the others get one line. It redraws at most 4 times a second ("a diagnosis walks the content, which is cheap but not free", `:90-100`). The backing query is `Simulation.Diagnose` (allow-listed). M6 extended it so a `world_state` objective lists the switches that set its flag (`docs/M6_STATUS.md:47`).
- **F1 controls** show a "DEVELOPER" heading with "Debug overlay (with the aim line)" F3 and "Quest debugger" F4 (`HelpPanel.cs:42-45, 98-100`).
- **Doctrine for overlays:** WORLD_ARCHITECTURE requires a tier overlay as a Phase-1 deliverable ("a debug overlay that shows the tier of every actor in view", `docs/WORLD_ARCHITECTURE.md:267`) and a fuller one: "The debug overlay must display: current cell key, its tier, every actor's tier and the reason for its last transition, the caps' headroom, and the last save time." (`:458`). **The current F3 shows cells' tiers and the cell key but not per-actor tiers or transition reasons, caps headroom or last save time** (FACT from `Main.cs:628-635`).
- **INFERENCE for M7 debug views:** the established pattern is an F-key that toggles a `CanvasLayer` text panel fed from a read-only `Simulation` query (for example `Diagnose`), plus optional world-space markers under a hidden `Node3D` (`_debug` in HollowView). Free F-keys are F2, F6, F7, F8, F10, F11 and F12. Candidates:
  - a navigation debug view (walkable graph and portal overlay drawn from a domain nav snapshot, with dirty regions per edit and seam links);
  - a building debug view (sockets, piece IDs, owners, health);
  - a faction and reputation inspector.
  All would be read-only queries that must enter the `Simulation` allow-list.

---

## 9. Runtime acceptance tooling (section 8 intentionally omitted; numbering kept for cross-references)

### 9.1 `--smoke` (headless boot smoke) - FACT
- Command: `godot --headless --path src/Presentation -- --smoke`. Exit 0 = pass (`AGENTS.md`; `src/Presentation/Smoke.cs:15-22`).
- What it does, through the real command path:
  1. walks to the lodge door and opens it;
  2. walks in to whoever's conversation hands over a teaching book, found in the data rather than named, and takes it;
  3. reads it and works the first self formula;
  4. takes a quest found in the data;
  5. swings at nothing (the attack runs its phases and misses);
  6. checks that the spawners placed the expected creatures;
  7. quicksaves and quickloads, and requires `loaded.IsComplete`, an identical `StateDigest()`, the same tick and strain, and the lines heard remembered (`Smoke.cs:199-212`).
- It prints `UNNAMED smoke: PASS - ...` (`:213`) and returns 0 (`:219`). It times out after 3000 frames (`:65`) and always deletes its scratch profile (`:306`).
- Separate boot check: `godot --headless --path src/Presentation --quit-after 300` (`AGENTS.md`; `docs/PROTOTYPE.md:250` "Every commit").
- **CI does not run Godot.** `.github/workflows/dotnet.yml` runs `dotnet restore`, `dotnet build` and `dotnet test` in `./src` on `ubuntu-latest`. The solution includes `Presentation\UNNAMED.Presentation.csproj` (`src/UNNAMED.sln:10`), so presentation **compiles** in CI but no smoke runs there ("The boot smoke is not in CI. The Linux runner has no Godot", `docs/M3_STATUS.md:88`).

### 9.2 `--ui-shots <dir>` - FACT
- Windowed. It plays the whole M3-M5 journey and saves a PNG after each step (`src/Presentation/UiShots.cs:16-30`): inventory; Renn; Sel and the primer; a ward's tell and Strain; the creature gallery (camera `Viewpoint` override); the boar fought; the seam; the stand; the forge, billet and spear; trade; the quest completing; a stray wounded; the death at the den.
- Exit code 0, or 1 on a step that fails. `Main` saves `failed.png` on failure (`Main.cs:247-262`).

### 9.3 `--playthrough <dir>` and `--playthrough-verify <dir>` - FACT
- Summary: `src/Presentation/Playthrough.cs:19-33`.
- **Determinism.** Fixed world seed `Playthrough.Seed = 0x0A5E_2026_0924_0001` (`:38`). `Engine.MaxFps = round(1/TickSeconds)`, i.e. 20 (`Main.cs:200-202`). `_session.Frame(TickSeconds)`, one tick a frame (`Main.cs:305`). The save slot is `"acceptance"` (`Playthrough.cs:40`), in `<dir>/profile` (`Main.cs:88`).
- **Beat model.** `record Beat(string Name, string Says, int Budget, Func<bool> Run, string? Shot = null)` (`:71`):
  - `Budget` is a tick budget; exceeding it fails the run (`:144-145`).
  - An exception in `Run` fails the run (`:149-156`).
  - Any death during the acceptance run fails it (`:142-143`).
  - A beat with `Shot` waits 6 frames, then returns the screenshot name for `Main` to save (`:175-179`; `Main.cs:283-295`).
- **Beats.** 26 play beats (`:185-218`), from spawn through chest, smithy, Kera, east, hound, cart, stand, quarry, seam, home, billet, spear, quest1, Sel, primer, Foldscar, the three stones, the fold, heart, Tavar, follow and ward to save. 5 verify beats: loaded, wait, den, death, respawn (`:220-230`).
- **Movement helpers.** `Travel` = `!Defend() && !Mend() && Walk(route)` (`:585`). `Walk` steers with `SteerWorld` toward (x, z) waypoints and snaps the camera yaw to the heading (`:587-603`). They are scripted waypoints, not pathfinding.
- **Outputs.**
  - `transcript.md`: a `| Game time | Tick | What happened |` table, from event subscriptions plus beat lines with `![name](name.png)` (`:111-117, 168, 534-580`).
  - `commands.tsv`: each `Simulation.CommandLog` entry as `tick \t sequence \t command \t rejected: reason` (`:523-530`).
  - PNG screenshots.
  - `state_saved.json` and `state_replay.json` via `StateDump.Render(simulation[, replayable:true])` (`:422-438`).
  - Verify mode writes `state_loaded.json` and `state_diff.txt` (`StateDump.Compare`; "Fields compared: N / Differences: M") (`:443-462`), and `transcript_relaunch.md` / `commands_relaunch.tsv`.
  - `Finish` returns "done" or "failed" (`:515-521`), and `Main` quits 0 or 1 (`Main.cs:281-295`).
- **`StateDump`** (`src/Application/StateDump.cs:12-47`): "Everything a save must bring back, as JSON with every field named": `world_tick`, `player` = `simulation.CaptureRecord()` and `world` = `simulation.World.TakeSnapshot()`. "Nothing is left out by hand; the records are written as they are." The replayable variant renames instance IDs by order of appearance, because instance IDs are fresh ULIDs per game (D-04). **INFERENCE:** M7 state (buildings, pieces, faction standing) enters the field-by-field comparison automatically if it lives in the player record or the `WorldDelta` snapshot. State kept anywhere else is invisible to this proof.
- **Recorded results.** Every beat happened, in 6:04 of game time; "**412 fields compared, 0 differences**"; the replay's `state_replay.json` is byte-identical (SHA-256 `5DBE95FB...`) (`docs/M6_STATUS.md:80-83`). After the delta: 415 fields, 0 differences; 79 of 80 transcript rows identical (the other is the raw digest) (`docs/M6_STATUS.md:171-172`). The closeout adds "2,499 commands identical (instance IDs masked)" (`:218`).

### 9.4 `--delta-shots <dir>` - FACT
- `src/Presentation/DeltaShots.cs:18-24`. The same beat model (`record Beat(Name, Says, int Budget, Func<bool> Run)`, `:43`), one tick a frame (`Main.cs:204-209`), seed `Playthrough.Seed` (`Main.cs:103`).
- 21 beats, `d01_compass_third` through `d21_strained_overlay` (`DeltaShots.cs:85-108`), each a picture of one playtest change. On a budget overrun it reports the last 3 refusals (`:130-131`).
- Writes `transcript.md` (`| Tick | What the picture shows |`, `:611-612`) and a PNG per beat.
- "All 18 beats pass with and without the asset workspace. The runs caught five things, since fixed" (`docs/M6_STATUS.md:170`). The closeout reports all 21 beats in the editor and in a fresh clone in greybox (`:219-220`).
- This is **the closest template for an M7 "each feature shown" proof.**

### 9.5 `--perf`, `--spike` and `--art-gallery` - FACT
- `--perf` and `--spike`: see section 10.
- `--art-gallery <dir>`: renders the bound models for review (`Main.cs:79-84`; `src/Presentation/Art/ArtGallery.cs`).

### 9.6 Headless test harness and generated evidence (FACT)
- `tests/Application.Tests/Harness.cs:31-66`: `Boot(TempProfile)`, `Ticks(session, n)`, `WalkTo(session, xMm, zMm, gait, maxTicks=4000, toleranceMm=300)` and `WalkPath(session, waypoints...)`. It drives the real `GameSession` without Godot. The M6 exit proofs are mostly here (for example `ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds`, `docs/M6_STATUS.md:65`).
- **Generated-doc pattern.** `docs/M3C_TTK_TABLE.md` and `docs/M3D_BEHAVIOUR_MATRIX.md` are "Generated from the build; do not edit". The test fails when the committed file differs from what the build produces, and regeneration uses an env var: `UNNAMED_WRITE_TTK=1` (`tests/Application.Tests/TtkTableTests.cs:14-18, 70-73`; `docs/M3C_TTK_TABLE.md:3`). **INFERENCE:** ROADMAP M7's "a reputation fixture table" proof (`docs/ROADMAP.md:285`) fits this pattern exactly.

### 9.7 `docs/acceptance/` contents - FACT
- `docs/acceptance/m6/`:
  - 21 play JPEGs (`01_spawn.jpg` … `21_saved.jpg`) and 4 relaunch JPEGs (`30_loaded`, `31_den`, `32_death`, `33_respawn`);
  - `transcript.md` (86 lines) and `transcript_relaunch.md` (19);
  - `commands.tsv` (2,499 lines, 445 KB) and `commands_relaunch.tsv` (152);
  - `state_saved.json` (645 lines), `state_replay.json` (643) and `state_diff.txt` (5 lines: "Fields compared: 412 / Differences: 0").
- `docs/acceptance/m6_delta/`: `d01…d18` JPEGs and `transcript.md` (18 beats). **The d19-d21 beats added in the closeout have no committed stills** (FACT: the listing ends at d18).
- Committed stills are **JPEG** and the transcripts link `.jpg` (`docs/acceptance/m6/transcript.md`, for example `![01_spawn](01_spawn.jpg)`), but the code writes and links **PNG** (`Main.cs:982-986`; `Playthrough.cs:168`). The evidence was converted after the run (INFERENCE).
- Videos are kept **outside** the public repository: "`playthrough.mp4`, 29 MB, and `relaunch.mp4`, 3 MB, 1920x1080 at 20 fps - real time ... on ASTRAL at `G:\UNNAMED_HISTORY\acceptance_final\`" (`docs/M6_STATUS.md:76`). Delta shots with art are at `G:\UNNAMED_HISTORY\delta_shots_5\` (`:165`).
- An acceptance artifact is therefore a deterministic, scripted, fixed-seed run through the real command path, with a tick-stamped markdown transcript, the command log, one screenshot per beat, field-by-field state JSON plus a diff, and a byte-identical replay check.

### 9.8 How a new M7 runtime proof would plug in (INFERENCE, grounded in the code shape above)
1. **Headless first.** Exit criteria such as NPC navigation through a structure, the seam-straddling path, save round-trips of pieces and damage/repair belong in `tests/Application.Tests` using `Harness`, like M6's C16 test. The roadmap's "navmesh path test including the straddling-seam case" (`docs/ROADMAP.md:285`) is headless by nature once navigation is in the domain (owner ruling 1).
2. **Windowed proof** as a new mode `--build-shots <dir>` modelled on `DeltaShots`, or as appended beats. Wiring:
   - add the option to `ParseArguments` (`Main.cs:1045-1047`);
   - add the profile branch (`Main.cs:89`) and the seed choice (`Main.cs:103`);
   - add a `_Ready` branch that sets `Engine.MaxFps` to the tick rate;
   - add a `_Process` branch returning "done", "failed" or a shot name;
   - include it in the `_UnhandledInput` guard (`Main.cs:342`) and in the one-tick-per-frame condition (`Main.cs:305`).
   Beats should submit the same M7 commands the keys would (placement, rotation, removal, repair), and should show the ghost preview, a refused placement's toast, a piece placed, a door in a player wall opening, the camera compressing inside a player-built room, and an NPC walking in.
3. **Persistence proof:** extend `--playthrough` or add a building segment before its save, so `state_saved.json` and `state_diff.txt` cover pieces. This is automatic if the pieces are in `WorldDelta.TakeSnapshot()`.
4. **Generated table** for reputation fixtures (the TTK pattern).
5. **Performance proof:** a `PerfRun` segment inside or around a player-built structure with N pieces, plus new counters (section 10.6).

---

## 10. Performance

### 10.1 The target and the gate (FACT)
- Bible §34: "**1080p / sustained 60 FPS on RAZER's RTX 4070 Ti in a clean performance window.**". Record CPU frame time, GPU frame time, 1% lows where practical, RAM, VRAM and visible streaming hitches. "Failure of the simple Phase-1 prototype to sustain 60 FPS after reasonable optimization is an owner-review stop." (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:1026-1043`).
- RISK_REGISTER RK-02 Phase-1 gate (owner ruling 2026-09-23): "The baseline machine is RAZER's RTX 4070 Ti at 1080p, measured with OBS, H3 and other significant GPU workloads stopped. The gate is the PROTOTYPE greybox scene at a sustained 60 FPS ... The isolated 2×2 km greybox is captured and recorded too, but it is **not** the formal `D-01` revisit gate in Phase 1" (`docs/RISK_REGISTER.md:88`). Also the precedence doc (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:114-120`) and PROTOTYPE §6.3 "Frame budget ... Weekly" (`docs/PROTOTYPE.md:252`).
- UNTRACKED runtime acceptance plan §16: "Owner provides the clean window. **Agent must not terminate OBS, live streams, H3 renders, or unrelated GPU workloads.**" It adds average FPS and the worst hitch and its location to the record (`PHASE1_RUNTIME_ACCEPTANCE_AND_PLAYTEST_PLAN.md:272-291`).
- Status: **not run.** "Exit (a) and (b) wait on one owner action: a RAZER measurement window" (`docs/M3_STATUS.md:4`). "Still open: the RAZER performance window (M6's 1080p/60 evidence)" (`docs/M6_STATUS.md:221`). The V4 handoff (UNTRACKED) lists "RAZER practical performance evidence passes or is consciously reviewed" in its Phase-1 close checklist and "Only then evaluate Phase 2 / M7" (`OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:1814-1815, 1834-1835, 1883`).
- Hardware: RAZER RTX 4070 Ti 12 GB (V4 UNTRACKED `:1713-1715`). ASTRAL is an RTX 5090 with a Ryzen 9 9950X3D, "not RAZER" (`docs/M3_STATUS.md:61`). ENGINE_VALIDATION: "Do not use only a 5090-class machine" (`docs/ENGINE_VALIDATION.md:41-43`).

### 10.2 The capture method (FACT)
- Owner procedure (`docs/M3_STATUS.md:76-82`):
  1. build the Presentation project;
  2. `godot --path src/Presentation -- --perf --perf-out <dir>/prototype` (about 5 min);
  3. `godot --path src/Presentation -- --spike --perf-out <dir>/spike`.
- `--perf` (`Main.cs:210-219`, `963-979`; `src/Presentation/Perf/PerfRun.cs`):
  - vsync is disabled ("measure the headroom, not the refresh rate");
  - segments: `warmup` 5 s at 3.5 m; `obstruction` (default 75 s, `--perf-seconds`) at camera distance 6 m through the lodge, the smithy and the fence gap, with a full camera orbit every 20 s; `third_person` 75 s at 3.5 m on a loop through all four cells; `first_person` 75 s at 0 m (`PerfRun.cs:51-60, 134-139`);
  - the player walks through the real command path (`SteerWorld`, Run gait) and opens the longhouse door once (`:125-130`);
  - **12 presentation-only stand-in `Avatar` bodies** walk circles, "the prototype's entity budget of 3 NPCs, 1 companion and 8 wolves ... kept as a margin" (`:15, 19, 75-86`);
  - one screenshot per segment halfway through, with the frame after it excluded (`Main.cs:310-314`);
  - the `route` note counts waypoints reached and blows struck and deaths; a clean capture is 0 and 0 (`Main.cs:972`).
- `--spike` (`SpikeScene.cs:12-17, 20-26`):
  - a 401×401 heightmap at 5 m (320,000 triangles);
  - 300 trees (trunk and canopy) and 150 rocks as `MultiMeshInstance3D`;
  - a 60-instance `MultiMesh` capsule crowd, "the tier-A cap";
  - D-06 tier rules over 400 cells at 20 Hz;
  - segments: `navmesh_bake`, `warmup`, `third_person`, `first_person`, `obstruction`.
- Outputs per run: `frames.csv` (`segment,frame,frame_ms,process_ms,render_cpu_ms,render_gpu_ms,static_mb,working_set_mb,vram_mb`, `FrameStats.cs:78`), `summary.json` and PNGs.

### 10.3 `FrameStats` - what is and is not measured (FACT, `src/Presentation/Perf/FrameStats.cs`)
- Per frame: `delta×1000`; `Performance.Monitor.TimeProcess` ms; `RenderingServer.ViewportGetMeasuredRenderTimeCpu/Gpu` (enabled with `ViewportSetMeasureRenderTime`, `:28-32`); `MemoryStatic`; the process working set, read every 30 frames (`:61-62`); `RenderVideoMemUsed` (`:41-72`).
- Per segment summary (`:99-129`): frames; seconds; `average_fps`; `one_percent_low_fps`; `point_one_percent_low_fps`; `minimum_fps`; `frame_ms` {mean, p50, p95, p99, max}; `frames_within_16_7_ms_percent`; `hitches_over_33_ms`; `hitches_over_twice_median`; `process_ms_worst_each_second` (Godot's monitor holds the worst of the last second, so it is kept once a second and never for the second a screenshot falls in; `:41-54`; `docs/M6_STATUS.md:98`); `render_cpu_ms`; `render_gpu_ms`; `static_memory_peak_mb`; `working_set_peak_mb`; `vram_peak_mb`; `sustained_60_fps_by_one_percent_low`.
- Machine block (`:145-159`): host, GPU, vendor, API, CPU, threads, OS, Godot version, renderer, resolution, vsync, UTC.
- **Not measured:** draw calls, objects or primitives in frame, physics, navigation, **simulation tick time** (the ticks run inside `_Process`, so they are folded into `process_ms`), event-bus time, autosave time, and the input-to-event latency from PROTOTYPE §6.3. ENGINE_VALIDATION's list asks for "draw calls; visible object count; navigation cost; streaming hitch duration; initial load; editor responsiveness; import/build times" (`docs/ENGINE_VALIDATION.md:47-60`).

### 10.4 Current measured numbers (all ASTRAL, RTX 5090 - **not the gate**)
- M3 trial (`docs/M3_STATUS.md:63-70`). Prototype third, first and obstruction: average 979/991/998 FPS, 1% low 172/177/184, p99 frame 3.5-3.8 ms, GPU p99 0.16-0.18 ms. Spike: average 928-939, 1% low 204-221, GPU p99 about 0.46 ms, no hitches.
- M6 trial after the route fix (`docs/M6_STATUS.md:85`): "average 798-840 FPS and 1% lows 192-213 FPS per segment, p99 frame 3.5-4.1 ms, GPU p99 0.21 ms, no hitch over 33 ms after the warm-up, VRAM 181 MB, working set 671 MB; the character struck 0 times". The process-time monitor "reads 8-9 ms typically" as the worst frame each second (`:98`).
- HISTORY: `G:/UNNAMED_HISTORY/perf_astral_m6_route3/prototype/summary.json` (captured 2026-09-24 04:54Z, Godot 4.7.2 forward_plus, 1920×1080):

  | Segment | Average FPS | 1% low | Frame p99 | Render CPU mean | GPU mean | Worst process time per second, p50 |
  |---|---|---|---|---|---|---|
  | obstruction | 800.6 | 221.3 | 3.384 ms | 0.352 ms | 0.179 ms | 8.604 ms |
  | third_person | 812.6 | 210.7 | 3.617 ms | 0.344 ms | 0.174 ms | 8.414 ms (p99 39.6 ms; one real 49.5 ms frame) |
  | first_person | 831.6 | 203.7 | 3.881 ms | 0.323 ms | 0.161 ms | 8.269 ms |

  Static memory 78 MB, working set about 670 MB, VRAM 181 MB. The route note: "warmup 0 waypoints, obstruction 35 waypoints, third_person 22 waypoints, first_person 8 waypoints; the character was struck 0 times and died 0 times".
- The same run's spike: average about 982-993 FPS, 1% lows 202-214, GPU mean about 0.45 ms, render CPU about 0.065 ms, VRAM 147 MB, mean tiers A 14.1 / B 118.5 / C 267.4 cells.
- **INFERENCE:** the GPU cost of the greybox is trivial (under 0.5 ms on a 5090), so building pieces are unlikely to threaten 60 FPS on a 4070 Ti through GPU load alone. The risks are CPU-side:
  - node, draw-call and physics-body counts from one `MeshInstance3D` plus `StaticBody3D` per piece;
  - navigation rebuilds;
  - main-thread autosave and serialization as pieces grow (RK-06: "save-size behaviour at hundreds of pieces per cell").
  The "worst process time each second" of about 8-9 ms on ASTRAL is already about half a 16.7 ms frame. Its composition is unmeasured.

### 10.5 Budget allocations (FACT; "budgets to design against", explicitly unmeasured)
- `docs/WORLD_ARCHITECTURE.md:442-460`:
  - target frame 16.6 ms at 60 fps and 1080p;
  - **main-thread world systems ≤ 4 ms**;
  - streaming work ≤ 3 ms per frame, spread over ≥ 4 frames per cell;
  - tier A ≤ 60 actors;
  - tier B ≤ 300 at 2 Hz;
  - tier C ≤ 20k records and ≤ 2 ms per tick aggregate;
  - peak memory ≤ 4 GB total, ≤ 1.5 GB world content;
  - **save main-thread cost ≤ 2 ms P99**;
  - interior load ≤ 500 ms P95.
  - The response order if budgets fail: "reduce radii → reduce caps → reduce AI decision rate → reduce LOD/foliage → *then* reconsider `D-01`" (`:460`).
  - Radii and caps: visual 400 m, collision 120 m, tier A 150 m, tier B 600 m, AI activation 45 m, caps A 60 and B 300, 20 Hz (`:255-265`).
- `docs/PERSISTENCE.md:532-533`: autosave every 5 minutes of playtime; "Autosave main-thread cost **≤ 2 ms P99**". T-25 "Autosave hitch" is a P2 mandatory test (`:599`). **No test measures it** (INFERENCE from a search of `tests/` for T-25, timing and 2 ms). Autosave runs synchronously inside `GameSession.Frame` (`GameSession.cs:194-199`).
- The existing simulation cost test: `SixtyCreatures_TickWithinTheBudget` boots a real session, places 60 creatures of 6 kinds and roles, ticks 400 times with a `Stopwatch`, and asserts `ms < 4` per tick: "The M3 budget leaves the simulation a few milliseconds of a 16.7 ms frame; creature AI must not eat it." (`tests/Application.Tests/CreatureTests.cs:526-541`). **This is the precedent for an M7 headless cost test**, for example the navigation rebuild after one building edit, or the tick cost with N pieces.
- RK-06 validation proposal: "place ~200 snapped pieces, place one companion and one NPC inside the structure, save ... reload, and assert (a) piece count and transforms match, (b) the navmesh rebuild lets both actors path from the doorway to an interior socket, (c) the delta save grows sub-linearly with piece count. All three are automatable headlessly" (`docs/RISK_REGISTER.md:154`). The mitigation includes "navmesh updates batched per building edit rather than per piece; a piece-count ceiling per cell defined by the Phase-1 measurement and treated as a content constraint" (`:158`). **No such measurement exists yet.**
- RK-02 mitigation: "Instrument frame time from the first playable commit ... treat the greybox stress scene as a regression gate that runs as part of the milestone, not a one-off" (`docs/RISK_REGISTER.md:92`).
- Accepted risk: "Command/event indirection cost ... Reopens only if profiling shows a material frame-time cost — and even then the fix is batching the bus, not dissolving the boundary." (`docs/RISK_REGISTER.md:384`).

### 10.6 How to add counters (FACT on API availability; INFERENCE on design)
- Verified in Godot 4.7.2's `GodotSharp.xml` (local NuGet cache `godotsharp/4.7.2`):
  - `Performance.Monitor.RenderTotalDrawCallsInFrame`, `RenderTotalObjectsInFrame`, `RenderTotalPrimitivesInFrame`, `RenderBufferMemUsed`, `RenderTextureMemUsed`;
  - `Physics3DActiveObjects`, `Physics3DCollisionPairs`, `Physics3DIslandCount`, `TimePhysicsProcess`;
  - `ObjectCount`, `ObjectNodeCount`, `ObjectOrphanNodeCount`, `ObjectResourceCount`;
  - `PipelineCompilationsDraw/Mesh/Surface/Specialization/Canvas` (shader-compile hitches);
  - `Navigation*`, `TimeNavigationProcess`;
  - custom monitors: `Performance.AddCustomMonitor`, `GetCustomMonitor`, `HasCustomMonitor`, `RemoveCustomMonitor`, `GetCustomMonitorNames`.
- The extension point is `FrameStats.Record`, which appends to `Sample` (`FrameStats.cs:63-71, 161-162`), plus the CSV header (`:78`) and `Summarise` (`:99-129`). **Draw calls, objects and node count** are the counters most relevant to per-piece rendering.
- **Simulation time:** wrap `_session.Frame(...)` in a `Stopwatch` in `Main._Process` (`Main.cs:305`) and record ms and `FrameResult.TicksRun`. Do **not** time `Simulation.Step()` directly from presentation, because the literal `.Step()` is banned there (`ArchitectureTests.cs:169`). A per-system breakdown needs an Application-side hook, which is a design decision, since `Simulation.Step` is the fixed system order (`Simulation.cs:308-340`).
- A building perf segment in `PerfRun` would need its own route, perhaps a structure placed by commands before the capture starts. The existing route arrays and 12 proxies are hard-coded (`PerfRun.cs:23-37`).

### 10.7 Greybox scale reference for M7 perf (FACT)
- The M6 world has 44 structures and 2 doors ("the 4 cells, a 41 x 41 height grid at 5 m, 44 structures, 2 doors", `docs/M3_STATUS.md:13`, since relaid into the four cells). The lodge and smithy are wall groups under the `longhouse_` and `forge_` prefixes.
- RK-06 talks of "hundreds of pieces placed in one cell" (`docs/RISK_REGISTER.md:152`), about 200 in its validation (`:154`).
- **INFERENCE:** 200 greybox pieces built with the current `Solid` helper would add about 200 `MeshInstance3D`, 200 `StaticBody3D` and 200 shapes, roughly quintupling the scene's structure node count. Options within current conventions:
  - `MultiMeshInstance3D` per piece kind, as the spike does for trees and rocks;
  - one merged `ArrayMesh` per building via `SurfaceTool`, like `BuildTerrain`;
  - one trimesh camera collider per building (`mesh.CreateTrimeshShape()`, as for the terrain at `HollowView.cs:125`).
  Only measurement on RAZER can settle it. The asset standard's LODs cannot help today, because the LOD files carry no material (section 5.2).

---

## 11. Greybox asset conventions for new building pieces

- **Primitive meshes and flat materials (FACT).**
  - `Palette` holds one flat `StandardMaterial3D` per kind of thing, "so the hollow reads at a glance" (`src/Presentation/Greybox/Palette.cs:8-9`). Opaque: Terrain, Wood `(0.42,0.30,0.20)`, Roof `(0.30,0.22,0.16)`, Door `(0.55,0.22,0.16)`, Rock `(0.45,0.45,0.47)`, Trunk, Canopy, Cliff, Skin, Cloth, Leather, Proxy, Metal, Ore, WorkedOut, AshBark, Leaves, Hearth, Iron, Shaft, Fletching and Embers (emissive), with roughness 0.9 by default (`:11-40, 67`).
  - **Translucent and emissive precedents** for a ghost or preview: `Fold` (alpha 0.22 violet, emissive, cull disabled, `:42-50`), `Aligned` (emissive band, `:52-58`), `Water` (alpha 0.75, `:60-65`), and the F3 ring material (alpha 0.35 gold, `HollowView.cs:335`).
  - Shapes used: `BoxMesh` for walls, beams, doors, roofs, containers and ground items; `CylinderMesh` for rocks, trunks, set marks and barriers; `SphereMesh` for canopies; `PlaneMesh` for water; `CapsuleMesh` for spike actors.
- **Units and transforms (FACT).**
  - The domain works in integer millimetres. Presentation converts with `HollowView.ToGodot(xMm, yMm, zMm)` = metres (`HollowView.cs:27`).
  - Facing is in millidegrees from +Z toward +X (`PlayerController.cs:279-286`).
  - The asset standard is metres, glTF Y-up, forward −Z, origin at the footprint centre with the base on the ground plane (`docs/WAVE_0_MODULAR_ASSET_STANDARD.md:66-76`), and modular parts are sized to a declared `nominal_size_m`, "not to the longest axis" (`:78-96`).
- **Every solid carries a camera collider** on layer 1 with mask 0, built by `Solid()`. Scenery the camera should pass through (canopies, hazes, marks) carries none (`HollowView.cs:187, 259-296`).
- **Base placement:** pieces stand on the lowest terrain sample under their footprint, minus 0.2 m, so they never float on slopes (`HollowView.cs:169-170, 386-392`; `Fitting.cs:98-111`).
- **Art binding for pieces (INFERENCE):** today's bindings key on **region blocker IDs** (for example `fence_smithy`, `rock_well`) or ID prefixes (`art_bindings.json:60-86`). Runtime pieces carry ULID instance IDs, so M7 would need bindings keyed on the **piece definition ID** (a new section, for example `"pieces"`), with the same `Fitting` rules (authored size, tile, mismatch → greybox). The kit remains withheld until DeepSeek fixes the swatch textures. DeepSeek owns `assets/`, `tools/asset_pipeline/**` and `docs/WAVE_0_*.md` (`AGENTS.md`).
- **Kit sizes available (FACT, `docs/SCALE_AUDIT_REPORT.md:177-188`, all PASS_SCALE):** beam 3.0 m, door frame 2.44, fence panel 2.4, floor planks 3.0, post 2.6, roof panel 3.0, ruin wall 3.0, step 1.2, stone wall 3.0, timber wall 3.0, well 2.82, window frame 1.1. `building_road_segment` is SUSPECT: measured 4.0 against an expected 3.0 (`:425`). **INFERENCE:** the kit implies a **3.0 m module grid**, which matches the pieces' AABB footprints and 90° rotations. The `building_step` (1.2 m) is an asset only and has no place in a one-storey M7 (owner ruling 2).
- **Asset budgets (FACT):** building module 5-25k triangles, 2-3 LODs, reduction 100/40/15/5 %, collision "box only" (`WAVE_0_MODULAR_ASSET_STANDARD.md:342, 368, 381`). The DeepSeek "mid" tier is 40k faces, 2048 textures and `--lod-faces 12000,4000,1000` (`docs/QUALITY_TIERS.md:41-46`), so it produces heavier pieces than the 25k module budget. "Box only" collision matches the domain's AABB blockers.
- **Where presentation still hard-codes structure semantics (FACT, relevant to player buildings):**
  - roof groups `{"longhouse","forge"}` (`HollowView.cs:205`);
  - door swing inferred from the wall-prefix group (`:250-254`);
  - material by ID prefix `rock_`/`den_rock` (`:383-384`);
  - trees by the `tree_` prefix (`:181`);
  - indoor footsteps by the `building_walls` prefixes (`SoundEvents.cs:62-70`).
  None of this generalises to player-built pieces.

---

## 12. Owner rulings - where recorded (for this topic)

| Ruling | Recorded where (FACT) | Notes |
|---|---|---|
| 1. Godot Navigation not authoritative; deterministic headless navigation in the domain | **Not found** in the repo at `e10d2c4` or in the V4 handoff. A search for "Godot nav", "NavigationServer", "navigation authoritative" and similar phrases returned no doc hits. | The code complies: the only Godot navigation is the spike (section 6). Older docs speak of a "navmesh ... baked per cell" (`WORLD_ARCHITECTURE.md:435`; `ROADMAP.md:284-285`; `RISK_REGISTER.md:154, 286-294`), which should be read as domain navigation or be revised. |
| 2. Building v1 is one storey | **Not found** ("one storey", "single storey", "upper floor"). | `ROADMAP.md:283` lists "foundations, walls, floors, roofs, doors; free rotation and socketing". Floors fit a single storey, but nothing records the one-storey limit. `WORLD_BUILDING_AND_PROPERTY_DESIGN.md:34` lists "stairs" and "lifts" among hidden interior transitions (long-term). |
| 3. Factions (smallest set, no global morality meter ...) | Partly: `ROADMAP.md:282` "**no universal morality meter** (Charter §20)"; V4 UNTRACKED `:459` "no psychic faction omniscience". | Outside this topic. |
| 4. C10 retired | `docs/PROTOTYPE.md:298` "**Revised by owner ruling (2026-09-24)** ... No single encounter is required to force every combat tool"; `docs/M6_STATUS.md:105, 141, 196`. | V4 UNTRACKED `:611-620` still calls it "OPEN ... not yet formally owner-ratified" (a stale snapshot). |
| 5. No core action requires a radial | Bible `:639-670` (formal rule names "building function"); UNTRACKED HUD spec `:17-23`; UNTRACKED V4 `:78-98`; UNTRACKED accessibility draft `:130-138`; `M6_STATUS.md:56`; `Main.cs:1093`. | Soft tension with `HUD_INPUT_AND_ACTIONS.md:30, 36`. |
| 6. No networking in M7 | Bible §36 non-goals include "networking" (`:1090`) for Ashen Hollow; V4 UNTRACKED `:131` "No networking in Phase 0–2", `:1514`. | Presentation-relevant seam: the command/event boundary and D-11 (`DECISIONS.md:232-242`). |
| 7. Engine-independent C#, commands, dotted IDs, ULIDs, sparse deltas | `AGENTS.md`; `DECISIONS.md` D-11 (`:232-242`); the architecture tests (section 1.6); V4 UNTRACKED `:113-131`. | - |

---

## 13. Contradictions found

1. **D-11 "do not poll" vs the code.** `docs/DECISIONS.md:234`: "Views subscribe to domain events; they do not poll and do not own truth." The code reads `Simulation` views **every frame** in `Main.Draw` and the view nodes (`Main.cs:540-637`; `CreaturesView.cs:33-51`; `CraftingView.cs:11-13` "read each frame, never kept"). The "own no truth" half holds; the "do not poll" half does not. INFERENCE: this is an accepted practical reading (immutable per-frame views), but it is undocumented.
2. **"Free rotation" (D-08 and ROADMAP M7) vs AABB-only blockers.** `docs/DECISIONS.md:182` ("free rotation/socketing") and `docs/ROADMAP.md:283` ("free rotation and socketing") conflict with `BoxBlocker(Id, MinXMm, MinZMm, MaxXMm, MaxZMm, HeightMm)`, which has no rotation (`src/Domain/Spatial/Blockers.cs:41`). Presentation (`HollowView.BuildStructure`, `Fitting.Structure`) also assumes axis-aligned boxes and at most 90° model turns (`Fitting.cs:46-47`). Either rotation is quantised to 90°, or the domain gains oriented footprints. The asset standard's "box only" collision (`WAVE_0_MODULAR_ASSET_STANDARD.md:381`) is neutral on orientation.
3. **Navmesh wording vs owner ruling 1.** `WORLD_ARCHITECTURE.md:414, 435`, `ROADMAP.md:284-285` ("the navmesh updates on placement", "navmesh path test") and `RISK_REGISTER.md:146, 154, 158, 286-294` all describe a baked "navmesh", which the only implementation (the Godot spike, `SpikeScene.cs:157-213`) realises with Godot's NavigationServer. Nothing states that engine navigation is authoritative, but none of these docs records the ruling. `ENGINE_VALIDATION.md:31, 56, 86` ("chunked navigation", "navigation cost", "nav compatibility") has the same ambiguity.
4. **Feel test gate.** `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:321` says the acceptance requirements "remain in force, including the 3–5 blind-tester feel test before Phase 2". `docs/ROADMAP.md:274` and `docs/M6_STATUS.md:135` record an owner ruling (2026-09-24): "deferred until a nontechnical Windows playtest build exists; it is **not an M7 entry blocker**". UNTRACKED `PHASE1_RUNTIME_ACCEPTANCE_AND_PLAYTEST_PLAN.md:335-347` and V4 `:1883` still treat it as a pre-Phase-2 gate.
5. **RAZER gate vs starting M7.** V4 UNTRACKED `:20` ("Do **not** begin M7 until Phase-1 consolidation, performance proof, and required playtest work are finished") and bible §35 ("1080p/60 FPS evidence exists" before Phase 2, `:1049, 1069`) sit against `M6_STATUS.md:221` (RAZER still open) and this M7 design effort. The rulings do not say whether the RAZER window gates M7 entry. The feel test was explicitly released; RAZER was not.
6. **HUD_INPUT_AND_ACTIONS §3 radials vs bible §20.** `docs/HUD_INPUT_AND_ACTIONS.md:36`: "Controller users should retain the same underlying capability through context/radials". This is softer than bible §20 ("keyboard/controller parity", "No capability disappears if radial menus are disabled"). It is not a direct contradiction but should be aligned.
7. **Stale csproj path.** `docs/M3_STATUS.md:78` says `dotnet build src/Presentation/Presentation.csproj`. The project was renamed `UNNAMED.Presentation.csproj` (`docs/M6_STATUS.md:211`; `AGENTS.md`; `src/UNNAMED.sln:10`).
8. **`.uid` files.** `AGENTS.md` says "Commit the `.uid` files". At `e10d2c4`, 26 of 42 presentation scripts have **no committed `.uid`**, for example `DeltaShots.cs`, `Playthrough.cs`, `HelpPanel.cs`, `JournalPanel.cs` and all of `Art/` and `Audio/` (FACT from the snapshot listing). The session's git status shows them untracked in `G:/UNNAMED`.
9. **PROTOTYPE §6.3 tests not implemented.**
   - The "Input→command latency" test (`docs/PROTOTYPE.md:253`) does not exist.
   - The "Content validation pass" via `godot --headless --script res://tools/validate_content.cs` (`:249`) is superseded by the `dotnet` content lint in `AGENTS.md`. No `validate_content.cs` script exists; `tools/` holds only DeepSeek's `asset_pipeline` and `godot_validate`.
   - The boot smoke is "Every commit" in PROTOTYPE but not in CI (`M3_STATUS.md:88`).
10. **HUD doctrine vs implementation.**
    - The bible's "Recommended prototype convenience layer: 8 slots" (`:570-589`) and the UNTRACKED HUD spec's 8-slot hotbar: only keys 4-6 exist, and 1-3/7-9 are reply keys only.
    - HUD modes Full/Minimal/Auto-hide/Off (`:686-691`) are not implemented.
    - "Tracker can be disabled" (UNTRACKED spec `:301`) is not implemented.
    - WORLD_ARCHITECTURE's required overlay contents (`:458`: per-actor tiers and reasons, caps headroom, last save time) are not all in F3.
    These are divergences, not errors, for M7 to note.
11. **Acceptance evidence formats.** The code writes and links PNGs (`Main.cs:985`; `Playthrough.cs:168`), but the committed evidence is JPEG with `.jpg` links (`docs/acceptance/m6/transcript.md`). `docs/acceptance/m6_delta` holds 18 stills against the 21 beats the code now runs (`DeltaShots.cs:85-108`; `M6_STATUS.md:219`).
12. **`PROTOTYPE.md` assigns RK-06 to the vertical slice**: "`RK-06` building persistence and navigability ... the prototype must not be blamed for them" (`docs/PROTOTYPE.md:373`). RISK_REGISTER proposes RK-06's validation for "Phase 1 or early Phase 2" (`docs/RISK_REGISTER.md:154`). These are consistent with M7 being Phase 2; the note is only that no Phase-1 piece-count measurement exists to set the "piece-count ceiling per cell" (`:158`).

---

## 14. Open questions (for the M7 designer or owner)

1. **Does the RAZER 1080p/60 window gate M7 entry?** The feel test was released as a blocker; RAZER was not addressed. A building-heavy M7 would be better measured against a known RAZER baseline.
2. **Build-mode input scheme.** Which free keys should it use (B is the obvious build-mode toggle; F, T, Z, the middle mouse and PageUp/PageDown are free)? How is the wheel/zoom collision resolved? How does "place" coexist with LMB = attack? Does Esc mean "cancel build" in build mode? Does R (take-all only in panels) become rotate in build mode? All must be direct keys or menus (bible §20), and all must appear in F1's `Sections`.
3. **Does build mode or the piece catalogue pause the simulation?** Nothing pauses today. The accessibility draft (UNTRACKED) prefers pausing in menus where appropriate. WORLD_ARCHITECTURE allows time scale only for "debug/pause".
4. **Is the placement preview presentation-only or a domain query?** SYSTEMS S-32 lists "Ghost/preview placement, snap candidates" as the building system's *transient* state and `IsValidPlacement` as its interface (`docs/SYSTEMS.md:356-357`). The M6 `Simulation.Aim` precedent suggests a read-only `Simulation` query, added to the architecture allow-list, that runs the same validation as the command handler. Presentation would hold only the cursor pose.
5. **Rotation granularity:** 90° steps (compatible with AABB blockers and presentation) or oriented footprints (a domain `Blocker` change that also touches `Kinematics`, combat traces and prediction)?
6. **The building camera distance bound.** The current `MaxDistance` is 6 m. How much "somewhat farther" is allowed in build mode, and is it exposed only in build mode (`CAMERA_PERSPECTIVE_AND_PRESENTATION.md:266-272, 320-330`)?
7. **Line of sight for prompts and interactions** once player walls exist: `FocusOn` has no occlusion test, and the acceptance plan (UNTRACKED) says prompts must not trigger through walls.
8. **Perf instrumentation scope for M7.** Should M7 add draw-call, object and node counters, simulation-tick timing and autosave timing (T-25) to `FrameStats` and `summary.json`? Should the building proof include a `PerfRun` segment with the RK-06 ~200 pieces? What piece-count ceiling per cell follows?
9. **Rendering strategy for many pieces:** one node per piece (the current `Solid` pattern), `MultiMesh` per kind, or a merged mesh per building. What camera-collider strategy (per piece, or one trimesh per building)?
10. **Art for pieces:** wait for DeepSeek's kit (withheld today) or ship greybox only? Should `art_bindings.json` gain a `pieces` section keyed by definition ID?
11. **Where the authoritative building records live.** If they are outside `CaptureRecord()` and `WorldDelta.TakeSnapshot()`, the `StateDump` field-by-field proof will not see them.
12. **Debug views for M7:** which F-key(s) should host the navigation, building and faction inspectors (F2, F6-F8, F10-F12 are free)? Should F3 grow the WORLD_ARCHITECTURE §12 overlay fields (actor tiers and reasons, caps headroom, last save time)?

---

## Source index (files read)
- Code: `src/Presentation/{project.godot, Main.cs, Main.tscn, UNNAMED.Presentation.csproj, Smoke.cs, Playthrough.cs, DeltaShots.cs, UiShots.cs}`, `Player/{PlayerController,CameraRig}.cs`, `Greybox/{HollowView,ItemsView,CreaturesView,NpcsView,CraftingView,Palette}.cs`, `Perf/{FrameStats,PerfRun}.cs`, `Spike/SpikeScene.cs`, `Ui/{Hud,HelpPanel,JournalPanel,InventoryPanel}.cs`, `Art/{ArtBindings,Fitting}.cs`, `Art/art_bindings.json`; `src/Application/{GameSession,StateDump}.cs`; `src/World/Runtime/{Simulation,Systems}.cs`; `src/Domain/Spatial/{Blockers,Kinematics}.cs` (signatures only); `src/Persistence/SaveStore.cs` (AutosaveCadence); `tests/Architecture.Tests/ArchitectureTests.cs`; `tests/Application.Tests/{Harness,CreatureTests,TtkTableTests,SessionTests}.cs` (parts); `.github/workflows/dotnet.yml`; `src/UNNAMED.sln`.
- Docs: `HUD_INPUT_AND_ACTIONS.md`, `CAMERA_PERSPECTIVE_AND_PRESENTATION.md`, `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` (§9, §19-§26, §34-§36), `M3_STATUS.md`, `M6_STATUS.md`, `QUALITY_TIERS.md`, `SCALE_AUDIT_REPORT.md` (building rows), `RISK_REGISTER.md` (RK-02, RK-06, RK-14, accepted risks), `ENGINE_VALIDATION.md`, `WORLD_ARCHITECTURE.md` (§6, §10-§12), `PERSISTENCE.md` (autosave rows), `SYSTEMS.md` (S-32), `ROADMAP.md` (M6, M7), `DECISIONS.md` (D-08, D-11), `PROTOTYPE.md` (§6.3, C10, §8), `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` (perf, stop point), `PHASE1_ASSET_INTEGRATION.md`, `WAVE_0_MODULAR_ASSET_STANDARD.md` (§2, §7-§9; DeepSeek-owned, read only), `WORLD_BUILDING_AND_PROPERTY_DESIGN.md` (§3, §11), `docs/acceptance/**` (listing and headers).
- UNTRACKED: `G:/UNNAMED/docs/PHASE1_HUD_UI_CONTROLS_AND_FEEDBACK_SPEC.md`, `PHASE1_RUNTIME_ACCEPTANCE_AND_PLAYTEST_PLAN.md`, `OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md` (§0-§4, §17-§18, §50-§53), `DIFFICULTY_ACCESSIBILITY_AND_PLAYER_CUSTOMIZATION_DRAFT.md` (§11-§12, §17, §21).
- HISTORY: `G:/UNNAMED_HISTORY/perf_astral_m6_route3/{prototype,spike}/summary.json`; `OVERNIGHT_REPORT_2026-09-24.md` (grep only).
