# M7 final reconciliation - rulings and canonical facts (input to every section editor)

**Canonical base:** `main` at `a69693108b89d471cdb409a229ba0bdf78b08fa4` (`a696931`). Phase 1 is closed and merged. A read-only snapshot is at `C:/Users/jluca/AppData/Local/Temp/claude/G--UNNAMED/bfa8b7ba-4180-4f2e-915c-4c2bd4993f45/scratchpad/main_a696931`. The old base is `e10d2c4` (snapshot `.../scratchpad/main_e10d2c4`), and the audit branch is `75e6759` (snapshot `.../scratchpad/audit_75e6759`).

**What changed where:**
- `src/World`, `src/Domain`, `src/Application` and `content` are identical at `75e6759` and `a696931`. Every runtime and content fact in the prep note therefore holds at the base.
- Persistence changed only in `SaveStore.cs` (the L-05 fix: exact `pre_migration_<schema>_<slot>` match, +10 lines after `:422`) and `ProfileLock.cs` (Linux).
- Presentation changed for Phase A.

Phase B is **non-authoritative**. Never design against it.

**Precedence:** these rulings > `M7_POST_PHASE1_RECONCILIATION_PREP.md` > the section text being edited.

## Citations

- The working copy of the design (since merged into `M7_IMPLEMENTATION_DESIGN.md`) had every citation to a file that changed between `e10d2c4` and `a696931` remapped exactly (520 citations; see `citation_remap_report.tsv`). Citations to unchanged files were correct and were left alone.
- The 63 citations whose cited lines themselves changed are listed per section in `recon/changed_citations_by_section.md`. Fix every one listed for your section: verify in the `a696931` snapshot, rewrite the line reference, and, if the behaviour changed, rewrite the claim.
- Any line reference you add must be verified at `a696931`.
- A citation to a `75e6759`-era line in the prep note is valid at `a696931` for `src/World`/`src/Domain`/`src/Application`/`content`. For Persistence, only lines after `SaveStore.cs:422` differ, by +10. For Presentation, re-verify.

## Canonical facts, verified at a696931

- **Schema and digests:**
  - `SaveFormat.SchemaVersion = 14` (`src/Persistence/SaveModel.cs:20`); 14 migration steps, the last `SchemaV13ToV14` (`Migrations.cs:756-805`); `Sections/SchemaV13.cs` exists; fixtures v1-v14; writer pack `content-0.1.6`, current pack `0.2.8`.
  - Digests: `unnamed.player/v9` (`src/World/PlayerState.cs:328`), `unnamed.effective-cell/v2` (`src/World/WorldDelta.cs:626`), `unnamed.simulation/v2` (`src/World/Runtime/Simulation.cs:363`).
- **Tests:** 825 at the base (Domain 147, Application 204, Persistence 173, Content 146, World 61, Presentation 57, EntityRegistry 23, Architecture 14). `tests/Presentation.Tests` is a new project. CI (ubuntu-latest) runs build and tests only; no Godot.
- **Phase 1 is closed** (`docs/PHASE1_TECHNICAL_CLOSEOUT.md:8-9`). M7 base: yes (`:202`).
- **The RAZER window has been measured (2026-09-25) and accepted as the baseline** (`PHASE1_TECHNICAL_CLOSEOUT.md:131, 202-204`; `docs/acceptance/phase1_closeout/razer/report.md`).
  - Every gameplay segment holds 60 FPS except the magic segment's 1% low.
  - The repeatable first-use hitches are carried as targeted presentation/performance follow-ups: magic at about 1.5 s and 3.3 s, combat at about 16.4 s, walking at about 43.9 s and 58.5 s. They are not M7 blockers.
  - Suspected shader preparation is NOT confirmed until profiled.
  - Still owed from M3: exit (b), the 2x2 km capture (`docs/M3_STATUS.md:22`). That is not M7's.
- **Documents:**
  - Byte-identical to `e10d2c4`: ROADMAP, DECISIONS, SYSTEMS, WORLD_ARCHITECTURE, RISK_REGISTER, IMPLEMENTATION_PRECEDENCE, PROTOTYPE, PROGRESSION, VERTICAL_SLICE, CRIME_LAW, WORLD_BUILDING, GAMEPLAY_LOOPS, HUD_INPUT_AND_ACTIONS, the content bible and README.
  - Changed: ARCHITECTURE (`:159`, H-02 note), DATA_MODEL (`:519`, `:559`), PERSISTENCE (about 27 lines from `:247`), INDEX, M3_STATUS, M3C_STATUS and M6_STATUS. New: `PHASE1_AUDIT_REMEDIATION_STATUS.md`, `PHASE1_TECHNICAL_CLOSEOUT.md`.
- **Rulings 1 and 2 are still recorded nowhere.** `docs/M7_STATUS.md` does not exist, and E0 is untouched and fully valid.
- **M7 is still not authorized** (`docs/M6_STATUS.md:187`). `AGENTS.md:13` still scopes Claude "through M6". No git tags exist.
- **The feel test's precondition** (a nontechnical Windows playtest build) is now met (`PHASE1_TECHNICAL_CLOSEOUT.md`, "Blind test"). The test itself has not been run and is not an M7 blocker.

## Decisions (apply exactly; use these names)

**R1 Owner rulings.**
- Q1-Q5 are **approved** (2026-09-25): quarter turns; crime, bounty, pardon and territory deferred; Kera's errand to the player's bench; one crossing build area; the Waystation and the Survey with report-only knowledge and the Animated Armour act.
- Delete every "latest decision point", "owner answer due" and "default stands until" gate, and every "OWNER QUESTION" marker. Replace "default (Qn)" with "approved (Qn, 2026-09-25)".
- The RAZER ruling above is also an owner ruling.

**R2 Schema 14 → 15.**
- Rename:
  - `SchemaV14ToV15` (not V13ToV14);
  - "required from schema 15";
  - the v15 fixture: regenerate `expected.json` for v1..v14, where older versions gain only the empty M7 fields; freeze v1..v14 (never edit them).
- Create `Sections/SchemaV14.cs` freezing:
  - `V14.Player` (18 keys, schemas 13-14);
  - `V14.Companion` (11 keys, schemas 12-14);
  - `V14.EntitiesSection` (6 keys incl. `noises`; `creatures` as the schema-14 `CreatureDto`).
- `SchemaV13.cs` is untouched.
- Repoint to the V14 shapes:
  - 11→12 companions (`Migrations.cs:703`);
  - 12→13 player (`:724`);
  - **13→14 entities writer (`:769`)**;
  - `SchemaV12.cs:32`.
- The step carries `Creatures` **and `Noises`**, and M7 adds `pieces`, `structure_seq` and `npc_errands` (entities) plus `factions` and the companion `route` (player).
- Packs: writer `content-0.1.7` writes v15; the current pack becomes `0.2.9`.
- Test names:
  - `Schema14To15_GivesNothingBuilt…` on `Copy(14)`, also asserting noises and creature continuation are kept;
  - `ASchema15…` corrupt tests;
  - `Schema15Tests.cs`.
- `MigrationTests`: step count 13; load count 14; "to schema 15"; step lists gain "schema 14 -> 15:"; 5-tuple deconstructions at `:149`, `:180`, `:198`, `:290` (M7 replaces them with `DeltaSnapshot`).
- `HistoricalFixtureTests` alias arms: `>= 14` becomes `14`, and a new `>= 15` arm is the 14 list plus M7's renames.

**R3 Digests.**
- `unnamed.player/v10` (unchanged plan).
- **`unnamed.effective-cell/v3`**: M7 terms after the v2 terms, including the creature continuation terms. The "v1 terms" wording becomes "v2 terms".
- **`unnamed.simulation/v3`**: `StructureSequence` after the player digest term, before the cell and noise terms.
- G12's exemption keeps `BaselineHash` out of the digest, as `CreatedEntityRecord.BaselineHash` is.

**R4 `DeltaSnapshot`.** It now carries `Noises`, and all four hand-built sites list it (`SaveLoader.cs:147`, `:369-372`; `BaselineTransitions.cs:112-118`; `WorldDelta.cs:545-548`; plus the test site `WorldDeltaTests.cs:195`). M7's plan stands (decode returns a `DeltaSnapshot`, `with` copies, guard G11). Every `with` keeps `Noises`, and the definition pass rewrites `Noise.CallerKind`.

**R5 Historical saves.**
- No `m6_hollow` and no E0 save commit.
- Extend `GameSaveTests` over the committed `tests/Application.Tests/GameSaves/m6_acceptance/` (schema 12 → 13 → 14 → 15 through the definition pass):
  - `AddedSince12` gains `$.player.Factions`, `$.player.Companions[\d+].Route` and `$.world.(Pieces|StructureSequence|NpcErrands)`;
  - the count formula rises by 5 with one companion;
  - the "nothing built, neutral, no errand, no route, clean first save" assertions go here;
  - the backup is `pre_migration_12_manual_acceptance`;
  - re-verify the "aliases empty" and "fingerprint equal" claims before asserting them.
- The start save for build-shots lives at **`tests/Application.Tests/GameSaves/m7_crossing_start/save/**`**; the existing `.gitattributes` line covers it.
- Criterion 20 becomes "the committed M6 acceptance save loads through 12→13→14→15, complete, …".
- Canonical test name for the M7 assertions over that save: `GameSaveTests.TheM6AcceptanceSave_LoadsIntoM7_NothingBuiltNeutralNoErrand` (new). The existing test's `AddedSince12` list and count are edited by name.

**R6 `StateDump` live.**
- The evidence baseline is **the live reconstructed-state comparison**: `StateDump` including its `live` section, a complete load, and the save's own digest. Phase 1 compared **956 fields** at tick 7303 (1 + player 232 + world 211 + live 512; `docs/acceptance/phase1_audit/`).
- Remove every "415 / 427 / ~470 / ~500 / ~540" figure. M7 records counts with a breakdown and asserts **0 differences**; it never asserts a count.
- M7 extends `StateDump.Live` with `pieces`, piece `stations`, `work_assignments` and `factions` (standings, acts, knowledge), and adds transient fields to `left_out`. `StateDumpTests` is edited by name. From E5 the timber stack appears in `live.containers`.

**R7 `PlayerRecordCompletenessTests` (T-03).**
- M7 extends `Full()` with `Factions` and the companion `Route`.
- `Coupled` gains route `status`, act `cell_key`, and knowledge `identity` with `delta`.
- G12 remains for `PieceRecord` and `NpcErrandRecord`, exempting `BaselineHash`. F-E4 (the `With*` copies) remains.

**R8 Background saves.**
- Saves are captured on the frame at a tick boundary (`GameSession.Capture`, `:268-273`) and encoded and written on one worker, in capture order (`:275-282`).
- `FrameResult` has `AutosaveTaken` (captured this frame) and `AutosavedTo` (a write that finished).
- New guard **G29 `SaveDocument_IsDeeplyImmutable`**: a reflection walk over the `SaveDocument` type graph that rejects mutable collections and settable members. Extend G25 to ban `ImmutableCollectionsMarshal` in `src/World` and `src/Domain`.
- `TakeSnapshot` materialises new arrays for pieces and errands. `NavRoute.Corners` never wraps a scratch buffer.
- `save_ms` = the capture cost on `AutosaveTaken` frames and on a quicksave's capture. The write is off-frame, reported by `FrameStats.Mark`.
- Delete the save-frame exemption and "never move save I/O off the main thread". R-X14 becomes "capture immutability".
- `AuthorityNeverReadsAClock` scans `src/World` and `src/Domain`. In `src/Application` it allows exactly the `GameSession` capture timestamp (`CapturedAt`), by name.
- b19 and every M7 synchronous save use `session.Save(...)`, not `QuickSave`.

**R9 Event isolation (H-02).**
- `GameSession`'s bus isolates subscriber exceptions (`EventBus.cs:26-31`, `:64-87`; `GameSession.cs:108`; `SubscriberFailures` `:365-374`). The rule "presentation cannot abort or replay an authoritative tick" is enforced by Phase 1, and M7 adds no guard for it.
- Every M7 test on `GameSession`/`Harness` collects events and asserts afterwards, **and asserts `SubscriberFailures == 0`**. Every scripted beat STOPs on a non-zero `SubscriberFailures` (part of STOP S5).
- System exceptions are still not isolated, so T10 `BuildingCommands_NeverThrow` stays.

**R10 Superseded by Phase 1 (drop or shrink):**
- E0's M6-save commit (see R5).
- The all-bodies close refusal on authored doors (L-16, `Systems.cs:279-285`). M7 keeps the piece-door refusal and adds **`SystemContext.BodyIn(Blocker)`**, lifted from the Phase-1 predicate and shared by authored doors, piece doors and building check 12. `NoDoorCloses_OnAnyBody` covers the NPC and companion cases Phase 1 lacks.
- The C1 corpse-clause edit (M-01's `IsCorpse`, `Items.cs:565`). Keep `APieceChest_EmptiedAndRefilled_KeepsOneIdentity` and T10, and assert `IsCorpse` never matches `container.pce_*`.
- "Save I/O off the main thread" and the save-frame exemption.
- R-A7's "shut in by a door".

**R11 Walls (L-09).**
- `SystemContext.Walled` (`Systems.cs:72-74`) must read `SightWalls()`, the fourth copy of the wall line. The others are Combat `:575`, Companions `:598` and Creatures `:924`.
- Assign and release reach use `SystemContext.InTalkReach`.
- New test `Kera_BehindAPlacedWall_CannotBeTalkedTo_TradedWith_OrAssigned`.
- `ClosedDoors()` has 9 call sites, and there are 18 `Layout.Space` reads in `src/World/Runtime`.

**R12 Charges (L-24).** Every NPC body stops a charge and stuns the charger (`Creatures.cs:597-605`), so Kera on an errand can stun a boar. State it in §3.1 and R-A7. Walking creatures and lunges still pass through non-companion NPCs.

**R13 Other Phase-1 behaviour M7 must respect:**
- `DamagePiece` from melee never sets `LastCombat` (L-10). Keep `ASwingAtNothing_…` and `ASwingAtTheAir_…` green.
- Scripted beats never equip or unequip under a guard or mid-action (L-22), and never crouch mid-dodge (L-11).
- No assertion expects `ExperienceGained` for a zero award or `HealthChanged` for a zero change.
- Prediction lives in **`src/Application/PlayerMotion.cs` (`Predict`, `:103`)**. M7's prediction edit goes there, and `Prediction_EqualsAuthority_AcrossANewWall` runs headless on `PlayerMotion`.

**R14 Content and dialogue.**
- **C-01 residue:** `o_ore` and `o_billet` are `acquire_item` with `or_item_refs`, so a billet bought from Kera satisfies them. It is reachable only after the armour kill and a report, and `o_spear` still needs the anvil. Record it in §5.19, and in the owner-awareness list as a known interaction, not a design change.
- **M-01:** the wares residue is reworded (a saved wares record never shows the billets; a Kera bought out under a pre-audit build has no record and reads the full stock).
- **L-17:** `world_state` `location_ref` is the fifth grandfathered leak class, sight-justified, and the first M9 witnessed-channel candidate. R2 and N1 still hold.
- **SOC001:** both copies of Sel's `tavar_back` (`greet :21-27`, `again :62-70`) get the identical, ordered `report_act` consequence.
- **Permitted edit:** `NpcTests.ATraderBoughtOut_StaysEmpty_AcrossASaveAndLoad` (the withheld billet stack stays in the record at neutral).
- The playthrough `sel` beat is now `books`/`take`/`back`/…
- NAV006 uses the new Blackvein Cut anchor, (38, 48) with radius 38.
- `IDialogueFacts.WorldFlag(flagId, locationId)`: the fake at `DialogueRulesTests.cs:12` implements ten facts after M7.
- The `QuestDebugger` constructor has 8 parameters. M7's `DescribeCondition` arms may fix the `world_state` "at the speaker" wording.

**R15 UI and the modal rule.**
- Build mode is a gameplay mode, never in `Main.Modal`.
- One exit check runs every frame, outside `ReadInput`, just before `UpdateMouse`: `if (_build.Active && (Modal || player dead)) _build.Exit();`. It also runs in harness runs. `Resync()` also exits build mode.
- `BuildMode.Enter()` never sets the mouse mode. Esc is consumed first by the build branch, and only the next Esc frees the mouse.
- B toggles only when `!Modal`. The build keys are read below the modal gate. Combat reads are `captured && !_build.Active`. Same-frame panel keys re-read `Modal`.
- F2 and F6 are overlays, read with F1/F3/J/F4 and never modal.
- Every key label uses `HelpPanel.Key(...)`, since labels follow the keyboard layout.
- `InputCheck` gains `build_mode`, `build_place`, `build_dismantle`, `build_repair`, `build_rotate`, `build_piece_1` and `work_order`, plus the build steps from prep §6.
- `LayoutCheck` gains the build panel and the F1 fit at 1366x768 and 1280x720.
- `EveryM7Action_IsBoundToADirectKey` asserts no total action count. L is the saves list.
- Views mark themselves dirty on events and rebuild once on the next frame, keyed on `StructureRevision` (P-02/P-03). They never rebuild inside a handler.
- Main.cs wiring for `--build-shots`/`--build-shots-verify` at `a696931`:
  - `ParseArguments` value list `:1432-1434`;
  - scratch or profile branch `:134-140`;
  - `scripted` set `:147-150`;
  - harness chain `:491-506` (follow the `VisualAudit` object pattern, `VisualAudit.cs:86`, `:122`);
  - one-tick `Frame` expression `:533`.

  `Modal`, `UpdateMouse`, `DefineInput` and `HelpPanel` are unchanged since `75e6759`.

**R16 Art coverage (Phase A) - decided:**
- The gate counts and never fails (`ArtCoverage.cs:14`, `:66-91`). The allowlist has `debug:*` only, with the rule "Never allow here what a player sees in ordinary play" (`art_coverage_allowlist.json:6`, `:9`). Reports are written only by `--smoke`, `--delta-shots` and `--playthrough`(`-verify`) through `Main.WriteReports`.
- The F2/F6 overlays use the `debug:` kind, which is already allowed.
- The placement ghost and the build-area outline are effects by design: `Coverage.Resolved("ghost"|"overlay", id, "m7_effect")`.
- Player-built pieces record `Coverage.Fallback("piece", defId, "M7 ships no piece art")`. They are **not** allowlisted by M7, and M7's evidence reports the `piece:*` count explicitly. The Phase-1 route's 0 unexpected must stay 0.
- `--build-shots`/`--build-shots-verify` call `WriteReports` at done and failed.
- Presentation-only reuse of the bound chest (`container_chest_iron_banded`) and anvil (`prop_blacksmith_anvil_stump`) models for the piece chest and bench is **optional**; the default is greybox. Timber wall, roof and floor art is still withheld (`art_bindings.json:22-24`).
- HollowView helpers are now `Solid :635`, `LowestUnder :647` and `BuildDoor :327-393` (root plus a separate leaf hinge when art-drawn). `Palette` `Shaft`, `Wood`, `Door`, `Roof`, `Leather` and `Iron` are unchanged. `HollowView.Bind` takes a `GroundField?`. `StructuresView` still copies its helpers and leaves `HollowView` untouched.

**R17 Performance.**
- M7's evidence builds on `--perf --perf-route extended` (unchanged in code at `a696931`), with one **`building`** segment appended after `first_person`, as prep §7 describes.
- Pass lines apply to that segment. M7 compares against the accepted Phase-A RAZER baseline. The carried first-use hitches are not M7's.
- The segment is named `building`, and its `PerfActivities` goal is `BuildAtTheCrossing`.
- `--perf-world` stays optional (the 256-piece stress case).
- Required instrumentation is unchanged in kind: `sim_ms`, `ticks` (`FrameResult.TicksRun`), `save_ms` per R8, and `NavCounters`.
- An M7 RAZER capture of the `building` segment is recorded as evidence, captured when the owner next runs RAZER. It is not an entry gate.

**R18 Runtime proof.**
- `--build-shots` follows this flow:
  1. join the `scripted` set;
  2. take the profile `.lock`;
  3. clear the profile and state files before Boot (L-20);
  4. copy S0 from `GameSaves/m7_crossing_start/save` into `<dir>/profile/quick`;
  5. Boot;
  6. `LoadChosen(Quick, Current)`, exiting 2 on failure.
- Verify loads `quick` explicitly and asserts `IsComplete`, digest equality and `SubscriberFailures == 0`.
- The playthrough relaunch goes through Continue. The manual save must still be the newest when the run quits.
- Faction beats extend the Phase-1 audit/closeout acceptance transcript (`docs/acceptance/phase1_audit/`, with `phase1_closeout/` for the package).
- The regression set adds `--input-check`, `--layout-check` (both sizes), `--resume-shots` and the extended perf route.

**R19 Unfixed audit items:**
- **L-05 is now FIXED** at the base (`SaveStore.cs:423-439`, `IsPreMigrationOf`). Remove M7's recommendation about it; M7's 14→15 migration relies on the fix.
- L-01: the save-tool claim is reworded ("loads through the game"); not an M7 fix.
- L-04: M7's own decode paths raise the exception types the loader already quarantines, with one hash-valid-malformed test per new DTO.
- T-05: `TakeSnapshot` is not a pure read.
- T-10: M7's env-gated writers refuse when `CI=true`.
- P-02/P-03: see R15.
- The others are unchanged from the prep §9 table.

**R20 Owner items.**
- **Remaining owner process items:** authorize M7; name the agent, worktree, branch and draft-PR convention (AGENTS.md still says "through M6"); the R-1 tag; the dialogue tone review; schedule the feel test (now actionable, not an M7 blocker).
- **One minor owner choice, not blocking, with a default:** allowlist `piece:*` as a scoped exception, or keep reporting it (the default: report, don't allowlist).
- **Done:** the RAZER window.

**R21 Style.** Keep the existing structure, voice and numbering. Minimal, surgical edits. No new sections unless these rulings require them. No production code. Test names follow these rulings.
