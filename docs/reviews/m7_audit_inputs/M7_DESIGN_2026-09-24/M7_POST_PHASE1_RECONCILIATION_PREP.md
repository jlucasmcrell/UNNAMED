# M7 - post-Phase-1 reconciliation preparation

**Date:** 2026-09-25. **Status:** read-only preparation. `M7_IMPLEMENTATION_DESIGN.md` has not been changed, the repository has not been touched, and M7 has not started.

**What was compared.** The M7 design was written against `origin/main` `e10d2c4`. This note compares it with the completed Phase-1 technical-audit remediation, `claude/phase1-audit-fixes` at `75e6759`: 18 commits on `e10d2c4`, pushed, and contained in the in-progress `phase1/complete-prototype` (`e871462`). `origin/main` is still `e10d2c4`; the audit branch is not merged yet. Code citations below are at `75e6759`. `D:nnnn` means a line of `M7_IMPLEMENTATION_DESIGN.md` as it stands (7,540 lines).

**Sources:**
- `docs/PHASE1_AUDIT_REMEDIATION_STATUS.md` at `75e6759`;
- `G:\UNNAMED_HISTORY\PHASE1_INDEPENDENT_TECHNICAL_AUDIT_2026-09-24.md`;
- four read-only diff reviews (persistence and saves; runtime and event bus; presentation, input and performance; content and dialogue), each checked against both snapshots.

**Owner decisions.** Q1-Q5 are **approved as the defaults**, and none is asked again:
- Q1: quarter-turn rotation.
- Q2: crime, bounty, pardon and territory are deferred.
- Q3: Kera walks to the player-built bench.
- Q4: the one crossing build area.
- Q5: the Waystation and the Survey, report-only knowledge, the Animated Armour act.

The final reconciliation rewrites §17 and §1 to say "approved", and removes every "latest decision point" gate from §10 and §18.

---

## 1. What in the original M7 design remains valid unchanged

The architecture and the mechanisms stand. Phase 1 changed facts around them, not the decisions.

- **Navigation (§3).**
  - The derived 250 mm integer lattice, with one tile per cell and the class-count bytes.
  - Gates evaluated at query time; the windowed A* specification (costs, neighbour order, tie-break, the 65,536 cap).
  - String pulling; routes persisted with their mover.
  - Rect rebuilds and seam handling; the one navigability rule; companion priority; the errand mover.
  - `Kinematics.Step` still does all collision, with the same sub-steps, passes and rounding. The landing change (L-15) touches only airborne bodies, and M7's movers are grounded. So the soundness margin (C1-C5) holds.
- **Building (§4).**
  - The 3 m lattice and quarter turns; the seven pieces and their dimensions.
  - The fifteen ordered checks; `PieceRecord` and `StructureSequence`; derived piece IDs.
  - Piece doors, one damage rule, repair, dismantle and destroy.
  - The build area at x/z 87-114 m; the Crossing Workshop and its command table.
  - The timber stack.
- **Factions (§5).**
  - The Waystation and the Survey; `RecordAct` and `ReportAct`; the relevance filter; report-only knowledge.
  - The PROGRESSION ladder and points; both gates.
  - FAC-R5: the armour is still a single `respawn: none` sentinel at (65, 34), and only the heart switch writes `world.foldscar.steadied`.
  - The reputation table and the P script: P1-P5 are still valid on the new dialogue.
  - Kera's billet ware ref is still `#05`.
  - The player digest tag is still `unnamed.player/v9`, so M7's **v10 still stands**.
- **Cross-system (§6).**
  - `StateSlice` still has 18 values, so 18 → 22 holds.
  - The composition order and the body of `Simulation.Step` are unchanged. `Dispatch` still has 32 arms.
  - No system subscribes to events. Construction and load publish nothing.
  - The `RecordDeed` pattern; one `RebuildNavigation` dispatcher; the faction-read ban.
- **Persistence approach (§7).**
  - One schema bump; no new section file.
  - Nullable DTO fields that are required on decode.
  - The `DeltaSnapshot`-as-`with` plan, now more valuable (§3 below).
  - The definition-ID pass additions; content packs `0.1.7` (writer) and `0.2.9` (current), both still unused.
  - The v15 fixture's contents, renumbered.
- **Slices.** E0-E10 with the same critical path. E0 and E2 shrink (§2 and §3 below).
- **Content lint.** Still 102 definitions, so M7's per-slice progression (102 → 116) stands.
- **Capture safety.** Every M7 persisted record is specified as a sealed record over values and `ImmutableArray`, which the new background save needs (§4).

## 2. What is definitely stale now

### 2.1 Superseded by Phase 1: shrink or drop in M7

| M7 item | Phase-1 change | Action |
|---|---|---|
| E0 commit 4: write and commit a real M6 save at `tests/Application.Tests/Saves/m6_hollow/` (D:3591, 4089-4094, 4151, 5018-5022, 7444-7522) | T-02 committed `tests/Application.Tests/GameSaves/m6_acceptance/` (schema 12, M6's own acceptance save) with `GameSaveTests` and a `.gitattributes` line | Drop the writer. Extend `GameSaveTests` instead (§8 below) |
| The all-bodies close refusal on authored doors (D:1616, 3123, 3487, 5295) | L-16: `InteractionSystem` refuses a close over the player, any living creature or any NPC or companion (`Systems.cs:279-285`) | Keep only the piece-door refusal. Recommended: lift the Phase-1 predicate into a `SystemContext.BodyIn(Blocker)` helper that authored doors, piece doors and building check 12 share. Add an NPC case: Phase 1 tests only a wolf |
| The C1 chest-crash clause on `Items.cs:558` (D:329, 1746-1752, 3489, 5566, R-B3) | M-01: an emptied container is removed only if `IsCorpse(site.Key)` (`Items.cs:565`) | Drop the edit. Keep `APieceChest_EmptiedAndRefilled_KeepsOneIdentity` and T10, and make sure `IsCorpse` never matches a `container.pce_*` key |
| "Never done in M7: save I/O off the main thread" (D:7041); the save-frame hitch exemption (D:7015); R-X14 (D:7129) | P-01: saves are captured on the frame and written off it | Delete D:7041. Save frames become ordinary frames (the audit measured 1.67-2.73 ms against a 2.08 ms median). R-X14 becomes "capture immutability" (§4) |
| R-A7's "a creature can be shut in by a door" | L-16 | Drop the clause |
| "Kinematics tuck applies while airborne" (D:263, 1480) | L-15: a landing step resolves at full radius | Reword to "while the feet are off the ground" |

### 2.2 Behaviour M7 must now follow, beyond renumbering

- **Talk and trade stop at walls (L-09).**
  - `SystemContext.Walled` (`Systems.cs:72-74`) reads `Setup.Layout.Space.Blockers` plus `ClosedDoors()`. `InTalkReach` (`:76-78`) uses it for talk, trade and companion revive, and `Simulation.Walled` is public for the prompt.
  - Closed piece doors join automatically, through `ClosedDoors()`. Placed walls do not.
  - M7 must make `Walled` read `SightWalls()`. That is a fourth copy of the wall line, beside Combat `:575`, Companions `:598` and Creatures `:924`. Otherwise talk, trade, revive and assign pass through a player wall.
  - Assign and release reach (D:1800, 1810) use `InTalkReach`.
  - Add a test: Kera at the bench behind a placed wall cannot be talked to or assigned. It mirrors `NpcTests` `Kera_CannotBeTalkedTo_OrTradedWith_ThroughTheSmithyWall`.
- **Charges stop on every NPC (L-24).** A boar charging Kera mid-errand is stunned (`Creatures.cs:597-605`). NPCs take no damage. State this in §3.1 and R-A7. Walking creatures and lunges still pass through non-companion NPCs.
- **A swing is not a fight (L-10).** A melee blow that damages a piece must not set `LastCombat`. Keep `ASwingAtNothing_…` and `ASwingAtTheAir_…` green.
- **Command refusals scripts must respect.**
  - L-22: the hands are locked under a guard or mid-action.
  - L-11: no crouch mid-dodge.
  - The M7 playthrough and build-shots beats must not equip, unequip or crouch in those states.
- **Event semantics.** There is no `ExperienceGained` for a zero award (L-12) and no `HealthChanged` for a zero change (L-26). No M7 assertion may expect them.
- **Prediction moved to Application.**
  - `PlayerMotion.Predict` (`src/Application/PlayerMotion.cs:103`) reads `setup.Layout.Space`. M7's prediction change (D:413, 3504, 5381, 7119) moves there.
  - `Prediction_EqualsAuthority_AcrossANewWall` can then run headless against `PlayerMotion`.

### 2.3 Content, quest and dialogue text

| M7 text | Now | Action |
|---|---|---|
| "Quest 1 is not bypassed: `o_billet` is a `craft_item` deed" (D:2664) | C-01: `o_ore` and `o_billet` are `acquire_item` and count a carried ingot or spear (`or_item_refs`). A billet bought from Kera once the Waystation is `accepted` satisfies both. `o_spear` still needs the anvil | Rewrite it. Record it as a residue in §5.19 and the owner-awareness list. It is not a redesign: the billets open only after the armour kill in Blackvein Cut and a report, and the new Cut discovery circle (38 m around (38, 48)) already includes the armour's post |
| Kera's wares residue: "touched before M7 never shows the billets" (D:2674, 3000, 7110) | M-01: a bought-out wares record now persists empty and never restocks. A Kera bought out under a pre-audit build has no record and shows the full authored stock | Reword the residue |
| Grandfathered Phase-1 knowledge leaks (§5.8, D:2685) | L-17: a `world_state` condition may name a place (`location_ref`). Sel now hides `tavar` once the heart is steadied (`sel_arien.yaml:53`, `:96`), reading the Survey's own reaction subject at a distance | Add it as the fifth grandfathered leak class. It is justified as sight, and it is the natural first consumer of the M9 witnessed channel. Standing is unaffected: R2 and N1 still hold |
| "Dialogue reads flags in the speaker's cell" (D:350, 436, 2695) | Flags may name a place | Reword |
| New `report_act` on Sel's `tavar_back` (§5.13) | SOC001: a reply with consequences on a `once` node must reappear on its fallback with an identical, ordered consequence list | State the rule. Both `tavar_back` copies (`greet :21-27`, `again :62-70`) get the identical consequence, or the content fails at load |
| `NpcTests.BuyingAndSelling_…` "passes unchanged" | Still true | Also add `ATraderBoughtOut_StaysEmpty_AcrossASaveAndLoad` (`NpcTests.cs:440-458`) to the permitted edits: at neutral the withheld billet stack stays in the materialised record |
| Playthrough and legs (D:2611-2612, 6524-6547) | `sel` beat is now `books`/`take`/`back`/… (`primer_given`). Legs at `Playthrough.cs:52-70`; `follow`/`ward` at `:218`/`:219`; `Defend` sentinel filter `:661` | Renumber. The insertion point between `follow` and `ward` is still right |
| NAV006 location reach (§3) | Blackvein Cut anchor moved from (54, 74) r 20 to (38, 48) r 38 | Re-run NAV006 on the new anchor. The seam walk already reaches it |

## 3. Schema 14 → 15 migration impact

Phase 1 shipped schema 14: creature continuation and a noise list (`SaveModel.cs:20`, `SchemaV13ToV14` at `Migrations.cs:756-805`, `Fixtures/v14`). **M7 becomes schema 15.**

### 3.1 Renumbering (mechanical)

| M7 design | Becomes | Where (D:) |
|---|---|---|
| "schema 14", "13 → 14", "required from schema 14" | "schema 15", "14 → 15", "required from schema 15" | 24, 163, 375, 379, 865, 1349, 1698, 2191, 2742-2743, 2756, 3385, 3571, 3577, 4874, 4891, 4917, 5124, 7446 and every "required from 14" |
| `SchemaV13ToV14` | `SchemaV14ToV15` | 380, 3582, 3713, 3763, 3811, 5142-5144, 7097 |
| Create `Sections/SchemaV13.cs` (`V13.Player`, `V13.Companion`, `V13.EntitiesSection`) | Create **`Sections/SchemaV14.cs`**: `V14.Player` (18 keys, schemas 13-14), `V14.Companion` (11 keys, 12-14), `V14.EntitiesSection` (6 keys incl. `noises`; `creatures` as the schema-14 `CreatureDto`) | 380-381, 3732-3755 |
| v14 fixture; regenerate v1..v13; freeze v1..v13 | **v15** fixture; regenerate v1..v14; freeze v1..v14 | 4022-4057, 5189, 7319 |
| `ASchema14…`, `Schema14Tests.cs`, `Schema13To14_GivesNothingBuilt…` on `Copy(13)` | `ASchema15…`, `Schema15Tests.cs`, `Schema14To15_…` on `Copy(14)` (also asserting noises and continuation are kept) | 1237, 4084-4087, 4175-4182, 6062, 6119, 6267-6274 |
| "All 14 fixtures", "1..14" (criterion 19) | "All 15", "1..15" | 5887-5893, 7340, 7368-7369 |
| `effective-cell/v2`, `simulation/v2`; "effective-cell v1 terms" | **`effective-cell/v3`**, **`simulation/v3`**; "v2 terms". Phase 1 already moved both tags to v2 (`WorldDelta.cs:626`, `Simulation.cs:363`) | 256, 385, 610, 856, 863, 1924, 3314-3319, 3374, 3450, 3585, 3909-3917, 4115-4119, 5135-5138 |
| `player/v10` | Unchanged (Phase 1 is still v9) | - |
| `MigrationTests` step counts and lists (`:78` 11 → 12, …) | `:78` 12 → 13, plus `Steps[12]` "schema 14 → 15:"; load count `:435` 13 → 14; `:466` → 15; the step lists gain "schema 14 → …"; the `:289` `Single` becomes 2 steps; 5-tuple deconstructions at `:149`, `:180`, `:198`, `:290` | 4061-4062, 4076 |
| `HistoricalFixtureTests` alias arms (`>= 12`, new `>= 14`) | Arms are `>= 14` (`:251`), `>= 12` (`:258`). M7: `>= 14` → `14`, new `>= 15` = the 14 list + M7's renames | 4060 |
| Line citations: `Migrations.cs`, `SectionCodec.cs`, `SaveLoader.cs`, `WorldDelta.cs`, `BaselineTransitions.cs`, `M2Fixtures.cs`, `HistoricalFixtureTests.cs` | All shifted (e.g. `SectionCodec` entities DTO `:204-222`, `DecodeEntitySection` `:592-644`; `SaveLoader` decode `:143-157`, `ResolveDefinitions` `:207-373`; `WorldDelta` `DeltaSnapshot` `:204-219`, `TakeSnapshot` `:478-549`, `FromSnapshot` `:559-615`, digest `:621-675`) | 3598, 3602, 3671, 3712-3719, 3826-3832, 3847, 3978, 3997-4020, 5150 |

### 3.2 Structural (not just renumbering)

1. **The freeze direction is inverted.** `SchemaV13.cs` and `V13.EntitiesSection` already exist, and M7 changes none of the DTOs they name, so they stay untouched. M7 freezes the **schema-14** shapes into `SchemaV14.cs`. It then repoints:
   - the 11→12 companions (`Migrations.cs:703`);
   - the 12→13 player (`:724`);
   - **the 13→14 entities writer (`:769`)**; if this one is missed, adding M7 keys to `EntitiesSectionDto` changes what 13→14 writes;
   - `SchemaV12.cs:32`.
2. **Carry the noises.** `SchemaV14ToV15` must set `Noises = old.Noises` alongside `Creatures`. A null list fails decode ("required from schema 14") and quarantines every migrated save's entities section. The v14 fixture has two noises, so the migrate-through-commit test catches an omission.
3. **`DeltaSnapshot` now has `Noises`.** All four hand-built sites list it:
   - `SaveLoader.cs:147`;
   - `SaveLoader.cs:369-372`;
   - `BaselineTransitions.cs:112-118`;
   - `WorldDelta.cs:545-548`.

   The trap Phase 1 did not fix is the one M7's plan closes: decode returns a `DeltaSnapshot`, with `with` copies and guard G11. Every `with` must keep `Noises`; the definition pass rewrites `Noise.CallerKind`. The non-empty noises in v14 and v15 satisfy G11's precondition.
4. **`PlayerRecordCompletenessTests` (T-03) partly covers G12.** It reflects over the player: every leaf filled, round-tripped, and moving the digest.
   - M7 must extend `Full()` with `Factions` and companion `Route`.
   - It must add the fields that cannot change alone to `Coupled`: route `status`, act `cell_key`, and knowledge `identity` with its delta.
   - G12 stays for `PieceRecord` and `NpcErrandRecord`. F-E4 (the `With*` copies) is still covered by nothing in Phase 1, so keep it.
5. **The historical-save proof.** Extend `GameSaveTests` over `m6_acceptance`, a real schema-12 save migrated 12 → 13 → 14 → 15 through the definition pass:
   - `AddedSince12` gains `$.player.Factions`, `$.player.Companions[\d+].Route` and `$.world.(Pieces|StructureSequence|NpcErrands)`.
   - The count formula rises by 5 with one companion.
   - M7's "nothing built, neutral, no errand, no route, clean first save" assertions move here.
   - The source schema is 12, and the pre-migration backup is `pre_migration_12_manual_acceptance` (`SaveStore.cs:441`).
   - M7's "aliases empty" and "fingerprint equal" rows must be re-verified for this save before they are relied on.
   - A committed schema-14 predecessor save is optional (owner's choice). It would be written with the post-Phase-A `--playthrough`.
6. **Fixture paths and packs.** Game saves live under `tests/Application.Tests/GameSaves/<name>/save/**`, already binary in `.gitattributes`. M7's `Saves/*/quick/**` paths (D:4092, 4146, 5022, 6440-6449) become `GameSaves/m7_crossing_start/save/**`. The writer pack is still `0.1.6` and the current pack `0.2.8`, so M7's `0.1.7`/`0.2.9` names stand; "0.1.7 writes v15".

## 4. Save-system impact

**How saves work now (P-01, B-01, M-02, M-07).**
- **Capture, on the frame thread at a tick boundary.** `GameSession.Capture()` (`GameSession.cs:268-273`) builds a `SaveDocument` from `CaptureRecord()`, `TakeSnapshot()`, the worldgen identity and `CapturedAt`.
- **Encode and commit, on one chained worker, in capture order** (`:275-283`, `SaveStore.cs:215-222`).
- **Waits and cadence.** `Save` and `Load` wait for pending writes. No new autosave is taken while one is pending. A failed autosave retries at 30, 60, 120 and 240 s.
- **Frame results.** `FrameResult` has `AutosaveTaken` (captured on this frame) and `AutosavedTo` (a write that finished, on a later frame).
- **Other additions.**
  - `.prev-<slot>` keeps a displaced, unproven quick or manual save.
  - Continue picks the newest loadable save of any kind, autosaves included.
  - `<profile>/.lock` enforces one game per profile.
  - `build_timestamp` is now the capture time.

**Verdict: M7's authoritative state is capture-safe as designed.**
- `PieceRecord`, `NpcErrandRecord` (with `NavRoute`), `ActRecord`, `FactionKnowledge`, `FactionStanding` and `FactionLedger` are sealed records over values and `ImmutableArray`. `StructureSequence` is a `long`. `CompanionRecord.Route` is an init property.
- Derived state never enters `SaveDocument`: the grid, scratch, preview scratch, counters, audits, `Space` and footprints.

Two conditions must be stated in §7 and guarded:
- (a) `TakeSnapshot` materialises new `ImmutableArray`s for pieces and errands.
- (b) `NavRoute.Corners` never wraps a scratch or pooled buffer.

Add guard **G29** `SaveDocument_IsDeeplyImmutable`: a reflection walk over the `SaveDocument` type graph that rejects mutable collections and settable members. Ban `ImmutableCollectionsMarshal` in `src/World` and `src/Domain`, extending G25. Nothing in Phase 1 enforces this today: `AsyncSaveTests` pauses after encoding, so a mutation during encoding would slip through.

**Stale save-timing text:**

| M7 text | Correction |
|---|---|
| "The frame loop … autosaves" inside `Frame`; `GameSession.cs:177-201` (D:259, 389, 6614-6620) | "Captured in `Frame` (`:327-353`), encoded and written off-thread in capture order" |
| `save_ms` from `frame.AutosavedTo`, "an upper bound equal to `sim_ms`"; the quicksave timed in `Main` (D:174, 420, 5704, 6605, 6880-6900) | `save_ms` = the capture cost on the `AutosaveTaken` frame (and on a quicksave's capture). The write is off-frame; report it through `Mark("written in the background")` |
| Capture and encode ≤ 1 ms on the main thread (D:6630, 6867) | Split them: capture on the main thread (budgeted), encode on the worker (not in the frame) |
| E-series compare tests and G21/G22 "save mid-conversation" | Still valid. They use the synchronous `Save`, and autosave still captures at a tick boundary with no conversation gate. The errand-never-reads-conversation rule is still needed |
| b19 "QuickSave" | Use `_session.Save(...)`, which waits. `QuickSave` is now background (`Main.cs:1145`) |
| `AuthorityNeverReadsAClock` "green at e10d2c4" (D:6970-6971) | Red at `75e6759`: `GameSession.cs:272` stamps `CapturedAt = DateTimeOffset.UtcNow`, a manifest label no decision reads. Scope the guard to `src/World` and `src/Domain`, and allow exactly that Application line by name |

## 5. Event/presentation impact

**Now (H-02).** `GameSession` builds `EventBus(onSubscriberFailure)` (`GameSession.cs:108`; `EventBus.cs:26-31`, `:64-87`). Each subscriber's exception is caught and reported (`SubscriberFailed`, `SubscriberFailures`, `:365-374`), and the other subscribers still run. Each publish iterates a snapshot of the subscriber list. Delivery is still inline, by exact type, in subscription order. A throwing view can no longer re-run a tick or double-apply a system. The rule "presentation cannot abort or replay an authoritative tick" is therefore enforced for everything that runs through `GameSession`.

**Consequences for M7:**
1. **No new M7 guard is needed** for this rule. M7's 15 new events inherit the isolation.
2. **Assertions inside handlers are swallowed** under `GameSession`/`Harness`. The view test, G27 `ConstructionAndLoad_PublishNoM7Event` and every event-driven M7 test must collect events and assert afterwards. Each such test, and every `--build-shots`/`--playthrough` beat, must also assert `SubscriberFailures == 0` (the pattern at `GameSaveTests.cs:73`). STOP S5 ("an exception") must include a non-zero `SubscriberFailures`.
3. **System exceptions are not isolated.** `Step` and `DrainCommands` have no catch. M7's T10 `BuildingCommands_NeverThrow` and the chest-identity tests stay required.
4. **Presentation work inside the tick.** Unfixed P-03 matters to M7:
   - Isolation contains exceptions, but handlers still run synchronously inside `Step`/`DrainCommands`.
   - `StructuresView`, the F2 overlay and the ghost must not rebuild inside a handler: on `PiecePlaced`/`StructuresChanged`/`NavigationRebuilt` they mark themselves dirty, and they rebuild once on the next frame, keyed on `StructureRevision`.
   - The same applies to P-02 (views allocated every frame): cache piece, station and navigation views per revision.
   - This is design text for §8 and §9, not new scope.

## 6. UI/input impact

**Now (M-03, L-27, M-06).**
- **The modal gate.** `Main.Modal` (`Main.cs:515`) is true while the inventory (any kind), dialogue, character sheet or saves list is open. `ReadInput` has one gate (`:598-605`), and nothing pauses.
- **Overlays.** F1, F3, J and F4 take no keys and never enter `Modal` (`:587-597`).
- **The mouse.** It is decided only in `UpdateMouse`, once a frame (`:545-552`). A recapture click is not an action.
- **Loads close panels.** `Resync` closes the panels (`:1126-1142`).
- **Keys.** L is the in-game saves list (`:1342`). Key labels follow the keyboard layout through `HelpPanel.Key(action)`.
- **Scaling.** The UI scales (`canvas_items`, `expand`; the logical canvas is at least 1920×1080).
- **Checks.** `--input-check` presses 14 gameplay actions with each panel open. `--layout-check` checks panels at 1366×768 and 1280×720.

**Reconciled build-mode rule.** Build mode is a **gameplay mode, not a modal panel**, and it keeps input gating centralized:
1. **One exit check every frame, outside `ReadInput`**, just before `UpdateMouse`: `if (_build.Active && (Modal || player dead)) _build.Exit();`. It also runs in harness runs, and `Resync()` calls `_build.Exit()`. This replaces M7's "exit triggers read at the top of `ReadInput`" and its three-panel list (D:4242, 4249, 4264, 4284). The saves list (L) and a failed load both end build mode through `Modal`.
2. **Build keys are read below the modal gate.**
   - B toggles build mode only when `!Modal`.
   - The build branch reads 1-7, PageUp/PageDown, R, left mouse (only with the mouse captured), Z/Delete and T.
   - The combat reads become `captured && !_build.Active`.
   - Y (`work_order`) sits in the gameplay section.
   - Panel keys pressed in the same frame re-read `Modal`, not a cached copy.
3. **The mouse.** `BuildMode.Enter()` sets no mouse mode (D:4279 is stale). Esc is consumed by the build branch first (`Exit`), and only the next Esc sets `_mouseFreed`.
4. **Debug views.** F2 and F6 are read with the overlays, never in `Modal`, and play continues under them.
5. **Labels.** Every key label comes from `HelpPanel.Key(...)`: the mend, take-down and assign prompts, "press Z again", and the F1 BUILDING rows (D:4358-4360, 4383, 4489). On QWERTZ, physical Z and Y print swapped. The b15 assertion composes its string the same way.
6. **Checks.**
   - `InputCheck` gains `build_mode`, `build_place`, `build_dismantle`, `build_repair`, `build_rotate`, `build_piece_1` and `work_order`, plus build-specific steps: panels end build mode; Esc leaves build mode before freeing the mouse; the recapture click never places; L ends build mode.
   - `LayoutCheck` gains the build panel and the F1 fit, at both sizes.
   - `EveryM7Action_IsBoundToADirectKey` must not assert a total action count, because `saves` now exists.
   - The build panel at (-380, 290) is still valid under scaling.
   - F1 recount: the right column is now 21 rows plus the `Files` label; `LeftSections` is at `HelpPanel.cs:51`.

## 7. Performance-harness impact

**Now (P-01, P-07).**
- `--perf --perf-route extended` (`PerfRun.cs:61-87`, `PerfActivities.cs`) plays warmup, conversation, magic, combat, loot (inventory and Take All), then a 150 s third-person and a 150 s first-person segment. That is more than 300 s, and the autosave lands inside the capture.
- `FrameStats` has `Mark(what)` and an `events` summary showing the three frames after each marked frame against the segment median.
- The gate route is unchanged.
- `FrameStats` still has **no** `sim_ms`, `ticks` or `save_ms`, so M7's required columns are still needed.
- The audit's ASTRAL capture: medians around 2.08 ms, and no autosave hitch. It is not the gate; RAZER is still owed.

**Reconciliation:**
- **Evidence base.** M7's performance evidence is built on the extended route, not the short clean walk. Replace D:6414, 6429, 6590, 6981 and 7000-7003 ("`--perf` unchanged; `--perf-world` replaces segments").
- **New segment `building`, appended last** after `first_person` through `PerfActivities`. It goes last because the route's `AfterCharwood` walk crosses (100, 100), inside the Crossing Workshop. The six existing segments stay comparable with the audit capture. The segment:
  1. Walk to the timber stack; open the stack (a modal panel) and take timber.
  2. Enter build mode, place the Crossing Workshop rows landed so far, and sweep the ghost for 10 s: the preview cost, plus check 15 every 0.5 s.
  3. Take down and replace one wall twice: the rebuild and replan tick.
  4. From E9: assign Kera, wait for `RoutePlanned`, release her. The walk-home plan is the known worst tick.
  5. Take one synchronous capture with the pieces standing, then exit.
  6. Mark `PiecePlaced`, `NavigationRebuilt`, `RoutePlanned`, `WorkerReleased` and the capture.
- **Pass lines.** Apply them to the new segment and read the command-frame line from the marked events. Drop the save-frame exemption.
- **`--perf-world`.** Stays optional, only as the 256-piece stress case.
- **Required instrumentation (L4) is unchanged in kind.** `sim_ms` and `ticks` come from `FrameResult.TicksRun`. `save_ms` is the capture cost on `AutosaveTaken` frames (§4).
- **Line citations.** Renumber `FrameStats`, `PerfRun` (`MaxDistance` is now read at `:74`, and is still a const) and the `GameSession` frame lines.

## 8. Test/evidence impact

- **Field counts.** The "415 fields" language is obsolete; so are the derived 427, ~470, ~500 and ~540 (D:3950-3951, 4113, 4123, 4144, 5178, 5257, 6554, 6571).
  - The baseline is the audit relaunch: **956 fields** at tick 7303 (1 + player 232 + world 211 + `live` 512), with the load required to be complete and the digest required to match (`docs/acceptance/phase1_audit/`).
  - M7 criteria keep "0 differences" and record counts; they never assert a count.
  - Every M7 save/load proof names the **live reconstructed-state comparison**: `StateDump` with its `live` section, a complete load, and the save's own digest.
- **The `live` dump is hand-listed** (`StateDump.cs:35`, `:57-89`), and `StateDumpTests` asserts its `left_out` list exactly.
  - D:3933's "the raw dump needs no change" is wrong.
  - M7 extends `Live` with pieces, stations, work assignments and factions. It lists transient fields in `left_out`, such as in-flight corner indices if exposed.
  - From E5 the timber stack appears in `live.containers`.
- **Kept unchanged.** Save-then-continue equality and deterministic replay are unchanged in kind. The acceptance relaunch now goes through Continue. M7's verify runs must load `quick` explicitly and assert `IsComplete` and the digest; an autosave taken late in a long run could otherwise win Continue.
- **Permitted Phase-1 test edits (§12.10) gain:**
  - `GameSaveTests` (`AddedSince12` and its count);
  - `StateDumpTests` (`left_out`, and the `live` view list if pinned);
  - `PlayerRecordCompletenessTests` (`Full()`, `Coupled`);
  - `NpcTests.ATraderBoughtOut_StaysEmpty_AcrossASaveAndLoad`;
  - the new `MigrationTests`/`HistoricalFixtureTests` lines (§3).
- **Must-stay-green set (§3.20.4, §12.10) gains:**
  - `ObserverTests`, `AsyncSaveTests`, `ResumeTests`, `SaveFailureTests`, `RotationTests`;
  - `ADoor_WillNotCloseOnAWolfInTheDoorway`;
  - `Kera_CannotBeTalkedTo_OrTradedWith_ThroughTheSmithyWall`;
  - `AChargingBoar_DoesNotRunThroughTavar`;
  - `ABodyThatStopsAsItLands_StandsClearOfTheTimber_AtItsFullRadius`;
  - `NothingCanBePutIntoACorpse_…`;
  - every T-01/T-02/T-03 test.
- **Harness runs in the regression set.** Add `--input-check`, `--layout-check` (both sizes), `--resume-shots` and `--perf --perf-route extended`.
- **Test count.** The Phase-1 baseline is **767** tests, not 696.
- **Unfixed audit items that matter here:**
  - **T-05:** `TakeSnapshot` is allow-listed as a read but rebases, stamping `BaselineHash`. M7 relies on it and must not treat it as pure. `PreviewsInterleaved_ChangeNothing` and G8 stay.
  - **T-10:** environment variables turn tests into tautologies. M7's own env-gated writers must refuse to write when `CI=true`: `UNNAMED_WRITE_REPUTATION`, the start-save writer, and optional `UNNAMED_WRITE_PERF_WORLD`/`UNNAMED_WRITE_COST`. That is in scope, because M7 creates them.

## 9. Source/interface assumptions likely to conflict

**Signatures and locations that moved:**
- `IDialogueFacts.WorldFlag(flagId, locationId)` now takes two arguments (`Domain/Social/Social.cs:137`). M7 adds `StandingLevel` and `ActDone` beside it, and the test fake in `DialogueRulesTests.cs:12` implements all ten.
- The `QuestDebugger` constructor has an eighth parameter (`worldItems`). `DescribeCondition` is at `:399`, its fallback at `:413`, `Recipe` at `:416-419`. Its `world_state` text still says "at the speaker" and ignores `LocationId`. M7's new arms can fix that line cheaply.
- `SystemContext` now spans `:24-95`, with `Walled` and `InTalkReach`. There are 9 `ClosedDoors()` call sites (not eight) and 18 `Layout.Space` reads in `src/World/Runtime` (not 17). M7 switches the new `Systems.cs:74` through `SightWalls()`.
- `FrameResult` is now `(TicksRun, Alpha, Saves)` plus `AutosaveTaken`, `AutosavedTo` and `AutosaveFailed` (`GameSession.cs:74-89`).
- `DeltaSnapshot` has `Noises`; `CreatureRecord` has four continuation fields; `DecodeEntitySection` returns a 5-tuple (M7 replaces it with a `DeltaSnapshot`).
- Wholesale line drift in `Main.cs` (+418/−164 lines), `Items.cs`, `Social.cs`, `Systems.cs`, `Creatures.cs`, `Combat.cs`, `Simulation.cs`, `Playthrough.cs`, `CompanionTests.cs` and `NpcTests.cs`. Every `path:line` in §2-§13 must be re-cited against the final base. The reviews produced remap tables; re-run them on the final base rather than trusting these.

**Run modes, profiles and evidence:**
- **Profiles and locks.** Scripted modes bypass the start screen only if they are listed in `Main`'s `scripted` set (`Main.cs:132-136`). `--build-shots`, `--build-shots-verify` and (if built) `--perf-world` must join it. Each takes the profile `.lock`.
- **Start-save flow.** `--build-shots` follows the playthrough pattern:
  1. Clear the profile and state files (L-20).
  2. Copy the committed start save into `<dir>/profile/quick`.
  3. Boot.
  4. `LoadChosen(Quick, Current)`, exiting 2 if it fails.

  M7's "`NewGame`, then `Load(quick)`" and its scratch-profile wording (D:4526, 5088, 6442-6449) are stale. A `.prev-quick` may appear in that profile.
- **Evidence baseline.** M7's faction beats extend the audit acceptance transcript in `docs/acceptance/phase1_audit/acceptance/` (commands and 956-field state), which supersedes the M6 transcript as the baseline. The manual save must still be the newest when the run quits.

**Unfixed Phase-1 audit items: relevance to M7** (M7 does not expand to fix them):

| Item | Matters to M7? | Decision |
|---|---|---|
| L-01 save tool reports real saves as blocked | Slightly: M7 §7 says "`save:migrate --dry-run` prints the report" | Reword to "loads through the game"; record as a known tool limit. Not an M7 fix |
| L-02 a tombstoned derived creature ID cannot be re-set | Same class as M7's derived-ID risk (R-B3), for creatures only | No action; M7's piece IDs never recur (`StructureSequence`) |
| L-04 a hash-valid malformed section can crash the loader | **Yes**: M7 widens the decode surface (fixed-length arrays, factory rethrows) | M7's own decode paths must raise the exception types the loader already turns into quarantine, with one test per new DTO (a hash-valid malformed row quarantines, never crashes). No general L-04 fix |
| L-05 pre-migration backups matched by suffix over-delete | **Yes**, mildly: M7's 14 → 15 migration runs on the owner's real saves | Recommend a Phase-1 maintenance fix before M7's E2 (owner or Phase-1 agent), not in M7. If it's not fixed, E2 tests that migrating one slot leaves other slots' backups intact |
| L-07 M3 → M6 transition carries coordinates | No (M7 has no transition) | - |
| L-08 two crafts in one tick share a quality roll | No new exposure (the piece bench uses the same crafting) | - |
| L-25 idle tick between actions | No (the damage rule is per blow, not per time) | - |
| L-28 hand-assembled export | Affects the M7 "playable build" proof process only | Record; not M7 scope |
| L-29 wolf wedge; L-30 Tavar's snag guard off while fighting | No; M7 leaves creature and fight movement alone | L-30 stays; M7's route mode must not touch fight movement |
| T-04 replay evidence is narrow | Optional: M7 can replay its build-shots command log headless, cheaply | Optional; not required |
| T-05, T-10 | Yes | §8 above |
| T-06, T-07, T-08, T-09, T-11 | No | - |
| P-02, P-03 | Yes, for M7's views | §5 point 4 (design text, not scope) |
| P-04, P-05, P-06 | No (greybox; negligible commands) | - |

## 10. Items that cannot be finalized until the active Phase-A asset/prototype session completes

These are provisional. Final hashes, source locations and presentation code are not assumed from the active session. What is committed on `phase1/complete-prototype` (`e871462`) today already signals the following:

1. **`Main.cs` wiring.** Phase A adds `--visual-audit` and `--visual-audit-ab` in the same five places M7 wires `--build-shots`:
   - the profile branch;
   - the `scripted` set;
   - the harness chain;
   - the one-tick `Frame` expression;
   - `ParseArguments`.

   Textual conflicts are certain. M7's presentation wiring is specified against the merged file.
2. **`HollowView` and greybox helpers.**
   - `BuildDoor` now returns a root with a separate leaf hinge, and the whole-building binding hides authored building walls and roofs while keeping their colliders.
   - M7 §9's "copy `HollowView`'s helpers; `HollowView` untouched", its door-swing convention and every `HollowView` citation must be re-derived from the merged file.
3. **`art_bindings.json`.**
   - Chest, anvil, fence panel, forge shed and longhouse leave the withheld list, and whole-building models are added.
   - M7 §9's statements that the chest, anvil and building kit are withheld (`:25-31`, `:46-47`) go stale.
   - One decision waits for Phase A: whether piece chests and anvils reuse the authored art records (presentation-only, optional) or stay greybox beside art-drawn authored ones. Timber wall, roof and floor art remain withheld.
   - M7 must not depend on either choice.
4. **The asset coverage gate** (uncommitted `ArtCoverage.cs`, intended to count unexpected greybox).
   - Phase A's gate is "zero unexpected greybox fallback on the normal play route".
   - M7's `StructuresView` pieces, the ghost, the build-area outline and the F2/F6 overlays are intentional greybox. They must be allowlisted (for example `piece:*`, `ghost`, `overlay:*`) in whatever form the merged gate takes, or M7 trips the asset gate.
   - The exact mechanism is unknown until Phase A lands.
5. **`ArtLibrary` H-02 handoff.** Presentation robustness only; no M7 dependency. Confirm it has been merged.
6. **Performance baseline.** Phase A changes LODs, materials and mipmaps, which moves every frame-time number. M7's `building` segment and pass lines are re-baselined on the post-Phase-A extended route. The owed RAZER window should capture after Phase A.
7. **Evidence baseline.** A fresh private Windows build and asset snapshot will exist. M7's "playable build" proof and the acceptance transcript baseline follow whatever the owner accepts at the Phase-A checkpoint.
8. **Field counts.** Phase A may add presentation-derived but authoritative-adjacent state (unlikely). Re-count the relaunch comparison on the merged base before writing any M7 estimate.
9. **Schema.** If Phase A (or any merge before M7) bumps the save schema again, M7 becomes 15 → 16 and §3 re-runs.

The modern visual overhaul after Phase A is **not** an M7 prerequisite, and M7 does not wait for it.

## 11. Exact final-reconciliation checklist to run after Phase A lands

Run this once the owner has accepted the Phase-A checkpoint and the audit and Phase-A work are merged. Use Sonnet or Haiku subagents for the mechanical steps and the top model only for the adjudications.

1. **Fix the base.** Record the merged `main` commit, confirm it contains `75e6759` and the Phase-A merge, and extract a read-only snapshot.
2. **Schema check.** Read `SaveFormat.SchemaVersion`:
   - if 14, apply §3 as written (M7 = 14 → 15);
   - if higher, shift every number by one and re-run §3.2 against the new last step.
3. **Digest check.** Read the tags at `PlayerState.cs` (expect player v9), `WorldDelta.cs` (expect effective-cell v2) and `Simulation.cs` (expect simulation v2). Set M7's to the next value of each.
4. **Delta diffs.** Run the four reviews again, from `75e6759` to the new base: persistence and saves; runtime and event bus; presentation, input and performance; content and dialogue. Record anything Phase A changed beyond §10.
5. **Apply §2-§9 to `M7_IMPLEMENTATION_DESIGN.md`:**
   - §1/§17: Q1-Q5 approved; remove the decision-point gates in §10 and §18.
   - §2: inventory rows for L-09 `Walled`, L-16 doors, L-24 charges, M-01/M-04 containers, H-02 event bus, P-01 saves, `PlayerMotion`, the modal rule, start screen and profile lock.
   - §3: L-24 note; R-A7 update.
   - §4: drop the C1 clause edit; add `BodyIn`; switch `Walled` to `SightWalls()`; assign/release via `InTalkReach`; `DamagePiece` never sets `LastCombat`.
   - §5: C-01 residue; M-01 wares residue; L-17 fifth leak class; SOC001 rule for `tavar_back`; P-script line cites.
   - §6: catalogue additions (`Walled`, `InTalkReach`, `BodyIn`, `SubscriberFailures`), G29, clock-guard scope.
   - §7: all of §3 and §4 above; `GameSaves` paths; `GameSaveTests` extension; `live` dump.
   - §8/§9: the modal-rule integration (§6 above); `HelpPanel.Key` labels; `InputCheck`/`LayoutCheck`; dirty-flag views (P-02/P-03); asset-gate allowlist; post-Phase-A `HollowView`/`art_bindings` re-derivation.
   - §10: E0 drops the M6-save commit. E2 becomes the schema-15 slice with the `SchemaV14.cs` freeze, the four repoints, the noise carry and the `GameSaveTests` extension. E1/E5 wire harness modes into the `scripted` set. E5 adds the `building` perf segment, and E9 extends it.
   - §11: rebase field language on the 956-field live comparison; criteria 19 and 20 renumbered; criterion 33 on the extended route.
   - §12: the permitted-edit and must-stay-green additions (§8); renamed schema tests; G29.
   - §13: the `--build-shots` start and verify flow (§9); `SubscriberFailures == 0` at every beat; the audit transcript as baseline.
   - §14: the extended route plus the `building` segment; `save_ms` semantics; remove the save-frame exemption and D:7041.
   - §15: R-X14 becomes capture immutability; R-A7, R-B3 and R-X9 refreshed.
   - §16: drop "save I/O off the main thread".
6. **Mechanical stale-term sweep** over the updated design. Each of these must return only intentional historical mentions:

   ```
   "schema 13|13 → 14|13->14|SchemaV13ToV14|V13\.(Player|Companion)|v14 fixture|effective-cell/v2|simulation/v2|\b415\b|\b427\b|~470|~500|~540|m6_hollow|Saves/m|AutosavedTo|save frame|runs synchronously|PlayerController\.cs:127|Items\.cs:558|696 tests|eight .ClosedDoors|17 .Layout\.Space"
   ```
7. **Re-cite.** Re-verify every `path:line` in §2-§13 against the new base, starting with `Main.cs`, `Items.cs`, `Social.cs`, `Systems.cs`, `Creatures.cs`, `Playthrough.cs`, `HollowView.cs` and `art_bindings.json`.
8. **Content re-check.**
   - Re-run the P-script walk-through against the merged dialogue and quest files.
   - Confirm FAC-R5 (armour single and non-respawning; only the heart switch writes `steadied`).
   - Confirm Kera's `#05` ware ref, the lint count of 102, and the NAV006 anchor.
9. **Verify.** One Sonnet cross-section consistency check over the updated document, and one code-fact spot check (40-60 claims) on the new base. Apply the findings.
10. **Update `M7_EXECUTIVE_BRIEF.md`:** schema 15, digest v3 tags, the 956-field evidence, the extended-route perf segment, the approved Q1-Q5, and the C-01 billet residue.
11. **Hand over for authorization.** Record the final design commit or date, the base commit, and the remaining owner process items: authorize M7 and name the agent, worktree and branch; the RAZER window after Phase A; the R-1 tag; the dialogue tone review; optionally L-05 before E2.
