## 8. UI and controls

### Decisions

- Build mode is a presentation mode toggled with **B**. Every build action is a named `InputMap` action on a direct key. M7 has no radial and no pointer-driven menu (ruling 5).
- The build keys are **1-7** (choose), **PageDown/PageUp** (cycle), **R** (turn), **left mouse** (place), **Z or Delete** pressed twice (take down), **T** (mend) and **B or Esc** (leave). **E** is unchanged. **Y** asks a worker or releases one, in and out of build mode.
- Build mode suppresses exactly the combat inputs (attack, guard, casts and dodge) and the bow reticle and formula tiles they drive.
- Build mode does not pause the simulation.
- The ghost is advisory. Its colour and words come only from `Simulation.PreviewPlacement`. Left mouse always submits, and a refusal is toasted in words.
- `Main.Words` turns dotted definition IDs into display names in every M7 reason. IDs appear only in the F2 and F6 panels.
- `CameraRig.MaxDistance` stays a `const` 6 m. `BuildMaxDistance = 9f` and an instance `Cap` raise the zoom cap only in build mode (L7).
- There is no player faction screen in M7. There are HUD lines for **reported** standing changes, gates met in the world, F6, and the generated table.
- **F2** shows structures, and a second press adds navigation. **F6** shows factions. Both are DEVELOPER rows in F1.
- There is no compass hint (L9). The build-area outline, drawn in build mode and F2, shows where to build.
- The controller-parity seam is the named action set. No gamepad binding is built.

### 8.1 Principles

1. **Presentation writes nothing, and never decides legality.**
   - Actions submit through the `PlayerController` submitters `Place`, `Dismantle`, `Repair`, `Assign` and `Release`, which scripted runs call too.
   - The ghost reads `PreviewPlacement`.
   - The guards that enforce this are the presentation scan (so no helper is named `Step`), G2, G4 and G25.
2. **Commands carry a pose, never camera state,** so building works at every distance, first person included.
3. **Keys are claimed only while build mode is on,** and only keys that are idle or harmless then. This is the dialogue branch's model for 1-9 (`src/Presentation/Main.cs:374-383`).
4. **Colour is never the only signal** (`docs/HUD_INPUT_AND_ACTIONS.md:117`).
5. **The game code knows family keys, never content IDs.**

### 8.2 Key table (the 17 new actions)

Bindings are added in `Main.DefineInput` (`Main.cs:1061-1104`) with `PhysicalKeycode`: `Key.B`, `Key.Key1`..`Key7`, `Key.Pagedown`, `Key.Pageup`, `Key.R`, `Key.Z`, `Key.Delete`, `Key.T`, `Key.Y`, `Key.F2` and `Key.F6`, all checked in GodotSharp 4.7.2. `build_place` is an `InputEventMouseButton` on the left button, bound like `attack`.

| Action | Key(s) | Active when | Effect / submits | Slice |
|---|---|---|---|---|
| `build_mode` | B | not a scripted run; no inventory, dialogue or character panel | enter or leave; with an empty catalogue, toast "Nothing to build with here" | E5 |
| `build_piece_1`..`_7` | 1-7 | build mode | select the n-th piece (§8.5) | E5 |
| `build_piece_next`, `_prev` | PageDown, PageUp | build mode | cycle every piece, wrapping | E5 |
| `build_rotate` | R | build mode | r := (r + 1) mod 4, clockwise | E5 |
| `build_place` | left mouse | build mode; a pose exists | `PlacePieceCommand(player, defId, x, z, r)` | E5 |
| `build_dismantle` | Z, Delete | build mode; a target exists | the first press arms for 2 s; a second on the same target sends `DismantlePieceCommand` | E5 |
| `build_repair` | T | build mode; a target with blocking parts | `RepairPieceCommand` (no confirmation: refused at full health) | E8 |
| `work_order` | Y | no modal panel; an NPC in focus | `AssignWorkerCommand` / `ReleaseWorkerCommand` (§8.9) | E9 |
| `build_debug` | F2 | not a scripted run | Off → Structures → Structures + Navigation → Off; E1-E4: Off → Navigation → Off | E1, E5 |
| `faction_debug` | F6 | not a scripted run | toggle the faction panel | E3 |

### 8.3 Existing keys while build mode is on

| Binding | In build mode |
|---|---|
| Left mouse `attack` (`Main.cs:386`) | **places**: `swing = captured && !build && attack` |
| Right mouse `guard`, C `dodge` (`:385`, `:403-407`) | suppressed; `Aim` (`:514-538`) is skipped, so no bow reticle or aim line |
| 4-6 `cast_n` (`:387`) | **pieces 4-6**; `SetMagic` gets an empty list, hiding the formula tiles (`:604-606`) |
| 1-9 `reply_n`, R `take_all` (`:374-380`, `:461-462`) | pieces, rotate. They never collide: dialogue and the inventory panel end build mode |
| Mouse wheel (`:350-355`) | zoom 0-9 m (§8.11) |
| E `interact` | unchanged; a panel or conversation it opens ends build mode |
| Esc `release_mouse` (`:489-490`) | **leaves build mode**; the next Esc frees the mouse |
| Tab/I, K; F9 | the panel ends build mode; the load's `Resync` ends it |
| Q, V, WASD, Shift, Ctrl, Space, X, H, G, J, F1-F6 | unchanged |

**Cancel.** Esc (or B) leaves build mode, discarding the ghost and any armed take-down. A placement is one immediate command, so nothing is queued to cancel. Changing the target, or waiting 2 s, disarms a take-down without leaving build mode.

When the unbuilt hotbar arrives (content bible `:570-589`), build mode overrides its digits as dialogue does.

### 8.4 Build mode lifecycle (`src/Presentation/Player/BuildMode.cs`, new)

```
BuildMode                              // presentation only; never saved
  Active; Slot; Rotation               // Slot and Rotation are remembered within a session
  ArmedKey; ArmedUntil
  Asked: (DefId, Pose, Revision, AtSeconds, PlacementPreview)?
  ScriptedAim: (XMm, ZMm)?             // §8.17
  Enter(): capture the mouse; camera.Cap = CameraRig.BuildMaxDistance; show the panel, outlines and crosshair
  Exit():  hide the ghost, panel, outlines and highlight; ArmedKey = null; camera.Cap = CameraRig.MaxDistance
  Select(int); BuildFrame(sim, setup, camera, body, now)
```

- **Exit triggers** are checked at the top of `ReadInput`: B or Esc; the inventory, dialogue or character panel becoming visible; `PlayerDied`; `Resync` (new game or load); a scripted mode taking over.
- **Shown while active:**
  - the build panel;
  - a status line and a target line under the interaction prompt;
  - the ghost and the target highlight;
  - the area outlines (§9.3.4);
  - the crosshair at every distance (today it shows in first person only, `Main.cs:571`).
- **The build panel** is display only, top right at (−380, 290) and 360 px wide, clear of the tracker (`Ui/Hud.cs:87`) and the log (`:130`). It lists the pieces with their keys, names and costs, marks the selection, shows the turn (r × 90°), and has a key legend. `Hud.SetBuild(string? panel, string? status, string? target, BuildTone tone)` draws it.

### 8.5 Piece order and aim

- **Order.**
  - Pieces are sorted by family, in the closed order `pad, wall, doorway, door, roof, storage, station`, then by definition ID (ordinal), over `GameSession.Setup.Building.Catalog`.
  - The M7 keys are: 1 Timber Pad, 2 Timber Wall, 3 Timber Doorway, 4 Timber Door, 5 Timber Roof, 6 Storage Chest, 7 Anvil Bench.
  - PageUp and PageDown reach every piece, including any beyond seven.
- **Aim.** Floats stay in presentation; only the snapped integer pose leaves it.

```
from, dir = camera.Camera.ProjectRayOrigin(centre), ProjectRayNormal(centre)    // Camera3D methods, not physics (G4)
march t = 0..20 m in 0.25 m steps; first t with (from + dir·t).y <= HeightAtMm(x, z)/1000 -> bisect 8 times; no hit -> body + GroundForward·3 m
aimMm = ScriptedAim ?? (round(x·1000), round(z·1000))
pose  = Snapper.Snap(catalog, sim.Pieces.Select(ToPiecePose), defId, aimMm.x, aimMm.z, Rotation)   // Domain, pure
```

`Snap` returns null only for a door with no free doorway within 2 m. The ghost is then hidden, the status line reads "Timber Door: aim at a doorway to hang it", and left mouse does nothing.

### 8.6 The advisory ghost

```
ask  = Asked is null || (def, pose) != Asked.(DefId, Pose) || sim.StructureRevision != Asked.Revision || now - Asked.AtSeconds >= 0.5 s
p    = sim.PreviewPlacement(def.Id, pose.X, pose.Z, pose.R, checkNavigability: ask)   // checks 1-14 every frame; check 15 when asked
if ask: Asked = (def.Id, pose, sim.StructureRevision, now, p)
tone = !p.Allowed                                               -> Refused (p.Reason)
       p.Navigability is NotApplicable or Proven                -> Allowed
       Asked matches (def, pose, revision) && Asked.p.Failed == PlacementRule.Navigability -> Refused (Asked.p.Reason)
       Asked matches && Asked.p.Navigability == Proven          -> Allowed
       otherwise                                                -> Unchecked
```

- **Cost.** Check 15 (≤ 2 ms median, ≤ 10 ms worst) runs only when the definition, pose or revision changes, and otherwise at most every 0.5 s, which picks up moving bodies. The preview writes nothing (G7).
- **By slice.** In E5-E6 check 15 returns `NotChecked` for solids, so they show Unchecked. From E7 the ghost is green or red.

| Tone | Material (§9.3.1) | Status line |
|---|---|---|
| Allowed | `GhostAllowed` | "{Name}: can be built here - {n} {Material} ({carried} carried)", from `p.Cost` |
| Unchecked | `GhostUnchecked` | "{Name}: will be checked when placed - {n} {Material}" |
| Refused | `GhostRefused` | "{Name}: {Words(reason)}", e.g. "Timber Wall: that would cut Kera Voss's work place off" |

### 8.7 Placing, and invalid-placement feedback

- **Left mouse always submits** the ghost's pose, even on red. The authority judges (D-11), and the ghost may be a tick stale.
- **A refusal** arrives as `CommandRejected` and is toasted as `Words(reason)` for 3 s. The status line already shows the same words.
- **Success toasts nothing.** The piece appears when the command drains, and the carried count drops.
- **A new filter in `Main.Subscribe`** (beside `Main.cs:662`) covers the five new commands.
- **`Words`:**

```csharp
static readonly Regex DottedId = new(@"(?<![A-Za-z0-9_.])[a-z][a-z0-9_]*(?:\.[a-z0-9_]+)+(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);
string Words(string reason) => DottedId.Replace(reason, m => _session.DisplayName(m.Value));   // an unknown ID passes through
```

  "needs 2 item.material.timber" becomes "needs 2 Rough Timber". "7.20 m" never matches. `Words` applies to the five new commands and to every M7 status line. Phase-1 toasts are unchanged.
- **Reason-text contract** (for sections 4 and 5):
  - An M7 reason holds words and, at most, dotted definition IDs. It never holds a region-local blocker ID or an instance ID. Check 10 reads "that would build over something already standing there".
  - Some M7 reasons reach the existing raw filters (`Main.cs:662-670`, `:780-784`). These must be words only at their source: a piece-door `InteractCommand`, the walking-errand `TalkCommand`, the billet `BuyCommand` ("Kera Voss will not sell you that"), `MoveItemCommand` or `TakeAllCommand` at a foreign piece chest, and `CraftCommand` at a piece bench.

### 8.8 Target, take down, mend, doors

**Target rule.** This rule is presentation only: it picks what to highlight, and the authority checks reach and ownership.
1. Candidates are the pieces whose bounds lie within `place_reach_mm` of the body.
2. Among pieces with blocking parts, take the first part entered by the segment from the body along `GroundForward`, of reach length. A door is tested by its closed leaf box. Ties: a door beats its doorway, then the lower piece ID wins.
3. Otherwise take the roof, then the pad, on the lattice square containing the aim point.

**Target line:**
- for a blocking piece: "{Name} {health}/{max} - [T] mend   [Z] take down";
- for a pad or a roof: "{Name} - [Z] take down" (L2: no rule changes their health);
- while armed: "Press Z again to take down the {Name}", with the highlight in `GhostRefused`.

The arm clears after 2 s or when the target changes. Refund and repair costs are not previewed, because that would copy the authority's rounding.

**Doors (E6).**
- E works a door in and out of build mode.
- `PlayerController.FocusOn` adds piece doors, measured to the closed leaf box as authored doors are (`PlayerController.cs:186-190`).
- `Describe` names a `pce_` key by its piece's `DefId`, so the prompt reads "[E] Open the Timber Door".
- `Resync` fills `_open` from `PieceView.DoorOpen` (`:56-58`).
- A close refused while a body stands in the leaf is toasted by the existing `InteractCommand` filter.

### 8.9 Assigning and releasing the worker (Y, E9)

```
if focus is not an NPC: toast "No one to ask"; return
npc = focus.Key
if sim.WorkAssignments has npc with phase ToWork or AtWork: controller.Release(npc); return
station = sim.Pieces.Where(family station, Owner == player, WorkerNpcId == null)
            .OrderBy(kind ∈ setup.Social.Npcs[npc].WorksAt ? 0 : 1).ThenBy(distance to body).ThenBy(id).FirstOrDefault()
station is null ? toast "You have no workplace for anyone to work at" : controller.Assign(npc, station.Id)
```

- **Presentation only chooses what to name.** It submits even on a kind mismatch, so the authority's refusal explains itself (for example "Kera Voss does not work an anvil").
- **The prompt suffix** (`Main.cs:582-583`) reads "[Y] Let Kera Voss go home" while she works for you, or "[Y] Ask Kera Voss to work at your Anvil Bench" when a matching owned, unmanned station exists. NPCs without `works_at` and downed companions get no suffix.
- **Asking is in person.** Focus uses talk reach, which is assign refusal 4.
- **At work, Kera talks and trades normally,** but does not turn to the player. Only the mover writes an errand NPC's facing (G21).
- **While she walks, talking to her is refused** ("Kera Voss is walking to work"). The existing `TalkCommand` filter toasts the refusal.

### 8.10 Event feedback

| Event | HUD |
|---|---|
| `PiecePlaced` | none; `StructuresView.Sync` draws it |
| `PieceRemoved` | toast "Took down the {name}: +{n} {material}" per refund line |
| `PieceDestroyed` | log "The {name} is destroyed", plus "; what it held lies on the ground" for storage |
| `PieceDamaged` (melee only) | log "You strike the {name} ({health}/{max})". It replaces that swing's `AttackMissed` |
| `PieceRepaired` | toast "Mended the {name}" |
| `DoorToggled` with a `pce_` key | `StructuresView.SetDoor` and `_open` update; one call is added to the handler at `Main.cs:642-646` |
| `WorkerAssigned`, `WorkerReleased` | toasts "{npc} will work at your {name}", and "{npc} heads home", plus " (the {name} is gone)" when dismantled or destroyed |
| `NpcArrivedAtWork`, `NpcReturnedHome` | log "{npc} is at your {name}", "{npc} is home" |
| `StructuresChanged`, `NavigationRebuilt`, `RoutePlanned` | F2 only |
| `ActRecorded`, `FactionLearned` | F6 only |
| `ReputationChanged` with `Source == "reported"` | log line (§8.13). Other sources stay silent; none exists in M7, and M9's witnessed changes stay silent by default |
| `CommandRejected`, the five new commands | toast `Words(reason)` |

Names come from `GameSession.DisplayName` (`src/Application/GameSession.cs:162`), through a piece's `DefId`, never its ID.

### 8.11 Camera limits

- **`CameraRig` changes:**
  - `const MaxDistance = 6f` stays (`CameraRig.cs:23`), because `src/Presentation/Perf/PerfRun.cs:56` reads it statically;
  - it adds `public const float BuildMaxDistance = 9f` and `public float Cap { get; set; } = MaxDistance`;
  - `Zoom` (`:85`) clamps to `Cap`;
  - setting `Cap` clamps `TargetDistance` and the first-person restore distance.
- **Entering and leaving.** Entering build mode does not move the camera, since manual zoom overrides (`docs/CAMERA_PERSPECTIVE_AND_PRESENTATION.md:320-332`). Leaving clamps it to 6 m, and the existing lerp eases it back.
- **Bounds.** The building doctrine's bound holds (`:266-272`): at 9 m and the steepest pitch the camera sits about 8.8 m above the eye.
- **Unchanged:** the pitch clamp; the layer-1 spring arm, which compresses under a player roof; no free or top-down camera.

### 8.12 No pause

Nothing pauses today: `_session.Frame` runs every frame (`Main.cs:305`), and time scale is "debug/pause only" (`docs/WORLD_ARCHITECTURE.md:249`). Build mode is not a menu, and the proof needs Kera and Tavar moving while the player builds. The build area is spawner-protected (BLD007).

### 8.13 Reputation presentation: no faction screen in M7

**What the player sees:**
1. **A HUD log line for each reported change.** On `ReputationChanged` with `Source == "reported"`, the line reads "{Faction}: {TierTo} ({±(To − From)}), told to {DisplayName(Via)}", for example "The Survey: neutral (-100), told to Sel Arien".
   - It uses the relationship line's signed style (`Main.cs:778-779`).
   - The faction name is its content `name` with the first letter capitalised.
   - The tier word arrives in the event, so presentation derives nothing (G2).
2. **The gates, met in the world:**
   - Sel's `notes` reply is offered or hidden;
   - Kera's billets are listed or omitted;
   - a raw buy is refused with "Kera Voss will not sell you that".
3. **F6 and `docs/M7_REPUTATION_TABLE.md`** serve the developer and the owner.

**Why there is no screen:**
- **The ROADMAP needs none.** Its proof is "a reputation fixture table", and its exit is met "in a fixture" (`docs/ROADMAP.md:284-285`).
- **A true-standing screen would show what the character cannot know** (ruling 3). Every M7 change is a report the character made in person, so the HUD line carries exactly their knowledge.
- **The charter's "factions" interface** (`docs/PROJECT_CHARTER.md:985`) belongs to the vertical slice. There, reputation becomes a choice between two opposed factions (`docs/VERTICAL_SLICE.md:94`, `:167-169`), and M9 builds a knowledge-limited screen with tier words and no points.
- **A screen would invite tier logic into presentation,** which G2 fails.

### 8.14 Debug views

Both panels follow `QuestDebugPanel` (`src/Presentation/Ui/JournalPanel.cs:76-100`): a `CanvasLayer` with a `PanelContainer` at (40, 130), 1100 wide, plain `Label` text, redrawn at most 4 times a second. Both are DEVELOPER keys in the playtest build, like F3 and F4, and may show IDs.

**F2, stage 1: Structures (E5; `Ui/StructureDebugPanel.cs`).**
- **Panel:**
  - `StructureRevision`, the piece count, each area's count against `max_pieces`, and `StructureAudit`;
  - the last `StructuresChanged`;
  - the ghost line from `BuildMode.Asked`: verdict, failed rule, reason, tick asked, and the preview's wall time measured in presentation;
  - the target: ID, definition, slot key (Domain `Lattice`), pose, owner and health;
  - the pieces within 12 m;
  - the `WorkAssignments` rows.
- **World markers:**
  - provider sockets as 0.15 m cubes (edge green, square blue, door orange), placed from the definition and `QuarterTurn`;
  - `Label3D` ID tails over the pieces within 12 m;
  - work-anchor discs with a facing tick;
  - the area outlines;
  - the last change rectangle.

**F2, stage 2: + Navigation (E1 builds it alone; `Greybox/NavigationOverlay.cs`).**
- **Unwalkable nodes.** Person-unwalkable nodes within 24 m, from `Simulation.Navigation.Grid.Walkable`, drawn as a `MultiMesh` of 0.25 m quads at terrain + 0.05 m. They are rebuilt on `NavigationRebuilt`, after a 2 m move, and at most twice a second.
- **Gates** from `Navigation.Gates`: red when closed, green when open, read at draw time.
- **Routes** from `Navigation.Movers` (the companion from E4, errands from E9), drawn as `ImmediateMesh` polylines at +0.3 m, with the trail marks.
- **Panel additions:**
  - per mover: status, goal, corners, planned tick and `Blocked`;
  - the last `RoutePlanned` and `NavigationRebuilt`;
  - the grid digest;
  - `NavCounters` as section 3's view gives them. They are read only here and in tests.

The two stages keep D31's one key without up to 37k quads burying the pieces.

**F6: Factions (E3; `Ui/FactionDebugPanel.cs`).** An illustrative excerpt, after the playthrough's faction beats (act #2's position is the player's body, 1.8 m behind the armour's post, because an act records the actor's body):

```
the Waystation  faction.ashen_hollow.waystation   100  accepted (1)  seat location.outpost  members Renn Vale, Kera Voss  the Survey: cordial
the Survey      faction.ashen_hollow.survey          0  neutral (0)   seat location.outpost  members Sel Arien  the Waystation: cordial
ACTS 2 / 256
 #2 creature_killed creature.construct.animated_armour  r_0_0:c_00_00 (63.3, 33.4) tick 9120
    the Waystation  reported via npc.ashen_hollow.kera_voss  identified  +100
    the Survey      reported via npc.ashen_hollow.sel_arien  identified  -100
GATES  merchant.ashen_hollow.kera_voss  item.material.iron_ingot  requires faction.ashen_hollow.waystation level >= 1  -> on offer: 3
```

- **Sources.**
  - Factions come from `Simulation.Factions` (`FactionView`); acts and their knowledge rows come from `Simulation.Acts` (`ActView.Known`, section 5).
  - The source and identity columns are the M9 seam; in M7 they always read `reported` and `identified`.
  - A ring buffer of the last 8 faction events is kept for display only.
- **The service gate.**
  - It is read from the allow-listed `Simulation.Wares(npcId)`.
  - The requirement is the raw `StandingRequirement.MinLevel`, and a level is never compared with it.
  - It is labelled "on offer", because a sold-out row also shows 0.
- **The dialogue gate is not evaluated** (G2). It is observed in conversation, and F4's `DescribeCondition` shows its requirement.

F3 is unchanged. F7, F8 and F10-F12 stay free.

### 8.15 F1 (HelpPanel) additions

`HelpPanel.Sections` gains **BUILDING** after ACTING, and `LeftSections` goes from 3 to 4 (`src/Presentation/Ui/HelpPanel.cs:14-40`, `:48`). Keys are read from the input map, as today.

| Row (DRAFT text) | Actions | Shown as |
|---|---|---|
| Build mode, on or off | `build_mode` | B |
| Choose a piece | `build_piece_1`, `build_piece_7` | 1 - 7 |
| Next or previous piece | `build_piece_next`, `build_piece_prev` | PageDown PageUp |
| Turn the piece | `build_rotate` | R |
| Place it (a room needs a doorway) | `build_place` | Left mouse |
| Take down what you face (press twice) | `build_dismantle` | Z or Delete |
| Mend what you face | `build_repair` | T |
| Cancel: leave build mode, dropping the ghost and any armed take-down | `release_mouse` | Escape |
| Ask someone to work at your bench, or let them go | `work_order` | Y |

`Developer` (`:42-45`) gains "Structure debug (again: the navigation grid)" on F2 and "Faction debug" on F6.

The left column becomes 30 rows and the right 20. The b03 still verifies the fit at 1080p. Rows land with their actions: F2 in E1, F6 in E3, the build rows in E5 except Mend, Mend in E8, and Y in E9.

### 8.16 Controller parity; no radial

- Every build action is a named action, and nothing polls a raw key. A gamepad pass later adds joypad events to the same actions.
- No action needs a pointer. The aim is the camera centre, and PageUp/PageDown reach every piece without digits.
- No radial exists. A later optional radial may only mirror these actions (content bible `:645`). E0 softens `docs/HUD_INPUT_AND_ACTIONS.md:36` accordingly.

### 8.17 Scripted runs (`--build-shots`)

- `ReadInput` and `_UnhandledInput` do not run in scripted modes (`Main.cs:297-300`, `:342`), so beats never press keys.
- `BuildMode` exposes `Enter()`, `Select(int)` and `ScriptedAim`, on the `UiShots.Viewpoint` precedent (`Main.cs:567`). Beats submit through the same `PlayerController` submitters.
- `--build-shots` joins the `ParseArguments` value list (`:1045-1047`), the scratch-profile branch (`:88-91`), the seed (`:103`), one tick per frame (`:305`) and the input guard (`:342`). Section 13 owns its start save, beats and verify mode.

### 8.18 Presentation change map

- **`Main.cs`:**
  - the 17 actions;
  - the build branch in `ReadInput`;
  - `BuildFrame` and the Y prompt in `Draw`;
  - §8.10's subscriptions;
  - `Resync` syncs `StructuresView` and ends build mode;
  - `OpenStation` (`:1003-1007`) reads `simulation.Stations`;
  - `Describe` (`:1021-1031`) names `pce_`, `container.pce_…` and `station.pce_…` keys by their `DefId`;
  - `KeepContainerInReach` (`:498-507`) closes the panel of a station that has vanished;
  - the `--build-shots` wiring.
- **`Player/PlayerController.cs`:**
  - `Predict` passes `simulation.Space` (`:127`);
  - `FocusOn` adds piece doors and reads `simulation.Stations` (`:202-203`);
  - the five submitters.
- **`Player/CameraRig.cs`** (§8.11) and **`Ui/HelpPanel.cs`** (§8.15).
- **`Ui/Hud.cs`:** `SetBuild`.
- **`Greybox/Palette.cs`:** five additive materials.
- **New files:**
  - `Player/BuildMode.cs`;
  - `Greybox/StructuresView.cs`;
  - `Greybox/NavigationOverlay.cs`;
  - `Ui/StructureDebugPanel.cs`;
  - `Ui/FactionDebugPanel.cs`;
  - `BuildShots.cs`.
- **Untouched:** `Art/**`, `art_bindings.json`, `Audio/**`, `HollowView`, `CraftingView`, `ItemsView` and `NpcsView` (§9.1).

### Not in M7 (section 8)

| Item | Belongs to |
|---|---|
| A knowledge-limited player faction screen | M9 |
| The production building UI. It needs a categorized, searchable catalogue with thumbnails (charter "building interface", `docs/PROJECT_CHARTER.md:987`); cost, shortfall and refund previews served by an authority read; wall runs, blueprints and multi-placement; non-90° steps if Q1 asks for them; a structure panel; per-activity camera memory; and `pieces` art bindings and building audio | M9 at the earliest, with the art kit |
| Gamepad bindings, rebinding, hold/toggle options, UI scale | the controls and accessibility pass (unscheduled) |
| An optional radial mirroring the build keys | unscheduled; never required |
| A compass hint to the build area | dropped (L9); revisit with wider areas (Q4) |
| Worker management beyond one Y assignment | M10 |
| Pause or slow time in build mode | not planned |

### Tests owned by this section

| Test | Project | What it proves |
|---|---|---|
| `EveryM7Action_IsBoundToADirectKey` | Architecture.Tests | A source scan of `Main.DefineInput`: every M7 action is bound to a key or mouse button, and none needs a radial (ruling 5). It lands in E5 with `build_debug` (E1) and `faction_debug` (E3); E8 adds `build_repair`; E9 adds `work_order` |
| `--build-shots` b03, b08, b09, b11-b15, b17 | Godot runtime | These beats check, in turn: the outline and a green ghost; the F1 BUILDING fit; refusals in words, on the status line and in the toast (no recorded toast or status line holds a dotted or instance ID); E on a piece door; the target line and T; Y with its toasts; F2 stage 2 showing Kera's route. From b01 on, the camera never passes a piece wall |
| `--playthrough` beat `m7_tell_sel_armour` | Godot runtime | The line "The Survey: neutral (-100), told to Sel Arien", and the F6 still |
| `--ui-shots`, `--delta-shots`, `--smoke`, `--quit-after 300` | Godot runtime | No Phase-1 presentation regression |

Section 6's G2, G4 and G25 guards, and the `PreviewPlacement` allow-list entry, bind this section too.

---

## 9. Minimal content and asset requirements

### Decisions

- **M7 needs no asset files.** Every visual is greybox built at run time from views and content dimensions. No work goes to DeepSeek or the asset-remediation agent.
- **`art_bindings.json` gets no `pieces` section in M7.** Its future shape is recorded only.
- **Factions get no visuals.**
- **Timber comes from an authored container** (L3): `container.timber_stack` at (84, 118), filled by the one-shot `loot.timber_stack` (80 timber). No node or resource is added, so M7 has **no baseline transition**.
- **`config.building.damage` holds only `melee`** (L2). **`config.factions` has no `witness` block** (L1). **`config.navigation` ships only the `person` class** (L9).
- **The content is 14 new definitions plus edits to 7 existing files**, landing with slices E1, E3, E5, E6, E8 and E9.

### 9.1 Rules for M7 visuals

1. **Greybox is the deliverable.** Pieces are drawn from `PieceView` parts and bounds in domain millimetres, and no drawing code names a content ID.
2. **No asset backlog is opened.**
   - The building kit is withheld (`src/Presentation/Art/art_bindings.json:25-31`).
   - The chest and anvil models are withheld as tilted (`:46-47`).
   - Art bindings touch no rule, save or content hash (`ArtBindings.cs:29-30`), so piece definitions carry no asset field.
3. **M7 does not touch the asset-remediation branch's files.** It leaves `src/Presentation/Art/**`, `Audio/**`, `HollowView`, `CraftingView`, `ItemsView` and `NpcsView` unchanged. The helpers it needs are copied (§9.3.2).

### 9.2 The three lists

**REQUIRED TO FUNCTION: no asset file.** Everything is code-built greybox:

- R1: `StructuresView` builders for the seven pieces (§9.3). Slices E5, E6 and E8.
- R2: a hinged door leaf whose camera collider turns with it, on the `HollowView.BuildDoor` pattern (`HollowView.cs:219-257`). E6.
- R3: the pad drape over `HeightAtMm` (ruling 2).
- R4: camera colliders on solids, lintels and roofs: `StaticBody3D`, layer 1, mask 0 (`HollowView.cs:18`, `:374-381`).
- R5: three ghost materials and a `MaterialOverlay` highlight.
- R6: the area outline.
- R7: the F2 primitives (`MultiMeshInstance3D`, `ImmediateMesh`, `Label3D`). E1 and E5.
- R8: existing looks, unchanged.
  - Kera and Tavar are drawn by `NpcsView`.
  - Timber on the ground is the `ItemsView` crate.
  - `ItemsView.BuildContainers` draws the stack as the authored-container greybox chest, with no code change. `Describe` gives its prompt: "[E] Open the timber stack".
  - Timber's inventory row is text only.

**NICE FOR PRESENTATION.** Code only; none of these gates the exit:
- N1: interpolate a walking errand NPC between ticks (`NpcsView` follows the body, `NpcsView.cs:44`);
- N2: a damage tint by health band;
- N3 and N4: indoor audio in player rooms, and place and take-down sounds. Both are deferred because `SoundEvents` (`SoundEvents.cs:62-70`) is in the remediation area;
- N5: one merged mesh and collider per structure, only if a section 14 measurement demands it.

**USE GREYBOX FOR NOW.** Nobody is asked for these:

| Item | Why |
|---|---|
| Kit art (`building_wall_timber`, `building_roof_panel`, `building_floor_planks`) | the kit is withheld |
| The door-leaf model on piece doors | authored doors bind by door key (`art_bindings.json:92-95`); a piece door's key is an instance ID |
| Chest and anvil models | withheld as tilted |
| A timber-pile look for the stack | it needs a `containers` binding, and M7 leaves `art_bindings.json` alone |
| A timber icon | only 17 icon keys are bound, and none is a material (`:172-190`) |
| Any faction visual | §9.5 |

### 9.3 The greybox kit (`src/Presentation/Greybox/StructuresView.cs`, new)

#### 9.3.1 Dimensions and materials (local mm at r0)

| Piece | Blocking parts | Drawn as | Material | Camera collider |
|---|---|---|---|---|
| `piece.pad.timber` | none | a drape over [−1500, 1500]² at terrain + 20 mm, with a 150 mm skirt | `Palette.Shaft` | none |
| `piece.wall.timber` | [−1700, −200, 1700, 200], h 3000 | box; base `LowestUnder(part)` − 0.2 m; 3.2 m tall | `Palette.Wood` | yes |
| `piece.doorway.timber` | jambs [−1700, −200, −800, 200], [800, −200, 1700, 200], h 3000 | two jambs, plus a drawn lintel [−800, −200, 800, 200] at +2.4 to +3.0 m | `Palette.Wood` | yes, lintel included |
| `piece.door.timber` | leaf [−800, −200, 800, 200], h 2400, blocking while closed | a 1.6 × 2.4 × 0.4 m leaf on a hinge, ±90° | `Palette.Door` | yes, turning |
| `piece.roof.timber` | none | a 3.0 × 0.2 × 3.0 m slab, bottom at the square's `LowestUnder` + 3.0 m | `Palette.Roof` | yes |
| `piece.storage.chest` | [−500, 600, 500, 1200], h 700 | box; base −0.2 m; top +0.7 m | `Palette.Leather` | yes |
| `piece.station.anvil` | [−500, 400, 500, 1000], h 900 | wood box, plus a 0.62 × 0.24 × 0.26 m iron block on top (no collider) | `Palette.Wood`, `Palette.Iron` | the box only |

New `Palette` materials, all additive. Each is translucent (`TransparencyEnum.Alpha`), emissive and cull-disabled, and the nodes wearing them cast no shadow:

| Name | Albedo RGBA | Use |
|---|---|---|
| `GhostAllowed` | (0.35, 0.85, 0.45, 0.35) | ghost Allowed |
| `GhostUnchecked` | (0.95, 0.75, 0.25, 0.35) | ghost Unchecked |
| `GhostRefused` | (0.95, 0.30, 0.25, 0.35) | ghost Refused; armed highlight |
| `Highlight` | (1.00, 0.95, 0.70, 0.25) | the target |
| `AreaOutline` | (1.00, 0.85, 0.20, 0.35) | area outline (the F3 ring colour, `HollowView.cs:335`) |

#### 9.3.2 The builder

```
StructuresView : Node3D                       // holds no truth; dictionaries keyed by the pce_ value (ordinal)
  Sync(Simulation s)            // add every s.Pieces ID not built; free every built ID not in s.Pieces; idempotent
  SetDoor(string key, bool open)  // unknown keys ignored, as HollowView.SetDoor (:56-60)
  Highlight(string? key, Material? overlay);  ShowOutlines(bool)
  static Node3D BuildPiece(PieceView p, TerrainGrid t, Material? ghost)   // one builder for pieces and the ghost
     pad  -> Drape(bounds)          roof -> Slab(bounds, LowestUnder(bounds) + 3.0, 0.2)
     door -> Hinge(p.Parts[0], p.Rotation)     otherwise -> Box(part) per part; doorway + Lintel; station + iron block
     colliders on StaticBody3D{layer 1, mask 0}, omitted when ghost != null
```

- **The pattern.** It follows `HollowView.BuildStructure` (`HollowView.cs:162-200`), with two differences: it runs at any time, and it rotates no node but a door hinge. `PieceView.Parts` are world-axis-aligned integer boxes.
- **Copied helpers.** `Solid` and `LowestUnder` are copied from `HollowView.cs:374-381` and `:386-392`, so `HollowView` stays byte-identical.
- **When it runs.** `Sync` runs in `Resync` and on every `PiecePlaced`, `PieceRemoved` and `PieceDestroyed`, all of which are published after the commit.
- **The ghost** is rebuilt only when (definition, pose) changes. It gets `CastShadow = Off`, and its material is swapped every frame.

#### 9.3.3 The door hinge

- **Position and directions.** The hinge sits at R_r(−800, 0) from the anchor, using the domain's `QuarterTurn`. The closed direction is R_r(+1, 0), and the open direction is R_r(0, +1), the piece's local +Z.
- **The swing angle.** θ ∈ {±90°} is computed so that the closed direction turned by θ equals the open direction, under `HollowView`'s convention (+90° about Y turns +Z to +X, `HollowView.cs:253`). At r0, θ = −90°. θ is never hard-coded per rotation.
- **State.** The initial state comes from `PieceView.DoorOpen`, and later states from `DoorToggled`.

#### 9.3.4 The pad drape and the area outline

- **Drape.** A 13 × 13 vertex grid at 250 mm, the lattice the relief check samples. Each vertex has y = `HeightAtMm(x, z)` / 1000 + 0.02. A 0.15 m skirt hangs from the edge, and normals are generated. Feet neither float nor sink, because `HeightAtMm` is what the body stands on.
- **Outline.** Each `RegionLayout.BuildAreas` box is drawn as four 0.1 m strips, sampled every 1 m at terrain + 0.03 m. It shows only in build mode and F2. Normal play draws no markers (`HollowView.cs:331`).

#### 9.3.5 Node budget

- **Nodes per piece:** a wall, roof or chest 3; a doorway 9; a door or bench 4; a pad 1.
- **Totals:** RK-06's 200-piece set (81 pads, 81 roofs, 37 walls, 1 doorway; section 4) is 444 nodes, 202 meshes and 121 colliders. The full 256-piece area layout is about 705 nodes (section 14).
- **Measurement:** only in the optional `--perf-world` run (L4). N5 is the lever if the measurement calls for it.

### 9.4 `art_bindings.json`: no `pieces` section in M7

- **Nothing can be bound yet.** The kit, the chest and the anvil are all withheld, and a loader property would sit in the remediation area (`ArtBindings.Load`).
- **The seam is the builder's shape.** Each part is built by one method, so a later `ArtPiece(PieceView, part)` can wrap it as `HollowView.ArtStructure` wraps `BuildStructure` (`HollowView.cs:135-160`), keeping the greybox collider.
- **The future shape is recorded, not written.** It is a top-level `"pieces"` object keyed by the **definition** ID (never a `pce_` value), whose values are `Placement` looks. For example: `"piece.wall.timber": { "model": "building_wall_timber", "fit": "tile", "material": "wood" }`.

### 9.5 Factions need no visuals

- **A faction-coloured world breaks ruling 3.** Banners, tints or colour-coded NPCs would show standing as a world property that everyone can see.
- **There is nothing to draw.** Membership is static content naming three existing NPCs, and there is no territory, no guard and no war state.
- **The proof is textual:** the generated table, the HUD line, the gates met in play, and F6 (§8.13-§8.14).

### 9.6 New definitions (14)

Each file is the ID's later segments, under its kind's directory: `config/navigation.yaml`, `factions/ashen_hollow/survey.yaml`, `pieces/wall/timber.yaml`, `items/material/timber.yaml`, `loot/timber_stack.yaml`. Names are DRAFT, and faction names are working names, not canon.

| ID | Contents | Slice | Lints |
|---|---|---|---|
| `config.navigation` | section 3's keys, without `work_anchor_max_m`; `classes: [{ id: person, radius_m: 0.35 }]` only | E1 | NAV001-NAV007 |
| `config.factions` | §9.8 | E3 | FAC001 |
| `faction.ashen_hollow.waystation` | "the Waystation"; seat `location.outpost`; `creature_killed creature.construct.animated_armour` +100; `cordial` to the Survey | E3 | FAC001 |
| `faction.ashen_hollow.survey` | "the Survey"; seat `location.outpost`; `switch_set world.foldscar.steadied` +100; the same kill −100; `cordial` to the Waystation | E3 | FAC001 |
| `config.building` | §9.8 | E5 | BLD006 |
| `piece.pad.timber` | "Timber Pad"; 1 timber; `health_max` 200 (stored; no M7 rule changes it) | E5 | BLD001-BLD004 |
| `piece.wall.timber` | "Timber Wall"; 2 timber; 200; `supports_roof` | E5 | same |
| `piece.doorway.timber` | "Timber Doorway"; 2 timber; 200; `door` provider; `supports_roof` | E5 | same |
| `piece.roof.timber` | "Timber Roof"; 1 timber; 100 (stored; no M7 rule changes it) | E5 | same |
| `item.material.timber`, `loot.timber_stack` | §9.8 | E5 | ITM001, SEM, XREF |
| `piece.door.timber` | "Timber Door"; 1 timber; 120 | E6 | BLD001-BLD004 |
| `piece.storage.chest` | "Storage Chest"; 2 timber; 100; `container: { stack_slots: 12, at_m: [0, 0.9] }` | E8 | BLD001-BLD005 |
| `piece.station.anvil` | "Anvil Bench"; 4 timber; 300; `station: { kind: anvil, work_anchor_m: [0, -0.25], work_facing_deg: 0 }` | E8 | BLD001-BLD005 |

- Piece geometry (parts, bounds, sockets, rotations) is section 4's catalogue.
- The Crossing Workshop spends 31 timber. The stack's 80 covers that scenario (its start carries 45), plus repairs and experiments.

### 9.7 Edits to existing content (7 files)

| Where | Edit | Slice | Lints |
|---|---|---|---|
| `content/regions/ashen_hollow.yaml`, `containers:` (after `:149`) | `- { key: container.timber_stack, loot_ref: loot.timber_stack, position_m: [84, 118], stack_slots: 12 }`. It sits in cell `r_0_0:c_00_01`, 5.0 m from the area corner (87, 114). It is layout, so it needs no transition | E5 | WLD009; NAV006 (if the point is refused, move it, never relax the lint) |
| same file, a new `build_areas:` block after `stations:` (`:161-163`) | `- { key: build_area.hollow_crossing, box_m: [87, 87, 114, 114], max_pieces: 256 }` | E5 | WLD015, BLD007, BLD009; BLD008 (E7) |
| `content/npcs/ashen_hollow/kera_voss.yaml` (after `:12`) | `faction_ref: faction.ashen_hollow.waystation` (E3); `works_at: [anvil, forge]` (E9) | E3, E9 | FAC-M1; BLD005 (recipe-used kinds; area corners within 85 m of her site per axis) |
| `…/renn_vale.yaml`, `…/sel_arien.yaml` | `faction_ref` Waystation and Survey. `tavar_orr.yaml` gets none (FAC-M2) | E3 | FAC-M1, FAC-M2 |
| `content/merchants/ashen_hollow/kera_voss.yaml`, appended after `:11` | `- { item_ref: item.material.iron_ingot, count: 3, price_bias: 1.0, requires: { faction_ref: faction.ashen_hollow.waystation, min_tier: accepted } }`. Existing refs `#00`-`#04` (three arrow stacks, vest, cap) keep their indices; the billets are `#05` while untouched. Tests read refs from the stock index | E3 | FAC001 (R5; a gated item in one row) |
| `content/dialogue/ashen_hollow/kera_voss.yaml`, node `again` (`:28`) | reply `armour` before `trade` (`:61`); node `armour_down` | E3 | FAC001 R3, R4 |
| `content/dialogue/ashen_hollow/sel_arien.yaml`, node `again` (`:30`) | replies `notes` and `armour`; nodes `notes` and `armour_down`; `report_act` on both `tavar_back` replies (`:21`, `:57`) after their relationship event | E3 | FAC001 R1, R3, R4 |

The validator order, after `QuestContent` (`src/Content/ContentLoader.cs:195`), is `NavigationContent`, then `BuildingContent`, then `FactionContent`.

### 9.8 YAML for the new and changed content

Each file carries the standard header (`id`, `kind`, `schema: 1`, `display_key: <id>.name`, `tags: [<kind>]`), which is omitted below.

```yaml
# config.building (E5)
notes: Building v1 (M7). Socket/snap on a 3 m lattice, quarter turns, one storey (D-08; owner rulings 1-2).
module_m: 3.0                # save-locked; a whole multiple of config.navigation node_m
rotation_step_deg: 90        # quarter turns only (owner question 1)
place_reach_m: 6.0
pad_max_relief_m: 0.25
refund_percent: 50
repair_cost_percent: 100
protection: { spawn_point_m: 3.0, npc_site_m: 1.5, site_m: 1.5, structure_margin_m: 1.5, spawner_margin_m: 1.0 }
damage: { melee: 10 }        # the only M7 damage source; BLD006 requires exactly this key

# config.factions (E3): exactly the file in §5.13 (the 11-row ladder, points, acts, and its notes text).

# item.material.timber (E5)
name: Rough Timber
notes: What M7's building pieces are made from (content/pieces); stacked by the crossing (loot.timber_stack). Traders do not buy it.
category: material
stack_max: 20
weight: 0.5
value_base: 2
rarity: common
no_sell: true

# loot.timber_stack (E5)
notes: Timber stacked by the crossing (M7). One-shot - an authored container keeps its record once emptied, and nothing refills it.
rolls: 0
guaranteed:
  - { item_ref: item.material.timber, count_range: [80, 80] }
```

FAC001 checks the config as section 5 states, minus the witness checks. Its closed field set refuses a `witness` block by name with §5.14's text: "the witnessed channel is M9".

Dialogue additions are DRAFT. No existing line's text or conditions change.

```yaml
# kera_voss.yaml, in `again`, before `trade`
      - id: armour
        text: "The armour at the seam won't stand guard any more."
        conditions:
          - { kind: act_done, act: creature_killed, creature_ref: creature.construct.animated_armour }
          - { kind: visited, node: armour_down, not: true }
        consequences:
          - { command: report_act, act: creature_killed, creature_ref: creature.construct.animated_armour }
        next: armour_down
  armour_down:
    text: "Then the seam's only a seam again. I keep a few billets back for the waystation's own. Ask, and they're yours to buy."
    once: true
    next_if_exhausted: again
    choices: [{ id: back, text: "I'll look.", next: again }]

# sel_arien.yaml, in `again`
      - id: notes
        text: "What's in the notes you keep back?"
        conditions: [{ kind: reputation, faction_ref: faction.ashen_hollow.survey, min_tier: accepted }]
        next: notes
      - id: armour
        text: "The armour in Blackvein Cut is down."
        # conditions, consequences and next exactly as Kera's `armour` reply
  notes:
    text: "The stones weren't knocked out of line. Each was turned the same quarter, by someone who knew the ring. That's more than I've written down."
    choices: [{ id: back, text: "I'll keep it to myself.", next: again }]
  armour_down:
    text: "Down. It kept that post longer than this waystation has stood, and now nobody can ask it who set it there."
    once: true
    next_if_exhausted: again
    choices: [{ id: back, text: "It was in the way.", next: again }]
# both tavar_back replies gain:  - { command: report_act, act: switch_set, flag_ref: world.foldscar.steadied }
```

On a migrated save, `report_act` finds no act and `act_done` is false, so Quest 2 is untouched.

### 9.9 Consequences for tests and saves

- **Every row moves `content_hash`; no row moves the worldgen fingerprint.**
  - Containers and build areas are layout, and no fixed node is added.
  - So no `BaselineTransition` is registered.
  - An M6 save loads with `CellsRebased` empty, and the stack sits at its baseline: no record, 80 timber in stacks `#00`-`#03`.
- **`LoadAll_Loads_Yaml_Files`** (`tests/Content.Tests/ValidationTests.cs:508-547`) gains 14 IDs. `KnownDirectories_Is_Closed_Set` may gain `pieces`; it asserts only `Contains`.
- **These stay unmodified:**
  - `ReachabilityTests.EveryItem_CanBeGotInAFreshSession` (`tests/Application.Tests/ReachabilityTests.cs:28`): timber comes from the stack;
  - `EveryCreatureAndNode_StandsInTheRegion` (`:16`);
  - `NpcTests.BuyingAndSelling_MoveCoinAndGoodsExactly_AndRefuseCleanly` (`NpcTests.cs:288`): the billet row is last and withheld at neutral.
- **The stack lets a new game build.** Section 12's new-game building proof takes its timber from it.

### 9.10 Test-only content (never in `content/`)

- **E2 (section 7).**
  - The writer pack `Fixtures/content-0.1.7`, with fixture pieces, factions `keepers` and `delvers`, `npc.fixture.smith`, timber and the three configs.
  - The current pack moves from 0.2.8 to 0.2.9. Its `_aliases.yaml` carries `piece.fixture.old_wall: piece.fixture.wall` and `faction.fixture.delvers: faction.fixture.diggers`, and it places `npc.fixture.smith` at (32, 70).
  - A later lint failure moves the pack to 0.2.10, with its hash and mirror in the same commit (L8).
- **E3 (section 5).** The hand-built `faction.fixture.keepers`, `faction.fixture.delvers` and `faction.fixture.watchers` in `tests/Application.Tests/ReputationFixture.cs` (shared by `ReputationTableTests` and `FactionTests`).
- **E5, E7, E8 (section 4).** The `EditedContent` refusal packs (a steep area, a rock in the area, an area over a spawner, `max_pieces: 3`), and one bad file for each of BLD001-BLD009 and WLD015.
- **Test data, not content (sections 7 and 13).** The committed saves `tests/Application.Tests/Saves/m6_hollow/` and `m7_crossing_start/`.

### Not in M7 (section 9)

| Item | Belongs to |
|---|---|
| Renewable timber (`resource.wood.deadfall`, `node.wood.deadfall`), with its own baseline transition and frozen fingerprint | the milestone that wants it (M9 or later) |
| Witness config keys (`sight_m`, `fov_deg`, `identify_m`) | M9 |
| `damage.shot`, `damage.formula`; creature, fire and raid damage | M9 if needed; M10 |
| `medium` and `large` navigation classes | unscheduled; M9 at the earliest |
| The `pieces` art bindings, kit art, a timber-pile look and a timber icon | when the owner releases the kit (M9 at the earliest) |
| More greybox pieces (half wall, window wall, post, fence, bed) | when a proof or playtest needs them |
| Faction visuals | never as world-visible standing |
| Kaldrun Reach, Vessmere and the VS factions | M9 |

### Tests owned by this section

| Test | Project | What it proves |
|---|---|---|
| `LoadAll_Loads_Yaml_Files` (changed) | Content.Tests | The shipped ID list gains exactly the 14 M7 definitions: E1 +1, E3 +3, E5 +7, E6 +1, E8 +2 |
| `TheGamePack_BuildsTheCatalogueTheAreaAndTheTimberStack` (renamed from `…AndTheDeadfall`, L3) | Content.Tests | Shipped content builds the slice's pieces (rows added in E6 and E8), `config.building` with only `melee` damage, `build_area.hollow_crossing`, and `container.timber_stack`, whose table yields 80 timber in 4 stacks |
| `EveryItem_CanBeGotInAFreshSession` (unchanged) | Application.Tests | Timber is reachable in a fresh session, through the stack (C18) |
| `EveryCreatureAndNode_StandsInTheRegion` (unchanged) | Application.Tests | No node is added |
| `BuyingAndSelling_MoveCoinAndGoodsExactly_AndRefuseCleanly` (unchanged) | Application.Tests | The appended gated row leaves Kera's existing wares and refs intact |
