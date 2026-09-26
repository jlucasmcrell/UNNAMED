# M7 audit: determinism and persistence

Status: auditor's report on the full M7 draft set (00, A, B, C, D, E, F, GH, IJ), 2026-09-24. Design review only; nothing here edits a draft. Every code citation is repo-relative and was re-read in the read-only snapshot of origin/main `e10d2c4`. Where a draft is cited, D's precedence rule applies (D wins over A, B and C; F refines D).

## 0. Verdict

The skeleton is sound. Piece IDs come from a persisted sequence. Acts use a persisted `Seq`. The grid is an order-free `min` over inputs and is never saved. Routes are saved together with their stamp, which is correct and necessary. The schema-14 plan finds all four `DeltaSnapshot` construction sites, all seven `PlayerRecord.With*` copies and the `SchemaV11ToV12` repoint that D missed. None of that needs to change.

The drafts still contain two real save-then-continue breaks and one replay flake:

- **High:** the witness check and the errand mover read state that is not saved: the open conversation, and the facing of NPCs who are not on an errand. Autosave can fire while a conversation is open, and quicksave has no dialogue gate. C's claim that continuity holds "by construction" is therefore false.
- **High:** item IDs are wall-clock values and are not monotonic within one millisecond. M7 adds more decisions that are ordered by `ItemId`, and more flows that mint item IDs. Even the replayable dump can therefore diverge in stack counts. N-A8, E21 and E30 claim raw-digest equality in windows that mint.

After those come four medium problems:

- unsaved tier hysteresis gates the errand mover;
- the definition pass can build an invalid ledger, and the resulting exception escapes as an uncaught crash;
- the `NavRoute` invariants contradict the follower, and an invalid route surfaces as an autosave crash or as a silently rejected row;
- record equality over `ImmutableArray` members is reference equality, which breaks several planned asserts.

Nine low findings follow.

## 1. Method

- **State walk (§4).** I listed every new piece of mutable state and every existing transient that a new M7 decision reads. For each one I asked three questions. Is it persisted? Is it a pure function of persisted state and content? Does it enter `StateDigest` and `StateDump`?
- **Decision walk (§5).** I listed every new decision point: placement checks 1-15, the edit check, the follower's triggers, door choice and operation, witnessing, learning, eviction, damage and cascade, assignment, arrival and retirement. I then traced each input back to the state walk.
- **Schema checks (§6).** I checked F's schema-14 plan against `SaveLoader`, `SectionCodec`, `Migrations`, `BaselineTransitions`, `WorldDelta`, `PlayerState`, `StateDump` and `M2Fixtures` in the snapshot.

---

## 2. Findings, most severe first

### H1. Witnessing and the errand mover read the unsaved open conversation and unsaved NPC facing

**Where.**
- C §5.1 K2 ("NPC bodies are taken as they are at that instant (an NPC in conversation faces the player…)").
- C §14.6 ("Save-then-continue equals continue by construction").
- D §1.3 (D27: "if conversation is with this NPC: face the player … continue").
- A §8.4 and B §15.3, which say the same as D27.

**Evidence.**
- The open conversation is declared transient: "Transient: a load starts outside any conversation" (`src/World/Runtime/Social.cs:90-91`).
- An NPC's body is declared transient too: "Transient in Phase 1" (`Social.cs:87-88`).
- `NpcSystem.Tick` turns every NPC that is not a companion towards the player while talking, and back to `Site.FacingMdeg` afterwards, at 18,000 mdeg per tick (`Social.cs:107`, `:149-164`). That is up to 10 ticks of history-dependent facing.
- `Perception.Sees` applies the field-of-view cone around `eye.FacingMdeg` (`src/Domain/Creatures/Perception.cs:95-97`). K2 passes `M.Body`, and C sets the cone to 140° (C §5.3).
- Saves happen while a conversation is open:
  - autosave runs from `GameSession.Frame` every 300 s of play (`src/Application/GameSession.cs:194-197`; `src/Persistence/SaveStore.cs:41-45`), with no conversation check;
  - quicksave has no dialogue gate (`src/Presentation/Main.cs:485-486`).
- The M6 companion already stands still in conversation (`src/World/Runtime/Companions.cs:286-290`). That is the pre-existing instance of the same hazard; M7 adds two new consumers of it.

**Failure scenarios.**
- (a) Kera is `to_work` and the player is talking to her when the autosave fires.
  - The continuing world holds her still (D27).
  - The loaded world has no conversation, so she walks.
  - Her errand pose, route and stuck count diverge, and `EffectiveCellDigest` v2 differs on the next tick.
- (b) The player ends a conversation with Renn, who is facing the player, and saves within 10 ticks.
  - On the next tick the armour dies within 30 m of Renn, inside his player-facing cone but outside his site-facing cone.
  - The continuing world records `witnessed` and moves the Waystation's standing. The loaded world records nothing.
  - The ledger, which is in `PlayerRecord.Digest` v10, diverges.

**Fix.**
1. **K2 reads only persisted facing, or facing from content.**
   - Add `WitnessEye(npc)` in `FactionSystem`.
   - For an errand NPC it is the errand record's `FacingMdeg`, which is persisted.
   - For every other NPC it is `npc.Site.FacingMdeg`, which is content.
   - Strike C §5.1's sentence "an NPC in conversation faces the player". A talking NPC is judged as if standing at their post.
2. **No save while a conversation is open.**
   - An autosave that falls due while `Simulation.Conversation != null` is deferred: `_lastAutosave` is not advanced, and the save runs on the first frame after the conversation closes.
   - `GameSession.Save` refuses with "finish the conversation first" while a conversation is open, and presentation shows that reason as a toast.
   - This also closes the M6 companion case. It is not "refusing to save" in E.1's sense, because R-1 (`docs/ROADMAP.md:15`) is about playable builds.
   - If the owner wants saves mid-conversation, the alternative is to persist `conversation_open {npc_id, dialogue_id, node_id}`: the key required from schema 14, the value nullable, and both IDs resolved by the definition pass. Item 1 is still required under that alternative.
3. **Tests (E8).**
   - `ASaveDuringAConversationWithAWalkingWorker_IsDeferred_AndTheWorldContinuesEqual`.
   - `AWitnessJustAfterAConversation_LearnsTheSameAfterASaveAndLoad`: save 3 ticks after `ConversationEnded`, kill the armour at +1, and assert `StateDigest` equal for 50 ticks.
4. Correct C §14.6's "by construction" sentence.

### H2. Wall-clock item IDs: M7 overclaims raw-digest replay equality, and ItemId-ordered decisions can diverge even in the replayable dump

**Where.**
- B §3.3 check 14 (stacks "ordered by quality ascending then `ItemId` ordinal").
- B §8.3 (refund through `GrantItem`) and B §18's last bullet.
- D §1.7's last bullet, D §6.2 and guard G8.
- A N-A8 ("`StateDigest` equal" for a script that includes dismantle).
- E acceptance 21 ("N-A8 … raw digest included") and 30 ("No M7 system mints a wall-clock ID").
- B §4 guard H4 and IJ T10 ("the replay equals the run", 500 LCG commands including store, take, dismantle and repair).

**Evidence.**
- `EntityId.NewId` is a millisecond timestamp plus 80 random bits (`src/Domain/EntityId.cs:48-53`). Two IDs minted in the same millisecond therefore sort in random order.
- Headless tests step many ticks per millisecond. The played session and the replay both run at that speed.
- Items are minted through the registry (`src/World/Runtime/Items.cs:657`; `src/EntityRegistry/EntityRegistry.cs:71-72`).
- `Put` merges into stacks of the same kind in `ItemId` order, filling each to `stack_max` (`Items.cs:576`, `:600`, `MergeInto` at `:611-627`).
- `ConsumeItem` spends in `ItemId` order (`Items.cs:387`), and crafting spends in `ItemId` order after quality (`src/World/Runtime/Crafting.cs:177-178`).

**Problems.**
- (a) **The claims do not hold.**
  - A dismantle refund mints whenever no partial stack exists (`GrantItem` → `Put`, `Items.cs:367-374`). Chest stores that split a stack mint, and deadfall gathers mint.
  - The raw `StateDigest` hashes item IDs (`src/World/PlayerState.cs:329-330`), so N-A8 and E21 fail whenever the script mints.
  - E30 is false in effect. G8's source scan cannot see the mint, because it happens in `Items.cs`, not in the M7 files.
- (b) **ID order decides counts, not only names.** Suppose two timber stacks of equal quality are minted in the same millisecond.
  - Check 14 can then take from a different stack in the run and the replay: stacks {20, 5} minus 4 becomes {16, 5} in one and {20, 1} in the other.
  - A refund of 7 into partial stacks {15, 10} becomes {20, 12} in one and {15, 17} in the other.
  - The replayable dump masks IDs but keeps counts, and it sorts records by content. It therefore differs, so T10 and H4 can flake.

**Fix.**
1. Check 14 (placement and repair) takes stacks ordered by **(quality ascending, count ascending, `ItemId`)**. Stacks of equal count are interchangeable, so the counts left after the spend no longer depend on ID order.
2. Change `Put`'s merge order at `Items.cs:576` and `:600` to **(count descending, `ItemId`)**: fill the fullest stack first. With one partial stack, which is the common case, behaviour is identical. With several, the result no longer depends on ID order. D §5.2 already touches `Items.cs`; run the full suite and record the change in `M7_STATUS`.
   - If the owner declines to touch `Items.cs`, T10 and H4 must be restricted to scripts that hold at most one stack per (definition, quality), and each must assert that restriction.
3. Every raw-`StateDigest` replay claim asserts first that the window mints nothing. This covers N-A8, `CrossingWorkshop_0and11` step 1 and H4's plain run.
   - The check: the number of `itm_` registry entries is the same before and after, and merges only.
   - Otherwise the test compares the replayable dump.
4. Reword E30: "No M7 system mints a wall-clock ID itself. Item IDs are minted only through the existing item commands (refunds, splits, gathers)." Extend G8's comment to say so.
5. H4 and T10 also assert equal `NavCounters` and `BuildingCounters`, and, when the window is mint-free, equal raw `StateDigest`.
   - `StateDump.Order` sorts every array of objects by content (`src/Application/StateDump.cs:101-119`). It therefore erases route-corner and trail order, which F §5.6 already records as a limit.
   - Only the digest checks that order.

### M1. The errand mover is gated on cell tiers, which have hysteresis and are not saved

**Where.** D §1.3 mover ("`if TierOf(npc.Body) != A: continue`"); B §15.3 ("tier A only, as for creatures and companions").

**Evidence.**
- `TierRules.Next` has hysteresis (`src/Domain/Spatial/Tiers.cs:27-46`). A cell stays A out to 160 m, but a cell reached from farther away becomes A only inside 140 m (150 m radius, 10 m hysteresis; `content/config/simulation_tiers.yaml`).
- Tiers are not saved. On load, `Settle` recomputes them from D in three steps (`src/World/Runtime/Systems.cs:470-480`, called at `Simulation.cs:144`).
- So for a cell 140-160 m away, the continuing world holds A and the loaded world settles to B.
- In Ashen Hollow (bounds 0-200 m, `content/regions/ashen_hollow.yaml:10`), a player at (0.35, 0.35) m is 140.9 m from cell `c_01_01`. That cell holds B's work anchor at (100 750, 103 500).
- Kera walking in `c_01_01` then moves in one world and freezes in the other.
- Companions (`Companions.cs:263`) and creatures (`Creatures.cs:264`) already carry this hazard. M7 adds a third consumer, and at M9's 2 km regions the band is everywhere.

**Fix.**
- In M7, do not gate the errand mover on tiers. There is one NPC in a 200 m region, and tiers B and C are stubs that run nothing (`Systems.cs:521-527`).
- Record in `M7_STATUS` and `RISK_REGISTER` that companions and creatures carry the unsaved-hysteresis hazard, and that it must be fixed before M9. Either persist `CellTiers` as a required list with a "settle" default for schema 13 and older, or give authoritative gates a pure distance rule without hysteresis.
- Add an N-A6 variant: the player at the region's south-west corner, Kera walking in `c_01_01`; save, continue 100 ticks, and assert equal digests.

### M2. The definition pass can build an invalid ledger, and the exception escapes the loader

**Where.** F §7 (the standing row: "points summed, then clamped to [-999, 1000]"; knowledge `via` resolved by calling `content.Resolve` directly; the spill rule); C §14.5.

**Evidence.**
- A standing row may never be 0: C §14.2 validates `Points != 0` in the `PlayerRecord` init, and F §2.1 says "zero is never stored".
- Merging +100 and -100 sums to 0. The resulting `PlayerRecord` construction throws `ArgumentException`.
- `ResolveDefinitions` is called at `src/Persistence/SaveLoader.cs:158-159`. That is outside the `try` at `:147-155`, which maps `ArgumentException` to corruption only for `DecodePlayer`.
- `SaveStore.Load` (`SaveStore.cs:212`) catches no `ArgumentException` (`:124`, `:488`), and `SaveLoader.Plan` catches only `SaveException` (`SaveLoader.cs:199`).
- **Result:** a content update that aliases two factions whose standings cancel crashes both the load and the dry run, instead of reporting.

**Two further gaps in the pass.**
- (a) F's direct `content.Resolve` call for `via` has to reproduce the local `Resolve`'s bookkeeping (`SaveLoader.cs:208-231`).
  - `Renamed` must call `CountAlias` and `Replaced` must call `CountReplacement`. F §9.3's alias expectation "`npc.fixture.warden -> warden_sera` ×4 (… 1 via)" depends on it.
  - The default, unresolved branch must still add a Blocker. Otherwise an unmapped `via` passes silently.
- (b) The spill must use the item definitions already resolved by the container pass (`SaveLoader.cs:271-281`), and must drop items whose definition was discarded, with a Loss line. F does not fix the order of the two passes.

**Fix.**
1. After summing, a zero result drops the row with the Warning "standing with {a} and {b} merged to neutral".
2. Defensively, `SaveLoader.Run` maps an `ArgumentException` thrown from the pass to a Blocker that names the pass.
3. Run the container item pass before the piece spill.
4. Tests:
   - `TwoFactionsMergedWithOppositeStanding_LoadNeutral_WithAWarning`;
   - `AnUnmappedWitness_IsABlocker`;
   - `ASpilledChestItemWithARenamedDefinition_LandsRenamed`.

### M3. `NavRoute` invariants contradict the follower, and an invalid route fails at save time or is dropped silently at load

**Where.** A §11 (`Problem()`: "Active => 1..32 corners"); A §8.2 step 3 ("The last corner is never dropped here"); A §7.6 trigger 7 ("`Corners` empty, `Partial`, not arrived"); F §2.3 and §6.4.

**Problem.** Trigger 7 can never fire, because the follower never empties `Corners`. An implementer who makes it fire by emptying `Corners` creates a route that `Problem()` rejects. Any `route with { Status = None }` that leaves fields non-zero does the same.

**Consequences.**
- **For a companion:** `CaptureRecord()` runs on every `StateDigest` (`src/World/Runtime/Simulation.cs:342-364`) and on every save (`GameSession.cs:158`). F adds route validation to the `PlayerRecord` constructor's companion loop (`src/World/PlayerState.cs:198-205`). The invalid route therefore throws there, and autosave crashes.
- **For an errand:** nothing validates at run time. The save writes the route, the load rejects the row at apply (F §6.4), and Kera reappears at her site. That is a silent save-then-continue divergence.

**Fix.**
1. Trigger 7 becomes: `Partial && Corners.Length == 1 && dist²(body, Corners[0]) ≤ corner_reach²` → replan. `Problem()` is unchanged.
2. A route can be built only through factories that throw when `Problem() != null`: `NavRoute.None`, `NavRoute.Active(goal, corners, tick, stamp, watch, partial)` and `NavRoute.Unreachable(goal, tick, stamp, watch)`. The positional constructor becomes private.
3. `WorldDelta.SetNpcErrand` and `CompanionSystem`'s state writes validate the route, so the failure comes at the mutation in a test, not at save or load.
4. N-D18 gains a partial route walked to its last corner.

### M4. Record equality over `ImmutableArray` members is reference equality

**Where.**
- A §10.5 (`HisState_RoundTrips…` "gains `Assert.Equal(before.Route, after.Route)`").
- N-A6 ("both `NavRoute`s equal field by field").
- B §9.7 ("equal rows", which covers errands carrying a route).
- Any runtime "write when changed" test using `!=` (B §15.3, "written every tick they change").

**Evidence.**
- `NavRoute` is a `sealed record` with `ImmutableArray<NavPoint> Corners` (A §11). The synthesized record `Equals` compares an `ImmutableArray` by its backing array reference.
- xUnit's `Assert.Equal` on a record uses `IEquatable<T>`. A decoded route has a fresh array, so the planned assert fails on equal data.
- M6 avoided this on purpose: it compares `before.Trail` by itself, not whole `CompanionRecord`s (`tests/Application.Tests/CompanionTests.cs:300-303`).
- `FactionLedger` has three such arrays, and `NpcErrandRecord` and `CompanionRecord` carry a `NavRoute`.
- A "fix" that compares references instead would make these tests hollow.

**Fix.**
- Override `Equals(NavRoute?)` and `GetHashCode` on `NavRoute` and `FactionLedger` to compare arrays with `SequenceEqual`. Equality then composes through `NpcErrandRecord`, `CompanionRecord` and `PlayerRecord`. Those are methods, so they do not affect the dump.
- Add the test `NavRoute_AndFactionLedger_EqualByValue_AfterADecode`.

### L1. New commands reuse the transient "dead" and "busy" checks

**Where.** B §3.3 check 1, and the refusals of B §10.1, §13.4 and §15.2.

**Evidence.**
- `RuntimeState.PlayerCombat` starts as `PlayerCombat.Rested` and is not part of `PlayerRecord` (`src/World/Runtime/RuntimeState.cs:128`).
- Crafting's "dead" and "busy" checks read it (`Crafting.cs:158-161`).
- A save made during a swing or while defeated loads as idle, so a place, repair, dismantle, assign or release in the next ticks is accepted in one world and refused in the other.
- This is the pre-existing crafting pattern; M7 adds five commands to it.

**Fix.**
- Building and assignment commands keep "dead", read from persisted pools where possible, and drop the combat-phase "busy" clause. Placement is instant and build mode already suppresses attack (GH H.2).
- Record `PlayerCombat` in `M7_STATUS` as a known unsaved input.

### L2. M7 adds float and trigonometric consequences to persisted state, contrary to D §6.2

**Where.** D §6.2 ("M7 adds none"); guard G16 and N-X1, which scan only `Nav*.cs`.

**Evidence.** Three new consequences reach persisted state:
- K2 decides knowledge and standing through `Perception.Sees` (`Math.Sqrt`, `Sin`, `Cos`; `Perception.cs:88-106`).
- B §13.2's `FirstStop` decides, through the existing `Sin`/`Cos` bisection, which piece loses persisted health.
- The errand's facing comes from `CombatRules.FacingTowards` (`Atan2`) and is persisted and hashed in the effective-cell digest v2.

**Fix.**
- Preferred: an integer witness cone in Domain, `WitnessRules.InCone`. Compare squared distances in mm, and test the cone with a dot product in `Int128` against a precomputed integer cos² threshold for the half angle.
- In every case, correct D §6.2 and record `FirstStop` and `FacingTowards` in `M7_STATUS` as floating-point paths that are deterministic only on one machine.
- Evidence replays (`--playthrough`, `--build-shots`) are recorded and verified on the same operating system.

### L3. Sorted collections in the new code must name their comparers

**Where.** C §15 (`FactionSetup(ImmutableSortedDictionary<string, FactionDefinition> Factions, …)`, no comparer given); A §13.1 (`ImmutableSortedDictionary<NavTileKey, NavTile>`, "ordered (Tz, Tx)").

**Evidence.**
- With no comparer, a `string` key uses `Comparer<string>.Default`. That comparison is culture-sensitive (ICU), and ICU orders `_` and `.` differently from ordinal.
- Content builders always pass `StringComparer.Ordinal` (for example `src/Content/CraftingContent.cs:94`, `ItemContent.cs:40`).
- A record struct without `IComparable` makes a sorted dictionary throw at run time.

**Fix.**
- Use `StringComparer.Ordinal` everywhere, and give `NavTileKey` an `IComparable<NavTileKey>` ordered by (Tz, Tx).
- Add an architecture test over the M7 files that fails on `ToImmutableSortedDictionary(`, `ImmutableSortedDictionary.Create`, `SortedDictionary<string` or `SortedSet<string` with no ordinal comparer.

### L4. Saved fields that duplicate derivable values are not checked for consistency

**Where.** C §3.1 (`ActRecord.CellKey`); B §15.1 and F §2.2 (`NpcErrandDto.work_owner`).

**Problem.**
- `CellKey` is a function of `(XMm, ZMm)`, yet `PlayerRecord` validates only that it parses (C §14.2).
- For `to_work` and `at_work`, `WorkOwner` duplicates the owner on the piece row. `TryApplyNpcErrand` does not compare them (F §6.4), and `CanOperate` reads the errand's copy (B §7.2).
- A disagreement cannot arise in play, but a crafted or buggy save carries it silently, and the digest hashes both copies.

**Fix.**
- `PlayerRecord` requires `CellKey == CellKey.OfWorld(XMm/1000.0, ZMm/1000.0)`.
- `TryApplyNpcErrand` rejects `to_work` or `at_work` rows whose `WorkOwner` differs from the owner of their piece.
- `to_home` keeps its own `WorkOwner`, because its piece may be gone. The ownership-transfer seam (F §6.4) later rewrites both copies in one command.

### L5. A refused `OpenGate` never advances the stuck count, so the mover stalls with no replan and no "Blocked" view

**Where.** D §1.3 (OpenGate arm: "face the door; keep step.Route"); A §8.3 (OpenGate: "no Kinematics step this tick"); D34 ("the mover holds and replans on stuck"); D28 (the stuck count rises only on `Unreachable`).

**Problem.** When the NPC cannot operate a piece door (foreign-owned, D34) or `OpenDoor` is refused, the follower returns `OpenGate` on every tick. `StuckTicks` never rises, so trigger 6 never replans and the "Blocked" view never shows. D34's claim is therefore false. The mover still behaves deterministically; it just never gets out.

**Fix.** When the `OpenDoor` dispatch is refused, `StuckTicks += 1` for both the errand and the companion. Add a test with a foreign door from a crafted save that expects a stuck replan at 20 ticks and "Blocked" at `blocked_view_s`.

### L6. The scratch's generation counter must clear on wrap

**Where.** A §4 and §13.1 ("generation stamps replace clearing"; `int[] stamp`).

**Problem.** On overflow, stale marks can equal the new generation. A search result would then depend on scratch history, which differs between a long session and a freshly loaded one.

**Fix.** When the generation wraps, clear the arrays and restart at 1. Add a test that starts the generation at `int.MaxValue - 1` and checks that the answers are identical.

### L7. The zero-copy grid exposure lets presentation write authoritative bytes

**Where.** A §13.1 (`ImmutableCollectionsMarshal.AsImmutableArray` over a `byte[]`); A §13.2 (`NavigationView.Grid` is a public read).

**Problem.** `ImmutableCollectionsMarshal.AsArray` returns the backing array. The presentation scan (`tests/Architecture.Tests/ArchitectureTests.cs:162-183`) does not forbid it.

**Fix.** Add `ImmutableCollectionsMarshal` to the forbidden presentation tokens. In `src/World` and `src/Domain`, allow it only in `NavTile.cs` and `NavGrid.cs`, and never retain the builder array after publication.

### L8. The contingent per-tick plan budget must hold no state across ticks

**Where.** IJ J.2 ("switch on A §9's per-tick plan budget (deterministic, reset every tick, never saved)").

**Problem.** A round-robin cursor or a partial search carried to the next tick is mover state that the next tick depends on. Left unsaved, it would break save-then-continue.

**Fix.** State the rule in A §9: the budget is spent in the fixed mover order and reset every tick. Any fairness rule is a pure function of the tick, for example its parity, never a cursor. A deferred mover shows as its stale stamp, which is persisted.

### L9. One NPC with both an errand and a companion record gets two movers

**Where.** B §15.1 ("companions never have one"), which is enforced only by assign refusal 3.

**Problem.**
- `Recruit` does not check for errands.
- The two records live in different sections (`entities.msgpack` and `player.msgpack`), so no `TryApply*` can see both.
- `NpcSystem.Populate` places the NPC at its errand pose, then `CompanionSystem.Populate` re-places it. From then on, both systems write the same body every tick.

**Fix.**
- `CompanionSystem.Handle(Recruit)` refuses an NPC that has an errand.
- `NpcSystem.Populate` drops, with a `StructureAudit` line, an errand whose `NpcId` is in the player record's companions.

---

## 3. What was verified sound (do not reopen)

- **Piece IDs** are `Derived(Piece, StructureSequence + 1, tag, owner)`.
  - `StructureSequence` is persisted, strictly increases, and is hashed in simulation v2. No ordinal is ever reused, so a tombstone (`EntityRegistry.cs:133-142`) can never block a later ID.
  - The replay test starts both sessions from one save (`tests/Application.Tests/DeterminismAndViewTests.cs:48-77`), so the owner-salted IDs coincide.
  - B §14.1's C1 clause removes the one path where a derived `cnt_` could be re-registered only in the unsaved run.
- **Acts** use the persisted `NextActSeq`. `Learn`'s clamp is integer arithmetic. Eviction is by lowest `Seq`.
- **The grid** is a `min` over inputs, so it is order-free. The tile stamp hashes geometry and never an instance ID (A §11). A toggle never rebuilds (G14).
- **The route stamp must be saved, as F plans.** A companion who is fighting or talking keeps an Active route without evaluating it (`Companions.cs:286-290`). If geometry changes meanwhile, the saved stamp is stale. A stamp recomputed on load would hide that, and the loaded world would not replan while the continuing world does.
- **Creature homes.** `CreatureSystem.Populate` samples homes against `Setup.Layout.Space` and other creatures' homes only (`src/World/Runtime/Creatures.cs:173-196`). D2 and G9 therefore hold.
- **Errand poses save x and z only.** `Kinematics.Step` sets Y to the terrain height at the quantised position (`src/Domain/Spatial/Kinematics.cs:155-158`), so saving only x and z is exact.
- **Iteration order.**
  - `State.Npcs`, `Creatures` and `Companions` are ordinal `ImmutableSortedDictionary`s (`RuntimeState.cs:130-141`).
  - `WorldDelta` containers and creatures are ordinal `SortedDictionary`s, and created instances are sorted on read (`src/World/WorldDelta.cs:222-223`, `:437-441`).
  - B's new stores are `SortedDictionary`, and `EntityId` compares ordinally (`EntityId.cs:129-130`).
  - Systems never subscribe to events (G5).
- **Preview isolation.** D26's preview scratch, with no counter sink and the same `Validate`, together with G7's scan, is sufficient, provided H2's fix 5 adds counter equality to H4.
- **Baseline transition.**
  - Fixed-node keys use the node's name (`src/World/Generation.cs:253-254`), so adding the deadfall shifts no key.
  - Cell A (`c_00_01`) holds no existing node; the seam is in `c_00_00` and the ash stand in `c_01_01` (`ashen_hollow.yaml:158-159`).
  - The M7 transition therefore vanishes nothing, and `DropVanishedTargets: false` is correct.
  - The frozen-fingerprint test also catches any other change to generator input.

## 4. State walk (every new or newly read mutable state)

| State | Owner | Saved / derived | Digest | Verdict |
|---|---|---|---|---|
| `PieceRecord` rows (pose, owner, health, `door_open`) | Building | saved, `entities` | effective-cell v2 | OK |
| `StructureSequence` | Building | saved | simulation v2 | OK |
| `Space`, closed piece leaves, socket index, footprints | Building | derived in `StructureOrder` | - | OK (D3, G10) |
| `StructureAudit` | Building, NPCs | derived report | - | OK |
| Piece-chest `ContainerRecord` | Inventory | saved (schema 6) | effective-cell v1 terms | OK, plus F §5.6 masking |
| `pce_` and `cnt_` registry entries and tombstones | WorldDelta | not saved | - | OK: no ordinal reuse; C1 clause |
| `NavGrid`, tiles, tile stamps | Navigation | derived | grid digest (tests) | OK |
| Authoritative and preview scratch | Navigation, Simulation | transient | - | OK, plus L6 |
| Nav, building and faction counters | per system | transient views | - | OK (never read); H2 fix 5 |
| `CompanionRecord.Route` | Companions | saved, `player` | player v10 | OK, plus M3, M4 |
| `NpcErrandRecord` (phase, piece, owner, pose, route, stuck) | NPCs | saved, `entities` | effective-cell v2 | OK, plus M1, M3, L4, L9 |
| An errand NPC's `Npcs` body | NPCs | mirror of the errand pose (G18) | via the errand | OK |
| **Facing of NPCs not on an errand** | NPCs | **transient** | none | **H1: witness must not read it** |
| **The open conversation** | Dialogue | **transient** (`Social.cs:90-91`) | none | **H1** |
| **Cell tiers** | Tiers | **transient, with hysteresis** | none | **M1** |
| **`PlayerCombat` (defeated, phase)** | Combat | **transient** | none | **L1** |
| `FactionLedger` (seq, acts, knowledge, standing) | Factions | saved, `player` | player v10 | OK, plus M2 |
| Faction tiers, relevance, members, relations | content | derived | - | OK |
| Item IDs minted by refunds, splits and gathers | Inventory | saved, wall-clock | player v9/v10, cell | **H2** |
| Creature homes | Creatures | derived from `Layout.Space` | - | OK (G9) |

## 5. Decision walk (every new decision point and what it reads)

| Decision | Inputs | Verdict |
|---|---|---|
| Placement checks 1-13 | player body, pieces, content, bodies, terrain (all integer) | OK except check 1 (L1) |
| Check 14 (materials) | inventory ordered by `ItemId` | H2 fix 1 |
| Check 15 and `CheckEdit` V-N1..V-N4 | grid, all gates passable (D §1.2), protected points from saved or derived positions | OK |
| Follower triggers 1-8 | route fields, stuck count, body, grid, barrier flags, tick | OK, plus M3 (trigger 7) |
| Tier gate on the errand mover | tiers | M1 |
| D27 conversation hold | open conversation | H1 |
| Door choice | squared distance, then `MinX`, `MinZ` (geometric) | OK |
| `OpenDoor`, `OperatePieceDoor`, `CanOperate` | errand `WorkOwner`, companions, reach | OK, plus L4, L5 |
| Close refusal over every body | bodies | OK |
| Witness K2-K4 | NPC positions, **facing**, `SightWalls`, floating point | H1, L2 |
| K5 report, `Learn`, eviction | ledger, content | OK |
| Reputation and billet gates | ledger, ladder | OK |
| `FirstStop` damage, cascade | existing float trace, piece-ID order | OK (L2 recorded) |
| Assign refusal 10 (`Reachable`) | grid, flags, bodies (counts authoritatively) | OK |
| Errand arrival and retirement | exact integer pose and facing | OK |

## 6. The schema-14 plan (F), checked

- **Bump and shape.** One step, 13 → 14, with no new section file. This is verified necessary: required-on-decode needs a version boundary, and a new file breaks the integrity root.
- **Required from 14.** `factions`, companion `route`, `pieces`, `structure_seq`, `npc_errands` and errand `route`. The defaults written for schemas 1-13 are correct and inline.
- **Frozen shapes and repoints.** Only `Migrations.cs:566` and `:722` write current DTOs. `V12.Player.Companions` (`Sections/SchemaV12.cs:32`) and `SchemaV11ToV12`'s `Array.Empty<CompanionDto>()` (`Migrations.cs:701`) must follow `V13.Companion`. F is complete here; D14 missed `:701`.
- **Hand-listed copies.** The four `new DeltaSnapshot(` sites are confirmed: `SaveLoader.cs:146`, `:358`; `BaselineTransitions.cs:112`; `WorldDelta.cs:520`. The seven `With*` methods hand-list `{ Posture = Posture }` (`PlayerState.cs:222-253`). F's guards E3 and E4 are right. E3 must compare element-wise (M4).
- **Digests.** `unnamed.player/v10`, `unnamed.effective-cell/v2` and `unnamed.simulation/v2` cover every new field. No digest is persisted.
- **`CanonicalState`** is written by hand; F lists every field.
- **Definition-ID pass.** Correct in coverage. Add M2's zero-merge rule, keep the Blocker branch for `via`, and fix the order of the spill and the container pass.
- **Quarantine.** An entities quarantine loses pieces, sequence, errands and chests together. That is safe: nothing in the player section names them, a stale route stamp only causes a replan, and the sequence restarting at 0 cannot collide in a fresh registry.
- **Baseline transition** and frozen M6 fingerprint: sound (§3). The `SaveTool` limit is recorded.
- **Fixture.** Every v14 record lies in `M2Fixtures.TenCells` (`tests/M2.Probe/M2Fixtures.cs:37-38`), so `CellsMatched` stays 10.
